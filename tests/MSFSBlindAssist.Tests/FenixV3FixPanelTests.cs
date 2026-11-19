// The overhead ELAC/SEC/FAC pushbuttons were listed under a second ["Flight Controls"] key in
// BuildPanelControls. A C# indexer initializer keeps only the last of two equal keys, so the
// overhead list vanished and the pedestal's controls were all that was left under that name.

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FenixV3FixPanelTests
{
    private readonly FenixA320Definition def = new();

    [Fact]
    public void Overhead_computers_have_their_own_panel()
    {
        Assert.Equal(
            new[]
            {
                "S_OH_FLT_CTL_ELAC_1", "S_OH_FLT_CTL_ELAC_2", "S_OH_FLT_CTL_SEC_1", "S_OH_FLT_CTL_SEC_2",
                "S_OH_FLT_CTL_SEC_3", "S_OH_FLT_CTL_FAC_1", "S_OH_FLT_CTL_FAC_2",
            },
            def.GetPanelControls()["Flight Control Computers"]);
    }

    [Fact]
    public void Overhead_lists_the_computers_after_GPWS()
    {
        var overhead = def.GetPanelStructure()["Overhead"];
        Assert.Equal(overhead.IndexOf("GPWS") + 1, overhead.IndexOf("Flight Control Computers"));
        Assert.True(overhead.IndexOf("GPWS") >= 0);
    }

    [Fact]
    public void Pedestal_flight_controls_are_unchanged()
    {
        var controls = def.GetPanelControls()["Flight Controls"];
        Assert.Equal(14, controls.Count);
        Assert.Equal("S_MIP_PARKING_BRAKE", controls.First());
        Assert.Equal("S_FC_FO_INST_DISCONNECT", controls.Last());
    }

    [Fact]
    public void Every_panel_is_in_a_section()
    {
        var listed = def.GetPanelStructure().Values.SelectMany(v => v).ToHashSet();
        foreach (var panel in def.GetPanelControls().Keys)
            Assert.True(listed.Contains(panel), panel);
    }

    [Fact]
    public void BuildPanelControls_names_each_panel_once()
    {
        string source = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
            "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs"));
        int start = source.IndexOf("BuildPanelControls()", StringComparison.Ordinal);
        int end = source.IndexOf("AddVariantPanelControls(panels)", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var names = Regex.Matches(source[start..end], @"\[""([^""]+)""\]\s*=\s*new List<string>")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(names);
        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}
