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
    public void Rescue_scan_skips_turnoffs_that_would_need_a_turn_past_ninety_degrees()
    {
        // A stub peeling BACK toward the approach end. Taking it means turning around, which
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
