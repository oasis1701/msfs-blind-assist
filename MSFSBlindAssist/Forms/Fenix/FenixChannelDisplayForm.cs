using System.Runtime.InteropServices;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The live window every display of the Fenix display channel uses ("Fenix PFD", "Fenix ND" and the rest): one list,
/// named "&lt;display&gt; display", that fills the window and shows what the display shows, top to bottom and left to
/// right, in the display's own words. A WinForms timer reads the display's snapshot and reconciles the list in
/// place every second, so the row the pilot is on stays under the reader; F5 reads it again now, and Escape closes
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
/// window does, so that the reader has a current row.</para>
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

    private readonly string _displayName;
    private readonly FenixChannelDisplay _display;
    private readonly FenixChannelMonitor _monitor;
    private readonly Func<FenixChannelSnapshot, FenixChannelRows> _compose;
    private readonly DisplayListBox _list;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _started;
    private IntPtr _previousWindow;

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
    /// (<see cref="DisplayListBox.SetLines"/>). The timer's tick and F5 (UI thread). It never speaks and never takes
    /// focus. After the window is disposed it does nothing, so a tick that was already queued as the window closed
    /// cannot touch a list that is gone.
    /// </summary>
    internal void RefreshNow()
    {
        if (IsDisposed || Disposing) return;

        _list.SetLines(RowsFor(
            _displayName,
            _monitor.Snapshot(_display),
            _compose,
            problem => _monitor.ReportFeatureProblem(_displayName, problem)));
    }

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
        => [$"{displayName}: not available, {reason}"];

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
