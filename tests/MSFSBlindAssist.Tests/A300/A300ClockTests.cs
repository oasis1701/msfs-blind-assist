using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The two clocks on the main panel, read from the aircraft's own clock variables (measured 2026-10-10): the
/// GMT digits, the chrono (its button cycles reset, running, stopped; seconds) and the elapsed time (its button
/// toggles running and stopped, restarting from zero; seconds).
/// </summary>
public class A300ClockTests
{
    private static readonly A300Placement Placement = A300PanelLayout.Place(A300ControlMap.Load());

    private static A300PlacedRow Row(string key) => Placement.RowsByPanel.Values.SelectMany(r => r).Single(r => r.Key == key);

    [Theory]
    [InlineData(0, 7, 3, 8, "07:38 GMT")]
    [InlineData(2, 3, 5, 9, "23:59 GMT")]
    public void The_clock_reads_its_gmt_digits(double h1, double h2, double m1, double m2, string text) =>
        Assert.Equal(text, A300Clock.Time(h1, h2, m1, m2));

    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(4.07, "4 seconds")]
    [InlineData(65, "1 minute 5 seconds")]
    [InlineData(120, "2 minutes 0 seconds")]
    public void The_chrono_reads_minutes_and_seconds(double seconds, string text) =>
        Assert.Equal(text, A300Clock.Chrono(seconds));

    [Theory]
    [InlineData(3.17, "0 minutes")]
    [InlineData(120, "2 minutes")]
    [InlineData(3900, "1 hour 5 minutes")]
    [InlineData(7260, "2 hours 1 minute")]
    public void The_elapsed_time_reads_hours_and_minutes(double seconds, string text) =>
        Assert.Equal(text, A300Clock.Elapsed(seconds));

    [Fact]
    public void Clock_text_reads_the_same_in_any_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1 minute 5 seconds", A300Clock.Chrono(65.4));
            Assert.Equal("07:38 GMT", A300Clock.Time(0, 7, 3, 8));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("A300_CPT_CLOCK_START", "A300_CPT_CLOCK_START", 1, "Running")]
    [InlineData("A300_CPT_CLOCK_START", "A300_CPT_CLOCK_START", 2, "Stopped")]
    [InlineData("A300_CPT_CLOCK_START", "A300_CPT_CLOCK_START", 0, "Reset")]
    [InlineData("A300_FO_CLOCK_RUN", "A300_FO_CLOCK_RUN", 1, "Running")]
    [InlineData("A300_FO_CLOCK_RUN", "A300_FO_CLOCK_RUN", 2, "Stopped")]
    public void A_clock_button_is_labelled_by_its_state(string button, string stateOf, double state, string word)
    {
        var cache = new Dictionary<string, double> { [A300Clock.StateKey(stateOf)] = state };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.Equal(A300RowAction.Press, Row(button).Action);
        Assert.Contains(A300Clock.StateKey(stateOf), def.GetVariables()[button].StateVariables!);
        Assert.True(def.TryDescribeControlState(button, out var text));
        Assert.Equal(word, text);
    }

    [Fact]
    public void A_clock_state_is_consumed_silently()
    {
        var def = new IniA300Definition();
        var speech = new SpeechCapture();
        Assert.True(def.ProcessSimVarUpdate(A300Clock.StateKey("A300_CPT_CLOCK_RUN"), 1, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_clock_box_reads_both_clocks()
    {
        var box = new IniA300Definition().GetPanelDisplayVariables()["Clock"];
        Assert.Equal(new[]
        {
            A300Clock.CaptainTimeKey, "A300_RO_CHRONO_CPT", "A300_RO_ET_CPT",
            A300Clock.FirstOfficerTimeKey, "A300_RO_CHRONO_FO", "A300_RO_ET_FO",
        }, box);
    }

    [Fact]
    public void The_clock_time_line_is_composed_from_its_digits()
    {
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        foreach (var (key, v) in A300Clock.CaptainDigitKeys.Zip(new double[] { 0, 7, 3, 8 }))
            cache[key] = v;
        Assert.True(def.TryGetDisplayOverride(A300Clock.CaptainTimeKey, 20, out var text));
        Assert.Equal("07:38 GMT", text);
    }
}
