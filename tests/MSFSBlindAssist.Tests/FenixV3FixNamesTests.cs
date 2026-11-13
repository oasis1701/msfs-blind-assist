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
    [InlineData("S_HYD_GRAVITY_GEAR_EXTEND_ROTATION")]
    public void No_variable_reads_a_name_v3_removed(string gone)
    {
        Assert.DoesNotContain(Vars, kv => kv.Key == gone || kv.Value.Name == gone);
    }

    // A lit master WARNING reads 2 on v3 (measured 2026-10-21), so the row speaks "On", never "2".
    // The cautions read 1 when lit and keep the shared Off/On, which the warnings must not change.
    [Theory]
    [InlineData("I_MIP_MASTER_WARNING_CAPT")]
    [InlineData("I_MIP_MASTER_WARNING_FO")]
    public void A_lit_master_warning_reads_on(string key)
    {
        var values = Vars[key].ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToArray();
        Assert.Equal(new[] { (0.0, "Off"), (1.0, "On"), (2.0, "On") }, values);
    }

    [Theory]
    [InlineData("I_MIP_MASTER_CAUTION_CAPT")]
    [InlineData("I_MIP_MASTER_CAUTION_FO")]
    [InlineData("I_MIP_MASTER_WARNING_CAPT_L")]
    [InlineData("I_MIP_MASTER_WARNING_FO_L")]
    public void The_other_master_lights_keep_off_and_on(string key)
    {
        var values = Vars[key].ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToArray();
        Assert.Equal(new[] { (0.0, "Off"), (1.0, "On") }, values);
    }

    // FenixQuartz is discontinued and v3 does not feed it. The First Officer reads the take-off speeds
    // from N_MISC_PERF_TO_*, so the last three FNX2PLD_ names are gone too.
    [Fact]
    public void No_FenixQuartz_name_remains()
    {
        Assert.DoesNotContain(Vars, kv => kv.Key.StartsWith("FNX2PLD_", StringComparison.Ordinal)
                                          || kv.Value.Name.StartsWith("FNX2PLD_", StringComparison.Ordinal));
    }
}
