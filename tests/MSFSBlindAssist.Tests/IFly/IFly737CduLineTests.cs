// Characterization tests for IFlySdkSnapshot.CduLine's symbol-cell mapping
// (SimConnect/IFly/IFlySdkSnapshot.cs). The color-code channel doubles as a
// symbol selector; the important pin here is the ACCESSIBLE rendering of the
// empty data-entry box prompt (color 5): it must be a plain '-' — NVDA has no
// spoken symbol for the faithful U+25AF box glyph and braille tables render
// nothing for it, which made required-entry fields (POS INIT line 4) read as
// blank (live report 2026-07-23). Dashes match the CDU's own optional-entry
// prompt style and read/braille correctly.

namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.SimConnect.IFly;

public class IFly737CduLineTests
{
    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];

    private static void SetCell(byte[] b, int unit, int row, int col, byte ch, byte color)
    {
        b[IFlySdkOffsets.LSKChar + unit * IFlySdkOffsets.LSKChar_Stride0
          + row * IFlySdkOffsets.LSKChar_Stride1 + col] = ch;
        b[IFlySdkOffsets.LSK_Color + unit * IFlySdkOffsets.LSK_Color_Stride0
          + row * IFlySdkOffsets.LSK_Color_Stride1 + col] = color;
    }

    [Fact]
    public void CduLine_EntryBoxPrompt_RendersAsReadableDash()
    {
        var b = Buf();
        SetCell(b, unit: 0, row: 8, col: 6, ch: 0, color: 5); // empty data-entry box
        Assert.Equal('-', new IFlySdkSnapshot(b).CduLine(0, 8)[6]);
    }

    [Fact]
    public void CduLine_DegreeAndPlainChars_RenderFaithfully()
    {
        var b = Buf();
        SetCell(b, 0, 2, 0, (byte)'N', 0);  // plain char passes through
        SetCell(b, 0, 2, 1, 0, 6);          // degree symbol (any of colors 6-8)
        string line = new IFlySdkSnapshot(b).CduLine(0, 2);
        Assert.Equal('N', line[0]);
        Assert.Equal('°', line[1]);
        Assert.Equal(' ', line[2]);         // NUL cell renders as space
    }
}
