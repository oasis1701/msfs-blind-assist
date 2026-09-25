using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Brakes. Both variants.
///
/// ONE control. The wheel brakes themselves are toe pedals — a flight control, flown from
/// the pilot's own hardware or keyboard, and no more a panel item than the stick is. What
/// the panel owns is the parking brake lever and, far more importantly, the things the
/// aeroplane gives a sighted pilot through their feet and nose and gives a blind pilot
/// through nothing at all.
///
/// THE BRAKES REALLY DO FADE, and this is why the panel exists. The model integrates a
/// temperature from braking energy and cools it over time, then:
///
///     if temp > 400:  fade = max(0.1, 1 - (temp - 400) / 400)
///     else:           fade = 1
///
/// so braking authority starts dropping at 400 °C and bottoms out at 760 °C having lost
/// NINETY PERCENT of it. That is the AFM's caution made real — "prolonged permanent
/// braking while taxiing will overheat the brakes and may cause loss of brake capacity
/// and subsequent damage to the airplane" — and the aeroplane has no brake temperature
/// gauge, so a sighted pilot learns it from smell, feel and the aircraft not slowing.
/// A blind pilot has none of those, and would find out on the landing rollout. Both
/// wheels are reported separately because they heat separately.
///
/// THE PARKING BRAKE IS SIMPLER THAN THE BOOK. AFM 7.5.2 has the pilot pull the lever and
/// then PUMP the toe pedals to build pressure. COWS does not model the pumping: setting
/// INPUT_PARK took BRAKE_PRESS from 0 to 100 in one step, measured live, and clearing it
/// took it back to 0. So one toggle is the whole control, and inventing a pump button
/// would be adding a step the simulation does not have.
/// </summary>
public partial class CowsDA40Definition
{
    private const string BrakesPanel = "Brakes";

    /// <summary>Where the model starts taking braking authority away.</summary>
    private const double BrakeFadeOnsetC = 400.0;

    /// <summary>Where the model's fade multiplier reaches its 0.1 floor.</summary>
    private const double BrakeFadeFloorC = 760.0;

    private static Dictionary<string, SimVarDefinition> BuildBrakeVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        v["DA40_BRAKE_PARK"] = new SimVarDefinition
        {
            Name = "INPUT_PARK",
            DisplayName = "Parking Brake",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Released",
                [1] = "Set"
            }
        };

        // ---------- Status ----------

        AddReadout(v, "DA40_BRAKE_PRESS_L", "BRAKE_PRESS:1", "Park Pressure Left", "percent", "F0");
        AddReadout(v, "DA40_BRAKE_PRESS_R", "BRAKE_PRESS:2", "Park Pressure Right", "percent", "F0");

        // Temperature and what it has cost. Neither is a cockpit gauge on this aeroplane
        // — see the class comment for why they are here anyway.
        AddReadout(v, "DA40_BRAKE_TEMP_L", "BRAKE_TEMP:1", "Brake Temperature Left", "celsius", "F0");
        AddReadout(v, "DA40_BRAKE_TEMP_R", "BRAKE_TEMP:2", "Brake Temperature Right", "celsius", "F0");

        // NAMED "effectiveness" and always rendered as a PERCENTAGE. The first labelling
        // was "Braking Left: full", which reads as "the left brake is fully applied" -
        // the opposite kind of fact from the one being reported. A percentage cannot be
        // misread that way.
        AddBrakeFade(v, "DA40_BRAKE_FADE_L", "BRAKE_FADE:1", "Brake Effectiveness Left");
        AddBrakeFade(v, "DA40_BRAKE_FADE_R", "BRAKE_FADE:2", "Brake Effectiveness Right");

        // How hard the pedals are actually being pressed, per side. A stuck or dragging
        // pedal is what heats a brake for no reason, and it is invisible otherwise.
        AddPedal(v, "DA40_BRAKE_PEDAL_L", "BRAKE LEFT POSITION", "Left Pedal");
        AddPedal(v, "DA40_BRAKE_PEDAL_R", "BRAKE RIGHT POSITION", "Right Pedal");

        return v;
    }

    private static void AddBrakeFade(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F2"
        };
    }

    private static void AddPedal(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };
    }

    private static readonly List<string> BrakeControls = new()
    {
        "DA40_BRAKE_PARK"
    };

    private static readonly List<string> BrakeDisplay = new()
    {
        "DA40_BRAKE_PRESS_L",
        "DA40_BRAKE_PRESS_R",
        "DA40_BRAKE_TEMP_L",
        "DA40_BRAKE_TEMP_R",
        "DA40_BRAKE_FADE_L",
        "DA40_BRAKE_FADE_R",
        "DA40_BRAKE_PEDAL_L",
        "DA40_BRAKE_PEDAL_R"
    };

    private bool HandleBrakeSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_BRAKE_PARK") return false;

        simConnect.SetLVar("INPUT_PARK", value >= 0.5 ? 1 : 0);
        return true;
    }

    /// <summary>
    /// Brake temperature and fade in words. A multiplier is not a reading a pilot can act
    /// on; "half your braking is gone" is.
    /// </summary>
    private bool TryGetBrakeDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";

        switch (varKey)
        {
            case "DA40_BRAKE_TEMP_L":
            case "DA40_BRAKE_TEMP_R":
                displayText = value >= BrakeFadeFloorC
                    ? $"{value:0} celsius, RED — braking down to a tenth"
                    : value >= BrakeFadeOnsetC
                        ? $"{value:0} celsius, HOT — losing braking"
                        : $"{value:0} celsius, cool";
                return true;

            case "DA40_BRAKE_FADE_L":
            case "DA40_BRAKE_FADE_R":
                displayText = value >= 0.995
                    ? "100 percent"
                    : $"{value * 100:0} percent, FADED";
                return true;
        }

        return false;
    }
}
