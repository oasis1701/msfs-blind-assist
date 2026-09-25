using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

// Characterization of the lineup-entry maths lifted out of TaxiAssistForm.PopulateDestinations:
// start row → snapped onto the centerline → FindRunwayLineupEntryNode. Runway 09 runs east along
// north=0 from east 0 to 1,000 m; taxiway A1 meets it at east 200 and leaves north.
public class RunwayLineupTargetTests
{
    private static double Lat(double n) => RunwayFixture.Lat(n);
    private static double Lon(double e) => RunwayFixture.Lon(e);

    private static TaxiGraph Graph() => TaxiGraph.Build(
        new List<TaxiPath>
        {
            new() { Name = "A1", Type = "T", Width = 98, StartType = "HS", EndType = "N",
                    StartLat = Lat(0), StartLon = Lon(200), EndLat = Lat(100), EndLon = Lon(200) },
            // "A" continues from A1's far end AWAY from the runway (east 200 -> 400), not toward
            // it. A west endpoint back at east 0 sits only ~100 m from the threshold-area lineup
            // points under test, which makes it FindNearestNode's answer there (nearer than A1's
            // own runway-side node) and, being within RUNWAY_REACH_MAX_CROSS_M, short-circuits
            // FindRunwayLineupEntryNode before it ever needs to look for a real entrance — this
            // fixture's job is to characterize that entrance resolution, not defeat it.
            new() { Name = "A", Type = "T", Width = 98, StartType = "N", EndType = "N",
                    StartLat = Lat(100), StartLon = Lon(200), EndLat = Lat(100), EndLon = Lon(400) },
        },
        new List<ParkingSpot>(), new List<StartPosition>());

    private static Runway Runway09() => new()
    {
        RunwayID = "09", StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(1000),
        Heading = 90, Length = 1000 / 0.3048, Width = 150,
    };

    [Fact]
    public void Lineup_point_is_the_start_row_snapped_onto_the_centerline()
    {
        // The row sits 8 m north of the axis at east 60; the snap pulls it back onto north = 0.
        var rows = new[] { new StartPosition { RunwayName = "09", Latitude = Lat(8), Longitude = Lon(60), Heading = 90 } };

        var r = RunwayLineupTarget.Resolve(Graph(), Runway09(), rows, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(60), r.LineupLon, 9);
        Assert.NotNull(r.EntryNode);
        Assert.Equal(Lon(200), r.EntryNode!.Longitude, 9);   // A1's runway-side node
    }

    [Fact]
    public void Without_a_start_row_the_pavement_start_is_the_lineup_point()
    {
        var r = RunwayLineupTarget.Resolve(Graph(), Runway09(), null, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(0), r.LineupLon, 9);
        Assert.NotNull(r.EntryNode);
        Assert.Equal(Lon(200), r.EntryNode!.Longitude, 9);
    }

    [Fact]
    public void Half_width_falls_back_to_150ft_when_the_runway_has_no_width()
    {
        var rwy = Runway09();
        rwy.Width = 0;
        var r = RunwayLineupTarget.Resolve(Graph(), rwy, null, null, null);
        Assert.NotNull(r.EntryNode);
    }
}
