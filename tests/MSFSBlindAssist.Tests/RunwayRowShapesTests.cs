using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The passing-callout runway probe warms from the RUNWAY ROWS alone — the start table and the
/// runway table — never from a taxi graph, so it answers at an airport with no taxi paths (18,737
/// of fs2024's 41,411 airports with a runway have none). Centreline pairing in TaxiGraph.Build reads
/// only those two tables, so building it with no paths and no parking IS the pairing; these pin
/// that the shapes come out identical to the full graph's, and what a field with nothing to pair
/// answers.
/// </summary>
public class RunwayRowShapesTests
{
    // 09/27: 3,000 m of pavement, 200 ft wide, whose 09 start row sits 600 m inside it (a displaced
    // threshold — RunwayMembershipTests' fixture). 36/18: 1,800 m, 150 ft wide, 1 km further east.
    private static List<StartPosition> Starts() => new()
    {
        new() { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(600), Heading = 90 },
        new() { RunwayName = "27", Latitude = Lat(0), Longitude = Lon(3000), Heading = 270 },
        new() { RunwayName = "36", Latitude = Lat(-900), Longitude = Lon(4000), Heading = 0 },
        new() { RunwayName = "18", Latitude = Lat(900), Longitude = Lon(4000), Heading = 180 },
    };

    private static List<Runway> Runways() => new()
    {
        new() { RunwayID = "09", Heading = 90, StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(3000), Width = 200.0 },
        new() { RunwayID = "27", Heading = 270, StartLat = Lat(0), StartLon = Lon(3000), EndLat = Lat(0), EndLon = Lon(0), Width = 200.0 },
        new() { RunwayID = "36", Heading = 0, StartLat = Lat(-900), StartLon = Lon(4000), EndLat = Lat(900), EndLon = Lon(4000), Width = 150.0 },
        new() { RunwayID = "18", Heading = 180, StartLat = Lat(900), StartLon = Lon(4000), EndLat = Lat(-900), EndLon = Lon(4000), Width = 150.0 },
    };

    /// <summary>A parallel taxiway north of 09/27, a connector to its hold line and one stand's
    /// lead-in — enough taxi network for Build to do everything it does with paths and parking.</summary>
    private static List<TaxiPath> Paths() => new()
    {
        new() { Name = "A", Type = "T", Width = 75, StartType = "N", EndType = "N", StartLat = Lat(150), StartLon = Lon(0), EndLat = Lat(150), EndLon = Lon(800) },
        new() { Name = "A", Type = "T", Width = 75, StartType = "N", EndType = "N", StartLat = Lat(150), StartLon = Lon(800), EndLat = Lat(150), EndLon = Lon(1500) },
        new() { Name = "A", Type = "T", Width = 75, StartType = "N", EndType = "N", StartLat = Lat(150), StartLon = Lon(1500), EndLat = Lat(150), EndLon = Lon(3000) },
        new() { Name = "A1", Type = "T", Width = 75, StartType = "N", EndType = "HS", StartLat = Lat(150), StartLon = Lon(1500), EndLat = Lat(70), EndLon = Lon(1500) },
        new() { Name = "", Type = "P", Width = 50, StartType = "P", EndType = "N", StartLat = Lat(260), StartLon = Lon(800), EndLat = Lat(150), EndLon = Lon(800) },
    };

    [Fact]
    public void The_runway_rows_alone_give_exactly_the_shapes_the_full_taxi_graph_does()
    {
        var parking = new List<ParkingSpot> { new() { Name = "GA", Number = 1, Latitude = Lat(260), Longitude = Lon(800), Heading = 180 } };
        var full = RunwayPavement.BuildShapes(TaxiGraph.Build(Paths(), parking, Starts(), Runways()).RunwayCenterlines);
        var rows = RunwayPavement.BuildShapesFromRunwayRows(Starts(), Runways());

        Assert.Equal(2, full.Count);
        Assert.Equal(full.Count, rows.Count);
        for (int i = 0; i < full.Count; i++)
        {
            Assert.Equal(full[i].Name1, rows[i].Name1);
            Assert.Equal(full[i].Name2, rows[i].Name2);
            Assert.Equal(full[i].Lat1, rows[i].Lat1, 12);
            Assert.Equal(full[i].Lon1, rows[i].Lon1, 12);
            Assert.Equal(full[i].Lat2, rows[i].Lat2, 12);
            Assert.Equal(full[i].Lon2, rows[i].Lon2, 12);
            Assert.Equal(full[i].HalfWidthMeters, rows[i].HalfWidthMeters, 9);
            Assert.Equal(full[i].UsesPavement, rows[i].UsesPavement);
            Assert.Equal(full[i].ExtentMinMeters, rows[i].ExtentMinMeters, 9);
            Assert.Equal(full[i].ExtentMaxMeters, rows[i].ExtentMaxMeters, 9);
        }
    }

    [Fact]
    public void The_runway_table_is_read_too_so_a_displaced_threshold_is_runway_pavement()
    {
        var shapes = RunwayPavement.BuildShapesFromRunwayRows(Starts(), Runways());
        Assert.True(RunwayPavement.IsOnPavement(Lat(0), Lon(200), shapes));       // 400 m before the 09 start row: still pavement
        Assert.True(RunwayPavement.IsOnPavement(Lat(0), Lon(4000), shapes));      // the middle of 18/36
        Assert.False(RunwayPavement.IsOnPavement(Lat(60), Lon(1500), shapes));    // 60 m off 09/27's centreline, outside its 30.5 m half-width
    }

    [Fact]
    public void An_airport_with_no_runways_gives_no_shapes_which_answers_not_on_a_runway()
    {
        var shapes = RunwayPavement.BuildShapesFromRunwayRows(new List<StartPosition>(), new List<Runway>());
        Assert.Empty(shapes);
        Assert.False(RunwayPavement.IsOnPavement(Lat(0), Lon(0), shapes));
    }

    [Fact]
    public void Start_rows_too_close_to_pair_give_no_shapes_exactly_as_in_the_full_graph()
    {
        // Build pairs opposing start rows only 200-6,000 m apart, so a 150 m strip is unknown to it —
        // the known limit RunwayPavement's own doc records, no worse here than for Where-Am-I
        // (measured on fs2024: 386 taxi-path-less airports have every start row within 200 m).
        var starts = new List<StartPosition>
        {
            new() { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(0), Heading = 90 },
            new() { RunwayName = "27", Latitude = Lat(0), Longitude = Lon(150), Heading = 270 },
        };
        Assert.Empty(RunwayPavement.BuildShapesFromRunwayRows(starts, new List<Runway>()));
    }
}
