using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ AN AGENT CALL MUST NAME THE AGENT'S GLOBAL, NEVER ITS INTERNAL SHORTHAND.
///
/// Every Coherent agent in this codebase keeps its namespace in a closure and publishes exactly
/// one thing on the page - the DA40's is <c>window.__MSFSBA_DA40G1000</c>. Inside the agent's own
/// source that namespace is called <c>A</c>, and writing <c>A.M.something()</c> in a C# call site
/// reads perfectly while being a ReferenceError on the page.
///
/// It fails SILENTLY, which is what makes it worth a test: the exception is caught, an empty
/// string comes back, and the feature is simply dead. Shipped exactly that way on 2026-09-03 -
/// the waypoint call fetched its leg names with "A.M.activeLeg()", got nothing for an entire
/// flight, and every readout said "Unnamed leg" while the flight plan had the names all along.
/// </summary>
public class CoherentAgentGlobalNameTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate the repository root from the test binary.");
        return dir!.FullName;
    }

    [Fact]
    public void NoCallSiteInvokesAnAgentThroughItsInternalShorthand()
    {
        string src = Path.Combine(RepoRoot(), "MSFSBlindAssist");
        Assert.True(Directory.Exists(src), $"Source folder not found at {src}");

        // A string literal that STARTS an expression with a bare A. — the agent's internal
        // name. Anything reaching the page must go through window.__MSFSBA_*.
        var offender = new Regex(@"InvokeAsync\s*\(\s*""\s*A\s*\.", RegexOptions.Compiled);

        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (File: f, Text: File.ReadAllText(f)))
            .Where(x => offender.IsMatch(x.Text))
            .Select(x => Path.GetRelativePath(src, x.File))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(hits.Count == 0,
            "These call sites invoke an agent through its internal shorthand 'A.', which is a " +
            "ReferenceError on the page and fails silently — use the agent's published global " +
            "(window.__MSFSBA_...): " + string.Join(", ", hits));
    }

    [Fact]
    public void TheDa40AgentStillPublishesTheGlobalTheCallSitesUse()
    {
        // The other half of the contract: if the agent is ever renamed, the call sites above
        // become wrong in the opposite direction and this catches it.
        string agent = Path.Combine(RepoRoot(), "MSFSBlindAssist", "Resources",
                                    "coherent-da40-g1000-agent.js");
        Assert.True(File.Exists(agent), $"DA40 agent not found at {agent}");
        Assert.Contains("__MSFSBA_DA40G1000", File.ReadAllText(agent));
    }
}
