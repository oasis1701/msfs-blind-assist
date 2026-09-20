using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

public class AirportFacilitiesTests
{
    // The REAL fs2024 box for KTIW: 457 m wide, its east edge exactly the outermost taxi path.
    private static AirportFacilities Ktiw() => new()
    { Icao = "KTIW", LeftLon = -122.579353, RightLon = -122.573303, TopLat = 47.274742, BottomLat = 47.260826 };

    [Fact]
    public void A_perimeter_building_is_outside_the_bare_box_and_inside_it_with_a_margin()
    {
        var box = Ktiw();
        Assert.False(box.ContainsPoint(47.2712, -122.5731));          // the tower: 15 m east of the box
        Assert.True(box.ContainsPoint(47.2712, -122.5731, 500));
        Assert.False(box.ContainsPoint(47.2712, -122.5600, 500));     // ~1 km east: still out
        Assert.False(box.ContainsPoint(47.2900, -122.5760, 500));     // ~1.7 km north: still out
    }

    private static AirportFacilities With(params ComFrequency[] coms)
    {
        var f = new AirportFacilities { Icao = "X" };
        f.Coms.AddRange(coms);
        return f;
    }

    [Fact]
    public void A_single_row_per_type_reads_as_before()
    {
        var f = new AirportFacilities { Icao = "KTIW", HasAvgas = true, HasJetFuel = true };
        f.Coms.AddRange(new[] { new ComFrequency("ATIS", 124050000, "KTIW"), new ComFrequency("G", 121800000, "TACOMA"),
                                new ComFrequency("T", 118500000, "TACOMA"), new ComFrequency("UC", 122950000, "TACOMA") });
        Assert.Equal("Avgas and jet fuel. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95.", f.DescribeFacts());
    }

    [Fact]
    public void Apron_control_is_not_read_out_as_Ground_when_a_plain_ground_row_exists()
    {
        var f = With(new ComFrequency("G", 121655000, "FRANKFURT APRON"), new ComFrequency("G", 121805000, "FRANKFURT"));
        Assert.Equal("Ground 121.805 (2 listed).", f.DescribeFacts());
    }

    [Fact]
    public void A_frequency_outside_the_com_band_is_never_read_out()
    {
        // EGLL lists VOR-broadcast ATIS at 113.75 / 117.0 beside the real one.
        var f = With(new ComFrequency("ATIS", 113750000, "HEATHROW"), new ComFrequency("ATIS", 128080000, "HEATHROW INFO"));
        Assert.Equal("ATIS 128.08.", f.DescribeFacts());
    }

    [Fact]
    public void Several_rows_of_one_type_say_how_many()
    {
        var f = With(new ComFrequency("T", 119100000, "ATLANTA"), new ComFrequency("T", 119500000, "ATLANTA"), new ComFrequency("T", 123850000, "ATLANTA"));
        Assert.Equal("Tower 119.1 (3 listed).", f.DescribeFacts());
    }
}
