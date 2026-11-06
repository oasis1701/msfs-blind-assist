using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The vertical path's pure law (<see cref="VisualGuidanceVertical"/>), stage 5 of the visual guidance
/// redesign: a 3° glide path, the profile's 60 ft glidepath bias and 140 kt unless a test says otherwise.
/// Heights are above the threshold; a height error is + above the reference; a descent angle is + down.
/// </summary>
public class VisualGuidanceVerticalTests
{
    private const double Gpa = 3.0, Bias = 60.0, Gs = 140.0, T = 20.0;

    [Fact]
    public void Path_height()
    {
        Assert.Equal(1015.3, VisualGuidanceVertical.PathHeightFt(3, Gpa, Bias), 0.5);
        Assert.Equal(60.0, VisualGuidanceVertical.PathHeightFt(0, Gpa, Bias), 9);
    }

    [Fact]
    public void Flight_path_angle()
    {
        Assert.Equal(-2.997, VisualGuidanceVertical.FlightPathAngleDeg(-742, 140), 0.01);
        Assert.True(double.IsFinite(VisualGuidanceVertical.FlightPathAngleDeg(-742, 0)));
    }

    [Fact]
    public void Above_the_path_is_steeper()
    {
        Assert.Equal(4.21, VisualGuidanceVertical.TargetDescentDeg(3, +100, Gs, T, 0, 5), 0.02);
        Assert.Equal(1.79, VisualGuidanceVertical.TargetDescentDeg(3, -100, Gs, T, 0, 5), 0.02);
    }

    [Fact]
    public void The_steep_side_stops_at_two_degrees()
        => Assert.Equal(5.0, VisualGuidanceVertical.TargetDescentDeg(3, +2000, Gs, T, 0, 5), 9);

    [Fact]
    public void Below_the_path_is_level_not_a_climb()
        => Assert.Equal(0.0, VisualGuidanceVertical.TargetDescentDeg(3, -2000, Gs, T, 0, 5), 9);

    // A height error of one degree's worth of 20 s at 140 kt (236.3 ft/s) asks for one degree.
    [Fact]
    public void Correction_takes_twenty_seconds()
        => Assert.Equal(1.0, VisualGuidanceVertical.CorrectionDeg(236.3 * 20 * Math.Tan(1.0 * Math.PI / 180), 140, 20), 0.01);

    // Half a degree below the path, never under the 60 ft warning floor (plan decision 13).
    [Fact]
    public void Too_low_margin()
    {
        Assert.Equal(159.5, VisualGuidanceVertical.TooLowMarginFt(3, Gpa), 1.0);
        Assert.Equal(79.7, VisualGuidanceVertical.TooLowMarginFt(1.5, Gpa), 1.0);
        Assert.Equal(60.0, VisualGuidanceVertical.TooLowMarginFt(1, Gpa), 9);    // half a degree is 53.2
        Assert.Equal(60.0, VisualGuidanceVertical.TooLowMarginFt(0.2, Gpa), 9);
    }

    [Fact]
    public void Gate_distance()
    {
        Assert.Equal(2.952, VisualGuidanceVertical.GateAlongNm(1000, Gpa, Bias), 0.005);
        Assert.Equal(0.754, VisualGuidanceVertical.GateAlongNm(300, Gpa, Bias), 0.005);
    }

    // 600 ft high: at 4 nm the 2° steeper path removes only 223 ft by the gate (needs 380 with the half
    // degree there); at 7 nm it removes 863 (needs 1,020). Inside the gate the margin is the half degree
    // at the aircraft's own distance: 106 ft at 2 nm.
    [Fact]
    public void Too_high_at_four_miles_not_seven()
    {
        Assert.True(VisualGuidanceVertical.TooHigh(4, 600, Gpa, Bias, 1000, 2));
        Assert.False(VisualGuidanceVertical.TooHigh(7, 600, Gpa, Bias, 1000, 2));
        Assert.False(VisualGuidanceVertical.TooHigh(2, 100, Gpa, Bias, 1000, 2));
        Assert.True(VisualGuidanceVertical.TooHigh(2, 110, Gpa, Bias, 1000, 2));
    }

    // A 3° line back from the level-off point a quarter of a degree under the path at the join, the
    // level part 20 s long (0.78 nm at 140 kt); never under the floor, 150 ft below the path at the
    // aircraft's own distance.
    [Fact]
    public void Planned_height()
    {
        var sloped = VisualGuidanceVertical.PlannedHeight(5, 6.2, 8, Gpa, Bias, Gs);
        Assert.Equal(3214.0, sloped.HeightFt, 5.0);
        Assert.Equal(3.0, sloped.FeedForwardDeg, 9);
        var floor = VisualGuidanceVertical.PlannedHeight(0.5, 6.2, 8, Gpa, Bias, Gs);
        Assert.Equal(2457.0, floor.HeightFt, 5.0);
        Assert.Equal(0.0, floor.FeedForwardDeg, 9);
    }

    [Fact]
    public void A_jet_arrives_on_the_floor()
    {
        var plan = VisualGuidanceVertical.PlannedHeight(0, 6.2, 6.3, Gpa, Bias, Gs);
        Assert.Equal(1916.0, plan.HeightFt, 5.0);    // the quarter degree would be 1,870
        Assert.Equal(0.0, plan.FeedForwardDeg, 9);
    }

    [Fact]
    public void A_light_aircraft_arrives_a_quarter_degree_low()
    {
        var plan = VisualGuidanceVertical.PlannedHeight(0, 1.0, 1.0, Gpa, Bias, 65);
        Assert.Equal(351.8, plan.HeightFt, 1.0);     // the floor would be 228
        Assert.Equal(0.0, plan.FeedForwardDeg, 9);
    }

    // London City's 5.5°: the path, the 2° limit and the half degree are the runway's own.
    [Fact]
    public void Steep_runway_uses_its_own_angle()
    {
        Assert.Equal(5.5, VisualGuidanceVertical.TargetDescentDeg(5.5, 0, Gs, T, 0, 7.5), 9);
        Assert.Equal(7.5, VisualGuidanceVertical.TargetDescentDeg(5.5, +3000, Gs, T, 0, 7.5), 9);
        Assert.Equal(0.0, VisualGuidanceVertical.TargetDescentDeg(5.5, -3000, Gs, T, 0, 7.5), 9);
        Assert.Equal(160.4, VisualGuidanceVertical.TooLowMarginFt(3, 5.5), 1.0);
    }

    // The approach harness's picks (docs/virtual-pilot.md, "Stage 5"): the correction 27 s, since at the
    // spec's 20 s the light aircraft's self-feedback read −0.31 against 0.3; the rest the spec's values.
    [Fact]
    public void The_tuning_defaults_are_the_harness_picks()
    {
        var t = VisualGuidanceVerticalTuning.Default;
        Assert.Equal(27.0, t.CorrectionTimeConstantS);
        Assert.Equal(2.0, t.PathLimitDeg);
        Assert.Equal(60.0, t.CorrectionFloorWheelFt);
        Assert.Equal(6.0, t.TrimTimeConstantS);
        Assert.Equal(1.0, t.PathAngleTimeConstantS);
        Assert.Equal(200.0, t.LevelVsFpm);
        Assert.Equal(5.0, t.LevelHoldS);
        Assert.Equal(1.5, t.HoldBreakPitchDeg);
    }

    // A slew or a pause: every function stays finite at a ground speed of 0.
    [Fact]
    public void A_stopped_aircraft_gets_finite_vertical_numbers()
    {
        Assert.True(double.IsFinite(VisualGuidanceVertical.FlightPathAngleDeg(-742, 0)));
        Assert.True(double.IsFinite(VisualGuidanceVertical.CorrectionDeg(300, 0, T)));
        Assert.True(double.IsFinite(VisualGuidanceVertical.TargetDescentDeg(3, 300, 0, T, 0, 5)));
        var plan = VisualGuidanceVertical.PlannedHeight(5, 6.2, 8, Gpa, Bias, 0);
        Assert.True(double.IsFinite(plan.HeightFt));
        Assert.True(double.IsFinite(plan.FeedForwardDeg));
        Assert.True(double.IsFinite(VisualGuidanceVertical.TooLowMarginFt(3, Gpa)));
        Assert.True(double.IsFinite(VisualGuidanceVertical.GateAlongNm(1000, Gpa, Bias)));
        var learner = new VisualGuidanceTrimLearner(6.0, VisualGuidanceVerticalTuning.Default);
        learner.Update(2.0, -742, 0, 1.0 / 30);
        Assert.True(double.IsFinite(learner.TrimDeg));
        Assert.True(double.IsFinite(learner.PathAngleDeg));
    }
}
