using System;
using System.IO;
using System.Linq;
using MSFSBlindAssist.Patching;
using Xunit;

/// <summary>
/// Pins the two properties that make StaleRuntimesCleanup safe to run on every startup:
/// the RID-pinned GUARD (a portable build must be left completely alone, because there the
/// runtimes tree holds the ONLY copy of e_sqlite3) and the KEEP-LIST (win-x64 stays because
/// the WebView2 package keeps putting its loader back there).
/// </summary>
public class StaleRuntimesCleanupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-runtimes-" + Guid.NewGuid().ToString("N"));

    public StaleRuntimesCleanupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    static readonly string[] ForeignRids =
    {
        "android-arm", "android-arm64", "android-x64", "android-x86", "browser-wasm",
        "ios-arm", "ios-arm64", "iossimulator-arm64", "iossimulator-x64", "iossimulator-x86",
        "linux-arm", "linux-arm64", "linux-armel", "linux-mips64", "linux-musl-arm",
        "linux-musl-arm64", "linux-musl-riscv64", "linux-musl-s390x", "linux-musl-x64",
        "linux-ppc64le", "linux-riscv64", "linux-s390x", "linux-x64", "linux-x86",
        "maccatalyst-arm64", "maccatalyst-x64", "osx-arm64", "osx-x64",
        "win-arm64", "win-x86",
    };

    string RuntimesDir => Path.Combine(_dir, "runtimes");

    void MakeRid(string rid, string fileName)
    {
        string dir = Path.Combine(RuntimesDir, rid, "native");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), "x");
    }

    /// <summary>The exact 32-folder tree a portable build shipped.</summary>
    void MakePortableTree()
    {
        foreach (var rid in ForeignRids) MakeRid(rid, "libe_sqlite3.so");
        MakeRid("win-x64", "e_sqlite3.dll");
        MakeRid("win-x64", "WebView2Loader.dll");
        string winLib = Path.Combine(RuntimesDir, "win", "lib", "net10.0");
        Directory.CreateDirectory(winLib);
        File.WriteAllText(Path.Combine(winLib, "System.Speech.dll"), "x");
    }

    void MakeRidPinned() => File.WriteAllText(Path.Combine(_dir, "e_sqlite3.dll"), "x");

    // ---------- the guard ----------

    [Fact]
    public void Portable_build_is_left_completely_alone() // deleting here would break SQLite outright
    {
        MakePortableTree(); // NO flattened e_sqlite3.dll beside the exe -> portable

        int removed = StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir);

        Assert.Equal(0, removed);
        Assert.Equal(32, Directory.GetDirectories(RuntimesDir).Length);
        Assert.True(File.Exists(Path.Combine(RuntimesDir, "win-x64", "native", "e_sqlite3.dll")));
    }

    [Fact]
    public void IsRidPinnedBuild_keys_on_the_flattened_native_dll()
    {
        Assert.False(StaleRuntimesCleanup.IsRidPinnedBuild(_dir));
        MakeRidPinned();
        Assert.True(StaleRuntimesCleanup.IsRidPinnedBuild(_dir));
    }

    // ---------- the sweep ----------

    [Fact]
    public void Rid_pinned_build_loses_every_foreign_folder_and_keeps_the_windows_ones()
    {
        MakePortableTree();
        MakeRidPinned();

        int removed = StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir);

        Assert.Equal(ForeignRids.Length, removed);
        var left = Directory.GetDirectories(RuntimesDir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "win", "win-x64" }, left);
        // win-x64 is LIVE, not stale - the WebView2 package rewrites its loader there every build.
        Assert.True(File.Exists(Path.Combine(RuntimesDir, "win-x64", "native", "WebView2Loader.dll")));
    }

    [Fact]
    public void Sweep_is_idempotent()
    {
        MakePortableTree();
        MakeRidPinned();

        Assert.Equal(ForeignRids.Length, StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir));
        Assert.Equal(0, StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir));
        Assert.Equal(0, StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir));
    }

    [Fact]
    public void An_empty_runtimes_shell_is_removed_but_a_populated_one_is_not()
    {
        MakeRidPinned();
        MakeRid("linux-x64", "libe_sqlite3.so"); // nothing kept -> folder ends up empty

        Assert.Equal(1, StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir));
        Assert.False(Directory.Exists(RuntimesDir));

        MakeRid("win-x64", "WebView2Loader.dll"); // a kept folder -> shell must survive
        Assert.Equal(0, StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir));
        Assert.True(Directory.Exists(RuntimesDir));
    }

    // ---------- never throws ----------

    [Fact]
    public void Missing_runtimes_folder_and_missing_app_folder_are_both_no_ops()
    {
        MakeRidPinned();
        Assert.Equal(0, StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir));
        Assert.Equal(0, StaleRuntimesCleanup.RemoveForeignRuntimeAssets(
            Path.Combine(_dir, "does", "not", "exist")));
    }

    [Fact]
    public void An_undeletable_folder_is_skipped_without_stopping_the_sweep()
    {
        MakePortableTree();
        MakeRidPinned();

        string locked = Path.Combine(RuntimesDir, "linux-x64", "native", "libe_sqlite3.so");
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            int removed = StaleRuntimesCleanup.RemoveForeignRuntimeAssets(_dir);

            // The locked one survives; every other foreign folder still went.
            Assert.True(Directory.Exists(Path.Combine(RuntimesDir, "linux-x64")));
            Assert.Equal(ForeignRids.Length - 1, removed);
            Assert.False(Directory.Exists(Path.Combine(RuntimesDir, "android-arm")));
            Assert.True(Directory.Exists(Path.Combine(RuntimesDir, "win-x64")));
        }
    }

    // ---------- the keep-list ----------

    [Theory]
    [InlineData("android-arm")]
    [InlineData("browser-wasm")]
    [InlineData("linux-x64")]
    [InlineData("osx-arm64")]
    [InlineData("ios-arm64")]
    [InlineData("maccatalyst-x64")]
    [InlineData("win-arm64")] // right OS, wrong architecture - this app is x64-only
    [InlineData("win-x86")]
    [InlineData("freebsd-x64")] // an allowlist sweeps RIDs nobody has thought of yet
    public void Foreign_rids_are_swept(string rid) => Assert.True(StaleRuntimesCleanup.ShouldRemove(rid));

    [Theory]
    [InlineData("win")]
    [InlineData("win-x64")]
    [InlineData("WIN-X64")] // OrdinalIgnoreCase, never ToLower() - tr-TR folds I to dotless i
    [InlineData("Win")]
    public void Windows_rids_are_kept(string rid) => Assert.False(StaleRuntimesCleanup.ShouldRemove(rid));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_names_are_never_removed(string rid) => Assert.False(StaleRuntimesCleanup.ShouldRemove(rid));
}
