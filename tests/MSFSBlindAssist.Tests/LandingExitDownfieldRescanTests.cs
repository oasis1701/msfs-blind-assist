// Tests for the missed-exit rescue scan.
//
// Reported 2026-08-23, CYYZ runway 23 (11,122 ft), live: the pilot planned taxiway H2 and
// rolled 104 ft past it. With 5,400 ft of runway still ahead — and, in navdata, three more
// turnoffs down that stretch — the rollout announced "Missed last exit on runway 23", counted
// the pilot down to the pavement end, and put them into a 180-degree backtrack on an active
// runway at a busy hub. The landing_exit.log line is
//   "OVERSHOOT no downfield exit -> EnterRunwayEndCountdown"  (allExits.Count=6)
//
// The overshoot handler only ever looks at the list GetLandingExits returned. That list is
// built for the PLANNER DIALOG, and it is lossy by design in two ways that matter here:
//
//   1. `hasHoldShortOnRunway` — the moment ONE node in the runway corridor carries a
//      hold-short marker with a forward exit, the geometric fallback is switched off for the
//      WHOLE runway. Turnoffs the scenery did not mark with a hold-short bar then do not
//      exist as far as the rollout is concerned. Rapid-exit taxiways are routinely modelled
//      that way — they are one-way turnoffs, so there is no hold-short line on the runway.
//   2. the per-name dedup keeps one entry per taxiway name.
//
// Neither is wrong for the dialog. Both are wrong as the sole answer to "is there any way off
// this runway ahead of me?", which is a safety question, asked at 92 kt, whose fallback answer
// is a backtrack. So the rescue scan asks the GRAPH directly, and only when the normal list
// has already come up empty.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitDownfieldRescanTests
{
    private const double M_PER_DEG = 111132.0;   // TaxiGraph's shared equirectangular constant
    private const double DEG_PER_M = 1.0 / M_PER_DEG;
    private const double FT_PER_M = 1.0 / 0.3048;

    private const double RunwayWidthFt = 200.0;                  // CYYZ 05/23 — half-width 30.5 m
    private const double RunwayLengthFt = 11122.0;

    /// <summary>Due-east runway on the equator, so along-runway = longitude and lateral = latitude.</summary>
    private static Runway Runway09() => new Runway
    {
        RunwayID = "09",
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,                                  // due east (true, per DB model)
        Length = RunwayLengthFt,
        Width = RunwayWidthFt,
        ThresholdOffset = 0.0,
    };

    /// <summary>
    /// A turnoff: a junction node on the centreline at <paramref name="alongFeet"/> with one
    /// named edge leaving at 45 degrees to a node 120 m north — well clear of the corridor
    /// tolerance (half-width 30.5 m + 15 m). <paramref name="startType"/> marks the junction
    /// node: "" for a plain junction, "HS" for a hold-short bar.
    /// </summary>
    private static TaxiPath Turnoff(string name, double alongFeet, string startType = "")
    {
        double alongM = alongFeet * 0.3048;
        return new TaxiPath
        {
            StartLat = 0.0,
            StartLon = alongM * DEG_PER_M,
            StartType = startType,
            EndLat = 120.0 * DEG_PER_M,
            EndLon = (alongM + 120.0) * DEG_PER_M,
            Name = name,
        };
    }

    /// <summary>
    /// The CYYZ 23 shape: an early crossing taxiway carrying a hold-short bar, then three
    /// unmarked rapid-exit turnoffs further down the runway.
    /// </summary>
    private static TaxiGraph BuildMarkedCrossingPlusUnmarkedTurnoffs() =>
        TaxiGraph.Build(
            new List<TaxiPath>
            {
                Turnoff("B",  1700, startType: "HS"),   // crossing taxiway — has a hold-short bar
                Turnoff("H2", 5100),                    // the planned exit — no hold-short bar
                Turnoff("H4", 6700),
                Turnoff("J2", 7600),
            },
            new List<ParkingSpot>(),
            new List<StartPosition>());

    // ---------------------------------------------------------------------------------
    // Characterization: this is the state of the world the rescue scan exists to survive.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void One_hold_short_marker_hides_every_unmarked_turnoff_from_the_planner_list()
    {
        var exits = BuildMarkedCrossingPlusUnmarkedTurnoffs().GetLandingExits(Runway09());

        // Only the marked crossing survives. H2, H4 and J2 are real turnoffs off the same
        // runway and none of them is offered.
        Assert.Equal(new[] { "B" }, exits.Select(e => e.TaxiwayName).ToArray());
    }

    // ---------------------------------------------------------------------------------
    // The rescue scan.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Rescue_scan_finds_turnoffs_the_planner_list_dropped()
    {
        var graph = BuildMarkedCrossingPlusUnmarkedTurnoffs();

        // Standing 104 ft past H2, as the CYYZ report did.
        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 5100 + 104);

        Assert.Equal(new[] { "H4", "J2" }, found.Select(e => e.TaxiwayName).ToArray());
    }

    [Fact]
    public void Rescue_scan_returns_turnoffs_nearest_first()
    {
        var graph = BuildMarkedCrossingPlusUnmarkedTurnoffs();

        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 0.0);

        Assert.Equal(
            new[] { 1700.0, 5100.0, 6700.0, 7600.0 },
            found.Select(e => Math.Round(e.DistanceFromThresholdFeet / 100.0) * 100.0).ToArray());
    }

    [Fact]
    public void Rescue_scan_never_offers_a_turnoff_behind_the_aircraft()
    {
        var graph = BuildMarkedCrossingPlusUnmarkedTurnoffs();

        // Past every turnoff on the runway — the honest answer is "nothing left".
        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 8000.0);

        Assert.Empty(found);
    }

    [Fact]
    public void Rescue_scan_skips_a_turnaround_turning_more_than_110_degrees_to_clear()
    {
        // A stub peeling BACK toward the approach end (135 degrees here, past
        // RolloutExitGate.TurnaroundAboveDeg). Taking it means turning around, which
        // is not an exit — it is the backtrack the rescue scan exists to avoid.
        double alongM = 6000.0 * 0.3048;
        var graph = TaxiGraph.Build(
            new List<TaxiPath>
            {
                Turnoff("B", 1700, startType: "HS"),
                new TaxiPath
                {
                    StartLat = 0.0,
                    StartLon = alongM * DEG_PER_M,
                    EndLat = 120.0 * DEG_PER_M,
                    EndLon = (alongM - 120.0) * DEG_PER_M,   // north AND back west
                    Name = "Z9",
                },
            },
            new List<ParkingSpot>(),
            new List<StartPosition>());

        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 2000.0);

        Assert.Empty(found);
    }

    [Fact]
    public void Rescue_scan_offers_a_branch_turning_about_100_degrees_with_its_angle_capped_at_90()
    {
        // A stub leaving at 100 degrees to the landing heading: past the old first-edge "> 90"
        // cut-off, but short of a turnaround (RolloutExitGate.TurnaroundAboveDeg, 110). Measured
        // by its branch it IS a way off the runway, offered with its angle capped at 90.
        double alongM = 6000.0 * 0.3048;
        double backM = 120.0 * Math.Tan(10.0 * Math.PI / 180.0);   // 120 m out, 21 m back: 100 degrees
        var graph = TaxiGraph.Build(
            new List<TaxiPath>
            {
                Turnoff("B", 1700, startType: "HS"),
                new TaxiPath
                {
                    StartLat = 0.0,
                    StartLon = alongM * DEG_PER_M,
                    EndLat = 120.0 * DEG_PER_M,
                    EndLon = (alongM - backM) * DEG_PER_M,   // north and slightly back west
                    Name = "Z1",
                },
            },
            new List<ParkingSpot>(),
            new List<StartPosition>());

        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 2000.0);

        var z1 = Assert.Single(found);
        Assert.Equal("Z1", z1.TaxiwayName);
        Assert.Equal(90.0, z1.ExitAngleDegrees);
    }

    [Fact]
    public void Rescue_scan_ignores_a_parallel_taxiway_running_alongside_the_runway()
    {
        // A taxiway inside the corridor whose edges run along the runway is not a way off it.
        var paths = new List<TaxiPath> { Turnoff("B", 1700, startType: "HS") };
        for (int i = 0; i < 4; i++)
        {
            double aM = (4000.0 + i * 800.0) * 0.3048;
            paths.Add(new TaxiPath
            {
                StartLat = 20.0 * DEG_PER_M,                 // 20 m off centreline: inside the corridor
                StartLon = aM * DEG_PER_M,
                EndLat = 20.0 * DEG_PER_M,
                EndLon = (aM + 240.0) * DEG_PER_M,
                Name = "P",
            });
        }
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 2000.0);

        Assert.Empty(found);
    }

    [Fact]
    public void Rescue_scan_never_offers_a_node_on_a_parallel_taxiway_beyond_the_pavement()
    {
        // S36 15 (fs2024): a 40 ft runway (half-width 6.1 m, corridor 21.1 m) with parallel A 20.9 m out,
        // inside the corridor; here A bends away past the runway end. The corridor walk from any node on
        // the straight stretch reaches the bend, and the scan offered the node as a 0.2-degree "high-speed
        // exit" - turn-now pointing the pilot across the grass at a taxiway no connector joins there.
        var runway = new Runway
        {
            RunwayID = "09", StartLat = 0.0, StartLon = 0.0, Heading = 90.0,
            Length = 3005.0, Width = 40.0, ThresholdOffset = 0.0,
        };
        const double lateralM = 20.9;
        double[] alongM = { 400.0, 500.0, 600.0, 700.0, 800.0, 905.0 };   // the bend is past the runway end
        var paths = new List<TaxiPath>();
        for (int i = 0; i + 1 < alongM.Length; i++)
            paths.Add(new TaxiPath
            {
                StartLat = lateralM * DEG_PER_M, StartLon = alongM[i] * DEG_PER_M,
                EndLat = lateralM * DEG_PER_M, EndLon = alongM[i + 1] * DEG_PER_M,
                Name = "A",
            });
        paths.Add(new TaxiPath
        {
            StartLat = lateralM * DEG_PER_M, StartLon = 905.0 * DEG_PER_M,
            EndLat = 60.0 * DEG_PER_M, EndLon = 925.0 * DEG_PER_M,
            Name = "A",
        });
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(runway, afterDistanceFromThresholdFeet: 1000.0);

        Assert.Empty(found);
    }

    [Theory]
    // S36 15 node 105: parallel A, 20.9 m out on a 6.1 m half-width, both edges within 0.2 degrees of the axis.
    [InlineData(20.9, 6.1, 0.2, false, false)]
    // 83FL 30 node 22: parallel A 22.2 m out on 8.5 m, its steepest edge 3.1 degrees.
    [InlineData(22.2, 8.5, 3.1, false, false)]
    // KBKD 04 node 44: on the pavement (3.7 m on 7.2 m) - always a candidate.
    [InlineData(3.7, 7.2, 0.0, false, true)]
    // KENV 26 node 338: taxiway B stopped 6.8 m beyond the pavement edge, leaving at 73 degrees.
    [InlineData(29.7, 22.9, 73.2, true, true)]
    // KSRQ 32 node 601: a fork just beyond the pavement, both arms leaving at about 76 degrees.
    [InlineData(24.0, 22.9, 76.1, true, true)]
    // NC12 26 node 30: parallel A meandering 20.0 m out on a 10.4 m half-width, 2.5 degrees one way and
    // 6.7 the other - steep enough to pass the 5-degree test, and still no way off the runway.
    [InlineData(20.0, 10.4, 6.7, false, false)]
    // SC41 33 node 66: parallel B 27.7 m out on 16.9 m, where a loop to the apron leaves it at 48 degrees.
    [InlineData(27.7, 16.9, 48.0, false, false)]
    // ZGKL 01 node 402: a rapid exit's arc 3.6 m beyond the pavement, 13.6 degrees in and 19.0 out - a line
    // along the runway at this node, but walked inward it gets onto it.
    [InlineData(23.4, 19.8, 19.0, true, true)]
    // KBUR 26 node 525: D's inner end 0.4 m beyond the pavement edge, with a 1.4 m link running forward
    // to its twin - no line along the runway there, so a way off it.
    [InlineData(23.3, 22.9, 80.3, true, true)]
    public void A_node_beyond_the_pavement_is_a_rescue_candidate_only_on_a_way_off_the_runway(
        double absLateralM, double halfWidthM, double steepestEdgeOffAxisDeg, bool leadsOntoRunway, bool expected)
    {
        Assert.Equal(expected, TaxiGraph.IsRescueCandidateSite(absLateralM, halfWidthM, steepestEdgeOffAxisDeg,
            leadsOntoRunway));
    }

    /// <summary>A narrow due-east runway on the equator, for the parallel-taxiway shapes below.</summary>
    private static Runway NarrowRunway09(double lengthFt, double widthFt) => new Runway
    {
        RunwayID = "09", StartLat = 0.0, StartLon = 0.0, Heading = 90.0,
        Length = lengthFt, Width = widthFt, ThresholdOffset = 0.0,
    };

    /// <summary>One edge per consecutive pair of (along, lateral) points, in metres.</summary>
    private static IEnumerable<TaxiPath> Line(string name, params (double AlongM, double LateralM)[] points)
        => Line(name, 0.0, points);

    /// <summary>As <see cref="Line(string, (double, double)[])"/>, with a navdata width on every edge.</summary>
    private static IEnumerable<TaxiPath> Line(string name, double widthFeet, params (double AlongM, double LateralM)[] points)
    {
        for (int i = 0; i + 1 < points.Length; i++)
            yield return new TaxiPath
            {
                StartLat = points[i].LateralM * DEG_PER_M, StartLon = points[i].AlongM * DEG_PER_M,
                EndLat = points[i + 1].LateralM * DEG_PER_M, EndLon = points[i + 1].AlongM * DEG_PER_M,
                Name = name,
                Width = widthFeet,
            };
    }

    [Fact]
    public void Rescue_scan_never_offers_a_bend_of_a_meandering_parallel_taxiway()
    {
        // NC12 26 (fs2024): a 68 ft runway (half-width 10.4 m, corridor 25.4 m) with parallel A beside it, 20 to
        // 25 m out and bending up to 6.7 degrees off the axis; its only connector is at 410 m. Past it, the scan
        // offered the bend at 1,404 m - 20.0 m out, reached from the runway only across the grass - as
        // "A, End, 6.7 degrees", and turn-now would have pointed the pilot at it.
        // A is 26 ft wide: 20.0 m out it is still 5.6 m of grass from the runway edge.
        var runway = NarrowRunway09(5097.0, 68.0);
        var paths = new List<TaxiPath>();
        paths.AddRange(Line("A", 26.0, (410.0, 0.0), (410.0, 20.0)));
        paths.AddRange(Line("A", 26.0, (410.0, 20.0), (1204.8, 23.0), (1351.0, 22.3), (1404.2, 20.0), (1442.0, 24.5),
            (1514.1, 32.1), (1549.5, 33.3)));
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(runway, afterDistanceFromThresholdFeet: 1446.0);

        Assert.Empty(found);
    }

    [Fact]
    public void Rescue_scan_never_offers_a_parallel_taxiway_where_a_loop_to_the_apron_leaves_it()
    {
        // SC41 33 (fs2024): parallel B 27.7 m out on a 111 ft runway (half-width 16.9 m, corridor 31.9 m), and
        // at 1,082 m a short loop leaves it at 48 degrees toward a parking lead-in. That steep edge passed the
        // 5-degree test, and the scan offered the node - on the parallel, 10.8 m beyond the runway edge - as
        // "B, End, 48 degrees".
        // B is 20 ft wide: 7.8 m of grass lies between it and the runway edge.
        var runway = NarrowRunway09(3708.0, 111.0);
        var paths = new List<TaxiPath>();
        paths.AddRange(Line("A", 20.0, (830.0, 0.0), (830.0, 60.0)));            // the real exit, behind
        paths.AddRange(Line("B", 20.0, (11.8, 31.3), (1082.0, 27.7), (1090.4, 27.9), (1134.7, 27.3)));
        paths.AddRange(Line("B", 20.0, (1082.0, 27.7), (1084.6, 30.6), (1086.1, 33.5), (1087.6, 84.5)));
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(runway, afterDistanceFromThresholdFeet: 2825.0);

        Assert.Empty(found);
    }

    [Fact]
    public void Rescue_scan_still_offers_a_parallel_taxiway_whose_pavement_meets_the_runway()
    {
        // 4AK6 19 (fs2024): CC runs 1.1 m beyond the navdata edge of a 98 ft runway, and it is 55 ft wide - its
        // pavement overlaps the runway's, so an aircraft rolls straight onto it. Where B leaves CC at 58.8
        // degrees is reachable on paved ground, and stays a way off.
        var runway = NarrowRunway09(3000.0, 98.0);
        var paths = new List<TaxiPath>();
        paths.AddRange(Line("CC", 55.0, (200.0, -16.0), (380.0, -16.0), (417.5, -16.0), (436.5, -16.6)));
        paths.AddRange(Line("B", 55.0, (417.5, -16.0), (426.8, -31.8), (430.0, -80.0)));
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(runway, afterDistanceFromThresholdFeet: 1300.0);

        Assert.Contains(found, e => e.TaxiwayName == "B");
    }

    [Theory]
    // EGDR 36 (fs2024): L, 37 ft wide, 27.4 m out on a 21.6 m half-width - 0.2 m short of the runway edge.
    [InlineData(27.4, 21.6, 37.0, true)]
    // NC12 26: A, 26 ft wide, 20.0 m out on 10.4 m - 5.7 m of grass.
    [InlineData(20.0, 10.4, 26.0, false)]
    // SC41 33: B, 20 ft wide, 27.7 m out on 16.9 m - 7.8 m of grass.
    [InlineData(27.7, 16.9, 20.0, false)]
    // A row with no width is no evidence of pavement, however close.
    [InlineData(11.0, 10.4, 0.0, false)]
    public void A_taxiway_touches_the_runway_pavement_across_a_seam_but_not_across_a_verge(
        double lateralM, double halfWidthM, double taxiwayWidthFt, bool expected)
    {
        var paths = Line("T", taxiwayWidthFt, (100.0, lateralM), (200.0, lateralM)).ToList();
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var axis = new RunwayAxis(0.0, 0.0, 90.0, halfWidthM);
        int node = graph.Nodes.Values.OrderBy(n => n.Longitude).First().NodeId;

        Assert.Equal(expected, ExitBranch.TouchesRunwayPavement(graph, axis, node, lateralM));
    }

    [Fact]
    public void Rescue_scan_still_offers_a_rapid_exit_arc_beyond_the_pavement()
    {
        // Past the arc's pavement nodes, the only candidate is 5.5 m beyond the runway edge on a line running
        // within 20 degrees of the axis both ways - a parallel taxiway's shape at that one node. But walked
        // inward its taxiway gets onto the runway (ExitBranch.LeadsOntoRunway), so it is a way off and stays.
        var paths = new List<TaxiPath>
        {
            Turnoff("B", 1700, startType: "HS"),
        };
        paths.AddRange(Line("R1", (1500.0, 0.0), (1560.0, 8.0), (1620.0, 20.0), (1680.0, 36.0), (1740.0, 55.0),
            (1790.0, 80.0)));
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 1650.0 * FT_PER_M);

        var r1 = Assert.Single(found);
        Assert.Equal("R1", r1.TaxiwayName);
    }

    [Fact]
    public void Rescue_scan_still_offers_a_connector_whose_stub_stops_short_of_the_runway()
    {
        // KTPA 10 (fs2024): N's two lanes meet in a small triangle whose inner corner stops 4.3 m beyond the
        // navdata runway edge (half-width 22.9 m). The candidate is the lane end 32.7 m out; walked inward it
        // stops at that corner, which is no line along the runway - so N is a way off, named N.
        var runway = NarrowRunway09(8300.0, 150.0);
        var paths = new List<TaxiPath>();
        paths.AddRange(Line("", (719.0, -32.7), (720.6, -27.2), (722.0, -32.8), (719.0, -32.7)));
        paths.AddRange(Line("N", (719.0, -32.7), (719.1, -102.1), (709.5, -121.7)));
        paths.AddRange(Line("N", (722.0, -32.8), (722.1, -101.9)));
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(runway, afterDistanceFromThresholdFeet: 2000.0);

        Assert.Equal("N", found.First().TaxiwayName);
    }

    [Fact]
    public void Rescue_scan_still_offers_an_exit_whose_first_edge_crosses_the_centreline()
    {
        // LEMD 18R (fs2024): Z7 leaves from 3.0 m right of the centreline in one 166 m edge at 14.5 degrees and
        // reaches 38.5 m left, 8.5 m beyond the pavement. Walked inward from there it is on the pavement after
        // 34 m - the walk's 150 m bound must not count the whole edge.
        var runway = NarrowRunway09(13710.0, 197.0);
        var paths = new List<TaxiPath>();
        paths.AddRange(Line("Z7", (2400.0, 3.0), (2560.7, -38.5), (2600.7, -59.1), (2640.0, -100.0)));
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(runway, afterDistanceFromThresholdFeet: 2500.0 * FT_PER_M);

        var z7 = Assert.Single(found);
        Assert.Equal("Z7", z7.TaxiwayName);
    }

    [Fact]
    public void Rescue_scan_still_offers_a_connector_ending_just_beyond_the_pavement_edge()
    {
        // KBUR 26 (fs2024): D's inner end is 0.4 m beyond the navdata runway edge, joined to its twin by a 1.4 m
        // link running forward along the runway. One edge along the runway is not a line along it: D stays.
        var runway = NarrowRunway09(6886.0, 150.0);
        var paths = new List<TaxiPath>();
        paths.AddRange(Line("", (600.8, 23.3), (602.2, 23.2)));
        paths.AddRange(Line("D", (600.8, 23.3), (605.4, 50.3), (613.5, 75.2), (625.6, 125.5)));
        paths.AddRange(Line("D", (602.2, 23.2), (608.2, 49.4), (613.5, 75.2)));
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var found = graph.FindDownfieldExits(runway, afterDistanceFromThresholdFeet: 1000.0);

        var d = Assert.Single(found);
        Assert.Equal("D", d.TaxiwayName);
    }

    [Fact]
    public void Rescue_scan_stops_at_the_runway_end()
    {
        // A turnoff at the very end of the pavement is a backtrack in disguise, and the
        // end-of-runway callouts already cover that stretch.
        var graph = TaxiGraph.Build(
            new List<TaxiPath>
            {
                Turnoff("B", 1700, startType: "HS"),
                Turnoff("Z", RunwayLengthFt + 200.0),
            },
            new List<ParkingSpot>(),
            new List<StartPosition>());

        var found = graph.FindDownfieldExits(Runway09(), afterDistanceFromThresholdFeet: 2000.0);

        Assert.Empty(found);
    }
}
