using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible OANS (Onboard Airport Navigation System) + BTV window for the FlyByWire A380X
/// (Ctrl+Shift+B, input mode). NATIVE WinForms with a real <see cref="TabControl"/> mirroring
/// the cockpit OANS tabs — MAP DATA / ARPT SEL / STATUS — so a screen-reader user moves between
/// them with the arrow keys / Ctrl+Tab and sees ONLY the active tab's controls (no cross-tab
/// clutter), each as a real combo / edit / button / list.
///
/// Driven by <see cref="CoherentNDClient"/> + Resources/coherent-oans-agent.js over the MSFS
/// Coherent debugger. Switching the native tab clicks the matching cockpit OANS tab; the scrape
/// (already tab-scoped — the OANS only renders the active tab) then drives that page's controls.
/// Each page has FIXED native controls updated in place from the scrape (so a live refresh never
/// rebuilds them or moves focus); button idx churn is handled by matching on the button TEXT.
///
///   • Map Data: mode combo (RWY/TWY/STAND/OTHER), "Runway or exit" search, ADD CROSS / CENTER
///     MAP / ADD FLAG / LDG SHIFT buttons, AND the accessible BTV exit arming (runway + exit
///     combos + Arm/Clear + the predicted distances / ROT / turnaround) — BTV is a map/runway
///     thing, so it lives here, never on STATUS.
///   • ARPT SEL: code-type combo (ICAO/IATA/CITY), airport-code search, DISPLAY AIRPORT, the
///     airport-info readout, and the recent-airports list.
///   • Status: the AMDB database cycle readout + SWAP.
/// </summary>
public sealed class FBWA380OansForm : Form
{
    private readonly CoherentNDClient _client;
    private readonly ScreenReaderAnnouncer _announcer;

    private Label _status = null!;
    private TabControl _tabs = null!;

    // Map Data tab — RWY/TWY/STAND/OTHER is a RadioButtonGroup in the cockpit, so native radios.
    private RadioButton _modeRwy = null!, _modeTwy = null!, _modeStand = null!, _modeOther = null!;
    private TextBox _mapSearch = null!;
    private Button _mapSearchBtn = null!;
    private Button _addCross = null!, _centerMap = null!, _addFlag = null!, _ldgShift = null!;
    private ComboBox _btvRunway = null!, _btvExit = null!;
    private Button _armRunway = null!, _armExit = null!, _btvClear = null!;
    private TextBox _btvReadout = null!;

    // ARPT SEL tab — ICAO/IATA/CITY NAME is a RadioButtonGroup in the cockpit, so native radios.
    private RadioButton _typeIcao = null!, _typeIata = null!, _typeCity = null!;
    private TextBox _airportCode = null!;
    private Button _displayAirport = null!;
    private TextBox _airportInfo = null!;
    private ListBox _recent = null!;

    // Status tab
    private TextBox _statusInfo = null!;
    private Button _swap = null!;

    private List<Elem> _elems = new();
    private Btv _btv = new();
    private bool _subscribed;
    private bool _firstPush = true;
    private bool _switchingTab;          // guard: programmatic TabControl change vs user change
    private bool _syncingRadios;         // guard: programmatic radio Check vs user click
    private string _lastBtvSpoken = "";

    public FBWA380OansForm(CoherentNDClient client, ScreenReaderAnnouncer announcer)
    {
        _client = client;
        _announcer = announcer;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "A380 Airport Map and BTV (OANS)";
        Size = new Size(640, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        KeyPreview = true;

        _status = new Label { Location = new Point(12, 10), Size = new Size(604, 20), Text = "OANS: connecting…", AccessibleName = "OANS status" };

        _tabs = new TabControl { Location = new Point(12, 34), Size = new Size(604, 520), AccessibleName = "OANS tabs" };
        var tpMap = new TabPage("Map Data") { AccessibleName = "Map Data" };
        var tpArpt = new TabPage("Airport Select") { AccessibleName = "Airport Select" };
        var tpStatus = new TabPage("Status") { AccessibleName = "Status" };
        _tabs.TabPages.AddRange(new[] { tpMap, tpArpt, tpStatus });
        _tabs.SelectedIndexChanged += OnTabChanged;

        BuildMapTab(tpMap);
        BuildArptTab(tpArpt);
        BuildStatusTab(tpStatus);

        var close = new Button { Text = "Close", Location = new Point(12, 562), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();

        Controls.AddRange(new Control[] { _status, _tabs, close });
    }

    private void BuildMapTab(TabPage tp)
    {
        // RWY / TWY / STAND / OTHER as a native radio group (mirrors the cockpit RadioButtonGroup).
        var modeGroup = new GroupBox { Text = "Map data mode", Location = new Point(12, 6), Size = new Size(562, 48), AccessibleName = "Map data mode" };
        _modeRwy = AddRadio(modeGroup, "RWY", 12);
        _modeTwy = AddRadio(modeGroup, "TWY", 92);
        _modeStand = AddRadio(modeGroup, "STAND", 172);
        _modeOther = AddRadio(modeGroup, "OTHER", 272);

        var searchLabel = new Label { Text = "Runway or e&xit:", Location = new Point(12, 64), Size = new Size(110, 22) };
        _mapSearch = new TextBox { Location = new Point(128, 61), Size = new Size(120, 24), AccessibleName = "Runway or exit search" };
        _mapSearchBtn = new Button { Text = "&Search", Location = new Point(256, 60), Size = new Size(80, 26), AccessibleName = "Search runway or exit" };
        _mapSearchBtn.Click += (_, _) => SetInput(_mapSearch.Text);

        _addCross = MapActionButton("ADD CROSS", new Point(12, 94));
        _centerMap = MapActionButton("CENTER MAP ON", new Point(160, 94));
        _addFlag = MapActionButton("ADD FLAG", new Point(308, 94));
        _ldgShift = MapActionButton("LDG SHIFT", new Point(440, 94));

        var btvLabel = new Label { Text = "BTV exit selection:", Location = new Point(12, 130), Size = new Size(560, 18), Font = new Font(Font, FontStyle.Bold) };
        var rwyLabel = new Label { Text = "BTV r&unway:", Location = new Point(12, 154), Size = new Size(86, 22) };
        _btvRunway = new ComboBox { Location = new Point(100, 151), Size = new Size(86, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "BTV runway" };
        _armRunway = new Button { Text = "Arm &runway", Location = new Point(192, 150), Size = new Size(110, 26), AccessibleName = "Arm BTV runway" };
        _armRunway.Click += (_, _) => ArmRunway();
        var exitLabel = new Label { Text = "BTV e&xit:", Location = new Point(316, 154), Size = new Size(64, 22) };
        _btvExit = new ComboBox { Location = new Point(382, 151), Size = new Size(86, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "BTV exit" };
        _armExit = new Button { Text = "Arm e&xit", Location = new Point(474, 150), Size = new Size(100, 26), AccessibleName = "Arm BTV exit" };
        _armExit.Click += (_, _) => ArmExit();
        _btvClear = new Button { Text = "&Clear BTV", Location = new Point(12, 182), Size = new Size(110, 26), AccessibleName = "Clear BTV selection" };
        _btvClear.Click += (_, _) => { _client.EnqueueCommand("oans_btv_clear"); _announcer?.Announce("Clearing BTV selection"); };

        _btvReadout = new TextBox
        {
            Location = new Point(12, 214), Size = new Size(562, 214),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10), AccessibleName = "BTV status", Text = ""
        };

        tp.Controls.AddRange(new Control[]
        {
            modeGroup, searchLabel, _mapSearch, _mapSearchBtn,
            _addCross, _centerMap, _addFlag, _ldgShift,
            btvLabel, rwyLabel, _btvRunway, _armRunway, exitLabel, _btvExit, _armExit, _btvClear, _btvReadout
        });
    }

    // Add a native RadioButton to a group; a user click (not a programmatic sync) clicks the
    // matching cockpit OANS radio element.
    private RadioButton AddRadio(GroupBox g, string label, int x)
    {
        var rb = new RadioButton { Text = label, Location = new Point(x, 20), Size = new Size(Math.Max(56, 18 + label.Length * 9), 22), AccessibleName = label };
        rb.CheckedChanged += (_, _) => { if (rb.Checked && !_syncingRadios) ClickRadio(label); };
        g.Controls.Add(rb);
        return rb;
    }

    private Button MapActionButton(string text, Point loc)
    {
        var b = new Button { Text = Title(text), Location = loc, Size = new Size(text == "CENTER MAP ON" ? 140 : (text == "LDG SHIFT" ? 110 : 120), 26), AccessibleName = Title(text) };
        b.Click += (_, _) => ClickByText(text);
        return b;
    }

    private void BuildArptTab(TabPage tp)
    {
        // ICAO / IATA / CITY NAME as a native radio group (mirrors the cockpit RadioButtonGroup).
        var typeGroup = new GroupBox { Text = "Code type", Location = new Point(12, 6), Size = new Size(562, 48), AccessibleName = "Airport code type" };
        _typeIcao = AddRadio(typeGroup, "ICAO", 12);
        _typeIata = AddRadio(typeGroup, "IATA", 92);
        _typeCity = AddRadio(typeGroup, "CITY NAME", 172);

        var codeLabel = new Label { Text = "Airport &code:", Location = new Point(12, 64), Size = new Size(90, 22) };
        _airportCode = new TextBox { Location = new Point(106, 61), Size = new Size(120, 24), AccessibleName = "Airport code", CharacterCasing = CharacterCasing.Upper };
        _displayAirport = new Button { Text = "&Display airport", Location = new Point(234, 60), Size = new Size(130, 26), AccessibleName = "Display airport" };
        _displayAirport.Click += (_, _) => { SetInput(_airportCode.Text); ClickByText("DISPLAY AIRPORT"); };

        var infoLabel = new Label { Text = "Airport &info:", Location = new Point(12, 94), Size = new Size(120, 18) };
        _airportInfo = new TextBox
        {
            Location = new Point(12, 114), Size = new Size(562, 110),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10), AccessibleName = "Airport info", Text = ""
        };

        var recentLabel = new Label { Text = "&Recent airports (Enter to select):", Location = new Point(12, 232), Size = new Size(320, 18) };
        _recent = new ListBox { Location = new Point(12, 252), Size = new Size(200, 170), AccessibleName = "Recent airports" };
        _recent.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; ActivateRecent(); } };
        _recent.DoubleClick += (_, _) => ActivateRecent();

        tp.Controls.AddRange(new Control[] { typeGroup, codeLabel, _airportCode, _displayAirport, infoLabel, _airportInfo, recentLabel, _recent });
    }

    private void BuildStatusTab(TabPage tp)
    {
        var infoLabel = new Label { Text = "Database &status:", Location = new Point(12, 14), Size = new Size(200, 18) };
        _statusInfo = new TextBox
        {
            Location = new Point(12, 34), Size = new Size(562, 380),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10), AccessibleName = "Database status", Text = ""
        };
        _swap = new Button { Text = "S&wap database", Location = new Point(12, 422), Size = new Size(140, 28), AccessibleName = "Swap database" };
        _swap.Click += (_, _) => ClickByText("SWAP");
        tp.Controls.AddRange(new Control[] { infoLabel, _statusInfo, _swap });
    }

    // ---- lifecycle ---------------------------------------------------------

    public void ShowForm()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        ActiveControl = _tabs;
        _tabs.Focus();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            if (!_subscribed) { _client.StateUpdated += OnState; _subscribed = true; }
            _firstPush = true;
            _client.EnqueueCommand("get_display_elements");
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
        if (keyData == Keys.F5) { _status.Text = "OANS: refreshing…"; _client.EnqueueCommand("get_display_elements"); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // User switched the native tab → drive the cockpit OANS to the matching tab; the scrape
    // (tab-scoped) then populates that page. Guarded so our own programmatic selection doesn't
    // fire a click loop.
    private void OnTabChanged(object? sender, EventArgs e)
    {
        if (_switchingTab) return;
        string oansTab = _tabs.SelectedIndex switch { 0 => "MAP DATA", 1 => "ARPT SEL", _ => "STATUS" };
        ClickTab(oansTab);
        _status.Text = $"OANS: {oansTab}…";
    }

    // ---- state intake ------------------------------------------------------

    private void OnState(object? sender, EFBStateUpdateEventArgs e)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => OnState(sender, e))); } catch { } return; }
        if (e.Type == "fbw_efb_connected") { _status.Text = "OANS: connected"; return; }
        if (e.Type != "fbw_efb_elements") return;

        ParseElements(e.Data);
        ParseBtv(e.Data);

        string active = CurrentTab();
        SelectNativeTab(active);          // keep the native tab in sync with the cockpit tab
        UpdateActivePage(active);
        AnnounceBtvChange();
        _status.Text = _btv.Ready || _elems.Count > 0 ? $"OANS: live ({active})" : "OANS: not available (no airport in range)";
        _firstPush = false;
    }

    private void SelectNativeTab(string active)
    {
        int idx = active switch { "ARPT SEL" => 1, "STATUS" => 2, _ => 0 };
        if (_tabs.SelectedIndex == idx) return;
        _switchingTab = true;
        try { _tabs.SelectedIndex = idx; } finally { _switchingTab = false; }
    }

    private void UpdateActivePage(string active)
    {
        if (active == "ARPT SEL") UpdateArptPage();
        else if (active == "STATUS") UpdateStatusPage();
        else UpdateMapPage();
    }

    // ---- Map Data page -----------------------------------------------------

    private void UpdateMapPage()
    {
        // Native radio group from the RWY/TWY/STAND/OTHER cockpit radios.
        SyncRadios(_modeRwy, _modeTwy, _modeStand, _modeOther);
        // Search field placeholder value (read-only-ish; user types a new search).
        // Action buttons: enable per the live dimmed state, idx looked up by text at click time.
        _addCross.Enabled = ButtonEnabled("ADD CROSS");
        _centerMap.Enabled = ButtonEnabled("CENTER MAP ON");
        _addFlag.Enabled = ButtonEnabled("ADD FLAG");
        _ldgShift.Enabled = ButtonEnabled("LDG SHIFT");
        // BTV combos/buttons + readout.
        SyncCombo(_btvRunway, _btv.Runways, _btv.Runway);
        SyncCombo(_btvExit, _btv.Exits, _btv.Exit);
        bool armed = !string.IsNullOrEmpty(_btv.Runway);
        _btvExit.Enabled = armed && _btvExit.Items.Count > 0;
        _armExit.Enabled = armed && _btvExit.Items.Count > 0;
        _armRunway.Enabled = _btvRunway.Items.Count > 0;
        _btvClear.Enabled = armed;
        DisplayText.SetPreserveCaret(_btvReadout, BtvReadoutBlock());
    }

    // ---- ARPT SEL page -----------------------------------------------------

    private void UpdateArptPage()
    {
        SyncRadios(_typeIcao, _typeIata, _typeCity);
        DisplayText.SetPreserveCaret(_airportInfo, string.Join(Environment.NewLine, TextLines()));
        // Recent-airports list = the short clickable airport-code buttons (e.g. VCBI / LIMC),
        // i.e. uppercase 3-4 letter button texts that aren't the DISPLAY AIRPORT action.
        var recent = _elems.Where(x => x.Clickable && x.Kind == "button"
                        && System.Text.RegularExpressions.Regex.IsMatch(x.Text, "^[A-Z]{3,4}$"))
                        .Select(x => x.Text).Distinct().ToList();
        if (RecentDiffers(recent) && !_recent.Focused)
        {
            _recent.BeginUpdate(); _recent.Items.Clear();
            foreach (var a in recent) _recent.Items.Add(a);
            _recent.EndUpdate();
        }
    }

    private bool RecentDiffers(List<string> next)
    {
        if (next.Count != _recent.Items.Count) return true;
        for (int i = 0; i < next.Count; i++) if ((_recent.Items[i] as string) != next[i]) return true;
        return false;
    }

    private void ActivateRecent()
    {
        if (_recent.SelectedItem is string a) ClickByText(a);
    }

    // ---- Status page -------------------------------------------------------

    private void UpdateStatusPage()
    {
        DisplayText.SetPreserveCaret(_statusInfo, string.Join(Environment.NewLine, TextLines()));
        _swap.Enabled = ButtonEnabled("SWAP");
    }

    // ---- shared scrape helpers --------------------------------------------

    private List<string> TextLines()
        => _elems.Where(e => e.Kind == "text" && !string.IsNullOrWhiteSpace(e.Text)).Select(e => e.Text).ToList();

    // Check the native radio matching the cockpit's "(selected)" radio. No-op while the user is
    // on the group (any radio focused) so a live refresh can't fight mid-navigation.
    private void SyncRadios(params RadioButton[] radios)
    {
        if (Array.Exists(radios, r => r.Focused)) return;
        var sel = _elems.FirstOrDefault(e => e.Kind == "radio" && e.Text.IndexOf("(selected)", StringComparison.OrdinalIgnoreCase) >= 0);
        string? selText = sel != null ? StripMarker(sel.Text) : null;
        _syncingRadios = true;
        try
        {
            foreach (var rb in radios)
            {
                bool want = selText != null && rb.Text.Equals(selText, StringComparison.OrdinalIgnoreCase);
                if (rb.Checked != want) rb.Checked = want;
            }
        }
        finally { _syncingRadios = false; }
    }

    private void ClickRadio(string text)
    {
        var r = _elems.FirstOrDefault(e => e.Kind == "radio" && StripMarker(e.Text).Equals(text, StringComparison.OrdinalIgnoreCase));
        if (r != null) _client.EnqueueCommand("click_display_element", new Dictionary<string, string> { ["index"] = r.Aidx.ToString() });
    }

    private void ClickByText(string text)
    {
        var b = _elems.FirstOrDefault(e => e.Clickable && e.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        if (b != null) _client.EnqueueCommand("click_display_element", new Dictionary<string, string> { ["index"] = b.Aidx.ToString() });
    }

    private void ClickTab(string text)
    {
        var t = _elems.FirstOrDefault(e => e.Kind == "tab" && e.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase));
        if (t != null) _client.EnqueueCommand("click_display_element", new Dictionary<string, string> { ["index"] = t.Aidx.ToString() });
    }

    private bool ButtonEnabled(string text)
    {
        var b = _elems.FirstOrDefault(e => e.Kind == "button" && e.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        return b != null && !b.Disabled;
    }

    private void SetInput(string value)
    {
        var inp = _elems.FirstOrDefault(e => e.Kind == "input");
        if (inp == null) { _announcer?.Announce("No input field on this tab"); return; }
        if (string.IsNullOrWhiteSpace(value)) return;
        _client.EnqueueCommand("set_element_value", new Dictionary<string, string> { ["index"] = inp.Aidx.ToString(), ["value"] = value.Trim() });
        _announcer?.Announce($"Searching {value.Trim()}");
    }

    private string CurrentTab()
    {
        foreach (var e in _elems)
            if (e.Kind == "tab" && e.Text.IndexOf("(active tab)", StringComparison.OrdinalIgnoreCase) >= 0)
                return StripMarker(e.Text).ToUpperInvariant();
        return "MAP DATA";
    }

    private static string StripMarker(string t)
        => t.Replace("(selected)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(active tab)", "", StringComparison.OrdinalIgnoreCase).Trim();

    // ---- BTV (Map Data) ----------------------------------------------------

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

    private string Dist(int metres) => _btv.Metric ? $"{metres} m" : $"{(int)Math.Round(metres * 3.280839895)} ft";

    private string BtvReadoutBlock()
    {
        if (!_btv.Ready) return "BTV: not available — fly within OANS range of an airport with a Navigraph AMDB map.";
        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrEmpty(_btv.Runway)
            ? "Runway: none selected"
            : $"Runway: {_btv.Runway}" + (_btv.Lda != null ? $" (landing distance available {Dist(_btv.Lda.Value)})" : ""));
        sb.AppendLine(string.IsNullOrEmpty(_btv.Exit)
            ? "Exit: none selected"
            : $"Exit: {_btv.Exit}" + (_btv.ExitDist != null ? $" ({Dist(_btv.ExitDist.Value)} from threshold)" : ""));
        if (_btv.Computing && (_btv.Dry != null || _btv.Wet != null))
        {
            if (_btv.Dry != null) sb.AppendLine($"Predicted stop, dry runway: {Dist(_btv.Dry.Value)}");
            if (_btv.Wet != null) sb.AppendLine($"Predicted stop, wet runway: {Dist(_btv.Wet.Value)}");
            if (_btv.Stop != null) sb.AppendLine($"Live stop-bar distance: {Dist(_btv.Stop.Value)}");
        }
        else sb.AppendLine("Predicted stopping distances: not yet computed");
        if (!string.IsNullOrEmpty(_btv.Exit))
        {
            if (_btv.Rot != null) sb.AppendLine($"Runway occupancy time: {_btv.Rot} seconds");
            if (_btv.TurnMax != null && _btv.TurnIdle != null)
                sb.AppendLine($"Turnaround: {_btv.TurnMax} minutes max reverse, {_btv.TurnIdle} minutes idle reverse");
        }
        return sb.ToString().TrimEnd();
    }

    private void ArmRunway()
    {
        if (_btvRunway.SelectedItem is not string rwy) { _announcer?.Announce("No runway selected"); return; }
        _client.EnqueueCommand("oans_btv_arm_runway", new Dictionary<string, string> { ["value"] = rwy });
        _announcer?.Announce($"Arming runway {rwy}");
    }

    private void ArmExit()
    {
        if (string.IsNullOrEmpty(_btv.Runway)) { _announcer?.Announce("Select a BTV runway first"); return; }
        if (_btvExit.SelectedItem is not string ex) { _announcer?.Announce("No exit selected"); return; }
        _client.EnqueueCommand("oans_btv_arm_exit", new Dictionary<string, string> { ["value"] = ex });
        _announcer?.Announce($"Arming exit {ex}");
    }

    private void AnnounceBtvChange()
    {
        string key = !_btv.Ready ? "" :
            string.IsNullOrEmpty(_btv.Runway) ? "none" : $"{_btv.Runway}|{_btv.Exit}";
        if (_firstPush) { _lastBtvSpoken = key; return; }
        if (key == _lastBtvSpoken) return;
        _lastBtvSpoken = key;
        if (key.Length == 0) return;
        if (key == "none") { _announcer?.Announce("BTV cleared"); return; }
        string spoken = $"runway {_btv.Runway}" +
                        (string.IsNullOrEmpty(_btv.Exit) ? ", no exit"
                            : $", exit {_btv.Exit}" + (_btv.ExitDist != null ? $", {Dist(_btv.ExitDist.Value)}" : ""));
        _announcer?.Announce("BTV armed, " + spoken);
    }

    // ---- parse -------------------------------------------------------------

    private void ParseElements(Dictionary<string, string> d)
    {
        int count = d.TryGetValue("count", out var c) && int.TryParse(c, out var n) ? n : 0;
        var list = new List<Elem>(count);
        for (int i = 0; i < count; i++)
        {
            string p = $"items.{i}.";
            list.Add(new Elem
            {
                Aidx = d.TryGetValue(p + "aidx", out var a) && int.TryParse(a, out var ai) ? ai : 0,
                Text = d.TryGetValue(p + "text", out var tx) ? tx : "",
                Kind = d.TryGetValue(p + "kind", out var k) ? k : "",
                Value = d.TryGetValue(p + "value", out var v) ? v : "",
                Clickable = d.TryGetValue(p + "clickable", out var cl) && cl == "true",
                Disabled = d.TryGetValue(p + "disabled", out var di) && di == "true"
            });
        }
        _elems = list;
    }

    private void ParseBtv(Dictionary<string, string> d)
    {
        var b = new Btv();
        if (d.TryGetValue("btv.ready", out var r)) b.Ready = r == "true";
        d.TryGetValue("btv.runway", out b.Runway!);
        d.TryGetValue("btv.exit", out b.Exit!);
        b.Lda = PI(d, "btv.lda"); b.ExitDist = PI(d, "btv.exitDist");
        b.Dry = PI(d, "btv.dry"); b.Wet = PI(d, "btv.wet"); b.Stop = PI(d, "btv.stop");
        b.Rot = PI(d, "btv.rot"); b.TurnMax = PI(d, "btv.turnMax"); b.TurnIdle = PI(d, "btv.turnIdle");
        b.Metric = !d.TryGetValue("btv.metric", out var mt) || mt != "false";
        if (d.TryGetValue("btv.computing", out var cm)) b.Computing = cm == "true";
        b.Runways = Split(d, "btv.runways"); b.Exits = Split(d, "btv.exits");
        _btv = b;
    }

    private static int? PI(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var s) && int.TryParse(s, out var n) ? n : (int?)null;
    private static List<string> Split(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var s) && s.Length > 0 ? new List<string>(s.Split((char)0x1f)) : new List<string>();

    private static string Title(string upper)
    {
        if (string.IsNullOrEmpty(upper)) return upper;
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(upper.ToLowerInvariant());
    }

    private sealed class Elem { public int Aidx; public string Text = ""; public string Kind = ""; public string Value = ""; public bool Clickable; public bool Disabled; }
    private sealed class Btv
    {
        public bool Ready; public List<string> Runways = new(); public List<string> Exits = new();
        public string? Runway; public string? Exit; public int? Lda; public int? ExitDist;
        public int? Dry; public int? Wet; public int? Stop; public int? Rot; public int? TurnMax; public int? TurnIdle;
        public bool Metric = true; public bool Computing;
    }
}
