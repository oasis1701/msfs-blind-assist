using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

// Shared pop-out window for the FlyByWire E/WD (Engine/Warning Display) of the A380X and
// A32NX, and the Synaptic A220's CAS window. Alt+E opens it instead of speaking the E/WD:
// a read-only, multiline, screen-reader-friendly text view of the WHOLE E/WD (engine
// parameters + the live ECAM memo / warning lines), or, on the A220, of every crew
// alerting message. It auto-refreshes on a timer and on F5; Escape closes.
//
// The window is content-agnostic: each aircraft passes an async text builder (the
// A380 decodes its SimVars, the A32NX scrapes its live EWD view, the A220 decodes its CAS
// bitset) and the message F5 speaks ("E W D refreshed" by default, "CAS refreshed" on the
// A220), so this one form serves all three. Refreshes reconcile the list in place (via
// DisplayListBox, which wraps DisplayList.UpdateInPlace), so the reader's selected row
// and scroll position are preserved instead of being reset to the top on every
// auto-update.
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

    private readonly string _refreshedMessage;

    /// <param name="description">The list's accessible description; the E/WD wording when null.</param>
    /// <param name="refreshedMessage">Spoken after an F5 refresh.</param>
    /// <param name="loadingText">The list's text before the first build returns.</param>
    public FbwEwdWindow(string title, Func<Task<string>> build, ScreenReaderAnnouncer announcer,
        string? description = null, string refreshedMessage = "E W D refreshed", string loadingText = "Loading E/WD...")
    {
        _previousWindow = GetForegroundWindow();
        _build = build;
        _announcer = announcer;
        _refreshedMessage = refreshedMessage;

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
            AccessibleDescription = description ?? "Engine and Warning Display. Read with the arrow keys. F5 refreshes; Escape closes. Auto-refreshes.",
        };
        _text.SetText(loadingText);

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
            if (announce) _announcer.Announce(_refreshedMessage);
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
