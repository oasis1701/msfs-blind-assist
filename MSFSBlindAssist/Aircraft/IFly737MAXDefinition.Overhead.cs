using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iFly 737 MAX8 — overhead systems panels: Electrical, Fuel, Hydraulics,
/// Air Systems, Pressurization, Anti-Ice, Engines and APU.
///
/// Field names/encodings from SDK_Defines.h (v1.5); write commands and their
/// Value2/Value3 encodings from key_command.h. Where the SET command's Value2
/// encoding differs from the status field's encoding, a map converts the combo
/// value; guarded switches use the 1-based *_Mode companion field with
/// Value3 = 1 ("ignore the guard, press the button directly").
/// </summary>
public partial class IFly737MAXDefinition
{
    // =========================================================================
    // Electrical
    // =========================================================================

    private void RegisterElectrical()
    {
        const string P = "Electrical";

        // Battery is GUARDED: combo on the 1-based Battery_Switch_Mode (1:OFF 2:ON);
        // BAT_SET Value2 is 0:OFF 1:ON -> map v-1; Value3 1 bypasses the guard.
        // NOTE: the write is special-cased in HandleUIVariableSet — it sends the
        // guard-bypassed SET, then reads Battery_Switch_Mode back and announces if
        // the guard swallowed the press (the SDK has no guard/cover command).
        SwD(P, "Battery_Switch_Mode", "Battery", IFlyKeyCommand.ELECTRICAL_BAT_SET,
            new Dictionary<double, string> { [1] = "Off", [2] = "On" }, map: v => v - 1, value3: 1);
        // Battery_Switch_Status (0:cover CLOSE 1:open,OFF 2:open,ON) skipped — the Mode field carries the switch state.

        Sw(P, "CAB_UTIL_Switch_Status", "Cabin Utility Power",
            IFlyKeyCommand.ELECTRICAL_CAB_UTIL_SET, new[] { "Off", "On" });
        Sw(P, "IFE_PASS_SEAT_Switch_Status", "IFE and Passenger Seat Power",
            IFlyKeyCommand.ELECTRICAL_IFE_PASS_SEAT_SET, new[] { "Off", "On" });

        // Standby power is GUARDED 3-position: Mode 1:BAT 2:OFF 3:AUTO;
        // STANDBY_POWER_SET Value2 is 0:BAT 1:OFF 2:AUTO -> map v-1; Value3 1 bypasses the guard.
        SwD(P, "STANDBY_POWER_Switch_Mode", "Standby Power", IFlyKeyCommand.ELECTRICAL_STANDBY_POWER_SET,
            new Dictionary<double, string> { [1] = "Battery", [2] = "Off", [3] = "Auto" }, map: v => v - 1, value3: 1);

        // Generator drive disconnects are GUARDED: Mode 1:CONNECT 2:DISCONNECT;
        // IDG_n_DISCONNECT_SET Value2 is 0:Connect 1:Disconnect -> map v-1; Value3 1 bypasses the guard.
        SwD(P, "Generator_Drive_Disconnect_Switch_Mode_0", "Generator Drive 1 Disconnect",
            IFlyKeyCommand.ELECTRICAL_IDG_1_DISCONNECT_SET,
            new Dictionary<double, string> { [1] = "Connected", [2] = "Disconnected" }, map: v => v - 1, value3: 1);
        SwD(P, "Generator_Drive_Disconnect_Switch_Mode_1", "Generator Drive 2 Disconnect",
            IFlyKeyCommand.ELECTRICAL_IDG_2_DISCONNECT_SET,
            new Dictionary<double, string> { [1] = "Connected", [2] = "Disconnected" }, map: v => v - 1, value3: 1);

        // Ground power + ENG/APU generators: 3-position momentary switches (spring
        // back to NEUTRAL). ENCODING TRAP (live-verified 2026-07-23): the _SET
        // commands only MOVE THE ANIMATION — the electrical connect/trip logic
        // never fires (GRD PWR SET was dead in BOTH directions with ground power
        // available; the old SET-hold-release emulation clicked audibly but never
        // connected). The momentary CLICK commands are the cockpit-clickspot path
        // and work: Move DOWN = ON/connect, Move UP = OFF/trip, and the SDK
        // springs the switch back to NEUTRAL by itself (verified on GRD PWR and
        // APU GEN 1, both directions, watching TRANSFER_BUS_OFF / GEN_OFF_BUS).
        // Because the resting position is always NEUTRAL, there is no meaningful
        // switch state to show — expose stateless On/Off momentary button pairs,
        // the exact PMDG 737 shape (ELEC_GrdPwrSw_On/_Off, ELEC_APUGenSw_n_On/_Off);
        // connect state is announced by the GEN OFF BUS / TRANSFER BUS OFF /
        // GRD POWER AVAILABLE annunciators below.
        Btn(P, "BTN_GRD_PWR_ON", "Ground Power On", IFlyKeyCommand.ELECTRICAL_GRD_PWR_DOWN);
        Btn(P, "BTN_GRD_PWR_OFF", "Ground Power Off", IFlyKeyCommand.ELECTRICAL_GRD_PWR_UP);

        // Bus transfer is GUARDED: Mode 1:OFF 2:AUTO; Value2 0:OFF 1:AUTO -> map v-1; Value3 1.
        SwD(P, "Bus_Transfer_Switches_Mode", "Bus Transfer", IFlyKeyCommand.ELECTRICAL_BUS_TRANSFER_SET,
            new Dictionary<double, string> { [1] = "Off", [2] = "Auto" }, map: v => v - 1, value3: 1);

        // Engine + APU generator switches — same momentary click-command pairs as
        // Ground Power above (the APU GEN SET happened to work in live testing,
        // but the click path is the verified pilot path for the whole family and
        // the GRD PWR SET proved the SETs can't be trusted switch-by-switch).
        Btn(P, "BTN_GEN_1_ON", "Generator 1 On", IFlyKeyCommand.ELECTRICAL_GENERATOR_1_DOWN);
        Btn(P, "BTN_GEN_1_OFF", "Generator 1 Off", IFlyKeyCommand.ELECTRICAL_GENERATOR_1_UP);
        Btn(P, "BTN_GEN_2_ON", "Generator 2 On", IFlyKeyCommand.ELECTRICAL_GENERATOR_2_DOWN);
        Btn(P, "BTN_GEN_2_OFF", "Generator 2 Off", IFlyKeyCommand.ELECTRICAL_GENERATOR_2_UP);
        Btn(P, "BTN_APU_GEN_1_ON", "APU Generator 1 On", IFlyKeyCommand.ELECTRICAL_APU_GENERATOR_1_DOWN);
        Btn(P, "BTN_APU_GEN_1_OFF", "APU Generator 1 Off", IFlyKeyCommand.ELECTRICAL_APU_GENERATOR_1_UP);
        Btn(P, "BTN_APU_GEN_2_ON", "APU Generator 2 On", IFlyKeyCommand.ELECTRICAL_APU_GENERATOR_2_DOWN);
        Btn(P, "BTN_APU_GEN_2_OFF", "APU Generator 2 Off", IFlyKeyCommand.ELECTRICAL_APU_GENERATOR_2_UP);

        // Meters selectors (status and SET Value2 encodings match).
        Sw(P, "DC_Meters_Selector_Status", "DC Meters Selector", IFlyKeyCommand.ELECTRICAL_DC_METER_SET,
            new[] { "Standby Power", "Battery Bus", "Battery", "Auxiliary Battery", "TR1", "TR2", "TR3", "Test" });
        Sw(P, "AC_Meters_Selector_Status", "AC Meters Selector", IFlyKeyCommand.ELECTRICAL_AC_METER_SET,
            new[] { "Standby Power", "Ground Power", "Generator 1", "APU Generator", "Generator 2", "Inverter", "Test" });

        // Ground service switch: READ-ONLY combo — the SDK exposes no write command for it.
        Sw(P, "Ground_Service_Switch_Status", "Ground Service", null, new[] { "Off", "On" });

        // Maintenance test button cycles the ELEC LED maintenance display below.
        Btn(P, "BTN_ELEC_MAINT", "Electrical Maintenance Test", IFlyKeyCommand.ELECTRICAL_MAINT);
        // ELEC_MAINT_Switch_Status skipped — momentary press readback with no resting state.

        // Annunciators.
        Annun(P, "BAT_DISCHARGE_Light_Status", "Battery Discharge light");
        Annun(P, "TR_UNIT_Light_Status", "TR Unit light");
        Annun(P, "ELEC_Light_Status", "Electrical light");
        Annun(P, "ENG_DRIVE_Light_Status_0", "Generator Drive 1 light");
        Annun(P, "ENG_DRIVE_Light_Status_1", "Generator Drive 2 light");
        Annun(P, "STANDBY_PWR_OFF_Light_Status", "Standby Power Off light");
        Annun(P, "GRD_POWER_AVAILABLE_Light_Status", "Ground Power Available light");
        Annun(P, "ENG_TRANSFER_BUS_OFF_Light_Status_0", "Transfer Bus 1 Off light");
        Annun(P, "ENG_TRANSFER_BUS_OFF_Light_Status_1", "Transfer Bus 2 Off light");
        Annun(P, "ENG_SOURCE_OFF_Light_Status_0", "Source 1 Off light");
        Annun(P, "ENG_SOURCE_OFF_Light_Status_1", "Source 2 Off light");
        Annun(P, "ENG_GEN_OFF_BUS_Light_Status_0", "Generator 1 Off Bus light");
        Annun(P, "ENG_GEN_OFF_BUS_Light_Status_1", "Generator 2 Off Bus light");
        Annun(P, "APU_GEN_OFF_BUS_Light_Status", "APU Generator Off Bus light");

        // Two-line ELEC maintenance LED display (client-composed; rendered by the core).
        Disp(P, "SYN_ELEC_LED", "Electrical Meter Display");
        // ELEC_LED_TEXT[2][12] skipped — raw character cells; surfaced through SYN_ELEC_LED.
    }

    // =========================================================================
    // Fuel
    // =========================================================================

    private void RegisterFuel()
    {
        const string P = "Fuel";

        // Crossfeed: status 0:valve CLOSED 1:valve OPEN; CROSSFEED_SET Value2 0:OFF 1:ON — same order.
        Sw(P, "Fuel_Crossfeed_Selector_Status", "Crossfeed Selector",
            IFlyKeyCommand.FUEL_CROSSFEED_SET, new[] { "Closed", "Open" });

        // Six fuel pump switches (0:OFF 1:ON, SET Value2 matches). Note the command names
        // use CTR/AFT_1/FWD_1 numbering: 1 = left tank, 2 = right tank.
        Sw(P, "Fuel_CENTER_L_Switch_Status", "Center Left Fuel Pump",
            IFlyKeyCommand.FUEL_CTR_L_PUMP_SET, new[] { "Off", "On" });
        Sw(P, "Fuel_CENTER_R_Switch_Status", "Center Right Fuel Pump",
            IFlyKeyCommand.FUEL_CTR_R_PUMP_SET, new[] { "Off", "On" });
        Sw(P, "Fuel_L_FWD_Switch_Status", "Left Forward Fuel Pump",
            IFlyKeyCommand.FUEL_FWD_1_PUMP_SET, new[] { "Off", "On" });
        Sw(P, "Fuel_L_AFT_Switch_Status", "Left Aft Fuel Pump",
            IFlyKeyCommand.FUEL_AFT_1_PUMP_SET, new[] { "Off", "On" });
        Sw(P, "Fuel_R_FWD_Switch_Status", "Right Forward Fuel Pump",
            IFlyKeyCommand.FUEL_FWD_2_PUMP_SET, new[] { "Off", "On" });
        Sw(P, "Fuel_R_AFT_Switch_Status", "Right Aft Fuel Pump",
            IFlyKeyCommand.FUEL_AFT_2_PUMP_SET, new[] { "Off", "On" });

        // Pump low-pressure lights.
        Annun(P, "LOW_PRESSURE_CENTER_L_Light_Status", "Center Left Fuel Pump Low Pressure light");
        Annun(P, "LOW_PRESSURE_CENTER_R_Light_Status", "Center Right Fuel Pump Low Pressure light");
        Annun(P, "LOW_PRESSURE_L_FWD_Light_Status", "Left Forward Fuel Pump Low Pressure light");
        Annun(P, "LOW_PRESSURE_L_AFT_Light_Status", "Left Aft Fuel Pump Low Pressure light");
        Annun(P, "LOW_PRESSURE_R_FWD_Light_Status", "Right Forward Fuel Pump Low Pressure light");
        Annun(P, "LOW_PRESSURE_R_AFT_Light_Status", "Right Aft Fuel Pump Low Pressure light");

        Annun(P, "VALVE_OPEN_Light_Status", "Crossfeed Valve Open light");
        Annun(P, "ENG_VALVE_CLOSED_Light_Status_0", "Engine 1 Valve Closed light");
        Annun(P, "ENG_VALVE_CLOSED_Light_Status_1", "Engine 2 Valve Closed light");
        Annun(P, "SPAR_VALVE_CLOSED_Light_Status_0", "Engine 1 Spar Valve Closed light");
        Annun(P, "SPAR_VALVE_CLOSED_Light_Status_1", "Engine 2 Spar Valve Closed light");
        Annun(P, "FILTER_BYPASS_Light_Status_0", "Engine 1 Fuel Filter Bypass light");
        Annun(P, "FILTER_BYPASS_Light_Status_1", "Engine 2 Fuel Filter Bypass light");

        // Gauges.
        Disp(P, "FUEL_TEMP_Indicator", "Fuel Temperature"); // degrees; <= -100 means invalid
        Disp(P, "SYN_FUEL_QTY_L", "Fuel Quantity Left");
        Disp(P, "SYN_FUEL_QTY_R", "Fuel Quantity Right");
        Disp(P, "SYN_FUEL_QTY_C", "Fuel Quantity Center");

        // Fueling station (the P15 panel behind the refuel access door on the right
        // wing — added on user request 2026-07-23 after the EFB fuel-loading
        // investigation; the iFly's fuel model only accepts fuel through its own
        // fueling simulation, so these switches matter to a blind user). Three
        // latching valve switches, 0 CLOSED / 1 OPEN (indices: 0 Left, 1 Right,
        // 2 Center; commands: VALVE_1 = Left, VALVE_2 = Right, VALVE_C = Center —
        // live-verified via the OPEN pair lighting status indices 0+1). The SDK has
        // only per-direction click commands (no absolute SET) -> SwPerValue.
        // GATING TRAP (live-verified): the whole station is DEAD while the refuel
        // access panel is closed — the write path in HandleUIVariableSet verifies
        // and speaks a hint; the panel itself has no SDK command (read-only status
        // below) and is opened via the EFB Ground Services "Fuel" service door.
        SwPerValue(P, "Refuel_Valve_Control_Switch_Status_0", "Fueling Valve Left Tank",
            new[] { IFlyKeyCommand.FUEL_FUELING_VALVE_1_CLOSE, IFlyKeyCommand.FUEL_FUELING_VALVE_1_OPEN },
            new[] { "Closed", "Open" });
        SwPerValue(P, "Refuel_Valve_Control_Switch_Status_1", "Fueling Valve Right Tank",
            new[] { IFlyKeyCommand.FUEL_FUELING_VALVE_2_CLOSE, IFlyKeyCommand.FUEL_FUELING_VALVE_2_OPEN },
            new[] { "Closed", "Open" });
        SwPerValue(P, "Refuel_Valve_Control_Switch_Status_2", "Fueling Valve Center Tank",
            new[] { IFlyKeyCommand.FUEL_FUELING_VALVE_C_CLOSE, IFlyKeyCommand.FUEL_FUELING_VALVE_C_OPEN },
            new[] { "Closed", "Open" });
        // Indication test switch: status 0 TEST GAGES / 1 NEUTRAL / 2 FUEL DOOR
        // SWITCH BYPASS; per-position commands _TEST/_OFF/_BYPASS assumed to map in
        // that order. LIVE-VERIFY: mapping + whether TEST springs back to NEUTRAL
        // (the real switch is spring-loaded from TEST) — untestable this session
        // because ground services were locked out; the write-verify hint only fires
        // with the access panel closed, so a spring-back never false-alarms.
        SwPerValue(P, "Fueling_Indication_Test_Switch_Status", "Fueling Indication Test",
            new[] { IFlyKeyCommand.FUEL_FUELING_INDICATION_TEST_TEST, IFlyKeyCommand.FUEL_FUELING_INDICATION_TEST_OFF,
                    IFlyKeyCommand.FUEL_FUELING_INDICATION_TEST_BYPASS },
            new[] { "Test Gauges", "Neutral", "Fuel Door Switch Bypass" });
        // Access panel state — no SDK write command exists; renders read-only.
        Sw(P, "Refuel_Power_Control_Switch_Status", "Refuel Access Panel", set: null,
            new[] { "Closed", "Open" });
        // Blue valve-open lights (bool fields — Annun's nonzero-is-lit handling fits).
        Annun(P, "REFUEL_VALVE_Light_Status_0", "Fueling Valve Left Open light");
        Annun(P, "REFUEL_VALVE_Light_Status_1", "Fueling Valve Right Open light");
        Annun(P, "REFUEL_VALVE_Light_Status_2", "Fueling Valve Center Open light");

        // Still skipped: Fuel_Quantity_Indicator_Status[3][5] and
        // Fuel_Quantity_Indicator_Index_Status[3] (per-digit gauge cells — covered by SYN_FUEL_QTY_*).
    }

    // =========================================================================
    // Hydraulics
    // =========================================================================

    private void RegisterHydraulics()
    {
        const string P = "Hydraulics";

        // Pump numbering trap: per key_command.h, ELECTRIC_PUMP_1 drives HYD SYSTEM B and
        // ELECTRIC_PUMP_2 drives HYD SYSTEM A (the struct's ELEC_1/ELEC_2 indices pair with
        // the same-numbered command; the light docs confirm ELEC 1 = system B).
        Sw(P, "ENG_1_HYD_Switch_Status", "Engine 1 Hydraulic Pump, System A",
            IFlyKeyCommand.HYDRAULIC_ENG_PUMP_1_SET, new[] { "Off", "On" });
        Sw(P, "ELEC_2_HYD_Switch_Status", "Electric Hydraulic Pump 2, System A",
            IFlyKeyCommand.HYDRAULIC_ELECTRIC_PUMP_2_SET, new[] { "Off", "On" });
        Sw(P, "ELEC_1_HYD_Switch_Status", "Electric Hydraulic Pump 1, System B",
            IFlyKeyCommand.HYDRAULIC_ELECTRIC_PUMP_1_SET, new[] { "Off", "On" });
        Sw(P, "ENG_2_HYD_Switch_Status", "Engine 2 Hydraulic Pump, System B",
            IFlyKeyCommand.HYDRAULIC_ENG_PUMP_2_SET, new[] { "Off", "On" });

        Annun(P, "ELEC_1_HYD_OVERHEAT_Light_Status", "Electric Hydraulic Pump 1 Overheat light");
        Annun(P, "ELEC_2_HYD_OVERHEAT_Light_Status", "Electric Hydraulic Pump 2 Overheat light");
        Annun(P, "ENG_1_HYD_LOW_PRESSURE_Light_Status", "Engine 1 Hydraulic Pump Low Pressure light");
        Annun(P, "ENG_2_HYD_LOW_PRESSURE_Light_Status", "Engine 2 Hydraulic Pump Low Pressure light");
        Annun(P, "ELEC_1_HYD_LOW_PRESSURE_Light_Status", "Electric Hydraulic Pump 1 Low Pressure light");
        Annun(P, "ELEC_2_HYD_LOW_PRESSURE_Light_Status", "Electric Hydraulic Pump 2 Low Pressure light");
    }

    // =========================================================================
    // Air Systems (bleed air + air conditioning + equipment cooling)
    // =========================================================================

    private void RegisterAirSystems()
    {
        const string P = "Air Systems";

        // Bleed sources (0:OFF 1:ON, SET Value2 matches).
        Sw(P, "Engine_Bleed_Air_Switch_Status_0", "Engine 1 Bleed Air",
            IFlyKeyCommand.AIRSYSTEM_ENG_1_BLEED_SET, new[] { "Off", "On" });
        Sw(P, "Engine_Bleed_Air_Switch_Status_1", "Engine 2 Bleed Air",
            IFlyKeyCommand.AIRSYSTEM_ENG_2_BLEED_SET, new[] { "Off", "On" });
        Sw(P, "APU_Bleed_Air_Switch_Status", "APU Bleed Air",
            IFlyKeyCommand.AIRSYSTEM_APU_BLEED_SET, new[] { "Off", "On" });

        // Packs (0:OFF 1:AUTO 2:HIGH, SET Value2 matches).
        Sw(P, "Pack_Switch_Status_0", "Left Pack",
            IFlyKeyCommand.AIRSYSTEM_PACK_1_SET, new[] { "Off", "Auto", "High" });
        Sw(P, "Pack_Switch_Status_1", "Right Pack",
            IFlyKeyCommand.AIRSYSTEM_PACK_2_SET, new[] { "Off", "Auto", "High" });

        // Recirculation fans (0:OFF 1:AUTO).
        Sw(P, "RecircFan_Switch_Status_0", "Left Recirculation Fan",
            IFlyKeyCommand.AIRSYSTEM_RECIRC_L_FAN_SET, new[] { "Off", "Auto" });
        Sw(P, "RecircFan_Switch_Status_1", "Right Recirculation Fan",
            IFlyKeyCommand.AIRSYSTEM_RECIRC_R_FAN_SET, new[] { "Off", "Auto" });

        // Isolation valve (0:CLOSE 1:AUTO 2:OPEN, SET Value2 matches).
        Sw(P, "Isolation_Valve_Switch_Status", "Isolation Valve",
            IFlyKeyCommand.AIRSYSTEM_ISOLATION_VALVE_SET, new[] { "Close", "Auto", "Open" });

        // Trim air (0:OFF 1:ON).
        Sw(P, "Trim_Air_Switch_Status", "Trim Air",
            IFlyKeyCommand.AIRSYSTEM_TRIM_AIR_SET, new[] { "Off", "On" });

        // Air temperature source selector (status and SET Value2 share the 0..6 order).
        Sw(P, "Air_Temperature_Source_Selector_Status", "Air Temperature Source Selector",
            IFlyKeyCommand.AIRSYSTEM_TEMP_SOURCE_SET,
            new[]
            {
                "Duct, Control Cabin", "Duct, Forward Cabin", "Duct, Aft Cabin",
                "Passenger Cabin, Forward", "Passenger Cabin, Aft", "Right Pack", "Left Pack"
            });

        // Zone temperature selectors: 0~7 continuous knob (0:OFF 1:C 4:AUTO 7:W);
        // SET Value2 is the same 0~7 scale ("from OFF to W").
        string[] tempPositions = { "Off", "Cool 1", "Cool 2", "Cool 3", "Auto", "Warm 1", "Warm 2", "Warm" };
        Sw(P, "Temperature_Selector_Status_0", "Control Cabin Temperature Selector",
            IFlyKeyCommand.AIRSYSTEM_TEMP_SEL_1_SET, tempPositions);
        Sw(P, "Temperature_Selector_Status_1", "Forward Cabin Temperature Selector",
            IFlyKeyCommand.AIRSYSTEM_TEMP_SEL_2_SET, tempPositions);
        Sw(P, "Temperature_Selector_Status_2", "Aft Cabin Temperature Selector",
            IFlyKeyCommand.AIRSYSTEM_TEMP_SEL_3_SET, tempPositions);

        // Momentary test/reset switches.
        Btn(P, "BTN_WINGBODY_OVHT_TEST", "Wing-Body Overheat Test", IFlyKeyCommand.AIRSYSTEM_WINGBODY_OVHT_TEST);
        Btn(P, "BTN_TRIP_RESET", "Trip Reset", IFlyKeyCommand.AIRSYSTEM_TRIP_RESET);
        // WingBody_Overheat_Test_Switch_Status / Trip_Reset_Switch_Status skipped — momentary press readbacks.

        // Equipment cooling (0:NORM 1:ALTN, SET Value2 matches).
        Sw(P, "Equipment_COOLING_SUPPLY_Switch_Status", "Equipment Cooling Supply",
            IFlyKeyCommand.AIRSYSTEM_COOLING_SUPPLY_SET, new[] { "Normal", "Alternate" });
        Sw(P, "Equipment_COOLING_EXHAUST_Switch_Status", "Equipment Cooling Exhaust",
            IFlyKeyCommand.AIRSYSTEM_COOLING_EXHAUST_SET, new[] { "Normal", "Alternate" });
        Annun(P, "Equip_Cooling_OFF_Light_1_Status", "Equipment Cooling Supply Off light");
        Annun(P, "Equip_Cooling_OFF_Light_2_Status", "Equipment Cooling Exhaust Off light");
        Annun(P, "EQUIP_SMOKE_Light_Status", "Equipment Smoke light");

        // Annunciators.
        Annun(P, "PACK_Light_Status_0", "Left Pack light");
        Annun(P, "PACK_Light_Status_1", "Right Pack light");
        Annun(P, "BLEED_Light_Status_0", "Engine 1 Bleed Trip Off light");
        Annun(P, "BLEED_Light_Status_1", "Engine 2 Bleed Trip Off light");
        Annun(P, "WING_BODY_OVERHEAT_Light_Status_0", "Left Wing-Body Overheat light");
        Annun(P, "WING_BODY_OVERHEAT_Light_Status_1", "Right Wing-Body Overheat light");
        Annun(P, "DUAL_BLEED_Light_Status", "Dual Bleed light");
        Annun(P, "ZONE_TEMP_Light_Status_0", "Control Cabin Zone Temperature light");
        Annun(P, "ZONE_TEMP_Light_Status_1", "Forward Cabin Zone Temperature light");
        Annun(P, "ZONE_TEMP_Light_Status_2", "Aft Cabin Zone Temperature light");

        // Gauges.
        Disp(P, "Duct_Pressure_Pointer_Status_0", "Left Duct Pressure");
        Disp(P, "Duct_Pressure_Pointer_Status_1", "Right Duct Pressure");
        Disp(P, "Air_Temperature_Pointer_Status", "Air Temperature");
    }

    // =========================================================================
    // Pressurization
    // =========================================================================

    private void RegisterPressurization()
    {
        const string P = "Pressurization";

        // Direct-entry selectors (FLT_ALT_SET Value2: -1000~42000 ft; LDG_ALT_SET Value2: -1000~14000 ft).
        NumSet(P, "PRESS_FLT_ALT_SET", "Flight Altitude", IFlyKeyCommand.AIRSYSTEM_FLT_ALT_SET,
            -1000, 42000, units: "feet");
        NumSet(P, "PRESS_LDG_ALT_SET", "Landing Altitude", IFlyKeyCommand.AIRSYSTEM_LDG_ALT_SET,
            -1000, 14000, units: "feet");
        // The Flight/Landing_Altitude_Indicator_* per-digit LED fields and the
        // Flight/Landing_Altitude_Switch_Status knob-animation fields are skipped —
        // the NumSet confirmation echo is the accessible readback.

        // Mode selector (0:AUTO 1:ALTN 2:MAN, SET Value2 matches).
        Sw(P, "Pressurization_Mode_Selector_Status", "Pressurization Mode Selector",
            IFlyKeyCommand.AIRSYSTEM_PRESSURIZATION_MODE_SET, new[] { "Auto", "Alternate", "Manual" });

        // Outflow valve: spring-loaded CLOSE/NEUTRAL/OPEN switch driven by two momentary
        // move commands (no SET exists) — two buttons instead of a combo.
        Btn(P, "BTN_OUTFLOW_VALVE_CLOSE", "Outflow Valve Close", IFlyKeyCommand.AIRSYSTEM_OUTFLOW_VALVE_CLOSE);
        Btn(P, "BTN_OUTFLOW_VALVE_OPEN", "Outflow Valve Open", IFlyKeyCommand.AIRSYSTEM_OUTFLOW_VALVE_OPEN);
        // Outflow_Valve_Switch_Status skipped — the momentary switch position; the gauge below is the state.
        Disp(P, "Outflow_VALVE_Position_Indicator_Pointer_Status", "Outflow Valve Position"); // 0 closed .. 100 open

        // Altitude horn cutout (momentary press).
        Btn(P, "BTN_ALT_HORN_CUTOUT", "Altitude Horn Cutout", IFlyKeyCommand.AIRSYSTEM_ALT_HORN_CUTOUT);
        // Altitude_HORN_Cutout_Switch_Status skipped — momentary press readback.

        // High altitude landing: status packs switch+INOP light (0-5, >=3 = switch ON);
        // HIGH_ALTITUDE_LANDING_SET Value2 is just 0:OFF 1:ON -> map v => v >= 3 ? 1 : 0.
        SwD(P, "High_Altitude_Landing_Switch_Status", "High Altitude Landing",
            IFlyKeyCommand.AIRSYSTEM_HIGH_ALTITUDE_LANDING_SET,
            new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Off, inoperative light on",
                [2] = "Off, inoperative light bright",
                [3] = "On",
                [4] = "On, inoperative light on",
                [5] = "On, inoperative light bright",
            }, map: v => v >= 3 ? 1 : 0);

        // Readback legitimately differs from the picked value (inoperative-light bit
        // folded into the same 0-5 field) — suppress the post-set echo on the time
        // window alone. (PR #163, minor 15.)
        _vars["High_Altitude_Landing_Switch_Status"].UiEchoMatchesAnyValue = true;

        // Annunciators.
        Annun(P, "AUTO_FAIL_Light_Status", "Auto Fail light");
        Annun(P, "OFF_SCHED_DESCENT_Light_Status", "Off Schedule Descent light");
        Annun(P, "ALTN_Light_Status", "Alternate Pressurization light");
        Annun(P, "MANUAL_Light_Status", "Manual Pressurization light");

        // Cabin readouts.
        Disp(P, "CabinAltitude", "Cabin Altitude");                 // feet
        Disp(P, "CabinDeltaPres", "Cabin Differential Pressure");   // psi
        Disp(P, "CabinAltitudeRate", "Cabin Altitude Rate");        // feet per minute
    }

    // =========================================================================
    // Anti-Ice (window heat, probe heat, wing/engine anti-ice, wipers)
    // =========================================================================

    private void RegisterAntiIce()
    {
        const string P = "Anti-Ice";

        // Window heat switches 1..4 follow the panel's left-to-right order:
        // 1 = Left Side, 2 = Left Forward, 3 = Right Forward, 4 = Right Side
        // (matching the command family LEFT_SIDE / LEFT_FWD / RIGHT_FWD / RIGHT_SIDE).
        Sw(P, "Window_Heat_Switch_1_Status", "Left Side Window Heat",
            IFlyKeyCommand.ANTIICE_LEFT_SIDE_WINDOW_HEAT_SET, new[] { "Off", "On" });
        Sw(P, "Window_Heat_Switch_2_Status", "Left Forward Window Heat",
            IFlyKeyCommand.ANTIICE_LEFT_FWD_WINDOW_HEAT_SET, new[] { "Off", "On" });
        Sw(P, "Window_Heat_Switch_3_Status", "Right Forward Window Heat",
            IFlyKeyCommand.ANTIICE_RIGHT_FWD_WINDOW_HEAT_SET, new[] { "Off", "On" });
        Sw(P, "Window_Heat_Switch_4_Status", "Right Side Window Heat",
            IFlyKeyCommand.ANTIICE_RIGHT_SIDE_WINDOW_HEAT_SET, new[] { "Off", "On" });

        // Window heat test: 3-position held switch (0:OVHT 1:NEUTRAL 2:PWR TEST, SET Value2 matches).
        Sw(P, "Window_Heat_Test_Switch_Status", "Window Heat Test",
            IFlyKeyCommand.ANTIICE_WINDOW_HEAT_TEST_SET, new[] { "Overheat", "Neutral", "Power Test" });

        Annun(P, "Window_OVERHEAT_1_Light_Status", "Left Side Window Overheat light");
        Annun(P, "Window_OVERHEAT_2_Light_Status", "Left Forward Window Overheat light");
        Annun(P, "Window_OVERHEAT_3_Light_Status", "Right Forward Window Overheat light");
        Annun(P, "Window_OVERHEAT_4_Light_Status", "Right Side Window Overheat light");
        Annun(P, "Window_Heat_ON_1_Light_Status", "Left Side Window Heat On light");
        Annun(P, "Window_Heat_ON_2_Light_Status", "Left Forward Window Heat On light");
        Annun(P, "Window_Heat_ON_3_Light_Status", "Right Forward Window Heat On light");
        Annun(P, "Window_Heat_ON_4_Light_Status", "Right Side Window Heat On light");

        // Probe heat (0:AUTO 1:ON, SET Value2 matches — note AUTO is position 0, not OFF).
        Sw(P, "Probe_Heat_Switch_1_Status", "Probe Heat A",
            IFlyKeyCommand.ANTIICE_PROBE_A_HEAT_SET, new[] { "Auto", "On" });
        Sw(P, "Probe_Heat_Switch_2_Status", "Probe Heat B",
            IFlyKeyCommand.ANTIICE_PROBE_B_HEAT_SET, new[] { "Auto", "On" });

        // Probe heat annunciators (all eight).
        Annun(P, "CAPT_PITOT_Light_Status", "Captain Pitot light");
        Annun(P, "L_ELEV_PITOT_Light_Status", "Left Elevator Pitot light");
        Annun(P, "L_ALPHA_VANE_Light_Status", "Left Alpha Vane light");
        Annun(P, "TEMP_PROBE_Light_Status", "Temperature Probe light");
        Annun(P, "FO_PITOT_Light_Status", "First Officer Pitot light");
        Annun(P, "R_ELEV_PITOT_Light_Status", "Right Elevator Pitot light");
        Annun(P, "R_ALPHA_VANE_Light_Status", "Right Alpha Vane light");
        Annun(P, "AUX_PITOT_Light_Status", "Auxiliary Pitot light");

        // Wing + engine anti-ice (0:OFF 1:ON, SET Value2 matches).
        Sw(P, "Wing_AntiIce_Switch_Status", "Wing Anti-Ice",
            IFlyKeyCommand.ANTIICE_WING_SET, new[] { "Off", "On" });
        Sw(P, "Eng_1_AntiIce_Switch_Status", "Engine 1 Anti-Ice",
            IFlyKeyCommand.ANTIICE_ENG_1_SET, new[] { "Off", "On" });
        Sw(P, "Eng_2_AntiIce_Switch_Status", "Engine 2 Anti-Ice",
            IFlyKeyCommand.ANTIICE_ENG_2_SET, new[] { "Off", "On" });

        Annun(P, "L_VALVE_Light_Status", "Left Wing Anti-Ice Valve light");
        Annun(P, "R_VALVE_Light_Status", "Right Wing Anti-Ice Valve light");
        Annun(P, "COWL_ANTI_ICE_1_Light_Status", "Engine 1 Cowl Anti-Ice light");
        Annun(P, "COWL_ANTI_ICE_2_Light_Status", "Engine 2 Cowl Anti-Ice light");
        Annun(P, "COWL_VALVE_1_Light_Status", "Engine 1 Cowl Valve light");
        Annun(P, "COWL_VALVE_2_Light_Status", "Engine 2 Cowl Valve light");
        Annun(P, "ENG_ANTI_ICE_1_Light_Status", "Engine 1 Anti-Ice light");
        Annun(P, "ENG_ANTI_ICE_2_Light_Status", "Engine 2 Anti-Ice light");

        // Ice detector light — only fitted on panels with the ice detector option
        // (IceDetectorSystem flag); registered anyway, it simply never lights otherwise.
        Annun(P, "ICE_DETECTOR_Light_Status", "Ice Detector light");
        // IceDetectorSystem skipped — airframe configuration flag, not a cockpit state.

        // Windshield wipers (0:PARK 1:INT 2:LOW 3:HIGH, SET Value2 matches).
        Sw(P, "Wiper_L_Switch_Status", "Left Windshield Wiper",
            IFlyKeyCommand.ANTIICE_LEFT_WINDSHIELD_WIPER_SET, new[] { "Park", "Intermittent", "Low", "High" });
        Sw(P, "Wiper_R_Switch_Status", "Right Windshield Wiper",
            IFlyKeyCommand.ANTIICE_RIGHT_WINDSHIELD_WIPER_SET, new[] { "Park", "Intermittent", "Low", "High" });

        // TAT test (momentary press).
        Btn(P, "BTN_TAT_TEST", "TAT Test", IFlyKeyCommand.ANTIICE_TAT_TEST);
        // TAT_Test_Switch_Status skipped — momentary press readback.
    }

    // =========================================================================
    // Engines and APU
    // =========================================================================

    private void RegisterEnginesApu()
    {
        const string P = "Engines and APU";

        // Ignition select (status 0:IGN L 1:BOTH 2:IGN R; SET Value2 0:switch 1, 1:BOTH, 2:switch 2 — same order).
        Sw(P, "Ignition_Select_Switch_Status", "Ignition Select",
            IFlyKeyCommand.ENGAPU_IGNITION_SELECT_SET, new[] { "Ignition Left", "Both", "Ignition Right" });

        // Engine start switches (0:GRD 1:OFF 2:CONT 3:FLT, SET Value2 matches).
        // Each engine's START LEVER sits directly after its start switch
        // (Start 1, Lever 1, Start 2, Lever 2) — the PMDG 737 "Engines" panel
        // ordering; the levers moved here from the Control Stand panel
        // 2026-07-24 by user request (PMDG parity — physically they live on the
        // control stand, but the start flow belongs together).
        //
        // Start levers: status 0-2 CUTOFF × fire light, 3-5 IDLE × fire light.
        // ⚠ ENCODING TRAP: ENGAPU_ENG_n_START_LEVER_SET Value2 is 0 = IDLE,
        // 1 = CUTOFF (INVERTED vs. the intuitive order) → map v >= 3 ? 0 : 1.
        var startLeverStates = new Dictionary<double, string>
        {
            [0] = "Cutoff",
            [1] = "Cutoff, fire light dim",
            [2] = "Cutoff, fire light bright",
            [3] = "Idle",
            [4] = "Idle, fire light dim",
            [5] = "Idle, fire light bright",
        };
        Sw(P, "Engine_Start_Switch_Status_0", "Engine 1 Start",
            IFlyKeyCommand.ENGAPU_ENG_1_START_SET, new[] { "Ground", "Off", "Continuous", "Flight" });
        SwD(P, "Engine_Start_Lever_Status_0", "Engine 1 Start Lever",
            IFlyKeyCommand.ENGAPU_ENG_1_START_LEVER_SET, startLeverStates, map: v => v >= 3 ? 0 : 1);
        Sw(P, "Engine_Start_Switch_Status_1", "Engine 2 Start",
            IFlyKeyCommand.ENGAPU_ENG_2_START_SET, new[] { "Ground", "Off", "Continuous", "Flight" });
        SwD(P, "Engine_Start_Lever_Status_1", "Engine 2 Start Lever",
            IFlyKeyCommand.ENGAPU_ENG_2_START_LEVER_SET, startLeverStates, map: v => v >= 3 ? 0 : 1);

        // Readback legitimately differs from the picked value (fire-light bit folded
        // into the same 0-5 field) — suppress the post-set echo on the time window
        // alone. (PR #163, minor 15.)
        foreach (var k in new[] { "Engine_Start_Lever_Status_0", "Engine_Start_Lever_Status_1" })
            _vars[k].UiEchoMatchesAnyValue = true;

        // EEC switches: the status is a complex 0-11 encoding (switch OFF/ON x ON-light
        // off/dim/bright x guard open/closed); EEC_n_SET Value2 is just 0:ALTN 1:ON
        // (the SET doc calls the OFF position "ALTN" — same physical position), so any
        // status >= 6 means the switch is ON -> map v => v >= 6 ? 1 : 0. No Value3
        // guard-bypass exists for the EEC SET.
        var eecStates = new Dictionary<double, string>
        {
            [0] = "Off",
            [1] = "Off, guard open",
            [2] = "Off, light on",
            [3] = "Off, light bright",
            [4] = "Off, light on, guard open",
            [5] = "Off, light bright, guard open",
            [6] = "On",
            [7] = "On, guard open",
            [8] = "On, light on",
            [9] = "On, light bright",
            [10] = "On, light on, guard open",
            [11] = "On, light bright, guard open",
        };
        SwD(P, "EEC_Switch_Status_0", "Engine 1 Electronic Engine Control",
            IFlyKeyCommand.ENGAPU_EEC_1_SET, eecStates, map: v => v >= 6 ? 1 : 0);
        SwD(P, "EEC_Switch_Status_1", "Engine 2 Electronic Engine Control",
            IFlyKeyCommand.ENGAPU_EEC_2_SET, eecStates, map: v => v >= 6 ? 1 : 0);

        // Readback legitimately differs from the picked value (guard/light bit folded
        // into the same 0-11 field) — suppress the post-set echo on the time window
        // alone. (PR #163, minor 15.)
        foreach (var k in new[] { "EEC_Switch_Status_0", "EEC_Switch_Status_1" })
            _vars[k].UiEchoMatchesAnyValue = true;

        // APU switch: 0:OFF 1:ON 2:START (SET Value2 matches). START is spring-loaded —
        // it snaps back to ON once released, so the combo will re-read ON after a start.
        Sw(P, "APU_Switch_Status", "APU",
            IFlyKeyCommand.ENGAPU_APU_SET, new[] { "Off", "On", "Start" });

        // Fuel flow switch: 0:RESET 1:RATE 2:USED (SET Value2 matches; RESET is spring-loaded).
        Sw(P, "Fuel_Flow_Switch_Status", "Fuel Flow Switch",
            IFlyKeyCommand.ENGAPU_FUEL_FLOW_SET, new[] { "Reset", "Rate", "Used" });

        // APU annunciators.
        Annun(P, "DOOR_Light_Status", "APU Door light");
        Annun(P, "LOW_OIL_PRESSURE_Light_Status", "APU Low Oil Pressure light");
        Annun(P, "FAULT_Light_Status", "APU Fault light");
        Annun(P, "OVER_SPEED_Light_Status", "APU Overspeed light");

        // Reverser + engine control annunciators.
        Annun(P, "REVERSER_LIMITED_Light_Status_0", "Engine 1 Reverser Limited light");
        Annun(P, "REVERSER_LIMITED_Light_Status_1", "Engine 2 Reverser Limited light");
        Annun(P, "REVERSER_COMMAND_Light_Status", "Reverser Command light");
        Annun(P, "REVERSER_AIRGND_Light_Status", "Reverser Air Ground light");
        Annun(P, "ENGINE_CONTROL_Light_Status_0", "Engine 1 Control light");
        Annun(P, "ENGINE_CONTROL_Light_Status_1", "Engine 2 Control light");

        // Skipped (Control Stand panel scope, registered elsewhere or deliberately not):
        // Throttle_Lever_1/2_Position, Reverse_Lever_1/2_Position, Reverse_Lever_1/2_locked.
    }
}
