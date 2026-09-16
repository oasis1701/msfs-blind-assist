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
}
