using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>
/// Checks the guide/ folder against the rules the site, GitHub's file view and the one-page build rely on (GUIDE-1,
/// GUIDE-2): every chapter is in the contents, with its sections listed under it; every heading carries an explicit, guide-wide unique {#id} and no
/// level is skipped; links between pages are plain relative "page.md#id" links that resolve; no Liquid; images have
/// alt text. Each problem names its file and line.
/// </summary>
public static class GuideChecker
{
    /// <summary>A file the site build adds next to the pages, which a page may link to.</summary>
    public const string OnePageFile = "one-page.html";

    /// <summary>Ids the one-page guide's own frame uses, so no heading may take them.</summary>
    public static readonly IReadOnlySet<string> ReservedIds = new HashSet<string>(StringComparer.Ordinal) { "main", "contents" };

    private static readonly Regex IdShape = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Check(GuideSource source)
    {
        var problems = new List<string>(source.Problems);

        if (!source.Pages.TryGetValue(GuideSource.ContentsFile, out GuidePage? contentsPage))
        {
            problems.Add($"{GuideSource.ContentsFile}: missing; the contents page sets the chapters and their order.");
            return problems;
        }

        CheckContents(source, contentsPage, problems);

        // Guide-wide ids: every heading id, every chapter's slug (its id in the one-page guide) and the reserved ones.
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string reserved in ReservedIds) owners[reserved] = "the one-page guide";
        foreach (GuidePage page in source.Pages.Values.Where(p => p.FileName != GuideSource.ContentsFile))
        {
            owners.TryAdd(page.Slug, $"{page.FileName} (the chapter's own id)");
        }

        foreach (GuidePage page in source.Pages.Values)
        {
            CheckPage(page, owners, problems);
        }

        var idsByFile = source.Pages.Values.ToDictionary(p => p.FileName, p => GuideMarkdown.Ids(p.Body), StringComparer.Ordinal);
        foreach (GuidePage page in source.Pages.Values)
        {
            CheckLinks(page, idsByFile, problems);
        }

        return problems;
    }

    private static void CheckContents(GuideSource source, GuidePage contentsPage, List<string> problems)
    {
        IReadOnlyList<GuidePart> parts = GuideContents.Parse(contentsPage.Body);
        if (parts.Count == 0)
        {
            problems.Add($"{GuideSource.ContentsFile}: has no part headings (## Part … {{#id}}); the one-page guide is built from them.");
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (GuidePart part in parts)
        {
            foreach (string file in part.ChapterFiles)
            {
                if (!listed.Add(file))
                {
                    problems.Add($"{GuideSource.ContentsFile}: lists {file} twice; each chapter appears once in the contents.");
                }
                else if (!source.Pages.ContainsKey(file))
                {
                    problems.Add($"{GuideSource.ContentsFile}: lists {file}, which does not exist.");
                }
            }
        }

        foreach (string file in source.Pages.Keys.Where(f => f != GuideSource.ContentsFile && !listed.Contains(f)))
        {
            problems.Add($"{file}: is not in {GuideSource.ContentsFile}'s numbered chapter lists, so the one-page guide would leave it out.");
        }

        foreach (GuideContentsChapter chapter in parts.SelectMany(p => p.Chapters))
        {
            if (!source.Pages.TryGetValue(chapter.File, out GuidePage? page)) continue;
            string? difference = SectionDifference(chapter.SectionLines, GuideContents.SectionLines(page, chapter.Indent));
            if (difference is not null)
            {
                problems.Add($"{GuideSource.ContentsFile}:{chapter.Line}: the sections listed under {page.Title} ({chapter.File}) do not match its ## headings: "
                    + $"{difference}. Run `dotnet run --project tools/GuideBuilder -- contents --guide guide` to rewrite every chapter's section list.");
            }
        }
    }

    /// <summary>The first way a chapter's section lines in the contents differ from what its headings give, or null.</summary>
    private static string? SectionDifference(IReadOnlyList<string> listed, IReadOnlyList<string> expected)
    {
        for (int i = 0; i < Math.Max(listed.Count, expected.Count); i++)
        {
            string? found = i < listed.Count ? listed[i] : null;
            string? wanted = i < expected.Count ? expected[i] : null;
            if (found == wanted) continue;

            // A heading added or removed shows as a missing or surplus line, not as every later line out of place.
            if (found is null || (wanted is not null && !listed.Contains(wanted) && expected.Contains(found)))
            {
                return $"\"{wanted}\" is missing";
            }

            if (wanted is null || (!expected.Contains(found) && listed.Contains(wanted)))
            {
                return $"\"{found}\" is not one of its sections";
            }

            return $"it reads \"{found}\" where it should read \"{wanted}\"";
        }

        return null;
    }

    private static void CheckPage(GuidePage page, Dictionary<string, string> owners, List<string> problems)
    {
        int previous = 1;
        foreach (GuideHeading heading in GuideMarkdown.Headings(page.Body))
        {
            string at = $"{page.FileName}:{heading.Line}";
            if (heading.Level == 1)
            {
                problems.Add($"{at}: a level-1 heading; the page's title is its only level-1 heading, so start sections at ##.");
            }
            else if (heading.Level > previous + 1)
            {
                problems.Add($"{at}: \"{heading.Text}\" jumps from level {previous} to level {heading.Level}; a screen reader user moving by level would miss it.");
            }

            previous = heading.Level;

            if (heading.Id.Length == 0)
            {
                problems.Add($"{at}: \"{heading.Text}\" has no {{#id}}; give every heading an explicit id that never changes (GUIDE-2).");
                continue;
            }

            if (!IdShape.IsMatch(heading.Id))
            {
                problems.Add($"{at}: the id \"{heading.Id}\" may use only letters, digits, - and _.");
            }

            if (!owners.TryAdd(heading.Id, at))
            {
                problems.Add($"{at}: the id \"{heading.Id}\" is already used by {owners[heading.Id]}; ids are unique across the whole guide, because the one-page guide joins every chapter.");
            }
        }

        foreach (int line in GuideMarkdown.SetextHeadings(page.Body))
        {
            problems.Add($"{page.FileName}:{line}: a line of = or - right under text turns that text into a heading with no id and no checked level; write it as ## Heading {{#id}} (GUIDE-2), or put a blank line above a --- rule.");
        }

        var rawIds = new HashSet<string>(StringComparer.Ordinal);
        foreach ((int number, string text, bool inFence) in GuideMarkdown.Lines(page.Body))
        {
            if (inFence) continue;
            if (GuideMarkdown.Liquid.IsMatch(text))
            {
                problems.Add($"{page.FileName}:{number}: Liquid ({{{{ or {{%) in the guide; link with page.md#id instead, which GitHub, the site and the one-page guide all follow (GUIDE-1).");
            }

            foreach (Match m in GuideMarkdown.RawId.Matches(text))
            {
                if (!rawIds.Add(m.Groups["id"].Value))
                {
                    problems.Add($"{page.FileName}:{number}: the id \"{m.Groups["id"].Value}\" appears twice on this page.");
                }
            }

            Match definition = GuideMarkdown.ReferenceDefinition.Match(text);
            if (definition.Success && !GuideMarkdown.IsExternal(definition.Groups["target"].Value))
            {
                problems.Add($"{page.FileName}:{number}: a reference-style link definition to {definition.Groups["target"].Value}; write links inline, [text](page.md#id).");
            }
        }
    }

    private static void CheckLinks(GuidePage page, IReadOnlyDictionary<string, IReadOnlySet<string>> idsByFile, List<string> problems)
    {
        IReadOnlySet<string> ownIds = idsByFile[page.FileName];
        foreach (GuideLink link in GuideMarkdown.Links(page.Body))
        {
            string at = $"{page.FileName}:{link.Line}";

            if (link.IsImage && link.Text.Trim().Length == 0)
            {
                problems.Add($"{at}: an image with no alt text; say what it shows in the [brackets].");
            }

            if (link.Target.Length == 0)
            {
                problems.Add($"{at}: a link with no target.");
                continue;
            }

            if (GuideMarkdown.IsExternal(link.Target)) continue;

            if (link.IsImage)
            {
                problems.Add($"{at}: an image at {link.Target}; the one-page guide shipped with the app is a single HTML file with no images beside it, so describe what it shows in words.");
                continue;
            }

            if (link.IsRawHtml)
            {
                problems.Add($"{at}: a raw HTML link to {link.Target}; write it as a Markdown link, [text](page.md#id), which the site converts (GUIDE-1).");
                continue;
            }

            (string file, string fragment) = GuideMarkdown.Split(link.Target);

            if (file.Length == 0)
            {
                if (!ownIds.Contains(fragment))
                {
                    problems.Add($"{at}: links to #{fragment}, which no heading on this page has.");
                }

                continue;
            }

            if (file == OnePageFile && fragment.Length == 0)
            {
                // The site serves a chapter at /guide/<chapter>/, so only the contents page (/guide/) reaches the
                // one-page file with a relative link.
                if (page.FileName != GuideSource.ContentsFile)
                {
                    problems.Add($"{at}: links to {OnePageFile}, which only the contents page can link to: from a chapter the site resolves it inside the chapter's own folder. Link to index.md instead.");
                }

                continue;
            }

            if (file.Contains('/') || !file.EndsWith(".md", StringComparison.Ordinal))
            {
                problems.Add($"{at}: links to {link.Target}; link to another guide page as page.md or page.md#id, never a site path (GUIDE-1).");
                continue;
            }

            if (!idsByFile.TryGetValue(file, out IReadOnlySet<string>? targetIds))
            {
                problems.Add($"{at}: links to {file}, which is not in the guide.");
                continue;
            }

            if (fragment.Length > 0 && !targetIds.Contains(fragment))
            {
                problems.Add($"{at}: links to {link.Target}, but {file} has no heading with the id \"{fragment}\".");
            }
        }
    }
}
