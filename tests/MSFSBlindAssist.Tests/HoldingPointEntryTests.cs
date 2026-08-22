// Characterization tests for TaxiGraph.ResolveHoldingPointEntries — the geometric,
// NAME-INDEPENDENT resolver that maps PAINTED holding-point names (OSM
// aeroway=holding_position refs, e.g. LSZH "A2") to runway entry nodes for the Taxi
// Assist "Depart from named holding point" option. Built for sceneries whose entry
// stub taxiways are UNNAMED in navdata (MK Studios LSZH: navNamed=0), where the
// name-keyed GetRunwayIntersections can never list those entries.
//
// Fixture idiom (shared with BacktrackEntryTests / RunwayIntersectionTests): a
// synthetic east-west runway on the equator (thresholds at (0,0) and (0,0.027)),
// where the code's equirectangular constant (111132 m/deg, cos(0)=1) makes
// along-track distances exactly metres = degrees-of-longitude x 111132.
//
// Pinned behaviors (see ResolveHoldingPointEntries' doc comment):
//   - a point binds to the runway only within maxPointPerpMeters (200 m) of the
//     centerline; its entry is the nearest same-component on-runway junction node
//     within 200 m of the point. The point gate is deliberately NOT the 150 m
//     HOLDSHORT_RUNWAY_MATCH_M used to name hold-short nodes — a set-back CAT II/III
//     hold (EGKK A3, 162 m out) is a legitimate departure entry and was being dropped
//   - a point whose NAME is a runway designator ("08L/26R" — how OSM labels a
//     runway-crossing hold line) is not a taxiway holding point and is skipped
//   - full-length entries ARE included (unlike GetRunwayIntersections) — choosing
//     the full-length stub by its painted name is the feature's use case
//   - the painted point only SELECTS the entry: the returned Latitude/Longitude is
//     the entry NODE projected onto the centerline, never the painted point itself
//   - unreachable (other-component) entries are skipped; same-name points at the
//     same entry dedupe; results sort by distance from the departure threshold
//
// Characterization, not spec: if a literal ever disagrees with real output, fix
// the test to match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HoldingPointEntryTests
{
    private const double FarLon = 0.027;
    private const double HalfWidthM = 30.0;   // maxPerp = 35 m
    private const double OffLat = 0.0006;     // ~66.7 m north — off the runway

    // Same shape as the backtrack fixture: an off-runway spine (one component)
    // links runway entrances at 667 m / 1334 m / 2000 m from the 09 threshold,
    // plus an ISOLATED entrance at 333 m in its own component.
    private static TaxiGraph BuildGraph()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = OffLat, EndLon = 0.012 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.012, EndLat = OffLat, EndLon = 0.018 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = 0, EndLon = 0.006 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.012, EndLat = 0, EndLon = 0.012 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.018, EndLat = 0, EndLon = 0.018 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.003, EndLat = 0, EndLon = 0.003 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    // Aircraft parked on the spine.
    private const double AcLat = OffLat;
    private const double AcLon = 0.006;

    // A painted hold line sits ~44 m back from the centerline on its stub.
    private const double HoldLat = 0.0004;

    [Fact]
    public void Resolves_named_points_to_their_entries_sorted_by_along_track()
    {
        var g = BuildGraph();
        var points = new List<(string, double, double)>
        {
            ("A2", HoldLat, 0.012),   // deliberately out of along-track order
            ("A1", HoldLat, 0.006),
        };

        var entries = g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon);

        Assert.Equal(2, entries.Count);
        Assert.Equal("A1", entries[0].TaxiwayName);
        Assert.Equal(0.006, entries[0].Longitude, 6);
        Assert.Equal(0.0, entries[0].Latitude, 6);      // entry node ON the centerline
        Assert.Equal("A2", entries[1].TaxiwayName);
        Assert.Equal(0.012, entries[1].Longitude, 6);
        Assert.True(entries[0].AlongMetersFromThreshold < entries[1].AlongMetersFromThreshold);
        Assert.True(entries[1].RemainingMeters > 1000);
    }

    [Fact]
    public void Point_far_from_the_centerline_does_not_bind_to_this_runway()
    {
        var g = BuildGraph();
        // ~222 m north of the centerline — beyond the 200 m point gate: that's another
        // runway's (or an unrelated) hold line, not ours. The node gate is widened here so
        // only the PERPENDICULAR test can be what rejects it; the two default to the same
        // 200 m and would otherwise both fire on a point taken abeam its entrance.
        var points = new List<(string, double, double)> { ("Z9", 0.002, 0.012) };

        var entries = g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon, maxNodeDistMeters: 400.0);

        Assert.Empty(entries);
    }

    [Fact]
    public void Unreachable_isolated_entry_is_skipped()
    {
        var g = BuildGraph();
        // Painted point at the ISOLATED 333 m entrance. Its own entry node is in
        // another component; the nearest reachable entrance (667 m) is ~334 m from
        // the point — beyond the 200 m node gate — so the point yields nothing
        // rather than silently borrowing a different entry.
        var points = new List<(string, double, double)> { ("B1", HoldLat, 0.003) };

        var entries = g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon);

        Assert.Empty(entries);
    }

    [Fact]
    public void A_hold_line_named_after_a_runway_is_not_offered_as_a_departure_point()
    {
        var g = BuildGraph();
        // OSM labels the hold line where a taxiway crosses a runway with the RUNWAY's
        // name. Sitting on a valid entrance doesn't make it something ATC clears you from.
        var points = new List<(string, double, double)>
        {
            ("08L/26R", HoldLat, 0.012),
            ("A2", HoldLat, 0.012),
        };

        var entry = Assert.Single(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon));
        Assert.Equal("A2", entry.TaxiwayName);
    }

    [Fact]
    public void Same_name_at_the_same_entry_dedupes()
    {
        var g = BuildGraph();
        // Two "A1" points (e.g. the same hold line mapped as two OSM nodes across
        // a dual carriageway stub) both nearest to the 0.006 entrance.
        var points = new List<(string, double, double)>
        {
            ("A1", HoldLat, 0.0059),
            ("A1", HoldLat, 0.0061),
        };

        var entries = g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon);

        Assert.Single(entries);
        Assert.Equal("A1", entries[0].TaxiwayName);
    }

    [Fact]
    public void Reciprocal_direction_measures_along_track_from_the_other_threshold()
    {
        var g = BuildGraph();
        var points = new List<(string, double, double)> { ("A2", HoldLat, 0.012) };

        var entries = g.ResolveHoldingPointEntries(
            points, 0, FarLon, 0, 0, HalfWidthM, AcLat, AcLon);

        Assert.Single(entries);
        // From the 27 end the 0.012 entrance is 0.015 deg away (~1667 m).
        Assert.Equal(0.015 * 111132.0, entries[0].AlongMetersFromThreshold, 0);
    }

    // The caller anchors thr on the DEPARTURE lineup point (the `start` table row), so a
    // FULL-LENGTH entry stub meets the runway right at it — sometimes a few metres behind,
    // since the lineup point is where the nose sits while the stub joins the pavement abeam
    // or short of that. The old 5 m along-track floor filtered that junction out, and the
    // full-length holding points then snapped to the NEXT entrance down the runway: at EGKK
    // 26L, M1/M3 resolved to the A entrance 112 m in, so picking "M1" silently routed you to
    // a different stub. These pin the −40 m floor that keeps the junction itself eligible.
    private static TaxiGraph BuildGraphWithFullLengthEntry()
    {
        var paths = new List<TaxiPath>
        {
            // Spine running back from behind the threshold to the 667 m entrance. Its
            // west end IS the full-length stub's off-runway end, so the two share a node.
            new TaxiPath { StartLat = OffLat, StartLon = -0.0002, EndLat = OffLat, EndLon = 0.006 },
            // Full-length stub, meeting the runway 22 m BEHIND the lineup point.
            new TaxiPath { StartLat = OffLat, StartLon = -0.0002, EndLat = 0, EndLon = -0.0002 },
            // A mid-field entrance at 667 m, as in the main fixture.
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = 0, EndLon = 0.006 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    [Fact]
    public void Full_length_entry_just_behind_the_lineup_point_is_still_offered()
    {
        var g = BuildGraphWithFullLengthEntry();
        // Painted line on the full-length stub, ~6 m behind the lineup point.
        var points = new List<(string, double, double)> { ("M1", HoldLat, -0.00005) };

        var entries = g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006);

        var entry = Assert.Single(entries);
        Assert.Equal("M1", entry.TaxiwayName);
        // It bound to the full-length junction, NOT the 667 m entrance downfield.
        Assert.True(entry.AlongMetersFromThreshold < 0,
            $"expected the junction behind the lineup point, got {entry.AlongMetersFromThreshold:F0} m");
        Assert.True(entry.RemainingMeters > FarLon * 111132.0);
    }

    [Fact]
    public void An_entry_far_behind_the_lineup_point_is_still_excluded()
    {
        // Same graph, but a painted point 100 m back — off the departure end entirely
        // (EGKK's W1 sits there). Without the behind-threshold opt-in (no kind data),
        // the point gate drops it before any node is considered.
        var g = BuildGraphWithFullLengthEntry();
        var points = new List<(string, double, double)> { ("W1", HoldLat, -0.0009) };

        Assert.Empty(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006));
    }

    // ── Behind-threshold holds (EGCC 23L: VB1 73 m / T1 430 m behind the pavement) ──
    // Some airports paint their full-length departure holds on the lead-in taxiways of
    // a holding area BEHIND the threshold. Admission is opt-in (the caller vouches the
    // point's OSM kind GUARDS a runway) and triple-gated; these pin each gate.

    private static bool RunwayKind(string _) => true;

    [Fact]
    public void Behind_threshold_runway_kind_hold_binds_to_the_full_length_entry()
    {
        var g = BuildGraphWithFullLengthEntry();
        // The same 100 m-back point the previous test drops — WITH the opt-in it
        // resolves, and to the full-length junction, never a downfield entrance.
        var points = new List<(string, double, double)> { ("VB1", HoldLat, -0.0009) };

        var entry = Assert.Single(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind, runwayName: "09"));
        Assert.Equal("VB1", entry.TaxiwayName);
        Assert.True(entry.AlongMetersFromThreshold < 0,
            $"expected the full-length junction, got {entry.AlongMetersFromThreshold:F0} m");
        Assert.True(entry.RemainingMeters > FarLon * 111132.0);
    }

    [Fact]
    public void Behind_threshold_hold_needs_both_the_kind_vouch_and_the_runway_name()
    {
        var g = BuildGraphWithFullLengthEntry();
        var points = new List<(string, double, double)> { ("VB1", HoldLat, -0.0009) };

        // Kind callback present but says "not a runway-guarding line" (EGCC V4,
        // holding_position:type=intermediate) — excluded.
        Assert.Empty(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: _ => false, runwayName: "09"));
        // No runway name — the other-runway ownership guard can't tell self apart,
        // so admission stays off (safe default).
        Assert.Empty(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind));
    }

    [Fact]
    public void Behind_threshold_hold_beyond_a_kilometre_is_excluded()
    {
        var g = BuildGraphWithFullLengthEntry();
        // 1111 m back — outside MAX_BEHIND_ALONG_M even with every vouch present.
        var points = new List<(string, double, double)> { ("VB1", HoldLat, -0.01) };

        Assert.Empty(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind, runwayName: "09"));
    }

    [Fact]
    public void Behind_threshold_hold_claimed_by_a_nearer_parallel_runway_is_excluded()
    {
        // The main fixture's graph plus a PARALLEL runway centerline 22 m north of the
        // painted point (the point is 44 m from OUR centerline). Reciprocal-heading
        // start rows pair into a RunwayCenterline named 09L/27R.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = OffLat, StartLon = -0.0002, EndLat = OffLat, EndLon = 0.006 },
            new TaxiPath { StartLat = OffLat, StartLon = -0.0002, EndLat = 0, EndLon = -0.0002 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = 0, EndLon = 0.006 },
        };
        var starts = new List<StartPosition>
        {
            new() { RunwayName = "09L", Type = "R", Heading = 90, Latitude = OffLat, Longitude = -0.001 },
            new() { RunwayName = "27R", Type = "R", Heading = 270, Latitude = OffLat, Longitude = FarLon },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts);
        Assert.NotEmpty(g.RunwayCenterlines);

        var points = new List<(string, double, double)> { ("VB1", HoldLat, -0.0009) };

        // Resolving runway "09" (not 09L): the parallel's centerline is closer to the
        // point than ours, so the point is ITS hold — excluded from our list…
        Assert.Empty(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind, runwayName: "09"));
        // …while resolving AS 09L (the centerline is self, excluded by name) the same
        // point resolves fine.
        Assert.Single(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind, runwayName: "09L"));
    }

    [Fact]
    public void Behind_threshold_hold_in_another_runways_own_behind_band_is_still_claimed()
    {
        // LEMD Y-1: a point behind BOTH runways' thresholds. The parallel starts 667 m
        // east, so the point (100 m behind OUR threshold) is ~767 m behind the
        // parallel's — inside its behind-threshold BAND but outside its span. The claim
        // window equals the admission window, so the nearer parallel still owns it.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = OffLat, StartLon = -0.0002, EndLat = OffLat, EndLon = 0.006 },
            new TaxiPath { StartLat = OffLat, StartLon = -0.0002, EndLat = 0, EndLon = -0.0002 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = 0, EndLon = 0.006 },
        };
        var starts = new List<StartPosition>
        {
            new() { RunwayName = "09L", Type = "R", Heading = 90, Latitude = 0.0005, Longitude = 0.006 },
            new() { RunwayName = "27R", Type = "R", Heading = 270, Latitude = 0.0005, Longitude = 0.033 },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts);
        Assert.NotEmpty(g.RunwayCenterlines);

        // 44 m from our centerline, 11 m from the parallel's — the parallel wins.
        var points = new List<(string, double, double)> { ("Y1", HoldLat, -0.0009) };

        Assert.Empty(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind, runwayName: "09"));
    }

    [Fact]
    public void Behind_threshold_hold_can_only_bind_toward_the_threshold()
    {
        // A graph with ONLY a downfield entrance (667 m in) — no full-length stub. A
        // point 100 m back may not reach past the threshold to grab it, even though
        // 767 m would be within a naive distance-scaled cap.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = OffLat, StartLon = -0.0009, EndLat = OffLat, EndLon = 0.006 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = 0, EndLon = 0.006 },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var points = new List<(string, double, double)> { ("VB1", HoldLat, -0.0009) };

        Assert.Empty(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind, runwayName: "09"));
    }

    [Fact]
    public void Behind_threshold_holds_sharing_the_entry_sort_nearest_line_first()
    {
        var g = BuildGraphWithFullLengthEntry();
        // T1 far back, VB1 near — both bind to the SAME full-length junction and tie
        // on along-track. The line nearest the runway sorts first: it is what
        // AnnounceDefaultHoldingPoint names as the normal full-length clearance.
        var points = new List<(string, double, double)>
        {
            ("T1", HoldLat, -0.0035),
            ("VB1", HoldLat, -0.0006),
        };

        var entries = g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, OffLat, 0.006,
            behindThresholdEligible: RunwayKind, runwayName: "09");

        Assert.Equal(2, entries.Count);
        Assert.Equal(entries[0].NodeId, entries[1].NodeId);
        Assert.Equal("VB1", entries[0].TaxiwayName);
        Assert.Equal("T1", entries[1].TaxiwayName);
    }

    // ── HoldNodeId: the painted LINE's own node, not the runway entry ───────────────
    // Routing pins the route through this node so the pilot taxis up the stub they named.
    // EGLL 27R, 2026-08-08: picking A2 fixed only the runway ENTRY, and since A2 and A3
    // merge just short of the runway, A* ran the pilot up A3 and rejoined A2 for the last
    // 60 m — so the aircraft crossed A3's painted line, and the hold-short (the LAST hold
    // node on the route) was correctly announced as "A3" against the pilot's A2 choice.

    [Fact]
    public void Entry_carries_the_painted_lines_own_node_for_the_route_pin()
    {
        var g = BuildGraph();
        var points = new List<(string, double, double)> { ("A1", HoldLat, 0.006) };

        var entry = Assert.Single(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon));

        // The painted line sits 44 m off the centerline on the stub; the nearest graph node
        // is the stub's off-runway end (22 m away, inside MAX_SNAP_M), NOT the entry itself.
        var stubNode = g.FindNearestNode(OffLat, 0.006);
        Assert.NotNull(stubNode);
        Assert.Equal(stubNode!.NodeId, entry.HoldNodeId);
        Assert.NotEqual(entry.NodeId, entry.HoldNodeId);
    }

    [Fact]
    public void Hold_node_is_projected_onto_the_stub_when_the_line_falls_between_its_nodes()
    {
        var g = BuildGraph();
        // Dead centre between the stub's two nodes: 33 m from each, beyond the resolver's
        // 30 m MAX_SNAP_M — but exactly ON the stub. Since the edge-projection fallback
        // (the EGLL LOMAN case) this pins to a node inserted at the paint instead of
        // degrading to a free-choice corridor.
        var points = new List<(string, double, double)> { ("A1", 0.0003, 0.006) };

        var entry = Assert.Single(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon));

        Assert.NotEqual(0, entry.HoldNodeId);
        Assert.NotEqual(0, entry.NodeId);
        Assert.NotEqual(entry.NodeId, entry.HoldNodeId);
        // The pin is the painted line's own position on the stub…
        var pin = g.Nodes[entry.HoldNodeId];
        Assert.True(g.IsHoldingPointProjectionNode(pin.NodeId));
        Assert.Equal(0.0003, pin.Latitude, 6);
        Assert.Equal(0.006, pin.Longitude, 6);
        // …and the ENTRY is still the real runway junction, not the inserted node.
        Assert.Equal(0.0, g.Nodes[entry.NodeId].Latitude, 6);
    }

    [Fact]
    public void Hold_node_is_zero_when_the_painted_line_is_near_neither_a_node_nor_pavement()
    {
        var g = BuildGraph();
        // 167 m east of the stub and 33 m south of the spine: outside MAX_SNAP_M and
        // outside the edge projection. No pin — the route falls back to today's
        // free-choice corridor rather than pinning to a node that isn't this line's.
        var points = new List<(string, double, double)> { ("A1", 0.0003, 0.0075) };

        var entry = Assert.Single(g.ResolveHoldingPointEntries(
            points, 0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon));

        Assert.Equal(0, entry.HoldNodeId);
        Assert.NotEqual(0, entry.NodeId);   // the entry itself still resolves
    }

    [Fact]
    public void Zero_length_runway_and_empty_input_return_empty()
    {
        var g = BuildGraph();
        Assert.Empty(g.ResolveHoldingPointEntries(
            new List<(string, double, double)> { ("A1", HoldLat, 0.006) },
            0, 0.006, 0, 0.006, HalfWidthM, AcLat, AcLon));
        Assert.Empty(g.ResolveHoldingPointEntries(
            new List<(string, double, double)>(),
            0, 0, 0, FarLon, HalfWidthM, AcLat, AcLon));
    }
}
