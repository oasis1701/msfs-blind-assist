using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The flex temperature and landing elevation boxes turn their knob ([A300-30]): the value is read fresh, the clicks that
/// reach the typed value are sent (a flex click a send of its own, landing elevation five to a send), and the value is read
/// again once they have landed; it is confirmed when it matches, or said where it stopped when it no longer moves. Against
/// a scripted fresh read, a recording sender and recorded waits.
/// </summary>
public class IniA300KnobEntryTests
{
    private const string Flex = A300TypedValues.FlexTemperatureKey;
    private const string Elevation = A300TypedValues.LandingElevationKey;
    private const string FlexReadout = A300Readouts.FlexTemperatureKey;
    private const string ElevationReadout = "A300_RO_LANDING_ELEV";
    private const string FlexDown = "-1 (>B:AIRLINER_FLEX_TEMP_Set)";
    private const string FlexUp = "1 (>B:AIRLINER_FLEX_TEMP_Set)";
    private const string ElevationUp = "1 (>B:AIRLINER_LANDING_ELEV_SET_Set)";
    private const string ElevationDown = "-1 (>B:AIRLINER_LANDING_ELEV_SET_Set)";

    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly List<string> _sent = new();
    private readonly List<int> _delays = new();
    private readonly List<TaskCompletionSource> _waits = new();
    private readonly Dictionary<string, Queue<double>> _reads = new();

    public IniA300KnobEntryTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = ms =>
            {
                _delays.Add(ms);
                return Task.CompletedTask;
            },
            ReadFresh = (_, key, _) =>
                Task.FromResult(_reads.TryGetValue(key, out var values) && values.Count > 0 ? values.Dequeue() : (double?)null),
        };
        _def.Attach(_sim);
    }

    /// <summary>What a knob's status line reads, one fresh read after another; a read past the last does not answer.</summary>
    private void Reads(string readoutKey, params double[] values) => _reads[readoutKey] = new Queue<double>(values);

    private bool Type(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    private static string Clicks(string click, int count) => string.Join(" ", Enumerable.Repeat(click, count));

    /// <summary>Every wait from here on is held until the test releases it.</summary>
    private void HoldEveryWait() => _def.TypedDelay = _ =>
    {
        var wait = new TaskCompletionSource();
        _waits.Add(wait);
        return wait.Task;
    };

    private void ReleaseAllWaits()
    {
        while (_waits.FirstOrDefault(w => !w.Task.IsCompleted) is { } wait)
            wait.SetResult();
    }

    [Fact]
    public void A_typed_flex_temperature_turns_the_knob_a_click_a_send_and_is_confirmed_once_read_back()
    {
        // Measured 2026-10-14: four clicks in one string moved the flex temperature one degree, so each click goes alone.
        Reads(FlexReadout, 58, 45);
        Assert.True(Type(Flex, 45));
        Assert.Equal(Enumerable.Repeat(FlexDown, 13), _sent);
        Assert.Equal(Enumerable.Repeat(IniA300Definition.KnobSendSpacingMs, 12).Append(IniA300Definition.KnobSettleMs), _delays);
        Assert.Equal(new[] { "Flex temperature 45 degrees" }, _speech.All);
    }

    [Theory]
    [InlineData(5450)]
    [InlineData(5434)]   // the knob's nearest click
    [InlineData(5425)]   // half a click: away from zero
    public void A_typed_landing_elevation_turns_the_knob_five_clicks_a_send(double typed)
    {
        // Measured 2026-10-14: 50 feet a click, and five clicks in one string all registered.
        Reads(ElevationReadout, 450, 5450);
        Assert.True(Type(Elevation, typed));
        Assert.Equal(Enumerable.Repeat(Clicks(ElevationUp, 5), 20), _sent);
        Assert.Equal(new[] { "Landing elevation 5450 feet" }, _speech.All);
    }

    [Fact]
    public void A_knob_that_does_not_move_says_where_it_stopped_after_one_round()
    {
        // Unpowered, or at a limit: a read that has not moved since the last round ends it, so nothing is sent forever.
        Reads(FlexReadout, 58, 58);
        Assert.True(Type(Flex, 45));
        Assert.Equal(Enumerable.Repeat(FlexDown, 13), _sent);
        Assert.Equal(new[] { "Flex temperature stopped at 58 degrees" }, _speech.All);
    }

    [Fact]
    public void A_knob_short_of_the_value_takes_another_round()
    {
        // Clicks can be lost: the next round sends what is still missing.
        Reads(FlexReadout, 58, 50, 45);
        Assert.True(Type(Flex, 45));
        Assert.Equal(Enumerable.Repeat(FlexDown, 13 + 5), _sent);
        Assert.Equal(new[] { "Flex temperature 45 degrees" }, _speech.All);
    }

    [Fact]
    public void A_knob_past_the_value_is_turned_back()
    {
        Reads(FlexReadout, 58, 40, 45);
        Assert.True(Type(Flex, 45));
        Assert.Equal(Enumerable.Repeat(FlexDown, 13).Concat(Enumerable.Repeat(FlexUp, 5)), _sent);
        Assert.Equal(new[] { "Flex temperature 45 degrees" }, _speech.All);
    }

    [Fact]
    public void A_knob_still_short_after_four_rounds_says_where_it_stopped()
    {
        // Each round moves it one degree only: four rounds of clicks, then the read after the last says where it is.
        Reads(FlexReadout, 58, 57, 56, 55, 54, 53);
        Assert.True(Type(Flex, 45));
        Assert.Equal(13 + 12 + 11 + 10, _sent.Count);
        Assert.Equal(new[] { "Flex temperature stopped at 54 degrees" }, _speech.All);
    }

    // A reading that is absurd (an unwritten or garbled status line) must not build a list of sends as long as the reading is,
    // on the UI thread: above about 1e10 the click count saturates its cast and the send throws, logged and silent. A round
    // sends the knob's whole travel at most, and the next read, which has not moved, says where the knob stopped.

    [Fact]
    public void A_flex_reading_far_above_the_range_turns_the_knob_down_no_further_than_its_travel()
    {
        Reads(FlexReadout, 1e12, 1e12);
        Assert.True(Type(Flex, 45));
        Assert.Equal(Enumerable.Repeat(FlexDown, 72), _sent);
        Assert.Equal(new[] { "Flex temperature stopped at 1000000000000 degrees" }, _speech.All);
    }

    [Fact]
    public void A_flex_reading_far_below_the_range_turns_the_knob_up_no_further_than_its_travel()
    {
        Reads(FlexReadout, -1e12, -1e12);
        Assert.True(Type(Flex, 45));
        Assert.Equal(Enumerable.Repeat(FlexUp, 72), _sent);
        Assert.Equal(new[] { "Flex temperature stopped at -1000000000000 degrees" }, _speech.All);
    }

    [Fact]
    public void A_landing_elevation_reading_far_above_the_range_turns_the_knob_down_no_further_than_its_travel()
    {
        Reads(ElevationReadout, 1e12, 1e12);
        Assert.True(Type(Elevation, 5450));
        Assert.Equal(Enumerable.Repeat(Clicks(ElevationDown, 5), 64), _sent);   // 320 clicks, five to a send
        Assert.Equal(new[] { "Landing elevation stopped at 1000000000000 feet" }, _speech.All);
    }

    [Fact]
    public void A_landing_elevation_reading_far_below_the_range_turns_the_knob_up_no_further_than_its_travel()
    {
        Reads(ElevationReadout, -1e12, -1e12);
        Assert.True(Type(Elevation, 5450));
        Assert.Equal(Enumerable.Repeat(Clicks(ElevationUp, 5), 64), _sent);
        Assert.Equal(new[] { "Landing elevation stopped at -1000000000000 feet" }, _speech.All);
    }

    [Fact]
    public void A_knob_already_at_the_value_is_confirmed_without_a_click()
    {
        Reads(FlexReadout, 45);
        Assert.True(Type(Flex, 45.4));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Flex temperature 45 degrees" }, _speech.All);
    }

    [Fact]
    public void A_read_that_does_not_answer_says_the_box_is_unavailable()
    {
        Assert.True(Type(Flex, 45));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Flex temperature setting unavailable" }, _speech.All);
    }

    [Theory]
    [InlineData(Flex, 73, "Flex temperature setting: 0 to 72 degrees")]
    [InlineData(Elevation, 14001, "Landing elevation setting: -2000 to 14000 feet")]
    [InlineData(Elevation, 5.45, "Landing elevation setting: type a whole number, without a thousands separator")]   // "5,450"
    [InlineData(Elevation, double.NaN,
        "Landing elevation setting: type a whole number, without a thousands separator; -2000 to 14000 feet")]   // "5 450"
    public void A_refused_value_says_why_and_turns_nothing(string key, double value, string said)
    {
        Reads(FlexReadout, 58, 45);
        Reads(ElevationReadout, 450, 5450);
        Assert.True(Type(key, value));
        Assert.Empty(_sent);
        Assert.Equal(new[] { said }, _speech.All);
    }

    [Fact]
    public void A_value_that_cannot_land_is_refused_aloud_and_nothing_is_read()
    {
        _def.CanLand = _ => false;
        Reads(FlexReadout, 58, 45);
        Assert.True(Type(Flex, 45));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Flex temperature setting unavailable" }, _speech.All);
        Assert.Equal(2, _reads[FlexReadout].Count);   // never read
    }

    [Fact]
    public void A_newer_entry_ends_the_older_one_with_nothing_more_sent_or_said()
    {
        HoldEveryWait();
        _def.ReadFresh = (_, _, _) => Task.FromResult<double?>(58);   // a knob that does not move
        Assert.True(Type(Flex, 45));   // its first click goes; the gap before the second is held
        Assert.True(Type(Flex, 50));   // the pilot types again
        Assert.Equal(new[] { FlexDown, FlexDown }, _sent);
        _sent.Clear();
        _waits[0].SetResult();         // the first entry's gap ends: it is no longer the knob's latest
        Assert.Empty(_sent);
        Assert.Empty(_speech.All);
        ReleaseAllWaits();             // the second goes on alone
        Assert.Equal(Enumerable.Repeat(FlexDown, 7), _sent);
        Assert.Equal(new[] { "Flex temperature stopped at 58 degrees" }, _speech.All);
    }

    [Fact]
    public void A_refused_entry_leaves_the_running_one_to_finish()
    {
        HoldEveryWait();
        Reads(FlexReadout, 58, 45);
        Assert.True(Type(Flex, 45));
        Assert.True(Type(Flex, 80));   // refused: it turns nothing, and ends nothing
        ReleaseAllWaits();
        Assert.Equal(Enumerable.Repeat(FlexDown, 13), _sent);
        Assert.Equal(new[] { "Flex temperature setting: 0 to 72 degrees", "Flex temperature 45 degrees" }, _speech.All);
    }

    [Fact]
    public void An_entry_on_one_knob_leaves_the_other_knob_turning()
    {
        HoldEveryWait();
        Reads(FlexReadout, 58, 45);
        Reads(ElevationReadout, 450, 5450);
        Assert.True(Type(Flex, 45));
        Assert.True(Type(Elevation, 5450));
        ReleaseAllWaits();
        Assert.Equal(13, _sent.Count(s => s == FlexDown));
        Assert.Equal(20, _sent.Count(s => s == Clicks(ElevationUp, 5)));
        Assert.Equal(new[] { "Flex temperature 45 degrees", "Landing elevation 5450 feet" }, _speech.All);
    }

    [Fact]
    public void A_definition_that_went_away_mid_turn_sends_and_says_nothing_more()
    {
        HoldEveryWait();
        _def.ReadFresh = (_, _, _) => Task.FromResult<double?>(58);
        Assert.True(Type(Flex, 45));
        _def.Dispose();
        ReleaseAllWaits();
        Assert.Equal(new[] { FlexDown }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_read_that_lands_after_the_definition_went_away_says_nothing()
    {
        // An aircraft switch disposes the definition before the pending reads are failed: that value is the old aircraft's.
        var read = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, _, _) => read.Task;
        Assert.True(Type(Flex, 45));
        _def.Dispose();
        read.SetResult(58);
        Assert.Empty(_sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Each_box_is_a_typed_row_on_the_panel_whose_status_line_it_confirms()
    {
        var vars = _def.GetVariables();
        foreach (var (key, panel, readout) in new[]
                 {
                     (Flex, "Thrust Rating Panel", FlexReadout),
                     (Elevation, "Landing Elevation", ElevationReadout),
                 })
        {
            // Write-only, never batch-covered ([A300-9]); an empty box arrives as NaN, which the range refuses.
            Assert.Equal((UpdateFrequency.Never, true), (vars[key].UpdateFrequency, vars[key].UnparseableTextAsNaN));
            Assert.Contains(key, _def.GetPanelControls()[panel]);
            Assert.Contains(readout, _def.GetPanelDisplayVariables()[panel]);
        }
        // The landing elevation knob left the panels, so its panel is its box alone.
        Assert.Equal(new[] { Elevation }, _def.GetPanelControls()["Landing Elevation"]);
        Assert.Contains("Landing Elevation", _def.GetPanelStructure()["Instrument"]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_turn_sends_and_speaks_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Reads(FlexReadout, 58, 45);
            Reads(ElevationReadout, 450, 5450);
            Assert.True(Type(Flex, 45.4));
            Assert.True(Type(Elevation, 5434));
            Assert.Equal(Enumerable.Repeat(FlexDown, 13).Concat(Enumerable.Repeat(Clicks(ElevationUp, 5), 20)), _sent);
            Assert.Equal(new[] { "Flex temperature 45 degrees", "Landing elevation 5450 feet" }, _speech.All);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
