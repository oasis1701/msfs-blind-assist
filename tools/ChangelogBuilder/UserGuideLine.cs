using System.Text.RegularExpressions;

namespace ChangelogBuilder;

/// <summary>Which of the accepted answers a PR description's "User guide:" line gives (CORE-19).</summary>
public enum UserGuideAnswer
{
    /// <summary>No "User guide:" line, or only empty ones (the template's own line left blank).</summary>
    Missing,

    /// <summary>Links the msfs-blind-assist-site pull request that updates the guide.</summary>
    SitePullRequest,

    /// <summary>"needed: &lt;chapter&gt;": the guide still has to be updated, and where.</summary>
    Needed,

    /// <summary>"none, because &lt;reason&gt;": the guide is still right.</summary>
    None,

    /// <summary>A line is there but is none of the forms above; <see cref="UserGuideLineResult.Error"/> says why.</summary>
    Invalid,
}

/// <summary>
/// Reads the "User guide:" line from a pull request's description. Pure: the description is passed in as a
/// string, nothing is read from GitHub or disk, so every case is expressible as a literal in the tests.
/// </summary>
public static class UserGuideLine
{
    public const string SiteRepository = "oasis1701/msfs-blind-assist-site";

    // HTML comments first: the PR template's own examples of the line sit inside one and must never count as
    // the answer. An unclosed comment hides the rest of the description, as it does when GitHub renders it.
    private static readonly Regex HtmlComment = new(@"<!--.*?(?:-->|\z)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // A list marker and bold are tolerated so "- **User guide:** none, because …" still reads. The colon is
    // required, which keeps the template's "## User guide" heading from matching.
    private static readonly Regex Line = new(@"^[ \t]*(?:[-*+][ \t]+)?(?:\*\*|__)?user guide:(?:\*\*|__)?[ \t]*(?<value>.*?)[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // A template placeholder such as <reason> or <n>; an autolink such as <https://…> is not one.
    private static readonly Regex Placeholder = new(@"<(?!https?:)[a-z][^<>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NoneForm = new(@"^none\b[\s,:;.\-–—]*(?:because\b[\s,:]*)?(?<reason>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NeededForm = new(@"^needed\b[\s,:;.\-–—]*(?<where>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The full URL or GitHub's short form, oasis1701/msfs-blind-assist-site#12. GitHub treats owner and
    // repository names case-insensitively, so this does too.
    private static readonly Regex SitePullRequestReference = new(
        @"(?:https?://github\.com/" + Regex.Escape(SiteRepository) + @"/pull/|\b" + Regex.Escape(SiteRepository) + "#)(?<number>[1-9][0-9]*)",
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

        if (SitePullRequestReference.IsMatch(value))
        {
            return new UserGuideLineResult(UserGuideAnswer.SitePullRequest, value, null);
        }

        return Invalid(value, $"it is none of the three forms: a link to the {SiteRepository} pull request that updates " +
            "the guide, \"needed: <chapter and section>\", or \"none, because <reason>\".");
    }

    private static bool HasWords(string text) => text.Any(char.IsLetterOrDigit);

    private static UserGuideLineResult Invalid(string value, string why)
        => new(UserGuideAnswer.Invalid, value, $"The User guide line reads \"{value}\", but {why}");
}

/// <summary>Outcome of <see cref="UserGuideLine.Parse"/>. <see cref="Error"/> is set only for <see cref="UserGuideAnswer.Invalid"/>.</summary>
public sealed record UserGuideLineResult(UserGuideAnswer Answer, string Value, string? Error)
{
    /// <summary>True for the three accepted answers.</summary>
    public bool Answered => Answer is UserGuideAnswer.SitePullRequest or UserGuideAnswer.Needed or UserGuideAnswer.None;
}
