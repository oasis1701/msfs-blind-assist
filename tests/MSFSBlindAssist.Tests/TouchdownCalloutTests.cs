// Characterization tests for TouchdownCallout — the ONE utterance that carries a landing-exit
// runway correction, and which rollout milestones it retires so they cannot cut it off
// (PR #236 review, findings F6 and F7).

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class TouchdownCalloutTests
{
    private static readonly TouchdownRunwayCorrection OnThirtyLeft = new("30L", "12L");

    // Feet-mode triggers, as TaxiGuidanceManager passes them.
    private const double T1500 = 1500, T900 = 900, T500 = 500, TurnNow = 150, TaxiKts = 30, Lead = 9.0;

    [Fact]
    public void Without_a_correction_the_touchdown_sentence_is_unchanged()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;

        Assert.Equal("Touchdown. high-speed exit taxiway M13 in 4000 feet.",
            TouchdownCallout.ComposeExit(null, "High-speed", "M13", 4000, default, null));
        Assert.Equal("Touchdown. exit taxiway K6.",
            TouchdownCallout.ComposeExit(null, "Normal", "K6", 0, default, null));
    }

    [Fact]
    public void A_correction_leads_the_sentence()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;

        Assert.Equal("Touchdown on runway 30L, not 12L. High-speed exit taxiway M12A in 4000 feet.",
            TouchdownCallout.ComposeExit(OnThirtyLeft, "High-speed", "M12A", 4000, default, null));
        Assert.Equal("Touchdown on runway 30L, not 12L. Runway-end exit taxiway K9 in 3000 feet.",
            TouchdownCallout.ComposeExit(OnThirtyLeft, "End", "K9", 3000, default, null));
    }

    [Fact]
    public void Retired_callouts_fold_slow_down_then_the_turn()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        var retired = new ExitCalloutRetirement(Retire1500: true, Retire900: false, Retire500: true, RetireTurnNow: true, SlowDown: true);

        Assert.Equal("Touchdown on runway 30L, not 12L. Exit taxiway B2 in 150 feet. Slow down. Turn left now.",
            TouchdownCallout.ComposeExit(OnThirtyLeft, "Normal", "B2", 140, retired, "Turn left"));
    }

    [Fact]
    public void No_usable_exit_sentence_folds_the_runway_end_distance_only_when_a_milestone_is_retired()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;

        Assert.Equal("Touchdown on runway 30L, not 12L. No usable exit.",
            TouchdownCallout.ComposeNoUsableExit(OnThirtyLeft, 9000, default));
        Assert.Equal("Touchdown on runway 30L, not 12L. No usable exit. Runway end in 900 feet. Slow down.",
            TouchdownCallout.ComposeNoUsableExit(OnThirtyLeft, 900, new RunwayEndCalloutRetirement(true, true, false, true)));
        Assert.Equal("Touchdown on runway 30L, not 12L. No usable exit. Runway end in 100 feet. Stop.",
            TouchdownCallout.ComposeNoUsableExit(OnThirtyLeft, 90, new RunwayEndCalloutRetirement(true, true, true, false)));
    }

    [Fact]
    public void A_milestone_already_inside_is_retired_even_when_stopped()
    {
        var r = TouchdownCallout.RetireExitCallouts(1200, 0, "Normal", Lead, T1500, T900, T500, TurnNow, TaxiKts);

        Assert.True(r.Retire1500);
        Assert.False(r.Retire900);
        Assert.False(r.Retire500);
        Assert.False(r.RetireTurnNow);
    }

    [Fact]
    public void A_milestone_reached_within_the_lead_is_retired_and_one_beyond_it_is_kept()
    {
        // 150 kt is about 253 ft/s, so 9 s covers about 2,279 ft. From 3,000 ft the 1,500 ft
        // trigger is 1,500 ft away (retired) and the 500 ft trigger 2,500 ft away (kept).
        var r = TouchdownCallout.RetireExitCallouts(3000, 150, "Normal", Lead, T1500, T900, T500, TurnNow, TaxiKts);

        Assert.True(r.Retire1500);
        Assert.False(r.Retire500);
        Assert.False(r.SlowDown);
    }

    [Fact]
    public void Nothing_is_retired_far_from_the_exit()
    {
        Assert.Equal(default(ExitCalloutRetirement),
            TouchdownCallout.RetireExitCallouts(6000, 150, "High-speed", Lead, T1500, T900, T500, TurnNow, TaxiKts));
    }

    [Fact]
    public void Turn_now_uses_the_strict_inside_test()
    {
        Assert.False(TouchdownCallout.RetireExitCallouts(200, 60, "Normal", Lead, T1500, T900, T500, TurnNow, TaxiKts).RetireTurnNow);
        Assert.True(TouchdownCallout.RetireExitCallouts(150, 60, "Normal", Lead, T1500, T900, T500, TurnNow, TaxiKts).RetireTurnNow);
    }

    [Fact]
    public void Slow_down_folds_only_for_a_retired_500_on_a_non_high_speed_exit_above_taxi_speed()
    {
        Assert.True(TouchdownCallout.RetireExitCallouts(450, 40, "Normal", Lead, T1500, T900, T500, TurnNow, TaxiKts).SlowDown);
        Assert.False(TouchdownCallout.RetireExitCallouts(450, 40, "High-speed", Lead, T1500, T900, T500, TurnNow, 60).SlowDown);
        Assert.False(TouchdownCallout.RetireExitCallouts(450, 20, "Normal", Lead, T1500, T900, T500, TurnNow, TaxiKts).SlowDown);
    }

    [Fact]
    public void The_900_ft_milestone_exists_only_for_high_speed_exits()
    {
        Assert.True(TouchdownCallout.RetireExitCallouts(800, 0, "High-speed", Lead, T1500, T900, T500, TurnNow, TaxiKts).Retire900);
        Assert.False(TouchdownCallout.RetireExitCallouts(800, 0, "Normal", Lead, T1500, T900, T500, TurnNow, TaxiKts).Retire900);
    }

    [Fact]
    public void Runway_end_callouts_retire_on_the_same_rules_with_a_strict_100_ft_stop()
    {
        var stopped = TouchdownCallout.RetireRunwayEndCallouts(400, 0, Lead, 1500, 500, 100, TaxiKts);
        Assert.True(stopped.Retire1500);
        Assert.True(stopped.Retire500);
        Assert.False(stopped.Retire100);
        Assert.False(stopped.SlowDown);

        Assert.True(TouchdownCallout.RetireRunwayEndCallouts(80, 60, Lead, 1500, 500, 100, TaxiKts).Retire100);

        // 60 kt is about 101 ft/s, but the aircraft is braking: it slows to taxi speed after ~7.7 s
        // and covers about 650 ft in the 9 s the sentence takes, not the 911 ft of holding 60 kt.
        // From 1,100 ft the 500 ft trigger is 600 ft away and comes due inside the sentence, so it
        // is folded in (with its "Slow down."); from 1,300 ft it is 800 ft away and is left to
        // speak for itself, which is the countdown the pilot still wants.
        Assert.True(TouchdownCallout.RetireRunwayEndCallouts(1100, 60, Lead, 1500, 500, 100, TaxiKts).SlowDown);
        Assert.False(TouchdownCallout.RetireRunwayEndCallouts(1300, 60, Lead, 1500, 500, 100, TaxiKts).Retire500);
    }

    [Fact]
    public void The_crossing_decline_helper_forwards_to_the_shared_rule()
    {
        foreach (var (d, t, gs, lead) in new[] { (1200.0, 1500.0, 0.0, 4.0), (1600.0, 1500.0, 60.0, 4.0), (3000.0, 1500.0, 60.0, 4.0) })
        {
            Assert.Equal(RolloutRunwayReCrossing.DeclineSupersedesCallout(d, t, gs, lead),
                         RolloutCalloutSupersession.Supersedes(d, t, gs, lead));
        }
    }
}
