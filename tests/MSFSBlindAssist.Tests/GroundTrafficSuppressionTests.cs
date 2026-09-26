// When the ground-traffic monitor holds its tongue (MainForm's SuppressCheck), as a rule of its
// own rather than a lambda nobody can test.
//
// PR #236 follow-up. The landing rollout suppresses traffic callouts for two stated reasons — the
// pilot's hands are on the brakes and rudder, and the exit / runway-end callouts must not be talked
// over. Neither is true of an aircraft that has STOPPED, and since the runway-end countdown learned
// to sit still through a mid-runway hold (rather than immediately calling it a backtrack), a pilot
// holding on the runway for ATC could stay in the landing rollout indefinitely with every Caution
// and Warning about converging traffic silently dropped. That is the moment a blind pilot most
// needs to hear them.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficSuppressionTests
{
    private const double Rolling = 25.0;
    private const double Stopped = 0.0;

    [Fact]
    public void The_takeoff_roll_silences_traffic_callouts_at_any_speed()
    {
        Assert.True(GroundTrafficSuppression.Suppress(true, TaxiGuidanceState.Taxiing, Rolling, false));
        Assert.True(GroundTrafficSuppression.Suppress(true, TaxiGuidanceState.Taxiing, Stopped, false));
    }

    [Fact]
    public void With_no_taxi_guidance_running_traffic_callouts_stay_off()
        => Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Inactive, Stopped, false));

    [Fact]
    public void Traffic_callouts_are_silent_while_the_landing_rollout_is_still_rolling()
    {
        Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, 120.0, false));
        Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, Rolling, false));
    }

    [Fact]
    public void Stopping_on_the_runway_brings_traffic_callouts_back()
        => Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, Stopped, false));

    [Fact]
    public void A_crawl_still_counts_as_rolling()
        => Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, 5.0, false));

    [Fact]
    public void An_unknown_ground_speed_keeps_the_rollout_silent_as_before()
        => Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, null, false));

    [Fact]
    public void Taxiing_backtracking_and_holding_all_get_traffic_callouts()
    {
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, Rolling, false));
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.BacktrackingOnRunway, Rolling, false));
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.HoldShort, Stopped, false));
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Arrived, Stopped, false));
    }

    // --- The runway watch's own gate (PR #247 review R1) -------------------------------------
    // Takeoff assist switches on at lineup alignment by default, and Suppress() silences
    // everything while it is on — which silenced the new runway watch for the whole line-up
    // wait. The watch keeps running on the ground below the takeoff-roll cutoff; proximity
    // callouts keep the old rule (Suppress is unchanged).

    [Theory]
    [InlineData(0.0)]
    [InlineData(29.9)]
    public void The_line_up_wait_keeps_the_runway_watch(double gs)
        => Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(true, TaxiGuidanceState.Inactive, gs, false));

    [Fact]
    public void The_takeoff_roll_silences_the_runway_watch()
        => Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(
            true, TaxiGuidanceState.Inactive, GroundTrafficSuppression.RunwayWatchTakeoffCutoffKts, false));

    [Fact]
    public void Takeoff_assist_with_an_unknown_speed_silences_the_runway_watch()
        => Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(true, TaxiGuidanceState.Inactive, null, false));

    [Fact]
    public void Without_takeoff_assist_the_watch_follows_the_proximity_rule()
    {
        Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.Inactive, Stopped, false));
        Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.LandingRollout, Rolling, false));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.LandingRollout, Stopped, false));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.HoldShort, Stopped, false));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.BacktrackDeparture, Rolling, false));
    }

    [Fact]
    public void Proximity_suppression_is_unchanged_during_the_line_up_wait()
        => Assert.True(GroundTrafficSuppression.Suppress(true, TaxiGuidanceState.Inactive, Stopped, false));

    // --- The landing exit (KMEM 36L 2026-09-26: "Slow down…", "Slow down…", "Stop…" interrupted the
    // --- exit at 44-47 kt the moment the rollout handed over to taxi steering) ----------------------

    [Fact]
    public void Traffic_callouts_stay_silent_on_the_landing_exit_above_taxi_speed()
    {
        Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, 47.4, true));
        Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, 30.0, true));
        Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, null, true));
    }

    [Fact]
    public void Below_taxi_speed_on_the_exit_they_come_back()
        => Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, 29.9, true));

    [Fact]
    public void Once_exit_guidance_ends_they_come_back()
        => Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Arrived, 35.7, true));

    [Fact]
    public void Ordinary_taxiing_is_unchanged()
        => Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, 47.4, false));

    [Fact]
    public void The_runway_watch_follows_the_exit_mute_too()
    {
        Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.Taxiing, 47.4, true));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.Taxiing, 20.0, true));
    }
}
