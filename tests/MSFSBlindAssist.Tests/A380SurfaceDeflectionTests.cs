using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ Written against a live report of an aeroplane that was working perfectly.
///
/// At flaps 2 the A380's ailerons droop, and the F/CTL readout announced it as
/// "16 percent" on one wing and "-17 percent" on the other - bare signed numbers, mirrored
/// by FBW, with no direction named. That is indistinguishable from a roll input with the
/// sidestick centred, and the pilot concluded the aircraft was broken. The stock
/// AILERON POSITION read -3.5e-6 and YOKE X POSITION -8.3e-7 throughout: nothing was wrong.
/// </summary>
public class A380SurfaceDeflectionTests
{
    [Fact]
    public void TheMeasuredDroopReadsAsBothWingsDown()
    {
        // The exact live values: left +16 (FBW-inverted), right -17 (as published).
        Assert.Equal("16 percent down", A380SurfaceDeflection.DescribeMirrored(0.16));
        Assert.Equal("17 percent down", A380SurfaceDeflection.Describe(-0.17));
    }

    [Fact]
    public void TheInboardPairAgreesToo()
    {
        // Left inward +12, right inward -20 - different magnitudes, same direction.
        Assert.Contains("down", A380SurfaceDeflection.DescribeMirrored(0.12));
        Assert.Contains("down", A380SurfaceDeflection.Describe(-0.20));
    }

    [Fact]
    public void ARealRollStillReadsAsOpposedSurfaces()
    {
        // ⚠️ The property the mirroring fix must NOT destroy. In a genuine roll the two wings
        // move OPPOSITE ways physically, and that has to survive un-mirroring or the readout
        // would hide a roll instead of a droop - the same failure pointed the other way.
        // Left published -0.5 (so physically up), right published -0.5 (physically down).
        Assert.Equal("50 percent up", A380SurfaceDeflection.DescribeMirrored(-0.5));
        Assert.Equal("50 percent down", A380SurfaceDeflection.Describe(-0.5));
    }

    [Fact]
    public void UpIsPositiveOnTheUninvertedSide()
    {
        // hyd_deflection_to_msfs_deflection maps [0,1] onto (hyd*50 - 20)/30, 30 degrees up
        // against 20 down, so the positive extreme is UP.
        Assert.Equal("100 percent up", A380SurfaceDeflection.Describe(1.0));
        Assert.Equal("67 percent down", A380SurfaceDeflection.Describe(-2.0 / 3.0));
    }

    [Fact]
    public void ASurfaceAtRestHasNoDirection()
    {
        Assert.Equal("neutral", A380SurfaceDeflection.Describe(0.0));
        Assert.Equal("neutral", A380SurfaceDeflection.DescribeMirrored(0.0));
        // Rounding to zero counts as neutral - the stock vars sit at ~1e-6, not exactly 0.
        Assert.Equal("neutral", A380SurfaceDeflection.Describe(-0.0000035));
    }

    [Fact]
    public void MirroringIsExactlyANegation()
    {
        foreach (double v in new[] { -1.0, -0.42, 0.13, 0.87, 1.0 })
            Assert.Equal(A380SurfaceDeflection.Describe(-v), A380SurfaceDeflection.DescribeMirrored(v));
    }
}
