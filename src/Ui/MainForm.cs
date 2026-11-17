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
/// The settings window: a dark "display" card with the live CPU readings in a 7-segment font, then standard controls in one
/// table of auto-height rows, so nothing can be cut off and it follows the system font, DPI and light/dark setting.
/// It can be resized (never smaller than its content). Created when opened, disposed when closed.
/// </summary>
sealed class MainForm : Form
{
    sealed class Choice
    {
        public readonly int Seconds;
        public Choice(int seconds) { Seconds = seconds; }
        public override string ToString() => Seconds + " seconds";
    }

    const int StartWidth = 440;   // logical pixels at 100 % scaling; scaled automatically on high-DPI screens

    // the cooler's own look: charcoal display, teal digits (DeepCool's brand teal), orange when the alarm is active
    static readonly Color DisplayBack = Color.FromArgb(18, 24, 28);
    static readonly Color DigitColor = Color.FromArgb(46, 196, 190);
    static readonly Color CaptionColor = Color.FromArgb(128, 150, 156);
    static readonly Color AlertColor = Color.FromArgb(255, 107, 53);

    readonly RadioButton _temp = new RadioButton { Text = "Temperature", AutoSize = true };
    readonly RadioButton _usage = new RadioButton { Text = "CPU usage", AutoSize = true };
    readonly RadioButton _both = new RadioButton { Text = "Both, alternating", AutoSize = true };
    readonly ComboBox _switch = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly RadioButton _celsius = new RadioButton { Text = "Celsius (\u00B0C)", AutoSize = true };
    readonly RadioButton _fahrenheit = new RadioButton { Text = "Fahrenheit (\u00B0F)", AutoSize = true };
    readonly FlowLayoutPanel _unit = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
    readonly TableLayoutPanel _unitRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true };
    readonly CheckBox _alarmOn = new CheckBox { Text = "Flash the display at", AutoSize = true };
    readonly NumericUpDown _alarmC = new NumericUpDown { Minimum = Settings.MinAlarmC, Maximum = Settings.MaxAlarmC, Width = 56 };
    readonly Label _alarmNote = new Label { AutoSize = true };
    readonly Label _tempDigits = new Label { AutoSize = true, Text = "---", ForeColor = DigitColor, BackColor = DisplayBack, Margin = new Padding(0) };
    readonly Label _tempUnit = new Label { AutoSize = true, Text = "\u00B0C", ForeColor = DigitColor, BackColor = DisplayBack };
    readonly Label _loadDigits = new Label { AutoSize = true, Text = "---", ForeColor = DigitColor, BackColor = DisplayBack, Margin = new Padding(0) };
    readonly Label _loadUnit = new Label { AutoSize = true, Text = "%", ForeColor = DigitColor, BackColor = DisplayBack };
    readonly WrapLabel _cooler = new WrapLabel { Dock = DockStyle.Fill };
    readonly WrapLabel _temperature = new WrapLabel { Dock = DockStyle.Fill };
    readonly CheckBox _start = new CheckBox { Text = "Start with Windows", AutoSize = true };
    readonly Button _close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(92, 30) };
    readonly TableLayoutPanel _root = new TableLayoutPanel();
    readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };   // only while the window is open
    readonly DigitalFont _digital = new DigitalFont();
    readonly List<Font> _fonts = new List<Font>();
    readonly Font _bold;
    readonly Func<Readings> _live;
    readonly Action<Settings> _onSettings;
    readonly Func<bool, string?> _onAutostart;
    Settings _current;
    bool _loading = true;
    int _minWidth;

    public MainForm(Settings settings, EngineStatus status, Func<Readings> live, bool autostartOn, bool autostartAvailable,
                    Action<Settings> onSettings, Func<bool, string?> onAutostart)
    {
        _live = live;
        _onSettings = onSettings;
        _onAutostart = onAutostart;
        _current = settings.Normalized();

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
        ClientSize = new Size(StartWidth, 560);      // the height is fitted to the content in OnLoad

        foreach (int s in Settings.SwitchChoices) _switch.Items.Add(new Choice(s));

        _root.Dock = DockStyle.Fill;
        _root.ColumnCount = 1;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        AddRow(BuildReadout());

        AddRow(Section("Display"));
        foreach (var rb in new[] { _temp, _usage, _both })
        {
            rb.Margin = new Padding(10, 3, 3, 3);
            AddRow(rb);
        }
        var switchRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(32, 0, 0, 6) };
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        switchRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        switchRow.Controls.Add(new Label { Text = "Switch every", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        _switch.Anchor = AnchorStyles.Left;
        switchRow.Controls.Add(_switch, 1, 0);
        AddRow(switchRow);

        _celsius.Margin = new Padding(0, 3, 16, 3);
        _fahrenheit.Margin = new Padding(0, 3, 3, 3);
        _unit.Controls.Add(_celsius);        // its own container, so these two form their own radio group
        _unit.Controls.Add(_fahrenheit);
        _unitRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _unitRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _unitRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _unitRow.Margin = new Padding(10, 0, 3, 0);
        _unitRow.Controls.Add(new Label { Text = "Temperature unit", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) }, 0, 0);
        _unitRow.Controls.Add(_unit, 1, 0);
        AddRow(_unitRow);

        AddRow(Section("Alarm"));
        var alarmRow = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Margin = new Padding(10, 0, 3, 0) };
        for (int i = 0; i < 3; i++) alarmRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        alarmRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _alarmOn.Anchor = AnchorStyles.Left;
        _alarmC.Anchor = AnchorStyles.Left;
        _alarmC.Margin = new Padding(6, 3, 6, 3);
        _alarmNote.Anchor = AnchorStyles.Left;
        alarmRow.Controls.Add(_alarmOn, 0, 0);
        alarmRow.Controls.Add(_alarmC, 1, 0);
        alarmRow.Controls.Add(_alarmNote, 2, 0);
        AddRow(alarmRow);

        AddRow(Section("Status"));
        _cooler.Margin = new Padding(10, 3, 3, 3);
        _temperature.Margin = new Padding(10, 3, 3, 3);
        AddRow(_cooler);
        AddRow(_temperature);

        _start.Margin = new Padding(10, 12, 3, 3);
        AddRow(_start);
        if (!autostartAvailable)
        {
            _start.Enabled = false;
            AddRow(new WrapLabel { Text = "Available after installing with the setup program.", Dock = DockStyle.Fill, Margin = new Padding(10, 0, 3, 3) });
        }

        _close.Anchor = AnchorStyles.Right;
        _close.Margin = new Padding(3, 12, 3, 0);
        AddRow(_close);

        Controls.Add(_root);
        AcceptButton = _close;
        CancelButton = _close;
        _close.Click += (s, e) => Close();

        // initial values, set before the change handlers can fire
        var n = _current;
        _temp.Checked = n.Mode == Mode.Temp;
        _usage.Checked = n.Mode == Mode.Usage;
        _both.Checked = n.Mode == Mode.Auto;
        for (int i = 0; i < _switch.Items.Count; i++)
            if (((Choice)_switch.Items[i]).Seconds == n.SwitchSeconds) _switch.SelectedIndex = i;
        _celsius.Checked = !n.Fahrenheit;
        _fahrenheit.Checked = n.Fahrenheit;
        _alarmOn.Checked = n.Alarm;
        _alarmC.Value = n.AlarmC;
        ApplyEnabledStates();
        UpdateAlarmNote();
        _start.Checked = autostartOn && autostartAvailable;
        UpdateStatus(status);
        UpdateReadings();
        _loading = false;

        _temp.CheckedChanged += OnSettingsChanged;
        _usage.CheckedChanged += OnSettingsChanged;
        _both.CheckedChanged += OnSettingsChanged;
        _switch.SelectedIndexChanged += OnSettingsChanged;
        _celsius.CheckedChanged += OnSettingsChanged;
        _fahrenheit.CheckedChanged += OnSettingsChanged;
        _alarmOn.CheckedChanged += OnSettingsChanged;
        _alarmC.ValueChanged += OnSettingsChanged;
        _start.CheckedChanged += OnStartChanged;
        _timer.Tick += (s, e) => UpdateReadings();
        ResumeLayout(true);
    }

    void AddRow(Control c)
    {
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.Controls.Add(c, 0, _root.RowStyles.Count - 1);
        _root.RowCount = _root.RowStyles.Count;
    }

    Font Own(Font f) { _fonts.Add(f); return f; }

    /// <summary>The dark card that echoes the cooler's display: CPU temperature and usage as 7-segment digits.</summary>
    Control BuildReadout()
    {
        var digits = _digital.Make(30F);
        _tempDigits.Font = digits;
        _loadDigits.Font = digits;
        var unitFont = Own(new Font(Font.FontFamily, Font.Size + 5F, FontStyle.Regular));
        _tempUnit.Font = unitFont;
        _loadUnit.Font = unitFont;
        var captionFont = Own(new Font(Font.FontFamily, Math.Max(Font.Size - 1F, 7F), FontStyle.Bold));

        var card = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill,
            BackColor = DisplayBack, Padding = new Padding(18, 12, 18, 14), Margin = new Padding(0, 0, 0, 6),
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(Caption("CPU TEMPERATURE", captionFont), 0, 0);
        card.Controls.Add(Caption("CPU USAGE", captionFont), 1, 0);
        card.Controls.Add(ValueCell(_tempDigits, _tempUnit), 0, 1);
        card.Controls.Add(ValueCell(_loadDigits, _loadUnit), 1, 1);
        return card;
    }

    static Label Caption(string text, Font font) =>
        new Label { Text = text, Font = font, AutoSize = true, ForeColor = CaptionColor, BackColor = DisplayBack, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 2) };

    static Control ValueCell(Label digits, Label unit)
    {
        var cell = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, BackColor = DisplayBack, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        cell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        digits.Anchor = AnchorStyles.Left;
        unit.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        unit.Margin = new Padding(6, 0, 0, 8);
        cell.Controls.Add(digits, 0, 0);
        cell.Controls.Add(unit, 1, 0);
        return cell;
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
        _timer.Start();
    }

    public void UpdateStatus(EngineStatus st)
    {
        _cooler.Text = StatusText.Cooler(st);
        string? t = StatusText.Temperature(st);
        _temperature.Text = t ?? "";
        _temperature.Visible = t != null;
        if (IsHandleCreated) FitHeight(false);   // a longer message may need more room
    }

    /// <summary>Refreshes the digits from the engine's latest readings (once a second while the window is open).</summary>
    void UpdateReadings()
    {
        var r = _live();
        var s = _current;
        _tempDigits.Text = r.TempOk ? (s.Fahrenheit ? Packet.ToFahrenheit(r.TempC) : r.TempC).ToString().PadLeft(3) : "---";
        _tempUnit.Text = s.Fahrenheit ? "\u00B0F" : "\u00B0C";
        _loadDigits.Text = r.LoadOk ? r.LoadPct.ToString().PadLeft(3) : "---";
        bool hot = s.Alarm && r.TempOk && r.TempC >= s.AlarmC;
        _tempDigits.ForeColor = hot ? AlertColor : DigitColor;
        _tempUnit.ForeColor = hot ? AlertColor : DigitColor;
    }

    void ApplyEnabledStates()
    {
        _switch.Enabled = _both.Checked;
        _unitRow.Enabled = !_usage.Checked;      // the unit only matters when the temperature is shown
        _alarmC.Enabled = _alarmOn.Checked;
    }

    void UpdateAlarmNote()
    {
        int c = (int)_alarmC.Value;
        _alarmNote.Text = _fahrenheit.Checked ? "\u00B0C  (" + Packet.ToFahrenheit(c) + " \u00B0F)" : "\u00B0C";
    }

    Settings Compose()
    {
        var mode = _temp.Checked ? Mode.Temp : _usage.Checked ? Mode.Usage : Mode.Auto;
        int seconds = (_switch.SelectedItem as Choice)?.Seconds ?? Settings.DefaultSwitchSeconds;
        return new Settings(mode, seconds, _fahrenheit.Checked, _alarmOn.Checked, (int)_alarmC.Value);
    }

    void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (sender is RadioButton rb && !rb.Checked) return;   // react to the button that became selected only
        ApplyEnabledStates();
        UpdateAlarmNote();
        _current = Compose();
        _onSettings(_current);
        UpdateReadings();
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
        if (disposing)
        {
            _timer.Dispose();
            _bold.Dispose();
            foreach (var f in _fonts) f.Dispose();
            _digital.Dispose();
        }
        base.Dispose(disposing);
    }
}
