using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Volanta;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The VirtualPilot hooks on visual guidance stay inert in the app (VP-1), and left and right are
/// pinned in both senses that got confused in the original work: right of final and parallel turns
/// LEFT toward the line; right of final on an intercept heading turns RIGHT at capture.
/// </summary>
public class VisualGuidanceHarnessHookTests
{
    // Runway "09": true 090, no variation. Runway "36": true 004, 9° E variation, magnetic 355 (wraps north).
    public static TheoryData<double, double> Runways => new() { { 90.0, 0.0 }, { 4.0, 9.0 } };

    [Fact]
    public void A_manager_is_not_headless_unless_the_harness_says_so()
        => Assert.False(new VisualGuidanceManager(new SpeechCapture()).HeadlessForHarness);

    [Fact]
    public void SimConnect_attitude_converts_the_way_MainForm_always_has()
    {
        Assert.Equal(-5.72958, VisualGuidanceManager.PitchDegreesFromSimConnect(0.1), 4);
        Assert.Equal(5.72958, VisualGuidanceManager.BankDegreesFromSimConnect(0.1), 4);
        Assert.Equal(5.72958, VisualGuidanceManager.AoaDegreesFromSimConnect(0.1), 4);
    }

    [Theory, MemberData(nameof(Runways))]
    public void Right_of_final_flying_parallel_banks_left_toward_the_line(double trueHdg, double magVar)
        => Assert.True(BankAfterFrames(trueHdg, magVar, crossNm: 1.0, headingRelDeg: 0) < -1.0);

    [Theory, MemberData(nameof(Runways))]
    public void Left_of_final_flying_parallel_banks_right_toward_the_line(double trueHdg, double magVar)
        => Assert.True(BankAfterFrames(trueHdg, magVar, crossNm: -1.0, headingRelDeg: 0) > 1.0);

    [Theory, MemberData(nameof(Runways))]
    public void Right_of_final_on_an_intercept_heading_banks_right_at_capture(double trueHdg, double magVar)
        => Assert.True(BankAfterFrames(trueHdg, magVar, crossNm: 0.2, headingRelDeg: -30) > 1.0);

    [Theory, MemberData(nameof(Runways))]
    public void Left_of_final_on_an_intercept_heading_banks_left_at_capture(double trueHdg, double magVar)
        => Assert.True(BankAfterFrames(trueHdg, magVar, crossNm: -0.2, headingRelDeg: 30) < -1.0);

    // Joining final on both runways, so the north wrap (runway 36, magnetic 355) is pinned: ATC's 30°
    // cut from 3 nm right, 12 nm out, crosses final 6.8 nm out and is held wings level; from 0.3 nm
    // right the turn onto final is due, and it is a RIGHT turn.
    [Theory, MemberData(nameof(Runways))]
    public void Right_of_final_on_an_ATC_heading_holds_it(double trueHdg, double magVar)
    {
        var vg = Fly(trueHdg, magVar, crossNm: 3.0, headingRelDeg: -30, frames: 5, alongNm: 12);
        Assert.Equal("Joining", vg.PhaseForHarness);
        Assert.InRange(vg.DesiredBankForHarness, -0.1, 0.1);
    }

    [Theory, MemberData(nameof(Runways))]
    public void Right_of_final_on_an_ATC_heading_turns_right_onto_final(double trueHdg, double magVar)
    {
        var vg = Fly(trueHdg, magVar, crossNm: 0.3, headingRelDeg: -30, frames: 5, alongNm: 12);
        Assert.Equal("Joining", vg.DecisionForHarness);
        Assert.True(vg.DesiredBankForHarness > 1.0, $"bank {vg.DesiredBankForHarness}");
    }

    [Fact]
    public void Headless_guidance_runs_with_no_tones()
    {
        var vg = Fly(90.0, 0.0, crossNm: 0.0, headingRelDeg: 0, frames: 3);
        Assert.NotEqual("NotStarted", vg.PhaseForHarness);
    }

    [Fact]
    public void Visual_guidance_reads_time_only_through_SimClock()
    {
        string path = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Services", "VisualGuidanceManager.cs");
        var code = File.ReadLines(path).Select(l => l.Split("//")[0]).ToList();
        foreach (string clock in new[] { "DateTime.UtcNow", "DateTime.Now", "DateTimeOffset.UtcNow", "DateTimeOffset.Now", "Environment.TickCount", "Stopwatch" })
            Assert.DoesNotContain(code, l => l.Contains(clock, StringComparison.Ordinal));
    }

    private static double BankAfterFrames(double trueHdg, double magVar, double crossNm, double headingRelDeg)
        => Fly(trueHdg, magVar, crossNm, headingRelDeg, frames: 5).DesiredBankForHarness;

    // alongNm (8 by default) back along the extended centreline, crossNm to the side (+ = right facing
    // the runway), on the 3° path, heading = course + headingRelDeg, wings level, 140 kt, fed in
    // MainForm's order. Real time: no SimClock.Override here (the harness sets one; these tests must
    // not), so the frames are microseconds apart and the cross-track rate term stays zero. These
    // assert sign only.
    private static VisualGuidanceManager Fly(double trueHdg, double magVar, double crossNm, double headingRelDeg, int frames,
        double alongNm = 8.0)
    {
        const double thrLat = 47.0, thrLon = 8.0, elev = 500.0;
        static double Norm(double d) => (d % 360 + 360) % 360;
        var (endLat, endLon) = VolantaTrafficCalculator.Project(thrLat, thrLon, trueHdg, 2.0);
        var runway = new Runway
        {
            AirportICAO = "ZZVG", RunwayID = "TEST", Heading = trueHdg, HeadingMag = Norm(trueHdg - magVar),
            StartLat = thrLat, StartLon = thrLon, EndLat = endLat, EndLon = endLon,
            ThresholdElevation = elev, GlideslopeAngleDeg = 3.0,
        };
        var airport = new Airport { ICAO = "ZZVG", Name = "Test", Latitude = thrLat, Longitude = thrLon, Altitude = elev, MagVar = magVar };
        var vg = new VisualGuidanceManager(new SpeechCapture()) { HeadlessForHarness = true };
        vg.Toggle();
        vg.Initialize(runway, airport, HandFlyWaveType.Triangle, 0.0, HandFlyWaveType.Sine, 0.0, false, new VisualGuidanceProfile());

        var back = VolantaTrafficCalculator.Project(thrLat, thrLon, Norm(trueHdg + 180), alongNm);
        var pos = crossNm >= 0
            ? VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(trueHdg + 90), crossNm)
            : VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(trueHdg - 90), -crossNm);
        double headingMag = Norm(trueHdg + headingRelDeg - magVar);
        double alt = elev + alongNm * 6076.12 * Math.Tan(3.0 * Math.PI / 180) + 60.0;

        for (int i = 0; i < frames; i++)
        {
            vg.UpdateLatitude(pos.Latitude); vg.UpdateLongitude(pos.Longitude); vg.UpdateAltitudeMSL(alt);
            vg.UpdateHeading(headingMag); vg.UpdateGroundSpeed(140); vg.UpdateVerticalSpeed(-700);
            vg.UpdateGroundTrack(headingMag);
            vg.UpdatePitch(VisualGuidanceManager.PitchDegreesFromSimConnect(-2.0 * Math.PI / 180));
            vg.UpdateBank(VisualGuidanceManager.BankDegreesFromSimConnect(0.0));
            vg.UpdateAoA(VisualGuidanceManager.AoaDegreesFromSimConnect(5.0 * Math.PI / 180));
            vg.UpdateAGL(alt - elev);
            vg.ProcessUpdate();
        }
        return vg;
    }
}
