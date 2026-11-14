using System.Drawing;
using System.Windows.Forms;

namespace DeepCoolLite.Ui;

/// <summary>The settings window: standard controls, laid out by containers so it follows the system font and DPI. Created when opened, disposed when closed.</summary>
sealed class MainForm : Form
{
    sealed class Choice
    {
        public readonly int Seconds;
        public Choice(int seconds) { Seconds = seconds; }
        public override string ToString() => Seconds + " seconds";
    }

    readonly RadioButton _temp = new RadioButton { Text = "Temperature", AutoSize = true };
    readonly RadioButton _usage = new RadioButton { Text = "CPU usage", AutoSize = true };
    readonly RadioButton _both = new RadioButton { Text = "Both, alternating", AutoSize = true };
    readonly ComboBox _switch = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly Label _cooler = new Label { AutoSize = true, MaximumSize = new Size(360, 0) };
    readonly Label _temperature = new Label { AutoSize = true, MaximumSize = new Size(360, 0) };
    readonly CheckBox _start = new CheckBox { Text = "Start with Windows", AutoSize = true };
    readonly Button _close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 0) };
    readonly Action<Settings> _onSettings;
    readonly Func<bool, string?> _onAutostart;
    bool _loading = true;

    public MainForm(Settings settings, EngineStatus status, bool autostartOn, bool autostartAvailable,
                    Action<Settings> onSettings, Func<bool, string?> onAutostart)
    {
        _onSettings = onSettings;
        _onAutostart = onAutostart;

        Text = "DeepCool Lite";
        Icon = AppIcon.Load();
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        foreach (int s in Settings.SwitchChoices) _switch.Items.Add(new Choice(s));

        // what the cooler shows
        var modeFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
        };
        modeFlow.Controls.Add(_temp);
        modeFlow.Controls.Add(_usage);
        modeFlow.Controls.Add(_both);
        var switchRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(20, 0, 0, 0) };
        switchRow.Controls.Add(new Label { Text = "Switch every", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        switchRow.Controls.Add(_switch, 1, 0);
        modeFlow.Controls.Add(switchRow);
        var modeGroup = new GroupBox
        {
            Text = "What the cooler shows", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 8), Margin = new Padding(0, 0, 0, 8),
        };
        modeGroup.Controls.Add(modeFlow);

        // status
        var statusFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
        };
        statusFlow.Controls.Add(_cooler);
        statusFlow.Controls.Add(_temperature);
        var statusGroup = new GroupBox
        {
            Text = "Status", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 8), Margin = new Padding(0, 0, 0, 8),
        };
        statusGroup.Controls.Add(statusFlow);

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        root.Controls.Add(modeGroup);
        root.Controls.Add(statusGroup);
        _start.Margin = new Padding(3, 0, 3, 8);
        root.Controls.Add(_start);
        if (!autostartAvailable)
        {
            _start.Enabled = false;
            root.Controls.Add(new Label
            {
                Text = "Available after installing with the setup program.", AutoSize = true,
                MaximumSize = new Size(360, 0), Margin = new Padding(3, 0, 3, 8),
            });
        }
        _close.Anchor = AnchorStyles.Right;
        root.Controls.Add(_close);
        Controls.Add(root);

        AcceptButton = _close;
        CancelButton = _close;
        _close.Click += (s, e) => Close();

        // initial values, set before the change handlers can fire
        var n = settings.Normalized();
        _temp.Checked = n.Mode == Mode.Temp;
        _usage.Checked = n.Mode == Mode.Usage;
        _both.Checked = n.Mode == Mode.Auto;
        for (int i = 0; i < _switch.Items.Count; i++)
            if (((Choice)_switch.Items[i]).Seconds == n.SwitchSeconds) _switch.SelectedIndex = i;
        _switch.Enabled = _both.Checked;
        _start.Checked = autostartOn && autostartAvailable;
        UpdateStatus(status);
        _loading = false;

        _temp.CheckedChanged += OnModeChanged;
        _usage.CheckedChanged += OnModeChanged;
        _both.CheckedChanged += OnModeChanged;
        _switch.SelectedIndexChanged += OnModeChanged;
        _start.CheckedChanged += OnStartChanged;
    }

    public void UpdateStatus(EngineStatus st)
    {
        _cooler.Text = StatusText.Cooler(st);
        string? t = StatusText.Temperature(st);
        _temperature.Text = t ?? "";
        _temperature.Visible = t != null;
    }

    void OnModeChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (sender is RadioButton rb && !rb.Checked) return;   // react to the button that became selected only
        _switch.Enabled = _both.Checked;
        var mode = _temp.Checked ? Mode.Temp : _usage.Checked ? Mode.Usage : Mode.Auto;
        int seconds = (_switch.SelectedItem as Choice)?.Seconds ?? Settings.DefaultSwitchSeconds;
        _onSettings(new Settings(mode, seconds));
    }

    void OnStartChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        string? error = _onAutostart(_start.Checked);
        if (error is null) return;
        _loading = true;
        _start.Checked = !_start.Checked;
        _loading = false;
        MessageBox.Show(this, error, "DeepCool Lite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
