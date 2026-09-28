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
    /// ChangelogCategory.Internal is deliberately absent — that is what excludes it. Its
    /// contributors are still credited, on the closing line (<see cref="ClosingCredit"/>).
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

        var closing = ClosingCredit(all, contributors);
        if (closing is not null)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(closing).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Credits everyone the entries above do not — in practice the people of a PR whose
    /// fragments are all internal, which are never rendered and so can carry no attribution
    /// (PR #248's author went uncredited that way). Each person is named once, with their PR
    /// numbers. Null when there is no one to add, so output without a map, or with everyone
    /// already credited, is unchanged.
    /// </summary>
    private static string? ClosingCredit(
        IReadOnlyList<ChangelogFragment> all,
        IReadOnlyDictionary<int, IReadOnlyList<string>> contributors)
    {
        // GitHub logins are case-insensitive.
        var credited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fragment in all.Where(f => f.Category != ChangelogCategory.Internal))
        {
            if (contributors.TryGetValue(fragment.PrNumber, out var logins))
            {
                credited.UnionWith(logins);
            }
        }

        // PRs ascending, map order within a PR: each person lands at their lowest PR, and the
        // first spelling seen is the one printed.
        var prsByLogin = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
        var people = new List<string>();
        foreach (var pr in all.Select(f => f.PrNumber).Distinct().OrderBy(n => n))
        {
            if (!contributors.TryGetValue(pr, out var logins))
            {
                continue;
            }

            foreach (var login in logins.Where(l => !credited.Contains(l)))
            {
                if (!prsByLogin.TryGetValue(login, out var prs))
                {
                    prs = new SortedSet<int>();
                    prsByLogin[login] = prs;
                    people.Add(login);
                }

                prs.Add(pr);
            }
        }

        if (people.Count == 0)
        {
            return null;
        }

        var credits = people
            .Select(login => $"@{login} ({string.Join(", ", prsByLogin[login].Select(n => $"#{n}"))})")
            .ToList();
        return "Also contributed to this release: " + JoinWithAnd(credits) + ".";
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

        var joined = JoinWithAnd(logins.Select(l => "@" + l).ToList());
        return entry.Body.TrimEnd() + " — " + joined;
    }

    /// <summary>"a", "a and b", "a, b and c" — the one list style every credit uses.</summary>
    private static string JoinWithAnd(IReadOnlyList<string> items) =>
        items.Count == 1
            ? items[0]
            : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

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
