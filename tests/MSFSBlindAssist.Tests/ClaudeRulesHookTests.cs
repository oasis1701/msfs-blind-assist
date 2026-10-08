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
