using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class LocationGridTests
{
    [Theory]
    [InlineData(38.55179, -76.48357)]
    [InlineData(-33.8688, 151.2093)]
    [InlineData(0, 0)]
    [InlineData(89.999, 179.999)]
    [InlineData(-90, -180)]
    public void Cell_and_offsets_round_trip_to_about_a_metre(double lat, double lon)
    {
        int cell = LocationGrid.Cell(lat, lon);
        var (latOff, lonOff) = LocationGrid.Offsets(lat, lon);
        var (lat2, lon2) = LocationGrid.Decode(cell, latOff, lonOff);
        Assert.Equal(lat, lat2, 4);
        Assert.Equal(lon, lon2, 4);
        Assert.InRange(latOff, 0, 25000);
        Assert.InRange(lonOff, 0, 25000);
    }

    [Fact]
    public void Longitude_180_is_the_same_place_as_minus_180()
    {
        Assert.Equal(LocationGrid.Cell(10, -180), LocationGrid.Cell(10, 180));
    }

    [Fact]
    public void A_small_radius_is_one_range_per_row()
    {
        var ranges = LocationGrid.RangesForRadius(38.55, -76.48, 40);
        int rows = ranges.Select(r => r.From / LocationGrid.Cols).Distinct().Count();
        Assert.Equal(rows, ranges.Count);
        Assert.Contains(ranges, r => r.From <= LocationGrid.Cell(38.55, -76.48) && LocationGrid.Cell(38.55, -76.48) <= r.To);
        Assert.All(ranges, r => Assert.Equal(r.From / LocationGrid.Cols, r.To / LocationGrid.Cols));
    }

    [Fact]
    public void Ranges_cover_a_point_at_the_edge_of_the_radius()
    {
        // 39.9 km due east and due north of the centre must both be inside a 40 km search.
        double lat = 60.0, lon = 10.0;
        double dLonEast = 39.9 / (LocationGrid.KmPerDegree * Math.Cos(lat * Math.PI / 180.0));
        var ranges = LocationGrid.RangesForRadius(lat, lon, 40);
        Assert.Contains(ranges, r => Covers(r, LocationGrid.Cell(lat, lon + dLonEast)));
        Assert.Contains(ranges, r => Covers(r, LocationGrid.Cell(lat + 39.9 / LocationGrid.KmPerDegree, lon)));
    }

    [Fact]
    public void A_box_crossing_the_antimeridian_splits_each_row_in_two()
    {
        var ranges = LocationGrid.RangesForRadius(-17.7, 179.9, 50); // Fiji
        Assert.Contains(ranges, r => Covers(r, LocationGrid.Cell(-17.7, 179.95)));
        Assert.Contains(ranges, r => Covers(r, LocationGrid.Cell(-17.7, -179.8)));
        Assert.DoesNotContain(ranges, r => Covers(r, LocationGrid.Cell(-17.7, 0)));
    }

    [Fact]
    public void A_box_reaching_a_pole_takes_every_longitude()
    {
        var ranges = LocationGrid.RangesForRadius(89.8, 0, 50);
        Assert.Contains(ranges, r => Covers(r, LocationGrid.Cell(89.9, 120)));
        Assert.Contains(ranges, r => Covers(r, LocationGrid.Cell(89.9, -120)));
    }

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(180, -180)]
    [InlineData(-180, -180)]
    public void NormalizeLon_folds_into_minus_180_to_180(double input, double expected)
    {
        Assert.Equal(expected, LocationGrid.NormalizeLon(input), 9);
    }

    private static bool Covers((int From, int To) range, int cell) => range.From <= cell && cell <= range.To;
}
