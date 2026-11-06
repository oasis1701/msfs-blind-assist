using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Volanta;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The geometry, the decision and the capture law for joining final (<see cref="VisualGuidanceJoin"/>),
/// at FlyByWire's gain (10259 deg·kt/nm) and 140 kt unless a test says otherwise. Positions are in
/// the approach frame: along nm back from the threshold, cross nm right of the extended centreline
/// facing the runway; relative tracks are degrees from the final course, + to the right.
/// </summary>
public class VisualGuidanceJoinTests
{
    private const double Gain = VisualGuidanceJoin.DefaultCaptureGain;
    private static readonly VisualGuidanceProfile Profile = new();

    // Runway "09": true 090, threshold 47 N 8 E. Runway "36": true 004 (magnetic 355 with 9° E), as
    // VisualGuidanceHarnessHookTests builds it.
    public static TheoryData<double> Courses => new() { 90.0, 4.0 };

    private static Runway MakeRunway(double trueCourse)
    {
        var (endLat, endLon) = VolantaTrafficCalculator.Project(47.0, 8.0, trueCourse, 2.0);
        return new Runway
        {
            AirportICAO = "ZZVG", RunwayID = "T", Heading = trueCourse, HeadingMag = trueCourse,
            StartLat = 47.0, StartLon = 8.0, EndLat = endLat, EndLon = endLon, ThresholdElevation = 500, GlideslopeAngleDeg = 3.0,
        };
    }

    private static (double Lat, double Lon) PointAt(double trueCourse, double alongNm, double crossNm)
    {
        static double Norm(double d) => (d % 360 + 360) % 360;
        var back = VolantaTrafficCalculator.Project(47.0, 8.0, Norm(trueCourse + 180), alongNm);
        var p = crossNm >= 0
            ? VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(trueCourse + 90), crossNm)
            : VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(trueCourse - 90), -crossNm);
        return (p.Latitude, p.Longitude);
    }

    private static VisualGuidanceApproachPoint P(double along, double cross) => new(along, cross);

    [Theory, MemberData(nameof(Courses))]
    public void Locate_matches_the_lateral_laws_cross_track(double trueCourse)
    {
        var runway = MakeRunway(trueCourse);
        foreach (double cross in new[] { 0.5, -0.5 })
        {
            var (lat, lon) = PointAt(trueCourse, 8.0, cross);
            var p = VisualGuidanceJoin.Locate(lat, lon, runway);
            // ±0.002, not 0.001: the test projects the 0.5 nm offset at the threshold's course, and 8 nm
            // west at 47° N the great circle has turned about 0.14°, which moves about 0.0012 nm of the
            // offset into along-track on runway 09 (runway 36 runs nearly north-south and shows none).
            Assert.Equal(8.000, p.AlongNm, 0.002);
            Assert.Equal(cross, p.CrossNm, 0.001);
            // The same reference the lateral law steers to.
            double lawCross = Math.Sign(NavigationCalculator.CalculateCrossTrackError(lat, lon, runway.StartLat, runway.StartLon, runway.Heading))
                * NavigationCalculator.CalculateDistanceToLocalizer(lat, lon, runway.StartLat, runway.StartLon, runway.Heading);
            Assert.Equal(lawCross, p.CrossNm, 0.001);
        }
    }

    [Fact]
    public void Relative_track_wraps_through_north()
    {
        Assert.Equal(15.0, VisualGuidanceJoin.Normalize(10 - 355), 9);
        Assert.Equal(-15.0, VisualGuidanceJoin.Normalize(350 - 5), 9);
        Assert.Equal(180.0, VisualGuidanceJoin.Normalize(180), 9);
        Assert.Equal(180.0, VisualGuidanceJoin.Normalize(-180), 9);
    }

    // Gain 4200 deg·kt/nm at 140 kt is today's arc capture, 30° per nm.
    [Fact]
    public void Capture_law_with_todays_gain_is_todays_arc()
    {
        Assert.Equal(-15.0, VisualGuidanceJoin.DesiredLegTrackDeg(0.5, 0, 140, 45, 4200), 6);
        Assert.Equal(15.0, VisualGuidanceJoin.DesiredLegTrackDeg(-0.5, 0, 140, 45, 4200), 6);
        Assert.Equal(-45.0, VisualGuidanceJoin.DesiredLegTrackDeg(2.0, 0, 140, 45, 4200), 6);
        Assert.Equal(-14.88, VisualGuidanceJoin.DesiredLegTrackDeg(0.5, -0.01, 140, 45, 4200), 6);
    }

    [Fact]
    public void Capture_gain_scales_with_ground_speed()
    {
        Assert.Equal(73.28, VisualGuidanceJoin.CaptureGainDegPerNm(140, Gain), 0.01);
        Assert.Equal(157.83, VisualGuidanceJoin.CaptureGainDegPerNm(65, Gain), 0.01);
    }

    [Fact]
    public void A_stopped_aircraft_gets_no_countdown_and_a_finite_gain()
    {
        Assert.Equal(256.475, VisualGuidanceJoin.CaptureGainDegPerNm(0, Gain), 3);
        Assert.Null(VisualGuidanceJoin.SecondsToTurn(2, 30, 30, 0, Gain, 1.0));
        Assert.True(double.IsFinite(VisualGuidanceJoin.DesiredLegTrackDeg(2, 0, 0, 30, Gain)));
        Assert.True(double.IsFinite(VisualGuidanceJoin.TurnCueCrossNm(30, 0, Gain, 1.0)));
    }

    // FlyByWire engages LOC* where its capture law first asks 5° of bank: on a 30° intercept at 140 kt, about 0.34 nm out.
    [Fact]
    public void The_turn_cue_comes_where_FlyByWire_engages()
        => Assert.Equal(0.341, VisualGuidanceJoin.TurnCueCrossNm(30, 140, Gain, 1.0), 0.001);

    [Fact]
    public void Seconds_to_turn_on_a_30_degree_intercept()
    {
        Assert.Equal(85.3, VisualGuidanceJoin.SecondsToTurn(2.0, 30, 30, 140, Gain, 1.0)!.Value, 0.1);
        Assert.Null(VisualGuidanceJoin.SecondsToTurn(2.0, 3, 30, 140, Gain, 1.0));
    }

    [Fact]
    public void Along_to_establish()
    {
        Assert.Equal(1.610, VisualGuidanceJoin.AlongToEstablishNm(3, 90, 140, Gain), 0.005);
        Assert.Equal(0.536, VisualGuidanceJoin.AlongToEstablishNm(0.2, 30, 140, Gain), 0.005);
        Assert.Equal(3.823, VisualGuidanceJoin.AlongToEstablishNm(2, 30, 140, Gain), 0.005);
        Assert.Equal(0.0, VisualGuidanceJoin.AlongToEstablishNm(0.05, 30, 140, Gain), 9);
    }

    [Fact]
    public void Turn_radius()
        => Assert.Equal(0.6125, VisualGuidanceJoin.TurnRadiusNm(140, 25), 0.0005);

    [Fact]
    public void Crosses_final()
    {
        Assert.True(VisualGuidanceJoin.CrossesFinal(P(12, 3), -30, 3, out double at));
        Assert.Equal(6.804, at, 0.01);
        Assert.True(VisualGuidanceJoin.CrossesFinal(P(12, -3), 30, 3, out _));
        Assert.False(VisualGuidanceJoin.CrossesFinal(P(12, 3), 30, 3, out _));      // diverging
        Assert.False(VisualGuidanceJoin.CrossesFinal(P(12, 3), 0, 3, out _));       // parallel
        Assert.False(VisualGuidanceJoin.CrossesFinal(P(4, 2), -45, 3, out _));      // crosses at 2.0, inside the minimum
        Assert.False(VisualGuidanceJoin.CrossesFinal(P(1, 3), -30, 3, out _));      // crosses behind the threshold
        Assert.True(VisualGuidanceJoin.CrossesFinal(P(12, 3), -100, 3, out _));
        Assert.False(VisualGuidanceJoin.CrossesFinal(P(12, 3), -101, 3, out _));
    }

    [Fact]
    public void Leg_cross_track_signs()
    {
        // Right-side downwind, flown outbound: toward the runway is right of it.
        var downwind = new VisualGuidanceLeg(VisualGuidanceLegKind.Downwind, 0, 3, 180, 1);
        Assert.Equal(1.0, VisualGuidanceJoin.LegCrossNm(downwind, P(2, 2)), 9);
        // Right-side base, flown toward the centreline: toward the threshold is right of it.
        var rightBase = new VisualGuidanceLeg(VisualGuidanceLegKind.Base, 7.61, 0, -90, 0);
        Assert.Equal(0.61, VisualGuidanceJoin.LegCrossNm(rightBase, P(7.0, 1.5)), 9);
        var final = new VisualGuidanceLeg(VisualGuidanceLegKind.Final, 0, 0, 0, 0);
        Assert.Equal(-0.7, VisualGuidanceJoin.LegCrossNm(final, P(9, -0.7)), 9);
    }

    [Fact]
    public void Intercept_is_positive_when_converging()
    {
        Assert.Equal(30.0, VisualGuidanceJoin.InterceptDeg(3, -30), 9);
        Assert.Equal(30.0, VisualGuidanceJoin.InterceptDeg(-3, 30), 9);
        Assert.Equal(-30.0, VisualGuidanceJoin.InterceptDeg(3, 30), 9);
        Assert.Equal(0.0, VisualGuidanceJoin.InterceptDeg(0, 30), 9);
    }

    [Fact]
    public void Decide_on_final()
    {
        Assert.Equal(VisualGuidanceJoinState.OnFinal, VisualGuidanceJoin.Decide(P(8, 0.05), 2, 140, Gain, Profile).State);
        Assert.Equal(30.0, VisualGuidanceJoin.Decide(P(8, 0.05), 2, 140, Gain, Profile).ClampDeg);
        Assert.NotEqual(VisualGuidanceJoinState.OnFinal, VisualGuidanceJoin.Decide(P(8, 0.15), 0, 140, Gain, Profile).State);
    }

    [Fact]
    public void Decide_joining()
    {
        var plan = VisualGuidanceJoin.Decide(P(12, 3), -30, 140, Gain, Profile);
        Assert.Equal(VisualGuidanceJoinState.Joining, plan.State);
        Assert.True(double.IsNaN(plan.ClampDeg));
        Assert.Equal(VisualGuidanceLegKind.Final, Assert.Single(plan.Legs).Kind);
    }

    [Fact]
    public void Decide_vectoring_straight_in()
    {
        foreach (double rel in new[] { 0.0, 180.0 })
        {
            var plan = VisualGuidanceJoin.Decide(P(10, 2), rel, 140, Gain, Profile);
            Assert.Equal(VisualGuidanceJoinState.Vectoring, plan.State);
            Assert.Equal(VisualGuidanceLegKind.Final, Assert.Single(plan.Legs).Kind);
        }
        // Heading away, the turn back toward final closes most of the 2 nm by itself: the shallowest cut does.
        Assert.Equal(30.0, VisualGuidanceJoin.Decide(P(10, 2), 180, 140, Gain, Profile).ClampDeg);
    }

    // The angle BA picks is the shallowest that, counting the turn onto it, lines the aircraft up at
    // least the minimum final out (the owner's choice, 2026-11-04): from 2 nm beside a 10 nm final,
    // flying parallel, 30° would line it up short of 6 nm once the turn is counted.
    [Theory]
    [InlineData(10, 2, 0)]
    [InlineData(8, 1, 0)]
    [InlineData(10, -2, 0)]
    public void Decide_vectoring_picks_the_shallowest_angle_that_leaves_the_minimum_final_counting_the_turn(double along, double cross, double rel)
    {
        var plan = VisualGuidanceJoin.Decide(P(along, cross), rel, 140, Gain, Profile);
        Assert.Equal(VisualGuidanceJoinState.Vectoring, plan.State);
        double cut = plan.ClampDeg;
        Assert.True(cut > 30, $"cut {cut}");
        Assert.True(VisualGuidanceJoin.JoinAlongNm(P(along, cross), rel, cut, 140, Gain, Profile) >= Profile.MinimumFinalNm);
        Assert.True(VisualGuidanceJoin.JoinAlongNm(P(along, cross), rel, cut - 1, 140, Gain, Profile) < Profile.MinimumFinalNm);
    }

    // Already on the cut there is no turn: the join is the plan's straight-cut formula.
    [Fact]
    public void Join_on_the_cut_already_is_the_straight_cut_formula()
        => Assert.Equal(12 - VisualGuidanceJoin.AlongToEstablishNm(3, 30, 140, Gain),
            VisualGuidanceJoin.JoinAlongNm(P(12, 3), -30, 30, 140, Gain, Profile), 9);

    // The turn from parallel onto a 30° cut, rolling in at 3°/s to at most 25°: at 140 kt about 17 s,
    // 0.6 nm along final and 0.15 nm toward it; at 65 kt about 12 s and 0.2 nm. Right of final it moves
    // left (toward the line), left of final right.
    [Fact]
    public void The_turn_onto_the_cut_depends_on_speed_and_bank()
    {
        var jet = VisualGuidanceJoin.TurnOntoCut(0, -30, 140, 25, 3);
        Assert.InRange(jet.InboundNm, 0.55, 0.70);
        Assert.InRange(jet.RightNm, -0.20, -0.12);
        var light = VisualGuidanceJoin.TurnOntoCut(0, -30, 65, 25, 3);
        Assert.InRange(light.InboundNm, 0.15, 0.25);
        Assert.True(light.RightNm < 0);
        var mirror = VisualGuidanceJoin.TurnOntoCut(0, 30, 140, 25, 3);
        Assert.Equal(jet.InboundNm, mirror.InboundNm, 9);
        Assert.Equal(-jet.RightNm, mirror.RightNm, 9);
        // A slower roll rate (the A330's 2.5°/s) takes longer, so it covers more ground.
        Assert.True(VisualGuidanceJoin.TurnOntoCut(0, -30, 140, 25, 2.5).InboundNm > jet.InboundNm);
        Assert.Equal((0.0, 0.0), VisualGuidanceJoin.TurnOntoCut(-30, -30, 140, 25, 3));
    }

    [Fact]
    public void The_turn_onto_the_cut_is_finite_for_a_stopped_aircraft()
    {
        var stopped = VisualGuidanceJoin.TurnOntoCut(0, -30, 0, 25, 3);
        Assert.True(double.IsFinite(stopped.InboundNm) && double.IsFinite(stopped.RightNm));
    }

    // Decision 4: inside the minimum final, close to the line and heading in, BA vectors straight onto final.
    [Fact]
    public void Decide_vectoring_close_to_the_line_on_short_final()
    {
        var plan = VisualGuidanceJoin.Decide(P(1.8, 0.15), 0, 140, Gain, new VisualGuidanceProfile { MinimumFinalNm = 3 });
        Assert.Equal(VisualGuidanceJoinState.Vectoring, plan.State);
        Assert.Equal(30.0, plan.ClampDeg);
    }

    [Fact]
    public void Decide_pattern_from_the_far_side()
    {
        var right = VisualGuidanceJoin.Decide(P(-1, 3), 180, 140, Gain, Profile);
        Assert.Equal(VisualGuidanceJoinState.Pattern, right.State);
        Assert.Equal(90.0, right.ClampDeg);
        Assert.Equal(3, right.Legs.Count);
        var (dw, bs, fin) = (right.Legs[0], right.Legs[1], right.Legs[2]);
        Assert.Equal(VisualGuidanceLegKind.Downwind, dw.Kind);
        Assert.Equal(0.0, dw.AlongNm, 9);
        Assert.Equal(3.0, dw.CrossNm, 9);
        Assert.Equal(180.0, dw.DirectionDeg, 9);
        Assert.Equal(1, dw.ReversedTurnSign);
        Assert.Equal(VisualGuidanceLegKind.Base, bs.Kind);
        Assert.Equal(7.61, bs.AlongNm, 0.01);
        Assert.Equal(0.0, bs.CrossNm, 9);
        Assert.Equal(-90.0, bs.DirectionDeg, 9);
        Assert.Equal(0, bs.ReversedTurnSign);
        Assert.Equal(new VisualGuidanceLeg(VisualGuidanceLegKind.Final, 0, 0, 0, 0), fin);

        var left = VisualGuidanceJoin.Decide(P(-1, -3), 180, 140, Gain, Profile);
        Assert.Equal(VisualGuidanceJoinState.Pattern, left.State);
        Assert.Equal(-3.0, left.Legs[0].CrossNm, 9);
        Assert.Equal(-1, left.Legs[0].ReversedTurnSign);
        Assert.Equal(90.0, left.Legs[1].DirectionDeg, 9);
    }

    // A tight downwind is widened until the aircraft can finish its turn onto the base before the turn
    // onto final starts (the owner's choice, 2026-11-05, after the far-side jets' base and final turns ran
    // into one U-turn from 1.225 nm and swung 640 ft past the centreline): the sideways room of its own
    // base turn at its speed and bank, plus the distance from final where the turn onto final comes.
    // About 1.92 nm at 140 kt and 2.7 at 180; a light aircraft at 65 kt keeps the 1 nm least downwind.
    [Fact]
    public void Decide_pattern_widens_a_tight_downwind()
    {
        double Width(double gs) => Math.Abs(VisualGuidanceJoin.TurnOntoCut(180, -90, gs, 25, 3).RightNm)
            + VisualGuidanceJoin.TurnCueCrossNm(90, gs, Gain, 1.0);
        double jet = VisualGuidanceJoin.Decide(P(-1, 0.3), 180, 140, Gain, Profile).Legs[0].CrossNm;
        Assert.Equal(Width(140), jet, 9);
        Assert.InRange(jet, 1.91, 1.93);
        double fast = VisualGuidanceJoin.Decide(P(-1, 0.3), 180, 180, Gain, Profile).Legs[0].CrossNm;
        Assert.Equal(Width(180), fast, 9);
        Assert.InRange(fast, 2.6, 2.8);
        Assert.Equal(-Width(140), VisualGuidanceJoin.Decide(P(-1, -0.3), 180, 140, Gain, Profile).Legs[0].CrossNm, 9);
        Assert.Equal(VisualGuidanceJoin.MinDownwindNm, VisualGuidanceJoin.Decide(P(-1, 0.3), 180, 65, Gain, Profile).Legs[0].CrossNm, 9);
        // Already wider than that, the downwind is where the aircraft is.
        Assert.Equal(3.0, VisualGuidanceJoin.Decide(P(-1, 3), 180, 140, Gain, Profile).Legs[0].CrossNm, 9);
    }

    // The base is placed the minimum final plus the turn onto final, counted from the aircraft's own speed
    // and bank (the stage-4 checkpoint): the wider of the capture law's curve from the 90° base and the
    // aircraft's own turn. At 140 kt with 25° the curve is wider (1.61 nm against 0.76), so the base stays
    // 7.61 nm out, wide downwind or tight; with 10° of bank at 180 kt the aircraft's own turn is wider and
    // sets it. Where the aircraft lines up from there is the minimum final.
    [Fact]
    public void The_base_counts_the_turn_onto_final()
    {
        Assert.Equal(7.61, VisualGuidanceJoin.Decide(P(-1, 3), 180, 140, Gain, Profile).Legs[1].AlongNm, 0.01);
        Assert.Equal(7.61, VisualGuidanceJoin.Decide(P(-1, 0.3), 180, 140, Gain, Profile).Legs[1].AlongNm, 0.01);
        Assert.InRange(VisualGuidanceJoin.TurnOntoCut(-90, 0, 140, 25, 3).InboundNm, 0.70, 0.80);

        var shallowBank = new VisualGuidanceProfile { MaxApproachBankDeg = 10 };
        var plan = VisualGuidanceJoin.Decide(P(-1, 3), 180, 180, Gain, shallowBank);
        double ownTurn = VisualGuidanceJoin.TurnOntoCut(-90, 0, 180, 10, 3).InboundNm;
        Assert.True(ownTurn > VisualGuidanceJoin.AlongToEstablishNm(plan.Legs[0].CrossNm, 90, 180, Gain), $"own turn {ownTurn}");
        Assert.Equal(6.0 + ownTurn, plan.Legs[1].AlongNm, 9);
        Assert.Equal(6.0, VisualGuidanceJoin.PatternJoinAlongNm(plan, 180, Gain, shallowBank), 9);
        // The left side's turns are left turns of the same size.
        var left = VisualGuidanceJoin.Decide(P(-1, -3), 180, 180, Gain, shallowBank);
        Assert.Equal(plan.Legs[1].AlongNm, left.Legs[1].AlongNm, 9);
    }

    [Fact]
    public void The_profile_defaults_are_the_airliner_values()
    {
        Assert.Equal(25.0, Profile.MaxApproachBankDeg);
        Assert.Equal(6.0, Profile.MinimumFinalNm);
    }
}
