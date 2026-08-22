// Characterization tests for TaxiGraph.GetLandingExits' final name dedup + coverage gap fill.
//
// Regression pinned: KBNA (Nashville) runway 20L offered a SINGLE exit, 2026-08-08.
//   That scenery names only eight taxiways at the whole airport (A-H; 1486 of its 2437 taxi
//   segments are unnamed and it has no numbered connectors at all), and every segment
//   touching runway 02R/20L is named "G". The exit pipeline ends by keeping one exit per
//   taxiway name — right when connectors are named individually, because then the repeats
//   are extra nodes along one RET arc — so 20L's five real turnoffs collapsed to one, and
//   the survivor was the threshold-nearest node: a backward-peeling arc at 1467 ft, i.e. a
//   130-degree turn 1500 ft down a 7991 ft runway. 02R was the mirror image.
//
// The fix keeps the name dedup exactly as it was and adds a COVERAGE pass: an exit a name
// dedup dropped is re-admitted only when no surviving exit lies within EXIT_COVERAGE_GAP_FT
// (1400 ft) of it. So a second node of the same arc stays collapsed, while a turnoff
// thousands of feet down an otherwise-uncovered runway comes back.
//
// Both properties matter and pull in opposite directions, so both are pinned here.
//
// Fixture: an east-west runway on the equator (lat 0) so lateral offset in metres is simply
// |node.lat| x 111132 (cos(0) = 1) and along-runway distance is the longitude offset. Every
// turnoff is a junction node ON the centreline with one named edge leaving at 45 degrees to a
// node 60 m off — clear of the 37.56 m corridor tolerance. No HoldShort/ILSHoldShort nodes
// exist, so this exercises the same geometric fallback path KBNA 20L is on.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitCoverageGapTests
{
    private const double M_PER_DEG = 111132.0;   // TaxiGraph's shared equirectangular constant
    private const double DEG_PER_M = 1.0 / M_PER_DEG;
    private const double FT_PER_M = 1.0 / 0.3048;

    private const double RunwayWidthFt = 148.0;                          // half-width 22.56 m
    private const double RunwayLengthDeg = 0.03;                         // ~10938 ft

    private static Runway Runway09() => new Runway
    {
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,                                  // due east (true, per DB model)
        Length = RunwayLengthDeg * M_PER_DEG * FT_PER_M,
        Width = RunwayWidthFt,
        ThresholdOffset = 0.0,
    };

    /// <summary>
    /// One turnoff per entry in <paramref name="alongFeet"/>, all sharing <paramref name="name"/>:
    /// a junction on the centreline with a single named edge leaving at 45 degrees to a node
    /// 60 m north — outside the corridor, so it reads as a real exit rather than a parallel
    /// taxiway. 45 degrees classifies as High-speed, which is the case KBNA's lost turnoffs hit.
    /// </summary>
    private static TaxiGraph BuildSharedNameGraph(string name, params double[] alongFeet)
    {
        var paths = new List<TaxiPath>();
        foreach (double ft in alongFeet)
        {
            double alongM = ft * 0.3048;
            paths.Add(new TaxiPath
            {
                StartLat = 0.0,
                StartLon = alongM * DEG_PER_M,
                EndLat = 60.0 * DEG_PER_M,
                EndLon = (alongM + 60.0) * DEG_PER_M,
                Name = name,
            });
        }
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    [Fact]
    public void Turnoffs_sharing_one_taxiway_name_are_all_offered_when_far_apart()
    {
        // The KBNA 20L shape: separate turnoffs, thousands of feet apart, all called "G".
        var exits = BuildSharedNameGraph("G", 2000, 4000, 6000).GetLandingExits(Runway09());

        Assert.Equal(3, exits.Count);
        Assert.All(exits, e => Assert.Equal("G", e.TaxiwayName));
        Assert.Collection(exits,
            e => Assert.Equal(2000, e.DistanceFromThresholdFeet, 10.0),
            e => Assert.Equal(4000, e.DistanceFromThresholdFeet, 10.0),
            e => Assert.Equal(6000, e.DistanceFromThresholdFeet, 10.0));
    }

    [Fact]
    public void Nodes_of_one_arc_sharing_a_name_still_collapse_to_a_single_exit()
    {
        // The property the name dedup exists for, and which the gap fill must not undo:
        // one curved RET modelled as several nodes is ONE choice, not three.
        var exits = BuildSharedNameGraph("G", 2000, 2400, 2800).GetLandingExits(Runway09());

        var only = Assert.Single(exits);
        Assert.Equal("G", only.TaxiwayName);
        Assert.Equal(2000, only.DistanceFromThresholdFeet, 10.0);
    }

    [Fact]
    public void An_arc_contributes_one_exit_even_when_it_spans_more_than_the_coverage_window()
    {
        // Chaining: each admitted exit becomes coverage for the next candidate, so a long arc
        // of closely spaced nodes cannot creep past the window one node at a time.
        var exits = BuildSharedNameGraph("G", 2000, 3000, 4000, 5000).GetLandingExits(Runway09());

        Assert.Equal(2, exits.Count);                       // 2000, then 4000 (3000 is covered)
        Assert.Equal(2000, exits[0].DistanceFromThresholdFeet, 10.0);
        Assert.Equal(4000, exits[1].DistanceFromThresholdFeet, 10.0);
    }

    [Fact]
    public void Distinctly_named_turnoffs_are_unaffected_by_the_gap_fill()
    {
        // The well-named-airport case (KBOS, EGLL, KJFK ...): every turnoff already survives
        // the name dedup, so the coverage pass has nothing to re-admit and changes nothing.
        var paths = new List<TaxiPath>();
        string[] names = { "A", "B", "C" };
        for (int i = 0; i < names.Length; i++)
        {
            double alongM = (2000 + i * 2000) * 0.3048;
            paths.Add(new TaxiPath
            {
                StartLat = 0.0,
                StartLon = alongM * DEG_PER_M,
                EndLat = 60.0 * DEG_PER_M,
                EndLon = (alongM + 60.0) * DEG_PER_M,
                Name = names[i],
            });
        }
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var exits = graph.GetLandingExits(Runway09());

        Assert.Equal(new[] { "A", "B", "C" }, exits.Select(e => e.TaxiwayName));
    }

    [Fact]
    public void Exits_stay_sorted_nearest_first_after_the_gap_fill()
    {
        // LandingExitPlanner's downfield scan, the planner form's default selection and
        // RetargetLandingExit all read this list in order; the gap fill appends out of order
        // and must re-sort.
        var exits = BuildSharedNameGraph("G", 2000, 4000, 6000, 8000).GetLandingExits(Runway09());

        Assert.True(exits.Count >= 3);
        for (int i = 1; i < exits.Count; i++)
            Assert.True(exits[i].DistanceFromThresholdFeet >= exits[i - 1].DistanceFromThresholdFeet,
                $"exit {i} at {exits[i].DistanceFromThresholdFeet:F0} ft came after " +
                $"{exits[i - 1].DistanceFromThresholdFeet:F0} ft");
    }
}
