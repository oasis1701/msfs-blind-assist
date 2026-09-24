// Proximity escalation guards re-sized for the 1 s cadence. PR #247 review R8 (the 20 ft per
// evaluation moving-away hysteresis, sized for 3 s, went 3× stricter → "Stop" for an aircraft pulling
// away; Caution/Warning re-fired as the speed-scaled boundary moved) and R9 (converging from behind).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ProximityEscalationTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_leader_pulling_away_is_moving_away_at_the_one_second_cadence()
    {
        // Review scenario: 300 → 315 → 327 → 335 ft, one second apart. The old per-evaluation
        // 20 ft rule said "not moving away" every second; per second it is 15, 12, 8 ft/s.
        Assert.True(GroundTrafficLogic.IsMovingAway(300, T0, 315, T0.AddSeconds(1)));
        Assert.True(GroundTrafficLogic.IsMovingAway(315, T0, 327, T0.AddSeconds(1)));
        Assert.True(GroundTrafficLogic.IsMovingAway(327, T0, 335, T0.AddSeconds(1)));
    }

    [Fact]
    public void The_three_second_meaning_is_unchanged()
    {
        Assert.True(GroundTrafficLogic.IsMovingAway(300, T0, 320, T0.AddSeconds(3)));
        Assert.False(GroundTrafficLogic.IsMovingAway(300, T0, 319, T0.AddSeconds(3)));
    }

    [Theory]
    [InlineData(300, 300)]   // stationary
    [InlineData(300, 280)]   // closing
    public void Not_opening_is_not_moving_away(double before, double after)
        => Assert.False(GroundTrafficLogic.IsMovingAway(before, T0, after, T0.AddSeconds(1)));

    [Fact]
    public void Without_a_previous_sample_nothing_is_moving_away()
    {
        Assert.False(GroundTrafficLogic.IsMovingAway(double.MaxValue, DateTime.MinValue, 300, T0));
        Assert.False(GroundTrafficLogic.IsMovingAway(double.NaN, T0, 300, T0.AddSeconds(1)));
        Assert.False(GroundTrafficLogic.IsMovingAway(200, T0, 300, T0));   // no time elapsed
    }

    // ── escalation ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_first_awareness_ping_is_announced()
        => Assert.True(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Awareness, GroundZone.None, GroundZone.None, DateTime.MinValue, T0));

    [Fact]
    public void Awareness_is_not_repeated_within_fifteen_seconds()
        => Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Awareness, GroundZone.None, GroundZone.Awareness, T0, T0.AddSeconds(10)));

    [Fact]
    public void Complying_with_slow_down_does_not_earn_another_slow_down()
    {
        // "Slow down" at T0; the pilot slows, the boundary retreats, the zone silently drops to
        // Awareness; five seconds later the aircraft is back inside the (new) caution boundary.
        Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Caution, GroundZone.Awareness, GroundZone.Caution, T0, T0.AddSeconds(5)));
        Assert.True(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Caution, GroundZone.Awareness, GroundZone.Caution, T0, T0.AddSeconds(15)));
    }

    [Fact]
    public void A_real_escalation_is_never_suppressed()
        => Assert.True(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Warning, GroundZone.Caution, GroundZone.Caution, T0, T0.AddSeconds(2)));

    [Fact]
    public void An_earlier_route_or_converging_alert_does_not_swallow_a_first_caution()
        => Assert.True(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Caution, GroundZone.Awareness, GroundZone.None, T0, T0.AddSeconds(3)));

    // ── re-closing after a de-escalation (PR #247 B1 review I3, B2 review Important 2) ───
    // "Stop" is spoken at 300 ft; the pilot slows, the speed-scaled boundary shrinks and the zone drops
    // silently; the pilot creeps back in within 15 s. Unchanged distance is boundary flicker; a gap
    // 50 ft smaller than when "Stop" was spoken is the pilot closing on it again. That rule is for
    // "Stop" ONLY: a pilot who complied with "Slow down" closes again as the boundary shrinks, and must
    // not hear "Slow down" again inside the window (R8) — the Warning line still says "Stop" if it matters.

    [Theory]
    [InlineData(260.0, false)]   // 40 ft closer: flicker, not closing
    [InlineData(250.0, true)]    // exactly EscalationReclosureFt closer
    [InlineData(240.0, true)]    // 60 ft closer
    [InlineData(200.0, true)]
    public void A_warning_re_entry_is_spoken_again_once_the_gap_has_closed_by_fifty_feet(double distFt, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Warning, GroundZone.Caution, GroundZone.Warning, T0, T0.AddSeconds(5), distFt, 300.0));

    [Theory]
    [InlineData(240.0)]   // 60 ft closer
    [InlineData(200.0)]
    [InlineData(100.0)]
    public void A_caution_re_entry_inside_the_window_is_not_spoken_again_by_closing(double distFt)
        => Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Caution, GroundZone.Awareness, GroundZone.Caution, T0, T0.AddSeconds(5), distFt, 300.0));

    // ── what a withheld escalation records ─────────────────────────────────────────────
    // A withheld WARNING escalation is not recorded — judged again next evaluation, so "Stop" is not
    // swallowed for good (I3). Everything else withheld is recorded silently (R8).

    [Fact]
    public void A_withheld_stop_is_not_recorded()
        => Assert.Equal(GroundZone.Caution,
            GroundTrafficLogic.ZoneToRecordWhenWithheld(GroundZone.Warning, GroundZone.Caution));

    [Fact]
    public void A_withheld_slow_down_is_recorded()
        => Assert.Equal(GroundZone.Caution,
            GroundTrafficLogic.ZoneToRecordWhenWithheld(GroundZone.Caution, GroundZone.Awareness));

    [Fact]
    public void A_de_escalation_is_recorded()
        => Assert.Equal(GroundZone.Caution,
            GroundTrafficLogic.ZoneToRecordWhenWithheld(GroundZone.Caution, GroundZone.Warning));

    [Fact]
    public void A_withheld_awareness_ping_is_recorded()
        => Assert.Equal(GroundZone.Awareness,
            GroundTrafficLogic.ZoneToRecordWhenWithheld(GroundZone.Awareness, GroundZone.None));

    [Fact]
    public void Awareness_is_not_re_announced_by_closing()
        => Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Awareness, GroundZone.None, GroundZone.Awareness, T0, T0.AddSeconds(10), 100.0, 300.0));

    [Fact]
    public void Without_both_distances_a_re_entry_keeps_the_window_rule()
    {
        Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Warning, GroundZone.Caution, GroundZone.Warning, T0, T0.AddSeconds(5), double.NaN, 300.0));
        Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Warning, GroundZone.Caution, GroundZone.Warning, T0, T0.AddSeconds(5), 100.0, double.NaN));
        Assert.True(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Warning, GroundZone.Caution, GroundZone.Warning, T0, T0.AddSeconds(15), double.NaN, double.NaN));
    }

    [Fact]
    public void Staying_in_or_dropping_a_zone_is_never_announced()
    {
        Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Caution, GroundZone.Caution, GroundZone.None, DateTime.MinValue, T0));
        Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.Awareness, GroundZone.Caution, GroundZone.None, DateTime.MinValue, T0));
        Assert.False(GroundTrafficLogic.ShouldAnnounceEscalation(
            GroundZone.None, GroundZone.None, GroundZone.None, DateTime.MinValue, T0));
    }

    // ── converging ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(180, 0, false)]   // from behind while stopped in a queue: cannot act on it
    [InlineData(180, 5, true)]    // from behind while moving: still relevant
    [InlineData(300, 0, true)]    // ahead-left
    [InlineData(120, 0, true)]    // edge of the forward arc
    [InlineData(121, 0, false)]
    public void Converging_needs_the_forward_arc_or_a_moving_pilot(double rel, double ownGs, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.ConvergingAllowed(rel, ownGs));
}
