// The disk rules SceneryPackageCensus and SceneryPackageIndexer share (review CL-4). Synthetic BGLs
// from BglPlacementReaderTests.BuildBgl — never a payware file.
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
}
