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
/// Driven by TOGGLE_ELT rather than ELT_SET, and that is measured rather than preferred:
/// `2 (>K:ELT_SET)` left ELT ACTIVATED at 0 while `1 (>K:TOGGLE_ELT)` moved it to 2.
/// ELT_SET does work for 0, so only the toggle is dependable in both directions — which
/// means comparing against the current state before firing, exactly like the fuel pumps.
/// </summary>
public partial class CowsDA40Definition
{
    private const string EltPanel = "ELT";

    /// <summary>The value ELT ACTIVATED takes when the beacon is transmitting.</summary>
    private const double EltOnValue = 2.0;

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

        double current = simConnect.GetCachedVariableValue("DA40_ELT") ?? 0;
        bool wantOn = value >= 1;

        if (wantOn != current >= 1)
        {
            simConnect.ExecuteCalculatorCode("1 (>K:TOGGLE_ELT)");
        }

        return true;
    }
}
