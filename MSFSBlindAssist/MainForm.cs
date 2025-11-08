using System.Collections.Concurrent;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.A32NX;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
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
    private ChecklistForm? checklistForm;
    private TakeoffAssistManager takeoffAssistManager = null!;
    private HandFlyManager handFlyManager = null!;
    private ElectronicFlightBagForm? electronicFlightBagForm;
    private MSFSBlindAssist.Navigation.FlightPlanManager flightPlanManager = null!;
    private MSFSBlindAssist.Navigation.WaypointTracker waypointTracker = null!;

    // Event batching infrastructure for high-volume variable updates
    // Producer-consumer pattern: SimConnect thread produces → UI timer consumes
    private readonly ConcurrentQueue<SimVarUpdateEventArgs> eventQueue = new ConcurrentQueue<SimVarUpdateEventArgs>();
    private System.Windows.Forms.Timer? eventBatchTimer;
    private int queuedEventCount = 0;  // Track queue size (ConcurrentQueue.Count is expensive)
    private int droppedEventCount = 0;  // Diagnostic: count dropped events due to queue overflow
    private int processedBatchCount = 0;  // Diagnostic: count processed batches

    // Panel loading debounce timer (prevents NVDA overload during rapid arrow navigation)
    private System.Windows.Forms.Timer? _panelLoadTimer;
    private string? _pendingPanelLoad = null;  // Track which panel to load when timer fires

    // Current state
    private string currentSection = "";
    private string currentPanel = "";
    private Dictionary<string, Control> currentControls = new Dictionary<string, Control>();
    private Dictionary<string, double> currentSimVarValues = new Dictionary<string, double>();
    private bool updatingFromSim = false;
    private Dictionary<string, double> displayValues = new Dictionary<string, double>();  // Store display values
    private Dictionary<string, TaskCompletionSource<bool>>? pendingDisplayRequests = null;  // Track pending display requests
    private ConcurrentDictionary<string, bool> pendingStateAnnouncements = new ConcurrentDictionary<string, bool>();  // Track state announcement requests
    private string currentFlightPhase = "";  // Track current flight phase for window title
    private IAircraftDefinition currentAircraft;

    public MainForm()
    {
        // Load last selected aircraft from settings
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        currentAircraft = LoadAircraftFromCode(settings.LastAircraft ?? "A320");

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
            // Future aircraft will be added here
            _ => new FlyByWireA320Definition() // Default to A320
        };
    }
    
    private void MainForm_Load(object? sender, EventArgs e)
    {
        // Set window title
        this.Text = "MSFS Blind Assist";

        // Populate sections dynamically from aircraft definition
        foreach (var section in currentAircraft.GetPanelStructure().Keys)
        {
            sectionsListBox.Items.Add(section);
        }

        // Sync menu items with the loaded aircraft (fixes first-launch menu mismatch)
        UpdateAircraftMenuItems();

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

        // Initialize hand fly manager
        handFlyManager = new HandFlyManager(announcer);
        handFlyManager.HandFlyModeActiveChanged += OnHandFlyModeActiveChanged;

        // Initialize airport database provider (optional - can be null if database not built yet)
        airportDataProvider = DatabaseSelector.SelectProvider();

        // Initialize flight plan manager with navigation database
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
        flightPlanManager = new MSFSBlindAssist.Navigation.FlightPlanManager(navigationDatabasePath, airportDataProvider);

        // Initialize waypoint tracker
        waypointTracker = new MSFSBlindAssist.Navigation.WaypointTracker();

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

        // Update status bar with database info
        UpdateDatabaseStatusDisplay();

        // Connect after a delay
        System.Windows.Forms.Timer connectTimer = new System.Windows.Forms.Timer();
        connectTimer.Interval = 2000;
        connectTimer.Tick += (s, e) =>
        {
            connectTimer.Stop();
            connectTimer.Dispose();
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

            // Automatically switch database if simulator version doesn't match
            CheckAndSwitchDatabase();

            // Request all current values when connected
            RequestAllCurrentValues();

            // Start a grace period before enabling continuous variable announcements
            // This prevents initial ECAM messages and other variables from being announced
            // when connecting to a cold and dark aircraft
            System.Windows.Forms.Timer announcementGracePeriodTimer = new System.Windows.Forms.Timer();
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
            // Stop event batching timer and clear queue
            eventBatchTimer?.Stop();

            // Clear event queue and reset counters
            while (eventQueue.TryDequeue(out _)) { }
            queuedEventCount = 0;
            droppedEventCount = 0;
            processedBatchCount = 0;
            System.Diagnostics.Debug.WriteLine("[MainForm] Event batching timer stopped, queue cleared");

            announcer.Announce(status);
            // Reset window title when disconnected
            this.Text = "MSFS Blind Assist";
            // Disable announcements when disconnected
            simVarMonitor.Reset();
            // Reset ECAM suppression flag for next connection
            simConnectManager.SuppressECAMAnnouncements = true;
        }
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (InvokeRequired)
        {
            // PRODUCER: Enqueue event for batch processing instead of immediate BeginInvoke
            // This reduces UI thread marshaling overhead by ~95% for high-volume updates (400+ vars/sec)
            // Queue overflow protection prevents unbounded memory growth
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

        // Step 2: Handle special one-off announcements (terminal cases only)
        if (HandleSpecialAnnouncements(e))
        {
            return; // These are terminal - no further processing needed
        }

        // Step 2.5: Allow aircraft-specific variable processing (e.g., FCU display combining)
        // This lets each aircraft handle complex variables before generic processing
        bool wasProcessedByAircraft = currentAircraft.ProcessSimVarUpdate(e.VarName, e.Value, announcer);
        if (wasProcessedByAircraft)
        {
            // Update window title if flight phase changed (for aircraft that track flight phases)
            if (!string.IsNullOrEmpty(currentAircraft.CurrentFlightPhase))
            {
                this.Text = $"MSFS BA - {currentAircraft.CurrentFlightPhase} phase active";
            }
            return; // Aircraft handled it completely, no further processing needed
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
                simVarMonitor.ProcessUpdate(e.VarName, e.Value, e.Description);
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

        // Diagnostics: Log batch statistics every 100 batches (~3 seconds at 33ms interval)
        if (processedCount > 0)
        {
            processedBatchCount++;

            if (processedBatchCount % 100 == 0)
            {
                int remainingInQueue = queuedEventCount;
                System.Diagnostics.Debug.WriteLine($"[MainForm] Batch stats: processed {processedCount} events, queue: {batchStartQueueSize}→{remainingInQueue}, dropped: {droppedEventCount}, total batches: {processedBatchCount}");

                // Warning if queue is growing (more events arriving than we can process)
                if (remainingInQueue > MAX_QUEUE_SIZE / 2)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainForm] WARNING: Event queue backlog ({remainingInQueue} events). Consider increasing MAX_BATCH_SIZE or EVENT_BATCH_INTERVAL_MS.");
                }
            }
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

        // Handle hand fly mode pitch updates
        if (e.VarName == "PLANE_PITCH_DEGREES" && handFlyManager.IsActive)
        {
            // Convert radians to degrees and negate (SimConnect uses body axis: negative = nose up)
            double pitchDegrees = -(e.Value * (180.0 / Math.PI));
            handFlyManager.ProcessPitchUpdate(pitchDegrees);
            return true;
        }

        // Handle hand fly mode bank updates
        if (e.VarName == "PLANE_BANK_DEGREES" && handFlyManager.IsActive)
        {
            // Convert radians to degrees (positive = right bank, negative = left bank)
            double bankDegrees = e.Value * (180.0 / Math.PI);
            handFlyManager.ProcessBankUpdate(bankDegrees);
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
        if (currentControls.ContainsKey(varName))
        {
            updatingFromSim = true;
            
            Control control = currentControls[varName];
            if (control is ComboBox combo)
            {
                // Find the matching value in the combo box
                if (currentAircraft.GetVariables().ContainsKey(varName))
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

    /// <summary>
    /// Request variables efficiently based on the variable context
    /// </summary>
    private void RequestRelatedVariables(string varKey, string actionDescription)
    {
        string? panelName = GetPanelForVariable(varKey);

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
        if (!e.IsInitialValue && !updatingFromSim)
        {
            announcer.Announce(e.Description);
        }
    }

    private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
    {
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
            case HotkeyAction.ReadVerticalSpeed:
                simConnectManager.RequestVerticalSpeed();
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
            case HotkeyAction.ReadDestinationRunwayDistance:
                RequestDestinationRunwayDistance();
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
            case HotkeyAction.ShowChecklist:
                ShowChecklistDialog();
                break;
            case HotkeyAction.ShowElectronicFlightBag:
                ShowElectronicFlightBagDialog();
                break;
            case HotkeyAction.ToggleTakeoffAssist:
                ToggleTakeoffAssist();
                break;
            case HotkeyAction.ToggleHandFlyMode:
                ToggleHandFlyMode();
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
            // Note: FCU push/pull, autopilot toggles, FCU set value dialogs, and A32NX-specific hotkeys
            // are now handled by the aircraft definition via HandleHotkeyAction()
        }
    }

    private void OnOutputHotkeyModeChanged(object? sender, bool active)
    {
        // Use the announcer properly
        if (active)
        {
            announcer.AnnounceImmediate("output");
        }
    }

    private void OnInputHotkeyModeChanged(object? sender, bool active)
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
            checklistForm = new ChecklistForm(announcer, currentAircraft.AircraftCode);
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
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            electronicFlightBagForm = new ElectronicFlightBagForm(flightPlanManager, simConnectManager, announcer, waypointTracker, settings.SimbriefUsername ?? "");
        }

        // Show the form (reuses same instance to preserve flight plan data)
        electronicFlightBagForm.Show();
        electronicFlightBagForm.BringToFront();
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
                    flightPlanManager.CurrentFlightPlan,
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

    private void ShowPFDDialog()
    {
        // Ensure output hotkey mode is deactivated before showing window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new PFDForm(announcer, simConnectManager);
        dialog.CurrentAircraft = currentAircraft;
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

    private void ShowFuelPayloadDialog()
    {
        // Ensure output hotkey mode is deactivated before showing window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new FuelPayloadDisplayForm(announcer, simConnectManager);
        dialog.Show();
    }

    private void ShowStatusDialog()
    {
        // Ensure output hotkey mode is deactivated before showing window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new StatusDisplayForm(announcer, simConnectManager);
        dialog.Show();
    }

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

        // Request current heading for takeoff assist toggle
        simConnectManager.RequestHeadingForTakeoffAssist();
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

            announcer.AnnounceImmediate("Analyzing scene with Gemini AI...");

            // Analyze scene with Gemini
            string analysis = await geminiService.AnalyzeSceneAsync(screenshot);

            // Show result in form
            var resultForm = new DisplayReadingResultForm("Scene", analysis, "Description");
            resultForm.ShowDialog(this);

            announcer.AnnounceImmediate("Scene description ready");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("API key"))
        {
            announcer.AnnounceImmediate("Gemini API key not configured. Please configure it in File menu, Gemini API Key Settings.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Error in DescribeSceneAsync: {ex.Message}");
            announcer.AnnounceImmediate($"Error describing scene: {ex.Message}");
        }
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

    private void OnTakeoffAssistActiveChanged(object? sender, bool isActive)
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
            // Start monitoring pitch and bank
            simConnectManager.StartHandFlyMonitoring();
        }
        else
        {
            // Stop monitoring
            simConnectManager.StopHandFlyMonitoring();
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

    private void GeminiApiKeySettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new Forms.GeminiApiKeySettingsForm())
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                statusLabel.Text = "Gemini API key saved successfully";
                announcer.Announce("Gemini API key saved successfully");
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

    private void FlyByWireA320MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FlyByWireA320Definition());
    }

    private void FenixA320MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FenixA320Definition());
    }

    private void SwitchAircraft(IAircraftDefinition newAircraft)
    {
        // Update the aircraft instance
        currentAircraft = newAircraft;

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

    private void UpdateAircraftSpecificMenuItems()
    {
        // Reserved for future menu-based aircraft-specific window launching
        // Currently all display windows are launched via hotkeys handled by aircraft definitions
    }

    /// <summary>
    /// Updates aircraft menu item check states to match the current aircraft.
    /// </summary>
    private void UpdateAircraftMenuItems()
    {
        // Clear all menu item checks first
        flyByWireA320MenuItem.Checked = false;
        fenixA320MenuItem.Checked = false;

        // Set the check on the current aircraft's menu item
        if (currentAircraft is FlyByWireA320Definition)
        {
            flyByWireA320MenuItem.Checked = true;
        }
        else if (currentAircraft is FenixA320Definition)
        {
            fenixA320MenuItem.Checked = true;
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

            // Request variables first
            if (simConnectManager != null && simConnectManager.IsConnected)
            {
                simConnectManager.RequestPanelVariables(panelToLoad, $"{panelToLoad} panel opened");
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

        foreach (var varKey in currentAircraft.GetPanelControls()[currentPanel])
        {
            if (!currentAircraft.GetVariables().ContainsKey(varKey))
                continue;

            var varDef = currentAircraft.GetVariables()[varKey];
            
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
                // Check if variable should be rendered as button instead of combo box (aircraft-specific)
                if (varDef.RenderAsButton)
                {
                    Button controlButton = new Button();
                    controlButton.Text = varDef.DisplayName;
                    controlButton.Size = new Size(240, 25);
                    controlButton.Name = varKey;
                    controlButton.AccessibleName = varDef.DisplayName;
                    controlButton.AccessibleDescription = $"Press {varDef.DisplayName}";

                    // Handle button click - send value 1 which triggers HandleUIVariableSet
                    controlButton.Click += (s2, e2) =>
                    {
                        // Let aircraft handle special cases first (custom button logic, transitions, etc.)
                        if (currentAircraft.HandleUIVariableSet(varKey, 1, varDef, simConnectManager, announcer))
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

                    // Handle selection change - set both engines
                    combo.SelectedIndexChanged += (s2, e2) =>
                    {
                        if (!updatingFromSim && combo.SelectedIndex >= 0)
                        {
                            uint mode = (uint)combo.SelectedIndex;
                            // Set both engines to the same mode
                            simConnectManager?.SendEvent("TURBINE_IGNITION_SWITCH_SET1", mode);
                            simConnectManager?.SendEvent("TURBINE_IGNITION_SWITCH_SET2", mode);
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

                    // Handle selection change - send multiple events
                    // Capture varKey to avoid nullable reference warnings in closure
                    string capturedVarKey = varKey;
                    combo.SelectedIndexChanged += (s2, e2) =>
                    {
                        if (!updatingFromSim && combo.SelectedIndex >= 0)
                        {
                            var selectedValue = sortedValues[combo.SelectedIndex].Key;

                            // Send the main LVar
                            simConnectManager?.SetLVar(capturedVarKey, selectedValue);
                            currentSimVarValues[capturedVarKey] = selectedValue;

                            // Send additional events based on the control and value
                            if (capturedVarKey == "LIGHTING_LANDING_1") // Nose Light
                            {
                                if (selectedValue == 2) // Off
                                {
                                    simConnectManager?.SendEvent("LANDING_LIGHTS_OFF", 1);
                                    simConnectManager?.SendEvent("LIGHT_TAXI", 0);
                                }
                                else if (selectedValue == 1) // Taxi
                                {
                                    simConnectManager?.SendEvent("LANDING_LIGHTS_ON", 1);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_20", 1);
                                    simConnectManager?.SendEvent("LIGHT_TAXI", 1);
                                }
                                else if (selectedValue == 0) // T.O.
                                {
                                    simConnectManager?.SendEvent("LANDING_LIGHTS_ON", 1);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_17", 1);
                                    simConnectManager?.SendEvent("LIGHT_TAXI", 0);
                                }
                            }
                            else if (capturedVarKey == "LIGHTING_LANDING_2") // Left Landing Light
                            {
                                if (selectedValue == 2) // Retract
                                {
                                    simConnectManager?.SendEvent("LANDING_2_RETRACTED", 1);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_18", 0);
                                }
                                else if (selectedValue == 1) // Off
                                {
                                    simConnectManager?.SendEvent("LANDING_2_RETRACTED", 0);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_18", 0);
                                }
                                else if (selectedValue == 0) // On
                                {
                                    simConnectManager?.SendEvent("LANDING_2_RETRACTED", 0);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_18", 1);
                                }
                            }
                            else if (capturedVarKey == "LIGHTING_LANDING_3") // Right Landing Light
                            {
                                if (selectedValue == 2) // Retract
                                {
                                    simConnectManager?.SendEvent("LANDING_3_RETRACTED", 1);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_19", 0);
                                }
                                else if (selectedValue == 1) // Off
                                {
                                    simConnectManager?.SendEvent("LANDING_3_RETRACTED", 0);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_19", 0);
                                }
                                else if (selectedValue == 0) // On
                                {
                                    simConnectManager?.SendEvent("LANDING_3_RETRACTED", 0);
                                    simConnectManager?.SendEvent("CIRCUIT_SWITCH_ON_19", 1);
                                }
                            }
                            else if (capturedVarKey == "LIGHTING_STROBE_0") // Strobe Lights
                            {
                                if (selectedValue == 2) // Off
                                {
                                    simConnectManager?.SendEvent("STROBES_OFF", 0);
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 0);
                                    simConnectManager?.SetLVar("LIGHT STROBE", 0);
                                    simConnectManager?.SetLVar("LIGHTING_STROBE_0", 2);
                                }
                                else if (selectedValue == 0) // On
                                {
                                    simConnectManager?.SendEvent("STROBES_ON", 0);
                                    simConnectManager?.SetLVar("LIGHT STROBE", 1);
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 0);
                                    simConnectManager?.SetLVar("LIGHTING_STROBE_0", 0);
                                }
                                else if (selectedValue == 1) // Auto
                                {
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 1);
                                    simConnectManager?.SetLVar("LIGHTING_STROBE_0", 1);
                                }
                            }
                            else if (capturedVarKey == "LIGHT BEACON") // Beacon Light
                            {
                                if (selectedValue == 0) // Off
                                {
                                    simConnectManager?.SendEvent("BEACON_LIGHTS_SET", 0);
                                }
                                else if (selectedValue == 1) // On
                                {
                                    simConnectManager?.SendEvent("BEACON_LIGHTS_SET", 1);
                                }
                            }
                            else if (capturedVarKey == "LIGHT WING") // Wing Lights
                            {
                                if (selectedValue == 0) // Off
                                {
                                    simConnectManager?.SendEvent("WING_LIGHTS_SET", 0);
                                }
                                else if (selectedValue == 1) // On
                                {
                                    simConnectManager?.SendEvent("WING_LIGHTS_SET", 1);
                                }
                            }
                            else if (capturedVarKey == "LIGHT NAV") // Nav Lights
                            {
                                // Nav and Logo lights are combined in real aircraft
                                // Control both when Nav light is changed
                                if (selectedValue == 0) // Off
                                {
                                    simConnectManager?.SendEvent("NAV_LIGHTS_SET", 0);
                                    simConnectManager?.SendEvent("LOGO_LIGHTS_SET", 0);
                                }
                                else if (selectedValue == 1) // On
                                {
                                    simConnectManager?.SendEvent("NAV_LIGHTS_SET", 1);
                                    simConnectManager?.SendEvent("LOGO_LIGHTS_SET", 1);
                                }
                            }
                            else if (capturedVarKey == "LIGHT LOGO") // Logo Lights
                            {
                                // Logo lights are controlled with Nav lights in real aircraft
                                // Control both when Logo light is changed
                                if (selectedValue == 0) // Off
                                {
                                    simConnectManager?.SendEvent("NAV_LIGHTS_SET", 0);
                                    simConnectManager?.SendEvent("LOGO_LIGHTS_SET", 0);
                                }
                                else if (selectedValue == 1) // On
                                {
                                    simConnectManager?.SendEvent("NAV_LIGHTS_SET", 1);
                                    simConnectManager?.SendEvent("LOGO_LIGHTS_SET", 1);
                                }
                            }
                            else if (capturedVarKey == "CIRCUIT_SWITCH_ON:21") // Left RWY Turn Off Light
                            {
                                // Write directly to the SimVar (as per FBW documentation)
                                simConnectManager?.SetSimVar("CIRCUIT SWITCH ON:21", selectedValue, "bool");
                            }
                            else if (capturedVarKey == "CIRCUIT_SWITCH_ON:22") // Right RWY Turn Off Light
                            {
                                // Write directly to the SimVar (as per FBW documentation)
                                simConnectManager?.SetSimVar("CIRCUIT SWITCH ON:22", selectedValue, "bool");
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
                    combo.SelectedIndexChanged += (s2, e2) =>
                    {
                        if (!updatingFromSim && combo.SelectedIndex >= 0)
                        {
                            var selectedValue = sortedValues[combo.SelectedIndex].Key;

                            // Let aircraft handle special cases first (validation, conversion, multi-step logic)
                            if (currentAircraft.HandleUIVariableSet(varKey, selectedValue, varDef, simConnectManager, announcer))
                            {
                                currentSimVarValues[varKey] = selectedValue;
                                // NOTE: We do NOT call RequestRelatedVariables here because:
                                // - Most combo boxes are independent switches (changing one doesn't affect others)
                                // - Variables are refreshed when the panel opens (see PanelsListBox_SelectedIndexChanged)
                                // - Requesting all panel variables after each combo change is wasteful
                                return; // Aircraft handled it
                            }

                            // Generic handling follows if aircraft didn't handle it
                            if (varKey == "CABIN SEATBELTS ALERT SWITCH") // Seat Belts Signs
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
                            simConnectManager?.SendEvent(varKey, bcdValue);
                            announcer.Announce($"Squawk set to {squawkCode}");
                        }
                    }
                    else if (double.TryParse(textBox.Text, out double value))
                    {
                        // Let aircraft handle special cases first (validation, conversion, multi-step logic)
                        if (currentAircraft.HandleUIVariableSet(varKey, value, varDef, simConnectManager, announcer))
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
                var displayVars = currentAircraft.GetPanelDisplayVariables()[currentPanel];

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
                    if (currentAircraft.GetVariables().ContainsKey(varKey))
                    {
                        simConnectManager?.RequestVariable(varKey);
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
        })); // End BeginInvoke - deferred control creation
    } // End PanelLoadTimer_Tick

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        hotkeyManager?.Cleanup();
        simConnectManager?.Disconnect();
        announcer?.Cleanup();
        base.OnFormClosing(e);
    }
}
