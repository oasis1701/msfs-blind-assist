using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class PlaceListBuilderTests
{
    private static AirportFeature Place(FeatureKind k, string name, double lat, double lon, bool generic = false, params (double, double)[] members)
        => new() { Kind = k, Name = name, NameIsGeneric = generic, Lat = lat, Lon = lon, Source = FeatureSource.Osm,
                   Members = members.Length == 0 ? null : members.Select(m => new LatLon(m.Item1, m.Item2)).ToList() };
    private static StandCandidate Stand(string name, int number, int type, double lat, double lon, int node = 7, string gsxId = "")
        => new(new ParkingSpot { Name = name, Number = number, Type = type, Latitude = lat, Longitude = lon, Heading = 90, GsxIdentifier = gsxId }, node);
    private static AirportFeatureCatalog Cat(params AirportFeature[] f) => AirportFeatureCatalog.Build("X", "v", f);
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
}
