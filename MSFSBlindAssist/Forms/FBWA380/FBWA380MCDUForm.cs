using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible MCDU/MFD window for the FlyByWire A380X (development build).
///
/// The A380's FMS is the MFD — a cursor-driven page, not a classic LSK MCDU.
/// We present it as a flat, screen-reader-friendly LIST: one interactive
/// element per line, numbered, e.g. "11: FLT NBR: 320". The page title is
/// NOT shown in the list; it is announced once each time the page changes
/// (so pressing INIT just says "INIT").
///
/// Tab order (top to bottom):
///   1. MCDU selector   — Captain / First Officer.
///   2. Display list    — one interactive element per line, numbered.
///                        • Enter on a line: if the scratchpad has text,
///                          send it to that field; otherwise click it.
///                        • Backspace on a line: Airbus CLR — clear the
///                          scratchpad first, else clear that field.
///                        • Delete on a line: clear that field on the MFD.
///   3. Scratchpad box  — staging buffer. Type, then go to the display and
///                        press Enter on the target field. Enter here sends
///                        to the MFD's current cursor field; Escape clears it.
///   4. Page buttons    — INIT, F-PLN, PERF, RAD NAV, SEC FPL, ATC COM, DIR,
///                        UP, DOWN, CLR (Alt+letter mnemonics). Each clicks the
///                        matching on-screen MFD control, KCCU key as fallback.
///
/// Page navigation: Ctrl+Up = previous page, Ctrl+Down = next page.
///
/// Everything auto-refreshes: the agent polls the MFD DOM (~350 ms) and the
/// client pushes new state only when it changes. Title and footer-message
/// changes are announced without the user having to refocus.
///
/// State pushes consumed: fbwa380_mcdu_screen (title + footer),
/// fbwa380_mcdu_elements (the numbered list), fbwa380_mcdu_connected.
/// Commands sent: navigate, type_key, key_prev_page/key_next_page,
/// select_mcdu, get_mcdu_elements, click_mcdu_element, send_scratchpad,
/// send_to_field.
/// </summary>
public class FBWA380MCDUForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeScreen = "fbwa380_mcdu_screen";
    private const string StateTypeElements = "fbwa380_mcdu_elements";
    private const string StateTypeConnected = "fbwa380_mcdu_connected";

    private readonly IMcduBridge _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly FlyByWireA380Definition? _aircraftDefinition;

    private Label _statusLabel = null!;
    private ComboBox _mcduSelector = null!;
    private ComboBox _pageSelector = null!;
    private ListBox _display = null!;

    // Every MFD page, grouped by top-level tab, with its page-selector index
    // (CAPT/FO_MFD_pageSelector{Prefix}_{Index}) and a KCCU-key fallback. Lets the
    // user jump to ANY of the 20 pages from one combo box (keyboard accessible),
    // in addition to the quick page buttons. (prefix,index) verified live.
    // Uri (when non-empty) navigates via the MFD UIService (navigateTo) — the
    // robust cross-system path used for the ATC COM / D-ATIS pages, which the
    // page-selector-id click can't reach from the FMS (their header isn't
    // mounted). FMS pages keep the verified (prefix,index) page-selector click.
    private sealed record PageNav(string Label, string Prefix, int Index, string Key, string Uri = "");
    private static readonly PageNav[] AllPages =
    {
        new("ACTIVE: F-PLN",      "Active",   0, "FPLN"),
        new("ACTIVE: PERF",       "Active",   1, "PERF"),
        new("ACTIVE: FUEL & LOAD","Active",   2, ""),
        new("ACTIVE: WIND",       "Active",   3, ""),
        new("ACTIVE: INIT",       "Active",   4, "INIT"),
        new("POSITION: MONITOR",  "Position", 0, ""),
        new("POSITION: REPORT",   "Position", 1, ""),
        new("POSITION: NAVAIDS",  "Position", 2, "NAVAID"),
        new("POSITION: IRS",      "Position", 3, ""),
        new("POSITION: GNSS",     "Position", 4, ""),
        new("POSITION: TIME",     "Position", 5, ""),
        // Secondary flight plans. The SEC INDEX page hosts the SEC 1/2/3 subtabs
        // plus F-PLN/PERF/INIT/WIND/FUEL buttons; the per-plan F-PLN URIs jump
        // straight to each secondary route for reading (UIService, verified live).
        new("SEC INDEX",          "", -1, "SECINDEX", "fms/sec/index"),
        new("SEC 1: F-PLN",       "", -1, "", "fms/sec1/f-pln"),
        new("SEC 2: F-PLN",       "", -1, "", "fms/sec2/f-pln"),
        new("SEC 3: F-PLN",       "", -1, "", "fms/sec3/f-pln"),
        new("DATA: STATUS",       "Data",     0, ""),
        new("DATA: WAYPOINT",     "Data",     1, ""),
        new("DATA: NAVAID",       "Data",     2, ""),
        new("DATA: ROUTE",        "Data",     3, ""),
        new("DATA: AIRPORT",      "Data",     4, ""),
        new("DATA: PRINTER",      "Data",     5, ""),
        // ATC COM (CPDLC datalink) + D-ATIS — UIService URIs (cross-system).
        new("ATC COM: CONNECT",       "", -1, "", "atccom/connect"),
        new("ATC COM: REQUEST",       "", -1, "", "atccom/request"),
        new("ATC COM: REPORT & NOTIFY","", -1, "", "atccom/report-modify/position"),
        new("ATC COM: MSG RECORD",    "", -1, "", "atccom/msg-record"),
        new("ATC COM: D-ATIS",        "", -1, "", "atccom/d-atis/list"),
        new("ATC COM: EMERGENCY",     "", -1, "", "atccom/emer"),
        // SURV (surveillance) — XPDR/TCAS/WXR/TAWS controls + status & switching.
        // Radios/inputs/buttons read and actuate via the standard click path
        // (verified live: selecting XPDR AUTO enabled the TCAS sub-radios).
        new("SURV: CONTROLS",         "", -1, "", "surv/controls"),
        new("SURV: STATUS & SWITCHING","", -1, "", "surv/status-switching"),
    };
    private TextBox _scratchpad = null!;

    // Page nav buttons mirror the physical KCCU keyboard.
    private Button _btnInit = null!;
    private Button _btnFPln = null!;
    private Button _btnPerf = null!;
    private Button _btnRadNav = null!;
    private Button _btnSecFPln = null!;
    private Button _btnAtc = null!;
    private Button _btnDir = null!;
    private Button _btnUp = null!;
    private Button _btnDown = null!;
    private Button _btnClr = null!;
    private Button _btnRefresh = null!;
    private Button _btnUnits = null!;

    private List<McduElement> _elements = new();
    // Elements actually rendered as display lines (empty-text lines dropped),
    // kept 1:1 with _display.Items so the selected row maps to the right element.
    private List<McduElement> _displayedElements = new();

    private string _previousTitle = "";
    private string _previousScratchpad = "";
    private bool _bridgeConnected;
    private int _mcduIndex = 1;
    private bool _initialPushReceived;
    private bool _resetSelection;

    private IntPtr _previousWindow = IntPtr.Zero;
    private System.Windows.Forms.Timer _statusTimer = null!;

    // Optional direct eval into the MFD Coherent context (CoherentDebuggerClient.
    // EvalForResultAsync), used for the metric/imperial units toggle which writes
    // the EFB persistent store (SetStoredData) — not expressible as a queued
    // bridge command. Null when the bridge isn't the debugger client.
    private readonly Func<string, Task<string>>? _evalAsync;

    public FBWA380MCDUForm(IMcduBridge bridgeServer, ScreenReaderAnnouncer announcer,
        FlyByWireA380Definition? aircraftDefinition = null,
        Func<string, Task<string>>? evalAsync = null)
    {
        _bridgeServer = bridgeServer;
        _announcer = announcer;
        _aircraftDefinition = aircraftDefinition;
        _evalAsync = evalAsync;

        InitializeComponent();
        SetupEventHandlers();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _statusTimer.Tick += (_, _) => UpdateStatusLabel();
        _statusTimer.Start();

        _bridgeServer.StateUpdated += OnBridgeStateUpdated;
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        if (!Visible) Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        // Ask the bridge for a fresh push so the display populates the
        // moment the user opens the form, rather than waiting for the
        // next 350-ms poll cycle.
        _bridgeServer.EnqueueCommand("get_mcdu_elements");
        _display.Focus();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "A380X MCDU (Captain)";
        ClientSize = new Size(640, 700);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;

        _mcduSelector = new ComboBox
        {
            Location = new Point(420, y),
            Size = new Size(210, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Active MCDU side",
            AccessibleDescription = "Choose which A380 MFD's FMS pages to display: Captain or First Officer."
        };
        _mcduSelector.Items.AddRange(new object[] { "Captain", "First Officer" });
        _mcduSelector.SelectedIndex = 0;
        Controls.Add(_mcduSelector);

        // "Go to page" — direct keyboard access to ALL 20 MFD pages (the quick
        // buttons below only cover the common ones).
        _pageSelector = new ComboBox
        {
            Location = new Point(420, y + 30),
            Size = new Size(210, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Go to MFD page",
            AccessibleDescription = "Jump to any of the 20 MFD pages. Choose a page to navigate to it."
        };
        foreach (var p in AllPages) _pageSelector.Items.Add(p.Label);
        Controls.Add(_pageSelector);

        _statusLabel = new Label
        {
            Text = "MCDU bridge: connecting…",
            Location = new Point(10, y),
            Size = new Size(400, 76),  // multi-line — full stage diagnostic
            AutoSize = false,
            AccessibleName = "MCDU bridge status"
        };
        Controls.Add(_statusLabel);
        y += 82;

        _display = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(620, 380),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU display",
            AccessibleDescription = "One field per line. Enter sends the scratchpad here, or activates the field. Backspace is clear.",
            IntegralHeight = false
        };
        Controls.Add(_display);
        y += 390;

        _scratchpad = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(620, 25),
            AccessibleName = "Scratchpad input",
            AccessibleDescription = "Type here, then go to the display and press Enter on the target field. Enter here sends to the MFD's current field. Escape clears."
        };
        Controls.Add(_scratchpad);
        y += 35;

        // text  = display label, "&" marks the Alt+letter mnemonic.
        // accName = clean name the screen reader speaks (no "&", no extra hint).
        int btnW = 90, btnH = 30, gap = 4, col = 10;
        Button MakeBtn(string text, string accName)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(col, y),
                Size = new Size(btnW, btnH),
                AccessibleName = accName
            };
            Controls.Add(b);
            col += btnW + gap;
            return b;
        }

        _btnInit    = MakeBtn("&INIT",    "INIT");
        _btnFPln    = MakeBtn("&F-PLN",   "F-PLN");
        _btnPerf    = MakeBtn("&PERF",    "PERF");
        _btnRadNav  = MakeBtn("&RAD NAV", "RAD NAV");
        _btnSecFPln = MakeBtn("&SEC FPL", "SEC FPL");
        _btnAtc     = MakeBtn("&ATC COM", "ATC COM");
        _btnDir     = MakeBtn("&DIR",     "DIR");
        y += btnH + gap; col = 10;

        _btnUp      = MakeBtn("&UP",      "UP");
        _btnDown    = MakeBtn("DO&WN",    "DOWN");
        _btnClr     = MakeBtn("&CLR",     "CLR");
        _btnRefresh = MakeBtn("Refres&h", "Refresh");
        // Toggle the FMS/EFB display units (metric / imperial). Disabled when this
        // window's bridge can't run a direct eval (the units write goes to the EFB
        // persistent store, not a queued command).
        _btnUnits   = MakeBtn("&Units", "Toggle units, metric or imperial");
        _btnUnits.Width = 110;
        _btnUnits.Enabled = _evalAsync != null;

        ResumeLayout(true);
        // Open with the DISPLAY focused, not the MCDU-side combo (the first-added
        // control). Otherwise the screen reader announces "Active MCDU side" on
        // open even though we move focus on Load — set it as the active control up
        // front so the display is the only thing announced.
        ActiveControl = _display;
    }

    private void SetupEventHandlers()
    {
        // Focus the display after the form is actually shown (BeginInvoke defers
        // past the screen reader's initial focus announcement so it lands on the
        // display, not the side combo).
        Shown += (_, _) =>
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(new Action(() => { if (!IsDisposed && _display is { IsDisposed: false }) _display.Focus(); }));
        };

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };

        // Each page button navigates by clicking the MFD's page-selector menu
        // item by its STABLE element id (CAPT/FO_MFD_pageSelector<Tab>_<idx>),
        // with the KCCU key as a cross-system fallback. The stable-id click is
        // the reliable path — FlyByWire's own issue #9348 says the KCCU H-events
        // are "practically unusable for external tools". The (prefix,index) pairs
        // come from FmsHeader.tsx; DIR / ATC COM have no FMS dropdown item, so
        // they navigate purely by KCCU key (empty prefix).
        _btnInit.Click    += (_, _) => SendNavigateById("Active",   4, "INIT");     // ACTIVE ▸ INIT
        _btnFPln.Click    += (_, _) => SendNavigateById("Active",   0, "FPLN");     // ACTIVE ▸ F-PLN
        _btnPerf.Click    += (_, _) => SendNavigateById("Active",   1, "PERF");     // ACTIVE ▸ PERF
        _btnRadNav.Click  += (_, _) => SendNavigateById("Position", 2, "NAVAID");   // POSITION ▸ NAVAIDS
        _btnSecFPln.Click += (_, _) => SendNavigateUri("fms/sec/index");          // SEC INDEX (secondary flight plans)
        _btnAtc.Click     += (_, _) => SendNavigateUri("atccom/connect");          // ATC COM via UIService (reliable)
        _btnDir.Click     += (_, _) => SendNavigateById("",        -1, "DIR");      // KCCU only
        _btnUp.Click      += (_, _) => SendCommand("key_prev_page");
        _btnDown.Click    += (_, _) => SendCommand("key_next_page");
        _btnClr.Click     += (_, _) => PerformClear();
        _btnRefresh.Click += (_, _) => _bridgeServer.EnqueueCommand("get_mcdu_elements");
        _btnUnits.Click   += (_, _) => ToggleUnits();

        _mcduSelector.SelectedIndexChanged += (_, _) =>
        {
            _mcduIndex = _mcduSelector.SelectedIndex + 1;
            Text = $"A380X MCDU ({_mcduSelector.SelectedItem})";
            _bridgeServer.EnqueueCommand("select_mcdu",
                new Dictionary<string, string> { ["mcdu"] = _mcduIndex.ToString() });
            _bridgeServer.EnqueueCommand("get_mcdu_elements");
        };

        _pageSelector.SelectedIndexChanged += (_, _) =>
        {
            int i = _pageSelector.SelectedIndex;
            if (i < 0 || i >= AllPages.Length) return;
            var p = AllPages[i];
            if (!string.IsNullOrEmpty(p.Uri)) SendNavigateUri(p.Uri);
            else SendNavigateById(p.Prefix, p.Index, p.Key);
            // No explicit announce: the screen reader already speaks the chosen
            // combo item, and the MFD page title announces when the new page loads.
            // Pull the new page's elements shortly after the route switches.
            var t = new System.Windows.Forms.Timer { Interval = 450 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); _bridgeServer.EnqueueCommand("get_mcdu_elements"); };
            t.Start();
        };

        _scratchpad.KeyDown += ScratchpadKeyDown;
        _display.KeyDown    += DisplayKeyDown;
        KeyDown             += FormKeyDown;
    }

    private void OnBridgeStateUpdated(object? sender, EFBStateUpdateEventArgs e)
    {
        if (e.Type == StateTypeConnected)
        {
            _bridgeConnected = true;
            if (IsHandleCreated) BeginInvoke(UpdateStatusLabel);
            return;
        }
        if (e.Type == StateTypeScreen)   { HandleScreenPush(e); return; }
        if (e.Type == StateTypeElements) { HandleElementsPush(e); return; }
    }

    private void HandleScreenPush(EFBStateUpdateEventArgs e)
    {
        _bridgeConnected = true;
        _initialPushReceived = true;
        // The display list is built from the elements push; from the screen
        // push we only need the page title (announced once on change) and the
        // footer/scratchpad message.
        string title = e.Data.TryGetValue("title", out var t) ? (t ?? "").Trim() : "";
        string footer = e.Data.TryGetValue("scratchpad", out var s) ? (s ?? "").Trim() : "";
        if (IsHandleCreated) BeginInvoke(() => ApplyScreenMeta(title, footer));
    }

    private void ApplyScreenMeta(string title, string footer)
    {
        if (!string.IsNullOrWhiteSpace(title) && title != _previousTitle)
        {
            _previousTitle = title;
            _resetSelection = true;   // jump back to the top of the new page
            _announcer.Announce(title);
        }
        if (footer != _previousScratchpad)
        {
            _previousScratchpad = footer;
            if (!string.IsNullOrWhiteSpace(footer)) _announcer.Announce(footer);
        }
        UpdateStatusLabel();
    }

    private void HandleElementsPush(EFBStateUpdateEventArgs e)
    {
        _bridgeConnected = true;
        // byPos is keyed on the DISPLAY position N (items.N.*) so the list keeps
        // the agent's reading order. el.Index is the agent's stable handle (from
        // items.N.index): >0 = interactive/actionable, 0 = static text line.
        var byPos = new SortedDictionary<int, McduElement>();
        foreach (var kv in e.Data)
        {
            if (!kv.Key.StartsWith("items.")) continue;
            var parts = kv.Key.Split('.');
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[1], out int pos)) continue;
            if (!byPos.TryGetValue(pos, out var el))
            {
                el = new McduElement();
                byPos[pos] = el;
            }
            switch (parts[2])
            {
                case "index":    if (int.TryParse(kv.Value, out int ix)) el.Index = ix; break;
                case "text":     el.Text = kv.Value; break;
                case "kind":     el.Kind = kv.Value; break;
                case "value":    el.Value = kv.Value; break;
                case "disabled": el.Disabled = kv.Value == "true"; break;
                case "expandstate": el.ExpandState = kv.Value; break;
            }
        }
        _elements = byPos.Values.ToList();
        if (IsHandleCreated) BeginInvoke(RefreshDisplay);
    }

    private void UpdateStatusLabel()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string desired;
        if (reallyConnected && _initialPushReceived)
            desired = $"MCDU connected (MCDU {_mcduIndex})";
        else if (reallyConnected)
            desired = "MCDU connected — reading MFD…";
        else
            desired = "MCDU not connected. Make sure MSFS is running, the A380X is loaded, and the MFD is powered up.";
        if (_statusLabel.Text != desired) _statusLabel.Text = desired;
    }

    private void RefreshDisplay()
    {
        // One thing per line. Interactive elements carry a trailing role word
        // ("<label>, button" / ", edit" / …) so the user can tell what they can
        // act on; static text lines carry no role word. Completely empty lines are
        // dropped. _displayedElements is kept 1:1 with the rendered lines so the
        // selected row maps to the right element.
        _displayedElements = new List<McduElement>(_elements.Count);
        var lines = new List<string>(_elements.Count);
        foreach (var el in _elements)
        {
            if (string.IsNullOrWhiteSpace(el.Text)) continue;
            _displayedElements.Add(el);
            if (el.Index > 0)
            {
                // Actionable element. Append a role word so the screen reader tells
                // the user what it is (a ListBox renders plain strings, so it
                // doesn't get the automatic role a native control would). The role
                // word ("button"/"edit"/…) is what marks a line as actionable — the
                // old leading "N:" index was an internal handle and just added noise
                // when read aloud, so it is no longer shown (el.Index is still used
                // internally to click the right element).
                string role = AnnounceRoles ? RoleWord(el.Kind) : "";
                // Combo boxes announce whether their option list is open, e.g.
                // "RWY: 30R, combo box, collapsed" → press Enter → "… expanded".
                string state = el.ExpandState.Length > 0 ? $", {el.ExpandState}" : "";
                // Tell the user a control is unavailable, so a no-op activation
                // reads as "dimmed" rather than "broken". (Matches the flyPad
                // browser view, which also says "dimmed".)
                string suffix = el.Disabled
                    ? (role.Length > 0 ? $", {role}{state}, dimmed" : $"{state}, dimmed")
                    : (role.Length > 0 ? $", {role}{state}" : state);
                lines.Add($"{el.Text}{suffix}");
            }
            else
            {
                lines.Add(el.Text);
            }
        }

        int saved = _display.SelectedIndex;
        _display.BeginUpdate();
        while (_display.Items.Count > lines.Count) _display.Items.RemoveAt(_display.Items.Count - 1);
        while (_display.Items.Count < lines.Count) _display.Items.Add("");
        for (int i = 0; i < lines.Count; i++)
            if (_display.Items[i]?.ToString() != lines[i])
                _display.Items[i] = lines[i];
        _display.EndUpdate();

        if (_resetSelection && _display.Items.Count > 0)
        {
            _display.SelectedIndex = 0;
            _resetSelection = false;
        }
        else if (saved >= 0 && saved < _display.Items.Count)
        {
            _display.SelectedIndex = saved;
        }

        UpdateStatusLabel();
    }

    // Set false to drop the trailing role word ("button"/"edit"/…) from
    // interactive lines if it ever feels too chatty.
    private const bool AnnounceRoles = true;

    /// <summary>
    /// Screen-reader role word for an element kind. Mirrors what a native
    /// control would announce, so the user knows how to act on the line:
    /// a button/tab/menu is activated with Enter; an edit field takes text
    /// (typed into the scratchpad, then Enter).
    /// </summary>
    private static string RoleWord(string? kind) => kind switch
    {
        "button" => "button",
        "icon"   => "button",
        "input"  => "edit",
        "dropdown" => "combo box",   // expands a list of choices when activated
        "menu"   => "option",
        "tab"    => "tab",
        "radio"  => "radio button",
        "subtab" => "tab",
        "surv"   => "button",
        "survstatus" => "button",
        "adsc"   => "button",
        // F-PLN rows the MFD makes clickable: a waypoint (Enter → lateral-revision
        // menu: DIR TO, INSERT, DELETE, HOLD, AIRWAYS, OVERFLY) or a discontinuity
        // (Enter → menu → DELETE * to clear it). "button" reads cleaner than
        // "waypoint" — the button role already implies it's actionable.
        "fplnwpt"  => "button",
        "fplndisc" => "button",
        _ => ""
    };

    /// <summary>
    /// The element backing the currently selected display row, or null.
    /// The ListBox rows are built 1:1 from <see cref="_displayedElements"/>
    /// (the filtered, rendered list) — NOT <see cref="_elements"/>, which may
    /// contain entries that were filtered out. Mapping the selected row onto
    /// _elements drifts by the number of skipped entries and lands on the wrong
    /// element (often a static idx-0 line), which is why "buttons didn't click".
    /// </summary>
    private McduElement? SelectedElement()
    {
        int i = _display.SelectedIndex;
        return (i >= 0 && i < _displayedElements.Count) ? _displayedElements[i] : null;
    }

    /// <summary>
    /// Element index (MFD handle) of the currently selected display line, or 0.
    /// </summary>
    private int SelectedElementIndex() => SelectedElement()?.Index ?? 0;

    // ---- input handlers ---------------------------------------------------

    private void SendCommand(string command) => _bridgeServer.EnqueueCommand(command);
    private void SendTypeKey(string key) =>
        _bridgeServer.EnqueueCommand("type_key", new Dictionary<string, string> { ["key"] = key });
    private void SendNavigate(string label, string key) =>
        _bridgeServer.EnqueueCommand("navigate",
            new Dictionary<string, string> { ["label"] = label, ["key"] = key });

    /// <summary>
    /// Navigate by clicking the MFD page-selector menu item with the stable id
    /// {CAPT|FO}_MFD_pageSelector{prefix}_{index}, falling back to the KCCU key
    /// (cross-system) when that id isn't present. prefix="" / index=-1 means
    /// "KCCU key only" (DIR, ATC COM — no FMS dropdown item).
    /// </summary>
    private void SendNavigateById(string prefix, int index, string key) =>
        _bridgeServer.EnqueueCommand("navigate_by_id", new Dictionary<string, string>
        {
            ["prefix"] = prefix,
            ["index"] = index.ToString(),
            ["key"] = key
        });

    /// <summary>
    /// Navigate to a page by its MFD UIService URI (e.g. "atccom/msg-record").
    /// The reliable cross-system path — works from any current page, used for the
    /// ATC COM (CPDLC datalink) and D-ATIS pages that the page-selector-id click
    /// can't reach while the FMS header is mounted.
    /// </summary>
    private void SendNavigateUri(string uri) =>
        _bridgeServer.EnqueueCommand("navigate_uri",
            new Dictionary<string, string> { ["uri"] = uri });

    /// <summary>
    /// Toggle the FMS / EFB display units between metric and imperial by flipping
    /// the EFB persistent setting A380X_CONFIG_USING_METRIC_UNIT (the same key the
    /// flyPad Aircraft Options page writes). Done with a single direct eval in the
    /// MFD Coherent context: read the stored value, flip it, write it back, return
    /// the new state. NXDataStore subscribers (the FMS/EFB) pick the change up live.
    /// </summary>
    private async void ToggleUnits()
    {
        if (_evalAsync == null) { _announcer?.AnnounceImmediate("Units toggle not available."); return; }
        try
        {
            const string js =
                "(function(){try{var k='A380X_CONFIG_USING_METRIC_UNIT';" +
                "var c=(typeof GetStoredData!=='undefined')?GetStoredData(k,'0'):'0';" +
                "var nv=((''+c)==='1')?'0':'1';SetStoredData(k,nv);return nv;}catch(e){return 'ERR '+e;}})()";
            string res = (await _evalAsync(js))?.Trim().Trim('"') ?? "";
            if (res == "1") _announcer?.AnnounceImmediate("Metric units");
            else if (res == "0") _announcer?.AnnounceImmediate("Imperial units");
            else _announcer?.AnnounceImmediate("Could not change units.");
            // Re-fetch so the page re-reads in the new units.
            _bridgeServer.EnqueueCommand("get_mcdu_elements");
        }
        catch { _announcer?.AnnounceImmediate("Could not change units."); }
    }

    /// <summary>
    /// Airbus CLR semantics. A real pilot pressing CLR first clears the
    /// scratchpad; once it's empty, CLR acts on the field. Mirror that:
    ///   • scratchpad has text  → clear it, announce "Scratchpad cleared"
    ///   • else field selected  → clear that field on the MFD
    ///   • else                 → fire KCCU CLR (clears the MFD's own scratchpad)
    /// </summary>
    private void PerformClear()
    {
        if (!string.IsNullOrEmpty(_scratchpad.Text))
        {
            _scratchpad.Text = "";
            _announcer.Announce("Scratchpad cleared");
            return;
        }
        int fieldIdx = SelectedElementIndex();
        if (fieldIdx > 0)
        {
            _bridgeServer.EnqueueCommand("send_to_field", new Dictionary<string, string>
            {
                ["index"] = fieldIdx.ToString(),
                ["text"]  = ""
            });
            _announcer.Announce("Cleared");
        }
        else
        {
            SendTypeKey("BACKSPACE");
        }
    }

    private void ScratchpadKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = _scratchpad.Text;
            if (!string.IsNullOrEmpty(text))
            {
                // Fires the chars + ENT into whichever field the MFD cursor is
                // currently on. (The primary path is Enter on a display line.)
                _bridgeServer.EnqueueCommand("send_scratchpad",
                    new Dictionary<string, string> { ["text"] = text });
                _scratchpad.Text = "";
                _announcer.Announce(text);   // speak back what was sent
            }
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Escape)
        {
            if (!string.IsNullOrEmpty(_scratchpad.Text))
            {
                _scratchpad.Text = "";
                _announcer.Announce("Scratchpad cleared");
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private void DisplayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            int fieldIdx = SelectedElementIndex();
            if (fieldIdx > 0)
            {
                string text = _scratchpad.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    // Commit the scratchpad straight to the selected field
                    // (click + clear existing + type + ENT).
                    _bridgeServer.EnqueueCommand("send_to_field", new Dictionary<string, string>
                    {
                        ["index"] = fieldIdx.ToString(),
                        ["text"]  = text
                    });
                    _scratchpad.Text = "";
                    _announcer.Announce(text);
                    ScheduleRefresh();
                }
                else
                {
                    // No pending value — just activate the element (buttons,
                    // toggles, page-selector tabs that need no entry). If it's
                    // disabled, say so instead of silently doing nothing (the MFD
                    // ignores clicks on disabled controls — that read as "broken").
                    if (SelectedElement()?.Disabled == true)
                    {
                        _announcer.Announce("Unavailable");
                    }
                    else
                    {
                        _bridgeServer.EnqueueCommand("click_mcdu_element",
                            new Dictionary<string, string> { ["index"] = fieldIdx.ToString() });
                        // Re-read the page after the click so its RESULT shows up in
                        // the list — e.g. activating the company-fpln dropdown button
                        // makes its INSERT* / CLEAR* menu items appear, or a button
                        // changes the page. Without this the list looked unchanged and
                        // the action read as "didn't do anything".
                        ScheduleRefresh();
                    }
                }
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            // Clear the selected field on the MFD (send empty value — the
            // agent backspaces over the existing content, then ENT).
            int fieldIdx = SelectedElementIndex();
            if (fieldIdx > 0)
            {
                _bridgeServer.EnqueueCommand("send_to_field", new Dictionary<string, string>
                {
                    ["index"] = fieldIdx.ToString(),
                    ["text"]  = ""
                });
                _announcer.Announce("Cleared");
                ScheduleRefresh();
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Back)
        {
            // Airbus CLR: clear the scratchpad first, else the selected field.
            PerformClear();
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            _bridgeServer.EnqueueCommand("get_mcdu_elements");
            e.Handled = true;
        }
    }

    // Re-fetch the MFD page a short moment after a mutating action (a click, a
    // field commit, a clear), so the action's RESULT is reflected in the list —
    // dropdown menus that open (e.g. company-fpln INSERT*), buttons that change the
    // page, fields that update. The delay lets the MFD apply the change first.
    // Mirrors the page-selector's post-navigate refresh.
    private void ScheduleRefresh()
    {
        // Re-fetch TWICE — once quickly, once after the MFD has fully settled — so a
        // scroll / click / page-change result reliably lands even when the first
        // read fires before the MFD has redrawn. (A single 450 ms read sometimes
        // landed too early and the page looked unchanged.)
        foreach (int delay in new[] { 250, 700 })
        {
            var t = new System.Windows.Forms.Timer { Interval = delay };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); _bridgeServer.EnqueueCommand("get_mcdu_elements"); };
            t.Start();
        }
    }

    private void FormKeyDown(object? sender, KeyEventArgs e)
    {
        // Page navigation. The plain arrows move the cursor through the lines, so
        // page stepping is on Ctrl+Up/Down (and PageUp/PageDown) = previous/next
        // page. The MFD has only a single LINEAR page order (and vertical scroll
        // within a page) — there is no horizontal page axis — so Left/Right are NOT
        // bound to paging (they would just duplicate Up/Down).
        if ((e.Control && e.KeyCode == Keys.Up) || e.KeyCode == Keys.PageUp)
        {
            SendCommand("key_prev_page");
            _announcer.Announce("Previous page");   // immediate feedback; the new
            ScheduleRefresh();                       // page title also auto-announces
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if ((e.Control && e.KeyCode == Keys.Down) || e.KeyCode == Keys.PageDown)
        {
            SendCommand("key_next_page");
            _announcer.Announce("Next page");
            ScheduleRefresh();
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridgeServer.StateUpdated -= OnBridgeStateUpdated;
            _statusTimer?.Stop();
            _statusTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private class McduElement
    {
        public int Index;
        public string Text = "";
        public string Kind = "";
        public string Value = "";
        public bool Disabled;
        // "expanded" / "collapsed" for combo boxes; "" for everything else.
        public string ExpandState = "";
    }
}
