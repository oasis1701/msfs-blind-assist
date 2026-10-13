using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The thrust rating panel, read from the aircraft's code (1.0.11): each button's lamp is
/// INI_TRP_MODE == its number (1 TOGA, 2 MCT, 3 CL, 4 CR, 5 AUTO, 6 FLEX TO); in AUTO the mode stays 5
/// and INI_TRP_AUTO_MODE holds the limit the aircraft chose (1 TOGA, 3 climb, 4 cruise, updateN1limit);
/// INI_THRUST_MAXIMUM_N1 is the active limit's N1.
/// </summary>
public class A300TrpTests
{
    [Theory]
    [InlineData("A300_TRP_TOGA", 1.0, "On")]
    [InlineData("A300_TRP_MCT", 2.0, "On")]
    [InlineData("A300_TRP_CL", 3.0, "On")]
    [InlineData("A300_TRP_CR", 4.0, "On")]
    [InlineData("A300_TRP_AUTO", 5.0, "On")]
    [InlineData("A300_TRP_FLEXTO", 6.0, "On")]
    [InlineData("A300_TRP_CL", 5.0, "Off")]   // AUTO choosing the climb limit lights AUTO, not CL
    [InlineData("A300_TRP_TOGA", 6.0, "Off")]
    public void A_button_shows_its_lamp(string rowKey, double mode, string state) =>
        Assert.Equal(state, A300Trp.ButtonState(A300Trp.ModeByButton[rowKey], mode));

    [Fact]
    public void A_button_with_the_mode_unread_shows_no_state() =>
        Assert.Null(A300Trp.ButtonState(3, null));

    [Theory]
    [InlineData(6.0, null, 45.0, null, null, "FLEX, 45 degrees")]
    [InlineData(5.0, 3.0, null, null, null, "AUTO, climb")]
    [InlineData(5.0, 1.0, null, null, null, "AUTO, TOGA")]
    [InlineData(5.0, 4.0, null, null, null, "AUTO, cruise")]
    [InlineData(5.0, null, null, null, null, "AUTO")]
    [InlineData(1.0, 3.0, null, null, null, "TOGA")]
    [InlineData(2.0, null, null, null, null, "max continuous")]
    [InlineData(3.0, null, null, null, null, "climb")]
    [InlineData(4.0, null, null, null, null, "cruise")]
    [InlineData(6.0, null, null, null, null, "FLEX")]
    [InlineData(5.0, 3.0, null, 95.34, 0.0, "AUTO, climb, N1 limit 95.3 percent")]
    [InlineData(6.0, null, 45.0, 92.06, 0.0, "FLEX, 45 degrees, N1 limit 92.1 percent")]
    [InlineData(5.0, 3.0, null, 95.34, 1.0, "AUTO, climb")]      // PW engines: the TRP shows EPR, not N1
    [InlineData(5.0, 3.0, null, 95.34, null, "AUTO, climb")]     // engine type not read: no N1
    public void The_trp_line_reads_the_mode_its_limit_or_flex_temperature_and_the_n1_limit(
        double mode, double? autoMode, double? flex, double? n1, double? pw, string line) =>
        Assert.Equal(line, A300Trp.Line(mode, autoMode, flex, n1, pw));

    [Fact]
    public void A_flex_knob_step_reads_back_as_the_flex_temperature() =>
        Assert.Equal("Flex temperature 46 degrees", A300Trp.FlexPhrase(46));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_trp_line_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("AUTO, climb, N1 limit 95.3 percent", A300Trp.Line(5, 3, null, 95.34, 0));
            Assert.Equal("Flex temperature 46 degrees", A300Trp.FlexPhrase(46));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

/// <summary>The TRP in the definition: the variables it reads, the buttons' labels, the Thrust Rating
/// Panel's TRP line and the flex knob's read-back.</summary>
public class IniA300TrpTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();

    public IniA300TrpTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = _ => Task.CompletedTask,
            ReadFresh = (_, key, _) => Task.FromResult(_cache.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    [Fact]
    public void The_trp_variables_stream_on_their_own_subscriptions_and_are_consumed_silently()
    {
        var vars = _def.GetVariables();
        var expected = new Dictionary<string, string>
        {
            [A300Trp.ModeKey] = "INI_TRP_MODE",
            [A300Trp.AutoModeKey] = "INI_TRP_AUTO_MODE",
            [A300Trp.N1LimitKey] = "INI_THRUST_MAXIMUM_N1",
            [A300Trp.PwEnginesKey] = "INI_IS_PW",
        };
        foreach (var (key, name) in expected)
        {
            var def = vars[key];
            Assert.Equal((name, UpdateFrequency.Continuous, true, true),
                (def.Name, def.UpdateFrequency, def.ExcludeFromBatch, def.ExcludeFromMonitorManager));
            Assert.False(ContinuousBatchLayout.RidesBatch(def));   // [A300-9]: never split the FMA's batch
            Assert.True(_def.ProcessSimVarUpdate(key, 1, _speech));
        }
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Each_trp_button_is_relabelled_from_the_mode_and_shows_its_lamp()
    {
        foreach (var rowKey in A300Trp.ModeByButton.Keys)
            Assert.Equal(new[] { A300Trp.ModeKey }, _def.GetVariables()[rowKey].StateVariables);
        Assert.False(_def.TryDescribeControlState("A300_TRP_CL", out _));   // nothing read yet
        _cache[A300Trp.ModeKey] = 5;
        Assert.True(_def.TryDescribeControlState("A300_TRP_AUTO", out var auto));
        Assert.True(_def.TryDescribeControlState("A300_TRP_CL", out var climb));
        Assert.Equal(("On", "Off"), (auto, climb));
    }

    [Fact]
    public void The_thrust_rating_panel_status_box_has_the_trp_line()
    {
        Assert.Contains(A300Trp.ModeKey, _def.GetPanelDisplayVariables()["Thrust Rating Panel"]);
        var def = _def.GetVariables()[A300Trp.ModeKey];
        Assert.Equal("TRP", def.DisplayName);
        // Any of its parts changing repaints the box (the line's own key too: its deliveries are consumed).
        Assert.Equal(new[] { A300Trp.ModeKey, A300Trp.AutoModeKey, A300Readouts.FlexTemperatureKey, A300Trp.N1LimitKey, A300Trp.PwEnginesKey },
            def.StateVariables);
        _cache[A300Trp.AutoModeKey] = 3;
        _cache[A300Readouts.FlexTemperatureKey] = 45;
        _cache[A300Trp.N1LimitKey] = 92.06;
        _cache[A300Trp.PwEnginesKey] = 0;
        Assert.True(_def.TryGetDisplayOverride(A300Trp.ModeKey, 6, out var line));
        Assert.Equal("FLEX, 45 degrees, N1 limit 92.1 percent", line);
    }

    [Fact]
    public async Task A_flex_knob_step_is_read_back_once_it_lands()
    {
        _cache[A300Readouts.FlexTemperatureKey] = 46;
        Assert.True(Set("A300_FLEX_TEMP#INC", 1));
        await Task.Yield();
        Assert.Equal(new[] { "1 (>B:AIRLINER_FLEX_TEMP_Set)" }, _sent);
        Assert.Equal(new[] { "Flex temperature 46 degrees" }, _speech.Interrupts);
    }

    [Fact]
    public void A_trp_press_says_nothing()
    {
        _cache[A300Trp.ModeKey] = 1;
        Assert.True(Set("A300_TRP_CL", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_TRP_CL_Set)" }, _sent);
        Assert.Empty(_speech.All);
    }
}
