using System.Security.Principal;

namespace AK400SE.Ui;

static class Elevation
{
    /// <summary>True only for an elevated administrator token (a filtered, un-elevated admin token returns false).</summary>
    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string CurrentUserSid()
    {
        using var id = WindowsIdentity.GetCurrent();
        return id.User?.Value ?? "";
    }
}
