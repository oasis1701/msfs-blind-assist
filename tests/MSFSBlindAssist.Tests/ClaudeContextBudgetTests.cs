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
    public const int PerFileLoadMaxChars = 30_000;

    private const string HowToAdd =
        "A rule is ONE line in its area's .claude/rules/<area>.md file; its explanation, measurements and history go under "
        + "'## <ID>' in docs/invariants/<area>.md. See \"Adding or changing a rule\" in CLAUDE.md.";

    private static readonly Regex IdStart = new(@"^- \[[A-Z][A-Z0-9]*-\d+\]", RegexOptions.CultureInvariant);
    private static readonly Regex RuleLine = new(
        @"^- \[(?<id>[A-Z][A-Z0-9]*-\d+)\] (?<text>\S.*?) Full: (?<file>docs/invariants/[a-z0-9-]+\.md)#(?<anchor>[a-z0-9-]+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex IdHeading = new(@"^## (?<id>[A-Z][A-Z0-9]*-\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLink = new(@"\]\((?<target>[^)\s]+)\)", RegexOptions.CultureInvariant);
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
    public void Every_rule_file_is_scoped_well_formed_and_within_budget()
    {
        var problems = new List<string>();
        foreach (RuleFile rf in RuleFiles())
        {
            if (rf.Globs is null || rf.Globs.Count == 0)
                problems.Add($"{rf.Name}: no 'paths:' front matter. A rule file without paths loads in EVERY session; "
                    + "scope it to the code it guards, or, if it truly applies to any file, move it into CLAUDE.md.");
            foreach (string g in rf.Globs ?? new List<string>())
                if (g.Contains('{') || g.Contains('['))
                    problems.Add($"{rf.Name}: glob '{g}' uses braces or brackets; list each pattern separately.");
            if (rf.Text.Length > RuleFileMaxChars)
                problems.Add($"{rf.Name}: {rf.Text.Length:N0} characters, over {RuleFileMaxChars:N0}. Split the area into "
                    + "two rule files with narrower paths, or shorten its lines.");
            foreach (string line in rf.Body.Split('\n'))
                if (line.StartsWith("- ", StringComparison.Ordinal))
                    CheckRuleLine(rf.Name, line, problems);
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
    public void Every_rule_points_at_its_full_text_and_every_full_text_has_a_rule()
    {
        string root = RepoRoot();
        var problems = new List<string>();
        var rules = new Dictionary<string, string>(StringComparer.Ordinal);   // id -> full-text file
        IEnumerable<(string Name, string Line)> lines = RuleFiles()
            .SelectMany(rf => rf.Body.Split('\n').Select(l => (rf.Name, l)))
            .Concat(Read(Path.Combine(root, "CLAUDE.md")).Split('\n').Select(l => ("CLAUDE.md", l)));
        foreach ((string name, string line) in lines)
        {
            Match m = RuleLine.Match(line);
            if (!m.Success) continue;
            string id = m.Groups["id"].Value, file = m.Groups["file"].Value;
            if (!rules.TryAdd(id, file))
                problems.Add($"{name}: [{id}] is used twice. IDs are never reused; take the next unused number.");
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
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_single_code_file_loads_more_rules_than_the_budget()
    {
        string root = RepoRoot();
        var compiled = RuleFiles().Select(rf => (rf, Globs: (rf.Globs ?? new List<string>()).Select(GlobRegex).ToList())).ToList();
        var problems = new List<string>();
        foreach (string file in RepoFiles().Where(f =>
                     (f.StartsWith("MSFSBlindAssist/", StringComparison.Ordinal) || f.StartsWith("tests/", StringComparison.Ordinal))
                     && (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal))))
        {
            var loaded = compiled.Where(c => c.Globs.Any(g => g.IsMatch(file))).Select(c => c.rf).ToList();
            int total = loaded.Sum(rf => rf.Text.Length);
            if (total > PerFileLoadMaxChars)
                problems.Add($"{file} loads {total:N0} characters of rules ({string.Join(", ", loaded.Select(r => r.Name))}); "
                    + $"the budget is {PerFileLoadMaxChars:N0}. Narrow a glob so fewer areas claim this file, or shorten lines.");
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

    private sealed record RuleFile(string Path, string Name, string Text, string Body, List<string>? Globs);

    private static IEnumerable<RuleFile> RuleFiles()
    {
        string root = RepoRoot();
        string dir = Path.Combine(root, ".claude", "rules");
        if (!Directory.Exists(dir)) yield break;
        foreach (string path in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = Read(path);
            (List<string>? globs, string body) = SplitFrontMatter(text);
            yield return new RuleFile(path, Rel(root, path), text, body, globs);
        }
    }

    private static (List<string>? Globs, string Body) SplitFrontMatter(string text)
    {
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (null, text);
        int end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        if (end < 0) return (null, text);
        var globs = new List<string>();
        bool inPaths = false;
        foreach (string raw in text[4..end].Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("paths:", StringComparison.Ordinal)) { inPaths = true; continue; }
            string trimmed = line.TrimStart();
            if (inPaths && trimmed.StartsWith("- ", StringComparison.Ordinal))
                globs.Add(trimmed[2..].Trim().Trim('"', '\''));
            else if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                inPaths = false;
        }
        return (globs, text[(end + 5)..]);
    }

    private static IEnumerable<string> InvariantFiles()
    {
        string dir = Path.Combine(RepoRoot(), "docs", "invariants");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.md").OrderBy(p => p, StringComparer.Ordinal)
            : Enumerable.Empty<string>();
    }

    /// <summary>Every file in the repository, as a '/'-separated path from the root, skipping build
    /// output, VCS internals and the worktrees Claude Code keeps under .claude/worktrees.</summary>
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
