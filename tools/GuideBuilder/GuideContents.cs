using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>One part of the guide as the contents page lists it: its heading, its id, any text under the heading and
/// its numbered chapter list, in order.</summary>
public sealed record GuidePart(string Title, string Id, string Intro, IReadOnlyList<string> ChapterFiles);

/// <summary>
/// Reads the contents page (index.md): every level-2 heading is a part, and the numbered list under it,
/// "1. [Title](chapter.md)", names its chapters in reading order. Other text under a part heading is its
/// introduction, which the one-page guide keeps. Text above the first part is the site's own preamble.
/// </summary>
public static class GuideContents
{
    private static readonly Regex ChapterItem = new(@"^\d+\.[ \t]+\[(?<title>[^\]]+)\]\((?<file>[^)#\s/]+\.md)\)[ \t]*$",
        RegexOptions.CultureInvariant);

    public static IReadOnlyList<GuidePart> Parse(string contentsBody)
    {
        var parts = new List<GuidePart>();
        string? title = null, id = null;
        var intro = new List<string>();
        var chapters = new List<string>();

        void Flush()
        {
            if (title is not null)
            {
                parts.Add(new GuidePart(title, id ?? "", string.Join("\n", intro).Trim('\n'), chapters.ToList()));
            }

            intro.Clear();
            chapters.Clear();
        }

        foreach ((_, string text, bool inFence) in GuideMarkdown.Lines(contentsBody))
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
                chapters.Add(item.Groups["file"].Value);
            }
            else
            {
                intro.Add(text);
            }
        }

        Flush();
        return parts;
    }
}
