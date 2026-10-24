using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>The knobs' push and pull words reach the app through the generated map's "action" field.</summary>
public class A300ControlMapActionTests
{
    [Theory]
    [InlineData("A300_HEADING_KNOB_PUSH", "AIRCRAFT HEADING")]
    [InlineData("A300_HEADING_KNOB_PULL", "HEADING MODE")]
    [InlineData("A300_ALT_KNOB_PUSH", "SWITCH 100/1000")]
    [InlineData("A300_VS_KNOB_PULL", "ENGAGE")]
    [InlineData("A300_CPT_ALTIMETER_KNOB_PULL", "SET STD PRESSURE")]
    [InlineData("A300_FO_ALTIMETER_KNOB_PUSH", "SET QNH PRESSURE")]
    public void The_shipped_map_carries_the_knobs_own_push_and_pull_words(string key, string action) =>
        Assert.Equal(action, A300ControlMap.Load().FindByKey(key)!.Action);

    [Fact]
    public void A_control_with_no_push_or_pull_words_has_no_action() =>
        Assert.Null(A300ControlMap.Load().FindByKey("A300_STORMLIGHT")!.Action);
}
