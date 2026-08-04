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
    [InlineData("Approach", "MCP_APP", "MCP_annunAPP")]
    [InlineData("LOC", "MCP_LOC", "MCP_annunLOC")]
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
    [InlineData("Approach", "MCP_AppBtn", "MCP_annunAPP")]
    [InlineData("VOR LOC", "MCP_VorLoc", "MCP_annunVOR_LOC")]
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

    // ---- echo suppression must cover every key that can announce the row ----
    // The binder marks the UI-set echo under BOTH VarKey and StateField because the two
    // aircraft are asymmetric (see PMDGAutopilotRowBinder.MarkEcho). These pin that,
    // because the failure mode is silent: marking only VarKey leaves the 737's momentary
    // rows double-announcing while the 777 stays correct, so nothing fails loudly.

    /// <summary>Presses the single button the given row binds to, returning every
    /// (key, value) pair the binder marked for echo suppression. No data manager is
    /// attached, so the row reads no state and the expected result is a plain engage.</summary>
    private static List<(string Key, double Value)> EchoKeysForPressing(
        ApRowSpec row, IReadOnlyDictionary<string, MSFSBlindAssist.SimConnect.SimVarDefinition> vars)
    {
        var marked = new List<(string, double)>();
        var (buttons, _) = MSFSBlindAssist.Forms.PMDG.PMDGAutopilotRowBinder.Bind(
            new[] { row },
            vars,
            new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero),
            (key, value) => marked.Add((key, value)),
            (_, _, _) => true);

        Assert.Single(buttons).OnPressed();
        return marked;
    }

    [Fact]
    public void Pmdg737_momentary_row_marks_the_echo_under_its_annunciator_too()
    {
        var vars = new PMDG737Definition().GetVariables();
        var row = Assert.Single(PMDGAutopilotRows.For737(), r => r.Label == "CMD A");

        var marked = EchoKeysForPressing(row, vars);

        // "MCP_CmdA" is declared Momentary with UpdateFrequency.Never and never raises an
        // event, so an echo under it alone is DEAD. The announcement comes from the
        // separate "MCP_annunCMD_A" entry, which must be marked or CMD A double-announces.
        Assert.Contains(("MCP_CmdA", 1d), marked);
        Assert.Contains(("MCP_annunCMD_A", 1d), marked);
    }

    [Fact]
    public void Pmdg777_momentary_row_marks_the_echo_under_its_varkey()
    {
        var vars = new PMDG777Definition().GetVariables();
        var row = Assert.Single(PMDGAutopilotRows.For777(), r => r.Label == "AP Left");

        var marked = EchoKeysForPressing(row, vars);

        // On the 777 the VarKey IS the announced variable (its varDef.Name is the CDA
        // field), so this is the load-bearing mark. The extra StateField mark is inert
        // here — "MCP_annunAP_0" is a varDef.Name, not a dictionary key.
        Assert.Contains(("MCP_AP_L", 1d), marked);
    }

    [Fact]
    public void A_row_whose_state_field_equals_its_varkey_marks_the_echo_once()
    {
        var vars = new PMDG737Definition().GetVariables();
        var row = Assert.Single(PMDGAutopilotRows.For737(), r => r.Label == "A/T Arm");
        Assert.Equal(row.VarKey, row.StateField); // precondition for this case

        Assert.Single(EchoKeysForPressing(row, vars));
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
