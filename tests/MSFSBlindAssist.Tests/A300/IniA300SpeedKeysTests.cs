using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The speed-tape keys, as on the A320s: each reads its speed fresh and says it, or says it is not
/// shown or not available. The readouts never touch the form or the hotkey manager. The B key and the
/// fuel keys are in here too: the same read-fresh-and-say shape.
/// </summary>
public class IniA300SpeedKeysTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _fresh = new();
    private readonly Dictionary<string, double> _cache = new();

    public IniA300SpeedKeysTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            ReadFresh = (_, key, _) => Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
    }

    private string[] Press(HotkeyAction action)
    {
        Assert.True(_def.HandleHotkeyAction(action, _sim, _speech, null!, null!));
        return _speech.All.ToArray();
    }

    [Theory]
    [InlineData(HotkeyAction.ReadSpeedVLS, A300Readouts.VlsKey, "VLS 180 knots")]
    [InlineData(HotkeyAction.ReadSpeedVS, A300Readouts.VsSpeedKey, "VS 168 knots")]
    [InlineData(HotkeyAction.ReadSpeedVFE, A300Readouts.VmaxKey, "VMAX 180 knots")]
    public void A_speed_key_says_its_speed(HotkeyAction action, string key, string said)
    {
        _fresh[key] = said.StartsWith("VS") ? 167.8 : 179.8;
        Assert.Equal(new[] { said }, Press(action));
    }

    [Theory]
    [InlineData(HotkeyAction.ReadSpeedGD, A300Readouts.GreenDotKey, 0, "Green dot 187 knots")]
    [InlineData(HotkeyAction.ReadSpeedGD, A300Readouts.GreenDotKey, 4, "Green dot not shown at this flap setting")]
    [InlineData(HotkeyAction.ReadSpeedS, A300Readouts.SSpeedKey, 1, "S speed 187 knots")]
    [InlineData(HotkeyAction.ReadSpeedF, A300Readouts.FSpeedKey, 3, "F speed 187 knots")]
    [InlineData(HotkeyAction.ReadSpeedF, A300Readouts.FSpeedKey, 0, "F speed not shown at this flap setting")]
    public void A_flap_speed_key_follows_the_flap_lever(HotkeyAction action, string key, double flapLever, string said)
    {
        _fresh[key] = 187.4;
        _fresh[A300Levers.FlapsKey] = flapLever;
        Assert.Equal(new[] { said }, Press(action));
    }

    [Theory]
    [InlineData(1, A300Readouts.SSpeedKey, "Green dot not shown. S speed 198 knots")]
    [InlineData(2, A300Readouts.FSpeedKey, "Green dot not shown. F speed 198 knots")]
    [InlineData(3, A300Readouts.FSpeedKey, "Green dot not shown. F speed 198 knots")]
    public void Green_dot_off_the_tape_says_the_speed_shown_instead(double flapLever, string shownKey, string said)
    {
        _fresh[A300Readouts.GreenDotKey] = 187.4;
        _fresh[A300Levers.FlapsKey] = flapLever;
        _fresh[shownKey] = 197.6;
        Assert.Equal(new[] { said }, Press(HotkeyAction.ReadSpeedGD));
    }

    [Fact]
    public void Green_dot_off_the_tape_with_the_other_speed_unanswered_says_so()
    {
        _fresh[A300Readouts.GreenDotKey] = 187.4;
        _fresh[A300Levers.FlapsKey] = 1;
        Assert.Equal(new[] { "Green dot not shown. S speed unavailable" }, Press(HotkeyAction.ReadSpeedGD));
    }

    [Fact]
    public void Green_dot_with_no_flap_lever_says_unavailable()
    {
        _fresh[A300Readouts.GreenDotKey] = 187.4;
        Assert.Equal(new[] { "Green dot unavailable" }, Press(HotkeyAction.ReadSpeedGD));
    }

    [Fact]
    public void A_speed_not_computed_says_not_available()
    {
        _fresh[A300Readouts.VlsKey] = 0;
        Assert.Equal(new[] { "VLS not available" }, Press(HotkeyAction.ReadSpeedVLS));
    }

    [Fact]
    public void A_speed_that_does_not_answer_says_unavailable()
    {
        Assert.Equal(new[] { "VLS unavailable" }, Press(HotkeyAction.ReadSpeedVLS));
    }

    [Fact]
    public void A_readout_whose_definition_went_away_says_nothing()
    {
        var read = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, _, _) => read.Task;
        Assert.True(_def.HandleHotkeyAction(HotkeyAction.ReadSpeedVLS, _sim, _speech, null!, null!));
        _def.Dispose();
        read.SetResult(null);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_green_dot_readout_whose_definition_went_away_says_nothing()
    {
        var read = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, _, _) => read.Task;
        Assert.True(_def.HandleHotkeyAction(HotkeyAction.ReadSpeedGD, _sim, _speech, null!, null!));
        _def.Dispose();
        read.SetResult(null);   // "Green dot unavailable" would otherwise be said over the next aircraft
        Assert.Empty(_speech.All);
    }

    // ---- The B key and the fuel keys: the fleet's words ----

    [Fact]
    public void The_b_key_says_standard_while_the_captain_side_is_std()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _fresh[A300Readouts.BaroCaptainKey] = 1013.25;
        Assert.Equal(new[] { "Altimeter standard" }, Press(HotkeyAction.ReadAltimeter));
    }

    [Fact]
    public void The_b_key_says_both_units_while_the_captain_side_is_qnh()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _fresh[A300Readouts.BaroCaptainKey] = 1017.9375;
        Assert.Equal(new[] { "Altimeter: 1018, 30.06" }, Press(HotkeyAction.ReadAltimeter));
    }

    [Fact]
    public void The_b_key_follows_the_captain_side_not_the_first_officers()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _fresh[A300Readouts.BaroCaptainKey] = 1017.9375;
        Assert.Equal(new[] { "Altimeter: 1018, 30.06" }, Press(HotkeyAction.ReadAltimeter));
    }

    [Fact]
    public void The_b_key_with_the_mode_unread_says_the_numbers()
    {
        _fresh[A300Readouts.BaroCaptainKey] = 1013.25;
        Assert.Equal(new[] { "Altimeter: 1013, 29.92" }, Press(HotkeyAction.ReadAltimeter));
    }

    [Fact]
    public void The_b_key_without_an_answer_says_unavailable()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        Assert.Equal(new[] { "Altimeter unavailable" }, Press(HotkeyAction.ReadAltimeter));
    }

    [Fact]
    public void The_fuel_keys_say_the_total_in_pounds_and_in_kilograms()
    {
        _fresh[A300Readouts.FuelTotalKey] = 12345.4;
        Assert.Equal(new[] { "Fuel 12,345 pounds" }, Press(HotkeyAction.ReadFuelQuantity));
        _speech.All.Clear();
        Assert.Equal(new[] { "Fuel 5,600 kilograms" }, Press(HotkeyAction.ReadFuelInfo));
        _speech.All.Clear();
        _fresh.Remove(A300Readouts.FuelTotalKey);
        Assert.Equal(new[] { "Fuel unavailable" }, Press(HotkeyAction.ReadFuelQuantity));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_altimeter_and_fuel_keys_speak_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            _cache[A300Baro.Captain.ModeKey] = 0;
            _fresh[A300Readouts.BaroCaptainKey] = 1017.9375;
            _fresh[A300Readouts.FuelTotalKey] = 12345.4;
            Assert.Equal(new[] { "Altimeter: 1018, 30.06" }, Press(HotkeyAction.ReadAltimeter));
            _speech.All.Clear();
            Assert.Equal(new[] { "Fuel 12,345 pounds" }, Press(HotkeyAction.ReadFuelQuantity));
            _speech.All.Clear();
            Assert.Equal(new[] { "Fuel 5,600 kilograms" }, Press(HotkeyAction.ReadFuelInfo));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
