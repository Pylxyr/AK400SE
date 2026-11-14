namespace DeepCoolLite;

/// <summary>What Windows reports about one HID interface, as plain data so the check can be tested without hardware.</summary>
public sealed record HidCandidate(
    int VendorId, int ProductId, string? ProductName, IReadOnlySet<byte>? OutputReportIds, int OutputReportLength);

public enum Presence { NotFound, Unsupported, Supported }

/// <summary>Result of looking at everything that is plugged in. <see cref="Index"/> points into the list given to Scan.</summary>
public sealed record DeviceScan(Presence Presence, int Index, byte ReportId, string ProductName);

/// <summary>
/// The only place that decides whether a device may be written to. Fail closed: anything that is not clearly
/// the AK400 DIGITAL SE is refused. The regular AK400 DIGITAL shares VID/PID but has another product string.
/// </summary>
public static class DeviceCheck
{
    public const int VendorId = 0x3633;
    public const int ProductId = 0x0001;
    public const string SeProductName = "A400-DIGITAL";
    /// <summary>Report ID byte plus the 5 payload bytes this program uses (see Packet).</summary>
    public const int MinOutputReportLength = 6;

    /// <summary>Product string made safe to compare and to show: control characters removed, trimmed, at most 64 characters.</summary>
    public static string CleanName(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) if (!char.IsControl(c)) sb.Append(c);
        string t = sb.ToString().Trim();
        return t.Length > 64 ? t.Substring(0, 64) : t;
    }

    public static bool IsSeProduct(string? name) => string.Equals(CleanName(name), SeProductName, StringComparison.Ordinal);

    /// <summary>
    /// Report ID taken from the device's own descriptor: 0x10 if it declares an Output report with that ID (the SE
    /// does), else 0x00 (unnumbered). Null when neither is declared: then nothing may be written.
    /// </summary>
    public static byte? ChooseReportId(IReadOnlySet<byte>? declared)
    {
        if (declared is null) return null;
        if (declared.Contains(0x10)) return 0x10;
        if (declared.Contains(0x00)) return 0x00;
        return null;
    }

    public static DeviceScan Scan(IReadOnlyList<HidCandidate> all)
    {
        string? other = null;
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (c.VendorId != VendorId || c.ProductId != ProductId) continue;   // any other device is never considered

            string name = CleanName(c.ProductName);
            if (!IsSeProduct(name)) { other ??= name; continue; }

            if (c.OutputReportLength < MinOutputReportLength || ChooseReportId(c.OutputReportIds) is not byte id)
            {
                other ??= name + " (no usable output report)";
                continue;
            }
            return new DeviceScan(Presence.Supported, i, id, SeProductName);
        }
        return other is null
            ? new DeviceScan(Presence.NotFound, -1, 0, "")
            : new DeviceScan(Presence.Unsupported, -1, 0, other);
    }
}
