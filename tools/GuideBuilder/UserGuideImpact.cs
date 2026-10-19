using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>
/// Decides whether CI asks a pull request about the user guide (CORE-19): when the PR adds a feature, aircraft or
/// improvement fragment, or changes one of the pilot-facing hotkey guides. A fix fragment does not count: most
/// fixes restore behavior the guide already describes. Pure: the path lists come from git diff in the workflow.
/// </summary>
public static class UserGuideImpact
{
    public const string GuideFolder = "guide/";
    public const string HotkeyGuidesFolder = "MSFSBlindAssist/HotkeyGuides/";

    private static readonly Regex GuideFragment = new(@"^changelog\.d/[^/]+\.(?:aircraft|feature|improvement)\.md$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The reasons the guide question is asked, one per path, in the order given; empty when it is not.
    /// <paramref name="addedPaths"/> are the files the PR adds (only an added fragment is a new entry; editing an
    /// older one is not), <paramref name="changedPaths"/> every file it adds, changes or deletes.
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

    /// <summary>The guide pages the PR adds, changes or deletes.</summary>
    public static IReadOnlyList<string> GuideChanges(IEnumerable<string> changedPaths)
        => Normalize(changedPaths).Where(p => p.StartsWith(GuideFolder, StringComparison.Ordinal)).ToList();

    private static IEnumerable<string> Normalize(IEnumerable<string> paths)
        => paths.Select(p => p.Trim().Replace('\\', '/')).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal);
}
