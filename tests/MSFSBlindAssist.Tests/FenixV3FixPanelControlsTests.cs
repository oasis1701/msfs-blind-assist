// Fenix panel controls that moved differently from the v3 cockpit (panel sweep, measured live 2026-10-21):
// momentary buttons now press and release, knob counters step from the sim's own value, baro STD is a
// pull and a push, spring-loaded switches return to the middle, and a few position labels follow the model.
// Handlers are source scans (a write needs a connected SimConnect), the FoFbwUnclaimedEventKeyTests technique.

using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FenixV3FixPanelControlsTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public FenixV3FixPanelControlsTests() => CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static readonly FenixA320Definition Def = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Def.GetVariables();
    private static readonly Dictionary<string, List<string>> Panels = Def.GetPanelControls();

    private static string DefinitionPath => Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs");

    private static string ExecutorPath => Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", "FirstOfficer", "Fenix", "FenixActionExecutor.cs");

    private static string BaroWindowPath => Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", "Forms", "FenixA320", "FenixBaroWindow.cs");

    private static readonly string Handler = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "HandleUIVariableSet");

    /// <summary>The <c>if (varKey == "key"…)</c> branch of HandleUIVariableSet, condition and body.</summary>
    private static string Branch(string key)
    {
        var m = Regex.Match(Handler, @"if \(varKey == """ + Regex.Escape(key) + @"""[^{]*\{[^}]*\}");
        Assert.True(m.Success, $"HandleUIVariableSet has no if (varKey == \"{key}\") branch");
        return m.Value;
    }

    private static void AssertValues(string key, params (double Value, string Label)[] expected)
    {
        var actual = Vars[key].ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToArray();
        Assert.Equal(expected, actual);
    }

    // Momentary buttons, with their light where one is registered (null where none is,
    // or where the Fenix never lights it on the ground: SVCE INT OVRD and AVIONICS COMPT).
    public static TheoryData<string, string, string?> MomentaryButtons => new()
    {
        { "S_OH_ELEC_BUS_TIE", "S_OH_ELEC_BUSTIE", "I_OH_ELEC_BUSTIE_L" },
        { "S_OH_SVCE_INT_OVRD", "S_OH_SVCE_INT_OVRD", null },
        { "S_OH_LIGHTING_AVIONICS_COMPT", "S_OH_LIGHTING_AVIONICS_COMPT", null },
        { "S_PED_COCKPIT_DOOR_VIDEO", "S_PED_COCKPIT_DOOR_VIDEO", null },   // the overhead VIDEO pb owns the light
        { "S_WR_GCS", "S_WR_GCS", null },
        { "S_ASP_PA_SEND", "S_ASP_PA_SEND", "I_ASP_PA_SEND" },
        { "S_OH_CALLS_MECH", "S_OH_CALLS_MECH", null },
        { "S_OH_CALLS_ALL", "S_OH_CALLS_ALL", null },
        { "S_OH_CALLS_FWD", "S_OH_CALLS_FWD", null },
        { "S_OH_CALLS_AFT", "S_OH_CALLS_AFT", null },
    };

    [Theory]
    [MemberData(nameof(MomentaryButtons))]
    public void Momentary_buttons_are_press_buttons(string key, string lvar, string? light)
    {
        var v = Vars[key];
        Assert.True(v.RenderAsButton, key);
        Assert.Empty(v.ValueDescriptions);
        Assert.Equal(light, v.StateVariable);
        Assert.Equal(lvar, v.Name);
        if (light != null) Assert.True(Vars.ContainsKey(light), light);

        string branch = Branch(key);
        Assert.Contains($"ExecuteButtonTransition(\"{lvar}\"", branch);
        Assert.DoesNotContain("SetLVar(", branch);
    }

    // CORE-7: a press with no readable state confirms "<name> pressed" once, in the name the reader
    // just spoke; a button with a light stays silent, its light speaks.
    [Theory]
    [MemberData(nameof(MomentaryButtons))]
    public void Only_a_button_with_no_light_confirms_its_press(string key, string lvar, string? light)
    {
        string branch = Branch(key);
        if (light == null)
        {
            Assert.Contains($"ExecuteButtonTransition(\"{lvar}\", varDef.DisplayName,", branch);
            Assert.Contains("confirmPress: true", branch);
        }
        else
        {
            Assert.DoesNotContain("confirmPress", branch);
        }
    }

    [Fact]
    public void The_press_confirmation_speaks_once_as_the_press_is_sent()
    {
        Assert.DoesNotContain("confirmPress", Branch("S_OH_ELEC_BUS_TIE"));

        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "ExecuteButtonTransition");
        int press = body.IndexOf("simConnect.SetLVar(varName, 1);", StringComparison.Ordinal);
        int say = body.IndexOf("if (confirmPress) announcer?.Announce($\"{displayName} pressed\");", StringComparison.Ordinal);
        int release = body.IndexOf("releaseTimer", StringComparison.Ordinal);
        Assert.True(press >= 0, "the press write is gone");
        // Right after the press write, inside its connected check, before the release is scheduled.
        Assert.True(say > press && say < release, "the confirmation is not spoken with the press");
        Assert.Equal("}", body.Substring(say).Split('\n')[1].Trim());
        Assert.Single(Regex.Matches(body, "pressed\""));
    }

    [Fact]
    public void Calls_emergency_is_not_a_press_button()
    {
        Assert.False(Vars["S_OH_CALLS_EMER"].RenderAsButton);
        Assert.False(Vars["S_OH_CALLS_EMER_Cover"].RenderAsButton);
    }

    [Fact]
    public void Counters_step_from_the_sims_value()
    {
        Assert.Equal("(L:E_FCU_SPEED) 3 + (>L:E_FCU_SPEED)", FenixA320Definition.CounterStepCode("E_FCU_SPEED", 3));
        Assert.Equal("(L:E_FCU_SPEED) 2 - (>L:E_FCU_SPEED)", FenixA320Definition.CounterStepCode("E_FCU_SPEED", -2));

        // AdjustBaroCounter too (the typed altimeter): a repeated identical ±1 correction is dropped by
        // MobiFlight unless uniquified (SIM-17).
        foreach (var helper in new[] { "IncrementCounter", "DecrementCounter", "JumpCounter", "AdjustBaroCounter" })
        {
            string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, helper);
            Assert.Contains("CounterStepCode(", body);
            Assert.Contains("ExecuteCalculatorCodeUnique(", body);
            Assert.DoesNotContain("SetLVar(", body);
        }

        Assert.DoesNotContain("rmpCounters", File.ReadAllText(DefinitionPath));
    }

    [Fact]
    public void Baro_std_is_a_pull_and_a_push()
    {
        foreach (var (n, side) in new[] { ("1", "Left"), ("2", "Right") })
        {
            string pull = $"S_FCU_EFIS{n}_BARO_PULL", push = $"S_FCU_EFIS{n}_BARO_PUSH", std = $"S_FCU_EFIS{n}_BARO_STD";
            var panel = Panels[$"EFIS {side}"];
            Assert.Contains(pull, panel);
            Assert.Contains(push, panel);
            Assert.DoesNotContain(std, panel);

            Assert.Equal($"EFIS {side} Baro Pull (STD)", Vars[pull].DisplayName);
            Assert.Equal($"EFIS {side} Baro Push (QNH)", Vars[push].DisplayName);
            foreach (var key in new[] { pull, push })
            {
                Assert.True(Vars[key].RenderAsButton, key);
                Assert.Equal(std, Vars[key].Name);
                Assert.Equal(UpdateFrequency.OnRequest, Vars[key].UpdateFrequency);
            }

            // The STD definition stays registered (the First Officer's evaluator still reads it).
            Assert.True(Vars.ContainsKey(std), std);

            // Pull is +1 (STD), push is -1 (QNH), from the sim's own count.
            Assert.Contains($"CounterStepCode(\"{std}\", 1)", Branch(pull));
            Assert.Contains($"CounterStepCode(\"{std}\", -1)", Branch(push));
            Assert.Contains("ExecuteCalculatorCodeUnique(", Branch(pull));
            Assert.Contains("ExecuteCalculatorCodeUnique(", Branch(push));

            var qnh = Vars[$"I_FCU_EFIS{n}_QNH"];
            Assert.Equal(UpdateFrequency.Continuous, qnh.UpdateFrequency);
            Assert.True(qnh.IsAnnounced);
        }

        string fo = FoFbwUnclaimedEventKeyTests.MethodBody(ExecutorPath, "SetBaroStdBoth");
        Assert.Contains("CounterStepCode(", fo);
        Assert.DoesNotContain("DispatchAsync(\"S_FCU_EFIS", fo);
    }

    [Fact]
    public void Typed_baro_leaves_std_with_a_push_step()
    {
        // An absolute 0 is no change when the count already reads 0 (a push from QNH, then a pull):
        // the side stayed in STD while the typed QNH went in.
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "SetFCUBaro");
        Assert.Contains("CounterStepCode($\"S_FCU_EFIS{efis}_BARO_STD\", -1)", body);
        Assert.Contains("ExecuteCalculatorCodeUnique(", body);
        Assert.DoesNotContain("SetLVar($\"S_FCU_EFIS{efis}_BARO_STD\"", body);

        // Nothing in the definition writes the STD counter as an absolute value.
        Assert.DoesNotMatch(@"SetLVar\(\$?""S_FCU_EFIS[^""]*_BARO_STD""", File.ReadAllText(DefinitionPath));
    }

    [Fact]
    public void Baro_window_mode_pick_steps_the_std_counter()
    {
        // The window's QNH/STD pick is a pull (+1) or a push (-1) from the sim's own count, as the
        // panel and the First Officer step it: an absolute write could select the wrong mode or nothing.
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(BaroWindowPath, "BaroModeCombo_SelectionChangeCommitted");
        foreach (var n in new[] { "1", "2" })
            Assert.Contains($"CounterStepCode(\"S_FCU_EFIS{n}_BARO_STD\", isStd ? 1 : -1)", body);
        Assert.Contains("ExecuteCalculatorCodeUnique(", body);

        string source = File.ReadAllText(BaroWindowPath);
        Assert.DoesNotMatch(@"SetLVar\(\$?""S_FCU_EFIS[^""]*_BARO_STD""", source);
        Assert.Contains("CounterStepCode(", source);
    }

    [Fact]
    public void Spring_switches_return_to_the_middle()
    {
        foreach (var key in new[] { "S_OH_CARGO_DISC_1_OLD_LAYOUT", "S_OH_CARGO_DISC_2_OLD_LAYOUT" })
            AssertValues(key, (0, "Agent 2"), (1, "Rest"), (2, "Agent 1"));
        foreach (var key in new[] { "S_DCDU1_DIM_BRT", "S_DCDU2_DIM_BRT" })
            AssertValues(key, (0, "Dim"), (1, "Mid"), (2, "Bright"));

        foreach (var key in new[] { "S_OH_CARGO_DISC_1_OLD_LAYOUT", "S_OH_CARGO_DISC_2_OLD_LAYOUT", "S_DCDU1_DIM_BRT", "S_DCDU2_DIM_BRT" })
        {
            string branch = Branch(key);
            Assert.Contains($"PulseSpringSwitch(\"{key}\"", branch);
            Assert.DoesNotContain("SetLVar(", branch);
        }

        // Manual pressurization keeps holding: the valve moves while the switch is held.
        Assert.Contains("SetLVar(\"S_OH_PNEUMATIC_PRESS_MAN\", value)", Branch("S_OH_PNEUMATIC_PRESS_MAN"));
    }

    [Fact]
    public void A_spring_switch_combo_resyncs_to_the_middle()
    {
        // The combo kept showing the picked end after the spring-back, and WinForms ignores a same-index
        // pick, so a second step the same way committed nothing. One forced read after the return write
        // re-syncs it ([MD11-27] pattern), on the L:var each caller passes, which is the panel's var key.
        foreach (var key in new[] { "S_OH_CARGO_DISC_1_OLD_LAYOUT", "S_OH_CARGO_DISC_2_OLD_LAYOUT", "S_DCDU1_DIM_BRT", "S_DCDU2_DIM_BRT" })
        {
            Assert.Equal(key, Vars[key].Name);
            Assert.Contains($"PulseSpringSwitch(\"{key}\", value, simConnect)", Branch(key));
        }

        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "PulseSpringSwitch");
        int back = body.IndexOf("simConnect.SetLVar(lvar, 1);", StringComparison.Ordinal);
        int read = body.IndexOf("simConnect.RequestVariable(lvar, forceUpdate: true);", StringComparison.Ordinal);
        Assert.True(back >= 0, "the return-to-middle write is gone");
        Assert.True(read > back, "the combo is not re-synced after the return write");
        Assert.Single(Regex.Matches(body, Regex.Escape("RequestVariable(")));
        Assert.Contains("if (simConnect.IsConnected) simConnect.RequestVariable(lvar, forceUpdate: true);", body);
    }

    [Fact]
    public void Positions_match_the_cockpit()
    {
        AssertValues("S_OH_PNEUMATIC_PRESS_MAN", (0, "Down"), (1, "Mid"), (2, "Up"));
        AssertValues("S_OH_NAV_SYS_DISP", (0, "Off"), (1, "IR 1"), (2, "IR 3"), (3, "IR 2"));
        AssertValues("S_WR_MODE", (0, "WX"), (1, "WX+T"), (2, "TURB"), (3, "MAP"));
        AssertValues("S_OH_IN_LT_ANN_LT", (0, "Dim"), (1, "Bright"), (2, "Test"));
    }

    [Fact]
    public void N1_mode_is_registered_once()
    {
        string source = File.ReadAllText(DefinitionPath);
        foreach (var n in new[] { "1", "2" })
        {
            string key = $"S_OH_ENG_N1_MODE_{n}";
            Assert.Single(Regex.Matches(source, Regex.Escape($"[\"{key}\"]")));
            Assert.Equal(UpdateFrequency.OnRequest, Vars[key].UpdateFrequency);
            Assert.Equal($"Engine N1 Mode {n}", Vars[key].DisplayName);
            AssertValues(key, (0, "Off"), (1, "On"));
        }
    }
}
