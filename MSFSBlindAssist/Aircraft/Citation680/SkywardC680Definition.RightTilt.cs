using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Right Tilt Panel: Pressurization and Bleed, Cabin Environment, Hydraulics, Fuel, Oxygen and
/// Emergency. Every selector's position words are the model's own (checked live where the
/// order was not obvious — see docs/citation680-variables.md).
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildRightTiltVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- Pressurization and Bleed
        AddSelector(v, "C680_PRESS_SRC", "SW_SOV_PRESS_SRC", "PRESS SOURCE Knob", new[] { "Off", "Left", "Both", "Right", "Emer" });
        AddSelector(v, "C680_BLEED_L", "SW_SOV_L_BLEED_AIR", "Left BLEED AIR Knob", new[] { "Off", "Low", "Norm", "High" });
        AddSelector(v, "C680_BLEED_R", "SW_SOV_R_BLEED_AIR", "Right BLEED AIR Knob", new[] { "Off", "Low", "Norm", "High" });
        AddSwitch(v, "C680_PRESS_MODE", "SW_SOV_PRESS_MODE", "PRESS MODE Button", "Auto", "Manual");
        AddSelector(v, "C680_CABIN_ALT_SW", "SW_SOV_CABIN_ALT_SWITCH_POS", "CABIN ALT Switch", new[] { "Center" },
            "Momentary: Up or Down while held, back to Center by itself.");
        v["C680_CABIN_ALT_SW"].ValueDescriptions = new Dictionary<double, string> { [-1] = "Down", [0] = "Center", [1] = "Up" }; // the model stores -1/0/1
        AddKnob(v, "C680_PRESS_RATE", "SW_SOV_PRESSURIZATION_RATE", "Pressurization Rate Knob");
        AddReadout(v, "C680_CABIN_ALT", "SW_SOV_CABIN_ALT", "Cabin Altitude", "number", "F0");
        AddSimReadout(v, "C680_CABIN_RATE", "PRESSURIZATION CABIN ALTITUDE RATE", "Cabin Rate", "feet per minute", "F0");
        AddSimReadout(v, "C680_CABIN_DIFF", "PRESSURIZATION PRESSURE DIFFERENTIAL", "Cabin Differential", "psi", "F1");
        AddSimReadout(v, "C680_LDG_ELEV", "PRESSURIZATION LANDING ELEVATION", "Landing Elevation (set on the MFD touchscreen)", "feet", "F0");
        AddFlag(v, "C680_PRESS_EMER", "SW_SOV_BLEED_EMER_ACTIVE", "Emergency Pressurization", "Off", "Active");
        AddFlag(v, "C680_DUCT_CKPT", "SW_SOV_BLEED_COCKPIT_DUCT_ACTIVE", "Cockpit Duct", "Closed", "Flowing");
        AddFlag(v, "C680_DUCT_CABIN", "SW_SOV_BLEED_CABIN_DUCT_ACTIVE", "Cabin Duct", "Closed", "Flowing");
        AddFlag(v, "C680_BLEED_ENG_L", "BLEED AIR ENGINE:1", "Left Engine Bleed", "Off", "On", simvar: true);
        AddFlag(v, "C680_BLEED_ENG_R", "BLEED AIR ENGINE:2", "Right Engine Bleed", "Off", "On", simvar: true);
        AddFlag(v, "C680_ECS_ACTIVE", "SW_SOV_ECS_ACTIVE", "Environmental Control", "Off", "Active");

        // ---- Cabin Environment (B:SOV_ECS_n_Temp_Inc/Dec drive the two temperature selectors)
        AddReadout(v, "C680_CKPT_TMP_SEL", "SW_SOV_CKPT_TMP_SEL", "Cockpit Temperature Select", "number", "F0");
        AddButton(v, "C680_CKPT_TMP_UP", "Cockpit Temperature Warmer");
        AddButton(v, "C680_CKPT_TMP_DN", "Cockpit Temperature Cooler");
        AddReadout(v, "C680_CABIN_TMP_SEL", "SW_SOV_CABIN_TMP_SEL", "Cabin Temperature Select", "number", "F0");
        AddButton(v, "C680_CABIN_TMP_UP", "Cabin Temperature Warmer");
        AddButton(v, "C680_CABIN_TMP_DN", "Cabin Temperature Cooler");
        AddSwitch(v, "C680_CABIN_CONTROL", "SW_SOV_CABIN_CONTROL", "Cabin Temperature Control", "Cockpit", "Cabin");
        AddSwitch(v, "C680_BAG_HEAT", "SW_SOV_BLEED_BAGHEAT_MODE", "BAG HEAT Button");
        AddReadout(v, "C680_CKPT_TMP", "SW_SOV_CKPT_TMP_CUR", "Cockpit Temperature", "number", "F0");
        AddReadout(v, "C680_CABIN_TMP", "SW_SOV_CABIN_TMP_CUR", "Cabin Temperature", "number", "F0");
        AddReadout(v, "C680_BAG_TMP", "Bag_Heat_Temp", "Baggage Temperature", "number", "F0");
        AddReadout(v, "C680_CKPT_FAN", "SW_SOV_Cockpit_Fan_1_Speed", "Cockpit Fan Speed", "number", "F0");
        AddReadout(v, "C680_AVN_FAN", "SW_SOV_Avionics_Fan_1_Speed", "Avionics Fan Speed", "number", "F0");

        // ---- Hydraulics (AUX PUMP is circuit AUX_HYD_PUMP_MT001 = systems.cfg circuit 120)
        AddSimSwitch(v, "C680_HYD_AUX", "CIRCUIT ON:120", "AUX HYD PUMP Button");
        AddSwitch(v, "C680_HYD_SW_1", "SW_SOV_HYDRAULICS_Switch_1", "Hydraulic Switch 1 (under cover)");
        AddSwitch(v, "C680_HYD_SW_2", "SW_SOV_HYDRAULICS_Switch_2", "Hydraulic Switch 2 (under cover)");
        AddReadout(v, "C680_HYD_PSI", "SW_SOV_HYD_PRESSURE", "Hydraulic Pressure", "number", "F0");
        AddReadout(v, "C680_HYD_QTY", "SW_SOV_HYD_RESERVOIR", "Hydraulic Reservoir", "number", "F0");
        AddFlag(v, "C680_HYD_PUMP_L", "SW_SOV_HYD_ENG1_PUMP", "Left Engine Pump", "Off", "On");
        AddFlag(v, "C680_HYD_PUMP_R", "SW_SOV_HYD_ENG2_PUMP", "Right Engine Pump", "Off", "On");
        AddFlag(v, "C680_HYD_AUX_ON", "SW_SOV_HYD_AUX_PUMP", "Aux Pump", "Off", "On");

        // ---- Fuel (checklist: BOOST PUMP, CROSSFEED)
        AddSwitch(v, "C680_BOOST_L", "SW_SOV_FUEL_PUMP_1_ON_SWITCH", "Left BOOST PUMP Button");
        AddSwitch(v, "C680_BOOST_R", "SW_SOV_FUEL_PUMP_2_ON_SWITCH", "Right BOOST PUMP Button");
        AddSelector(v, "C680_CROSSFEED", "SW_SOV_FUEL_TRANSFER", "CROSSFEED Knob", new[] { "Left Tank", "Off", "Right Tank" },
            "Feeds both engines from the named tank: its boost pump runs and the crossfeed valve opens.");
        AddSimReadout(v, "C680_FUEL_L_LB", "FUELSYSTEM TANK WEIGHT:1", "Left Tank", "pounds", "F0");
        AddSimReadout(v, "C680_FUEL_R_LB", "FUELSYSTEM TANK WEIGHT:2", "Right Tank", "pounds", "F0");
        AddSimReadout(v, "C680_FUEL_TOTAL_LB", "FUEL TOTAL QUANTITY WEIGHT", "Total Fuel", "pounds", "F0");
        AddSimReadout(v, "C680_FUEL_TEMP", "FUELSYSTEM TANK TEMPERATURE:1", "Fuel Temperature", "celsius", "F0");
        AddStateReadout(v, "C680_FUEL_LOW", "SW_SOV_FUEL_LEVEL_LOW", "Fuel Level Low",
            new Dictionary<double, string> { [0] = "No", [1] = "Left", [2] = "Right", [3] = "Both" });
        AddFlag(v, "C680_FUEL_IMBAL", "SW_SOV_FUEL_IMBALANCE_ACTIVE", "Fuel Imbalance", "No", "Yes");
        AddFlag(v, "C680_BOOST_L_ON", "FUELSYSTEM PUMP ACTIVE:1", "Left Boost Pump", "Off", "Running", simvar: true);
        AddFlag(v, "C680_BOOST_R_ON", "FUELSYSTEM PUMP ACTIVE:2", "Right Boost Pump", "Off", "Running", simvar: true);
        AddSimReadout(v, "C680_FUEL_USED_L", "GENERAL ENG FUEL USED SINCE START:1", "Left Fuel Used", "pounds", "F0");
        AddSimReadout(v, "C680_FUEL_USED_R", "GENERAL ENG FUEL USED SINCE START:2", "Right Fuel Used", "pounds", "F0");

        // ---- Oxygen and Emergency (PASS OXY knob = Mask_Selector_Position, checklist wording)
        AddSelector(v, "C680_PASS_OXY", "Mask_Selector_Position", "PASS OXY Knob", new[] { "Normal", "Off", "Manual" });
        AddSwitch(v, "C680_MASK_L", "Mask_Oxygen_L", "Left Crew Mask", "Stowed", "Donned");
        AddSwitch(v, "C680_MASK_R", "Mask_Oxygen_R", "Right Crew Mask", "Stowed", "Donned");
        AddButton(v, "C680_OXY_TEST_L", "Left Oxygen Test Button");
        AddButton(v, "C680_OXY_TEST_R", "Right Oxygen Test Button");
        AddSwitch(v, "C680_OXY_TWO", "SW_SOV_OXYGEN_TWO_BOTTLES", "Second Oxygen Bottle", "Not installed", "Installed");
        AddReadout(v, "C680_OXY_L_PSI", "Oxygen_Tank_L_Gauge", "Left Oxygen Pressure (raw)", "number", "F0");
        AddReadout(v, "C680_OXY_R_PSI", "Oxygen_Tank_R_Gauge", "Right Oxygen Pressure (raw)", "number", "F0");
        AddReadout(v, "C680_OXY_FLOW", "Oxy_Flow", "Oxygen Flow", "number", "F1");
        AddSwitch(v, "C680_CVR_HEADSET", "SW_SOV_CVR_headset_button", "CVR Headset Button");
        AddButton(v, "C680_CVR_TEST", "CVR Test Button (hold)");
        AddButton(v, "C680_CVR_ERASE", "CVR Erase Button (hold)");
        AddFlag(v, "C680_CVR_IND", "CVR_indicator", "CVR Test Light", "Out", "Lit");
        AddSimSwitch(v, "C680_ELT", "ELT ACTIVATED", "ELT Switch", "Armed", "On",
            "The stock emergency locator transmitter switch. On transmits; the model reports only whether it is transmitting.");

        foreach (var k in new[] { "C680_FUEL_L_LB", "C680_FUEL_R_LB", "C680_FUEL_TOTAL_LB", "C680_CABIN_ALT", "C680_CABIN_RATE", "C680_CABIN_DIFF", "C680_HYD_PSI", "C680_OXY_L_PSI" })
            Cache(v, k);
        return v;
    }

    private static readonly List<string> PressControls = new() { "C680_PRESS_SRC", "C680_BLEED_L", "C680_BLEED_R", "C680_PRESS_MODE", "C680_CABIN_ALT_SW", "C680_PRESS_RATE" };
    private static readonly List<string> PressDisplay = new() { "C680_CABIN_ALT", "C680_CABIN_RATE", "C680_CABIN_DIFF", "C680_LDG_ELEV", "C680_PRESS_EMER", "C680_DUCT_CKPT", "C680_DUCT_CABIN", "C680_BLEED_ENG_L", "C680_BLEED_ENG_R", "C680_ECS_ACTIVE" };
    private static readonly List<string> EnvironmentControls = new() { "C680_CKPT_TMP_UP", "C680_CKPT_TMP_DN", "C680_CABIN_TMP_UP", "C680_CABIN_TMP_DN", "C680_CABIN_CONTROL", "C680_BAG_HEAT" };
    private static readonly List<string> EnvironmentDisplay = new() { "C680_CKPT_TMP_SEL", "C680_CKPT_TMP", "C680_CABIN_TMP_SEL", "C680_CABIN_TMP", "C680_BAG_TMP", "C680_CKPT_FAN", "C680_AVN_FAN" };
    private static readonly List<string> HydraulicsControls = new() { "C680_HYD_AUX", "C680_HYD_SW_1", "C680_HYD_SW_2" };
    private static readonly List<string> HydraulicsDisplay = new() { "C680_HYD_PSI", "C680_HYD_QTY", "C680_HYD_PUMP_L", "C680_HYD_PUMP_R", "C680_HYD_AUX_ON" };
    private static readonly List<string> FuelControls = new() { "C680_BOOST_L", "C680_BOOST_R", "C680_CROSSFEED" };
    private static readonly List<string> FuelDisplay = new() { "C680_FUEL_L_LB", "C680_FUEL_R_LB", "C680_FUEL_TOTAL_LB", "C680_FUEL_TEMP", "C680_FUEL_LOW", "C680_FUEL_IMBAL", "C680_BOOST_L_ON", "C680_BOOST_R_ON", "C680_FUEL_USED_L", "C680_FUEL_USED_R" };
    private static readonly List<string> OxygenControls = new() { "C680_PASS_OXY", "C680_MASK_L", "C680_MASK_R", "C680_OXY_TEST_L", "C680_OXY_TEST_R", "C680_OXY_TWO", "C680_CVR_HEADSET", "C680_CVR_TEST", "C680_CVR_ERASE", "C680_ELT" };
    private static readonly List<string> OxygenDisplay = new() { "C680_OXY_L_PSI", "C680_OXY_R_PSI", "C680_OXY_FLOW", "C680_CVR_IND" };

    private static readonly HashSet<string> RightTiltPlainLVars = new(StringComparer.Ordinal)
    {
        "C680_PRESS_SRC", "C680_BLEED_L", "C680_BLEED_R", "C680_PRESS_MODE", "C680_CABIN_ALT_SW", "C680_PRESS_RATE",
        "C680_CABIN_CONTROL", "C680_BAG_HEAT", "C680_HYD_SW_1", "C680_HYD_SW_2", "C680_BOOST_L", "C680_BOOST_R", "C680_CROSSFEED",
        "C680_PASS_OXY", "C680_MASK_L", "C680_MASK_R", "C680_OXY_TWO", "C680_CVR_HEADSET"
    };

    private bool HandleRightTiltSet(string varKey, double value, SimConnectManager sc)
    {
        if (RightTiltPlainLVars.Contains(varKey))
        {
            sc.SetLVar(GetVariables()[varKey].Name, value);
            return true;
        }
        switch (varKey)
        {
            case "C680_CKPT_TMP_UP": sc.ExecuteCalculatorCodeUnique("(>B:SOV_ECS_1_Temp_Inc)"); return true;
            case "C680_CKPT_TMP_DN": sc.ExecuteCalculatorCodeUnique("(>B:SOV_ECS_1_Temp_Dec)"); return true;
            case "C680_CABIN_TMP_UP": sc.ExecuteCalculatorCodeUnique("(>B:SOV_ECS_2_Temp_Inc)"); return true;
            case "C680_CABIN_TMP_DN": sc.ExecuteCalculatorCodeUnique("(>B:SOV_ECS_2_Temp_Dec)"); return true;
            case "C680_ELT": sc.ExecuteCalculatorCode($"{Rpn(value)} (>B:SAFETY_ELT_1_Set)"); return true;
            case "C680_HYD_AUX": sc.ExecuteCalculatorCode($"(A:CIRCUIT ON:120, Bool) {(value > 0.5 ? 0 : 1)} == if{{ 120 (>K:ELECTRICAL_CIRCUIT_TOGGLE) }}"); return true;
            case "C680_OXY_TEST_L": Pulse(sc, "Oxygen_L_Test", 1500); return true;
            case "C680_OXY_TEST_R": Pulse(sc, "Oxygen_R_Test", 1500); return true;
            case "C680_CVR_TEST": Pulse(sc, "SW_SOV_CVR_test_button", 5500); return true;
            case "C680_CVR_ERASE": Pulse(sc, "SW_SOV_CVR_erase_button", 2500); return true;
        }
        return false;
    }
}
