// Characterization tests for the 2026-09-18 VirtualPilot taxi round:
//   - GetRunwayIntersections quotes an ANGLED entry's distance from where the taxiway crosses
//     onto the pavement on the aircraft's side, only ever further down the runway (never back),
//     bounded to 300 m, and not at all without an aircraft position.
//   - IsDescriptiveHoldLabel drops OSM hold labels that name a KIND of hold ("28C APCH",
//     "GP HOLD LINE", "ILS") from the departure-entry picker, and keeps real refs ("A1", "B 12").
//
// Fixture: an east-west runway on the equator, as in RunwayIntersectionTests, where
// metres = degrees of longitude x 111132 exactly. Heading east, LEFT is north (lat > 0).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class AngledRunwayEntryTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;
    private const double FarLon = 0.027;                 // 3000.6 m runway
    private const double RunwayLenM = FarLon * M_PER_DEG;
    private const double HalfWidthM = 30.0;
    private const double StubLat = 0.0006;               // ~67 m off the centreline
    private const double EdgeLat = 28.0 * DEG_PER_M;     // 28 m: inside the edge band

    // B — enters from the NORTH angled BACK against the 09 takeoff direction: reaches the
    //     pavement at 1089 m, meets the centreline at 1000 m.
    // F — enters from the north angled FORWARD: pavement at 1911 m, centreline at 2000 m.
    // X — crosses the whole runway diagonally: north edge 1600 m, centreline 1667 m, south edge 1733 m.
    private static TaxiGraph Build()
    {
        var paths = new List<TaxiPath>
        {
            new() { StartLat = StubLat,  StartLon = 0.0105, EndLat = EdgeLat,  EndLon = 0.0098, Name = "B" },
            new() { StartLat = EdgeLat,  StartLon = 0.0098, EndLat = 0,        EndLon = 0.009,  Name = "B" },
            new() { StartLat = StubLat,  StartLon = 0.0166, EndLat = EdgeLat,  EndLon = 0.0172, Name = "F" },
            new() { StartLat = EdgeLat,  StartLon = 0.0172, EndLat = 0,        EndLon = 0.018,  Name = "F" },
            new() { StartLat = StubLat,  StartLon = 0.0138, EndLat = EdgeLat,  EndLon = 0.0144, Name = "X" },
            new() { StartLat = EdgeLat,  StartLon = 0.0144, EndLat = 0,        EndLon = 0.015,  Name = "X" },
            new() { StartLat = 0,        StartLon = 0.015,  EndLat = -EdgeLat, EndLon = 0.0156, Name = "X" },
            new() { StartLat = -EdgeLat, StartLon = 0.0156, EndLat = -StubLat, EndLon = 0.0162, Name = "X" },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static TaxiGraph.RunwayIntersection Entry(string name, double? acLat)
        => Build().GetRunwayIntersections(0, 0, 0, FarLon, HalfWidthM,
               aircraftLat: acLat, aircraftLon: acLat.HasValue ? 0.01 : null)
           .Single(ix => ix.TaxiwayName == name);

    [Fact]
    public void Entry_angled_back_is_quoted_from_where_it_meets_the_pavement()
    {
        var b = Entry("B", acLat: 0.001);   // aircraft north: comes in on B's side
        Assert.Equal(0.0098 * M_PER_DEG, b.AlongMetersFromThreshold, 1);
        Assert.Equal(RunwayLenM - 0.0098 * M_PER_DEG, b.RemainingMeters, 1);
    }

    [Fact]
    public void Without_an_aircraft_position_the_centreline_node_is_quoted_as_before()
    {
        var b = Entry("B", acLat: null);
        Assert.Equal(0.009 * M_PER_DEG, b.AlongMetersFromThreshold, 1);
    }

    [Fact]
    public void Entry_angled_forward_is_never_moved_back_towards_the_threshold()
    {
        // Moving it back would promise runway the lineup turn uses up (EGLL 09L A8).
        var f = Entry("F", acLat: 0.001);
        Assert.Equal(0.018 * M_PER_DEG, f.AlongMetersFromThreshold, 1);
    }

    [Fact]
    public void A_taxiway_crossing_the_runway_is_judged_from_the_aircrafts_side()
    {
        // From the north it reaches the pavement BEFORE its centreline node: unchanged.
        Assert.Equal(0.015 * M_PER_DEG, Entry("X", acLat: 0.001).AlongMetersFromThreshold, 1);
        // From the south it reaches the pavement after it: quoted from there.
        Assert.Equal(0.0156 * M_PER_DEG, Entry("X", acLat: -0.001).AlongMetersFromThreshold, 1);
    }

    [Theory]
    [InlineData("28C APCH", true)]
    [InlineData("28R APCH", true)]
    [InlineData("GP HOLD LINE", true)]
    [InlineData("ILS", true)]
    [InlineData("CAT III", true)]
    [InlineData("RWY 27 CAT II", true)]
    [InlineData("A1", false)]
    [InlineData("VIKAS", false)]
    [InlineData("N2E", false)]
    [InlineData("B 12", false)]      // a spaced taxiway ref, not a hold kind
    [InlineData("08L/26R", false)]   // IsRunwayDesignatorLabel's, not this rule's
    [InlineData("", false)]
    public void Descriptive_hold_labels_are_recognised(string label, bool expected)
        => Assert.Equal(expected, TaxiGraph.IsDescriptiveHoldLabel(label));
}
