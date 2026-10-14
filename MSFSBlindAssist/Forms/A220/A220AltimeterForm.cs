using System.Globalization;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Ctrl+B for the Synaptic A220: "Altimeter and minimums". QNH, unit, STD and the BARO/RADIO
/// minimums for both CTPs or one, each set through the CTP's own SetConfig call and read back by
/// SynapticA220Definition (Avionics partial, [A220-33]); the old KOHLSMAN_SET + knob walk is gone.
/// The definition speaks the read-backs; this form speaks only an entry it cannot parse. "Now"
/// is a read-only TextBox (VAT-13), refreshed once a second and only when its text changes.
/// </summary>
public sealed class A220AltimeterForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly SynapticA220Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly ComboBox _sides = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _qnh = new();
    private readonly Button _setQnh = new() { Text = "Set &QNH" };
    private readonly ComboBox _unit = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _std = new() { Text = "S&tandard: unknown" };
    private readonly ComboBox _minMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _minimums = new();
    private readonly Button _setMinimums = new() { Text = "S&et minimums" };
    private readonly TextBox _status = new() { ReadOnly = true, Multiline = true, TabStop = true };
    private readonly Button _close = new() { Text = "&Close" };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private IntPtr _previousWindow;
    private bool _syncing;

    public A220AltimeterForm(SynapticA220Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
        Text = "Altimeter and minimums";
        Size = new Size(480, 420);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        // Mnemonics: D sides, N QNH box, Q set QNH, U units, T standard, M minimums mode,
        // I minimums box, E set minimums, W now, C close — each label sits right before its control.
        int y = 12, tab = 0;
        void Row(string label, Control control, Control? button = null)
        {
            Controls.Add(new Label { Text = label, Location = new Point(12, y + 4), Size = new Size(160, 20), TabIndex = tab++ });
            control.Location = new Point(176, y);
            control.Size = new Size(150, 24);
            control.TabIndex = tab++;
            Controls.Add(control);
            if (button != null)
            {
                button.Location = new Point(334, y - 1);
                button.Size = new Size(124, 26);
                button.TabIndex = tab++;
                Controls.Add(button);
            }
            y += 34;
        }

        _sides.Items.AddRange(new object[] { "Both sides", "Captain", "First officer" });
        _sides.SelectedIndex = 0;
        _sides.AccessibleName = "Sides";
        Row("Si&des:", _sides);

        _qnh.AccessibleName = "QNH, hectopascals or inches";
        Row("Q&NH (hPa or inches):", _qnh, _setQnh);

        _unit.Items.AddRange(new object[] { "Inches", "Hectopascals" });
        _unit.AccessibleName = "Units";
        Row("&Units:", _unit);

        _std.Location = new Point(176, y);
        _std.Size = new Size(282, 26);
        _std.TabIndex = tab++;
        _std.AccessibleName = "Standard: unknown";
        Controls.Add(_std);
        y += 34;

        _minMode.Items.AddRange(new object[] { "Baro", "Radio" });
        _minMode.AccessibleName = "Minimums mode";
        Row("Minimums &mode:", _minMode);

        _minimums.AccessibleName = "Minimums, feet";
        Row("M&inimums (feet):", _minimums, _setMinimums);

        _status.AccessibleName = "Now";
        Row("No&w:", _status);
        _status.Size = new Size(282, 60);
        y += 30;

        _close.Location = new Point(334, y);
        _close.Size = new Size(124, 28);
        _close.TabIndex = tab++;
        Controls.Add(_close);
        CancelButton = _close;

        _setQnh.Click += (_, _) => SetQnh();
        _qnh.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SetQnh(); } };
        _setMinimums.Click += (_, _) => SetMinimums();
        _minimums.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SetMinimums(); } };
        _unit.SelectionChangeCommitted += (_, _) =>
        {
            if (!_syncing && _unit.SelectedIndex >= 0) _ = _def.SetBaroUnitAsync(_unit.SelectedIndex == 1, Sides(), _announcer);
        };
        _minMode.SelectionChangeCommitted += (_, _) =>
        {
            if (!_syncing && _minMode.SelectedIndex >= 0) _ = _def.SetMinimumsModeAsync(_minMode.SelectedIndex, Sides(), _announcer);
        };
        _std.Click += (_, _) => _ = _def.ToggleStdAsync(Sides(), _announcer);
        _close.Click += (_, _) => Close();
        _refresh.Tick += (_, _) => SyncFromAircraft();
        FormClosed += (_, _) =>
        {
            _refresh.Stop();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        SyncFromAircraft();
        if (_qnh.Text.Length == 0 && _def.CurrentQnhEntry(Sides()[0]) is { } q) _qnh.Text = q;
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _qnh.Focus();
        _qnh.SelectAll();
        _refresh.Start();
    }

    private IReadOnlyList<int> Sides() => _sides.SelectedIndex switch
    {
        1 => SynapticA220Definition.Captain,
        2 => SynapticA220Definition.FirstOfficer,
        _ => SynapticA220Definition.BothSides
    };

    private static bool TryReadNumber(string text, out double value)
        => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void SetQnh()
    {
        if (!TryReadNumber(_qnh.Text, out double v))
        {
            _announcer.AnnounceImmediate("Enter a QNH in hectopascals, like 1013, or inches, like 29.92.");
            return;
        }
        _ = _def.SetQnhAsync(v, Sides(), _announcer);
    }

    private void SetMinimums()
    {
        if (!TryReadNumber(_minimums.Text, out double v))
        {
            _announcer.AnnounceImmediate("Enter the minimums in feet, 0 to 41000.");
            return;
        }
        _ = _def.SetMinimumsAsync(v, Sides(), _announcer);
    }

    /// <summary>Status text and the controls' shown state from the aircraft. Never speaks; a
    /// focused control is left alone so a refresh never moves what the pilot is working on.</summary>
    private void SyncFromAircraft()
    {
        if (IsDisposed) return;
        string status = _def.DescribeAltimeters();
        if (_status.Text != status) _status.Text = status;
        var view = _def.DescribeCtp(Sides()[0]);
        string std = "S&tandard: " + (view.Std switch { true => "On", false => "Off", _ => "unknown" });
        if (_std.Text != std)
        {
            _std.Text = std;
            _std.AccessibleName = std.Replace("&", "");
        }
        _syncing = true;
        try
        {
            if (!_unit.Focused && view.Hpa is { } hpa && _unit.SelectedIndex != (hpa ? 1 : 0)) _unit.SelectedIndex = hpa ? 1 : 0;
            if (!_minMode.Focused && view.MinimumsMode is { } mm && mm is 0 or 1 && _minMode.SelectedIndex != mm) _minMode.SelectedIndex = mm;
        }
        finally { _syncing = false; }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Stop();
            _refresh.Dispose();
        }
        base.Dispose(disposing);
    }
}
