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
}
