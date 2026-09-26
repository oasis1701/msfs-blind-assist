// The KMEM 36L landing of 2026-09-26 replayed through the new rules — the pilot-level benchmark for the
// landing-exit fixes. Values at the decision points are the ones landing_exit.log recorded; the exit
// track (01:23:30.3-01:23:50.6) is taxi_guidance.log's per-frame record.

using System.Text.Json;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class KmemLandingReplayTests
{
    private static readonly List<LandingExit> Exits =
        KmemRunway36LFixture.BuildGraph().GetLandingExits(KmemRunway36LFixture.Runway36L());

    private static LandingExit Exit(string name) => Exits.Single(e => e.TaxiwayName == name);

    private static double M7Window()
    {
        var m7 = Exit("M7");
        var axis = RunwayAxis.For(KmemRunway36LFixture.Runway36L());
        double lateral = axis.Project(m7.Latitude, m7.Longitude).LateralMetres;
        return RolloutExitGate.TurnWindowFeetFor(164.0, lateral, m7.ExitAngleDegrees);
    }

    private static double M7RelativeBearing()
        => RolloutExitGate.ExitRelativeBearingDeg(Exit("M7").ExitBearingTrue, KmemRunway36LFixture.RunwayHeadingTrue);

    [Fact]
    public void At_the_M6_turn_point_the_pilot_is_told_to_continue_not_to_turn()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.True(RolloutExitGate.IsTooFastToTurn(49.0, Exit("M6").ExitAngleDegrees));
        Assert.Equal("Too fast for taxiway M6. Continue to taxiway M7, 900 feet.",
            RetargetCallout.Compose(RetargetReason.TooFast, "M6", "M7", 880, straighten: false, slowDown: false));
    }

    [Fact]
    public void At_631_ft_from_M7_with_a_leftover_right_turn_the_tone_steers_left()
    {
        double window = M7Window();
        Assert.InRange(window, 300.0, 350.0);
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(48.1, 631.0, 8.4, M7RelativeBearing(), window));
    }

    [Fact]
    public void The_retarget_sentence_says_straighten_and_retires_the_stale_callouts()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        double window = M7Window();
        bool straighten = RolloutExitGate.ShouldStraightenAfterRetarget(8.4, M7RelativeBearing(), 631.0, false, window);
        Assert.True(straighten);
        var r = RetargetCallout.Retire(RetargetReason.Missed, straighten, 631, 48.1, Exit("M7").ExitType,
            1500, 900, 500, 150, RolloutExitGate.SlowDownAboveKts(Exit("M7").ExitAngleDegrees, Exit("M7").ExitType));
        Assert.True(r.Retire900);
        Assert.True(r.Retire500);
        Assert.False(r.RetireTurnNow);
        Assert.Equal("Missed taxiway M6. Straighten. Retargeting taxiway M7, 650 feet ahead.",
            RetargetCallout.Compose(RetargetReason.Missed, "M6", "M7", 631, straighten, r.SlowDown));
    }

    [Fact]
    public void There_is_no_handoff_483_ft_before_M7()
        => Assert.False(RolloutExitGate.IsExitTurnBegun(15.0, 47.6, 483.0, false, M7RelativeBearing(), M7Window()));

    private sealed record Frame(double t, double lat, double lon, double hdgTrue, double gs);
    private sealed record Track(string source, List<Frame> frames);

    [Fact]
    public void Off_pavement_is_said_twice_during_the_excursion_and_not_after_it()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kmem-36l-exit-2026-09-26.json");
        var track = JsonSerializer.Deserialize<Track>(File.ReadAllText(path))!;
        var map = PavementMap.Build(KmemRunway36LFixture.BuildGraph());
        // Production also counts the runway being landed on as pavement (_rolloutRunway within
        // half-width + 10 m, along from -10 m to length + 10 m) — a private TaxiGuidanceManager
        // helper this replay cannot call. That shifts these alerts by about one frame (to roughly
        // 34.19 s and 40.32 s); the windows below already allow for it.
        var alert = new OffPavementAlert();
        var t0 = new DateTime(2026, 9, 26, 1, 23, 0, DateTimeKind.Utc);
        var spokenAt = new List<double>();
        foreach (var f in track.frames)
            if (alert.Update(!map.IsOnMappedPavement(f.lat, f.lon), f.gs, t0.AddSeconds(f.t)))
                spokenAt.Add(f.t);

        Assert.Equal(2, spokenAt.Count);
        Assert.InRange(spokenAt[0], 33.6, 34.6);
        Assert.InRange(spokenAt[1], 39.6, 40.6);
    }

    [Fact]
    public void On_the_fast_exit_only_warnings_speak_and_everything_is_back_after_it()
    {
        // 01:23:32 and 01:23:37: the two "Slow down" cautions are held back on the exit ...
        Assert.True(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Taxiing, 47.4, true));
        Assert.False(TrafficSpeechPolicy.SpeaksOnFastLandingExit(TrafficCalloutKind.Caution));
        // ... 01:23:40: the "Stop" for the A380 near the route ahead still interrupts, by design ...
        Assert.True(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Taxiing, 44.1, true));
        Assert.True(TrafficSpeechPolicy.SpeaksOnFastLandingExit(TrafficCalloutKind.Warning));
        // ... and 01:23:53, exit guidance over, everything speaks again.
        Assert.False(GroundTrafficSuppression.LandingExitWarningsOnly(TaxiGuidanceState.Arrived, 35.0, true));
    }
}
