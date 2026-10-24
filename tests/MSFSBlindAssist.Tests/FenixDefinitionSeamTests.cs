// FenixA320Definition is a partial class with optional `partial void` seams, so the variant and cabin
// code each implement theirs in their own file without editing the 13,000-line main file. With no
// implementation a partial void call compiles to nothing (the engine background seams have none): the
// definition must behave exactly as it did before the seams existed.
//
// The facts here survive other partials adding and removing definitions, so none of them pins a total
// count.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixDefinitionSeamTests
{
    private static readonly FenixA320Definition Definition = new();

    [Fact]
    public void Panel_controls_are_visible_when_no_variant_hides_them()
        => Assert.True(Definition.IsPanelControlVisible("S_OH_ELEC_BAT1", null));

    [Fact]
    public void No_display_override_exists_without_a_variant_implementation()
    {
        Assert.False(Definition.TryGetDisplayOverride("S_OH_ELEC_BAT1", 1, out string text));
        Assert.Equal("", text);
    }

    [Theory]
    [InlineData("S_OH_ELEC_BAT1")]
    [InlineData("N_FCU_SPEED")]
    [InlineData("S_FC_FLAPS")]
    [InlineData("I_MIP_MASTER_WARNING_CAPT")]
    public void Long_standing_variables_are_still_registered(string key)
        => Assert.Contains(key, Definition.GetVariables().Keys);

    [Fact]
    public void Every_panel_control_names_a_registered_variable()
    {
        var variables = Definition.GetVariables();
        // A panel name can itself contain a slash ("GPWS/Terrain"), so pair the two, never split a string.
        var unknown = Definition.GetPanelControls()
            .SelectMany(panel => panel.Value.Select(key => (Panel: panel.Key, Key: key)))
            .Where(entry => !variables.ContainsKey(entry.Key))
            .Select(entry => $"{entry.Panel} -> {entry.Key}")
            .ToList();
        Assert.True(unknown.Count == 0, "Panel controls without a variable: " + string.Join(", ", unknown));
    }

    [Fact]
    public async Task A_switched_away_definition_keeps_its_disposed_data_service()
    {
        // MainForm makes a new definition on every switch, so a late callback touching the old one's
        // DataService must get the disposed, inert service back, never a fresh live one that retries
        // its socket for the rest of the session. The disposed service answers without a request.
        var definition = new FenixA320Definition();
        var service = definition.DataService;

        definition.OnSwitchedAway();

        Assert.Same(service, definition.DataService);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, (await definition.DataService.ReadAsync("x")).Kind);
    }

    [Fact]
    public async Task A_switched_away_definition_never_builds_a_live_data_service()
    {
        // The same holds when nothing touched DataService before the switch: a late callback must not
        // build a live one then. Disposed, so the read below answers without a request.
        var definition = new FenixA320Definition();

        definition.OnSwitchedAway();

        Assert.True(definition.DataService.IsDisposed, "a live data service was built after the switch");
        Assert.Same(definition.DataService, definition.DataService);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, (await definition.DataService.ReadAsync("x")).Kind);
    }

    [Fact]
    public void A_sim_update_starts_no_background_seam_in_the_tests()
    {
        // FenixTestHost turns the seams off for the whole test assembly: the cabin seam polls
        // 127.0.0.1:8083 live, and the pilot's sim may be answering there.
        Assert.False(FenixA320Definition.BackgroundSeamsEnabled, "FenixTestHost's module initializer did not run");
        var definition = new FenixA320Definition();

        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 3, new SpeechCapture());
        definition.ProcessSimVarUpdate("NOT_A_FENIX_CONFIG_VAR", 1, new SpeechCapture());

        Assert.False(definition.BackgroundStarted);
    }

    [Fact]
    public void The_definition_is_partial_and_calls_every_seam_from_its_main_file()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs"));
        Assert.Contains("public partial class FenixA320Definition", source);
        foreach (string seam in new[]
        {
            "AddVariantVariables(", "AddVariantPanelControls(", "AddVariantPanelStructure(",
            "AddVariantPanelDisplayVariables(", "HandleVariantUIVariableSet(", "OnVariantSimVarUpdate(",
        })
            Assert.True(source.Contains(seam), $"FenixA320Definition.cs never calls {seam}");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
