namespace MSFSBlindAssist.Services;

/// <summary>
/// Whether a taxi-guidance callout may be held until <c>TaxiGuidanceManager</c>'s
/// start-warning chatter window (<c>_startChatterSuppressUntil</c>) closes, or must be
/// spoken right away because holding it would lose it outright.
///
/// <para>PR #238 review follow-up. That window exists so an informational callout can't
/// cut off a safety-critical route-reach/unmapped-start warning spoken at guidance
/// start. Before PR #235 it gated exactly one thing — the "Crossing taxiway X" callout
/// — which SKIPS while the window is open (see
/// <c>TaxiGuidanceManager.TryAnnounceCrossing</c>; deliberate, and left untouched by
/// this class). PR #235 widened the window from 8.0s to 12.5s and additionally gated
/// three callouts that WAIT instead of skipping: the final-destination "X ahead.", the
/// advance turn notice, and the curve cue — each behind a raw
/// <c>DateTime.UtcNow >= _startChatterSuppressUntil</c> check.</para>
///
/// <para>Waiting is not free for those three. The advance notice is the only setter of
/// <c>_approachAnnounced</c>, and <c>AdvanceSegment</c> clears both that latch and
/// <c>_turnImminentAnnounced</c> the instant the aircraft passes the junction they
/// describe — so a junction reached INSIDE the window permanently loses "turn left onto
/// taxiway B" AND "turn left now," not merely delayed. The same mechanism silently
/// drops the destination-ahead callout on any route shorter than
/// <c>APPROACH_ANNOUNCE_DISTANCE_M</c> (100m) — exactly the short bridged-stand routes
/// PR #235 itself creates.</para>
///
/// <para>The fix: hold a callout only when it can still be delivered in time. Project
/// the aircraft's ground speed forward to the moment the window closes; if it would
/// still be short of the point the callout describes, waiting costs nothing — the
/// callout fires normally once the window is over. If it would already be there (or
/// past it), speak now instead of losing the callout for good. Pure —
/// <c>StartWarningChatterGateTests</c>.</para>
/// </summary>
public static class StartWarningChatterGate
{
    /// <summary>1 knot in metres/second — the conversion already used throughout
    /// <c>TaxiGuidanceManager</c> (e.g. its advance-notice and turn-imminent
    /// lead-distance math).</summary>
    private const double MetersPerSecondPerKnot = 0.5144;

    /// <param name="nowUtc">The current time.</param>
    /// <param name="suppressUntilUtc">When the start-warning window closes.</param>
    /// <param name="distanceToTargetMeters">How far the aircraft currently is from the
    /// point the held callout describes (the turn junction, the destination, the curve's
    /// scan window, ...). Must be a positive, finite number to reason about — anything
    /// else means "speak now."</param>
    /// <param name="groundSpeedKts">The aircraft's current ground speed. NaN is treated
    /// as stationary, never as "reaches the target instantly" — an unread speed must
    /// never be the reason a callout is dropped.</param>
    /// <returns>True when the callout can wait for the window to close and still be
    /// delivered in time; false when it must be spoken immediately.</returns>
    public static bool ShouldHold(
        DateTime nowUtc, DateTime suppressUntilUtc,
        double distanceToTargetMeters, double groundSpeedKts)
    {
        // Window already closed: nothing left to hold for.
        if (nowUtc >= suppressUntilUtc) return false;

        // A distance we cannot reason about — the target is already behind the
        // aircraft (<= 0), or the geometry is undefined (NaN/infinite) — must never be
        // used to justify a hold. Speak now rather than gamble on bad geometry.
        if (!double.IsFinite(distanceToTargetMeters) || distanceToTargetMeters <= 0)
            return false;

        // An unread ground speed must never be read as "closing in fast" — that would
        // drop the very callout this gate exists to protect. Treat it as stationary.
        double speedKts = double.IsNaN(groundSpeedKts) ? 0.0 : groundSpeedKts;

        // A stationary (or reversing) aircraft reaches nothing before the window
        // closes, so holding costs nothing.
        if (speedKts <= 0) return true;

        double remainingSeconds = (suppressUntilUtc - nowUtc).TotalSeconds;
        double metersCoveredBeforeWindowCloses =
            speedKts * MetersPerSecondPerKnot * remainingSeconds;

        // Still short of the target when the window closes -> safe to hold; it fires
        // normally once the window is over. At or past the target -> holding would
        // either lose the callout outright (the latches it depends on get cleared the
        // instant the target is reached) or describe a point already behind the
        // aircraft, so speak now instead.
        return metersCoveredBeforeWindowCloses < distanceToTargetMeters;
    }
}
