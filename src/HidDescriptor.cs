namespace AK400SE;

/// <summary>Minimal HID report-descriptor reader: which report IDs carry an Output (host to device) report.</summary>
public static class HidDescriptor
{
    /// <returns>
    /// The report IDs that have an Output item. ID 0 means "unnumbered": the device has no report IDs and
    /// the first byte given to Windows must be 0. An empty set means no Output report was found.
    /// </returns>
    public static SortedSet<byte> OutputReportIds(ReadOnlySpan<byte> d)
    {
        var ids = new SortedSet<byte>();
        byte current = 0;
        int i = 0;
        while (i < d.Length)
        {
            byte prefix = d[i];
            if (prefix == 0xFE)                      // long item: 0xFE, data length, tag, data...
            {
                if (i + 1 >= d.Length) break;
                i += 3 + d[i + 1];
                continue;
            }
            int size = (prefix & 3) == 3 ? 4 : prefix & 3;
            int type = (prefix >> 2) & 3;            // 0 main, 1 global, 2 local
            int tag = prefix >> 4;
            if (i + 1 + size > d.Length) break;

            if (type == 1 && tag == 8 && size == 1) current = d[i + 1];   // Global: Report ID
            else if (type == 0 && tag == 9) ids.Add(current);              // Main: Output
            i += 1 + size;
        }
        return ids;
    }
}
