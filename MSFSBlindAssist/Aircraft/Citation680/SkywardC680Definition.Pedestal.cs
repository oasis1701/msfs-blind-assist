using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Pedestal: thrust levers and reversers, flaps / speedbrake / trims, gear and brakes, the
/// flight-control handles, the yoke buttons and the passenger signs and cabin lights.
/// The speedbrake lever, park brake, gear lever, control lock and signs are the vendor's own
/// L:vars (its plugin drives the stock systems from them); flaps and trims are the stock
/// handles on stock events. Owner's rule: nothing here moves the gear on the ground — the
/// gear write is refused, out loud, while SIM ON GROUND is true.
/// Live 2026-09-10: the signs and cabin overhead light L:vars stuck and LIGHT CABIN followed;
/// the stock CABIN SEATBELTS ALERT SWITCH is NOT mirrored (stays 0 with the sign on).
/// Not exposed: the passenger briefing (five per-language trigger L:vars, not a selector) and
/// a takeoff-trim band (the EIS arc has not been measured).
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildPedestalVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- Thrust and Autothrottle (the AT and AT DISC buttons live on the Autopilot panel)
        AddSimReadout(v, "C680_THR_L", "GENERAL ENG THROTTLE LEVER POSITION:1", "Left Thrust Lever", "percent", "F0");
        AddSimReadout(v, "C680_THR_R", "GENERAL ENG THROTTLE LEVER POSITION:2", "Right Thrust Lever", "percent", "F0");
        AddTyped(v, "C680_THR_SET", "Both Thrust Levers", "percent", "0 to 100", currentKey: "C680_THR_L");
        AddSimSwitch(v, "C680_REV_L", "TURB ENG REVERSE NOZZLE PERCENT:1", "Left Thrust Reverser", "Stowed", "Deployed", units: "percent");
        AddSimSwitch(v, "C680_REV_R", "TURB ENG REVERSE NOZZLE PERCENT:2", "Right Thrust Reverser", "Stowed", "Deployed", units: "percent");
        AddSwitch(v, "C680_AT_LOCK", "SW_SOV_CONFIG_LOCK_THROTTLE_ON_AT", "Lock Throttle On Autothrottle");
        AddSimReadout(v, "C680_N1_TGT_L", "TURB ENG N1:1", "Left N1", "percent", "F1");
        AddSimReadout(v, "C680_N1_TGT_R", "TURB ENG N1:2", "Right N1", "percent", "F1");

        // ---- Flaps, Speedbrakes and Trim (stock flap lever, 0 Up / 1 / 2 / 3 Full; the speedbrake lever is the vendor's percent L:var)
        AddSimState(v, "C680_FLAPS", "FLAPS HANDLE INDEX", "Flap Selector",
            new Dictionary<double, string> { [0] = "Up", [1] = "1", [2] = "2", [3] = "Full" });
        AddKnob(v, "C680_SPEEDBRAKE", "SW_SOV_HANDLING_SPOILER_LEVER", "SPEEDBRAKE Lever", "0 is stowed, 100 is fully extended.");
        AddButton(v, "C680_FLAPS_RESET", "Flaps Reset Button (hold)");
        AddButton(v, "C680_TRIM_NU", "Pitch Trim Nose Up");
        AddButton(v, "C680_TRIM_ND", "Pitch Trim Nose Down");
        AddButton(v, "C680_TRIM_AIL_L", "Aileron Trim Left");
        AddButton(v, "C680_TRIM_AIL_R", "Aileron Trim Right");
        AddButton(v, "C680_TRIM_RUD_L", "Rudder Trim Left");
        AddButton(v, "C680_TRIM_RUD_R", "Rudder Trim Right");
        AddSwitch(v, "C680_SEC_TRIM_COVER", "HANDLING_Push_ElevatorTrim_Secondary_Cover", "Secondary Trim Cover", "Closed", "Open");
        AddSwitch(v, "C680_SEC_TRIM", "SW_SOV_HANDLING_Push_ElevatorTrim_Secondary", "Secondary Pitch Trim", "Off", "On", "Needs the cover open.");
        AddSimReadout(v, "C680_FLAP_DEG", "TRAILING EDGE FLAPS LEFT ANGLE", "Flap Angle", "degrees", "F0");
        AddSimReadout(v, "C680_SPOILER", "SPOILERS HANDLE POSITION", "Speedbrake Position", "percent", "F0");
        AddSimReadout(v, "C680_TRIM_PITCH", "ELEVATOR TRIM PCT", "Pitch Trim", "percent", "F1");
        AddSimReadout(v, "C680_TRIM_AIL", "AILERON TRIM PCT", "Aileron Trim", "percent", "F1");
        AddSimReadout(v, "C680_TRIM_RUD", "RUDDER TRIM PCT", "Rudder Trim", "percent", "F1");

        // ---- Gear and Brakes (the vendor's lever L:var; the plugin moves the gear)
        AddSelector(v, "C680_GEAR", "SW_SOV_LANDING_GEAR_LEVER", "Landing Gear Handle", new[] { "Up", "Down" },
            "Refused on the ground: the gear stays down until airborne.");
        AddSwitch(v, "C680_PARK_BRAKE", "SW_SOV_PARKING_BRAKE", "PARK BRAKE", "Released", "Set");
        AddButton(v, "C680_EMER_BRAKE", "EMER BRAKE Handle (pull)");
        AddSwitch(v, "C680_GRAV_GEAR_MAIN", "SW_SOV_HANDLING_Gravity_Gear_Main_1", "Gravity Gear Main Handle", "Stowed", "Pulled");
        AddSwitch(v, "C680_GRAV_GEAR_NOSE", "SW_SOV_HANDLING_Gravity_Gear_Nose_1", "Gravity Gear Nose Handle", "Stowed", "Pulled");
        AddSwitch(v, "C680_GEAR_BLOWDOWN", "SW_SOV_HANDLING_GEAR_BLOWDOWN", "Nitrogen Blowdown Handle", "Stowed", "Pulled");
        AddSimReadout(v, "C680_GEAR_C", "GEAR CENTER POSITION", "Nose Gear", "percent", "F0");
        AddSimReadout(v, "C680_GEAR_L", "GEAR LEFT POSITION", "Left Gear", "percent", "F0");
        AddSimReadout(v, "C680_GEAR_R", "GEAR RIGHT POSITION", "Right Gear", "percent", "F0");
        AddFlag(v, "C680_GEAR_LOCK_C", "SW_SOV_HANDLING_GEAR_LOCKED:1", "Nose Gear Lock", "Not locked", "Locked");
        AddFlag(v, "C680_GEAR_LOCK_L", "SW_SOV_HANDLING_GEAR_LOCKED:2", "Left Gear Lock", "Not locked", "Locked");
        AddFlag(v, "C680_GEAR_LOCK_R", "SW_SOV_HANDLING_GEAR_LOCKED:3", "Right Gear Lock", "Not locked", "Locked");
        AddFlag(v, "C680_GEAR_POWER", "SW_SOV_HANDLING_GEAR_POWER_ON", "Gear Power", "Off", "On");
        AddFlag(v, "C680_PARK_BRAKE_STATE", "BRAKE PARKING POSITION", "Parking Brake", "Released", "Set", simvar: true);
        AddFlag(v, "C680_ANTISKID", "CIRCUIT ON:126", "Anti-Skid Circuit", "Off", "On", simvar: true);
        AddFlag(v, "C680_ON_GROUND", "SIM ON GROUND", "On Ground", "No", "Yes", simvar: true);

        // ---- Flight Controls
        AddSwitch(v, "C680_CONTROL_LOCK", "SW_SOV_CONTROL_LOCK", "Control Lock", "Off", "On");
        AddSwitch(v, "C680_RUDDER_BIAS_COVER", "HANDLING_Push_RudderBias_Cover", "Rudder Bias Cover", "Closed", "Open");
        AddSwitch(v, "C680_RUDDER_BIAS", "HANDLING_Push_RudderBias", "Rudder Bias", "Off", "On", "Needs the cover open.");
        AddSelector(v, "C680_CSF_DISC", "SW_SOV_CSF_Disconnect", "Control Disconnect Handle", new[] { "Connected", "Pulled 1", "Pulled 2", "Pulled 3" },
            "The model's four-detent handle; the aileron and elevator rows below say which axis has let go.");
        AddSwitch(v, "C680_TILLER", "SW_SOV_USE_TILLER_AXIS", "Separate Tiller Axis");
        AddFlag(v, "C680_RB_POWERED", "SW_SOV_RUDDER_BIAS_IS_POWERED", "Rudder Bias Power", "Off", "On");
        AddFlag(v, "C680_AIL_DISC", "Skyward_Ailerons_Disconnected", "Ailerons", "Connected", "Disconnected");
        AddFlag(v, "C680_ELEV_DISC", "Skyward_Elevator_L_Disconnected", "Elevators", "Connected", "Disconnected");
        AddFlag(v, "C680_SHAKER_L", "SW_SOV_HANDLING_STICKSHAKER_L", "Left Stick Shaker", "Off", "Active");
        AddFlag(v, "C680_SHAKER_R", "SW_SOV_HANDLING_STICKSHAKER_R", "Right Stick Shaker", "Off", "Active");
        AddFlag(v, "C680_OVERSPEED", "SW_SOV_OVERSPEED", "Overspeed", "No", "Yes");

        // ---- Yoke
        AddButton(v, "C680_IDENT", "IDENT Button");
        AddButton(v, "C680_MIC_L", "Left MIC Button");
        AddButton(v, "C680_MIC_R", "Right MIC Button");
        AddFlag(v, "C680_WHO_FLIES", "SW_SOV_Pilot_CoPilot_Control", "Control Seat", "Pilot", "Copilot");

        // ---- Passenger Signs and Cabin (checklist: SEAT BELTS and PAX SAFETY buttons)
        AddSwitch(v, "C680_SEAT_BELTS", "SW_SOV_PASSENGER_SEAT_BELT", "SEAT BELTS Button");
        AddSwitch(v, "C680_PAX_SAFETY", "SW_SOV_PASSENGER_SAFETY_PUSH", "PAX SAFETY Button");
        AddSwitch(v, "C680_CABIN_OVERHEAD", "SW_SOV_LIGHT_OVERHEAD", "Cabin Overhead Lights");
        AddKnob(v, "C680_CABIN_OVERHEAD_BRT", "SW_SOV_LIGHT_OVERHEAD_BRT", "Cabin Overhead Brightness", max: 1);
        AddSwitch(v, "C680_LIGHT_VANITY", "SW_SOV_LIGHT_VANITY", "Vanity Light");
        AddSwitch(v, "C680_LIGHT_BAG", "SW_SOV_LIGHTS_BAGGAGE", "Baggage Compartment Light");
        AddKnob(v, "C680_MUSIC_VOL", "PAX_MUSIC_VOL", "Cabin Music Volume");

        foreach (var k in new[] { "C680_THR_L", "C680_THR_R", "C680_FLAP_DEG", "C680_SPOILER", "C680_GEAR_C", "C680_GEAR_L", "C680_GEAR_R",
            "C680_PARK_BRAKE_STATE", "C680_TRIM_PITCH", "C680_TRIM_AIL", "C680_TRIM_RUD", "C680_ON_GROUND" })
            Cache(v, k);
        return v;
    }

    private static readonly List<string> ThrustControls = new() { "C680_THR_SET", "C680_REV_L", "C680_REV_R", "C680_AT_LOCK" };
    private static readonly List<string> ThrustDisplay = new() { "C680_THR_L", "C680_THR_R", "C680_N1_TGT_L", "C680_N1_TGT_R", "C680_AT_STATUS" };
    private static readonly List<string> FlapsControls = new() { "C680_FLAPS", "C680_SPEEDBRAKE", "C680_FLAPS_RESET", "C680_TRIM_NU", "C680_TRIM_ND", "C680_TRIM_AIL_L", "C680_TRIM_AIL_R", "C680_TRIM_RUD_L", "C680_TRIM_RUD_R", "C680_SEC_TRIM_COVER", "C680_SEC_TRIM" };
    private static readonly List<string> FlapsDisplay = new() { "C680_FLAP_DEG", "C680_SPOILER", "C680_TRIM_PITCH", "C680_TRIM_AIL", "C680_TRIM_RUD" };
    private static readonly List<string> GearControls = new() { "C680_GEAR", "C680_PARK_BRAKE", "C680_EMER_BRAKE", "C680_GRAV_GEAR_MAIN", "C680_GRAV_GEAR_NOSE", "C680_GEAR_BLOWDOWN" };
    private static readonly List<string> GearDisplay = new() { "C680_GEAR_C", "C680_GEAR_LOCK_C", "C680_GEAR_L", "C680_GEAR_LOCK_L", "C680_GEAR_R", "C680_GEAR_LOCK_R", "C680_GEAR_POWER", "C680_PARK_BRAKE_STATE", "C680_ANTISKID", "C680_ON_GROUND" };
    private static readonly List<string> FlightControlsControls = new() { "C680_CONTROL_LOCK", "C680_RUDDER_BIAS_COVER", "C680_RUDDER_BIAS", "C680_CSF_DISC", "C680_TILLER" };
    private static readonly List<string> FlightControlsDisplay = new() { "C680_RB_POWERED", "C680_AIL_DISC", "C680_ELEV_DISC", "C680_SHAKER_L", "C680_SHAKER_R", "C680_OVERSPEED" };
    private static readonly List<string> YokeControls = new() { "C680_IDENT", "C680_MIC_L", "C680_MIC_R" };
    private static readonly List<string> YokeDisplay = new() { "C680_WHO_FLIES" };
    private static readonly List<string> SignsControls = new() { "C680_SEAT_BELTS", "C680_PAX_SAFETY", "C680_CABIN_OVERHEAD", "C680_CABIN_OVERHEAD_BRT", "C680_LIGHT_VANITY", "C680_LIGHT_BAG", "C680_MUSIC_VOL" };

    private static readonly HashSet<string> PedestalPlainLVars = new(StringComparer.Ordinal)
    {
        "C680_AT_LOCK", "C680_SPEEDBRAKE", "C680_SEC_TRIM_COVER", "C680_SEC_TRIM", "C680_PARK_BRAKE",
        "C680_GRAV_GEAR_MAIN", "C680_GRAV_GEAR_NOSE", "C680_GEAR_BLOWDOWN", "C680_CONTROL_LOCK", "C680_RUDDER_BIAS_COVER",
        "C680_RUDDER_BIAS", "C680_CSF_DISC", "C680_TILLER", "C680_SEAT_BELTS", "C680_PAX_SAFETY", "C680_CABIN_OVERHEAD",
        "C680_CABIN_OVERHEAD_BRT", "C680_LIGHT_VANITY", "C680_LIGHT_BAG", "C680_MUSIC_VOL"
    };

    private bool HandlePedestalSet(string varKey, double value, SimConnectManager sc, ScreenReaderAnnouncer announcer)
    {
        if (PedestalPlainLVars.Contains(varKey))
        {
            sc.SetLVar(GetVariables()[varKey].Name, value);
            return true;
        }
        switch (varKey)
        {
            case "C680_THR_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Clamp(value, 0, 100) * 163.84)} (>K:THROTTLE_SET)"); return true;
            case "C680_REV_L": sc.ExecuteCalculatorCodeUnique(value > 0.5 ? "(>K:THROTTLE1_DECR)" : "(>K:THROTTLE1_CUT)"); return true;
            case "C680_REV_R": sc.ExecuteCalculatorCodeUnique(value > 0.5 ? "(>K:THROTTLE2_DECR)" : "(>K:THROTTLE2_CUT)"); return true;
            case "C680_FLAPS":
                sc.ExecuteCalculatorCodeUnique((int)Math.Round(value) switch { 0 => "(>K:FLAPS_UP)", 1 => "(>K:FLAPS_1)", 2 => "(>K:FLAPS_2)", _ => "(>K:FLAPS_DOWN)" });
                return true;
            case "C680_FLAPS_RESET": Pulse(sc, "SW_SOV_HANDLING_Push_FlapsReset", 1200); return true;
            case "C680_TRIM_NU": sc.ExecuteCalculatorCodeUnique("(>K:ELEV_TRIM_UP)"); return true;
            case "C680_TRIM_ND": sc.ExecuteCalculatorCodeUnique("(>K:ELEV_TRIM_DN)"); return true;
            case "C680_TRIM_AIL_L": sc.ExecuteCalculatorCodeUnique("(>K:AILERON_TRIM_LEFT)"); return true;
            case "C680_TRIM_AIL_R": sc.ExecuteCalculatorCodeUnique("(>K:AILERON_TRIM_RIGHT)"); return true;
            case "C680_TRIM_RUD_L": sc.ExecuteCalculatorCodeUnique("(>K:RUDDER_TRIM_LEFT)"); return true;
            case "C680_TRIM_RUD_R": sc.ExecuteCalculatorCodeUnique("(>K:RUDDER_TRIM_RIGHT)"); return true;
            case "C680_GEAR":
                if (value < 0.5 && (!Has("C680_ON_GROUND") || Live("C680_ON_GROUND") > 0.5))
                {
                    announcer.AnnounceImmediate("Refused. The gear stays down on the ground.");
                    sc.RequestVariable("C680_GEAR", forceUpdate: true);
                    return true;
                }
                sc.SetLVar("SW_SOV_LANDING_GEAR_LEVER", value);
                return true;
            case "C680_EMER_BRAKE": Pulse(sc, "SW_SOV_HANDLING_SW_SOV_EMER_BRAKE", 1500); return true;
            case "C680_IDENT": sc.ExecuteCalculatorCodeUnique("(>K:XPNDR_IDENT_TOGGLE)"); return true;
            case "C680_MIC_L": Pulse(sc, "INSTRUMENT_Push_Microphone_1", 300); return true;
            case "C680_MIC_R": Pulse(sc, "INSTRUMENT_Push_Microphone_2", 300); return true;
        }
        return false;
    }
}
