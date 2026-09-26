using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class PlaceListBuilderTests
{
    private static AirportFeature Place(FeatureKind k, string name, double lat, double lon, bool generic = false, params (double, double)[] members)
        => new() { Kind = k, Name = name, NameIsGeneric = generic, Lat = lat, Lon = lon, Source = FeatureSource.Osm,
                   Members = members.Length == 0 ? null : members.Select(m => new LatLon(m.Item1, m.Item2)).ToList() };
    private static StandCandidate Stand(string name, int number, int type, double lat, double lon, int node = 7, string gsxId = "")
        => new(new ParkingSpot { Name = name, Number = number, Type = type, Latitude = lat, Longitude = lon, Heading = 90, GsxIdentifier = gsxId }, node);
    private static AirportFeatureCatalog Cat(params AirportFeature[] f) => AirportFeatureCatalog.Build("v", f);
    private static readonly StandCandidate[] None = Array.Empty<StandCandidate>();
    private static NearestNode? NoNode(double lat, double lon) => null;
    private static bool Any(ParkingSpot s) => true;

    [Fact]
    public void A_place_takes_the_stand_beside_it_never_a_namesake_elsewhere()
    {
        // KMEM: gate 1 and a military-cargo ramp spot 1 both map to the identity ("", 1, "") — 2.2 km apart.
        var ramp = Stand("", 1, 7, 35.02839, -89.96750, node: 11);
        var gate = Stand("", 1, 10, 35.04361, -89.98231, node: 42, gsxId: " Gate 1");
        var terminal = Place(FeatureKind.Terminal, "Terminal B", 35.04370, -89.98240);
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(terminal), new[] { ramp, gate }, None, NoNode, Any));
        Assert.Same(gate.Spot, entry.Spot);          // the very object the Gate list would offer: identifier, stop position and all
        Assert.Equal(42, entry.NodeId);
        Assert.Equal(90, entry.HeadingDeg);
        Assert.Equal((35.04361, -89.98231), (entry.Lat, entry.Lon));   // the stand's own position, like the Gate branch — never the stop, never the building
    }

    [Fact]
    public void The_matching_stand_type_beats_a_nearer_stand_of_another_type()
    {
        var gate = Stand("A", 1, 10, 47.2701, -122.5700);          // ~11 m away
        var gaRamp = Stand("Parking", 12, 4, 47.2705, -122.5700);  // ~55 m away
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(Place(FeatureKind.Fbo, "Narrows Aviation", 47.2700, -122.5700)), new[] { gate, gaRamp }, None, NoNode, Any));
        Assert.Same(gaRamp.Spot, entry.Spot);
        Assert.Equal("Narrows Aviation, FBO, Parking 12", entry.Label);
    }

    [Fact]
    public void A_stand_GSX_does_not_list_is_still_reachable_and_carries_no_GSX_identity()
    {
        var fuelStand = Stand("Parking", 0, 16, 47.2702, -122.5700);            // GSX drops fuel stands; navdata has it
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(Place(FeatureKind.Fuel, "Fuel", 47.2700, -122.5700, generic: true)), None, new[] { fuelStand }, NoNode, Any));
        Assert.Same(fuelStand.Spot, entry.Spot);
        Assert.True(string.IsNullOrEmpty(entry.Spot!.GsxIdentifier));
        Assert.Equal("Fuel, Parking", entry.Label);                              // not "Fuel, fuel, Parking 0"
    }

    [Fact]
    public void With_no_stand_in_reach_a_place_ends_at_a_taxi_node_and_has_NO_heading()
    {
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(Place(FeatureKind.Hangar, "Cessna Service Hangar", 47.2700, -122.5700)), None, None,
            (lat, lon) => new NearestNode(99, 47.2704, -122.5700, 44.0), Any));
        Assert.Null(entry.Spot);
        Assert.Null(entry.HeadingDeg);        // no "align with" toward a building: arrive and stop
        Assert.Equal(99, entry.NodeId);
        Assert.Equal("Cessna Service Hangar, nearest taxiway point", entry.Label);   // the node is any routable one in reach, not a taxiway END
        Assert.Empty(PlaceListBuilder.Build(Cat(Place(FeatureKind.Hangar, "Far Hangar", 47.2700, -122.5700)), None, None,
            (lat, lon) => new NearestNode(99, 47.2720, -122.5700, 222.0), Any));          // beyond 100 m: not routable
    }

    [Theory]
    [InlineData(FeatureKind.DeicePad)] [InlineData(FeatureKind.Tower)] [InlineData(FeatureKind.Apron)] [InlineData(FeatureKind.Helipad)]
    public void Kinds_you_do_not_taxi_to_are_never_places(FeatureKind kind)
        => Assert.Empty(PlaceListBuilder.Build(Cat(Place(kind, "X", 47.2700, -122.5700)), new[] { Stand("A", 1, 10, 47.2701, -122.5700) }, None, NoNode, Any));

    [Fact]
    public void A_stand_the_filters_reject_or_with_no_taxi_node_is_passed_over()
    {
        var tooSmall = Stand("A", 1, 10, 47.2701, -122.5700);
        var unreachable = Stand("A", 2, 10, 47.2702, -122.5700, node: -1);
        var fits = Stand("A", 3, 10, 47.2706, -122.5700);
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(Place(FeatureKind.Terminal, "Terminal 1", 47.2700, -122.5700)),
            new[] { tooSmall, unreachable, fits }, None, NoNode, s => s.Number != 1));
        Assert.Same(fits.Spot, entry.Spot);
    }

    [Fact]
    public void A_concourse_resolves_to_one_of_ITS_OWN_gates_not_a_nearer_neighbour()
    {
        // CYYZ "Concourse A" resolved to "B 14": the nearest stand to a centroid need not be a member.
        var b14 = Stand("B", 14, 10, 43.6800, -79.6120);
        var a2 = Stand("A", 2, 10, 43.6790, -79.6120);
        var a9 = Stand("A", 9, 10, 43.6812, -79.6120);
        var concourseA = Place(FeatureKind.Concourse, "Concourse A", 43.6801, -79.6120, false, (43.6790, -79.6120), (43.6812, -79.6120));
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(concourseA), new[] { b14, a2, a9 }, None, NoNode, Any));
        Assert.Equal("A", entry.Spot!.Name);
    }

    [Fact]
    public void Labels_survive_the_spaced_dash_cut_and_duplicates_are_numbered_not_dropped()
    {
        var s1 = Stand("B", 25, 10, 40.6401, -73.7800); var s2 = Stand("", 3, 4, 40.6501, -73.7800); var s3 = Stand("", 4, 4, 40.6601, -73.7800);
        var entries = PlaceListBuilder.Build(Cat(
            Place(FeatureKind.Concourse, "Terminal 4 - Concourse B", 40.6400, -73.7800),
            Place(FeatureKind.Hangar, "", 40.6500, -73.7800), Place(FeatureKind.Hangar, "", 40.6600, -73.7800)),
            new[] { s1, s2, s3 }, None, NoNode, Any);
        Assert.Contains(entries, e => e.Label == "Terminal 4, Concourse B, B 25");       // no " - ": RouteReachabilityMessages cuts a label there
        Assert.Contains(entries, e => e.Label == "Hangar, Spot 3");
        Assert.Contains(entries, e => e.Label == "Hangar, Spot 4");
        Assert.Equal(entries.Count, entries.Select(e => e.Label).Distinct().Count());
    }

    [Fact]
    public void Two_places_that_would_read_the_same_are_both_listed()
    {
        var stand = Stand("", 3, 4, 40.6501, -73.7800);
        var entries = PlaceListBuilder.Build(Cat(Place(FeatureKind.Hangar, "", 40.6500, -73.7800), Place(FeatureKind.Hangar, "", 40.65005, -73.78060)),
            new[] { stand }, None, NoNode, Any);
        Assert.Equal(new[] { "Hangar, Spot 3", "Hangar, Spot 3 (2)" }, entries.Select(e => e.Label).OrderBy(l => l));
    }

    [Fact]
    public void A_cargo_place_prefers_a_civil_cargo_stand_over_a_nearer_military_one()
    {
        // navdata type 7 is RAMP_MIL_CARGO — a MILITARY stand, not the cargo ramp a Cargo place is
        // about. Counted as cargo it outranked the civil stand on distance alone.
        var military = Stand("Parking", 18, 7, 21.3308, -157.9469);   // ~11 m from the building
        var civil = Stand("Parking", 5, 6, 21.3312, -157.9469);       // ~55 m
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(Place(FeatureKind.Cargo, "UPS Cargo", 21.3307, -157.9469)),
            new[] { military, civil }, None, NoNode, Any));
        Assert.Same(civil.Spot, entry.Spot);
    }

    // A place must never END on a hold line. Hold-short identity is navdata's endpoint type, never
    // TaxiNode.Type: the parking pass stamps the node nearest a stand "Parking" whatever it was, so a
    // hold-short node 10 m from a stand with no lead-in of its own reads as Parking.
    [Fact]
    public void A_hold_short_node_stamped_parking_is_still_never_a_place_end()
    {
        const double lat = 52.0, lon = 4.0, mPerDeg = 111_320.0;
        var paths = new List<TaxiPath>
        {
            new() { Name = "A", Type = "T", Width = 75, StartType = "N", EndType = "HS",
                    StartLat = lat - 200 / mPerDeg, StartLon = lon, EndLat = lat, EndLon = lon },
        };
        double standLon = lon + 10 / (mPerDeg * Math.Cos(lat * Math.PI / 180));
        var stand = new ParkingSpot { Name = "A", Number = 9, Latitude = lat, Longitude = standLon };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot> { stand }, new List<StartPosition>(), new List<Runway>());
        var holdNode = g.FindNearestNode(lat, lon)!;
        Assert.Equal(TaxiNodeType.Parking, holdNode.Type);            // the stamp this test is about

        var n = PlaceListBuilder.NearestRoutableNode(g, lat, lon);

        Assert.Null(n);
    }

    private static AirportFeature Nav(FeatureKind k, string name, bool generic, params (double Lat, double Lon)[] members)
    {
        var pts = members.Select(m => new LatLon(m.Lat, m.Lon)).ToList();
        return new() { Kind = k, Name = name, NameIsGeneric = generic, Lat = pts.Average(p => p.Lat), Lon = pts.Average(p => p.Lon),
                       Source = FeatureSource.Navdata, Members = pts };
    }

    [Fact]
    public void A_cargo_ramp_or_concourse_only_navdata_describes_is_not_a_place()
    {
        // KMEM listed 40 "Cargo ramp" places (live 2026-09-26): one per cluster of its 156 unnamed
        // cargo stands, each routed to the cluster's central stand — a stand the Gate / Parking list
        // already offers, under a vaguer name. A concourse inferred from gate letters is the same.
        var cargo = new[] { Stand("", 1, 6, 35.0400, -89.9700), Stand("", 2, 6, 35.0402, -89.9700) };
        var gates = new[] { Stand("B", 1, 10, 35.0500, -89.9800), Stand("B", 2, 10, 35.0502, -89.9800) };
        var catalog = Cat(Nav(FeatureKind.Cargo, "Cargo ramp", true, (35.0400, -89.9700), (35.0402, -89.9700)),
                          Nav(FeatureKind.Concourse, "Concourse B", false, (35.0500, -89.9800), (35.0502, -89.9800)));
        Assert.Empty(PlaceListBuilder.Build(catalog, cargo.Concat(gates).ToList(), None, NoNode, Any));
    }

    [Fact]
    public void Navdata_fuel_stays_a_place()
    {
        // With GSX supplying the gate list, fuel stands are not in it: a fuel place is the only route there.
        var fuelStand = Stand("", 0, 16, 47.2702, -122.5700);
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(Nav(FeatureKind.Fuel, "Fuel", true, (47.2702, -122.5700))),
            None, new[] { fuelStand }, NoNode, Any));
        Assert.Same(fuelStand.Spot, entry.Spot);
    }

    [Theory]
    [InlineData(FeatureSource.Osm)] [InlineData(FeatureSource.Scenery)] [InlineData(FeatureSource.Gsx)]
    public void A_cargo_ramp_or_concourse_with_a_real_name_from_another_source_stays_a_place(FeatureSource source)
    {
        var stands = new[] { Stand("", 1, 6, 35.0400, -89.9700), Stand("B", 1, 10, 35.0500, -89.9800) };
        var fedex = new AirportFeature { Kind = FeatureKind.Cargo, Name = "FedEx World Hub", Lat = 35.0401, Lon = -89.9700, Source = source };
        var pier = new AirportFeature { Kind = FeatureKind.Concourse, Name = "Concourse B", Lat = 35.0501, Lon = -89.9800, Source = source };
        Assert.Equal(2, PlaceListBuilder.Build(Cat(fedex, pier), stands, None, NoNode, Any).Count);

        // The same source's bare kind word ("Cargo ramp") names nothing the gate list cannot.
        var bare = new AirportFeature { Kind = FeatureKind.Cargo, Name = "Cargo ramp", NameIsGeneric = true, Lat = 35.0401, Lon = -89.9700, Source = source };
        Assert.Empty(PlaceListBuilder.Build(Cat(bare), stands, None, NoNode, Any));
    }

    [Fact]
    public void An_OSM_name_that_absorbs_a_navdata_concourse_keeps_the_place()
    {
        // The catalog merges the two into one feature under the OSM winner's name and source.
        var gates = new[] { Stand("B", 1, 10, 35.0500, -89.9800), Stand("B", 2, 10, 35.0502, -89.9800) };
        var navdata = Nav(FeatureKind.Concourse, "Concourse B", false, (35.0500, -89.9800), (35.0502, -89.9800));
        var osm = new AirportFeature { Kind = FeatureKind.Concourse, Name = "Concourse B", Lat = 35.0501, Lon = -89.9801, Source = FeatureSource.Osm };
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(navdata, osm), gates, None, NoNode, Any));
        Assert.Equal(FeatureSource.Osm, entry.Feature.Source);
    }

    [Fact]
    public void KMEM_shaped_navdata_alone_lists_its_fuel_and_no_cargo_or_concourse_places()
    {
        // End to end from the stands: NavdataFeatureSource makes a Cargo ramp, a Concourse B and a
        // Fuel feature; only the fuel one becomes a place.
        var spots = new List<ParkingSpot>
        {
            new() { Name = "", Number = 1, Type = 6, Latitude = 35.0400, Longitude = -89.9700, Heading = 90 },
            new() { Name = "", Number = 2, Type = 6, Latitude = 35.0402, Longitude = -89.9700, Heading = 90 },
            new() { Name = "B", Number = 1, Type = 10, Latitude = 35.0500, Longitude = -89.9800, Heading = 90 },
            new() { Name = "B", Number = 2, Type = 10, Latitude = 35.0502, Longitude = -89.9800, Heading = 90 },
            new() { Name = "", Number = 0, Type = 16, Latitude = 35.0450, Longitude = -89.9750, Heading = 90 },
        };
        var features = NavdataFeatureSource.Read(spots, null);
        Assert.Contains(features, f => f.Kind == FeatureKind.Cargo);
        Assert.Contains(features, f => f.Kind == FeatureKind.Concourse);
        var entries = PlaceListBuilder.Build(AirportFeatureCatalog.Build("v", features),
            spots.Select(s => new StandCandidate(s, 7)).ToList(), None, NoNode, Any);
        Assert.Equal(FeatureKind.Fuel, Assert.Single(entries).Feature.Kind);
    }

    [Fact]
    public void A_named_office_kind_place_is_not_labelled_airport_office()
    {
        // Office is the catch-all kind (office, admin, cafe, restaurant): live LOWI read
        // "Burkia Restaurant, airport office, Parking 14". The name says what it is.
        var restaurant = Place(FeatureKind.Office, "Burkia Restaurant", 0, 0);
        var entry = Assert.Single(PlaceListBuilder.Build(Cat(restaurant), new[] { Stand("Parking", 14, 4, 0, 0) }, None, NoNode, Any));
        Assert.Equal("Burkia Restaurant, Parking 14", entry.Label);
        // A model named only by its own word ("Restaurant", "Cafe") is generic, and says it just as well.
        var bare = Place(FeatureKind.Office, "Restaurant", 0, 0, generic: true);
        var bareEntry = Assert.Single(PlaceListBuilder.Build(Cat(bare), new[] { Stand("Parking", 14, 4, 0, 0) }, None, NoNode, Any));
        Assert.Equal("Restaurant, Parking 14", bareEntry.Label);
    }
}
