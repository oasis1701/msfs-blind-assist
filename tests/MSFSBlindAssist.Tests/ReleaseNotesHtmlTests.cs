using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ReleaseNotesHtml.Build turns a GitHub release body (Markdown) into the HTML document
/// the update dialog's WebView2 renders. The inputs pinned here are the shapes the two
/// real note producers emit: ChangelogRenderer ("## Heading" + "- bullet", LF-only) and
/// GitHub's generated notes ("## What's Changed" + "* title by @user in URL").
///
/// The security property matters most: release bodies carry third-party text (PR titles
/// from contributors), so raw HTML in the Markdown must render as escaped text, never as
/// markup — and the page must carry no script of its own.
/// </summary>
public class ReleaseNotesHtmlTests
{
    [Fact]
    public void Build_RendersChangelogHeadingAsH2()
    {
        var html = ReleaseNotesHtml.Build("## New features\n\n- Something new.\n");

        Assert.Contains("<h2>New features</h2>", html);
    }

    [Fact]
    public void Build_RendersBulletsAsListItems()
    {
        var html = ReleaseNotesHtml.Build("## Fixes\n\n- First fix.\n- Second fix.\n");

        Assert.Contains("<ul>", html);
        Assert.Contains("<li>First fix.</li>", html);
        Assert.Contains("<li>Second fix.</li>", html);
    }

    [Fact]
    public void Build_RendersBoldAndCodeSpans_ThePreviewNotesLead()
    {
        // The exact lead preview.yml writes: **Preview build `8.0.1-pre.7`**, built from `4f7e7ba`.
        var html = ReleaseNotesHtml.Build("**Preview build `8.0.1-pre.7`**, built from `4f7e7ba`.");

        Assert.Contains("<strong>Preview build <code>8.0.1-pre.7</code></strong>", html);
        Assert.Contains("<code>4f7e7ba</code>", html);
    }

    [Fact]
    public void Build_RendersThematicBreak()
    {
        var html = ReleaseNotesHtml.Build("above\n\n---\n\nbelow");

        Assert.Contains("<hr", html);
    }

    [Fact]
    public void Build_TreatsLfOnlyLineBreaksAsHardBreaks()
    {
        // Both producers emit LF-only text, and GitHub renders release bodies with hard
        // line breaks — a paragraph's inner newline must become a <br>, not be swallowed.
        var html = ReleaseNotesHtml.Build("line one\nline two");

        Assert.Contains("<br", html);
    }

    [Fact]
    public void Build_MakesMarkdownLinksAnchors()
    {
        var html = ReleaseNotesHtml.Build("[the release](https://github.com/oasis1701/msfs-blind-assist/releases)");

        Assert.Contains("<a href=\"https://github.com/oasis1701/msfs-blind-assist/releases\">the release</a>", html);
    }

    [Fact]
    public void Build_AutoLinksBareUrls_TheGeneratedNotesShape()
    {
        // GitHub's generated list: "* title by @user in https://github.com/.../pull/184".
        var html = ReleaseNotesHtml.Build("* A change by @robin24 in https://github.com/oasis1701/msfs-blind-assist/pull/184");

        Assert.Contains("<a href=\"https://github.com/oasis1701/msfs-blind-assist/pull/184\">", html);
    }

    [Fact]
    public void Build_EscapesRawHtml_NeverRendersIt()
    {
        // A PR title is third-party text. HTML-shaped input must come out as escaped
        // text, not markup.
        var html = ReleaseNotesHtml.Build("* Fix <b>bold</b> handling <script>alert(1)</script>");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;", html);
    }

    [Fact]
    public void Build_EscapesHtmlBlocks_NotJustInlineHtml()
    {
        // At line start, <div> is an HTML *block* in Markdown — a different code path
        // from inline HTML, and it must be neutralized the same way.
        var html = ReleaseNotesHtml.Build("<div onclick=\"x()\">block</div>");

        Assert.DoesNotContain("<div", html);
        Assert.Contains("&lt;div", html);
    }

    [Fact]
    public void Build_ContainsNoScriptTagOfItsOwn()
    {
        // The page itself must carry no JS: scripting is disabled in the WebView, and
        // nothing in the wrapper should depend on it. The positive assertion keeps this
        // honest — an empty document would "contain no script" too.
        var html = ReleaseNotesHtml.Build("## Fixes\n\n- A fix.\n");

        Assert.Contains("<li>A fix.</li>", html);
        Assert.DoesNotContain("<script", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Build_EmptyBody_SaysNoNotesAvailable(string? markdown)
    {
        var html = ReleaseNotesHtml.Build(markdown);

        Assert.Contains("No release notes available.", html);
    }

    [Fact]
    public void Build_IsACompleteDocumentWithTitleAndLanguage()
    {
        var html = ReleaseNotesHtml.Build("- A change.");

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"en\">", html);
        // The document title is what NVDA announces when focus enters the web view.
        Assert.Contains("<title>Release notes</title>", html);
        Assert.Contains("charset=\"utf-8\"", html);
    }

    [Fact]
    public void Build_RendersTheFullPreviewNotesShapeEndToEnd()
    {
        // The exact document preview.yml assembles, LF line endings and all.
        const string body =
            "**Preview build `8.0.1-pre.7`**, built from `4f7e7ba`.\n" +
            "\n" +
            "This is a rolling preview: it is replaced every time a change lands on main.\n" +
            "It contains everything merged since v8.0.0.\n" +
            "\n" +
            "---\n" +
            "\n" +
            "## Fixes\n" +
            "\n" +
            "- Docking no longer says complete when you are parked askew.\n";

        var html = ReleaseNotesHtml.Build(body);

        Assert.Contains("<strong>Preview build <code>8.0.1-pre.7</code></strong>", html);
        Assert.Contains("<hr", html);
        Assert.Contains("<h2>Fixes</h2>", html);
        Assert.Contains("<li>Docking no longer says complete when you are parked askew.</li>", html);
    }
}
