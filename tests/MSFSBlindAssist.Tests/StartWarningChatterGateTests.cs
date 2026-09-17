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

    [Fact]
    public void An_already_closed_window_always_speaks_now()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(-1), distanceToTargetMeters: 50, groundSpeedKts: 10));

    [Fact]
    public void A_window_closing_at_exactly_now_counts_as_closed()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now, distanceToTargetMeters: 50, groundSpeedKts: 10));

    [Fact]
    public void A_stationary_aircraft_can_always_wait_out_an_open_window()
        => Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: 50, groundSpeedKts: 0));

    [Fact]
    public void A_reversing_aircraft_counts_as_stationary_too()
        => Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: 50, groundSpeedKts: -3));

    [Fact]
    public void Still_short_of_the_target_when_the_window_closes_can_wait()
    {
        // 10 kts * 0.5144 m/s per kt * 5 s = 25.72 m covered; a 50 m target is still ahead.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: 50, groundSpeedKts: 10));
    }

    [Fact]
    public void Reaching_the_target_before_the_window_closes_speaks_now_instead_of_losing_it()
    {
        // 10 kts * 0.5144 m/s per kt * 5 s = 25.72 m covered; a 20 m target is already
        // behind the aircraft by the time the window would close.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: 20, groundSpeedKts: 10));
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
            Now, Now.AddSeconds(10), distanceToTargetMeters: covered, groundSpeedKts: 10));
    }

    [Fact]
    public void A_zero_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: 0, groundSpeedKts: 10));

    [Fact]
    public void A_negative_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: -10, groundSpeedKts: 10));

    [Fact]
    public void A_NaN_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: double.NaN, groundSpeedKts: 10));

    [Fact]
    public void An_infinite_distance_never_waits()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: double.PositiveInfinity, groundSpeedKts: 10));

    [Fact]
    public void A_NaN_ground_speed_is_treated_as_stationary_not_as_reaches_instantly()
        => Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5), distanceToTargetMeters: 50, groundSpeedKts: double.NaN));

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
    // Two of the three sites (advance notice, destination-ahead) are fixed by the new
    // five-argument ShouldHold overload below, which subtracts an explicit clear radius
    // before delegating to the four-argument method pinned above. The third (curve cue)
    // needed no radius -- nothing clears _curveAnnouncedSign early -- it needed the
    // real, live distance instead of a fixed scan-window constant, so those tests
    // characterize the plain four-argument overload directly.
    // -------------------------------------------------------------------------------

    [Fact]
    public void Radius_aware_overload_still_speaks_now_once_the_window_is_closed()
        => Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(-1),
            distanceToJunctionMeters: 45, clearRadiusMeters: 25, groundSpeedKts: 5));

    [Fact]
    public void Radius_aware_overload_with_zero_radius_matches_the_plain_overload()
    {
        // clearRadiusMeters: 0 must reduce to exactly the four-argument behaviour --
        // same case as Still_short_of_the_target_when_the_window_closes_can_wait above.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5),
            distanceToJunctionMeters: 50, clearRadiusMeters: 0, groundSpeedKts: 10));
        // Same case as Reaching_the_target_before_the_window_closes_speaks_now_instead_of_losing_it.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5),
            distanceToJunctionMeters: 20, clearRadiusMeters: 0, groundSpeedKts: 10));
    }

    [Fact]
    public void A_clear_radius_that_already_reaches_the_junction_speaks_now_even_when_stationary()
    {
        // distanceToJunctionMeters - clearRadiusMeters = 20 - 25 = -5: the aircraft is
        // already inside the radius that clears the callout, so this must speak now --
        // even though an unshifted distance of 20 at 0 kt would otherwise "wait forever"
        // (A_stationary_aircraft_can_always_wait_out_an_open_window above). The
        // non-positive-distance guard in the four-argument overload must fire BEFORE the
        // stationary-speed shortcut, and this proves the radius-aware overload still
        // reaches it after subtracting.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(5),
            distanceToJunctionMeters: 20, clearRadiusMeters: 25, groundSpeedKts: 0));
    }

    // ---- CRITICAL finding: the advance-notice / turn-imminent junction is cleared by
    // AdvanceSegment 25m (WAYPOINT_CAPTURE_RADIUS_M) EARLY, not at distance zero.

    [Fact]
    public void Reviewers_45m_junction_5kt_case_speaks_now_once_the_capture_radius_is_applied()
    {
        // Reviewer's worked failure (task-1 review, CRITICAL): a 45m junction, steady
        // 5kt (2.572 m/s), this class's 12.5s window. 5kt covers 32.15m by the time the
        // window closes -- comfortably under the raw 45m, so the OLD call (bare
        // distance, see the next test) said "hold". But AdvanceSegment clears
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
        // Same scenario through the OLD (bare four-argument) call the advance-notice
        // site used to make -- distanceToTargetMeters: 45 (the raw junction distance,
        // no adjustment). 5kt covers 32.15m in 12.5s, still short of 45m, so this says
        // "hold" -- reproducing the exact defect the fix above closes. Kept as a
        // permanent regression marker for the four-argument overload's own arithmetic.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToTargetMeters: 45, groundSpeedKts: 5));
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
        // the OLD bare-distance call said "hold" even though the 25m-early clear had
        // already fired. At 6.9 kt, deep inside that old band, coverage is 6.9 * 0.5144
        // * 12.5 = 44.37m -- just under the raw 45m (the old call still wrongly holds
        // here, see the contrast assertion below) but comfortably past the true 20m
        // loss point. The fix must speak now even this close to the old band's edge.
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToJunctionMeters: 45, clearRadiusMeters: 25, groundSpeedKts: 6.9));
        // Contrast: the old bare-distance call at the same speed/window still (wrongly) holds.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToTargetMeters: 45, groundSpeedKts: 6.9));
    }

    // ---- IMPORTANT finding: the destination-ahead callout's clearing point on the
    // final segment is the ARRIVAL radius (gate or runway lineup), not distance zero.

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
        // Same scenario through the bare four-argument call the destination-ahead site
        // used to make: distanceToTargetMeters: 15 (raw), no radius. 12.86m covered is
        // still short of 15m, so the old call says "hold" -- the defect the fix above
        // closes.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToTargetMeters: 15, groundSpeedKts: 2.0));
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
            distanceToTargetMeters: 20, groundSpeedKts: 1.5));
    }

    // ---- IMPORTANT finding: the curve cue's call site passed the fixed
    // CURVE_SCAN_WINDOW_M (100m) constant instead of the real, live distance to the
    // next bend. No radius is involved -- AdvanceSegment never resets
    // _curveAnnouncedSign -- so these characterize the four-argument overload directly,
    // at the numbers from the review.

    [Fact]
    public void Old_curve_site_constant_holds_for_the_entire_window_at_normal_taxi_speed()
    {
        // At 10 kt (well under the ~15.55kt threshold used below), CURVE_SCAN_WINDOW_M
        // = 100 is so far beyond anything 10 kt can cover in 12.5s that ShouldHold says
        // "hold" from the moment the window opens...
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToTargetMeters: 100, groundSpeedKts: 10));
        // ...and keeps saying "hold" all the way down to half a second before it
        // closes -- "holds for the WHOLE window", exactly as the review describes. The
        // fixed 100m constant carries no information about where the aircraft actually
        // is, so at any normal taxi speed gating the curve cue did nothing at this site.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(0.5),
            distanceToTargetMeters: 100, groundSpeedKts: 10));
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
            distanceToTargetMeters: 20, groundSpeedKts: 10));
    }

    [Fact]
    public void Old_curve_site_constant_was_the_only_non_monotonic_call_site()
    {
        // At 20 kt (above the ~15.55kt threshold = 100m / (0.5144 * 12.5s)), the full
        // 12.5s window covers 128.6m -- already past the fixed 100m constant -- so
        // ShouldHold says "speak now" at the very top of the window...
        Assert.False(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(12.5),
            distanceToTargetMeters: 100, groundSpeedKts: 20));
        // ...yet with only 4.5s left in that SAME window, 20 kt covers just 46.3m --
        // back under 100m -- so it flips to "hold". A per-frame caller would hear
        // "speak now" then "hold" a moment later with the aircraft no better off: the
        // review's point (b), and why a fixed-distance argument can never be right here.
        Assert.True(StartWarningChatterGate.ShouldHold(
            Now, Now.AddSeconds(4.5),
            distanceToTargetMeters: 100, groundSpeedKts: 20));
    }
}
