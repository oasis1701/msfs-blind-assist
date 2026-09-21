using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class NavdataFeatureSourceTests
{
    private static ParkingSpot Spot(string name, int number, int type, double lat, double lon, string airlines = "")
        => new() { Name = name, Number = number, Type = type, Latitude = lat, Longitude = lon, AirlineCodes = airlines };

    [Fact]
    public void Two_or_more_lettered_gates_become_a_concourse_at_their_centroid()
    {
        // 111 m per hop (well inside GateLinkMetres) so all three single-link into one cluster;
        // the old 222 m spacing relied on the un-clustered name-wide grouping this PR replaces.
        var spots = new List<ParkingSpot>
        {
            Spot("B", 1, 10, 33.640, -84.430, "DAL"),
            Spot("B", 2, 10, 33.641, -84.430, "DAL"),
            Spot("B", 3, 11, 33.642, -84.430, "AAL"),
            Spot("C", 9, 10, 33.650, -84.420),           // alone: no concourse
        };
        var features = NavdataFeatureSource.Read(spots, null);
        var b = Assert.Single(features, f => f.Kind == FeatureKind.Concourse);
        Assert.Equal("Concourse B", b.Name);
        Assert.InRange(b.Lat, 33.6409, 33.6411);
        Assert.Equal("Delta gates", b.Detail);          // 2 of 3 coded gates = 67 % ≥ 60 %
        Assert.Equal(FeatureSource.Navdata, b.Source);
        Assert.Equal(3, b.Members!.Count);
        Assert.False(b.NameIsGeneric);
    }

    [Fact]
    public void Airline_detail_is_omitted_below_the_majority_threshold()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("A", 1, 10, 33.640, -84.430, "DAL"),
            Spot("A", 2, 10, 33.641, -84.430, "AAL"),
        };
        var a = Assert.Single(NavdataFeatureSource.Read(spots, null));
        Assert.Null(a.Detail);
    }

    [Fact]
    public void Directional_ramps_become_named_aprons()
    {
        var spots = new List<ParkingSpot> { Spot("North", 1, 4, 47.27, -122.57), Spot("North", 2, 4, 47.271, -122.57) };
        var apron = Assert.Single(NavdataFeatureSource.Read(spots, null));
        Assert.Equal(FeatureKind.Apron, apron.Kind);
        Assert.Equal("North ramp", apron.Name);
    }

    [Fact]
    public void Fuel_stands_cluster_into_one_fuel_feature_and_only_a_single_flag_names_a_grade()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("Parking", 1, 16, 47.2700, -122.5700),
            Spot("Parking", 2, 16, 47.2701, -122.5700),   // ~11 m away → same cluster
            Spot("Parking", 3, 16, 47.2750, -122.5700),   // 550 m away → second cluster
        };
        var fac = new AirportFacilities { Icao = "KTIW", HasAvgas = true, HasJetFuel = true };
        var fuel = NavdataFeatureSource.Read(spots, fac).Where(f => f.Kind == FeatureKind.Fuel).ToList();
        Assert.Equal(2, fuel.Count);
        Assert.All(fuel, f => Assert.Equal("Fuel", f.Name));
        // Both flags together grade nothing on an MSFS 2024 database (they are all-or-nothing
        // there — see AirportFacilities.DescribeFacts), and the feature is already called "Fuel",
        // so there is nothing left to add. One flag alone still names its grade.
        Assert.All(fuel, f => Assert.Null(f.Detail));

        var avgasOnly = new AirportFacilities { Icao = "KTIW", HasAvgas = true };
        Assert.Equal("avgas", NavdataFeatureSource.Read(spots, avgasOnly).First(f => f.Kind == FeatureKind.Fuel).Detail);
        var jetOnly = new AirportFacilities { Icao = "KTIW", HasJetFuel = true };
        Assert.Equal("jet fuel", NavdataFeatureSource.Read(spots, jetOnly).First(f => f.Kind == FeatureKind.Fuel).Detail);
    }

    [Fact]
    public void Cargo_and_ga_ramps_cluster_and_vehicles_are_ignored()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("Parking", 1, 6, 33.62, -84.44), Spot("Parking", 2, 6, 33.6201, -84.44),
            Spot("Parking", 3, 3, 47.27, -122.58), Spot("Parking", 4, 3, 47.2701, -122.58), Spot("Parking", 5, 4, 47.2702, -122.58),
            Spot("Parking", 6, 17, 47.27, -122.58),
        };
        var features = NavdataFeatureSource.Read(spots, null);
        Assert.Single(features, f => f.Kind == FeatureKind.Cargo && f.Name == "Cargo ramp");
        Assert.Single(features, f => f.Kind == FeatureKind.Apron && f.Name == "GA ramp");
        Assert.DoesNotContain(features, f => f.Kind == FeatureKind.Other);
    }

    [Fact]
    public void Helipads_come_from_facilities_and_are_numbered_only_when_several()
    {
        var one = new AirportFacilities { Icao = "X", Helipads = { new LatLon(1, 1) } };
        Assert.Equal("Helipad", Assert.Single(NavdataFeatureSource.Read(new List<ParkingSpot>(), one)).Name);
        var two = new AirportFacilities { Icao = "X", Helipads = { new LatLon(1, 1), new LatLon(1.001, 1) } };
        var names = NavdataFeatureSource.Read(new List<ParkingSpot>(), two).Select(f => f.Name).ToList();
        Assert.Equal(new[] { "Helipad 1", "Helipad 2" }, names);
    }

    [Fact]
    public void Facts_line_lists_fuel_and_the_common_frequencies_in_mhz()
    {
        var fac = new AirportFacilities
        {
            Icao = "KTIW", HasAvgas = true, HasJetFuel = false,
            Coms = { new ComFrequency("T", 118500000, "TACOMA"), new ComFrequency("G", 121800000, "TACOMA"),
                     new ComFrequency("ATIS", 124050000, "KTIW"), new ComFrequency("UC", 122950000, "TACOMA"),
                     new ComFrequency("D", 120100000, "SEATTLE") }
        };
        Assert.Equal("Avgas. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95.", fac.DescribeFacts());
        Assert.Equal("", new AirportFacilities { Icao = "X" }.DescribeFacts());
    }

    [Fact]
    public void A_letter_reused_by_two_terminals_is_two_concourses_not_one_phantom_between_them()
    {
        // KJFK with GSX lettering: T4-B and T8-B are 1.3 km apart. One name-wide centroid put
        // "Concourse B" 179 m from any gate.
        var spots = new List<ParkingSpot>();
        for (int i = 0; i < 6; i++) spots.Add(Spot("B", 20 + i, 10, 40.6400 + i * 0.0004, -73.7800));
        for (int i = 0; i < 6; i++) spots.Add(Spot("B", 1 + i, 10, 40.6400 + i * 0.0004, -73.7950));   // ~1.27 km west
        var bs = NavdataFeatureSource.Read(spots, null).Where(f => f.Kind == FeatureKind.Concourse).ToList();
        Assert.Equal(2, bs.Count);
        Assert.All(bs, b => { Assert.Equal("Concourse B", b.Name); Assert.Equal(6, b.Members!.Count); Assert.False(b.NameIsGeneric); });
        Assert.All(bs, b => Assert.True(b.Members!.Min(m => TaxiGeo.HaversineMeters(b.Lat, b.Lon, m.Lat, m.Lon)) < 60));
    }

    [Fact]
    public void One_long_pier_is_one_concourse()
    {
        var spots = Enumerable.Range(0, 12).Select(i => Spot("C", i + 1, 10, 33.6400 + i * 0.0005, -84.4300)).ToList();   // 55 m apart, ~610 m long
        var only = Assert.Single(NavdataFeatureSource.Read(spots, null), f => f.Kind == FeatureKind.Concourse);
        Assert.Equal(12, only.Members!.Count);
    }

    [Fact]
    public void One_contiguous_GA_ramp_is_one_feature_not_one_per_60_metres()
    {
        var spots = Enumerable.Range(0, 12).Select(i => Spot("Parking", i + 1, 4, 40.8500, -96.7600 + i * 0.0006)).ToList();   // ~50 m apart, 550 m long
        var ramp = Assert.Single(NavdataFeatureSource.Read(spots, null), f => f.Kind == FeatureKind.Apron);
        Assert.Equal("GA ramp", ramp.Name);
        Assert.True(ramp.NameIsGeneric);
        Assert.Equal(12, ramp.Members!.Count);
    }

    [Fact]
    public void A_directional_name_used_on_two_aprons_is_two_ramps()
    {
        var spots = new List<ParkingSpot> { Spot("North", 1, 4, 32.0100, 34.8800), Spot("North", 2, 4, 32.0104, 34.8800),
                                            Spot("North", 3, 4, 32.0300, 34.8800), Spot("North", 4, 4, 32.0304, 34.8800) };
        Assert.Equal(2, NavdataFeatureSource.Read(spots, null).Count(f => f.Name == "North ramp"));
    }

    [Fact]
    public void The_tower_comes_from_navdata_when_navdata_has_it()
    {
        var with = new AirportFacilities { Icao = "KTIW", TowerLat = 47.269379, TowerLon = -122.574707 };
        var tower = Assert.Single(NavdataFeatureSource.Read(Array.Empty<ParkingSpot>(), with), f => f.Kind == FeatureKind.Tower);
        Assert.Equal("Control tower", tower.SpokenName);
        Assert.False(tower.HasProperName);
        Assert.DoesNotContain(NavdataFeatureSource.Read(Array.Empty<ParkingSpot>(), new AirportFacilities { Icao = "KATL" }), f => f.Kind == FeatureKind.Tower);
    }
}
