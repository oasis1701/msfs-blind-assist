using System.Collections.Concurrent;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;
public partial class SimConnectManager
{
    // Shared diagnostic-log channels used across SimConnectManager's partial-class files
    // (Dispatch.cs, Setup.cs). Each channel serializes every writer of the same file
    // through the one LogWriter background thread.
    private static readonly LogChannel _registrationLog = Log.Channel("registration");
    private static readonly LogChannel _dockingAircraftLog = Log.Channel("docking-aircraft");
    private static readonly LogChannel _inputEventsLog = Log.Channel("input_events.txt");

    private Microsoft.FlightSimulator.SimConnect.SimConnect? simConnect;

    /// <summary>
    /// Internal access to SimConnect instance for aircraft-specific implementations.
    /// </summary>
    internal Microsoft.FlightSimulator.SimConnect.SimConnect? SimConnectInstance => simConnect;

    private IntPtr windowHandle;
    private const int WM_USER_SIMCONNECT = 0x0402;

    // Re-entrancy guard for ReceiveMessage(). The managed SimConnect ReceiveMessage()
    // is NOT reentrant: a nested call corrupts its internal receive buffer, which shows
    // up as a native heap-corruption access violation (0xC0000005) inside coreclr.dll
    // and a 0x80131506 ExecutionEngineException — uncatchable by managed try/catch.
    // Application.DoEvents() pump loops (used while waiting for data-definition changes
    // during aircraft switches/disconnect) can dispatch a queued WM_USER_SIMCONNECT and
    // re-enter ReceiveMessage() in the middle of one already in flight. This flag blocks
    // that; the skipped message stays queued and is drained on the next non-nested pump.
    //
    // SHARED (static) across BOTH SimConnect connections — the main one AND the always-on
    // GsxService connection (MSFSBA_GSX). All dispatch happens on the UI thread (WndProc /
    // DoEvents pumps), so a plain flag is sufficient. Making it shared is the key fix: a
    // DoEvents pump during one connection's ReceiveMessage could otherwise dispatch the
    // OTHER connection's WM_USER and interleave the two marshalling passes — the same
    // corruption, just across connections. While EITHER is dispatching, the other defers.
    internal static bool SimConnectDispatchInProgress;

    // Events
    public event EventHandler<string>? ConnectionStatusChanged;
    public event EventHandler<string>? SimulatorVersionDetected;
    public event EventHandler<SimVarUpdateEventArgs>? SimVarUpdated;
    public event EventHandler<AircraftPosition>? AircraftPositionReceived;
    public event EventHandler<AiTrafficDataEventArgs>? AiTrafficReceived;
    // Fired when a RequestAiTrafficData sweep delivers its final entry
    // (dwentrynumber == dwoutof). Lets callers announce/process a COMPLETE
    // traffic snapshot instead of racing the per-aircraft responses.
    public event EventHandler? AiTrafficSweepCompleted;
    public event EventHandler<WindData>? WindReceived;
    public event EventHandler<AmbientWeatherData>? WeatherDataReceived;
    public event EventHandler<NavRadioData>? NavRadioReceived;
    public event EventHandler<TakeoffRunwayReferenceEventArgs>? TakeoffRunwayReferenceSet;
    /// <summary>
    /// Fires when the loaded aircraft's ICAO type designator becomes known (on connect / aircraft change).
    /// The string is the extracted ICAO code (e.g. "B77W", "A20N") — may be empty if unresolved.
    /// </summary>
    public event EventHandler<string>? AircraftIcaoTypeDetected;

    // Aircraft definition
    private IAircraftDefinition? _currentAircraft;
    public IAircraftDefinition? CurrentAircraft
    {
        get => _currentAircraft;
        set
        {
            _currentAircraft = value;
            RebuildLedVarMap();
        }
    }

    // Cache of LedVariable -> owning SimVarDefinition for the current aircraft, rebuilt whenever
    // CurrentAircraft changes. Replaces a per-event `variables.Values.FirstOrDefault(v =>
    // v.LedVariable == e.VariableName)` LINQ scan over the full (~400-700 entry) variable set --
    // one MobiFlight default-channel push can raise up to 64 LVar events, each of which used to pay
    // for a fresh scan + closure. Built with TryAdd (first-wins in variables.Values enumeration
    // order) to replicate FirstOrDefault's semantics if two defs ever share a LedVariable; as of
    // this writing only FlyByWireA320Definition sets LedVariable and every value there is unique,
    // so this is a defensive equivalence guarantee rather than an observed collision.
    private Dictionary<string, SimVarDefinition> ledVarToDef = new();

    private void RebuildLedVarMap()
    {
        var map = new Dictionary<string, SimVarDefinition>();
        var variables = _currentAircraft?.GetVariables();
        if (variables != null)
        {
            foreach (var v in variables.Values)
            {
                if (!string.IsNullOrEmpty(v.LedVariable))
                {
                    map.TryAdd(v.LedVariable, v);
                }
            }
        }
        ledVarToDef = map;
    }

    // Connection state
    public bool IsConnected { get; private set; }
    public bool IsFullyConnected { get; private set; } // Set to true after aircraft detection completes
    public double AircraftWingSpan { get; private set; } // Wing span in feet, populated on connect
    private bool wasConnected = false; // Track if we've already announced connection state
    private System.Windows.Forms.Timer reconnectTimer = null!;
    // Aircraft-detection retry. RequestAircraftInfo() fires once at Connect() with PERIOD.ONCE;
    // on a heavy aircraft the one-shot AIRCRAFT_INFO/ATC response can be missed, so
    // IsFullyConnected never flips and every hotkey reports "not connected" (continuous
    // monitoring/auto-announce works — separate path). This timer re-requests every 2s until
    // detection completes, independent of continuous monitoring. (5 doors connected fine; 16
    // pushed setup past the tipping point — this makes it self-heal regardless of load.)
    private System.Windows.Forms.Timer _detectRetryTimer = null!;
    private int _detectRetryCount = 0;

    // MobiFlight WASM integration
    private MobiFlightWasmModule? mobiFlightWasm;
    // ⚠️ Gate ONLY on Initialize having completed (IsConnected). Two "smarter"
    // gates were tried 2026-06-11 and BOTH broke working installs (the A380 "STD
    // combo bounces back" regression):
    //   - IsRegistered: the FBWBA client registration can time out (documented
    //     "Timeout - Using Fallback" state) while the DEFAULT MobiFlight.Command
    //     channel works fine — the calc path needs no registration.
    //   - HasModuleResponded (any inbound response): on a real install the module's
    //     RESPONSE side was completely silent (no registration Finished, no MF.Pong)
    //     while the one-way COMMAND side executed everything — response-based
    //     evidence can never open the gate there.
    // So: module object initialized → fire the calc path. The no-WASM-install case
    // (writes into a dead CDA) is NOT detectable from responses; a proper presence
    // probe must be END-TO-END (calc-write a nonce L:var, read it back via the
    // data-def path). That end-to-end probe now EXISTS (MSFSBA_BRIDGE_PROBE →
    // CalcPathVerified, see below) and is the live gate for SetLVar/dotted-event
    // routing; IsMobiFlightConnected's gate role is now limited to the H: event
    // channel (which has no alternative transport).
    public bool IsMobiFlightConnected => mobiFlightWasm?.IsConnected == true;

    // End-to-end calc-path verification: MainForm's bridge probe calc-writes a nonce
    // L:var (MSFSBA_BRIDGE_PROBE, registered by the FBW defs) and reads it back over
    // the independent data-def channel. A match PROVES the WASM module executed our
    // RPN — the only presence signal that works when the module's response side is
    // silent (IsMobiFlightConnected is true even when no WASM module is installed,
    // because MobiFlightWasmModule.Initialize() is purely local client-data setup).
    // This IS the live gate for the calc-path write routing in SetLVar/SendEvent.
    public bool CalcPathVerified { get; private set; }

    // True once the probe has reached a conclusion either way: VERIFIED, or given up
    // (module absent / probe not applicable on this aircraft). Until concluded, dotted
    // custom events are queued rather than dropped (see SendEvent); once concluded
    // unverified, they fall back to the legacy TransmitClientEvent transport.
    public bool CalcPathProbeConcluded { get; private set; }

    public void MarkCalcPathVerified()
    {
        if (CalcPathVerified) return;
        CalcPathVerified = true;
        CalcPathProbeConcluded = true;
        FlushPendingCalcEvents();
    }

    /// <summary>
    /// Called by MainForm's bridge probe when verification cannot succeed: either the
    /// probe gave up (module absent or data-def read failing) or the loaded aircraft
    /// doesn't register the probe var (non-FBW). Queued dotted events are released to
    /// the legacy TransmitClientEvent transport; queued H: events are fired at the
    /// MobiFlight channel anyway (there is no alternative transport for H: events).
    /// </summary>
    public void MarkCalcPathProbeConcluded()
    {
        if (CalcPathProbeConcluded) return;
        CalcPathProbeConcluded = true;
        FlushPendingCalcEvents();
    }

    /// <summary>Re-arms the probe verdict (called from MainForm's probe re-arm path on
    /// reconnect/aircraft swap, alongside the Disconnect() reset).</summary>
    public void ResetCalcPathProbe()
    {
        CalcPathVerified = false;
        CalcPathProbeConcluded = false;
        // An aircraft swap re-arms the probe WITHOUT a MobiFlight teardown — drop any
        // events queued during the previous aircraft's probe window so they can't
        // replay at the new aircraft when the new verdict flushes. (Post-swap events
        // can't be in here: until this reset runs, the stale latched verdict routes
        // them immediately instead of queueing.)
        lock (pendingCalcEvents) pendingCalcEvents.Clear();
    }
    public bool CanSendHVars => mobiFlightWasm?.CanSendHVars == true;
    public string MobiFlightStatus => mobiFlightWasm?.ConnectionStatus ?? "Not Available";

    // PMDG data manager (generic slot; populated by InitializePMDG factory)
    private IPMDGDataManager? pmdgDataManager;
    public IPMDGDataManager? PMDGDataManager => pmdgDataManager;

    // ECAM data collection via MobiFlight
    private Dictionary<string, string> ecamStringData = new Dictionary<string, string>();
    private int ecamStringsReceived = 0;
    private int ecamTotalStringsExpected = 14;
    private Dictionary<string, string> ecamAnnouncementData = new Dictionary<string, string>();  // ECAM messages with color for announcements
    private HashSet<string> previousECAMMessages = new HashSet<string>();  // Track previous message set for change detection

    /// <summary>
    /// Controls whether ECAM message announcements are enabled.
    /// When true, ECAM message changes are suppressed to prevent initial startup noise.
    /// </summary>
    public bool SuppressECAMAnnouncements { get; set; } = true;

    /// <summary>
    /// Controls whether ECAM monitoring is enabled (user-controlled toggle).
    /// When false, ECAM announcements are disabled regardless of SuppressECAMAnnouncements.
    /// This is separate from SuppressECAMAnnouncements which is for startup grace period.
    /// </summary>
    public bool ECAMMonitoringEnabled { get; set; } = false;

    // Variable tracking for individual registrations
    private ConcurrentDictionary<string, int> variableDataDefinitions = new ConcurrentDictionary<string, int>();  // Maps variable keys to data definition IDs
    // Reverse of variableDataDefinitions (data definition/request ID -> variable key), kept in exact
    // sync at every add/remove/clear site of variableDataDefinitions. Lets the per-frame individual-var
    // response dispatch (ProcessIndividualVariableResponse, fired at up to SIM_FRAME rate for
    // HighFrequency vars like G_FORCE) do an O(1) TryGetValue instead of an O(n) FirstOrDefault scan.
    private ConcurrentDictionary<int, string> requestIdToVarKey = new ConcurrentDictionary<int, string>();
    private HashSet<string> forceUpdateVariables = new HashSet<string>();  // Track variables that should always fire updates
    // H:/dotted events fired while the MobiFlight WASM bridge is still connecting (the brief window
    // right after aircraft load). These have NO working TransmitClientEvent fallback, so they are queued
    // here and flushed when the bridge connects rather than dropped. Bounded + cleared on teardown.
    private readonly Queue<(string eventName, uint data)> pendingCalcEvents = new();
    private const int MaxPendingCalcEvents = 64;
    private ConcurrentDictionary<string, double> lastVariableValues = new ConcurrentDictionary<string, double>();  // Cache last values for change detection
    private int nextDataDefinitionId = 1000;  // Start IDs from 1000 to avoid conflicts
    private static int nextTempDefId = 50000;  // Counter for temporary definition IDs (SetLVar/SetSimVar)

    // Batched continuous variable monitoring (using unsafe pointers instead of reflection)
    // Multi-batch system: Maps variable key -> (batchNumber, indexWithinBatch)
    // batchNumber: 1-5, indexWithinBatch: 0-99
    private Dictionary<string, (int batchNum, int index)> continuousVariableIndexMap = new Dictionary<string, (int batchNum, int index)>();

    // Prebuilt per-batch arrays mirroring continuousVariableIndexMap, built once in
    // StartContinuousMonitoring (SimConnectManager.Setup.cs) and reused by every 1 Hz batch
    // delivery in ProcessContinuousBatchImpl (SimConnectManager.VarCache.cs). Avoids scanning the
    // whole ~700-var map 5x/second (skipping the 4 batches that aren't the current one) AND
    // re-resolving each SimVarDefinition via variables.TryGetValue per var — the varDef is
    // pre-resolved into the tuple instead. Indexed directly by batchNum (1-5, matching
    // continuousVariableIndexMap's batchNum); index 0 is unused padding so callers can index with
    // batchVarArrays[batchNum] without a -1 offset. continuousVariableIndexMap itself is kept —
    // other consumers (e.g. RequestVariable's ContainsKey check in DataRequests.cs) read it
    // independently of the batch loop. Cleared in lockstep with continuousVariableIndexMap:
    // rebuilt in StartContinuousMonitoring, reset to empty in Disconnect.
    private (string key, int index, SimVarDefinition def)[][] batchVarArrays =
    {
        Array.Empty<(string key, int index, SimVarDefinition def)>(), // index 0 (unused)
        Array.Empty<(string key, int index, SimVarDefinition def)>(), // batch 1
        Array.Empty<(string key, int index, SimVarDefinition def)>(), // batch 2
        Array.Empty<(string key, int index, SimVarDefinition def)>(), // batch 3
        Array.Empty<(string key, int index, SimVarDefinition def)>(), // batch 4
        Array.Empty<(string key, int index, SimVarDefinition def)>(), // batch 5
    };

    // Event handling
    private Dictionary<string, uint> eventIds = new Dictionary<string, uint>();
    private uint nextEventId = 1000;

    // SimConnect InputEvent (B:) — name → hash, populated by EnumerateInputEvents on
    // every aircraft load. The hash is required by SetInputEvent / SubscribeInputEvent.
    // OrdinalIgnoreCase so callers don't have to worry about exact casing of the WT/Asobo
    // InputEvent names. Reset on aircraft change so per-aircraft InputEvents don't leak.
    private readonly Dictionary<string, ulong> inputEventHashes =
        new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

    // Destination runway for distance calculations
    private Runway? destinationRunway;
    private Airport? destinationAirport;

    // ILS guidance request data
    private ILSData? currentILSRequest;
    private Runway? ilsRunway;
    private Airport? ilsAirport;

    // Last known aircraft position
    private AircraftPosition? lastKnownPosition;
    /// <summary>Returns the most recently received own-aircraft position without making a new request.</summary>
    public AircraftPosition? LastKnownPosition => lastKnownPosition;

    // Latest SIM_ON_GROUND sample as a tri-state (null = never sampled, true =
    // on the ground, false = airborne). MainForm caches its own copy too for
    // the Where Am I gate; this lets components that have a SimConnectManager
    // reference (LandingExitForm, etc.) check air/ground without taking a
    // separate dependency on MainForm.
    public bool? LastKnownOnGround { get; internal set; }

    // Aircraft identification
    private string currentAircraftAtcId = "";
    private string currentAircraftAirline = "";
    private string currentAircraftFlightNumber = "";
    private string currentAircraftAtcModel = ""; // raw ATC MODEL simvar value
    private string currentAircraftTitle = "";    // TITLE simvar value (aircraft.cfg [FLTSIM.N] title)
    /// <summary>Extracted ICAO type designator for the current aircraft (e.g. "B77W"). Empty if not yet known.</summary>
    public string CurrentAircraftIcaoType { get; private set; } = "";

    // Universal aircraft.cfg ICAO catalog — the runtime fallback used ONLY when the ATC MODEL
    // simvar doesn't resolve to a clean ICAO. Pure/dependency-light; its scan runs on a
    // background thread and never blocks the SimConnect callback.
    private readonly Services.AircraftCfgCatalog aircraftCfgCatalog = new();

    // Aircraft connection announcement - wait for both aircraft info and ATC data
    private AircraftInfo? pendingAircraftInfo = null;
    private bool atcDataReceived = false;

    // Simulator version detection
    public string DetectedSimulatorVersion { get; private set; } = "Unknown";

    // Data requests for specific functions
    internal enum DATA_REQUESTS
    {
        REQUEST_FCU_VALUES = 2,
        REQUEST_AIRCRAFT_INFO = 3,
        REQUEST_AIRCRAFT_POSITION = 4,
        REQUEST_WIND_DATA = 5,
        REQUEST_ATC_ID = 6,
        REQUEST_ILS_GUIDANCE = 7,
        // Multi-batch continuous monitoring (5 batches of ~100 variables each)
        REQUEST_CONTINUOUS_BATCH_1 = 8,
        REQUEST_CONTINUOUS_BATCH_2 = 9,
        REQUEST_CONTINUOUS_BATCH_3 = 10,
        REQUEST_CONTINUOUS_BATCH_4 = 11,
        REQUEST_CONTINUOUS_BATCH_5 = 12,
        // Panel batch for OnRequest variables
        REQUEST_PANEL_BATCH = 13,
        REQUEST_WEATHER_DATA = 14,
        // Hotkey readout requests (one-shot, used by aircraft definitions)
        REQUEST_HEADING = 300,
        REQUEST_SPEED = 301,
        REQUEST_ALTITUDE = 302,
        REQUEST_ALTITUDE_AGL = 303,
        REQUEST_ALTITUDE_MSL = 304,
        REQUEST_AIRSPEED_IAS = 305,
        REQUEST_AIRSPEED_TAS = 306,
        REQUEST_GROUND_SPEED = 307,
        REQUEST_VERTICAL_SPEED = 308,
        REQUEST_HEADING_MAG = 309,
        REQUEST_HEADING_TRUE = 310,
        REQUEST_MACH = 311,
        REQUEST_PITCH = 312,
        REQUEST_BANK = 313,
        REQUEST_FUEL_QUANTITY = 314,
        REQUEST_WAYPOINT_INFO = 315,
        REQUEST_FLAP_POSITION = 316,
        REQUEST_GEAR_POSITION = 317,
        REQUEST_FUEL_QUANTITY_KG = 318,
        REQUEST_GROSS_WEIGHT = 319,
        REQUEST_GROSS_WEIGHT_KG = 320,
        REQUEST_FUEL_QUANTITY_FBW = 321,
        REQUEST_NAV_RADIO = 322,
        REQUEST_OUTSIDE_TEMP = 323,
        // 324-328 used by hardcoded takeoff assist / hand fly requests
        REQUEST_SQUAWK_CODE = 329,
        // 330-337 used by hardcoded V-speed requests.
        // Use the gaps at 338 / 339 for time-of-day.
        REQUEST_LOCAL_TIME = 338,
        REQUEST_ZULU_TIME = 339,
        REQUEST_AI_TRAFFIC = 500,
        // Aircraft-specific InputEvent (B:) catalog enumeration.
        REQUEST_ENUMERATE_INPUT_EVENTS = 700,
        // Individual variable requests start from 1000
        INDIVIDUAL_VARIABLE_BASE = 1000
    }

    internal enum DATA_DEFINITIONS
    {
        FCU_VALUES = 2,
        AIRCRAFT_INFO = 3,
        INIT_POSITION = 4,
        AIRCRAFT_POSITION = 5,
        WIND_DATA = 6,
        ATC_ID_INFO = 7,
        // Multi-batch continuous monitoring (5 batches of ~100 variables each)
        CONTINUOUS_BATCH_1 = 8,
        CONTINUOUS_BATCH_2 = 9,
        CONTINUOUS_BATCH_3 = 10,
        CONTINUOUS_BATCH_4 = 11,
        CONTINUOUS_BATCH_5 = 12,
        // Panel batch for OnRequest variables
        PANEL_REQUEST_BATCH = 13,
        // Visual guidance consolidated data
        VISUAL_GUIDANCE_DATA = 14,
        // Takeoff assist consolidated data
        TAKEOFF_ASSIST_DATA = 15,
        // Ambient weather data (on-request)
        WEATHER_DATA = 16,
        // Hotkey readout definitions (one-shot, used by aircraft definitions)
        DEF_HEADING = 300,
        DEF_SPEED = 301,
        DEF_ALTITUDE = 302,
        DEF_ALTITUDE_AGL = 303,
        DEF_ALTITUDE_MSL = 304,
        DEF_AIRSPEED_IAS = 305,
        DEF_AIRSPEED_TAS = 306,
        DEF_GROUND_SPEED = 307,
        DEF_VERTICAL_SPEED = 308,
        DEF_HEADING_MAG = 309,
        DEF_HEADING_TRUE = 310,
        DEF_MACH = 311,
        DEF_PITCH = 312,
        DEF_BANK = 313,
        DEF_FUEL_QUANTITY = 314,
        DEF_WAYPOINT_INFO = 315,
        DEF_FLAP_POSITION = 316,
        DEF_GEAR_POSITION = 317,
        DEF_FUEL_QUANTITY_KG = 318,
        DEF_GROSS_WEIGHT = 319,
        DEF_GROSS_WEIGHT_KG = 320,
        DEF_FUEL_QUANTITY_FBW = 321,
        DEF_NAV_RADIO = 322,
        DEF_OUTSIDE_TEMP = 323,
        // 324-328 used by hardcoded takeoff assist / hand fly definitions
        DEF_SQUAWK_CODE = 329,
        DEF_AI_TRAFFIC = 500,
        // Individual variable definitions start from 1000
        INDIVIDUAL_VARIABLE_BASE = 1000
    }
    
    private enum DEFINITIONS
    {
        Dummy = 0
    }

    /// <summary>IDs for SimConnect SubscribeToSystemEvent notifications.</summary>
    private enum SYSTEM_EVENT_ID : uint
    {
        AircraftLoaded = 9000
    }


    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct FCUValues
    {
        public double heading;
        public double speed;
        public double altitude;
    }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct AircraftInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string title;
        public double wingSpan;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct AircraftAtcData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string atcId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string atcType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string atcAirline;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string atcFlightNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string atcModel; // ATC MODEL simvar — usually the ICAO type designator (B77W, A20N, …)
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct InitPosition
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double Pitch;
        public double Bank;
        public double Heading;
        public uint OnGround;
        public uint Airspeed;
    }

    /// <summary>
    /// Data received per-object from RequestDataOnSimObjectType(AIRCRAFT).
    /// Field order must exactly match AddToDataDefinition call order for DEF_AI_TRAFFIC.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct AiTrafficData
    {
        public double Latitude;
        public double Longitude;
        public double AltitudeFt;
        public double HeadingMagnetic;
        public double GroundSpeedKnots;
        public double SimOnGround;        // 0.0 = airborne, 1.0 = on ground
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string AtcId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string AtcType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string AtcModel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string FromAirport;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string ToAirport;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string AtcAirline;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string TrafficState;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct AircraftPosition
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double HeadingMagnetic;
        public double MagneticVariation;
        public double GroundSpeedKnots;
        public double VerticalSpeedFPM;
        public double SimOnGround;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct VisualGuidanceData
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double HeadingMagnetic;
        public double MagneticVariation;
        public double GroundSpeedKnots;
        public double VerticalSpeedFPM;
        public double AGL;
        public double GroundTrack;
        // Attitude (radians from SimConnect; consumers convert to degrees + standard convention).
        // Added so visual guidance can run independently of HandFly mode — the current-attitude
        // follower tone needs live pitch/bank, and we don't want to gate VG on HandFly anymore.
        public double PitchRadians;
        public double BankRadians;
        // Angle of attack (radians from SimConnect; consumer converts to degrees). Fed into
        // VG's nominal-pitch baseline so the desired-tone reflects what the airplane actually
        // needs to fly given its current weight / flap / speed, instead of a static
        // TypicalApproachAoaDeg estimate. With autothrust holding Vref this is a near-constant;
        // gusts and configuration changes shift it transiently.
        public double AlphaRadians;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct TakeoffAssistData
    {
        public double Latitude;
        public double Longitude;
        public double Pitch;
        public double HeadingMagnetic;
        public double IndicatedAirspeedKnots;
        public double MagneticVariation;
        // Real ground velocity, separate from IAS. Required for the taxi-guidance
        // GS announcer because at low taxi speeds (under ~30 kt) IAS reads near
        // zero — pitot pressure differential is below the indicator's working
        // range — so substituting IAS for GS made the announcer say "0 kt" at
        // 5 kt actual GS and "10 kt" at 15-20 kt actual. Takeoff-assist still
        // reads IAS for its V-speed callouts (separate field, intentional).
        public double GroundVelocityKnots;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WindData
    {
        public double Direction;
        public double Speed;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct AmbientWeatherData
    {
        public double PrecipRate;      // AMBIENT PRECIP RATE, percent (0-100)
        public double PrecipState;     // AMBIENT PRECIP STATE, bitmask (0=none,1=rain,4=snow,8=freezing)
        public double InCloud;         // AMBIENT IN CLOUD, bool
        public double Visibility;      // AMBIENT VISIBILITY, meters
        public double Temperature;     // AMBIENT TEMPERATURE, Celsius
        public double WindDirection;   // AMBIENT WIND DIRECTION, degrees
        public double WindSpeed;       // AMBIENT WIND VELOCITY, knots
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct NavRadioData
    {
        public double Nav1Freq;
        public double Nav1HasNav;
        public double Nav1HasLocalizer;
        public double Nav1HasGlideSlope;
        public double Nav1HasDME;
        public double Nav1DME;
        public double Nav1Localizer;
        public double Nav1GlideSlope;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Nav1Ident;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Nav1Name;
        public double Nav1Obs;
        public double Nav2Freq;
        public double Nav2HasNav;
        public double Nav2HasLocalizer;
        public double Nav2HasGlideSlope;
        public double Nav2HasDME;
        public double Nav2DME;
        public double Nav2Localizer;
        public double Nav2GlideSlope;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Nav2Ident;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Nav2Name;
        public double Nav2Obs;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct ECAMMessages
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string leftLine1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string leftLine2;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string leftLine3;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string leftLine4;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string leftLine5;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string leftLine6;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string leftLine7;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string rightLine1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string rightLine2;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string rightLine3;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string rightLine4;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string rightLine5;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string rightLine6;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string rightLine7;
        public double masterWarning;
        public double masterCaution;
        public double stallWarning;
    }

    public SimConnectManager(IntPtr handle)
    {
        windowHandle = handle;
        
        reconnectTimer = new System.Windows.Forms.Timer();
        reconnectTimer.Interval = 5000;
        reconnectTimer.Tick += ReconnectTimer_Tick;

        _detectRetryTimer = new System.Windows.Forms.Timer();
        _detectRetryTimer.Interval = 2000;
        _detectRetryTimer.Tick += DetectRetryTimer_Tick;
    }

    // Re-request aircraft info until detection completes (IsFullyConnected). Stops itself
    // once connected. Ultimate fallback: after several retries, if aircraft info has arrived
    // but ATC data never did, stop waiting on ATC and complete detection so hotkeys unblock.
    private void DetectRetryTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsConnected || IsFullyConnected) { _detectRetryTimer.Stop(); return; }
        _detectRetryCount++;
        try { RequestAircraftInfo(); } catch { }
        if (_detectRetryCount >= 5 && pendingAircraftInfo.HasValue && !atcDataReceived)
        {
            atcDataReceived = true;
            TryAnnounceConnection();
        }
    }

    public void Connect()
    {
        try
        {
            simConnect = new Microsoft.FlightSimulator.SimConnect.SimConnect("FBWBA", windowHandle, WM_USER_SIMCONNECT, null, 0);
            IsConnected = true;  // Set IMMEDIATELY so StartContinuousMonitoring() can run
            reconnectTimer.Stop();

            SetupDataDefinitions();
            SetupEvents();
            RegisterClientEvents();

            // Initialize MobiFlight WASM module
            InitializeMobiFlight();

            // Detect and announce simulator version
            DetectedSimulatorVersion = Utils.SimulatorDetector.DetectRunningSimulator();
            Log.Debug("SimConnect", $"Detected simulator in Connect(): {DetectedSimulatorVersion}");

            if (DetectedSimulatorVersion == "FS2020")
            {
                SimulatorVersionDetected?.Invoke(this, "MSFS 2020 detected");
            }
            else if (DetectedSimulatorVersion == "FS2024")
            {
                SimulatorVersionDetected?.Invoke(this, "MSFS 2024 detected");
            }

            // Warm the universal aircraft.cfg ICAO catalog in the background so the rare
            // ATC-MODEL-miss fallback can resolve a TITLE→ICAO without any latency. Non-blocking.
            aircraftCfgCatalog.BeginBuild();

            // Check aircraft type — fire once now, and start the retry timer so a missed
            // one-shot response self-heals (otherwise IsFullyConnected can stick at false).
            _detectRetryCount = 0;
            RequestAircraftInfo();
            _detectRetryTimer.Stop();
            _detectRetryTimer.Start();
        }
        catch (COMException)
        {
            IsConnected = false;

            // Only announce disconnection if we were previously connected
            if (wasConnected)
            {
                ConnectionStatusChanged?.Invoke(this, "Disconnected from simulator");
                wasConnected = false;
                Log.Debug("SimConnect", "Connection lost - announced disconnection");
            }
            else
            {
                // Log.Debug("SimConnect", "Connection attempt failed - no announcement (not previously connected)");
            }

            reconnectTimer.Start();
        }
    }

    private void ReconnectTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsConnected)
        {
            Connect();
        }
    }

    /// <summary>
    /// Start continuous monitoring for variables marked as announced.
    /// Uses multi-batch SimConnect requests for better performance and scalability:
    /// - Splits continuous variables into 5 batches of ~100 variables each
    /// - Each batch requests updates at SIMCONNECT_PERIOD.SECOND
    /// - Reduces SimConnect load per request while maintaining simultaneous updates
    /// - Scales to 500+ variables across all batches
    /// </summary>
    private static int batchSetupCounter = 0;  // Track how many times this is called

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct SingleValue
    {
        public double value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct FCUValueWithStatus
    {
        public double value;
        public double managedStatus;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WaypointInfo
    {
        public double ident0;
        public double ident1;
        public double distance;
        public double bearing;
    }

    public void ProcessWindowMessage(ref Message m)
    {
        if (m.Msg == WM_USER_SIMCONNECT && simConnect != null)
        {
            // Never dispatch ReceiveMessage() reentrantly (see _inReceiveMessage). A DoEvents()
            // pump can land us back here while an outer ReceiveMessage() is still on the stack;
            // skipping leaves the data queued for the next clean pump rather than corrupting the
            // marshalling buffer.
            if (SimConnectDispatchInProgress) return;
            SimConnectDispatchInProgress = true;
            try
            {
                simConnect.ReceiveMessage();
            }
            catch (COMException ex)
            {
                // SimConnect disposed or MSFS closed - log and ignore
                Log.Debug("SimConnect", $"SimConnect ReceiveMessage COM exception (expected during disconnect): {ex.Message}");
            }
            catch (NullReferenceException ex)
            {
                // SimConnect became null between check and call - log and ignore
                Log.Debug("SimConnect", $"SimConnect ReceiveMessage null reference (expected during disconnect): {ex.Message}");
            }
            catch (Exception ex)
            {
                // Unexpected exception - log but don't crash
                Log.Debug("SimConnect", $"Unexpected exception in ProcessWindowMessage: {ex}");
            }
            finally
            {
                SimConnectDispatchInProgress = false;
            }
        }
    }

    // MobiFlight WASM Integration Methods
    private void InitializeMobiFlight()
    {
        try
        {
            mobiFlightWasm = new MobiFlightWasmModule(simConnect!);

            // Subscribe to MobiFlight events
            mobiFlightWasm.ConnectionStatusChanged += (sender, status) =>
            {
                Log.Debug("SimConnect", $"MobiFlight status: {status}");
                // Release any H: events queued during the connect window. Dotted events stay
                // queued until the end-to-end probe concludes (see MarkCalcPathVerified /
                // MarkCalcPathProbeConcluded).
                if (IsMobiFlightConnected) FlushPendingHEvents();
            };

            mobiFlightWasm.LVarUpdated += MobiFlightWasm_LVarUpdated;
            mobiFlightWasm.LedValueReceived += MobiFlightWasm_LedValueReceived;
            mobiFlightWasm.ResponseReceived += (sender, response) =>
            {
                Log.Debug("SimConnect", $"MobiFlight response: {response}");
            };

            // Initialize the WASM module
            mobiFlightWasm.Initialize();

            Log.Debug("SimConnect", "MobiFlight WASM module initialized");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Failed to initialize MobiFlight: {ex.Message}");
            mobiFlightWasm = null;
        }
    }

    private void MobiFlightWasm_LVarUpdated(object? sender, MobiFlightWasmModule.MobiFlightLVarUpdateEventArgs e)
    {
        try
        {
            // Find the corresponding variable definition via the cached LED-var lookup
            ledVarToDef.TryGetValue(e.VariableName, out var varDef);

            if (varDef != null)
            {
                // Trigger SimVar update event for LED state changes
                var updateArgs = new SimVarUpdateEventArgs
                {
                    VarName = e.VariableName,
                    Value = e.Value,
                    Description = $"{varDef.DisplayName} LED"
                };

                SimVarUpdated?.Invoke(this, updateArgs);
                // Log.Debug("SimConnect", $"LED update: {varDef.DisplayName} LED = {(e.Value > 0 ? "On" : "Off")}");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error processing MobiFlight LVar update: {ex.Message}");
        }
    }

    private void MobiFlightWasm_LedValueReceived(object? sender, MobiFlightWasmModule.MobiFlightLedValueEventArgs e)
    {
        try
        {
            // Find the corresponding variable definition via the cached LED-var lookup
            ledVarToDef.TryGetValue(e.LedVariable, out var varDef);

            // Fallback: route a one-shot MobiFlight read by var KEY when no def
            // declares it as a LedVariable. Used for FCU readouts (e.g. the VS
            // selected target) whose SimConnect data-def read is unreliable, so
            // ReadLedVariable(key) can deliver the correct MobiFlight value under
            // the var's own name without setting LedVariable (which would make
            // MainForm re-request it over the unreliable SimConnect path).
            var variables = CurrentAircraft?.GetVariables();
            if (varDef == null && variables != null && variables.TryGetValue(e.LedVariable, out var byKey))
            {
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = e.LedVariable,
                    Value = e.Value,
                    Description = byKey.DisplayName
                });
                return;
            }

            if (varDef != null)
            {
                // Trigger SimVar update event for LED state changes
                var updateArgs = new SimVarUpdateEventArgs
                {
                    VarName = e.LedVariable,
                    Value = e.Value,
                    Description = $"{varDef.DisplayName} LED"
                };

                SimVarUpdated?.Invoke(this, updateArgs);
                // Log.Debug("SimConnect", $"LED value received: {varDef.DisplayName} LED = {(e.Value > 0 ? "On" : "Off")}");
            }
            else
            {
                Log.Debug("SimConnect", $"LED variable not found in definitions: {e.LedVariable}");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error processing MobiFlight LED value: {ex.Message}");
        }
    }

    public void InitializePMDG(IAircraftDefinition aircraft)
    {
        if (simConnect == null || !IsConnected) return;
        DisposePMDG();
        pmdgDataManager = aircraft.AircraftCode switch
        {
            "PMDG_777" => new PMDG777DataManager(),
            "PMDG_737" => new PMDGNG3DataManager(),
            _ => null
        };

        pmdgDataManager?.Initialize(simConnect, mobiFlightWasm);
    }

    public void DisposePMDG()
    {
        pmdgDataManager?.Dispose();
        pmdgDataManager = null;
    }

    public void Disconnect()
    {
        // Stop reconnect timer first to prevent it from firing during cleanup
        reconnectTimer.Stop();
        _detectRetryTimer.Stop();
        Log.Debug("SimConnect", "Reconnect timer stopped");

        // Disconnect MobiFlight WASM module
        if (mobiFlightWasm != null)
        {
            mobiFlightWasm.Disconnect();
            mobiFlightWasm.Dispose();
            mobiFlightWasm = null;
            CalcPathVerified = false;        // re-probe after the next bridge init
            CalcPathProbeConcluded = false;
            lock (pendingCalcEvents) pendingCalcEvents.Clear();   // don't carry queued events across a teardown
            Log.Debug("SimConnect", "MobiFlight WASM module disconnected");
        }

        if (simConnect != null)
        {
            try
            {
                // ===== CRITICAL: Clean up SimConnect resources BEFORE disposing =====
                // Without this, data definitions and requests remain registered server-side,
                // causing crashes when restarting the app quickly (< 5-10 seconds).
                // This was the root cause of Fenix A320's intermittent crashes on restart.
                Log.Debug("SimConnect", "Cleaning up SimConnect resources before disconnect...");

                // 1. Cancel all 5 continuous batch requests
                var batchConfigs = new[]
                {
                    (DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_1, DATA_DEFINITIONS.CONTINUOUS_BATCH_1, "Batch 1"),
                    (DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_2, DATA_DEFINITIONS.CONTINUOUS_BATCH_2, "Batch 2"),
                    (DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_3, DATA_DEFINITIONS.CONTINUOUS_BATCH_3, "Batch 3"),
                    (DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_4, DATA_DEFINITIONS.CONTINUOUS_BATCH_4, "Batch 4"),
                    (DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_5, DATA_DEFINITIONS.CONTINUOUS_BATCH_5, "Batch 5")
                };

                foreach (var (request, definition, name) in batchConfigs)
                {
                    try
                    {
                        simConnect.RequestDataOnSimObject(
                            request,
                            definition,
                            SIMCONNECT_OBJECT_ID_USER,
                            SIMCONNECT_PERIOD.NEVER,  // Cancel recurring updates
                            SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                            0, 0, 0
                        );
                        Log.Debug("SimConnect", $"Cancelled {name} request");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("SimConnect", $"Error cancelling {name} request (may not exist): {ex.Message}");
                    }
                }

                // 2. Wait with message pumping for cancellation to process
                // CRITICAL: Must pump messages so SimConnect can process the cancellation
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 500)
                {
                    try
                    {
                        System.Windows.Forms.Application.DoEvents();  // Pump SimConnect messages
                        Thread.Sleep(10);
                    }
                    catch (Exception ex)
                    {
                        // If MSFS closes during cleanup, DoEvents may throw - log and break
                        Log.Debug("SimConnect", $"Message pump exception during disconnect (expected if MSFS closed): {ex.Message}");
                        break;
                    }
                }
                Log.Debug("SimConnect", "Waited 500ms for batch request cancellations to process");

                // 3. Clear all 5 batch data definitions
                foreach (var (request, definition, name) in batchConfigs)
                {
                    try
                    {
                        simConnect.ClearDataDefinition(definition);
                        Log.Debug("SimConnect", $"Cleared {name} definition");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("SimConnect", $"Error clearing {name} definition (may not exist): {ex.Message}");
                    }
                }

                // 4. Clear all individual variable data definitions
                int clearedCount = 0;
                foreach (var kvp in variableDataDefinitions)
                {
                    try
                    {
                        simConnect.ClearDataDefinition((DATA_DEFINITIONS)kvp.Value);
                        clearedCount++;
                    }
                    catch
                    {
                        // Ignore failures - definition may already be cleared
                    }
                }
                Log.Debug("SimConnect", $"Cleared {clearedCount}/{variableDataDefinitions.Count} individual data definitions");

                Log.Debug("SimConnect", "SimConnect resource cleanup complete!");
                // ===== END OF CLEANUP SECTION =====

                // Unregister event handlers before disposal to ensure clean disconnect
                simConnect.OnRecvOpen -= SimConnect_OnRecvOpen;
                simConnect.OnRecvQuit -= SimConnect_OnRecvQuit;
                simConnect.OnRecvSimobjectData -= SimConnect_OnRecvSimobjectData;
                simConnect.OnRecvSimobjectDataBytype -= SimConnect_OnRecvSimobjectDataBytype;
                simConnect.OnRecvClientData -= SimConnect_OnRecvClientData;
                simConnect.OnRecvException -= SimConnect_OnRecvException;
                simConnect.OnRecvEventFilename -= SimConnect_OnRecvEventFilename;

                Log.Debug("SimConnect", "Event handlers unregistered, disposing SimConnect...");
            }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"Error unregistering event handlers: {ex.Message}");
            }

            simConnect.Dispose();
            simConnect = null;
            Log.Debug("SimConnect", "SimConnect disposed");
        }

        // Clear all internal state dictionaries to ensure clean reconnection
        variableDataDefinitions.Clear();
        requestIdToVarKey.Clear();
        lastVariableValues.Clear();
        continuousVariableIndexMap.Clear();
        for (int i = 0; i < batchVarArrays.Length; i++)
            batchVarArrays[i] = Array.Empty<(string key, int index, SimVarDefinition def)>();
        eventIds.Clear();
        lock (forceUpdateVariables) { forceUpdateVariables.Clear(); }
        ecamStringData.Clear();
        ecamAnnouncementData.Clear();
        previousECAMMessages.Clear();
        Log.Debug("SimConnect", "All internal state dictionaries cleared");

        IsConnected = false;
        IsFullyConnected = false;

        // Only announce disconnection if we were previously connected
        if (wasConnected)
        {
            ConnectionStatusChanged?.Invoke(this, "Disconnected from simulator");
            wasConnected = false;
            Log.Debug("SimConnect", "Intentional disconnect - announced disconnection");
        }
        else
        {
            Log.Debug("SimConnect", "Disconnect called but was not previously connected");
        }

        // Restart reconnect timer AFTER cleanup is complete (we stopped it at the beginning)
        // This prevents race conditions during cleanup while still enabling auto-reconnect
        reconnectTimer.Start();
        Log.Debug("SimConnect", "Disconnect complete - reconnect timer restarted");
    }

    private enum EVENTS
    {
        // Events will be dynamically assigned starting from 1000
    }

    private enum GROUP_PRIORITY : uint
    {
        HIGHEST = 1,
        HIGH = 2,
        STANDARD = 1000000000,
        DEFAULT = 1000000000,
        LOWEST = 4000000000
    }
}

public class AiTrafficDataEventArgs : EventArgs
{
    public uint   ObjectId         { get; set; }
    public string Callsign         { get; set; } = "";
    public string AircraftType     { get; set; } = "";
    public double Latitude         { get; set; }
    public double Longitude        { get; set; }
    public double AltitudeFt       { get; set; }
    public double HeadingMagnetic  { get; set; }
    public double GroundSpeedKnots { get; set; }
    public bool   OnGround         { get; set; }
    public string FromAirport      { get; set; } = "";
    public string ToAirport        { get; set; } = "";
    public string Airline          { get; set; } = "";
}

public class SimVarUpdateEventArgs : EventArgs
{
    public string VarName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Description { get; set; } = string.Empty;
    public SimConnectManager.AircraftPosition? PositionData { get; set; }  // For visual guidance position updates

    /// <summary>
    /// True for events sourced from a PMDG initial baseline snapshot. UI
    /// caches should populate and controls should refresh, but announcers
    /// must skip — these represent app-load state, not user-triggered
    /// transitions. Other update paths (regular SimVar polls, hotkey
    /// requests) always leave this false.
    /// </summary>
    public bool IsInitialSnapshot { get; set; }
}

public class TakeoffRunwayReferenceEventArgs : EventArgs
{
    public double ThresholdLat { get; set; }
    public double ThresholdLon { get; set; }
    public double RunwayHeadingTrue { get; set; }
    public double RunwayHeadingMagnetic { get; set; }
    public string RunwayID { get; set; } = string.Empty;
    public string AirportICAO { get; set; } = string.Empty;
}
