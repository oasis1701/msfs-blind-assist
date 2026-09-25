using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Glareshield: the GMC 7200 autopilot controller, the master warning / caution and fire
/// panel, and the GH-3900 standby instrument's menu. Measured 2026-09-10 (docs/citation680-variables.md):
/// the autopilot is the stock G3000 autopilot on stock K: events; the vendor plugin INTERCEPTS
/// AUTOPILOT_OFF and the AUTO_THROTTLE_* keys and owns the yoke AP DISC buttons through their
/// _Pressed L:vars; VNAV is the Working Title H:AS1000_VNAV_TOGGLE; the autothrottle status is
/// the plugin's own L:SW_SOV_Autothrottle_Status (0 Off, 1 Disconnected, 2 Armed, 3 On).
/// Not exposed, and why: the bank limit (neither AP_MAX_BANK_SET nor _INC moved the stock bank
/// id and the GMC lamp is not published) and the IAS/Mach units (AP_MANAGED_SPEED_IN_MACH_ON is
/// inert on the ground; the touchscreen's speed-bug page owns it).
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildGlareshieldVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- Autopilot and Flight Director (GMC 7200)
        AddSimSwitch(v, "C680_AP_MASTER", "AUTOPILOT MASTER", "AP Button", "Off", "Engaged", "The G3000 refuses to engage on the ground.");
        AddSimSwitch(v, "C680_AP_YD", "AUTOPILOT YAW DAMPER", "YD Button");
        AddSimSwitch(v, "C680_AP_FD_L", "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", "Left FD Button");
        AddSimSwitch(v, "C680_AP_FD_R", "AUTOPILOT FLIGHT DIRECTOR ACTIVE:2", "Right FD Button");
        AddSimSwitch(v, "C680_AP_HDG", "AUTOPILOT HEADING LOCK", "HDG Button");
        AddSimSwitch(v, "C680_AP_NAV", "AUTOPILOT NAV1 LOCK", "NAV Button");
        AddSimSwitch(v, "C680_AP_APR", "AUTOPILOT APPROACH HOLD", "APR Button");
        AddSimSwitch(v, "C680_AP_BC", "AUTOPILOT BACKCOURSE HOLD", "BC Button");
        AddSimSwitch(v, "C680_AP_ALT", "AUTOPILOT ALTITUDE LOCK", "ALT Button");
        AddSimSwitch(v, "C680_AP_VS", "AUTOPILOT VERTICAL HOLD", "VS Button");
        AddSimSwitch(v, "C680_AP_FLC", "AUTOPILOT FLIGHT LEVEL CHANGE", "FLC Button");
        AddButton(v, "C680_AP_VNAV", "VNAV Button", "Arms or disarms VNAV; the armed vertical mode reads on the PFD flight mode annunciator.");
        AddButton(v, "C680_AT_ARM", "AT Button", "Arms the autothrottle, or disconnects it when it is armed or on.");
        AddButton(v, "C680_AT_DISC", "AT Disconnect (throttle)");
        AddTyped(v, "C680_AP_ALT_SET", "Altitude Preselect", "feet", "100 to 47000, rounded to 100", currentKey: "C680_AP_ALT_SEL");
        AddTyped(v, "C680_AP_HDG_SET", "Heading Bug", "degrees", "0 to 359", currentKey: "C680_AP_HDG_BUG");
        AddTyped(v, "C680_AP_VS_SET", "Vertical Speed Target", "feet per minute", "-6000 to 6000, rounded to 100", currentKey: "C680_AP_VS_TGT");
        AddTyped(v, "C680_AP_SPD_SET", "Speed Target", "knots or Mach", "100 to 305 knots, or 0.30 to 0.80 Mach", currentKey: "C680_AP_SPD_TGT");
        AddButton(v, "C680_TOGA", "TO/GA Button");
        AddButton(v, "C680_AP_DISC", "AP Disconnect (yoke)");
        AddSwitch(v, "C680_CWS", "SW_SOV_AUTOPILOT_CVS", "CWS Button (yoke)", "Off", "On");
        AddSimReadout(v, "C680_AP_ALT_SEL", "AUTOPILOT ALTITUDE LOCK VAR", "Altitude Preselect", "feet", "F0");
        AddSimReadout(v, "C680_AP_HDG_BUG", "AUTOPILOT HEADING LOCK DIR", "Heading Bug", "degrees", "F0");
        AddSimReadout(v, "C680_AP_VS_TGT", "AUTOPILOT VERTICAL HOLD VAR", "Vertical Speed Target", "feet per minute", "F0");
        AddSimReadout(v, "C680_AP_SPD_TGT", "AUTOPILOT AIRSPEED HOLD VAR", "Speed Target", "knots", "F0");
        AddSimReadout(v, "C680_AP_MACH_TGT", "AUTOPILOT MACH HOLD VAR", "Mach Target", "number", "F2");
        AddFlag(v, "C680_AP_SPD_IS_MACH", "AUTOPILOT MANAGED SPEED IN MACH", "Speed Units", "Knots", "Mach", simvar: true);
        AddSwitch(v, "C680_AP_SPD_MANUAL", "XMLVAR_SpeedIsManuallySet", "Speed Source", "FMS", "Manual",
            "FMS lets the G3000 compute the FLC speed from the performance plan and REFUSES every typed target; Manual takes the typed target. Setting a target switches to Manual.");
        AddStateReadout(v, "C680_AT_STATUS", "SW_SOV_Autothrottle_Status", "Autothrottle",
            new Dictionary<double, string> { [0] = "Off", [1] = "Disconnected", [2] = "Armed", [3] = "On" });
        AddFlag(v, "C680_AP_GS", "AUTOPILOT GLIDESLOPE ACTIVE", "Glideslope", "Not captured", "Captured", simvar: true);
        AddFlag(v, "C680_AP_APR_ARMED", "AUTOPILOT APPROACH ARM", "Approach", "Not armed", "Armed", simvar: true);

        // ---- Warning and Fire (stock master lamps; the vendor's fire panel L:vars)
        AddButton(v, "C680_MASTER_WARN_ACK", "MASTER WARNING Acknowledge");
        AddButton(v, "C680_MASTER_CAUT_ACK", "MASTER CAUTION Acknowledge");
        AddAnnounced(v, "C680_MASTER_WARN", "MASTER WARNING ACTIVE", "Master Warning", "Master warning cleared", "Master warning");
        AddAnnounced(v, "C680_MASTER_CAUT", "MASTER CAUTION ACTIVE", "Master Caution", "Master caution cleared", "Master caution");
        v["C680_MASTER_WARN"].Type = SimVarType.SimVar; v["C680_MASTER_WARN"].Units = "bool";
        v["C680_MASTER_CAUT"].Type = SimVarType.SimVar; v["C680_MASTER_CAUT"].Units = "bool";
        AddSwitch(v, "C680_FIRE_L_COVER", "SAFETY_Push_Extinguisher_1_Cover", "Left ENG FIRE Cover", "Closed", "Open");
        AddSwitch(v, "C680_FIRE_L", "SAFETY_Push_Extinguisher_1", "Left ENG FIRE Button", "Normal", "Pushed", "Needs the cover open. Pushing arms the bottles and closes the fuel and hydraulic shutoffs.");
        AddButton(v, "C680_BOTTLE_L", "Left BOTTLE ARMED Button (discharge)");
        AddSwitch(v, "C680_FIRE_R_COVER", "SAFETY_Push_Extinguisher_2_Cover", "Right ENG FIRE Cover", "Closed", "Open");
        AddSwitch(v, "C680_FIRE_R", "SAFETY_Push_Extinguisher_2", "Right ENG FIRE Button", "Normal", "Pushed", "Needs the cover open.");
        AddButton(v, "C680_BOTTLE_R", "Right BOTTLE ARMED Button (discharge)");
        AddSwitch(v, "C680_FIRE_APU_COVER", "SAFETY_Push_Extinguisher_APU_Cover", "APU FIRE Cover", "Closed", "Open");
        AddButton(v, "C680_FIRE_APU", "APU FIRE Button");
        AddSwitch(v, "C680_BAG_FIRE_COVER", "SAFETY_Push_Baggage_Fire_Cover", "BAGGAGE FIRE Cover", "Closed", "Open");
        AddButton(v, "C680_BAG_FIRE", "BAGGAGE FIRE Button");
        AddSwitch(v, "C680_BAG_BOTTLE_COVER", "SAFETY_Push_Sec_Bag_Bottle_Cover", "Second Bag Bottle Cover", "Closed", "Open");
        AddButton(v, "C680_BAG_BOTTLE", "Second Bag Bottle Button");
        AddFlag(v, "C680_FIRE_L_LIT", "SAFETY_Push_Extinguisher_1_LIT", "Left ENG FIRE Light", "Out", "Lit");
        AddFlag(v, "C680_FIRE_R_LIT", "SAFETY_Push_Extinguisher_2_LIT", "Right ENG FIRE Light", "Out", "Lit");
        AddFlag(v, "C680_BOTTLE_L_LIT", "SAFETY_Push_Extinguisher_Arm_1_LIT", "Left Bottle Armed Light", "Out", "Lit");
        AddFlag(v, "C680_BOTTLE_R_LIT", "SAFETY_Push_Extinguisher_Arm_2_LIT", "Right Bottle Armed Light", "Out", "Lit");
        AddFlag(v, "C680_FIRE_APU_LIT", "SAFETY_Push_Extinguisher_APU_LIT", "APU Fire Light", "Out", "Lit");
        AddFlag(v, "C680_BAG_FIRE_LIT", "SAFETY_Push_Baggage_Fire_LIT", "Baggage Fire Light", "Out", "Lit");
        AddFlag(v, "C680_BAG_BOTTLE_LIT", "SAFETY_Push_Sec_Bag_Bottle_LIT", "Second Bag Bottle Light", "Out", "Lit");
        AddSwitch(v, "C680_TEST_ANNUN", "SW_SOV_TEST_ANNUNCIATOR", "Annunciator Test", "Off", "Testing");

        // ---- Standby Instrument (GH-3900; the EFB Settings page writes the same L:vars)
        AddSwitch(v, "C680_SAI_BL_MODE", "SW_SOV_SAI_BACKLIGHT_MODE", "Standby Backlight Mode", "Auto", "Manual");
        AddKnob(v, "C680_SAI_BL", "SW_SOV_GH3900_BL", "Standby Backlight", max: 1);
        AddSwitch(v, "C680_SAI_QNH_UNIT", "SW_SOV_GH3900_QNH", "Standby QNH Unit", "hPa", "inHg");
        AddSwitch(v, "C680_SAI_METER", "SW_SOV_GH3900_METER", "Standby Meter Overlay");
        AddSwitch(v, "C680_SAI_TURN", "SW_SOV_GH3900_TURN_IND", "Standby Turn Indicator");
        AddSwitch(v, "C680_SAI_GS", "SW_SOV_GH3900_GS", "Standby Ground Speed");
        AddSimReadout(v, "C680_SAI_BARO", "KOHLSMAN SETTING MB:3", "Standby Baro", "millibars", "F0");
        AddDerived(v, "C680_SAI_LIMITS", "Standby Limits");

        foreach (var k in new[] { "C680_AP_ALT_SEL", "C680_AP_HDG_BUG", "C680_AP_VS_TGT", "C680_AP_SPD_TGT", "C680_AP_MACH_TGT", "C680_AP_SPD_IS_MACH", "C680_AT_STATUS" })
            Cache(v, k);
        return v;
    }

    private static readonly List<string> AutopilotControls = new() { "C680_AP_MASTER", "C680_AP_YD", "C680_AP_FD_L", "C680_AP_FD_R", "C680_AP_HDG", "C680_AP_NAV", "C680_AP_APR", "C680_AP_BC", "C680_AP_ALT", "C680_AP_VS", "C680_AP_FLC", "C680_AP_VNAV", "C680_AT_ARM", "C680_AT_DISC", "C680_AP_ALT_SET", "C680_AP_HDG_SET", "C680_AP_VS_SET", "C680_AP_SPD_SET", "C680_AP_SPD_MANUAL", "C680_TOGA", "C680_AP_DISC", "C680_CWS" };
    private static readonly List<string> AutopilotDisplay = new() { "C680_AP_ALT_SEL", "C680_AP_HDG_BUG", "C680_AP_VS_TGT", "C680_AP_SPD_TGT", "C680_AP_MACH_TGT", "C680_AP_SPD_IS_MACH", "C680_AP_SPD_MANUAL", "C680_AT_STATUS", "C680_AP_APR_ARMED", "C680_AP_GS" };
    private static readonly List<string> WarningControls = new() { "C680_MASTER_WARN_ACK", "C680_MASTER_CAUT_ACK", "C680_FIRE_L_COVER", "C680_FIRE_L", "C680_BOTTLE_L", "C680_FIRE_R_COVER", "C680_FIRE_R", "C680_BOTTLE_R", "C680_FIRE_APU_COVER", "C680_FIRE_APU", "C680_BAG_FIRE_COVER", "C680_BAG_FIRE", "C680_BAG_BOTTLE_COVER", "C680_BAG_BOTTLE", "C680_TEST_ANNUN" };
    private static readonly List<string> WarningDisplay = new() { "C680_MASTER_WARN", "C680_MASTER_CAUT", "C680_FIRE_L_LIT", "C680_BOTTLE_L_LIT", "C680_FIRE_R_LIT", "C680_BOTTLE_R_LIT", "C680_FIRE_APU_LIT", "C680_BAG_FIRE_LIT", "C680_BAG_BOTTLE_LIT" };
    private static readonly List<string> StandbyControls = new() { "C680_SAI_BL_MODE", "C680_SAI_BL", "C680_SAI_QNH_UNIT", "C680_SAI_METER", "C680_SAI_TURN", "C680_SAI_GS" };
    private static readonly List<string> StandbyDisplay = new() { "C680_SAI_BARO", "C680_SAI_LIMITS" };

    private static readonly HashSet<string> GlareshieldPlainLVars = new(StringComparer.Ordinal)
    {
        "C680_CWS", "C680_FIRE_L_COVER", "C680_FIRE_L", "C680_FIRE_R_COVER", "C680_FIRE_R", "C680_FIRE_APU_COVER",
        "C680_BAG_FIRE_COVER", "C680_BAG_BOTTLE_COVER", "C680_TEST_ANNUN",
        "C680_SAI_BL_MODE", "C680_SAI_BL", "C680_SAI_QNH_UNIT", "C680_SAI_METER", "C680_SAI_TURN", "C680_SAI_GS"
    };

    /// <summary>
    /// The FLC speed target. The G3000's FmsSpeedManager (mfd.js) INTERCEPTS AP_SPD_VAR_SET,
    /// AP_SPD_VAR_SET_EX1, AP_MACH_VAR_SET and the DEC events and only passes them through while
    /// L:XMLVAR_SpeedIsManuallySet is 1 (its ap_selected_speed_is_manual topic) — in FMS speed mode
    /// every absolute set is silently swallowed and the target sat at 80 knots (measured airborne
    /// 2026-09-10: 250 (>K:AP_SPD_VAR_SET) left 80; AP_SPD_VAR_INC, not intercepted, moved it to 81;
    /// with the flag set the same set landed 200). So a typed target always switches to manual
    /// first, the same thing pressing the speed knob does on the real GTC. Mach goes as hundredths.
    /// </summary>
    private static void SetSpeedTarget(SimConnectManager sc, double value)
    {
        string set = value < 1 ? $"{Rpn(Math.Round(value * 100))} (>K:AP_MACH_VAR_SET)" : $"{Rpn(Math.Round(value))} (>K:AP_SPD_VAR_SET)";
        sc.ExecuteCalculatorCode($"1 (>L:XMLVAR_SpeedIsManuallySet) {set}");
    }

    private bool HandleGlareshieldSet(string varKey, double value, SimConnectManager sc)
    {
        if (GlareshieldPlainLVars.Contains(varKey))
        {
            sc.SetLVar(GetVariables()[varKey].Name, value);
            return true;
        }
        bool on = value > 0.5;
        switch (varKey)
        {
            case "C680_AP_MASTER": ToggleTo(sc, "AUTOPILOT MASTER", "Bool", on, "AP_MASTER"); return true;
            case "C680_AP_YD": ToggleTo(sc, "AUTOPILOT YAW DAMPER", "Bool", on, "YAW_DAMPER_TOGGLE"); return true;
            case "C680_AP_FD_L": sc.ExecuteCalculatorCode($"(A:AUTOPILOT FLIGHT DIRECTOR ACTIVE:1, Bool) {(on ? 0 : 1)} == if{{ 1 (>K:TOGGLE_FLIGHT_DIRECTOR) }}"); return true;
            case "C680_AP_FD_R": sc.ExecuteCalculatorCode($"(A:AUTOPILOT FLIGHT DIRECTOR ACTIVE:2, Bool) {(on ? 0 : 1)} == if{{ 2 (>K:TOGGLE_FLIGHT_DIRECTOR) }}"); return true;
            case "C680_AP_HDG": ToggleTo(sc, "AUTOPILOT HEADING LOCK", "Bool", on, "AP_PANEL_HEADING_HOLD"); return true;
            case "C680_AP_NAV": ToggleTo(sc, "AUTOPILOT NAV1 LOCK", "Bool", on, "AP_NAV1_HOLD"); return true;
            case "C680_AP_APR": ToggleTo(sc, "AUTOPILOT APPROACH HOLD", "Bool", on, "AP_APR_HOLD"); return true;
            case "C680_AP_BC": ToggleTo(sc, "AUTOPILOT BACKCOURSE HOLD", "Bool", on, "AP_BC_HOLD"); return true;
            case "C680_AP_ALT": ToggleTo(sc, "AUTOPILOT ALTITUDE LOCK", "Bool", on, "AP_PANEL_ALTITUDE_HOLD"); return true;
            case "C680_AP_VS": ToggleTo(sc, "AUTOPILOT VERTICAL HOLD", "Bool", on, "AP_PANEL_VS_HOLD"); return true;
            case "C680_AP_FLC": ToggleTo(sc, "AUTOPILOT FLIGHT LEVEL CHANGE", "Bool", on, "FLIGHT_LEVEL_CHANGE"); return true;
            case "C680_AP_VNAV": sc.ExecuteCalculatorCodeUnique("(>H:AS1000_VNAV_TOGGLE)"); return true;
            case "C680_AT_ARM": sc.ExecuteCalculatorCodeUnique("(>K:AUTO_THROTTLE_ARM)"); return true;
            case "C680_AT_DISC": sc.ExecuteCalculatorCodeUnique("(>K:AUTO_THROTTLE_DISCONNECT)"); return true;
            case "C680_AP_ALT_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Round(value / 100) * 100)} (>K:AP_ALT_VAR_SET_ENGLISH)"); return true;
            case "C680_AP_HDG_SET": sc.ExecuteCalculatorCode($"{Rpn(((value % 360) + 360) % 360)} (>K:HEADING_BUG_SET)"); return true;
            case "C680_AP_VS_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Round(value / 100) * 100)} (>K:AP_VS_VAR_SET_ENGLISH)"); return true;
            case "C680_AP_SPD_SET": SetSpeedTarget(sc, value); return true;
            case "C680_AP_SPD_MANUAL": sc.SetLVar("XMLVAR_SpeedIsManuallySet", value); return true;
            case "C680_TOGA": sc.ExecuteCalculatorCodeUnique("(>K:AUTO_THROTTLE_TO_GA)"); return true;
            case "C680_AP_DISC": Pulse(sc, "SW_SOV_AUTOPILOT_Push_Disconnect_1_Pressed", 300); sc.ExecuteCalculatorCodeUnique("(>K:AUTOPILOT_OFF)"); return true;

            case "C680_MASTER_WARN_ACK": sc.ExecuteCalculatorCodeUnique("(>K:MASTER_WARNING_ACKNOWLEDGE)"); return true;
            case "C680_MASTER_CAUT_ACK": sc.ExecuteCalculatorCodeUnique("(>K:MASTER_CAUTION_ACKNOWLEDGE)"); return true;
            case "C680_BOTTLE_L": Pulse(sc, "SAFETY_Push_Extinguisher_Arm_1", 500); return true;
            case "C680_BOTTLE_R": Pulse(sc, "SAFETY_Push_Extinguisher_Arm_2", 500); return true;
            case "C680_FIRE_APU": Pulse(sc, "SAFETY_Push_Extinguisher_APU", 500); return true;
            case "C680_BAG_FIRE": Pulse(sc, "SAFETY_Push_Baggage_Fire", 500); return true;
            case "C680_BAG_BOTTLE": Pulse(sc, "SAFETY_Push_Sec_Bag_Bottle", 500); return true;
        }
        return false;
    }
}
