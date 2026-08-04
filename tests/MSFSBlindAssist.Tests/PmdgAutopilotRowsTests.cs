using System.Reflection;
using System.Runtime.InteropServices;
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

    // ---- every state field must resolve against the CDA struct ----
    // The varKey existence tests above can't catch a StateField typo: reads go through
    // IPMDGDataManager.GetFieldValue, which returns the 0.0 unknown-field sentinel for
    // a name it can't resolve instead of throwing — so a misspelled state field ships
    // as a button silently stuck at "Off". Mirror GetFieldValue's resolution exactly:
    // an exact-name NON-array struct field (a bare array name reads 0.0 by design), or
    // a "Base_N" suffix where Base is a marshalled array and N is inside its SizeConst.

    [Fact]
    public void Every_777_state_field_resolves_against_the_cda_struct()
    {
        foreach (var row in PMDGAutopilotRows.For777())
            AssertStateFieldResolves(typeof(MSFSBlindAssist.SimConnect.PMDG777XDataStruct), row, "777");
    }

    [Fact]
    public void Every_737_state_field_resolves_against_the_cda_struct()
    {
        foreach (var row in PMDGAutopilotRows.For737())
            AssertStateFieldResolves(typeof(MSFSBlindAssist.SimConnect.PMDGNG3DataStruct), row, "737");
    }

    private static void AssertStateFieldResolves(Type cdaStruct, ApRowSpec row, string aircraft)
    {
        if (row.StateField.Length == 0) return; // the stateless disconnects read nothing

        var exact = cdaStruct.GetField(row.StateField);
        if (exact != null && !exact.FieldType.IsArray) return;

        int cut = row.StateField.LastIndexOf('_');
        if (cut > 0 && int.TryParse(row.StateField[(cut + 1)..], out int index))
        {
            var baseField = cdaStruct.GetField(row.StateField[..cut]);
            int size = baseField?.GetCustomAttribute<MarshalAsAttribute>()?.SizeConst ?? 0;
            if (baseField != null && baseField.FieldType.IsArray && index < size) return;
        }

        Assert.Fail($"{aircraft} row '{row.Label}': state field '{row.StateField}' does not " +
            $"resolve against {cdaStruct.Name} — GetFieldValue would return the 0.0 sentinel forever");
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

    // ---- stateful toggles must not actuate blind ----
    // A Toggle press computes its flip target from the current state (current == 0
    // ? 1 : 0), so with no CDA snapshot the read is null and the press would command
    // a guessed position — the 737's 2-position branch has no IsReady guard and would
    // set the switch to 1 regardless of where it really is. The binder therefore
    // gates Toggle rows on the snapshot via IsEnabled and the window disables those
    // buttons, matching the selector combo. Momentary presses ignore the value
    // entirely and the stateless disconnects read nothing, so both stay pressable
    // while the label shows "--".

    /// <summary>Binds the single button the given row produces, against a manager
    /// that has no PMDG data manager — i.e. no CDA snapshot.</summary>
    private static MSFSBlindAssist.Forms.ToggleButtonDef BindSingleButton(
        ApRowSpec row, IReadOnlyDictionary<string, MSFSBlindAssist.SimConnect.SimVarDefinition> vars)
    {
        var (buttons, _) = MSFSBlindAssist.Forms.PMDG.PMDGAutopilotRowBinder.Bind(
            new[] { row },
            vars,
            new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero),
            (_, _) => { },
            (_, _, _) => true);
        return Assert.Single(buttons);
    }

    [Fact]
    public void Toggle_rows_disable_while_the_cda_snapshot_is_missing()
    {
        var v737 = new PMDG737Definition().GetVariables();
        var v777 = new PMDG777Definition().GetVariables();

        foreach (var (row, vars) in
            PMDGAutopilotRows.For737().Select(r => (r, v737))
                .Concat(PMDGAutopilotRows.For777().Select(r => (r, v777))))
        {
            if (row.Kind != ApRowKind.Toggle) continue;

            var btn = BindSingleButton(row, vars);
            Assert.NotNull(btn.IsEnabled);
            Assert.False(btn.IsEnabled!(), $"toggle '{row.Label}' must disable with no snapshot");
        }
    }

    [Fact]
    public void Momentary_and_stateless_rows_never_gate_on_the_snapshot()
    {
        var vars = new PMDG737Definition().GetVariables();

        var momentary = BindSingleButton(
            Assert.Single(PMDGAutopilotRows.For737(), r => r.Label == "CMD A"), vars);
        var stateless = BindSingleButton(
            Assert.Single(PMDGAutopilotRows.For737(), r => r.Label == "A/P Disconnect"), vars);

        Assert.Null(momentary.IsEnabled);
        Assert.Null(stateless.IsEnabled);
    }

    // ---- window hotkeys: mnemonics are row data ----
    // Alt-keys chosen per-window with the user (2026-08-04 spec): first AP engage
    // Alt+A; second Alt+B (737) / Alt+R (777 — "AP Right" has no B and an
    // unannounced key was rejected); Approach Alt+P; localizer Alt+O (the letter
    // exists in both "VOR LOC" and "LOC"); Bank Limit Alt+L (off the requested
    // Alt+B, which CMD B owns). Uniqueness and label-occurrence are pinned because
    // both failure modes are silent: a duplicate letter makes Alt+X activate an
    // arbitrary matching control; a letter absent from its label makes the &
    // insertion no-op so the hotkey quietly never exists.

    [Theory]
    [InlineData("CMD A", 'A')]
    [InlineData("CMD B", 'B')]
    [InlineData("Approach", 'P')]
    [InlineData("VOR LOC", 'O')]
    [InlineData("Bank Limit", 'L')]
    [InlineData("CWS A", '\0')]
    [InlineData("Disengage Bar", '\0')]
    public void Pmdg737_row_pins_its_mnemonic(string label, char mnemonic)
    {
        var row = Assert.Single(PMDGAutopilotRows.For737(), r => r.Label == label);
        Assert.Equal(mnemonic, row.Mnemonic);
    }

    [Theory]
    [InlineData("AP Left", 'A')]
    [InlineData("AP Right", 'R')]
    [InlineData("Approach", 'P')]
    [InlineData("LOC", 'O')]
    [InlineData("Bank Limit", 'L')]
    [InlineData("A/T", '\0')]
    public void Pmdg777_row_pins_its_mnemonic(string label, char mnemonic)
    {
        var row = Assert.Single(PMDGAutopilotRows.For777(), r => r.Label == label);
        Assert.Equal(mnemonic, row.Mnemonic);
    }

    [Fact]
    public void Assigned_mnemonics_are_unique_within_each_table()
    {
        foreach (var table in new[] { PMDGAutopilotRows.For737(), PMDGAutopilotRows.For777() })
        {
            var assigned = table.Where(r => r.Mnemonic != '\0')
                .Select(r => char.ToUpperInvariant(r.Mnemonic)).ToList();
            Assert.Equal(assigned.Count, assigned.Distinct().Count());
        }
    }

    [Fact]
    public void Assigned_mnemonics_occur_in_their_row_label()
    {
        foreach (var row in PMDGAutopilotRows.For737().Concat(PMDGAutopilotRows.For777()))
        {
            if (row.Mnemonic == '\0') continue;
            Assert.True(row.Label.Contains(row.Mnemonic, StringComparison.OrdinalIgnoreCase),
                $"row '{row.Label}': mnemonic '{row.Mnemonic}' not in label — the & insertion would silently no-op");
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
