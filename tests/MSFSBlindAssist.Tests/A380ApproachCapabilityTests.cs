// FBW #10855 ("add FG part to PRIM", a380x 1bbd304) deleted L:A32NX_FCDC_{1,2}_FG_DISCRETE_WORD_4,
// the word MSFSBA read the A380's approach capability from, and moved LAND 2 / LAND 3 SINGLE /
// LAND 3 DUAL to FCDC FG discrete word 1, bits 24/25/26 (Fcdc.cpp; the PFD's computeD1D2Message
// reads them there). Nothing writes word 4 any more, so the readout said "none" on every approach
// and "Approach capability LAND 3 dual" was never spoken. The old name survived a text search of the
// FBW tree only because FBW's own OIT maintenance page still reads it — a stale reader, not a writer.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380ApproachCapabilityTests
{
    // An FCDC discrete word as FBW packs it: the bitfield converted to a float NUMERICALLY, that
    // float's IEEE-754 bits in the low 32, the SSM above them (Arinc429WordTests).
    private static double FcdcWord(uint ssm, uint bitfield) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits((float)bitfield));
    private const uint NormalOperation = 0b11, NoComputedData = 0b01;

    [Fact]
    public void The_capability_is_read_from_fcdc_fg_word_1()
    {
        var def = new FlyByWireA380Definition().GetVariables()["PFD_AUTOLAND"];
        Assert.Equal("A32NX_FCDC_1_FG_DISCRETE_WORD_1", def.Name);
        Assert.Equal(A380ApproachCapability.Word, def.Name);
    }

    [Theory]
    [InlineData(24, "LAND 2")]
    [InlineData(25, "LAND 3 single")]
    [InlineData(26, "LAND 3 dual")]
    public void Each_capability_is_its_own_bit(int bit, string expected)
    {
        Assert.Equal(expected, A380ApproachCapability.Describe(FcdcWord(NormalOperation, 1u << (bit - 1))));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u << 22)]   // bit 23: LAND 2 in the deleted word 4, nothing in word 1
    public void A_word_reporting_no_capability_describes_nothing(uint bits)
    {
        Assert.Null(A380ApproachCapability.Describe(FcdcWord(NormalOperation, bits)));
    }

    [Fact]
    public void A_word_without_data_describes_nothing()
    {
        Assert.Null(A380ApproachCapability.Describe(FcdcWord(NoComputedData, 1u << 25)));
    }

    [Fact]
    public void The_status_readout_names_the_capability()
    {
        var def = new FlyByWireA380Definition();

        Assert.True(def.TryGetDisplayOverride("PFD_AUTOLAND", FcdcWord(NormalOperation, 1u << 25), out var text));
        Assert.Equal("LAND 3 dual", text);
        Assert.True(def.TryGetDisplayOverride("PFD_AUTOLAND", FcdcWord(NormalOperation, 0), out var none));
        Assert.Equal("none", none);
    }

    [Fact]
    public void A_capability_change_in_flight_is_announced()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("A32NX_FMGC_FLIGHT_PHASE", 5, speech);   // Approach
        speech.All.Clear();

        def.ProcessSimVarUpdate("PFD_AUTOLAND", FcdcWord(NormalOperation, 0), speech);        // baseline
        def.ProcessSimVarUpdate("PFD_AUTOLAND", FcdcWord(NormalOperation, 1u << 24), speech); // LAND 3 single
        def.ProcessSimVarUpdate("PFD_AUTOLAND", FcdcWord(NormalOperation, 1u << 25), speech); // LAND 3 dual

        Assert.Equal(new[] { "Approach capability LAND 3 single", "Approach capability LAND 3 dual" }, speech.All);
    }
}
