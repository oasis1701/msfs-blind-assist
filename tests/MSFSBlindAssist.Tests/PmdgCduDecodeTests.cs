// Characterization tests for MSFSBlindAssist.SimConnect.PMDGNG3DataManager:
//   - DecodeCellSymbol(byte) -> char  (PMDGNG3DataManager.cs:990, private -> internal for this suite)
//   - ToDouble(object?) -> double     (PMDGNG3DataManager.cs:496, private -> internal for this suite)
//
// Purpose: this is the PRE-REFACTOR NET for a planned later port of the CDU arrow decode
// (0xA3/0xA4) to the PMDG 777 manager, whose own two inline copies currently drop arrows as
// plain spaces. The arrow rows below are therefore the most important rows in this file.
//
// This is characterization, not spec verification: expected values were captured by reading
// the implementation and confirmed by running the tests. If a literal ever disagrees with
// actual output, the test must be corrected to match real output, not the other way around.

using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class PmdgCduDecodeTests
{
    // --- DecodeCellSymbol ---------------------------------------------------------
    //
    // Implementation (PMDGNG3DataManager.cs:990) is a 6-arm switch, tried in order:
    //   0xA1                => '<'
    //   0xA2                => '>'
    //   0xA3                => '↑'  (up arrow)   <-- pre-port net for the 777 arrow decode
    //   0xA4                => '↓'  (down arrow) <-- pre-port net for the 777 arrow decode
    //   >= 0x20 and <= 0x7E => (char)sym          (printable ASCII passthrough)
    //   _                   => ' '                (unknown byte fallback)

    [Theory]
    // -- special symbols: line-select brackets --
    [InlineData((byte)0xA1, '<')]
    [InlineData((byte)0xA2, '>')]
    // -- special symbols: arrows (the pre-port net for PR-4's SC-9 777 port) --
    [InlineData((byte)0xA3, '↑')]
    [InlineData((byte)0xA4, '↓')]
    // -- printable ASCII passthrough: range boundaries + representative samples --
    [InlineData((byte)0x20, ' ')]  // low boundary of printable range (space)
    [InlineData((byte)0x30, '0')]  // digit
    [InlineData((byte)0x41, 'A')]  // uppercase letter
    [InlineData((byte)0x7E, '~')]  // high boundary of printable range (tilde)
    // -- unknown-byte fallback: below range, above range, and other unmapped bytes --
    [InlineData((byte)0x00, ' ')]  // NUL, below printable range
    [InlineData((byte)0x1F, ' ')]  // just below printable range
    [InlineData((byte)0x7F, ' ')]  // just above printable range (DEL)
    [InlineData((byte)0xA0, ' ')]  // just below the 0xA1 special-symbol block
    [InlineData((byte)0xA5, ' ')]  // just above the 0xA4 special-symbol block
    [InlineData((byte)0xFF, ' ')]  // top of byte range
    public void DecodeCellSymbol_maps_byte_to_expected_char(byte sym, char expected)
    {
        Assert.Equal(expected, PMDGNG3DataManager.DecodeCellSymbol(sym));
    }

    // Cross-decoder agreement: the PMDG 777 manager's decoder (ported from two duplicated
    // inline copies as part of PR-4's SC-9) must match the NG3's semantics for every byte,
    // including the 0xA3/0xA4 arrows the 777 previously rendered as plain spaces.
    [Fact]
    public void Pmdg777_decoder_agrees_with_NG3_for_all_bytes()
    {
        for (int b = 0; b <= 0xFF; b++)
            Assert.Equal(PMDGNG3DataManager.DecodeCellSymbol((byte)b),
                         PMDG777DataManager.DecodeCellSymbol((byte)b));
    }

    // --- ToDouble -------------------------------------------------------------------
    //
    // Implementation (PMDGNG3DataManager.cs:496) is an 11-type switch (+ fallback):
    //   bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double
    //   plus a catch-all "_ => 0.0" for any unhandled type (e.g. string, null, other object).

    [Fact]
    public void ToDouble_converts_bool_true_to_one()
    {
        Assert.Equal(1.0, PMDGNG3DataManager.ToDouble(true));
    }

    [Fact]
    public void ToDouble_converts_bool_false_to_zero()
    {
        Assert.Equal(0.0, PMDGNG3DataManager.ToDouble(false));
    }

    [Fact]
    public void ToDouble_converts_byte()
    {
        Assert.Equal(200.0, PMDGNG3DataManager.ToDouble((byte)200));
    }

    [Fact]
    public void ToDouble_converts_sbyte()
    {
        Assert.Equal(-100.0, PMDGNG3DataManager.ToDouble((sbyte)-100));
    }

    [Fact]
    public void ToDouble_converts_short()
    {
        Assert.Equal(-30000.0, PMDGNG3DataManager.ToDouble((short)-30000));
    }

    [Fact]
    public void ToDouble_converts_ushort()
    {
        Assert.Equal(60000.0, PMDGNG3DataManager.ToDouble((ushort)60000));
    }

    [Fact]
    public void ToDouble_converts_int()
    {
        Assert.Equal(-123456.0, PMDGNG3DataManager.ToDouble(-123456));
    }

    [Fact]
    public void ToDouble_converts_uint()
    {
        Assert.Equal(4000000000.0, PMDGNG3DataManager.ToDouble(4000000000u));
    }

    [Fact]
    public void ToDouble_converts_long()
    {
        Assert.Equal(-9000000000000.0, PMDGNG3DataManager.ToDouble(-9000000000000L));
    }

    [Fact]
    public void ToDouble_converts_ulong()
    {
        Assert.Equal(18000000000000000000.0, PMDGNG3DataManager.ToDouble(18000000000000000000UL));
    }

    [Fact]
    public void ToDouble_converts_float()
    {
        Assert.Equal(42.5, PMDGNG3DataManager.ToDouble(42.5f), 3);
    }

    [Fact]
    public void ToDouble_passes_through_double()
    {
        Assert.Equal(123.456, PMDGNG3DataManager.ToDouble(123.456));
    }

    [Fact]
    public void ToDouble_unhandled_type_falls_back_to_zero()
    {
        Assert.Equal(0.0, PMDGNG3DataManager.ToDouble("not a number"));
    }

    [Fact]
    public void ToDouble_null_falls_back_to_zero()
    {
        Assert.Equal(0.0, PMDGNG3DataManager.ToDouble(null));
    }
}
