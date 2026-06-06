using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible OANS (Onboard Airport Navigation System) + BTV window for the FlyByWire A380X
/// (Ctrl+Shift+B, input mode). NATIVE WinForms — real combo boxes / buttons / list, so a
/// screen reader gets correct control roles and selected-state (the WebView shell rendered
/// radios as checkboxes whose checked state didn't track — fixed here by not using HTML at all).
///
/// Driven by <see cref="CoherentNDClient"/> + Resources/coherent-oans-agent.js over the MSFS
/// Coherent debugger (same transport as the MCDU / flyPad). The agent pushes:
///   • the scraped OANS control-panel ELEMENTS (tabs / RWY-TWY-STAND-OTHER mode / the
///     "Runway or exit" search field / ADD CROSS / CENTER MAP ON / ADD FLAG / LDG SHIFT / the
///     ARPT SEL airport-entry + ORIGIN/DEST/ALTN presets), and
///   • a structured BTV SNAPSHOT — the runway/exit PICK-LISTS, the armed runway/exit, and the
///     predicted DRY / WET / live STOP-BAR distances + runway LDA + exit distance.
///
/// BTV exit arming: on the real A380 you click the runway-end + exit LABELS on the moving map,
/// which a blind pilot can't reach. Here you pick a runway from a combo + press "Arm runway",
/// then an exit + "Arm exit" — the form drives the OANS btvUtils directly (oans_btv_arm_*),
/// exactly the methods the map-label clicks call. The armed selection auto-announces on change.
///
/// Layout (RMP-form pattern): a read-only multiline READOUT (arrow-navigable), the BTV combos +
/// buttons, a runway/exit SEARCH field, and a "OANS controls" LIST (Enter = activate). Hidden
/// (not disposed) on close so the scrape stays warm for an instant reopen.
/// </summary>
public sealed class FBWA380OansForm : Form
{
    private readonly CoherentNDClient _client;
    private readonly ScreenReaderAnnouncer _announcer;

    private Label _status = null!;
    private TextBox _readout = null!;
    private ComboBox _runwayCombo = null!;
    private ComboBox _exitCombo = null!;
    private Button _armRunwayBtn = null!;
    private Button _armExitBtn = null!;
    private Button _clearBtn = null!;
    private Label _searchLabel = null!;
    private TextBox _searchBox = null!;
    private Button _searchBtn = null!;
    private ListBox _controls = null!;

    private List<Elem> _elems = new();
    private Btv _btv = new();
    private int _searchIdx = -1;            // aidx of the active OANS text input (-1 = none)
    private string _searchField = "Search field";  // tab-dependent: "Airport" (ARPT SEL) / "Runway or exit" (MAP DATA)
    private bool _subscribed;
    private bool _firstPush = true;
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
        Size = new Size(640, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        KeyPreview = true;

        _status = new Label { Location = new Point(12, 10), Size = new Size(600, 20), Text = "OANS: connecting…", AccessibleName = "OANS status" };

        var readoutLabel = new Label { Text = "&Readout:", Location = new Point(12, 34), Size = new Size(200, 18) };
        _readout = new TextBox
        {
            Location = new Point(12, 54), Size = new Size(604, 210),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 11),
            AccessibleName = "OANS readout",
            Text = "Loading the airport map…"
        };

        // --- BTV exit selection ---
        var btvLabel = new Label { Text = "BTV exit selection:", Location = new Point(12, 272), Size = new Size(604, 18), Font = new Font(Font, FontStyle.Bold) };

        var rwyLabel = new Label { Text = "BTV r&unway:", Location = new Point(12, 296), Size = new Size(90, 22) };
        _runwayCombo = new ComboBox { Location = new Point(104, 293), Size = new Size(90, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "BTV runway" };
        _armRunwayBtn = new Button { Text = "Arm runway", Location = new Point(202, 292), Size = new Size(110, 26), AccessibleName = "Arm BTV runway" };
        _armRunwayBtn.Click += (_, _) => ArmRunway();

        var exitLabel = new Label { Text = "BTV e&xit:", Location = new Point(330, 296), Size = new Size(70, 22) };
        _exitCombo = new ComboBox { Location = new Point(402, 293), Size = new Size(90, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "BTV exit" };
        _armExitBtn = new Button { Text = "Arm exit", Location = new Point(500, 292), Size = new Size(110, 26), AccessibleName = "Arm BTV exit" };
        _armExitBtn.Click += (_, _) => ArmExit();

        _clearBtn = new Button { Text = "Clear BTV", Location = new Point(12, 324), Size = new Size(110, 26), AccessibleName = "Clear BTV selection" };
        _clearBtn.Click += (_, _) => { _client.EnqueueCommand("oans_btv_clear"); _announcer?.Announce("Clearing BTV selection"); };

        // --- Runway / exit search (the FBW "Runway or exit" InputField is a text search box) ---
        _searchLabel = new Label { Text = "&Search field:", Location = new Point(12, 358), Size = new Size(140, 22) };
        _searchBox = new TextBox { Location = new Point(156, 355), Size = new Size(110, 24), AccessibleName = "Search field" };
        _searchBtn = new Button { Text = "Search", Location = new Point(274, 354), Size = new Size(90, 26), AccessibleName = "Search" };
        _searchBtn.Click += (_, _) => DoSearch();

        // --- OANS controls (tabs / mode / actions) — Enter activates ---
        var listLabel = new Label { Text = "OANS &controls (Enter to activate):", Location = new Point(12, 386), Size = new Size(400, 18) };
        _controls = new ListBox { Location = new Point(12, 406), Size = new Size(604, 120), AccessibleName = "OANS controls" };
        _controls.KeyDown += OnControlsKeyDown;
        _controls.DoubleClick += (_, _) => ActivateSelectedControl();

        // No Refresh BUTTON — the window auto-updates live on every state change (silently, with
        // caret/selection preserved). F5 is the manual re-poll (handled in ProcessCmdKey).
        var close = new Button { Text = "Close", Location = new Point(12, 534), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();

        Controls.AddRange(new Control[]
        {
            _status, readoutLabel, _readout, btvLabel,
            rwyLabel, _runwayCombo, _armRunwayBtn, exitLabel, _exitCombo, _armExitBtn, _clearBtn,
            _searchLabel, _searchBox, _searchBtn,
            listLabel, _controls, close
        });

        int t = 1;
        _readout.TabIndex = t++;
        _runwayCombo.TabIndex = t++; _armRunwayBtn.TabIndex = t++;
        _exitCombo.TabIndex = t++; _armExitBtn.TabIndex = t++; _clearBtn.TabIndex = t++;
        _searchBox.TabIndex = t++; _searchBtn.TabIndex = t++;
        _controls.TabIndex = t++; close.TabIndex = t;
    }

    /// <summary>Show the window (lazy creation handled by the caller); like RMP's ShowForm.</summary>
    public void ShowForm()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        ActiveControl = _readout;
        _readout.Focus();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            if (!_subscribed) { _client.StateUpdated += OnState; _subscribed = true; }
            _firstPush = true;
            _client.EnqueueCommand("get_display_elements");   // open + force a fresh push
        }
        else if (_subscribed)
        {
            _client.StateUpdated -= OnState; _subscribed = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        if (keyData == Keys.F5) { Refresh2(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Refresh2()
    {
        _status.Text = "OANS: refreshing…";
        _client.EnqueueCommand("get_display_elements");
    }

    // The search box drives whichever text input the active tab exposes (the airport code on
    // ARPT SEL, the runway/exit search on MAP DATA), so label it from the live field.
    private void ApplySearchLabel()
    {
        string want = _searchIdx >= 0 ? _searchField : "Search field";
        if (_searchLabel.Text != "&" + want + ":") _searchLabel.Text = "&" + want + ":";
        if (_searchBox.AccessibleName != want) _searchBox.AccessibleName = want;
        _searchBox.Enabled = _searchIdx >= 0;
        _searchBtn.Enabled = _searchIdx >= 0;
    }

    // ---- state intake ------------------------------------------------------

    private void OnState(object? sender, EFBStateUpdateEventArgs e)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => OnState(sender, e))); } catch { } return; }

        if (e.Type == "fbw_efb_connected") { _status.Text = "OANS: connected"; return; }
        if (e.Type != "fbw_efb_elements") return;

        ParseElements(e.Data);
        ParseBtv(e.Data);
        RenderControls();
        RenderCombos();
        RenderReadout();
        ApplySearchLabel();
        AnnounceBtvChange();
        _firstPush = false;
    }

    private void ParseElements(Dictionary<string, string> d)
    {
        int count = d.TryGetValue("count", out var c) && int.TryParse(c, out var n) ? n : 0;
        var list = new List<Elem>(count);
        _searchIdx = -1;
        for (int i = 0; i < count; i++)
        {
            string p = $"items.{i}.";
            var el = new Elem
            {
                Aidx = d.TryGetValue(p + "aidx", out var a) && int.TryParse(a, out var ai) ? ai : 0,
                Text = d.TryGetValue(p + "text", out var tx) ? tx : "",
                Kind = d.TryGetValue(p + "kind", out var k) ? k : "",
                Type = d.TryGetValue(p + "type", out var ty) ? ty : "",
                Value = d.TryGetValue(p + "value", out var v) ? v : "",
                Clickable = d.TryGetValue(p + "clickable", out var cl) && cl == "true",
                Disabled = d.TryGetValue(p + "disabled", out var di) && di == "true"
            };
            list.Add(el);
            if (_searchIdx < 0 && el.Kind == "input")
            {
                _searchIdx = el.Aidx;
                // The input's label is tab-dependent ("Airport: …" on ARPT SEL, "Runway or exit: …"
                // on MAP DATA). Use the part before the colon as the search-box label.
                int colon = el.Text.IndexOf(':');
                _searchField = colon > 0 ? el.Text.Substring(0, colon).Trim() : "Search field";
            }
        }
        _elems = list;
    }

    private void ParseBtv(Dictionary<string, string> d)
    {
        var b = new Btv();
        if (d.TryGetValue("btv.ready", out var r)) b.Ready = r == "true";
        d.TryGetValue("btv.runway", out b.Runway!);
        d.TryGetValue("btv.exit", out b.Exit!);
        b.Lda = ParseInt(d, "btv.lda");
        b.ExitDist = ParseInt(d, "btv.exitDist");
        b.Dry = ParseInt(d, "btv.dry");
        b.Wet = ParseInt(d, "btv.wet");
        b.Stop = ParseInt(d, "btv.stop");
        b.Rot = ParseInt(d, "btv.rot");
        b.TurnMax = ParseInt(d, "btv.turnMax");
        b.TurnIdle = ParseInt(d, "btv.turnIdle");
        b.Metric = !d.TryGetValue("btv.metric", out var mt) || mt != "false";   // default metric
        if (d.TryGetValue("btv.computing", out var cm)) b.Computing = cm == "true";
        b.Runways = SplitList(d, "btv.runways");
        b.Exits = SplitList(d, "btv.exits");
        _btv = b;
    }

    private static int? ParseInt(Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var s) && int.TryParse(s, out var n) ? n : (int?)null;

    private static List<string> SplitList(Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var s) && s.Length > 0
            ? new List<string>(s.Split((char)0x1f))
            : new List<string>();

    // ---- rendering ---------------------------------------------------------

    private void RenderControls()
    {
        // Activatable controls = clickable, non-input elements (tabs / mode radios / action
        // buttons). The input search field is handled by the dedicated search box.
        var items = new List<OansControl>();
        foreach (var el in _elems)
        {
            if (!el.Clickable || el.Kind == "input") continue;
            string label = el.Text;
            if (el.Disabled) label += " (dimmed)";
            items.Add(new OansControl { Aidx = el.Aidx, Label = label });
        }

        // Preserve selection by label across re-renders so focus doesn't jump.
        string? sel = (_controls.SelectedItem as OansControl)?.Label;
        if (ControlsDiffer(items))
        {
            _controls.BeginUpdate();
            _controls.Items.Clear();
            foreach (var it in items) _controls.Items.Add(it);
            _controls.EndUpdate();
            if (sel != null)
            {
                for (int i = 0; i < _controls.Items.Count; i++)
                    if (((OansControl)_controls.Items[i]!).Label == sel) { _controls.SelectedIndex = i; break; }
            }
        }
    }

    private bool ControlsDiffer(List<OansControl> next)
    {
        if (next.Count != _controls.Items.Count) return true;
        for (int i = 0; i < next.Count; i++)
            if (((OansControl)_controls.Items[i]!).Label != next[i].Label) return true;
        return false;
    }

    private void RenderCombos()
    {
        SyncCombo(_runwayCombo, _btv.Runways, _btv.Runway);
        // Exits only become valid once a runway is armed; FBW reports an empty list until then.
        SyncCombo(_exitCombo, _btv.Exits, _btv.Exit);
        bool runwayArmed = !string.IsNullOrEmpty(_btv.Runway);
        _exitCombo.Enabled = runwayArmed && _exitCombo.Items.Count > 0;
        _armExitBtn.Enabled = runwayArmed && _exitCombo.Items.Count > 0;
        _armRunwayBtn.Enabled = _runwayCombo.Items.Count > 0;
        _clearBtn.Enabled = runwayArmed;
    }

    // Repopulate a combo only when its option set changed; keep the user's selection, and select
    // the armed value when one exists (so the combo always shows what's actually armed).
    private static void SyncCombo(ComboBox combo, List<string> options, string? armed)
    {
        // NEVER mutate a combo the user is currently in — a live refresh must not move the
        // selection (or rebuild the items) under the screen-reader cursor. It catches up the
        // moment focus leaves; the readout already shows the true armed state meanwhile.
        if (combo.Focused || combo.DroppedDown) return;

        bool same = combo.Items.Count == options.Count;
        if (same)
            for (int i = 0; i < options.Count; i++)
                if ((combo.Items[i] as string) != options[i]) { same = false; break; }

        string? keep = combo.SelectedItem as string;
        if (!same)
        {
            combo.BeginUpdate();
            combo.Items.Clear();
            foreach (var o in options) combo.Items.Add(o);
            combo.EndUpdate();
        }
        // Prefer the armed value; else restore the prior selection; else first item.
        string? want = !string.IsNullOrEmpty(armed) ? StripIcao(armed!, options) : keep;
        if (want != null)
        {
            int ix = combo.Items.IndexOf(want);
            if (ix >= 0 && combo.SelectedIndex != ix) combo.SelectedIndex = ix;
        }
        else if (combo.Items.Count > 0 && combo.SelectedIndex < 0) combo.SelectedIndex = 0;
    }

    // btvRunway reads as "VCBI22"; the pick-list options are bare "22" — match the suffix.
    private static string? StripIcao(string armed, List<string> options)
    {
        foreach (var o in options) if (armed.EndsWith(o, StringComparison.Ordinal)) return o;
        return options.Contains(armed) ? armed : null;
    }

    private void RenderReadout()
    {
        var sb = new StringBuilder();
        sb.AppendLine("OANS Airport Map / BTV");
        // The active tab is known (it carries "(active tab)"); surface it once as a header and
        // drop the three tab lines from the readout body (they stay in the controls list to
        // switch between). So the readout says "Current tab: MAP DATA" instead of listing all.
        string curTab = CurrentTab();
        if (curTab.Length > 0) sb.AppendLine($"Current tab: {curTab}");
        sb.AppendLine();
        foreach (var el in _elems)
        {
            if (el.Kind == "tab") continue;          // shown by the "Current tab" header above
            if (string.IsNullOrWhiteSpace(el.Text)) continue;
            string role = el.Kind == "dropdown" ? " (combo box)" : "";
            string line = el.Text + role;
            if (el.Disabled) line += " (dimmed)";
            sb.AppendLine(line);
        }

        sb.AppendLine();
        sb.AppendLine("--- BTV exit selection ---");
        sb.AppendLine(BtvReadoutBlock());

        DisplayText.SetPreserveCaret(_readout, sb.ToString().TrimEnd());
        _status.Text = _btv.Ready ? "OANS: live" : "OANS: not available (no airport in range)";
    }

    // The active OANS tab (MAP DATA / ARPT SEL / STATUS), with the "(active tab)" marker stripped.
    private string CurrentTab()
    {
        foreach (var el in _elems)
            if (el.Kind == "tab" && el.Text.IndexOf("(active tab)", StringComparison.OrdinalIgnoreCase) >= 0)
                return el.Text.Replace("(active tab)", "", StringComparison.OrdinalIgnoreCase).Trim();
        return "";
    }

    // Format a metre distance per the OANS unit setting (metres, or feet when imperial — the same
    // A32NX_EFB_USING_METRIC_UNIT toggle MSFSBA follows for weights; matches what the ND shows).
    private string Dist(int metres)
        => _btv.Metric ? $"{metres} m" : $"{(int)Math.Round(metres * 3.280839895)} ft";

    private string BtvReadoutBlock()
    {
        if (!_btv.Ready) return "BTV: not available — fly within OANS range of an airport with a Navigraph AMDB map.";

        var sb = new StringBuilder();
        if (string.IsNullOrEmpty(_btv.Runway))
            sb.AppendLine("Runway: none selected");
        else
            sb.AppendLine($"Runway: {_btv.Runway}" + (_btv.Lda != null ? $" (landing distance available {Dist(_btv.Lda.Value)})" : ""));

        if (string.IsNullOrEmpty(_btv.Exit))
            sb.AppendLine("Exit: none selected");
        else
            sb.AppendLine($"Exit: {_btv.Exit}" + (_btv.ExitDist != null ? $" ({Dist(_btv.ExitDist.Value)} from threshold)" : ""));

        if (_btv.Computing && (_btv.Dry != null || _btv.Wet != null))
        {
            if (_btv.Dry != null) sb.AppendLine($"Predicted stop, dry runway: {Dist(_btv.Dry.Value)}");
            if (_btv.Wet != null) sb.AppendLine($"Predicted stop, wet runway: {Dist(_btv.Wet.Value)}");
            if (_btv.Stop != null) sb.AppendLine($"Live stop-bar distance: {Dist(_btv.Stop.Value)}");
        }
        else
        {
            sb.AppendLine("Predicted stopping distances: not yet computed");
        }

        // ROT (runway occupancy time) + turnaround times — the real OANS only shows these once an
        // EXIT is selected, so gate on that to avoid surfacing stale/meaningless values.
        if (!string.IsNullOrEmpty(_btv.Exit))
        {
            if (_btv.Rot != null) sb.AppendLine($"Runway occupancy time: {_btv.Rot} seconds");
            if (_btv.TurnMax != null && _btv.TurnIdle != null)
                sb.AppendLine($"Turnaround: {_btv.TurnMax} minutes max reverse, {_btv.TurnIdle} minutes idle reverse");
        }
        return sb.ToString().TrimEnd();
    }

    // ---- announce-on-change ------------------------------------------------

    private void AnnounceBtvChange()
    {
        // Dedup on the SELECTION identity only (runway + exit) — NOT the distance — so a live
        // distance tick can never re-fire the announce; the spoken text still includes the metres.
        string key = !_btv.Ready ? "" :
            string.IsNullOrEmpty(_btv.Runway) ? "none" : $"{_btv.Runway}|{_btv.Exit}";

        if (_firstPush) { _lastBtvSpoken = key; return; }   // baseline silently on first push
        if (key == _lastBtvSpoken) return;
        _lastBtvSpoken = key;
        if (key.Length == 0) return;                        // OANS not available — stay silent
        if (key == "none") { _announcer?.Announce("BTV cleared"); return; }

        string spoken = $"runway {_btv.Runway}" +
                        (string.IsNullOrEmpty(_btv.Exit)
                            ? ", no exit"
                            : $", exit {_btv.Exit}" + (_btv.ExitDist != null ? $", {Dist(_btv.ExitDist.Value)}" : ""));
        _announcer?.Announce("BTV armed, " + spoken);
    }

    // ---- actions -----------------------------------------------------------

    private void ArmRunway()
    {
        if (_runwayCombo.SelectedItem is not string rwy) { _announcer?.Announce("No runway selected"); return; }
        _client.EnqueueCommand("oans_btv_arm_runway", new Dictionary<string, string> { ["value"] = rwy });
        _announcer?.Announce($"Arming runway {rwy}");
    }

    private void ArmExit()
    {
        if (string.IsNullOrEmpty(_btv.Runway)) { _announcer?.Announce("Select a BTV runway first"); return; }
        if (_exitCombo.SelectedItem is not string ex) { _announcer?.Announce("No exit selected"); return; }
        _client.EnqueueCommand("oans_btv_arm_exit", new Dictionary<string, string> { ["value"] = ex });
        _announcer?.Announce($"Arming exit {ex}");
    }

    private void DoSearch()
    {
        if (_searchIdx < 0) { _announcer?.Announce("No search field on this page"); return; }
        string q = _searchBox.Text.Trim();
        if (q.Length == 0) return;
        _client.EnqueueCommand("set_element_value", new Dictionary<string, string> { ["index"] = _searchIdx.ToString(), ["value"] = q });
        _announcer?.Announce($"Searching {q}");
    }

    private void OnControlsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { ActivateSelectedControl(); e.Handled = true; e.SuppressKeyPress = true; }
    }

    private void ActivateSelectedControl()
    {
        if (_controls.SelectedItem is not OansControl c) return;
        _client.EnqueueCommand("click_display_element", new Dictionary<string, string> { ["index"] = c.Aidx.ToString() });
        // No announcement — the screen reader already speaks the activation; the next push refreshes state.
    }

    // ---- data carriers -----------------------------------------------------

    private sealed class Elem
    {
        public int Aidx;
        public string Text = "";
        public string Kind = "";
        public string Type = "";
        public string Value = "";
        public bool Clickable;
        public bool Disabled;
    }

    private sealed class OansControl
    {
        public int Aidx;
        public string Label = "";
        public override string ToString() => Label;
    }

    private sealed class Btv
    {
        public bool Ready;
        public List<string> Runways = new();
        public List<string> Exits = new();
        public string? Runway;
        public string? Exit;
        public int? Lda;
        public int? ExitDist;
        public int? Dry;
        public int? Wet;
        public int? Stop;
        public int? Rot;
        public int? TurnMax;
        public int? TurnIdle;
        public bool Metric = true;
        public bool Computing;
    }
}
