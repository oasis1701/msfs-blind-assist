// Characterization tests for IFlySdkSnapshot's transponder code window reads
// (SimConnect/IFly/IFlySdkSnapshot.cs). The squawk-entry rewrite (2026-07) keys
// digits by replaying the cockpit keypad clickspots, and both the entry
// normalization and the SYN_XPDR_CODE announce gate key on the window's FILLED
// DIGIT COUNT: entry fills left to right, cells read 10 (blank) until keyed, and
// only a complete 4-digit window carries a real code (a partial "12__" parses as
// 12 and would announce a bogus "Squawk 0012" without the gate). These pin the
// live-verified digit-cell semantics: 0-9 = digit, 10 = blank.

namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.SimConnect.IFly;

public class IFly737TransponderWindowTests
{
    private static IFlySdkSnapshot Snap(byte d1000, byte d100, byte d10, byte d1)
    {
        var b = new byte[IFlySdkOffsets.StructSize];
        b[IFlySdkOffsets.Transponder_Windows_Digital_1000_Status] = d1000;
        b[IFlySdkOffsets.Transponder_Windows_Digital_100_Status] = d100;
        b[IFlySdkOffsets.Transponder_Windows_Digital_10_Status] = d10;
        b[IFlySdkOffsets.Transponder_Windows_Digital_1_Status] = d1;
        return new IFlySdkSnapshot(b);
    }

    [Fact]
    public void DigitCount_AllBlank_IsZero()
    {
        // 10 = blank (unpowered panel or nothing keyed yet).
        Assert.Equal(0, Snap(10, 10, 10, 10).TransponderCodeDigitCount());
        Assert.Equal("", Snap(10, 10, 10, 10).TransponderCodeText());
    }

    [Fact]
    public void DigitCount_PartialEntry_CountsLeftToRightFill()
    {
        // Entry in progress: "23__" — two digits keyed, two cells still blank.
        Assert.Equal(2, Snap(2, 3, 10, 10).TransponderCodeDigitCount());
        Assert.Equal("23", Snap(2, 3, 10, 10).TransponderCodeText());
    }

    [Fact]
    public void DigitCount_CompleteCode_IsFour()
    {
        Assert.Equal(4, Snap(4, 0, 6, 6).TransponderCodeDigitCount());
        Assert.Equal("4066", Snap(4, 0, 6, 6).TransponderCodeText());
    }

    [Fact]
    public void DigitCount_ZeroDigitsAreFilledNotBlank()
    {
        // Code 0000 is a legitimate complete window — 0 cells must count as filled.
        Assert.Equal(4, Snap(0, 0, 0, 0).TransponderCodeDigitCount());
        Assert.Equal("0000", Snap(0, 0, 0, 0).TransponderCodeText());
    }
}
