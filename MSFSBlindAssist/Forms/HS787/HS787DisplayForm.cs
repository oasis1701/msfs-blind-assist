using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Generic live read-out window for a HorizonSim 787 cockpit display (the Navigation /
/// Synoptic-System / Standby displays). It scrapes the display's Coherent GT view through the
/// generic <see cref="CoherentDisplayClient"/> + coherent-display-agent.js (the same row-
/// reconstruction the A380 SD/EWD use) and shows the rows as read-only, screen-reader-navigable
/// text that live-updates, with the review caret preserved across refreshes.
///
/// Each instance owns its own client/socket for ONE view, disposed on close — so it never
/// contends with the always-on IRS reader (which holds HSB789_PFD) or the CDU/EFB clients.
/// The PFD itself is intentionally NOT offered here: its tape values are positional (a scrape
/// returns scale ticks, not readings) and are already covered by the SimVar read-out hotkeys
/// (B altimeter, Shift+S speed, Shift+H heading, A/Q altitude), and the IRS reader owns that view.
/// </summary>
public sealed class HS787DisplayForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly CoherentDisplayClient _client;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TextBox _text;
    private readonly string _title;
    private readonly IntPtr _previousWindow;
    private bool _disposed;

    public HS787DisplayForm(string title, string coherentViewNeedle, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _title = title;

        Text = title;
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            WordWrap = false,
            TabIndex = 0,
            AccessibleName = title,
            AccessibleDescription = title + ". Read with the arrow keys. F5 refreshes; Escape closes. Auto-updates.",
            Text = "Connecting to the display..."
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var refreshButton = new Button { Text = "&Refresh (F5)", Location = new Point(560, 8), Size = new Size(90, 30), TabIndex = 1, AccessibleName = "Refresh" };
        refreshButton.Click += (s, e) => _ = _client.ScrapeNowAsync();
        var closeButton = new Button { Text = "&Close", Location = new Point(655, 8), Size = new Size(85, 30), TabIndex = 2, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();
        bottom.Controls.AddRange(new Control[] { refreshButton, closeButton });

        Controls.Add(_text);
        Controls.Add(bottom);
        CancelButton = closeButton;

        // CoherentDisplayClient raises RowsUpdated on the thread that created it (this UI thread),
        // only when the row set actually changes.
        _client = new CoherentDisplayClient(coherentViewNeedle, pollIntervalMs: 1500);
        _client.RowsUpdated += OnRowsUpdated;

        Load += (s, e) =>
        {
            BringToFront();
            Activate();
            _text.Focus();
            _client.Start();
            _client.SetActive(true);
        };
    }

    private void OnRowsUpdated(List<string> rows)
    {
        if (_disposed) return;
        string txt = (rows == null || rows.Count == 0)
            ? "(no display content — power up the aircraft displays, or press F5)"
            : string.Join(Environment.NewLine, rows);
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => { if (!_disposed) DisplayText.SetPreserveCaret(_text, txt); })); }
            catch (InvalidOperationException) { /* handle destroyed mid-swap */ }
        }
        else
        {
            DisplayText.SetPreserveCaret(_text, txt);
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.F5) { _ = _client.ScrapeNowAsync(); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _client.RowsUpdated -= OnRowsUpdated;
            _client.Dispose();
        }
        base.Dispose(disposing);
    }
}
