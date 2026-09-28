using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

// Exit distances: ComfortableExitLeadFeet(140 kt, 90°) ≈ 4,641 ft and (140, 30°) ≈ 4,185 ft;
// (130, 90°) ≈ 4,021 ft; (115, 90°) ≈ 3,173 ft. DistanceFromTouchdownFeet = threshold − 1,000.
public class BriefingExitPickerTests
{
    private static LandingExit Exit(string name, double thresholdFt, double angle = 90, bool vacates = true, string side = "Left") => new()
    {
        TaxiwayName = name,
        DistanceFromThresholdFeet = thresholdFt,
        DistanceFromTouchdownFeet = thresholdFt - 1000.0,
        ExitAngleDegrees = angle,
        ExitType = angle <= 50 ? "High-speed" : angle > 110 ? "End" : "Normal",
        ExitSide = side,
        VacatesRunway = vacates,
    };

    private static Runway Rwy(double lengthFt, double thresholdOffsetFt = 0) =>
        new() { RunwayID = "09", Length = lengthFt, ThresholdOffset = thresholdOffsetFt };

    // ── the aim point (ICAO Annex 14 Table 5-2 on a short runway) ─────────────────────────────

    [Theory]
    [InlineData(2000, 0, 492.1)] [InlineData(3500, 0, 820.2)] [InlineData(5000, 0, 1000.0)]
    [InlineData(4500, 1000, 820.2)] [InlineData(0, 0, 1000.0)]
    public void The_aim_point_is_Annex_14_s_on_a_short_runway(double lengthFt, double offsetFt, double aimFt)
        => Assert.Equal(aimFt, BriefingExitPicker.AimPointFeet(Rwy(lengthFt, offsetFt)), 1);

    [Fact]
    public void A_Cessna_makes_the_last_exit_of_a_2000_ft_strip_comfortably()
    {
        var last = Exit("A", 1950);
        double aim = BriefingExitPicker.AimPointFeet(Rwy(2000));
        Assert.True(BriefingExitPicker.Pick(new[] { last }, 70, null, aim)!.ComfortablyReachable);
        Assert.False(BriefingExitPicker.Pick(new[] { last }, 70)!.ComfortablyReachable);   // the old 1,000 ft aim
    }

    [Fact]
    public void A_heavy_skips_an_exit_it_cannot_slow_down_for()
    {
        var a = Exit("A", 3500);   // 2,500 ft from touchdown — needs 4,641 at 140 kt
        var b = Exit("B", 6500);   // 5,500 ft — reachable
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);
        Assert.True(choice.ComfortablyReachable);
    }

    [Fact]
    public void A_lighter_aircraft_takes_the_earlier_exit()
    {
        var a = Exit("A", 4500);   // 3,500 ft from touchdown — enough at 115 kt (3,173), not at 140 (4,641)
        var b = Exit("B", 6500);
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, b }, 115.0)!.Exit);
        Assert.Same(b, BriefingExitPicker.Pick(new[] { a, b }, 140.0)!.Exit);
    }

    [Fact]
    public void A_high_speed_exit_within_1500ft_beyond_the_first_reachable_wins()
    {
        var a = Exit("A", 6000);            // normal, reachable
        var h = Exit("H", 7200, angle: 30); // high-speed, 1,200 ft further
        var c = Exit("C", 9000);
        var choice = BriefingExitPicker.Pick(new[] { a, h, c }, 140.0)!;

        Assert.Same(h, choice.Exit);
        Assert.Same(c, choice.NextExit);
    }

    [Fact]
    public void A_high_speed_exit_further_than_1500ft_does_not_win()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7800, angle: 30); // 1,800 ft further
        var choice = BriefingExitPicker.Pick(new[] { a, h }, 140.0)!;

        Assert.Same(a, choice.Exit);
        Assert.Same(h, choice.NextExit);
    }

    [Fact]
    public void Non_vacating_and_end_exits_are_not_candidates_when_a_usable_one_exists()
    {
        var a = Exit("A", 6000, vacates: false);
        var b = Exit("B", 7000);
        var e = Exit("E", 8000, angle: 130);
        var choice = BriefingExitPicker.Pick(new[] { a, b, e }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);   // E is not a candidate, so nothing follows B
    }

    [Fact]
    public void Only_end_exits_vacating_falls_back_to_them()
    {
        var e = Exit("E", 8000, angle: 130);
        Assert.Same(e, BriefingExitPicker.Pick(new[] { e }, 140.0)!.Exit);
    }

    [Fact]
    public void Nothing_reachable_picks_the_furthest_and_flags_it()
    {
        var a = Exit("A", 2000);
        var b = Exit("B", 3000);
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.False(choice.ComfortablyReachable);
        Assert.Null(choice.NextExit);
    }

    [Fact]
    public void No_vacating_exit_returns_null()
    {
        Assert.Null(BriefingExitPicker.Pick(Array.Empty<LandingExit>(), 140.0));
        Assert.Null(BriefingExitPicker.Pick(new[] { Exit("A", 6000, vacates: false) }, 140.0));
    }

    // ── route reachability (a route always beats no route, task 8) ────────────────────────────

    [Fact]
    public void An_exit_that_routes_beats_one_that_does_not_even_when_it_crosses()
    {
        var a = Exit("A", 5000); var b = Exit("B", 6000);
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 130, Routes((b, ExitRoute.CrossesLandingRunway)))!;
        Assert.Same(b, choice.Exit);
    }

    [Fact]
    public void A_routable_exit_beyond_the_window_beats_routeless_ones_inside_it()
    {
        var a = Exit("A", 5100); var b = Exit("B", 5500); var c = Exit("C", 9000);
        Assert.Same(c, BriefingExitPicker.Pick(new[] { a, b, c }, 130, Routes((c, ExitRoute.CrossesLandingRunway)))!.Exit);
    }

    [Fact]
    public void Inside_the_window_a_clear_route_still_beats_a_crossing_one_and_a_crossing_one_beats_one_beyond()
    {
        var a = Exit("A", 5100); var b = Exit("B", 5500); var c = Exit("C", 9000);
        Assert.Same(b, BriefingExitPicker.Pick(new[] { a, b, c }, 130,
            Routes((a, ExitRoute.CrossesLandingRunway), (b, ExitRoute.Clear), (c, ExitRoute.Clear)))!.Exit);
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, b, c }, 130,
            Routes((a, ExitRoute.CrossesLandingRunway), (c, ExitRoute.Clear)))!.Exit);
    }

    [Fact]
    public void With_nothing_routable_the_first_preferred_exit_is_still_briefed()
    {
        var a = Exit("A", 5100); var b = Exit("B", 5500);
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, b }, 130, Routes())!.Exit);
    }

    [Fact]
    public void With_no_reachable_exit_routable_the_furthest_exit_that_routes_is_briefed_and_flagged()
    {
        // Re-review N-6: A and B are comfortable but lead away from the stand; P, too early to make comfortably, routes.
        var p = Exit("P", 900); var a = Exit("A", 5100); var b = Exit("B", 5500);
        var choice = BriefingExitPicker.Pick(new[] { p, a, b }, 130, Routes((p, ExitRoute.Clear)))!;
        Assert.Same(p, choice.Exit);
        Assert.False(choice.ComfortablyReachable);
        Assert.True(choice.ReachableExitsHaveNoRoute);
        Assert.True(choice.BriefedExitBehindReachable);
        // Fix wave 3 (Minor 4): A has no route to the stand, so it is never the exit to take if P is missed.
        Assert.Null(choice.NextExit);
    }

    [Fact]
    public void With_routed_exits_behind_the_first_comfortable_one_the_last_of_them_is_briefed_not_one_ahead()
    {
        // At 130 kt: R (30°, 3,700 ft from touchdown) is comfortable, S (90°, 4,000 ft) is not (needs 4,021). P and Q
        // route to the stand behind R, S routes ahead of it: the aircraft stops and backtracks to Q, the last one before R.
        var p = Exit("P", 900); var q = Exit("Q", 2000); var r = Exit("R", 4700, angle: 30); var s = Exit("S", 5000);
        var t = Exit("T", 6000);
        Assert.True(BriefingExitPicker.IsComfortablyReachable(r, 130));    // precondition
        Assert.False(BriefingExitPicker.IsComfortablyReachable(s, 130));   // precondition

        var choice = BriefingExitPicker.Pick(new[] { p, q, r, s, t }, 130,
            Routes((p, ExitRoute.Clear), (q, ExitRoute.Clear), (s, ExitRoute.Clear)))!;

        Assert.Same(q, choice.Exit);
        Assert.False(choice.ComfortablyReachable);
        Assert.True(choice.ReachableExitsHaveNoRoute);
        Assert.True(choice.BriefedExitBehindReachable);
        Assert.Same(s, choice.NextExit);   // the next ROUTED exit on that side, 3,000 ft on
    }

    [Fact]
    public void With_routed_exits_only_after_the_first_comfortable_one_the_first_of_them_is_briefed()
    {
        // A comfortable rapid exit R with no route, then two steep routed exits S (300 ft after it) and V, neither
        // comfortable at 130 kt: the first, S, is briefed — the aircraft reaches it before V — and it is AHEAD of R.
        var r = Exit("R", 4600, angle: 30); var s = Exit("S", 4900); var v = Exit("V", 5000);
        Assert.True(BriefingExitPicker.IsComfortablyReachable(r, 130));    // precondition
        Assert.False(BriefingExitPicker.IsComfortablyReachable(s, 130));   // precondition
        Assert.False(BriefingExitPicker.IsComfortablyReachable(v, 130));   // precondition

        var choice = BriefingExitPicker.Pick(new[] { r, s, v }, 130, Routes((s, ExitRoute.Clear), (v, ExitRoute.Clear)))!;

        Assert.Same(s, choice.Exit);
        Assert.False(choice.ComfortablyReachable);
        Assert.True(choice.ReachableExitsHaveNoRoute);
        Assert.False(choice.BriefedExitBehindReachable);
        Assert.Null(choice.NextExit);      // V is only 100 ft on
    }

    [Fact]
    public void On_the_no_route_path_the_next_exit_is_never_one_without_a_route()
    {
        // Minor 4: Q, 600 ft past P on the same side, has no route to the stand — never "Next exit if missed".
        var p = Exit("P", 900); var q = Exit("Q", 1500); var a = Exit("A", 5100);
        var choice = BriefingExitPicker.Pick(new[] { p, q, a }, 130, Routes((p, ExitRoute.Clear)))!;
        Assert.Same(p, choice.Exit);
        Assert.Null(choice.NextExit);
    }

    [Fact]
    public void With_no_stand_known_the_choice_says_so()
    {
        var p = Exit("P", 500); var q = Exit("Q", 900);
        Assert.False(BriefingExitPicker.Pick(new[] { p, q }, 130)!.StandKnown);
        Assert.True(BriefingExitPicker.Pick(new[] { p, q }, 130, Routes((p, ExitRoute.Clear)))!.StandKnown);
        Assert.False(BriefingExitPicker.Pick(new[] { Exit("A", 6000) }, 130)!.StandKnown);   // a comfortable one too
    }

    [Fact]
    public void With_nothing_comfortable_the_furthest_exit_that_routes_is_briefed()
    {
        var p = Exit("P", 500); var q = Exit("Q", 900);
        Assert.Same(p, BriefingExitPicker.Pick(new[] { p, q }, 130, Routes((p, ExitRoute.Clear)))!.Exit);
        Assert.Same(q, BriefingExitPicker.Pick(new[] { p, q }, 130)!.Exit);   // no stand known: the furthest
    }

    // ── the gate side (owner decision, 2026-09-26) ────────────────────────────────────────────

    /// <summary>The planner's answer to "what does this exit's route to the stand do about the runway just
    /// landed on" — every exit named crosses it, every other one supplied is clear. Unlisted exits read as
    /// <see cref="ExitRoute.None"/> (no route at all), so every exit a test cares about must be named.</summary>
    private static Func<LandingExit, ExitRoute> Routes(params (LandingExit Exit, ExitRoute Route)[] map) =>
        e => map.FirstOrDefault(m => ReferenceEquals(m.Exit, e)).Route;   // unlisted → None

    [Fact]
    public void The_exit_on_the_stand_s_side_wins_over_one_whose_route_crosses_back_over_the_runway()
    {
        // OMDB 12L, A380: N5A (left) is the first comfortable exit, but the stand is on the right and its route
        // crosses 30R — the runway just landed on. M7A, 30 ft further, turns right and reaches it without.
        var n5a = Exit("N5A", 6467, angle: 30, side: "Left");
        var m7a = Exit("M7A", 6497, angle: 30, side: "Right");
        var n6 = Exit("N6", 7613, angle: 30, side: "Left");
        var m9 = Exit("M9", 7613.5, angle: 30, side: "Right");
        var exits = new[] { n5a, m7a, n6, m9 };

        Assert.Same(n5a, BriefingExitPicker.Pick(exits, 140.0)!.Exit);

        var choice = BriefingExitPicker.Pick(exits, 140.0,
            Routes((n5a, ExitRoute.CrossesLandingRunway), (m7a, ExitRoute.Clear), (n6, ExitRoute.Clear), (m9, ExitRoute.Clear)))!;
        Assert.Same(m7a, choice.Exit);
        Assert.Same(m9, choice.NextExit);    // the next exit on the RIGHT, 1,116 ft further
        Assert.True(choice.ComfortablyReachable);
    }

    [Fact]
    public void When_every_exit_in_reach_crosses_back_the_usual_choice_stands()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7200, angle: 30);
        var choice = BriefingExitPicker.Pick(new[] { a, h }, 140.0, _ => ExitRoute.CrossesLandingRunway)!;

        Assert.Same(h, choice.Exit);          // the high-speed exit within 1,500 ft, exactly as with no stand known
    }

    [Fact]
    public void The_gate_side_preference_looks_no_further_than_1500ft_beyond_the_first_comfortable_exit()
    {
        var a = Exit("A", 6000, side: "Left");
        var atEdge = Exit("B", 7500, side: "Right");      // exactly 1,500 ft further: inside the window
        Assert.Same(atEdge, BriefingExitPicker.Pick(new[] { a, atEdge }, 140.0,
            Routes((a, ExitRoute.CrossesLandingRunway), (atEdge, ExitRoute.Clear)))!.Exit);

        var beyond = Exit("B", 7501, side: "Right");      // 1,501 ft: outside, so A stands although it crosses back
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, beyond }, 140.0,
            Routes((a, ExitRoute.CrossesLandingRunway), (beyond, ExitRoute.Clear)))!.Exit);
    }

    [Fact]
    public void A_high_speed_exit_exactly_1500ft_further_still_wins_over_a_normal_one()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7500, angle: 30);
        Assert.Same(h, BriefingExitPicker.Pick(new[] { a, h }, 140.0)!.Exit);
    }

    [Fact]
    public void Of_two_high_speed_exits_in_the_window_the_first_that_stays_clear_of_the_runway_wins()
    {
        var n = Exit("N", 6000);                        // normal: the first comfortable exit
        var h1 = Exit("H1", 6300, angle: 30);            // high-speed, crosses back
        var h2 = Exit("H2", 6900, angle: 30);            // high-speed, stays clear
        var exits = new[] { n, h1, h2 };

        Assert.Same(h1, BriefingExitPicker.Pick(exits, 140.0)!.Exit);
        Assert.Same(h2, BriefingExitPicker.Pick(exits, 140.0,
            Routes((h1, ExitRoute.CrossesLandingRunway), (n, ExitRoute.Clear), (h2, ExitRoute.Clear)))!.Exit);
        // Both high-speed exits cross back: the normal one that stays clear beats them.
        Assert.Same(n, BriefingExitPicker.Pick(exits, 140.0,
            Routes((h1, ExitRoute.CrossesLandingRunway), (h2, ExitRoute.CrossesLandingRunway), (n, ExitRoute.Clear)))!.Exit);
    }

    [Fact]
    public void The_next_exit_if_missed_is_on_the_same_side_at_least_500ft_further()
    {
        // KPIT 32: the "next exit if missed" was N2, on the other side and 1 ft further — the same place.
        var r1 = Exit("R1", 6000, side: "Left");
        var n2 = Exit("N2", 6001, side: "Right");        // other side, 1 ft on
        var q = Exit("Q", 6300, side: "Left");           // same side, only 300 ft on: missing R1 misses Q too
        var n3 = Exit("N3", 6600, side: "Right");        // other side
        var p = Exit("P", 6500, side: "Left");           // same side, exactly 500 ft on
        var choice = BriefingExitPicker.Pick(new[] { r1, n2, q, n3, p }, 140.0)!;

        Assert.Same(r1, choice.Exit);
        Assert.Same(p, choice.NextExit);

        Assert.Null(BriefingExitPicker.Pick(new[] { r1, n2, q, n3 }, 140.0)!.NextExit);   // none on the left 500 ft on
    }
}
