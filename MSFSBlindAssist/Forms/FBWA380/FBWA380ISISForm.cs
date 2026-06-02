using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// On-demand A380X Integrated Standby Instrument System (ISIS) readout —
/// standby attitude (pitch / bank), heading, indicated airspeed + Mach,
/// standby altitude with its baro reference (STD / QNH), the baro setting,
/// and the LS (ILS) overlay state.
///
/// The ISIS display values themselves are not published as dedicated L:vars
/// (only its knob / baro-mode / LS controls are), so the standby figures are
/// read from the standard attitude/air-data simvars — exactly what the standby
/// instrument shows — combined with the A32NX_ISIS_* mode L:vars. This window
/// duplicates the universal altitude/airspeed hotkeys by design; it gathers the
/// standby picture in one place for cross-checking.
/// </summary>
public class FBWA380ISISForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly Dictionary<string, double> _raw = new();
    private TextBox _text = null!;

    // Standby air-data / attitude simvars (registered with proper units in the
    // aircraft def) plus the ISIS mode L:vars.
    private static readonly string[] Vars =
    {
        "PLANE PITCH DEGREES", "PLANE BANK DEGREES",
        "PLANE HEADING DEGREES MAGNETIC",
        "AIRSPEED INDICATED", "AIRSPEED MACH",
        // The ISIS uses the STANDBY air-data source (index :3), shown in both hPa and
        // inHg, plus body-X accel for the slip/skid ball — exactly what the real ISIS
        // renders (these can differ from the PFD during an ADR fault).
        "INDICATED ALTITUDE:3", "KOHLSMAN SETTING MB:3", "KOHLSMAN_SETTING_INHG_3",
        "ACCELERATION BODY X",
        "A32NX_ISIS_BARO_MODE", "A32NX_ISIS_LS_ACTIVE",
        // LS overlay deviations (only meaningful when LS is active) — the same
        // radio-receiver L:vars the PFD/ND use.
        "A32NX_RADIO_RECEIVER_LOC_IS_VALID", "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
        "A32NX_RADIO_RECEIVER_GS_IS_VALID", "A32NX_RADIO_RECEIVER_GS_DEVIATION",
    };

    /// <summary>L:var / simvar names this window reads (for registration in the aircraft def).</summary>
    public static IEnumerable<string> AllVariableNames() => Vars;

    public FBWA380ISISForm(ScreenReaderAnnouncer announcer, SimConnectManager sim)
    {
        _announcer = announcer;
        _sim = sim;
        BuildUi();
        if (_sim != null) _sim.SimVarUpdated += OnSimVarUpdated;
        Refresh_();
    }

    private void BuildUi()
    {
        Text = "A380 Standby Instruments (ISIS)";
        Size = new Size(700, 440);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        _text = new TextBox
        {
            Location = new Point(12, 12),
            Size = new Size(660, 340),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            AccessibleName = "Standby instrument readout",
            Text = "Loading…"
        };
        var refresh = new Button { Text = "&Refresh", Location = new Point(12, 360), Size = new Size(90, 30), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { Refresh_(); _announcer?.Announce("Refreshed"); };
        var live = new Button { Text = "&Live scrape", Location = new Point(110, 360), Size = new Size(110, 30), AccessibleName = "Live scrape view" };
        live.Click += (_, _) => OpenLiveScrape();
        var close = new Button { Text = "&Close", Location = new Point(228, 360), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { _text, refresh, live, close });
        CancelButton = close;
        AcceptButton = refresh;
        Load += (_, _) => _text.Focus();
    }

    private async void Refresh_()
    {
        if (_sim == null) return;   // same nullable contract as the ctor's event guard
        foreach (var kvp in _sim.GetCachedVariableSnapshot(Vars.ToList())) _raw[kvp.Key] = kvp.Value;
        foreach (var v in Vars) _sim.RequestVariable(v, forceUpdate: true);
        await Task.Delay(500);
        _text.Text = Render();
    }

    private double R(string v) => _raw.TryGetValue(v, out double d) ? d : 0;

    private string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A380 STANDBY INSTRUMENTS (ISIS)");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        // ATTITUDE. MSFS: PLANE PITCH DEGREES is positive nose-DOWN; PLANE BANK
        // DEGREES is positive when banked LEFT. Present as magnitude + direction.
        double pitch = R("PLANE PITCH DEGREES");
        double bank = R("PLANE BANK DEGREES");
        string pitchDir = pitch <= 0 ? "nose up" : "nose down";
        string bankDir = bank >= 0 ? "left" : "right";
        sb.AppendLine("ATTITUDE");
        sb.AppendLine($"  Pitch: {Math.Abs(pitch):0.0} degrees {pitchDir}");
        sb.AppendLine($"  Bank: {Math.Abs(bank):0.0} degrees {bankDir}");
        sb.AppendLine();

        // HEADING
        double hdg = R("PLANE HEADING DEGREES MAGNETIC");
        if (hdg < 0) hdg += 360;
        int hdgInt = (int)Math.Round(hdg) % 360;
        if (hdgInt == 0) hdgInt = 360;
        sb.AppendLine($"Heading: {hdgInt:000} degrees magnetic");
        sb.AppendLine();

        // AIRSPEED
        sb.AppendLine("AIRSPEED");
        sb.AppendLine($"  Indicated: {R("AIRSPEED INDICATED"):0} knots");
        double mach = R("AIRSPEED MACH");
        if (mach >= 0.10) sb.AppendLine($"  Mach: {mach:0.00}");
        sb.AppendLine();

        // ALTITUDE + baro reference (standby ADM, index :3)
        int baroMode = (int)Math.Round(R("A32NX_ISIS_BARO_MODE"));
        string baroRef = baroMode == 1 ? "STD" : "QNH";
        sb.AppendLine("ALTITUDE");
        sb.AppendLine($"  Indicated: {R("INDICATED ALTITUDE:3"):0} feet");
        sb.AppendLine($"  Baro reference: {baroRef}");
        if (baroMode != 1)
        {
            double hpa = R("KOHLSMAN SETTING MB:3");
            double inHg = R("KOHLSMAN_SETTING_INHG_3");
            sb.AppendLine($"  Baro setting: {hpa:0} hectopascals ({inHg:0.00} inHg)");
        }
        sb.AppendLine();

        // SLIP/SKID — the ISIS sideslip ball is driven by lateral (body-X) acceleration,
        // clamped to ±0.3 G. Positive body-X accel = ball to the right ("slip right").
        double accX = R("ACCELERATION BODY X");
        string slip = Math.Abs(accX) < 0.02 ? "centred"
                    : $"{Math.Min(100, Math.Abs(accX) / 0.3 * 100):0}% {(accX > 0 ? "right" : "left")}";
        sb.AppendLine($"Slip/skid: {slip}");
        sb.AppendLine();

        // LS overlay — when active, surface the LOC/G-S deviation in dots (full scale
        // = 2 dots) so the "LS overlay: On" line is actionable on a standby approach.
        bool lsActive = R("A32NX_ISIS_LS_ACTIVE") > 0.5;
        sb.AppendLine($"LS (ILS) overlay: {(lsActive ? "On" : "Off")}");
        if (lsActive)
        {
            if (R("A32NX_RADIO_RECEIVER_LOC_IS_VALID") > 0.5)
            {
                double locDots = R("A32NX_RADIO_RECEIVER_LOC_DEVIATION") / 0.4;
                sb.AppendLine($"  Localizer: {DotsPhrase(locDots, "left", "right")}");
            }
            else sb.AppendLine("  Localizer: no signal");
            if (R("A32NX_RADIO_RECEIVER_GS_IS_VALID") > 0.5)
            {
                double gsDots = R("A32NX_RADIO_RECEIVER_GS_DEVIATION") / 0.4;
                sb.AppendLine($"  Glideslope: {DotsPhrase(gsDots, "up", "down")}");
            }
            else sb.AppendLine("  Glideslope: no signal");
        }

        return sb.ToString();
    }

    // Render an ILS deviation in dots with a direction word (the sign convention:
    // positive deviation = fly-toward is the "neg" word). |dots| < 0.1 reads "centred".
    private static string DotsPhrase(double dots, string negWord, string posWord)
    {
        if (Math.Abs(dots) < 0.1) return "centred";
        string dir = dots > 0 ? posWord : negWord;
        return $"{Math.Min(2.0, Math.Abs(dots)):0.0} dots {dir}";
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.VarName)) _raw[e.VarName] = e.Value;
    }

    // Live DOM-scrape of the real ISIS Coherent view. The standby instrument is a
    // graphical tape (airspeed/altitude/attitude are positional), so the scrape
    // mostly returns the fixed tape tick labels + baro — the SimVar air-data
    // readout above remains the reliable source. Provided for completeness.
    private void OpenLiveScrape()
        => new FBWA380LiveDisplayForm(_announcer, "ISISlegacy", "Standby Instrument").Show();

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
