// Characterization tests for FenixChannelDcduBrightness, which says how bright the captain's DCDU is after the pilot presses
// Bright or Dim, and for the L:var the window reads it from (N_DCDU1_BRIGHTNESS, the part the cockpit file names as what
// lights the captain's DCDU). Pure logic.
//
// The contract: the value runs 0 to 510 and is said as a percentage to the nearest ten, "Brightness 60 percent", clamped to
// 0 to 100; a value that was not read (null, or not a number) is "Brightness not available". Measured on build 3.0.0.7613
// (2026-11-03): each 200 ms press of S_DCDU1_DIM_BRT moves it about 50; from 0 on Bright it read 60, 110, 160, 210, 260,
// 310, 360, 410, 470, 510, and it stops at 510. The words are the app's own, so the PC's culture changes none of them.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduBrightnessTests
{
    // The ten steps of Bright from 0, as measured, and the percentage each is said as.
    public static readonly TheoryData<double, int> MeasuredSteps = new()
    {
        { 0, 0 }, { 60, 10 }, { 110, 20 }, { 160, 30 }, { 210, 40 }, { 260, 50 },
        { 310, 60 }, { 360, 70 }, { 410, 80 }, { 470, 90 }, { 510, 100 }
    };

    private static void InCulture(string name, Action action)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_level_is_the_cockpit_files_lvar_and_runs_to_510()
    {
        Assert.Equal("N_DCDU1_BRIGHTNESS", FenixChannelDcduBrightness.LevelKey);
        Assert.Equal(510, FenixChannelDcduBrightness.FullScale);
    }

    [Theory]
    [MemberData(nameof(MeasuredSteps))]
    public void The_measured_steps_are_said_to_the_nearest_ten(double value, int percent)
    {
        Assert.Equal(percent, FenixChannelDcduBrightness.Percent(value));
        Assert.Equal($"Brightness {percent} percent", FenixChannelDcduBrightness.Spoken(value));
    }

    [Fact]
    public void The_percentage_is_rounded_to_the_nearest_ten_with_a_half_going_up()
    {
        // 25 percent of 510 is 127.5: halfway between 20 and 30, and it is 30 (away from zero, not to the even ten).
        Assert.Equal(30, FenixChannelDcduBrightness.Percent(127.5));
        Assert.Equal(20, FenixChannelDcduBrightness.Percent(127.4));
        Assert.Equal(10, FenixChannelDcduBrightness.Percent(25.5));
        Assert.Equal(0, FenixChannelDcduBrightness.Percent(25.4));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(484.5));
        Assert.Equal(90, FenixChannelDcduBrightness.Percent(484.4));
    }

    [Fact]
    public void The_percentage_stays_between_nothing_and_a_hundred()
    {
        Assert.Equal(0, FenixChannelDcduBrightness.Percent(-50));
        Assert.Equal(0, FenixChannelDcduBrightness.Percent(double.NegativeInfinity));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(520));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(5100));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(double.PositiveInfinity));
    }

    [Fact]
    public void A_value_that_was_not_read_is_not_available()
    {
        Assert.Equal("Brightness not available", FenixChannelDcduBrightness.Spoken(null));
        Assert.Equal("Brightness not available", FenixChannelDcduBrightness.Spoken(double.NaN));
        Assert.Equal("Brightness not available", FenixChannelDcduBrightness.Spoken(double.PositiveInfinity));
        Assert.Equal("Brightness not available", FenixChannelDcduBrightness.Spoken(double.NegativeInfinity));
    }

    [Fact]
    public void The_words_are_the_same_in_de_DE_and_sv_SE()
    {
        // CI runs under en-US: a figure written with the PC's culture could read differently. These do not.
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            InCulture(culture, () =>
            {
                foreach ((double value, int percent) in new[] { (0.0, 0), (60.0, 10), (310.0, 60), (470.0, 90), (510.0, 100) })
                    Assert.Equal($"Brightness {percent.ToString(CultureInfo.InvariantCulture)} percent", FenixChannelDcduBrightness.Spoken(value));

                Assert.Equal("Brightness 60 percent", FenixChannelDcduBrightness.Spoken(310));
                Assert.Equal("Brightness 100 percent", FenixChannelDcduBrightness.Spoken(510));
                Assert.Equal("Brightness not available", FenixChannelDcduBrightness.Spoken(null));
                Assert.Equal(30, FenixChannelDcduBrightness.Percent(127.5));
            });
        }
    }

    [Fact]
    public void The_definition_registers_the_level_as_an_on_request_lvar_on_no_panel()
    {
        // Read by the window with ReadFreshAsync and consumed by nothing else, so it must stay out of the Ctrl+M monitor list
        // ([VAR-9]) and on no panel.
        var definition = new FenixA320Definition();
        Dictionary<string, SimVarDefinition> variables = definition.GetVariables();

        Assert.True(variables.TryGetValue(FenixChannelDcduBrightness.LevelKey, out SimVarDefinition? level),
            "N_DCDU1_BRIGHTNESS is not registered");
        Assert.Equal(FenixChannelDcduBrightness.LevelKey, level!.Name);
        Assert.Equal(SimVarType.LVar, level.Type);
        Assert.Equal(UpdateFrequency.OnRequest, level.UpdateFrequency);
        Assert.True(level.ExcludeFromMonitorManager, "the level would earn a Ctrl+M checkbox that mutes nothing");
        Assert.False(level.IsAnnounced);

        Assert.DoesNotContain(definition.GetPanelControls(), panel => panel.Value.Contains(FenixChannelDcduBrightness.LevelKey));
    }
}
