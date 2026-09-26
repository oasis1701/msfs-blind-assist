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
}
