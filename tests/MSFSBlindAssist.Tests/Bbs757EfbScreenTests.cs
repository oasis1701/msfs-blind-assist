using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Loads a 757 tablet-reader fixture as the page model the pilot is given. The fixtures are the page's serialized
/// model (<c>&lt;set&gt;/&lt;name&gt;.model.json</c>, written by <see cref="Bbs757EfbModelJson"/> from the captured pages); the
/// window, extras and settings tests exercise their code against these real captured-page models without a screenshot. (Named a loader, kept as
/// <c>Bbs757EfbScreenTests</c> so its many call sites are unchanged.)
/// </summary>
internal static class Bbs757EfbScreenTests
{
    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "Fixtures", "bbs757-efb");
    }

    /// <summary>The page model of a captured fixture.</summary>
    internal static Bbs757EfbScreenModel Load(string set, string name) =>
        Read(Path.Combine(FixtureDir(), set, name + ".model.json"));

    /// <summary>A named variant of a fixture's page (e.g. "remove": the AIRAC page with UPDATE read as REMOVE when no
    /// newer cycle waits).</summary>
    internal static Bbs757EfbScreenModel LoadVariant(string set, string name, string variant) =>
        Read(Path.Combine(FixtureDir(), set, name + "." + variant + ".model.json"));

    private static Bbs757EfbScreenModel Read(string path) => Bbs757EfbModelJson.Read(File.ReadAllText(path));
}
