namespace MSFSBlindAssist.Services;

/// <summary>
/// When the ground-traffic monitor stays quiet. The rule behind
/// <c>MainForm</c>'s <c>groundTrafficMonitor.SuppressCheck</c>, so it can be reasoned about and
/// pinned rather than living as an untestable lambda.
///
/// <para>Automatic Caution and Warning callouts are held back during the takeoff roll (the pilot's
/// hands are on rudder and throttle and they cannot act on one), when taxi guidance is not engaged
/// at all, and while a landing rollout is STILL ROLLING — there the exit and runway-end callouts
/// must not be talked over.</para>
///
/// <para>The rolling qualifier is the part that matters. Neither rollout reason survives the
/// aircraft coming to a stop: a stationary aircraft has no callouts pending and its pilot has a
/// hand free. Since the runway-end countdown learned to sit through a mid-runway hold rather than
/// treating it as a backtrack, a pilot stopped on the runway for ATC can remain in the landing
/// rollout indefinitely — and used to have every traffic callout dropped for the whole of it.</para>
///
/// <para>The Alt+G manual summary is unaffected; it lives outside the monitor's poll loop.
/// Pure — <c>GroundTrafficSuppressionTests</c>.</para>
/// </summary>
public static class GroundTrafficSuppression
{
    /// <param name="groundSpeedKts">The aircraft's ground speed, or null when it is not known yet.
    /// Unknown counts as rolling, so an unread position never turns callouts on mid-rollout.</param>
    public static bool Suppress(bool takeoffAssistActive, TaxiGuidanceState state, double? groundSpeedKts)
    {
        if (takeoffAssistActive) return true;
        if (state == TaxiGuidanceState.Inactive) return true;
        if (state != TaxiGuidanceState.LandingRollout) return false;

        return groundSpeedKts is not double gs
               || gs >= Navigation.RolloutExitGate.NoExitStoppedGroundSpeedKts;
    }

    /// <summary>
    /// Takeoff-roll cutoff for the runway watch: below it, with takeoff assist on, the pilot is
    /// lined up and waiting, which is exactly when traffic landing on or entering the runway
    /// matters most (PR #247 review R1).
    /// </summary>
    public const double RunwayWatchTakeoffCutoffKts = 30.0;

    /// <summary>
    /// The runway watch's own gate. <see cref="Suppress"/> silences everything while takeoff assist
    /// is on — the takeoff-roll rule for PROXIMITY callouts — and takeoff assist auto-activates at
    /// lineup alignment, so the line-up wait lost the runway watch with it. Here takeoff assist
    /// suppresses only once the ground speed is known to be at or above
    /// <see cref="RunwayWatchTakeoffCutoffKts"/> (an unknown speed counts as rolling); otherwise the
    /// rule is exactly <see cref="Suppress"/>'s.
    /// </summary>
    public static bool SuppressRunwayWatch(bool takeoffAssistActive, TaxiGuidanceState state, double? groundSpeedKts)
    {
        if (takeoffAssistActive)
            return groundSpeedKts is not double gs || gs >= RunwayWatchTakeoffCutoffKts;
        return Suppress(false, state, groundSpeedKts);
    }
}
