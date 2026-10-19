using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>Which answer a PR description's "User guide:" line gives (CORE-19).</summary>
public enum UserGuideAnswer
{
    /// <summary>No "User guide:" line, or only empty ones (the template's own line left blank).</summary>
    Missing,

    /// <summary>"needed: &lt;chapter&gt;": the guide still has to be updated, and where.</summary>
    Needed,

    /// <summary>"none, because &lt;reason&gt;": the guide is still right.</summary>
    None,

    /// <summary>A line is there but is neither form; <see cref="UserGuideLineResult.Error"/> says why.</summary>
    Invalid,
}

/// <summary>
/// Reads the "User guide:" line from a pull request's description. Pure: the description is passed in as a
/// string, nothing is read from GitHub or disk, so every case is expressible as a literal in the tests.
/// </summary>
public static class UserGuideLine
{
    // HTML comments first: the PR template's own examples of the line sit inside one and must never count as
    // the answer. An unclosed comment hides the rest of the description, as it does when GitHub renders it.
    private static readonly Regex HtmlComment = new(@"<!--.*?(?:-->|\z)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // A list marker and bold are tolerated, with the bold ending before or after the colon, so "- **User guide:**
    // none, because …" and "**User guide**: none, because …" both read. The colon is required, which keeps the
    // template's "## User guide" heading from matching.
    private static readonly Regex Line = new(@"^[ \t]*(?:[-*+][ \t]+)?(?:\*\*|__)?user guide(?:\*\*|__)?:(?:\*\*|__)?[ \t]*(?<value>.*?)[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // The placeholders the PR template and CORE-19 use, and only those: a reason may name an HTML element (<nav>).
    private static readonly Regex Placeholder = new(@"<(?:chapter(?: and section)?|reason|why the guide is still right|where)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NoneForm = new(@"^none\b[\s,:;.\-–—]*(?:because\b[\s,:]*)?(?<reason>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NeededForm = new(@"^needed\b[\s,:;.\-–—]*(?<where>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static UserGuideLineResult Parse(string? description)
    {
        string visible = HtmlComment.Replace(description ?? "", "").Replace("\r\n", "\n").Replace('\r', '\n');

        // The first line that says something is the answer: an empty line above it (the template's blank one,
        // when the answer was typed on a line of its own) is not.
        string? value = Line.Matches(visible)
            .Select(m => m.Groups["value"].Value)
            .FirstOrDefault(v => v.Length > 0);

        if (value is null)
        {
            return new UserGuideLineResult(UserGuideAnswer.Missing, "", null);
        }

        if (Placeholder.Match(value) is { Success: true } placeholder)
        {
            return Invalid(value, $"it still holds the template's placeholder {placeholder.Value}; replace it with the real text.");
        }

        if (NoneForm.Match(value) is { Success: true } none)
        {
            return HasWords(none.Groups["reason"].Value)
                ? new UserGuideLineResult(UserGuideAnswer.None, value, null)
                : Invalid(value, "it says none but gives no reason; write \"none, because <why the guide is still right>\".");
        }

        if (NeededForm.Match(value) is { Success: true } needed)
        {
            return HasWords(needed.Groups["where"].Value)
                ? new UserGuideLineResult(UserGuideAnswer.Needed, value, null)
                : Invalid(value, "it says needed but not where; write \"needed: <chapter and section>\".");
        }

        return Invalid(value, "it is neither form: \"needed: <chapter and section>\" or \"none, because <reason>\". " +
            "A PR that updates the guide changes files in guide/ and needs no line.");
    }

    private static bool HasWords(string text) => text.Any(char.IsLetterOrDigit);

    private static UserGuideLineResult Invalid(string value, string why)
        => new(UserGuideAnswer.Invalid, value, $"The User guide line reads \"{value}\", but {why}");
}

/// <summary>Outcome of <see cref="UserGuideLine.Parse"/>. <see cref="Error"/> is set only for <see cref="UserGuideAnswer.Invalid"/>.</summary>
public sealed record UserGuideLineResult(UserGuideAnswer Answer, string Value, string? Error)
{
    /// <summary>True for the two accepted answers.</summary>
    public bool Answered => Answer is UserGuideAnswer.Needed or UserGuideAnswer.None;
}
