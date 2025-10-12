using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FBWBA.Hotkeys
{
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
        private const int HOTKEY_ILS_GUIDANCE = 9014;
        private const int HOTKEY_WIND_INFO = 9016;
        private const int HOTKEY_METAR_REPORT = 9017;
        private const int HOTKEY_VISUAL_APPROACH = 9015;
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
        private const int HOTKEY_TAKEOFF_ASSIST = 9058;
        private const int HOTKEY_TOGGLE_ECAM_MONITORING = 9059;

        private IntPtr windowHandle;
        private bool outputHotkeyModeActive = false;
        private bool inputHotkeyModeActive = false;
        private bool disposed = false;
        
        public event EventHandler<HotkeyEventArgs> HotkeyTriggered;
        public event EventHandler<bool> OutputHotkeyModeChanged;
        public event EventHandler<bool> InputHotkeyModeChanged;

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
                    if (!outputHotkeyModeActive && !inputHotkeyModeActive)
                    {
                        ActivateOutputHotkeyMode();
                    }
                    return true;
                }
                else if (hotkeyId == HOTKEY_INPUT_ACTIVATE)
                {
                    if (!inputHotkeyModeActive && !outputHotkeyModeActive)
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
                            TriggerHotkey(HotkeyAction.ReadAltitudeAGL);
                            break;
                        case HOTKEY_ALTITUDE_MSL:
                            TriggerHotkey(HotkeyAction.ReadAltitudeMSL);
                            break;
                        case HOTKEY_AIRSPEED_IND:
                            TriggerHotkey(HotkeyAction.ReadAirspeedIndicated);
                            break;
                        case HOTKEY_AIRSPEED_TRUE:
                            TriggerHotkey(HotkeyAction.ReadAirspeedTrue);
                            break;
                        case HOTKEY_GROUND_SPEED:
                            TriggerHotkey(HotkeyAction.ReadGroundSpeed);
                            break;
                        case HOTKEY_VERTICAL_SPEED:
                            TriggerHotkey(HotkeyAction.ReadVerticalSpeed);
                            break;
                        case HOTKEY_HEADING_MAGNETIC:
                            TriggerHotkey(HotkeyAction.ReadHeadingMagnetic);
                            break;
                        case HOTKEY_HEADING_TRUE:
                            TriggerHotkey(HotkeyAction.ReadHeadingTrue);
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
                        case HOTKEY_VISUAL_APPROACH:
                            TriggerHotkey(HotkeyAction.ToggleVisualApproach);
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
                        case HOTKEY_TAKEOFF_ASSIST:
                            TriggerHotkey(HotkeyAction.ToggleTakeoffAssist);
                            break;
                        case HOTKEY_TOGGLE_ECAM_MONITORING:
                            TriggerHotkey(HotkeyAction.ToggleECAMMonitoring);
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
                        case HOTKEY_TOGGLE_AP2:
                            TriggerHotkey(HotkeyAction.ToggleAutopilot2);
                            break;
                    }
                    DeactivateInputHotkeyMode();
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
            RegisterHotKey(windowHandle, HOTKEY_ALTITUDE_AGL, MOD_NONE, 0x51); // Q (Altitude AGL)
            RegisterHotKey(windowHandle, HOTKEY_ALTITUDE_MSL, MOD_NONE, 0x41); // A (Altitude MSL)
            RegisterHotKey(windowHandle, HOTKEY_AIRSPEED_IND, MOD_NONE, 0x53); // S (Airspeed Indicated)
            RegisterHotKey(windowHandle, HOTKEY_AIRSPEED_TRUE, MOD_NONE, 0x54); // T (Airspeed True)
            RegisterHotKey(windowHandle, HOTKEY_GROUND_SPEED, MOD_NONE, 0x47); // G (Ground Speed)
            RegisterHotKey(windowHandle, HOTKEY_VERTICAL_SPEED, MOD_NONE, 0x56); // V (Vertical Speed)
            RegisterHotKey(windowHandle, HOTKEY_HEADING_MAGNETIC, MOD_NONE, 0x48); // H (Magnetic Heading)
            RegisterHotKey(windowHandle, HOTKEY_HEADING_TRUE, MOD_NONE, 0x55); // U (True Heading)
            RegisterHotKey(windowHandle, HOTKEY_ILS_GUIDANCE, MOD_CONTROL, 0x49); // Ctrl+I (ILS Guidance)
            RegisterHotKey(windowHandle, HOTKEY_LOCATION_INFO, MOD_CONTROL, 0x4C);   // Ctrl+L (Location Info)
            RegisterHotKey(windowHandle, HOTKEY_WIND_INFO, MOD_NONE, 0x49); // I (Wind Info)
            RegisterHotKey(windowHandle, HOTKEY_METAR_REPORT, MOD_SHIFT, 0x49); // Shift+I (METAR Report)
            RegisterHotKey(windowHandle, HOTKEY_VISUAL_APPROACH, MOD_CONTROL, 0x56); // Ctrl+V (Visual Approach)
            RegisterHotKey(windowHandle, HOTKEY_PFD, MOD_CONTROL, 0x50); // Ctrl+P (PFD Window)
            RegisterHotKey(windowHandle, HOTKEY_SIMBRIEF_BRIEFING, MOD_SHIFT, 0x44); // Shift+D (SimBrief Briefing)
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
            RegisterHotKey(windowHandle, HOTKEY_NAV_DISPLAY, MOD_NONE, 0x4E);    // N (Navigation Display)
            RegisterHotKey(windowHandle, HOTKEY_WAYPOINT_INFO, MOD_NONE, 0x57);  // W (Waypoint Info)
            RegisterHotKey(windowHandle, HOTKEY_ECAM_DISPLAY, MOD_SHIFT, 0x55);  // Shift+U (ECAM Display)
            RegisterHotKey(windowHandle, HOTKEY_STATUS_DISPLAY, MOD_SHIFT, 0x54); // Shift+T (STATUS Display)
            RegisterHotKey(windowHandle, HOTKEY_TAKEOFF_ASSIST, MOD_CONTROL, 0x54); // Ctrl+T (Takeoff Assist)
            RegisterHotKey(windowHandle, HOTKEY_TOGGLE_ECAM_MONITORING, MOD_CONTROL, 0x45); // Ctrl+E (Toggle ECAM Monitoring)

            // Auto-timeout disabled - hotkey mode stays active until used or escape pressed
            
            OutputHotkeyModeChanged?.Invoke(this, true);
        }

        private void DeactivateOutputHotkeyMode()
        {
            if (!outputHotkeyModeActive) return;

            outputHotkeyModeActive = false;

            // Unregister the temporary hotkeys
            UnregisterHotKey(windowHandle, HOTKEY_HEADING);
            UnregisterHotKey(windowHandle, HOTKEY_SPEED);
            UnregisterHotKey(windowHandle, HOTKEY_ALTITUDE);
            UnregisterHotKey(windowHandle, HOTKEY_FCU_VSFPA);
            UnregisterHotKey(windowHandle, HOTKEY_ALTITUDE_AGL);
            UnregisterHotKey(windowHandle, HOTKEY_ALTITUDE_MSL);
            UnregisterHotKey(windowHandle, HOTKEY_AIRSPEED_IND);
            UnregisterHotKey(windowHandle, HOTKEY_AIRSPEED_TRUE);
            UnregisterHotKey(windowHandle, HOTKEY_GROUND_SPEED);
            UnregisterHotKey(windowHandle, HOTKEY_VERTICAL_SPEED);
            UnregisterHotKey(windowHandle, HOTKEY_HEADING_MAGNETIC);
            UnregisterHotKey(windowHandle, HOTKEY_HEADING_TRUE);
            UnregisterHotKey(windowHandle, HOTKEY_ILS_GUIDANCE);
            UnregisterHotKey(windowHandle, HOTKEY_LOCATION_INFO);
            UnregisterHotKey(windowHandle, HOTKEY_WIND_INFO);
            UnregisterHotKey(windowHandle, HOTKEY_METAR_REPORT);
            UnregisterHotKey(windowHandle, HOTKEY_VISUAL_APPROACH);
            UnregisterHotKey(windowHandle, HOTKEY_PFD);
            UnregisterHotKey(windowHandle, HOTKEY_SIMBRIEF_BRIEFING);
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
            UnregisterHotKey(windowHandle, HOTKEY_NAV_DISPLAY);
            UnregisterHotKey(windowHandle, HOTKEY_WAYPOINT_INFO);
            UnregisterHotKey(windowHandle, HOTKEY_ECAM_DISPLAY);
            UnregisterHotKey(windowHandle, HOTKEY_STATUS_DISPLAY);
            UnregisterHotKey(windowHandle, HOTKEY_TAKEOFF_ASSIST);
            UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_ECAM_MONITORING);

            OutputHotkeyModeChanged?.Invoke(this, false);
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
            RegisterHotKey(windowHandle, HOTKEY_TOGGLE_AP2, MOD_CONTROL, 0x4F);      // Ctrl+O (Toggle Autopilot 2)

            InputHotkeyModeChanged?.Invoke(this, true);
        }

        private void DeactivateInputHotkeyMode()
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
            UnregisterHotKey(windowHandle, HOTKEY_TOGGLE_AP2);

            InputHotkeyModeChanged?.Invoke(this, false);
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
                    DeactivateOutputHotkeyMode();
                if (inputHotkeyModeActive)
                    DeactivateInputHotkeyMode();
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
        ReadVerticalSpeed,
        ReadHeadingMagnetic,
        ReadHeadingTrue,
        SelectDestinationRunway,
        ReadILSGuidance,
        ReadWindInfo,
        ShowMETARReport,
        ToggleVisualApproach,
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
        ToggleAutopilot2,
        ReadSpeedGD,
        ReadSpeedS,
        ReadSpeedF,
        ReadSpeedVFE,
        ReadSpeedVLS,
        ReadSpeedVS,
        ShowChecklist,
        ReadFuelQuantity,
        ShowNavigationDisplay,
        ReadWaypointInfo,
        ShowECAM,
        ShowStatusPage,
        ToggleTakeoffAssist,
        ToggleECAMMonitoring
    }
}
