using HidSharp;

namespace DeepCoolLite;

/// <summary>
/// Opens the cooler and writes frames to it. The device is chosen by <see cref="DeviceCheck"/> (VID, PID and product string
/// of the AK400 DIGITAL SE, and an Output report ID taken from its own descriptor); nothing else is ever opened.
/// There is no way to hand this class raw bytes: the only write is <see cref="Show"/>, which builds a clamped frame.
/// </summary>
sealed class Display : ICoolerLink
{
    readonly HidStream _stream;
    readonly int _reportLen;      // includes the report-ID byte
    readonly byte _reportId;
    readonly WriteGuard _guard = new();

    Display(HidStream s, int len, byte id) { _stream = s; _reportLen = len; _reportId = id; }

    public static ConnectOutcome TryConnect()
    {
        List<HidDevice> devices;
        List<HidCandidate> candidates;
        try
        {
            // Only devices with the cooler's VID/PID are even listed; everything else on the PC is left alone.
            devices = DeviceList.Local.GetHidDevices(DeviceCheck.VendorId, DeviceCheck.ProductId).ToList();
            candidates = devices.Select(d => new HidCandidate(d.VendorID, d.ProductID, SafeName(d), ReadOutputIds(d), SafeLen(d))).ToList();
        }
        catch (Exception) { return new ConnectOutcome(CoolerState.NotFound, "", null); }

        var scan = DeviceCheck.Scan(candidates);
        if (scan.Presence == Presence.NotFound) return new ConnectOutcome(CoolerState.NotFound, "", null);
        if (scan.Presence == Presence.Unsupported) return new ConnectOutcome(CoolerState.Unsupported, scan.ProductName, null);

        try
        {
            if (!devices[scan.Index].TryOpen(out HidStream stream))
                return new ConnectOutcome(CoolerState.CannotOpen, scan.ProductName, null);
            stream.WriteTimeout = 2000;
            var d = new Display(stream, candidates[scan.Index].OutputReportLength, scan.ReportId);
            try
            {
                d.Write(Packet.Init());
                return new ConnectOutcome(CoolerState.Connected, scan.ProductName, d);
            }
            catch (Exception) { d.Dispose(); return new ConnectOutcome(CoolerState.CannotOpen, scan.ProductName, null); }
        }
        catch (Exception) { return new ConnectOutcome(CoolerState.CannotOpen, scan.ProductName, null); }
    }

    /// <summary>Shows one frame. Frames closer together than the minimum write interval are dropped.</summary>
    public void Show(bool showUsage, int tempC, int usagePct) => Write(Packet.Frame(showUsage, tempC, usagePct));

    void Write(byte[] payload)
    {
        if (!_guard.TryPass(Environment.TickCount64)) return;
        var buf = new byte[_reportLen];
        buf[0] = _reportId;
        Array.Copy(payload, 0, buf, 1, Math.Min(payload.Length, _reportLen - 1));
        _stream.Write(buf);
    }

    static IReadOnlySet<byte>? ReadOutputIds(HidDevice d)
    {
        try { return HidDescriptor.OutputReportIds(d.GetRawReportDescriptor()); }
        catch (Exception) { return null; }
    }

    static int SafeLen(HidDevice d) { try { return d.GetMaxOutputReportLength(); } catch (Exception) { return 0; } }
    static string? SafeName(HidDevice d) { try { return d.GetProductName(); } catch (Exception) { return null; } }

    public void Dispose() => _stream.Dispose();
}

sealed class HidFinder : ICoolerFinder
{
    public ConnectOutcome TryConnect() => Display.TryConnect();
}
