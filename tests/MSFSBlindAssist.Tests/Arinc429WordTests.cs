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
    /// Same packing, but for tests that care about the raw low-32 bit pattern
    /// directly (BitValueOr) rather than its meaning as a float.
    /// </summary>
    private static double WordBits(uint ssm, uint raw32) =>
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
        // raw32 = 0b101 -> bit 1 and bit 3 (1-based) set, bit 2 clear.
        var w = new Arinc429Word(WordBits(0b11, 0b101));

        Assert.True(w.BitValueOr(1, false));
    }

    [Fact]
    public void BitValueOr_reads_clear_bit_for_NormalOp_word()
    {
        var w = new Arinc429Word(WordBits(0b11, 0b101));

        Assert.False(w.BitValueOr(2, true));
    }

    [Fact]
    public void BitValueOr_returns_fallback_for_invalid_ssm()
    {
        var w = new Arinc429Word(WordBits(0b00, 0b101));

        Assert.True(w.BitValueOr(1, true));
        Assert.False(w.BitValueOr(1, false));
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
