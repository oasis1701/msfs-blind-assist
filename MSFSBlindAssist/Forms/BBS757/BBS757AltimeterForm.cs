using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// The 757's "Altimeter and minimums" window (Ctrl+B in input mode). Two boxes that Enter applies,
/// and a Standard button:
/// <list type="bullet">
/// <item>Altimeter, in inches or hectopascals: sets all three altimeters at once.</item>
/// <item>Standard: 29.92 inches, 1013 hectopascals.</item>
/// <item>Radio minimums, in feet: the decision height the ADI shows.</item>
/// </list>
/// What is set and said is the definition's (BBS757Definition.Altimeter.cs): it reads the values
/// back and speaks them (CORE-7: a numeric confirmation always speaks). This window speaks only a
/// typed value it cannot take (a validation error always speaks), and keeps the last result in a
/// read-only status box the pilot can Tab to (VAT-13), which opens on the values the aircraft
/// shows. The window stays open for the next value; Escape closes it.
/// </summary>
public sealed class BBS757AltimeterForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<string, string?> _setAltimeter;
    private readonly Action _setStandard;
    private readonly Func<string, string?> _setMinimums;
    private readonly IntPtr _previousWindow;

    private TextBox _altimeterBox = null!;
    private TextBox _minimumsBox = null!;
    private TextBox _statusBox = null!;

    /// <param name="setAltimeter">Takes the typed altimeter; returns the error to say, or null when the set started.</param>
    /// <param name="setStandard">Sets the altimeters to standard.</param>
    /// <param name="setMinimums">Takes the typed minimums; returns the error to say, or null when the set started.</param>
    public BBS757AltimeterForm(ScreenReaderAnnouncer announcer, Func<string, string?> setAltimeter, Action setStandard,
        Func<string, string?> setMinimums)
    {
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _setAltimeter = setAltimeter;
        _setStandard = setStandard;
        _setMinimums = setMinimums;
        BuildForm();
    }

    private void BuildForm()
    {
        Text = "Altimeter and minimums";
        AccessibleName = "Altimeter and minimums";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        KeyPreview = true;

        const int left = 15, labelW = 210, boxX = 230, boxW = 120, h = 28, gap = 10;
        int y = 15, tab = 0;

        Controls.Add(new Label { Text = "&Altimeter, inches or hectopascals", Location = new Point(left, y + 5), Size = new Size(labelW, 20), TabIndex = tab++ });
        _altimeterBox = new TextBox
        {
            Location = new Point(boxX, y + 2),
            Size = new Size(boxW, 25),
            AccessibleName = "Altimeter value",
            AccessibleDescription = "Type 29.92 or 1013 and press Enter to set all three altimeters",
            TabIndex = tab++,
        };
        _altimeterBox.KeyDown += (_, e) => OnEnter(e, _altimeterBox, _setAltimeter);
        Controls.Add(_altimeterBox);
        y += h + gap;

        var standard = new Button
        {
            Text = "&Standard, 29.92",
            Location = new Point(left, y),
            Size = new Size(boxX + boxW - left, h),
            TabIndex = tab++,
        };
        standard.Click += (_, _) =>
        {
            ShowStatus("Setting the altimeters to standard.");
            _setStandard();
        };
        Controls.Add(standard);
        y += h + gap + 6;

        Controls.Add(new Label { Text = "Radio &minimums, feet", Location = new Point(left, y + 5), Size = new Size(labelW, 20), TabIndex = tab++ });
        _minimumsBox = new TextBox
        {
            Location = new Point(boxX, y + 2),
            Size = new Size(boxW, 25),
            AccessibleName = "Radio minimums value",
            AccessibleDescription = "Type the decision height in feet, 0 to 2500, and press Enter to set it",
            TabIndex = tab++,
        };
        _minimumsBox.KeyDown += (_, e) => OnEnter(e, _minimumsBox, _setMinimums);
        Controls.Add(_minimumsBox);
        y += h + gap + 6;

        Controls.Add(new Label { Text = "Status", Location = new Point(left, y), AutoSize = true, TabIndex = tab++ });
        y += 20;
        _statusBox = new TextBox
        {
            Location = new Point(left, y),
            Size = new Size(boxX + boxW - left, 60),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Altimeter and minimums status",
            TabIndex = tab++,
        };
        Controls.Add(_statusBox);
        y += 60 + gap;

        var close = new Button { Text = "Close", Location = new Point(left, y), Size = new Size(boxX + boxW - left, h), TabIndex = tab++ };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        ClientSize = new Size(boxX + boxW + left, y + h + 15);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && !e.Alt && !e.Control && !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Close();
            }
        };
        FormClosed += (_, _) =>
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
        Load += (_, _) =>
        {
            BringToFront();
            Activate();
            _altimeterBox.Focus();
        };
    }

    /// <summary>Enter in a box: hand the text over; a value it cannot take is said and the text selected for retyping.</summary>
    private void OnEnter(KeyEventArgs e, TextBox box, Func<string, string?> set)
    {
        if (e.KeyCode != Keys.Enter || e.Alt || e.Control) return;
        e.Handled = true;
        e.SuppressKeyPress = true;   // no ding
        string text = box.Text.Trim();
        string? error = text.Length == 0 ? "Please enter a value" : set(text);
        if (error != null)
        {
            ShowStatus(error);
            _announcer.AnnounceImmediate(error);
        }
        else
        {
            ShowStatus(box == _altimeterBox ? "Setting the altimeters." : "Setting the radio minimums.");
        }
        box.SelectAll();
    }

    /// <summary>The status box's text (not spoken: the definition speaks what it reads back). UI thread; a closed window ignores it.</summary>
    public void ShowStatus(string text)
    {
        if (IsDisposed) return;
        if (_statusBox.Text != text) _statusBox.Text = text;
    }
}
