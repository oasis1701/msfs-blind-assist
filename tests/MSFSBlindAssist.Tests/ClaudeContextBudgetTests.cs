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
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Afdx.cs", ".claude/rules/a220.md", "MSFSBlindAssist/Aircraft/A220/**", false)]
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Afdx.cs", ".claude/rules/a220.md", "MSFSBlindAssist/Forms/A220/**", true)]
    [InlineData("MSFSBlindAssist/Aircraft/A220/A220Afdx.cs", ".claude/rules/variable-definitions.md", "MSFSBlindAssist/Aircraft/**", true)]
    public void An_aircraft_folder_file_needs_a_rule_file_of_its_own(string file, string ruleFile, string glob, bool flagged)
        => Assert.Equal(flagged, AreaFilesLoadingNoOwnRuleFile(new[] { file },
            new[] { (ruleFile, new List<string> { glob }) }).Count > 0);

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
    public void Every_aircraft_folder_file_and_coherent_agent_loads_a_rule_file()
    {
        List<string> problems = AreaFilesLoadingNoOwnRuleFile(RepoFiles(),
                RuleFiles().Select(rf => (rf.Name, rf.Globs ?? new List<string>())))
            .Select(file => $"{file} loads no rule file of its own (the shared aircraft rules in variable-definitions.md "
                + "do not count), so no area rule reaches whoever edits it. Add a glob for it to its "
                + "area's .claude/rules file. A new aircraft or feature with no rules yet gets a rule file of its own "
                + "whose preamble names its doc (CLAUDE.md, \"Adding or changing a rule\"); only a shared folder that "
                + "no rule guards goes in AreaFolderExemptions, with the reason.")
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Only_the_shared_aircraft_rules_glob_all_of_Aircraft()
    {
        const string anyNewAircraftFile = "MSFSBlindAssist/Aircraft/NewAircraft/NewAircraftDefinition.cs";
        List<RuleFile> all = RuleFiles().ToList();
        RuleFile? shared = all.FirstOrDefault(rf => rf.Name == SharedAircraftRules);
        var problems = new List<string>();
        if (shared?.Globs?.Contains("MSFSBlindAssist/Aircraft/**") != true)
            problems.Add($"{SharedAircraftRules} must exist and glob \"MSFSBlindAssist/Aircraft/**\": the area-folder check "
                + "ignores it by that name. If it moved or was renamed, update SharedAircraftRules.");
        foreach (RuleFile rf in all.Where(rf => rf.Name != SharedAircraftRules))
            if ((rf.Globs ?? new List<string>()).Any(g => GlobMatches(g, anyNewAircraftFile)))
                problems.Add($"{rf.Name} globs every aircraft's folder, so the area-folder check would count it as any new "
                    + "aircraft's own rule file. Scope it to the aircraft it guards, or add it to SharedAircraftRules' "
                    + "exclusion in AreaFilesLoadingNoOwnRuleFile.");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

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

    /// <summary>The shared aircraft rules glob every file under Aircraft/, so they never stand in for an aircraft's own
    /// rule file: a ported aircraft whose rule file misses its own subfolder is still flagged.</summary>
    private const string SharedAircraftRules = ".claude/rules/variable-definitions.md";

    /// <summary>Area-owned files (<see cref="IsAreaOwnedFile"/>) that no rule file of their own matches.</summary>
    private static List<string> AreaFilesLoadingNoOwnRuleFile(IEnumerable<string> files,
        IEnumerable<(string Name, List<string> Globs)> ruleFiles)
    {
        var compiled = ruleFiles.Where(rf => rf.Name != SharedAircraftRules)
            .SelectMany(rf => rf.Globs).Select(GlobRegex).ToList();
        return files.Where(IsAreaOwnedFile).Where(f => !compiled.Any(g => g.IsMatch(f)))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    /// <summary>Folders that belong to one aircraft or area but that no rule guards, each with the reason.</summary>
    private static readonly Dictionary<string, string> AreaFolderExemptions = new(StringComparer.Ordinal)
    {
        ["MSFSBlindAssist/Forms/PMDG/"] = "the autopilot window both PMDG aircraft share; no rule names it",
        ["MSFSBlindAssist/Forms/Settings/"] = "app-wide settings panels, not one area: VAT-13 in CLAUDE.md covers them all, "
            + "and an area that owns a panel globs it in its own rule file",
    };

    /// <summary>A file in an aircraft's or area's own subfolder of Aircraft/, Forms/ or SimConnect/, or a Coherent
    /// agent script: code that belongs to one area, so some rule file must load with it.</summary>
    private static bool IsAreaOwnedFile(string file)
    {
        if (file.StartsWith("MSFSBlindAssist/Resources/coherent-", StringComparison.Ordinal)
            && file.EndsWith(".js", StringComparison.Ordinal))
            return true;
        string[] parts = file.Split('/');
        return parts.Length >= 4 && parts[0] == "MSFSBlindAssist"
            && parts[1] is "Aircraft" or "Forms" or "SimConnect"
            && file.EndsWith(".cs", StringComparison.Ordinal)
            && !AreaFolderExemptions.Keys.Any(k => file.StartsWith(k, StringComparison.Ordinal));
    }

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

    /// <summary>What a rule file puts into context: its body. "Claude Code removes the frontmatter before loading the
    /// rule into context" (code.claude.com/docs/en/memory), and a Read of AppVersion.cs injected updates.md from its
    /// heading on, with no paths list (measured 2026-10-05), so the globs cost nothing. The one-line "Contents of
    /// &lt;path&gt;:" header Claude Code puts above each injected file is not counted.</summary>
    private static int LoadedChars(string ruleFileText) => SplitFrontMatter(ruleFileText).Body.Length;

    private static bool OverRuleFileBudget(string ruleFileText) => LoadedChars(ruleFileText) > RuleFileMaxChars;

    internal static bool GlobMatches(string glob, string relativePath) => GlobRegex(glob).IsMatch(relativePath);

    private static Regex GlobRegex(string glob)
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

    private sealed record RuleFile(string Path, string Name, string Text, string Body, List<string>? Globs,
        List<string> PathItems);

    private static IEnumerable<RuleFile> RuleFiles()
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
    private static (List<string>? Globs, string Body, List<string> Items) SplitFrontMatter(string text)
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
    private static IEnumerable<string> RepoFiles()
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

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
