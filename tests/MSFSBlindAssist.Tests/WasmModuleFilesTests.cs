// Every installed PMDG 737 variant (-600 to -900) carries its own 737NG3.wasm, in Community or in one of
// the Official folders (OneStore one level deeper). The FMA reader analyses each one it finds.

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

    [Fact]
    public void FindInstalled_gives_an_empty_list_for_a_missing_root()
    {
        Assert.Empty(ModuleFiles.FindInstalled(Path.Combine(_root, "missing"), Pmdg737Module.Description));
    }
}
