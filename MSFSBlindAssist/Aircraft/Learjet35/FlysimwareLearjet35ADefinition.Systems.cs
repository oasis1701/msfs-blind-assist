using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Anti-Ice and Fuel Computer Panel (Anti-Ice; Fuel Computers and Avionics), Test Panel →
/// Systems Test, Lower Center Panel → Lower Center Switches, Climate and Lights Panel →
/// Climate and Exterior Lights.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string AntiIcePanel = "Anti-Ice";
    private const string FuelComputerPanel = "Fuel Computers and Avionics";
    private const string TestPanel = "Systems Test";
    private const string LowerCenterPanel = "Lower Center Switches";
    private const string ClimatePanel = "Climate and Exterior Lights";

    private static Dictionary<string, SimVarDefinition> BuildSystemsVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- anti-ice ----
        AddThreeWay(v, "LJ35_WSHLD_HEAT", "LEAR_WSHLD_HEAT", "Windshield Heat", "On", "Hold", "Off");
        AddThreeWay(v, "LJ35_WSHLD_RADOME", "LEAR_WSHLD_RADOME", "Windshield and Radome Anti-Ice", "Windshield and radome", "Radome", "Off");
        AddSimSwitch(v, "LJ35_WING_STAB_AI", "STRUCTURAL DEICE SWITCH", "Wing and Stabilizer Heat");
        AddSimSwitch(v, "LJ35_PITOT_L", "PITOT HEAT SWITCH:1", "Left Pitot Heat");
        AddSimSwitch(v, "LJ35_PITOT_R", "PITOT HEAT SWITCH:2", "Right Pitot Heat");
        AddSimSwitch(v, "LJ35_NAC_HEAT_L", "ENG ANTI ICE:1", "Left Nacelle Heat");
        AddSimSwitch(v, "LJ35_NAC_HEAT_R", "ENG ANTI ICE:2", "Right Nacelle Heat");
        AddThreeWay(v, "LJ35_STATIC_SOURCE", "LEAR_SW_STATIC_SOURCE", "Static Source", "Left", "Both", "Right");
        AddSimReadout(v, "LJ35_ICE_STRUCT", "STRUCTURAL ICE PCT", "Airframe Ice", "percent", "F0");
        AddSimReadout(v, "LJ35_ICE_PITOT", "PITOT ICE PCT", "Pitot Ice", "percent", "F0");
        AddFlag(v, "LJ35_WSHLD_DEICE_ON", "WINDSHIELD DEICE SWITCH", "Windshield Anti-Ice", "Off", "On", simvar: true);
        Cache(v, "LJ35_ICE_STRUCT");

        // ---- fuel computers and avionics ----
        AddSwitch(v, "LJ35_FUEL_COMP_L", "LEAR_SW_COMPLEFT_1", "Left Fuel Computer");
        AddSwitch(v, "LJ35_FUEL_COMP_R", "LEAR_SW_COMPRIGHT_1", "Right Fuel Computer");
        AddSimSwitch(v, "LJ35_MARKER_SENS", "MARKER BEACON SENSITIVITY HIGH", "Marker Sensitivity", "Low", "High");
        AddSwitch(v, "LJ35_RADIO_ALT_PWR", "LEAR_SW_RADIO_ALT_1", "Radio Altimeter");
        AddThreeWay(v, "LJ35_SPR", "LEAR_SW_SPR", "Start Pressure Regulator", "Left", "Off", "Right", "Only during an engine start; springs back.");
        AddSwitch(v, "LJ35_AC_BUS", "LEAR_SW_AC_BUS", "AC Bus", "Secondary", "Primary");
        AddSwitch(v, "LJ35_SLAVE_PILOT", "LEAR_SW_SLAVE_FREE1", "Pilot Compass", "Free", "Slaved");
        AddThreeWay(v, "LJ35_SLAVE_DIR_PILOT", "LEAR_SW_SLAVE_LR1", "Pilot Compass Slew", "Left", "Off", "Right", "Springs back.");
        AddFlag(v, "LJ35_RADALT_CIRCUIT_ON", "CIRCUIT ON:227", "Radio Altimeter Circuit", "Off", "On", simvar: true);
        AddReadout(v, "LJ35_SPR_FLOW_L", "SPR_FLOW_LEFT", "Left SPR Flow", "number", "F0");
        AddReadout(v, "LJ35_SPR_FLOW_R", "SPR_FLOW_RIGHT", "Right SPR Flow", "number", "F0");
        AddSimReadout(v, "LJ35_GYRO_DRIFT", "GYRO DRIFT ERROR", "Gyro Drift", "degrees", "F1");

        // ---- systems test ----
        AddRotary(v, "LJ35_TEST_KNOB", "LEAR_TEST_KNOB", "Test Selector",
            new[] { "Test off", "Cabin altitude", "Mach", "Mach trim", "Fire detection", "Left stall", "Right stall", "Trim overspeed", "Trim monitor" });
        AddButton(v, "LJ35_TEST_BUTTON", "GENERIC_LEAR_TEST_BUTTON", "Test", "Holds the test button three seconds; the annunciator panel reports the result.");
        for (int i = 1; i <= 8; i++)
            AddFlag(v, "LJ35_TEST_MODE_" + i, "TEST_MODE_" + i, "Test " + i + " Running", "No", "Yes");
        AddFlag(v, "LJ35_TEST_3_DONE", "TEST_MODE_3_TIME_REACHED", "Mach Trim Test", "Not complete", "Complete");
        AddReadout(v, "LJ35_AOA_TEST_L", "AOA_Test5_Needle", "Left Stall Test Needle", "number", "F0");
        AddReadout(v, "LJ35_AOA_TEST_R", "AOA_Test6_Needle", "Right Stall Test Needle", "number", "F0");

        // ---- lower center ----
        AddSwitch(v, "LJ35_ANTISKID", "LEAR_SW_ANTISKID_1", "Anti-Skid");
        AddSwitch(v, "LJ35_STALL_L", "LEAR_STALL_L_1", "Left Stall Warning");
        AddSwitch(v, "LJ35_STALL_R", "LEAR_STALL_R_1", "Right Stall Warning");
        AddSimSwitch(v, "LJ35_HYD_PUMP", "HYDRAULIC SWITCH", "Hydraulic Auxiliary Pump");
        AddThreeWay(v, "LJ35_TAXI_LAND_L", "LEAR_TAXI_LAND_L", "Left Landing and Taxi Light", "Landing", "Taxi", "Off");
        AddThreeWay(v, "LJ35_TAXI_LAND_R", "LEAR_TAXI_LAND_R", "Right Landing and Taxi Light", "Landing", "Taxi", "Off");
        AddThreeWay(v, "LJ35_SMOKING_BELTS", "LEAR_SW_SMOKING", "Passenger Signs", "No smoking and seat belts", "Off", "Seat belts");
        AddButton(v, "LJ35_HORN_SILENCE", "GENERIC_LEAR_SW_HORN_SILENCE_1", "Cabin Altitude Horn Silence");
        AddButton(v, "LJ35_SPOILERON_RESET", "GENERIC_LEAR_SPOILERON_RESET", "Spoileron Reset", "Not simulated by the vendor.");
        AddFlag(v, "LJ35_ANTISKID_ACTIVE", "ANTISKID BRAKES ACTIVE", "Anti-Skid", "Inactive", "Active", simvar: true);
        AddFlag(v, "LJ35_STALL_SHAKER", "STALL_SHAKER", "Stick Shaker", "Off", "Shaking");
        AddFlag(v, "LJ35_HORN_SILENCED", "HORN_SILENCE_ENABLED", "Cabin Altitude Horn", "Armed", "Silenced");
        AddReadout(v, "LJ35_TEMP_STAB", "TEMP_STAB", "Stabilizer Temperature", "number", "F0");
        AddFlag(v, "LJ35_SEATBELT_SIGN", "CABIN SEATBELTS ALERT SWITCH", "Seat Belt Sign", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_NO_SMOKING_SIGN", "CABIN NO SMOKING ALERT SWITCH", "No Smoking Sign", "Off", "On", simvar: true);

        // ---- climate and exterior lights ----
        AddSwitch(v, "LJ35_TEMP_AUTO", "LEAR_TEMP_AUTO_1", "Cabin Temperature", "Manual", "Auto");
        AddDimmer(v, "LJ35_TEMP_KNOB", "XMLVAR_LEAR_TEMP_CONTROL_Position", "Temperature Control (Cold to Hot)");
        AddThreeWay(v, "LJ35_COOL_FAN", "LEAR_TEMP_FAN", "Cool Fan", "Cool", "Off", "Fan", "Off for engine start.");
        AddThreeWay(v, "LJ35_BLEED_L", "LEAR_SW_BLEED_L", "Left Bleed Air", "Emergency", "On", "Off");
        AddThreeWay(v, "LJ35_BLEED_R", "LEAR_SW_BLEED_R", "Right Bleed Air", "Emergency", "On", "Off");
        AddSimSwitch(v, "LJ35_LT_RECOG", "LIGHT RECOGNITION", "Recognition Lights");
        AddSimSwitch(v, "LJ35_LT_STROBE", "LIGHT STROBE", "Strobe Lights");
        AddSimSwitch(v, "LJ35_LT_NAV", "LIGHT NAV", "Navigation Lights");
        AddSimSwitch(v, "LJ35_LT_BEACON", "LIGHT BEACON", "Beacon");
        AddSwitch(v, "LJ35_SLAVE_COPILOT", "LEAR_SW_SLAVE_FREE2", "Copilot Compass", "Free", "Slaved");
        AddThreeWay(v, "LJ35_SLAVE_DIR_COPILOT", "LEAR_SW_SLAVE_LR2", "Copilot Compass Slew", "Left", "Off", "Right", "Springs back.");
        AddReadout(v, "LJ35_TEMP_CONT", "TEMP_CONT", "Cabin Temperature Control", "number", "F0");
        AddFlag(v, "LJ35_FREON_CIRCUIT", "CIRCUIT ON:142", "Freon Cooling", "Off", "On", simvar: true);
        AddSimReadout(v, "LJ35_BLEED_SOURCE", "BLEED AIR SOURCE CONTROL", "Bleed Source", "number", "F0");
        return v;
    }

    private static readonly List<string> AntiIceControls = new()
    {
        "LJ35_WSHLD_HEAT", "LJ35_WSHLD_RADOME", "LJ35_WING_STAB_AI", "LJ35_NAC_HEAT_L", "LJ35_NAC_HEAT_R",
        "LJ35_PITOT_L", "LJ35_PITOT_R", "LJ35_STATIC_SOURCE"
    };
    private static readonly List<string> AntiIceDisplay = new() { "LJ35_ICE_STRUCT", "LJ35_ICE_PITOT", "LJ35_WSHLD_DEICE_ON", "LJ35_OAT" };
    private static readonly List<string> FuelComputerControls = new()
    {
        "LJ35_FUEL_COMP_L", "LJ35_FUEL_COMP_R", "LJ35_SPR", "LJ35_AC_BUS", "LJ35_RADIO_ALT_PWR", "LJ35_MARKER_SENS",
        "LJ35_SLAVE_PILOT", "LJ35_SLAVE_DIR_PILOT"
    };
    private static readonly List<string> FuelComputerDisplay = new()
    {
        "LJ35_SPR_FLOW_L", "LJ35_SPR_FLOW_R", "LJ35_RADALT_CIRCUIT_ON", "LJ35_GYRO_DRIFT"
    };
    private static readonly List<string> TestControls = new() { "LJ35_TEST_KNOB", "LJ35_TEST_BUTTON" };
    private static readonly List<string> TestDisplay = new()
    {
        "LJ35_TEST_MODE_1", "LJ35_TEST_MODE_2", "LJ35_TEST_MODE_3", "LJ35_TEST_3_DONE", "LJ35_TEST_MODE_4",
        "LJ35_TEST_MODE_5", "LJ35_AOA_TEST_L", "LJ35_TEST_MODE_6", "LJ35_AOA_TEST_R", "LJ35_TEST_MODE_7", "LJ35_TEST_MODE_8"
    };
    private static readonly List<string> LowerCenterControls = new()
    {
        "LJ35_ANTISKID", "LJ35_STALL_L", "LJ35_STALL_R", "LJ35_HYD_PUMP", "LJ35_TAXI_LAND_L", "LJ35_TAXI_LAND_R",
        "LJ35_SMOKING_BELTS", "LJ35_HORN_SILENCE", "LJ35_SPOILERON_RESET"
    };
    private static readonly List<string> LowerCenterDisplay = new()
    {
        "LJ35_ANTISKID_ACTIVE", "LJ35_STALL_SHAKER", "LJ35_HORN_SILENCED", "LJ35_TEMP_STAB",
        "LJ35_SEATBELT_SIGN", "LJ35_NO_SMOKING_SIGN"
    };
    private static readonly List<string> ClimateControls = new()
    {
        "LJ35_TEMP_AUTO", "LJ35_TEMP_KNOB", "LJ35_COOL_FAN", "LJ35_BLEED_L", "LJ35_BLEED_R",
        "LJ35_LT_RECOG", "LJ35_LT_STROBE", "LJ35_LT_NAV", "LJ35_LT_BEACON", "LJ35_SLAVE_COPILOT", "LJ35_SLAVE_DIR_COPILOT"
    };
    private static readonly List<string> ClimateDisplay = new() { "LJ35_TEMP_CONT", "LJ35_FREON_CIRCUIT", "LJ35_BLEED_SOURCE" };

    private static bool HandleSystemsSet(string varKey, double value, SimConnectManager sc)
    {
        bool on = value > 0.5;
        switch (varKey)
        {
            case "LJ35_WSHLD_HEAT": sc.SetLVar("GENERIC_Momentary_LEAR_WSHLD_HEAT", value); return true;
            case "LJ35_WSHLD_RADOME": sc.SetLVar("GENERIC_Momentary_LEAR_WSHLD_RADOME", value); return true;
            case "LJ35_WING_STAB_AI": ToggleTo(sc, "STRUCTURAL DEICE SWITCH", "Bool", on, "TOGGLE_STRUCTURAL_DEICE"); return true;
            case "LJ35_PITOT_L": sc.ExecuteCalculatorCode($"(A:PITOT HEAT SWITCH:1, Bool) {(on ? 0 : 1)} == if{{ 1 (>K:PITOT_HEAT_TOGGLE) }}"); return true;
            case "LJ35_PITOT_R": sc.ExecuteCalculatorCode($"(A:PITOT HEAT SWITCH:2, Bool) {(on ? 0 : 1)} == if{{ 2 (>K:PITOT_HEAT_TOGGLE) }}"); return true;
            case "LJ35_NAC_HEAT_L": ToggleTo(sc, "ENG ANTI ICE:1", "Bool", on, "ANTI_ICE_TOGGLE_ENG1"); return true;
            case "LJ35_NAC_HEAT_R": ToggleTo(sc, "ENG ANTI ICE:2", "Bool", on, "ANTI_ICE_TOGGLE_ENG2"); return true;
            case "LJ35_STATIC_SOURCE": sc.SetLVar("GENERIC_Momentary_LEAR_SW_STATIC_SOURCE", value); return true;

            case "LJ35_FUEL_COMP_L": sc.SetLVar("GENERIC_LEAR_SW_COMPLEFT_1", value); return true;
            case "LJ35_FUEL_COMP_R": sc.SetLVar("GENERIC_LEAR_SW_COMPRIGHT_1", value); return true;
            case "LJ35_MARKER_SENS": sc.ExecuteCalculatorCode($"{(on ? 1 : 0)} (>K:MARKER_BEACON_SENSITIVITY_HIGH)"); return true;
            case "LJ35_RADIO_ALT_PWR": sc.SetLVar("GENERIC_LEAR_SW_RADIO_ALT_1", value); return true;
            case "LJ35_SPR": sc.SetLVar("GENERIC_Momentary_LEAR_SW_SPR", value); return true;
            case "LJ35_AC_BUS": sc.SetLVar("GENERIC_LEAR_SW_AC_BUS", value); return true;
            case "LJ35_SLAVE_PILOT": sc.SetLVar("GENERIC_LEAR_SW_SLAVE_FREE1", value); return true;
            case "LJ35_SLAVE_DIR_PILOT": sc.SetLVar("GENERIC_Momentary_LEAR_SW_SLAVE_LR1", value); return true;

            case "LJ35_TEST_KNOB": sc.SetLVar("XMLVAR_LEAR_TEST_KNOB_Position", value); return true;
            case "LJ35_TEST_BUTTON": Pulse(sc, "GENERIC_LEAR_TEST_BUTTON", 3000); return true;

            case "LJ35_ANTISKID": sc.SetLVar("GENERIC_LEAR_SW_ANTISKID_1", value); return true;
            case "LJ35_STALL_L": sc.SetLVar("GENERIC_LEAR_STALL_L_1", value); return true;
            case "LJ35_STALL_R": sc.SetLVar("GENERIC_LEAR_STALL_R_1", value); return true;
            case "LJ35_HYD_PUMP": ToggleTo(sc, "HYDRAULIC SWITCH", "Bool", on, "HYDRAULIC_SWITCH_TOGGLE"); return true;
            case "LJ35_TAXI_LAND_L": sc.SetLVar("GENERIC_Momentary_LEAR_TAXI_LAND_L", value); return true;
            case "LJ35_TAXI_LAND_R": sc.SetLVar("GENERIC_Momentary_LEAR_TAXI_LAND_R", value); return true;
            case "LJ35_SMOKING_BELTS": sc.SetLVar("GENERIC_Momentary_LEAR_SW_SMOKING", value); return true;
            case "LJ35_HORN_SILENCE": Pulse(sc, "GENERIC_LEAR_SW_HORN_SILENCE_1"); return true;
            case "LJ35_SPOILERON_RESET": Pulse(sc, "GENERIC_LEAR_SPOILERON_RESET"); return true;

            case "LJ35_TEMP_AUTO": sc.SetLVar("GENERIC_LEAR_TEMP_AUTO_1", value); return true;
            case "LJ35_TEMP_KNOB": sc.SetLVar("XMLVAR_LEAR_TEMP_CONTROL_Position", value); return true;
            case "LJ35_COOL_FAN": sc.SetLVar("GENERIC_Momentary_LEAR_TEMP_FAN", value); return true;
            case "LJ35_BLEED_L": sc.SetLVar("GENERIC_Momentary_LEAR_SW_BLEED_L", value); return true;
            case "LJ35_BLEED_R": sc.SetLVar("GENERIC_Momentary_LEAR_SW_BLEED_R", value); return true;
            case "LJ35_LT_RECOG": ToggleTo(sc, "LIGHT RECOGNITION", "Bool", on, "TOGGLE_RECOGNITION_LIGHTS"); return true;
            case "LJ35_LT_STROBE": ToggleTo(sc, "LIGHT STROBE", "Bool", on, "STROBES_TOGGLE"); return true;
            case "LJ35_LT_NAV": ToggleTo(sc, "LIGHT NAV", "Bool", on, "TOGGLE_NAV_LIGHTS"); return true;
            case "LJ35_LT_BEACON": ToggleTo(sc, "LIGHT BEACON", "Bool", on, "TOGGLE_BEACON_LIGHTS"); return true;
            case "LJ35_SLAVE_COPILOT": sc.SetLVar("GENERIC_LEAR_SW_SLAVE_FREE2", value); return true;
            case "LJ35_SLAVE_DIR_COPILOT": sc.SetLVar("GENERIC_Momentary_LEAR_SW_SLAVE_LR2", value); return true;
        }
        return false;
    }
}
