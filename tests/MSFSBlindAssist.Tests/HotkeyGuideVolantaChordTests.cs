using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests;

/// <summary>Same reasoning as HotkeyGuideSurroundingsChordTests: every guide, the folder enumerated so a
/// guide added later cannot be forgotten, lists the Volanta window once, under the chord the app registers
/// (output mode, Shift+O).</summary>
public class HotkeyGuideVolantaChordTests
{
    private const string VolantaWindowDescription = "Open Volanta window";

    [Theory, MemberData(nameof(HotkeyGuideSurroundingsChordTests.AllGuides), MemberType = typeof(HotkeyGuideSurroundingsChordTests))]
    public void Every_guide_lists_the_Volanta_window_under_the_registered_chord(string guide)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "HotkeyGuides", guide))
                        .Where(l => l.Contains(VolantaWindowDescription, StringComparison.Ordinal)).ToList();
        string entry = Assert.Single(lines).Trim();   // exactly one entry: a stale duplicate fails too
        Assert.StartsWith(HotkeyManager.VolantaChordText + " ", entry);
    }
}
