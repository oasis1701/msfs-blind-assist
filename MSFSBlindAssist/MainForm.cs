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
using MSFSBlindAssist.Services.SayIntentions;
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

    // The surroundings feature's OSM building tier: its OWN Overpass request, cached per ICAO in
    // memory. Separate from the taxiway fetch above so a mirror miss on the buildings can never
    // cost the taxiway names (it once did — the two rode one query).
    private MSFSBlindAssist.Services.Surroundings.OnlineFeatureStore? onlineFeatures;

    // Tier 3 of the surroundings feature: reads the installed scenery package's placement
    // BGLs for named buildings, cached on disk per package under %APPDATA%.
    private static readonly string SceneryIndexCacheDir =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "scenery-index");
    private readonly MSFSBlindAssist.Services.SceneryIndex.SceneryPackageIndexer sceneryIndexer = new(SceneryIndexCacheDir);

    // Which package models the airport, when navdata does not say — an MSFS 2024 database names
    // none for any airport. Shares the indexer's cache folder; its own census.json there.
    private readonly MSFSBlindAssist.Services.SceneryIndex.SceneryPackageCensus sceneryCensus = new(SceneryIndexCacheDir);

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

    // iFly 737 MAX8: CDU window (renders the SDK shared-memory screen) + the SP1
    // HTTP EFB tablet hosted in WebView2. The SDK client itself lives on the def.
    private Forms.IFly737.IFly737CDUForm? iflyCduForm;

    private Forms.IFly737.IFlyEfbForm? iflyEfbForm;

    // TFDi MD-11: all three MCDUs (Left/Center/Right) in one window, fed by the MD11MCDU
    // client data area. The form reads SimConnectManager for the live manager, so it survives
    // being opened before the sim connects.
    private Forms.MD11.Md11McduForm? md11McduForm;

    // TFDi MD-11 EFB — the shared FbwEfbForm over the Coherent debugger, pointed at the MD-11's
    // own EFB view. One tablet, so unlike PMDG there is no Captain/FO pair.
    private CoherentPmdgEfbClient? coherentMd11Efb;
    private Forms.FBWA380.FbwEfbForm? md11EfbForm;

    // MD-11 monitor manager (Ctrl+M) — the aircraft announces 532 annunciator lamps, so muting
    // them individually is not a nicety here.
    private Forms.MD11.Md11MonitorManagerForm? md11MonitorManagerForm;

    private Forms.IFly737.IFly737MonitorManagerForm? iflyMonitorManagerForm;

    private TakeoffAssistManager takeoffAssistManager = null!;

    // Manual-landing flare + rollout tone assist. Armed by the "Manual landing assist"
    // checkbox in the destination-runway dialog (unchecked by default → feature inert).
    private LandingFlareAssistManager flareAssistManager = null!;

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

    private MSFSBlindAssist.Services.VPilot.VatsimAnnouncementService? vatsimService;

    private Forms.WeatherRadarForm? weatherRadarForm;

    private MSFSBlindAssist.Forms.SayIntentionsInfoForm? surroundingsForm;

    private MSFSBlindAssist.Navigation.FlightPlanManager flightPlanManager = null!;

    private MSFSBlindAssist.Navigation.WaypointTracker waypointTracker = null!;

    private TaxiGuidanceManager taxiGuidanceManager = null!;

    private readonly MSFSBlindAssist.Services.SurroundingsCatalogCache surroundingsCache = new();

    private DockingGuidanceManager dockingGuidanceManager = null!;

    private TaxiAssistForm? taxiAssistForm;

    private LandingExitPlanner landingExitPlanner = null!;

    private GroundTrafficMonitor groundTrafficMonitor = null!;
    private MSFSBlindAssist.Services.AirportSurroundingsMonitor? surroundingsMonitor;
    private SayIntentionsService sayIntentionsService = null!;

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

    // MobiFlight end-to-end bridge probe state (see BridgeProbeTimer_Tick).
    private System.Windows.Forms.Timer? _bridgeProbeTimer;

    private int _bridgeProbeNonce = (Environment.TickCount & 0x3FFF) + 1; // 1..16384, never 0
    private int _bridgeProbePrevNonce;                                    // 0 = nothing written yet

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

    private readonly MSFSBlindAssist.Services.IceAccretionTracker _iceAccretionTracker = new();

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

    private readonly MSFSBlindAssist.Services.RouteAdvisoryProximityTracker _routeAdvisoryProximity = new();

    // Turnaround liftoff re-baseline for the route-advisory proximity tracker — see
    // Services/TurnaroundLiftoffDetector.cs for the touchdown+dwell+liftoff semantics.
    private readonly MSFSBlindAssist.Services.TurnaroundLiftoffDetector _turnaroundDetector = new();

    private bool _routeAdvisoryCheckRunning;

    // Consecutive-empty-feed counter for the route-advisory proximity tracker (M3, final
    // review): a single successfully-fetched empty feed ("No airmet/sigmet…") must NOT prune
    // every tracked zone in one tick — symmetric with LeaveConfirmTicks, a 1-tick feed flap
    // (e.g. ActiveSky reloading its flight plan) can't wipe zone state and re-announce
    // everything nearby as first-sight. Two consecutive empty ticks confirm a genuine plan
    // change before CheckRouteAdvisoriesAsync lets the prune through. Reset to 0 on any tick
    // that returns advisories, and on both tracker Reset sites.
    private int _emptyRouteFeedTicks;

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

        // The update check runs off Shown, not Load: the window is already up, so the
        // dialog has a parent and the message pump is running for the queued announcer.
        // It never blocks startup — the HTTP call is awaited after the form is visible.
        this.Shown += MainForm_Shown;
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

        // iFly 737 MAX8: start the shared-memory SDK bridge (independent of SimConnect —
        // it works whenever the sim + iFly plugin are running). Generic announcements
        // don't wait on SimConnect either — see StartIFlyAnnouncementGrace's call sites.
        StartIFlySdkBridge();

        // Don't set focus - let default tab order handle it for proper menu accessibility
    }

    /// <summary>
    /// Fires once, the first time the window is displayed. The bool is belt and braces:
    /// Shown is documented as first-display only, and a second update check would be
    /// harmless but pointless.
    /// </summary>
    private bool _startupUpdateCheckDone;

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        if (_startupUpdateCheckDone) return;
        _startupUpdateCheckDone = true;

        if (!SettingsManager.Current.CheckForUpdatesOnStartup) return;

        // A local build reports 0.0.0 (the csproj's placeholder — CI passes the real
        // -p:Version), which loses to every published tag, so without this every
        // developer launch would open the update dialog. The MANUAL check still offers
        // the update, which is the documented behaviour for a dev build.
        var current = Services.AppVersion.Current;
        if (current is null || (current.Major == 0 && current.Minor == 0 && current.Patch == 0))
        {
            // Logged because RunUpdateCheckAsync never runs here, so without this line the
            // skip is indistinguishable in debug.log from a check that silently failed.
            Log.Debug("Updates",
                $"Startup update check skipped: dev build (version {Services.AppVersion.DisplayString}).");
            return;
        }

        // Deliberately not awaited: startup must not wait on a network round-trip.
        // RunUpdateCheckAsync swallows everything when userInitiated is false.
        _ = RunUpdateCheckAsync(userInitiated: false);
    }

    private void InitializeManagers()
    {
        announcer = new ScreenReaderAnnouncer(this.Handle);

        // Guidance-tone routing notices. These arrive on the router's own worker thread, and
        // ScreenReaderAnnouncer silently no-ops off the UI thread, so this has to marshal.
        // BeginInvoke, never Invoke: the UI thread can be inside the settings save that asked
        // for the sweep. Queued Announce (never AnnounceImmediate) so a device notice can
        // never interrupt a hold-short or landing callout.
        Services.AudioOutputRouter.Shared.AnnounceRouteChange = message =>
        {
            try
            {
                if (!IsHandleCreated || IsDisposed)
                    return;

                BeginInvoke(() =>
                {
                    try { announcer.Announce(message); } catch { }
                });
            }
            catch (InvalidOperationException)
            {
                // Handle destroyed between the check and the post — nothing to announce to.
            }
        };

        // The startup BASELINE sweep (AudioOutputRouter.RequestBaselineSweep) is deliberately
        // NOT requested here, even though the sink above is what it needs: it is requested
        // from the connect timer's tick, immediately after "Initializing, please wait" —
        // see MainForm_Load. Requested here, its one startup phrase (a saved device that is
        // gone at launch) was spoken the instant the message pump started, into the same
        // first second as NVDA's own window/focus announcements — which cancel in-progress
        // speech — so the pilot heard it cut off (live report, 2026-08-20). Anchored after
        // the Initializing announcement it appends behind it in the screen reader's own
        // queue and is heard whole.

        // Note: Diagnostic test removed to prevent test speech on startup
        // Uncomment the next lines if you need to troubleshoot screen reader connections:
        // Log.Debug("MainForm", "[MainForm] Running initial screen reader diagnostic test");
        // announcer.TestScreenReaderConnection();

        simConnectManager = new SimConnectManager(this.Handle);
        simConnectManager.CurrentAircraft = currentAircraft;
        // The saved-aircraft path never goes through SwitchAircraft, so the MD-11's Attach (the
        // SimConnect handle and the UI SynchronizationContext its control-state hook needs) has
        // to happen here as well — otherwise every panel row opens without a state, and nothing
        // is described until the pilot's first press. Idempotent: a later switch re-attaches.
        if (currentAircraft is TFDiMD11Definition startupMd11) startupMd11.Attach(simConnectManager);
        simConnectManager.ConnectionStatusChanged += OnConnectionStatusChanged;
        // A calc path that never came up is a DEGRADED session on FBW aircraft — overhead
        // switches can silently revert and the FCU can ignore commands. Say so once, rather
        // than let the pilot find it one dead control at a time (which is what happened for
        // ten weeks). Queued: it must not cut across a callout.
        simConnectManager.CalcPathDegraded += (_, msg) =>
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (InvokeRequired) BeginInvoke(new Action(() => announcer.Announce(msg)));
            else announcer.Announce(msg);
        };
        simConnectManager.SimulatorVersionDetected += OnSimulatorVersionDetected;
        simConnectManager.SimVarUpdated += OnSimVarUpdated;
        simConnectManager.ContinuousBatchDelivered += OnContinuousBatchDelivered;
        simConnectManager.QueuedEventDispatched += OnQueuedEventDispatched;
        simConnectManager.TakeoffRunwayReferenceSet += OnTakeoffRunwayReferenceSet;
        simConnectManager.AircraftIcaoTypeDetected += OnAircraftIcaoTypeDetected;
        simConnectManager.AircraftLoaded += OnAircraftLoaded;
        simConnectManager.ConnectionLost += OnConnectionLost;

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
        // installs). The aircraft that opt in register the probe var (see BridgeProbeTimer_Tick).
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

        // Name every parking node of the graphs it builds the way the taxi dialog, the
        // gate-teleport list and gate.select name them, so Where-Am-I cannot say "Gate A 25"
        // about the stand every other readout calls "Gate B 25" (Services/ParkingSpotSource).
        // GetNamedSpots, NOT GetSelectableGates: this is navdata's own spot SET with its names
        // corrected in place, so the same nodes are marked Parking as before and the hold-short
        // and named-holding-point resolvers see an identical graph.
        //
        // The lambda reads `airportDataProvider` and builds its GateDataSource PER CALL rather
        // than capturing either: the field is reassigned on a database switch (and nulled on
        // teardown), and a shared GateDataSource instance would be touched from both the UI
        // thread and the SimConnect position callback that reaches Where-Am-I — its per-ICAO
        // caches are plain Dictionaries. Affordable because every consumer is a graph build
        // (once per airport, then cached), never a position update.
        taxiGuidanceManager.ParkingSpotSupplier = icao =>
        {
            var provider = airportDataProvider;
            return provider == null
                ? new List<MSFSBlindAssist.Database.Models.ParkingSpot>()
                : MSFSBlindAssist.Services.ParkingSpotSource.GetNamedSpots(provider, BuildGateDataSource(), icao);
        };

        // The staleness key for the graphs built from that supplier. Stand names are frozen
        // into a graph's nodes at build time, so a Where-Am-I graph built before GSX published
        // this airport would otherwise keep navdata's concourse letters for the whole session
        // while every other readout moved to GSX's — see TaxiGuidanceManager._whereAmICachedToken.
        // O(1) and builds no GateDataSource (GateListVersion), so it is cheap per press.
        taxiGuidanceManager.ParkingSpotVersionSupplier = GateListVersion;

        // Same token as the Where-Am-I graph, so a GSX publish re-letters the inferred concourses
        // too. Asked on every monitor sample, so it must stay as cheap as GateListVersion.
        surroundingsCache.VersionSupplier = GateListVersion;
        var catalogBuilder = new MSFSBlindAssist.Services.Surroundings.SurroundingsCatalogBuilder(
            () => airportDataProvider, BuildGateDataSource, () => onlineFeatures, sceneryCensus, sceneryIndexer,
            () => MSFSBlindAssist.Settings.SettingsManager.Current.SceneryIndexEnabled,
            () => MSFSBlindAssist.Settings.SettingsManager.Current.SimulatorVersion ?? "FS2020");
        surroundingsCache.BuildSupplier = catalogBuilder.Build;
        sayIntentionsService = new SayIntentionsService();

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
        // The two landing-rollout entries that can begin with no position stream running ask for
        // one (TaxiGuidanceManager.PositionStreamRequired).
        taxiGuidanceManager.PositionStreamRequired += (s, e) => simConnectManager.StartTaxiGuidanceMonitoring();
        taxiGuidanceManager.RequestTakeoffAssistAutoActivate += OnTaxiGuidanceRequestTakeoffAssistAutoActivate;

        // Landing exit planner — watches for touchdown and auto-activates taxi guidance
        // to the pre-selected exit taxiway. Opens via MainForm menu / hotkey.
        landingExitPlanner = new LandingExitPlanner(announcer, taxiGuidanceManager);

        // Manual-landing flare + rollout assist. Armed from the destination-runway
        // dialog's checkbox; sleeps until on approach (see ProcessSlowSample gate).
        // Delegates read live state so aircraft swaps and guidance-mode changes after
        // arming are always honored.
        flareAssistManager = new LandingFlareAssistManager(announcer,
            () => currentAircraft?.GetVisualGuidanceProfile()?.FlareAltitudeBiasFt ?? 12.0,
            () => visualGuidanceManager.IsActive,
            () => taxiGuidanceManager.IsLandingExitRolloutGuidanceActive,
            () => taxiGuidanceManager.IsLandingExitTaxiSteering,
            // With a landing-exit plan pending, the planner leads its own touchdown sentence with
            // the runway correction and interrupts — so the assist leaves the telling to it rather
            // than having its own sentence cut off mid-word.
            () => landingExitPlanner.HasPendingExit);
        flareAssistManager.MonitoringRequestChanged += OnFlareAssistMonitoringRequestChanged;
        flareAssistManager.EngagedChanged += OnFlareAssistEngagedChanged;
        simConnectManager.FlareAssistDataReceived += (s, d) => flareAssistManager.ProcessFrame(d);

        // Ground traffic monitor — proximity, route-aware and runway-watch callouts for on-ground
        // AI/multiplayer traffic. Ticks every second; sweeps its own small-radius traffic request every
        // second while something can change an answer, every three seconds otherwise; gates on
        // LastKnownOnGround each tick.
        groundTrafficMonitor = new GroundTrafficMonitor(announcer, simConnectManager);
        // Suppress proximity/route/queue callouts in three contexts: during the takeoff roll (pilot's
        // hands are on rudder + throttle, can't act on a callout — keyed on takeoff assist), when Taxi
        // Guidance is not engaged, and during a landing rollout that is still rolling. The rule lives in
        // Services/GroundTrafficSuppression so it can be pinned; the Alt+G summary stays ungated.
        groundTrafficMonitor.SuppressCheck = () =>
            GroundTrafficSuppression.Suppress(
                takeoffAssistManager.IsActive,
                taxiGuidanceManager.State,
                simConnectManager.LastKnownPosition?.GroundSpeedKnots);
        // The runway watch has its OWN gate: takeoff assist switches on at lineup alignment, and the
        // line-up wait is exactly when traffic landing on or entering the runway matters most, so the
        // watch keeps running until the takeoff roll passes 30 kt (PR #247 review R1).
        groundTrafficMonitor.RunwayWatchSuppressCheck = () =>
            GroundTrafficSuppression.SuppressRunwayWatch(
                takeoffAssistManager.IsActive,
                taxiGuidanceManager.State,
                simConnectManager.LastKnownPosition?.GroundSpeedKnots);
        // Route + runway context: traffic ON the route vs beside it, the queue, and the hold facts the
        // runway watch is derived from.
        groundTrafficMonitor.RouteContextProvider = () => taxiGuidanceManager.GetGroundTrafficContext();
        // Takeoff assist's runway while it is active: taxi guidance has stopped by then, so this is how
        // the watch knows which runway the pilot is lined up on.
        groundTrafficMonitor.TakeoffRunwayProvider = () =>
            takeoffAssistManager.IsActive
            && takeoffAssistManager.TryGetRunwayReference(out _, out _, out _, out _, out string runwayId, out string icao)
                ? (runwayId, icao)
                : null;
        // The airport's runways when takeoff assist has a runway but taxi guidance never built a route
        // there (a departure that starts on the runway): runway centerlines only, no taxi network.
        groundTrafficMonitor.RunwaySupplier = icao =>
        {
            var provider = airportDataProvider;
            if (provider == null || string.IsNullOrWhiteSpace(icao)) return Array.Empty<MSFSBlindAssist.Navigation.TaxiGraph.RunwayCenterline>();
            var starts = provider.GetRunwayStarts(icao);
            if (starts == null || starts.Count == 0) return Array.Empty<MSFSBlindAssist.Navigation.TaxiGraph.RunwayCenterline>();
            return MSFSBlindAssist.Navigation.TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(), starts,
                provider.GetRunways(icao)).RunwayCenterlines;
        };

        // Opt-in "Passing Concourse B, on the left." callouts while taxiing. Own 2 s poll
        // timer (same shape as groundTrafficMonitor above) — the taxi position stream is
        // taxi-scoped and off when no route is loaded, so this cannot ride it.
        surroundingsMonitor = new MSFSBlindAssist.Services.AirportSurroundingsMonitor(announcer, simConnectManager, () => airportDataProvider, surroundingsCache)
        {
            Enabled = MSFSBlindAssist.Settings.SettingsManager.Current.SurroundingsCalloutsEnabled,
            SurfaceCalloutsEnabled = MSFSBlindAssist.Settings.SettingsManager.Current.SurfaceChangeCalloutsEnabled,
            SuppressCheck = () =>
                takeoffAssistManager.IsActive
                || dockingGuidanceManager.IsActive
                || taxiGuidanceManager.State is TaxiGuidanceState.LandingRollout or TaxiGuidanceState.LiningUp
                    or TaxiGuidanceState.HoldShort or TaxiGuidanceState.ProgressiveHold
                    or TaxiGuidanceState.BacktrackingOnRunway or TaxiGuidanceState.BacktrackDeparture,
            // A takeoff without Takeoff Assist or a landing without an exit plan sets none of the
            // states above, so the pavement is asked directly.
            RunwayProbe = (icao, lat, lon) => taxiGuidanceManager.IsOnRunwayPavement(icao, lat, lon),
            // Runway rows only, never a taxi graph; prepared on the UI thread so it carries the
            // provider's database generation.
            PrepareRunwayProbeWarmUp = taxiGuidanceManager.PrepareRunwayShapeWarmUp,
        };

        // Per-aircraft rollout-anticipation lead for the taxi steering tone
        // (see IAircraftDefinition.TaxiTurnLeadSeconds).
        taxiGuidanceManager.TurnLeadSeconds = currentAircraft.TaxiTurnLeadSeconds;

        // Initialize airport database provider (optional - can be null if database not built yet)
        airportDataProvider = DatabaseSelector.SelectProvider();

        // Built unconditionally: the buildings tier needs no base provider, so a database built
        // mid-session still gets it (a switch Clear()s the store, never rebuilds it).
        var http = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(60) };
        // One Overpass client for both OSM readers (mirror cooldowns are process-wide anyway).
        var overpassClient = new MSFSBlindAssist.Services.TaxiAugment.OverpassClient(http);

        // Buildings have their own query, store and event; a catalog built before they landed is
        // invalidated here.
        var featureSource = new MSFSBlindAssist.Services.Surroundings.OsmFeatureSource(overpassClient);
        onlineFeatures = new MSFSBlindAssist.Services.Surroundings.OnlineFeatureStore(featureSource.FetchAsync)
        { Enabled = MSFSBlindAssist.Settings.SettingsManager.Current.TaxiAugmentEnabled };
        onlineFeatures.FeaturesUpdated += icao =>
        {
            surroundingsCache.Invalidate(icao);
            // …and tell the taxi dialog, whose Place list has no other way to learn that FBOs and
            // hangars (mostly OSM) arrived. Raised on a pool thread, so marshalled; the form stays
            // silent unless its list really changes.
            SafeBeginInvoke(() => taxiAssistForm?.OnSurroundingsInvalidated(icao));
        };

        // Seeded now, so the first Settings OK clears the catalog cache and OSM store only when
        // one of these settings really changed (unseeded, every first OK threw them all away).
        _appliedSceneryIndexEnabled = MSFSBlindAssist.Settings.SettingsManager.Current.SceneryIndexEnabled;
        _appliedTaxiAugmentEnabled = MSFSBlindAssist.Settings.SettingsManager.Current.TaxiAugmentEnabled;

        // Wrap with the taxi-data augmentation decorator (Phase 5).
        // The decorator is transparent: all IAirportDataProvider calls delegate to the base
        // except GetTaxiPaths, which enriches unnamed segments from OSM / X-Plane apt.dat.
        // Only wrap when a base provider is available — no DB means no decoration needed.
        if (airportDataProvider != null)
        {
            var sources = new System.Collections.Generic.List<MSFSBlindAssist.Services.TaxiAugment.ITaxiDataSource>
            {
                new MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource(overpassClient),
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

        // VATSIM announcements from vPilot. Constructed always so the settings dialog can
        // start it live via ApplyRuntimeSettings; ApplySettings is what installs the
        // plugin and starts the pipe server, and it does nothing at all while the master
        // switch is off (which is the default).
        vatsimService = new MSFSBlindAssist.Services.VPilot.VatsimAnnouncementService(announcer, this);
        var vatsimStartupInstall = vatsimService.ApplySettings(MSFSBlindAssist.Settings.SettingsManager.Current);
        if (vatsimStartupInstall != null)
        {
            // atStartup: only the outcomes the pilot could not otherwise notice. Dropping
            // the result here entirely — as this line used to — meant an app update that
            // shipped a new plugin while vPilot happened to be running went through as
            // Locked in total silence, and the pilot flew the whole leg on the old plugin.
            AnnounceVatsimInstallOutcome(vatsimStartupInstall, atStartup: true);
        }

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

            // The audio router's ONE seeding pass, anchored HERE — after "Initializing,
            // please wait" and never back in InitializeManagers where the announcement sink
            // is wired. The sweep seeds the last-target state silently either way, but its
            // one startup phrase (a saved guidance-tone device that is gone at launch:
            // "Guidance tone device X is not available…") must land AFTER the Initializing
            // announcement: requested at manager-init it spoke the moment the message pump
            // started, into the same first second as NVDA's own window/focus speech — which
            // cancels in-progress utterances — and the pilot heard it cut off (live report,
            // 2026-08-20). From here the sweep's notice reaches announcer.Announce a few tens
            // of milliseconds after the Initializing call, and both use the append-not-
            // interrupt speak, so the screen reader speaks them in order, each in full. The
            // sink it needs has been assigned since InitializeManagers, so the ordering
            // contract (sink first, baseline second) still holds; endpoint notifications in
            // the first two seconds now run as ordinary sweeps against unseeded state, which
            // is the documented pre-baseline behaviour for that window and self-heals — every
            // sweep stores the trio.
            Services.AudioOutputRouter.Shared.RequestBaselineSweep();

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
    // The flare assist only wants its SIM_FRAME feed while it is armed and on approach —
    // see LandingFlareAssistManager.ProcessSlowSample, which raises this at 1 Hz off
    // INDICATED_ALTITUDE. Running it for a whole flight would be pure waste.
    private void OnFlareAssistMonitoringRequestChanged(object? sender, bool wanted)
    {
        if (wanted)
            simConnectManager.StartFlareAssistMonitoring();
        else
            simConnectManager.StopFlareAssistMonitoring();
    }

    private void OnFlareAssistEngagedChanged(object? sender, bool engaged)
    {
        // Same interplay as visual guidance: the flare/rollout tone and HandFly's
        // attitude tone share the frequency-coded audio channel, so mute HandFly for
        // the duration. Resume only if VG isn't also holding the suppression.
        if (engaged)
            handFlyManager.SuppressAudio();
        else if (!visualGuidanceManager.IsActive)
            handFlyManager.ResumeAudio();
    }

    private void MarkUiSet(string? varName, double value)
    {
        if (!string.IsNullOrEmpty(varName)) _uiSetEcho[varName] = (value, Environment.TickCount64);
    }

    /// <summary>
    /// Public wrapper over <see cref="MarkUiSet"/> for hotkey windows that live outside
    /// MainForm (the PMDG Ctrl+P autopilot window) and set panel-monitored variables
    /// directly. Without it those windows triple-announce: the screen reader announces
    /// the click, the refreshed button label announces on focus, and the background
    /// monitor announces the change.
    /// <para>
    /// Pass the EXPECTED RESULTING value, not the press parameter. The generic
    /// _uiSetEcho gate is value-matched, so marking a momentary press with 1 would
    /// suppress an engage but let the matching DISENGAGE (annunciator 1 -> 0) leak
    /// through as a duplicate announcement.
    /// </para>
    /// </summary>
    public void SuppressUiEcho(string varKey, double expectedValue) => MarkUiSet(varKey, expectedValue);

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
        surroundingsMonitor?.Dispose();
        // Owns two tone generators — without this they keep sounding on shutdown.
        flareAssistManager?.Dispose();

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

        // Clean up the VATSIM pipe server (owns a background listener thread)
        vatsimService?.Dispose();

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

        // Same for the MD-11's windows + EFB client (same leak class, same swap-only teardown in
        // MainForm.AircraftSwitch.cs): the MCDU form's 250 ms poll timer would otherwise tick
        // through Disconnect()'s DoEvents pump, and the EFB client holds the ONE inspector socket
        // Coherent allows for that view. Forms first, then the client, as on the swap path.
        if (md11McduForm != null && !md11McduForm.IsDisposed) md11McduForm.Dispose();
        if (md11EfbForm != null && !md11EfbForm.IsDisposed) md11EfbForm.Dispose();
        // The Ctrl+M monitor manager holds no timer or socket; disposed here so every MD-11 window
        // ends the same way on exit as on a swap (MainForm.AircraftSwitch.cs).
        if (md11MonitorManagerForm != null && !md11MonitorManagerForm.IsDisposed) md11MonitorManagerForm.Dispose();
        md11MonitorManagerForm = null;
        coherentMd11Efb?.Dispose();

        // Clean up 787 forms + the IRS / CAS Coherent clients
        hs787FMCForm?.Dispose();
        hs787IrsClient?.Dispose();
        hs787IrsClient = null;
        hs787CasClient?.Dispose();
        hs787CasClient = null;

        // Clean up the iFly SDK client (otherwise only shut down on aircraft swap — a
        // quit with the iFly active would leave the 250 ms poll + off-sweep timers
        // firing into the teardown, the same leak class as the PMDG EFB clients above).
        if (currentAircraft is IFly737MAXDefinition iflyExitDef)
        {
            iflyExitDef.Sdk.VariableChanged -= OnIFlyVariableChanged;
            iflyExitDef.Shutdown();
        }

        // Definition-owned UI-thread timers (both FBW defs' TCAS RA compose timer) and any held
        // call-out. StopAllMotion is otherwise only reached from the aircraft-SWAP path, so a
        // quit left them live across simConnectManager.Disconnect()'s 500 ms DoEvents pump —
        // which dispatches WM_TIMER, so a timer could still speak on the way out. Same leak
        // class as the iFly def and the PMDG EFB clients handled above.
        (currentAircraft as FlyByWireA380Definition)?.StopAllMotion();
        (currentAircraft as FlyByWireA320Definition)?.StopAllMotion();
        currentAircraft?.CancelDeferredFlush();

        // The MD-11 def owns the CEVENT pump and, during a hold-to-test's 3 s, a button the sim
        // still has pressed. Dispose it here — BEFORE Disconnect() — so its Dispose (which queues
        // and drains any held test button's UP, then stops the pump; see Md11EventBus.Dispose)
        // writes into the sim while it is still connected and the MD-11 is still the loaded
        // aircraft. Otherwise this def, like the iFly SDK client and the PMDG EFB clients above,
        // was only ever torn down on the aircraft-swap path. The count of buttons actually
        // released (if any) is logged by Md11EventBus.Dispose itself, under its own [MD11] line —
        // not repeated here, since a bounded drain can fall short and this line must not claim a
        // release the drain could still have dropped.
        if (currentAircraft is TFDiMD11Definition md11ExitDef)
        {
            md11ExitDef.Dispose();
            Log.Debug("MD11", "App exit: definition disposed.");
        }

        // Stop listening BEFORE Disconnect(). Disconnect ends by raising ConnectionLost, whose
        // handler calls currentAircraft.OnSimContextReset() — so on this path it re-entered the
        // definition disposed six lines above and re-armed the seed gate Dispose had just
        // disarmed, against its own stated invariant ("nor may a pending seed pass run for one").
        // Nothing observable followed today, only because that method happens to be inert with a
        // null _sim and no delivery can follow; the next tracker added to it that owns a timer or
        // speaks would regress on exit with no warning. Unsubscribing is the fix that does not
        // depend on the body staying inert.
        if (simConnectManager != null) simConnectManager.ConnectionLost -= OnConnectionLost;

        // Clean up managers and resources
        hotkeyManager?.Cleanup();
        simConnectManager?.Disconnect();
        announcer?.Cleanup();

        // The router owns an IMMNotificationClient COM registration and a background worker;
        // Dispose unregisters the callback and stops the worker. Cleared first so a sweep that
        // is already in flight has nothing to marshal an announcement into on the way down.
        try
        {
            Services.AudioOutputRouter.Shared.AnnounceRouteChange = null;
            Services.AudioOutputRouter.Shared.Dispose();
        }
        catch { }

        base.OnFormClosing(e);
    }
}
