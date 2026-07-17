using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FirstOfficer;

/// <summary>
/// Shared, aircraft-agnostic First Officer window.
/// Contains two tabs: Checklists and Flows.
/// Designed for NVDA / JAWS screen reader use — all controls have explicit accessible names.
///
/// All aircraft-specific construction (executor, evaluator, data-manager binding,
/// flow/checklist data, auto/phase managers, window title) is provided by the injected
/// <see cref="IFoProfile{TExec,TState}"/>. Everything else (tabs, tree, listboxes,
/// buttons, handlers, SimBrief load, timer, threading) is identical across aircraft.
///
/// Accessibility notes:
/// - NativeAccessibleTreeView used for checklist (bypasses .NET 9 UIA bug)
/// - Checkboxes are per-node state images (CheckboxStateImages + ShowCheckBox), NOT
///   TreeView.CheckBoxes — a TVS_CHECKBOXES tree makes NVDA announce every visited
///   group header as a checkbox (see NativeAccessibleTreeView doc)
/// - Space on a checklist node toggles the item
/// - Flow steps are displayed in a ListBox (simple, screen-reader friendly)
/// - All status changes are announced via ScreenReaderAnnouncer
/// </summary>
public class FirstOfficerForm<TExec, TState> : Form, IFirstOfficerWindow
    where TExec : IFoActionExecutor
    where TState : IFoStateEvaluator
{
    // ------------------------------------------------------------------
    // Dependencies
    // ------------------------------------------------------------------
    private readonly IFoProfile<TExec, TState> _profile;
    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly UserSettings _settings;
    private readonly SimBriefService _simBriefService;

    // ------------------------------------------------------------------
    // First Officer services
    // ------------------------------------------------------------------
    private readonly TState _stateEval;
    private readonly TExec _actionExec;
    private readonly ChecklistManager<TExec, TState> _checklistMgr;
    private readonly FlowManager<TExec, TState> _flowMgr;
    private readonly IFoPhaseMonitor _flightPhaseMon;
    private readonly IFoAutoManager _foAutoMgr;

    // ------------------------------------------------------------------
    // Data
    // ------------------------------------------------------------------
    private readonly List<ChecklistGroup<TExec, TState>> _checklistGroups;
    private readonly List<FlowDefinition<TState>> _flows;
    private SimBriefOFP? _loadedOFP;

    // Latest one-shot values fed to FOAutoManager
    private double _latestAgl = double.NaN;
    private double _latestIas;
    // Latest engine N2 (percent) fed to the state evaluator for engine-start detection
    private double _latestEng1N2;
    private double _latestEng2N2;

    // ------------------------------------------------------------------
    // UI — core
    // ------------------------------------------------------------------
    private TabControl _tabs = null!;
    private TabPage _checklistTab = null!;
    private TabPage _flowsTab = null!;

    // ------------------------------------------------------------------
    // UI — Checklist tab
    // ------------------------------------------------------------------
    private NativeAccessibleTreeView _checklistTree = null!;
    private Label _checklistStatusLabel = null!;
    private Button _resetSectionBtn = null!;
    private Button _resetAllBtn = null!;
    private Button _runRelatedFlowBtn = null!;
    private Button _loadSimBriefChecklistBtn = null!;

    // ------------------------------------------------------------------
    // UI — Flows tab
    // ------------------------------------------------------------------
    private ListBox _flowListBox = null!;
    private ListBox _stepsListBox = null!;
    private Label _flowStatusLabel = null!;
    private Label _currentStepLabel = null!;
    private Button _startFlowBtn = null!;
    private Button _pauseResumeBtn = null!;
    private Button _stopFlowBtn = null!;
    private Button _loadSimBriefBtn = null!;
    private CheckBox _speakProgressCheck = null!;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------
    private System.Windows.Forms.Timer _autoDetectTimer = null!;
    private bool _suppressTreeEvents;

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------
    public FirstOfficerForm(
        IFoProfile<TExec, TState> profile,
        SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        UserSettings settings,
        SimBriefService simBriefService)
    {
        _profile    = profile;
        _simConnect = simConnect;
        _announcer  = announcer;
        _settings   = settings;
        _simBriefService = simBriefService;

        // Build services via the injected profile
        _stateEval  = profile.CreateEvaluator();
        _actionExec = profile.CreateExecutor();
        _checklistGroups = profile.BuildChecklists();
        _flows       = profile.BuildFlows();
        _checklistMgr = new ChecklistManager<TExec, TState>(_stateEval, _actionExec, _checklistGroups);
        _flowMgr     = new FlowManager<TExec, TState>(_stateEval, _actionExec, _checklistMgr, announcer);
        _flightPhaseMon = profile.CreatePhaseMonitor(_actionExec, _stateEval, announcer);
        _flightPhaseMon.AutoLights10kEnabled = settings.FOAutoLights10kEnabled;
        _flightPhaseMon.AutoSeatbeltMode = (FoSeatbeltMode)settings.FOAutoSeatbeltMode;
        _foAutoMgr   = profile.CreateAutoManager(_actionExec, _stateEval, announcer, settings);

        // Wire services
        UpdateServicesFromConnection();

        // Subscribe to events
        _checklistMgr.ItemStateChanged   += OnChecklistItemChanged;
        _checklistMgr.GroupProgressChanged += OnGroupProgressChanged;
        _flowMgr.FlowStarted   += OnFlowStarted;
        _flowMgr.FlowCompleted += OnFlowCompleted;
        _flowMgr.FlowCancelled += OnFlowCancelled;
        _flowMgr.FlowFailed    += OnFlowFailed;
        _flowMgr.FlowPaused    += OnFlowPaused;
        _flowMgr.FlowResumed   += OnFlowResumed;
        _flowMgr.StepStarted   += OnStepStarted;
        _flowMgr.StepCompleted += OnStepCompleted;
        _flowMgr.StepFailed    += OnStepFailed;
        _flowMgr.StepSkipped   += OnStepSkipped;
        _flowMgr.CaptainReminderRequired += OnCaptainReminder;

        BuildUI();

        // Subscribe to position data for flight phase monitoring
        _simConnect.AircraftPositionReceived += OnAircraftPositionReceived;
        _simConnect.SimVarUpdated            += OnSimVarUpdated;

        // Auto-detection polling timer — 1 second interval
        // Also requests aircraft position + AGL + IAS each tick
        _autoDetectTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _autoDetectTimer.Tick += (_, _) =>
        {
            _checklistMgr.EvaluateAutoDetection();
            if (_simConnect.IsConnected)
            {
                _simConnect.RequestAircraftPosition();
                _simConnect.RequestFOAltitudeAGL();
                _simConnect.RequestFOAirspeedIndicated();
                _simConnect.RequestFOEngineN2();

                // LVar-based profiles (Fenix/FBW): poll OnRequest-registered control vars
                // onto the cache so checklist auto-detection can read them. PMDG evaluators
                // are not LVarStateEvaluator, so this is a no-op for them.
                if (_stateEval is MSFSBlindAssist.FirstOfficer.Generic.LVarStateEvaluator lvarEval)
                {
                    foreach (string field in lvarEval.OnRequestPollFields)
                        _simConnect.RequestVariable(field);
                }
            }
        };
        _autoDetectTimer.Start();

        PopulateChecklistTree();
        PopulateFlowList();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        UpdateServicesFromConnection();
        Show();
        BringToFront();
        Activate();
        _tabs.Focus();
        // Belt-and-braces: re-register group headers now the handle exists. Without
        // TVS_CHECKBOXES nothing stamps state images on them at handle creation, but this
        // keeps the interceptor armed from the first moment the tree is visible.
        foreach (TreeNode root in _checklistTree.Nodes)
            _checklistTree.HideCheckBox(root);
    }

    /// <summary>Apply updated automation settings (call after the Settings dialog's First Officer panel saves).</summary>
    public void ApplySettings()
    {
        _foAutoMgr.AutoFlapsEnabled    = _settings.FOAutoFlapsEnabled;
        _flightPhaseMon.AutoLights10kEnabled = _settings.FOAutoLights10kEnabled;
        _flightPhaseMon.AutoSeatbeltMode = (FoSeatbeltMode)_settings.FOAutoSeatbeltMode;
    }

    /// <summary>Update the data manager reference when SimConnect reconnects.</summary>
    public void OnSimConnectChanged()
    {
        UpdateServicesFromConnection();
    }

    /// <summary>
    /// Called from EFB form or other consumers when a SimBrief OFP is loaded.
    /// Provides transition altitude/level to the flight phase monitor.
    /// </summary>
    public void SetFlightPlan(SimBriefOFP ofp)
    {
        _loadedOFP = ofp;
        ApplyFlightPlanThresholds(ofp);
    }

    private void ApplyFlightPlanThresholds(SimBriefOFP ofp)
    {
        // Parse transition altitude (feet) and transition level (flight level → feet)
        int transAltFt = 0;
        if (int.TryParse(ofp.OriginTransAlt, out int ta) && ta > 0)
            transAltFt = ta;

        int transLvlFt = 0;
        if (int.TryParse(ofp.DestTransLevel, out int tl) && tl > 0)
        {
            // SimBrief trans_level: if > 1000 it's in feet already; if < 1000 it's a FL (multiply × 100)
            transLvlFt = tl > 1000 ? tl : tl * 100;
        }

        if (transAltFt > 0 || transLvlFt > 0)
        {
            int effectiveTl = transLvlFt > 0 ? transLvlFt : transAltFt;
            _flightPhaseMon.SetThresholds(transAltFt, effectiveTl);
            _announcer.AnnounceImmediate(
                $"Flight phase monitor: transition altitude {transAltFt} feet, transition level {effectiveTl} feet.");
        }

        // Store takeoff flap setting for use by Before Taxi flow (interface method —
        // works for any aircraft's evaluator, not just the 777).
        if (int.TryParse(ofp.TakeoffFlaps, out int flaps) && flaps > 0)
        {
            _stateEval.SetTakeoffFlaps(flaps);
            _announcer.AnnounceImmediate($"Takeoff flaps: {flaps}");
        }

        // Pressurization plan: FLT ALT = SimBrief cruise, LAND ALT = destination field
        // elevation (interface method — the 737 evaluator stores it rounded to the panel
        // knob steps; the 777 no-ops, its pressurization is automatic). DestElevation may
        // legitimately parse to 0 (sea level), so no > 0 gate on it. Clamp negative
        // elevations to 0 to match the evaluator's RoundToStep clamping.
        int? cruiseFt   = int.TryParse(ofp.InitialAltitude, out int crz) && crz > 0 ? crz : null;
        int? destElevFt = int.TryParse(ofp.DestElevation, out int elev) ? Math.Max(0, elev) : null;  // below-sea-level out of scope (min 0, matches evaluator clamp)
        _stateEval.SetPlannedPressurizationAltitudes(cruiseFt, destElevFt);
        if (cruiseFt != null || destElevFt != null)
        {
            var pressParts = new List<string>();
            if (cruiseFt != null)   pressParts.Add($"flight altitude {cruiseFt} feet");
            if (destElevFt != null) pressParts.Add($"landing altitude {destElevFt} feet");
            _announcer.AnnounceImmediate($"Pressurization plan: {string.Join(", ", pressParts)}.");
        }
    }

    private void OnAircraftPositionReceived(object? sender, SimConnectManager.AircraftPosition pos)
    {
        // Forward to both monitors (called on SimConnect message thread)
        _flightPhaseMon.Update(pos.Altitude, pos.VerticalSpeedFPM);
        _foAutoMgr.Update(pos.Altitude, pos.VerticalSpeedFPM, _latestAgl, _latestIas, pos.SimOnGround >= 0.5);
    }

    private void OnSimVarUpdated(object? sender, SimConnect.SimVarUpdateEventArgs e)
    {
        switch (e.VarName)
        {
            case "FO_ALTITUDE_AGL":  _latestAgl = e.Value; break;
            case "FO_AIRSPEED_IAS":  _latestIas = e.Value; break;
            case "FO_ENG1_N2":       _latestEng1N2 = e.Value; _stateEval.SetEngineN2(_latestEng1N2, _latestEng2N2); break;
            case "FO_ENG2_N2":       _latestEng2N2 = e.Value; _stateEval.SetEngineN2(_latestEng1N2, _latestEng2N2); break;
        }
    }

    // ------------------------------------------------------------------
    // Service wiring
    // ------------------------------------------------------------------

    private void UpdateServicesFromConnection()
    {
        _profile.BindDataManager(_stateEval, _simConnect);
        _profile.SetExecutorSimConnect(_actionExec, _simConnect.IsConnected ? _simConnect : null);
    }

    // ------------------------------------------------------------------
    // UI construction
    // ------------------------------------------------------------------

    private void BuildUI()
    {
        Text = _profile.Title;
        Width = 720;
        Height = 580;
        MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterScreen;
        AccessibleName = "First Officer";

        _tabs = new TabControl { Dock = DockStyle.Fill };

        _checklistTab = new TabPage("Checklists") { AccessibleName = "Checklists tab" };
        _flowsTab     = new TabPage("Flows")      { AccessibleName = "Flows tab" };

        _tabs.TabPages.Add(_checklistTab);
        _tabs.TabPages.Add(_flowsTab);

        Controls.Add(_tabs);

        BuildChecklistTab();
        BuildFlowsTab();

        // Keyboard shortcut: Escape to close
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Hide();
        };
        KeyPreview = true;
    }

    private void BuildChecklistTab()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // TreeView. Checkboxes come from CheckboxStateImages + per-node ShowCheckBox —
        // NEVER TreeView.CheckBoxes: in a TVS_CHECKBOXES tree the focused/expanded-once
        // items report MSAA role "check box" to NVDA even with their state image hidden,
        // which made every visited group header announce as "check box not checked"
        // (2026-07-16 fix; see NativeAccessibleTreeView class doc).
        _checklistTree = new NativeAccessibleTreeView
        {
            Dock       = DockStyle.Fill,
            CheckboxStateImages = true,
            ShowLines  = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            AccessibleName = "Checklists",
            AccessibleRole = AccessibleRole.Outline,
        };
        _checklistTree.AfterCheck    += ChecklistTree_AfterCheck;
        _checklistTree.KeyDown       += ChecklistTree_KeyDown;
        _checklistTree.AfterSelect   += ChecklistTree_AfterSelect;
        _checklistTree.BeforeExpand  += ChecklistTree_BeforeExpand;
        _checklistTree.AfterExpand   += ChecklistTree_AfterExpand;
        _checklistTree.AfterCollapse += ChecklistTree_AfterCollapse;
        _checklistTree.NodeMouseClick += ChecklistTree_NodeMouseClick;

        layout.Controls.Add(_checklistTree, 0, 0);

        // Status label
        _checklistStatusLabel = new Label
        {
            Text = "Select a checklist group to view items.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 22,
            AccessibleName = "Checklist status",
        };
        layout.Controls.Add(_checklistStatusLabel, 0, 1);

        // Buttons panel
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
        };

        _resetSectionBtn = MakeButton("Reset Section", "Reset all items in the selected section", ResetSection);
        _resetAllBtn     = MakeButton("Reset All", "Reset all checklist items", ResetAll);
        _runRelatedFlowBtn = MakeButton("Run Related Flow", "Run the flow associated with the selected section", RunRelatedFlow);
        _loadSimBriefChecklistBtn = MakeButton("Load SimBrief", "Load SimBrief flight plan and set transition altitudes", LoadSimBrief);

        // Run Related Flow before the reset buttons (user request 2026-07-08) —
        // starting a flow is the frequent action; resets are the exceptional one.
        // The Toggle Item button was removed 2026-07-11 (redundant: Space / the
        // checkbox itself toggles the selected item).
        btnPanel.Controls.AddRange(new Control[] { _runRelatedFlowBtn, _resetSectionBtn, _resetAllBtn, _loadSimBriefChecklistBtn });
        layout.Controls.Add(btnPanel, 0, 2);

        _checklistTab.Controls.Add(layout);
    }

    private void BuildFlowsTab()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(6),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Flow list (left)
        _flowListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Available flows",
        };
        _flowListBox.SelectedIndexChanged += FlowListBox_SelectedIndexChanged;
        layout.Controls.Add(_flowListBox, 0, 0);

        // Steps list (right)
        _stepsListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Flow steps",
        };
        layout.Controls.Add(_stepsListBox, 1, 0);

        // Current step label
        _currentStepLabel = new Label
        {
            Text = "No flow running.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 22,
            AccessibleName = "Current step",
        };
        layout.SetColumnSpan(_currentStepLabel, 2);
        layout.Controls.Add(_currentStepLabel, 0, 1);

        // Flow status label
        _flowStatusLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 22,
            AccessibleName = "Flow status",
        };
        layout.SetColumnSpan(_flowStatusLabel, 2);
        layout.Controls.Add(_flowStatusLabel, 0, 2);

        // Buttons
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
        };
        layout.SetColumnSpan(btnPanel, 2);

        _startFlowBtn    = MakeButton("Start Flow",    "Start the selected flow",   StartSelectedFlow);
        _pauseResumeBtn  = MakeButton("Pause / Resume","Pause or resume the running flow", PauseResumeFlow);
        _stopFlowBtn     = MakeButton("Stop Flow",     "Stop and cancel the running flow", StopFlow);
        _loadSimBriefBtn = MakeButton("Load SimBrief", "Load SimBrief flight plan and set transition altitudes", LoadSimBrief);

        _speakProgressCheck = new CheckBox
        {
            Text = "Speak flow progress",
            Checked = true,
            AutoSize = true,
            AccessibleName = "Speak flow progress",
        };

        btnPanel.Controls.AddRange(new Control[] {
            _startFlowBtn, _pauseResumeBtn, _stopFlowBtn, _loadSimBriefBtn,
            _speakProgressCheck });
        layout.Controls.Add(btnPanel, 0, 3);

        _flowsTab.Controls.Add(layout);

        UpdateFlowButtonStates();
    }

    // ------------------------------------------------------------------
    // Checklist tree population
    // ------------------------------------------------------------------

    private void PopulateChecklistTree()
    {
        _suppressTreeEvents = true;
        _checklistTree.BeginUpdate();
        _checklistTree.Nodes.Clear();

        foreach (var group in _checklistGroups)
        {
            var parentNode = new TreeNode(group.ProgressLabel)
            {
                Tag = group.Id,
            };

            // Dummy child so the expand indicator appears
            parentNode.Nodes.Add(new TreeNode("Loading...") { Tag = "placeholder" });

            _checklistTree.Nodes.Add(parentNode);
        }

        _checklistTree.EndUpdate();

        // Group headers are not tickable. Without TVS_CHECKBOXES nothing stamps a state
        // image on them unsolicited, but registering them with HideCheckBox arms the
        // WndProc interceptor as a safety net (a stray Checked write on a header would
        // otherwise give it a checkbox image). The "Loading..." placeholder is deliberately
        // NOT registered: it gets REMOVED on first expand — registering its native handle
        // would risk suppressing a real leaf's checkbox once comctl32 reuses that freed
        // handle. Only persistent non-tickable nodes (headers + Informational separators,
        // hidden in BeforeExpand) are registered with the interceptor.
        foreach (TreeNode parent in _checklistTree.Nodes)
            _checklistTree.HideCheckBox(parent);

        _suppressTreeEvents = false;
    }

    private void ChecklistTree_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var parent = e.Node;
        if (parent?.Tag is not string groupId) return;
        if (parent.Nodes.Count == 1 && parent.Nodes[0].Tag is "placeholder")
        {
            parent.Nodes.Clear();
            var group = _checklistGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return;

            _suppressTreeEvents = true;
            foreach (var item in group.Items)
            {
                var node = new TreeNode(item.Label)
                {
                    Tag = item.Id,
                    Checked = item.IsChecked,   // cached pre-attach; ShowCheckBox applies it
                };
                parent.Nodes.Add(node);
                // Tickable items get a checkbox state image; non-tickable items (e.g.
                // "— Below the line —" separators) stay plain tree items.
                if (item.ManualCompletionAllowed)
                    _checklistTree.ShowCheckBox(node);
                else
                    _checklistTree.HideCheckBox(node);
            }
            // Keep the parent registered with the interceptor (under TVS_CHECKBOXES,
            // adding children used to reassert the parent's state-image checkbox; without
            // the style this is a no-op guard).
            _checklistTree.HideCheckBox(parent);
            _suppressTreeEvents = false;
        }
    }

    private void ChecklistTree_AfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_suppressTreeEvents) return;
        if (e.Node == null) return;

        // Group headers, the placeholder, and non-tickable items have hidden checkboxes;
        // if a native toggle sneaks through anyway, re-hide (self-heal) and ignore.
        if (e.Node.Tag is not string itemId || e.Node.Parent?.Tag is not string groupId)
        {
            _checklistTree.HideCheckBox(e.Node);
            return;
        }

        var item = _checklistMgr.FindItem(groupId, itemId);
        if (item == null || !item.ManualCompletionAllowed)
        {
            _checklistTree.HideCheckBox(e.Node);
            return;
        }

        if (e.Node.Checked == item.IsChecked) return; // Prevent loop

        _suppressTreeEvents = true;
        if (_checklistMgr.ToggleItem(groupId, itemId) == null)
        {
            // Toggle rejected — revert the checkbox
            e.Node.Checked = item.IsChecked;
        }
        else
        {
            e.Node.Checked = item.IsChecked;
            string status = item.IsChecked ? "checked" : "unchecked";
            _announcer.AnnounceImmediate($"{item.Label}: {status}");
        }
        _suppressTreeEvents = false;
    }

    private void ChecklistTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || _checklistTree.SelectedNode == null) return;

        var node = _checklistTree.SelectedNode;
        if (node.Tag is string tagStr && tagStr != "placeholder" &&
            node.Parent?.Tag is string groupId)
        {
            var item = _checklistMgr.FindItem(groupId, tagStr);
            if (item != null && item.ManualCompletionAllowed)
                node.Checked = !node.Checked; // AfterCheck fires and handles the rest
        }

        // Always consume Space: on group headers, the placeholder, and non-tickable
        // separators the native TVS_CHECKBOXES control would otherwise cycle the state
        // image itself and resurrect a hidden checkbox.
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void ChecklistTree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        UpdateChecklistStatus();
    }

    // Without TVS_CHECKBOXES the native control no longer toggles on a state-image
    // click, so mouse users get the toggle here (same guarded path as the Space key).
    private void ChecklistTree_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_checklistTree.HitTest(e.Location).Location != TreeViewHitTestLocations.StateImage) return;
        if (e.Node?.Tag is not string itemId || itemId == "placeholder" ||
            e.Node.Parent?.Tag is not string groupId) return;

        var item = _checklistMgr.FindItem(groupId, itemId);
        if (item != null && item.ManualCompletionAllowed)
            e.Node.Checked = !e.Node.Checked; // AfterCheck fires and handles the rest
    }

    // Single-expand: keep only one top-level group open at a time so the tree stays
    // short and screen-reader navigation is predictable. Leaf/child nodes unaffected.
    private void ChecklistTree_AfterExpand(object? sender, TreeViewEventArgs e)
    {
        if (_suppressTreeEvents) return;
        var expanded = e.Node;
        if (expanded == null || expanded.Parent != null) return; // only when a root expands

        _suppressTreeEvents = true;
        try
        {
            foreach (TreeNode root in _checklistTree.Nodes)
                if (!ReferenceEquals(root, expanded) && root.IsExpanded)
                    root.Collapse();
            // No-op guard without TVS_CHECKBOXES (under it, expand/collapse used to
            // reassert header state-image checkboxes) — keeps the interceptor registered.
            foreach (TreeNode root in _checklistTree.Nodes)
                _checklistTree.HideCheckBox(root);
        }
        finally { _suppressTreeEvents = false; }
    }

    // Same no-op guard on user collapse (see AfterExpand).
    private void ChecklistTree_AfterCollapse(object? sender, TreeViewEventArgs e)
    {
        if (e.Node is { Parent: null } root)
            _checklistTree.HideCheckBox(root);
    }

    // ------------------------------------------------------------------
    // Checklist change callbacks (from ChecklistManager — may be on any thread)
    // ------------------------------------------------------------------

    private void OnChecklistItemChanged(ChecklistGroup<TExec, TState> group, ChecklistItem<TExec, TState> item)
    {
        if (InvokeRequired) { Invoke(() => OnChecklistItemChanged(group, item)); return; }
        RefreshTreeNodeForItem(group, item);
    }

    private void OnGroupProgressChanged(ChecklistGroup<TExec, TState> group)
    {
        if (InvokeRequired) { Invoke(() => OnGroupProgressChanged(group)); return; }
        RefreshGroupNode(group);
        UpdateChecklistStatus();
    }

    private void RefreshTreeNodeForItem(ChecklistGroup<TExec, TState> group, ChecklistItem<TExec, TState> item)
    {
        var parentNode = FindGroupNode(group.Id);
        if (parentNode == null) return;

        _suppressTreeEvents = true;
        foreach (TreeNode child in parentNode.Nodes)
        {
            if (child.Tag as string == item.Id)
            {
                child.Checked = item.IsChecked;
                break;
            }
        }
        _suppressTreeEvents = false;
    }

    private void RefreshGroupNode(ChecklistGroup<TExec, TState> group)
    {
        var node = FindGroupNode(group.Id);
        if (node == null) return;
        node.Text = group.ProgressLabel;
    }

    private TreeNode? FindGroupNode(string groupId)
    {
        foreach (TreeNode n in _checklistTree.Nodes)
            if (n.Tag as string == groupId) return n;
        return null;
    }

    private void UpdateChecklistStatus()
    {
        var node = _checklistTree.SelectedNode;
        if (node == null) { _checklistStatusLabel.Text = ""; return; }

        if (node.Parent == null && node.Tag is string groupId)
        {
            var group = _checklistGroups.FirstOrDefault(g => g.Id == groupId);
            _checklistStatusLabel.Text = group?.ProgressLabel ?? "";
        }
        else if (node.Parent != null && node.Tag is string itemId &&
                 node.Parent.Tag is string parentGroupId)
        {
            var item = _checklistMgr.FindItem(parentGroupId, itemId);
            _checklistStatusLabel.Text = item != null
                ? $"{item.Label} — {(item.IsChecked ? "Complete" : "Incomplete")}"
                : "";
        }
    }

    // ------------------------------------------------------------------
    // Checklist button handlers
    // ------------------------------------------------------------------

    private void ResetSection()
    {
        var selectedNode = _checklistTree.SelectedNode;
        // Find the group node (may be the selected node itself or its parent)
        var groupNode = selectedNode?.Parent == null ? selectedNode : selectedNode?.Parent;
        if (groupNode?.Tag is not string groupId) return;

        _checklistMgr.ResetGroup(groupId);

        // Clear all child checkboxes. Skip the placeholder and already-unchecked nodes:
        // TreeNode.Checked's setter writes the state image unconditionally, which would
        // resurrect a hidden checkbox (the "Loading..." placeholder has none).
        _suppressTreeEvents = true;
        foreach (TreeNode child in groupNode.Nodes)
            if (child.Tag is not "placeholder" && child.Checked)
                child.Checked = false;
        _suppressTreeEvents = false;

        var group = _checklistGroups.FirstOrDefault(g => g.Id == groupId);
        _announcer.AnnounceImmediate($"{group?.Name ?? groupId} checklist reset");
    }

    private void ResetAll()
    {
        _checklistMgr.ResetAll();
        // Same placeholder/hidden-checkbox care as ResetSection.
        _suppressTreeEvents = true;
        foreach (TreeNode parent in _checklistTree.Nodes)
            foreach (TreeNode child in parent.Nodes)
                if (child.Tag is not "placeholder" && child.Checked)
                    child.Checked = false;
        _suppressTreeEvents = false;
        _announcer.AnnounceImmediate("All checklists reset");
    }

    private void RunRelatedFlow()
    {
        var selectedNode = _checklistTree.SelectedNode;
        var groupNode = selectedNode?.Parent == null ? selectedNode : selectedNode?.Parent;
        if (groupNode?.Tag is not string groupId) return;

        var flow = FlowForGroup(groupId);
        if (flow == null)
        {
            _announcer.AnnounceImmediate("No related flow found for this section");
            return;
        }

        // Switch to Flows tab and start the flow
        _tabs.SelectedTab = _flowsTab;
        int flowIndex = _flows.IndexOf(flow);
        if (flowIndex >= 0) _flowListBox.SelectedIndex = flowIndex;
        StartSelectedFlow();
    }

    // Strip a trailing "_CL" so a readback group and its action group share one base
    // phase key (e.g. BEFORE_START_CL → BEFORE_START).
    private static string BasePhaseId(string groupId)
        => groupId.EndsWith("_CL", StringComparison.Ordinal) ? groupId[..^3] : groupId;

    /// <summary>
    /// Resolve the flow for a selected checklist group. Robust across aircraft whose flows
    /// reference the ACTION group, the readback "_CL" group, or neither: a flow's Id is the
    /// phase base (COCKPIT_PREP, BEFORE_START, …), so we match the exact related id, then the
    /// flow Id against the group's base phase, then any related id's base phase. This fixed
    /// the A380 (its flows list only the "_CL" groups, so selecting an action group found
    /// nothing) and hardens every aircraft against the same action-vs-readback mismatch.
    /// </summary>
    private FlowDefinition<TState>? FlowForGroup(string groupId)
    {
        string baseId = BasePhaseId(groupId);
        return _flows.FirstOrDefault(f => f.RelatedChecklistGroupIds.Contains(groupId))
            ?? _flows.FirstOrDefault(f => f.Id == baseId)
            ?? _flows.FirstOrDefault(f => f.RelatedChecklistGroupIds.Any(id => BasePhaseId(id) == baseId));
    }

    /// <summary>
    /// All checklist group ids a flow corresponds to — its declared related ids PLUS the
    /// action group (Id == flow.Id) and the readback group (flow.Id + "_CL") when those
    /// exist. Used to mark every related checklist section complete when the flow finishes.
    /// </summary>
    private IEnumerable<string> RelatedGroupIdsFor(FlowDefinition<TState> flow)
    {
        var ids = new HashSet<string>(flow.RelatedChecklistGroupIds, StringComparer.Ordinal);
        if (_checklistGroups.Any(g => g.Id == flow.Id)) ids.Add(flow.Id);
        string cl = flow.Id + "_CL";
        if (_checklistGroups.Any(g => g.Id == cl)) ids.Add(cl);
        return ids;
    }

    // ------------------------------------------------------------------
    // Flow list population
    // ------------------------------------------------------------------

    private void PopulateFlowList()
    {
        _flowListBox.Items.Clear();
        foreach (var flow in _flows)
            _flowListBox.Items.Add(flow.Name);

        // Pre-select first item so NVDA reads the flow name rather than announcing "0"
        // when focus first lands on the listbox.
        if (_flowListBox.Items.Count > 0)
            _flowListBox.SelectedIndex = 0;
    }

    private void FlowListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        int index = _flowListBox.SelectedIndex;
        if (index < 0 || index >= _flows.Count)
        {
            _stepsListBox.Items.Clear();
            return;
        }

        var flow = _flows[index];
        _stepsListBox.BeginUpdate();
        _stepsListBox.Items.Clear();
        for (int i = 0; i < flow.Steps.Count; i++)
        {
            var step = flow.Steps[i];
            string prefix = step.ActionType == FlowStepActionType.CaptainReminder ? "[CAPTAIN] " : "";
            _stepsListBox.Items.Add($"{i + 1}. {prefix}{step.Label}");
        }
        _stepsListBox.EndUpdate();

        UpdateFlowButtonStates();
    }

    // ------------------------------------------------------------------
    // Flow button handlers
    // ------------------------------------------------------------------

    private void StartSelectedFlow()
    {
        int index = _flowListBox.SelectedIndex;
        if (index < 0 || index >= _flows.Count)
        {
            _announcer.AnnounceImmediate("No flow selected");
            return;
        }
        if (_flowMgr.IsRunning)
        {
            _announcer.AnnounceImmediate("A flow is already running. Stop it first.");
            return;
        }
        _flowMgr.StartFlow(_flows[index]);
        UpdateFlowButtonStates();
    }

    private void PauseResumeFlow()
    {
        if (!_flowMgr.IsRunning) return;
        if (_flowMgr.IsPaused)
            _flowMgr.Resume();
        else
            _flowMgr.Pause();
        UpdateFlowButtonStates();
    }

    private void StopFlow()
    {
        if (!_flowMgr.IsRunning) return;
        _flowMgr.Cancel();
        UpdateFlowButtonStates();
    }

    private async void LoadSimBrief()
    {
        _loadSimBriefBtn.Enabled = false;
        if (_loadSimBriefChecklistBtn != null) _loadSimBriefChecklistBtn.Enabled = false;

        try
        {
            string username = _settings.SimbriefUsername;
            if (string.IsNullOrWhiteSpace(username))
            {
                _announcer.AnnounceImmediate("SimBrief username not set. Configure it in Settings.");
                return;
            }

            _announcer.AnnounceImmediate("Loading SimBrief flight plan...");

            SimBriefOFP ofp;
            try
            {
                ofp = await _simBriefService.FetchFullOFPAsync(username);
            }
            catch (Exception ex)
            {
                _announcer.AnnounceImmediate($"Failed to fetch SimBrief data: {ex.Message}");
                return;
            }

            _loadedOFP = ofp;
            ApplyFlightPlanThresholds(ofp);
            _flightPhaseMon.Reset();
            _announcer.AnnounceImmediate("SimBrief flight plan loaded.");
        }
        finally
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(() =>
                {
                    _loadSimBriefBtn.Enabled = true;
                    if (_loadSimBriefChecklistBtn != null) _loadSimBriefChecklistBtn.Enabled = true;
                });
        }
    }

    private void UpdateFlowButtonStates()
    {
        bool running = _flowMgr.IsRunning;
        bool paused  = _flowMgr.IsPaused;

        _startFlowBtn.Enabled    = !running;
        _pauseResumeBtn.Enabled  = running;
        _pauseResumeBtn.Text     = paused ? "Resume" : "Pause";
        _pauseResumeBtn.AccessibleName = paused ? "Resume flow" : "Pause flow";
        _stopFlowBtn.Enabled     = running;
    }

    // ------------------------------------------------------------------
    // Flow event callbacks (arrive on background thread)
    // ------------------------------------------------------------------

    private void OnFlowStarted(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowStarted(flow)); return; }
        _flowStatusLabel.Text = $"Running: {flow.Name}";
        UpdateFlowButtonStates();
    }

    private void OnFlowCompleted(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowCompleted(flow)); return; }
        _flowStatusLabel.Text = $"Complete: {flow.Name}";
        _currentStepLabel.Text = "Flow complete.";

        // Mark the phase's checklist section(s) complete — running the flow is the FO
        // working that phase, so its checklist header should read "Complete" instead of a
        // stale partial count (the phase's reminder items never auto-tick from state).
        foreach (var groupId in RelatedGroupIdsFor(flow))
            _checklistMgr.MarkGroupComplete(groupId);

        UpdateFlowButtonStates();
    }

    private void OnFlowCancelled(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowCancelled(flow)); return; }
        _flowStatusLabel.Text = $"Cancelled: {flow.Name}";
        UpdateFlowButtonStates();
    }

    private void OnFlowFailed(FlowDefinition<TState> flow, string reason)
    {
        if (InvokeRequired) { Invoke(() => OnFlowFailed(flow, reason)); return; }
        _flowStatusLabel.Text = $"Failed: {flow.Name} — {reason}";
        UpdateFlowButtonStates();
    }

    private void OnFlowPaused(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowPaused(flow)); return; }
        _flowStatusLabel.Text = $"Paused: {flow.Name}";
        UpdateFlowButtonStates();
    }

    private void OnFlowResumed(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowResumed(flow)); return; }
        _flowStatusLabel.Text = $"Resumed: {flow.Name}";
        UpdateFlowButtonStates();
    }

    private void OnStepStarted(FlowDefinition<TState> flow, FlowStep<TState> step, int index)
    {
        if (InvokeRequired) { Invoke(() => OnStepStarted(flow, step, index)); return; }
        _currentStepLabel.Text = $"Step {index + 1}: {step.Label}";
        HighlightFlowStep(index);
    }

    private void OnStepCompleted(FlowDefinition<TState> flow, FlowStep<TState> step, int index)
    {
        if (InvokeRequired) { Invoke(() => OnStepCompleted(flow, step, index)); return; }
        // Mark step as completed in list (prefix ✓)
        if (index < _stepsListBox.Items.Count)
        {
            string current = _stepsListBox.Items[index].ToString() ?? "";
            if (!current.StartsWith("✓"))
                _stepsListBox.Items[index] = "✓ " + current;
        }
    }

    private void OnStepFailed(FlowDefinition<TState> flow, FlowStep<TState> step, int index, string reason)
    {
        if (InvokeRequired) { Invoke(() => OnStepFailed(flow, step, index, reason)); return; }
        if (index < _stepsListBox.Items.Count)
        {
            string current = _stepsListBox.Items[index].ToString() ?? "";
            if (!current.StartsWith("✗"))
                _stepsListBox.Items[index] = "✗ " + current;
        }
    }

    private void OnStepSkipped(FlowDefinition<TState> flow, FlowStep<TState> step, int index)
    {
        if (InvokeRequired) { Invoke(() => OnStepSkipped(flow, step, index)); return; }
        if (index < _stepsListBox.Items.Count)
        {
            string current = _stepsListBox.Items[index].ToString() ?? "";
            if (!current.StartsWith("→"))
                _stepsListBox.Items[index] = "→ " + current;
        }
    }

    private void OnCaptainReminder(string text)
    {
        if (InvokeRequired) { Invoke(() => OnCaptainReminder(text)); return; }
        _flowStatusLabel.Text = $"Captain action: {text}";
    }

    private void HighlightFlowStep(int index)
    {
        if (index >= 0 && index < _stepsListBox.Items.Count)
            _stepsListBox.SelectedIndex = index;
    }

    // ------------------------------------------------------------------
    // Cleanup
    // ------------------------------------------------------------------

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hide rather than close unless app is exiting
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _autoDetectTimer.Stop();
        _flowMgr.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _simConnect.AircraftPositionReceived -= OnAircraftPositionReceived;
            _simConnect.SimVarUpdated            -= OnSimVarUpdated;
            _autoDetectTimer.Dispose();
            _flowMgr.Cancel();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------
    // UI helper
    // ------------------------------------------------------------------

    private static Button MakeButton(string text, string accessibleName, Action onClick)
    {
        var btn = new Button
        {
            Text           = text,
            AccessibleName = accessibleName,
            AutoSize       = true,
            Margin         = new Padding(3),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
