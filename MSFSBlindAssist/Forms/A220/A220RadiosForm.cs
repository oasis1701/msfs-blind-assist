using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Synaptic A220 — NAV radios, course and nav source (Ctrl+N).
///
/// Reads and writes through the aircraft's own AFDX CommBus calls (see
/// <see cref="A220RadioState"/>): the course goes to the captain CTP's CRS
/// ("A22X.L CTP Action" SetConfig), NAV PRESET frequencies and AUTO/MAN tuning to
/// the CNS NAV CONTROL ("A22X.Display Tune"). NAV ACTIVE is never written — manual
/// active tuning breaks the A220's NAV-to-NAV transfer (docs/a220.md invariant);
/// with tuning on AUTO the FMS tunes the approach's ILS itself. The nav source has
/// no set command — only the CTP NAV button, which steps FMS ↔ radio — so the combo
/// presses it until the source is the one picked.
///
/// Screen-reader rules: picks and presses are not announced; numeric entries are
/// confirmed from the aircraft's read-back, and a change that did not take is an
/// error and is spoken.
/// </summary>
public sealed class A220RadiosForm : Form
{
    private readonly Func<string, Task<string?>> _agent;
    private readonly Action _pressNavSource;
    private readonly ScreenReaderAnnouncer _announcer;

    private readonly ListBox _status;
    private readonly TextBox _course;
    private readonly ComboBox _source;
    private readonly TextBox _nav1Preset;
    private readonly TextBox _nav2Preset;
    private readonly ComboBox _nav1Tuning;
    private readonly ComboBox _nav2Tuning;
    private readonly System.Windows.Forms.Timer _poll;
    private A220RadioState? _state;
    private bool _pollBusy;
    private bool _populating;
    private bool _seeded;

    public A220RadiosForm(Func<string, Task<string?>> agent, Action pressNavSource, ScreenReaderAnnouncer announcer)
    {
        _agent = agent;
        _pressNavSource = pressNavSource;
        _announcer = announcer;

        Text = "A220 NAV Radios and Course";
        Size = new Size(640, 520);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;
        Label L(string text) { var l = new Label { Text = text, Location = new Point(12, y + 4), AutoSize = true }; Controls.Add(l); return l; }

        L("&Status");
        _status = new ListBox { Location = new Point(12, y + 24), Size = new Size(600, 110), IntegralHeight = false, AccessibleName = "Status" };
        Controls.Add(_status);
        y += 145;

        L("Captain &course (degrees, Enter to set)");
        _course = new TextBox { Location = new Point(330, y), Size = new Size(100, 24), AccessibleName = "Captain course" };
        _course.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SetCourseAsync(); } };
        Controls.Add(_course);
        y += 36;

        L("Captain nav &source");
        _source = new ComboBox { Location = new Point(330, y), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Captain nav source" };
        _source.Items.AddRange(new object[] { "FMS", "Radio (VOR or LOC)" });
        _source.SelectedIndexChanged += (_, _) => { if (!_populating) _ = SetSourceAsync(_source.SelectedIndex == 1); };
        Controls.Add(_source);
        y += 36;

        L("NAV &1 preset frequency (MHz, Enter to set)");
        _nav1Preset = new TextBox { Location = new Point(330, y), Size = new Size(100, 24), AccessibleName = "NAV 1 preset frequency" };
        _nav1Preset.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SetPresetAsync(3, _nav1Preset); } };
        Controls.Add(_nav1Preset);
        y += 36;

        L("NAV 1 &tuning");
        _nav1Tuning = TuningCombo(y, "NAV 1 tuning", 3);
        y += 36;

        L("NAV &2 preset frequency (MHz, Enter to set)");
        _nav2Preset = new TextBox { Location = new Point(330, y), Size = new Size(100, 24), AccessibleName = "NAV 2 preset frequency" };
        _nav2Preset.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SetPresetAsync(4, _nav2Preset); } };
        Controls.Add(_nav2Preset);
        y += 36;

        L("NAV 2 t&uning");
        _nav2Tuning = TuningCombo(y, "NAV 2 tuning", 4);
        y += 40;

        var hint = new Label
        {
            Location = new Point(12, y), Size = new Size(600, 48),
            Text = "With tuning on AUTO the FMS tunes the approach's ILS and course by itself. " +
                   "Presets are the frequencies waiting in the NAV CONTROL page; the aircraft makes them active."
        };
        Controls.Add(hint);
        var close = new Button { Text = "&Close", Location = new Point(12, y + 54), Size = new Size(100, 30) };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        CancelButton = close;

        _poll = new System.Windows.Forms.Timer { Interval = 1000 };
        _poll.Tick += async (_, _) => await PollAsync();
        FormClosed += (_, _) => _poll.Stop();
    }

    private ComboBox TuningCombo(int y, string name, int index)
    {
        var c = new ComboBox { Location = new Point(330, y), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name };
        c.Items.AddRange(new object[] { "AUTO", "MANUAL" });
        c.SelectedIndexChanged += (_, _) => { if (!_populating) _ = SetTuningAsync(index, c.SelectedIndex == 0); };
        Controls.Add(c);
        return c;
    }

    public async Task OpenAsync()
    {
        Show();
        Activate();
        _status.Items.Add("Reading the radios…");
        _status.Focus();
        await PollAsync();
        _poll.Start();
    }

    private async Task PollAsync()
    {
        if (_pollBusy || IsDisposed) return;
        _pollBusy = true;
        try
        {
            var st = A220RadioState.Parse(await _agent("radios()"));
            if (st == null || IsDisposed) return;
            _state = st;
            ReplaceItems(_status, st.Describe());
            SeedInputs(st);
        }
        catch { }
        finally { _pollBusy = false; }
    }

    /// <summary>Fill the entry fields once (the pilot edits from the current values);
    /// the combos follow the aircraft whenever they are not focused.</summary>
    private void SeedInputs(A220RadioState st)
    {
        if (!st.LinkUp) return;
        _populating = true;
        try
        {
            if (!_seeded)
            {
                _seeded = true;
                double? crs = st.CtpCourse ?? st.Course;
                if (crs != null) _course.Text = A220RadioState.Degrees(crs);
                if (st.Nav1?.PresetMhz is double p1) _nav1Preset.Text = A220RadioState.Mhz(p1);
                if (st.Nav2?.PresetMhz is double p2) _nav2Preset.Text = A220RadioState.Mhz(p2);
            }
            if (!_source.Focused && st.NavSource != null)
                _source.SelectedIndex = A220RadioState.IsRadioSource(st.NavSource) ? 1 : 0;
            if (!_nav1Tuning.Focused && st.Nav1?.AutoTune is bool a1) _nav1Tuning.SelectedIndex = a1 ? 0 : 1;
            if (!_nav2Tuning.Focused && st.Nav2?.AutoTune is bool a2) _nav2Tuning.SelectedIndex = a2 ? 0 : 1;
        }
        finally { _populating = false; }
    }

    private async Task SetCourseAsync()
    {
        if (!A220RadioState.TryParseCourse(_course.Text, out int course))
        {
            _announcer.AnnounceImmediate("Course must be a whole number from 1 to 360.");
            return;
        }
        string? r = await _agent($"setCourse(1,{course.ToString(CultureInfo.InvariantCulture)})");
        if (r != "SENT") { _announcer.AnnounceImmediate("Could not reach the aircraft to set the course."); return; }
        await Task.Delay(700);
        await PollAsync();
        double? now = _state?.CtpCourse ?? _state?.Course;
        if (now is double d && ((int)Math.Round(d) % 360) == course % 360)
            _announcer.AnnounceImmediate($"Course {A220RadioState.Degrees(d)} set.");
        else
            _announcer.AnnounceImmediate($"The course did not change. It reads {A220RadioState.Degrees(now)}.");
    }

    private async Task SetPresetAsync(int index, TextBox box)
    {
        string name = index == 3 ? "NAV 1" : "NAV 2";
        if (!A220RadioState.TryParseNavMhz(box.Text, out double mhz))
        {
            _announcer.AnnounceImmediate("Frequency must be between 108.00 and 117.95.");
            return;
        }
        long hz = (long)Math.Round(mhz * 1_000_000.0);
        string? r = await _agent($"setNavPreset({index},{hz.ToString(CultureInfo.InvariantCulture)})");
        if (r != "SENT") { _announcer.AnnounceImmediate($"Could not reach the aircraft to set {name}."); return; }
        await Task.Delay(700);
        await PollAsync();
        var nav = index == 3 ? _state?.Nav1 : _state?.Nav2;
        double? now = nav?.PresetMhz;
        if (now is double d && Math.Abs(d - mhz) < 0.004)
            _announcer.AnnounceImmediate($"{name} preset {A220RadioState.Mhz(d)} set.");
        else if (now == null)
            _announcer.AnnounceImmediate($"{name} preset sent; the aircraft did not report it back.");
        else
            _announcer.AnnounceImmediate($"{name} preset did not change. It reads {A220RadioState.Mhz(now)}.");
    }

    private async Task SetTuningAsync(int index, bool auto)
    {
        string? r = await _agent($"setNavAutotune({index},{(auto ? "true" : "false")})");
        if (r != "SENT") { _announcer.AnnounceImmediate("Could not reach the aircraft to change the tuning mode."); return; }
        await Task.Delay(700);
        await PollAsync();
        var nav = index == 3 ? _state?.Nav1 : _state?.Nav2;
        if (nav?.AutoTune is bool now && now != auto)
            _announcer.AnnounceImmediate($"{(index == 3 ? "NAV 1" : "NAV 2")} tuning did not change.");
    }

    /// <summary>Press the CTP NAV button until the source class matches (at most 3
    /// presses — the button steps through the sources the aircraft offers).</summary>
    private async Task SetSourceAsync(bool radio)
    {
        for (int i = 0; i < 3; i++)
        {
            if (_state?.NavSource is int s && A220RadioState.IsRadioSource(s) == radio) return;
            _pressNavSource();
            await Task.Delay(900);
            await PollAsync();
        }
        if (_state?.NavSource is int final && A220RadioState.IsRadioSource(final) != radio)
            _announcer.AnnounceImmediate($"Nav source did not change. It is {A220RadioState.SourceName(final)}.");
    }

    private static void ReplaceItems(ListBox box, IList<string> lines)
    {
        bool same = box.Items.Count == lines.Count;
        for (int i = 0; same && i < lines.Count; i++) same = (string)box.Items[i] == lines[i];
        if (same) return;
        int sel = box.SelectedIndex;
        box.BeginUpdate();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i < box.Items.Count) { if ((string)box.Items[i] != lines[i]) box.Items[i] = lines[i]; }
            else box.Items.Add(lines[i]);
        }
        while (box.Items.Count > lines.Count) box.Items.RemoveAt(box.Items.Count - 1);
        box.EndUpdate();
        if (box.Items.Count > 0) box.SelectedIndex = Math.Clamp(sel < 0 ? 0 : sel, 0, box.Items.Count - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _poll.Dispose();
        base.Dispose(disposing);
    }
}
