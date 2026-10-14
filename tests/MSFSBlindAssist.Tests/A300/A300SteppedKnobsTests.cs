using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The two knobs a typed box turns ([A300-30]): how many clicks reach a value, and how the clicks are sent. Measured
/// 2026-10-14 in cruise (1.0.11): the landing elevation knob steps 50 feet a click and five clicks in one calculator
/// string all registered; the flex temperature knob steps one degree a click, and four clicks in one string moved it
/// one degree, so a flex click is a send of its own.
/// </summary>
public class A300SteppedKnobsTests
{
    private const string FlexDown = "-1 (>B:AIRLINER_FLEX_TEMP_Set)";
    private const string ElevationUp = "1 (>B:AIRLINER_LANDING_ELEV_SET_Set)";
    private const string ElevationDown = "-1 (>B:AIRLINER_LANDING_ELEV_SET_Set)";

    private static readonly A300ControlMap Map = A300ControlMap.Load();

    private static A300Control Control(A300SteppedKnob knob) => Map.FindByKey(knob.ControlKey)!;

    private static string Clicks(string click, int count) => string.Join(" ", Enumerable.Repeat(click, count));

    [Theory]
    [InlineData(58, 45, 1, -13)]
    [InlineData(450, 5450, 50, 100)]
    [InlineData(450, 470, 50, 0)]
    [InlineData(450, 480, 50, 1)]      // nearer the next click
    [InlineData(475, 450, 50, -1)]     // half a click: away from zero
    [InlineData(13, 450, 50, 9)]       // a value between clicks (463 is the nearest the knob reaches)
    public void Clicks_are_the_signed_steps_to_the_nearest_click(double current, double target, double step, int clicks) =>
        Assert.Equal(clicks, A300SteppedKnobs.ClicksFor(current, target, step));

    [Fact]
    public void A_flex_click_is_a_send_of_its_own() =>
        Assert.Equal(Enumerable.Repeat(FlexDown, 3), A300SteppedKnobs.Sends(Control(A300SteppedKnobs.Flex), -3, 1));

    [Fact]
    public void Landing_elevation_clicks_go_five_to_a_send()
    {
        var knob = Control(A300SteppedKnobs.LandingElevation);
        Assert.Equal(new[] { Clicks(ElevationUp, 5), Clicks(ElevationUp, 5), Clicks(ElevationUp, 2) },
            A300SteppedKnobs.Sends(knob, 12, 5));
        Assert.Equal(new[] { Clicks(ElevationDown, 5), Clicks(ElevationDown, 2) }, A300SteppedKnobs.Sends(knob, -7, 5));
    }

    [Fact]
    public void No_click_is_no_send() => Assert.Empty(A300SteppedKnobs.Sends(Control(A300SteppedKnobs.Flex), 0, 1));

    [Fact]
    public void A_send_holds_at_least_one_click() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => A300SteppedKnobs.Sends(Control(A300SteppedKnobs.Flex), 3, 0));

    [Fact]
    public void Each_knob_turns_its_map_encoder_and_reads_back_the_status_line_beside_its_box()
    {
        var flex = A300SteppedKnobs.Flex;
        Assert.Equal((A300TypedValues.FlexTemperatureKey, A300Trp.FlexKnobKey, A300Readouts.FlexTemperatureKey, 1.0, 1),
            (flex.TypedKey, flex.ControlKey, flex.ReadoutKey, flex.Step, flex.ClicksPerSend));
        var elevation = A300SteppedKnobs.LandingElevation;
        Assert.Equal((A300TypedValues.LandingElevationKey, "A300_LANDING_ELEV_SET", "A300_RO_LANDING_ELEV", 50.0, 5),
            (elevation.TypedKey, elevation.ControlKey, elevation.ReadoutKey, elevation.Step, elevation.ClicksPerSend));
        foreach (var knob in new[] { flex, elevation })
        {
            Assert.Equal(A300Kinds.Encoder, Control(knob).Kind);
            var box = A300TypedValues.All.Single(t => t.Key == knob.TypedKey);
            Assert.Equal(box.Panel, A300Readouts.All.Single(r => r.Key == knob.ReadoutKey).Panel);
        }
    }

    [Fact]
    public void The_flex_temperature_and_landing_elevation_boxes_are_the_two_that_turn_a_knob()
    {
        Assert.Same(A300SteppedKnobs.Flex, A300SteppedKnobs.For(A300TypedValues.FlexTemperatureKey));
        Assert.Same(A300SteppedKnobs.LandingElevation, A300SteppedKnobs.For(A300TypedValues.LandingElevationKey));
        Assert.All(A300TypedValues.All, typed =>
            Assert.Equal(A300SteppedKnobs.For(typed.Key) != null, A300TypedValues.IsKnobTyped(typed.Key)));
        Assert.Equal(2, A300TypedValues.All.Count(typed => A300TypedValues.IsKnobTyped(typed.Key)));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_knobs_send_and_speak_the_same_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(Enumerable.Repeat(FlexDown, 3), A300SteppedKnobs.Sends(Control(A300SteppedKnobs.Flex), -3, 1));
            Assert.Equal(new[] { Clicks(ElevationUp, 5), Clicks(ElevationUp, 5), Clicks(ElevationUp, 2) },
                A300SteppedKnobs.Sends(Control(A300SteppedKnobs.LandingElevation), 12, 5));
            Assert.Equal("Flex temperature 45 degrees", A300SteppedKnobs.Flex.Phrase(45));
            Assert.Equal("Flex temperature stopped at 72 degrees", A300SteppedKnobs.Flex.StoppedPhrase(72));
            // The landing elevation in its status line's format: no thousands separator.
            Assert.Equal("Landing elevation 5450 feet", A300SteppedKnobs.LandingElevation.Phrase(5450));
            Assert.Equal("Landing elevation stopped at 14000 feet", A300SteppedKnobs.LandingElevation.StoppedPhrase(14000));
            Assert.Equal("Landing elevation -50 feet", A300SteppedKnobs.LandingElevation.Phrase(-50));
            Assert.Equal("Landing elevation 0 feet", A300SteppedKnobs.LandingElevation.Phrase(-0.3));   // never "-0"
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
