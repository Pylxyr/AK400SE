using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AK400SE.Ui;

/// <summary>
/// The tray icon and its menu, the engine thread, and the window (created on demand, disposed on close).
/// Idle cost: one thread that wakes once per second, no window, no extra timers.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon _icon;
    readonly Engine _engine;
    readonly Thread _thread;
    readonly ManualResetEvent _stop = new ManualResetEvent(false);
    readonly Control _marshal = new Control();   // never shown: only gives background threads a way onto the UI thread
    readonly string _settingsPath = SettingsFile.DefaultPath();
    Settings _settings;
    MainForm? _form;

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    static long NowMs() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

    public TrayApp()
    {
        _ = _marshal.Handle;
        _settings = SettingsFile.Load(_settingsPath);
        _engine = new Engine(new HidFinder(), () => Cpu.Open(), PawnIo.IsInstalled, NowMs, _settings);

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open AK400SE");
        open.Font = new Font(open.Font, FontStyle.Bold);
        open.Click += (s, e) => ShowWindow();
        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (s, e) => Quit();
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quit);

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = StatusText.Tray(_engine.Status),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        _engine.StatusChanged += st => OnUi(() => OnStatus(st));
        _thread = new Thread(Loop) { IsBackground = true, Name = "AK400SE engine" };
        _thread.Start();
    }

    void Loop()
    {
        do
        {
            try { _engine.Tick(); }
            catch (Exception) { /* a failed tick must never end the loop */ }
        }
        while (!_stop.WaitOne(1000));
    }

    void OnUi(Action a)
    {
        try { if (_marshal.IsHandleCreated && !_marshal.IsDisposed) _marshal.BeginInvoke(a); }
        catch (Exception) { }
    }

    void OnStatus(EngineStatus st)
    {
        _icon.Text = StatusText.Tray(st);
        _form?.UpdateStatus(st);
    }

    void ShowWindow()
    {
        if (_form != null && !_form.IsDisposed)
        {
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            return;
        }
        var form = new MainForm(_settings, _engine.Status, StartTask.IsLogonEnabled(), StartTask.CanManage(), OnSettings, OnAutostart);
        form.FormClosed += (s, e) => { _form = null; OnUi(ReleaseMemory); };   // closing = hide to the tray: the window is gone, the app stays
        _form = form;
        form.Show();
        form.Activate();
    }

    void OnSettings(Settings s)
    {
        _settings = s;
        _engine.Settings = s;
        SettingsFile.TrySave(_settingsPath, s);
    }

    static string? OnAutostart(bool on) => StartTask.SetLogon(on);

    static void ReleaseMemory()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch (Exception) { }
    }

    void Quit()
    {
        _icon.Visible = false;
        _stop.Set();
        _thread.Join(4000);          // a write in progress can take up to its 2 s timeout
        try { _form?.Close(); } catch (Exception) { }
        _engine.Dispose();
        _icon.Dispose();
        ExitThread();
    }
}
