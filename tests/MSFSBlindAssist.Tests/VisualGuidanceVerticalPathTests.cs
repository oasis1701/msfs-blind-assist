using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The vertical path on final (visual guidance stage 5), through the real manager
/// (<see cref="VisualGuidanceTestRig"/>, runway 09, 3°, 140 kt, updates at 30 Hz). Pressed on final 6 nm
/// out, so the phase is Final at once. The aircraft holds 2° of pitch descending at 742 fpm, a 3.0° flight
/// path, so its learned trim is 5.0° (pitch minus flight path). The live angle of attack reads 7°: the cue
/// never uses it (VG-25), so a cue built on it would sit 2° high.
/// </summary>
public class VisualGuidanceVerticalPathTests
{
    private const double LiveAoaDeg = 7.0;

    private static VisualGuidanceTestRig OnFinal(double alongNm = 6.0)
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(alongNm, 0, 0, aoaDeg: LiveAoaDeg);
        rig.UpdateAt(0);
        rig.FlyFor(10, 30);
        Assert.Equal("Final", rig.Phase);
        return rig;
    }

    private static void Hold(VisualGuidanceTestRig rig, double seconds, double alongNm, double crossNm = 0, double altOffsetFt = 0)
        => rig.FlyFor(seconds, 30, _ => rig.Place(alongNm, crossNm, 0, altOffsetFt: altOffsetFt, aoaDeg: LiveAoaDeg));

    // Trim 5.0 minus the glide path's 3.0.
    [Fact]
    public void On_the_path_the_cue_is_trim_minus_the_glide_path()
        => Assert.Equal(2.0, OnFinal().Pitch, 0.05);

    // 100 ft above the path at 140 kt: atan(100 / (the correction time × 236.3 ft/s)) steeper, 0.90° at
    // the default 27 s (1.21° at the spec's 20).
    [Fact]
    public void Above_the_path_asks_for_less_pitch()
    {
        var rig = OnFinal();
        double onPath = rig.Pitch;
        Hold(rig, 5, 6.0, altOffsetFt: 100);
        double steeper = Math.Atan(100.0 / (MSFSBlindAssist.Services.VisualGuidanceVerticalTuning.Default.CorrectionTimeConstantS * 140 * 1.68781))
            * 180 / Math.PI;
        Assert.Equal(onPath - steeper, rig.Pitch, 0.05);
        Assert.True(steeper > 0.5);
    }

    // 1,000 ft below: level flight (target descent 0), the trim itself, never a climb.
    [Fact]
    public void Below_the_path_never_asks_for_a_climb()
    {
        var rig = OnFinal();
        Hold(rig, 5, 6.0, altOffsetFt: -1000);
        Assert.Equal(0.0, rig.TargetDescent, 9);
        Assert.Equal(5.0, rig.Pitch, 0.05);
    }

    // Wheels 55 ft up (the datum 67 ft above the threshold, past it), 20 ft above the path: under the 60 ft
    // floor the correction stops and the cue holds the glide path's angle. (At 742 fpm the flare starts at
    // 53 ft, 4.3 s out: stage 6.)
    [Fact]
    public void The_correction_stops_at_sixty_feet()
    {
        var rig = OnFinal();
        double along = -13.0 / (6076.12 * Math.Tan(3.0 * Math.PI / 180));   // the path 47 ft up there, plus 20
        Hold(rig, 0.5, along, altOffsetFt: 20);
        Assert.Equal("Final", rig.Phase);
        Assert.Equal(3.0, rig.TargetDescent, 9);
    }

    // The old law locked to the natural rate inside 1 nm; the path law still corrects 60 ft there.
    [Fact]
    public void No_lock_at_one_mile()
    {
        var rig = OnFinal();
        Hold(rig, 1, 1.0, altOffsetFt: 60);
        Assert.True(rig.TargetDescent > 3.5, $"target descent {rig.TargetDescent:0.000}°");
    }

    // 0.5 nm right of the centreline at the path's height for 4.0 nm along the course: on the path. The
    // straight-line 4.03 nm would put the path 10 ft higher and ask for 2.88°.
    [Fact]
    public void Distance_is_along_the_course()
    {
        var rig = OnFinal(4.0);
        Hold(rig, 1, 4.0, crossNm: 0.5);
        Assert.Equal(3.00, rig.TargetDescent, 0.02);
    }

    [Fact]
    public void Its_mode_is_path()
        => Assert.Equal("Path", OnFinal().VerticalMode);

    // The checkpoint review's finding 2: a go-around back above the flare's 100 ft band (stage 6: a
    // balloon under it stays in the flare). Low in the flare the cue has risen toward the level attitude;
    // back on final it must leave from there at the profile's pitch rate (2.5°/s, 0.083° a frame at
    // 30 Hz), not jump to the path's cue in one frame.
    [Fact]
    public void Back_above_the_flare_the_cue_leaves_from_the_flare_pitch()
    {
        var rig = OnFinal();
        Hold(rig, 1.0 / 30, 0.0, altOffsetFt: -30);     // the datum 30 ft up, the wheels 18: flare
        Assert.Equal("Flare", rig.Phase);
        Hold(rig, 1, 0.0, altOffsetFt: -40);            // the wheels 8 ft up: the cue rises
        Assert.Equal("Flare", rig.Phase);
        double flarePitch = rig.Pitch;
        Assert.True(flarePitch > 3.0, $"the flare's cue is {flarePitch:0.00}°");
        Hold(rig, 1.0 / 30, 0.0, altOffsetFt: 53);      // the wheels 101 ft up: final again
        Assert.Equal("Final", rig.Phase);
        Assert.True(Math.Abs(rig.Pitch - flarePitch) <= 2.5 / 30 + 1e-6,
            $"the cue went from {flarePitch:0.00}° to {rig.Pitch:0.00}° in one frame");
    }
}
