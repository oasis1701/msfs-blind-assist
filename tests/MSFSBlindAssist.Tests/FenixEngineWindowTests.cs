// Pins the Engines window's shape. Fenix engine values reach it on pool threads ([FNX-2]): the
// subscription pushes the running flags (and some numbers) as they change, and a once-a-second poll
// reads the 16 non-flag values (FenixEngineNames.Polled, FenixDataPoller). The form must never marshal
// per update (the callbacks write only the snapshot, a 1 s timer reads it), must dispose its
// subscription, and must read the Fenix's own values, never the stock engine SimVars, which run a
// generic engine model on the Fenix. Source scans, because the stream and the aircraft switch only
// show in the live sim.
using MSFSBlindAssist.Forms.Fenix;

namespace MSFSBlindAssist.Tests;

public class FenixEngineWindowTests
{
    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(parts).ToArray()));

    [Fact]
    public void Registry_lists_the_Engines_window_once()
    {
        var e = Assert.Single(FenixWindowRegistry.Build(), x => x.Name == "Engines");
        Assert.Equal("N1, EGT, N2, fuel flow, oil and air temperatures, from the Fenix's own values", e.Description);
    }

    [Fact]
    public void Engines_window_never_marshals_and_cleans_up()
    {
        string form = Source("Forms", "Fenix", "FenixEnginesForm.cs");
        Assert.DoesNotContain("BeginInvoke", form);
        Assert.DoesNotContain("Invoke(", form);
        Assert.Contains("FenixEngineNames.All", form);
        Assert.Contains("Interval = 1000", form);
        Assert.Contains("protected override void Dispose(bool disposing)", form);
    }

    [Fact]
    public void Engine_readouts_never_use_stock_engine_simvars()
    {
        var files = new List<string> { Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "Fenix", "FenixEnginesForm.cs") };
        files.AddRange(Directory.GetFiles(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Services"), "FenixEngine*.cs"));
        Assert.True(files.Count >= 6, "the form and the five FenixEngine services");
        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            foreach (string banned in new[] { "TURB ENG", "GENERAL ENG", "ENG EXHAUST", "FUEL FLOW PPH", "AMBIENT TEMPERATURE", "localhost" })
                Assert.False(source.Contains(banned, StringComparison.Ordinal), $"{Path.GetFileName(file)} contains \"{banned}\"");
        }
    }
}
