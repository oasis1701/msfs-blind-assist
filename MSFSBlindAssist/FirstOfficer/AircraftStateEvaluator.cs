using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Centralised, read-only access to PMDG 777 aircraft state via PMDG777DataManager.
/// All state-dependent logic in checklist auto-detection and flow condition checks
/// goes through here so there is a single point of truth.
/// </summary>
public class AircraftStateEvaluator
{
    private PMDG777DataManager? _dm;

    public AircraftStateEvaluator() { }

    /// <summary>Update the data manager reference (called when sim connects/disconnects).</summary>
    public void SetDataManager(PMDG777DataManager? dm) => _dm = dm;

    public bool IsAvailable => _dm != null;

    // -----------------------------------------------------------------------
    // Raw access
    // -----------------------------------------------------------------------

    /// <summary>
    /// Read a raw value from the PMDG data struct by field name.
    /// Returns 0 if the data manager is unavailable or the field is unknown.
    /// </summary>
    public double GetValue(string fieldName)
    {
        try { return _dm?.GetFieldValue(fieldName) ?? 0; }
        catch { return 0; }
    }

    /// <summary>Returns true if the given field is non-zero.</summary>
    public bool IsOn(string fieldName) => GetValue(fieldName) > 0.5;

    /// <summary>Returns true if all supplied fields are non-zero.</summary>
    public bool AllOn(params string[] fields) => fields.All(IsOn);

    /// <summary>Returns true if the given field equals the expected integer position.</summary>
    public bool IsPosition(string fieldName, int position) =>
        Math.Abs(GetValue(fieldName) - position) < 0.1;

    // -----------------------------------------------------------------------
    // Electrical
    // -----------------------------------------------------------------------

    public bool IsBatteryOn()         => IsOn("ELEC_Battery_Sw_ON");
    public bool IsGpuPower1On()       => IsOn("ELEC_annunExtPowr_ON_0");
    public bool IsGpuPower2On()       => IsOn("ELEC_annunExtPowr_ON_1");
    public bool IsAnyGpuOn()          => IsGpuPower1On() || IsGpuPower2On();
    public bool IsApuGenOn()          => IsOn("ELEC_APUGen_Sw_ON");
    public bool IsBusTie1Auto()       => IsOn("ELEC_BusTie_Sw_AUTO_0");
    public bool IsBusTie2Auto()       => IsOn("ELEC_BusTie_Sw_AUTO_1");
    public bool IsBackupGen1On()      => IsOn("ELEC_BackupGen_Sw_ON_0");
    public bool IsBackupGen2On()      => IsOn("ELEC_BackupGen_Sw_ON_1");
    public bool IsIFEPassSeatsOn()    => IsOn("ELEC_IFEPassSeatsSw");
    public int  ApuSelectorPosition() => (int)Math.Round(GetValue("ELEC_APU_Selector"));

    // -----------------------------------------------------------------------
    // Hydraulic
    // -----------------------------------------------------------------------

    public bool IsEngPump1On()  => IsOn("HYD_PrimaryEngPump_Sw_ON_0");
    public bool IsEngPump2On()  => IsOn("HYD_PrimaryEngPump_Sw_ON_1");
    public bool IsElecPump1On() => IsOn("HYD_PrimaryElecPump_Sw_ON_0");
    public bool IsElecPump2On() => IsOn("HYD_PrimaryElecPump_Sw_ON_1");

    // -----------------------------------------------------------------------
    // Fuel
    // -----------------------------------------------------------------------

    public bool IsFuelPump1FwdOn()   => IsOn("FUEL_PumpFwd_Sw_0");
    public bool IsFuelPump2FwdOn()   => IsOn("FUEL_PumpFwd_Sw_1");
    public bool IsFuelPump1AftOn()   => IsOn("FUEL_PumpAft_Sw_0");
    public bool IsFuelPump2AftOn()   => IsOn("FUEL_PumpAft_Sw_1");
    public bool IsFuelPump1CtrOn()   => IsOn("FUEL_PumpCtr_Sw_0");
    public bool IsFuelPump2CtrOn()   => IsOn("FUEL_PumpCtr_Sw_1");
    public bool AreWingFuelPumpsOn() =>
        IsFuelPump1FwdOn() && IsFuelPump2FwdOn() && IsFuelPump1AftOn() && IsFuelPump2AftOn();

    // Fuel Control: 0=CUTOFF, 1=RUN in PMDG data
    public bool IsEng1FuelControlRun()    => IsOn("ENG_FuelControl_Sw_RUN_0");
    public bool IsEng2FuelControlRun()    => IsOn("ENG_FuelControl_Sw_RUN_1");
    public bool AreFuelControlsCutoff()   => !IsEng1FuelControlRun() && !IsEng2FuelControlRun();

    // -----------------------------------------------------------------------
    // Engines
    // -----------------------------------------------------------------------

    public int Eng1StartSelector() => (int)Math.Round(GetValue("ENG_Start_Selector_0"));
    public int Eng2StartSelector() => (int)Math.Round(GetValue("ENG_Start_Selector_1"));
    public bool IsEecMode1Norm()   => IsOn("ENG_EECMode_Sw_NORM_0");
    public bool IsEecMode2Norm()   => IsOn("ENG_EECMode_Sw_NORM_1");
    public bool IsAutoStartOn()    => IsOn("ENG_Autostart_Sw_ON");

    // -----------------------------------------------------------------------
    // Air conditioning / bleed / pressurization
    // -----------------------------------------------------------------------

    public bool IsPack1Auto()      => IsOn("AIR_Pack_Sw_AUTO_0");
    public bool IsPack2Auto()      => IsOn("AIR_Pack_Sw_AUTO_1");
    public bool IsEngBleed1On()    => IsOn("AIR_EngBleedAir_Sw_AUTO_0");
    public bool IsEngBleed2On()    => IsOn("AIR_EngBleedAir_Sw_AUTO_1");
    public bool IsApuBleedOn()     => IsOn("AIR_APUBleedAir_Sw_AUTO");
    public bool IsOutflowFwdAuto() => IsOn("AIR_OutflowValve_Sw_AUTO_0");
    public bool IsOutflowAftAuto() => IsOn("AIR_OutflowValve_Sw_AUTO_1");
    public bool IsEquipCoolAuto()  => IsOn("AIR_EquipCooling_Sw_AUTO");
    public bool IsGasperOn()       => IsOn("AIR_Gasper_Sw_On");
    public bool IsTrimAir1On()     => IsOn("AIR_TrimAir_Sw_On_0");
    public bool IsTrimAir2On()     => IsOn("AIR_TrimAir_Sw_On_1");

    // -----------------------------------------------------------------------
    // Anti-ice
    // -----------------------------------------------------------------------

    public bool IsWindowHeat1On()  => IsOn("ICE_WindowHeat_Sw_ON_0");
    public bool IsWindowHeat2On()  => IsOn("ICE_WindowHeat_Sw_ON_1");
    public bool IsWindowHeat3On()  => IsOn("ICE_WindowHeat_Sw_ON_2");
    public bool IsWindowHeat4On()  => IsOn("ICE_WindowHeat_Sw_ON_3");
    public bool AreAllWindowHeatOn() =>
        IsWindowHeat1On() && IsWindowHeat2On() && IsWindowHeat3On() && IsWindowHeat4On();
    public bool IsWingAntiIceAuto() => IsOn("ICE_WingAntiIceSw");
    public bool IsEng1AntiIceAuto() => IsOn("ICE_EngAntiIceSw_0");
    public bool IsEng2AntiIceAuto() => IsOn("ICE_EngAntiIceSw_1");

    // -----------------------------------------------------------------------
    // Lights
    // -----------------------------------------------------------------------

    public bool IsBeaconOn()         => IsOn("LTS_Beacon_Sw_ON");
    public bool IsNavOn()            => IsOn("LTS_NAV_Sw_ON");
    public bool IsStrobeOn()         => IsOn("LTS_Strobe_Sw_ON");
    public bool IsLandingLightLOn()  => IsOn("LTS_LandingLights_Sw_ON_0");
    public bool IsLandingLightROn()  => IsOn("LTS_LandingLights_Sw_ON_1");
    public bool IsLandingLightNOn()  => IsOn("LTS_LandingLights_Sw_ON_2");
    public bool IsRwyTurnoffLOn()    => IsOn("LTS_RunwayTurnoff_Sw_ON_0");
    public bool IsRwyTurnoffROn()    => IsOn("LTS_RunwayTurnoff_Sw_ON_1");
    public bool IsTaxiOn()           => IsOn("LTS_Taxi_Sw_ON");
    public bool IsStormOn()          => IsOn("LTS_Storm_Sw_ON");
    public bool IsLogoOn()           => IsOn("LTS_Logo_Sw_ON");
    public bool IsWingLightsOn()     => IsOn("LTS_Wing_Sw_ON");

    // -----------------------------------------------------------------------
    // Signs
    // -----------------------------------------------------------------------

    public int SeatBeltsSelector()   => (int)Math.Round(GetValue("SIGNS_SeatBeltsSelector"));
    public int NoSmokingSelector()   => (int)Math.Round(GetValue("SIGNS_NoSmokingSelector"));
    // EmerLightsSelector: 0=Off (guard open), 1=Armed, 2=On
    public int EmerLightsSelector()  => (int)Math.Round(GetValue("LTS_EmerLightsSelector"));

    // -----------------------------------------------------------------------
    // Flight controls / pedestal
    // -----------------------------------------------------------------------

    // FlapsLever: 0=UP, 1=1, 2=5, 3=15, 4=20, 5=25, 6=30 (PMDG positions)
    public int FlapsLeverPosition()       => (int)Math.Round(GetValue("FCTL_Flaps_Lever"));
    public bool AreFlapsUp()              => FlapsLeverPosition() == 0;
    public bool AreFlapsForTakeoff()      => FlapsLeverPosition() >= 1 && FlapsLeverPosition() <= 3;
    public bool AreFlapsForLanding()      => FlapsLeverPosition() >= 4;

    // SpeedBrakeLever: 0=Down, 1=Armed, 2–7 = deployed positions
    public double SpeeedbrakeLeverPos()   => GetValue("FCTL_Speedbrake_Lever");
    public bool IsSpeedbrakeDown()        => SpeeedbrakeLeverPos() < 0.5;
    public bool IsSpeedbrakeArmed()       => SpeeedbrakeLeverPos() is > 0.5 and < 1.5;

    // -----------------------------------------------------------------------
    // Gear / brakes
    // -----------------------------------------------------------------------

    // GearLever: 0=Up, 1=Down
    public bool IsGearDown()             => IsPosition("GEAR_Lever", 1);
    public bool IsGearUp()               => IsPosition("GEAR_Lever", 0);

    // Autobrake: 0=RTO, 1=Off, 2=1, 3=2, 4=3, 5=4, 6=Auto
    public int AutobrakeSelector()       => (int)Math.Round(GetValue("BRAKES_AutobrakeSelector"));
    public bool IsAutobrakRTO()          => AutobrakeSelector() == 0;
    public bool IsAutobrakeOff()         => AutobrakeSelector() == 1;

    // ParkingBrake: 0=Off, 1=On
    public bool IsParkingBrakeSet()      => IsPosition("BRAKES_ParkingBrakeLeverOn", 1);

    // -----------------------------------------------------------------------
    // MCP / EFIS
    // -----------------------------------------------------------------------

    public bool IsFDLeftOn()             => IsOn("MCP_FD_Sw_On_0");
    public bool IsFDRightOn()            => IsOn("MCP_FD_Sw_On_1");
    public bool IsATArmLeftOn()          => IsOn("MCP_ATArm_Sw_On_0");
    public bool IsATArmRightOn()         => IsOn("MCP_ATArm_Sw_On_1");
    public int EFISModeCapt()            => (int)Math.Round(GetValue("EFIS_ModeSel_0"));
    public int EFISModeFO()              => (int)Math.Round(GetValue("EFIS_ModeSel_1"));
    public int EFISRangeCapt()           => (int)Math.Round(GetValue("EFIS_RangeSel_0"));
    public int EFISRangeFO()             => (int)Math.Round(GetValue("EFIS_RangeSel_1"));

    // -----------------------------------------------------------------------
    // ADIRU / Misc
    // -----------------------------------------------------------------------

    public bool IsADIRUOn()              => IsOn("ADIRU_Sw_On");
    public bool IsThrustAsymCompAuto()   => IsOn("FCTL_ThrustAsymComp_Sw_AUTO");

    // -----------------------------------------------------------------------
    // EFIS / Baro
    // -----------------------------------------------------------------------

    /// <summary>True when the Captain's altimeter is in STD (1013/29.92) mode.</summary>
    public bool IsBaroSTDCapt() => IsOn("EFIS_BaroSTD_Sw_Pushed_0");

    /// <summary>True when the FO's altimeter is in STD mode.</summary>
    public bool IsBaroSTDFO()   => IsOn("EFIS_BaroSTD_Sw_Pushed_1");

    /// <summary>True when any landing light (L, N, or R) is on.</summary>
    public bool AreLandingLightsOn() =>
        IsLandingLightLOn() || IsLandingLightROn() || IsLandingLightNOn();

    // -----------------------------------------------------------------------
    // FMC speeds — bytes (0 = not programmed / FMC not yet initialised)
    // -----------------------------------------------------------------------

    public int GetV1()   => (int)GetValue("FMC_V1");
    public int GetVR()   => (int)GetValue("FMC_VR");
    public int GetV2()   => (int)GetValue("FMC_V2");
    public int GetVRef() => (int)GetValue("FMC_LandingVREF");

    // -----------------------------------------------------------------------
    // SimBrief performance targets (set when an OFP is loaded)
    // -----------------------------------------------------------------------

    private int _takeoffFlaps = 5; // default — common 777 takeoff setting

    /// <summary>Store the takeoff flap setting from SimBrief perf data.</summary>
    public void SetTakeoffFlaps(int flaps) => _takeoffFlaps = flaps;

    /// <summary>
    /// Returns the SimBrief planned takeoff flap setting (degrees: 1, 5, 15, 20, 25, 30).
    /// Defaults to 5 if no SimBrief OFP has been loaded.
    /// </summary>
    public int GetTakeoffFlaps() => _takeoffFlaps;

    // -----------------------------------------------------------------------
    // Fuel quantities (for informational use)
    // -----------------------------------------------------------------------

    public int FuelLeftLbs()             => (int)Math.Round(GetValue("FUEL_QtyLeft"));
    public int FuelCenterLbs()           => (int)Math.Round(GetValue("FUEL_QtyCenter"));
    public int FuelRightLbs()            => (int)Math.Round(GetValue("FUEL_QtyRight"));
    public int TotalFuelLbs()            => FuelLeftLbs() + FuelCenterLbs() + FuelRightLbs();
}
