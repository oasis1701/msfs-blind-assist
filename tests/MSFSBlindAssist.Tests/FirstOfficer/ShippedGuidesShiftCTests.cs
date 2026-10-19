using System;
using System.IO;
using System.Linq;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Shift+C opens the First Officer window, and starts its automation, since the text checklist
/// window was retired (FO-24). Every guide and manual the app ships, both folders enumerated so a
/// file added later cannot be forgotten, must stop promising a passive text checklist: a pilot
/// following one would press Shift+C expecting a list and get the First Officer instead.
/// </summary>
public class ShippedGuidesShiftCTests
{
    public static TheoryData<string> AllShippedGuides()
    {
        var data = new TheoryData<string>();
        foreach (var (folder, pattern) in new[] { ("HotkeyGuides", "*.txt"), ("Guides", "*.html") })
        {
            string dir = Path.Combine(AppContext.BaseDirectory, folder);
            Assert.True(Directory.Exists(dir), $"Shipped guides not found where the app copies them: {dir}");
            foreach (var f in Directory.GetFiles(dir, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                data.Add(Path.Combine(folder, Path.GetFileName(f)));
        }
        return data;
    }

    [Theory, MemberData(nameof(AllShippedGuides))]
    public void No_shipped_guide_promises_the_retired_text_checklist(string guide)
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, guide));
        Assert.DoesNotContain("text checklist", text, StringComparison.OrdinalIgnoreCase);
    }

    // The pilot's guide moved into this repo (guide/, PR #319) after Shift+C changed, so it kept
    // describing the retired window. Every chapter, enumerated so a new one cannot be forgotten.
    public static TheoryData<string> AllGuideChapters()
    {
        var data = new TheoryData<string>();
        string dir = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "guide");
        foreach (var f in Directory.GetFiles(dir, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            data.Add(Path.GetFileName(f));
        return data;
    }

    [Theory, MemberData(nameof(AllGuideChapters))]
    public void No_guide_chapter_promises_the_retired_text_checklist(string chapter)
    {
        string text = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "guide", chapter));
        Assert.DoesNotContain("text checklist", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plain-text checklist", text, StringComparison.OrdinalIgnoreCase);
    }
}
