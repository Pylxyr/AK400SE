using DeepCoolLite;
using System.Xml.Linq;

// Tests for the logic that decides what may be written to the cooler, what is shown, and how settings and the
// start-up task are stored. Pure code only. Usage: dotnet run --project tests/LogicTests -c Release
int checks = 0, failed = 0;
void Check(bool ok, string what) { checks++; if (!ok) { failed++; Console.WriteLine("FAIL: " + what); } }

// ---------- DeviceCheck ----------
IReadOnlySet<byte> Ids(params byte[] b) => new SortedSet<byte>(b);
HidCandidate Cand(string? name, int vid = 0x3633, int pid = 0x0001, IReadOnlySet<byte>? ids = null, int len = 65) =>
    new(vid, pid, name, ids ?? Ids(0x10), len);

var se = DeviceCheck.Scan(new[] { Cand("A400-DIGITAL") });
Check(se.Presence == Presence.Supported && se.Index == 0 && se.ReportId == 0x10, "SE with report ID 0x10 is supported (not rejected for the ID)");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", ids: Ids(0x00)) }).ReportId == 0x00, "SE with unnumbered output uses 0");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", ids: Ids(0x02, 0x10)) }).ReportId == 0x10, "0x10 preferred when declared with others");

var reg = DeviceCheck.Scan(new[] { Cand("AK400-DIGITAL") });
Check(reg.Presence == Presence.Unsupported && reg.ProductName == "AK400-DIGITAL" && reg.Index == -1, "regular AK400 DIGITAL: unsupported, name reported, nothing selected");
Check(DeviceCheck.Scan(new[] { Cand("a400-digital") }).Presence == Presence.Unsupported, "product name is compared exactly (case)");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL2") }).Presence == Presence.Unsupported, "prefix match is not enough");
Check(DeviceCheck.Scan(new[] { Cand(null) }).Presence == Presence.Unsupported, "missing product name: unsupported");
Check(DeviceCheck.Scan(new[] { Cand("") }).ProductName == "", "empty product name is reported as empty");
Check(DeviceCheck.Scan(Array.Empty<HidCandidate>()).Presence == Presence.NotFound, "nothing plugged in: not found");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", vid: 0x1234) }).Presence == Presence.NotFound, "wrong VID is never considered, even with the right name");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", pid: 0x0002) }).Presence == Presence.NotFound, "wrong PID is never considered, even with the right name");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", ids: Ids()) }).Presence == Presence.Unsupported, "no output report in descriptor: nothing may be written");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", ids: Ids(0x02, 0x05)) }).Presence == Presence.Unsupported, "only unrelated report IDs declared: refused");
Check(DeviceCheck.Scan(new[] { new HidCandidate(0x3633, 1, "A400-DIGITAL", null, 65) }).Presence == Presence.Unsupported, "unreadable descriptor: refused (no guessing)");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", len: 0) }).Presence == Presence.Unsupported, "zero output length: refused");
Check(DeviceCheck.Scan(new[] { Cand("A400-DIGITAL", len: 5) }).Presence == Presence.Unsupported, "output report too short for the frame: refused");
var mixed = DeviceCheck.Scan(new[] { Cand("Something else", vid: 0x046D, pid: 0xC52B), Cand("AK400-DIGITAL"), Cand("A400-DIGITAL") });
Check(mixed.Presence == Presence.Supported && mixed.Index == 2, "the SE is picked out of several devices; others are skipped");
Check(DeviceCheck.CleanName("A400-DIGITAL\0\0 ") == "A400-DIGITAL", "trailing NULs and spaces are removed");
Check(DeviceCheck.CleanName("x\u0007y\n").Equals("xy"), "control characters are removed from shown names");
Check(DeviceCheck.CleanName(new string('A', 200)).Length == 64, "shown names are capped at 64 characters");
Check(DeviceCheck.ChooseReportId(Ids(0x10, 0x00)) == 0x10 && DeviceCheck.ChooseReportId(null) is null, "report ID choice");

// ---------- WriteGuard ----------
var g = new WriteGuard();
Check(g.TryPass(1000), "first write passes");
Check(!g.TryPass(1000) && !g.TryPass(1499), "writes closer than 500 ms are refused");
Check(g.TryPass(1500), "write after 500 ms passes");
Check(!g.TryPass(1600), "interval counts from the last allowed write");
Check(!g.TryPass(100) && !g.TryPass(300), "clock going backwards is refused, not trusted");
Check(g.TryPass(700), "and writing resumes once the interval has passed from the new time");

// ---------- ModeScheduler ----------
Check(ModeScheduler.Pick(Mode.Temp, 0, 5, true, true) == Shown.Temperature, "temp mode");
Check(ModeScheduler.Pick(Mode.Usage, 0, 5, true, true) == Shown.Usage, "usage mode");
Check(ModeScheduler.Pick(Mode.Temp, 0, 5, false, true) == Shown.Nothing, "temp mode without a temperature shows nothing (never 0)");
Check(ModeScheduler.Pick(Mode.Usage, 0, 5, true, false) == Shown.Nothing, "usage mode without usage shows nothing");
Check(ModeScheduler.Pick(Mode.Auto, 0, 5, true, true) == Shown.Temperature, "auto starts with the temperature");
Check(ModeScheduler.Pick(Mode.Auto, 4999, 5, true, true) == Shown.Temperature, "auto: still temperature just before the switch");
Check(ModeScheduler.Pick(Mode.Auto, 5000, 5, true, true) == Shown.Usage, "auto: usage after 5 s");
Check(ModeScheduler.Pick(Mode.Auto, 9999, 5, true, true) == Shown.Usage, "auto: still usage");
Check(ModeScheduler.Pick(Mode.Auto, 10000, 5, true, true) == Shown.Temperature, "auto: back to temperature after 10 s");
Check(ModeScheduler.Pick(Mode.Auto, 7000, 7, true, true) == Shown.Usage && ModeScheduler.Pick(Mode.Auto, 6999, 7, true, true) == Shown.Temperature, "auto follows the chosen interval");
Check(ModeScheduler.Pick(Mode.Auto, 6000, 5, false, true) == Shown.Usage, "auto without a temperature shows usage only");
Check(ModeScheduler.Pick(Mode.Auto, 0, 5, true, false) == Shown.Temperature, "auto without usage shows temperature only");
Check(ModeScheduler.Pick(Mode.Auto, 0, 5, false, false) == Shown.Nothing, "auto with nothing shows nothing");
Check(ModeScheduler.Pick(Mode.Auto, -50, 5, true, true) == Shown.Temperature, "negative elapsed time is treated as 0");
Check(ModeScheduler.Pick(Mode.Auto, 6000, 0, true, true) == Shown.Usage, "interval 0 falls back to the default (5 s)");
Check(ModeScheduler.Pick((Mode)99, 0, 5, true, true) == Shown.Nothing, "unknown mode shows nothing");

// ---------- Settings ----------
Check(Settings.Default == new Settings(Mode.Auto, 5), "defaults: both alternating, 5 s");
Check(SettingsFile.Parse(null) == Settings.Default && SettingsFile.Parse("") == Settings.Default && SettingsFile.Parse("   ") == Settings.Default, "empty input: defaults");
Check(SettingsFile.Parse("not json at all") == Settings.Default, "garbage: defaults");
Check(SettingsFile.Parse("[1,2,3]") == Settings.Default && SettingsFile.Parse("42") == Settings.Default && SettingsFile.Parse("\"auto\"") == Settings.Default, "non-object JSON: defaults");
Check(SettingsFile.Parse("{\"mode\":\"temp\",\"switchSeconds\":7}") == new Settings(Mode.Temp, 7), "temp, 7");
Check(SettingsFile.Parse("{\"mode\":\"USAGE\",\"switchSeconds\":3}") == new Settings(Mode.Usage, 3), "mode is case-insensitive");
Check(SettingsFile.Parse("{\"mode\":\" auto \"}") == new Settings(Mode.Auto, 5), "missing interval: default; mode is trimmed");
Check(SettingsFile.Parse("{\"mode\":\"banana\",\"switchSeconds\":10}") == new Settings(Mode.Auto, 10), "unknown mode: default mode, interval kept");
Check(SettingsFile.Parse("{\"mode\":5,\"switchSeconds\":\"7\"}") == Settings.Default, "wrong types: defaults");
Check(SettingsFile.Parse("{\"switchSeconds\":6}").SwitchSeconds is 5 or 7, "6 s snaps to a listed choice");
Check(SettingsFile.Parse("{\"switchSeconds\":1}").SwitchSeconds == 3, "too small snaps to the smallest choice");
Check(SettingsFile.Parse("{\"switchSeconds\":99999999999}").SwitchSeconds == 10, "huge number snaps to the largest choice");
Check(SettingsFile.Parse("{\"switchSeconds\":-4}").SwitchSeconds == 3, "negative snaps to the smallest choice");
Check(SettingsFile.Parse("{\"switchSeconds\":5.4}").SwitchSeconds == 5, "fractions are rounded then snapped");
Check(SettingsFile.Parse("{\"mode\":\"temp\",}") .Mode == Mode.Temp, "trailing comma tolerated");
Check(SettingsFile.Parse("{\"mode\":\"temp\",\"extra\":{\"a\":[1,2]}}").Mode == Mode.Temp, "unknown properties are ignored");
Check(new Settings((Mode)42, 1234).Normalized() == new Settings(Mode.Auto, 10), "Normalized fixes invalid values");
foreach (var m in new[] { Mode.Temp, Mode.Usage, Mode.Auto })
    foreach (int sec in Settings.SwitchChoices)
        Check(SettingsFile.Parse(SettingsFile.ToJson(new Settings(m, sec))) == new Settings(m, sec), $"round trip {m}/{sec}");
Check(SettingsFile.ToJson(Settings.Default).Length < 100, "the file is small");

string dir = Path.Combine(Path.GetTempPath(), "dcl-tests-" + Guid.NewGuid().ToString("N"));
try
{
    string path = Path.Combine(dir, "sub", "settings.json");
    Check(SettingsFile.Load(path) == Settings.Default, "missing file: defaults");
    Check(SettingsFile.TrySave(path, new Settings(Mode.Usage, 7)), "save creates the folder and the file");
    Check(SettingsFile.Load(path) == new Settings(Mode.Usage, 7), "load returns what was saved");
    Check(!File.Exists(path + ".tmp"), "no temporary file is left behind");
    Check(SettingsFile.TrySave(path, new Settings(Mode.Temp, 3)) && SettingsFile.Load(path) == new Settings(Mode.Temp, 3), "saving again overwrites");
    File.WriteAllText(path, new string(' ', SettingsFile.MaxBytes + 1) + "{\"mode\":\"temp\"}");
    Check(SettingsFile.Load(path) == Settings.Default, "oversized file is ignored");
    File.WriteAllText(path, "{ broken");
    Check(SettingsFile.Load(path) == Settings.Default, "corrupt file: defaults, no exception");
    Check(!SettingsFile.TrySave(Path.Combine(path, "x", "y.json"), Settings.Default), "a path that cannot be written returns false instead of throwing");
}
finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

// ---------- TaskXml ----------
const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
const string Exe = @"C:\Program Files\DeepCool Lite\deepcool-lite.exe";
XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
var on = XDocument.Parse(TaskXml.Build(Sid, Exe, true));
var off = XDocument.Parse(TaskXml.Build(Sid, Exe, false));
Check(on.Descendants(ns + "ExecutionTimeLimit").Single().Value == "PT0S", "task has no execution time limit");
Check(on.Descendants(ns + "RunLevel").Single().Value == "HighestAvailable", "task runs with highest privileges");
Check(on.Descendants(ns + "LogonType").Single().Value == "InteractiveToken", "task runs in the user's own session");
Check(on.Descendants(ns + "MultipleInstancesPolicy").Single().Value == "IgnoreNew", "a second start is ignored");
Check(on.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value == "false" && on.Descendants(ns + "StopIfGoingOnBatteries").Single().Value == "false", "runs on battery");
Check(on.Descendants(ns + "Command").Single().Value == "\"" + Exe + "\"", "command is the quoted exe path");
Check(on.Descendants(ns + "Arguments").Single().Value == "--from-task", "task passes the marker argument");
Check(on.Descendants(ns + "Principal").Single().Element(ns + "UserId")!.Value == Sid, "principal is the given user");
Check(on.Descendants(ns + "LogonTrigger").Count() == 1 && on.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value == Sid, "logon trigger for that user only");
Check(!off.Descendants(ns + "LogonTrigger").Any() && !off.Descendants(ns + "Triggers").Any(), "without the switch there is no trigger (start on demand only)");
Check(TaskXml.HasEnabledLogonTrigger(TaskXml.Build(Sid, Exe, true)) && !TaskXml.HasEnabledLogonTrigger(TaskXml.Build(Sid, Exe, false)), "trigger detection matches what was built");
Check(!TaskXml.HasEnabledLogonTrigger("<Task><Triggers><LogonTrigger><Enabled>false</Enabled></LogonTrigger></Triggers></Task>"), "a disabled trigger does not count");
Check(TaskXml.HasEnabledLogonTrigger("<Task><Triggers><LogonTrigger/></Triggers></Task>"), "a trigger without Enabled counts as enabled");
Check(!TaskXml.HasEnabledLogonTrigger("") && !TaskXml.HasEnabledLogonTrigger("ERROR: not xml") && !TaskXml.HasEnabledLogonTrigger(null), "empty or invalid output: no trigger");
bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }
Check(Throws(() => TaskXml.Build("not-a-sid", Exe, true)), "invalid SID is rejected");
Check(Throws(() => TaskXml.Build(Sid + "</UserId><x/>", Exe, true)), "SID cannot carry XML");
Check(Throws(() => TaskXml.Build(Sid, "relative\\app.exe", true)) && Throws(() => TaskXml.Build(Sid, "C:\\a\"b\\app.exe", true)), "exe path must be absolute and quote-free");
Check(XDocument.Parse(TaskXml.Build(Sid, @"C:\Program Files\A & B <x>\deepcool-lite.exe", true)).Descendants(ns + "Command").Single().Value == "\"C:\\Program Files\\A & B <x>\\deepcool-lite.exe\"", "XML special characters in the path are escaped");
const string Pf = @"C:\Program Files";
Check(TaskXml.IsSafeInstallLocation(Exe, Pf), "Program Files location is safe");
Check(TaskXml.IsSafeInstallLocation(@"c:\program files\x\a.exe", @"C:\Program Files\"), "comparison ignores case and a trailing backslash");
Check(!TaskXml.IsSafeInstallLocation(@"C:\Users\me\Downloads\deepcool-lite.exe", Pf), "user folders are not safe");
Check(!TaskXml.IsSafeInstallLocation(@"C:\Program Files\..\Users\me\a.exe", Pf), "'..' is rejected");
Check(!TaskXml.IsSafeInstallLocation(@"C:\Program Files Evil\a.exe", Pf), "a sibling folder with the same prefix is not safe");
Check(!TaskXml.IsSafeInstallLocation(@"\\server\share\a.exe", Pf) && !TaskXml.IsSafeInstallLocation(null, Pf) && !TaskXml.IsSafeInstallLocation(Exe, null) && !TaskXml.IsSafeInstallLocation(Exe, "C:"), "network paths and missing values are not safe");

// ---------- StatusText ----------
var st = new EngineStatus(CoolerState.NotFound, "", TempState.Unknown, false);
Check(StatusText.Cooler(st) == "Cooler not found", "status: not found");
Check(StatusText.Cooler(st with { Cooler = CoolerState.Unsupported, Product = "AK400-DIGITAL" }) == "Unsupported device: AK400-DIGITAL", "status: unsupported shows the product string");
Check(StatusText.Cooler(st with { Cooler = CoolerState.Unsupported, Product = "" }) == "Unsupported device: (unknown)", "status: unsupported without a name");
Check(StatusText.Cooler(st with { Cooler = CoolerState.Connected }) == "Cooler connected", "status: connected");
Check(StatusText.Temperature(st with { Temp = TempState.DriverMissing })!.Contains("PawnIO"), "missing PawnIO is stated, not shown as 0");
Check(StatusText.Temperature(st) is null, "unknown temperature state has no line");
Check(StatusText.Tray(st with { Cooler = CoolerState.Unsupported, Product = new string('x', 64) }).Length <= 127, "tray text fits the 127 character limit");
Check(StatusText.Tray(st with { Cooler = CoolerState.Connected, Temp = TempState.DriverMissing }).Contains("no CPU temperature"), "tray mentions a missing temperature");

// ---------- Engine with fakes ----------
long now = 0;
long Clock() => now;

int sensorOpens = 0;
Engine Make(FakeFinder f, FakeSensors? s, bool driver = true, Settings? set = null)
{
    sensorOpens = 0;
    return new(f, () => { sensorOpens++; return s; }, () => driver, Clock, set ?? new Settings(Mode.Auto, 5));
}
void At(Engine e, long t) { now = t; e.Tick(); }

{   // cooler missing: nothing is written, retries are quiet
    var f = new FakeFinder(new ConnectOutcome(CoolerState.NotFound, "", null));
    using var e = Make(f, new FakeSensors());
    for (long t = 0; t <= 4000; t += 1000) At(e, t);
    Check(f.Calls == 1, "not found: one search in the first 5 s, not one per tick");
    At(e, 5000);
    Check(f.Calls == 2, "and again after 5 s");
    Check(e.Status.Cooler == CoolerState.NotFound, "status says cooler not found");
}
{   // unsupported device: no link, status shows the product
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Unsupported, "AK400-DIGITAL", null));
    using var e = Make(f, new FakeSensors());
    At(e, 0);
    Check(e.Status.Cooler == CoolerState.Unsupported && e.Status.Product == "AK400-DIGITAL", "unsupported device is reported");
    Check(sensorOpens == 0 && f.Links.Count == 0, "no link exists, so nothing can be written");
}
{   // finder that throws
    var f = new FakeFinder(null) { Throw = true };
    using var e = Make(f, new FakeSensors());
    At(e, 0);
    Check(e.Status.Cooler == CoolerState.NotFound, "an exception while searching is treated as not found");
}
{   // connected: alternates, bar follows usage, one write per tick, first frame on the tick after connecting
    var link = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    var sensors = new FakeSensors { Temp = 61, Load = 23 };
    using var e = Make(f, sensors);
    var seen = new List<EngineStatus>();
    e.StatusChanged += seen.Add;
    At(e, 0);
    Check(link.Shows.Count == 0 && e.Status.Cooler == CoolerState.Connected, "connecting writes no frame by itself");
    At(e, 1000); At(e, 2000); At(e, 3000); At(e, 4000);
    Check(link.Shows.Count == 4 && link.Shows.All(s => !s.Usage && s.Temp == 61 && s.Load == 23), "first 5 s: temperature frames");
    At(e, 5000); At(e, 6000);
    Check(link.Shows.Skip(4).All(s => s.Usage), "after 5 s: usage frames");
    At(e, 10000);
    Check(!link.Shows[^1].Usage, "after 10 s: temperature again");
    Check(e.Status.Temp == TempState.Ok && seen.Count >= 1 && seen[^1] == e.Status, "status events fire on change and match Status");
    int n = seen.Count; At(e, 11000); Check(seen.Count == n, "no event when nothing changed");

    e.Settings = new Settings(Mode.Usage, 5);
    At(e, 12000);
    Check(link.Shows[^1].Usage, "changing the mode applies on the next tick");
    e.Settings = new Settings((Mode)77, 9999);
    Check(e.Settings == new Settings(Mode.Auto, 10), "the engine normalizes settings it is given");
    e.Settings = new Settings(Mode.Auto, 3);
    At(e, 13000); At(e, 14000);
    Check(!link.Shows[^1].Usage, "'both' restarts with the temperature after a change");
    At(e, 16000);
    Check(link.Shows[^1].Usage, "and switches after the new interval (3 s)");
}
{   // no temperature (driver missing): never a fake 0
    var link = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    var sensors = new FakeSensors { Temp = 0, TempOk = false, Load = 40 };
    using var e = Make(f, sensors, driver: false, set: new Settings(Mode.Temp, 5));
    At(e, 0); At(e, 1000); At(e, 2000);
    Check(link.Shows.Count == 0, "temperature mode without a temperature writes nothing");
    Check(e.Status.Temp == TempState.DriverMissing, "status says the PawnIO driver is missing");
    e.Settings = new Settings(Mode.Auto, 5);
    At(e, 3000); At(e, 9000);
    Check(link.Shows.Count == 2 && link.Shows.All(s => s.Usage && s.Load == 40), "both-mode falls back to usage only");
    e.Settings = new Settings(Mode.Usage, 5);
    At(e, 10000);
    Check(link.Shows[^1].Usage, "usage mode works without the driver");
}
{   // driver installed but no sensor
    var link = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    using var e = Make(f, new FakeSensors { TempOk = false }, driver: true);
    At(e, 0); At(e, 1000);
    Check(e.Status.Temp == TempState.NoSensor, "driver present but no sensor: reported as such");
}
{   // PawnIO gets installed while the app runs: sensors are reopened and the temperature starts working
    var link = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    var broken = new FakeSensors { TempOk = false, Temp = 0 };
    var fixedS = new FakeSensors { Temp = 58, Load = 12 };
    bool driver = false; int opens = 0;
    using var e = new Engine(f, () => { opens++; return opens == 1 ? broken : fixedS; }, () => driver, Clock, new Settings(Mode.Temp, 5));
    At(e, 0); At(e, 1000);
    Check(e.Status.Temp == TempState.DriverMissing && link.Shows.Count == 0 && opens == 1, "before the driver exists: reported, nothing written");
    driver = true;
    At(e, 20000);
    Check(opens == 1 && e.Status.Temp == TempState.DriverMissing, "the driver is looked for only every 30 s");
    At(e, 31000);
    Check(opens == 2 && broken.Disposed, "once the driver shows up the sensors are reopened (and the old ones closed)");
    At(e, 32000);
    Check(e.Status.Temp == TempState.Ok && link.Shows.Count == 1 && link.Shows[0].Temp == 58, "and the temperature is shown without restarting");
}
{   // sensors cannot be opened at all
    var link = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    using var e = Make(f, null, driver: true);
    At(e, 0); At(e, 1000); At(e, 2000);
    Check(link.Shows.Count == 0 && e.Status.Temp == TempState.NoSensor, "no sensors: nothing written, reported");
}
{   // sensor read throws
    var link = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    var sensors = new FakeSensors { ThrowOnRead = true };
    using var e = Make(f, sensors);
    At(e, 0); At(e, 1000); At(e, 2000);
    Check(link.Shows.Count == 0, "a failing sensor read writes nothing and does not throw");
}
{   // lost connection: link disposed, reconnect after 2 s, then 5 s search cadence
    var link1 = new FakeLink { FailOnShow = true };
    var link2 = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link1));
    using var e = Make(f, new FakeSensors());
    At(e, 0); At(e, 1000);
    Check(link1.Disposed && e.Status.Cooler == CoolerState.NotFound, "a failed write drops the connection and says so");
    f.Next = new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link2);
    At(e, 2000);
    Check(f.Calls == 1, "no reconnect attempt before the 2 s pause is over");
    At(e, 3000);
    Check(f.Calls == 2 && e.Status.Cooler == CoolerState.Connected, "reconnects after the pause");
    At(e, 4000);
    Check(link2.Shows.Count == 1, "writing continues on the new link");
    Check(sensorOpens == 1, "the CPU sensors are opened once, not on every reconnect");
}
{   // engine disposes what it holds
    var link = new FakeLink();
    var sensors = new FakeSensors();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    var e = Make(f, sensors);
    At(e, 0); e.Dispose();
    Check(link.Disposed && sensors.Disposed, "Dispose closes the cooler and the sensors");
}
{   // write rate: at one tick per second the engine never asks for more than one frame per tick
    var link = new FakeLink();
    var f = new FakeFinder(new ConnectOutcome(CoolerState.Connected, "A400-DIGITAL", link));
    using var e = Make(f, new FakeSensors());
    for (long t = 0; t <= 20000; t += 1000) At(e, t);
    Check(link.Shows.Count == 20, "20 ticks after connecting produce 20 frames");
}

// ---------- the packet builder still clamps what the engine hands it (see also PacketTests) ----------
var fr = Packet.Frame(false, 5000, 400);
Check(fr[2] == 9 && fr[3] == 9 && fr[4] == 9 && fr[1] == 10, "frames are clamped: 999 deg, 100 %");

Console.WriteLine(failed == 0 ? $"LOGIC TESTS: PASS ({checks} checks)" : $"LOGIC TESTS: FAIL ({failed} of {checks})");
return failed == 0 ? 0 : 1;

// ---------- fakes ----------
sealed class FakeLink : ICoolerLink
{
    public record Frame(bool Usage, int Temp, int Load);
    public List<Frame> Shows = new();
    public bool FailOnShow, Disposed;
    public void Show(bool showUsage, int tempC, int usagePct)
    {
        if (FailOnShow) throw new IOException("unplugged");
        Shows.Add(new Frame(showUsage, tempC, usagePct));
    }
    public void Dispose() => Disposed = true;
}

sealed class FakeFinder : ICoolerFinder
{
    public ConnectOutcome? Next;
    public bool Throw;
    public int Calls;
    public List<FakeLink> Links = new();
    public FakeFinder(ConnectOutcome? first) { Next = first; }
    public ConnectOutcome TryConnect()
    {
        Calls++;
        if (Throw) throw new InvalidOperationException("boom");
        if (Next?.Link is FakeLink l) Links.Add(l);
        return Next!;
    }
}

sealed class FakeSensors : ISensorReader
{
    public int Temp = 50, Load = 10;
    public bool TempOk = true, LoadOk = true, ThrowOnRead, Disposed;
    public bool TempAvailable => TempOk;
    public bool LoadAvailable => LoadOk;
    public (int TempC, int LoadPct) Read()
    {
        if (ThrowOnRead) throw new InvalidOperationException("sensor");
        return (Temp, Load);
    }
    public void Dispose() => Disposed = true;
}
