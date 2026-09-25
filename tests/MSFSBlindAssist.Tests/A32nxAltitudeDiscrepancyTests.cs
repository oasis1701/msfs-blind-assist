// A32NX FWC discrete word 124, bits 24 and 25 (PseudoFWC.ts: altiStdDiscrepancy /
// altiBaroDiscrepancy) are ONE condition in two baro modes: one side's air-data altitude differs
// from the other side's displayed altitude by more than 250 ft for 5 s, with both sides in STD (24)
// or both in QNH/QFE (25). They drive the PFD's CHECK ALT flag and ECAM "NAV ALT DISCREPANCY".
//
// Two defects kept MSFSBA's call-out from ever being right:
//   1. It was worded as a baro-SETTING mismatch ("Baro standard mode discrepancy" / "Baro reference
//      discrepancy"), which neither bit can signal — both need the sides in the same mode.
//   2. It never fired at all. PseudoFWC builds the word with Arinc429RegisterSubject.createEmpty()
//      and never sets its SSM, so the L:var always carries Failure Warning, and an SSM-gated read
//      (BitValueOr) always returns its fallback. The PFD reads it with bitValue, which ignores the
//      SSM; so must MSFSBA.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A32nxAltitudeDiscrepancyTests
{
    // The word as PseudoFWC writes it: SSM 0 (Failure Warning, never changed), the bitfield as the
    // float's value (Arinc429WordTests).
    private static double FwcWord(uint bitfield) =>
        BitConverter.SingleToUInt32Bits((float)bitfield);

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    public void Either_bit_is_spoken_as_an_altitude_discrepancy(int bit)
    {
        var def = new FlyByWireA320Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("A32NX_FWC_1_DISCRETE_WORD_124", FwcWord(0), speech);
        def.ProcessSimVarUpdate("A32NX_FWC_1_DISCRETE_WORD_124", FwcWord(1u << (bit - 1)), speech);
        def.ProcessSimVarUpdate("A32NX_FWC_1_DISCRETE_WORD_124", FwcWord(1u << (bit - 1)), speech);

        Assert.Equal(new[] { "Altitude discrepancy between sides" }, speech.All);
    }

    [Fact]
    public void A_discrepancy_that_clears_and_returns_is_spoken_again()
    {
        var def = new FlyByWireA320Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("A32NX_FWC_1_DISCRETE_WORD_124", FwcWord(1u << 24), speech);
        def.ProcessSimVarUpdate("A32NX_FWC_1_DISCRETE_WORD_124", FwcWord(0), speech);
        def.ProcessSimVarUpdate("A32NX_FWC_1_DISCRETE_WORD_124", FwcWord(1u << 23), speech);

        Assert.Equal(new[] { "Altitude discrepancy between sides", "Altitude discrepancy between sides" },
            speech.All);
    }

    [Fact]
    public void Unrelated_bits_of_the_word_say_nothing()
    {
        // Bits 18 and 21: the raw-bit decoder read this float's exponent as bit 24.
        var def = new FlyByWireA320Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("A32NX_FWC_1_DISCRETE_WORD_124", FwcWord((1u << 17) | (1u << 20)), speech);

        Assert.Empty(speech.All);
    }
}
