using DeepCoolLite;
using System.Text;

// Dumps every frame for temp 0..255 x usage 0..100 x {temp,usage} mode, as hex, for diffing against ref_se.py.
// Usage: dotnet run --project tests/PacketTests -c Release -- <output file>
// Lines end in \n on every OS so the dump is byte-comparable with ref_se.py's.
string outFile = args.Length > 0 ? args[0] : "packets-cs.txt";
var sb = new StringBuilder();
for (int showUsage = 0; showUsage <= 1; showUsage++)
for (int t = 0; t <= 255; t++)
for (int u = 0; u <= 100; u++)
    sb.Append(showUsage).Append(' ').Append(t).Append(' ').Append(u).Append(' ')
      .Append(Convert.ToHexString(Packet.Frame(showUsage == 1, t, u))).Append('\n');
sb.Append("init ").Append(Convert.ToHexString(Packet.Init())).Append('\n');
File.WriteAllText(outFile, sb.ToString());

// Spot checks that do not depend on the Python port
void Check(bool ok, string what) { if (!ok) { Console.WriteLine("FAIL: " + what); Environment.Exit(1); } }
var f = Packet.Frame(false, 45, 7);
Check(f.Length == 64, "length");
Check(f[0] == 19 && f[1] == 1 && f[2] == 0 && f[3] == 4 && f[4] == 5 && f[5] == 0, "45C @7% -> 19,1,0,4,5,0");
f = Packet.Frame(true, 45, 100);
Check(f[0] == 76 && f[1] == 10 && f[2] == 1 && f[3] == 0 && f[4] == 0, "100% usage -> 76,10,1,0,0");
f = Packet.Frame(false, 1234, -5);
Check(f[2] == 9 && f[3] == 9 && f[4] == 9 && f[1] == 1, "clamping 999 / 0%");
Check(Packet.Init()[0] == 170 && Packet.Init().Skip(1).All(b => b == 0), "init");
Console.WriteLine("spot checks OK");
