using System.Text.RegularExpressions;

namespace ChangelogBuilder;

/// <summary>
/// Decides whether CI asks a pull request for its "User guide:" line (CORE-19). It asks when the PR adds a
/// feature, aircraft or improvement fragment, or changes one of the pilot-facing hotkey guides. A fix fragment
/// does not count: most fixes restore behavior the guide already describes. Pure: the path lists come from
/// git diff in the workflow and are passed in.
/// </summary>
public static class UserGuideImpact
{
    public const string HotkeyGuidesFolder = "MSFSBlindAssist/HotkeyGuides/";

    private static readonly Regex GuideFragment = new(@"^changelog\.d/[^/]+\.(?:aircraft|feature|improvement)\.md$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The reasons the line is required, one per path, in the order given; empty when it is not.
    /// <paramref name="addedPaths"/> are the files the PR adds (only an added fragment is a new entry; editing
    /// an older one is not), <paramref name="changedPaths"/> every file it adds, changes or deletes.
    /// </summary>
    public static IReadOnlyList<string> Reasons(IEnumerable<string> addedPaths, IEnumerable<string> changedPaths)
    {
        var reasons = new List<string>();

        foreach (string path in Normalize(addedPaths))
        {
            if (GuideFragment.IsMatch(path))
            {
                reasons.Add($"adds {path}");
            }
        }

        foreach (string path in Normalize(changedPaths))
        {
            if (path.StartsWith(HotkeyGuidesFolder, StringComparison.Ordinal))
            {
                reasons.Add($"changes {path}");
            }
        }

        return reasons;
    }

    private static IEnumerable<string> Normalize(IEnumerable<string> paths)
        => paths.Select(p => p.Trim().Replace('\\', '/')).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal);
}
