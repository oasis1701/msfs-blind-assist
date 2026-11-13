using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The XLS's "red box" - the mixture region that damages a cylinder - transcribed from the
/// model (Logic, "Whos there?"): it exists only while a cylinder's heat output is at or
/// above 42 kW AND its air/fuel ratio sits in 12.9-15.5. Then FAC = (kW - 42) / 8, the
/// rich edge is 14.7 - 1.8 FAC, the lean edge 14.7 + 0.8 FAC, and "how far inside" is the
/// distance past whichever edge is nearer, scaled to that edge's width. Damage accrues at
/// 0.05 per tick times that depth. ⚠️ DAMAGE_REDBOX_ITS:n is NOT zeroed when the box
/// closes (measured 19.7 with the engine stopped), so the state is recomputed from the
/// inputs here, never read from that variable.
/// </summary>
public class DA40RedBoxTests
{
    [Fact]
    public void RunUpPowerIsBelowTheBoxWhateverTheMixture()
        // Measured at 2211 rpm: 30.8 kW per cylinder.
        => Assert.False(DA40RedBox.IsInside(heatKw: 30.8, airFuel: 13.5));

    [Fact]
    public void AtFortyTwoKilowattsTheBoxIsAPointAtStoichiometric()
    {
        // FAC 0: both edges at 14.7, so only exactly 14.7 is "inside" - and the depth is 0.
        Assert.False(DA40RedBox.IsInside(42, 14.0));
        Assert.False(DA40RedBox.IsInside(42, 15.2));
    }

    [Fact]
    public void AtFiftyKilowattsTheBoxSpansTheRichAndLeanEdges()
    {
        // FAC 1: rich edge 12.9, lean edge 15.5.
        Assert.Equal(12.9, DA40RedBox.RichEdge(50), 3);
        Assert.Equal(15.5, DA40RedBox.LeanEdge(50), 3);
        Assert.True(DA40RedBox.IsInside(50, 13.5));
        Assert.True(DA40RedBox.IsInside(50, 15.0));
        Assert.False(DA40RedBox.IsInside(50, 12.5));   // richer than the rich edge
        Assert.False(DA40RedBox.IsInside(50, 16.0));   // leaner than the lean edge
    }

    [Fact]
    public void DepthIsMeasuredFromTheNearerEdge()
    {
        // 13.5 at 50 kW: rich of 14.7, so (13.5 - 12.9) / 1.8.
        Assert.Equal(0.333, DA40RedBox.Depth(50, 13.5), 3);
        // 15.0 at 50 kW: lean of 14.7, so (15.5 - 15.0) / 0.8.
        Assert.Equal(0.625, DA40RedBox.Depth(50, 15.0), 3);
        Assert.Equal(0, DA40RedBox.Depth(30.8, 13.5));
    }

    [Fact]
    public void TheStateNamesTheCylindersInside()
    {
        var heat = new[] { 50.0, 50.0, 30.0, 50.0 };
        var afr = new[] { 13.5, 11.0, 13.5, 15.0 };
        Assert.Equal("In the red box, cylinders 1 and 4 - the mixture is damaging the engine",
            DA40RedBox.Describe(heat, afr));
        Assert.Equal("Clear", DA40RedBox.Describe(new[] { 30.8, 30.8, 30.8, 30.8 }, new[] { 13.5, 13.5, 13.5, 13.5 }));
    }
}
