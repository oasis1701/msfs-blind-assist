using System.Runtime.InteropServices;
using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the MD-11 MCDU client-data-area layout against TFDi's published declaration.
///
/// This is the highest-stakes struct in the MD-11 work and it fails SILENTLY: a wrong size or a
/// mis-marshalled bool does not throw, it just reads misaligned bytes and renders the CDU as
/// garbage.
///
/// THE SIZE HAS BEEN WRONG TWICE. Both wrong answers are pinned here as explicit NotEqual guards,
/// because each is a live trap for the next person who "re-derives" it:
///
///   1. **8068** — a doc-summarising fetch back-computed a 24-byte MCDUCHAR. Fabricated.
///   2. **1348** — assumed NATURAL alignment (MCDUCHAR = char16_t 2 + bool 1 + 1 pad = 4).
///      Plausible C++ and self-consistent arithmetic — which is exactly why it survived: the
///      size assert written against it PASSED while every cell was misaligned. A guard whose
///      two sides are both derived from the same wrong assumption cannot fail.
///
/// The real value is **1012**: TFDi PACK these structs (`#pragma pack(1)`), so MCDUCHAR is 3
/// bytes with no tail padding. Proven from md11host.wasm — DWARF `MCDUEXPORTDATA byte_size = 1012`
/// / `MCDUCHAR byte_size = 3`, plus a literal `i32.const 1012` in SetupExportData (code 969964).
/// Scanning the whole 103 MB binary: `i32.const 1012` appears 9 times, `i32.const 1348` ZERO.
///
/// So these tests deliberately assert against the BINARY's numbers, not against our own
/// arithmetic — that is the only check with an independent source on the other side.
///
/// TFDi's declaration, verbatim:
///   #pragma pack(push, 1)
///   struct MCDUCHAR       { char16_t value; bool large = false; };
///   struct MCDUEXPORTDATA { bool dspy, fail, msg, ofst; MCDUCHAR text[MCDU_ROWS][MCDU_COLS]; };
///   #define MCDU_ROWS 14 / #define MCDU_COLS 24
///   size_t MCDU_DATA_SIZE = sizeof(MCDUCHAR) * MCDU_CHARS + (sizeof(bool) * 4);
/// </summary>
public class Md11McduLayoutTests
{
    /// <summary>
    /// 3 bytes — PACKED. Not 4: the natural-alignment reading (2 + 1 + 1 pad) is what made the
    /// CDU read garbage, and it is the mistake most likely to be made again.
    /// </summary>
    [Fact]
    public void McduChar_IsThreeBytes_Packed()
    {
        Assert.Equal(3, Marshal.SizeOf<Md11McduChar>());
        Assert.NotEqual(4, Marshal.SizeOf<Md11McduChar>());
    }

    [Fact]
    public void McduExportData_Marshals_To1012_NeitherOfTheTwoWrongAnswers()
    {
        // 3 * 336 + 4 == 1012, matching the i32.const in the aircraft's own SetupExportData.
        Assert.Equal(1012, Md11McduLayout.DataSize);
        Assert.Equal(Md11McduLayout.DataSize, Marshal.SizeOf<Md11McduExportData>());

        // The two historical wrong answers, asserted against the MARSHALLED size so these cannot
        // become tautologies if DataSize is edited again.
        Assert.NotEqual(1348, Marshal.SizeOf<Md11McduExportData>());
        Assert.NotEqual(8068, Marshal.SizeOf<Md11McduExportData>());
    }

    [Fact]
    public void Grid_Is14By24()
    {
        Assert.Equal(14, Md11McduLayout.Rows);
        Assert.Equal(24, Md11McduLayout.Cols);
        Assert.Equal(336, Md11McduLayout.Chars);
    }

    /// <summary>The four flags must occupy exactly 4 bytes, so the grid starts at offset 4.</summary>
    [Fact]
    public void Flags_OccupyFourBytes_SoTextStartsAtOffset4()
    {
        var textOffset = Md11McduLayout.DataSize - (Md11McduLayout.Chars * Marshal.SizeOf<Md11McduChar>());

        Assert.Equal(4, textOffset);
    }

    /// <summary>All three MCDUs live in ONE area, back to back — unlike PMDG's area-per-CDU.</summary>
    [Fact]
    public void Offsets_AreContiguousThirds()
    {
        Assert.Equal(0, Md11McduLayout.OffsetLeft);
        Assert.Equal(1012, Md11McduLayout.OffsetCenter);
        Assert.Equal(2024, Md11McduLayout.OffsetRight);
        Assert.Equal(3036, Md11McduLayout.AreaSize);
        Assert.Equal(Md11McduLayout.DataSize * 3, Md11McduLayout.AreaSize);
    }

    [Fact]
    public void AreaName_IsMd11Mcdu()
    {
        // Corroborated as a literal string in md11host.wasm.
        Assert.Equal("MD11MCDU", Md11McduLayout.AreaName);
    }

    /// <summary>Order must match TFDi's CLIENT_DATA_DEFINE_ID enum — it decides the offsets.</summary>
    [Fact]
    public void UnitEnum_OrderMatchesTfdiDefineIds()
    {
        Assert.Equal(0, (int)Md11McduUnit.Left);
        Assert.Equal(1, (int)Md11McduUnit.Center);
        Assert.Equal(2, (int)Md11McduUnit.Right);
    }

    /// <summary>Scratchpad is the bottom line; title is the top.</summary>
    [Fact]
    public void Screen_ScratchpadIsBottomLine_TitleIsTop()
    {
        var lines = new string[Md11McduLayout.Rows];
        for (var i = 0; i < lines.Length; i++) lines[i] = $"line{i}";

        var screen = new Md11McduScreen { Lines = lines };

        Assert.Equal("line0", screen.Title);
        Assert.Equal("line13", screen.Scratchpad);
    }
}
