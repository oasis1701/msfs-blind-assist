// PMDG keeps each variant's <family>_Options.ini in that package's "work" folder, in one of four
// storage trees (FS2024/FS2020 x Store/Steam). The check looks for EVERY installed variant of the
// family, since each variant has its own file and each needs the [SDK] lines.

using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

public class PMDGOptionsIniLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-pmdg-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string WriteOptionsFile(string storageRoot, string packageFolder, string fileName)
    {
        string work = Path.Combine(storageRoot, packageFolder, "work");
        Directory.CreateDirectory(work);
        string path = Path.Combine(work, fileName);
        File.WriteAllLines(path, new[] { "[IRS]", "LastPosValid=0" });
        return path;
    }

    [Fact]
    public void The_storage_roots_are_the_FS2024_WASM_trees_then_the_FS2020_package_trees()
    {
        var roots = PMDGOptionsIniLocator.StorageRoots(@"C:\Local", @"C:\Roaming");

        Assert.Equal(new[]
        {
            @"C:\Local\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2024",
            @"C:\Roaming\Microsoft Flight Simulator 2024\WASM\MSFS2024",
            @"C:\Local\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages",
            @"C:\Roaming\Microsoft Flight Simulator\Packages",
        }, roots.Select(r => r.Path));
        Assert.Equal(new[] { "MSFS 2024", "MSFS 2024", "MSFS 2020", "MSFS 2020" }, roots.Select(r => r.Simulator));
    }

    [Fact]
    public void Finds_every_installed_variant_of_the_family_and_nothing_else()
    {
        string root = Path.Combine(_root, "MSFS2024");
        string p738 = WriteOptionsFile(root, "pmdg-aircraft-738", "737_Options.ini");
        string p736 = WriteOptionsFile(root, "pmdg-aircraft-736", "737_Options.ini");
        WriteOptionsFile(root, "pmdg-aircraft-77w", "777_Options.ini");          // the other family
        WriteOptionsFile(root, "fenix-a320", "737_Options.ini");                  // not a PMDG package
        Directory.CreateDirectory(Path.Combine(root, "pmdg-aircraft-739", "work")); // never loaded: no file yet

        var found = PMDGOptionsIniLocator.FindOptionsFiles(
            new[] { new PMDGStorageRoot(root, "MSFS 2024") }, PMDGSdkFamily.B737);

        Assert.Equal(new[] { p736, p738 }, found.Select(f => f.Path));
        Assert.Equal(new[] { "pmdg-aircraft-736", "pmdg-aircraft-738" }, found.Select(f => f.PackageFolder));
        Assert.All(found, f => Assert.Equal("MSFS 2024", f.Simulator));
    }

    [Fact]
    public void Looks_in_every_root_and_tags_each_file_with_its_simulator()
    {
        string fs2024 = Path.Combine(_root, "MSFS2024");
        string fs2020 = Path.Combine(_root, "packages");
        string in2024 = WriteOptionsFile(fs2024, "pmdg-aircraft-77w", "777_Options.ini");
        string in2020 = WriteOptionsFile(fs2020, "pmdg-aircraft-77w", "777_Options.ini");

        var found = PMDGOptionsIniLocator.FindOptionsFiles(
            new[] { new PMDGStorageRoot(fs2024, "MSFS 2024"), new PMDGStorageRoot(fs2020, "MSFS 2020") },
            PMDGSdkFamily.B777);

        Assert.Equal(new[] { in2024, in2020 }, found.Select(f => f.Path));
        Assert.Equal(new[] { "MSFS 2024", "MSFS 2020" }, found.Select(f => f.Simulator));
    }

    [Fact]
    public void A_root_that_does_not_exist_finds_nothing_rather_than_throwing()
    {
        var found = PMDGOptionsIniLocator.FindOptionsFiles(
            new[] { new PMDGStorageRoot(Path.Combine(_root, "absent"), "MSFS 2020") }, PMDGSdkFamily.B737);

        Assert.Empty(found);
    }
}
