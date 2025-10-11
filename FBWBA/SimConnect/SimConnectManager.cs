using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using FBWBA.Database.Models;
using FBWBA.Navigation;

namespace FBWBA.SimConnect
{
    public class SimConnectManager
    {
        private Microsoft.FlightSimulator.SimConnect.SimConnect simConnect;
        private IntPtr windowHandle;
        private const int WM_USER_SIMCONNECT = 0x0402;
        
        // Events
        public event EventHandler<string> ConnectionStatusChanged;
        public event EventHandler<SimVarUpdateEventArgs> SimVarUpdated;
        public event EventHandler<AircraftPosition> AircraftPositionReceived;
        public event EventHandler<WindData> WindReceived;
        public event EventHandler<ECAMDataEventArgs> ECAMDataReceived;
        
        // Connection state
        public bool IsConnected { get; private set; }
        private bool wasConnected = false; // Track if we've already announced connection state
        private System.Windows.Forms.Timer reconnectTimer;

        // MobiFlight WASM integration
        private MobiFlightWasmModule mobiFlightWasm;
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

        // Variable tracking for individual registrations
        private ConcurrentDictionary<string, int> variableDataDefinitions = new ConcurrentDictionary<string, int>();  // Maps variable keys to data definition IDs
        private ConcurrentDictionary<int, string> pendingRequests = new ConcurrentDictionary<int, string>();  // Track pending requests
        private HashSet<string> forceUpdateVariables = new HashSet<string>();  // Track variables that should always fire updates
        private ConcurrentDictionary<string, double> lastVariableValues = new ConcurrentDictionary<string, double>();  // Cache last values for change detection
        private int nextDataDefinitionId = 1000;  // Start IDs from 1000 to avoid conflicts
        
        // Event handling
        private Dictionary<string, uint> eventIds = new Dictionary<string, uint>();
        private uint nextEventId = 1000;

        // Destination runway for ILS guidance
        private Runway destinationRunway;
        private Airport destinationAirport;

        // Last known aircraft position
        private AircraftPosition? lastKnownPosition;

        // Aircraft identification
        private string currentAircraftAtcId = "";
        private string currentAircraftAirline = "";
        private string currentAircraftFlightNumber = "";

        // Visual approach monitoring
        private System.Windows.Forms.Timer visualApproachTimer;
        private bool visualApproachActive = false;
        private string lastVisualApproachAnnouncement = "";

        // Data requests for specific functions
        private enum DATA_REQUESTS
        {
            REQUEST_FCU_VALUES = 2,
            REQUEST_AIRCRAFT_INFO = 3,
            REQUEST_AIRCRAFT_POSITION = 4,
            REQUEST_WIND_DATA = 5,
            REQUEST_ATC_ID = 6,
            REQUEST_ECAM_MESSAGES = 350,
            // Individual variable requests start from 1000
            INDIVIDUAL_VARIABLE_BASE = 1000
        }

        private enum DATA_DEFINITIONS
        {
            FCU_VALUES = 2,
            AIRCRAFT_INFO = 3,
            INIT_POSITION = 4,
            AIRCRAFT_POSITION = 5,
            WIND_DATA = 6,
            ATC_ID_INFO = 7,
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

            visualApproachTimer = new System.Windows.Forms.Timer();
            visualApproachTimer.Interval = 3000; // Default 3 seconds
            visualApproachTimer.Tick += VisualApproachTimer_Tick;
        }

        public void Connect()
        {
            try
            {
                simConnect = new Microsoft.FlightSimulator.SimConnect.SimConnect("FBWBA", windowHandle, WM_USER_SIMCONNECT, null, 0);
                SetupDataDefinitions();
                SetupEvents();
                RegisterClientEvents();

                // Initialize MobiFlight WASM module
                InitializeMobiFlight();

                IsConnected = true;
                reconnectTimer.Stop();

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

        private void ReconnectTimer_Tick(object sender, EventArgs e)
        {
            if (!IsConnected)
            {
                Connect();
            }
        }

        private void SetupDataDefinitions()
        {
            // Register all variables as individual data definitions
            RegisterAllVariables();

            // Start continuous monitoring for announced variables
            StartContinuousMonitoring();

            // Register FCU values for hotkey readouts
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.FCU_VALUES, 
                "L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.FCU_VALUES, 
                "L:A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.FCU_VALUES, 
                "L:A32NX_FCU_AFS_DISPLAY_ALT_VALUE", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
            
            simConnect.RegisterDataDefineStruct<FCUValues>(DATA_DEFINITIONS.FCU_VALUES);

            // Register aircraft info
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_INFO, "TITLE", null,
                SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
            simConnect.RegisterDataDefineStruct<AircraftInfo>(DATA_DEFINITIONS.AIRCRAFT_INFO);

            // Register ATC data separately (ID, Type, Airline, Flight Number)
            // Using STRING256 for all to ensure proper marshaling
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC ID", null,
                SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)0);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC TYPE", null,
                SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)1);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC AIRLINE", null,
                SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)2);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC FLIGHT NUMBER", null,
                SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)3);
            simConnect.RegisterDataDefineStruct<AircraftAtcData>(DATA_DEFINITIONS.ATC_ID_INFO);

            // Register INIT_POSITION for teleportation
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.INIT_POSITION, "Initial Position", null,
                SIMCONNECT_DATATYPE.INITPOSITION, 0.0f, SIMCONNECT_UNUSED);
            simConnect.RegisterDataDefineStruct<InitPosition>(DATA_DEFINITIONS.INIT_POSITION);

            // Register aircraft position for ILS guidance
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE LATITUDE", "degrees",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE LONGITUDE", "degrees",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE ALTITUDE", "feet",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE HEADING DEGREES MAGNETIC", "degrees",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
            simConnect.RegisterDataDefineStruct<AircraftPosition>(DATA_DEFINITIONS.AIRCRAFT_POSITION);

            // Register wind data for wind information
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.WIND_DATA, "AMBIENT WIND DIRECTION", "degrees",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
            simConnect.AddToDataDefinition(DATA_DEFINITIONS.WIND_DATA, "AMBIENT WIND VELOCITY", "knots",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
            simConnect.RegisterDataDefineStruct<WindData>(DATA_DEFINITIONS.WIND_DATA);
        }

        /// <summary>
        /// Register all variables as individual data definitions
        /// </summary>
        private void RegisterAllVariables()
        {
            int registeredCount = 0;

            foreach (var kvp in SimVarDefinitions.Variables)
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
                        simConnect.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                            $"L:{varDef.Name}", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    }
                    else if (varDef.Type == SimVarType.SimVar)
                    {
                        simConnect.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                            varDef.Name, varDef.Units ?? "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    }

                    // Register the SingleValue struct for this definition
                    simConnect.RegisterDataDefineStruct<SingleValue>((DATA_DEFINITIONS)dataDefId);

                    // Only add to dictionary if registration was successful
                    variableDataDefinitions.TryAdd(kvp.Key, dataDefId);
                    registeredCount++;
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
        /// Start continuous monitoring for variables marked as announced
        /// </summary>
        private void StartContinuousMonitoring()
        {
            var continuousVariables = new List<string>();

            foreach (var kvp in SimVarDefinitions.Variables)
            {
                if (kvp.Value.UpdateFrequency == UpdateFrequency.Continuous &&
                    kvp.Value.IsAnnounced &&
                    variableDataDefinitions.ContainsKey(kvp.Key))
                {
                    continuousVariables.Add(kvp.Key);
                }
            }

            System.Diagnostics.Debug.WriteLine($"Starting continuous monitoring for {continuousVariables.Count} announced variables");

            // Request all continuous variables every second
            if (continuousVariables.Count > 0)
            {
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 1000; // 1 second
                timer.Tick += (sender, e) => RequestContinuousVariables(continuousVariables);
                timer.Start();
            }
        }

        /// <summary>
        /// Request updates for continuously monitored variables
        /// </summary>
        private void RequestContinuousVariables(List<string> variableKeys)
        {
            if (!IsConnected || simConnect == null) return;

            foreach (string varKey in variableKeys)
            {
                if (variableDataDefinitions.ContainsKey(varKey))
                {
                    try
                    {
                        int dataDefId = variableDataDefinitions[varKey];
                        simConnect.RequestDataOnSimObject((DATA_REQUESTS)dataDefId,
                            (DATA_DEFINITIONS)dataDefId, SIMCONNECT_OBJECT_ID_USER,
                            SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error requesting continuous variable {varKey}: {ex.Message}");
                    }
                }
            }
        }

        private void SetupEvents()
        {
            simConnect.OnRecvOpen += SimConnect_OnRecvOpen;
            simConnect.OnRecvQuit += SimConnect_OnRecvQuit;
            simConnect.OnRecvSimobjectData += SimConnect_OnRecvSimobjectData;
            simConnect.OnRecvClientData += SimConnect_OnRecvClientData;
            simConnect.OnRecvException += SimConnect_OnRecvException;
        }

        private void RegisterClientEvents()
        {
            int registeredCount = 0;

            // Register FlyByWire custom events
            foreach (var kvp in SimVarDefinitions.Variables)
            {
                if (kvp.Value.Type == SimVarType.Event)
                {
                    uint eventId = nextEventId++;
                    eventIds[kvp.Key] = eventId;

                    try
                    {
                        // Map the event
                        simConnect.MapClientEventToSimEvent((EVENTS)eventId, kvp.Value.Name);
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
            // Connection established, but wait for aircraft verification before announcing
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
                if (pendingRequests.TryRemove((int)data.dwRequestID, out string varKey))
                {
                    // Check if this is an ECAM status variable
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
                    if (SimVarDefinitions.Variables.ContainsKey(varKey))
                    {
                        var varDef = SimVarDefinitions.Variables[varKey];

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
                    CheckAircraftType(aircraftInfo);
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
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error parsing ATC data: {ex.Message}");
                    }
                    break;

                case DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION:
                    AircraftPosition positionData = (AircraftPosition)data.dwData[0];
                    ProcessAircraftPosition(positionData);
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

                case (DATA_REQUESTS)320: // Heading with status
                    FCUValueWithStatus headingWithStatus = (FCUValueWithStatus)data.dwData[0];
                    string headingStatus = headingWithStatus.managedStatus > 0 ? "managed" : "selected";
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "FCU_HEADING_WITH_STATUS",
                        Value = headingWithStatus.value,
                        Description = $"FCU heading {headingWithStatus.value:000} degrees, {headingStatus}"
                    });
                    break;

                case (DATA_REQUESTS)321: // Speed with status
                    FCUValueWithStatus speedWithStatus = (FCUValueWithStatus)data.dwData[0];
                    string speedStatus = speedWithStatus.managedStatus > 0 ? "managed" : "selected";
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "FCU_SPEED_WITH_STATUS",
                        Value = speedWithStatus.value,
                        Description = $"FCU speed {speedWithStatus.value:000} knots, {speedStatus}"
                    });
                    break;

                case (DATA_REQUESTS)322: // Altitude with status
                    FCUValueWithStatus altitudeWithStatus = (FCUValueWithStatus)data.dwData[0];
                    string altitudeStatus = altitudeWithStatus.managedStatus > 0 ? "managed" : "selected";
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "FCU_ALTITUDE_WITH_STATUS",
                        Value = altitudeWithStatus.value,
                        Description = $"FCU altitude {altitudeWithStatus.value:00000} feet, {altitudeStatus}"
                    });
                    break;

                case (DATA_REQUESTS)323: // VS/FPA value
                    FCUValueWithStatus vsfpaWithMode = (FCUValueWithStatus)data.dwData[0];
                    bool isFpaMode = vsfpaWithMode.managedStatus > 0; // Using managedStatus to store FPA mode
                    string modeText = isFpaMode ? "FPA" : "VS";
                    string units = isFpaMode ? "degrees" : "feet per minute";
                    string valueText = isFpaMode ? $"{vsfpaWithMode.value:+0.0;-0.0;0.0}" : $"{vsfpaWithMode.value:+0;-0;0}";
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "FCU_VSFPA_VALUE",
                        Value = vsfpaWithMode.value,
                        Description = $"FCU {modeText} {valueText} {units}"
                    });
                    break;

                case (DATA_REQUESTS)303: // Altitude AGL
                    SingleValue altAglData = (SingleValue)data.dwData[0];
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "ALTITUDE_AGL",
                        Value = altAglData.value,
                        Description = $"Altitude AGL {altAglData.value:0.0} feet"
                    });
                    break;

                case (DATA_REQUESTS)304: // Altitude MSL
                    SingleValue altMslData = (SingleValue)data.dwData[0];
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "ALTITUDE_MSL",
                        Value = altMslData.value,
                        Description = $"Altitude {altMslData.value:0.0} feet"
                    });
                    break;

                case (DATA_REQUESTS)305: // Airspeed Indicated
                    SingleValue iasData = (SingleValue)data.dwData[0];
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "AIRSPEED_INDICATED",
                        Value = iasData.value,
                        Description = $"IAS {iasData.value:0.0} knots"
                    });
                    break;

                case (DATA_REQUESTS)306: // Airspeed True
                    SingleValue tasData = (SingleValue)data.dwData[0];
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "AIRSPEED_TRUE",
                        Value = tasData.value,
                        Description = $"TAS {tasData.value:0.0} knots"
                    });
                    break;

                case (DATA_REQUESTS)307: // Ground Speed
                    SingleValue gsData = (SingleValue)data.dwData[0];
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "GROUND_SPEED",
                        Value = gsData.value,
                        Description = $"Ground speed {gsData.value:0.0} knots"
                    });
                    break;

                case (DATA_REQUESTS)308: // Vertical Speed
                    SingleValue vsData = (SingleValue)data.dwData[0];
                    double vsInFpm = vsData.value * 60; // Convert feet per second to feet per minute
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "VERTICAL_SPEED",
                        Value = vsInFpm,
                        Description = $"Vertical speed {vsInFpm:0.0} feet per minute"
                    });
                    break;

                case (DATA_REQUESTS)309: // Heading Magnetic
                    SingleValue hdgMagData = (SingleValue)data.dwData[0];
                    double hdgMagInDegrees = hdgMagData.value * (180.0 / Math.PI); // Convert radians to degrees
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "HEADING_MAGNETIC",
                        Value = hdgMagInDegrees,
                        Description = $"Magnetic heading {hdgMagInDegrees:000} degrees"
                    });
                    break;

                case (DATA_REQUESTS)310: // Heading True
                    SingleValue hdgTrueData = (SingleValue)data.dwData[0];
                    double hdgTrueInDegrees = hdgTrueData.value * (180.0 / Math.PI); // Convert radians to degrees
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "HEADING_TRUE",
                        Value = hdgTrueInDegrees,
                        Description = $"True heading {hdgTrueInDegrees:000} degrees"
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

                var varDef = SimVarDefinitions.Variables.ContainsKey(varKey) ? SimVarDefinitions.Variables[varKey] : null;

                if (varDef == null)
                {
                    return;
                }

                double currentValue = data.value;

                // Check if this is an ECAM message line variable (numeric code)
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
            else if (varKey == "A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS")
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
                return $"{value:F1}V";
            }
            else if (varDef.Units == "feet")
            {
                return $"{value:F0} feet";
            }
            else if (varDef.Units == "degrees")
            {
                return $"{value:F0} degrees";
            }
            else if (varDef.Units == "knots")
            {
                return $"{value:F0} knots";
            }

            // Default formatting
            return $"{value:F1}";
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

                // Handle ILS/visual approach calculations only if destination runway is set
                if (HasDestinationRunway())
                {
                    // Handle visual approach monitoring if active
                    if (visualApproachActive)
                    {
                        ProcessVisualApproachGuidance(data);
                    }
                    else
                    {
                        // Handle one-time ILS guidance request
                        var guidance = NavigationCalculator.CalculateILSGuidance(data, destinationRunway, destinationAirport);
                        string announcement = FormatILSGuidanceAnnouncement(guidance);

                        SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                        {
                            VarName = "ILS_GUIDANCE",
                            Value = 0,
                            Description = announcement
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error processing aircraft position: {ex.Message}");
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

        private void ProcessVisualApproachGuidance(AircraftPosition data)
        {
            try
            {
                // Calculate visual approach guidance
                var visualGuidance = Navigation.VisualApproachMonitor.CalculateGuidance(data, destinationRunway, destinationAirport);

                // Check if monitoring should continue
                if (!visualGuidance.ShouldContinue)
                {
                    // Announce why monitoring is stopping
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "VISUAL_APPROACH_STATUS",
                        Value = 0,
                        Description = $"Visual approach monitoring stopped: {visualGuidance.StopReason}"
                    });

                    StopVisualApproachMonitoring();
                    return;
                }

                // Update timer interval if needed
                if (visualApproachTimer.Interval != visualGuidance.UpdateIntervalMs)
                {
                    visualApproachTimer.Interval = visualGuidance.UpdateIntervalMs;
                }

                // Format and announce guidance
                string announcement = Navigation.VisualApproachMonitor.FormatGuidanceAnnouncement(visualGuidance);

                // Only announce if the message has changed (prevents spam)
                if (announcement != lastVisualApproachAnnouncement)
                {
                    lastVisualApproachAnnouncement = announcement;
                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "VISUAL_APPROACH_GUIDANCE",
                        Value = 0,
                        Description = announcement
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error processing visual approach guidance: {ex.Message}");
            }
        }

        private string FormatILSGuidanceAnnouncement(ILSGuidance guidance)
        {
            // Simplified format: one announcement with direct heading to centerline
            string gsText = guidance.GlideSlopeDeviation > 0
                ? $"{Math.Abs(guidance.GlideSlopeDeviation):0} feet above glideslope"
                : $"{Math.Abs(guidance.GlideSlopeDeviation):0} feet below glideslope";

            switch (guidance.State)
            {
                case ILSGuidanceState.Established:
                    // Established on localizer
                    return $"Established on localizer, heading {guidance.CurrentHeading:000}, " +
                           $"{guidance.DistanceToThreshold:F1} miles to threshold, {gsText}";

                case ILSGuidanceState.TooFar:
                    // Beyond 100nm - warning only
                    return $"Caution: {guidance.DistanceToThreshold:F1} miles from runway. " +
                           $"Guidance available within 100 miles. Turn {guidance.TurnDirection} to heading {guidance.CurrentHeading:000} toward destination.";

                default:
                    // Simplified: Direct-to centerline (VectoringToSetup state)
                    return $"Turn {guidance.TurnDirection} to heading {guidance.InterceptHeading:000}, " +
                           $"{guidance.DistanceToSetupPoint:F1} miles to centerline point, " +
                           $"{guidance.DistanceToThreshold:F1} miles to threshold, {gsText}";
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

            if (info.title.Contains("A32NX") || info.title.Contains("A320"))
            {
                ConnectionStatusChanged?.Invoke(this, $"Connected to FBW A32NX{identification}");
                wasConnected = true; // Mark that we're now successfully connected
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Successfully connected to FBW A32NX{identification}");
            }
            else
            {
                ConnectionStatusChanged?.Invoke(this, $"Warning: Connected to {info.title}{identification} - not FBW A32NX");
                wasConnected = true; // Still connected, just wrong aircraft
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Connected to wrong aircraft: {info.title}{identification}");
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

            if (!SimVarDefinitions.PanelControls.ContainsKey(panelName)) return;

            var panelVariables = SimVarDefinitions.PanelControls[panelName];
            System.Diagnostics.Debug.WriteLine($"Requesting {panelVariables.Count} variables for panel '{panelName}' after: {relatedAction}");

            foreach (string varKey in panelVariables)
            {
                RequestVariable(varKey);
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

        public void RequestFCUHeading()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    // Create a specific request for just heading
                    var tempDefId = (DATA_DEFINITIONS)300;
                    // Don't clear if it doesn't exist - just define it
                    simConnect.AddToDataDefinition(tempDefId, 
                        "L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", "number", 
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)300,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting heading: {ex.Message}");
                }
            }
        }
        
        public void RequestFCUSpeed()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)301;
                    simConnect.AddToDataDefinition(tempDefId, 
                        "L:A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE", "number", 
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)301,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting speed: {ex.Message}");
                }
            }
        }
        
        public void RequestFCUAltitude()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)302;
                    simConnect.AddToDataDefinition(tempDefId, 
                        "L:A32NX_FCU_AFS_DISPLAY_ALT_VALUE", "number", 
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)302,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting altitude: {ex.Message}");
                }
            }
        }

        public void RequestFCUHeadingWithStatus()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)320;
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<FCUValueWithStatus>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)320,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting heading with status: {ex.Message}");
                }
            }
        }

        public void RequestFCUSpeedWithStatus()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)321;
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<FCUValueWithStatus>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)321,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting speed with status: {ex.Message}");
                }
            }
        }

        public void RequestFCUAltitudeWithStatus()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)322;
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FCU_AFS_DISPLAY_ALT_VALUE", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<FCUValueWithStatus>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)322,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting altitude with status: {ex.Message}");
                }
            }
        }

        public void RequestFCUVerticalSpeedFPA()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)323;
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_TRK_FPA_MODE_ACTIVE", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<FCUValueWithStatus>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)323,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting VS/FPA: {ex.Message}");
                }
            }
        }

        public void RequestFCUValues()
        {
            if (IsConnected && simConnect != null)
            {
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_FCU_VALUES,
                    DATA_DEFINITIONS.FCU_VALUES, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
        }

        public void RequestAltitudeAGL()
        {
            System.Diagnostics.Debug.WriteLine("RequestAltitudeAGL called");
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)303;
                    simConnect.ClearDataDefinition(tempDefId);
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
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "PLANE ALTITUDE", "feet",
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
                    simConnect.ClearDataDefinition(tempDefId);
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
                    simConnect.ClearDataDefinition(tempDefId);
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
                    simConnect.ClearDataDefinition(tempDefId);
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
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "VERTICAL SPEED", "feet per second",
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

        public void RequestFuelQuantity()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)314;
                    simConnect.ClearDataDefinition(tempDefId);
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

        public void RequestHeadingMagnetic()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)309;
                    simConnect.ClearDataDefinition(tempDefId);
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

        public void RequestHeadingTrue()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)310;
                    simConnect.ClearDataDefinition(tempDefId);
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

        // Speed tape value request methods
        public void RequestSpeedGD()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)330;
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_SPEEDS_GD", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)330,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting Speed GD: {ex.Message}");
                }
            }
        }

        public void RequestSpeedS()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)331;
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_SPEEDS_S", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)331,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting Speed S: {ex.Message}");
                }
            }
        }

        public void RequestSpeedF()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)332;
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_SPEEDS_F", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)332,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting Speed F: {ex.Message}");
                }
            }
        }

        public void RequestSpeedVFE()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)335;
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_FAC_1_V_FE_NEXT.value", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)335,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting Speed VFE: {ex.Message}");
                }
            }
        }

        public void RequestSpeedVLS()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)336;
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_SPEEDS_VLS", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)336,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting Speed VLS: {ex.Message}");
                }
            }
        }

        public void RequestSpeedVS()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)337;
                    simConnect.ClearDataDefinition(tempDefId);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_SPEEDS_VS", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)337,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting Speed VS: {ex.Message}");
                }
            }
        }

        public void RequestWaypointInfo()
        {
            if (IsConnected && simConnect != null)
            {
                try
                {
                    var tempDefId = (DATA_DEFINITIONS)315;
                    simConnect.ClearDataDefinition(tempDefId);

                    // Add all waypoint variables to definition
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_EFIS_L_TO_WPT_IDENT_0", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_EFIS_L_TO_WPT_IDENT_1", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_EFIS_L_TO_WPT_DISTANCE", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(tempDefId,
                        "L:A32NX_EFIS_L_TO_WPT_BEARING", "radians",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

                    simConnect.RegisterDataDefineStruct<WaypointInfo>(tempDefId);
                    simConnect.RequestDataOnSimObject((DATA_REQUESTS)315,
                        tempDefId, SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error requesting waypoint info: {ex.Message}");
                }
            }
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
                try { simConnect.ClearDataDefinition(tempDefId); } catch { }

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
            var tempDefId = (DATA_DEFINITIONS)(100 + variableDataDefinitions.Count);

            try
            {
                simConnect.AddToDataDefinition(tempDefId, $"L:{varName}", "number",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

                simConnect.SetDataOnSimObject(tempDefId,
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT, value);

                simConnect.ClearDataDefinition(tempDefId);
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
            var tempDefId = (DATA_DEFINITIONS)(200 + variableDataDefinitions.Count);

            try
            {
                simConnect.AddToDataDefinition(tempDefId, varName, units,
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

                simConnect.SetDataOnSimObject(tempDefId,
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT, value);

                simConnect.ClearDataDefinition(tempDefId);

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
                simConnect.ReceiveMessage();
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
                mobiFlightWasm = new MobiFlightWasmModule(simConnect);

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

        private void MobiFlightWasm_LVarUpdated(object sender, MobiFlightWasmModule.MobiFlightLVarUpdateEventArgs e)
        {
            try
            {
                // Find the corresponding variable definition
                var varDef = SimVarDefinitions.Variables.Values.FirstOrDefault(v =>
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

        private void MobiFlightWasm_LedValueReceived(object sender, MobiFlightWasmModule.MobiFlightLedValueEventArgs e)
        {
            try
            {
                // Find the corresponding variable definition
                var varDef = SimVarDefinitions.Variables.Values.FirstOrDefault(v =>
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

        public void ExecuteCalculatorCode(string calculatorCode)
        {
            if (mobiFlightWasm?.IsRegistered == true)
            {
                mobiFlightWasm.ExecuteCalculatorCode(calculatorCode);
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Executed calculator code: {calculatorCode}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Cannot execute calculator code - MobiFlight not available: {calculatorCode}");
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
                simConnect.Dispose();
                simConnect = null;
            }
            IsConnected = false;

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

            reconnectTimer.Start();
        }

        // Visual approach monitoring
        private void VisualApproachTimer_Tick(object sender, EventArgs e)
        {
            if (!IsConnected || !HasDestinationRunway() || !visualApproachActive) return;

            try
            {
                // Request aircraft position for visual approach guidance
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                    DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting visual approach data: {ex.Message}");
            }
        }

        public void StartVisualApproachMonitoring()
        {
            if (!HasDestinationRunway())
            {
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_APPROACH_ERROR",
                    Value = 0,
                    Description = "No destination runway selected. Press bracket Ctrl+D to select a destination runway first."
                });
                return;
            }

            visualApproachActive = true;
            visualApproachTimer.Start();
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Visual approach monitoring started");

            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "VISUAL_APPROACH_STATUS",
                Value = 0,
                Description = "Visual approach monitoring started"
            });
        }

        public void StopVisualApproachMonitoring()
        {
            visualApproachActive = false;
            visualApproachTimer.Stop();
            lastVisualApproachAnnouncement = ""; // Reset for next session
            System.Diagnostics.Debug.WriteLine("[SimConnectManager] Visual approach monitoring stopped");

            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "VISUAL_APPROACH_STATUS",
                Value = 0,
                Description = "Visual approach monitoring stopped"
            });
        }

        public void ToggleVisualApproachMonitoring()
        {
            if (visualApproachActive)
                StopVisualApproachMonitoring();
            else
                StartVisualApproachMonitoring();
        }

        // Destination runway management
        public void SetDestinationRunway(Runway runway, Airport airport)
        {
            destinationRunway = runway;
            destinationAirport = airport;
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Destination runway set: {airport.ICAO} Runway {runway.RunwayID}");
        }

        public Runway GetDestinationRunway()
        {
            return destinationRunway;
        }

        public Airport GetDestinationAirport()
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
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
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

        public void RequestAircraftPositionAsync(Action<AircraftPosition> callback)
        {
            if (!IsConnected || callback == null) return;

            try
            {
                // Set up a one-time event handler for this specific request
                EventHandler<AircraftPosition> handler = null;
                handler = (sender, position) =>
                {
                    // Unsubscribe the handler after first use
                    AircraftPositionReceived -= handler;
                    // Invoke the callback with the position
                    callback(position);
                };

                // Subscribe to the event
                AircraftPositionReceived += handler;

                // Request the position data
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
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
                EventHandler<WindData> handler = null;
                handler = (sender, windData) =>
                {
                    // Unsubscribe the handler after first use
                    WindReceived -= handler;
                    // Invoke the callback with the wind data
                    callback(windData);
                };

                // Subscribe to the event
                WindReceived += handler;

                // Request the wind data
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_WIND_DATA,
                    DATA_DEFINITIONS.WIND_DATA, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting wind info: {ex.Message}");
            }
        }

        public void RequestILSGuidance()
        {
            if (!IsConnected || !HasDestinationRunway()) return;

            try
            {
                // Request aircraft position for ILS guidance calculation
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                    DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting ILS guidance: {ex.Message}");
            }
        }

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
        public string VarName { get; set; }
        public double Value { get; set; }
        public string Description { get; set; }
    }

    public class ECAMDataEventArgs : EventArgs
    {
        public string LeftLine1 { get; set; }
        public string LeftLine2 { get; set; }
        public string LeftLine3 { get; set; }
        public string LeftLine4 { get; set; }
        public string LeftLine5 { get; set; }
        public string LeftLine6 { get; set; }
        public string LeftLine7 { get; set; }
        public string RightLine1 { get; set; }
        public string RightLine2 { get; set; }
        public string RightLine3 { get; set; }
        public string RightLine4 { get; set; }
        public string RightLine5 { get; set; }
        public string RightLine6 { get; set; }
        public string RightLine7 { get; set; }
        public bool MasterWarning { get; set; }
        public bool MasterCaution { get; set; }
        public bool StallWarning { get; set; }
    }
}
