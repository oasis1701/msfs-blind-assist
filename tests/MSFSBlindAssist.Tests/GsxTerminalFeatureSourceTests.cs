using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class GsxTerminalFeatureSourceTests
{
    private static ParkingSpot Gsx(string terminal, double lat, double lon)
        => new() { Source = GateSource.Gsx, TerminalName = terminal, Latitude = lat, Longitude = lon, Number = 1, Name = "B" };

    [Fact]
    public void Groups_gsx_stands_by_terminal_name_and_types_concourses()
    {
        var spots = new List<ParkingSpot>
        {
            Gsx("Terminal 4 - Concourse B", 40.6440, -73.7820), Gsx("Terminal 4 - Concourse B", 40.6442, -73.7820),
            Gsx("A-Platform =< Medium ", 52.31, 4.76), Gsx("A-Platform =< Medium ", 52.311, 4.76),
            Gsx("Lonely", 1, 1),
            new ParkingSpot { Source = GateSource.Navdata, TerminalName = "Terminal 4 - Concourse B", Latitude = 40.7, Longitude = -73.7 },
        };
        var f = GsxTerminalFeatureSource.Read(spots);
        Assert.Equal(2, f.Count);
        var b = f.Single(x => x.Kind == FeatureKind.Concourse);
        Assert.Equal("Terminal 4 - Concourse B", b.Name);
        Assert.InRange(b.Lat, 40.6440, 40.6442);
        Assert.Equal(FeatureSource.Gsx, b.Source);
        Assert.Equal("A-Platform", f.Single(x => x.Kind == FeatureKind.Terminal).Name);
    }

    private static ParkingSpot G(string terminal, int number, int type, double lat, double lon)
        => new() { Name = "", Number = number, Type = type, Latitude = lat, Longitude = lon, TerminalName = terminal, Source = GateSource.Gsx };

    [Fact]
    public void A_group_gets_its_kind_from_its_name_and_its_stands()
    {
        var spots = new List<ParkingSpot>
        {
            G("North Cargo Ramp", 1, 6, 40.660, -73.790), G("North Cargo Ramp", 2, 6, 40.6604, -73.790),
            G("Terminal 5", 1, 10, 40.645, -73.776), G("Terminal 5", 2, 10, 40.6454, -73.776),
            G("Terminal 4 - Concourse B", 20, 11, 40.641, -73.780), G("Terminal 4 - Concourse B", 21, 11, 40.6414, -73.780),
            G("Terminal 5 - Remote", 1, 4, 40.650, -73.770), G("Terminal 5 - Remote", 2, 4, 40.6504, -73.770),
            G("General Aviation Terminal", 1, 4, 40.655, -73.765), G("General Aviation Terminal", 2, 4, 40.6554, -73.765),
        };
        var byName = GsxTerminalFeatureSource.Read(spots).ToDictionary(f => f.Name, f => f.Kind);
        Assert.Equal(FeatureKind.Cargo, byName["North Cargo Ramp"]);
        Assert.Equal(FeatureKind.Terminal, byName["Terminal 5"]);
        Assert.Equal(FeatureKind.Concourse, byName["Terminal 4 - Concourse B"]);
        Assert.Equal(FeatureKind.Apron, byName["Terminal 5 - Remote"]);          // GA-ramp stands: an apron, not a terminal building
        Assert.Equal(FeatureKind.Fbo, byName["General Aviation Terminal"]);      // the shared lexicon, as the OSM tier already says
    }

    [Theory]
    [InlineData("Parking")] [InlineData("Ramp")] [InlineData("Gates")] [InlineData(" stands ")]
    public void A_bare_category_header_is_not_a_place(string header)
        => Assert.Empty(GsxTerminalFeatureSource.Read(new[] { G(header, 1, 10, 1.0, 1.0), G(header, 2, 10, 1.0004, 1.0) }));

    [Fact]
    public void A_group_carries_its_member_stands()
    {
        var f = Assert.Single(GsxTerminalFeatureSource.Read(new[] { G("Terminal 5", 1, 10, 40.645, -73.776), G("Terminal 5", 2, 10, 40.6454, -73.776) }));
        Assert.Equal(2, f.Members!.Count);
        Assert.Equal(FeatureSource.Gsx, f.Source);
    }
}
