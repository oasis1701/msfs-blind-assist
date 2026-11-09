using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// A cockpit lamp's own state rule, copied by the generator from the aircraft's emissive code
/// (package 1.0.11) and evaluated here over the values MSFSBA reads, so a light reads exactly as the
/// cockpit draws it. The forms below are every form the 496 lamps use.
/// </summary>
public class A300LampRuleTests
{
    private static bool? Lit(string state, params (string Input, double Value)[] values)
    {
        var rule = A300LampRule.Parse(state);
        var known = values.ToDictionary(v => v.Input, v => v.Value);
        return rule.IsLit(input => known.TryGetValue(input.Id, out var v) ? v : null);
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    public void A_variable_lights_while_it_is_set(double value, bool lit) =>
        Assert.Equal(lit, Lit("(L:INI_STARTER1_OPEN)", ("L:INI_STARTER1_OPEN", value)));

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(0.0, true)]
    public void Not_lights_while_it_is_clear(double value, bool lit) =>
        Assert.Equal(lit, Lit("(L:INI_BAT1_ON) !", ("L:INI_BAT1_ON", value)));

    [Theory]
    [InlineData(1.0, 0.0, true)]
    [InlineData(1.0, 1.0, false)]
    [InlineData(0.0, 0.0, false)]
    public void External_power_avail_lights_with_power_available_and_not_on(double avail, double on, bool lit) =>
        Assert.Equal(lit, Lit("(L:INI_gpu_avail, Bool) (A:EXTERNAL POWER ON:1, Bool) ! and",
            ("L:INI_gpu_avail", avail), ("A:EXTERNAL POWER ON:1|Bool", on)));

    [Theory]
    [InlineData(100.0, true)]
    [InlineData(99.0, false)]
    [InlineData(0.0, false)]
    public void A_gear_green_lights_only_fully_down(double percent, bool lit) =>
        Assert.Equal(lit, Lit("(A:GEAR POSITION:1, Percent) 100 ==", ("A:GEAR POSITION:1|Percent", percent)));

    [Fact]
    public void Comparisons_or_and_division_follow_the_cockpit()
    {
        Assert.Equal(true, Lit("(L:A) 2 > (L:B) 3 < and", ("L:A", 3), ("L:B", 1)));
        Assert.Equal(false, Lit("(L:A) 2 > (L:B) 3 < and", ("L:A", 2), ("L:B", 1)));
        Assert.Equal(true, Lit("(L:A) (L:B) 1 == or", ("L:A", 0), ("L:B", 1)));
        Assert.Equal(true, Lit("(L:A) 1 == (L:B) 0 != and", ("L:A", 1), ("L:B", 5)));
        Assert.Equal(true, Lit("(L:A) 100 /", ("L:A", 80)));
        Assert.Equal(false, Lit("(L:A) 100 /", ("L:A", 20)));
    }

    [Fact]
    public void An_unread_input_leaves_the_lamp_unknown() =>
        Assert.Null(Lit("(L:INI_gpu_avail, Bool) (A:EXTERNAL POWER ON:1, Bool) ! and", ("L:INI_gpu_avail", 1)));

    [Fact]
    public void The_inputs_name_each_variable_once_with_the_unit_a_stock_one_is_read_in()
    {
        var rule = A300LampRule.Parse("(L:INI_X, Bool) (L:INI_X) ! (A:GEAR POSITION:2, Percent) 100 == and and");
        Assert.Equal(new[] { "L:INI_X", "A:GEAR POSITION:2|Percent" }, rule.Inputs.Select(i => i.Id));
        Assert.Equal(("INI_X", false, "number"), (rule.Inputs[0].Name, rule.Inputs[0].IsStock, rule.Inputs[0].Units));
        Assert.Equal(("GEAR POSITION:2", true, "Percent"), (rule.Inputs[1].Name, rule.Inputs[1].IsStock, rule.Inputs[1].Units));
    }

    [Theory]
    [InlineData("(L:A) 2 +")]
    [InlineData("(L:A) and")]
    [InlineData("(E:SIMULATION TIME, seconds)")]
    public void A_form_the_cockpit_does_not_use_is_refused(string state) =>
        Assert.Throws<FormatException>(() => A300LampRule.Parse(state));

    [Fact]
    public void Every_lamp_in_the_shipped_map_parses()
    {
        var lamps = A300ControlMap.Load().Lamps;
        Assert.Equal(496, lamps.Count);
        Assert.All(lamps, l => A300LampRule.Parse(l.State));
        var start = lamps.Single(l => l.Node == "ENG_1_START_SEQ1_LIGHT");
        Assert.Equal(("(L:INI_STARTER1_OPEN)", A300LightPower.Ac), (start.State, start.Power));
        Assert.Equal(A300LightPower.None, lamps.Single(l => l.Node == "EXT_PWR_SEQ1_LIGHT").Power);
    }
}
