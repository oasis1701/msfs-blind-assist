using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// The "757 EFB settings" window: what Shift+T opens on the 757 while no simulator process runs
/// (<see cref="Bbs757EfbWindowChoice"/>; with a simulator running, or when the app cannot tell, Shift+T opens the tablet window
/// instead). Every setting of BlueBird's tablet is read from BlueBird's own files and changed in them, which takes effect when
/// the 757 next loads and is possible only while no simulator runs ([B757-17]; the content is
/// <c>BBS757EfbForm.Settings.cs</c>). The window needs no Coherent client, no tablet and no SimConnect: it reads and writes
/// BlueBird's work folder (<see cref="Bbs757WorkFolder"/>) and asks <see cref="Bbs757SimGuard"/> whether it may. The Navigraph
/// sign-in, the nav data update and the SimBrief load are on the tablet window's own pages.
///
/// The window is MainForm's: made at the first Shift+T that finds no simulator, hidden by Escape and its close box (it
/// is shown again by the next Shift+T), and disposed with the 757's other windows on every profile switch and at app exit.
/// </summary>
public partial class BBS757EfbForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // How often the simulator is asked for while the window shows: the guard's tick (UpdateSettingsGuard).
    private const int GuardTickMs = 1000;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<IReadOnlyList<string>> _workRoots;
    // Canceled when the window is disposed (a profile switch, app exit): no pending wait may speak after that.
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Windows.Forms.Timer _guardTimer;

    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _disposed;

    public BBS757EfbForm(ScreenReaderAnnouncer announcer)
        : this(announcer, Bbs757WorkFolder.DefaultRoots)
    {
    }

    internal BBS757EfbForm(ScreenReaderAnnouncer announcer, Func<IReadOnlyList<string>> workRoots)
    {
        _announcer = announcer;
        _workRoots = workRoots;

        BuildForm();
        _guardTimer = new System.Windows.Forms.Timer { Interval = GuardTickMs };
        _guardTimer.Tick += (_, _) => UpdateSettingsGuard();
    }

    // ------------------------------------------------------------------
    // Form construction
    // ------------------------------------------------------------------

    private void BuildForm()
    {
        Text = "757 EFB settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;
        // The status box, the section list and a few rows below them (the rows scroll); the window scrolls too should a large
        // font need more.
        ClientSize = new Size(470, 620);
        AutoScroll = true;

        BuildSettingsContent();

        KeyDown += (_, e) =>
        {
            // Escape closes an open list first (a choice list's), then the window.
            if (e.KeyCode == Keys.Escape && !e.Alt && !e.Control && !e.Shift
                && ActiveControl is not ComboBox { DroppedDown: true })
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                HideAndRestore();
            }
        };

        FormClosing += (_, e) =>
        {
            // Let real app/OS shutdown through; everything else hides, as the CDU window does.
            if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
                return;
            e.Cancel = true;
            HideAndRestore();
        };
    }

    private CancellationToken Lifetime => _lifetime.Token;

    // ------------------------------------------------------------------
    // Show / hide
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        // BlueBird's settings read fresh and the simulator checked, silently, before the window shows: the status and the locks
        // are right from the first moment.
        OpenSettings();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        // The first control that takes focus: the status box.
        SelectNextControl(null, forward: true, tabStopOnly: true, nested: true, wrap: false);
    }

    private void HideAndRestore()
    {
        // A Settings box left with a change not saved: dropped, and said, as leaving it would.
        DropUnsavedEntry();
        Hide();
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }

    // The simulator is asked for only while the window is shown.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_disposed) return;
        if (Visible) _guardTimer.Start();
        else _guardTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _guardTimer.Stop();
            _guardTimer.Dispose();
            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
