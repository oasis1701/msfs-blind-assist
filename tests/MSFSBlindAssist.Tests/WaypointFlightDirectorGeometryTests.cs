// Characterization tests for MSFSBlindAssist.Navigation.WaypointFlightDirectorGeometry — the pure
// command math behind the synthetic en-route Waypoint Flight Director (Ctrl+F).
//
// Ports the golden cases from tools/WaypointFdProbe/Program.cs, the same way
// DockingGeometryTests ports tools/DockingProbe. The probe stays as a dev-loop tool; THIS file is
// what CI runs, so a regression in the command math is caught on every PR rather than only when
// someone remembers to run a console app that is not even in the solution.
//
// This is characterization, not spec verification: values come from the probe / are derived by
// reasoning about the source and confirmed by running the tests. If a literal ever disagrees with
// actual output, correct the test to match real output — not the other way around.
//
// Sign conventions under test (right-positive, "standard"):
//   track error  > 0  → the fix is RIGHT of the current ground track
//   bank command > 0  → roll right       pitch command > 0 → nose up
//   cross-track  > 0  → aircraft is RIGHT of the course line

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class WaypointFlightDirectorGeometryTests
{
    private const double Eps = 1e-6;

    // --- NormalizeSigned ---------------------------------------------------

    [Theory]
    [InlineData(350.0, -10.0)]
    [InlineData(-350.0, 10.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(10.0, 10.0)]
    // Both ±180 land on +180: the impl wraps (-180, +180], so -180 is pushed up to 180.
    [InlineData(180.0, 180.0)]
    [InlineData(-180.0, 180.0)]
    [InlineData(720.0, 0.0)]
    public void NormalizeSigned_wraps_into_range(double input, double expected)
    {
        Assert.Equal(expected, WaypointFlightDirectorGeometry.NormalizeSigned(input), Eps);
    }

    // --- TrackError --------------------------------------------------------

    [Fact]
    public void TrackError_is_negative_when_the_fix_is_left_of_track()
    {
        // Bearing to fix 350, ground track 010 → the fix is 20° LEFT.
        Assert.Equal(-20.0, WaypointFlightDirectorGeometry.TrackError(350, 10), Eps);
    }

    [Fact]
    public void TrackError_is_positive_when_the_fix_is_right_of_track()
    {
        Assert.Equal(20.0, WaypointFlightDirectorGeometry.TrackError(10, 350), Eps);
    }

    // --- CommandedBankDeg --------------------------------------------------

    [Fact]
    public void CommandedBankDeg_is_proportional_to_track_error()
    {
        double bank = WaypointFlightDirectorGeometry.CommandedBankDeg(
            trackErrorDeg: 10, yawRateDegPerSec: 0, kRoll: 1.1, bankRateLeadSec: 1.0, maxBankDeg: 25);
        Assert.Equal(11.0, bank, 1e-9);
    }

    [Theory]
    [InlineData(40.0, 25.0)]     // clamps to the cap turning right
    [InlineData(-40.0, -25.0)]   // and symmetrically turning left
    public void CommandedBankDeg_clamps_to_the_bank_cap(double trackError, double expected)
    {
        double bank = WaypointFlightDirectorGeometry.CommandedBankDeg(
            trackError, yawRateDegPerSec: 0, kRoll: 1.1, bankRateLeadSec: 1.0, maxBankDeg: 25);
        Assert.Equal(expected, bank, 1e-9);
    }

    [Fact]
    public void CommandedBankDeg_rate_lead_reduces_the_command_when_already_turning_toward_target()
    {
        // Rolling right (yaw +5°/s) into a right-hand error rolls out early instead of overshooting.
        double noLead = WaypointFlightDirectorGeometry.CommandedBankDeg(20, 0, 1.1, 1.3, 25);
        double withLead = WaypointFlightDirectorGeometry.CommandedBankDeg(20, 5, 1.1, 1.3, 25);
        Assert.True(withLead < noLead, $"expected lead to reduce the command, got {withLead} vs {noLead}");
    }

    [Fact]
    public void CommandedBankDeg_rate_lead_increases_the_command_when_turning_away()
    {
        double noLead = WaypointFlightDirectorGeometry.CommandedBankDeg(10, 0, 1.1, 1.3, 25);
        double turningAway = WaypointFlightDirectorGeometry.CommandedBankDeg(10, -5, 1.1, 1.3, 25);
        Assert.True(turningAway > noLead, $"expected a larger command, got {turningAway} vs {noLead}");
    }

    // --- RequiredFpaDeg / CommandedPitchDeg --------------------------------

    [Fact]
    public void RequiredFpaDeg_climb_3000ft_over_10nm_is_about_2_83_degrees()
    {
        double fpa = WaypointFlightDirectorGeometry.RequiredFpaDeg(
            targetAltFt: 8000, altMslFt: 5000, distToFixNm: 10);
        Assert.Equal(2.83, fpa, 0.01);
    }

    [Fact]
    public void RequiredFpaDeg_is_negative_for_a_descent()
    {
        double fpa = WaypointFlightDirectorGeometry.RequiredFpaDeg(5000, 8000, 10);
        Assert.Equal(-2.83, fpa, 0.01);
    }

    [Fact]
    public void RequiredFpaDeg_is_guarded_to_zero_near_overhead()
    {
        // Inside ~0.05 NM the command would blow up; it is pinned to level instead.
        Assert.Equal(0.0, WaypointFlightDirectorGeometry.RequiredFpaDeg(8000, 5000, 0.01), Eps);
    }

    [Fact]
    public void CommandedPitchDeg_is_fpa_plus_aoa()
    {
        double fpa = WaypointFlightDirectorGeometry.RequiredFpaDeg(8000, 5000, 10);
        double pitch = WaypointFlightDirectorGeometry.CommandedPitchDeg(fpa, aoaDeg: 3.0, maxPitchDeg: 12);
        Assert.Equal(fpa + 3.0, pitch, Eps);
    }

    [Theory]
    [InlineData(30.0, 5.0, 12.0)]      // clamps nose-up
    [InlineData(-30.0, 5.0, -12.0)]    // clamps nose-down
    public void CommandedPitchDeg_clamps_to_the_pitch_cap(double fpa, double aoa, double expected)
    {
        Assert.Equal(expected, WaypointFlightDirectorGeometry.CommandedPitchDeg(fpa, aoa, 12), Eps);
    }

    // --- ProjectedCrossingAltFt --------------------------------------------

    [Fact]
    public void ProjectedCrossingAltFt_projects_the_current_vertical_speed_to_the_fix()
    {
        // Descending 1000 fpm, 6 NM out at 180 kt → 2 minutes → -2000 ft.
        double projected = WaypointFlightDirectorGeometry.ProjectedCrossingAltFt(
            altMslFt: 10000, vsFpm: -1000, distToFixNm: 6, groundSpeedKts: 180);
        Assert.Equal(8000.0, projected, 1.0);
    }

    [Fact]
    public void ProjectedCrossingAltFt_holds_current_altitude_when_stopped()
    {
        // Below 1 kt the time-to-fix is meaningless (and would divide toward infinity).
        Assert.Equal(10000.0, WaypointFlightDirectorGeometry.ProjectedCrossingAltFt(10000, -1000, 6, 0), Eps);
    }

    // --- ResolveVerticalTarget ---------------------------------------------

    [Fact]
    public void AtOrAbove_is_neutral_when_projected_to_arrive_above()
    {
        var (active, _) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.AtOrAbove, 6000, null, projectedCrossingAltFt: 7000);
        Assert.False(active);
    }

    [Fact]
    public void AtOrAbove_commands_a_climb_when_projected_to_arrive_low()
    {
        var (active, target) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.AtOrAbove, 6000, null, projectedCrossingAltFt: 5000);
        Assert.True(active);
        Assert.Equal(6000.0, target, Eps);
    }

    [Fact]
    public void AtOrBelow_is_neutral_when_projected_to_arrive_below()
    {
        var (active, _) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.AtOrBelow, 6000, null, projectedCrossingAltFt: 5000);
        Assert.False(active);
    }

    [Fact]
    public void AtOrBelow_commands_a_descent_when_projected_to_bust_above()
    {
        var (active, target) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.AtOrBelow, 6000, null, projectedCrossingAltFt: 9000);
        Assert.True(active);
        Assert.Equal(6000.0, target, Eps);
    }

    [Fact]
    public void Between_is_neutral_inside_the_window()
    {
        var (active, _) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.Between, 5000, 7000, projectedCrossingAltFt: 6000);
        Assert.False(active);
    }

    [Fact]
    public void Between_commands_down_to_the_upper_bound_when_above_it()
    {
        var (active, target) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.Between, 5000, 7000, projectedCrossingAltFt: 8000);
        Assert.True(active);
        Assert.Equal(7000.0, target, Eps);
    }

    [Fact]
    public void Between_commands_up_to_the_lower_bound_when_below_it()
    {
        var (active, target) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.Between, 5000, 7000, projectedCrossingAltFt: 4000);
        Assert.True(active);
        Assert.Equal(5000.0, target, Eps);
    }

    [Fact]
    public void Between_orders_its_bounds_so_a_reversed_pair_still_works()
    {
        var (active, target) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.Between, 7000, 5000, projectedCrossingAltFt: 8000);
        Assert.True(active);
        Assert.Equal(7000.0, target, Eps);
    }

    [Fact]
    public void At_always_commands_toward_the_target()
    {
        var (active, target) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.At, 4000, null, projectedCrossingAltFt: 9000);
        Assert.True(active);
        Assert.Equal(4000.0, target, Eps);
    }

    [Fact]
    public void None_is_never_active()
    {
        var (active, _) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.None, 4000, 9000, projectedCrossingAltFt: 1000);
        Assert.False(active);
    }

    [Theory]
    [InlineData(AltitudeConstraintType.At)]
    [InlineData(AltitudeConstraintType.AtOrAbove)]
    [InlineData(AltitudeConstraintType.AtOrBelow)]
    [InlineData(AltitudeConstraintType.Between)]
    public void A_missing_lower_bound_is_never_active(AltitudeConstraintType constraint)
    {
        var (active, _) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            constraint, null, 7000, projectedCrossingAltFt: 1000);
        Assert.False(active);
    }

    [Fact]
    public void Between_without_an_upper_bound_is_not_active()
    {
        var (active, _) = WaypointFlightDirectorGeometry.ResolveVerticalTarget(
            AltitudeConstraintType.Between, 5000, null, projectedCrossingAltFt: 9000);
        Assert.False(active);
    }

    // --- CrossTrackNm ------------------------------------------------------

    [Fact]
    public void CrossTrackNm_east_of_a_north_course_is_right_positive()
    {
        Assert.Equal(6.0, WaypointFlightDirectorGeometry.CrossTrackNm(6, 90, 0), 0.05);
    }

    [Fact]
    public void CrossTrackNm_west_of_a_north_course_is_left_negative()
    {
        Assert.Equal(-6.0, WaypointFlightDirectorGeometry.CrossTrackNm(6, 270, 0), 0.05);
    }

    [Fact]
    public void CrossTrackNm_on_the_course_line_is_zero()
    {
        Assert.Equal(0.0, WaypointFlightDirectorGeometry.CrossTrackNm(6, 0, 0), 0.01);
    }

    [Fact]
    public void CrossTrackNm_behind_the_fix_on_the_line_is_still_zero()
    {
        // The course line is infinite in both directions — a point on the inbound side (bearing
        // from the fix = course + 180) is ON the line, not 2× the distance off it.
        Assert.Equal(0.0, WaypointFlightDirectorGeometry.CrossTrackNm(6, 180, 0), 0.01);
    }

    [Fact]
    public void CrossTrackNm_right_of_a_270_course_is_positive()
    {
        Assert.True(WaypointFlightDirectorGeometry.CrossTrackNm(6, 360, 270) > 0);
    }

    [Fact]
    public void CrossTrackNm_left_of_a_270_course_is_negative()
    {
        Assert.True(WaypointFlightDirectorGeometry.CrossTrackNm(6, 180, 270) < 0);
    }

    // --- CourseInterceptTrackDeg -------------------------------------------

    [Fact]
    public void CourseIntercept_right_of_course_turns_left_and_caps_at_max_intercept()
    {
        // 2 NM right × 20°/NM = 40°, capped at 40 → fly 270 − 40 = 230.
        Assert.Equal(230.0, WaypointFlightDirectorGeometry.CourseInterceptTrackDeg(270, 2, 40, 20), Eps);
    }

    [Fact]
    public void CourseIntercept_left_of_course_turns_right()
    {
        Assert.Equal(290.0, WaypointFlightDirectorGeometry.CourseInterceptTrackDeg(270, -1, 40, 20), Eps);
    }

    [Fact]
    public void CourseIntercept_on_course_holds_the_course()
    {
        Assert.Equal(270.0, WaypointFlightDirectorGeometry.CourseInterceptTrackDeg(270, 0, 40, 20), Eps);
    }

    [Fact]
    public void CourseIntercept_shallows_as_the_cross_track_closes()
    {
        double far = WaypointFlightDirectorGeometry.CourseInterceptTrackDeg(270, 2, 40, 20);
        double near = WaypointFlightDirectorGeometry.CourseInterceptTrackDeg(270, 0.5, 40, 20);
        Assert.True(near > far, $"expected a shallower intercept closer in, got {near} vs {far}");
    }

    [Fact]
    public void CourseIntercept_wraps_below_zero()
    {
        Assert.Equal(350.0, WaypointFlightDirectorGeometry.CourseInterceptTrackDeg(10, 1, 40, 20), Eps);
    }

    [Fact]
    public void CourseIntercept_wraps_above_360()
    {
        Assert.Equal(10.0, WaypointFlightDirectorGeometry.CourseInterceptTrackDeg(350, -1, 40, 20), Eps);
    }

    // --- HasArrived --------------------------------------------------------

    [Fact]
    public void HasArrived_inside_the_capture_radius()
    {
        Assert.True(WaypointFlightDirectorGeometry.HasArrived(0.3, 100, 100, 0.5));
    }

    [Fact]
    public void HasArrived_abeam_counts_as_station_passage()
    {
        // Bearing now 100° off track — the fix has gone past the wing.
        Assert.True(WaypointFlightDirectorGeometry.HasArrived(2.0, 200, 100, 0.5));
    }

    [Fact]
    public void HasArrived_is_false_while_still_ahead_and_outside_the_radius()
    {
        Assert.False(WaypointFlightDirectorGeometry.HasArrived(2.0, 105, 100, 0.5));
    }
}
