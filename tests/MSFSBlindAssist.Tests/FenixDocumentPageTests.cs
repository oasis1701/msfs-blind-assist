// Characterization tests for the inert reading page of the Fenix documents window: the served HTML
// becomes a script-free page (policy first, pictures and scripted widgets replaced by text) and a
// plain-text fallback. Everything is ordinal and culture-invariant.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixDocumentPageTests
{
    const string Doc = "fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md";
    static string Served() => FenixDocumentPage.ParseHtml(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fenix-document-spd-brk-disagree.json")))!;

    [Fact] public void Build_wraps_the_served_document_in_an_inert_page()
    {
        string page = FenixDocumentPage.Build(Doc, Served());
        Assert.StartsWith("<!DOCTYPE html>", page);
        Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src http://127.0.0.1:8083; style-src 'unsafe-inline'\">", page);
        Assert.Contains("<title>F/CTL SPD BRK DISAGREE</title>", page);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Regex.Matches(page, "<h4\\b").Count);
        Assert.Equal(2, Regex.Matches(page, "<h5\\b").Count);
    }

    [Fact] public void Served_markup_lands_after_the_policy()
    {
        string page = FenixDocumentPage.Build("my-documents/x.md", "<script>alert(1)</script><meta http-equiv=\"refresh\" content=\"0;url=http://example.com\">");
        Assert.True(page.IndexOf("Content-Security-Policy", StringComparison.Ordinal) < page.IndexOf("<script>", StringComparison.Ordinal));
        Assert.True(page.IndexOf("<body>", StringComparison.Ordinal) < page.IndexOf("<script>", StringComparison.Ordinal));
    }

    [Fact] public void Served_meta_tags_are_removed()
    {
        string page = FenixDocumentPage.Build("my-documents/x.md",
            "<p>x</p><meta http-equiv=\"refresh\" content=\"0;url=http://example.com\"><META HTTP-EQUIV=\"Refresh\" content=\"0;url=https://example.com\">");
        Assert.DoesNotContain("refresh", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", page, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(page, "Content-Security-Policy"));
        Assert.Equal(2, Regex.Matches(page, "<meta\\b", RegexOptions.IgnoreCase).Count);

        // Nested: removing the inner tag must not join "<me" and "ta ..." into a working meta.
        string nested = FenixDocumentPage.Build("my-documents/x.md",
            "<p>x</p><me<meta>ta http-equiv=\"refresh\" content=\"0;url=https://example.com\">");
        Assert.Equal(2, Regex.Matches(nested, "<meta\\b", RegexOptions.IgnoreCase).Count);
    }

    [Theory]
    [InlineData("3,4,5", "Flight phases: 3, 4, 5")]
    [InlineData("3a,4,5,7,8", "Flight phases: 3 + (a), 4, 5, 7, 8")]
    [InlineData("4a,5a", "Flight phases: 4 + (a), 5 + (a)")]
    [InlineData("", "Flight phases: none")]
    public void Flight_phase_pictures_become_text(string phases, string text)
    {
        string page = FenixDocumentPage.Build(Doc, $"<p><img src=\"http://127.0.0.1:8083/fenix/documents/inhibit.png?phases={phases}\" alt=\"Flight phase\" /></p>");
        Assert.Contains(text, page);
        Assert.DoesNotContain("inhibit.png", page);
    }

    [Fact] public void Checklist_lines_become_tick_boxes()
    {
        string page = FenixDocumentPage.Build(Doc, Served());
        Assert.Contains("<label><input type=\"checkbox\"> SPD BRK LEVER: RETRACT</label>", page);
        Assert.Contains("<label><input type=\"checkbox\"> SPD BRK: DO NOT USE</label>", page);
        Assert.Contains("<label><input type=\"checkbox\"> DES TO FL 100/MEA-MORA</label>", FenixDocumentPage.Build(Doc,
            "<div class=\"checklist checklist--no-value\" data-checklist-item-id=\"checklist-item-8\" data-item-index=\"2\"><span>DES TO FL 100/MEA-MORA</span><span></span></div>"));
        Assert.Contains("GEAR PINS &amp; COVERS: REMOVED", FenixDocumentPage.Build(Doc,
            "<div class=\"checklist\" data-checklist-item-id=\"checklist-item-1\" data-item-index=\"0\"><span>GEAR PINS &amp; COVERS</span><span>REMOVED</span></div>"));
    }

    [Fact] public void Script_only_buttons_go_and_every_section_opens()
    {
        string page = FenixDocumentPage.Build(Doc, Served() + "<button type=\"button\" class=\"cl-complete-btn\" data-checklist-action=\"toggle-all\" data-checklist-target-id=\"checklist-1\" data-checklist-action-id=\"checklist-action-1\">COMPLETE</button>");
        Assert.DoesNotContain("<button", page);
        Assert.Contains("<details open class=\"card\" data-card-id=\"card-1\">", page);
        Assert.Equal(Regex.Matches(page, "<details\\b").Count, Regex.Matches(page, "<details open\\b").Count);
    }

    [Fact] public void Colours_are_said_in_words()
    {
        Assert.Contains("F/CTL SPD BRK DISAGREE<span class=\"colour-word\"> (amber)</span></h1>", FenixDocumentPage.Build(Doc, Served()));
        Assert.Contains("<strong>LAND ASAP</strong> <span class=\"colour-word\"> (red)</span></p>", FenixDocumentPage.Build(Doc, "<p class=\"doc-color-red\"><strong>LAND ASAP</strong> </p>"));
        Assert.Contains("LAND ASAP<span class=\"colour-word\"> (amber)</span></span>", FenixDocumentPage.Build(Doc, "<span class=\"doc-color-amber\">LAND ASAP</span><br />"));
    }

    [Fact] public void Only_Fenix_documents_carry_the_notice_and_titles_are_encoded()
    {
        Assert.Contains("FOR FLIGHT SIMULATOR USE ONLY", FenixDocumentPage.Build(Doc, "<p>x</p>"));
        Assert.DoesNotContain("FOR FLIGHT SIMULATOR USE ONLY", FenixDocumentPage.Build("my-documents/notes.md", "<p>x</p>"));
        Assert.Contains("<title>OPTIMUM &amp; MAXIMUM ALTITUDES</title>",
            FenixDocumentPage.Build("fenix/QRH/IN FLIGHT PERFORMANCE/ALL ENGINES INOPERATIVE/OPTIMUM & MAXIMUM ALTITUDES.md", "<p>x</p>"));
    }

    [Fact] public void Placeholder_is_an_inert_page_with_the_hint()
    {
        string page = FenixDocumentPage.Placeholder("No document open", "Choose a document in the list and press Enter.");
        Assert.StartsWith("<!DOCTYPE html>", page);
        Assert.Single(Regex.Matches(page, Regex.Escape("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src http://127.0.0.1:8083; style-src 'unsafe-inline'\">")));
        Assert.Contains("<title>No document open</title>", page);
        Assert.Contains("<p>Choose a document in the list and press Enter.</p>", page);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FenixDocumentPage.Notice, page);
        Assert.Contains("<title>Fish &amp; chips</title>", FenixDocumentPage.Placeholder("Fish & chips", "x"));
    }

    [Theory] [InlineData("de-DE")] [InlineData("en-US")]
    public void Plain_text_reads_the_procedure_in_order(string culture) => Assert.Equal(string.Join("\r\n",
        "F/CTL SPD BRK DISAGREE (amber)", "ANNUNCIATIONS", "Triggering Conditions:",
        "This alert triggers when there is a position disagree between surfaces and lever position.",
        "Flight Phase Inhibition:", "Flight phases: 3, 4, 5", "PROCEDURES", "SPD BRK LEVER: RETRACT", "SPD BRK: DO NOT USE",
        "INOP SYS", "SPD BRK 3+4", "STEEP APPR", "FOR FLIGHT SIMULATOR USE ONLY"),
        TestCulture.Run(culture, () => FenixDocumentPage.ToPlainText(Doc, Served())));

    [Theory] [InlineData(null)] [InlineData("oops")] [InlineData("{\"contentsList\":[]}")]
    public void ParseHtml_without_html_is_null(string? json) => Assert.Null(FenixDocumentPage.ParseHtml(json));
}
