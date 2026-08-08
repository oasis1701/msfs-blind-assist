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

/// <summary>One user-facing change, read from changelog.d/&lt;slug&gt;.&lt;category&gt;.md.</summary>
public sealed record ChangelogFragment(string Slug, ChangelogCategory Category, string Body)
{
    private static readonly Regex NamePattern = new(
        @"^(?<slug>[a-z0-9][a-z0-9-]*)\.(?<cat>aircraft|feature|improvement|fix|internal)\.md$",
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
                $"{fileName}: name must be <slug>.<category>.md, where slug is lower-case " +
                "letters, digits and dashes (starting with a letter or digit) and category " +
                "is one of aircraft, feature, improvement, fix, internal.");
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
            _ => ChangelogCategory.Internal,
        };

        return new ParseResult(new ChangelogFragment(match.Groups["slug"].Value, category, body), null);
    }
}

/// <summary>Outcome of <see cref="ChangelogFragment.Parse"/>: exactly one side is set.</summary>
public sealed record ParseResult(ChangelogFragment? Fragment, string? Error)
{
    public bool Ok => Error is null;
}
