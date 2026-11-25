using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>
/// Reads the "Variants:", "Planned in:" and "Sim-tested on:" lines from a pull request's description (CORE-22,
/// CORE-23, CORE-5). Pure, like <see cref="UserGuideLine"/>: the description is a string.
/// </summary>
public static class ScopeLines
{
    public const string VariantsLabel = "Variants";
    public const string PlannedInLabel = "Planned in";
    public const string SimTestedOnLabel = "Sim-tested on";

    // The PR template's own examples sit inside an HTML comment and must never count as an answer.
    private static readonly Regex HtmlComment = new(@"<!--.*?(?:-->|\z)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // A list marker and bold are tolerated, with the bold ending before or after the colon. The colon is required.
    private static readonly Regex Line = new(
        @"^[ \t]*(?:[-*+][ \t]+)?(?:\*\*|__)?(?<label>variants|planned in|sim[- ]tested on)(?:\*\*|__)?:(?:\*\*|__)?[ \t]*(?<value>.*?)[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex Bracketed = new(@"^<(?<inner>[^>]*)>$", RegexOptions.CultureInvariant);

    public static ScopeLinesResult Parse(string? description)
    {
        string visible = HtmlComment.Replace(description ?? "", "").Replace("\r\n", "\n").Replace('\r', '\n');

        string? variants = null;
        string? plannedIn = null;
        var simTested = new List<string>();

        foreach (Match match in Line.Matches(visible))
        {
            string? value = Clean(match.Groups["value"].Value);
            if (value is null) continue;

            string label = match.Groups["label"].Value.ToLowerInvariant();
            if (label == "variants") variants ??= value;
            else if (label == "planned in") plannedIn ??= value;
            else simTested.Add(value);
        }

        return new ScopeLinesResult(variants, plannedIn, simTested);
    }

    // An empty value is missing; so is a lone <placeholder>, unless it holds "://", when it is a Markdown autolink.
    private static string? Clean(string value)
    {
        if (value.Length == 0) return null;
        if (Bracketed.Match(value) is { Success: true } bracketed)
        {
            string inner = bracketed.Groups["inner"].Value.Trim();
            return inner.Contains("://", StringComparison.Ordinal) ? inner : null;
        }

        return value;
    }
}

/// <summary>Outcome of <see cref="ScopeLines.Parse"/>. A missing or empty line is null (or an empty list).</summary>
public sealed record ScopeLinesResult(string? Variants, string? PlannedIn, IReadOnlyList<string> SimTestedOn);
