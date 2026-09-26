namespace MSFSBlindAssist.Services;

/// <summary>
/// When the ground-traffic monitor stays quiet. The rule behind
/// <c>MainForm</c>'s <c>groundTrafficMonitor.SuppressCheck</c>, so it can be reasoned about and
/// pinned rather than living as an untestable lambda.
///
/// <para>Automatic Caution and Warning callouts are held back during the takeoff roll (the pilot's
/// hands are on rudder and throttle and they cannot act on one), when taxi guidance is not engaged
/// at all, while a landing rollout is STILL ROLLING — there the exit and runway-end callouts
/// must not be talked over — and while taxi guidance is steering a landing-exit route above taxi
/// speed, for the same reason.</para>
///
/// <para>The rolling qualifier is the part that matters. Neither rollout reason survives the
/// aircraft coming to a stop: a stationary aircraft has no callouts pending and its pilot has a
/// hand free. Since the runway-end countdown learned to sit through a mid-runway hold rather than
/// treating it as a backtrack, a pilot stopped on the runway for ATC can remain in the landing
/// rollout indefinitely — and used to have every traffic callout dropped for the whole of it.</para>
///
/// <para>The same is true once the rollout hands over to taxi steering on the exit: the handoff
/// itself does not mean the aircraft has slowed down, and a pilot still rolling fast down the exit
/// taxiway has the same hands-full, don't-talk-over-the-exit reasons as the rollout did (KMEM 36L
/// 2026-09-26: "Slow down…"/"Stop…" interrupted the exit guidance at 44-47 kt, moments after the
/// handoff).</para>
///
/// <para>The Alt+G manual summary is unaffected; it lives outside the monitor's poll loop.
/// Pure — <c>GroundTrafficSuppressionTests</c>.</para>
/// </summary>
public static class GroundTrafficSuppression
{
    /// <param name="groundSpeedKts">The aircraft's ground speed, or null when it is not known yet.
    /// Unknown counts as rolling, so an unread position never turns callouts on mid-rollout or
    /// mid-exit.</param>
    /// <param name="landingExitSteering">
    /// Taxi guidance is steering a landing-exit route (<c>TaxiGuidanceManager.IsLandingExitTaxiSteering</c>).
    /// Then callouts stay silent while the ground speed is unknown or at or above
    /// <see cref="Navigation.RolloutExitGate.TaxiGroundSpeedKts"/> (30 kt): the rollout's reasons for
    /// silence — hands on the brakes, the exit guidance must not be talked over — outlive the handoff to
    /// taxi steering until the aircraft is at taxi speed (KMEM 36L 2026-09-26).
    /// </param>
    public static bool Suppress(bool takeoffAssistActive, TaxiGuidanceState state, double? groundSpeedKts,
        bool landingExitSteering)
    {
        if (takeoffAssistActive) return true;
        if (state == TaxiGuidanceState.Inactive) return true;
        if (state == TaxiGuidanceState.LandingRollout)
            return groundSpeedKts is not double gs
                   || gs >= Navigation.RolloutExitGate.NoExitStoppedGroundSpeedKts;
        if (landingExitSteering && state == TaxiGuidanceState.Taxiing)
            return groundSpeedKts is not double exitGs
                   || exitGs >= Navigation.RolloutExitGate.TaxiGroundSpeedKts;
        return false;
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
    /// rule is exactly <see cref="Suppress"/>'s — landing-exit steering above taxi speed included.
    /// </summary>
    /// <param name="landingExitSteering">Passed straight through to <see cref="Suppress"/>; see its
    /// own doc comment.</param>
    public static bool SuppressRunwayWatch(bool takeoffAssistActive, TaxiGuidanceState state,
        double? groundSpeedKts, bool landingExitSteering)
    {
        if (takeoffAssistActive)
            return groundSpeedKts is not double gs || gs >= RunwayWatchTakeoffCutoffKts;
        return Suppress(false, state, groundSpeedKts, landingExitSteering);
    }
}
