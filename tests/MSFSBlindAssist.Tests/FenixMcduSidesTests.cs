// Characterization tests for FenixMcduSides: the Fenix data-service names of the two MCDU screens
// and the key switches. One subscription carries both screens, and each "next" message names the
// screen it carries, so the name is how a screen finds its side.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixMcduSidesTests
{
    [Theory]
    [InlineData("aircraft.mcdu1.display", 1)]
    [InlineData("aircraft.mcdu2.display", 2)]
    public void Each_screen_name_maps_to_its_side(string name, int expected)
    {
        Assert.True(FenixMcduSides.TryGetSide(name, out int side));
        Assert.Equal(expected, side);
    }

    [Theory]
    [InlineData("aircraft.mcdu3.display")]
    [InlineData("aircraft.fms.flightPlanXml")]
    [InlineData("AIRCRAFT.MCDU1.DISPLAY")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_rejected(string? name)
    {
        Assert.False(FenixMcduSides.TryGetSide(name, out int side));
        Assert.Equal(0, side);
    }

    [Fact]
    public void The_subscription_asks_for_both_screens_and_nothing_else()
        => Assert.Equal(new[] { "aircraft.mcdu1.display", "aircraft.mcdu2.display" }, FenixMcduSides.SubscribedRefs);

    [Fact]
    public void DisplayRef_round_trips_through_TryGetSide()
    {
        foreach (int side in new[] { FenixMcduSides.Left, FenixMcduSides.Right })
        {
            Assert.True(FenixMcduSides.TryGetSide(FenixMcduSides.DisplayRef(side), out int back));
            Assert.Equal(side, back);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void DisplayRef_and_KeyRef_reject_a_side_the_Fenix_does_not_have(int side)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FenixMcduSides.DisplayRef(side));
        Assert.Throws<ArgumentOutOfRangeException>(() => FenixMcduSides.KeyRef(side, "A"));
    }
}
