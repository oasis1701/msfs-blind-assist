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
    public void The_community_folder_is_returned_only_when_it_is_really_there()
    {
        string packages = WriteUserCfg("Roaming/Microsoft Flight Simulator 2024", Path.Combine(_root, "p2024"));
        string roaming = Path.Combine(_root, "Roaming"), local = Path.Combine(_root, "Local");

        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local));             // packages root, but no Community under it
        Directory.CreateDirectory(Path.Combine(packages, "Community"));
        Assert.Equal(Path.Combine(packages, "Community"), MsfsPackagesLocator.TryGetCommunityPath("FS2024", roaming, local));
        Assert.Null(MsfsPackagesLocator.TryGetCommunityPath("FS2020", roaming, local));             // no config for that simulator at all
    }
}
