using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Generic LIVE-SCRAPE readout window for any A380X cockpit display rendered in a
/// Coherent GT view (E/WD, PFD, ND, ISIS, …). Owns a <see cref="CoherentDisplayClient"/>
/// pointed at the view by title needle and shows the reconstructed rows
/// (coherent-display-agent.js: leaf text clustered by Y, sorted by X) live in an
/// accessible read-only TextBox. No injection.
///
/// This surfaces exactly the DECODED text the crew sees — for the E/WD that means
/// the engine N1/EGT/FF, the memo columns and the active warnings/procedures; for
/// the PFD the FMA text, baro, THS and approach flags. Graphical tape values
/// (raw airspeed/altitude needles) are positional and read as scale ticks, so this
/// window AUGMENTS rather than replaces the dedicated SimVar PFD/ND/ISIS windows.
/// </summary>
public sealed class FBWA380LiveDisplayForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly CoherentDisplayClient _disp;
    private readonly string _displayName;
    private TextBox _text = null!;
    private Label _status = null!;
    private bool _haveRows;

    public FBWA380LiveDisplayForm(ScreenReaderAnnouncer announcer, string titleNeedle, string displayName)
    {
        _announcer = announcer;
        _displayName = displayName;
        _disp = new CoherentDisplayClient(titleNeedle);
        _disp.RowsUpdated += OnRowsUpdated;
        BuildUi();
        _disp.Start();
        _ = InitialScrape();
    }

    private void BuildUi()
    {
        Text = $"A380 {_displayName} (live)";
        Size = new Size(820, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        _status = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(780, 22),
            Text = $"Connecting to {_displayName}…",
            AccessibleName = "Status"
        };
        _text = new TextBox
        {
            Location = new Point(12, 40),
            Size = new Size(780, 500),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            AccessibleName = $"{_displayName} readout",
            Text = "Loading…"
        };
        var refresh = new Button { Text = "&Refresh", Location = new Point(12, 548), Size = new Size(90, 30), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { _ = RefreshNow(); };
        var close = new Button { Text = "&Close", Location = new Point(110, 548), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _status, _text, refresh, close });
        CancelButton = close;
        AcceptButton = refresh;
        Load += (_, _) => _text.Focus();
    }

    private async Task InitialScrape()
    {
        var rows = await _disp.ScrapeNowAsync();
        Apply(rows);
    }

    private async Task RefreshNow()
    {
        var rows = await _disp.ScrapeNowAsync();
        Apply(rows);
        _announcer?.Announce("Refreshed");
    }

    private void OnRowsUpdated(List<string> rows) => Apply(rows);

    private void Apply(List<string> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            if (!_haveRows) _status.Text = $"{_displayName} view not reachable — is the A380X loaded and powered?";
            return;
        }
        _haveRows = true;
        _status.Text = $"Live from {_displayName}";
        var sb = new StringBuilder();
        sb.AppendLine($"A380 {_displayName.ToUpperInvariant()}");
        sb.AppendLine(new string('=', 50));
        foreach (var r in rows) sb.AppendLine(r);
        _text.Text = sb.ToString();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { _ = RefreshNow(); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}
