using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Glareshield → Reverser Panel and Fire Protection.
///
/// The reverser switch's TEST position is a spring-back (vendor type ON_OFF_RESET), so the
/// combo re-reads after a Test. The fire handle is a finite lever 0-100 that the vendor
/// treats as pulled at 90; it is exposed as Stowed / Pulled, and the bottle switches only do
/// anything while the matching handle is pulled (Systems.xml fires EXTINGUISH_ENGINE_FIRE).
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string ReverserPanel = "Reverser Panel";
    private const string FirePanel = "Fire Protection";

    private static Dictionary<string, SimVarDefinition> BuildGlareshieldVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddThreeWay(v, "LJ35_REV_L", "LEAR_SW_REV_L", "Left Reverser", "Armed", "Off", "Test",
            "Test springs back to Off.");
        AddThreeWay(v, "LJ35_REV_R", "LEAR_SW_REV_R", "Right Reverser", "Armed", "Off", "Test",
            "Test springs back to Off.");
        AddSimReadout(v, "LJ35_REV_NOZZLE_L", "TURB ENG REVERSE NOZZLE PERCENT:1", "Left Reverser Deployed", "percent", "F0");
        AddSimReadout(v, "LJ35_REV_NOZZLE_R", "TURB ENG REVERSE NOZZLE PERCENT:2", "Right Reverser Deployed", "percent", "F0");
        AddSimReadout(v, "LJ35_HYD_PSI", "HYDRAULIC PRESSURE:1", "Hydraulic Pressure", "psi", "F0");
        AddDerived(v, "LJ35_ANN_REV_ARM_L", "Left ARM Lamp");
        AddDerived(v, "LJ35_ANN_REV_ARM_R", "Right ARM Lamp");
        AddDerived(v, "LJ35_ANN_DEPLOY_L", "Left DEPLOY Lamp");
        AddDerived(v, "LJ35_ANN_DEPLOY_R", "Right DEPLOY Lamp");
        AddDerived(v, "LJ35_ANN_UNLOCK_L", "Left UNLOCK Lamp");
        AddDerived(v, "LJ35_ANN_UNLOCK_R", "Right UNLOCK Lamp");
        Cache(v, "LJ35_REV_NOZZLE_L"); Cache(v, "LJ35_REV_NOZZLE_R"); Cache(v, "LJ35_HYD_PSI");

        AddLVarState(v, "LJ35_FIRE_HANDLE_L", "GENERIC_LEAR_FIRE_HANDLE1", "Left Fire Handle",
            new Dictionary<double, string> { [0] = "Stowed", [100] = "Pulled" },
            "Pull to arm both bottles for the left engine; the bottle switches then discharge.");
        AddLVarState(v, "LJ35_FIRE_HANDLE_R", "GENERIC_LEAR_FIRE_HANDLE2", "Right Fire Handle",
            new Dictionary<double, string> { [0] = "Stowed", [100] = "Pulled" },
            "Pull to arm both bottles for the right engine; the bottle switches then discharge.");
        AddButton(v, "LJ35_BOTTLE1_ENG1", "GENERIC_LEAR_BOTTLE1_ENG1", "Bottle 1 to Left Engine", "Needs the left fire handle pulled.");
        AddButton(v, "LJ35_BOTTLE2_ENG1", "GENERIC_LEAR_BOTTLE2_ENG1", "Bottle 2 to Left Engine", "Needs the left fire handle pulled.");
        AddButton(v, "LJ35_BOTTLE1_ENG2", "GENERIC_LEAR_BOTTLE1_ENG2", "Bottle 1 to Right Engine", "Needs the right fire handle pulled.");
        AddButton(v, "LJ35_BOTTLE2_ENG2", "GENERIC_LEAR_BOTTLE2_ENG2", "Bottle 2 to Right Engine", "Needs the right fire handle pulled.");
        AddButton(v, "LJ35_MASTER_RESET_PILOT", "GENERIC_LEAR_SW_MASTER_RESET1_1", "Master Warning Reset (Pilot)");
        AddButton(v, "LJ35_MASTER_RESET_COPILOT", "GENERIC_LEAR_SW_MASTER_RESET2_1", "Master Warning Reset (Copilot)");
        AddFlag(v, "LJ35_FIRE_L", "ENG ON FIRE:1", "Left Engine Fire", "No fire", "FIRE", simvar: true);
        AddFlag(v, "LJ35_FIRE_R", "ENG ON FIRE:2", "Right Engine Fire", "No fire", "FIRE", simvar: true);
        AddFlag(v, "LJ35_BOTTLE1_ENG1_USED", "GENERIC_LEAR_BOTTLE1_ENG1", "Bottle 1 Left", "Available", "Discharged");
        AddFlag(v, "LJ35_BOTTLE2_ENG1_USED", "GENERIC_LEAR_BOTTLE2_ENG1", "Bottle 2 Left", "Available", "Discharged");
        AddFlag(v, "LJ35_BOTTLE1_ENG2_USED", "GENERIC_LEAR_BOTTLE1_ENG2", "Bottle 1 Right", "Available", "Discharged");
        AddFlag(v, "LJ35_BOTTLE2_ENG2_USED", "GENERIC_LEAR_BOTTLE2_ENG2", "Bottle 2 Right", "Available", "Discharged");
        v["LJ35_FIRE_L"].UpdateFrequency = UpdateFrequency.Continuous; v["LJ35_FIRE_L"].IsAnnounced = true;
        v["LJ35_FIRE_R"].UpdateFrequency = UpdateFrequency.Continuous; v["LJ35_FIRE_R"].IsAnnounced = true;
        return v;
    }

    private static readonly List<string> ReverserControls = new() { "LJ35_REV_L", "LJ35_REV_R" };
    private static readonly List<string> ReverserDisplay = new()
    {
        "LJ35_ANN_REV_ARM_L", "LJ35_ANN_DEPLOY_L", "LJ35_ANN_UNLOCK_L", "LJ35_REV_NOZZLE_L",
        "LJ35_ANN_REV_ARM_R", "LJ35_ANN_DEPLOY_R", "LJ35_ANN_UNLOCK_R", "LJ35_REV_NOZZLE_R", "LJ35_HYD_PSI"
    };
    private static readonly List<string> FireControls = new()
    {
        "LJ35_FIRE_HANDLE_L", "LJ35_BOTTLE1_ENG1", "LJ35_BOTTLE2_ENG1",
        "LJ35_FIRE_HANDLE_R", "LJ35_BOTTLE1_ENG2", "LJ35_BOTTLE2_ENG2",
        "LJ35_MASTER_RESET_PILOT", "LJ35_MASTER_RESET_COPILOT"
    };
    private static readonly List<string> FireDisplay = new()
    {
        "LJ35_FIRE_L", "LJ35_BOTTLE1_ENG1_USED", "LJ35_BOTTLE2_ENG1_USED",
        "LJ35_FIRE_R", "LJ35_BOTTLE1_ENG2_USED", "LJ35_BOTTLE2_ENG2_USED"
    };

    private static bool HandleGlareshieldSet(string varKey, double value, SimConnectManager simConnect)
    {
        switch (varKey)
        {
            case "LJ35_REV_L": simConnect.SetLVar("GENERIC_Momentary_LEAR_SW_REV_L", value); return true;
            case "LJ35_REV_R": simConnect.SetLVar("GENERIC_Momentary_LEAR_SW_REV_R", value); return true;
            case "LJ35_FIRE_HANDLE_L": simConnect.SetLVar("GENERIC_LEAR_FIRE_HANDLE1", value >= 50 ? 100 : 0); return true;
            case "LJ35_FIRE_HANDLE_R": simConnect.SetLVar("GENERIC_LEAR_FIRE_HANDLE2", value >= 50 ? 100 : 0); return true;
            case "LJ35_BOTTLE1_ENG1": simConnect.SetLVar("GENERIC_LEAR_BOTTLE1_ENG1", 1); return true;
            case "LJ35_BOTTLE2_ENG1": simConnect.SetLVar("GENERIC_LEAR_BOTTLE2_ENG1", 1); return true;
            case "LJ35_BOTTLE1_ENG2": simConnect.SetLVar("GENERIC_LEAR_BOTTLE1_ENG2", 1); return true;
            case "LJ35_BOTTLE2_ENG2": simConnect.SetLVar("GENERIC_LEAR_BOTTLE2_ENG2", 1); return true;
            case "LJ35_MASTER_RESET_PILOT": Pulse(simConnect, "GENERIC_LEAR_SW_MASTER_RESET1_1"); return true;
            case "LJ35_MASTER_RESET_COPILOT": Pulse(simConnect, "GENERIC_LEAR_SW_MASTER_RESET2_1"); return true;
        }
        return false;
    }
}
