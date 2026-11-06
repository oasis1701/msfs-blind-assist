// Characterization tests for FenixChannelDcduBrightness, which says how bright the captain's DCDU is after the pilot presses
// Bright or Dim, and for the L:var the window reads it from (N_DCDU1_BRIGHTNESS, the part the cockpit file names as what
// lights the captain's DCDU). Pure logic.
//
// The contract: the L:var is the Fenix's own count (0 to 510) over 1023, and is said as a percentage of that count to the
// nearest ten, "Brightness 60 percent", clamped to 0 to 100, except that a lit DCDU (any count above 0) is never 0 percent: it
// is at least 10; a value that was not read (null, or not a number) is "Brightness not available". Measured on build 3.0.0.7613 (2026-11-03): each 200 ms press of S_DCDU1_DIM_BRT moves the count
// about 50; from 0 on Bright it read 60, 110, 160, 210, 260, 310, 360, 410, 470, 510, and it stops at 510; at the moment the
// Fenix's own value was 510 the L:var read 0.49853372434017595, which is 510 / 1023. The words are the app's own, so the PC's
// culture changes none of them.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduBrightnessTests
{
    // The L:var for a count of the Fenix's own value: the count over 1023.
    private static double Level(double count) => count / 1023.0;

    // The ten steps of Bright from 0, as measured (the Fenix's count), and the percentage each is said as.
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
    public void The_level_is_the_cockpit_files_lvar_and_is_the_count_over_1023_with_510_at_the_top()
    {
        Assert.Equal("N_DCDU1_BRIGHTNESS", FenixChannelDcduBrightness.LevelKey);
        Assert.Equal(1023, FenixChannelDcduBrightness.CountRange);
        Assert.Equal(510, FenixChannelDcduBrightness.FullCount);
        Assert.Equal(0.49853372434017595, FenixChannelDcduBrightness.FullCount / FenixChannelDcduBrightness.CountRange);
    }

    [Theory]
    [MemberData(nameof(MeasuredSteps))]
    public void The_measured_steps_are_said_to_the_nearest_ten(double count, int percent)
    {
        double value = Level(count);

        Assert.Equal(percent, FenixChannelDcduBrightness.Percent(value));
        Assert.Equal($"Brightness {percent} percent", FenixChannelDcduBrightness.Spoken(value));
    }

    [Fact]
    public void The_measured_lvar_at_full_brightness_is_a_hundred_percent()
    {
        // Read with the Fenix's own value at 510 (2026-11-03): the L:var is a fraction, not the count. Read as a count it was
        // "Brightness 0 percent" on every press.
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(0.49853372434017595));
        Assert.Equal("Brightness 100 percent", FenixChannelDcduBrightness.Spoken(0.49853372434017595));
    }

    [Fact]
    public void The_percentage_is_rounded_to_the_nearest_ten_with_a_half_going_up()
    {
        // 25 percent of a count of 510 is 127.5: halfway between 20 and 30, and it is 30 (away from zero, not to the even ten).
        Assert.Equal(30, FenixChannelDcduBrightness.Percent(Level(127.5)));
        Assert.Equal(20, FenixChannelDcduBrightness.Percent(Level(127.4)));
        Assert.Equal(10, FenixChannelDcduBrightness.Percent(Level(25.5)));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(Level(484.5)));
        Assert.Equal(90, FenixChannelDcduBrightness.Percent(Level(484.4)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(20, 10)]
    [InlineData(25.4, 10)]
    [InlineData(60, 10)]
    [InlineData(510, 100)]
    public void A_lit_dcdu_never_says_zero_percent_and_only_a_true_zero_does(double count, int percent)
    {
        // The level steps down to 20 of 510 before it reaches 0 (measured 2026-11-03), and the DCDU is still lit at 20: rounding
        // 20 to the nearest ten gave 0, and "Brightness 0 percent" was said over a lit screen. Any count above 0 is at least 10.
        double value = Level(count);

        Assert.Equal(percent, FenixChannelDcduBrightness.Percent(value));
        Assert.Equal($"Brightness {percent} percent", FenixChannelDcduBrightness.Spoken(value));
    }

    [Fact]
    public void The_percentage_stays_between_nothing_and_a_hundred()
    {
        Assert.Equal(0, FenixChannelDcduBrightness.Percent(Level(-50)));
        Assert.Equal(0, FenixChannelDcduBrightness.Percent(double.NegativeInfinity));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(Level(520)));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(Level(5100)));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(1.0));
        Assert.Equal(100, FenixChannelDcduBrightness.Percent(double.PositiveInfinity));
        Assert.Equal(0, FenixChannelDcduBrightness.Percent(double.NaN));
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
                foreach ((double count, int percent) in new[] { (0.0, 0), (20.0, 10), (60.0, 10), (310.0, 60), (470.0, 90), (510.0, 100) })
                    Assert.Equal($"Brightness {percent.ToString(CultureInfo.InvariantCulture)} percent", FenixChannelDcduBrightness.Spoken(Level(count)));

                Assert.Equal("Brightness 60 percent", FenixChannelDcduBrightness.Spoken(Level(310)));
                Assert.Equal("Brightness 100 percent", FenixChannelDcduBrightness.Spoken(0.49853372434017595));
                Assert.Equal("Brightness not available", FenixChannelDcduBrightness.Spoken(null));
                Assert.Equal(30, FenixChannelDcduBrightness.Percent(Level(127.5)));
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
