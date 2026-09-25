// The A380's runway-overrun (ROW/ROP) and RWY AHEAD call-outs. Two defects kept every one of them
// silent until 2026-09-25:
//   1. Arinc429Word read a discrete word's RAW IEEE bits instead of the bitfield the float carries;
//      the bits tested here (11-15) are float mantissa bits a word using only bits 11-15 never sets.
//   2. FBW #10699 (2026-07-04) re-laid ROW_ROP_WORD_1: 11 = BRAKE MAX BRAKING, 12 = SET MAX REVERSE,
//      13 = KEEP MAX REVERSE (14/15 unchanged), with "operative" moved to the SSM. MSFSBA still
//      carried the old map, so with only the decoder fixed it would have said "Maximum braking" for
//      SET MAX REVERSE and nothing at all for BRAKE MAX BRAKING.
// The layout is what the PFD's AttitudeIndicatorWarnings and the FWS aurals (FwsAutoCallouts) read.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A380RowRopCalloutTests
{
    // A discrete word as FBW packs it (Arinc429WordTests): Rust `(value as f32).to_bits()` for
    // ROW_ROP_WORD_1, TS Arinc429Register.writeToSimVar for OANS_WORD_1 — the same encoding.
    private static double Word(uint ssm, uint bitfield) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits((float)bitfield));
    private const uint FailureWarning = 0b00, NormalOperation = 0b11;

    [Theory]
    [InlineData(11, "Max braking")]
    [InlineData(12, "Set max reverse")]
    [InlineData(13, "Keep max reverse")]
    [InlineData(14, "If wet, runway too short")]
    [InlineData(15, "Runway too short")]
    public void Each_row_rop_request_is_spoken_on_its_rising_edge(int bit, string phrase)
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("A32NX_ROW_ROP_WORD_1", Word(NormalOperation, 0), speech);
        def.ProcessSimVarUpdate("A32NX_ROW_ROP_WORD_1", Word(NormalOperation, 1u << (bit - 1)), speech);
        def.ProcessSimVarUpdate("A32NX_ROW_ROP_WORD_1", Word(NormalOperation, 1u << (bit - 1)), speech);

        Assert.Equal(new[] { phrase }, speech.All);
    }

    [Fact]
    public void An_inoperative_row_rop_says_nothing()
    {
        // Since #10699 "inoperative" is the SSM, not a bit: a Failure Warning word is not a request.
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("A32NX_ROW_ROP_WORD_1", Word(FailureWarning, (1u << 10) | (1u << 14)), speech);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void Runway_ahead_is_spoken()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("A32NX_OANS_WORD_1", Word(NormalOperation, 0), speech);
        def.ProcessSimVarUpdate("A32NX_OANS_WORD_1", Word(NormalOperation, 1u << 10), speech);

        Assert.Equal(new[] { "Runway ahead" }, speech.All);
    }
}
