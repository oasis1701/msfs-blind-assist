using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

// Shared FlyByWire E/WD (Engine/Warning Display) pop-out window for the A380X and
// A32NX. Alt+E opens it instead of speaking the E/WD: a read-only, multiline,
// screen-reader-friendly text view of the WHOLE E/WD (engine parameters + the live
// ECAM memo / warning lines). It auto-refreshes on a timer and on F5; Escape closes.
//
// The window is content-agnostic: each aircraft passes an async text builder (the
// A380 decodes its SimVars, the A32NX scrapes its live EWD view), so this one form
// serves both. The reading position (caret) is preserved across refreshes so the
// auto-update doesn't yank a user who is reading line by line.
public sealed class FbwEwdWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Func<Task<string>> _build;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly DisplayListBox _text;
    private System.Windows.Forms.Timer? _timer;
    private readonly IntPtr _previousWindow;
    private bool _busy;

    public FbwEwdWindow(string title, Func<Task<string>> build, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _build = build;
        _announcer = announcer;

        Text = title;
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _text = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            TabIndex = 0,
            AccessibleName = title,
            AccessibleDescription = "Engine and Warning Display. Read with the arrow keys. F5 refreshes; Escape closes. Auto-refreshes.",
        };
        _text.SetText("Loading E/WD...");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var refreshButton = new Button { Text = "&Refresh (F5)", Location = new Point(560, 8), Size = new Size(90, 30), TabIndex = 1, AccessibleName = "Refresh" };
        refreshButton.Click += (s, e) => _ = RefreshAsync(true);
        var closeButton = new Button { Text = "&Close", Location = new Point(655, 8), Size = new Size(85, 30), TabIndex = 2, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();
        bottom.Controls.AddRange(new Control[] { refreshButton, closeButton });

        Controls.Add(_text);
        Controls.Add(bottom);
        CancelButton = closeButton;

        Load += async (s, e) =>
        {
            BringToFront();
            Activate();
            _text.Focus();
            await RefreshAsync(false);
            _timer = new System.Windows.Forms.Timer { Interval = 2000 };
            _timer.Tick += async (s2, e2) => await RefreshAsync(false);
            _timer.Start();
        };
    }

    private async Task RefreshAsync(bool announce)
    {
        if (_busy || IsDisposed) return;
        _busy = true;
        try
        {
            string txt = await _build();
            if (IsDisposed) return;
            if (string.IsNullOrWhiteSpace(txt)) txt = "(E/WD content not available — power up the displays / try again)";
            // Reconcile in place (no-ops when unchanged) so a refresh doesn't reset the
            // screen-reader review cursor to the top.
            _text.SetText(txt);
            if (announce) _announcer.Announce("E W D refreshed");
        }
        catch { /* best-effort; keep the last good content */ }
        finally { _busy = false; }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.F5) { _ = RefreshAsync(true); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        base.OnFormClosed(e);
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    // Form.Dispose() does NOT raise OnFormClosed, so an aircraft-swap Dispose of a
    // tracked window must stop the refresh timer here too (both paths are idempotent).
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }
        base.Dispose(disposing);
    }
}
