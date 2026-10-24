using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.FirstOfficer.A300;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

public class A300FoStateEvaluatorTests
{
    private readonly Dictionary<string, double> _cache = new();
    private readonly A300FoStateEvaluator _state;

    public A300FoStateEvaluatorTests()
    {
        _state = new A300FoStateEvaluator { Read = key => _cache.TryGetValue(key, out var v) ? v : null };
    }

    [Fact]
    public void Every_row_the_first_officer_names_is_a_placed_panel_row()
    {
        var def = new IniA300Definition();
        var rows = A300FoKeys.AllAt.Values.SelectMany(w => w).Select(w => w.Key)
            .Concat(A300FoKeys.PressConfirmations.Keys).Concat(A300FoKeys.SingleRows).Distinct();
        foreach (var key in rows)
            Assert.True(def.IsPlacedRow(key), key);
    }

    [Fact]
    public void Every_light_the_first_officer_reads_is_a_board_light_or_the_master_warning()
    {
        foreach (var light in A300FoKeys.Lights)
            Assert.True(A300LampBoard.ById.ContainsKey(light) || light == A300FoKeys.MasterWarning, light);
    }

    [Fact]
    public void An_unread_field_is_nan()
        => Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.Batt1)));

    [Fact]
    public void A_row_reads_its_cached_position()
    {
        _cache[A300FoKeys.Batt1] = 1;
        Assert.Equal(1, _state.GetValue(A300FoKeys.Batt1));
    }

    [Fact]
    public void A_light_reads_as_the_cockpit_shows_it_power_included()
    {
        A300FoTestLights.Set(_cache, A300FoKeys.ApuAvailLight, lit: true);
        Assert.Equal(1, _state.GetValue(A300FoKeys.ApuAvailLight));
        A300FoTestLights.Set(_cache, A300FoKeys.ApuAvailLight, lit: false);
        Assert.Equal(0, _state.GetValue(A300FoKeys.ApuAvailLight));
        var power = A300LampBoard.ById[A300FoKeys.ApuAvailLight].Inputs
            .FirstOrDefault(i => i is A300LampBoard.AcPowerKey or A300LampBoard.DcPowerKey);
        if (power != null)
        {
            A300FoTestLights.Set(_cache, A300FoKeys.ApuAvailLight, lit: true);
            _cache[power] = 0;   // the bus's light power off: dark, whatever its state
            Assert.Equal(0, _state.GetValue(A300FoKeys.ApuAvailLight));
        }
    }

    [Fact]
    public void A_group_field_needs_every_row_read_and_at_its_target()
    {
        Assert.True(double.IsNaN(_state.GetValue("FO_BATTERIES_ON")));
        _cache[A300FoKeys.Batt1] = 1; _cache[A300FoKeys.Batt2] = 1;
        Assert.True(double.IsNaN(_state.GetValue("FO_BATTERIES_ON")));
        _cache[A300FoKeys.Batt3] = 0;
        Assert.Equal(0, _state.GetValue("FO_BATTERIES_ON"));
        _cache[A300FoKeys.Batt3] = 1;
        Assert.Equal(1, _state.GetValue("FO_BATTERIES_ON"));
    }

    [Fact]
    public void Engines_stopped_needs_both_fuel_levers_read_off()
    {
        Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.EnginesStopped)));
        _state.SetEngineN2(0, 0);
        _cache[A300FoKeys.FuelLever1] = 1; _cache[A300FoKeys.FuelLever2] = 0;
        Assert.Equal(0, _state.GetValue(A300FoKeys.EnginesStopped));
        _cache[A300FoKeys.FuelLever1] = 0;
        Assert.Equal(1, _state.GetValue(A300FoKeys.EnginesStopped));
        _state.SetEngineN2(25, 0);
        Assert.Equal(0, _state.GetValue(A300FoKeys.EnginesStopped));
    }

    [Fact]
    public void Engines_stopped_and_spooled_down_are_unknown_until_both_N2_are_read()
    {
        // N2 is unknown until the window's first delivery: never stopped, never running.
        _cache[A300FoKeys.FuelLever1] = 0; _cache[A300FoKeys.FuelLever2] = 0;
        Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.EnginesStopped)));
        Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.EnginesSpooledDown)));
        _state.SetEngineN2(0, double.NaN);
        Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.EnginesStopped)));
        Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.EnginesSpooledDown)));
        _state.SetEngineN2(0, 0);
        Assert.Equal(1, _state.GetValue(A300FoKeys.EnginesStopped));
        Assert.Equal(1, _state.GetValue(A300FoKeys.EnginesSpooledDown));
    }

    [Fact]
    public void Brakes_below_100_needs_both_temperatures_read_and_below_100()
    {
        Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.BrakesBelow100)));
        _cache[A300FoVariables.BrakeTempLeftKey] = 99;
        Assert.True(double.IsNaN(_state.GetValue(A300FoKeys.BrakesBelow100)));
        _cache[A300FoVariables.BrakeTempRightKey] = 100;
        Assert.Equal(0, _state.GetValue(A300FoKeys.BrakesBelow100));
        _cache[A300FoVariables.BrakeTempRightKey] = 95;
        Assert.Equal(1, _state.GetValue(A300FoKeys.BrakesBelow100));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 1)] [InlineData(4, 0)]
    public void Takeoff_flaps_are_15_0_to_15_20(double index, double expected)
    {
        _cache[A300FoKeys.Flaps] = index;
        Assert.Equal(expected, _state.GetValue(A300FoKeys.FlapsTakeoff));
    }

    [Fact]
    public void Gear_down_is_the_lever_down_and_three_greens()
    {
        Assert.Null(A300FoGearConfirmation.Down(1, new bool?[] { true, true, null }));
        Assert.True(A300FoGearConfirmation.Down(1, new bool?[] { true, true, true }));
        Assert.False(A300FoGearConfirmation.Down(0, new bool?[] { true, true, true }));
        Assert.False(A300FoGearConfirmation.Down(1, new bool?[] { true, false, true }));
    }

    [Fact]
    public void Gear_up_is_the_lever_up_and_every_gear_light_dark()
    {
        var dark = new bool?[] { false, false, false };
        Assert.True(A300FoGearConfirmation.Up(0, dark, dark));
        Assert.False(A300FoGearConfirmation.Up(0, dark, new bool?[] { false, true, false }));
        Assert.False(A300FoGearConfirmation.Up(1, dark, dark));
        Assert.Null(A300FoGearConfirmation.Up(null, dark, dark));
    }

    [Fact]
    public void Every_on_request_value_the_first_officer_reads_is_polled_and_registered()
    {
        var vars = new IniA300Definition().GetVariables();
        foreach (var key in _state.OnRequestPollFields)
            Assert.True(vars.ContainsKey(key), key);
        foreach (var key in A300FoVariables.Keys)
            Assert.Contains(key, _state.OnRequestPollFields);
    }
}
