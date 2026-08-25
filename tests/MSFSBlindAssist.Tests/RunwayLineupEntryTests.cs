// Characterization tests for TaxiGraph.FindRunwayLineupEntryNode — the runway
// DESTINATION-node picker used when TaxiAssistForm populates the runway dropdown.
//
// Why it exists (LPPT 20, 2026-08-24): the lineup point comes from the navdata
// start row, which is trusted for where ALONG the runway the departure begins.
// Nothing guarantees a taxiway MEETS the runway there. LPPT 20's start row sits on
// a 1955 ft (596 m) displaced threshold ~619 m into the takeoff run, while the taxi
// network touches that centerline only at ~70 m (S3, the full-length end) and
// ~1396 m (U5/U6). A bare FindNearestNode therefore returned an S3 node 204 m away
// and 201 m OFF TO THE SIDE — abeam the runway, not on it — so the route dead-ended
// beside the runway and tripped "this route does not reach Runway 20".
//
// Fixture idiom (shared with BacktrackEntryTests / RunwayIntersectionTests): a
// synthetic east-west runway on the equator, where the code's equirectangular
// constant (111132 m/deg, cos(0)=1) makes along-track metres = degrees-of-longitude
// x 111132. The geometry below is LPPT 20 to scale.
//
// Pinned behaviors (see the method doc comment):
//   - the plain nearest node is returned UNCHANGED whenever it is within
//     maxAcceptableCrossM of the centerline (routes that work today are untouched)
//   - past that, a real runway ENTRANCE is chosen: on the pavement, on the runway
//     proper, with an off-runway neighbour, in the nearest node's component
//   - ONLY entrances AT OR BEHIND the lineup point are eligible, so the pilot is never
//     silently given less runway than the runway they selected
//   - an entrance BEHIND the runway_end pavement edge still counts (starter extensions
//     — iniBuilds EGLL 09L is entered only from AB13, ~300-355 m back)
//   - with no qualifying entrance at all — including when the only one is downfield —
//     the plain nearest node is returned, so the reach warning still fires
//
// Characterization, not spec: if a literal ever disagrees with real output, fix
// the test to match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayLineupEntryTests
{
    private const double FarLon = 0.027;          // ~3000 m runway
    private const double HalfWidthM = 22.5;       // LPPT: 148 ft wide -> maxPerp 27.5 m
    private const double MaxCrossM = 120.0;       // TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M

    private const double SpineLat = 0.00181;      // ~201 m north — the parallel taxiway
    private const double NearLon = 0.00063;       // entrance ~70 m along   (LPPT S3)
    private const double FarEntryLon = 0.01256;   // entrance ~1396 m along (LPPT U5/U6)
    private const double LineupLon = 0.00557;     // lineup point ~619 m along (displaced thr)

    /// <summary>
    /// A parallel taxiway 201 m north of the runway with connectors down onto the
    /// centerline at the requested along-track positions. With both connectors this
    /// is LPPT 20 to scale.
    /// </summary>
    private static TaxiGraph Build(bool nearEntrance, bool farEntrance)
    {
        var paths = new List<TaxiPath>
        {
            // Spine, split at the node abeam the lineup point (the node a bare
            // nearest-node search picks — 201 m off the centerline).
            new TaxiPath { StartLat = SpineLat, StartLon = NearLon,   EndLat = SpineLat, EndLon = LineupLon },
            new TaxiPath { StartLat = SpineLat, StartLon = LineupLon, EndLat = SpineLat, EndLon = FarEntryLon },
        };
        if (nearEntrance)
            paths.Add(new TaxiPath { StartLat = SpineLat, StartLon = NearLon, EndLat = 0, EndLon = NearLon });
        if (farEntrance)
            paths.Add(new TaxiPath { StartLat = SpineLat, StartLon = FarEntryLon, EndLat = 0, EndLon = FarEntryLon });

        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static TaxiNode? Entry(TaxiGraph g) =>
        g.FindRunwayLineupEntryNode(
            lineupLat: 0, lineupLon: LineupLon,
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, maxAcceptableCrossM: MaxCrossM);

    [Fact]
    public void Stranded_lineup_point_resolves_to_the_entrance_behind_it_not_the_node_abeam()
    {
        var g = Build(nearEntrance: true, farEntrance: true);

        // Sanity: this is the failure being fixed. The nearest node to the lineup
        // point is the spine node abeam it, 201 m off the centerline.
        var nearest = g.FindNearestNode(0, LineupLon);
        Assert.NotNull(nearest);
        Assert.Equal(SpineLat, nearest!.Latitude, 6);
        Assert.Equal(LineupLon, nearest.Longitude, 6);

        // Resolved: the full-length entrance ~70 m along, ON the centerline.
        var entry = Entry(g);
        Assert.NotNull(entry);
        Assert.Equal(0.0, entry!.Latitude, 6);
        Assert.Equal(NearLon, entry.Longitude, 6);
    }

    [Fact]
    public void Entrance_behind_the_lineup_point_beats_one_ahead()
    {
        // The 1396 m entrance is 777 m AHEAD of the lineup point; the 70 m entrance
        // is 549 m behind it. Behind must win even though it is further away.
        var g = Build(nearEntrance: true, farEntrance: true);
        var entry = Entry(g);

        Assert.NotNull(entry);
        Assert.Equal(NearLon, entry!.Longitude, 6);
    }

    [Fact]
    public void A_downfield_only_entrance_is_refused_and_the_warning_is_left_to_fire()
    {
        // With only the 1396 m entrance, the sole way onto the runway is 777 m PAST
        // the lineup point — an intersection departure. Substituting it would hand the
        // pilot a shorter takeoff run than the runway they selected, with nothing
        // spoken (measured over the whole fs2020 DB, allowing it moved 2,209 runway
        // ends, EGLL 09L by 755 m and EHAM 36R by 1,417 m). The plain nearest node is
        // returned instead so the reach warning fires and the pilot reprograms; the
        // intersection-departure checkbox is how that entrance is taken deliberately.
        var g = Build(nearEntrance: false, farEntrance: true);
        var entry = Entry(g);

        Assert.NotNull(entry);
        Assert.Equal(SpineLat, entry!.Latitude, 6);
        Assert.Equal(LineupLon, entry.Longitude, 6);
    }

    [Fact]
    public void An_entrance_behind_the_pavement_edge_still_counts_starter_extension()
    {
        // Starter extension: both the lineup point and the only entrance sit BEHIND the
        // runway_end pavement edge (negative along-track). iniBuilds EGLL 09L is entered
        // only from AB13, ~300-355 m back, so an along >= 0 floor would reject exactly
        // the entrance that runway depends on.
        const double BehindLon = -0.0027;   // ~-300 m along
        const double BehindLineupLon = -0.0023;
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = SpineLat, StartLon = BehindLon, EndLat = SpineLat, EndLon = LineupLon },
            new TaxiPath { StartLat = SpineLat, StartLon = BehindLon, EndLat = 0, EndLon = BehindLon },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var entry = g.FindRunwayLineupEntryNode(
            lineupLat: 0, lineupLon: BehindLineupLon,
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, maxAcceptableCrossM: MaxCrossM);

        Assert.NotNull(entry);
        Assert.Equal(0.0, entry!.Latitude, 6);
        Assert.Equal(BehindLon, entry.Longitude, 6);
    }

    [Fact]
    public void No_qualifying_entrance_returns_the_plain_nearest_node_so_the_warning_still_fires()
    {
        // A taxiway that merely PARALLELS the runway, with no connector onto it.
        // The node abeam must be returned unchanged so
        // TaxiGuidanceManager's reach check still warns, rather than the route being
        // silently retargeted somewhere arbitrary.
        var g = Build(nearEntrance: false, farEntrance: false);
        var entry = Entry(g);

        Assert.NotNull(entry);
        Assert.Equal(SpineLat, entry!.Latitude, 6);
        Assert.Equal(LineupLon, entry.Longitude, 6);
    }

    [Fact]
    public void A_nearest_node_within_the_cross_track_limit_is_returned_unchanged()
    {
        // The no-change guarantee. The lineup point here is the 70 m entrance
        // itself, whose nearest node is on the centerline (0 m cross-track), so the
        // entrance search must not run at all and the plain answer stands.
        var g = Build(nearEntrance: true, farEntrance: true);

        var entry = g.FindRunwayLineupEntryNode(
            lineupLat: 0, lineupLon: NearLon,
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, maxAcceptableCrossM: MaxCrossM);

        var nearest = g.FindNearestNode(0, NearLon);
        Assert.NotNull(entry);
        Assert.NotNull(nearest);
        Assert.Equal(nearest!.NodeId, entry!.NodeId);
    }

    [Fact]
    public void Reciprocal_direction_measures_along_track_from_its_own_threshold()
    {
        // Departing the other way (thr = the far end): the lineup point is now
        // ~2381 m along, the 1396 m entrance is 777 m BEHIND it and the 70 m one is
        // 549 m ahead — so the answer flips, i.e. the choice is direction-aware,
        // exactly as FindBacktrackEntryNode's is.
        var g = Build(nearEntrance: true, farEntrance: true);

        var entry = g.FindRunwayLineupEntryNode(
            lineupLat: 0, lineupLon: LineupLon,
            thrLat: 0, thrLon: FarLon, farLat: 0, farLon: 0,
            halfWidthMeters: HalfWidthM, maxAcceptableCrossM: MaxCrossM);

        Assert.NotNull(entry);
        Assert.Equal(0.0, entry!.Latitude, 6);
        Assert.Equal(FarEntryLon, entry.Longitude, 6);
    }
}
