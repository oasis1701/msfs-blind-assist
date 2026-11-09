using System.Globalization;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Parsed, validated NAV-radio settings produced by <see cref="NavRadiosForm"/>. Frequencies are in MHz,
/// courses in whole degrees (0–359). The third radio is set only by a window that has one (the A300's
/// ILS, a radio of its own where the 737s tune the ILS through NAV 1 and 2).
/// </summary>
public record NavRadioSettings(double Nav1FreqMHz, int Nav1Course, double Nav2FreqMHz, int Nav2Course,
    double? Nav3FreqMHz = null, int? Nav3Course = null);

/// <summary>One radio of the window: what it is called, its pre-filled values, and its band.</summary>
public sealed record NavRadioRow(string Name, double FrequencyMHz, int Course, double MinMHz = 108.00, double MaxMHz = 117.95);

/// <summary>
/// Dialog for tuning two or three NAV radios (frequency + course), opened from input mode via Ctrl+N.
/// Fields are pre-filled with the current values, and Enter in any field hands every field to the
/// caller's apply callback, which decides what is written: PMDG737Definition (standard SimConnect
/// events) and IFly737MAXDefinition (the NAV panel keypad, read back) re-apply all of them, unchanged
/// values too, while IniA300Definition.SetNavRadios writes only the fields that differ from the ones
/// the window opened with.
/// </summary>
public class NavRadiosForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Action<NavRadioSettings> _onApply;
    private readonly IntPtr _previousWindow;
    private readonly IReadOnlyList<NavRadioRow> _radios;
    private readonly List<TextBox> _freqBoxes = new();
    private readonly List<TextBox> _courseBoxes = new();
    private Button _cancelButton = null!;

    /// <summary>NAV 1 and NAV 2, the 108.00–117.95 band (PMDG 737, iFly 737).</summary>
    public NavRadiosForm(
        ScreenReaderAnnouncer announcer,
        double nav1FreqMHz, int nav1Course,
        double nav2FreqMHz, int nav2Course,
        Action<NavRadioSettings> onApply)
        : this(announcer, new[] { new NavRadioRow("NAV 1", nav1FreqMHz, nav1Course), new NavRadioRow("NAV 2", nav2FreqMHz, nav2Course) }, onApply)
    {
    }

    /// <summary>Two or three named radios, each with its own band.</summary>
    public NavRadiosForm(ScreenReaderAnnouncer announcer, IReadOnlyList<NavRadioRow> radios, Action<NavRadioSettings> onApply)
    {
        if (radios.Count is < 2 or > 3)
            throw new ArgumentException("A NAV radios window has two or three radios.", nameof(radios));
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _onApply = onApply;
        _radios = radios;

        BuildLayout();

        // Pre-fill with current values so the user hears them and edits as needed.
        for (int i = 0; i < _radios.Count; i++)
        {
            _freqBoxes[i].Text = _radios[i].FrequencyMHz.ToString("0.00", Inv);
            _courseBoxes[i].Text = _radios[i].Course.ToString(Inv);
        }
    }

    /// <summary>A typed frequency in megahertz, snapped to its 50 kHz channel ("110.30", "110.3", "110,3",
    /// or the compact "11030"), or null when it is not a number or lies outside min..max. "NaN" parses as a
    /// number but is none: it fails both range comparisons, so it is refused here before them.</summary>
    internal static double? ParseFrequency(string text, double minMHz, double maxMHz)
    {
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out double v) || !double.IsFinite(v))
            return null;
        if (v >= 1000) v /= 100.0;          // "11030" -> 110.30
        v = Math.Round(Math.Round(v / 0.05) * 0.05, 2);
        return v < minMHz - 1e-9 || v > maxMHz + 1e-9 ? null : v;
    }

    /// <summary>A course in whole degrees, 360 read as 0, or null outside 0..359.</summary>
    internal static int? ParseCourse(string text)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, Inv, out int v))
            return null;
        if (v == 360) v = 0;
        return v is >= 0 and <= 359 ? v : null;
    }

    private void BuildLayout()
    {
        Text = "NAV Radios";
        Size = new Size(360, 160 + 70 * _radios.Count);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        int y = 20;
        int tab = 0;

        foreach (var radio in _radios)
        {
            var freq = new TextBox();
            var course = new TextBox();
            AddField($"{radio.Name} frequency (MHz)", freq,
                $"{radio.Name} frequency in megahertz, {radio.MinMHz.ToString("0.00", Inv)} to {radio.MaxMHz.ToString("0.00", Inv)}", ref y, ref tab);
            AddField($"{radio.Name} course (degrees)", course, $"{radio.Name} course in degrees, 0 to 359", ref y, ref tab);
            _freqBoxes.Add(freq);
            _courseBoxes.Add(course);
        }

        // Enter in any field applies them all (AddField).
        _cancelButton = new Button
        {
            Text = "&Cancel",
            Location = new Point(255, y + 8),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            TabIndex = tab++
        };
        // Both callers show this window modeless (Show), where DialogResult closes nothing:
        // Cancel closes it itself, as Escape does.
        _cancelButton.Click += (_, _) => { Close(); RestoreFocus(); };

        Controls.Add(_cancelButton);
        CancelButton = _cancelButton; // Esc cancels

        Load += (_, _) =>
        {
            BringToFront();
            Activate();
            _freqBoxes[0].Focus();
            _freqBoxes[0].SelectAll();
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
        // Enter applies every field. With no AcceptButton the box receives Enter itself;
        // SuppressKeyPress stops the edit control's ding.
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Apply();
        };
        Controls.Add(lbl);
        Controls.Add(box);
        y += 35;
    }

    private void Apply()
    {
        var freqs = new double[_radios.Count];
        var courses = new int[_radios.Count];
        for (int i = 0; i < _radios.Count; i++)
        {
            var radio = _radios[i];
            if (ParseFrequency(_freqBoxes[i].Text, radio.MinMHz, radio.MaxMHz) is not double freq)
            {
                Reject(_freqBoxes[i], $"{radio.Name} frequency must be between {radio.MinMHz.ToString("0.00", Inv)} and {radio.MaxMHz.ToString("0.00", Inv)} megahertz");
                return;
            }
            if (ParseCourse(_courseBoxes[i].Text) is not int course)
            {
                Reject(_courseBoxes[i], $"{radio.Name} course must be a whole number of degrees, 0 to 359");
                return;
            }
            freqs[i] = freq;
            courses[i] = course;
        }

        bool third = _radios.Count > 2;
        _onApply(new NavRadioSettings(freqs[0], courses[0], freqs[1], courses[1],
            third ? freqs[2] : null, third ? courses[2] : null));
        Close();
        RestoreFocus();
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
