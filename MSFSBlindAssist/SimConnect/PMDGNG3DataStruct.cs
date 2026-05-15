using System.Runtime.InteropServices;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Constants from PMDG_NG3_SDK.h — Client Data Area names/IDs and CDU geometry.
/// </summary>
public static class PMDGNG3Constants
{
    public const string PMDG_NG3_DATA_NAME = "PMDG_NG3_Data";
    public const uint PMDG_NG3_DATA_ID = 0x4E473331;
    public const uint PMDG_NG3_DATA_DEFINITION = 0x4E473332;

    public const string PMDG_NG3_CONTROL_NAME = "PMDG_NG3_Control";
    public const uint PMDG_NG3_CONTROL_ID = 0x4E473333;
    public const uint PMDG_NG3_CONTROL_DEFINITION = 0x4E473334;

    public const string PMDG_NG3_CDU_0_NAME = "PMDG_NG3_CDU_0";
    public const string PMDG_NG3_CDU_1_NAME = "PMDG_NG3_CDU_1";
    public const uint PMDG_NG3_CDU_0_ID = 0x4E473335;
    public const uint PMDG_NG3_CDU_1_ID = 0x4E473336;
    public const uint PMDG_NG3_CDU_0_DEFINITION = 0x4E473338;
    public const uint PMDG_NG3_CDU_1_DEFINITION = 0x4E473339;

    public const int CDU_COLUMNS = 24;
    public const int CDU_ROWS = 14;
    public const int CDU_CELL_COUNT = CDU_COLUMNS * CDU_ROWS;

    public const int THIRD_PARTY_EVENT_ID_MIN = 0x00011000;  // 69632
}

/// <summary>
/// One cell of the NG3 CDU broadcast — 3 bytes per cell (Symbol/Color/Flags).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PMDGNG3CDUCell
{
    public byte Symbol;
    public byte Color;
    public byte Flags;
}

/// <summary>CDU color codes used in <see cref="PMDGNG3CDUCell.Color"/>.</summary>
public static class PMDGNG3CDUColor
{
    public const byte WHITE = 0;
    public const byte CYAN = 1;
    public const byte GREEN = 2;
    public const byte MAGENTA = 3;
    public const byte AMBER = 4;
    public const byte RED = 5;
}

/// <summary>CDU cell flag bits.</summary>
[Flags]
public enum PMDGNG3CDUFlag : byte
{
    None = 0,
    SmallFont = 0x01,
    Reverse = 0x02,
    Unused = 0x04,
}

/// <summary>
/// CDU screen broadcast struct — 24 columns × 14 rows = 336 cells column-major
/// + Powered flag. Matches PMDG_NG3_CDU_Screen from the SDK header.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PMDGNG3CDUScreen
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = PMDGNG3Constants.CDU_CELL_COUNT)]
    public PMDGNG3CDUCell[] Cells;

    [MarshalAs(UnmanagedType.U1)]
    public bool Powered;
}

/// <summary>
/// Control area write target — set EventId and Parameter, then SimConnect.SetClientData
/// will fire the event into the simulator. PMDG WASM zeroes EventId after processing.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PMDGNG3Control
{
    public uint EventId;
    public uint Parameter;
}
