using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which runway arms the landing assist's rollout centerline cue (RolloutCueArming.Decide), one test per
/// row of the design's table: the destination runway dialog (with or without its manual-landing
/// checkbox), the landing exit plan, the RolloutCenterlineToneEnabled setting, and which was chosen last.
/// With the setting off it is exactly the old behaviour: only the manual-landing checkbox arms anything.
/// </summary>
public class RolloutCueArmingTests
{
    private static Runway Ksea16R() => new Runway
    {
        RunwayID = "16R", Heading = 180.3, StartLat = 47.4638, StartLon = -122.3189, EndLat = 47.4405, EndLon = -122.3191,
    };

    private static Runway Ksea16C() => new Runway
    {
        RunwayID = "16C", Heading = 180.3, StartLat = 47.4638, StartLon = -122.3110, EndLat = 47.4310, EndLon = -122.3112,
    };

    private static Runway Kbfi14R() => new Runway
    {
        RunwayID = "14R", Heading = 133.0, StartLat = 47.5400, StartLon = -122.3100, EndLat = 47.5180, EndLon = -122.2900,
    };

    private static readonly IReadOnlyList<Runway> KseaRunways = new[] { Ksea16R(), Ksea16C() };
    private static readonly IReadOnlyList<Runway> KbfiRunways = new[] { Kbfi14R() };

    private static RolloutCueDestination Destination(bool manualLanding, string icao = "KSEA") =>
        new(Ksea16R(), icao, KseaRunways, manualLanding);

    private static RolloutCueExitPlan KseaPlan() => new(Ksea16C(), "KSEA", KseaRunways);
    private static RolloutCueExitPlan KbfiPlan() => new(Kbfi14R(), "KBFI", KbfiRunways);

    // ---- Setting off: exactly the old behaviour ----

    [Fact]
    public void SettingOff_DestinationWithTheCheckbox_ArmsWithFlare()
    {
        var d = RolloutCueArming.Decide(false, Destination(manualLanding: true), null, RolloutCueSource.Destination);
        Assert.NotNull(d);
        Assert.Equal(RolloutCueSource.Destination, d!.Source);
        Assert.Equal("16R", d.Runway.RunwayID);
        Assert.True(d.IncludeFlare);
    }

    [Fact]
    public void SettingOff_DestinationWithoutTheCheckbox_ArmsNothing()
        => Assert.Null(RolloutCueArming.Decide(false, Destination(manualLanding: false), null, RolloutCueSource.Destination));

    [Fact]
    public void SettingOff_ExitPlanOnly_ArmsNothing()
        => Assert.Null(RolloutCueArming.Decide(false, null, KseaPlan(), RolloutCueSource.ExitPlan));

    // ---- Setting on ----

    [Fact]
    public void SettingOn_Neither_ArmsNothing()
        => Assert.Null(RolloutCueArming.Decide(true, null, null, RolloutCueSource.Destination));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingOn_DestinationOnly_ArmsIt_WithFlareOnlyFromTheCheckbox(bool manualLanding)
    {
        var d = RolloutCueArming.Decide(true, Destination(manualLanding), null, RolloutCueSource.Destination);
        Assert.NotNull(d);
        Assert.Equal(RolloutCueSource.Destination, d!.Source);
        Assert.Equal("16R", d.Runway.RunwayID);
        Assert.Equal("KSEA", d.Icao);
        Assert.Same(KseaRunways, d.Runways);
        Assert.Equal(manualLanding, d.IncludeFlare);
    }

    [Fact]
    public void SettingOn_ExitPlanOnly_ArmsThePlansRunway_WithoutFlare()
    {
        var d = RolloutCueArming.Decide(true, null, KseaPlan(), RolloutCueSource.ExitPlan);
        Assert.NotNull(d);
        Assert.Equal(RolloutCueSource.ExitPlan, d!.Source);
        Assert.Equal("16C", d.Runway.RunwayID);
        Assert.False(d.IncludeFlare);
    }

    // The same airport: the destination keeps the arming (and a ticked checkbox its flare tones), even
    // when the plan came later; the touchdown runway check corrects 16R to 16C if that is where it lands.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingOn_BothAtTheSameAirport_ArmTheDestination(bool manualLanding)
    {
        var d = RolloutCueArming.Decide(true, Destination(manualLanding), KseaPlan(), RolloutCueSource.ExitPlan);
        Assert.NotNull(d);
        Assert.Equal(RolloutCueSource.Destination, d!.Source);
        Assert.Equal("16R", d.Runway.RunwayID);
        Assert.Equal(manualLanding, d.IncludeFlare);
    }

    [Fact]
    public void SettingOn_TheSameAirport_IgnoresIcaoCase()
    {
        var d = RolloutCueArming.Decide(true, Destination(manualLanding: true, icao: "ksea"), KseaPlan(),
            RolloutCueSource.ExitPlan);
        Assert.Equal(RolloutCueSource.Destination, d!.Source);
    }

    // Different airports: the pilot's latest choice wins.
    [Fact]
    public void SettingOn_BothAtDifferentAirports_TheLatestExitPlanWins_WithoutFlare()
    {
        var d = RolloutCueArming.Decide(true, Destination(manualLanding: true), KbfiPlan(), RolloutCueSource.ExitPlan);
        Assert.NotNull(d);
        Assert.Equal(RolloutCueSource.ExitPlan, d!.Source);
        Assert.Equal("KBFI", d.Icao);
        Assert.Equal("14R", d.Runway.RunwayID);
        Assert.False(d.IncludeFlare);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingOn_BothAtDifferentAirports_TheLatestDestinationWins(bool manualLanding)
    {
        var d = RolloutCueArming.Decide(true, Destination(manualLanding), KbfiPlan(), RolloutCueSource.Destination);
        Assert.NotNull(d);
        Assert.Equal(RolloutCueSource.Destination, d!.Source);
        Assert.Equal("KSEA", d.Icao);
        Assert.Equal(manualLanding, d.IncludeFlare);
    }

    // ---- SameArming: MainForm re-arms only when this says the decision changed ----

    private static RolloutCueArmingDecision Decision(Runway runway, string icao, bool flare,
        RolloutCueSource source = RolloutCueSource.Destination)
        => new(source, runway, icao, KseaRunways, flare);

    [Fact]
    public void SameArming_NullAndNull_IsTheSame()
        => Assert.True(RolloutCueArming.SameArming(null, null));

    [Fact]
    public void SameArming_NullAndADecision_Differ()
    {
        Assert.False(RolloutCueArming.SameArming(null, Decision(Ksea16R(), "KSEA", false)));
        Assert.False(RolloutCueArming.SameArming(Decision(Ksea16R(), "KSEA", false), null));
    }

    [Fact]
    public void SameArming_TheSameIcaoEndAndFlare_IsTheSame_WhateverTheSource()
        => Assert.True(RolloutCueArming.SameArming(
            Decision(Ksea16R(), "KSEA", true, RolloutCueSource.Destination),
            Decision(Ksea16R(), "ksea", true, RolloutCueSource.ExitPlan)));

    [Fact]
    public void SameArming_ADifferentFlareEndOrAirport_Differs()
    {
        var baseline = Decision(Ksea16R(), "KSEA", false);
        Assert.False(RolloutCueArming.SameArming(baseline, Decision(Ksea16R(), "KSEA", true)));
        Assert.False(RolloutCueArming.SameArming(baseline, Decision(Ksea16C(), "KSEA", false)));
        Assert.False(RolloutCueArming.SameArming(baseline, Decision(Kbfi14R(), "KBFI", false)));
    }
}
