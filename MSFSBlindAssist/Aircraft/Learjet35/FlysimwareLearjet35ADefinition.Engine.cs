using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Engine Panel → Engine Instruments, and Throttle Quadrant → Thrust and Flaps.
/// The engine values are cached for the P / E / Shift+O readouts.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string EnginePanel = "Engine Instruments";
    private const string ThrottlePanel = "Thrust and Flaps";

    private static Dictionary<string, SimVarDefinition> BuildEngineVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddTyped(v, "LJ35_N1_REM_SET", "N1 Reminder", "percent", "The three wheels, typed as one number 0 to 999.", "LJ35_N1_REM");
        AddDerived(v, "LJ35_N1_REM", "N1 Reminder Set");
        AddReadout(v, "LJ35_N1_REM_100", "GENERIC_LEAR_N1_REMINDER_100", "N1 Reminder Hundreds", "number", "F0");
        AddReadout(v, "LJ35_N1_REM_10", "GENERIC_LEAR_N1_REMINDER_10", "N1 Reminder Tens", "number", "F0");
        AddReadout(v, "LJ35_N1_REM_1", "GENERIC_LEAR_N1_REMINDER_1", "N1 Reminder Units", "number", "F0");
        AddSwitch(v, "LJ35_ENG_SYNC_TURB", "LEAR_SW_TURB_1", "Engine Sync", "Fan", "Turbine");

        AddSimReadout(v, "LJ35_N1_L", "TURB ENG N1:1", "Left N1", "percent", "F1");
        AddSimReadout(v, "LJ35_N1_R", "TURB ENG N1:2", "Right N1", "percent", "F1");
        AddSimReadout(v, "LJ35_N2_L", "TURB ENG N2:1", "Left N2", "percent", "F1");
        AddSimReadout(v, "LJ35_N2_R", "TURB ENG N2:2", "Right N2", "percent", "F1");
        AddSimReadout(v, "LJ35_ITT_L", "TURB ENG ITT:1", "Left ITT", "celsius", "F0");
        AddSimReadout(v, "LJ35_ITT_R", "TURB ENG ITT:2", "Right ITT", "celsius", "F0");
        AddSimReadout(v, "LJ35_FF_L", "ENG FUEL FLOW PPH:1", "Left Fuel Flow", "pounds per hour", "F0");
        AddSimReadout(v, "LJ35_FF_R", "ENG FUEL FLOW PPH:2", "Right Fuel Flow", "pounds per hour", "F0");
        AddSimReadout(v, "LJ35_OIL_P_L", "GENERAL ENG OIL PRESSURE:1", "Left Oil Pressure", "psi", "F0");
        AddSimReadout(v, "LJ35_OIL_P_R", "GENERAL ENG OIL PRESSURE:2", "Right Oil Pressure", "psi", "F0");
        AddSimReadout(v, "LJ35_OIL_T_L", "GENERAL ENG OIL TEMPERATURE:1", "Left Oil Temperature", "celsius", "F0");
        AddSimReadout(v, "LJ35_OIL_T_R", "GENERAL ENG OIL TEMPERATURE:2", "Right Oil Temperature", "celsius", "F0");
        AddSimReadout(v, "LJ35_BLEED_PSI_L", "TURB ENG BLEED AIR:1", "Left Bleed Air", "psi", "F0");
        AddSimReadout(v, "LJ35_BLEED_PSI_R", "TURB ENG BLEED AIR:2", "Right Bleed Air", "psi", "F0");
        AddSimReadout(v, "LJ35_TAT", "TOTAL AIR TEMPERATURE", "Total Air Temperature", "celsius", "F0");
        AddSimReadout(v, "LJ35_OAT", "AMBIENT TEMPERATURE", "Outside Air Temperature", "celsius", "F0");
        AddReadout(v, "LJ35_EMERG_AIR", "EMERG_AIR", "Emergency Air", "psi", "F0");
        AddReadout(v, "LJ35_FUEL_USED", "FUEL_USED", "Fuel Used", "pounds", "F0");
        AddSimReadout(v, "LJ35_THR_L", "GENERAL ENG THROTTLE LEVER POSITION:1", "Left Thrust Lever", "percent", "F0");
        AddSimReadout(v, "LJ35_THR_R", "GENERAL ENG THROTTLE LEVER POSITION:2", "Right Thrust Lever", "percent", "F0");
        foreach (var k in new[] { "LJ35_N1_L", "LJ35_N1_R", "LJ35_N2_L", "LJ35_N2_R", "LJ35_ITT_L", "LJ35_ITT_R",
                     "LJ35_FF_L", "LJ35_FF_R", "LJ35_OIL_P_L", "LJ35_OIL_P_R", "LJ35_OIL_T_L", "LJ35_OIL_T_R",
                     "LJ35_BLEED_PSI_L", "LJ35_BLEED_PSI_R", "LJ35_THR_L", "LJ35_THR_R", "LJ35_TAT" })
            Cache(v, k);

        // ---- throttle quadrant ----
        AddTyped(v, "LJ35_THROTTLE_SET", "Both Thrust Levers", "percent", "0 to 100. 0 is idle with the cut-off switches at Run.", "LJ35_THR_L");
        AddTyped(v, "LJ35_THROTTLE_L_SET", "Left Thrust Lever", "percent", "0 to 100.", "LJ35_THR_L");
        AddTyped(v, "LJ35_THROTTLE_R_SET", "Right Thrust Lever", "percent", "0 to 100.", "LJ35_THR_R");
        AddSwitch(v, "LJ35_CUTOFF_L", "LEAR_SHUTOFF1", "Left Fuel Cut-off", "Run", "Cut-off",
            "The lever's cut-off gate. Run with the lever at idle lights the engine during a start.");
        AddSwitch(v, "LJ35_CUTOFF_R", "LEAR_SHUTOFF2", "Right Fuel Cut-off", "Run", "Cut-off",
            "The lever's cut-off gate. Run with the lever at idle lights the engine during a start.");
        AddSimState(v, "LJ35_FLAPS", "FLAPS HANDLE INDEX", "Flaps",
            new Dictionary<double, string> { [0] = "Up", [1] = "8 degrees", [2] = "20 degrees", [3] = "40 degrees" });
        AddSimSwitch(v, "LJ35_SPOILERS", "SPOILERS HANDLE POSITION", "Spoilers", "Retracted", "Extended",
            "Ground spoilers; the SPOILER lamp lights with flaps past 13 degrees.", "percent");
        v["LJ35_SPOILERS"].ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [100] = "Extended" };
        AddSimSwitch(v, "LJ35_PARKING_BRAKE", "BRAKE PARKING POSITION", "Parking Brake", "Off", "On", "Releasing it needs hydraulic pressure.");
        AddTyped(v, "LJ35_EMER_BRAKE_SET", "Emergency Brake", "percent", "0 to 100 of lever travel; uses emergency air.", "LJ35_EMER_BRAKE");
        AddReadout(v, "LJ35_EMER_BRAKE", "GENERIC_LEAR_EMER_BRAKE", "Emergency Brake Lever", "percent", "F0");
        AddSimReadout(v, "LJ35_FLAP_L", "TRAILING EDGE FLAPS LEFT ANGLE", "Left Flap", "degrees", "F0");
        AddSimReadout(v, "LJ35_FLAP_R", "TRAILING EDGE FLAPS RIGHT ANGLE", "Right Flap", "degrees", "F0");
        AddSimReadout(v, "LJ35_SPOILER_L_DEG", "SPOILERS LEFT POSITION", "Left Spoiler", "degrees", "F0");
        AddSimReadout(v, "LJ35_SPOILER_R_DEG", "SPOILERS RIGHT POSITION", "Right Spoiler", "degrees", "F0");
        AddFlag(v, "LJ35_EMER_BRAKES_ON", "LEAR_EMER_BRAKES_ON", "Emergency Brakes", "Off", "Applied");
        AddFlag(v, "LJ35_STEER_ON", "STEER_ON", "Steer Lock", "Off", "On");
        Cache(v, "LJ35_FLAP_L"); Cache(v, "LJ35_FLAP_R"); Cache(v, "LJ35_SPOILER_L_DEG"); Cache(v, "LJ35_SPOILER_R_DEG");
        Cache(v, "LJ35_STEER_ON"); Cache(v, "LJ35_EMER_BRAKE");
        return v;
    }

    private static readonly List<string> EngineControls = new() { "LJ35_N1_REM_SET", "LJ35_ENG_SYNC_TURB" };
    private static readonly List<string> EngineDisplay = new()
    {
        "LJ35_N1_L", "LJ35_N2_L", "LJ35_ITT_L", "LJ35_FF_L", "LJ35_OIL_P_L", "LJ35_OIL_T_L", "LJ35_BLEED_PSI_L", "LJ35_THR_L",
        "LJ35_N1_R", "LJ35_N2_R", "LJ35_ITT_R", "LJ35_FF_R", "LJ35_OIL_P_R", "LJ35_OIL_T_R", "LJ35_BLEED_PSI_R", "LJ35_THR_R",
        "LJ35_N1_REM", "LJ35_TAT", "LJ35_OAT", "LJ35_EMERG_AIR", "LJ35_FUEL_USED"
    };
    private static readonly List<string> ThrottleControls = new()
    {
        "LJ35_THROTTLE_SET", "LJ35_THROTTLE_L_SET", "LJ35_THROTTLE_R_SET", "LJ35_CUTOFF_L", "LJ35_CUTOFF_R",
        "LJ35_FLAPS", "LJ35_SPOILERS", "LJ35_PARKING_BRAKE", "LJ35_EMER_BRAKE_SET"
    };
    private static readonly List<string> ThrottleDisplay = new()
    {
        "LJ35_THR_L", "LJ35_THR_R", "LJ35_FLAP_L", "LJ35_FLAP_R", "LJ35_SPOILER_L_DEG", "LJ35_SPOILER_R_DEG",
        "LJ35_EMER_BRAKE", "LJ35_EMER_BRAKES_ON", "LJ35_EMERG_AIR", "LJ35_STEER_ON"
    };

    private static uint ThrottleRaw(double percent) => (uint)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * 16383);

    private static bool HandleEngineSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_N1_REM_SET":
            {
                int n = (int)Math.Clamp(Math.Round(value), 0, 999);
                sc.SetLVar("GENERIC_LEAR_N1_REMINDER_100", n / 100);
                sc.SetLVar("GENERIC_LEAR_N1_REMINDER_10", n / 10 % 10);
                sc.SetLVar("GENERIC_LEAR_N1_REMINDER_1", n % 10);
                return true;
            }
            case "LJ35_ENG_SYNC_TURB": sc.SetLVar("GENERIC_LEAR_SW_TURB_1", value); return true;
            case "LJ35_THROTTLE_SET":
                sc.SendEvent("THROTTLE1_SET", ThrottleRaw(value));
                sc.SendEvent("THROTTLE2_SET", ThrottleRaw(value));
                return true;
            case "LJ35_THROTTLE_L_SET": sc.SendEvent("THROTTLE1_SET", ThrottleRaw(value)); return true;
            case "LJ35_THROTTLE_R_SET": sc.SendEvent("THROTTLE2_SET", ThrottleRaw(value)); return true;
            case "LJ35_CUTOFF_L": sc.SetLVar("GENERIC_LEAR_SHUTOFF1", value); return true;
            case "LJ35_CUTOFF_R": sc.SetLVar("GENERIC_LEAR_SHUTOFF2", value); return true;
            case "LJ35_FLAPS":
                sc.SendEvent(value switch { < 0.5 => "FLAPS_UP", < 1.5 => "FLAPS_1", < 2.5 => "FLAPS_2", _ => "FLAPS_DOWN" });
                return true;
            case "LJ35_SPOILERS": sc.SendEvent("SPOILERS_SET", value > 50 ? 16383u : 0u); return true;
            case "LJ35_PARKING_BRAKE": sc.SendEvent("PARKING_BRAKE_SET", value > 0.5 ? 1u : 0u); return true;
            case "LJ35_EMER_BRAKE_SET": sc.SetLVar("GENERIC_LEAR_EMER_BRAKE", Math.Clamp(Math.Round(value), 0, 100)); return true;
        }
        return false;
    }
}
