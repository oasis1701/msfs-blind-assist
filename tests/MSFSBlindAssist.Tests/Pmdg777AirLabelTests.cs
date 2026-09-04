using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the PMDG 777 bleed-air isolation valves. Three rows share the phrase
/// "Isolation Valve", so the discriminator leads; and each CLOSED light carries
/// its switch's name verbatim, because the two are spoken through different
/// channels (the switch from the panel, the light as a background announcement)
/// and a drift between them surfaces as a light for a valve the panel appears
/// not to have. That drift is exactly what this family had until 2026-09.
/// </summary>
public class Pmdg777AirLabelTests
{
    private const string ClosedSuffix = " CLOSED Light";

    /// <summary>varKey, PMDG struct field, spoken label, CLOSED light varKey, CLOSED light struct field.</summary>
    public static TheoryData<string, string, string, string, string> Valves() => new()
    {
        { "AIR_IsolationValve_L",   "AIR_IsolationValve_Sw_0",  "Left Isolation Valve",   "AIR_annunIsolationValveCLOSED_L",  "AIR_annunIsolationValveCLOSED_0" },
        { "AIR_IsolationValve_R",   "AIR_IsolationValve_Sw_1",  "Right Isolation Valve",  "AIR_annunIsolationValveCLOSED_R",  "AIR_annunIsolationValveCLOSED_1" },
        { "AIR_CtrIsolationValve",  "AIR_CtrIsolationValve_Sw", "Center Isolation Valve", "AIR_annunCtrIsolationValveCLOSED", "AIR_annunCtrIsolationValveCLOSED" },
    };

    [Theory]
    [MemberData(nameof(Valves))]
    public void Isolation_valve_pins_its_struct_field_and_spoken_label(
        string varKey, string structField, string label, string lightKey, string lightField)
    {
        _ = lightKey;
        _ = lightField;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(varKey), $"missing isolation valve var {varKey}");
        Assert.Equal(structField, vars[varKey].Name);
        PmdgStructFields.AssertResolves777(structField, varKey);
        Assert.Equal(label, vars[varKey].DisplayName);
    }

    /// <summary>
    /// The light's own array slot as well as its label: without the slot pin, binding a
    /// CLOSED light to the wrong index leaves every label assertion green while "Left
    /// Isolation Valve CLOSED Light" reports the RIGHT valve.
    /// </summary>
    [Theory]
    [MemberData(nameof(Valves))]
    public void Closed_light_label_is_derived_from_its_valve_label(
        string varKey, string structField, string label, string lightKey, string lightField)
    {
        _ = structField;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(lightKey), $"missing CLOSED light var {lightKey}");
        Assert.Equal(lightField, vars[lightKey].Name);
        PmdgStructFields.AssertResolves777(lightField, lightKey);
        Assert.Equal(label + ClosedSuffix, vars[lightKey].DisplayName);
        Assert.Equal(vars[varKey].DisplayName + ClosedSuffix, vars[lightKey].DisplayName);
    }
}
