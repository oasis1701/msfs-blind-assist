using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The planned descent while vectoring and on the pattern (visual guidance stage 5, plan Task 5), through
/// the real manager (<see cref="VisualGuidanceTestRig"/>, runway 09, 3°, 140 kt, 30 Hz). The aircraft
/// descends at 742 fpm with 2° of pitch, a 3° flight path, so its learned trim is 5.0° unless a test says
/// otherwise. Where the plan puts the aircraft is pinned by <see cref="VisualGuidanceVerticalTests"/>;
/// these pin the mode and the signs.
/// </summary>
public class VisualGuidancePlannedDescentTests
{
    private const double Trim = 5.0;

    // The right-side pattern from the far side (Decide at (−1, 3), heading 270, the reverse of runway 09):
    // pressed 2,000 ft above the field, the harness's far-side height, about 3,400 ft under the plan,
    // which still has a downwind, a base and a final to descend along.
    private static VisualGuidanceTestRig OnTheDownwind(double altOffsetFt = 0, double vsFpm = -742, double pitchDeg = 2.0)
    {
        var rig = new VisualGuidanceTestRig();
        double offset = VisualGuidanceJoiningTests.AboveFieldOffset(-1) + altOffsetFt;
        rig.Place(-1, 3, 180, altOffsetFt: offset, vsFpm: vsFpm, pitchDeg: pitchDeg);
        rig.UpdateAt(0);
        Assert.Equal("Pattern", rig.Decision);
        rig.FlyFor(5, 30);
        return rig;
    }

    private static double BaseAlong()
        => VisualGuidanceJoin.Decide(new VisualGuidanceApproachPoint(-1, 3), 180, 140, VisualGuidanceJoin.DefaultCaptureGain,
            new VisualGuidanceProfile()).Legs[1].AlongNm;

    // 7,000 ft higher, far above any plan: the steepest the plan allows (the glide path + 2°), so the cue
    // sits that far under the trim (VG-24).
    [Fact]
    public void Above_the_plan_asks_for_less_pitch_on_the_downwind()
    {
        var rig = OnTheDownwind(altOffsetFt: 7000);
        Assert.Equal("Plan", rig.VerticalMode);
        Assert.Equal(5.0, rig.TargetDescent, 9);
        Assert.Equal(Trim - 5.0, rig.Pitch, 0.05);
    }

    // The stage-4 final review's C1(b): a descending aircraft on the downwind heard its own pitch, no
    // vertical cue at all. Descending at 1,000 fpm (pitch 5° minus its 4.03° flight path, so the trim
    // stays 5.0) far under the plan, the cue asks for level flight: the trim, not the pilot's pitch.
    [Fact]
    public void A_descending_aircraft_on_the_downwind_gets_a_cue()
    {
        double pitch = Trim - Math.Atan(1000 / (140 * 101.269)) * 180 / Math.PI;
        var rig = OnTheDownwind(vsFpm: -1000, pitchDeg: pitch);
        Assert.Equal("Plan", rig.VerticalMode);
        Assert.Equal(0.0, rig.TargetDescent, 9);
        Assert.Equal(Trim, rig.Pitch, 0.05);
        Assert.True(Math.Abs(rig.Pitch - pitch) > 3.5, $"cue {rig.Pitch:0.00}°, own pitch {pitch:0.00}°");
    }

    // Vectoring straight in from 8 nm, 0.2 nm off the line: 650 ft under the path, at least 500 under the
    // plan (the floor, 150 ft under the path, or the higher sloped line): level, never a climb (VG-26).
    [Fact]
    public void Below_the_plan_asks_for_level()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(8, 0.2, 0, altOffsetFt: -650);
        rig.UpdateAt(0);
        Assert.Equal("Vectoring", rig.Phase);
        rig.FlyFor(3, 30);
        Assert.Equal("Plan", rig.VerticalMode);
        Assert.Equal(0.0, rig.TargetDescent, 9);
    }

    // On the pattern's final, past its join point (fixed where the base was placed, 6.14 nm here) and not
    // yet established: the plan's level part (1,854 ft, a quarter degree under the path at the join point)
    // would hold the aircraft level while the path came down through it, so it established above the path
    // (the light far-side runs, 82 ft). On final the plan never sits above the path: on the path at 5 nm,
    // the cue follows it down (the glide path's 3°) instead of asking for level.
    [Fact]
    public void On_final_the_plan_never_sits_above_the_path()
    {
        double aB = BaseAlong();
        var rig = OnTheDownwind();
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB - 0.5, 3, 180));
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB, 0.5, -90));
        rig.FlyFor(1, 30, beforeEach: _ => rig.Place(5.0, 0.1, 0));
        Assert.Equal("Vectoring", rig.Phase);
        Assert.Equal("Plan", rig.VerticalMode);
        Assert.Equal(3.0, rig.TargetDescent, 0.05);
    }

    // The final review's finding 1: vectoring from 9 nm out and 4 nm abeam ("cleared visual", flying
    // parallel), 10 ft under the floor (150 ft under the path at the straight-line 9.85 nm). The plan is
    // never below the floor, so this aircraft is under the plan and the cue asks for no more than the
    // glide path's 3°. The final-leg cap had taken the plan down to the path at 9 nm along the course
    // (2,926 ft, 120 ft under the floor) and asked 4° at once.
    [Fact]
    public void Wide_abeam_the_plan_never_goes_under_the_floor()
    {
        double along = 9, cross = 4, dist = Math.Sqrt(along * along + cross * cross);
        double offset = (dist - along) * 6076.12 * Math.Tan(3.0 * Math.PI / 180) - 150 - 10;
        var rig = new VisualGuidanceTestRig();
        rig.Place(along, cross, 0, altOffsetFt: offset);
        rig.UpdateAt(0);
        Assert.Equal("Vectoring", rig.Phase);
        rig.FlyFor(2, 30);
        Assert.Equal("Plan", rig.VerticalMode);
        Assert.True(rig.TargetDescent <= 3.0 + 1e-9, $"target descent {rig.TargetDescent:0.000}°");
    }

    // The owner's choice (2026-11-06, the final review's finding 3): vectored while already on or below the
    // glide path, BA keeps the aircraft where it is, descending with the path, instead of taking it down to
    // the floor, 150 ft under the path, first. On the path 0.2 nm off the line: the glide path's 3°, not the
    // 4.35° that took it to the floor at 1,100 fpm.
    [Fact]
    public void On_the_path_when_vectored_it_rides_the_path()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(6, 0.2, 0);
        rig.UpdateAt(0);
        Assert.Equal("Vectoring", rig.Phase);
        rig.FlyFor(3, 30);
        Assert.Equal("Plan", rig.VerticalMode);
        Assert.Equal(3.0, rig.TargetDescent, 0.05);
    }

    // 80 ft under the path when vectored: it stays 80 ft under, descending with the path (the floor would
    // have asked 3.6°), and joins the path from below once established.
    [Fact]
    public void Under_the_path_when_vectored_it_keeps_its_height_under_it()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(6, 0.2, 0, altOffsetFt: -80);
        rig.UpdateAt(0);
        rig.FlyFor(3, 30);
        Assert.Equal(3.0, rig.TargetDescent, 0.05);
    }

    // Kept where it was when BA started vectoring, not wherever it has sunk to since: started on the path,
    // now 60 ft under it, the cue asks for a shallower descent (the path coming down to it), not the path's
    // 3° that would keep it 60 ft low (a light aircraft cutting in close joined 70 ft low and heard "Too low").
    [Fact]
    public void Sinking_under_where_it_started_asks_back()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(6, 0.2, 0);
        rig.UpdateAt(0);
        rig.FlyFor(1, 30);
        rig.FlyFor(3, 30, _ => rig.Place(6, 0.2, 0, altOffsetFt: -60));
        Assert.True(rig.TargetDescent < 2.7, $"60 ft under where it started: target {rig.TargetDescent:0.00}°");
    }

    // Vectored from 300 ft above the path, it still joins from below: the plan is the floor, and it stays
    // the floor after the aircraft has come down through the path (decided once, when BA starts vectoring).
    [Fact]
    public void From_above_it_still_joins_from_below()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(6, 0.2, 0, altOffsetFt: 300);
        rig.UpdateAt(0);
        rig.FlyFor(3, 30);
        Assert.Equal(5.0, rig.TargetDescent, 9);   // 450 ft over the floor: the steepest, the glide path + 2°
        rig.FlyFor(3, 30, _ => rig.Place(6, 0.2, 0, altOffsetFt: -10));
        Assert.True(rig.TargetDescent > 3.5, $"10 ft under the path, 140 over the floor: target {rig.TargetDescent:0.00}°");
    }

    // The downwind, the base, and final before established all fly the plan; established, the path.
    [Fact]
    public void Its_mode_is_plan_on_every_leg()
    {
        double aB = BaseAlong();
        var rig = OnTheDownwind();
        Assert.Equal("Plan", rig.VerticalMode);
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB - 0.5, 3, 180));
        Assert.Equal("Vectoring", rig.Phase);
        Assert.Equal("Plan", rig.VerticalMode);
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB, 0.5, -90));
        Assert.Equal("Vectoring", rig.Phase);
        Assert.Equal("Plan", rig.VerticalMode);
        rig.FlyFor(3.5, 30, beforeEach: _ => rig.Place(aB, 0.02, 0));
        Assert.Equal("Final", rig.Phase);
        Assert.Equal("Path", rig.VerticalMode);
    }
}
