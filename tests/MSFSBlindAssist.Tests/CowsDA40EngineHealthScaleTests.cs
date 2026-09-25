using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ The block does not share the other health readings' scale, and the first version of the
/// engine-health announcer assumed it did. The model's own formulas:
///
///     HEALTH_BLOCK = 1 - DAMAGE_BLOCK / 800
///     HEALTH_OIL   = (100 - DAMAGE_OIL) / 100
///
/// with damage capped at 100 on both. So a completely destroyed block publishes 0.875 where a
/// destroyed oil system publishes 0.0 — and read raw, "engine block 88 percent" describes an
/// engine that is as damaged as the model can make it.
/// </summary>
public class CowsDA40EngineHealthScaleTests
{
    [Fact]
    public void AFactoryEngineIsAHundredPercentOnEveryReading()
    {
        Assert.Equal(100, CowsDA40Definition.HealthPercent("DA40_HEALTH_BLOCK", 1.0), 1);
        Assert.Equal(100, CowsDA40Definition.HealthPercent("DA40_HEALTH_OIL", 1.0), 1);
        Assert.Equal(100, CowsDA40Definition.HealthPercent("DA40_HEALTH_PUMP1", 1.0), 1);
    }

    [Fact]
    public void AFullyDamagedBlockReadsZeroRatherThanEightyEight()
    {
        // DAMAGE_BLOCK at its 100 cap -> the model publishes 1 - 100/800 = 0.875.
        Assert.Equal(0, CowsDA40Definition.HealthPercent("DA40_HEALTH_BLOCK", 0.875), 1);
    }

    [Fact]
    public void HalfTheBlockDamageIsHalfTheReading()
    {
        // DAMAGE_BLOCK 50 -> 1 - 50/800 = 0.9375.
        Assert.Equal(50, CowsDA40Definition.HealthPercent("DA40_HEALTH_BLOCK", 0.9375), 1);
    }

    [Fact]
    public void TheOtherReadingsAreLeftAlone()
    {
        // Oil and fuel already span the full range; rescaling them would be the mirror bug.
        Assert.Equal(40, CowsDA40Definition.HealthPercent("DA40_HEALTH_OIL", 0.40), 1);
        Assert.Equal(40, CowsDA40Definition.HealthPercent("DA40_HEALTH_FUEL", 0.40), 1);
    }

    [Fact]
    public void TheReadingIsClampedRatherThanAllowedToGoNegative()
    {
        // Nothing should publish below the cap, but a rescale that multiplies a shortfall by
        // eight turns any surprise into a wildly negative percentage read aloud to a pilot.
        Assert.Equal(0, CowsDA40Definition.HealthPercent("DA40_HEALTH_BLOCK", 0.0), 1);
    }
}
