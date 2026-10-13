using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The speed-tape keys, as on the A320s: each reads its speed fresh and says it, or says it is not
/// shown or not available. The readouts never touch the form or the hotkey manager.
/// </summary>
public class IniA300SpeedKeysTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _fresh = new();

    public IniA300SpeedKeysTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
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
}
