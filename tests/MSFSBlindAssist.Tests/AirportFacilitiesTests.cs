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
        Assert.Equal("Fuel available. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95.", f.DescribeFacts());
    }

    [Fact]
    public void Both_fuel_flags_together_say_only_that_there_is_fuel()
    {
        // Measured on fs2024 (2026-09-21): the two flags are ALL-OR-NOTHING there — 17,079 airports
        // carry both, 67,199 neither, not one carries a single flag — so "both" grades nothing, and
        // "Avgas and jet fuel." was spoken at 1,147 fields with no hard runway and a longest runway
        // under 2,500 ft (4II2 "Hangar Fly Ultralight Fly Club", 965 ft). A disk-built MSFS 2020
        // database sets the two independently, so one flag on its own still names its grade.
        Assert.Equal("Fuel available.", new AirportFacilities { HasAvgas = true, HasJetFuel = true }.DescribeFacts());
        Assert.Equal("Avgas.", new AirportFacilities { HasAvgas = true }.DescribeFacts());
        Assert.Equal("Jet fuel.", new AirportFacilities { HasJetFuel = true }.DescribeFacts());
        Assert.Equal("", new AirportFacilities().DescribeFacts());
    }

    [Fact]
    public void A_gates_frequency_is_not_read_out_as_Ground_when_a_plain_ground_row_exists()
    {
        // KMIA, replayed on the real data: its nine G rows begin with "MIAMI GATES" at 120.35, so
        // the readout named the ramp-gates frequency as the ground controller's.
        var f = With(new ComFrequency("G", 120350000, "MIAMI GATES"), new ComFrequency("G", 121800000, "MIAMI"),
                     new ComFrequency("G", 128025000, "MIAMI GATES"), new ComFrequency("G", 132375000, "MIAMI GATES"));
        Assert.Equal("Ground 121.8 (4 listed).", f.DescribeFacts());
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
