using HidSharp;

namespace DeepCoolLite;

/// <summary>
/// Finds the cooler's HID interface, uses the report ID its descriptor declares, and writes payloads.
/// Windows rejects a write whose first byte is not a declared report ID ("The parameter is incorrect"),
/// so the ID is read from the device rather than guessed from the product name.
/// </summary>
sealed class Display : IDisposable
{
    const int Vid = 0x3633, Pid = 0x0001;
    readonly HidStream _stream;
    readonly int _reportLen;      // includes the report-ID byte
    readonly byte _reportId;      // 0x00 = device has no numbered reports (SE), 0x10 = report ID 16 (regular AK400 DIGITAL)

    Display(HidStream s, int len, byte id) { _stream = s; _reportLen = len; _reportId = id; }

    /// <param name="layout">"auto" (use the descriptor), "se" (report ID 0) or "std" (report ID 0x10)</param>
    /// <param name="log">Filled with what was found and what failed, for the user to paste when nothing works.</param>
    public static Display? TryConnect(string layout, List<string> log, out string info)
    {
        info = "";
        List<HidDevice> devices;
        try { devices = DeviceList.Local.GetHidDevices(Vid, Pid).ToList(); }
        catch (Exception e) { log.Add($"listing HID devices failed: {e.Message}"); return null; }
        if (devices.Count == 0) { log.Add("no HID device 3633:0001 is present"); return null; }

        var candidates = devices
            .Select(d => (Dev: d, Ids: ReadOutputIds(d), Len: SafeLen(d)))
            .Select(x => { log.Add(Describe(x.Dev, x.Ids, x.Len)); return x; })
            .Where(x => x.Len > 0)
            .OrderBy(x => x.Ids?.Contains(0x10) == true ? 0 : x.Ids?.Contains(0x00) == true ? 1 : 2)
            .ToList();

        foreach (var (dev, ids, len) in candidates)
        {
            string name = SafeName(dev);
            if (!dev.TryOpen(out HidStream stream))
            {
                log.Add($"could not open {SafePath(dev)} (is the official DeepCool app still running?)");
                continue;
            }
            stream.WriteTimeout = 2000;
            foreach (byte id in ReportIdsToTry(layout, ids, name))
            {
                var d = new Display(stream, len, id);
                try
                {
                    d.Write(Packet.Init());
                    info = $"{name}, report ID 0x{id:X2}, out report {len} bytes";
                    return d;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.Add($"  write with report ID 0x{id:X2} failed: {e.GetType().Name}: {e.Message}");
                }
            }
            stream.Dispose();
        }
        return null;
    }

    static IEnumerable<byte> ReportIdsToTry(string layout, SortedSet<byte>? declared, string name)
    {
        if (layout == "se") return [0x00];
        if (layout == "std") return [0x10];
        if (declared is { Count: > 0 })
        {
            var known = new List<byte>();
            if (declared.Contains(0x10)) known.Add(0x10);
            if (declared.Contains(0x00)) known.Add(0x00);
            if (known.Count > 0) return known;
        }
        // descriptor unreadable, or it declares neither: fall back to the product name, then try the other one
        return name.StartsWith("AK", StringComparison.OrdinalIgnoreCase) ? [0x10, 0x00] : [0x00, 0x10];
    }

    static SortedSet<byte>? ReadOutputIds(HidDevice d)
    {
        try { return HidDescriptor.OutputReportIds(d.GetRawReportDescriptor()); }
        catch (Exception) { return null; }
    }

    static string Describe(HidDevice d, SortedSet<byte>? ids, int outLen)
    {
        string idText = ids is null ? "descriptor unavailable"
            : ids.Count == 0 ? "no output report in descriptor"
            : "output report IDs: " + string.Join(",", ids.Select(i => $"0x{i:X2}"));
        int inLen = 0, featLen = 0;
        try { inLen = d.GetMaxInputReportLength(); featLen = d.GetMaxFeatureReportLength(); } catch (Exception) { }
        return $"HID interface '{SafeName(d)}' {SafePath(d)}: max report bytes out/in/feature = {outLen}/{inLen}/{featLen}; {idText}";
    }

    static int SafeLen(HidDevice d) { try { return d.GetMaxOutputReportLength(); } catch (Exception) { return 0; } }
    static string SafeName(HidDevice d) { try { return d.GetProductName(); } catch (Exception) { return ""; } }
    static string SafePath(HidDevice d) { try { return d.GetFileSystemName(); } catch (Exception) { return "?"; } }

    public void Write(byte[] payload)
    {
        var buf = new byte[_reportLen];
        buf[0] = _reportId;
        Array.Copy(payload, 0, buf, 1, Math.Min(payload.Length, _reportLen - 1));
        _stream.Write(buf);
    }

    public void Dispose() => _stream.Dispose();
}
