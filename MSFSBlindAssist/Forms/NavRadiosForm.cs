using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Parsed, validated NAV-radio settings produced by <see cref="NavRadiosForm"/>.
/// Frequencies are in MHz (108.00–117.95), courses in whole degrees (0–359).
/// </summary>
public record NavRadioSettings(double Nav1FreqMHz, int Nav1Course, double Nav2FreqMHz, int Nav2Course);

/// <summary>
/// Four-field dialog for tuning the NAV 1 / NAV 2 radios (frequency + course).
/// Opened from input mode via Ctrl+N. Fields are pre-filled with the current
/// values; pressing Set applies all four (re-applying unchanged values is
/// harmless). Frequency/course are set through standard SimConnect events by the
/// caller's apply callback — see PMDG737Definition.
/// </summary>
public class NavRadiosForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // VHF NAV band, 50 kHz channel spacing.
    private const double FreqMin = 108.00;
    private const double FreqMax = 117.95;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Action<NavRadioSettings> _onApply;
    private readonly IntPtr _previousWindow;

    private readonly TextBox _nav1Freq = new();
    private readonly TextBox _nav1Course = new();
    private readonly TextBox _nav2Freq = new();
    private readonly TextBox _nav2Course = new();
    private Button _setButton = null!;
    private Button _cancelButton = null!;

    public NavRadiosForm(
        ScreenReaderAnnouncer announcer,
        double nav1FreqMHz, int nav1Course,
        double nav2FreqMHz, int nav2Course,
        Action<NavRadioSettings> onApply)
    {
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _onApply = onApply;

        BuildLayout();

        // Pre-fill with current values so the user hears them and edits as needed.
        _nav1Freq.Text   = nav1FreqMHz.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        _nav1Course.Text = nav1Course.ToString();
        _nav2Freq.Text   = nav2FreqMHz.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        _nav2Course.Text = nav2Course.ToString();
    }

    private void BuildLayout()
    {
        Text = "NAV Radios";
        Size = new Size(360, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        int y = 20;
        int tab = 0;

        AddField("NAV 1 frequency (MHz)", _nav1Freq,
            "NAV 1 frequency in megahertz, 108.00 to 117.95", ref y, ref tab);
        AddField("NAV 1 course (degrees)", _nav1Course,
            "NAV 1 course in degrees, 0 to 359", ref y, ref tab);
        AddField("NAV 2 frequency (MHz)", _nav2Freq,
            "NAV 2 frequency in megahertz, 108.00 to 117.95", ref y, ref tab);
        AddField("NAV 2 course (degrees)", _nav2Course,
            "NAV 2 course in degrees, 0 to 359", ref y, ref tab);

        _setButton = new Button
        {
            Text = "&Set",
            Location = new Point(170, y + 8),
            Size = new Size(75, 30),
            AccessibleName = "Set NAV radios",
            TabIndex = tab++
        };
        _setButton.Click += (_, _) => Apply();

        _cancelButton = new Button
        {
            Text = "&Cancel",
            Location = new Point(255, y + 8),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            TabIndex = tab++
        };

        Controls.Add(_setButton);
        Controls.Add(_cancelButton);
        AcceptButton = _setButton;   // Enter applies
        CancelButton = _cancelButton; // Esc cancels

        Load += (_, _) =>
        {
            BringToFront();
            Activate();
            _nav1Freq.Focus();
            _nav1Freq.SelectAll();
        };
    }

    private void AddField(string label, TextBox box, string accessibleDescription, ref int y, ref int tab)
    {
        var lbl = new Label
        {
            Text = label,
            Location = new Point(20, y),
            Size = new Size(180, 20),
            AccessibleName = label
        };
        box.Location = new Point(200, y - 2);
        box.Size = new Size(130, 25);
        box.AccessibleName = label;
        box.AccessibleDescription = accessibleDescription;
        box.TabIndex = tab++;
        Controls.Add(lbl);
        Controls.Add(box);
        y += 35;
    }

    private void Apply()
    {
        if (!TryParseFreq(_nav1Freq, "NAV 1 frequency", out double f1)) return;
        if (!TryParseCourse(_nav1Course, "NAV 1 course", out int c1)) return;
        if (!TryParseFreq(_nav2Freq, "NAV 2 frequency", out double f2)) return;
        if (!TryParseCourse(_nav2Course, "NAV 2 course", out int c2)) return;

        _onApply(new NavRadioSettings(f1, c1, f2, c2));
        Close();
        RestoreFocus();
    }

    // Accepts "110.30", "110.3", "110", or the compact "11030" form.
    private bool TryParseFreq(TextBox box, string fieldName, out double mhz)
    {
        mhz = 0;
        string input = box.Text.Trim().Replace(',', '.');
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            Reject(box, $"{fieldName} must be a number between {FreqMin:0.00} and {FreqMax:0.00} megahertz");
            return false;
        }
        if (v >= 1000) v /= 100.0;          // "11030" -> 110.30
        // Snap to the nearest 50 kHz channel.
        v = Math.Round(v / 0.05) * 0.05;
        if (v < FreqMin || v > FreqMax)
        {
            Reject(box, $"{fieldName} must be between {FreqMin:0.00} and {FreqMax:0.00} megahertz");
            return false;
        }
        mhz = v;
        return true;
    }

    private bool TryParseCourse(TextBox box, string fieldName, out int course)
    {
        course = 0;
        string input = box.Text.Trim();
        if (!int.TryParse(input, out int v))
        {
            Reject(box, $"{fieldName} must be a whole number of degrees, 0 to 359");
            return false;
        }
        if (v == 360) v = 0;
        if (v < 0 || v > 359)
        {
            Reject(box, $"{fieldName} must be between 0 and 359 degrees");
            return false;
        }
        course = v;
        return true;
    }

    private void Reject(TextBox box, string message)
    {
        _announcer.AnnounceImmediate(message);
        box.Focus();
        box.SelectAll();
    }

    private void RestoreFocus()
    {
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            RestoreFocus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
