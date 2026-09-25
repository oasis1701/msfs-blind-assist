using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Glareshield → FC-530 Autopilot, and Yoke → Yoke Switches.
///
/// Every mode is the stock autopilot underneath (FC530.xml fires the stock K: events from the
/// bezel), so each mode is a combo over the stock state SimVar and the router fires the
/// vendor's OWN callback code when the pick disagrees with the state — the ALT SEL, ALT HLD
/// and SPD callbacks carry extra logic (the preselect arm, the capture altitude, the IAS/Mach
/// flip) and are transcribed verbatim rather than reduced to the bare event.
///
/// The vendor feature table for the GNS 530 fit: no go-around mode (use pitch sync), no half
/// bank, no VOR arm; capture altitude in FD-only mode, SPD Mach hold and ILS arm all work.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string AutopilotPanel = "FC-530 Autopilot";
    private const string YokePanel = "Yoke Switches";

    // FC530.xml callbacks, verbatim.
    private const string AltSelCallback =
        "(L:MODE_ALTSEL) ! (>L:MODE_ALTSEL) (L:MODE_ALTSEL) (A:AUTOPILOT ALTITUDE LOCK,bool) and if{ (>K:AP_ALT_HOLD_OFF) }";
    private const string AltHoldCallback =
        "(>K:AP_ALT_HOLD) (L:MODE_ALTSEL) (A:AUTOPILOT ALTITUDE LOCK,bool) and if{ (A:INDICATED ALTITUDE,Feet) (>A:AUTOPILOT ALTITUDE LOCK VAR:2,Feet) } " +
        "(L:MODE_ALTSEL) (A:AUTOPILOT ALTITUDE LOCK,bool) 0 == and if{ 0 (>L:MODE_ALTSEL) }";
    private const string SpdCallback =
        "(A:AUTOPILOT FLIGHT LEVEL CHANGE, Bool) 0 == if{ (>K:FLIGHT_LEVEL_CHANGE_ON) } (A:AUTOPILOT FLIGHT LEVEL CHANGE, Bool) if{ (>K:AP_MANAGED_SPEED_IN_MACH_TOGGLE) }";
    // Flysimware_AUTOPILOT_PITCH_SWITCH_Template: releases the vertical mode and syncs the pitch reference.
    private const string PitchSyncCallback =
        "(A:AUTOPILOT VERTICAL HOLD,bool) if{ (>K:AP_PANEL_VS_HOLD) } (A:AUTOPILOT FLIGHT LEVEL CHANGE,Bool) if{ (>K:FLIGHT_LEVEL_CHANGE) } " +
        "(A:AUTOPILOT ALTITUDE LOCK,bool) if{ (>K:AP_ALT_HOLD) } (>K:AP_PITCH_REF_SET)";

    private static Dictionary<string, SimVarDefinition> BuildAutopilotVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddSwitch(v, "LJ35_AP_MASTER", "LEAR_SW_AUTOPILOT", "Autopilot Master Circuit", "Off", "On",
            "Powers the FC-530 (circuits 24 and 61). Nothing on the panel works with it off.");
        AddSimSwitch(v, "LJ35_AP_ENG", "AUTOPILOT MASTER", "Autopilot Engage", "Disengaged", "Engaged");
        AddSimSwitch(v, "LJ35_AP_HDG", "AUTOPILOT HEADING LOCK", "Heading Mode");
        AddSimSwitch(v, "LJ35_AP_NAV", "AUTOPILOT NAV1 LOCK", "NAV Mode", "Off", "On", "Follows the GNS 530 in GPS, or NAV 1 with the NAV/GPS switch on NAV.");
        AddSimSwitch(v, "LJ35_AP_APR", "AUTOPILOT APPROACH HOLD", "Approach (GS) Mode", "Off", "On", "Arms the localizer and glideslope.");
        AddSimSwitch(v, "LJ35_AP_BC", "AUTOPILOT BACKCOURSE HOLD", "Back Course Mode");
        AddSimSwitch(v, "LJ35_AP_LVL", "AUTOPILOT WING LEVELER", "Wing Level Mode");
        AddSimSwitch(v, "LJ35_AP_VS", "AUTOPILOT VERTICAL HOLD", "Vertical Speed Mode", "Off", "On", "Captures the vertical speed it is engaged at.");
        AddSimSwitch(v, "LJ35_AP_ALT_HLD", "AUTOPILOT ALTITUDE LOCK", "Altitude Hold", "Off", "On", "Holds the altitude it is engaged at.");
        AddLVarState(v, "LJ35_AP_ALT_SEL", "MODE_ALTSEL", "Altitude Select (Preselect Arm)",
            new Dictionary<double, string> { [0] = "Off", [1] = "Armed" }, "Arms capture of the alerter altitude.");
        AddSimSwitch(v, "LJ35_AP_SPD", "AUTOPILOT FLIGHT LEVEL CHANGE", "Speed Mode", "Off", "On", "Holds the airspeed with pitch.");
        AddSimSwitch(v, "LJ35_AP_SPD_UNIT", "AUTOPILOT MANAGED SPEED IN MACH", "Speed Mode Units", "Knots", "Mach",
            "Pressing SPD again in the cockpit flips this.");
        AddLVarState(v, "LJ35_AP_SFT", "MODE_SFT", "Soft Ride",
            new Dictionary<double, string> { [0] = "Off", [1] = "On" }, "Not simulated by the vendor; the lamp only.");
        AddSimState(v, "LJ35_AP_HALF_BANK", "AUTOPILOT MAX BANK ID", "Bank Limit",
            new Dictionary<double, string> { [0] = "Full", [1] = "Half" }, help: "Vendor: half bank is not available with the GNS 530 fit.");
        AddButton(v, "LJ35_AP_TST", "AUTOPILOT_LIGHT_TEST", "Autopilot Lamp Test");
        AddTyped(v, "LJ35_AP_PRESELECT_SET", "Altitude Alerter", "feet", "0 to 99900, whole hundreds.", "LJ35_AP_PRESELECT");
        // ⚠️ "number", not "feet": the vendor stores the alerter as a bare number that IS feet
        // (2400 = 2,400 ft), and asking SimConnect for feet made it convert the raw value as
        // metres — 2400 read back as 7,874, a 35,000 ft alerter as 114,829 (live 2026-09-09).
        // The other unit-bearing L:var readouts were checked the same way and do not convert.
        AddReadout(v, "LJ35_AP_PRESELECT", "ALERTER_DIGITAL", "Altitude Alerter Set", "number", "F0");
        AddTyped(v, "LJ35_AP_HDG_SET", "Heading Bug", "degrees", "0 to 359.", "LJ35_AP_HDG_BUG");
        AddSimReadout(v, "LJ35_AP_HDG_BUG", "AUTOPILOT HEADING LOCK DIR", "Heading Bug", "degrees", "F0");
        AddTyped(v, "LJ35_AP_VS_SET", "Vertical Speed Target", "feet per minute", "Minus 6000 to 6000.", "LJ35_AP_VS_VAR");
        AddSimReadout(v, "LJ35_AP_VS_VAR", "AUTOPILOT VERTICAL HOLD VAR", "Vertical Speed Target", "feet per minute", "F0");
        AddTyped(v, "LJ35_AP_IAS_SET", "Airspeed Target", "knots", "80 to 350.", "LJ35_AP_IAS_VAR");
        AddSimReadout(v, "LJ35_AP_IAS_VAR", "AUTOPILOT AIRSPEED HOLD VAR", "Airspeed Target", "knots", "F0");
        AddTyped(v, "LJ35_AP_MACH_SET", "Mach Target", "mach", "0.40 to 0.81.", "LJ35_AP_MACH_VAR");
        AddSimReadout(v, "LJ35_AP_MACH_VAR", "AUTOPILOT MACH HOLD VAR", "Mach Target", "number", "F2");
        AddButton(v, "LJ35_AP_PITCH_UP", "AP_PITCH_REF", "Pitch Reference Up", "One nudge nose up in pitch hold.");
        AddButton(v, "LJ35_AP_PITCH_DN", "AP_PITCH_REF", "Pitch Reference Down", "One nudge nose down in pitch hold.");
        AddButton(v, "LJ35_GO_AROUND", "GA_MOMENTARY_BUTTON", "Go Around", "Vendor: no go-around mode with the GNS 530 fit; use pitch sync.");

        AddSimReadout(v, "LJ35_AP_ALT_ARMED", "AUTOPILOT ALTITUDE LOCK VAR:1", "Armed Altitude", "feet", "F0");
        AddSimReadout(v, "LJ35_AP_ALT_CAPTURED", "AUTOPILOT ALTITUDE LOCK VAR:2", "Held Altitude", "feet", "F0");
        AddFlag(v, "LJ35_FD_ACTIVE", "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", "Flight Director", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_AP_GS_ACTIVE", "AUTOPILOT GLIDESLOPE ACTIVE", "Glideslope", "Not captured", "Captured", simvar: true);
        AddFlag(v, "LJ35_AP_TEST_PITCH", "MODE_TST_PITCH", "Pitch Test", "Idle", "Running");
        AddFlag(v, "LJ35_AP_TEST_ROLL", "MODE_TST_ROLL", "Roll Test", "Idle", "Running");
        AddFlag(v, "LJ35_YAW_DISCONNECT", "YAW_DISCONNECT", "Yaw Damper", "Connected", "Disconnected");
        AddSimReadout(v, "LJ35_AP_PITCH_REF_VAL", "AUTOPILOT PITCH HOLD REF", "Pitch Reference", "degrees", "F1");
        Cache(v, "LJ35_AP_PRESELECT"); Cache(v, "LJ35_AP_HDG_BUG"); Cache(v, "LJ35_AP_VS_VAR");
        Cache(v, "LJ35_AP_IAS_VAR"); Cache(v, "LJ35_AP_MACH_VAR"); Cache(v, "LJ35_AP_ALT_ARMED");
        Cache(v, "LJ35_AP_ALT_CAPTURED"); Cache(v, "LJ35_FD_ACTIVE");

        // Yoke
        AddButton(v, "LJ35_YOKE_MSW", "GENERIC_LEAR_STEER_ON_PILOT_1", "Master Switch (MSW)",
            "Disengages the autopilot and releases the steer lock. Held, it stops all trim motion.");
        AddButton(v, "LJ35_YOKE_MSW_HOLD", "GENERIC_LEAR_STEER_ON_PILOT_1", "Master Switch Held (Trim Interrupt)");
        AddButton(v, "LJ35_YOKE_PITCH_SYNC", "GENERIC_Pitch_Sync_Switch_Pilot_1", "Pitch Sync",
            "Releases the vertical mode and syncs the flight director pitch to the present attitude.");
        AddButton(v, "LJ35_YOKE_MANUV", "GENERIC_Manuv_RP_Switch_Pilot_1", "Manoeuvre (Roll and Pitch)");
        return v;
    }

    private static readonly List<string> AutopilotControls = new()
    {
        "LJ35_AP_MASTER", "LJ35_AP_ENG", "LJ35_AP_HDG", "LJ35_AP_NAV", "LJ35_AP_APR", "LJ35_AP_BC", "LJ35_AP_LVL",
        "LJ35_AP_VS", "LJ35_AP_ALT_HLD", "LJ35_AP_ALT_SEL", "LJ35_AP_SPD", "LJ35_AP_SPD_UNIT", "LJ35_AP_SFT",
        "LJ35_AP_HALF_BANK", "LJ35_AP_TST", "LJ35_AP_PRESELECT_SET", "LJ35_AP_HDG_SET", "LJ35_AP_VS_SET",
        "LJ35_AP_IAS_SET", "LJ35_AP_MACH_SET", "LJ35_AP_PITCH_UP", "LJ35_AP_PITCH_DN", "LJ35_GO_AROUND"
    };

    private static readonly List<string> AutopilotDisplay = new()
    {
        "LJ35_FD_ACTIVE", "LJ35_AP_PRESELECT", "LJ35_AP_ALT_ARMED", "LJ35_AP_ALT_CAPTURED", "LJ35_AP_HDG_BUG",
        "LJ35_AP_VS_VAR", "LJ35_AP_IAS_VAR", "LJ35_AP_MACH_VAR", "LJ35_AP_PITCH_REF_VAL", "LJ35_AP_GS_ACTIVE",
        "LJ35_AP_TEST_PITCH", "LJ35_AP_TEST_ROLL", "LJ35_YAW_DISCONNECT"
    };

    private static readonly List<string> YokeControls = new()
    {
        "LJ35_YOKE_MSW", "LJ35_YOKE_MSW_HOLD", "LJ35_YOKE_PITCH_SYNC", "LJ35_YOKE_MANUV"
    };

    private static void FireWhenDiffers(SimConnectManager sc, string key, double pick, string calc)
    {
        bool current = (ReadNow(sc, key) ?? 0) > 0.5;
        if (current != pick > 0.5) sc.ExecuteCalculatorCodeUnique(calc);
    }

    private bool HandleAutopilotSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_AP_MASTER": sc.SetLVar("GENERIC_LEAR_SW_AUTOPILOT", value); return true;
            case "LJ35_AP_ENG": FireWhenDiffers(sc, varKey, value, "(>K:AP_MASTER)"); return true;
            case "LJ35_AP_HDG": FireWhenDiffers(sc, varKey, value, "(>K:AP_PANEL_HEADING_HOLD)"); return true;
            case "LJ35_AP_NAV": FireWhenDiffers(sc, varKey, value, "(>K:AP_NAV1_HOLD)"); return true;
            case "LJ35_AP_APR": FireWhenDiffers(sc, varKey, value, "(>K:AP_APR_HOLD)"); return true;
            case "LJ35_AP_BC": FireWhenDiffers(sc, varKey, value, "(>K:AP_BC_HOLD)"); return true;
            case "LJ35_AP_LVL": FireWhenDiffers(sc, varKey, value, "(>K:AP_WING_LEVELER)"); return true;
            case "LJ35_AP_VS": FireWhenDiffers(sc, varKey, value, "(>K:AP_PANEL_VS_HOLD)"); return true;
            case "LJ35_AP_ALT_HLD": FireWhenDiffers(sc, varKey, value, AltHoldCallback); return true;
            case "LJ35_AP_ALT_SEL": FireWhenDiffers(sc, varKey, value, AltSelCallback); return true;
            case "LJ35_AP_SPD":
                if (value > 0.5) sc.ExecuteCalculatorCodeUnique("(A:AUTOPILOT FLIGHT LEVEL CHANGE, Bool) 0 == if{ (>K:FLIGHT_LEVEL_CHANGE_ON) }");
                else sc.ExecuteCalculatorCodeUnique("(A:AUTOPILOT FLIGHT LEVEL CHANGE, Bool) if{ (>K:FLIGHT_LEVEL_CHANGE_OFF) }");
                return true;
            case "LJ35_AP_SPD_UNIT": FireWhenDiffers(sc, varKey, value, "(>K:AP_MANAGED_SPEED_IN_MACH_TOGGLE)"); return true;
            case "LJ35_AP_SFT": sc.SetLVar("MODE_SFT", value); return true;
            case "LJ35_AP_HALF_BANK": sc.ExecuteCalculatorCodeUnique($"{(value > 0.5 ? 1 : 0)} (>K:AP_MAX_BANK_SET)"); return true;
            case "LJ35_AP_TST": Pulse(sc, "AUTOPILOT_LIGHT_TEST", 500); return true;
            case "LJ35_AP_PRESELECT_SET": sc.SetLVar("ALERTER_DIGITAL", Lj35Preselect.Clamp(value)); return true;
            case "LJ35_AP_HDG_SET": sc.SendEvent("HEADING_BUG_SET", (uint)(((int)Math.Round(value) % 360 + 360) % 360)); return true;
            case "LJ35_AP_VS_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Clamp(Math.Round(value / 100) * 100, -6000, 6000))} (>K:AP_VS_VAR_SET_ENGLISH)"); return true;
            case "LJ35_AP_IAS_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Clamp(Math.Round(value), 80, 350))} (>K:AP_SPD_VAR_SET)"); return true;
            case "LJ35_AP_MACH_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Clamp(Math.Round(value * 100), 40, 81))} (>K:AP_MACH_VAR_SET)"); return true;
            case "LJ35_AP_PITCH_UP": sc.ExecuteCalculatorCodeUnique("(>K:AP_PITCH_REF_INC_UP)"); return true;
            case "LJ35_AP_PITCH_DN": sc.ExecuteCalculatorCodeUnique("(>K:AP_PITCH_REF_INC_DN)"); return true;
            case "LJ35_GO_AROUND": Pulse(sc, "GA_MOMENTARY_BUTTON"); return true;

            case "LJ35_YOKE_MSW": Pulse(sc, "GENERIC_LEAR_STEER_ON_PILOT_1"); return true;
            case "LJ35_YOKE_MSW_HOLD": Pulse(sc, "GENERIC_LEAR_STEER_ON_PILOT_1", 1500); return true;
            case "LJ35_YOKE_PITCH_SYNC":
                Pulse(sc, "GENERIC_Pitch_Sync_Switch_Pilot_1");
                sc.ExecuteCalculatorCodeUnique(PitchSyncCallback);
                return true;
            case "LJ35_YOKE_MANUV": Pulse(sc, "GENERIC_Manuv_RP_Switch_Pilot_1"); return true;
        }
        return false;
    }
}
