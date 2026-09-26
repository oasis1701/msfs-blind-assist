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
    public void Every_frequency_is_its_own_row_in_the_order_a_pilot_uses_them()
    {
        // KMEM's own rows (fs2024), shuffled. The one-line summary this replaced read only the first
        // tower and ground frequency ("Tower 118.3 (3 listed)") and left clearance delivery out.
        var f = With(new ComFrequency("T", 118300000, "MEMPHIS"), new ComFrequency("UC", 122950000, "MEMPHIS"),
                     new ComFrequency("A", 119100000, "MEMPHIS"), new ComFrequency("G", 121000000, "MEMPHIS"),
                     new ComFrequency("ASOS", 127750000, "KMEM"), new ComFrequency("C", 125200000, "MEMPHIS"),
                     new ComFrequency("T", 119700000, "MEMPHIS"), new ComFrequency("D", 124150000, "MEMPHIS"),
                     new ComFrequency("ATIS", 127750000, "KMEM"), new ComFrequency("G", 121900000, "MEMPHIS"));
        Assert.Equal(new[]
        {
            "ATIS 127.75", "Clearance delivery 125.2", "Ground 121.0", "Ground 121.9", "Tower 118.3", "Tower 119.7",
            "Departure 124.15", "Approach 119.1", "UNICOM 122.95", "ASOS 127.75",
        }, f.DescribeFacts().Frequencies.Select(r => r.Text));
    }

    [Fact]
    public void Each_row_carries_the_frequency_Enter_tunes()
    {
        // The value travels with the row: nothing parses "Clearance delivery 125.2" back into Hz.
        var f = With(new ComFrequency("C", 125200000, "MEMPHIS"), new ComFrequency("T", 128425000, "MEMPHIS"));
        Assert.Equal(new[] { new FrequencyRow("Clearance delivery 125.2", 125200000), new FrequencyRow("Tower 128.425", 128425000) },
                     f.DescribeFacts().Frequencies);
    }

    [Fact]
    public void Both_fuel_flags_together_say_only_that_there_is_fuel()
    {
        // Measured on fs2024 (2026-09-21): the two flags are ALL-OR-NOTHING there — 17,079 airports
        // carry both, 67,199 neither, not one carries a single flag — so "both" grades nothing, and
        // "Avgas and jet fuel." was spoken at 1,147 fields with no hard runway and a longest runway
        // under 2,500 ft (4II2 "Hangar Fly Ultralight Fly Club", 965 ft). A disk-built MSFS 2020
        // database sets the two independently, so one flag on its own still names its grade.
        Assert.Equal("Fuel available", new AirportFacilities { HasAvgas = true, HasJetFuel = true }.DescribeFacts().Fuel);
        Assert.Equal("Avgas available", new AirportFacilities { HasAvgas = true }.DescribeFacts().Fuel);
        Assert.Equal("Jet fuel available", new AirportFacilities { HasJetFuel = true }.DescribeFacts().Fuel);
        Assert.True(new AirportFacilities().DescribeFacts().IsEmpty);
    }

    [Fact]
    public void A_gates_frequency_is_listed_after_the_ground_controllers_own_and_named()
    {
        // KMIA, replayed on the real data: its nine G rows begin with "MIAMI GATES" at 120.35. The
        // names differ within the kind, so each row says whose frequency it is.
        var f = With(new ComFrequency("G", 120350000, "MIAMI GATES"), new ComFrequency("G", 121800000, "MIAMI"),
                     new ComFrequency("G", 128025000, "MIAMI GATES"), new ComFrequency("G", 132375000, "MIAMI GATES"));
        Assert.Equal(new[] { "Ground 121.8, MIAMI", "Ground 120.35, MIAMI GATES", "Ground 128.025, MIAMI GATES", "Ground 132.375, MIAMI GATES" },
                     f.DescribeFacts().Frequencies.Select(r => r.Text));
    }

    [Fact]
    public void Apron_control_is_listed_after_ground_and_named()
    {
        var f = With(new ComFrequency("G", 121655000, "FRANKFURT APRON"), new ComFrequency("G", 121805000, "FRANKFURT"));
        Assert.Equal(new[] { "Ground 121.805, FRANKFURT", "Ground 121.655, FRANKFURT APRON" }, f.DescribeFacts().Frequencies.Select(r => r.Text));
    }

    [Fact]
    public void A_name_is_given_only_where_it_tells_the_rows_of_one_kind_apart()
    {
        // KATL's ramp control rows are ground frequencies named differently from the controller's;
        // its three towers all share "ATLANTA", which tells them apart from nothing.
        var f = With(new ComFrequency("G", 121900000, "ATLANTA"), new ComFrequency("G", 129250000, "RAMP CONTROL"),
                     new ComFrequency("T", 119100000, "ATLANTA"), new ComFrequency("T", 119500000, "ATLANTA"), new ComFrequency("T", 123850000, "ATLANTA"));
        Assert.Equal(new[] { "Ground 121.9, ATLANTA", "Ground 129.25, RAMP CONTROL", "Tower 119.1", "Tower 119.5", "Tower 123.85" },
                     f.DescribeFacts().Frequencies.Select(r => r.Text));
    }

    [Fact]
    public void A_frequency_outside_the_com_band_is_never_listed()
    {
        // EGLL lists VOR-broadcast ATIS at 113.75 / 117.0 beside the real one. The name contrast is
        // judged on the listed rows only, so the one ATIS left carries no name.
        var f = With(new ComFrequency("ATIS", 113750000, "HEATHROW"), new ComFrequency("ATIS", 128080000, "HEATHROW INFO"));
        Assert.Equal(new[] { "ATIS 128.08" }, f.DescribeFacts().Frequencies.Select(r => r.Text));
    }

    [Fact]
    public void A_duplicate_row_and_an_unknown_type_are_not_listed()
    {
        var f = With(new ComFrequency("T", 118500000, "TACOMA"), new ComFrequency("T", 118500000, "TACOMA"),
                     new ComFrequency("XYZ", 123450000, "TACOMA"));
        Assert.Equal(new[] { "Tower 118.5" }, f.DescribeFacts().Frequencies.Select(r => r.Text));
    }
}
