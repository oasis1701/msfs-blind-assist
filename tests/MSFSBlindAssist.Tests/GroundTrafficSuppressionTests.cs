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
        Assert.True(GroundTrafficSuppression.Suppress(true, TaxiGuidanceState.Taxiing, Rolling));
        Assert.True(GroundTrafficSuppression.Suppress(true, TaxiGuidanceState.Taxiing, Stopped));
    }

    [Fact]
    public void With_no_taxi_guidance_running_traffic_callouts_stay_off()
        => Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Inactive, Stopped));

    [Fact]
    public void Traffic_callouts_are_silent_while_the_landing_rollout_is_still_rolling()
    {
        Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, 120.0));
        Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, Rolling));
    }

    [Fact]
    public void Stopping_on_the_runway_brings_traffic_callouts_back()
        => Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, Stopped));

    [Fact]
    public void A_crawl_still_counts_as_rolling()
        => Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, 5.0));

    [Fact]
    public void An_unknown_ground_speed_keeps_the_rollout_silent_as_before()
        => Assert.True(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.LandingRollout, null));

    [Fact]
    public void Taxiing_backtracking_and_holding_all_get_traffic_callouts()
    {
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, Rolling));
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.BacktrackingOnRunway, Rolling));
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.HoldShort, Stopped));
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Arrived, Stopped));
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
        => Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(true, TaxiGuidanceState.Inactive, gs));

    [Fact]
    public void The_takeoff_roll_silences_the_runway_watch()
        => Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(
            true, TaxiGuidanceState.Inactive, GroundTrafficSuppression.RunwayWatchTakeoffCutoffKts));

    [Fact]
    public void Takeoff_assist_with_an_unknown_speed_silences_the_runway_watch()
        => Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(true, TaxiGuidanceState.Inactive, null));

    [Fact]
    public void Without_takeoff_assist_the_watch_follows_the_proximity_rule()
    {
        Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.Inactive, Stopped));
        Assert.True(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.LandingRollout, Rolling));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.LandingRollout, Stopped));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.HoldShort, Stopped));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.BacktrackDeparture, Rolling));
    }

    [Fact]
    public void Proximity_suppression_is_unchanged_during_the_line_up_wait()
        => Assert.True(GroundTrafficSuppression.Suppress(true, TaxiGuidanceState.Inactive, Stopped));

    // --- The landing exit (KMEM 36L 2026-09-26: "Slow down…", "Slow down…", "Stop…" interrupted the
    // --- exit at 44-47 kt the moment the rollout handed over to taxi steering) ----------------------

    [Fact]
    public void The_fast_landing_exit_no_longer_mutes_the_monitor()
    {
        // The monitor keeps evaluating (and the runway watch keeps watching) - only what is spoken is filtered.
        Assert.False(GroundTrafficSuppression.Suppress(false, TaxiGuidanceState.Taxiing, 44.1));
        Assert.False(GroundTrafficSuppression.SuppressRunwayWatch(false, TaxiGuidanceState.Taxiing, 44.1));
    }

    [Fact]
    public void Above_taxi_speed_on_the_exit_only_warnings_are_spoken()
    {
        Assert.True(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Taxiing, 44.1, true));
        Assert.True(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Taxiing, 30.0, true));
        Assert.True(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Taxiing, null, true));
    }

    [Fact]
    public void Below_taxi_speed_after_the_exit_or_on_an_ordinary_taxi_everything_is_spoken()
    {
        Assert.False(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Taxiing, 25.0, true));
        Assert.False(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Arrived, 35.7, true));
        Assert.False(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Taxiing, 47.4, false));
    }

    [Theory]
    [InlineData(TrafficCalloutKind.Warning, true)]
    [InlineData(TrafficCalloutKind.RunwayCritical, true)]
    [InlineData(TrafficCalloutKind.RunwayInfo, true)]
    [InlineData(TrafficCalloutKind.Caution, false)]
    [InlineData(TrafficCalloutKind.Converging, false)]
    [InlineData(TrafficCalloutKind.OnRoute, false)]
    [InlineData(TrafficCalloutKind.Awareness, false)]
    [InlineData(TrafficCalloutKind.QueueMoving, false)]
    [InlineData(TrafficCalloutKind.MoveUp, false)]
    [InlineData(TrafficCalloutKind.QueuePosition, false)]
    public void On_the_fast_exit_only_stop_runway_events_and_the_runway_status_speak(TrafficCalloutKind kind, bool speaks)
        => Assert.Equal(speaks, TrafficSpeechPolicy.SpeaksOnFastLandingExit(kind));
}
