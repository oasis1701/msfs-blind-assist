using System;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

/// <summary>The two crew gear checks every First Officer profile maps its own lever and
/// light fields onto — "gear up, lights out" and "three green" — and the NaN-safe verdict
/// they are published as (owner decisions 2026-09-22).</summary>
public class GearLightRulesTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    public void Up_is_lever_up_and_every_light_out(bool leverUp, bool light1, bool light2, bool expected)
    {
        Assert.Equal(expected, GearLightRules.IsUp(leverUp, new[] { light1, light2 }));
    }

    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, false, false)]   // one green dark
    [InlineData(true, true, true, true, false)]     // a red on
    [InlineData(false, true, true, false, false)]   // lever not down
    public void Down_is_lever_down_every_green_on_and_no_red(bool leverDown, bool g1, bool g2, bool red, bool expected)
    {
        Assert.Equal(expected, GearLightRules.IsDown(leverDown, new[] { g1, g2 }, new[] { red }));
    }

    [Fact]
    public void Down_is_never_confirmed_with_no_greens_supplied()
    {
        Assert.False(GearLightRules.IsDown(true, Array.Empty<bool>(), Array.Empty<bool>()));
    }

    [Fact]
    public void AsField_is_NaN_when_any_reading_is_unknown_else_one_or_zero()
    {
        Assert.True(double.IsNaN(GearLightRules.AsField(new[] { 0.0, double.NaN }, true)));
        Assert.Equal(1.0, GearLightRules.AsField(new[] { 0.0, 2.0 }, true));
        Assert.Equal(0.0, GearLightRules.AsField(new[] { 0.0, 2.0 }, false));
        Assert.Equal(1.0, GearLightRules.AsField(Array.Empty<double>(), true));
    }
}
