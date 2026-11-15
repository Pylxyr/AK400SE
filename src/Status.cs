namespace DeepCoolLite;

public enum CoolerState { NotFound, Unsupported, CannotOpen, Connected }
public enum TempState { Unknown, Ok, DriverMissing, NoSensor }

/// <summary>What the window and the tray tooltip report. Plain data, compared by value so changes can be detected.</summary>
public sealed record EngineStatus(CoolerState Cooler, string Product, TempState Temp, bool LoadOk)
{
    public static readonly EngineStatus Start = new(CoolerState.NotFound, "", TempState.Unknown, false);
}

public static class StatusText
{
    public static string Cooler(EngineStatus s) => s.Cooler switch
    {
        CoolerState.Connected => "Cooler connected",
        CoolerState.Unsupported => "Unsupported device: " + (string.IsNullOrEmpty(s.Product) ? "(unknown)" : s.Product),
        CoolerState.CannotOpen => "Cooler found but could not be used (is the official DeepCool software still running?)",
        _ => "Cooler not found",
    };

    /// <summary>Null when there is nothing to say about the temperature yet.</summary>
    public static string? Temperature(EngineStatus s) => s.Temp switch
    {
        TempState.Ok => "CPU temperature: available",
        TempState.DriverMissing => "CPU temperature unavailable: the PawnIO driver is not installed. Once it is installed this updates by itself (restart Windows if it asks).",
        TempState.NoSensor => "CPU temperature unavailable: no readable temperature sensor was found.",
        _ => null,
    };

    /// <summary>Tray tooltip; Windows allows at most 127 characters.</summary>
    public static string Tray(EngineStatus s)
    {
        string t = "DeepCool Lite: " + Cooler(s);
        if (s.Cooler == CoolerState.Connected && s.Temp is TempState.DriverMissing or TempState.NoSensor)
            t += " (no CPU temperature)";
        return t.Length > 120 ? t.Substring(0, 117) + "..." : t;
    }
}
