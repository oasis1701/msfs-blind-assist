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
}
