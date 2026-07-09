// Characterization tests for MSFSBlindAssist.Services.DockingGeometry.
//
// Ports the golden cases from tools/DockingProbe/Program.cs (a live-verified probe:
// KATL C55, KBOS E13, EDDF A66, KATL C20 incidents). This is characterization, not
// spec verification: values are taken from the probe / derived by reasoning about the
// source and confirmed by running the tests; if a literal ever disagrees with actual
// output, the test must be corrected to match real output, not the other way around.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DockingGeometryTests
{
    private const double Eps = 1e-6;

    // --- NormalizeDeg180 --------------------------------------------------

    [Theory]
    [InlineData(190.0, -170.0)]
    [InlineData(-190.0, 170.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(180.0, 180.0)]
    [InlineData(-180.0, 180.0)]
    public void NormalizeDeg180_wraps_into_range(double input, double expected)
    {
        Assert.Equal(expected, DockingGeometry.NormalizeDeg180(input), Eps);
    }

    // --- AlongTrackMetres ---------------------------------------------------

    [Fact]
    public void AlongTrackMetres_straight_ahead_equals_distance()
    {
        Assert.Equal(50.0, DockingGeometry.AlongTrackMetres(50, 0), 1e-9);
    }

    [Fact]
    public void AlongTrackMetres_90deg_off_is_near_zero()
    {
        Assert.Equal(0.0, DockingGeometry.AlongTrackMetres(50, 90), 1e-6);
    }

    [Fact]
    public void AlongTrackMetres_180deg_is_negative_overshoot()
    {
        Assert.True(DockingGeometry.AlongTrackMetres(50, 180) < 0);
    }

    // --- IsStop / IsOvershoot -----------------------------------------------

    [Theory]
    [InlineData(0.3, true)]     // at the tightened tolerance edge
    [InlineData(0.5, false)]    // just above tightened tolerance
    [InlineData(0.8, false)]
    [InlineData(5.0, false)]
    [InlineData(-0.5, true)]    // within [-1, 0.3]
    public void IsStop_matches_tolerance_band(double alongMetres, bool expected)
    {
        Assert.Equal(expected, DockingGeometry.IsStop(alongMetres));
    }

    [Theory]
    [InlineData(-3.0, true)]
    [InlineData(5.0, false)]
    [InlineData(-0.5, false)]   // not yet past 1 m
    [InlineData(-1.5, true)]
    public void IsOvershoot_trips_past_1m(double alongMetres, bool expected)
    {
        Assert.Equal(expected, DockingGeometry.IsOvershoot(alongMetres));
    }

    // --- BeepIntervalMs -------------------------------------------------------

    [Fact]
    public void BeepIntervalMs_far_is_slower_than_near()
    {
        Assert.True(DockingGeometry.BeepIntervalMs(40) > DockingGeometry.BeepIntervalMs(5));
    }

    [Fact]
    public void BeepIntervalMs_clamps_at_far_bound()
    {
        Assert.True(DockingGeometry.BeepIntervalMs(1000) <= 1000.0 + 1e-9);
    }

    [Fact]
    public void BeepIntervalMs_clamps_at_near_bound()
    {
        Assert.True(DockingGeometry.BeepIntervalMs(0) >= 80.0 - 1e-9);
    }

    // --- ShouldEngage ------------------------------------------------------

    private const double R = DockingGeometry.EngageRangeMetres;

    [Fact]
    public void ShouldEngage_true_for_a_typical_approach()
    {
        Assert.True(DockingGeometry.ShouldEngage(10, 40, 20, 0.0, R));
    }

    [Fact]
    public void ShouldEngage_false_when_too_fast()
    {
        Assert.False(DockingGeometry.ShouldEngage(40, 40, 20, 0.0, R));
    }

    [Fact]
    public void ShouldEngage_false_when_too_far()
    {
        Assert.False(DockingGeometry.ShouldEngage(10, 200, 20, 0.0, R));
        Assert.False(DockingGeometry.ShouldEngage(10, 55, 20, 0.0, R)); // just past the 50 m ceiling
    }

    [Fact]
    public void ShouldEngage_false_when_behind_the_stop()
    {
        Assert.False(DockingGeometry.ShouldEngage(10, -5, 20, 0.0, R));
    }

    [Fact]
    public void ShouldEngage_false_when_outside_the_cone()
    {
        Assert.False(DockingGeometry.ShouldEngage(10, 40, 120, 0.0, R));
    }

    [Fact]
    public void ShouldEngage_false_at_taxi_speed_above_15kts()
    {
        Assert.False(DockingGeometry.ShouldEngage(20, 40, 20, 0.0, R));
    }

    [Fact]
    public void ShouldEngage_true_just_under_the_speed_ceiling()
    {
        Assert.True(DockingGeometry.ShouldEngage(12, 40, 20, 0.0, R));
    }

    [Fact]
    public void ShouldEngage_false_for_KATL_C55_infeasible_cross_track()
    {
        // KATL C55 regression: along 25 m, cross 38.4 m, hdgErr -57 (inside the +-70 cone),
        // but the 35deg-max intercept can never close that lateral offset.
        Assert.False(DockingGeometry.ShouldEngage(1.3, 25.0, -57.0, 38.4, R));
    }

    [Fact]
    public void ShouldEngage_true_for_a_teleport_onto_the_line()
    {
        Assert.True(DockingGeometry.ShouldEngage(0.2, 1.8, 0.8, 0.03, R));
    }

    [Fact]
    public void ShouldEngage_true_mid_turn_once_feasible()
    {
        Assert.True(DockingGeometry.ShouldEngage(5, 20, 30, 9.0, R));
    }

    [Fact]
    public void ShouldEngage_false_mid_turn_while_still_infeasible()
    {
        Assert.False(DockingGeometry.ShouldEngage(5, 20, 30, 15.0, R));
    }

    [Fact]
    public void MaxEngageCrossMetres_floors_at_StopMaxCross_at_the_squared_up_point()
    {
        Assert.Equal(
            DockingGeometry.StopMaxCrossMetres,
            DockingGeometry.MaxEngageCrossMetres(DockingGeometry.SquareUpEndMetres),
            Eps);
    }

    // --- IsLateralMiss -------------------------------------------------------

    [Fact]
    public void IsLateralMiss_true_for_KATL_C55_unrecoverable_geometry()
    {
        Assert.True(DockingGeometry.IsLateralMiss(5.7, 22.2));
    }

    [Fact]
    public void IsLateralMiss_false_for_a_salvageable_residual()
    {
        Assert.False(DockingGeometry.IsLateralMiss(2.5, 2.2));
    }

    [Fact]
    public void IsLateralMiss_false_outside_the_squaring_zone()
    {
        Assert.False(DockingGeometry.IsLateralMiss(20.0, 10.0));
    }

    [Fact]
    public void IsLateralMiss_true_at_the_stop_band_off_the_line()
    {
        Assert.True(DockingGeometry.IsLateralMiss(0.3, 3.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.5)]
    [InlineData(6.0)]
    [InlineData(25.0)]
    [InlineData(50.0)]
    public void EngageBound_is_always_at_least_as_tight_as_the_lateral_miss_bound(double alongMetres)
    {
        // Invariant: an engage-passing geometry can never lateral-miss on the same frame.
        Assert.True(
            DockingGeometry.MaxEngageCrossMetres(alongMetres)
                <= DockingGeometry.StopMaxCrossMetres
                   + DockingGeometry.MaxCrossClosurePerMetre * Math.Max(0.0, alongMetres) + 1e-9);
    }

    // --- LineupInterceptDeg --------------------------------------------------

    [Fact]
    public void LineupInterceptDeg_keeps_full_intercept_off_line_in_the_squaring_zone()
    {
        // KATL C55 trace: cross -73 ft, along 5.7 m. Far off the line the fade must be
        // DISABLED (no snap toward the raw gate heading — that was the hard-pan bug).
        Assert.Equal(-35.0, DockingGeometry.LineupInterceptDeg(-73.0, 5.7), 1e-9);
    }

    [Fact]
    public void LineupInterceptDeg_keeps_full_intercept_off_line_far_out()
    {
        Assert.Equal(-35.0, DockingGeometry.LineupInterceptDeg(-73.0, 14.5), 1e-9);
    }

    [Fact]
    public void LineupInterceptDeg_squares_to_gate_heading_near_the_line_at_fade_end()
    {
        Assert.Equal(0.0, DockingGeometry.LineupInterceptDeg(-2.0, 2.0), 1e-9);
    }

    [Fact]
    public void LineupInterceptDeg_sqrt_intercept_near_line_far_out()
    {
        double expected = -35.0 * Math.Sqrt(1.0 / 39.0);
        Assert.Equal(expected, DockingGeometry.LineupInterceptDeg(-2.0, 7.0), 1e-9);
    }

    [Fact]
    public void LineupInterceptDeg_half_blend_at_cross_6ft()
    {
        double expected = -35.0 * Math.Sqrt(5.0 / 39.0) * 0.5;
        Assert.Equal(expected, DockingGeometry.LineupInterceptDeg(-6.0, 2.5), 1e-9);
    }

    [Fact]
    public void LineupInterceptDeg_sign_follows_cross_sign()
    {
        Assert.True(DockingGeometry.LineupInterceptDeg(30.0, 20.0) > 0);
        Assert.True(DockingGeometry.LineupInterceptDeg(-30.0, 20.0) < 0);
    }

    [Fact]
    public void LineupInterceptDeg_deadband_within_1ft_is_the_gate_heading()
    {
        Assert.Equal(0.0, DockingGeometry.LineupInterceptDeg(0.8, 20.0), 1e-9);
    }

    // --- Documented constants -------------------------------------------------

    [Fact]
    public void BeepNearMetres_equals_StopToleranceMetres_no_plateau()
    {
        Assert.Equal(DockingGeometry.StopToleranceMetres, DockingGeometry.BeepNearMetres);
    }

    [Fact]
    public void Documented_tuning_constants_are_pinned()
    {
        Assert.Equal(6.0, DockingGeometry.SlowDownMetres);
        Assert.Equal(5.0, DockingGeometry.SlowDownSpeedKts);
        Assert.Equal(50.0, DockingGeometry.EngageRangeMetres);
        Assert.Equal(30.0, DockingGeometry.BeepFarMetres);
        Assert.Equal(2.0, DockingGeometry.OccupancyClampMarginMetres);
    }

    // --- ShiftStopMetres ------------------------------------------------------

    private const double Lat0 = 50.0;
    private const double Lon0 = 8.0;

    [Fact]
    public void ShiftStopMetres_zero_offset_is_a_strict_no_op()
    {
        DockingGeometry.ShiftStopMetres(Lat0, Lon0, 137.0, 0.0, 0.0, out double lat, out double lon);
        Assert.Equal(Lat0, lat);
        Assert.Equal(Lon0, lon);
    }

    [Fact]
    public void ShiftStopMetres_longitudinal_at_heading0_moves_north()
    {
        DockingGeometry.ShiftStopMetres(Lat0, Lon0, 0.0, 100.0, 0.0, out double lat, out double lon);
        Assert.Equal(Lat0 + 100.0 / 111320.0, lat, 1e-9);
        Assert.Equal(Lon0, lon, 1e-9);
    }

    [Fact]
    public void ShiftStopMetres_lateral_right_at_heading0_moves_east()
    {
        DockingGeometry.ShiftStopMetres(Lat0, Lon0, 0.0, 0.0, 100.0, out double lat, out double lon);
        Assert.Equal(Lat0, lat, 1e-9);
        Assert.True(lon > Lon0);
    }

    [Fact]
    public void ShiftStopMetres_lateral_left_at_heading0_moves_west()
    {
        DockingGeometry.ShiftStopMetres(Lat0, Lon0, 0.0, 0.0, -100.0, out _, out double lon);
        Assert.True(lon < Lon0);
    }

    [Fact]
    public void ShiftStopMetres_longitudinal_at_heading90_moves_east()
    {
        DockingGeometry.ShiftStopMetres(Lat0, Lon0, 90.0, 100.0, 0.0, out double lat, out double lon);
        Assert.True(lon > Lon0);
        Assert.Equal(Lat0, lat, 1e-9);
    }

    // --- ClampStopToOccupancy -------------------------------------------------

    [Fact]
    public void ClampStopToOccupancy_KBOS_E13_clamps_to_threshold_minus_margin()
    {
        // T=25, base-stop gap=25, .py-shifted datum at 31.5 m -> clamp to T-margin = 23 m.
        // Verified live: 31.5 m reads as "reposition", 23 m as arrival services + jetway.
        double ppLat = 42.0, ppLon = -71.0, hdg = 207.7, threshold = 25.0;
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 25.0, 0.0, out double bsLat, out double bsLon);
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 31.5, 0.0, out double sLat, out double sLon);

        bool moved = DockingGeometry.ClampStopToOccupancy(
            ppLat, ppLon, bsLat, bsLon, hdg, threshold,
            ref sLat, ref sLon, out double gap, out double desired, out double clamped);

        Assert.True(moved);
        Assert.Equal(25.0, gap, 1e-3);
        Assert.Equal(31.5, desired, 1e-3);
        Assert.Equal(23.0, clamped, 1e-3);

        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 23.0, 0.0, out double exLat, out double exLon);
        Assert.Equal(exLat, sLat, 1e-7);
        Assert.Equal(exLon, sLon, 1e-7);
    }

    [Fact]
    public void ClampStopToOccupancy_is_a_no_op_when_the_base_stop_is_beyond_the_circle()
    {
        // EDDF A66: T=15 < gap=25.1 -> VDGS-reliant gate, no-op.
        double ppLat = 50.0, ppLon = 8.5, hdg = 339.9, threshold = 15.0;
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 25.1, 0.0, out double bsLat, out double bsLon);
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 30.4, 0.0, out double sLat, out double sLon);
        double s0Lat = sLat, s0Lon = sLon;

        bool moved = DockingGeometry.ClampStopToOccupancy(
            ppLat, ppLon, bsLat, bsLon, hdg, threshold,
            ref sLat, ref sLon, out double gap, out _, out _);

        Assert.False(moved);
        Assert.Equal(s0Lat, sLat);
        Assert.Equal(s0Lon, sLon);
        Assert.Equal(25.1, gap, 1e-3);
    }

    [Fact]
    public void ClampStopToOccupancy_is_a_no_op_when_the_datum_is_already_inside_the_circle()
    {
        // KATL C20: T=25, gap=12.7, offset 0 -> datum already inside the circle -> no-op.
        double ppLat = 33.64, ppLon = -84.43, hdg = 90.0, threshold = 25.0;
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 12.7, 0.0, out double bsLat, out double bsLon);
        double sLat = bsLat, sLon = bsLon;
        double s0Lat = sLat, s0Lon = sLon;

        bool moved = DockingGeometry.ClampStopToOccupancy(
            ppLat, ppLon, bsLat, bsLon, hdg, threshold,
            ref sLat, ref sLon, out _, out double desired, out _);

        Assert.False(moved);
        Assert.Equal(s0Lat, sLat);
        Assert.Equal(s0Lon, sLon);
        Assert.Equal(12.7, desired, 1e-3);
    }

    [Fact]
    public void ClampStopToOccupancy_is_a_no_op_when_desired_is_within_threshold()
    {
        // Inside the circle with a forward offset (gap 20, datum 23, T 25): 23 <= 25 -> no-op,
        // so currently-working docks stay byte-identical.
        double ppLat = 40.0, ppLon = -80.0, hdg = 180.0, threshold = 25.0;
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 20.0, 0.0, out double bsLat, out double bsLon);
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 23.0, 0.0, out double sLat, out double sLon);
        double s0Lat = sLat, s0Lon = sLon;

        bool moved = DockingGeometry.ClampStopToOccupancy(
            ppLat, ppLon, bsLat, bsLon, hdg, threshold,
            ref sLat, ref sLon, out _, out _, out _);

        Assert.False(moved);
        Assert.Equal(s0Lat, sLat);
        Assert.Equal(s0Lon, sLon);
    }

    [Fact]
    public void ClampStopToOccupancy_floors_the_clamped_along_track_at_zero()
    {
        // Degenerate profile: threshold (1 m) < OccupancyClampMarginMetres (2 m). The clamp
        // still fires, but must floor clampedAlong at 0 (parking point), never negative
        // (which would pull the datum BEHIND this_parking_pos).
        double ppLat = 51.0, ppLon = 0.0, hdg = 270.0, threshold = 1.0;
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 0.5, 0.0, out double bsLat, out double bsLon);
        DockingGeometry.ShiftStopMetres(ppLat, ppLon, hdg, 4.0, 0.0, out double sLat, out double sLon);

        bool moved = DockingGeometry.ClampStopToOccupancy(
            ppLat, ppLon, bsLat, bsLon, hdg, threshold,
            ref sLat, ref sLon, out _, out _, out double clamped);

        Assert.True(moved);
        Assert.Equal(0.0, clamped, 1e-9);
        Assert.Equal(ppLat, sLat, 1e-7);
        Assert.Equal(ppLon, sLon, 1e-7);
    }
}
