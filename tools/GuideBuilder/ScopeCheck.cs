using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>Everything <see cref="ScopeCheck.Evaluate"/> looks at; the workflow reads it from git and GitHub.</summary>
/// <param name="TestClasses">Every "&lt;Name&gt;Tests" the PR's tree declares as a type or a method, in any .cs file.</param>
/// <param name="PrCommits">Full hashes of the commits on the PR's own branch and not on its base.</param>
/// <param name="BaseNames">Every tools/&lt;name&gt; and &lt;Name&gt;Tests the base branch's tree already mentions.</param>
public sealed record ScopeCheckInput(
    string Description,
    string BaseBranch,
    IReadOnlyList<string> AddedPaths,
    IReadOnlyList<AddedLine> AddedLines,
    IReadOnlyCollection<string> TreePaths,
    IReadOnlyCollection<string> TestClasses,
    string SwitchBefore,
    string SwitchAfter,
    IReadOnlyList<int> StackedWith,
    IReadOnlyCollection<string> PrCommits,
    IReadOnlyCollection<string> BaseNames);

/// <summary>
/// The advisory PR scope check (CORE-22 to CORE-25, and CORE-5's "Sim-tested on:" line). Pure: the workflow reads git
/// and GitHub, this decides. It is not a required check.
/// </summary>
public static class ScopeCheck
{
    private static readonly Regex SwitchArm = new(@"^\s*""(?<code>[A-Z0-9_]+)""\s*=>\s*new\s+\w+Definition\(\)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex Fragment = new(
        @"^changelog\.d/[1-9][0-9]*-[a-z0-9][a-z0-9-]*\.(?<cat>aircraft|feature)\.md$", RegexOptions.CultureInvariant);

    private static readonly Regex ToolName = new(
        @"(?<![\w/.:-])tools/(?<name>[A-Za-z0-9_][A-Za-z0-9_.-]*[A-Za-z0-9_])", RegexOptions.CultureInvariant);

    private static readonly Regex TestClassName = new(@"\b[A-Z][A-Za-z0-9]*Tests\b", RegexOptions.CultureInvariant);

    private static readonly Regex ToolReadme = new(@"^tools/[^/]+/README\.md$", RegexOptions.CultureInvariant);

    private static readonly Regex HashToken = new(@"\b[0-9A-Fa-f]{7,40}\b", RegexOptions.CultureInvariant);

    /// <summary>The aircraft codes the factory switch builds a definition for, in order.</summary>
    public static IReadOnlyList<string> AircraftCodes(string switchSource)
        => SwitchArm.Matches(switchSource).Select(m => m.Groups["code"].Value).Distinct().ToList();

    public static (bool Pass, string Message) Evaluate(ScopeCheckInput input)
    {
        ScopeLinesResult lines = ScopeLines.Parse(input.Description);
        var problems = new List<string>();

        bool baseIsMain = input.BaseBranch == "main";
        if (!baseIsMain && lines.PlannedIn is null)
        {
            problems.Add($"CORE-23: this PR's base is {input.BaseBranch}, not main, and the description has no " +
                "\"Planned in: <where the plan is written>\" line. Add it, naming the document that says what the " +
                "follow-ups are, or retarget the PR at main.");
        }

        IReadOnlyList<string> before = AircraftCodes(input.SwitchBefore);
        List<string> newCodes = AircraftCodes(input.SwitchAfter).Except(before).ToList();
        if (newCodes.Count > 1 && lines.Variants is null)
        {
            problems.Add($"CORE-22: this PR adds {newCodes.Count} aircraft ({string.Join(", ", newCodes)}) with no " +
                "\"Variants: <the family they share>\" line. One aircraft per PR; a family of variants of one " +
                "add-on may share one, and the line says so.");
        }

        int aircraftFragments = 0;
        int featureFragments = 0;
        foreach (string path in input.AddedPaths)
        {
            Match match = Fragment.Match(path.Replace('\\', '/'));
            if (!match.Success) continue;
            if (match.Groups["cat"].Value == "aircraft") aircraftFragments++;
            else featureFragments++;
        }

        if (aircraftFragments >= 1 && featureFragments >= 1)
        {
            problems.Add("CORE-22: this PR adds both an aircraft and a feature fragment. If the feature is part of " +
                "the aircraft's support, describe it in the aircraft's fragment and drop the feature one; otherwise " +
                "split the PR. A Variants line does not explain this: it covers several aircraft, not an aircraft " +
                "plus a feature.");
        }
        else if (aircraftFragments >= 2 && lines.Variants is null)
        {
            problems.Add($"CORE-22: this PR adds {aircraftFragments} aircraft fragments with no \"Variants: <the " +
                "family they share>\" line. One aircraft per PR, unless they are variants of one add-on.");
        }

        problems.AddRange(DanglingNames(input));

        if (baseIsMain && input.StackedWith.Count > 0)
        {
            string others = string.Join(", ", input.StackedWith.Select(n => $"#{n}"));
            problems.Add($"CORE-23: this PR into main is stacked with another open PR ({others}): it shares commits " +
                "with it. Rebase on fresh main so it carries only its own commits, or retarget it at the other " +
                "PR's branch and add a \"Planned in:\" line.");
        }

        problems.AddRange(UnknownBuilds(lines, input.PrCommits));

        const string Advisory = "This check is advisory: it does not block merging. See CONTRIBUTING.md.";
        if (problems.Count > 0)
        {
            return (false, string.Join("\n", problems.Select(p => "- " + p)) + "\n\n" + Advisory);
        }

        string builds = lines.SimTestedOn.Count > 0
            ? $"Sim-tested on: {string.Join("; ", lines.SimTestedOn)}"
            : "no Sim-tested on line";
        return (true, "Scope check passed. Checked: base branch and Planned in (CORE-23), one aircraft or feature " +
            "(CORE-22), stacked PRs (CORE-23), tools and test classes named in added lines (CORE-25), " +
            $"Sim-tested on builds (CORE-5; {builds}).\n\n{Advisory}");
    }

    private static IEnumerable<string> DanglingNames(ScopeCheckInput input)
    {
        var tools = new List<string>();
        var tests = new List<string>();

        foreach (AddedLine added in input.AddedLines)
        {
            string path = added.Path.Replace('\\', '/');
            bool scanned = path.StartsWith("MSFSBlindAssist/", StringComparison.Ordinal) ||
                           path.StartsWith("tests/", StringComparison.Ordinal) ||
                           ToolReadme.IsMatch(path);
            if (!scanned || IsExempt(added.Text)) continue;

            foreach (Match match in ToolName.Matches(added.Text))
            {
                string tool = "tools/" + match.Groups["name"].Value;
                bool inTree = input.TreePaths.Any(p =>
                    p == tool || p.StartsWith(tool + "/", StringComparison.Ordinal));
                if (!inTree && !input.BaseNames.Contains(tool) && !tools.Contains(tool)) tools.Add(tool);
            }

            foreach (Match match in TestClassName.Matches(added.Text))
            {
                string name = match.Value;
                if (!input.TestClasses.Contains(name) && !input.BaseNames.Contains(name) && !tests.Contains(name))
                {
                    tests.Add(name);
                }
            }
        }

        if (tools.Count > 0)
        {
            yield return $"CORE-25: added lines name {string.Join(", ", tools)}, which is not in the tree. A tool a " +
                "comment or test relies on must be in the repository; if it is a one-off, say \"one-off\" and " +
                "\"not kept\" on the line, and if it lives in the project's private repo, say so.";
        }

        if (tests.Count > 0)
        {
            yield return $"CORE-25: added lines name {string.Join(", ", tests)}, which no file declares. A test " +
                "a comment cites must exist; add it or fix the name.";
        }
    }

    private static bool IsExempt(string text)
        => (text.Contains("one-off", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("not kept", StringComparison.OrdinalIgnoreCase)) ||
           text.Contains("private repo", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("layouts store", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> UnknownBuilds(ScopeLinesResult lines, IReadOnlyCollection<string> prCommits)
    {
        var unknown = new List<string>();
        foreach (string value in lines.SimTestedOn)
        {
            foreach (Match match in HashToken.Matches(value))
            {
                string token = match.Value;
                bool hasDigit = token.Any(char.IsDigit);
                bool hasLetter = token.Any(c => char.IsLetter(c));
                if (!hasDigit || !hasLetter) continue;
                if (prCommits.Any(c => c.StartsWith(token, StringComparison.OrdinalIgnoreCase))) continue;
                if (!unknown.Contains(token, StringComparer.OrdinalIgnoreCase)) unknown.Add(token);
            }
        }

        if (unknown.Count > 0)
        {
            yield return $"CORE-5: the Sim-tested on line names {string.Join(", ", unknown)}, which is not a commit on " +
                "this PR's branch (a combined build with other open PRs merged in, a merge that was not pushed, a " +
                "build of main alone, or a commit a rebase replaced). This line names only builds of this PR, so " +
                "report this PR's results from its own build, a commit pushed to its branch. A \"before\" result " +
                "on main belongs in the test plan's text, labelled, not on this line; or say on the line which " +
                "build it was.";
        }
    }
}
