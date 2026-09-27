using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public class MsfsPackagesLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-usercfg-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    /// <summary>A UserCfg.opt at one of the four real locations, naming <paramref name="packages"/>.</summary>
    private string WriteUserCfg(string relativeFolder, string packages, string? extraLineBefore = null)
    {
        string dir = Path.Combine(_root, relativeFolder);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(packages);
        var lines = new List<string> { "{Graphics", "  Version 1.1.0" };
        if (extraLineBefore != null) lines.Add(extraLineBefore);
        lines.Add($"InstalledPackagesPath \"{packages}\"");
        lines.Add("}");
        File.WriteAllLines(Path.Combine(dir, "UserCfg.opt"), lines);
        return packages;
    }

    [Fact]
    public void Reads_the_quoted_path_whatever_surrounds_it()
    {
        var lines = new[] { "{Graphics", "  Version 1.1.0", "InstalledPackagesPath \"F:\\msfs2024\"", "}" };
        Assert.Equal("F:\\msfs2024", MsfsPackagesLocator.ParseInstalledPackagesPath(lines));
        Assert.Equal("C:\\Users\\a b\\Packages", MsfsPackagesLocator.ParseInstalledPackagesPath(new[] { "\tinstalledpackagespath   \"C:\\Users\\a b\\Packages\"  " }));
    }

    [Fact]
    public void No_such_line_or_an_unquoted_one_is_null()
    {
        Assert.Null(MsfsPackagesLocator.ParseInstalledPackagesPath(new[] { "Video 1", "" }));
        Assert.Null(MsfsPackagesLocator.ParseInstalledPackagesPath(new[] { "InstalledPackagesPath" }));
    }

    [Fact]
    public void The_value_ends_at_its_own_closing_quote_not_at_a_later_one()
    {
        // A trailing comment carrying quotes must not be swallowed into the path. (The old
        // first-quote/LAST-quote read returned "F:\msfs2024" // "was E:\old".)
        Assert.Equal("F:\\msfs2024", MsfsPackagesLocator.ParseInstalledPackagesPath(
            new[] { "InstalledPackagesPath \"F:\\msfs2024\"   // was \"E:\\old\"" }));
        Assert.Null(MsfsPackagesLocator.ParseInstalledPackagesPath(new[] { "InstalledPackagesPath \"\"" }));   // quoted nothing names nothing
    }

    [Fact]
    public void The_first_line_carrying_the_key_decides()
    {
        Assert.Equal("D:\\First", MsfsPackagesLocator.ParseInstalledPackagesPath(
            new[] { "InstalledPackagesPath \"D:\\First\"", "InstalledPackagesPath \"D:\\Second\"" }));
    }

    [Fact]
    public void Every_value_the_file_names_is_offered_in_file_order()
    {
        // What the file reader walks: it needs each candidate in turn, because it takes the first
        // one that is a folder on disk. The singular above is this sequence's first element.
        var lines = new[] { "Video 1", "InstalledPackagesPathNextBoot \"D:\\NotYet\"", "InstalledPackagesPath \"D:\\Active\"" };
        Assert.Equal(new[] { "D:\\NotYet", "D:\\Active" }, MsfsPackagesLocator.ParseInstalledPackagesPaths(lines, includeNextBoot: true));
        Assert.Empty(MsfsPackagesLocator.ParseInstalledPackagesPaths(new[] { "Video 1", "InstalledPackagesPath" }, includeNextBoot: true));
    }

    [Fact]
    public void The_roaming_config_is_read_first_and_an_unknown_simulator_reads_only_the_fs2020_one()
    {
        string fs2020 = WriteUserCfg("Roaming/Microsoft Flight Simulator", Path.Combine(_root, "p2020"));
        string fs2024 = WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", Path.Combine(_root, "p2024"));
        WriteUserCfg("Local/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/LocalCache", Path.Combine(_root, "s2020"));
        WriteUserCfg("Local/Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache", Path.Combine(_root, "s2024"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        Assert.Equal(fs2020, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2020", roaming, local));
        Assert.Equal(fs2024, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));

        // An unknown version string reads the FS2020 Roaming folder and NEVER a Store LocalCache —
        // exactly what the method it replaced did (both Store branches were == comparisons against
        // "FS2020"/"FS2024", so neither ran, while the Roaming file name fell to the else branch).
        Assert.Equal(fs2020, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2030", roaming, local));
    }

    [Fact]
    public void The_store_location_is_read_when_the_roaming_one_is_absent()
    {
        string store2020 = WriteUserCfg("Local/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/LocalCache", Path.Combine(_root, "s2020"));
        string store2024 = WriteUserCfg("Local/Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache", Path.Combine(_root, "s2024"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        Assert.Equal(store2020, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2020", roaming, local));
        Assert.Equal(store2024, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));
        Assert.Null(MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2030", roaming, local));
    }

    [Fact]
    public void A_missing_or_keyless_config_and_a_path_that_is_not_there_all_answer_null()
    {
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");
        Assert.Null(MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));     // nothing written yet

        string dir = Path.Combine(roaming, "Microsoft Flight Simulator 2024");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "UserCfg.opt"), new[] { "{Graphics", "  Version 1.1.0", "}" });
        Assert.Null(MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));     // no key

        File.WriteAllLines(Path.Combine(dir, "UserCfg.opt"), new[] { $"InstalledPackagesPath \"{Path.Combine(_root, "gone")}\"" });
        Assert.Null(MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));     // names a folder that is not there
    }

    [Fact]
    public void A_key_line_naming_a_folder_that_is_not_there_does_not_hide_a_later_one_that_is()
    {
        // The behaviour the method this replaced had: it kept reading lines past a path that did
        // not resolve. UserCfg.opt also carries InstalledPackagesPathNextBoot, which can name a
        // relocation that has not happened yet.
        string real = Path.Combine(_root, "real");
        WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", real,
                     extraLineBefore: $"InstalledPackagesPathNextBoot \"{Path.Combine(_root, "not-yet")}\"");
        Assert.Equal(real, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local")));
    }

    [Fact]
    public void A_config_this_process_cannot_read_answers_null_rather_than_throwing()
    {
        string packages = WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", Path.Combine(_root, "p2024"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");
        Assert.Equal(packages, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));

        // The simulator can hold its own config exclusively while it runs.
        using var held = new FileStream(Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                                        FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Null(MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));
    }

    [Fact]
    public void A_config_the_simulator_has_open_for_writing_is_still_read()
    {
        // This read now runs on every FS2024 surroundings catalog build, i.e. while the simulator
        // is running, where on main it ran only during a navdata build. It must not deny the
        // simulator its own config — which is the same share mode, seen from the other side: a
        // reader that permits no writer cannot open a file a writer already holds.
        string packages = WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", Path.Combine(_root, "p2024"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        using var simulator = new FileStream(Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                                             FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        Assert.Equal(packages, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));
    }

    [Fact]
    public void The_community_folder_is_returned_only_when_it_is_really_there()
    {
        string packages = WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", Path.Combine(_root, "p2024"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out bool failed));  // packages root, but no Community under it
        Assert.False(failed);
        Directory.CreateDirectory(Path.Combine(packages, "Community"));
        Assert.Equal(Path.Combine(packages, "Community"), MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out failed));
        Assert.False(failed);
        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2020", roaming, local, out failed));      // no config for that simulator at all
        Assert.False(failed);
    }

    [Fact]
    public void A_config_that_exists_but_cannot_be_read_is_a_read_failure_not_no_community_folder()
    {
        // SI-2: every MSFS 2024 surroundings build asks this. "Could not read the config" and "there
        // is no Community folder" were the same null, so one moment's exclusive lock left a catalog
        // with no scenery tier that was cached as complete and never built again.
        string packages = WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", Path.Combine(_root, "p2024"));
        Directory.CreateDirectory(Path.Combine(packages, "Community"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        Assert.Equal(Path.Combine(packages, "Community"), MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out bool failed));
        Assert.False(failed);

        using var held = new FileStream(Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                                        FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out failed));
        Assert.True(failed);
    }

    [Fact]
    public void Nothing_to_read_is_never_a_read_failure()
    {
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");
        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out bool failed));   // no config anywhere
        Assert.False(failed);

        string dir = Path.Combine(roaming, "Microsoft Flight Simulator 2024");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "UserCfg.opt"), new[] { "{Graphics", "  Version 1.1.0", "}" });
        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out failed));        // no key
        Assert.False(failed);

        File.WriteAllLines(Path.Combine(dir, "UserCfg.opt"), new[] { $"InstalledPackagesPath \"{Path.Combine(_root, "gone")}\"" });
        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out failed));        // names a folder that is not there
        Assert.False(failed);
    }

    [Fact]
    public void A_failed_read_is_reported_even_when_the_store_config_still_answers()
    {
        // The chain still falls through to the Store copy, as the navdata build always has — but the
        // config that WOULD have won could not be read, so the answer is only what could be had.
        WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", Path.Combine(_root, "p2024"));
        string store = WriteUserCfg("Local/Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache", Path.Combine(_root, "s2024"));
        Directory.CreateDirectory(Path.Combine(store, "Community"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        using var held = new FileStream(Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                                        FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal(Path.Combine(store, "Community"), MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out bool failed));
        Assert.True(failed);
    }

    [Fact]
    public void The_community_folder_comes_from_the_active_key_never_the_next_boot_one()
    {
        // The simulator writes InstalledPackagesPathNextBoot as soon as the pilot PICKS a new folder
        // in-sim, and that folder normally exists already — so on the first-existing rule a NextBoot
        // line above the active one won, and the census scanned a Community folder the running
        // simulator is not loading (review SI-5).
        string next = Path.Combine(_root, "next"), active = Path.Combine(_root, "active");
        Directory.CreateDirectory(Path.Combine(next, "Community"));
        WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", active, extraLineBefore: $"InstalledPackagesPathNextBoot \"{next}\"");
        Directory.CreateDirectory(Path.Combine(active, "Community"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        Assert.Equal(Path.Combine(active, "Community"), MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local, out bool failed));
        Assert.False(failed);                                                                      // skipping a line is not a read failure
        // The navdata database build keeps its documented first-existing rule, NextBoot included.
        Assert.Equal(next, MsfsPackagesLocator.TryGetInstalledPackagesPath("FS2024", roaming, local));
    }

    [Fact]
    public void The_active_only_parse_skips_the_next_boot_key_in_any_case_and_any_position()
    {
        var lines = new[] { "InstalledPackagesPathNextBoot \"D:\\NotYet\"", "  InstalledPackagesPath \"D:\\Active\"",
                            "installedpackagespathnextboot \"D:\\Lower\"" };
        Assert.Equal(new[] { "D:\\Active" }, MsfsPackagesLocator.ParseInstalledPackagesPaths(lines, includeNextBoot: false));
        Assert.Equal(new[] { "D:\\NotYet", "D:\\Active", "D:\\Lower" }, MsfsPackagesLocator.ParseInstalledPackagesPaths(lines, includeNextBoot: true));
    }
}
