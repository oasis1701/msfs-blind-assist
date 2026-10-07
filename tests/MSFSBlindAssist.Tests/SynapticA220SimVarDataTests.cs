using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pin tests for the generated documented-SimVar table
/// (tools/a220-gen/generate_a220_simvars.py from the vendored simvars.mdx).
/// A regeneration that changes counts, enum labels, or the lamp/aural
/// classification should fail here first and be reviewed, not silently committed.
/// </summary>
public class SynapticA220SimVarDataTests
{
    [Fact]
    public void Table_HasPinnedCounts()
    {
        Assert.Equal(448, SynapticA220SimVarData.Vars.Length);
        Assert.Equal(53, SynapticA220SimVarData.LampVars.Length);
        Assert.Equal(88, SynapticA220SimVarData.AuralVars.Length);
    }

    [Theory]
    [InlineData("L:A22X APU Switch", new[] { "Off", "Run", "Start" })]
    [InlineData("L:A22X Taxi Lights", new[] { "Off", "Narrow", "Wide" })]
    [InlineData("L:A22X Eng Start Mode", new[] { "L Eng Crank", "Auto", "R Eng Crank" })]
    [InlineData("L:A22X Manual Transfer", new[] { "Off", "Right", "Center", "Left" })]
    [InlineData("L:A22X Fwd Cargo Air", new[] { "Off", "Vent", "Lo Heat", "Hi Heat" })]
    [InlineData("L:A22X Bus Isolation Mode", new[] { "Main", "Auto", "Ess" })]
    public void EnumLabels_MatchOfficialDocs(string name, string[] expected)
    {
        var v = SynapticA220SimVarData.Vars.Single(x => x.Name == name);
        Assert.Equal(expected, v.EnumLabels);
    }

    [Fact]
    public void LampAndAuralClassification_IsByNameConvention()
    {
        foreach (var v in SynapticA220SimVarData.Vars)
            Assert.Equal(v.Name.EndsWith(" Lamp", StringComparison.Ordinal), v.IsLamp);
        Assert.Contains("L:A22X L Gen Fail Lamp", SynapticA220SimVarData.LampVars);
        Assert.Contains("L:A22X Aural V1", SynapticA220SimVarData.AuralVars);
        // The aural-warning-inhibit SWITCH (and lamp/self-test) share the "Aural"
        // name prefix but are real controls, not native playback flags.
        Assert.DoesNotContain("L:A22X Aural Warn Inhibit", SynapticA220SimVarData.AuralVars);
        Assert.DoesNotContain("L:A22X Aural Warn Inhibit Lamp", SynapticA220SimVarData.AuralVars);
        Assert.DoesNotContain("L:A22X Aural Internal Test", SynapticA220SimVarData.AuralVars);
    }

    [Fact]
    public void PlaceholderExpansion_ProducedConcreteNames()
    {
        Assert.Contains(SynapticA220SimVarData.Vars, v => v.Name == "L:A22X Engine 1 Reverser");
        Assert.Contains(SynapticA220SimVarData.Vars, v => v.Name == "L:A22X Engine 2 Reverser");
        Assert.Contains(SynapticA220SimVarData.Vars, v => v.Name == "L:A22X AC Ess Bus Voltage");
        Assert.Contains(SynapticA220SimVarData.Vars, v => v.Name == "L:A22X L Altimeter STD");
        Assert.DoesNotContain(SynapticA220SimVarData.Vars, v => v.Name.Contains('{'));
    }
}
