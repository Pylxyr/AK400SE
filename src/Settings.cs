using System.Text.Json;

namespace AK400SE;

/// <summary>The user's choices. Everything is clamped to known values: a hand-edited file cannot produce anything else.</summary>
public sealed record Settings(Mode Mode, int SwitchSeconds, bool Fahrenheit = false, bool Alarm = false, int AlarmC = Settings.DefaultAlarmC)
{
    /// <summary>The auto mode of the official software alternates every 5 s (7 s in an early release); offer a few nearby values.</summary>
    public static readonly IReadOnlyList<int> SwitchChoices = new[] { 3, 5, 7, 10 };
    public const int DefaultSwitchSeconds = 5;
    /// <summary>Alarm temperature in Celsius (the official software flashes the display at 90 C / 194 F). Allowed range 75-100 C.</summary>
    public const int DefaultAlarmC = 90, MinAlarmC = 75, MaxAlarmC = 100;
    public static readonly Settings Default = new(Mode.Auto, DefaultSwitchSeconds);

    public Settings Normalized() =>
        new(Enum.IsDefined(Mode) ? Mode : Default.Mode, NearestChoice(SwitchSeconds), Fahrenheit, Alarm, Math.Clamp(AlarmC, MinAlarmC, MaxAlarmC));

    public static int NearestChoice(int seconds)
    {
        int best = DefaultSwitchSeconds;
        long bestDist = long.MaxValue;
        foreach (int c in SwitchChoices)
        {
            long d = Math.Abs((long)c - seconds);
            if (d < bestDist) { best = c; bestDist = d; }
        }
        return best;
    }
}

/// <summary>One small JSON file: {"mode": "temp|usage|auto", "switchSeconds": n, "fahrenheit": true|false, "alarm": true|false, "alarmC": n}. Reading never throws.</summary>
public static class SettingsFile
{
    public const int MaxBytes = 4096;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AK400SE", "settings.json");

    /// <summary>Where versions up to 0.1.x (named "DeepCool Lite") kept their settings.</summary>
    public static string LegacyPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepCoolLite", "settings.json");

    /// <summary>Like <see cref="Load"/>, but when there is no settings file yet, takes over the one from the old name (once: it is copied to the new place).</summary>
    public static Settings LoadMigrating(string path, string legacyPath)
    {
        if (File.Exists(path)) return Load(path);
        if (!File.Exists(legacyPath)) return Settings.Default;
        var s = Load(legacyPath);
        TrySave(path, s);
        return s;
    }

    static string ModeName(Mode m) => m switch { Mode.Temp => "temp", Mode.Usage => "usage", _ => "auto" };

    public static string ToJson(Settings s)
    {
        var n = s.Normalized();
        return "{\n  \"mode\": \"" + ModeName(n.Mode) + "\",\n  \"switchSeconds\": " + n.SwitchSeconds +
               ",\n  \"fahrenheit\": " + (n.Fahrenheit ? "true" : "false") +
               ",\n  \"alarm\": " + (n.Alarm ? "true" : "false") + ",\n  \"alarmC\": " + n.AlarmC + "\n}\n";
    }

    public static Settings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Settings.Default;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 4 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Settings.Default;

            var mode = Settings.Default.Mode;
            if (doc.RootElement.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String)
            {
                switch ((m.GetString() ?? "").Trim().ToLowerInvariant())
                {
                    case "temp": mode = Mode.Temp; break;
                    case "usage": mode = Mode.Usage; break;
                    case "auto": mode = Mode.Auto; break;
                }
            }
            int seconds = Settings.DefaultSwitchSeconds;
            if (doc.RootElement.TryGetProperty("switchSeconds", out var n) && n.ValueKind == JsonValueKind.Number)
                seconds = n.TryGetDouble(out double d) ? (int)Math.Clamp(Math.Round(d), 0, 1000) : seconds;
            bool fahrenheit = doc.RootElement.TryGetProperty("fahrenheit", out var f) && f.ValueKind == JsonValueKind.True;
            bool alarm = doc.RootElement.TryGetProperty("alarm", out var a) && a.ValueKind == JsonValueKind.True;
            int alarmC = Settings.DefaultAlarmC;
            if (doc.RootElement.TryGetProperty("alarmC", out var ac) && ac.ValueKind == JsonValueKind.Number && ac.TryGetDouble(out double acd))
                alarmC = (int)Math.Clamp(Math.Round(acd), 0, 1000);
            return new Settings(mode, seconds, fahrenheit, alarm, alarmC).Normalized();
        }
        catch (JsonException) { return Settings.Default; }
    }

    public static Settings Load(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > MaxBytes) return Settings.Default;
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Settings.Default; }
    }

    /// <summary>Writes through a temporary file so a crash cannot leave half a file. False if it could not be saved.</summary>
    public static bool TrySave(string path, Settings s)
    {
        string tmp = path + ".tmp";
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, ToJson(s));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (Exception) { }
            return false;
        }
    }
}
