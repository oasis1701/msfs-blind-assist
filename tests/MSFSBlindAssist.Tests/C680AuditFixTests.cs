using MSFSBlindAssist.Aircraft.Citation680;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Panel rows the 2026-09-27 audit found reading the wrong variable, or reading a variable
/// that is not a control at all, pinned against the definition.
/// </summary>
public class C680AuditFixTests
{
    private static readonly Dictionary<string, SimVarDefinition> Vars = new SkywardC680Definition().GetVariables();

    [Fact]
    public void TailFloodIsTheVariableThePluginCallsTailFlood()
        => Assert.Equal("SW_SOV_LIGHTS_LOGO", Vars["C680_TAIL_FLOOD"].Name);

    [Theory]
    [InlineData("SW_SOV_LIGHTS_FLOOD_ON")]       // write does not stick; nothing reads it
    [InlineData("SW_SOV_GARMIN_AMBIENT_LIGHT")]  // a computed ambient level, not a switch
    public void DeadOrComputedVariablesAreNotControls(string name)
        => Assert.DoesNotContain(Vars.Values, d => d.Name == name && !d.RenderAsReadOnlyStatus);

    [Fact]
    public void NoSecondRowForTheTailFloodSwitch()
        => Assert.False(Vars.ContainsKey("C680_LOGO"));

    [Theory]
    [InlineData("C680_NAV_LT", "LIGHT NAV")]
    [InlineData("C680_BEACON", "LIGHT BEACON")]
    public void NavAndBeaconAreReadOnlyTheyAreSetOnTheTouchscreen(string key, string simvar)
    {
        Assert.Equal(simvar, Vars[key].Name);
        Assert.True(Vars[key].RenderAsReadOnlyStatus);
    }

    [Fact]
    public void MfdTouchscreenBacklightDrivesBothMfdTouchscreens()
        => Assert.Equal("40 (>L:WTG3000_Gtc_Backlight:2) 40 (>L:WTG3000_Gtc_Backlight:3)",
                        C680Commands.For("C680_KNOB_GTC_MFD", 40).Single().Code);

    [Theory]
    [InlineData("C680_AI_BOOST_L")]
    [InlineData("C680_AI_BOOST_R")]
    public void N1BoostIsAnOnOffFlag(string key)
    {
        Assert.NotNull(Vars[key].ValueDescriptions);
        Assert.Equal("On", Vars[key].ValueDescriptions![1]);
    }

    // ---- Right tilt (Task 5)

    [Fact]
    public void PressureSourceUsesTheModelsEncoding()
        => Assert.Equal(new[] { "EMER", "L", "NORM", "R", "OFF" }, Vars["C680_PRESS_SRC"].ValueDescriptions!.OrderBy(p => p.Key).Select(p => p.Value));

    [Theory]
    [InlineData("C680_BLEED_L")]
    [InlineData("C680_BLEED_R")]
    public void BleedKnobsUseThePluginsWords(string key)
        => Assert.Equal(new[] { "OFF", "LP", "NORM", "HP" }, Vars[key].ValueDescriptions!.OrderBy(p => p.Key).Select(p => p.Value));

    [Fact]
    public void PassengerOxygenUsesTheModelsEncoding()
        => Assert.Equal(new[] { "OFF", "NORM", "ON" }, Vars["C680_PASS_OXY"].ValueDescriptions!.OrderBy(p => p.Key).Select(p => p.Value));

    [Theory]
    [InlineData(0, "0 (>L:Mask_Selector_Position) 0 (>L:Oxy_Flow) 0 (>L:Oxy_Flow_Force)")]
    [InlineData(1, "1 (>L:Mask_Selector_Position) 1 (>L:Oxy_Flow) 0 (>L:Oxy_Flow_Force)")]
    [InlineData(2, "2 (>L:Mask_Selector_Position) 0 (>L:Oxy_Flow) 1 (>L:Oxy_Flow_Force)")]
    public void PassengerOxygenSetsTheFlowAsTheKnobDoes(double position, string code)
        => Assert.Equal(code, C680Commands.For("C680_PASS_OXY", position).Single().Code);

    [Fact]
    public void DumpNeedsItsCoverOpenAsInTheCockpit()
    {
        Assert.Equal("(B:PRESSURIZATION_Dump_Cover) if{ 1 (>B:PRESSURIZATION_Dump_Set) }", C680Commands.For("C680_PRESS_DUMP", 1).Single().Code);
        Assert.Equal("1 (>B:PRESSURIZATION_Dump_Cover_Set)", C680Commands.For("C680_PRESS_DUMP_COVER", 1).Single().Code);
        Assert.Equal("PRESSURIZATION DUMP SWITCH", Vars["C680_PRESS_DUMP"].Name);
        Assert.Equal(C680SwitchMirror.DumpCover, Vars["C680_PRESS_DUMP_COVER"].Name);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void HydraulicSwitchesNeedTheirCoverOpen(int n)
    {
        Assert.Equal($"(L:HYDRAULICS_Switch_{n}_Cover) 1 == if{{ 1 (>L:SW_SOV_HYDRAULICS_Switch_{n}) }}",
                     C680Commands.For($"C680_HYD_SW_{n}", 1).Single().Code);
        Assert.Equal($"HYDRAULICS_Switch_{n}_Cover", Vars[$"C680_HYD_SW_{n}_COVER"].Name);
    }

    [Theory]
    [InlineData("C680_CKPT_TMP_UP")]
    [InlineData("C680_CKPT_TMP_DN")]
    [InlineData("C680_CABIN_TMP_UP")]
    [InlineData("C680_CABIN_TMP_DN")]
    [InlineData("C680_CABIN_CONTROL")]
    public void DeadTemperatureControlsAreGoneTheTouchscreenOwnsThem(string key)
        => Assert.False(Vars.ContainsKey(key));
}
