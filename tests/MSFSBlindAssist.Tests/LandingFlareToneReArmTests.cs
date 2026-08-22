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
/// The pilot gets a confident "on profile" while descending too fast. That is also why a vertical
/// tone that stays lost AFTER its retry must be SPOKEN (AnnounceVerticalLost): the manager's own
/// go-around rule — silence must never be mistakable for "on profile" — applies equally to a cue
/// the manager tried to revive and could not.
///
/// The policy pinned here is deliberately NOT VisualGuidanceManager's pair rebuild. VG restarts
/// BOTH tones when either dies because its two are a matched reference/follower pair that is
/// meaningless unless heard together. These two are independent axes — vertical sink rate and
/// lateral position — so restarting a healthy one would only punch an audible hole in a cue the
/// pilot is actively flying. For the same reason each tone carries its OWN one-per-outage latch:
/// a vertical tone that failed permanently must not spend the lateral tone's re-arm.
/// </summary>
public class LandingFlareToneReArmTests
{
    private static LandingFlareAssistManager.ToneReArmDecision Decide(
        bool latStarted = true, bool latLost = false, bool latSpent = false,
        bool vertStarted = true, bool vertLost = false, bool vertSpent = false)
        => LandingFlareAssistManager.DecideToneReArm(
            lateralStarted: latStarted, lateralNeedsDevice: latLost, lateralReArmSpent: latSpent,
            verticalStarted: vertStarted, verticalNeedsDevice: vertLost, verticalReArmSpent: vertSpent);

    [Fact]
    public void Healthy_tones_need_no_restart_and_no_announcement()
    {
        var d = Decide();

        Assert.False(d.RestartLateral);
        Assert.False(d.RestartVertical);
        Assert.False(d.AnnounceVerticalLost);
    }

    [Fact]
    public void A_lost_lateral_tone_restarts_only_itself()
    {
        var d = Decide(latLost: true);

        Assert.True(d.RestartLateral);
        // The healthy vertical tone is mid-flare guidance; tearing it down to keep a pair
        // invariant this feature does not have would silence the sink-rate cue for the gap.
        Assert.False(d.RestartVertical);
    }

    [Fact]
    public void A_lost_vertical_tone_restarts_only_itself()
    {
        var d = Decide(vertLost: true);

        Assert.True(d.RestartVertical);
        Assert.False(d.RestartLateral);
    }

    [Fact]
    public void Both_lost_restarts_both()
    {
        var d = Decide(latLost: true, vertLost: true);

        Assert.True(d.RestartLateral);
        Assert.True(d.RestartVertical);
    }

    [Fact]
    public void A_tone_that_was_never_started_is_not_restarted()
    {
        // The vertical tone is flare-only — StopVerticalTone runs at touchdown, so through the
        // whole rollout it is legitimately stopped. A stale NeedsDevice on a tone that is not
        // supposed to be sounding must never drag it back up mid-rollout, and must not be
        // announced as a lost cue either: nothing was lost.
        var d = Decide(vertStarted: false, vertLost: true, vertSpent: true);

        Assert.False(d.RestartVertical);
        Assert.False(d.RestartLateral);
        Assert.False(d.AnnounceVerticalLost);
    }

    [Fact]
    public void One_re_arm_per_tone_per_outage_then_the_router_owns_the_retries()
    {
        // Second and later retries belong to the router's event-driven sweeps. Without this the
        // 1 Hz sampler would attempt a WASAPI open every tick for the rest of the approach.
        var d = Decide(latLost: true, latSpent: true, vertLost: true, vertSpent: true);

        Assert.False(d.RestartLateral);
        Assert.False(d.RestartVertical);
        Assert.True(d.SpendLateralReArm);    // stay spent while the outage lasts
        Assert.True(d.SpendVerticalReArm);
    }

    [Fact]
    public void The_latches_are_per_tone_so_one_dead_tone_cannot_lock_out_the_other()
    {
        // A permanently failed vertical tone must not spend the LATERAL tone's re-arm: the
        // lateral tone carries the whole rollout, and a later independent outage on it still
        // deserves its one restart.
        var d = Decide(latLost: true, latSpent: false, vertLost: true, vertSpent: true);

        Assert.True(d.RestartLateral);
        Assert.False(d.RestartVertical);
    }

    [Fact]
    public void Firing_a_re_arm_spends_that_tones_latch_only()
    {
        var d = Decide(latLost: true);

        Assert.True(d.SpendLateralReArm);
        Assert.False(d.SpendVerticalReArm);
    }

    [Fact]
    public void Recovery_clears_the_latches_so_a_later_outage_re_arms_again()
    {
        // Per-OUTAGE, not per-approach: a pilot who changes device twice on one approach must
        // get a restart both times.
        var d = Decide(latSpent: true, vertSpent: true);

        Assert.False(d.SpendLateralReArm);
        Assert.False(d.SpendVerticalReArm);
        Assert.False(d.RestartLateral);
        Assert.False(d.RestartVertical);
    }

    [Fact]
    public void A_vertical_tone_still_lost_after_its_retry_is_announced()
    {
        // The retry ran (latch spent) and the tone is STILL device-less: from here the assist
        // would silently impersonate "on profile" for the rest of the flare. Speak it instead —
        // same rule as go-around. The caller adds the phase gate (Flare only, not silentFlare)
        // and the once-per-engagement latch.
        var d = Decide(vertLost: true, vertSpent: true);

        Assert.True(d.AnnounceVerticalLost);
        Assert.False(d.RestartVertical);
    }

    [Fact]
    public void The_announcement_waits_for_the_retry_not_the_first_loss()
    {
        // First detection restarts the tone; speaking on the same tick would announce an outage
        // the restart may be about to cure.
        var d = Decide(vertLost: true, vertSpent: false);

        Assert.True(d.RestartVertical);
        Assert.False(d.AnnounceVerticalLost);
    }

    [Fact]
    public void A_lost_lateral_tone_is_not_what_the_announcement_is_for()
    {
        // The lateral tone sounds continuously when off-centre, so its death is audible as an
        // absence the way VG's tones are; only the silent-when-good vertical cue can lie.
        var d = Decide(latLost: true, latSpent: true);

        Assert.False(d.AnnounceVerticalLost);
    }
}
