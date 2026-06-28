using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// Read-only access to PMDG 737 NG3 aircraft state via <see cref="PMDGNG3DataManager"/>,
/// implementing the shared <see cref="IFoStateEvaluator"/> contract. All field names are
/// verified against <c>PMDGNG3DataStruct.cs</c>; array fields are read with the
/// underscore-index form (e.g. <c>"ELEC_GenSw_0"</c>).
/// </summary>
public class AircraftStateEvaluator : IFoStateEvaluator
{
    private PMDGNG3DataManager? _dm;

    /// <summary>Update the data-manager reference (called on connect/disconnect).</summary>
    public void SetDataManager(PMDGNG3DataManager? dm) => _dm = dm;

    public bool IsAvailable => _dm != null;

    public double GetValue(string field)
    {
        try { return _dm?.GetFieldValue(field) ?? 0; }
        catch { return 0; }
    }

    public bool IsOn(string field) => GetValue(field) > 0.5;
    public bool AllOn(params string[] fields) => fields.All(IsOn);
    public bool IsPosition(string field, int position) => Math.Abs(GetValue(field) - position) < 0.1;

    // -----------------------------------------------------------------------
    // Electrical
    // -----------------------------------------------------------------------
    public bool IsBatteryOn()      => IsPosition("ELEC_BatSelector", 2);   // 0=OFF,1=BAT,2=ON
    public bool IsGen1On()         => IsOn("ELEC_GenSw_0");
    public bool IsGen2On()         => IsOn("ELEC_GenSw_1");
    public bool AreGeneratorsOn()  => IsGen1On() && IsGen2On();
    public bool IsApuGen1On()      => IsOn("ELEC_APUGenSw_0");
    public bool IsApuGen2On()      => IsOn("ELEC_APUGenSw_1");
    public bool AreApuGensOn()     => IsApuGen1On() && IsApuGen2On();
    public bool IsGpuAvailable()   => IsOn("ELEC_annunGRD_POWER_AVAILABLE");
    public bool IsGpuOn()          => IsOn("ELEC_GrdPwrSw");
    public int  StandbyPower()     => (int)Math.Round(GetValue("ELEC_StandbyPowerSelector")); // 0=BAT,1=OFF,2=AUTO
    public int  ApuSelector()      => (int)Math.Round(GetValue("APU_Selector"));              // 0=OFF,1=ON,2=START
    public bool IsApuRunning()     => ApuSelector() == 1; // START springs back to ON when available

    // -----------------------------------------------------------------------
    // Fuel + engine start levers (Run derived from the valve annunciator byte; <0.5 == open/Run)
    // -----------------------------------------------------------------------
    public bool IsFuelPumpFwd1On() => IsOn("FUEL_PumpFwdSw_0");
    public bool IsFuelPumpFwd2On() => IsOn("FUEL_PumpFwdSw_1");
    public bool IsFuelPumpAft1On() => IsOn("FUEL_PumpAftSw_0");
    public bool IsFuelPumpAft2On() => IsOn("FUEL_PumpAftSw_1");
    public bool AreWingFuelPumpsOn() =>
        IsFuelPumpFwd1On() && IsFuelPumpFwd2On() && IsFuelPumpAft1On() && IsFuelPumpAft2On();
    public bool IsEng1Run()        => GetValue("FUEL_annunENG_VALVE_CLOSED_0") < 0.5;
    public bool IsEng2Run()        => GetValue("FUEL_annunENG_VALVE_CLOSED_1") < 0.5;
    public int  Eng1StartSelector()=> (int)Math.Round(GetValue("ENG_StartSelector_0")); // 0=GRD,1=OFF,2=CONT,3=FLT
    public int  Eng2StartSelector()=> (int)Math.Round(GetValue("ENG_StartSelector_1"));
    public bool IsStartValve1Open()=> IsOn("ENG_StartValve_0");
    public bool IsStartValve2Open()=> IsOn("ENG_StartValve_1");

    // -----------------------------------------------------------------------
    // Hydraulics  (SDK array: elec[0] = pump 2, elec[1] = pump 1)
    // -----------------------------------------------------------------------
    public bool IsEngHydPump1On()  => IsOn("HYD_PumpSw_eng_0");
    public bool IsEngHydPump2On()  => IsOn("HYD_PumpSw_eng_1");
    public bool AreEngHydPumpsOn() => IsEngHydPump1On() && IsEngHydPump2On();
    public bool IsElecHydPump1On() => IsOn("HYD_PumpSw_elec_1");
    public bool IsElecHydPump2On() => IsOn("HYD_PumpSw_elec_0");
    public bool AreElecHydPumpsOn()=> IsElecHydPump1On() && IsElecHydPump2On();

    // -----------------------------------------------------------------------
    // Air / bleed / pressurization
    // -----------------------------------------------------------------------
    public bool IsPack1Auto()      => IsPosition("AIR_PackSwitch_0", 1); // 0=OFF,1=AUTO,2=HIGH
    public bool IsPack2Auto()      => IsPosition("AIR_PackSwitch_1", 1);
    public bool IsEngBleed1On()    => IsOn("AIR_BleedAirSwitch_0");
    public bool IsEngBleed2On()    => IsOn("AIR_BleedAirSwitch_1");
    public bool IsApuBleedOn()     => IsOn("AIR_APUBleedAirSwitch");
    public int  IsolationValve()   => (int)Math.Round(GetValue("AIR_IsolationValveSwitch")); // 0=CLOSE,1=AUTO,2=OPEN
    public bool IsIsolationAuto()  => IsolationValve() == 1;
    public int  PressMode()        => (int)Math.Round(GetValue("AIR_PressurizationModeSelector")); // 0=AUTO
    public bool IsRecircFanLOn()   => IsOn("AIR_RecircFanSwitch_0");
    public bool IsRecircFanROn()   => IsOn("AIR_RecircFanSwitch_1");
    public bool IsTrimAirOn()      => IsOn("AIR_TrimAirSwitch");

    // -----------------------------------------------------------------------
    // Anti-ice
    // -----------------------------------------------------------------------
    public bool AreWindowHeatsOn() =>
        AllOn("ICE_WindowHeatSw_0", "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3");
    public bool IsProbeHeat1On()   => IsOn("ICE_ProbeHeatSw_0");
    public bool IsProbeHeat2On()   => IsOn("ICE_ProbeHeatSw_1");
    public bool AreProbeHeatsOn()  => IsProbeHeat1On() && IsProbeHeat2On();
    public bool IsWingAntiIceOn()  => IsOn("ICE_WingAntiIceSw");
    public bool IsEng1AntiIceOn()  => IsOn("ICE_EngAntiIceSw_0");
    public bool IsEng2AntiIceOn()  => IsOn("ICE_EngAntiIceSw_1");

    // -----------------------------------------------------------------------
    // Lights
    // -----------------------------------------------------------------------
    public int  LandingLtL()       => (int)Math.Round(GetValue("LTS_LandingLtRetractableSw_0")); // 0=RETRACT,1=EXTEND,2=ON
    public int  LandingLtR()       => (int)Math.Round(GetValue("LTS_LandingLtRetractableSw_1"));
    public bool AreLandingLightsOn() => LandingLtL() == 2 || LandingLtR() == 2;
    public bool IsRwyTurnoffLOn()  => IsOn("LTS_RunwayTurnoffSw_0");
    public bool IsRwyTurnoffROn()  => IsOn("LTS_RunwayTurnoffSw_1");
    public bool AreRwyTurnoffsOn() => IsRwyTurnoffLOn() && IsRwyTurnoffROn();
    public bool IsTaxiLightOn()    => IsOn("LTS_TaxiSw");
    public bool IsLogoOn()         => IsOn("LTS_LogoSw");
    public int  PositionLight()    => (int)Math.Round(GetValue("LTS_PositionSw")); // 0=STEADY,1=OFF,2=STROBE&STEADY
    public bool IsBeaconOn()       => IsOn("LTS_AntiCollisionSw");
    public bool IsWingLightOn()    => IsOn("LTS_WingSw");

    // -----------------------------------------------------------------------
    // Signs
    // -----------------------------------------------------------------------
    public int  SeatbeltSign()     => (int)Math.Round(GetValue("COMM_FastenBeltsSelector")); // 0=OFF,1=AUTO,2=ON
    public int  EmerExitLights()   => (int)Math.Round(GetValue("LTS_EmerExitSelector"));      // 0=OFF,1=ARMED,2=ON

    // -----------------------------------------------------------------------
    // Flight controls / pedestal
    // -----------------------------------------------------------------------
    public bool IsYawDamperOn()    => IsOn("FCTL_YawDamper_Sw");
    public int  GearLever()        => (int)Math.Round(GetValue("MAIN_GearLever")); // 0=UP,1=OFF,2=DOWN
    public bool IsGearDown()       => GearLever() == 2;
    public bool IsGearUp()         => GearLever() == 0;
    public int  Autobrake()        => (int)Math.Round(GetValue("MAIN_AutobrakeSelector")); // 0=RTO,1=OFF,2..=1/2/3,5=MAX
    public bool IsAutobrakeRTO()   => Autobrake() == 0;
    public bool IsParkingBrakeSet()=> IsOn("PED_annunParkingBrake");

    // -----------------------------------------------------------------------
    // MCP / EFIS / IRS
    // -----------------------------------------------------------------------
    public bool IsFDLeftOn()       => IsOn("MCP_FDSw_0");
    public bool IsFDRightOn()      => IsOn("MCP_FDSw_1");
    public bool IsATArmOn()        => IsOn("MCP_ATArmSw");
    public int  EFISModeCapt()     => (int)Math.Round(GetValue("EFIS_ModeSel_0")); // 0=APP,1=VOR,2=MAP,3=PLAN
    public int  EFISModeFO()       => (int)Math.Round(GetValue("EFIS_ModeSel_1"));
    public int  EFISRangeCapt()    => (int)Math.Round(GetValue("EFIS_RangeSel_0"));
    public int  EFISRangeFO()      => (int)Math.Round(GetValue("EFIS_RangeSel_1"));
    public int  IrsMode1()         => (int)Math.Round(GetValue("IRS_ModeSelector_0")); // 0=OFF,1=ALIGN,2=NAV,3=ATT
    public int  IrsMode2()         => (int)Math.Round(GetValue("IRS_ModeSelector_1"));
    public bool AreIrsInNav()      => IrsMode1() == 2 && IrsMode2() == 2;
    public bool IsIrsAligned()     => IsOn("IRS_aligned");

    // NOTE: the 737 NG3 data struct exposes NO baro-STD state field (only EFIS_BaroSelHPA,
    // the hPa/inHg unit). The altimeter STD/QNH push EVENTS exist, so the FlightPhaseMonitor
    // can push STD at the transition altitude/level, but it must track its own STD intent
    // rather than reading current state — there is intentionally no IsBaroSTD* accessor here.

    // -----------------------------------------------------------------------
    // FMC speeds (bytes; 0 = not yet programmed)
    // -----------------------------------------------------------------------
    public int  GetV1()   => (int)GetValue("FMC_V1");
    public int  GetVR()   => (int)GetValue("FMC_VR");
    public int  GetV2()   => (int)GetValue("FMC_V2");
    public int  GetVRef() => (int)GetValue("FMC_LandingVREF");

    // -----------------------------------------------------------------------
    // SimBrief takeoff flaps (set when an OFP is loaded; used by the Before Taxi flow)
    // -----------------------------------------------------------------------
    private int _takeoffFlaps = 5;
    public void SetTakeoffFlaps(int flaps) => _takeoffFlaps = flaps;
    public int  GetTakeoffFlaps() => _takeoffFlaps;
}
