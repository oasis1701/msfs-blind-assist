using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Start Panel → Engine Start. The manual's start procedure (page 63): fuel computers on,
/// thrust levers at idle cut-off, starter switch to START, at 10 % N1 lever to idle; with the
/// fuel computer ON the starter drops out itself at 45 % N2, OFF the pilot moves it.
/// The generator lives on the SAME switch as the starter (GEN / OFF / START), which is why
/// there is no separate generator control anywhere.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string StartPanel = "Engine Start";

    private static Dictionary<string, SimVarDefinition> BuildStartVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        AddSwitch(v, "LJ35_BATT1", "LEAR_BATT1", "Battery 1");
        AddSwitch(v, "LJ35_BATT2", "LEAR_BATT2", "Battery 2");
        AddThreeWay(v, "LJ35_EMER_BATT", "LEAR_SW_EMER_BATT", "Emergency Battery", "Emergency", "Standby", "Off",
            "Standby with both batteries off lights EMER PWR for the test; Off after the flight or it drains.");
        AddLVarState(v, "LJ35_GPU", "EFB_PUSH_GPU", "Ground Power Unit",
            new Dictionary<double, string> { [0] = "Disconnected", [1] = "Connected" },
            "Drops itself when a generator comes on line, the parking brake is released, or the door opens.");
        AddSwitch(v, "LJ35_INV_PRI", "LEAR_INV_PRI_1", "Primary Inverter");
        AddSwitch(v, "LJ35_INV_SEC", "LEAR_INV_SEC_1", "Secondary Inverter");
        AddSwitch(v, "LJ35_TIE_AC", "BREAKER_AC_TIE", "AC Tie Breaker", "Open", "Closed");
        AddSwitch(v, "LJ35_TIE_MAIN", "BREAKER_MAIN_TIE", "Main Tie Breaker", "Open", "Closed");
        AddSwitch(v, "LJ35_TIE_ESS", "BREAKER_ESS_TIE", "Essential Tie Breaker", "Open", "Closed");
        AddThreeWay(v, "LJ35_STARTER_L", "LEAR_STARTER_L", "Left Starter Generator", "Generator", "Off", "Start",
            "Start cranks the engine; bring the left thrust lever out of cut-off for light-off. Drops to Off itself at 45 percent N2 with the fuel computer on.");
        AddThreeWay(v, "LJ35_STARTER_R", "LEAR_STARTER_R", "Right Starter Generator", "Generator", "Off", "Start",
            "Start cranks the engine; bring the right thrust lever out of cut-off for light-off. Drops to Off itself at 45 percent N2 with the fuel computer on.");
        AddSwitch(v, "LJ35_AIRSTART_L", "LEAR_AIRSTART_L_1", "Left Air Start Ignition");
        AddSwitch(v, "LJ35_AIRSTART_R", "LEAR_AIRSTART_R_1", "Right Air Start Ignition");

        AddFlag(v, "LJ35_SHUTOFF_L_STATE", "GENERIC_LEAR_SHUTOFF1", "Left Thrust Lever", "Idle or above", "Cut-off");
        AddFlag(v, "LJ35_SHUTOFF_R_STATE", "GENERIC_LEAR_SHUTOFF2", "Right Thrust Lever", "Idle or above", "Cut-off");
        AddSimReadout(v, "LJ35_BUS_MAIN_V", "ELECTRICAL MAIN BUS VOLTAGE", "Main Bus Volts", "volts", "F1");
        AddSimReadout(v, "LJ35_BUS_BATT_V", "ELECTRICAL BATTERY BUS VOLTAGE", "Battery Bus Volts", "volts", "F1");
        AddSimReadout(v, "LJ35_BATT1_V", "ELECTRICAL BATTERY VOLTAGE:1", "Battery 1 Volts", "volts", "F1");
        AddSimReadout(v, "LJ35_BATT2_V", "ELECTRICAL BATTERY VOLTAGE:2", "Battery 2 Volts", "volts", "F1");
        AddSimReadout(v, "LJ35_BATT3_V", "ELECTRICAL BATTERY VOLTAGE:3", "Emergency Battery Volts", "volts", "F1");
        AddSimReadout(v, "LJ35_GEN_L_V", "ELECTRICAL GENALT BUS VOLTAGE:1", "Left Generator Volts", "volts", "F1");
        AddSimReadout(v, "LJ35_GEN_R_V", "ELECTRICAL GENALT BUS VOLTAGE:2", "Right Generator Volts", "volts", "F1");
        AddFlag(v, "LJ35_GEN_L_ON", "GENERAL ENG MASTER ALTERNATOR:1", "Left Generator", "Off line", "On line", simvar: true);
        AddFlag(v, "LJ35_GEN_R_ON", "GENERAL ENG MASTER ALTERNATOR:2", "Right Generator", "Off line", "On line", simvar: true);
        AddFlag(v, "LJ35_EXT_PWR_ON", "EXTERNAL POWER ON:1", "External Power", "Off", "Supplying", simvar: true);
        AddFlag(v, "LJ35_STARTER_L_ON", "GENERAL ENG STARTER:1", "Left Starter", "Off", "Cranking", simvar: true);
        AddFlag(v, "LJ35_STARTER_R_ON", "GENERAL ENG STARTER:2", "Right Starter", "Off", "Cranking", simvar: true);
        AddFlag(v, "LJ35_IGN_L_ON", "TURB ENG IGNITION SWITCH:1", "Left Ignition", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_IGN_R_ON", "TURB ENG IGNITION SWITCH:2", "Right Ignition", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_COMB_L", "GENERAL ENG COMBUSTION:1", "Left Engine", "Not running", "Running", simvar: true);
        AddFlag(v, "LJ35_COMB_R", "GENERAL ENG COMBUSTION:2", "Right Engine", "Not running", "Running", simvar: true);
        AddSimReadout(v, "LJ35_START_N2_L", "TURB ENG N2:1", "Left N2", "percent", "F1");
        AddSimReadout(v, "LJ35_START_N2_R", "TURB ENG N2:2", "Right N2", "percent", "F1");
        AddSimReadout(v, "LJ35_START_ITT_L", "TURB ENG ITT:1", "Left ITT", "celsius", "F0");
        AddSimReadout(v, "LJ35_START_ITT_R", "TURB ENG ITT:2", "Right ITT", "celsius", "F0");

        // The lamps and the hotkeys read these from the cache.
        Cache(v, "LJ35_GEN_L_V"); Cache(v, "LJ35_GEN_R_V");
        Cache(v, "LJ35_GEN_L_ON"); Cache(v, "LJ35_GEN_R_ON");
        Cache(v, "LJ35_BUS_MAIN_V");
        Cache(v, "LJ35_COMB_L"); Cache(v, "LJ35_COMB_R");
        return v;
    }

    private static readonly List<string> StartControls = new()
    {
        "LJ35_BATT1", "LJ35_BATT2", "LJ35_EMER_BATT", "LJ35_GPU", "LJ35_INV_PRI", "LJ35_INV_SEC",
        "LJ35_TIE_AC", "LJ35_TIE_MAIN", "LJ35_TIE_ESS", "LJ35_STARTER_L", "LJ35_STARTER_R",
        "LJ35_AIRSTART_L", "LJ35_AIRSTART_R"
    };

    private static readonly List<string> StartDisplay = new()
    {
        "LJ35_BUS_MAIN_V", "LJ35_BUS_BATT_V", "LJ35_BATT1_V", "LJ35_BATT2_V", "LJ35_BATT3_V",
        "LJ35_EXT_PWR_ON", "LJ35_GEN_L_ON", "LJ35_GEN_L_V", "LJ35_GEN_R_ON", "LJ35_GEN_R_V",
        "LJ35_SHUTOFF_L_STATE", "LJ35_SHUTOFF_R_STATE",
        "LJ35_COMB_L", "LJ35_STARTER_L_ON", "LJ35_IGN_L_ON", "LJ35_START_N2_L", "LJ35_START_ITT_L",
        "LJ35_COMB_R", "LJ35_STARTER_R_ON", "LJ35_IGN_R_ON", "LJ35_START_N2_R", "LJ35_START_ITT_R"
    };

    private static bool HandleStartSet(string varKey, double value, SimConnectManager simConnect)
    {
        switch (varKey)
        {
            case "LJ35_BATT1": simConnect.SetLVar("GENERIC_LEAR_BATT1", value); return true;
            case "LJ35_BATT2": simConnect.SetLVar("GENERIC_LEAR_BATT2", value); return true;
            case "LJ35_EMER_BATT": simConnect.SetLVar("GENERIC_Momentary_LEAR_SW_EMER_BATT", value); return true;
            case "LJ35_GPU": simConnect.SetLVar("EFB_PUSH_GPU", value); return true;
            case "LJ35_INV_PRI": simConnect.SetLVar("GENERIC_LEAR_INV_PRI_1", value); return true;
            case "LJ35_INV_SEC": simConnect.SetLVar("GENERIC_LEAR_INV_SEC_1", value); return true;
            case "LJ35_TIE_AC": simConnect.SetLVar("GENERIC_BREAKER_AC_TIE", value); return true;
            case "LJ35_TIE_MAIN": simConnect.SetLVar("GENERIC_BREAKER_MAIN_TIE", value); return true;
            case "LJ35_TIE_ESS": simConnect.SetLVar("GENERIC_BREAKER_ESS_TIE", value); return true;
            case "LJ35_STARTER_L": simConnect.SetLVar("GENERIC_Momentary_LEAR_STARTER_L", value); return true;
            case "LJ35_STARTER_R": simConnect.SetLVar("GENERIC_Momentary_LEAR_STARTER_R", value); return true;
            case "LJ35_AIRSTART_L": simConnect.SetLVar("GENERIC_LEAR_AIRSTART_L_1", value); return true;
            case "LJ35_AIRSTART_R": simConnect.SetLVar("GENERIC_LEAR_AIRSTART_R_1", value); return true;
        }
        return false;
    }
}
