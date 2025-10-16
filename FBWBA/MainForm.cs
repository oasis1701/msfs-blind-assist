using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FBWBA.Accessibility;
using FBWBA.Database;
using FBWBA.Database.Models;
using FBWBA.Forms;
using FBWBA.Hotkeys;
using FBWBA.Services;
using FBWBA.SimConnect;

namespace FBWBA
{
    public partial class MainForm : Form
    {
        private SimConnectManager simConnectManager;
        private SimVarMonitor simVarMonitor;
        private ScreenReaderAnnouncer announcer;
        private HotkeyManager hotkeyManager;
        private IAirportDataProvider airportDataProvider;
        private ChecklistForm checklistForm;
        private TakeoffAssistManager takeoffAssistManager;
        private ElectronicFlightBagForm electronicFlightBagForm;
        private FBWBA.Navigation.FlightPlanManager flightPlanManager;

        // Current state
        private string currentSection = "";
        private string currentPanel = "";
        private Dictionary<string, Control> currentControls = new Dictionary<string, Control>();
        private Dictionary<string, double> currentSimVarValues = new Dictionary<string, double>();
        private bool updatingFromSim = false;
        private Dictionary<string, double> displayValues = new Dictionary<string, double>();  // Store display values
        private Dictionary<string, TaskCompletionSource<bool>> pendingDisplayRequests = null;  // Track pending display requests
        private ConcurrentDictionary<string, bool> pendingStateAnnouncements = new ConcurrentDictionary<string, bool>();  // Track state announcement requests
        private string currentFlightPhase = "";  // Track current flight phase for window title

        // Unified mapping of button events to their corresponding state variables
        private static readonly Dictionary<string, string> ButtonStateMapping = new Dictionary<string, string>
        {
            // FCU buttons
            ["A32NX.FCU_HDG_PUSH"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
            ["A32NX.FCU_HDG_PULL"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
            ["A32NX.FCU_SPD_PUSH"] = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
            ["A32NX.FCU_SPD_PULL"] = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
            ["A32NX.FCU_ALT_PUSH"] = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
            ["A32NX.FCU_ALT_PULL"] = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
            ["A32NX.FCU_LOC_PUSH"] = "A32NX_FCU_LOC_LIGHT_ON",
            ["A32NX.FCU_APPR_PUSH"] = "A32NX_FCU_APPR_LIGHT_ON",
            ["A32NX.FCU_AP_1_PUSH"] = "A32NX_FCU_AP_1_LIGHT_ON",
            ["A32NX.FCU_AP_2_PUSH"] = "A32NX_FCU_AP_2_LIGHT_ON",
            ["A32NX.FCU_ATHR_PUSH"] = "A32NX_FCU_ATHR_LIGHT_ON",
            ["A32NX.FCU_EXPED_PUSH"] = "A32NX_FCU_EXPED_LIGHT_ON",
            ["A32NX.FCU_SPD_MACH_TOGGLE_PUSH"] = "A32NX_FCU_AFS_DISPLAY_MACH_MODE",
            ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = "A32NX_TRK_FPA_MODE_ACTIVE",

            // EFIS Control Panel buttons
            ["A32NX.FCU_EFIS_L_FD_PUSH"] = "A32NX_FCU_EFIS_L_FD_LIGHT_ON",
            ["A32NX.FCU_EFIS_R_FD_PUSH"] = "A32NX_FCU_EFIS_R_FD_LIGHT_ON",
            ["A32NX.FCU_EFIS_L_BARO_PUSH"] = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
            ["A32NX.FCU_EFIS_L_BARO_PULL"] = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
            ["A32NX.FCU_EFIS_R_BARO_PUSH"] = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",
            ["A32NX.FCU_EFIS_R_BARO_PULL"] = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",

            // Autobrake buttons
            ["A32NX.AUTOBRAKE_SET_DISARM"] = "A32NX_AUTOBRAKES_ARMED_MODE",
            ["A32NX.AUTOBRAKE_BUTTON_LO"] = "A32NX_AUTOBRAKES_ARMED_MODE",
            ["A32NX.AUTOBRAKE_BUTTON_MED"] = "A32NX_AUTOBRAKES_ARMED_MODE",
            ["A32NX.AUTOBRAKE_BUTTON_MAX"] = "A32NX_AUTOBRAKES_ARMED_MODE",

            // Pedestal buttons
            ["SPOILERS_ARM_TOGGLE"] = "A32NX_SPOILERS_ARMED",
            ["SPOILERS_ON"] = "A32NX_SPOILERS_HANDLE_POSITION",
            ["SPOILERS_OFF"] = "A32NX_SPOILERS_HANDLE_POSITION",

        };

        public MainForm()
        {
            InitializeComponent();
            InitializeManagers();
            
            // Set up form after load
            this.Load += MainForm_Load;
        }
        
        private void MainForm_Load(object sender, EventArgs e)
        {
            // Populate sections after form is loaded
            sectionsListBox.Items.Add("Overhead Forward");
            sectionsListBox.Items.Add("Glareshield");
            sectionsListBox.Items.Add("Instrument");
            sectionsListBox.Items.Add("Pedestal");

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
            simConnectManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            simConnectManager.SimulatorVersionDetected += OnSimulatorVersionDetected;
            simConnectManager.SimVarUpdated += OnSimVarUpdated;

            simVarMonitor = new SimVarMonitor();
            simVarMonitor.ValueChanged += OnSimVarValueChanged;

            hotkeyManager = new HotkeyManager();
            hotkeyManager.Initialize(this.Handle); // Initialize with window handle
            hotkeyManager.HotkeyTriggered += OnHotkeyTriggered;
            hotkeyManager.OutputHotkeyModeChanged += OnOutputHotkeyModeChanged;
            hotkeyManager.InputHotkeyModeChanged += OnInputHotkeyModeChanged;

            // Initialize takeoff assist manager
            takeoffAssistManager = new TakeoffAssistManager(announcer);
            takeoffAssistManager.TakeoffAssistActiveChanged += OnTakeoffAssistActiveChanged;

            // Initialize airport database provider
            airportDataProvider = DatabaseSelector.SelectProvider();

            // Initialize flight plan manager with navigation database
            var settings = FBWBA.Settings.SettingsManager.Current;
            string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
            flightPlanManager = new FBWBA.Navigation.FlightPlanManager(navigationDatabasePath, airportDataProvider);

            // Update status bar with database info
            UpdateDatabaseStatusDisplay();

            // Connect after a delay
            Timer connectTimer = new Timer();
            connectTimer.Interval = 2000;
            connectTimer.Tick += (s, e) =>
            {
                connectTimer.Stop();
                connectTimer.Dispose();
                simConnectManager.Connect();
            };
            connectTimer.Start();
        }

        private void OnSimulatorVersionDetected(object sender, string version)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnSimulatorVersionDetected(sender, version)));
                return;
            }

            // Announce the detected simulator version
            announcer.Announce(version);
        }

        private void OnConnectionStatusChanged(object sender, string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnConnectionStatusChanged(sender, status)));
                return;
            }

            statusLabel.Text = status;

            if (status.StartsWith("Connected to"))
            {
                announcer.Announce(status);

                // Automatically switch database if simulator version doesn't match
                CheckAndSwitchDatabase();

                // Request all current values when connected
                RequestAllCurrentValues();

                // Start a grace period before enabling continuous variable announcements
                // This prevents initial ECAM messages and other variables from being announced
                // when connecting to a cold and dark aircraft
                Timer announcementGracePeriodTimer = new Timer();
                announcementGracePeriodTimer.Interval = 5000; // 5 second grace period
                announcementGracePeriodTimer.Tick += (s, e) =>
                {
                    announcementGracePeriodTimer.Stop();
                    announcementGracePeriodTimer.Dispose();
                    simVarMonitor.EnableAnnouncements();
                    simConnectManager.EnableECAMAnnouncements();
                };
                announcementGracePeriodTimer.Start();
            }
            else if (status.Contains("Disconnected"))
            {
                announcer.Announce(status);
                // Disable announcements when disconnected
                simVarMonitor.Reset();
                // Reset ECAM suppression flag for next connection
                simConnectManager.SuppressECAMAnnouncements = true;
            }
        }

        private void OnSimVarUpdated(object sender, SimVarUpdateEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                return;
            }

            // Step 1: ALWAYS store the value first (needed by all consumers)
            currentSimVarValues[e.VarName] = e.Value;

            // Step 2: Handle special one-off announcements (terminal cases only)
            if (HandleSpecialAnnouncements(e))
            {
                return; // These are terminal - no further processing needed
            }

            // Step 3: Update display values (if this variable is used in any panel display)
            // This happens silently without announcements - users read the display manually
            if (SimVarDefinitions.Variables.ContainsKey(e.VarName) &&
                SimVarDefinitions.PanelDisplayVariables.Values.Any(list => list.Contains(e.VarName)))
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
                    TextBox displayBox = currentControls["_DISPLAY_"] as TextBox;
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
            if (SimVarDefinitions.Variables.ContainsKey(e.VarName))
            {
                var varDef = SimVarDefinitions.Variables[e.VarName];
                if (varDef.IsAnnounced && varDef.UpdateFrequency == UpdateFrequency.Continuous)
                {
                    simVarMonitor.ProcessUpdate(e.VarName, e.Value, e.Description);
                }
            }
        }

        /// <summary>
        /// Handles special announcements that should terminate processing.
        /// Returns true if the event was handled and no further processing is needed.
        /// </summary>
        private bool HandleSpecialAnnouncements(SimVarUpdateEventArgs e)
        {
            // Handle flight phase updates
            if (e.VarName == "A32NX_FMGC_FLIGHT_PHASE")
            {
                var varDefinition = SimVarDefinitions.Variables[e.VarName];
                if (varDefinition.ValueDescriptions.TryGetValue(e.Value, out string phaseName))
                {
                    // Only update if the phase has actually changed
                    if (currentFlightPhase != phaseName)
                    {
                        currentFlightPhase = phaseName;

                        // Update window title
                        this.Text = $"FBWBA - {phaseName} phase active";

                        // Announce the phase change
                        announcer.Announce($"Entering {phaseName} phase");
                    }
                }
                return true;
            }

            // Handle FCU hotkey value announcements
            if (e.VarName == "FCU_HEADING" || e.VarName == "FCU_SPEED" || e.VarName == "FCU_ALTITUDE" ||
                e.VarName == "FCU_HEADING_WITH_STATUS" || e.VarName == "FCU_SPEED_WITH_STATUS" ||
                e.VarName == "FCU_ALTITUDE_WITH_STATUS" || e.VarName == "FCU_VSFPA_VALUE")
            {
                announcer.AnnounceImmediate(e.Description);
                return true;
            }

            // Handle takeoff assist toggle activation (receives heading from RequestHeadingForTakeoffAssist)
            if (e.VarName == "HEADING_FOR_TAKEOFF_ASSIST")
            {
                // Convert radians to degrees (value is in radians)
                double headingDegrees = e.Value * (180.0 / Math.PI);
                takeoffAssistManager.Toggle(headingDegrees);
                return true;
            }

            // Handle takeoff assist pitch updates
            if (e.VarName == "PLANE_PITCH_DEGREES" && takeoffAssistManager.IsActive)
            {
                // Convert radians to degrees and negate (SimConnect uses body axis: negative = nose up)
                double pitchDegrees = -(e.Value * (180.0 / Math.PI));
                takeoffAssistManager.ProcessPitchUpdate(pitchDegrees);
                return true;
            }

            // Handle takeoff assist heading updates
            if (e.VarName == "PLANE_HEADING_DEGREES_MAGNETIC" && takeoffAssistManager.IsActive)
            {
                double headingDegrees = e.Value * (180.0 / Math.PI); // Convert radians to degrees
                takeoffAssistManager.ProcessHeadingUpdate(headingDegrees);
                return true;
            }

            // Handle aircraft variable hotkey announcements
            if (e.VarName == "ALTITUDE_AGL" || e.VarName == "ALTITUDE_MSL" || e.VarName == "AIRSPEED_INDICATED" ||
                e.VarName == "AIRSPEED_TRUE" || e.VarName == "GROUND_SPEED" || e.VarName == "MACH_SPEED" ||
                e.VarName == "VERTICAL_SPEED" || e.VarName == "HEADING_MAGNETIC" || e.VarName == "HEADING_TRUE" ||
                e.VarName == "SPEED_GD" || e.VarName == "SPEED_S" || e.VarName == "SPEED_F" ||
                e.VarName == "SPEED_VFE" || e.VarName == "SPEED_VLS" || e.VarName == "SPEED_VS" ||
                e.VarName == "FUEL_QUANTITY" || e.VarName == "WAYPOINT_INFO")
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

            // Handle visual approach announcements
            if (e.VarName == "VISUAL_APPROACH_STATUS" ||
                e.VarName == "VISUAL_APPROACH_ERROR" ||
                e.VarName == "VISUAL_APPROACH_GUIDANCE")
            {
                announcer.AnnounceImmediate(e.Description);
                return true;
            }

            // Handle LED state announcements (from MobiFlight WASM)
            if (e.VarName?.StartsWith("A32NX_ECP_LIGHT_") == true)
            {
                announcer.AnnounceImmediate(e.Description);
                return true;
            }

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
            if (SimVarDefinitions.Variables.ContainsKey(varName))
            {
                var varDef = SimVarDefinitions.Variables[varName];
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
            if (currentControls.ContainsKey(varName))
            {
                updatingFromSim = true;
                
                Control control = currentControls[varName];
                if (control is ComboBox combo)
                {
                    // Find the matching value in the combo box
                    if (SimVarDefinitions.Variables.ContainsKey(varName))
                    {
                        var varDef = SimVarDefinitions.Variables[varName];
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
                
                updatingFromSim = false;
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
        private string GetPanelForVariable(string varKey)
        {
            foreach (var panel in SimVarDefinitions.PanelControls)
            {
                if (panel.Value.Contains(varKey))
                {
                    return panel.Key;
                }
            }
            return null; // Variable not found in any panel
        }

        /// <summary>
        /// Request variables efficiently based on the variable context
        /// </summary>
        private void RequestRelatedVariables(string varKey, string actionDescription)
        {
            string panelName = GetPanelForVariable(varKey);

            if (panelName != null)
            {
                // Request all variables for the panel this variable belongs to
                simConnectManager.RequestPanelVariables(panelName, actionDescription);
                System.Diagnostics.Debug.WriteLine($"Requesting panel '{panelName}' variables after {actionDescription}");
            }
            else
            {
                // Fallback: request just the specific variable
                simConnectManager.RequestVariable(varKey);
                System.Diagnostics.Debug.WriteLine($"Requesting single variable '{varKey}' after {actionDescription}");
            }
        }

        private void HandleButtonStateAnnouncement(string eventName)
        {
            // Check if this button has a corresponding state variable to announce
            if (ButtonStateMapping.ContainsKey(eventName))
            {
                string stateVarKey = ButtonStateMapping[eventName];

                // Request the state after a short delay to allow the sim to update
                Timer stateTimer = new Timer();
                stateTimer.Interval = 300; // 300ms delay
                stateTimer.Tick += (s, e) =>
                {
                    stateTimer.Stop();
                    stateTimer.Dispose();

                    // Request the current state and announce it
                    if (SimVarDefinitions.Variables.ContainsKey(stateVarKey))
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
            if (SimVarDefinitions.PanelDisplayVariables.ContainsKey(currentPanel))
            {
                var displayVars = SimVarDefinitions.PanelDisplayVariables[currentPanel];
                List<string> values = new List<string>();

                foreach (var varKey in displayVars)
                {
                    if (SimVarDefinitions.Variables.ContainsKey(varKey))
                    {
                        var varDef = SimVarDefinitions.Variables[varKey];

                        if (displayValues.ContainsKey(varKey))
                        {
                            double value = displayValues[varKey];
                            string displayValue;

                            // Check if we have value descriptions (like Off/Aligning/Aligned)
                            if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsKey(value))
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

                displayBox.Text = string.Join("\r\n", values);
            }
        }


        /// <summary>
        /// Converts a decimal squawk code to BCD (Binary Coded Decimal) format for XPNDR_SET event.
        /// Each digit of the squawk code is encoded in 4 bits.
        /// Example: 0422 -> (0*4096) + (4*256) + (2*16) + (2*1) = 1058
        /// </summary>
        private uint ConvertSquawkToBCD(string squawkCode, out string errorMessage)
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

        private void OnSimVarValueChanged(object sender, SimVarChangeEventArgs e)
        {
            if (!e.IsInitialValue && !updatingFromSim)
            {
                announcer.Announce(e.Description);
            }
        }

        private void OnHotkeyTriggered(object sender, HotkeyEventArgs e)
        {
            switch (e.Action)
            {
                case HotkeyAction.ReadHeading:
                    simConnectManager.RequestFCUHeadingWithStatus();
                    break;
                case HotkeyAction.ReadSpeed:
                    simConnectManager.RequestFCUSpeedWithStatus();
                    break;
                case HotkeyAction.ReadAltitude:
                    simConnectManager.RequestFCUAltitudeWithStatus();
                    break;
                case HotkeyAction.ReadFCUVerticalSpeedFPA:
                    simConnectManager.RequestFCUVerticalSpeedFPA();
                    break;
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
                case HotkeyAction.ReadVerticalSpeed:
                    simConnectManager.RequestVerticalSpeed();
                    break;
                case HotkeyAction.ReadFuelQuantity:
                    simConnectManager.RequestFuelQuantity();
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
                case HotkeyAction.SimBriefBriefing:
                    OpenSimBriefBriefing();
                    break;
                case HotkeyAction.SelectDestinationRunway:
                    ShowDestinationRunwayDialog();
                    break;
                case HotkeyAction.ReadILSGuidance:
                    RequestILSGuidance();
                    break;
                case HotkeyAction.ReadWindInfo:
                    RequestWindInfo();
                    break;
                case HotkeyAction.ShowMETARReport:
                    ShowMETARReportDialog();
                    break;
                case HotkeyAction.ShowPFD:
                    ShowPFDDialog();
                    break;
                case HotkeyAction.ShowChecklist:
                    ShowChecklistDialog();
                    break;
                case HotkeyAction.ShowElectronicFlightBag:
                    ShowElectronicFlightBagDialog();
                    break;
                case HotkeyAction.ShowNavigationDisplay:
                    ShowNavigationDisplayDialog();
                    break;
                case HotkeyAction.ShowECAM:
                    ShowECAMDialog();
                    break;
                case HotkeyAction.ShowStatusPage:
                    ShowStatusDialog();
                    break;
                case HotkeyAction.ToggleTakeoffAssist:
                    ToggleTakeoffAssist();
                    break;
                case HotkeyAction.ToggleECAMMonitoring:
                    ToggleECAMMonitoring();
                    break;
                case HotkeyAction.ReadWaypointInfo:
                    simConnectManager.RequestWaypointInfo();
                    break;
                case HotkeyAction.ToggleVisualApproach:
                    ToggleVisualApproachMonitoring();
                    break;
                case HotkeyAction.ToggleAutopilot1:
                    simConnectManager.SendEvent("A32NX.FCU_AP_1_PUSH");
                    break;
                case HotkeyAction.ToggleApproachMode:
                    simConnectManager.SendEvent("A32NX.FCU_APPR_PUSH");
                    break;
                case HotkeyAction.ReadApproachCapability:
                    RequestApproachCapability();
                    break;
                case HotkeyAction.FCUHeadingPush:
                    simConnectManager.SendEvent("A32NX.FCU_HDG_PUSH");
                    HandleButtonStateAnnouncement("A32NX.FCU_HDG_PUSH");
                    break;
                case HotkeyAction.FCUHeadingPull:
                    simConnectManager.SendEvent("A32NX.FCU_HDG_PULL");
                    HandleButtonStateAnnouncement("A32NX.FCU_HDG_PULL");
                    break;
                case HotkeyAction.FCUAltitudePush:
                    simConnectManager.SendEvent("A32NX.FCU_ALT_PUSH");
                    HandleButtonStateAnnouncement("A32NX.FCU_ALT_PUSH");
                    break;
                case HotkeyAction.FCUAltitudePull:
                    simConnectManager.SendEvent("A32NX.FCU_ALT_PULL");
                    HandleButtonStateAnnouncement("A32NX.FCU_ALT_PULL");
                    break;
                case HotkeyAction.FCUSpeedPush:
                    simConnectManager.SendEvent("A32NX.FCU_SPD_PUSH");
                    HandleButtonStateAnnouncement("A32NX.FCU_SPD_PUSH");
                    break;
                case HotkeyAction.FCUSpeedPull:
                    simConnectManager.SendEvent("A32NX.FCU_SPD_PULL");
                    HandleButtonStateAnnouncement("A32NX.FCU_SPD_PULL");
                    break;
                case HotkeyAction.FCUVSPush:
                    simConnectManager.SendEvent("A32NX.FCU_VS_PUSH");
                    HandleButtonStateAnnouncement("A32NX.FCU_VS_PUSH");
                    break;
                case HotkeyAction.FCUVSPull:
                    simConnectManager.SendEvent("A32NX.FCU_VS_PULL");
                    HandleButtonStateAnnouncement("A32NX.FCU_VS_PULL");
                    break;
                case HotkeyAction.FCUSetHeading:
                    ShowFCUHeadingInputDialog();
                    break;
                case HotkeyAction.FCUSetSpeed:
                    ShowFCUSpeedInputDialog();
                    break;
                case HotkeyAction.FCUSetAltitude:
                    ShowFCUAltitudeInputDialog();
                    break;
                case HotkeyAction.FCUSetVS:
                    ShowFCUVSInputDialog();
                    break;
                case HotkeyAction.ToggleAutopilot2:
                    simConnectManager.SendEvent("A32NX.FCU_AP_2_PUSH");
                    break;
                case HotkeyAction.ReadSpeedGD:
                    simConnectManager.RequestSpeedGD();
                    break;
                case HotkeyAction.ReadSpeedS:
                    simConnectManager.RequestSpeedS();
                    break;
                case HotkeyAction.ReadSpeedF:
                    simConnectManager.RequestSpeedF();
                    break;
                case HotkeyAction.ReadSpeedVFE:
                    simConnectManager.RequestSpeedVFE();
                    break;
                case HotkeyAction.ReadSpeedVLS:
                    simConnectManager.RequestSpeedVLS();
                    break;
                case HotkeyAction.ReadSpeedVS:
                    simConnectManager.RequestSpeedVS();
                    break;
            }
        }

        private void RequestApproachCapability()
        {
            if (simConnectManager != null)
            {
                // Get cached value immediately
                var cachedValue = simConnectManager.GetCachedVariableValue("A32NX_APPROACH_CAPABILITY");
                if (cachedValue.HasValue)
                {
                    var varDef = SimVarDefinitions.Variables["A32NX_APPROACH_CAPABILITY"];
                    string capability = varDef.ValueDescriptions.ContainsKey(cachedValue.Value)
                        ? varDef.ValueDescriptions[cachedValue.Value]
                        : cachedValue.Value.ToString();
                    announcer.AnnounceImmediate($"Approach Capability: {capability}");
                }
                else
                {
                    // Request fresh value if not cached
                    simConnectManager.RequestVariable("A32NX_APPROACH_CAPABILITY");
                    announcer.AnnounceImmediate("Approach capability not available");
                }
            }
        }

        private void OnOutputHotkeyModeChanged(object sender, bool active)
        {
            // Use the announcer properly
            if (active)
            {
                announcer.AnnounceImmediate("output");
            }
        }

        private void OnInputHotkeyModeChanged(object sender, bool active)
        {
            if (active)
            {
                announcer.AnnounceImmediate("input");
            }
        }

        /// <summary>
        /// Validates that the selected database matches the running simulator version
        /// </summary>
        /// <returns>True if validation passes or user chooses to continue anyway, false otherwise</returns>
        private bool ValidateDatabaseSimulatorMatch()
        {
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
            if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
            {
                announcer.AnnounceImmediate("Airport database not found. Configure database from File menu first.");
                return;
            }

            // Validate database matches simulator
            if (!ValidateDatabaseSimulatorMatch())
                return;

            var dialog = new GateTeleportForm(airportDataProvider, announcer);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                if (dialog.SelectedParkingSpot != null && dialog.SelectedAirport != null)
                {
                    simConnectManager.TeleportToParkingSpot(dialog.SelectedParkingSpot, dialog.SelectedAirport);
                }
            }
        }

        private void ShowFCUHeadingInputDialog()
        {
            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            var validator = new Func<string, (bool isValid, string message)>((input) =>
            {
                if (double.TryParse(input, out double value))
                {
                    if (value >= 0 && value <= 360)
                        return (true, "");
                    else
                        return (false, "Heading must be between 0 and 360 degrees");
                }
                return (false, "Invalid number format");
            });

            var dialog = new FCUInputForm("Set Heading", "Heading", "0-360 degrees", announcer, validator);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValidInput)
            {
                if (double.TryParse(dialog.InputValue, out double value))
                {
                    simConnectManager.SendEvent("A32NX.FCU_HDG_SET", (uint)value);
                    announcer.AnnounceImmediate($"Heading set to {value}");
                }
            }
        }

        private void ShowFCUSpeedInputDialog()
        {
            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            var validator = new Func<string, (bool isValid, string message)>((input) =>
            {
                if (double.TryParse(input, out double value))
                {
                    // Check if it's a Mach number (0.10-0.99) or knots (100-399)
                    if ((value >= 0.10 && value <= 0.99) || (value >= 100 && value <= 399))
                        return (true, "");
                    else
                        return (false, "Speed must be 100-399 knots or 0.10-0.99 Mach");
                }
                return (false, "Invalid number format");
            });

            var dialog = new FCUInputForm("Set Speed", "Speed", "100-399 knots or 0.10-0.99 Mach", announcer, validator);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValidInput)
            {
                if (double.TryParse(dialog.InputValue, out double value))
                {
                    // Determine if Mach (< 1.0) or knots (>= 100)
                    // Mach numbers need to be multiplied by 100, knots are sent as-is
                    uint valueToSend = value < 1.0 ? (uint)(value * 100) : (uint)value;
                    simConnectManager.SendEvent("A32NX.FCU_SPD_SET", valueToSend);
                    announcer.AnnounceImmediate($"Speed set to {value}");
                }
            }
        }

        private void ShowFCUAltitudeInputDialog()
        {
            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            var validator = new Func<string, (bool isValid, string message)>((input) =>
            {
                if (double.TryParse(input, out double value))
                {
                    if (value >= 100 && value <= 49000)
                        return (true, "");
                    else
                        return (false, "Altitude must be between 100 and 49000 feet");
                }
                return (false, "Invalid number format");
            });

            var dialog = new FCUInputForm("Set Altitude", "Altitude", "100-49000 feet", announcer, validator);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValidInput)
            {
                if (double.TryParse(dialog.InputValue, out double value))
                {
                    // FCU_ALT_SET requires values to be multiples of 100 feet
                    // Round to nearest 100 to ensure compatibility
                    uint roundedValue = (uint)(Math.Round(value / 100) * 100);

                    // Set FCU altitude increment mode to 100ft before setting altitude
                    // This ensures intermediate values like 2500, 3500, 31500 work correctly
                    simConnectManager.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
                    System.Threading.Thread.Sleep(50); // Brief delay for mode to activate

                    simConnectManager.SendEvent("A32NX.FCU_ALT_SET", roundedValue);
                    announcer.AnnounceImmediate($"Altitude set to {roundedValue}");
                }
            }
        }

        private void ShowFCUVSInputDialog()
        {
            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            // Check current TRK/FPA mode to validate input range
            bool isFpaMode = currentSimVarValues.ContainsKey("A32NX_TRK_FPA_MODE_ACTIVE") &&
                           currentSimVarValues["A32NX_TRK_FPA_MODE_ACTIVE"] == 1;

            string rangeText = isFpaMode ? "-9.9 to 9.9 degrees" : "-6000 to 6000 ft/min";
            string paramType = isFpaMode ? "FPA" : "Vertical Speed";

            var validator = new Func<string, (bool isValid, string message)>((input) =>
            {
                if (double.TryParse(input, out double value))
                {
                    if (isFpaMode)
                    {
                        if (value >= -9.9 && value <= 9.9)
                            return (true, "");
                        else
                            return (false, "FPA must be between -9.9 and 9.9 degrees");
                    }
                    else
                    {
                        if (value >= -6000 && value <= 6000)
                            return (true, "");
                        else
                            return (false, "Vertical Speed must be between -6000 and 6000 ft/min");
                    }
                }
                return (false, "Invalid number format");
            });

            var dialog = new FCUInputForm($"Set {paramType}", paramType, rangeText, announcer, validator);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValidInput)
            {
                if (double.TryParse(dialog.InputValue, out double value))
                {
                    simConnectManager.SendEvent("A32NX.FCU_VS_SET", (uint)(value * (isFpaMode ? 10 : 1)));
                    announcer.AnnounceImmediate($"{paramType} set to {value}");
                }
            }
        }

        private void ShowLocationInfoDialog()
        {
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


        private void OpenSimBriefBriefing()
        {
            try
            {
                announcer.AnnounceImmediate("Opening your SimBrief briefing");
                Process.Start("https://dispatch.simbrief.com/briefing/latest");
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
                    announcer.AnnounceImmediate($"Destination runway set: {dialog.SelectedAirport.ICAO} Runway {dialog.SelectedRunway.RunwayID}");
                }
            }
        }

        private void ShowMETARReportDialog()
        {
            // Ensure output hotkey mode is deactivated before showing modal dialog
            hotkeyManager.ExitOutputHotkeyMode();

            var dialog = new METARReportForm(announcer);
            dialog.ShowDialog(this);
        }

        private void ShowChecklistDialog()
        {
            // Ensure output hotkey mode is deactivated before showing dialog
            hotkeyManager.ExitOutputHotkeyMode();

            // Create form if it doesn't exist or has been disposed
            if (checklistForm == null || checklistForm.IsDisposed)
            {
                checklistForm = new ChecklistForm(announcer);
            }

            // Show the form (reuses same instance to preserve checkbox states)
            checklistForm.ShowForm();
        }

        private void ShowElectronicFlightBagDialog()
        {
            // Ensure output hotkey mode is deactivated before showing dialog
            hotkeyManager.ExitOutputHotkeyMode();

            // Create form if it doesn't exist or has been disposed
            if (electronicFlightBagForm == null || electronicFlightBagForm.IsDisposed)
            {
                var settings = FBWBA.Settings.SettingsManager.Current;
                electronicFlightBagForm = new ElectronicFlightBagForm(flightPlanManager, simConnectManager, announcer, settings.SimbriefUsername ?? "");
            }

            // Show the form (reuses same instance to preserve flight plan data)
            electronicFlightBagForm.Show();
            electronicFlightBagForm.BringToFront();
        }

        private void ShowPFDDialog()
        {
            // Ensure output hotkey mode is deactivated before showing window
            hotkeyManager.ExitOutputHotkeyMode();

            var dialog = new PFDForm(announcer, simConnectManager);
            dialog.Show();
        }

        private void ShowNavigationDisplayDialog()
        {
            // Ensure output hotkey mode is deactivated before showing window
            hotkeyManager.ExitOutputHotkeyMode();

            var dialog = new NavigationDisplayForm(announcer, simConnectManager);
            dialog.Show();
        }

        private void ShowECAMDialog()
        {
            // Ensure output hotkey mode is deactivated before showing window
            hotkeyManager.ExitOutputHotkeyMode();

            var dialog = new ECAMDisplayForm(announcer, simConnectManager);
            dialog.Show();
        }

        private void ShowStatusDialog()
        {
            // Ensure output hotkey mode is deactivated before showing window
            hotkeyManager.ExitOutputHotkeyMode();

            var dialog = new StatusDisplayForm(announcer, simConnectManager);
            dialog.Show();
        }

        private void RequestILSGuidance()
        {
            if (!simConnectManager.HasDestinationRunway())
            {
                announcer.AnnounceImmediate("No destination runway selected. Press bracket T to select a destination runway first.");
                return;
            }

            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            // Request current aircraft position and calculate ILS guidance
            // This will be handled asynchronously through the SimConnect event system
            simConnectManager.RequestILSGuidance();
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

                    // Get destination wind from VATSIM API
                    var destinationWindData = await VATSIMService.GetAirportWindAsync(destinationAirport.ICAO);
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

        private string FormatWindData(FBWBA.SimConnect.SimConnectManager.WindData windData)
        {
            // Convert direction to integer and round speed to nearest knot
            int direction = (int)Math.Round(windData.Direction);
            int speed = (int)Math.Round(windData.Speed);

            if (speed == 0)
                return "calm";

            // Format as "direction at speed"
            return $"{direction:000} at {speed}";
        }

        private void ToggleVisualApproachMonitoring()
        {
            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            simConnectManager.ToggleVisualApproachMonitoring();
        }

        private void ToggleTakeoffAssist()
        {
            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            // Request current heading for takeoff assist toggle
            simConnectManager.RequestHeadingForTakeoffAssist();
        }

        private void ToggleECAMMonitoring()
        {
            if (!simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            bool isEnabled = simConnectManager.ToggleECAMMonitoring();
            string statusMessage = isEnabled ? "E W D monitoring enabled" : "E W D monitoring disabled";
            announcer.AnnounceImmediate(statusMessage);
        }

        private void OnTakeoffAssistActiveChanged(object sender, bool isActive)
        {
            if (isActive)
            {
                // Start monitoring pitch and heading
                simConnectManager.StartTakeoffAssistMonitoring();
            }
            else
            {
                // Stop monitoring
                simConnectManager.StopTakeoffAssistMonitoring();
            }
        }

        private void DatabaseSettingsMenuItem_Click(object sender, EventArgs e)
        {
            using (var settingsForm = new DatabaseSettingsForm(announcer))
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

        private void AnnouncementSettingsMenuItem_Click(object sender, EventArgs e)
        {
            var currentMode = announcer.GetAnnouncementMode();
            using (var settingsForm = new AnnouncementSettingsForm(currentMode))
            {
                if (settingsForm.ShowDialog(this) == DialogResult.OK)
                {
                    var newMode = settingsForm.SelectedMode;
                    announcer.SetAnnouncementMode(newMode);

                    string modeText = newMode == AnnouncementMode.ScreenReader ? "screen reader" : "SAPI";
                    statusLabel.Text = $"Announcement mode changed to {modeText}";
                    announcer.Announce($"Announcement mode changed to {modeText}");

                    // Diagnostic test disabled to prevent test speech
                    // Uncomment if you need to troubleshoot:
                    // System.Diagnostics.Debug.WriteLine("[MainForm] Running screen reader diagnostic test after mode change");
                    // announcer.TestScreenReaderConnection();
                }
            }
        }

        private void GeoNamesSettingsMenuItem_Click(object sender, EventArgs e)
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

        private void SimBriefSettingsMenuItem_Click(object sender, EventArgs e)
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

        private void HotkeyListMenuItem_Click(object sender, EventArgs e)
        {
            using (var hotkeyListForm = new HotkeyListForm())
            {
                hotkeyListForm.ShowDialog(this);
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

            // Reload database provider based on current settings
            airportDataProvider = DatabaseSelector.SelectProvider();

            // Recreate flight plan manager with new navigation database path
            var settings = FBWBA.Settings.SettingsManager.Current;
            string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
            flightPlanManager = new FBWBA.Navigation.FlightPlanManager(navigationDatabasePath, airportDataProvider);

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

                var settings = FBWBA.Settings.SettingsManager.Current;
                string currentDbSetting = settings.SimulatorVersion ?? "FS2020";

                // Check if database setting matches detected simulator
                bool needsSwitch = false;
                string targetVersion = null;

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

                if (needsSwitch)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainForm] Auto-switching database from {currentDbSetting} to {targetVersion}");

                    // Update settings
                    settings.SimulatorVersion = targetVersion;
                    FBWBA.Settings.SettingsManager.Save(settings);

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

        private async void UpdateApplicationMenuItem_Click(object sender, EventArgs e)
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

        private void AboutMenuItem_Click(object sender, EventArgs e)
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
            
            base.WndProc(ref m);
        }

        private void SectionsListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sectionsListBox.SelectedItem == null) return;

            string newSection = sectionsListBox.SelectedItem.ToString();
            if (newSection == currentSection) return;

            currentSection = newSection;
            
            // Clear panels without triggering events
            panelsListBox.SelectedIndexChanged -= PanelsListBox_SelectedIndexChanged;
            panelsListBox.Items.Clear();
            currentPanel = "";
            
            if (SimVarDefinitions.PanelStructure.ContainsKey(currentSection))
            {
                foreach (var panel in SimVarDefinitions.PanelStructure[currentSection])
                {
                    panelsListBox.Items.Add(panel);
                }
            }
            
            // Re-enable event
            panelsListBox.SelectedIndexChanged += PanelsListBox_SelectedIndexChanged;
        }

        private void PanelsListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (panelsListBox.SelectedItem == null) return;

            string newPanel = panelsListBox.SelectedItem.ToString();
            if (newPanel == currentPanel) return;

            currentPanel = newPanel;

            // Force refresh OnDemand variables for Exterior Lighting panel
            if (newPanel == "Exterior Lighting" && simConnectManager != null && simConnectManager.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Refreshing OnDemand variables for Exterior Lighting panel");
                simConnectManager.RequestPanelVariables("Exterior Lighting", "Exterior Lighting panel opened");
            }

            // Force refresh OnDemand variables for Signs panel
            if (newPanel == "Signs" && simConnectManager != null && simConnectManager.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Refreshing OnDemand variables for Signs panel");
                simConnectManager.RequestPanelVariables("Signs", "Signs panel opened");
            }

            // Force refresh OnDemand variables for FCU panel
            if (newPanel == "FCU" && simConnectManager != null && simConnectManager.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Refreshing OnDemand variables for FCU panel");
                simConnectManager.RequestPanelVariables("FCU", "FCU panel opened");
            }

            // Clear and reload controls
            controlsContainer.Controls.Clear();
            currentControls.Clear();

            if (!SimVarDefinitions.PanelControls.ContainsKey(currentPanel))
                return;

            // Create a TableLayoutPanel for better layout
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 2;
            layout.RowCount = 0;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            layout.AutoSize = true;
            layout.Location = new Point(10, 10);

            foreach (var varKey in SimVarDefinitions.PanelControls[currentPanel])
            {
                if (!SimVarDefinitions.Variables.ContainsKey(varKey))
                    continue;

                var varDef = SimVarDefinitions.Variables[varKey];
                
                // Add a new row
                int rowIndex = layout.RowCount++;
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));

                // Create label
                Label label = new Label();
                label.Text = varDef.DisplayName + ":";
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.AutoSize = false;
                label.Size = new Size(140, 25);
                layout.Controls.Add(label, 0, rowIndex);

                // Create control based on type
                if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 1)
                {
                    // Special handling for APU Start - create as button instead of combo box
                    if (varKey == "A32NX_OVHD_APU_START_PB_IS_ON")
                    {
                        Button apuButton = new Button();
                        apuButton.Text = "APU Start";
                        apuButton.Size = new Size(240, 25);
                        apuButton.Name = varKey;
                        apuButton.AccessibleName = "APU Start";
                        apuButton.AccessibleDescription = "Press to start APU";

                        // Handle button click - send value 1 to start APU
                        apuButton.Click += (s2, e2) =>
                        {
                            simConnectManager.SetLVar(varDef.Name, 1);
                            currentSimVarValues[varKey] = 1;
                            announcer.Announce("APU Start pressed");

                            // Request related variables to refresh states efficiently
                            RequestRelatedVariables(varKey, "User pressed APU Start");
                        };

                        layout.Controls.Add(apuButton, 1, rowIndex);
                        currentControls[varKey] = apuButton;
                    }
                    // Special handling for ENGINE_MODE_SELECTOR
                    else if (varKey == "ENGINE_MODE_SELECTOR")
                    {
                        ComboBox combo = new ComboBox();
                        combo.DropDownStyle = ComboBoxStyle.DropDownList;
                        combo.Size = new Size(240, 25);
                        combo.Name = varKey;
                        combo.AccessibleName = varDef.DisplayName;
                        combo.AccessibleDescription = "Press Alt+Down to open, arrows to navigate";
                        
                        // Add items
                        combo.Items.Add("CRANK");
                        combo.Items.Add("NORM");
                        combo.Items.Add("IGN");
                        
                        // Set initial value from sim if we have it
                        if (currentSimVarValues.ContainsKey("TURB ENG IGNITION SWITCH EX1:1"))
                        {
                            double currentValue = currentSimVarValues["TURB ENG IGNITION SWITCH EX1:1"];
                            combo.SelectedIndex = (int)currentValue;
                        }
                        else
                        {
                            combo.SelectedIndex = 1; // Default to NORM
                        }
                        
                        // Auto-open dropdown on first arrow key press
                        bool firstArrowPress = true;
                        combo.PreviewKeyDown += (s2, e2) =>
                        {
                            ComboBox cb = s2 as ComboBox;
                            if ((e2.KeyCode == Keys.Up || e2.KeyCode == Keys.Down))
                            {
                                if (!cb.DroppedDown && firstArrowPress)
                                {
                                    firstArrowPress = false;
                                    cb.DroppedDown = true;
                                    e2.IsInputKey = true;
                                }
                            }
                            else if (e2.KeyCode == Keys.Tab)
                            {
                                firstArrowPress = true;
                            }
                        };
                        
                        combo.Leave += (s2, e2) =>
                        {
                            firstArrowPress = true;
                            ComboBox cb = s2 as ComboBox;
                            if (cb.DroppedDown)
                                cb.DroppedDown = false;
                        };
                        
                        // Handle selection change - set both engines
                        combo.SelectedIndexChanged += (s2, e2) =>
                        {
                            if (!updatingFromSim && combo.SelectedIndex >= 0)
                            {
                                uint mode = (uint)combo.SelectedIndex;
                                // Set both engines to the same mode
                                simConnectManager.SendEvent("TURBINE_IGNITION_SWITCH_SET1", mode);
                                simConnectManager.SendEvent("TURBINE_IGNITION_SWITCH_SET2", mode);
                                currentSimVarValues["TURB ENG IGNITION SWITCH EX1:1"] = mode;
                            }
                        };
                        
                        layout.Controls.Add(combo, 1, rowIndex);
                        currentControls[varKey] = combo;
                    }
                    // Special handling for Lighting controls
                    else if (varKey == "LIGHTING_LANDING_1" || varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3" ||
                             varKey == "LIGHTING_STROBE_0" || varKey == "LIGHT BEACON" || varKey == "LIGHT WING" ||
                             varKey == "LIGHT NAV" || varKey == "LIGHT LOGO" ||
                             varKey == "CIRCUIT_SWITCH_ON:21" || varKey == "CIRCUIT_SWITCH_ON:22")
                    {
                        ComboBox combo = new ComboBox();
                        combo.DropDownStyle = ComboBoxStyle.DropDownList;
                        combo.Size = new Size(240, 25);
                        combo.Name = varKey;
                        combo.AccessibleName = varDef.DisplayName;
                        combo.AccessibleDescription = "Press Alt+Down to open, arrows to navigate";

                        // Add items in order
                        var sortedValues = varDef.ValueDescriptions.OrderBy(x => x.Key).ToList();
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

                        // Auto-open dropdown on first arrow key press for better NVDA support
                        bool firstArrowPress = true;
                        combo.PreviewKeyDown += (s2, e2) =>
                        {
                            ComboBox cb = s2 as ComboBox;

                            // If arrow key pressed and dropdown not open, open it
                            if ((e2.KeyCode == Keys.Up || e2.KeyCode == Keys.Down))
                            {
                                if (!cb.DroppedDown && firstArrowPress)
                                {
                                    firstArrowPress = false;
                                    cb.DroppedDown = true;
                                    e2.IsInputKey = true; // Process this key
                                }
                            }
                            else if (e2.KeyCode == Keys.Tab)
                            {
                                firstArrowPress = true; // Reset when leaving
                            }
                        };

                        // Reset flag when focus leaves
                        combo.Leave += (s2, e2) =>
                        {
                            firstArrowPress = true;
                            ComboBox cb = s2 as ComboBox;
                            if (cb.DroppedDown)
                                cb.DroppedDown = false;
                        };

                        // Handle selection change - send multiple events
                        combo.SelectedIndexChanged += (s2, e2) =>
                        {
                            if (!updatingFromSim && combo.SelectedIndex >= 0)
                            {
                                var selectedValue = sortedValues[combo.SelectedIndex].Key;

                                // Send the main LVar
                                simConnectManager.SetLVar(varKey, selectedValue);
                                currentSimVarValues[varKey] = selectedValue;

                                // Send additional events based on the control and value
                                if (varKey == "LIGHTING_LANDING_1") // Nose Light
                                {
                                    if (selectedValue == 2) // Off
                                    {
                                        simConnectManager.SendEvent("LANDING_LIGHTS_OFF", 1);
                                        simConnectManager.SendEvent("LIGHT_TAXI", 0);
                                    }
                                    else if (selectedValue == 1) // Taxi
                                    {
                                        simConnectManager.SendEvent("LANDING_LIGHTS_ON", 1);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_20", 1);
                                        simConnectManager.SendEvent("LIGHT_TAXI", 1);
                                    }
                                    else if (selectedValue == 0) // T.O.
                                    {
                                        simConnectManager.SendEvent("LANDING_LIGHTS_ON", 1);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_17", 1);
                                        simConnectManager.SendEvent("LIGHT_TAXI", 0);
                                    }
                                }
                                else if (varKey == "LIGHTING_LANDING_2") // Left Landing Light
                                {
                                    if (selectedValue == 2) // Retract
                                    {
                                        simConnectManager.SendEvent("LANDING_2_RETRACTED", 1);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_18", 0);
                                    }
                                    else if (selectedValue == 1) // Off
                                    {
                                        simConnectManager.SendEvent("LANDING_2_RETRACTED", 0);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_18", 0);
                                    }
                                    else if (selectedValue == 0) // On
                                    {
                                        simConnectManager.SendEvent("LANDING_2_RETRACTED", 0);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_18", 1);
                                    }
                                }
                                else if (varKey == "LIGHTING_LANDING_3") // Right Landing Light
                                {
                                    if (selectedValue == 2) // Retract
                                    {
                                        simConnectManager.SendEvent("LANDING_3_RETRACTED", 1);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_19", 0);
                                    }
                                    else if (selectedValue == 1) // Off
                                    {
                                        simConnectManager.SendEvent("LANDING_3_RETRACTED", 0);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_19", 0);
                                    }
                                    else if (selectedValue == 0) // On
                                    {
                                        simConnectManager.SendEvent("LANDING_3_RETRACTED", 0);
                                        simConnectManager.SendEvent("CIRCUIT_SWITCH_ON_19", 1);
                                    }
                                }
                                else if (varKey == "LIGHTING_STROBE_0") // Strobe Lights
                                {
                                    if (selectedValue == 2) // Off
                                    {
                                        simConnectManager.SendEvent("STROBES_OFF", 0);
                                        simConnectManager.SetLVar("STROBE_0_AUTO", 0);
                                        simConnectManager.SetLVar("LIGHT STROBE", 0);
                                        simConnectManager.SetLVar("LIGHTING_STROBE_0", 2);
                                    }
                                    else if (selectedValue == 0) // On
                                    {
                                        simConnectManager.SendEvent("STROBES_ON", 0);
                                        simConnectManager.SetLVar("LIGHT STROBE", 1);
                                        simConnectManager.SetLVar("STROBE_0_AUTO", 0);
                                        simConnectManager.SetLVar("LIGHTING_STROBE_0", 0);
                                    }
                                    else if (selectedValue == 1) // Auto
                                    {
                                        simConnectManager.SetLVar("STROBE_0_AUTO", 1);
                                        simConnectManager.SetLVar("LIGHTING_STROBE_0", 1);
                                    }
                                }
                                else if (varKey == "LIGHT BEACON") // Beacon Light
                                {
                                    if (selectedValue == 0) // Off
                                    {
                                        simConnectManager.SendEvent("BEACON_LIGHTS_SET", 0);
                                    }
                                    else if (selectedValue == 1) // On
                                    {
                                        simConnectManager.SendEvent("BEACON_LIGHTS_SET", 1);
                                    }
                                }
                                else if (varKey == "LIGHT WING") // Wing Lights
                                {
                                    if (selectedValue == 0) // Off
                                    {
                                        simConnectManager.SendEvent("WING_LIGHTS_SET", 0);
                                    }
                                    else if (selectedValue == 1) // On
                                    {
                                        simConnectManager.SendEvent("WING_LIGHTS_SET", 1);
                                    }
                                }
                                else if (varKey == "LIGHT NAV") // Nav Lights
                                {
                                    // Nav and Logo lights are combined in real aircraft
                                    // Control both when Nav light is changed
                                    if (selectedValue == 0) // Off
                                    {
                                        simConnectManager.SendEvent("NAV_LIGHTS_SET", 0);
                                        simConnectManager.SendEvent("LOGO_LIGHTS_SET", 0);
                                    }
                                    else if (selectedValue == 1) // On
                                    {
                                        simConnectManager.SendEvent("NAV_LIGHTS_SET", 1);
                                        simConnectManager.SendEvent("LOGO_LIGHTS_SET", 1);
                                    }
                                }
                                else if (varKey == "LIGHT LOGO") // Logo Lights
                                {
                                    // Logo lights are controlled with Nav lights in real aircraft
                                    // Control both when Logo light is changed
                                    if (selectedValue == 0) // Off
                                    {
                                        simConnectManager.SendEvent("NAV_LIGHTS_SET", 0);
                                        simConnectManager.SendEvent("LOGO_LIGHTS_SET", 0);
                                    }
                                    else if (selectedValue == 1) // On
                                    {
                                        simConnectManager.SendEvent("NAV_LIGHTS_SET", 1);
                                        simConnectManager.SendEvent("LOGO_LIGHTS_SET", 1);
                                    }
                                }
                                else if (varKey == "CIRCUIT_SWITCH_ON:21") // Left RWY Turn Off Light
                                {
                                    // Write directly to the SimVar (as per FBW documentation)
                                    simConnectManager.SetSimVar("CIRCUIT SWITCH ON:21", selectedValue, "bool");
                                }
                                else if (varKey == "CIRCUIT_SWITCH_ON:22") // Right RWY Turn Off Light
                                {
                                    // Write directly to the SimVar (as per FBW documentation)
                                    simConnectManager.SetSimVar("CIRCUIT SWITCH ON:22", selectedValue, "bool");
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
                        combo.AccessibleDescription = $"Press Alt+Down to open, arrows to navigate";
                        
                        // Add items in order
                        var sortedValues = varDef.ValueDescriptions.OrderBy(x => x.Key).ToList();
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

                        // Auto-open dropdown on first arrow key press for better NVDA support
                        bool firstArrowPress = true;
                        combo.PreviewKeyDown += (s2, e2) =>
                        {
                            ComboBox cb = s2 as ComboBox;
                            
                            // If arrow key pressed and dropdown not open, open it
                            if ((e2.KeyCode == Keys.Up || e2.KeyCode == Keys.Down))
                            {
                                if (!cb.DroppedDown && firstArrowPress)
                                {
                                    firstArrowPress = false;
                                    cb.DroppedDown = true;
                                    e2.IsInputKey = true; // Process this key
                                }
                            }
                            else if (e2.KeyCode == Keys.Tab)
                            {
                                firstArrowPress = true; // Reset when leaving
                            }
                        };

                        // Reset flag when focus leaves
                        combo.Leave += (s2, e2) =>
                        {
                            firstArrowPress = true;
                            ComboBox cb = s2 as ComboBox;
                            if (cb.DroppedDown)
                                cb.DroppedDown = false;
                        };
                        
                        // Handle selection change
                        combo.SelectedIndexChanged += (s2, e2) =>
                        {
                            if (!updatingFromSim && combo.SelectedIndex >= 0)
                            {
                                var selectedValue = sortedValues[combo.SelectedIndex].Key;

                                // Special handling for autobrakes - try multiple approaches
                                // NOTE: Autobrakes may only be settable under specific flight conditions:
                                // - Aircraft on ground
                                // - Engines running
                                // - During approach/landing phase
                                // - Not during taxi or with parking brake set
                                // TODO: Test this during different flight phases to determine exact requirements
                                if (varKey == "AUTOBRAKE_MODE")
                                {
                                    // Try the write LVar first (most likely to work)
                                    simConnectManager.SetLVar("A32NX_AUTOBRAKES_ARMED_MODE_SET", selectedValue);

                                    // Also try the event as backup
                                    simConnectManager.SendEvent("A32NX.AUTOBRAKE_SET", (uint)selectedValue);

                                    currentSimVarValues[varKey] = selectedValue;

                                    // Request related variables to refresh states efficiently
                                    RequestRelatedVariables(varKey, $"User changed {varDef.DisplayName}");
                                }
                                else if (varKey == "CABIN SEATBELTS ALERT SWITCH") // Seat Belts Signs
                                {
                                    // Send the toggle event to change the state
                                    simConnectManager.SendEvent("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 0);

                                    // Update our stored value to match the selection
                                    currentSimVarValues[varKey] = selectedValue;

                                    // Request related variables to refresh after the toggle
                                    RequestRelatedVariables(varKey, $"User changed {varDef.DisplayName}");
                                }
                                else if (varDef.Type == SimVarType.LVar)
                                {
                                    simConnectManager.SetLVar(varDef.Name, selectedValue);
                                    currentSimVarValues[varKey] = selectedValue;

                                    // Request related variables to refresh states efficiently
                                    RequestRelatedVariables(varKey, $"User changed {varDef.DisplayName}");
                                }
                            }
                        };
                        
                        layout.Controls.Add(combo, 1, rowIndex);
                        currentControls[varKey] = combo;
                        
                        // Request current value for this control if not automatically monitored by Important tier
                        if (varDef.Type == SimVarType.LVar && varDef.UpdateFrequency != UpdateFrequency.Continuous)
                        {
                            simConnectManager.RequestSpecificLVar(varKey, varDef.Name);
                        }
                    }
                }
                else if (varKey.Contains("_SET") && !varKey.StartsWith("A32NX.AUTOBRAKE_"))
                {
                    // Panel for TextBox and Button
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
                        // Special handling for transponder code (requires BCD encoding)
                        if (varKey == "TRANSPONDER_CODE_SET")
                        {
                            string squawkCode = textBox.Text.Trim();

                            // Pad with leading zeros if needed (e.g., "422" -> "0422")
                            if (squawkCode.Length < 4 && squawkCode.All(char.IsDigit))
                            {
                                squawkCode = squawkCode.PadLeft(4, '0');
                            }

                            uint bcdValue = ConvertSquawkToBCD(squawkCode, out string errorMessage);

                            if (errorMessage != null)
                            {
                                announcer.Announce(errorMessage);
                            }
                            else
                            {
                                simConnectManager.SendEvent(varKey, bcdValue);
                                announcer.Announce($"Squawk set to {squawkCode}");
                            }
                        }
                        else if (double.TryParse(textBox.Text, out double value))
                        {
                            // Special handling for VS/FPA set
                            if (varKey == "A32NX.FCU_VS_SET")
                            {
                                // Check current TRK/FPA mode to validate input range
                                bool isFpaMode = currentSimVarValues.ContainsKey("A32NX_TRK_FPA_MODE_ACTIVE") &&
                                               currentSimVarValues["A32NX_TRK_FPA_MODE_ACTIVE"] == 1;

                                bool isValidValue = false;
                                string modeText = "";

                                if (isFpaMode)
                                {
                                    // FPA mode: -9.9 to 9.9 degrees
                                    isValidValue = value >= -9.9 && value <= 9.9;
                                    modeText = "FPA";
                                }
                                else
                                {
                                    // VS mode: -6000 to 6000 ft/min
                                    isValidValue = value >= -6000 && value <= 6000;
                                    modeText = "VS";
                                }

                                if (isValidValue)
                                {
                                    simConnectManager.SendEvent(varKey, (uint)(value * (isFpaMode ? 10 : 1)));
                                    announcer.Announce($"{modeText} set to {value}");
                                }
                                else
                                {
                                    string rangeText = isFpaMode ? "-9.9 to 9.9" : "-6000 to 6000";
                                    announcer.Announce($"Invalid {modeText} value. Range: {rangeText}");
                                }
                            }
                            // Special handling for COM frequency set (uses Hz events)
                            else if (varKey.StartsWith("COM_") && varKey.Contains("FREQUENCY_SET"))
                            {
                                // Validate COM frequency range (118.000 - 136.975 MHz)
                                if (value >= 118.0 && value <= 136.975)
                                {
                                    // Convert MHz to Hz (simple multiplication, no BCD16 needed)
                                    uint frequencyHz = (uint)(value * 1000000);

                                    // Determine which COM radio (1, 2, or 3)
                                    string comIndex = "1"; // Default to COM1
                                    if (varKey.Contains(":2")) comIndex = "2";
                                    else if (varKey.Contains(":3")) comIndex = "3";

                                    // Always set standby first, then swap if setting active
                                    string setEvent = "COM_STBY_RADIO_SET_HZ"; // Sets standby in Hz
                                    string swapEvent = $"COM{comIndex}_RADIO_SWAP";

                                    // For active frequency: set standby then swap
                                    if (varKey.Contains("ACTIVE"))
                                    {
                                        simConnectManager.SendEvent(setEvent, frequencyHz);
                                        System.Threading.Thread.Sleep(100); // Small delay for sim to process
                                        simConnectManager.SendEvent(swapEvent);
                                        announcer.Announce($"Active frequency set to {value:F3}");
                                    }
                                    // For standby frequency: just set it
                                    else
                                    {
                                        simConnectManager.SendEvent(setEvent, frequencyHz);
                                        announcer.Announce($"Standby frequency set to {value:F3}");
                                    }
                                }
                                else
                                {
                                    announcer.Announce($"Invalid COM frequency. Range: 118.000 to 136.975 MHz");
                                }
                            }
                            // Special handling for A32NX baro setting (requires conversion based on unit mode)
                            else if (varKey == "A32NX.FCU_EFIS_L_BARO_SET")
                            {
                                // Check current unit mode from the combo box
                                bool isInHgMode = false;
                                if (currentSimVarValues.ContainsKey("A32NX_FCU_EFIS_L_BARO_IS_INHG"))
                                {
                                    isInHgMode = currentSimVarValues["A32NX_FCU_EFIS_L_BARO_IS_INHG"] == 1;
                                }

                                // Convert to hPa * 16 format expected by FlyByWire
                                uint convertedValue;
                                string unitLabel;

                                if (isInHgMode)
                                {
                                    // User entered inHg, convert to hPa first then multiply by 16
                                    double hPa = value * 33.8639;
                                    convertedValue = (uint)(hPa * 16);
                                    unitLabel = "inHg";
                                }
                                else
                                {
                                    // User entered hPa, just multiply by 16
                                    convertedValue = (uint)(value * 16);
                                    unitLabel = "hPa";
                                }

                                simConnectManager.SendEvent(varKey, convertedValue);
                                announcer.Announce($"{varDef.DisplayName} set to {value:F2} {unitLabel}");
                            }
                            // Special handling for A32NX right baro setting (requires conversion based on unit mode)
                            else if (varKey == "A32NX.FCU_EFIS_R_BARO_SET")
                            {
                                // Check current unit mode from the combo box
                                bool isInHgMode = false;
                                if (currentSimVarValues.ContainsKey("A32NX_FCU_EFIS_R_BARO_IS_INHG"))
                                {
                                    isInHgMode = currentSimVarValues["A32NX_FCU_EFIS_R_BARO_IS_INHG"] == 1;
                                }

                                // Convert to hPa * 16 format expected by FlyByWire
                                uint convertedValue;
                                string unitLabel;

                                if (isInHgMode)
                                {
                                    // User entered inHg, convert to hPa first then multiply by 16
                                    double hPa = value * 33.8639;
                                    convertedValue = (uint)(hPa * 16);
                                    unitLabel = "inHg";
                                }
                                else
                                {
                                    // User entered hPa, just multiply by 16
                                    convertedValue = (uint)(value * 16);
                                    unitLabel = "hPa";
                                }

                                simConnectManager.SendEvent(varKey, convertedValue);
                                announcer.Announce($"{varDef.DisplayName} set to {value:F2} {unitLabel}");
                            }
                            else
                            {
                                simConnectManager.SendEvent(varKey, (uint)value);
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
                                simConnectManager.SendEvent(varDef.Name, 0);
                            }
                            else
                            {
                                // Use EventParam if it's set, otherwise use 0
                                uint param = varDef.EventParam > 0 ? varDef.EventParam : 0;
                                simConnectManager.SendEvent(varDef.Name, param);
                            }

                            // Handle button state announcements for all panels
                            HandleButtonStateAnnouncement(varKey);
                        }
                        else if (varDef.Type == SimVarType.HVar)
                        {
                            // Handle H-variable buttons (MobiFlight WASM)
                            if (!string.IsNullOrEmpty(varDef.PressEvent) && !string.IsNullOrEmpty(varDef.ReleaseEvent))
                            {
                                // Automatic press/release sequence
                                simConnectManager.SendButtonPressRelease(varDef.PressEvent, varDef.ReleaseEvent, varDef.PressReleaseDelay);

                                // Request LED state after button action using existing LVar system
                                if (!string.IsNullOrEmpty(varDef.LedVariable))
                                {
                                    Timer ledCheckTimer = new Timer();
                                    ledCheckTimer.Interval = varDef.PressReleaseDelay + 300; // Wait for press/release + 300ms
                                    ledCheckTimer.Tick += (ts, te) =>
                                    {
                                        ledCheckTimer.Stop();
                                        ledCheckTimer.Dispose();
                                        // Use existing LVar request system to read LED state
                                        simConnectManager.RequestSpecificLVar(varDef.LedVariable, varDef.LedVariable);
                                        System.Diagnostics.Debug.WriteLine($"[MainForm] Requesting LED state: {varDef.LedVariable}");
                                    };
                                    ledCheckTimer.Start();
                                }
                            }
                            else if (!string.IsNullOrEmpty(varDef.PressEvent))
                            {
                                // Single H-variable execution
                                simConnectManager.SendHVar(varDef.PressEvent);
                            }

                            System.Diagnostics.Debug.WriteLine($"[MainForm] H-variable button pressed: {varDef.DisplayName}");
                        }
                        else if (varDef.Type == SimVarType.LVar)
                        {
                            // Special handling for clear warning buttons - send 0 to turn off
                            if (varKey == "CLEAR_MASTER_WARNING" || varKey == "CLEAR_MASTER_CAUTION")
                            {
                                simConnectManager.SetLVar(varDef.Name, 0);
                            }
                            else
                            {
                                simConnectManager.SetLVar(varDef.Name, 1);

                                // Handle momentary buttons - auto-reset to 0 after a short delay
                                if (varDef.IsMomentary)
                                {
                                    Timer momentaryTimer = new Timer();
                                    momentaryTimer.Interval = 150; // 150ms delay
                                    momentaryTimer.Tick += (ts, te) =>
                                    {
                                        momentaryTimer.Stop();
                                        momentaryTimer.Dispose();
                                        simConnectManager.SetLVar(varDef.Name, 0);
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
            if (SimVarDefinitions.PanelDisplayVariables.ContainsKey(currentPanel))
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
                displayTextBox.AccessibleName = "Status display";
                displayTextBox.Text = "";  // Empty by default

                // Refresh button
                Button refreshButton = new Button();
                refreshButton.Text = "Refresh";
                refreshButton.Size = new Size(80, 23);
                refreshButton.Location = new Point(0, 32);
                refreshButton.AccessibleName = "Refresh status";

                refreshButton.Click += async (s2, e2) =>
                {
                    displayTextBox.Text = "Loading...";
                    displayValues.Clear();  // Clear old values for this panel

                    // Get the display variables for this panel
                    var displayVars = SimVarDefinitions.PanelDisplayVariables[currentPanel];

                    // Create a task completion source for each variable
                    var pendingValues = new Dictionary<string, TaskCompletionSource<bool>>();
                    foreach (var varKey in displayVars)
                    {
                        pendingValues[varKey] = new TaskCompletionSource<bool>();
                    }

                    // Store the pending values temporarily
                    pendingDisplayRequests = pendingValues;

                    // Request all values
                    foreach (var varKey in displayVars)
                    {
                        if (SimVarDefinitions.Variables.ContainsKey(varKey))
                        {
                            simConnectManager.RequestVariable(varKey);
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
                };

                displayPanel.Controls.Add(displayTextBox);
                displayPanel.Controls.Add(refreshButton);
                layout.Controls.Add(displayPanel, 1, displayRow);

                // Store reference to display textbox
                currentControls["_DISPLAY_"] = displayTextBox;
            }

            controlsContainer.Controls.Add(layout);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            hotkeyManager?.Cleanup();
            simConnectManager?.Disconnect();
            announcer?.Cleanup();
            base.OnFormClosing(e);
        }
    }
}
