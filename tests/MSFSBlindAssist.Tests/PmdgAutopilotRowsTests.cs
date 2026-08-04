using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the PMDG Ctrl+P autopilot window's row tables.
/// These pin the varKey -> state-field pairings, which are the failure mode with
/// history in this codebase: PMDG event names are swapped relative to annunciator
/// array indices in places, so an off-by-one here produces a button that silently
/// reports the wrong engine/side rather than failing loudly.
/// </summary>
public class PmdgAutopilotRowsTests
{
    [Fact]
    public void Pmdg737_exposes_a_yoke_ap_disconnect_variable()
    {
        var vars = new PMDG737Definition().GetVariables();

        Assert.True(vars.ContainsKey("YOKE_APDisc"));
        var def = vars["YOKE_APDisc"];
        Assert.Equal("YOKE_APDisc", def.Name);
        Assert.True(def.IsMomentary);
        Assert.True(def.RenderAsButton);
    }

    // ---- 777 row table ----

    [Theory]
    [InlineData("AP Left", "MCP_AP_L", "MCP_annunAP_0")]
    [InlineData("AP Right", "MCP_AP_R", "MCP_annunAP_1")]
    [InlineData("F/D Left", "MCP_FD_L", "MCP_FD_Sw_On_0")]
    [InlineData("F/D Right", "MCP_FD_R", "MCP_FD_Sw_On_1")]
    [InlineData("A/T Arm Left", "MCP_ATArm_L", "MCP_ATArm_Sw_On_0")]
    [InlineData("A/T Arm Right", "MCP_ATArm_R", "MCP_ATArm_Sw_On_1")]
    [InlineData("A/T", "MCP_AT", "MCP_annunAT")]
    [InlineData("Disengage Bar", "MCP_DisengageBar", "MCP_DisengageBar")]
    [InlineData("Bank Limit", "MCP_BankLimitSel", "MCP_BankLimitSel")]
    public void Pmdg777_row_pins_its_varkey_and_state_field(string label, string varKey, string stateField)
    {
        var row = Assert.Single(PMDGAutopilotRows.For777(), r => r.Label == label);
        Assert.Equal(varKey, row.VarKey);
        Assert.Equal(stateField, row.StateField);
    }

    // ---- 737 row table ----

    [Theory]
    [InlineData("CMD A", "MCP_CmdA", "MCP_annunCMD_A")]
    [InlineData("CMD B", "MCP_CmdB", "MCP_annunCMD_B")]
    [InlineData("CWS A", "MCP_CwsA", "MCP_annunCWS_A")]
    [InlineData("CWS B", "MCP_CwsB", "MCP_annunCWS_B")]
    [InlineData("F/D Captain", "MCP_FDSw_0", "MCP_FDSw_0")]
    [InlineData("F/D First Officer", "MCP_FDSw_1", "MCP_FDSw_1")]
    [InlineData("A/T Arm", "MCP_ATArmSw", "MCP_ATArmSw")]
    [InlineData("Disengage Bar", "MCP_DisengageBar", "MCP_DisengageBar")]
    [InlineData("Bank Limit", "MCP_BankLimitSel", "MCP_BankLimitSel")]
    public void Pmdg737_row_pins_its_varkey_and_state_field(string label, string varKey, string stateField)
    {
        var row = Assert.Single(PMDGAutopilotRows.For737(), r => r.Label == label);
        Assert.Equal(varKey, row.VarKey);
        Assert.Equal(stateField, row.StateField);
    }

    // ---- every row must resolve against the live variable dictionary ----
    // A typo here would otherwise ship as a dead button that no test notices.

    [Fact]
    public void Every_777_row_varkey_exists_in_the_definition()
    {
        var vars = new PMDG777Definition().GetVariables();
        foreach (var row in PMDGAutopilotRows.For777())
            Assert.True(vars.ContainsKey(row.VarKey), $"777 row '{row.Label}' names unknown varKey '{row.VarKey}'");
    }

    [Fact]
    public void Every_737_row_varkey_exists_in_the_definition()
    {
        var vars = new PMDG737Definition().GetVariables();
        foreach (var row in PMDGAutopilotRows.For737())
            Assert.True(vars.ContainsKey(row.VarKey), $"737 row '{row.Label}' names unknown varKey '{row.VarKey}'");
    }

    // The 737's momentary rows read an annunciator that differs from the varDef Name;
    // the def records that pairing in StateVariable, so the two must agree.

    [Fact]
    public void Pmdg737_momentary_rows_agree_with_the_definitions_state_variable()
    {
        var vars = new PMDG737Definition().GetVariables();
        foreach (var row in PMDGAutopilotRows.For737())
        {
            if (row.Kind != ApRowKind.Momentary || row.StateField.Length == 0) continue;
            Assert.Equal(vars[row.VarKey].StateVariable, row.StateField);
        }
    }

    // Selector rows must actually be multi-position, or they belong on a button.

    [Fact]
    public void Selector_rows_have_at_least_three_positions()
    {
        var v777 = new PMDG777Definition().GetVariables();
        foreach (var row in PMDGAutopilotRows.For777())
            if (row.Kind == ApRowKind.Selector)
                Assert.True(v777[row.VarKey].ValueDescriptions.Count >= 3);

        var v737 = new PMDG737Definition().GetVariables();
        foreach (var row in PMDGAutopilotRows.For737())
            if (row.Kind == ApRowKind.Selector)
                Assert.True(v737[row.VarKey].ValueDescriptions.Count >= 3);
    }
}
