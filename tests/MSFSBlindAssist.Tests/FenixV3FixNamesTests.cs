// Fenix v3 (3.0.0.7426) renamed or removed some L:vars and the FenixQuartz display module is discontinued.
// These tests pin the definition to the v3 names: a variable the package no longer has reads "" on v3.

using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FenixV3FixNamesTests
{
    private static readonly Dictionary<string, SimVarDefinition> Vars = new FenixA320Definition().GetVariables();

    [Theory]
    [InlineData("B_ELEC_BUS_POWER_DC_SERVICE")]
    [InlineData("I_OH_COCKPIT_DOOR_VIDEO")]
    [InlineData("I_OH_ELEC_COMMERCIAL_L")]
    public void V3_name_is_registered_under_its_own_key_and_announced(string name)
    {
        Assert.True(Vars.ContainsKey(name));
        Assert.Equal(name, Vars[name].Name);
        Assert.Equal(UpdateFrequency.Continuous, Vars[name].UpdateFrequency);
        Assert.True(Vars[name].IsAnnounced);
    }

    [Theory]
    [InlineData("B_ELEC_BUS_POWER_DC_SERIVICE")]
    [InlineData("I_OH_DOOR_VIDEO")]
    [InlineData("I_OH_ELEC_COMMERCIAL_U")]
    public void No_variable_reads_a_name_v3_removed(string gone)
    {
        Assert.DoesNotContain(Vars, kv => kv.Key == gone || kv.Value.Name == gone);
    }

    // Plan 05 moves the First Officer off the three take-off speeds, plan 12 deletes them.
    [Fact]
    public void Only_the_takeoff_speed_FenixQuartz_names_remain()
    {
        var names = Vars.Values.Select(v => v.Name).Where(n => n.StartsWith("FNX2PLD_"))
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "FNX2PLD_speedV1", "FNX2PLD_speedV2", "FNX2PLD_speedVR" }, names);
    }
}
