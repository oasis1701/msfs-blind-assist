using System.Globalization;
using System.Text.RegularExpressions;

namespace ChangelogBuilder;

/// <summary>Which release-notes heading an entry appears under.</summary>
public enum ChangelogCategory
{
    Aircraft,
    Feature,
    Improvement,
    Fix,

    /// <summary>Validated like any other, but deliberately never rendered.</summary>
    Internal,
}

/// <summary>
/// One user-facing change, read from changelog.d/&lt;pr&gt;-&lt;slug&gt;.&lt;category&gt;.md.
/// </summary>
public sealed record ChangelogFragment(int PrNumber, string Slug, ChangelogCategory Category, string Body)
{
    // This is a SHAPE check only — is there a numeric prefix at all — never a value check.
    // Parse has no git or filesystem access and cannot know which PR is actually running,
    // so it can't confirm the number is THIS PR's own; that's .github/workflows/changelog.yml's
    // job, the only place github.event.pull_request.number is available. Keep that boundary:
    // don't grow this into anything that needs to know "the real" PR number.
    private static readonly Regex NamePattern = new(
        @"^(?<pr>[1-9][0-9]*)-(?<slug>[a-z0-9][a-z0-9-]*)\.(?<cat>aircraft|feature|improvement|fix|internal)\.md$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Validates one fragment. Pure: <paramref name="fileName"/> may be a bare name or a
    /// path (only the final segment is inspected) and <paramref name="content"/> is the
    /// file's text — nothing is read from disk.
    /// </summary>
    public static ParseResult Parse(string fileName, string content)
    {
        var name = Path.GetFileName(fileName);

        var match = NamePattern.Match(name);
        if (!match.Success)
        {
            return new ParseResult(null,
                $"{fileName}: name must be <pr>-<slug>.<category>.md, where pr is the pull " +
                "request number (no leading zero), slug is lower-case letters, digits and " +
                "dashes (starting with a letter or digit) and category is one of aircraft, " +
                "feature, improvement, fix, internal.");
        }

        var body = content.Trim();
        if (body.Length == 0)
        {
            return new ParseResult(null, $"{fileName}: the fragment is empty.");
        }

        var category = match.Groups["cat"].Value switch
        {
            "aircraft" => ChangelogCategory.Aircraft,
            "feature" => ChangelogCategory.Feature,
            "improvement" => ChangelogCategory.Improvement,
            "fix" => ChangelogCategory.Fix,
            "internal" => ChangelogCategory.Internal,
            _ => throw new InvalidOperationException(
                $"Category '{match.Groups["cat"].Value}' matched the filename pattern but has no " +
                "ChangelogCategory mapping. Add the arm — an unmapped category would be silently dropped."),
        };

        var prNumber = int.Parse(match.Groups["pr"].Value, CultureInfo.InvariantCulture);

        return new ParseResult(
            new ChangelogFragment(prNumber, match.Groups["slug"].Value, category, body), null);
    }
}

/// <summary>Outcome of <see cref="ChangelogFragment.Parse"/>: exactly one side is set.</summary>
public sealed record ParseResult(ChangelogFragment? Fragment, string? Error)
{
    public bool Ok => Error is null;
}
