using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests.Walkthroughs;

internal enum MarkerKind { None, Template, Fragment }

/// <summary>
/// One C# block of a walkthrough: its opening fence's 1-based line, the marker on the last non-blank line above
/// the fence (its kind and argument: a region name, or <c>path#identifier</c>), the block's normalized text with
/// the fence's indentation removed, and that line above the fence, trimmed.
/// </summary>
internal sealed record DocBlock(int FenceLine, MarkerKind Kind, string Argument, string Text, string LineAboveFence);

/// <summary>Finds the C# blocks of a markdown file and the marker each carries.</summary>
internal static class DocBlocks
{
    private static readonly Regex OpeningFence = new(@"^(?<indent> *)(?<fence>`{3,}|~{3,})(?<info>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Marker = new(
        @"^\s*<!--\s*(?<kind>template|fragment):\s*(?<arg>\S+)\s*-->\s*$", RegexOptions.CultureInvariant);
    private static readonly string[] CSharpTags = { "csharp", "cs", "c#" };

    /// <summary>
    /// Every fenced block whose info string's first word is csharp, cs or c# (any case). Every fence, C# or not,
    /// is skipped to its close, a line of the same character at least as long; an unclosed fence runs to the end.
    /// </summary>
    public static IReadOnlyList<DocBlock> CSharpBlocks(string markdown)
    {
        string[] lines = DocText.Lines(markdown);
        var blocks = new List<DocBlock>();
        for (int i = 0; i < lines.Length; i++)
        {
            Match open = OpeningFence.Match(lines[i]);
            if (!open.Success) continue;
            string fence = open.Groups["fence"].Value;
            string info = open.Groups["info"].Value.Trim();
            if (fence[0] == '`' && info.Contains('`')) continue; // inline code, not a fence
            int close = i + 1;
            while (close < lines.Length && !Closes(lines[close], fence)) close++;
            if (IsCSharp(info))
            {
                int indent = open.Groups["indent"].Value.Length;
                IEnumerable<string> content = lines.Skip(i + 1).Take(close - i - 1).Select(l => WithoutIndent(l, indent));
                (MarkerKind kind, string argument, string above) = MarkerAbove(lines, i);
                blocks.Add(new DocBlock(i + 1, kind, argument, DocText.Normalize(content, dedent: false), above));
            }
            i = close;
        }
        return blocks;
    }

    /// <summary>Whether <paramref name="word"/> occurs with no letter, digit or underscore on either side (ordinal).</summary>
    public static bool ContainsWord(string text, string word)
    {
        if (word.Length == 0) return false;
        for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
            if (!IsWordChar(text, at - 1) && !IsWordChar(text, at + word.Length))
                return true;
        return false;
    }

    private static bool IsWordChar(string text, int index)
        => index >= 0 && index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_');

    private static bool Closes(string line, string fence)
    {
        string trimmed = line.Trim();
        return trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]);
    }

    private static bool IsCSharp(string info)
    {
        string language = info.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return CSharpTags.Any(tag => string.Equals(tag, language, StringComparison.OrdinalIgnoreCase));
    }

    private static string WithoutIndent(string line, int indent)
    {
        int n = 0;
        while (n < indent && n < line.Length && line[n] == ' ') n++;
        return line[n..];
    }

    private static (MarkerKind Kind, string Argument, string Above) MarkerAbove(string[] lines, int fenceIndex)
    {
        int j = fenceIndex - 1;
        while (j >= 0 && lines[j].Trim().Length == 0) j--;
        if (j < 0) return (MarkerKind.None, "", "");
        string above = lines[j].Trim();
        Match marker = Marker.Match(lines[j]);
        if (!marker.Success) return (MarkerKind.None, "", above);
        return (marker.Groups["kind"].Value == "template" ? MarkerKind.Template : MarkerKind.Fragment,
            marker.Groups["arg"].Value, above);
    }
}
