using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The flare through the real manager (visual guidance stage 6; <see cref="VisualGuidanceTestRig"/>, runway
/// 09, 140 kt, the A320 default profile: the flare height 30 ft is the backstop, the flare bias 12 ft, so the
/// wheels are the datum less 12 ft). Pressed on final 6 nm out at 742 fpm with 2° of pitch, a 3.0° flight
/// path, so the learned level attitude is 5.0°; then the aircraft is placed over the threshold at the wheel
/// heights a test gives. The cue is the level attitude minus the descent angle of a sink rate that starts at
/// the aircraft's own and eases to 150 fpm at touchdown (VG-27).
/// </summary>
public class VisualGuidanceFlareManagerTests
{
    private const double Bias = 12.0, PathBiasFt = 60.0, Gs = 140.0, Touchdown = 150.0;

    private static VisualGuidanceTestRig OnFinal(double vsFpm = -742)
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(6.0, 0, 0, vsFpm: vsFpm);
        rig.UpdateAt(0);
        rig.FlyFor(10, 30, _ => rig.Place(6.0, 0, 0, vsFpm: vsFpm));
        Assert.Equal("Final", rig.Phase);
        return rig;
    }

    // Over the threshold with the wheels wheelFt up: the datum wheelFt + 12 ft above it, where the path is 60.
    private static void AtWheel(VisualGuidanceTestRig rig, double wheelFt, double vsFpm = -742)
        => rig.Place(0.0, 0, 0, vsFpm: vsFpm, altOffsetFt: wheelFt + Bias - PathBiasFt);

    // One update at hz with the wheels wheelFt up.
    private static void Step(VisualGuidanceTestRig rig, double wheelFt, double vsFpm = -742, int hz = 30)
        => rig.FlyFor(1.0 / hz, hz, _ => AtWheel(rig, wheelFt, vsFpm));

    // At 742 fpm touchdown is 4.37 s away at 54 ft and 4.29 s at 53.
    [Fact]
    public void The_flare_starts_four_seconds_before_touchdown()
    {
        var rig = OnFinal();
        Step(rig, 54);
        Assert.Equal("Final", rig.Phase);
        Step(rig, 53);
        Assert.Equal("Flare", rig.Phase);
    }

    // At 300 fpm 4.3 s is 21.5 ft: the aircraft's own flare height (30 ft) comes first.
    [Fact]
    public void A_slow_descent_flares_at_the_aircrafts_height()
    {
        var rig = OnFinal();
        rig.FlyFor(5, 30, _ => AtWheel(rig, 90, -300));   // the smoothed descent rate settles at 300 fpm
        Step(rig, 31, -300);
        Assert.Equal("Final", rig.Phase);
        Step(rig, 30, -300);
        Assert.Equal("Flare", rig.Phase);
    }

    [Fact]
    public void The_cue_rises_as_the_wheels_come_down()
    {
        var rig = OnFinal();
        Step(rig, 53);
        Assert.Equal("Flare", rig.Phase);
        double tau = VisualGuidanceFlare.TauS(53, 742, Touchdown);
        double previous = rig.TargetDescent;
        for (double wheel = 52; wheel >= 6; wheel -= 1)
        {
            Step(rig, wheel);
            Assert.Equal("Flare", rig.Phase);
            Assert.True(rig.TargetDescent < previous, $"at {wheel} ft the target went from {previous:0.000}° to {rig.TargetDescent:0.000}°");
            Assert.True(rig.TargetDescent > 0.5, $"at {wheel} ft the target is {rig.TargetDescent:0.000}°");
            previous = rig.TargetDescent;
        }
        double expected = VisualGuidanceFlare.DescentDeg(VisualGuidanceFlare.TargetSinkFpm(6, tau, Touchdown), Gs);
        Assert.Equal(expected, rig.TargetDescent, 0.1);
    }

    // The profile starts at the aircraft's own descent, so the cue does not jump at the flare's start.
    [Fact]
    public void It_starts_where_the_aircraft_is()
    {
        var rig = OnFinal();
        Step(rig, 54);
        double lastFinal = rig.TargetDescent;
        Step(rig, 53);
        Assert.Equal("Flare", rig.Phase);
        Assert.Equal(lastFinal, rig.TargetDescent, 0.05);
        Assert.Equal(3.0, rig.TargetDescent, 0.05);
    }

    // Coming down at 900 fpm, steeper than the path: the flare starts from 900, not from the path's 3°.
    [Fact]
    public void It_starts_from_the_aircrafts_own_descent_rate()
    {
        var rig = OnFinal();
        rig.FlyFor(5, 30, _ => AtWheel(rig, 99, -900));   // inside the flare's 100 ft band, but 6.6 s out
        Assert.Equal("Final", rig.Phase);
        Step(rig, 64, -900);   // 4.27 s out
        Assert.Equal("Flare", rig.Phase);
        Assert.Equal(VisualGuidanceFlare.DescentDeg(900, Gs), rig.TargetDescent, 0.05);
    }

    // A balloon stays in the flare, asking gently back down; only above 100 ft is it final again, and the
    // path's cue then leaves from the flare's pitch at the profile's 2.5°/s, not from where it was.
    [Fact]
    public void A_balloon_stays_in_the_flare()
    {
        var rig = OnFinal();
        Step(rig, 53);
        rig.FlyFor(1, 30, _ => AtWheel(rig, 10));        // the cue rises toward 5.0 − 1.1
        Assert.Equal("Flare", rig.Phase);
        double flarePitch = rig.Pitch;
        Assert.True(flarePitch > 3.5, $"the flare's cue is {flarePitch:0.00}°");
        Step(rig, 70, +300);
        Assert.Equal("Flare", rig.Phase);
        Assert.True(rig.TargetDescent > 0, $"the balloon's target is {rig.TargetDescent:0.000}°");
        double balloonPitch = rig.Pitch;
        Step(rig, 101, +300);
        Assert.Equal("Final", rig.Phase);
        Assert.True(Math.Abs(rig.Pitch - balloonPitch) <= 2.5 / 30 + 1e-6,
            $"the cue went from {balloonPitch:0.00}° to {rig.Pitch:0.00}° in one frame");
    }

    // Ctrl+V pressed at 40 ft coming down at 742 fpm (3.2 s out): the flare from the first update, from the
    // aircraft's own descent, a finite cue.
    [Fact]
    public void Pressed_low_starts_the_flare_from_its_own_descent()
    {
        var rig = new VisualGuidanceTestRig();
        AtWheel(rig, 40);
        rig.UpdateAt(0);
        Assert.Equal("Flare", rig.Phase);
        Assert.True(double.IsFinite(rig.Pitch));
        Assert.True(double.IsFinite(rig.TargetDescent));
        rig.FlyFor(1, 30, _ => AtWheel(rig, 40));
        Assert.Equal(3.0, rig.TargetDescent, 0.3);
    }

    // The aircraft placed down the profile for 3 s: the cue reads the same at 10, 30 and 60 updates a second.
    [Fact]
    public void The_flare_cue_is_the_same_at_every_update_rate()
    {
        double At(int hz)
        {
            var rig = OnFinal();
            Step(rig, 53, -742, hz);
            Assert.Equal("Flare", rig.Phase);
            double tau = VisualGuidanceFlare.TauS(53, 742, Touchdown);
            double start = rig.Now;
            rig.FlyFor(3, hz, t =>
            {
                // h(t) of the profile: dh/dt = −h/τ − 2.5 ft/s.
                double h = (53 + 2.5 * tau) * Math.Exp(-(t - start) / tau) - 2.5 * tau;
                AtWheel(rig, h, -(h * 60 / tau + Touchdown));
            });
            return rig.Pitch;
        }
        double p10 = At(10), p30 = At(30), p60 = At(60);
        Assert.True(Math.Max(p10, Math.Max(p30, p60)) - Math.Min(p10, Math.Min(p30, p60)) <= 0.02,
            $"10 Hz {p10:0.000}°, 30 Hz {p30:0.000}°, 60 Hz {p60:0.000}°");
    }
}
