using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Center Pedestal → Trim and Steering, Yaw Damper, Collins Radios. COM 1 and NAV 1 are the
/// GNS 530's radios and are tuned on the GNS 530 panel; COM 2, NAV 2 and both ADFs are the
/// Collins heads here. Frequencies are Continuous and announced with a settle window so a
/// knob turn speaks the value it comes to rest on.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string TrimPanel = "Trim and Steering";
    private const string YawDamperPanel = "Yaw Damper";
    private const string RadiosPanel = "Collins Radios";

    private static Dictionary<string, SimVarDefinition> BuildPedestalVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- trim and steering ----
        AddThreeWay(v, "LJ35_TRIM_PRI_SEC", "LEAR_TRIM_PRI_SEC", "Pitch Trim System", "Primary", "Off", "Secondary");
        AddTyped(v, "LJ35_ELEV_TRIM_SET", "Elevator Trim", "percent", "Minus 100 nose down to 100 nose up.", "LJ35_TRIM_PCT");
        AddButton(v, "LJ35_TRIM_UP", "ELEV_TRIM_UP", "Trim Nose Up");
        AddButton(v, "LJ35_TRIM_DOWN", "ELEV_TRIM_DN", "Trim Nose Down");
        AddButton(v, "LJ35_RUDDER_TRIM_L", "RUDDER_TRIM_LEFT", "Rudder Trim Left");
        AddButton(v, "LJ35_RUDDER_TRIM_R", "RUDDER_TRIM_RIGHT", "Rudder Trim Right");
        AddButton(v, "LJ35_AIL_TRIM_L", "AILERON_TRIM_LEFT", "Aileron Trim Left");
        AddButton(v, "LJ35_AIL_TRIM_R", "AILERON_TRIM_RIGHT", "Aileron Trim Right");
        AddButton(v, "LJ35_STEER_LOCK", "GENERIC_LEAR_STEER_LOCK_1", "Steer Lock",
            "Full nose-wheel steering below 45 knots; the yoke MSW releases it.");
        AddSimReadout(v, "LJ35_TRIM_PCT", "ELEVATOR TRIM PCT", "Elevator Trim", "percent", "F0");
        AddSimReadout(v, "LJ35_TRIM_DEG", "ELEVATOR TRIM POSITION", "Elevator Trim Angle", "degrees", "F1");
        AddDerived(v, "LJ35_TRIM_TO_BAND", "Takeoff Trim Band");
        AddSimReadout(v, "LJ35_AIL_TRIM_PCT", "AILERON TRIM PCT", "Aileron Trim", "percent", "F0");
        AddSimReadout(v, "LJ35_RUD_TRIM_PCT", "RUDDER TRIM PCT", "Rudder Trim", "percent", "F0");
        AddFlag(v, "LJ35_TRIM_CLACKER", "TRIM_CLACKER", "Trim Clacker", "Quiet", "Sounding");
        Cache(v, "LJ35_TRIM_PCT"); Cache(v, "LJ35_TRIM_DEG"); Cache(v, "LJ35_AIL_TRIM_PCT"); Cache(v, "LJ35_RUD_TRIM_PCT");

        // ---- yaw damper ----
        AddSwitch(v, "LJ35_YD_PRI_PWR", "LEAR_YAW_PRI_PWR", "Primary Yaw Damper Power");
        AddSwitch(v, "LJ35_YD_PRI_ENG", "LEAR_YAW_PRI_ENG", "Primary Yaw Damper Engage", "Off", "Engaged", "Engaging one disengages the other.");
        AddSwitch(v, "LJ35_YD_SEC_PWR", "LEAR_YAW_SEC_PWR", "Secondary Yaw Damper Power");
        AddSwitch(v, "LJ35_YD_SEC_ENG", "LEAR_YAW_SEC_ENG", "Secondary Yaw Damper Engage", "Off", "Engaged", "Engaging one disengages the other.");
        AddButton(v, "LJ35_YD_TEST", "GENERIC_LEAR_YAW_TEST", "Yaw Damper Test");
        AddFlag(v, "LJ35_YD_PRI_ENGAGED", "YAW_PRI_ENG_ENGAGED", "Primary Yaw Damper", "Off", "Engaged");
        AddFlag(v, "LJ35_YD_SEC_ENGAGED", "YAW_SEC_ENG_ENGAGED", "Secondary Yaw Damper", "Off", "Engaged");
        AddFlag(v, "LJ35_YD_STOCK", "AUTOPILOT YAW DAMPER", "Yaw Damper", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_YD_CIRCUIT_PRI", "CIRCUIT ON:98", "Primary Yaw Damper Circuit", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_YD_CIRCUIT_SEC", "CIRCUIT ON:116", "Secondary Yaw Damper Circuit", "Off", "On", simvar: true);

        // ---- Collins radios ----
        AddRotary(v, "LJ35_COM2_POWER", "COLLINS_COM_POWER2", "COM 2 Power", new[] { "Off", "On", "Hold" });
        AddRotary(v, "LJ35_NAV2_POWER", "COLLINS_NAV_POWER2", "NAV 2 Power", new[] { "Off", "On", "Hold" });
        AddRotary(v, "LJ35_ADF1_POWER", "COLLINS_ADF_POWER1", "ADF 1 Mode", new[] { "Antenna", "ADF", "Tone" });
        AddRotary(v, "LJ35_ADF2_POWER", "COLLINS_ADF_POWER2", "ADF 2 Mode", new[] { "Antenna", "ADF", "Tone" });
        AddTyped(v, "LJ35_COM2_STBY_SET", "COM 2 Standby", "MHz", "Type 118.000 to 136.990.", "LJ35_COM2_STBY");
        AddTyped(v, "LJ35_NAV2_STBY_SET", "NAV 2 Standby", "MHz", "Type 108.00 to 117.95.", "LJ35_NAV2_STBY");
        AddTyped(v, "LJ35_ADF1_SET", "ADF 1 Frequency", "kHz", "Type 190 to 1750.", "LJ35_ADF1_ACT");
        AddTyped(v, "LJ35_ADF2_SET", "ADF 2 Frequency", "kHz", "Type 190 to 1750.", "LJ35_ADF2_ACT");
        AddButton(v, "LJ35_COM2_SWAP", "COM2_RADIO_SWAP", "COM 2 Swap");
        AddButton(v, "LJ35_NAV2_SWAP", "NAV2_RADIO_SWAP", "NAV 2 Swap");
        AddButton(v, "LJ35_COM2_TEST", "GENERIC_COM2_TEST_1", "COM 2 Test");
        AddButton(v, "LJ35_NAV2_TEST", "GENERIC_NAV2_TEST_1", "NAV 2 Test");
        AddButton(v, "LJ35_ADF1_TEST", "GENERIC_ADF1_TEST_1", "ADF 1 Test");
        AddButton(v, "LJ35_ADF2_TEST", "GENERIC_ADF2_TEST_1", "ADF 2 Test");
        v["LJ35_COM2_ACT"] = Freq("COM ACTIVE FREQUENCY:2", "COM 2 Active", "MHz", "F3");
        v["LJ35_COM2_STBY"] = Freq("COM STANDBY FREQUENCY:2", "COM 2 Standby", "MHz", "F3");
        v["LJ35_NAV2_ACT"] = Freq("NAV ACTIVE FREQUENCY:2", "NAV 2 Active", "MHz", "F2");
        v["LJ35_NAV2_STBY"] = Freq("NAV STANDBY FREQUENCY:2", "NAV 2 Standby", "MHz", "F2");
        v["LJ35_ADF1_ACT"] = Freq("ADF ACTIVE FREQUENCY:1", "ADF 1", "kHz", "F1");
        v["LJ35_ADF2_ACT"] = Freq("ADF ACTIVE FREQUENCY:2", "ADF 2", "kHz", "F1");
        AddSimReadout(v, "LJ35_NAV2_IDENT", "NAV IDENT:2", "NAV 2 Ident", "string", "F0");
        v.Remove("LJ35_NAV2_IDENT"); // string SimVars are not batch-readable; the ident is on the GNS window.
        return v;
    }

    /// <summary>A frequency the pilot hears when it settles (FAST-SAMPLED, silenced in the settle announcer).</summary>
    private static SimVarDefinition Freq(string simvar, string display, string units, string format) => new()
    {
        Name = simvar,
        DisplayName = display,
        Type = SimVarType.SimVar,
        Units = units,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        RenderAsReadOnlyStatus = true,
        Format = format
    };

    private static readonly List<string> TrimControls = new()
    {
        "LJ35_TRIM_PRI_SEC", "LJ35_ELEV_TRIM_SET", "LJ35_TRIM_UP", "LJ35_TRIM_DOWN", "LJ35_RUDDER_TRIM_L", "LJ35_RUDDER_TRIM_R",
        "LJ35_AIL_TRIM_L", "LJ35_AIL_TRIM_R", "LJ35_STEER_LOCK"
    };
    private static readonly List<string> TrimDisplay = new()
    {
        "LJ35_TRIM_PCT", "LJ35_TRIM_DEG", "LJ35_TRIM_TO_BAND", "LJ35_AIL_TRIM_PCT", "LJ35_RUD_TRIM_PCT", "LJ35_STEER_ON", "LJ35_TRIM_CLACKER"
    };
    private static readonly List<string> YawDamperControls = new()
    {
        "LJ35_YD_PRI_PWR", "LJ35_YD_PRI_ENG", "LJ35_YD_SEC_PWR", "LJ35_YD_SEC_ENG", "LJ35_YD_TEST"
    };
    private static readonly List<string> YawDamperDisplay = new()
    {
        "LJ35_YD_PRI_ENGAGED", "LJ35_YD_SEC_ENGAGED", "LJ35_YD_STOCK", "LJ35_YD_CIRCUIT_PRI", "LJ35_YD_CIRCUIT_SEC", "LJ35_YAW_DISCONNECT"
    };
    private static readonly List<string> RadiosControls = new()
    {
        "LJ35_COM2_POWER", "LJ35_COM2_STBY_SET", "LJ35_COM2_SWAP", "LJ35_COM2_TEST",
        "LJ35_NAV2_POWER", "LJ35_NAV2_STBY_SET", "LJ35_NAV2_SWAP", "LJ35_NAV2_TEST",
        "LJ35_ADF1_POWER", "LJ35_ADF1_SET", "LJ35_ADF1_TEST", "LJ35_ADF2_POWER", "LJ35_ADF2_SET", "LJ35_ADF2_TEST"
    };
    private static readonly List<string> RadiosDisplay = new()
    {
        "LJ35_COM2_ACT", "LJ35_COM2_STBY", "LJ35_NAV2_ACT", "LJ35_NAV2_STBY", "LJ35_ADF1_ACT", "LJ35_ADF2_ACT"
    };

    private static bool HandlePedestalSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_TRIM_PRI_SEC": sc.SetLVar("GENERIC_Momentary_LEAR_TRIM_PRI_SEC", value); return true;
            case "LJ35_ELEV_TRIM_SET":
                sc.ExecuteCalculatorCode($"{Rpn(Math.Clamp(value, -100, 100) / 100.0 * 16383)} (>K:ELEVATOR_TRIM_SET)");
                return true;
            case "LJ35_TRIM_UP": sc.SendEvent("ELEV_TRIM_UP"); return true;
            case "LJ35_TRIM_DOWN": sc.SendEvent("ELEV_TRIM_DN"); return true;
            case "LJ35_RUDDER_TRIM_L": sc.SendEvent("RUDDER_TRIM_LEFT"); return true;
            case "LJ35_RUDDER_TRIM_R": sc.SendEvent("RUDDER_TRIM_RIGHT"); return true;
            case "LJ35_AIL_TRIM_L": sc.SendEvent("AILERON_TRIM_LEFT"); return true;
            case "LJ35_AIL_TRIM_R": sc.SendEvent("AILERON_TRIM_RIGHT"); return true;
            case "LJ35_STEER_LOCK": Pulse(sc, "GENERIC_LEAR_STEER_LOCK_1"); return true;

            case "LJ35_YD_PRI_PWR": sc.SetLVar("GENERIC_LEAR_YAW_PRI_PWR", value); return true;
            case "LJ35_YD_PRI_ENG": sc.SetLVar("GENERIC_LEAR_YAW_PRI_ENG", value); return true;
            case "LJ35_YD_SEC_PWR": sc.SetLVar("GENERIC_LEAR_YAW_SEC_PWR", value); return true;
            case "LJ35_YD_SEC_ENG": sc.SetLVar("GENERIC_LEAR_YAW_SEC_ENG", value); return true;
            case "LJ35_YD_TEST": Pulse(sc, "GENERIC_LEAR_YAW_TEST", 1500); return true;

            case "LJ35_COM2_POWER": sc.SetLVar("XMLVAR_COLLINS_COM_POWER2_Position", value); return true;
            case "LJ35_NAV2_POWER": sc.SetLVar("XMLVAR_COLLINS_NAV_POWER2_Position", value); return true;
            case "LJ35_ADF1_POWER": sc.SetLVar("XMLVAR_COLLINS_ADF_POWER1_Position", value); return true;
            case "LJ35_ADF2_POWER": sc.SetLVar("XMLVAR_COLLINS_ADF_POWER2_Position", value); return true;
            case "LJ35_COM2_STBY_SET": sc.SendEvent("COM2_STBY_RADIO_SET_HZ", FrequencyHz(value)); return true;
            case "LJ35_NAV2_STBY_SET": sc.SendEvent("NAV2_STBY_SET_HZ", FrequencyHz(value)); return true;
            case "LJ35_ADF1_SET": sc.SendEvent("ADF_ACTIVE_SET", (uint)Math.Round(value * 1000)); return true;
            case "LJ35_ADF2_SET": sc.SendEvent("ADF2_ACTIVE_SET", (uint)Math.Round(value * 1000)); return true;
            case "LJ35_COM2_SWAP": sc.SendEvent("COM2_RADIO_SWAP"); return true;
            case "LJ35_NAV2_SWAP": sc.SendEvent("NAV2_RADIO_SWAP"); return true;
            case "LJ35_COM2_TEST": Pulse(sc, "GENERIC_COM2_TEST_1", 1500); return true;
            case "LJ35_NAV2_TEST": Pulse(sc, "GENERIC_NAV2_TEST_1", 1500); return true;
            case "LJ35_ADF1_TEST": Pulse(sc, "GENERIC_ADF1_TEST_1", 1500); return true;
            case "LJ35_ADF2_TEST": Pulse(sc, "GENERIC_ADF2_TEST_1", 1500); return true;
        }
        return false;
    }
}
