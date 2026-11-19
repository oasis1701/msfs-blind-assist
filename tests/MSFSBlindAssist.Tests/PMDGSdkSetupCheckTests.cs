// What the PMDG SDK setup check (an Aircraft-menu pick of the PMDG 737 or 777, or startup with one
// selected, [PSDK-2]) finds on disk, and what the setup dialog is given to say: every installed variant's options file is READ (never written, [PSDK-1]),
// and each one still missing the [SDK] lines becomes one row the pilot can open.

using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

public class PMDGSdkSetupCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-pmdg-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static readonly string[] Configured737 =
        { "[SDK]", "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1", "", "[IRS]", "LastPosValid=0" };
    private static readonly string[] Unconfigured = { "[IRS]", "LastPosValid=0" };

    private PMDGStorageRoot Root(string name, string simulator) => new(Path.Combine(_root, name), simulator);

    private static string WriteOptionsFile(PMDGStorageRoot root, string packageFolder, string fileName, string[] lines)
    {
        string work = Path.Combine(root.Path, packageFolder, "work");
        Directory.CreateDirectory(work);
        string path = Path.Combine(work, fileName);
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void No_options_file_anywhere_needs_attention_with_nothing_to_list()
    {
        var result = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B737, new[] { Root("MSFS2024", "MSFS 2024") });

        Assert.Equal(0, result.FilesFound);
        Assert.Empty(result.Problems);
        Assert.True(result.NeedsAttention);
    }

    [Fact]
    public void Every_variant_configured_needs_no_attention()
    {
        var root = Root("MSFS2024", "MSFS 2024");
        WriteOptionsFile(root, "pmdg-aircraft-737", "737_Options.ini", Configured737);
        WriteOptionsFile(root, "pmdg-aircraft-738", "737_Options.ini", Configured737);

        var result = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B737, new[] { root });

        Assert.Equal(2, result.FilesFound);
        Assert.False(result.NeedsAttention);
    }

    [Fact]
    public void Only_the_variant_missing_the_lines_is_listed_by_its_aircraft_name()
    {
        var root = Root("MSFS2024", "MSFS 2024");
        WriteOptionsFile(root, "pmdg-aircraft-737", "737_Options.ini", Configured737);
        string broken = WriteOptionsFile(root, "pmdg-aircraft-738", "737_Options.ini", Unconfigured);

        var result = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B737, new[] { root });

        Assert.True(result.NeedsAttention);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(broken, problem.Path);
        Assert.Equal("737-800: no [SDK] section", problem.ToString());
    }

    [Fact]
    public void The_same_variant_in_both_simulators_is_told_apart_by_simulator()
    {
        var fs2024 = Root("MSFS2024", "MSFS 2024");
        var fs2020 = Root("packages", "MSFS 2020");
        WriteOptionsFile(fs2024, "pmdg-aircraft-77w", "777_Options.ini", Unconfigured);
        WriteOptionsFile(fs2020, "pmdg-aircraft-77w", "777_Options.ini", Unconfigured);

        var result = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B777, new[] { fs2024, fs2020 });

        Assert.Equal(new[] { "777-300ER, MSFS 2024: no [SDK] section", "777-300ER, MSFS 2020: no [SDK] section" },
            result.Problems.Select(p => p.ToString()));
    }

    [Fact]
    public void A_file_the_simulator_has_open_for_writing_is_still_read()
    {
        var root = Root("MSFS2024", "MSFS 2024");
        string path = WriteOptionsFile(root, "pmdg-aircraft-738", "737_Options.ini", Configured737);

        // PMDG's own writer holding the file open must neither block the check nor be blocked by it.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            var result = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B737, new[] { root });

            Assert.Equal(1, result.FilesFound);
            Assert.False(result.NeedsAttention);
        }
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_listed_rather_than_skipped()
    {
        var root = Root("MSFS2024", "MSFS 2024");
        string path = WriteOptionsFile(root, "pmdg-aircraft-738", "737_Options.ini", Configured737);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B737, new[] { root });

            var problem = Assert.Single(result.Problems);
            Assert.StartsWith("737-800: could not be read", problem.ToString());
        }
    }

    [Theory]
    [InlineData("pmdg-aircraft-736", "737-600")]
    [InlineData("pmdg-aircraft-737", "737-700")]
    [InlineData("pmdg-aircraft-738", "737-800")]
    [InlineData("pmdg-aircraft-739", "737-900")]
    [InlineData("pmdg-aircraft-77er", "777-200ER")]
    [InlineData("pmdg-aircraft-77l", "777-200LR")]
    [InlineData("PMDG-AIRCRAFT-77F", "777F")]
    [InlineData("pmdg-aircraft-77w", "777-300ER")]
    [InlineData("pmdg-aircraft-73x-beta", "pmdg-aircraft-73x-beta")]
    public void A_package_folder_reads_as_its_aircraft(string packageFolder, string spoken)
    {
        Assert.Equal(spoken, PMDGSdkFamily.VariantName(packageFolder));
    }

    [Theory]
    [InlineData("PMDG_737", "737_Options.ini", false)]
    [InlineData("PMDG_777", "777_Options.ini", true)]
    public void Each_PMDG_profile_checks_its_own_file_and_lines(string aircraftCode, string fileName, bool centerCdu)
    {
        var family = PMDGSdkFamily.ForAircraftCode(aircraftCode);

        Assert.NotNull(family);
        Assert.Equal(fileName, family!.OptionsFileName);
        Assert.Equal(centerCdu, family.RequiresCenterCdu);
    }

    [Theory]
    [InlineData("TFDI_MD11")]
    [InlineData("IFLY_737MAX8")]
    [InlineData("BBS_757")]
    [InlineData(null)]
    public void Any_other_profile_is_not_checked(string? aircraftCode)
    {
        Assert.Null(PMDGSdkFamily.ForAircraftCode(aircraftCode));
    }

    [Fact]
    public void The_instructions_count_the_files_to_fix()
    {
        var root = Root("MSFS2024", "MSFS 2024");
        WriteOptionsFile(root, "pmdg-aircraft-738", "737_Options.ini", Unconfigured);
        var one = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B737, new[] { root });
        WriteOptionsFile(root, "pmdg-aircraft-739", "737_Options.ini", Unconfigured);
        var two = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B737, new[] { root });

        Assert.Contains("1 file to fix", PMDGSdkSetupCheck.Instructions(one));
        Assert.Contains("2 files to fix", PMDGSdkSetupCheck.Instructions(two));
    }

    [Fact]
    public void With_no_file_yet_the_instructions_name_the_file_to_look_for()
    {
        var result = PMDGSdkSetupCheck.Run(PMDGSdkFamily.B777, new[] { Root("MSFS2024", "MSFS 2024") });

        string text = PMDGSdkSetupCheck.Instructions(result);

        Assert.Contains("777_Options.ini", text);
        Assert.DoesNotContain("to fix", text);
    }
}
