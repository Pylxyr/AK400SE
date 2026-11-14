using LibreHardwareMonitor.Hardware;

namespace DeepCoolLite;

/// <summary>CPU temperature + total load through LibreHardwareMonitorLib (CPU group only).</summary>
sealed class Cpu : IDisposable
{
    readonly Computer _pc;
    readonly IHardware _cpu;
    readonly ISensor _temp;
    readonly ISensor? _load;
    float _lastTemp, _lastLoad;   // a sensor can report no value for a tick: keep the last good one, never show a fake 0
    public string TempName => _temp.Name;

    Cpu(Computer pc, IHardware cpu, ISensor temp, ISensor? load)
    {
        _pc = pc; _cpu = cpu; _temp = temp; _load = load;
        _lastTemp = temp.Value ?? 0f;
        _lastLoad = load?.Value ?? 0f;
    }

    /// <summary>Returns null when no CPU temperature sensor is readable (not admin / PawnIO driver missing).</summary>
    public static Cpu? Open()
    {
        var pc = new Computer { IsCpuEnabled = true };
        pc.Open();
        var cpu = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is null) { pc.Close(); return null; }
        cpu.Update();

        var temps = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
        ISensor? temp = null;
        // Intel: "CPU Package"; AMD: "Core (Tctl/Tdie)"; then fallbacks
        foreach (var key in new[] { "Package", "Tctl", "Core Max", "Average" })
            if ((temp = temps.FirstOrDefault(s => s.Name.Contains(key, StringComparison.OrdinalIgnoreCase))) != null) break;
        temp ??= temps.FirstOrDefault();
        if (temp is null) { pc.Close(); return null; }

        var load = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "CPU Total");
        return new Cpu(pc, cpu, temp, load);
    }

    public (int TempC, int LoadPct) Read()
    {
        _cpu.Update();
        if (_temp.Value is float t && !float.IsNaN(t)) _lastTemp = t;
        if (_load?.Value is float l && !float.IsNaN(l)) _lastLoad = l;
        return ((int)Math.Round(_lastTemp), (int)Math.Round(_lastLoad));
    }

    public void Dispose() => _pc.Close();
}
