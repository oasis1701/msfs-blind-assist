// Whether a taxi-guidance callout may wait out the start-warning chatter window
// (TaxiGuidanceManager's _startChatterSuppressUntil) or must be spoken right away.
//
// PR #238 review follow-up. PR #235 widened that window from 8.0s to 12.5s and, new in
// that PR, started gating three callouts in TaxiGuidanceManager.Announcements.cs with a
// raw `DateTime.UtcNow >= _startChatterSuppressUntil` check that WAITS rather than
// SKIPS: the final-destination "X ahead.", the advance turn notice, and the curve cue.
// But the advance notice is the only setter of _approachAnnounced, and AdvanceSegment
// clears both _approachAnnounced and _turnImminentAnnounced the instant the aircraft
// passes the junction it describes -- so a junction reached INSIDE the window loses
// both "turn left onto taxiway B" AND "turn left now" permanently, not just late. The
// same mechanism silently drops the destination-ahead callout on any route shorter
// than APPROACH_ANNOUNCE_DISTANCE_M (100m) -- exactly the short bridged-stand routes
// PR #235 itself creates.
//
// The fix: only hold a callout when it can still be delivered before the aircraft
// reaches the point it describes. Project the ground speed forward to the moment the
// window closes; if the aircraft would already be at or past the target by then, speak
// now instead of losing the callout outright.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class StartWarningChatterGateTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The first four tests below exercise the base arithmetic (window/stationary/speed)
    // with clearRadiusMeters: 0 -- there is no early-clearing radius to model here, so 0
    // is the plain pass-through documented on the parameter, not a special case.

    [Fact]
    public void An_already_closed_window_always_speaks_now()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(-1), distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: 10));

    [Fact]
    public void A_window_closing_at_exactly_now_counts_as_closed()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now, distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: 10));

    [Fact]
    public void A_stationary_aircraft_can_always_wait_out_an_open_window()
        => Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: 0));

    [Fact]
    public void A_reversing_aircraft_counts_as_stationary_too()
        => Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: -3));

    [Fact]
    public void Still_short_of_the_target_when_the_window_closes_can_wait()
    {
        // 10 kts * 0.5144 m/s per kt * 5 s = 25.72 m covered; a 50 m target is still ahead.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: 10));
    }

    [Fact]
    public void Reaching_the_target_before_the_window_closes_speaks_now_instead_of_losing_it()
    {
        // 10 kts * 0.5144 m/s per kt * 5 s = 25.72 m covered; a 20 m target is already
        // behind the aircraft by the time the window would close.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: 20, clearRadiusMeters: 0, groundSpeedKts: 10));
    }

    [Fact]
    public void Landing_exactly_on_the_target_when_the_window_closes_speaks_now()
    {
        // Same formula as production, so this is bit-for-bit the distance covered in
        // 10s at 10kts -- the boundary where the aircraft arrives exactly as the window
        // closes. Treated as "reaches", not "still short", so it speaks now rather than
        // risk losing the callout to the boundary.
        double covered = 10.0 * 0.5144 * 10.0;
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(10), distanceToJunctionMeters: covered, clearRadiusMeters: 0, groundSpeedKts: 10));
    }

    [Fact]
    public void A_zero_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: 0, clearRadiusMeters: 0, groundSpeedKts: 10));

    [Fact]
    public void A_negative_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: -10, clearRadiusMeters: 0, groundSpeedKts: 10));

    [Fact]
    public void A_NaN_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: double.NaN, clearRadiusMeters: 0, groundSpeedKts: 10));

    [Fact]
    public void An_infinite_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: double.PositiveInfinity, clearRadiusMeters: 0, groundSpeedKts: 10));

    [Fact]
    public void A_NaN_ground_speed_is_treated_as_stationary_not_as_reaches_instantly()
        => Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: double.NaN));

    // -------------------------------------------------------------------------------
    // PR #238 review-of-the-review. ShouldHold's own arithmetic (above) was correct,
    // but three call sites in TaxiGuidanceManager.Announcements.cs fed it the wrong
    // DISTANCE -- the raw distance to the junction/target, not the distance to the
    // point that actually clears the callout's backing latch(es) early. Under constant
    // velocity, hold-vs-speak-now is a straight line in (distance, speed) space, so
    // measuring to the wrong point doesn't just shift that line -- it opens a whole BAND
    // of (distance, speed) pairs where ShouldHold still says "hold" after the real
    // clearing point has already gone by underneath it.
    //
    // Two of the three sites (advance notice, destination-ahead) are fixed by passing an
    // explicit clearRadiusMeters that ShouldHold subtracts before running its
    // arithmetic. The third (curve cue) needed no radius -- nothing clears
    // _curveAnnouncedSign early -- it needed the real, live distance instead of a fixed
    // scan-window constant, so it passes clearRadiusMeters: 0 and those tests
    // characterize the plain distance/speed arithmetic directly.
    //
    // (PR #238 second-round review, MINOR finding: this method used to be split across a
    // four-argument base overload and a five-argument radius-aware wrapper that
    // subtracted the radius and delegated to it -- a footgun, since a caller who forgot
    // the radius argument bound silently to the shorter overload with no compile error.
    // The two are now one method; every test below states clearRadiusMeters explicitly,
    // including the ones pinning what the OLD, radius-less call sites used to compute.)
    // -------------------------------------------------------------------------------

    [Fact]
    public void A_closed_window_speaks_now_even_with_a_clear_radius_applied()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(-1),
            distanceToJunctionMeters: 45, clearRadiusMeters: 25, groundSpeedKts: 5));

    [Fact]
    public void A_zero_clear_radius_reduces_to_the_raw_distance_arithmetic()
    {
        // clearRadiusMeters: 0 must reduce to exactly the same arithmetic as the
        // radius-less tests above -- same cases as
        // Still_short_of_the_target_when_the_window_closes_can_wait and
        // Reaching_the_target_before_the_window_closes_speaks_now_instead_of_losing_it.
        // Before the overload merge this pinned parity between two separate methods;
        // now it pins that clearRadiusMeters: 0 is a pure pass-through within the one
        // method, kept as an explicit regression marker for that property.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5),
            distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: 10));
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5),
            distanceToJunctionMeters: 20, clearRadiusMeters: 0, groundSpeedKts: 10));
    }

    // ---- PR #238 second-round review, CRITICAL finding: a STATIONARY aircraft must
    // HOLD no matter how negative the clear-radius-adjusted distance is. The first cut
    // of the radius-aware overload subtracted the radius and then ran the non-positive-
    // distance guard BEFORE the stationary-speed shortcut, so a parked aircraft sitting
    // inside a junction's capture radius (ordinary bridged-stand geometry: segment 0
    // ends 20m away at a junction, WAYPOINT_CAPTURE_RADIUS_M is 25m) spoke NOW instead of
    // holding -- AnnounceInstruction -> AnnounceImmediate, cutting the start-warning
    // safety callout off mid-word for an aircraft that had not moved an inch and was
    // never going to reach anything before the window closed. There is no time pressure
    // to protect against when ground speed is zero, because the aircraft cannot reach
    // the point being measured to -- "geometry we cannot reason about" (the reason the
    // non-positive/non-finite guard exists) does not describe a parked aircraft; its
    // geometry is perfectly well understood; it just never arrives. The fix moves the
    // stationary check ABOVE the distance guard so it wins regardless of the distance's
    // sign or finiteness.
    [Fact]
    public void A_stationary_aircraft_holds_even_when_the_clear_radius_already_reaches_the_junction()
    {
        // distanceToJunctionMeters - clearRadiusMeters = 20 - 25 = -5: the aircraft is
        // already inside the radius that clears the callout, but at 0 kt it will never
        // get any closer to it either -- so this must HOLD, not speak now. This is the
        // exact reviewer-reported regression: a parked aircraft at a bridged stand with
        // an unmapped-start warning pending must never have its safety warning cut off
        // by a callout that was never going anywhere.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5),
            distanceToJunctionMeters: 20, clearRadiusMeters: 25, groundSpeedKts: 0));
    }

    [Fact]
    public void A_moving_aircraft_with_an_already_reached_clear_radius_still_speaks_now()
    {
        // Same geometry as immediately above (20 - 25 = -5), but MOVING (5 kt) instead
        // of parked. This is the Critical fix from the first review round and it must
        // not regress when the stationary check moves ahead of the distance guard: a
        // moving aircraft that has already reached (or passed) the point that clears the
        // callout must still speak now, because it really is closing in and waiting
        // really would lose the callout.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5),
            distanceToJunctionMeters: 20, clearRadiusMeters: 25, groundSpeedKts: 5));
    }

    // ---- CRITICAL finding: the advance-notice / turn-imminent junction is cleared by
    // AdvanceSegment 25m (WAYPOINT_CAPTURE_RADIUS_M) EARLY, not at distance zero.

    [Fact]
    public void Reviewers_45m_junction_5kt_case_speaks_now_once_the_capture_radius_is_applied()
    {
        // Reviewer's worked failure (task-1 review, CRITICAL): a 45m junction, steady
        // 5kt (2.572 m/s), this class's 12.5s window. 5kt covers 32.15m by the time the
        // window closes -- comfortably under the raw 45m, so the OLD call (clearRadius
        // 0, see the next test) said "hold". But AdvanceSegment clears
        // _approachAnnounced/_turnImminentAnnounced 25m EARLY, at 20m, which 5kt covers
        // in just 7.78s -- well inside the 12.5s window. The callout was gone before the
        // "hold" it was granted ever paid off. Feeding the gate the capture radius fixes
        // it: effective distance 45-25=20m, and 32.15m covered >= 20m, so this must say
        // "speak now" instead.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 45, clearRadiusMeters: 25, groundSpeedKts: 5));
    }

    [Fact]
    public void Reviewers_45m_junction_5kt_case_would_have_wrongly_held_without_the_radius_fix()
    {
        // Same scenario through the OLD call the advance-notice site used to make
        // before the radius fix -- clearRadiusMeters: 0, i.e. distanceToJunctionMeters:
        // 45 (the raw junction distance) with nothing subtracted. 5kt covers 32.15m in
        // 12.5s, still short of 45m, so this says "hold" -- reproducing the exact defect
        // the fix above closes. Kept as a permanent regression marker for the plain
        // distance/speed arithmetic on its own, radius-less.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 45, clearRadiusMeters: 0, groundSpeedKts: 5));
    }

    [Fact]
    public void Just_below_the_new_threshold_speed_can_still_hold()
    {
        // The new hold/speak-now threshold speed is (junction - radius) / (kt-to-m/s *
        // window) = 20 / (0.5144 * 12.5) = 3.11 kt. At 3.0 kt, coverage is 3.0 * 0.5144
        // * 12.5 = 19.29m, still short of the 20m true loss point -- safe to hold.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 45, clearRadiusMeters: 25, groundSpeedKts: 3.0));
    }

    [Fact]
    public void Just_above_the_new_threshold_speed_speaks_now()
    {
        // At 3.2 kt, coverage is 3.2 * 0.5144 * 12.5 = 20.576m -- past the 20m true loss
        // point, so this must speak now rather than hold into the loss band.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 45, clearRadiusMeters: 25, groundSpeedKts: 3.2));
    }

    [Fact]
    public void Near_the_old_loss_bands_former_upper_edge_the_fix_still_speaks_now()
    {
        // Reviewer's demonstrated loss band was 3.11-7.00 kt -- the whole range where
        // the OLD radius-less call said "hold" even though the 25m-early clear had
        // already fired. At 6.9 kt, deep inside that old band, coverage is 6.9 * 0.5144
        // * 12.5 = 44.37m -- just under the raw 45m (the old call still wrongly holds
        // here, see the contrast assertion below) but comfortably past the true 20m
        // loss point. The fix must speak now even this close to the old band's edge.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 45, clearRadiusMeters: 25, groundSpeedKts: 6.9));
        // Contrast: the old radius-less call at the same speed/window still (wrongly) holds.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 45, clearRadiusMeters: 0, groundSpeedKts: 6.9));
    }

    // ---- IMPORTANT finding: the destination-ahead callout's clearing point on the
    // final segment is the ARRIVAL radius (gate, runway lineup, or landing exit), not
    // distance zero.

    [Fact]
    public void Destination_ahead_gate_radius_case_speaks_now_once_the_arrival_radius_is_applied()
    {
        // GATE_ARRIVAL_RADIUS_FEET (20ft) / METERS_TO_FEET (3.28084) = ~6.096m -- the
        // radius UpdatePosition's own "arrived" check uses for a gate destination. A
        // 15m destination distance at 2.0 kt covers 2.0 * 0.5144 * 12.5 = 12.86m by the
        // time the window closes -- short of the raw 15m (old call held, see the next
        // test) but past the true (15 - 6.096) = 8.9m loss point, so this must speak now.
        const double gateArrivalRadiusM = 20.0 / 3.28084; // TaxiGuidanceManager.GATE_ARRIVAL_RADIUS_FEET / METERS_TO_FEET
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 15, clearRadiusMeters: gateArrivalRadiusM, groundSpeedKts: 2.0));
    }

    [Fact]
    public void Destination_ahead_gate_radius_case_would_have_wrongly_held_on_the_raw_distance()
    {
        // Same scenario through the radius-less call the destination-ahead site used to
        // make: distanceToJunctionMeters: 15 (raw), clearRadiusMeters: 0. 12.86m covered
        // is still short of 15m, so the old call says "hold" -- the defect the fix above
        // closes.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 15, clearRadiusMeters: 0, groundSpeedKts: 2.0));
    }

    [Fact]
    public void Destination_ahead_runway_lineup_radius_case_speaks_now_once_applied()
    {
        // ARRIVAL_RADIUS_M (12m) is the radius UpdatePosition uses for a runway-lineup
        // destination. A 20m destination distance at 1.5 kt covers 1.5 * 0.5144 * 12.5 =
        // 9.645m by the time the window closes -- short of the raw 20m (old call held,
        // see the next test) but past the true (20 - 12) = 8m loss point.
        const double runwayArrivalRadiusM = 12.0; // TaxiGuidanceManager.ARRIVAL_RADIUS_M
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 20, clearRadiusMeters: runwayArrivalRadiusM, groundSpeedKts: 1.5));
    }

    [Fact]
    public void Destination_ahead_runway_lineup_radius_case_would_have_wrongly_held_on_the_raw_distance()
    {
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 20, clearRadiusMeters: 0, groundSpeedKts: 1.5));
    }

    // ---- IMPORTANT finding: the curve cue's call site passed the fixed
    // CURVE_SCAN_WINDOW_M (100m) constant instead of the real, live distance to the
    // next bend. No radius is involved -- AdvanceSegment never resets
    // _curveAnnouncedSign, so the curve callout is never lost via a latch -- but it IS
    // lost geometrically once AdvanceSegment moves the cumulative-turn scan's anchor
    // forward, which is why the real, live distance (not the fixed window) is what
    // matters. These characterize the plain distance/speed arithmetic (clearRadiusMeters:
    // 0) directly, at the numbers from the review.

    [Fact]
    public void Old_curve_site_constant_holds_for_the_entire_window_at_normal_taxi_speed()
    {
        // At 10 kt (well under the ~15.55kt threshold used below), CURVE_SCAN_WINDOW_M
        // = 100 is so far beyond anything 10 kt can cover in 12.5s that ShouldHold says
        // "hold" from the moment the window opens...
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 100, clearRadiusMeters: 0, groundSpeedKts: 10));
        // ...and keeps saying "hold" all the way down to half a second before it
        // closes -- "holds for the WHOLE window", exactly as the review describes. The
        // fixed 100m constant carries no information about where the aircraft actually
        // is, so at any normal taxi speed gating the curve cue did nothing at this site.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(0.5),
            distanceToJunctionMeters: 100, clearRadiusMeters: 0, groundSpeedKts: 10));
    }

    [Fact]
    public void Real_bend_distance_can_still_speak_now_where_the_fixed_constant_could_not()
    {
        // Same speed and window as above, but the REAL distance to a close bend (20m,
        // comfortably inside CURVE_SCAN_WINDOW_M's 100m) rather than the constant. 10 kt
        // covers 64.3m by the time the window closes -- past the real 20m bend -- so
        // this must speak now. This is the fix: TryAnnounceCurve now passes
        // distToTargetM here, not CURVE_SCAN_WINDOW_M.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 20, clearRadiusMeters: 0, groundSpeedKts: 10));
    }

    [Fact]
    public void Old_curve_site_constant_was_the_only_non_monotonic_call_site()
    {
        // At 20 kt (above the ~15.55kt threshold = 100m / (0.5144 * 12.5s)), the full
        // 12.5s window covers 128.6m -- already past the fixed 100m constant -- so
        // ShouldHold says "speak now" at the very top of the window...
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 100, clearRadiusMeters: 0, groundSpeedKts: 20));
        // ...yet with only 4.5s left in that SAME window, 20 kt covers just 46.3m --
        // back under 100m -- so it flips to "hold". A per-frame caller would hear
        // "speak now" then "hold" a moment later with the aircraft no better off: the
        // review's point (b), and why a fixed-distance argument can never be right here.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(4.5),
            distanceToJunctionMeters: 100, clearRadiusMeters: 0, groundSpeedKts: 20));
    }
}
