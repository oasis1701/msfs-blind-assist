// Pins the Fenix v3 fix: N_FCU_VS carries the LIVE vertical speed while the FCU V/S window is dashed
// (managed climb or descent), so the generic announcement would speak it every second or two. It is
// consumed silently while B_FCU_VERTICALSPEED_DASHED reads 1; the flag itself is only peeked, so
// "FCU VERTICAL SPEED DISPLAY: Dashed" is still heard.

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FenixV3FixFcuVsTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public FenixV3FixFcuVsTests() => CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private const string Vs = "N_FCU_VS";
    private const string Dashed = "B_FCU_VERTICALSPEED_DASHED";

    [Fact]
    public void Vs_is_silent_while_dashed()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate(Dashed, 1, speech);

        Assert.True(def.ProcessSimVarUpdate(Vs, 3400, speech));
        Assert.True(def.ProcessSimVarUpdate(Vs, 1700, speech));
        Assert.Empty(speech.All);
    }

    // Owner request (2026-10-22): a climb is spoken with its sign, as a descent already was, and as
    // the FBW A320's V/S call-out does. The class runs under sv-SE, so a culture-formatted value fails.
    [Theory]
    [InlineData(300.0, "FCU VERTICAL SPEED: +300")]
    [InlineData(1500.0, "FCU VERTICAL SPEED: +1500")]
    [InlineData(-300.0, "FCU VERTICAL SPEED: -300")]
    [InlineData(0.0, "FCU VERTICAL SPEED: 0")]
    public void Vs_is_spoken_with_its_sign(double value, string expected)
    {
        var def = new FenixA320Definition().GetVariables()[Vs];
        Assert.Equal(expected, new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero).FormatVariableValue(Vs, def, value));
    }

    [Fact]
    public void Vs_speaks_when_shown()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate(Dashed, 1, speech);
        def.ProcessSimVarUpdate(Dashed, 0, speech);

        Assert.False(def.ProcessSimVarUpdate(Vs, 1500, speech));
    }

    [Fact]
    public void Vs_speaks_before_the_flag_is_read()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(def.ProcessSimVarUpdate(Vs, 1500, speech));
    }

    [Fact]
    public void Dashed_flag_still_announces()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(def.ProcessSimVarUpdate(Dashed, 1, speech));
    }

    // The flag mirrors a state, not a baseline. ResetAnnouncementBaselines runs after the reconnect's
    // first batch has re-delivered every var, and a flight load clears no cache, so an unchanged
    // announced flag is never delivered again: a wipe on either would let the V/S speak for a whole
    // managed climb.
    [Fact]
    public void A_reconnect_or_flight_load_keeps_the_dashed_flag()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate(Dashed, 1, speech);
        def.ResetAnnouncementBaselines();
        Assert.True(def.ProcessSimVarUpdate(Vs, 3400, speech));

        def.OnSimContextReset();
        Assert.True(def.ProcessSimVarUpdate(Vs, 3400, speech));

        def.ProcessSimVarUpdate(Dashed, 0, speech);
        Assert.False(def.ProcessSimVarUpdate(Vs, 1500, speech));
    }

    // After a cache clear (a dropped connection) every var re-fires, so the flag is re-read: until it
    // is, the V/S is not silenced on a stale value.
    [Fact]
    public void A_cache_clear_forgets_the_flag_until_it_is_read_again()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate(Dashed, 1, speech);
        def.OnSimContextReset();
        def.OnVariableCacheCleared();
        Assert.False(def.ProcessSimVarUpdate(Vs, 1500, speech));

        def.ProcessSimVarUpdate(Dashed, 1, speech);
        Assert.True(def.ProcessSimVarUpdate(Vs, 3400, speech));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    [InlineData(2.0, false)]
    [InlineData(double.NaN, false)]
    public void Only_exactly_one_silences(double dashed, bool expected)
        => Assert.Equal(expected, FenixA320Definition.FcuVsIsSilenced(dashed));

    [Fact]
    public void An_unread_flag_does_not_silence()
        => Assert.False(FenixA320Definition.FcuVsIsSilenced(null));
}
