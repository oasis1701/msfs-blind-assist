// The A380's hPa/inHg selector (A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG) re-announces the altimeter in the
// new unit when it changes. The Baro window's unit combo (Ctrl+B) sets BOTH sides through
// ApplyUIVariable, which MainForm's _uiSetEcho never sees — so since FBW #10855 made both writes take
// effect, the pilot's own combo pick came back as "Captain altimeter …" AND "First officer
// altimeter …", over a combo the screen reader had just read (found by review, 2026-09-25).
// HandleUIVariableSet records the unit it writes (RememberCommandedValue); the change it causes is
// the echo and is not spoken. A change made anywhere else still is.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A380BaroUnitEchoTests
{
    [Theory]
    [InlineData("A32NX_FCU_EFIS_L_BARO_IS_INHG")]
    [InlineData("A32NX_FCU_EFIS_R_BARO_IS_INHG")]
    public void A_unit_msfsba_set_is_not_spoken_back(string key)
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(key, 0, speech);   // baseline, hPa

        def.RememberCommandedValue(key, 1);         // what HandleUIVariableSet records for a set
        def.ProcessSimVarUpdate(key, 1, speech);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_unit_change_made_elsewhere_is_still_spoken()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0, speech);

        def.ProcessSimVarUpdate("A32NX_FCU_EFIS_L_BARO_IS_INHG", 1, speech);

        Assert.Equal(new[] { "Captain baro unit inches" }, speech.All);
    }

    [Fact]
    public void Only_the_commanded_unit_is_the_echo()
    {
        // MSFSBA set inHg; the selector going back to hPa is somebody else's change.
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("A32NX_FCU_EFIS_L_BARO_IS_INHG", 1, speech);   // baseline, inHg

        def.RememberCommandedValue("A32NX_FCU_EFIS_L_BARO_IS_INHG", 1);
        def.ProcessSimVarUpdate("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0, speech);

        Assert.Equal(new[] { "Captain baro unit hectopascals" }, speech.All);
    }
}
