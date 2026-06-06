using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible OANS / BTV (Brake-To-Vacate) window for the FlyByWire A380X (Ctrl+Shift+B).
/// Snapshot-driven from <see cref="CoherentNDClient"/> (data-only, zero-render — it reads the ND
/// JS instance objects + OANS L:vars and drives BTV via btvUtils/EventBus; it NEVER renders or
/// zooms the airport map). Three native tabs: Map/BTV, Airport, Status. The visual-only OANS
/// controls (TWY/STAND/OTHER, ADD CROSS/FLAG, CENTER MAP, LDG SHIFT) are intentionally absent —
/// they have no perceivable effect for a blind pilot.
/// </summary>
public sealed class FBWA380OansForm : Form
{
    private readonly CoherentNDClient _client;
    private readonly ScreenReaderAnnouncer _announcer;

    private Label _status = null!;
    private TabControl _tabs = null!;

    // Map / BTV
    private ComboBox _btvRunway = null!, _btvExit = null!;
    private Button _armRunway = null!, _armExit = null!, _btvClear = null!;
    private TextBox _btvReadout = null!;
    private Label _manualHeader = null!, _runwayLengthLabel = null!, _fmsHint = null!;
    private TextBox _manualStop = null!;
    private Button _manualStopApply = null!;

    // Airport
    private TextBox _airportCode = null!;
    private Button _displayAirport = null!, _btnOrigin = null!, _btnDest = null!, _btnAltn = null!;
    private TextBox _airportInfo = null!;

    // Status
    private TextBox _statusInfo = null!;

    private Snap _s = new();
    private bool _subscribed, _firstPush = true;
    private string _lastBtvSpoken = "";

    public FBWA380OansForm(CoherentNDClient client, ScreenReaderAnnouncer announcer)
    {
        _client = client; _announcer = announcer; BuildUi();
    }

    private void BuildUi()
    {
        Text = "A380 Airport Map and BTV (OANS)";
        Size = new Size(640, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false; KeyPreview = true;

        _status = new Label { Location = new Point(12, 10), Size = new Size(604, 36), Text = "OANS: connecting…", AccessibleName = "OANS status" };
        _tabs = new TabControl { Location = new Point(12, 50), Size = new Size(604, 504), AccessibleName = "OANS tabs" };
        var tpMap = new TabPage("Map and BTV") { AccessibleName = "Map and BTV" };
        var tpArpt = new TabPage("Airport") { AccessibleName = "Airport" };
        var tpStatus = new TabPage("Status") { AccessibleName = "Status" };
        _tabs.TabPages.AddRange(new[] { tpMap, tpArpt, tpStatus });
        BuildMapTab(tpMap); BuildArptTab(tpArpt); BuildStatusTab(tpStatus);

        var close = new Button { Text = "Close", Location = new Point(12, 562), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();
        Controls.AddRange(new Control[] { _status, _tabs, close });
    }

    private void BuildMapTab(TabPage tp)
    {
        var rwyLabel = new Label { Text = "BTV r&unway:", Location = new Point(12, 14), Size = new Size(86, 22) };
        _btvRunway = new ComboBox { Location = new Point(100, 11), Size = new Size(90, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "BTV runway" };
        _armRunway = new Button { Text = "Arm &runway", Location = new Point(196, 10), Size = new Size(110, 26), AccessibleName = "Arm BTV runway" };
        _armRunway.Click += (_, _) => ArmRunway();
        var exitLabel = new Label { Text = "BTV e&xit:", Location = new Point(320, 14), Size = new Size(60, 22) };
        _btvExit = new ComboBox { Location = new Point(382, 11), Size = new Size(90, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "BTV exit" };
        _armExit = new Button { Text = "Arm e&xit", Location = new Point(478, 10), Size = new Size(96, 26), AccessibleName = "Arm BTV exit" };
        _armExit.Click += (_, _) => ArmExit();
        _btvClear = new Button { Text = "&Clear BTV", Location = new Point(12, 42), Size = new Size(110, 26), AccessibleName = "Clear BTV selection" };
        _btvClear.Click += (_, _) => _client.EnqueueCommand("oans_clear");

        _btvReadout = new TextBox
        {
            Location = new Point(12, 76), Size = new Size(562, 230), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), AccessibleName = "BTV status"
        };

        _manualHeader = new Label { Text = "Manual BTV (no Navigraph maps):", Location = new Point(12, 316), Size = new Size(560, 18), Font = new Font(Font, FontStyle.Bold) };
        _runwayLengthLabel = new Label { Text = "Runway length: —", Location = new Point(12, 338), Size = new Size(560, 18), AccessibleName = "Runway length" };
        var msLabel = new Label { Text = "Stop &distance:", Location = new Point(12, 362), Size = new Size(96, 22) };
        _manualStop = new TextBox { Location = new Point(112, 359), Size = new Size(90, 24), AccessibleName = "Manual BTV stop distance" };
        _manualStopApply = new Button { Text = "&Apply", Location = new Point(208, 358), Size = new Size(80, 26), AccessibleName = "Apply manual stop distance" };
        _manualStopApply.Click += (_, _) => ApplyManualStop();
        _fmsHint = new Label { Text = "Then select the landing runway in the FMS.", Location = new Point(12, 388), Size = new Size(560, 18) };

        tp.Controls.AddRange(new Control[]
        {
            rwyLabel, _btvRunway, _armRunway, exitLabel, _btvExit, _armExit, _btvClear, _btvReadout,
            _manualHeader, _runwayLengthLabel, msLabel, _manualStop, _manualStopApply, _fmsHint
        });
    }

    private void BuildArptTab(TabPage tp)
    {
        var codeLabel = new Label { Text = "Airport &code:", Location = new Point(12, 14), Size = new Size(90, 22) };
        _airportCode = new TextBox { Location = new Point(106, 11), Size = new Size(120, 24), AccessibleName = "Airport code", CharacterCasing = CharacterCasing.Upper };
        _displayAirport = new Button { Text = "&Display airport", Location = new Point(234, 10), Size = new Size(130, 26), AccessibleName = "Display airport" };
        _displayAirport.Click += (_, _) => DisplayTyped();

        var fp = new Label { Text = "Flight-plan airports:", Location = new Point(12, 46), Size = new Size(560, 18) };
        _btnOrigin = new Button { Text = "Origin", Location = new Point(12, 68), Size = new Size(170, 28), AccessibleName = "Display origin airport", Enabled = false };
        _btnDest = new Button { Text = "Destination", Location = new Point(192, 68), Size = new Size(180, 28), AccessibleName = "Display destination airport", Enabled = false };
        _btnAltn = new Button { Text = "Alternate", Location = new Point(382, 68), Size = new Size(180, 28), AccessibleName = "Display alternate airport", Enabled = false };
        _btnOrigin.Click += (_, _) => DisplayIcao(_s.FmsOrigin);
        _btnDest.Click += (_, _) => DisplayIcao(_s.FmsDest);
        _btnAltn.Click += (_, _) => DisplayIcao(_s.FmsAltn);

        var infoLabel = new Label { Text = "Loaded airport:", Location = new Point(12, 106), Size = new Size(560, 18) };
        _airportInfo = new TextBox
        {
            Location = new Point(12, 126), Size = new Size(562, 300), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), AccessibleName = "Loaded airport info"
        };
        tp.Controls.AddRange(new Control[] { codeLabel, _airportCode, _displayAirport, fp, _btnOrigin, _btnDest, _btnAltn, infoLabel, _airportInfo });
    }

    private void BuildStatusTab(TabPage tp)
    {
        var infoLabel = new Label { Text = "Database &status:", Location = new Point(12, 14), Size = new Size(200, 18) };
        _statusInfo = new TextBox
        {
            Location = new Point(12, 34), Size = new Size(562, 400), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), AccessibleName = "Database status"
        };
        tp.Controls.AddRange(new Control[] { infoLabel, _statusInfo });
    }

    public void ShowForm()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront(); Activate(); ActiveControl = _tabs; _tabs.Focus();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            if (!_subscribed) { _client.StateUpdated += OnState; _subscribed = true; }
            _firstPush = true;
            _client.EnqueueCommand("get_snapshot");
        }
        else if (_subscribed) { _client.StateUpdated -= OnState; _subscribed = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        if (keyData == Keys.F5) { _status.Text = "OANS: refreshing…"; _client.EnqueueCommand("get_snapshot"); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnState(object? sender, EFBStateUpdateEventArgs e)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => OnState(sender, e))); } catch { } return; }
        if (e.Type == "oans_connected") { _status.Text = "OANS: connected"; return; }
        if (e.Type == "oans_action") { AnnounceActionResult(e.Data.TryGetValue("result", out var r) ? r : ""); return; }
        if (e.Type != "oans_state") return;

        _s = Snap.Parse(e.Data);
        UpdateBanner();
        UpdateMap();
        UpdateArpt();
        UpdateStatus();
        AnnounceBtvChange();
        _firstPush = false;
    }

    private void UpdateBanner()
    {
        _status.Text = _s.Available
            ? (_s.BtvReady ? $"OANS: full — {(_s.AirportName?.Length > 0 ? _s.AirportName : _s.AirportIcao)}" : "OANS: maps available — load an airport (Airport tab)")
            : "OANS: Navigraph maps NOT loaded — manual BTV only. Log in via the flyPad EFB (Shift+T) → Navigraph.";
    }

    private void UpdateMap()
    {
        SyncCombo(_btvRunway, _s.Runways, _s.BtvRunway);
        SyncCombo(_btvExit, _s.Exits, _s.BtvExit);
        bool armed = !string.IsNullOrEmpty(_s.BtvRunway);
        _armRunway.Enabled = _btvRunway.Items.Count > 0;
        _btvExit.Enabled = armed && _btvExit.Items.Count > 0;
        _armExit.Enabled = armed && _btvExit.Items.Count > 0;
        _btvClear.Enabled = armed;
        DisplayText.SetPreserveCaret(_btvReadout, BtvReadoutBlock());

        // Manual tier visible only without Navigraph maps.
        bool manual = !_s.Available;
        _manualHeader.Visible = manual; _runwayLengthLabel.Visible = manual;
        _manualStop.Enabled = manual; _manualStopApply.Enabled = manual; _fmsHint.Visible = manual;
        _runwayLengthLabel.Text = _s.RunwayLengthM != null ? $"Runway length: {Dist(_s.RunwayLengthM.Value)}" : "Runway length: —";
    }

    private void UpdateArpt()
    {
        _btnOrigin.Text = _s.FmsOrigin is { Length: > 0 } ? $"Origin {_s.FmsOrigin}" : "Origin";
        _btnDest.Text = _s.FmsDest is { Length: > 0 } ? $"Destination {_s.FmsDest}" : "Destination";
        _btnAltn.Text = _s.FmsAltn is { Length: > 0 } ? $"Alternate {_s.FmsAltn}" : "Alternate";
        _btnOrigin.Enabled = _s.FmsOrigin is { Length: > 0 };
        _btnDest.Enabled = _s.FmsDest is { Length: > 0 };
        _btnAltn.Enabled = _s.FmsAltn is { Length: > 0 };
        string info = string.IsNullOrEmpty(_s.AirportIcao) ? "No airport loaded." : $"{_s.AirportName} ({_s.AirportIcao})";
        DisplayText.SetPreserveCaret(_airportInfo, info);
    }

    private void UpdateStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine(_s.Available ? "Airport database: loaded" : "Airport database: NOT loaded (no Navigraph)");
        if (!string.IsNullOrEmpty(_s.Airac)) sb.AppendLine($"AIRAC cycle: {_s.Airac}");
        if (_s.Failed) sb.AppendLine("OANS: FAILED");
        DisplayText.SetPreserveCaret(_statusInfo, sb.ToString().TrimEnd());
    }

    private string Dist(int metres) => _s.Metric ? $"{metres} m" : $"{(int)Math.Round(metres * 3.280839895)} ft";

    private string BtvReadoutBlock()
    {
        if (!_s.BtvReady)
            return _s.Available
                ? "BTV: load an airport on the Airport tab to get the runway/exit lists."
                : "BTV: Navigraph maps not loaded — use Manual BTV below, or log in via the flyPad EFB.";
        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrEmpty(_s.BtvRunway) ? "Runway: none selected"
            : $"Runway: {_s.BtvRunway}" + (_s.Lda != null ? $" (landing distance available {Dist(_s.Lda.Value)})" : ""));
        sb.AppendLine(string.IsNullOrEmpty(_s.BtvExit) ? "Exit: none selected"
            : $"Exit: {_s.BtvExit}" + (_s.ExitDist != null ? $" ({Dist(_s.ExitDist.Value)} from threshold)" : ""));
        if (_s.Computing && (_s.Dry != null || _s.Wet != null))
        {
            if (_s.Dry != null) sb.AppendLine($"Predicted stop, dry runway: {Dist(_s.Dry.Value)}");
            if (_s.Wet != null) sb.AppendLine($"Predicted stop, wet runway: {Dist(_s.Wet.Value)}");
            if (_s.Stop != null) sb.AppendLine($"Live stop-bar distance: {Dist(_s.Stop.Value)}");
        }
        else sb.AppendLine("Predicted stopping distances: not yet computed (compute on approach)");
        if (!string.IsNullOrEmpty(_s.BtvExit))
        {
            if (_s.Rot != null) sb.AppendLine($"Runway occupancy time: {_s.Rot} seconds");
            if (_s.TurnMax != null && _s.TurnIdle != null)
                sb.AppendLine($"Turnaround: {_s.TurnMax} minutes max reverse, {_s.TurnIdle} minutes idle reverse");
        }
        if (!string.IsNullOrEmpty(_s.RwyAheadQfu)) sb.AppendLine($"Caution: runway {_s.RwyAheadQfu} ahead");
        return sb.ToString().TrimEnd();
    }

    private void ArmRunway()
    {
        if (_btvRunway.SelectedItem is not string rwy) { _announcer?.Announce("No runway selected"); return; }
        _client.EnqueueCommand("oans_arm_runway", new Dictionary<string, string> { ["value"] = rwy });
        // Success is announced by AnnounceBtvChange (snapshot-driven); a failure (e.g. "runway not
        // found") is announced by AnnounceActionResult when the command result comes back.
    }

    private void ArmExit()
    {
        if (string.IsNullOrEmpty(_s.BtvRunway)) { _announcer?.Announce("Select a BTV runway first"); return; }
        if (_btvExit.SelectedItem is not string ex) { _announcer?.Announce("No exit selected"); return; }
        _client.EnqueueCommand("oans_arm_exit", new Dictionary<string, string> { ["value"] = ex });
        // Success → AnnounceBtvChange (snapshot); rejection ("Exit X not valid for this runway …")
        // → AnnounceActionResult, so an invalid exit is heard instead of silently doing nothing.
    }

    private void ApplyManualStop()
    {
        if (!int.TryParse(_manualStop.Text.Trim(), out var m) || m <= 0) { _announcer?.Announce("Enter a stop distance in metres"); return; }
        _client.EnqueueCommand("oans_set_manual_stop", new Dictionary<string, string> { ["value"] = m.ToString() });
        _announcer?.Announce($"Manual stop distance {m} metres. Select the landing runway in the FMS.");
    }

    private void DisplayTyped()
    {
        string icao = _airportCode.Text.Trim();
        if (icao.Length is < 3 or > 4) { _announcer?.Announce("Enter a 3 or 4 letter airport code"); return; }
        DisplayIcao(icao);
    }

    private void DisplayIcao(string? icao)
    {
        if (string.IsNullOrEmpty(icao)) return;
        _client.EnqueueCommand("oans_display_airport", new Dictionary<string, string> { ["value"] = icao });
        _announcer?.Announce($"Loading airport {icao}");
    }

    private void AnnounceBtvChange()
    {
        string key = !_s.BtvReady ? "" : string.IsNullOrEmpty(_s.BtvRunway) ? "none" : $"{_s.BtvRunway}|{_s.BtvExit}";
        if (_firstPush) { _lastBtvSpoken = key; return; }
        if (key == _lastBtvSpoken) return;
        _lastBtvSpoken = key;
        if (key.Length == 0) return;
        if (key == "none") { _announcer?.Announce("BTV cleared"); return; }
        string spoken = $"runway {_s.BtvRunway}" +
            (string.IsNullOrEmpty(_s.BtvExit) ? ", no exit"
                : $", exit {_s.BtvExit}" + (_s.ExitDist != null ? $", {Dist(_s.ExitDist.Value)}" : ""));
        _announcer?.Announce("BTV armed, " + spoken);
    }

    // Speak the result of an arm/clear command. Successful arming and clearing are already
    // announced by AnnounceBtvChange (snapshot-driven) and the manual-stop confirmation by
    // ApplyManualStop — so here we only surface PROBLEMS (e.g. "Exit K1 not valid for this
    // runway (wrong side or too close to threshold)"), which would otherwise fail silently.
    private void AnnounceActionResult(string res)
    {
        if (string.IsNullOrWhiteSpace(res)) return;
        if (res.StartsWith("Armed", StringComparison.OrdinalIgnoreCase)) return;
        if (res.StartsWith("BTV selection cleared", StringComparison.OrdinalIgnoreCase)) return;
        if (res.StartsWith("manual stop distance", StringComparison.OrdinalIgnoreCase)) return;
        _announcer?.Announce(res);
    }

    private static void SyncCombo(ComboBox combo, List<string> options, string? armed)
    {
        if (combo.Focused || combo.DroppedDown) return;
        bool same = combo.Items.Count == options.Count;
        if (same) for (int i = 0; i < options.Count; i++) if ((combo.Items[i] as string) != options[i]) { same = false; break; }
        string? keep = combo.SelectedItem as string;
        if (!same) { combo.BeginUpdate(); combo.Items.Clear(); foreach (var o in options) combo.Items.Add(o); combo.EndUpdate(); }
        string? want = !string.IsNullOrEmpty(armed) ? StripIcao(armed!, options) : keep;
        if (want != null) { int ix = combo.Items.IndexOf(want); if (ix >= 0 && combo.SelectedIndex != ix) combo.SelectedIndex = ix; }
        else if (combo.Items.Count > 0 && combo.SelectedIndex < 0) combo.SelectedIndex = 0;
    }

    private static string? StripIcao(string armed, List<string> options)
    {
        foreach (var o in options) if (armed.EndsWith(o, StringComparison.Ordinal)) return o;
        return options.Contains(armed) ? armed : null;
    }

    private sealed class Snap
    {
        public bool Available, Failed, BtvReady, Metric, Computing;
        public string? AirportIcao, AirportName, Airac, FmsOrigin, FmsDest, FmsAltn;
        public string? BtvRunway, BtvExit, RwyAheadQfu;
        public int? Lda, ExitDist, Dry, Wet, Stop, Rot, TurnMax, TurnIdle, RunwayLengthM, ManualStopDist;
        public List<string> Runways = new(); public List<string> Exits = new();

        public static Snap Parse(Dictionary<string, string> d)
        {
            string S(string k) => d.TryGetValue(k, out var v) ? v : "";
            bool B(string k) => d.TryGetValue(k, out var v) && v == "true";
            int? I(string k) => d.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? n : null;
            List<string> L(string k) => d.TryGetValue(k, out var v) && v.Length > 0 ? new List<string>(v.Split((char)0x1f)) : new List<string>();
            return new Snap
            {
                Available = B("available"), Failed = B("failed"), BtvReady = B("btv.ready"),
                Metric = B("btv.metric"), Computing = B("btv.computing"),
                AirportIcao = S("airport.icao"), AirportName = S("airport.name"), Airac = S("airac"),
                FmsOrigin = S("fms.origin"), FmsDest = S("fms.dest"), FmsAltn = S("fms.altn"),
                BtvRunway = S("btv.runway"), BtvExit = S("btv.exit"), RwyAheadQfu = S("btv.rwyAheadQfu"),
                Lda = I("btv.lda"), ExitDist = I("btv.exitDist"), Dry = I("btv.dry"), Wet = I("btv.wet"),
                Stop = I("btv.stop"), Rot = I("btv.rot"), TurnMax = I("btv.turnMax"), TurnIdle = I("btv.turnIdle"),
                RunwayLengthM = I("manual.runwayLengthM"), ManualStopDist = I("manual.manualStopDist"),
                Runways = L("btv.runways"), Exits = L("btv.exits")
            };
        }
    }
}
