using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// No DA40 readout may SPEAK its registration unit.
///
/// ⚠️ "ECU BATTERY CAPACITY: 149.1 NUMBER". An L:var must be registered Units = "number" -
/// it holds a raw value, and any other unit makes SimConnect convert from a base unit it
/// does not have (the standby subscale read ZERO for exactly that reason). But that same
/// string is what the generic renderer appends, so twelve panel rows read their unit aloud
/// as the word "number": all eight Engine Damage accumulators, the ECU battery capacity and
/// its surface depletion, both ECU fault timers, the auto-start step and the trim axis.
///
/// The intent had been right all along - the definitions' own file says these carry no unit
/// deliberately, "a bare number a pilot can watch fall is honest; a wrong unit is not" - and
/// only the rendering leaked.
/// </summary>
public class CowsDA40RawUnitTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoNumericReadoutSpeaksTheWordNumber(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        // ⚠️ READOUTS ONLY. A panel CONTROL renders as an edit box or a combo and its unit
        // lives in the label and the range text, not after the value - DA40_G1000_BARO_SET
        // is a write-only proxy that reads nothing at all. This is about the status rows a
        // pilot scans, which is where the word was being spoken.
        var shown = def.GetPanelDisplayVariables()
                       .SelectMany(p => p.Value)
                       .Distinct();

        foreach (string key in shown)
        {
            if (!vars.TryGetValue(key, out var d)) continue;
            if (d.Units != "number") continue;
            if (d.ValueDescriptions.Count > 0) continue;   // reads a state name, not a unit
            if (d.RenderAsButton) continue;                 // a button has no value to speak

            Assert.True(def.TryGetDisplayOverride(key, 1.0, out string text),
                $"{key} ({d.DisplayName}) is registered Units=\"number\" and has no display " +
                "override, so the panel speaks the word \"number\" after its value.");
            Assert.DoesNotContain("number", text, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
