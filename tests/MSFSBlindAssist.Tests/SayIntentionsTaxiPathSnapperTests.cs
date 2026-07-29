// Characterization for turning SayIntentions' taxi_path GEOMETRY into a taxiway
// sequence. Measured from a live LSZH arrival, 2026-07-29: Zurich Ground cleared
// "Taxi to Gate E52 via E4, E, C", and the capture taken 9 s later snaps to
//
//     E4x5  Ex7  "Link 6"x1  Ex1  "Link 5"x1  Ex10  Cx11  (unsnapped)x4
//
// which is exactly the cleared route once single-point connector stubs are dropped.
// "Link 5"/"Link 6"/"Inner" are OSM names for connectors ATC never says; the four
// trailing unsnapped points are the lead-in to stand E52, which is apron, not
// taxiway pavement, so nothing in the taxiway data is within tolerance of them.
// (An earlier probe run without the 25 m tolerance reported that tail as Cx15 —
// same answer, because a whole run of stand points beyond every edge still cannot
// out-vote the C run it follows. The numbers above are the measurement WITH the
// tolerance the shipped constant applies, and TheLiveLszhArrivalIsFourtyPoints...
// pins them so the comment cannot drift.)
//
// The run-length filter is not a tuned constant: thresholds 2 and 3 give the same
// answer on the real data.
//
// Fixtures (tests/MSFSBlindAssist.Tests/Fixtures/):
//   lszh-taxipath-arrival.json — the current_flight.taxi_path array of that capture,
//     verbatim, with the rest of flight.json dropped. flight.json carries the
//     pilot's Email/displayname/userid/api_key in plain text and must never be
//     committed whole; nothing but the geometry is needed here anyway. The entries
//     keep the wire's inconsistent key order ("point" first on some, "heading"
//     first on others) on purpose — that is what a reader has to survive.
//   lszh-taxiways.json — a byte-for-byte copy of the OSM taxiway probe output for
//     LSZH (name + geometry only, no personal data), kept in its source POLYLINE
//     shape so it stays re-derivable from the capture. NamedEdge is one SEGMENT, so
//     LoadFixtureEdges splits each polyline into consecutive point pairs; feeding a
//     whole polyline in as a single edge would measure distance to the chord across
//     a curving taxiway rather than to the taxiway.

using System.Collections.Concurrent;
using System.Text.Json;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsTaxiPathSnapperTests
{
    // ---- Synthetic geometry, so the maths is pinned independently of the fixture ----
    //
    // Two edges meeting at a corner near 50N 8E: "A" runs 111 m north, "B" runs 72 m
    // east from A's far end. Distances at this latitude: 0.0001 deg of latitude is
    // 11.13 m, 0.0001 deg of longitude is 7.16 m.
    private static readonly NamedEdge EdgeA = new("A", 50.0000, 8.0000, 50.0010, 8.0000);
    private static readonly NamedEdge EdgeB = new("B", 50.0010, 8.0000, 50.0010, 8.0010);
    private static readonly NamedEdge[] CornerEdges = { EdgeA, EdgeB };

    private static GeoPoint OnA(double fraction) => new(50.0000 + 0.0010 * fraction, 8.0000);
    private static GeoPoint OnB(double fraction) => new(50.0010, 8.0000 + 0.0010 * fraction);

    [Fact]
    public void TheLiveLszhArrivalReproducesTheClearedRoute()
    {
        var path = LoadFixturePath("lszh-taxipath-arrival.json");
        var edges = LoadFixtureEdges("lszh-taxiways.json");

        var result = SayIntentionsTaxiPathSnapper.Snap(path, edges);

        Assert.Equal(new[] { "E4", "E", "C" }, result.Taxiways);
    }

    [Fact]
    public void ConnectorStubsAreDroppedNotReported()
    {
        var result = SayIntentionsTaxiPathSnapper.Snap(
            LoadFixturePath("lszh-taxipath-arrival.json"),
            LoadFixtureEdges("lszh-taxiways.json"));

        Assert.DoesNotContain("Link 5", result.Taxiways);
        Assert.DoesNotContain("Link 6", result.Taxiways);
    }

    [Fact]
    public void TheLiveLszhArrivalCountsTheStandLeadInAsUnsnapped()
    {
        // The tail of the path is the turn into stand E52, off taxiway pavement.
        // Counting those four points is the whole point of UnsnappedCount: raising
        // the tolerance until they attach to something would silently hang the
        // stand lead-in on whichever taxiway happens to be nearest.
        var result = SayIntentionsTaxiPathSnapper.Snap(
            LoadFixturePath("lszh-taxipath-arrival.json"),
            LoadFixtureEdges("lszh-taxiways.json"));

        Assert.Equal(40, result.PointCount);
        Assert.Equal(4, result.UnsnappedCount);
    }

    [Fact]
    public void AStraightRunAlongOneEdgeThenAnotherYieldsBothInOrder()
    {
        var path = new[] { OnA(0.2), OnA(0.4), OnA(0.6), OnB(0.2), OnB(0.4), OnB(0.6) };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, CornerEdges);

        Assert.Equal(new[] { "A", "B" }, result.Taxiways);
        Assert.Equal(6, result.PointCount);
        Assert.Equal(0, result.UnsnappedCount);
    }

    [Fact]
    public void APointFartherThanToleranceFromEveryEdgeIsCountedNotGuessed()
    {
        // Off-pavement points must raise UnsnappedCount rather than attaching to the
        // nearest thing at any distance — a silently wrong leg is the failure mode
        // this whole feature exists to remove.
        var edges = new[] { new NamedEdge("A", 50.0000, 8.0000, 50.0010, 8.0000) };
        var path = new[] { new GeoPoint(50.0005, 8.0000), new GeoPoint(50.0005, 8.0100) };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, edges);

        Assert.Equal(1, result.UnsnappedCount);
    }

    [Fact]
    public void ASinglePointRunIsDroppedAndTheTaxiwayEitherSideBecomesOne()
    {
        // The LSZH pattern in miniature: E x7, Link6 x1, E x1... The stub is dropped
        // FIRST, and only then do the two surviving runs of A collapse into one.
        // Collapsing before filtering would leave every one-point connector standing.
        var path = new[] { OnA(0.2), OnA(0.4), OnB(0.5), OnA(0.6), OnA(0.8) };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, CornerEdges);

        Assert.Equal(new[] { "A" }, result.Taxiways);
    }

    [Fact]
    public void ATaxiwayRevisitedAfterAnotherIsKeptTwice()
    {
        // Non-consecutive reuse is real (a clearance crosses and comes back). Only
        // CONSECUTIVE duplicates collapse.
        var path = new[] { OnA(0.1), OnA(0.2), OnB(0.5), OnB(0.7), OnA(0.8), OnA(0.9) };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, CornerEdges);

        Assert.Equal(new[] { "A", "B", "A" }, result.Taxiways);
    }

    [Fact]
    public void AnUnsnappedPointBreaksARunRatherThanBeingSkippedOver()
    {
        // Run lengths are counted over the raw per-point sequence INCLUDING the
        // misses. If a miss were skipped instead, these two lone A points either
        // side of it would merge into a run of two and survive the filter — i.e. a
        // taxiway would be reported off the strength of a gap in the data.
        var path = new[] { OnA(0.2), new GeoPoint(50.0005, 8.0100), OnA(0.4) };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, CornerEdges);

        Assert.Empty(result.Taxiways);
        Assert.Equal(1, result.UnsnappedCount);
    }

    [Fact]
    public void AnEmptyPathYieldsAnEmptySequenceNotAnError()
    {
        var result = SayIntentionsTaxiPathSnapper.Snap(Array.Empty<GeoPoint>(), CornerEdges);

        Assert.Empty(result.Taxiways);
        Assert.Equal(0, result.PointCount);
        Assert.Equal(0, result.UnsnappedCount);
    }

    [Fact]
    public void AnAirportWithNoNamedEdgesLeavesEveryPointUnsnapped()
    {
        // Degrades to "we cannot tell", never to an exception: an airport whose
        // taxiway data failed to load must not take the caller down with it.
        var path = new[] { OnA(0.2), OnA(0.4) };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, Array.Empty<NamedEdge>());

        Assert.Empty(result.Taxiways);
        Assert.Equal(2, result.PointCount);
        Assert.Equal(2, result.UnsnappedCount);
    }

    // ---- PointToSegmentMetres ----

    [Fact]
    public void APointOnTheSegmentIsZeroMetresFromIt()
    {
        Assert.Equal(0.0, SayIntentionsTaxiPathSnapper.PointToSegmentMetres(
            50.0005, 8.0000, 50.0000, 8.0000, 50.0010, 8.0000), 3);
    }

    [Fact]
    public void APointBesideTheSegmentIsItsPerpendicularDistance()
    {
        // 0.0010 deg of longitude at 50N = 111320 * cos(50.0005 deg) * 0.0010.
        Assert.Equal(71.554, SayIntentionsTaxiPathSnapper.PointToSegmentMetres(
            50.0005, 8.0010, 50.0000, 8.0000, 50.0010, 8.0000), 3);
    }

    [Fact]
    public void APointBeyondTheEndOfTheSegmentMeasuresToTheEndpoint()
    {
        // 0.0020 deg of latitude past the far end = 222.64 m. Without the t clamp
        // this reads 0 — the point sits exactly on the segment's INFINITE line — and
        // every taxiway in the airport becomes a candidate for every point on it.
        Assert.Equal(222.640, SayIntentionsTaxiPathSnapper.PointToSegmentMetres(
            50.0030, 8.0000, 50.0000, 8.0000, 50.0010, 8.0000), 3);

        Assert.Equal(111.320, SayIntentionsTaxiPathSnapper.PointToSegmentMetres(
            49.9990, 8.0000, 50.0000, 8.0000, 50.0010, 8.0000), 3);
    }

    [Fact]
    public void AZeroLengthSegmentMeasuresToItsPointInsteadOfDividingByZero()
    {
        // Duplicate consecutive nodes exist in real OSM ways.
        Assert.Equal(111.320, SayIntentionsTaxiPathSnapper.PointToSegmentMetres(
            50.0010, 8.0000, 50.0000, 8.0000, 50.0000, 8.0000), 3);
    }

    // ---- Fixture loading ----

    // Parsed once per name: the taxiway fixture is 3114 segments across 108 ways and
    // several tests want it.
    private static readonly ConcurrentDictionary<string, IReadOnlyList<GeoPoint>> PathCache = new();
    private static readonly ConcurrentDictionary<string, IReadOnlyList<NamedEdge>> EdgeCache = new();

    private static IReadOnlyList<GeoPoint> LoadFixturePath(string fileName) =>
        PathCache.GetOrAdd(fileName, ReadPath);

    private static IReadOnlyList<NamedEdge> LoadFixtureEdges(string fileName) =>
        EdgeCache.GetOrAdd(fileName, ReadEdges);

    private static IReadOnlyList<GeoPoint> ReadPath(string fileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath(fileName)));
        var points = new List<GeoPoint>();
        foreach (var entry in doc.RootElement.GetProperty("taxi_path").EnumerateArray())
        {
            var point = entry.GetProperty("point");
            points.Add(new GeoPoint(
                point.GetProperty("lat").GetDouble(),
                point.GetProperty("lon").GetDouble()));
        }
        return points;
    }

    private static IReadOnlyList<NamedEdge> ReadEdges(string fileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath(fileName)));
        var edges = new List<NamedEdge>();
        foreach (var way in doc.RootElement.EnumerateArray())
        {
            var name = way.GetProperty("name").GetString() ?? "";
            var nodes = way.GetProperty("geometry").EnumerateArray()
                .Select(n => (Lat: n.GetProperty("lat").GetDouble(), Lon: n.GetProperty("lon").GetDouble()))
                .ToList();

            for (int i = 0; i + 1 < nodes.Count; i++)
            {
                edges.Add(new NamedEdge(name, nodes[i].Lat, nodes[i].Lon, nodes[i + 1].Lat, nodes[i + 1].Lon));
            }
        }
        return edges;
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
