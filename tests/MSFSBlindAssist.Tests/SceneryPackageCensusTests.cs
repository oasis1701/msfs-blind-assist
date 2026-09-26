// A fake Community folder: each package is a layout.json, a manifest.json naming its content type
// and one objects BGL placing N models at a coordinate. Same synthetic BGL builder as
// BglPlacementReaderTests — never a payware file.
using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryPackageCensusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-census-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private string Community => Path.Combine(_root, "Community");
    private static readonly AirportFacilities Katl = new() { Icao = "KATL", LeftLon = -84.45, RightLon = -84.40, TopLat = 33.66, BottomLat = 33.62 };
    private static readonly string Schema = SceneryPackageCensus.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture);

    private string Package(string name, int count, double lat, double lon, string contentType = "SCENERY")
    {
        string dir = Path.Combine(Community, name, "scenery");
        Directory.CreateDirectory(dir);
        var objs = Enumerable.Range(0, count).Select(i => (lat + i * 0.0001, lon, 0.0, Guid.NewGuid())).ToArray();
        File.WriteAllBytes(Path.Combine(dir, "objects.bgl"), BglPlacementReaderTests.BuildBgl(objs));
        File.WriteAllText(Path.Combine(Community, name, "layout.json"), "{}");
        File.WriteAllText(Path.Combine(Community, name, "manifest.json"), $"{{\"content_type\":\"{contentType}\"}}");
        return Path.Combine(Community, name);
    }

    [Fact]
    public void Finds_the_package_whose_objects_stand_on_the_airport_and_nothing_else()
    {
        string here = Package("flytampa-somewhere", 30, 33.6400, -84.4300);        // no ICAO in the folder name — position decides
        Package("other-airport", 30, 34.2000, -84.4300);                           // ~60 km north
        Package("a-few-objects", 5, 33.6400, -84.4300);                            // below the threshold: a livery's hangar, a city pack's edge
        Package("an-aircraft", 30, 33.6400, -84.4300, contentType: "AIRCRAFT");    // not scenery: never scanned
        var found = new SceneryPackageCensus(Path.Combine(_root, "cache")).Locate(Community, Katl);
        Assert.Equal(here, Assert.Single(found));
    }

    [Fact]
    public void A_second_lookup_is_served_from_the_disk_cache_and_a_changed_package_is_rescanned()
    {
        string pkg = Package("kxyz", 30, 33.6400, -84.4300);
        string cache = Path.Combine(_root, "cache");
        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));

        File.Delete(Path.Combine(pkg, "scenery", "objects.bgl"));                  // prove nothing is re-read
        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));

        File.WriteAllText(Path.Combine(pkg, "layout.json"), "{ \"changed\": true }");   // the package was updated
        Assert.Empty(new SceneryPackageCensus(cache).Locate(Community, Katl));
    }

    [Fact]
    public void A_packages_manifest_is_read_once_until_its_layout_changes()
    {
        // Every catalog build calls Locate, and it re-read and re-parsed every package's manifest.json
        // each time for an answer only a package update can change (review CL-8). An update rewrites
        // layout.json, so the verdict is memoised on its stamp.
        string pkg = Package("kxyz", 30, 33.6400, -84.4300);
        var census = new SceneryPackageCensus(Path.Combine(_root, "cacheManifest"));
        Assert.Single(census.Locate(Community, Katl));

        // Rewritten WITHOUT a package update: not read again, so the package is still scenery.
        File.WriteAllText(Path.Combine(pkg, "manifest.json"), "{\"content_type\":\"AIRCRAFT\"}");
        Assert.Single(census.Locate(Community, Katl));

        // A package update rewrites layout.json, and only then is the manifest read again.
        File.WriteAllText(Path.Combine(pkg, "layout.json"), "{ \"changed\": true }");
        Assert.Empty(census.Locate(Community, Katl));
    }

    [Fact]
    public void A_missing_folder_or_a_broken_package_never_throws()
    {
        Assert.Empty(new SceneryPackageCensus(Path.Combine(_root, "cache")).Locate(Path.Combine(_root, "nope"), Katl));
        Directory.CreateDirectory(Path.Combine(Community, "broken", "scenery"));
        File.WriteAllText(Path.Combine(Community, "broken", "layout.json"), "{}");
        File.WriteAllBytes(Path.Combine(Community, "broken", "scenery", "x.bgl"), new byte[] { 9, 9, 9 });
        Assert.Empty(new SceneryPackageCensus(Path.Combine(_root, "cache2")).Locate(Community, Katl));
    }

    [Fact]
    public void An_empty_community_folder_finds_nothing_and_writes_no_cache()
    {
        Directory.CreateDirectory(Community);
        string cache = Path.Combine(_root, "cache");
        Assert.Empty(new SceneryPackageCensus(cache).Locate(Community, Katl));
        Assert.False(File.Exists(Path.Combine(cache, "census.json")));             // nothing was learned, so nothing is written
    }

    [Fact]
    public void A_package_that_keeps_its_bgls_outside_a_scenery_folder_is_scanned_all_the_same()
    {
        // The scan walks the package, not a fixed "scenery" subfolder: authors ship BGLs under
        // scenery\<icao>\, under a vendor folder, or beside layout.json.
        string dir = Path.Combine(Community, "loose");
        Directory.CreateDirectory(Path.Combine(dir, "deep", "deeper"));
        var objs = Enumerable.Range(0, 30).Select(i => (33.6400 + i * 0.0001, -84.4300, 0.0, Guid.NewGuid())).ToArray();
        File.WriteAllBytes(Path.Combine(dir, "deep", "deeper", "objects.BGL"), BglPlacementReaderTests.BuildBgl(objs));
        File.WriteAllText(Path.Combine(dir, "layout.json"), "{}");                 // no manifest at all: still scanned
        Assert.Equal(dir, Assert.Single(new SceneryPackageCensus(Path.Combine(_root, "cache")).Locate(Community, Katl)));
    }

    [Fact]
    public void Packages_over_the_threshold_come_back_highest_first_and_never_more_than_the_cap()
    {
        string most = Package("most", 60, 33.6400, -84.4300);
        string mid = Package("mid", 45, 33.6410, -84.4300);
        string few = Package("few", 30, 33.6420, -84.4300);
        Package("fewest", 25, 33.6430, -84.4300);                                  // a fourth over the bar: cut by MaxPackages
        Package("under", SceneryPackageCensus.MinPlacementsInBox - 1, 33.6440, -84.4300);

        var found = new SceneryPackageCensus(Path.Combine(_root, "cache")).Locate(Community, Katl);
        Assert.Equal(new[] { most, mid, few }, found);
        Assert.Equal(SceneryPackageCensus.MaxPackages, found.Count);
    }

    [Fact]
    public void A_box_with_no_extent_still_matches_what_stands_on_it_and_null_island_matches_nothing()
    {
        // An airport whose navdata hull collapsed to a point (one record) still has the margin
        // around it; (0, 0) is a real coordinate to a box test and must match nothing here.
        Package("kxyz", 30, 33.6400, -84.4300);
        var point = new AirportFacilities { Icao = "KXYZ", LeftLon = -84.4300, RightLon = -84.4300, TopLat = 33.6410, BottomLat = 33.6410 };
        var nullIsland = new AirportFacilities { Icao = "ZZZZ", LeftLon = 0, RightLon = 0, TopLat = 0, BottomLat = 0 };
        var census = new SceneryPackageCensus(Path.Combine(_root, "cache"));

        Assert.Single(census.Locate(Community, point));
        Assert.Empty(census.Locate(Community, nullIsland));
    }

    [Fact]
    public void A_cache_written_by_another_schema_is_ignored_whole_and_rebuilt()
    {
        string pkg = Package("kxyz", 30, 33.6400, -84.4300);
        string cache = Path.Combine(_root, "cache");
        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));
        string file = Path.Combine(cache, "census.json");
        Assert.Contains("\"SchemaVersion\":" + Schema, File.ReadAllText(file));

        // Another schema's fields mean nothing here. This document would otherwise be believed:
        // it carries the package's REAL stamp and a plausible Cells array.
        var layout = new FileInfo(Path.Combine(pkg, "layout.json"));
        File.WriteAllText(file, "{\"SchemaVersion\":99,\"Packages\":[{\"Path\":\"" + pkg.Replace("\\", "\\\\") + "\"," +
                                $"\"LayoutLength\":{layout.Length},\"LayoutTicks\":{layout.LastWriteTimeUtc.Ticks}," +
                                "\"Cells\":[[9999,9999,5000]]}]}");
        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));    // rebuilt from the BGLs, not from cell 9999
        Assert.Contains("\"SchemaVersion\":" + Schema, File.ReadAllText(file));

        // A document carrying no Cells key says nothing about where the package's objects stand,
        // which is not the same as saying it has none — rebuilt rather than believed. Written at the
        // CURRENT schema, or the schema check would reject it first and this would prove nothing.
        File.WriteAllText(file, "{\"SchemaVersion\":" + Schema + ",\"Packages\":[{\"Path\":\"" + pkg.Replace("\\", "\\\\") + "\"," +
                                $"\"LayoutLength\":{layout.Length},\"LayoutTicks\":{layout.LastWriteTimeUtc.Ticks}}}]}}");
        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));
    }

    [Fact]
    public void A_row_an_earlier_schema_wrote_is_read_again()
    {
        // Schema 1 persisted scans taken while an installer was still writing the package (review
        // SI-1): a row of it may hold a short count frozen under the package's FINAL stamp. This one
        // says the package models nothing, and must not be believed.
        string pkg = Package("kxyz", 30, 33.6400, -84.4300);
        string cache = Path.Combine(_root, "cacheOld");
        Directory.CreateDirectory(cache);
        var layout = new FileInfo(Path.Combine(pkg, "layout.json"));
        File.WriteAllText(Path.Combine(cache, "census.json"), "{\"SchemaVersion\":1,\"Packages\":[{\"Path\":\"" + pkg.Replace("\\", "\\\\") + "\"," +
                          $"\"LayoutLength\":{layout.Length},\"LayoutTicks\":{layout.LastWriteTimeUtc.Ticks},\"Cells\":[]}}]}}");

        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));
    }

    [Fact]
    public void A_package_its_installer_has_not_finished_is_used_but_never_cached()
    {
        // An installer writes layout.json FIRST, with its final stamp, and the BGLs after it
        // (measured on 33 of 35 real packages). Every file that IS there reads fine, so the scan
        // looked complete and its short count was frozen under that final stamp for good.
        Package("good", 30, 33.6400, -84.4300);
        string installing = Package("installing", 30, 33.6410, -84.4300);
        long objects = new FileInfo(Path.Combine(installing, "scenery", "objects.bgl")).Length;
        byte[] terminal = BglPlacementReaderTests.BuildBgl((33.6420, -84.4300, 0.0, Guid.NewGuid()));
        SceneryPackageDiskTests.WriteLayout(installing, ("scenery/objects.bgl", objects), ("scenery/terminal.bgl", terminal.Length));
        string cache = Path.Combine(_root, "cacheInstalling");

        Assert.Equal(2, new SceneryPackageCensus(cache).Locate(Community, Katl, out bool incomplete).Count);   // what IS there still counts
        Assert.True(incomplete);
        Assert.DoesNotContain("installing", File.ReadAllText(Path.Combine(cache, "census.json")));

        File.WriteAllBytes(Path.Combine(installing, "scenery", "terminal.bgl"), terminal);                   // the installer finishes
        new SceneryPackageCensus(cache).Locate(Community, Katl, out bool after);
        Assert.False(after);
        Assert.Contains("installing", File.ReadAllText(Path.Combine(cache, "census.json")));
    }

    [Fact]
    public void An_option_its_configurator_switched_off_does_not_keep_the_package_out_of_the_cache()
    {
        // Aerosoft EDDF's shape, measured: "eddf_placements_staticac.bgl" listed, "…staticac.off" on disk.
        string pkg = Package("options", 30, 33.6400, -84.4300);
        long objects = new FileInfo(Path.Combine(pkg, "scenery", "objects.bgl")).Length;
        File.WriteAllBytes(Path.Combine(pkg, "scenery", "staticac.off"), new byte[604]);
        SceneryPackageDiskTests.WriteLayout(pkg, ("scenery/objects.bgl", objects), ("scenery/staticac.bgl", 604));
        string cache = Path.Combine(_root, "cacheOptions");

        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl, out bool incomplete));
        Assert.False(incomplete);
        Assert.Contains("options", File.ReadAllText(Path.Combine(cache, "census.json")));
    }

    [Fact]
    public void A_cache_row_with_no_path_is_dropped_and_the_good_rows_still_serve()
    {
        // A hand-edited or half-corrupted census.json can be valid JSON and still carry a row
        // naming nothing. Indexing the dictionary on that null threw out of Locate, out of
        // SurroundingsCatalogBuilder, and cost the pilot the WHOLE surroundings catalog at that airport.
        string pkg = Package("kxyz", 30, 33.6400, -84.4300);
        string cache = Path.Combine(_root, "cache");
        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));

        string file = Path.Combine(cache, "census.json");
        string good = File.ReadAllText(file);
        int firstRow = good.IndexOf("[{", StringComparison.Ordinal) + 1;
        File.WriteAllText(file, good.Insert(firstRow, "null,{\"Path\":null,\"Cells\":[[6728,-16886,9999]]},{\"Path\":\"\",\"Cells\":[]},"));

        Assert.Equal(pkg, Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl)));
    }

    [Fact]
    public void A_scan_that_could_not_read_every_file_is_used_but_never_cached()
    {
        // A BGL the simulator has open exclusively is a MOMENT, not a property of the package:
        // its placements are missing from this pass, so freezing that count until layout.json
        // changes would hide the package for the rest of the install's life.
        Package("good", 30, 33.6400, -84.4300);
        string partial = Package("partial", 30, 33.6410, -84.4300);
        string locked = Path.Combine(partial, "scenery", "locked.bgl");
        File.WriteAllBytes(locked, BglPlacementReaderTests.BuildBgl((33.6420, -84.4300, 0.0, Guid.NewGuid())));
        string cache = Path.Combine(_root, "cache");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // What WAS read still counts for this call — the package is not hidden meanwhile.
            Assert.Equal(2, new SceneryPackageCensus(cache).Locate(Community, Katl).Count);
            string json = File.ReadAllText(Path.Combine(cache, "census.json"));
            Assert.Contains("good", json);
            Assert.DoesNotContain("partial", json);
        }

        // Released: the next lookup reads it again rather than serving the short count.
        Assert.Equal(2, new SceneryPackageCensus(cache).Locate(Community, Katl).Count);
        Assert.Contains("partial", File.ReadAllText(Path.Combine(cache, "census.json")));
    }

    [Fact]
    public void A_short_scan_reports_itself_so_the_catalog_built_on_it_expires()
    {
        // Same reason as the indexer's: not caching the short count is only half of it, because
        // the CATALOG built on this locate is cached and nothing rebuilds it on its own. Saying
        // the scan was short is what gives that catalog a lifetime (SurroundingsCatalogBuilder ORs it into
        // degraded), so the package is looked for again once the lock is gone.
        Package("good", 30, 33.6400, -84.4300);
        string partial = Package("partial", 30, 33.6410, -84.4300);
        string locked = Path.Combine(partial, "scenery", "locked.bgl");
        File.WriteAllBytes(locked, BglPlacementReaderTests.BuildBgl((33.6420, -84.4300, 0.0, Guid.NewGuid())));
        string cache = Path.Combine(_root, "cacheShort");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            new SceneryPackageCensus(cache).Locate(Community, Katl, out bool incomplete);
            Assert.True(incomplete);
        }

        new SceneryPackageCensus(cache).Locate(Community, Katl, out bool after);
        Assert.False(after);
    }

    [Fact]
    public void A_package_that_was_uninstalled_leaves_the_cache()
    {
        Package("stays", 30, 33.6400, -84.4300);
        string going = Package("going", 30, 33.6410, -84.4300);
        string cache = Path.Combine(_root, "cache");
        Assert.Equal(2, new SceneryPackageCensus(cache).Locate(Community, Katl).Count);

        Directory.Delete(going, recursive: true);
        Assert.Single(new SceneryPackageCensus(cache).Locate(Community, Katl));
        Assert.DoesNotContain("going", File.ReadAllText(Path.Combine(cache, "census.json")));
    }

    [Fact]
    public void Two_threads_locating_at_once_both_get_the_package_and_write_one_cache_file()
    {
        string pkg = Package("kxyz", 30, 33.6400, -84.4300);
        string cache = Path.Combine(_root, "cache");
        var census = new SceneryPackageCensus(cache);

        var found = new IReadOnlyList<string>[8];
        Parallel.For(0, found.Length, i => found[i] = census.Locate(Community, Katl));

        Assert.All(found, f => Assert.Equal(pkg, Assert.Single(f)));
        Assert.Single(Directory.GetFiles(cache, "*.json"));
        Assert.Empty(Directory.GetFiles(cache, "*.tmp"));       // the half-written file is never left behind
    }
}
