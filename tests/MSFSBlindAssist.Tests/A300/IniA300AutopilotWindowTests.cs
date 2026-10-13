using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>Ctrl+P's window: labels, state in the aircraft's own words, presses through the panel path
/// that say nothing when they work, and the status list.</summary>
public class IniA300AutopilotWindowTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly List<string> _requested = new();

    public IniA300AutopilotWindowTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            RequestRead = (key, _) => _requested.Add(key),
        };
        _def.Attach(_sim);
    }

    private static string Set(string key, double value) => $"{value} (>B:{A300ControlMap.Load().FindByKey(key)!.Event})";

    [Fact]
    public void The_buttons_are_named_as_their_rows_with_alt_letters()
    {
        Assert.Equal(new[]
        {
            "Autopilot &1", "Autopilot &2", "C&WS", "&VOR LOC", "&Land", "Au&tothrottle", "Autothrottle 1",
            "Autothrottle 2", "Captain autopilot &disconnect", "Aut&othrottle disconnect", "TO&GA", "&Bank limit",
        }, _def.AutopilotButtons(_sim, _speech).Select(b => b.Label));
    }

    [Fact]
    public void A_switch_shows_its_position_in_its_rows_words_and_a_lamp_its_lamp()
    {
        var buttons = _def.AutopilotButtons(_sim, _speech);
        Assert.Equal("", buttons[0].GetCurrentState());
        _cache["A300_AP_SWITCH_1"] = 1;
        _cache["A300_FCU_LT_VL"] = 1;
        _cache["A300_BANK_KNOB"] = 0;
        Assert.Equal("Engaged", buttons[0].GetCurrentState());
        Assert.Equal("On", buttons[3].GetCurrentState());
        Assert.Equal("Off", buttons[11].GetCurrentState());
    }

    [Fact]
    public void A_switch_flips_from_its_known_position_and_says_nothing()
    {
        _cache["A300_AP_SWITCH_1"] = 0;
        _def.AutopilotButtons(_sim, _speech)[0].OnPressed();
        Assert.Equal(new[] { Set("A300_AP_SWITCH_1", 1) }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_switch_in_an_unknown_position_is_refused_aloud()
    {
        _def.AutopilotButtons(_sim, _speech)[0].OnPressed();
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Switch not ready, please try again in a moment." }, _speech.All);
    }

    [Fact]
    public void A_mode_button_is_pressed_and_says_nothing()
    {
        _def.AutopilotButtons(_sim, _speech)[3].OnPressed();
        Assert.Equal(new[] { Set("A300_VL_BUTTON", 1) }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_flight_directors_are_combos_named_for_their_side()
    {
        var selectors = _def.AutopilotSelectors(_sim, _speech);
        Assert.Equal(new[] { "Captain flight director", "First officer flight director" }, selectors.Select(s => s.Label));
        Assert.Equal(new[] { "Off", "Normal", "Flight path vector" }, selectors[0].Positions.OrderBy(p => p.Key).Select(p => p.Value));
        _cache["A300_CPT_FLT_DIR"] = 1;
        Assert.Equal(1.0, selectors[0].GetCurrentValue());
        selectors[0].OnSelected(2);
        Assert.Equal(new[] { Set("A300_CPT_FLT_DIR", 2) }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_status_lines_ask_again_for_the_on_request_windows()
    {
        _cache[A300Readouts.SpeedKey] = 250;
        var lines = _def.AutopilotStatusLines(_sim);
        Assert.Equal("Speed 250 knots", lines[0]);
        Assert.Equal(A300AutopilotStatus.RequestedKeys, _requested);
    }
}
