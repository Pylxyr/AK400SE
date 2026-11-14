using HidSharp;

namespace DeepCoolLite;

/// <summary>Opens the cooler and writes payloads with the right HID report ID.</summary>
sealed class Display : IDisposable
{
    const int Vid = 0x3633, Pid = 0x0001;
    readonly HidStream _stream;
    readonly int _reportLen;      // includes the report-ID byte
    readonly byte _reportId;      // 0x00 = device has no numbered reports (SE), 0x10 = regular AK400 DIGITAL

    Display(HidStream s, int len, byte id) { _stream = s; _reportLen = len; _reportId = id; }

    /// <param name="layout">"auto", "se" or "std"</param>
    public static Display? TryOpen(string layout, out string info)
    {
        info = "";
        var dev = DeviceList.Local.GetHidDevices(Vid, Pid).FirstOrDefault(d => SafeLen(d) > 0);
        if (dev is null) return null;

        string name = "";
        try { name = dev.GetProductName(); } catch (IOException) { }

        // SE units enumerate as "A400-DIGITAL", regular ones as "AK400-DIGITAL"
        bool se = layout switch
        {
            "se" => true,
            "std" => false,
            _ => !name.StartsWith("AK", StringComparison.OrdinalIgnoreCase),
        };

        if (!dev.TryOpen(out HidStream stream)) return null;
        stream.WriteTimeout = 2000;
        info = $"{name} ({(se ? "SE, no report ID" : "standard, report ID 0x10")}, out report {SafeLen(dev)} bytes)";
        return new Display(stream, SafeLen(dev), se ? (byte)0x00 : (byte)0x10);
    }

    static int SafeLen(HidDevice d) { try { return d.GetMaxOutputReportLength(); } catch (IOException) { return 0; } }

    public void Write(byte[] payload)
    {
        var buf = new byte[_reportLen];
        buf[0] = _reportId;
        Array.Copy(payload, 0, buf, 1, Math.Min(payload.Length, _reportLen - 1));
        _stream.Write(buf);
    }

    public void Dispose() => _stream.Dispose();
}
