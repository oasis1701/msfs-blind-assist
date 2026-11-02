// Every installed PMDG 737 variant (-600 to -900) carries its own 737NG3.wasm, in Community or in one of
// the Official folders (OneStore one level deeper). The FMA reader analyses each one it finds. MSFS 2024's
// package keeps it under the airplane's attachments\pmdg\Function_Interior_* folder, MSFS 2020's in the
// airplane's own panel folder (a pilot's install, 2026-11-02).

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Tests;

public class WasmModuleFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-pmdg737-wasm-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string WriteFile(string packageFolder, string airplane, string interior, string fileName = "737NG3.wasm")
    {
        string panel = Path.Combine(_root, packageFolder, "SimObjects", "Airplanes", airplane, "attachments", "pmdg", interior, "panel");
        Directory.CreateDirectory(panel);
        string path = Path.Combine(panel, fileName);
        File.WriteAllBytes(path, new byte[] { 0x00, 0x61, 0x73, 0x6D });
        return path;
    }

    [Fact]
    public void FindInstalled_finds_community_and_onestore_variants()
    {
        string community = WriteFile(Path.Combine("Community", "pmdg-aircraft-738"), "PMDG 737-800", "Function_Interior_738");
        string oneStore = WriteFile(Path.Combine("Official2024", "OneStore", "pmdg-aircraft-737"), "PMDG 737-700", "Function_Interior_737");
        WriteFile(Path.Combine("Community", "pmdg-aircraft-77w"), "PMDG 777-300ER", "Function_Interior_77W");        // the 777
        WriteFile(Path.Combine("StreamedPackages", "pmdg-aircraft-739"), "PMDG 737-900", "Function_Interior_739");   // not a packages folder
        WriteFile(Path.Combine("Community", "pmdg-aircraft-736"), "PMDG 737-600", "Function_Interior_736", "other.wasm"); // another file
        WriteFile(Path.Combine("Community", "pmdg-aircraft-735"), "PMDG 737-500", "Function_Interior_735");          // not -600 to -900

        var found = ModuleFiles.FindInstalled(_root, Pmdg737Module.Description);

        Assert.Equal(new[] { community, oneStore }.OrderBy(p => p, StringComparer.Ordinal), found);
    }

    /// <summary>An MSFS 2020 package's file: SimObjects\Airplanes\&lt;airplane&gt;\panel\737NG3.wasm.</summary>
    private string WriteMsfs2020File(string packageFolder, string airplane, string fileName = "737NG3.wasm")
    {
        string panel = Path.Combine(_root, packageFolder, "SimObjects", "Airplanes", airplane, "panel");
        Directory.CreateDirectory(panel);
        string path = Path.Combine(panel, fileName);
        File.WriteAllBytes(path, new byte[] { 0x00, 0x61, 0x73, 0x6D });
        return path;
    }

    [Fact]
    public void FindInstalled_finds_the_msfs_2020_package_layout()
    {
        // A pilot's MSFS 2020 install: one package, four airplanes, each with its own copy in its panel folder.
        string package = Path.Combine("Community", "pmdg-aircraft-738");
        var expected = new[] { "PMDG 737-800", "PMDG 737-800BBJ2", "PMDG 737-800BCF", "PMDG 737-800BDSF" }
            .Select(a => WriteMsfs2020File(package, a)).ToList();
        WriteMsfs2020File(package, "PMDG 737-800", "msfs_navigation_data_interface.wasm"); // another file
        WriteMsfs2020File(Path.Combine("Community", "pmdg-aircraft-77w"), "PMDG 777-300ER");    // not the 737

        var found = ModuleFiles.FindInstalled(_root, Pmdg737Module.Description);

        Assert.Equal(expected.OrderBy(p => p, StringComparer.Ordinal), found);
    }

    [Fact]
    public void FindInstalled_finds_both_layouts_in_one_root_each_file_once()
    {
        string attachments = WriteFile(Path.Combine("Community", "pmdg-aircraft-738"), "PMDG 737-800", "Function_Interior_738");
        string panel = WriteMsfs2020File(Path.Combine("Community", "pmdg-aircraft-738"), "PMDG 737-800");

        var found = ModuleFiles.FindInstalled(_root, Pmdg737Module.Description);

        Assert.Equal(new[] { attachments, panel }.OrderBy(p => p, StringComparer.Ordinal), found);
    }

    [Fact]
    public void FindInstalled_gives_an_empty_list_for_a_missing_root()
    {
        Assert.Empty(ModuleFiles.FindInstalled(Path.Combine(_root, "missing"), Pmdg737Module.Description));
    }
}
