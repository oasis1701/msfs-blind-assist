using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the one change rule both SimConnect delivery paths apply (SimConnectManager.IsValueChange):
/// a first delivery is a change; after that a value must move by MORE than the variable's own
/// ChangeTolerance, or the shared 0.001 when it sets none. The MD-11's DC bus voltage uses 0.5 V:
/// its power gate needs only the 20 V line, and with the shared tolerance every ripple of the bus
/// re-composed every stateful row of the open panel once a second.
/// </summary>
public class ValueChangeToleranceTests
{
    private static SimVarDefinition Def(double? tolerance) => new() { Name = "X", ChangeTolerance = tolerance };

    [Fact]
    public void A_first_delivery_is_always_a_change()
    {
        Assert.True(SimConnectManager.IsValueChange(null, 0, Def(null)));
        Assert.True(SimConnectManager.IsValueChange(null, 24, Def(0.5)));
    }

    [Fact]
    public void Without_a_tolerance_of_its_own_a_var_uses_the_shared_one()
    {
        Assert.Equal(0.001, SimConnectManager.ChangeTolerance);
        Assert.False(SimConnectManager.IsValueChange(24.0, 24.0005, Def(null)));
        Assert.True(SimConnectManager.IsValueChange(24.0, 24.01, Def(null)));
        Assert.True(SimConnectManager.IsValueChange(24.0, 24.3, null));      // no definition at all: the shared rule
    }

    [Fact]
    public void A_var_with_its_own_tolerance_ignores_ripple_inside_it_and_reports_a_step_beyond_it()
    {
        var def = Def(0.5);
        Assert.False(SimConnectManager.IsValueChange(24.0, 24.3, def));      // bus ripple
        Assert.False(SimConnectManager.IsValueChange(24.0, 23.5, def));      // exactly the tolerance is still the same value
        Assert.True(SimConnectManager.IsValueChange(24.0, 23.4, def));
        Assert.True(SimConnectManager.IsValueChange(24.0, 0.0, def));        // power lost: the step the gate needs
    }

    [Fact]
    public void The_Md11_DC_bus_voltage_carries_half_a_volt_and_is_not_a_seeded_var()
    {
        var def = new TFDiMD11Definition().GetVariables()[TFDiMD11Definition.DcPowerKey];

        Assert.Equal(0.5, def.ChangeTolerance);
        // Md11SeedGate compares at the shared constant; a var with a tolerance of its own must never
        // be one it counts, or the gate and the delivery filter would disagree on what a change is.
        Assert.DoesNotContain(TFDiMD11Definition.DcPowerKey, TFDiMD11Definition.SeededScalarKeys);
    }

    [Fact]
    public void No_other_Md11_variable_sets_a_tolerance_of_its_own()
    {
        // Widening another var is a decision about its readers AND about Md11SeedGate (which counts
        // every lamp and SeededScalarKeys at the shared constant) — make it here, deliberately.
        var widened = new TFDiMD11Definition().GetVariables()
            .Where(kv => kv.Value.ChangeTolerance != null)
            .Select(kv => kv.Key)
            .ToList();

        Assert.Equal(new[] { TFDiMD11Definition.DcPowerKey }, widened);
    }
}
