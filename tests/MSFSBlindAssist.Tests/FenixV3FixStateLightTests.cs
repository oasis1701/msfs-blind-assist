// Fenix buttons whose state light is an OFF legend, and the altimeter read-back after a typed setting
// (owner checks of builds 2 and 3, 2026-10-22). Measured live: pressing ADR 1 or IR 1 lights its lower
// legend and switches it off; ADR 2/3 and IR 2/3 share their template. Bus Tie carries one annunciator,
// dark in the normal state (the Fenix's Cockpit_Behavior.xml), read as the OFF legend until a press
// confirms it in the sim. MainForm labelled every StateVariable button "On" when lit, so these read
// backwards ("ADR 1: On" with ADR 1 off). The door video lamp is the overhead VIDEO pb's: VIDEO 0 lit
// it and 1 put it out; the pedestal Cockpit Door Video button has no light (a press left it unchanged).

using System.Globalization;
using System.IO;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FenixV3FixStateLightTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public FenixV3FixStateLightTests() => CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static readonly Dictionary<string, SimVarDefinition> Vars = new FenixA320Definition().GetVariables();

    private static string MainFormPath(string partial) => Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", partial);

    public static TheoryData<string, string, string> OffLegendButtons => new()
    {
        { "S_OH_ELEC_BUS_TIE", "I_OH_ELEC_BUSTIE_L", "Auto" },
        { "S_OH_NAV_ADR1", "I_OH_NAV_ADR1_L", "On" },
        { "S_OH_NAV_ADR2", "I_OH_NAV_ADR2_L", "On" },
        { "S_OH_NAV_ADR3", "I_OH_NAV_ADR3_L", "On" },
        { "S_OH_NAV_IR1_SWITCH", "I_OH_NAV_IR1_SWITCH_L", "On" },
        { "S_OH_NAV_IR2_SWITCH", "I_OH_NAV_IR2_SWITCH_L", "On" },
        { "S_OH_NAV_IR3_SWITCH", "I_OH_NAV_IR3_SWITCH_L", "On" },
    };

    [Theory]
    [MemberData(nameof(OffLegendButtons))]
    public void Off_legend_buttons_say_off_while_the_light_is_lit(string key, string light, string dark)
    {
        var v = Vars[key];
        Assert.Equal(light, v.StateVariable);
        Assert.Equal("Off", v.DescribeStateLight(1));
        Assert.Equal("Off", v.DescribeStateLight(2));
        Assert.Equal(dark, v.DescribeStateLight(0));
    }

    [Fact]
    public void A_state_light_without_texts_reads_on_when_lit()
    {
        var v = new SimVarDefinition { StateVariable = "I_ANY_LIGHT" };
        Assert.Equal("On", v.DescribeStateLight(1));
        Assert.Equal("Off", v.DescribeStateLight(0));
    }

    [Theory]
    [MemberData(nameof(OffLegendButtons))]
    public void An_off_legend_lamp_speaks_as_its_button_reads(string key, string light, string dark)
    {
        // The light is the only speech after a press; it must say what the button's label says.
        foreach (double v in new[] { 0.0, 1.0, 2.0 })
            Assert.Equal(Vars[key].DescribeStateLight(v), Vars[light].ValueDescriptions[v]);
        Assert.Equal(dark, Vars[light].ValueDescriptions[0]);
    }

    [Theory]
    [InlineData("I_OH_NAV_ADR1_L", "ADR 1")]
    [InlineData("I_OH_NAV_ADR2_L", "ADR 2")]
    [InlineData("I_OH_NAV_ADR3_L", "ADR 3")]
    [InlineData("I_OH_NAV_IR1_SWITCH_L", "IR 1")]
    [InlineData("I_OH_NAV_IR2_SWITCH_L", "IR 2")]
    [InlineData("I_OH_NAV_IR3_SWITCH_L", "IR 3")]
    public void The_adr_and_ir_off_lamps_are_named_for_their_system(string light, string name)
    {
        // Owner, 2026-10-22: "ADR 1: Off", not "OH ADIRS ADR1 Lower: On", when ADR 1 is switched off.
        Assert.Equal(name, Vars[light].DisplayName);
        Assert.Equal("Off", Vars[light].ValueDescriptions[1]);
    }

    [Fact]
    public void The_door_video_lamp_speaks_as_the_overhead_video_switch_reads()
    {
        // Measured: VIDEO Off (0) lights the lamp, VIDEO On (1) puts it out.
        var video = Vars["S_OH_COCKPIT_DOOR_VIDEO"].ValueDescriptions;
        var lamp = Vars["I_OH_COCKPIT_DOOR_VIDEO"].ValueDescriptions;
        Assert.Equal(video[0], lamp[1]);
        Assert.Equal(video[1], lamp[0]);
        Assert.Equal("Off", lamp[2]);
    }

    [Fact]
    public void The_pedestal_door_video_button_has_no_light_and_confirms_its_press()
    {
        var v = Vars["S_PED_COCKPIT_DOOR_VIDEO"];
        Assert.True(v.RenderAsButton);
        Assert.Null(v.StateVariable);
        Assert.Null(v.StateLitText);
        Assert.Null(v.StateDarkText);
    }

    [Fact]
    public void Only_the_off_legend_buttons_change_their_words()
    {
        var withTexts = Vars.Where(kv => kv.Value.StateLitText != null || kv.Value.StateDarkText != null)
            .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var expected = OffLegendButtons.Select(row => (string)row[0]).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, withTexts);
    }

    [Theory]
    [InlineData("MainForm.PanelBuilder.cs")]
    [InlineData("MainForm.Announcers.cs")]
    public void MainForm_labels_state_lights_through_the_definition(string partial)
    {
        string source = File.ReadAllText(MainFormPath(partial));
        Assert.Contains(".DescribeStateLight(", source);
        Assert.DoesNotContain("!= 0 ? \"On\" : \"Off\"", source);
    }

    [Fact]
    public void Both_altimeters_alike_are_spoken_once_for_both_sides()
    {
        Assert.Equal("Both altimeters set: 1015, 29.97.",
            FenixA320Definition.AltimeterSetText(1015.0, 29.97, false, 1015.0, 29.97, false));
    }

    [Fact]
    public void Altimeters_that_differ_are_spoken_side_by_side()
    {
        Assert.Equal("Captain altimeter 1015, 29.97. First officer altimeter 1014, 29.94.",
            FenixA320Definition.AltimeterSetText(1015.0, 29.97, false, 1014.0, 29.94, false));
    }

    [Fact]
    public void An_unread_side_is_named()
    {
        Assert.Equal("Captain altimeter 1015, 29.97. First officer altimeter not read.",
            FenixA320Definition.AltimeterSetText(1015.0, 29.97, false, null, null, false));
        Assert.Equal("Captain altimeter not read. First officer altimeter 1015, 29.97.",
            FenixA320Definition.AltimeterSetText(null, 29.97, false, 1015.0, 29.97, false));
        Assert.Null(FenixA320Definition.AltimeterSetText(null, null, false, null, null, false));
    }

    [Fact]
    public void A_side_left_in_std_is_named()
    {
        Assert.Equal("Captain altimeter 1015, 29.97. First officer altimeter still in STD.",
            FenixA320Definition.AltimeterSetText(1015.0, 29.97, false, 1015.0, 29.97, true));
        Assert.Equal("Captain altimeter still in STD. First officer altimeter still in STD.",
            FenixA320Definition.AltimeterSetText(1013.0, 29.92, true, 1013.0, 29.92, true));
    }

    [Fact]
    public void Altimeter_sides_compare_as_spoken()
    {
        // 1016.25 hPa and 1015.8 both speak 1016; 30.009 and 30.011 both speak 30.01.
        Assert.Equal("Both altimeters set: 1016, 30.01.",
            FenixA320Definition.AltimeterSetText(1016.25, 30.009, false, 1015.8, 30.011, false));
    }
}
