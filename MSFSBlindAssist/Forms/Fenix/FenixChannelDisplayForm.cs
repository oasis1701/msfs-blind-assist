using System.Diagnostics;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The live window every display of the Fenix display channel uses ("Fenix PFD", "Fenix ND" and the rest): one list,
/// named "&lt;display&gt; display", that fills the window and shows what the display shows, top to bottom and left to
/// right, in the display's own words. A WinForms timer reads the display's snapshot and reconciles the list in
/// place every second, so the row the pilot is on stays under the reader (<see cref="RowToFollow"/>); F5 reads it
/// again now, and Escape closes
/// the window, once any combo list a derived window has open is closed. A derived window may add controls after the
/// list (<see cref="AddControlAfterList"/>) and take keys before the base does (<see cref="HandleWindowKey"/>).
/// </summary>
/// <remarks>
/// <para>Where the rows come from. The window takes its verdict and its rows from one snapshot ([FNX-22]), through
/// <see cref="RowsFor"/>: while the display is not trusted, or the feature cannot decode a field it uses, one status
/// row, "&lt;display&gt;: not available, &lt;reason&gt;", stands in place of the content, so the list never holds a
/// stale row and never an empty one, and a composer that throws costs only this window, never the app.</para>
/// <para>Silence. The window says nothing itself and has no announcer: the screen reader reads the list when it
/// takes focus and a row that changes under the pilot, so a refresh, F5 included, speaks nothing, and nothing is said
/// on open or close ([CORE-7]).</para>
/// <para>Focus. A refresh runs every second and never takes focus or moves the reader. Only the pilot's own open
/// (<see cref="ShowForm"/>) does, and it lands on the list; the first open selects its first row, as the Engines
/// window does, so that the reader has a current row. The pilot keeps their row. The list follows a row by its exact
/// text, and these rows change every second ("Altitude -63 feet" is "Altitude -64 feet" a second later) while rows come
/// and go above them, so a refresh would slide the content under the cursor. The window therefore remembers the words
/// of the selected row and, after the rows are reconciled, moves the selection to the row that reads the same or, when
/// its value changed, shares the most leading words with it (<see cref="RowToFollow"/>). While the list holds a
/// status row the row remembered stays the one followed, so the pilot lands back on it when the content returns. A
/// row with no word in common is left to the list's own clamp. Moving the selection fires a selection change but
/// never speaks and never takes focus.</para>
/// <para>One class for each display. Fenix windows are tracked one for each type
/// (<c>FenixA320Definition.ShowFenixWindow</c>), so two displays that shared this class would share one window:
/// each display's window is a class of its own, derived from this one, in a file of its own. This class is not
/// abstract only so that a test can build it.</para>
/// <para>Threads. The timer, the keys and the composer all run on the UI thread, and a composer only reads the
/// snapshot it is given and does no I/O, so a refresh is short and nothing here marshals from a pool thread.</para>
/// </remarks>
internal class FenixChannelDisplayForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // Bit 30 of a key message's lParam: set when the key was already down, so the message is an auto-repeat.
    private const long KeyWasDown = 0x40000000;

    private const int RefreshIntervalMs = 1000;

    // A list update this long holds up the app's keys and speech, so it is written to the channel's log.
    private const double SlowListUpdateMs = 1000;

    private readonly string _displayName;
    private readonly FenixChannelDisplay _display;
    private readonly FenixChannelMonitor _monitor;
    private readonly Func<FenixChannelSnapshot, FenixChannelRows> _compose;
    private readonly DisplayListBox _list;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _started;
    private IntPtr _previousWindow;

    // The row the pilot is on, by its text, as the last refresh found it with content in the list; null until one has.
    private string? _followedRow;

    /// <param name="displayName">The display's short name: "PFD", "ND", "E/WD", "SD", "ISIS" or "DCDU". It makes the
    /// title ("Fenix PFD"), names the list ("PFD display") and starts the status row.</param>
    /// <param name="display">Which display's verdict and snapshot the window reads.</param>
    /// <param name="monitor">The channel monitor the snapshots come from, and where a composer that throws is
    /// reported.</param>
    /// <param name="compose">The display's rows from a trusted snapshot, or the reason there are none. It runs on the
    /// UI thread once a second, only reads the snapshot and does no I/O.</param>
    public FenixChannelDisplayForm(
        string displayName,
        FenixChannelDisplay display,
        FenixChannelMonitor monitor,
        Func<FenixChannelSnapshot, FenixChannelRows> compose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(compose);

        _displayName = displayName;
        _display = display;
        _monitor = monitor;
        _compose = compose;
        _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
        _timer.Tick += (_, _) => RefreshNow();

        Text = "Fenix " + displayName;
        Size = new Size(520, 440);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = displayName + " display",
            TabIndex = 0
        };
        Controls.Add(_list);

        FormClosing += (_, _) =>
        {
            _timer.Stop();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    /// <summary>
    /// Shows the window, or brings the open one forward, with focus on the list. The first call also starts it
    /// (<see cref="StartLive"/>). The pilot's own open: the only focus calls in the form.
    /// </summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        StartLive();
        // A second press of the key brings the window forward even when the pilot had minimized it.
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        BringToFront();
        Activate();
        // The key opens this while the sim has the foreground, where Activate alone can leave the window behind it.
        TopMost = true;
        TopMost = false;
        _list.Focus();
    }

    /// <summary>
    /// The first open's work, once: the rows are read before the window shows, row 0 is selected so that the reader has
    /// a current row to land on, and the timer starts. Later calls do nothing, so a second press of the key never
    /// moves the pilot to the top again. Apart from <see cref="ShowForm"/> it is for the tests, which cannot show a
    /// window.
    /// </summary>
    internal void StartLive()
    {
        if (_started) return;
        _started = true;

        RefreshNow();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _timer.Start();
    }

    /// <summary>
    /// Reads the display's snapshot again and reconciles the list with it in place, so the pilot keeps their row
    /// (<see cref="DisplayListBox.SetLines"/>, then <see cref="RowToFollow"/> for a row that moved or changed its words).
    /// The timer's tick and F5 (UI thread). It never speaks and never takes focus. After the window is disposed it does
    /// nothing, so a tick that was already queued as the window closed cannot touch a list that is gone.
    /// </summary>
    internal void RefreshNow()
    {
        if (IsDisposed || Disposing) return;

        // The row the pilot is on, before the rows change. A status row is nobody's row, so while the list holds one the
        // row followed before it stays the one followed.
        if (!HoldsStatusRow(_list.Items.Cast<string>().ToList())) _followedRow = _list.SelectedItem as string;

        FenixChannelSnapshot snapshot = _monitor.Snapshot(_display);

        // A fault in a derived window's controls costs only them: this runs on the timer, every second.
        try
        {
            OnRefresh(snapshot);
        }
        catch (Exception ex)
        {
            _monitor.ReportFeatureProblem(_displayName, $"controls failed: {ex.GetType().Name}: {ex.Message}");
        }

        IReadOnlyList<string> rows = RowsFor(
            _displayName,
            snapshot,
            _compose,
            problem => _monitor.ReportFeatureProblem(_displayName, problem));

        // Twice in flight a list shrinking to its status row held this thread for 5 s, with a UI Automation reader attached
        // and the cause never found. The log line that measured it went with the SimConnect checks, so it is measured here.
        long before = Stopwatch.GetTimestamp();
        _list.SetLines(rows);
        if (Stopwatch.GetElapsedTime(before).TotalMilliseconds >= SlowListUpdateMs)
        {
            _monitor.ReportFeatureProblem(_displayName, "a list update held the UI thread for over a second");
        }

        if (HoldsStatusRow(rows)) return;

        // The list put the cursor back by the row's exact text. A row whose value changed, with another row above it
        // appearing, is not found that way and the clamp would leave the pilot on a neighbor: follow its words.
        int best = RowToFollow(rows, _followedRow, _list.SelectedIndex);
        if (best != -1 && best != _list.SelectedIndex) _list.SelectedIndex = best;
    }

    /// <summary>
    /// Called on the UI thread at every refresh with the snapshot the rows are read from, trusted or not, before the
    /// rows are set; for a derived window's controls. The base does nothing.
    /// </summary>
    protected virtual void OnRefresh(FenixChannelSnapshot s) { }

    /// <summary>
    /// The index in <paramref name="rows"/> of the row the pilot was on, or -1 when none reads like it (the list's own
    /// clamp then stands). <paramref name="followed"/> is the text the pilot's row had, and <paramref name="currentIndex"/>
    /// where the cursor is now. A row with exactly that text is the one, the nearest to <paramref name="currentIndex"/>
    /// when several are. Otherwise the row that shares the most leading words with it, at least one (words split on spaces
    /// and compared ordinally), again the nearest on a tie: "Altitude -63 feet, STD" is followed to "Altitude -64 feet,
    /// STD", and "Heading 094, track 094" to "Heading not available".
    /// </summary>
    internal static int RowToFollow(IReadOnlyList<string> rows, string? followed, int currentIndex)
    {
        if (followed is null) return -1;

        int exact = Nearest(rows, currentIndex, row => string.Equals(row, followed, StringComparison.Ordinal) ? 1 : 0);
        if (exact != -1) return exact;

        string[] words = Words(followed);
        return Nearest(rows, currentIndex, row => LeadingWordsShared(words, Words(row)));
    }

    // The index of the row with the highest score above 0, the nearest to currentIndex among equals (the lower index
    // when two are as near); -1 when no row scores.
    private static int Nearest(IReadOnlyList<string> rows, int currentIndex, Func<string, int> score)
    {
        int best = -1;
        int bestScore = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            int rowScore = score(rows[i]);
            if (rowScore == 0) continue;

            if (rowScore > bestScore || (rowScore == bestScore && Math.Abs(i - currentIndex) < Math.Abs(best - currentIndex)))
            {
                best = i;
                bestScore = rowScore;
            }
        }

        return best;
    }

    private static string[] Words(string row) => row.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static int LeadingWordsShared(string[] first, string[] second)
    {
        int shared = 0;
        while (shared < first.Length && shared < second.Length
               && string.Equals(first[shared], second[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return shared;
    }

    // A status row is a single row that starts "<display>: not available, ": what StatusRow builds.
    private bool HoldsStatusRow(IReadOnlyList<string> rows)
        => rows.Count == 1 && rows[0].StartsWith(StatusStart(_displayName), StringComparison.Ordinal);

    private static string StatusStart(string displayName) => $"{displayName}: not available, ";

    /// <summary>
    /// The rows the list shows for one snapshot, and the testable core of the window. Never empty, so a screen
    /// reader always has a row to land on.
    /// </summary>
    /// <param name="displayName">The display's short name, which starts a status row.</param>
    /// <param name="s">The snapshot to show: its verdict decides whether <paramref name="compose"/> is asked at all.</param>
    /// <param name="compose">The display's rows from a trusted snapshot, or the reason there are none.</param>
    /// <param name="reportThrow">Where to report a composer that throws, as "read failed: &lt;exception type&gt;:
    /// &lt;message&gt;"; the window passes the monitor's <see cref="FenixChannelMonitor.ReportFeatureProblem"/> under its
    /// own name, which writes each distinct line once, so a composer that throws at every refresh is one line in the
    /// log. A problem the composer returns is its own to report; this reports only what it catches.</param>
    /// <returns>
    /// A display that is not trusted: one row, "&lt;display&gt;: not available, &lt;reason&gt;", and
    /// <paramref name="compose"/> is not asked, so no data of it can reach the list ([FNX-22]). A composer that says
    /// not available: one row with its problem. One that throws: "&lt;display&gt;: not available, read failed". One
    /// that gives no rows and no problem (a result that was never filled in, an empty list, or a list holding a null):
    /// "&lt;display&gt;: not available, no data". Otherwise the composed rows, in the order given. The composers say "no
    /// data" themselves when the snapshot holds no message, so a missing <see cref="FenixChannelSnapshot.Push"/> is not
    /// checked here.
    /// </returns>
    internal static IReadOnlyList<string> RowsFor(
        string displayName,
        FenixChannelSnapshot s,
        Func<FenixChannelSnapshot, FenixChannelRows> compose,
        Action<string>? reportThrow = null)
    {
        if (!s.Verdict.Trusted) return StatusRow(displayName, s.Verdict.Reason);

        FenixChannelRows composed;
        try
        {
            composed = compose(s);
        }
        catch (Exception ex)
        {
            reportThrow?.Invoke($"read failed: {ex.GetType().Name}: {ex.Message}");
            return StatusRow(displayName, "read failed");
        }

        // A result with a problem is a problem even if it also holds rows: fail closed, never show them.
        if (composed.Problem is not null) return StatusRow(displayName, composed.Problem);

        // The list box throws on a null row, and it would do so on the timer's tick, past the try above: no data.
        if (composed.Rows is not { Count: > 0 } rows || rows.Any(row => row is null))
            return StatusRow(displayName, "no data");

        return rows;
    }

    private static IReadOnlyList<string> StatusRow(string displayName, string reason)
        => [StatusStart(displayName) + reason];

    /// <summary>
    /// Adds a control below the list, at the bottom edge of the window, and last in the tab order: the list first,
    /// then the controls in the order they were added. The control keeps its own height. For a derived window, from
    /// its constructor.
    /// </summary>
    protected void AddControlAfterList(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        // The list fills what the docked controls leave, and Windows Forms docks the last control added first, so the
        // controls end up below the list in the order they were added.
        control.Dock = DockStyle.Bottom;
        Controls.Add(control);
        control.TabIndex = Controls.Count - 1;
    }

    /// <summary>
    /// A panel's keys as one row of buttons, for a derived window to add after its list (<see cref="AddControlAfterList"/>):
    /// the SD window's ECAM page keys, the E/WD window's T.O CONFIG, CLR, RCL and EMER CANC and the ISIS window's buttons.
    /// One container wrapping and sizing itself, holding a button for each of <paramref name="keys"/> in the order given,
    /// the label as the text and the accessible name (the reader says it and "button", and nothing changes it), tab indexes
    /// in that order. A click calls <paramref name="press"/> with the key's variable name and nothing else.
    /// </summary>
    internal static FlowLayoutPanel BuildKeyRow(IReadOnlyList<(string Label, string Key)> keys, Action<string> press)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        foreach ((string label, string key) in keys)
        {
            var button = new Button { Text = label, AccessibleName = label, AutoSize = true };
            button.Click += (_, _) => press(key);
            panel.Controls.Add(button);
        }

        for (int i = 0; i < panel.Controls.Count; i++) panel.Controls[i].TabIndex = i;
        return panel;
    }

    /// <summary>
    /// A derived window's chance at a key before the base's own (Escape closes, unless a combo's list is open; F5
    /// refreshes). Asked about every key press: an auto-repeat of a key held down and an Alt chord included, which a
    /// window gets as a system key message. Returns true when the window took the key, which then goes no further; the
    /// base takes no key.
    /// </summary>
    protected virtual bool HandleWindowKey(Keys keyData) => false;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (msg.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            if (HandleWindowKey(keyData)) return true;

            // Escape closes, unless it is closing a combo's open list: a derived window's combo has the key then, so a
            // pilot cancelling the list does not lose the window.
            if (keyData == Keys.Escape && ActiveControl is not ComboBox { DroppedDown: true })
            {
                Close();
                return true;
            }

            if (keyData == Keys.F5)
            {
                // Once per press: a held key repeats, and each repeat has the already-down bit set.
                if ((msg.LParam.ToInt64() & KeyWasDown) == 0) RefreshNow();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
