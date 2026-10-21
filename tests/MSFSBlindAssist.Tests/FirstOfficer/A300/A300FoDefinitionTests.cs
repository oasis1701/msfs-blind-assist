using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>What the A300 First Officer needs from the definition: which keys are panel rows, how long a
/// test row is held, and the values only the First Officer reads.</summary>
public class A300FoDefinitionTests
{
    private readonly IniA300Definition _def = new();

    [Theory]
    [InlineData("A300_BATT_1", true)]
    [InlineData("A300_ENG2_FIRE_PUSH2", true)]
    [InlineData(A300Levers.FlapsKey, true)]
    [InlineData("A300_LT_APU_AVAIL_SEQ1_LIGHT", false)]
    [InlineData("A300_NOT_A_ROW", false)]
    public void Only_placed_panel_rows_are_rows(string key, bool expected)
        => Assert.Equal(expected, _def.IsPlacedRow(key));

    [Theory]
    [InlineData("A300_ENG1_FIRE_PUSH2", 7000)]
    [InlineData("A300_APU_FIRE_PUSH1", 3000)]
    [InlineData("A300_TO_CONFIG_TEST", 5000)]
    [InlineData("A300_ECAM_APU", A300WritePlan.HoldMs)]
    [InlineData("A300_NOT_A_ROW", 0)]
    public void A_test_row_is_held_as_long_as_the_aircraft_checklist_holds_it(string key, int ms)
        => Assert.Equal(ms, _def.FoHoldMs(key));

    [Fact]
    public void The_first_officer_values_are_read_on_request_silently()
    {
        var vars = _def.GetVariables();
        Assert.Equal(7, A300FoVariables.All.Count);
        foreach (var (key, var) in A300FoVariables.All)
        {
            var def = vars[key];
            Assert.Equal(var, def.Name);
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
            Assert.False(def.IsAnnounced);
            Assert.True(def.ExcludeFromMonitorManager);
        }
    }
}
