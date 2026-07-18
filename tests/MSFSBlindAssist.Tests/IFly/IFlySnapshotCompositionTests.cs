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
}
