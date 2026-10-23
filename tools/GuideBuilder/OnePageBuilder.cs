using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace GuideBuilder;

/// <summary>Which build of the app a one-page guide describes; it sets the sentence under the title.</summary>
public enum GuideEdition
{
    /// <summary>Built from a working copy, not by a release or preview workflow.</summary>
    Local,
    Release,
    Preview,
}

/// <summary>Outcome of <see cref="OnePageBuilder.Build"/>: the page, or the problems that stopped it.</summary>
public sealed record OnePageResult(string? Html, IReadOnlyList<string> Problems)
{
    public bool Ok => Html is not null;
}

/// <summary>
/// Joins the whole guide into one self-contained HTML page: the copy shipped in every release zip and opened from
/// Help, User Guide, and the site's "whole guide on one page". Parts become level-2 headings, chapters level 3, a
/// chapter's sections level 4 and subsections level 5. Links between pages become jumps within the page, which works
/// because every heading id is unique across the guide (GUIDE-2). The site's per-chapter navigation (the part line,
/// "In this chapter" and the previous and next links) is left out. Pure: the result is a string.
/// </summary>
public static class OnePageBuilder
{
    public const string OnlineGuideUrl = "https://msfsblindassist.com/guide/";
    public const string Title = "MSFS Blind Assist User Guide";

    /// <summary>A chapter page's ## becomes ####: two levels for the part and chapter headings above it.</summary>
    private const int Demotion = 2;

    private static readonly (string Open, Regex Close)[] SiteOnlyBlocks =
    {
        ("<div class=\"guide-part\"", ClosingTag("div")),
        ("<nav class=\"chapter-toc\"", ClosingTag("nav")),
        ("<nav class=\"guide-nav\"", ClosingTag("nav")),
    };

    private static Regex ClosingTag(string tag) => new($@"</{tag}\s*>\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownAttribute = new(@"\s+markdown=""1""", RegexOptions.CultureInvariant);
    private static readonly Regex BlockTagLine = new(@"^</?(?:div|nav|section|aside)\b[^>]*>$", RegexOptions.CultureInvariant);
    private static readonly Regex HtmlId = new(@"\sid=""(?<id>[^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex HtmlHref = new(@"\shref=""(?<href>[^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex HtmlSrc = new(@"\ssrc=""(?<src>[^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex LeftoverAttributes = new(@"\{#[A-Za-z0-9_-]+\}", RegexOptions.CultureInvariant);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGenericAttributes() // reads "## Heading {#id}"; must be added last
        .Build();

    public static OnePageResult Build(GuideSource source, GuideEdition edition, string? version)
    {
        var problems = GuideChecker.Check(source).ToList();
        if (problems.Count > 0)
        {
            return new OnePageResult(null, problems);
        }

        IReadOnlyList<GuidePart> parts = GuideContents.Parse(source.Pages[GuideSource.ContentsFile].Body);
        var markdown = new StringBuilder();
        foreach (GuidePart part in parts)
        {
            markdown.Append("## ").Append(part.Title).Append(" {#").Append(part.Id).Append("}\n\n");
            if (part.Intro.Trim().Length > 0)
            {
                markdown.Append(Transform(GuideSource.ContentsFile, part.Intro, problems)).Append("\n\n");
            }

            foreach (string file in part.ChapterFiles)
            {
                GuidePage page = source.Pages[file];
                markdown.Append("### ").Append(page.Title).Append(" {#").Append(page.Slug).Append("}\n\n");
                markdown.Append(Transform(file, page.Body, problems)).Append("\n\n");
            }
        }

        string html = Frame(parts, source, Markdown.ToHtml(markdown.ToString(), Pipeline), edition, version);
        problems.AddRange(Verify(html));
        return problems.Count > 0 ? new OnePageResult(null, problems) : new OnePageResult(html, problems);
    }

    /// <summary>One page's Markdown, made ready to sit inside the single page.</summary>
    private static string Transform(string fileName, string body, List<string> problems)
    {
        var output = new List<(string Text, bool InFence)>();
        Regex? skippingUntil = null;
        int skipStart = 0;

        foreach ((int number, string raw, bool inFence) in GuideMarkdown.Lines(body))
        {
            string text = raw;
            if (skippingUntil is not null)
            {
                if (inFence) continue;
                if (skippingUntil.IsMatch(text))
                {
                    skippingUntil = null;
                    continue;
                }

                if (!GuideMarkdown.Heading.IsMatch(text)) continue;

                // A navigation block holds no headings, so reaching one means its closing tag is missing; without
                // this the chapter up to the next matching closing tag (a callout's, say) would vanish unnoticed.
                problems.Add(Unclosed(fileName, skipStart, $"the heading on line {number}"));
                skippingUntil = null;
            }

            if (!inFence)
            {
                (string Open, Regex Close) block = SiteOnlyBlocks.FirstOrDefault(b => text.TrimStart().StartsWith(b.Open, StringComparison.Ordinal));
                if (block.Open is not null)
                {
                    if (!block.Close.IsMatch(text))
                    {
                        skippingUntil = block.Close;
                        skipStart = number;
                    }

                    continue;
                }

                Match heading = GuideMarkdown.Heading.Match(text);
                if (heading.Success)
                {
                    Group hashes = heading.Groups["hashes"];
                    int level = hashes.Length + Demotion;
                    if (level > 6)
                    {
                        problems.Add($"{fileName}:{number}: a level-{hashes.Length} heading is too deep for the one-page guide, where it would be level {level}; HTML stops at 6.");
                        level = 6;
                    }

                    text = new string('#', level) + text[(hashes.Index + hashes.Length)..];
                }

                text = MarkdownAttribute.Replace(text, "");

                // CommonMark ends an HTML block at a blank line, so a block tag on its own line needs one either side
                // for the Markdown inside a callout to be read as Markdown, as kramdown's markdown="1" reads it.
                if (BlockTagLine.IsMatch(text.Trim()))
                {
                    output.Add(("", false));
                    output.Add((text.Trim(), false));
                    output.Add(("", false));
                    continue;
                }
            }

            output.Add((text, inFence));
        }

        // Without this the rest of the chapter would silently vanish from the one-page guide.
        if (skippingUntil is not null)
        {
            problems.Add(Unclosed(fileName, skipStart, "the end of the page"));
        }

        // Links are rewritten a run of non-code lines at a time, so a link whose text wraps onto the next line is
        // rewritten too, and nothing inside fenced code is touched.
        var lines = new List<string>();
        var run = new List<string>();
        foreach ((string text, bool inFence) in output)
        {
            if (!inFence)
            {
                run.Add(text);
                continue;
            }

            if (run.Count > 0) lines.Add(RewriteLinks(string.Join("\n", run)));
            run.Clear();
            lines.Add(text);
        }

        if (run.Count > 0) lines.Add(RewriteLinks(string.Join("\n", run)));
        return string.Join("\n", lines).Trim('\n');
    }

    private static string Unclosed(string fileName, int line, string before)
        => $"{fileName}:{line}: this site navigation block does not close before {before}, so the one-page guide would "
            + "leave out what follows; end it with a line holding only its closing tag.";

    /// <summary>"page.md#id" and "#id" become "#id"; "page.md" becomes "#page" (the chapter heading's id).</summary>
    public static string RewriteLinks(string text) => GuideMarkdown.InlineLink.Replace(text, m =>
    {
        if (m.Groups["bang"].Length > 0) return m.Value;
        string target = m.Groups["target"].Value;
        if (GuideMarkdown.IsExternal(target)) return m.Value;

        (string file, string fragment) = GuideMarkdown.Split(target);
        string rewritten = file switch
        {
            "" => "#" + fragment,
            GuideSource.ContentsFile or GuideChecker.OnePageFile => fragment.Length > 0 ? "#" + fragment : "#contents",
            _ => "#" + (fragment.Length > 0 ? fragment : Path.GetFileNameWithoutExtension(file)),
        };
        return $"[{m.Groups["text"].Value}]({rewritten})";
    });

    private static string Frame(IReadOnlyList<GuidePart> parts, GuideSource source, string body, GuideEdition edition, string? version)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        string v = version?.Trim() ?? "";
        string about = edition switch
        {
            GuideEdition.Release => v.Length > 0
                ? $"This guide covers MSFS Blind Assist {E(v)}. The guide for the newest release is online at "
                : "This guide covers the released version of MSFS Blind Assist. The guide for the newest release is online at ",
            GuideEdition.Preview => $"This guide covers the preview build{(v.Length > 0 ? " " + E(v) : "")} of MSFS Blind Assist, which has features "
                + "the released version does not have yet. The guide for the released version is online at ",
            _ => "This copy of the guide was built from the source code, not from a release. The guide for the released version is online at ",
        };

        var contents = new StringBuilder();
        foreach (GuidePart part in parts)
        {
            contents.Append("<li><a href=\"#").Append(E(part.Id)).Append("\">").Append(E(part.Title)).Append("</a>\n<ul>\n");
            foreach (string file in part.ChapterFiles)
            {
                GuidePage page = source.Pages[file];
                contents.Append("<li><a href=\"#").Append(E(page.Slug)).Append("\">").Append(E(page.Title)).Append("</a>");

                // Each chapter's sections, as the site's contents page lists them.
                List<GuideHeading> sections = GuideMarkdown.Headings(page.Body).Where(h => h.Level == 2).ToList();
                if (sections.Count > 0)
                {
                    contents.Append("\n<ul>\n");
                    foreach (GuideHeading section in sections)
                    {
                        contents.Append("<li><a href=\"#").Append(E(section.Id)).Append("\">").Append(E(section.Text)).Append("</a></li>\n");
                    }

                    contents.Append("</ul>\n");
                }

                contents.Append("</li>\n");
            }

            contents.Append("</ul>\n</li>\n");
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <title>{{Title}}</title>
            <style>
            :root { --bg: #ffffff; --fg: #1b1b1b; --accent: #0b57d0; --border: #c4c4c4; --callout: #f1f4f9; }
            @media (prefers-color-scheme: dark) {
              :root { --bg: #121212; --fg: #e6e6e6; --accent: #8ab4f8; --border: #4a4a4a; --callout: #1d232c; }
            }
            body { margin: 0 auto; max-width: 48rem; padding: 1rem; background: var(--bg); color: var(--fg);
                   font: 1.05rem/1.6 system-ui, "Segoe UI", sans-serif; }
            a { color: var(--accent); }
            .callout { border-left: 4px solid var(--accent); background: var(--callout); padding: 0.1rem 1rem; margin: 1rem 0; }
            .table-wrap { overflow-x: auto; }
            table { border-collapse: collapse; }
            th, td { border: 1px solid var(--border); padding: 0.3rem 0.6rem; text-align: left; vertical-align: top; }
            </style>
            </head>
            <body>
            <main id="main">
            <h1>{{Title}}</h1>
            <p>{{about}}<a href="{{OnlineGuideUrl}}">msfsblindassist.com/guide</a>.</p>
            <p>The whole guide is on this page. Parts are headings at level 2, chapters level 3, sections level 4 and subsections level 5, so with a screen reader you can press 2 to move by part and 3 by chapter.</p>
            <nav aria-labelledby="contents">
            <h2 id="contents">Contents</h2>
            <ul>
            {{contents}}</ul>
            </nav>
            {{body}}
            </main>
            </body>
            </html>

            """;
    }

    /// <summary>What must hold of the finished page: unique ids, every link either leaves the guide or jumps to an id on
    /// it, and nothing loaded from a relative path.</summary>
    private static IEnumerable<string> Verify(string html)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in HtmlId.Matches(html))
        {
            if (!ids.Add(m.Groups["id"].Value))
            {
                yield return $"one-page guide: the id \"{m.Groups["id"].Value}\" appears twice.";
            }
        }

        foreach (Match m in HtmlHref.Matches(html))
        {
            string href = WebUtility.HtmlDecode(m.Groups["href"].Value);
            if (GuideMarkdown.IsExternal(href)) continue;
            if (href.StartsWith('#'))
            {
                if (!ids.Contains(href[1..])) yield return $"one-page guide: a link to {href}, which is not on the page.";
                continue;
            }

            yield return $"one-page guide: a link to {href}, which would not work in a page opened from disk.";
        }

        // The page ships alone in the zip, so a relative image or other file it loads would be missing.
        foreach (Match m in HtmlSrc.Matches(html))
        {
            string src = WebUtility.HtmlDecode(m.Groups["src"].Value);
            if (!GuideMarkdown.IsExternal(src))
            {
                yield return $"one-page guide: it loads {src}, which is not shipped with the page.";
            }
        }

        foreach (Match m in LeftoverAttributes.Matches(html))
        {
            yield return $"one-page guide: \"{m.Value}\" was left in the text; an id must end its heading line.";
        }
    }
}
