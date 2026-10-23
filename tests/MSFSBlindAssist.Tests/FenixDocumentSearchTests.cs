// Characterization tests for the Fenix documents window's search results and fixed texts.
// The fixture is the first two live results for "SPD BRK", verbatim.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixDocumentSearchTests
{
    const string SpdBrk = "[{\"path\":\"fenix/ECAM/F__CTL/F__CTL_SPD_BRK_FAULT.md\",\"title\":\"F__CTL_SPD_BRK_FAULT\",\"breadcrumb\":\"fenix > ECAM > F__CTL > F__CTL_SPD_BRK_FAULT\",\"snippet\":\"F CTL <b>SPD</b> <b>BRK</b> FAULT ANNUNCIATIONS Triggering Conditions Thi…\",\"matchIndex\":8},"
        + "{\"path\":\"fenix/QRH/ABNORMAL AND EMERGENCY PROCEDURES/ABN-11 AIR/ENGINE 1+2 BLEED FAULT.md\",\"title\":\"ENGINE 1+2 BLEED FAULT\",\"breadcrumb\":\"x\",\"snippet\":\"…if AIR <b>ENG</b> <b>1</b> <b>BLEED</b> FAULT &amp; more\",\"matchIndex\":175}]";

    [Fact] public void Results_read_name_folder_and_match()
    {
        var r = FenixDocumentSearch.Parse(SpdBrk)!;
        Assert.Equal("F/CTL SPD BRK FAULT, ECAM > F/CTL: F CTL SPD BRK FAULT ANNUNCIATIONS Triggering Conditions Thi…", FenixDocumentSearch.ResultText(r[0]));
        Assert.Equal("ENGINE 1+2 BLEED FAULT, QRH > ABNORMAL AND EMERGENCY PROCEDURES > ABN-11 AIR: …if AIR ENG 1 BLEED FAULT & more", FenixDocumentSearch.ResultText(r[1]));
    }

    [Theory] [InlineData(null)] [InlineData("{}")] [InlineData("oops")]
    public void No_answer_is_null(string? json) => Assert.Null(FenixDocumentSearch.Parse(json));

    [Fact] public void A_date_shaped_snippet_stays_text()
        => Assert.Equal("Checklists: 2026-10-22", FenixDocumentSearch.ResultText(
            Assert.Single(FenixDocumentSearch.Parse("[{\"path\":\"fenix/Checklists.md\",\"snippet\":\"2026-10-22\"}]")!)));

    [Fact] public void Nothing_found_is_empty() => Assert.Empty(FenixDocumentSearch.Parse("[]")!);

    [Theory] [InlineData("de-DE")] [InlineData("en-US")]
    public void Texts_read_the_same_everywhere(string culture) => TestCulture.Run(culture, () =>
    {
        Assert.Equal("772 documents, 1 pinned. Enter opens, F6 moves between the document, the list and the search box, Control+F searches, Control+P opens the pinned document, Control+D pins or unpins, Escape closes.", FenixDocumentTexts.Ready(772, 1));
        Assert.Equal("Pinned (0)", FenixDocumentTexts.PinnedNode(0));
        Assert.Equal("Pinned (12)", FenixDocumentTexts.PinnedNode(12));
        Assert.Equal("Refreshed: 1772 documents, 0 pinned.", FenixDocumentTexts.Refreshed(1772, 0));
        Assert.Equal("Search results for \"spd brk\" (10)", FenixDocumentTexts.ResultsNode("spd brk", 10));
        Assert.Equal("F/CTL SPD BRK DISAGREE, ECAM > F/CTL", FenixDocumentTexts.PinnedItem("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", null));
        Assert.Equal("Checklists, Before flight", FenixDocumentTexts.PinnedItem("fenix/Checklists.md", "Before flight"));
        Assert.Equal("F/CTL SPD BRK DISAGREE. ECAM > F/CTL.", FenixDocumentTexts.Opened("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md"));
        Assert.Equal("Notes (PDF)", FenixDocumentTexts.FileNode(new("my-documents/notes.pdf", "Pdf", "User")));
        return 0;
    });
}
