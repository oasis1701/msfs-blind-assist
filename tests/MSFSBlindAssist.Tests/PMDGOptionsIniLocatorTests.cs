// PMDG's per-variant options.ini lives in one of four places depending on simulator generation
// (FS2020/FS2024) and storefront (MS Store/Steam) — confirmed against PMDG's own forum, not
// guessed. These tests pin the four candidate paths and their ordering; the actual "does this
// file exist" check is thin, untested IO glue (PMDGOptionsIniLocator.FindExisting), consistent
// with this codebase's convention of not unit-testing raw disk access.

using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

public class PMDGOptionsIniLocatorTests
{
    [Fact]
    public void Builds_all_four_storefront_and_generation_candidates()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", "FS2024");

        Assert.Equal(4, candidates.Count);
    }

    [Fact]
    public void Every_candidate_uses_the_family_prefixed_ini_filename()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-77f", "777", "FS2024");

        Assert.All(candidates, path => Assert.EndsWith("777_Options.ini", path));
    }

    [Fact]
    public void Every_candidate_includes_the_package_folder_name()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-739", "737", "FS2024");

        Assert.All(candidates, path => Assert.Contains("pmdg-aircraft-739", path));
    }

    [Fact]
    public void Includes_the_FS2024_MS_Store_work_folder_under_the_WASM_MSFS2024_tree()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", "FS2024");

        Assert.Contains(candidates, path =>
            path.Contains("Microsoft.Limitless_8wekyb3d8bbwe", StringComparison.Ordinal) &&
            path.Contains(Path.Combine("WASM", "MSFS2024"), StringComparison.Ordinal) &&
            path.Contains(Path.Combine("pmdg-aircraft-738", "work"), StringComparison.Ordinal));
    }

    [Fact]
    public void Includes_the_FS2024_Steam_work_folder_under_the_WASM_MSFS2024_tree()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", "FS2024");

        Assert.Contains(candidates, path =>
            path.Contains("Microsoft Flight Simulator 2024", StringComparison.Ordinal) &&
            path.Contains(Path.Combine("WASM", "MSFS2024"), StringComparison.Ordinal) &&
            path.Contains(Path.Combine("pmdg-aircraft-738", "work"), StringComparison.Ordinal));
    }

    [Fact]
    public void Includes_the_FS2020_MS_Store_work_folder_under_LocalState_packages()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", "FS2020");

        Assert.Contains(candidates, path =>
            path.Contains("Microsoft.FlightSimulator_8wekyb3d8bbwe", StringComparison.Ordinal) &&
            path.Contains(Path.Combine("LocalState", "packages", "pmdg-aircraft-738", "work"), StringComparison.Ordinal));
    }

    [Fact]
    public void Includes_the_FS2020_Steam_work_folder_under_Microsoft_Flight_Simulator_Packages()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", "FS2020");

        Assert.Contains(candidates, path =>
            !path.Contains("2024", StringComparison.Ordinal) &&
            path.Contains(Path.Combine("Microsoft Flight Simulator", "Packages", "pmdg-aircraft-738", "work"), StringComparison.Ordinal));
    }

    [Fact]
    public void Tries_the_detected_running_simulators_own_paths_first_when_FS2020_is_running()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", "FS2020");

        // The FS2020 candidates (WASM\MSFS2024 absent) must be tried before the FS2024 ones.
        Assert.DoesNotContain("MSFS2024", candidates[0]);
        Assert.DoesNotContain("MSFS2024", candidates[1]);
    }

    [Fact]
    public void Tries_the_detected_running_simulators_own_paths_first_when_FS2024_is_running()
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", "FS2024");

        Assert.Contains("MSFS2024", candidates[0]);
        Assert.Contains("MSFS2024", candidates[1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    public void An_unresolved_running_simulator_tries_FS2024_first_as_the_newer_sim(string? runningSimulatorVersion)
    {
        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths("pmdg-aircraft-738", "737", runningSimulatorVersion);

        Assert.Contains("MSFS2024", candidates[0]);
    }
}
