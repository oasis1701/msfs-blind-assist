using System;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The coolant leak, turbo failure and boost leak are severities, not flags. Their onset is
/// announced — which needs them DELIVERED (Continuous and announced, or they are only read on
/// request) and read in the right SCALE (0 to 1 in the aeroplane, percent in the rule).
/// </summary>
public class CowsDA40GradedFailureTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryGradedFailureIsDeliveredInTheBackground(DA40Variant variant)
    {
        var vars = new CowsDA40Definition(variant).GetVariables();
        foreach (string key in CowsDA40Definition.GradedFailureKeys)
        {
            if (!vars.TryGetValue(key, out var def)) continue;   // variant-scoped
            Assert.True(def.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced,
                $"{key} is never delivered, so its onset can never be announced");
            Assert.Equal("number", def.Units);
        }
    }

    [Fact]
    public void TheFirstReadingIsABaselineNotAnOnset()
        => Assert.Null(CowsDA40Definition.GradedFailureCall("Coolant leak", null, 35, out _));

    [Fact]
    public void AnOnsetIsAnnouncedInPercent()
        => Assert.Equal("Coolant leak, 35 percent",
            CowsDA40Definition.GradedFailureCall("Coolant leak", 0, 35, out _));

    [Fact]
    public void ASmallChangeIsNotNews()
    {
        Assert.Null(CowsDA40Definition.GradedFailureCall("Boost leak", 35, 50, out bool record));
        Assert.False(record);
    }

    [Fact]
    public void A25PointWorseningIsAnnounced()
        => Assert.Equal("Boost leak worsening, 60 percent",
            CowsDA40Definition.GradedFailureCall("Boost leak", 35, 60, out _));

    [Fact]
    public void AClearIsSilentButRecorded()
    {
        Assert.Null(CowsDA40Definition.GradedFailureCall("Turbocharger failure", 60, 0, out bool record));
        Assert.True(record);
    }
}
