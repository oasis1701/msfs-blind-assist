// Characterization tests for the pure layer of the Fenix documents window: the names the EFB shows
// (Humanize is the EFB's own pipe), the served list, the folder tree in the EFB's order and the
// gateway endpoints. Everything is ordinal and culture-invariant.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixDocumentCatalogTests
{
    static FenixDocumentItem D(string p) => new(p, "Markdown", "Fenix");

    [Theory]
    [InlineData("F__CTL_SPD_BRK_DISAGREE", "F/CTL SPD BRK DISAGREE")]
    [InlineData("ANTI_ICE_CAPT+F__O_PITOT", "ANTI ICE CAPT+F/O PITOT")]
    [InlineData("my-documents", "My Documents")]
    [InlineData("delay_allocation_codes", "Delay Allocation Codes")]
    [InlineData("A-ICE", "A-ICE")]
    [InlineData("ABN-01 [ADV] ECAM ADVISORY", "ABN-01 [ADV] ECAM ADVISORY")]
    [InlineData("C__B", "C/B")]
    [InlineData("", "")]
    public void Humanize_matches_the_EFB_pipe(string segment, string name) => Assert.Equal(name, FenixDocumentNames.Humanize(segment));

    [Fact] public void Humanize_is_culture_proof()
        => Assert.Equal("Ice Detector Items", TestCulture.Run("tr-TR", () => FenixDocumentNames.Humanize("ice_detector_items")));

    [Theory]
    [InlineData("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", "F/CTL SPD BRK DISAGREE", "ECAM > F/CTL")]
    [InlineData("fenix/QRH/BACK COVER/NORMAL CHECKLIST.md", "NORMAL CHECKLIST", "QRH > BACK COVER")]
    [InlineData("fenix/Checklists.md", "Checklists", "")]
    [InlineData("my-documents/notes.pdf", "Notes", "My Documents")]
    public void Names_and_trails(string path, string name, string trail)
    {
        Assert.Equal(name, FenixDocumentNames.DisplayName(path));
        Assert.Equal(trail, FenixDocumentNames.FolderTrail(path));
    }

    [Fact] public void Tree_keeps_the_EFB_order()
    {
        var items = new[] { "fenix/QRH/BACK COVER/NORMAL CHECKLIST.md", "fenix/QRH/BACK COVER/EMER EVAC.md",
            "fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", "fenix/ECAM/AIR/AIR_ENG_1_BLEED_FAULT.md",
            "fenix/User Guide/User Guide.md", "fenix/Checklists.md", "fenix/delay_allocation_codes.md" }.Select(D).ToList();
        var root = FenixDocumentIndex.BuildTree(items);
        Assert.Equal(new[] { "QRH", "ECAM", "User Guide", "My Documents", "Checklists", "Delay Allocation Codes" }, root.Select(n => n.Name));
        Assert.Equal(new[] { "NORMAL CHECKLIST", "EMER EVAC" }, root[0].Children[0].Children.Select(n => n.Name));
        Assert.Equal(new[] { "F/CTL", "AIR" }, root[1].Children.Select(n => n.Name));
        Assert.Equal("my-documents", root[3].Path);
        Assert.Empty(root[3].Children);
        Assert.Equal("fenix/Checklists.md", root[4].Document!.Path);
    }

    [Fact] public void Parse_reads_the_served_list()
    {
        var items = FenixDocumentIndex.Parse("{\"items\":[{\"path\":\"fenix/QRH/BACK COVER/NORMAL CHECKLIST.md\",\"name\":\"NORMAL CHECKLIST.md\",\"kind\":\"Markdown\",\"source\":\"Fenix\",\"pdf\":null},{\"name\":\"x\"}]}")!;
        Assert.Equal(new FenixDocumentItem("fenix/QRH/BACK COVER/NORMAL CHECKLIST.md", "Markdown", "Fenix"), Assert.Single(items));
        Assert.Empty(FenixDocumentIndex.Parse("{\"items\":[]}")!);
    }

    [Fact] public void Parse_keeps_a_date_shaped_path_as_text()
        => Assert.Equal("2026-10-22", Assert.Single(FenixDocumentIndex.Parse("{\"items\":[{\"path\":\"2026-10-22\"}]}")!).Path);

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("not json")] [InlineData("[]")] [InlineData("{\"items\":null}")]
    public void Parse_refuses_what_is_not_a_list(string? json) => Assert.Null(FenixDocumentIndex.Parse(json));

    [Fact] public void ByPath_ignores_case()
        => Assert.True(FenixDocumentIndex.ByPath(new[] { D("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md") })
            .ContainsKey("FENIX/ecam/F__CTL/F__CTL_SPD_BRK_DISAGREE.md"));

    [Theory]
    [InlineData("fenix/ECAM/AIR/AIR_ENG_1+2_BLEED_FAULT.md", "/fenix/documents/content/fenix/ECAM/AIR/AIR_ENG_1%2B2_BLEED_FAULT.md")]
    [InlineData("fenix/QRH/IN FLIGHT PERFORMANCE/ALL ENGINES INOPERATIVE/OPTIMUM & MAXIMUM ALTITUDES.md",
        "/fenix/documents/content/fenix/QRH/IN%20FLIGHT%20PERFORMANCE/ALL%20ENGINES%20INOPERATIVE/OPTIMUM%20%26%20MAXIMUM%20ALTITUDES.md")]
    [InlineData("fenix/QRH/ABNORMAL AND EMERGENCY PROCEDURES/ABN-01 [ADV] ECAM ADVISORY/ECAM ADVISORY CONDITIONS.md",
        "/fenix/documents/content/fenix/QRH/ABNORMAL%20AND%20EMERGENCY%20PROCEDURES/ABN-01%20%5BADV%5D%20ECAM%20ADVISORY/ECAM%20ADVISORY%20CONDITIONS.md")]
    public void Content_escapes_each_segment(string path, string expected) => Assert.Equal(expected, FenixDocumentEndpoints.Content(path));

    [Fact] public void Search_trims_and_escapes()
    {
        Assert.Equal("/fenix/documents/search?q=spd%20brk&limit=10", FenixDocumentEndpoints.Search("  spd brk "));
        Assert.Equal("/fenix/documents/search?q=1%2B2%20bleed&limit=10", FenixDocumentEndpoints.Search("1+2 bleed"));
    }
}
