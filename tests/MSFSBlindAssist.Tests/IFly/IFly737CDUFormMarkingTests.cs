// Characterization tests for IFly737CDUForm's pure color-based selection marking
// (Forms/IFly737/IFly737CDUForm.cs): MarkSelectedOption and CduColorPlaneText.
// Both were already `private static` pure functions operating only on an
// IFlySdkSnapshot + row text — promoted to `internal static` (zero logic change) so
// they're directly testable without constructing the form.

namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.Forms.IFly737;
using MSFSBlindAssist.SimConnect.IFly;

public class IFly737CDUFormMarkingTests
{
    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];
    private static IFlySdkSnapshot Snap(byte[] b) => new(b);

    private const int Unit = 0;
    private const int Row = 4;

    private static void SetColor(byte[] b, int row, int col, byte color) =>
        b[IFlySdkOffsets.LSK_Color + Unit * IFlySdkOffsets.LSK_Color_Stride0 + row * IFlySdkOffsets.LSK_Color_Stride1 + col] = color;

    // --- MarkSelectedOption --------------------------------------------------------------

    [Fact]
    public void MarkSelectedOption_GreyRun_PrefixedWithX()
    {
        var b = Buf();
        string text = "   CI 000   ";
        // Highlight "CI" (columns 3-4) grey — the active-selection cue.
        SetColor(b, Row, 3, 4);
        SetColor(b, Row, 4, 4);

        string result = IFly737CDUForm.MarkSelectedOption(Snap(b), Unit, Row, text);

        Assert.Equal("   X CI 000   ", result);
    }

    [Fact]
    public void MarkSelectedOption_NoGreyCells_RowUnchanged()
    {
        var b = Buf();
        string text = "   CI 000   ";

        string result = IFly737CDUForm.MarkSelectedOption(Snap(b), Unit, Row, text);

        Assert.Equal(text, result);
    }

    [Fact]
    public void MarkSelectedOption_WhitespaceOnlyGreyRun_NotMarked()
    {
        var b = Buf();
        string text = "AAA   BBB";
        // Highlight only the spaces between the two words — no visible text to mark.
        SetColor(b, Row, 3, 4);
        SetColor(b, Row, 4, 4);
        SetColor(b, Row, 5, 4);

        string result = IFly737CDUForm.MarkSelectedOption(Snap(b), Unit, Row, text);

        Assert.Equal(text, result);
    }

    [Fact]
    public void MarkSelectedOption_TwoSeparateGreyRuns_BothMarked()
    {
        var b = Buf();
        // "LNAV   VNAV" with LNAV (0-3) and VNAV (7-10) both highlighted as
        // independently-selected options (e.g. two separate toggle fields on one row).
        string text = "LNAV   VNAV";
        SetColor(b, Row, 0, 4);
        SetColor(b, Row, 1, 4);
        SetColor(b, Row, 2, 4);
        SetColor(b, Row, 3, 4);
        SetColor(b, Row, 7, 4);
        SetColor(b, Row, 8, 4);
        SetColor(b, Row, 9, 4);
        SetColor(b, Row, 10, 4);

        string result = IFly737CDUForm.MarkSelectedOption(Snap(b), Unit, Row, text);

        Assert.Equal("X LNAV   X VNAV", result);
    }

    [Fact]
    public void MarkSelectedOption_NonGreyColor_NotMarked()
    {
        var b = Buf();
        string text = "CI 000";
        // Green text (color 1) is a normal readable color, not the grey/inverse
        // selection cue — must not be marked.
        SetColor(b, Row, 0, 1);
        SetColor(b, Row, 1, 1);

        string result = IFly737CDUForm.MarkSelectedOption(Snap(b), Unit, Row, text);

        Assert.Equal(text, result);
    }

    // --- CduColorPlaneText -----------------------------------------------------------------

    [Fact]
    public void CduColorPlaneText_DiffersWhenOnlyColorChanges()
    {
        var b1 = Buf();
        var b2 = Buf();
        SetColor(b2, Row, 0, 4); // highlight added, no character changed anywhere

        string plane1 = IFly737CDUForm.CduColorPlaneText(Snap(b1), Unit);
        string plane2 = IFly737CDUForm.CduColorPlaneText(Snap(b2), Unit);

        Assert.NotEqual(plane1, plane2);
    }

    [Fact]
    public void CduColorPlaneText_IdenticalColors_ProducesIdenticalPlane()
    {
        var b1 = Buf();
        var b2 = Buf();
        SetColor(b1, Row, 2, 4);
        SetColor(b2, Row, 2, 4);

        Assert.Equal(IFly737CDUForm.CduColorPlaneText(Snap(b1), Unit), IFly737CDUForm.CduColorPlaneText(Snap(b2), Unit));
    }

    [Fact]
    public void CduColorPlaneText_OtherUnitChange_DoesNotAffectThisUnitsPlane()
    {
        var b1 = Buf();
        var b2 = Buf();
        // Highlight a cell on unit 1 (FO) only — unit 0's (Captain) plane must be unaffected.
        b2[IFlySdkOffsets.LSK_Color + 1 * IFlySdkOffsets.LSK_Color_Stride0 + Row * IFlySdkOffsets.LSK_Color_Stride1 + 0] = 4;

        Assert.Equal(IFly737CDUForm.CduColorPlaneText(Snap(b1), Unit), IFly737CDUForm.CduColorPlaneText(Snap(b2), Unit));
    }
}
