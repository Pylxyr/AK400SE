using DeepCoolLite;

const int AutoSwitchMs = 5000;

Mode mode = Mode.Temp;
int intervalMs = 1000;
string layout = "auto";
bool demo = false;

const string Usage = """
    deepcool-lite [--mode temp|usage|auto] [--interval ms>=200] [--layout auto|se|std] [--demo]
      --mode      temp  : digits = CPU temperature (default)
                  usage : digits = CPU usage %
                  auto  : alternate every 5 s
                  (the 10-segment bar always shows CPU usage)
      --demo      show fixed 88 deg / 50 % without reading sensors (tests the USB path only)
    """;

for (int i = 0; i < args.Length; i++)
{
    string a = args[i], v = i + 1 < args.Length ? args[i + 1] : "";
    Mode? parsed = v.ToLowerInvariant() switch { "temp" => Mode.Temp, "usage" => Mode.Usage, "auto" => Mode.Auto, _ => null };

    if (a == "--mode" && parsed is { } m) { mode = m; i++; }
    else if (a == "--interval" && int.TryParse(v, out int ms) && ms >= 200) { intervalMs = ms; i++; }
    else if (a == "--layout" && v is "auto" or "se" or "std") { layout = v; i++; }
    else if (a == "--demo") demo = true;
    else { Console.WriteLine(Usage); return a is "-h" or "--help" ? 0 : 1; }
}

// Two writers on one cooler fight each other (e.g. the boot task plus a manual --demo run): allow only one.
using var single = TryAcquireSingleInstance();
if (single is null)
{
    Console.Error.WriteLine("deepcool-lite is already running (stop the other copy first, e.g. the DeepCoolLite scheduled task).");
    return 3;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Cpu? cpu = null;
if (!demo)
{
    cpu = Cpu.Open();
    if (cpu is null)
    {
        Console.Error.WriteLine("No readable CPU temperature sensor. Run as Administrator and install the PawnIO driver.");
        return 2;
    }
    Console.WriteLine($"temperature sensor: {cpu.TempName}");
}

Display? disp = null;
bool warned = false;
while (!cts.IsCancellationRequested)
{
    try
    {
        if (disp is null)
        {
            var log = new List<string>();
            disp = Display.TryConnect(layout, log, out var info);
            if (disp is not null)
            {
                Console.WriteLine($"connected: {info}");
                warned = false;
            }
            else if (!warned)
            {
                // print the details once per outage, so a failing unit can be diagnosed from the console output
                Console.Error.WriteLine("cooler (3633:0001) not connected, retrying every tick. Details:");
                foreach (var line in log) Console.Error.WriteLine("  " + line);
                warned = true;
            }
        }

        if (disp is not null)
        {
            var (t, u) = demo ? (88, 50) : cpu!.Read();
            bool showUsage = mode == Mode.Usage || (mode == Mode.Auto && Environment.TickCount64 / AutoSwitchMs % 2 == 1);
            disp.Write(Packet.Frame(showUsage, t, u));
        }
    }
    catch (Exception e) when (e is not OperationCanceledException)
    {
        // Unplug/replug, sleep/resume, a stalled write: drop the handle and reconnect on the next tick.
        // Deliberately broad: this runs unattended at boot, and an uncaught exception would end it for good.
        Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}; will reconnect");
        try { disp?.Dispose(); } catch (Exception) { }
        disp = null;
    }
    cts.Token.WaitHandle.WaitOne(intervalMs);
}

disp?.Dispose();
cpu?.Dispose();
return 0;

// Named mutex in the Global\ namespace so the SYSTEM boot task and an elevated console see each other.
static Mutex? TryAcquireSingleInstance()
{
    try
    {
        var m = new Mutex(true, @"Global\DeepCoolLite", out bool created);
        if (created) return m;
        m.Dispose();
        return null;
    }
    catch (UnauthorizedAccessException) { return null; } // exists, owned by another account
}
