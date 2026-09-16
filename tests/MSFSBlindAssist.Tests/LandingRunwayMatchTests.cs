// Characterization tests for LandingRunwayMatch — "is the aircraft that just touched down
// actually on the runway the landing-exit plan was made for?"
//
// Regression pinned: OMDB, 2026-09-12 (issue #234), and the identical 2026-09-06 landing
// six days earlier.
//   The pilot planned exit M13 in the landing-exit planner, which had defaulted to the
//   first runway in the list (12L), and then landed on 30L — a different physical runway,
//   and the reciprocal direction into the bargain. LandingExitPlanner handed the 12L frame
//   to BeginLandingRollout unchecked, so on the FIRST rollout update every measurement was
//   computed 179.2 degrees backwards:
//
//     UpdateLandingRollout periodic: signedAlongPast=4570ft hdgDelta=179.2deg pastExit=True
//     UpdateLandingRollout HANDOFF -> Taxiing: exitedLaterally=True lateral=1263ft
//
//   The rollout was abandoned on the frame it began — no touchdown callout, no 1500/500 ft
//   approach calls, no turn cue — and normal taxi guidance took over and started naming
//   taxiways. That is exactly what was reported: "the minute i touched down, it started
//   reading out taxiways instead of applying the rollout standard."
//
// Fixture geometry is on the equator (lat 0) so one degree of latitude and one of longitude
// are both 111,320 m — RunwayFrame's own constant.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingRunwayMatchTests
{
    private const double M_PER_DEG = 111320.0;   // RunwayFrame.DEG_TO_M_LAT
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    private const double LengthM = 3000.0;
    private const double WidthFt = 150.0;                       // half-width 22.9 m

    /// <summary>Runway 09: threshold at (0,0), running due east.</summary>
    private static Runway Rwy09() => new Runway
    {
        RunwayID = "09", Heading = 90.0, Length = LengthM / 0.3048, Width = WidthFt,
        StartLat = 0.0, StartLon = 0.0,
        EndLat = 0.0,   EndLon = LengthM * DEG_PER_M,
    };

    /// <summary>Runway 27: the same pavement, the other way round.</summary>
    private static Runway Rwy27() => new Runway
    {
        RunwayID = "27", Heading = 270.0, Length = LengthM / 0.3048, Width = WidthFt,
        StartLat = 0.0, StartLon = LengthM * DEG_PER_M,
        EndLat = 0.0,   EndLon = 0.0,
    };

    /// <summary>A parallel runway 800 m north — the OMDB 12L/30L separation in miniature.</summary>
    private static Runway Rwy09Parallel() => new Runway
    {
        RunwayID = "09R", Heading = 90.0, Length = LengthM / 0.3048, Width = WidthFt,
        StartLat = 800.0 * DEG_PER_M, StartLon = 0.0,
        EndLat = 800.0 * DEG_PER_M,   EndLon = LengthM * DEG_PER_M,
    };

    private static Runway Rwy27Parallel() => new Runway
    {
        RunwayID = "27L", Heading = 270.0, Length = LengthM / 0.3048, Width = WidthFt,
        StartLat = 800.0 * DEG_PER_M, StartLon = LengthM * DEG_PER_M,
        EndLat = 800.0 * DEG_PER_M,   EndLon = 0.0,
    };

    private static List<Runway> AllFour() =>
        new() { Rwy09(), Rwy27(), Rwy09Parallel(), Rwy27Parallel() };

    // 760 m down the runway, 3 m off the centreline — the reporter's own touchdown offsets,
    // measured from their database.
    private const double AlongM = 760.0;
    private const double CrossM = 3.0;

    [Fact]
    public void Rolling_down_the_planned_runway_matches()
    {
        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 90.0,
            Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
    }

    [Fact]
    public void A_small_touchdown_crab_still_matches()
    {
        // Touchdown yaw and crosswind crab routinely leave the heading several degrees off
        // the runway; that must never read as the wrong runway.
        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 97.0,
            Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
    }

    [Fact]
    public void Landing_the_other_way_down_the_same_pavement_is_the_reciprocal_end()
    {
        // Planned 09, landed 27: same pavement, so the pilot's chosen exit taxiway is still
        // the right pavement — it is just a different distance from the other threshold.
        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 270.0,
            Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.ReciprocalEnd, r.Verdict);
        Assert.Equal("27", r.Actual?.RunwayID);
    }

    [Fact]
    public void Landing_on_the_parallel_runway_is_a_different_runway()
    {
        // The reported OMDB shape: planned 12L, landed 30L — another runway entirely, and
        // reversed. The plan's exit belongs to pavement the aircraft is not on.
        var r = LandingRunwayMatch.Evaluate(
            (800.0 + CrossM) * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 270.0,
            Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("27L", r.Actual?.RunwayID);
    }

    [Fact]
    public void The_reported_heading_picks_the_end_the_aircraft_is_rolling_towards()
    {
        // Same position on the parallel, rolling the other way: the verdict must name 09R,
        // not 27L — the runway-end countdown measures to the end AHEAD of the aircraft.
        var r = LandingRunwayMatch.Evaluate(
            (800.0 + CrossM) * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 90.0,
            Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("09R", r.Actual?.RunwayID);
    }

    [Fact]
    public void A_touchdown_off_every_runway_is_unknown()
    {
        // Nothing to correct to, so the caller must keep today's behaviour rather than
        // invent a runway.
        var r = LandingRunwayMatch.Evaluate(
            5000.0 * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 90.0,
            Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.Unknown, r.Verdict);
        Assert.Null(r.Actual);
    }

    [Fact]
    public void A_reciprocal_end_missing_from_the_list_degrades_to_unknown()
    {
        // Never report a correction the caller cannot act on: without the 27 row there is
        // no frame to re-measure from, so this must fall back to today's behaviour.
        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 270.0,
            Rwy09(), new List<Runway> { Rwy09() });

        Assert.Equal(LandingRunwayVerdict.Unknown, r.Verdict);
    }

    [Fact]
    public void Landing_beyond_the_far_end_is_not_on_that_runway()
    {
        // Guards the along-track window: a point off the end of 09 must not read as being
        // on 09 just because it is still on the extended centreline.
        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, (LengthM + 400.0) * DEG_PER_M, headingTrue: 90.0,
            Rwy09(), new List<Runway> { Rwy09(), Rwy27() });

        Assert.Equal(LandingRunwayVerdict.Unknown, r.Verdict);
    }

    // ---------------------------------------------------------------------------------
    // The reported landings, replayed against OMDB's REAL geometry.
    //
    // Runway rows transcribed from the reporter's own fs2024.sqlite (attached to #234) as
    // LittleNavMapProvider.GetRunways returns them — one Runway per END, Heading true, the
    // runway table's own thresholds. Touchdown positions and headings are the
    // ProcessGroundState lines from their landing_exit.log.
    // ---------------------------------------------------------------------------------

    private static Runway Omdb(string id, double hdg, double lenFt,
                               double sLat, double sLon, double eLat, double eLon)
        => new Runway
        {
            RunwayID = id, Heading = hdg, Length = lenFt, Width = 197.0,
            StartLat = sLat, StartLon = sLon, EndLat = eLat, EndLon = eLon,
        };

    private static List<Runway> OmdbRunways() => new()
    {
        Omdb("12L", 121.40273, 13282, 25.266695023, 55.346588135, 25.247714996, 55.380981445),
        Omdb("30R", 301.40274, 13282, 25.247714996, 55.380981445, 25.266695023, 55.346588135),
        Omdb("12R", 121.40859, 12219, 25.252784729, 55.364379883, 25.235319138, 55.395996094),
        Omdb("30L", 301.40860, 12219, 25.235319138, 55.395996094, 25.252784729, 55.364379883),
    };

    private static Runway Omdb(string id) => OmdbRunways().First(r => r.RunwayID == id);

    [Fact]
    public void Omdb_2026_09_12_planned_12L_landed_30L_is_a_different_runway()
    {
        // landing_exit.log 17:30:48.467 — "onGround=True gs=161.8 lat=25.238902
        // lon=55.389573 hdgTrue=302.2", against a plan made for 12L.
        var r = LandingRunwayMatch.Evaluate(
            25.238902, 55.389573, 302.2, Omdb("12L"), OmdbRunways());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("30L", r.Actual?.RunwayID);
    }

    [Fact]
    public void Omdb_2026_09_06_the_same_fault_six_days_earlier()
    {
        // landing_exit.log 13:05:01.196 — the identical shape, exit M12A instead of M13.
        var r = LandingRunwayMatch.Evaluate(
            25.241431, 55.385098, 299.7, Omdb("12L"), OmdbRunways());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("30L", r.Actual?.RunwayID);
    }

    [Fact]
    public void Omdb_2026_09_04_the_landing_that_worked_is_left_alone()
    {
        // The same pilot, same airport, eight days earlier, with the runway set correctly:
        // "runway=30L exit='K6' ... lat=25.239674 lon=55.388190 hdgTrue=301.5". This is the
        // control — the fix must not touch a landing that already worked.
        var r = LandingRunwayMatch.Evaluate(
            25.239674, 55.388190, 301.5, Omdb("30L"), OmdbRunways());

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
        Assert.Null(r.Actual);
    }

    [Fact]
    public void Omdb_planning_12R_and_landing_30L_is_the_reciprocal_end()
    {
        // Right pavement, wrong end — the other way a pilot gets this wrong, and the one
        // that is recoverable: their exit taxiway is still the correct pavement.
        var r = LandingRunwayMatch.Evaluate(
            25.239674, 55.388190, 301.5, Omdb("12R"), OmdbRunways());

        Assert.Equal(LandingRunwayVerdict.ReciprocalEnd, r.Verdict);
        Assert.Equal("30L", r.Actual?.RunwayID);
    }

    [Fact]
    public void An_empty_runway_list_still_judges_the_planned_runway()
    {
        // The planned runway is judged from its own geometry, so an airport list that never
        // arrived cannot turn a correct landing into a correction — or an exception.
        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 90.0,
            Rwy09(), new List<Runway>());

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);

        // Reversed, with nothing to correct to, it degrades rather than guesses.
        var reversed = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 270.0,
            Rwy09(), new List<Runway>());

        Assert.Equal(LandingRunwayVerdict.Unknown, reversed.Verdict);
    }

    // ---------------------------------------------------------------------------------
    // Intersecting runways (PR #236 review, finding F2). Rows transcribed read-only from
    // fs2024.sqlite exactly as LittleNavMapProvider.GetRunways builds them (one Runway per
    // END, Start = this end, End = the opposite end). Positions are the centreline
    // intersection computed with RunwayFrame's own projection (case C: 5 m off 19's
    // centreline toward 15), replayed in Python against the same algorithm before use.
    // ---------------------------------------------------------------------------------

    private static Runway Rw(string id, double hdg, double lenFt, double widthFt, double offsetFt,
                             double sLat, double sLon, double eLat, double eLon)
        => new Runway
        {
            RunwayID = id, Heading = hdg, Length = lenFt, Width = widthFt, ThresholdOffset = offsetFt,
            StartLat = sLat, StartLon = sLon, EndLat = eLat, EndLon = eLon,
        };

    private static List<Runway> KdcaRunways() => new()
    {
        Rw("04", 25.469158172607422, 5001.0, 146.0, 203.0, 38.84112548828125, -77.04145812988281, 38.85350799560547, -77.03388977050781),
        Rw("22", 205.4691619873047, 5001.0, 146.0, 0.0, 38.85350799560547, -77.03388977050781, 38.84112548828125, -77.04145812988281),
        Rw("15", 142.76260375976562, 5202.0, 147.0, 0.0, 38.86116409301758, -77.04330444335938, 38.84980392456055, -77.03221130371094),
        Rw("33", 322.7626037597656, 5202.0, 147.0, 0.0, 38.84980392456055, -77.03221130371094, 38.86116409301758, -77.04330444335938),
        Rw("19", 175.5161590576172, 7170.0, 146.0, 0.0, 38.861183166503906, -77.03872680664062, 38.841575622558594, -77.03675842285156),
        Rw("01", 355.51617431640625, 7170.0, 146.0, 0.0, 38.841575622558594, -77.03675842285156, 38.861183166503906, -77.03872680664062),
    };

    private static Runway Kdca(string id) => KdcaRunways().First(r => r.RunwayID == id);

    private static List<Runway> KphlRunways() => new()
    {
        Rw("17", 159.1230010986328, 6491.0, 151.0, 0.0, 39.88765335083008, -75.236083984375, 39.87102127075195, -75.22781372070312),
        Rw("35", 339.12298583984375, 6491.0, 151.0, 0.0, 39.87102127075195, -75.22781372070312, 39.88765335083008, -75.236083984375),
        Rw("26", 255.42579650878906, 4994.0, 151.0, 0.0, 39.88178634643555, -75.21275329589844, 39.87833786010742, -75.23002624511719),
        Rw("08", 75.42579650878906, 4994.0, 151.0, 0.0, 39.87833786010742, -75.23002624511719, 39.88178634643555, -75.21275329589844),
        Rw("27L", 255.46890258789062, 12008.0, 200.0, 1936.0, 39.86908721923828, -75.23371124267578, 39.86082077026367, -75.2752456665039),
        Rw("09R", 75.46890258789062, 12008.0, 200.0, 0.0, 39.86082077026367, -75.2752456665039, 39.86908721923828, -75.23371124267578),
        Rw("27R", 255.47740173339844, 9492.0, 151.0, 0.0, 39.87522506713867, -75.22286224365234, 39.86869812011719, -75.25569915771484),
        Rw("09L", 75.47740173339844, 9492.0, 151.0, 0.0, 39.86869812011719, -75.25569915771484, 39.87522506713867, -75.22286224365234),
    };

    private static Runway Kphl(string id) => KphlRunways().First(r => r.RunwayID == id);

    [Fact]
    public void Kdca_plan_04_touchdown_on_01_inside_the_intersection_is_a_different_runway()
    {
        // The aircraft is on BOTH pavements here. Heading alignment decides: 01 is aligned,
        // 04 is 30 degrees off. The old 90-degree rule read this as Matches (#234 again).
        var r = LandingRunwayMatch.Evaluate(
            38.84778733508005, -77.03738381584637, 355.51617431640625, Kdca("04"), KdcaRunways());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("01", r.Actual?.RunwayID);
    }

    [Fact]
    public void Kphl_plan_17_touchdown_on_27R_at_the_crossing_is_a_different_runway_not_the_reciprocal()
    {
        // On 17's pavement, rolling 96 degrees off it: the old rule labelled the crossing
        // runway "ReciprocalEnd" and swapped the frame to it while keeping 17's exit.
        var r = LandingRunwayMatch.Evaluate(
            39.87395042087517, -75.22927403937646, 255.47740173339844, Kphl("17"), KphlRunways());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("27R", r.Actual?.RunwayID);
    }

    [Fact]
    public void Kdca_plan_04_touchdown_on_19_beside_runway_15_names_19_not_15()
    {
        // 5.0 m off 19's centreline and 4.7 m off 15's: nearest-centreline picked 15 and ran
        // the countdown along the wrong runway. Alignment picks 19.
        var r = LandingRunwayMatch.Evaluate(
            38.855944789035725, -77.03814140186083, 175.5161590576172, Kdca("04"), KdcaRunways());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("19", r.Actual?.RunwayID);
    }

    /// <summary>A runway crossing 09 at right angles, 1,000 m east of 09's threshold.</summary>
    private static Runway Rwy36Crossing() => new Runway
    {
        RunwayID = "36", Heading = 0.0, Length = LengthM / 0.3048, Width = WidthFt,
        StartLat = -1500.0 * DEG_PER_M, StartLon = 1000.0 * DEG_PER_M,
        EndLat = 1500.0 * DEG_PER_M,    EndLon = 1000.0 * DEG_PER_M,
    };

    [Fact]
    public void A_crossing_runway_is_never_the_reciprocal_end()
    {
        var r = LandingRunwayMatch.Evaluate(
            0.0, 1000.0 * DEG_PER_M, headingTrue: 0.0,
            Rwy09(), new List<Runway> { Rwy09(), Rwy27(), Rwy36Crossing() });

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("36", r.Actual?.RunwayID);
    }

    [Fact]
    public void The_reciprocal_end_is_recognised_by_its_swapped_endpoints()
    {
        Assert.True(LandingRunwayMatch.IsTwin(Rwy27(), Rwy09()));
        Assert.False(LandingRunwayMatch.IsTwin(Rwy36Crossing(), Rwy09()));
        Assert.False(LandingRunwayMatch.IsTwin(Rwy27Parallel(), Rwy09()));
    }

    [Fact]
    public void Alignment_tolerance_is_45_degrees()
    {
        var at44 = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 134.0, Rwy09(), AllFour());
        var at46 = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, headingTrue: 136.0, Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.Matches, at44.Verdict);
        Assert.Equal(LandingRunwayVerdict.Unknown, at46.Verdict);
    }

    [Fact]
    public void Approach_mode_accepts_an_airborne_point_before_the_threshold()
    {
        // 250 m short of 09's threshold on the extended centreline: still "approaching 09"
        // with the flare assist's 300 m margin, not "on 09" with the touchdown margin.
        var approach = LandingRunwayMatch.Evaluate(
            0.0, -250.0 * DEG_PER_M, 90.0, Rwy09(), AllFour(),
            LandingRunwayMatch.ApproachBeforeThresholdMarginM);
        var touchdown = LandingRunwayMatch.Evaluate(
            0.0, -250.0 * DEG_PER_M, 90.0, Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.Matches, approach.Verdict);
        Assert.Equal(LandingRunwayVerdict.Unknown, touchdown.Verdict);
    }

    // ---------------------------------------------------------------------------------
    // Near-parallel runway ends whose heading differs by hundredths of a degree
    // (PR #236 final review, finding A). A single-pass "smallest heading delta, with
    // cross-track only breaking an EXACT tie" comparison decides these by which side of
    // that tiny gap the aircraft's touchdown heading happens to fall on, not by which
    // centreline the aircraft is actually on.
    //
    // Rows transcribed read-only from fs2024.sqlite exactly as LittleNavMapProvider.
    // GetRunways builds them (one Runway per END). LFCH 25/25L: heading differs by
    // 0.0848 degrees; 25's threshold projects 50.1 m from 25L's centreline (49.1 m at
    // the touchdown point below — near-parallel runways, so the offset barely changes
    // along their length), well inside 25L's 53.1 m corridor (half-width 38.1 m + the
    // 15 m off-pavement margin shared with RunwayVacateResolver.IsOffPavement) — so a
    // landing on 25 is geometrically also "on" 25L's pavement per IsOnRunway. Touchdown
    // point: 760 m down 25's own centreline (cross-track 0 against 25, ~49 m against
    // 25L; both runways' along-track windows contain it).
    // ---------------------------------------------------------------------------------

    private static List<Runway> LfchRunways() => new()
    {
        Rw("25", 252.42982482910156, 4586.0, 60.0, 1290.0, 44.598228454589844, -1.1036442518234253, 44.59442901611328, -1.1204839944839478),
        Rw("07", 72.42982482910156, 4586.0, 60.0, 353.0, 44.59442901611328, -1.1204839944839478, 44.598228454589844, -1.1036442518234253),
        Rw("25L", 252.51466369628906, 3866.0, 250.0, 1239.0, 44.59756088256836, -1.1045126914978027, 44.5943717956543, -1.1187163591384888),
        Rw("07R", 72.51466369628906, 3866.0, 250.0, 0.0, 44.5943717956543, -1.1187163591384888, 44.59756088256836, -1.1045126914978027),
    };

    private static Runway Lfch(string id) => LfchRunways().First(r => r.RunwayID == id);

    // 760 m down 25's centreline from its threshold, cross-track 0 against 25.
    private const double LfchTouchdownLat = 44.59616751322576;
    private const double LfchTouchdownLon = -1.1127850201686644;
    private const double Lfch25HeadingDeg = 252.42982482910156;

    [Fact]
    public void Lfch_25_plan_matches_one_degree_either_side_of_25s_own_heading()
    {
        // 25L's heading is only 0.0848 degrees off 25's, so a +1 degree touchdown
        // heading lands fractionally closer to 25L than to 25 (0.915 vs 1.0 degrees) —
        // the old single-pass rule picked 25L outright on that side of the gap.
        var plus1 = LandingRunwayMatch.Evaluate(
            LfchTouchdownLat, LfchTouchdownLon, headingTrue: Lfch25HeadingDeg + 1.0,
            Lfch("25"), LfchRunways());
        var minus1 = LandingRunwayMatch.Evaluate(
            LfchTouchdownLat, LfchTouchdownLon, headingTrue: Lfch25HeadingDeg - 1.0,
            Lfch("25"), LfchRunways());

        Assert.Equal(LandingRunwayVerdict.Matches, plus1.Verdict);
        Assert.Equal(LandingRunwayVerdict.Matches, minus1.Verdict);
    }

    [Fact]
    public void Lfch_25L_plan_on_25s_centreline_is_a_different_runway()
    {
        // Same position, planned for the neighbour instead: physically on 25, not 25L.
        var r = LandingRunwayMatch.Evaluate(
            LfchTouchdownLat, LfchTouchdownLon, headingTrue: Lfch25HeadingDeg,
            Lfch("25L"), LfchRunways());

        Assert.Equal(LandingRunwayVerdict.DifferentRunway, r.Verdict);
        Assert.Equal("25", r.Actual?.RunwayID);
    }

    // ---------------------------------------------------------------------------------
    // PR #236 follow-up: what counts as a runway you can land on.
    //
    // The candidate list is the airport's WHOLE runway table straight from the navigation
    // database, while the list the pilot may PLAN on excludes closed runways. So a runway the
    // planner would never offer could still win the touchdown match, be named back to the pilot
    // ("Touchdown on runway 08, not 09") and have the rollout re-planned onto it.
    //
    // Two database shapes make that reachable. A magnetic-drift renumbering leaves a second,
    // closed record on or beside the same pavement; and a water runway (1,166 W-suffixed ends in
    // an fs2024 build) sits in the runway table with a scenery-authored width but no taxiways at
    // all, so winning it turns a perfectly normal landing into "no usable exit". Runway START
    // positions are already filtered for water in the database layer; the runway table is not.
    // ---------------------------------------------------------------------------------

    /// <summary>A closed record whose centreline passes exactly through the touchdown point, so it
    /// beats the open runway on cross-track — the renumbering shape, in miniature.</summary>
    private static Runway ClosedRenumberOf09() => new Runway
    {
        RunwayID = "08", Heading = 89.9, Length = LengthM / 0.3048, Width = WidthFt,
        StartLat = CrossM * DEG_PER_M, StartLon = 0.0,
        EndLat = CrossM * DEG_PER_M,   EndLon = LengthM * DEG_PER_M,
        IsClosed = true,
    };

    /// <summary>A water runway on the same alignment, also through the touchdown point.</summary>
    private static Runway WaterLaneOver09() => new Runway
    {
        RunwayID = "09W", Heading = 89.9, Length = LengthM / 0.3048, Width = 600.0,
        StartLat = CrossM * DEG_PER_M, StartLon = 0.0,
        EndLat = CrossM * DEG_PER_M,   EndLon = LengthM * DEG_PER_M,
        Surface = 2,   // Runway.GetSurfaceType: 2 = Water
    };

    [Fact]
    public void A_closed_runway_record_never_wins_the_touchdown()
    {
        var runways = new List<Runway> { Rwy09(), Rwy27(), ClosedRenumberOf09() };

        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, 90.0, Rwy09(), runways);

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
    }

    [Fact]
    public void A_water_runway_never_wins_the_touchdown()
    {
        var runways = new List<Runway> { Rwy09(), Rwy27(), WaterLaneOver09() };

        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, 90.0, Rwy09(), runways);

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
    }

    [Fact]
    public void The_runway_the_pilot_planned_on_is_judged_even_when_the_database_calls_it_closed()
    {
        // The pilot picked it, so it is theirs to land on whatever the database says. Landing on
        // it must still read as a match, not as "runway not identified".
        var planned = Rwy09();
        planned.IsClosed = true;

        var r = LandingRunwayMatch.Evaluate(
            CrossM * DEG_PER_M, AlongM * DEG_PER_M, 90.0, planned,
            new List<Runway> { planned, Rwy27() });

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
    }

    // ---------------------------------------------------------------------------------
    // PR #236 follow-up: one answer to "am I still on the runway?".
    //
    // The touchdown match and the runway-end countdown ask that question of the same aircraft one
    // frame apart — the match decides which runway the rollout is measured in, the countdown then
    // decides whether the aircraft has left it. They were asking two different questions: the
    // match allowed half-width + 15 m and defaulted an unrecorded width to 150 ft, the countdown
    // allowed half-width + 10 m and defaulted to 200 ft. In the band between them the app believed
    // the aircraft was on the runway AND had vacated it, and the very first countdown frame said
    // "Runway vacated", stopped the tone and dropped guidance at landing speed. The rollout's own
    // test is the one the rest of the app already shares, so the match now uses it too.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void The_edge_of_the_pavement_is_where_the_rest_of_the_rollout_puts_it()
    {
        // 35 m off the centreline of a 150 ft runway: 12 m outside the edge. The rollout has always
        // called this clear of the runway; the match used to call it still on it.
        Assert.True(RolloutExitGate.IsLaterallyClearOfRunway(35.0, WidthFt));

        var r = LandingRunwayMatch.Evaluate(
            35.0 * DEG_PER_M, AlongM * DEG_PER_M, 90.0, Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.Unknown, r.Verdict);
    }

    [Fact]
    public void Well_inside_the_pavement_is_still_a_match()
    {
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(20.0, WidthFt));

        var r = LandingRunwayMatch.Evaluate(
            20.0 * DEG_PER_M, AlongM * DEG_PER_M, 90.0, Rwy09(), AllFour());

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
    }

    [Fact]
    public void A_runway_with_no_recorded_width_uses_the_same_fallback_as_the_rollout()
    {
        // Plenty of scenery leaves the width column empty. The two tests disagreed here too, in
        // the other direction — the match was the STRICTER of the pair.
        var noWidth = Rwy09();
        noWidth.Width = 0.0;
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(39.0, 0.0));

        var r = LandingRunwayMatch.Evaluate(
            39.0 * DEG_PER_M, AlongM * DEG_PER_M, 90.0, noWidth, new List<Runway> { noWidth });

        Assert.Equal(LandingRunwayVerdict.Matches, r.Verdict);
    }
}
