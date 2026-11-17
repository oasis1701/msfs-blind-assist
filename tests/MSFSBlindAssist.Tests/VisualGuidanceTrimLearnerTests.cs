using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The trim learner (<see cref="VisualGuidanceTrimLearner"/>): the aircraft's pitch minus its flight path,
/// smoothed over about 6 s, so a push or a pull barely moves it. A jet at 140 kt, updates at 30 Hz.
/// </summary>
public class VisualGuidanceTrimLearnerTests
{
    private const double Dt = 1.0 / 30, Gs = 140.0, FpmPerKt = 101.269;

    // The vertical speed of a flight path angle at 140 kt, as the learner reads it back (atan).
    private static double VsFor(double gammaDeg) => Math.Tan(gammaDeg * Math.PI / 180) * Gs * FpmPerKt;

    private static VisualGuidanceTrimLearner Settled(double pitch, double gammaDeg)
    {
        var learner = new VisualGuidanceTrimLearner(6.0, VisualGuidanceVerticalTuning.Default);
        for (int i = 0; i < 30 * 60; i++) learner.Update(pitch, VsFor(gammaDeg), Gs, Dt);
        return learner;
    }

    [Fact]
    public void First_sample_sets_the_trim()
    {
        var learner = new VisualGuidanceTrimLearner(6.0, VisualGuidanceVerticalTuning.Default);
        Assert.False(learner.HasData);
        Assert.Equal(6.0, learner.TrimDeg, 9);
        learner.Update(2.0, -742, 140, 0);
        Assert.True(learner.HasData);
        Assert.Equal(4.997, learner.TrimDeg, 0.01);
    }

    // The pilot pushes 1° (pitch 2 to 1); the flight path follows over 2.5 s, as the harness's jet flies.
    [Fact]
    public void A_push_barely_moves_it()
    {
        var learner = Settled(2.0, -3.0);
        Assert.Equal(5.0, learner.TrimDeg, 0.001);
        double gamma = -3.0, worst = 0;
        for (int i = 0; i < 30 * 10; i++)
        {
            gamma += ((1.0 - 5.0) - gamma) / 2.5 * Dt;
            learner.Update(1.0, VsFor(gamma), Gs, Dt);
            worst = Math.Max(worst, Math.Abs(learner.TrimDeg - 5.0));
        }
        Assert.True(worst <= 0.35, $"the trim moved {worst:0.000}°");
    }

    [Fact]
    public void It_follows_a_new_trim_in_six_seconds()
    {
        var learner = Settled(2.0, -3.0);
        for (int i = 0; i < 30 * 6; i++) learner.Update(3.0, VsFor(-3.0), Gs, Dt);
        Assert.Equal(0.37, 6.0 - learner.TrimDeg, 0.03);
    }

    // In the flare (stage 6) the flight path keeps being read, for the time to touchdown, while the level
    // attitude learned before it is held.
    [Fact]
    public void The_flight_path_keeps_up_while_the_trim_is_held()
    {
        var learner = Settled(2.0, -3.0);
        double trim = learner.TrimDeg;
        for (int i = 0; i < 30 * 5; i++) learner.UpdatePathAngle(VsFor(-1.0), Gs, Dt);
        Assert.Equal(-1.0, learner.PathAngleDeg, 0.02);
        Assert.Equal(trim, learner.TrimDeg, 9);
        // The split halves learn exactly what Update learns.
        var whole = Settled(2.0, -3.0);
        var split = Settled(2.0, -3.0);
        for (int i = 0; i < 30 * 3; i++)
        {
            whole.Update(3.0, VsFor(-2.0), Gs, Dt);
            split.UpdatePathAngle(VsFor(-2.0), Gs, Dt);
            split.UpdateTrim(3.0, Gs, Dt);
        }
        Assert.Equal(whole.TrimDeg, split.TrimDeg, 12);
        Assert.Equal(whole.PathAngleDeg, split.PathAngleDeg, 12);
    }

    [Fact]
    public void Slow_or_wild_samples_are_skipped()
    {
        var learner = Settled(2.0, -3.0);
        double before = learner.TrimDeg;
        for (int i = 0; i < 30 * 5; i++) learner.Update(8.0, VsFor(-3.0), 30, Dt);
        Assert.Equal(before, learner.TrimDeg, 9);
        for (int i = 0; i < 30 * 5; i++) learner.Update(30.0, 0, Gs, Dt);
        Assert.Equal(before, learner.TrimDeg, 9);
    }
}
