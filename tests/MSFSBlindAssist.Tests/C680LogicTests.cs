using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class C680LogicTests
{
    [Fact]
    public void TakeoffSpeedsAtReferenceWeightAreTheTable()
        => Assert.Equal("V1 110, rotate 113, V2 121, flaps up 180 knots at 30775 pounds", C680Speeds.ComposeTakeoff(30775));

    [Fact]
    public void TakeoffSpeedsScaleWithWeightAndSaySo()
    {
        var s = C680Speeds.ComposeTakeoff(24000);
        Assert.Contains("estimated", s);
        Assert.Contains("V1 97", s); // 110 * sqrt(24000/30775) = 97.1
    }

    [Fact]
    public void UnknownWeightFallsBackToTheTable()
        => Assert.Contains("at reference weight", C680Speeds.ComposeTakeoff(null));

    [Fact]
    public void StallSpeedsComeFromTheFlightModel()
    {
        Assert.Contains("clean 89", C680Speeds.ComposeStall(null));
        Assert.Contains("full flap 76", C680Speeds.ComposeStall(null));
        Assert.Contains("estimated", C680Speeds.ComposeStall(24000));
    }

    [Fact]
    public void WeightMarginsNameTheLimitBeingApproached()
    {
        Assert.Contains("over max ramp 31025", C680Weights.Describe(31100));
        Assert.Contains("over max takeoff 30775", C680Weights.Describe(30900));
        Assert.Contains("under max takeoff", C680Weights.Describe(28000));
        Assert.Contains("max landing 27575", C680Weights.Describe(28000));
    }

    [Fact]
    public void BreakerLabelsAreUnique()
        => Assert.Equal(C680BreakerTable.Rows.Count, C680BreakerTable.Rows.Select(r => r.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Fact]
    public void SilentCachedReadoutsAreExcludedFromTheMonitorManager()
    {
        var vars = new SkywardC680Definition().GetVariables();
        foreach (var key in SkywardC680Definition.SilentCachedReadoutKeys)
            Assert.True(vars[key].ExcludeFromMonitorManager, key + " is cached-silent but would earn a Ctrl+M row");
    }
}
