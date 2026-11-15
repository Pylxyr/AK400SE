using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AK400SE;

/// <summary>
/// The scheduled task that starts the app with administrator rights without a UAC prompt. Created once by the installer
/// (which is elevated); the "Start with Windows" switch only adds or removes its logon trigger. Pure text work.
/// </summary>
public static class TaskXml
{
    public const string TaskName = "AK400SE";
    public const string FromTaskArgument = "--from-task";
    static readonly Regex SidPattern = new(@"^S-1-\d{1,3}(-\d{1,10}){1,15}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// A highest-privilege task must only ever point at a file that normal users cannot replace. True only for a path inside
    /// <paramref name="programFilesDir"/> (e.g. C:\Program Files), without ".." tricks.
    /// </summary>
    public static bool IsSafeInstallLocation(string? exePath, string? programFilesDir)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(programFilesDir)) return false;
        string p = exePath.Replace('/', '\\');
        string root = programFilesDir.Replace('/', '\\').TrimEnd('\\');
        if (root.Length < 3 || root[1] != ':') return false;
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        foreach (string seg in p.Split('\\')) if (seg == "..") return false;
        return p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) && p.Length > root.Length + 1;
    }

    public static string Build(string userSid, string exePath, bool logonTrigger)
    {
        if (!SidPattern.IsMatch(userSid ?? "")) throw new ArgumentException("not a Windows SID", nameof(userSid));
        if (string.IsNullOrWhiteSpace(exePath) || exePath.Length < 4 || exePath[1] != ':' || exePath[2] != '\\' ||
            exePath.Contains('"') || exePath.Any(char.IsControl))
            throw new ArgumentException("not an absolute Windows path", nameof(exePath));

        string sid = SecurityElement.Escape(userSid)!;
        string cmd = SecurityElement.Escape("\"" + exePath + "\"")!;
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n");
        sb.Append("<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n");
        sb.Append("  <RegistrationInfo><Description>Starts AK400SE with administrator rights, without a prompt.</Description></RegistrationInfo>\n");
        if (logonTrigger)
            sb.Append("  <Triggers>\n    <LogonTrigger>\n      <Enabled>true</Enabled>\n      <UserId>").Append(sid).Append("</UserId>\n    </LogonTrigger>\n  </Triggers>\n");
        sb.Append("  <Principals>\n    <Principal id=\"Author\">\n      <UserId>").Append(sid).Append("</UserId>\n");
        sb.Append("      <LogonType>InteractiveToken</LogonType>\n      <RunLevel>HighestAvailable</RunLevel>\n    </Principal>\n  </Principals>\n");
        sb.Append("  <Settings>\n");
        sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n");
        sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n");
        sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n");
        sb.Append("    <AllowHardTerminate>true</AllowHardTerminate>\n");
        sb.Append("    <StartWhenAvailable>true</StartWhenAvailable>\n");
        sb.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n");
        sb.Append("    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\n");
        sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\n");
        sb.Append("    <Enabled>true</Enabled>\n");
        sb.Append("    <Hidden>false</Hidden>\n");
        sb.Append("    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n");
        sb.Append("    <WakeToRun>false</WakeToRun>\n");
        sb.Append("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n");   // unlimited: the default would stop the app after 3 days
        sb.Append("    <Priority>7</Priority>\n");
        sb.Append("    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>\n");
        sb.Append("  </Settings>\n");
        sb.Append("  <Actions Context=\"Author\">\n    <Exec>\n      <Command>").Append(cmd).Append("</Command>\n");
        sb.Append("      <Arguments>").Append(FromTaskArgument).Append("</Arguments>\n    </Exec>\n  </Actions>\n</Task>\n");
        return sb.ToString();
    }

    /// <summary>True if the task XML (as printed by schtasks /Query /XML) has a logon trigger that is not disabled.</summary>
    public static bool HasEnabledLogonTrigger(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var t in doc.Descendants().Where(e => e.Name.LocalName == "LogonTrigger"))
            {
                var en = t.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled");
                if (en is null || !string.Equals(en.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch (System.Xml.XmlException) { return false; }
    }
}
