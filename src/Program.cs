using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;
using AK400SE.Ui;

namespace AK400SE;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Installer hooks (the installer is elevated): "--task on|off|remove". Nothing else is accepted on the command line
        // apart from the marker the scheduled task passes.
        if (args.Length == 2 && args[0] == "--task") return StartTask.RunHook(args[1]);
        bool fromTask = args.Length == 1 && args[0] == TaskXml.FromTaskArgument;
        if (args.Length > 0 && !fromTask) return StartTask.ExitBadArgs;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        DarkMode.Enable();

        if (!Elevation.IsElevated())
        {
            if (fromTask)
            {
                // The task started us but Windows did not give administrator rights (not an administrator account?). Do not relaunch: no loop.
                MessageBox.Show("AK400SE needs administrator rights to read the CPU temperature, and Windows did not grant them to this account.",
                    "AK400SE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }
            return LaunchElevated();
        }

        // One writer to the cooler: a second copy exits quietly (with 0, so the task does not restart it).
        Mutex? single;
        try
        {
            single = new Mutex(true, @"Global\AK400SE.Single", out bool created);
            if (!created) { single.Dispose(); return 0; }
        }
        catch (UnauthorizedAccessException) { return 0; }

        try { Application.Run(new TrayApp()); }
        finally { try { single.ReleaseMutex(); } catch (Exception) { } single.Dispose(); }
        return 0;
    }

    /// <summary>
    /// Started normally (Start menu, double click): ask Task Scheduler to start the installed task, which runs this exe elevated
    /// without a prompt. If there is no such task (for example a build folder), fall back to one ordinary UAC prompt.
    /// </summary>
    static int LaunchElevated()
    {
        if (StartTask.RunNow()) return 0;
        try
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception) { /* the user declined the prompt */ }
        return 0;
    }
}
