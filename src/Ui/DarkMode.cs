using System.Windows.Forms;

namespace AK400SE.Ui;

static class DarkMode
{
    /// <summary>
    /// Follow the Windows light/dark setting (Windows 11 and later; older versions and high-contrast themes stay light).
    /// Must run before any window is created. The WFO5001 "experimental" warning only applies to .NET 9.
    /// </summary>
    public static void Enable()
    {
#pragma warning disable WFO5001
        try { Application.SetColorMode(SystemColorMode.System); }
        catch (Exception) { /* keep the light theme */ }
#pragma warning restore WFO5001
    }
}
