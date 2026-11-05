// Characterization tests for the live window every display of the Fenix display channel uses
// (Forms/Fenix/FenixChannelDisplayForm.cs), and for the two ways the definition opens one: ShowChannelWindow, which a
// Fenix windows list entry calls, and OpenChannelWindow, which a display key calls. The window is built, never shown:
// a DisplayListBox and a WinForms timer construct headless in this xUnit host (FenixCabinWindowTests), and the
// window's first open, the part of ShowForm that reads and selects and starts the timer, is StartLive, so it runs
// without putting anything on screen. What only the live sim shows (focus landing on the list, the window coming to
// the front from a hotkey) is checked there ([CORE-5]).
//
// The contract: the window is titled "Fenix <display>", holds one list named "<display> display" that fills it, and
// says nothing itself, since the refreshed rows are what the pilot reads ([CORE-7]). Its rows come from RowsFor, the
// testable core: a display that is not trusted gives one row "<display>: not available, <reason>" and its data is
// never composed ([FNX-22]); a composer that says not available, or gives no rows or a list holding a null, or throws,
// gives one row too ("no data" for no rows or a null, "read failed" for a throw), never an empty list a screen reader
// cannot land on and never a crash; a throw is also reported once through the monitor, under the display's name. A
// refresh reconciles the list in place, so the pilot keeps their row, and only the pilot's own open moves focus. The
// first refresh happens at the first open, before the window shows, and selects row 0; the timer starts then, ticks
// every second, and ends with the window; a refresh after the window is disposed does nothing. A derived window is
// asked about every key before the base is (Escape closes, unless a combo's list is open, and then that list has the
// key; F5 refreshes once per press, however long it is held), and controls it adds go after the list, in the order
// added, in the tab order too. A display key opens its window while the display is trusted at the press, or dark (the
// channel is healthy and the display itself is off: the window then shows its one status row), and leaves both hotkey
// modes first; for any other reason the display is not trusted the key does nothing, and goes on to its AI read. A
// window list entry opens it whatever the verdict.
//
// Rows are built from the verdict's own words and the composer's, never formatted from a number here, so the status
// rows read the same where the PC's culture writes a comma.

using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelWindowTests
{
    // ---- What the list shows --------------------------------------------------------------------

    private static FenixChannelSnapshot Untrusted(string reason)
        => new(new FenixDisplayVerdict(false, reason), null, null, null, 0, 1);

    private static FenixChannelSnapshot Trusted()
        => new(new FenixDisplayVerdict(true, "trusted"), null, null, null, 0, 1);

    [Theory]
    [InlineData("PFD", "display off", "PFD: not available, display off")]
    [InlineData("E/WD", "no data yet", "E/WD: not available, no data yet")]
    [InlineData("DCDU", "stream stalled", "DCDU: not available, stream stalled")]
    [InlineData("ISIS", "channel not answering: connection refused",
        "ISIS: not available, channel not answering: connection refused")]
    public void An_untrusted_display_shows_one_status_row(string displayName, string reason, string expected)
    {
        bool composed = false;
        var reported = new List<string>();

        IReadOnlyList<string> rows = FenixChannelDisplayForm.RowsFor(displayName, Untrusted(reason), _ =>
        {
            composed = true;
            return FenixChannelRows.Of(["a row of a display that is not trusted"]);
        }, reported.Add);

        Assert.Equal(new[] { expected }, rows);
        // The gate has said no: nothing of the display is composed, so nothing of it can reach the list.
        Assert.False(composed, "the data of a display that is not trusted was composed");
        Assert.Empty(reported);
    }

    [Fact]
    public void A_compose_problem_shows_one_status_row()
    {
        var reported = new List<string>();

        IReadOnlyList<string> rows = FenixChannelDisplayForm.RowsFor("PFD", Trusted(),
            _ => FenixChannelRows.NotAvailable("field 125.2 has the wrong shape"), reported.Add);

        Assert.Equal(new[] { "PFD: not available, field 125.2 has the wrong shape" }, rows);
        // A composer reports a problem it returns itself (once, under its own feature's name); the window reports
        // only what it catches.
        Assert.Empty(reported);

        // A result that holds rows and a problem is read as the problem, never as rows to show.
        rows = FenixChannelDisplayForm.RowsFor("PFD", Trusted(),
            _ => new FenixChannelRows(["SPD 250"], "no speed limits"));
        Assert.Equal(new[] { "PFD: not available, no speed limits" }, rows);
    }

    [Fact]
    public void A_compose_with_no_rows_and_no_problem_reads_as_no_data()
    {
        // The window never shows an empty list: a screen reader has nowhere to land on one. A result that was never
        // filled in (default), and a composer that found nothing to show, both read as no data.
        string[] expected = ["ND: not available, no data"];

        Assert.Equal(expected, FenixChannelDisplayForm.RowsFor("ND", Trusted(), _ => default));
        Assert.Equal(expected, FenixChannelDisplayForm.RowsFor("ND", Trusted(), _ => FenixChannelRows.Of([])));
        Assert.Equal(expected, FenixChannelDisplayForm.RowsFor("ND", Trusted(),
            _ => new FenixChannelRows(Array.Empty<string>(), null)));
    }

    [Fact]
    public void A_compose_whose_list_holds_a_null_reads_as_no_data()
    {
        // The list box throws on a null row, and it would do so on the timer's tick, past the try that catches a
        // composer's throw. So a list holding one is no data, whichever row it is, and it never reaches the list box.
        string[] expected = ["ND: not available, no data"];

        Assert.Equal(expected, FenixChannelDisplayForm.RowsFor("ND", Trusted(),
            _ => FenixChannelRows.Of([null!])));
        Assert.Equal(expected, FenixChannelDisplayForm.RowsFor("ND", Trusted(),
            _ => FenixChannelRows.Of(["a row", null!, "another row"])));

        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        using FenixChannelDisplayForm form = NewForm(monitor, _ => FenixChannelRows.Of(["SPD 250", null!]));

        form.StartLive();

        Assert.Equal(new[] { "PFD: not available, no data" }, ItemsOf(ListOf(form)));
    }

    // A window whose controls' refresh throws, as a derived window's could.
    private sealed class ThrowingControlsForm(FenixChannelMonitor monitor)
        : FenixChannelDisplayForm("PFD", FenixChannelDisplay.Pfd, monitor, _ => FenixChannelRows.Of(["a row"]))
    {
        protected override void OnRefresh(FenixChannelSnapshot s) => throw new InvalidOperationException("no header");
    }

    [Fact]
    public void A_refresh_of_the_controls_that_throws_costs_only_the_controls_and_is_reported_once()
    {
        // It runs on the window's timer: a throw that left it would reach the app's UI-thread handler every second.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        using var form = new ThrowingControlsForm(monitor);

        form.StartLive();
        form.RefreshNow();

        Assert.Equal(new[] { "PFD: not available, channel not answering" }, ItemsOf(ListOf(form)));
        Assert.Contains("PFD: controls failed: InvalidOperationException: no header", lines.All);
    }

    [Fact]
    public void A_throwing_compose_shows_a_status_row_and_is_reported_once()
    {
        // The monitor writes each distinct line once, so a composer that throws at every refresh is one line in the
        // log, however many seconds the window stays open.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);

        for (int refresh = 0; refresh < 3; refresh++)
        {
            IReadOnlyList<string> rows = FenixChannelDisplayForm.RowsFor("PFD", Trusted(),
                _ => throw new InvalidOperationException("no speed limits"),
                problem => monitor.ReportFeatureProblem("PFD", problem));

            Assert.Equal(new[] { "PFD: not available, read failed" }, rows);
        }

        Assert.Equal(new[] { "PFD: read failed: InvalidOperationException: no speed limits" }, lines.All);
    }

    [Fact]
    public void A_throw_with_nowhere_to_report_it_still_shows_the_status_row()
    {
        // The report is optional: a caller that passes none still gets the row, and nothing escapes.
        IReadOnlyList<string>? rows = null;

        Exception? escaped = Record.Exception(() => rows = FenixChannelDisplayForm.RowsFor(
            "SD", Trusted(), _ => throw new NullReferenceException()));

        Assert.Null(escaped);
        Assert.Equal(new[] { "SD: not available, read failed" }, rows);
    }

    [Fact]
    public void A_trusted_display_shows_its_rows()
    {
        FenixChannelSnapshot snapshot = Trusted();
        FenixChannelSnapshot? composedFrom = null;
        var reported = new List<string>();

        IReadOnlyList<string> rows = FenixChannelDisplayForm.RowsFor("PFD", snapshot, s =>
        {
            composedFrom = s;
            return FenixChannelRows.Of(["SPD 250", "ALT 10000", "HDG 090"]);
        }, reported.Add);

        // Top to bottom, in the order the composer gave them, from the very snapshot the verdict came with: the
        // window takes its verdict and its data from one snapshot, never two reads ([FNX-22]).
        Assert.Equal(new[] { "SPD 250", "ALT 10000", "HDG 090" }, rows);
        Assert.Equal(snapshot, composedFrom);
        Assert.Empty(reported);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_status_rows_read_the_same_under_a_comma_decimal_culture(string culture)
    {
        // A status row is spoken as written: the verdict's words, the composer's words and the exception's, none of
        // them formatted here, so a number inside one keeps its decimal point where the PC's culture writes a comma.
        string[] rows = TestCulture.Run(culture, () =>
        {
            var reported = new List<string>();
            var all = new List<string>();
            all.AddRange(FenixChannelDisplayForm.RowsFor("PFD", Untrusted("UI thread stalled 1.6 s"), _ => default));
            all.AddRange(FenixChannelDisplayForm.RowsFor("ND", Trusted(),
                _ => FenixChannelRows.NotAvailable("field 125.2 has the wrong shape")));
            all.AddRange(FenixChannelDisplayForm.RowsFor("SD", Trusted(),
                _ => throw new InvalidOperationException("speed 250.5 is outside 0.0-1.0"), reported.Add));
            all.AddRange(reported);
            return all.ToArray();
        });

        Assert.Equal(
            new[]
            {
                "PFD: not available, UI thread stalled 1.6 s",
                "ND: not available, field 125.2 has the wrong shape",
                "SD: not available, read failed",
                "read failed: InvalidOperationException: speed 250.5 is outside 0.0-1.0",
            },
            rows);
    }

    // ---- The window -----------------------------------------------------------------------------

    private static FenixChannelDisplayForm NewForm(
        FenixChannelMonitor monitor, Func<FenixChannelSnapshot, FenixChannelRows>? compose = null)
        => new("PFD", FenixChannelDisplay.Pfd, monitor, compose ?? (_ => FenixChannelRows.Of(["a row"])));

    private static DisplayListBox ListOf(Control form) => form.Controls.OfType<DisplayListBox>().Single();

    private static string[] ItemsOf(ListBox list) => list.Items.Cast<string>().ToArray();

    private static System.Windows.Forms.Timer TimerOf(FenixChannelDisplayForm form)
        => (System.Windows.Forms.Timer)typeof(FenixChannelDisplayForm)
            .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    // A monitor the PFD trusts: the data call is connected, the build is checked, and one good push has arrived.
    private static FenixChannelMonitor TrustedPfdMonitor(MonitorTestKit.Lines lines)
    {
        FenixChannelMonitor monitor = ConnectedMonitor(lines);
        FeedTrusted(monitor);
        return monitor;
    }

    private static FenixChannelMonitor ConnectedMonitor(MonitorTestKit.Lines lines)
    {
        FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        Connect(monitor, lines);
        return monitor;
    }

    // Connects the data call of a monitor that has been idle, and waits for its build to be checked, which is asked on
    // a pool thread. No push has arrived yet.
    private static void Connect(FenixChannelMonitor monitor, MonitorTestKit.Lines lines)
    {
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.True(
            SpinWait.SpinUntil(
                () => lines.All.Contains($"build {MonitorTestKit.CheckedBuild}: checked"), MonitorTestKit.Generous),
            "the build was never checked. The log: " + lines);
    }

    // One good push, so that a test which refreshes more than once never meets "stream stalled" (the gate gives up
    // on a stream after 2 s without a message).
    private static void FeedTrusted(FenixChannelMonitor monitor)
    {
        monitor.OnDataMessage(MonitorTestKit.GoodPush());
        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), monitor.VerdictFor(FenixChannelDisplay.Pfd));
    }

    [Theory]
    [InlineData("PFD")]
    [InlineData("ND")]
    [InlineData("E/WD")]
    [InlineData("SD")]
    [InlineData("ISIS")]
    [InlineData("DCDU")]
    public void The_list_is_named_for_its_display(string displayName)
    {
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new FenixChannelDisplayForm(displayName, FenixChannelDisplay.Pfd, monitor,
            _ => FenixChannelRows.Of(["a row"]));

        Assert.Equal("Fenix " + displayName, form.Text);

        // The list is the only control until a derived window adds one: it fills the window, is the first stop of the
        // tab order, and is named for the display, which is what the reader says when focus lands on it.
        DisplayListBox list = Assert.IsType<DisplayListBox>(Assert.Single(form.Controls));
        Assert.Equal(displayName + " display", list.AccessibleName);
        Assert.Equal(DockStyle.Fill, list.Dock);
        Assert.Equal(0, list.TabIndex);
        Assert.True(list.TabStop);
    }

    [Fact]
    public void The_first_open_reads_the_rows_selects_the_first_and_starts_the_timer()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        using FenixChannelDisplayForm form = NewForm(monitor, _ => FenixChannelRows.Of(["SPD 250", "ALT 10000"]));
        DisplayListBox list = ListOf(form);
        System.Windows.Forms.Timer timer = TimerOf(form);
        Assert.Equal(1000, timer.Interval);
        Assert.False(timer.Enabled);

        form.StartLive();

        // The rows are there before the window shows, and the reader will land on the first.
        Assert.Equal(new[] { "SPD 250", "ALT 10000" }, ItemsOf(list));
        Assert.Equal(0, list.SelectedIndex);
        Assert.True(timer.Enabled);
    }

    [Fact]
    public void A_second_open_does_not_read_again_or_move_the_pilot()
    {
        // A second press of the key brings the open window forward. It must not select row 0 again, whatever row the
        // pilot had reached, nor start a second timer.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        int composed = 0;
        using FenixChannelDisplayForm form = NewForm(monitor, _ =>
        {
            composed++;
            return FenixChannelRows.Of(["SPD 250", "ALT 10000", "HDG 090"]);
        });
        form.StartLive();
        ListOf(form).SelectedIndex = 2;

        form.StartLive();

        Assert.Equal(1, composed);
        Assert.Equal(2, ListOf(form).SelectedIndex);
    }

    [Fact]
    public void A_refresh_keeps_the_row_the_pilot_is_on()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        var rows = new List<string> { "SPD 250", "ALT 10000", "HDG 090" };
        using FenixChannelDisplayForm form = NewForm(monitor, _ => FenixChannelRows.Of(rows.ToArray()));
        DisplayListBox list = ListOf(form);
        form.StartLive();
        list.SelectedIndex = 2;

        // Only the first row changed. The pilot is on the third, and stays there.
        rows[0] = "SPD 251";
        FeedTrusted(monitor);
        form.RefreshNow();

        Assert.Equal(new[] { "SPD 251", "ALT 10000", "HDG 090" }, ItemsOf(list));
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void A_display_that_stops_being_trusted_gets_one_status_row_in_place_of_the_rows()
    {
        // Never stale rows: the moment the verdict turns, the content goes and the one status row stands in its
        // place, with the reader's place kept inside it. When the display is trusted again the rows come back.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        using FenixChannelDisplayForm form = NewForm(monitor, _ => FenixChannelRows.Of(["SPD 250", "ALT 10000"]));
        DisplayListBox list = ListOf(form);

        // Nothing has connected: the status row, with the gate's own reason in it.
        form.StartLive();
        string reason = monitor.VerdictFor(FenixChannelDisplay.Pfd).Reason;
        Assert.Equal(new[] { $"PFD: not available, {reason}" }, ItemsOf(list));
        Assert.Equal(0, list.SelectedIndex);

        // The display comes up: its rows replace the status row.
        Connect(monitor, lines);
        FeedTrusted(monitor);
        form.RefreshNow();
        Assert.Equal(new[] { "SPD 250", "ALT 10000" }, ItemsOf(list));
        Assert.Equal(0, list.SelectedIndex);
        list.SelectedIndex = 1;

        // The channel stops answering: the rows are gone at the next refresh, and the pilot lands on the one row.
        monitor.OnDataState(FenixChannelState.NotAnswering, "gone");
        form.RefreshNow();
        Assert.Equal(new[] { "PFD: not available, channel not answering: gone" }, ItemsOf(list));
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void The_window_reports_a_throwing_compose_under_its_own_name_once()
    {
        // The form wires RowsFor's report to the monitor under the display's name, so three refreshes of a composer
        // that throws are one line in the channel log, and the list shows the status row.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        using FenixChannelDisplayForm form = NewForm(monitor, _ => throw new InvalidOperationException("boom"));

        form.StartLive();
        for (int refresh = 0; refresh < 2; refresh++)
        {
            FeedTrusted(monitor);
            form.RefreshNow();
        }

        Assert.Equal(new[] { "PFD: not available, read failed" }, ItemsOf(ListOf(form)));
        Assert.Equal(new[] { "PFD: read failed: InvalidOperationException: boom" }, lines.Starting("PFD: "));
    }

    [Fact]
    public void A_refresh_after_the_window_is_disposed_does_nothing()
    {
        // A tick queued as the window closes, or a derived window's late call, must not touch a list that is gone.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        int composed = 0;
        FenixChannelDisplayForm form = NewForm(monitor, _ =>
        {
            composed++;
            return FenixChannelRows.Of(["SPD 250"]);
        });
        form.StartLive();
        Assert.Equal(1, composed);
        form.Dispose();

        Exception? escaped = Record.Exception(() => form.RefreshNow());

        Assert.Null(escaped);
        Assert.Equal(1, composed);
    }

    [Fact]
    public void The_timer_stops_when_the_window_closes()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        using FenixChannelDisplayForm form = NewForm(monitor);
        System.Windows.Forms.Timer timer = TimerOf(form);
        form.StartLive();
        Assert.True(timer.Enabled);
        _ = form.Handle;   // a handle-less Form.Close() raises nothing

        form.Close();

        Assert.False(timer.Enabled);
        Assert.True(form.IsDisposed);
    }

    [Fact]
    public void Only_the_pilots_own_open_moves_focus()
    {
        // A refresh runs every second and must never take focus or move the reader: the focus calls are in ShowForm
        // alone, the pilot's own open, and nowhere else in the window.
        string source = StripComments(File.ReadAllText(SourcePath("Forms", "Fenix", "FenixChannelDisplayForm.cs")));
        int open = source.IndexOf("public void ShowForm()", StringComparison.Ordinal);
        Assert.True(open >= 0, "the window has no ShowForm");
        string showForm = BlockAfter(source, open);
        string rest = source.Remove(source.IndexOf(showForm, open, StringComparison.Ordinal), showForm.Length);

        Assert.Contains(".Focus()", showForm);
        foreach (string call in new[] { "Focus(", "Activate(", "BringToFront(", "Select(", "SelectNextControl(" })
            Assert.False(rest.Contains(call, StringComparison.Ordinal), $"outside ShowForm the window calls {call}");
    }

    // ---- Keys -----------------------------------------------------------------------------------

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const long KeyWasDown = 0x40000000;   // bit 30 of a key message's lParam: an auto-repeat

    private static readonly MethodInfo ProcessCmdKey =
        typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // What the window's ProcessCmdKey makes of a key press, as the message loop would hand it over.
    private static bool Press(Form form, Keys keyData, bool autoRepeat = false, int message = WM_KEYDOWN)
    {
        var msg = Message.Create(IntPtr.Zero, message, (IntPtr)(int)(keyData & Keys.KeyCode),
            (IntPtr)(autoRepeat ? KeyWasDown | 1 : 1));
        object?[] args = { msg, keyData };
        return (bool)ProcessCmdKey.Invoke(form, args)!;
    }

    // A derived window of the tests: it records the keys it is asked about and takes the ones it is told to.
    private sealed class KeyedForm : FenixChannelDisplayForm
    {
        public KeyedForm(FenixChannelMonitor monitor, Func<FenixChannelSnapshot, FenixChannelRows> compose)
            : base("PFD", FenixChannelDisplay.Pfd, monitor, compose)
        {
        }

        public List<Keys> Asked { get; } = new();

        public HashSet<Keys> Takes { get; } = new();

        public void Add(Control control) => AddControlAfterList(control);

        protected override bool HandleWindowKey(Keys keyData)
        {
            Asked.Add(keyData);
            return Takes.Contains(keyData);
        }
    }

    [Fact]
    public void Escape_closes_the_window()
    {
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using FenixChannelDisplayForm form = NewForm(monitor);
        var closed = false;
        form.FormClosed += (_, _) => closed = true;
        _ = form.Handle;   // a handle-less Form.Close() raises nothing

        bool handled = Press(form, Keys.Escape);

        Assert.True(handled);
        Assert.True(closed);
    }

    // A combo whose list reads as open with nothing on the screen: DroppedDown asks the control's own window, and this
    // one answers as the test tells it to.
    private sealed class ComboWithAList : ComboBox
    {
        private const int CB_GETDROPPEDSTATE = 0x0157;

        public bool ListOpen;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == CB_GETDROPPEDSTATE)
            {
                m.Result = ListOpen ? 1 : 0;
                return;
            }

            base.WndProc(ref m);
        }
    }

    [Fact]
    public void Escape_leaves_an_open_combo_list_before_it_closes_the_window()
    {
        // A derived window's combo: a pilot pressing Escape to cancel its dropped list must not lose the whole window.
        // Not taken here, so the key goes on to the combo, which closes its list; the next Escape closes the window.
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new KeyedForm(monitor, _ => FenixChannelRows.Of(["a row"]));
        var combo = new ComboWithAList();
        form.Add(combo);
        _ = form.Handle;   // a handle-less Form.Close() raises nothing
        _ = combo.Handle;   // a handle-less combo reads its list as closed
        form.ActiveControl = combo;
        combo.ListOpen = true;
        Assert.True(combo.DroppedDown);

        Assert.False(Press(form, Keys.Escape));
        Assert.False(form.IsDisposed);

        combo.ListOpen = false;
        Assert.True(Press(form, Keys.Escape));
        Assert.True(form.IsDisposed);
    }

    [Fact]
    public void F5_refreshes_once_per_press_however_long_it_is_held()
    {
        // F5 reads the rows again, without a word. A held key repeats, and each repeat has bit 30 of its lParam set:
        // the repeats are taken (the list never sees F5) and refresh nothing.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        int composed = 0;
        using FenixChannelDisplayForm form = NewForm(monitor, _ =>
        {
            composed++;
            return FenixChannelRows.Of(["SPD 250"]);
        });
        form.StartLive();
        Assert.Equal(1, composed);

        FeedTrusted(monitor);
        Assert.True(Press(form, Keys.F5));
        Assert.Equal(2, composed);

        FeedTrusted(monitor);
        Assert.True(Press(form, Keys.F5, autoRepeat: true));
        Assert.True(Press(form, Keys.F5, autoRepeat: true));
        Assert.Equal(2, composed);

        FeedTrusted(monitor);
        Assert.True(Press(form, Keys.F5));
        Assert.Equal(3, composed);
    }

    [Fact]
    public void A_derived_window_is_asked_about_every_key_first_and_can_take_Escape_and_F5()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        int composed = 0;
        using var form = new KeyedForm(monitor, _ =>
        {
            composed++;
            return FenixChannelRows.Of(["SPD 250"]);
        });
        form.StartLive();
        _ = form.Handle;
        form.Takes.Add(Keys.Escape);
        form.Takes.Add(Keys.F5);

        // Taken before the base looks: Escape does not close, F5 does not refresh.
        Assert.True(Press(form, Keys.Escape));
        Assert.True(Press(form, Keys.F5));
        Assert.False(form.IsDisposed);
        Assert.Equal(1, composed);
        Assert.Equal(new[] { Keys.Escape, Keys.F5 }, form.Asked);

        // Not taken: the base does what it does.
        form.Takes.Clear();
        FeedTrusted(monitor);
        Assert.True(Press(form, Keys.F5));
        Assert.Equal(2, composed);
        Assert.True(Press(form, Keys.Escape));
        Assert.True(form.IsDisposed);
        Assert.Equal(new[] { Keys.Escape, Keys.F5, Keys.F5, Keys.Escape }, form.Asked);
    }

    [Fact]
    public void A_derived_window_is_asked_about_Alt_chords_and_an_untaken_key_goes_on_to_the_list()
    {
        // Alt+digit reaches a window as a system key message, not a plain one; it is a key press like any other.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        using var form = new KeyedForm(monitor, _ => FenixChannelRows.Of(["SPD 250"]));
        form.Takes.Add(Keys.Alt | Keys.D1);
        form.Takes.Add(Keys.Control | Keys.D2);

        Assert.True(Press(form, Keys.Alt | Keys.D1, message: WM_SYSKEYDOWN));
        Assert.True(Press(form, Keys.Control | Keys.D2));

        // The keys that move through the list belong to the list: the window takes none of them.
        foreach (Keys key in new[] { Keys.Down, Keys.Up, Keys.PageDown, Keys.PageUp, Keys.Home, Keys.End, Keys.Tab })
            Assert.False(Press(form, key), $"{key} was taken from the list");

        Assert.Equal(
            new[]
            {
                Keys.Alt | Keys.D1, Keys.Control | Keys.D2, Keys.Down, Keys.Up, Keys.PageDown, Keys.PageUp, Keys.Home,
                Keys.End, Keys.Tab,
            },
            form.Asked);
    }

    // ---- Controls after the list ----------------------------------------------------------------

    [Fact]
    public void Controls_a_derived_window_adds_go_after_the_list_in_the_order_added()
    {
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new KeyedForm(monitor, _ => FenixChannelRows.Of(["a row"]));
        var first = new Panel { Name = "first", Height = 28 };
        var second = new Button { Name = "second", Height = 32, Text = "Second" };

        form.Add(first);
        form.Add(second);

        // The list first, then the controls as they were added.
        DisplayListBox list = ListOf(form);
        Assert.Equal(new Control[] { list, first, second }, form.Controls.Cast<Control>().ToArray());

        // The tab order is the same order: the list first.
        Assert.Equal(
            new Control[] { list, first, second },
            form.Controls.Cast<Control>().OrderBy(control => control.TabIndex).ToArray());
        Assert.Equal(3, form.Controls.Cast<Control>().Select(control => control.TabIndex).Distinct().Count());

        // Below the list, each at the bottom edge: the list keeps the rest of the window and the controls read
        // top to bottom as they tab.
        Assert.Equal(DockStyle.Fill, list.Dock);
        Assert.Equal(DockStyle.Bottom, first.Dock);
        Assert.Equal(DockStyle.Bottom, second.Dock);
        form.PerformLayout();
        Assert.True(list.Bottom <= first.Top, $"the list ends at {list.Bottom} and the first control starts at {first.Top}");
        Assert.True(first.Bottom <= second.Top, $"the first control ends at {first.Bottom} and the second starts at {second.Top}");
        Assert.True(list.Height > 0);
    }

    // ---- Opening it from the definition ---------------------------------------------------------

    private static FenixA320Definition.ChannelKeyContext Context(SpeechCapture speech, FenixChannelMonitor? monitor)
        => new(null!, speech, null!, null!, monitor);

    private static FenixA320Definition.ChannelKeyContext Context(
        SpeechCapture speech, FenixChannelMonitor monitor, HotkeyManager hotkeys)
        => new(null!, speech, null!, hotkeys, monitor);

    // A monitor whose channel is healthy and whose displays are all dark: connected, the build checked, SimConnect's
    // values agreeing, and one push with no display unit powered.
    private static FenixChannelMonitor DarkMonitor(MonitorTestKit.Lines lines)
    {
        FenixChannelMonitor monitor = ConnectedMonitor(lines);
        monitor.OnDataMessage(FenixChannelProtoWriter.GoodPush(displaysOn: false));
        return monitor;
    }

    // Hands the definition the monitor a key's window is built on. In the app the key context's monitor and the
    // definition's own are one object, because HandleHotkeyAction makes the context from that field.
    private static void GiveMonitor(FenixA320Definition definition, FenixChannelMonitor monitor)
        => typeof(FenixA320Definition).GetField("_displayChannelMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(definition, monitor);

    [Fact]
    public void A_key_opens_no_window_for_a_display_that_is_not_trusted_and_not_dark()
    {
        // Any reason but a dark display leaves the key to the AI read with no extra words, as the PMDG 737's display
        // keys do, because the screen is live: false, nothing spoken, no window built, and nothing touched. The hotkey
        // manager is null here, so a call to it would throw.
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        int built = 0;

        void AssertRefused(FenixChannelMonitor monitor, FenixChannelDisplay display, string reason)
        {
            FenixDisplayVerdict verdict = monitor.VerdictFor(display);
            Assert.False(verdict.Trusted);
            Assert.StartsWith(reason, verdict.Reason);
            Assert.False(
                definition.OpenChannelWindow<FenixChannelDisplayForm>(Context(speech, monitor), display, "PFD", _ =>
                {
                    built++;
                    return null!;
                }),
                $"the {display} key opened a window on '{verdict.Reason}'");
        }

        // No monitor: before Start, after Stop.
        foreach (FenixChannelDisplay display in Enum.GetValues<FenixChannelDisplay>())
        {
            Assert.False(definition.OpenChannelWindow<FenixChannelDisplayForm>(
                Context(speech, null), display, "PFD", _ =>
                {
                    built++;
                    return null!;
                }));
        }

        // Each monitor has a log of its own: Connect waits for the line that says its build was checked.

        // A monitor that was never started has nothing connected.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        // The displays are dark and the channel stops answering: the channel-wide reason wins, so the screen is not
        // known to be dark and the key keeps its AI read.
        using FenixChannelMonitor stopped = DarkMonitor(new MonitorTestKit.Lines());
        stopped.OnDataState(FenixChannelState.NotAnswering, "gone");
        // The displays are dark and a push has a field in the wrong shape: the channel is latched until reconnect.
        using FenixChannelMonitor latched = DarkMonitor(new MonitorTestKit.Lines());
        latched.OnDataMessage(MonitorTestKit.WrongShapedPush());

        foreach (FenixChannelDisplay display in Enum.GetValues<FenixChannelDisplay>())
        {
            AssertRefused(neverStarted, display, "channel not answering");
            AssertRefused(stopped, display, "channel not answering: gone");
            AssertRefused(latched, display, "field 110 has the wrong shape");
        }

        // A reason of the display's own that is not darkness: the E/WD holds a text code the app does not know while
        // the others are lit and trusted.
        var unknownCodeLines = new MonitorTestKit.Lines();
        using FenixChannelMonitor unknownCode = TrustedPfdMonitor(unknownCodeLines);
        unknownCode.OnDataMessage(FenixChannelProtoWriter.GoodPush(upperEcamLeft: ["xENG 1 FIRE"]));
        AssertRefused(unknownCode, FenixChannelDisplay.Ewd, "unknown code 'x' in the upper ECAM");
        Assert.True(
            unknownCode.VerdictFor(FenixChannelDisplay.Pfd).Trusted,
            "the PFD should stay lit. The log: " + unknownCodeLines);

        Assert.Equal(0, built);
        Assert.Empty(speech.All);
    }

    // A display window that is built and takes the open a key gives it, and is never put on screen: ShowForm shows a
    // window through Visible, which this declines, and everything else it does (the first refresh, the timer) runs.
    private sealed class UnshownDisplayForm : FenixChannelDisplayForm
    {
        public UnshownDisplayForm(string displayName, FenixChannelDisplay display, FenixChannelMonitor monitor)
            : base(displayName, display, monitor, _ => FenixChannelRows.Of(["a row of a trusted display"]))
        {
        }

        protected override void SetVisibleCore(bool value)
        {
        }
    }

    private static readonly (FenixChannelDisplay Display, string Name)[] NamedDisplays =
    [
        (FenixChannelDisplay.Pfd, "PFD"), (FenixChannelDisplay.Nd, "ND"), (FenixChannelDisplay.Ewd, "E/WD"),
        (FenixChannelDisplay.Sd, "SD"), (FenixChannelDisplay.Isis, "ISIS"), (FenixChannelDisplay.Dcdu, "DCDU")
    ];

    // The key's press on a definition: both hotkey modes are on, and the window the factory builds is never shown.
    private static bool PressKey(
        FenixA320Definition definition, SpeechCapture speech, HotkeyManager hotkeys, FenixChannelMonitor monitor,
        FenixChannelDisplay display, string name, List<UnshownDisplayForm> built)
    {
        foreach (string mode in new[] { "outputHotkeyModeActive", "inputHotkeyModeActive" })
        {
            typeof(HotkeyManager).GetField(mode, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hotkeys, true);
        }

        Assert.True(hotkeys.IsOutputHotkeyModeActive && hotkeys.IsInputHotkeyModeActive);

        return definition.OpenChannelWindow(Context(speech, monitor, hotkeys), display, name, windowMonitor =>
        {
            var window = new UnshownDisplayForm(name, display, windowMonitor);
            built.Add(window);
            return window;
        });
    }

    [Fact]
    public void A_key_opens_the_window_on_its_status_row_for_a_dark_display()
    {
        // The owner's ruling (spec 2.4): while the channel is healthy and the display itself is dark, the key opens its
        // window on the one status row, as its Fenix windows list entry does, instead of giving the AI read. Every
        // display's key is the same helper. The key leaves both hotkey modes, builds the window once on the definition's
        // monitor, says nothing itself ([CORE-7]: the list is what the reader reads), and a second press brings the
        // open window forward.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = DarkMonitor(lines);

        foreach ((FenixChannelDisplay display, string name) in NamedDisplays)
        {
            Assert.Equal(new FenixDisplayVerdict(false, "display off"), monitor.VerdictFor(display));
            var definition = new FenixA320Definition();
            GiveMonitor(definition, monitor);
            var speech = new SpeechCapture();
            using var hotkeys = new HotkeyManager();
            var built = new List<UnshownDisplayForm>();

            try
            {
                Assert.True(PressKey(definition, speech, hotkeys, monitor, display, name, built), $"the {name} key");
                Assert.False(hotkeys.IsOutputHotkeyModeActive, $"the {name} key left the output mode on");
                Assert.False(hotkeys.IsInputHotkeyModeActive, $"the {name} key left the input mode on");
                UnshownDisplayForm window = Assert.Single(built);
                Assert.Equal(new[] { $"{name}: not available, display off" }, ItemsOf(ListOf(window)));

                Assert.True(PressKey(definition, speech, hotkeys, monitor, display, name, built), $"the {name} key again");
                Assert.False(hotkeys.IsOutputHotkeyModeActive, $"the {name} key left the output mode on the second time");
                Assert.False(hotkeys.IsInputHotkeyModeActive, $"the {name} key left the input mode on the second time");
                Assert.Same(window, Assert.Single(built));
                Assert.Empty(speech.All);
            }
            finally
            {
                definition.DisposeTrackedWindows();
            }
        }
    }

    [Fact]
    public void A_key_opens_the_window_with_its_rows_for_a_trusted_display()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines);
        var definition = new FenixA320Definition();
        GiveMonitor(definition, monitor);
        var speech = new SpeechCapture();
        using var hotkeys = new HotkeyManager();
        var built = new List<UnshownDisplayForm>();

        try
        {
            Assert.True(PressKey(definition, speech, hotkeys, monitor, FenixChannelDisplay.Pfd, "PFD", built));

            Assert.False(hotkeys.IsOutputHotkeyModeActive);
            Assert.False(hotkeys.IsInputHotkeyModeActive);
            UnshownDisplayForm window = Assert.Single(built);
            Assert.Equal(new[] { "a row of a trusted display" }, ItemsOf(ListOf(window)));
            Assert.Empty(speech.All);
        }
        finally
        {
            definition.DisposeTrackedWindows();
        }
    }

    [Fact]
    public void A_key_that_opens_a_window_leaves_both_hotkey_modes_first()
    {
        // A window opened with a hotkey mode still on loses its keys to it, as the 737's display windows and the
        // Fenix windows list know. The order is the contract: the verdict (a display that is neither trusted nor dark is
        // refused), then both exits, then the window. The tests above run the two ways through; this pins that the exits
        // come before the window shows, which a test that never shows one cannot see, so the order is pinned in the
        // source.
        string source = StripComments(File.ReadAllText(
            SourcePath("Aircraft", "FenixA320Definition.DisplayChannel.cs")));
        int declaration = source.IndexOf("internal bool OpenChannelWindow<TForm>(", StringComparison.Ordinal);
        Assert.True(declaration >= 0, "the definition has no OpenChannelWindow");
        string body = BlockAfter(source, declaration);

        Match refusal = Regex.Match(
            body, @"if\s*\(\s*!\s*(?<verdict>\w+)\.Trusted\s*&&\s*!\s*\k<verdict>\.IsDisplayOff\s*\)\s*return\s+false\s*;");
        int output = body.IndexOf("HotkeyManager.ExitOutputHotkeyMode()", StringComparison.Ordinal);
        int input = body.IndexOf("HotkeyManager.ExitInputHotkeyMode()", StringComparison.Ordinal);
        int show = body.IndexOf("ShowChannelWindow(", StringComparison.Ordinal);

        Assert.True(refusal.Success, "OpenChannelWindow does not refuse a display that is neither trusted nor dark");
        Assert.True(refusal.Index < output, "OpenChannelWindow leaves the output hotkey mode before it has refused");
        Assert.True(output >= 0 && input >= 0, "OpenChannelWindow does not leave both hotkey modes");
        Assert.True(output < show && input < show, "OpenChannelWindow opens the window before it leaves the hotkey modes");
        Assert.Contains("return true", body[show..]);
    }

    [Fact]
    public void Without_a_monitor_a_list_entry_says_the_display_is_not_available()
    {
        // A Fenix windows list entry opens the window through ShowChannelWindow. Before Start and after Stop there
        // is no monitor to show: the pilot is told, and nothing opens.
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        int built = 0;

        definition.ShowChannelWindow<FenixChannelDisplayForm>(speech, "ND", _ =>
        {
            built++;
            return null!;
        });

        Assert.Equal(new[] { "ND display not available" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
        Assert.Equal(0, built);
    }

    [Fact]
    public void A_list_entry_opens_the_window_on_the_definitions_monitor_whatever_the_verdict()
    {
        // The window shows its status row while the display is not trusted, so the entry does not ask for a verdict.
        // The factory throws as soon as it is reached, so that no window is shown: what it was handed is the
        // definition's own monitor, and it was handed one that is not trusted.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        var definition = new FenixA320Definition();
        typeof(FenixA320Definition).GetField("_displayChannelMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(definition, neverStarted);
        var speech = new SpeechCapture();
        FenixChannelMonitor? handedOver = null;

        Assert.Throws<InvalidOperationException>(() => definition.ShowChannelWindow<FenixChannelDisplayForm>(
            speech, "ND", monitor =>
            {
                handedOver = monitor;
                throw new InvalidOperationException("stop before a window shows");
            }));

        Assert.Same(neverStarted, handedOver);
        Assert.False(neverStarted.VerdictFor(FenixChannelDisplay.Nd).Trusted);
        Assert.Empty(speech.All);
    }

    // ---- The Fenix windows list -----------------------------------------------------------------

    [Fact]
    public void The_window_list_still_has_unique_names()
    {
        // The list speaks "Name. Description": the display windows join Engines, Cabin and the others, and two
        // entries with one name cannot be told apart by ear.
        List<string> names = FenixWindowRegistry.Build().Select(entry => entry.Name).ToList();

        string[] duplicates = names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(duplicates.Length == 0, "Fenix windows registered more than once: " + string.Join(", ", duplicates));
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name), "a Fenix window has no name"));
    }

    [Fact]
    public void The_window_list_calls_the_six_display_seams_in_order_after_the_options()
    {
        // Each display plan implements its own seam, and a seam nothing implements is no call at all, so the order
        // cannot be seen in the list until they do. It is pinned in the source: PFD, ND, E/WD, SD, ISIS, DCDU, after
        // the five windows that were there before.
        string source = StripComments(File.ReadAllText(SourcePath("Forms", "Fenix", "FenixWindowRegistry.cs")));
        int declaration = source.IndexOf("public static IReadOnlyList<FenixWindowEntry> Build()", StringComparison.Ordinal);
        Assert.True(declaration >= 0, "the registry has no Build");
        string body = BlockAfter(source, declaration);

        string[] expected =
        {
            "AddEngineWindows", "AddCabinWindows", "AddFailureWindows", "AddDocumentWindows", "AddOptionsWindows",
            "AddChannelPfdWindows", "AddChannelNdWindows", "AddChannelEwdWindows", "AddChannelSdWindows",
            "AddChannelIsisWindows", "AddChannelDcduWindows",
        };
        string[] called = Regex.Matches(body, @"\b(?<seam>Add\w+Windows)\s*\(\s*entries\s*\)\s*;")
            .Select(match => match.Groups["seam"].Value)
            .ToArray();
        Assert.Equal(expected, called);

        // Each is declared once, as an optional partial method of the registry: the five that were there before in the
        // registry's own file, the six display seams in its channel partial, FenixWindowRegistry.Channel.cs, a file
        // the channel's rule glob (FenixWindowRegistry.Channel*.cs) loads the channel rules on, as it does on the
        // partial of each display that implements its seam.
        string channelSource = StripComments(
            File.ReadAllText(SourcePath("Forms", "Fenix", "FenixWindowRegistry.Channel.cs")));
        foreach (string seam in expected)
        {
            string declaredIn = seam.StartsWith("AddChannel", StringComparison.Ordinal) ? channelSource : source;
            Assert.Single(Regex.Matches(declaredIn,
                @"static\s+partial\s+void\s+" + seam + @"\s*\(\s*List<FenixWindowEntry>\s+entries\s*\)\s*;"));
        }
    }

    // ---- Reading the source ---------------------------------------------------------------------

    private static string SourcePath(params string[] parts)
        => Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(parts).ToArray());

    // A line comment or a block comment: a call named in a comment is no call.
    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string StripComments(string source) => Comment.Replace(source, "");

    // The block that opens at the first "{" after start, braces and all, up to its closing brace.
    private static string BlockAfter(string code, int start)
    {
        int open = code.IndexOf('{', start);
        Assert.True(open >= 0, "no block after the declaration");
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
        }

        throw new InvalidOperationException("the block is not closed");
    }
}
