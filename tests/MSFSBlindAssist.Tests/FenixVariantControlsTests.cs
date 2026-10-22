// Pins Fenix v3 plan 06 (variants): the controls only some Fenix airframes have are registered and
// placed once, and the panel placement keeps every touched panel's labels distinct.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixVariantControlsTests
{
    private static readonly FenixA320Definition Fenix = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Fenix.GetVariables();
    private static readonly Dictionary<string, List<string>> Panels = Fenix.GetPanelControls();

    private const string AirPanel = "Air Conditioning and Pressurization";

    private static readonly string[] Lights =
    {
        "I_OH_PNEUMATIC_ECON_FLOW_L", "I_OH_FUEL_ACT_U", "I_OH_FUEL_ACT_L",
        "I_OH_ENG_N1_MODE_1_L", "I_OH_ENG_N1_MODE_2_L",
    };

    private static readonly string[] Switches =
    {
        "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_FUEL_ACT", "S_OH_CALLS_MID", "S_OH_CALLS_EXIT",
    };

    [Fact]
    public void Switches_are_on_request_and_lights_are_announced()
    {
        foreach (var key in new[] { "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_FUEL_ACT" })
        {
            var v = Vars[key];
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, v.ValueDescriptions);
            Assert.False(v.RenderAsButton);
            Assert.Equal(key, v.Name);
        }

        foreach (var (key, label) in new[] { ("S_OH_CALLS_MID", "Calls Mid"), ("S_OH_CALLS_EXIT", "Calls Exit") })
        {
            var v = Vars[key];
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.True(v.RenderAsButton);
            Assert.Equal(label, v.DisplayName);
            Assert.Equal(key, v.Name);
        }

        foreach (var key in Lights)
        {
            var v = Vars[key];
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced);
            Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, v.ValueDescriptions);
            Assert.Equal(key, v.Name);
        }
    }

    [Fact]
    public void Each_light_is_labelled_from_its_switch()
    {
        Assert.Equal(FenixA320Definition.EconFlowName + " Lower", Vars["I_OH_PNEUMATIC_ECON_FLOW_L"].DisplayName);
        Assert.Equal(FenixA320Definition.ActPumpName + " Upper", Vars["I_OH_FUEL_ACT_U"].DisplayName);
        Assert.Equal(FenixA320Definition.ActPumpName + " Lower", Vars["I_OH_FUEL_ACT_L"].DisplayName);
        Assert.Equal(Vars["S_OH_ENG_N1_MODE_1"].DisplayName + " Lower", Vars["I_OH_ENG_N1_MODE_1_L"].DisplayName);
        Assert.Equal(Vars["S_OH_ENG_N1_MODE_2"].DisplayName + " Lower", Vars["I_OH_ENG_N1_MODE_2_L"].DisplayName);
    }

    [Fact]
    public void Each_switch_sits_once_where_the_plan_puts_it()
    {
        var air = Panels[AirPanel];
        Assert.Equal(air.IndexOf("S_OH_PNEUMATIC_PACK_FLOW") + 1, air.IndexOf("S_OH_PNEUMATIC_ECON_FLOW"));

        var calls = Panels["Calls"].Where(k => k is "S_OH_CALLS_FWD" or "S_OH_CALLS_MID" or "S_OH_CALLS_EXIT" or "S_OH_CALLS_AFT");
        Assert.Equal(new[] { "S_OH_CALLS_FWD", "S_OH_CALLS_MID", "S_OH_CALLS_EXIT", "S_OH_CALLS_AFT" }, calls);

        var fuel = Panels["Fuel"];
        Assert.Equal(fuel.IndexOf("S_OH_FUEL_CENTER_2") + 1, fuel.IndexOf("S_OH_FUEL_ACT"));

        var all = Panels.Values.SelectMany(p => p).ToList();
        foreach (var key in Switches)
            Assert.Single(all, k => k == key);
        foreach (var key in Lights)
            Assert.DoesNotContain(key, all);
    }

    [Theory]
    [InlineData(AirPanel)]
    [InlineData("Fuel")]
    [InlineData("Calls")]
    public void Touched_panels_repeat_no_label(string panel)
    {
        var names = Panels[panel].Select(k => Vars[k].DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
