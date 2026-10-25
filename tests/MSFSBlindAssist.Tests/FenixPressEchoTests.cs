// Pins what the Fenix A320 says, and keeps quiet, right after the pilot's own press (CORE-7), as measured on
// 2026-10-24 (Fenix 3.0.0.7454, A321 IAE, NVDA):
//
// - Econ Flow and ACT Pump: the light only mirrors the switch, so the app said "Econ Flow Lower: On" right
//   after NVDA said "On". Each switch names its light as UiEchoVariable; MainForm's value-matched UI-set echo
//   gate then swallows that one change and still speaks a change from anywhere else;
// - the EFIS FD and LS buttons: the lamps are the buttons' own "on" lights, never a fault. They are named as
//   the button the pilot presses, and the four buttons (only those) opt in to PressTogglesStateVariable, so a
//   panel press marks the lamp with the value the press leaves, through the same gate.
//
// MainForm itself is sim-facing and has no seam: the gate's value match is pinned by the pure
// SimVarDefinition.StateVariableAfterPress and by source scans of the two MainForm call sites.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixPressEchoTests
{
    private static readonly FenixA320Definition Fenix = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Fenix.GetVariables();

    private static readonly (string Button, string Lamp, string Name)[] EfisButtons =
    {
        ("S_FCU_EFIS1_FD_PRESS", "I_FCU_EFIS1_FD", "EFIS Left FD"),
        ("S_FCU_EFIS1_LS_PRESS", "I_FCU_EFIS1_LS", "EFIS Left LS"),
        ("S_FCU_EFIS2_FD_PRESS", "I_FCU_EFIS2_FD", "EFIS Right FD"),
        ("S_FCU_EFIS2_LS_PRESS", "I_FCU_EFIS2_LS", "EFIS Right LS"),
    };

    public static IEnumerable<object[]> Efis() => EfisButtons.Select(b => new object[] { b.Button, b.Lamp, b.Name });

    // ---- Econ Flow and ACT Pump ----

    [Theory]
    [InlineData("S_OH_PNEUMATIC_ECON_FLOW", "I_OH_PNEUMATIC_ECON_FLOW_L")]
    [InlineData("S_OH_FUEL_ACT", "I_OH_FUEL_ACT_L")]
    public void The_switch_names_the_light_that_only_mirrors_it(string switchKey, string light)
    {
        Assert.Equal(light, Vars[switchKey].UiEchoVariable);
        // The partner is an announced light that reads back the same 0/1 as the switch, which the value-matched
        // gate needs; Every_ui_echo_partner_is_an_announced_light_with_the_same_values checks it for every aircraft.
        Assert.Equal(Vars[switchKey].ValueDescriptions.Keys.OrderBy(k => k), Vars[light].ValueDescriptions.Keys.OrderBy(k => k));
        Assert.True(Vars[light].IsAnnounced);
        Assert.Equal(UpdateFrequency.Continuous, Vars[light].UpdateFrequency);
    }

    [Fact]
    public void The_upper_ACT_light_never_lit_with_the_switch_so_it_is_no_echo_partner()
    {
        Assert.DoesNotContain(Vars.Values, v => v.UiEchoVariable == "I_OH_FUEL_ACT_U");
        Assert.Equal("ACT Pump Upper", Vars["I_OH_FUEL_ACT_U"].DisplayName);   // keeps its position name (FNX-7)
    }

    [Fact]
    public void The_Econ_Flow_and_ACT_echo_is_value_matched()
    {
        // CORE-9: the generic gate stays value-matched. UiEchoMatchesAnyValue would swallow ANY change of the light
        // inside the window, including the opposite one from the virtual cockpit.
        foreach (var key in new[] { "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_FUEL_ACT", "I_OH_PNEUMATIC_ECON_FLOW_L", "I_OH_FUEL_ACT_L" })
            Assert.False(Vars[key].UiEchoMatchesAnyValue, key);
    }

    // ---- The FD and LS buttons and their lamps ----

    [Theory, MemberData(nameof(Efis))]
    public void The_lamp_is_named_as_its_button_and_says_off_or_on(string button, string lamp, string name)
    {
        Assert.Equal(name, Vars[button].DisplayName);
        Assert.Equal(name, Vars[lamp].DisplayName);
        Assert.Equal(lamp, Vars[lamp].Name);
        Assert.Equal(lamp, Vars[button].StateVariable);
        Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, Vars[lamp].ValueDescriptions);
        Assert.True(Vars[lamp].IsAnnounced);
        Assert.Equal(UpdateFrequency.Continuous, Vars[lamp].UpdateFrequency);
        Assert.DoesNotContain("Fault", Vars[lamp].DisplayName);
    }

    [Fact]
    public void The_lamps_are_announced_variables_not_panel_controls()
    {
        var panelKeys = Fenix.GetPanelControls().Values.SelectMany(p => p).ToHashSet();
        foreach (var (button, lamp, _) in EfisButtons)
        {
            Assert.Contains(button, panelKeys);
            Assert.DoesNotContain(lamp, panelKeys);
        }
    }

    [Theory]
    [InlineData("EFIS Left")]
    [InlineData("EFIS Right")]
    public void The_efis_panels_repeat_no_label(string panel)
    {
        var names = Fenix.GetPanelControls()[panel].Select(k => Vars[k].DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void The_lamps_keep_their_variable_keys_so_a_Ctrl_M_mute_still_applies()
    {
        foreach (var lamp in new[] { "I_FCU_EFIS1_FD", "I_FCU_EFIS1_LS", "I_FCU_EFIS2_FD", "I_FCU_EFIS2_LS" })
            Assert.True(Vars.ContainsKey(lamp), lamp);
    }

    [Fact]
    public void Exactly_the_four_FD_and_LS_buttons_opt_in_to_the_press_echo()
    {
        var optedIn = Vars.Where(kv => kv.Value.PressTogglesStateVariable).Select(kv => kv.Key).OrderBy(k => k).ToList();
        Assert.Equal(EfisButtons.Select(b => b.Button).OrderBy(k => k), optedIn);
        // Bus Tie's double announcement (the reader's new label, then its OFF light) stays: the owner's choice, 2026-10-22.
        Assert.False(Vars["S_OH_ELEC_BUS_TIE"].PressTogglesStateVariable);
    }

    [Theory, MemberData(nameof(Efis))]
    public void A_press_marks_the_value_the_lamp_will_read(string button, string lamp, string _)
    {
        var def = Vars[button];
        Assert.Equal(1.0, def.StateVariableAfterPress(0));   // dark lamp: the press lights it
        Assert.Equal(0.0, def.StateVariableAfterPress(1));   // lit lamp: the press turns it off
        // The marked value is the one the lamp's own announcement would carry, so the value-matched gate swallows
        // exactly that change ("<name>: On" after a press of a dark lamp) and no other.
        Assert.Equal("On", Vars[lamp].ValueDescriptions[def.StateVariableAfterPress(0)!.Value]);
        Assert.Equal("Off", Vars[lamp].ValueDescriptions[def.StateVariableAfterPress(1)!.Value]);
    }

    [Fact]
    public void A_press_of_a_lamp_that_has_not_been_read_marks_nothing()
    {
        var def = Vars["S_FCU_EFIS1_LS_PRESS"];
        Assert.Null(def.StateVariableAfterPress(null));
        Assert.Null(def.StateVariableAfterPress(double.NaN));
    }

    [Fact]
    public void A_control_that_did_not_opt_in_or_has_no_lamp_marks_nothing()
    {
        // Bus Tie's lamp is an OFF legend and its announcement stays; a button with no lamp has nothing to mark.
        Assert.Null(Vars["S_OH_ELEC_BUS_TIE"].StateVariableAfterPress(0));
        Assert.Null(new SimVarDefinition { PressTogglesStateVariable = true }.StateVariableAfterPress(0));
        Assert.Null(new SimVarDefinition { StateVariable = "I_X" }.StateVariableAfterPress(0));
    }

    [Fact]
    public void Any_lit_value_goes_dark_on_a_press()
    {
        var def = new SimVarDefinition { PressTogglesStateVariable = true, StateVariable = "I_X" };
        Assert.Equal(0.0, def.StateVariableAfterPress(2));
        Assert.Equal(0.0, def.StateVariableAfterPress(0.5));
        Assert.Equal(1.0, def.StateVariableAfterPress(0.0));
    }

    [Fact]
    public void The_lamps_use_the_value_matched_gate()
    {
        // CORE-9: no UiEchoMatchesAnyValue, so a change to the lamp the pilot did not cause with this press (the other
        // side through the FD/LS linking option, the First Officer, a click in the virtual cockpit) still speaks.
        foreach (var (button, lamp, _) in EfisButtons)
        {
            Assert.False(Vars[lamp].UiEchoMatchesAnyValue, lamp);
            Assert.False(Vars[button].UiEchoMatchesAnyValue, button);
            Assert.Null(Vars[button].UiEchoVariable);   // the mark goes to the StateVariable, never a second partner
        }
    }

    // ---- MainForm's two call sites, which have no seam ----

    private static string Src(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(p).ToArray()));

    [Fact]
    public void The_panel_button_marks_the_lamp_before_it_sends_the_press()
    {
        string panel = Src("MainForm.PanelBuilder.cs");
        int mark = panel.IndexOf("MarkStateLightEchoForPress(varDef);", StringComparison.Ordinal);
        Assert.True(mark > 0, "the RenderAsButton click does not mark the state light");
        int send = panel.IndexOf("currentAircraft.HandleUIVariableSet(varKey, 1, varDef", mark, StringComparison.Ordinal);
        Assert.True(send > mark, "the mark must come before the press is handed to the aircraft");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(panel, @"MarkStateLightEchoForPress\("));
    }

    [Fact]
    public void The_mark_goes_through_the_shared_value_matched_gate()
    {
        string main = Src("MainForm.cs");
        int at = main.IndexOf("private void MarkStateLightEchoForPress", StringComparison.Ordinal);
        Assert.True(at > 0);
        string body = main[at..main.IndexOf("/// <summary>", at, StringComparison.Ordinal)];
        Assert.Contains("MarkUiSet(buttonDef.StateVariable, expected)", body);
        Assert.Contains("StateVariableAfterPress(cached)", body);
        Assert.DoesNotContain("_uiSetEcho[", body);   // never a second, differently matched record
    }
}
