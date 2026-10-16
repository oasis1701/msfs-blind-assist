using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Where the A220's <c>checklists.json</c> is looked for under one MSFS package root
/// (<see cref="A220ChecklistPack.CandidatePaths"/>). The package is named
/// <c>inibuilds-aircraft-a220</c> on the Marketplace (Official\OneStore) but
/// <c>synaptic-aircraft-a220</c> on the direct-sale install, which keeps the stock pack in
/// Community\...\Config\Default. A loader that knew only the first name told the pilot the
/// checklist file was not found ("Do this item for me") on a direct-sale install.
/// </summary>
public class A220ChecklistPackPathTests : IDisposable
{
    private const string Inibuilds = "inibuilds-aircraft-a220";
    private const string Synaptic = "synaptic-aircraft-a220";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-a220-pack-" + Guid.NewGuid().ToString("N"));

    public A220ChecklistPackPathTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static string Stock(string root, string zone, string package)
        => zone == "Community"
            ? Path.Combine(root, "Community", package, "Config", "Default", "checklists.json")
            : Path.Combine(root, "Official", "OneStore", package, "Config", "Default", "checklists.json");

    private static string CustomDir(string root, string package)
        => Path.Combine(root, "Community", package, "Config", "Synaptic");

    private string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return path;
    }

    [Fact]
    public void CandidatePaths_ListCustomPacksBeforeStock_AndTheInibuildsNameBeforeSynaptic()
    {
        var paths = A220ChecklistPack.CandidatePaths(_root);

        Assert.Equal(new (string Path, bool IsDirectory)[]
        {
            // Custom packs first (manual p.31), the Marketplace name before the direct-sale name.
            (CustomDir(_root, Inibuilds), true),
            (CustomDir(_root, Synaptic), true),
            // Then the stock pack: the Marketplace copy exactly where it always was, then the
            // direct-sale name (a Community package overrides an Official one in the sim).
            (Stock(_root, "Official", Inibuilds), false),
            (Stock(_root, "Community", Synaptic), false),
            (Stock(_root, "Official", Synaptic), false),
        }, paths.Select(p => (p.Path, p.IsDirectory)).ToArray());
    }

    [Fact]
    public void CandidateFilesIn_FindsTheDirectSaleInstall()
    {
        // The user's PC: <Community>\synaptic-aircraft-a220\Config\Default\checklists.json.
        string stock = Touch(Stock(_root, "Community", Synaptic));

        Assert.Equal(new[] { stock }, A220ChecklistPack.CandidateFilesIn(_root));
    }

    [Fact]
    public void CandidateFilesIn_FindsTheMarketplaceInstall_UnderEitherName()
    {
        string inibuilds = Touch(Stock(_root, "Official", Inibuilds));
        Assert.Equal(new[] { inibuilds }, A220ChecklistPack.CandidateFilesIn(_root));

        string synaptic = Touch(Stock(_root, "Official", Synaptic));
        Assert.Equal(new[] { inibuilds, synaptic }, A220ChecklistPack.CandidateFilesIn(_root));
    }

    [Fact]
    public void CandidateFilesIn_YieldsACustomPackBeforeTheStockPack()
    {
        string stock = Touch(Stock(_root, "Community", Synaptic));
        string custom = Touch(Path.Combine(CustomDir(_root, Synaptic), "mine.json"));
        string marketplace = Touch(Stock(_root, "Official", Inibuilds));

        Assert.Equal(new[] { custom, marketplace, stock }, A220ChecklistPack.CandidateFilesIn(_root));
    }

    [Fact]
    public void CandidateFilesIn_IsEmptyWhenNoPackIsInstalled()
    {
        Assert.Empty(A220ChecklistPack.CandidateFilesIn(_root));
    }
}
