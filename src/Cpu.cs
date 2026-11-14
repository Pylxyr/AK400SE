using LibreHardwareMonitor.Hardware;

namespace DeepCoolLite;

/// <summary>
/// CPU temperature and total load through LibreHardwareMonitorLib. Only the CPU group is enabled: no motherboard / Super I/O,
/// embedded controller, fan controller, GPU, storage, memory, network, battery or PSU access. Reading only.
/// </summary>
sealed class Cpu : ISensorReader
{
    readonly Computer _pc;
    readonly IHardware _cpu;
    readonly ISensor? _temp;
    readonly ISensor? _load;
    float _lastTemp, _lastLoad;   // a sensor can report no value for a tick: keep the last good one, never show a fake 0
    bool _tempOk, _loadOk;

    public bool TempAvailable => _tempOk;
    public bool LoadAvailable => _loadOk;

    Cpu(Computer pc, IHardware cpu, ISensor? temp, ISensor? load)
    {
        _pc = pc; _cpu = cpu; _temp = temp; _load = load;
        Refresh();
    }

    /// <summary>
    /// Opens the CPU. The temperature sensor may be missing (PawnIO driver not installed, or no admin rights): then
    /// <see cref="TempAvailable"/> stays false while the load keeps working. Null only when no CPU is found at all.
    /// </summary>
    public static Cpu? Open()
    {
        var pc = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = false,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,   // Super I/O and embedded controller live here
            IsControllerEnabled = false,    // fan and other controllers
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsBatteryEnabled = false,
            IsPsuEnabled = false,
        };
        try { pc.Open(); }
        catch (Exception) { try { pc.Close(); } catch (Exception) { } return null; }

        var cpu = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is null) { pc.Close(); return null; }
        cpu.Update();

        var temps = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0f).ToList();
        ISensor? temp = null;
        // Intel: "CPU Package"; AMD: "Core (Tctl/Tdie)"; then fallbacks
        foreach (var key in new[] { "Package", "Tctl", "Core Max", "Average" })
            if ((temp = temps.FirstOrDefault(s => s.Name.Contains(key, StringComparison.OrdinalIgnoreCase))) != null) break;
        temp ??= temps.FirstOrDefault();

        var load = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "CPU Total");
        return new Cpu(pc, cpu, temp, load);
    }

    void Refresh()
    {
        if (_temp?.Value is float t && t > 0f) { _lastTemp = t; _tempOk = true; }   // 0 / NaN = driver gave nothing: keep the last good value
        if (_load?.Value is float l && !float.IsNaN(l)) { _lastLoad = l; _loadOk = true; }
    }

    public (int TempC, int LoadPct) Read()
    {
        _cpu.Update();
        Refresh();
        return ((int)Math.Round(_lastTemp), (int)Math.Round(_lastLoad));
    }

    public void Dispose() => _pc.Close();
}
