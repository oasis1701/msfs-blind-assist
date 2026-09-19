using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Cabin → Doors and Windows. Both variants.
///
/// Four openings: the front canopy, the rear door, and the two storm windows. Each is a
/// stock exit toggled with K:TOGGLE_AIRCRAFT_EXIT, and its position reads back from
/// INTERACTIVE POINT OPEN — but the two indices are OFF BY ONE, which is the trap here.
/// The event index is one higher than the SimVar index, verified live in both directions:
/// event 3 opened INTERACTIVE POINT OPEN:2 to 100 %, event 7 opened :6.
///
///     canopy  event 3 -> :2      storm window left   event 7 -> :6
///     rear    event 4 -> :3      storm window right  event 8 -> :7
///
/// THE AEROPLANE REFUSES ABOVE 30 KNOTS, and does it twice over. The click code is gated
/// on `(A:RELATIVE WIND VELOCITY BODY Z, Knots) abs 30 <=`, so a door will not open into
/// a slipstream; and a 1 Hz Update SLAMS a fully-open canopy or rear door shut the moment
/// relative wind reaches 30. A blind pilot pressing the control and hearing nothing change
/// would have no idea which of those had happened, so the relative wind is on the scan and
/// the refusal is spoken rather than silent.
///
/// The doors report a PERCENTAGE, not a flag - they travel - so mid-travel reads as
/// "Opening" rather than rounding to one end or the other.
/// </summary>
public partial class CowsDA40Definition
{
    private const string DoorsPanel = "Doors and Windows";

    /// <summary>
    /// The model's own threshold, in both the click gate and the auto-close Update.
    /// </summary>
    private const double DoorWindLimitKts = 30.0;

    // Control key -> the K:TOGGLE_AIRCRAFT_EXIT index. The SimVar index is one LOWER, and
    // that difference is the whole reason this table exists rather than a single number.
    private static readonly Dictionary<string, int> DoorExitIndex = new()
    {
        ["DA40_DOOR_CANOPY"] = 3,
        ["DA40_DOOR_REAR"] = 4,
        ["DA40_DOOR_STORM_L"] = 7,
        ["DA40_DOOR_STORM_R"] = 8
    };

    private static Dictionary<string, SimVarDefinition> BuildDoorVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddDoor(v, "DA40_DOOR_CANOPY", 2, "Canopy");
        AddDoor(v, "DA40_DOOR_REAR", 3, "Rear Canopy");
        AddDoor(v, "DA40_DOOR_STORM_L", 6, "Left Window");
        AddDoor(v, "DA40_DOOR_STORM_R", 7, "Right Window");

        // ---------- Status ----------

        // Where each opening ACTUALLY is. The control is a two-state combo, so it can only
        // ever say Closed or Open; a door that is travelling is neither, and rounding it
        // to an end would be a lie at exactly the moment the pilot is listening. These are
        // OnRequest deliberately: two CONTINUOUS variables sharing one SimVar name would
        // collide in the continuous batch and shift every later variable's slot.
        AddDoorPosition(v, "DA40_DOOR_CANOPY_POS", 2, "Canopy");
        AddDoorPosition(v, "DA40_DOOR_REAR_POS", 3, "Rear Canopy");
        AddDoorPosition(v, "DA40_DOOR_STORM_L_POS", 6, "Left Window");
        AddDoorPosition(v, "DA40_DOOR_STORM_R_POS", 7, "Right Window");

        // Why a door will not open, and why an open one just shut itself.
        v["DA40_DOOR_WIND"] = new SimVarDefinition
        {
            Name = "RELATIVE WIND VELOCITY BODY Z",
            DisplayName = "Relative Wind",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        return v;
    }

    /// <summary>
    /// A door is a PERCENTAGE that travels, so it is bound to its own position rather than
    /// to a synthetic flag: the value is the truth and mid-travel has somewhere to live.
    /// </summary>
    private static void AddDoor(Dictionary<string, SimVarDefinition> v, string key,
        int simvarIndex, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = $"INTERACTIVE POINT OPEN:{simvarIndex}",
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Closed",
                [100] = "Open"
            }
        };
    }

    private static readonly List<string> DoorControls = new()
    {
        "DA40_DOOR_CANOPY",
        "DA40_DOOR_REAR",
        "DA40_DOOR_STORM_L",
        "DA40_DOOR_STORM_R"
    };

    private static void AddDoorPosition(Dictionary<string, SimVarDefinition> v, string key,
        int simvarIndex, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = $"INTERACTIVE POINT OPEN:{simvarIndex}",
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };
    }

    private static readonly List<string> DoorDisplay = new()
    {
        "DA40_DOOR_CANOPY_POS",
        "DA40_DOOR_REAR_POS",
        "DA40_DOOR_STORM_L_POS",
        "DA40_DOOR_STORM_R_POS",
        "DA40_DOOR_WIND"
    };

    private bool HandleDoorSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (!DoorExitIndex.TryGetValue(varKey, out int exit)) return false;

        double wind = Math.Abs(simConnect.GetCachedVariableValue("DA40_DOOR_WIND") ?? 0);
        bool wantOpen = value >= 50;

        // The aeroplane refuses above 30 knots. Say so rather than letting the control
        // appear to do nothing - a refusal and a broken button sound identical otherwise.
        if (wantOpen && wind > DoorWindLimitKts)
        {
            announcer.AnnounceImmediate(
                $"Will not open at {wind:0} knots. The limit is {DoorWindLimitKts:0}.");
            return true;
        }

        // FIRE THE TOGGLE UNCONDITIONALLY. A two-state combo only reaches this method when
        // the SELECTION CHANGED, so the pilot has by definition asked for the state the
        // door is not in, and a toggle is always the right action.
        //
        // The first version compared the request against the door's cached position first,
        // and that is exactly what broke closing: every door opened and none would shut.
        // A comparison here can only ever be wrong in one of two ways - the cache misses
        // and the door looks closed when it is open, or the position is mid-travel and
        // neither branch describes it - and both failures are silent.
        // ⚠️ UNIQUE, NOT PLAIN, AND THIS IS WHY CLOSING FAILED. Opening a door and then
        // closing it sends the SAME calculator string twice running - "2 (>K:TOGGLE_
        // AIRCRAFT_EXIT)" both times - and MobiFlight coalesces byte-identical consecutive
        // commands, so the second was dropped and the door stayed open. Reported from the
        // cockpit exactly as it behaves: the canopy would not shut, and doing the REAR door
        // in between made it work, because that broke the identical pair.
        //
        // This codebase's own invariant already said every valueless calc write goes
        // through the unique form. This call was the exception that proved it, again - the
        // same trap as the squawk keypad, the softkeys and the wiper switch.
        simConnect.ExecuteCalculatorCodeUnique($"{exit} (>K:TOGGLE_AIRCRAFT_EXIT)");
        return true;
    }

    /// <summary>
    /// Mid-travel needs a word: a door at 45 % is neither open nor closed. And the wind
    /// needs a MEANING - RELATIVE WIND VELOCITY BODY Z is signed, so it reads "-4" sitting
    /// still, which is both alarming and useless. The model compares its ABSOLUTE value
    /// against 30, so that is what is reported, with what it implies for the doors.
    /// </summary>
    private bool TryGetDoorDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";

        // The CONTROL as well as the position readout. Both carry a percentage and neither
        // should ever say one: a pilot asked for open or closed and that is the whole of
        // what a door has to tell them.
        if (DoorAnnounceKeys.Contains(varKey))
        {
            displayText = value > 0.5 ? "Open" : "Closed";
            return true;
        }

        if (varKey.StartsWith("DA40_DOOR_") && varKey.EndsWith("_POS"))
        {
            displayText = value <= 0.5 ? "Closed"
                : value >= 99.5 ? "Open"
                : $"Moving, {value:0} percent";
            return true;
        }

        if (varKey == "DA40_DOOR_WIND")
        {
            double knots = Math.Abs(value);
            displayText = knots > DoorWindLimitKts
                ? $"{knots:0} knots — too fast to open a door"
                : $"{knots:0} knots — doors free";
            return true;
        }

        return false;
    }
}
