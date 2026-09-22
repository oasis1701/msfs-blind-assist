// tests/MSFSBlindAssist.Tests/Cessna172SquawkTests.cs
// TRANSPONDER CODE:1 is read with Units=Bco16 (0x1200 = squawk 1200) and XPNDR_SET takes the same
// BCD. The panel entry box hands the definition a parsed DOUBLE, so "0422" arrives as 422 and must
// be padded back to four digits before encoding.
using MSFSBlindAssist.Aircraft.C172;

namespace MSFSBlindAssist.Tests;

public class Cessna172SquawkTests
{
    [Theory]
    [InlineData(1200, 0x1200u)]
    [InlineData(7000, 0x7000u)]
    [InlineData(422, 0x0422u)]
    public void A_typed_code_encodes_to_bcd(double typed, uint expected)
    {
        Assert.True(Cessna172Squawk.TryToBcd(typed, out uint bcd, out string error));
        Assert.Equal(expected, bcd);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(1280)]      // digit 8
    [InlineData(1209)]      // digit 9
    [InlineData(12345)]     // five digits
    [InlineData(-1)]
    [InlineData(0)]         // a BLANK or garbled entry box: MainForm parses it to 0
    [InlineData(double.NaN)]
    public void An_invalid_code_is_refused_with_a_reason(double typed)
    {
        Assert.False(Cessna172Squawk.TryToBcd(typed, out _, out string error));
        Assert.Contains("0 to 7", error);
    }

    [Theory]
    [InlineData(0x1200, "1200")]
    [InlineData(0x0422, "0422")]
    [InlineData(0x7777, "7777")]
    public void A_bco16_reading_renders_as_four_digits(double raw, string expected)
    {
        Assert.Equal(expected, Cessna172Squawk.FromBcd(raw));
    }
}
