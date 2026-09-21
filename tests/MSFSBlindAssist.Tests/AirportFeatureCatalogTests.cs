using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class AirportFeatureCatalogTests
{
    private static AirportFeature F(FeatureKind k, string name, double lat, double lon, FeatureSource src, string? detail = null, IReadOnlyList<LatLon>? fp = null)
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = src, Detail = detail, Footprint = fp };

    private static AirportFeature N(FeatureKind k, string name, double lat, double lon, bool generic = false, FeatureSource src = FeatureSource.Navdata, params (double, double)[] members)
        => new() { Kind = k, Name = name, NameIsGeneric = generic, Lat = lat, Lon = lon, Source = src,
                   Members = members.Length == 0 ? null : members.Select(m => new LatLon(m.Item1, m.Item2)).ToList() };

    [Fact]
    public void Same_kind_within_radius_collapses_to_the_higher_rank()
    {
        var osm = F(FeatureKind.Tower, "Control Tower", 47.2700, -122.5700, FeatureSource.Osm);
        var scenery = F(FeatureKind.Tower, "Control Tower 1", 47.2705, -122.5700, FeatureSource.Scenery); // ~55 m
        var cat = AirportFeatureCatalog.Build("KTIW", "v", new[] { scenery, osm });
        var only = Assert.Single(cat.Features);
        Assert.Equal("Control Tower", only.Name);
        Assert.Equal(FeatureSource.Osm, only.Source);
    }

    [Fact]
    public void Loser_donates_footprint_and_detail_the_winner_lacks()
    {
        var square = new[] { new LatLon(0, 0), new LatLon(0, 0.001), new LatLon(0.001, 0.001), new LatLon(0.001, 0) };
        var navdata = F(FeatureKind.Concourse, "Concourse B", 0.0005, 0.0005, FeatureSource.Navdata, detail: "Delta gates");
        var osm = F(FeatureKind.Concourse, "Concourse B", 0.0004, 0.0005, FeatureSource.Osm, fp: square);
        var cat = AirportFeatureCatalog.Build("X", "v", new[] { navdata, osm });
        var only = Assert.Single(cat.Features);
        Assert.Equal(FeatureSource.Osm, only.Source);
        Assert.Equal("Delta gates", only.Detail);
        Assert.NotNull(only.Footprint);
    }

    [Fact]
    public void Concourses_with_a_matching_name_merge_beyond_the_plain_radius_but_not_without_limit()
    {
        // Concourse's plain merge radius is 150 m; its same-name radius is 300 m. This pair sits
        // in between (~250 m) so it only merges BECAUSE the names match — unlike the old rule,
        // which matched on the letter "regardless of distance" and is exactly the bug that let
        // two "Concourse B" piers 1.3 km apart at KJFK collapse into one.
        var a = F(FeatureKind.Concourse, "Concourse B", 33.6400, -84.430, FeatureSource.Navdata);
        var b = F(FeatureKind.Concourse, "Concourse B", 33.64225, -84.430, FeatureSource.Scenery); // ~250 m
        Assert.Single(AirportFeatureCatalog.Build("KATL", "v", new[] { a, b }).Features);
    }

    [Fact]
    public void Different_kinds_never_merge_and_unnamed_hangars_stay_separate_beyond_40m()
    {
        var h1 = F(FeatureKind.Hangar, "", 47.2700, -122.5700, FeatureSource.Osm);
        var h2 = F(FeatureKind.Hangar, "", 47.2705, -122.5700, FeatureSource.Osm);   // 55 m
        var fuel = F(FeatureKind.Fuel, "Fuel", 47.2700, -122.5700, FeatureSource.Navdata);
        Assert.Equal(3, AirportFeatureCatalog.Build("X", "v", new[] { h1, h2, fuel }).Features.Count);
    }

    [Fact]
    public void Named_beats_unnamed_within_a_source_and_unnamed_Other_is_dropped()
    {
        var unnamed = F(FeatureKind.Hangar, "", 47.27, -122.57, FeatureSource.Osm);
        var named = F(FeatureKind.Hangar, "ATP Hangar", 47.2701, -122.57, FeatureSource.Scenery);
        var junk = F(FeatureKind.Other, "", 47.28, -122.58, FeatureSource.Osm);
        var cat = AirportFeatureCatalog.Build("X", "v", new[] { unnamed, named, junk });
        Assert.Equal("ATP Hangar", Assert.Single(cat.Features).Name);
    }

    [Fact]
    public void Rank_prefers_named_then_source_order()
    {
        // Rank no longer collapses every unnamed feature to a flat 0 (it still carries a source
        // component), so this is pinned as an ORDERING, not the old literal 40/30/20/10/0 values.
        AirportFeature Named(FeatureSource src) => F(FeatureKind.Hangar, "N", 0, 0, src);
        var unnamed = F(FeatureKind.Hangar, "", 0, 0, FeatureSource.Osm);
        Assert.True(AirportFeatureCatalog.Rank(Named(FeatureSource.Osm)) > AirportFeatureCatalog.Rank(Named(FeatureSource.Scenery)));
        Assert.True(AirportFeatureCatalog.Rank(Named(FeatureSource.Scenery)) > AirportFeatureCatalog.Rank(Named(FeatureSource.Gsx)));
        Assert.True(AirportFeatureCatalog.Rank(Named(FeatureSource.Gsx)) > AirportFeatureCatalog.Rank(Named(FeatureSource.Navdata)));
        // Even the lowest-priority source, named, outranks the highest-priority source, unnamed.
        Assert.True(AirportFeatureCatalog.Rank(Named(FeatureSource.Navdata)) > AirportFeatureCatalog.Rank(unnamed));
    }

    [Fact]
    public void Features_are_sorted_by_kind_then_name_and_the_version_is_kept()
    {
        var cat = AirportFeatureCatalog.Build("X", "tok", new[] {
            F(FeatureKind.Hangar, "B Hangar", 1, 1, FeatureSource.Osm), F(FeatureKind.Concourse, "Concourse A", 2, 2, FeatureSource.Osm), F(FeatureKind.Hangar, "A Hangar", 3, 3, FeatureSource.Osm) });
        Assert.Equal("tok", cat.Version);
        Assert.Equal(new[] { "Concourse A", "A Hangar", "B Hangar" }, cat.Features.Select(f => f.Name));
    }

    [Fact]
    public void Differently_named_neighbours_are_never_collapsed()
    {
        // KMSP: inferred Concourse A and B centroids are 109 m apart — B used to vanish.
        var cat = AirportFeatureCatalog.Build("KMSP", "v", new[] { N(FeatureKind.Concourse, "Concourse A", 44.8800, -93.2100), N(FeatureKind.Concourse, "Concourse B", 44.8810, -93.2100) });
        Assert.Equal(new[] { "Concourse A", "Concourse B" }, cat.Features.Select(f => f.Name).OrderBy(n => n));
    }

    [Fact]
    public void Numbered_helipads_45_metres_apart_both_survive()
    {
        var cat = AirportFeatureCatalog.Build("KJAC", "v", new[] { N(FeatureKind.Helipad, "Helipad 1", 43.6000, -110.7400, true), N(FeatureKind.Helipad, "Helipad 2", 43.6004, -110.7400, true) });
        Assert.Equal(2, cat.Features.Count);
    }

    [Fact]
    public void Two_generic_pieces_of_one_ramp_merge_and_a_proper_name_absorbs_a_generic_one()
    {
        var ramps = AirportFeatureCatalog.Build("X", "v", new[] { N(FeatureKind.Apron, "GA ramp", 0, 0, true), N(FeatureKind.Apron, "GA ramp", 0.0003, 0, true) });
        Assert.Single(ramps.Features);

        var fuel = AirportFeatureCatalog.Build("X", "v", new[] { N(FeatureKind.Fuel, "Fuel", 0, 0, true), N(FeatureKind.Fuel, "Avfuel", 0.0003, 0, false, FeatureSource.Osm) });
        Assert.Equal("Avfuel", Assert.Single(fuel.Features).Name);
    }

    [Fact]
    public void The_same_name_far_apart_is_two_features_and_close_by_is_one()
    {
        var far = AirportFeatureCatalog.Build("KJFK", "v", new[] { N(FeatureKind.Concourse, "Concourse B", 40.6400, -73.7800), N(FeatureKind.Concourse, "Concourse B", 40.6400, -73.7950) });
        Assert.Equal(2, far.Features.Count);

        var near = AirportFeatureCatalog.Build("KATL", "v", new[]
        {
            N(FeatureKind.Concourse, "Concourse B", 33.6400, -84.4300, false, FeatureSource.Navdata, (33.6390, -84.4300), (33.6410, -84.4300)),
            N(FeatureKind.Concourse, "concourse  b", 33.6415, -84.4300, false, FeatureSource.Osm),      // ~165 m from the navdata centroid
        });
        var one = Assert.Single(near.Features);
        Assert.Equal(FeatureSource.Osm, one.Source);
        Assert.Equal(2, one.Members!.Count);             // the winner inherits what it lacks
    }

    [Fact]
    public void A_guessed_navdata_concourse_yields_to_the_GSX_feature_made_of_the_same_stands()
    {
        // KJFK T5: navdata's letter says "Concourse D" (29 gates); GSX says "Terminal 5" for those very stands.
        var stands = new[] { (40.6450, -73.7760), (40.6454, -73.7760), (40.6458, -73.7760) };
        var cat = AirportFeatureCatalog.Build("KJFK", "v", new[]
        {
            N(FeatureKind.Concourse, "Concourse D", 40.6454, -73.7760, false, FeatureSource.Navdata, stands),
            N(FeatureKind.Terminal, "Terminal 5", 40.6454, -73.7760, false, FeatureSource.Gsx, stands),
        });
        Assert.Equal("Terminal 5", Assert.Single(cat.Features).Name);
    }

    [Fact]
    public void A_proper_name_outranks_a_generic_one_which_outranks_none_and_the_facts_ride_along()
    {
        Assert.True(AirportFeatureCatalog.Rank(N(FeatureKind.Fuel, "Avfuel", 0, 0)) > AirportFeatureCatalog.Rank(N(FeatureKind.Fuel, "Fuel", 0, 0, true, FeatureSource.Osm)));
        Assert.True(AirportFeatureCatalog.Rank(N(FeatureKind.Fuel, "Fuel", 0, 0, true)) > AirportFeatureCatalog.Rank(N(FeatureKind.Fuel, "", 0, 0, false, FeatureSource.Osm)));
        Assert.Equal("Tower 118.5.", AirportFeatureCatalog.Build("X", "v", Array.Empty<AirportFeature>(), "Tower 118.5.").Facts);
    }
}
