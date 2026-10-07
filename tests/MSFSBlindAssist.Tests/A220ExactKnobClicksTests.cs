using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The speed/heading walk steps one unit per click, so a burst must never be larger
/// than the gap (no overshoot) and never a runaway (live 2026-09-24: a bad read-back
/// fired the old 220-click cap in alternating directions, 210 kt entered → 80 kt).
/// </summary>
public class A220ExactKnobClicksTests
{
    [Theory]
    [InlineData(68, 68)]
    [InlineData(-68, 68)]
    [InlineData(0.4, 0)]
    [InlineData(0, 0)]
    [InlineData(-303, 120)]
    public void ClicksAreTheGapCappedPerBurst(double delta, int expected)
        => Assert.Equal(expected, A220Afdx.ExactKnobClicks(delta));
}

public class A220KnobPaceTests
{
    [Theory]
    [InlineData(25, 77, 49, 43)]   // live speed burst: 64% landed -> slow down
    [InlineData(25, 58, 21, 76)]   // live heading burst: 36% landed
    [InlineData(40, 30, 30, 40)]   // all landed -> keep
    [InlineData(25, 2, 0, 25)]     // too small to judge
    [InlineData(100, 50, 5, 120)]  // capped
    public void PaceFollowsDelivery(int pace, int clicks, double moved, int expected)
        => Assert.Equal(expected, A220Afdx.NextKnobPaceMs(pace, clicks, moved));
}
