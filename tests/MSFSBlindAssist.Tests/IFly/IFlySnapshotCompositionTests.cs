namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.SimConnect.IFly;

public class IFlySnapshotCompositionTests
{
    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];
    private static IFlySdkSnapshot Snap(byte[] b) => new(b);

    [Fact]
    public void McpSpeed_Ias_ComposesInteger()
    {
        var b = Buf();
        b[IFlySdkOffsets.SPD_Symbol_Status] = 14; // blank symbol cell
        b[IFlySdkOffsets.SPD_100_Status] = 2;
        b[IFlySdkOffsets.SPD_10_Status] = 8;
        b[IFlySdkOffsets.SPD_1_Status] = 0;
        Assert.Equal("280", Snap(b).McpSpeedText());
        Assert.False(Snap(b).McpSpeedBlank());
    }

    [Fact]
    public void McpSpeed_Mach_ComposesWithPoint()
    {
        var b = Buf();
        b[IFlySdkOffsets.SPD_Symbol_Status] = 14;
        b[IFlySdkOffsets.SPD_100_Status] = 0;
        b[IFlySdkOffsets.SPD_10_Status] = 7;
        b[IFlySdkOffsets.SPD_1_Status] = 8;
        b[IFlySdkOffsets.SPD_Point_Status] = 1;
        Assert.Equal("0.78", Snap(b).McpSpeedText());
    }

    [Fact]
    public void McpSpeed_AllBlank_ReportsBlank()
    {
        var b = Buf();
        b[IFlySdkOffsets.SPD_Symbol_Status] = 14;
        b[IFlySdkOffsets.SPD_100_Status] = 14;
        b[IFlySdkOffsets.SPD_10_Status] = 14;
        b[IFlySdkOffsets.SPD_1_Status] = 14;
        Assert.True(Snap(b).McpSpeedBlank());
        Assert.Equal("", Snap(b).McpSpeedText());
    }

    [Fact]
    public void McpSpeed_SymbolLetterA_ComposesLiteralText()
    {
        // Non-blank, unparseable symbol text (e.g. an "A250" speed-window flag) —
        // this is the composition the SYN_MCP_SPEED text-sentinel path (IFlySdkClient
        // .RaiseSyntheticEvents) depends on to speak the literal window text instead
        // of falsely reporting "blank, FMC managed".
        var b = Buf();
        b[IFlySdkOffsets.SPD_Symbol_Status] = 10; // 'A'
        b[IFlySdkOffsets.SPD_100_Status] = 2;
        b[IFlySdkOffsets.SPD_10_Status] = 5;
        b[IFlySdkOffsets.SPD_1_Status] = 0;
        Assert.Equal("A250", Snap(b).McpSpeedText());
        Assert.False(Snap(b).McpSpeedBlank());
    }

    [Fact]
    public void McpVerticalSpeed_NegativeSign_Composes()
    {
        var b = Buf();
        b[IFlySdkOffsets.VS_Symbol_Status] = 11; // '-'
        b[IFlySdkOffsets.VS_1000_Status] = 1;
        b[IFlySdkOffsets.VS_100_Status] = 5;
        b[IFlySdkOffsets.VS_10_Status] = 0;
        b[IFlySdkOffsets.VS_1_Status] = 0;
        Assert.Equal("-1500", Snap(b).McpVerticalSpeedText());
    }

    [Fact]
    public void McpHeading_LeadingZeros_Kept()
    {
        var b = Buf();
        b[IFlySdkOffsets.HDG_100_Status] = 0;
        b[IFlySdkOffsets.HDG_10_Status] = 0;
        b[IFlySdkOffsets.HDG_1_Status] = 5;
        Assert.Equal("005", Snap(b).McpHeadingText());
    }

    // ===== M4: these two FAIL against the buggy shared Digit() map =====

    [Fact]
    public void TransponderCode_Code10IsBlank_NotLetterA()
    {
        var b = Buf();
        // All four windows blanked (code 10 per the generated header doc for
        // Transponder_Windows_Digital_*: "0~9:'0'~'9' 10:blank").
        b[IFlySdkOffsets.Transponder_Windows_Digital_1000_Status] = 10;
        b[IFlySdkOffsets.Transponder_Windows_Digital_100_Status] = 10;
        b[IFlySdkOffsets.Transponder_Windows_Digital_10_Status] = 10;
        b[IFlySdkOffsets.Transponder_Windows_Digital_1_Status] = 10;
        Assert.Equal("", Snap(b).TransponderCodeText());
    }

    [Fact]
    public void TransponderCode_Digits_Compose()
    {
        var b = Buf();
        b[IFlySdkOffsets.Transponder_Windows_Digital_1000_Status] = 2;
        b[IFlySdkOffsets.Transponder_Windows_Digital_100_Status] = 0;
        b[IFlySdkOffsets.Transponder_Windows_Digital_10_Status] = 0;
        b[IFlySdkOffsets.Transponder_Windows_Digital_1_Status] = 0;
        Assert.Equal("2000", Snap(b).TransponderCodeText());
    }

    [Fact]
    public void FuelQuantity_LeadingBlankCode10_TrimsToDigits()
    {
        var b = Buf();
        int t0 = IFlySdkOffsets.Fuel_Quantity_Indicator_Status; // tank 0, 5 cells
        b[t0 + 0] = 10; // blank (leading)
        b[t0 + 1] = 8;
        b[t0 + 2] = 4;
        b[t0 + 3] = 6;
        b[t0 + 4] = 0;
        Assert.Equal("8460", Snap(b).FuelQuantityText(0));
    }

    // ===== ADF standby window (round-2 task 6) =====
    // Five digit cells (1000/100/10/1/01, unit stride 1 — index 0 = Left/ADF1,
    // 1 = Right/ADF2) + four independent decimal-point flags ("Decimal after
    // 1st/2nd/3rd/4th digital" — one flag per gap between the five cells).
    // Digit map is the XPDR/fuel-gauge one (10 = blank), NOT the MCP window map
    // (10 = 'A') — see IFlySdkSnapshot.XpdrFuelDigit's doc comment / PR #163 M4.

    [Fact]
    public void AdfText_ComposesFrequencyWithPointAfter4thDigit()
    {
        var b = Buf();
        const int u = 0; // Left / ADF1
        b[IFlySdkOffsets.ADF_num_1000_Status + u] = 10; // blank leading thousands
        b[IFlySdkOffsets.ADF_num_100_Status + u] = 2;
        b[IFlySdkOffsets.ADF_num_10_Status + u] = 3;
        b[IFlySdkOffsets.ADF_num_1_Status + u] = 4;
        b[IFlySdkOffsets.ADF_num_point4_Status + u] = 1; // decimal after the 4th digit (the '4')
        b[IFlySdkOffsets.ADF_num_01_Status + u] = 5;
        Assert.Equal("234.5", Snap(b).AdfText(u));
    }

    [Fact]
    public void AdfText_AllBlank_ReturnsEmpty()
    {
        var b = Buf();
        const int u = 1; // Right / ADF2
        b[IFlySdkOffsets.ADF_num_1000_Status + u] = 10;
        b[IFlySdkOffsets.ADF_num_100_Status + u] = 10;
        b[IFlySdkOffsets.ADF_num_10_Status + u] = 10;
        b[IFlySdkOffsets.ADF_num_1_Status + u] = 10;
        b[IFlySdkOffsets.ADF_num_01_Status + u] = 10;
        Assert.Equal("", Snap(b).AdfText(u));
    }

    [Fact]
    public void AdfText_PointPlacement_HonorsWhicheverGapFlagIsSet()
    {
        // Same five digits as the "234.5" case, but with point2 set instead of
        // point4 (decimal after the 2nd digit, between the hundreds and tens
        // cells) — pins that AdfText places the '.' at the FLAGGED gap, not a
        // hardcoded "one digit before the end" position.
        var b = Buf();
        const int u = 0;
        b[IFlySdkOffsets.ADF_num_1000_Status + u] = 1;
        b[IFlySdkOffsets.ADF_num_100_Status + u] = 2;
        b[IFlySdkOffsets.ADF_num_point2_Status + u] = 1; // decimal after the 2nd digit (the '2')
        b[IFlySdkOffsets.ADF_num_10_Status + u] = 3;
        b[IFlySdkOffsets.ADF_num_1_Status + u] = 4;
        b[IFlySdkOffsets.ADF_num_01_Status + u] = 5;
        Assert.Equal("12.345", Snap(b).AdfText(u));
    }

    [Fact]
    public void AdfText_Unit1_IsIndependentOfUnit0()
    {
        // Stride isolation: ADF1 (unit 0) tuned to "234.5", ADF2 (unit 1) fully
        // blanked — reading unit 1 must not pick up any of unit 0's byte cells
        // (each field is base + unit, stride 1).
        var b = Buf();
        b[IFlySdkOffsets.ADF_num_1000_Status + 0] = 10;
        b[IFlySdkOffsets.ADF_num_100_Status + 0] = 2;
        b[IFlySdkOffsets.ADF_num_10_Status + 0] = 3;
        b[IFlySdkOffsets.ADF_num_1_Status + 0] = 4;
        b[IFlySdkOffsets.ADF_num_point4_Status + 0] = 1;
        b[IFlySdkOffsets.ADF_num_01_Status + 0] = 5;

        b[IFlySdkOffsets.ADF_num_1000_Status + 1] = 10;
        b[IFlySdkOffsets.ADF_num_100_Status + 1] = 10;
        b[IFlySdkOffsets.ADF_num_10_Status + 1] = 10;
        b[IFlySdkOffsets.ADF_num_1_Status + 1] = 10;
        b[IFlySdkOffsets.ADF_num_01_Status + 1] = 10;

        var snap = Snap(b);
        Assert.Equal("234.5", snap.AdfText(0));
        Assert.Equal("", snap.AdfText(1));
    }
}
