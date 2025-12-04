using System.Collections.Concurrent;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.SimConnect;
public class SimConnectManager
{
    private Microsoft.FlightSimulator.SimConnect.SimConnect? simConnect;

    /// <summary>
    /// Internal access to SimConnect instance for aircraft-specific implementations.
    /// </summary>
    internal Microsoft.FlightSimulator.SimConnect.SimConnect? SimConnectInstance => simConnect;

    private IntPtr windowHandle;
    private const int WM_USER_SIMCONNECT = 0x0402;

    // Events
    public event EventHandler<string>? ConnectionStatusChanged;
    public event EventHandler<string>? SimulatorVersionDetected;
    public event EventHandler<SimVarUpdateEventArgs>? SimVarUpdated;
    public event EventHandler<AircraftPosition>? AircraftPositionReceived;
    public event EventHandler<WindData>? WindReceived;
    public event EventHandler<ECAMDataEventArgs>? ECAMDataReceived;
    public event EventHandler<TakeoffRunwayReferenceEventArgs>? TakeoffRunwayReferenceSet;

    // Aircraft definition
    public IAircraftDefinition? CurrentAircraft { get; set; }

    // Connection state
    public bool IsConnected { get; private set; }
    public bool IsFullyConnected { get; private set; } // Set to true after aircraft detection completes
    private bool wasConnected = false; // Track if we've already announced connection state
    private System.Windows.Forms.Timer reconnectTimer = null!;

    // MobiFlight WASM integration
    private MobiFlightWasmModule? mobiFlightWasm;
    public bool IsMobiFlightConnected => mobiFlightWasm?.IsConnected == true;
    public bool CanSendHVars => mobiFlightWasm?.CanSendHVars == true;
    public string MobiFlightStatus => mobiFlightWasm?.ConnectionStatus ?? "Not Available";

    // ECAM data collection via MobiFlight
    private Dictionary<string, string> ecamStringData = new Dictionary<string, string>();
    private int ecamStringsReceived = 0;
    private int ecamTotalStringsExpected = 14;
    private double ecamMasterWarning = 0;
    private double ecamMasterCaution = 0;
    private double ecamStallWarning = 0;
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
    private ConcurrentDictionary<int, string> pendingRequests = new ConcurrentDictionary<int, string>();  // Track pending requests
    private HashSet<string> forceUpdateVariables = new HashSet<string>();  // Track variables that should always fire updates
    private ConcurrentDictionary<string, double> lastVariableValues = new ConcurrentDictionary<string, double>();  // Cache last values for change detection
    private int nextDataDefinitionId = 1000;  // Start IDs from 1000 to avoid conflicts
    private static int nextTempDefId = 50000;  // Counter for temporary definition IDs (SetLVar/SetSimVar)

    // Batched continuous variable monitoring (using unsafe pointers instead of reflection)
    // Multi-batch system: Maps variable key -> (batchNumber, indexWithinBatch)
    // batchNumber: 1-5, indexWithinBatch: 0-99
    private Dictionary<string, (int batchNum, int index)> continuousVariableIndexMap = new Dictionary<string, (int batchNum, int index)>();

    // Panel batch tracking for OnRequest variables
    private Dictionary<string, int> panelVariableIndexMap = new Dictionary<string, int>();  // Maps panel variable keys to batch field indices

    // Event handling
    private Dictionary<string, uint> eventIds = new Dictionary<string, uint>();
    private uint nextEventId = 1000;

    // Destination runway for distance calculations
    private Runway? destinationRunway;
    private Airport? destinationAirport;

    // ILS guidance request data
    private ILSData? currentILSRequest;
    private Runway? ilsRunway;
    private Airport? ilsAirport;

    // Last known aircraft position
    private AircraftPosition? lastKnownPosition;

    // Aircraft identification
    private string currentAircraftAtcId = "";
    private string currentAircraftAirline = "";
    private string currentAircraftFlightNumber = "";

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
        REQUEST_ECAM_MESSAGES = 350,
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
        ECAM_MESSAGES = 350,
        // Individual variable definitions start from 1000
        INDIVIDUAL_VARIABLE_BASE = 1000
    }
    
    private enum DEFINITIONS
    {
        Dummy = 0
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
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct TakeoffAssistData
    {
        public double Latitude;
        public double Longitude;
        public double Pitch;
        public double HeadingMagnetic;
        public double IndicatedAirspeedKnots;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WindData
    {
        public double Direction;
        public double Speed;
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
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Detected simulator in Connect(): {DetectedSimulatorVersion}");

            if (DetectedSimulatorVersion == "FS2020")
            {
                SimulatorVersionDetected?.Invoke(this, "MSFS 2020 detected");
            }
            else if (DetectedSimulatorVersion == "FS2024")
            {
                SimulatorVersionDetected?.Invoke(this, "MSFS 2024 detected");
            }

            // Check aircraft type
            RequestAircraftInfo();
        }
        catch (COMException)
        {
            IsConnected = false;

            // Only announce disconnection if we were previously connected
            if (wasConnected)
            {
                ConnectionStatusChanged?.Invoke(this, "Disconnected from simulator");
                wasConnected = false;
                System.Diagnostics.Debug.WriteLine("[SimConnectManager] Connection lost - announced disconnection");
            }
            else
            {
                // System.Diagnostics.Debug.WriteLine("[SimConnectManager] Connection attempt failed - no announcement (not previously connected)");
            }

            reconnectTimer.Start();
        }
    }

    /// <summary>
    /// Enables ECAM message announcements.
    /// Call this after initial connection to begin monitoring ECAM changes.
    /// </summary>
    public void EnableECAMAnnouncements()
    {
        SuppressECAMAnnouncements = false;
        System.Diagnostics.Debug.WriteLine("[SimConnectManager] ECAM announcements enabled");
    }

    /// <summary>
    /// Toggles ECAM monitoring on/off (user-controlled via hotkey).
    /// Returns the new state (true = enabled, false = disabled).
    /// </summary>
    public bool ToggleECAMMonitoring()
    {
        ECAMMonitoringEnabled = !ECAMMonitoringEnabled;
        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM monitoring {(ECAMMonitoringEnabled ? "enabled" : "disabled")}");
        return ECAMMonitoringEnabled;
    }

    private void ReconnectTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsConnected)
        {
            Connect();
        }
    }

    private void SetupDataDefinitions()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety

        // Register all variables as individual data definitions
        RegisterAllVariables();

        // Start continuous monitoring for announced variables
        StartContinuousMonitoring();

        // NOTE: FCU values are now handled by aircraft-specific implementations
        // Each aircraft definition (e.g., FlyByWireA320Definition) handles its own FCU variables

        // Register aircraft info
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_INFO, "TITLE", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
        sc.RegisterDataDefineStruct<AircraftInfo>(DATA_DEFINITIONS.AIRCRAFT_INFO);

        // Register ATC data separately (ID, Type, Airline, Flight Number)
        // Using STRING256 for all to ensure proper marshaling
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC ID", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC TYPE", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC AIRLINE", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC FLIGHT NUMBER", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)3);
        sc.RegisterDataDefineStruct<AircraftAtcData>(DATA_DEFINITIONS.ATC_ID_INFO);

        // Register INIT_POSITION for teleportation
        sc.AddToDataDefinition(DATA_DEFINITIONS.INIT_POSITION, "Initial Position", null,
            SIMCONNECT_DATATYPE.INITPOSITION, 0.0f, SIMCONNECT_UNUSED);
        sc.RegisterDataDefineStruct<InitPosition>(DATA_DEFINITIONS.INIT_POSITION);

        // Register aircraft position for distance calculations
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE LATITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE LONGITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE ALTITUDE", "feet",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE HEADING DEGREES MAGNETIC", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "MAGVAR", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)4);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "GROUND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)5);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "VERTICAL SPEED", "feet per minute",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)6);
        sc.RegisterDataDefineStruct<AircraftPosition>(DATA_DEFINITIONS.AIRCRAFT_POSITION);

        // Register visual guidance data (consolidated position + AGL + ground track)
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE LATITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE LONGITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE ALTITUDE", "feet",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE HEADING DEGREES MAGNETIC", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "MAGVAR", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)4);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "GROUND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)5);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "VERTICAL SPEED", "feet per minute",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)6);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE ALT ABOVE GROUND", "feet",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)7);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "GPS GROUND MAGNETIC TRACK", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)8);
        sc.RegisterDataDefineStruct<VisualGuidanceData>(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA);

        // Register takeoff assist data (consolidated position + pitch + heading + airspeed)
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE LATITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE LONGITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE PITCH DEGREES", "radians",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE HEADING DEGREES MAGNETIC", "radians",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "AIRSPEED INDICATED", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)4);
        sc.RegisterDataDefineStruct<TakeoffAssistData>(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA);

        // Register wind data for wind information
        sc.AddToDataDefinition(DATA_DEFINITIONS.WIND_DATA, "AMBIENT WIND DIRECTION", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WIND_DATA, "AMBIENT WIND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.RegisterDataDefineStruct<WindData>(DATA_DEFINITIONS.WIND_DATA);
    }

    /// <summary>
    /// Register all variables as individual data definitions
    /// </summary>
    private void RegisterAllVariables()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety
        int registeredCount = 0;
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();

        foreach (var kvp in variables)
        {
            var varDef = kvp.Value;

            // Skip write-only variables (Never frequency) and H-variables
            if (varDef.UpdateFrequency == UpdateFrequency.Never || varDef.Type == SimVarType.HVar)
                continue;

            // Get a unique data definition ID for this variable
            int dataDefId = nextDataDefinitionId++;

            try
            {
                // Register the variable based on its type
                if (varDef.Type == SimVarType.LVar)
                {
                    sc.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                        $"L:{varDef.Name}", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                }
                else if (varDef.Type == SimVarType.SimVar)
                {
                    sc.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                        varDef.Name, varDef.Units ?? "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                }

                // Register the SingleValue struct for this definition
                sc.RegisterDataDefineStruct<SingleValue>((DATA_DEFINITIONS)dataDefId);

                // Only add to dictionary if registration was successful
                variableDataDefinitions.TryAdd(kvp.Key, dataDefId);
                registeredCount++;

                // Log visual guidance variables specifically
                if (kvp.Key.StartsWith("VISUAL_GUIDANCE"))
                {
                    System.Diagnostics.Debug.WriteLine($"[RegisterAllVariables] Registered {kvp.Key} -> ID {dataDefId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register variable {kvp.Key}: {ex.Message}");
                // Don't add failed registrations to the dictionary
            }
        }

        System.Diagnostics.Debug.WriteLine($"Successfully registered {registeredCount} individual variables");
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

    private void StartContinuousMonitoring()
    {
        batchSetupCounter++;
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] ===== CALL #{batchSetupCounter} at {timestamp} =====");

        if (!IsConnected || simConnect == null)
        {
            System.Diagnostics.Debug.WriteLine("[StartContinuousMonitoring] Cannot start continuous monitoring - not connected");
            return;
        }

        var sc = simConnect; // Local reference for null-safety

        // Clear previous batch setup (important when switching aircraft or adding/removing variables)
        int previousMapSize = continuousVariableIndexMap.Count;
        continuousVariableIndexMap.Clear();
        System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] Cleared previous map (had {previousMapSize} entries)");

        // Get all continuous variables from current aircraft
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
        var continuousVariables = new List<KeyValuePair<string, SimVarDefinition>>();

        foreach (var kvp in variables)
        {
            if (kvp.Value.UpdateFrequency == UpdateFrequency.Continuous &&
                kvp.Value.IsAnnounced)
            {
                continuousVariables.Add(kvp);
            }
        }

        // CRITICAL: Sort variables alphabetically by FULL NAME (with prefix) to match SimConnect's internal ordering
        continuousVariables.Sort((a, b) =>
        {
            string aFullName = a.Value.Type == SimVarType.LVar ? $"L:{a.Value.Name}" : a.Value.Name;
            string bFullName = b.Value.Type == SimVarType.LVar ? $"L:{b.Value.Name}" : b.Value.Name;
            return string.CompareOrdinal(aFullName, bFullName);
        });

        System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] Aircraft: {CurrentAircraft?.AircraftName ?? "null"}");
        System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] Found {continuousVariables.Count} continuous+announced variables (out of {variables.Count} total)");

        if (continuousVariables.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] No continuous variables to monitor");
            return;
        }

        if (continuousVariables.Count > 500)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] WARNING: {continuousVariables.Count} continuous variables exceeds multi-batch capacity of 500 (5 batches × 100)!");
            // Continue anyway - we'll use as many batches as needed
        }

        try
        {
            // Split variables into 5 batches (up to 100 variables per batch)
            const int BATCH_SIZE = 100;
            const int NUM_BATCHES = 5;

            // Batch configuration: (batchNum, dataDefinition, dataRequest, structType)
            var batchConfigs = new[]
            {
                (1, DATA_DEFINITIONS.CONTINUOUS_BATCH_1, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_1, typeof(GenericBatch1)),
                (2, DATA_DEFINITIONS.CONTINUOUS_BATCH_2, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_2, typeof(GenericBatch2)),
                (3, DATA_DEFINITIONS.CONTINUOUS_BATCH_3, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_3, typeof(GenericBatch3)),
                (4, DATA_DEFINITIONS.CONTINUOUS_BATCH_4, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_4, typeof(GenericBatch4)),
                (5, DATA_DEFINITIONS.CONTINUOUS_BATCH_5, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_5, typeof(GenericBatch5))
            };

            int totalVariablesAdded = 0;

            // Process each batch
            for (int batchNum = 1; batchNum <= NUM_BATCHES; batchNum++)
            {
                // Calculate variable range for this batch
                int startIdx = (batchNum - 1) * BATCH_SIZE;
                int endIdx = Math.Min(startIdx + BATCH_SIZE, continuousVariables.Count);
                int batchVarCount = endIdx - startIdx;

                if (batchVarCount <= 0) break; // No more variables

                var config = batchConfigs[batchNum - 1];
                System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] Setting up Batch {batchNum}: variables {startIdx}-{endIdx - 1} ({batchVarCount} vars)");

                // Clear previous batch definition
                SafelyClearDataDefinition(
                    config.Item2, // DATA_DEFINITIONS
                    config.Item3, // DATA_REQUESTS
                    delayMs: 300  // 300ms for batch cleanup
                );

                // Add variables to this batch
                int indexWithinBatch = 0;
                for (int i = startIdx; i < endIdx; i++)
                {
                    var kvp = continuousVariables[i];
                    var varDef = kvp.Value;

                    // Build SimConnect variable name with L: prefix for LVars
                    string simVarName = varDef.Type == SimVarType.LVar ? $"L:{varDef.Name}" : varDef.Name;
                    string units = varDef.Units ?? "number";

                    // Add to batch data definition
                    sc.AddToDataDefinition(
                        config.Item2, // DATA_DEFINITIONS
                        simVarName,
                        units,
                        SIMCONNECT_DATATYPE.FLOAT64,
                        0.0f,
                        SIMCONNECT_UNUSED
                    );

                    // Store mapping: variable key -> (batchNum, indexWithinBatch)
                    continuousVariableIndexMap[kvp.Key] = (batchNum, indexWithinBatch);
                    indexWithinBatch++;
                    totalVariablesAdded++;

                    // THROTTLE: Give SimConnect time to process every 50 variables
                    if (totalVariablesAdded % 50 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] Throttling after {totalVariablesAdded} total variables");
                        Thread.Sleep(5);
                    }
                }

                // Register the batch struct using reflection (C# doesn't support dynamic generic types easily)
                var registerMethod = typeof(Microsoft.FlightSimulator.SimConnect.SimConnect)
                    .GetMethod("RegisterDataDefineStruct")
                    ?.MakeGenericMethod(config.Item4); // GenericBatch1-5
                registerMethod?.Invoke(sc, new object[] { config.Item2 });

                // Request data with SIMCONNECT_PERIOD.SECOND
                // All batches update simultaneously every second
                sc.RequestDataOnSimObject(
                    config.Item3, // DATA_REQUESTS
                    config.Item2, // DATA_DEFINITIONS
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SECOND,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0
                );

                System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] Batch {batchNum} monitoring started for {batchVarCount} variables");
            }

            System.Diagnostics.Debug.WriteLine($"[StartContinuousMonitoring] Multi-batch monitoring started for {totalVariablesAdded} variables across {Math.Min(NUM_BATCHES, (continuousVariables.Count + BATCH_SIZE - 1) / BATCH_SIZE)} batches");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] CRITICAL ERROR setting up batched continuous monitoring!");
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Exception Type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Inner Exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Restart continuous monitoring with the current aircraft's variables.
    /// Call this when switching aircraft to update continuous monitoring to the new aircraft.
    /// </summary>
    public void RestartContinuousMonitoring()
    {
        System.Diagnostics.Debug.WriteLine("[SimConnectManager] Restarting continuous monitoring for new aircraft");
        StartContinuousMonitoring();
    }

    /// <summary>
    /// Safely clears a data definition by first ensuring no active requests exist.
    /// CRITICAL: Calling ClearDataDefinition() while a request is active causes intermittent crashes.
    /// Per FSDeveloper forums: "SimConnect may crash when removing/changing data requests while still active."
    /// This method implements the recommended pattern: Cancel request → Wait → Clear definition.
    /// </summary>
    /// <param name="defId">The data definition ID to clear</param>
    /// <param name="requestId">Optional: The request ID to cancel before clearing (if actively monitoring)</param>
    /// <param name="delayMs">Delay in milliseconds after cancelling request (default 200ms, use 500ms for large datasets)</param>
    private void SafelyClearDataDefinition(DATA_DEFINITIONS defId, DATA_REQUESTS? requestId = null, int delayMs = 200)
    {
        if (simConnect == null) return;

        try
        {
            // If this is an active recurring request, cancel it first
            if (requestId != null)
            {
                try
                {
                    simConnect.RequestDataOnSimObject(
                        requestId.Value,
                        defId,
                        SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.NEVER,  // Cancel the recurring request
                        SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                        0, 0, 0
                    );
                    System.Diagnostics.Debug.WriteLine($"[SafelyClearDataDefinition] Cancelled recurring request {requestId.Value} for definition {defId}");
                }
                catch (Exception ex)
                {
                    // Ignore errors - request might not exist yet (first setup)
                    System.Diagnostics.Debug.WriteLine($"[SafelyClearDataDefinition] Error cancelling request (expected on first setup): {ex.Message}");
                }

                // CRITICAL: Wait for SimConnect to process the cancellation using message pumping
                // With Fenix A320's 477 continuous variables, we need time for in-flight data to clear
                // Thread.Sleep() BLOCKS the UI thread, preventing SimConnect from processing messages!
                // We MUST use Application.DoEvents() to pump messages while waiting.
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < delayMs)
                {
                    System.Windows.Forms.Application.DoEvents(); // CRITICAL: Pump SimConnect messages!
                    Thread.Sleep(10); // Small sleep to prevent CPU spinning
                }
                System.Diagnostics.Debug.WriteLine($"[SafelyClearDataDefinition] Waited {delayMs}ms with message pumping for cancellation to process");
            }

            // Now it's safe to clear the data definition
            simConnect.ClearDataDefinition(defId);
            System.Diagnostics.Debug.WriteLine($"[SafelyClearDataDefinition] Successfully cleared data definition {defId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SafelyClearDataDefinition] Error clearing data definition {defId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-register all variables for the current aircraft.
    /// Call this when switching aircraft to update variable registrations.
    /// </summary>
    public void ReregisterAllVariables()
    {
        System.Diagnostics.Debug.WriteLine("[SimConnectManager] Re-registering all variables for new aircraft");

        // Clear old data definitions from SimConnect before losing track of their IDs
        if (simConnect != null)
        {
            foreach (var kvp in variableDataDefinitions)
            {
                try
                {
                    simConnect.ClearDataDefinition((DATA_DEFINITIONS)kvp.Value);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error clearing data definition {kvp.Value} for {kvp.Key}: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Cleared {variableDataDefinitions.Count} old data definitions from SimConnect");
        }

        // Clear existing registrations
        variableDataDefinitions.Clear();
        lastVariableValues.Clear();
        forceUpdateVariables.Clear();

        // Reset ID counter to avoid accumulating stale ID ranges over multiple switches
        nextDataDefinitionId = 1000;
        System.Diagnostics.Debug.WriteLine("[SimConnectManager] Reset nextDataDefinitionId to 1000");

        // Re-register all variables for new aircraft
        RegisterAllVariables();
    }

    private void SetupEvents()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety
        sc.OnRecvOpen += SimConnect_OnRecvOpen;
        sc.OnRecvQuit += SimConnect_OnRecvQuit;
        sc.OnRecvSimobjectData += SimConnect_OnRecvSimobjectData;
        sc.OnRecvClientData += SimConnect_OnRecvClientData;
        sc.OnRecvException += SimConnect_OnRecvException;
    }

    private void RegisterClientEvents()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety
        int registeredCount = 0;
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();

        // Register FlyByWire custom events
        foreach (var kvp in variables)
        {
            if (kvp.Value.Type == SimVarType.Event)
            {
                uint eventId = nextEventId++;
                eventIds[kvp.Key] = eventId;

                try
                {
                    // Map the event
                    sc.MapClientEventToSimEvent((EVENTS)eventId, kvp.Value.Name);
                    registeredCount++;
                }
                catch (Exception ex)
                {
                    // Silently ignore unrecognized events (FBW-specific events not yet loaded)
                    System.Diagnostics.Debug.WriteLine($"Failed to register event {kvp.Key} ({kvp.Value.Name}): {ex.Message}");
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"Successfully registered {registeredCount} events");
    }

    private void SimConnect_OnRecvOpen(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        // Connection established, detect simulator version using shared utility
        try
        {
            DetectedSimulatorVersion = Utils.SimulatorDetector.DetectRunningSimulator();
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Detected simulator: {DetectedSimulatorVersion}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error detecting simulator version: {ex.Message}");
            DetectedSimulatorVersion = "Unknown";
        }

        System.Diagnostics.Debug.WriteLine("[SimConnectManager] SimConnect connection opened, requesting aircraft info");
    }

    private void SimConnect_OnRecvQuit(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV data)
    {
        Disconnect();
    }

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
    
    private void SimConnect_OnRecvSimobjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        // Check if this is a specific LVar request (400-499 range)
        if ((int)data.dwRequestID >= 400 && (int)data.dwRequestID < 500)
        {
            SingleValue specificValue = (SingleValue)data.dwData[0];

            // Look up which variable this was
            if (pendingRequests.TryRemove((int)data.dwRequestID, out string? varKey))
            {
                // FlyByWire A32NX ECAM variable tracking for display window
                // These hardcoded checks are safe - other aircraft (Fenix, PMDG) use different variable names
                // so these conditions won't trigger. Each aircraft's warning/caution announcements work via
                // the continuous monitoring system (UpdateFrequency.Continuous + IsAnnounced in their definition).
                if (varKey == "A32NX_MASTER_WARNING")
                {
                    ecamMasterWarning = specificValue.value;
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM Master Warning updated: {specificValue.value}");
                }
                else if (varKey == "A32NX_MASTER_CAUTION")
                {
                    ecamMasterCaution = specificValue.value;
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM Master Caution updated: {specificValue.value}");
                }
                else if (varKey == "A32NX_STALL_WARNING")
                {
                    ecamStallWarning = specificValue.value;
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM Stall Warning updated: {specificValue.value}");
                }

                // Format the description based on variable type
                string description = $"{specificValue.value:F1}";
                var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
                if (variables.ContainsKey(varKey))
                {
                    var varDef = variables[varKey];

                    // For LED variables, use the DisplayName with On/Off state
                    if (varKey.StartsWith("A32NX_ECP_LIGHT_"))
                    {
                        string state = specificValue.value > 0 ? "On" : "Off";
                        description = $"{varDef.DisplayName} {state}";
                    }
                    else if (varDef.Units == "volts")
                    {
                        description = $"{specificValue.value:F1}V";
                    }
                }

                // Send update with the actual variable key
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = varKey,
                    Value = specificValue.value,
                    Description = description
                });
            }
            return;
        }
        
        // Handle responses from individual variable registrations
        if ((int)data.dwRequestID >= (int)DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE)
        {
            ProcessIndividualVariableResponse((int)data.dwRequestID, (SingleValue)data.dwData[0]);
            return;
        }

        switch ((DATA_REQUESTS)data.dwRequestID)
        {
                
            case DATA_REQUESTS.REQUEST_FCU_VALUES:
                FCUValues fcuData = (FCUValues)data.dwData[0];
                ProcessFCUValues(fcuData);
                break;
                
            case DATA_REQUESTS.REQUEST_AIRCRAFT_INFO:
                AircraftInfo aircraftInfo = (AircraftInfo)data.dwData[0];
                pendingAircraftInfo = aircraftInfo;
                TryAnnounceConnection();
                break;

            case DATA_REQUESTS.REQUEST_ATC_ID:
                try
                {
                    System.Diagnostics.Debug.WriteLine("[SimConnectManager] Received ATC data, attempting to parse...");
                    AircraftAtcData atcData = (AircraftAtcData)data.dwData[0];
                    currentAircraftAtcId = atcData.atcId?.Trim() ?? "";
                    currentAircraftAirline = atcData.atcAirline?.Trim() ?? "";
                    currentAircraftFlightNumber = atcData.atcFlightNumber?.Trim() ?? "";
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ATC Data - ID: '{currentAircraftAtcId}', Type: '{atcData.atcType?.Trim()}', Airline: '{currentAircraftAirline}', Flight: '{currentAircraftFlightNumber}'");
                    atcDataReceived = true;
                    TryAnnounceConnection();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error parsing ATC data: {ex.Message}");
                    atcDataReceived = true; // Set flag even on error so we don't block announcement
                    TryAnnounceConnection();
                }
                break;

            // Multi-batch continuous variable monitoring (5 batches of ~100 variables each)
            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_1:
                GenericBatch1 batch1Data = (GenericBatch1)data.dwData[0];
                ProcessContinuousBatch(1, in batch1Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_2:
                GenericBatch2 batch2Data = (GenericBatch2)data.dwData[0];
                ProcessContinuousBatch(2, in batch2Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_3:
                GenericBatch3 batch3Data = (GenericBatch3)data.dwData[0];
                ProcessContinuousBatch(3, in batch3Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_4:
                GenericBatch4 batch4Data = (GenericBatch4)data.dwData[0];
                ProcessContinuousBatch(4, in batch4Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_5:
                GenericBatch5 batch5Data = (GenericBatch5)data.dwData[0];
                ProcessContinuousBatch(5, in batch5Data);
                break;

            case DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION:
                AircraftPosition positionData = (AircraftPosition)data.dwData[0];
                ProcessAircraftPosition(positionData);
                break;

            case DATA_REQUESTS.REQUEST_ILS_GUIDANCE:
                AircraftPosition ilsPositionData = (AircraftPosition)data.dwData[0];
                ProcessILSGuidance(ilsPositionData);
                break;

            case DATA_REQUESTS.REQUEST_WIND_DATA:
                WindData windData = (WindData)data.dwData[0];
                ProcessWindData(windData);
                break;

            // REQUEST_ECAM_MESSAGES case removed - now handled via MobiFlight

            case (DATA_REQUESTS)300: // Heading only
                SingleValue headingData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FCU_HEADING",
                    Value = headingData.value,
                    Description = $"Heading {headingData.value:000} degrees"
                });
                break;
                
            case (DATA_REQUESTS)301: // Speed only
                SingleValue speedData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FCU_SPEED",
                    Value = speedData.value,
                    Description = $"Speed {speedData.value:000}"
                });
                break;
                
            case (DATA_REQUESTS)302: // Altitude only
                SingleValue altitudeData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FCU_ALTITUDE",
                    Value = altitudeData.value,
                    Description = $"Altitude {altitudeData.value:00000} feet"
                });
                break;

            // NOTE: FCU data requests (320-323) removed - now handled by aircraft definitions
            // using existing variable registrations instead of duplicate temp definitions

            case (DATA_REQUESTS)304: // Altitude MSL (swapped)
                SingleValue altMslData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ALTITUDE_MSL",
                    Value = altMslData.value,
                    Description = $"{altMslData.value:0}"
                });
                break;

            case (DATA_REQUESTS)303: // Altitude AGL (swapped)
                SingleValue altAglData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ALTITUDE_AGL",
                    Value = altAglData.value,
                    Description = $"{altAglData.value:0}"
                });
                break;

            case (DATA_REQUESTS)305: // Airspeed Indicated
                SingleValue iasData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "AIRSPEED_INDICATED",
                    Value = iasData.value,
                    Description = $"{iasData.value:0}"
                });
                break;

            case (DATA_REQUESTS)306: // Airspeed True
                SingleValue tasData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "AIRSPEED_TRUE",
                    Value = tasData.value,
                    Description = $"{tasData.value:0}"
                });
                break;

            case (DATA_REQUESTS)307: // Ground Speed
                SingleValue gsData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "GROUND_SPEED",
                    Value = gsData.value,
                    Description = $"{gsData.value:0}"
                });
                break;

            case (DATA_REQUESTS)308: // Vertical Speed
                SingleValue vsData = (SingleValue)data.dwData[0];
                double vsInFpm = vsData.value; // Already in feet per minute
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VERTICAL_SPEED",
                    Value = vsInFpm,
                    Description = $"{vsInFpm:0}"
                });
                break;

            case (DATA_REQUESTS)310: // Heading True (swapped)
                SingleValue hdgTrueData = (SingleValue)data.dwData[0];
                double hdgTrueInDegrees = hdgTrueData.value * (180.0 / Math.PI); // Convert radians to degrees
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "HEADING_TRUE",
                    Value = hdgTrueInDegrees,
                    Description = $"{hdgTrueInDegrees:000}"
                });
                break;

            case (DATA_REQUESTS)309: // Heading Magnetic (swapped)
                SingleValue hdgMagData = (SingleValue)data.dwData[0];
                double hdgMagInDegrees = hdgMagData.value * (180.0 / Math.PI); // Convert radians to degrees
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "HEADING_MAGNETIC",
                    Value = hdgMagInDegrees,
                    Description = $"{hdgMagInDegrees:000}"
                });
                break;

            case (DATA_REQUESTS)311: // Mach Speed
                SingleValue machData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "MACH_SPEED",
                    Value = machData.value,
                    Description = $"{machData.value:0.00}"
                });
                break;

            case (DATA_REQUESTS)313: // Bank Angle
                SingleValue bankData = (SingleValue)data.dwData[0];
                double bankInDegrees = -bankData.value * (180.0 / Math.PI); // Convert radians to degrees, negated so right bank = positive
                string bankFormatted = bankInDegrees >= 0
                    ? (bankInDegrees == 0 ? "0" : $"+{bankInDegrees:F1}")
                    : $"{bankInDegrees:F1}";
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "BANK_ANGLE",
                    Value = bankInDegrees,
                    Description = bankFormatted
                });
                break;

            case (DATA_REQUESTS)312: // Pitch
                SingleValue pitchData = (SingleValue)data.dwData[0];
                double pitchInDegrees = -(pitchData.value * (180.0 / Math.PI)); // Convert radians to degrees and negate (SimConnect: negative = nose up)
                string pitchFormatted = pitchInDegrees >= 0
                    ? $"+{pitchInDegrees:F1}"
                    : $"{pitchInDegrees:F1}";
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PITCH_ANGLE",
                    Value = pitchInDegrees,
                    Description = pitchFormatted
                });
                break;

            case (DATA_REQUESTS)314: // Fuel Quantity
                SingleValue fuelData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FUEL_QUANTITY",
                    Value = fuelData.value,
                    Description = $"Total fuel {fuelData.value:0} kilograms"
                });
                break;

            // Speed tape values
            case (DATA_REQUESTS)330: // Speed GD (O Speed)
                SingleValue speedGDData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_GD",
                    Value = speedGDData.value,
                    Description = $"O Speed {speedGDData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)331: // Speed S
                SingleValue speedSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_S",
                    Value = speedSData.value,
                    Description = $"S-Speed {speedSData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)332: // Speed F
                SingleValue speedFData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_F",
                    Value = speedFData.value,
                    Description = $"F-Speed {speedFData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)335: // Speed VFE
                SingleValue speedVFEData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_VFE",
                    Value = speedVFEData.value,
                    Description = $"V FE Speed {speedVFEData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)336: // Speed VLS
                SingleValue speedVLSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_VLS",
                    Value = speedVLSData.value,
                    Description = $"Minimum Selectable Speed {speedVLSData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)337: // Speed VS (Stall Speed)
                SingleValue speedVSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_VS",
                    Value = speedVSData.value,
                    Description = $"Stall Speed {speedVSData.value:0} knots"
                });
                break;

            // Fuel and Payload Data Requests (340-363)
            case (DATA_REQUESTS)340: // Fuel Weight Per Gallon
            case (DATA_REQUESTS)341: // Fuel Left Aux
            case (DATA_REQUESTS)342: // Fuel Left Main
            case (DATA_REQUESTS)343: // Fuel Center
            case (DATA_REQUESTS)344: // Fuel Right Main
            case (DATA_REQUESTS)345: // Fuel Right Aux
            case (DATA_REQUESTS)346: // Pax A
            case (DATA_REQUESTS)347: // Pax B
            case (DATA_REQUESTS)348: // Pax C
            case (DATA_REQUESTS)349: // Pax D
            case (DATA_REQUESTS)350: // Pax Weight
            case (DATA_REQUESTS)351: // Bag Weight
            case (DATA_REQUESTS)352: // Cargo Fwd
            case (DATA_REQUESTS)353: // Cargo Aft Container
            case (DATA_REQUESTS)354: // Cargo Aft Baggage
            case (DATA_REQUESTS)355: // Cargo Aft Bulk
            case (DATA_REQUESTS)356: // Empty Weight
            case (DATA_REQUESTS)357: // ZFW
            case (DATA_REQUESTS)358: // GW
            case (DATA_REQUESTS)359: // CG MAC
            case (DATA_REQUESTS)360: // GW CG
            case (DATA_REQUESTS)361: // FMS Pax
            case (DATA_REQUESTS)362: // FMS ZFW
            case (DATA_REQUESTS)363: // FMS GW
            case (DATA_REQUESTS)364: // FMS CG
                SingleValue fuelPayloadData = (SingleValue)data.dwData[0];
                string varName = GetFuelPayloadVarName((int)data.dwRequestID);
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = varName,
                    Value = fuelPayloadData.value,
                    Description = $"{varName}: {fuelPayloadData.value}"
                });
                break;

            case (DATA_REQUESTS)315: // Waypoint Info
                WaypointInfo waypointData = (WaypointInfo)data.dwData[0];

                // Unpack waypoint name from encoded doubles
                string waypointName = UnpackWaypointName(waypointData.ident0, waypointData.ident1);

                string description;
                if (string.IsNullOrWhiteSpace(waypointName))
                {
                    description = "No active waypoint";
                }
                else
                {
                    // Convert bearing from radians to degrees
                    double bearingDegrees = waypointData.bearing * (180.0 / Math.PI);
                    // Normalize to 0-360 range
                    if (bearingDegrees < 0) bearingDegrees += 360;

                    description = $"{waypointName}, {waypointData.distance:0.0} NM, {bearingDegrees:0} degrees";
                }

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "WAYPOINT_INFO",
                    Value = waypointData.distance,
                    Description = description
                });
                break;

            case (DATA_REQUESTS)370: // Waypoint Info (new ID to avoid collision with fuel/payload range)
                WaypointInfo waypointData370 = (WaypointInfo)data.dwData[0];

                // Unpack waypoint name from encoded doubles
                string waypointName370 = UnpackWaypointName(waypointData370.ident0, waypointData370.ident1);

                string description370;
                if (string.IsNullOrWhiteSpace(waypointName370))
                {
                    description370 = "No active waypoint";
                }
                else
                {
                    // Convert bearing from radians to degrees
                    double bearingDegrees370 = waypointData370.bearing * (180.0 / Math.PI);
                    // Normalize to 0-360 range
                    if (bearingDegrees370 < 0) bearingDegrees370 += 360;

                    description370 = $"{waypointName370}, {waypointData370.distance:0.0} NM, {bearingDegrees370:0} degrees";
                }

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "WAYPOINT_INFO",
                    Value = waypointData370.distance,
                    Description = description370
                });
                break;

            case (DATA_REQUESTS)324: // Takeoff Assist - Pitch
                SingleValue takeoffPitchData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_PITCH_DEGREES",
                    Value = takeoffPitchData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)325: // Takeoff Assist - Heading
                SingleValue takeoffHeadingData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_HEADING_DEGREES_MAGNETIC",
                    Value = takeoffHeadingData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)326: // Position for Takeoff Assist Toggle
                TakeoffAssistData toggleData = (TakeoffAssistData)data.dwData[0];
                // Return position data for toggle with unique VarName
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "POSITION_FOR_TAKEOFF_ASSIST",
                    Value = toggleData.HeadingMagnetic * (180.0 / Math.PI), // Heading in degrees for announcement
                    Description = "",
                    PositionData = new AircraftPosition
                    {
                        Latitude = toggleData.Latitude,
                        Longitude = toggleData.Longitude,
                        HeadingMagnetic = toggleData.HeadingMagnetic * (180.0 / Math.PI) // Convert radians to degrees
                    }
                });
                break;

            case (DATA_REQUESTS)327: // Hand Fly Mode - Pitch
                SingleValue handFlyPitchData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_PITCH_DEGREES",
                    Value = handFlyPitchData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)328: // Hand Fly Mode - Bank
                SingleValue handFlyBankData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_BANK_DEGREES",
                    Value = handFlyBankData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)371: // Hand Fly Mode - Heading
                SingleValue handFlyHeadingData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_HEADING_DEGREES_MAGNETIC",
                    Value = handFlyHeadingData.value, // Radians - will be converted to degrees in handler
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)372: // Hand Fly Mode - Vertical Speed
                SingleValue handFlyVSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "HAND_FLY_VERTICAL_SPEED",  // Use distinct name to avoid conflict with hotkey VS requests
                    Value = handFlyVSData.value, // Feet per minute
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)505: // Visual Guidance - Consolidated Data
                VisualGuidanceData vgData = (VisualGuidanceData)data.dwData[0];

                // Extract position data and send as AircraftPosition for compatibility
                AircraftPosition vgPosData = new AircraftPosition
                {
                    Latitude = vgData.Latitude,
                    Longitude = vgData.Longitude,
                    Altitude = vgData.Altitude,
                    HeadingMagnetic = vgData.HeadingMagnetic,
                    MagneticVariation = vgData.MagneticVariation,
                    GroundSpeedKnots = vgData.GroundSpeedKnots,
                    VerticalSpeedFPM = vgData.VerticalSpeedFPM
                };

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_POSITION",
                    Value = 0,
                    Description = "",
                    PositionData = vgPosData
                });

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_AGL",
                    Value = vgData.AGL,
                    Description = ""
                });

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_GROUND_TRACK",
                    Value = vgData.GroundTrack,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)506: // Takeoff Assist - Consolidated Data
                TakeoffAssistData taData = (TakeoffAssistData)data.dwData[0];

                // Send position update for centerline tracking
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "TAKEOFF_ASSIST_POSITION",
                    Value = 0,
                    Description = "",
                    PositionData = new AircraftPosition
                    {
                        Latitude = taData.Latitude,
                        Longitude = taData.Longitude,
                        HeadingMagnetic = taData.HeadingMagnetic * (180.0 / Math.PI) // Convert radians to degrees
                    }
                });

                // Send pitch update (convert radians to degrees, negate for body axis)
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "TAKEOFF_ASSIST_PITCH",
                    Value = -(taData.Pitch * (180.0 / Math.PI)),
                    Description = ""
                });

                // Send IAS update for speed callouts
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "TAKEOFF_ASSIST_IAS",
                    Value = taData.IndicatedAirspeedKnots,
                    Description = ""
                });
                break;
        }
    }

    /// <summary>
    /// Unpack waypoint name from FlyByWire encoded format
    /// </summary>
    private string UnpackWaypointName(double ident0, double ident1)
    {
        double[] values = { ident0, ident1 };
        string result = "";

        for (int i = 0; i < values.Length * 8; i++)
        {
            int word = i / 8;
            int charPos = i % 8;
            int code = (int)(values[word] / Math.Pow(2, charPos * 6)) & 0x3F;

            if (code > 0)
            {
                result += (char)(code + 31);
            }
        }

        return result.Trim();
    }

    /// <summary>
    /// Process individual variable response from our new registration system
    /// </summary>
    private void ProcessIndividualVariableResponse(int requestId, SingleValue data)
    {
        try
        {
            // Find the variable key for this request ID
            var variableEntry = variableDataDefinitions.FirstOrDefault(x => x.Value == requestId);
            if (variableEntry.Key == null)
            {
                return;
            }

            string varKey = variableEntry.Key;

            var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
            var varDef = variables.ContainsKey(varKey) ? variables[varKey] : null;

            if (varDef == null)
            {
                return;
            }

            double currentValue = data.value;

            // FlyByWire A32NX ECAM message processing for the ECAM Display window
            // This processes A32NX-specific variable names (A32NX_Ewd_LOWER_*).
            // Other aircraft variants (Fenix, PMDG) use different variable names, so this
            // block won't execute for them. Safe to keep aircraft-specific.
            if (varKey.StartsWith("A32NX_Ewd_LOWER_"))
            {
                // Convert numeric code to text message via EWDMessageLookup
                long numericCode = (long)currentValue;

                // Get raw message with ANSI codes
                string rawMessage = EWDMessageLookup.GetRawMessage(numericCode);

                // Store RAW message for ECAM Display window (it will clean and extract color itself)
                ecamStringData[varKey] = rawMessage;

                // Clean message for screen reader announcements
                string priority = EWDMessageLookup.GetMessagePriority(rawMessage);
                string cleanText = EWDMessageLookup.CleanANSICodes(rawMessage);

                // Create announcement text WITH color appended for screen readers (with comma)
                string announcementText = cleanText;
                if (!string.IsNullOrEmpty(priority) && !string.IsNullOrWhiteSpace(cleanText))
                {
                    announcementText = $"{cleanText}, {priority}";
                }
                ecamAnnouncementData[varKey] = announcementText;

                ecamStringsReceived++;

                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM Line received: {varKey} = Code:{numericCode} → Display:'{cleanText}' | Announce:'{announcementText}' ({ecamStringsReceived}/{ecamTotalStringsExpected})");

                // Check if all 14 ECAM lines have been received (modulo ensures it fires every 14 lines)
                if (ecamStringsReceived % ecamTotalStringsExpected == 0)
                {
                    // Fire the ECAM data received event with all collected data
                    ECAMDataReceived?.Invoke(this, new ECAMDataEventArgs
                    {
                        LeftLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_1"] : "",
                        LeftLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_2"] : "",
                        LeftLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_3"] : "",
                        LeftLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_4"] : "",
                        LeftLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_5"] : "",
                        LeftLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_6"] : "",
                        LeftLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_7"] : "",
                        RightLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_1"] : "",
                        RightLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_2"] : "",
                        RightLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_3"] : "",
                        RightLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_4"] : "",
                        RightLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_5"] : "",
                        RightLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_6"] : "",
                        RightLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_7"] : "",
                        MasterWarning = ecamMasterWarning > 0.5,
                        MasterCaution = ecamMasterCaution > 0.5,
                        StallWarning = ecamStallWarning > 0.5
                    });

                    System.Diagnostics.Debug.WriteLine("[SimConnectManager] All ECAM data collected and event fired");

                    // Announce new ECAM messages (batch processing after all 14 lines collected)
                    AnnounceECAMChanges();
                }

                // Don't continue with normal processing for ECAM codes - batch collection is handled above
                return;
            }

            // Check if this is a forced update request
            bool isForceUpdate = false;
            lock (forceUpdateVariables)
            {
                isForceUpdate = forceUpdateVariables.Remove(varKey);
            }

            // Check for value changes (for announced variables)
            bool hasChanged = true;
            if (lastVariableValues.ContainsKey(varKey))
            {
                hasChanged = Math.Abs(lastVariableValues[varKey] - currentValue) > 0.001; // Small tolerance for floating point
            }
            lastVariableValues.AddOrUpdate(varKey, currentValue, (key, oldValue) => currentValue);

            // Always fire SimVarUpdated event - displays need current values even if unchanged
            // The event recipients will decide what to do based on IsAnnounced and hasChanged
            string description = FormatVariableValue(varKey, varDef, currentValue);

            System.Diagnostics.Debug.WriteLine($"[ProcessIndividualVariableResponse] Firing SimVarUpdated for {varKey}: Value={currentValue}, IsAnnounced={varDef.IsAnnounced}, HasChanged={hasChanged}, ForceUpdate={isForceUpdate}");

            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = varKey,
                Value = currentValue,
                Description = description
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing individual variable response: {ex.Message}");
        }
    }

    /// <summary>
    /// Format variable value for display/announcement
    /// </summary>
    private string FormatVariableValue(string varKey, SimVarDefinition varDef, double value)
    {
        // Check for custom value descriptions
        if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsKey(value))
        {
            // If AnnounceValueOnly is true, return just the value (e.g., "On ground")
            // Otherwise return "DisplayName: value" (e.g., "Parking brake: Set")
            if (varDef.AnnounceValueOnly)
            {
                return varDef.ValueDescriptions[value];
            }
            return $"{varDef.DisplayName}: {varDef.ValueDescriptions[value]}";
        }

        // Special formatting for FMA armed modes (bitmask decoding)
        if (varKey == "A32NX_FMA_LATERAL_ARMED")
        {
            return FormatFMALateralArmed((int)value);
        }
        else if (varKey == "A32NX_FMA_VERTICAL_ARMED")
        {
            return FormatFMAVerticalArmed((int)value);
        }
        else if (varKey == "A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS" || varKey == "A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS")
        {
            return FormatNDFMMessage((int)value);
        }
        // Special formatting for different types of variables
        else if (varKey.StartsWith("A32NX_ECP_LIGHT_"))
        {
            string state = value > 0 ? "On" : "Off";
            return $"{varDef.DisplayName} {state}";
        }
        else if (varDef.Units == "volts")
        {
            return $"{varDef.DisplayName}: {value:F1}V";
        }
        else if (varDef.Units == "feet")
        {
            return $"{varDef.DisplayName}: {value:F0} feet";
        }
        else if (varDef.Units == "degrees")
        {
            return $"{varDef.DisplayName}: {value:F0} degrees";
        }
        else if (varDef.Units == "knots")
        {
            return $"{varDef.DisplayName}: {value:F0} knots";
        }
        else if (varDef.Units == "millibars" || varDef.Units == "millibar")
        {
            return $"{varDef.DisplayName}: {value:F2}";
        }
        else if (varDef.Units == "inHg" || varDef.Units == "inhg")
        {
            return $"{varDef.DisplayName}: {value:F2}";
        }

        // Default formatting
        return $"{varDef.DisplayName}: {value:F1}";
    }

    /// <summary>
    /// Decode FMA lateral armed mode bitmask
    /// </summary>
    private string FormatFMALateralArmed(int bitmask)
    {
        if (bitmask == 0)
        {
            return "Armed Lateral: None";
        }

        var modes = new List<string>();

        // Check each bit for lateral modes
        if ((bitmask & (1 << 0)) != 0) modes.Add("NAV");
        if ((bitmask & (1 << 1)) != 0) modes.Add("LOC");

        return modes.Count > 0 ? $"Armed Lateral: {string.Join(", ", modes)}" : "Armed Lateral: None";
    }

    /// <summary>
    /// Decode FMA vertical armed mode bitmask
    /// </summary>
    private string FormatFMAVerticalArmed(int bitmask)
    {
        if (bitmask == 0)
        {
            return "Armed Vertical: None";
        }

        var modes = new List<string>();

        // Check each bit for vertical modes
        if ((bitmask & (1 << 0)) != 0) modes.Add("ALT");
        if ((bitmask & (1 << 1)) != 0) modes.Add("ALT CST");
        if ((bitmask & (1 << 2)) != 0) modes.Add("CLB");
        if ((bitmask & (1 << 3)) != 0) modes.Add("DES");
        if ((bitmask & (1 << 4)) != 0) modes.Add("GS");
        if ((bitmask & (1 << 5)) != 0) modes.Add("FINAL");
        if ((bitmask & (1 << 6)) != 0) modes.Add("TCAS");

        return modes.Count > 0 ? $"Armed Vertical: {string.Join(", ", modes)}" : "Armed Vertical: None";
    }

    /// <summary>
    /// Decode ND FM message flags bitmask
    /// </summary>
    private string FormatNDFMMessage(int bitmask)
    {
        if (bitmask == 0)
        {
            return "ND Message: None";
        }

        // Check each bit for ND FM messages
        // Note: Only one message is typically active at a time, but we check in priority order
        if ((bitmask & (1 << 0)) != 0) return "ND Message: Select True Ref";
        if ((bitmask & (1 << 1)) != 0) return "ND Message: Check North Ref";
        if ((bitmask & (1 << 2)) != 0) return "ND Message: Nav Accuracy Downgrade";
        if ((bitmask & (1 << 3)) != 0) return "ND Message: Nav Accuracy Upgrade No GPS";
        if ((bitmask & (1 << 4)) != 0) return "ND Message: Specified VOR DME Unavailable";
        if ((bitmask & (1 << 5)) != 0) return "ND Message: Nav Accuracy Upgrade GPS";
        if ((bitmask & (1 << 6)) != 0) return "ND Message: GPS Primary";
        if ((bitmask & (1 << 7)) != 0) return "ND Message: Map Partly Displayed";
        if ((bitmask & (1 << 8)) != 0) return "ND Message: Set Offside Range Mode";
        if ((bitmask & (1 << 9)) != 0) return "ND Message: Offside FM Control";
        if ((bitmask & (1 << 10)) != 0) return "ND Message: Offside FM Wxr Control";
        if ((bitmask & (1 << 11)) != 0) return "ND Message: Offside Wxr Control";
        if ((bitmask & (1 << 12)) != 0) return "ND Message: GPS Primary Lost";
        if ((bitmask & (1 << 13)) != 0) return "ND Message: RTA Missed";
        if ((bitmask & (1 << 14)) != 0) return "ND Message: Backup Nav";

        return "ND Message: None";
    }

    /// <summary>
    /// Process batched continuous variable updates for Batch 1.
    /// Extracts values from GenericBatch1 struct and routes to existing variable processing pipeline.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch1 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 2.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch2 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 3.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch3 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 4.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch4 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 5.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch5 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Generic implementation for processing batch data.
    /// Uses unsafe pointer access for efficient memory access across all batch types.
    /// </summary>
    private void ProcessContinuousBatchImpl<T>(int batchNum, in T batch) where T : unmanaged
    {
        // Get variables dictionary once at the start
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();

        int processedCount = 0;
        int skippedCount = 0;
        int invalidIndexCount = 0;
        int exceptionCount = 0;

        // SAFETY: Check if map is empty (possible race condition)
        if (continuousVariableIndexMap.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] WARNING: Map is empty! Possible race condition with StartContinuousMonitoring");
            return;
        }

        // Use unsafe pointer access instead of reflection for performance and stability
        // Each batch struct is a sequential struct of 100 doubles, so we can access directly
        try
        {
            unsafe
            {
                // batch is an 'in' parameter (readonly reference)
                // Use 'fixed' to get a pointer to the readonly reference
                fixed (T* batchPtr = &batch)
                {
                    double* values = (double*)batchPtr;  // Treat struct as array of doubles

                    // Process each continuous variable using direct memory access (no reflection!)
                    // Filter to only process variables belonging to this batch
                    foreach (var kvp in continuousVariableIndexMap)
                    {
                        string varKey = kvp.Key;
                        (int varBatchNum, int index) = kvp.Value;

                        // Skip variables that don't belong to this batch
                        if (varBatchNum != batchNum) continue;

                        // SAFETY: Validate index is within bounds
                        // Each batch struct has 100 doubles (V0-V99)
                        if (index < 0 || index >= 100)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] ERROR: Batch {batchNum} index {index} out of bounds [0-99] for variable '{varKey}'");
                            invalidIndexCount++;
                            continue;
                        }

                        // SAFETY: Wrap value access to catch any memory exceptions
                        double value;
                        try
                        {
                            // Direct memory access - blazing fast, no reflection overhead!
                            value = values[index];

                            // Get variable definition
                            if (!variables.TryGetValue(varKey, out var varDef))
                            {
                                skippedCount++;
                                System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] WARNING: Variable {varKey} not found in aircraft definition!");
                                continue;
                            }

                        // Special handling for ECAM variables (convert numeric codes to readable text)
                        if (varKey.StartsWith("A32NX_Ewd_LOWER_"))
                        {
                            // Convert numeric code to readable message via EWDMessageLookup
                            long numericCode = (long)value;
                            string rawMessage = EWDMessageLookup.GetRawMessage(numericCode);
                            string priority = EWDMessageLookup.GetMessagePriority(rawMessage);
                            string cleanText = EWDMessageLookup.CleanANSICodes(rawMessage);

                            // Store RAW message for ECAM Display window
                            ecamStringData[varKey] = rawMessage;

                            // Create announcement text WITH color appended for screen readers
                            string announcementText = cleanText;
                            if (!string.IsNullOrEmpty(priority) && !string.IsNullOrWhiteSpace(cleanText))
                            {
                                announcementText = $"{cleanText}, {priority}";
                            }
                            ecamAnnouncementData[varKey] = announcementText;

                            ecamStringsReceived++;

                            // Check if all 14 ECAM lines have been received
                            if (ecamStringsReceived % ecamTotalStringsExpected == 0)
                            {
                                // Fire the ECAM data received event with all collected data
                                ECAMDataReceived?.Invoke(this, new ECAMDataEventArgs
                                {
                                    LeftLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_1"] : "",
                                    LeftLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_2"] : "",
                                    LeftLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_3"] : "",
                                    LeftLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_4"] : "",
                                    LeftLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_5"] : "",
                                    LeftLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_6"] : "",
                                    LeftLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_7"] : "",
                                    RightLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_1"] : "",
                                    RightLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_2"] : "",
                                    RightLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_3"] : "",
                                    RightLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_4"] : "",
                                    RightLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_5"] : "",
                                    RightLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_6"] : "",
                                    RightLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_7"] : "",
                                    MasterWarning = ecamMasterWarning > 0.5,
                                    MasterCaution = ecamMasterCaution > 0.5,
                                    StallWarning = ecamStallWarning > 0.5
                                });

                                System.Diagnostics.Debug.WriteLine("[ProcessContinuousBatch] All ECAM data collected and event fired");

                                // Announce new ECAM messages (batch processing after all 14 lines collected)
                                AnnounceECAMChanges();
                            }

                            processedCount++;
                            continue; // Skip normal processing for ECAM variables
                        }

                        // Check for value changes (skip unchanged values to reduce announcement spam)
                        bool hasChanged = true;
                        if (lastVariableValues.TryGetValue(varKey, out double lastValue))
                        {
                            hasChanged = Math.Abs(lastValue - value) > 0.001; // Small tolerance for floating point
                        }

                        // Update cache
                        lastVariableValues[varKey] = value;

                        // Only fire event if value changed (or it's the first time we're seeing it)
                        if (hasChanged || !lastVariableValues.ContainsKey(varKey))
                        {
                            // Check if we should only announce matches to ValueDescriptions (e.g., thrust lever detents)
                            if (varDef.OnlyAnnounceValueDescriptionMatches &&
                                varDef.ValueDescriptions != null &&
                                varDef.ValueDescriptions.Count > 0)
                            {
                                // Check if value matches any defined detent (within tolerance)
                                const double DETENT_TOLERANCE = 0.1;
                                bool matchesDetent = varDef.ValueDescriptions.Keys.Any(key =>
                                    Math.Abs(value - key) < DETENT_TOLERANCE);

                                if (!matchesDetent)
                                {
                                    // Skip announcement for intermediate values (e.g., "4.3" while moving between detents)
                                    continue;
                                }
                            }

                            string description = FormatVariableValue(varKey, varDef, value);

                            // Fire SimVarUpdated event directly (no routing through ProcessIndividualVariableResponse)
                            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                            {
                                VarName = varKey,
                                Value = value,
                                Description = description
                            });

                            processedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] EXCEPTION: Error accessing value for variable '{varKey}' at index {index}: {ex.GetType().Name}: {ex.Message}");
                        exceptionCount++;
                    }
                }  // end foreach
                }  // end fixed
            }  // end unsafe
        }
        catch (ExecutionEngineException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] CRITICAL: ExecutionEngineException caught! This is a serious CLR error.");
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch]   Message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch]   Variable count: {continuousVariableIndexMap.Count}");
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] Skipping this batch to prevent crash. Please report this issue.");
            return;  // Abort processing this batch to prevent crash
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] UNEXPECTED EXCEPTION in unsafe block: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch]   Message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch]   Stack trace: {ex.StackTrace}");
            return;  // Abort processing to prevent crash
        }
    }

    private void ProcessFCUValues(FCUValues data)
    {
        SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
        {
            VarName = "FCU_VALUES",
            Value = 0,
            Description = $"Heading: {data.heading:000}°, Speed: {data.speed:000}, Altitude: {data.altitude:00000} feet"
        });
    }

    private void ProcessAircraftPosition(AircraftPosition data)
    {
        try
        {
            // Always store the last known position and fire the event
            lastKnownPosition = data;
            AircraftPositionReceived?.Invoke(this, data);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error processing aircraft position: {ex.Message}");
        }
    }

    private void ProcessILSGuidance(AircraftPosition data)
    {
        try
        {
            // Validate we have all required data
            if (currentILSRequest == null || ilsRunway == null || ilsAirport == null)
            {
                System.Diagnostics.Debug.WriteLine("[SimConnectManager] ILS guidance request incomplete - missing data");
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ILS_GUIDANCE",
                    Value = 0,
                    Description = "ILS guidance request incomplete - missing data"
                });
                return;
            }

            var ilsData = currentILSRequest;
            var runway = ilsRunway;
            var airport = ilsAirport;

            // Guidance thresholds
            const double CENTERLINE_THRESHOLD = 0.1; // NM - Distance considered "on centerline" (~600 feet)

            // Calculate distance from aircraft to runway threshold
            double distanceToThreshold = NavigationCalculator.CalculateDistance(
                data.Latitude, data.Longitude,
                runway.StartLat, runway.StartLon);

            // Check if approaching from behind (wrong direction)
            // Provide extension guidance regardless of distance - always correct to turn around when on wrong side
            bool fromBehind = NavigationCalculator.IsApproachingFromBehind(
                data.Latitude, data.Longitude,
                data.HeadingMagnetic, // Already magnetic from SimConnect
                runway.StartLat, runway.StartLon,
                runway.EndLat, runway.EndLon,
                ilsData.LocalizerHeading,
                data.MagneticVariation);

            if (fromBehind)
            {
                // Calculate extension heading to fly away from runway
                double extensionHeading = NavigationCalculator.CalculateExtensionHeading(
                    ilsData.LocalizerHeading,
                    data.MagneticVariation);

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ILS_GUIDANCE",
                    Value = 0,
                    Description = $"Approaching from opposite direction. Fly heading {extensionHeading:000} to extend outbound."
                });
                return;
            }

            // Calculate cross-track error (degrees off centerline)
            double crossTrackError = NavigationCalculator.CalculateCrossTrackError(
                data.Latitude, data.Longitude,
                runway.StartLat, runway.StartLon,
                ilsData.LocalizerHeading);

            // Calculate perpendicular distance to localizer centerline
            double distanceToLocalizer = NavigationCalculator.CalculateDistanceToLocalizer(
                data.Latitude, data.Longitude,
                runway.StartLat, runway.StartLon,
                ilsData.LocalizerHeading);

            // Check if on centerline
            bool onCenterline = distanceToLocalizer < CENTERLINE_THRESHOLD;

            string announcement;

            // Check if beyond ILS signal range
            string rangeWarning = "";
            if (ilsData.Range > 0 && distanceToThreshold > ilsData.Range)
            {
                rangeWarning = $"Warning: ILS signal range is {ilsData.Range} nautical miles. ";
            }

            // Calculate glideslope deviation (always, regardless of zone or lateral position)
            bool withinGSRange = NavigationCalculator.IsWithinGlideslopeRange(distanceToThreshold, 25);
            string glideslopeInfo = "";

            if (withinGSRange)
            {
                double gsDeviation = NavigationCalculator.CalculateGlideslopeDeviation(
                    data.Altitude,
                    distanceToThreshold,
                    ilsData.GlideslopePitch,
                    ilsData.AntennaAltitude,
                    ilsData.GlideslopeLatitude,
                    ilsData.GlideslopeLongitude,
                    ilsData.GlideslopeAltitude,
                    data.Latitude,
                    data.Longitude);

                string gsDirection = gsDeviation > 0 ? "above" : "below";
                glideslopeInfo = $" {Math.Abs(gsDeviation):F0} feet {gsDirection} glideslope.";
            }

            // LOCALIZER GUIDANCE (all distances)
            if (onCenterline)
            {
                // On centerline - just track it
                double localizerMagneticHeading = (ilsData.LocalizerHeading - data.MagneticVariation + 360) % 360;
                announcement = $"{rangeWarning}{distanceToThreshold:F1} nautical miles from threshold, on centerline.{glideslopeInfo} " +
                              $"Runway heading {localizerMagneticHeading:000}.";
            }
            else
            {
                // Off centerline - provide three intercept headings
                var (directHeading, mediumHeading, shallowHeading) =
                    NavigationCalculator.CalculateThreeInterceptHeadings(
                        data.Latitude, data.Longitude,
                        runway.StartLat, runway.StartLon,
                        ilsData.LocalizerHeading,
                        data.MagneticVariation);

                string direction = crossTrackError < 0 ? "left" : "right";

                announcement = $"{rangeWarning}{distanceToThreshold:F1} nautical miles from threshold, " +
                              $"{distanceToLocalizer:F1} nautical miles {direction} of centerline, " +
                              $"{Math.Abs(crossTrackError):F0} degrees {direction} of centerline.{glideslopeInfo} " +
                              $"Fly heading {directHeading:000} for 60 degree intercept, " +
                              $"{mediumHeading:000} for 45 degree intercept, " +
                              $"or {shallowHeading:000} for 30 degree intercept.";
            }

            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "ILS_GUIDANCE",
                Value = 0,
                Description = announcement
            });

            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ILS Guidance: {announcement}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error processing ILS guidance: {ex.Message}");
            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "ILS_GUIDANCE",
                Value = 0,
                Description = $"Error processing ILS guidance: {ex.Message}"
            });
        }
    }

    private void ProcessWindData(WindData data)
    {
        try
        {
            WindReceived?.Invoke(this, data);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error processing wind data: {ex.Message}");
        }
    }

    // ProcessECAMData method removed - now using MobiFlight WASM for string L:vars

    /// <summary>
    /// Announce ECAM message changes after all 14 lines are collected.
    /// Only announces NEW messages that weren't in the previous set.
    /// Uses announcement dictionary which includes color descriptions.
    /// </summary>
    private void AnnounceECAMChanges()
    {
        try
        {
            // Collect all current non-empty announcement messages (with color)
            var currentMessages = new HashSet<string>();
            foreach (var kvp in ecamAnnouncementData)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                {
                    currentMessages.Add(kvp.Value);
                }
            }

            // If monitoring is disabled or suppression is enabled, just update the previous set without announcing
            if (!ECAMMonitoringEnabled)
            {
                previousECAMMessages = currentMessages;
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM messages collected silently (monitoring disabled): {currentMessages.Count} messages");
                return;
            }

            if (SuppressECAMAnnouncements)
            {
                previousECAMMessages = currentMessages;
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM messages collected silently (suppression active): {currentMessages.Count} messages");
                return;
            }

            // Find new messages (in current but not in previous)
            var newMessages = new List<string>();
            foreach (var message in currentMessages)
            {
                if (!previousECAMMessages.Contains(message))
                {
                    newMessages.Add(message);
                }
            }

            // Announce each new message
            foreach (var message in newMessages)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] New ECAM message detected for announcement: '{message}'");
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ECAM_MESSAGE",
                    Value = 0,
                    Description = message
                });
            }

            // Update previous set for next comparison
            previousECAMMessages = currentMessages;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error announcing ECAM changes: {ex.Message}");
        }
    }


    private void TryAnnounceConnection()
    {
        // Only announce when we have both aircraft info AND ATC data
        if (pendingAircraftInfo.HasValue && atcDataReceived)
        {
            CheckAircraftType(pendingAircraftInfo.Value);

            // Reset flags for potential reconnection
            pendingAircraftInfo = null;
            atcDataReceived = false;
        }
    }

    private void CheckAircraftType(AircraftInfo info)
    {
        // Build smart identification string based on available ATC data
        string identification = "";

        // Priority 1: Airline + Flight Number (for airline operations)
        if (!string.IsNullOrWhiteSpace(currentAircraftAirline) && !string.IsNullOrWhiteSpace(currentAircraftFlightNumber))
        {
            identification = $" - {currentAircraftAirline} {currentAircraftFlightNumber}";
        }
        // Priority 2: Tail number/registration (if it's not just the aircraft type)
        else if (!string.IsNullOrWhiteSpace(currentAircraftAtcId) &&
                 !currentAircraftAtcId.Contains("A32") &&
                 !currentAircraftAtcId.Contains("A320"))
        {
            identification = $" - {currentAircraftAtcId}";
        }
        // Priority 3: No identification available (just show aircraft type)

        // Announce full aircraft title with ATC identification
        ConnectionStatusChanged?.Invoke(this, $"Connected to {info.title}{identification}");
        wasConnected = true; // Mark that we're now successfully connected
        IsFullyConnected = true; // Aircraft detection complete, hotkeys are now safe to use

        // Log whether this is the expected FBW A32NX aircraft
        if (info.title.Contains("A32NX") || info.title.Contains("A320"))
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Successfully connected to FBW A32NX{identification}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Connected to {info.title}{identification} - not FBW A32NX");
        }
    }

    private void SimConnect_OnRecvException(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        string exceptionName;
        switch (data.dwException)
        {
            case 0: exceptionName = "NONE"; break;
            case 1: exceptionName = "ERROR"; break;
            case 2: exceptionName = "SIZE_MISMATCH"; break;
            case 3: exceptionName = "UNRECOGNIZED_ID"; break;
            case 4: exceptionName = "UNOPENED"; break;
            case 5: exceptionName = "VERSION_MISMATCH"; break;
            case 6: exceptionName = "TOO_MANY_GROUPS"; break;
            case 7: exceptionName = "NAME_UNRECOGNIZED"; break;
            case 8: exceptionName = "TOO_MANY_EVENT_NAMES"; break;
            case 9: exceptionName = "EVENT_ID_DUPLICATE"; break;
            case 10: exceptionName = "TOO_MANY_MAPS"; break;
            case 11: exceptionName = "TOO_MANY_OBJECTS"; break;
            case 12: exceptionName = "TOO_MANY_REQUESTS"; break;
            case 13: exceptionName = "WEATHER_INVALID_PORT"; break;
            case 14: exceptionName = "WEATHER_INVALID_METAR"; break;
            case 15: exceptionName = "WEATHER_UNABLE_TO_GET_OBSERVATION"; break;
            case 16: exceptionName = "WEATHER_UNABLE_TO_CREATE_STATION"; break;
            case 17: exceptionName = "WEATHER_UNABLE_TO_REMOVE_STATION"; break;
            case 18: exceptionName = "INVALID_DATA_TYPE"; break;
            case 19: exceptionName = "INVALID_DATA_SIZE"; break;
            case 20: exceptionName = "DATA_ERROR"; break;
            case 21: exceptionName = "INVALID_ARRAY"; break;
            case 22: exceptionName = "CREATE_OBJECT_FAILED"; break;
            case 23: exceptionName = "LOAD_FLIGHTPLAN_FAILED"; break;
            case 24: exceptionName = "OPERATION_INVALID_FOR_OBJECT_TYPE"; break;
            case 25: exceptionName = "ILLEGAL_OPERATION"; break;
            case 26: exceptionName = "ALREADY_SUBSCRIBED"; break;
            case 27: exceptionName = "INVALID_ENUM"; break;
            case 28: exceptionName = "DEFINITION_ERROR"; break;
            case 29: exceptionName = "DUPLICATE_ID"; break;
            case 30: exceptionName = "DATUM_ID"; break;
            case 31: exceptionName = "OUT_OF_BOUNDS"; break;
            case 32: exceptionName = "ALREADY_CREATED"; break;
            case 33: exceptionName = "OBJECT_OUTSIDE_REALITY_BUBBLE"; break;
            case 34: exceptionName = "OBJECT_CONTAINER"; break;
            case 35: exceptionName = "OBJECT_AI"; break;
            case 36: exceptionName = "OBJECT_ATC"; break;
            case 37: exceptionName = "OBJECT_SCHEDULE"; break;
            default: exceptionName = "UNKNOWN"; break;
        }

        System.Diagnostics.Debug.WriteLine($"SimConnect Exception: {data.dwException} ({exceptionName}) - SendID: {data.dwSendID}, Index: {data.dwIndex}");
    }

    private void SimConnect_OnRecvClientData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_CLIENT_DATA data)
    {
        // Forward MobiFlight client data responses to the WASM module
        if (mobiFlightWasm != null)
        {
            mobiFlightWasm.ProcessClientDataResponse(data);
        }
    }

    public void RequestAircraftInfo()
    {
        if (IsConnected && simConnect != null)
        {
            simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_INFO,
                DATA_DEFINITIONS.AIRCRAFT_INFO, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Also request ATC ID
            simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_ATC_ID,
                DATA_DEFINITIONS.ATC_ID_INFO, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
    }

    /// <summary>
    /// Request variables for a specific panel (replacement for RequestOnDemandVars)
    /// </summary>
    public void RequestPanelVariables(string panelName, string relatedAction = "")
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            var panelControls = CurrentAircraft?.GetPanelControls() ?? new Dictionary<string, List<string>>();
            if (!panelControls.ContainsKey(panelName)) return;

            var panelVariables = panelControls[panelName];
            System.Diagnostics.Debug.WriteLine($"Requesting {panelVariables.Count} variables for panel '{panelName}' after: {relatedAction}");

            foreach (string varKey in panelVariables)
            {
                try
                {
                    RequestVariable(varKey);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RequestPanelVariables] Error requesting variable {varKey}: {ex.Message}");
                    // Continue with other variables even if one fails
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RequestPanelVariables] Error requesting panel '{panelName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Request a single variable by key
    /// </summary>
    /// <param name="varKey">The variable key to request</param>
    /// <param name="forceUpdate">If true, will always fire SimVarUpdated event even if value hasn't changed</param>
    public void RequestVariable(string varKey, bool forceUpdate = false)
    {
        if (!IsConnected || simConnect == null)
        {
            return;
        }

        if (!variableDataDefinitions.ContainsKey(varKey))
        {
            return;
        }

        try
        {
            // Track if this should force an update
            if (forceUpdate)
            {
                lock (forceUpdateVariables)
                {
                    forceUpdateVariables.Add(varKey);
                }
            }

            int dataDefId = variableDataDefinitions[varKey];
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)dataDefId,
                (DATA_DEFINITIONS)dataDefId, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RequestVariable] Error requesting variable {varKey}: {ex.Message}");
        }
    }

    /// <summary>
    /// Request multiple variables by keys
    /// </summary>
    public void RequestVariables(List<string> varKeys)
    {
        foreach (string varKey in varKeys)
        {
            RequestVariable(varKey);
        }
    }

    /// <summary>
    /// Get cached value for a variable if available
    /// </summary>
    public double? GetCachedVariableValue(string varKey)
    {
        if (lastVariableValues.TryGetValue(varKey, out double value))
            return value;
        return null;
    }

    /// <summary>
    /// Get snapshot of multiple cached variables
    /// </summary>
    public Dictionary<string, double> GetCachedVariableSnapshot(List<string> varKeys)
    {
        var snapshot = new Dictionary<string, double>();
        foreach (var key in varKeys)
        {
            if (lastVariableValues.TryGetValue(key, out double value))
                snapshot[key] = value;
        }
        return snapshot;
    }

    // NOTE: FCU request methods (RequestFCUHeading, RequestFCUSpeed, RequestFCUAltitude, RequestFCUVerticalSpeed)
    // have been moved to aircraft-specific implementations.
    // See FlyByWireA320Definition.cs for A320 FCU implementation.
    // Other aircraft will have their own FCU/MCP implementations.

    public void RequestAltitudeAGL()
    {
        System.Diagnostics.Debug.WriteLine("RequestAltitudeAGL called");
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)303;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE ALT ABOVE GROUND", "feet",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)303,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting altitude AGL: {ex.Message}");
            }
        }
    }

    public void RequestAltitudeMSL()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)304;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "INDICATED ALTITUDE", "feet",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)304,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting altitude MSL: {ex.Message}");
            }
        }
    }

    public void RequestAirspeedIndicated()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)305;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AIRSPEED INDICATED", "knots",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)305,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting indicated airspeed: {ex.Message}");
            }
        }
    }

    public void RequestAirspeedTrue()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)306;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AIRSPEED TRUE", "knots",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)306,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting true airspeed: {ex.Message}");
            }
        }
    }

    public void RequestGroundSpeed()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)307;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "GROUND VELOCITY", "knots",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)307,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting ground speed: {ex.Message}");
            }
        }
    }

    public void RequestVerticalSpeed()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)308;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "VERTICAL SPEED", "feet per minute",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)308,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting vertical speed: {ex.Message}");
            }
        }
    }

    public void RequestMachSpeed()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)311;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AIRSPEED MACH", "number",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)311,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting mach speed: {ex.Message}");
            }
        }
    }

    public void RequestBankAngle()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)313;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE BANK DEGREES", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)313,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting bank angle: {ex.Message}");
            }
        }
    }

    public void RequestPitch()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)312;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE PITCH DEGREES", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)312,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting pitch: {ex.Message}");
            }
        }
    }


    public void RequestHeadingMagnetic()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)309;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE HEADING DEGREES MAGNETIC", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)309,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting magnetic heading: {ex.Message}");
            }
        }
    }

    public void RequestPositionForTakeoffAssist()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                // Request one-shot takeoff assist data (lat, lon, pitch, heading) for toggle
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)326,
                    DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting position for takeoff assist: {ex.Message}");
            }
        }
    }

    public void RequestHeadingTrue()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)310;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE HEADING DEGREES TRUE", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)310,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting true heading: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A32NX-SPECIFIC: Requests fuel and payload data for FlyByWire Airbus A320neo.
    /// Uses both standard SimVars and A32NX-specific L-variables for weight/balance calculations.
    /// Called by Forms/A32NX/FuelPayloadDisplayForm.cs only.
    ///
    /// NOTE: This method is safe for multi-aircraft - it's only called when the A32NX Fuel/Payload window
    /// is opened (via hotkey or menu). Other aircraft (Fenix, PMDG, etc.) would have their own methods
    /// with their own variable names if they implement this feature.
    /// </summary>
    public void RequestFuelAndPayloadData()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                // Request fuel data
                RequestSingleValue(340, "FUEL WEIGHT PER GALLON", "kilograms", "FUEL_WEIGHT_PER_GALLON");
                RequestSingleValue(341, "FUEL TANK LEFT AUX QUANTITY", "gallons", "FUEL_LEFT_AUX");
                RequestSingleValue(342, "FUEL TANK LEFT MAIN QUANTITY", "gallons", "FUEL_LEFT_MAIN");
                RequestSingleValue(343, "FUEL TANK CENTER QUANTITY", "gallons", "FUEL_CENTER");
                RequestSingleValue(344, "FUEL TANK RIGHT MAIN QUANTITY", "gallons", "FUEL_RIGHT_MAIN");
                RequestSingleValue(345, "FUEL TANK RIGHT AUX QUANTITY", "gallons", "FUEL_RIGHT_AUX");

                // Request passenger data
                RequestSingleValue(346, "L:A32NX_PAX_A", "number", "PAX_A");
                RequestSingleValue(347, "L:A32NX_PAX_B", "number", "PAX_B");
                RequestSingleValue(348, "L:A32NX_PAX_C", "number", "PAX_C");
                RequestSingleValue(349, "L:A32NX_PAX_D", "number", "PAX_D");
                RequestSingleValue(350, "L:A32NX_WB_PER_PAX_WEIGHT", "kilograms", "PAX_WEIGHT");
                RequestSingleValue(351, "L:A32NX_WB_PER_BAG_WEIGHT", "kilograms", "BAG_WEIGHT");

                // Request cargo data
                RequestSingleValue(352, "PAYLOAD STATION WEIGHT:5", "kilograms", "CARGO_FWD");
                RequestSingleValue(353, "PAYLOAD STATION WEIGHT:6", "kilograms", "CARGO_AFT_CONT");
                RequestSingleValue(354, "PAYLOAD STATION WEIGHT:7", "kilograms", "CARGO_AFT_BAG");
                RequestSingleValue(355, "PAYLOAD STATION WEIGHT:8", "kilograms", "CARGO_AFT_BULK");

                // Request weights
                RequestSingleValue(356, "EMPTY WEIGHT", "kilograms", "EMPTY_WEIGHT");
                RequestSingleValue(357, "L:A32NX_AIRFRAME_ZFW", "number", "ZFW");
                RequestSingleValue(358, "L:A32NX_AIRFRAME_GW", "number", "GW");
                RequestSingleValue(359, "L:A32NX_AIRFRAME_ZFW_CG_PERCENT_MAC", "number", "ZFW_CG_MAC");
                RequestSingleValue(360, "L:A32NX_AIRFRAME_GW_CG_PERCENT_MAC", "number", "GW_CG_MAC");

                // Request FMS values (entered by pilot in MCDU)
                RequestSingleValue(361, "L:A32NX_FMS_PAX_NUMBER", "number", "FMS_PAX");
                RequestSingleValue(362, "L:A32NX_FM1_ZERO_FUEL_WEIGHT", "number", "FMS_ZFW");
                RequestSingleValue(363, "L:A32NX_FM_GROSS_WEIGHT", "number", "FMS_GW");
                RequestSingleValue(364, "L:A32NX_FM1_ZERO_FUEL_WEIGHT_CG", "number", "FMS_CG");

                System.Diagnostics.Debug.WriteLine("[SimConnectManager] Fuel and payload data requested");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting fuel and payload data: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A32NX-SPECIFIC: Requests total fuel quantity for FlyByWire Airbus A320neo.
    /// Uses L:A32NX_TOTAL_FUEL_QUANTITY variable specific to FlyByWire implementation.
    /// Called by Forms/A32NX/ECAMDisplayForm.cs and FlyByWireA320Definition.cs.
    /// </summary>
    public void RequestFuelQuantity()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)314;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_TOTAL_FUEL_QUANTITY", "kilograms",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)314,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting fuel quantity: {ex.Message}");
            }
        }
    }

    public void RequestSingleValue(int id, string simVarName, string units, string varName)
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)id;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    simVarName, units,
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)id,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting {varName}: {ex.Message}");
            }
        }
    }

    private string GetFuelPayloadVarName(int requestId)
    {
        return requestId switch
        {
            340 => "FUEL_WEIGHT_PER_GALLON",
            341 => "FUEL_LEFT_AUX",
            342 => "FUEL_LEFT_MAIN",
            343 => "FUEL_CENTER",
            344 => "FUEL_RIGHT_MAIN",
            345 => "FUEL_RIGHT_AUX",
            346 => "PAX_A",
            347 => "PAX_B",
            348 => "PAX_C",
            349 => "PAX_D",
            350 => "PAX_WEIGHT",
            351 => "BAG_WEIGHT",
            352 => "CARGO_FWD",
            353 => "CARGO_AFT_CONT",
            354 => "CARGO_AFT_BAG",
            355 => "CARGO_AFT_BULK",
            356 => "EMPTY_WEIGHT",
            357 => "ZFW",
            358 => "GW",
            359 => "ZFW_CG_MAC",
            360 => "GW_CG_MAC",
            361 => "FMS_PAX",
            362 => "FMS_ZFW",
            363 => "FMS_GW",
            364 => "FMS_CG",
            _ => "UNKNOWN"
        };
    }

    public void RequestLVarValue(string varName)
    {
        // For now, do nothing - values are updated through tier system
    }

    /// <summary>
    /// Request specific L-variable (legacy compatibility method)
    /// </summary>
    [Obsolete("Use RequestVariable instead for better efficiency")]
    public void RequestSpecificLVar(string varKey, string varName)
    {
        // Use the new system if the variable is already registered
        if (variableDataDefinitions.ContainsKey(varKey))
        {
            RequestVariable(varKey);
            return;
        }

        // Fallback to original behavior for backward compatibility
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Create a unique request ID based on the variable key hash
            int requestId = 400 + Math.Abs(varKey.GetHashCode() % 100);
            var tempDefId = (DATA_DEFINITIONS)requestId;

            // Track this pending request
            pendingRequests[requestId] = varKey;

            // Clear any existing definition
            SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);

            // Add the LVar to the definition
            simConnect.AddToDataDefinition(tempDefId,
                $"L:{varName}", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

            simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);

            // Request the value once
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)requestId,
                tempDefId, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error requesting specific LVar {varName}: {ex.Message}");
        }
    }

    public void SetLVar(string varName, double value)
    {
        if (!IsConnected || simConnect == null) return;

        // For setting LVars, we'll need to use a workaround
        // Create a temporary data definition for this specific LVar
        // Use thread-safe counter to generate unique IDs (fixes crash from ID collision)
        var tempDefId = (DATA_DEFINITIONS)System.Threading.Interlocked.Increment(ref nextTempDefId);

        try
        {
            simConnect.AddToDataDefinition(tempDefId, $"L:{varName}", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

            simConnect.SetDataOnSimObject(tempDefId,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, value);

            SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting LVar {varName}: {ex.Message}");
        }
    }

    public void SetSimVar(string varName, double value, string units = "number")
    {
        if (!IsConnected || simConnect == null) return;

        System.Diagnostics.Debug.WriteLine($"Setting SimVar: {varName} = {value} ({units})");

        // Create a temporary data definition for this specific SimVar
        // Use thread-safe counter to generate unique IDs (fixes crash from ID collision)
        var tempDefId = (DATA_DEFINITIONS)System.Threading.Interlocked.Increment(ref nextTempDefId);

        try
        {
            simConnect.AddToDataDefinition(tempDefId, varName, units,
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

            simConnect.SetDataOnSimObject(tempDefId,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, value);

            SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);

            System.Diagnostics.Debug.WriteLine($"Successfully set SimVar {varName} to {value}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting SimVar {varName}: {ex.Message}");
        }
    }   

    public void SendEvent(string eventName, uint data = 0)
    {
        if (!IsConnected || simConnect == null) return;
        
        System.Diagnostics.Debug.WriteLine($"Sending event: {eventName} with data: {data}");
        
        // Map the event name to an ID if not already mapped
        if (!eventIds.ContainsKey(eventName))
        {
            uint eventId = nextEventId++;
            eventIds[eventName] = eventId;
            simConnect.MapClientEventToSimEvent((EVENTS)eventId, eventName);
            System.Diagnostics.Debug.WriteLine($"Registered new event: {eventName} with ID: {eventId}");
        }
        
        // Send the event with the data parameter
        simConnect.TransmitClientEvent(SIMCONNECT_OBJECT_ID_USER,
            (EVENTS)eventIds[eventName], data, GROUP_PRIORITY.HIGHEST,
            SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    public void ProcessWindowMessage(ref Message m)
    {
        if (m.Msg == WM_USER_SIMCONNECT && simConnect != null)
        {
            try
            {
                simConnect.ReceiveMessage();
            }
            catch (COMException ex)
            {
                // SimConnect disposed or MSFS closed - log and ignore
                System.Diagnostics.Debug.WriteLine($"SimConnect ReceiveMessage COM exception (expected during disconnect): {ex.Message}");
            }
            catch (NullReferenceException ex)
            {
                // SimConnect became null between check and call - log and ignore
                System.Diagnostics.Debug.WriteLine($"SimConnect ReceiveMessage null reference (expected during disconnect): {ex.Message}");
            }
            catch (Exception ex)
            {
                // Unexpected exception - log but don't crash
                System.Diagnostics.Debug.WriteLine($"Unexpected exception in ProcessWindowMessage: {ex}");
            }
        }
    }

    public void TeleportAircraft(double latitude, double longitude, double altitude, double heading, bool onGround = true)
    {
        if (!IsConnected || simConnect == null)
        {
            System.Diagnostics.Debug.WriteLine("Cannot teleport: Not connected to simulator");
            return;
        }

        try
        {
            var initPos = new InitPosition
            {
                Latitude = latitude,
                Longitude = longitude,
                Altitude = altitude,
                Pitch = 0.0,
                Bank = 0.0,
                Heading = heading,
                OnGround = onGround ? 1u : 0u,
                Airspeed = 0
            };

            simConnect.SetDataOnSimObject(DATA_DEFINITIONS.INIT_POSITION,
                SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, initPos);

            System.Diagnostics.Debug.WriteLine($"Aircraft teleported to: {latitude:F6}, {longitude:F6}, {altitude:F0}ft, heading {heading:F0}°");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error teleporting aircraft: {ex.Message}");
        }
    }

    public void TeleportToRunway(Database.Models.Runway runway, Database.Models.Airport airport)
    {
        if (runway == null || airport == null)
        {
            System.Diagnostics.Debug.WriteLine("Cannot teleport: Invalid runway or airport data");
            return;
        }

        // Calculate position 20 meters back from runway threshold for safety
        double distanceBackMeters = 20.0;
        double headingRadians = runway.Heading * Math.PI / 180.0;

        // Convert distance to degrees (approximately)
        double latOffset = (distanceBackMeters / 111111.0) * Math.Cos(headingRadians + Math.PI);
        double lonOffset = (distanceBackMeters / (111111.0 * Math.Cos(runway.StartLat * Math.PI / 180.0))) * Math.Sin(headingRadians + Math.PI);

        double teleportLat = runway.StartLat + latOffset;
        double teleportLon = runway.StartLon + lonOffset;
        double teleportAlt = airport.Altitude + 5.0; // 5 feet above runway

        TeleportAircraft(teleportLat, teleportLon, teleportAlt, runway.Heading, onGround: true);

        // Notify takeoff assist manager of the runway reference
        TakeoffRunwayReferenceSet?.Invoke(this, new TakeoffRunwayReferenceEventArgs
        {
            ThresholdLat = runway.StartLat,
            ThresholdLon = runway.StartLon,
            RunwayHeadingTrue = runway.Heading,
            RunwayHeadingMagnetic = runway.HeadingMag,
            RunwayID = runway.RunwayID,
            AirportICAO = airport.ICAO
        });

        System.Diagnostics.Debug.WriteLine($"Teleported to runway {runway.RunwayID} at {airport.ICAO}");
    }

    public void TeleportToParkingSpot(Database.Models.ParkingSpot parkingSpot, Database.Models.Airport airport)
    {
        if (parkingSpot == null || airport == null)
        {
            System.Diagnostics.Debug.WriteLine("Cannot teleport: Invalid parking spot or airport data");
            return;
        }

        // Teleport to parking spot position
        double teleportAlt = airport.Altitude + 3.0; // 3 feet above ground

        TeleportAircraft(parkingSpot.Latitude, parkingSpot.Longitude, teleportAlt, parkingSpot.Heading, onGround: true);

        System.Diagnostics.Debug.WriteLine($"Teleported to parking spot {parkingSpot} at {airport.ICAO}");
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
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] MobiFlight status: {status}");
            };

            mobiFlightWasm.LVarUpdated += MobiFlightWasm_LVarUpdated;
            mobiFlightWasm.LedValueReceived += MobiFlightWasm_LedValueReceived;
            mobiFlightWasm.ResponseReceived += (sender, response) =>
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] MobiFlight response: {response}");
            };

            // Initialize the WASM module
            mobiFlightWasm.Initialize();

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] MobiFlight WASM module initialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Failed to initialize MobiFlight: {ex.Message}");
            mobiFlightWasm = null;
        }
    }

    private void MobiFlightWasm_LVarUpdated(object? sender, MobiFlightWasmModule.MobiFlightLVarUpdateEventArgs e)
    {
        try
        {
            // Find the corresponding variable definition
            var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
            var varDef = variables.Values.FirstOrDefault(v =>
                v.LedVariable == e.VariableName);

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
                // System.Diagnostics.Debug.WriteLine($"[SimConnectManager] LED update: {varDef.DisplayName} LED = {(e.Value > 0 ? "On" : "Off")}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error processing MobiFlight LVar update: {ex.Message}");
        }
    }

    private void MobiFlightWasm_LedValueReceived(object? sender, MobiFlightWasmModule.MobiFlightLedValueEventArgs e)
    {
        try
        {
            // Find the corresponding variable definition
            var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
            var varDef = variables.Values.FirstOrDefault(v =>
                v.LedVariable == e.LedVariable);

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
                // System.Diagnostics.Debug.WriteLine($"[SimConnectManager] LED value received: {varDef.DisplayName} LED = {(e.Value > 0 ? "On" : "Off")}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] LED variable not found in definitions: {e.LedVariable}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error processing MobiFlight LED value: {ex.Message}");
        }
    }

    public void SendHVar(string hvar)
    {
        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Attempting to send H-variable: {hvar}");
        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] MobiFlight Status: {MobiFlightStatus}");
        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Can Send H-Vars: {CanSendHVars}");

        if (mobiFlightWasm?.CanSendHVars == true)
        {
            mobiFlightWasm.SendHVar(hvar);
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Successfully sent H-variable: {hvar}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ❌ Cannot send H-variable - MobiFlight not ready: {hvar}");
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] MobiFlight module null: {mobiFlightWasm == null}");
            if (mobiFlightWasm != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] IsConnected: {mobiFlightWasm.IsConnected}");
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] IsRegistered: {mobiFlightWasm.IsRegistered}");
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] CanSendHVars: {mobiFlightWasm.CanSendHVars}");
            }
        }
    }

    public void SendButtonPressRelease(string pressEvent, string releaseEvent, int delayMs = 200)
    {
        if (string.IsNullOrEmpty(pressEvent) || string.IsNullOrEmpty(releaseEvent))
        {
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Invalid press/release events");
            return;
        }

        // Send press event
        SendHVar(pressEvent);

        // Set up timer for release event
        var releaseTimer = new System.Windows.Forms.Timer();
        releaseTimer.Interval = delayMs;
        releaseTimer.Tick += (sender, e) =>
        {
            releaseTimer.Stop();
            releaseTimer.Dispose();
            SendHVar(releaseEvent);
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Button press/release completed: {pressEvent} -> {releaseEvent}");
        };
        releaseTimer.Start();
    }

    public void AddLedVariable(string ledVariable)
    {
        if (mobiFlightWasm != null && !string.IsNullOrEmpty(ledVariable))
        {
            // Use default MobiFlight channel to add L-variable for reading
            mobiFlightWasm.AddDefaultChannelLVar(ledVariable);
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Added LED variable for monitoring via default channel: {ledVariable}");
        }
    }

    public void RequestLedVariableUpdate()
    {
        // This triggers an update of all registered L-variables in the default channel
        // MobiFlight will automatically send updates when any L-variable changes
        if (mobiFlightWasm != null)
        {
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] LED variable updates will be received automatically from MobiFlight");
        }
    }

    public void ReadLedVariable(string ledVariable)
    {
        if (mobiFlightWasm != null && !string.IsNullOrEmpty(ledVariable))
        {
            mobiFlightWasm.ReadLedVariable(ledVariable);
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Reading LED variable: {ledVariable}");
        }
    }

    public void Disconnect()
    {
        // Stop reconnect timer first to prevent it from firing during cleanup
        reconnectTimer.Stop();
        System.Diagnostics.Debug.WriteLine("[SimConnectManager] Reconnect timer stopped");

        // Disconnect MobiFlight WASM module
        if (mobiFlightWasm != null)
        {
            mobiFlightWasm.Disconnect();
            mobiFlightWasm.Dispose();
            mobiFlightWasm = null;
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] MobiFlight WASM module disconnected");
        }

        if (simConnect != null)
        {
            try
            {
                // ===== CRITICAL: Clean up SimConnect resources BEFORE disposing =====
                // Without this, data definitions and requests remain registered server-side,
                // causing crashes when restarting the app quickly (< 5-10 seconds).
                // This was the root cause of Fenix A320's intermittent crashes on restart.
                System.Diagnostics.Debug.WriteLine("[SimConnectManager] Cleaning up SimConnect resources before disconnect...");

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
                        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Cancelled {name} request");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error cancelling {name} request (may not exist): {ex.Message}");
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
                        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Message pump exception during disconnect (expected if MSFS closed): {ex.Message}");
                        break;
                    }
                }
                System.Diagnostics.Debug.WriteLine("[SimConnectManager] Waited 500ms for batch request cancellations to process");

                // 3. Clear all 5 batch data definitions
                foreach (var (request, definition, name) in batchConfigs)
                {
                    try
                    {
                        simConnect.ClearDataDefinition(definition);
                        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Cleared {name} definition");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error clearing {name} definition (may not exist): {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Cleared {clearedCount}/{variableDataDefinitions.Count} individual data definitions");

                System.Diagnostics.Debug.WriteLine("[SimConnectManager] SimConnect resource cleanup complete!");
                // ===== END OF CLEANUP SECTION =====

                // Unregister event handlers before disposal to ensure clean disconnect
                simConnect.OnRecvOpen -= SimConnect_OnRecvOpen;
                simConnect.OnRecvQuit -= SimConnect_OnRecvQuit;
                simConnect.OnRecvSimobjectData -= SimConnect_OnRecvSimobjectData;
                simConnect.OnRecvClientData -= SimConnect_OnRecvClientData;
                simConnect.OnRecvException -= SimConnect_OnRecvException;

                System.Diagnostics.Debug.WriteLine("[SimConnectManager] Event handlers unregistered, disposing SimConnect...");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error unregistering event handlers: {ex.Message}");
            }

            simConnect.Dispose();
            simConnect = null;
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] SimConnect disposed");
        }

        // Clear all internal state dictionaries to ensure clean reconnection
        variableDataDefinitions.Clear();
        pendingRequests.Clear();
        lastVariableValues.Clear();
        continuousVariableIndexMap.Clear();
        panelVariableIndexMap.Clear();
        eventIds.Clear();
        forceUpdateVariables.Clear();
        ecamStringData.Clear();
        ecamAnnouncementData.Clear();
        previousECAMMessages.Clear();
        System.Diagnostics.Debug.WriteLine("[SimConnectManager] All internal state dictionaries cleared");

        IsConnected = false;
        IsFullyConnected = false;

        // Only announce disconnection if we were previously connected
        if (wasConnected)
        {
            ConnectionStatusChanged?.Invoke(this, "Disconnected from simulator");
            wasConnected = false;
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Intentional disconnect - announced disconnection");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Disconnect called but was not previously connected");
        }

        // Restart reconnect timer AFTER cleanup is complete (we stopped it at the beginning)
        // This prevents race conditions during cleanup while still enabling auto-reconnect
        reconnectTimer.Start();
        System.Diagnostics.Debug.WriteLine("[SimConnectManager] Disconnect complete - reconnect timer restarted");
    }


    // Takeoff assist monitoring
    public void StartTakeoffAssistMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Request consolidated takeoff assist data at SIM_FRAME rate
            // Includes: lat, lon, pitch, heading for centerline tracking
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)506,
                DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Takeoff assist monitoring started (consolidated data)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error starting takeoff assist monitoring: {ex.Message}");
        }
    }

    public void StopTakeoffAssistMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Stop consolidated takeoff assist data request
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)506,
                DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Takeoff assist monitoring stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error stopping takeoff assist monitoring: {ex.Message}");
        }
    }

    // Hand fly mode monitoring
    public void StartHandFlyMonitoring(bool monitorHeading, bool monitorVerticalSpeed)
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Request continuous updates for pitch and bank at SIM_FRAME rate
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)327,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_PITCH_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            simConnect.RequestDataOnSimObject((DATA_REQUESTS)328,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_BANK_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Request heading monitoring if enabled
            if (monitorHeading)
            {
                var headingDefId = (DATA_DEFINITIONS)371;
                SafelyClearDataDefinition(headingDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(headingDefId,
                    "PLANE HEADING DEGREES MAGNETIC", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(headingDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)371,
                    headingDefId,
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }

            // Request vertical speed monitoring if enabled
            if (monitorVerticalSpeed)
            {
                var vsDefId = (DATA_DEFINITIONS)372;
                SafelyClearDataDefinition(vsDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(vsDefId,
                    "VERTICAL SPEED", "feet per minute",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(vsDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)372,
                    vsDefId,
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }

            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Hand fly mode monitoring started (Heading: {monitorHeading}, VS: {monitorVerticalSpeed})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error starting hand fly mode monitoring: {ex.Message}");
        }
    }

    public void StopHandFlyMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Stop continuous updates for pitch and bank
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)327,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_PITCH_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            simConnect.RequestDataOnSimObject((DATA_REQUESTS)328,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_BANK_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Stop heading monitoring (371)
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)371,
                (DATA_DEFINITIONS)371,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Stop vertical speed monitoring (372)
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)372,
                (DATA_DEFINITIONS)372,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Hand fly mode monitoring stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error stopping hand fly mode monitoring: {ex.Message}");
        }
    }

    // Visual guidance monitoring
    public void StartVisualGuidanceMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Request consolidated visual guidance data at high frequency
            // Includes: lat, lon, altitude MSL, heading, mag var, ground speed, vertical speed, AGL, ground track
            // Using SIM_FRAME for responsive PID controller (~20-30 Hz)
            // Consolidated into single request to reduce message queue flooding (60-90 msg/sec → 20-30 msg/sec)
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)505,
                DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Visual guidance monitoring started (consolidated data)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error starting visual guidance monitoring: {ex.Message}");
        }
    }

    public void StopVisualGuidanceMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Stop consolidated visual guidance data request
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)505,
                DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Visual guidance monitoring stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error stopping visual guidance monitoring: {ex.Message}");
        }
    }

    private int GetVariableDataDefinition(string varKey)
    {
        if (variableDataDefinitions.TryGetValue(varKey, out int defId))
        {
            return defId;
        }
        return -1;
    }

    // Destination runway management
    public void SetDestinationRunway(Runway runway, Airport airport)
    {
        destinationRunway = runway;
        destinationAirport = airport;
        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Destination runway set: {airport.ICAO} Runway {runway.RunwayID}");
    }

    public Runway? GetDestinationRunway()
    {
        return destinationRunway;
    }

    public Airport? GetDestinationAirport()
    {
        return destinationAirport;
    }

    public bool HasDestinationRunway()
    {
        return destinationRunway != null && destinationAirport != null;
    }

    public AircraftPosition? GetAircraftPosition()
    {
        if (!IsConnected) return null;

        try
        {
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
            return null; // Will be returned via event handler
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting aircraft position: {ex.Message}");
            return null;
        }
    }

    public void RequestAircraftPosition()
    {
        if (!IsConnected) return;

        try
        {
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting aircraft position: {ex.Message}");
        }
    }

    public void RequestAircraftPositionAsync(Action<AircraftPosition> callback)
    {
        if (!IsConnected || callback == null) return;

        try
        {
            // Set up a one-time event handler for this specific request
            EventHandler<AircraftPosition>? handler = null;
            handler = (sender, position) =>
            {
                // Unsubscribe the handler after first use
                AircraftPositionReceived -= handler!;
                // Invoke the callback with the position
                callback(position);
            };

            // Subscribe to the event
            AircraftPositionReceived += handler;

            // Request the position data
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting aircraft position async: {ex.Message}");
        }
    }

    public void RequestWindInfo(Action<WindData> callback)
    {
        if (!IsConnected || callback == null) return;

        try
        {
            // Set up a one-time event handler for this specific request
            EventHandler<WindData>? handler = null;
            handler = (sender, windData) =>
            {
                // Unsubscribe the handler after first use
                WindReceived -= handler!;
                // Invoke the callback with the wind data
                callback(windData);
            };

            // Subscribe to the event
            WindReceived += handler;

            // Request the wind data
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_WIND_DATA,
                DATA_DEFINITIONS.WIND_DATA, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting wind info: {ex.Message}");
        }
    }

    public void RequestDestinationRunwayDistance()
    {
        if (!IsConnected || !HasDestinationRunway()) return;
        if (destinationRunway == null || destinationAirport == null) return;

        try
        {
            // Use callback pattern to calculate and announce distance only for this specific request
            RequestAircraftPositionAsync(position =>
            {
                try
                {
                    // Calculate distance to runway threshold
                    double distance = NavigationCalculator.CalculateDistance(
                        position.Latitude, position.Longitude,
                        destinationRunway.StartLat, destinationRunway.StartLon);

                    // Calculate magnetic bearing to runway
                    double bearing = NavigationCalculator.CalculateMagneticBearing(
                        position.Latitude, position.Longitude,
                        destinationRunway.StartLat, destinationRunway.StartLon,
                        position.MagneticVariation);

                    // Format announcement with runway identifier
                    string announcement = $"{distance:F1} miles to runway {destinationRunway.RunwayID} at {destinationAirport.ICAO}, bearing {bearing:000} degrees";

                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "DISTANCE_TO_RUNWAY",
                        Value = 0,
                        Description = announcement
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error calculating destination runway distance: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting destination runway distance: {ex.Message}");
        }
    }

    /// <summary>
    /// Requests ILS (Instrument Landing System) guidance information for the currently selected destination runway.
    /// Calculates localizer and glideslope deviation, intercept heading, and distance from threshold.
    /// </summary>
    /// <param name="ilsData">ILS data for the runway (from database)</param>
    /// <param name="runway">The destination runway</param>
    /// <param name="airport">The destination airport</param>
    public void RequestILSGuidance(ILSData ilsData, Runway runway, Airport airport)
    {
        if (!IsConnected) return;

        try
        {
            // Store ILS request data for processing when position is received
            currentILSRequest = ilsData;
            ilsRunway = runway;
            ilsAirport = airport;

            // Request aircraft position for ILS guidance calculations
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_ILS_GUIDANCE,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting ILS guidance: {ex.Message}");
            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "ILS_GUIDANCE",
                Value = 0,
                Description = $"Error requesting ILS guidance: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// A32NX-SPECIFIC: Requests ECAM (Engine Warning and Advisory Display) message codes for FlyByWire Airbus A320neo.
    /// Retrieves 14 numeric L-variables (A32NX_Ewd_LOWER_LEFT_LINE_*, A32NX_Ewd_LOWER_RIGHT_LINE_*) that map to ECAM messages.
    /// Also requests master warning/caution/stall indicators. Called by Forms/A32NX/ECAMDisplayForm.cs only.
    /// </summary>
    public void RequestECAMMessages()
    {
        if (!IsConnected || simConnect == null)
        {
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Cannot request ECAM messages - not connected");
            return;
        }

        try
        {
            // Reset collection state
            ecamStringData.Clear();
            ecamStringsReceived = 0;

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Requesting ECAM message codes via SimConnect...");

            // Request all 14 numeric L-vars via standard SimConnect (NO MobiFlight needed!)
            // These return numeric codes that get looked up in EWDMessageLookup
            RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_1");
            RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_2");
            RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_3");
            RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_4");
            RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_5");
            RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_6");
            RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_7");

            RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_1");
            RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_2");
            RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_3");
            RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_4");
            RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_5");
            RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_6");
            RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_7");

            // Request numeric status variables
            RequestVariable("A32NX_MASTER_WARNING");
            RequestVariable("A32NX_MASTER_CAUTION");
            RequestVariable("A32NX_STALL_WARNING");

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] All 14 ECAM code requests sent via SimConnect");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting ECAM messages: {ex.Message}");
        }
    }

    /// <summary>
    /// A32NX-SPECIFIC: Requests ECAM STATUS page message codes for FlyByWire Airbus A320neo.
    /// Retrieves 36 numeric L-variables (A32NX_STATUS_LEFT_LINE_1-18, A32NX_STATUS_RIGHT_LINE_1-18) for STATUS display.
    /// Called by Forms/A32NX/StatusDisplayForm.cs only.
    /// </summary>
    public void RequestStatusMessages()
    {
        if (!IsConnected || simConnect == null)
        {
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Cannot request STATUS messages - not connected");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Requesting STATUS message codes via SimConnect...");

            // Request all 36 STATUS variables (18 LEFT + 18 RIGHT)
            // LEFT side
            for (int i = 1; i <= 18; i++)
            {
                RequestVariable($"A32NX_STATUS_LEFT_LINE_{i}");
            }

            // RIGHT side
            for (int i = 1; i <= 18; i++)
            {
                RequestVariable($"A32NX_STATUS_RIGHT_LINE_{i}");
            }

            System.Diagnostics.Debug.WriteLine("[SimConnectManager] All 36 STATUS code requests sent via SimConnect");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting STATUS messages: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert MHz frequency to BCD16 Hz format for COM_STBY_RADIO_SET event
    /// Example: 122.800 MHz → 122800000 Hz → BCD16 encoding
    /// BCD16 Hz represents each digit as 4 bits: 0x122800000 but in practice
    /// SimConnect expects the frequency in a specific BCD format
    /// </summary>
    public static uint ConvertMHzToBcd16Hz(double frequencyMHz)
    {
        // Convert MHz to Hz
        uint frequencyHz = (uint)(frequencyMHz * 1000000);

        // Convert to BCD16 format
        // Each decimal digit becomes a 4-bit nibble
        uint bcd = 0;
        uint multiplier = 1;

        while (frequencyHz > 0)
        {
            uint digit = frequencyHz % 10;
            bcd += digit * multiplier;
            multiplier *= 16; // Shift by 4 bits (one hex digit)
            frequencyHz /= 10;
        }

        return bcd;
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

public class SimVarUpdateEventArgs : EventArgs
{
    public string VarName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Description { get; set; } = string.Empty;
    public SimConnectManager.AircraftPosition? PositionData { get; set; }  // For visual guidance position updates
}

public class ECAMDataEventArgs : EventArgs
{
    public string LeftLine1 { get; set; } = string.Empty;
    public string LeftLine2 { get; set; } = string.Empty;
    public string LeftLine3 { get; set; } = string.Empty;
    public string LeftLine4 { get; set; } = string.Empty;
    public string LeftLine5 { get; set; } = string.Empty;
    public string LeftLine6 { get; set; } = string.Empty;
    public string LeftLine7 { get; set; } = string.Empty;
    public string RightLine1 { get; set; } = string.Empty;
    public string RightLine2 { get; set; } = string.Empty;
    public string RightLine3 { get; set; } = string.Empty;
    public string RightLine4 { get; set; } = string.Empty;
    public string RightLine5 { get; set; } = string.Empty;
    public string RightLine6 { get; set; } = string.Empty;
    public string RightLine7 { get; set; } = string.Empty;
    public bool MasterWarning { get; set; }
    public bool MasterCaution { get; set; }
    public bool StallWarning { get; set; }
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
