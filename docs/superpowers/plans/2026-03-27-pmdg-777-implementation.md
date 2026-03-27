# PMDG Boeing 777 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full PMDG Boeing 777 accessibility support to MSFS Blind Assist, including all cockpit panels, CDU access, and hotkey integration.

**Architecture:** New `PMDG777DataManager` handles binary Client Data Area communication with the PMDG SDK. `PMDG777Definition` implements the aircraft definition interface with all variables, panels, and event handling. `PMDG777CDUForm` provides CDU access mirroring the Fenix MCDU pattern. Minimal changes to existing code (SimConnectManager routing, MainForm menu).

**Tech Stack:** C# 13, .NET 9, Windows Forms, SimConnect SDK, MobiFlight WASM

**Spec:** `docs/superpowers/specs/2026-03-27-pmdg-777-design.md`

**Reference files:**
- Fenix aircraft definition: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`
- Base class: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs`
- Interface: `MSFSBlindAssist/Aircraft/IAircraftDefinition.cs`
- SimConnect manager: `MSFSBlindAssist/SimConnect/SimConnectManager.cs`
- Variable definitions: `MSFSBlindAssist/SimConnect/SimVarDefinitions.cs`
- Fenix MCDU form: `MSFSBlindAssist/Forms/FenixA320/FenixMCDUForm.cs`
- MobiFlight module: `MSFSBlindAssist/SimConnect/MobiFlightWasmModule.cs`
- PMDG Python reference: `C:\Users\robin\Downloads\simconnect-mcp\src\simconnect_mcp\pmdg.py`
- PMDG event catalog: `C:\Users\robin\Downloads\simconnect-mcp\src\simconnect_mcp\data\pmdg_777.json`

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `SimConnect/PMDG777XDataStruct.cs` | C# struct mirroring 684-byte PMDG binary data |
| `SimConnect/PMDG777DataManager.cs` | Client Data Area registration, polling, change detection, event dispatch |
| `Aircraft/PMDG777Definition.cs` | Aircraft definition: variables, panels, controls, hotkeys, event handling |
| `Forms/PMDG777/PMDG777CDUForm.cs` | CDU dialog form |
| `Forms/PMDG777/PMDG777CDUForm.Designer.cs` | CDU form layout |

### Modified Files
| File | Change |
|------|--------|
| `SimConnect/SimVarDefinitions.cs` | Add `PMDGVar` to `SimVarType` enum |
| `SimConnect/SimConnectManager.cs` | Add PMDG777DataManager property, OnRecvClientData routing, SendPMDGEvent method |
| `MainForm.cs` | Add PMDG 777 to LoadAircraftFromCode, HandleUIVariableSet routing for PMDGVar, CDU hotkey routing |
| `MainForm.Designer.cs` | Add PMDG 777 menu item |

---

## Task 1: Add PMDGVar to SimVarType Enum

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimVarDefinitions.cs:3-9`

- [ ] **Step 1: Add PMDGVar enum value**

In `SimConnect/SimVarDefinitions.cs`, add `PMDGVar` to the `SimVarType` enum:

```csharp
public enum SimVarType
{
    LVar,      // Local variable (L:varname)
    Event,     // SimConnect Event
    SimVar,    // Standard SimVar
    HVar,      // H-variable (requires MobiFlight WASM)
    PMDGVar    // PMDG SDK variable (read via Client Data Area)
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/SimConnect/SimVarDefinitions.cs
git commit -m "feat(pmdg777): add PMDGVar to SimVarType enum"
```

---

## Task 2: Create PMDG 777X Data Struct

**Files:**
- Create: `MSFSBlindAssist/SimConnect/PMDG777XDataStruct.cs`

This struct mirrors the 684-byte `PMDG_777X_Data` binary structure from the PMDG SDK. It uses default MSVC alignment (no Pack attribute). The field order MUST match the PMDG SDK exactly.

- [ ] **Step 1: Create the data struct file**

Create `MSFSBlindAssist/SimConnect/PMDG777XDataStruct.cs` with the complete struct. Reference the Python ctypes definition in `simconnect-mcp/src/simconnect_mcp/pmdg.py` lines 238-635 for the exact field order.

The struct must use `[StructLayout(LayoutKind.Sequential)]` without Pack to match MSVC default alignment. Bool fields in PMDG are 1 byte each. Bool arrays use `[MarshalAs(UnmanagedType.ByValArray)]`. Byte arrays similarly. The `FMC_flightNumber` field is a 9-byte char array.

Key type mappings from PMDG SDK:
- `c_bool` -> `[MarshalAs(UnmanagedType.U1)] public bool`
- `c_bool * N` -> `[MarshalAs(UnmanagedType.ByValArray, SizeConst = N)] public bool[]`
- `c_ubyte` -> `public byte`
- `c_ubyte * N` -> `[MarshalAs(UnmanagedType.ByValArray, SizeConst = N)] public byte[]`
- `c_short` -> `public short`
- `c_ushort` -> `public ushort`
- `c_int` -> `public int`
- `c_float` -> `public float`
- `c_float * N` -> `[MarshalAs(UnmanagedType.ByValArray, SizeConst = N)] public float[]`
- `c_char * N` -> `[MarshalAs(UnmanagedType.ByValArray, SizeConst = N)] public byte[]`

Complete field list (176 fields, in exact PMDG SDK order):

```
// Overhead Maintenance Panel
ICE_WindowHeatBackUp_Sw_OFF         bool[2]
ELEC_StandbyPowerSw                 byte
FCTL_WingHydValve_Sw_SHUT_OFF       bool[3]
FCTL_TailHydValve_Sw_SHUT_OFF       bool[3]
FCTL_annunTailHydVALVE_CLOSED       bool[3]
FCTL_annunWingHydVALVE_CLOSED       bool[3]
FCTL_PrimFltComputersSw_AUTO        bool
FCTL_annunPrimFltComputersDISC      bool
APU_Power_Sw_TEST                   bool
ENG_EECPower_Sw_TEST                bool[2]
ELEC_TowingPower_Sw_BATT            bool
ELEC_annunTowingPowerON_BATT        bool
AIR_CargoTemp_Selector              byte[2]
AIR_CargoTemp_MainDeckFwd_Sel       byte
AIR_CargoTemp_MainDeckAft_Sel       byte
AIR_CargoTemp_LowerFwd_Sel          byte
AIR_CargoTemp_LowerAft_Sel          byte

// Overhead Panel
ADIRU_Sw_On                         bool
ADIRU_annunOFF                      bool
ADIRU_annunON_BAT                   bool
FCTL_ThrustAsymComp_Sw_AUTO         bool
FCTL_annunThrustAsymCompOFF         bool
ELEC_CabUtilSw                      bool
ELEC_annunCabUtilOFF                bool
ELEC_IFEPassSeatsSw                 bool
ELEC_annunIFEPassSeatsOFF           bool
ELEC_Battery_Sw_ON                  bool
ELEC_annunBattery_OFF               bool
ELEC_annunAPU_GEN_OFF               bool
ELEC_APUGen_Sw_ON                   bool
ELEC_APU_Selector                   byte
ELEC_annunAPU_FAULT                 bool
ELEC_BusTie_Sw_AUTO                 bool[2]
ELEC_annunBusTieISLN                bool[2]
ELEC_ExtPwrSw                       bool[2]
ELEC_annunExtPowr_ON                bool[2]
ELEC_annunExtPowr_AVAIL             bool[2]
ELEC_Gen_Sw_ON                      bool[2]
ELEC_annunGenOFF                    bool[2]
ELEC_BackupGen_Sw_ON                bool[2]
ELEC_annunBackupGenOFF              bool[2]
ELEC_IDGDiscSw                      bool[2]
ELEC_annunIDGDiscDRIVE              bool[2]
WIPERS_Selector                     byte[2]
LTS_EmerLightsSelector              byte
COMM_ServiceInterphoneSw            bool
OXY_PassOxygen_Sw_On                bool
OXY_annunPassOxygenON               bool
ICE_WindowHeat_Sw_ON                bool[4]
ICE_annunWindowHeatINOP             bool[4]
HYD_RamAirTurbineSw                 bool
HYD_annunRamAirTurbinePRESS         bool
HYD_annunRamAirTurbineUNLKD         bool
HYD_PrimaryEngPump_Sw_ON            bool[2]
HYD_PrimaryElecPump_Sw_ON           bool[2]
HYD_DemandElecPump_Selector         byte[2]
HYD_DemandAirPump_Selector          byte[2]
HYD_annunPrimaryEngPumpFAULT        bool[2]
HYD_annunPrimaryElecPumpFAULT       bool[2]
HYD_annunDemandElecPumpFAULT        bool[2]
HYD_annunDemandAirPumpFAULT         bool[2]
SIGNS_NoSmokingSelector             byte
SIGNS_SeatBeltsSelector             byte
LTS_DomeLightKnob                   byte
LTS_CircuitBreakerKnob              byte
LTS_OvereadPanelKnob                byte
LTS_GlareshieldPNLlKnob             byte
LTS_GlareshieldFLOODKnob            byte
LTS_Storm_Sw_ON                     bool
LTS_MasterBright_Sw_ON              bool
LTS_MasterBrigntKnob                byte
LTS_IndLightsTestSw                 byte
LTS_LandingLights_Sw_ON             bool[3]
LTS_Beacon_Sw_ON                    bool
LTS_NAV_Sw_ON                       bool
LTS_Logo_Sw_ON                      bool
LTS_Wing_Sw_ON                      bool
LTS_RunwayTurnoff_Sw_ON             bool[2]
LTS_Taxi_Sw_ON                      bool
LTS_Strobe_Sw_ON                    bool
FIRE_CargoFire_Sw_Arm               bool[2]
FIRE_annunCargoFire                 bool[2]
FIRE_CargoFireDisch_Sw              bool
FIRE_annunCargoDISCH                bool
FIRE_FireOvhtTest_Sw                bool
FIRE_APUHandle                      byte
FIRE_APUHandleUnlock_Sw             bool
FIRE_annunAPU_BTL_DISCH             bool
FIRE_EngineHandleIlluminated        bool[2]
FIRE_APUHandleIlluminated           bool
FIRE_EngineHandleIsUnlocked         bool[2]
FIRE_APUHandleIsUnlocked            bool
FIRE_annunMainDeckCargoFire         bool
FIRE_annunCargoDEPR                 bool
ENG_EECMode_Sw_NORM                 bool[2]
ENG_Start_Selector                  byte[2]
ENG_Autostart_Sw_ON                 bool
ENG_annunALTN                       bool[2]
ENG_annunAutostartOFF               bool
FUEL_CrossFeedFwd_Sw                bool
FUEL_CrossFeedAft_Sw                bool
FUEL_PumpFwd_Sw                     bool[2]
FUEL_PumpAft_Sw                     bool[2]
FUEL_PumpCtr_Sw                     bool[2]
FUEL_JettisonNozle_Sw               bool[2]
FUEL_JettisonArm_Sw                 bool
FUEL_FuelToRemain_Sw_Pulled         bool
FUEL_FuelToRemain_Selector          byte
FUEL_annunFwdXFEED_VALVE            bool
FUEL_annunAftXFEED_VALVE            bool
FUEL_annunLOWPRESS_Fwd              bool[2]
FUEL_annunLOWPRESS_Aft              bool[2]
FUEL_annunLOWPRESS_Ctr              bool[2]
FUEL_annunJettisonNozleVALVE        bool[2]
FUEL_annunArmFAULT                  bool
ICE_WingAntiIceSw                   byte
ICE_EngAntiIceSw                    byte[2]
AIR_Pack_Sw_AUTO                    bool[2]
AIR_TrimAir_Sw_On                   bool[2]
AIR_RecircFan_Sw_On                 bool[2]
AIR_TempSelector                    byte[2]
AIR_AirCondReset_Sw_Pushed          bool
AIR_EquipCooling_Sw_AUTO            bool
AIR_Gasper_Sw_On                    bool
AIR_annunPackOFF                    bool[2]
AIR_annunTrimAirFAULT               bool[2]
AIR_annunEquipCoolingOVRD           bool
AIR_AltnVentSw_ON                   bool
AIR_annunAltnVentFAULT              bool
AIR_MainDeckFlowSw_NORM             bool
AIR_EngBleedAir_Sw_AUTO             bool[2]
AIR_APUBleedAir_Sw_AUTO             bool
AIR_IsolationValve_Sw               bool[2]
AIR_CtrIsolationValve_Sw            bool
AIR_annunEngBleedAirOFF             bool[2]
AIR_annunAPUBleedAirOFF             bool
AIR_annunIsolationValveCLOSED       bool[2]
AIR_annunCtrIsolationValveCLOSED    bool
AIR_OutflowValve_Sw_AUTO            bool[2]
AIR_OutflowValveManual_Selector     byte[2]
AIR_LdgAlt_Sw_Pulled                bool
AIR_LdgAlt_Selector                 byte
AIR_annunOutflowValve_MAN           bool[2]

// Forward Panel
GEAR_Lever                          byte
GEAR_LockOvrd_Sw                    bool
GEAR_AltnGear_Sw_DOWN               bool
GPWS_FlapInhibitSw_OVRD             bool
GPWS_GearInhibitSw_OVRD             bool
GPWS_TerrInhibitSw_OVRD             bool
GPWS_RunwayOvrdSw_OVRD              bool
GPWS_GSInhibit_Sw                   bool
GPWS_annunGND_PROX_top              bool
GPWS_annunGND_PROX_bottom           bool
BRAKES_AutobrakeSelector            byte
ISFD_Baro_Sw_Pushed                 bool
ISFD_RST_Sw_Pushed                  bool
ISFD_Minus_Sw_Pushed                bool
ISFD_Plus_Sw_Pushed                 bool
ISFD_APP_Sw_Pushed                  bool
ISFD_HP_IN_Sw_Pushed                bool
ISP_Nav_L_Sw_CDU                    bool
ISP_DsplCtrl_L_Sw_Altn             bool
ISP_AirDataAtt_L_Sw_Altn           bool
DSP_InbdDspl_L_Selector            byte
EFIS_HdgRef_Sw_Norm                bool
EFIS_annunHdgRefTRUE               bool
BRAKES_BrakePressNeedle             int
BRAKES_annunBRAKE_SOURCE            bool
ISP_Nav_R_Sw_CDU                    bool
ISP_DsplCtrl_R_Sw_Altn             bool
ISP_AirDataAtt_R_Sw_Altn           bool
ISP_FMC_Selector                    byte
DSP_InbdDspl_R_Selector            byte
AIR_ShoulderHeaterKnob              byte[2]
AIR_FootHeaterSelector              byte[2]
LTS_LeftFwdPanelPNLKnob            byte
LTS_LeftFwdPanelFLOODKnob          byte
LTS_LeftOutbdDsplBRIGHTNESSKnob    byte
LTS_LeftInbdDsplBRIGHTNESSKnob     byte
LTS_RightFwdPanelPNLKnob           byte
LTS_RightFwdPanelFLOODKnob         byte
LTS_RightInbdDsplBRIGHTNESSKnob    byte
LTS_RightOutbdDsplBRIGHTNESSKnob   byte
CHR_Chr_Sw_Pushed                   bool[2]
CHR_TimeDate_Sw_Pushed              bool[2]
CHR_TimeDate_Selector               byte[2]
CHR_Set_Selector                    byte[2]
CHR_ET_Selector                     byte[2]

// Glareshield - EFIS
EFIS_MinsSelBARO                    bool[2]
EFIS_BaroSelHPA                     bool[2]
EFIS_VORADFSel1                     byte[2]
EFIS_VORADFSel2                     byte[2]
EFIS_ModeSel                        byte[2]
EFIS_RangeSel                       byte[2]
EFIS_MinsKnob                       byte[2]
EFIS_BaroKnob                       byte[2]
EFIS_MinsRST_Sw_Pushed              bool[2]
EFIS_BaroSTD_Sw_Pushed              bool[2]
EFIS_ModeCTR_Sw_Pushed              bool[2]
EFIS_RangeTFC_Sw_Pushed             bool[2]
EFIS_WXR_Sw_Pushed                  bool[2]
EFIS_STA_Sw_Pushed                  bool[2]
EFIS_WPT_Sw_Pushed                  bool[2]
EFIS_ARPT_Sw_Pushed                 bool[2]
EFIS_DATA_Sw_Pushed                 bool[2]
EFIS_POS_Sw_Pushed                  bool[2]
EFIS_TERR_Sw_Pushed                 bool[2]

// Glareshield - MCP
MCP_IASMach                         float
MCP_IASBlank                        bool
MCP_Heading                         ushort
MCP_Altitude                        ushort
MCP_VertSpeed                       short
MCP_FPA                             float
MCP_VertSpeedBlank                  bool
MCP_FD_Sw_On                        bool[2]
MCP_ATArm_Sw_On                     bool[2]
MCP_BankLimitSel                    byte
MCP_AltIncrSel                      bool
MCP_DisengageBar                    bool
MCP_Speed_Dial                      byte
MCP_Heading_Dial                    byte
MCP_Altitude_Dial                   byte
MCP_VS_Wheel                        byte
MCP_HDGDial_Mode                    byte
MCP_VSDial_Mode                     byte
MCP_AP_Sw_Pushed                    bool[2]
MCP_CLB_CON_Sw_Pushed               bool
MCP_AT_Sw_Pushed                    bool
MCP_LNAV_Sw_Pushed                  bool
MCP_VNAV_Sw_Pushed                  bool
MCP_FLCH_Sw_Pushed                  bool
MCP_HDG_HOLD_Sw_Pushed              bool
MCP_VS_FPA_Sw_Pushed                bool
MCP_ALT_HOLD_Sw_Pushed              bool
MCP_LOC_Sw_Pushed                   bool
MCP_APP_Sw_Pushed                   bool
MCP_Speeed_Sw_Pushed                bool
MCP_Heading_Sw_Pushed               bool
MCP_Altitude_Sw_Pushed              bool
MCP_IAS_MACH_Toggle_Sw_Pushed       bool
MCP_HDG_TRK_Toggle_Sw_Pushed        bool
MCP_VS_FPA_Toggle_Sw_Pushed         bool
MCP_annunAP                         bool[2]
MCP_annunAT                         bool
MCP_annunLNAV                       bool
MCP_annunVNAV                       bool
MCP_annunFLCH                       bool
MCP_annunHDG_HOLD                   bool
MCP_annunVS_FPA                     bool
MCP_annunALT_HOLD                   bool
MCP_annunLOC                        bool
MCP_annunAPP                        bool

// Display Select Panel
DSP_L_INBD_Sw                       bool
DSP_R_INBD_Sw                       bool
DSP_LWR_CTR_Sw                      bool
DSP_ENG_Sw                          bool
DSP_STAT_Sw                         bool
DSP_ELEC_Sw                         bool
DSP_HYD_Sw                          bool
DSP_FUEL_Sw                         bool
DSP_AIR_Sw                          bool
DSP_DOOR_Sw                         bool
DSP_GEAR_Sw                         bool
DSP_FCTL_Sw                         bool
DSP_CAM_Sw                          bool
DSP_CHKL_Sw                         bool
DSP_COMM_Sw                         bool
DSP_NAV_Sw                          bool
DSP_CANC_RCL_Sw                     bool
DSP_annunL_INBD                     bool
DSP_annunR_INBD                     bool
DSP_annunLWR_CTR                    bool

// Warning/Caution
WARN_Reset_Sw_Pushed                bool[2]
WARN_annunMASTER_WARNING            bool[2]
WARN_annunMASTER_CAUTION            bool[2]

// Forward Aisle Stand Panel
ISP_DsplCtrl_C_Sw_Altn             bool
LTS_UpperDsplBRIGHTNESSKnob        byte
LTS_LowerDsplBRIGHTNESSKnob        byte
EICAS_EventRcd_Sw_Pushed           bool
CDU_annunEXEC                       bool[3]
CDU_annunDSPY                       bool[3]
CDU_annunFAIL                       bool[3]
CDU_annunMSG                        bool[3]
CDU_annunOFST                       bool[3]
CDU_BrtKnob                         byte[3]

// Control Stand
FCTL_AltnFlaps_Sw_ARM               bool
FCTL_AltnFlaps_Control_Sw           byte
FCTL_StabCutOutSw_C_NORMAL          bool
FCTL_StabCutOutSw_R_NORMAL          bool
FCTL_AltnPitch_Lever                byte
FCTL_Speedbrake_Lever               byte
FCTL_Flaps_Lever                    byte
ENG_FuelControl_Sw_RUN              bool[2]
BRAKES_ParkingBrakeLeverOn          bool

// Aft Aisle Stand Panel
COMM_SelectedMic                    byte[3]
COMM_ReceiverSwitches               ushort[3]
COMM_OBSAudio_Selector              byte
COMM_SelectedRadio                  byte[3]
COMM_RadioTransfer_Sw_Pushed        bool[3]
COMM_RadioPanelOff                  bool[3]
COMM_annunAM                        bool[3]
XPDR_XpndrSelector_R               bool
XPDR_AltSourceSel_ALTN             bool
XPDR_ModeSel                        byte
XPDR_Ident_Sw_Pushed               bool
FIRE_EngineHandle                   byte[2]
FIRE_EngineHandleUnlock_Sw          bool[2]
FIRE_annunENG_BTL_DISCH             bool[2]
FCTL_AileronTrim_Switches           byte
FCTL_RudderTrim_Knob                byte
FCTL_RudderTrimCancel_Sw_Pushed     bool
EVAC_Command_Sw_ON                  bool
EVAC_PressToTest_Sw_Pressed         bool
EVAC_HornSutOff_Sw_Pulled           bool
EVAC_LightIlluminated               bool
LTS_AisleStandPNLKnob              byte
LTS_AisleStandFLOODKnob            byte
LTS_FloorLightsSw                   byte

// Door State
DOOR_state                          byte[16]
DOOR_CockpitDoorOpen                bool

// Additional Variables
ENG_StartValve                      bool[2]
AIR_DuctPress                       float[2]
FUEL_QtyCenter                      float
FUEL_QtyLeft                        float
FUEL_QtyRight                       float
FUEL_QtyAux                         float
IRS_aligned                         bool
EFIS_BaroMinimumsSet                bool[2]
EFIS_BaroMinimums                   int[2]
EFIS_RadioMinimumsSet               bool[2]
EFIS_RadioMinimums                  int[2]
EFIS_Display                        byte[6]
AircraftModel                       byte
WeightInKg                          bool
GPWS_V1CallEnabled                  bool
GroundConnAvailable                 bool
FMC_TakeoffFlaps                    byte
FMC_V1                              byte
FMC_VR                              byte
FMC_V2                              byte
FMC_ThrustRedAlt                    ushort
FMC_AccelerationAlt                 ushort
FMC_EOAccelerationAlt               ushort
FMC_LandingFlaps                    byte
FMC_LandingVREF                     byte
FMC_CruiseAlt                       ushort
FMC_LandingAltitude                 short
FMC_TransitionAlt                   ushort
FMC_TransitionLevel                 ushort
FMC_PerfInputComplete               bool
FMC_DistanceToTOD                   float
FMC_DistanceToDest                  float
FMC_flightNumber                    byte[9]
WheelChocksSet                      bool
APURunning                          bool
FMC_ThrustLimitMode                 byte
ECL_ChecklistComplete               bool[10]
reserved                            byte[84]
```

Also add a CDU cell struct and CDU screen struct:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PMDG777CDUCell
{
    public byte Symbol;
    public byte Color;
    public byte Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PMDG777CDUScreen
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24 * 14)]
    public PMDG777CDUCell[] Cells; // 24 columns x 14 rows
    [MarshalAs(UnmanagedType.U1)]
    public bool Powered;
}
```

And a control struct:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct PMDG777Control
{
    public uint EventId;
    public uint Parameter;
}
```

- [ ] **Step 2: Build to verify struct compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/SimConnect/PMDG777XDataStruct.cs
git commit -m "feat(pmdg777): add PMDG 777X binary data structs"
```

---

## Task 3: Create PMDG777DataManager

**Files:**
- Create: `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs`

This is the core data layer. It manages Client Data Area registration, data polling, change detection, and event dispatch.

- [ ] **Step 1: Create the data manager class**

Create `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs` with the following structure:

```csharp
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;

namespace MSFSBlindAssist.SimConnect;

public class PMDG777DataManager : IDisposable
{
    // PMDG Client Data Area IDs
    private const uint PMDG_777X_DATA_ID = 0x504D4447;
    private const uint PMDG_777X_DATA_DEFINITION = 0x504D4448;
    private const uint PMDG_777X_CONTROL_ID = 0x504D4449;
    private const uint PMDG_777X_CONTROL_DEFINITION = 0x504D444A;
    private const uint PMDG_777X_CDU_0_ID = 0x4E477835;
    private const uint PMDG_777X_CDU_0_DEFINITION = 0x4E477838;
    private const uint PMDG_777X_CDU_1_ID = 0x4E477836;
    private const uint PMDG_777X_CDU_1_DEFINITION = 0x4E477839;
    private const uint PMDG_777X_CDU_2_ID = 0x4E477837;
    private const uint PMDG_777X_CDU_2_DEFINITION = 0x4E47783A;

    // PMDG Event constants
    private const uint THIRD_PARTY_EVENT_ID_MIN = 0x00011000; // 69632
    private const int DIRECT_SET_OFFSET_THRESHOLD = 14500;

    // Request IDs for data polling
    private const uint DATA_REQUEST_ID = 50000;
    private const uint CDU_0_REQUEST_ID = 50001;
    private const uint CDU_1_REQUEST_ID = 50002;
    private const uint CDU_2_REQUEST_ID = 50003;

    private Microsoft.FlightSimulator.SimConnect.SimConnect? simConnect;
    private MobiFlightWasmModule? mobiFlightWasm;
    private PMDG777XData previousData;
    private PMDG777XData currentData;
    private bool isRegistered;
    private System.Windows.Forms.Timer? pollTimer;
    private bool disposed;

    // Event raised when a PMDG variable changes
    public event EventHandler<PMDGVarUpdateEventArgs>? VariableChanged;

    public bool IsRegistered => isRegistered;

    public PMDG777XData CurrentData => currentData;

    public void Initialize(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect, MobiFlightWasmModule mobiFlightWasm)
    {
        this.simConnect = simConnect;
        this.mobiFlightWasm = mobiFlightWasm;
        RegisterClientDataAreas();
        StartPolling();
    }

    private void RegisterClientDataAreas()
    {
        if (simConnect == null) return;

        try
        {
            // Map data area names to IDs
            simConnect.MapClientDataNameToID("PMDG_777X_Data", (SIMCONNECT_CLIENT_DATA_ID)PMDG_777X_DATA_ID);
            simConnect.MapClientDataNameToID("PMDG_777X_Control", (SIMCONNECT_CLIENT_DATA_ID)PMDG_777X_CONTROL_ID);
            simConnect.MapClientDataNameToID("PMDG_777X_CDU_0", (SIMCONNECT_CLIENT_DATA_ID)PMDG_777X_CDU_0_ID);
            simConnect.MapClientDataNameToID("PMDG_777X_CDU_1", (SIMCONNECT_CLIENT_DATA_ID)PMDG_777X_CDU_1_ID);
            simConnect.MapClientDataNameToID("PMDG_777X_CDU_2", (SIMCONNECT_CLIENT_DATA_ID)PMDG_777X_CDU_2_ID);

            // Define data areas with struct sizes
            int dataSize = Marshal.SizeOf(typeof(PMDG777XData));
            simConnect.AddToClientDataDefinition(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_DATA_DEFINITION,
                0, (uint)dataSize, 0, 0);

            simConnect.AddToClientDataDefinition(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CONTROL_DEFINITION,
                0, (uint)Marshal.SizeOf(typeof(PMDG777Control)), 0, 0);

            int cduSize = Marshal.SizeOf(typeof(PMDG777CDUScreen));
            simConnect.AddToClientDataDefinition(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CDU_0_DEFINITION,
                0, (uint)cduSize, 0, 0);
            simConnect.AddToClientDataDefinition(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CDU_1_DEFINITION,
                0, (uint)cduSize, 0, 0);
            simConnect.AddToClientDataDefinition(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CDU_2_DEFINITION,
                0, (uint)cduSize, 0, 0);

            // Register structs
            simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777XData>(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_DATA_DEFINITION);
            simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777Control>(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CONTROL_DEFINITION);
            simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777CDUScreen>(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CDU_0_DEFINITION);
            simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777CDUScreen>(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CDU_1_DEFINITION);
            simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777CDUScreen>(
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CDU_2_DEFINITION);

            isRegistered = true;
            System.Diagnostics.Debug.WriteLine("[PMDG777] Client data areas registered successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PMDG777] Failed to register client data areas: {ex.Message}");
        }
    }

    private void StartPolling()
    {
        pollTimer = new System.Windows.Forms.Timer();
        pollTimer.Interval = 1000; // 1 second
        pollTimer.Tick += (s, e) => RequestData();
        pollTimer.Start();
    }

    public void RequestData()
    {
        if (!isRegistered || simConnect == null) return;
        try
        {
            simConnect.RequestClientData(
                (SIMCONNECT_CLIENT_DATA_ID)PMDG_777X_DATA_ID,
                (SIMCONNECT_DATA_REQUEST_ID)DATA_REQUEST_ID,
                (SIMCONNECT_DEFINE_ID)PMDG_777X_DATA_DEFINITION,
                SIMCONNECT_CLIENT_DATA_PERIOD.ONCE,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PMDG777] RequestData failed: {ex.Message}");
        }
    }

    public PMDG777CDUScreen? RequestCDUScreen(int cdu)
    {
        // CDU screen reading is handled via ProcessClientData callback
        // This triggers a request; the result comes back asynchronously
        if (!isRegistered || simConnect == null) return null;
        try
        {
            uint requestId = cdu switch
            {
                0 => CDU_0_REQUEST_ID,
                1 => CDU_1_REQUEST_ID,
                2 => CDU_2_REQUEST_ID,
                _ => CDU_0_REQUEST_ID
            };
            uint dataId = cdu switch
            {
                0 => PMDG_777X_CDU_0_ID,
                1 => PMDG_777X_CDU_1_ID,
                2 => PMDG_777X_CDU_2_ID,
                _ => PMDG_777X_CDU_0_ID
            };
            uint defId = cdu switch
            {
                0 => PMDG_777X_CDU_0_DEFINITION,
                1 => PMDG_777X_CDU_1_DEFINITION,
                2 => PMDG_777X_CDU_2_DEFINITION,
                _ => PMDG_777X_CDU_0_DEFINITION
            };
            simConnect.RequestClientData(
                (SIMCONNECT_CLIENT_DATA_ID)dataId,
                (SIMCONNECT_DATA_REQUEST_ID)requestId,
                (SIMCONNECT_DEFINE_ID)defId,
                SIMCONNECT_CLIENT_DATA_PERIOD.ONCE,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PMDG777] RequestCDUScreen({cdu}) failed: {ex.Message}");
        }
        return _lastCDUScreen[cdu];
    }

    private PMDG777CDUScreen?[] _lastCDUScreen = new PMDG777CDUScreen?[3];

    /// <summary>
    /// Process client data received from SimConnect. Called by SimConnectManager.
    /// </summary>
    public void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data)
    {
        uint requestId = data.dwRequestID;

        if (requestId == DATA_REQUEST_ID)
        {
            try
            {
                previousData = currentData;
                currentData = (PMDG777XData)data.dwData[0];
                DetectChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PMDG777] ProcessClientData failed: {ex.Message}");
            }
        }
        else if (requestId >= CDU_0_REQUEST_ID && requestId <= CDU_2_REQUEST_ID)
        {
            try
            {
                int cduIndex = (int)(requestId - CDU_0_REQUEST_ID);
                _lastCDUScreen[cduIndex] = (PMDG777CDUScreen)data.dwData[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PMDG777] CDU ProcessClientData failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Compare current data with previous data and raise events for changed fields.
    /// Uses reflection on the struct to compare all fields.
    /// </summary>
    private void DetectChanges()
    {
        var fields = typeof(PMDG777XData).GetFields();
        foreach (var field in fields)
        {
            if (field.Name == "reserved") continue;

            var prevValue = field.GetValue(previousData);
            var currValue = field.GetValue(currentData);

            if (field.FieldType.IsArray)
            {
                var prevArr = prevValue as Array;
                var currArr = currValue as Array;
                if (prevArr == null || currArr == null) continue;

                for (int i = 0; i < currArr.Length; i++)
                {
                    var prev = prevArr.GetValue(i);
                    var curr = currArr.GetValue(i);
                    if (!Equals(prev, curr))
                    {
                        string varName = $"{field.Name}_{i}";
                        double numericValue = Convert.ToDouble(curr);
                        VariableChanged?.Invoke(this, new PMDGVarUpdateEventArgs(varName, numericValue));
                    }
                }
            }
            else
            {
                if (!Equals(prevValue, currValue))
                {
                    double numericValue = Convert.ToDouble(currValue);
                    VariableChanged?.Invoke(this, new PMDGVarUpdateEventArgs(field.Name, numericValue));
                }
            }
        }
    }

    /// <summary>
    /// Read a field value from the current data snapshot by field name.
    /// For array fields, append _N (e.g., "ELEC_Gen_Sw_ON_0" for index 0).
    /// </summary>
    public double GetFieldValue(string fieldName)
    {
        // Check for array index suffix
        int lastUnderscore = fieldName.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(fieldName[(lastUnderscore + 1)..], out int index))
        {
            string baseName = fieldName[..lastUnderscore];
            var field = typeof(PMDG777XData).GetField(baseName);
            if (field != null && field.FieldType.IsArray)
            {
                var arr = field.GetValue(currentData) as Array;
                if (arr != null && index < arr.Length)
                    return Convert.ToDouble(arr.GetValue(index));
            }
        }

        // Try as simple field
        var simpleField = typeof(PMDG777XData).GetField(fieldName);
        if (simpleField != null)
            return Convert.ToDouble(simpleField.GetValue(currentData));

        return 0;
    }

    /// <summary>
    /// Send a PMDG event. Automatically routes to ROTOR_BRAKE or Control CDA based on offset.
    /// </summary>
    public void SendEvent(string eventName, uint eventId, int? parameter = null)
    {
        uint offset = eventId - THIRD_PARTY_EVENT_ID_MIN;

        if (offset >= DIRECT_SET_OFFSET_THRESHOLD)
        {
            // Direct-set via Control data area
            SendControlDataEvent(eventId, (uint)(parameter ?? 0));
        }
        else
        {
            // Standard event via ROTOR_BRAKE RPN
            SendRotorBrakeEvent(offset, parameter);
        }
    }

    private void SendControlDataEvent(uint eventId, uint parameter)
    {
        if (simConnect == null) return;
        try
        {
            var control = new PMDG777Control { EventId = eventId, Parameter = parameter };
            simConnect.SetClientData(
                (SIMCONNECT_CLIENT_DATA_ID)PMDG_777X_CONTROL_ID,
                (SIMCONNECT_DEFINE_ID)PMDG_777X_CONTROL_DEFINITION,
                SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT,
                0, control);
            System.Diagnostics.Debug.WriteLine($"[PMDG777] Sent control event {eventId} with param {parameter}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PMDG777] SendControlDataEvent failed: {ex.Message}");
        }
    }

    private void SendRotorBrakeEvent(uint offset, int? parameter)
    {
        if (mobiFlightWasm == null) return;

        uint rotorParam = offset * 100 + 1;
        string rpn = parameter.HasValue
            ? $"{rotorParam} {parameter.Value} (>K:ROTOR_BRAKE)"
            : $"{rotorParam} (>K:ROTOR_BRAKE)";

        string command = $"MF.SimVars.Set.{rpn}";
        mobiFlightWasm.SendMFCommand(command);
        System.Diagnostics.Debug.WriteLine($"[PMDG777] Sent ROTOR_BRAKE: {rpn}");
    }

    /// <summary>
    /// Send a guarded switch toggle: open guard, toggle switch, close guard.
    /// </summary>
    public async Task SendGuardedToggle(string guardEventName, uint guardEventId,
                                         string switchEventName, uint switchEventId)
    {
        SendEvent(guardEventName, guardEventId);    // Open guard
        await Task.Delay(150);
        SendEvent(switchEventName, switchEventId);  // Toggle switch
        await Task.Delay(150);
        SendEvent(guardEventName, guardEventId);    // Close guard
    }

    /// <summary>
    /// Parse CDU screen data into text rows.
    /// </summary>
    public string[]? GetCDURows(int cdu)
    {
        var screen = _lastCDUScreen[cdu];
        if (screen == null || !screen.Value.Powered) return null;

        var rows = new string[14];
        for (int row = 0; row < 14; row++)
        {
            var chars = new char[24];
            for (int col = 0; col < 24; col++)
            {
                int idx = row * 24 + col;
                byte symbol = screen.Value.Cells[idx].Symbol;
                chars[col] = symbol switch
                {
                    0xA1 => '<', // left arrow
                    0xA2 => '>', // right arrow
                    >= 0x20 and <= 0x7E => (char)symbol,
                    _ => ' '
                };
            }
            rows[row] = new string(chars).TrimEnd();
        }
        return rows;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pollTimer?.Stop();
        pollTimer?.Dispose();
    }
}

public class PMDGVarUpdateEventArgs : EventArgs
{
    public string FieldName { get; }
    public double Value { get; }

    public PMDGVarUpdateEventArgs(string fieldName, double value)
    {
        FieldName = fieldName;
        Value = value;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds (may have warnings about unused fields, that's OK)

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/SimConnect/PMDG777DataManager.cs
git commit -m "feat(pmdg777): add PMDG 777 data manager for Client Data Area communication"
```

---

## Task 4: Integrate PMDG777DataManager into SimConnectManager

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs`

- [ ] **Step 1: Add PMDG777DataManager property and initialization**

Add a public property and initialization method to SimConnectManager:

```csharp
// Add field near other manager fields
private PMDG777DataManager? pmdg777DataManager;
public PMDG777DataManager? PMDG777DataManager => pmdg777DataManager;

// Add public method to initialize PMDG support
public void InitializePMDG777()
{
    if (simConnect == null || !IsConnected) return;
    pmdg777DataManager = new PMDG777DataManager();
    pmdg777DataManager.Initialize(simConnect, mobiFlightWasm);
}

// Add public method to clean up PMDG support
public void DisposePMDG777()
{
    pmdg777DataManager?.Dispose();
    pmdg777DataManager = null;
}
```

- [ ] **Step 2: Route client data to PMDG manager**

Modify the existing `SimConnect_OnRecvClientData` handler to also route to the PMDG manager:

```csharp
private void SimConnect_OnRecvClientData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_CLIENT_DATA data)
{
    // Forward to MobiFlight
    if (mobiFlightWasm != null)
    {
        mobiFlightWasm.ProcessClientDataResponse(data);
    }

    // Forward to PMDG 777 data manager
    if (pmdg777DataManager != null)
    {
        pmdg777DataManager.ProcessClientData(data);
    }
}
```

- [ ] **Step 3: Add SendPMDGEvent convenience method**

```csharp
public void SendPMDGEvent(string eventName, uint eventId, int? parameter = null)
{
    pmdg777DataManager?.SendEvent(eventName, eventId, parameter);
}

public async Task SendPMDGGuardedToggle(string guardEventName, uint guardEventId,
                                          string switchEventName, uint switchEventId)
{
    if (pmdg777DataManager != null)
        await pmdg777DataManager.SendGuardedToggle(guardEventName, guardEventId, switchEventName, switchEventId);
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/SimConnect/SimConnectManager.cs
git commit -m "feat(pmdg777): integrate PMDG data manager into SimConnectManager"
```

---

## Task 5: Create PMDG777Definition Scaffold

**Files:**
- Create: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

This creates the class with all abstract overrides returning minimal implementations. Subsequent tasks fill in the panel content.

- [ ] **Step 1: Create the definition class scaffold**

Create `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`. The class extends `BaseAircraftDefinition` and provides all required overrides. Start with the core class structure, a helper dictionary for PMDG event IDs, and empty panel/variable dictionaries that subsequent tasks will populate.

Key design:
- `AircraftCode` = `"PMDG_777"`
- All FCU control types = `FCUControlType.SetValue`
- Variables use `SimVarType.PMDGVar` with `Name` set to the PMDG SDK field name
- For array fields, variable Name uses `FieldName_N` suffix (e.g., `ELEC_Gen_Sw_ON_0`)
- Each variable's `DisplayName` is human-readable (e.g., "Generator 1")
- Event IDs stored in a static dictionary mapping event name to uint ID

The scaffold should include:
- All abstract property overrides (`AircraftName`, `AircraftCode`)
- `GetVariables()` returning base variables merged with an empty PMDG dictionary (filled in later tasks)
- `GetPanelStructure()` returning the complete section -> panel hierarchy per spec
- `BuildPanelControls()` returning empty panel -> variable mappings (filled in later tasks)
- `GetPanelDisplayVariables()` returning empty dict
- `GetButtonStateMapping()` returning empty dict
- All FCU control type methods returning `SetValue`
- Empty `HandleUIVariableSet()`, `ProcessSimVarUpdate()`, `HandleHotkeyAction()` overrides
- Static event ID dictionary with all PMDG event IDs needed for the implementation

For the event ID dictionary, extract IDs from the PMDG 777 event catalog (`pmdg_777.json`). Include at minimum:
- All overhead switch events (battery, generators, APU, hydraulic, fuel, bleed, air cond, anti-ice, fire, lights, signs, wipers)
- All guard events
- All MCP events (AP, AT, LNAV, VNAV, FLCH, HDG HOLD, VS/FPA, ALT HOLD, LOC, APP, speed/heading/altitude set)
- All EFIS events
- All display select panel events
- All CDU events (L1-L6, R1-R6, function keys, alphanumeric keys, special keys)
- Forward panel events (gear, brakes, GPWS, ISFD)
- Control stand events (speedbrake, flaps, fuel control, parking brake)

- [ ] **Step 2: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add PMDG777Definition scaffold with panel structure and event IDs"
```

---

## Task 6: Add PMDG 777 Variables - Overhead Panels

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

Populate `GetVariables()` and `BuildPanelControls()` for all 14 overhead panels plus 3 overhead maintenance panels.

- [ ] **Step 1: Add overhead panel variables**

Add all overhead variables to `GetVariables()`. Follow this pattern for each variable type:

**Boolean switch (ComboBox Off/On):**
```csharp
["ELEC_Battery"] = new SimVarDefinition
{
    Name = "ELEC_Battery_Sw_ON",
    DisplayName = "Battery",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
```

**Multi-position selector (ComboBox with named positions):**
```csharp
["ELEC_APU_Selector"] = new SimVarDefinition
{
    Name = "ELEC_APU_Selector",
    DisplayName = "APU Selector",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Start" }
},
```

**Read-only annunciator (monitored, no panel control):**
```csharp
["ELEC_annunBattery_OFF"] = new SimVarDefinition
{
    Name = "ELEC_annunBattery_OFF",
    DisplayName = "Battery OFF Light",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Continuous,
    IsAnnounced = true,
    OnlyAnnounceValueDescriptionMatches = true,
    ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
},
```

**Array-indexed variable (use _N suffix):**
```csharp
["ELEC_Gen_1"] = new SimVarDefinition
{
    Name = "ELEC_Gen_Sw_ON_0",  // Index 0 = left/engine 1
    DisplayName = "Generator 1",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
},
```

Add variables for all 14 overhead panels:
1. **Electrical**: Battery, APU gen, APU selector, bus ties L/R, ext power primary/secondary, generators L/R, backup gens L/R, IDG disconnect L/R, cab utility, IFE/pass seats, standby power + all annunciators
2. **Hydraulic**: Primary engine pumps L/R, primary electric pumps L/R, demand electric pump selectors L/R, demand air pump selectors L/R, RAM air turbine + all annunciators
3. **Fuel**: Forward pumps L/R, aft pumps L/R, center pumps L/R, aux pump (if available), crossfeed fwd/aft, jettison nozzles L/R, jettison arm, fuel to remain + all annunciators
4. **Engines**: EEC mode L/R, start selectors L/R, autostart + annunciators
5. **Bleed Air**: Engine bleed L/R, APU bleed, isolation valves L/C/R + annunciators
6. **Air Conditioning**: Packs L/R, trim air L/R, recirc fans upper/lower, equip cooling, gasper, alt ventilation, temp selectors + annunciators
7. **Pressurization**: Outflow valves fwd/aft, landing altitude
8. **Anti-Ice**: Window heat 4 zones, wing anti-ice, engine anti-ice L/R + annunciators
9. **Fire**: Cargo fire arm fwd/aft, cargo discharge, fire test, APU handle, engine handles + annunciators
10. **Lights**: Landing L/R/nose, runway turnoff L/R, taxi, strobe, beacon, nav, logo, wing, storm
11. **Signs**: No smoking, seat belts, passenger oxygen
12. **Wipers**: Wiper selectors L/R, service interphone
13. **Panel Lighting**: All brightness knobs (discretized to Off/Dim/Medium/Bright/Full), master bright switch, indicator lights test, emergency lights
14. **Cargo Temperature**: Cargo temp selectors fwd/aft

And 3 overhead maintenance panels:
15. **Flight Controls**: Wing/tail hyd valve shutoffs (6 total), primary flt computers + annunciators
16. **Backup Systems**: Backup window heat L/R, towing power
17. **EEC/APU Maintenance**: EEC test L/R, APU test

- [ ] **Step 2: Add overhead panel control mappings**

Add entries to `BuildPanelControls()` mapping each panel name to its variable keys. Only include writable/interactive variables, not read-only annunciators (annunciators are monitored automatically via continuous updates).

Example:
```csharp
["Electrical"] = new List<string>
{
    "ELEC_Battery", "ELEC_APUGen", "ELEC_APU_Selector",
    "ELEC_BusTie_1", "ELEC_BusTie_2", "ELEC_ExtPwr_Primary", "ELEC_ExtPwr_Secondary",
    "ELEC_Gen_1", "ELEC_Gen_2", "ELEC_BackupGen_1", "ELEC_BackupGen_2",
    "ELEC_IDGDisc_1", "ELEC_IDGDisc_2", "ELEC_CabUtil", "ELEC_IFEPassSeats",
    "ELEC_StandbyPower"
},
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add overhead panel variables and control mappings"
```

---

## Task 7: Add PMDG 777 Variables - Glareshield Panels

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add glareshield variables**

Add variables for all 4 glareshield panels:

1. **EFIS Captain** (index 0): Mins selector, baro selector, VOR/ADF selectors 1&2, mode selector, range selector, all map overlay buttons, mins/baro knobs, RST/STD/CTR/TFC buttons, FPV, MTRS
2. **EFIS First Officer** (index 1): Same controls, using array index 1
3. **Mode Control Panel**: MCP_IASMach (read-only label), MCP_Heading (read-only label), MCP_Altitude (read-only label), MCP_VertSpeed (read-only label), FD L/R, AT arm L/R, bank limit selector, alt increment selector, disengage bar. All push buttons rendered as buttons with annunciator state: LNAV, VNAV, FLCH, HDG HOLD, VS/FPA, ALT HOLD, LOC, APP, CLB CON, A/T, AP L/R. Toggle buttons: IAS/MACH, HDG/TRK, VS/FPA.
4. **Display Select Panel**: All 17 momentary pushbuttons rendered as buttons

For MCP push buttons with annunciators, use `RenderAsButton = true` and add corresponding annunciator variables for state readback.

For EFIS selectors, use clear value descriptions:
```csharp
// EFIS Mode selector
["EFIS_Mode_Capt"] = new SimVarDefinition
{
    Name = "EFIS_ModeSel_0",
    DisplayName = "Mode",
    Type = SimVarType.PMDGVar,
    UpdateFrequency = UpdateFrequency.Continuous,
    IsAnnounced = true,
    ValueDescriptions = new Dictionary<double, string>
    {
        [0] = "APP", [1] = "VOR", [2] = "MAP", [3] = "PLAN"
    }
},
```

- [ ] **Step 2: Add glareshield panel control mappings**

Add entries to `BuildPanelControls()` for all 4 glareshield panels.

- [ ] **Step 3: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add glareshield panel variables (EFIS, MCP, DSP)"
```

---

## Task 8: Add PMDG 777 Variables - Forward Panel & Pedestal

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Add forward panel variables**

Add variables for 5 forward panels:
1. **Landing Gear**: Gear lever (Up/Down), lock override, alternate gear down
2. **Brakes**: Autobrake selector (RTO/Off/Disarm/1/2/3/Max Auto)
3. **GPWS**: Terrain/gear/flap/GS inhibit, runway override
4. **Instruments**: ISFD buttons, source selects, FMC selector, heading ref, inboard display selectors
5. **Chronometers**: CHR/time-date/ET/set controls for captain and FO

- [ ] **Step 2: Add pedestal variables**

Add variables for 6 pedestal panels:
1. **Control Stand**: Speed brake, flaps (Up/1/5/15/20/25/30), alt flaps arm+control, stab cutout C/R, alt pitch trim, fuel control L/R, parking brake
2. **Transponder/TCAS**: Transponder selector, alt source, mode selector, IDENT, code knobs
3. **Weather Radar**: Mode buttons, tilt, gain, auto, L/R, test
4. **Communication**: Audio panels, radio panels
5. **CDU**: Button to open CDU form (rendered as button), CDU brightness knobs
6. **Warning**: Master warning/caution reset L/R

- [ ] **Step 3: Add panel control mappings**

Add entries to `BuildPanelControls()` for all forward and pedestal panels.

- [ ] **Step 4: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): add forward panel and pedestal variables"
```

---

## Task 9: Implement HandleUIVariableSet

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

This method maps UI control changes to PMDG events.

- [ ] **Step 1: Implement HandleUIVariableSet**

Override `HandleUIVariableSet` with routing logic for all variable types:

```csharp
public override bool HandleUIVariableSet(string varKey, double value,
    SimVarDefinition varDef, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
{
    // Route based on variable key prefix and type
    if (TryHandleToggleSwitch(varKey, value, simConnect, announcer)) return true;
    if (TryHandleSelector(varKey, value, simConnect, announcer)) return true;
    if (TryHandleMomentaryButton(varKey, value, simConnect, announcer)) return true;
    if (TryHandleGuardedSwitch(varKey, value, simConnect, announcer)) return true;

    return false; // Let generic handler try
}
```

Create helper methods:

**TryHandleToggleSwitch**: For simple boolean switches, look up the event name in the event dictionary and send via `SendPMDGEvent()`. Map variable keys to event IDs using the static dictionary.

**TryHandleSelector**: For multi-position selectors, calculate the number of steps needed from current position to target position, and send WHEEL_UP or WHEEL_DOWN mouse flag events. For selectors that support direct position setting, send the position value directly.

**TryHandleMomentaryButton**: For buttons (MCP modes, DSP buttons, etc.), send the press event and then read back the annunciator state to announce.

**TryHandleGuardedSwitch**: For guarded switches (EEC mode, jettison nozzles, etc.), call `SendPMDGGuardedToggle()` with the guard event and switch event.

Key mappings (variable key -> event name -> event ID):
- Build a comprehensive dictionary mapping each variable key to its corresponding PMDG event name and ID
- Use the event catalog from `pmdg_777.json` for accurate IDs
- Group by panel for maintainability

- [ ] **Step 2: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): implement HandleUIVariableSet for all control types"
```

---

## Task 10: Implement ProcessSimVarUpdate

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Implement ProcessSimVarUpdate**

Override `ProcessSimVarUpdate` to handle:

1. **MCP value readouts**: When MCP fields change, format and announce:
   - `MCP_IASMach`: "Speed [value] knots" or "Mach [value]" (if value < 10, it's Mach)
   - `MCP_Heading`: "Heading [value]"
   - `MCP_Altitude`: "Altitude [value]"
   - `MCP_VertSpeed`: "Vertical speed [value]" or "FPA [value]" based on `MCP_VSDial_Mode`
   - `MCP_IASBlank`/`MCP_VertSpeedBlank`: "Speed blank" / "VS blank"

2. **MCP annunciator state changes**: When annunciators change, announce:
   - `MCP_annunAP_0`/`MCP_annunAP_1`: "Autopilot left engaged/disengaged"
   - `MCP_annunAT`: "Autothrottle engaged/disengaged"
   - `MCP_annunLNAV` through `MCP_annunAPP`: "[Mode] engaged/disengaged"

3. **System annunciators**: When any `_annun` variable changes to true, announce the fault/status.
   Format: "[System] [description] light on" / "light off"

4. **Call base**: Always call `base.ProcessSimVarUpdate()` first for altitude crossing announcements.

```csharp
public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
{
    if (base.ProcessSimVarUpdate(varName, value, announcer)) return true;

    // MCP value announcements
    if (varName == "MCP_Heading")
    {
        announcer.AnnounceImmediate($"Heading {(int)value}");
        return true;
    }
    // ... similar for other MCP values

    // MCP mode annunciators
    if (varName == "MCP_annunLNAV")
    {
        announcer.AnnounceImmediate(value > 0 ? "LNAV engaged" : "LNAV disengaged");
        return true;
    }
    // ... similar for other annunciators

    return false;
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): implement ProcessSimVarUpdate for MCP and annunciator announcements"
```

---

## Task 11: Implement HandleHotkeyAction

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

- [ ] **Step 1: Implement hotkey handlers**

Override `HandleHotkeyAction` to handle PMDG-specific actions:

**MCP Readouts (output mode):**
- `ReadHeading` (Shift+H): Read `MCP_Heading` from data manager, announce
- `ReadSpeed` (Shift+S): Read `MCP_IASMach`, format as IAS or Mach
- `ReadAltitude` (Shift+A): Read `MCP_Altitude`
- `ReadFCUVerticalSpeedFPA` (Shift+V): Read `MCP_VertSpeed` or `MCP_FPA` based on mode

**Fuel/Weight Readouts (output mode):**
- `ReadFuelQuantity` (F): Read PMDG fuel quantities in pounds, announce "Left [X], Center [X], Right [X], Total [X] pounds"
- `ReadGrossWeightKg` (Shift+W): Read gross weight, convert to kg if needed

**MCP Direct-Set Dialogs (input mode):**
- `FCUSetHeading` (Ctrl+H): Show input dialog, validate 0-359, send `EVT_MCP_HDGTRK_SET`
- `FCUSetSpeed` (Ctrl+S): Show input dialog, detect IAS vs Mach, send `EVT_MCP_IAS_SET` or `EVT_MCP_MACH_SET`
- `FCUSetAltitude` (Ctrl+A): Show input dialog, validate range, send `EVT_MCP_ALT_SET`
- `FCUSetVS` (Ctrl+V): Show input dialog, encode as value+10000, send `EVT_MCP_VS_SET`
- `FCUSetBaro` (Ctrl+B): Show baro input dialog

**CDU (input mode):**
- `ShowFenixMCDU` (Shift+M): Open `PMDG777CDUForm` instead of Fenix MCDU

Use `ShowFCUInputDialog()` from base class for the MCP set dialogs. Access the data manager via `simConnect.PMDG777DataManager`.

- [ ] **Step 2: Implement RequestFCU methods**

Override the FCU request methods to read from PMDG data:
```csharp
public override void RequestFCUHeading(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
{
    var dm = simConnect.PMDG777DataManager;
    if (dm == null) return;
    int heading = (int)dm.GetFieldValue("MCP_Heading");
    announcer.AnnounceImmediate($"Heading {heading}");
}
```

- [ ] **Step 3: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "feat(pmdg777): implement HandleHotkeyAction for readouts, MCP direct-set, and CDU"
```

---

## Task 12: Create PMDG 777 CDU Form

**Files:**
- Create: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs`
- Create: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs`

- [ ] **Step 1: Create CDU form Designer file**

Create `Forms/PMDG777/PMDG777CDUForm.Designer.cs` with the form layout:

Controls:
- `statusLabel` (Label): Connection status at top
- `cduDisplay` (ListBox): 14 rows, monospace font (Consolas 10pt), read-only
- `scratchpadInput` (TextBox): Scratchpad text entry
- `cduSelector` (ComboBox): CDU 0/1/2 selection (Left/Center/Right)
- Line select buttons: `btnL1`-`btnL6`, `btnR1`-`btnR6`
- Page buttons: `btnInitRef`, `btnRte`, `btnDepArr`, `btnAltn`, `btnVnav`, `btnFix`, `btnLegs`, `btnHold`, `btnFmcComm`, `btnProg`, `btnMenu`, `btnNavRad`, `btnPrevPage`, `btnNextPage`
- Special buttons: `btnExec`, `btnClr`, `btnDel`

Set all `AccessibleName` and `AccessibleDescription` properties. Set `TabIndex` for logical keyboard navigation order: status -> display -> scratchpad -> line select -> page buttons.

Form properties:
- `Text` = "PMDG 777 CDU"
- `Size` = 600x700
- `FormBorderStyle` = FixedDialog
- `KeyPreview` = true

- [ ] **Step 2: Create CDU form code-behind**

Create `Forms/PMDG777/PMDG777CDUForm.cs`:

```csharp
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777;

public partial class PMDG777CDUForm : Form
{
    private readonly PMDG777DataManager _dataManager;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private string[]? _previousRows;
    private string _previousScratchpad = "";
    private int _selectedCDU = 0;

    // CDU event name -> event ID mappings
    private static readonly Dictionary<string, uint> CDUEvents = new()
    {
        ["L1"] = 69986, ["L2"] = 69990, ["L3"] = 69994, ["L4"] = 69998, ["L5"] = 70002, ["L6"] = 70006,
        ["R1"] = 69988, ["R2"] = 69992, ["R3"] = 69996, ["R4"] = 70000, ["R5"] = 70004, ["R6"] = 70008,
        ["INIT_REF"] = 70010, ["RTE"] = 70012, ["DEP_ARR"] = 70014, ["ALTN"] = 70016,
        ["VNAV"] = 70018, ["FIX"] = 70020, ["LEGS"] = 70022, ["HOLD"] = 70024,
        ["FMC_COMM"] = 70026, ["PROG"] = 70028, ["EXEC"] = 70030, ["MENU"] = 70032,
        ["NAV_RAD"] = 70034, ["PREV_PAGE"] = 70036, ["NEXT_PAGE"] = 70038,
        // Alphanumeric keys
        ["A"] = 70040, ["B"] = 70042, ["C"] = 70044, ["D"] = 70046,
        ["E"] = 70048, ["F"] = 70050, ["G"] = 70052, ["H"] = 70054,
        ["I"] = 70056, ["J"] = 70058, ["K"] = 70060, ["L"] = 70062,
        ["M"] = 70064, ["N"] = 70066, ["O"] = 70068, ["P"] = 70070,
        ["Q"] = 70072, ["R"] = 70074, ["S"] = 70076, ["T"] = 70078,
        ["U"] = 70080, ["V"] = 70082, ["W"] = 70084, ["X"] = 70086,
        ["Y"] = 70088, ["Z"] = 70090,
        ["1"] = 70033, ["2"] = 70035, ["3"] = 70037, ["4"] = 70039,
        ["5"] = 70041, ["6"] = 70043, ["7"] = 70045, ["8"] = 70047,
        ["9"] = 70049, ["0"] = 70051,
        ["DOT"] = 70099, ["SLASH"] = 70093, ["SPACE"] = 70091,
        ["DEL"] = 70095, ["CLR"] = 70097, ["PLUS_MINUS"] = 70101,
    };
    // NOTE: The above event IDs are approximate. Verify exact IDs from pmdg_777.json during implementation.

    public PMDG777CDUForm(PMDG777DataManager dataManager, ScreenReaderAnnouncer announcer)
    {
        _dataManager = dataManager;
        _announcer = announcer;
        InitializeComponent();
        SetupAccessibility();
        SetupEventHandlers();

        _pollTimer = new System.Windows.Forms.Timer();
        _pollTimer.Interval = 500;
        _pollTimer.Tick += PollTimer_Tick;
    }

    private void SetupAccessibility()
    {
        cduDisplay.AccessibleName = "CDU Display";
        cduDisplay.AccessibleDescription = "CDU screen content, 14 lines";
        scratchpadInput.AccessibleName = "Scratchpad";
        scratchpadInput.AccessibleDescription = "Type text and press Enter to send to CDU";
    }

    private void SetupEventHandlers()
    {
        // Line select buttons
        btnL1.Click += (s, e) => SendCDUKey("L1");
        btnL2.Click += (s, e) => SendCDUKey("L2");
        // ... repeat for all L/R buttons

        // Page function buttons
        btnInitRef.Click += (s, e) => SendCDUKey("INIT_REF");
        btnRte.Click += (s, e) => SendCDUKey("RTE");
        // ... repeat for all function buttons

        btnExec.Click += (s, e) => SendCDUKey("EXEC");
        btnClr.Click += (s, e) => SendCDUKey("CLR");
        btnDel.Click += (s, e) => SendCDUKey("DEL");

        // Scratchpad input
        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;

        // Form-level keyboard shortcuts
        this.KeyDown += Form_KeyDown;
        this.KeyPreview = true;

        // CDU selector change
        cduSelector.SelectedIndexChanged += (s, e) => _selectedCDU = cduSelector.SelectedIndex;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _pollTimer.Start();
        scratchpadInput.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _pollTimer.Stop();
        base.OnFormClosing(e);
    }

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        _dataManager.RequestCDUScreen(_selectedCDU);
        var rows = _dataManager.GetCDURows(_selectedCDU);

        if (rows == null)
        {
            if (statusLabel.Text != "CDU Not Powered")
            {
                statusLabel.Text = "CDU Not Powered";
                _announcer.AnnounceImmediate("CDU not powered");
            }
            return;
        }

        statusLabel.Text = "CDU Connected";
        UpdateDisplay(rows);
    }

    private void UpdateDisplay(string[] rows)
    {
        // Update ListBox
        cduDisplay.BeginUpdate();
        cduDisplay.Items.Clear();
        foreach (var row in rows)
            cduDisplay.Items.Add(row);
        cduDisplay.EndUpdate();

        // Announce title change
        if (_previousRows == null || rows[0] != _previousRows[0])
        {
            if (!string.IsNullOrWhiteSpace(rows[0]))
                _announcer.AnnounceImmediate($"Page: {rows[0].Trim()}");
        }

        // Announce scratchpad change (last row)
        string scratchpad = rows[13].Trim();
        if (scratchpad != _previousScratchpad)
        {
            if (!string.IsNullOrWhiteSpace(scratchpad))
                _announcer.AnnounceImmediate($"Scratchpad: {scratchpad}");
            _previousScratchpad = scratchpad;
        }

        _previousRows = rows;
    }

    private void SendCDUKey(string key)
    {
        if (CDUEvents.TryGetValue(key, out uint eventId))
        {
            _dataManager.SendEvent($"EVT_CDU_L_{key}", eventId);
        }
    }

    private async Task SendTextToCDU(string text)
    {
        foreach (char c in text.ToUpperInvariant())
        {
            string? key = c switch
            {
                >= 'A' and <= 'Z' => c.ToString(),
                >= '0' and <= '9' => c.ToString(),
                '.' => "DOT",
                '/' => "SLASH",
                ' ' => "SPACE",
                '-' or '+' => "PLUS_MINUS",
                _ => null
            };
            if (key != null)
            {
                SendCDUKey(key);
                await Task.Delay(50);
            }
        }
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            _ = SendTextToCDU(scratchpadInput.Text);
            scratchpadInput.Clear();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Back && string.IsNullOrEmpty(scratchpadInput.Text))
        {
            SendCDUKey("CLR");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+1-6 for L1-L6
        if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            int lsk = e.KeyCode - Keys.D1 + 1;
            SendCDUKey($"L{lsk}");
            // Announce the line content
            int lineIndex = (lsk - 1) * 2 + 1; // L1=row1, L2=row3, etc.
            if (_previousRows != null && lineIndex < _previousRows.Length)
                _announcer.AnnounceImmediate(_previousRows[lineIndex]);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        // Alt+1-6 for R1-R6
        else if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            int rsk = e.KeyCode - Keys.D1 + 1;
            SendCDUKey($"R{rsk}");
            int lineIndex = (rsk - 1) * 2 + 1;
            if (_previousRows != null && lineIndex < _previousRows.Length)
                _announcer.AnnounceImmediate(_previousRows[lineIndex]);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        // Page Up/Down
        else if (e.KeyCode == Keys.PageUp)
        {
            SendCDUKey("PREV_PAGE");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.PageDown)
        {
            SendCDUKey("NEXT_PAGE");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        // Ctrl+Enter for EXEC
        else if (e.Control && e.KeyCode == Keys.Return)
        {
            SendCDUKey("EXEC");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }
}
```

**NOTE:** The CDU event IDs in the dictionary above are approximate. During implementation, verify exact IDs from the `pmdg_777.json` catalog by searching for `EVT_CDU_L_L1`, `EVT_CDU_L_A`, etc.

- [ ] **Step 3: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/Forms/PMDG777/
git commit -m "feat(pmdg777): add PMDG 777 CDU form with keyboard shortcuts and screen reading"
```

---

## Task 13: MainForm Integration

**Files:**
- Modify: `MSFSBlindAssist/MainForm.Designer.cs`
- Modify: `MSFSBlindAssist/MainForm.cs`

- [ ] **Step 1: Add PMDG 777 menu item**

In `MainForm.Designer.cs`:

Add field declaration:
```csharp
private System.Windows.Forms.ToolStripMenuItem pmdg777MenuItem = null!;
```

Add menu item initialization (after the fenixA320MenuItem block):
```csharp
// pmdg777MenuItem
this.pmdg777MenuItem.AccessibleName = "PMDG Boeing 777";
this.pmdg777MenuItem.AccessibleDescription = "Switch to PMDG Boeing 777";
this.pmdg777MenuItem.Name = "pmdg777MenuItem";
this.pmdg777MenuItem.Size = new System.Drawing.Size(240, 26);
this.pmdg777MenuItem.Text = "PMDG Boeing &777";
this.pmdg777MenuItem.Checked = false;
this.pmdg777MenuItem.Click += new System.EventHandler(this.PMDG777MenuItem_Click);
```

Add to the aircraftMenuItem dropdown:
```csharp
this.aircraftMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
    this.flyByWireA320MenuItem,
    this.fenixA320MenuItem,
    this.pmdg777MenuItem});
```

- [ ] **Step 2: Add menu click handler and LoadAircraftFromCode**

In `MainForm.cs`, add the click handler:
```csharp
private void PMDG777MenuItem_Click(object? sender, EventArgs e)
{
    SwitchAircraft("PMDG_777");
}
```

Update `LoadAircraftFromCode()`:
```csharp
private IAircraftDefinition LoadAircraftFromCode(string aircraftCode)
{
    return aircraftCode switch
    {
        "A320" => new FlyByWireA320Definition(),
        "FENIX_A320CEO" => new FenixA320Definition(),
        "PMDG_777" => new PMDG777Definition(),
        _ => new FlyByWireA320Definition()
    };
}
```

- [ ] **Step 3: Add PMDG data manager lifecycle**

In the aircraft switching logic, initialize/dispose the PMDG data manager:

After `SwitchAircraft` loads the new aircraft definition, check if it's a PMDG aircraft:
```csharp
// After loading new aircraft definition:
if (aircraftCode == "PMDG_777")
{
    simConnectManager.InitializePMDG777();
    // Wire up PMDG variable change events to the existing SimVarUpdated pipeline
    if (simConnectManager.PMDG777DataManager != null)
    {
        simConnectManager.PMDG777DataManager.VariableChanged += OnPMDGVariableChanged;
    }
}
else
{
    simConnectManager.DisposePMDG777();
}
```

Add the PMDG variable change handler:
```csharp
private void OnPMDGVariableChanged(object? sender, PMDGVarUpdateEventArgs e)
{
    // Route through the same event pipeline as SimVar updates
    if (currentAircraft != null)
    {
        eventQueue.Enqueue(new SimVarUpdateEventArgs(e.FieldName, e.Value));
    }
}
```

- [ ] **Step 4: Route HandleUIVariableSet for PMDGVar**

In the generic UI variable set handler, add a branch for `PMDGVar` type:
```csharp
// Before the generic SetLVar/SendEvent fallback:
if (varDef.Type == SimVarType.PMDGVar)
{
    // Let the aircraft definition handle it (it knows the event IDs)
    bool handled = currentAircraft.HandleUIVariableSet(varKey, value, varDef, simConnectManager, announcer);
    if (!handled)
    {
        System.Diagnostics.Debug.WriteLine($"[PMDG] Unhandled PMDGVar set: {varKey}");
    }
    return;
}
```

- [ ] **Step 5: Route CDU hotkey**

In the hotkey handler, when `ShowFenixMCDU` is triggered, check aircraft type:
```csharp
case HotkeyAction.ShowFenixMCDU:
    if (currentAircraft?.AircraftCode == "PMDG_777" && simConnectManager.PMDG777DataManager != null)
    {
        var cduForm = new Forms.PMDG777.PMDG777CDUForm(simConnectManager.PMDG777DataManager, announcer);
        cduForm.Show();
    }
    else if (currentAircraft?.AircraftCode == "FENIX_A320CEO")
    {
        // existing Fenix MCDU logic
    }
    break;
```

- [ ] **Step 6: Update menu check marks**

In the `SwitchAircraft` method or menu click handlers, update check marks:
```csharp
flyByWireA320MenuItem.Checked = aircraftCode == "A320";
fenixA320MenuItem.Checked = aircraftCode == "FENIX_A320CEO";
pmdg777MenuItem.Checked = aircraftCode == "PMDG_777";
```

- [ ] **Step 7: Build and commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

```bash
git add MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.Designer.cs
git commit -m "feat(pmdg777): integrate PMDG 777 into MainForm with menu, routing, and CDU hotkey"
```

---

## Task 14: Build, Test, and Fix

**Files:**
- All new and modified files

- [ ] **Step 1: Full build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

Fix any compilation errors. Common issues:
- Missing `using` statements
- Struct alignment mismatches (may need to adjust MarshalAs attributes)
- Event ID mismatches (verify against pmdg_777.json)

- [ ] **Step 2: Verify struct size**

Add a temporary debug line to verify the data struct size matches PMDG's 684 bytes:
```csharp
System.Diagnostics.Debug.WriteLine($"PMDG777XData size: {Marshal.SizeOf(typeof(PMDG777XData))}");
```

If size doesn't match 684, adjust padding/alignment in the struct. The Python reference uses no `_pack_` attribute, meaning default alignment. Common issues:
- `bool` fields may be 1 byte but alignment may add padding
- `ushort` fields need 2-byte alignment
- `int`/`float` fields need 4-byte alignment
- The `reserved` byte array at the end may need size adjustment to hit exactly 684

- [ ] **Step 3: Live test with PMDG 777 in sim**

Test sequence:
1. Launch app, select PMDG 777 from Aircraft menu
2. Verify sections/panels load in the UI
3. Navigate to Electrical panel, verify Battery switch shows Off/On
4. Change Battery switch to On, verify it toggles in sim
5. Test a guarded switch (e.g., EEC Mode)
6. Open CDU form ([ then Shift+M), verify it opens
7. Test MCP readout hotkeys (] then Shift+H for heading)
8. Test MCP direct-set ([ then Ctrl+A to set altitude)

- [ ] **Step 4: Fix issues and commit**

Fix any runtime issues found during testing.

```bash
git add -A
git commit -m "fix(pmdg777): fix build and runtime issues from integration testing"
```

---

## Task 15: Polish and Final Commit

- [ ] **Step 1: Review all announcements for clarity**

Walk through each panel and verify:
- Switch state announcements are clear ("Battery: On" not "ELEC_Battery_Sw_ON: 1")
- Annunciator lights use readable format ("Generator 1 OFF light on")
- MCP readouts are properly formatted ("Speed 280 knots", "Mach 0.84")
- CDU rows are readable

- [ ] **Step 2: Verify all panels have correct controls**

Tab through each panel and confirm:
- All controls are present and labeled correctly
- ComboBoxes have correct options
- Buttons respond to Enter key
- Tab order is logical

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat(pmdg777): polish announcements and panel controls"
```
