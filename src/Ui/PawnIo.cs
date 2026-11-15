using Microsoft.Win32;

namespace AK400SE.Ui;

static class PawnIo
{
    /// <summary>Whether the PawnIO driver looks installed (its uninstall entry, or its library in Program Files). Information only.</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if (key != null) return true;
        }
        catch (Exception) { }
        try { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll")); }
        catch (Exception) { return false; }
    }
}
