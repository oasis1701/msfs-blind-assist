// Characterization tests for pure-logic helpers in MSFSBlindAssist.SimConnect.SimConnectManager:
//   - ExtractIcaoFromAtcModel(string?)  — Dispatch.cs, public static
//   - ConvertMHzToBcd16Hz(double)       — DataRequests.cs, public static
//   - UnpackWaypointName(double,double) — Dispatch.cs, promoted private -> internal for this suite
//
// This is characterization, not spec verification: expected values were captured by running
// the tests against the current implementation. If a literal ever disagrees with actual output,
// the test must be corrected to match real output, not the other way around.

using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class SimConnectPureLogicTests
{
    // --- ExtractIcaoFromAtcModel --------------------------------------------------
    //
    // Implementation (Dispatch.cs:1129) has three tiers, tried in order:
    //   1. Regex "AC_MODEL[ _]([A-Za-z0-9]{2,6})" (case-insensitive) -> group 1 uppercased.
    //   2. Whole trimmed string already matches ^[A-Za-z][A-Za-z0-9]{1,5}$ -> uppercased.
    //   3. Otherwise "" (explicitly avoids the old greedy right-to-left grab that could
    //      return wrong tokens like "NG3"/"CEO" out of an unrelated multi-word string).

    [Theory]
    // -- null / blank guard --
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    // -- tier 1: AC_MODEL token, underscore and space separators, case-insensitive --
    [InlineData("TT:ATCCOM.AC_MODEL_B748.0.text", "B748")]
    [InlineData("ATCCOM.AC_MODEL B77W.0.text", "B77W")]
    [InlineData("ac_model_a20n.0.text", "A20N")]
    // A 3-char token IS honored when explicitly tagged by AC_MODEL — this is the
    // legitimate case the tier-3 comment distinguishes from the old buggy greedy grab.
    [InlineData("TT:ATCCOM.AC_MODEL_NG3.0.text", "NG3")]
    // -- tier 2: bare ICAO (whole trimmed string is letter + 1-5 alphanumerics) --
    [InlineData("B738", "B738")]
    [InlineData("a320", "A320")]
    [InlineData("AB", "AB")]
    // single letter is below the tier-2 minimum length (letter + >=1 more char) -> tier 3
    [InlineData("A", "")]
    // -- tier 3: unresolved, falls through to empty --
    [InlineData("Airbus A320 Neo FlyByWire", "")]
    [InlineData("1234567", "")] // digit-leading (fails letter-first) and too long
    public void ExtractIcao_pins_current_tiers(string? atcModel, string expected)
        => Assert.Equal(expected, SimConnectManager.ExtractIcaoFromAtcModel(atcModel));

    // --- ConvertMHzToBcd16Hz --------------------------------------------------------
    //
    // Implementation (DataRequests.cs:746): frequencyHz = (uint)(frequencyMHz * 1_000_000),
    // then each decimal digit of frequencyHz is packed into a 4-bit nibble via
    // `bcd += digit * multiplier; multiplier *= 16;` — i.e. the result reads as the SAME
    // digit string as frequencyHz, just interpreted as hex instead of decimal.
    //
    // DISCREPANCY FOUND (not the anticipated one): `multiplier` is a uint. For any
    // frequencyHz >= 100,000,000 (i.e. any MHz value >= 100.000 — the ENTIRE 108-137 MHz
    // aviation VOR/COM band), the 9th decimal digit's multiplier is 16^8 == 2^32 exactly,
    // which wraps a uint to 0. That digit's contribution vanishes, so the encoded BCD
    // silently DROPS THE LEADING (hundreds-of-MHz) DIGIT — e.g. 122.800 MHz encodes
    // identically to what 22.800 MHz would. This is a real bug, distinct from the
    // brief's anticipated double-truncation hazard (which does NOT manifest for these
    // three sample frequencies — see the exact bcd literals below, which reflect a
    // precise Hz value with only the leading digit missing, not an off-by-one digit).
    // ConvertMHzToBcd16Hz currently has NO callers in the codebase (dead code), which
    // limits blast radius, but the bug is pinned here as found, not fixed.
    //
    // Decodes the nibbles back (each nibble as a base-10 digit at its power-of-10 place)
    // to make the dropped-digit behavior explicit rather than opaque hex.

    private static long DecodeBcdNibblesToDecimal(uint bcd)
    {
        long result = 0;
        long placeValue = 1;
        uint v = bcd;
        while (v > 0)
        {
            uint nibble = v & 0xF;
            Assert.True(nibble <= 9, $"BCD nibble {nibble:X} is not a valid decimal digit (bcd=0x{bcd:X})");
            result += nibble * placeValue;
            placeValue *= 10;
            v >>= 4;
        }
        return result;
    }

    [Theory]
    // -- >=100 MHz: 9-digit Hz value -> leading digit lost to the uint32 multiplier
    //    overflow (16^8 == 2^32). Decoded value is the INTENDED Hz minus its leading digit.
    [InlineData(122.800, 0x22800000u, 22_800_000L)]
    [InlineData(118.000, 0x18000000u, 18_000_000L)]
    [InlineData(136.975, 0x36975000u, 36_975_000L)]
    // -- <100 MHz control case: 8-digit Hz value, no overflow -> round-trips exactly.
    //    Confirms the bug is specifically the 9th-digit overflow, not a general defect.
    [InlineData(99.999, 0x99999000u, 99_999_000L)]
    public void Bcd16_pins_current_encoding_including_the_100MHz_digit_drop(
        double mhz, uint expectedBcd, long expectedDecodedHz)
    {
        uint bcd = SimConnectManager.ConvertMHzToBcd16Hz(mhz);
        Assert.Equal(expectedBcd, bcd);
        Assert.Equal(expectedDecodedHz, DecodeBcdNibblesToDecimal(bcd));
    }

    // --- UnpackWaypointName ----------------------------------------------------------
    //
    // Implementation (Dispatch.cs:804): two doubles (ident0, ident1) each pack up to 8
    // characters, 6 bits each (code = (int)(value / 2^(charPos*6)) & 0x3F), decoded char =
    // (char)(code + 31). code == 0 is skipped (not appended); the final string is Trim()'d,
    // so an interior/trailing "code 1" (-> ASCII 32, space) survives unless trailing.
    //
    // Packed literals below are hand-computed from that scheme (code = charAscii - 31,
    // ident = sum(code_i * 64^charPos)) and confirmed by running the test.

    [Fact]
    public void UnpackWaypointName_decodes_four_chars_packed_in_ident0()
    {
        // "KORD": K=75->44, O=79->48, R=82->51, D=68->37
        // ident0 = 44*64^0 + 48*64^1 + 51*64^2 + 37*64^3 = 9,911,340
        var sut = new SimConnectManager(IntPtr.Zero);
        Assert.Equal("KORD", sut.UnpackWaypointName(9_911_340.0, 0.0));
    }

    [Fact]
    public void UnpackWaypointName_decodes_five_chars_packed_in_ident0()
    {
        // "ABCDE": A=65->34, B=66->35, C=67->36, D=68->37, E=69->38
        // ident0 = 34 + 35*64 + 36*64^2 + 37*64^3 + 38*64^4 = 647,383,266
        var sut = new SimConnectManager(IntPtr.Zero);
        Assert.Equal("ABCDE", sut.UnpackWaypointName(647_383_266.0, 0.0));
    }

    [Fact]
    public void UnpackWaypointName_reads_ident1_when_ident0_is_empty()
    {
        // "Z" alone in ident1 charPos 0: Z=90->59, ident1 = 59
        var sut = new SimConnectManager(IntPtr.Zero);
        Assert.Equal("Z", sut.UnpackWaypointName(0.0, 59.0));
    }

    [Fact]
    public void UnpackWaypointName_concatenates_across_ident0_and_ident1()
    {
        // ident0 = "AB" (A=34@charPos0, B=35@charPos1) -> 34 + 35*64 = 2274
        // ident1 = "Z" (59@charPos0)
        var sut = new SimConnectManager(IntPtr.Zero);
        Assert.Equal("ABZ", sut.UnpackWaypointName(2274.0, 59.0));
    }

    [Fact]
    public void UnpackWaypointName_trims_a_trailing_space_code()
    {
        // "AB" + trailing space (code 1 -> char 32): A=34@0, B=35@1, space=1@2
        // ident0 = 34 + 35*64 + 1*4096 = 6370
        var sut = new SimConnectManager(IntPtr.Zero);
        Assert.Equal("AB", sut.UnpackWaypointName(6370.0, 0.0));
    }

    [Fact]
    public void UnpackWaypointName_both_zero_yields_empty_string()
    {
        var sut = new SimConnectManager(IntPtr.Zero);
        Assert.Equal("", sut.UnpackWaypointName(0.0, 0.0));
    }
}
