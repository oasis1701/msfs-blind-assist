using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The DA40's landing numbers (visual guidance and the manual landing assist). Each is a
/// measurement or a published figure, never the A320 baseline it used to inherit.
/// </summary>
public class CowsDA40LandingProfileTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheFlareIsALightSinglesFlare(DA40Variant variant)
    {
        var p = new CowsDA40Definition(variant).GetVisualGuidanceProfile();
        // STATIC CG TO GROUND, and PLANE ALT ABOVE GROUND parked: 2.70 ft (2026-10-12).
        Assert.Equal(2.7, p.FlareAltitudeBiasFt);
        // FAA Airplane Flying Handbook: a light single rounds out 10-20 ft above the runway.
        Assert.InRange(p.FlareTriggerWheelHeightFt, 10.0, 20.0);
        Assert.True(p.FlareHoldsEntrySink);
        Assert.True(p.RolloutGuidanceEndKnots < 40.0);
    }

    [Theory]
    // The AFM's final approach speeds (66-77 KIAS) on a 3 degree path: about 350-410 fpm.
    [InlineData(350.0)]
    [InlineData(410.0)]
    public void ANormalApproachIsOnProfileUntilTheRoundOut(double approachSinkFpm)
    {
        var p = new CowsDA40Definition(DA40Variant.NG).GetVisualGuidanceProfile();
        // From the 50 ft engage down to just above the flare height the target is the sink the
        // aircraft arrived with, so the vertical tone stays silent through a normal approach.
        foreach (double h in new[] { 50.0, 30.0, 20.0 })
            Assert.Equal(-approachSinkFpm,
                LandingGuidanceLaws.FlareTargetVsFpm(h, p.FlareSinkTauSec, p.FlareTouchdownSinkFpm, approachSinkFpm), 6);
        // And it eases to the touchdown sink on the runway.
        Assert.Equal(-p.FlareTouchdownSinkFpm,
            LandingGuidanceLaws.FlareTargetVsFpm(0.0, p.FlareSinkTauSec, p.FlareTouchdownSinkFpm, approachSinkFpm), 6);
    }
}
