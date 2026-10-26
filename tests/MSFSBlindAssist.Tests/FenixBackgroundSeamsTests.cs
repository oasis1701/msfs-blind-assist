// The Fenix definition's background seam (the cabin monitor) starts from the first sim update
// and stops in OnSwitchedAway. FenixTestHost turns it off for the whole test assembly, so the facts
// here that need it on (or need its shipped default) live apart from FenixDefinitionSeamTests.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// <c>FenixA320Definition.BackgroundSeamsEnabled</c> is process-global mutable state, and a test here turns it
/// on for a moment. xUnit runs a collection that disables parallelization after every parallel one, so no
/// other test sees it on.
/// </summary>
[CollectionDefinition("FenixBackgroundSeamsState", DisableParallelization = true)]
public class FenixBackgroundSeamsStateCollection
{
}

[Collection("FenixBackgroundSeamsState")]
public class FenixBackgroundSeamsTests
{
    [Fact]
    public void A_late_sim_update_after_the_switch_away_starts_no_background()
    {
        // On quit, OnSwitchedAway runs and Disconnect() then pumps messages for 500 ms, so a late SimConnect
        // update can still reach the definition. Its data service is disposed by then, and a background
        // started on it would only poll an inert service until the app exits. MainForm builds a new
        // definition on every switch, so a disposed service always means this instance is finished.
        var definition = new FenixA320Definition();
        definition.OnSwitchedAway();

        bool before = FenixA320Definition.BackgroundSeamsEnabled;
        FenixA320Definition.BackgroundSeamsEnabled = true;
        try
        {
            definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 3, new SpeechCapture());
            definition.ProcessSimVarUpdate("NOT_A_FENIX_CONFIG_VAR", 1, new SpeechCapture());

            Assert.False(definition.BackgroundStarted, "the background started on a disposed data service");
        }
        finally
        {
            FenixA320Definition.BackgroundSeamsEnabled = before;
            // Stops anything the update did start, so a failing run leaves no poller behind.
            definition.OnSwitchedAway();
        }
    }

    [Fact]
    public void The_shipped_default_leaves_the_background_seams_on()
    {
        // FenixTestHost writes the flag false before any test runs, so the property cannot show the shipped
        // default: read its initializer. Flipping that default would silently turn the cabin
        // background off in the app.
        string source = File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.Hooks.cs"));

        Assert.Matches(
            new Regex(@"static\s+bool\s+BackgroundSeamsEnabled\s*\{\s*get;\s*set;\s*\}\s*=\s*true\s*;"),
            source);
    }
}
