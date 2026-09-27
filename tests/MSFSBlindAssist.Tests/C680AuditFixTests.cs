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
}
