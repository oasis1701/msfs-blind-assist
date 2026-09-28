// The incursion guard's per-node memory is reset in ONE place.
//
// The warned-node latch (_lastIncursionWarnedNodeId) and the once-per-node "Incursion warning
// withheld" log set (_incursionWithheldLoggedNodes) describe the same thing — which hold lines
// the guard has already spoken or logged about — and must be reset together. PR #255 reset the
// set on LoadRoute, StopGuidance and a new graph, but the accepted recalculation re-armed only
// the latch, so a hold line withheld on the old route was never logged again on the new one.
// TaxiGuidanceManager has no test harness, so this pins the wiring from the source: the latch is
// re-armed only inside ResetIncursionNodeMemory, which clears both.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class IncursionGuardResetTests
{
    private static readonly Regex LatchReset = new(@"^\s*_lastIncursionWarnedNodeId\s*=\s*-1\s*;", RegexOptions.Multiline);

    [Fact]
    public void The_warned_node_latch_is_re_armed_only_by_the_shared_reset()
    {
        string services = Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services");
        var offenders = Directory.GetFiles(services, "TaxiGuidanceManager*.cs")
            .SelectMany(f => LatchReset.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Index: m.Index, Text: File.ReadAllText(f))))
            .Where(m => EnclosingMethod(m.Text, m.Index) != "ResetIncursionNodeMemory")
            .Select(m => $"{m.File}: {EnclosingMethod(m.Text, m.Index)}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "Re-arm the incursion latch through ResetIncursionNodeMemory() so the withheld-log set is cleared with it: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_shared_reset_clears_the_withheld_log_set_too()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));
        int at = text.IndexOf("void ResetIncursionNodeMemory()", StringComparison.Ordinal);
        Assert.True(at >= 0, "ResetIncursionNodeMemory() not found in TaxiGuidanceManager.cs");
        string body = text.Substring(at, text.IndexOf('}', at) - at);
        Assert.Contains("_incursionWithheldLoggedNodes.Clear();", body);
        Assert.Matches(LatchReset, body);
    }

    // The name of the last method declared before <index> — enough to tell which member a
    // statement sits in for this file's plain `private void Name(` / `public void Name(` shapes.
    private static string EnclosingMethod(string text, int index)
    {
        var decls = Regex.Matches(text.Substring(0, index),
            @"^\s*(?:public|private|internal|protected)[^\n=;]*?\b(\w+)\s*\(", RegexOptions.Multiline);
        return decls.Count == 0 ? "" : decls[^1].Groups[1].Value;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
