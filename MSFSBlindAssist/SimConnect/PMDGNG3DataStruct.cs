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
[StructLayout(LayoutKind.Sequential, Pack = 1)]
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

/// <summary>
/// Binary-compatible mirror of struct PMDG_NG3_Data from PMDG_NG3_SDK.h.
/// Field order matches the SDK header verbatim and MUST NOT be reordered —
/// SimConnect silently truncates on size mismatch.
/// Uses default sequential layout (no Pack) to match MSVC default alignment.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PMDGNG3DataStruct
{
    // ===== Aft overhead =====

    // ADIRU
    public byte IRS_DisplaySelector;                            // Positions 0..4
    [MarshalAs(UnmanagedType.U1)]
    public bool IRS_SysDisplay_R;                               // false: L  true: R
    [MarshalAs(UnmanagedType.U1)]
    public bool IRS_annunGPS;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] IRS_annunALIGN;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] IRS_annunON_DC;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] IRS_annunFAULT;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] IRS_annunDC_FAIL;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] IRS_ModeSelector;                             // 0: OFF  1: ALIGN  2: NAV  3: ATT
    [MarshalAs(UnmanagedType.U1)]
    public bool IRS_aligned;                                    // at least one IRU is aligned
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 7)]
    public byte[] IRS_DisplayLeft;                              // char[7], zero terminated
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 8)]
    public byte[] IRS_DisplayRight;                             // char[8], zero terminated
    [MarshalAs(UnmanagedType.U1)]
    public bool IRS_DisplayShowsDots;

    // AFS
    [MarshalAs(UnmanagedType.U1)]
    public bool AFS_AutothrottleServosConnected;
    [MarshalAs(UnmanagedType.U1)]
    public bool AFS_ControlsPitch;
    [MarshalAs(UnmanagedType.U1)]
    public bool AFS_ControlsRoll;

    // PSEU
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunPSEU;

    // Service Interphone
    [MarshalAs(UnmanagedType.U1)]
    public bool COMM_ServiceInterphoneSw;

    // Lights
    public byte LTS_DomeWhiteSw;                                // 0: DIM  1: OFF  2: BRIGHT

    // Engine
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ENG_EECSwitch;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ENG_annunREVERSER;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ENG_annunENGINE_CONTROL;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ENG_annunALTN;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ENG_StartValve;                               // true: valve open

    // Oxygen
    public byte OXY_Needle;                                     // Position 0...240
    [MarshalAs(UnmanagedType.U1)]
    public bool OXY_SwNormal;                                   // true: NORMAL  false: ON
    [MarshalAs(UnmanagedType.U1)]
    public bool OXY_annunPASS_OXY_ON;

    // Gear (overhead)
    [MarshalAs(UnmanagedType.U1)]
    public bool GEAR_annunOvhdLEFT;
    [MarshalAs(UnmanagedType.U1)]
    public bool GEAR_annunOvhdNOSE;
    [MarshalAs(UnmanagedType.U1)]
    public bool GEAR_annunOvhdRIGHT;

    // Flight recorder + CVR
    [MarshalAs(UnmanagedType.U1)]
    public bool FLTREC_SwNormal;                                // true: NORMAL  false: TEST
    [MarshalAs(UnmanagedType.U1)]
    public bool FLTREC_annunOFF;
    [MarshalAs(UnmanagedType.U1)]
    public bool CVR_annunTEST;

    // ===== Forward overhead =====

    // Flight Controls
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] FCTL_FltControl_Sw;                           // 0: STBY/RUD  1: OFF  2: ON
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FCTL_Spoiler_Sw;                              // true: ON  false: OFF
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_YawDamper_Sw;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_AltnFlaps_Sw_ARM;                          // true: ARM  false: OFF
    public byte FCTL_AltnFlaps_Control_Sw;                      // 0: UP  1: OFF  2: DOWN
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FCTL_annunFC_LOW_PRESSURE;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunYAW_DAMPER;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunLOW_QUANTITY;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunLOW_PRESSURE;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunLOW_STBY_RUD_ON;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunFEEL_DIFF_PRESS;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunSPEED_TRIM_FAIL;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunMACH_TRIM_FAIL;
    [MarshalAs(UnmanagedType.U1)]
    public bool FCTL_annunAUTO_SLAT_FAIL;

    // Navigation/Displays
    public byte NAVDIS_VHFNavSelector;                          // 0: BOTH ON 1  1: NORMAL  2: BOTH ON 2
    public byte NAVDIS_IRSSelector;
    public byte NAVDIS_FMCSelector;
    public byte NAVDIS_SourceSelector;
    public byte NAVDIS_ControlPaneSelector;
    public uint ADF_StandbyFrequency;                           // freq * 10

    // Fuel
    public float FUEL_FuelTempNeedle;
    [MarshalAs(UnmanagedType.U1)]
    public bool FUEL_CrossFeedSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_PumpFwdSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_PumpAftSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_PumpCtrSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_AuxFwd;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_AuxAft;
    [MarshalAs(UnmanagedType.U1)]
    public bool FUEL_FWDBleed;
    [MarshalAs(UnmanagedType.U1)]
    public bool FUEL_AFTBleed;
    [MarshalAs(UnmanagedType.U1)]
    public bool FUEL_GNDXfr;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] FUEL_annunENG_VALVE_CLOSED;                   // 0/1/2
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] FUEL_annunSPAR_VALVE_CLOSED;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_annunFILTER_BYPASS;
    public byte FUEL_annunXFEED_VALVE_OPEN;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_annunLOWPRESS_Fwd;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_annunLOWPRESS_Aft;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FUEL_annunLOWPRESS_Ctr;
    public float FUEL_QtyCenter;                                // LBS
    public float FUEL_QtyLeft;
    public float FUEL_QtyRight;

    // Electrical
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_annunBAT_DISCHARGE;
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_annunTR_UNIT;
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_annunELEC;
    public byte ELEC_DCMeterSelector;
    public byte ELEC_ACMeterSelector;
    public byte ELEC_BatSelector;                               // 0: OFF  1: BAT  2: ON
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_CabUtilSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_IFEPassSeatSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ELEC_annunDRIVE;
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_annunSTANDBY_POWER_OFF;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ELEC_IDGDisconnectSw;
    public byte ELEC_StandbyPowerSelector;                      // 0: BAT  1: OFF  2: AUTO
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_annunGRD_POWER_AVAILABLE;
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_GrdPwrSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_BusTransSw_AUTO;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ELEC_GenSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ELEC_APUGenSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ELEC_annunTRANSFER_BUS_OFF;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ELEC_annunSOURCE_OFF;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ELEC_annunGEN_BUS_OFF;
    [MarshalAs(UnmanagedType.U1)]
    public bool ELEC_annunAPU_GEN_OFF_BUS;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 13)]
    public byte[] ELEC_MeterDisplayTop;                         // char[13], zero terminated
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 13)]
    public byte[] ELEC_MeterDisplayBottom;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 16)]
    public bool[] ELEC_BusPowered;

    // APU
    public float APU_EGTNeedle;
    [MarshalAs(UnmanagedType.U1)]
    public bool APU_annunMAINT;
    [MarshalAs(UnmanagedType.U1)]
    public bool APU_annunLOW_OIL_PRESSURE;
    [MarshalAs(UnmanagedType.U1)]
    public bool APU_annunFAULT;
    [MarshalAs(UnmanagedType.U1)]
    public bool APU_annunOVERSPEED;

    // Wipers
    public byte OH_WiperLSelector;                              // 0: PARK ... 3: HIGH
    public byte OH_WiperRSelector;

    // Center overhead controls & indicators
    public byte LTS_CircuitBreakerKnob;                         // 0...150
    public byte LTS_OvereadPanelKnob;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_EquipCoolingSupplyNORM;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_EquipCoolingExhaustNORM;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunEquipCoolingSupplyOFF;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunEquipCoolingExhaustOFF;
    [MarshalAs(UnmanagedType.U1)]
    public bool LTS_annunEmerNOT_ARMED;
    public byte LTS_EmerExitSelector;                           // 0: OFF  1: ARMED  2: ON
    public byte COMM_NoSmokingSelector;                         // 0: OFF  1: AUTO   2: ON
    public byte COMM_FastenBeltsSelector;
    [MarshalAs(UnmanagedType.U1)]
    public bool COMM_annunCALL;
    [MarshalAs(UnmanagedType.U1)]
    public bool COMM_annunPA_IN_USE;

    // Anti-ice
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
    public bool[] ICE_annunOVERHEAT;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
    public bool[] ICE_annunON;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
    public bool[] ICE_WindowHeatSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunCAPT_PITOT;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunL_ELEV_PITOT;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunL_ALPHA_VANE;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunL_TEMP_PROBE;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunFO_PITOT;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunR_ELEV_PITOT;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunR_ALPHA_VANE;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_annunAUX_PITOT;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ICE_ProbeHeatSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ICE_annunVALVE_OPEN;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ICE_annunCOWL_ANTI_ICE;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ICE_annunCOWL_VALVE_OPEN;
    [MarshalAs(UnmanagedType.U1)]
    public bool ICE_WingAntiIceSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] ICE_EngAntiIceSw;
    public int ICE_WindowHeatTestSw;                            // signed int: 0: OVHT  1: Neutral  2: PWR TEST

    // Hydraulics
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] HYD_annunLOW_PRESS_eng;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] HYD_annunLOW_PRESS_elec;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] HYD_annunOVERHEAT_elec;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] HYD_PumpSw_eng;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] HYD_PumpSw_elec;

    // Air systems
    public byte AIR_TempSourceSelector;                         // Positions 0..6
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_TrimAirSwitch;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 3)]
    public bool[] AIR_annunZoneTemp;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunDualBleed;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunRamDoorL;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunRamDoorR;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] AIR_RecircFanSwitch;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] AIR_PackSwitch;                               // 0=OFF 1=AUTO 2=HIGH
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] AIR_BleedAirSwitch;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_APUBleedAirSwitch;
    public byte AIR_IsolationValveSwitch;                       // 0=CLOSE  1=AUTO  2=OPEN
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] AIR_annunPackTripOff;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] AIR_annunWingBodyOverheat;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] AIR_annunBleedTripOff;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunAUTO_FAIL;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunOFFSCHED_DESCENT;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunALTN;
    [MarshalAs(UnmanagedType.U1)]
    public bool AIR_annunMANUAL;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public float[] AIR_DuctPress;                               // PSI
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public float[] AIR_DuctPressNeedle;
    public float AIR_CabinAltNeedle;
    public float AIR_CabinDPNeedle;
    public float AIR_CabinVSNeedle;
    public float AIR_CabinValveNeedle;
    public float AIR_TemperatureNeedle;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 6)]
    public byte[] AIR_DisplayFltAlt;                            // char[6], zero terminated
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 6)]
    public byte[] AIR_DisplayLandAlt;

    // Doors
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunFWD_ENTRY;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunFWD_SERVICE;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunAIRSTAIR;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunLEFT_FWD_OVERWING;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunRIGHT_FWD_OVERWING;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunFWD_CARGO;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunEQUIP;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunLEFT_AFT_OVERWING;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunRIGHT_AFT_OVERWING;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunAFT_CARGO;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunAFT_ENTRY;
    [MarshalAs(UnmanagedType.U1)]
    public bool DOOR_annunAFT_SERVICE;

    public uint AIR_FltAltWindow;                               // obsolete - use AIR_DisplayFltAlt
    public uint AIR_LandAltWindow;                              // obsolete - use AIR_DisplayLandAlt
    public uint AIR_OutflowValveSwitch;                         // 0=CLOSE  1=NEUTRAL  2=OPEN
    public uint AIR_PressurizationModeSelector;                 // 0=AUTO  1=ALTN  2=MAN

    // Bottom overhead
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] LTS_LandingLtRetractableSw;                   // 0: RETRACT  1: EXTEND  2: ON
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] LTS_LandingLtFixedSw;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] LTS_RunwayTurnoffSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool LTS_TaxiSw;
    public byte APU_Selector;                                   // 0: OFF  1: ON  2: START
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] ENG_StartSelector;                            // 0: GRD  1: OFF  2: CONT  3: FLT
    public byte ENG_IgnitionSelector;                           // 0: IGN L  1: BOTH  2: IGN R
    [MarshalAs(UnmanagedType.U1)]
    public bool LTS_LogoSw;
    public byte LTS_PositionSw;                                 // 0: STEADY  1: OFF  2: STROBE&STEADY
    [MarshalAs(UnmanagedType.U1)]
    public bool LTS_AntiCollisionSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool LTS_WingSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool LTS_WheelWellSw;

    // ===== Glareshield =====

    // Warnings
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] WARN_annunFIRE_WARN;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] WARN_annunMASTER_CAUTION;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunFLT_CONT;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunIRS;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunFUEL;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunELEC;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunAPU;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunOVHT_DET;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunANTI_ICE;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunHYD;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunDOORS;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunENG;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunOVERHEAD;
    [MarshalAs(UnmanagedType.U1)]
    public bool WARN_annunAIR_COND;

    // EFIS control panels
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] EFIS_MinsSelBARO;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] EFIS_BaroSelHPA;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] EFIS_VORADFSel1;                              // 0: VOR  1: OFF  2: ADF
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] EFIS_VORADFSel2;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] EFIS_ModeSel;                                 // 0: APP  1: VOR  2: MAP  3: PLAN
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] EFIS_RangeSel;                                // 0: 5 ... 7: 640

    // Mode control panel
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public ushort[] MCP_Course;
    public float MCP_IASMach;                                   // Mach if < 10.0
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_IASBlank;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_IASOverspeedFlash;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_IASUnderspeedFlash;
    public ushort MCP_Heading;
    public ushort MCP_Altitude;
    public short MCP_VertSpeed;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_VertSpeedBlank;

    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MCP_FDSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_ATArmSw;
    public byte MCP_BankLimitSel;                               // 0: 10 ... 4: 30
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_DisengageBar;

    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MCP_annunFD;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunATArm;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunN1;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunSPEED;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunVNAV;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunLVL_CHG;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunHDG_SEL;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunLNAV;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunVOR_LOC;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunAPP;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunALT_HOLD;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunVS;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunCMD_A;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunCWS_A;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunCMD_B;
    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_annunCWS_B;

    [MarshalAs(UnmanagedType.U1)]
    public bool MCP_indication_powered;

    // ===== Forward panel =====
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_NoseWheelSteeringSwNORM;                   // false: ALT
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MAIN_annunBELOW_GS;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] MAIN_MainPanelDUSel;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] MAIN_LowerDUSel;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MAIN_annunAP;                                 // Red
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MAIN_annunAP_Amber;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MAIN_annunAT;                                 // Red
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MAIN_annunAT_Amber;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] MAIN_annunFMC;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] MAIN_DisengageTestSelector;                   // 0: 1  1: OFF  2: 2
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunSPEEDBRAKE_ARMED;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunSPEEDBRAKE_DO_NOT_ARM;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunSPEEDBRAKE_EXTENDED;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunSTAB_OUT_OF_TRIM;
    public byte MAIN_LightsSelector;                            // 0: TEST  1: BRT  2: DIM
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_RMISelector1_VOR;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_RMISelector2_VOR;
    public byte MAIN_N1SetSelector;
    public byte MAIN_SpdRefSelector;
    public byte MAIN_FuelFlowSelector;
    public byte MAIN_AutobrakeSelector;                         // 0: RTO  1: OFF ... 5: MAX
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunANTI_SKID_INOP;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunAUTO_BRAKE_DISARM;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunLE_FLAPS_TRANSIT;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunLE_FLAPS_EXT;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public float[] MAIN_TEFlapsNeedle;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 3)]
    public bool[] MAIN_annunGEAR_transit;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 3)]
    public bool[] MAIN_annunGEAR_locked;
    public byte MAIN_GearLever;                                 // 0: UP  1: OFF  2: DOWN
    public float MAIN_BrakePressNeedle;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunCABIN_ALTITUDE;
    [MarshalAs(UnmanagedType.U1)]
    public bool MAIN_annunTAKEOFF_CONFIG;

    // HGS annunciator block (12 fields)
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_AIII;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_NO_AIII;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_FLARE;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_RO;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_RO_CTN;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_RO_ARM;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_TO;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_TO_CTN;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_APCH;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_TO_WARN;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_Bar;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annun_FAIL;

    // ===== Lower forward panel =====
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] LTS_MainPanelKnob;
    public byte LTS_BackgroundKnob;
    public byte LTS_AFDSFloodKnob;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] LTS_OutbdDUBrtKnob;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] LTS_InbdDUBrtKnob;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] LTS_InbdDUMapBrtKnob;
    public byte LTS_UpperDUBrtKnob;
    public byte LTS_LowerDUBrtKnob;
    public byte LTS_LowerDUMapBrtKnob;

    [MarshalAs(UnmanagedType.U1)]
    public bool GPWS_annunINOP;
    [MarshalAs(UnmanagedType.U1)]
    public bool GPWS_FlapInhibitSw_NORM;
    [MarshalAs(UnmanagedType.U1)]
    public bool GPWS_GearInhibitSw_NORM;
    [MarshalAs(UnmanagedType.U1)]
    public bool GPWS_TerrInhibitSw_NORM;

    // ===== Control Stand =====
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] CDU_annunEXEC;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] CDU_annunCALL;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] CDU_annunFAIL;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] CDU_annunMSG;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] CDU_annunOFST;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] CDU_BrtKnob;                                  // 0...127

    public byte COMM_Attend_PressCount;
    public byte COMM_GrdCall_PressCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] COMM_SelectedMic;                             // 0=capt, 1=F/O, 2=observer
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public uint[] COMM_ReceiverSwitches;                        // bit flags per ACP_SEL_RECV_*
    [MarshalAs(UnmanagedType.U1)]
    public bool TRIM_StabTrimMainElecSw_NORMAL;
    [MarshalAs(UnmanagedType.U1)]
    public bool TRIM_StabTrimAutoPilotSw_NORMAL;
    [MarshalAs(UnmanagedType.U1)]
    public bool PED_annunParkingBrake;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] FIRE_OvhtDetSw;                               // 0: A  1: NORMAL  2: B
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FIRE_annunENG_OVERHEAT;
    public byte FIRE_DetTestSw;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] FIRE_HandlePos;                               // 0: In  1: Blocked  2: Out  3: Turned Left  4: Turned right
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 3)]
    public bool[] FIRE_HandleIlluminated;
    [MarshalAs(UnmanagedType.U1)]
    public bool FIRE_annunWHEEL_WELL;
    [MarshalAs(UnmanagedType.U1)]
    public bool FIRE_annunFAULT;
    [MarshalAs(UnmanagedType.U1)]
    public bool FIRE_annunAPU_DET_INOP;
    [MarshalAs(UnmanagedType.U1)]
    public bool FIRE_annunAPU_BOTTLE_DISCHARGE;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] FIRE_annunBOTTLE_DISCHARGE;
    public byte FIRE_ExtinguisherTestSw;                        // 0: 1  1: neutral  2: 2
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 3)]
    public bool[] FIRE_annunExtinguisherTest;                   // Left, Right, APU

    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] CARGO_annunExtTest;                           // Fwd, Aft
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] CARGO_DetSelect;                              // 0: A  1: ORM  2: B
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 2)]
    public bool[] CARGO_ArmedSw;
    [MarshalAs(UnmanagedType.U1)]
    public bool CARGO_annunFWD;
    [MarshalAs(UnmanagedType.U1)]
    public bool CARGO_annunAFT;
    [MarshalAs(UnmanagedType.U1)]
    public bool CARGO_annunDETECTOR_FAULT;
    [MarshalAs(UnmanagedType.U1)]
    public bool CARGO_annunDISCH;

    // HGS pedestal annunciators
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annunRWY;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annunGS;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annunFAULT;
    [MarshalAs(UnmanagedType.U1)]
    public bool HGS_annunCLR;

    // Transponder
    [MarshalAs(UnmanagedType.U1)]
    public bool XPDR_XpndrSelector_2;                           // false: 1  true: 2
    [MarshalAs(UnmanagedType.U1)]
    public bool XPDR_AltSourceSel_2;
    public byte XPDR_ModeSel;                                   // 0: STBY  1: ALT RPTG OFF ... 4: TA/RA
    [MarshalAs(UnmanagedType.U1)]
    public bool XPDR_annunFAIL;

    // Pedestal lights
    public byte LTS_PedFloodKnob;                               // 0...150
    public byte LTS_PedPanelKnob;

    [MarshalAs(UnmanagedType.U1)]
    public bool TRIM_StabTrimSw_NORMAL;
    [MarshalAs(UnmanagedType.U1)]
    public bool PED_annunLOCK_FAIL;
    [MarshalAs(UnmanagedType.U1)]
    public bool PED_annunAUTO_UNLK;
    public byte PED_FltDkDoorSel;                               // 0: UNLKD  1: AUTO pushed in  2: AUTO  3: DENY

    // ===== FMS =====
    public byte FMC_TakeoffFlaps;                               // degrees, 0 if not set
    public byte FMC_V1;                                         // knots, 0 if not set
    public byte FMC_VR;
    public byte FMC_V2;
    public byte FMC_LandingFlaps;
    public byte FMC_LandingVREF;
    public ushort FMC_CruiseAlt;
    public short FMC_LandingAltitude;                           // -32767 if n/a
    public ushort FMC_TransitionAlt;
    public ushort FMC_TransitionLevel;
    [MarshalAs(UnmanagedType.U1)]
    public bool FMC_PerfInputComplete;
    public float FMC_DistanceToTOD;                             // nm
    public float FMC_DistanceToDest;
    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 9)]
    public byte[] FMC_flightNumber;                             // char[9]

    // ===== General and misc =====
    public ushort AircraftModel;                                // 1..21 — see SDK header for variants
    [MarshalAs(UnmanagedType.U1)]
    public bool WeightInKg;                                     // false: LBS  true: KG
    [MarshalAs(UnmanagedType.U1)]
    public bool GPWS_V1CallEnabled;
    [MarshalAs(UnmanagedType.U1)]
    public bool GroundConnAvailable;

    [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 255)]
    public byte[] reserved;
}
