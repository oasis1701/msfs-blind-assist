using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The pure RPN builders behind every BlueBird 757 write. The vendor's click code is replayed
/// verbatim, so what is under test is the arithmetic around it: when a press is owed, how many
/// steps a move takes, how long bursts are split, and that no number is formatted with the PC's
/// culture (CI runs under en-US, so the de-DE test is the one that can catch a comma).
/// </summary>
public class Bbs757RpnTests
{
    private const string Press = "(L:X,bool) ! (>L:X,bool)";
    private const string Cw = "(L:S, enum) 1 + 2 min (>L:S)";
    private const string Ccw = "(L:S, enum) 1 - 0 max (>L:S)";

    private static List<Bbs757Position> Positions(params double[] values)
        => values.Select(v => new Bbs757Position(v, "P" + v.ToString(CultureInfo.InvariantCulture))).ToList();

    private static Bbs757Control Toggle() => new()
    {
        Node = "X", Kind = Bbs757Kinds.Toggle, StateVar = "X", Positions = Positions(0, 1), Press = Press,
    };

    private static Bbs757Control ThreePositionSwitch() => new()
    {
        Node = "S", Kind = Bbs757Kinds.Switch, StateVar = "S", Positions = Positions(0, 1, 2), Cw = Cw, Ccw = Ccw,
    };

    private static string Repeat(string code, int count) => string.Join(" ", Enumerable.Repeat(code, count));

    [Fact]
    public void ToggleTo_returns_the_press_code_verbatim_when_a_press_is_owed()
    {
        Assert.Equal(Press, Bbs757Rpn.ToggleTo(Toggle(), 0, 1));
        Assert.Equal(Press, Bbs757Rpn.ToggleTo(Toggle(), 1, 0));
    }

    [Fact]
    public void ToggleTo_returns_null_when_already_there()
    {
        Assert.Null(Bbs757Rpn.ToggleTo(Toggle(), 1, 1));
        Assert.Null(Bbs757Rpn.ToggleTo(Toggle(), 0, 0));
    }

    [Fact]
    public void StepTo_builds_two_ccw_steps()
    {
        IReadOnlyList<string> calls = Bbs757Rpn.StepTo(ThreePositionSwitch(), 2, 0);

        Assert.Equal(new[] { Ccw + " " + Ccw }, calls);
    }

    [Fact]
    public void StepTo_builds_cw_steps_going_up()
    {
        Assert.Equal(new[] { Cw }, Bbs757Rpn.StepTo(ThreePositionSwitch(), 1, 2));
    }

    [Fact]
    public void StepTo_is_empty_when_already_there()
    {
        Assert.Empty(Bbs757Rpn.StepTo(ThreePositionSwitch(), 1, 1));
    }

    [Fact]
    public void StepTo_splits_long_bursts()
    {
        var knob = new Bbs757Control
        {
            Node = "K", Kind = Bbs757Kinds.Switch, StateVar = "K", Positions = Positions(Enumerable.Range(0, 46).Select(i => (double)i).ToArray()),
            Cw = Cw, Ccw = Ccw,
        };

        IReadOnlyList<string> calls = Bbs757Rpn.StepTo(knob, 0, 45);

        Assert.Equal(3, calls.Count);
        Assert.Equal(Repeat(Cw, 20), calls[0]);
        Assert.Equal(Repeat(Cw, 20), calls[1]);
        Assert.Equal(Repeat(Cw, 5), calls[2]);
    }

    [Fact]
    public void StepTo_counts_position_indices_not_raw_values()
    {
        // Positions 0, 4 and 7: from 0 to 7 is two steps, not seven.
        var odd = new Bbs757Control
        {
            Node = "O", Kind = Bbs757Kinds.Switch, StateVar = "O", Positions = Positions(0, 4, 7), Cw = Cw, Ccw = Ccw,
        };

        Assert.Equal(new[] { Repeat(Cw, 2) }, Bbs757Rpn.StepTo(odd, 0, 7));
        Assert.Equal(new[] { Repeat(Ccw, 1) }, Bbs757Rpn.StepTo(odd, 7, 4));
    }

    [Fact]
    public void StepTo_cycles_a_press_only_control_forward()
    {
        var cycler = new Bbs757Control
        {
            Node = "C", Kind = Bbs757Kinds.Button, StateVar = "C", Positions = Positions(0, 1, 2, 3), Press = Press,
        };

        Assert.Equal(new[] { Repeat(Press, 3) }, Bbs757Rpn.StepTo(cycler, 0, 3));
        Assert.Equal(new[] { Repeat(Press, 1) }, Bbs757Rpn.StepTo(cycler, 3, 0));
        Assert.Equal(new[] { Repeat(Press, 3) }, Bbs757Rpn.StepTo(cycler, 2, 1));
        Assert.Empty(Bbs757Rpn.StepTo(cycler, 2, 2));
    }

    [Fact]
    public void StepTo_cycles_over_position_indices_when_the_values_are_not_consecutive()
    {
        var cycler = new Bbs757Control
        {
            Node = "C", Kind = Bbs757Kinds.Button, StateVar = "C", Positions = Positions(0, 4, 7), Press = Press,
        };

        Assert.Equal(new[] { Repeat(Press, 1) }, Bbs757Rpn.StepTo(cycler, 0, 4));
        Assert.Equal(new[] { Repeat(Press, 2) }, Bbs757Rpn.StepTo(cycler, 4, 0));
    }

    [Fact]
    public void StepTo_is_empty_when_a_value_is_not_one_of_the_positions()
    {
        Assert.Empty(Bbs757Rpn.StepTo(ThreePositionSwitch(), 5, 0));
        Assert.Empty(Bbs757Rpn.StepTo(ThreePositionSwitch(), 0, 5));
    }

    [Fact]
    public void StepTo_is_empty_for_a_control_with_no_stepping_code()
    {
        // The writer for these is SetLVar on the state var, not a click.
        var bare = new Bbs757Control
        {
            Node = "B", Kind = Bbs757Kinds.Switch, StateVar = "B", Positions = Positions(0, 1, 2),
        };

        Assert.Empty(Bbs757Rpn.StepTo(bare, 0, 2));
    }

    [Theory]
    [InlineData("B757_OVHD_L_ENG_START_SWITCH")]
    [InlineData("B757_OVHD_R_ENG_START_SWITCH")]
    public void StepTo_engine_start_selector_matches_its_click_code(string node)
    {
        // The vendor's cw goes 1 (GND) to 3 (AUTO) and ccw 3 to 1: value 2 is never written, so the map lists
        // 1, 3, 4, 5, 6 and one click between GND and AUTO is one step either way.
        Bbs757Control selector = Assert.Single(Bbs757ControlMap.Load().Controls, c => c.Node == node);

        Assert.Equal(new double[] { 1, 3, 4, 5, 6 }, selector.Positions.Select(p => p.Value));
        Assert.Equal(new[] { selector.Ccw! }, Bbs757Rpn.StepTo(selector, 3, 1));
        Assert.Equal(new[] { selector.Cw! }, Bbs757Rpn.StepTo(selector, 1, 3));
    }

    [Fact]
    public void SetLVar_is_invariant_under_de_DE()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1.5 (>L:X)", Bbs757Rpn.SetLVar("X", 1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(0, "0 (>L:X)")]
    [InlineData(1, "1 (>L:X)")]
    [InlineData(-2, "-2 (>L:X)")]
    [InlineData(0.1234567891, "0.123457 (>L:X)")]
    [InlineData(12.5, "12.5 (>L:X)")]
    public void SetLVar_writes_up_to_six_decimals_without_trailing_zeros(double value, string expected)
    {
        Assert.Equal(expected, Bbs757Rpn.SetLVar("X", value));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(60, 3)]
    public void KeyBurst_caps_25_per_call(int count, int expectedCalls)
    {
        IReadOnlyList<string> calls = Bbs757Rpn.KeyBurst("AP_SPD_VAR_INC", count);

        Assert.Equal(expectedCalls, calls.Count);
        Assert.All(calls.Take(calls.Count - 1), c => Assert.Equal(25, Occurrences(c)));
        Assert.Equal(count, calls.Sum(Occurrences));
        Assert.All(calls, c => Assert.StartsWith("(>K:AP_SPD_VAR_INC)", c));
    }

    [Fact]
    public void KeyBurst_of_nothing_is_empty()
    {
        Assert.Empty(Bbs757Rpn.KeyBurst("AP_SPD_VAR_INC", 0));
        Assert.Empty(Bbs757Rpn.KeyBurst("AP_SPD_VAR_INC", -3));
    }

    [Fact]
    public void KeyBurst_joins_with_a_single_space()
    {
        Assert.Equal(new[] { "(>K:E) (>K:E) (>K:E)" }, Bbs757Rpn.KeyBurst("E", 3));
    }

    private static int Occurrences(string call) => call.Split("(>K:").Length - 1;

    // ---- calculator string length ---------------------------------------------
    //
    // MobiFlight's command buffer is 1024 bytes (MobiFlightWasmModule.MAX_COMMAND_SIZE) and it
    // truncates silently; the unique prefix (ExecuteCalculatorCodeUnique) and "MF.SimVars.Set." add
    // a few dozen. Every string a 757 write can produce stays under 900.

    [Fact]
    public void A_burst_of_long_steps_is_split_by_length_as_well_as_by_count()
    {
        string longStep = "(L:LONG_STEP_VAR, enum) 1 + 9 min (>L:LONG_STEP_VAR) " + new string('x', 140);   // ~200 chars
        var knob = new Bbs757Control
        {
            Node = "K", Kind = Bbs757Kinds.Switch, StateVar = "K",
            Positions = Positions(Enumerable.Range(0, 20).Select(i => (double)i).ToArray()), Cw = longStep, Ccw = longStep,
        };

        IReadOnlyList<string> calls = Bbs757Rpn.StepTo(knob, 0, 19);

        Assert.All(calls, c => Assert.True(c.Length < Bbs757Rpn.MaxCallChars, $"{c.Length} chars"));
        Assert.Equal(19, calls.Sum(c => c.Split(longStep).Length - 1));   // every step still sent, in order
        Assert.True(calls.Count > 1);
    }

    [Fact]
    public void A_single_step_longer_than_the_cap_is_still_one_call_of_its_own()
    {
        // Never cut inside a vendor click: such a step would go alone (the map has none; see below).
        string huge = new string('y', 1000);
        var knob = new Bbs757Control
        {
            Node = "K", Kind = Bbs757Kinds.Switch, StateVar = "K", Positions = Positions(0, 1, 2), Cw = huge, Ccw = huge,
        };

        Assert.Equal(new[] { huge, huge }, Bbs757Rpn.StepTo(knob, 0, 2));
    }

    [Fact]
    public void Every_string_a_757_control_can_produce_stays_under_900_characters()
    {
        var map = Bbs757ControlMap.Load();
        var strings = new List<(string Where, string Rpn)>();
        foreach (var c in map.Controls)
        {
            if (!string.IsNullOrWhiteSpace(c.Press)) strings.Add(($"{c.Node} press", c.Press));
            if (!string.IsNullOrWhiteSpace(c.Release)) strings.Add(($"{c.Node} release", c.Release));
            foreach (var from in c.Positions)
            {
                foreach (var to in c.Positions)
                {
                    foreach (var call in Bbs757Rpn.StepTo(c, from.Value, to.Value)) strings.Add(($"{c.Node} step {from.Value}->{to.Value}", call));
                    string? toggle = Bbs757Rpn.ToggleTo(c, from.Value, to.Value);
                    if (toggle != null) strings.Add(($"{c.Node} toggle", toggle));
                    foreach (var call in Bbs757Write.ToPosition(c, from.Value, to.Value).Calls) strings.Add(($"{c.Node} write {from.Value}->{to.Value}", call));
                }
                if (!string.IsNullOrWhiteSpace(c.StateVar)) strings.Add(($"{c.Node} set", Bbs757Rpn.SetLVar(c.StateVar, from.Value)));
            }
        }
        // The MCP's K-event bursts, at their cap and past it.
        foreach (string k in new[] { "AP_SPD_VAR_INC", "AP_SPD_VAR_DEC", "AP_VS_VAR_INC", "AP_VS_VAR_DEC" })
            foreach (var call in Bbs757Rpn.KeyBurst(k, Bbs757Rpn.MaxKeysPerCall * 4)) strings.Add((k, call));

        Assert.NotEmpty(strings);
        var tooLong = strings.Where(s => s.Rpn.Length >= 900).Select(s => $"{s.Where}: {s.Rpn.Length}").ToList();
        Assert.True(tooLong.Count == 0, string.Join("; ", tooLong));
    }
}
