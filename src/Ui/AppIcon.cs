using System.Drawing;
using System.Windows.Forms;

namespace DeepCoolLite.Ui;

static class AppIcon
{
    /// <summary>The icon embedded as resource "app.ico", in the size Windows wants for a small icon (tray, title bar).</summary>
    public static Icon Load()
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico");
            if (s != null) return new Icon(s, SystemInformation.SmallIconSize.Width, SystemInformation.SmallIconSize.Height);
        }
        catch (Exception) { }
        return SystemIcons.Application;
    }
}
