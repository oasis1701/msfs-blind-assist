using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// On-demand A380X Navigation Display (ND) readout — TO waypoint (ident /
/// distance / bearing / ETA), ND mode + range, cross-track error, RNP, and
/// LOC/GS deviation. Read-only (the EFIS-CP set controls live on the dedicated
/// EFIS Control Panel). Shared A32NX_ EFIS vars (verified live on the A380X).
/// </summary>
public class FBWA380NavDisplayForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly Dictionary<string, double> _raw = new();
    private TextBox _text = null!;

    private static readonly string[] NdModes = { "ROSE ILS", "ROSE VOR", "ROSE NAV", "ARC", "PLAN" };
    private static readonly string[] NdRanges = { "10", "20", "40", "80", "160", "320" };

    private static readonly string[] Vars =
    {
        "A32NX_EFIS_L_TO_WPT_IDENT_0", "A32NX_EFIS_L_TO_WPT_IDENT_1",
        "A32NX_EFIS_L_TO_WPT_DISTANCE", "A32NX_EFIS_L_TO_WPT_BEARING", "A32NX_EFIS_L_TO_WPT_ETA",
        "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_L_ND_RANGE",
        "A32NX_FG_CROSS_TRACK_ERROR", "A32NX_FMGC_L_RNP",
        "A32NX_RADIO_RECEIVER_LOC_IS_VALID", "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
        "A32NX_RADIO_RECEIVER_GS_IS_VALID", "A32NX_RADIO_RECEIVER_GS_DEVIATION",
    };

    /// <summary>L:var names this window reads (for registration in the aircraft def).</summary>
    public static IEnumerable<string> AllVariableNames() => Vars;

    public FBWA380NavDisplayForm(ScreenReaderAnnouncer announcer, SimConnectManager sim)
    {
        _announcer = announcer;
        _sim = sim;
        BuildUi();
        if (_sim != null) _sim.SimVarUpdated += OnSimVarUpdated;
        Refresh_();
    }

    private void BuildUi()
    {
        Text = "A380 Navigation Display";
        Size = new Size(700, 480);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        _text = new TextBox
        {
            Location = new Point(12, 12),
            Size = new Size(660, 380),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            AccessibleName = "Navigation display readout",
            Text = "Loading…"
        };
        var refresh = new Button { Text = "&Refresh", Location = new Point(12, 400), Size = new Size(90, 30), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { Refresh_(); _announcer?.Announce("Refreshed"); };
        var live = new Button { Text = "&Live scrape", Location = new Point(110, 400), Size = new Size(110, 30), AccessibleName = "Live scrape view" };
        live.Click += (_, _) => OpenLiveScrape();
        var close = new Button { Text = "&Close", Location = new Point(228, 400), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { _text, refresh, live, close });
        CancelButton = close;
        AcceptButton = refresh;
        Load += (_, _) => _text.Focus();
    }

    private async void Refresh_()
    {
        foreach (var kvp in _sim.GetCachedVariableSnapshot(Vars.ToList())) _raw[kvp.Key] = kvp.Value;
        foreach (var v in Vars) _sim.RequestVariable(v, forceUpdate: true);
        await Task.Delay(500);
        _text.Text = Render();
    }

    private double R(string v) => _raw.TryGetValue(v, out double d) ? d : 0;

    private static string UnpackIdent(double ident0, double ident1)
    {
        double[] values = { ident0, ident1 };
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length * 8; i++)
        {
            int word = i / 8, charPos = i % 8;
            int code = (int)(values[word] / Math.Pow(2, charPos * 6)) & 0x3F;
            if (code > 0) sb.Append((char)(code + 31));
        }
        return sb.ToString().Trim();
    }

    private string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A380 NAVIGATION DISPLAY");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        int mode = (int)Math.Round(R("A32NX_EFIS_L_ND_MODE"));
        int range = (int)Math.Round(R("A32NX_EFIS_L_ND_RANGE"));
        sb.AppendLine($"ND mode: {(mode >= 0 && mode < NdModes.Length ? NdModes[mode] : mode.ToString())}");
        sb.AppendLine($"ND range: {(range >= 0 && range < NdRanges.Length ? NdRanges[range] + " NM" : range.ToString())}");
        sb.AppendLine();

        string ident = UnpackIdent(R("A32NX_EFIS_L_TO_WPT_IDENT_0"), R("A32NX_EFIS_L_TO_WPT_IDENT_1"));
        sb.AppendLine("TO WAYPOINT");
        if (string.IsNullOrWhiteSpace(ident))
        {
            sb.AppendLine("  No active waypoint");
        }
        else
        {
            double brg = R("A32NX_EFIS_L_TO_WPT_BEARING") * 180.0 / Math.PI;
            if (brg < 0) brg += 360;
            sb.AppendLine($"  Ident: {ident}");
            sb.AppendLine($"  Distance: {R("A32NX_EFIS_L_TO_WPT_DISTANCE"):0.0} NM");
            sb.AppendLine($"  Bearing: {brg:0} degrees");
            double etaSec = R("A32NX_EFIS_L_TO_WPT_ETA");
            if (etaSec > 0)
            {
                var t = TimeSpan.FromSeconds(etaSec);
                sb.AppendLine($"  ETA: {t.Hours:00}:{t.Minutes:00} UTC");
            }
        }
        sb.AppendLine();

        sb.AppendLine("GUIDANCE");
        sb.AppendLine($"  Cross-track error: {R("A32NX_FG_CROSS_TRACK_ERROR"):0.00} NM");
        double rnp = R("A32NX_FMGC_L_RNP");
        sb.AppendLine($"  RNP: {(rnp > 0 ? rnp.ToString("0.00") + " NM" : "Not set")}");
        sb.AppendLine();

        sb.AppendLine("ILS");
        if (R("A32NX_RADIO_RECEIVER_LOC_IS_VALID") > 0.5)
            sb.AppendLine($"  Localizer deviation: {R("A32NX_RADIO_RECEIVER_LOC_DEVIATION"):0.00}");
        else
            sb.AppendLine("  Localizer: no signal");
        if (R("A32NX_RADIO_RECEIVER_GS_IS_VALID") > 0.5)
            sb.AppendLine($"  Glideslope deviation: {R("A32NX_RADIO_RECEIVER_GS_DEVIATION"):0.00}");
        else
            sb.AppendLine("  Glideslope: no signal");

        return sb.ToString();
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.VarName)) _raw[e.VarName] = e.Value;
    }

    // Open the live DOM-scrape of the real ND Coherent view (mode/range, TO wpt,
    // GS/TAS, TCAS/NAV flags as rendered). Augments the SimVar readout above — the
    // graphical compass/needle values are positional so the SimVar view stays
    // primary, but the scrape catches text the SimVar set doesn't model.
    private void OpenLiveScrape()
        => new FBWA380LiveDisplayForm(_announcer, "A380X_ND_1", "Navigation Display").Show();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { Refresh_(); return true; }
        if (keyData == Keys.F6) { OpenLiveScrape(); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_sim != null) _sim.SimVarUpdated -= OnSimVarUpdated;
        base.OnFormClosed(e);
    }
}
