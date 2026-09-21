using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class AirportFeatureCatalogTests
{
    private static AirportFeature F(FeatureKind k, string name, double lat, double lon, FeatureSource src, string? detail = null, IReadOnlyList<LatLon>? fp = null)
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = src, Detail = detail, Footprint = fp };

    private static AirportFeature N(FeatureKind k, string name, double lat, double lon, bool generic = false, FeatureSource src = FeatureSource.Navdata, params (double, double)[] members)
        => new() { Kind = k, Name = name, NameIsGeneric = generic, Lat = lat, Lon = lon, Source = src,
                   Members = members.Length == 0 ? null : members.Select(m => new LatLon(m.Item1, m.Item2)).ToList() };

    // ── KTIW, the PR's reference airport: real navdata stands + the real OSM reply ───────────
    //
    // The 11 parking rows of KTIW, read ONCE from %APPDATA%\MSFSBlindAssist\databases\fs2024.sqlite
    // and embedded here as the navdata facts they are — this test never opens a database. Types are
    // LittleNavMapProvider.MapParkingType's: 3 = RAMP_GA_SMALL, 4 = RAMP_GA_MEDIUM, both GA ramp;
    // navdata's parking name "P" becomes "Parking". The odd numbers are one row of 6 stands to the
    // north, the even ones a row of 5 about 600 m south, and NavdataFeatureSource clusters each into
    // its own generic "GA ramp" (RampLinkMetres 80, at least 3 stands).
    private static readonly (int Number, int Type, double Lat, double Lon)[] KtiwStands =
    {
        (1,  3, 47.27431869506836,   -122.57439422607422), (2,  4, 47.269439697265625,  -122.57501983642578),
        (3,  3, 47.274192810058594,  -122.57440948486328), (4,  4, 47.269107818603516,  -122.57505798339844),
        (5,  3, 47.27407455444336,   -122.5744400024414),  (6,  4, 47.26865768432617,   -122.57512664794922),
        (7,  3, 47.27395248413086,   -122.57445526123047), (8,  4, 47.26814651489258,   -122.57530212402344),
        (9,  3, 47.27383041381836,   -122.57447814941406), (10, 4, 47.267799377441406,  -122.57539367675781),
        (11, 3, 47.273712158203125,  -122.57450866699219),
    };

    /// <summary>Both tiers a KTIW catalog is built from: the navdata GA ramps and the real Overpass
    /// reply this PR ships as a fixture (4 unnamed apron polygons, 20 hangars, a tower).</summary>
    private static List<AirportFeature> KtiwFeatures()
    {
        var stands = KtiwStands
            .Select(s => new ParkingSpot { Name = "Parking", Number = s.Number, Type = s.Type, Latitude = s.Lat, Longitude = s.Lon })
            .ToList();
        var features = new List<AirportFeature>(NavdataFeatureSource.Read(stands, null));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-features-area-ktiw.json")));
        features.AddRange(doc.RootElement.GetProperty("elements").EnumerateArray()
            .Select(OsmFeatureClassifier.Classify).Where(f => f != null).Select(f => f!));
        return features;
    }

    [Fact]
    public void At_KTIW_every_stand_is_standing_on_the_ramp_that_owns_it()
    {
        // The defect this pins: the 6-stand ramp adopted the 66,471 m² main apron (which contains
        // the OTHER row's stands too) and the 5-stand ramp adopted a 2,335 m² neighbour containing
        // none of its own, so Nearest measured to a polygon instead of to the stands. Parked on the
        // southern row, a pilot heard the ramp they were standing on named 12-85 m away.
        var cat = AirportFeatureCatalog.Build("KTIW", "v", KtiwFeatures());
        foreach (var s in KtiwStands)
        {
            var owner = Assert.Single(cat.Features, f => f.Members != null
                && f.Members.Any(m => TaxiGeo.HaversineMeters(m.Lat, m.Lon, s.Lat, s.Lon) <= 1.0));
            Assert.InRange(SurroundingsGeometry.Nearest(s.Lat, s.Lon, owner).Metres, 0.0, 1.0);
        }
    }

    [Fact]
    public void A_feature_never_carries_a_footprint_its_own_members_are_outside_of()
    {
        foreach (var f in AirportFeatureCatalog.Build("KTIW", "v", KtiwFeatures()).Features)
            if (f.Footprint != null && f.Members is { Count: > 0 })
                Assert.Contains(f.Members, m => SurroundingsGeometry.Contains(f.Footprint, m.Lat, m.Lon));
    }

    /// <summary>How many times `needle` appears in `haystack`.</summary>
    private static int Count(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    [Fact]
    public void At_KTIW_the_ramp_you_are_parked_on_IS_the_zone_and_nothing_is_named_at_zero_range()
    {
        // The whole point of the pass, end to end: the ramp the aircraft is standing on is where it
        // IS, not a nearby thing — and "GA ramp, to the left, 0 metres" gave a blind pilot a side
        // computed from a degenerate bearing, under a generic name they had just heard for the
        // anonymous pavement. The zone's own name is never said twice, and no distance is zero.
        //
        // KTIW's OTHER ramp, 600 m away, is also called "GA ramp", so it is the one that stays out
        // — that is the whole of the rule. The airport's other unnamed apron POLYGONS are separate
        // pavement under a different word and are named normally (P6: "Apron, ahead, 12 metres");
        // a count over the whole kind would silence those too, which is what the earlier
        // spend-the-kind rule did — and with it a real "North Apron" at any airport that has one.
        var cat = AirportFeatureCatalog.Build("KTIW", "v", KtiwFeatures());
        foreach (var s in KtiwStands)
        {
            string said = SurroundingsReport.Compose($"Parking {s.Number} at KTIW.", "KTIW", cat, s.Lat, s.Lon, 0.0,
                                                     m => $"{Math.Round(m)} metres");
            Assert.StartsWith($"Parking {s.Number} at KTIW. On the GA ramp.", said);
            Assert.DoesNotContain(", 0 metres", said);   // leading comma: "520 metres" ends in "0 metres" too
            Assert.Equal(1, Count(said, "GA ramp"));     // the zone line, and nowhere else
        }
    }

    [Fact]
    public void At_KTIW_out_on_the_pavement_between_the_rows_the_zone_is_the_apron_itself()
    {
        // Inside the 66,471 m² apron and 230 m from the nearest stand — well beyond
        // ZoneMemberMetres, so there is no ramp to be "at" and the polygon answers, as it always
        // did. The member rung must not reach across an apron to the nearest row.
        var cat = AirportFeatureCatalog.Build("KTIW", "v", KtiwFeatures());
        var zone = SurroundingsReport.Zone(cat, 47.2715, -122.5748);
        Assert.NotNull(zone);
        Assert.Equal(FeatureKind.Apron, zone!.Kind);
        Assert.False(zone.HasName);
        Assert.Contains("On the Apron.", SurroundingsReport.Compose("X.", "KTIW", cat, 47.2715, -122.5748, 0.0, m => $"{Math.Round(m)} metres"));
    }

    [Fact]
    public void At_KTIW_the_pavement_under_the_stands_survives_as_its_own_feature()
    {
        // Keeping the ramps' own stand geometry must not cost the polygon a pilot is STANDING on.
        // It is what answers out on the apron away from any row (the test above), and it only has
        // the chance because it is no longer merged into a ramp. All four of the fixture's aprons
        // survive; the one holding the stands is the 66,471 m² main apron, 49 vertices.
        var cat = AirportFeatureCatalog.Build("KTIW", "v", KtiwFeatures());
        var aprons = cat.Features.Where(f => f.Kind == FeatureKind.Apron && f.Footprint != null).ToList();
        Assert.Equal(4, aprons.Count);
        var stand = KtiwStands.Single(s => s.Number == 2);
        Assert.Single(aprons, a => SurroundingsGeometry.Contains(a.Footprint!, stand.Lat, stand.Lon));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_named_building_beside_one_stand_of_a_600_metre_row_is_a_SECOND_place(bool reversed)
    {
        // The mirror of the KTIW case, in Members instead of Footprint. Measured cluster extents:
        // KMEM's cargo rows run 686 m, KSNA's GA ramp 1,296 m, KLNK's 1,016 m. A proper name 30 m
        // from ONE stand absorbs the generic cluster under the name rule alone — and inheriting its
        // members would then report the building at 0 m from the far end of the row. Refusing the
        // members is not enough: the cluster is a real place and consuming it leaves a pilot at the
        // far end with no cargo area near them at all.
        var row = new[] { (35.0400, -89.9800), (35.0430, -89.9800), (35.0454, -89.9800) };     // ~601 m end to end
        var cluster = N(FeatureKind.Cargo, "Cargo ramp", 35.0428, -89.9800, true, FeatureSource.Navdata, row);
        var building = N(FeatureKind.Cargo, "FedEx Cargo", 35.04027, -89.9800, false, FeatureSource.Osm);   // ~30 m from the first stand
        Assert.False(AirportFeatureCatalog.SameFeature(cluster, building));
        Assert.False(AirportFeatureCatalog.SameFeature(building, cluster));                     // and symmetrically

        var input = reversed ? new[] { building, cluster } : new[] { cluster, building };
        var cat = AirportFeatureCatalog.Build("KMEM", "v", input);
        Assert.Equal(2, cat.Features.Count);
        var kept = Assert.Single(cat.Features, f => f.Name == "Cargo ramp");
        Assert.Equal(3, kept.Members!.Count);                                                   // the row keeps its stands…
        Assert.True(kept.NameIsGeneric);                                                        // …and its synthesized name
        Assert.InRange(SurroundingsGeometry.Nearest(35.0454, -89.9800, kept).Metres, 0.0, 1.0); // …and answers at the far end
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_cluster_the_named_building_really_does_describe_still_becomes_one_place(bool reversed)
    {
        // The good case, kept: every stand within SameNameRadiusMetres of the building, so the row
        // IS the building's ramp. One feature, the proper name, the stands as its geometry.
        var row = new[] { (35.0400, -89.9800), (35.0404, -89.9800), (35.0408, -89.9800) };     // ~89 m end to end
        var cluster = N(FeatureKind.Cargo, "Cargo ramp", 35.0404, -89.9800, true, FeatureSource.Navdata, row);
        var building = N(FeatureKind.Cargo, "FedEx Cargo", 35.04027, -89.9800, false, FeatureSource.Osm);
        var input = reversed ? new[] { building, cluster } : new[] { cluster, building };
        var one = Assert.Single(AirportFeatureCatalog.Build("KMEM", "v", input).Features);
        Assert.Equal("FedEx Cargo", one.Name);
        Assert.Equal(3, one.Members!.Count);
    }

    /// <summary>A square of <paramref name="side"/> metres whose south-west corner sits
    /// <paramref name="eastMetres"/> east of longitude 0, on the equator — where a degree of
    /// longitude and a degree of latitude are the same 111,320 m, so the metres are exact.</summary>
    private static IReadOnlyList<LatLon> Square(double eastMetres, double side)
    {
        const double M = 111_320.0;
        double w = eastMetres / M, e = (eastMetres + side) / M, n = side / M;
        return new[] { new LatLon(0.0, w), new LatLon(0.0, e), new LatLon(n, e), new LatLon(n, w) };
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Two_apron_polygons_26_metres_apart_are_two_aprons_whatever_the_order(bool reversed)
    {
        // KTIW again, in miniature: two of its four unnamed aprons sit 26.4 m apart and a third
        // 27.9 m from one of those, well inside Apron's 50 m merge radius — so with OSM alone the
        // element order decided which polygon survived, and dropping the big one takes the zone a
        // pilot is standing in with it. Two rings that neither contain nor overlap are two aprons.
        var a = Square(0, 20); var b = Square(46, 20);                     // 26 m between the near edges
        LatLon ca = SurroundingsGeometry.Centroid(a), cb = SurroundingsGeometry.Centroid(b);
        var west = F(FeatureKind.Apron, "", ca.Lat, ca.Lon, FeatureSource.Osm, fp: a);
        var east = F(FeatureKind.Apron, "", cb.Lat, cb.Lon, FeatureSource.Osm, fp: b);
        var input = reversed ? new[] { east, west } : new[] { west, east };
        Assert.Equal(2, AirportFeatureCatalog.Build("X", "v", input).Features.Count);
    }

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
