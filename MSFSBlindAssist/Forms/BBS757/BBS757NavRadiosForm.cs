using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// The 757's "Nav Radios" window (Ctrl+N in input mode): what the FMC cannot set. A read-only status box
/// first, which opens on the nav radios as output N reads them and the FMC's runway ILS from APPROACH REF
/// (the ILS control panel is tuned by hand, and the FMC posts an ERROR when it differs), then the
/// "ILS course" and "ILS frequency" boxes, each applied by Enter in it alone (R55), and the "ILS park"
/// button. The VORs are not here: a VOR is tuned, and its course set, on the CDU (the CDU window).
///
/// What is read and set is the definition's (BBS757Definition.Radios.cs). This window speaks only a typed
/// value it cannot take (a validation error always speaks); the definition speaks the status once it is
/// filled, the ILS course it reads back and the ILS frequency or park the panel's display shows after the
/// knobs turned (or, when the display cannot be read, the clicks it counted, saying so). A fill that ends after
/// a set, park or course was pressed only adds the FMC's runway ILS to the status, silently
/// (<see cref="AppendStatus"/>). The window stays open for the next value; Escape closes it (which stops its
/// fill) and gives focus back to the window that was in front, as the value dialogs do.
/// </summary>
public sealed class BBS757NavRadiosForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<string, string?> _setIlsCourse;
    private readonly Func<string, string?> _setIlsFrequency;
    private readonly Action _parkIls;
    private readonly IntPtr _previousWindow;

    private TextBox _statusBox = null!;
    private TextBox _ilsCourseBox = null!;
    private TextBox _ilsFrequencyBox = null!;

    /// <param name="setIlsCourse">Takes the typed ILS course; returns the error to say, or null when the set started.</param>
    /// <param name="setIlsFrequency">Takes the typed ILS frequency; returns the error to say, or null when the set started.</param>
    /// <param name="parkIls">Parks the ILS; the definition says what came of it.</param>
    public BBS757NavRadiosForm(ScreenReaderAnnouncer announcer, Func<string, string?> setIlsCourse,
        Func<string, string?> setIlsFrequency, Action parkIls)
    {
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _setIlsCourse = setIlsCourse;
        _setIlsFrequency = setIlsFrequency;
        _parkIls = parkIls;
        BuildForm();
    }

    private void BuildForm()
    {
        Text = "757 Nav Radios";
        AccessibleName = "757 Nav Radios";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        KeyPreview = true;

        const int left = 15, width = 360, h = 28, gap = 10;
        int y = 15, tab = 0;

        Controls.Add(new Label { Text = "Status", Location = new Point(left, y), AutoSize = true, TabIndex = tab++ });
        y += 20;
        _statusBox = new TextBox
        {
            Location = new Point(left, y),
            Size = new Size(width, 110),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Nav radios status",
            TabIndex = tab++,
        };
        Controls.Add(_statusBox);
        y += 110 + gap + 6;

        Controls.Add(new Label { Text = "&ILS course", Location = new Point(left, y + 5), Size = new Size(120, 20), TabIndex = tab++ });
        _ilsCourseBox = new TextBox
        {
            Location = new Point(left + 130, y + 2),
            Size = new Size(110, 25),
            AccessibleName = "ILS course",
            AccessibleDescription = "Type the course in degrees, 0 to 360, and press Enter to set it",
            TabIndex = tab++,
        };
        _ilsCourseBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Alt || e.Control) return;
            e.Handled = true;
            e.SuppressKeyPress = true;   // no ding
            ApplyIlsCourse(_ilsCourseBox.Text);
        };
        Controls.Add(_ilsCourseBox);
        y += h + gap + 6;

        Controls.Add(new Label { Text = "ILS &frequency", Location = new Point(left, y + 5), Size = new Size(120, 20), TabIndex = tab++ });
        _ilsFrequencyBox = new TextBox
        {
            Location = new Point(left + 130, y + 2),
            Size = new Size(110, 25),
            AccessibleName = "ILS frequency",
            AccessibleDescription = "Type the frequency, 108.10 to 111.95, and press Enter to set it",
            TabIndex = tab++,
        };
        _ilsFrequencyBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Alt || e.Control) return;
            e.Handled = true;
            e.SuppressKeyPress = true;   // no ding
            ApplyIlsFrequency(_ilsFrequencyBox.Text);
        };
        Controls.Add(_ilsFrequencyBox);
        y += h + gap + 6;

        var park = new Button
        {
            Text = "ILS &park",
            AccessibleName = "ILS park",
            AccessibleDescription = "Turns the ILS control panel's megahertz knob to PARK",
            Location = new Point(left, y),
            Size = new Size(width, h),
            TabIndex = tab++,
        };
        park.Click += (_, _) => ParkIls();
        Controls.Add(park);
        y += h + gap;

        var close = new Button { Text = "Close", Location = new Point(left, y), Size = new Size(width, h), TabIndex = tab++ };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        ClientSize = new Size(left + width + left, y + h + 15);

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
            _statusBox.Focus();
        };
    }

    /// <summary>
    /// Enter in the ILS course box: hand the text over. Only that box is applied (R55). A value the
    /// definition cannot take is said and shown, and the text is selected for retyping.
    /// </summary>
    internal void ApplyIlsCourse(string typed)
    {
        ShowStatus("Setting the ILS course.");   // before the set starts: its own result may follow at once
        string? error = _setIlsCourse(typed.Trim());
        if (error != null)
        {
            ShowStatus(error);
            _announcer.AnnounceImmediate(error);
        }
        _ilsCourseBox.SelectAll();
    }

    /// <summary>
    /// Enter in the ILS frequency box: hand the text over. Only that box is applied (R55). A value the
    /// definition cannot take is said and shown, and the text is selected for retyping.
    /// </summary>
    internal void ApplyIlsFrequency(string typed)
    {
        ShowStatus("Setting the ILS frequency.");   // before the set starts: its own result may follow at once
        string? error = _setIlsFrequency(typed.Trim());
        if (error != null)
        {
            ShowStatus(error);
            _announcer.AnnounceImmediate(error);
        }
        _ilsFrequencyBox.SelectAll();
    }

    /// <summary>The ILS park button: the definition parks the ILS and says what came of it.</summary>
    internal void ParkIls()
    {
        ShowStatus("Parking the ILS.");   // before the park starts: its own result may follow at once
        _parkIls();
    }

    /// <summary>The status box's text.</summary>
    internal string StatusText => _statusBox.Text;

    /// <summary>
    /// Shows <paramref name="text"/> in the status box (it does not speak: the definition speaks what it
    /// reads). Safe from any thread; a closed window ignores it.
    /// </summary>
    public void ShowStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            SafeBeginInvoke(() => ShowStatus(text));
            return;
        }
        if (_statusBox.Text != text) _statusBox.Text = text;
    }

    /// <summary>
    /// Adds the sentence <paramref name="text"/> after what the status box shows, closing that with a full stop
    /// first when it has none ("ILS course 095" and "FMC runway ILS …." read "ILS course 095. FMC runway ILS …."),
    /// so the words already there stay. It does not speak. Safe from any thread; a closed window ignores it.
    /// </summary>
    public void AppendStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            SafeBeginInvoke(() => AppendStatus(text));
            return;
        }
        string shown = _statusBox.Text.TrimEnd();
        if (shown.Length == 0) _statusBox.Text = text;
        else _statusBox.Text = shown.EndsWith('.') ? $"{shown} {text}" : $"{shown}. {text}";
    }

    // ObjectDisposedException derives from InvalidOperationException, so one catch covers both; the
    // IsHandleCreated check alone races a handle destroy ([A380C-6]).
    private void SafeBeginInvoke(Action action)
    {
        try { if (IsHandleCreated) BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }
}
