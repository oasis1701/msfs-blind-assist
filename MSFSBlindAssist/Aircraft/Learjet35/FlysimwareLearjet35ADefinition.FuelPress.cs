using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Pressurization Panel → Pressurization, and Fuel System → Fuel.
/// Tanks (Fuel.xml): 1 left tip, 2 right tip, 3 left wing, 4 right wing, 5 fuselage.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string PressurizationPanel = "Pressurization";
    private const string FuelPanel = "Fuel";

    private static Dictionary<string, SimVarDefinition> BuildFuelPressVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- pressurization ----
        AddSwitch(v, "LJ35_CABIN_AIR", "CABIN_AIR_MODE", "Cabin Air", "Off", "On", "Bleed air to the cabin.");
        AddSwitch(v, "LJ35_CABIN_AUTO", "LEAR_CABIN_AUTO_SW", "Pressurization Mode", "Manual", "Auto");
        AddTyped(v, "LJ35_CABIN_ALT_SET", "Selected Cabin Altitude", "feet", "Minus 1000 to 10000.", "LJ35_CABIN_ALT_SEL");
        AddTyped(v, "LJ35_CABIN_RATE_SET", "Selected Cabin Rate", "feet per minute", "175 to 2500.", "LJ35_CABIN_RATE_SEL");
        AddThreeWay(v, "LJ35_CABIN_ROCKER", "LEAR_CABIN_PRESS_ROCKER", "Manual Cabin Control", "Up", "Off", "Down", "Manual mode only; springs back.");
        // The bleed-air circuit (systems.cfg circuit 43, AIR_BL, on the left essential bus).
        // The vendor's Pressurization.xml takes its "no pressurization" branch whenever this
        // circuit is unpowered, and NOTHING in the cockpit drives it: measured live 2026-09-08
        // with the switch found OFF at FL350 and the cabin following the aircraft up until the
        // circuit was toggled by hand. Exposed so a pilot can see and set it; it also reads
        // off while the essential bus is dead (generators off line), which is the other way
        // to lose pressurization.
        AddSimSwitch(v, "LJ35_BLEED_CIRCUIT", "CIRCUIT SWITCH ON:43", "Bleed Air Circuit", "Off", "On",
            "Unpowered means no pressurization. Needs the left essential bus.");
        AddReadout(v, "LJ35_CABIN_ALT", "LEAR_CABIN_ALT_NEEDLE", "Cabin Altitude", "feet", "F0");
        AddReadout(v, "LJ35_CABIN_DIFF", "LEAR_CABIN_ALT_DIFF_NEEDLE", "Cabin Differential", "psi", "F1");
        AddReadout(v, "LJ35_CABIN_RATE", "XMLVAR_LEAR_CABIN_ALTITUDE_RATE", "Cabin Rate", "feet per minute", "F0");
        AddReadout(v, "LJ35_CABIN_ALT_SEL", "LEAR_CABIN_PRESSURE_KNOB", "Selected Cabin Altitude", "feet", "F0");
        AddReadout(v, "LJ35_CABIN_RATE_SEL", "LEAR_CABIN_CLIMB_RATE", "Selected Cabin Rate", "feet per minute", "F0");
        AddDerived(v, "LJ35_CABIN_CATEGORY", "Cabin State");
        AddReadout(v, "LJ35_CABIN_CATEGORY_RAW", "CABIN_CATEGORY", "Cabin Category", "number", "F0");
        AddDerived(v, "LJ35_CABIN_MODE", "Controller Mode");
        AddReadout(v, "LJ35_CABIN_MODE_RAW", "CABIN_MODE", "Cabin Mode", "number", "F1");
        AddDerived(v, "LJ35_CABIN_LADDER", "Protection");
        Cache(v, "LJ35_CABIN_ALT"); Cache(v, "LJ35_CABIN_DIFF"); Cache(v, "LJ35_CABIN_RATE");
        Cache(v, "LJ35_CABIN_ALT_SEL"); Cache(v, "LJ35_CABIN_RATE_SEL"); Cache(v, "LJ35_CABIN_CATEGORY_RAW"); Cache(v, "LJ35_CABIN_MODE_RAW");

        // ---- fuel ----
        AddRotary(v, "LJ35_FUEL_SEL", "LEAR_FUEL_SEL", "Fuel Quantity Selector",
            new[] { "Left tip", "Left wing", "Fuselage", "Right wing", "Right tip", "Total" });
        AddButton(v, "LJ35_FUEL_RESET", "GENERIC_LEAR_FUEL_RESET", "Fuel Used Reset");
        AddSwitch(v, "LJ35_JET_PUMP_L", "LEAR_FUEL_MAINPUMP1_1", "Left Jet Pump");
        AddSwitch(v, "LJ35_JET_PUMP_R", "LEAR_FUEL_MAINPUMP2_1", "Right Jet Pump");
        AddSwitch(v, "LJ35_STBY_PUMP_L", "LEAR_FUEL_SECPUMP1_1", "Left Standby Pump");
        AddSwitch(v, "LJ35_STBY_PUMP_R", "LEAR_FUEL_SECPUMP2_1", "Right Standby Pump");
        AddSwitch(v, "LJ35_XFLOW", "LEAR_FUEL_XFEED_1", "Crossflow");
        AddSwitch(v, "LJ35_JETTISON", "LEAR_FUEL_JTSN_1", "Fuel Jettison", "Off", "On", "Empties the tip tanks only.");
        AddThreeWay(v, "LJ35_XFER_FILL", "LEAR_FUEL_XFER", "Fuselage Tank", "Transfer", "Off", "Fill");
        AddSimReadout(v, "LJ35_FUEL_TIP_L", "FUELSYSTEM TANK WEIGHT:1", "Left Tip", "pounds", "F0");
        AddSimReadout(v, "LJ35_FUEL_TIP_R", "FUELSYSTEM TANK WEIGHT:2", "Right Tip", "pounds", "F0");
        AddSimReadout(v, "LJ35_FUEL_WING_L", "FUELSYSTEM TANK WEIGHT:3", "Left Wing", "pounds", "F0");
        AddSimReadout(v, "LJ35_FUEL_WING_R", "FUELSYSTEM TANK WEIGHT:4", "Right Wing", "pounds", "F0");
        AddSimReadout(v, "LJ35_FUEL_FUS", "FUELSYSTEM TANK WEIGHT:5", "Fuselage", "pounds", "F0");
        AddSimReadout(v, "LJ35_FUEL_TOTAL", "FUEL TOTAL QUANTITY WEIGHT", "Total Fuel", "pounds", "F0");
        AddDerived(v, "LJ35_FUEL_ADVICE", "Transfer");
        AddFlag(v, "LJ35_PUMP_JET_L_ACTIVE", "FUELSYSTEM PUMP ACTIVE:1", "Left Jet Pump", "Off", "Running", simvar: true);
        AddFlag(v, "LJ35_PUMP_JET_R_ACTIVE", "FUELSYSTEM PUMP ACTIVE:2", "Right Jet Pump", "Off", "Running", simvar: true);
        AddFlag(v, "LJ35_PUMP_STBY_L_ACTIVE", "FUELSYSTEM PUMP ACTIVE:3", "Left Standby Pump", "Off", "Running", simvar: true);
        AddFlag(v, "LJ35_PUMP_STBY_R_ACTIVE", "FUELSYSTEM PUMP ACTIVE:4", "Right Standby Pump", "Off", "Running", simvar: true);
        foreach (var k in new[] { "LJ35_FUEL_TIP_L", "LJ35_FUEL_TIP_R", "LJ35_FUEL_WING_L", "LJ35_FUEL_WING_R", "LJ35_FUEL_FUS", "LJ35_FUEL_TOTAL" })
            Cache(v, k);
        return v;
    }

    private static readonly List<string> PressurizationControls = new()
    {
        "LJ35_CABIN_AIR", "LJ35_CABIN_AUTO", "LJ35_CABIN_ALT_SET", "LJ35_CABIN_RATE_SET", "LJ35_CABIN_ROCKER", "LJ35_BLEED_CIRCUIT"
    };
    private static readonly List<string> PressurizationDisplay = new()
    {
        "LJ35_CABIN_ALT", "LJ35_CABIN_DIFF", "LJ35_CABIN_RATE", "LJ35_CABIN_ALT_SEL", "LJ35_CABIN_RATE_SEL",
        "LJ35_CABIN_CATEGORY", "LJ35_CABIN_MODE", "LJ35_CABIN_LADDER"
    };
    private static readonly List<string> FuelControls = new()
    {
        "LJ35_JET_PUMP_L", "LJ35_JET_PUMP_R", "LJ35_STBY_PUMP_L", "LJ35_STBY_PUMP_R", "LJ35_XFLOW", "LJ35_XFER_FILL",
        "LJ35_JETTISON", "LJ35_FUEL_SEL", "LJ35_FUEL_RESET"
    };
    private static readonly List<string> FuelDisplay = new()
    {
        "LJ35_FUEL_TIP_L", "LJ35_FUEL_WING_L", "LJ35_FUEL_FUS", "LJ35_FUEL_WING_R", "LJ35_FUEL_TIP_R", "LJ35_FUEL_TOTAL",
        "LJ35_FUEL_ADVICE", "LJ35_FUEL_PRESS_L", "LJ35_FUEL_PRESS_R", "LJ35_PUMP_JET_L_ACTIVE", "LJ35_PUMP_JET_R_ACTIVE",
        "LJ35_PUMP_STBY_L_ACTIVE", "LJ35_PUMP_STBY_R_ACTIVE", "LJ35_XFLOW_VALVE", "LJ35_FUEL_USED"
    };

    private static bool HandleFuelPressSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_CABIN_AIR": sc.SetLVar("GENERIC_CABIN_AIR_MODE", value); return true;
            case "LJ35_CABIN_AUTO": sc.SetLVar("GENERIC_LEAR_CABIN_AUTO_SW", value); return true;
            case "LJ35_CABIN_ALT_SET": sc.SetLVar("XMLVAR_LEAR_CABIN_PRESSURE_KNOB_Position", Lj35Pressurization.AltitudeKnobPosition(value)); return true;
            case "LJ35_CABIN_RATE_SET": sc.SetLVar("XMLVAR_LEAR_CABIN_CLIMB_RATE_Position", Lj35Pressurization.RateKnobPosition(value)); return true;
            case "LJ35_CABIN_ROCKER": sc.SetLVar("GENERIC_Momentary_LEAR_CABIN_PRESS_ROCKER", value); return true;
            case "LJ35_BLEED_CIRCUIT":
                // A toggle, fired only when the switch disagrees with the pick (the same
                // conditional the transponder circuit uses), so a repeated set cannot flip it back.
                sc.ExecuteCalculatorCode($"(A:CIRCUIT SWITCH ON:43, Bool) {(value > 0.5 ? 0 : 1)} == if{{ 43 (>K:ELECTRICAL_CIRCUIT_TOGGLE) }}");
                return true;

            case "LJ35_FUEL_SEL": sc.SetLVar("XMLVAR_LEAR_FUEL_SEL_Position", value); return true;
            case "LJ35_FUEL_RESET": Pulse(sc, "GENERIC_LEAR_FUEL_RESET"); return true;
            case "LJ35_JET_PUMP_L": sc.SetLVar("GENERIC_LEAR_FUEL_MAINPUMP1_1", value); return true;
            case "LJ35_JET_PUMP_R": sc.SetLVar("GENERIC_LEAR_FUEL_MAINPUMP2_1", value); return true;
            case "LJ35_STBY_PUMP_L": sc.SetLVar("GENERIC_LEAR_FUEL_SECPUMP1_1", value); return true;
            case "LJ35_STBY_PUMP_R": sc.SetLVar("GENERIC_LEAR_FUEL_SECPUMP2_1", value); return true;
            case "LJ35_XFLOW": sc.SetLVar("GENERIC_LEAR_FUEL_XFEED_1", value); return true;
            case "LJ35_JETTISON": sc.SetLVar("GENERIC_LEAR_FUEL_JTSN_1", value); return true;
            case "LJ35_XFER_FILL": sc.SetLVar("GENERIC_Momentary_LEAR_FUEL_XFER", value); return true;
        }
        return false;
    }
}
