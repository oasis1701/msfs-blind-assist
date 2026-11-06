using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The joining level hold (visual guidance stage 5, plan Task 4), through the real manager
/// (<see cref="VisualGuidanceTestRig"/>, runway 09, 140 kt, 30 Hz). Joining from 3 nm right of final,
/// 12 nm out, on ATC's 30° cut. The aircraft first descends at 742 fpm with 2° of pitch, a 3° flight path,
/// so its learned trim (pitch minus flight path) is 5.0°; level, it flies 5° of pitch, and descending at
/// 1,000 or 1,500 fpm the pitch is 5° minus that flight path, so the trim stays 5.0° throughout.
/// </summary>
public class VisualGuidanceLevelHoldManagerTests
{
    private const double Along = 12, Cross = 3, Rel = -30, LevelPitch = 5.0, Hz = 30;

    private static VisualGuidanceTestRig Joining()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(Along, Cross, Rel);
        rig.UpdateAt(0);
        rig.FlyFor(10, 30);
        Assert.Equal("Joining", rig.Phase);
        return rig;
    }

    private static void Fly(VisualGuidanceTestRig rig, double seconds, double vsFpm, double pitchDeg, double altOffsetFt,
        Action<double>? afterEach = null)
        => rig.FlyFor(seconds, 30, _ => rig.Place(Along, Cross, Rel, vsFpm: vsFpm, altOffsetFt: altOffsetFt, pitchDeg: pitchDeg), afterEach);

    /// <summary>Joining, then level for 6 s at the starting altitude: held there.</summary>
    private static VisualGuidanceTestRig Held()
    {
        var rig = Joining();
        Fly(rig, 6, 0, LevelPitch, 0);
        Assert.Equal("Hold", rig.VerticalMode);
        return rig;
    }

    private static double CorrectionDeg(double errorFt)
        => Math.Atan(errorFt / (VisualGuidanceVerticalTuning.Default.CorrectionTimeConstantS * 140 * 1.68781)) * 180 / Math.PI;

    // ATC's "descend and maintain": the altitude is the pilot's, so the pair matches and asks for nothing.
    [Fact]
    public void Descending_the_cue_sits_on_the_pilots_pitch()
    {
        var rig = Joining();
        Fly(rig, 20, -1500, -1.0, 0, afterEach: _ =>
        {
            Assert.Equal(-1.0, rig.Pitch, 0.01);
            Assert.Equal("Follow", rig.VerticalMode);
        });
    }

    // Level (50 fpm, under 200) from t0, the altitude drifting down with it: held 5 s later, at the
    // altitude of that update. Held at the start's instead (4 ft higher), the cue would ask 0.04° up.
    [Fact]
    public void Level_for_five_seconds_it_holds()
    {
        var rig = Joining();
        double t0 = rig.Now;
        double Offset(double t) => -50.0 / 60 * (t - t0);
        double? holdT = null, holdOffset = null;
        rig.FlyFor(6, 30, t => rig.Place(Along, Cross, Rel, vsFpm: -50, altOffsetFt: Offset(t), pitchDeg: LevelPitch),
            t => { if (holdT == null && rig.VerticalMode == "Hold") { holdT = t; holdOffset = Offset(t); } });
        Assert.NotNull(holdT);
        Assert.InRange(holdT!.Value - t0, 5.0, 5.0 + 2 / Hz);

        Fly(rig, 1, 0, LevelPitch, holdOffset!.Value);
        Assert.Equal("Hold", rig.VerticalMode);
        Assert.Equal(0.0, rig.TargetDescent, 0.002);
    }

    // Held: 80 ft high asks down (the correction for 80 ft, 0.72° at 27 s), 80 ft low asks back up the
    // same, and far below the climb back stops at 2°.
    [Fact]
    public void The_hold_asks_back_from_both_sides()
    {
        var rig = Held();
        double c = CorrectionDeg(80);

        Fly(rig, 1, 0, LevelPitch, +80);
        Assert.Equal("Hold", rig.VerticalMode);
        Assert.Equal(c, rig.TargetDescent, 0.01);
        Assert.True(rig.Pitch < LevelPitch - 0.5, $"80 ft high: cue {rig.Pitch:0.00}°");

        Fly(rig, 1, 0, LevelPitch, -80);
        Assert.Equal("Hold", rig.VerticalMode);
        Assert.Equal(-c, rig.TargetDescent, 0.01);
        Assert.True(rig.Pitch > LevelPitch + 0.5, $"80 ft low: cue {rig.Pitch:0.00}°");

        Fly(rig, 1 / Hz, 0, LevelPitch, -400);
        Assert.Equal(-VisualGuidanceVerticalTuning.Default.PathLimitDeg, rig.TargetDescent, 9);
    }

    // Held, the pilot pitches 2° under the cue (ATC cleared a descent): the hold lets go on that update
    // and the cue sits on the pilot's pitch for the whole descent.
    [Fact]
    public void A_deliberate_descent_is_never_fought()
    {
        var rig = Held();
        double cue = rig.Pitch;
        Fly(rig, 1 / Hz, -800, cue - 2.0, 0);
        Assert.Equal("Follow", rig.VerticalMode);
        Assert.Equal(cue - 2.0, rig.Pitch, 0.01);

        Fly(rig, 10, -800, cue - 2.0, 0, afterEach: _ =>
        {
            Assert.Equal("Follow", rig.VerticalMode);
            Assert.Equal(cue - 2.0, rig.Pitch, 0.01);
        });
    }

    // The final review's finding 2: held, the air carries the aircraft up 200 fpm for a minute (an updraft, a
    // power change, a busy readback) while the pilot keeps the level pitch. The cue walks down, asking back,
    // past 1.5° from the pilot's pitch at about 167 ft; the pilot has not moved, so the hold must not let go
    // (the spec: "a deliberate pitch change of more than about 1.5° from the held pitch").
    [Fact]
    public void Drifting_off_without_pitching_never_lets_go()
    {
        var rig = Held();
        double t0 = rig.Now;
        rig.FlyFor(60, 30, t => rig.Place(Along, Cross, Rel, vsFpm: 200, pitchDeg: LevelPitch, altOffsetFt: 200.0 / 60 * (t - t0)),
            _ => Assert.Equal("Hold", rig.VerticalMode));
        Assert.True(rig.TargetDescent > 1.5, $"target descent {rig.TargetDescent:0.00}° 200 ft above the held altitude");
    }

    // "Descend and maintain" again: held at A, down 1,000 ft at 1,000 fpm, level 5 s: held at A − 1,000.
    // Still held at A, the cue would ask the 2° climb back.
    [Fact]
    public void A_second_level_off_is_held_at_the_new_altitude()
    {
        var rig = Held();
        double t0 = rig.Now;
        double descentPitch = LevelPitch - Math.Atan(1000 / (140 * 101.269)) * 180 / Math.PI;
        rig.FlyFor(60, 30, t => rig.Place(Along, Cross, Rel, vsFpm: -1000, pitchDeg: descentPitch,
            altOffsetFt: -1000.0 / 60 * (t - t0)));
        Assert.Equal("Follow", rig.VerticalMode);

        Fly(rig, 6, 0, LevelPitch, -1000);
        Assert.Equal("Hold", rig.VerticalMode);
        Assert.Equal(0.0, rig.TargetDescent, 0.01);
    }
}
