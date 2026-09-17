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
/// <c>_turnImminentAnnounced</c> the instant the aircraft comes within
/// <c>WAYPOINT_CAPTURE_RADIUS_M</c> (25m) of the junction they describe — NOT when the
/// junction itself is reached — so a junction reached INSIDE the window permanently
/// loses "turn left onto taxiway B" AND "turn left now," not merely delayed. The same
/// mechanism silently drops the destination-ahead callout on any route shorter than
/// <c>APPROACH_ANNOUNCE_DISTANCE_M</c> (100m) — exactly the short bridged-stand routes
/// PR #235 itself creates.</para>
///
/// <para>The fix: hold a callout only when it can still be delivered in time. Project
/// the aircraft's ground speed forward to the moment the window closes; if it would
/// still be short of the point the callout describes, waiting costs nothing — the
/// callout fires normally once the window is over. If it would already be there (or
/// past it), speak now instead of losing the callout for good. Pure —
/// <c>StartWarningChatterGateTests</c>.</para>
///
/// <para>PR #238 review-of-the-review: the first cut of this class measured "there" as
/// the raw junction/target distance, but that is not where the callout is actually
/// lost — the true clearing point sits short of it by whatever radius fires first, e.g.
/// <c>WAYPOINT_CAPTURE_RADIUS_M</c> for a mid-route junction or the destination's own
/// arrival radius (<c>ARRIVAL_RADIUS_M</c> / <c>GATE_ARRIVAL_RADIUS_FEET</c> /
/// <c>LANDING_EXIT_ARRIVAL_RADIUS_M</c>) on the final segment. Under constant velocity,
/// hold-vs-speak-now is a straight line in (distance, speed) space, so measuring to the
/// wrong point does not merely shift that line — it opens a whole BAND of (distance,
/// speed) pairs where this method still says "hold" after the real clearing point has
/// already been passed underneath it. Reviewer's worked example: a 45m junction at 5kt,
/// 12.5s window — 5kt covers 32.15m by the time the window closes, comfortably under
/// the raw 45m (says hold), but the 25m-early latch-clear is reached at just 7.78s,
/// well before the window's own close. The <c>clearRadiusMeters</c> parameter closes
/// that band by measuring to (junction distance - clear radius) instead of to the
/// junction. The curve cue has no early-clearing LATCH to protect against — nothing
/// clears <c>_curveAnnouncedSign</c> early — so it passes <c>clearRadiusMeters: 0</c>;
/// what its call site actually needed fixed was passing the real, live distance to the
/// next bend instead of a fixed 100m scan-window constant. (A zero clear radius does
/// not mean the curve cue is immune to being lost — <c>TaxiGuidanceManager.
/// TryAnnounceCurve</c>'s own remarks cover the real mechanism: the GEOMETRY itself
/// stops qualifying once <c>AdvanceSegment</c> moves the cumulative-turn scan's anchor
/// point forward, not a latch reset.)</para>
///
/// <para>PR #238 second-round review, MINOR finding: this class used to split the above
/// across a four-argument base method plus a five-argument radius-aware wrapper that
/// subtracted the radius and delegated. That split was itself a footgun — a caller that
/// forgot the radius argument bound SILENTLY to the shorter overload and reintroduced
/// the Critical defect above with no compile error to catch it (CLAUDE.md documents the
/// identical hazard for the A380 <c>Configure</c> method: "a second one differing only
/// by a trailing double binds silently"). Merged into the single method below — every
/// caller now states its clear radius explicitly, passing 0 when none applies rather
/// than reaching for a shorter overload that no longer exists.</para>
/// </summary>
public static class StartWarningChatterGate
{
    /// <summary>1 knot in metres/second. Public so it is a single source of truth
    /// (PR #238 review, MINOR finding): <c>TaxiGuidanceManager.Announcements.cs</c>'s
    /// advance-notice and turn-imminent lead-distance math now initialise their own
    /// <c>0.5144</c> literals from this constant instead of repeating it, following the
    /// pattern in <c>RolloutExitGate</c>.</summary>
    public const double MetersPerSecondPerKnot = 0.5144;

    /// <param name="nowUtc">The current time.</param>
    /// <param name="suppressUntilUtc">When the start-warning window closes.</param>
    /// <param name="distanceToJunctionMeters">How far the aircraft currently is from the
    /// point NAMED by the callout (the junction it will turn/change taxiway at, or the
    /// destination) -- the raw geometric distance, before accounting for whatever clears
    /// the callout early.</param>
    /// <param name="clearRadiusMeters">How far short of <paramref
    /// name="distanceToJunctionMeters"/> reaching zero the callout is actually lost --
    /// e.g. <c>WAYPOINT_CAPTURE_RADIUS_M</c> for the advance-notice/turn-imminent
    /// junction (<c>AdvanceSegment</c> clears their latches there, 25m short of it) or
    /// the destination's own arrival radius on the final segment. Pass 0 for a callout
    /// with no such radius (the curve cue) -- it is a pure pass-through to the
    /// arithmetic below, not a special case, and there is deliberately no separate
    /// shorter overload to reach for instead (PR #238 second-round review, MINOR
    /// finding: the previous two-overload split let a caller who forgot this argument
    /// bind silently to the shorter one and lose the radius adjustment entirely).</param>
    /// <param name="groundSpeedKts">The aircraft's current ground speed. NaN is treated
    /// as stationary, never as "reaches the target instantly" — an unread speed must
    /// never be the reason a callout is dropped.</param>
    /// <returns>True when the callout can wait for the window to close and still be
    /// delivered in time; false when it must be spoken immediately.</returns>
    public static bool ShouldHold(
        DateTime nowUtc, DateTime suppressUntilUtc,
        double distanceToJunctionMeters, double clearRadiusMeters, double groundSpeedKts)
    {
        // Measure to the point that actually clears the callout, not to the raw
        // junction/destination distance -- see the "review-of-the-review" class
        // remarks above. A callout with no such radius passes clearRadiusMeters: 0,
        // which leaves this unchanged.
        double distanceToTargetMeters = distanceToJunctionMeters - clearRadiusMeters;

        // Window already closed: nothing left to hold for.
        if (nowUtc >= suppressUntilUtc) return false;

        // An unread ground speed must never be read as "closing in fast" — that would
        // drop the very callout this gate exists to protect. Treat it as stationary.
        double speedKts = double.IsNaN(groundSpeedKts) ? 0.0 : groundSpeedKts;

        // A stationary (or reversing) aircraft reaches nothing before the window
        // closes, so holding costs nothing -- WHATEVER the distance says, including a
        // negative, NaN, or infinite one. This check must run BEFORE the distance guard
        // below (PR #238 second-round review, CRITICAL): the guard's own comment says it
        // exists for "geometry we cannot reason about," but a parked aircraft's geometry
        // is perfectly well understood -- it is not going to reach anything before the
        // window closes, so there is no time pressure to protect against, and holding a
        // callout it will never lose costs nothing. The reviewer's failure case: a
        // parked aircraft (0 kt) at a bridged stand with segment 0 ending 20m away at a
        // junction (WAYPOINT_CAPTURE_RADIUS_M 25m) computes an adjusted distance of
        // 20 - 25 = -5. With the distance guard checked first that tripped "speak now"
        // on a start-warning safety callout for an aircraft that had not moved at all.
        if (speedKts <= 0) return true;

        // A distance we cannot reason about — the target is already behind the
        // aircraft (<= 0), or the geometry is undefined (NaN/infinite) — must never be
        // used to justify a hold for a MOVING aircraft. Speak now rather than gamble on
        // bad geometry. (A stationary aircraft never reaches this line — see above.)
        if (!double.IsFinite(distanceToTargetMeters) || distanceToTargetMeters <= 0)
            return false;

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
