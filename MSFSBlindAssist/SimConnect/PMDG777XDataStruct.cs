using System.Runtime.InteropServices;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// 3-byte CDU cell: symbol, color, flags.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PMDG777CDUCell
    {
        public byte Symbol;
        public byte Color;
        public byte Flags;
    }

    /// <summary>
    /// CDU screen: 24 columns × 14 rows = 336 cells, plus powered flag.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PMDG777CDUScreen
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 336)] // 24 * 14
        public PMDG777CDUCell[] Cells;
        [MarshalAs(UnmanagedType.U1)]
        public bool Powered;
    }

    /// <summary>
    /// Control struct sent to PMDG to trigger events (8 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PMDG777Control
    {
        public uint EventId;
        public uint Parameter;
    }

    /// <summary>
    /// Binary-compatible mirror of PMDG_777X_Data from the PMDG 777X SDK.
    /// Uses default sequential layout (no Pack) to match MSVC default alignment.
    /// Total size: 684 bytes (confirmed by live testing).
    /// Field order mirrors PMDG_777X_DataStruct exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PMDG777XDataStruct
    {
        // ------------------------------------------------------------------
        // Overhead Maintenance Panel
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ICE_WindowHeatBackUp_Sw_OFF;            // c_bool * 2
        public byte ELEC_StandbyPowerSw;                      // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] FCTL_WingHydValve_Sw_SHUT_OFF;          // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] FCTL_TailHydValve_Sw_SHUT_OFF;          // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] FCTL_annunTailHydVALVE_CLOSED;          // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] FCTL_annunWingHydVALVE_CLOSED;          // c_bool * 3
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_PrimFltComputersSw_AUTO;             // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_annunPrimFltComputersDISC;           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool APU_Power_Sw_TEST;                        // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ENG_EECPower_Sw_TEST;                   // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_TowingPower_Sw_BATT;                 // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_annunTowingPowerON_BATT;             // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] AIR_CargoTemp_Selector;                 // c_ubyte * 2
        public byte AIR_CargoTemp_MainDeckFwd_Sel;            // c_ubyte
        public byte AIR_CargoTemp_MainDeckAft_Sel;            // c_ubyte
        public byte AIR_CargoTemp_LowerFwd_Sel;               // c_ubyte
        public byte AIR_CargoTemp_LowerAft_Sel;               // c_ubyte

        // ------------------------------------------------------------------
        // Overhead Panel
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.U1)]
        public bool ADIRU_Sw_On;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ADIRU_annunOFF;                           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ADIRU_annunON_BAT;                        // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_ThrustAsymComp_Sw_AUTO;              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_annunThrustAsymCompOFF;              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_CabUtilSw;                           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_annunCabUtilOFF;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_IFEPassSeatsSw;                      // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_annunIFEPassSeatsOFF;                // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_Battery_Sw_ON;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_annunBattery_OFF;                    // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_annunAPU_GEN_OFF;                    // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_APUGen_Sw_ON;                        // c_bool
        public byte ELEC_APU_Selector;                        // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool ELEC_annunAPU_FAULT;                      // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_BusTie_Sw_AUTO;                    // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_annunBusTieISLN;                   // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_ExtPwrSw;                          // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_annunExtPowr_ON;                   // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_annunExtPowr_AVAIL;                // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_Gen_Sw_ON;                         // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_annunGenOFF;                       // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_BackupGen_Sw_ON;                   // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_annunBackupGenOFF;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_IDGDiscSw;                         // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ELEC_annunIDGDiscDRIVE;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] WIPERS_Selector;                        // c_ubyte * 2
        public byte LTS_EmerLightsSelector;                   // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool COMM_ServiceInterphoneSw;                 // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool OXY_PassOxygen_Sw_On;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool OXY_annunPassOxygenON;                    // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public bool[] ICE_WindowHeat_Sw_ON;                   // c_bool * 4
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public bool[] ICE_annunWindowHeatINOP;                // c_bool * 4
        [MarshalAs(UnmanagedType.U1)]
        public bool HYD_RamAirTurbineSw;                      // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool HYD_annunRamAirTurbinePRESS;              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool HYD_annunRamAirTurbineUNLKD;              // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] HYD_PrimaryEngPump_Sw_ON;               // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] HYD_PrimaryElecPump_Sw_ON;              // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] HYD_DemandElecPump_Selector;            // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] HYD_DemandAirPump_Selector;             // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] HYD_annunPrimaryEngPumpFAULT;           // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] HYD_annunPrimaryElecPumpFAULT;          // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] HYD_annunDemandElecPumpFAULT;           // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] HYD_annunDemandAirPumpFAULT;            // c_bool * 2
        public byte SIGNS_NoSmokingSelector;                   // c_ubyte
        public byte SIGNS_SeatBeltsSelector;                   // c_ubyte
        public byte LTS_DomeLightKnob;                        // c_ubyte
        public byte LTS_CircuitBreakerKnob;                   // c_ubyte
        public byte LTS_OvereadPanelKnob;                     // c_ubyte
        public byte LTS_GlareshieldPNLlKnob;                  // c_ubyte
        public byte LTS_GlareshieldFLOODKnob;                 // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_Storm_Sw_ON;                          // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_MasterBright_Sw_ON;                   // c_bool
        public byte LTS_MasterBrigntKnob;                     // c_ubyte
        public byte LTS_IndLightsTestSw;                      // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] LTS_LandingLights_Sw_ON;                // c_bool * 3
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_Beacon_Sw_ON;                         // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_NAV_Sw_ON;                            // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_Logo_Sw_ON;                           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_Wing_Sw_ON;                           // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] LTS_RunwayTurnoff_Sw_ON;                // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_Taxi_Sw_ON;                           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool LTS_Strobe_Sw_ON;                         // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FIRE_CargoFire_Sw_Arm;                  // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FIRE_annunCargoFire;                    // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_CargoFireDisch_Sw;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_annunCargoDISCH;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_FireOvhtTest_Sw;                     // c_bool
        public byte FIRE_APUHandle;                           // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_APUHandleUnlock_Sw;                  // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_annunAPU_BTL_DISCH;                  // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FIRE_EngineHandleIlluminated;           // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_APUHandleIlluminated;                // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FIRE_EngineHandleIsUnlocked;            // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_APUHandleIsUnlocked;                 // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_annunMainDeckCargoFire;              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FIRE_annunCargoDEPR;                      // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ENG_EECMode_Sw_NORM;                    // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] ENG_Start_Selector;                     // c_ubyte * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool ENG_Autostart_Sw_ON;                      // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ENG_annunALTN;                          // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool ENG_annunAutostartOFF;                    // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FUEL_CrossFeedFwd_Sw;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FUEL_CrossFeedAft_Sw;                     // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_PumpFwd_Sw;                        // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_PumpAft_Sw;                        // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_PumpCtr_Sw;                        // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_JettisonNozle_Sw;                  // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool FUEL_JettisonArm_Sw;                      // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FUEL_FuelToRemain_Sw_Pulled;              // c_bool
        public byte FUEL_FuelToRemain_Selector;               // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool FUEL_annunFwdXFEED_VALVE;                 // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FUEL_annunAftXFEED_VALVE;                 // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_annunLOWPRESS_Fwd;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_annunLOWPRESS_Aft;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_annunLOWPRESS_Ctr;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FUEL_annunJettisonNozleVALVE;           // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool FUEL_annunArmFAULT;                       // c_bool
        public byte ICE_WingAntiIceSw;                        // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] ICE_EngAntiIceSw;                       // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_Pack_Sw_AUTO;                       // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_TrimAir_Sw_On;                      // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_RecircFan_Sw_On;                    // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] AIR_TempSelector;                       // c_ubyte * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_AirCondReset_Sw_Pushed;               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_EquipCooling_Sw_AUTO;                 // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_Gasper_Sw_On;                         // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_annunPackOFF;                       // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_annunTrimAirFAULT;                  // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_annunEquipCoolingOVRD;                // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_AltnVentSw_ON;                        // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_annunAltnVentFAULT;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_MainDeckFlowSw_NORM;                  // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_EngBleedAir_Sw_AUTO;                // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_APUBleedAir_Sw_AUTO;                  // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_IsolationValve_Sw;                  // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_CtrIsolationValve_Sw;                 // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_annunEngBleedAirOFF;                // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_annunAPUBleedAirOFF;                  // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_annunIsolationValveCLOSED;          // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_annunCtrIsolationValveCLOSED;         // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_OutflowValve_Sw_AUTO;               // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] AIR_OutflowValveManual_Selector;        // c_ubyte * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool AIR_LdgAlt_Sw_Pulled;                     // c_bool
        public byte AIR_LdgAlt_Selector;                      // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] AIR_annunOutflowValve_MAN;              // c_bool * 2

        // ------------------------------------------------------------------
        // Forward panel
        // ------------------------------------------------------------------
        public byte GEAR_Lever;                               // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool GEAR_LockOvrd_Sw;                         // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GEAR_AltnGear_Sw_DOWN;                    // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_FlapInhibitSw_OVRD;                  // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_GearInhibitSw_OVRD;                  // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_TerrInhibitSw_OVRD;                  // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_RunwayOvrdSw_OVRD;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_GSInhibit_Sw;                        // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_annunGND_PROX_top;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_annunGND_PROX_bottom;                // c_bool
        public byte BRAKES_AutobrakeSelector;                 // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool ISFD_Baro_Sw_Pushed;                      // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISFD_RST_Sw_Pushed;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISFD_Minus_Sw_Pushed;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISFD_Plus_Sw_Pushed;                      // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISFD_APP_Sw_Pushed;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISFD_HP_IN_Sw_Pushed;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISP_Nav_L_Sw_CDU;                         // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISP_DsplCtrl_L_Sw_Altn;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISP_AirDataAtt_L_Sw_Altn;                 // c_bool
        public byte DSP_InbdDspl_L_Selector;                  // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool EFIS_HdgRef_Sw_Norm;                      // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool EFIS_annunHdgRefTRUE;                     // c_bool
        public int BRAKES_BrakePressNeedle;                   // c_int  (4-byte aligned)
        [MarshalAs(UnmanagedType.U1)]
        public bool BRAKES_annunBRAKE_SOURCE;                 // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISP_Nav_R_Sw_CDU;                         // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISP_DsplCtrl_R_Sw_Altn;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool ISP_AirDataAtt_R_Sw_Altn;                 // c_bool
        public byte ISP_FMC_Selector;                         // c_ubyte
        public byte DSP_InbdDspl_R_Selector;                  // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] AIR_ShoulderHeaterKnob;                 // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] AIR_FootHeaterSelector;                 // c_ubyte * 2
        public byte LTS_LeftFwdPanelPNLKnob;                  // c_ubyte
        public byte LTS_LeftFwdPanelFLOODKnob;               // c_ubyte
        public byte LTS_LeftOutbdDsplBRIGHTNESSKnob;         // c_ubyte
        public byte LTS_LeftInbdDsplBRIGHTNESSKnob;          // c_ubyte
        public byte LTS_RightFwdPanelPNLKnob;                 // c_ubyte
        public byte LTS_RightFwdPanelFLOODKnob;              // c_ubyte
        public byte LTS_RightInbdDsplBRIGHTNESSKnob;         // c_ubyte
        public byte LTS_RightOutbdDsplBRIGHTNESSKnob;        // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] CHR_Chr_Sw_Pushed;                      // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] CHR_TimeDate_Sw_Pushed;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] CHR_TimeDate_Selector;                  // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] CHR_Set_Selector;                       // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] CHR_ET_Selector;                        // c_ubyte * 2

        // ------------------------------------------------------------------
        // Glareshield
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_MinsSelBARO;                       // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_BaroSelHPA;                        // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] EFIS_VORADFSel1;                        // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] EFIS_VORADFSel2;                        // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] EFIS_ModeSel;                           // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] EFIS_RangeSel;                          // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] EFIS_MinsKnob;                          // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] EFIS_BaroKnob;                          // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_MinsRST_Sw_Pushed;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_BaroSTD_Sw_Pushed;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_ModeCTR_Sw_Pushed;                 // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_RangeTFC_Sw_Pushed;                // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_WXR_Sw_Pushed;                     // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_STA_Sw_Pushed;                     // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_WPT_Sw_Pushed;                     // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_ARPT_Sw_Pushed;                    // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_DATA_Sw_Pushed;                    // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_POS_Sw_Pushed;                     // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_TERR_Sw_Pushed;                    // c_bool * 2
        public float MCP_IASMach;                             // c_float (4-byte aligned)
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_IASBlank;                             // c_bool
        public ushort MCP_Heading;                            // c_ushort (2-byte aligned)
        public ushort MCP_Altitude;                           // c_ushort
        public short MCP_VertSpeed;                           // c_short
        public float MCP_FPA;                                 // c_float (4-byte aligned)
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_VertSpeedBlank;                       // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] MCP_FD_Sw_On;                           // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] MCP_ATArm_Sw_On;                        // c_bool * 2
        public byte MCP_BankLimitSel;                         // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_AltIncrSel;                           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_DisengageBar;                         // c_bool
        public byte MCP_Speed_Dial;                           // c_ubyte
        public byte MCP_Heading_Dial;                         // c_ubyte
        public byte MCP_Altitude_Dial;                        // c_ubyte
        public byte MCP_VS_Wheel;                             // c_ubyte
        public byte MCP_HDGDial_Mode;                         // c_ubyte
        public byte MCP_VSDial_Mode;                          // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] MCP_AP_Sw_Pushed;                       // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_CLB_CON_Sw_Pushed;                    // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_AT_Sw_Pushed;                         // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_LNAV_Sw_Pushed;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_VNAV_Sw_Pushed;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_FLCH_Sw_Pushed;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_HDG_HOLD_Sw_Pushed;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_VS_FPA_Sw_Pushed;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_ALT_HOLD_Sw_Pushed;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_LOC_Sw_Pushed;                        // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_APP_Sw_Pushed;                        // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_Speeed_Sw_Pushed;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_Heading_Sw_Pushed;                    // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_Altitude_Sw_Pushed;                   // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_IAS_MACH_Toggle_Sw_Pushed;            // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_HDG_TRK_Toggle_Sw_Pushed;             // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_VS_FPA_Toggle_Sw_Pushed;              // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] MCP_annunAP;                            // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunAT;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunLNAV;                            // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunVNAV;                            // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunFLCH;                            // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunHDG_HOLD;                        // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunVS_FPA;                          // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunALT_HOLD;                        // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunLOC;                             // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool MCP_annunAPP;                             // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_L_INBD_Sw;                            // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_R_INBD_Sw;                            // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_LWR_CTR_Sw;                           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_ENG_Sw;                               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_STAT_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_ELEC_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_HYD_Sw;                               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_FUEL_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_AIR_Sw;                               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_DOOR_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_GEAR_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_FCTL_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_CAM_Sw;                               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_CHKL_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_COMM_Sw;                              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_NAV_Sw;                               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_CANC_RCL_Sw;                          // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_annunL_INBD;                          // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_annunR_INBD;                          // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool DSP_annunLWR_CTR;                         // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] WARN_Reset_Sw_Pushed;                   // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] WARN_annunMASTER_WARNING;               // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] WARN_annunMASTER_CAUTION;               // c_bool * 2

        // ------------------------------------------------------------------
        // Forward Aisle Stand Panel
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.U1)]
        public bool ISP_DsplCtrl_C_Sw_Altn;                   // c_bool
        public byte LTS_UpperDsplBRIGHTNESSKnob;              // c_ubyte
        public byte LTS_LowerDsplBRIGHTNESSKnob;              // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool EICAS_EventRcd_Sw_Pushed;                  // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] CDU_annunEXEC;                           // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] CDU_annunDSPY;                           // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] CDU_annunFAIL;                           // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] CDU_annunMSG;                            // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] CDU_annunOFST;                           // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] CDU_BrtKnob;                             // c_ubyte * 3

        // ------------------------------------------------------------------
        // Control Stand
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_AltnFlaps_Sw_ARM;                    // c_bool
        public byte FCTL_AltnFlaps_Control_Sw;                // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_StabCutOutSw_C_NORMAL;               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_StabCutOutSw_R_NORMAL;               // c_bool
        public byte FCTL_AltnPitch_Lever;                     // c_ubyte
        public byte FCTL_Speedbrake_Lever;                    // c_ubyte
        public byte FCTL_Flaps_Lever;                         // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ENG_FuelControl_Sw_RUN;                 // c_bool * 2
        [MarshalAs(UnmanagedType.U1)]
        public bool BRAKES_ParkingBrakeLeverOn;               // c_bool

        // ------------------------------------------------------------------
        // Aft Aisle Stand Panel
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] COMM_SelectedMic;                       // c_ubyte * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] COMM_ReceiverSwitches;                // c_ushort * 3 (2-byte aligned)
        public byte COMM_OBSAudio_Selector;                   // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] COMM_SelectedRadio;                     // c_ubyte * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] COMM_RadioTransfer_Sw_Pushed;           // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] COMM_RadioPanelOff;                     // c_bool * 3
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public bool[] COMM_annunAM;                           // c_bool * 3
        [MarshalAs(UnmanagedType.U1)]
        public bool XPDR_XpndrSelector_R;                     // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool XPDR_AltSourceSel_ALTN;                   // c_bool
        public byte XPDR_ModeSel;                             // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool XPDR_Ident_Sw_Pushed;                     // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] FIRE_EngineHandle;                      // c_ubyte * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FIRE_EngineHandleUnlock_Sw;             // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] FIRE_annunENG_BTL_DISCH;                // c_bool * 2
        public byte FCTL_AileronTrim_Switches;                // c_ubyte
        public byte FCTL_RudderTrim_Knob;                     // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool FCTL_RudderTrimCancel_Sw_Pushed;          // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool EVAC_Command_Sw_ON;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool EVAC_PressToTest_Sw_Pressed;              // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool EVAC_HornSutOff_Sw_Pulled;                // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool EVAC_LightIlluminated;                    // c_bool
        public byte LTS_AisleStandPNLKnob;                    // c_ubyte
        public byte LTS_AisleStandFLOODKnob;                  // c_ubyte
        public byte LTS_FloorLightsSw;                        // c_ubyte

        // ------------------------------------------------------------------
        // Door state
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] DOOR_state;                             // c_ubyte * 16
        [MarshalAs(UnmanagedType.U1)]
        public bool DOOR_CockpitDoorOpen;                     // c_bool

        // ------------------------------------------------------------------
        // Additional variables
        // ------------------------------------------------------------------
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] ENG_StartValve;                         // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] AIR_DuctPress;                         // c_float * 2 (4-byte aligned)
        public float FUEL_QtyCenter;                          // c_float
        public float FUEL_QtyLeft;                            // c_float
        public float FUEL_QtyRight;                           // c_float
        public float FUEL_QtyAux;                             // c_float
        [MarshalAs(UnmanagedType.U1)]
        public bool IRS_aligned;                              // c_bool
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_BaroMinimumsSet;                   // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] EFIS_BaroMinimums;                       // c_int * 2 (4-byte aligned)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public bool[] EFIS_RadioMinimumsSet;                  // c_bool * 2
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] EFIS_RadioMinimums;                      // c_int * 2 (4-byte aligned)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] EFIS_Display;                           // c_ubyte * 6
        public byte AircraftModel;                            // c_ubyte
        [MarshalAs(UnmanagedType.U1)]
        public bool WeightInKg;                               // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GPWS_V1CallEnabled;                       // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool GroundConnAvailable;                      // c_bool
        public byte FMC_TakeoffFlaps;                         // c_ubyte
        public byte FMC_V1;                                   // c_ubyte
        public byte FMC_VR;                                   // c_ubyte
        public byte FMC_V2;                                   // c_ubyte
        public ushort FMC_ThrustRedAlt;                       // c_ushort (2-byte aligned)
        public ushort FMC_AccelerationAlt;                    // c_ushort
        public ushort FMC_EOAccelerationAlt;                  // c_ushort
        public byte FMC_LandingFlaps;                         // c_ubyte
        public byte FMC_LandingVREF;                          // c_ubyte
        public ushort FMC_CruiseAlt;                          // c_ushort (2-byte aligned)
        public short FMC_LandingAltitude;                     // c_short
        public ushort FMC_TransitionAlt;                      // c_ushort
        public ushort FMC_TransitionLevel;                    // c_ushort
        [MarshalAs(UnmanagedType.U1)]
        public bool FMC_PerfInputComplete;                    // c_bool
        public float FMC_DistanceToTOD;                       // c_float (4-byte aligned)
        public float FMC_DistanceToDest;                      // c_float
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public byte[] FMC_flightNumber;                       // c_char * 9
        [MarshalAs(UnmanagedType.U1)]
        public bool WheelChocksSet;                           // c_bool
        [MarshalAs(UnmanagedType.U1)]
        public bool APURunning;                               // c_bool
        public byte FMC_ThrustLimitMode;                      // c_ubyte
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public bool[] ECL_ChecklistComplete;                  // c_bool * 10
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 84)]
        public byte[] reserved;                               // c_ubyte * 84
    }
}
