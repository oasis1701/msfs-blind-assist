using System.Collections.Concurrent;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.FenixA320;
using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Forms.PMDG777;
using MSFSBlindAssist.Forms.HS787;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Patching;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist;
public partial class MainForm : Form
{
    // Event batching configuration - Proven pattern from aerospace/trading systems
    // Reduces UI thread marshaling overhead by ~95% for high-volume variable updates
    private const int EVENT_BATCH_INTERVAL_MS = 33; // ~30 batches/second (balances latency vs throughput)
    private const int MAX_QUEUE_SIZE = 2000; // Safety limit to prevent unbounded memory growth
    private const int MAX_BATCH_SIZE = 50; // Process up to 50 events per batch (prevents UI freezing)

    private SimConnectManager simConnectManager = null!;
    private SimVarMonitor simVarMonitor = null!;
    private ScreenReaderAnnouncer announcer = null!;
    private HotkeyManager hotkeyManager = null!;
    private IAirportDataProvider? airportDataProvider;
    // Typed reference to the augmentation decorator so Phase 6 can call PrefetchAsync.
    private MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider? _augmentingProvider;
    private ChecklistForm? checklistForm;
    private FenixMonitorManagerForm? fenixMonitorManagerForm;
    private Forms.FBWA380.FBWA380MonitorManagerForm? fbwA380MonitorManagerForm;
    private Forms.FlyByWireA320.FlyByWireA320MonitorManagerForm? fbwA320MonitorManagerForm;
    private Forms.HS787.HS787MonitorManagerForm? hs787MonitorManagerForm;
    private PMDGAnnouncementMonitorForm? pmdgAnnouncementMonitorForm;
    private MSFSBlindAssist.Services.PMDGProgPageMonitor? pmdgProgPageMonitor;
    private FenixMCDUForm? fenixMCDUForm;
    private FenixMCDUService? fenixMCDUService;
    private Forms.Fenix.FenixEFBForm? fenixEFBForm;
    private MSFSBlindAssist.Forms.FlyByWireA320.FlyByWireMCDUForm? flyByWireMCDUForm;
    private MSFSBlindAssist.Services.FlyByWireMCDUService? flyByWireMCDUService;
    private System.Windows.Forms.Form? pmdgCDUForm;
    private Forms.FirstOfficer.FirstOfficerForm<FirstOfficer.AircraftActionExecutor, FirstOfficer.AircraftStateEvaluator>? pmdg777FirstOfficerForm;
    private Forms.FirstOfficer.FirstOfficerForm<FirstOfficer.PMDG737.AircraftActionExecutor, FirstOfficer.PMDG737.AircraftStateEvaluator>? pmdg737FirstOfficerForm;
    private Forms.FBWA380.FBWA380MCDUForm? fbwA380MCDUForm;
    private Forms.FBWA380.FbwEfbForm? fbwEfbForm;
    // No-injection A380X transport: reads/drives the MFD live through the
    // MSFS Coherent GT debugger (127.0.0.1:19999). Created when the A380X
    // loads; replaces the injection bridge for the MCDU.
    private CoherentDebuggerClient? coherentClient;
    // No-injection A380X flyPad transport: reads/drives the EFB live through the
    // same Coherent GT debugger, resolved to the flyPad view ("- EFB" title).
    // Replaces the injection bridge for the flyPad.
    private CoherentEFBClient? coherentEFBClient;
    // No-injection PMDG EFB transport: reads/drives the PMDG 737/777 EFB tablet
    // live through the Coherent GT debugger. One client per crew side (Captain /
    // First Officer), each reusing the generic FbwEfbForm. Created lazily on
    // Shift+T (CA) / Ctrl+Shift+T (FO); disposed on aircraft swap.
    private CoherentPmdgEfbClient? coherentPmdgEfbCaptain;
    private CoherentPmdgEfbClient? coherentPmdgEfbFirstOfficer;
    private Forms.FBWA380.FbwEfbForm? pmdgCoherentEfbCaptainForm;
    private Forms.FBWA380.FbwEfbForm? pmdgCoherentEfbFirstOfficerForm;
    // No-injection A380X ND OANS transport (BTV exit selection / airport map),
    // resolved to the Captain ND view ("A380X_ND_1"). Reuses FbwEfbForm.
    private CoherentNDClient? coherentNDClient;
    // Background A380X E/WD failure monitor: scrapes the abnormal/warning
    // procedures (which have no SimVar) from the E/WD Coherent view and announces
    // new failures. Runs whenever the A380X is active — no window needed.
    private CoherentEWDClient? coherentEWDClient;
    // Authoritative A380 failure announcer — reads the FwsCore (presentedFailures) directly
    // so a master caution always names its cause, even for WIP procedures the E/WD DOM
    // doesn't render (e.g. ENG 3/4 FAIL). Owns failure call-outs; the E/WD scrape keeps
    // memos/PFD/status (coherentEWDClient.AnnounceWarnings set false to avoid double-speak).
    private CoherentFwsFailureClient? coherentFwsFailureClient;
    private Forms.FBWA380.FBWA380OansForm? fbwA380OansForm;
    private Forms.FBWA380.FBWA380RmpForm? fbwA380RmpForm;
    // A32NX DCDU (CPDLC) window — one-shot Coherent evals, no persistent socket.
    private Forms.FlyByWireA320.FlyByWireDcduForm? fbwDcduForm;
    // Live A380X Electronic Checklist window (normal checklists + ECP controls),
    // read from the E/WD Coherent view. Opened by the Checklist hotkey on the A380.
    private Forms.FBWA380.FBWA380ChecklistForm? fbwA380ChecklistForm;
    private HS787FMCForm? hs787FMCForm;
    private HS787SimBriefForm? hs787SimBriefForm;
    // Background Coherent reader for the WT IRS "TIME TO ALIGN" state — writes the synthetic
    // MSFSBA_IRS_ALIGN_STATE / _MINUTES L-vars the HS787 def reads. Runs while the HS787 is loaded.
    private SimConnect.CoherentHS787IrsClient? hs787IrsClient;
    // Always-on EICAS Crew-Alerting-System monitor — announces new cautions/warnings as they post.
    private SimConnect.CoherentHS787CasClient? hs787CasClient;
    // On-demand EICAS alert window (Alt+E), fed by hs787CasClient.GetAlertsText().
    private Forms.HS787.HS787EicasForm? hs787EicasForm;
    private TakeoffAssistManager takeoffAssistManager = null!;
    private HandFlyManager handFlyManager = null!;
    private VisualGuidanceManager visualGuidanceManager = null!;
    private MSFSBlindAssist.Services.GroundSpeedAnnouncer groundSpeedAnnouncer = null!;
    private MSFSBlindAssist.Services.LandingRateAnnouncer landingRateAnnouncer = null!;
    private MSFSBlindAssist.Services.AltitudeCalloutAnnouncer altitudeCalloutAnnouncer = null!;
    private ElectronicFlightBagForm? electronicFlightBagForm;
    private TrackFixForm? trackFixForm;
    private TcasForm? tcasForm;
    private MSFSBlindAssist.Services.TcasService? tcasService;
    // Background monitor that announces ActiveSky weather updates as they
    // come in. Runs unconditionally — it self-skips when AS isn't detected
    // (silent fallback), so users without AS see/hear no change.
    private MSFSBlindAssist.Services.ActiveSkyWeatherMonitor? activeSkyWeatherMonitor;
    private Forms.WeatherRadarForm? weatherRadarForm;
    private MSFSBlindAssist.Navigation.FlightPlanManager flightPlanManager = null!;
    private MSFSBlindAssist.Navigation.WaypointTracker waypointTracker = null!;
    private TaxiGuidanceManager taxiGuidanceManager = null!;
    private DockingGuidanceManager dockingGuidanceManager = null!;
    private TaxiAssistForm? taxiAssistForm;
    private LandingExitPlanner landingExitPlanner = null!;
    private GroundTrafficMonitor groundTrafficMonitor = null!;

    // Access GSX integration — owns its own SimConnect client (distinct
    // WM_USER id 0x0403). The form is created lazily on first hotkey use and
    // hidden (not closed) on dismiss so the service can keep speaking
    // tooltip updates in the background when configured.
    private GsxService? _gsxService;
    private Forms.AccessGSXForm? _accessGsxForm;

    // Per-aircraft gsx.cfg geometry — docking consumes only the door SIDE (the spoken
    // "jetway on your left/right" cue); the stop math is datum-aligned and takes no door
    // offset. Constructed once; background scan is warmed at startup so the first docking
    // session has the side ready. Thread-safe internally (single-flight Lazy build).
    private readonly MSFSBlindAssist.Services.Gsx.GsxAirplaneProfile _gsxAirplaneProfile = new();
    // Tracks ICAOs that have already triggered a Refresh() so we only rebuild the map once
    // per distinct ICAO miss (a Refresh re-scans the package folders — seconds on a cold
    // disk). Mutated from concurrent Task.Run handlers — always lock(_refreshedIcaos).
    private readonly System.Collections.Generic.HashSet<string> _refreshedIcaos = new();

    // Latest SIM_ON_GROUND sample. Cached unconditionally from the SIM_ON_GROUND
    // event so any feature that needs to know "on ground vs airborne" right now
    // can read it without making a fresh SimConnect request. Defaults to true
    // (assume on ground) so a query before the first sample doesn't claim the
    // aircraft is in flight at startup. Used by AnnounceWhereAmI to gate the
    // ground-only Where-Am-I lookup so it doesn't report a phantom "Taxiway B"
    // while cruising over the airport.
    private bool _lastOnGround = true;
    private LandingExitForm? landingExitForm;

    // Per-session set of ICAOs already prefetched by AugmentingAirportDataProvider.
    // Guards automatic departure/destination prefetches so each airport is fetched at most once
    // per app session. Manual refresh (force:true) bypasses it.
    private readonly HashSet<string> _augmentPrefetched = new(StringComparer.OrdinalIgnoreCase);

    // FBW A380 STD-flag watchdog debounce (see the BARO_MB_WATCH_* branch in OnSimVarUpdated).
    private DateTime _a380BaroStdMismatchL = DateTime.MinValue, _a380BaroStdMismatchR = DateTime.MinValue;

    // MobiFlight end-to-end bridge probe state (see BridgeProbeTimer_Tick).
    private System.Windows.Forms.Timer? _bridgeProbeTimer;
    private int _bridgeProbeNonce = (Environment.TickCount & 0x3FFF) + 1; // 1..16384, never 0
    private int _bridgeProbeAttempts;
    private bool _bridgeProbeAwaitingRead;
    private bool _bridgeProbeWasDisconnected = true;
    private bool _bridgeProbeRebound;

    private void BridgeProbeTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Gate on FULL detection, not just the socket: a probe started while the
            // sim is still in the menu burns its attempts against an empty world
            // (calc writes no-op without an aircraft) and falsely logs "gave up" —
            // observed live 2026-06-12 (all 40 attempts spent before the A320 loaded).
            // Treating not-fully-connected like disconnected also re-arms the probe
            // on every aircraft detection / swap.
            if (simConnectManager == null || !simConnectManager.IsConnected || !simConnectManager.IsFullyConnected)
            {
                _bridgeProbeWasDisconnected = true;
                return;
            }
            if (_bridgeProbeWasDisconnected)
            {
                // Fresh connection/aircraft: re-arm the probe (CalcPathVerified resets on teardown).
                _bridgeProbeWasDisconnected = false;
                _bridgeProbeAttempts = 0;
                _bridgeProbeAwaitingRead = false;
                _bridgeProbeRebound = false;
                simConnectManager.ResetCalcPathProbe();
            }
            if (simConnectManager.CalcPathVerified || simConnectManager.CalcPathProbeConcluded) return;
            // Only the FBW defs register the probe var; other aircraft can never verify —
            // conclude immediately so dotted events route via the legacy transport.
            if (currentAircraft is not (Aircraft.FlyByWireA320Definition or Aircraft.FlyByWireA380Definition))
            {
                simConnectManager.MarkCalcPathProbeConcluded();
                return;
            }

            if (_bridgeProbeAwaitingRead)
            {
                double cached = simConnectManager.GetCachedVariableValue("MSFSBA_BRIDGE_PROBE") ?? 0;
                if ((int)Math.Round(cached) == _bridgeProbeNonce)
                {
                    simConnectManager.MarkCalcPathVerified();
                    return;
                }
                // First mismatch: the probe L:var did not exist when the data
                // definitions were registered at connect, and a def bound to a
                // nonexistent L:var never delivers — re-bind it now that the
                // first write has created the var (live finding 2026-06-12:
                // writes held at the sim while our reads stayed empty).
                if (!_bridgeProbeRebound)
                {
                    _bridgeProbeRebound = true;
                    simConnectManager.RebindVariableDataDefinition("MSFSBA_BRIDGE_PROBE");
                }
                _bridgeProbeNonce = (_bridgeProbeNonce % 16384) + 1; // new nonce each retry
            }
            if (_bridgeProbeAttempts >= 40)
            {
                // All 40 writes have been issued and attempt 40's read-back (above) just
                // failed — give up: module absent or data-def read failing.
                simConnectManager.MarkCalcPathProbeConcluded();
                return;
            }
            _bridgeProbeAttempts++;
            simConnectManager.ExecuteCalculatorCode($"{_bridgeProbeNonce} (>L:MSFSBA_BRIDGE_PROBE)");
            simConnectManager.RequestVariable("MSFSBA_BRIDGE_PROBE", forceUpdate: true);
            _bridgeProbeAwaitingRead = true;
        }
        catch { /* a probe fault must never disturb the app */ }
    }

    // DIAGNOSTIC (debug/landing-rollout-instrumentation): one-shot flag for
    // logging the first TAXI_GUIDANCE_POSITION event we receive while taxi
    // guidance is in LandingRollout state. Reset on every transition out of
    // LandingRollout so a subsequent landing gets fresh instrumentation.
    private bool _diagLoggedFirstRolloutPos;

    // Event batching infrastructure for high-volume variable updates
    // Producer-consumer pattern: SimConnect thread produces → UI timer consumes
    private readonly ConcurrentQueue<SimVarUpdateEventArgs> eventQueue = new ConcurrentQueue<SimVarUpdateEventArgs>();
    private System.Windows.Forms.Timer? eventBatchTimer;
    private int queuedEventCount = 0;  // Track queue size (ConcurrentQueue.Count is expensive)
    private int droppedEventCount = 0;  // Diagnostic: count dropped events due to queue overflow

    // Panel loading debounce timer (prevents NVDA overload during rapid arrow navigation)
    private System.Windows.Forms.Timer? _panelLoadTimer;
    private string? _pendingPanelLoad = null;  // Track which panel to load when timer fires

    // Nearest city announcement timer (periodic automatic announcements)
    private System.Windows.Forms.Timer? nearestCityAnnouncementTimer;

    // Weather auto-announcement timer
    private System.Windows.Forms.Timer? weatherAnnouncementTimer;
    private double _prevPrecipState = -1;

    // Periodic auto-refresh for the currently-shown Status Display box. SD-page content
    // (FOB, engine N1/N2, fuel per-tank, etc.) is an OnRequest snapshot — without this it
    // freezes at whatever it read when the panel opened and never reflects live changes.
    // While a panel with a "_REFRESH_" button is shown, this ticks every few seconds and
    // (a) rebuilds any snapshot SD content via OnDisplayPanelShown and (b) re-pulls the
    // panel's OnRequest display vars — silently (the "Loading..." placeholder only shows on
    // the first empty populate, so the box updates in place with no flash).
    private System.Windows.Forms.Timer? _sdAutoRefreshTimer;
    private double _prevPrecipRate = -1;
    private double _prevInCloud = -1;
    private double _prevVisibility = -1;      // meters; -1 = uninitialized
    private bool _prevVisLow = false;         // was visibility below 1500m last check
    private readonly HashSet<string> _announcedSigmetKeys = new HashSet<string>();
    private readonly HashSet<string> _announcedPirepKeys  = new HashSet<string>();
    private DateTime _sigmetKeysClearedAt = DateTime.MinValue;

    // Current state
    private string currentSection = "";
    private string currentPanel = "";
    private Dictionary<string, Control> currentControls = new Dictionary<string, Control>();
    private Dictionary<string, double> currentSimVarValues = new Dictionary<string, double>();
    private bool updatingFromSim = false;
    // Set true for the entire duration of panel-build code (PanelLoadTimer_Tick body, including
    // its BeginInvoke continuation). All combo selection-change handlers gate writes on this
    // being false. This blocks ANY phantom user-action fire that originates from panel
    // construction — including the WinForms deferred handle-creation replay that surfaces a
    // buffered SelectedIndex value through the SIC handler regardless of how it was set.
    private bool _buildingPanel = false;
    private Dictionary<string, double> displayValues = new Dictionary<string, double>();  // Store display values
    private Dictionary<string, TaskCompletionSource<bool>>? pendingDisplayRequests = null;  // Track pending display requests
    // The control to return focus to after a status-box refresh (F5). The async refresh
    // moves focus onto the Refresh button; the F5 handler captures the status box here so
    // refreshButton.Click can restore it — otherwise the blind user "lands elsewhere".
    private Control? _refreshFocusReturn = null;
    private ConcurrentDictionary<string, bool> pendingStateAnnouncements = new ConcurrentDictionary<string, bool>();  // Track state announcement requests
    private IAircraftDefinition currentAircraft;
    private Dictionary<string, string>? _pmdgFieldToKeyMap;

    public MainForm()
    {
        // Load last selected aircraft from settings
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        currentAircraft = LoadAircraftFromCode(settings.LastAircraft ?? "A320");

        // Wire distance formatter to the active settings unit.
        MSFSBlindAssist.Services.DistanceFormatter.UnitProvider =
            () => MSFSBlindAssist.Settings.SettingsManager.Current.GroundDistanceUnit;

        InitializeComponent();
        InitializeManagers();

        // Set up form after load
        this.Load += MainForm_Load;
    }

    private IAircraftDefinition LoadAircraftFromCode(string aircraftCode)
    {
        return aircraftCode switch
        {
            "A320" => new FlyByWireA320Definition(),
            "FENIX_A320CEO" => new FenixA320Definition(),
            "PMDG_777" => new PMDG777Definition(),
            "FBW_A380" => new FlyByWireA380Definition(),
            "PMDG_737" => new PMDG737Definition(),
            "HS_787" => new HorizonSim787Definition(),
            // Future aircraft will be added here
            _ => new FlyByWireA320Definition() // Default to A320
        };
    }
    
    private void MainForm_Load(object? sender, EventArgs e)
    {
        // Set window title
        this.Text = "MSFS Blind Assist";

        // Initial menu visibility for aircraft-conditional items (e.g., the
        // PMDG-only FMC Settings entry). Subsequent aircraft swaps re-call
        // UpdateAircraftSpecificMenuItems via SwitchAircraft.
        UpdateAircraftSpecificMenuItems();

        // Start the PROG-page monitor if the current aircraft is PMDG and
        // Enhanced distance mode is on. No-op for other configurations.
        EnsurePMDGProgPageMonitor();

        // Populate sections dynamically from aircraft definition
        foreach (var section in currentAircraft.GetPanelStructure().Keys)
        {
            sectionsListBox.Items.Add(section);
        }

        // Sync menu items with the loaded aircraft (fixes first-launch menu mismatch)
        UpdateAircraftMenuItems();

        // The FBW A380X MFD/MCDU, flyPad and ND OANS are read live through the
        // MSFS Coherent GT debugger (127.0.0.1:19999). Start the MFD client now so
        // it is connected by the time the user opens the MCDU.
        if (currentAircraft?.AircraftCode == "FBW_A380")
        {
            coherentClient = new CoherentDebuggerClient();
            coherentClient.Start();
            coherentClient.SetActive(false);   // connect + install agent now; scrape only while the MCDU window is open
            StartA380EWDMonitor();
        }

        // FBW flyPad: the EFB form owns its CDP client; nothing to pre-start here.

        // The HS787 CDU + EFB open their own Coherent connections on demand (from their forms).
        // The IRS-alignment monitor must run continuously from load so it catches the alignment
        // countdown, so start it here.
        if (currentAircraft?.AircraftCode == "HS_787")
            StartHS787IrsMonitor();

        // Don't set focus - let default tab order handle it for proper menu accessibility
    }

    private void InitializeManagers()
    {
        announcer = new ScreenReaderAnnouncer(this.Handle);

        // Note: Diagnostic test removed to prevent test speech on startup
        // Uncomment the next lines if you need to troubleshoot screen reader connections:
        // System.Diagnostics.Debug.WriteLine("[MainForm] Running initial screen reader diagnostic test");
        // announcer.TestScreenReaderConnection();

        simConnectManager = new SimConnectManager(this.Handle);
        simConnectManager.CurrentAircraft = currentAircraft;
        simConnectManager.ConnectionStatusChanged += OnConnectionStatusChanged;
        simConnectManager.SimulatorVersionDetected += OnSimulatorVersionDetected;
        simConnectManager.SimVarUpdated += OnSimVarUpdated;
        simConnectManager.TakeoffRunwayReferenceSet += OnTakeoffRunwayReferenceSet;
        simConnectManager.AircraftIcaoTypeDetected += OnAircraftIcaoTypeDetected;

        // Warm the GSX door-offset map in the background so docking sessions have
        // offsets ready without blocking the UI thread for the ~12 s scan.
        System.Threading.Tasks.Task.Run(() =>
        {
            try { _gsxAirplaneProfile.GetDoorOffsetMetres("B77W"); } // warms the map
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainForm] GsxAirplaneProfile warm failed: {ex.Message}"); }
        });

        // MobiFlight end-to-end bridge probe: calc-write a nonce L:var, read it back
        // over the data-def channel; a match proves the WASM executed our RPN (the
        // only valid presence signal — the response side can be silent on healthy
        // installs). FBW defs register the probe var.
        _bridgeProbeTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _bridgeProbeTimer.Tick += BridgeProbeTimer_Tick;
        _bridgeProbeTimer.Start();

        // Access GSX integration — separate SimConnect client (WM_USER 0x0403),
        // routed alongside the main client in WndProc. Started on connect and
        // stopped on disconnect; tolerates GSX not being installed (the
        // service logs and exposes a status string for the form to bind to).
        _gsxService = new GsxService(this.Handle, announcer);
        _gsxService.AnnounceWhenFormHidden =
            MSFSBlindAssist.Settings.SettingsManager.Current.GsxBackgroundMonitoring;

        simVarMonitor = new SimVarMonitor();
        simVarMonitor.ValueChanged += OnSimVarValueChanged;

        hotkeyManager = new HotkeyManager();
        hotkeyManager.Initialize(this.Handle); // Initialize with window handle
        hotkeyManager.HotkeyTriggered += OnHotkeyTriggered;
        hotkeyManager.OutputHotkeyModeChanged += OnOutputHotkeyModeChanged;
        hotkeyManager.InputHotkeyModeChanged += OnInputHotkeyModeChanged;

        // Initialize takeoff assist manager
        var takeoffSettings = MSFSBlindAssist.Settings.SettingsManager.Current;
        takeoffAssistManager = new TakeoffAssistManager(announcer,
            takeoffSettings.TakeoffAssistToneWaveform, takeoffSettings.TakeoffAssistToneVolume,
            takeoffSettings.TakeoffAssistMuteCenterlineAnnouncements,
            takeoffSettings.TakeoffAssistInvertPanning,
            takeoffSettings.TakeoffAssistHeadingToneThreshold, takeoffSettings.TakeoffAssistLegacyMode,
            takeoffSettings.TakeoffAssistEnableCallouts);
        takeoffAssistManager.TakeoffAssistActiveChanged += OnTakeoffAssistActiveChanged;

        // Initialize hand fly manager
        handFlyManager = new HandFlyManager(announcer);
        handFlyManager.HandFlyModeActiveChanged += OnHandFlyModeActiveChanged;

        // Initialize visual guidance manager
        visualGuidanceManager = new VisualGuidanceManager(announcer);
        visualGuidanceManager.VisualGuidanceActiveChanged += OnVisualGuidanceActiveChanged;

        // Global ground-speed announcer — fed by the always-on GROUND_VELOCITY continuous
        // variable, so callouts work in every phase (takeoff roll, landing rollout, taxi),
        // not just while taxi guidance is active.
        groundSpeedAnnouncer = new MSFSBlindAssist.Services.GroundSpeedAnnouncer(announcer);
        // Captures the last landing's touchdown rate + peak g (the ReadLastLandingRate /
        // ReadLastLandingPeakG output hotkeys). Fed by the always-on G FORCE var.
        landingRateAnnouncer = new MSFSBlindAssist.Services.LandingRateAnnouncer();
        // 1,000-foot crossing callouts, fed by the always-on INDICATED ALTITUDE var.
        altitudeCalloutAnnouncer = new MSFSBlindAssist.Services.AltitudeCalloutAnnouncer(announcer);

        // Initialize taxi guidance manager
        taxiGuidanceManager = new TaxiGuidanceManager(announcer);

        // Initialize docking guidance manager
        dockingGuidanceManager = new DockingGuidanceManager(announcer);

        // When docking reaches the precise GSX stop ("GSX docking complete."), stop taxi
        // guidance so the whole flow ends cleanly instead of taxi sitting in LiningUp forever.
        // Raised on the SimConnect position thread; StopGuidance is thread-safe + silent (docking
        // already announced the stop), so no marshalling and no contradictory second callout.
        dockingGuidanceManager.DockingCompleted += () => taxiGuidanceManager.StopGuidance();

        // Subscribe to taxi guidance state changes ONCE, here at construction time.
        // This wires SimConnect taxi-position monitoring on/off (see
        // OnTaxiGuidanceStateChanged). Previously the subscription only happened
        // inside OpenTaxiForm, which meant the Landing Exit Planner flow
        // (Shift+X → auto-activate on touchdown) had a silent state machine: the
        // route loaded, the state advanced, but no SimConnect position feed
        // ever started. Subscribing here ensures every entry point — manual
        // taxi form, landing-exit auto-activation, future entry points — gets
        // monitoring wired up automatically.
        taxiGuidanceManager.StateChanged += OnTaxiGuidanceStateChanged;
        taxiGuidanceManager.RequestTakeoffAssistAutoActivate += OnTaxiGuidanceRequestTakeoffAssistAutoActivate;

        // Landing exit planner — watches for touchdown and auto-activates taxi guidance
        // to the pre-selected exit taxiway. Opens via MainForm menu / hotkey.
        landingExitPlanner = new LandingExitPlanner(announcer, taxiGuidanceManager);

        // Ground traffic monitor — proximity alerts for on-ground AI/multiplayer traffic.
        // Starts its own 3-second poll timer; gates on LastKnownOnGround each tick.
        groundTrafficMonitor = new GroundTrafficMonitor(announcer, simConnectManager);
        // Suppress traffic auto-alerts in three contexts: during takeoff roll
        // (pilot's hands are on rudder + throttle, can't act on a callout),
        // when Taxi Guidance is not engaged (no route loaded / pre-pushback
        // / post-stop), and during the landing rollout (hands on brakes +
        // rudder, and the exit/runway-end callouts must not be talked over).
        // Hotkey summary (Alt+G) remains available in all cases because it
        // lives outside this poll loop.
        groundTrafficMonitor.SuppressCheck = () =>
            takeoffAssistManager.IsActive
            || taxiGuidanceManager.State == TaxiGuidanceState.Inactive
            || taxiGuidanceManager.State == TaxiGuidanceState.LandingRollout;

        // Per-aircraft rollout-anticipation lead for the taxi steering tone
        // (see IAircraftDefinition.TaxiTurnLeadSeconds).
        taxiGuidanceManager.TurnLeadSeconds = currentAircraft.TaxiTurnLeadSeconds;

        // Initialize airport database provider (optional - can be null if database not built yet)
        airportDataProvider = DatabaseSelector.SelectProvider();

        // Wrap with the taxi-data augmentation decorator (Phase 5).
        // The decorator is transparent: all IAirportDataProvider calls delegate to the base
        // except GetTaxiPaths, which enriches unnamed segments from OSM / X-Plane apt.dat.
        // Only wrap when a base provider is available — no DB means no decoration needed.
        if (airportDataProvider != null)
        {
            var http = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(60) };
            var sources = new System.Collections.Generic.List<MSFSBlindAssist.Services.TaxiAugment.ITaxiDataSource>
            {
                new MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource(http),
                new MSFSBlindAssist.Services.TaxiAugment.XplaneAptDatSource(http),
            };
            var mergeOpt = new MSFSBlindAssist.Services.TaxiAugment.MergeOptions();

            // IN-MEMORY cache only — nothing written to the user's disk. It just holds an async
            // fetch's result for the route build that follows + avoids re-fetching one airport
            // repeatedly in a session. Everything is real-time: the active flight's dep/dest are
            // force-refreshed, and the cache is gone on exit. (7-day TTL is moot in-session.)
            var augCache  = new MSFSBlindAssist.Services.TaxiAugment.TaxiDataCache(ttlDays: 7);
            var decorator = new MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider(
                airportDataProvider, augCache, sources, mergeOpt);

            // Phase 8: honour the user's on/off setting.
            decorator.Enabled = MSFSBlindAssist.Settings.SettingsManager.Current.TaxiAugmentEnabled;

            decorator.AirportDataUpdated += icao =>
            {
                // Real-time: drop any cached graph built from the older (pre-augmentation) data so
                // Where-Am-I and friends pick up the fresh names on next use — no manual refresh.
                taxiGuidanceManager?.OnAirportDataUpdated(icao);

                string logLine = $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  taxi-augment: data updated for {icao}{System.Environment.NewLine}";
                try { System.IO.File.AppendAllText(MSFSBlindAssist.Utils.AppLogs.PathFor("taxi-augment.log"), logLine); }
                catch { /* log failure must never surface */ }
            };

            _augmentingProvider = decorator;
            airportDataProvider = decorator;
        }

        // Initialize flight plan manager with navigation database
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
        flightPlanManager = new MSFSBlindAssist.Navigation.FlightPlanManager(navigationDatabasePath, airportDataProvider);

        // Initialize waypoint tracker
        waypointTracker = new MSFSBlindAssist.Navigation.WaypointTracker();

        // Initialize TCAS service (polls for AI/multiplayer traffic via SimConnect)
        tcasService = new MSFSBlindAssist.Services.TcasService(simConnectManager);

        // ActiveSky weather-update announcer. Started unconditionally — when
        // AS isn't running each poll is just a ~1.2 s parallel-probe timeout
        // and nothing is announced. When AS IS running and pushes new
        // weather, the user hears "Weather update. Surface wind X at Y …"
        // within ~1 minute. Silent for non-AS users.
        activeSkyWeatherMonitor = new MSFSBlindAssist.Services.ActiveSkyWeatherMonitor(
            new MSFSBlindAssist.Services.ActiveSkyClient(), announcer);
        activeSkyWeatherMonitor.IntervalMinutes =
            MSFSBlindAssist.Settings.SettingsManager.Current.WeatherAutoAnnounceIntervalMinutes;
        activeSkyWeatherMonitor.Start();

        // Initialize event batching timer for high-volume variable updates
        // Timer runs on UI thread, draining the event queue in controlled batches
        eventBatchTimer = new System.Windows.Forms.Timer();
        eventBatchTimer.Interval = EVENT_BATCH_INTERVAL_MS;
        eventBatchTimer.Tick += ProcessEventBatch;
        // Timer starts when SimConnect connects (see OnConnectionStatusChanged)
        System.Diagnostics.Debug.WriteLine($"[MainForm] Event batching initialized: {EVENT_BATCH_INTERVAL_MS}ms interval, max {MAX_BATCH_SIZE} events/batch");

        // Initialize panel loading debounce timer (prevents NVDA overload during rapid arrow navigation)
        _panelLoadTimer = new System.Windows.Forms.Timer();
        _panelLoadTimer.Interval = 150; // 150ms delay - allows rapid navigation while preventing event queue buildup
        _panelLoadTimer.Tick += PanelLoadTimer_Tick;
        System.Diagnostics.Debug.WriteLine("[MainForm] Panel load debouncing initialized: 150ms delay");

        // Initialize nearest city announcement timer (periodic automatic announcements)
        nearestCityAnnouncementTimer = new System.Windows.Forms.Timer();
        nearestCityAnnouncementTimer.Tick += NearestCityAnnouncementTimer_Tick;
        // Timer interval and start/stop handled by settings and connection status

        // Initialize weather auto-announcement timer (30 second interval)
        weatherAnnouncementTimer = new System.Windows.Forms.Timer();
        weatherAnnouncementTimer.Interval = 30000;
        weatherAnnouncementTimer.Tick += WeatherAnnouncementTimer_Tick;

        // Update status bar with database info
        UpdateDatabaseStatusDisplay();

        // Connect after a delay
        System.Windows.Forms.Timer connectTimer = new System.Windows.Forms.Timer();
        connectTimer.Interval = 2000;
        connectTimer.Tick += (s, e) =>
        {
            connectTimer.Stop();
            connectTimer.Dispose();
            announcer.Announce("Initializing, please wait");
            simConnectManager.Connect();
        };
        connectTimer.Start();
    }

    private void OnSimulatorVersionDetected(object? sender, string version)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnSimulatorVersionDetected(sender, version)));
            return;
        }

        // Announce the detected simulator version
        announcer.Announce(version);
    }

    private void OnConnectionStatusChanged(object? sender, string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnConnectionStatusChanged(sender, status)));
            return;
        }

        statusLabel.Text = status;

        if (status.StartsWith("Connected to"))
        {
            // Start event batching timer for high-volume variable updates
            eventBatchTimer?.Start();
            System.Diagnostics.Debug.WriteLine("[MainForm] Event batching timer started");

            announcer.Announce(status);
            announcer.Announce($"{currentAircraft.AircraftName} Profile and panels active");

            // Start the Access GSX service alongside the main SimConnect
            // client. Safe to call repeatedly — it no-ops if already open.
            try { _gsxService?.Start(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainForm] GsxService.Start failed: {ex.Message}");
            }

            // After SimConnect connects, if current aircraft is a PMDG type, initialize data manager.
            // Use IPMDGAircraft (not == "PMDG_777") so the 737 NG3 is initialized too.
            if (currentAircraft is IPMDGAircraft)
            {
                simConnectManager.InitializePMDG(currentAircraft);
                if (simConnectManager.PMDGDataManager != null)
                {
                    simConnectManager.PMDGDataManager.VariableChanged += OnPMDGVariableChanged;
                }
                // Dispose any existing PROG monitor — it holds a reference
                // to the previous data-manager instance (which is now
                // disposed if this is a reconnect, or never existed if
                // this is the first connect). EnsurePMDGProgPageMonitor
                // will recreate against the fresh data manager.
                if (pmdgProgPageMonitor != null)
                {
                    pmdgProgPageMonitor.Dispose();
                    pmdgProgPageMonitor = null;
                }
                EnsurePMDGProgPageMonitor();
            }

            // Notify First Officer form so it can re-wire its data manager reference
            if (pmdg777FirstOfficerForm != null && !pmdg777FirstOfficerForm.IsDisposed)
                pmdg777FirstOfficerForm.OnSimConnectChanged();
            if (pmdg737FirstOfficerForm != null && !pmdg737FirstOfficerForm.IsDisposed)
                pmdg737FirstOfficerForm.OnSimConnectChanged();

            // Automatically switch database if simulator version doesn't match
            CheckAndSwitchDatabase();

            // Request all current values when connected
            RequestAllCurrentValues();

            // Start a grace period before enabling continuous variable announcements
            // This prevents initial ECAM messages and other variables from being announced
            // when connecting to a cold and dark aircraft. Also mute the announcer's
            // automatic paths so aircraft-specific ProcessSimVarUpdate branches (which
            // announce directly, bypassing simVarMonitor) stay silent on first detect —
            // e.g. the A380 altimeter setting. User hotkeys (AnnounceImmediate) still talk.
            // GATED TO THE A380: this extra announcer-level mute was added for the A380's
            // direct-announce branches; other aircraft keep their prior behaviour (the
            // simVarMonitor + ECAM grace below already applies to every aircraft).
            if (announcer != null && currentAircraft?.AircraftCode == "FBW_A380") announcer.Suppressed = true;
            System.Windows.Forms.Timer announcementGracePeriodTimer = new System.Windows.Forms.Timer();
            announcementGracePeriodTimer.Interval = 5000; // 5 second grace period
            announcementGracePeriodTimer.Tick += (s, e) =>
            {
                announcementGracePeriodTimer.Stop();
                announcementGracePeriodTimer.Dispose();
                if (announcer != null) announcer.Suppressed = false;
                simVarMonitor.EnableAnnouncements();
                simConnectManager.EnableECAMAnnouncements();
            };
            announcementGracePeriodTimer.Start();

            // Start nearest city announcement timer if enabled in settings
            StartNearestCityAnnouncementTimer();

            // Start weather auto-announcement timer
            _prevPrecipState = -1;
            _prevPrecipRate = -1;
            _prevInCloud = -1;
            _prevVisibility = -1;
            _prevVisLow = false;
            _announcedSigmetKeys.Clear();
            _announcedPirepKeys.Clear();
            _sigmetKeysClearedAt = DateTime.UtcNow;
            weatherAnnouncementTimer?.Start();
        }
        else if (status.Contains("Disconnected"))
        {
            // Stop event batching timer and clear queue
            eventBatchTimer?.Stop();

            // Stop nearest city announcement timer
            nearestCityAnnouncementTimer?.Stop();

            // Stop weather auto-announcement timer
            weatherAnnouncementTimer?.Stop();

            // Clear event queue and reset counters
            while (eventQueue.TryDequeue(out _)) { }
            queuedEventCount = 0;
            droppedEventCount = 0;
            System.Diagnostics.Debug.WriteLine("[MainForm] Event batching timer stopped, queue cleared");

            announcer.Announce(status);
            // Reset window title when disconnected
            this.Text = "MSFS Blind Assist";
            // Disable announcements when disconnected
            simVarMonitor.Reset();
            // Reset ECAM suppression flag for next connection
            simConnectManager.SuppressECAMAnnouncements = true;

            // Stop the GSX SimConnect client so we don't leak it across
            // reconnects. Start() will be called again on the next connect.
            try { _gsxService?.Stop(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainForm] GsxService.Stop failed: {ex.Message}");
            }
        }
    }

    // --- User-set auto-announce de-dup (GLOBAL, all aircraft + all combo types) ---
    // When the user operates a panel combo, the screen reader already speaks the new
    // value. The same change ALSO comes back through OnSimVarUpdated and would be
    // auto-announced a second time by the monitor (per the "announce every state change"
    // rule). We record the var+value the user just committed, then suppress exactly that
    // echo once (updating the monitor baseline silently). A change to the SAME var from
    // ANY OTHER source (flyPad, ground crew, failure, systems-host) still announces,
    // because only the matching value within the short window is consumed.
    private readonly Dictionary<string, (double value, long tick)> _uiSetEcho = new();
    private const int UiSetEchoSuppressMs = 1500;
    private void MarkUiSet(string? varName, double value)
    {
        if (!string.IsNullOrEmpty(varName)) _uiSetEcho[varName] = (value, Environment.TickCount64);
    }

    /// <summary>
    /// Called when SimConnect resolves the loaded aircraft's ICAO type designator.
    /// Looks up the aircraft's gsx.cfg geometry and feeds the preferred-door SIDE to the
    /// docking guidance manager (the "jetway on your left/right" cue). The longitudinal
    /// door offset is deliberately NOT plumbed anywhere — the stop math aligns the aircraft
    /// DATUM to the stop position (see the comment block in DockingGuidanceManager); the
    /// offset's only former consumer was a telemetry column.
    /// </summary>
    private void OnAircraftIcaoTypeDetected(object? sender, string icaoType)
    {
        // Run on a background thread so neither the UI thread nor the SimConnect
        // thread is blocked by the GsxAirplaneProfile disk scan (~12 s on first call).
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var geom = _gsxAirplaneProfile.GetGeometry(icaoType);
                bool shouldRefresh;
                if (geom == null && GsxLikelyInstalled())
                {
                    // _refreshedIcaos is mutated from concurrently-running Task.Run handlers
                    // (the event fires on connect, on every AircraftLoaded, and again from the
                    // aircraft.cfg catalog fallback) — HashSet is not thread-safe, so lock it.
                    lock (_refreshedIcaos) { shouldRefresh = _refreshedIcaos.Add(icaoType ?? ""); }
                }
                else shouldRefresh = false;

                if (shouldRefresh)
                {
                    // First miss for this ICAO — rebuild the map in case a gsx.cfg was
                    // written after startup (e.g. user just installed a GSX profile via
                    // the GSX installer into %APPDATA%\Virtuali\Airplanes). Only useful
                    // when GSX is actually installed; skip the disk scan otherwise.
                    _gsxAirplaneProfile.Refresh();
                    geom = _gsxAirplaneProfile.GetGeometry(icaoType);
                }

                // Stale-task guard: the Refresh() scan can take seconds; if the user swapped
                // aircraft meanwhile, a late-finishing task for the OLD ICAO must not clobber
                // the NEW aircraft's door side (the spoken "jetway on your left/right" cue).
                // Mirrors the title-snapshot recheck in SimConnectManager's catalog fallback.
                if (!string.Equals(simConnectManager.CurrentAircraftIcaoType, icaoType, StringComparison.OrdinalIgnoreCase))
                    return;

                string side = geom?.Side switch
                {
                    MSFSBlindAssist.Services.Gsx.DoorSide.Left => "left",
                    MSFSBlindAssist.Services.Gsx.DoorSide.Right => "right",
                    _ => ""
                };
                dockingGuidanceManager.SetDoorSide(side);
                System.Diagnostics.Debug.WriteLine($"[MainForm] Door side for ICAO '{icaoType}': '{side}'");

                try
                {
                    string logPath = MSFSBlindAssist.Utils.AppLogs.PathFor("docking-aircraft.log");
                    System.IO.File.AppendAllText(logPath,
                        $"{DateTime.Now:HH:mm:ss}  ICAO=\"{icaoType}\"  doorSide={side}{System.Environment.NewLine}");
                }
                catch { /* never propagate log failures */ }
            }
            catch { }
        });
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (InvokeRequired)
        {
            // PRODUCER: Enqueue event for batch processing instead of immediate BeginInvoke
            // This reduces UI thread marshaling overhead by ~95% for high-volume updates (400+ vars/sec)
            if (Interlocked.Increment(ref queuedEventCount) <= MAX_QUEUE_SIZE)
            {
                eventQueue.Enqueue(e);
            }
            else
            {
                // Queue full - drop event and track for diagnostics
                Interlocked.Decrement(ref queuedEventCount);
                Interlocked.Increment(ref droppedEventCount);

                // Log overflow warning (throttled to prevent log spam)
                if (droppedEventCount % 100 == 1)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainForm] WARNING: Event queue overflow! Dropped {droppedEventCount} events. Consider increasing MAX_QUEUE_SIZE or reducing variable count.");
                }
            }
            return;
        }

        // CONSUMER: Process event on UI thread (called from ProcessEventBatch)
        // Step 1: ALWAYS store the value first (needed by all consumers)
        currentSimVarValues[e.VarName] = e.Value;

        // Initial-snapshot fast path: populate caches and refresh UI controls
        // but skip all announcement paths. These events represent "what the
        // cockpit looked like when the app started", not user-triggered
        // transitions, so announcing them would spam the user on every launch.
        if (e.IsInitialSnapshot)
        {
            UpdateControlFromSimVar(e.VarName, e.Value);
            // Also mirror to displayValues so panel display textboxes have
            // the right initial content when first rendered.
            if (currentAircraft.GetVariables().ContainsKey(e.VarName) &&
                currentAircraft.GetPanelDisplayVariables().Values.Any(list => list.Contains(e.VarName)))
            {
                displayValues[e.VarName] = e.Value;
            }
            return;
        }

        // FBW A380 engine-mode-selector watchdog: the cockpit ENG START knob only fans
        // ignition to engines 1+2 on builds whose template defaults ENGINE_COUNT=2 (the
        // A320 inheritance), so engines 3+4 motor but never light. The knob updates
        // XMLVAR_ENG_MODE_SEL (monitored as ENGINE_MODE_SELECTOR); mirror its position onto
        // engines 3+4 via TURBINE_IGNITION_SWITCH_SET3/4 (live-verified to address + light
        // the outboard engines). Keys on the selector var only → no feedback loop; harmless
        // when MSFSBA's own Engine Mode Selector combo is used (it already fires SET1-4).
        if (currentAircraft?.AircraftCode == "FBW_A380" && e.VarName == "ENGINE_MODE_SELECTOR")
        {
            int igPos = (int)Math.Round(e.Value);
            if (igPos >= 0 && igPos <= 2)
            {
                simConnectManager?.ExecuteCalculatorCode($"{igPos} (>K:TURBINE_IGNITION_SWITCH_SET3)");
                simConnectManager?.ExecuteCalculatorCode($"{igPos} (>K:TURBINE_IGNITION_SWITCH_SET4)");
            }
            // Fall through so ENGINE_MODE_SELECTOR still auto-announces its position.
        }

        // FBW A380 STD-flag watchdog: in STD the FCU forces the stock altimeter to
        // exactly 1013.25 hPa, but its KOHLSMAN SETTING STD write is TRANSITION-only —
        // a session that starts with STD already engaged reads a stale 0 (observed
        // live 2026-06-11), which mis-read the Altimeter STD combo/hotkey. When the
        // MB mirror sits at the STD constant for >2 s with the flag still 0, back-fill
        // the flag (everything keys on it). ONE direction only (0→1): the FCU's
        // exit-write is same-tick reliable, and correcting 1→0 could fight a
        // mid-transition frame. The def suppresses these vars' announcements.
        if (currentAircraft?.AircraftCode == "FBW_A380" &&
            (e.VarName == "BARO_MB_WATCH_L" || e.VarName == "BARO_MB_WATCH_R"))
        {
            bool baroCapt = e.VarName == "BARO_MB_WATCH_L";
            bool atStdConstant = Math.Abs(e.Value - 1013.25) < 0.02;
            double? stdFlag = simConnectManager?.GetCachedVariableValue(
                baroCapt ? "A32NX_FCU_LEFT_EIS_BARO_IS_STD" : "A32NX_FCU_RIGHT_EIS_BARO_IS_STD");
            if (atStdConstant && (stdFlag ?? 0) < 0.5)
            {
                var nowUtc = DateTime.UtcNow;
                var since = baroCapt ? _a380BaroStdMismatchL : _a380BaroStdMismatchR;
                if (since == DateTime.MinValue)
                {
                    if (baroCapt) _a380BaroStdMismatchL = nowUtc; else _a380BaroStdMismatchR = nowUtc;
                }
                else if ((nowUtc - since).TotalSeconds > 2)
                {
                    simConnectManager?.ExecuteCalculatorCode($"1 (>A:KOHLSMAN SETTING STD:{(baroCapt ? 1 : 2)}, Bool)");
                    if (baroCapt) _a380BaroStdMismatchL = DateTime.MinValue; else _a380BaroStdMismatchR = DateTime.MinValue;
                }
            }
            else
            {
                if (baroCapt) _a380BaroStdMismatchL = DateTime.MinValue; else _a380BaroStdMismatchR = DateTime.MinValue;
            }
            // Fall through; the def's ProcessSimVarUpdate returns true for these keys.
        }

        // Step 2: Handle special one-off announcements (terminal cases only)
        if (HandleSpecialAnnouncements(e))
        {
            return; // These are terminal - no further processing needed
        }

        // Step 2.5: Allow aircraft-specific variable processing (e.g., FCU display combining)
        // This lets each aircraft handle complex variables before generic processing.
        //
        // The HS787 auto-announces ~100 of its vars from INSIDE ProcessSimVarUpdate, which returns
        // true and exits this method (line below) BEFORE reaching either the generic disabled-monitor
        // gate OR the generic _uiSetEcho gate further down. So two suppressions that work for every
        // other aircraft (which announce on the generic path) silently never fire on the 787:
        //   (1) monitor-manager mute (Ctrl+M),
        //   (2) UI-set echo — don't re-announce a value the user JUST set via a combo (the screen
        //       reader already spoke the combo selection).
        // Both are fixed here the same way: ProcessSimVarUpdate auto-announces ONLY via the
        // suppressible announcer.Announce(...) (its AnnounceImmediate calls are all hotkey readouts),
        // so suppress the announcer just for this var's processing — the per-branch state/baseline
        // updates still run, only the speech is dropped.
        bool hs787 = currentAircraft!.AircraftCode == "HS_787";
        bool hs787Muted = hs787 &&
            Settings.SettingsManager.Current.HS787DisabledMonitorVariables.Contains(e.VarName);
        // UI-set echo suppression — applies to EVERY aircraft, not just the HS787 (was the bug).
        // A def that auto-announces from INSIDE ProcessSimVarUpdate (the PMDG APU selector + the
        // Boris Audio Works soundpack switches, the HS787, the A380, ...) returns true and exits
        // this method BEFORE the generic _uiSetEcho gate further down ever runs, so without
        // suppressing right here the value the user JUST set via a combo is spoken TWICE: once by
        // the screen reader (the combo selection) and again by the def. Match on the time window
        // ONLY (not the value): a combo set can write a different encoding than the SDK reads back
        // (event position vs struct field, 0/1 vs 0/100), so a value compare silently misses and
        // the double-announce survives. The user just touched THIS control, so any change to it
        // inside the short echo window IS the echo. The generic value-matched gate below still
        // guards the non-def-handled announce path and its own baseline accuracy.
        bool uiEcho = _uiSetEcho.TryGetValue(e.VarName, out var ue)
            && Environment.TickCount64 - ue.tick < UiSetEchoSuppressMs;
        bool suppressDefAnnounce = hs787Muted || uiEcho;
        bool prevSuppressed = announcer.Suppressed;
        if (suppressDefAnnounce) announcer.Suppressed = true;
        bool wasProcessedByAircraft;
        try
        {
            wasProcessedByAircraft = currentAircraft.ProcessSimVarUpdate(e.VarName, e.Value, announcer);
        }
        finally
        {
            if (suppressDefAnnounce) announcer.Suppressed = prevSuppressed;
        }
        if (wasProcessedByAircraft)
        {
            // The def announced (suppressed) and updated its own baseline — consume the echo so a
            // later change from any source still announces. (If the def did NOT handle it, the echo
            // is left intact for the generic _uiSetEcho gate further down.)
            if (uiEcho) _uiSetEcho.Remove(e.VarName);
            // Update window title if flight phase changed (for aircraft that track flight phases)
            if (!string.IsNullOrEmpty(currentAircraft.CurrentFlightPhase))
            {
                this.Text = $"MSFS BA - {currentAircraft.CurrentFlightPhase} phase active";
            }
            // Check StateVariable reverse lookup only (don't call full UpdateControlFromSimVar
            // which can interfere with aircraft-specific processing — we tried it and combo
            // programmatic updates appear to trigger the user-action SIC handler despite the
            // updatingFromSim flag for HS787 vars whose write handler toggles state).
            UpdateButtonStateFromStateVariable(e.VarName, e.Value);
            return; // Aircraft handled it completely, no further generic processing needed
        }

        // Step 3: Update display values (if this variable is used in any panel display)
        // This happens silently without announcements - users read the display manually
        if (currentAircraft.GetVariables().ContainsKey(e.VarName) &&
            currentAircraft.GetPanelDisplayVariables().Values.Any(list => list.Contains(e.VarName)))
        {
            displayValues[e.VarName] = e.Value;

            // Signal completion for pending requests
            if (pendingDisplayRequests != null && pendingDisplayRequests.ContainsKey(e.VarName))
            {
                pendingDisplayRequests[e.VarName].TrySetResult(true);
            }

            // Update display textbox if visible
            if (currentControls.ContainsKey("_DISPLAY_"))
            {
                TextBox? displayBox = currentControls["_DISPLAY_"] as TextBox;
                if (displayBox != null)
                {
                    UpdateDisplayText(displayBox);
                }
            }
            // DON'T return - continue processing for announcements if needed
        }

        // Step 4: Update UI controls (if this variable has a control in current panel)
        UpdateControlFromSimVar(e.VarName, e.Value);

        // Step 5: Handle pending state announcements (button press feedback)
        if (pendingStateAnnouncements.TryRemove(e.VarName, out _))
        {
            AnnounceVariableState(e.VarName, e.Value);
            // DON'T return - might also need continuous monitoring
        }

        // Step 6: Process continuous monitoring for auto-announcements
        // Only announce variables marked with IsAnnounced = true and UpdateFrequency = Continuous
        if (currentAircraft.GetVariables().ContainsKey(e.VarName))
        {
            var varDef = currentAircraft.GetVariables()[e.VarName];
            if (varDef.IsAnnounced && varDef.UpdateFrequency == UpdateFrequency.Continuous)
            {
                // INDICATED_ALTITUDE is continuously monitored only to feed the 1,000-ft
                // crossing announcer (HandleSpecialAnnouncements); never speak it as a raw
                // "Altitude: 5234" through the generic gate. Display/feed already ran above.
                if (e.VarName == "INDICATED_ALTITUDE") return;

                // Check if disabled in Fenix Monitor Manager
                if (currentAircraft.AircraftCode == "FENIX_A320CEO" &&
                    Settings.SettingsManager.Current.FenixDisabledMonitorVariables.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in PMDG Announcement Monitor. AircraftCode
                // for PMDG aircraft starts with "PMDG_" (e.g. "PMDG_777") so
                // a single prefix check covers any future PMDG additions
                // sharing the same disabled-variables list.
                if (currentAircraft.AircraftCode.StartsWith("PMDG_", StringComparison.Ordinal) &&
                    Settings.SettingsManager.Current.PMDGDisabledMonitorVariables.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in the A380 Monitor Manager.
                if (currentAircraft.AircraftCode == "FBW_A380" &&
                    Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in the A32NX Monitor Manager.
                if (currentAircraft.AircraftCode == "A320" &&
                    Settings.SettingsManager.Current.A32NXDisabledMonitorVariables.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in the HS787 Monitor Manager.
                if (currentAircraft.AircraftCode == "HS_787" &&
                    Settings.SettingsManager.Current.HS787DisabledMonitorVariables.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // For PMDG variables, build the description from ValueDescriptions
                // since PMDG events don't carry description strings like SimConnect does
                string description = e.Description;
                if (string.IsNullOrEmpty(description) && varDef.ValueDescriptions.Count > 0)
                {
                    if (varDef.ValueDescriptions.TryGetValue(e.Value, out string? desc))
                        description = $"{varDef.DisplayName}: {desc}";
                    else if (!varDef.OnlyAnnounceValueDescriptionMatches)
                        description = $"{varDef.DisplayName}: {e.Value}";
                }
                else if (string.IsNullOrEmpty(description))
                {
                    description = $"{varDef.DisplayName}: {e.Value}";
                }

                // Generic ARINC429 auto-decode for the announce path (only reached for vars
                // the aircraft's ProcessSimVarUpdate did NOT handle, so existing ad-hoc ARINC
                // announce branches are untouched — no double-decode). Renders the spoken value
                // decoded instead of a raw word.
                if (currentAircraft is BaseAircraftDefinition arincAnnDef &&
                    arincAnnDef.TryDecodeArinc429(e.VarName, e.Value, out string arincSpoken))
                {
                    description = $"{varDef.DisplayName}: {arincSpoken}";
                }

                // Suppress the duplicate echo of a value the user JUST set via the UI (the
                // screen reader already spoke the combo). Update the baseline silently so a
                // later change to this var from any OTHER source still announces. Consumed
                // once; only a value matching what the user set within the window is dropped.
                if (_uiSetEcho.TryGetValue(e.VarName, out var echo)
                    && Math.Abs(echo.value - e.Value) < 0.001
                    && Environment.TickCount64 - echo.tick < UiSetEchoSuppressMs)
                {
                    _uiSetEcho.Remove(e.VarName);
                    simVarMonitor.SetBaseline(e.VarName, e.Value);
                    return;
                }

                simVarMonitor.ProcessUpdate(e.VarName, e.Value, description);
            }
        }
    }

    /// <summary>
    /// CONSUMER: Process batched events from the queue on UI thread.
    /// Called by eventBatchTimer every EVENT_BATCH_INTERVAL_MS (~33ms).
    /// Drains the queue in controlled batches to prevent UI thread freezing.
    /// </summary>
    private void ProcessEventBatch(object? sender, EventArgs e)
    {
        int processedCount = 0;
        int batchStartQueueSize = queuedEventCount;

        // Drain queue in batches (up to MAX_BATCH_SIZE events per timer tick)
        // This prevents UI freezing if queue contains thousands of events
        while (processedCount < MAX_BATCH_SIZE && eventQueue.TryDequeue(out SimVarUpdateEventArgs? eventArgs))
        {
            Interlocked.Decrement(ref queuedEventCount);

            // Call OnSimVarUpdated directly on UI thread (InvokeRequired will be false)
            // This executes the exact same logic as before, just batched instead of individual
            OnSimVarUpdated(this, eventArgs);

            processedCount++;
        }
    }

    /// <summary>
    /// Handles special announcements that should terminate processing.
    /// Returns true if the event was handled and no further processing is needed.
    /// </summary>
    private bool HandleSpecialAnnouncements(SimVarUpdateEventArgs e)
    {
        // NOTE: Aircraft-specific ProcessSimVarUpdate() is now called in the main flow (line 206)
        // to avoid duplicate calls. Flight phase window title updates happen there.

        // 1,000-foot crossing callouts. INDICATED_ALTITUDE is also a panel-display var, so
        // this is a NON-terminal feed (no early return) — processing continues so the
        // display box still updates. The var is registered IsAnnounced=false (per aircraft),
        // so the generic announce gate stays silent and only these callouts speak.
        if (e.VarName == "INDICATED_ALTITUDE")
        {
            altitudeCalloutAnnouncer.ProcessAltitude(e.Value, _lastOnGround);
        }

        // Handle FCU hotkey value announcements
        if (e.VarName == "FCU_HEADING" || e.VarName == "FCU_SPEED" || e.VarName == "FCU_ALTITUDE" ||
            e.VarName == "FCU_HEADING_WITH_STATUS" || e.VarName == "FCU_SPEED_WITH_STATUS" ||
            e.VarName == "FCU_ALTITUDE_WITH_STATUS" || e.VarName == "FCU_VSFPA_VALUE")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // Ground-speed announcer. GROUND_VELOCITY is a continuous base variable (always
        // monitored while connected). Route it to the dedicated announcer's bucket/hysteresis
        // logic and return true so the generic "value changed" announcement is suppressed.
        // The announcer self-gates on the interval setting AND on the on-ground state
        // (_lastOnGround, cached from SIM_ON_GROUND) — GS callouts are on-ground only.
        if (e.VarName == "GROUND_VELOCITY")
        {
            groundSpeedAnnouncer.ProcessGroundSpeed(e.Value, _lastOnGround, takeoffAssistManager.IsActive);
            // Taxiway-name augmentation is fetched ONLY for the active flight's departure and
            // destination (the airports you actually taxi at, both force-fresh) plus on demand when
            // you type an ICAO into the gate-teleport dialog. The old 50 NM geofence scan was removed
            // — it added background fetching for airports you never taxi at, with no benefit.
            return true;
        }

        // Feed g-force to the landing-rate tracker so it can capture the peak touchdown g
        // inside the post-touchdown window (the ReadLastLandingPeakG hotkey). Not announced.
        if (e.VarName == "G_FORCE")
        {
            landingRateAnnouncer.ProcessG(e.Value);
            return true;
        }

        // Touchdown vertical speed is monitored only so the ReadLastLandingRate hotkey can
        // read it from the cache (it's latched by the sim at touchdown). It must never be
        // spoken as a generic "value changed" call-out — swallow it here.
        if (e.VarName == "PLANE_TOUCHDOWN_NORMAL_VELOCITY")
        {
            return true;
        }

        // Handle takeoff assist toggle activation (receives position from RequestPositionForTakeoffAssist)
        if (e.VarName == "POSITION_FOR_TAKEOFF_ASSIST")
        {
            if (e.PositionData.HasValue)
            {
                var pos = e.PositionData.Value;

                // If takeoff assist isn't already active AND doesn't already have a
                // reference, try to seed one. Probe order:
                //   (1) taxi-guidance lineup reference (the common case — pilot taxied
                //       to the runway via taxi guidance)
                //   (2) under-aircraft runway detection (pilot taxied manually; the
                //       runway centerline geometry is available from the airport's
                //       taxi graph, so we can identify the runway from position +
                //       heading alone — same geometry Where-Am-I uses)
                //   (3) (no fallback here — TakeoffAssistManager.Toggle's no-reference
                //       branch will create a synthetic centerline from current
                //       position and heading)
                if (!takeoffAssistManager.IsActive && !takeoffAssistManager.HasRunwayReference)
                {
                    // (1) Taxi-guidance lineup
                    bool seeded = false;
                    if (taxiGuidanceManager.TryGetRunwayLineupReference(
                        out double rwyLat, out double rwyLon,
                        out double rwyHdgTrue, out double rwyHdgMag,
                        out string rwyId, out string rwyIcao))
                    {
                        if (!string.IsNullOrEmpty(rwyId))
                        {
                            takeoffAssistManager.SetRunwayReference(
                                rwyLat, rwyLon, rwyHdgTrue, rwyHdgMag, rwyId, rwyIcao);
                            seeded = true;
                        }
                    }

                    // (2) Under-aircraft detection — only when on the ground. Same
                    //     ICAO-resolution pattern as Where-Am-I (canonical 4-char
                    //     ICAOs only; the 3-char idents the DB also returns are for
                    //     fields the taxi-graph layer can't load).
                    if (!seeded && _lastOnGround && airportDataProvider != null)
                    {
                        var nearby = airportDataProvider
                            .GetNearbyAirportICAOs(pos.Latitude, pos.Longitude, 5.0)
                            .Where(c => c != null && c.Length == 4)
                            .ToList();
                        if (nearby.Count > 0 &&
                            taxiGuidanceManager.TryDetectRunwayUnderAircraft(
                                airportDataProvider, nearby[0],
                                pos.Latitude, pos.Longitude,
                                pos.HeadingMagnetic, pos.MagneticVariation,
                                out double detLat, out double detLon,
                                out double detHdgTrue, out double detHdgMag,
                                out string detRwyId, out string detIcao))
                        {
                            takeoffAssistManager.SetRunwayReference(
                                detLat, detLon, detHdgTrue, detHdgMag, detRwyId, detIcao);
                        }
                    }
                }

                takeoffAssistManager.Toggle(pos.Latitude, pos.Longitude, pos.HeadingMagnetic, pos.MagneticVariation);
            }
            return true;
        }

        // Handle takeoff assist position updates (for centerline tracking)
        if (e.VarName == "TAKEOFF_ASSIST_POSITION" && takeoffAssistManager.IsActive)
        {
            if (e.PositionData.HasValue)
            {
                var pos = e.PositionData.Value;
                takeoffAssistManager.ProcessPositionUpdate(pos.Latitude, pos.Longitude, pos.HeadingMagnetic);
            }
        }

        // Handle takeoff assist pitch updates
        if (e.VarName == "TAKEOFF_ASSIST_PITCH" && takeoffAssistManager.IsActive)
        {
            takeoffAssistManager.ProcessPitchUpdate(e.Value);
        }

        // Handle takeoff assist IAS updates (for speed callouts)
        if (e.VarName == "TAKEOFF_ASSIST_IAS" && takeoffAssistManager.IsActive)
        {
            takeoffAssistManager.ProcessSpeedUpdate(e.Value);
        }

        // Handle taxi guidance position updates (active during Taxiing, LiningUp,
        // AND LandingRollout phases). LandingRollout is critical: BeginLandingRollout
        // sets state=LandingRollout and UpdateLandingRollout's per-frame logic (auto-
        // transition to Taxiing on slowdown, distance-based callouts) only runs if
        // UpdatePosition is fed every frame. Without LandingRollout in this gate, the
        // touchdown announcement fires once and then the state-machine is silent
        // until StopGuidance.
        if (e.VarName == "TAXI_GUIDANCE_POSITION" &&
            (taxiGuidanceManager.State == TaxiGuidanceState.Taxiing ||
             taxiGuidanceManager.State == TaxiGuidanceState.LiningUp ||
             taxiGuidanceManager.State == TaxiGuidanceState.LandingRollout))
        {
            if (e.PositionData.HasValue)
            {
                var pos = e.PositionData.Value;
                // DIAGNOSTIC: log the first TAXI_GUIDANCE_POSITION event we
                // dispatch while in LandingRollout, so we can tell whether the
                // per-frame data is actually flowing during the rollout phase.
                if (taxiGuidanceManager.State == TaxiGuidanceState.LandingRollout &&
                    !_diagLoggedFirstRolloutPos)
                {
                    _diagLoggedFirstRolloutPos = true;
                    try
                    {
                        string diagPath = MSFSBlindAssist.Utils.AppLogs.PathFor("landing_exit.log");
                        System.IO.File.AppendAllText(diagPath,
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [MF] First TAXI_GUIDANCE_POSITION in LandingRollout: " +
                            $"lat={pos.Latitude:F6} lon={pos.Longitude:F6} hdgMag={pos.HeadingMagnetic:F1} " +
                            $"magVar={pos.MagneticVariation:F2} gs={pos.GroundSpeedKnots:F1}{Environment.NewLine}");
                    }
                    catch { }
                }
                taxiGuidanceManager.UpdatePosition(
                    pos.Latitude, pos.Longitude,
                    pos.HeadingMagnetic, pos.MagneticVariation,
                    pos.GroundSpeedKnots);
            }
        }

        // Docking guidance runs on every TAXI_GUIDANCE_POSITION frame regardless of
        // taxi-guidance state — it has its own guard (_gate == null / disabled / not Idle
        // → reset), so calling it every frame is safe and cheap. NOTE the feed itself is
        // taxi-scoped: OnTaxiGuidanceStateChanged stops position monitoring when taxi
        // reaches Arrived/Inactive, so docking gets NO frames after that point. That is
        // fine by design — arrival ownership is engage-latched (docking has either already
        // finished, or never engaged and taxi announced the arrival), the parked solid
        // tone is self-sustaining until the pilot presses Stop, and stale docking state is
        // cleared at the next flight boundary (takeoff-assist / LandingRollout) or healed
        // by the absolute-distance disengage on the next route's frames.
        if (e.VarName == "TAXI_GUIDANCE_POSITION" && e.PositionData.HasValue)
        {
            var pos = e.PositionData.Value;
            dockingGuidanceManager.UpdatePosition(
                pos.Latitude, pos.Longitude,
                pos.HeadingMagnetic, pos.MagneticVariation,
                pos.GroundSpeedKnots);

            // Exactly one panning tone at a time, and ENGAGE-LATCHED arrival ownership.
            // Docking owns the PRECISE final lineup: once it is engaged (within ~50 m of the
            // stop, roughly aligned, slow) it pans an intercept-angle cue to the gate centerline,
            // so mute the taxi steering tone AND taxi's terminal arrival callouts while docking
            // is active (Docking or Stopped). Before engagement taxi speaks and steers normally —
            // critical for navdata gates where docking may NEVER engage (approach outside the 70°
            // cone, stop beyond engage range, approximate navdata heading): the pilot still gets
            // taxi's full arrival sequence instead of total verbal silence. The brief overlap case
            // (taxi says "Stop. Hold position." at its route-end node and docking engages a moment
            // later with "Docking guidance… X to stop") is sequential and self-correcting — far
            // better than the silent-arrival failure mode of suppressing for the whole approach.
            // IsActive / IsArmedAwaitingEngage are lock-free volatile snapshots — no lock cost.
            bool dockingActive = dockingGuidanceManager.IsActive;
            taxiGuidanceManager.SetSteeringToneSuppressed(dockingActive);
            taxiGuidanceManager.SetDockingActive(dockingActive);
            // Pre-engage window: docking armed with the GSX stop still ahead — taxi's
            // arrival wording redirects forward instead of saying "parking brake" at
            // the navdata point (KATL F3 2026-06-11: 26 s parked short, docking Armed).
            taxiGuidanceManager.SetDockingPending(dockingGuidanceManager.IsArmedAwaitingEngage);
        }

        // Cache SIM_ON_GROUND on every update, regardless of which features are
        // currently active. AnnounceWhereAmI uses this to silence itself in
        // flight (it's a ground-only feature — there's a separate location/city
        // hotkey for airborne queries). The landing-exit planner forwarding is
        // gated separately on HasPendingExit, but the cache must always run.
        if (e.VarName == "SIM_ON_GROUND")
        {
            bool onGround = e.Value >= 0.5;
            bool justTouchedDown = onGround && !_lastOnGround;
            _lastOnGround = onGround;
            // Mirror to SimConnectManager so other components (LandingExitForm,
            // etc.) that have a SimConnectManager reference can read the latest
            // air/ground state without a separate MainForm dependency.
            simConnectManager.LastKnownOnGround = onGround;

            // Auto-deactivate visual guidance on touchdown: from this moment on,
            // the landing-exit planner / taxi guidance take over the rollout and
            // taxi guidance respectively, so the dual-tone guidance no longer
            // has a useful job. Keeping it running would compete with the taxi
            // steering tone audibly. Only fires on the airborne→on-ground edge,
            // so a user who manually engages visual guidance on the ramp for any
            // reason (preflight test, etc.) is not surprised by auto-deactivation.
            if (justTouchedDown && visualGuidanceManager.IsActive)
            {
                visualGuidanceManager.Toggle();
            }

            // Open the peak-g capture window at the touchdown edge, seeded with the g at contact,
            // so the ReadLastLandingPeakG hotkey reports the impact spike. The landing RATE itself
            // is read live from the persistent PLANE_TOUCHDOWN_NORMAL_VELOCITY cache by its hotkey.
            if (justTouchedDown)
            {
                landingRateAnnouncer.OnTouchdown(
                    simConnectManager.GetCachedVariableValue("G_FORCE") ?? 1.0);
            }

            // Feed SIM_ON_GROUND transitions to the landing-exit planner so it
            // can detect touchdown and auto-activate taxi guidance to the
            // pre-selected exit. ALWAYS request a fresh aircraft position at
            // this moment — do NOT trust SimConnectManager.LastKnownPosition.
            //
            // Why: lastKnownPosition is only updated by VISUAL_GUIDANCE,
            // TAXI_GUIDANCE, and TAKEOFF_ASSIST data paths. None of those
            // fire during a hand-flown approach without visual guidance
            // enabled. In that case the cached position is whatever the
            // last active path left there — typically the departure-airport
            // taxi-out at GS ~10 kts. Feeding that to ProcessGroundState
            // fails the planner's GS≥40 kt "real landing" gate and the
            // activation is silently skipped at touchdown. Always going
            // through RequestAircraftPositionAsync costs one SimConnect
            // roundtrip (~33 ms at 30 Hz) — negligible inside the rollout
            // window — and guarantees fresh GS / lat / lon at the moment
            // the planner needs them.
            //
            // _activatedThisLanding inside ActivateGuidance + a
            // HasPendingExit recheck inside the callback together prevent
            // double-fire if SIM_ON_GROUND bounces (oleo flicker on hard
            // landings).
            if (landingExitPlanner.HasPendingExit)
            {
                bool capturedOnGround = onGround;
                simConnectManager.RequestAircraftPositionAsync(p =>
                {
                    if (!landingExitPlanner.HasPendingExit) return;
                    double hdgTrue = p.HeadingMagnetic + p.MagneticVariation;
                    landingExitPlanner.ProcessGroundState(
                        capturedOnGround, p.GroundSpeedKnots, p.Latitude, p.Longitude, hdgTrue);
                });
            }
        }

        // Keep the open Taxi Assist form's cached position fresh so that
        // when the user presses Calculate (especially during a mid-taxi
        // route amendment), the route starts from the CURRENT position.
        if (e.VarName == "TAXI_GUIDANCE_POSITION" &&
            taxiAssistForm != null && !taxiAssistForm.IsDisposed && taxiAssistForm.Visible &&
            e.PositionData.HasValue)
        {
            var pos = e.PositionData.Value;
            taxiAssistForm.UpdateAircraftPosition(pos.Latitude, pos.Longitude, pos.HeadingMagnetic);
        }

        // Handle hand fly mode pitch updates
        if (e.VarName == "PLANE_PITCH_DEGREES" && handFlyManager.IsActive)
        {
            // Convert radians to degrees and negate (SimConnect uses body axis: negative = nose up)
            double pitchDegrees = -(e.Value * (180.0 / Math.PI));
            handFlyManager.ProcessPitchUpdate(pitchDegrees);
            // Don't return - allow data to flow to visual guidance too
        }

        // Handle hand fly mode bank updates
        if (e.VarName == "PLANE_BANK_DEGREES" && handFlyManager.IsActive)
        {
            // Convert radians to degrees (positive = right bank, negative = left bank)
            double bankDegrees = e.Value * (180.0 / Math.PI);
            handFlyManager.ProcessBankUpdate(bankDegrees);
            // Don't return - allow data to flow to visual guidance too
        }

        // Handle hand fly mode heading updates
        if (e.VarName == "PLANE_HEADING_DEGREES_MAGNETIC" && handFlyManager.IsActive)
        {
            // Convert radians to degrees
            double headingDegrees = e.Value * (180.0 / Math.PI);
            handFlyManager.ProcessHeadingUpdate(headingDegrees);
            // Don't return - allow data to flow to visual guidance too
        }

        // Handle hand fly mode vertical speed updates
        if (e.VarName == "HAND_FLY_VERTICAL_SPEED" && handFlyManager.IsActive)
        {
            // Already in feet per minute
            handFlyManager.ProcessVerticalSpeedUpdate(e.Value);
            return true;
        }

        // Handle visual guidance position updates
        // Handle visual guidance position updates (AIRCRAFT_POSITION struct)
        if (e.VarName == "VISUAL_GUIDANCE_POSITION" && visualGuidanceManager.IsActive && e.PositionData != null)
        {
            var pos = e.PositionData.Value;

            // Update position data from AIRCRAFT_POSITION struct
            visualGuidanceManager.UpdateLatitude(pos.Latitude);
            visualGuidanceManager.UpdateLongitude(pos.Longitude);
            visualGuidanceManager.UpdateAltitudeMSL(pos.Altitude);
            visualGuidanceManager.UpdateHeading(pos.HeadingMagnetic);
            visualGuidanceManager.UpdateGroundSpeed(pos.GroundSpeedKnots);
            visualGuidanceManager.UpdateVerticalSpeed(pos.VerticalSpeedFPM);

            // Note: AGL is updated separately via VISUAL_GUIDANCE_AGL handler
            // ProcessUpdate() is called when AGL arrives to ensure all data is complete

            return true;
        }

        // Handle visual guidance AGL updates (requested separately)
        if (e.VarName == "VISUAL_GUIDANCE_AGL" && visualGuidanceManager.IsActive)
        {
            visualGuidanceManager.UpdateAGL(e.Value);

            // Process the update now that all position data should be available
            visualGuidanceManager.ProcessUpdate();
            return true;
        }

        // Handle visual guidance ground track updates (for PID drift detection)
        if (e.VarName == "VISUAL_GUIDANCE_GROUND_TRACK" && visualGuidanceManager.IsActive)
        {
            visualGuidanceManager.UpdateGroundTrack(e.Value);
            return true;
        }

        // Visual guidance attitude (pitch / bank) now comes from VG's own SimConnect
        // monitoring batch — no longer dependent on HandFly being active. Heading is
        // already populated by the VG position update above.
        if (e.VarName == "VISUAL_GUIDANCE_PITCH" && visualGuidanceManager.IsActive)
        {
            // SimConnect pitch is positive=nose down (Euler convention); negate to
            // standard right-handed convention (positive=nose up).
            double pitchDegrees = -(e.Value * (180.0 / Math.PI));
            visualGuidanceManager.UpdatePitch(pitchDegrees);
            return true;
        }
        if (e.VarName == "VISUAL_GUIDANCE_BANK" && visualGuidanceManager.IsActive)
        {
            // SimConnect bank is left-positive; VisualGuidanceManager.StandardBank() applies
            // the sign conversion at the consumer side, so we pass the raw SimConnect value
            // (just converted from radians to degrees).
            double bankDegrees = e.Value * (180.0 / Math.PI);
            visualGuidanceManager.UpdateBank(bankDegrees);
            return true;
        }
        if (e.VarName == "VISUAL_GUIDANCE_AOA" && visualGuidanceManager.IsActive)
        {
            // INCIDENCE ALPHA from SimConnect arrives in radians. VG smooths and sanity-gates
            // it consumer-side; we just convert and forward.
            double aoaDegrees = e.Value * (180.0 / Math.PI);
            visualGuidanceManager.UpdateAoA(aoaDegrees);
            return true;
        }

        // Handle aircraft variable hotkey announcements
        // A380 metric-altitude mode (FCU MTRS / A32NX_METRIC_ALT_TOGGLE): when active, the
        // current-altitude readouts (A = MSL, Q = AGL) speak metres instead of feet. Gated to
        // the A380 by both the aircraft-type check and the MetricAlt flag — no other aircraft
        // and no non-metric A380 state reach this branch, so feet behaviour is unchanged.
        if ((e.VarName == "ALTITUDE_MSL" || e.VarName == "ALTITUDE_AGL")
            && currentAircraft is Aircraft.FlyByWireA380Definition a380Alt)
        {
            // Metric on -> "X meters"; metric off -> "X feet". Previously the off case fell
            // through and spoke just the number with no unit — now it says "feet" for
            // consistency with the "meters" suffix.
            if (a380Alt.MetricAlt) announcer.AnnounceImmediate($"{e.Value * 0.3048:0} meters");
            else announcer.AnnounceImmediate($"{e.Value:0} feet");
            return true;
        }

        if (e.VarName == "ALTITUDE_AGL" || e.VarName == "ALTITUDE_MSL" || e.VarName == "AIRSPEED_INDICATED" ||
            e.VarName == "AIRSPEED_TRUE" || e.VarName == "GROUND_SPEED" || e.VarName == "MACH_SPEED" ||
            e.VarName == "VERTICAL_SPEED" || e.VarName == "HEADING_MAGNETIC" || e.VarName == "HEADING_TRUE" ||
            e.VarName == "BANK_ANGLE" || e.VarName == "PITCH_ANGLE" ||
            e.VarName == "SPEED_GD" || e.VarName == "SPEED_S" || e.VarName == "SPEED_F" ||
            e.VarName == "SPEED_VFE" || e.VarName == "SPEED_VLS" || e.VarName == "SPEED_VS" ||
            e.VarName == "FUEL_QUANTITY" || e.VarName == "FUEL_QUANTITY_KG" || e.VarName == "GROSS_WEIGHT" || e.VarName == "GROSS_WEIGHT_KG" || e.VarName == "FLAP_POSITION" || e.VarName == "GEAR_POSITION" || e.VarName == "WAYPOINT_INFO" ||
            e.VarName == "OUTSIDE_TEMP" || e.VarName == "SQUAWK_CODE" ||
            e.VarName == "LOCAL_TIME_SECONDS" || e.VarName == "ZULU_TIME_SECONDS")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // Handle destination runway distance announcements
        if (e.VarName == "DISTANCE_TO_RUNWAY")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // Handle ILS guidance announcements
        if (e.VarName == "ILS_GUIDANCE")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // ECAM LED announcements are now handled by aircraft-specific ProcessSimVarUpdate()

        // Handle special display updates
        if (e.VarName == "DISPLAY_UPDATE")
        {
            return true;
        }

        // Handle FCU_VALUES special case
        if (e.VarName == "FCU_VALUES")
        {
            announcer.Announce(e.Description);
            return true;
        }

        // Handle ECAM message announcements (using queue for sequential delivery)
        if (e.VarName == "ECAM_MESSAGE")
        {
            announcer.AnnounceWithQueue(e.Description);
            return true;
        }

        return false; // Not a special case, continue normal processing
    }

    /// <summary>
    /// Announces the state of a variable based on its value descriptions.
    /// </summary>
    private void AnnounceVariableState(string varName, double value)
    {
        if (currentAircraft.GetVariables().ContainsKey(varName))
        {
            var varDef = currentAircraft.GetVariables()[varName];
            if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsKey(value))
            {
                string stateDescription = varDef.ValueDescriptions[value];
                announcer.AnnounceImmediate(stateDescription);
            }
            else
            {
                // Fallback to display name + value if no descriptions
                announcer.AnnounceImmediate($"{varDef.DisplayName}: {value}");
            }
        }
    }

    private void UpdateControlFromSimVar(string varName, double value)
    {
        bool controlFound = currentControls.ContainsKey(varName);

        if (controlFound)
        {
            updatingFromSim = true;

            Control control = currentControls[varName];
            if (control is TrackBar slider)
            {
                // Reflect a sim-side axis change back into the slider (updatingFromSim is set,
                // so the slider's ValueChanged handler won't write it back — no feedback loop).
                if (currentAircraft.GetVariables().TryGetValue(varName, out var sVarDef) && sVarDef.RenderAsSlider)
                {
                    double sspan = (sVarDef.SliderMax - sVarDef.SliderMin) == 0 ? 1 : (sVarDef.SliderMax - sVarDef.SliderMin);
                    int pct = (int)Math.Round((value - sVarDef.SliderMin) / sspan * 100.0);
                    pct = Math.Max(0, Math.Min(100, pct));
                    if (slider.Value != pct) slider.Value = pct;
                }
            }
            else if (control is ComboBox combo)
            {
                // Synthetic, MSFSBA-internal selector combos (the A32NX System Display page
                // picker A32NX_MSFSBA_SD_PAGE, the synthetic speed-brake combo, and the
                // thrust-lever _DETENT combos) are the SOLE source of truth for their own value:
                // the combo's SelectedIndex IS the state. They have no real, continuously
                // broadcast sim var to defer to — the backing L:var is written ONLY by the
                // user's own selection and is re-requested purely to repaint the status box.
                // Re-setting SelectedIndex from those (stale / async) round-trip reads yanks the
                // selection backward while the user is arrowing (the "wonky" A320 SD combo). Skip
                // the snap-back for them; the same update still flows on to repaint the box.
                // (The A380 SD combo is a REAL Continuous sim var whose broadcast always agrees
                // with the user's selection, so it is unaffected. Mirrors the synthetic-combo
                // exclusion list in FlyByWireA320Definition.cs.)
                bool isSyntheticSelector =
                    varName == "A32NX_MSFSBA_SD_PAGE" ||
                    varName == "A32NX_MSFSBA_SPEEDBRAKE" ||
                    varName.EndsWith("_DETENT", StringComparison.Ordinal);

                // Find the matching value in the combo box
                if (!isSyntheticSelector && currentAircraft.GetVariables().ContainsKey(varName))
                {
                    var varDef = currentAircraft.GetVariables()[varName];
                    if (varDef.ValueDescriptions.ContainsKey(value))
                    {
                        string description = varDef.ValueDescriptions[value];
                        int index = combo.Items.IndexOf(description);
                        if (index >= 0 && combo.SelectedIndex != index)
                        {
                            combo.SelectedIndex = index;
                        }
                    }
                }
            }
            else if (control is TextBox textBox && textBox.ReadOnly)
            {
                // Read-only status TextBox. Two flavors:
                //  (a) Continuous-numeric readout (RenderAsReadOnlyStatus + Units +
                //      no ValueDescriptions) — format as "<value:Format> <Units>".
                //  (b) Enum-style status field (door state, annunciator, etc.) —
                //      mirror the value through ValueDescriptions; fall back to
                //      raw numeric if the cached value isn't in the map.
                if (currentAircraft.GetVariables().ContainsKey(varName))
                {
                    var varDef = currentAircraft.GetVariables()[varName];
                    string newText;
                    bool isContinuousReadout =
                        varDef.RenderAsReadOnlyStatus &&
                        (varDef.ValueDescriptions == null || varDef.ValueDescriptions.Count == 0) &&
                        !string.IsNullOrEmpty(varDef.Units);
                    if (isContinuousReadout)
                    {
                        double displayValue = value * varDef.Scale + varDef.Offset;
                        newText = $"{displayValue.ToString(varDef.Format, System.Globalization.CultureInfo.InvariantCulture)} {varDef.Units}";
                    }
                    else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.TryGetValue(value, out string? desc))
                    {
                        newText = desc;
                    }
                    else
                    {
                        newText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (textBox.Text != newText)
                        textBox.Text = newText;
                }
            }
            else if (control is Button btn)
            {
                // Update stateful button label from StateVariable or ValueDescriptions
                if (currentAircraft.GetVariables().ContainsKey(varName))
                {
                    var varDef = currentAircraft.GetVariables()[varName];
                    if (!string.IsNullOrEmpty(varDef.StateVariable))
                    {
                        // This button uses a StateVariable — but this update is for the button's own variable,
                        // not the state variable. Skip — the state variable update will handle the label.
                    }
                    else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 0)
                    {
                        // Mirror the build-time button-label logic (the RenderAsButton branch):
                        // resting-state (value 0) suppression is OPT-IN via
                        // SuppressRestingButtonState, set only by the FBW momentary-button
                        // helpers (ECAM-CP keys, calls, acks, tests) — a momentary push-button
                        // has no meaningful RESTING state, so relabelling e.g. "ECAM All" to
                        // "ECAM All: Released" reads as noise. By DEFAULT the value-0 label
                        // shows: on PMDG ("LNAV: Off") and HS787 ("Baro STD: QNH") it IS
                        // meaningful state and must not be silenced. The functional dispatch
                        // keys on the var name/events, never the label, so this is cosmetic.
                        string newLabel = ((value != 0 || !varDef.SuppressRestingButtonState)
                                           && varDef.ValueDescriptions.TryGetValue(value, out string? stateText))
                            ? $"{varDef.DisplayName}: {stateText}"
                            : varDef.DisplayName;
                        if (btn.Text != newLabel)
                        {
                            btn.Text = newLabel;
                            btn.AccessibleName = newLabel;
                        }
                    }
                }
            }

            updatingFromSim = false;
        }

        // Also update any button labels whose StateVariable matches this variable
        UpdateButtonStateFromStateVariable(varName, value);
    }

    /// <summary>
    /// Updates button labels for any buttons whose StateVariable matches the given variable name.
    /// Separated from UpdateControlFromSimVar so it can be called independently without
    /// triggering control updates that could interfere with aircraft-specific processing.
    /// </summary>
    private void UpdateButtonStateFromStateVariable(string varName, double value)
    {
        foreach (var kvp in currentControls)
        {
            if (kvp.Value is Button stateBtn && currentAircraft.GetVariables().ContainsKey(kvp.Key))
            {
                var btnVarDef = currentAircraft.GetVariables()[kvp.Key];
                if (btnVarDef.StateVariable == varName)
                {
                    string stateLabel = $"{btnVarDef.DisplayName}: {(value != 0 ? "On" : "Off")}";
                    stateBtn.Text = stateLabel;
                    stateBtn.AccessibleName = stateLabel;
                }
            }
        }
    }

    private void RequestAllCurrentValues()
    {
        // The new continuous monitoring system automatically handles critical variables,
        // so we don't need to request ALL variables on connection anymore.
        // This dramatically improves connection performance.
        if (simConnectManager != null && simConnectManager.IsConnected)
        {
            System.Diagnostics.Debug.WriteLine("Connection established - continuous monitoring active for critical variables");
            // Continuous variables (IsAnnounced = true) are automatically requested every second
            // Panel variables are requested when panels are opened
            // Individual variables are requested on hotkey presses
        }
    }

    /// <summary>
    /// Find which panel a variable belongs to for efficient variable requests
    /// </summary>
    private string? GetPanelForVariable(string varKey)
    {
        foreach (var panel in currentAircraft.GetPanelControls())
        {
            if (panel.Value.Contains(varKey))
            {
                return panel.Key;
            }
        }
        return null; // Variable not found in any panel
    }

    private void HandleButtonStateAnnouncement(string eventName)
    {
        // Check if this button has a corresponding state variable to announce
        if (currentAircraft.GetButtonStateMapping().ContainsKey(eventName))
        {
            string stateVarKey = currentAircraft.GetButtonStateMapping()[eventName];

            // Request the state after a short delay to allow the sim to update
            System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
            stateTimer.Interval = 300; // 300ms delay
            stateTimer.Tick += (s, e) =>
            {
                stateTimer.Stop();
                stateTimer.Dispose();

                // Request the current state and announce it
                if (currentAircraft.GetVariables().ContainsKey(stateVarKey))
                {
                    // Track this state announcement request
                    pendingStateAnnouncements.TryAdd(stateVarKey, true);

                    // Request with forceUpdate=true to ensure we get the update even if value hasn't changed
                    simConnectManager.RequestVariable(stateVarKey, forceUpdate: true);
                }
            };
            stateTimer.Start();
        }
    }

    private void UpdateDisplayText(TextBox displayBox)
    {
        if (currentAircraft.GetPanelDisplayVariables().ContainsKey(currentPanel))
        {
            var displayVars = currentAircraft.GetPanelDisplayVariables()[currentPanel];
            List<string> values = new List<string>();

            foreach (var varKey in displayVars)
            {
                if (currentAircraft.GetVariables().ContainsKey(varKey))
                {
                    var varDef = currentAircraft.GetVariables()[varKey];

                    // Fall back to SimConnectManager's lastVariableValues cache
                    // when displayValues lacks an entry. lastVariableValues is
                    // populated in ProcessIndividualVariableResponse BEFORE the
                    // announced-var "unchanged" suppression at line 2215, so it
                    // holds the current value even when SimVarUpdated was
                    // suppressed and never reached MainForm's displayValues
                    // sink. Without this fallback, panel display fields for
                    // stable continuous announced vars (e.g. IRS POS_SET held
                    // at 1, IRS minutes held at -1) silently render as "--".
                    if (!displayValues.ContainsKey(varKey))
                    {
                        double? cached = simConnectManager?.GetCachedVariableValue(varKey);
                        if (cached.HasValue)
                        {
                            displayValues[varKey] = cached.Value;
                        }
                    }

                    if (displayValues.ContainsKey(varKey))
                    {
                        double value = displayValues[varKey];
                        string displayValue;

                        // Aircraft-specific decode for non-presentable raw values
                        // (e.g. ARINC429 baro/minimums words on the A380, which would
                        // otherwise render as a ~14-billion raw double).
                        if (currentAircraft.TryGetDisplayOverride(varKey, value, out string overrideText))
                        {
                            displayValue = overrideText;
                        }
                        // Generic ARINC429 auto-decode (after the ad-hoc override so baro/minimums/
                        // rudder etc. keep their custom logic; covers any IsArinc429 var with just
                        // value+unit, so a raw ~14-billion word never reaches a panel field).
                        else if (currentAircraft is BaseAircraftDefinition arincDef &&
                                 arincDef.TryDecodeArinc429(varKey, value, out string arincText))
                        {
                            displayValue = arincText;
                        }
                        // ARINC429 ENUM decode (mirrors the FBW ProcessSimVarUpdate announce
                        // guard): some announced FBW discretes (e.g. APU low fuel pressure)
                        // arrive as a huge SSM-encoded word (12884901888 = 0x3_00000000) that
                        // matches no 0/1 ValueDescription, so they'd render as a raw ~13-billion
                        // number. Decode to the 0/1 payload and map via ValueDescriptions.
                        else if (varDef.ValueDescriptions is { Count: > 0 } && value >= 4294967296.0
                                 && varDef.ValueDescriptions.TryGetValue(
                                        System.Math.Round(new SimConnect.Arinc429Word(value).ValueOr(0f)), out string? arincEnumDesc))
                        {
                            displayValue = arincEnumDesc;
                        }
                        // Check if we have value descriptions (like Off/Aligning/Aligned)
                        else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsKey(value))
                        {
                            displayValue = varDef.ValueDescriptions[value];
                        }
                        else
                        {
                            // Use numeric formatting for values without descriptions
                            string unit = "";
                            string formattedValue = "";

                            switch (varDef.Units)
                            {
                                case "volts":
                                    formattedValue = $"{value:F1}";
                                    unit = "V";
                                    break;
                                case "millibars":
                                    formattedValue = $"{value:F2}";
                                    unit = " hPa";
                                    break;
                                case "inHg":
                                    formattedValue = $"{value:F2}";
                                    unit = " inHg";
                                    break;
                                case "kHz":
                                    // Convert kHz to MHz for display (better precision)
                                    double freqMHz = value / 1000.0;
                                    formattedValue = $"{freqMHz:F3}";
                                    unit = " MHz";
                                    break;
                                default:
                                    formattedValue = $"{value:F0}";
                                    break;
                            }

                            displayValue = $"{formattedValue}{unit}";
                        }

                        values.Add($"{varDef.DisplayName}: {displayValue}");
                    }
                    else
                    {
                        values.Add($"{varDef.DisplayName}: --");
                    }
                }
            }

            SetDisplayTextPreserveCaret(displayBox, string.Join("\r\n", values));
        }
    }

    /// <summary>
    /// Writes new text into a (read-only) status display box WITHOUT yanking the
    /// screen-reader review cursor back to the top on every refresh. Delegates to the
    /// shared <see cref="Forms.DisplayText.SetPreserveCaret"/>, which rewrites only the
    /// characters that changed (so NVDA sees a localized edit, not a full content
    /// replacement) and keeps the caret on the same line. No-ops when unchanged.
    /// </summary>
    private void SetDisplayTextPreserveCaret(TextBox box, string text)
        => Forms.DisplayText.SetPreserveCaret(box, text);


    /// <summary>
    /// Converts a decimal squawk code to BCD (Binary Coded Decimal) format for XPNDR_SET event.
    /// Each digit of the squawk code is encoded in 4 bits.
    /// Example: 0422 -> (0*4096) + (4*256) + (2*16) + (2*1) = 1058
    /// </summary>
    private uint ConvertSquawkToBCD(string squawkCode, out string? errorMessage)
    {
        errorMessage = null;

        // Validate length
        if (squawkCode.Length != 4)
        {
            errorMessage = "Squawk code must be exactly 4 digits";
            return 0;
        }

        // Validate each digit (must be 0-7 for transponder codes)
        foreach (char c in squawkCode)
        {
            if (!char.IsDigit(c))
            {
                errorMessage = "Squawk code must contain only digits";
                return 0;
            }
            int digit = c - '0';
            if (digit > 7)
            {
                errorMessage = "Squawk code digits must be 0-7 only";
                return 0;
            }
        }

        // Convert to BCD format
        uint bcd = 0;
        for (int i = 0; i < 4; i++)
        {
            int digit = squawkCode[i] - '0';
            bcd += (uint)(digit << ((3 - i) * 4)); // Shift each digit by 4 bits per position
        }

        return bcd;
    }

    private void OnSimVarValueChanged(object? sender, SimVarChangeEventArgs e)
    {
        // For PMDG aircraft, IsInitialValue is always true on first change because the
        // simVarMonitor has never seen the variable before. But PMDG data manager already
        // suppresses the initial snapshot, so any change that reaches here IS a real change.
        // The FBW A380 has the SAME behaviour: its L:vars are monitored changed-only, so a
        // var's first sample only arrives WHEN it first changes (no startup baseline) — which
        // made the first switch/flap movement after load silent (only the 2nd worked). The
        // 5-second announcement grace period (EnableAnnouncements) already suppresses the
        // cold-and-dark startup snapshot, so treating the A380 like PMDG here is safe.
        bool isPMDG = currentAircraft is IPMDGAircraft;
        bool announceInitialChange = isPMDG || currentAircraft?.AircraftCode == "FBW_A380";
        bool shouldAnnounce = announceInitialChange ? !updatingFromSim : (!e.IsInitialValue && !updatingFromSim);

        if (shouldAnnounce && !string.IsNullOrEmpty(e.Description))
        {
            announcer.Announce(e.Description);
        }
    }

    private void BuildPMDGFieldMap()
    {
        _pmdgFieldToKeyMap = new Dictionary<string, string>();
        if (currentAircraft == null) return;
        foreach (var kvp in currentAircraft.GetVariables())
        {
            // Map Name (struct field name) → Key (variable key)
            if (!_pmdgFieldToKeyMap.ContainsKey(kvp.Value.Name))
                _pmdgFieldToKeyMap[kvp.Value.Name] = kvp.Key;
        }
    }

    private void OnPMDGVariableChanged(object? sender, PMDGVarUpdateEventArgs e)
    {
        if (_pmdgFieldToKeyMap == null) BuildPMDGFieldMap();

        // Translate struct field name to variable key
        if (!_pmdgFieldToKeyMap!.TryGetValue(e.FieldName, out string? varKey))
        {
            if (e.FieldName is "ELEC_GrdPwrSw" or "ELEC_GenSw_0" or "ELEC_GenSw_1" or "ELEC_APUGenSw_0" or "ELEC_APUGenSw_1")
                System.Diagnostics.Debug.WriteLine($"[MainForm] PMDG event {e.FieldName} DROPPED (varKey not found in map)");
            return;
        }

        if (e.FieldName is "ELEC_GrdPwrSw" or "ELEC_GenSw_0" or "ELEC_GenSw_1" or "ELEC_APUGenSw_0" or "ELEC_APUGenSw_1")
            System.Diagnostics.Debug.WriteLine($"[MainForm] PMDG event {e.FieldName} -> varKey={varKey} value={e.Value} initial={e.IsInitialSnapshot}");

        // Route PMDG variable changes through the same pipeline as SimVar updates
        var simVarEvent = new SimVarUpdateEventArgs
        {
            VarName = varKey,
            Value   = e.Value,
            Description = string.Empty,
            IsInitialSnapshot = e.IsInitialSnapshot,
        };
        OnSimVarUpdated(this, simVarEvent);
    }

    /// <summary>
    /// True for the quick-access readout hotkeys (the H/V/Q/S/D/B/P/A/F set) — single
    /// keypresses whose whole purpose is to speak a value. When one of these fires during
    /// an active visual-guidance session, VG opens a grace window so its per-second
    /// bank/centerline callouts don't talk over the readout.
    /// </summary>
    private static bool IsManualReadoutAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadTargetFPM
            or HotkeyAction.ReadPitch
            or HotkeyAction.ReadBankAngle
            or HotkeyAction.ReadVerticalSpeed
            or HotkeyAction.ReadAltitudeAGL
            or HotkeyAction.ReadAltitudeMSL
            or HotkeyAction.ReadAirspeedIndicated
            or HotkeyAction.ReadDestinationRunwayDistance
            or HotkeyAction.ReadHeadingMagnetic => true,
        _ => false
    };

    private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
    {
        // Actions that don't require SimConnect connection (can be used offline)
        var offlineActions = new HashSet<HotkeyAction>
        {
            HotkeyAction.ShowChecklist,
            HotkeyAction.ShowMETARReport,
            HotkeyAction.ShowColdTempCorrection,
            HotkeyAction.SimBriefBriefing,
            HotkeyAction.ShowElectronicFlightBag,
            HotkeyAction.ShowFenixMCDU,
            HotkeyAction.ShowPMDGEFB,
            HotkeyAction.ShowPMDGEFBFirstOfficer,
            HotkeyAction.TaxiStatus
        };

        // Guard clause: Block SimConnect-dependent actions if not fully connected
        if (!offlineActions.Contains(e.Action) && !simConnectManager.IsFullyConnected)
        {
            announcer.Announce("Not connected to simulator, please wait");
            return;
        }

        // If the pilot fired a manual readout query while visual guidance is active, open a
        // short grace window so VG's per-second bank/centerline callouts don't interrupt the
        // readout mid-sentence. See VisualGuidanceManager.NotifyManualQuery.
        if (visualGuidanceManager.IsActive && IsManualReadoutAction(e.Action))
        {
            visualGuidanceManager.NotifyManualQuery();
        }

        // Try aircraft-specific handler first
        bool handledByAircraft = currentAircraft.HandleHotkeyAction(e.Action, simConnectManager, announcer, this, hotkeyManager);

        // If aircraft handled it and it's a button action, check for state announcement
        if (handledByAircraft)
        {
            // Get the variable mapping to see if this needs state announcement
            var buttonStateMap = currentAircraft.GetButtonStateMapping();
            var variableMap = (currentAircraft as Aircraft.BaseAircraftDefinition)?.GetType()
                .GetMethod("GetHotkeyVariableMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(currentAircraft, null) as Dictionary<HotkeyAction, string>;

            if (variableMap != null && variableMap.TryGetValue(e.Action, out string? eventName))
            {
                // Check if this button has a state announcement
                if (!string.IsNullOrEmpty(eventName))
                {
                    HandleButtonStateAnnouncement(eventName);
                }
            }
            return; // Action was handled by aircraft
        }

        // Fall through to universal actions (truly universal, not aircraft-specific)
        switch (e.Action)
        {
            case HotkeyAction.ReadAltitudeAGL:
                simConnectManager.RequestAltitudeAGL();
                break;
            case HotkeyAction.ReadAltitudeMSL:
                simConnectManager.RequestAltitudeMSL();
                break;
            case HotkeyAction.ReadAirspeedIndicated:
                simConnectManager.RequestAirspeedIndicated();
                break;
            case HotkeyAction.ReadAirspeedTrue:
                simConnectManager.RequestAirspeedTrue();
                break;
            case HotkeyAction.ReadGroundSpeed:
                simConnectManager.RequestGroundSpeed();
                break;
            case HotkeyAction.ReadMachSpeed:
                simConnectManager.RequestMachSpeed();
                break;
            case HotkeyAction.ReadLastLandingRate:
            {
                // Read straight from the persistent touchdown-velocity cache (ft/s × 60 = fpm).
                // The value is latched by the sim at touchdown and survives until the next landing.
                // Gated on a touchdown edge actually OBSERVED this session (HasLanding) — the
                // SimVar can hold a junk latched value at a runway spawn.
                double? td = simConnectManager.GetCachedVariableValue("PLANE_TOUCHDOWN_NORMAL_VELOCITY");
                if (landingRateAnnouncer.HasLanding && td.HasValue && System.Math.Abs(td.Value) > 0.01)
                {
                    int fpm = (int)System.Math.Round(System.Math.Abs(td.Value) * 60.0);
                    announcer.AnnounceImmediate($"Landing rate {fpm} feet per minute.");
                }
                else
                {
                    announcer.AnnounceImmediate("No landing recorded this session.");
                }
                break;
            }
            case HotkeyAction.ReadLastLandingPeakG:
            {
                double? g = landingRateAnnouncer.LastPeakG;
                if (g.HasValue)
                {
                    announcer.AnnounceImmediate($"Landing g-force {g.Value:F2} g.");
                }
                else
                {
                    announcer.AnnounceImmediate("No landing recorded this session.");
                }
                break;
            }
            case HotkeyAction.ReadVerticalSpeed:
                simConnectManager.RequestVerticalSpeed();
                break;
            case HotkeyAction.ReadBankAngle:
                simConnectManager.RequestBankAngle();
                break;
            case HotkeyAction.ReadPitch:
                simConnectManager.RequestPitch();
                break;
            case HotkeyAction.ReadHeadingMagnetic:
                simConnectManager.RequestHeadingMagnetic();
                break;
            case HotkeyAction.ReadHeadingTrue:
                simConnectManager.RequestHeadingTrue();
                break;
            case HotkeyAction.RunwayTeleport:
                ShowRunwayTeleportDialog();
                break;
            case HotkeyAction.GateTeleport:
                ShowGateTeleportDialog();
                break;
            case HotkeyAction.LocationInfo:
                ShowLocationInfoDialog();
                break;
            case HotkeyAction.ReadNearestCity:
                AnnounceNearestCity();
                break;
            case HotkeyAction.SimBriefBriefing:
                OpenSimBriefBriefing();
                break;
            case HotkeyAction.ShowTcasWindow:
                OpenTcasWindow();
                break;
            case HotkeyAction.AnnounceTcasTraffic:
                AnnounceTrackedTcasTraffic();
                break;
            case HotkeyAction.ShowWeatherRadar:
                OpenWeatherRadarWindow();
                break;
            case HotkeyAction.ReadOutsideTemperature:
                simConnectManager.RequestOutsideTemperature();
                break;
            case HotkeyAction.ReadSquawkCode:
                simConnectManager.RequestSquawkCode();
                break;
            case HotkeyAction.SelectDestinationRunway:
                ShowDestinationRunwayDialog();
                break;
            case HotkeyAction.ReadDestinationRunwayDistance:
                RequestDestinationRunwayDistance();
                break;
            case HotkeyAction.ReadILSGuidance:
                RequestILSGuidance();
                break;
            case HotkeyAction.ReadWindInfo:
                RequestWindInfo();
                break;
            case HotkeyAction.ReadNavRadioInfo:
                RequestNavRadioInfo();
                break;
            case HotkeyAction.ShowMETARReport:
                ShowMETARReportDialog();
                break;
            case HotkeyAction.ShowColdTempCorrection:
                ShowColdTempCorrectionDialog();
                break;
            case HotkeyAction.ShowChecklistECL:
                ShowChecklistECLDialog();
                break;
            case HotkeyAction.ShowChecklist:
                ShowChecklistDialog();
                break;
            case HotkeyAction.ShowElectronicFlightBag:
                ShowElectronicFlightBagDialog();
                break;
            case HotkeyAction.ShowFenixMCDU:
                // Single "show MCDU" hotkey routed by the currently-selected
                // aircraft. The action's enum name is historical (it was added
                // for Fenix first); FBW A380 reuses the same chord.
                if (currentAircraft is IPMDGAircraft && simConnectManager.PMDGDataManager != null)
                {
                    ShowPMDGCDUDialog();
                }
                else if (currentAircraft?.AircraftCode == "FBW_A380")
                {
                    ShowFBWA380MCDUDialog();
                }
                else if (currentAircraft?.AircraftCode == "HS_787")
                {
                    ShowHS787FMCDialog();
                }
                else if (currentAircraft?.AircraftCode == "A320")
                {
                    ShowFlyByWireMCDUDialog();
                }
                else
                {
                    ShowFenixMCDUDialog();
                }
                break;
            case HotkeyAction.ShowPMDGEFB:
                if (currentAircraft is IPMDGAircraft pmdgEFB && pmdgEFB.HasEFBSupport)
                {
                    ShowPmdgCoherentEfbDialog(firstOfficer: false);
                }
                else if (currentAircraft?.AircraftCode == "FBW_A380")
                {
                    ShowFbwEfbDialog();
                }
                else if (currentAircraft?.AircraftCode == "HS_787")
                {
                    announcer.AnnounceImmediate("787 E F B not available.");
                }
                else if (currentAircraft?.AircraftCode == "A320")
                {
                    // Unified flyPad: the A320 uses the SAME generic WebView2 form +
                    // CoherentEFBClient as the A380 (both drive the one shared
                    // coherent-flypad-agent.js over the "- EFB" Coherent view).
                    ShowFbwEfbDialog();
                }
                else if (currentAircraft?.AircraftCode == "FENIX_A320CEO")
                {
                    // The Fenix EFB web UI is already screen-reader accessible, so we
                    // host the live site directly (no scraping/shim like the FBW flyPad).
                    ShowFenixEFBDialog();
                }
                break;
            case HotkeyAction.ShowPMDGEFBFirstOfficer:
                if (currentAircraft is IPMDGAircraft pmdgEfbFo && pmdgEfbFo.HasEFBSupport)
                    ShowPmdgCoherentEfbDialog(firstOfficer: true);
                break;
            case HotkeyAction.ShowRMP:
                if (currentAircraft is FlyByWireA380Definition)
                {
                    ShowFBWA380RmpDialog();
                }
                else
                {
                    announcer.AnnounceImmediate("The Radio Management Panel window is only available on the A380.");
                }
                break;
            case HotkeyAction.ShowDCDU:
                if (currentAircraft?.AircraftCode == "A320")
                {
                    ShowA32NXDcduDialog();
                }
                else
                {
                    announcer.AnnounceImmediate("The DCDU window is only available on the A32NX.");
                }
                break;
            case HotkeyAction.ShowOANS:
                if (currentAircraft?.AircraftCode == "FBW_A380")
                {
                    ShowFBWA380OansDialog();
                }
                else
                {
                    announcer.AnnounceImmediate("OANS airport map is only available on the A380.");
                }
                break;
            case HotkeyAction.ShowTrackFixWindow:
                ShowTrackFixDialog();
                break;
            case HotkeyAction.ToggleTakeoffAssist:
                ToggleTakeoffAssist();
                break;
            case HotkeyAction.ToggleHandFlyMode:
                ToggleHandFlyMode();
                break;
            case HotkeyAction.ToggleVisualGuidance:
                ToggleVisualGuidance();
                break;
            case HotkeyAction.ReadTargetFPM:
                if (visualGuidanceManager.IsActive)
                {
                    double targetFPM = visualGuidanceManager.GetTargetFPM();
                    double altitudeDeviation = visualGuidanceManager.GetAltitudeDeviation();

                    // Format: "target -600, 1200 high" or "target -200, 1580 low"
                    string deviationText = altitudeDeviation >= 0
                        ? $"{Math.Abs(altitudeDeviation):F0} high"
                        : $"{Math.Abs(altitudeDeviation):F0} low";

                    announcer.AnnounceImmediate($"target {targetFPM:F0}, {deviationText}");
                }
                else
                {
                    announcer.AnnounceImmediate("Visual guidance not active");
                }
                break;
            case HotkeyAction.ReadTrackSlot1:
                ReadTrackedWaypoint(1);
                break;
            case HotkeyAction.ReadTrackSlot2:
                ReadTrackedWaypoint(2);
                break;
            case HotkeyAction.ReadTrackSlot3:
                ReadTrackedWaypoint(3);
                break;
            case HotkeyAction.ReadTrackSlot4:
                ReadTrackedWaypoint(4);
                break;
            case HotkeyAction.ReadTrackSlot5:
                ReadTrackedWaypoint(5);
                break;
            case HotkeyAction.DescribeScene:
                DescribeSceneAsync();
                break;
            case HotkeyAction.TaxiAssistForm:
                ShowTaxiAssistForm();
                break;
            case HotkeyAction.TaxiStatus:
                // Y — rolling current status from live position (current taxiway, next turn,
                // distance to destination). Recomputed on every press from the route + position.
                // While docking owns the final approach, report ITS distance to the precise
                // stop — not taxi's distance to the route-end node — so the status never
                // contradicts the live docking countdown ("25 m to gate" vs "20 m to stop").
                if (dockingGuidanceManager.IsActive)
                {
                    string dockStatus = dockingGuidanceManager.GetStatusAnnouncement();
                    announcer.AnnounceImmediate(string.IsNullOrEmpty(dockStatus)
                        ? taxiGuidanceManager.GetStatusAnnouncement()
                        : dockStatus);
                }
                else
                {
                    announcer.AnnounceImmediate(taxiGuidanceManager.GetStatusAnnouncement());
                }
                break;
            case HotkeyAction.TaxiRepeat:
                // Ctrl+Y — replays the most recent actionable instruction (turn callout,
                // hold-short, taxiway change, lineup, arrival, etc.) verbatim. Distinct from
                // TaxiStatus: that recomputes a snapshot; this gives back exactly what the
                // pilot just heard, useful when the announcement was clipped by another sound.
                announcer.AnnounceImmediate(taxiGuidanceManager.RepeatLastInstruction());
                break;
            case HotkeyAction.TaxiContinue:
                taxiGuidanceManager.ContinuePastHoldShort();
                break;
            case HotkeyAction.TaxiStop:
                taxiGuidanceManager.StopGuidance();
                dockingGuidanceManager?.SetDestinationGate(null);
                simConnectManager.StopTaxiGuidanceMonitoring();
                announcer.AnnounceImmediate("Taxi guidance stopped.");
                break;
            case HotkeyAction.TaxiWhereAmI:
                AnnounceWhereAmI();
                break;
            case HotkeyAction.AnnounceGroundTraffic:
                groundTrafficMonitor.AnnounceNearestTrafficSummary();
                break;
            case HotkeyAction.LandingExitPlanner:
                ShowLandingExitForm();
                break;
            case HotkeyAction.ShowAccessGSX:
                ShowAccessGSXForm();
                break;
            case HotkeyAction.ReadGsxTooltip:
                ReadLatestGsxTooltip();
                break;
            // Note: FCU push/pull, autopilot toggles, FCU set value dialogs, and A32NX-specific hotkeys
            // are now handled by the aircraft definition via HandleHotkeyAction()
        }
    }

    /// <summary>
    /// Open (or refocus) the Access GSX form. The underlying GsxService runs
    /// from connect-time, independently of this form, so the form is just a
    /// UI surface for the existing connection.
    /// </summary>
    private void ShowAccessGSXForm()
    {
        if (_gsxService == null)
        {
            announcer.AnnounceImmediate("Access GSX: service not initialized.");
            return;
        }

        if (!_gsxService.IsConnected)
        {
            announcer.AnnounceImmediate("Access GSX: not connected to the simulator.");
            return;
        }

        if (_accessGsxForm == null || _accessGsxForm.IsDisposed)
        {
            _accessGsxForm = new Forms.AccessGSXForm(_gsxService, announcer);
        }

        // Show ownerless so the window is an independent top-level — MainForm
        // stays usable, and the GSX window gets its own taskbar entry. The
        // brief TopMost flash brings it to the foreground without keeping it
        // pinned (same pattern as HS787FMCForm.ShowForm).
        if (!_accessGsxForm.Visible)
            _accessGsxForm.Show();
        _accessGsxForm.TopMost = true;
        _accessGsxForm.TopMost = false;
        _accessGsxForm.BringToFront();
        _accessGsxForm.Activate();
    }

    /// <summary>
    /// Output Ctrl+G: speak the most recent GSX tooltip without opening the
    /// AccessGSX window. The GsxService keeps the last tooltip cached for the
    /// duration of the SimConnect connection, so this works whether or not the
    /// AccessGSX form has been opened this session.
    /// </summary>
    private void ReadLatestGsxTooltip()
    {
        if (_gsxService == null || !_gsxService.IsConnected)
        {
            announcer.AnnounceImmediate("Access GSX: not connected to the simulator.");
            return;
        }
        _gsxService.RefreshTooltip();
        string tooltip = _gsxService.LastTooltip;
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            announcer.AnnounceImmediate("No GSX tooltip yet.");
            return;
        }
        announcer.AnnounceImmediate(tooltip);
    }

    private void OnOutputHotkeyModeChanged(object? sender, HotkeyModeEventArgs e)
    {
        if (e.Status == HotkeyModeStatus.Activated)
            announcer.AnnounceImmediate("output");
        else if (e.Status == HotkeyModeStatus.Cancelled)
            announcer.AnnounceImmediate("cancelled");
    }

    private void OnInputHotkeyModeChanged(object? sender, HotkeyModeEventArgs e)
    {
        if (e.Status == HotkeyModeStatus.Activated)
            announcer.AnnounceImmediate("input");
        else if (e.Status == HotkeyModeStatus.Cancelled)
            announcer.AnnounceImmediate("cancelled");
    }

    /// <summary>
    /// Validates that the selected database matches the running simulator version
    /// </summary>
    /// <returns>True if validation passes or user chooses to continue anyway, false otherwise</returns>
    private bool ValidateDatabaseSimulatorMatch()
    {
        // If no database provider, skip validation
        if (airportDataProvider == null)
            return true;

        string simVersion = simConnectManager.DetectedSimulatorVersion;
        string dbType = airportDataProvider.DatabaseType;

        // Unknown simulator version - allow operation
        if (simVersion == "Unknown")
            return true;

        bool isFs2024Sim = simVersion.Contains("2024");
        bool isFs2024Db = dbType.Contains("FS2024");

        // Check for mismatch
        if (isFs2024Sim && !isFs2024Db)
        {
            // FS2024 sim with FS2020 database
            var result = DatabaseMismatchDialog.ShowMismatchWarning("FS2024", dbType);

            if (result == DialogResult.Yes)
            {
                // Open database settings
                DatabaseSettingsMenuItem_Click(null, EventArgs.Empty);
                return false; // Cancel teleport
            }
            else if (result == DialogResult.Cancel)
            {
                return false; // Cancel teleport
            }
            // If "No", continue anyway
        }
        else if (!isFs2024Sim && isFs2024Db)
        {
            // FS2020 sim with FS2024 database
            var result = DatabaseMismatchDialog.ShowMismatchWarning("FS2020", dbType);

            if (result == DialogResult.Yes)
            {
                DatabaseSettingsMenuItem_Click(null, EventArgs.Empty);
                return false;
            }
            else if (result == DialogResult.Cancel)
            {
                return false;
            }
        }

        return true; // Match is valid or user chose to continue
    }

    private void ShowRunwayTeleportDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. Configure database from File menu first.");
            return;
        }

        // Validate database matches simulator
        if (!ValidateDatabaseSimulatorMatch())
            return;

        var dialog = new RunwayTeleportForm(airportDataProvider, announcer);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.SelectedRunway != null && dialog.SelectedAirport != null)
            {
                simConnectManager.TeleportToRunway(dialog.SelectedRunway, dialog.SelectedAirport);
            }
        }
    }

    private void ShowGateTeleportDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. Configure database from File menu first.");
            return;
        }

        // Validate database matches simulator
        if (!ValidateDatabaseSimulatorMatch())
            return;

        var dialog = new GateTeleportForm(airportDataProvider, announcer, simConnectManager.AircraftWingSpan, BuildGateDataSource());
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.SelectedParkingSpot != null && dialog.SelectedAirport != null)
            {
                simConnectManager.TeleportToParkingSpot(dialog.SelectedParkingSpot, dialog.SelectedAirport);
            }
        }
    }

    private void ShowLocationInfoDialog()
    {
        // Deactivate output hotkey mode before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator. Cannot get location information.");
            return;
        }

        try
        {
            announcer.AnnounceImmediate("Requesting aircraft position...");

            simConnectManager.RequestAircraftPositionAsync((position) =>
            {
                // This callback runs when position data is received
                try
                {
                    var locationForm = new Forms.LocationInfoForm(position.Latitude, position.Longitude, announcer);
                    locationForm.Show();
                }
                catch (Exception ex)
                {
                    announcer.AnnounceImmediate($"Error displaying location information: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[MainForm] Error in position callback: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Error requesting location information: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error in ShowLocationInfoDialog: {ex.Message}");
        }
    }


    private void OpenWeatherRadarWindow()
    {
        try
        {
            if (weatherRadarForm == null || weatherRadarForm.IsDisposed)
                weatherRadarForm = new Forms.WeatherRadarForm(announcer, simConnectManager);
            weatherRadarForm.ShowForm();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error opening weather radar: {ex.Message}");
        }
    }



    private void OpenTcasWindow()
    {
        try
        {
            if (tcasForm == null || tcasForm.IsDisposed)
            {
                var gateResolver = new Services.GateResolver(Database.DatabaseSelector.SelectProvider());
                tcasForm = new Forms.TcasForm(tcasService!, announcer, gateResolver);
            }
            tcasForm.ShowForm();
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Error opening TCAS: {ex.Message}");
        }
    }

    private async void AnnounceTrackedTcasTraffic()
    {
        if (tcasService == null || !tcasService.HasTracked)
        {
            announcer.AnnounceImmediate("No tracked aircraft. Add aircraft to track list from the TCAS window.");
            return;
        }

        // Kick off a fresh poll so SimConnect returns the latest positions.
        // Wait ~600 ms for responses to arrive before reading announcements.
        tcasService.PollNow();
        await Task.Delay(600);

        var items = tcasService.GetTrackedAnnouncements();
        announcer.AnnounceImmediate(string.Join(". ", items));
    }

    private void OpenSimBriefBriefing()
    {
        try
        {
            announcer.AnnounceImmediate("Opening your SimBrief briefing");
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://dispatch.simbrief.com/briefing/latest",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Error opening SimBrief briefing: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error in OpenSimBriefBriefing: {ex.Message}");
        }
    }

    private void ShowDestinationRunwayDialog()
    {
        // Ensure output hotkey mode is deactivated before showing modal dialog
        hotkeyManager.ExitOutputHotkeyMode();

        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. Configure database from File menu first.");
            return;
        }

        // Validate database matches simulator
        if (!ValidateDatabaseSimulatorMatch())
            return;

        var dialog = new DestinationRunwayForm(airportDataProvider, announcer);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.SelectedRunway != null && dialog.SelectedAirport != null)
            {
                simConnectManager.SetDestinationRunway(dialog.SelectedRunway, dialog.SelectedAirport);
                // Destination just set → force-fresh the online taxiway names + gate aliases NOW, so
                // they're ready well before the approach (instead of waiting until ILS guidance or the
                // landing-exit planner is opened).
                if (_augmentPrefetched.Add(dialog.SelectedAirport.ICAO))
                    _ = _augmentingProvider?.PrefetchAsync(dialog.SelectedAirport.ICAO, force: true);
                announcer.AnnounceImmediate($"Destination runway set: {dialog.SelectedAirport.ICAO} Runway {dialog.SelectedRunway.RunwayID}");
            }
        }
    }

    private void ShowMETARReportDialog()
    {
        // Ensure output hotkey mode is deactivated before showing window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new METARReportForm(announcer);
        dialog.ShowForm();
    }

    private void ShowColdTempCorrectionDialog()
    {
        // Ensure output hotkey mode is deactivated before showing the window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new ColdTemperatureCorrectionForm(announcer);
        dialog.ShowForm();
    }

    private void ShowChecklistDialog()
    {
        // Ensure output hotkey mode is deactivated before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // Shift+C opens the static text checklist (same for every aircraft, including
        // the A380). The A380's LIVE Electronic Checklist is on its own key,
        // Ctrl+Shift+C (ShowChecklistECLDialog).
        if (checklistForm == null || checklistForm.IsDisposed)
        {
            checklistForm = new ChecklistForm(announcer, currentAircraft.AircraftCode);
        }

        // Show the form (reuses same instance to preserve checkbox states)
        checklistForm.ShowForm();
    }

    // Ctrl+Shift+C on the A380: the LIVE Electronic Checklist (ECL) read from the
    // E/WD — the real normal checklists + active ECAM procedures, with sensed
    // auto-completion. A380-only; other aircraft have no ECL to drive.
    private void ShowChecklistECLDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();

        if (currentAircraft?.AircraftCode != "FBW_A380")
        {
            announcer.AnnounceImmediate("The live Electronic Checklist is only on the A380. Use Shift+C for the text checklist.");
            return;
        }
        // The live ECL reads through the SHARED A380X_EWD monitor connection (only
        // one Coherent inspector socket per page is allowed). Ensure it's running.
        if (coherentEWDClient == null) StartA380EWDMonitor();
        if (fbwA380ChecklistForm == null || fbwA380ChecklistForm.IsDisposed)
            fbwA380ChecklistForm = new Forms.FBWA380.FBWA380ChecklistForm(announcer, simConnectManager, coherentEWDClient);
        fbwA380ChecklistForm.Show();
        fbwA380ChecklistForm.BringToFront();
        fbwA380ChecklistForm.Activate();
    }

    public void ShowFenixMonitorManagerDialog()
    {
        // Deactivate output hotkey mode before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // Create form if it doesn't exist or has been disposed
        if (fenixMonitorManagerForm == null || fenixMonitorManagerForm.IsDisposed)
        {
            fenixMonitorManagerForm = new FenixMonitorManagerForm(currentAircraft.GetVariables());
        }

        // Show the form (reuses same instance to preserve state)
        fenixMonitorManagerForm.ShowForm();
    }

    public void ShowA380MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwA380MonitorManagerForm == null || fbwA380MonitorManagerForm.IsDisposed)
        {
            fbwA380MonitorManagerForm = new Forms.FBWA380.FBWA380MonitorManagerForm(
                announcer, currentAircraft.GetVariables());
        }
        fbwA380MonitorManagerForm.ShowForm();
    }

    public void ShowA320MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwA320MonitorManagerForm == null || fbwA320MonitorManagerForm.IsDisposed)
        {
            fbwA320MonitorManagerForm = new Forms.FlyByWireA320.FlyByWireA320MonitorManagerForm(
                announcer, currentAircraft.GetVariables());
        }
        fbwA320MonitorManagerForm.ShowForm();
    }

    public void ShowHS787MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (hs787MonitorManagerForm == null || hs787MonitorManagerForm.IsDisposed)
        {
            hs787MonitorManagerForm = new Forms.HS787.HS787MonitorManagerForm(currentAircraft.GetVariables());
        }
        hs787MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// Public accessor for the PROG-page monitor. PMDG777Definition's distance
    /// handlers read its <see cref="PMDGProgPageMonitor.LastProgData"/> when
    /// Enhanced distance mode is on. Returns null when the monitor isn't
    /// running (non-PMDG aircraft or Enhanced mode off).
    /// </summary>
    public MSFSBlindAssist.Services.PMDGProgPageMonitor? GetPMDGProgPageMonitor() => pmdgProgPageMonitor;

    /// <summary>
    /// Starts or stops the PROG-page monitor to match current state. Called
    /// at startup, on aircraft swap, and after the FMC Settings dialog
    /// closes with OK. The monitor only runs when both conditions hold:
    /// (a) a PMDG aircraft is loaded, (b) Enhanced distance mode is on.
    /// </summary>
    private void EnsurePMDGProgPageMonitor()
    {
        bool wantRunning = currentAircraft != null
            && currentAircraft.AircraftCode.StartsWith("PMDG_", StringComparison.Ordinal)
            && Settings.SettingsManager.Current.PMDGEnhancedDistanceMode;

        if (wantRunning)
        {
            // Lazy-create on first need. Recreated whenever the
            // PMDG data manager changes (e.g., after aircraft swap)
            // because the monitor holds a reference to a specific
            // data-manager instance.
            // The PROG-page monitor is currently 777-specific; cast
            // through the interface slot. Non-777 PMDG aircraft will
            // need their own monitor wiring (Phase D).
            var dm = simConnectManager?.PMDGDataManager as PMDG777DataManager;
            if (dm == null) return;
            if (pmdgProgPageMonitor == null)
            {
                pmdgProgPageMonitor = new MSFSBlindAssist.Services.PMDGProgPageMonitor(dm);
            }
            if (!pmdgProgPageMonitor.IsRunning)
            {
                pmdgProgPageMonitor.Start();
            }
        }
        else if (pmdgProgPageMonitor != null)
        {
            pmdgProgPageMonitor.Stop();
        }
    }

    public void ShowPMDGAnnouncementMonitorDialog()
    {
        // Deactivate output hotkey mode before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // The form snapshots the variables dictionary at construction time,
        // so we recreate it whenever the loaded aircraft might have changed.
        // CleanupAircraftSpecificForms() disposes this on aircraft swap, so
        // a stale instance from the previous aircraft never lingers.
        if (pmdgAnnouncementMonitorForm == null || pmdgAnnouncementMonitorForm.IsDisposed)
        {
            pmdgAnnouncementMonitorForm = new PMDGAnnouncementMonitorForm(announcer, currentAircraft.GetVariables());
        }

        pmdgAnnouncementMonitorForm.ShowForm();
    }

    private void ShowFenixMCDUDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        // Create service if it doesn't exist
        if (fenixMCDUService == null)
        {
            fenixMCDUService = new FenixMCDUService();
            fenixMCDUService.Connect();
        }

        // Create form if it doesn't exist or has been disposed
        if (fenixMCDUForm == null || fenixMCDUForm.IsDisposed)
        {
            fenixMCDUForm = new FenixMCDUForm(fenixMCDUService, announcer);
        }

        // Show the form (reuses same instance to preserve state)
        fenixMCDUForm.ShowForm();
    }

    private void ShowFenixEFBDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        // Reuse a single instance; recreate after it has been disposed (close / swap).
        if (fenixEFBForm == null || fenixEFBForm.IsDisposed)
        {
            fenixEFBForm = new Forms.Fenix.FenixEFBForm(announcer);
        }

        fenixEFBForm.ShowForm();
    }

    private void ShowFlyByWireMCDUDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (flyByWireMCDUService == null)
        {
            flyByWireMCDUService = new MSFSBlindAssist.Services.FlyByWireMCDUService();
            flyByWireMCDUService.Connect();
        }

        if (flyByWireMCDUForm == null || flyByWireMCDUForm.IsDisposed)
        {
            flyByWireMCDUForm = new MSFSBlindAssist.Forms.FlyByWireA320.FlyByWireMCDUForm(flyByWireMCDUService, announcer);
        }

        flyByWireMCDUForm.ShowForm();
    }

    private void ShowPMDGCDUDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (simConnectManager?.PMDGDataManager == null) return;

        // Create form if it doesn't exist or has been disposed.
        // Dispatch by aircraft code: the 777 form takes a concrete
        // PMDG777DataManager (cast through the abstraction); the 737
        // form accepts IPMDGDataManager directly.
        if (pmdgCDUForm == null || pmdgCDUForm.IsDisposed)
        {
            if (currentAircraft?.AircraftCode == "PMDG_737")
            {
                pmdgCDUForm = new PMDG737CDUForm(simConnectManager.PMDGDataManager, announcer);
            }
            else
            {
                pmdgCDUForm = new PMDG777CDUForm((PMDG777DataManager)simConnectManager.PMDGDataManager, announcer);
            }
        }

        // Show the form (reuses same instance to preserve state)
        switch (pmdgCDUForm)
        {
            case PMDG737CDUForm f737: f737.ShowForm(); break;
            case PMDG777CDUForm f777: f777.ShowForm(); break;
        }
    }

    /// <summary>
    /// Speaks FMS flight progress for the A380 D / Shift+D hotkeys. The numbers come
    /// from the FMS guidance controller in the MFD page (no stock SimVar exposes
    /// them), read via the Coherent debugger. <paramref name="tod"/> selects Top of
    /// Descent (Shift+D) vs distance to destination (D). Async fire-and-forget; the
    /// announcement lands when the eval returns.
    /// </summary>
    public async void AnnounceA380FlightInfo(bool tod)
    {
        if (coherentClient == null) { announcer.AnnounceImmediate("Flight info unavailable."); return; }
        string raw = "";
        try { raw = await coherentClient.EvalForResultAsync("window.__MSFSBA_A380 ? __MSFSBA_A380.flightInfo() : ''"); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[A380 flightInfo] {ex.Message}"); }
        AnnounceFlightInfoJson(raw, tod);
    }

    /// <summary>
    /// A32NX equivalent. The A320 has no D/Shift+D path of its own and drives its MCDU over
    /// the SimBridge relay (not the Coherent MCDU bridge), so we read its FMS guidanceController
    /// directly via a ONE-SHOT Coherent eval of the self-contained coherent-a32nx-flightinfo.js,
    /// then announce identically to the A380 (PMDG-format TOD).
    /// </summary>
    public async void AnnounceA32NXFlightInfo(bool tod)
    {
        string js = LoadA32NXFlightInfoJs();
        if (string.IsNullOrEmpty(js)) { announcer.AnnounceImmediate("Flight info unavailable."); return; }
        string raw = "";
        try { raw = await SimConnect.CoherentEvalClient.EvalAsync("A32NX_MCDU", js); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[A32NX flightInfo] {ex.Message}"); }
        AnnounceFlightInfoJson(raw, tod);
    }

    private string? _a32nxFlightInfoJs;
    private string LoadA32NXFlightInfoJs()
    {
        if (_a32nxFlightInfoJs == null)
        {
            try { _a32nxFlightInfoJs = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-a32nx-flightinfo.js")); }
            catch { _a32nxFlightInfoJs = ""; }
        }
        return _a32nxFlightInfoJs;
    }

    // Parse the flightInfo JSON (same shape for the A380 + A32NX) and speak the D/Shift+D
    // readout. Shared so both FBW jets announce identically (PMDG-format TOD).
    private void AnnounceFlightInfoJson(string raw, bool tod)
    {
        try
        {
            if (string.IsNullOrEmpty(raw)) { announcer.AnnounceImmediate("Flight management not ready."); return; }

            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ok", out var okEl) || okEl.ValueKind != System.Text.Json.JsonValueKind.True)
            {
                announcer.AnnounceImmediate("Flight management not ready.");
                return;
            }

            double? Num(string key) =>
                r.TryGetProperty(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? e.GetDouble() : (double?)null;

            if (tod)
            {
                double? td = Num("distToTD");
                double? tc = Num("distToTC");
                double? tdSecs = Num("timeToTD");   // FMS time-to-go (seconds), null until computed
                double? phase = Num("flightPhase");  // FMGC phase: >=4 = descent/approach/… = past TOD
                // Past TOD once descending — the robust PMDG-parity signal (PMDG keys off
                // FMC_DistanceToTOD going negative; the A380's (T/D) pseudo-waypoint just
                // disappears, so its distance/time can read stale — phase is authoritative).
                bool pastTod = phase.HasValue && phase.Value >= 4 && phase.Value <= 7;
                if (pastTod || (td.HasValue && td.Value <= 0.5))
                    announcer.AnnounceImmediate("Past top of descent");
                else if (td.HasValue)
                {
                    // Match the PMDG TOD readout format exactly:
                    // "145 miles to top of descent: 00:16:58" (time from the FMS).
                    string eta = tdSecs.HasValue ? FormatEtaSeconds(tdSecs.Value) : "";
                    announcer.AnnounceImmediate($"{Math.Round(td.Value)} miles to top of descent{eta}");
                }
                else if (tc.HasValue && tc.Value > 0.5)
                    announcer.AnnounceImmediate($"{Math.Round(tc.Value)} miles to top of climb");
                else
                    announcer.AnnounceImmediate("Top of descent not yet computed");
            }
            else
            {
                double? dd = Num("distToDest");
                double? ddSecs = Num("timeToDest");   // FMS time-to-go (seconds), null if the profile hasn't computed it
                if (dd.HasValue && dd.Value >= 0)
                {
                    // Match the TOD readout format: "1355 miles to destination: 02:54:33"
                    // (the ": HH:MM:SS" suffix is omitted when the FMS supplies no time).
                    string eta = ddSecs.HasValue ? FormatEtaSeconds(ddSecs.Value) : "";
                    announcer.AnnounceImmediate($"{Math.Round(dd.Value)} miles to destination{eta}");
                }
                else
                    announcer.AnnounceImmediate("Destination distance not available");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[flightInfo] {ex.Message}");
            announcer.AnnounceImmediate("Flight info error.");
        }
    }

    // ": HH:MM:SS" suffix for the A380 TOD readout — identical to the PMDG TOD
    // format (PMDG737Definition.FormatEtaFromDistance). Empty when there's no time.
    private static string FormatEtaSeconds(double seconds)
    {
        if (seconds <= 0) return "";
        int totalSeconds = (int)Math.Round(seconds);
        int hh = totalSeconds / 3600;
        int mm = (totalSeconds % 3600) / 60;
        int ss = totalSeconds % 60;
        return $": {hh:D2}:{mm:D2}:{ss:D2}";
    }

    private void ShowFBWA380MCDUDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        if (coherentClient == null) { coherentClient = new CoherentDebuggerClient(); coherentClient.Start(); }
        IMcduBridge bridge = coherentClient;

        if (fbwA380MCDUForm == null || fbwA380MCDUForm.IsDisposed)
        {
            fbwA380MCDUForm = new Forms.FBWA380.FBWA380MCDUForm(
                bridge, announcer,
                currentAircraft as Aircraft.FlyByWireA380Definition);
            // Idle-gate the 350 ms MFD scrape to the window's visibility. The form hides
            // (not closes) on user-close, so VisibleChanged fires on every open/close; the
            // form and client are both disposed on aircraft swap, so the closure can't
            // dangle. The connection itself stays warm for D / Shift+D flight info.
            var form = fbwA380MCDUForm;
            form.VisibleChanged += (_, _) => coherentClient?.SetActive(!form.IsDisposed && form.Visible);
        }
        coherentClient.SetActive(true);   // covers the already-visible re-Show path (no VisibleChanged)
        fbwA380MCDUForm.ShowForm();
    }

    private void ShowFbwEfbDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        if (coherentEFBClient == null) { coherentEFBClient = new CoherentEFBClient(); coherentEFBClient.Start(); }
        IMcduBridge bridge = coherentEFBClient;

        if (fbwEfbForm == null || fbwEfbForm.IsDisposed)
        {
            // One generic flyPad form serves both FBW aircraft; only the window
            // title differs. The form is disposed on aircraft swap (see the swap
            // handler), so it is always recreated with the correct title.
            string title = currentAircraft?.AircraftCode == "A320"
                ? "A320 flyPad EFB" : "A380X flyPad EFB";
            fbwEfbForm = new Forms.FBWA380.FbwEfbForm(bridge, announcer, title, "flyPad");
            // Idle-gate the 600 ms flyPad scrape to the window's visibility (same pattern
            // as the MCDU window above); the connection + powerOn handshake stay warm.
            var form = fbwEfbForm;
            form.VisibleChanged += (_, _) => coherentEFBClient?.SetActive(!form.IsDisposed && form.Visible);
        }
        coherentEFBClient.SetActive(true);   // covers the already-visible re-Show path (no VisibleChanged)
        fbwEfbForm.ShowForm();
    }

    private void PMDG777FirstOfficerMenuItem_Click(object? sender, EventArgs e)
    {
        ShowPMDG777FirstOfficerDialog();
    }

    private void ShowPMDG777FirstOfficerDialog()
    {
        if (pmdg777FirstOfficerForm == null || pmdg777FirstOfficerForm.IsDisposed)
        {
            pmdg777FirstOfficerForm = new Forms.FirstOfficer.FirstOfficerForm<FirstOfficer.AircraftActionExecutor, FirstOfficer.AircraftStateEvaluator>(
                new FirstOfficer.Pmdg777FoProfile(),
                simConnectManager,
                announcer,
                MSFSBlindAssist.Settings.SettingsManager.Current,
                new MSFSBlindAssist.Services.SimBriefService());
        }
        pmdg777FirstOfficerForm.ShowForm();
    }

    private void PMDG737FirstOfficerMenuItem_Click(object? sender, EventArgs e)
    {
        ShowPMDG737FirstOfficerDialog();
    }

    private void ShowPMDG737FirstOfficerDialog()
    {
        if (pmdg737FirstOfficerForm == null || pmdg737FirstOfficerForm.IsDisposed)
        {
            pmdg737FirstOfficerForm = new Forms.FirstOfficer.FirstOfficerForm<FirstOfficer.PMDG737.AircraftActionExecutor, FirstOfficer.PMDG737.AircraftStateEvaluator>(
                new FirstOfficer.PMDG737.Pmdg737FoProfile(),
                simConnectManager,
                announcer,
                MSFSBlindAssist.Settings.SettingsManager.Current,
                new MSFSBlindAssist.Services.SimBriefService());
        }
        pmdg737FirstOfficerForm.ShowForm();
    }

    private void FOSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        using var dlg = new FirstOfficerSettingsForm(settings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            MSFSBlindAssist.Settings.SettingsManager.Save();
            // Push updated settings into the form if it's already open
            if (pmdg777FirstOfficerForm != null && !pmdg777FirstOfficerForm.IsDisposed)
                pmdg777FirstOfficerForm.ApplySettings();
            if (pmdg737FirstOfficerForm != null && !pmdg737FirstOfficerForm.IsDisposed)
                pmdg737FirstOfficerForm.ApplySettings();
        }
    }

    // PMDG 737/777 EFB tablet over the Coherent debugger — one client + window per
    // crew side, reusing the generic FbwEfbForm. Mirrors ShowFbwEfbDialog's lazy
    // client/form creation + non-modal ShowForm pattern.
    private void ShowPmdgCoherentEfbDialog(bool firstOfficer)
    {
        hotkeyManager.ExitInputHotkeyMode();
        string side = firstOfficer ? "FO" : "CA";
        string title = (currentAircraft?.AircraftCode == "PMDG_737" ? "PMDG 737 EFB" : "PMDG 777 EFB") + (firstOfficer ? " (First Officer)" : "");

        if (firstOfficer)
        {
            if (coherentPmdgEfbFirstOfficer == null) { coherentPmdgEfbFirstOfficer = new CoherentPmdgEfbClient(side); coherentPmdgEfbFirstOfficer.Start(); }
            if (pmdgCoherentEfbFirstOfficerForm == null || pmdgCoherentEfbFirstOfficerForm.IsDisposed)
            {
                pmdgCoherentEfbFirstOfficerForm = new Forms.FBWA380.FbwEfbForm(coherentPmdgEfbFirstOfficer, announcer, title, "EFB", "Universal Flight Tablet");
                // Idle-gate the 600 ms tablet scrape to the window's visibility (same pattern as
                // the flyPad form above); the inspector socket + installed agent stay warm. Without
                // this the scrape runs forever after the first open until aircraft swap.
                var foForm = pmdgCoherentEfbFirstOfficerForm;
                foForm.VisibleChanged += (_, _) => coherentPmdgEfbFirstOfficer?.SetActive(!foForm.IsDisposed && foForm.Visible);
            }
            coherentPmdgEfbFirstOfficer.SetActive(true);   // covers the already-visible re-Show path (no VisibleChanged)
            pmdgCoherentEfbFirstOfficerForm.ShowForm();
        }
        else
        {
            if (coherentPmdgEfbCaptain == null) { coherentPmdgEfbCaptain = new CoherentPmdgEfbClient(side); coherentPmdgEfbCaptain.Start(); }
            if (pmdgCoherentEfbCaptainForm == null || pmdgCoherentEfbCaptainForm.IsDisposed)
            {
                pmdgCoherentEfbCaptainForm = new Forms.FBWA380.FbwEfbForm(coherentPmdgEfbCaptain, announcer, title, "EFB", "Universal Flight Tablet");
                var caForm = pmdgCoherentEfbCaptainForm;
                caForm.VisibleChanged += (_, _) => coherentPmdgEfbCaptain?.SetActive(!caForm.IsDisposed && caForm.Visible);
            }
            coherentPmdgEfbCaptain.SetActive(true);
            pmdgCoherentEfbCaptainForm.ShowForm();
        }
    }

    // A380 ND OANS / BTV control panel — reuses the WebView2 EFB form, but driven
    // by the ND Coherent view through CoherentNDClient. Used for BTV (Brake-To-
    // Vacate) exit selection and airport/runway/exit search.
    // Open the accessible A380 RMP window (Ctrl+Shift+R in input mode) — replaces the old
    // per-key RMP button panel. Scrapes A380X_RMP_1/2 live; one window, Captain ↔ FO combo.
    private void ShowA32NXDcduDialog()
    {
        // Opened by an INPUT-mode hotkey — release the mode hotkeys so the
        // form's Ctrl+1/2 / Alt+1/2 soft keys and PageUp/Down navigation reach
        // the window instead of the global registrations (RMP/OANS precedent).
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwDcduForm == null || fbwDcduForm.IsDisposed)
        {
            fbwDcduForm = new Forms.FlyByWireA320.FlyByWireDcduForm(announcer, simConnectManager);
        }
        fbwDcduForm.Show();
        fbwDcduForm.BringToFront();
        fbwDcduForm.Activate();
    }

    private void ShowFBWA380RmpDialog()
    {
        if (currentAircraft is not FlyByWireA380Definition a380rmp) return;
        // CRITICAL: release the mode hotkeys before showing. The RMP window is opened by an
        // INPUT-mode hotkey (Ctrl+Shift+R), so input mode is still active and its global
        // RegisterHotKey shortcuts (Ctrl+1/2/3 = FCU pulls, Alt+n, digits via Track Slots,
        // etc.) would be consumed system-wide and NEVER reach the RMP window — making the
        // RMP soft keys, page switching and digit entry all appear dead. Exiting both modes
        // unregisters those, so every keystroke flows to the form. (Mirrors the OANS dialog.)
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwA380RmpForm == null || fbwA380RmpForm.IsDisposed)
        {
            fbwA380RmpForm = new Forms.FBWA380.FBWA380RmpForm(announcer, a380rmp, simConnectManager);
        }
        fbwA380RmpForm.Show();
        fbwA380RmpForm.BringToFront();
        fbwA380RmpForm.Activate();
    }

    private void ShowFBWA380OansDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();

        if (coherentNDClient == null) { coherentNDClient = new CoherentNDClient(); coherentNDClient.Start(); }

        if (fbwA380OansForm == null || fbwA380OansForm.IsDisposed)
        {
            fbwA380OansForm = new Forms.FBWA380.FBWA380OansForm(coherentNDClient, announcer);
        }
        fbwA380OansForm.ShowForm();
    }

    // Start the background A380X E/WD failure monitor. The sensed abnormal/warning
    // PROCEDURES (failure titles + ECAM action items) have NO SimVar — the FwsCore
    // publishes them on an in-process EventBus and only the E/WD instrument renders
    // them — so they are scraped from the E/WD Coherent view and announced here.
    // Memos (PARK BRK, etc.) are NOT announced by this client; the SimVar EWD_LOWER
    // path already covers them.
    private void StartA380EWDMonitor()
    {
        if (coherentEWDClient != null) return;
        // Hand E/WD call-outs to the scrape: suppress the SimVar EWD_LOWER memo
        // auto-announce so failures AND memos come from the one DOM source.
        if (currentAircraft is FlyByWireA380Definition a380def) a380def.EwdScrapeHandlesAnnounce = true;
        coherentEWDClient = new CoherentEWDClient();
        // Let the SD "Upper E/WD" page read the live E/WD content through this one shared
        // socket (a second client on A380X_EWD is rejected — one inspector per page).
        if (currentAircraft is FlyByWireA380Definition a380ewd) a380ewd.EwdMonitor = coherentEWDClient;
        coherentEWDClient.LineAnnounced += line =>
        {
            // Honour the Ctrl+M / Ctrl+E ECAM-monitor mute (same sentinel the
            // SimVar EWD memo path consults), so the user can silence E/WD chatter.
            if (Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(
                    Forms.FBWA380.FBWA380MonitorManagerForm.EcamMemosKey))
                return;
            // Audio dedup: skip a memo the FwsFailureClient already calls out as an active
            // warning (e.g. XPDR STBY — amber in the FWS list AND green in the memos).
            if (currentAircraft is FlyByWireA380Definition a380dd && a380dd.IsTextAnActiveWarning(line))
                return;
            announcer.Announce(line);
        };
        coherentEWDClient.Start();

        // Authoritative failure announcer — reads the FwsCore (presentedFailures) directly,
        // so a master caution always names its cause even for WIP procedures the E/WD DOM
        // doesn't render (ENG 3/4 FAIL). It OWNS failure call-outs; the DOM scrape above
        // therefore stops announcing warning lines (no double-speak) but keeps memos/PFD/
        // status. The live list is pushed into the A380 def for the displays panel.
        coherentEWDClient.AnnounceWarnings = false;
        coherentFwsFailureClient = new CoherentFwsFailureClient();
        coherentFwsFailureClient.FailureAnnounced += line =>
        {
            if (Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(
                    Forms.FBWA380.FBWA380MonitorManagerForm.EcamMemosKey))
                return;
            announcer.Announce(line);
        };
        coherentFwsFailureClient.FailuresChanged += (ewd, status) =>
        {
            if (currentAircraft is FlyByWireA380Definition a380f) a380f.SetActiveFwsFailures(ewd, status);
        };
        coherentFwsFailureClient.Start();
    }

    private void StopA380EWDMonitor(IAircraftDefinition? owner = null)
    {
        // `owner` lets the aircraft-swap cleanup clear the flag on the OUTGOING def —
        // by the time the cleanup runs, currentAircraft is already the NEW aircraft,
        // so the no-arg form would silently no-op when leaving the A380.
        if ((owner ?? currentAircraft) is FlyByWireA380Definition a380def) a380def.EwdScrapeHandlesAnnounce = false;
        coherentFwsFailureClient?.Dispose();
        coherentFwsFailureClient = null;
        if (coherentEWDClient == null) return;
        coherentEWDClient.Dispose();
        coherentEWDClient = null;
    }


    private void ShowHS787FMCDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        // The CDU now reads + drives over the Coherent debugger (HSB789_MFD_3) — no HTTP
        // bridge server, no injected JS, no mod-package HTML patching required.
        if (hs787FMCForm == null || hs787FMCForm.IsDisposed)
        {
            hs787FMCForm = new HS787FMCForm(simConnectManager, announcer);
        }

        hs787FMCForm.ShowForm();
    }

    // Start the always-on HS787 Coherent monitors (idempotent): the IRS-alignment reader (writes
    // MSFSBA_IRS_ALIGN_STATE/_MINUTES from the PFD view) and the EICAS CAS alert monitor (announces
    // new cautions/warnings from the MFD_1 view).
    private void StartHS787IrsMonitor()
    {
        if (hs787IrsClient == null)
        {
            hs787IrsClient = new SimConnect.CoherentHS787IrsClient();
            hs787IrsClient.Start();
        }
        if (hs787CasClient == null)
        {
            hs787CasClient = new SimConnect.CoherentHS787CasClient();
            hs787CasClient.Start();
        }
    }

    // Open the EICAS alert window on demand (the HS787 Alt+E key). A navigable read-only window
    // (arrow keys to read every active warning/caution/advisory, Escape to close), refreshed live —
    // not a one-shot spoken read-back.
    public void AnnounceHs787CasAlerts()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        // The EICAS window reads engine indications + the CAS monitor's alert list. If the monitor
        // isn't up yet (HS787 not fully initialised, or a startup failure left it null), speak a cue
        // rather than returning in total silence — a blind pilot can't otherwise tell whether there
        // are zero alerts or the feature is broken.
        if (hs787CasClient == null)
        {
            announcer.AnnounceImmediate("EICAS not available.");
            return;
        }
        if (hs787EicasForm == null || hs787EicasForm.IsDisposed)
            hs787EicasForm = new Forms.HS787.HS787EicasForm(BuildHs787EicasText);
        hs787EicasForm.ShowForm();
    }

    // Full EICAS read-out for the Alt+E window: the primary/secondary engine indications (per
    // engine N1 / EGT / N2 / oil), fuel + gross weight + TAT, then the live crew-alert list — i.e.
    // what the real 787 EICAS shows, not just the alert messages. Values come from the cached
    // HS787_Eicas* SimVars (see GetVariables); the alerts from the always-on CAS monitor.
    private string BuildHs787EicasText()
    {
        double GV(string k) => simConnectManager?.GetCachedVariableValue(k) ?? 0;
        int Pct(string k) => (int)Math.Round(GV(k) * 100);     // N1/N2 are 0..1 ratios
        int C(string k) => (int)Math.Round(GV(k));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("EICAS");
        sb.AppendLine($"Engine 1 N1: {Pct("HS787_EicasN1_1")}%");
        sb.AppendLine($"Engine 1 EGT: {C("HS787_EicasEGT_1")} C");
        sb.AppendLine($"Engine 1 N2: {Pct("HS787_EicasN2_1")}%");
        sb.AppendLine($"Engine 1 Oil Pressure: {C("HS787_EicasOilP_1")} psi");
        sb.AppendLine($"Engine 1 Oil Temp: {C("HS787_EicasOilT_1")} C");
        sb.AppendLine($"Engine 2 N1: {Pct("HS787_EicasN1_2")}%");
        sb.AppendLine($"Engine 2 EGT: {C("HS787_EicasEGT_2")} C");
        sb.AppendLine($"Engine 2 N2: {Pct("HS787_EicasN2_2")}%");
        sb.AppendLine($"Engine 2 Oil Pressure: {C("HS787_EicasOilP_2")} psi");
        sb.AppendLine($"Engine 2 Oil Temp: {C("HS787_EicasOilT_2")} C");
        sb.AppendLine($"Total Fuel: {C("HS787_EicasFuelKg")} kg");
        sb.AppendLine($"Gross Weight: {Math.Round(GV("HS787_EicasGwKg") / 1000.0, 1)} t");
        sb.AppendLine($"TAT: {C("HS787_EicasTat")} C");
        sb.AppendLine();
        sb.Append(hs787CasClient?.GetAlertsText() ?? "");
        return sb.ToString();
    }

    // Dispose the HS787 CDU / SimBrief / EFB windows + the IRS monitor (e.g. on aircraft swap)
    // so their Coherent debugger connections close. There is no HTTP bridge to stop.
    private void DisposeHS787Forms()
    {
        if (hs787FMCForm != null && !hs787FMCForm.IsDisposed)
        {
            hs787FMCForm.Dispose();
            hs787FMCForm = null;
        }

        if (hs787SimBriefForm != null && !hs787SimBriefForm.IsDisposed)
        {
            hs787SimBriefForm.Dispose();
            hs787SimBriefForm = null;
        }

        hs787IrsClient?.Dispose();
        hs787IrsClient = null;
        if (hs787EicasForm != null && !hs787EicasForm.IsDisposed) hs787EicasForm.Dispose();
        hs787EicasForm = null;
        hs787CasClient?.Dispose();
        hs787CasClient = null;
    }

    private void ShowElectronicFlightBagDialog()
    {
        // Ensure output hotkey mode is deactivated before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // Create form if it doesn't exist or has been disposed
        if (electronicFlightBagForm == null || electronicFlightBagForm.IsDisposed)
        {
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            electronicFlightBagForm = new ElectronicFlightBagForm(flightPlanManager, simConnectManager, announcer, waypointTracker, settings.SimbriefUsername ?? "");
        }

        // Show the form (reuses same instance to preserve flight plan data)
        electronicFlightBagForm.ShowForm();
    }

    /// <summary>
    /// "Where Am I" — tells the pilot which taxiway/runway/gate they're currently on at
    /// the nearest airport. Works whether or not taxi guidance is active. Format:
    /// "Taxiway Bravo at KJFK." / "Gate A25 at KJFK." / "Runway 22L at KJFK."
    /// </summary>
    private void AnnounceWhereAmI()
    {
        if (airportDataProvider == null)
        {
            announcer.AnnounceImmediate("Airport database not available.");
            return;
        }

        // Where Am I is GROUND-ONLY by design: it tells the pilot which gate /
        // taxiway / runway they're sitting on. In flight there's a separate
        // location/city hotkey for that — Where Am I would otherwise just pick
        // the nearest taxiway 4000 ft below, which is misleading. Silence it
        // when airborne. Default _lastOnGround = true means a startup-time
        // query before any SIM_ON_GROUND sample still works on the ramp.
        if (!_lastOnGround)
        {
            announcer.AnnounceImmediate("In flight.");
            return;
        }

        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            string announcement;
            try
            {
                // GetNearbyAirportICAOs may return 3-char idents for small fields with
                // no canonical ICAO (kept for the GateResolver TCAS-gate use case). The
                // taxi-graph lookup needs canonical 4-char ICAOs, so filter here at the
                // call site — do NOT add the filter to the SQL or it breaks GateResolver.
                var nearby = airportDataProvider.GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
                    .Where(c => c != null && c.Length == 4)
                    .ToList();
                if (nearby == null || nearby.Count == 0)
                {
                    announcement = "No airport nearby.";
                }
                else
                {
                    announcement = taxiGuidanceManager.DescribeCurrentLocation(
                        airportDataProvider,
                        nearby[0],
                        position.Latitude,
                        position.Longitude);
                }
            }
            catch (Exception ex)
            {
                announcement = $"Location lookup failed. {ex.Message}";
            }

            if (this.InvokeRequired)
                this.Invoke(() => announcer.AnnounceImmediate(announcement));
            else
                announcer.AnnounceImmediate(announcement);
        });
    }

    private void ShowTaxiAssistForm()
    {
        if (airportDataProvider == null)
        {
            announcer.AnnounceImmediate("Airport database not available. Configure database in settings.");
            return;
        }

        // Ensure input and output hotkey modes are deactivated before showing dialog
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();

        // Get current aircraft position
        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            if (this.InvokeRequired)
            {
                this.Invoke(() => OpenTaxiForm(position));
            }
            else
            {
                OpenTaxiForm(position);
            }
        });
    }

    private Services.GateDataSource? BuildGateDataSource()
    {
        if (airportDataProvider == null) return null;
        // GSX gates only when GSX is running this session (Couatl started) AND a profile matches.
        return new Services.GateDataSource(
            airportDataProvider,
            () => _gsxService != null && _gsxService.CouatlStarted);
    }

    /// <summary>
    /// Constructs a <see cref="Services.Gsx.GsxGateSelector"/> when GSX is
    /// available in this session. Returns <c>null</c> when there is no GSX
    /// service (GSX not installed / not yet started), so callers can simply
    /// null-check before using it.
    /// </summary>
    private Services.Gsx.GsxGateSelector? BuildGsxGateSelector()
    {
        if (_gsxService == null) return null;
        return new Services.Gsx.GsxGateSelector(
            _gsxService,
            new Services.Gsx.GsxMenuAutomation(_gsxService),
            announcer);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the GSX installation folder
    /// (%APPDATA%\Virtuali) exists, indicating GSX is likely installed.
    /// Used to gate the per-ICAO profile rescan: the Refresh() call is only
    /// useful to pick up a gsx.cfg written by GSX to %APPDATA%\Virtuali\Airplanes,
    /// which never happens on a machine without GSX.
    /// </summary>
    private static bool GsxLikelyInstalled()
    {
        try { return System.IO.Directory.Exists(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtuali")); }
        catch { return false; }
    }

    private void OpenTaxiForm(SimConnectManager.AircraftPosition position)
    {
        if (taxiAssistForm == null || taxiAssistForm.IsDisposed)
        {
            taxiAssistForm = new TaxiAssistForm(
                airportDataProvider!, announcer, taxiGuidanceManager, simConnectManager, tcasService,
                simConnectManager.AircraftWingSpan, BuildGateDataSource(), BuildGsxGateSelector(), dockingGuidanceManager);
        }

        // Find nearest airport. Filter to 4-char canonical ICAO at the call site —
        // GetNearbyAirportICAOs may return 3-char idents (used by GateResolver's
        // TCAS lookup). The taxi-graph builder needs canonical ICAOs.
        string nearestIcao = "";
        var nearbyAirports = airportDataProvider!.GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
            .Where(c => c != null && c.Length == 4)
            .ToList();
        if (nearbyAirports.Count > 0)
            nearestIcao = nearbyAirports[0];

        // Task 2 — Departure prefetch: when on the ground and we've resolved a nearest
        // airport, prefetch once per session so taxiway names are cached before taxi starts.
        // SILENT (fire-and-forget, debounced via _augmentPrefetched).
        if (_lastOnGround && !string.IsNullOrEmpty(nearestIcao) && _augmentPrefetched.Add(nearestIcao))
            _ = _augmentingProvider?.PrefetchAsync(nearestIcao, force: true);

        taxiAssistForm.SetAircraftPosition(position.Latitude, position.Longitude, position.HeadingMagnetic, nearestIcao);

        // (StateChanged is subscribed once in InitializeManagers. We deliberately do NOT
        // re-subscribe here — re-subscribing on every form open would either double-fire
        // the handler or, with the -=/+= pattern previously used here, hide the fact
        // that other entry points like the Landing Exit Planner were never wired up.)

        taxiAssistForm.Show();
        taxiAssistForm.BringToFront();
    }

    /// <summary>
    /// Opens the Landing Exit Planner form. Pre-fills the airport + runway from the
    /// pilot's existing ILS destination selection (SimConnectManager.GetDestinationRunway)
    /// so there's no duplicate UI for picking the destination — the pilot only picks the
    /// exit taxiway here.
    /// </summary>
    private void ShowLandingExitForm()
    {
        if (airportDataProvider == null)
        {
            announcer.AnnounceImmediate("Airport database not available. Configure database in settings.");
            return;
        }

        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();

        // Reuse the existing ILS destination selection (already settable via the
        // "select runway as destination" hotkey). If nothing is set, the form still
        // opens empty so the pilot can type an ICAO + pick a runway manually.
        string? presetIcao = null;
        Database.Models.Runway? presetRunway = null;
        if (simConnectManager.HasDestinationRunway())
        {
            presetRunway = simConnectManager.GetDestinationRunway();
            var destAp = simConnectManager.GetDestinationAirport();
            presetIcao = destAp?.ICAO;
            // Task 1 — Destination prefetch (silent, fire-and-forget)
            if (!string.IsNullOrEmpty(presetIcao) && _augmentPrefetched.Add(presetIcao))
                _ = _augmentingProvider?.PrefetchAsync(presetIcao, force: true);
        }

        // Always rebuild the form so the preset (ICAO + runway from the current
        // ILS destination selection) is fresh. The preset is only consumed by
        // the constructor/Load handler; reusing a prior instance would show
        // stale values if the user changed ILS destination between opens.
        if (landingExitForm != null && !landingExitForm.IsDisposed)
        {
            landingExitForm.Close();
            landingExitForm.Dispose();
        }

        landingExitForm = new LandingExitForm(
            airportDataProvider, announcer, landingExitPlanner, presetIcao, presetRunway,
            simConnectManager);

        landingExitForm.Show();
        landingExitForm.BringToFront();
        landingExitForm.Activate();
    }

    private void OnTaxiGuidanceStateChanged(object? sender, TaxiGuidanceState newState)
    {
        // DIAGNOSTIC: log state transitions to landing_exit.log so we can correlate
        // them with the rollout-phase per-frame log entries.
        try
        {
            string diagPath = MSFSBlindAssist.Utils.AppLogs.PathFor("landing_exit.log");
            System.IO.File.AppendAllText(diagPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [MF] OnTaxiGuidanceStateChanged newState={newState}{Environment.NewLine}");
        }
        catch { }

        // DIAGNOSTIC: reset the first-rollout-pos one-shot whenever we ENTER
        // LandingRollout so each rollout gets its own fresh log entry.
        if (newState == TaxiGuidanceState.LandingRollout)
            _diagLoggedFirstRolloutPos = false;

        switch (newState)
        {
            case TaxiGuidanceState.Taxiing:
                simConnectManager.StartTaxiGuidanceMonitoring();
                break;
            case TaxiGuidanceState.LandingRollout:
                // A landing just started (Landing Exit Planner auto-activation). Any docking
                // destination still set belongs to the PREVIOUS flight's arrival — clear it so
                // the stale gate can't keep IsActive latched and mute the rollout steering tone.
                // Covers hand-flown departures where the takeoff-assist clear never ran.
                // (Position monitoring is unchanged here — it's already running from the
                // route-load Taxiing transition.)
                dockingGuidanceManager?.SetDestinationGate(null);
                break;
            case TaxiGuidanceState.Arrived:
            case TaxiGuidanceState.Inactive:
            case TaxiGuidanceState.ProgressiveHold:
                simConnectManager.StopTaxiGuidanceMonitoring();
                break;
        }
    }

    private void ShowTrackFixDialog()
    {
        // Ensure input and output hotkey modes are deactivated before showing dialog
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();

        // Create form if it doesn't exist or has been disposed
        if (trackFixForm == null || trackFixForm.IsDisposed)
        {
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
            trackFixForm = new TrackFixForm(waypointTracker, simConnectManager, announcer, navigationDatabasePath);
        }

        // Show the form
        trackFixForm.ShowForm();
    }

    private void ReadTrackedWaypoint(int slotNumber)
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator");
            return;
        }

        // Check if slot is empty
        if (waypointTracker.IsSlotEmpty(slotNumber))
        {
            announcer.AnnounceImmediate($"Track slot {slotNumber} empty");
            return;
        }

        // Get current aircraft position
        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            try
            {
                // Get tracked waypoint info with current distance and bearing
                string? waypointInfo = waypointTracker.GetTrackedWaypointInfo(
                    slotNumber,
                    position.Latitude,
                    position.Longitude,
                    position.MagneticVariation);

                if (waypointInfo != null)
                {
                    announcer.AnnounceImmediate(waypointInfo);
                }
                else
                {
                    announcer.AnnounceImmediate($"Track slot {slotNumber} waypoint not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading tracked waypoint: {ex.Message}");
                announcer.AnnounceImmediate($"Error reading track slot {slotNumber}");
            }
        });
    }

    // (Old PFD / ND / ECAM / Status display-window launchers removed — the FBW
    // aircraft read these through the accessible status-box panels now.)

    private void RequestDestinationRunwayDistance()
    {
        if (!simConnectManager.HasDestinationRunway())
        {
            announcer.AnnounceImmediate("No destination runway selected. Press left bracket then shift+d to select a destination runway first.");
            return;
        }

        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        // Request current aircraft position to calculate distance and bearing to destination runway
        // This will be handled asynchronously through the SimConnect event system
        simConnectManager.RequestDestinationRunwayDistance();
    }

    private void RequestILSGuidance()
    {
        // Check if destination runway is selected
        if (!simConnectManager.HasDestinationRunway())
        {
            announcer.AnnounceImmediate("No destination runway selected. Press left bracket then shift+d to select a destination runway first.");
            return;
        }

        // Check if connected to simulator
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        // Check if airport database is available
        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. ILS guidance requires database.");
            return;
        }

        // Get destination runway and airport
        var runway = simConnectManager.GetDestinationRunway();
        var airport = simConnectManager.GetDestinationAirport();

        if (runway == null || airport == null)
        {
            announcer.AnnounceImmediate("No destination runway selected.");
            return;
        }

        // Task 1 — Destination prefetch (silent, fire-and-forget)
        if (_augmentPrefetched.Add(airport.ICAO))
            _ = _augmentingProvider?.PrefetchAsync(airport.ICAO, force: true);

        // Query ILS data from database
        var ilsData = airportDataProvider.GetILSForRunway(airport.ICAO, runway.RunwayID);

        if (ilsData == null)
        {
            announcer.AnnounceImmediate($"No ILS available for runway {runway.RunwayID} at {airport.ICAO}.");
            return;
        }

        // Request ILS guidance calculation
        // This will be handled asynchronously through the SimConnect event system
        simConnectManager.RequestILSGuidance(ilsData, runway, airport);
    }

    private async void RequestNavRadioInfo()
    {
        if (simConnectManager == null || !simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        bool received = false;
        string announcement = "";

        simConnectManager.RequestNavRadioInfo(navData =>
        {
            announcement = FormatNavRadioData(navData);
            received = true;
        });

        var timeout = DateTime.Now.AddSeconds(2);
        while (!received && DateTime.Now < timeout)
        {
            await Task.Delay(50);
            Application.DoEvents();
        }

        if (received)
            announcer.AnnounceImmediate(announcement);
        else
            announcer.AnnounceImmediate("NAV radio data unavailable.");
    }

    private string FormatNavRadioData(SimConnect.SimConnectManager.NavRadioData data)
    {
        var parts = new List<string>();

        parts.Add(FormatSingleNav("Nav 1", data.Nav1Freq, data.Nav1HasNav, data.Nav1HasLocalizer,
            data.Nav1HasGlideSlope, data.Nav1HasDME, data.Nav1DME, data.Nav1Localizer,
            data.Nav1GlideSlope, data.Nav1Ident, data.Nav1Name));

        parts.Add(FormatSingleNav("Nav 2", data.Nav2Freq, data.Nav2HasNav, data.Nav2HasLocalizer,
            data.Nav2HasGlideSlope, data.Nav2HasDME, data.Nav2DME, data.Nav2Localizer,
            data.Nav2GlideSlope, data.Nav2Ident, data.Nav2Name));

        return string.Join(". ", parts);
    }

    private string FormatSingleNav(string label, double freq, double hasNav, double hasLoc,
        double hasGS, double hasDME, double dme, double locCourse, double gsAngle,
        string ident, string name)
    {
        string freqStr = freq.ToString("F2");
        var info = new List<string> { $"{label}: {freqStr}" };

        if (hasNav <= 0)
        {
            info.Add("no signal");
            return string.Join(", ", info);
        }

        if (!string.IsNullOrWhiteSpace(ident))
            info.Add(ident);
        if (!string.IsNullOrWhiteSpace(name))
            info.Add(name);

        if (hasLoc > 0)
            info.Add($"localizer course {(int)locCourse}");

        if (hasGS > 0)
            info.Add($"glideslope {gsAngle:F1} degrees");

        if (hasDME > 0)
            info.Add($"DME {dme:F1} nautical miles");

        return string.Join(", ", info);
    }

    private async void RequestWindInfo()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        try
        {
            // Get current wind from SimConnect (synchronously for now)
            string currentWind = "unavailable";
            bool currentWindReceived = false;

            simConnectManager.RequestWindInfo(currentWindData =>
            {
                currentWind = FormatWindData(currentWindData);
                currentWindReceived = true;
            });

            // Wait briefly for current wind data
            var timeout = DateTime.Now.AddSeconds(2);
            while (!currentWindReceived && DateTime.Now < timeout)
            {
                await Task.Delay(50);
                Application.DoEvents();
            }

            // Check if destination airport is set
            if (simConnectManager.HasDestinationRunway())
            {
                var destinationAirport = simConnectManager.GetDestinationAirport();

                // Task 1 — Destination prefetch (silent, fire-and-forget)
                if (destinationAirport != null && _augmentPrefetched.Add(destinationAirport.ICAO))
                    _ = _augmentingProvider?.PrefetchAsync(destinationAirport.ICAO, force: true);

                // Get destination wind from VATSIM API
                var destinationWindData = await VATSIMService.GetAirportWindAsync(destinationAirport?.ICAO ?? "");
                string destinationWind = VATSIMService.FormatWind(destinationWindData);

                announcer.AnnounceImmediate($"{currentWind}, {destinationWind}");
            }
            else
            {
                announcer.AnnounceImmediate($"{currentWind}, no destination");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error in RequestWindInfo: {ex.Message}");
            announcer.AnnounceImmediate("Error getting wind information");
        }
    }

    private string FormatWindData(MSFSBlindAssist.SimConnect.SimConnectManager.WindData windData)
    {
        // Convert direction to integer and round speed to nearest knot
        int direction = (int)Math.Round(windData.Direction);
        int speed = (int)Math.Round(windData.Speed);

        if (speed == 0)
            return "calm";

        // Format as "direction at speed"
        return $"{direction:000} at {speed}";
    }


    private void ToggleTakeoffAssist()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        // Request current position for takeoff assist toggle
        simConnectManager.RequestPositionForTakeoffAssist();
    }

    private async void DescribeSceneAsync()
    {
        try
        {
            announcer.AnnounceImmediate("Capturing scene...");

            // Create screenshot and Gemini services
            var screenshotService = new ScreenshotService();
            var geminiService = new GeminiService();

            // Check if MSFS window is available
            if (!screenshotService.IsMsfsWindowAvailable())
            {
                announcer.AnnounceImmediate("Microsoft Flight Simulator window not found.");
                return;
            }

            // Capture screenshot
            byte[]? screenshot = await screenshotService.CaptureAsync();
            if (screenshot == null || screenshot.Length == 0)
            {
                announcer.AnnounceImmediate("Failed to capture scene screenshot.");
                return;
            }

            // Analyze scene with Gemini
            string analysis = await geminiService.AnalyzeSceneAsync(screenshot);

            // Show result in form (independent window with synchronous focus)
            var resultForm = new DisplayReadingResultForm("Scene", analysis, "Description");
            resultForm.ShowForm();

            announcer.AnnounceImmediate("Scene description ready");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("API key"))
        {
            announcer.AnnounceImmediate("Gemini API key not configured. Please configure it in File menu, Gemini Settings.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error in DescribeSceneAsync: {ex.Message}");
            announcer.AnnounceImmediate($"Error describing scene: {ex.Message}");
        }
    }

    private void OnTakeoffAssistActiveChanged(object? sender, bool isActive)
    {
        if (isActive)
        {
            // If taxi guidance is still running (e.g. pilot stayed in LiningUp state after
            // reaching the runway), stop it now — otherwise both systems compete for the
            // steering tone channel during takeoff roll and the pilot hears two tones.
            if (taxiGuidanceManager.State != TaxiGuidanceState.Inactive)
            {
                taxiGuidanceManager.StopGuidance();
            }

            // The flight is departing — clear any docking destination from the previous
            // arrival. Without this the gate (and a latched Docking/Stopped state) survived
            // the whole flight: on landing, the rollout's position frames fed docking a stale
            // departure-airport gate and could keep the landing-exit steering tone muted.
            // (The Stopped state also self-heals on absolute distance now, but takeoff is the
            // unambiguous "previous arrival is over" boundary — clear it here.)
            dockingGuidanceManager?.SetDestinationGate(null);

            // Start monitoring position, pitch, and IAS for takeoff assist
            simConnectManager.StartTakeoffAssistMonitoring();

            // If Fenix aircraft, read V1/VR speeds from MCDU performance data (already continuously monitored)
            if (currentAircraft.AircraftCode == "FENIX_A320CEO")
            {
                // Use N_MISC_PERF_TO_V1/VR (MCDU performance data), not FNX2PLD_speedV1/VR (display variables)
                bool foundV1 = currentSimVarValues.TryGetValue("N_MISC_PERF_TO_V1", out double v1Val);
                bool foundVR = currentSimVarValues.TryGetValue("N_MISC_PERF_TO_VR", out double vrVal);

                double? v1 = foundV1 ? v1Val : null;
                double? vr = foundVR ? vrVal : null;
                takeoffAssistManager.SetFenixVSpeeds(v1, vr);

                System.Diagnostics.Debug.WriteLine($"[TakeoffAssist] Fenix V-speeds from MCDU: V1={v1Val}, VR={vrVal}");
            }
        }
        else
        {
            // Stop monitoring
            simConnectManager.StopTakeoffAssistMonitoring();

            // Clear Fenix V-speeds on deactivation
            takeoffAssistManager.ClearFenixVSpeeds();
        }
    }

    private void OnTakeoffRunwayReferenceSet(object? sender, TakeoffRunwayReferenceEventArgs e)
    {
        // Set the runway reference in the takeoff assist manager when user teleports to a runway
        takeoffAssistManager.SetRunwayReference(e.ThresholdLat, e.ThresholdLon,
            e.RunwayHeadingTrue, e.RunwayHeadingMagnetic,
            e.RunwayID, e.AirportICAO);
    }

    /// <summary>
    /// Fires when TaxiGuidanceManager detects the aircraft has become lined up
    /// on its destination runway (one-shot per route). Auto-activates Takeoff
    /// Assist when the user setting permits, via the standard CTRL+T flow.
    /// </summary>
    private void OnTaxiGuidanceRequestTakeoffAssistAutoActivate(
        object? sender, TakeoffAssistAutoActivateEventArgs e)
    {
        // Marshal to the UI thread — the event is raised from a SimConnect-
        // thread UpdatePosition callback (inside _stateLock), but we touch
        // takeoffAssistManager / announcer / SettingsManager.Current here.
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
                OnTaxiGuidanceRequestTakeoffAssistAutoActivate(sender, e)));
            return;
        }

        if (!SettingsManager.Current.TakeoffAssistAutoActivateOnLineup) return;
        if (takeoffAssistManager.IsActive) return;
        if (!_lastOnGround) return;

        // e.RunwayId / e.AirportIcao are informational only; the actual
        // reference seeding goes through TryGetRunwayLineupReference in the
        // POSITION_FOR_TAKEOFF_ASSIST reply handler.

        // Tell the pilot WHY takeoff assist is coming on — they didn't press
        // a key, and a sudden system-initiated activation needs a verbal
        // breadcrumb. The standard "Takeoff assist active, runway X at Y"
        // callout follows from Toggle() once the position request returns.
        announcer.AnnounceImmediate("Lined up. Activating takeoff assist.");

        // Re-uses the same path as CTRL+T: the POSITION_FOR_TAKEOFF_ASSIST
        // reply handler will see takeoffAssistManager.HasRunwayReference == false,
        // probe TryGetRunwayLineupReference (which succeeds because the event
        // fires AT the lineup-aligned moment), seed the reference, and call
        // Toggle. No special-case wiring needed.
        simConnectManager.RequestPositionForTakeoffAssist();
    }

    private void ToggleHandFlyMode()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        handFlyManager.Toggle();
    }

    private void OnHandFlyModeActiveChanged(object? sender, bool isActive)
    {
        if (isActive)
        {
            // Start monitoring pitch, bank, and optionally heading/VS
            simConnectManager.StartHandFlyMonitoring(handFlyManager.MonitorHeading, handFlyManager.MonitorVerticalSpeed);

            // Register global H, V, Q hotkeys for quick access during hand fly mode
            bool hotkeysRegistered = hotkeyManager.RegisterHandFlyHotkeys();
            if (!hotkeysRegistered)
            {
                // Registration failed - likely another application is using H, V, or Q keys
                announcer.Announce("Hand fly mode active. Quick access keys unavailable. Use output mode for H, V, Q.");
            }
        }
        else
        {
            // Stop monitoring
            simConnectManager.StopHandFlyMonitoring();

            // Unregister global H, V, Q hotkeys
            hotkeyManager.UnregisterHandFlyHotkeys();

            // Visual guidance is now independent of HandFly mode — do NOT stop it just
            // because HandFly is being toggled off. VG runs its own attitude monitoring
            // (VISUAL_GUIDANCE_PITCH / VISUAL_GUIDANCE_BANK) and has nothing to lose from
            // HandFly going inactive. If anything, HandFly turning off makes VG audio
            // cleaner because there are now only two tones playing instead of three.
        }
    }

    private void ToggleVisualGuidance()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator");
            return;
        }

        visualGuidanceManager.Toggle();
    }

    private void OnVisualGuidanceActiveChanged(object? sender, bool isActive)
    {
        if (isActive)
        {
            // Validation: visual guidance no longer requires HandFly mode — it monitors its
            // own pitch/bank/heading via VISUAL_GUIDANCE_DATA. Decoupled per pilot feedback that
            // HandFly's single tone interfered with VG's dual tones, making it hard to tell
            // which tone to follow. If HandFly happens to also be active, its tone is paused
            // for the duration of VG (see HandFlyManager.SuppressAudio).
            // Use Stop(announce: false) — Toggle has already flipped isActive=true but the user
            // never actually had a running guidance session, so the public "Visual guidance off"
            // callout would be misleading after a validation error.
            var runway = simConnectManager.GetDestinationRunway();
            var airport = simConnectManager.GetDestinationAirport();
            if (runway == null)
            {
                announcer.Announce("No destination runway selected");
                visualGuidanceManager.Stop(announce: false);
                return;
            }
            // Defensive: Initialize() dereferences the airport (MagVar / Altitude). Runway and
            // airport are set as a pair today, so this won't currently fire, but guarding here
            // mirrors the runway check and prevents an NPE if that invariant ever changes.
            if (airport == null)
            {
                announcer.Announce("No destination airport selected");
                visualGuidanceManager.Stop(announce: false);
                return;
            }

            // Task 1 — Destination prefetch (silent, fire-and-forget)
            if (_augmentPrefetched.Add(airport.ICAO))
                _ = _augmentingProvider?.PrefetchAsync(airport.ICAO, force: true);

            // Get user preferences from settings
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            var guidanceToneWaveform = settings.VisualGuidanceToneWaveform;
            var guidanceVolume = settings.VisualGuidanceToneVolume;

            // Initialize visual guidance with runway, audio preferences (desired + optional follower tone),
            // and aircraft-specific tunables from the current aircraft definition.
            visualGuidanceManager.Initialize(
                runway, airport,
                guidanceToneWaveform, guidanceVolume,
                settings.VisualGuidanceCurrentToneWaveform,
                settings.VisualGuidanceCurrentToneVolume,
                settings.VisualGuidanceHardPanTone,
                currentAircraft.GetVisualGuidanceProfile());

            // PMDG 777: if the FMC has a pilot-entered landing Vref, push it as a live
            // override of the profile-default reference Vref. The PMDG SDK doesn't expose
            // AoA (which we read via the standard SimConnect INCIDENCE ALPHA simvar) but
            // it DOES publish FMC_LandingVREF in its CDA broadcast — snapshot it at VG
            // activation time. FBW / Fenix A320 have no equivalent SDK field, so they
            // continue to use the A320 profile default. Snapshot rather than live: if the
            // pilot re-enters Vref mid-approach (rare), they re-toggle VG to pick it up.
            if (currentAircraft?.AircraftCode == "PMDG_777" &&
                simConnectManager?.PMDGDataManager != null)
            {
                double fmcVref = simConnectManager.PMDGDataManager.GetFieldValue("FMC_LandingVREF");
                if (fmcVref > 0)
                {
                    visualGuidanceManager.UpdateReferenceVref(fmcVref);
                    System.Diagnostics.Debug.WriteLine($"[MainForm] VG: pushed PMDG FMC_LandingVREF={fmcVref:F0}kt as ReferenceVref");
                }
            }

            // Start monitoring position variables at 1 Hz
            simConnectManager!.StartVisualGuidanceMonitoring();

            // Silence HandFly's tone if it's also active — VG's two tones use the same
            // Hz/pan mapping as HandFly's single tone, and pilots reported the three tones
            // together were impossible to follow. Announcements (if HandFly's feedback mode
            // includes them) still fire. Idempotent — no-op if HandFly was already silent.
            handFlyManager.SuppressAudio();

            // Register the quick-access hotkey set (H, V, Q, S, D, B, P, A, F). The set is
            // shared with HandFly — VG is a hand-flying scenario with extra audio guidance, so
            // the same per-key readouts apply. The shared registration is reference-counted
            // inside HotkeyManager, so activating both modes is conflict-free; whichever
            // deactivates last releases the keys. If a key fails to register (some other app
            // is holding it globally), the user is told to fall back to output mode.
            bool allQuickKeysRegistered = hotkeyManager.RegisterVisualGuidanceHotkeys();
            if (!allQuickKeysRegistered)
            {
                announcer.Announce("Visual guidance active. Some quick-access keys unavailable; use output mode.");
            }
        }
        else
        {
            // Stop monitoring
            simConnectManager.StopVisualGuidanceMonitoring();

            // Release VG's claim on the quick-access hotkey set. If HandFly is still active,
            // its claim keeps the keys registered; if not, this drops the ref count to zero
            // and unregisters all 9 keys.
            hotkeyManager.UnregisterVisualGuidanceHotkeys();

            // Resume HandFly's tone if HandFly is still active and its feedback mode wants
            // tones. Idempotent — no-op if HandFly is off or in announcements-only mode.
            handFlyManager.ResumeAudio();
        }
    }

    private void DatabaseSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new DatabaseSettingsForm(announcer, this))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                // Reload database provider with new settings
                RefreshDatabaseProvider();

                // Announce the change
                var status = DatabaseSelector.GetDatabaseStatus();
                if (status.hasDatabase)
                {
                    announcer.AnnounceImmediate($"Database settings saved. Using {status.message}");
                }
                else
                {
                    announcer.AnnounceImmediate($"Database settings saved. {status.message}");
                }
            }
        }
    }

    private void AnnouncementSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        var currentMode = announcer.GetAnnouncementMode();
        using (var settingsForm = new AnnouncementSettingsForm(
            currentMode,
            settings.NearestCityAnnouncementInterval,
            settings.WeatherAutoAnnounceEnabled,
            settings.WeatherAutoAnnounceIntervalMinutes,
            settings.SigmetProximityAlertsEnabled,
            settings.PirepProximityAlertsEnabled,
            settings.SigmetProximityRangeNm,
            settings.AnnounceTimeWithSeconds,
            settings.GsxBackgroundMonitoring))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                // Announcement mode
                var newMode = settingsForm.SelectedMode;
                announcer.SetAnnouncementMode(newMode);

                // Nearest city interval
                settings.NearestCityAnnouncementInterval = settingsForm.NearestCityAnnouncementInterval;
                RestartNearestCityAnnouncementTimer();

                // Weather announcements
                settings.WeatherAutoAnnounceEnabled = settingsForm.WeatherAutoAnnounceEnabled;
                settings.WeatherAutoAnnounceIntervalMinutes = settingsForm.WeatherAutoAnnounceIntervalMinutes;
                settings.SigmetProximityAlertsEnabled = settingsForm.SigmetProximityAlertsEnabled;
                settings.PirepProximityAlertsEnabled = settingsForm.PirepProximityAlertsEnabled;
                settings.SigmetProximityRangeNm = settingsForm.SigmetProximityRangeNm;

                // Push the new interval to the live monitor so the change
                // takes effect without restarting the app.
                if (activeSkyWeatherMonitor != null)
                    activeSkyWeatherMonitor.IntervalMinutes = settings.WeatherAutoAnnounceIntervalMinutes;

                // Time-of-day format toggle (Output Z / Shift+Z).
                settings.AnnounceTimeWithSeconds = settingsForm.AnnounceTimeWithSeconds;

                // GSX background-monitoring toggle. Push the new value into
                // the live service. The form's VisibleChanged handler will
                // overwrite this when the form is open/hidden — that's
                // intentional (form open = form drives speech). When the
                // form is hidden the saved setting wins.
                settings.GsxBackgroundMonitoring = settingsForm.GsxBackgroundMonitoring;
                if (_gsxService != null && (_accessGsxForm == null || !_accessGsxForm.Visible))
                    _gsxService.AnnounceWhenFormHidden = settings.GsxBackgroundMonitoring;

                MSFSBlindAssist.Settings.SettingsManager.Save();

                string modeText = newMode == AnnouncementMode.ScreenReader ? "screen reader" : "SAPI";
                statusLabel.Text = $"Announcement settings saved (mode: {modeText})";
                announcer.Announce("Announcement settings saved");
            }
        }
    }

    private void GeoNamesSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new Forms.GeoNamesApiKeyForm())
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                statusLabel.Text = "GeoNames settings saved successfully";
                announcer.Announce("GeoNames settings saved successfully");
            }
        }
    }

    private void SimBriefSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new Forms.SimBriefSettingsForm())
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                statusLabel.Text = "SimBrief settings saved successfully";
                announcer.Announce("SimBrief settings saved successfully");
            }
        }
    }


    private void GeminiSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new Forms.GeminiSettingsForm())
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                statusLabel.Text = "Gemini settings saved successfully";
                announcer.Announce("Gemini settings saved successfully");
            }
        }
    }

    private void HandFlyOptionsMenuItem_Click(object? sender, EventArgs e)
    {
        var currentSettings = SettingsManager.Current;
        using (var settingsForm = new Forms.HandFlyOptionsForm(
            currentSettings.HandFlyFeedbackMode,
            currentSettings.HandFlyWaveType,
            currentSettings.HandFlyToneVolume,
            currentSettings.HandFlyMonitorHeading,
            currentSettings.HandFlyMonitorVerticalSpeed,
            currentSettings.VisualGuidanceToneWaveform,
            currentSettings.VisualGuidanceToneVolume,
            currentSettings.VisualGuidanceCurrentToneWaveform,
            currentSettings.VisualGuidanceCurrentToneVolume,
            currentSettings.VisualGuidanceHardPanTone,
            currentSettings.TakeoffAssistToneWaveform,
            currentSettings.TakeoffAssistToneVolume,
            currentSettings.TakeoffAssistMuteCenterlineAnnouncements,
            currentSettings.TakeoffAssistInvertPanning,
            currentSettings.TakeoffAssistHardPanTone,
            currentSettings.TakeoffAssistHeadingToneThreshold,
            currentSettings.TakeoffAssistLegacyMode,
            currentSettings.TakeoffAssistEnableCallouts,
            currentSettings.TakeoffAssistAutoActivateOnLineup))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                // Update settings
                currentSettings.HandFlyFeedbackMode = settingsForm.SelectedFeedbackMode;
                currentSettings.HandFlyWaveType = settingsForm.SelectedWaveType;
                currentSettings.HandFlyToneVolume = settingsForm.SelectedVolume;
                currentSettings.HandFlyMonitorHeading = settingsForm.MonitorHeading;
                currentSettings.HandFlyMonitorVerticalSpeed = settingsForm.MonitorVerticalSpeed;
                currentSettings.VisualGuidanceToneWaveform = settingsForm.GuidanceToneWaveform;
                currentSettings.VisualGuidanceToneVolume = settingsForm.SelectedGuidanceVolume;
                currentSettings.VisualGuidanceCurrentToneWaveform = settingsForm.VisualGuidanceCurrentToneWaveform;
                currentSettings.VisualGuidanceCurrentToneVolume = settingsForm.VisualGuidanceCurrentToneVolume;
                currentSettings.VisualGuidanceHardPanTone = settingsForm.VisualGuidanceHardPanTone;
                currentSettings.TakeoffAssistToneWaveform = settingsForm.TakeoffToneWaveform;
                currentSettings.TakeoffAssistToneVolume = settingsForm.TakeoffToneVolume;
                currentSettings.TakeoffAssistMuteCenterlineAnnouncements = settingsForm.TakeoffAssistMuteCenterlineAnnouncements;
                currentSettings.TakeoffAssistInvertPanning = settingsForm.TakeoffAssistInvertPanning;
                currentSettings.TakeoffAssistHardPanTone = settingsForm.TakeoffAssistHardPanTone;
                currentSettings.TakeoffAssistHeadingToneThreshold = settingsForm.TakeoffAssistHeadingToneThreshold;
                currentSettings.TakeoffAssistLegacyMode = settingsForm.TakeoffAssistLegacyMode;
                currentSettings.TakeoffAssistEnableCallouts = settingsForm.TakeoffAssistEnableCallouts;
                currentSettings.TakeoffAssistAutoActivateOnLineup = settingsForm.TakeoffAssistAutoActivateOnLineup;
                SettingsManager.Save();

                // Recreate TakeoffAssistManager to pick up new settings (invert panning, legacy mode, tone, volume)
                // The manager's mode is set at construction time
                if (takeoffAssistManager != null)
                {
                    takeoffAssistManager.Reset();
                    takeoffAssistManager.Dispose();
                    takeoffAssistManager = new TakeoffAssistManager(announcer,
                        currentSettings.TakeoffAssistToneWaveform, currentSettings.TakeoffAssistToneVolume,
                        currentSettings.TakeoffAssistMuteCenterlineAnnouncements,
                        currentSettings.TakeoffAssistInvertPanning,
                        currentSettings.TakeoffAssistHeadingToneThreshold, currentSettings.TakeoffAssistLegacyMode,
                        currentSettings.TakeoffAssistEnableCallouts);
                    takeoffAssistManager.TakeoffAssistActiveChanged += OnTakeoffAssistActiveChanged;
                }

                // Update HandFlyManager if it's active
                handFlyManager?.UpdateSettings(
                    settingsForm.SelectedFeedbackMode,
                    settingsForm.SelectedWaveType,
                    settingsForm.SelectedVolume,
                    settingsForm.MonitorHeading,
                    settingsForm.MonitorVerticalSpeed);

                statusLabel.Text = "Hand fly options saved successfully";
                announcer.Announce("Hand fly options saved successfully");
            }
        }
    }

    private void TaxiGuidanceOptionsMenuItem_Click(object? sender, EventArgs e)
    {
        var currentSettings = SettingsManager.Current;
        // Task 4 — Manual taxiway-name refresh callback.
        // Only wired when the augmenting provider is available; null otherwise
        // (the button in the dialog disables itself when the callback is null).
        // The callback runs on a thread-pool thread and marshals the announce
        // back to the UI thread via BeginInvoke so it is always SILENT unless
        // the user explicitly pressed the button.
        Func<Task>? refreshCallback = null;
        if (_augmentingProvider != null && airportDataProvider != null)
        {
            var provider = _augmentingProvider;
            var dataProvider = airportDataProvider;
            refreshCallback = async () =>
            {
                var pos = simConnectManager.LastKnownPosition;
                if (pos == null) return;

                string? icao = await Task.Run(() =>
                    dataProvider.GetNearbyAirportICAOs(pos.Value.Latitude, pos.Value.Longitude, 50.0)
                        .Where(c => c != null && c.Length == 4)
                        .FirstOrDefault());

                if (icao == null) return;

                await provider.PrefetchAsync(icao, force: true);

                // Tell the pilot HOW MANY names the online sources added (new feature).
                var cov = provider.GetLastCoverage(icao);
                int added = cov == null ? 0
                    : cov.NamesAdoptedFromOsm + cov.NamesAdoptedFromAptDat + cov.AliasesAdded;
                string msg = added > 0
                    ? $"Taxiway names refreshed for {icao}: {added} added."
                    : $"Taxiway names refreshed for {icao}. No new names found.";
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(() => announcer.AnnounceImmediate(msg));
            };
        }

        using (var settingsForm = new Forms.TaxiGuidanceOptionsForm(
            currentSettings.TaxiGuidanceToneWaveform,
            currentSettings.TaxiGuidanceToneVolume,
            currentSettings.TaxiGuidanceInvertSteeringTone,
            currentSettings.TaxiGuidanceHardPanTone,
            currentSettings.TaxiGuidanceAnnounceCrossings,
            currentSettings.TaxiGuidanceGroundSpeedAnnounceInterval,
            currentSettings.TakeoffAssistGroundSpeedAnnounceInterval,
            currentSettings.GroundTrafficUseMetres,
            currentSettings.GsxAutoSelectGateOnRoute,
            currentSettings.DockingGuidanceEnabled,
            currentSettings.DockingBeepWaveform,
            currentSettings.DockingBeepVolume,
            onRefreshTaxiwayNames: refreshCallback,
            taxiAugmentEnabled: currentSettings.TaxiAugmentEnabled))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                currentSettings.TaxiGuidanceToneWaveform = settingsForm.SelectedToneWaveform;
                currentSettings.TaxiGuidanceToneVolume = settingsForm.SelectedVolume;
                currentSettings.TaxiGuidanceInvertSteeringTone = settingsForm.InvertSteeringTone;
                currentSettings.TaxiGuidanceHardPanTone = settingsForm.HardPanSteeringTone;
                currentSettings.TaxiGuidanceAnnounceCrossings = settingsForm.AnnounceCrossings;
                currentSettings.TaxiGuidanceGroundSpeedAnnounceInterval = settingsForm.GroundSpeedAnnounceInterval;
                currentSettings.TakeoffAssistGroundSpeedAnnounceInterval = settingsForm.TakeoffGroundSpeedAnnounceInterval;
                currentSettings.GroundDistanceUnit = settingsForm.SelectedDistanceUnit;
                currentSettings.GroundTrafficUseMetres = settingsForm.GroundTrafficUseMetres;
                currentSettings.GsxAutoSelectGateOnRoute = settingsForm.GsxAutoSelectGateOnRoute;
                currentSettings.DockingGuidanceEnabled = settingsForm.DockingGuidanceEnabled;
                currentSettings.DockingBeepWaveform = settingsForm.DockingBeepWaveform;
                currentSettings.DockingBeepVolume = settingsForm.DockingBeepVolume;
                currentSettings.TaxiAugmentEnabled = settingsForm.TaxiAugmentEnabled;
                // Apply live (next route build) — no restart needed.
                if (_augmentingProvider != null)
                    _augmentingProvider.Enabled = settingsForm.TaxiAugmentEnabled;
                SettingsManager.Save();

                statusLabel.Text = "Taxi guidance options saved successfully";
                announcer.Announce("Taxi guidance options saved successfully");
            }
        }
    }

    private void HotkeyListMenuItem_Click(object? sender, EventArgs e)
    {
        using (var hotkeyListForm = new HotkeyListForm(currentAircraft.AircraftCode))
        {
            hotkeyListForm.ShowDialog(this);
        }
    }

    private void FMCSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        var s = SettingsManager.Current;
        using (var settingsForm = new Forms.FMCSettingsForm(
            s.MCDUUseAlternateLSKKeys,
            s.PMDGEnhancedDistanceMode))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                s.MCDUUseAlternateLSKKeys = settingsForm.UseAlternateLSKKeys;
                s.PMDGEnhancedDistanceMode = settingsForm.EnhancedDistanceMode;
                SettingsManager.Save();

                // Toggle the PROG-page monitor in/out of running state to
                // match the new Enhanced-distance setting. Effect is
                // immediate — no app restart needed.
                EnsurePMDGProgPageMonitor();

                statusLabel.Text = "FMC settings saved";
                announcer.Announce("FMC settings saved");
            }
        }
    }


    private void SuspendHotkeysMenuItem_Click(object? sender, EventArgs e)
    {
        if (suspendHotkeysMenuItem.Checked)
        {
            hotkeyManager.Suspend();
            announcer.AnnounceImmediate("Hotkeys suspended");
        }
        else
        {
            if (hotkeyManager.Resume())
            {
                announcer.AnnounceImmediate("Hotkeys resumed");
            }
            else
            {
                announcer.AnnounceImmediate("Warning: failed to re-register hotkeys. Another application may be using the bracket keys.");
            }
        }
    }

    private void FlyByWireA320MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FlyByWireA320Definition());
    }

    private void FenixA320MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FenixA320Definition());
    }

    private void PMDG777MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new PMDG777Definition());
    }

    private void FlyByWireA380MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FlyByWireA380Definition());
    }

    private void PMDG737MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new PMDG737Definition());
    }

    private void HorizonSim787MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new HorizonSim787Definition());
    }

    private void SwitchAircraft(IAircraftDefinition newAircraft)
    {
        // Capture the OUTGOING definition BEFORE reassignment — several cleanup steps
        // below must act on the old instance (its motion timers, its EWD-announce flag),
        // and `currentAircraft` already points at the new aircraft by the time the
        // cleanup block runs.
        var oldAircraft = currentAircraft;
        // Halt the old A380 def's seat-motor / slider-ramp timers — they keep firing
        // calc-path L:var writes at the new aircraft otherwise (sim stays connected).
        // Both FBW defs' StopAllMotion also dispose their TCAS RA compose timer and
        // any tracked hotkey windows (FCU/Baro/E/WD) the old def instance created.
        (oldAircraft as FlyByWireA380Definition)?.StopAllMotion();
        (oldAircraft as FlyByWireA320Definition)?.StopAllMotion();
        // The HS787 def owns its synoptic-display window (a live MFD_2 Coherent socket) + the
        // autopilot window (a refresh timer) — dispose them so they don't outlive the def.
        (oldAircraft as HorizonSim787Definition)?.CloseAuxWindows();

        // Update the aircraft instance
        currentAircraft = newAircraft;

        taxiGuidanceManager.TurnLeadSeconds = newAircraft.TaxiTurnLeadSeconds;

        // Refresh aircraft-conditional menu items (FMC Settings is PMDG-only).
        UpdateAircraftSpecificMenuItems();

        // Dispose the old PROG-page monitor — it references the previous
        // aircraft's data manager. Recreation happens later, AFTER
        // InitializePMDG() has produced a fresh data manager for the new
        // aircraft (see EnsurePMDGProgPageMonitor call near the end of this
        // method). Calling EnsurePMDGProgPageMonitor here would no-op for a
        // PMDG-to-PMDG swap because the new data manager doesn't yet exist.
        if (pmdgProgPageMonitor != null)
        {
            pmdgProgPageMonitor.Dispose();
            pmdgProgPageMonitor = null;
        }

        // Invalidate PMDG field map so it rebuilds for the new aircraft
        _pmdgFieldToKeyMap = null;

        // Update SimConnectManager
        simConnectManager.CurrentAircraft = currentAircraft;

        // Reset monitor to clear cache and disable announcements during transition
        // This prevents flooding TTS with hundreds of "initial" values when switching aircraft
        simVarMonitor.Reset();
        simConnectManager.SuppressECAMAnnouncements = true;

        // Re-register variables and restart continuous monitoring for new aircraft
        if (simConnectManager.IsConnected)
        {
            simConnectManager.ReregisterAllVariables();
            simConnectManager.RestartContinuousMonitoring();
            // Re-read ATC MODEL / ICAO for the newly selected aircraft definition so
            // the door-offset map is refreshed for the new aircraft profile.
            simConnectManager.RequestAircraftInfo();

            // First-detect announcer grace for a mid-session switch TO the A380 — the
            // connect handler arms this on initial connection, but a swap reaches the
            // A380's direct-announce ProcessSimVarUpdate branches (E/WD memo codes,
            // flight phase) during batch re-registration without it. AnnounceImmediate
            // (user hotkeys) is unaffected.
            if (newAircraft is FlyByWireA380Definition) announcer.Suppressed = true;

            // Start grace period for new aircraft variables to populate
            // This prevents announcement flood when hundreds of continuous variables send initial values
            System.Windows.Forms.Timer gracePeriodTimer = new System.Windows.Forms.Timer();
            gracePeriodTimer.Interval = 5000; // 5 second grace period (same as initial connection)
            gracePeriodTimer.Tick += (s, e) =>
            {
                gracePeriodTimer.Stop();
                gracePeriodTimer.Dispose();
                simVarMonitor.EnableAnnouncements();
                simConnectManager.EnableECAMAnnouncements();
                // Unconditional: clearing an already-false flag is harmless, and NOT
                // clearing it would mute every queued announce forever.
                announcer.Suppressed = false;
                System.Diagnostics.Debug.WriteLine("[MainForm] Aircraft switch grace period ended - announcements enabled");
            };
            gracePeriodTimer.Start();
            System.Diagnostics.Debug.WriteLine("[MainForm] Aircraft switch grace period started (5 seconds)");
        }

        // Update window title
        this.Text = "MSFS Blind Assist";

        // Clear existing UI
        sectionsListBox.Items.Clear();
        panelsListBox.Items.Clear();
        controlsContainer.Controls.Clear();

        // Dispose checklistForm so it reloads for new aircraft
        if (checklistForm != null && !checklistForm.IsDisposed)
        {
            checklistForm.Dispose();
            checklistForm = null;
        }

        // Dispose A380 monitor manager when switching aircraft
        if (fbwA380MonitorManagerForm != null && !fbwA380MonitorManagerForm.IsDisposed)
        {
            fbwA380MonitorManagerForm.Dispose();
            fbwA380MonitorManagerForm = null;
        }
        // Dispose fenixMonitorManagerForm when switching aircraft
        if (fenixMonitorManagerForm != null && !fenixMonitorManagerForm.IsDisposed)
        {
            fenixMonitorManagerForm.Dispose();
            fenixMonitorManagerForm = null;
        }

        // Same for PMDGAnnouncementMonitorForm — its variable list is
        // snapshotted at construction time, so a stale instance would show
        // the previous aircraft's variables after a swap.
        if (pmdgAnnouncementMonitorForm != null && !pmdgAnnouncementMonitorForm.IsDisposed)
        {
            pmdgAnnouncementMonitorForm.Dispose();
            pmdgAnnouncementMonitorForm = null;
        }

        // Dispose Fenix MCDU form and service when switching aircraft
        if (fenixMCDUForm != null && !fenixMCDUForm.IsDisposed)
        {
            fenixMCDUForm.Dispose();
            fenixMCDUForm = null;
        }
        if (fenixEFBForm != null && !fenixEFBForm.IsDisposed)
        {
            fenixEFBForm.Dispose();
            fenixEFBForm = null;
        }
        if (fenixMCDUService != null)
        {
            fenixMCDUService.Dispose();
            fenixMCDUService = null;
        }

        // Dispose FlyByWire MCDU form and service when switching aircraft
        if (flyByWireMCDUForm != null && !flyByWireMCDUForm.IsDisposed)
        {
            flyByWireMCDUForm.Dispose();
            flyByWireMCDUForm = null;
        }
        if (flyByWireMCDUService != null)
        {
            flyByWireMCDUService.Dispose();
            flyByWireMCDUService = null;
        }

        // Dispose PMDG CDU form when switching aircraft
        if (pmdgCDUForm != null && !pmdgCDUForm.IsDisposed)
        {
            pmdgCDUForm.Dispose();
            pmdgCDUForm = null;
        }

        // Dispose FBW A380 MCDU + EFB forms on swap; disposing the forms clears
        // their state-update wiring so the next aircraft doesn't get cross-talk.
        if (fbwA380MCDUForm != null && !fbwA380MCDUForm.IsDisposed)
        {
            fbwA380MCDUForm.Dispose();
            fbwA380MCDUForm = null;
        }
        if (fbwEfbForm != null && !fbwEfbForm.IsDisposed)
        {
            fbwEfbForm.Dispose();
            fbwEfbForm = null;
        }
        // Tear down the Coherent debugger client on every swap; it is
        // recreated below only when the new aircraft is the A380X.
        if (coherentClient != null)
        {
            coherentClient.Dispose();
            coherentClient = null;
        }
        if (coherentEFBClient != null)
        {
            coherentEFBClient.Dispose();
            coherentEFBClient = null;
        }
        // Tear down the PMDG EFB Coherent clients + windows on swap.
        pmdgCoherentEfbCaptainForm?.Dispose(); pmdgCoherentEfbCaptainForm = null;
        pmdgCoherentEfbFirstOfficerForm?.Dispose(); pmdgCoherentEfbFirstOfficerForm = null;
        coherentPmdgEfbCaptain?.Dispose(); coherentPmdgEfbCaptain = null;
        coherentPmdgEfbFirstOfficer?.Dispose(); coherentPmdgEfbFirstOfficer = null;

        // Dispose PMDG First Officer forms when switching aircraft
        if (pmdg777FirstOfficerForm != null && !pmdg777FirstOfficerForm.IsDisposed)
        {
            pmdg777FirstOfficerForm.Dispose();
            pmdg777FirstOfficerForm = null;
        }
        if (pmdg737FirstOfficerForm != null && !pmdg737FirstOfficerForm.IsDisposed)
        {
            pmdg737FirstOfficerForm.Dispose();
            pmdg737FirstOfficerForm = null;
        }

        if (coherentNDClient != null)
        {
            coherentNDClient.Dispose();
            coherentNDClient = null;
        }
        // The OANS window holds the ND client by reference; dispose it too so a later
        // reopen rebuilds it against the freshly-created client (otherwise it would keep
        // the now-disposed client and never update).
        if (fbwA380OansForm != null && !fbwA380OansForm.IsDisposed)
        {
            fbwA380OansForm.Dispose();
            fbwA380OansForm = null;
        }
        // The RMP window owns its own CoherentDisplayClient + three timers and hides
        // (not closes) on user-close, so it survives a swap polling the old aircraft's
        // view — and on return it would be reused bound to the DISCARDED def instance.
        // Its Dispose(bool) override runs the full teardown.
        if (fbwA380RmpForm != null && !fbwA380RmpForm.IsDisposed)
        {
            fbwA380RmpForm.Dispose();
            fbwA380RmpForm = null;
        }
        // The DCDU window polls the A32NX DCDU view via one-shot evals on a timer —
        // dispose on swap so it can't keep evaluating against the wrong aircraft.
        if (fbwDcduForm != null && !fbwDcduForm.IsDisposed)
        {
            fbwDcduForm.Dispose();
            fbwDcduForm = null;
        }
        // The ECL checklist form is bound (readonly ctor field) to the CoherentEWDClient
        // that StopA380EWDMonitor disposes below; left alive it would be reused on the
        // next A380 load permanently blank against the dead client.
        if (fbwA380ChecklistForm != null && !fbwA380ChecklistForm.IsDisposed)
        {
            fbwA380ChecklistForm.Dispose();
            fbwA380ChecklistForm = null;
        }
        // A32NX monitor manager — same stale-snapshot reason as its A380/Fenix/PMDG
        // siblings, which are already disposed in this block.
        if (fbwA320MonitorManagerForm != null && !fbwA320MonitorManagerForm.IsDisposed)
        {
            fbwA320MonitorManagerForm.Dispose();
            fbwA320MonitorManagerForm = null;
        }
        if (hs787MonitorManagerForm != null && !hs787MonitorManagerForm.IsDisposed)
        {
            hs787MonitorManagerForm.Dispose();
            hs787MonitorManagerForm = null;
        }
        StopA380EWDMonitor(oldAircraft);

        // Dispose HS 787 forms when switching aircraft
        if (hs787FMCForm != null && !hs787FMCForm.IsDisposed)
        {
            hs787FMCForm.Dispose();
            hs787FMCForm = null;
        }

        if (hs787SimBriefForm != null && !hs787SimBriefForm.IsDisposed)
        {
            hs787SimBriefForm.Dispose();
            hs787SimBriefForm = null;
        }

        // PMDG data manager lifecycle
        if (newAircraft is IPMDGAircraft && simConnectManager.IsConnected)
        {
            simConnectManager.InitializePMDG(newAircraft);
            if (simConnectManager.PMDGDataManager != null)
            {
                simConnectManager.PMDGDataManager.VariableChanged += OnPMDGVariableChanged;
            }
        }
        else
        {
            // Unwire events before disposing
            if (simConnectManager.PMDGDataManager != null)
            {
                simConnectManager.PMDGDataManager.VariableChanged -= OnPMDGVariableChanged;
            }
            simConnectManager.DisposePMDG();
        }

        // Start the PROG-page monitor now that the new aircraft's data
        // manager exists (or stop it cleanly if we just left PMDG). This
        // must happen AFTER InitializePMDG so EnsurePMDGProgPageMonitor
        // can see the freshly-created data manager — calling it before the
        // init would silently no-op (see comment above the dispose block).
        EnsurePMDGProgPageMonitor();

        // EFB transport per aircraft. PMDG (Shift+T) and FBW A320/A380 flyPad all
        // read live through the Coherent GT debugger now — the PMDG/A320 clients are
        // created lazily when the user opens the EFB and are disposed by the swap
        // cleanup above. The A380 MCDU client is pre-started so it is connected by
        // the time the user opens the MCDU.
        if (newAircraft.AircraftCode == "FBW_A380")
        {
            coherentClient = new CoherentDebuggerClient();
            coherentClient.Start();
            coherentClient.SetActive(false);   // connect + install agent now; scrape only while the MCDU window is open
            StartA380EWDMonitor();
        }

        // The HS787 CDU + EFB open their own Coherent debugger connections on demand (from
        // their forms) — no HTTP bridge to start. The IRS-alignment monitor runs continuously
        // (so it catches the alignment countdown). When leaving the HS787, dispose its forms +
        // the IRS monitor so their Coherent connections close.
        if (newAircraft.AircraftCode == "HS_787")
            StartHS787IrsMonitor();
        else
            DisposeHS787Forms();

        // Rebuild sections from new aircraft structure
        foreach (var section in currentAircraft.GetPanelStructure().Keys)
        {
            sectionsListBox.Items.Add(section);
        }

        // Update all aircraft menu items' checked state
        UpdateAircraftMenuItems();

        // Announce the switch
        announcer.AnnounceImmediate($"Switched to {currentAircraft.AircraftName}");

        // Save preference
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        settings.LastAircraft = currentAircraft.AircraftCode;
        MSFSBlindAssist.Settings.SettingsManager.Save();

        // Update aircraft-specific menu items visibility
        UpdateAircraftSpecificMenuItems();
    }

    /// <summary>
    /// Toggles visibility of menu items that are only meaningful for specific
    /// aircraft. Called whenever the loaded aircraft changes (and at initial
    /// MainForm_Load). Gates the FMC Settings item — it's hidden for any
    /// aircraft that doesn't have an MCDU/CDU the dialog applies to (PMDG
    /// or Fenix), so the screen reader doesn't surface a settings option
    /// the user can't act on.
    /// </summary>
    private void UpdateAircraftSpecificMenuItems()
    {
        bool isPmdg = currentAircraft != null &&
                      currentAircraft.AircraftCode.StartsWith("PMDG_", StringComparison.Ordinal);
        bool isFenix = currentAircraft != null &&
                       currentAircraft.AircraftCode.StartsWith("FENIX_", StringComparison.Ordinal);
        bool isHs787 = currentAircraft != null &&
                       currentAircraft.AircraftCode.StartsWith("HS_", StringComparison.Ordinal);
        fmcSettingsMenuItem.Visible = isPmdg || isFenix || isHs787;
    }

    /// <summary>
    /// Updates aircraft menu item check states to match the current aircraft.
    /// </summary>
    private void UpdateAircraftMenuItems()
    {
        // Clear all menu item checks first
        flyByWireA320MenuItem.Checked = false;
        fenixA320MenuItem.Checked = false;
        pmdg777MenuItem.Checked = false;
        flyByWireA380MenuItem.Checked = false;
        pmdg737MenuItem.Checked = false;
        horizonSim787MenuItem.Checked = false;

        // Set the check on the current aircraft's menu item
        if (currentAircraft is FlyByWireA320Definition)
        {
            flyByWireA320MenuItem.Checked = true;
        }
        else if (currentAircraft is FenixA320Definition)
        {
            fenixA320MenuItem.Checked = true;
        }
        else if (currentAircraft is PMDG777Definition)
        {
            pmdg777MenuItem.Checked = true;
        }
        else if (currentAircraft is FlyByWireA380Definition)
        {
            flyByWireA380MenuItem.Checked = true;
        }
        else if (currentAircraft is PMDG737Definition)
        {
            pmdg737MenuItem.Checked = true;
        }
        else if (currentAircraft is HorizonSim787Definition)
        {
            horizonSim787MenuItem.Checked = true;
        }
    }

    private void UpdateDatabaseStatusDisplay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateDatabaseStatusDisplay()));
            return;
        }

        try
        {
            if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
            {
                // Database status will be shown in file menu or on-demand
                return;
            }

            // Database info is available but not shown in status bar by default
            // It can be queried when needed (e.g., in database settings dialog)
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating database status: {ex.Message}");
        }
    }

    private void RefreshDatabaseProvider()
    {
        // Save current flight plan state before recreating managers
        var savedFlightPlan = flightPlanManager?.CurrentFlightPlan;

        // Reload database provider based on current settings (can be null if not built yet)
        airportDataProvider = DatabaseSelector.SelectProvider();

        // Recreate flight plan manager with new navigation database path
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
        flightPlanManager = new MSFSBlindAssist.Navigation.FlightPlanManager(navigationDatabasePath, airportDataProvider);

        // Restore flight plan state if one existed
        if (savedFlightPlan != null && !savedFlightPlan.IsEmpty())
        {
            // Copy all flight plan data to the new manager's flight plan
            var newFlightPlan = flightPlanManager.CurrentFlightPlan;

            // Copy metadata
            newFlightPlan.DepartureICAO = savedFlightPlan.DepartureICAO;
            newFlightPlan.DepartureRunway = savedFlightPlan.DepartureRunway;
            newFlightPlan.ArrivalICAO = savedFlightPlan.ArrivalICAO;
            newFlightPlan.ArrivalRunway = savedFlightPlan.ArrivalRunway;
            newFlightPlan.SIDName = savedFlightPlan.SIDName;
            newFlightPlan.STARName = savedFlightPlan.STARName;
            newFlightPlan.ApproachName = savedFlightPlan.ApproachName;
            newFlightPlan.SimBriefUsername = savedFlightPlan.SimBriefUsername;
            newFlightPlan.LoadedTime = savedFlightPlan.LoadedTime;

            // Copy all waypoint sections
            newFlightPlan.DepartureAirportWaypoints = new List<WaypointFix>(savedFlightPlan.DepartureAirportWaypoints);
            newFlightPlan.SIDWaypoints = new List<WaypointFix>(savedFlightPlan.SIDWaypoints);
            newFlightPlan.EnrouteWaypoints = new List<WaypointFix>(savedFlightPlan.EnrouteWaypoints);
            newFlightPlan.STARWaypoints = new List<WaypointFix>(savedFlightPlan.STARWaypoints);
            newFlightPlan.ApproachWaypoints = new List<WaypointFix>(savedFlightPlan.ApproachWaypoints);
            newFlightPlan.ArrivalAirportWaypoints = new List<WaypointFix>(savedFlightPlan.ArrivalAirportWaypoints);
        }

        // Close EFB window if open - it will be recreated with the new manager when reopened
        if (electronicFlightBagForm != null && !electronicFlightBagForm.IsDisposed)
        {
            electronicFlightBagForm.Close();
            electronicFlightBagForm = null;
        }

        UpdateDatabaseStatusDisplay();
    }

    /// <summary>
    /// Closes all database connections to allow file operations (like rebuilding databases).
    /// Saves the current flight plan state to restore after reconnection.
    /// </summary>
    public void CloseDatabaseConnections()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[MainForm] Closing database connections...");

            // Close EFB window if open - it holds database connections
            if (electronicFlightBagForm != null && !electronicFlightBagForm.IsDisposed)
            {
                electronicFlightBagForm.Close();
                electronicFlightBagForm = null;
            }

            // Set providers to null to release connections
            airportDataProvider = null;
            flightPlanManager = null!;

            // Force garbage collection to ensure connections are fully released
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            System.Diagnostics.Debug.WriteLine("[MainForm] Database connections closed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error closing database connections: {ex.Message}");
        }
    }

    /// <summary>
    /// Reopens database connections after file operations complete.
    /// Uses RefreshDatabaseProvider() to restore connections and flight plan state.
    /// </summary>
    public void ReopenDatabaseConnections()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[MainForm] Reopening database connections...");
            RefreshDatabaseProvider();
            System.Diagnostics.Debug.WriteLine("[MainForm] Database connections reopened");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error reopening database connections: {ex.Message}");
        }
    }

    /// <summary>
    /// Automatically switches database setting if detected simulator version doesn't match
    /// </summary>
    private void CheckAndSwitchDatabase()
    {
        try
        {
            string detectedSim = simConnectManager.DetectedSimulatorVersion;

            // Unknown simulator - no action needed
            if (detectedSim == "Unknown")
            {
                System.Diagnostics.Debug.WriteLine("[MainForm] Simulator version unknown, keeping current database setting");
                return;
            }

            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            string currentDbSetting = settings.SimulatorVersion ?? "FS2020";

            // Check if database setting matches detected simulator
            bool needsSwitch = false;
            string? targetVersion = null;

            if (detectedSim == "FS2024" && currentDbSetting != "FS2024")
            {
                needsSwitch = true;
                targetVersion = "FS2024";
            }
            else if (detectedSim == "FS2020" && currentDbSetting != "FS2020")
            {
                needsSwitch = true;
                targetVersion = "FS2020";
            }

            if (needsSwitch && targetVersion != null)
            {
                System.Diagnostics.Debug.WriteLine($"[MainForm] Auto-switching database from {currentDbSetting} to {targetVersion}");

                // Update settings
                settings.SimulatorVersion = targetVersion;
                MSFSBlindAssist.Settings.SettingsManager.Save(settings);

                // Reload database provider
                RefreshDatabaseProvider();

                // Announce the change to the user
                string announcement = $"Database automatically switched to {targetVersion}";
                System.Diagnostics.Debug.WriteLine($"[MainForm] {announcement}");
                announcer.Announce(announcement);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MainForm] Database setting ({currentDbSetting}) already matches detected simulator ({detectedSim})");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error in CheckAndSwitchDatabase: {ex.Message}");
        }
    }

    private async void UpdateApplicationMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            announcer.AnnounceImmediate("Checking for updates...");

            var updateService = new UpdateService();
            var result = await updateService.CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                announcer.AnnounceImmediate($"Update check failed: {result.ErrorMessage}");
                MessageBox.Show(
                    result.ErrorMessage,
                    "Update Check Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (!result.IsUpdateAvailable)
            {
                announcer.AnnounceImmediate("You are running the latest version.");
                MessageBox.Show(
                    $"You are running the latest version ({result.CurrentVersion}).",
                    "No Updates Available",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            // Show update dialog
            announcer.AnnounceImmediate($"Update available: version {result.LatestVersion}");
            using (var updateDialog = new UpdateAvailableForm(result, updateService))
            {
                if (updateDialog.ShowDialog(this) == DialogResult.OK && updateDialog.ShouldUpdate)
                {
                    try
                    {
                        // Launch updater
                        announcer.AnnounceImmediate("Launching updater. Application will close and restart.");
                        updateService.LaunchUpdater(updateDialog.DownloadedZipPath);

                        // Close the main application
                        Application.Exit();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Failed to launch updater: {ex.Message}",
                            "Update Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Update failed: {ex.Message}");
            MessageBox.Show(
                $"An error occurred while checking for updates: {ex.Message}",
                "Update Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void AboutMenuItem_Click(object? sender, EventArgs e)
    {
        using (var aboutForm = new AboutForm())
        {
            aboutForm.ShowDialog(this);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle local navigation hotkeys first (only work when app is in focus)
        if (keyData == (Keys.Control | Keys.D1))
        {
            sectionsListBox.Focus();
            return true;
        }
        else if (keyData == (Keys.Control | Keys.D2))
        {
            panelsListBox.Focus();
            return true;
        }
        // Ctrl+3 jumps straight to the current panel's Status Display field, mirroring
        // Ctrl+1 (sections list) / Ctrl+2 (panels list). Status displays are the primary
        // readout for the A320/A380, so a one-key jump to them is high-value.
        //
        // No conflict with the FCU "Pull Speed" global hotkey (also Ctrl+3): that hotkey
        // is only registered while INPUT mode is active, and a registered global hotkey
        // consumes the keystroke before ProcessCmdKey sees it — so this branch only fires
        // when input mode is OFF. This is the exact same coexistence the existing Ctrl+1/
        // Ctrl+2 panel-nav already relies on against FCU Pull-Heading/Pull-Altitude.
        else if (keyData == (Keys.Control | Keys.D3))
        {
            if (currentControls.TryGetValue("_DISPLAY_", out var dispCtrl) && dispCtrl is TextBox dispBox)
            {
                dispBox.Focus();
                // If the field is empty (OnRequest display vars don't auto-update until a
                // refresh), pull live content so the user lands on real status rather than a
                // blank box. The refresh is silent; the screen reader reads the field itself.
                // If it already has content (continuously-monitored vars / a prior refresh),
                // leave it untouched so NVDA reads the current value immediately.
                if (string.IsNullOrWhiteSpace(dispBox.Text) &&
                    currentControls.TryGetValue("_REFRESH_", out var refreshOnJump) &&
                    refreshOnJump is Button jumpRefreshBtn && jumpRefreshBtn.Enabled)
                {
                    jumpRefreshBtn.PerformClick();
                }
            }
            else
            {
                announcer.AnnounceImmediate("No status display on this panel.");
            }
            return true;
        }
        // F5 refreshes the current panel's Status Display without leaving the
        // edit field/combo you're on (easier than tabbing to the Refresh button).
        else if (keyData == Keys.F5 &&
                 currentControls.TryGetValue("_REFRESH_", out var refreshCtrl) &&
                 refreshCtrl is Button refreshBtn && refreshBtn.Enabled)
        {
            // F5 must not steal focus from the status box the user is reading. Capture the
            // box as the focus-return target BEFORE PerformClick (the async refresh moves
            // focus onto the Refresh button); refreshButton.Click restores it when done.
            if (currentControls.TryGetValue("_DISPLAY_", out var dispCtrl) && dispCtrl is Control dispC && dispC.Focused)
                _refreshFocusReturn = dispC;
            refreshBtn.PerformClick();
            return true;
        }

        // Let hotkey manager process other hotkeys
        if (hotkeyManager.ProcessKeyDown(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        // Process hotkey messages first
        if (hotkeyManager != null && hotkeyManager.ProcessWindowMessage(ref m))
        {
            return;
        }
        
        // Then process SimConnect messages
        if (simConnectManager != null)
        {
            simConnectManager.ProcessWindowMessage(ref m);
        }

        // Route messages destined for the GSX SimConnect client (distinct
        // WM_USER id 0x0403). Safe to call unconditionally; it filters on id.
        _gsxService?.ProcessWindowMessage(ref m);

        base.WndProc(ref m);
    }

    private void SectionsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (sectionsListBox.SelectedItem == null) return;

        string? newSection = sectionsListBox.SelectedItem.ToString();
        if (newSection == null || newSection == currentSection) return;

        currentSection = newSection;
        
        // Clear panels without triggering events
        panelsListBox.SelectedIndexChanged -= PanelsListBox_SelectedIndexChanged;
        panelsListBox.Items.Clear();
        currentPanel = "";
        
        if (currentAircraft.GetPanelStructure().ContainsKey(currentSection))
        {
            foreach (var panel in currentAircraft.GetPanelStructure()[currentSection])
            {
                panelsListBox.Items.Add(panel);
            }
        }
        
        // Re-enable event
        panelsListBox.SelectedIndexChanged += PanelsListBox_SelectedIndexChanged;
    }

    private void PanelsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (panelsListBox.SelectedItem == null) return;

        string? newPanel = panelsListBox.SelectedItem.ToString();
        if (newPanel == null || newPanel == currentPanel) return;

        // Update currentPanel IMMEDIATELY so screen reader sees it
        // This allows NVDA to announce the panel name instantly
        currentPanel = newPanel;

        // DEBOUNCE MECHANISM: Don't load panel immediately during rapid arrow navigation
        //
        // PROBLEM: When user rapidly arrows through panels, each SelectedIndexChanged
        // would queue expensive operations (variable requests, control creation), causing:
        // - NVDA to get overwhelmed and announce "panel list view list"
        // - Operation queue buildup leading to lag/silence
        //
        // SOLUTION: Use timer-based debouncing:
        // - Stop any pending timer (cancel previous panel load)
        // - Store which panel to load
        // - Start fresh timer (150ms)
        // - Only load panel when timer fires (user stopped arrowing)
        //
        // RESULT: User can rapidly arrow through 20 panels hearing each name instantly,
        // then only the FINAL selection loads its controls.

        if (_panelLoadTimer != null)
        {
            _panelLoadTimer.Stop(); // Cancel any pending load
            _pendingPanelLoad = newPanel; // Remember which panel to load
            _panelLoadTimer.Start(); // Start fresh timer
            System.Diagnostics.Debug.WriteLine($"[Panel Nav] Debouncing load for '{newPanel}' panel");
        }
    }

    /// <summary>
    /// Timer callback: Load panel controls after debounce delay.
    /// Only called when user stops arrowing through panels.
    /// </summary>
    private void PanelLoadTimer_Tick(object? sender, EventArgs e)
    {
        if (_panelLoadTimer != null)
        {
            _panelLoadTimer.Stop(); // Stop timer
        }

        string? panelToLoad = _pendingPanelLoad;
        if (panelToLoad == null) return;

        _pendingPanelLoad = null;

        // Now do the actual heavy work (deferred to UI thread to avoid blocking)
        BeginInvoke(new Action(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[Panel Load] Loading controls and requesting variables for '{panelToLoad}' panel");

            // Gate all combo selection-change handlers off for the duration of this build.
            // Also schedule a post-build clear so that any deferred SIC events that WinForms
            // queues during handle creation (which run after this method returns, on the
            // message loop) still see the flag set.
            _buildingPanel = true;

            // Request variables first
            if (simConnectManager != null && simConnectManager.IsConnected)
            {
                simConnectManager.RequestPanelVariables(panelToLoad, $"{panelToLoad} panel opened");

                // Also request StateVariable LVars for buttons in this panel
                var panelControls = currentAircraft.GetPanelControls();
                var variables = currentAircraft.GetVariables();
                if (panelControls.ContainsKey(panelToLoad))
                {
                    foreach (string varKey in panelControls[panelToLoad])
                    {
                        if (variables.ContainsKey(varKey) && !string.IsNullOrEmpty(variables[varKey].StateVariable))
                        {
                            simConnectManager.RequestVariable(variables[varKey].StateVariable!);
                        }
                    }
                }
            }

            // Clear and reload controls
            controlsContainer.Controls.Clear();
            currentControls.Clear();

            if (!currentAircraft.GetPanelControls().ContainsKey(currentPanel))
                return;

        // Create a TableLayoutPanel for better layout
        TableLayoutPanel layout = new TableLayoutPanel();
        layout.ColumnCount = 2;
        layout.RowCount = 0;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        layout.AutoSize = true;
        layout.Location = new Point(10, 10);
        // PERF: build the whole panel with layout suspended. This TableLayoutPanel
        // is AutoSize, so every Controls.Add otherwise forces a full re-layout — an
        // O(N^2) thrash that lagged large panels (the A380 overhead has dozens of
        // controls). Suspend now, resume once after all rows are added (below).
        layout.SuspendLayout();

        foreach (var varKey in currentAircraft.GetPanelControls()[currentPanel])
        {
            if (!currentAircraft.GetVariables().ContainsKey(varKey))
                continue;

            var varDef = currentAircraft.GetVariables()[varKey];
            
            // Add a new row (sliders need a little more height for the TrackBar).
            int rowIndex = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, varDef.RenderAsSlider ? 48 : 35));

            // Create label
            Label label = new Label();
            label.Text = varDef.DisplayName + ":";
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoSize = false;
            label.Size = new Size(140, 25);
            layout.Controls.Add(label, 0, rowIndex);

            // Create control based on type.
            // Accessible SLIDER (TrackBar) for continuous axis controls — checked first.
            if (varDef.RenderAsSlider)
            {
                double smin = varDef.SliderMin, smax = varDef.SliderMax;
                double span = (smax - smin) == 0 ? 1 : (smax - smin);
                TrackBar tb = new TrackBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    SmallChange = 1,
                    LargeChange = 10,
                    TickStyle = TickStyle.None,
                    Size = new Size(240, 40),
                    Name = varKey,
                    AccessibleName = varDef.DisplayName
                };
                if (currentSimVarValues.ContainsKey(varKey))
                {
                    int pct = (int)Math.Round((currentSimVarValues[varKey] - smin) / span * 100.0);
                    tb.Value = Math.Max(0, Math.Min(100, pct));
                }
                tb.ValueChanged += (s2, e2) =>
                {
                    if (updatingFromSim) return;   // change came from the sim, don't write back
                    double mapped = smin + (tb.Value / 100.0) * span;
                    bool handled = currentAircraft.HandleUIVariableSet(varKey, mapped, varDef, simConnectManager!, announcer);
                    if (!handled) simConnectManager?.SetLVar(varDef.Name, mapped);
                };
                layout.Controls.Add(tb, 1, rowIndex);
                currentControls[varKey] = tb;
            }
            // Check RenderAsButton next — buttons may have no ValueDescriptions
            else if (varDef.RenderAsButton)
            {
                // Render as button (momentary pushbutton, action button, etc.)
                // If StateVariable is set, show on/off state from the indicator LVar
                string buttonText = varDef.DisplayName;
                if (!string.IsNullOrEmpty(varDef.StateVariable) && currentSimVarValues.ContainsKey(varDef.StateVariable))
                {
                    double stateVal = currentSimVarValues[varDef.StateVariable];
                    buttonText = $"{varDef.DisplayName}: {(stateVal != 0 ? "On" : "Off")}";
                }
                else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count >= 2 && currentSimVarValues.ContainsKey(varKey))
                {
                    // Fallback for non-Fenix buttons that still use ValueDescriptions.
                    // Resting-state (value 0 = Off/Idle) suppression is OPT-IN via
                    // SuppressRestingButtonState, set only by the FBW momentary-button helpers
                    // — a momentary push-button has no meaningful resting value, so appending
                    // it read as noise ("Chronometer Start / Stop: Idle, button"). By DEFAULT
                    // the value-0 label shows: PMDG 777 MCP buttons ("LNAV: Off") and the
                    // HS787 Baro STD ("QNH") use value-0 descriptions that ARE meaningful state.
                    double val = currentSimVarValues[varKey];
                    if ((val != 0 || !varDef.SuppressRestingButtonState)
                        && varDef.ValueDescriptions.TryGetValue(val, out string? stateText))
                        buttonText = $"{varDef.DisplayName}: {stateText}";
                }

                Button controlButton = new Button();
                controlButton.Text = buttonText;
                controlButton.Size = new Size(240, 25);
                controlButton.Name = varKey;
                controlButton.AccessibleName = buttonText;

                controlButton.Click += (s2, e2) =>
                {
                    bool handled = currentAircraft.HandleUIVariableSet(varKey, 1, varDef, simConnectManager!, announcer);
                    if (!handled)
                    {
                        simConnectManager?.SetLVar(varDef.Name, 1);
                        announcer.Announce($"{varDef.DisplayName} pressed");
                    }
                    // Request state variable update after button press
                    if (!string.IsNullOrEmpty(varDef.StateVariable) && simConnectManager != null)
                    {
                        System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
                        stateTimer.Interval = 500; // Wait for transition to complete
                        stateTimer.Tick += (ts, te) =>
                        {
                            stateTimer.Stop();
                            stateTimer.Dispose();
                            simConnectManager.RequestVariable(varDef.StateVariable);
                        };
                        stateTimer.Start();
                    }
                };

                layout.Controls.Add(controlButton, 1, rowIndex);
                currentControls[varKey] = controlButton;
            }
            else if (varDef.RenderAsReadOnlyStatus &&
                     (varDef.ValueDescriptions == null || varDef.ValueDescriptions.Count == 0) &&
                     !string.IsNullOrEmpty(varDef.Units))
            {
                // Continuous-numeric read-only TextBox. Used for cockpit gauges
                // exposed by the PMDG NG3 SDK as float fields (cabin altitude,
                // DP, duct pressure, APU EGT, fuel temp, etc.). Text is
                // "{value:Format} {Units}" and is silently refreshed on each
                // continuous broadcast via UpdateControlFromSimVar — the user
                // reads the current value by Tab-focusing the field.
                TextBox readoutBox = new TextBox();
                readoutBox.ReadOnly = true;
                readoutBox.TabStop = true;
                readoutBox.Size = new Size(240, 25);
                readoutBox.Name = varKey;
                readoutBox.AccessibleName = varDef.DisplayName;

                string initial = "—";
                if (currentSimVarValues.ContainsKey(varKey))
                {
                    double cur = currentSimVarValues[varKey] * varDef.Scale + varDef.Offset;
                    initial = $"{cur.ToString(varDef.Format, System.Globalization.CultureInfo.InvariantCulture)} {varDef.Units}";
                }
                readoutBox.Text = initial;

                layout.Controls.Add(readoutBox, 1, rowIndex);
                currentControls[varKey] = readoutBox;
            }
            else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 1 &&
                     (varDef.RenderAsReadOnlyStatus || varDef.OnlyAnnounceValueDescriptionMatches))
            {
                // Read-only status field (annunciators, door state, etc.).
                // ValueDescriptions still drive the text; the user can focus the
                // field for the screen reader to read it, but cannot change it.
                TextBox statusBox = new TextBox();
                statusBox.ReadOnly = true;
                statusBox.TabStop = true;
                statusBox.Size = new Size(240, 25);
                statusBox.Name = varKey;
                statusBox.AccessibleName = varDef.DisplayName;

                // Seed initial text from cached value, falling back to numeric string
                // and finally to "—" if no value is known yet.
                string initial = "—";
                if (currentSimVarValues.ContainsKey(varKey))
                {
                    double cur = currentSimVarValues[varKey];
                    initial = varDef.ValueDescriptions.TryGetValue(cur, out string? desc)
                        ? desc
                        : cur.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                statusBox.Text = initial;

                layout.Controls.Add(statusBox, 1, rowIndex);
                currentControls[varKey] = statusBox;
            }
            else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 1)
            {
                // Check if variable should be rendered as button instead of combo box (aircraft-specific)
                // (Legacy path — kept for backward compat with Fenix buttons that have ValueDescriptions)
                if (varDef.RenderAsButton)
                {
                    Button controlButton = new Button();
                    controlButton.Text = varDef.DisplayName;
                    controlButton.Size = new Size(240, 25);
                    controlButton.Name = varKey;
                    controlButton.AccessibleName = varDef.DisplayName;
                    controlButton.AccessibleDescription = varDef.HelpText ?? $"Press {varDef.DisplayName}";

                    // Handle button click - send value 1 which triggers HandleUIVariableSet
                    controlButton.Click += (s2, e2) =>
                    {
                        // Let aircraft handle special cases first (custom button logic, transitions, etc.)
                        bool handled = currentAircraft.HandleUIVariableSet(varKey, 1, varDef, simConnectManager!, announcer);
                        if (handled)
                        {
                            currentSimVarValues[varKey] = 1;
                            // NOTE: We do NOT call RequestRelatedVariables here because:
                            // - Most buttons are independent (pressing one doesn't affect others)
                            // - Variables are refreshed when the panel opens (see PanelsListBox_SelectedIndexChanged)
                            // - Requesting all panel variables after each button press is wasteful
                            return; // Aircraft handled it
                        }

                        // Generic handling if aircraft didn't handle it
                        simConnectManager?.SetLVar(varDef.Name, 1);
                        currentSimVarValues[varKey] = 1;
                        announcer.Announce($"{varDef.DisplayName} pressed");
                        // NOTE: No RequestRelatedVariables - see comment above
                    };

                    layout.Controls.Add(controlButton, 1, rowIndex);
                    currentControls[varKey] = controlButton;
                }
                // Special handling for ENGINE_MODE_SELECTOR
                else if (varKey == "ENGINE_MODE_SELECTOR")
                {
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.Size = new Size(240, 25);
                    combo.Name = varKey;
                    combo.AccessibleName = varDef.DisplayName;

                    // Add items
                    combo.Items.Add("CRANK");
                    combo.Items.Add("NORM");
                    combo.Items.Add("IGN");
                    
                    // Set initial value from sim if we have it.
                    // Cache is keyed by the DICT KEY (SimVarUpdated carries VarName = varKey),
                    // not the SimVar Name — on the A380 that key is ENGINE_MODE_SELECTOR.
                    if (currentSimVarValues.ContainsKey(varKey))
                    {
                        double currentValue = currentSimVarValues[varKey];
                        combo.SelectedIndex = (int)currentValue;
                    }
                    else
                    {
                        combo.SelectedIndex = 1; // Default to NORM
                    }

                    // Handle selection change - set both engines
                    // SelectionChangeCommitted fires only on user-initiated changes (mouse click,
                    // arrow key commit, Enter). SelectedIndexChanged ALSO fires on programmatic
                    // assignment AND on the deferred replay that happens when the combo is
                    // parented and its native handle is created — which was firing phantom user-
                    // action writes during panel build, toggling state-sensing SimVars (battery,
                    // generator, ext-pwr, avionics master) and cascading the WT 787 electrical bus.
                    combo.SelectionChangeCommitted += (s2, e2) =>
                    {
                        if (!updatingFromSim && !_buildingPanel && combo.SelectedIndex >= 0)
                        {
                            uint mode = (uint)combo.SelectedIndex;
                            // Set both engines to the same mode. The combo reads back the
                            // stock ignition simvar (TURB ENG IGNITION SWITCH EX1:1), which
                            // these events DO move — so unlike the A380's old bug it never
                            // went stale. Also nudge the FBW knob-position L:var so the
                            // cockpit/EWD display matches (the events don't touch it), the
                            // same display-sync the A380 fix added.
                            simConnectManager?.SendEvent("TURBINE_IGNITION_SWITCH_SET1", mode);
                            simConnectManager?.SendEvent("TURBINE_IGNITION_SWITCH_SET2", mode);
                            simConnectManager?.ExecuteCalculatorCode($"{mode} (>L:XMLVAR_ENG_MODE_SEL)");
                            // Suppress the echo under the SAME identifier the monitor uses:
                            // SimVarUpdated carries VarName = varKey (the dict key), NOT
                            // varDef.Name — storing under the Name never matched, so the
                            // monitor re-announced every user combo change on the A380.
                            currentSimVarValues[varKey] = mode;
                            MarkUiSet(varKey, mode);
                        }
                    };
                    
                    layout.Controls.Add(combo, 1, rowIndex);
                    currentControls[varKey] = combo;
                }
                // Special handling for Lighting controls
                else if (varKey == "LIGHTING_LANDING_1" || varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3" ||
                         varKey == "LIGHTING_STROBE_0" || varKey == "LIGHT BEACON" || varKey == "LIGHT WING" ||
                         varKey == "LIGHT TAXI:2")
                {
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.Size = new Size(240, 25);
                    combo.Name = varKey;
                    combo.AccessibleName = varDef.DisplayName;

                    // Add items in order (reverse if ReverseDisplayOrder is set)
                    var sortedValues = varDef.ReverseDisplayOrder
                        ? varDef.ValueDescriptions.OrderByDescending(x => x.Key).ToList()
                        : varDef.ValueDescriptions.OrderBy(x => x.Key).ToList();
                    foreach (var kvp in sortedValues)
                    {
                        combo.Items.Add(kvp.Value);
                    }

                    // Set initial value from sim if we have it
                    if (currentSimVarValues.ContainsKey(varKey))
                    {
                        double currentValue = currentSimVarValues[varKey];

                        // Debug logging for landing lights
                        if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                        {
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] {varKey} received value: {currentValue}");
                        }

                        if (varDef.ValueDescriptions.ContainsKey(currentValue))
                        {
                            string description = varDef.ValueDescriptions[currentValue];
                            combo.SelectedItem = description;

                            // Additional debug for landing lights
                            if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                            {
                                System.Diagnostics.Debug.WriteLine($"[DEBUG] {varKey} set to: {description} (value {currentValue})");
                            }
                        }
                        else
                        {
                            // Debug if value doesn't match any description
                            if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                            {
                                System.Diagnostics.Debug.WriteLine($"[DEBUG] {varKey} value {currentValue} not found in descriptions!");
                            }
                        }
                    }
                    else
                    {
                        combo.SelectedIndex = 0; // Default to first item (typically "Off")

                        // Debug if no value found
                        if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                        {
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] {varKey} not found in currentSimVarValues, defaulting to index 0");
                        }
                    }

                    // Handle selection change
                    // Capture varKey to avoid nullable reference warnings in closure
                    string capturedVarKey = varKey;
                    // SelectionChangeCommitted fires only on user-initiated changes (mouse click,
                    // arrow key commit, Enter). SelectedIndexChanged ALSO fires on programmatic
                    // assignment AND on the deferred replay that happens when the combo is
                    // parented and its native handle is created — which was firing phantom user-
                    // action writes during panel build, toggling state-sensing SimVars (battery,
                    // generator, ext-pwr, avionics master) and cascading the WT 787 electrical bus.
                    combo.SelectionChangeCommitted += (s2, e2) =>
                    {
                        if (!updatingFromSim && !_buildingPanel && combo.SelectedIndex >= 0)
                        {
                            var selectedValue = sortedValues[combo.SelectedIndex].Key;
                            // Suppress the echo under the SAME identifier the monitor uses:
                            // SimVarUpdated carries VarName = varKey (the dict key), NOT
                            // varDef.Name. They coincide for plain L:vars but differ for stock
                            // simvars given a clean key (SEATBELT_SIGN -> "CABIN SEATBELTS ALERT
                            // SWITCH"), which is why those combos double-announced.
                            MarkUiSet(capturedVarKey, selectedValue);

                            // Capture the ACTUAL current cached state BEFORE the lines below
                            // overwrite currentSimVarValues with the new selection. The
                            // circuit-toggle branches (RWY turn-off) need this: ELECTRICAL_CIRCUIT_TOGGLE
                            // is toggle-only, so they must compare desired vs actual. The old code
                            // read currentSimVarValues AFTER the overwrite, so current == selected
                            // always, and the toggle never fired (RWY turn-off appeared dead).
                            double priorCachedState = currentSimVarValues.ContainsKey(capturedVarKey)
                                ? currentSimVarValues[capturedVarKey] : -1;

                            // Let the aircraft handle this SimVar-backed combo first
                            // (e.g. an A380 valve/exit combo whose STATE is a SimVar
                            // but whose CONTROL is a K-event — engine masters,
                            // crossfeed, doors, jetway). Mirrors the LVar-combo path.
                            if (currentAircraft.HandleUIVariableSet(capturedVarKey, selectedValue, varDef, simConnectManager!, announcer))
                            {
                                currentSimVarValues[capturedVarKey] = selectedValue;
                                return;
                            }

                            // Send the main LVar
                            simConnectManager?.SetLVar(capturedVarKey, selectedValue);
                            currentSimVarValues[capturedVarKey] = selectedValue;

                            // Landing lights: ASOBO_LIGHTING_Switch_Light_Landing_Template reads
                            // LIGHTING_LANDING_x every frame and manages the circuits automatically.
                            // SetLVar via SimConnect is equivalent to the cockpit click. No MobiFlight needed.
                            if (capturedVarKey == "LIGHTING_LANDING_1") // Nose Light (T.O./Taxi/Off)
                            {
                                simConnectManager?.SetLVar("LIGHTING_LANDING_1", selectedValue);
                            }
                            else if (capturedVarKey == "LIGHTING_LANDING_2") // Left Landing Light
                            {
                                // LANDING_2_RETRACTED: 0 = extended, 1 = retracted
                                simConnectManager?.SetLVar("LIGHTING_LANDING_2", selectedValue);
                                simConnectManager?.SetLVar("LANDING_2_RETRACTED", selectedValue == 2 ? 1 : 0);
                            }
                            else if (capturedVarKey == "LIGHTING_LANDING_3") // Right Landing Light
                            {
                                simConnectManager?.SetLVar("LIGHTING_LANDING_3", selectedValue);
                                simConnectManager?.SetLVar("LANDING_3_RETRACTED", selectedValue == 2 ? 1 : 0);
                            }
                            else if (capturedVarKey == "LIGHTING_STROBE_0") // Strobe Lights
                            {
                                if (selectedValue == 2) // Off
                                {
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 0);
                                    simConnectManager?.SendEvent("STROBES_OFF", 0);
                                }
                                else if (selectedValue == 0) // On
                                {
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 0);
                                    simConnectManager?.SendEvent("STROBES_ON", 0);
                                }
                                else // Auto (1)
                                {
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 1);
                                    // STROBE_0_AUTO=1 is sufficient — FBW FMGC manages strobe state
                                }
                            }
                            else if (capturedVarKey == "LIGHT BEACON") // Beacon Light
                            {
                                simConnectManager?.SendEvent("BEACON_LIGHTS_SET", (uint)selectedValue);
                            }
                            else if (capturedVarKey == "LIGHT WING") // Wing Lights
                            {
                                simConnectManager?.SendEvent("WING_LIGHTS_SET", (uint)selectedValue);
                            }
                            else if (capturedVarKey == "LIGHT TAXI:2") // Runway Turn Off Lights (single switch -> both sides)
                            {
                                // The real A320 has ONE RWY TURN OFF switch (SWITCH_OVHD_EXTLT_RWY)
                                // driving BOTH lights via the cockpit template RPN:
                                //   "INDEX VALUE (>K:2:TAXI_LIGHTS_SET)"
                                // (FBW_Switch_LeftClick_MouseWheel, Airbus.xml lines 250-255;
                                //  SIMVAR_INDEX_1=2 / SIMVAR_INDEX_2=3, TOGGLE_EVENT=TAXI_LIGHTS_SET)
                                // This mirrors that exactly so LIGHT TAXI:2/3, FBW presets, and the
                                // EFB all stay in sync. The old ELECTRICAL_CIRCUIT_TOGGLE path drove
                                // circuits 21/22 directly, which desynchronised LIGHT TAXI:2/3.
                                int on = selectedValue == 1 ? 1 : 0;
                                simConnectManager?.ExecuteCalculatorCode($"2 {on} (>K:2:TAXI_LIGHTS_SET)");
                                simConnectManager?.ExecuteCalculatorCode($"3 {on} (>K:2:TAXI_LIGHTS_SET)");
                                // Refresh both LIGHT TAXI state vars so an external (cockpit) change
                                // can't leave the next read stale.
                                simConnectManager?.RequestVariable("LIGHT TAXI:2", forceUpdate: true);
                                simConnectManager?.RequestVariable("LIGHT TAXI:3", forceUpdate: true);
                            }
                        }
                    };

                    layout.Controls.Add(combo, 1, rowIndex);
                    currentControls[varKey] = combo;
                }
                else
                {
                    // Normal ComboBox for other multi-value controls
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.Size = new Size(240, 25);
                    combo.Name = varKey;
                    combo.AccessibleName = varDef.DisplayName;

                    // Add items in order (reverse if ReverseDisplayOrder is set)
                    var sortedValues = varDef.ReverseDisplayOrder
                        ? varDef.ValueDescriptions.OrderByDescending(x => x.Key).ToList()
                        : varDef.ValueDescriptions.OrderBy(x => x.Key).ToList();
                    foreach (var kvp in sortedValues)
                    {
                        combo.Items.Add(kvp.Value);
                    }
                    
                    // Set initial value from sim if we have it
                    if (currentSimVarValues.ContainsKey(varKey))
                    {
                        double currentValue = currentSimVarValues[varKey];
                        if (varDef.ValueDescriptions.ContainsKey(currentValue))
                        {
                            string description = varDef.ValueDescriptions[currentValue];
                            int index = combo.Items.IndexOf(description);
                            if (index >= 0)
                            {
                                combo.SelectedIndex = index;
                            }
                        }
                    }
                    else if (combo.Items.Count > 0)
                    {
                        combo.SelectedIndex = 0; // Default to first item
                    }

                    // Handle selection change
                    // SelectionChangeCommitted fires only on user-initiated changes (mouse click,
                    // arrow key commit, Enter). SelectedIndexChanged ALSO fires on programmatic
                    // assignment AND on the deferred replay that happens when the combo is
                    // parented and its native handle is created — which was firing phantom user-
                    // action writes during panel build, toggling state-sensing SimVars (battery,
                    // generator, ext-pwr, avionics master) and cascading the WT 787 electrical bus.
                    combo.SelectionChangeCommitted += (s2, e2) =>
                    {
                        if (!updatingFromSim && !_buildingPanel && combo.SelectedIndex >= 0)
                        {
                            var selectedValue = sortedValues[combo.SelectedIndex].Key;
                            // Echo-suppress under varKey — the monitor's VarName is the dict key,
                            // not varDef.Name (fixes double-announce on key!=Name combos like the
                            // A380 seat-belt sign / "CABIN SEATBELTS ALERT SWITCH").
                            MarkUiSet(varKey, selectedValue);

                            // Let aircraft handle special cases first (validation, conversion, multi-step logic)
                            bool aircraftHandled = currentAircraft.HandleUIVariableSet(varKey, selectedValue, varDef, simConnectManager!, announcer);
                            if (aircraftHandled)
                            {
                                currentSimVarValues[varKey] = selectedValue;
                                // NOTE: We do NOT call RequestRelatedVariables here because:
                                // - Most combo boxes are independent switches (changing one doesn't affect others)
                                // - Variables are refreshed when the panel opens (see PanelsListBox_SelectedIndexChanged)
                                // - Requesting all panel variables after each combo change is wasteful
                                return; // Aircraft handled it
                            }

                            // Generic handling follows if aircraft didn't handle it
                            if (varDef.Type == SimVarType.PMDGVar)
                            {
                                bool handled = currentAircraft.HandleUIVariableSet(varKey, selectedValue, varDef, simConnectManager!, announcer);
                                if (!handled)
                                    System.Diagnostics.Debug.WriteLine($"[PMDG] Unhandled PMDGVar set: {varKey}");
                                currentSimVarValues[varKey] = selectedValue;
                            }
                            else if (varKey == "CABIN SEATBELTS ALERT SWITCH") // Seat Belts Signs
                            {
                                // Send the toggle event to change the state
                                simConnectManager?.SendEvent("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 0);

                                // Update our stored value to match the selection
                                currentSimVarValues[varKey] = selectedValue;
                                // NOTE: No RequestRelatedVariables - see comment above
                            }
                            else if (varDef.Type == SimVarType.LVar)
                            {
                                simConnectManager?.SetLVar(varDef.Name, selectedValue);
                                currentSimVarValues[varKey] = selectedValue;
                                // NOTE: No RequestRelatedVariables - see comment above
                            }
                        }
                    };
                    
                    layout.Controls.Add(combo, 1, rowIndex);
                    currentControls[varKey] = combo;
                    
                    // Request current value for this control if not automatically monitored by Important tier
                    if (varDef.Type == SimVarType.LVar && varDef.UpdateFrequency != UpdateFrequency.Continuous)
                    {
                        simConnectManager?.RequestVariable(varKey);
                    }
                }
            }
            else if (varKey.Contains("_SET") && !varDef.PreventTextInput)
            {
                // Panel for TextBox and Button (unless aircraft prevents text input)
                Panel inputPanel = new Panel();
                inputPanel.Size = new Size(240, 25);
                
                TextBox textBox = new TextBox();
                textBox.Location = new Point(0, 0);
                textBox.Size = new Size(100, 25);
                textBox.AccessibleName = $"{varDef.DisplayName} value";
                
                Button button = new Button();
                button.Text = "Set";
                button.Location = new Point(110, 0);
                button.Size = new Size(60, 23);
                button.AccessibleName = $"Set {varDef.DisplayName}";
                
                button.Click += (s2, e2) =>
                {
                    // Aircraft delegation: let the loaded aircraft claim _SET keys
                    // (e.g., PMDG 737's EFIS_MinsValueFt_*_SET vars need RST-then-rotate
                    // dispatch). The aircraft parses textBox.Text itself; we pass the
                    // double value when parseable, else 0.
                    double parsedValue = 0;
                    double.TryParse(
                        textBox.Text.Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out parsedValue);
                    if (currentAircraft.HandleUIVariableSet(
                            varKey, parsedValue, varDef, simConnectManager!, announcer))
                    {
                        return;
                    }

                    // Special handling for transponder code (requires BCD encoding)
                    if (varKey == "TRANSPONDER_CODE_SET")
                    {
                        string squawkCode = textBox.Text.Trim();

                        // Pad with leading zeros if needed (e.g., "422" -> "0422")
                        if (squawkCode.Length < 4 && squawkCode.All(char.IsDigit))
                        {
                            squawkCode = squawkCode.PadLeft(4, '0');
                        }

                        uint bcdValue = ConvertSquawkToBCD(squawkCode, out string? errorMessage);

                        if (errorMessage != null)
                        {
                            announcer.Announce(errorMessage);
                        }
                        else
                        {
                            simConnectManager?.SendEvent("XPNDR_SET", bcdValue);
                            announcer.Announce($"Squawk set to {squawkCode}");
                        }
                    }
                    else if (double.TryParse(textBox.Text, out double value))
                    {
                        // Let aircraft handle special cases first (validation, conversion, multi-step logic)
                        if (currentAircraft.HandleUIVariableSet(varKey, value, varDef, simConnectManager!, announcer))
                        {
                            return; // Aircraft handled it
                        }

                        // Generic handling follows - COM frequencies, transponder, etc.
                        if (varKey.StartsWith("COM_") && varKey.Contains("FREQUENCY_SET"))
                        {
                            // Validate COM frequency range (118.000 - 136.975 MHz)
                            if (value >= 118.0 && value <= 136.975)
                            {
                                // Convert MHz to Hz (simple multiplication, no BCD16 needed)
                                uint frequencyHz = (uint)Math.Round(value * 1000000);

                                // Determine which COM radio (1, 2, or 3)
                                string comIndex = "1"; // Default to COM1
                                if (varKey.Contains(":2")) comIndex = "2";
                                else if (varKey.Contains(":3")) comIndex = "3";

                                // Always set standby first, then swap if setting active.
                                // The COM1 standby-set event is the un-numbered "COM_STBY_RADIO_SET_HZ";
                                // COM2/COM3 use the numbered form so a COM2 set doesn't write COM1's standby.
                                string setEvent = comIndex == "1" ? "COM_STBY_RADIO_SET_HZ" : $"COM{comIndex}_STBY_RADIO_SET_HZ";
                                string swapEvent = $"COM{comIndex}_RADIO_SWAP";

                                // For active frequency: set standby then swap
                                if (varKey.Contains("ACTIVE"))
                                {
                                    simConnectManager?.SendEvent(setEvent, frequencyHz);
                                    System.Threading.Thread.Sleep(100); // Small delay for sim to process
                                    simConnectManager?.SendEvent(swapEvent);
                                    announcer.Announce($"Active frequency set to {value:F3}");
                                }
                                // For standby frequency: just set it
                                else
                                {
                                    simConnectManager?.SendEvent(setEvent, frequencyHz);
                                    announcer.Announce($"Standby frequency set to {value:F3}");
                                }
                            }
                            else
                            {
                                announcer.Announce($"Invalid COM frequency. Range: 118.000 to 136.975 MHz");
                            }
                        }
                        else if (varKey == "TRANSPONDER_CODE_SET")
                        {
                            // Validate transponder code (0000-7777 in octal)
                            int code = (int)value;
                            if (code >= 0 && code <= 7777)
                            {
                                // Verify each digit is 0-7 (octal)
                                string codeStr = code.ToString("D4");
                                bool valid = true;
                                foreach (char c in codeStr)
                                {
                                    if (c < '0' || c > '7')
                                    {
                                        valid = false;
                                        break;
                                    }
                                }

                                if (valid)
                                {
                                    // Convert to BCD16 format expected by XPNDR_SET
                                    uint bcd = (uint)(
                                        ((code / 1000) << 12) |
                                        (((code / 100) % 10) << 8) |
                                        (((code / 10) % 10) << 4) |
                                        (code % 10)
                                    );
                                    simConnectManager?.SendEvent("XPNDR_SET", bcd);
                                    announcer.AnnounceImmediate($"Transponder code set to {codeStr}");
                                }
                                else
                                {
                                    announcer.Announce("Invalid transponder code. Each digit must be 0-7.");
                                }
                            }
                            else
                            {
                                announcer.Announce("Invalid transponder code. Range: 0000 to 7777.");
                            }
                        }
                        // All A32NX-specific handling (VS/FPA, baro) now done by HandleUIVariableSet
                        else
                        {
                            simConnectManager?.SendEvent(varKey, (uint)value);
                            announcer.Announce($"{varDef.DisplayName} set to {value}");
                        }
                    }
                };
                
                inputPanel.Controls.Add(textBox);
                inputPanel.Controls.Add(button);
                layout.Controls.Add(inputPanel, 1, rowIndex);
                currentControls[varKey] = textBox;
            }
            else
            {
                // Button for simple operations
                Button button = new Button();
                button.Text = varDef.DisplayName;
                button.Size = new Size(240, 25);
                button.AccessibleName = varDef.DisplayName;
                
                button.Click += (s2, e2) =>
                {
                    if (varDef.Type == SimVarType.Event)
                    {
                        // Special handling for events that don't take parameters
                        if (varDef.Name == "A32NX.AUTOBRAKE_SET_DISARM")
                        {
                            // This event doesn't take any parameters
                            simConnectManager?.SendEvent(varDef.Name, 0);
                        }
                        else
                        {
                            // Use EventParam if it's set, otherwise use 0
                            uint param = varDef.EventParam > 0 ? varDef.EventParam : 0;
                            simConnectManager?.SendEvent(varDef.Name, param);
                        }

                        // Handle button state announcements for all panels
                        HandleButtonStateAnnouncement(varKey);
                        // Aircraft-specific post-press read-out (e.g. FCU push/pull
                        // buttons speak the resulting value like their hotkeys do).
                        currentAircraft.OnPanelButtonFired(varKey, simConnectManager!, announcer);
                    }
                    else if (varDef.Type == SimVarType.HVar)
                    {
                        // Handle H-variable buttons (MobiFlight WASM)
                        if (!string.IsNullOrEmpty(varDef.PressEvent) && !string.IsNullOrEmpty(varDef.ReleaseEvent))
                        {
                            // Automatic press/release sequence
                            simConnectManager?.SendButtonPressRelease(varDef.PressEvent, varDef.ReleaseEvent, varDef.PressReleaseDelay);

                            // Request LED state after button action using existing LVar system
                            if (!string.IsNullOrEmpty(varDef.LedVariable))
                            {
                                System.Windows.Forms.Timer ledCheckTimer = new System.Windows.Forms.Timer();
                                ledCheckTimer.Interval = varDef.PressReleaseDelay + 300; // Wait for press/release + 300ms
                                ledCheckTimer.Tick += (ts, te) =>
                                {
                                    ledCheckTimer.Stop();
                                    ledCheckTimer.Dispose();
                                    // Use existing LVar request system to read LED state
                                    simConnectManager?.RequestVariable(varDef.LedVariable);
                                    System.Diagnostics.Debug.WriteLine($"[MainForm] Requesting LED state: {varDef.LedVariable}");
                                };
                                ledCheckTimer.Start();
                            }
                        }
                        else if (!string.IsNullOrEmpty(varDef.PressEvent))
                        {
                            // Single H-variable execution
                            simConnectManager?.SendHVar(varDef.PressEvent);
                        }

                        System.Diagnostics.Debug.WriteLine($"[MainForm] H-variable button pressed: {varDef.DisplayName}");
                    }
                    else if (varDef.Type == SimVarType.LVar)
                    {
                        // Special handling for clear warning buttons - send 0 to turn off
                        if (varKey == "CLEAR_MASTER_WARNING" || varKey == "CLEAR_MASTER_CAUTION")
                        {
                            simConnectManager?.SetLVar(varDef.Name, 0);
                        }
                        else
                        {
                            simConnectManager?.SetLVar(varDef.Name, 1);

                            // Handle momentary buttons - auto-reset to 0 after a short delay
                            if (varDef.IsMomentary)
                            {
                                System.Windows.Forms.Timer momentaryTimer = new System.Windows.Forms.Timer();
                                momentaryTimer.Interval = 150; // 150ms delay
                                momentaryTimer.Tick += (ts, te) =>
                                {
                                    momentaryTimer.Stop();
                                    momentaryTimer.Dispose();
                                    simConnectManager?.SetLVar(varDef.Name, 0);
                                };
                                momentaryTimer.Start();
                            }
                        }
                    }
                };
                
                layout.Controls.Add(button, 1, rowIndex);
                currentControls[varKey] = button;
            }
        }

        // Add display field if this panel has display variables
        if (currentAircraft.GetPanelDisplayVariables().ContainsKey(currentPanel))
        {
            // Standard display for other panels
            // Add separator row
            int separatorRow = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));

            // Add display row
            int displayRow = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

            Label displayLabel = new Label();
            displayLabel.Text = "Status Display:";
            displayLabel.TextAlign = ContentAlignment.TopLeft;
            displayLabel.AutoSize = false;
            displayLabel.Size = new Size(140, 25);
            layout.Controls.Add(displayLabel, 0, displayRow);

            // Panel to hold textbox and button
            Panel displayPanel = new Panel();
            displayPanel.Size = new Size(240, 55);

            // Read-only multiline textbox for display
            TextBox displayTextBox = new TextBox();
            displayTextBox.Multiline = true;
            displayTextBox.ReadOnly = true;
            displayTextBox.Size = new Size(240, 30);
            displayTextBox.Location = new Point(0, 0);
            displayTextBox.AccessibleName = "Status display (press F5 to refresh)";
            displayTextBox.Text = "";  // Empty by default

            // Refresh button
            Button refreshButton = new Button();
            refreshButton.Text = "Refresh";
            refreshButton.Size = new Size(80, 23);
            refreshButton.Location = new Point(0, 32);
            refreshButton.AccessibleName = "Refresh status";

            // F5 on the read-only display triggers the same refresh action as the
            // button — convenient for blind users who don't want to tab to the button.
            displayTextBox.KeyDown += (s2, e2) =>
            {
                if (e2.KeyCode == Keys.F5)
                {
                    e2.SuppressKeyPress = true;
                    refreshButton.PerformClick();
                }
            };

            // When the user moves focus TO the status box, refresh it to the current selection.
            // The auto-refresh timer deliberately skips while a selector combo (or the box) is
            // focused — that periodic mid-navigation update was interrupting NVDA's combo
            // announcements — so this GotFocus refresh is what brings the box current when the
            // user goes to read it. It updates once on focus-in (review cursor at the top, which
            // is what you want when you start reading), then the box-focused guard above keeps it
            // stable. SetDisplayTextPreserveCaret no-ops when the content is unchanged.
            displayTextBox.GotFocus += (s2, e2) =>
            {
                try { currentAircraft?.OnDisplayPanelShown(currentPanel, simConnectManager!); } catch { }
            };

            refreshButton.Click += async (s2, e2) =>
            {
                // Where to return focus when the refresh finishes (set by the F5 handler
                // before PerformClick moves focus onto this button). Fall back to the box
                // if it's somehow still focused here.
                Control? focusReturn = _refreshFocusReturn ?? (displayTextBox.Focused ? displayTextBox : null);
                _refreshFocusReturn = null;

                // Only show the "Loading..." placeholder on the FIRST populate (empty box).
                // On subsequent refreshes — manual F5 or the periodic auto-refresh timer —
                // keep the existing content visible so the box doesn't flash/blank every
                // cycle; the new values simply overwrite it when they arrive.
                if (string.IsNullOrEmpty(displayTextBox.Text))
                    displayTextBox.Text = "Loading...";
                displayValues.Clear();  // Clear old values for this panel

                // Get the display variables for this panel
                var displayVars = currentAircraft.GetPanelDisplayVariables()[currentPanel];

                // Create a task completion source for each variable
                var pendingValues = new Dictionary<string, TaskCompletionSource<bool>>();
                foreach (var varKey in displayVars)
                {
                    pendingValues[varKey] = new TaskCompletionSource<bool>();
                }

                // Store the pending values temporarily
                pendingDisplayRequests = pendingValues;

                // Rebuild any aircraft-managed SNAPSHOT content (the A380/A32NX SD-page
                // box). That content lives in the aircraft def's _sdPageContent and is ONLY
                // regenerated by OnDisplayPanelShown -> RefreshSdPageDisplayAsync, which
                // re-reads the underlying SimVars (FOB, engine N1-N3, per-tank fuel, …).
                // The display-var re-request below renders that string but never rebuilds
                // it, so WITHOUT this call a manual F5 / Refresh re-printed the SAME stale
                // snapshot and values like "FOB 13400 KG" never moved. Fire-and-forget: it
                // pushes its own UpdateDisplayText when the fresh read completes (~0.6s).
                try { currentAircraft.OnDisplayPanelShown(currentPanel, simConnectManager!); } catch { }

                // Request all values. forceUpdate=true bypasses the
                // ProcessIndividualVariableResponse suppression that drops
                // SimVarUpdated for unchanged announced variables — without it,
                // a Refresh on a stable announced var (e.g. IRS state held at
                // Aligning for minutes) silently no-ops and the display falls
                // through to "--" after the 2-second timeout.
                foreach (var varKey in displayVars)
                {
                    if (currentAircraft.GetVariables().ContainsKey(varKey))
                    {
                        simConnectManager?.RequestVariable(varKey, forceUpdate: true);
                    }
                }

                // Wait for all responses or timeout after 2 seconds
                var allTasks = pendingValues.Values.Select(tcs => tcs.Task).ToArray();
                var timeoutTask = Task.Delay(2000);
                await Task.WhenAny(Task.WhenAll(allTasks), timeoutTask);

                // Clear pending requests
                pendingDisplayRequests = null;

                // Update display - NO announcement, user will read with NVDA
                UpdateDisplayText(displayTextBox);

                // Restore focus to the status box if the refresh moved it (onto the Refresh
                // button). Only refocuses when it actually left — a deliberate click on the
                // Refresh button won't bounce focus back to the box.
                if (focusReturn != null && focusReturn.IsHandleCreated && focusReturn.CanFocus && !focusReturn.Focused)
                    focusReturn.Focus();
            };

            displayPanel.Controls.Add(displayTextBox);
            displayPanel.Controls.Add(refreshButton);
            layout.Controls.Add(displayPanel, 1, displayRow);

            // Store reference to display textbox + refresh button (F5 in ProcessCmdKey
            // performs the refresh from anywhere in the panel).
            currentControls["_DISPLAY_"] = displayTextBox;
            currentControls["_REFRESH_"] = refreshButton;
        }

            // Resume + lay out ONCE now that every row exists, then attach.
            layout.ResumeLayout(true);
            controlsContainer.SuspendLayout();
            controlsContainer.Controls.Add(layout);
            controlsContainer.ResumeLayout(true);

            // Auto-populate a multi-page status box (e.g. the SD-page combo) with the
            // combo's CURRENT page, so the user doesn't have to cycle it to get content
            // on first display. No-op for panels without such a box.
            try { currentAircraft.OnDisplayPanelShown(currentPanel, simConnectManager!); } catch { }

            // Start (or stop) the live status-box auto-refresh for THIS panel. Only panels
            // that actually built a status display (have a "_REFRESH_" button) get the timer;
            // everything else stops it so we don't poll in the background on a static panel.
            StartOrStopSdAutoRefresh();

            // For PMDG aircraft, populate controls with current data from the data manager
            if (currentAircraft is IPMDGAircraft && simConnectManager?.PMDGDataManager != null)
            {
                var dm = simConnectManager.PMDGDataManager;
                foreach (var varKey in currentAircraft.GetPanelControls()[currentPanel])
                {
                    if (!currentAircraft.GetVariables().ContainsKey(varKey)) continue;
                    var varDef = currentAircraft.GetVariables()[varKey];

                    // Only PMDG broadcast fields carry data in the data manager. Reading any
                    // other type (LVar/SimVar/Event) returns GetFieldValue's 0.0 "unknown
                    // field" sentinel, which force-resets the control (e.g. the System Display
                    // page combo to page 0) and logs spurious "unknown field" lines. Non-PMDG
                    // controls are populated via combo-creation + continuous monitoring instead.
                    if (varDef.Type != SimVarType.PMDGVar) continue;

                    // Read current value from PMDG data manager using the struct field name
                    double value = dm.GetFieldValue(varDef.Name);
                    currentSimVarValues[varKey] = value;

                    // Update the UI control
                    UpdateControlFromSimVar(varKey, value);
                }
            }
            // Note: a previous attempt to "force-refresh" all panel variables here caused
            // duplicate-announce oscillation (on, then off) for HS787 vars whose
            // ProcessSimVarUpdate handler announces on transitions. Reverted; rely on the
            // initial-value read at combo creation (line 4297-4314) plus continuous
            // monitoring to keep combo state in sync with the sim.
            // Clear the flag asynchronously so any handle-creation-replay SIC events that
            // got queued while we built controls also see _buildingPanel = true. 200 ms is
            // generous; the actual replay window is sub-frame on a modern machine.
            var clearTimer = new System.Windows.Forms.Timer { Interval = 200 };
            clearTimer.Tick += (_, __) =>
            {
                clearTimer.Stop();
                clearTimer.Dispose();
                _buildingPanel = false;
            };
            clearTimer.Start();
        })); // End BeginInvoke - deferred control creation
    } // End PanelLoadTimer_Tick

    /// <summary>
    /// Starts the nearest city announcement timer if enabled in settings.
    /// Checks the current setting value and configures the timer accordingly.
    /// Called when SimConnect initially connects.
    /// </summary>
    private void StartNearestCityAnnouncementTimer()
    {
        // Delegate to restart method for consistent behavior
        RestartNearestCityAnnouncementTimer();
    }

    /// <summary>
    /// Restarts the nearest city announcement timer with current settings.
    /// Stops timer if disabled (interval = 0), or updates interval and restarts if enabled.
    /// Should be called whenever GeoNames settings are saved.
    /// </summary>
    private void RestartNearestCityAnnouncementTimer()
    {
        if (nearestCityAnnouncementTimer == null)
            return;

        // Always stop first to ensure clean state
        nearestCityAnnouncementTimer.Stop();

        // Read current setting
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        int intervalSeconds = settings.NearestCityAnnouncementInterval;

        // Start with new interval if enabled
        if (intervalSeconds > 0)
        {
            nearestCityAnnouncementTimer.Interval = intervalSeconds * 1000; // Convert to milliseconds
            nearestCityAnnouncementTimer.Start();
            System.Diagnostics.Debug.WriteLine($"[MainForm] Nearest city announcement timer restarted: {intervalSeconds} seconds interval");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[MainForm] Nearest city announcement timer stopped (disabled in settings)");
        }
    }

    /// <summary>
    /// Timer tick handler for nearest city announcements.
    /// Requests current aircraft position and announces the nearest city.
    /// </summary>
    private void NearestCityAnnouncementTimer_Tick(object? sender, EventArgs e)
    {
        AnnounceNearestCity();
    }

    // Starts the live status-box auto-refresh when the currently-shown panel has a status
    // display ("_REFRESH_" button); stops it otherwise. Called every time a panel is shown.
    private void StartOrStopSdAutoRefresh()
    {
        bool hasDisplay = currentControls != null && currentControls.ContainsKey("_REFRESH_");
        if (!hasDisplay)
        {
            _sdAutoRefreshTimer?.Stop();
            return;
        }
        if (_sdAutoRefreshTimer == null)
        {
            // 3s: longer than the Refresh handler's 2s response timeout so ticks don't stack.
            _sdAutoRefreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _sdAutoRefreshTimer.Tick += SdAutoRefreshTimer_Tick;
        }
        _sdAutoRefreshTimer.Stop();
        _sdAutoRefreshTimer.Start();
    }

    private void SdAutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Panel changed out from under us (or app tearing down) → stop polling.
            if (currentAircraft == null || string.IsNullOrEmpty(currentPanel) ||
                currentControls == null || !currentControls.ContainsKey("_REFRESH_"))
            {
                _sdAutoRefreshTimer?.Stop();
                return;
            }
            if (simConnectManager == null || !simConnectManager.IsConnected) return;

            // DON'T refresh the status box WHILE THE USER IS READING IT. Replacing a
            // read-only multiline TextBox's .Text fires an MSAA value-change that resets
            // NVDA's review cursor to the top even though the system caret is preserved
            // (SetDisplayTextPreserveCaret can't stop the review-cursor reset). So if the
            // display box currently has focus, skip this auto tick entirely — the content
            // the user is reading stays frozen and stable. Ticks resume the moment they
            // move focus away (combo/another control), and manual F5 always refreshes.
            if (currentControls.TryGetValue("_DISPLAY_", out var dc) && dc is TextBox dtb
                && dtb.IsHandleCreated && dtb.Focused)
                return;

            // Also skip while the user is on a SELECTOR COMBO in this panel (e.g. the SD page
            // picker). The refresh re-requests the page var — UpdateControlFromSimVar can then
            // re-set the combo's SelectedIndex to a lagging value, fighting the user's arrowing —
            // and it replaces the box .Text (the MSAA interference noted above). Either one steps
            // on NVDA's page-selection announcement, which is why arrowing the combo "frequently"
            // didn't announce the landed page. The box is brought current when the user moves
            // focus TO it (the display box's GotFocus refresh).
            foreach (var kv in currentControls)
                if (kv.Value is ComboBox cb && cb.IsHandleCreated && cb.Focused)
                    return;

            // (a) Rebuild any snapshot SD-page content (FOB, engine, fuel, etc.) — silent,
            //     no speech, pushes into the box via the page-index display var.
            try { currentAircraft.OnDisplayPanelShown(currentPanel, simConnectManager); } catch { }

            // (b) Re-pull the panel's OnRequest display vars for the generic status box.
            //     The handler keeps existing content (no "Loading..." flash) and overwrites
            //     in place when the fresh values arrive.
            if (currentControls.TryGetValue("_REFRESH_", out var rc) && rc is Button rb &&
                rb.Enabled && rb.IsHandleCreated)
            {
                rb.PerformClick();
            }
        }
        catch { /* best-effort live refresh; never let a tick crash the UI */ }
    }

    private void WeatherAnnouncementTimer_Tick(object? sender, EventArgs e)
    {
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        if (!simConnectManager.IsConnected) return;

        if (settings.WeatherAutoAnnounceEnabled)
            CheckAmbientWeatherChanges();

        if (settings.SigmetProximityAlertsEnabled || settings.PirepProximityAlertsEnabled)
            _ = CheckWeatherProximityAsync(settings.SigmetProximityRangeNm,
                    settings.SigmetProximityAlertsEnabled, settings.PirepProximityAlertsEnabled);
    }

    private void CheckAmbientWeatherChanges()
    {
        simConnectManager.RequestWeatherInfo(data =>
        {
            if (InvokeRequired) { BeginInvoke(() => AnnounceAmbientChanges(data)); return; }
            AnnounceAmbientChanges(data);
        });
    }

    private void AnnounceAmbientChanges(MSFSBlindAssist.SimConnect.SimConnectManager.AmbientWeatherData data)
    {
        // Cloud entry/exit
        double inCloud = data.InCloud;
        if (_prevInCloud >= 0 && Math.Abs(inCloud - _prevInCloud) > 0.5)
            announcer.Announce(inCloud >= 0.5 ? "Entering cloud" : "Leaving cloud");
        _prevInCloud = inCloud;

        // Precipitation — announce on start, stop, and intensity tier changes
        double precipState = data.PrecipState;
        double precipRate = data.PrecipRate;
        bool wasRaining = _prevPrecipState > 0.5;
        bool isRaining = precipState > 0.5;

        if (_prevPrecipState >= 0)
        {
            if (!wasRaining && isRaining)
            {
                // Started
                announcer.Announce($"Precipitation started: {DescribePrecipIntensity(precipRate)}");
            }
            else if (wasRaining && !isRaining)
            {
                // Stopped
                announcer.Announce("Precipitation stopped");
            }
            else if (isRaining && _prevPrecipRate >= 0 && IntensityTier(precipRate) != IntensityTier(_prevPrecipRate))
            {
                // Intensity changed tier (light → moderate, moderate → heavy, etc.)
                announcer.Announce($"Precipitation now {DescribePrecipIntensity(precipRate)}");
            }
        }
        _prevPrecipState = precipState;
        _prevPrecipRate = precipRate;

        // Visibility — announce crossing the 1500 m threshold in either direction
        double vis = data.Visibility;
        if (_prevVisibility >= 0)
        {
            bool isLow = vis < 1500;
            if (isLow && !_prevVisLow)
                announcer.Announce($"Visibility low: {vis / 1000.0:F1} km");
            else if (!isLow && _prevVisLow)
                announcer.Announce($"Visibility improving: {vis / 1000.0:F1} km");
        }
        _prevVisibility = vis;
        _prevVisLow = vis < 1500;
    }

    private static int IntensityTier(double rate) => rate switch
    {
        < 20 => 0,   // light
        < 50 => 1,   // moderate
        < 80 => 2,   // heavy
        _    => 3    // extreme
    };

    private static string DescribePrecipIntensity(double rate) => rate switch
    {
        < 20 => "light",
        < 50 => "moderate",
        < 80 => "heavy",
        _    => "extreme"
    };

    private async Task CheckWeatherProximityAsync(int rangeNm, bool checkSigmets, bool checkPireps)
    {
        try
        {
            var lastPos = simConnectManager.LastKnownPosition;
            if (lastPos == null) return;
            var pos = lastPos.Value;
            if (pos.Latitude == 0 && pos.Longitude == 0) return;

            // Clear stale announced keys every 15 minutes
            if ((DateTime.UtcNow - _sigmetKeysClearedAt).TotalMinutes > 15)
            {
                _announcedSigmetKeys.Clear();
                _announcedPirepKeys.Clear();
                _sigmetKeysClearedAt = DateTime.UtcNow;
            }

            if (!IsHandleCreated || IsDisposed) return;

            if (checkSigmets)
            {
                var advisories = await MSFSBlindAssist.Services.WeatherService.GetNearbyAdvisoriesAsync(
                    pos.Latitude, pos.Longitude, rangeNm);

                if (!IsHandleCreated || IsDisposed) return;

                foreach (var adv in advisories)
                {
                    string key = $"{adv.AdvisoryType}_{adv.Hazard}_{adv.ValidFrom}_{adv.ValidTo}";
                    if (_announcedSigmetKeys.Contains(key)) continue;
                    _announcedSigmetKeys.Add(key);

                    string msg = $"{adv.AdvisoryType}: {adv.HazardLabel}";
                    if (!string.IsNullOrEmpty(adv.AltitudeRange)) msg += $", {adv.AltitudeRange}";
                    msg += $", bearing {adv.BearingDeg:F0} degrees, {adv.DistanceNm:F0} nautical miles";
                    Invoke(() => announcer.Announce(msg));
                }
            }

            if (checkPireps)
            {
                var pireps = await MSFSBlindAssist.Services.WeatherService.GetNearbyPirepsAsync(
                    pos.Latitude, pos.Longitude, rangeNm);

                if (!IsHandleCreated || IsDisposed) return;

                foreach (var p in pireps)
                {
                    if (!p.IsSignificantHazard) continue;  // skip light reports

                    string key = $"PIREP_{p.ObsTime}_{p.AltitudeFt}_{p.TurbulenceIntensity}_{p.IcingIntensity}";
                    if (_announcedPirepKeys.Contains(key)) continue;
                    _announcedPirepKeys.Add(key);

                    int fl = p.AltitudeFt / 100;
                    string msg = $"Pilot report: {p.HazardSummary} at FL{fl:D3}";
                    msg += $", bearing {p.BearingDeg:F0} degrees, {p.DistanceNm:F0} nautical miles";
                    Invoke(() => announcer.Announce(msg));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Weather proximity check error: {ex.Message}");
        }
    }

    /// <summary>
    /// Announces the nearest city to the current aircraft position.
    /// Used by both the periodic timer and the hotkey shortcut (] then C).
    /// </summary>
    private void AnnounceNearestCity()
    {
        try
        {
            // Guard clause: Check if SimConnect is connected
            if (!simConnectManager.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("[MainForm] Nearest city announcement skipped: Not connected to simulator");
                return;
            }

            // Check if GeoNames API is configured
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            if (string.IsNullOrWhiteSpace(settings.GeoNamesApiUsername))
            {
                announcer.Announce("GeoNames API not configured. Please configure it in the settings.");
                return;
            }

            // Request current aircraft position with callback
            simConnectManager.RequestAircraftPositionAsync(async (position) =>
            {
                try
                {
                    // Get location data from GeoNames service
                    var geoNamesService = new GeoNamesService();
                    var locationData = await geoNamesService.GetLocationInfoAsync(position.Latitude, position.Longitude);

                    if (locationData?.NearbyPlaces != null && locationData.NearbyPlaces.Count > 0)
                    {
                        var nearestCity = locationData.NearbyPlaces[0];

                        // Format announcement (same format as LocationInfoForm)
                        string announcement = $"Near {nearestCity.Name}";
                        if (!string.IsNullOrEmpty(nearestCity.State))
                        {
                            announcement += $", {nearestCity.State}";
                        }
                        if (!string.IsNullOrEmpty(nearestCity.Country))
                        {
                            announcement += $", {nearestCity.Country}";
                        }
                        announcement += $", {nearestCity.Distance:F1} {settings.DistanceUnits} {nearestCity.Direction}";

                        // Check if over a body of water
                        var waterLandmark = locationData.Landmarks.FirstOrDefault(l => l.Type == "water");
                        if (waterLandmark != null)
                        {
                            announcement += $", {waterLandmark.Name}";
                        }

                        announcer.Announce(announcement);
                        System.Diagnostics.Debug.WriteLine($"[MainForm] Nearest city announced: {announcement}");
                    }
                    else
                    {
                        // No nearby cities found - check if over water
                        var waterLandmark = locationData?.Landmarks.FirstOrDefault(l => l.Type == "water");
                        if (waterLandmark != null)
                        {
                            announcer.Announce($"Over {waterLandmark.Name}");
                            System.Diagnostics.Debug.WriteLine($"[MainForm] Nearest city announced: Over {waterLandmark.Name}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[MainForm] Nearest city announcement skipped: No nearby cities found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainForm] Error in nearest city announcement callback: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error during nearest city announcement: {ex.Message}");
            // Don't announce errors to avoid interrupting the user
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Stop and dispose all timers to prevent them firing during/after shutdown
        eventBatchTimer?.Stop();
        eventBatchTimer?.Dispose();

        _panelLoadTimer?.Stop();
        _panelLoadTimer?.Dispose();

        nearestCityAnnouncementTimer?.Stop();
        nearestCityAnnouncementTimer?.Dispose();

        weatherAnnouncementTimer?.Stop();
        weatherAnnouncementTimer?.Dispose();

        _sdAutoRefreshTimer?.Stop();
        _sdAutoRefreshTimer?.Dispose();


        // Clean up taxi guidance, docking guidance, and ground traffic monitor
        taxiGuidanceManager?.Dispose();
        dockingGuidanceManager?.Dispose();
        groundTrafficMonitor?.Dispose();

        // Clean up the PROG-page monitor (owns a Windows-Forms timer; if not
        // disposed, the timer keeps a reference to OnTick and prevents the
        // monitor from being collected, and on shutdown the timer can fire
        // one more time against a half-torn-down data manager).
        pmdgProgPageMonitor?.Dispose();
        pmdgProgPageMonitor = null;

        // Clean up TCAS service
        tcasService?.Dispose();

        // Clean up ActiveSky weather-update monitor
        activeSkyWeatherMonitor?.Dispose();

        // Clean up A380X Coherent clients
        coherentClient?.Dispose();
        coherentEFBClient?.Dispose();
        coherentNDClient?.Dispose();
        coherentEWDClient?.Dispose();
        coherentFwsFailureClient?.Dispose();

        // Clean up PMDG EFB Coherent clients (otherwise only disposed on aircraft swap —
        // a user who opens the EFB then quits without switching aircraft leaks the socket + poll loop).
        coherentPmdgEfbCaptain?.Dispose();
        coherentPmdgEfbFirstOfficer?.Dispose();

        // Clean up 787 forms + the IRS / CAS Coherent clients
        hs787FMCForm?.Dispose();
        hs787SimBriefForm?.Dispose();
        hs787IrsClient?.Dispose();
        hs787IrsClient = null;
        hs787CasClient?.Dispose();
        hs787CasClient = null;

        // Clean up managers and resources
        hotkeyManager?.Cleanup();
        simConnectManager?.Disconnect();
        announcer?.Cleanup();
        base.OnFormClosing(e);
    }
}
