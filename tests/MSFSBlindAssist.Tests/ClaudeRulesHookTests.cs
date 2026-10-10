using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Runs .claude/hooks/rules-hook.ps1 the way Claude Code does (hook input as JSON on stdin, hook output as JSON on
/// stdout) and checks what it adds or refuses. The hook brings area rules where Claude Code's own path-scoped loading
/// does not reach (CORE-16), and its glob matching must stay identical to ClaudeContextBudgetTests' (CCT-2).
/// </summary>
public class ClaudeRulesHookTests : IDisposable
{
    private sealed record HookRun(int ExitCode, string Stdout, string Stderr);

    /// <summary>Temp folders this class made; xUnit disposes the class after each test, which deletes them (each test
    /// copies every rule file, so a full run would otherwise leave tens of megabytes in %TEMP%).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> TempDirs = new();

    public void Dispose()
    {
        while (TempDirs.TryTake(out string? dir))
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string ScriptPath =>
        Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "hooks", "rules-hook.ps1");

    [Fact]
    public void For_lists_the_rule_files_a_path_loads()
    {
        HookRun run = RunHook(new[] { "for", ShownInFull });
        Assert.Equal(0, run.ExitCode);
        Assert.Contains(ShownInFull + ": .claude/rules/ifly-737.md, .claude/rules/variable-definitions.md (", run.Stdout);
    }

    [Fact]
    public void For_says_when_no_rule_file_covers_a_path()
    {
        HookRun run = RunHook(new[] { "for", "changelog.d/README.md" });
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("changelog.d/README.md: no rule files", run.Stdout);
    }

    [Fact]
    public void For_lists_rule_files_in_ordinal_order_not_directory_order()
    {
        // NTFS lists a-x.md before B-x.md (case-insensitive), ordinal order puts B-x.md first. Windows PowerShell 5.1
        // binds [Array]::Sort($keys, $values, ...) to the generic overload and sorts a converted copy of $values, so
        // without the [Array] casts the keys are sorted and the rule files keep the directory's order.
        string checkout = NewTempDir();
        CreateFile(checkout, ".git", "gitdir: elsewhere\n");
        CreateFile(checkout, ".claude/rules/a-x.md", "---\npaths:\n  - \"src/x.cs\"\n---\n# A\n- [A-1] r\n");
        CreateFile(checkout, ".claude/rules/B-x.md", "---\npaths:\n  - \"src/x.cs\"\n---\n# B\n- [B-1] r\n");

        HookRun run = RunHook(new[] { "for", "src/x.cs" }, workingDirectory: checkout);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("src/x.cs: .claude/rules/B-x.md, .claude/rules/a-x.md (", run.Stdout);
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
        string file = CreateFile(worktree, ShownInFull);

        JsonElement? output = HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1")));

        Assert.NotNull(output);
        Assert.Equal("PostToolUse", output.Value.GetProperty("hookEventName").GetString());
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.StartsWith("Area rules for " + ShownInFull + ". Claude Code does not load", context);
        Assert.Contains("Contents of " + Path.Combine(worktree, ".claude", "rules", "ifly-737.md") + ":", context);
        Assert.Contains(RuleBody("ifly-737.md"), context);
        Assert.Contains(RuleBody("variable-definitions.md"), context);   // holds non-ASCII text: checks the encoding
    }

    [Theory]
    [InlineData("MSFSBlindAssist/Navigation/TaxiGraph.cs", "gsx-stands-docking.md;landing-exits.md;rollout-geometry.md;"
        + "runway-holds-call-sites.md;stand-naming-call-sites.md;taxi-graph.md;taxi-position.md;taxi-routing.md")]
    [InlineData("MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Rmp.cs",
        "a380-coherent.md;a380-fcu.md;a380-systems.md;fbw-arinc.md;tcas-ra.md;troubleshooting.md;variable-definitions.md")]
    public void Read_keeps_its_context_within_what_Claude_Code_shows_in_full(string path, string ruleFiles)
    {
        // Claude Code saves hook context over about 10,000 characters to a file and shows the model a 2 KB preview
        // (measured 2026-10-08), so the hook shows at most 9,000; each of these files loads far more rules than that, so
        // some of its rule files must be named rather than shown.
        string worktree = CreateAgentWorktree(NewTempDir());
        string file = CreateFile(worktree, path);

        JsonElement? output = HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1")));

        Assert.NotNull(output);
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.InRange(context.Length, 1, 9_000);
        AssertEachRuleFileShownOrNamed(context, worktree, ruleFiles.Split(';'));
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
    public void Read_names_a_rule_file_once_per_subagent()
    {
        // FlyByWireA380Definition.Rmp.cs loads far more rules than the hook shows within its 9,000 characters: some are
        // only named ("Not shown in full"), and a named rule file is remembered too, so a second Read adds nothing.
        string worktree = CreateAgentWorktree(NewTempDir());
        string file = CreateFile(worktree, "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Rmp.cs");
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };

        JsonElement? first = HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1"), env: env));
        Assert.NotNull(first);
        Assert.Contains("Not shown in full", first.Value.GetProperty("additionalContext").GetString());
        Assert.Null(HookOutput(RunHook(new[] { "read" }, ReadInput(file, agentId: "a1"), env: env)));
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
    [InlineData("Bash", "sed -Ei 's/a/b/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "perl -i.bak -pe 's/a/b/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "# Don't touch the header\nsed -i 's/a/b/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "sed -i 's/a/b/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs  # it's a one-off", Pmdg737)]
    [InlineData("PowerShell", "# Don't touch the header\nSet-Content -Path MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs -Value x",
        Pmdg737)]
    [InlineData("Bash", "f=MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs && sed -i 's/a/b/' \"$f\"", Pmdg737)]
    [InlineData("Bash", "export D=MSFSBlindAssist/Aircraft; sed -i 's/a/b/' ${D}/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("PowerShell", "$p = 'MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs'; 'x' | Set-Content $p", Pmdg737)]
    [InlineData("PowerShell", "$P='MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs'; Set-Content -Path $p -Value x", Pmdg737)]
    [InlineData("Bash", "echo x>MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "(cd MSFSBlindAssist && sed -i 's/a/b/' Aircraft/Pmdg737DisplayReads.cs)", Pmdg737)]
    [InlineData("Bash", "export D=MSFSBlindAssist; cd $D && sed -i 's/a/b/' Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    // A > inside quotes, or one after an earlier redirect in the same word, never hides the unquoted > that follows.
    [InlineData("Bash", "echo \"a => b\">MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("PowerShell", "echo \"a => b\">MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "echo 'a>b'>MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "echo x>a>MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "echo x 2>&1>MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "echo x>>MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "echo x>|MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "echo x &>MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    // A subshell keeps its cd and its variables to itself; its closing ) comes off a quoted last word too, but the ) that
    // closes a $( ) does not end the subshell.
    [InlineData("Bash", "(cd tests && true); sed -i 's/a/b/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs", Pmdg737)]
    [InlineData("Bash", "(sed -i 's/a/b/' \"MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs\")", Pmdg737)]
    [InlineData("Bash", "(cd tests && v=$(pwd) && sed -i 's/a/b/' ../MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs)", Pmdg737)]
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
    [InlineData("Bash", "perl -Mstrict -ne 'print if /PMDG/' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("Bash", "perl -Ilib -ne 'print' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("Bash", "perl -Mwarnings -lne 'print' MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("Bash", "cat MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs # > MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("Bash", "cat > changelog.d/999-x.fix.md <<'EOF'\n> MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs\nEOF")]
    [InlineData("Bash", "cat > changelog.d/999-x.fix.md <<\\EOF\n> MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs\nEOF")]
    [InlineData("Bash", "cat > changelog.d/999-x.fix.md <<'END-OF-NOTE'\n> MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs\nEND-OF-NOTE")]
    [InlineData("Bash", "f=changelog.d/999-x.fix.md && sed -i 's/a/b/' \"$f\"")]
    [InlineData("Bash", "F=MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs; sed -i 's/a/b/' \"$f\"")]   // bash names are case-sensitive
    [InlineData("Bash", "cat MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs 2>&1")]
    [InlineData("Bash", "echo 'a>MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs' > changelog.d/999-x.fix.md")]
    [InlineData("PowerShell", "$p = $env:TEMP + '\\x.txt'; Set-Content $p x")]
    // A variable counts only while its value is known: NAME=VALUE before a command is that command's alone, and a value
    // that is not a literal (or a PowerShell += or computed value) makes the name unknown again.
    [InlineData("Bash", "f=MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs sed -i 's/a/b/' \"$f\"")]
    [InlineData("Bash", "f=MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs; f=$(mktemp); sed -i 's/a/b/' \"$f\"")]
    [InlineData("PowerShell", "$p = 'MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs'; $p += 'x'; Set-Content $p x")]
    [InlineData("Bash", "(f=MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs); sed -i 's/a/b/' \"$f\"")]   // a subshell's variable ends with it
    public void Shell_guard_allows_commands_that_write_no_covered_file(string tool, string command)
    {
        HookRun run = RunHook(new[] { "shell-guard" }, ShellInput(tool, command));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Stdout);
    }

    private static string Root => ClaudeContextBudgetTests.RepoRoot();
    private static string CoveredAbs => Path.Combine(Root, "MSFSBlindAssist", "Aircraft", "Pmdg737DisplayReads.cs");

    // The Python a command runs is read from -c, a heredoc, a here-string, stdin or a script file (also one the command
    // writes itself), and every write it names with a literal path is a write of that file. Interpreter options before
    // the script or the - (py -3 -X utf8 -, python -I -u s.py) do not hide it, and `python gen.py > covered` is a redirect.
    [Theory]
    [InlineData("python -c \"open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs','w').write('x')\"")]
    [InlineData("python - <<'PY'\nopen('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'w').write('x')\nPY")]
    [InlineData("cd MSFSBlindAssist && python3 - <<PY\nopen('Aircraft/Pmdg737DisplayReads.cs', 'a')\nPY")]
    [InlineData("py -3 -X utf8 - <<'PY'\nopen('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'w')\nPY")]
    [InlineData("cat > s.py <<'EOF'\nopen('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'w')\nEOF\npython -I -u s.py")]
    [InlineData("python gen.py > MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs")]
    [InlineData("python -Bc \"open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs','w')\"")]
    [InlineData("python3 -c \"import io; io.open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', encoding='utf-8', mode='w')\"")]
    [InlineData("PYTHONIOENCODING=utf-8 python - <<'PY'\nimport codecs\ncodecs.open(\"MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs\", \"a\", \"utf-8\")\nPY")]
    [InlineData("python - <<< \"open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'w')\"")]
    [InlineData("tee s.py <<'EOF' > /dev/null\nopen('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'w')\nEOF\npython s.py")]
    public void Shell_guard_refuses_python_that_writes_a_covered_file(string command)
    {
        JsonElement? output = HookOutput(RunHook(new[] { "shell-guard" }, ShellInput("Bash", command)));
        Assert.NotNull(output);
        Assert.Equal("deny", output.Value.GetProperty("permissionDecision").GetString());
        Assert.Contains(Pmdg737, output.Value.GetProperty("permissionDecisionReason").GetString());
    }

    [Fact]
    public void Shell_guard_reads_a_python_script_from_disk_and_from_stdin()
    {
        string script = CreateFile(NewTempDir(), "edit.py", $"open(r'{CoveredAbs}', 'w').write('x')\n");
        foreach (string command in new[] { $"python \"{script}\"", $"python - < \"{script}\"", $"python < \"{script}\"" })
        {
            JsonElement? output = HookOutput(RunHook(new[] { "shell-guard" }, ShellInput("Bash", command)));
            Assert.NotNull(output);
            Assert.Equal("deny", output.Value.GetProperty("permissionDecision").GetString());
        }
    }

    [Theory]
    [InlineData("python -m pytest tests")]
    [InlineData("python C:/no/such/script.py")]
    [InlineData("python - <<'PY'\nprint(open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs').read())\nPY")]
    [InlineData("python -c")]
    [InlineData("python -c \"open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'rb').read()\"")]
    [InlineData("python - <<'PY'\nopen('changelog.d/999-x.fix.md', 'w').write(open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'r', encoding='utf-8').read())\nPY")]
    [InlineData("cat > s.py <<'EOF'\nopen('changelog.d/999-x.fix.md', 'w')\nEOF\npython s.py")]
    // A heredoc fed to a command that is not Python is text, not code, whatever it contains.
    [InlineData("git commit -F - <<'EOF'\nopen('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'w')\nEOF")]
    // A write in a comment or inside a string is not code.
    [InlineData("python -c \"# open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs','w')\"")]
    [InlineData("python -c \"print(\\\"open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs', 'w')\\\")\"")]
    public void Shell_guard_allows_python_that_writes_no_covered_file(string command)
    {
        HookRun run = RunHook(new[] { "shell-guard" }, ShellInput("Bash", command));
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Stdout);
    }

    [Fact]
    public void Shell_guard_refuses_python_run_from_powershell()
    {
        JsonElement? output = HookOutput(RunHook(new[] { "shell-guard" },
            ShellInput("PowerShell", "python -c \"open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs','w')\"")));
        Assert.NotNull(output);
        Assert.Equal("deny", output.Value.GetProperty("permissionDecision").GetString());
    }

    // CCT-7: a write sink (open with a w/a/x/+ mode, write_text/write_bytes, a copy, move or replace destination) is
    // refused when its target resolves from literals, through a name's nearest earlier binding, to a covered file. Each
    // script runs as `python - <<'PY'`; {C} stands for the covered Pmdg737 file, {S} for an uncovered changelog fragment
    // and {CoveredAbs} for the covered file's absolute path.
    [Theory]
    [InlineData("import io\np = \"{C}\"\ns = io.open(p, encoding=\"utf-8\", newline=\"\").read()\n"
        + "io.open(p, \"w\", encoding=\"utf-8\", newline=\"\").write(s.replace(\"a\", \"b\"))")]
    [InlineData("import pathlib\npathlib.Path(r'{CoveredAbs}').write_text('x')")]
    [InlineData("from pathlib import Path\nP = Path('{C}')\nP.write_text(P.read_text().replace('a', 'b'))")]
    [InlineData("files = [\n    '{S}',\n    '{C}',\n]\nfor f in files:\n    open(f, 'a').write('x')")]
    [InlineData("import shutil\nshutil.copy('scratch.cs', '{C}')")]
    [InlineData("import os\np = os.path.join('MSFSBlindAssist', 'Aircraft', 'Pmdg737DisplayReads.cs')\nopen(p, mode='w')")]
    [InlineData("p = 'MSFSBlindAssist\\\\Aircraft\\\\Pmdg737DisplayReads.cs'\nopen(p, 'w')")]   // doubled backslashes in the Python
    [InlineData("import os\nos.replace(tmp, '{C}')")]
    [InlineData("with open('{C}', 'r+') as fh:\n    fh.write('x')")]
    [InlineData("\"\"\"Don't panic: open('nowhere', 'w').\"\"\"\n# it's fine\nopen('{C}', 'w')")]
    // A mode bound to a name, file=/dst= keywords, a bare copy2, a Path variant, literals side by side, a raw string.
    [InlineData("m = 'w'\nopen('{C}', m)")]
    [InlineData("open(file='{C}', mode='w')")]
    [InlineData("import shutil\nshutil.copy2(s, dst='{C}')")]
    [InlineData("from shutil import copy2\ncopy2('scratch.cs', '{C}')")]
    [InlineData("from pathlib import PureWindowsPath\nopen(PureWindowsPath('MSFSBlindAssist/Aircraft', 'Pmdg737DisplayReads.cs'), 'w')")]
    [InlineData("open('MSFSBlindAssist/Aircraft/' 'Pmdg737DisplayReads.cs', 'w')")]
    [InlineData("open(r'MSFSBlindAssist/Forms/PMDG737\\new.cs', 'w')", "MSFSBlindAssist/Forms/PMDG737/new.cs")]
    // A def body that writes its own local binding: the binding holds until the body ends.
    [InlineData("OUT = 'scratch/report.md'\ndef save(t):\n    OUT = '{C}'\n    open(OUT, 'w').write(t)")]
    // A loop the guard does not follow, earlier in the text, does not hide the covered binding that comes after it.
    [InlineData("import sys\nfor p in sys.argv[1:]:\n    print(p)\np = '{C}'\nopen(p, 'w')")]
    public void Shell_guard_refuses_a_python_write_resolved_to_a_covered_file(string script, string covered = Pmdg737)
    {
        JsonElement? output = HookOutput(RunHook(new[] { "shell-guard" }, ShellInput("Bash", PythonHeredoc(script))));
        Assert.NotNull(output);
        Assert.Equal("deny", output.Value.GetProperty("permissionDecision").GetString());
        Assert.Contains(covered, output.Value.GetProperty("permissionDecisionReason").GetString());
    }

    // A long one-line literal ahead of the write must not use up the guard's time (the analysis stops at a deadline
    // and lets the rest run): a literal target is checked before any name is resolved, and resolving a name stays
    // linear in the length of the line. Both writes are refused, well inside the hook's 10-second timeout.
    [Theory]
    [InlineData("out = 'scratch/x'; open(out, 'w')\nopen('{C}', 'w')")]
    [InlineData("p = '{C}'\nopen(p, 'w')")]
    public void Shell_guard_refuses_a_python_write_after_a_long_one_line_literal(string tail)
    {
        string dict = "d = {" + string.Join(", ", Enumerable.Range(0, 2500).Select(i => $"'k{i}': {i}")) + "}\n";
        var clock = Stopwatch.StartNew();
        JsonElement? output = HookOutput(RunHook(new[] { "shell-guard" }, ShellInput("Bash", PythonHeredoc(dict + tail))));
        clock.Stop();
        Assert.NotNull(output);
        Assert.Equal("deny", output.Value.GetProperty("permissionDecision").GetString());
        Assert.Contains(Pmdg737, output.Value.GetProperty("permissionDecisionReason").GetString());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"the hook took {clock.Elapsed.TotalSeconds:F1} s");
    }

    // A script that only reads a covered file, whose write target is another file, or whose target the guard cannot
    // resolve (a loop variable over os.walk, an f-string) always runs; so does a write that only appears inside a string.
    [Theory]
    [InlineData("src = '{C}'\nout = '{S}'\nopen(out, 'w').write(open(src).read())")]
    [InlineData("p = '{C}'\ns = open(p).read()\np = '{S}'\nopen(p, 'w').write(s)")]
    [InlineData("p = '{C}'\nprint(open(p, 'rb').read())")]
    [InlineData("import os\nfor d, _, fs in os.walk('MSFSBlindAssist'):\n    for f in fs:\n        open(os.path.join(d, f), 'w')")]
    [InlineData("n = 'X'\nopen(f'MSFSBlindAssist/Aircraft/{n}.cs', 'w')")]
    [InlineData("doc = \"\"\"\nopen('{C}', 'w')\n\"\"\"\nprint(doc)")]
    [InlineData("print(\"open('{C}', 'w')\")")]
    // A rebinding the guard does not follow leaves the name unresolved, so the write runs: an augmented assignment, a
    // tuple swap, a def or lambda parameter, with ... as, a comprehension variable (on one line, spread over lines, or a
    // generator inside a call), and a keyword argument on its own line, which is no binding at all.
    [InlineData("p = '{C}'\np += '.bak'\nopen(p, 'w')")]
    [InlineData("a = '{C}'\nb = '{S}'\na, b = b, a\nopen(a, 'w')")]
    [InlineData("p = '{C}'\ndef save(p):\n    open(p, 'w')")]
    [InlineData("p = '{C}'\nsave = lambda p: open(p, 'w')")]
    [InlineData("p = '{C}'\nwith open('{S}') as p:\n    pass\nopen(p, 'w')")]
    [InlineData("f = '{C}'\nouts = [open(f, 'w') for f in ['{S}']]")]
    [InlineData("p = '{C}'\ntext = open(p).read()\nouts = [\n    open(p, 'w')\n    for p in ['a.txt', 'b.txt']\n]")]
    [InlineData("from pathlib import Path\np = '{C}'\nlist(\n    Path(p).write_text('x')\n    for p in ['a.txt']\n)")]
    [InlineData("p = '{S}'\nfoo(\n    p='{C}',\n)\nopen(p, 'w')")]
    // A binding made inside a def body ends with that body: report() writes the global OUT, not scan()'s local one.
    [InlineData("OUT = 'scratch/report.md'\ndef scan():\n    OUT = '{C}'\n    return open(OUT).read()\n"
        + "def report(t):\n    open(OUT, 'w').write(t)")]
    // An escape that changes the value (\n here) leaves the literal unresolved: Python would not write this path.
    [InlineData("open('MSFSBlindAssist/Forms/PMDG737\\new.cs', 'w')")]
    // A binding the guard does not follow, earlier in the text than the scratch write, must not shift the write onto
    // the later covered binding the script only reads (the bindings are found per kind, then put in text order).
    [InlineData("names = [p for p in ['a', 'b']]\np = 'scratch/out'\nopen(p, 'w')\np = '{C}'\nprint(open(p).read())")]
    [InlineData("for p in ['a', 'b']:\n    print(p)\np = 'scratch/out'\nopen(p, 'w')\np = '{C}'\nprint(open(p).read())")]
    [InlineData("with open('a.txt') as p:\n    pass\np = 'scratch/out'\nopen(p, 'w')\np = '{C}'\nprint(open(p).read())")]
    [InlineData("p = 'a'\np += 'b'\np = 'scratch/out'\nopen(p, 'w')\np = '{C}'\nprint(open(p).read())")]
    [InlineData("def show(p):\n    return p\np = 'scratch/out'\nopen(p, 'w')\np = '{C}'\nprint(open(p).read())")]
    [InlineData("import sys\nfor OUT in sys.argv[1:]:\n    print(OUT)\nOUT = 'scratch/report.md'\ndef report(t):\n"
        + "    open(OUT, 'w').write(t)\ndef scan():\n    OUT = '{C}'\n    return open(OUT).read()")]
    [InlineData("p = 'scratch/x.txt'\nouts = [\n    open(p, 'w')\n    for p in ['a.txt', 'b.txt']\n]\np = '{C}'\nprint(open(p).read())")]
    public void Shell_guard_allows_a_python_script_that_writes_no_resolved_covered_file(string script)
    {
        HookRun run = RunHook(new[] { "shell-guard" }, ShellInput("Bash", PythonHeredoc(script)));
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Stdout);
    }

    private static string PythonHeredoc(string script) => "python - <<'PY'\n"
        + script.Replace("{C}", Pmdg737).Replace("{S}", "changelog.d/999-x.fix.md").Replace("{CoveredAbs}", CoveredAbs)
        + "\nPY";

    // python-writes.ps1 is optional (CCT-1, CCT-7): when it is missing or does not parse, the guard loses its Python
    // analysis (a Python write is allowed) and nothing else - redirects are still refused and diff mode still adds rules.
    [Theory]
    [InlineData(null)]
    [InlineData("function Get-PythonWriteTargets( {\n")]
    public void A_missing_or_broken_python_helper_costs_only_the_python_analysis(string? helper)
    {
        string hooks = Path.Combine(NewTempDir(), "hooks");
        Directory.CreateDirectory(hooks);
        string script = Path.Combine(hooks, "rules-hook.ps1");
        File.Copy(ScriptPath, script);
        if (helper is not null) File.WriteAllText(Path.Combine(hooks, "python-writes.ps1"), helper);

        JsonElement? redirect = HookOutput(RunHook(new[] { "shell-guard" },
            ShellInput("Bash", "echo x > " + Pmdg737), scriptPath: script));
        Assert.NotNull(redirect);
        Assert.Equal("deny", redirect.Value.GetProperty("permissionDecision").GetString());
        Assert.Contains(Pmdg737, redirect.Value.GetProperty("permissionDecisionReason").GetString());

        HookRun python = RunHook(new[] { "shell-guard" },
            ShellInput("Bash", "python -c \"open('MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs','w')\""), scriptPath: script);
        Assert.Equal(0, python.ExitCode);
        Assert.Equal("", python.Stdout);

        JsonElement? diff = HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse(ShownInFullDiff)), scriptPath: script));
        Assert.NotNull(diff);
        Assert.Contains(RuleBody("ifly-737.md"), diff.Value.GetProperty("additionalContext").GetString());
    }

    // ifly-737.md and variable-definitions.md: about 7,000 characters, so both arrive in full.
    private const string ShownInFull = "MSFSBlindAssist/Aircraft/IFly737FlapSpeeds.cs";
    private const string ShownInFullDiff =
        "diff --git a/" + ShownInFull + " b/" + ShownInFull + "\n"
        + "index 1111111..2222222 100644\n--- a/" + ShownInFull + "\n"
        + "+++ b/" + ShownInFull + "\n@@ -1 +1 @@\n-a\n+b\n";

    [Fact]
    public void Diff_adds_the_rule_files_for_a_changed_path()
    {
        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse(ShownInFullDiff))));

        Assert.NotNull(output);
        Assert.Equal("PostToolUse", output.Value.GetProperty("hookEventName").GetString());
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.StartsWith("Area rules for the files in this diff.", context);
        Assert.Contains(RuleBody("variable-definitions.md"), context);   // holds non-ASCII text: checks the encoding
        Assert.Contains("Contents of " + Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "rules", "ifly-737.md")
            + ":", context);
    }

    [Fact]
    public void Diff_reads_name_only_output()
    {
        JsonElement? output = HookOutput(RunHook(new[] { "diff" },
            DiffInput("git diff --name-only", BashResponse(ShownInFull + "\n"))));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("variable-definitions.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Diff_accepts_a_plain_string_tool_response()
    {
        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", ShownInFullDiff)));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("variable-definitions.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Diff_uses_the_folder_named_by_git_dash_C()
    {
        string root = ClaudeContextBudgetTests.RepoRoot();

        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput($"git -C \"{root}\" diff --name-only",
            BashResponse(ShownInFull + "\n"), cwd: NewTempDir())));

        Assert.NotNull(output);
        Assert.Contains(RuleBody("variable-definitions.md"), output.Value.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void Diff_keeps_its_context_within_what_Claude_Code_shows_in_full()
    {
        // Claude Code saves hook context over about 10,000 characters to a file and shows the model a 2 KB preview
        // (measured 2026-10-08). TaxiGuidanceManager.cs and FlyByWireA380Definition.cs load fifteen rule files, over
        // 45,000 characters of bodies (48,766 on 2026-10-10): the hook shows whole files within 9,000 characters and names
        // the rest to Read.
        JsonElement? output = HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff --name-only", BashResponse(
            "MSFSBlindAssist/Services/TaxiGuidanceManager.cs\nMSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs\n"))));

        Assert.NotNull(output);
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.InRange(context.Length, 1, 9_000);
        AssertEachRuleFileShownOrNamed(context, ClaudeContextBudgetTests.RepoRoot(), "landing-rollout.md",
            "runway-holds.md", "taxi-manager-call-sites.md", "taxi-position.md", "taxi-steering.md",
            "a380-definition-call-sites.md", "a380-fcu.md", "a380-panels.md", "a380-systems.md", "fbw-arinc.md",
            "fcu-callouts.md", "flypad-call-sites.md", "tcas-ra.md", "troubleshooting.md", "variable-definitions.md");
    }

    [Fact]
    public void Diff_adds_nothing_new_on_a_repeat()
    {
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };

        Assert.NotNull(HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse(ShownInFullDiff)), env: env)));
        Assert.Null(HookOutput(RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse(ShownInFullDiff)), env: env)));
    }

    [Fact]
    public void Diff_names_a_rule_file_once_per_session()
    {
        // TaxiGuidanceManager.cs + FlyByWireA380Definition.cs load over 45,000 characters of rules: most are only
        // named, and a named rule file is remembered too, so a second diff of the same files adds nothing.
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };
        string input = DiffInput("git diff --name-only", BashResponse(
            "MSFSBlindAssist/Services/TaxiGuidanceManager.cs\nMSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs\n"));

        JsonElement? first = HookOutput(RunHook(new[] { "diff" }, input, env: env));

        Assert.NotNull(first);
        Assert.Contains("Not shown in full", first.Value.GetProperty("additionalContext").GetString());
        Assert.Null(HookOutput(RunHook(new[] { "diff" }, input, env: env)));
    }

    [Theory]
    [InlineData("git log --name-only -1")]
    [InlineData("git status --short")]
    [InlineData("gh pr view 270 --json files")]
    public void Diff_ignores_commands_that_are_not_a_diff(string command)
    {
        // The diff mode is registered under Bash(git *) and Bash(gh *), so it also runs for git log and the like.
        HookRun run = RunHook(new[] { "diff" }, DiffInput(command, BashResponse(Pmdg737 + "\n")));

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void Diff_adds_rules_once_when_several_hooks_run_it_at_once()
    {
        // A command can match several registered handlers, which Claude Code runs in parallel (seen 2026-10-08: five
        // diff handlers, five copies). Only one of them may add the rules.
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };
        string input = DiffInput("git diff", BashResponse(ShownInFullDiff));

        HookRun[] runs = Enumerable.Range(0, 5).AsParallel().WithDegreeOfParallelism(5)
            .Select(_ => RunHook(new[] { "diff" }, input, env: env)).ToArray();

        Assert.Equal(1, runs.Count(r => HookOutput(r) != null));
    }

    [Fact]
    public void Diff_ignores_output_without_paths()
    {
        HookRun run = RunHook(new[] { "diff" }, DiffInput("git diff", BashResponse("")));

        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void Plan_agent_starts_with_the_rules_for_any_file_and_is_told_to_read_CLAUDE_md()
    {
        // CLAUDE.md (17,000+ characters) is over what Claude Code shows of hook context in full (about 10,000), so
        // the Plan agent gets the two sections that bind a plan and the path to Read for the rest.
        string root = ClaudeContextBudgetTests.RepoRoot();
        string claudeMd = File.ReadAllText(Path.Combine(root, "CLAUDE.md")).Replace("\r\n", "\n");

        JsonElement? output = HookOutput(RunHook(new[] { "subagent-start" }, SubagentInput("Plan", root),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root }));

        Assert.NotNull(output);
        Assert.Equal("SubagentStart", output.Value.GetProperty("hookEventName").GetString());
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.StartsWith("CLAUDE.md (the built-in Plan agent skips it;", context);
        Assert.InRange(context.Length, 1, 9_000);
        Assert.Contains("Read " + Path.Combine(root, "CLAUDE.md"), context);
        Assert.Contains(Section(claudeMd, "## Rules for any file"), context);
        Assert.Contains(Section(claudeMd, "## Before changing behaviour"), context);
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
    public void A_subagent_in_the_sessions_own_worktree_needs_no_instruction()
    {
        // Desktop-app session: it runs in .claude\worktrees\<name>, its transcripts are filed under that folder, and
        // CLAUDE_PROJECT_DIR is the MAIN checkout (measured 2026-10-09). Claude Code loads the rules there.
        string main = NewTempDir();
        string worktree = Path.Combine(main, ".claude", "worktrees", "review-x");
        HookRun run = RunHook(new[] { "subagent-start" }, SubagentInput("general-purpose", worktree, TranscriptFor(worktree)),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = main });
        Assert.Equal(0, run.ExitCode);
        Assert.Null(HookOutput(run));
    }

    [Fact]
    public void A_subagent_in_someone_elses_worktree_is_told_to_load_its_rules()
    {
        string main = NewTempDir();
        string worktree = Path.Combine(main, ".claude", "worktrees", "review-x");
        JsonElement? output = HookOutput(RunHook(new[] { "subagent-start" },
            SubagentInput("general-purpose", worktree, TranscriptFor(main)),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = worktree }));   // no longer consulted
        Assert.NotNull(output);
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.Contains("review-x", context);
        // Windows client machines default to the Restricted execution policy, where a bare -File run fails.
        Assert.Contains("powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/rules-hook.ps1 for <path>", context);
    }

    [Fact]
    public void A_subagent_with_no_transcript_path_is_told_nothing()
    {
        string main = NewTempDir();
        string worktree = Path.Combine(main, ".claude", "worktrees", "review-x");
        Assert.Null(HookOutput(RunHook(new[] { "subagent-start" }, SubagentInput("general-purpose", worktree),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = main })));
    }

    [Fact]
    public void Plan_agent_gets_the_CLAUDE_md_of_its_own_checkout()
    {
        string checkout = NewTempDir();
        CreateFile(checkout, ".git", "gitdir: elsewhere\n");
        Directory.CreateDirectory(Path.Combine(checkout, ".claude", "rules"));
        CreateFile(checkout, "CLAUDE.md", "# X\n\n## Rules for any file\n\n- own-checkout-marker\n\n## Before changing behaviour\n\nb\n");
        JsonElement? output = HookOutput(RunHook(new[] { "subagent-start" }, SubagentInput("Plan", checkout),
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = ClaudeContextBudgetTests.RepoRoot() }));
        Assert.NotNull(output);
        string context = output.Value.GetProperty("additionalContext").GetString()!;
        Assert.Contains("own-checkout-marker", context);
        Assert.Contains("Read " + Path.Combine(checkout, "CLAUDE.md"), context);
    }

    [Fact]
    public void An_agent_worktree_needs_no_instruction()
    {
        // The transcript key here differs from the worktree's own key, so only the agent-* exemption by name keeps the
        // hook silent. Pass a differing key: without a transcript_path the hook is silent anyway.
        string cwd = Path.Combine(NewTempDir(), ".claude", "worktrees", "agent-abc");

        Assert.Null(HookOutput(RunHook(new[] { "subagent-start" },
            SubagentInput("general-purpose", cwd, TranscriptFor(NewTempDir())))));
    }

    [Fact]
    public void Compaction_lets_rules_be_added_again()
    {
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };
        string diff = DiffInput("git diff", BashResponse(ShownInFullDiff));

        Assert.NotNull(HookOutput(RunHook(new[] { "diff" }, diff, env: env)));
        Assert.Null(HookOutput(RunHook(new[] { "diff" }, diff, env: env)));
        HookRun compacted = RunHook(new[] { "session-start" },
            HookInput(new { session_id = "s1", hook_event_name = "SessionStart", source = "compact" }), env: env);
        Assert.Equal("", compacted.Stdout);
        JsonElement? again = HookOutput(RunHook(new[] { "diff" }, diff, env: env));
        Assert.NotNull(again);
        Assert.Contains(RuleBody("variable-definitions.md"), again.Value.GetProperty("additionalContext").GetString());
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
            "diff" => DiffInput("git diff", BashResponse(ShownInFullDiff)),
            _ => SubagentInput("Plan", root),
        };

        HookRun on = RunHook(new[] { mode }, input, env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root });
        HookRun off = RunHook(new[] { mode }, input,
            env: new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root, ["MSFSBA_RULES_HOOK"] = "off" });

        Assert.NotEqual("", on.Stdout);
        Assert.Equal(0, off.ExitCode);
        Assert.Equal("", off.Stdout);
    }

    private static readonly string[] HookArgsPrefix =
        { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", "${CLAUDE_PROJECT_DIR}/.claude/hooks/rules-hook.ps1" };

    [Fact]
    public void Settings_register_the_pinned_hooks()
    {
        // Pinned on purpose: the original filters were checked in a live session (2026-10-08). The Python filters were
        // added 2026-10-09 (CCT-7) and Bash(python *) was live-checked that day; Bash(python3 *), Bash(py *) and the
        // PowerShell Python filters are pinned but not individually live-checked. Some filters never match
        // (Write(...), Edit(...) on a Write, redirect forms such as Bash(cat >*)), and one naming more than the command
        // (Bash(git diff*)) runs on any command holding $VAR or $(). Change a filter only after the live checks in
        // docs/development.md, then update this list (CCT-3).
        List<RegisteredHook> hooks = RegisteredHooks();
        foreach (RegisteredHook hook in hooks)
        {
            Assert.Equal("command", hook.Type);
            Assert.Equal("powershell.exe", hook.Command);
            Assert.Equal(HookArgsPrefix, hook.Args[..^1]);
            Assert.Equal(10, hook.Timeout);
        }
        Assert.Equal(new[]
        {
            "PostToolUse @ Read @ read @ Read(//**/.claude/worktrees/agent-*/**)",
            "PostToolUse @ Write|NotebookEdit @ read @ ",
            "PostToolUse @ Bash @ diff @ Bash(git *)", "PostToolUse @ Bash @ diff @ Bash(gh *)",
            "PostToolUse @ PowerShell @ diff @ PowerShell(git *)", "PostToolUse @ PowerShell @ diff @ PowerShell(gh *)",
            "PreToolUse @ Bash @ shell-guard @ Bash(sed *)", "PreToolUse @ Bash @ shell-guard @ Bash(perl *)",
            "PreToolUse @ Bash @ shell-guard @ Bash(tee *)", "PreToolUse @ Bash @ shell-guard @ Bash(cat *)",
            "PreToolUse @ Bash @ shell-guard @ Bash(echo *)", "PreToolUse @ Bash @ shell-guard @ Bash(printf *)",
            "PreToolUse @ Bash @ shell-guard @ Bash(python *)", "PreToolUse @ Bash @ shell-guard @ Bash(python3 *)",
            "PreToolUse @ Bash @ shell-guard @ Bash(py *)",
            "PreToolUse @ PowerShell @ shell-guard @ PowerShell(Set-Content *)",
            "PreToolUse @ PowerShell @ shell-guard @ PowerShell(Add-Content *)",
            "PreToolUse @ PowerShell @ shell-guard @ PowerShell(Out-File *)",
            "PreToolUse @ PowerShell @ shell-guard @ PowerShell(python *)",
            "PreToolUse @ PowerShell @ shell-guard @ PowerShell(python3 *)",
            "PreToolUse @ PowerShell @ shell-guard @ PowerShell(py *)",
            "SubagentStart @  @ subagent-start @ ",
            "SessionStart @ compact @ session-start @ ",
        }, hooks.Select(h => $"{h.Event} @ {h.Matcher} @ {h.Args[^1]} @ {h.If}"));
    }

    [Fact]
    public void Every_registered_hook_runs_a_mode_that_acts_on_its_event()
    {
        // A mistyped mode exits silently by design (CCT-1), so check each registered (event, mode) does something.
        string root = ClaudeContextBudgetTests.RepoRoot();
        var env = new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = root };
        foreach ((string evt, string mode) in RegisteredHooks().Select(h => (h.Event, h.Args[^1])).Distinct())
        {
            bool acts = (evt, mode) switch
            {
                ("PostToolUse", "read") => HookOutput(RunHook(new[] { mode },
                    ReadInput(CreateFile(CreateAgentWorktree(NewTempDir()), Pmdg737), agentId: "a1"))) != null,
                ("PostToolUse", "diff") => HookOutput(RunHook(new[] { mode }, DiffInput("git diff", BashResponse(ShownInFullDiff)))) != null,
                ("PreToolUse", "shell-guard") => HookOutput(RunHook(new[] { mode },
                    ShellInput("Bash", $"sed -i 's/a/b/' {Pmdg737}"))) != null,
                ("SubagentStart", "subagent-start") => HookOutput(RunHook(new[] { mode }, SubagentInput("Plan", root), env: env)) != null,
                ("SessionStart", "session-start") => ForgetsAfterCompaction(mode),
                _ => false,
            };
            Assert.True(acts, $"{evt} runs rules-hook.ps1 in mode '{mode}', which does nothing for that event.");
        }
    }

    private static bool ForgetsAfterCompaction(string mode)
    {
        var env = new Dictionary<string, string?> { ["TEMP"] = NewTempDir(), ["TMP"] = null };
        string diff = DiffInput("git diff", BashResponse(ShownInFullDiff));
        RunHook(new[] { "diff" }, diff, env: env);
        RunHook(new[] { mode }, HookInput(new { session_id = "s1", hook_event_name = "SessionStart", source = "compact" }), env: env);
        return HookOutput(RunHook(new[] { "diff" }, diff, env: env)) != null;
    }

    private sealed record RegisteredHook(string Event, string Matcher, string Type, string Command, string[] Args, int Timeout,
        string If);

    private static List<RegisteredHook> RegisteredHooks()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "settings.json")));
        var hooks = new List<RegisteredHook>();
        foreach (JsonProperty evt in doc.RootElement.GetProperty("hooks").EnumerateObject())
            foreach (JsonElement group in evt.Value.EnumerateArray())
            {
                string matcher = group.TryGetProperty("matcher", out JsonElement m) ? m.GetString()! : "";
                foreach (JsonElement h in group.GetProperty("hooks").EnumerateArray())
                    hooks.Add(new RegisteredHook(evt.Name, matcher, h.GetProperty("type").GetString()!,
                        h.GetProperty("command").GetString()!,
                        h.GetProperty("args").EnumerateArray().Select(a => a.GetString()!).ToArray(),
                        h.GetProperty("timeout").GetInt32(),
                        h.TryGetProperty("if", out JsonElement f) ? f.GetString()! : ""));
            }
        return hooks;
    }

    // ---- inputs and fixtures ----

    private static string SubagentInput(string agentType, string cwd, string? transcriptPath = null) => transcriptPath is null
        ? HookInput(new { session_id = "s1", hook_event_name = "SubagentStart", agent_id = "p1", agent_type = agentType, cwd })
        : HookInput(new
        {
            session_id = "s1", hook_event_name = "SubagentStart", agent_id = "p1", agent_type = agentType, cwd,
            transcript_path = transcriptPath,
        });

    /// <summary>Where Claude Code files a session's subagent transcript: under .claude\projects\ in a folder named after
    /// the session's folder, every character outside [A-Za-z0-9] written as '-'.</summary>
    private static string TranscriptFor(string sessionFolder) => Path.Combine(NewTempDir(), ".claude", "projects",
        Regex.Replace(sessionFolder.TrimEnd('\\'), "[^A-Za-z0-9]", "-"), "s1", "subagents", "agent-p1.jsonl");

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

    /// <summary>Every listed rule file is either shown in full (its whole body is in the context) or named by its
    /// absolute path for Claude to Read, and at least one is shown in full.</summary>
    private static void AssertEachRuleFileShownOrNamed(string context, string root, params string[] ruleFiles)
    {
        int shown = 0;
        foreach (string ruleFile in ruleFiles)
        {
            if (context.Contains(RuleBody(ruleFile), StringComparison.Ordinal)) { shown++; continue; }
            Assert.Contains(Path.Combine(root, ".claude", "rules", ruleFile), context);
        }
        Assert.True(shown > 0, "no rule file was shown in full");
    }

    /// <summary>A CLAUDE.md section: its heading line up to the next "## " heading.</summary>
    private static string Section(string text, string heading)
    {
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"CLAUDE.md has no '{heading}' heading");
        int end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return (end < 0 ? text[start..] : text[start..end]).TrimEnd('\n');
    }

    /// <summary>A rule file's body as the guard test reads it, independently of the script under test.</summary>
    private static string RuleBody(string ruleFile) => ClaudeContextBudgetTests.SplitFrontMatter(
        File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".claude", "rules", ruleFile)).Replace("\r\n", "\n")).Body
        .TrimEnd('\n');

    // ---- harness ----

    /// <summary>Runs the hook script as Claude Code does. TEMP/TMP point at a fresh folder per call (the hook keeps
    /// its memory of added rule files there) and the MSFSBA_RULES_HOOK switch is cleared, unless <paramref name="env"/>
    /// sets them; a null value in <paramref name="env"/> removes that variable.</summary>
    private static HookRun RunHook(string[] args, string? stdin = null, string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? env = null, string? scriptPath = null)
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
        foreach (string a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath ?? ScriptPath })
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
        TempDirs.Add(dir);
        return dir;
    }
}
