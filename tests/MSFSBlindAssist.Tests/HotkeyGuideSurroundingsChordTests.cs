using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests;

/// <summary>Same reasoning as HotkeyGuideSayIntentionsChordTests: the guides are prose nothing
/// compiles against, and they are the ONLY in-app place a blind pilot can look a key up. Unlike
/// that test this one enumerates the folder, so a guide added later cannot be forgotten.</summary>
public class HotkeyGuideSurroundingsChordTests
{
    private const string LookAroundDescription = "Look Around";
    private const string SurroundingsWindowDescription = "Surroundings window";

    public static TheoryData<string> AllGuides()
    {
        var data = new TheoryData<string>();
        string dir = Path.Combine(AppContext.BaseDirectory, "HotkeyGuides");
        Assert.True(Directory.Exists(dir), $"Hotkey guides not found where the app looks for them: {dir}");
        foreach (var f in Directory.GetFiles(dir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) data.Add(Path.GetFileName(f));
        return data;
    }

    private static string EntryFor(string guide, string description)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "HotkeyGuides", guide))
                        .Where(l => l.Contains(description, StringComparison.Ordinal)).ToList();
        return Assert.Single(lines).Trim();          // exactly one entry: a stale duplicate fails too
    }

    [Theory, MemberData(nameof(AllGuides))]
    public void Every_guide_lists_Look_Around_under_the_registered_chord(string guide)
        => Assert.StartsWith(HotkeyManager.LookAroundChordText + " ", EntryFor(guide, LookAroundDescription));

    [Theory, MemberData(nameof(AllGuides))]
    public void Every_guide_lists_the_Surroundings_window_under_the_registered_chord(string guide)
        => Assert.StartsWith(HotkeyManager.SurroundingsWindowChordText + " ", EntryFor(guide, SurroundingsWindowDescription));

    [Fact]
    public void There_are_guides_to_check() => Assert.NotEmpty(AllGuides());
}
