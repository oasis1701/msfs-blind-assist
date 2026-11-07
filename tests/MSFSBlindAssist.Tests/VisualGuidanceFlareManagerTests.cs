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

    // Still coming down at 742 fpm at 20 ft, where the target is 373 fpm: the cue asks for the target's
    // attitude plus the difference, as FlyByWire's flare does (gain 1), not the target's attitude alone.
    [Fact]
    public void Coming_down_faster_than_the_target_asks_for_more_nose_up()
    {
        var rig = OnFinal();
        Step(rig, 53);
        Assert.Equal("Flare", rig.Phase);
        rig.FlyFor(2, 30, _ => AtWheel(rig, 20));
        double trim = 2.0 - VisualGuidanceVertical.FlightPathAngleDeg(-742, Gs);
        double target = VisualGuidanceFlare.DescentDeg(VisualGuidanceFlare.TargetSinkFpm(20, VisualGuidanceFlare.TauS(53, 742, Touchdown), Touchdown), Gs);
        double actual = VisualGuidanceFlare.DescentDeg(742, Gs);
        Assert.Equal(target, rig.TargetDescent, 0.01);
        Assert.Equal(trim - target + (actual - target), rig.Pitch, 0.05);
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

    // A balloon to 30 ft, climbing 200 fpm: the cue asks gently back down, the level attitude less the
    // target's descent there (about 3.0°), never a push below it, however long the climb lasts (the stage 6
    // final review: the two-sided correction asked about 0.3°, 4.7° below level, near the ground).
    [Fact]
    public void A_balloon_asks_gently_back_down()
    {
        var rig = OnFinal();
        Step(rig, 53);
        rig.FlyFor(1, 30, _ => AtWheel(rig, 10));
        double trim = 2.0 - VisualGuidanceVertical.FlightPathAngleDeg(-742, Gs);
        double gentle = trim - VisualGuidanceFlare.DescentDeg(
            VisualGuidanceFlare.TargetSinkFpm(30, VisualGuidanceFlare.TauS(53, 742, Touchdown), Touchdown), Gs);
        double lowest = double.MaxValue;
        rig.FlyFor(3, 30, _ => AtWheel(rig, 30, +200), _ => lowest = Math.Min(lowest, rig.Pitch));
        Assert.Equal("Flare", rig.Phase);
        Assert.Equal(gentle, rig.Pitch, 0.1);
        Assert.True(lowest >= gentle - 0.1, $"the cue went down to {lowest:0.00}°, below the gentle {gentle:0.00}°");
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

    private const string PrepareLine = "Prepare to flare";

    private static int Count(VisualGuidanceTestRig rig, string line) => rig.Speech.Spoken.Count(s => s.Text == line);
    private static int Flares(VisualGuidanceTestRig rig) => Count(rig, "Flare");

    // Down from one wheel height to another at sinkFpm, one update at 30 Hz per step: the heights at which
    // the line ("Flare" unless given) was said, and with each the phase before that update.
    private static List<(double Ft, string PhaseBefore)> FlyDown(VisualGuidanceTestRig rig, double fromFt, double toFt,
        double sinkFpm, string line = "Flare")
    {
        var said = new List<(double, string)>();
        for (double h = fromFt; h >= toFt; h -= sinkFpm / 60.0 / 30)
        {
            int before = Count(rig, line);
            string phase = rig.Phase;
            Step(rig, h, -sinkFpm);
            if (Count(rig, line) > before) said.Add((h, phase));
        }
        return said;
    }

    // Settled at sinkFpm above the calls, then down to the runway: where "Prepare to flare" and "Flare" came.
    private static (List<(double Ft, string PhaseBefore)> Ready, List<(double Ft, string PhaseBefore)> Flare) Calls(double sinkFpm, double fromFt)
    {
        var rig = OnFinal(-sinkFpm);
        rig.FlyFor(5, 30, _ => AtWheel(rig, fromFt, -sinkFpm));   // the smoothed descent settles at sinkFpm
        var ready = new List<(double, string)>();
        var flare = new List<(double, string)>();
        for (double h = fromFt; h >= 6; h -= sinkFpm / 60.0 / 30)
        {
            int readyBefore = Count(rig, PrepareLine), flareBefore = Flares(rig);
            string phase = rig.Phase;
            Step(rig, h, -sinkFpm);
            if (Count(rig, PrepareLine) > readyBefore) ready.Add((h, phase));
            if (Flares(rig) > flareBefore) flare.Add((h, phase));
        }
        return (ready, flare);
    }

    // At 742 fpm the flare starts at 53 ft (4.3 s out). "Prepare to flare" comes 4 s before that, at 102.6 ft, and
    // "Flare" 1 s before it, at 65.5 ft, while still on final, so the pilot is pulling as the tone rises (the owner,
    // 2026-11-06: "Flare" used to come 2.5 s before touchdown, in the middle of the flare). Each once, queued.
    [Fact]
    public void Prepare_to_flare_then_flare_before_the_flare_starts()
    {
        var (ready, flare) = Calls(742, 130);
        var r = Assert.Single(ready);
        var f = Assert.Single(flare);
        Assert.InRange(r.Ft, 102.1, 102.7);
        Assert.InRange(f.Ft, 65.1, 65.6);
        Assert.Equal("Final", r.PhaseBefore);
        Assert.Equal("Final", f.PhaseBefore);
    }

    [Fact]
    public void The_calls_are_queued_not_cut_off()
    {
        var rig = OnFinal();
        rig.FlyFor(5, 30, _ => AtWheel(rig, 130, -742));
        FlyDown(rig, 130, 6, 742);
        Assert.False(rig.Speech.Spoken.Single(s => s.Text == PrepareLine).Interrupted);
        Assert.False(rig.Speech.Spoken.Single(s => s.Text == "Flare").Interrupted);
        Assert.True(rig.Speech.Spoken.FindIndex(s => s.Text == PrepareLine) < rig.Speech.Spoken.FindIndex(s => s.Text == "Flare"));
    }

    // Faster, both come higher: at 1,000 fpm "Prepare to flare" at 138 ft and "Flare" at 88 ft.
    [Fact]
    public void Coming_down_faster_the_calls_come_higher()
    {
        var (ready, flare) = Calls(1000, 170);
        Assert.InRange(Assert.Single(ready).Ft, 137.5, 138.4);
        Assert.InRange(Assert.Single(flare).Ft, 87.2, 88.4);   // 88.3 ft, within one 0.56 ft step at 30 Hz
    }

    // At 1,500 fpm the flare starts at the band's top (100 ft) and both calls come above it, each once: the
    // calls are armed again only by a climb above the band, so the way down never repeats them.
    [Fact]
    public void Very_fast_the_calls_above_the_band_are_said_once()
    {
        var (ready, flare) = Calls(1500, 250);
        Assert.InRange(Assert.Single(ready).Ft, 199, 200.1);
        Assert.InRange(Assert.Single(flare).Ft, 124, 125.1);
    }

    // At 300 fpm the flare starts at the aircraft's flare height (30 ft): "Flare" a second before it, at 35 ft,
    // and "Prepare to flare" at 50.
    [Fact]
    public void Slow_the_calls_come_before_the_flare_height()
    {
        var (ready, flare) = Calls(300, 90);
        Assert.InRange(Assert.Single(ready).Ft, 49.8, 50.1);
        var f = Assert.Single(flare);
        Assert.InRange(f.Ft, 34.8, 35.1);
        Assert.Equal("Final", f.PhaseBefore);
    }

    [Fact]
    public void A_balloon_is_not_said_again()
    {
        var rig = OnFinal();
        rig.FlyFor(5, 30, _ => AtWheel(rig, 130, -742));
        Assert.Single(FlyDown(rig, 130, 25, 742));
        rig.FlyFor(1, 30, _ => AtWheel(rig, 70, +300));
        Assert.Equal("Flare", rig.Phase);
        Assert.Empty(FlyDown(rig, 70, 6, 742));
        Assert.Equal(1, Flares(rig));
        Assert.Equal(1, Count(rig, PrepareLine));
    }

    // A climb above 100 ft (a go-around) arms both calls again for the next approach.
    [Fact]
    public void The_calls_are_said_again_after_a_go_around()
    {
        var rig = OnFinal();
        rig.FlyFor(5, 30, _ => AtWheel(rig, 130, -742));
        Assert.Single(FlyDown(rig, 130, 25, 742));
        rig.FlyFor(3, 30, _ => AtWheel(rig, 150, +1000));
        Assert.Equal("Final", rig.Phase);
        rig.FlyFor(5, 30, _ => AtWheel(rig, 150, -742));   // the smoothed descent settles again
        Assert.Single(FlyDown(rig, 150, 6, 742));
        Assert.Equal(2, Flares(rig));
        Assert.Equal(2, Count(rig, PrepareLine));
    }

    // F on final repeats the call once said (VG-22): "Prepare to flare." until "Flare" comes.
    [Fact]
    public void F_on_final_repeats_prepare_to_flare()
    {
        var rig = OnFinal();
        rig.FlyFor(5, 30, _ => AtWheel(rig, 130, -742));
        Assert.DoesNotContain("flare", rig.Readout, StringComparison.OrdinalIgnoreCase);
        FlyDown(rig, 130, 90, 742);
        Assert.StartsWith("On final", rig.Readout);
        Assert.EndsWith(" Prepare to flare.", rig.Readout);
    }

    // F in the flare adds the target, to 50 fpm: τ from 53 ft at 742 fpm, at 20 ft 20 × 60 / 5.37 + 150 = 373.
    [Fact]
    public void F_in_the_flare_says_the_target()
    {
        var rig = OnFinal();
        Step(rig, 53);
        Step(rig, 20);
        Assert.Equal("Flare, 20 feet. On the centreline. Target 350 down.", rig.Readout);
    }

    // Pressed low, the level attitude is still learned from the first update (pitch 2° minus a −3.0° flight
    // path: 5.0°), not left at the profile's 6° guess for the whole flare (the stage 6 final review): the
    // cue starts where the aircraft is.
    [Fact]
    public void Pressed_low_learns_the_level_attitude_from_the_first_update()
    {
        var rig = new VisualGuidanceTestRig();
        AtWheel(rig, 40);
        rig.UpdateAt(0);
        Assert.Equal("Flare", rig.Phase);
        Assert.Equal(2.0, rig.Pitch, 0.05);
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
