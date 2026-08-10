using System.Text;

namespace ChangelogBuilder;

/// <summary>
/// Renders parsed fragments into the release body. The output is prepended to GitHub's
/// generated notes by softprops/action-gh-release, so it carries no title of its own.
/// </summary>
public static class ChangelogRenderer
{
    /// <summary>
    /// Editorial order, not alphabetical: what a pilot most wants to know comes first.
    /// ChangelogCategory.Internal is deliberately absent — that is what excludes it.
    /// </summary>
    private static readonly (ChangelogCategory Category, string Heading)[] Sections =
    [
        (ChangelogCategory.Aircraft, "New aircraft"),
        (ChangelogCategory.Feature, "New features"),
        (ChangelogCategory.Improvement, "Improvements"),
        (ChangelogCategory.Fix, "Fixes"),
    ];

    public static string Render(IEnumerable<ChangelogFragment> fragments) =>
        Render(fragments, new Dictionary<int, IReadOnlyList<string>>());

    public static string Render(
        IEnumerable<ChangelogFragment> fragments,
        IReadOnlyDictionary<int, IReadOnlyList<string>> contributors)
    {
        var all = fragments.ToList();
        var builder = new StringBuilder();

        foreach (var (category, heading) in Sections)
        {
            // Numeric, not lexicographic: as strings "1000-x" sorts before "182-x" (their
            // first characters tie at '1', then '0' < '8'), which would shuffle release
            // notes out of PR order. Slug is only a tiebreak, for the PR that adds more
            // than one fragment.
            var entries = all
                .Where(f => f.Category == category)
                .OrderBy(f => f.PrNumber)
                .ThenBy(f => f.Slug, StringComparer.Ordinal)
                .ToList();

            if (entries.Count == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append("## ").Append(heading).Append("\n\n");

            foreach (var entry in entries)
            {
                AppendBullet(builder, WithAttribution(entry, contributors));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends " — @a", " — @a and @b" or " — @a, @b and @c" to the bullet body. The map
    /// is keyed by the fragment's PR number; a PR the generator could not resolve is
    /// simply absent, and the entry renders unattributed.
    /// </summary>
    private static string WithAttribution(
        ChangelogFragment entry,
        IReadOnlyDictionary<int, IReadOnlyList<string>> contributors)
    {
        if (!contributors.TryGetValue(entry.PrNumber, out var logins) || logins.Count == 0)
        {
            return entry.Body;
        }

        var handles = logins.Select(l => "@" + l).ToList();
        var joined = handles.Count == 1
            ? handles[0]
            : string.Join(", ", handles.Take(handles.Count - 1)) + " and " + handles[^1];

        return entry.Body.TrimEnd() + " — " + joined;
    }

    private static void AppendBullet(StringBuilder builder, string body)
    {
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        builder.Append("- ").Append(lines[0]).Append('\n');

        for (var i = 1; i < lines.Length; i++)
        {
            // Blank lines stay blank; indenting them would emit trailing whitespace.
            if (lines[i].Length == 0)
            {
                builder.Append('\n');
            }
            else
            {
                builder.Append("  ").Append(lines[i]).Append('\n');
            }
        }
    }
}
