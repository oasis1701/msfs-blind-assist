using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300's lights as the cockpit shows them (the aircraft's emissive code, package 1.0.11): a light
/// is lit while its state is set AND its bus's light power is on (<c>INI_AC_LIGHTS_FAILURE</c> or
/// <c>INI_DC_LIGHTS_FAILURE</c>, 1 = powered). One board decides every fault light and the autobrake
/// lights, so a fault clearing on a dark panel says nothing and power coming on speaks only what lights.
/// </summary>
public class A300LampBoardTests
{
    private readonly A300LampBoard _board = new();

    private static string KeyOf(string var) => A300FaultLights.All.Single(l => l.Var == var).Key;

    private List<string> Feed(string key, double value) =>
        _board.Update(key, value).Select(c => $"{c.Lamp.Name} {(c.On ? "on" : "off")}").ToList();

    [Theory]
    [InlineData("INI_elec_gen1_fault", A300LightPower.Dc)]
    [InlineData("INI_BAT1_light", A300LightPower.Dc)]
    [InlineData("INI_elec_standby_gen_fault", A300LightPower.Ac)]
    [InlineData("INI_cabin_sys1_regulator_fault", A300LightPower.Ac)]
    [InlineData("INI_FMS1_message_light", A300LightPower.Ac)]
    [InlineData("INI_ECAM_CLR_LIGHT", A300LightPower.Ac)]
    [InlineData("INI_PITCH_FEEL1_FAULT", A300LightPower.Dc)]
    [InlineData("INI_PITCH_FEEL2_FAULT", A300LightPower.Ac)]
    public void Each_light_takes_its_bus_light_power_from_the_aircrafts_code(string var, A300LightPower power) =>
        Assert.Equal(power, A300FaultLights.All.Single(l => l.Var == var).Power);

    [Fact]
    public void Every_fault_light_has_a_bus() =>
        Assert.DoesNotContain(A300FaultLights.All, l => l.Power == A300LightPower.None);

    [Fact]
    public void A_light_whose_power_is_unread_sets_no_baseline_and_says_nothing()
    {
        string gen = KeyOf("INI_elec_gen1_fault");
        Assert.Empty(Feed(gen, 1));
        Assert.Empty(Feed(gen, 0));
        Assert.Empty(Feed(A300LampBoard.DcPowerKey, 1));   // now readable: a silent baseline
        Assert.Equal(new[] { "Engine 1 generator fault light on" }, Feed(gen, 1));
    }

    [Fact]
    public void A_light_on_a_powered_bus_speaks_both_ways()
    {
        string gen = KeyOf("INI_elec_gen1_fault");
        Feed(A300LampBoard.DcPowerKey, 1);
        Feed(gen, 0);
        Assert.Equal(new[] { "Engine 1 generator fault light on" }, Feed(gen, 1));
        Assert.Equal(new[] { "Engine 1 generator fault light off" }, Feed(gen, 0));
    }

    [Fact]
    public void A_fault_clearing_on_a_dark_bus_says_nothing()
    {
        // Batteries on, AC still dark: the standby generator light is dark in the cockpit whatever its state.
        string standby = KeyOf("INI_elec_standby_gen_fault");
        Feed(A300LampBoard.AcPowerKey, 0);
        Feed(standby, 1);
        Assert.Empty(Feed(standby, 0));
        Assert.Empty(Feed(standby, 1));
    }

    [Fact]
    public void Power_coming_on_speaks_only_the_lights_that_light()
    {
        string standby = KeyOf("INI_elec_standby_gen_fault");
        string regulator = KeyOf("INI_cabin_sys1_regulator_fault");
        Feed(A300LampBoard.AcPowerKey, 0);
        Feed(standby, 1);
        Feed(regulator, 0);
        Assert.Equal(new[] { "Standby generator fault light on" }, Feed(A300LampBoard.AcPowerKey, 1));
    }

    [Fact]
    public void Power_going_off_darkens_the_lit_lights_as_the_cockpit_does()
    {
        string gen = KeyOf("INI_elec_gen1_fault");
        string battery = KeyOf("INI_BAT1_light");
        Feed(A300LampBoard.DcPowerKey, 1);
        Feed(gen, 1);
        Feed(battery, 0);
        Assert.Equal(new[] { "Engine 1 generator fault light off" }, Feed(A300LampBoard.DcPowerKey, 0));
    }

    [Fact]
    public void An_autobrake_level_lights_its_armed_light_and_decel_takes_over()
    {
        Feed(A300LampBoard.AcPowerKey, 1);
        Feed(A300Autobrake.LevelKey, 0);
        Feed("A300_AUTOBRAKE_LOW_DECEL", 0);
        Assert.Equal(new[] { "Autobrake low armed light on" }, Feed(A300Autobrake.LevelKey, 1));
        Assert.Equal(new[] { "Autobrake low armed light off", "Autobrake low decel light on" },
            Feed("A300_AUTOBRAKE_LOW_DECEL", 1).OrderBy(s => s.Contains("decel")).ToList());
    }

    [Fact]
    public void Moving_from_one_autobrake_level_to_another_swaps_the_armed_lights()
    {
        Feed(A300LampBoard.AcPowerKey, 1);
        foreach (var decel in new[] { "A300_AUTOBRAKE_LOW_DECEL", "A300_AUTOBRAKE_MED_DECEL", "A300_AUTOBRAKE_HI_DECEL" })
            Feed(decel, 0);
        Feed(A300Autobrake.LevelKey, 1);
        Assert.Equal(new[] { "Autobrake low armed light off", "Autobrake max armed light on" },
            Feed(A300Autobrake.LevelKey, 3).OrderBy(s => s.Contains(" on")).ToList());
    }

    [Fact]
    public void An_autobrake_armed_light_waits_for_its_decel_input()
    {
        // The armed segment is lit while the level is its level AND its DECEL segment is dark, so with the DECEL
        // input unread it is undecided ([A300-23]: an unread input sets no baseline), not baselined as lit.
        Feed(A300LampBoard.AcPowerKey, 1);
        Assert.Empty(Feed(A300Autobrake.LevelKey, 3));
        Assert.Empty(Feed("A300_AUTOBRAKE_HI_DECEL", 1));   // both max lights' first values: armed dark, decel lit
        Assert.Equal(new[] { "Autobrake max armed light on", "Autobrake max decel light off" },
            Feed("A300_AUTOBRAKE_HI_DECEL", 0).OrderBy(s => s, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void An_autobrake_armed_light_stays_undecided_until_its_own_decel_is_read()
    {
        // Another button's DECEL light does not decide it: each armed light reads its own button's DECEL.
        string low = "Autobrake low armed light", max = "Autobrake max armed light";
        var byName = A300LampBoard.Lamps.ToDictionary(l => l.Name);
        var values = new Dictionary<string, double>
        {
            [A300LampBoard.AcPowerKey] = 1, [A300Autobrake.LevelKey] = 1, ["A300_AUTOBRAKE_LOW_DECEL"] = 0,
        };
        double? Read(string key) => values.TryGetValue(key, out var v) ? v : null;
        Assert.True(A300LampBoard.IsLit(byName[low], Read));
        Assert.Null(A300LampBoard.IsLit(byName[max], Read));
    }

    [Fact]
    public void The_autobrake_lights_are_dark_without_ac_light_power()
    {
        Feed(A300LampBoard.AcPowerKey, 0);
        Feed("A300_AUTOBRAKE_MED_DECEL", 0);
        Feed(A300Autobrake.LevelKey, 0);
        Assert.Empty(Feed(A300Autobrake.LevelKey, 2));
        Assert.Equal(new[] { "Autobrake medium armed light on" }, Feed(A300LampBoard.AcPowerKey, 1));
    }

    [Fact]
    public void The_armed_lights_share_the_level_rows_mute_and_each_decel_light_has_its_own()
    {
        var byName = A300LampBoard.Lamps.ToDictionary(l => l.Name);
        Assert.Equal(A300Autobrake.LevelKey, byName["Autobrake low armed light"].MuteKey);
        Assert.Equal("A300_AUTOBRAKE_HI_DECEL", byName["Autobrake max decel light"].MuteKey);
        Assert.Equal(KeyOf("INI_PACK1_FAULT"), byName["Pack 1 fault light"].MuteKey);
    }

    [Fact]
    public void Every_lamp_is_named_once() =>
        Assert.Equal(A300LampBoard.Lamps.Count, A300LampBoard.Lamps.Select(l => l.Name).Distinct().Count());

    [Fact]
    public void Every_rule_light_asks_only_for_the_inputs_it_lists()
    {
        // The board works each input's key out once for a lamp (a shared input's key is built with a regex), and the rule asks
        // for them on every delivery: with every input read, the keys a rule light asks the cache for are exactly the inputs
        // the board lists for it, its own key and its bus's light power among them.
        foreach (var resolved in A300PanelLamps.Resolved)
        {
            var lamp = A300LampBoard.ById[resolved.Lamp.Key];
            var asked = new HashSet<string>(StringComparer.Ordinal);
            Assert.NotNull(A300LampBoard.IsLit(lamp, key => { asked.Add(key); return 1; }));
            Assert.True(lamp.Inputs.ToHashSet(StringComparer.Ordinal).SetEquals(asked),
                $"{lamp.Name} lists [{string.Join(", ", lamp.Inputs)}] but asks for [{string.Join(", ", asked)}]");
            Assert.Contains(resolved.Lamp.Key, asked);
            Assert.All(resolved.Rule.Inputs.Where(i => i.Id != resolved.Primary.Id), i => Assert.Contains(A300PanelLamps.InputKey(i), asked));
        }
    }

    [Fact]
    public void A_seeded_value_is_a_silent_baseline_and_never_overwrites_one_already_read()
    {
        string gen = KeyOf("INI_elec_gen1_fault");
        Assert.True(_board.Seed(A300LampBoard.DcPowerKey, 1));
        Assert.True(_board.Seed(gen, 1));
        Assert.False(_board.Seed(gen, 0));   // already read
        Assert.Equal(new[] { "Engine 1 generator fault light off" }, Feed(gen, 0));
    }

    [Fact]
    public void A_reset_forgets_every_value_and_baseline()
    {
        string gen = KeyOf("INI_elec_gen1_fault");
        Feed(A300LampBoard.DcPowerKey, 1);
        Feed(gen, 0);
        _board.Reset();
        Assert.Empty(Feed(gen, 1));                      // power unread again
        Assert.Empty(Feed(A300LampBoard.DcPowerKey, 1)); // baseline
        Assert.Equal(new[] { "Engine 1 generator fault light off" }, Feed(gen, 0));
    }

    [Theory]
    [InlineData(1.0, 1.0, true)]
    [InlineData(1.0, 0.0, false)]
    [InlineData(0.0, 1.0, false)]
    public void A_lights_cockpit_state_is_its_state_and_its_power(double state, double power, bool lit)
    {
        var lamp = A300LampBoard.Lamps.Single(l => l.Name == "Engine 1 generator fault light");
        var values = new Dictionary<string, double> { [KeyOf("INI_elec_gen1_fault")] = state, [A300LampBoard.DcPowerKey] = power };
        Assert.Equal(lit, A300LampBoard.IsLit(lamp, k => values.TryGetValue(k, out var v) ? v : null));
    }

    [Fact]
    public void An_unread_input_leaves_the_cockpit_state_unknown()
    {
        var lamp = A300LampBoard.Lamps.Single(l => l.Name == "Engine 1 generator fault light");
        Assert.Null(A300LampBoard.IsLit(lamp, k => k == KeyOf("INI_elec_gen1_fault") ? 1 : null));
    }

    [Fact]
    public void A_light_power_flag_turning_on_or_off_is_a_power_flip()
    {
        _board.Update(A300LampBoard.AcPowerKey, 0);
        Assert.True(_board.PowerFlips(A300LampBoard.AcPowerKey, 1));
        _board.Update(A300LampBoard.AcPowerKey, 1);
        Assert.True(_board.PowerFlips(A300LampBoard.AcPowerKey, 0));
    }

    [Fact]
    public void The_first_power_value_the_same_value_and_a_fault_are_not_power_flips()
    {
        Assert.False(_board.PowerFlips(A300LampBoard.DcPowerKey, 1));   // nothing read yet: a baseline
        _board.Update(A300LampBoard.DcPowerKey, 1);
        Assert.False(_board.PowerFlips(A300LampBoard.DcPowerKey, 1));
        Assert.False(_board.PowerFlips(KeyOf("INI_elec_gen1_fault"), 1));
    }
}
