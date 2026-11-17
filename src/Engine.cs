namespace AK400SE;

/// <summary>An open, already verified cooler. Show may throw when the connection is lost.</summary>
public interface ICoolerLink : IDisposable
{
    void Show(bool showUsage, int temp, int usagePct, bool fahrenheit, bool alarm);
}

/// <summary>The latest CPU readings (Celsius), for the live readout in the window. Not shown values: those depend on the unit.</summary>
public sealed record Readings(int TempC, int LoadPct, bool TempOk, bool LoadOk)
{
    public static readonly Readings None = new(0, 0, false, false);
}

public sealed record ConnectOutcome(CoolerState State, string Product, ICoolerLink? Link);

public interface ICoolerFinder
{
    /// <summary>Looks for the cooler, verifies it and opens it. Never opens or writes to anything unverified.</summary>
    ConnectOutcome TryConnect();
}

public interface ISensorReader : IDisposable
{
    bool TempAvailable { get; }
    bool LoadAvailable { get; }
    (int TempC, int LoadPct) Read();
}

/// <summary>
/// One tick per second drives everything: find the cooler, read the CPU, decide what to show, write one frame.
/// The only writer to the cooler. Takes interfaces and a clock, so it runs (and is tested) without hardware.
/// </summary>
public sealed class Engine : IDisposable
{
    public const int ReconnectMs = 5000;      // while no cooler is connected, look for it this often (quietly)
    public const int DropRetryMs = 2000;      // after a lost connection
    public const int DriverCheckMs = 30000;

    readonly ICoolerFinder _finder;
    readonly Func<ISensorReader?> _openSensors;
    readonly Func<bool> _driverInstalled;
    readonly Func<long> _clock;

    volatile Settings _settings;
    Settings? _applied;
    long _phaseStart;
    ICoolerLink? _link;
    ISensorReader? _sensors;
    bool _sensorsTried;
    long _nextConnectAt;
    CoolerState _cooler = CoolerState.NotFound;
    string _product = "";
    bool _driverCached = true;
    bool _reopenSensors;
    long _nextDriverCheck;
    volatile EngineStatus _status = EngineStatus.Start;
    volatile Readings _live = Readings.None;

    public event Action<EngineStatus>? StatusChanged;
    public EngineStatus Status => _status;
    /// <summary>Latest readings; polled by the window while it is open (no events, so nothing wakes up when the window is closed).</summary>
    public Readings Live => _live;

    public Settings Settings { get => _settings; set => _settings = value.Normalized(); }

    public Engine(ICoolerFinder finder, Func<ISensorReader?> openSensors, Func<bool> driverInstalled, Func<long> clockMs, Settings initial)
    {
        _finder = finder; _openSensors = openSensors; _driverInstalled = driverInstalled; _clock = clockMs;
        _settings = initial.Normalized();
    }

    public void Tick()
    {
        long now = _clock();
        var s = _settings;
        if (!ReferenceEquals(s, _applied)) { _applied = s; _phaseStart = now; }

        if (_link is null)
        {
            if (now < _nextConnectAt) return;
            ConnectOutcome o;
            try { o = _finder.TryConnect(); }
            catch (Exception) { o = new ConnectOutcome(CoolerState.NotFound, "", null); }
            _nextConnectAt = now + ReconnectMs;
            _live = Readings.None;
            _cooler = o.Link is null ? o.State : CoolerState.Connected;
            _product = o.Product;
            _link = o.Link;
            if (_link is not null && !_sensorsTried) { _sensorsTried = true; _sensors = SafeOpenSensors(); }
            Publish(now, tempOk: false, loadOk: false);
            return;                          // the first frame follows on the next tick
        }

        bool tempOk = false, loadOk = false;
        int temp = 0, load = 0;
        if (_sensors is not null)
        {
            try { (temp, load) = _sensors.Read(); tempOk = _sensors.TempAvailable; loadOk = _sensors.LoadAvailable; }
            catch (Exception) { tempOk = false; loadOk = false; }
        }
        _live = new Readings(temp, load, tempOk, loadOk);
        Publish(now, tempOk, loadOk);
        if (_reopenSensors)
        {
            // the PawnIO driver appeared while we were running: open the CPU sensors again so the temperature starts working
            _reopenSensors = false;
            try { _sensors?.Dispose(); } catch (Exception) { }
            _sensors = SafeOpenSensors();
            return;
        }

        var pick = ModeScheduler.Pick(s.Mode, now - _phaseStart, s.SwitchSeconds, tempOk, loadOk);
        if (pick == Shown.Nothing) return;   // never send a made-up 0

        int shown = s.Fahrenheit ? Packet.ToFahrenheit(temp) : temp;
        bool alarm = s.Alarm && tempOk && temp >= s.AlarmC;   // flashes in every frame while too hot, whichever number is shown
        try { _link.Show(pick == Shown.Usage, shown, load, s.Fahrenheit, alarm); }
        catch (Exception)
        {
            // unplugged, sleep/resume, stalled write: drop the handle and look again shortly
            try { _link.Dispose(); } catch (Exception) { }
            _link = null; _cooler = CoolerState.NotFound; _nextConnectAt = now + DropRetryMs; _live = Readings.None;
            Publish(now, false, false);
        }
    }

    ISensorReader? SafeOpenSensors()
    {
        try { return _openSensors(); } catch (Exception) { return null; }
    }

    void Publish(long now, bool tempOk, bool loadOk)
    {
        TempState t;
        if (tempOk) t = TempState.Ok;
        else
        {
            if (now >= _nextDriverCheck)
            {
                bool was = _driverCached;
                try { _driverCached = _driverInstalled(); } catch (Exception) { _driverCached = true; }
                if (!was && _driverCached && _link is not null) _reopenSensors = true;
                _nextDriverCheck = now + DriverCheckMs;
            }
            t = !_driverCached ? TempState.DriverMissing : _sensorsTried ? TempState.NoSensor : TempState.Unknown;
        }
        var st = new EngineStatus(_cooler, _product, t, loadOk);
        if (st == _status) return;
        _status = st;
        StatusChanged?.Invoke(st);
    }

    public void Dispose()
    {
        try { _link?.Dispose(); } catch (Exception) { }
        try { _sensors?.Dispose(); } catch (Exception) { }
        _link = null; _sensors = null;
    }
}
