using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A CONTROL THAT TAKES A NUMBER MUST HAVE "_SET" IN ITS KEY, OR IT SILENTLY BECOMES A
/// BARE BUTTON AND THE NUMBER CAN NEVER BE TYPED.
///
/// MainForm.PanelBuilder picks the widget in this order: RenderAsButton -> button;
/// RenderAsReadOnlyStatus + units -> read-only box; two or more ValueDescriptions -> combo;
/// key CONTAINS "_SET" -> text box plus a Set button. A control matching none of those falls
/// through to a plain button, which fires the setter with no value the pilot chose.
///
/// It is a naming convention doing load-bearing work, which is exactly the kind of rule that
/// gets broken silently: the fuel loads were written as typed gallon transactions - the
/// setter reads `value` and refuels to it - but were keyed "..._LOAD", so they rendered as
/// buttons and the gallons could not be entered at all. Reported from the cockpit as "why
/// are there buttons instead of just setters where you insert the value you want".
/// </summary>
public class CowsDA40ControlRenderingTests
{
    private static IEnumerable<(string Key, MSFSBlindAssist.SimConnect.SimVarDefinition Def)>
        ControlsOf(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();
        foreach (var panel in def.GetPanelControls())
            foreach (string key in panel.Value)
                if (vars.TryGetValue(key, out var d))
                    yield return (key, d);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPanelControlRendersAsSomethingAPilotCanOperate(DA40Variant variant)
    {
        var broken = new List<string>();

        foreach (var (key, d) in ControlsOf(variant))
        {
            bool button = d.RenderAsButton;
            bool readOnly = d.RenderAsReadOnlyStatus && !string.IsNullOrEmpty(d.Units);
            bool combo = d.ValueDescriptions != null && d.ValueDescriptions.Count >= 2;
            bool typed = key.Contains("_SET");

            if (!button && !readOnly && !combo && !typed)
                broken.Add(key);
        }

        Assert.True(broken.Count == 0,
            "These panel controls match no widget rule, so MainForm falls through to a bare " +
            "button and the value can never be entered: " + string.Join(", ", broken.Distinct()));
    }
}
