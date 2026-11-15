using System.Diagnostics;
using System.Text;

namespace AK400SE.Ui;

/// <summary>
/// The scheduled task "AK400SE" (see <see cref="TaskXml"/>). The installer creates it once while elevated; after that the
/// app can start with administrator rights without a UAC prompt. All calls go to schtasks.exe with fixed arguments.
/// </summary>
static class StartTask
{
    public const int ExitOk = 0, ExitFailed = 1, ExitBadArgs = 2, ExitNotAllowed = 3, ExitNotElevated = 5;

    static string Schtasks => Path.Combine(Environment.SystemDirectory, "schtasks.exe");
    static string ExePath => System.Windows.Forms.Application.ExecutablePath;

    /// <summary>The task runs with highest privileges, so it may only point at a copy normal users cannot replace (Program Files).</summary>
    public static bool CanManage() =>
        TaskXml.IsSafeInstallLocation(ExePath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    static int Exec(string arguments, out string output)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo(Schtasks, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return -1;
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } return -2; }
            output = o.Result + e.Result;
            return p.ExitCode;
        }
        catch (Exception) { return -3; }
    }

    public static bool Exists() => Exec("/Query /TN " + TaskXml.TaskName, out _) == 0;

    public static bool IsLogonEnabled() =>
        Exec("/Query /TN " + TaskXml.TaskName + " /XML", out string xml) == 0 && TaskXml.HasEnabledLogonTrigger(xml);

    /// <summary>Starts the task now (no prompt). True if Task Scheduler accepted the request.</summary>
    public static bool RunNow() => Exec("/Run /TN " + TaskXml.TaskName, out _) == 0;

    /// <summary>Creates or replaces the task, with or without the logon trigger. Null on success, else a message for the user.</summary>
    public static string? Register(bool logonTrigger)
    {
        if (!CanManage()) return "This setting is only available after installing AK400SE with the setup program.";
        if (!Elevation.IsElevated()) return "Administrator rights are needed to change this setting.";

        // The XML file goes next to the exe (inside Program Files), where normal users cannot swap it while schtasks reads it.
        string dir = Path.GetDirectoryName(ExePath)!;
        string file = Path.Combine(dir, "task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            string xml = TaskXml.Build(Elevation.CurrentUserSid(), ExePath, logonTrigger);
            File.WriteAllText(file, xml, Encoding.Unicode);   // schtasks wants UTF-16
            int code = Exec("/Create /TN " + TaskXml.TaskName + " /XML \"" + file + "\" /F", out string output);
            return code == 0 ? null : "Windows could not update the start-up task: " + output.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "Windows could not update the start-up task: " + e.Message;
        }
        finally { try { File.Delete(file); } catch (Exception) { } }
    }

    public static string? SetLogon(bool enabled) => Register(enabled);

    public static string? Remove()
    {
        if (!Elevation.IsElevated()) return "Administrator rights are needed to remove the start-up task.";
        int code = Exec("/Delete /TN " + TaskXml.TaskName + " /F", out string output);
        return code == 0 || !Exists() ? null : "Windows could not remove the start-up task: " + output.Trim();
    }

    /// <summary>Entry for the installer: "--task on|off|remove". Exactly these three words; anything else is refused.</summary>
    public static int RunHook(string verb)
    {
        if (verb != "on" && verb != "off" && verb != "remove") return ExitBadArgs;
        if (!Elevation.IsElevated()) return ExitNotElevated;
        if (verb != "remove" && !CanManage()) return ExitNotAllowed;
        string? error = verb == "remove" ? Remove() : Register(verb == "on");
        return error is null ? ExitOk : ExitFailed;
    }
}
