using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Device-loss re-arm policy for the manual-landing assist's two tones.
///
/// Why this exists: the router rebinds a generator that comes back with NeedsDevice set, but it
/// only does so on a SWEEP, and a sweep is what a device ARRIVING triggers. A rebind that moves
/// one tone successfully and fails the other leaves the failed one silent with no further event
/// coming, and the manager's own started-latch still true — so nothing restarts it.
///
/// For this feature that failure is worse than a missing cue. The vertical tone is SILENT WHEN ON
/// PROFILE by design, so a vertical tone that has quietly lost its device is indistinguishable
/// from a perfectly flown flare, while the surviving lateral tone keeps the assist sounding alive.
/// The pilot gets a confident "on profile" while descending too fast.
///
/// The policy pinned here is deliberately NOT VisualGuidanceManager's pair rebuild. VG restarts
/// BOTH tones when either dies because its two are a matched reference/follower pair that is
/// meaningless unless heard together. These two are independent axes — vertical sink rate and
/// lateral position — so restarting a healthy one would only punch an audible hole in a cue the
/// pilot is actively flying.
/// </summary>
public class LandingFlareToneReArmTests
{
    [Fact]
    public void Healthy_tones_need_no_restart()
    {
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: false,
            verticalStarted: true, verticalNeedsDevice: false,
            reArmSpent: false);

        Assert.False(d.RestartLateral);
        Assert.False(d.RestartVertical);
    }

    [Fact]
    public void A_lost_lateral_tone_restarts_only_itself()
    {
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: true,
            verticalStarted: true, verticalNeedsDevice: false,
            reArmSpent: false);

        Assert.True(d.RestartLateral);
        // The healthy vertical tone is mid-flare guidance; tearing it down to keep a pair
        // invariant this feature does not have would silence the sink-rate cue for the gap.
        Assert.False(d.RestartVertical);
    }

    [Fact]
    public void A_lost_vertical_tone_restarts_only_itself()
    {
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: false,
            verticalStarted: true, verticalNeedsDevice: true,
            reArmSpent: false);

        Assert.True(d.RestartVertical);
        Assert.False(d.RestartLateral);
    }

    [Fact]
    public void Both_lost_restarts_both()
    {
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: true,
            verticalStarted: true, verticalNeedsDevice: true,
            reArmSpent: false);

        Assert.True(d.RestartLateral);
        Assert.True(d.RestartVertical);
    }

    [Fact]
    public void A_tone_that_was_never_started_is_not_restarted()
    {
        // The vertical tone is flare-only — StopVerticalTone runs at touchdown, so through the
        // whole rollout it is legitimately stopped. A stale NeedsDevice on a tone that is not
        // supposed to be sounding must never drag it back up mid-rollout.
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: false,
            verticalStarted: false, verticalNeedsDevice: true,
            reArmSpent: false);

        Assert.False(d.RestartVertical);
        Assert.False(d.RestartLateral);
    }

    [Fact]
    public void One_re_arm_per_outage_then_the_router_owns_the_retries()
    {
        // Second and later retries belong to the router's event-driven sweeps. Without this the
        // 1 Hz sampler would attempt a WASAPI open every tick for the rest of the approach.
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: true,
            verticalStarted: true, verticalNeedsDevice: true,
            reArmSpent: true);

        Assert.False(d.RestartLateral);
        Assert.False(d.RestartVertical);
        Assert.True(d.SpendReArm);   // stays spent while the outage lasts
    }

    [Fact]
    public void Firing_the_re_arm_spends_the_latch()
    {
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: true,
            verticalStarted: true, verticalNeedsDevice: false,
            reArmSpent: false);

        Assert.True(d.SpendReArm);
    }

    [Fact]
    public void Recovery_clears_the_latch_so_a_later_outage_re_arms_again()
    {
        // The latch is per-OUTAGE, not per-approach: a pilot who changes device twice on one
        // approach must get a restart both times.
        var d = LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: true, lateralNeedsDevice: false,
            verticalStarted: true, verticalNeedsDevice: false,
            reArmSpent: true);

        Assert.False(d.SpendReArm);
        Assert.False(d.RestartLateral);
        Assert.False(d.RestartVertical);
    }
}
