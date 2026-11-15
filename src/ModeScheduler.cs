namespace AK400SE;

public enum Shown { Nothing, Temperature, Usage }

/// <summary>Decides what the digits show. Pure: no clock, no hardware.</summary>
public static class ModeScheduler
{
    /// <param name="elapsedMs">Time since the current settings were applied; "both" always starts with the temperature.</param>
    public static Shown Pick(Mode mode, long elapsedMs, int switchSeconds, bool tempOk, bool usageOk)
    {
        switch (mode)
        {
            case Mode.Temp: return tempOk ? Shown.Temperature : Shown.Nothing;
            case Mode.Usage: return usageOk ? Shown.Usage : Shown.Nothing;
            case Mode.Auto:
                if (tempOk && !usageOk) return Shown.Temperature;
                if (usageOk && !tempOk) return Shown.Usage;
                if (!tempOk) return Shown.Nothing;
                long period = (switchSeconds > 0 ? switchSeconds : Settings.DefaultSwitchSeconds) * 1000L;
                long phase = Math.Max(0, elapsedMs) / period;
                return phase % 2 == 0 ? Shown.Temperature : Shown.Usage;
            default: return Shown.Nothing;
        }
    }
}
