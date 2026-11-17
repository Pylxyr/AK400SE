using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AK400SE.Ui;

/// <summary>
/// The 7-segment font for the live readout (DSEG7 Classic Bold, SIL Open Font License 1.1, see ThirdParty/DSEG-LICENSE.txt),
/// loaded from the copy embedded in the exe. Falls back to a fixed-width system font if it cannot be loaded.
/// Created with the window and disposed with it, so nothing stays in memory while the window is closed.
/// </summary>
sealed class DigitalFont : IDisposable
{
    readonly PrivateFontCollection _fonts = new PrivateFontCollection();
    readonly List<Font> _made = new List<Font>();
    IntPtr _memory = IntPtr.Zero;
    FontFamily? _family;

    public DigitalFont()
    {
        try
        {
            using var s = typeof(DigitalFont).Assembly.GetManifestResourceStream("digits.ttf");
            if (s is null) return;
            var bytes = new byte[s.Length];
            int read = 0;
            while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
            _memory = Marshal.AllocCoTaskMem(bytes.Length);       // must stay allocated as long as the collection lives
            Marshal.Copy(bytes, 0, _memory, bytes.Length);
            _fonts.AddMemoryFont(_memory, bytes.Length);
            if (_fonts.Families.Length > 0) _family = _fonts.Families[0];
        }
        catch (Exception) { _family = null; }
    }

    public Font Make(float points)
    {
        Font f;
        try { f = _family != null ? new Font(_family, points, FontStyle.Bold, GraphicsUnit.Point) : new Font("Consolas", points, FontStyle.Bold, GraphicsUnit.Point); }
        catch (Exception) { f = new Font(FontFamily.GenericMonospace, points, FontStyle.Bold, GraphicsUnit.Point); }
        _made.Add(f);
        return f;
    }

    public void Dispose()
    {
        foreach (var f in _made) f.Dispose();
        _made.Clear();
        _fonts.Dispose();
        if (_memory != IntPtr.Zero) { Marshal.FreeCoTaskMem(_memory); _memory = IntPtr.Zero; }
    }
}
