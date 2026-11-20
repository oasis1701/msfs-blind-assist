using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// W and Shift+W (2026-10-26): W was dead on the DA40 and Shift+W gave no centre of gravity,
/// though the guide promised both. Run under sv-SE: CI runs en-US and would not see a
/// decimal comma.
/// </summary>
public class CowsDA40WeightReadoutTests
{
    [Fact]
    public void WGivesPoundsWithTheCentreOfGravity()
    {
        string text = TestCulture.Run("sv-SE",
            () => CowsDA40Definition.GrossWeightReadout(2445, 25.34, 2888, kilograms: false));

        Assert.Equal("Gross weight 2445 pounds, center of gravity 25.3 percent MAC. Maximum 2888 pounds.", text);
    }

    [Fact]
    public void ShiftWGivesKilogramsWithTheCentreOfGravity()
    {
        string text = TestCulture.Run("sv-SE",
            () => CowsDA40Definition.GrossWeightReadout(2445, 25.34, 2888, kilograms: true));

        Assert.Equal("Gross weight 1109 kilograms, center of gravity 25.3 percent MAC. Maximum 1310 kilograms.", text);
    }

    [Fact]
    public void AMissingCentreOfGravityIsLeftOutNeverSpokenAsZero()
    {
        Assert.Equal("Gross weight 2445 pounds. Maximum 2888 pounds.",
            CowsDA40Definition.GrossWeightReadout(2445, null, 2888, kilograms: false));
        Assert.Equal("Gross weight not available yet",
            CowsDA40Definition.GrossWeightReadout(null, 25, 2888, kilograms: false));
    }
}
