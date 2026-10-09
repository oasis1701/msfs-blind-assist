using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests.Walkthroughs;

/// <summary>
/// Finds, checks and renders the doc-regions of the compiled walkthrough template
/// (tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs). A region is the lines between
/// <c>// doc-region: name</c> and <c>// doc-region-end: name</c>; regions nest, and one whose start
/// marker ends in <c>(collapsed)</c> shows as a single <c>// ...</c> line inside a larger region.
/// </summary>
internal static class DocRegions
{
    private static readonly Regex Start = new(
        @"^\s*// doc-region: (?<name>[a-z0-9]+(?:-[a-z0-9]+)*)(?<collapsed> \(collapsed\))?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex End = new(
        @"^\s*// doc-region-end: (?<name>[a-z0-9]+(?:-[a-z0-9]+)*)\s*$", RegexOptions.CultureInvariant);
    // Anything that starts like a marker but is not one would otherwise show in a doc as text or end no region.
    private static readonly Regex NearMiss = new(@"^\s*//\s*doc-region", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static RegionMap Parse(string text)
    {
        string[] lines = DocText.Lines(text);
        var regions = new Dictionary<string, DocRegion>(StringComparer.Ordinal);
        var declaredOn = new Dictionary<string, int>(StringComparer.Ordinal);
        var problems = new List<string>();
        var open = new Stack<(string Name, int Start, bool Collapsed, bool Kept)>();
        for (int i = 0; i < lines.Length; i++)
        {
            Match start = Start.Match(lines[i]);
            if (start.Success)
            {
                string name = start.Groups["name"].Value;
                bool duplicate = declaredOn.TryGetValue(name, out int first);
                if (duplicate)
                    problems.Add($"line {i + 1}: doc-region '{name}' is already declared on line {first + 1}; region names are unique.");
                else
                    declaredOn[name] = i;
                open.Push((name, i, start.Groups["collapsed"].Success, !duplicate));
                continue;
            }
            Match end = End.Match(lines[i]);
            if (end.Success)
            {
                string name = end.Groups["name"].Value;
                if (open.Count == 0)
                    problems.Add($"line {i + 1}: doc-region-end '{name}' closes no open region.");
                else if (open.Peek().Name != name)
                    problems.Add($"line {i + 1}: doc-region-end '{name}' does not close the innermost open region, "
                        + $"'{open.Peek().Name}' (opened on line {open.Peek().Start + 1}); regions nest.");
                else
                {
                    var closed = open.Pop();
                    if (closed.Kept)
                        regions[name] = new DocRegion(name, closed.Start, i, closed.Collapsed);
                }
                continue;
            }
            if (NearMiss.IsMatch(lines[i]))
                problems.Add($"line {i + 1}: '{lines[i].Trim()}' looks like a region marker but is not one. Write "
                    + "'// doc-region: <name>', '// doc-region: <name> (collapsed)' or '// doc-region-end: <name>', "
                    + "with a lowercase kebab-case name.");
        }
        foreach (var unclosed in open)
            problems.Add($"line {unclosed.Start + 1}: doc-region '{unclosed.Name}' is never closed.");
        return new RegionMap(lines, regions, problems);
    }
}

/// <summary>A region's name, the 0-based lines of its two markers, and whether it collapses inside another.</summary>
internal sealed record DocRegion(string Name, int StartIndex, int EndIndex, bool Collapsed);

/// <summary>The regions <see cref="DocRegions.Parse"/> found, the problems it reported, and each region's rendering.</summary>
internal sealed class RegionMap
{
    private readonly string[] _lines;
    private readonly Dictionary<string, DocRegion> _regions;
    private readonly Dictionary<int, DocRegion> _byStart;
    private readonly HashSet<int> _endIndexes;

    internal RegionMap(string[] lines, Dictionary<string, DocRegion> regions, List<string> problems)
    {
        _lines = lines;
        _regions = regions;
        _byStart = regions.Values.ToDictionary(r => r.StartIndex);
        _endIndexes = regions.Values.Select(r => r.EndIndex).ToHashSet();
        Problems = problems;
    }

    /// <summary>Each problem starts with "line N: " (1-based).</summary>
    public IReadOnlyList<string> Problems { get; }

    public IReadOnlyCollection<string> Names => _regions.Keys;

    /// <summary>
    /// The region's lines as a doc block shows them: a nested collapsed region becomes one <c>// ...</c> line at
    /// its start marker's indentation, any other nested region shows in full without its markers, and the result is
    /// normalized and dedented (<see cref="DocText.Normalize"/>). Throws <see cref="KeyNotFoundException"/> for a
    /// name the template does not declare.
    /// </summary>
    public string Render(string name)
    {
        DocRegion region = _regions[name];
        var shown = new List<string>();
        for (int i = region.StartIndex + 1; i < region.EndIndex; i++)
        {
            if (_byStart.TryGetValue(i, out DocRegion? nested))
            {
                if (nested.Collapsed)
                {
                    string line = _lines[i];
                    shown.Add(line[..(line.Length - line.TrimStart().Length)] + "// ...");
                    i = nested.EndIndex;
                }
                continue;
            }
            if (!_endIndexes.Contains(i))
                shown.Add(_lines[i]);
        }
        return DocText.Normalize(shown, dedent: true);
    }
}

/// <summary>Line handling shared by <see cref="DocRegions"/> and <see cref="DocBlocks"/>.</summary>
internal static class DocText
{
    /// <summary>The text's lines, with a byte-order mark dropped and CRLF read as LF.</summary>
    public static string[] Lines(string text) => text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n');

    /// <summary>
    /// Trailing whitespace trimmed from each line, leading and trailing blank lines dropped, and, when
    /// <paramref name="dedent"/> is set, the smallest indentation of the non-blank lines removed; joined with LF.
    /// </summary>
    public static string Normalize(IEnumerable<string> lines, bool dedent)
    {
        List<string> trimmed = lines.Select(l => l.TrimEnd()).ToList();
        int first = trimmed.FindIndex(l => l.Length > 0);
        if (first < 0) return "";
        int last = trimmed.FindLastIndex(l => l.Length > 0);
        trimmed = trimmed.GetRange(first, last - first + 1);
        if (dedent)
        {
            int indent = trimmed.Where(l => l.Length > 0).Min(l => l.Length - l.TrimStart(' ').Length);
            trimmed = trimmed.Select(l => l.Length == 0 ? l : l[indent..]).ToList();
        }
        return string.Join("\n", trimmed);
    }
}
