using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Centralised, read-only access to PMDG 777 aircraft state via PMDG777DataManager.
/// All state-dependent logic in checklist auto-detection and flow condition checks
/// goes through here so there is a single point of truth.
/// </summary>
public class AircraftStateEvaluator : IFoStateEvaluator
{
    // N2 (percent) at/above which an engine is treated as RUNNING (stabilised near idle).
    // Same threshold as both 737 profiles.
    public const double EngineRunningN2 = 50.0;

    private PMDG777DataManager? _dm;

    // Engine N2 (percent), pushed from the shared FO background timer via SetEngineN2.
    // NaN (not 0) until the first push: before then "engine N2" is genuinely UNKNOWN, never
    // "engine stopped", and ChecklistManager treats NaN as "skip both auto-tick and revert".
    private double _eng1N2 = double.NaN;
    private double _eng2N2 = double.NaN;

    // Gear-leg positions (percent extended; left main, nose, right main), pushed from the same
    // timer via SetGearPosition — NaN until the first push, so the gear verdicts stay unknown
    // rather than guessing. Indexed as Pmdg777GearConfirmation.PositionFields.
    private readonly double[] _gearPct = { double.NaN, double.NaN, double.NaN };

    public AircraftStateEvaluator() { }

    private static int GearLegIndex(string fieldName)
    {
        var fields = Pmdg777GearConfirmation.PositionFields;
        for (int i = 0; i < fields.Count; i++)
            if (fields[i] == fieldName) return i;
        return -1;
    }

    /// <summary>
    /// Store one gear leg's position (percent extended), fed by FirstOfficerForm from the stock
    /// GEAR LEFT/CENTER/RIGHT POSITION SimVars. <paramref name="field"/> is one of
    /// <see cref="Pmdg777GearConfirmation.PositionFields"/>; anything else is ignored.
    /// </summary>
    public void SetGearPosition(string field, double percent)
    {
        int leg = GearLegIndex(field);
        if (leg >= 0) System.Threading.Volatile.Write(ref _gearPct[leg], percent);
    }

    /// <summary>Update the data manager reference (called when sim connects/disconnects).</summary>
    public void SetDataManager(PMDG777DataManager? dm) => _dm = dm;

    // Values the PMDG CDA does not carry come from SimConnect's variable cache (a
    // ConcurrentDictionary, safe from the flow's pool threads) — the speed-brake lever's main
    // L-var. Set by Pmdg777FoProfile.BindDataManager; a test injects its own.
    private Func<string, double?>? _cachedValue;

    public void SetCachedValueSource(Func<string, double?>? source) => _cachedValue = source;

    public bool IsAvailable => _dm != null;

    // -----------------------------------------------------------------------
    // Raw access
    // -----------------------------------------------------------------------

    /// <summary>
    /// Read a raw value from the PMDG data struct by field name.
    /// Returns NaN (INDETERMINATE) until the first CDA snapshot has arrived —
    /// GetFieldValue reads 0.0 for EVERY field before then (interface contract),
    /// which false-matched every OFF/closed checklist condition at startup and
    /// latched items complete (the same fix as the 737 evaluator).
    /// ChecklistManager treats NaN as "skip both auto-tick and revert".
    /// </summary>
    public double GetValue(string fieldName)
    {
        // Synthetic FO-only field: ANY external power on bus (the two annunciators
        // are OR'd — a single StateFieldName can't express OR).
        if (fieldName == "FO_ANY_GPU_ON")
            return !CdaReady ? double.NaN
                : (RawOn("ELEC_annunExtPowr_ON_0") || RawOn("ELEC_annunExtPowr_ON_1") ? 1 : 0);

        // Merged Before-Start "Fuel pumps: ON" detection (§6): wing on AND center matches
        // fuel state. ONE synthetic (never a primary+additional split — the Auto() helper
        // applies the same condition to every field, which cannot express wing≠center logic).
        if (fieldName == "FO_FUEL_PUMPS_BS_OK")
            return !CdaReady ? double.NaN
                : (FuelSystemLogic.BeforeStartFuelPumpsOk(
                       AreWingFuelPumpsOn(), IsEitherCenterPumpOn(),
                       FuelCenterLbs() > CenterFuelPumpAutomation.OffThresholdLbs) ? 1 : 0);

        // Engine N2 comes from SimConnect, NOT the PMDG CDA — so it is served BEFORE the
        // CdaReady gate below. Gating it would return NaN for the whole session whenever the
        // CDA snapshot has not landed, which is precisely when an engine start happens.
        if (fieldName == "FO_ENG1_N2") return System.Threading.Volatile.Read(ref _eng1N2);
        if (fieldName == "FO_ENG2_N2") return System.Threading.Volatile.Read(ref _eng2N2);
        // Both engines below 10 % N2 (1), either above (0), NaN until both are read: the
        // cold-and-dark flows' guard (PMDG777FlowDefinitions.EnginesStopped's test).
        if (fieldName == EnginesStoppedField)
        {
            double n1 = System.Threading.Volatile.Read(ref _eng1N2), n2 = System.Threading.Volatile.Read(ref _eng2N2);
            return double.IsNaN(n1) || double.IsNaN(n2) ? double.NaN : n1 < 10 && n2 < 10 ? 1 : 0;
        }
        // SIM ON GROUND from SimConnect (IsOnGround): 1 on the ground, 0 airborne, NaN unread.
        if (fieldName == OnGroundField)
            return IsOnGround() switch { true => 1, false => 0, null => double.NaN };

        // The speed-brake lever comes from SimConnect too (main's L:switch_498_a) — served
        // ahead of the CdaReady gate like N2.
        if (fieldName == SpeedbrakeLeverState.LeverField) return SpeedbrakeLeverPos();

        // Gear-leg positions come from SimConnect too (stock GEAR x POSITION, percent) —
        // served ahead of the CdaReady gate for the same reason as N2.
        int leg = GearLegIndex(fieldName);
        if (leg >= 0) return System.Threading.Volatile.Read(ref _gearPct[leg]);

        // Landing gear confirmed UP / DOWN from the lever AND the physical leg positions
        // (Pmdg777GearConfirmation). NaN until the lever (CDA) and all three legs are known.
        if (fieldName == Pmdg777GearConfirmation.UpField)   return Pmdg777GearConfirmation.UpValue(GetValue);
        if (fieldName == Pmdg777GearConfirmation.DownField) return Pmdg777GearConfirmation.DownValue(GetValue);

        // Cockpit Prep's power-up check: battery, ADIRU, demand pumps OFF, nav lights and gear
        // lever DOWN all as their Preflight lines say (Pmdg777PowerUpCheck). Every input is a CDA
        // field, so it is NaN until the first snapshot.
        if (fieldName == Pmdg777PowerUpCheck.Field) return Pmdg777PowerUpCheck.Value(GetValue);

        // Two-switch writes read back on BOTH switches (a Multi step verifies one field, after its
        // last write, so a dropped first write passed): the runway turnoff lights and the fuel
        // control switches, each as a count of the pair, NaN until both are read.
        if (fieldName == RunwayTurnoffLightsOnField)
            return CountOn(GetValue, "LTS_RunwayTurnoff_Sw_ON_0", "LTS_RunwayTurnoff_Sw_ON_1");
        if (fieldName == FuelControlsAtRunField)
            return CountOn(GetValue, "ENG_FuelControl_Sw_RUN_0", "ENG_FuelControl_Sw_RUN_1");

        // The recirculation fans of the loaded variant (Pmdg777RecircFans): NaN until the CDA says
        // which 777 this is.
        if (fieldName == Pmdg777RecircFans.OnField) return Pmdg777RecircFans.OnValue(GetValue);

        if (!CdaReady) return double.NaN;
        try { return _dm?.GetFieldValue(fieldName) ?? double.NaN; }
        catch { return double.NaN; }
    }

    // First CDA snapshot received — see GetValue.
    private bool CdaReady => _dm is { IsReady: true };

    /// <summary>First CDA snapshot received (public form of the private CdaReady). Consumers:
    /// the center-pump adapter's dataReady gate (F13).</summary>
    public bool IsDataReady => CdaReady;

    // Raw CDA read for composing synthetic fields. Only call when CdaReady.
    private bool RawOn(string fieldName)
    {
        try { return (_dm?.GetFieldValue(fieldName) ?? 0) > 0.5; }
        catch { return false; }
    }

    /// <summary>Returns true if the given field is non-zero.</summary>
    public bool IsOn(string fieldName) => GetValue(fieldName) > 0.5;

    /// <summary>True only when the field has been READ and is 0. Use it for "already off",
    /// never <c>!IsOn</c>: that reads an unread (NaN) switch as off, and an OFF step said
    /// "Already set" and ticked its line before the first CDA snapshot.</summary>
    public bool IsOff(string fieldName) => IsPosition(fieldName, 0);

    /// <summary>True once the field has a reading (not NaN). A TOGGLE is never pressed on an
    /// unread state: the press flips whatever the switch really is.</summary>
    public bool IsRead(string fieldName) => !double.IsNaN(GetValue(fieldName));

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
    public bool IsApuGenOn()          => IsOn("ELEC_APUGen_Sw_ON");
    public bool IsBusTie1Auto()       => IsOn("ELEC_BusTie_Sw_AUTO_0");
    public bool IsBusTie2Auto()       => IsOn("ELEC_BusTie_Sw_AUTO_1");
    public bool IsGen1On()            => IsOn("ELEC_Gen_Sw_ON_0");   // MAIN generator control switches
    public bool IsGen2On()            => IsOn("ELEC_Gen_Sw_ON_1");
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
    public bool IsEitherCenterPumpOn() => IsFuelPump1CtrOn() || IsFuelPump2CtrOn();
    // Before Start "Fuel pumps: ON" exactly as its line reads it (FO_FUEL_PUMPS_BS_OK: wing on AND
    // centre matching the centre fuel); false while unread. The flow's "Already set" uses this, never
    // the wing pumps alone, which left the centre pumps off over a fuelled centre tank.
    public bool AreBeforeStartFuelPumpsOk() => IsPosition("FO_FUEL_PUMPS_BS_OK", 1);
    // All six pump switches OFF. NaN-safe (IsPosition): "!IsOn" would read an unread switch as
    // OFF, and Cockpit Prep's pumps-off step would say "Already set" before the first CDA snapshot.
    public bool AreAllFuelPumpsOff() =>
        IsPosition("FUEL_PumpFwd_Sw_0", 0) && IsPosition("FUEL_PumpFwd_Sw_1", 0) &&
        IsPosition("FUEL_PumpAft_Sw_0", 0) && IsPosition("FUEL_PumpAft_Sw_1", 0) &&
        IsPosition("FUEL_PumpCtr_Sw_0", 0) && IsPosition("FUEL_PumpCtr_Sw_1", 0);

    // Fuel Control: 0=CUTOFF, 1=RUN in PMDG data
    public bool IsEng1FuelControlRun()    => IsOn("ENG_FuelControl_Sw_RUN_0");
    public bool IsEng2FuelControlRun()    => IsOn("ENG_FuelControl_Sw_RUN_1");
    public bool AreFuelControlsCutoff()   => !IsEng1FuelControlRun() && !IsEng2FuelControlRun();

    /// <summary>Synthetic: how many fuel control switches are at RUN (0, 1 or 2), NaN until both are
    /// read. The CUTOFF steps verify it at 0, so a dropped write to either switch is heard skipped.</summary>
    public const string FuelControlsAtRunField = "FO_FUEL_CONTROLS_RUN";

    /// <summary>How many of <paramref name="fields"/> read on (above 0.5); NaN while any is unread.</summary>
    public static double CountOn(Func<string, double> read, params string[] fields)
    {
        int on = 0;
        foreach (string field in fields)
        {
            double v = read(field);
            if (double.IsNaN(v)) return double.NaN;
            if (v > 0.5) on++;
        }
        return on;
    }

    // -----------------------------------------------------------------------
    // Engines
    // -----------------------------------------------------------------------

    public int Eng1StartSelector() => (int)Math.Round(GetValue("ENG_Start_Selector_0"));
    public int Eng2StartSelector() => (int)Math.Round(GetValue("ENG_Start_Selector_1"));

    /// <summary>The engine (1 or 2) is RUNNING: its N2 read at or above <see cref="EngineRunningN2"/>
    /// AND its fuel control switch at RUN. Engine Start does not start such an engine again (a re-run
    /// after a stop at engine 1, or an engine started by hand). An engine coasting down after a cutoff
    /// still shows N2 but not RUN, so it is never taken for a running one.</summary>
    public bool IsEngineRunning(int engine) => engine switch
    {
        1 => EngineRunning(GetValue("FO_ENG1_N2"), GetValue("ENG_FuelControl_Sw_RUN_0")),
        2 => EngineRunning(GetValue("FO_ENG2_N2"), GetValue("ENG_FuelControl_Sw_RUN_1")),
        _ => false,
    };

    /// <summary>The test of <see cref="IsEngineRunning"/>: N2 at or above <see cref="EngineRunningN2"/>
    /// and the fuel control switch at RUN (1); false while either is unread (NaN).</summary>
    public static bool EngineRunning(double n2, double fuelControlRun) =>
        n2 >= EngineRunningN2 && fuelControlRun > 0.5;

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

    /// <summary>Synthetic: how many runway turnoff lights are on (0, 1 or 2), NaN until both are read.
    /// The turnoff steps write each side and read both back: 2 for ON, 0 for OFF.</summary>
    public const string RunwayTurnoffLightsOnField = "FO_TURNOFF_LIGHTS_ON";

    public bool IsTaxiOn()           => IsOn("LTS_Taxi_Sw_ON");
    public bool IsStormOn()          => IsOn("LTS_Storm_Sw_ON");
    public bool IsLogoOn()           => IsOn("LTS_Logo_Sw_ON");
    public bool IsWingLightsOn()     => IsOn("LTS_Wing_Sw_ON");

    // -----------------------------------------------------------------------
    // Signs
    // -----------------------------------------------------------------------

    public int SeatBeltsSelector()   => (int)Math.Round(GetValue("SIGNS_SeatBeltsSelector"));
    // NaN-safe (IsPosition): SeatBeltsSelector() == 0 would read an unread selector as OFF,
    // because (int)Math.Round(NaN) is 0, and Cockpit Prep's OFF step would say "Already set".
    public bool AreSeatBeltsOff()        => IsPosition("SIGNS_SeatBeltsSelector", 0);
    public int NoSmokingSelector()   => (int)Math.Round(GetValue("SIGNS_NoSmokingSelector"));
    // EmerLightsSelector: 0=Off (guard open), 1=Armed, 2=On
    public int EmerLightsSelector()  => (int)Math.Round(GetValue("LTS_EmerLightsSelector"));

    // -----------------------------------------------------------------------
    // Flight controls / pedestal
    // -----------------------------------------------------------------------

    // FlapsLever: 0=UP, 1=1, 2=5, 3=15, 4=20, 5=25, 6=30 (PMDG positions)
    // NaN-safe: (int)Math.Round(NaN) is 0 under .NET's saturating float->int conversion, so a
    // rounded "== 0" test read "flaps up" before the first CDA snapshot and the After
    // Takeoff flaps steps said "Already set" over data that had not arrived.
    public bool AreFlapsUp()              => IsPosition("FCTL_Flaps_Lever", 0);
    // The read-back lines' own lever ranges — "Flaps: Set for takeoff" (BTKOF_FLAPS) and "Flaps:
    // Set for landing" (LDG_FLAPS) — shared with the flows' read-only lever checks and their
    // "Already set", so the three can never disagree. False for an unread lever (NaN). The takeoff
    // range used to stop at 3 here, as the line once did (Pmdg777TakeoffFlapsTests), and to start
    // at 1 until 2026-10-18: flaps 1 is not a 777 takeoff setting, so a lever left at 1 was
    // confirmed "set for takeoff".
    public static bool IsTakeoffFlapLever(double lever) => lever >= 2 && lever <= 4;   // flaps 5, 15 or 20
    public static bool IsLandingFlapLever(double lever) => lever >= 4;                 // flaps 20, 25 or 30
    public bool AreFlapsForTakeoff()      => IsTakeoffFlapLever(GetValue("FCTL_Flaps_Lever"));
    public bool AreFlapsForLanding()      => IsLandingFlapLever(GetValue("FCTL_Flaps_Lever"));

    // The speed-brake lever is main's L:switch_498_a (0 / 200 / 300 / 400), never the SDK's
    // truncating FCTL_Speedbrake_Lever byte — see Pmdg777SpeedbrakeLever. NaN until read.
    public double SpeedbrakeLeverPos()    => _cachedValue?.Invoke(SpeedbrakeLeverState.Pmdg777.LeverKey) ?? double.NaN;
    public bool IsSpeedbrakeDown()        => Pmdg777SpeedbrakeLever.IsDown(SpeedbrakeLeverPos());
    public bool IsSpeedbrakeArmed()       => Pmdg777SpeedbrakeLever.IsArmed(SpeedbrakeLeverPos());
    public bool IsSpeedbrakeDeployed()    => Pmdg777SpeedbrakeLever.IsDeployed(SpeedbrakeLeverPos());

    // -----------------------------------------------------------------------
    // Gear / brakes
    // -----------------------------------------------------------------------

    // GearLever: 0=Up, 1=Down
    public bool IsGearDown()             => IsPosition("GEAR_Lever", 1);
    public bool IsGearUp()               => IsPosition("GEAR_Lever", 0);

    // The CDA carries no air/ground field: SIM ON GROUND is the base definition's stock
    // SIM_ON_GROUND (1 Hz batch), read from SimConnect's cache like the speed-brake lever.
    public const string OnGroundKey = "SIM_ON_GROUND";

    /// <summary>True on the ground, false airborne, null before SIM ON GROUND has been read.</summary>
    public bool? IsOnGround() =>
        _cachedValue?.Invoke(OnGroundKey) is double v && !double.IsNaN(v) ? v > 0.5 : null;

    /// <summary>Synthetic: <see cref="IsOnGround"/> as 1 / 0 / NaN, for a flow's on-ground guard.</summary>
    public const string OnGroundField = "FO_ON_GROUND";

    /// <summary>Synthetic: both engines below 10 % N2 (1), either above (0), NaN until both are read.</summary>
    public const string EnginesStoppedField = "FO_ENGINES_STOPPED";

    /// <summary>The gear lever goes UP only when this is true, SIM ON GROUND READ as airborne (the
    /// MD-11's rule): on the ground, or unread, it stays where it is.</summary>
    public bool IsDefinitelyAirborne() => IsOnGround() == false;

    /// <summary>Spoken when the gear-up step or line leaves the lever alone.</summary>
    public const string GearUpLeaveAloneText = "Not airborne, so the gear stays down.";

    /// <summary>Spoken when After Takeoff's flaps-up step or line leaves the lever alone on the ground
    /// (After Landing's flaps up is a ground step and is never refused).</summary>
    public const string FlapsUpLeaveAloneText = "Not airborne, so the flaps stay where they are.";

    // Gear CONFIRMED from the lever AND the physical leg positions (Pmdg777GearConfirmation);
    // false (never true) while any reading is unknown.
    public bool IsGearConfirmedUp()      => IsPosition(Pmdg777GearConfirmation.UpField, 1);
    public bool IsGearConfirmedDown()    => IsPosition(Pmdg777GearConfirmation.DownField, 1);

    // Autobrake (PMDG_777X_SDK.h line 272): 0=RTO, 1=Off, 2=Disarm, 3="1", 4="2",
    // 5="3", 6="4", 7=Max Auto. The DISARM detent used to be missing from this map;
    // only 0 and 1 are used below, and both survived the correction unchanged.
    public int AutobrakeSelector()       => (int)Math.Round(GetValue("BRAKES_AutobrakeSelector"));
    // NaN-safe (IsPosition): AutobrakeSelector() == 0 read RTO before the first CDA snapshot,
    // because (int)Math.Round(NaN) is 0, and the RTO steps said "Already set" (AreFlapsUp's fix).
    public bool IsAutobrakRTO()          => IsPosition("BRAKES_AutobrakeSelector", 0);
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

    // Battery, ADIRU, demand pumps OFF, nav lights and gear lever DOWN as their Preflight lines say
    // (Pmdg777PowerUpCheck); false (never true) while any reading is unknown.
    public bool IsPowerUpConfirmed()     => IsPosition(Pmdg777PowerUpCheck.Field, 1);

    // -----------------------------------------------------------------------
    // EFIS / Baro
    // -----------------------------------------------------------------------

    // The 777 SDK has no STD-mode field. EFIS_BaroSTD_Sw_Pushed[2] is listed under "EFIS
    // MOMENTARY action" (PMDG_777X_SDK.h): it reads 0 at rest whatever the mode, so reading it
    // as the mode made the descent push nothing while "set to local QNH" was spoken, and the
    // climb push a blind toggle. STD is read as the panel's own altimeter readout reads it
    // (commit 1be55388): the definition's KOHLSMAN SETTING HG cache, at 29.92 inHg. That reading
    // cannot tell STD from a QNH of 29.92 (the US standard day, the MSFS preset weather), so the
    // phase monitor pushes STD only from a reading that is NOT 29.92 and takes back only its own
    // push (Pmdg777TransitionStd). There is no FO-side reading: the FO's STD goes with the Captain's.
    public const string AltimeterSettingKey = "ALTIMETER_SETTING";

    /// <summary>True when the Captain's altimeter reads 29.92 inHg (STD, or a QNH of 29.92: the two
    /// cannot be told apart), false when it reads anything else, null before it has been read.</summary>
    public bool? IsBaroStdCapt() => BaroReadsStandard(_cachedValue?.Invoke(AltimeterSettingKey));

    /// <summary>The STD test of <see cref="IsBaroStdCapt"/>: null for no reading, else whether
    /// it is 29.92 inHg (the definition's own 0.005 tolerance).</summary>
    public static bool? BaroReadsStandard(double? inHg) =>
        inHg is double v && !double.IsNaN(v) ? Math.Abs(v - 29.92) < 0.005 : null;

    /// <summary>True when any landing light (L, N, or R) is on.</summary>
    public bool AreLandingLightsOn() =>
        IsLandingLightLOn() || IsLandingLightROn() || IsLandingLightNOn();

    /// <summary>True when all three landing lights (L, N and R) are on: Before Takeoff's
    /// "Landing lights: ON" line (BTKO_LANDING) reads all three, so its step is "Already set"
    /// only then, never when one of them is.</summary>
    public bool AreAllLandingLightsOn() =>
        IsLandingLightLOn() && IsLandingLightROn() && IsLandingLightNOn();

    /// <summary>True when every landing light has been READ as off (NaN-safe, see IsOff).</summary>
    public bool AreAllLandingLightsOff() =>
        IsOff("LTS_LandingLights_Sw_ON_0") && IsOff("LTS_LandingLights_Sw_ON_1") && IsOff("LTS_LandingLights_Sw_ON_2");

    /// <summary>True when both runway turnoff lights are on: the Before Takeoff line
    /// (BTKO_TURNOFF) reads both.</summary>
    public bool AreBothRunwayTurnoffLightsOn() => IsRwyTurnoffLOn() && IsRwyTurnoffROn();

    /// <summary>True when both runway turnoff lights have been READ as off (NaN-safe).</summary>
    public bool AreBothRunwayTurnoffLightsOff() =>
        IsOff("LTS_RunwayTurnoff_Sw_ON_0") && IsOff("LTS_RunwayTurnoff_Sw_ON_1");

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

    private const int NoPlanTakeoffFlaps = 5; // default — common 777 takeoff setting
    private int _takeoffFlaps = NoPlanTakeoffFlaps;

    /// <summary>Store the takeoff flap setting from SimBrief perf data; 0 or less (a plan
    /// without one, or a plan forgotten) restores the no-plan default.</summary>
    public void SetTakeoffFlaps(int flaps) => _takeoffFlaps = flaps > 0 ? flaps : NoPlanTakeoffFlaps;

    /// <summary>
    /// Store both engines' N2, fed by the shared FirstOfficerForm timer for every profile.
    /// Read back as the synthetic FO_ENG{1,2}_N2 fields — the ENGINE_START selector items
    /// latch off them, because the start selector springs back at cutout and so cannot be
    /// detected from its own position.
    /// </summary>
    public void SetEngineN2(double eng1N2, double eng2N2)
    {
        System.Threading.Volatile.Write(ref _eng1N2, eng1N2);
        System.Threading.Volatile.Write(ref _eng2N2, eng2N2);
    }

    /// <summary>
    /// Interface parity with the 737 (which sets pressurization FLT/LAND ALT from the
    /// SimBrief plan). The 777's pressurization is automatic (FMC landing altitude) —
    /// nothing to store.
    /// </summary>
    public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }

    /// <summary>
    /// Returns the SimBrief planned takeoff flap setting (degrees: 1, 5, 15, 20, 25, 30).
    /// Defaults to 5 if no SimBrief OFP has been loaded.
    /// </summary>
    public int GetTakeoffFlaps() => _takeoffFlaps;

    // -----------------------------------------------------------------------
    // Fuel quantities (for informational use)
    // -----------------------------------------------------------------------

    public int FuelLeftLbs()             => FuelSystemLogic.SafeRoundToInt(GetValue("FUEL_QtyLeft"));
    public int FuelCenterLbs()           => FuelSystemLogic.SafeRoundToInt(GetValue("FUEL_QtyCenter"));
    public int FuelRightLbs()            => FuelSystemLogic.SafeRoundToInt(GetValue("FUEL_QtyRight"));
    public int TotalFuelLbs()            => FuelLeftLbs() + FuelCenterLbs() + FuelRightLbs();
}
