// Builds a fake package on disk: a modelLib BGL holding ModelInfo XML for the models named and an
// objects BGL placing them. Same synthetic builder as BglPlacementReaderTests. MakePackage() is the
// original three-model package (two parts of one concourse, one fence); MakePackage(name, …) places
// whatever a test names, at whatever coordinates it needs.
using System.Text;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryPackageIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-scenery-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string MakePackage()
    {
        string pkg = Path.Combine(_root, "pkg", "scenery");
        Directory.CreateDirectory(pkg);
        var gA1 = Guid.NewGuid(); var gA2 = Guid.NewGuid(); var gF = Guid.NewGuid();
        string xml = $"<ModelInfo guid=\"{{{gA1}}}\" name=\"concourse_a_01\"/><ModelInfo guid=\"{{{gA2}}}\" name=\"concourse_a_02\"/><ModelInfo guid=\"{{{gF}}}\" name=\"KTIW_Fence2\"/>";
        var lib = new byte[0x38 + 20].Concat(Encoding.Latin1.GetBytes(xml)).ToArray();
        BitConverter.TryWriteBytes(lib.AsSpan(0, 4), 0x19920201u);            // a BGL with zero sections + XML tail
        File.WriteAllBytes(Path.Combine(pkg, "modelLib.BGL"), lib);
        File.WriteAllBytes(Path.Combine(pkg, "objects.bgl"), BglPlacementReaderTests.BuildBgl((33.640, -84.430, 0, gA1), (33.642, -84.430, 0, gA2), (33.650, -84.440, 0, gF)));
        File.WriteAllText(Path.Combine(_root, "pkg", "layout.json"), "{}");
        return Path.Combine(_root, "pkg");
    }

    private string MakePackage(string name, params (string model, double lat, double lon)[] placements)
    {
        string root = Path.Combine(_root, name), scenery = Path.Combine(root, "scenery");
        Directory.CreateDirectory(scenery);
        var guids = placements.Select(p => p.model).Distinct().ToDictionary(m => m, _ => Guid.NewGuid());
        string xml = string.Concat(guids.Select(g => $"<ModelInfo guid=\"{{{g.Value}}}\" name=\"{g.Key}\"/>"));
        var lib = new byte[0x38 + 20].Concat(Encoding.Latin1.GetBytes(xml)).ToArray();
        BitConverter.TryWriteBytes(lib.AsSpan(0, 4), 0x19920201u);
        File.WriteAllBytes(Path.Combine(scenery, "modelLib.BGL"), lib);
        File.WriteAllBytes(Path.Combine(scenery, "objects.bgl"),
            BglPlacementReaderTests.BuildBgl(placements.Select(p => (p.lat, p.lon, 0.0, guids[p.model])).ToArray()));
        File.WriteAllText(Path.Combine(root, "layout.json"), "{}");
        return root;
    }

    [Fact]
    public void Indexes_a_package_merging_parts_and_dropping_noise_then_serves_from_cache()
    {
        string pkg = MakePackage();
        string cache = Path.Combine(_root, "cache");
        var indexer = new SceneryPackageIndexer(cache);

        var features = indexer.GetFeatures("KATL", new[] { pkg }, null);
        var only = Assert.Single(features);
        Assert.Equal(FeatureKind.Concourse, only.Kind);
        Assert.Equal("Concourse A", only.Name);
        Assert.InRange(only.Lat, 33.6409, 33.6411);
        Assert.Equal(FeatureSource.Scenery, only.Source);
        Assert.Single(Directory.GetFiles(cache, "*.json"));
        Assert.Contains("1 features", indexer.LastStatus);

        // Second call: cache hit (delete the BGLs to prove nothing is re-read).
        File.Delete(Path.Combine(pkg, "scenery", "objects.bgl"));
        Assert.Single(indexer.GetFeatures("KATL", new[] { pkg }, null));

        // layout.json change → rebuild → now empty because the placements are gone.
        File.WriteAllText(Path.Combine(pkg, "layout.json"), "{ \"changed\": true }");
        Assert.Empty(indexer.GetFeatures("KATL", new[] { pkg }, null));
    }

    [Fact]
    public void The_cache_file_stores_raw_model_names_and_points_under_the_schema_version()
    {
        string pkg = MakePackage();
        string cache = Path.Combine(_root, "cache");
        var indexer = new SceneryPackageIndexer(cache);

        Assert.Single(indexer.GetFeatures("KATL", new[] { pkg }, null));

        string json = File.ReadAllText(Assert.Single(Directory.GetFiles(cache, "*.json")));
        Assert.Contains("\"SchemaVersion\":2", json);
        Assert.Contains("\"Name\":\"concourse_a_01\"", json);   // the author's own name, unclassified
        Assert.DoesNotContain("Concourse A", json);             // no spoken name: the airport that asks decides it
        Assert.DoesNotContain("KTIW_Fence2", json);             // the prefilter rejected it, so it never reached the cache
    }

    [Fact]
    public void A_cache_written_by_an_older_schema_is_rebuilt_never_half_read()
    {
        string pkg = MakePackage();
        string cache = Path.Combine(_root, "cache");
        Assert.Single(new SceneryPackageIndexer(cache).GetFeatures("KATL", new[] { pkg }, null));

        // Schema 1 cached CLASSIFIED features — a shape this build must not read a single field of.
        string cachePath = Assert.Single(Directory.GetFiles(cache, "*.json"));
        File.WriteAllText(cachePath, "{\"SchemaVersion\":1,\"LayoutLength\":2,\"LayoutTicks\":0,\"Placements\":3,\"Unresolved\":0," +
                                     "\"Features\":[{\"Kind\":\"Hangar\",\"Name\":\"Phantom\",\"Lat\":1.0,\"Lon\":2.0}]}");

        var rebuilt = new SceneryPackageIndexer(cache).GetFeatures("KATL", new[] { pkg }, null);
        Assert.Equal("Concourse A", Assert.Single(rebuilt).Name);
        Assert.Contains("\"SchemaVersion\":2", File.ReadAllText(cachePath));
    }

    [Fact]
    public void Two_packages_with_the_same_leaf_name_get_distinct_cache_files()
    {
        string cache = Path.Combine(_root, "cache");

        // Create two packages with the same leaf folder name in different root directories
        string pkgA = Path.Combine(_root, "a", "pkg", "scenery");
        string pkgB = Path.Combine(_root, "b", "pkg", "scenery");
        Directory.CreateDirectory(pkgA);
        Directory.CreateDirectory(pkgB);

        var gA = Guid.NewGuid();
        string xml = $"<ModelInfo guid=\"{{{gA}}}\" name=\"concourse_a_01\"/>";
        var lib = new byte[0x38 + 20].Concat(Encoding.Latin1.GetBytes(xml)).ToArray();
        BitConverter.TryWriteBytes(lib.AsSpan(0, 4), 0x19920201u);

        // Write package A
        File.WriteAllBytes(Path.Combine(pkgA, "modelLib.BGL"), lib);
        File.WriteAllBytes(Path.Combine(pkgA, "objects.bgl"), BglPlacementReaderTests.BuildBgl((33.640, -84.430, 0, gA)));
        File.WriteAllText(Path.Combine(_root, "a", "pkg", "layout.json"), "{}");

        // Write package B (same structure, different location)
        File.WriteAllBytes(Path.Combine(pkgB, "modelLib.BGL"), lib);
        File.WriteAllBytes(Path.Combine(pkgB, "objects.bgl"), BglPlacementReaderTests.BuildBgl((34.640, -85.430, 0, gA)));
        File.WriteAllText(Path.Combine(_root, "b", "pkg", "layout.json"), "{}");

        var indexer = new SceneryPackageIndexer(cache);
        string pkgDirA = Path.Combine(_root, "a", "pkg");
        string pkgDirB = Path.Combine(_root, "b", "pkg");

        // Index both packages
        indexer.GetFeatures("KATL", new[] { pkgDirA }, null);
        indexer.GetFeatures("KATL", new[] { pkgDirB }, null);

        // Both should be cached with different file names (despite same leaf name)
        var cacheFiles = Directory.GetFiles(cache, "*.json");
        Assert.Equal(2, cacheFiles.Length);
    }

    [Fact]
    public void Same_named_buildings_far_apart_are_separate_features_never_one_average()
    {
        // MK Studios BIKF: seven "DS Hangar" buildings up to 2.3 km apart became ONE point between them.
        string pkg = MakePackage("bikf", ("MK_BIKF_DS_Hangar_01", 63.9850, -22.6000), ("MK_BIKF_DS_Hangar_02", 63.9950, -22.6000));
        var hangars = new SceneryPackageIndexer(Path.Combine(_root, "c1")).GetFeatures("BIKF", new[] { pkg }, null);
        Assert.Equal(2, hangars.Count);
        Assert.All(hangars, h => { Assert.Equal("DS Hangar", h.Name); Assert.Single(h.Members!); });
    }

    [Fact]
    public void A_name_scattered_over_many_spots_is_clutter_and_is_dropped()
    {
        var scattered = Enumerable.Range(0, 8).Select(i => ("kxyz_freight_shed", 40.0 + i * 0.01, -75.0)).ToArray();   // 8 spots, ~1.1 km apart
        string pkg = MakePackage("clutter", scattered.Append(("KXYZ_Fire_Station", 40.0005, -75.0005)).ToArray());
        var features = new SceneryPackageIndexer(Path.Combine(_root, "c2")).GetFeatures("KXYZ", new[] { pkg }, null);
        Assert.Equal("Fire Station", Assert.Single(features).Name);
    }

    [Fact]
    public void A_model_placed_two_hundred_times_on_one_ramp_is_ground_equipment_whatever_it_is_called()
    {
        // Measured at EIDW: 41 "Lgcontainercargo Dhl" containers in ONE 252 m blob. They cluster as
        // one, so only how MANY of them there are tells them from the building beside them.
        var fleet = Enumerable.Range(0, 200).Select(i => ("KXYZ_Cargo_Trailer", 40.0 + i * 0.00001, -75.0)).ToArray();
        string pkg = MakePackage("fleet", fleet.Append(("KXYZ_Fire_Station", 40.0005, -75.0005)).ToArray());
        var features = new SceneryPackageIndexer(Path.Combine(_root, "c9")).GetFeatures("KXYZ", new[] { pkg }, null);
        Assert.Equal("Fire Station", Assert.Single(features).Name);
    }

    [Fact]
    public void A_terminal_modelled_as_dozens_of_parts_in_one_place_is_one_building()
    {
        // EDDB: "Terminal A" is 46 separate models, one cluster, 350 m across. The classifier
        // deliberately collapses the parts onto one name; counting them dropped the whole terminal.
        var parts = Enumerable.Range(1, 46).Select(i => ($"EDDB_Terminal_A_{i:00}", 52.3600 + i * 0.00007, 13.5000)).ToArray();
        string pkg = MakePackage("eddb", parts);
        var only = Assert.Single(new SceneryPackageIndexer(Path.Combine(_root, "c10")).GetFeatures("EDDB", new[] { pkg }, null));
        Assert.Equal(FeatureKind.Terminal, only.Kind);
        Assert.Equal("Terminal A", only.Name);
        Assert.Equal(46, only.Members!.Count);
    }

    [Fact]
    public void A_terminals_parts_stacked_on_one_coordinate_are_one_building()
    {
        // KPHX "Terminal L": 21 parts at literally the same point, so the cluster has zero extent.
        var parts = Enumerable.Range(1, 21).Select(i => ($"KPHX_Terminal_L_{i:00}", 33.43420, -112.01160)).ToArray();
        var only = Assert.Single(new SceneryPackageIndexer(Path.Combine(_root, "c11")).GetFeatures("KPHX", new[] { MakePackage("kphx", parts) }, null));
        Assert.Equal("Terminal L", only.Name);
        Assert.Equal(21, only.Members!.Count);
        Assert.InRange(only.Lat, 33.4341, 33.4343);
    }

    [Fact]
    public void A_terminal_with_no_name_of_its_own_is_one_building_however_many_parts_it_has()
    {
        // EDDB's unlettered terminal: 175 parts in one 764 m cluster, named only by its kind.
        var parts = Enumerable.Range(1, 175).Select(i => ($"EDDB_Terminal_Part_{i:000}", 52.3600 + i * 0.0000392, 13.5000)).ToArray();
        var only = Assert.Single(new SceneryPackageIndexer(Path.Combine(_root, "c12")).GetFeatures("EDDB", new[] { MakePackage("eddb-generic", parts) }, null));
        Assert.Equal("Terminal", only.Name);
        Assert.True(only.NameIsGeneric);
        Assert.Equal(175, only.Members!.Count);
    }

    [Fact]
    public void A_fuel_truck_fleet_on_one_apron_is_still_dropped_by_the_placement_cap()
    {
        // ENGM ships five "Ground Fuel N" fleets, 33 to 451 trucks each. Only terminals and
        // concourses are exempt from the placement cap; a fuel fleet is what it exists for.
        var fleet = Enumerable.Range(0, 60).Select(i => ("KXYZ_Ground_Fuel_2", 40.0 + i * 0.00002, -75.0)).ToArray();
        string pkg = MakePackage("fuelfleet", fleet.Append(("KXYZ_Fire_Station", 40.0005, -75.0005)).ToArray());
        var features = new SceneryPackageIndexer(Path.Combine(_root, "c13")).GetFeatures("KXYZ", new[] { pkg }, null);
        Assert.Equal("Fire Station", Assert.Single(features).Name);
    }

    [Fact]
    public void A_terminal_name_standing_in_more_places_than_the_cluster_cap_allows_is_still_dropped()
    {
        // The exemption is from the PLACEMENT cap only: four "Terminal B" buildings a kilometre
        // apart are not one building an author split up.
        var spread = Enumerable.Range(0, 4).Select(i => ($"KXYZ_Terminal_B_{i}", 40.0 + i * 0.01, -75.0)).ToArray();
        Assert.Empty(new SceneryPackageIndexer(Path.Combine(_root, "c14")).GetFeatures("KXYZ", new[] { MakePackage("spread", spread) }, null));
    }

    [Fact]
    public void One_building_placed_twice_within_a_stones_throw_is_one_feature_with_two_members()
    {
        string pkg = MakePackage("twice", ("KXYZ_Fire_Station", 40.0000, -75.0000), ("KXYZ_Fire_Station", 40.00027, -75.0000));   // ~30 m apart
        var only = Assert.Single(new SceneryPackageIndexer(Path.Combine(_root, "c6")).GetFeatures("KXYZ", new[] { pkg }, null));
        Assert.Equal("Fire Station", only.Name);
        Assert.Equal(2, only.Members!.Count);
    }

    [Fact]
    public void Placements_outside_the_asking_airports_box_belong_to_another_airport()
    {
        // One package, two airports 18 km apart (MK Studios ships BIKF and BIRK together).
        string pkg = MakePackage("two", ("mk_bikf_da_terminal", 63.9950, -22.6240), ("mk_birk_da_terminal", 64.1320, -21.9470));
        var bikf = new AirportFacilities { Icao = "BIKF", LeftLon = -22.66, RightLon = -22.58, TopLat = 64.01, BottomLat = 63.96 };
        var only = Assert.Single(new SceneryPackageIndexer(Path.Combine(_root, "c3")).GetFeatures("BIKF", new[] { pkg }, bikf));
        Assert.InRange(only.Lat, 63.99, 64.00);
    }

    [Fact]
    public void The_cache_holds_raw_placements_so_the_asking_icao_decides_the_name_every_time()
    {
        string pkg = MakePackage("multi", ("KPWT_Hangar_07", 47.4900, -122.7600), ("KTIW_Hangar_07", 47.2700, -122.5700));
        var indexer = new SceneryPackageIndexer(Path.Combine(_root, "c4"));
        var first = indexer.GetFeatures("KTIW", new[] { pkg }, null);
        File.Delete(Path.Combine(pkg, "scenery", "objects.bgl"));                                   // served from the cache from here on
        var second = indexer.GetFeatures("KPWT", new[] { pkg }, null);
        Assert.Contains(first, f => f.Name == "Hangar 7");
        Assert.Contains(second, f => f.Name == "Hangar 7");                                         // not "Kpwt Hangar": classified for KPWT, not replayed from KTIW's pass
        Assert.DoesNotContain(second, f => f.Name.StartsWith("Kpwt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void One_unreadable_file_does_not_cost_the_package_and_the_status_is_honest()
    {
        string pkg = MakePackage("honest", ("KXYZ_Fire_Station", 40.0005, -75.0005));
        File.WriteAllBytes(Path.Combine(pkg, "scenery", "broken.bgl"), new byte[] { 1, 2, 3 });
        var indexer = new SceneryPackageIndexer(Path.Combine(_root, "c5"));
        Assert.Single(indexer.GetFeatures("KXYZ", new[] { pkg }, null));
        Assert.Contains("1 features from honest", indexer.LastStatus);
        Assert.DoesNotContain("base-library", indexer.LastStatus);
        indexer.GetFeatures("KABC", Array.Empty<string>(), null);
        Assert.Equal("KABC: no installed scenery package found", indexer.LastStatus);
        indexer.GetFeatures("KXYZ", new[] { pkg }, null, locatedByCensus: true);
        Assert.EndsWith("(located by Community scan)", indexer.LastStatus);
    }

    [Fact]
    public void A_missing_package_folder_is_reported_unreadable_and_costs_no_other_package()
    {
        string good = MakePackage("good", ("KXYZ_Fire_Station", 40.0005, -75.0005));
        string gone = Path.Combine(_root, "not-installed");
        var indexer = new SceneryPackageIndexer(Path.Combine(_root, "c7"));

        Assert.Single(indexer.GetFeatures("KXYZ", new[] { gone, good }, null));
        Assert.Contains("not-installed: unreadable", indexer.LastStatus);
        Assert.Contains("1 features from good", indexer.LastStatus);
    }

    [Fact]
    public void Two_threads_indexing_one_package_both_get_the_features_and_write_one_cache_file()
    {
        string pkg = MakePackage("shared", ("KXYZ_Fire_Station", 40.0005, -75.0005));
        string cache = Path.Combine(_root, "c8");
        var indexer = new SceneryPackageIndexer(cache);

        var counts = new int[8];
        Parallel.For(0, counts.Length, i => counts[i] = indexer.GetFeatures("KXYZ", new[] { pkg }, null).Count);

        Assert.All(counts, c => Assert.Equal(1, c));
        Assert.Single(Directory.GetFiles(cache, "*.json"));
        Assert.Empty(Directory.GetFiles(cache, "*.tmp"));       // the half-written file is never left behind
    }
}
