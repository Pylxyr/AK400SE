using System.Drawing;
using System.Windows.Forms;

namespace AK400SE.Ui;

/// <summary>A label that wraps its text to the width it is given and reports the height that needs (plain Labels do not).</summary>
sealed class WrapLabel : Label
{
    public WrapLabel() { AutoSize = true; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width;
        if (width <= 0 || width == int.MaxValue) width = Parent != null ? Parent.ClientSize.Width : 320;
        int inner = Math.Max(width - Padding.Horizontal, 20);
        Size text = TextRenderer.MeasureText(Text, Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        return new Size(width, text.Height + Padding.Vertical + 2);
    }
}

/// <summary>
/// The settings window: standard controls in one table of auto-height rows, so nothing can be cut off and it follows the
/// system font and DPI. It can be resized (never smaller than its content). Created when opened, disposed when closed.
/// </summary>
sealed class MainForm : Form
{
    sealed class Choice
    {
        public readonly int Seconds;
        public Choice(int seconds) { Seconds = seconds; }
        public override string ToString() => Seconds + " seconds";
    }

    const int StartWidth = 420;   // logical pixels at 100 % scaling; scaled automatically on high-DPI screens

    readonly RadioButton _temp = new RadioButton { Text = "Temperature", AutoSize = true };
    readonly RadioButton _usage = new RadioButton { Text = "CPU usage", AutoSize = true };
    readonly RadioButton _both = new RadioButton { Text = "Both, alternating", AutoSize = true };
    readonly RadioButton _celsius = new RadioButton { Text = "Celsius (\u00B0C)", AutoSize = true };
    readonly RadioButton _fahrenheit = new RadioButton { Text = "Fahrenheit (\u00B0F)", AutoSize = true };
    readonly FlowLayoutPanel _unit = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
    readonly ComboBox _switch = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly WrapLabel _cooler = new WrapLabel { Dock = DockStyle.Fill };
    readonly WrapLabel _temperature = new WrapLabel { Dock = DockStyle.Fill };
    readonly CheckBox _start = new CheckBox { Text = "Start with Windows", AutoSize = true };
    readonly Button _close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(92, 30) };
    readonly TableLayoutPanel _root = new TableLayoutPanel();
    readonly Font _bold;
    readonly Action<Settings> _onSettings;
    readonly Func<bool, string?> _onAutostart;
    bool _loading = true;
    int _minWidth;

    public MainForm(Settings settings, EngineStatus status, bool autostartOn, bool autostartAvailable,
                    Action<Settings> onSettings, Func<bool, string?> onAutostart)
    {
        _onSettings = onSettings;
        _onAutostart = onAutostart;

        SuspendLayout();
        Text = "AK400SE";
        Icon = AppIcon.LoadWindow();
        Font = SystemFonts.MessageBoxFont ?? Font;
        _bold = new Font(Font, FontStyle.Bold);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;   // can be resized; the title bar moves it as usual
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(18, 12, 18, 16);
        ClientSize = new Size(StartWidth, 380);      // the height is fitted to the content in OnLoad

        foreach (int s in Settings.SwitchChoices) _switch.Items.Add(new Choice(s));

        _root.Dock = DockStyle.Fill;
        _root.ColumnCount = 1;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        AddRow(Section("What the cooler shows"));
        foreach (var rb in new[] { _temp, _usage, _both })
        {
            rb.Margin = new Padding(10, 3, 3, 3);
            AddRow(rb);
        }
        var switchRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(32, 0, 0, 0) };
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        switchRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        switchRow.Controls.Add(new Label { Text = "Switch every", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        _switch.Anchor = AnchorStyles.Left;
        switchRow.Controls.Add(_switch, 1, 0);
        AddRow(switchRow);

        AddRow(Section("Temperature unit"));
        _celsius.Margin = new Padding(0, 3, 16, 3);
        _fahrenheit.Margin = new Padding(0, 3, 3, 3);
        _unit.Controls.Add(_celsius);       // its own container, so these two form their own radio group
        _unit.Controls.Add(_fahrenheit);
        _unit.Margin = new Padding(10, 0, 3, 0);
        AddRow(_unit);

        AddRow(Section("Status"));
        _cooler.Margin = new Padding(10, 3, 3, 3);
        _temperature.Margin = new Padding(10, 3, 3, 3);
        AddRow(_cooler);
        AddRow(_temperature);

        AddRow(Section("Start-up"));
        _start.Margin = new Padding(10, 3, 3, 3);
        AddRow(_start);
        if (!autostartAvailable)
        {
            _start.Enabled = false;
            AddRow(new WrapLabel { Text = "Available after installing with the setup program.", Dock = DockStyle.Fill, Margin = new Padding(10, 0, 3, 3) });
        }

        _close.Anchor = AnchorStyles.Right;
        _close.Margin = new Padding(3, 14, 3, 0);
        AddRow(_close);

        Controls.Add(_root);
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
        _celsius.Checked = !n.Fahrenheit;
        _fahrenheit.Checked = n.Fahrenheit;
        _unit.Enabled = !_usage.Checked;     // the unit only matters when the temperature is shown
        _start.Checked = autostartOn && autostartAvailable;
        UpdateStatus(status);
        _loading = false;

        _temp.CheckedChanged += OnModeChanged;
        _usage.CheckedChanged += OnModeChanged;
        _both.CheckedChanged += OnModeChanged;
        _celsius.CheckedChanged += OnModeChanged;
        _fahrenheit.CheckedChanged += OnModeChanged;
        _switch.SelectedIndexChanged += OnModeChanged;
        _start.CheckedChanged += OnStartChanged;
        ResumeLayout(true);
    }

    void AddRow(Control c)
    {
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.Controls.Add(c, 0, _root.RowStyles.Count - 1);
        _root.RowCount = _root.RowStyles.Count;
    }

    /// <summary>A bold heading followed by the usual thin etched line.</summary>
    Control Section(string text)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = text, Font = _bold, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        t.Controls.Add(new Label { AutoSize = false, Height = 2, BorderStyle = BorderStyle.Fixed3D, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 2, 0, 0) }, 1, 0);
        return t;
    }

    /// <summary>Height the content needs at the current width.</summary>
    void FitHeight(bool exact)
    {
        int needed = _root.GetPreferredSize(new Size(ClientSize.Width - Padding.Horizontal, 0)).Height + Padding.Vertical;
        if (exact || needed > ClientSize.Height) ClientSize = new Size(ClientSize.Width, needed);
        if (_minWidth == 0) _minWidth = Width;   // never narrower than it opened
        MinimumSize = new Size(_minWidth, Height - ClientSize.Height + needed);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitHeight(true);
    }

    public void UpdateStatus(EngineStatus st)
    {
        _cooler.Text = StatusText.Cooler(st);
        string? t = StatusText.Temperature(st);
        _temperature.Text = t ?? "";
        _temperature.Visible = t != null;
        if (IsHandleCreated) FitHeight(false);   // a longer message may need more room
    }

    void OnModeChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (sender is RadioButton rb && !rb.Checked) return;   // react to the button that became selected only
        _switch.Enabled = _both.Checked;
        _unit.Enabled = !_usage.Checked;
        var mode = _temp.Checked ? Mode.Temp : _usage.Checked ? Mode.Usage : Mode.Auto;
        int seconds = (_switch.SelectedItem as Choice)?.Seconds ?? Settings.DefaultSwitchSeconds;
        _onSettings(new Settings(mode, seconds, _fahrenheit.Checked));
    }

    void OnStartChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        string? error = _onAutostart(_start.Checked);
        if (error is null) return;
        _loading = true;
        _start.Checked = !_start.Checked;
        _loading = false;
        MessageBox.Show(this, error, "AK400SE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bold.Dispose();
        base.Dispose(disposing);
    }
}
