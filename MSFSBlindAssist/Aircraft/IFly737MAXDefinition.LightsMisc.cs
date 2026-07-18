using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iFly 737 MAX8 — lights, signs, oxygen, flight controls, IRS, recorder/warning panels.
/// Every write command's Value2/Value3 semantics were verified against the SDK
/// key_command.h doc lines; map: lambdas convert the STATUS field encoding
/// (what the combo shows) to the SET command's Value2 encoding where they differ.
/// </summary>
public partial class IFly737MAXDefinition
{
    // =========================================================================
    // Exterior Lights
    // =========================================================================
    private void RegisterExteriorLights()
    {
        const string P = "Exterior Lights";

        // TRAP: status is 0 OFF / 1 ON, but GENERAL_LANDING_LIGHT_n_SET Value2 is
        // 0 OFF / 1 FLASH / 2 ON — map On (1) to Value2 2, never send 1 from a 2-state combo.
        Sw(P, "Landing_Light_1_Switch_Status", "Left Landing Light",
            IFlyKeyCommand.GENERAL_LANDING_LIGHT_1_SET, new[] { "Off", "On" }, map: v => v == 0 ? 0 : 2);
        Sw(P, "Landing_Light_2_Switch_Status", "Right Landing Light",
            IFlyKeyCommand.GENERAL_LANDING_LIGHT_2_SET, new[] { "Off", "On" }, map: v => v == 0 ? 0 : 2);

        Sw(P, "Runway_Turnoff_Light_1_Switch_Status", "Left Runway Turnoff Light",
            IFlyKeyCommand.GENERAL_RUNWAY_TURNOFF_LIGHT_1_SET, new[] { "Off", "On" });
        Sw(P, "Runway_Turnoff_Light_2_Switch_Status", "Right Runway Turnoff Light",
            IFlyKeyCommand.GENERAL_RUNWAY_TURNOFF_LIGHT_2_SET, new[] { "Off", "On" });
        Sw(P, "Taxi_Light_Switch_Status", "Taxi Light",
            IFlyKeyCommand.GENERAL_TAXI_LIGHT_SET, new[] { "Off", "On" });
        Sw(P, "Logo_Light_Switch_Status", "Logo Light",
            IFlyKeyCommand.GENERAL_LOGO_LIGHT_SET, new[] { "Off", "On" });
        Sw(P, "Anti_Collision_Light_Switch_Status", "Anti-Collision Light",
            IFlyKeyCommand.GENERAL_ANTI_COLLISION_LIGHT_SET, new[] { "Off", "On" });
        Sw(P, "Wing_Light_Switch_Status", "Wing Illumination Light",
            IFlyKeyCommand.GENERAL_WING_LIGHT_SET, new[] { "Off", "On" });
        Sw(P, "Wheel_Well_Light_Switch_Status", "Wheel Well Light",
            IFlyKeyCommand.GENERAL_WHEEL_WELL_LIGHT_SET, new[] { "Off", "On" });

        // TRAP: status is 0 STEADY / 1 OFF / 2 STROBE & STEADY, but
        // GENERAL_POSITION_LIGHT_SET Value2 is 0 STROBE & STEADY / 1 OFF / 2 STEADY —
        // exactly reversed. Labels follow the STATUS encoding; map inverts for the write.
        Sw(P, "Position_Light_Switch_Status", "Position Lights",
            IFlyKeyCommand.GENERAL_POSITION_LIGHT_SET,
            new[] { "Steady", "Off", "Strobe and steady" }, map: v => 2 - v);

        // Skipped (config flags, not cockpit controls): AutoOffTaxiLlight, LandingLlightAlternateFlash.
    }

    // =========================================================================
    // Interior Lights and Signs
    // =========================================================================
    private void RegisterInteriorLightsSigns()
    {
        const string P = "Interior Lights and Signs";

        // Master LIGHTS TEST: no GENERAL_LIGHTS_TEST_SET exists — read-only position
        // combo plus three absolute-position buttons (TEST / BRT / DIM).
        Sw(P, "Lights_Test_Status", "Master Lights Test Switch Position", null,
            new[] { "Test", "Bright", "Dim" });
        Btn(P, "BTN_LIGHTS_TEST_TEST", "Master Lights Test to Test", IFlyKeyCommand.GENERAL_LIGHTS_TEST_TEST);
        Btn(P, "BTN_LIGHTS_TEST_BRT", "Master Lights Test to Bright", IFlyKeyCommand.GENERAL_LIGHTS_TEST_BRT);
        Btn(P, "BTN_LIGHTS_TEST_DIM", "Master Lights Test to Dim", IFlyKeyCommand.GENERAL_LIGHTS_TEST_DIM);

        // Dome light: status and Value2 share the 0 DIM / 1 OFF / 2 BRIGHT encoding;
        // Value3 = 1 (guard-ignore) offered by the doc.
        Sw(P, "Dome_Light_Switch_Status", "Dome Light",
            IFlyKeyCommand.GENERAL_DOME_LIGHT_SET, new[] { "Dim", "Off", "Bright" }, value3: 1);

        Sw(P, "No_Smoking_Switch_Status", "No Smoking Sign",
            IFlyKeyCommand.GENERAL_NO_SMOKING_SET, new[] { "Off", "Auto", "On" });
        Sw(P, "Fasten_Belts_Switch_Status", "Fasten Seat Belts Sign",
            IFlyKeyCommand.GENERAL_FASTEN_BELTS_SET, new[] { "Off", "Auto", "On" });

        Btn(P, "BTN_ATTENDANT_CALL", "Attendant Call", IFlyKeyCommand.COMMUNICATION_ATTENDANT_CALL);
        Btn(P, "BTN_GROUND_CALL", "Ground Call", IFlyKeyCommand.COMMUNICATION_GROUND_CALL);

        // Emergency exit lights: combo reads the ORIGINAL guarded field
        // (0 cover closed / 1 Off / 2 Armed / 3 On); GENERAL_EMER_LIGHT_SET Value2
        // is 0 OFF / 1 ARMED / 2 ON (no guard-ignore Value3 in the doc), so the
        // guard-closed state collapses onto Off for the write.
        SwD(P, "Emergency_Light_Switch_Status", "Emergency Exit Lights",
            IFlyKeyCommand.GENERAL_EMER_LIGHT_SET,
            new Dictionary<double, string>
            {
                [0] = "Guard closed",
                [1] = "Off",
                [2] = "Armed",
                [3] = "On",
            }, map: v => v == 0 ? 0 : v - 1);
        Annun(P, "NOT_ARMED_Light_Status", "Emergency Exit Lights Not Armed light");
        // Skipped: Emergency_Exit_Light_Switch_Status (v1.1 duplicate of the same switch;
        // its 0-3 encoding carries cover state but no ARMED position — the original field above is used).

        // The two main-panel brightness controls are exposed; all other brightness
        // knobs are deliberately skipped (see the skip block below).
        NumSet(P, "CAPT_MAIN_PANEL_BRIGHT_SET", "Captain Main Panel Brightness (0 to 20)",
            IFlyKeyCommand.GENERAL_CAPT_MAIN_PANEL_SET, 0, 20);
        NumSet(P, "FO_MAIN_PANEL_BRIGHT_SET", "First Officer Main Panel Brightness (0 to 20)",
            IFlyKeyCommand.GENERAL_FO_MAIN_PANEL_SET, 0, 20);

        // Deliberately skipped Airplane General fields:
        //   Aircraft_Model / Tick18 / UNITstyle — SDK metadata, not cockpit controls.
        //   Attendant_Call_Switch_Status / Ground_Call_Switch_Status — momentary press state; exposed as the buttons above.
        //   EQUIP/FWD_ENTRY/AFT_ENTRY/FWD_SERVICE/AFT_SERVICE/FWD_CARGO/AFT_CARGO/
        //   LEFT_FWD_OVERWING/LEFT_AFT_OVERWING/RIGHT_FWD_OVERWING/RIGHT_AFT_OVERWING/
        //   LEFT_MID_EXIT/RIGHT_MID_EXIT/AIRSTAIR _Light_Status — door annunciator lights
        //   are registered as Annun lamps in RegisterDoors() (overhead "Doors" panel,
        //   PR #163 R2, user opted in); their GENERAL_LIGHT_* PRESS commands (per-door
        //   lamp test buttons) stay skipped — out of scope, the lamps themselves are the ask.
        //   Main_Background/AFDS_Flood/Aft_Electronics_Flood/Aft_Electronics_Panel/Compass
        //   _Light_Switch_Status + CB_Light_Control_Status + OVHT_Panel_Light_Control_Status —
        //   panel brightness knobs other than the two main-panel controls (skip per plan).
        //   Door_Lock_Selector_Status / AUTO_UNLK_Light_Status / LOCK_FAIL_Light_Status —
        //   belong to the pedestal "Door Lock" panel (registered in RegisterDoorLock).
    }

    // =========================================================================
    // Doors (overhead door-panel annunciator lights)
    // =========================================================================
    private void RegisterDoors()
    {
        const string P = "Doors";

        // Thirteen door/exit annunciator lights (SDK_Defines.h offsets 33-46), all
        // sharing the standard 0 off / 1 dim / 2 bright encoding. Announce-only lamps
        // (PR #163 R2, user opted in) — no write command is registered for any of
        // them; their GENERAL_LIGHT_* commands are per-door lamp-test PRESS buttons,
        // deliberately left out of scope (see the skip note in RegisterInteriorLightsSigns).
        Annun(P, "EQUIP_Light_Status", "Equipment Door light");
        Annun(P, "FWD_ENTRY_Light_Status", "Forward Entry Door light");
        Annun(P, "AFT_ENTRY_Light_Status", "Aft Entry Door light");
        Annun(P, "FWD_SERVICE_Light_Status", "Forward Service Door light");
        Annun(P, "AFT_SERVICE_Light_Status", "Aft Service Door light");
        Annun(P, "FWD_CARGO_Light_Status", "Forward Cargo Door light");
        Annun(P, "AFT_CARGO_Light_Status", "Aft Cargo Door light");
        Annun(P, "LEFT_FWD_OVERWING_Light_Status", "Left Forward Overwing Exit light");
        Annun(P, "LEFT_AFT_OVERWING_Light_Status", "Left Aft Overwing Exit light");
        Annun(P, "RIGHT_FWD_OVERWING_Light_Status", "Right Forward Overwing Exit light");
        Annun(P, "RIGHT_AFT_OVERWING_Light_Status", "Right Aft Overwing Exit light");
        Annun(P, "LEFT_MID_EXIT_Light_Status", "Left Mid Exit Door light");
        Annun(P, "RIGHT_MID_EXIT_Light_Status", "Right Mid Exit Door light");
        Annun(P, "AIRSTAIR_Light_Status", "Airstair Door light");
    }

    // =========================================================================
    // Oxygen
    // =========================================================================
    private void RegisterOxygen()
    {
        const string P = "Oxygen";

        // Guarded switch: status 0 cover closed (Normal) / 1 Normal / 2 On;
        // GENERAL_PASS_OXYGEN_SET Value2 0 NORMAL / 1 ON, Value3 1 = ignore the guard.
        SwD(P, "Oxygen_Switch_Status", "Passenger Oxygen",
            IFlyKeyCommand.GENERAL_PASS_OXYGEN_SET,
            new Dictionary<double, string>
            {
                [0] = "Guard closed, Normal",
                [1] = "Normal",
                [2] = "On",
            }, map: v => v >= 2 ? 1 : 0, value3: 1);
        Annun(P, "PASS_OXY_ON_Light_Status", "Passenger Oxygen On light");

        Disp(P, "Oxygen_Pointer_Status", "Crew Oxygen Pressure PSI");

        // Guarded switch: status 0 cover closed (Armed) / 1 Armed / 2 On;
        // GENERAL_ELT_SET Value2 0 ARM / 1 ON, Value3 1 = ignore the guard.
        SwD(P, "ELT_Switch_Status", "Emergency Locator Transmitter",
            IFlyKeyCommand.GENERAL_ELT_SET,
            new Dictionary<double, string>
            {
                [0] = "Guard closed, Armed",
                [1] = "Armed",
                [2] = "On",
            }, map: v => v >= 2 ? 1 : 0, value3: 1);
        Annun(P, "ELT_Light_Status", "Emergency Locator Transmitter light");
    }

    // =========================================================================
    // Flight Controls (overhead panel)
    // =========================================================================
    private void RegisterFlightControls()
    {
        const string P = "Flight Controls";

        // Guarded switches: the *_Mode fields carry the pure switch position (1-based)
        // regardless of guard state; the SET Value2 encodings are 0-based, hence map v => v - 1.
        // Value3 1 = ignore the guard (offered for FLIGHT CONTROL and ALTERNATE FLAPS MASTER).
        Sw(P, "Flight_Control_A_Mode", "Flight Control A",
            IFlyKeyCommand.FLTCTRL_FLIGHT_CONTROL_A_SET,
            new[] { "Standby Rudder", "Off", "On" }, map: v => v - 1, value3: 1, valueBase: 1);
        Sw(P, "Flight_Control_B_Mode", "Flight Control B",
            IFlyKeyCommand.FLTCTRL_FLIGHT_CONTROL_B_SET,
            new[] { "Standby Rudder", "Off", "On" }, map: v => v - 1, value3: 1, valueBase: 1);

        // Spoiler A/B SET commands document Value2 only (no guard-ignore Value3).
        Sw(P, "Spoiler_A_Mode", "Flight Spoiler A",
            IFlyKeyCommand.FLTCTRL_SPOILER_A_SET, new[] { "Off", "On" }, map: v => v - 1, valueBase: 1);
        Sw(P, "Spoiler_B_Mode", "Flight Spoiler B",
            IFlyKeyCommand.FLTCTRL_SPOILER_B_SET, new[] { "Off", "On" }, map: v => v - 1, valueBase: 1);

        Sw(P, "Yaw_Damper_Switch_Status", "Yaw Damper",
            IFlyKeyCommand.FLTCTRL_YAW_DAMPER_SET, new[] { "Off", "On" });

        Sw(P, "Altn_Flap_Master_Mode", "Alternate Flaps Master",
            IFlyKeyCommand.FLTCTRL_ALTERNATE_FLAPS_MASTER_SET,
            new[] { "Off", "Armed" }, map: v => v - 1, value3: 1, valueBase: 1);
        // Position switch: status and Value2 share the 0 UP / 1 OFF / 2 DOWN encoding.
        Sw(P, "Altn_Flap_Position_Switch_Status", "Alternate Flaps Position",
            IFlyKeyCommand.FLTCTRL_ALTERNATE_FLAPS_SET, new[] { "Up", "Off", "Down" });

        Annun(P, "FLT_CTL_A_LOW_PRESSURE_Light_Status", "Flight Control A Low Pressure light");
        Annun(P, "FLT_CTL_B_LOW_PRESSURE_Light_Status", "Flight Control B Low Pressure light");
        Annun(P, "YAW_DAMPER_Light_Status", "Yaw Damper light");
        Annun(P, "STBY_HYD_LOW_QUANTITY_Light_Status", "Standby Hydraulic Low Quantity light");
        Annun(P, "STBY_HYD_LOW_PRESSURE_Light_Status", "Standby Hydraulic Low Pressure light");
        Annun(P, "STBY_RUD_ON_Light_Status", "Standby Rudder On light");
        Annun(P, "FEEL_DIFF_PRESS_Light_Status", "Feel Differential Pressure light");
        Annun(P, "SPEED_TRIM_FAIL_Light_Status", "Speed Trim Fail light");
        Annun(P, "MACH_TRIM_FAIL_Light_Status", "Mach Trim Fail light");
        Annun(P, "AUTO_SLAT_FAIL_Light_Status", "Auto Slat Fail light");
        Annun(P, "SPOILERS_Light_Status", "Spoilers light");

        // Deliberately skipped Flight Controls section fields:
        //   Flight_Control_A/B_Switch_Status, Spoiler_A/B_Switch_Status,
        //   Altn_Flap_Master_Switch_Status — guard+switch composites; the pure *_Mode
        //   fields above are the combo state (guard state adds no actionable info).
        //   Stab_Trim_Primary/Backup/Override_Switch_Status + _Mode, Stabilizer_Trim_Pointer_Status,
        //   Stab_Trim_Wheel_Move, Stabilizer_Trim_Switch_Status[2], STAB_OUT_TRIM_Light_Status,
        //   Rudder_Trim_Pointer_Status, Rudder_Trim_Switch_Status, Aileron_Trim_Switch_Status,
        //   Rudder_Trim_OFF_Light_Status — pedestal "Trim" panel scope (RegisterTrim).
        //   FLAP_Status, Flap/Slat_Left/Right_Light_Status, LE_Devices_Test_Switch_Status,
        //   Spoiler_Lever_Status, SPEED_BRAKE_ARMED/DO_NOT_ARM/SPEEDBRAKES_EXTENDED_Light_Status —
        //   "Control Stand" panel scope (RegisterControlStand).
        //   Elevator_Jam_Landing_Assist_Switch_Status + ASSIST_ON_Light_Status — registered
        //   in RegisterTrim (pedestal "Trim" panel — the aft aisle stand control it lives on).
    }

    // =========================================================================
    // IRS
    // =========================================================================
    private void RegisterIrs()
    {
        const string P = "IRS";

        // IRS mode selectors (array field flattened _0 = Left, _1 = Right).
        // Status and Value2 share the 0 OFF / 1 ALIGN / 2 NAV / 3 ATT encoding.
        Sw(P, "IRS_Mode_Switch_Status_0", "Left IRS Mode Selector",
            IFlyKeyCommand.FMS_IRS_L_MODE_SET, new[] { "Off", "Align", "Nav", "Attitude" });
        Sw(P, "IRS_Mode_Switch_Status_1", "Right IRS Mode Selector",
            IFlyKeyCommand.FMS_IRS_R_MODE_SET, new[] { "Off", "Align", "Nav", "Attitude" });

        // IRS source transfer switch. Status and Value2 share the 0 BOTH ON L /
        // 1 NORMAL / 2 BOTH ON R encoding. SDK doc names the SET command "...Switch -
        // Click" (a vendor naming slip shared by this whole three-switch transfer
        // family — see the VHF NAV transfer in RegisterRadios and the FMC source
        // transfer in RegisterDisplaySelect). LIVE-VERIFY the SET actually takes an
        // absolute position; FMS_IRS_TFR_DEC/INC exist as a click-through fallback
        // if it turns out to be a relative toggle instead.
        Sw(P, "IRS_Switch_Status", "IRS Transfer",
            IFlyKeyCommand.FMS_IRS_TFR_SET, new[] { "Both on left", "Normal", "Both on right" });

        Annun(P, "IRS_ALIGN_Light_Status_0", "Left IRS Align light");
        Annun(P, "IRS_ALIGN_Light_Status_1", "Right IRS Align light");
        Annun(P, "IRS_ON_DC_Light_Status_0", "Left IRS On DC light");
        Annun(P, "IRS_ON_DC_Light_Status_1", "Right IRS On DC light");
        Annun(P, "IRS_FAULT_Light_Status_0", "Left IRS Fault light");
        Annun(P, "IRS_FAULT_Light_Status_1", "Right IRS Fault light");
        Annun(P, "IRS_DC_FAIL_Light_Status_0", "Left IRS DC Fail light");
        Annun(P, "IRS_DC_FAIL_Light_Status_1", "Right IRS DC Fail light");

        Annun(P, "GPS_Light_Status", "GPS light");
        Annun(P, "ILS_Light_Status", "ILS light");
        Annun(P, "GLS_Light_Status", "GLS light");

        // ISDU display selector + system-display side selector.
        Sw(P, "DSPL_SEL_Switch_Status", "IRS Display Selector",
            IFlyKeyCommand.FMS_IRS_DSPL_SEL_SET,
            new[] { "Test", "Track and ground speed", "Present position", "Wind", "Heading and status" });
        Sw(P, "SYS_DSPL_Switch_Status", "IRS System Display Source",
            IFlyKeyCommand.FMS_IRS_SYS_DSPL_SET, new[] { "Left", "Right" });

        // Composed ISDU readout (client-synthetic from the IRS_Window_* digit fields).
        Disp(P, "SYN_IRS_DISPLAY", "IRS Display");

        // Deliberately skipped Flight Management (IRS part) fields:
        //   VHF_NAV_Switch_Status (RegisterRadios) and FMC_Switch_Status
        //   (RegisterDisplaySelect) — the other two source transfer selectors, registered
        //   in their own panels. FMC_Indicators_Light_Status_0 (FMC ALERT light) is
        //   registered in RegisterDisplaySelect too; FMC_Indicators_Light_Switch_Status
        //   (the alert's own press/test button) and the FO-side _1 copies of both fields
        //   stay skipped per side policy.
        //   IRS_Brightness_Switch_Status — panel brightness knob (skip per plan).
        //   IRS_Window_L/R_1..7_Status + IRS_Window_L/R_point_1..3_Status — per-digit display
        //   cells, composed into the SYN_IRS_DISPLAY readout by the SDK client.
        //   IRS_KB_CLR/ENT/0..9_Switch_Status — ISDU keypad digits (skip per plan).
    }

    // =========================================================================
    // Flight Recorder and Warning
    // =========================================================================
    private void RegisterRecorderWarning()
    {
        const string P = "Flight Recorder and Warning";

        // Guarded switch: status 0 cover closed (Normal) / 1 Normal / 2 Test;
        // INSTRUMENT_FLIGHT_RECORD_MODE_SET Value2 0 NORMAL / 1 TEST, Value3 1 = ignore the guard.
        SwD(P, "Flight_Recorder_Switch_Status", "Flight Recorder Test Switch",
            IFlyKeyCommand.INSTRUMENT_FLIGHT_RECORD_MODE_SET,
            new Dictionary<double, string>
            {
                [0] = "Guard closed, Normal",
                [1] = "Normal",
                [2] = "Test",
            }, map: v => v >= 2 ? 1 : 0, value3: 1);
        Annun(P, "Flight_Recorder_Light_Status", "Flight Recorder Off light");

        // CVR selector (SDK v1.5, MSFS only): readable state, but the only write
        // command is a click TOGGLE — read-only combo plus a toggle button.
        Sw(P, "CVR_Switch_Status", "Cockpit Voice Recorder Switch", null, new[] { "Auto", "On" });
        Btn(P, "BTN_CVR_SWITCH_TOGGLE", "Cockpit Voice Recorder Switch Toggle",
            IFlyKeyCommand.COMMUNICATION_CVR_SWITCH);
        Btn(P, "BTN_CVR_TEST", "Cockpit Voice Recorder Test", IFlyKeyCommand.COMMUNICATION_CVR_TEST);
        Btn(P, "BTN_CVR_ERASE", "Cockpit Voice Recorder Erase", IFlyKeyCommand.COMMUNICATION_CVR_ERASE);
        Annun(P, "CVR_STATUS_Light_Status", "Cockpit Voice Recorder Status light");

        Btn(P, "BTN_MACH_AIRSPEED_TEST_1", "Mach Airspeed Warning Test 1", IFlyKeyCommand.WARNING_AIRSPEED_TEST1);
        Btn(P, "BTN_MACH_AIRSPEED_TEST_2", "Mach Airspeed Warning Test 2", IFlyKeyCommand.WARNING_AIRSPEED_TEST2);
        Btn(P, "BTN_STALL_WARNING_TEST_1", "Stall Warning Test 1", IFlyKeyCommand.WARNING_STALL_1_TEST);
        Btn(P, "BTN_STALL_WARNING_TEST_2", "Stall Warning Test 2", IFlyKeyCommand.WARNING_STALL_2_TEST);
        Btn(P, "BTN_GEAR_WARNING_CUTOUT", "Landing Gear Warning Cutout", IFlyKeyCommand.WARNING_LDG_GEAR_WARNING_CUTOUT);

        Sw(P, "Service_Interphone_Switch_Status", "Service Interphone",
            IFlyKeyCommand.COMMUNICATION_SERVICE_INTERPHONE_SET, new[] { "Off", "On" });

        Annun(P, "MAINT_light_Status", "Maintenance light");

        // Deliberately skipped fields in this area:
        //   CVR_TEST/CVR_ERASE_Switch_Status — momentary press states; exposed as the buttons above.
        //   Mach_Airspeed_TEST_1/2, Stall_Warning_TEST_1/2, Landing_Gear_Warning_Cutout
        //   _Switch_Status — momentary press states; exposed as the buttons above.
        //   Clock_Switch_Status[2] — clock chrono push state, no readable clock value in the SDK.
    }
}
