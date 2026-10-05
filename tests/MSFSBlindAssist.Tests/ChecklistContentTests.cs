using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What the checklist window (output mode, Shift+C) shows, and where it comes from: the
/// aircraft's own checklist first, then the bundled text file its definition names.
/// </summary>
public class ChecklistContentTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    private static string ChecklistFolder() => Path.Combine(RepoRoot(), "MSFSBlindAssist", "Checklists");

    /// <summary>Every bundled file, by the definitions that name it (main #266 moved the map there).</summary>
    private static IReadOnlyDictionary<string, string> BundledFiles()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var defs = ComboLabelCollapseTests.AllAircraft().Select(o => (MSFSBlindAssist.Aircraft.IAircraftDefinition)o[0])
            .Append(new CowsDA40Definition(DA40Variant.NG))
            .Append(new CowsDA40Definition(DA40Variant.XLS));
        foreach (var d in defs)
            if (d.ChecklistFileName is { } f) map[d.AircraftCode] = f;
        return map;
    }

    private static string ReadBundled(string aircraftCode)
        => File.ReadAllText(Path.Combine(ChecklistFolder(), BundledFiles()[aircraftCode]));

    // ---------- which file ----------

    [Theory]
    [InlineData(DA40Variant.NG, "COWS_DA40NG_Checklist.txt")]
    [InlineData(DA40Variant.XLS, "COWS_DA40XLS_Checklist.txt")]
    public void EachDA40NamesItsOwnFile(DA40Variant variant, string expected)
        => Assert.Equal(expected, new CowsDA40Definition(variant).ChecklistFileName);

    [Fact]
    public void EveryFileInTheFolderIsNamedByAnAircraft()
    {
        // A file nobody names can never be shown, which is how a checklist gets written and
        // then silently never reaches the pilot.
        var named = new HashSet<string>(BundledFiles().Values, StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.GetFiles(ChecklistFolder(), "*.txt"))
            Assert.Contains(Path.GetFileName(path), named);
    }

    /// <summary>
    /// The files reach the pilot only if the build COPIES them next to the exe, one csproj
    /// entry per file — there is no wildcard. A file the build leaves behind reads as
    /// "Checklist file not found" in the window.
    /// </summary>
    [Fact]
    public void TheBuildCopiesEveryNamedFile()
    {
        var csproj = XDocument.Load(Path.Combine(RepoRoot(), "MSFSBlindAssist", "MSFSBlindAssist.csproj"));
        var copied = csproj.Descendants()
            .Where(e => e.Name.LocalName == "None")
            .Where(e => e.Elements().Any(c => c.Name.LocalName == "CopyToOutputDirectory" && c.Value.Trim().Length > 0))
            .Select(e => (string?)e.Attribute("Update") ?? "")
            .Select(p => p.Replace('/', '\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string file in BundledFiles().Values)
            Assert.Contains($"Checklists\\{file}", copied);
    }

    // ---------- the files themselves ----------

    public static IEnumerable<object[]> AllBundledCodes()
        => BundledFiles().Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(AllBundledCodes))]
    public void EveryFileParsesIntoSectionsThatEachHoldItems(string aircraftCode)
    {
        string text = ReadBundled(aircraftCode);
        var sections = ChecklistContent.Parse(text);

        Assert.NotEmpty(sections);
        foreach (var section in sections)
            Assert.True(section.Items.Count > 0, $"{aircraftCode}: [{section.Title}] has no items");
    }

    [Theory]
    [InlineData("COWS_DA40NG")]
    [InlineData("COWS_DA40XLS")]
    public void TheDA40FilesUseEachHeadingOnce(string aircraftCode)
    {
        // The parser keeps a repeated heading apart by numbering it, which is right for the
        // aircraft's own checklist but would be an authoring slip in ours.
        var headings = ReadBundled(aircraftCode).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('[') && l.EndsWith(']'))
            .ToList();

        Assert.Equal(headings.Count, headings.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The two DA40s have different engines, so their checklists must not mix: the NG is a
    /// FADEC diesel with an engine master, an ECU test and glow plugs; the XLS is a Lycoming
    /// with magnetos, a mixture, priming and a fuel selector. A step from the other airframe
    /// is a step the pilot cannot do.
    /// </summary>
    [Theory]
    [InlineData("COWS_DA40NG", new[] { "magneto", "mixture", "priming", "fuel selector", "battery master", "alternator master" })]
    [InlineData("COWS_DA40XLS", new[] { "ECU", "glow", "engine master", "fuel valve", "transfer pump", "electric master", "percent load" })]
    public void ADA40ChecklistNeverNamesTheOtherAirframesControls(string aircraftCode, string[] foreign)
    {
        string text = ReadBundled(aircraftCode);
        foreach (string word in foreign)
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The checklist quotes the same speeds the output-mode speed keys speak, from the one
    /// table both are built on — so a corrected speed cannot leave the checklist behind.
    /// </summary>
    [Theory]
    [InlineData("COWS_DA40NG", DA40Variant.NG)]
    [InlineData("COWS_DA40XLS", DA40Variant.XLS)]
    public void TheDA40ChecklistQuotesTheSameSpeedsAsTheSpeedKeys(string aircraftCode, DA40Variant variant)
    {
        string text = ReadBundled(aircraftCode);
        var s = DA40Speeds.For(variant);

        Assert.Contains($"Rotate {s.Vr:0} KIAS", text, StringComparison.Ordinal);
        Assert.Contains($"Climb {s.VyFlapsTakeoff:0} KIAS with flaps T/O", text, StringComparison.Ordinal);
        Assert.Contains($"Climb {s.VyFlapsUp:0} KIAS, flaps UP", text, StringComparison.Ordinal);
        Assert.Contains($"Best glide {s.VbestGlide:0} KIAS", text, StringComparison.Ordinal);
        Assert.Contains($"T/O {s.VfeTakeoff:0} KIAS, LDG {s.VfeLanding:0} KIAS", text, StringComparison.Ordinal);
        Assert.Contains($"Never exceed {s.Vne:0} KIAS", text, StringComparison.Ordinal);
    }

    // ---------- parsing ----------

    [Fact]
    public void SectionsKeepTheirOrderAndTheirItems()
    {
        var sections = ChecklistContent.Parse("[First]\nOne\nTwo\n\n[Second]\n  Three  \n");

        Assert.Equal(new[] { "First", "Second" }, sections.Select(s => s.Title));
        Assert.Equal(new[] { "One", "Two" }, sections[0].Items);
        Assert.Equal(new[] { "Three" }, sections[1].Items);
    }

    [Fact]
    public void CrLfLineEndingsParseTheSame()
    {
        var sections = ChecklistContent.Parse("[First]\r\nOne\r\nTwo\r\n");

        Assert.Single(sections);
        Assert.Equal(new[] { "One", "Two" }, sections[0].Items);
    }

    [Fact]
    public void ARepeatedHeadingKeepsBothSectionsInPlace()
    {
        // The old parser replaced the first section's list with an empty one, throwing its
        // items away. A checklist reads in order, so the second keeps its own place too.
        var sections = ChecklistContent.Parse("[Notes]\nA\n[Middle]\nB\n[Notes]\nC\n");

        Assert.Equal(new[] { "Notes", "Middle", "Notes 2" }, sections.Select(s => s.Title));
        Assert.Equal(new[] { "A" }, sections[0].Items);
        Assert.Equal(new[] { "C" }, sections[2].Items);
    }

    [Fact]
    public void LinesBeforeTheFirstHeadingAreDropped()
    {
        var sections = ChecklistContent.Parse("stray\n[Only]\nItem\n");

        Assert.Single(sections);
        Assert.Equal(new[] { "Item" }, sections[0].Items);
    }

    [Fact]
    public void AnIndentedLineIsKeptAsAnItem()
    {
        // The aircraft's own checklist renders a clue indented under its checkpoint; the
        // window trims it, but it must still be there.
        var sections = ChecklistContent.Parse("[Page]\nParking Brake ... Set\n   Forwards = go\n");

        Assert.Equal(new[] { "Parking Brake ... Set", "Forwards = go" }, sections[0].Items);
    }

    // ---------- where the text comes from ----------

    [Fact]
    public void TheAircraftsOwnChecklistWinsOverTheBundledFile()
    {
        string text = ChecklistContent.Load("COWS_DA40NG", "COWS_DA40NG_Checklist.txt", _ => "[Native]\nFrom the package\n", ChecklistFolder());

        Assert.StartsWith("[Native]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBundledFileIsUsedWhenThePackageHasNone()
    {
        string text = ChecklistContent.Load("COWS_DA40NG", "COWS_DA40NG_Checklist.txt", _ => null, ChecklistFolder());

        Assert.Equal(File.ReadAllText(Path.Combine(ChecklistFolder(), "COWS_DA40NG_Checklist.txt")), text);
    }

    [Fact]
    public void ABlankNativeChecklistCountsAsNone()
    {
        string text = ChecklistContent.Load("COWS_DA40XLS", "COWS_DA40XLS_Checklist.txt", _ => "   \n", ChecklistFolder());

        Assert.Contains("[Before Starting Engine]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingBundledFileIsReportedAsAnError()
    {
        string empty = Directory.CreateTempSubdirectory("msfsba-checklist-").FullName;
        try
        {
            string text = ChecklistContent.Load("COWS_DA40NG", "COWS_DA40NG_Checklist.txt", _ => null, empty);

            Assert.StartsWith("[Error]", text, StringComparison.Ordinal);
            Assert.Contains("COWS_DA40NG_Checklist.txt", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(empty, recursive: true); }
    }

    // ---------- ticked items ----------

    [Fact]
    public void ATickBelongsToItsOwnAircraft()
    {
        // Both DA40 files open with "[Before Starting Engine] / Preflight inspection
        // complete", and the ticks are kept for the whole session.
        Assert.NotEqual(
            ChecklistContent.ItemKey("COWS_DA40NG", "Before Starting Engine", "Preflight inspection complete"),
            ChecklistContent.ItemKey("COWS_DA40XLS", "Before Starting Engine", "Preflight inspection complete"));
    }

    [Fact]
    public void ATickIsKeyedOnSectionAndItem()
    {
        Assert.Equal(
            ChecklistContent.ItemKey("A320", "Cockpit Prep", "Batteries on"),
            ChecklistContent.ItemKey("A320", "Cockpit Prep", "Batteries on"));
        Assert.NotEqual(
            ChecklistContent.ItemKey("A320", "Cockpit Prep", "Batteries on"),
            ChecklistContent.ItemKey("A320", "Before Start", "Batteries on"));
    }
}
