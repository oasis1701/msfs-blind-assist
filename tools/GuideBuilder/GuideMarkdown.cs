using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>A Markdown heading: its level (2 for "##"), text and explicit {#id}, empty when it has none.</summary>
public sealed record GuideHeading(int Line, int Level, string Text, string Id);

/// <summary>A link in the guide's Markdown or raw HTML, with the line it is on (1-based, counted in the body).</summary>
public sealed record GuideLink(int Line, string Target, bool IsRawHtml, bool IsImage, string Text);

/// <summary>
/// The few pieces of the guide's Markdown dialect the checker and the one-page build need to see, read line by line
/// with fenced code skipped. It is deliberately not a Markdown parser: the guide's conventions (GUIDE-1, GUIDE-2) keep
/// these patterns unambiguous.
/// </summary>
public static class GuideMarkdown
{
    // An ATX heading, which Markdown allows up to three spaces in.
    public static readonly Regex Heading = new(@"^[ ]{0,3}(?<hashes>#{1,6})[ \t]+(?<text>.*?)(?:[ \t]+\{#(?<id>[^}\s]+)\})?[ \t]*$",
        RegexOptions.CultureInvariant);

    // [text](target) or ![alt](target), with an optional "title". Link text may hold escaped characters and one level
    // of brackets, and may wrap onto the next line but never across a blank one; an escaped \[ is literal text.
    public static readonly Regex InlineLink = new(
        @"(?<bang>!?)(?<!\\)\[(?<text>(?:\\.|[^\[\]\\\n]|\n(?![ \t]*(?:\n|$))|\[[^\]\n]*\])*)\]\((?<target>[^)\s]*)(?:[ \t]+""[^""]*"")?\)",
        RegexOptions.CultureInvariant);

    // A line of = or - under a paragraph line makes that line a setext heading, which carries no {#id}.
    private static readonly Regex SetextUnderline = new(@"^[ ]{0,3}(?:=+|-+)[ \t]*$", RegexOptions.CultureInvariant);

    // A line a setext underline cannot turn into a heading: blank, a list item, a heading, a quote, a table row,
    // HTML or a fence. A --- after one of these is a rule or a list item.
    private static readonly Regex NotParagraphText = new(@"^\s*$|^[ ]{0,3}(?:[-*+][ \t]|\d+[.)][ \t]|[#>|<]|`{3}|~{3})",
        RegexOptions.CultureInvariant);

    public static readonly Regex ReferenceDefinition = new(@"^[ ]{0,3}\[(?<label>[^\]]+)\]:[ \t]*(?<target>\S+)",
        RegexOptions.CultureInvariant);

    public static readonly Regex RawHref = new(@"<a\s[^>]*?href=""(?<href>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly Regex RawId = new(@"<[a-zA-Z][^>]*?\sid=""(?<id>[^""]+)""", RegexOptions.CultureInvariant);

    public static readonly Regex Liquid = new(@"\{\{|\{%", RegexOptions.CultureInvariant);

    private static readonly Regex Fence = new(@"^[ ]{0,3}(?<fence>`{3,}|~{3,})", RegexOptions.CultureInvariant);

    /// <summary>The body's lines with their 1-based numbers, and whether each sits inside a fenced code block.</summary>
    public static IEnumerable<(int Number, string Text, bool InFence)> Lines(string body)
    {
        string[] lines = body.Split('\n');
        string? openFence = null;
        for (int i = 0; i < lines.Length; i++)
        {
            Match fence = Fence.Match(lines[i]);
            if (fence.Success)
            {
                string marker = fence.Groups["fence"].Value;
                if (openFence is null)
                {
                    openFence = marker;
                    yield return (i + 1, lines[i], true);
                    continue;
                }

                if (marker[0] == openFence[0] && marker.Length >= openFence.Length)
                {
                    openFence = null;
                    yield return (i + 1, lines[i], true);
                    continue;
                }
            }

            yield return (i + 1, lines[i], openFence is not null);
        }
    }

    public static IReadOnlyList<GuideHeading> Headings(string body)
    {
        var headings = new List<GuideHeading>();
        foreach ((int number, string text, bool inFence) in Lines(body))
        {
            if (inFence) continue;
            Match m = Heading.Match(text);
            if (m.Success)
            {
                headings.Add(new GuideHeading(number, m.Groups["hashes"].Length, m.Groups["text"].Value, m.Groups["id"].Value));
            }
        }

        return headings;
    }

    /// <summary>The 1-based line numbers of setext headings: text underlined with a line of = or -.</summary>
    public static IReadOnlyList<int> SetextHeadings(string body)
    {
        var found = new List<int>();
        (string Text, bool InFence)? previous = null;
        foreach ((int number, string text, bool inFence) in Lines(body))
        {
            if (!inFence && SetextUnderline.IsMatch(text) && previous is { InFence: false } p && !NotParagraphText.IsMatch(p.Text))
            {
                found.Add(number);
            }

            previous = (text, inFence);
        }

        return found;
    }

    public static IReadOnlyList<GuideLink> Links(string body)
    {
        var links = new List<GuideLink>();

        // Inline links are matched over the whole body, with fenced code blanked out and the line count kept, so a
        // link whose text wraps onto the next line is found too.
        string prose = OutsideFences(body);
        foreach (Match m in InlineLink.Matches(prose))
        {
            int line = 1 + prose.AsSpan(0, m.Index).Count('\n');
            links.Add(new GuideLink(line, m.Groups["target"].Value, false, m.Groups["bang"].Length > 0, m.Groups["text"].Value));
        }

        foreach ((int number, string text, bool inFence) in Lines(body))
        {
            if (inFence) continue;
            foreach (Match m in RawHref.Matches(text))
            {
                links.Add(new GuideLink(number, m.Groups["href"].Value, true, false, ""));
            }
        }

        return links;
    }

    /// <summary>The body with every fenced-code line emptied, so it keeps its line numbers.</summary>
    public static string OutsideFences(string body) => string.Join("\n", Lines(body).Select(l => l.InFence ? "" : l.Text));

    /// <summary>Every id a page defines: its headings' {#id}s and any raw HTML id="…" attributes.</summary>
    public static IReadOnlySet<string> Ids(string body)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (GuideHeading h in Headings(body))
        {
            if (h.Id.Length > 0) ids.Add(h.Id);
        }

        foreach ((_, string text, bool inFence) in Lines(body))
        {
            if (inFence) continue;
            foreach (Match m in RawId.Matches(text)) ids.Add(m.Groups["id"].Value);
        }

        return ids;
    }

    /// <summary>True for a target with a scheme (https:, mailto:): it leaves the guide and is not checked.</summary>
    public static bool IsExternal(string target) => Regex.IsMatch(target, "^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant);

    /// <summary>Splits "taxi-guidance.md#holds" into ("taxi-guidance.md", "holds"); "#holds" gives ("", "holds").</summary>
    public static (string File, string Fragment) Split(string target)
    {
        int hash = target.IndexOf('#');
        return hash < 0 ? (target, "") : (target[..hash], target[(hash + 1)..]);
    }
}
