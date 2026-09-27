// Characterization tests for MSFSBlindAssist.SimConnect.Arinc429Word.
//
// These lock in CURRENT behavior of the decoder (see Arinc429Word.cs):
//   - constructed from a double (numeric truncation to UInt64, guarded against
//     NaN/negative/overflow -> 0)
//   - low 32 bits = IEEE-754 float payload (Value)
//   - bits 32-33 = SSM (Sign/Status Matrix): 0=FailureWarning, 1=NoComputedData,
//     2=FunctionalTest, 3=NormalOperation
//   - ValueOr/BitValueOr treat BOTH NormalOperation (0b11) and FunctionalTest
//     (0b10) as "data present"
//
// This is characterization, not spec verification: the expected values below
// were derived by reasoning about the real source and confirmed by running
// the tests; if a literal ever disagrees with actual output, the test must be
// corrected to match real output, not the other way around.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Arinc429WordTests
{
    /// <summary>
    /// Packs an SSM (bits 32-33) and a float payload (low 32 bits, as its raw
    /// IEEE-754 bit pattern) into the double the Arinc429Word constructor
    /// expects, mirroring how FBW/SimConnect delivers the raw L:var value.
    /// </summary>
    private static double Word(uint ssm, float payload) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits(payload));

    /// <summary>
    /// A DISCRETE word packed the way FBW packs one: the bitfield converted NUMERICALLY to a
    /// float, and that float's IEEE-754 bits in the low 32 (C++ Arinc429Utils::setBit +
    /// toSimVar, TS Arinc429Register.setBitValue + writeToSimVar, Rust `as f32` + to_bits).
    /// </summary>
    private static double DiscreteWord(uint ssm, uint bitfield) => Word(ssm, (float)bitfield);

    /// <summary>A raw low-32 pattern, for the captures that were recorded that way.</summary>
    private static double RawWord(uint ssm, uint raw32) =>
        (double)(((ulong)ssm << 32) | raw32);

    // --- SSM state mapping -------------------------------------------------

    [Theory]
    [InlineData(0b00u, false, false, false, true)]
    [InlineData(0b01u, false, false, true, false)]
    [InlineData(0b10u, false, true, false, false)]
    [InlineData(0b11u, true, false, false, false)]
    public void Ssm_maps_to_correct_Is_flag(uint ssm, bool expectNormalOp, bool expectFunctionalTest, bool expectNoComputedData, bool expectFailureWarning)
    {
        var w = new Arinc429Word(Word(ssm, 0f));

        Assert.Equal(expectNormalOp, w.IsNormalOperation);
        Assert.Equal(expectFunctionalTest, w.IsFunctionalTest);
        Assert.Equal(expectNoComputedData, w.IsNoComputedData);
        Assert.Equal(expectFailureWarning, w.IsFailureWarning);
    }

    // --- ValueOr -------------------------------------------------------------

    [Fact]
    public void ValueOr_returns_payload_for_NormalOperation()
    {
        var w = new Arinc429Word(Word(0b11, 42.5f));

        Assert.Equal(42.5f, w.ValueOr(-1f));
    }

    [Fact]
    public void ValueOr_returns_payload_for_FunctionalTest()
    {
        var w = new Arinc429Word(Word(0b10, 42.5f));

        Assert.Equal(42.5f, w.ValueOr(-1f));
    }

    [Fact]
    public void ValueOr_returns_fallback_for_FailureWarning()
    {
        var w = new Arinc429Word(Word(0b00, 42.5f));

        Assert.Equal(-1f, w.ValueOr(-1f));
    }

    [Fact]
    public void ValueOr_returns_fallback_for_NoComputedData()
    {
        var w = new Arinc429Word(Word(0b01, 42.5f));

        Assert.Equal(-1f, w.ValueOr(-1f));
    }

    // --- Payload round-trip ---------------------------------------------------

    [Theory]
    [InlineData(123.5f)]
    [InlineData(-50.25f)]
    [InlineData(0.0f)]
    public void Payload_roundtrips_through_Value_for_NormalOp_word(float payload)
    {
        var w = new Arinc429Word(Word(0b11, payload));

        Assert.Equal(payload, w.Value);
    }

    // --- Out-of-range constructor guard -----------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(1.8e19)]
    [InlineData(2e19)]
    public void Out_of_range_simVar_decodes_to_zeroed_FailureWarning_word(double simVar)
    {
        var w = new Arinc429Word(simVar);

        Assert.True(w.IsFailureWarning);
        Assert.Equal(0u, w.Ssm);
        Assert.Equal(0f, w.Value);
        Assert.Equal(-1f, w.ValueOr(-1f));
    }

    // --- BitValueOr ------------------------------------------------------------

    [Fact]
    public void BitValueOr_reads_set_bit_for_NormalOp_word()
    {
        // bitfield 0b101 -> bit 1 and bit 3 (1-based) set, bit 2 clear.
        var w = new Arinc429Word(DiscreteWord(0b11, 0b101));

        Assert.True(w.BitValueOr(1, false));
        Assert.True(w.BitValueOr(3, false));
    }

    [Fact]
    public void BitValueOr_reads_clear_bit_for_NormalOp_word()
    {
        var w = new Arinc429Word(DiscreteWord(0b11, 0b101));

        Assert.False(w.BitValueOr(2, true));
    }

    [Fact]
    public void BitValueOr_returns_fallback_for_invalid_ssm()
    {
        var w = new Arinc429Word(DiscreteWord(0b00, 0b101));

        Assert.True(w.BitValueOr(1, true));
        Assert.False(w.BitValueOr(1, false));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(18)]
    [InlineData(28)]
    [InlineData(29)]
    public void BitValueOr_reads_every_data_bit_alone_and_nothing_else(int bit)
    {
        // Pins the numeric reading bit by bit across FBW's data bits (11-29). The raw-bit
        // reading missed 11, 13, 18 and 29 alone, and read 28 right only because the float
        // for 2^27 happens to have that exponent bit set — it also "found" 28 for 18 and 29.
        var w = new Arinc429Word(DiscreteWord(0b11, 1u << (bit - 1)));

        for (int n = 1; n <= 32; n++)
            Assert.Equal(n == bit, w.BitValueOr(n, false));
    }

    [Fact]
    public void BitValueOr_reads_the_widest_span_FBW_uses_exactly()
    {
        // Bits 11 and 29 together: the widest span a discrete word carries, still inside a
        // float's 24-bit significand, so the conversion rounds nothing away.
        var w = new Arinc429Word(DiscreteWord(0b11, (1u << 10) | (1u << 28)));

        Assert.True(w.BitValueOr(11, false));
        Assert.True(w.BitValueOr(29, false));
        Assert.Equal((1u << 10) | (1u << 28), w.DiscreteBits);
    }

    [Fact]
    public void The_word_5_capture_at_rest_is_manual_speed_not_a_reversion()
    {
        // PRIM FG word 5, measured on a cold parked A380: raw 0x48000000 with SSM Normal
        // Operation. That is the float 131072 = bit 18, manual_spd_control_active — NOT bits 28
        // and 31, which is how the raw-bit reading had it ("reversion asserted at rest").
        var w = new Arinc429Word(RawWord(0b11, 0x48000000));

        Assert.Equal(131072f, w.Value);
        Assert.True(w.BitValueOr(18, false));
        Assert.False(w.BitValueOr(28, true));
        Assert.False(w.BitValueOr(31, true));
    }

    [Fact]
    public void The_word_3_capture_in_cruise_is_alt_hold_and_cruise()
    {
        // PRIM FG word 3, measured level at FL360 after a step climb: raw 0x4D804000. That is
        // 2^28 + 2^19 = bits 29 (cruise, the PFD's altIsCrzAlt) and 20 (ALT hold) — level cruise
        // exactly. The raw-bit reading had it as the constraint qualifier (bit 28) and not bit 29.
        var w = new Arinc429Word(RawWord(0b11, 0x4D804000));

        Assert.True(w.BitValueOr(29, false));
        Assert.True(w.BitValueOr(20, false));
        Assert.False(w.BitValueOr(28, true));
    }

    [Fact]
    public void BitValue_reads_a_bit_whatever_the_ssm_says()
    {
        // FBW's own bitValue, for the one kind of word whose writer never sets its SSM (the A32NX
        // FWC word 124 stays Failure Warning for good; the PFD reads CHECK ALT with bitValue).
        var w = new Arinc429Word(DiscreteWord(0b00, 1u << 23));

        Assert.True(w.BitValue(24));
        Assert.False(w.BitValue(25));
        Assert.False(w.BitValueOr(24, false));   // the gated read still refuses it
    }

    [Theory]
    [InlineData(-4096f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_value_no_discrete_word_carries_reads_no_bits(float payload)
    {
        var w = new Arinc429Word(Word(0b11, payload));

        Assert.Equal(0u, w.DiscreteBits);
        Assert.False(w.BitValueOr(13, false));
    }

    // --- ToReadout ---------------------------------------------------------

    [Fact]
    public void ToReadout_formats_value_with_unit_for_NormalOp_word()
    {
        var w = new Arinc429Word(Word(0b11, 42f));

        Assert.Equal("42 ft", w.ToReadout("0", "ft"));
    }

    [Fact]
    public void ToReadout_returns_invalid_marker_for_non_NormalOp_word()
    {
        var w = new Arinc429Word(Word(0b01, 42f));

        Assert.Equal("invalid", w.ToReadout("0", "ft"));
    }
}
