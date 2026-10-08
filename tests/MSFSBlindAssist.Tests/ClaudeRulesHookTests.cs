using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Runs .claude/hooks/rules-hook.ps1 the way Claude Code does (hook input as JSON on stdin, hook output as JSON on
/// stdout) and checks what it adds or refuses. The hook brings area rules where Claude Code's own path-scoped loading
/// does not reach (CORE-16), and its glob matching must stay identical to ClaudeContextBudgetTests' (CCT-2).
/// </summary>
public class ClaudeRulesHookTests
{
    private sealed record HookRun(int ExitCode, string Stdout, string Stderr);

    private static string ScriptPath =>
        Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "hooks", "rules-hook.ps1");

    [Fact]
    public void For_lists_the_rule_files_a_path_loads()
    {
        HookRun run = RunHook(new[] { "for", "MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs" });
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs: .claude/rules/pmdg-737.md, "
            + ".claude/rules/variable-definitions.md (", run.Stdout);
    }

    [Fact]
    public void For_says_when_no_rule_file_covers_a_path()
    {
        HookRun run = RunHook(new[] { "for", "changelog.d/README.md" });
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("changelog.d/README.md: no rule files", run.Stdout);
    }

    [Fact]
    public void For_machine_output_matches_the_guard_matcher_for_every_repo_file()
    {
        List<string> files = ClaudeContextBudgetTests.RepoFiles().ToList();
        var ruleFiles = ClaudeContextBudgetTests.RuleFiles()
            .Select(rf => (rf.Name, Globs: (rf.Globs ?? new List<string>()).Select(ClaudeContextBudgetTests.GlobRegex).ToList()))
            .ToList();

        HookRun run = RunHook(new[] { "for", "-Machine", "-Stdin" }, stdin: string.Join("\n", files) + "\n");

        Assert.Equal(0, run.ExitCode);
        var hook = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.TrimEnd('\r').Split('\t');
            hook[parts[0]] = parts.Length > 1 ? parts[1] : "";
        }
        var mismatches = new List<string>();
        foreach (string file in files)
        {
            string guard = string.Join(";", ruleFiles.Where(r => r.Globs.Any(g => g.IsMatch(file)))
                .Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal));
            string got = hook.TryGetValue(file, out string? names) ? names : "<missing>";
            if (got != guard) mismatches.Add($"{file}: hook '{got}', guard '{guard}'");
        }
        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} of {files.Count} paths differ:\n" + string.Join("\n", mismatches.Take(20)));
    }

    [Fact]
    public void Read_adds_the_rule_files_for_a_file_in_an_agent_worktree()
    {
        string worktree = CreateAgentWorktree(NewTempDir());
        string file = CreateFile(worktree, "MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs");

        JsonElement? output = HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1")));

        Assert.NotNull(output);
        Assert.Equal("PostToolUse", output.Value.GetProperty("hookEventName").GetString());
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.StartsWith("Area rules for MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs. Claude Code does not load",
            context);
        Assert.Contains("Contents of " + Path.Combine(worktree, ".claude", "rules", "pmdg-737.md") + ":", context);
        Assert.Contains(RuleBody("pmdg-737.md"), context);
        Assert.Contains(RuleBody("variable-definitions.md"), context);   // holds non-ASCII text: checks the encoding
    }

    [Fact]
    public void Read_adds_each_rule_file_once_per_subagent()
    {
        string worktree = CreateAgentWorktree(NewTempDir());
        string file = CreateFile(worktree, "MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs");
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };

        Assert.NotNull(HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1"), env: env)));
        Assert.Null(HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1"), env: env)));
        Assert.NotNull(HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a2"), env: env)));
    }

    [Fact]
    public void Read_adds_nothing_in_the_main_conversation()
    {
        string worktree = CreateAgentWorktree(NewTempDir());
        string file = CreateFile(worktree, "MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs");

        HookRun run = RunHook(new[] { "read" }, ReadInput(file, agentId: null));

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void Read_adds_nothing_outside_an_agent_worktree()
    {
        string file = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "Pmdg737DisplayReads.cs");

        Assert.Null(HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1"))));
    }

    [Fact]
    public void Read_matches_worktree_folders_whatever_their_case()
    {
        string temp = NewTempDir();
        CreateAgentWorktree(temp);
        CreateFile(Path.Combine(temp, ".claude", "worktrees", "agent-test"), "MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs");
        string file = Path.Combine(temp, ".Claude", "Worktrees", "Agent-test", "MSFSBlindAssist", "Aircraft",
            "Pmdg737DisplayReads.cs");

        JsonElement? output = HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1")));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("pmdg-737.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Read_adds_nothing_for_a_file_outside_any_checkout()
    {
        // A .claude\rules folder with no .git beside it (a home folder's user-level rules) is not a checkout, even when
        // its rule matches everything.
        string temp = NewTempDir();
        CreateFile(temp, ".claude/rules/catch-all.md", "---\npaths:\n  - \"**\"\n---\n# Catch-all\n- [X-1] r\n");
        string file = CreateFile(temp, ".claude/worktrees/agent-x/a.cs");

        HookRun run = RunHook(new[] { "read" }, ReadInput(file, agentId: "a1"));

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void Write_of_a_new_file_adds_its_rules()
    {
        string worktree = CreateAgentWorktree(NewTempDir());
        string file = Path.Combine(worktree, "MSFSBlindAssist", "Aircraft", "Pmdg737NewFile.cs");   // not on disk yet

        JsonElement? output = HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1", tool: "Write")));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("pmdg-737.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Read_is_silent_when_switched_off()
    {
        string worktree = CreateAgentWorktree(NewTempDir());
        string file = CreateFile(worktree, "MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs");

        HookRun run = RunHook(new[] { "read" }, ReadInput(file, agentId: "a1"),
            env: new Dictionary<string, string?> { ["MSFSBA_RULES_HOOK"] = "off" });

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Theory]
    [InlineData("read")]
    [InlineData("shell-guard")]
    [InlineData("diff")]
    [InlineData("subagent-start")]
    [InlineData("session-start")]
    public void A_malformed_hook_input_is_ignored(string mode)
    {
        foreach (string stdin in new[] { "not json", "{}" })
        {
            HookRun run = RunHook(new[] { mode }, stdin);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("", run.Stdout);
        }
    }

    private const string Pmdg737 = "MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs";

    [Theory]
    [InlineData("Bash", "sed -i 's/a/b/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "cd MSFSBlindAssist && sed -i 's/a/b/' Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "sed -i 's/a|b/c/;s/d/e/' \"MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs\"", Pmdg737)]
    [InlineData("Bash", "cat > MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs <<'EOF'\nclass X {}\nEOF", Pmdg737)]
    [InlineData("Bash", "printf 'x' >> tests/MSFSBlindAssist.Tests/Pmdg737ProbeTests.cs",
        "tests/MSFSBlindAssist.Tests/Pmdg737ProbeTests.cs")]
    [InlineData("Bash", "echo x | tee -a MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "perl -pi -e 's/a/b/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("PowerShell", "Set-Content -Path MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs -Value x", Pmdg737)]
    [InlineData("PowerShell", "'x' | Out-File MSFSBlindAssist\\Aircraft\\Pmdg737DisplayReads.cs", Pmdg737)]
    public void Shell_guard_refuses_writes_to_covered_files(string tool, string command, string target)
    {
        JsonElement? output = HookOutput(RunHook(new[] { "shell-guard" }, ShellInput(tool, command)));

        Assert.NotNull(output);
        Assert.Equal("deny", output.Value.GetProperty("permissionDecision").GetString());
        string reason = output.Value.GetProperty("permissionDecisionReason").GetString()!;
        Assert.Contains(target, reason);
        Assert.Contains(".claude/rules/pmdg-737.md", reason);
        Assert.Contains("(CORE-16)", reason);
    }

    [Fact]
    public void Shell_guard_resolves_git_bash_paths()
    {
        string root = ClaudeContextBudgetTests.RepoRoot();
        string gitBashRoot = "/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/');

        JsonElement? output = HookOutput(RunHook(new[] { "shell-guard" },
            ShellInput("Bash", $"sed -i 's/a/b/' {gitBashRoot}/{Pmdg737}")));

        Assert.NotNull(output);
        Assert.Contains(Pmdg737, output.Value.GetProperty("permissionDecisionReason").GetString());
    }

    [Theory]
    [InlineData("Bash", "sed -n '1,5p' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("Bash", "cat MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("Bash", "cat > changelog.d/999-x.fix.md <<'EOF'\nList<string> x => y > z\nEOF")]
    [InlineData("Bash", "echo hi > /dev/null")]
    [InlineData("Bash", "cat x.txt && grep \">\" MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("Bash", "git diff > \"C:/Temp/My Folder/x.patch\"")]
    [InlineData("Bash", "echo $HOME > $TMPFILE")]
    [InlineData("Bash", "tee -a changelog.d/999-x.fix.md < MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("PowerShell", "Set-Content -Path $env:TEMP\\x.txt -Value x")]
    [InlineData("PowerShell", "Set-Content -Value MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs changelog.d/999-x.fix.md")]
    [InlineData("Bash", "sed -i 's/a/b/ unterminated")]
    public void Shell_guard_allows_commands_that_write_no_covered_file(string tool, string command)
    {
        HookRun run = RunHook(new[] { "shell-guard" }, ShellInput(tool, command));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Stdout);
    }

    private const string TaxiDiff =
        "diff --git a/MSFSBlindAssist/Navigation/TaxiGraph.cs b/MSFSBlindAssist/Navigation/TaxiGraph.cs\n"
        + "index 1111111..2222222 100644\n--- a/MSFSBlindAssist/Navigation/TaxiGraph.cs\n"
        + "+++ b/MSFSBlindAssist/Navigation/TaxiGraph.cs\n@@ -1 +1 @@\n-a\n+b\n";

    [Fact]
    public void Diff_adds_the_rule_files_for_a_changed_path()
    {
        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse(TaxiDiff))));

        Assert.NotNull(output);
        Assert.Equal("PostToolUse", output.Value.GetProperty("hookEventName").GetString());
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.StartsWith("Area rules for the files in this diff.", context);
        Assert.Contains(RuleBody("taxi-routing.md"), context);   // holds non-ASCII text: checks the encoding
        Assert.Contains("Contents of " + Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "rules", "runway-holds.md")
            + ":", context);
    }

    [Fact]
    public void Diff_reads_name_only_output()
    {
        JsonElement? output = HookOutput(RunHook(new[] { "diff" },
            DiffInput("git diff --name-only", BashResponse("MSFSBlindAssist/Navigation/TaxiGraph.cs\n"))));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("taxi-routing.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Diff_accepts_a_plain_string_tool_response()
    {
        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", TaxiDiff)));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("taxi-routing.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Diff_uses_the_folder_named_by_git_dash_C()
    {
        string root = ClaudeContextBudgetTests.RepoRoot();

        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput($"git -C \"{root}\" diff --name-only",
            BashResponse("MSFSBlindAssist/Navigation/TaxiGraph.cs\n"), cwd: NewTempDir())));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("taxi-routing.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Diff_stops_at_the_cap_and_names_the_rest()
    {
        // TaxiGraph.cs and FlyByWireA380Definition.Rmp.cs load disjoint rule sets, 24,216 + 26,412 characters
        // (measured 2026-10-08): more than the 40,000-character cap.
        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff --name-only", BashResponse(
            "MSFSBlindAssist/Navigation/TaxiGraph.cs\nMSFSBlindAssist/Aircraft/FlyByWireA380Definition.Rmp.cs\n"))));

        Assert.NotNull(output);
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.Contains("Not added (over the 40,000-character cap): .claude/rules/", context);
        int added = System.Text.RegularExpressions.Regex.Matches(context, @"Contents of (?<path>[^\n]+?\.md):")
            .Sum(m => RuleBody(Path.GetFileName(m.Groups["path"].Value)).Length);
        Assert.InRange(added, 1, 40_000);
    }

    [Fact]
    public void Diff_adds_nothing_new_on_a_repeat()
    {
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };

        Assert.NotNull(HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse(TaxiDiff)), env: env)));
        Assert.Null(HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse(TaxiDiff)), env: env)));
    }

    [Fact]
    public void Diff_ignores_output_without_paths()
    {
        HookRun run = RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse("")));

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void Plan_agent_starts_with_CLAUDE_md()
    {
        string root = ClaudeContextBudgetTests.RepoRoot();

        JsonElement? output = HookOutput(RunHook(new[] { "subagent-start" }, SubagentInput("Plan", root),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root }));

        Assert.NotNull(output);
        Assert.Equal("SubagentStart", output.Value.GetProperty("hookEventName").GetString());
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.StartsWith("CLAUDE.md (the built-in Plan agent skips it;", context);
        Assert.Contains(File.ReadAllText(Path.Combine(root, "CLAUDE.md")).Replace("\r\n", "\n"), context);
    }

    [Fact]
    public void Other_agents_start_with_nothing_added()
    {
        string root = ClaudeContextBudgetTests.RepoRoot();

        HookRun run = RunHook(new[] { "subagent-start" }, SubagentInput("general-purpose", root),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root });

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void A_subagent_in_an_unrecognised_worktree_is_told_to_load_its_rules()
    {
        string cwd = Path.Combine(NewTempDir(), ".claude", "worktrees", "review-x");

        JsonElement? output = HookOutput(RunHook(new[] { "subagent-start" }, SubagentInput("general-purpose", cwd),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = ClaudeContextBudgetTests.RepoRoot() }));

        Assert.NotNull(output);
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.Contains("review-x", context);
        Assert.Contains("rules-hook.ps1 for <path>", context);
    }

    [Fact]
    public void A_subagent_in_the_sessions_own_worktree_needs_no_instruction()
    {
        // A desktop-app session runs in its own .claude\worktrees\<name>, and its normal subagents start there too:
        // Claude Code loads their rules, so they need no instruction.
        string worktree = Path.Combine(NewTempDir(), ".claude", "worktrees", "review-x");

        HookRun run = RunHook(new[] { "subagent-start" }, SubagentInput("general-purpose", worktree),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = worktree });

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void An_agent_worktree_needs_no_instruction()
    {
        string cwd = Path.Combine(NewTempDir(), ".claude", "worktrees", "agent-abc");

        Assert.Null(HookOutput(RunHook(new[] { "subagent-start" }, SubagentInput("general-purpose", cwd))));
    }

    [Fact]
    public void Compaction_lets_rules_be_added_again()
    {
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };
        string diff = DiffInput("git diff", BashResponse(TaxiDiff));

        Assert.NotNull(HookOutput(RunHook(new[] { "diff" }, diff, env: env)));
        Assert.Null(HookOutput(RunHook(new[] { "diff" }, diff, env: env)));
        HookRun compacted = RunHook(new[] { "session-start" },
            HookInput(new { session_id = "s1", hook_event_name = "SessionStart", source = "compact" }), env: env);
        Assert.Equal("", compacted.Stdout);
        JsonElement? again = HookOutput(RunHook(new[] { "diff" }, diff, env: env));
        Assert.NotNull(again);
        Assert.Contains(RuleBody("taxi-routing.md"), again.Value.GetProperty("additionalContext").GetString());
    }

    [Theory]
    [InlineData("read")]
    [InlineData("shell-guard")]
    [InlineData("diff")]
    [InlineData("subagent-start")]
    public void Every_hook_mode_is_silent_when_switched_off(string mode)
    {
        string root = ClaudeContextBudgetTests.RepoRoot();
        string input = mode switch
        {
            "read" => ReadInput(CreateFile(CreateAgentWorktree(NewTempDir()), Pmdg737), agentId: "a1"),
            "shell-guard" => ShellInput("Bash", $"sed -i 's/a/b/' {Pmdg737}"),
            "diff" => DiffInput("git diff", BashResponse(TaxiDiff)),
            _ => SubagentInput("Plan", root),
        };

        HookRun on = RunHook(new[] { mode }, input, env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root });
        HookRun off = RunHook(new[] { mode }, input,
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root, ["MSFSBA_RULES_HOOK"] = "off" });

        Assert.NotEqual("", on.Stdout);
        Assert.Equal(0, off.ExitCode);
        Assert.Equal("", off.Stdout);
    }

    // ---- inputs and fixtures ----

    private static string SubagentInput(string agentType, string cwd) => HookInput(new
    {
        session_id = "s1", hook_event_name = "SubagentStart", agent_id = "p1", agent_type = agentType, cwd,
    });

    private static object BashResponse(string stdout) =>
        new { stdout, stderr = "", interrupted = false, isImage = false, noOutputExpected = false };

    private static string DiffInput(string command, object toolResponse, string? cwd = null) => HookInput(new
    {
        session_id = "s1", hook_event_name = "PostToolUse", tool_name = "Bash", tool_input = new { command },
        tool_response = toolResponse, cwd = cwd ?? ClaudeContextBudgetTests.RepoRoot(),
    });

    private static string ShellInput(string tool, string command) => HookInput(new
    {
        session_id = "s1", hook_event_name = "PreToolUse", tool_name = tool, tool_input = new { command },
        cwd = ClaudeContextBudgetTests.RepoRoot(),
    });

    private static string ReadInput(string file, string? agentId, string tool = "Read") => agentId is null
        ? HookInput(new { session_id = "s1", hook_event_name = "PostToolUse", tool_name = tool, tool_input = new { file_path = file } })
        : HookInput(new
        {
            session_id = "s1", agent_id = agentId, agent_type = "general-purpose", hook_event_name = "PostToolUse",
            tool_name = tool, tool_input = new { file_path = file },
        });

    /// <summary>A folder laid out like Claude Code's worktree for an isolated subagent: a .git file and a copy of the
    /// repository's rule files.</summary>
    private static string CreateAgentWorktree(string tempDir, string name = "agent-test")
    {
        string worktree = Path.Combine(tempDir, ".claude", "worktrees", name);
        string rules = Path.Combine(worktree, ".claude", "rules");
        Directory.CreateDirectory(rules);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: elsewhere\n");
        foreach (string rule in Directory.GetFiles(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "rules"), "*.md"))
            File.Copy(rule, Path.Combine(rules, Path.GetFileName(rule)));
        return worktree;
    }

    private static string CreateFile(string root, string relativePath, string content = "")
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A rule file's body as the guard test reads it, independently of the script under test.</summary>
    private static string RuleBody(string ruleFile) => ClaudeContextBudgetTests.SplitFrontMatter(
        File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "rules", ruleFile)).Replace("\r\n", "\n")).Body;

    // ---- harness ----

    /// <summary>Runs the hook script as Claude Code does. TEMP/TMP point at a fresh folder per call (the hook keeps
    /// its memory of added rule files there) and the MSFSBA_RULES_HOOK switch is cleared, unless <paramref name="env"/>
    /// sets them; a null value in <paramref name="env"/> removes that variable.</summary>
    private static HookRun RunHook(string[] args, string? stdin = null, string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = workingDirectory ?? ClaudeContextBudgetTests.RepoRoot(),
        };
        foreach (string a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ScriptPath })
            psi.ArgumentList.Add(a);
        foreach (string a in args) psi.ArgumentList.Add(a);
        string temp = NewTempDir();
        psi.Environment["TEMP"] = temp;
        psi.Environment["TMP"] = temp;
        psi.Environment.Remove("MSFSBA_RULES_HOOK");
        if (env != null)
            foreach ((string key, string? value) in env)
                if (value is null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;

        using Process process = Process.Start(psi)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (stdin != null) process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("rules-hook.ps1 did not exit within 30 seconds.");
        }
        return new HookRun(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string HookInput(object input) => JsonSerializer.Serialize(input);

    /// <summary>The hook's hookSpecificOutput, or null when it printed nothing (it added and refused nothing).</summary>
    private static JsonElement? HookOutput(HookRun run)
    {
        if (string.IsNullOrWhiteSpace(run.Stdout)) return null;
        using JsonDocument doc = JsonDocument.Parse(run.Stdout);
        return doc.RootElement.GetProperty("hookSpecificOutput").Clone();
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "msfsba-hook-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
