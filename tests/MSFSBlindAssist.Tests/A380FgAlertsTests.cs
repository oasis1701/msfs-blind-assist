// PRIM FG discrete word 5 carries the A380's two FMA alerts MSFSBA speaks: bit 28
// ap_fd_mode_reversion ("FMA Reversion") and bit 29 vs_target_not_held ("Speed Protection" — what
// the PFD's own inSpeedProtection reads). Until 2026-09-25 Arinc429Word tested the RAW low 32 bits
// instead of the bitfield the float carries, so bit 29 could never read true and bit 28 read true
// for any word with a bit at 18 or above — bit 18 is manual_spd_control_active, so switching between
// selected and managed speed was announced as "FMA Reversion: active" / "off", and a real speed
// protection was never announced at all.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A380FgAlertsTests
{
    // A PRIM FG discrete word as FBW packs it (Arinc429WordTests).
    private static double FgWord5(uint bitfield) =>
        (double)((3UL << 32) | BitConverter.SingleToUInt32Bits((float)bitfield));

    private const uint AutoSpeed = 1u << 16;     // bit 17 auto_spd_control_active
    private const uint ManualSpeed = 1u << 17;   // bit 18 manual_spd_control_active
    private const uint Reversion = 1u << 27;     // bit 28
    private const uint SpeedProtection = 1u << 28; // bit 29

    [Fact]
    public void Switching_between_selected_and_managed_speed_says_nothing()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(ManualSpeed), speech);   // baseline
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(AutoSpeed), speech);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(ManualSpeed), speech);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_mode_reversion_is_announced_and_cleared()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(AutoSpeed), speech);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(ManualSpeed | Reversion), speech);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(ManualSpeed), speech);

        Assert.Equal(new[] { "FMA Reversion: active", "FMA Reversion: off" }, speech.All);
    }

    [Fact]
    public void Speed_protection_is_announced()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(AutoSpeed), speech);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord5(AutoSpeed | SpeedProtection), speech);

        Assert.Equal(new[] { "Speed Protection: active" }, speech.All);
    }
}
