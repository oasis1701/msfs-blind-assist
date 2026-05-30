
namespace MSFSBlindAssist.Hotkeys;

public class HotkeyManager : IDisposable
    {
        // Windows API for registering global hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Modifier keys
        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        // Virtual key codes
        private const uint VK_OEM_6 = 0xDD; // ] key
        private const uint VK_OEM_4 = 0xDB; // [ key

        // Hotkey IDs
        private const int HOTKEY_ACTIVATE = 9001;
        private const int HOTKEY_INPUT_ACTIVATE = 9020;
        private const int HOTKEY_HEADING = 9002;
        private const int HOTKEY_SPEED = 9003;
        private const int HOTKEY_ALTITUDE = 9004;
        private const int HOTKEY_ALTITUDE_AGL = 9005;
        private const int HOTKEY_ALTITUDE_MSL = 9006;
        private const int HOTKEY_AIRSPEED_IND = 9007;
        private const int HOTKEY_AIRSPEED_TRUE = 9008;
        private const int HOTKEY_GROUND_SPEED = 9009;
        private const int HOTKEY_VERTICAL_SPEED = 9010;
        private const int HOTKEY_HEADING_MAGNETIC = 9011;
        private const int HOTKEY_HEADING_TRUE = 9012;
        private const int HOTKEY_DESTINATION_RUNWAY = 9013;
        private const int HOTKEY_DESTINATION_RUNWAY_DISTANCE = 9014;
        private const int HOTKEY_ILS_GUIDANCE = 9015;
        private const int HOTKEY_WIND_INFO = 9016;
        private const int HOTKEY_METAR_REPORT = 9017;
        private const int HOTKEY_RUNWAY_TELEPORT = 9021;
        private const int HOTKEY_GATE_TELEPORT = 9022;
        private const int HOTKEY_LOCATION_INFO = 9023;
        private const int HOTKEY_SIMBRIEF_BRIEFING = 9025;
        private const int HOTKEY_PFD = 9026;
        private const int HOTKEY_TOGGLE_AP1 = 9027;
        private const int HOTKEY_TOGGLE_APPR = 9028;
        private const int HOTKEY_FCU_VSFPA = 9029;
        private const int HOTKEY_APPROACH_CAPABILITY = 9030;

        // FCU push/pull hotkey IDs
        private const int HOTKEY_FCU_HDG_PUSH = 9031;
        private const int HOTKEY_FCU_HDG_PULL = 9032;
        private const int HOTKEY_FCU_ALT_PUSH = 9033;
        private const int HOTKEY_FCU_ALT_PULL = 9034;
        private const int HOTKEY_FCU_SPD_PUSH = 9035;
        private const int HOTKEY_FCU_SPD_PULL = 9036;
        private const int HOTKEY_FCU_VS_PUSH = 9037;
        private const int HOTKEY_FCU_VS_PULL = 9038;

        // FCU set value hotkey IDs
        private const int HOTKEY_FCU_SET_HEADING = 9039;
        private const int HOTKEY_FCU_SET_SPEED = 9040;
        private const int HOTKEY_FCU_SET_ALTITUDE = 9041;
        private const int HOTKEY_FCU_SET_VS = 9042;
        private const int HOTKEY_FCU_SET_AUTOPILOT = 9075;
        private const int HOTKEY_TOGGLE_AP2 = 9043;
        private const int HOTKEY_SPEED_GD = 9044;
        private const int HOTKEY_SPEED_S = 9045;
        private const int HOTKEY_SPEED_F = 9046;
        private const int HOTKEY_SPEED_VFE = 9049;
        private const int HOTKEY_SPEED_VLS = 9050;
        private const int HOTKEY_SPEED_VS = 9051;
        private const int HOTKEY_CHECKLIST = 9052;
        private const int HOTKEY_FUEL_QUANTITY = 9053;
        private const int HOTKEY_NAV_DISPLAY = 9054;
        private const int HOTKEY_WAYPOINT_INFO = 9055;
        private const int HOTKEY_ECAM_DISPLAY = 9056;
        private const int HOTKEY_STATUS_DISPLAY = 9057;
        private const int HOTKEY_TOGGLE_TRIM = 9100;
        private const int HOTKEY_DISTANCE_TO_DEST = 9101;
        private const int HOTKEY_DISTANCE_TO_TOD = 9102;
        private const int HOTKEY_NAV_RADIO_INFO = 9103;
        private const int HOTKEY_NAV_RADIOS_SET = 9212;   // Input mode: Ctrl+N (Set NAV radios)
        private const int HOTKEY_TAKEOFF_ASSIST = 9058;
        private const int HOTKEY_TOGGLE_ECAM_MONITORING = 9059;
        private const int HOTKEY_HAND_FLY_MODE = 9075;
        private const int HOTKEY_VISUAL_GUIDANCE = 9076;
        private const int HOTKEY_MACH_SPEED = 9060;
        private const int HOTKEY_EFB = 9061;
        private const int HOTKEY_TRACK_SLOT_1 = 9062;
        private const int HOTKEY_TRACK_SLOT_2 = 9063;
        private const int HOTKEY_TRACK_SLOT_3 = 9064;
        private const int HOTKEY_TRACK_SLOT_4 = 9065;
        private const int HOTKEY_TRACK_SLOT_5 = 9066;
        private const int HOTKEY_FLAPS = 9067;
        private const int HOTKEY_FUEL_PAYLOAD = 9068;
        private const int HOTKEY_GEAR = 9095;
        private const int HOTKEY_ALTIMETER = 9096;
        private const int HOTKEY_FCU_SET_BARO = 9097;
        private const int HOTKEY_GROSS_WEIGHT_KG = 9098;
        private const int HOTKEY_TRACK_FIX = 9076;

        // Display reading hotkey IDs (Output mode - Alt+1-5, Fenix A320 only)
        private const int HOTKEY_READ_DISPLAY_PFD = 9069;
        private const int HOTKEY_READ_DISPLAY_LOWER_ECAM = 9070;
        private const int HOTKEY_READ_DISPLAY_UPPER_ECAM = 9071;
        private const int HOTKEY_READ_DISPLAY_ND = 9072;
        private const int HOTKEY_READ_DISPLAY_ISIS = 9073;
        private const int HOTKEY_DESCRIBE_SCENE = 9074;

        // Hand fly mode global hotkey IDs (separate from output mode)
        private const int HOTKEY_HANDFLY_HEADING = 9077;
        private const int HOTKEY_HANDFLY_VERTICAL_SPEED = 9078;
        private const int HOTKEY_HANDFLY_ALTITUDE_AGL = 9079;
        private const int HOTKEY_HANDFLY_SPEED = 9080;
        private const int HOTKEY_HANDFLY_RUNWAY_DISTANCE = 9081;
        private const int HOTKEY_HANDFLY_BANK_ANGLE = 9082;
        private const int HOTKEY_HANDFLY_PITCH = 9083;
        private const int HOTKEY_HANDFLY_ALTITUDE_MSL = 9084;

        // Visual guidance mode hotkey IDs
        private const int HOTKEY_VISUAL_TARGET_FPM = 9090;

        // Monitor Manager hotkey ID (per-aircraft behavior)
        private const int HOTKEY_MONITOR_MANAGER = 9091;

        // Fenix MCDU hotkey ID
        private const int HOTKEY_FENIX_MCDU = 9092;

        // PMDG EFB hotkey ID
        private const int HOTKEY_PMDG_EFB = 9094;

        // Nearest city announcement hotkey ID
        private const int HOTKEY_NEAREST_CITY = 9093;

        // TCAS and Weather Radar hotkey IDs (Output mode)
        private const int HOTKEY_TCAS_ANNOUNCE = 9104;
        private const int HOTKEY_TCAS_WINDOW = 9105;
        private const int HOTKEY_WEATHER_RADAR = 9106;

        // Outside temperature hotkey ID (Output mode)
        private const int HOTKEY_OUTSIDE_TEMP = 9107;

        // Squawk code hotkey ID (Output mode)
        private const int HOTKEY_SQUAWK_CODE = 9108;

        // Taxi guidance hotkey IDs
        private const int HOTKEY_TAXI_STATUS = 9200;        // Output mode: Y (Taxi status)
        private const int HOTKEY_TAXI_REPEAT = 9201;        // Output mode: Ctrl+Y (Repeat instruction)
        private const int HOTKEY_TAXI_FORM = 9202;          // Input mode: Shift+Y (Open taxi form)
        private const int HOTKEY_TAXI_CONTINUE = 9203;      // Input mode: Y (Continue past hold-short)
        private const int HOTKEY_TAXI_STOP = 9204;          // Input mode: Ctrl+Y (Stop guidance)
        private const int HOTKEY_TAXI_WHERE_AM_I = 9205;    // Output mode: Alt+Y (Describe current location)
        private const int HOTKEY_LANDING_EXIT = 9206;       // Input mode: Shift+X (Landing Exit Planner)
        private const int HOTKEY_GROUND_TRAFFIC = 9207;     // Output mode: Alt+G (Nearest ground traffic)
        private const int HOTKEY_ACCESS_GSX = 9208;         // Input mode: Alt+G (Open Access GSX window)
        private const int HOTKEY_READ_GSX_TOOLTIP = 9209;   // Output mode: Ctrl+G (Read latest GSX tooltip)

        // Time-of-day hotkey IDs (Output mode). Local time = aircraft position
        // local time (sim handles tz mapping); Zulu = UTC. HH:MM by default,
        // HH:MM:SS when AnnounceTimeWithSeconds is on.
        private const int HOTKEY_LOCAL_TIME = 9210;
        private const int HOTKEY_ZULU_TIME = 9211;

        private IntPtr windowHandle;
        private bool visualGuidanceHotkeysActive = false;
        private bool outputHotkeyModeActive = false;
        private bool inputHotkeyModeActive = false;
        private bool handFlyHotkeysActive = false;
        private bool disposed = false;
        private bool suspended = false;

        // === Shared quick-access (single-letter, no-modifier) hotkey set ============
        // Registered when EITHER HandFly OR visual guidance is active. Same keys serve
        // both modes — they're "things a pilot wants to query while hand-flying", and
        // running visual guidance is a hand-flying scenario with extra audio guidance.
        // Reference-counted so the keys survive a single mode toggling off while the
        // other mode is still active. Per-key registration tracking + partial-retry
        // on each acquire handles the case where Windows previously refused a key
        // (some other app held it) but it's now available.
        private int quickAccessActiveModeCount = 0;
        private readonly bool[] quickAccessKeyRegistered = new bool[9];
        private static readonly (int id, uint vk, string label)[] QuickAccessKeys = new[]
        {
            (HOTKEY_HANDFLY_HEADING,         (uint)0x48, "H"),
            (HOTKEY_HANDFLY_VERTICAL_SPEED,  (uint)0x56, "V"),
            (HOTKEY_HANDFLY_ALTITUDE_AGL,    (uint)0x51, "Q"),
            (HOTKEY_HANDFLY_SPEED,           (uint)0x53, "S"),
            (HOTKEY_HANDFLY_RUNWAY_DISTANCE, (uint)0x44, "D"),
            (HOTKEY_HANDFLY_BANK_ANGLE,      (uint)0x42, "B"),
            (HOTKEY_HANDFLY_PITCH,           (uint)0x50, "P"),
            (HOTKEY_HANDFLY_ALTITUDE_MSL,    (uint)0x41, "A"),
            (HOTKEY_VISUAL_TARGET_FPM,       (uint)0x46, "F"),
        };

        public event EventHandler<HotkeyEventArgs>? HotkeyTriggered;
        public event EventHandler<HotkeyModeEventArgs>? OutputHotkeyModeChanged;
        public event EventHandler<HotkeyModeEventArgs>? InputHotkeyModeChanged;

        public bool IsOutputHotkeyModeActive => outputHotkeyModeActive;
        public bool IsInputHotkeyModeActive => inputHotkeyModeActive;

        public void Initialize(IntPtr handle)
        {
            windowHandle = handle;

            // Register the ] key as global hotkey for read mode
            bool registered = RegisterHotKey(windowHandle, HOTKEY_ACTIVATE, MOD_NONE, VK_OEM_6);

            // Register the [ key as global hotkey for input mode
            bool inputRegistered = RegisterHotKey(windowHandle, HOTKEY_INPUT_ACTIVATE, MOD_NONE, VK_OEM_4);

            if (!registered || !inputRegistered)
            {
                System.Diagnostics.Debug.WriteLine("Failed to register hotkeys");
            }
        }

        public bool ProcessWindowMessage(ref Message m)
        {
            // WM_HOTKEY message
            if (m.Msg == 0x0312)
            {
                int hotkeyId = m.WParam.ToInt32();

                if (hotkeyId == HOTKEY_ACTIVATE)
                {
                    if (outputHotkeyModeActive)
                    {
                        DeactivateOutputHotkeyMode(wasCancelled: true);
                    }
                    else if (!inputHotkeyModeActive)
                    {
                        ActivateOutputHotkeyMode();
                    }
                    return true;
                }
                else if (hotkeyId == HOTKEY_INPUT_ACTIVATE)
                {
                    if (inputHotkeyModeActive)
                    {
                        DeactivateInputHotkeyMode(wasCancelled: true);
                    }
                    else if (!outputHotkeyModeActive)
                    {
                        ActivateInputHotkeyMode();
                    }
                    return true;
                }
                else if (outputHotkeyModeActive)
                {
                    switch (hotkeyId)
                    {
                        case HOTKEY_HEADING:
                            TriggerHotkey(HotkeyAction.ReadHeading);
                            break;
                        case HOTKEY_SPEED:
                            TriggerHotkey(HotkeyAction.ReadSpeed);
                            break;
                        case HOTKEY_ALTITUDE:
                            TriggerHotkey(HotkeyAction.ReadAltitude);
                            break;
                        case HOTKEY_FCU_VSFPA:
                            TriggerHotkey(HotkeyAction.ReadFCUVerticalSpeedFPA);
                            break;
                        case HOTKEY_ALTITUDE_AGL:
                        case HOTKEY_HANDFLY_ALTITUDE_AGL:
                            TriggerHotkey(HotkeyAction.ReadAltitudeAGL);
                            break;
                        case HOTKEY_ALTITUDE_MSL:
                        case HOTKEY_HANDFLY_ALTITUDE_MSL:
                            TriggerHotkey(HotkeyAction.ReadAltitudeMSL);
                            break;
                        case HOTKEY_AIRSPEED_IND:
                        case HOTKEY_HANDFLY_SPEED:
                            TriggerHotkey(HotkeyAction.ReadAirspeedIndicated);
                            break;
                        case HOTKEY_AIRSPEED_TRUE:
                            TriggerHotkey(HotkeyAction.ReadAirspeedTrue);
                            break;
                        case HOTKEY_GROUND_SPEED:
                            TriggerHotkey(HotkeyAction.ReadGroundSpeed);
                            break;
                        case HOTKEY_MACH_SPEED:
                            TriggerHotkey(HotkeyAction.ReadMachSpeed);
                            break;
                        case HOTKEY_VERTICAL_SPEED:
                        case HOTKEY_HANDFLY_VERTICAL_SPEED:
                            TriggerHotkey(HotkeyAction.ReadVerticalSpeed);
                            break;
                        case HOTKEY_HEADING_MAGNETIC:
                        case HOTKEY_HANDFLY_HEADING:
                            TriggerHotkey(HotkeyAction.ReadHeadingMagnetic);
                            break;
                        case HOTKEY_HEADING_TRUE:
                            TriggerHotkey(HotkeyAction.ReadHeadingTrue);
                            break;
                        case HOTKEY_LOCAL_TIME:
                            TriggerHotkey(HotkeyAction.ReadLocalTime);
                            break;
                        case HOTKEY_ZULU_TIME:
                            TriggerHotkey(HotkeyAction.ReadZuluTime);
                            break;
                        case HOTKEY_DESTINATION_RUNWAY_DISTANCE:
                            TriggerHotkey(HotkeyAction.ReadDestinationRunwayDistance);
                            break;
                        case HOTKEY_ILS_GUIDANCE:
                            TriggerHotkey(HotkeyAction.ReadILSGuidance);
                            break;
                        case HOTKEY_WIND_INFO:
                            TriggerHotkey(HotkeyAction.ReadWindInfo);
                            break;
                        case HOTKEY_METAR_REPORT:
                            TriggerHotkey(HotkeyAction.ShowMETARReport);
                            break;
                        case HOTKEY_PFD:
                            TriggerHotkey(HotkeyAction.ShowPFD);
                            break;
                        case HOTKEY_LOCATION_INFO:
                            TriggerHotkey(HotkeyAction.LocationInfo);
                            break;
                        case HOTKEY_SIMBRIEF_BRIEFING:
                            TriggerHotkey(HotkeyAction.SimBriefBriefing);
                            break;
                        case HOTKEY_DISTANCE_TO_DEST:
                            TriggerHotkey(HotkeyAction.ReadDistanceToDest);
                            break;
                        case HOTKEY_DISTANCE_TO_TOD:
                            TriggerHotkey(HotkeyAction.ReadDistanceToTOD);
                            break;
                        case HOTKEY_NAV_RADIO_INFO:
                            TriggerHotkey(HotkeyAction.ReadNavRadioInfo);
                            break;
                        case HOTKEY_APPROACH_CAPABILITY:
                            TriggerHotkey(HotkeyAction.ReadApproachCapability);
                            break;
                        case HOTKEY_SPEED_GD:
                            TriggerHotkey(HotkeyAction.ReadSpeedGD);
                            break;
                        case HOTKEY_SPEED_S:
                            TriggerHotkey(HotkeyAction.ReadSpeedS);
                            break;
                        case HOTKEY_SPEED_F:
                            TriggerHotkey(HotkeyAction.ReadSpeedF);
                            break;
                        case HOTKEY_SPEED_VFE:
                            TriggerHotkey(HotkeyAction.ReadSpeedVFE);
                            break;
                        case HOTKEY_SPEED_VLS:
                            TriggerHotkey(HotkeyAction.ReadSpeedVLS);
                            break;
                        case HOTKEY_SPEED_VS:
                            TriggerHotkey(HotkeyAction.ReadSpeedVS);
                            break;
                        case HOTKEY_CHECKLIST:
                            TriggerHotkey(HotkeyAction.ShowChecklist);
                            break;
                        case HOTKEY_FUEL_QUANTITY:
                            TriggerHotkey(HotkeyAction.ReadFuelQuantity);
                            break;
                        case HOTKEY_FLAPS:
                            TriggerHotkey(HotkeyAction.ReadFlaps);
                            break;
                        case HOTKEY_GEAR:
                            TriggerHotkey(HotkeyAction.ReadGear);
                            break;
                        case HOTKEY_ALTIMETER:
                            TriggerHotkey(HotkeyAction.ReadAltimeter);
                            break;
                        case HOTKEY_GROSS_WEIGHT_KG:
                            TriggerHotkey(HotkeyAction.ReadGrossWeightKg);
                            break;
                        case HOTKEY_NAV_DISPLAY:
                            TriggerHotkey(HotkeyAction.ShowNavigationDisplay);
                            break;
                        case HOTKEY_WAYPOINT_INFO:
                            TriggerHotkey(HotkeyAction.ReadWaypointInfo);
                            break;
                        case HOTKEY_ECAM_DISPLAY:
                            TriggerHotkey(HotkeyAction.ShowECAM);
                            break;
                        case HOTKEY_STATUS_DISPLAY:
                            TriggerHotkey(HotkeyAction.ShowStatusPage);
                            break;
                        case HOTKEY_TOGGLE_TRIM:
                            TriggerHotkey(HotkeyAction.ToggleTrimAnnouncements);
                            break;
                        case HOTKEY_TAKEOFF_ASSIST:
                            TriggerHotkey(HotkeyAction.ToggleTakeoffAssist);
                            break;
                        case HOTKEY_TOGGLE_ECAM_MONITORING:
                            TriggerHotkey(HotkeyAction.ToggleECAMMonitoring);
                            break;
                        case HOTKEY_MONITOR_MANAGER:
                            TriggerHotkey(HotkeyAction.MonitorManager);
                            break;
                        case HOTKEY_HAND_FLY_MODE:
                            // Deactivate output mode BEFORE triggering hand fly mode to avoid race condition
                            // Hand fly mode needs to register its own hotkeys, which fails if output mode is still active
                            DeactivateOutputHotkeyMode();
                            TriggerHotkey(HotkeyAction.ToggleHandFlyMode);
                            return true;  // Return immediately, mode already deactivated
                        case HOTKEY_VISUAL_GUIDANCE:
                            // Visual guidance now registers the same quick-access keys HandFly
                            // does (H/V/Q/S/D/B/P/A/F), and RegisterVisualGuidanceHotkeys is
                            // gated on `!outputHotkeyModeActive`. If we don't deactivate output
                            // mode before triggering, RegisterVisualGuidanceHotkeys silently
                            // returns false and the user has VG running with NO quick-access
                            // hotkeys. Same fix pattern as HOTKEY_HAND_FLY_MODE above.
                            DeactivateOutputHotkeyMode();
                            TriggerHotkey(HotkeyAction.ToggleVisualGuidance);
                            return true;
                        case HOTKEY_EFB:
                            TriggerHotkey(HotkeyAction.ShowElectronicFlightBag);
                            break;
                        case HOTKEY_TRACK_SLOT_1:
                            TriggerHotkey(HotkeyAction.ReadTrackSlot1);
                            break;
                        case HOTKEY_TRACK_SLOT_2:
                            TriggerHotkey(HotkeyAction.ReadTrackSlot2);
                            break;
                        case HOTKEY_TRACK_SLOT_3:
                            TriggerHotkey(HotkeyAction.ReadTrackSlot3);
                            break;
                        case HOTKEY_TRACK_SLOT_4:
                            TriggerHotkey(HotkeyAction.ReadTrackSlot4);
                            break;
                        case HOTKEY_TRACK_SLOT_5:
                            TriggerHotkey(HotkeyAction.ReadTrackSlot5);
                            break;
                        case HOTKEY_FUEL_PAYLOAD:
                            TriggerHotkey(HotkeyAction.ReadFuelInfo);
                            break;
                        case HOTKEY_READ_DISPLAY_UPPER_ECAM:
                            TriggerHotkey(HotkeyAction.ReadDisplayUpperECAM);
                            break;
                        case HOTKEY_READ_DISPLAY_LOWER_ECAM:
                            TriggerHotkey(HotkeyAction.ReadDisplayLowerECAM);
                            break;
                        case HOTKEY_READ_DISPLAY_ND:
                            TriggerHotkey(HotkeyAction.ReadDisplayND);
                            break;
                        case HOTKEY_READ_DISPLAY_PFD:
                            TriggerHotkey(HotkeyAction.ReadDisplayPFD);
                            break;
                        case HOTKEY_READ_DISPLAY_ISIS:
                            TriggerHotkey(HotkeyAction.ReadDisplayISIS);
                            break;
                        case HOTKEY_DESCRIBE_SCENE:
                            TriggerHotkey(HotkeyAction.DescribeScene);
                            break;
                        case HOTKEY_NEAREST_CITY:
                            TriggerHotkey(HotkeyAction.ReadNearestCity);
                            break;
                        case HOTKEY_TCAS_ANNOUNCE:
                            TriggerHotkey(HotkeyAction.AnnounceTcasTraffic);
                            break;
                        case HOTKEY_TCAS_WINDOW:
                            TriggerHotkey(HotkeyAction.ShowTcasWindow);
                            break;
                        case HOTKEY_WEATHER_RADAR:
                            TriggerHotkey(HotkeyAction.ShowWeatherRadar);
                            break;
                        case HOTKEY_OUTSIDE_TEMP:
                            TriggerHotkey(HotkeyAction.ReadOutsideTemperature);
                            break;
                        case HOTKEY_SQUAWK_CODE:
                            TriggerHotkey(HotkeyAction.ReadSquawkCode);
                            break;
                        case HOTKEY_TAXI_STATUS:
                            TriggerHotkey(HotkeyAction.TaxiStatus);
                            break;
                        case HOTKEY_TAXI_REPEAT:
                            TriggerHotkey(HotkeyAction.TaxiRepeat);
                            break;
                        case HOTKEY_TAXI_WHERE_AM_I:
                            TriggerHotkey(HotkeyAction.TaxiWhereAmI);
                            break;
                        case HOTKEY_GROUND_TRAFFIC:
                            TriggerHotkey(HotkeyAction.AnnounceGroundTraffic);
                            break;
                        case HOTKEY_READ_GSX_TOOLTIP:
                            TriggerHotkey(HotkeyAction.ReadGsxTooltip);
                            break;
                    }
                    DeactivateOutputHotkeyMode();
                    return true;
                }
                else if (inputHotkeyModeActive)
                {
                    switch (hotkeyId)
                    {
                        case HOTKEY_RUNWAY_TELEPORT:
                            TriggerHotkey(HotkeyAction.RunwayTeleport);
                            break;
                        case HOTKEY_GATE_TELEPORT:
                            TriggerHotkey(HotkeyAction.GateTeleport);
                            break;
                        case HOTKEY_DESTINATION_RUNWAY:
                            TriggerHotkey(HotkeyAction.SelectDestinationRunway);
                            break;
                        case HOTKEY_TOGGLE_AP1:
                            TriggerHotkey(HotkeyAction.ToggleAutopilot1);
                            break;
                        case HOTKEY_TOGGLE_APPR:
                            TriggerHotkey(HotkeyAction.ToggleApproachMode);
                            break;
                        case HOTKEY_FCU_HDG_PUSH:
                            TriggerHotkey(HotkeyAction.FCUHeadingPush);
                            break;
                        case HOTKEY_FCU_HDG_PULL:
                            TriggerHotkey(HotkeyAction.FCUHeadingPull);
                            break;
                        case HOTKEY_FCU_ALT_PUSH:
                            TriggerHotkey(HotkeyAction.FCUAltitudePush);
                            break;
                        case HOTKEY_FCU_ALT_PULL:
                            TriggerHotkey(HotkeyAction.FCUAltitudePull);
                            break;
                        case HOTKEY_FCU_SPD_PUSH:
                            TriggerHotkey(HotkeyAction.FCUSpeedPush);
                            break;
                        case HOTKEY_FCU_SPD_PULL:
                            TriggerHotkey(HotkeyAction.FCUSpeedPull);
                            break;
                        case HOTKEY_FCU_VS_PUSH:
                            TriggerHotkey(HotkeyAction.FCUVSPush);
                            break;
                        case HOTKEY_FCU_VS_PULL:
                            TriggerHotkey(HotkeyAction.FCUVSPull);
                            break;
                        case HOTKEY_FCU_SET_HEADING:
                            TriggerHotkey(HotkeyAction.FCUSetHeading);
                            break;
                        case HOTKEY_FCU_SET_SPEED:
                            TriggerHotkey(HotkeyAction.FCUSetSpeed);
                            break;
                        case HOTKEY_FCU_SET_ALTITUDE:
                            TriggerHotkey(HotkeyAction.FCUSetAltitude);
                            break;
                        case HOTKEY_FCU_SET_VS:
                            TriggerHotkey(HotkeyAction.FCUSetVS);
                            break;
                        case HOTKEY_FCU_SET_AUTOPILOT:
                            TriggerHotkey(HotkeyAction.FCUSetAutopilot);
                            break;
                        case HOTKEY_FCU_SET_BARO:
                            TriggerHotkey(HotkeyAction.FCUSetBaro);
                            break;
                        case HOTKEY_NAV_RADIOS_SET:
                            TriggerHotkey(HotkeyAction.SetNavRadios);
                            break;
                        case HOTKEY_TOGGLE_AP2:
                            TriggerHotkey(HotkeyAction.ToggleAutopilot2);
                            break;
                        case HOTKEY_TRACK_FIX:
                            TriggerHotkey(HotkeyAction.ShowTrackFixWindow);
                            break;
                        case HOTKEY_FENIX_MCDU:
                            TriggerHotkey(HotkeyAction.ShowFenixMCDU);
                            break;
                        case HOTKEY_PMDG_EFB:
                            TriggerHotkey(HotkeyAction.ShowPMDGEFB);
                            break;
                        case HOTKEY_TAXI_FORM:
                            TriggerHotkey(HotkeyAction.TaxiAssistForm);
                            break;
                        case HOTKEY_TAXI_CONTINUE:
                            TriggerHotkey(HotkeyAction.TaxiContinue);
                            break;
                        case HOTKEY_TAXI_STOP:
                            TriggerHotkey(HotkeyAction.TaxiStop);
                            break;
                        case HOTKEY_LANDING_EXIT:
                            TriggerHotkey(HotkeyAction.LandingExitPlanner);
                            break;
                        case HOTKEY_ACCESS_GSX:
                            TriggerHotkey(HotkeyAction.ShowAccessGSX);
                            break;
                    }
                    DeactivateInputHotkeyMode();
                    return true;
                }
                else if (handFlyHotkeysActive || visualGuidanceHotkeysActive)
                {
                    // Unified quick-access dispatch. The same H/V/Q/S/D/B/P/A/F keys serve
                    // both HandFly and visual guidance — they're "things a pilot wants to
                    // query while hand-flying", and VG implies hand-flying with extra audio.
                    // Registration is reference-counted (see AcquireQuickAccessHotkeys), so
                    // either mode by itself or both active produces the same dispatch path.
                    System.Diagnostics.Debug.WriteLine($"Quick-access hotkey: Received WM_HOTKEY id={hotkeyId} (handFly={handFlyHotkeysActive}, vg={visualGuidanceHotkeysActive})");
                    switch (hotkeyId)
                    {
                        case HOTKEY_HANDFLY_HEADING:
                            TriggerHotkey(HotkeyAction.ReadHeadingMagnetic);
                            break;
                        case HOTKEY_HANDFLY_VERTICAL_SPEED:
                            TriggerHotkey(HotkeyAction.ReadVerticalSpeed);
                            break;
                        case HOTKEY_HANDFLY_ALTITUDE_AGL:
                            TriggerHotkey(HotkeyAction.ReadAltitudeAGL);
                            break;
                        case HOTKEY_HANDFLY_SPEED:
                            TriggerHotkey(HotkeyAction.ReadAirspeedIndicated);
                            break;
                        case HOTKEY_HANDFLY_RUNWAY_DISTANCE:
                            TriggerHotkey(HotkeyAction.ReadDestinationRunwayDistance);
                            break;
                        case HOTKEY_HANDFLY_BANK_ANGLE:
                            TriggerHotkey(HotkeyAction.ReadBankAngle);
                            break;
                        case HOTKEY_HANDFLY_PITCH:
                            TriggerHotkey(HotkeyAction.ReadPitch);
                            break;
                        case HOTKEY_HANDFLY_ALTITUDE_MSL:
                            TriggerHotkey(HotkeyAction.ReadAltitudeMSL);
                            break;
                        case HOTKEY_VISUAL_TARGET_FPM:
                            // The action handler self-gates on VG.IsActive — when only HandFly
                            // is up and the user presses F, they get "Visual guidance not active".
                            TriggerHotkey(HotkeyAction.ReadTargetFPM);
                            break;
                        default:
                            System.Diagnostics.Debug.WriteLine($"Quick-access hotkey: Unknown hotkey ID {hotkeyId}");
                            break;
                    }
                    return true;
                }
            }

            return false;
        }

        private void ActivateOutputHotkeyMode()
        {
            outputHotkeyModeActive = true;

            // Register the temporary hotkeys
            RegisterHotKey(windowHandle, HOTKEY_HEADING, MOD_SHIFT, 0x48); // Shift+H (FCU Heading)
            RegisterHotKey(windowHandle, HOTKEY_SPEED, MOD_SHIFT, 0x53);   // Shift+S (FCU Speed)
            RegisterHotKey(windowHandle, HOTKEY_ALTITUDE, MOD_SHIFT, 0x41); // Shift+A (FCU Altitude)
            RegisterHotKey(windowHandle, HOTKEY_FCU_VSFPA, MOD_SHIFT, 0x56); // Shift+V (FCU VS/FPA)

            // Register new hotkeys without modifiers
            // Skip H, V, Q, S, A, D if hand fly hotkeys are active (to prevent conflicts)
            if (!handFlyHotkeysActive)
            {
                RegisterHotKey(windowHandle, HOTKEY_ALTITUDE_AGL, MOD_NONE, 0x51); // Q (Altitude AGL)
                RegisterHotKey(windowHandle, HOTKEY_VERTICAL_SPEED, MOD_NONE, 0x56); // V (Vertical Speed)
                RegisterHotKey(windowHandle, HOTKEY_HEADING_MAGNETIC, MOD_NONE, 0x48); // H (Magnetic Heading)
                RegisterHotKey(windowHandle, HOTKEY_AIRSPEED_IND, MOD_NONE, 0x53); // S (Airspeed Indicated)
                RegisterHotKey(windowHandle, HOTKEY_ALTITUDE_MSL, MOD_NONE, 0x41); // A (Altitude MSL)
                RegisterHotKey(windowHandle, HOTKEY_DISTANCE_TO_DEST, MOD_NONE, 0x44); // D (Distance to Destination)
            }
            RegisterHotKey(windowHandle, HOTKEY_AIRSPEED_TRUE, MOD_NONE, 0x54); // T (Airspeed True)
            RegisterHotKey(windowHandle, HOTKEY_GROUND_SPEED, MOD_NONE, 0x47); // G (Ground Speed)
            RegisterHotKey(windowHandle, HOTKEY_MACH_SPEED, MOD_NONE, 0x4D); // M (Mach Speed)
            RegisterHotKey(windowHandle, HOTKEY_HEADING_TRUE, MOD_NONE, 0x55); // U (True Heading)
            RegisterHotKey(windowHandle, HOTKEY_DESTINATION_RUNWAY_DISTANCE, MOD_CONTROL, 0x44); // Ctrl+D (Distance to Destination Runway)
            RegisterHotKey(windowHandle, HOTKEY_ILS_GUIDANCE, MOD_CONTROL, 0x49); // Ctrl+I (ILS Guidance)
            RegisterHotKey(windowHandle, HOTKEY_LOCATION_INFO, MOD_SHIFT, 0x4C);   // Shift+L (Location Info)
            RegisterHotKey(windowHandle, HOTKEY_WIND_INFO, MOD_NONE, 0x49); // I (Wind Info)
            RegisterHotKey(windowHandle, HOTKEY_NAV_RADIO_INFO, MOD_NONE, 0x4E); // N (NAV Radio Info)
            RegisterHotKey(windowHandle, HOTKEY_METAR_REPORT, MOD_SHIFT, 0x4D); // Shift+M (METAR Report)
            RegisterHotKey(windowHandle, HOTKEY_PFD, MOD_SHIFT, 0x50); // Shift+P (PFD Window)
            RegisterHotKey(windowHandle, HOTKEY_SIMBRIEF_BRIEFING, MOD_SHIFT, 0x42); // Shift+B (SimBrief Briefing)
            RegisterHotKey(windowHandle, HOTKEY_DISTANCE_TO_TOD, MOD_SHIFT, 0x44); // Shift+D (Distance to TOD)
            RegisterHotKey(windowHandle, HOTKEY_APPROACH_CAPABILITY, MOD_CONTROL, 0x30); // Ctrl+0 (Approach Capability)

            // Register speed tape hotkeys
            RegisterHotKey(windowHandle, HOTKEY_SPEED_GD, MOD_SHIFT, 0x31);      // Shift+1 (O Speed)
            RegisterHotKey(windowHandle, HOTKEY_SPEED_S, MOD_SHIFT, 0x32);       // Shift+2 (S-Speed)
            RegisterHotKey(windowHandle, HOTKEY_SPEED_F, MOD_SHIFT, 0x33);       // Shift+3 (F-Speed)
            RegisterHotKey(windowHandle, HOTKEY_SPEED_VLS, MOD_SHIFT, 0x34);     // Shift+4 (Minimum Selectable Speed)
            RegisterHotKey(windowHandle, HOTKEY_SPEED_VS, MOD_SHIFT, 0x35);      // Shift+5 (Stall Speed)
            RegisterHotKey(windowHandle, HOTKEY_SPEED_VFE, MOD_SHIFT, 0x36);     // Shift+6 (V FE Speed)
            RegisterHotKey(windowHandle, HOTKEY_CHECKLIST, MOD_SHIFT, 0x43);     // Shift+C (Checklist Window)
            RegisterHotKey(windowHandle, HOTKEY_FUEL_QUANTITY, MOD_NONE, 0x46);  // F (Fuel Quantity)
            RegisterHotKey(windowHandle, HOTKEY_FLAPS, MOD_NONE, 0x4C);          // L (Flaps)
            RegisterHotKey(windowHandle, HOTKEY_GEAR, MOD_SHIFT, 0x47);          // Shift+G (Gear)
            RegisterHotKey(windowHandle, HOTKEY_ALTIMETER, MOD_NONE, 0x42);      // B (Altimeter)
            RegisterHotKey(windowHandle, HOTKEY_GROSS_WEIGHT_KG, MOD_SHIFT, 0x57); // Shift+W (Gross Weight KG)
            RegisterHotKey(windowHandle, HOTKEY_NAV_DISPLAY, MOD_SHIFT, 0x4E);    // Shift+N (Navigation Display)
            RegisterHotKey(windowHandle, HOTKEY_WAYPOINT_INFO, MOD_NONE, 0x57);  // W (Waypoint Info)
            RegisterHotKey(windowHandle, HOTKEY_ECAM_DISPLAY, MOD_SHIFT, 0x55);  // Shift+U (ECAM Display)
            RegisterHotKey(windowHandle, HOTKEY_STATUS_DISPLAY, MOD_SHIFT, 0x59); // Shift+Y (STATUS Display)
            RegisterHotKey(windowHandle, HOTKEY_TOGGLE_TRIM, MOD_SHIFT, 0x54);   // Shift+T (Toggle Trim Announcements)
            RegisterHotKey(windowHandle, HOTKEY_TAKEOFF_ASSIST, MOD_CONTROL, 0x54); // Ctrl+T (Takeoff Assist)
            RegisterHotKey(windowHandle, HOTKEY_TOGGLE_ECAM_MONITORING, MOD_CONTROL, 0x45); // Ctrl+E (Toggle ECAM Monitoring)
            RegisterHotKey(windowHandle, HOTKEY_MONITOR_MANAGER, MOD_CONTROL, 0x4D); // Ctrl+M (Monitor Manager - per-aircraft)
            RegisterHotKey(windowHandle, HOTKEY_HAND_FLY_MODE, MOD_CONTROL, 0x48); // Ctrl+H (Hand Fly Mode)
            RegisterHotKey(windowHandle, HOTKEY_VISUAL_GUIDANCE, MOD_CONTROL, 0x56); // Ctrl+V (Visual Guidance)
            RegisterHotKey(windowHandle, HOTKEY_EFB, MOD_SHIFT, 0x45); // Shift+E (Electronic Flight Bag)
            RegisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_1, MOD_NONE, 0x31);  // 1 (Track Slot 1)
            RegisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_2, MOD_NONE, 0x32);  // 2 (Track Slot 2)
            RegisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_3, MOD_NONE, 0x33);  // 3 (Track Slot 3)
            RegisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_4, MOD_NONE, 0x34);  // 4 (Track Slot 4)
            RegisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_5, MOD_NONE, 0x35);  // 5 (Track Slot 5)
            RegisterHotKey(windowHandle, HOTKEY_FUEL_PAYLOAD, MOD_SHIFT, 0x46); // Shift+F (Fuel & Payload)

            // Register display reading hotkeys for Fenix A320
            RegisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_UPPER_ECAM, MOD_ALT, 0x45);  // Alt+E (Read E/WD)
            RegisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_LOWER_ECAM, MOD_ALT, 0x53);  // Alt+S (Read SD)
            RegisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_ND, MOD_ALT, 0x4E);          // Alt+N (Read ND)
            RegisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_PFD, MOD_ALT, 0x50);         // Alt+P (Read PFD)
            RegisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_ISIS, MOD_ALT, 0x49);        // Alt+I (Read ISIS)
            RegisterHotKey(windowHandle, HOTKEY_DESCRIBE_SCENE, MOD_ALT, 0x44);           // Alt+D (Describe Scene)
            RegisterHotKey(windowHandle, HOTKEY_NEAREST_CITY, MOD_NONE, 0x43);             // C (Nearest City)
            RegisterHotKey(windowHandle, HOTKEY_TCAS_ANNOUNCE, MOD_NONE, 0x52);            // R (Announce Tracked TCAS Traffic)
            RegisterHotKey(windowHandle, HOTKEY_TCAS_WINDOW, MOD_CONTROL, 0x52);           // Ctrl+R (TCAS Traffic Window)
            RegisterHotKey(windowHandle, HOTKEY_WEATHER_RADAR, MOD_SHIFT, 0x52);           // Shift+R (Weather Radar Window)
            RegisterHotKey(windowHandle, HOTKEY_OUTSIDE_TEMP, MOD_NONE, 0x4F);             // O (Outside Temperature)
            RegisterHotKey(windowHandle, HOTKEY_SQUAWK_CODE, MOD_NONE, 0x58);             // X (Squawk Code)

            // Time-of-day hotkeys (Output mode).
            //   Z       → local time at aircraft position (sim's LOCAL TIME SimVar)
            //   Shift+Z → Zulu / UTC time (sim's ZULU TIME SimVar)
            // HH:MM by default; HH:MM:SS when UserSettings.AnnounceTimeWithSeconds is on.
            // VK code 0x5A = 'Z'. Both registrations use the same key with
            // different modifier flags so they coexist without collision.
            RegisterHotKey(windowHandle, HOTKEY_LOCAL_TIME, MOD_NONE, 0x5A);              // Z (Local Time)
            RegisterHotKey(windowHandle, HOTKEY_ZULU_TIME, MOD_SHIFT, 0x5A);              // Shift+Z (Zulu Time)

            // Taxi guidance hotkeys (Output mode)
            RegisterHotKey(windowHandle, HOTKEY_TAXI_STATUS, MOD_NONE, 0x59);             // Y (Taxi Status)
            RegisterHotKey(windowHandle, HOTKEY_TAXI_REPEAT, MOD_CONTROL, 0x59);          // Ctrl+Y (Repeat Instruction)
            // Where Am I lives on Alt+Y (output). Shift+Y is already taken by HOTKEY_STATUS_DISPLAY
            // earlier in this same activation block — Win32 RegisterHotKey silently rejects the
            // second registration of the same chord, which made Where Am I a dead key. We can't
            // give up Shift+Y for STATUS Display (it's a long-standing FBW/Fenix display hotkey),
            // so Where Am I moves to Alt+Y. Stays on the same physical key — easy to remember.
            RegisterHotKey(windowHandle, HOTKEY_TAXI_WHERE_AM_I, MOD_ALT, 0x59);          // Alt+Y (Where Am I)
            RegisterHotKey(windowHandle, HOTKEY_GROUND_TRAFFIC, MOD_ALT, 0x47);           // Alt+G (Nearest ground traffic)
            RegisterHotKey(windowHandle, HOTKEY_READ_GSX_TOOLTIP, MOD_CONTROL, 0x47);     // Ctrl+G (Read latest GSX tooltip)

            // Auto-timeout disabled - hotkey mode stays active until used or escape pressed

            OutputHotkeyModeChanged?.Invoke(this, new HotkeyModeEventArgs(HotkeyModeStatus.Activated));
        }

        private void DeactivateOutputHotkeyMode(bool wasCancelled = false)
        {
            if (!outputHotkeyModeActive) return;

            outputHotkeyModeActive = false;

            // Unregister the temporary hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_HEADING);
            UnregisterHotKey(windowHandle, HOTKEY_SPEED);
            UnregisterHotKey(windowHandle, HOTKEY_ALTITUDE);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_VSFPA);
            // Only unregister H, V, Q, S, A if hand fly hotkeys aren't active
            if (!handFlyHotkeysActive)
            {
                UnregisterHotKey(windowHandle, HOTKEY_ALTITUDE_AGL);
                UnregisterHotKey(windowHandle, HOTKEY_VERTICAL_SPEED);
                UnregisterHotKey(windowHandle, HOTKEY_HEADING_MAGNETIC);
                UnregisterHotKey(windowHandle, HOTKEY_AIRSPEED_IND);
                UnregisterHotKey(windowHandle, HOTKEY_ALTITUDE_MSL);
            }
            UnregisterHotKey(windowHandle, HOTKEY_AIRSPEED_TRUE);
            UnregisterHotKey(windowHandle, HOTKEY_GROUND_SPEED);
            UnregisterHotKey(windowHandle, HOTKEY_MACH_SPEED);
            UnregisterHotKey(windowHandle, HOTKEY_HEADING_TRUE);
            UnregisterHotKey(windowHandle, HOTKEY_DESTINATION_RUNWAY_DISTANCE);
            UnregisterHotKey(windowHandle, HOTKEY_ILS_GUIDANCE);
            UnregisterHotKey(windowHandle, HOTKEY_LOCATION_INFO);
            UnregisterHotKey(windowHandle, HOTKEY_WIND_INFO);
            UnregisterHotKey(windowHandle, HOTKEY_METAR_REPORT);
            UnregisterHotKey(windowHandle, HOTKEY_PFD);
            UnregisterHotKey(windowHandle, HOTKEY_SIMBRIEF_BRIEFING);
            UnregisterHotKey(windowHandle, HOTKEY_DISTANCE_TO_DEST);
            UnregisterHotKey(windowHandle, HOTKEY_DISTANCE_TO_TOD);
            UnregisterHotKey(windowHandle, HOTKEY_NAV_RADIO_INFO);
            UnregisterHotKey(windowHandle, HOTKEY_APPROACH_CAPABILITY);

            // Unregister speed tape hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_SPEED_GD);
            UnregisterHotKey(windowHandle, HOTKEY_SPEED_S);
            UnregisterHotKey(windowHandle, HOTKEY_SPEED_F);
            UnregisterHotKey(windowHandle, HOTKEY_SPEED_VLS);
            UnregisterHotKey(windowHandle, HOTKEY_SPEED_VS);
            UnregisterHotKey(windowHandle, HOTKEY_SPEED_VFE);
            UnregisterHotKey(windowHandle, HOTKEY_CHECKLIST);
            UnregisterHotKey(windowHandle, HOTKEY_FUEL_QUANTITY);
            UnregisterHotKey(windowHandle, HOTKEY_FLAPS);
            UnregisterHotKey(windowHandle, HOTKEY_GEAR);
            UnregisterHotKey(windowHandle, HOTKEY_ALTIMETER);
            UnregisterHotKey(windowHandle, HOTKEY_GROSS_WEIGHT_KG);
            UnregisterHotKey(windowHandle, HOTKEY_NAV_DISPLAY);
            UnregisterHotKey(windowHandle, HOTKEY_WAYPOINT_INFO);
            UnregisterHotKey(windowHandle, HOTKEY_ECAM_DISPLAY);
            UnregisterHotKey(windowHandle, HOTKEY_STATUS_DISPLAY);
            UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_TRIM);
            UnregisterHotKey(windowHandle, HOTKEY_TAKEOFF_ASSIST);
            UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_ECAM_MONITORING);
            UnregisterHotKey(windowHandle, HOTKEY_MONITOR_MANAGER);
            UnregisterHotKey(windowHandle, HOTKEY_HAND_FLY_MODE);
            UnregisterHotKey(windowHandle, HOTKEY_VISUAL_GUIDANCE);
            UnregisterHotKey(windowHandle, HOTKEY_EFB);
            UnregisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_1);
            UnregisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_2);
            UnregisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_3);
            UnregisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_4);
            UnregisterHotKey(windowHandle, HOTKEY_TRACK_SLOT_5);
            UnregisterHotKey(windowHandle, HOTKEY_FUEL_PAYLOAD);

            // Unregister display reading hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_UPPER_ECAM);
            UnregisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_LOWER_ECAM);
            UnregisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_ND);
            UnregisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_PFD);
            UnregisterHotKey(windowHandle, HOTKEY_READ_DISPLAY_ISIS);
            UnregisterHotKey(windowHandle, HOTKEY_DESCRIBE_SCENE);
            UnregisterHotKey(windowHandle, HOTKEY_NEAREST_CITY);
            UnregisterHotKey(windowHandle, HOTKEY_TCAS_ANNOUNCE);
            UnregisterHotKey(windowHandle, HOTKEY_TCAS_WINDOW);
            UnregisterHotKey(windowHandle, HOTKEY_WEATHER_RADAR);
            UnregisterHotKey(windowHandle, HOTKEY_OUTSIDE_TEMP);
            UnregisterHotKey(windowHandle, HOTKEY_SQUAWK_CODE);

            // Time-of-day hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_LOCAL_TIME);
            UnregisterHotKey(windowHandle, HOTKEY_ZULU_TIME);

            // Taxi guidance hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_TAXI_STATUS);
            UnregisterHotKey(windowHandle, HOTKEY_TAXI_REPEAT);
            UnregisterHotKey(windowHandle, HOTKEY_TAXI_WHERE_AM_I);
            UnregisterHotKey(windowHandle, HOTKEY_GROUND_TRAFFIC);
            UnregisterHotKey(windowHandle, HOTKEY_READ_GSX_TOOLTIP);

            OutputHotkeyModeChanged?.Invoke(this, new HotkeyModeEventArgs(wasCancelled ? HotkeyModeStatus.Cancelled : HotkeyModeStatus.Deactivated));
        }

        private void ActivateInputHotkeyMode()
        {
            inputHotkeyModeActive = true;

            // Register input mode hotkeys
            RegisterHotKey(windowHandle, HOTKEY_RUNWAY_TELEPORT, MOD_SHIFT, 0x52);   // Shift+R (Runway Teleport)
            RegisterHotKey(windowHandle, HOTKEY_GATE_TELEPORT, MOD_SHIFT, 0x47);     // Shift+G (Gate Teleport)
            RegisterHotKey(windowHandle, HOTKEY_DESTINATION_RUNWAY, MOD_SHIFT, 0x44); // Shift+D (Destination Runway)
            RegisterHotKey(windowHandle, HOTKEY_TOGGLE_AP1, MOD_SHIFT, 0x41);        // Shift+A (Toggle Autopilot 1)
            RegisterHotKey(windowHandle, HOTKEY_TOGGLE_APPR, MOD_SHIFT, 0x50);       // Shift+P (Toggle Approach Mode)

            // Register FCU push/pull hotkeys
            RegisterHotKey(windowHandle, HOTKEY_FCU_HDG_PUSH, MOD_SHIFT, 0x31);     // Shift+1 (Push Heading Knob)
            RegisterHotKey(windowHandle, HOTKEY_FCU_HDG_PULL, MOD_CONTROL, 0x31);   // Ctrl+1 (Pull Heading Knob)
            RegisterHotKey(windowHandle, HOTKEY_FCU_ALT_PUSH, MOD_SHIFT, 0x32);     // Shift+2 (Push Altitude Knob)
            RegisterHotKey(windowHandle, HOTKEY_FCU_ALT_PULL, MOD_CONTROL, 0x32);   // Ctrl+2 (Pull Altitude Knob)
            RegisterHotKey(windowHandle, HOTKEY_FCU_SPD_PUSH, MOD_SHIFT, 0x33);     // Shift+3 (Push Speed Knob)
            RegisterHotKey(windowHandle, HOTKEY_FCU_SPD_PULL, MOD_CONTROL, 0x33);   // Ctrl+3 (Pull Speed Knob)
            RegisterHotKey(windowHandle, HOTKEY_FCU_VS_PUSH, MOD_SHIFT, 0x34);      // Shift+4 (Push VS Knob)
            RegisterHotKey(windowHandle, HOTKEY_FCU_VS_PULL, MOD_CONTROL, 0x34);    // Ctrl+4 (Pull VS Knob)

            // Register FCU set value hotkeys
            RegisterHotKey(windowHandle, HOTKEY_FCU_SET_HEADING, MOD_CONTROL, 0x48); // Ctrl+H (Set Heading)
            RegisterHotKey(windowHandle, HOTKEY_FCU_SET_SPEED, MOD_CONTROL, 0x53);   // Ctrl+S (Set Speed)
            RegisterHotKey(windowHandle, HOTKEY_FCU_SET_ALTITUDE, MOD_CONTROL, 0x41); // Ctrl+A (Set Altitude)
            RegisterHotKey(windowHandle, HOTKEY_FCU_SET_VS, MOD_CONTROL, 0x56);      // Ctrl+V (Set VS)
            RegisterHotKey(windowHandle, HOTKEY_FCU_SET_AUTOPILOT, MOD_CONTROL, 0x50); // Ctrl+P (Set Autopilot)
            RegisterHotKey(windowHandle, HOTKEY_FCU_SET_BARO, MOD_CONTROL, 0x42);     // Ctrl+B (Set Baro)
            RegisterHotKey(windowHandle, HOTKEY_NAV_RADIOS_SET, MOD_CONTROL, 0x4E);   // Ctrl+N (Set NAV Radios)
            RegisterHotKey(windowHandle, HOTKEY_TOGGLE_AP2, MOD_CONTROL, 0x4F);      // Ctrl+O (Toggle Autopilot 2)
            RegisterHotKey(windowHandle, HOTKEY_TRACK_FIX, MOD_SHIFT, 0x46);         // Shift+F (Track Fix Window)
            RegisterHotKey(windowHandle, HOTKEY_FENIX_MCDU, MOD_SHIFT, 0x4D);       // Shift+M (Fenix MCDU)
            RegisterHotKey(windowHandle, HOTKEY_PMDG_EFB, MOD_SHIFT, 0x54);        // Shift+T (PMDG EFB Tablet)

            // Taxi guidance hotkeys (Input mode)
            RegisterHotKey(windowHandle, HOTKEY_TAXI_FORM, MOD_SHIFT, 0x59);            // Shift+Y (Open Taxi Form)
            RegisterHotKey(windowHandle, HOTKEY_TAXI_CONTINUE, MOD_NONE, 0x59);         // Y (Continue past hold-short)
            RegisterHotKey(windowHandle, HOTKEY_TAXI_STOP, MOD_CONTROL, 0x59);          // Ctrl+Y (Stop guidance)
            RegisterHotKey(windowHandle, HOTKEY_LANDING_EXIT, MOD_SHIFT, 0x58);         // Shift+X (Landing Exit Planner)

            // Access GSX hotkey (Input mode). Alt+G is free here — output mode
            // Alt+G is taken by Nearest Ground Traffic, but each mode has its
            // own registration set so they don't collide.
            RegisterHotKey(windowHandle, HOTKEY_ACCESS_GSX, MOD_ALT, 0x47);             // Alt+G (Open Access GSX window)

            InputHotkeyModeChanged?.Invoke(this, new HotkeyModeEventArgs(HotkeyModeStatus.Activated));
        }

        private void DeactivateInputHotkeyMode(bool wasCancelled = false)
        {
            if (!inputHotkeyModeActive) return;

            inputHotkeyModeActive = false;

            // Unregister input mode hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_RUNWAY_TELEPORT);
            UnregisterHotKey(windowHandle, HOTKEY_GATE_TELEPORT);
            UnregisterHotKey(windowHandle, HOTKEY_DESTINATION_RUNWAY);
            UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_AP1);
            UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_APPR);

            // Unregister FCU push/pull hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_FCU_HDG_PUSH);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_HDG_PULL);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_ALT_PUSH);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_ALT_PULL);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SPD_PUSH);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SPD_PULL);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_VS_PUSH);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_VS_PULL);

            // Unregister FCU set value hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SET_HEADING);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SET_SPEED);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SET_ALTITUDE);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SET_VS);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SET_AUTOPILOT);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_SET_BARO);
            UnregisterHotKey(windowHandle, HOTKEY_NAV_RADIOS_SET);
            UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_AP2);
            UnregisterHotKey(windowHandle, HOTKEY_TRACK_FIX);
            UnregisterHotKey(windowHandle, HOTKEY_FENIX_MCDU);
            UnregisterHotKey(windowHandle, HOTKEY_PMDG_EFB);

            // Taxi guidance hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_TAXI_FORM);
            UnregisterHotKey(windowHandle, HOTKEY_TAXI_CONTINUE);
            UnregisterHotKey(windowHandle, HOTKEY_TAXI_STOP);
            UnregisterHotKey(windowHandle, HOTKEY_LANDING_EXIT);

            // Access GSX (Input mode Alt+G).
            UnregisterHotKey(windowHandle, HOTKEY_ACCESS_GSX);

            InputHotkeyModeChanged?.Invoke(this, new HotkeyModeEventArgs(wasCancelled ? HotkeyModeStatus.Cancelled : HotkeyModeStatus.Deactivated));
        }

        private void TriggerHotkey(HotkeyAction action)
        {
            HotkeyTriggered?.Invoke(this, new HotkeyEventArgs { Action = action });
        }

        public bool ProcessKeyDown(Keys keyData)
        {
            // Handle Escape key to exit output hotkey mode or input hotkey mode
            if ((outputHotkeyModeActive || inputHotkeyModeActive) && keyData == Keys.Escape)
            {
                if (outputHotkeyModeActive)
                    DeactivateOutputHotkeyMode(wasCancelled: true);
                if (inputHotkeyModeActive)
                    DeactivateInputHotkeyMode(wasCancelled: true);
                return true;
            }

            return false;
        }

        public void ExitOutputHotkeyMode()
        {
            DeactivateOutputHotkeyMode();
        }

        public void ExitInputHotkeyMode()
        {
            DeactivateInputHotkeyMode();
        }

        /// <summary>
        /// Internal — register every quick-access key not yet held by us. Called by both
        /// HandFly and visual-guidance Register methods. Reference-counted via
        /// <see cref="quickAccessActiveModeCount"/>; the actual RegisterHotKey calls happen
        /// once and the keys persist as long as at least one mode is active. Per-key
        /// tracking + retry-on-each-acquire handles the case where Windows previously
        /// refused a key (some other app held it) but the key has since freed up.
        /// </summary>
        private bool AcquireQuickAccessHotkeys()
        {
            quickAccessActiveModeCount++;
            bool allOk = true;
            for (int i = 0; i < QuickAccessKeys.Length; i++)
            {
                if (!quickAccessKeyRegistered[i])
                {
                    bool ok = RegisterHotKey(windowHandle, QuickAccessKeys[i].id, MOD_NONE, QuickAccessKeys[i].vk);
                    quickAccessKeyRegistered[i] = ok;
                    if (!ok)
                    {
                        allOk = false;
                        System.Diagnostics.Debug.WriteLine($"Quick-access hotkey: failed to register {QuickAccessKeys[i].label} (id={QuickAccessKeys[i].id})");
                    }
                }
            }
            System.Diagnostics.Debug.WriteLine($"Quick-access hotkeys: refCount={quickAccessActiveModeCount}, allOk={allOk}");
            return allOk;
        }

        /// <summary>
        /// Internal — drop one mode's reference to the quick-access keys. If no mode is
        /// still active, releases every key we registered. Idempotent guard against
        /// over-release (refCount won't go negative).
        /// </summary>
        private void ReleaseQuickAccessHotkeys()
        {
            quickAccessActiveModeCount--;
            if (quickAccessActiveModeCount <= 0)
            {
                quickAccessActiveModeCount = 0;
                for (int i = 0; i < QuickAccessKeys.Length; i++)
                {
                    if (quickAccessKeyRegistered[i])
                    {
                        UnregisterHotKey(windowHandle, QuickAccessKeys[i].id);
                        quickAccessKeyRegistered[i] = false;
                    }
                }
                System.Diagnostics.Debug.WriteLine("Quick-access hotkeys: all keys released (no active modes)");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Quick-access hotkeys: refCount={quickAccessActiveModeCount} (other mode still active, keys kept)");
            }
        }

        /// <summary>
        /// Registers HandFly's quick-access hotkeys (H, V, Q, S, D, B, P, A, F).
        /// F (Target FPM) is included because pilots use HandFly during approaches too and
        /// it's confusing to lose F just because VG isn't yet active. If VG also activates,
        /// the keys stay registered exactly once via the shared acquire/release mechanism.
        /// </summary>
        public bool RegisterHandFlyHotkeys()
        {
            if (outputHotkeyModeActive || handFlyHotkeysActive)
            {
                System.Diagnostics.Debug.WriteLine("Hand fly hotkeys: Skipped registration (mode conflict or already active)");
                return false;
            }
            bool ok = AcquireQuickAccessHotkeys();
            handFlyHotkeysActive = true;  // mode flag; dispatch reads it to know which actions to fire
            System.Diagnostics.Debug.WriteLine($"Hand fly hotkeys: registered (allKeysOk={ok})");
            return ok;
        }

        /// <summary>
        /// Drops HandFly's claim on the quick-access keys. If visual guidance is also active,
        /// the keys stay registered (VG still needs them).
        /// </summary>
        public void UnregisterHandFlyHotkeys()
        {
            if (!handFlyHotkeysActive)
            {
                System.Diagnostics.Debug.WriteLine("Hand fly hotkeys: Unregister skipped (not active)");
                return;
            }
            handFlyHotkeysActive = false;
            ReleaseQuickAccessHotkeys();
            System.Diagnostics.Debug.WriteLine("Hand fly hotkeys: Unregistered successfully");
        }

        /// <summary>
        /// Registers visual guidance's quick-access hotkeys — the same H/V/Q/S/D/B/P/A/F set
        /// that HandFly uses. VG implies hand-flying (the pilot is matching tones to control
        /// pitch and bank manually), so all the same in-flight readouts apply. If HandFly is
        /// also active, the keys stay registered exactly once via the shared mechanism.
        /// </summary>
        public bool RegisterVisualGuidanceHotkeys()
        {
            if (outputHotkeyModeActive || visualGuidanceHotkeysActive)
            {
                System.Diagnostics.Debug.WriteLine("Visual guidance hotkeys: Skipped registration (mode conflict or already active)");
                return false;
            }
            bool ok = AcquireQuickAccessHotkeys();
            visualGuidanceHotkeysActive = true;
            System.Diagnostics.Debug.WriteLine($"Visual guidance hotkeys: registered (allKeysOk={ok})");
            return ok;
        }

        /// <summary>
        /// Unregisters visual guidance mode hotkeys
        /// </summary>
        public void UnregisterVisualGuidanceHotkeys()
        {
            if (!visualGuidanceHotkeysActive)
            {
                System.Diagnostics.Debug.WriteLine("Visual guidance hotkeys: Unregister skipped (not active)");
                return;
            }

            visualGuidanceHotkeysActive = false;
            ReleaseQuickAccessHotkeys();
            System.Diagnostics.Debug.WriteLine("Visual guidance hotkeys: Unregistered successfully");
        }

        public void Suspend()
        {
            if (suspended || disposed) return;

            if (outputHotkeyModeActive)
                DeactivateOutputHotkeyMode(wasCancelled: true);
            if (inputHotkeyModeActive)
                DeactivateInputHotkeyMode(wasCancelled: true);

            UnregisterHotKey(windowHandle, HOTKEY_ACTIVATE);
            UnregisterHotKey(windowHandle, HOTKEY_INPUT_ACTIVATE);
            suspended = true;
        }

        public bool Resume()
        {
            if (!suspended || disposed) return true;

            bool registered = RegisterHotKey(windowHandle, HOTKEY_ACTIVATE, MOD_NONE, VK_OEM_6);
            bool inputRegistered = RegisterHotKey(windowHandle, HOTKEY_INPUT_ACTIVATE, MOD_NONE, VK_OEM_4);
            suspended = false;

            if (!registered || !inputRegistered)
            {
                System.Diagnostics.Debug.WriteLine("Failed to re-register hotkeys after resume");
                return false;
            }
            return true;
        }

        public void Cleanup()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!disposed)
            {
                if (outputHotkeyModeActive)
                {
                    DeactivateOutputHotkeyMode();
                }

                if (inputHotkeyModeActive)
                {
                    DeactivateInputHotkeyMode();
                }

                if (handFlyHotkeysActive)
                {
                    UnregisterHandFlyHotkeys();
                }

                if (visualGuidanceHotkeysActive)
                {
                    UnregisterVisualGuidanceHotkeys();
                }

                // Unregister main hotkeys
                if (windowHandle != IntPtr.Zero)
                {
                    UnregisterHotKey(windowHandle, HOTKEY_ACTIVATE);
                    UnregisterHotKey(windowHandle, HOTKEY_INPUT_ACTIVATE);
                }

                disposed = true;
            }
        }
    }

    public enum HotkeyModeStatus { Activated, Deactivated, Cancelled }

    public class HotkeyModeEventArgs : EventArgs
    {
        public HotkeyModeStatus Status { get; }

        public HotkeyModeEventArgs(HotkeyModeStatus status) => Status = status;
    }

    public class HotkeyEventArgs : EventArgs
    {
        public HotkeyAction Action { get; set; }
    }

    public enum HotkeyAction
    {
        ReadHeading,
        ReadAltitude,
        ReadSpeed,
        ReadAltitudeAGL,
        ReadAltitudeMSL,
        ReadAirspeedIndicated,
        ReadAirspeedTrue,
        ReadGroundSpeed,
        ReadMachSpeed,
        ReadVerticalSpeed,
        ReadHeadingMagnetic,
        ReadHeadingTrue,
        ReadLocalTime,
        ReadZuluTime,
        SelectDestinationRunway,
        ReadDestinationRunwayDistance,
        ReadILSGuidance,
        ReadWindInfo,
        ShowMETARReport,
        RunwayTeleport,
        GateTeleport,
        LocationInfo,
        SimBriefBriefing,
        ShowPFD,
        ToggleAutopilot1,
        ToggleApproachMode,
        ReadFCUVerticalSpeedFPA,
        ReadApproachCapability,
        FCUHeadingPush,
        FCUHeadingPull,
        FCUAltitudePush,
        FCUAltitudePull,
        FCUSpeedPush,
        FCUSpeedPull,
        FCUVSPush,
        FCUVSPull,
        FCUSetHeading,
        FCUSetSpeed,
        FCUSetAltitude,
        FCUSetVS,
        FCUSetAutopilot,
        ToggleAutopilot2,
        ReadSpeedGD,
        ReadSpeedS,
        ReadSpeedF,
        ReadSpeedVFE,
        ReadSpeedVLS,
        ReadSpeedVS,
        ShowChecklist,
        ReadFuelQuantity,
        ReadFlaps,
        ReadGear,
        ReadAltimeter,
        FCUSetBaro,
        SetNavRadios,
        ReadGrossWeightKg,
        ShowNavigationDisplay,
        ReadWaypointInfo,
        ShowECAM,
        ShowStatusPage,
        ToggleTakeoffAssist,
        ToggleECAMMonitoring,
        MonitorManager,
        ToggleHandFlyMode,
        ToggleVisualGuidance,
        ShowElectronicFlightBag,
        ReadTrackSlot1,
        ReadTrackSlot2,
        ReadTrackSlot3,
        ReadTrackSlot4,
        ReadTrackSlot5,
        ReadFuelInfo,
        ReadDisplayPFD,
        ReadDisplayLowerECAM,
        ReadDisplayUpperECAM,
        ReadDisplayND,
        ReadDisplayISIS,
        DescribeScene,
        ShowTrackFixWindow,
        ReadBankAngle,
        ReadPitch,
        ReadTargetFPM,
        ShowFenixMCDU,
        ShowPMDGEFB,
        ReadNearestCity,
        ReadDistanceToTOD,
        ReadDistanceToDest,
        ToggleTrimAnnouncements,
        ReadNavRadioInfo,
        AnnounceTcasTraffic,
        ShowTcasWindow,
        ShowWeatherRadar,
        ReadOutsideTemperature,
        ReadSquawkCode,
        TaxiAssistForm,
        TaxiStatus,
        TaxiRepeat,
        TaxiContinue,
        TaxiStop,
        TaxiWhereAmI,
        LandingExitPlanner,
        AnnounceGroundTraffic,
        ShowAccessGSX,
        ReadGsxTooltip,
    }
