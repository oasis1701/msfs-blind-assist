using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the six PMDG 777 outflow-valve rows: three roles per valve, each row
/// distinctly named, and the manual selector's detent words taken from the SDK
/// header rather than guessed.
///
/// PMDG_777X_SDK.h:
///   AIR_OutflowValve_Sw_AUTO[2];        // fwd / aft
///   AIR_OutflowValveManual_Selector[2]; // fwd / aft   0: OPEN  1: Neutral  2: CLOSE
///   AIR_annunOutflowValve_MAN[2];       // fwd / aft
///
/// The middle detent is spring-loaded NEUTRAL. It was labelled "Auto" until
/// 2026-09, which collided semantically with the Mode row's genuine "Auto" -
/// the audible half of the collision PR #223 set out to fix.
/// </summary>
public class Pmdg777OutflowValveLabelTests
{
    [Theory]
    [InlineData("AIR_OutflowValveFwd", "AIR_OutflowValveManual_Selector_0", "Forward Outflow Valve Manual Selector")]
    [InlineData("AIR_OutflowValveAft", "AIR_OutflowValveManual_Selector_1", "Aft Outflow Valve Manual Selector")]
    [InlineData("AIR_OutflowValve_Fwd", "AIR_OutflowValve_Sw_AUTO_0", "Forward Outflow Valve Mode")]
    [InlineData("AIR_OutflowValve_Aft", "AIR_OutflowValve_Sw_AUTO_1", "Aft Outflow Valve Mode")]
    [InlineData("AIR_annunOutflowValveMAN_1", "AIR_annunOutflowValve_MAN_0", "Forward Outflow Valve MAN Light")]
    [InlineData("AIR_annunOutflowValveMAN_2", "AIR_annunOutflowValve_MAN_1", "Aft Outflow Valve MAN Light")]
    public void Outflow_row_pins_its_struct_field_and_spoken_label(
        string varKey, string structField, string label)
    {
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(varKey), $"missing outflow var {varKey}");
        Assert.Equal(structField, vars[varKey].Name);
        PmdgStructFields.AssertResolves777(structField, varKey);
        Assert.Equal(label, vars[varKey].DisplayName);
    }

    [Theory]
    [InlineData("AIR_OutflowValveFwd")]
    [InlineData("AIR_OutflowValveAft")]
    public void Manual_selector_middle_detent_is_neutral_not_auto(string varKey)
    {
        var vars = new PMDG777Definition().GetVariables();
        var d = vars[varKey].ValueDescriptions;

        // SDK: 0: OPEN  1: Neutral  2: CLOSE
        Assert.Equal("Open", d[0]);
        Assert.Equal("Neutral", d[1]);
        Assert.Equal("Close", d[2]);

        // "Auto" belongs to the Mode row alone. If the manual selector says it
        // too, the pilot hears the same word for opposite states on adjacent rows.
        Assert.DoesNotContain("Auto", d.Values);
    }
}
