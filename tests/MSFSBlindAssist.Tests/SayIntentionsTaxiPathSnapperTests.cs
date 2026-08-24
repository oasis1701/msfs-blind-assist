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
//   kdtw-taxipath-crossing.json — the current_flight.taxi_path array of the KDTW
//     capture of 2026-07-31, verbatim and whole (124 entries), for the trim below.
//     Same reduction rule as the LSZH one: nothing but the geometry. This one is
//     SayIntentions' own published track, not scenery data.
//   kdtw-taxiways.json — the same thing as lszh-taxiways.json and from the same
//     source: OSM, name + geometry only, no personal data, POLYLINE shape, split
//     into segments by LoadFixtureEdges. It is the whole probe output, unpruned —
//     the app's own query (way["aeroway"="taxiway"](around:5000,…), ref then name,
//     unnamed ways skipped) gives 116 named ways for KDTW, which is smaller than
//     the LSZH fixture already.
//
// WHICH SOURCE NAMES THE PAVEMENT CHANGES THE UNTRIMMED ANSWER, AND NOT THE TRIMMED
// ONE. Both were measured against this capture:
//
//     OSM (this fixture)   untrimmed  A5, A, R, K, Q, U9      trimmed  R, K, Q, U9
//     KDTW's own navdata   untrimmed      A, R, K, Q, U9      trimmed  R, K, Q, U9
//
// The navdata reading is what the shipped import logged on the day
// (geoTaxiways=[A,R,K,Q,U9]) — OSM additionally names the stub off the stand A5,
// where this airport's navdata leaves it unnamed. The property under test is the
// TRIMMED sequence, and it is identical either way: the trim is decided by where the
// aircraft is, not by who named the taxiway, so it does not depend on the provenance
// of the edges at all. That is what makes the OSM fixture a fair substitute, and it
// is worth more here than reproducing one log line exactly.

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

    // General version of OnA/OnB for tests that need edges other than the corner pair.
    private static GeoPoint PointOnEdge(NamedEdge edge, double fraction) => new(
        edge.FromLat + ((edge.ToLat - edge.FromLat) * fraction),
        edge.FromLon + ((edge.ToLon - edge.FromLon) * fraction));

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

        // DoesNotContain alone is vacuously true for an empty result, so this test
        // would still pass if Snap returned nothing at all. NotEmpty makes it stand on
        // its own: the stubs must be absent from a route that is otherwise present.
        Assert.NotEmpty(result.Taxiways);
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
        //
        // This is the only live test that would catch a tolerance change, but its
        // real guarantee is band-limited, not exact: sorted nearest-edge distances for
        // this fixture jump from 13.48 m to 41.51 m (measured directly via
        // PointToSegmentMetres over every point/edge pair), so ANY SnapToleranceMetres
        // in that gap — not just the shipped 25 m — reproduces UnsnappedCount == 4.
        // A tolerance change inside the gap will not fail here.
        var result = SayIntentionsTaxiPathSnapper.Snap(
            LoadFixturePath("lszh-taxipath-arrival.json"),
            LoadFixtureEdges("lszh-taxiways.json"));

        Assert.Equal(40, result.PointCount);
        Assert.Equal(4, result.UnsnappedCount);
        // "Link 6" x1, E x1, "Link 5" x1 — three non-null runs too short to report
        // (see the file header for the full run breakdown).
        Assert.Equal(3, result.DroppedRunCount);
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
    public void AGenuineShortLegUnderOneSampleIntervalIsReportedAsADroppedRun()
    {
        // SI resamples taxi_path at a near-fixed ~28 m step, so MinRunPoints = 2 means
        // a taxiway needs to hold roughly one full sample interval to be reported. A
        // real leg crossed in under that distance produces exactly one point on it —
        // same shape as a connector stub — and the run-length filter drops it. Unlike
        // a stub, though, this run's point genuinely snapped (it is not beyond
        // tolerance), so it never touches UnsnappedCount either: without
        // DroppedRunCount the caller is told every point was read and the route is
        // "A" then "C", with no signal that a real taxiway (B) was crossed and lost.
        var edgeA = new NamedEdge("A", 50.0000, 8.0000, 50.0010, 8.0000);
        var edgeB = new NamedEdge("B", 50.0010, 8.0000, 50.0010, 8.00041925); // ~30 m E-W connector
        var edgeC = new NamedEdge("C", 50.0010, 8.00041925, 50.0020, 8.00041925);
        var edges = new[] { edgeA, edgeB, edgeC };

        var path = new[]
        {
            PointOnEdge(edgeA, 0.1), PointOnEdge(edgeA, 0.3), PointOnEdge(edgeA, 0.5),
            PointOnEdge(edgeA, 0.7), PointOnEdge(edgeA, 0.9),
            PointOnEdge(edgeB, 0.5),
            PointOnEdge(edgeC, 0.1), PointOnEdge(edgeC, 0.3), PointOnEdge(edgeC, 0.5),
            PointOnEdge(edgeC, 0.7), PointOnEdge(edgeC, 0.9),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, edges);

        Assert.Equal(new[] { "A", "C" }, result.Taxiways);
        Assert.Equal(0, result.UnsnappedCount);
        Assert.Equal(1, result.DroppedRunCount);
    }

    [Fact]
    public void ATaxiwayRevisitedAfterAnotherIsKeptTwice()
    {
        // Non-consecutive reuse is real (a clearance crosses and comes back). Only
        // CONSECUTIVE duplicates collapse — for a run long enough to sit outside the
        // sandwiched-junction-excursion window (MaxExcursionRunPoints). A SHORT revisit
        // of a different taxiway between two runs of the same one is now exactly what
        // the excursion pass removes by design (see the sandwiched-excursion tests
        // above), so B needs more than MaxExcursionRunPoints (4) points here to keep
        // this test testing non-consecutive reuse rather than the excursion pass.
        var path = new[]
        {
            OnA(0.1), OnA(0.2),
            OnB(0.1), OnB(0.3), OnB(0.5), OnB(0.7), OnB(0.9),
            OnA(0.8), OnA(0.9),
        };

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

    // --- Sandwiched junction excursions -----------------------------------------------
    //
    // Near a junction the spur taxiway legitimately wins the nearest-edge scan for a few
    // consecutive points, so the sequence reads X, Y, X and the pilot is routed out onto
    // pavement the aircraft only crossed. Measured against real navdata: KORD A/A17 gives
    // a 2-point run, CYVR D/D5 a 3-point run, KDEN P/P7 a 4-point run (its centreline
    // stops 21.13 m short of P, inside the snap tolerance). Across 600 junctions at five
    // airports, 97.8 % of sandwiched excursions are 4 points or fewer.

    private static readonly NamedEdge EdgeMain = new("X", 50.0000, 8.0000, 50.0100, 8.0000);
    private static readonly NamedEdge EdgeSpur = new("Y", 50.0050, 8.0002, 50.0060, 8.0002);
    private static readonly NamedEdge EdgeOther = new("Z", 50.0070, 8.0002, 50.0080, 8.0002);
    private static readonly NamedEdge[] JunctionEdges = { EdgeMain, EdgeSpur, EdgeOther };

    /// <summary>A point on the main taxiway X, at the given latitude offset in units of
    /// 0.0001 degrees north of 50.0000.</summary>
    private static GeoPoint OnMain(int step) => new(50.0000 + (0.0001 * step), 8.0000);

    /// <summary>A point sitting exactly on the spur Y.</summary>
    private static GeoPoint OnSpur(int step) => new(50.0050 + (0.0001 * step), 8.0002);

    /// <summary>A point sitting exactly on the third taxiway Z.</summary>
    private static GeoPoint OnOther(int step) => new(50.0070 + (0.0001 * step), 8.0002);

    /// <summary>A point beyond every edge in <see cref="JunctionEdges"/>, so it snaps to
    /// nothing and breaks the run it lands in. 0.02 degrees of longitude is ~1.4 km at
    /// this latitude, far outside SnapToleranceMetres.</summary>
    private static GeoPoint OffGraph(int step) => new(50.0050 + (0.0001 * step), 8.0200);

    /// <summary>Three points along X, then <paramref name="spurPoints"/> on the spur Y,
    /// then three more along X — the X, Y, X shape every real case takes.</summary>
    private static GeoPoint[] MainSpurMain(int spurPoints)
    {
        var path = new List<GeoPoint> { OnMain(10), OnMain(20), OnMain(30) };
        for (int i = 1; i <= spurPoints; i++) path.Add(OnSpur(i));
        path.Add(OnMain(80));
        path.Add(OnMain(90));
        path.Add(OnMain(95));
        return path.ToArray();
    }

    [Theory]
    [InlineData(2)]  // KORD A, A17, A
    [InlineData(3)]  // CYVR D, D5, D
    [InlineData(4)]  // KDEN P, P7, P — the worst real case, exactly on the bound
    public void AnExcursionUpToTheBoundIsRemoved(int spurPoints)
    {
        var result = SayIntentionsTaxiPathSnapper.Snap(MainSpurMain(spurPoints), JunctionEdges);

        Assert.Equal(new[] { "X" }, result.Taxiways);
        Assert.Equal(1, result.ExcursionRunCount);
        Assert.Equal(0, result.DroppedRunCount);
    }

    [Fact]
    public void AFivePointExcursionIsKeptBecauseTheBoundIsExact()
    {
        // One point past MaxExcursionRunPoints. A track that holds a taxiway for five
        // samples (~140 m) is describing pavement, not clipping a junction.
        var result = SayIntentionsTaxiPathSnapper.Snap(MainSpurMain(5), JunctionEdges);

        Assert.Equal(new[] { "X", "Y", "X" }, result.Taxiways);
        Assert.Equal(0, result.ExcursionRunCount);
    }

    [Fact]
    public void AShortRunBetweenTwoDIFFERENTTaxiwaysIsNotAnExcursion()
    {
        // The sandwich is the whole discriminator: X, Y, Z is a route that genuinely
        // passes along three taxiways, however briefly it holds the middle one.
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OnSpur(1), OnSpur(2),
            OnOther(1), OnOther(2), OnOther(3),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "X", "Y", "Z" }, result.Taxiways);
        Assert.Equal(0, result.ExcursionRunCount);
    }

    [Fact]
    public void AShortLeadingRunIsKeptBecauseNothingPrecedesIt()
    {
        // A real track starts on the stand lead-in. With no run before it there is no
        // sandwich, so it is a leg like any other.
        var path = new[]
        {
            OnSpur(1), OnSpur(2),
            OnMain(80), OnMain(90), OnMain(95),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "Y", "X" }, result.Taxiways);
        Assert.Equal(0, result.ExcursionRunCount);
    }

    [Fact]
    public void AShortTrailingRunIsKeptBecauseNothingFollowsIt()
    {
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OnSpur(1), OnSpur(2),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "X", "Y" }, result.Taxiways);
        Assert.Equal(0, result.ExcursionRunCount);
    }

    [Fact]
    public void TwoExcursionsOffTheSameTaxiwayBothGoInOnePass()
    {
        // X, Y, X, Z, X. The second excursion is only visible once the first has been
        // removed, which is why the pass compares against the last run KEPT rather than
        // the immediately preceding one.
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OnSpur(1), OnSpur(2),
            OnMain(60), OnMain(65), OnMain(68),
            OnOther(1), OnOther(2),
            OnMain(90), OnMain(95), OnMain(98),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "X" }, result.Taxiways);
        Assert.Equal(2, result.ExcursionRunCount);
    }

    [Fact]
    public void AnExcursionIsMeasuredAgainstTheLastRunKEPTNotTheOneImmediatelyBefore()
    {
        // X, Y, X, Y, Z, with the middle X short. This is the ONLY shape that separates
        // the two rules, and the counter is the witness: under kept[^1] the second Y is
        // measured against the X still standing and survives (exc 1); under
        // surviving[i-1] the middle X is measured against the Y that was just dropped,
        // so the ANCHOR is deleted too (exc 2). Both rules yield X, Y, Z, so the taxiway
        // list cannot tell them apart.
        //
        // This test used to run X, Y, X, Z, X and claim the same property. It does not
        // hold there: when the loop reaches Z the entry before it in `surviving` is
        // already X under either rule, so Y's fate never enters the comparison. Verified
        // by mutation — the old fixture passed with surviving[i-1] substituted, this one
        // does not. TwoExcursionsOffTheSameTaxiwayBothGoInOnePass still covers X,Y,X,Z,X
        // for the single-pass property, which is what that shape does pin.
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OnSpur(1), OnSpur(2),
            OnMain(60), OnMain(65),
            OnSpur(5), OnSpur(6),
            OnOther(1), OnOther(2), OnOther(3),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "X", "Y", "Z" }, result.Taxiways);
        Assert.Equal(1, result.ExcursionRunCount);
        Assert.Equal(new[] { "Y" }, result.ExcursionTaxiways);
        Assert.Equal(5, result.PreExcursionTaxiwayCount);
    }

    [Fact]
    public void AStubDroppedBetweenTwoPassesOfOneTaxiwayIsNotCountedAsAnExcursion()
    {
        // The stub filter runs first and can leave two same-named runs adjacent. That is
        // one leg, not an excursion — DroppedRunCount already accounts for the stub.
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OnSpur(1),
            OnMain(80), OnMain(90), OnMain(95),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "X" }, result.Taxiways);
        Assert.Equal(1, result.DroppedRunCount);
        Assert.Equal(0, result.ExcursionRunCount);
    }

    [Fact]
    public void ThePreExcursionCountReportsTheTrackBeforeExcursionsWereRemoved()
    {
        // X, Y, X collapses to a single applied leg, but the track as published was three.
        var result = SayIntentionsTaxiPathSnapper.Snap(MainSpurMain(3), JunctionEdges);

        Assert.Equal(new[] { "X" }, result.Taxiways);
        Assert.Equal(3, result.PreExcursionTaxiwayCount);
    }

    [Fact]
    public void ThePreExcursionCountEqualsTheAppliedCountWhenNothingWasRemoved()
    {
        var result = SayIntentionsTaxiPathSnapper.Snap(MainSpurMain(5), JunctionEdges);

        Assert.Equal(new[] { "X", "Y", "X" }, result.Taxiways);
        Assert.Equal(3, result.PreExcursionTaxiwayCount);
        Assert.Equal(0, result.ExcursionRunCount);
    }


    // --- The sandwich has to be CONTIGUOUS in the published track --------------------

    [Fact]
    public void AnExcursionReachedAcrossLostPointsIsNotASandwich()
    {
        // X, [off-graph], Y, [off-graph], X. The track never read X -> Y -> X: two runs
        // of points beyond every taxiway sit between them, so nothing says the aircraft
        // went straight from X onto Y and straight back. Y is a leg it really flew.
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OffGraph(0),
            OnSpur(1), OnSpur(2),
            OffGraph(9),
            OnMain(80), OnMain(90), OnMain(95),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "X", "Y", "X" }, result.Taxiways);
        Assert.Equal(0, result.ExcursionRunCount);
        Assert.Equal(2, result.UnsnappedCount);
    }

    [Fact]
    public void AnExcursionSeparatedFromItsAnchorByADroppedStubIsNotASandwich()
    {
        // X, Y, Z(1 point), X. This is the TWO-SPUR shape, not the sandwich one: the
        // track left X, touched two different taxiways and came back. That Z was short
        // enough for the stub filter to remove must not turn it into a sandwich —
        // AShortRunBetweenTwoDIFFERENTTaxiwaysIsNotAnExcursion pins the same shape when
        // Z is long enough to survive, and the two must agree.
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OnSpur(1), OnSpur(2),
            OnOther(1),
            OnMain(80), OnMain(90), OnMain(95),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "X", "Y", "X" }, result.Taxiways);
        Assert.Equal(0, result.ExcursionRunCount);
        Assert.Equal(1, result.DroppedRunCount);
    }


    // --- What the filter removed has to be recoverable from the log -------------------

    [Fact]
    public void TheNamesDroppedAsExcursionsAreReported()
    {
        // A count alone cannot say WHICH leg went, and the count is not even a leg
        // delta: removing the filling also merges the two anchors, so
        // Taxiways.Count + ExcursionRunCount does not add back up to the published
        // length. The names are the only thing that can settle "was the cleared
        // taxiway the one deleted?" from sayintentions.log.
        var result = SayIntentionsTaxiPathSnapper.Snap(MainSpurMain(3), JunctionEdges);

        Assert.Equal(new[] { "Y" }, result.ExcursionTaxiways);
    }

    [Fact]
    public void EveryDroppedExcursionIsNamedInOrder()
    {
        var path = new[]
        {
            OnMain(10), OnMain(20), OnMain(30),
            OnSpur(1), OnSpur(2),
            OnMain(60), OnMain(65), OnMain(68),
            OnOther(1), OnOther(2),
            OnMain(90), OnMain(95), OnMain(98),
        };

        var result = SayIntentionsTaxiPathSnapper.Snap(path, JunctionEdges);

        Assert.Equal(new[] { "Y", "Z" }, result.ExcursionTaxiways);
        Assert.Equal(2, result.ExcursionRunCount);
    }

    [Fact]
    public void NothingIsNamedWhenNoExcursionWasRemoved()
    {
        var result = SayIntentionsTaxiPathSnapper.Snap(MainSpurMain(5), JunctionEdges);

        Assert.Empty(result.ExcursionTaxiways);
    }


    // --- KNOWN LIMITATION: the trim can take the sandwich's leading anchor -------------

    [Fact]
    public void AnExcursionWhoseLeadingAnchorTheTrimRemovedIsNotRecognised()
    {
        // Pins CURRENT behaviour, not desired behaviour. TrimToPointsAhead runs BEFORE
        // Snap and cuts at the published point nearest the aircraft. An excursion sits
        // inside SnapToleranceMetres of the taxiway being travelled by definition, so an
        // aircraft standing at the junction - which is where a pilot presses Ctrl+Shift+Y
        // after a hold-short crossing - makes the trim remove the leading X. With nothing
        // before it the spur cannot be a sandwich, and it becomes the route's FIRST leg:
        // the one the summary speaks and the one guidance steers at.
        //
        // Not introduced by the excursion pass - the same list came out before it existed
        // - but the pass does not cover it, and nothing else composed the two. Recorded in
        // docs/sayintentions.md. If a future change fixes it, this test SHOULD fail.
        var wholeTrack = MainSpurMain(3);
        var aircraft = OnSpur(1);

        var ahead = SayIntentionsTaxiPathSnapper.TrimToPointsAhead(
            wholeTrack, aircraft.Latitude, aircraft.Longitude);
        var result = SayIntentionsTaxiPathSnapper.Snap(ahead, JunctionEdges);

        Assert.Equal(new[] { "Y", "X" }, result.Taxiways);
        Assert.Equal(0, result.ExcursionRunCount);

        // Standing one sample earlier, the anchor survives the trim and the pass works.
        var earlier = OnMain(20);
        var aheadEarlier = SayIntentionsTaxiPathSnapper.TrimToPointsAhead(
            wholeTrack, earlier.Latitude, earlier.Longitude);
        var fromEarlier = SayIntentionsTaxiPathSnapper.Snap(aheadEarlier, JunctionEdges);

        Assert.Equal(new[] { "X" }, fromEarlier.Taxiways);
        Assert.Equal(1, fromEarlier.ExcursionRunCount);
    }

    // ---- TrimToPointsAhead: the published track is not always what is LEFT of the route ----
    //
    // KDTW, live, 2026-07-31. Holding short of runway 4R, cleared to cross and continue
    // ("cross-runway 4R, then continue taxi via K, Q"), the pilot pressed Ctrl+Shift+Y and
    // got a route down A5, A and R — pavement already behind the aircraft. taxi_path had
    // been documented as the REMAINING route on the strength of one capture that shrank
    // 77 -> 40 points; this one is the route as ISSUED, with 61 % of it already flown.

    // Verbatim from the capture's flight_details.coordinates, heading 121.
    private const double KdtwAircraftLatitude = 42.2076316561171;
    private const double KdtwAircraftLongitude = -83.3676542345143;

    [Fact]
    public void TheLiveKdtwTrackIsMostlyBehindTheAircraft()
    {
        var path = LoadFixturePath("kdtw-taxipath-crossing.json");

        var ahead = SayIntentionsTaxiPathSnapper.TrimToPointsAhead(
            path, KdtwAircraftLatitude, KdtwAircraftLongitude);

        // 124 published, the nearest point index 76 at 7.4 m, so 76 points — 61 % — sit
        // behind the aircraft and 48 remain.
        Assert.Equal(124, path.Count);
        Assert.Equal(48, ahead.Count);
        Assert.Equal(path[76], ahead[0]);
        Assert.Equal(7.4, MetresFromKdtwAircraft(path[76]), 1);

        // ...and the far end of what was trimmed is not marginal: the FIRST published
        // point is a kilometre and a half back down the taxiway.
        Assert.Equal(1509.9, MetresFromKdtwAircraft(path[0]), 1);
    }

    [Fact]
    public void TheLiveKdtwTrackSnappedWholeNamesTaxiwaysAlreadyLeft()
    {
        // The shipped behaviour: the route opens on A5 and A, the pavement the aircraft
        // came in on and left. (Against KDTW's own navdata the same track reads
        // A, R, K, Q, U9 — what sayintentions.log recorded on the day. See the file
        // header: the leading legs are where the two sources differ, and they are exactly
        // the legs the trim removes.)
        var result = SayIntentionsTaxiPathSnapper.Snap(
            LoadFixturePath("kdtw-taxipath-crossing.json"),
            LoadFixtureEdges("kdtw-taxiways.json"));

        Assert.Equal(new[] { "A5", "A", "R", "K", "Q", "U9" }, result.Taxiways);
    }

    [Fact]
    public void TheLiveKdtwTrackTrimmedDropsTheTaxiwaysAlreadyLeft()
    {
        var result = SayIntentionsTaxiPathSnapper.Snap(
            SayIntentionsTaxiPathSnapper.TrimToPointsAhead(
                LoadFixturePath("kdtw-taxipath-crossing.json"),
                KdtwAircraftLatitude, KdtwAircraftLongitude),
            LoadFixtureEdges("kdtw-taxiways.json"));

        // A5 and A are gone — up to 1.5 km behind — and K and Q, the two taxiways the
        // controller actually named, survive with the stand lead-in after them.
        //
        // R STAYS, and that is correct rather than a leftover: the aircraft is standing
        // ON R, 2.1 m from its centreline and 21 m from runway 4R's, with about 220 m of
        // R still to run past the crossing before K. What is behind the aircraft is
        // decided by where the aircraft is, not by which leg it started.
        Assert.Equal(new[] { "R", "K", "Q", "U9" }, result.Taxiways);
    }

    [Fact]
    public void AnAircraftNowhereNearTheTrackLeavesItUntouched()
    {
        // If the aircraft is not on the published track — towed, repositioned, or the
        // track is for somewhere else — nothing here can say which part of it is behind,
        // and a wrong trim silently deletes legs the pilot was cleared for. The path
        // comes back as it went in, the same instance.
        var path = new[] { OnA(0.2), OnA(0.4), OnA(0.6) };

        Assert.Same(path, SayIntentionsTaxiPathSnapper.TrimToPointsAhead(path, 50.0003, 8.0100));
    }

    [Fact]
    public void TheGuardIsTheSameLineTheSnapDraws()
    {
        // 25 m: the snapper's own "on pavement or not". Abeam the middle point, 0.0004 deg
        // of longitude at 50N is 28.6 m and is left alone; 0.0003 deg is 21.5 m and trims.
        var path = new[] { OnA(0.2), OnA(0.4), OnA(0.6) };

        Assert.Same(path, SayIntentionsTaxiPathSnapper.TrimToPointsAhead(path, 50.0004, 8.0004));
        Assert.Equal(2, SayIntentionsTaxiPathSnapper.TrimToPointsAhead(path, 50.0004, 8.0003).Count);
    }

    [Fact]
    public void AnAircraftAtTheStartOfTheTrackTrimsNothing()
    {
        // The ordinary case: the pilot presses before moving, so the whole published
        // track is still ahead.
        var path = new[] { OnA(0.2), OnA(0.4), OnA(0.6) };

        Assert.Same(path, SayIntentionsTaxiPathSnapper.TrimToPointsAhead(path, 50.0002, 8.0000));
    }

    [Fact]
    public void AnExactTieTrimsToTheEarlierPoint()
    {
        // A route that doubles back past the aircraft touches the same pavement twice, so
        // two points sit at an identical distance. Keeping the earlier one keeps the whole
        // of the second pass; breaking the tie the other way would delete it.
        var path = new[] { OnA(0.1), OnA(0.5), OnA(0.9), OnA(0.5) };

        var ahead = SayIntentionsTaxiPathSnapper.TrimToPointsAhead(path, 50.0005, 8.0000);

        Assert.Equal(3, ahead.Count);
        Assert.Equal(OnA(0.5), ahead[0]);
    }

    [Fact]
    public void AnEmptyPathTrimsToNothingRatherThanThrowing()
    {
        // Same contract as Snap: the caller is a hotkey a blind pilot presses mid-taxi.
        Assert.Empty(SayIntentionsTaxiPathSnapper.TrimToPointsAhead(
            Array.Empty<GeoPoint>(), KdtwAircraftLatitude, KdtwAircraftLongitude));
    }

    private static double MetresFromKdtwAircraft(GeoPoint point) =>
        SayIntentionsTaxiPathSnapper.PointToSegmentMetres(
            KdtwAircraftLatitude, KdtwAircraftLongitude,
            point.Latitude, point.Longitude, point.Latitude, point.Longitude);

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
