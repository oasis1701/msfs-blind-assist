using System.Globalization;
using System.Text.RegularExpressions;

namespace ChangelogBuilder;

/// <summary>
/// Parses the machine-written contributor map (`&lt;pr&gt;=&lt;login&gt;[,&lt;login&gt;...]` per line)
/// that tools/changelog-contributors.sh generates and the --contributors option consumes.
///
/// Malformed input is an ERROR, never a skip: this file is generated, so a bad line means
/// the generator broke, and tolerating it would silently misattribute entries. The
/// graceful path lives in the generator instead — a PR it cannot resolve is OMITTED, and
/// the renderer leaves a fragment whose PR is absent from the map unattributed.
/// </summary>
public static class ContributorMap
{
    // Same "no leading zero" rule as the fragment filename grammar. Logins are GitHub's
    // alphabet (letters, digits, dashes); the renderer adds the @ itself, so one in the
    // file is a generator glitch, not a valid spelling.
    private static readonly Regex PrPattern = new(
        "^[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LoginPattern = new(
        "^[A-Za-z0-9-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Pure: parses the file's text. Blank lines and #-comments are skipped; whitespace
    /// around tokens is tolerated. Every error is collected, not just the first.
    /// </summary>
    public static ContributorMapResult Parse(string content)
    {
        var map = new Dictionary<int, IReadOnlyList<string>>();
        var errors = new List<string>();

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var lineNumber = i + 1;
            var parts = line.Split('=');

            if (parts.Length != 2 || !PrPattern.IsMatch(parts[0].Trim()))
            {
                errors.Add(
                    $"line {lineNumber}: expected <pr>=<login>[,<login>...] with no leading " +
                    $"zero in the PR number, got \"{line}\".");
                continue;
            }

            // TryParse, not Parse: a number long enough to overflow int passed the shape
            // check above, and Parse must report, never throw.
            if (!int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pr))
            {
                errors.Add($"line {lineNumber}: PR number \"{parts[0].Trim()}\" is out of range.");
                continue;
            }

            if (map.ContainsKey(pr))
            {
                errors.Add($"line {lineNumber}: duplicate entry for PR {pr}.");
                continue;
            }

            var logins = parts[1].Split(',').Select(l => l.Trim()).ToList();

            if (logins.Count == 0 || logins.Any(l => !LoginPattern.IsMatch(l)))
            {
                errors.Add(
                    $"line {lineNumber}: logins must be non-empty and contain only letters, " +
                    $"digits and dashes (no @), got \"{parts[1].Trim()}\".");
                continue;
            }

            map[pr] = logins;
        }

        return new ContributorMapResult(map, errors);
    }
}

/// <summary>Outcome of <see cref="ContributorMap.Parse"/>. Map holds every valid line.</summary>
public sealed record ContributorMapResult(
    IReadOnlyDictionary<int, IReadOnlyList<string>> Map,
    IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;
}
