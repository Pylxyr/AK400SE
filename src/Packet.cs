namespace DeepCoolLite;

public enum Mode { Temp, Usage, Auto }

/// <summary>
/// Payload for the AK400 DIGITAL SE (USB 3633:0001), 64 bytes, report ID NOT included:
///   [0] mode  170 = init/animation, 19 = temperature (C), 35 = temperature (F), 76 = usage
///   [1] status bar, 1..10 segments
///   [2] hundreds digit  [3] tens digit  [4] ones digit
///   [5] alarm (never set here)
/// Regular (non-SE) AK devices use the same bytes shifted by one, behind report ID 0x10.
/// </summary>
public static class Packet
{
    public const int Size = 64;
    const byte ModeInit = 170, ModeTempC = 19, ModeTempF = 35, ModeUsage = 76;

    public static byte[] Init()
    {
        var p = new byte[Size];
        p[0] = ModeInit;
        return p;
    }

    public static byte[] Frame(bool showUsage, int tempC, int usagePct) => Frame(showUsage, tempC, usagePct, false);

    /// <param name="temp">The temperature in the unit that is shown (Celsius, or Fahrenheit when <paramref name="fahrenheit"/> is true).
    /// The display only shows the number; the mode byte tells it which unit to light up. Ignored in usage frames.</param>
    public static byte[] Frame(bool showUsage, int temp, int usagePct, bool fahrenheit)
    {
        temp = Math.Clamp(temp, 0, 999);
        usagePct = Math.Clamp(usagePct, 0, 100);
        int n = showUsage ? usagePct : temp;

        var p = new byte[Size];
        p[0] = showUsage ? ModeUsage : fahrenheit ? ModeTempF : ModeTempC;
        // bar always follows CPU usage: <15 % -> 1 segment, else round to nearest 10 % (half up)
        p[1] = usagePct < 15 ? (byte)1 : (byte)((usagePct + 5) / 10);
        p[2] = (byte)(n / 100);
        p[3] = (byte)(n % 100 / 10);
        p[4] = (byte)(n % 10);
        return p;
    }

    /// <summary>Whole degrees Fahrenheit for a whole-degree Celsius reading, rounded half up.</summary>
    public static int ToFahrenheit(int tempC) => (int)Math.Round(tempC * 9.0 / 5.0 + 32.0, MidpointRounding.AwayFromZero);
}
