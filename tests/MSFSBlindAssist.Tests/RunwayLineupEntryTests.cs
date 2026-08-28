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
        // Starter extension: the only entrance sits BEHIND the runway_end pavement edge
        // (negative along-track). iniBuilds EGLL 09L is entered only from AB13, ~300-355 m
        // back, so an along >= 0 floor would reject exactly the entrance that runway
        // depends on.
        //
        // The lineup point must be placed so the NEAREST node to it is the spine node, not
        // the entrance: otherwise plainPerp is 0, FindRunwayLineupEntryNode returns at its
        // `plainPerp <= maxAcceptableCrossM` early return, and the candidate loop —
        // MAX_BEHIND_THRESHOLD_M included — never runs at all. (The earlier fixture had the
        // entrance itself as the nearest node, so it passed with the constant set to zero.)
        var g = BuildStarterExtension(entranceLon: BehindEntranceLon);

        // Sanity: the scan is genuinely entered, i.e. the nearest node is 201 m off.
        var nearest = g.FindNearestNode(0, StarterLineupLon);
        Assert.NotNull(nearest);
        Assert.Equal(SpineLat, nearest!.Latitude, 6);

        var entry = StarterEntry(g);

        Assert.NotNull(entry);
        Assert.Equal(0.0, entry!.Latitude, 6);
        Assert.Equal(BehindEntranceLon, entry.Longitude, 6);
    }

    [Fact]
    public void An_entrance_further_back_than_the_starter_extension_budget_is_refused()
    {
        // ~-600 m, past MAX_BEHIND_THRESHOLD_M. Nothing qualifies, so the plain nearest node
        // is returned unchanged and TaxiGuidanceManager's honest reach warning still fires.
        // This is the assertion that gives the constant's MAGNITUDE regression cover — the
        // test above only proves the floor is not zero.
        var g = BuildStarterExtension(entranceLon: TooFarBehindLon);

        var entry = StarterEntry(g);

        Assert.NotNull(entry);
        Assert.Equal(SpineLat, entry!.Latitude, 6);   // the spine node, i.e. unchanged
    }

    private const double BehindEntranceLon = -0.0027;  // ~-300 m along (EGLL 09L via AB13)
    private const double TooFarBehindLon = -0.0054;    // ~-600 m along, past the 500 m budget
    private const double StarterLineupLon = 0.0001;    // ~11 m along — the lineup spot

    /// <summary>
    /// A spine 201 m north with a node abeam the lineup spot (so that node, not the
    /// entrance, is nearest to it) and a single connector down onto the centreline at
    /// <paramref name="entranceLon"/>, behind the pavement edge.
    /// </summary>
    private static TaxiGraph BuildStarterExtension(double entranceLon)
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = SpineLat, StartLon = entranceLon,      EndLat = SpineLat, EndLon = StarterLineupLon },
            new TaxiPath { StartLat = SpineLat, StartLon = StarterLineupLon, EndLat = SpineLat, EndLon = LineupLon },
            new TaxiPath { StartLat = SpineLat, StartLon = entranceLon,      EndLat = 0,        EndLon = entranceLon },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static TaxiNode? StarterEntry(TaxiGraph g) =>
        g.FindRunwayLineupEntryNode(
            lineupLat: 0, lineupLon: StarterLineupLon,
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, maxAcceptableCrossM: MaxCrossM);

    // ---- Reachability is anchored on the AIRCRAFT, not on the stranded node --------------
    //
    // The scan restricts candidates to one connected component, and the node it used to
    // anchor that on was `plain` — the very node the method exists because it is wrong. An
    // isolated taxi island is a documented shape in this navdata (GCLP S5), and an island
    // beside the lineup point is a plausible CAUSE of the >120 m nearest-node offset that
    // triggers the search in the first place. Anchored there, every real entrance is
    // filtered out and the island node is returned as the ROUTE DESTINATION — after which
    // LoadRoute filters its start-node candidates to the island's component too, and the
    // route begins where the aircraft is not. FindBacktrackEntryNode, whose scan this one
    // mirrors, has always anchored on the aircraft.

    private const double IslandLat = 0.00181;   // ~201 m north — an isolated stub
    private const double MainSpineLat = 0.0027; // ~300 m north — the real taxi network

    private static TaxiGraph BuildWithIslandBesideTheLineupPoint()
    {
        var paths = new List<TaxiPath>
        {
            // The real network: a spine with a connector onto the runway 70 m along.
            new TaxiPath { StartLat = MainSpineLat, StartLon = NearLon, EndLat = MainSpineLat, EndLon = LineupLon },
            new TaxiPath { StartLat = MainSpineLat, StartLon = NearLon, EndLat = 0,            EndLon = NearLon },
            // An isolated island, closer to the lineup point than the spine is.
            new TaxiPath { StartLat = IslandLat, StartLon = LineupLon, EndLat = IslandLat, EndLon = LineupLon + 0.0005 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    [Fact]
    public void An_island_beside_the_lineup_point_does_not_trap_the_entrance_search()
    {
        var g = BuildWithIslandBesideTheLineupPoint();

        // Sanity: the nearest node to the lineup point IS the island, and it is on a
        // different component from the real network.
        var nearest = g.FindNearestNode(0, LineupLon);
        Assert.NotNull(nearest);
        Assert.Equal(IslandLat, nearest!.Latitude, 6);
        var realEntrance = g.Nodes.Values.Single(n =>
            Math.Abs(n.Latitude) < 1e-9 && Math.Abs(n.Longitude - NearLon) < 1e-9);
        Assert.NotEqual(nearest.ComponentId, realEntrance.ComponentId);

        var entry = g.FindRunwayLineupEntryNode(
            lineupLat: 0, lineupLon: LineupLon,
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, maxAcceptableCrossM: MaxCrossM,
            aircraftLat: MainSpineLat, aircraftLon: LineupLon);

        Assert.NotNull(entry);
        Assert.Equal(0.0, entry!.Latitude, 6);
        Assert.Equal(NearLon, entry.Longitude, 6);
    }

    [Fact]
    public void Without_an_aircraft_position_the_entrance_search_is_unchanged()
    {
        // The form only knows where the aircraft is once SimConnect has reported it; until
        // then the anchor must stay what it was, not FindNearestNode(0, 0).
        var g = BuildWithIslandBesideTheLineupPoint();

        var entry = g.FindRunwayLineupEntryNode(
            lineupLat: 0, lineupLon: LineupLon,
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, maxAcceptableCrossM: MaxCrossM);

        Assert.NotNull(entry);
        Assert.Equal(IslandLat, entry!.Latitude, 6);   // the plain nearest node, unchanged
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

    // ---- Painted-holding-point projection nodes are not runway entrances ----------------
    //
    // NamedHoldingPointResolver.SnapOrInsert splits a live graph edge to plant a node on a
    // painted hold LINE, and TaxiAssistForm re-runs PopulateDestinations whenever the
    // destination type changes — so by the second visit to the Runway list the graph can
    // contain such nodes. A hold line sits close to the centreline by construction, so the
    // projection node passes the perpendicular filter and (via its far end on the spine)
    // HasOffRunwayNeighbour, and beats the real junction on along-track proximity. The
    // sibling scan in ResolveHoldingPointEntries has always skipped them, documenting
    // exactly this hazard; this scan picks the ROUTE DESTINATION, so it matters more here.

    private const double StubLon = 0.0036;      // a stub meeting the runway ~400 m along
    private const double StubTipLat = 0.00009;  // its tip ~10 m off the centreline
    private const double PaintedLat = 0.00018;  // the painted hold line ~20 m off

    private static TaxiGraph BuildWithPaintedHoldStub()
    {
        var paths = new List<TaxiPath>
        {
            // Spine 201 m north, split at every branch so the nodes exist.
            new TaxiPath { StartLat = SpineLat, StartLon = NearLon,   EndLat = SpineLat, EndLon = StubLon },
            new TaxiPath { StartLat = SpineLat, StartLon = StubLon,   EndLat = SpineLat, EndLon = LineupLon },
            // The real full-length entrance, ON the centreline 70 m along.
            new TaxiPath { StartLat = SpineLat, StartLon = NearLon,   EndLat = 0,          EndLon = NearLon },
            // A stub reaching down toward the runway ~400 m along but stopping short of it.
            new TaxiPath { StartLat = SpineLat, StartLon = StubLon,   EndLat = StubTipLat, EndLon = StubLon },
        };

        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var painted = g.InsertHoldingPointNodeOnEdge(PaintedLat, StubLon, maxPerpMeters: 5.0);
        Assert.NotNull(painted);
        Assert.True(g.IsHoldingPointProjectionNode(painted!.NodeId));
        return g;
    }

    [Fact]
    public void A_painted_holding_point_projection_node_is_never_the_runway_destination()
    {
        var g = BuildWithPaintedHoldStub();

        var entry = Entry(g);

        Assert.NotNull(entry);
        Assert.False(g.IsHoldingPointProjectionNode(entry!.NodeId));
        // The real entrance, on the centreline at the full-length end.
        Assert.Equal(0.0, entry.Latitude, 6);
        Assert.Equal(NearLon, entry.Longitude, 6);
    }
}
