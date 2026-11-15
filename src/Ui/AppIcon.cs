using System.Drawing;
using System.Windows.Forms;

namespace AK400SE.Ui;

static class AppIcon
{
    /// <summary>The whole multi-size icon, so the title bar and the taskbar each get a sharp size.</summary>
    public static Icon LoadWindow()
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico");
            if (s != null) return new Icon(s);
        }
        catch (Exception) { }
        return SystemIcons.Application;
    }

    /// <summary>The embedded icon "app.ico" in the size Windows wants for the tray.</summary>
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
