// The disk rules SceneryPackageCensus and SceneryPackageIndexer share (review CL-4). Synthetic BGLs
// from BglPlacementReaderTests.BuildBgl — never a payware file.
using System.Globalization;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryPackageDiskTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-disk-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Folder(params string[] parts)
    {
        string dir = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_walk_finds_every_bgl_at_any_depth_and_in_any_case_and_keys_it_the_way_layout_json_spells_it()
    {
        string pkg = Folder("pkg");
        byte[] bgl = BglPlacementReaderTests.BuildBgl((1.0, 2.0, 0.0, Guid.NewGuid()));
        File.WriteAllBytes(Path.Combine(Folder("pkg", "Scenery", "World"), "objects.BGL"), bgl);
        File.WriteAllBytes(Path.Combine(pkg, "top.bgl"), bgl);
        File.WriteAllText(Path.Combine(pkg, "notes.txt"), "not a bgl");
        File.WriteAllBytes(Path.Combine(pkg, "option.bgl.off"), bgl);                   // switched off: not a *.bgl

        var walk = SceneryPackageDisk.WalkBgls(pkg, "test", s => { BglPlacementReader.Read(s, out bool done); return done; });

        Assert.Equal(2, walk.Files.Count);
        Assert.Equal((long?)bgl.Length, walk.Files["scenery/world/objects.bgl"]);       // '/' and any case
        Assert.Equal((long?)bgl.Length, walk.Files["top.bgl"]);
        Assert.Equal(0, walk.Unreadable);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_is_found_counted_once_and_carries_no_length()
    {
        string pkg = Folder("locked");
        string locked = Path.Combine(pkg, "locked.bgl");
        File.WriteAllBytes(locked, BglPlacementReaderTests.BuildBgl((1.0, 2.0, 0.0, Guid.NewGuid())));

        SceneryPackageDisk.BglWalk walk;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            walk = SceneryPackageDisk.WalkBgls(pkg, "test", _ => true);

        Assert.Null(Assert.Single(walk.Files).Value);
        Assert.Equal(1, walk.Unreadable);
    }

    [Fact]
    public void A_read_that_did_not_finish_is_unreadable_and_carries_no_length()
    {
        string pkg = Folder("cut");
        File.WriteAllBytes(Path.Combine(pkg, "cut.bgl"), new byte[100]);

        var walk = SceneryPackageDisk.WalkBgls(pkg, "test", _ => false);

        Assert.Null(walk.Files["cut.bgl"]);
        Assert.Equal(1, walk.Unreadable);
    }

    [Fact]
    public void A_file_the_simulator_holds_open_for_writing_is_still_read()
    {
        string pkg = Folder("shared");
        string held = Path.Combine(pkg, "held.bgl");
        File.WriteAllBytes(held, BglPlacementReaderTests.BuildBgl((1.0, 2.0, 0.0, Guid.NewGuid())));

        using var simulator = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        var walk = SceneryPackageDisk.WalkBgls(pkg, "test", _ => true);

        Assert.NotNull(walk.Files["held.bgl"]);
        Assert.Equal(0, walk.Unreadable);
    }

    [Fact]
    public void A_package_folder_that_is_not_there_throws_for_the_caller_to_judge()
        => Assert.ThrowsAny<IOException>(() => SceneryPackageDisk.WalkBgls(Path.Combine(_root, "nope"), "test", _ => true));

    [Fact]
    public void A_layout_stamp_is_its_length_and_mtime_and_nothing_where_there_is_none()
    {
        string pkg = Folder("stamped");
        Assert.False(SceneryPackageDisk.LayoutStamp.TryOf(pkg, out _));
        Assert.Equal(default, SceneryPackageDisk.LayoutStamp.Of(pkg));

        File.WriteAllText(Path.Combine(pkg, "layout.json"), "{}");
        var info = new FileInfo(Path.Combine(pkg, "layout.json"));
        Assert.True(SceneryPackageDisk.LayoutStamp.TryOf(pkg, out var stamp));
        Assert.Equal(new SceneryPackageDisk.LayoutStamp(info.Length, info.LastWriteTimeUtc.Ticks), stamp);
    }

    [Fact]
    public void A_persisted_cache_is_whole_or_absent_and_never_leaves_a_temporary_file()
    {
        string cache = Path.Combine(_root, "cache"), file = Path.Combine(cache, "x.json");
        SceneryPackageDisk.PersistJson(cache, file, "{\"a\":1}");
        Assert.Equal("{\"a\":1}", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(cache, "*.tmp"));

        // A path that cannot take the file (a FOLDER sits there) costs the write and nothing else.
        string blocked = Path.Combine(cache, "blocked.json");
        Directory.CreateDirectory(blocked);
        SceneryPackageDisk.PersistJson(cache, blocked, "{}");
        Assert.True(Directory.Exists(blocked));
        Assert.Empty(Directory.GetFiles(cache, "*.tmp"));
    }

    // ---- the package's own layout.json (review SI-1) ------------------------------------------

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>A layout.json in the schema real packages carry — {"content":[{"path","size","date"}]},
    /// lower-case '/'-separated paths (Fixtures/layout-kpdx-excerpt.json is a verbatim excerpt of one) —
    /// listing <paramref name="bgls"/>. Shared with the census and indexer tests.</summary>
    internal static void WriteLayout(string packageDir, params (string Path, long Size)[] bgls)
        => File.WriteAllText(Path.Combine(packageDir, "layout.json"),
            "{\"content\":[" + string.Join(",", bgls.Select(b =>
                "{\"path\":\"" + b.Path + "\",\"size\":" + b.Size.ToString(CultureInfo.InvariantCulture) +
                ",\"date\":133921371750000001}")) + "]}");

    private string KpdxShaped(string name)
    {
        string pkg = Folder(name);
        File.Copy(Fixture("layout-kpdx-excerpt.json"), Path.Combine(pkg, "layout.json"));
        return pkg;
    }

    [Fact]
    public void The_real_layout_schema_lists_every_bgl_with_its_size_and_ignores_the_rest()
    {
        var listed = SceneryPackageDisk.ListedBgls(KpdxShaped("kpdx-shape"));

        Assert.NotNull(listed);
        Assert.Equal(2, listed!.Count);                                                          // bglindex.bout is not a BGL
        Assert.Equal((long?)423_623_764L, listed["scenery/global/scenery/modellib.bgl"]);
        Assert.Equal((long?)27_804L, listed["Scenery/World/Scenery/flightbeam-airport-kpdx-addl-objects.BGL"]);   // case ignored
    }

    [Fact]
    public void A_layout_json_held_open_for_writing_is_still_read()
    {
        // An installer can hold layout.json open while it works. The list is opened through
        // OpenShared, as every BGL is: File.ReadAllText permits no writer and would fail here.
        string pkg = KpdxShaped("held-layout");
        using var installer = new FileStream(Path.Combine(pkg, "layout.json"), FileMode.Open, FileAccess.ReadWrite,
                                             FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(2, SceneryPackageDisk.ListedBgls(pkg)!.Count);
    }

    [Theory]
    [InlineData(null)]                                   // no layout.json at all
    [InlineData("{}")]                                   // what every other scenery test writes
    [InlineData("{\"content\":{}}")]                     // a content key that is not a list
    [InlineData("{\"content\":[{\"path\":")]             // half-written
    [InlineData("not json")]
    public void A_layout_with_no_content_list_holds_a_scan_to_nothing(string? layout)
    {
        string pkg = Folder("nolist");
        if (layout != null) File.WriteAllText(Path.Combine(pkg, "layout.json"), layout);

        Assert.Null(SceneryPackageDisk.ListedBgls(pkg));
        Assert.Equal(0, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, new SceneryPackageDisk.BglWalk()));
    }

    [Fact]
    public void A_walk_that_saw_every_listed_bgl_at_its_listed_size_is_finished()
    {
        string pkg = KpdxShaped("whole");
        var walk = new SceneryPackageDisk.BglWalk();
        walk.Files["scenery/global/scenery/modelLib.BGL"] = 423_623_764L;                         // the disk's own spelling
        walk.Files["scenery/world/scenery/flightbeam-airport-kpdx-addl-objects.bgl"] = 27_804L;
        walk.Files["scenery/world/scenery/unlisted-extra.bgl"] = 100L;                            // not listed: not the layout's business

        Assert.Equal(0, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, walk));
    }

    [Fact]
    public void A_listed_bgl_read_at_another_length_or_not_found_at_all_is_unfinished()
    {
        string pkg = KpdxShaped("installing");
        var walk = new SceneryPackageDisk.BglWalk();
        walk.Files["scenery/global/scenery/modellib.bgl"] = 400_000_000L;                         // still being written

        Assert.Equal(2, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, walk));                     // …and addl-objects not there at all
    }

    [Fact]
    public void A_listed_bgl_the_walk_could_not_read_is_left_to_the_unreadable_count()
    {
        string pkg = KpdxShaped("locked");
        var walk = new SceneryPackageDisk.BglWalk();
        walk.Files["scenery/global/scenery/modellib.bgl"] = null;                                 // found, not read to the end
        walk.Files["scenery/world/scenery/flightbeam-airport-kpdx-addl-objects.bgl"] = 27_804L;

        Assert.Equal(0, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, walk));                     // never counted twice
    }

    // Measured 2026-09-22: 24 listed BGLs in 6 healthy packages are absent because the vendor's
    // options tool renamed them, in exactly these three shapes — and ONLY these count (pre-flight I13).
    [Theory]
    [InlineData("flightbeam-airport-kpdx-addl-objects.bgl.disabled")]   // iniBuilds EGKK/EGLL/PHNL
    [InlineData("flightbeam-airport-kpdx-addl-objects.bgl.off")]        // Orbx KATL
    [InlineData("flightbeam-airport-kpdx-addl-objects.off")]            // Aerosoft EDDF/ENGM: the extension replaced
    [InlineData("Flightbeam-Airport-KPDX-Addl-Objects.BGL.Disabled")]   // compared ignoring case
    public void A_listed_bgl_an_options_tool_renamed_is_switched_off_not_missing(string renamed)
    {
        string pkg = KpdxShaped("options");
        File.WriteAllBytes(Path.Combine(Folder("options", "scenery", "world", "scenery"), renamed), new byte[27_804]);
        var walk = new SceneryPackageDisk.BglWalk();
        walk.Files["scenery/global/scenery/modellib.bgl"] = 423_623_764L;

        Assert.Equal(0, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, walk));
    }

    // Anything else beside an absent listed BGL leaves it UNFINISHED. An installer that stages its files
    // under temporary names ("X.bgl.part", "X.bgl.tmp") would otherwise have its half-written package
    // cached as complete — the frozen short answer this check exists to prevent — and a same-stem source
    // file is another file, not the BGL switched off.
    [Theory]
    [InlineData("flightbeam-airport-kpdx-addl-objects.bgl.part")]
    [InlineData("flightbeam-airport-kpdx-addl-objects.bgl.tmp")]
    [InlineData("flightbeam-airport-kpdx-addl-objects.xml")]
    public void A_listed_bgl_beside_any_other_sibling_is_still_unfinished(string sibling)
    {
        string pkg = KpdxShaped("staged");
        File.WriteAllBytes(Path.Combine(Folder("staged", "scenery", "world", "scenery"), sibling), new byte[27_804]);
        var walk = new SceneryPackageDisk.BglWalk();
        walk.Files["scenery/global/scenery/modellib.bgl"] = 423_623_764L;

        Assert.Equal(1, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, walk));
    }

    [Fact]
    public void A_neighbour_that_only_shares_a_prefix_is_not_a_renamed_copy()
    {
        // "…addl-objects-2.bgl" is ANOTHER file, not "…addl-objects" switched off.
        string pkg = KpdxShaped("prefix");
        File.WriteAllBytes(Path.Combine(Folder("prefix", "scenery", "world", "scenery"), "flightbeam-airport-kpdx-addl-objects-2.bgl"), new byte[10]);
        var walk = new SceneryPackageDisk.BglWalk();
        walk.Files["scenery/global/scenery/modellib.bgl"] = 423_623_764L;
        walk.Files["scenery/world/scenery/flightbeam-airport-kpdx-addl-objects-2.bgl"] = 10L;

        Assert.Equal(1, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, walk));
    }

    [Fact]
    public void Entries_outside_the_package_or_deeper_than_any_walk_goes_are_not_held_against_it()
    {
        string pkg = Folder("odd-entries");
        string deep = string.Join("/", Enumerable.Repeat("d", SceneryPackageDisk.MaxBglRecursionDepth + 1)) + "/deep.bgl";
        File.WriteAllText(Path.Combine(pkg, "layout.json"),
            "{\"content\":[{\"path\":\"../other/escape.bgl\",\"size\":1},{\"path\":\"C:/abs/rooted.bgl\",\"size\":1}," +
            "{\"path\":\"" + deep + "\",\"size\":1},{\"path\":\"nosize.bgl\"}]}");
        var walk = new SceneryPackageDisk.BglWalk();
        walk.Files["nosize.bgl"] = 12_345L;                                                      // no listed size: held to being present

        Assert.Equal(0, SceneryPackageDisk.UnfinishedLayoutFiles(pkg, walk));
    }
}
