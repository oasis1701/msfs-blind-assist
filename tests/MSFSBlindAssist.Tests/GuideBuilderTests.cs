// Tests for tools/GuideBuilder: GuideChecker (the rules guide/ keeps, GUIDE-1 and GUIDE-2), OnePageBuilder (the
// one-page guide shipped in every zip) and SiteLinkChecker (links in a built copy of the website). The checker and
// builder are pure, so most cases are a small guide written as literals; the last tests run both over the real
// guide/ folder, which is what keeps it sound on every PR.

using GuideBuilder;

namespace MSFSBlindAssist.Tests;

public class GuideBuilderTests
{
    private const string Index = "---\ntitle: \"User guide\"\n---\n\nThe site's own preamble, [one page](one-page.html).\n\n" +
        "## Part 1: Start {#part-1}\n\nPart text with [a link](beta.md#beta-b).\n\n1. [Alpha](alpha.md)\n2. [Beta](beta.md)\n";

    private const string Alpha = "---\ntitle: \"Alpha\"\n---\n\n" +
        "<div class=\"guide-part\" markdown=\"1\">\n\nChapter 1 of 2 in [Part 1: Start](index.md#part-1).\n\n</div>\n\n" +
        "<nav class=\"chapter-toc\" aria-labelledby=\"in-this-chapter\" markdown=\"1\">\n\n" +
        "<p id=\"in-this-chapter\"><strong>In this chapter:</strong></p>\n\n- [A](#alpha-a)\n\n</nav>\n\n" +
        "## A {#alpha-a}\n\nSee [Beta's B](beta.md#beta-b), [Beta](beta.md) and the [contents](index.md).\n\n" +
        "### A one {#alpha-a-one}\n\n" +
        "<div class=\"callout note\" role=\"note\" markdown=\"1\">\n\n**Note:** a [jump](#alpha-a).\n\n</div>\n\n" +
        "<nav class=\"guide-nav\" aria-label=\"Guide chapters\" markdown=\"1\">\n\n- [Next chapter: Beta](beta.md)\n- [Guide contents](index.md)\n\n</nav>\n";

    private const string Beta = "---\ntitle: \"Beta\"\n---\n\n## B {#beta-b}\n\n" +
        "<div class=\"table-wrap\" markdown=\"1\">\n\n| Key | Does |\n|---|---|\n| A | Thing |\n\n</div>\n";

    private static Dictionary<string, string> Guide(params (string File, string Text)[] overrides)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["index.md"] = Index, ["alpha.md"] = Alpha, ["beta.md"] = Beta };
        foreach ((string file, string text) in overrides) files[file] = text;
        return files;
    }

    private static IReadOnlyList<string> Check(params (string File, string Text)[] overrides)
        => GuideChecker.Check(GuideSource.FromFiles(Guide(overrides)));

    private static string BetaWith(string extra) => Beta + "\n" + extra + "\n";

    [Fact]
    public void A_well_formed_guide_has_no_problems()
        => Assert.Empty(Check());

    [Fact]
    public void Crlf_files_read_the_same()
        => Assert.Empty(GuideChecker.Check(GuideSource.FromFiles(Guide().ToDictionary(f => f.Key, f => f.Value.Replace("\n", "\r\n")))));

    [Theory]
    [InlineData("## No id here", "has no {#id}")]
    [InlineData("#### Too deep {#beta-deep}", "jumps from level 2 to level 4")]
    [InlineData("# Title again {#beta-title}", "level-1 heading")]
    [InlineData("## Again {#alpha-a}", "already used by alpha.md")]
    [InlineData("## Clash {#alpha}", "chapter's own id")]
    [InlineData("## Reserved {#contents}", "the one-page guide")]
    [InlineData("## Odd {#bad.id}", "letters, digits")]
    [InlineData("   ## Indented, no id", "has no {#id}")]
    [InlineData("Underlined text\n---", "a line of = or -")]
    [InlineData("Underlined text\n=====", "a line of = or -")]
    public void Headings_need_explicit_unique_ids_and_no_skipped_level(string heading, string problem)
        => Assert.Contains(Check(("beta.md", BetaWith(heading))), p => p.StartsWith("beta.md:", StringComparison.Ordinal) && p.Contains(problem));

    [Theory]
    [InlineData("See [B]({{ '/guide/beta/' | relative_url }}).", "Liquid")]
    [InlineData("See <a href=\"alpha.md\">alpha</a>.", "raw HTML link")]
    [InlineData("See [B](/guide/beta/).", "never a site path")]
    [InlineData("See [B](../alpha/).", "never a site path")]
    [InlineData("See [B](gamma.md).", "not in the guide")]
    [InlineData("See [B](alpha.md#nowhere).", "no heading with the id \"nowhere\"")]
    [InlineData("See [B](#nowhere).", "no heading on this page has")]
    [InlineData("See [B]().", "no target")]
    [InlineData("![](diagram.png)", "no alt text")]
    [InlineData("![A diagram of the taxi tone](images/tones.png)", "describe what it shows in words")]
    [InlineData("[ref]: alpha.md", "reference-style")]
    [InlineData("See the [whole guide on one page](one-page.html).", "only the contents page")]
    [InlineData("See [Alpha's\nA section](alpha.md#nowhere).", "no heading with the id \"nowhere\"")]
    public void Links_are_plain_relative_page_links_that_resolve(string line, string problem)
        => Assert.Contains(Check(("beta.md", BetaWith(line))), p => p.StartsWith("beta.md:", StringComparison.Ordinal) && p.Contains(problem));

    [Theory]
    [InlineData("Press \\[ then \\]; a line reading \\[SDK\\] and \\[a\\](#nowhere).")]
    [InlineData("See [the site](https://msfsblindassist.com/) and [mail](mailto:x@example.com).")]
    [InlineData("```\n{{ liquid }} and [x](#nowhere)\n## Not a heading\n```")]
    [InlineData("Text above a rule.\n\n---")]
    [InlineData("- A list item\n---")]
    [InlineData("A paragraph with a [ stray bracket.\n\nAnd a [real link](#beta-b) later.")]
    public void Escaped_brackets_external_links_code_and_rules_pass(string line)
        => Assert.Empty(Check(("beta.md", BetaWith(line))));

    [Fact]
    public void Every_chapter_is_in_the_contents_once()
    {
        IReadOnlyList<string> unlisted = Check(("gamma.md", "---\ntitle: \"Gamma\"\n---\n\n## G {#gamma-g}\n"));
        IReadOnlyList<string> twice = Check(("index.md", Index + "3. [Alpha](alpha.md)\n"));
        IReadOnlyList<string> missing = Check(("index.md", Index + "3. [Gamma](gamma.md)\n"));

        Assert.Contains(unlisted, p => p.StartsWith("gamma.md:", StringComparison.Ordinal) && p.Contains("not in index.md"));
        Assert.Contains(twice, p => p.Contains("lists alpha.md twice"));
        Assert.Contains(missing, p => p.Contains("lists gamma.md, which does not exist"));
    }

    [Theory]
    [InlineData("## B {#beta-b}\n", "no front matter")]
    [InlineData("---\nlayout: default\n---\n\n## B {#beta-b}\n", "no title")]
    [InlineData("---\ntitle: \"Beta\"\n\n## B {#beta-b}\n", "no closing ---")]
    public void Every_page_has_a_titled_front_matter(string text, string problem)
        => Assert.Contains(Check(("beta.md", text)), p => p.StartsWith("beta.md:", StringComparison.Ordinal) && p.Contains(problem));

    [Fact]
    public void A_missing_contents_page_is_reported()
    {
        Dictionary<string, string> files = Guide();
        files.Remove("index.md");

        Assert.Contains(GuideChecker.Check(GuideSource.FromFiles(files)), p => p.StartsWith("index.md: missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Front_matter_titles_are_unquoted()
    {
        GuideSource source = GuideSource.FromFiles(new Dictionary<string, string>
        {
            ["a.md"] = "---\ntitle: \"Say \\\"hello\\\"\"\n---\n",
            ["b.md"] = "---\ntitle: 'Plain'\n---\n",
            ["c.md"] = "---\ntitle: Bare words\n---\n",
        });

        Assert.Equal(new[] { "Say \"hello\"", "Plain", "Bare words" }, source.Pages.Values.Select(p => p.Title));
    }

    [Fact]
    public void Contents_reads_parts_their_intro_and_chapters_in_order()
    {
        GuidePart part = Assert.Single(GuideContents.Parse(GuideSource.FromFiles(Guide()).Pages["index.md"].Body));

        Assert.Equal("Part 1: Start", part.Title);
        Assert.Equal("part-1", part.Id);
        Assert.Equal("Part text with [a link](beta.md#beta-b).", part.Intro);
        Assert.Equal(new[] { "alpha.md", "beta.md" }, part.ChapterFiles);
    }

    [Theory]
    [InlineData("[B](beta.md#beta-b)", "[B](#beta-b)")]
    [InlineData("[Beta](beta.md)", "[Beta](#beta)")]
    [InlineData("[Contents](index.md)", "[Contents](#contents)")]
    [InlineData("[Part](index.md#part-1)", "[Part](#part-1)")]
    [InlineData("[One page](one-page.html)", "[One page](#contents)")]
    [InlineData("[Here](#alpha-a)", "[Here](#alpha-a)")]
    [InlineData("[Site](https://msfsblindassist.com/guide/)", "[Site](https://msfsblindassist.com/guide/)")]
    [InlineData("![Map](map.png)", "![Map](map.png)")]
    public void One_page_links_become_jumps_within_the_page(string markdown, string expected)
        => Assert.Equal(expected, OnePageBuilder.RewriteLinks(markdown));

    [Fact]
    public void The_one_page_guide_nests_parts_chapters_and_sections()
    {
        string html = OnePageBuilder.Build(GuideSource.FromFiles(Guide()), GuideEdition.Local, null).Html!;

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("<h2 id=\"contents\">Contents</h2>", html);
        Assert.Contains("<h2 id=\"part-1\">Part 1: Start</h2>", html);
        Assert.Contains("<h3 id=\"alpha\">Alpha</h3>", html);
        Assert.Contains("<h4 id=\"alpha-a\">A</h4>", html);
        Assert.Contains("<h5 id=\"alpha-a-one\">A one</h5>", html);
        Assert.Contains("<h3 id=\"beta\">Beta</h3>", html);
        Assert.Contains("<li><a href=\"#alpha\">Alpha</a></li>", html);
        Assert.Contains("<html lang=\"en\">", html);
    }

    [Fact]
    public void The_one_page_guide_rewrites_links_and_drops_the_sites_chapter_navigation()
    {
        string html = OnePageBuilder.Build(GuideSource.FromFiles(Guide()), GuideEdition.Local, null).Html!;

        Assert.Contains("href=\"#beta-b\"", html);
        Assert.Contains("href=\"#beta\"", html);
        Assert.DoesNotContain(".md", html);
        Assert.DoesNotContain("in-this-chapter", html);
        Assert.DoesNotContain("Next chapter", html);
        Assert.DoesNotContain("Chapter 1 of 2", html);
        Assert.DoesNotContain("site's own preamble", html);
        Assert.Contains("Part text with <a href=\"#beta-b\">a link</a>.", html);
    }

    [Fact]
    public void The_one_page_guide_renders_markdown_inside_callouts_and_tables()
    {
        string html = OnePageBuilder.Build(GuideSource.FromFiles(Guide()), GuideEdition.Local, null).Html!;

        Assert.Contains("<div class=\"callout note\" role=\"note\">", html);
        Assert.Contains("<strong>Note:</strong>", html);
        Assert.Contains("<th>Key</th>", html);
        Assert.DoesNotContain("markdown=", html);
        Assert.DoesNotContain("**", html);
        Assert.DoesNotContain("{#", html);
    }

    [Theory]
    [InlineData(GuideEdition.Release, "8.3.0", "This guide covers MSFS Blind Assist 8.3.0.")]
    [InlineData(GuideEdition.Release, null, "This guide covers the released version")]
    [InlineData(GuideEdition.Preview, "8.3.1-pre.4", "the preview build 8.3.1-pre.4 of MSFS Blind Assist")]
    [InlineData(GuideEdition.Local, null, "built from the source code")]
    public void The_one_page_guide_says_which_build_it_describes(GuideEdition edition, string? version, string expected)
    {
        string html = OnePageBuilder.Build(GuideSource.FromFiles(Guide()), edition, version).Html!;

        Assert.Contains(expected, html);
        Assert.Contains($"<a href=\"{OnePageBuilder.OnlineGuideUrl}\">", html);
    }

    [Fact]
    public void The_one_page_guide_is_refused_when_the_guide_has_problems()
    {
        OnePageResult result = OnePageBuilder.Build(GuideSource.FromFiles(Guide(("beta.md", BetaWith("## No id")))), GuideEdition.Local, null);

        Assert.False(result.Ok);
        Assert.Contains(result.Problems, p => p.Contains("has no {#id}"));
    }

    [Fact]
    public void The_one_page_guide_rewrites_a_link_that_wraps_and_demotes_an_indented_heading()
    {
        string beta = BetaWith("See the [first\nsection of Alpha](alpha.md#alpha-a).\n\n   ### Indented {#beta-indented}");
        string html = OnePageBuilder.Build(GuideSource.FromFiles(Guide(("beta.md", beta))), GuideEdition.Local, null).Html!;

        Assert.Contains("href=\"#alpha-a\">first\nsection of Alpha</a>", html);
        Assert.Contains("<h5 id=\"beta-indented\">Indented</h5>", html);
    }

    [Fact]
    public void A_site_block_that_never_closes_is_refused_rather_than_dropping_the_chapter()
    {
        string alpha = Alpha.Replace("Chapter 1 of 2 in [Part 1: Start](index.md#part-1).\n\n</div>", "Chapter 1 of 2 in [Part 1: Start](index.md#part-1).");
        OnePageResult result = OnePageBuilder.Build(GuideSource.FromFiles(Guide(("alpha.md", alpha))), GuideEdition.Local, null);

        Assert.False(result.Ok);
        Assert.Contains(result.Problems, p => p.StartsWith("alpha.md:2:", StringComparison.Ordinal) && p.Contains("does not close before the heading"));
    }

    [Fact]
    public void A_site_block_left_open_at_the_end_of_a_page_is_refused()
    {
        string beta = BetaWith("<nav class=\"guide-nav\" aria-label=\"Guide chapters\" markdown=\"1\">\n\n- [Guide contents](index.md)");
        OnePageResult result = OnePageBuilder.Build(GuideSource.FromFiles(Guide(("beta.md", beta))), GuideEdition.Local, null);

        Assert.False(result.Ok);
        Assert.Contains(result.Problems, p => p.StartsWith("beta.md:", StringComparison.Ordinal) && p.Contains("the end of the page"));
    }

    [Fact]
    public void A_site_block_closed_in_another_case_or_spacing_still_ends()
    {
        string alpha = Alpha.Replace("of 2 in [Part 1: Start](index.md#part-1).\n\n</div>", "of 2 in [Part 1: Start](index.md#part-1).\n\n</DIV >");
        string html = OnePageBuilder.Build(GuideSource.FromFiles(Guide(("alpha.md", alpha))), GuideEdition.Local, null).Html!;

        Assert.Contains("<h4 id=\"alpha-a\">A</h4>", html);
        Assert.DoesNotContain("Chapter 1 of 2", html);
    }

    [Fact]
    public void The_one_page_guide_is_refused_when_it_would_load_a_relative_file()
    {
        OnePageResult result = OnePageBuilder.Build(GuideSource.FromFiles(Guide(("beta.md", BetaWith("<img src=\"tones.png\" alt=\"Tones\">")))),
            GuideEdition.Local, null);

        Assert.False(result.Ok);
        Assert.Contains(result.Problems, p => p.Contains("tones.png") && p.Contains("not shipped"));
    }

    [Fact]
    public void A_heading_too_deep_for_the_one_page_guide_is_refused()
    {
        string deep = BetaWith("### C {#beta-c}\n\n#### D {#beta-d}\n\n##### E {#beta-e}");
        OnePageResult result = OnePageBuilder.Build(GuideSource.FromFiles(Guide(("beta.md", deep))), GuideEdition.Local, null);

        Assert.False(result.Ok);
        Assert.Contains(result.Problems, p => p.Contains("too deep"));
    }

    [Fact]
    public void Site_links_must_reach_a_built_page_and_its_id()
    {
        string root = Directory.CreateTempSubdirectory("guide-site-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "guide", "beta"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<p>home</p>");
            File.WriteAllText(Path.Combine(root, "guide", "beta", "index.html"), "<h2 id=\"beta-b\">B</h2>");
            File.WriteAllText(Path.Combine(root, "guide", "index.html"),
                "<a href=\"/guide/beta/#beta-b\">ok</a> <a href=\"beta/\">ok</a> <a href=\"/\">home</a> <a href=\"#top\">top</a> <a id=\"top\"></a>" +
                "<a href=\"https://example.com/x\">out</a> <a href=\"/guide/beta/#nope\">bad id</a> <a href=\"/guide/gamma/\">gone</a> " +
                "<a href=\"beta.md#beta-b\">raw md</a>");

            IReadOnlyList<string> problems = SiteLinkChecker.Check(root, "guide");

            Assert.Equal(3, problems.Count);
            Assert.Contains(problems, p => p.Contains("/guide/beta/#nope") && p.Contains("no id"));
            Assert.Contains(problems, p => p.Contains("/guide/gamma/") && p.Contains("does not have"));
            Assert.Contains(problems, p => p.Contains("beta.md#beta-b") && p.Contains("unconverted"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RealGuide() => Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "guide");

    [Fact]
    public void The_real_guide_passes_its_checks()
        => Assert.Empty(GuideChecker.Check(GuideSource.FromFolder(RealGuide())));

    [Fact]
    public void The_real_guide_builds_one_page()
    {
        OnePageResult result = OnePageBuilder.Build(GuideSource.FromFolder(RealGuide()), GuideEdition.Release, "1.0.0");

        Assert.Empty(result.Problems);
        Assert.True(result.Ok);
    }
}
