using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → ELT. Both variants.
///
/// THIS PANEL EXISTS BECAUSE AN EARLIER AUDIT GOT IT WRONG. That audit concluded the ELT
/// (AFM 7.4 legend item 21) had "no interactive component in the model, so there is
/// nothing to expose". It does: `Component ID="SAFETY"` uses
/// ASOBO_SAFETY_Switch_ELT_Template with TYPE ARM_ON — the component is simply named after
/// the system it belongs to rather than after the switch, which is how it was missed.
///
/// The shutdown checklist item is "ELT ... check not transmitting on 121.5 MHz", so the
/// state is the whole point: ARMED is the normal position and ON means it is transmitting.
///
/// ⚠️ RE-MEASURED 2026-10-05 (MSFS 2024, COWS 1.2.0), AND THE EARLIER NOTE WAS WRONG BY
/// NOW. It said `1 (>K:TOGGLE_ELT)` moved ELT ACTIVATED to 2 and ELT_SET could not reach
/// it, so the control toggled and called 2 "ON". Today TOGGLE_ELT does nothing at all,
/// and the cockpit switch itself - its input event SAFETY_ELT_1 - has exactly two values:
/// 0 ARM and 1 ON (driving it to 2 leaves 1). ELT_SET reaches 0, 1 and 2 directly, so the
/// control writes the switch's own two values with it, absolute, and needs no compare.
/// The row reads anything at 1 or above as transmitting.
/// </summary>
public partial class CowsDA40Definition
{
    private const string EltPanel = "ELT";

    /// <summary>The value the cockpit switch gives ELT ACTIVATED at ON (SAFETY_ELT_1 = 1).</summary>
    private const double EltOnValue = 1.0;

    private static Dictionary<string, SimVarDefinition> BuildEltVariables() => new()
    {
        ["DA40_ELT"] = new SimVarDefinition
        {
            Name = "ELT ACTIVATED",
            DisplayName = "Emergency Locator Transmitter",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Armed",
                [EltOnValue] = "ON — transmitting"
            }
        }
    };

    private static readonly List<string> EltControls = new() { "DA40_ELT" };

    private bool HandleEltSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_ELT") return false;

        simConnect.ExecuteCalculatorCodeUnique(
            FormattableString.Invariant($"{(value >= 1 ? EltOnValue : 0):0} (>K:ELT_SET)"));
        return true;
    }
}
