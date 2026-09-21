// RunwayShape: the one definition of where a runway is, shared by the route classifier, hold
// placement, Where-Am-I, takeoff detection and the landing guards. See docs/taxi-guidance.md
// ("Runway crossings and entries") for why the pavement ends are preferred and when they are not.
//
// Fixture idiom: an east-west runway just north of the equator (lat 0.01, never (0, 0), which the
// accessor treats as unset), where one degree of latitude is 111,132 m and cos(lat) is ~1.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayShapeTests
{
    private const double M = 111132.0;
    private const double BaseLat = 0.01;
    private const double BaseLon = 0.01;

    private static double Lon(double metresEast) => BaseLon + metresEast / (M * Math.Cos(BaseLat * Math.PI / 180.0));
    private static double Lat(double metresNorth) => BaseLat + metresNorth / M;

    /// <summary>Pavement 0..3000 m east, 30 m half-width; start rows at <paramref name="row1"/> / <paramref name="row2"/> metres along, on the axis.</summary>
    private static TaxiGraph.RunwayCenterline Runway(double row1 = 0.0, double row2 = 3000.0, double rowLateral = 0.0) => new()
    {
        Name1 = "09", Name2 = "27",
        Lat1 = Lat(rowLateral), Lon1 = Lon(row1),
        Lat2 = Lat(rowLateral), Lon2 = Lon(row2),
        HalfWidthMeters = 22.86,
        PavementLat1 = BaseLat, PavementLon1 = BaseLon,
        PavementLat2 = BaseLat, PavementLon2 = Lon(3000.0),
        PavementHalfWidthMeters = 30.0,
    };

    [Fact]
    public void Usable_pavement_supplies_the_ends_and_the_half_width()
    {
        var shape = RunwayShape.For(Runway(row1: 600.0));

        Assert.True(shape.UsesPavement);
        Assert.Equal(BaseLon, shape.Lon1, 9);
        Assert.Equal(Lon(3000.0), shape.Lon2, 9);
        Assert.Equal(30.0, shape.HalfWidthMeters, 6);
        Assert.InRange(shape.LengthMeters, 2999.0, 3001.0);
    }

    [Fact]
    public void A_half_filled_pavement_pair_falls_back_to_the_start_rows()
    {
        var cl = Runway(row1: 600.0);
        cl.PavementLat2 = 0.0;
        cl.PavementLon2 = 0.0;

        var shape = RunwayShape.For(cl);

        Assert.False(shape.UsesPavement);
        Assert.Equal(cl.Lon1, shape.Lon1, 9);
        Assert.Equal(22.86, shape.HalfWidthMeters, 6);
    }

    [Fact]
    public void A_non_finite_pavement_falls_back_to_the_start_rows()
    {
        var cl = Runway();
        cl.PavementLon2 = double.NaN;
        Assert.False(RunwayShape.For(cl).UsesPavement);
    }

    [Fact]
    public void A_zero_length_pavement_falls_back_to_the_start_rows()
    {
        var cl = Runway();
        cl.PavementLat2 = cl.PavementLat1;
        cl.PavementLon2 = cl.PavementLon1;
        Assert.False(RunwayShape.For(cl).UsesPavement);
    }

    [Fact]
    public void Start_rows_off_the_pavement_axis_mean_the_pavement_belongs_to_another_runway()
    {
        // EDVQ: the heading pass paired a grass runway with another runway's pavement. The rows
        // sit 150 m to the side, far beyond half-width + the 10 m clear margin.
        var shape = RunwayShape.For(Runway(rowLateral: 150.0));

        Assert.False(shape.UsesPavement);
        Assert.Equal(Lat(150.0), shape.Lat1, 9);
    }

    [Fact]
    public void A_non_positive_half_width_uses_the_75_foot_default()
    {
        var cl = Runway();
        cl.PavementHalfWidthMeters = 0.0;
        Assert.Equal(RunwayShape.DefaultHalfWidthMeters, RunwayShape.For(cl).HalfWidthMeters, 6);
        Assert.Equal(22.86, RunwayShape.DefaultHalfWidthMeters, 6);
    }

    [Fact]
    public void A_malformed_pavement_width_is_capped_to_the_plausible_maximum()
    {
        // fs2024's worst observed runway.width defect (2001 ft -> ~304.95 m half-width, measured
        // over the shipped database). The pavement line itself is sound (on the start rows'
        // axis), so it stays usable — only the implausible half-width is repaired.
        var cl = Runway();
        cl.PavementHalfWidthMeters = 2001.0 * 0.3048 / 2.0;

        var shape = RunwayShape.For(cl);

        Assert.True(shape.UsesPavement);
        Assert.Equal(RunwayShape.MaxPlausibleHalfWidthMeters, shape.HalfWidthMeters, 6);
        Assert.Equal(60.96, RunwayShape.MaxPlausibleHalfWidthMeters, 6);
    }

    [Fact]
    public void The_extent_covers_an_outboard_start_row()
    {
        // LIMC 17L / KSAW 01 shape: the start row sits 300 m BEYOND the pavement end.
        var shape = RunwayShape.For(Runway(row1: -300.0));

        Assert.True(shape.UsesPavement);
        Assert.InRange(shape.ExtentMinMeters, -300.5, -299.5);
        Assert.InRange(shape.ExtentMaxMeters, 2999.0, 3001.0);
        Assert.True(shape.Contains(Lat(0.0), Lon(-200.0), 0.0));
        Assert.False(shape.Contains(Lat(0.0), Lon(-350.0), 0.0));
    }

    [Fact]
    public void Without_usable_pavement_the_extent_is_the_start_row_line()
    {
        var cl = Runway(row1: 600.0);
        cl.PavementLon2 = double.NaN;
        var shape = RunwayShape.For(cl);

        Assert.Equal(0.0, shape.ExtentMinMeters, 6);
        Assert.InRange(shape.ExtentMaxMeters, 2399.0, 2401.0);
    }

    [Fact]
    public void Membership_uses_the_half_width_plus_the_callers_margin()
    {
        var shape = RunwayShape.For(Runway());

        Assert.True(shape.Contains(Lat(29.0), Lon(1000.0), 0.0));
        Assert.False(shape.Contains(Lat(31.0), Lon(1000.0), 0.0));
        Assert.True(shape.Contains(Lat(-34.0), Lon(1000.0), 5.0));
        Assert.False(shape.Contains(Lat(-36.0), Lon(1000.0), 5.0));
    }

    [Fact]
    public void Lateral_sign_is_consistent_and_along_is_unclamped()
    {
        var shape = RunwayShape.For(Runway());
        var north = shape.Project(Lat(40.0), Lon(-100.0));
        var south = shape.Project(Lat(-40.0), Lon(3100.0));

        Assert.True(Math.Sign(north.Lateral) == -Math.Sign(south.Lateral));
        Assert.InRange(north.Along, -100.5, -99.5);
        Assert.InRange(south.Along, 3099.0, 3101.0);
        Assert.InRange(Math.Abs(north.Lateral), 39.9, 40.1);
    }

    [Fact]
    public void Clear_of_the_runway_needs_the_10_metre_margin_beyond_the_edge()
    {
        var shape = RunwayShape.For(Runway());
        Assert.False(shape.IsClearOf(35.0));
        Assert.False(shape.IsClearOf(-40.0));
        Assert.True(shape.IsClearOf(40.5));
        Assert.True(shape.IsClearOf(-41.0));
    }

    [Fact]
    public void NameAt_names_the_nearer_end()
    {
        var shape = RunwayShape.For(Runway());
        Assert.Equal("09", shape.NameAt(1499.0));
        Assert.Equal("27", shape.NameAt(1501.0));
    }

    [Fact]
    public void NameAt_falls_back_to_the_other_name_when_one_is_empty()
    {
        var cl = Runway();
        cl.Name2 = "";
        Assert.Equal("09", RunwayShape.For(cl).NameAt(2900.0));
    }

    [Fact]
    public void A_name_swapped_pair_is_named_by_the_physical_pavement_end()
    {
        // AYCH shape: the row labelled "09" sits at the FAR end. Build orients Pavement1 to Name1
        // from the runway table, so the pavement end named 09 is still the real 09 threshold and
        // the rows stay on the axis, so the pavement is usable and naming is physical.
        var cl = Runway(row1: 3000.0, row2: 0.0);
        var shape = RunwayShape.For(cl);

        Assert.True(shape.UsesPavement);
        Assert.Equal("09", shape.NameAt(shape.Project(Lat(0.0), Lon(200.0)).Along));
    }

    [Fact]
    public void A_degenerate_runway_contains_nothing()
    {
        var cl = new TaxiGraph.RunwayCenterline { Name1 = "09", Name2 = "27", Lat1 = BaseLat, Lon1 = BaseLon, Lat2 = BaseLat, Lon2 = BaseLon };
        var shape = RunwayShape.For(cl);

        Assert.True(shape.IsDegenerate);
        Assert.False(shape.Contains(BaseLat, BaseLon, 0.0));
    }
}
