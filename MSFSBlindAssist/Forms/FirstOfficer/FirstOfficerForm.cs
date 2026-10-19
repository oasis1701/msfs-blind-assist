using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

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
    // Latest engine N2 (percent) fed to the state evaluator for engine-start detection. NaN until
    // answered: engine 1's answer landing first pushes engine 2 as unknown, never as a stopped 0.
    private double _latestEng1N2 = double.NaN;
    private double _latestEng2N2 = double.NaN;

    // In-flight start (owner, 2026-10-14): the window first seeing the aircraft flying without a
    // takeoff it watched marks the departure checklists complete (FoDepartureGroups). The detector
    // runs on the SimConnect thread; the marking runs on the UI timer.
    private readonly InFlightStartDetector _inFlightStart = new();
    private volatile bool _inFlightCatchUpPending;
    // _latestIas starts at 0 for the auto manager; the detector needs to know it was never read.
    private volatile bool _iasReceived;

    // The SimBrief plan is for ONE flight, as SeatbeltAutomation's is: it is forgotten once the
    // aircraft has really flown (FlyingMinAglFt and FlyingMinIasKt, InFlightStartDetector's
    // thresholds) and then slowed to taxi speed on the ground (set on the position thread,
    // consumed by the UI timer), and on a new flight load (OnFlightLoaded), so the next flight
    // never flies on the last one's transition altitude, takeoff flaps or pressurization. A GSX
    // warp or a teleport delivers single not-on-ground samples at about 0 ft and 0 kt: counted as
    // flying, each one forgot a plan just loaded for the flight about to be flown. A touch-and-go
    // never slows to taxi speed, so it keeps the plan. The next Shift+C loads the new plan, but
    // never the one just flown (_flownPlanId): SimBrief still serves it until the next one is
    // generated, and re-applied after landing it set up the next leg on the last one's values.
    private bool _airborneSeen;
    private volatile bool _forgetPlanPending;
    private bool _simBriefLoading;
    private string? _flownPlanId;
    private const double LandedTaxiIasKt = 40;

    // The Steps list's ✓ / → / ✗ marks belong to ONE run: the flow it ran and, per step index,
    // the strongest mark it earned (✗ over → over ✓, so a Skip-policy failure, which raises
    // StepFailed and then StepSkipped, reads ✗ and never "→ ✗"). Shown only while that flow is
    // the one selected, so browsing another flow mid-run never marks its rows.
    private FlowDefinition<TState>? _markedFlow;
    private readonly Dictionary<int, char> _stepMarks = new();

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
    private TextBox _checklistStatusBox = null!;
    private Button _resetSectionBtn = null!;
    private Button _resetAllBtn = null!;
    private Button _runRelatedFlowBtn = null!;
    private Button _loadSimBriefChecklistBtn = null!;

    // ------------------------------------------------------------------
    // UI — Flows tab
    // ------------------------------------------------------------------
    private ListBox _flowListBox = null!;
    private ListBox _stepsListBox = null!;
    private TextBox _flowStatusBox = null!;
    private TextBox _currentStepBox = null!;
    private Button _startFlowBtn = null!;
    private Button _pauseResumeBtn = null!;
    private Button _stopFlowBtn = null!;
    private Button _loadSimBriefBtn = null!;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------
    private System.Windows.Forms.Timer _autoDetectTimer = null!;
    private bool _suppressTreeEvents;
    // The window that had the foreground when ShowForm ran (the simulator after Shift+C,
    // MainForm after the File menu); Escape and a user close hand focus back to it.
    private IntPtr _previousWindow;
    private bool _shownBefore;

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
        _flightPhaseMon.AutoAltimetersEnabled = settings.FOAutoAltimetersEnabled;
        _flightPhaseMon.AutoSeatbeltMode = (FoSeatbeltMode)settings.FOAutoSeatbeltMode;
        _foAutoMgr   = profile.CreateAutoManager(_actionExec, _stateEval, announcer, settings);

        // Wire services
        UpdateServicesFromConnection();

        // Subscribe to events
        _checklistMgr.ItemStateChanged   += OnChecklistItemChanged;
        _checklistMgr.ItemActionFailed   += OnChecklistItemActionFailed;
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
            if (_inFlightCatchUpPending)
            {
                _inFlightCatchUpPending = false;
                // A background change the pilot did not trigger: spoken once, queued (CORE-7).
                if (FoDepartureGroups.MarkComplete(_checklistMgr) > 0)
                    _announcer.Announce(FoDepartureGroups.Announcement);
            }

            if (_forgetPlanPending)
            {
                _forgetPlanPending = false;
                ForgetFlightPlan(flown: true);
            }

            _checklistMgr.EvaluateAutoDetection();
            if (_simConnect.IsConnected)
            {
                _simConnect.RequestAircraftPosition();
                _simConnect.RequestFOAltitudeAGL();
                _simConnect.RequestFOAirspeedIndicated();
                _simConnect.RequestFOEngineN2();

                // iFly MAX8 only: the SDK's fuel-gauge digit cells read blank all flight,
                // so its center-pump logic reads the stock sim fuel system instead
                // (pushed back via OnSimVarUpdated -> SetSimCenterFuelLbs).
                if (_stateEval is MSFSBlindAssist.FirstOfficer.IFly737.IFly737StateEvaluator)
                    _simConnect.RequestFOCenterFuelLbs();

                // PMDG 777 only: its SDK exposes no gear lights, so the checklist gear lines
                // confirm the physical gear from the stock GEAR x POSITION SimVars
                // (Pmdg777GearConfirmation; pushed back via OnSimVarUpdated -> SetGearPosition).
                if (_stateEval is MSFSBlindAssist.FirstOfficer.AircraftStateEvaluator)
                    _simConnect.RequestFOGearPositions();

                // LVar-based profiles (Fenix/FBW): poll OnRequest-registered control vars
                // onto the cache so checklist auto-detection can read them. PMDG evaluators
                // are not LVarStateEvaluator, so this is a no-op for them. Cache-only: an
                // unchanged answer no longer re-runs MainForm's SimVarUpdated pipeline.
                if (_stateEval is MSFSBlindAssist.FirstOfficer.Generic.LVarStateEvaluator lvarEval)
                {
                    foreach (string field in lvarEval.OnRequestPollFields)
                        _simConnect.RequestVariableForCache(field);
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
        // A re-press while this window is already in front must not record itself as the
        // window to return to, or Escape would leave focus nowhere.
        IntPtr foreground = NativeForeground.Current();
        if (foreground != Handle) _previousWindow = foreground;

        UpdateServicesFromConnection();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false; // Flash to the front of the simulator, as the other ShowForm windows do

        // The first show starts on the tab strip; a re-show (Shift+C again) returns the pilot to
        // the control they left, such as the checklist item they were on, as the retired text
        // checklist did, instead of the tab strip every time. A flow that started or ended while
        // the window was hidden can have disabled that control (Stop Flow after the flow ended),
        // and a disabled control takes no focus: then the flow button that took over gets it.
        Control? returnTo = _shownBefore ? ActiveControl : null;
        if (returnTo is { Enabled: false }) returnTo = FlowButtonTakingOverFrom(returnTo);
        (returnTo ?? _tabs).Focus();
        _shownBefore = true;
        // Belt-and-braces: re-register group headers now the handle exists. Without
        // TVS_CHECKBOXES nothing stamps state images on them at handle creation, but this
        // keeps the interceptor armed from the first moment the tree is visible.
        foreach (TreeNode root in _checklistTree.Nodes)
            _checklistTree.HideCheckBox(root);

        // Auto-load the SimBrief plan on open (mirrors the EFB's Shift+E behavior) so
        // transition altitudes/flaps/pressurization are set without pressing Load
        // SimBrief. Only while no plan is loaded — reloads stay manual, and a plan is
        // forgotten after a landing or a new flight load (ForgetFlightPlan), so the next
        // flight's first Shift+C loads its own — and never over a fetch already in flight.
        if (_loadedOFP == null && !string.IsNullOrWhiteSpace(_settings.SimbriefUsername)
            && !_simBriefLoading)
            FetchAndApplySimBrief(automatic: true);
    }

    /// <summary>A flight was loaded in the simulator (MainForm's AircraftLoaded handler): the
    /// last flight's plan, phase latches and checklists are forgotten, and a flow still running
    /// is stopped.</summary>
    public void OnFlightLoaded()
    {
        // A flow started on the last flight must not go on writing into this one: a Secure flow
        // in its APU cool-down sent Battery OFF to the newly loaded aircraft. An aircraft switch
        // disposes the window for the same reason.
        if (_flowMgr.IsRunning) _flowMgr.Cancel();
        ForgetFlightPlan(flown: false);
        _airborneSeen = false;
        _forgetPlanPending = false;
        // The latches would otherwise carry the last flight over: a load from cruise to a gate
        // read as a descent through 10,000 ft and switched the landing lights on at the gate.
        _flightPhaseMon.Reset();
        _foAutoMgr.Reset();
        // The checklists were the last flight's record: kept, its latched Landing checklist read
        // "Landing gear: DOWN — Complete" on this flight's final with the gear up (a latched
        // group never reverts). A load in the air is seen afresh by the in-flight start rule
        // (FO-22), which marks this flight's departure groups again.
        _inFlightStart.Reset();
        _inFlightCatchUpPending = false;
        ClearAllChecklists();
    }

    /// <summary>Apply updated automation settings (call after the Settings dialog's First Officer panel saves).</summary>
    public void ApplySettings()
    {
        _foAutoMgr.AutoFlapsEnabled    = _settings.FOAutoFlapsEnabled;
        _flightPhaseMon.AutoLights10kEnabled = _settings.FOAutoLights10kEnabled;
        _flightPhaseMon.AutoAltimetersEnabled = _settings.FOAutoAltimetersEnabled;
        _flightPhaseMon.AutoSeatbeltMode = (FoSeatbeltMode)_settings.FOAutoSeatbeltMode;
    }

    /// <summary>Update the data manager reference when SimConnect reconnects.</summary>
    public void OnSimConnectChanged()
    {
        UpdateServicesFromConnection();
    }

    // Applies a loaded SimBrief plan and returns what to say about it, ONE utterance the caller
    // speaks: the four AnnounceImmediate calls this once made back to back each cancelled the one
    // before, so the pilot heard only "SimBrief flight plan loaded." and never the values the FO
    // acts on. A value the plan lacks clears the one an earlier plan set (no transition altitude,
    // no takeoff flaps), so a plan without it never keeps the last flight's.
    private string ApplyFlightPlan(SimBriefOFP ofp)
    {
        var spoken = new List<string> { "SimBrief flight plan loaded." };

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

        // A plan that gives only one of the two uses it for both, either way round (exact for the
        // US's 18,000 ft and FL180). With the level alone, a missing altitude once read "Transition
        // altitude 0 feet" and left both crossings unarmed: no standard on the climb, no QNH on the
        // descent, and at 18,000 ft "No transition altitude loaded … Load SimBrief".
        int effectiveTa = transAltFt > 0 ? transAltFt : transLvlFt;
        int effectiveTl = transLvlFt > 0 ? transLvlFt : transAltFt;
        _flightPhaseMon.SetThresholds(effectiveTa, effectiveTl);
        if (effectiveTa > 0)
            spoken.Add($"Transition altitude {effectiveTa} feet, transition level {effectiveTl} feet.");

        // Store takeoff flap setting for use by Before Taxi flow (interface method —
        // works for any aircraft's evaluator, not just the 777). 0 = none in this plan.
        int flaps = int.TryParse(ofp.TakeoffFlaps, out int f) && f > 0 ? f : 0;
        _stateEval.SetTakeoffFlaps(flaps);
        if (flaps > 0)
            spoken.Add($"Takeoff flaps {flaps}.");

        // Pressurization plan: FLT ALT = SimBrief cruise, LAND ALT = destination field
        // elevation (interface method — the 737 evaluator stores it rounded to the panel
        // knob steps; the 777 no-ops, its pressurization is automatic). DestElevation may
        // legitimately parse to 0 (sea level), so no > 0 gate on it. Clamp negative
        // elevations to 0 to match the evaluator's RoundToStep clamping.
        int? cruiseFt   = int.TryParse(ofp.InitialAltitude, out int crz) && crz > 0 ? crz : null;
        int? destElevFt = int.TryParse(ofp.DestElevation, out int elev) ? Math.Max(0, elev) : null;  // below-sea-level out of scope (min 0, matches evaluator clamp)
        _stateEval.SetPlannedPressurizationAltitudes(cruiseFt, destElevFt);
        // The TOC/TOD seat-belt automation calls a level-off "cruise" only near this.
        _flightPhaseMon.SetPlannedCruiseAltitude(cruiseFt);
        if (cruiseFt != null || destElevFt != null)
        {
            var pressParts = new List<string>();
            if (cruiseFt != null)   pressParts.Add($"flight altitude {cruiseFt} feet");
            if (destElevFt != null) pressParts.Add($"landing altitude {destElevFt} feet");
            spoken.Add($"Pressurization plan: {string.Join(", ", pressParts)}.");
        }
        return string.Join(" ", spoken);
    }

    // Forgets the loaded SimBrief plan and everything it set (see _airborneSeen). Silent: no
    // aircraft state changes, and the next Shift+C speaks the new plan as it loads it.
    // flown: the plan's flight has landed, so the next automatic load refuses this same plan.
    private void ForgetFlightPlan(bool flown)
    {
        if (_loadedOFP == null) return;
        if (flown) _flownPlanId = _loadedOFP.IdentityKey;
        _loadedOFP = null;
        _flightPhaseMon.SetThresholds(0, 0);
        _stateEval.SetTakeoffFlaps(0);
        _stateEval.SetPlannedPressurizationAltitudes(null, null);
        _flightPhaseMon.SetPlannedCruiseAltitude(null);
        Log.Info("FO", flown
            ? "SimBrief plan forgotten: the flight it was for has landed"
            : "SimBrief plan forgotten: another flight was loaded");
    }

    private void OnAircraftPositionReceived(object? sender, SimConnectManager.AircraftPosition pos)
    {
        bool onGround = pos.SimOnGround >= 0.5;
        // Forward to both monitors (called on SimConnect message thread)
        _flightPhaseMon.Update(pos.Altitude, pos.VerticalSpeedFPM, onGround);
        _foAutoMgr.Update(pos.Altitude, pos.VerticalSpeedFPM, _latestAgl, _latestIas, onGround);
        if (_inFlightStart.Update(onGround, _latestAgl,
                _iasReceived ? _latestIas : double.NaN, DateTime.UtcNow))
            _inFlightCatchUpPending = true;

        // The plan's flight ends when the aircraft has really flown and then slows to taxi speed
        // on the ground (see _airborneSeen). Unread AGL or IAS never counts as flying.
        if (!onGround)
        {
            if (_latestAgl >= InFlightStartDetector.FlyingMinAglFt
                && _iasReceived && _latestIas >= InFlightStartDetector.FlyingMinIasKt)
                _airborneSeen = true;
        }
        else if (_airborneSeen && _iasReceived && _latestIas < LandedTaxiIasKt)
        {
            _airborneSeen = false;
            _forgetPlanPending = true;
        }
    }

    private void OnSimVarUpdated(object? sender, SimConnect.SimVarUpdateEventArgs e)
    {
        switch (e.VarName)
        {
            case "FO_ALTITUDE_AGL":  _latestAgl = e.Value; break;
            case "FO_AIRSPEED_IAS":  _latestIas = e.Value; _iasReceived = true; break;
            case "FO_ENG1_N2":       _latestEng1N2 = e.Value; _stateEval.SetEngineN2(_latestEng1N2, _latestEng2N2); break;
            case "FO_ENG2_N2":       _latestEng2N2 = e.Value; _stateEval.SetEngineN2(_latestEng1N2, _latestEng2N2); break;
            case "FO_ENG3_N2":
                if (_stateEval is MSFSBlindAssist.FirstOfficer.IFoEngine3N2Sink eng3Sink)
                    eng3Sink.SetEngine3N2(e.Value);
                break;
            case "FO_GEAR_LEFT_POS":
            case "FO_GEAR_CENTER_POS":
            case "FO_GEAR_RIGHT_POS":
                if (_stateEval is MSFSBlindAssist.FirstOfficer.AircraftStateEvaluator eval777)
                    eval777.SetGearPosition(e.VarName, e.Value);
                break;
            case "FO_CENTER_FUEL_LBS":
                if (_stateEval is MSFSBlindAssist.FirstOfficer.IFly737.IFly737StateEvaluator iflyEval)
                    iflyEval.SetSimCenterFuelLbs(e.Value);
                break;
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
            if (e.KeyCode == Keys.Escape) HideAndReturnFocus();
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

        // Status readout (a line's live value and state, without ticking it)
        _checklistStatusBox = MakeStatusBox("Checklist status", "Select a checklist group to view items.");
        layout.Controls.Add(_checklistStatusBox, 0, 1);

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

        // Tab order: the tree, then the buttons, then the status readout (still focusable, VAT-13).
        _checklistTree.TabIndex      = 0;
        btnPanel.TabIndex            = 1;
        _checklistStatusBox.TabIndex = 2;

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
        // Enter starts the selected flow, so a pilot never tabs past the lists to Start Flow.
        _flowListBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            StartSelectedFlow();
        };
        layout.Controls.Add(_flowListBox, 0, 0);

        // Steps list (right)
        _stepsListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Flow steps",
        };
        layout.Controls.Add(_stepsListBox, 1, 0);

        // Current step readout
        _currentStepBox = MakeStatusBox("Current step", "No flow running.");
        layout.SetColumnSpan(_currentStepBox, 2);
        layout.Controls.Add(_currentStepBox, 0, 1);

        // Flow status readout
        _flowStatusBox = MakeStatusBox("Flow status", "");
        layout.SetColumnSpan(_flowStatusBox, 2);
        layout.Controls.Add(_flowStatusBox, 0, 2);

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

        // A "Speak flow progress" checkbox sat here until 2026-09: nothing ever read it,
        // so unticking it silenced nothing. Removed rather than wired up (owner decision).
        btnPanel.Controls.AddRange(new Control[] {
            _startFlowBtn, _pauseResumeBtn, _stopFlowBtn, _loadSimBriefBtn });
        layout.Controls.Add(btnPanel, 0, 3);

        // Tab order: the lists, then the buttons, then the status readouts. The readouts stay
        // focusable (VAT-13) but no longer sit between the flow list and Start Flow.
        _flowListBox.TabIndex    = 0;
        _stepsListBox.TabIndex   = 1;
        btnPanel.TabIndex        = 2;
        _currentStepBox.TabIndex = 3;
        _flowStatusBox.TabIndex  = 4;

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
        if (_checklistMgr.ToggleItem(groupId, itemId, out string? leftAloneText) == null)
        {
            // Toggle rejected — revert the checkbox
            e.Node.Checked = item.IsChecked;
        }
        else
        {
            e.Node.Checked = item.IsChecked;
            // NVDA itself speaks the tick ("checked" / "not checked"), so the First Officer adds
            // only what it cannot say (CORE-7): why a tick was refused (ChecklistItem
            // .LeaveAloneWhen), or else the line's live value. Queued, so NVDA's own word is
            // never cut off; a line with neither says nothing more.
            if ((leftAloneText ?? ChecklistItemSpeech.TickText(item.ReadLiveValue(_stateEval))) is { } speech)
                _announcer.Announce(speech);
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

    // The pilot ticked this by hand, the linked action ran, and the state never agreed —
    // so the item just un-ticked itself. That correction is otherwise silent and
    // visual-only, which is no use to a blind pilot who has left the tree. Speaking a
    // failure is permitted under the screen-reader rule (it is an error condition, not a
    // UI interaction), and it is QUEUED so it can never cut across a landing callout.
    private void OnChecklistItemActionFailed(ChecklistGroup<TExec, TState> group,
                                             ChecklistItem<TExec, TState> item)
    {
        if (InvokeRequired) { Invoke(() => OnChecklistItemActionFailed(group, item)); return; }
        _announcer.Announce($"Unable to complete: {item.Label}");
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
        if (node == null) { _checklistStatusBox.Text = ""; return; }

        if (node.Parent == null && node.Tag is string groupId)
        {
            var group = _checklistGroups.FirstOrDefault(g => g.Id == groupId);
            _checklistStatusBox.Text = group?.ProgressLabel ?? "";
        }
        else if (node.Parent != null && node.Tag is string itemId &&
                 node.Parent.Tag is string parentGroupId)
        {
            var item = _checklistMgr.FindItem(parentGroupId, itemId);
            _checklistStatusBox.Text = item != null
                ? ChecklistItemSpeech.StatusText(item.Label, item.ReadLiveValue(_stateEval), item.IsChecked)
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
        ClearAllChecklists();
        _announcer.AnnounceImmediate("All checklists reset");
    }

    // Every group unticked, unlatched and without participation (Reset All, a new flight load).
    private void ClearAllChecklists()
    {
        _checklistMgr.ResetAll();
        // Same placeholder/hidden-checkbox care as ResetSection.
        _suppressTreeEvents = true;
        foreach (TreeNode parent in _checklistTree.Nodes)
            foreach (TreeNode child in parent.Nodes)
                if (child.Tag is not "placeholder" && child.Checked)
                    child.Checked = false;
        _suppressTreeEvents = false;
    }

    private void RunRelatedFlow()
    {
        var selectedNode = _checklistTree.SelectedNode;
        var groupNode = selectedNode?.Parent == null ? selectedNode : selectedNode?.Parent;
        if (groupNode?.Tag is not string groupId) return;

        var flow = RelatedFlow.For(_flows, groupId);
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

    /// <summary>
    /// All checklist group ids a flow corresponds to — its declared related ids PLUS the
    /// action group (Id == flow.Id) and the readback group (flow.Id + "_CL") when those
    /// exist. Used to mark every related checklist section complete when the flow finishes.
    /// </summary>
    private IEnumerable<string> RelatedGroupIdsFor(FlowDefinition<TState> flow) =>
        flow.CompletionGroupIds(id => _checklistGroups.Any(g => g.Id == id));

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

        RenderSteps(_flows[index]);
        UpdateFlowButtonStates();
    }

    // The Steps list for one flow, with the marks of its run when it is the flow that ran.
    private void RenderSteps(FlowDefinition<TState> flow)
    {
        _stepsListBox.BeginUpdate();
        _stepsListBox.Items.Clear();
        for (int i = 0; i < flow.Steps.Count; i++)
            _stepsListBox.Items.Add(StepRowText(flow, i));
        _stepsListBox.EndUpdate();
    }

    private string StepRowText(FlowDefinition<TState> flow, int index)
    {
        var step = flow.Steps[index];
        string prefix = step.ActionType == FlowStepActionType.CaptainReminder ? "[CAPTAIN] " : "";
        string row = $"{index + 1}. {prefix}{step.Label}";
        return ReferenceEquals(flow, _markedFlow) && _stepMarks.TryGetValue(index, out char mark)
            ? $"{mark} {row}" : row;
    }

    // True while the Steps list shows the flow the marks belong to.
    private bool ShowsMarkedFlow =>
        _markedFlow != null && _flowListBox.SelectedIndex >= 0 && _flowListBox.SelectedIndex < _flows.Count
        && ReferenceEquals(_flows[_flowListBox.SelectedIndex], _markedFlow);

    private static int MarkRank(char mark) => mark switch { '✗' => 3, '→' => 2, _ => 1 };

    // Records a step's mark for the run of `flow` (a weaker mark never replaces a stronger one)
    // and redraws its row when that flow is the one shown.
    private void MarkStep(FlowDefinition<TState> flow, int index, char mark)
    {
        if (!ReferenceEquals(flow, _markedFlow)) return;
        if (_stepMarks.TryGetValue(index, out char old) && MarkRank(old) >= MarkRank(mark)) return;
        _stepMarks[index] = mark;
        if (ShowsMarkedFlow && index < _stepsListBox.Items.Count)
            _stepsListBox.Items[index] = StepRowText(flow, index);
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
            _announcer.AnnounceImmediate(_flowMgr.IsStopping
                ? "The last flow is still stopping. Start again when it has stopped."
                : "A flow is already running. Stop it first.");
            return;
        }
        _flowMgr.StartFlow(_flows[index]);
        UpdateFlowButtonStates();
    }

    private void PauseResumeFlow()
    {
        if (!_flowMgr.IsRunning) return;
        if (_flowMgr.IsStopping)
        {
            _announcer.AnnounceImmediate(StoppingText());
            return;
        }
        if (_flowMgr.IsPaused)
            _flowMgr.Resume();
        else
            _flowMgr.Pause();
        UpdateFlowButtonStates();
    }

    // A Stop between steps is answered at once by "<flow> flow cancelled". The executor's step is
    // atomic, so a Stop during it lands only when it returns: about 25 s into the PMDG weather radar
    // test, which always restores the radar. Said at once, or the pilot heard nothing, Start said
    // "Stop it first" and Pause said "paused" over a stopped flow. A second Stop repeats it.
    private void StopFlow()
    {
        if (!_flowMgr.IsRunning) return;
        if (!_flowMgr.IsStopping)
        {
            bool finishesStepFirst = _flowMgr.IsInExecutorStep;
            _flowMgr.Cancel();
            if (!finishesStepFirst) { UpdateFlowButtonStates(); return; }
        }
        _announcer.AnnounceImmediate(StoppingText());
        UpdateFlowButtonStates();
    }

    private string StoppingText()
    {
        var flow = _flowMgr.CurrentFlow;
        int i = _flowMgr.CurrentStepIndex;
        return flow != null && i >= 0 && i < flow.Steps.Count
            ? $"Stopping after the current step: {flow.Steps[i].AnnounceText}"
            : "Stopping after the current step";
    }

    // The Load SimBrief buttons: always fetches and applies, even the plan just flown (the pilot's
    // choice, e.g. flying the same leg again).
    private void LoadSimBrief() => FetchAndApplySimBrief(automatic: false);

    // automatic: ShowForm's load on open, which never applies the plan whose flight just landed
    // (_flownPlanId).
    private async void FetchAndApplySimBrief(bool automatic)
    {
        // One fetch at a time. The two Load SimBrief buttons stay enabled while it runs:
        // disabling the one with focus moved focus on to the tab strip.
        if (_simBriefLoading)
        {
            _announcer.AnnounceImmediate("SimBrief flight plan is still loading.");
            return;
        }

        string username = _settings.SimbriefUsername;
        if (string.IsNullOrWhiteSpace(username))
        {
            _announcer.AnnounceImmediate("SimBrief username not set. Configure it in Settings.");
            return;
        }

        _simBriefLoading = true;
        try
        {
            _announcer.AnnounceImmediate("Loading SimBrief flight plan...");

            SimBriefOFP ofp;
            try
            {
                ofp = await _simBriefService.FetchFullOFPAsync(username);
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _announcer.AnnounceImmediate($"Failed to fetch SimBrief data: {ex.Message}");
                return;
            }

            // The window was disposed during the fetch (an aircraft switch): the plan is not
            // this aircraft's to apply, and "loaded" would be spoken over the new aircraft.
            if (IsDisposed) return;

            // SimBrief serves the latest plan, which after a landing is still the one just flown
            // until the next is generated. Applied by the load on open, it set the next leg up on
            // this leg's transition altitude, flaps and pressurization, and the next Shift+C no
            // longer loaded the new plan.
            if (automatic && _flownPlanId != null && ofp.IdentityKey == _flownPlanId)
            {
                _announcer.AnnounceImmediate(
                    "SimBrief still has the flight just flown. Generate the next flight plan, then press Load SimBrief.");
                return;
            }

            _flownPlanId = null;
            _loadedOFP = ofp;
            string spoken = ApplyFlightPlan(ofp);
            _flightPhaseMon.Reset();
            _announcer.AnnounceImmediate(spoken);
        }
        finally
        {
            _simBriefLoading = false;
        }
    }

    // runningOverride lets a caller assert "not running" even while FlowManager.IsRunning
    // is technically still true — see OnFlowCompleted/OnFlowCancelled/OnFlowFailed below,
    // which fire from inside RunFlowAsync before the run task has actually unwound.
    // This is safe only because StartSelectedFlow refuses to start a new flow while
    // FlowManager.IsRunning is still true. If a flow could ever be restarted while one is
    // already running, a late FlowCancelled/FlowCompleted/FlowFailed from the OLD run could
    // still fire after the new run has started and stamp "not running" over it.
    private void UpdateFlowButtonStates(bool? runningOverride = null)
    {
        bool running = runningOverride ?? _flowMgr.IsRunning;
        bool paused  = running && _flowMgr.IsPaused;

        // Disabling the FOCUSED button makes WinForms move focus to the next control in tab
        // order: Start Flow disabled itself before Pause and Stop were enabled, so the pilot who
        // pressed it landed on "Load SimBrief" (a second Space re-fetched the plan mid-flow), and
        // a flow ending under Pause or Stop did the same. The button that takes over is enabled
        // first and given focus, then the rest are set. Only while this window has focus: a
        // Focus() call on a background window would activate it over the simulator.
        if (running) { _pauseResumeBtn.Enabled = true; _stopFlowBtn.Enabled = true; }
        else _startFlowBtn.Enabled = true;
        if (ContainsFocus)
        {
            if (running && _startFlowBtn.Focused) _pauseResumeBtn.Focus();
            else if (!running && (_pauseResumeBtn.Focused || _stopFlowBtn.Focused)) _startFlowBtn.Focus();
        }

        _startFlowBtn.Enabled    = !running;
        _pauseResumeBtn.Enabled  = running;
        _pauseResumeBtn.Text     = paused ? "Resume" : "Pause";
        _pauseResumeBtn.AccessibleName = paused ? "Resume flow" : "Pause flow";
        _stopFlowBtn.Enabled     = running;
    }

    // The flow button that takes over from a disabled one: Start Flow once a flow has ended,
    // Pause while one runs. Null for any other control (ShowForm's re-show focus).
    private Control? FlowButtonTakingOverFrom(Control disabled)
    {
        if (disabled != _startFlowBtn && disabled != _pauseResumeBtn && disabled != _stopFlowBtn) return null;
        if (_startFlowBtn.Enabled) return _startFlowBtn;
        return _pauseResumeBtn.Enabled ? _pauseResumeBtn : null;
    }

    // ------------------------------------------------------------------
    // Flow event callbacks (arrive on background thread)
    // ------------------------------------------------------------------

    private void OnFlowStarted(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowStarted(flow)); return; }
        _flowStatusBox.Text = $"Running: {flow.Name}";
        // A new run starts with a clean Steps list: the last run's marks are dropped.
        _markedFlow = flow;
        _stepMarks.Clear();
        if (ShowsMarkedFlow) RenderSteps(flow);
        UpdateFlowButtonStates();
    }

    private void OnFlowCompleted(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowCompleted(flow)); return; }
        _flowStatusBox.Text = $"Complete: {flow.Name}";
        _currentStepBox.Text = "Flow complete.";

        // Mark the phase's checklist section(s) complete — running the flow is the FO
        // working that phase, so its checklist header should read "Complete" instead of a
        // stale partial count (the phase's reminder items never auto-tick from state).
        // Never tick — or latch over — an item whose step the flow announced as skipped.
        // The pilot heard "Skipping: X"; force-ticking X two seconds later contradicted
        // that out loud, and the latch made the contradiction permanent for the session.
        var unfinished = _flowMgr.UnfinishedChecklistItemIds;
        foreach (var groupId in RelatedGroupIdsFor(flow))
            _checklistMgr.MarkGroupComplete(groupId, unfinished);

        // These three events fire from INSIDE RunFlowAsync, while FlowManager.IsRunning is
        // still true (the run task hasn't unwound yet) — the bare call read that as "still
        // running" and left Start Flow disabled until the pilot changed the flow selection.
        UpdateFlowButtonStates(runningOverride: false);
    }

    private void OnFlowCancelled(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowCancelled(flow)); return; }
        _flowStatusBox.Text = $"Cancelled: {flow.Name}";
        // See the comment in OnFlowCompleted: fires while IsRunning is still true.
        UpdateFlowButtonStates(runningOverride: false);
    }

    private void OnFlowFailed(FlowDefinition<TState> flow, string reason)
    {
        if (InvokeRequired) { Invoke(() => OnFlowFailed(flow, reason)); return; }
        _flowStatusBox.Text = $"Failed: {flow.Name} — {reason}";
        // See the comment in OnFlowCompleted: fires while IsRunning is still true.
        UpdateFlowButtonStates(runningOverride: false);
    }

    private void OnFlowPaused(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowPaused(flow)); return; }
        _flowStatusBox.Text = $"Paused: {flow.Name}";
        UpdateFlowButtonStates();
    }

    private void OnFlowResumed(FlowDefinition<TState> flow)
    {
        if (InvokeRequired) { Invoke(() => OnFlowResumed(flow)); return; }
        _flowStatusBox.Text = $"Resumed: {flow.Name}";
        UpdateFlowButtonStates();
    }

    private void OnStepStarted(FlowDefinition<TState> flow, FlowStep<TState> step, int index)
    {
        if (InvokeRequired) { Invoke(() => OnStepStarted(flow, step, index)); return; }
        _currentStepBox.Text = $"Step {index + 1}: {step.Label}";
        // Only the running flow's own rows: selecting row N of a flow the pilot is browsing
        // would point at a step that is not running.
        if (ReferenceEquals(flow, _markedFlow) && ShowsMarkedFlow)
            HighlightFlowStep(index);
    }

    private void OnStepCompleted(FlowDefinition<TState> flow, FlowStep<TState> step, int index)
    {
        if (InvokeRequired) { Invoke(() => OnStepCompleted(flow, step, index)); return; }
        MarkStep(flow, index, '✓');
    }

    private void OnStepFailed(FlowDefinition<TState> flow, FlowStep<TState> step, int index, string reason)
    {
        if (InvokeRequired) { Invoke(() => OnStepFailed(flow, step, index, reason)); return; }
        MarkStep(flow, index, '✗');
    }

    private void OnStepSkipped(FlowDefinition<TState> flow, FlowStep<TState> step, int index)
    {
        if (InvokeRequired) { Invoke(() => OnStepSkipped(flow, step, index)); return; }
        MarkStep(flow, index, '→');
    }

    private void OnCaptainReminder(string text)
    {
        if (InvokeRequired) { Invoke(() => OnCaptainReminder(text)); return; }
        _flowStatusBox.Text = $"Captain action: {text}";
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
            HideAndReturnFocus();
            return;
        }

        _autoDetectTimer.Stop();
        _flowMgr.Cancel();
        base.OnFormClosing(e);
    }

    // Hide (the First Officer keeps running) and give focus back to the window the pilot came
    // from, as the retired text checklist did: Shift+C, read, Escape lands back in the sim.
    private void HideAndReturnFocus()
    {
        Hide();
        NativeForeground.Restore(_previousWindow);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _simConnect.AircraftPositionReceived -= OnAircraftPositionReceived;
            _simConnect.SimVarUpdated            -= OnSimVarUpdated;
            _autoDetectTimer.Dispose();
            // Unhooked before the cancel: the cancelled run still raises FlowCancelled (and
            // speaks "flow cancelled" itself), which must not reach this disposed window.
            _flowMgr.FlowStarted   -= OnFlowStarted;
            _flowMgr.FlowCompleted -= OnFlowCompleted;
            _flowMgr.FlowCancelled -= OnFlowCancelled;
            _flowMgr.FlowFailed    -= OnFlowFailed;
            _flowMgr.FlowPaused    -= OnFlowPaused;
            _flowMgr.FlowResumed   -= OnFlowResumed;
            _flowMgr.StepStarted   -= OnStepStarted;
            _flowMgr.StepCompleted -= OnStepCompleted;
            _flowMgr.StepFailed    -= OnStepFailed;
            _flowMgr.StepSkipped   -= OnStepSkipped;
            _flowMgr.CaptainReminderRequired -= OnCaptainReminder;
            _flowMgr.Cancel();
            // A test the executor still holds (a fire test, T.O CONFIG) is released now, while the
            // sim still listens: on app exit MainForm disconnects right after this, and the hold's
            // own release then went nowhere, leaving the test held in the sim.
            _actionExec.ReleaseHeldPresses();
            // Detach the executor, its disconnect path: the cancel stops the flow, not a write
            // already queued on the executor's gate or pacing, and on an aircraft switch that
            // write would land on the NEXT aircraft (a 777 window-heat event id is a 737 centre
            // fuel pump). Each executor re-checks after its pacing wait and refuses.
            _profile.SetExecutorSimConnect(_actionExec, null);
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------
    // UI helper
    // ------------------------------------------------------------------

    // A status readout is a read-only TextBox, never a Label (VAT-13): a Label is not in the tab
    // order, and NVDA read only its fixed AccessibleName ("Flow status"), never the status in it.
    // An edit control's name labels its value instead of replacing it.
    private static TextBox MakeStatusBox(string accessibleName, string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AccessibleName = accessibleName,
    };

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
