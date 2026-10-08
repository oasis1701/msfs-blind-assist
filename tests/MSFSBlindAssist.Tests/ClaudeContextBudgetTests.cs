using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Keeps what every Claude Code session and subagent loads at start small, and keeps the
/// path-scoped rule files that replaced CLAUDE.md's invariants well formed. CLAUDE.md regrew from
/// 26,000 to 518,000 characters in twelve weeks after the July cleanup because nothing enforced its
/// "one line here, the story in the doc" intent; an idle general-purpose subagent paid about 253,000
/// tokens for it (measured 2026-09-30). Every failure says what to do instead.
/// Design: docs/design/2026-09-30-lean-claude-md-design.md. How to add a rule: CLAUDE.md.
/// </summary>
public class ClaudeContextBudgetTests
{
    public const int ClaudeMdMaxChars = 25_000;
    public const int ClaudeMdMaxLines = 200;
    public const int RuleLineMaxChars = 400;
    public const int RuleFileMaxChars = 12_000;
    public const int RuleFileProseMaxChars = 1_000;
    public const int PerFileLoadMaxChars = 30_000;

    private const string HowToAdd =
        "A rule is ONE line in its area's .claude/rules/<area>.md file; its explanation, measurements and history go under "
        + "'## <ID>' in docs/invariants/<area>.md. See \"Adding or changing a rule\" in CLAUDE.md.";

    private static readonly Regex IdStart = new(@"^- \[[A-Z][A-Z0-9]*-\d+\]", RegexOptions.CultureInvariant);
    private static readonly Regex RuleLine = new(
        @"^- \[(?<id>[A-Z][A-Z0-9]*-\d+)\] (?<text>\S.*?) Full: (?<file>docs/invariants/[a-z0-9-]+\.md)#(?<anchor>[a-z0-9-]+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex IdHeading = new(@"^## (?<id>[A-Z][A-Z0-9]*-\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex AnyIdHeading = new(@"^## (?<id>[A-Z][A-Z0-9]*-\d+)(?: \(retired:.*\))?$", RegexOptions.CultureInvariant);
    private static readonly Regex IdCitation = new(@"\[(?<id>[A-Z][A-Z0-9]*-\d+)\]", RegexOptions.CultureInvariant);
    private static readonly Regex QuotedPathItem = new("^ +- \"[^\"]+\"$", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLink =new(@"\]\((?<target>[^)\s]+)\)", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> PrunedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", ".vs", "TestResults" };

    [Theory]
    [InlineData("MSFSBlindAssist/Services/Gsx/**", "MSFSBlindAssist/Services/Gsx/Remote/GsxRemoteConnection.cs", true)]
    [InlineData("MSFSBlindAssist/Services/Gsx/**", "MSFSBlindAssist/Services/GsxService.cs", false)]
    [InlineData("MSFSBlindAssist/SimConnect/*.cs", "MSFSBlindAssist/SimConnect/SimConnectManager.cs", true)]
    [InlineData("MSFSBlindAssist/SimConnect/*.cs", "MSFSBlindAssist/SimConnect/MD11/Md11McduDataManager.cs", false)]
    [InlineData("tests/MSFSBlindAssist.Tests/**/*Gsx*.cs", "tests/MSFSBlindAssist.Tests/GsxGateSelectPlanTests.cs", true)]
    [InlineData("tests/MSFSBlindAssist.Tests/**/*Gsx*.cs", "tests/MSFSBlindAssist.Tests/Gsx/GsxFooTests.cs", true)]
    [InlineData("MSFSBlindAssist/Aircraft/PMDG777*.cs", "MSFSBlindAssist/Aircraft/PMDG777Definition.SystemDisplay.cs", true)]
    [InlineData("MSFSBlindAssist/Aircraft/PMDG777*.cs", "MSFSBlindAssist/aircraft/PMDG777Definition.cs", false)]
    [InlineData("MSFSBlindAssist/MainForm.cs", "MSFSBlindAssist/MainForm.cs.bak", false)]
    [InlineData("MSFSBlindAssist/MainForm.cs", "MSFSBlindAssistXMainForm.cs", false)]
    public void Glob_matches_the_way_the_rule_files_expect(string glob, string path, bool expected)
        => Assert.Equal(expected, GlobMatches(glob, path));

    [Fact]
    public void An_empty_front_matter_reads_as_no_paths_instead_of_throwing()
    {
        (List<string>? globs, string body, _) = SplitFrontMatter("---\n---\n# Rules\n");
        Assert.Empty(globs ?? new List<string>());
        Assert.Equal("# Rules\n", body);
    }

    [Theory]
    [InlineData("---\npaths:\n  - \"a/**\"\n---\n", null)]
    [InlineData("\uFEFF---\npaths:\n  - \"a/**\"\n---\n", "byte-order mark")]
    [InlineData("---\r\npaths:\r\n  - \"a/**\"\r\n---\r\n", "CRLF")]
    public void A_rule_file_must_be_bom_free_LF_text(string content, string? expectedProblem)
    {
        List<string> problems = RawFormatProblems("x.md", Encoding.UTF8.GetBytes(content)).ToList();
        if (expectedProblem is null) Assert.Empty(problems);
        else Assert.Contains(problems, p => p.Contains(expectedProblem, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("  - \"a/**\"", true)]
    [InlineData("\t- \"a/**\"", false)]
    [InlineData("  - a/**", false)]
    public void A_paths_item_is_a_double_quoted_glob_indented_with_spaces(string item, bool accepted)
        => Assert.Equal(accepted, QuotedPathItem.IsMatch(item));

    [Theory]
    [InlineData(1, 120, false)]
    [InlineData(2, 300, false)]
    [InlineData(1, 401, true)]
    [InlineData(3, 350, true)]
    public void A_rule_file_body_holds_rule_lines_not_prose(int proseLines, int proseLength, bool rejected)
    {
        // The first prose line is a preamble; the rest follow the rule, as a "Mirrored from …:" header does.
        var body = new StringBuilder("# Area rules\n\n").Append('a', proseLength).Append("\n\n")
            .Append("- [X-1] Never do the thing. Full: docs/invariants/x.md#x-1\n");
        for (int i = 1; i < proseLines; i++) body.Append('\n').Append('a', proseLength).Append('\n');
        Assert.Equal(rejected, RuleBodyProblems("x.md", body.ToString()).Count > 0);
    }

    [Theory]
    [InlineData("## A-1\n", "## A-2\n", "see [A-1]", false)]
    [InlineData("## A-1\n", "## A-1\n", "", true)]
    [InlineData("## A-1 (retired: merged into A-2)\n", "## A-1\n", "", true)]
    [InlineData("## A-1 (retired: merged into A-2)\n", "## A-2\n", "see [A-1]", false)]
    [InlineData("## A-1\n", "## A-2\n", "see [A-3]", true)]
    [InlineData("## A-1\n", "## A-2\n", "the [MD-11] reads [UTF-8] text", false)]
    public void An_ID_names_one_rule_forever_and_every_citation_resolves(string fullText1, string fullText2, string citing,
        bool rejected)
    {
        var fullTexts = new[] { ("one.md", fullText1), ("two.md", fullText2) };
        Assert.Equal(rejected, IdProblems(fullTexts, new[] { ("doc.md", citing) }).Count > 0);
    }

    [Theory]
    [InlineData("MSFSBlindAssist/Services/StandId.cs", "tests/MSFSBlindAssist.Tests/StandIdTests.cs", "", "gsx.md", true)]
    [InlineData("MSFSBlindAssist/Services/StandId.cs", "tests/MSFSBlindAssist.Tests/StandIdTests.cs", "gsx.md", "gsx.md", false)]
    [InlineData("MSFSBlindAssist/Services/StandId.cs", "tests/MSFSBlindAssist.Tests/StandIdTests.cs", "other.md", "gsx.md", false)]
    [InlineData("MSFSBlindAssist/Services/StandId.cs", "tests/MSFSBlindAssist.Tests/StandIdTests.cs", "", "", false)]
    [InlineData("MSFSBlindAssist/Services/LandingGuidanceLaws.cs", "tests/MSFSBlindAssist.Tests/LandingGuidanceLawTests.cs", "", "rollout.md", true)]
    [InlineData("MSFSBlindAssistUpdater/Updater.cs", "tests/MSFSBlindAssist.Tests/Updates/UpdaterTests.cs", "", "updates.md", true)]
    [InlineData("MSFSBlindAssist/Services/StandIdParser.cs", "tests/MSFSBlindAssist.Tests/StandIdTests.cs", "", "gsx.md", false)]
    [InlineData("tools/CDUTest/Program.cs", "tests/MSFSBlindAssist.Tests/ProgramTests.cs", "", "core.md", false)]
    public void Code_loads_a_rule_file_when_its_test_does(string code, string test, string codeLoads, string testLoads,
        bool flagged)
    {
        var loads = new Dictionary<string, List<string>>
        {
            [code] = codeLoads.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
            [test] = testLoads.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
        };
        Assert.Equal(flagged, CodeMissingItsTestsRules(new[] { code, test }, f => loads[f]).Count > 0);
    }

    [Theory]
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Afdx.cs", ".claude/rules/a220.md", "MSFSBlindAssist/Aircraft/A220/**", "", false)]
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Afdx.cs", ".claude/rules/a220.md", "MSFSBlindAssist/Forms/A220/**", "", true)]
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Afdx.cs", ".claude/rules/variable-definitions.md", "MSFSBlindAssist/Aircraft/**", "", true)]
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Definition.cs", ".claude/rules/troubleshooting.md", "MSFSBlindAssist/Aircraft/**/*Definition*.cs", "", true)]
    [InlineData("MSFSBlindAssist/Aircraft/WiperPosition.cs", ".claude/rules/variable-definitions.md", "MSFSBlindAssist/Aircraft/**", "", false)]
    [InlineData("MSFSBlindAssist/Services/TcasService.cs", ".claude/rules/tcas.md", "MSFSBlindAssist/Services/Tcas*.cs", "", false)]
    [InlineData("MSFSBlindAssist/Services/TcasService.cs", ".claude/rules/weather.md", "MSFSBlindAssist/Services/Weather*.cs", "", true)]
    [InlineData("MSFSBlindAssistUpdater/Program.cs", ".claude/rules/core.md", "MSFSBlindAssist/**", "", true)]
    [InlineData("plugins/VPilotPlugin/Plugin.cs", ".claude/rules/core.md", "MSFSBlindAssist/**", "", true)]
    [InlineData("MSFSBlindAssist/Resources/flypad-shell.html", ".claude/rules/flypad.md", "MSFSBlindAssist/Resources/coherent-flypad-agent.js", "", true)]
    [InlineData("MSFSBlindAssist/Resources/md11_control_map.json", ".claude/rules/md11.md", "MSFSBlindAssist/Aircraft/MD11/**", "", false)]
    [InlineData("tests/MSFSBlindAssist.Tests/TcasServiceTests.cs", ".claude/rules/core.md", "MSFSBlindAssist/**", "", false)]
    [InlineData("tools/CDUTest/Program.cs", ".claude/rules/core.md", "MSFSBlindAssist/**", "", false)]
    [InlineData("MSFSBlindAssist/Utils/Logging/Log.cs", ".claude/rules/core.md", "MSFSBlindAssist/Services/**", "MSFSBlindAssist/Utils/Logging/", false)]
    [InlineData("MSFSBlindAssist/Utils/LoggingHelpers.cs", ".claude/rules/core.md", "MSFSBlindAssist/Services/**", "MSFSBlindAssist/Utils/Logging/", true)]
    [InlineData("MSFSBlindAssist/Program.cs", ".claude/rules/core.md", "MSFSBlindAssist/Services/**", "MSFSBlindAssist/Program.cs", false)]
    [InlineData("MSFSBlindAssist/Forms/TrackFixForm.Designer.cs", ".claude/rules/core.md", "MSFSBlindAssist/Services/**", "MSFSBlindAssist/Forms/TrackFixForm.cs", true)]
    [InlineData("MSFSBlindAssist/Forms/Settings/AudioPanel.cs", ".claude/rules/core.md", "MSFSBlindAssist/Services/**", "MSFSBlindAssist/Forms/Settings/", false)]
    [InlineData("MSFSBlindAssist/Resources/coherent-x-agent.js", ".claude/rules/core.md", "MSFSBlindAssist/Services/**", "MSFSBlindAssist/Resources/", true)]
    public void A_shipped_code_file_needs_a_rule_file_or_an_exemption(string file, string ruleFile, string glob, string exemption,
        bool flagged)
        => Assert.Equal(flagged, ShippedFilesLoadingNoRuleFile(new[] { file }, new[] { (ruleFile, new List<string> { glob }) },
            exemption.Length == 0 ? Array.Empty<string>() : new[] { exemption }).Count > 0);

    [Theory]
    [InlineData("MSFSBlindAssist/Program.cs", false)]
    [InlineData("MSFSBlindAssist/Gone.cs", true)]
    [InlineData("MSFSBlindAssist/Utils/Logging/", false)]
    [InlineData("MSFSBlindAssist/Utils/Gone/", true)]
    [InlineData("MSFSBlindAssist/Utils/Logging", true)]
    public void An_exemption_entry_names_a_file_or_folder_that_exists(string entry, bool flagged)
        => Assert.Equal(flagged, ExemptionEntriesMatchingNoFile(
            new[] { "MSFSBlindAssist/Program.cs", "MSFSBlindAssist/Utils/Logging/Log.cs" }, new[] { entry }).Count > 0);

    [Theory]
    [InlineData("MSFSBlindAssist/Resources/coherent-x-agent.js", false)]
    [InlineData("MSFSBlindAssist/Services/TcasService.cs", true)]
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Afdx.cs", true)]
    public void The_coverage_failure_offers_an_exemption_only_where_one_can_work(string file, bool offersExemption)
        => Assert.Equal(offersExemption, UncoveredFileProblem(file).Contains("CoverageExemptions", StringComparison.Ordinal));

    [Fact]
    public void A_rule_file_loads_its_body_not_its_front_matter()
        => Assert.Equal("# Rules\n- [X-1] r\n".Length, LoadedChars("---\npaths:\n  - \"a/**\"\n---\n# Rules\n- [X-1] r\n"));

    [Theory]
    [InlineData(11_000, false)]
    [InlineData(12_001, true)]
    public void A_rule_files_size_cap_counts_its_body_not_its_globs(int bodyLength, bool over)
    {
        string globs = string.Concat(Enumerable.Range(0, 60).Select(i => $"  - \"MSFSBlindAssist/Area/File{i:D2}.cs\"\n"));
        Assert.Equal(over, OverRuleFileBudget("---\npaths:\n" + globs + "---\n" + new string('a', bodyLength)));
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("- Never do the thing.\n", "", true)]
    [InlineData("", "### Flysimware Learjet 35A\n", true)]
    [InlineData("", "- **[Citation Sovereign+](docs/c680.md)** - Skyward C680\n", true)]
    [InlineData("", "See [a](docs/a.md#part).\n", false)]
    [InlineData("", "```\n# a shell comment\n```\n", false)]
    public void CLAUDE_md_keeps_its_outline_rule_lines_and_map(string inRules, string atEnd, bool rejected)
    {
        string text = "# T\n## Rules for any file\n- [X-1] Never do the thing. Full: docs/invariants/x.md#x-1\n" + inRules
            + "## Where things live\n| [a.md](docs/a.md) | when | x |\n" + atEnd;
        string[] outline = { "# T", "## Rules for any file", "## Where things live" };
        Assert.Equal(rejected, ClaudeMdStructureProblems(text, outline).Count > 0);
    }

    [Fact]
    public void CLAUDE_md_stays_within_its_budget()
    {
        string text = Read(Path.Combine(RepoRoot(), "CLAUDE.md"));
        int lines = text.TrimEnd('\n').Split('\n').Length;
        Assert.True(text.Length <= ClaudeMdMaxChars && lines <= ClaudeMdMaxLines,
            $"CLAUDE.md is {text.Length:N0} characters and {lines} lines; the budget is {ClaudeMdMaxChars:N0} and "
            + $"{ClaudeMdMaxLines}. It is loaded into every session and every general-purpose subagent, so it takes only "
            + "rules that apply to ANY file. " + HowToAdd);
    }

    [Fact]
    public void CLAUDE_md_keeps_its_outline_and_maps_every_doc_it_links()
    {
        List<string> problems = ClaudeMdStructureProblems(Read(Path.Combine(RepoRoot(), "CLAUDE.md")), ClaudeMdOutline);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_rule_file_is_scoped_well_formed_and_within_budget()
    {
        var problems = new List<string>();
        foreach (RuleFile rf in RuleFiles())
        {
            problems.AddRange(RawFormatProblems(rf.Name, File.ReadAllBytes(rf.Path)));
            if (rf.Globs is null || rf.Globs.Count == 0)
                problems.Add($"{rf.Name}: no 'paths:' list. A rule file without paths loads in EVERY session; "
                    + "scope it to the code it guards, one '  - \"<glob>\"' line per glob (the comma-separated form is not "
                    + "accepted here), or, if it truly applies to any file, move it into CLAUDE.md.");
            foreach (string item in rf.PathItems)
                if (!QuotedPathItem.IsMatch(item))
                    problems.Add($"{rf.Name}: paths item '{item.Trim()}' must be a double-quoted glob indented with spaces, "
                        + "'  - \"<glob>\"'. Unquoted, YAML can read '*' as an alias or '#' as a comment, and a tab in the "
                        + "indentation is a YAML error; Claude Code then ignores the front matter and loads the file in EVERY "
                        + "session.");
            foreach (string g in rf.Globs ?? new List<string>())
            {
                if (g.Contains('{') || g.Contains('['))
                    problems.Add($"{rf.Name}: glob '{g}' uses braces or brackets. Claude Code expands braces, but this "
                        + "test's matcher does not, so it could not check the glob; list each pattern separately.");
                if (g.Split('/').Any(seg => seg.Contains("**", StringComparison.Ordinal) && seg != "**"))
                    problems.Add($"{rf.Name}: glob '{g}' uses '**' inside a path segment. This test's matcher supports "
                        + "'**' only as a whole segment ('a/**/b'), and the Claude Code docs do not say how they read it "
                        + "elsewhere; rewrite the glob with '*' or a whole-segment '**'.");
            }
            if (OverRuleFileBudget(rf.Text))
                problems.Add($"{rf.Name}: {LoadedChars(rf.Text):N0} characters, over {RuleFileMaxChars:N0}. Split the area into "
                    + "two rule files with narrower paths, or shorten its lines.");
            problems.AddRange(RuleBodyProblems(rf.Name, rf.Body));
        }
        foreach (string line in Read(Path.Combine(RepoRoot(), "CLAUDE.md")).Split('\n'))
            if (IdStart.IsMatch(line))
                CheckRuleLine("CLAUDE.md", line, problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_rule_file_glob_still_matches_a_file()
    {
        List<string> files = RepoFiles().ToList();
        var problems = new List<string>();
        foreach (RuleFile rf in RuleFiles())
            foreach (string g in rf.Globs ?? new List<string>())
            {
                var re = GlobRegex(g);
                if (!files.Any(f => re.IsMatch(f)))
                    problems.Add($"{rf.Name}: glob '{g}' matches no file, so its rules never load. The code moved or was "
                        + "renamed; point the glob at where it lives now.");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_shipped_code_file_loads_a_rule_file_or_is_exempt()
    {
        List<RuleFile> ruleFiles = RuleFiles().ToList();
        List<string> files = RepoFiles().ToList();
        List<string> exemptions = CoverageExemptions.Values.SelectMany(entries => entries).ToList();
        List<string> problems = SharedAircraftRules.Where(name => ruleFiles.All(rf => rf.Name != name))
            .Select(name => $"{name} is in SharedAircraftRules but does not exist. If it moved or was renamed, update "
                + "SharedAircraftRules, or this check counts it as every aircraft's own rule file.")
            .Concat(ExemptionEntriesMatchingNoFile(files, exemptions)
                .Select(entry => $"{entry} in CoverageExemptions matches no file. It moved, was renamed or was deleted: "
                    + "point the entry at where it lives now, or drop it."))
            .Concat(ShippedFilesLoadingNoRuleFile(files, ruleFiles.Select(rf => (rf.Name, rf.Globs ?? new List<string>())),
                    exemptions)
                .Select(UncoveredFileProblem))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>What the coverage check says about a shipped file no rule file loads: the ways out that can work
    /// for that file. A Coherent agent script gets no exemption advice, since <see cref="IsExempt"/> never exempts
    /// one.</summary>
    private static string UncoveredFileProblem(string file)
        => IsCoherentAgentScript(file)
            ? $"{file} loads no rule file of its own. A Coherent agent script is never exempt (the owner's choice, "
                + "2026-10-05): glob it into its aircraft's or area's .claude/rules file, or give a new one a rule file of "
                + "its own whose preamble names its doc (CLAUDE.md, \"Adding or changing a rule\")."
            : $"{file} loads no rule file"
            + (IsAreaOwnedFile(file) ? " of its own (the shared aircraft rules in SharedAircraftRules do not count)" : "")
            + ", so no rule reaches whoever edits it. Add a glob for it to its area's .claude/rules file. A new "
            + "feature or aircraft with no rules yet gets a rule file of its own whose preamble names its doc "
            + "(CLAUDE.md, \"Adding or changing a rule\"). If no area's rules apply, add it to CoverageExemptions "
            + "under the reason that fits, or under a new reason saying why none applies.";

    [Fact]
    public void Every_tested_code_file_loads_a_rule_file_when_its_test_does()
    {
        var compiled = RuleFiles().Select(rf => (rf.Name, Globs: (rf.Globs ?? new List<string>()).Select(GlobRegex).ToList()))
            .ToList();
        List<string> Loads(string file) => compiled.Where(c => c.Globs.Any(g => g.IsMatch(file))).Select(c => c.Name).ToList();
        List<string> problems = CodeMissingItsTestsRules(RepoFiles(), Loads);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_rule_points_at_its_full_text_and_every_full_text_has_a_rule()
    {
        string root = RepoRoot();
        var problems = new List<string>();
        var rules = new Dictionary<string, string>(StringComparer.Ordinal);   // id -> full-text file
        var firstLine = new Dictionary<string, (string Name, string Line)>(StringComparer.Ordinal);
        IEnumerable<(string Name, string Line)> lines = RuleFiles()
            .SelectMany(rf => rf.Body.Split('\n').Select(l => (rf.Name, l)))
            .Concat(Read(Path.Combine(root, "CLAUDE.md")).Split('\n').Select(l => ("CLAUDE.md", l)));
        foreach ((string name, string line) in lines)
        {
            Match m = RuleLine.Match(line);
            if (!m.Success) continue;
            string id = m.Groups["id"].Value, file = m.Groups["file"].Value;
            // A rule whose code spans areas may be MIRRORED: the same line, word for word, in a second rule
            // file (or CLAUDE.md) so it also loads with that code. A second line under the same ID that is
            // not identical is either drift or a reused ID.
            if (firstLine.TryGetValue(id, out var first))
            {
                if (first.Line != line)
                    problems.Add($"{name}: [{id}] differs from its line in {first.Name}. A mirrored rule must be the same "
                        + "line word for word; a new rule takes the next unused number.");
                continue;
            }
            firstLine[id] = (name, line);
            rules[id] = file;
            if (m.Groups["anchor"].Value != id.ToLowerInvariant())
                problems.Add($"{name}: [{id}] points at #{m.Groups["anchor"].Value}; the anchor must be #{id.ToLowerInvariant()}.");
            string path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                problems.Add($"{name}: [{id}] points at {file}, which does not exist.");
            else if (!Read(path).Split('\n').Any(l => l == $"## {id}"))
                problems.Add($"{name}: [{id}] has no '## {id}' section in {file}. " + HowToAdd);
        }
        foreach (string path in InvariantFiles())
        {
            string rel = Rel(root, path);
            foreach (string line in Read(path).Split('\n'))
            {
                Match h = IdHeading.Match(line);
                if (!h.Success) continue;
                string id = h.Groups["id"].Value;
                if (!rules.TryGetValue(id, out string? owner))
                    problems.Add($"{rel}: '## {id}' has no rule line. Add '- [{id}] … Full: {rel}#{id.ToLowerInvariant()}' "
                        + "to the area's rule file, or to CLAUDE.md if it applies to any file.");
                else if (owner != rel)
                    problems.Add($"{rel}: '## {id}' is claimed by a rule line pointing at {owner}.");
            }
        }
        IEnumerable<string> citing = new[] { Path.Combine(root, "CLAUDE.md") }.Concat(RuleFiles().Select(rf => rf.Path))
            .Concat(InvariantFiles()).Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md"));
        problems.AddRange(IdProblems(InvariantFiles().Select(p => (Rel(root, p), Read(p))),
            citing.Select(p => (Rel(root, p), Read(p)))));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_single_code_file_loads_more_rules_than_the_budget()
    {
        string root = RepoRoot();
        var compiled = RuleFiles().Select(rf => (rf, Globs: (rf.Globs ?? new List<string>()).Select(GlobRegex).ToList())).ToList();
        var problems = new List<string>();
        foreach (string file in RepoFiles())
        {
            var loaded = compiled.Where(c => c.Globs.Any(g => g.IsMatch(file))).Select(c => c.rf).ToList();
            int total = loaded.Sum(rf => LoadedChars(rf.Text));
            if (total > PerFileLoadMaxChars)
                problems.Add($"{file} loads {total:N0} characters of rules ({string.Join(", ", loaded.Select(r => r.Name))}); "
                    + $"the budget is {PerFileLoadMaxChars:N0}. Shorten lines, or, where an area globs this file for only a "
                    + "few of its rules, mirror those lines into a rule file scoped here in place of the glob. Never just "
                    + "drop a glob: its rules would stop loading with the code they guard.");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Links_in_CLAUDE_md_rule_files_and_full_texts_resolve()
    {
        string root = RepoRoot();
        var problems = new List<string>();
        IEnumerable<string> sources = new[] { Path.Combine(root, "CLAUDE.md") }
            .Concat(RuleFiles().Select(rf => rf.Path)).Concat(InvariantFiles());
        foreach (string source in sources)
            foreach (Match m in MarkdownLink.Matches(Read(source)))
            {
                string target = m.Groups["target"].Value;
                if (Regex.IsMatch(target, "^(https?:|mailto:|#)", RegexOptions.CultureInvariant)) continue;
                string pathPart = target.Split('#')[0];
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,
                    pathPart.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                    problems.Add($"{Rel(root, source)}: link '{target}' does not resolve.");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---- helpers ----

    private static void CheckRuleLine(string name, string line, List<string> problems)
    {
        string head = line.Length > 60 ? line[..60] + "…" : line;
        if (!RuleLine.IsMatch(line))
            problems.Add($"{name}: '{head}' is not a rule line ('- [ID] <rule> Full: docs/invariants/<area>.md#<id>'). "
                + "Rule files hold rule lines only. " + HowToAdd);
        else if (line.Contains("<<", StringComparison.Ordinal))
            problems.Add($"{name}: '{head}' still has its placeholder; write the one-line rule.");
        if (line.Length > RuleLineMaxChars)
            problems.Add($"{name}: '{head}' is {line.Length} characters, over {RuleLineMaxChars}. " + HowToAdd);
    }

    /// <summary>An ID names one rule forever: it heads ONE full text, retired or not, and every '[ID]' citation names
    /// one of them.</summary>
    private static List<string> IdProblems(IEnumerable<(string Name, string Text)> fullTexts,
        IEnumerable<(string Name, string Text)> citingTexts)
    {
        var problems = new List<string>();
        var home = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string text) in fullTexts)
            foreach (string line in text.Split('\n'))
            {
                Match h = AnyIdHeading.Match(line);
                if (!h.Success) continue;
                string id = h.Groups["id"].Value;
                if (home.TryGetValue(id, out string? first))
                    problems.Add($"{name}: '## {id}' is also a heading in {first}. An ID names ONE rule forever, retired "
                        + "or not; a new rule takes the next unused number.");
                else home[id] = name;
            }
        // Only a prefix that heads a section makes a citation, so "[MD-11]" or "[UTF-8]" in prose is not one.
        var prefixes = home.Keys.Select(Prefix).ToHashSet(StringComparer.Ordinal);
        foreach ((string name, string text) in citingTexts)
            foreach (string id in IdCitation.Matches(text).Select(m => m.Groups["id"].Value).Distinct())
                if (prefixes.Contains(Prefix(id)) && !home.ContainsKey(id))
                    problems.Add($"{name}: cites [{id}], which no '## {id}' heading in docs/invariants defines.");
        return problems;
    }

    private static string Prefix(string id) => id[..id.LastIndexOf('-')];

    /// <summary>Rule lines, plus headings and a short preamble; the story goes under the ID in docs/invariants.</summary>
    private static List<string> RuleBodyProblems(string name, string body)
    {
        var problems = new List<string>();
        int prose = 0;
        foreach (string line in body.Split('\n'))
        {
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                CheckRuleLine(name, line, problems);
                continue;
            }
            prose += line.Length;
            if (line.Length > RuleLineMaxChars)
                problems.Add($"{name}: a {line.Length}-character line that is not a rule line. " + HowToAdd);
        }
        if (prose > RuleFileProseMaxChars)
            problems.Add($"{name}: {prose:N0} characters of text that is not a rule line, over {RuleFileProseMaxChars:N0}; "
                + "a rule file holds rule lines, headings and a short preamble. " + HowToAdd);
        return problems;
    }

    /// <summary>Checks the bytes Claude Code reads, which <see cref="Read"/> normalizes away: a front matter it fails
    /// to parse makes the file load in EVERY session.</summary>
    private static IEnumerable<string> RawFormatProblems(string name, byte[] raw)
    {
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            yield return $"{name}: starts with a UTF-8 byte-order mark before '---'; save it as UTF-8 without BOM.";
        if (Array.IndexOf(raw, (byte)'\r') >= 0)
            yield return $"{name}: has CRLF line endings; rule files are LF (see .gitattributes).";
    }

    /// <summary>Shipped code that loads no rule file while its test does. A test is paired with its code by name only:
    /// 'XTests.cs' tests 'X.cs', or 'Xs.cs' (LandingGuidanceLawTests tests LandingGuidanceLaws.cs), in the app, the
    /// updater or the vPilot plugin (never a tools/ probe or vendored example). A test named for a behaviour
    /// ('A380BaroMuteTests') or a partial ('X.Part.cs') pairs with nothing and is not checked.</summary>
    private static List<string> CodeMissingItsTestsRules(IEnumerable<string> files, Func<string, List<string>> loads)
    {
        List<string> all = files.ToList();
        ILookup<string, string> codeByName = all
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) && ShippedCodeRoots.Any(r => f.StartsWith(r, StringComparison.Ordinal)))
            .ToLookup(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (string test in all.Where(f => f.StartsWith("tests/MSFSBlindAssist.Tests/", StringComparison.Ordinal)
                     && f.EndsWith("Tests.cs", StringComparison.Ordinal)))
        {
            List<string> testLoads = loads(test);
            if (testLoads.Count == 0) continue;
            string name = Path.GetFileName(test)[..^"Tests.cs".Length];
            foreach (string code in codeByName[name].Concat(codeByName[name + "s"]))
                if (loads(code).Count == 0)
                    problems.Add($"{code} loads no rule file, but its test {test} loads {string.Join(", ", testLoads)}. "
                        + (testLoads.Count == 1 ? "Add a glob for the code to that rule file" : "Add a glob for the code to "
                            + "the one whose rules guard it")
                        + ", so whoever edits the code gets the rules whoever edits its test gets.");
        }
        return problems;
    }

    private static readonly string[] ShippedCodeRoots = { "MSFSBlindAssist/", "MSFSBlindAssistUpdater/", "plugins/" };

    /// <summary>The shared aircraft rules glob files in every aircraft's folder under Aircraft/ (every file; every
    /// *Definition*.cs), so they never stand in for an aircraft's own rule file: a ported aircraft whose rule file
    /// misses its own subfolder is still flagged.</summary>
    private static readonly HashSet<string> SharedAircraftRules = new(StringComparer.Ordinal)
        { ".claude/rules/variable-definitions.md", ".claude/rules/troubleshooting.md" };

    /// <summary>Shipped code that no rule file needs to reach, by reason: each key says why no area's rules apply to its
    /// entries. An entry ending in '/' is a folder, kept for infrastructure no area rule guards; any other entry is one
    /// file, so a new file beside it still needs a glob or an entry of its own. When a feature listed here gains a rule,
    /// give it a rule file and drop its entries. A Coherent agent script is never exempt (<see cref="IsExempt"/>).</summary>
    private static readonly Dictionary<string, string[]> CoverageExemptions = new(StringComparer.Ordinal)
    {
        ["CLAUDE.md always loads, and its rules for any file cover these: CORE-7 and CORE-8 (screen-reader output), "
            + "CORE-12 (controls), CORE-13 (the database path and its dialogs), CORE-14 and CORE-15 (logging), and VAT-13 "
            + "(settings panels; an area that owns a panel globs it as well)"] = new[]
        {
            "MSFSBlindAssist/Accessibility/", "MSFSBlindAssist/Controls/", "MSFSBlindAssist/Utils/Logging/",
            "MSFSBlindAssist/Utils/AppLogs.cs", "MSFSBlindAssist/Database/DatabasePathResolver.cs",
            "MSFSBlindAssist/Database/DatabaseSelector.cs", "MSFSBlindAssist/Forms/DatabaseSettingsForm.cs",
            "MSFSBlindAssist/Forms/DatabaseMismatchDialog.cs", "MSFSBlindAssist/Forms/Settings/",
        },
        ["app scaffolding that no feature's rules apply to"] = new[]
        {
            "MSFSBlindAssist/Program.cs", "MSFSBlindAssist/GlobalUsings.cs", "MSFSBlindAssist/SingleInstanceManager.cs",
            "MSFSBlindAssist/Properties/", "MSFSBlindAssist/Utils/RuntimeChecker.cs", "MSFSBlindAssist/Utils/SimulatorDetector.cs",
        },
        ["plain data records: the rules about their fields load with the code that reads them"] = new[]
        {
            "MSFSBlindAssist/Database/Models/Airport.cs", "MSFSBlindAssist/Database/Models/AirportCandidate.cs",
            "MSFSBlindAssist/Database/Models/DatabaseMetadata.cs", "MSFSBlindAssist/Database/Models/ILSData.cs",
            "MSFSBlindAssist/Database/Models/Runway.cs", "MSFSBlindAssist/Database/Models/StartPosition.cs",
            "MSFSBlindAssist/Database/Models/WaypointFix.cs", "MSFSBlindAssist/Models/LocationData.cs",
            "MSFSBlindAssist/Models/SimBriefOFP.cs", "MSFSBlindAssist/Models/TcasTraffic.cs",
        },
        ["generic dialogs and display controls that no feature's rules apply to"] = new[]
        {
            "MSFSBlindAssist/Forms/AboutForm.cs", "MSFSBlindAssist/Forms/ValueInputForm.cs",
            "MSFSBlindAssist/Forms/HotkeyListForm.cs", "MSFSBlindAssist/Forms/ChecklistForm.cs",
            "MSFSBlindAssist/Forms/DisplayList.cs", "MSFSBlindAssist/Forms/DisplayListBox.cs", "MSFSBlindAssist/Forms/DisplayText.cs",
        },
        ["shared by several aircraft or areas, and no rule names it"] = new[]
        {
            "MSFSBlindAssist/Forms/PMDG/", "MSFSBlindAssist/Forms/CduScratchpadAnnouncer.cs", "MSFSBlindAssist/Forms/FbwEwdWindow.cs",
            "MSFSBlindAssist/Forms/NavRadiosForm.cs", "MSFSBlindAssist/Forms/FMCSettingsForm.cs",
            "MSFSBlindAssist/Services/NdWaypointReadout.cs", "MSFSBlindAssist/Services/RelativeDirection.cs",
            "MSFSBlindAssist/Navigation/NavigationCalculator.cs", "MSFSBlindAssist/Navigation/RunwayCenterlineTracker.cs",
        },
        ["a feature with no rules or doc of its own yet (TCAS display, SimBrief planner, GeoNames, waypoint tracking, "
            + "landing rate): when it gains a rule, give it a rule file and drop its entries"] = new[]
        {
            "MSFSBlindAssist/Services/TcasService.cs", "MSFSBlindAssist/Forms/TcasForm.cs",
            "MSFSBlindAssist/Services/SimBriefService.cs", "MSFSBlindAssist/Forms/SimBriefPlannerForm.cs",
            "MSFSBlindAssist/Services/GeoNamesService.cs", "MSFSBlindAssist/Forms/LocationInfoForm.cs",
            "MSFSBlindAssist/Navigation/WaypointTracker.cs", "MSFSBlindAssist/Forms/TrackFixForm.cs",
            "MSFSBlindAssist/Forms/TrackFixForm.Designer.cs", "MSFSBlindAssist/Services/LandingRateAnnouncer.cs",
        },
        ["retired-install cleanup that no rule names"] = new[]
        {
            "MSFSBlindAssist/Patching/StaleRuntimesCleanup.cs",
        },
    };

    /// <summary>Code that ships: a .cs file in the app, the updater or the vPilot plugin, or a script or page the app
    /// injects from Resources/. Tests and tools/ are not checked here; the tested-code check covers tests.</summary>
    private static bool IsShippedCode(string file)
        => (file.EndsWith(".cs", StringComparison.Ordinal)
                && ShippedCodeRoots.Any(r => file.StartsWith(r, StringComparison.Ordinal)))
            || (file.StartsWith("MSFSBlindAssist/Resources/", StringComparison.Ordinal)
                && (file.EndsWith(".js", StringComparison.Ordinal) || file.EndsWith(".html", StringComparison.Ordinal)));

    /// <summary>Whether an exemption entry covers the file. A Coherent agent script is never exempt: the owner's choice
    /// of 2026-10-05, since its rule file is what points whoever opens the script at its doc.</summary>
    private static bool IsExempt(string file, IEnumerable<string> exemptions)
        => !IsCoherentAgentScript(file) && exemptions.Any(e => EntryCovers(e, file));

    private static bool EntryCovers(string entry, string file)
        => entry.EndsWith('/') ? file.StartsWith(entry, StringComparison.Ordinal) : file == entry;

    /// <summary>Shipped code (<see cref="IsShippedCode"/>) outside the exemptions that no rule file loads. An area-owned
    /// file (<see cref="IsAreaOwnedFile"/>) needs a rule file of its own: the shared aircraft rules do not count.</summary>
    private static List<string> ShippedFilesLoadingNoRuleFile(IEnumerable<string> files,
        IEnumerable<(string Name, List<string> Globs)> ruleFiles, IReadOnlyCollection<string> exemptions)
    {
        var compiled = ruleFiles.Select(rf => (rf.Name, Globs: rf.Globs.Select(GlobRegex).ToList())).ToList();
        var anyGlob = compiled.SelectMany(c => c.Globs).ToList();
        var ownGlob = compiled.Where(c => !SharedAircraftRules.Contains(c.Name)).SelectMany(c => c.Globs).ToList();
        return files.Where(f => IsShippedCode(f) && !IsExempt(f, exemptions))
            .Where(f => !(IsAreaOwnedFile(f) ? ownGlob : anyGlob).Any(g => g.IsMatch(f)))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    /// <summary>Exemption entries that match no file. A moved or deleted file must not leave its entry behind, just as a
    /// dead glob fails <see cref="Every_rule_file_glob_still_matches_a_file"/>.</summary>
    private static List<string> ExemptionEntriesMatchingNoFile(IEnumerable<string> files, IEnumerable<string> exemptions)
    {
        List<string> all = files.ToList();
        return exemptions.Where(e => !all.Any(f => EntryCovers(e, f))).ToList();
    }

    /// <summary>A file in an aircraft's or area's own subfolder of Aircraft/, Forms/ or SimConnect/, or a Coherent
    /// agent script: code that belongs to one area, so a rule file of its own must load with it.</summary>
    private static bool IsAreaOwnedFile(string file)
    {
        if (IsCoherentAgentScript(file)) return true;
        string[] parts = file.Split('/');
        return parts.Length >= 4 && parts[0] == "MSFSBlindAssist"
            && parts[1] is "Aircraft" or "Forms" or "SimConnect"
            && file.EndsWith(".cs", StringComparison.Ordinal);
    }

    private static bool IsCoherentAgentScript(string file)
        => file.StartsWith("MSFSBlindAssist/Resources/coherent-", StringComparison.Ordinal)
            && file.EndsWith(".js", StringComparison.Ordinal);

    /// <summary>What a rule file puts into context: its body. "Claude Code removes the frontmatter before loading the
    /// rule into context" (code.claude.com/docs/en/memory), and a Read of AppVersion.cs injected updates.md from its
    /// heading on, with no paths list (measured 2026-10-05), so the globs cost nothing. The one-line "Contents of
    /// &lt;path&gt;:" header Claude Code puts above each injected file is not counted.</summary>
    /// <summary>CLAUDE.md's headings, in full. A new aircraft or feature takes a row in "Where things live" and a rule
    /// file of its own, never a CLAUDE.md section, so a change to this list is a deliberate change to CLAUDE.md's shape.</summary>
    private static readonly string[] ClaudeMdOutline =
    {
        "# CLAUDE.md", "## Project Overview", "## Build", "## Testing", "## Before changing behaviour",
        "## Git workflow and release notes", "## Rules for any file", "### Screen reader announcements",
        "### Everywhere else", "## Multi-Aircraft Architecture", "## Quick Reference", "## Where things live",
        "## Adding or changing a rule", "## Technology Stack",
    };

    /// <summary>CLAUDE.md keeps its outline, its "Rules for any file" section holds rule lines only, and every doc it
    /// links to has a row in "Where things live". Text a branch carries over from the old CLAUDE.md after merging main
    /// fails here even when it is too short to break the size budget; git can merge a small hunk with no conflict.</summary>
    private static List<string> ClaudeMdStructureProblems(string text, IReadOnlyCollection<string> outline)
    {
        var problems = new List<string>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        var linked = new List<string>();
        string section = "";
        bool inFence = false;
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            if (inFence || line.StartsWith("```", StringComparison.Ordinal)) continue;
            if (line.StartsWith('#'))
            {
                if (!outline.Contains(line))
                    problems.Add($"CLAUDE.md: '{line}' is not one of its headings. A new aircraft or feature takes a row in "
                        + "\"Where things live\" and a rule file of its own, not a CLAUDE.md section; if CLAUDE.md's own "
                        + "outline must change, update ClaudeMdOutline in this test in the same PR.");
                if (line.StartsWith("## ", StringComparison.Ordinal)) section = line;
                continue;
            }
            if (section == "## Rules for any file" && line.StartsWith("- ", StringComparison.Ordinal) && !RuleLine.IsMatch(line))
                problems.Add($"CLAUDE.md: '{(line.Length > 60 ? line[..60] + "…" : line)}' under \"Rules for any file\" is "
                    + "not a rule line. " + HowToAdd);
            foreach (Match m in MarkdownLink.Matches(line))
            {
                string target = m.Groups["target"].Value.Split('#')[0];
                if (!target.StartsWith("docs/", StringComparison.Ordinal)) continue;
                if (section == "## Where things live" && line.StartsWith("| ", StringComparison.Ordinal)) mapped.Add(target);
                else linked.Add(target);
            }
        }
        foreach (string doc in linked.Distinct(StringComparer.Ordinal).Where(d => !mapped.Contains(d)))
            problems.Add($"CLAUDE.md links to {doc}, which has no row in \"Where things live\". Give it a row there (the doc, "
                + "when to read it, its rule files) instead of a pointer of its own.");
        return problems;
    }

    private static int LoadedChars(string ruleFileText) => SplitFrontMatter(ruleFileText).Body.Length;

    private static bool OverRuleFileBudget(string ruleFileText) => LoadedChars(ruleFileText) > RuleFileMaxChars;

    internal static bool GlobMatches(string glob, string relativePath) => GlobRegex(glob).IsMatch(relativePath);

    internal static Regex GlobRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/') { i++; sb.Append("(?:[^/]+/)*"); }
                else sb.Append(".*");
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return new Regex(sb.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    internal sealed record RuleFile(string Path, string Name, string Text, string Body, List<string>? Globs,
        List<string> PathItems);

    internal static IEnumerable<RuleFile> RuleFiles()
    {
        string root = RepoRoot();
        string dir = Path.Combine(root, ".claude", "rules");
        if (!Directory.Exists(dir)) yield break;
        foreach (string path in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = Read(path);
            (List<string>? globs, string body, List<string> items) = SplitFrontMatter(text);
            yield return new RuleFile(path, Rel(root, path), text, body, globs, items);
        }
    }

    /// <summary>The globs, the body after the front matter, and the raw paths item lines (for the quoting check).</summary>
    internal static (List<string>? Globs, string Body, List<string> Items) SplitFrontMatter(string text)
    {
        var items = new List<string>();
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (null, text, items);
        int end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        if (end < 0) return (null, text, items);
        var globs = new List<string>();
        bool inPaths = false;
        foreach (string raw in (end > 4 ? text[4..end] : "").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("paths:", StringComparison.Ordinal)) { inPaths = true; continue; }
            string trimmed = line.TrimStart();
            if (inPaths && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                items.Add(line);
                globs.Add(trimmed[2..].Trim().Trim('"', '\''));
            }
            else if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                inPaths = false;
        }
        return (globs, text[(end + 5)..], items);
    }

    private static IEnumerable<string> InvariantFiles()
    {
        string dir = Path.Combine(RepoRoot(), "docs", "invariants");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.md").OrderBy(p => p, StringComparer.Ordinal)
            : Enumerable.Empty<string>();
    }

    /// <summary>Every file in the repository, as a '/'-separated path from the root, skipping build
    /// output, VCS internals and the worktrees Claude Code keeps under .claude/worktrees. It walks the working
    /// tree, as the suite's other source scans do, not git's index: an untracked file counts locally and not in
    /// CI, so a local run can differ from CI's clean checkout, which is the one that gates a merge.</summary>
    internal static IEnumerable<string> RepoFiles()
    {
        string root = RepoRoot();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                if (PrunedDirectories.Contains(name)) continue;
                if (name == "worktrees" && Path.GetFileName(dir) == ".claude") continue;
                stack.Push(sub);
            }
            foreach (string f in Directory.EnumerateFiles(dir))
                yield return Rel(root, f);
        }
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
