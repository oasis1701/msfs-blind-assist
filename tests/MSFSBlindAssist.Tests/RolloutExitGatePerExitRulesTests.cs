// Per-exit rollout rules added after the KMEM 36L grass excursion (2026-09-26): the exit's own turn
// window, the too-fast line for "turn now", the "Slow down." line, the retarget "Straighten." condition,
// and the exit-bearing sanity check. Numbers come from the real KMEM geometry (runway 164 ft wide in
// navdata; M7's junction 2.3 m off the centerline, its branch turns 22.7° before it is clear of the runway).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutExitGatePerExitRulesTests
{
    // ---- TurnWindowFeetFor ---------------------------------------------------------------

    [Fact]
    public void A_junction_on_the_centerline_gets_only_the_pavement_lead()
        => Assert.InRange(RolloutExitGate.TurnWindowFeetFor(164.0, 2.3, 22.7), 322.0, 326.0);

    [Fact]
    public void A_hold_short_marker_off_the_centerline_adds_its_forward_offset()
        => Assert.InRange(RolloutExitGate.TurnWindowFeetFor(164.0, 40.0, 30.0), 531.0, 536.0);

    [Fact]
    public void The_old_fixed_window_was_the_worst_case_of_the_same_sum()
        => Assert.InRange(RolloutExitGate.TurnWindowFeetFor(200.0, 45.5, 15.0), 928.0, 933.0);

    [Fact]
    public void The_window_never_exceeds_the_fixed_1000_ft()
        => Assert.Equal(RolloutExitGate.TurnWindowFeet, RolloutExitGate.TurnWindowFeetFor(200.0, 60.0, 15.0));

    [Fact]
    public void A_missing_width_falls_back_to_200_ft()
        => Assert.InRange(RolloutExitGate.TurnWindowFeetFor(0.0, 0.0, 30.0), 371.0, 375.0);

    [Fact]
    public void An_unknown_or_turnaround_angle_never_breaks_the_arithmetic()
    {
        // 0 is treated as 15°, anything past 90° as 90° (no forward offset).
        Assert.InRange(RolloutExitGate.TurnWindowFeetFor(164.0, 10.0, 0.0), 427.0, 431.0);
        Assert.InRange(RolloutExitGate.TurnWindowFeetFor(164.0, 10.0, 130.0), 304.0, 308.0);
    }

    // ---- The windowed turn gate and tone mode (KMEM replay values) ---------------------------

    [Fact]
    public void Kmem_a_15_degree_turn_483_ft_before_M7_is_not_the_M7_turn()
        => Assert.False(RolloutExitGate.IsExitTurnBegun(15.0, 47.6, 483.0, false, 15.5, 324.0));

    [Fact]
    public void Inside_the_window_the_same_turn_is_accepted()
        => Assert.True(RolloutExitGate.IsExitTurnBegun(15.0, 47.6, 300.0, false, 15.5, 324.0));

    [Fact]
    public void Past_the_exit_the_window_does_not_apply()
        => Assert.True(RolloutExitGate.IsExitTurnBegun(15.0, 30.0, 5000.0, true, 15.5, 324.0));

    [Fact]
    public void Kmem_an_8_degree_leftover_turn_631_ft_before_M7_gets_the_drift_tone_not_silence()
        => Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(48.1, 631.0, 8.4, 15.5, 324.0));

    [Fact]
    public void Inside_the_window_a_turn_toward_the_exit_is_still_left_alone()
        => Assert.Equal(RolloutToneMode.Silent,
            RolloutExitGate.SelectToneMode(48.1, 310.0, 8.4, 15.5, 324.0));

    // ---- Too fast to turn -------------------------------------------------------------------

    [Theory]
    [InlineData(73.0, 30.0)]
    [InlineData(45.0, 30.0)]
    [InlineData(44.9, 60.0)]
    [InlineData(22.7, 60.0)]
    [InlineData(0.0, 30.0)]
    [InlineData(130.0, 30.0)]
    public void Max_turn_speed_is_the_turn_off_speed_plus_ten_knots(double angle, double expected)
        => Assert.Equal(expected, RolloutExitGate.MaxTurnSpeedKts(angle));

    [Fact]
    public void Kmem_49_knots_is_too_fast_for_M6()
    {
        Assert.True(RolloutExitGate.IsTooFastToTurn(49.0, 52.0));   // the M6 the app believed in
        Assert.True(RolloutExitGate.IsTooFastToTurn(49.0, 73.0));   // the real 36L M6
        Assert.False(RolloutExitGate.IsTooFastToTurn(49.0, 22.7));  // M7, a high-speed exit
    }

    [Fact]
    public void The_line_itself_is_still_flyable()
    {
        Assert.False(RolloutExitGate.IsTooFastToTurn(30.0, 90.0));
        Assert.True(RolloutExitGate.IsTooFastToTurn(30.1, 90.0));
    }

    // ---- The "Slow down." line ----------------------------------------------------------------

    [Theory]
    [InlineData(30.0, "Normal")]
    [InlineData(30.0, "High-speed")]
    [InlineData(90.0, "Normal")]
    public void Any_exit_but_an_end_exit_hears_slow_down_above_its_max_turn_speed(double angle, string exitType)
        => Assert.Equal(RolloutExitGate.MaxTurnSpeedKts(angle), RolloutExitGate.SlowDownAboveKts(angle, exitType));

    [Theory]
    [InlineData(30.0)]
    [InlineData(54.0)]
    [InlineData(130.0)]
    public void An_end_exit_keeps_the_30_knot_line_whatever_its_angle(double angle)
        => Assert.Equal(RolloutExitGate.TaxiGroundSpeedKts, RolloutExitGate.SlowDownAboveKts(angle, "End"));

    [Fact]
    public void A_shallow_end_exit_keeps_30_knots_where_its_angle_alone_would_allow_60()
    {
        Assert.Equal(60.0, RolloutExitGate.SlowDownAboveKts(30.0, "High-speed"));
        Assert.Equal(RolloutExitGate.TaxiGroundSpeedKts, RolloutExitGate.SlowDownAboveKts(30.0, "End"));
    }

    // ---- Straighten -------------------------------------------------------------------------

    [Fact]
    public void Kmem_a_leftover_M6_turn_631_ft_before_M7_needs_straightening()
        => Assert.True(RolloutExitGate.ShouldStraightenAfterRetarget(8.4, 15.5, 631.0, false, 324.0));

    [Fact]
    public void A_small_deviation_needs_no_words()
        => Assert.False(RolloutExitGate.ShouldStraightenAfterRetarget(4.9, 15.5, 631.0, false, 324.0));

    [Fact]
    public void A_turn_the_new_exit_would_accept_is_left_alone()
        => Assert.False(RolloutExitGate.ShouldStraightenAfterRetarget(8.4, 15.5, 300.0, false, 324.0));

    [Fact]
    public void A_turn_away_from_the_new_exit_is_straightened_even_close_to_it()
        => Assert.True(RolloutExitGate.ShouldStraightenAfterRetarget(-8.4, 15.5, 300.0, false, 324.0));

    // ---- Exit bearing sanity ------------------------------------------------------------------

    [Fact]
    public void Kmem_the_127_degree_M6_bearing_is_not_a_plausible_exit_direction()
        => Assert.False(RolloutExitGate.IsPlausibleExitBearing(127.2, 359.0));

    [Fact]
    public void A_perpendicular_exit_is_plausible()
    {
        Assert.True(RolloutExitGate.IsPlausibleExitBearing(89.0, 359.0));
        Assert.True(RolloutExitGate.IsPlausibleExitBearing(360.0, 359.0));
    }

    [Fact]
    public void An_unknown_bearing_is_never_steered_to()
        => Assert.False(RolloutExitGate.IsPlausibleExitBearing(0.0, 359.0));

    // ---- An exit declined as too fast: overshoot margin and tone ----------------------------

    [Theory]
    [InlineData(100.0)]
    [InlineData(500.0)]
    public void A_declined_exit_is_overshot_as_soon_as_the_aircraft_is_past_it(double baseMargin)
        => Assert.Equal(0.0, RolloutExitGate.OvershootMarginFeet(baseMargin, tooFastDeclined: true));

    [Theory]
    [InlineData(100.0)]
    [InlineData(500.0)]
    public void Otherwise_the_exit_type_margin_is_unchanged(double baseMargin)
        => Assert.Equal(baseMargin, RolloutExitGate.OvershootMarginFeet(baseMargin, tooFastDeclined: false));

    [Fact]
    public void A_countdown_no_too_fast_sentence_holds_may_speak_at_once()
        => Assert.True(RolloutExitGate.CountdownStatusMaySpeak(
               new DateTime(2026, 9, 26, 1, 23, 40, DateTimeKind.Utc), DateTime.MinValue));

    [Fact]
    public void After_too_fast_to_turn_the_countdown_waits_until_the_sentence_has_been_spoken()
    {
        // Probed 50 ms either side of the 5.3 s line: AddSeconds converts through a double, so a probe AT
        // it lands a tick short — a boundary no 30-60 Hz frame is ever exact on.
        var spoken = new DateTime(2026, 9, 26, 1, 23, 30, DateTimeKind.Utc);
        Assert.False(RolloutExitGate.CountdownStatusMaySpeak(spoken.AddSeconds(3.5), spoken));   // node passed
        Assert.False(RolloutExitGate.CountdownStatusMaySpeak(spoken.AddSeconds(5.25), spoken));
        Assert.True(RolloutExitGate.CountdownStatusMaySpeak(spoken.AddSeconds(5.35), spoken));
        Assert.True(RolloutExitGate.CountdownStatusMaySpeak(spoken.AddSeconds(60.0), spoken));  // never dropped
    }

    [Fact]
    public void The_hold_covers_the_measured_sentence_plus_about_a_fifth()
        => Assert.InRange(RolloutExitGate.TooFastNoExitSpeechHoldSeconds, 4.39 * 1.2, 4.39 * 1.25);

    [Fact]
    public void Too_fast_near_an_off_centreline_exit_holds_the_runway_heading()
    {
        // 200 ft out at 40 kt, exit 30° to the right: normally the exit-bearing pan toward its node.
        Assert.Equal(RolloutToneMode.ExitBearing,
            RolloutExitGate.SelectToneMode(40.0, 200.0, 0.0, 30.0, 500.0));
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(40.0, 200.0, 0.0, 30.0, 500.0, tooFastForExit: true));
    }

    [Fact]
    public void Too_fast_a_turn_toward_the_exit_inside_its_window_is_opposed_not_silenced()
    {
        // 400 ft out, inside a 500 ft window, 8° right toward a right-hand exit: normally left alone.
        Assert.Equal(RolloutToneMode.Silent,
            RolloutExitGate.SelectToneMode(40.0, 400.0, 8.0, 30.0, 500.0));
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(40.0, 400.0, 8.0, 30.0, 500.0, tooFastForExit: true));
    }

    [Fact]
    public void Above_the_tone_line_a_too_fast_exit_is_still_silent()
        => Assert.Equal(RolloutToneMode.Silent,
            RolloutExitGate.SelectToneMode(60.0, 200.0, 0.0, 30.0, 500.0, tooFastForExit: true));

    // ---- The too-fast alternative's comfortable pass ----------------------------------------

    private static LandingExit At(string name, double distFromThresholdFt, double angleDeg) => new LandingExit
    {
        TaxiwayName = name, DistanceFromThresholdFeet = distFromThresholdFt, ExitAngleDegrees = angleDeg
    };

    [Fact]
    public void Above_60_knots_the_too_fast_alternative_is_one_the_aircraft_can_slow_down_for()
    {
        // At 65 kt ExitLeadFeet asks only 715 ft, but slowing to a 90° exit's 20 kt with comfortable braking
        // takes about 1,050 ft: B, 900 ft ahead, would itself be too fast at its own turn point.
        var exits = new List<LandingExit> { At("B", 5900.0, 90.0), At("C", 6200.0, 90.0) };
        Assert.Equal("C",
            RolloutExitGate.FirstComfortableDownfieldExit(exits, 5100.0, 5000.0, 65.0)?.TaxiwayName);
    }

    [Fact]
    public void With_no_comfortable_exit_the_comfortable_pass_finds_none()
        => Assert.Null(RolloutExitGate.FirstComfortableDownfieldExit(
               new List<LandingExit> { At("B", 5900.0, 90.0) }, 5100.0, 5000.0, 65.0));

    [Fact]
    public void Not_too_fast_every_mode_is_unchanged()
    {
        Assert.Equal(RolloutToneMode.ExitBearing,
            RolloutExitGate.SelectToneMode(40.0, 200.0, 0.0, 30.0, 500.0, tooFastForExit: false));
        Assert.Equal(RolloutToneMode.Silent,
            RolloutExitGate.SelectToneMode(40.0, 400.0, 8.0, 30.0, 500.0, tooFastForExit: false));
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(48.1, 631.0, 8.4, 15.5, 324.0, tooFastForExit: false));
        Assert.Equal(RolloutToneMode.Silent,
            RolloutExitGate.SelectToneMode(60.0, 200.0, 0.0, 30.0, 500.0, tooFastForExit: false));
    }
}
