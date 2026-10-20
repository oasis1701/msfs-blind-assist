// Characterization tests for TaxiGraph.FindFullLengthEntrance — the taxiway a plain
// (no intersection, no named holding point) departure enters the runway by, spoken by
// TaxiAssistForm.AnnounceDefaultHoldingPoint as "Full length entry via K16."
//
// Why it exists (OMDB, 2026-09-18): the painted-holding-point call-out speaks a name
// that comes from OSM `aeroway=holding_position` refs. OMDB publishes 235 such nodes —
// all typed (94 runway / 19 ILS / 122 intermediate) and NOT ONE carrying a ref or name.
// So the picker said "No named holding points available for this runway" and arrowing
// through the runway list said nothing at all, at an airport where every runway has a
// perfectly ordinary named entrance. The name is in navdata; only the painted line's
// designator is missing.
//
// Fixture idiom (shared with RunwayLineupEntryTests / BacktrackEntryTests): a synthetic
// east-west runway on the equator, where the code's equirectangular constant
// (111132 m/deg, cos(0)=1) makes along-track metres = degrees-of-longitude x 111132.
// The four cases below are OMDB's four runway ends to scale, measured off the user's
// own fs2020.sqlite.
//
// Pinned behaviors (see the method doc comment):
//   - the entrance NEAREST THE LINEUP POINT wins, not the one nearest the threshold
//     (30R: N9 at +81 m beats N10 at -28 m against a lineup point at +49 m)
//   - an entrance BEHIND the pavement edge is admitted (30L is entered via K16, which
//     meets the runway 6 m behind the edge)
//   - the window is wider than GetRunwayIntersections' 50 m full-length margin
//     (12R is entered via K5, 72 m past the start row)
//   - the window is ASYMMETRIC: 150 m ahead of the lineup point (every metre past it is
//     runway the pilot loses) against 400 m behind it (which costs nothing, and is where
//     starter extensions live — EGLL 09L is entered only from AB13, ~300-355 m back)
//   - nothing named within the window returns null — SILENCE, never the nearest named
//     taxiway at any distance (30L's next named entrance is M18, 695 m down)
//   - unnamed paths never win, which is what keeps the runway's own centerline chain
//     (unnamed in navdata) from being reported as the entrance
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the
// test to match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class FullLengthEntranceTests
{
    private const double MPerDeg = 111132.0;      // metres per degree at the equator
    private const double FarLon = 3600.0 / MPerDeg;  // ~3600 m runway, as OMDB's are
    private const double HalfWidthM = 30.0;       // 197 ft wide -> maxPerp 35 m
    private const double SpineLat = 150.0 / MPerDeg;  // parallel taxiway 150 m north

    private static double Lon(double metres) => metres / MPerDeg;

    /// <summary>
    /// A parallel taxiway north of the runway with NAMED connectors dropping onto the
    /// centerline at the given along-track offsets (metres from the pavement edge;
    /// negative is behind it), plus the unnamed centerline chain navdata really models.
    /// </summary>
    private static TaxiGraph Build(params (string Name, double AlongM)[] entrances)
    {
        var paths = new List<TaxiPath>();

        // The runway's own pavement, as navdata models it: a chain of UNNAMED segments.
        // Present in every case so the tests prove the scan ignores it.
        for (double m = -100; m < 3600; m += 300)
            paths.Add(new TaxiPath
            {
                Name = "",
                StartLat = 0, StartLon = Lon(m),
                EndLat = 0, EndLon = Lon(Math.Min(m + 300, 3600)),
            });

        foreach (var (name, along) in entrances)
        {
            // Spine stub + the connector down onto the centerline, both carrying the name.
            paths.Add(new TaxiPath
            {
                Name = name,
                StartLat = SpineLat, StartLon = Lon(along),
                EndLat = SpineLat, EndLon = Lon(along + 40),
            });
            paths.Add(new TaxiPath
            {
                Name = name,
                StartLat = SpineLat, StartLon = Lon(along),
                EndLat = 0, EndLon = Lon(along),
            });
        }

        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static string? EntranceFor(TaxiGraph g, double lineupAlongM) =>
        g.FindFullLengthEntrance(
            0, 0, 0, FarLon, HalfWidthM,
            lineupLat: 0, lineupLon: Lon(lineupAlongM))?.TaxiwayName;

    // OMDB 30R: lineup point 49 m in; N9 meets the runway at +81 m and N10 at -28 m.
    // N10 leaves MORE runway ahead, but N9 is the entrance at the lineup point and the
    // one a full-length clearance means — so proximity to the lineup point decides,
    // not proximity to the threshold.
    [Fact]
    public void NearestToLineupPointWins_NotNearestToThreshold()
    {
        var g = Build(("N10", -28.5), ("N9", 81.0));
        Assert.Equal("N9", EntranceFor(g, 48.9));
    }

    // OMDB 30L: the threshold apron is modelled but unnamed, and the one named entrance,
    // K16, meets the runway 6 m BEHIND the pavement edge. A non-negative along-track
    // floor (GetRunwayIntersections uses 15 m, to drop the threshold connector) hides it.
    [Fact]
    public void EntranceBehindThePavementEdgeIsAdmitted()
    {
        var g = Build(("K16", -6.4));
        Assert.Equal("K16", EntranceFor(g, 38.8));
    }

    // OMDB 12R: K5 is the full-length entrance and meets the runway 72 m past the start
    // row — outside GetRunwayIntersections' 50 m full-length margin, which exists to keep
    // such an entrance OUT of the intersection list and is tuned tight for that job.
    [Fact]
    public void WindowIsWiderThanTheIntersectionListsFullLengthMargin()
    {
        var g = Build(("K5", 115.1));
        Assert.Equal("K5", EntranceFor(g, 42.7));
    }

    // OMDB 30L as navdata would stand without its K16 stub: the first named entrance is
    // M18, 695 m down a 3,606 m runway. Naming it would describe an intersection
    // departure as a full-length one — the confidently-wrong answer a blind pilot cannot
    // check. Silence is the correct output.
    [Fact]
    public void NothingWithinTheWindowIsSilent_NotTheNearestNamedTaxiway()
    {
        var g = Build(("M18", 695.2), ("K14", 995.1));
        Assert.Null(EntranceFor(g, 38.8));
    }

    // The runway pavement itself is a chain of unnamed taxi_path segments in navdata, so
    // a scan over all graph nodes would return the centerline rather than an entrance.
    [Fact]
    public void UnnamedPathsNeverWin()
    {
        var g = Build();   // centerline chain only
        Assert.Null(EntranceFor(g, 48.9));
    }

    // iniBuilds EGLL 09L: the runway has a starter extension and its ONLY entrance, AB13,
    // meets the pavement ~345 m behind the lineup point. Behind is free — the pilot still
    // gets the whole runway — so the back window reaches it while the forward one stays tight.
    [Fact]
    public void StarterExtensionEntranceFarBehindTheLineupPointIsFound()
    {
        var g = Build(("AB13", -300.0), ("S4", 900.0));
        Assert.Equal("AB13", EntranceFor(g, 45.0));
    }

    // The forward side stays tight: an entrance 200 m PAST the lineup point costs the pilot
    // 200 m of runway and is an intersection departure, not a full-length one.
    [Fact]
    public void ForwardWindowStaysTight()
    {
        var g = Build(("A5", 245.0));
        Assert.Null(EntranceFor(g, 45.0));
    }

    // A corrupt start row past mid-runway has no sane reference to measure against.
    // GetRunwayIntersections degrades to "no filter" there; here the point IS the
    // reference, so the answer is null rather than a guess.
    [Fact]
    public void LineupPointPastMidRunwayIsRefused()
    {
        var g = Build(("K16", -6.4), ("M9", 2400.0));
        Assert.Null(EntranceFor(g, 2400.0));
    }

    // Two entrances equidistant either side of the lineup point: the one FURTHER BACK
    // wins, because it leaves more runway ahead.
    [Fact]
    public void EquidistantTieBreaksToTheEntranceFurtherBack()
    {
        var g = Build(("A1", 0.0), ("A2", 100.0));
        Assert.Equal("A1", EntranceFor(g, 50.0));
    }
}
