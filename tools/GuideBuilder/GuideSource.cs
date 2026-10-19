using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>One Markdown file of the guide: its file name ("core-concepts.md"), its front-matter title and its body.</summary>
public sealed record GuidePage(string FileName, string Title, string Body)
{
    /// <summary>The file name without ".md": the page's address on the site and its id in the one-page guide.</summary>
    public string Slug => Path.GetFileNameWithoutExtension(FileName);
}

/// <summary>
/// The guide/ folder, read into memory. Pure apart from <see cref="FromFolder"/>: the tests build one from literals
/// with <see cref="FromFiles"/>. Line endings are normalized to "\n" on the way in.
/// </summary>
public sealed class GuideSource
{
    /// <summary>The contents page: its part headings and numbered chapter lists set the order of the one-page guide.</summary>
    public const string ContentsFile = "index.md";

    private static readonly Regex TitleLine = new(@"^title:[ \t]*(?:""(?<t>(?:[^""\\]|\\.)*)""|'(?<t>[^']*)'|(?<t>.*?))[ \t]*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private GuideSource(IReadOnlyDictionary<string, GuidePage> pages, IReadOnlyList<string> problems)
    {
        Pages = pages;
        Problems = problems;
    }

    /// <summary>Every page, keyed by file name.</summary>
    public IReadOnlyDictionary<string, GuidePage> Pages { get; }

    /// <summary>What went wrong reading the files (a missing front matter or title).</summary>
    public IReadOnlyList<string> Problems { get; }

    public static GuideSource FromFolder(string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(folder, "*.md", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
        {
            files[Path.GetFileName(path)] = File.ReadAllText(path);
        }

        return FromFiles(files);
    }

    public static GuideSource FromFiles(IReadOnlyDictionary<string, string> files)
    {
        var pages = new Dictionary<string, GuidePage>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach ((string fileName, string raw) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            string text = raw.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
            string title = "";
            string body = text;

            if (text.StartsWith("---\n", StringComparison.Ordinal))
            {
                int end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
                if (end < 0)
                {
                    problems.Add($"{fileName}: the front matter has no closing ---.");
                }
                else
                {
                    string frontMatter = text[4..(end + 1)];
                    body = text[(end + 5)..];
                    Match m = TitleLine.Match(frontMatter);
                    title = m.Success ? m.Groups["t"].Value.Replace("\\\"", "\"").Trim() : "";
                }
            }
            else
            {
                problems.Add($"{fileName}: starts with no front matter; begin the file with ---, a title: line and ---.");
            }

            if (title.Length == 0 && body != text)
            {
                problems.Add($"{fileName}: the front matter has no title; the site and the one-page guide use it as the chapter heading.");
            }

            pages[fileName] = new GuidePage(fileName, title, body);
        }

        return new GuideSource(pages, problems);
    }
}
