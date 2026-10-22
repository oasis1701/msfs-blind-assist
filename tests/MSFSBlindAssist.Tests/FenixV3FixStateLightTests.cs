// Fenix buttons whose state light is an OFF legend, and the altimeter read-back after a typed setting
// (owner check of build 2, 2026-10-22). Measured live: pressing ADR 1 or IR 1 lights its lower legend and
// switches it off; Bus Tie and Cockpit Door Video carry one OFF legend, dark in the normal state. MainForm
// labelled every StateVariable button "On" when lit, so these read backwards ("ADR 1: On" with ADR 1 off).

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
        { "S_PED_COCKPIT_DOOR_VIDEO", "I_OH_COCKPIT_DOOR_VIDEO", "On" },
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

    [Fact]
    public void Only_the_measured_off_legend_buttons_change_their_words()
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
            FenixA320Definition.AltimeterSetText(1015.0, 29.97, 1015.0, 29.97));
    }

    [Fact]
    public void Altimeters_that_differ_are_spoken_side_by_side()
    {
        Assert.Equal("Captain altimeter 1015, 29.97. First officer altimeter 1014, 29.94.",
            FenixA320Definition.AltimeterSetText(1015.0, 29.97, 1014.0, 29.94));
    }

    [Fact]
    public void An_unread_side_is_named()
    {
        Assert.Equal("Captain altimeter 1015, 29.97. First officer altimeter not read.",
            FenixA320Definition.AltimeterSetText(1015.0, 29.97, null, null));
        Assert.Equal("Captain altimeter not read. First officer altimeter 1015, 29.97.",
            FenixA320Definition.AltimeterSetText(null, 29.97, 1015.0, 29.97));
        Assert.Null(FenixA320Definition.AltimeterSetText(null, null, null, null));
    }

    [Fact]
    public void Altimeter_sides_compare_as_spoken()
    {
        // 1016.25 hPa and 1015.8 both speak 1016; 30.009 and 30.011 both speak 30.01.
        Assert.Equal("Both altimeters set: 1016, 30.01.",
            FenixA320Definition.AltimeterSetText(1016.25, 30.009, 1015.8, 30.011));
    }
}
