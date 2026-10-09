using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Tests.Walkthroughs;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Keeps the contributor walkthroughs (<see cref="Walkthroughs"/>) teaching code that compiles and naming code that
/// exists. Every C# block there is either a word-for-word copy of a region of the compiled template
/// (<see cref="TemplatePath"/>), marked <c>&lt;!-- template: region --&gt;</c>, or a fragment of other code, marked
/// <c>&lt;!-- fragment: path.cs#identifier --&gt;</c>. The template's own tests check what it teaches: that it starts
/// from the base variables, follows the registration conventions and never announces a button press. Design: the
/// 2026-10-08 "walkthroughs compile" spec (PR 4 of the docs-restructure fix sequence).
/// </summary>
public class WalkthroughTemplateTests
{
    internal const string TemplatePath = "tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs";
    internal static readonly string[] Walkthroughs = { "docs/adding-features.md", "docs/QUICK-REFERENCE.md" };

    private static string Root => ClaudeContextBudgetTests.RepoRoot();

    private static RegionMap Template() => DocRegions.Parse(File.ReadAllText(Path.Combine(Root, TemplatePath)));

    private static IEnumerable<(string Doc, DocBlock Block)> WalkthroughBlocks()
        => Walkthroughs.SelectMany(doc => DocBlocks.CSharpBlocks(File.ReadAllText(Path.Combine(Root, doc))).Select(block => (doc, block)));

    private static DocBlock BlockAt(string doc, int fenceLine)
        => DocBlocks.CSharpBlocks(File.ReadAllText(Path.Combine(Root, doc))).Single(block => block.FenceLine == fenceLine);

    public static IEnumerable<object[]> TemplateBlocks()
        => WalkthroughBlocks().Where(x => x.Block.Kind == MarkerKind.Template)
            .Select(x => new object[] { x.Doc, x.Block.FenceLine, x.Block.Argument });

    public static IEnumerable<object[]> FragmentBlocks()
        => WalkthroughBlocks().Where(x => x.Block.Kind == MarkerKind.Fragment)
            .Select(x => new object[] { x.Doc, x.Block.FenceLine, x.Block.Argument });

    [Theory]
    [MemberData(nameof(TemplateBlocks))]
    public void Template_blocks_match_their_regions(string doc, int line, string region)
    {
        RegionMap map = Template();
        Assert.True(map.Names.Contains(region),
            $"{doc}:{line}: the marker names region '{region}', which {TemplatePath} does not declare. Its regions are: "
            + string.Join(", ", map.Names.OrderBy(name => name, StringComparer.Ordinal)) + ".");
        string expected = map.Render(region);
        Assert.True(BlockAt(doc, line).Text == expected,
            $"{doc}:{line}: this block differs from region '{region}' of {TemplatePath}. Replace its lines with:\n"
            + $"```csharp\n{expected}\n```");
    }

    [Theory]
    [MemberData(nameof(FragmentBlocks))]
    public void Fragment_blocks_name_code_that_exists(string doc, int line, string argument)
    {
        int hash = argument.LastIndexOf('#');
        string path = hash > 0 ? argument[..hash] : "";
        string identifier = hash > 0 ? argument[(hash + 1)..] : "";
        Assert.True(path.EndsWith(".cs", StringComparison.Ordinal) && !path.Contains('\\') && identifier.Length > 0,
            $"{doc}:{line}: the fragment marker '{argument}' must read '<repo-relative path>.cs#<identifier>', with forward slashes.");
        string file = Path.Combine(Root, path);
        Assert.True(File.Exists(file),
            $"{doc}:{line}: {path} does not exist. Point the marker at the file that holds the code now, and update the block from it.");
        Assert.True(DocBlocks.ContainsWord(File.ReadAllText(file), identifier),
            $"{doc}:{line}: {path} no longer contains '{identifier}'. The code was renamed or moved: update the block from where "
            + "it lives now, and its marker.");
        Assert.True(DocBlocks.ContainsWord(BlockAt(doc, line).Text, identifier),
            $"{doc}:{line}: the block does not show '{identifier}', which its marker names. Name an identifier the block and "
            + $"{path} share.");
    }

    [Fact]
    public void Every_csharp_block_is_marked()
    {
        List<string> unmarked = WalkthroughBlocks().Where(x => x.Block.Kind == MarkerKind.None)
            .Select(x => $"{x.Doc}:{x.Block.FenceLine}: a C# block with no marker (the line above its fence: "
                + $"'{x.Block.LineAboveFence}'). Put <!-- template: <region> --> above the fence for a region of {TemplatePath}, "
                + "or <!-- fragment: <path>.cs#<identifier> --> for code the template cannot hold.")
            .ToList();
        Assert.True(unmarked.Count == 0, $"{unmarked.Count} C# blocks have no marker:\n" + string.Join("\n", unmarked));
    }

    [Fact]
    public void Every_region_is_shown_in_a_walkthrough()
    {
        HashSet<string> shown = WalkthroughBlocks().Where(x => x.Block.Kind == MarkerKind.Template)
            .Select(x => x.Block.Argument).ToHashSet(StringComparer.Ordinal);
        List<string> unshown = Template().Names.Where(name => !shown.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.True(unshown.Count == 0,
            $"{TemplatePath}: no walkthrough shows the regions {string.Join(", ", unshown)}. Show each in a block marked "
            + "<!-- template: <region> -->, or drop its markers.");
    }

    [Fact]
    public void Template_regions_are_well_formed()
    {
        RegionMap map = Template();
        List<string> problems = map.Problems.Select(p => $"{TemplatePath} {p}")
            .Concat(WalkthroughBlocks().Where(x => x.Block.Kind == MarkerKind.Template && !map.Names.Contains(x.Block.Argument))
                .Select(x => $"{x.Doc}:{x.Block.FenceLine}: the marker names region '{x.Block.Argument}', which {TemplatePath} "
                    + "does not declare."))
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
            else if (v.LedVariable.Length > 0
                && variables[v.LedVariable].UpdateFrequency == global::MSFSBlindAssist.SimConnect.UpdateFrequency.Never)
                problems.Add($"{key}: LedVariable '{v.LedVariable}' names a variable that is never registered (UpdateFrequency.Never), "
                    + "so the read after a press returns nothing; give the light UpdateFrequency.OnRequest, or leave LedVariable out.");
        }
        Assert.True(problems.Count == 0, $"{TemplatePath}:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Template_button_state_mapping_stays_empty()
        => Assert.True(new YourAircraftDefinition().GetButtonStateMapping().Count == 0,
            $"{TemplatePath}: GetButtonStateMapping() must stay empty in the template. An entry is CORE-7's button read-back, "
            + "which only the FBW A320, Headwind A330 and FBW A380 have; a new aircraft takes one only as the owner's deliberate "
            + "choice, for a button whose effect the pilot cannot otherwise hear.");

    /// <summary>Reaches the protected base variables the template must start from.</summary>
    private sealed class Probe : YourAircraftDefinition
    {
        public IEnumerable<string> BaseKeys() => GetBaseVariables().Keys;
    }
}
