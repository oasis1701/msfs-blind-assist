using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Tests.Walkthroughs;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Keeps the contributor walkthroughs teaching code that compiles. The test project compiles the template
/// (<see cref="TemplatePath"/>), and these tests check what it teaches: that it starts from the base variables,
/// follows the registration conventions and never announces a button press. Design: the 2026-10-08
/// "walkthroughs compile" spec (PR 4 of the docs-restructure fix sequence).
/// </summary>
public class WalkthroughTemplateTests
{
    internal const string TemplatePath = "tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs";
    internal static readonly string[] Walkthroughs = { "docs/adding-features.md", "docs/QUICK-REFERENCE.md" };

    private static readonly string[] TemplateRegions =
    {
        "aircraft-file", "panel-variable", "monitoring-variable", "h-variable", "panel-controls", "button-state-mapping",
        "hotkey-map", "custom-hotkey-handler",
    };

    private static RegionMap Template()
        => DocRegions.Parse(File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), TemplatePath)));

    [Fact]
    public void Template_regions_are_well_formed()
    {
        RegionMap map = Template();
        List<string> problems = map.Problems.Select(p => $"{TemplatePath} {p}")
            .Concat(TemplateRegions.Where(r => !map.Names.Contains(r))
                .Select(r => $"{TemplatePath}: region '{r}' is missing, and the walkthroughs show it."))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Template_variables_include_every_base_variable()
    {
        var probe = new Probe();
        List<string> missing = probe.BaseKeys().Where(key => !probe.GetVariables().ContainsKey(key)).ToList();
        Assert.True(missing.Count == 0,
            $"{TemplatePath}: GetVariables() lacks the base variables {string.Join(", ", missing)}. BuildVariables() must "
            + "start from GetBaseVariables() and add the aircraft's own variables to it, as every aircraft does.");
    }

    [Fact]
    public void Template_variables_follow_the_registration_conventions()
    {
        var variables = new YourAircraftDefinition().GetVariables();
        var problems = new List<string>();
        foreach (var (key, v) in variables)
        {
            if (v.Name.StartsWith("L:", StringComparison.Ordinal) || v.Name.StartsWith("H:", StringComparison.Ordinal))
                problems.Add($"{key}: Name '{v.Name}' carries a prefix, but registration adds 'L:' to an L:var's name itself.");
            if (v.PressEvent.StartsWith("H:", StringComparison.Ordinal))
                problems.Add($"{key}: PressEvent '{v.PressEvent}' carries 'H:', but MobiFlight sends '(>H:<name>)' itself.");
            if (v.ReleaseEvent.StartsWith("H:", StringComparison.Ordinal))
                problems.Add($"{key}: ReleaseEvent '{v.ReleaseEvent}' carries 'H:', but MobiFlight sends '(>H:<name>)' itself.");
            if (v.LedVariable.Length > 0 && !variables.ContainsKey(v.LedVariable))
                problems.Add($"{key}: LedVariable '{v.LedVariable}' is not a variable key; it names the light's own variable by its key.");
        }
        Assert.True(problems.Count == 0, $"{TemplatePath}:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Template_button_state_mapping_stays_empty()
        => Assert.True(new YourAircraftDefinition().GetButtonStateMapping().Count == 0,
            $"{TemplatePath}: GetButtonStateMapping() must stay empty. Reading a button's state back after a press is "
            + "CORE-7's scoped exception for the FBW A320, Headwind A330 and FBW A380; a new aircraft never announces a press.");

    /// <summary>Reaches the protected base variables the template must start from.</summary>
    private sealed class Probe : YourAircraftDefinition
    {
        public IEnumerable<string> BaseKeys() => GetBaseVariables().Keys;
    }
}
