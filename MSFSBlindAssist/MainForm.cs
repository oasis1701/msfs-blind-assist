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
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist;
public partial class MainForm : Form
{
    // Event batching configuration - Proven pattern from aerospace/trading systems
    // Reduces UI thread marshaling overhead by ~95% for high-volume variable updates
    private const int EVENT_BATCH_INTERVAL_MS = 33; // ~30 batches/second (balances latency vs throughput)

    // Shared diagnostic-log channels used across MainForm's partial-class files
    // (MainForm.cs, MainForm.AircraftSwitch.cs, MainForm.Announcers.cs). Each
    // channel serializes all writers of the same file through the one LogWriter
    // background thread, fixing prior multi-writer interleaving/corruption.
    private static readonly LogChannel _landingExitLog = Log.Channel("landing_exit");
    private static readonly LogChannel _dockingAircraftLog = Log.Channel("docking-aircraft");
    private static readonly LogChannel _taxiAugmentLog = Log.Channel("taxi-augment");

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

    // Liftoff → Hand Fly handoff guard. The handoff is ARMED on the on-ground→
    // airborne edge but only PERFORMED after these confirm a real rotation, so a
    // spurious airborne sample (high-speed roll bump / oleo flicker, or low-speed
    // false-airborne from pushback/slope/replay) can't drop centerline guidance
    // during the roll.
    private System.Windows.Forms.Timer? _liftoffHandoffTimer;
    private const double LIFTOFF_HANDOFF_MIN_GS_KTS = 40.0; // reject pushback/slope/replay; every supported airframe rotates well above this
    // Must stay airborne this long before handing off. Exceeds the 1 Hz
    // continuous-batch sampling period of SIM_ON_GROUND so a settled bounce's
    // canceling ground sample is normally processed inside the window. The
    // cache alone is NOT trusted at fire time — at 1 Hz a touchdown in the
    // final second before the tick is invisible — so the tick confirms against
    // a fresh one-shot position read (SimOnGround + ground speed) before
    // performing the handoff; see PerformLiftoffHandoffIfValid.
    private const int    LIFTOFF_HANDOFF_CONFIRM_MS  = 1500;
    // How long the handoff mutes Hand Fly's own spoken pitch/bank/heading/VS
    // callouts so the breadcrumb ("Airborne. Takeoff assist off, hand fly
    // active.") can finish — the first post-activation callouts pass their
    // announce gates within one sim frame and AnnounceImmediate interrupts,
    // which would clip the breadcrumb after a syllable. The tone is unaffected;
    // spoken pitch resumes right after the window.
    private const int    LIFTOFF_HANDOFF_ANNOUNCE_GRACE_MS = 3500;
    // Outcome of the most recent RegisterHandFlyHotkeys() call, recorded by
    // OnHandFlyModeActiveChanged. The liftoff auto-handoff folds the
    // quick-access-keys warning into its breadcrumb: the breadcrumb's
    // AnnounceImmediate cancels pending speech on every backend
    // (nvdaController_cancelSpeech / Tolk interrupt / SAPI SpeakAsyncCancelAll),
    // which would otherwise silently swallow the handler's standalone warning.
    private bool _handFlyQuickKeysRegistered = true;
    // Generation token for the liftoff handoff's fresh-position confirm. The
    // confirm callback is a one-shot AircraftPositionReceived subscription that
    // LEAKS if the response never arrives (disconnect mid-request, or the
    // request call throwing after the subscribe) — a leaked handler fires on
    // the NEXT position response from ANY requester, potentially a later
    // flight's rotation, bypassing the debounce entirely. The tick captures the
    // token before requesting; every event that voids a pending handoff
    // (touchdown edge, disconnect, aircraft switch, TA deactivation) bumps it,
    // so a stale callback aborts on entry.
    private int _liftoffHandoffConfirmToken;

    // One-shot debounce that COALESCES status-list repaints. Many display vars can push within a
    // few ms of each other (the auto-refresh tick force-reads the whole panel at once), and each
    // push would otherwise rebuild + reconcile the entire list — O(N) work N times per cycle.
    // The timer is armed by the FIRST push and NOT restarted by later ones, so the repaint fires
    // a bounded 120 ms after the burst began. (A restart-per-push trailing debounce starved here:
    // hand-fly/takeoff-assist stream PLANE_PITCH/BANK/HEADING per SIM_FRAME — ~30-60 Hz — and
    // those are PFD/ISIS display vars, so the repaint deadline was pushed out forever and the
    // "live" list froze exactly while the aircraft was being hand-flown.)
    private System.Windows.Forms.Timer? _displayRepaintDebounce;

    // Cached view of currentAircraft.GetPanelDisplayVariables(). The aircraft defs rebuild that
    // dictionary from scratch on EVERY call (hundreds of interpolated strings on the FBW jets —
    // the same lag class GetVariables' _varCache fixed), and OnSimVarUpdated consults it PER
    // EVENT: with the 1 s display force-read firing an event per panel var per second, the
    // uncached call was thousands of allocations per second on the UI thread. Panel display sets
    // are static per aircraft-definition instance, so cache both the dict and a flat name set,
    // keyed on the aircraft instance (an aircraft switch invalidates automatically).
    private Dictionary<string, List<string>>? _panelDisplayVarsCache;

    private HashSet<string>? _displayVarNameCache;

    private IAircraftDefinition? _displayVarCacheOwner;

    private double _prevPrecipRate = -1;

    private double _prevInCloud = -1;

    private double _prevVisibility = -1;      // meters; -1 = uninitialized

    private bool _prevVisLow = false;         // was visibility below 1500m last check
    // ActiveSky precip descriptor last seen (#129): null = no AS baseline yet
    // (or AS not active), "" = no precip, else the parsed phrase ("light rain").
    private string? _prevAsPrecip;
    // On-demand ActiveSky client for the ambient-change precip source + the output+I
    // wind readout (caches its port so repeated queries are cheap).
    private readonly MSFSBlindAssist.Services.ActiveSkyClient weatherActiveSky = new();

    private readonly HashSet<string> _announcedSigmetKeys = new HashSet<string>();

    private readonly HashSet<string> _announcedPirepKeys  = new HashSet<string>();

    private DateTime _sigmetKeysClearedAt = DateTime.MinValue;

    // Reentrancy latch for CheckWeatherProximityAsync — the announcement timer tick fires
    // it fire-and-forget, and a slow WeatherService call could still be in flight when the
    // next tick lands.
    private bool _proximityCheckRunning = false;

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
        // Log.Debug("MainForm", "[MainForm] Running initial screen reader diagnostic test");
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
            catch (Exception ex) { Log.Debug("MainForm", $"GsxAirplaneProfile warm failed: {ex.Message}"); }
        });

        // MobiFlight end-to-end bridge probe: calc-write a nonce L:var, read it back
        // over the data-def channel; a match proves the WASM executed our RPN (the
        // only valid presence signal — the response side can be silent on healthy
        // installs). FBW defs register the probe var.
        _bridgeProbeTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _bridgeProbeTimer.Tick += BridgeProbeTimer_Tick;
        _bridgeProbeTimer.Start();

        // One-shot debounce for the liftoff → Hand Fly handoff (started on the
        // liftoff edge, stopped on touchdown; ticks once after the confirm window).
        _liftoffHandoffTimer = new System.Windows.Forms.Timer { Interval = LIFTOFF_HANDOFF_CONFIRM_MS };
        _liftoffHandoffTimer.Tick += (s, e) => PerformLiftoffHandoffIfValid();

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
            takeoffSettings.TakeoffAssistSteerTowardTone,
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

                try { _taxiAugmentLog.Info($"taxi-augment: data updated for {icao}"); }
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

        // ActiveSky weather-update announcer. Constructed always (so the settings
        // dialog can start it live via ApplyRuntimeSettings), but STARTED only when
        // ActiveSkyWeatherMonitor.ShouldRun says so — the user must have opted into
        // ActiveSky AND asked for weather announcements. When the AS switch is off no
        // AS code may run at all, and even when it's on but AS isn't running, each
        // poll would be a ~1.2 s parallel-probe timeout.
        activeSkyWeatherMonitor = new MSFSBlindAssist.Services.ActiveSkyWeatherMonitor(
            new MSFSBlindAssist.Services.ActiveSkyClient(), announcer);
        activeSkyWeatherMonitor.IntervalMinutes =
            MSFSBlindAssist.Settings.SettingsManager.Current.WeatherAutoAnnounceIntervalMinutes;
        activeSkyWeatherMonitor.Enabled = MSFSBlindAssist.Services.ActiveSkyWeatherMonitor
            .ShouldRun(MSFSBlindAssist.Settings.SettingsManager.Current);

        // Initialize event batching timer for high-volume variable updates
        // Timer runs on UI thread, draining the event queue in controlled batches
        eventBatchTimer = new System.Windows.Forms.Timer();
        eventBatchTimer.Interval = EVENT_BATCH_INTERVAL_MS;
        eventBatchTimer.Tick += ProcessEventBatch;
        // Timer starts when SimConnect connects (see OnConnectionStatusChanged)
        Log.Debug("MainForm", $"Event batching initialized: {EVENT_BATCH_INTERVAL_MS}ms interval, max {MAX_BATCH_SIZE} events/batch");

        // Initialize panel loading debounce timer (prevents NVDA overload during rapid arrow navigation)
        _panelLoadTimer = new System.Windows.Forms.Timer();
        _panelLoadTimer.Interval = 150; // 150ms delay - allows rapid navigation while preventing event queue buildup
        _panelLoadTimer.Tick += PanelLoadTimer_Tick;
        Log.Debug("MainForm", "Panel load debouncing initialized: 150ms delay");

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

    // --- User-set auto-announce de-dup (GLOBAL, all aircraft + all combo types) ---
    // When the user operates a panel combo, the screen reader already speaks the new
    // value. The same change ALSO comes back through OnSimVarUpdated and would be
    // auto-announced a second time by the monitor (per the "announce every state change"
    // rule). We record the var+value the user just committed, then suppress exactly that
    // echo once (updating the monitor baseline silently). A change to the SAME var from
    // ANY OTHER source (flyPad, ground crew, failure, systems-host) still announces,
    // because only the matching value within the short window is consumed.
    private readonly Dictionary<string, (double value, long tick)> _uiSetEcho = new();
    // Window sized for the SLOWEST echo path: the PMDG 777 CDA is POLLED at 1000 ms
    // (PMDG777DataManager._pollTimer), and a GUARDED switch (alt vent etc.) adds a
    // 150+150 ms guard-open/set/close sequence before the value even changes — so the
    // echo can land ~1.2-1.5 s after the combo commit and escaped the old 1500 ms
    // window (reported live on the 777 alt vent, 2026-07). Both gates below stay
    // consumed-once (+ the generic one value-matched), so a wider window does not eat
    // genuine later changes.
    private const int UiSetEchoSuppressMs = 3000;
    private void MarkUiSet(string? varName, double value)
    {
        if (!string.IsNullOrEmpty(varName)) _uiSetEcho[varName] = (value, Environment.TickCount64);
    }

    /// <summary>
    /// Cached <c>currentAircraft.GetPanelDisplayVariables()</c> (see the cache fields for why:
    /// the defs rebuild the dictionary per call and the per-event Step-3 gate made that a
    /// per-second allocation storm). Self-invalidates when the aircraft instance changes.
    /// </summary>
    private Dictionary<string, List<string>> GetPanelDisplayVarsCached()
    {
        if (_panelDisplayVarsCache == null || !ReferenceEquals(_displayVarCacheOwner, currentAircraft))
        {
            _panelDisplayVarsCache = currentAircraft.GetPanelDisplayVariables();
            _displayVarNameCache = new HashSet<string>(_panelDisplayVarsCache.Values.SelectMany(l => l));
            _displayVarCacheOwner = currentAircraft;
        }
        return _panelDisplayVarsCache;
    }

    /// <summary>Flat set of every display-var name across all panels — the O(1) form of
    /// "is this var used in any panel display" for the per-event OnSimVarUpdated gate.</summary>
    private HashSet<string> GetDisplayVarNamesCached()
    {
        GetPanelDisplayVarsCached();
        return _displayVarNameCache!;
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

    private string? _a32nxFlightInfoJs;

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

        _liftoffHandoffTimer?.Stop();
        _liftoffHandoffTimer?.Dispose();

        _displayRepaintDebounce?.Stop();
        _displayRepaintDebounce?.Dispose();

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
