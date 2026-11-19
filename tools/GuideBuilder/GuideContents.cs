using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>One part of the guide as the contents page lists it: its heading, its id, any text under the heading and
/// its numbered chapter list, in order.</summary>
public sealed record GuidePart(string Title, string Id, string Intro, IReadOnlyList<GuideContentsChapter> Chapters)
{
    public IReadOnlyList<string> ChapterFiles => Chapters.Select(c => c.File).ToList();
}

/// <summary>A chapter as the contents page lists it: its file, the line of its numbered item (counted in the body), the
/// column its title starts at, and the section lines under it as written ("   - [Section](chapter.md#id)").</summary>
public sealed record GuideContentsChapter(string File, int Line, int Indent, IReadOnlyList<string> SectionLines);

/// <summary>
/// Reads the contents page (index.md): every level-2 heading is a part, and the numbered list under it,
/// "1. [Title](chapter.md)", names its chapters in reading order. Under each chapter, an indented list names that
/// chapter's level-2 sections; <see cref="WithSections"/> writes those lists and the checker holds them to the
/// chapters' headings. Other text under a part heading is its introduction, which the one-page guide keeps. Text above
/// the first part is the site's own preamble.
/// </summary>
public static class GuideContents
{
    private static readonly Regex ChapterItem = new(@"^\d+\.[ \t]+\[(?<title>[^\]]+)\]\((?<file>[^)#\s/]+\.md)\)[ \t]*$",
        RegexOptions.CultureInvariant);

    // Any indented list item after a chapter's numbered item belongs to that chapter's section list, so a malformed one
    // is still kept out of the part's introduction and reported by the checker.
    private static readonly Regex SectionItem = new(@"^[ \t]+[-*+][ \t]", RegexOptions.CultureInvariant);

    private static readonly Regex UnescapedBracket = new(@"(?<!\\)[\[\]]", RegexOptions.CultureInvariant);

    public static IReadOnlyList<GuidePart> Parse(string contentsBody)
    {
        var parts = new List<GuidePart>();
        string? title = null, id = null;
        var intro = new List<string>();
        var chapters = new List<GuideContentsChapter>();
        List<string>? sections = null;

        void Flush()
        {
            if (title is not null)
            {
                parts.Add(new GuidePart(title, id ?? "", string.Join("\n", intro).Trim('\n'), chapters.ToList()));
            }

            intro.Clear();
            chapters.Clear();
            sections = null;
        }

        foreach ((int number, string text, bool inFence) in GuideMarkdown.Lines(contentsBody))
        {
            Match heading = inFence ? Match.Empty : GuideMarkdown.Heading.Match(text);
            if (heading.Success && heading.Groups["hashes"].Length == 2)
            {
                Flush();
                title = heading.Groups["text"].Value;
                id = heading.Groups["id"].Value;
                continue;
            }

            if (title is null) continue;

            Match item = inFence ? Match.Empty : ChapterItem.Match(text);
            if (item.Success)
            {
                sections = new List<string>();
                chapters.Add(new GuideContentsChapter(item.Groups["file"].Value, number, IndentOf(item), sections));
            }
            else if (!inFence && sections is not null && SectionItem.IsMatch(text))
            {
                sections.Add(text);
            }
            else
            {
                intro.Add(text);
            }
        }

        Flush();
        return parts;
    }

    /// <summary>The section lines a chapter's contents entry carries: one per level-2 heading, in order, indented to
    /// the column of the chapter's title so Markdown nests them under it.</summary>
    public static IReadOnlyList<string> SectionLines(GuidePage chapter, int indent)
        => GuideMarkdown.Headings(chapter.Body)
            .Where(h => h.Level == 2)
            .Select(h => $"{new string(' ', indent)}- [{UnescapedBracket.Replace(h.Text, @"\$0")}]({chapter.FileName}#{h.Id})")
            .ToList();

    /// <summary>
    /// The contents page (the whole file, front matter included) with every chapter's section list written afresh
    /// from that chapter's headings: what GuideBuilder's contents command saves. Everything else is kept as it is;
    /// line endings come out as "\n".
    /// </summary>
    public static string WithSections(string contents, GuideSource source)
    {
        var output = new List<string>();
        bool inPart = false, afterChapter = false;

        foreach ((_, string text, bool inFence) in GuideMarkdown.Lines(contents.Replace("\r\n", "\n").Replace('\r', '\n')))
        {
            if (!inFence)
            {
                Match heading = GuideMarkdown.Heading.Match(text);
                if (heading.Success && heading.Groups["hashes"].Length == 2)
                {
                    inPart = true;
                    afterChapter = false;
                }
                else if (inPart)
                {
                    Match item = ChapterItem.Match(text);
                    if (item.Success)
                    {
                        output.Add(text);
                        if (source.Pages.TryGetValue(item.Groups["file"].Value, out GuidePage? page))
                        {
                            output.AddRange(SectionLines(page, IndentOf(item)));
                        }

                        afterChapter = true;
                        continue;
                    }

                    if (afterChapter && SectionItem.IsMatch(text)) continue;
                }
            }

            output.Add(text);
        }

        return string.Join("\n", output);
    }

    // "1. [Title]" puts the title at column 3, "10. [Title]" at 4.
    private static int IndentOf(Match chapterItem) => chapterItem.Groups["title"].Index - 1;
}
