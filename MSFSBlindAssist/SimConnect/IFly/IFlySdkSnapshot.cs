using System;
using System.Text;

namespace MSFSBlindAssist.SimConnect.IFly;

/// <summary>
/// Immutable typed view over one polled copy of the iFly 737 MAX shared-memory
/// state block (ShareMemory737MAXSDK). All accessors read from the captured
/// byte buffer via the generated <see cref="IFlySdkOffsets"/> — no marshaling.
/// </summary>
public sealed class IFlySdkSnapshot
{
    private readonly byte[] _data;

    public IFlySdkSnapshot(byte[] data)
    {
        _data = data;
    }

    public byte ByteAt(int offset) => offset < _data.Length ? _data[offset] : (byte)0;
    public int IntAt(int offset) => offset + 4 <= _data.Length ? BitConverter.ToInt32(_data, offset) : 0;
    public double DoubleAt(int offset) => offset + 8 <= _data.Length ? BitConverter.ToDouble(_data, offset) : 0.0;
    public bool BoolAt(int offset) => ByteAt(offset) != 0;

    /// <summary>True when the iFly 737 MAX is the loaded/running aircraft (iFly737MAX_STATE == 1).</summary>
    public bool IsRunning => IntAt(IFlySdkOffsets.iFly737MAX_STATE) == 1;

    /// <summary>FS tick counter — advances while the plugin is alive; used as a staleness signal.</summary>
    public int Tick18 => IntAt(IFlySdkOffsets.Tick18);

    // ------------------------------------------------------------------
    // CDU screens: LSKChar[2][14][24] + LSK_SmallFont + LSK_Color
    // Color codes: 0 White, 1 Green, 2 Cyan, 3 Magenta, 4 Grey background,
    // 5 empty box, 6/7/8 degree symbol variants, 9 left arrow, 10 right arrow,
    // 11 up arrow, 12 down arrow, 13 solid box (v1.5).
    // ------------------------------------------------------------------
    public const int CduRows = 14;
    public const int CduCols = 24;

    /// <summary>One CDU screen row as readable text. unit: 0 = Captain (left CDU), 1 = FO (right CDU).</summary>
    public string CduLine(int unit, int row)
    {
        var sb = new StringBuilder(CduCols);
        int rowBase = IFlySdkOffsets.LSKChar + unit * IFlySdkOffsets.LSKChar_Stride0 + row * IFlySdkOffsets.LSKChar_Stride1;
        int colorBase = IFlySdkOffsets.LSK_Color + unit * IFlySdkOffsets.LSK_Color_Stride0 + row * IFlySdkOffsets.LSK_Color_Stride1;
        for (int c = 0; c < CduCols; c++)
        {
            byte color = ByteAt(colorBase + c);
            char ch = (char)ByteAt(rowBase + c);
            sb.Append(color switch
            {
                5 => '▯',            // empty box (data-entry box)
                6 or 7 or 8 => '°',  // degree symbol
                9 => '<',                 // left arrow (prompt)
                10 => '>',                // right arrow (prompt)
                11 => '↑',           // up arrow
                12 => '↓',           // down arrow
                13 => '■',           // solid box
                _ => ch == '\0' ? ' ' : ch,
            });
        }
        return sb.ToString();
    }

    /// <summary>Raw color code for a CDU cell (see class comment).</summary>
    public byte CduColor(int unit, int row, int col) =>
        ByteAt(IFlySdkOffsets.LSK_Color + unit * IFlySdkOffsets.LSK_Color_Stride0 + row * IFlySdkOffsets.LSK_Color_Stride1 + col);

    /// <summary>True when the CDU cell renders in the small font (label rows / inactive values).</summary>
    public bool CduSmallFont(int unit, int row, int col) =>
        BoolAt(IFlySdkOffsets.LSK_SmallFont + unit * IFlySdkOffsets.LSK_SmallFont_Stride0 + row * IFlySdkOffsets.LSK_SmallFont_Stride1 + col);

    public bool CduExecLit(int unit) => ByteAt(IFlySdkOffsets.CDU_EXEC_Status + unit) != 0;
    public bool CduMsgLit(int unit) => ByteAt(IFlySdkOffsets.CDU_MSG_Status + unit) != 0;
    public bool CduOfstLit(int unit) => ByteAt(IFlySdkOffsets.CDU_OFST_Status + unit) != 0;
    public bool CduCallLit(int unit) => ByteAt(IFlySdkOffsets.CDU_CALL_Status + unit) != 0;
    public bool CduFailLit(int unit) => ByteAt(IFlySdkOffsets.CDU_FAIL_Status + unit) != 0;

    /// <summary>The whole CDU screen (14 rows) for change hashing.</summary>
    public string CduScreenText(int unit)
    {
        var sb = new StringBuilder((CduCols + 2) * CduRows);
        for (int r = 0; r < CduRows; r++)
            sb.AppendLine(CduLine(unit, r));
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // MCP electronic display windows (per-digit statuses).
    // Digits: 0~9 = '0'~'9', 14 = blank. SPD symbol: 10 = 'A', 11 = '-', 12 = '+'.
    // ------------------------------------------------------------------
    private static char Digit(byte v) => v switch
    {
        <= 9 => (char)('0' + v),
        10 => 'A',
        11 => '-',
        12 => '+',
        _ => ' ',
    };

    /// <summary>MCP IAS/MACH window, e.g. "250", ".78", or "" when blanked (FMC speed).</summary>
    public string McpSpeedText()
    {
        char sym = Digit(ByteAt(IFlySdkOffsets.SPD_Symbol_Status));
        char h = Digit(ByteAt(IFlySdkOffsets.SPD_100_Status));
        char t = Digit(ByteAt(IFlySdkOffsets.SPD_10_Status));
        char o = Digit(ByteAt(IFlySdkOffsets.SPD_1_Status));
        bool point = ByteAt(IFlySdkOffsets.SPD_Point_Status) != 0;
        string s = point ? $"{sym}{h}.{t}{o}" : $"{sym}{h}{t}{o}";
        return s.Trim();
    }

    /// <summary>True when the MCP speed window is blanked (VNAV/FMC-managed speed).</summary>
    public bool McpSpeedBlank() =>
        ByteAt(IFlySdkOffsets.SPD_Symbol_Status) == 14 && ByteAt(IFlySdkOffsets.SPD_100_Status) == 14 &&
        ByteAt(IFlySdkOffsets.SPD_10_Status) == 14 && ByteAt(IFlySdkOffsets.SPD_1_Status) == 14;

    public string McpHeadingText() =>
        $"{Digit(ByteAt(IFlySdkOffsets.HDG_100_Status))}{Digit(ByteAt(IFlySdkOffsets.HDG_10_Status))}{Digit(ByteAt(IFlySdkOffsets.HDG_1_Status))}".Trim();

    public string McpAltitudeText() =>
        ($"{Digit(ByteAt(IFlySdkOffsets.ALT_10000_Status))}{Digit(ByteAt(IFlySdkOffsets.ALT_1000_Status))}" +
         $"{Digit(ByteAt(IFlySdkOffsets.ALT_100_Status))}{Digit(ByteAt(IFlySdkOffsets.ALT_10_Status))}{Digit(ByteAt(IFlySdkOffsets.ALT_1_Status))}").Trim();

    /// <summary>MCP V/S window, e.g. "-1500", or "" when blanked.</summary>
    public string McpVerticalSpeedText()
    {
        string s = ($"{Digit(ByteAt(IFlySdkOffsets.VS_Symbol_Status))}{Digit(ByteAt(IFlySdkOffsets.VS_1000_Status))}" +
                    $"{Digit(ByteAt(IFlySdkOffsets.VS_100_Status))}{Digit(ByteAt(IFlySdkOffsets.VS_10_Status))}{Digit(ByteAt(IFlySdkOffsets.VS_1_Status))}").Trim();
        return s;
    }

    public string McpCourseText(int side) => side == 0
        ? $"{Digit(ByteAt(IFlySdkOffsets.Course_1_100_Status))}{Digit(ByteAt(IFlySdkOffsets.Course_1_10_Status))}{Digit(ByteAt(IFlySdkOffsets.Course_1_1_Status))}".Trim()
        : $"{Digit(ByteAt(IFlySdkOffsets.Course_2_100_Status))}{Digit(ByteAt(IFlySdkOffsets.Course_2_10_Status))}{Digit(ByteAt(IFlySdkOffsets.Course_2_1_Status))}".Trim();

    // ------------------------------------------------------------------
    // ELEC maintenance LED text (2 lines x 12 chars) — DC/AC meter values.
    // ------------------------------------------------------------------
    public string ElecLedLine(int line)
    {
        var sb = new StringBuilder(12);
        int b = IFlySdkOffsets.ELEC_LED_TEXT + line * IFlySdkOffsets.ELEC_LED_TEXT_Stride0;
        for (int i = 0; i < 12; i++)
        {
            char ch = (char)ByteAt(b + i);
            sb.Append(ch == '\0' ? ' ' : ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>IRS display windows (left 6 chars + right 7 chars with decimal points).</summary>
    public string IrsDisplayText()
    {
        char C(int off) => ByteAt(off) switch
        {
            <= 9 and var v => (char)('0' + v),
            10 => ' ',
            11 => 'E',
            12 => 'N',
            13 => 'S',
            14 => 'T',
            15 => 'W',
            16 => 'M',
            17 => 'H',
            _ => ' ',
        };
        var l = new StringBuilder();
        l.Append(C(IFlySdkOffsets.IRS_Window_L_1_Status));
        l.Append(C(IFlySdkOffsets.IRS_Window_L_2_Status));
        if (BoolAt(IFlySdkOffsets.IRS_Window_L_point_1_Status)) l.Append('.');
        l.Append(C(IFlySdkOffsets.IRS_Window_L_3_Status));
        if (BoolAt(IFlySdkOffsets.IRS_Window_L_point_2_Status)) l.Append('.');
        l.Append(C(IFlySdkOffsets.IRS_Window_L_4_Status));
        if (BoolAt(IFlySdkOffsets.IRS_Window_L_point_3_Status)) l.Append('.');
        l.Append(C(IFlySdkOffsets.IRS_Window_L_5_Status));
        l.Append(C(IFlySdkOffsets.IRS_Window_L_6_Status));
        var r = new StringBuilder();
        r.Append(C(IFlySdkOffsets.IRS_Window_R_1_Status));
        r.Append(C(IFlySdkOffsets.IRS_Window_R_2_Status));
        r.Append(C(IFlySdkOffsets.IRS_Window_R_3_Status));
        if (BoolAt(IFlySdkOffsets.IRS_Window_R_point_1_Status)) r.Append('.');
        r.Append(C(IFlySdkOffsets.IRS_Window_R_4_Status));
        if (BoolAt(IFlySdkOffsets.IRS_Window_R_point_2_Status)) r.Append('.');
        r.Append(C(IFlySdkOffsets.IRS_Window_R_5_Status));
        if (BoolAt(IFlySdkOffsets.IRS_Window_R_point_3_Status)) r.Append('.');
        r.Append(C(IFlySdkOffsets.IRS_Window_R_6_Status));
        r.Append(C(IFlySdkOffsets.IRS_Window_R_7_Status));
        string left = l.ToString().Trim(), right = r.ToString().Trim();
        if (left.Length == 0 && right.Length == 0) return "";
        return $"{left}  {right}".Trim();
    }

    /// <summary>Transponder code window, e.g. "2000"; "" when blanked.</summary>
    public string TransponderCodeText() =>
        ($"{Digit(ByteAt(IFlySdkOffsets.Transponder_Windows_Digital_1000_Status))}{Digit(ByteAt(IFlySdkOffsets.Transponder_Windows_Digital_100_Status))}" +
         $"{Digit(ByteAt(IFlySdkOffsets.Transponder_Windows_Digital_10_Status))}{Digit(ByteAt(IFlySdkOffsets.Transponder_Windows_Digital_1_Status))}").Trim();

    /// <summary>Fuel quantity indicator (tank: 0 = left, 1 = right, 2 = center), digits only.</summary>
    public string FuelQuantityText(int tank)
    {
        var sb = new StringBuilder(5);
        int b = IFlySdkOffsets.Fuel_Quantity_Indicator_Status + tank * IFlySdkOffsets.Fuel_Quantity_Indicator_Status_Stride0;
        for (int i = 0; i < 5; i++) sb.Append(Digit(ByteAt(b + i)));
        return sb.ToString().Trim();
    }

    // ------------------------------------------------------------------
    // Radio Tuning Panel (RTP) displays: index 0/1/2 = RTP 1 (Captain) / RTP 2 (FO) / RTP 3 (overhead).
    // Char codes: 0~9 digits, 10 A, 11 D, 12 E, 13 F, 14 I, 15 L, 16 N, 17 O, 18 P, 19 T, 24 blank.
    // ------------------------------------------------------------------
    private static char RtpChar(byte v) => v switch
    {
        <= 9 => (char)('0' + v),
        10 => 'A',
        11 => 'D',
        12 => 'E',
        13 => 'F',
        14 => 'I',
        15 => 'L',
        16 => 'N',
        17 => 'O',
        18 => 'P',
        19 => 'T',
        _ => ' ',
    };

    /// <summary>One side of an RTP display (left = active, right = standby), e.g. "118.250".</summary>
    public string RtpText(int rtp, bool rightSide)
    {
        int o100 = rightSide ? IFlySdkOffsets.RTP_Right_Num_100_Status : IFlySdkOffsets.RTP_Left_Num_100_Status;
        int o10 = rightSide ? IFlySdkOffsets.RTP_Right_Num_10_Status : IFlySdkOffsets.RTP_Left_Num_10_Status;
        int o1 = rightSide ? IFlySdkOffsets.RTP_Right_Num_1_Status : IFlySdkOffsets.RTP_Left_Num_1_Status;
        int op1 = rightSide ? IFlySdkOffsets.RTP_Right_Num_point1_Status : IFlySdkOffsets.RTP_Left_Num_point1_Status;
        int op2 = rightSide ? IFlySdkOffsets.RTP_Right_Num_point2_Status : IFlySdkOffsets.RTP_Left_Num_point2_Status;
        int op3 = rightSide ? IFlySdkOffsets.RTP_Right_Num_point3_Status : IFlySdkOffsets.RTP_Left_Num_point3_Status;
        int opoint = rightSide ? IFlySdkOffsets.RTP_Right_Num_point_Status : IFlySdkOffsets.RTP_Left_Num_point_Status;
        var sb = new StringBuilder(8);
        sb.Append(RtpChar(ByteAt(o100 + rtp)));
        sb.Append(RtpChar(ByteAt(o10 + rtp)));
        sb.Append(RtpChar(ByteAt(o1 + rtp)));
        if (ByteAt(opoint + rtp) != 0) sb.Append('.');
        sb.Append(RtpChar(ByteAt(op1 + rtp)));
        sb.Append(RtpChar(ByteAt(op2 + rtp)));
        sb.Append(RtpChar(ByteAt(op3 + rtp)));
        return sb.ToString().Trim();
    }

    // ------------------------------------------------------------------
    // NAV control panel displays (aft pedestal): panel 0/1 = left (NAV 1) / right
    // (NAV 2); window 0/1 = left (active) / right (standby), the RTP convention.
    // Digit codes: 0~9 digits, 10 blank, 11 line '-', 12+ full display (test).
    // ------------------------------------------------------------------

    /// <summary>NAV window frequency text, e.g. "110.90" (VOR/ILS) or a 5-digit
    /// GLS channel; "" when blank; "---" style lines when failed/unpowered.</summary>
    public string NavFrequencyText(int panel, int window)
    {
        int idx = panel * IFlySdkOffsets.NAV_num_Flag_Status_Stride0 + window;
        char C(int baseOff) => ByteAt(baseOff + idx) switch
        {
            <= 9 and var v => (char)('0' + v),
            10 => ' ',
            11 => '-',
            _ => '8', // full display (test pattern)
        };
        var sb = new StringBuilder(8);
        sb.Append(C(IFlySdkOffsets.NAV_num_100_Status));
        sb.Append(C(IFlySdkOffsets.NAV_num_10_Status));
        if (ByteAt(IFlySdkOffsets.NAV_num_Point1_Status + idx) != 0) sb.Append('.');
        sb.Append(C(IFlySdkOffsets.NAV_num_1_Status));
        if (ByteAt(IFlySdkOffsets.NAV_num_Point2_Status + idx) != 0) sb.Append('.');
        sb.Append(C(IFlySdkOffsets.NAV_num_01_Status));
        sb.Append(C(IFlySdkOffsets.NAV_num_001_Status));
        sb.Append(C(IFlySdkOffsets.NAV_num_0001_Status));
        return sb.ToString().Replace(" ", "");
    }

    /// <summary>NAV window mode flag ("GLS"/"ILS"/"VOR"/"ERR"/"Test"), "" when blank.</summary>
    public string NavFlagText(int panel, int window) =>
        ByteAt(IFlySdkOffsets.NAV_num_Flag_Status + panel * IFlySdkOffsets.NAV_num_Flag_Status_Stride0 + window) switch
        {
            0 => "GLS",
            1 => "ILS",
            2 => "VOR",
            3 => "ERR",
            4 => "Test",
            _ => "",
        };

    /// <summary>Combined NAV window readout, e.g. "110.90 ILS"; "" when fully blank.</summary>
    public string NavWindowText(int panel, int window)
    {
        string freq = NavFrequencyText(panel, window);
        string flag = NavFlagText(panel, window);
        return freq.Length > 0 && flag.Length > 0 ? $"{freq} {flag}" : freq.Length > 0 ? freq : flag;
    }
}
