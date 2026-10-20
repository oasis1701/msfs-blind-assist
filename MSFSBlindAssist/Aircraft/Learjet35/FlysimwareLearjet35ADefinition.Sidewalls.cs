using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Pilot's and Copilot's Sidewalls → the lighting knobs, and Audio → Pilot Audio Panel.
/// Lighting_Custom.xml maps each knob to a stock potentiometer, so the knob percentage is
/// the light level. The copilot audio panel is marked NOT SIMULATED by the vendor.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string PilotLightingPanel = "Pilot Lighting";
    private const string CopilotLightingPanel = "Copilot Lighting";
    private const string AudioPanel = "Pilot Audio Panel";

    private static Dictionary<string, SimVarDefinition> BuildSidewallVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddDimmer(v, "LJ35_LT_EL_L", "XMLVAR_LEAR_LTS_EL_L_Position", "Pilot Electroluminescent Panels");
        AddDimmer(v, "LJ35_LT_PNL_L", "XMLVAR_LEAR_LTS_PNL_L_Position", "Pilot Instrument Lights");
        AddDimmer(v, "LJ35_LT_FLOOD", "XMLVAR_LEAR_LTS_FLOOD_L_Position", "Glareshield Flood");
        AddDimmer(v, "LJ35_LT_HSI", "XMLVAR_LEAR_LTS_HSI_Position", "HSI LCD Lights");
        AddDimmer(v, "LJ35_LT_MAP_L", "XMLVAR_LEAR_LTS_MAP_L_Position", "Pilot Map Light");
        AddDimmer(v, "LJ35_LT_PEDESTAL", "XMLVAR_LEAR_LTS_PEDESTAL_Position", "Pedestal Lights");
        AddDimmer(v, "LJ35_LT_EL_R", "XMLVAR_LEAR_LTS_EL_R_Position", "Copilot Electroluminescent Panels");
        AddDimmer(v, "LJ35_LT_PNL_R", "XMLVAR_LEAR_LTS_PNL_R_Position", "Copilot Instrument Lights");
        AddDimmer(v, "LJ35_LT_MAP_R", "XMLVAR_LEAR_LTS_MAP_R_Position", "Copilot Map Light");
        AddSwitch(v, "LJ35_WING_INSPECTION", "WING_INSPECTION_SWITCH", "Wing Inspection Light");
        AddFlag(v, "LJ35_LIGHT_WING", "LIGHT WING", "Wing Light", "Off", "On", simvar: true);

        // Audio
        AddSimSwitch(v, "LJ35_AUD_COM1", "COM RECEIVE:1", "COM 1 Receive");
        AddSimSwitch(v, "LJ35_AUD_COM2", "COM RECEIVE:2", "COM 2 Receive");
        AddSimSwitch(v, "LJ35_AUD_NAV1", "NAV SOUND:1", "NAV 1 Ident");
        AddSimSwitch(v, "LJ35_AUD_NAV2", "NAV SOUND:2", "NAV 2 Ident");
        AddSimSwitch(v, "LJ35_AUD_ADF1", "ADF SOUND:1", "ADF 1 Audio");
        AddSimSwitch(v, "LJ35_AUD_ADF2", "ADF SOUND:2", "ADF 2 Audio");
        AddSimSwitch(v, "LJ35_AUD_DME", "DME SOUND:1", "DME Audio");
        AddSimSwitch(v, "LJ35_AUD_MKR", "MARKER SOUND", "Marker Audio");
        AddRotary(v, "LJ35_AUD_TRANSMIT", "LEAR_SW_AUDIO_TRANS1", "Transmit Selector",
            new[] { "VHF 1", "VHF 2", "HF (inoperative)", "Interphone", "Passenger speaker" });
        AddThreeWay(v, "LJ35_AUD_PHONE", "LEAR_SW_AUDIO_PHONE1", "Receive Audio", "Passenger phone", "Phone", "Emergency");
        AddThreeWay(v, "LJ35_AUD_PASS_SPKR", "LEAR_SW_AUDIO_PASS_SPKR1", "Passenger Speaker", "Speaker", "Volume adjust", "Off");
        AddDimmer(v, "LJ35_AUD_MASTER_VOL", "XMLVAR_LEAR_SW_AUDIO_MSTR_VOL1_Position", "Master Speaker Volume");
        AddDimmer(v, "LJ35_AUD_PASS_VOL", "XMLVAR_LEAR_SW_AUDIO_SPKR_VOL1_Position", "Passenger Speaker Volume");
        AddLVarState(v, "LJ35_HEADSET", "HEADSET_FILTER", "Headset",
            new Dictionary<double, string> { [0] = "Off", [1] = "On" }, "Applies the vendor's headset sound filter.");
        AddFlag(v, "LJ35_COM1_TX", "COM TRANSMIT:1", "COM 1 Transmit", "No", "Selected", simvar: true);
        AddFlag(v, "LJ35_COM2_TX", "COM TRANSMIT:2", "COM 2 Transmit", "No", "Selected", simvar: true);
        return v;
    }

    private static readonly List<string> PilotLightingControls = new()
    {
        "LJ35_LT_EL_L", "LJ35_LT_PNL_L", "LJ35_LT_FLOOD", "LJ35_LT_HSI", "LJ35_LT_MAP_L", "LJ35_LT_PEDESTAL"
    };
    private static readonly List<string> CopilotLightingControls = new()
    {
        "LJ35_LT_EL_R", "LJ35_LT_PNL_R", "LJ35_LT_MAP_R", "LJ35_WING_INSPECTION"
    };
    private static readonly List<string> CopilotLightingDisplay = new() { "LJ35_LIGHT_WING" };
    private static readonly List<string> AudioControls = new()
    {
        "LJ35_AUD_TRANSMIT", "LJ35_AUD_COM1", "LJ35_AUD_COM2", "LJ35_AUD_NAV1", "LJ35_AUD_NAV2", "LJ35_AUD_ADF1",
        "LJ35_AUD_ADF2", "LJ35_AUD_DME", "LJ35_AUD_MKR", "LJ35_AUD_PHONE", "LJ35_AUD_PASS_SPKR",
        "LJ35_AUD_MASTER_VOL", "LJ35_AUD_PASS_VOL", "LJ35_HEADSET"
    };
    private static readonly List<string> AudioDisplay = new() { "LJ35_COM1_TX", "LJ35_COM2_TX" };

    private static bool HandleSidewallSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_LT_EL_L": sc.SetLVar("XMLVAR_LEAR_LTS_EL_L_Position", value); return true;
            case "LJ35_LT_PNL_L": sc.SetLVar("XMLVAR_LEAR_LTS_PNL_L_Position", value); return true;
            case "LJ35_LT_FLOOD": sc.SetLVar("XMLVAR_LEAR_LTS_FLOOD_L_Position", value); return true;
            case "LJ35_LT_HSI": sc.SetLVar("XMLVAR_LEAR_LTS_HSI_Position", value); return true;
            case "LJ35_LT_MAP_L": sc.SetLVar("XMLVAR_LEAR_LTS_MAP_L_Position", value); return true;
            case "LJ35_LT_PEDESTAL": sc.SetLVar("XMLVAR_LEAR_LTS_PEDESTAL_Position", value); return true;
            case "LJ35_LT_EL_R": sc.SetLVar("XMLVAR_LEAR_LTS_EL_R_Position", value); return true;
            case "LJ35_LT_PNL_R": sc.SetLVar("XMLVAR_LEAR_LTS_PNL_R_Position", value); return true;
            case "LJ35_LT_MAP_R": sc.SetLVar("XMLVAR_LEAR_LTS_MAP_R_Position", value); return true;
            case "LJ35_WING_INSPECTION": sc.SetLVar("GENERIC_WING_INSPECTION_SWITCH", value); return true;

            case "LJ35_AUD_COM1": sc.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>K:COM1_RECEIVE_SELECT)"); return true;
            case "LJ35_AUD_COM2": sc.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>K:COM2_RECEIVE_SELECT)"); return true;
            case "LJ35_AUD_NAV1": ToggleTo(sc, "NAV SOUND:1", "Bool", value > 0.5, "RADIO_VOR1_IDENT_TOGGLE"); return true;
            case "LJ35_AUD_NAV2": ToggleTo(sc, "NAV SOUND:2", "Bool", value > 0.5, "RADIO_VOR2_IDENT_TOGGLE"); return true;
            case "LJ35_AUD_ADF1": ToggleTo(sc, "ADF SOUND:1", "Bool", value > 0.5, "RADIO_ADF_IDENT_TOGGLE"); return true;
            case "LJ35_AUD_ADF2": ToggleTo(sc, "ADF SOUND:2", "Bool", value > 0.5, "RADIO_ADF2_IDENT_TOGGLE"); return true;
            case "LJ35_AUD_DME": ToggleTo(sc, "DME SOUND:1", "Bool", value > 0.5, "RADIO_DME1_IDENT_TOGGLE"); return true;
            case "LJ35_AUD_MKR": ToggleTo(sc, "MARKER SOUND", "Bool", value > 0.5, "MARKER_SOUND_TOGGLE"); return true;
            case "LJ35_AUD_TRANSMIT":
                sc.SetLVar("XMLVAR_LEAR_SW_AUDIO_TRANS1_Position", value);
                if (value < 1.5) sc.ExecuteCalculatorCode($"{(int)value} (>K:PILOT_TRANSMITTER_SET)");
                return true;
            case "LJ35_AUD_PHONE": sc.SetLVar("GENERIC_Momentary_LEAR_SW_AUDIO_PHONE1", value); return true;
            case "LJ35_AUD_PASS_SPKR": sc.SetLVar("GENERIC_Momentary_LEAR_SW_AUDIO_PASS_SPKR1", value); return true;
            case "LJ35_AUD_MASTER_VOL": sc.SetLVar("XMLVAR_LEAR_SW_AUDIO_MSTR_VOL1_Position", value); return true;
            case "LJ35_AUD_PASS_VOL": sc.SetLVar("XMLVAR_LEAR_SW_AUDIO_SPKR_VOL1_Position", value); return true;
            case "LJ35_HEADSET": sc.SetLVar("HEADSET_FILTER", value); return true;
        }
        return false;
    }
}
