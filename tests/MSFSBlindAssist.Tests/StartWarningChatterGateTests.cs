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
}
