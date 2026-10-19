using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.DA40;

using E = DA40FoStateEvaluator;
using X = DA40FoActionExecutor;
using R = DA40FoReadback;

/// <summary>
/// The COWS DA40's First Officer procedures, phase by phase, for both airframes. They follow the
/// aircraft's OWN checklist pages, which COWS edited for the simulator (Before engine start through
/// Shutdown), in their order, with the AFM's normal procedures where the page is terse (DA40-18).
///
/// What the First Officer does and what stays the Captain's (FO-2, DA40-8): the First Officer sets
/// switches, runs the composite steps the checklist spells out (the flap travel test, the voter swap,
/// the ECU test, the magneto check, the propeller cycle, the XLS priming and start) and checks what it
/// can read. It never moves the power lever or throttle except where the page gives a position for the
/// start (the XLS priming and starting positions, which COWS says to repeat exactly), never sets a
/// transponder code, an altimeter value, a frequency or the flight plan, and never decides: a lever,
/// a value or a judgement ("as required", "if required") is a Captain item, and a lever the page asks
/// to be checked is waited for, never moved.
/// </summary>
internal static class DA40FoProcedures
{
    // Conditions on one field.
    private static bool On(double v) => v > 0.5;
    private static bool Off(double v) => v < 0.5;
    private static Func<double, bool> Is(int n) => v => Math.Abs(v - n) < 0.5;
    private static bool AtOrAbove(double v, double limit) => v >= limit;

    /// <summary>The flap travel the test waits for: LDG reads at least this percent, UP at most
    /// <see cref="FlapUpTravelPercent"/>.</summary>
    internal const double FlapLdgTravelPercent = 95, FlapUpTravelPercent = 5;

    /// <summary>How long a check waits for the pilot's own lever (the power lever to idle).</summary>
    internal const int PilotLeverWaitSeconds = 30;

    /// <summary>The XLS priming time: the COWS page's "3-5 seconds", the middle of it.</summary>
    internal const int ColdPrimeSeconds = 4;
    /// <summary>The reprime: "1-2 seconds".</summary>
    internal const int ReprimeSeconds = 2;

    /// <summary>XLS throttle positions for the start, percent of travel. The priming mark is half
    /// open; the starting position is a quarter inch open (docs/da40-xls-variables.md, measured);
    /// the flooded start's "Half" is the priming mark.</summary>
    internal const int XlsThrottlePrimingPercent = 50, XlsThrottleStartingPercent = 12, XlsThrottleHalfPercent = 50;

    /// <summary>The run-up RPM the XLS's propeller cycle and magneto check are made at, and the
    /// band the check accepts.</summary>
    internal const double XlsRunUpRpmLow = 1900, XlsRunUpRpmHigh = 2100;

    /// <summary>Settle between the ECU voter positions and the magneto positions: long enough for
    /// the RPM to show its bump or its drop (the definition's magneto check settles in 2 s).</summary>
    internal const int SettleSeconds = 3;

    /// <summary>One propeller cycle and its RPM recovery: the definition's cycle ends within 5 s.</summary>
    internal const int PropCycleSeconds = 8;

    internal static List<DA40FoPhase> Build(DA40Variant variant)
        => variant == DA40Variant.NG ? Ng() : Xls();

    // =======================================================================
    // DA40-NG
    // =======================================================================

    private static List<DA40FoPhase> Ng() => new()
    {
        NgBeforeStart(), NgEngineStart(), NgAfterStart(), Taxi(DA40Variant.NG), NgBeforeTakeoff(),
        Lineup(DA40Variant.NG), NgClimb(), NgCruise(), NgDescent(), NgApproach(), Final(DA40Variant.NG),
        NgAfterLanding(), NgShutdown(),
    };

    private static DA40FoPhase NgBeforeStart()
    {
        var p = new DA40FoPhase("BEFORE_START", "Before Engine Start",
            "Cold cockpit checks, electric master on, the flap travel test, fuel.");
        EngineStoppedGuard(p, "BS_ENGINE_STOPPED");
        Trim(p);
        PowerLeverIdle(p, "BS", "Power lever", R.BscPowerIdle);
        p.Set("BS_PARKBRK", "Parking brake: set", "DA40_BRAKE_PARK", 1, "DA40_BRAKE_PARK", On, R.BscParkBrake);
        p.Set("BS_ALT_AIR", "Alternate air: closed", "DA40_ICE_ALTERNATE_AIR", 0, "DA40_ICE_ALTERNATE_AIR", Off, R.BscAltAir);
        p.Set("BS_AVIONICS_OFF", "Avionics master: off", "DA40_ELEC_AVIONICS_MASTER", 0, "DA40_ELEC_AVIONICS_MASTER", Off,
                R.BscAvionicsEss)
         .LeaveAlone(s => s.IsOn("DA40_ELEC_MASTER_BATTERY"),
                "Avionics master stays as it is: the electric master is already on.");
        p.Set("BS_ESS_OFF", "ESS bus: off", "DA40_ELEC_ESS_BUS", 0, "DA40_ELEC_ESS_BUS", Off, R.BscAvionicsEss);
        p.Set("BS_ALT_STATIC", "Alternate static: closed", "DA40_ICE_ALTERNATE_STATIC", 0, "DA40_ICE_ALTERNATE_STATIC", Off,
            R.BscAltStatic);
        p.Set("BS_ENG_MASTER_OFF", "Engine master: off", "DA40_START_ENGINE_MASTER", 0, "DA40_START_ENGINE_MASTER", Off,
                R.BscStartSwitches)
         .LeaveAlone(NotStopped, "Engine master stays as it is: the engine is not read as stopped.");
        p.Set("BS_FUEL_PUMP_OFF", "Fuel pump: off", "DA40_FUEL_PUMPS", 0, "DA40_FUEL_PUMPS", Off, R.BscStartSwitches);
        p.Set("BS_VOTER", "ECU voter: auto", "DA40_ECU_VOTER", 1, "DA40_ECU_VOTER", Is(1), R.BscStartSwitches);
        AllLightsOff(p);
        p.Check("BS_BREAKERS", "Circuit breakers: all in", E.BreakersInField, On, 5, R.BscBreakers);
        p.Set("BS_FLAPS_UP", "Flaps: up", "DA40_FLAPS_POSITION", 0, "DA40_FLAPS_POSITION", Is(0), R.BscFlapsUp);
        p.Set("BS_PITOT_OFF", "Pitot heat: off", "DA40_ICE_PITOT_HEAT", 0, "DA40_ICE_PITOT_HEAT", Off, R.BscPitotXfer);
        p.Set("BS_XFER_OFF", "Fuel transfer: off", "DA40_FUEL_TRANSFER", 0, "DA40_FUEL_TRANSFER", Off, R.BscPitotXfer);
        p.Set("BS_ELEC_ON", "Electric master: on", "DA40_ELEC_MASTER_BATTERY", 1, "DA40_ELEC_MASTER_BATTERY", On,
            R.BscElecOn);
        PowerUpTail(p, "Fuel quantity, temperature and totaliser: check and set");
        return p;
    }

    private static DA40FoPhase NgEngineStart()
    {
        var p = new DA40FoPhase("ENGINE_START", "Engine Start",
            "Doors, strobe, engine master, glow, the start, oil pressure, the electrical check.");
        EngineStoppedGuard(p, "ES_ENGINE_STOPPED");
        DoorsAndStrobe(p, R.EscDoors, R.EscStrobe);
        PowerLeverIdle(p, "ES", "Power lever", R.EscPowerIdle);
        p.Captain("ES_PROP_CLEAR", "Propeller area: clear front and back");
        // The guard over the engine master: opened if closed; not on the COWS page, flow-only.
        p.Move("ES_MASTER_GUARD", "Engine master guard: open", "DA40_START_ENGINE_MASTER_COVER", 1);
        p.Steps[^1].SkipCondition = s => s.IsOn("DA40_START_ENGINE_MASTER_COVER");
        p.Set("ES_ENG_MASTER", "Engine master: on", "DA40_START_ENGINE_MASTER", 1, "DA40_START_ENGINE_MASTER", On,
            R.EscEngineMaster);
        // The glow lamp comes on with the master; read before it has had a second to, it reads off.
        p.Wait("ES_GLOW_SETTLE", "Glow plugs coming on", 3);
        // "Wait for GLOW ON to extinguish": a glow light that never goes out stops the flow before the start.
        p.Check("ES_GLOW", "Glow indication: off", "DA40_START_GLOW_ON", Off, 60, R.EscGlow).StopOnFailure();
        p.Run("ES_CRANK", "Start key: start, held until 500 RPM", X.CrankKey, 1, E.EngineRunningField, On,
                FlowStepFailurePolicy.Stop, R.EscStartKey)
         .LeaveAlone(NotStopped, "Not starting: the engine is not read as stopped.");
        p.Check("ES_OIL", "Oil pressure: out of the red", E.OilPressureOutOfRedField, On, 5, R.EscOilPressure);
        p.Check("ES_VOLTS", "Voltage: in the green", "DA40_ELEC_BUS_MAIN_VOLT", v => v >= 25, 15, R.EscElectrical);
        p.Check("ES_BREAKERS", "Circuit breakers: all in", E.BreakersInField, On, 5, R.EscElectrical);
        p.Captain("ES_LOAD", "Electrical load: check");
        return p;
    }

    private static DA40FoPhase NgAfterStart()
    {
        var p = new DA40FoPhase("AFTER_START", "After Engine Start",
            "Oil pressure, idle RPM, the pitot heat test, avionics, altimeters, the standby horizon, flaps T/O.");
        p.Check("AS_OIL", "Oil pressure: green", E.OilPressureGreenField, On, 60, R.AscOilPressure);
        p.Check("AS_RPM", "Idle RPM: 710, plus or minus 30", E.IdleRpmField, On, 30, R.AscIdleRpm);
        p.Captain("AS_LIGHTS", "Lights: as required");
        PitotTest(p);
        p.Set("AS_AVIONICS", "Avionics master: on", "DA40_ELEC_AVIONICS_MASTER", 1, "DA40_ELEC_AVIONICS_MASTER", On,
            R.AscAvionics);
        p.Captain("AS_XPDR", "Transponder: mode and code");
        Altimeters(p, "AS", R.AscAltimeters);
        p.Run("AS_CAGE", "Standby horizon: cage", "DA40_STBY_GYRO_CAGE", 1, cl: R.AscCage);
        AutopilotTest(p);
        p.Set("AS_FLAPS_TO", "Flaps: T/O", "DA40_FLAPS_POSITION", 1, "DA40_FLAPS_POSITION", Is(1), R.AscFlapsTo);
        p.Captain("AS_IFR", "IFR departure or heavy traffic: run the before take-off checklist now if required");
        return p;
    }

    private static DA40FoPhase NgBeforeTakeoff()
    {
        var p = new DA40FoPhase("BEFORE_TAKEOFF", "Before Take-off",
            "Parking brake, doors, breakers, temperatures, the voter check, the ECU test, fuel pump on.");
        p.Set("BT_PARKBRK", "Parking brake: set", "DA40_BRAKE_PARK", 1, "DA40_BRAKE_PARK", On, R.BtcParkBrake);
        p.Check("BT_DOORS", "Doors: closed, no warning", E.DoorsClosedField, On, 5, R.BtcDoors);
        p.Check("BT_BREAKERS", "Circuit breakers: all in", E.BreakersInField, On, 5, R.BtcBreakers);
        PowerLeverIdle(p, "BT", "Power lever", R.BtcPowerIdle);
        p.Check("BT_TEMPS", "Engine temperatures: green", E.EngineTempsField, On, 10, R.BtcTemps);
        // "Voter switch: A, Auto, B, Auto. Check for rpm bump": each position held long enough to hear it.
        p.Move("BT_VOTER_A", "ECU voter: A", "DA40_ECU_VOTER", 2);
        p.Wait("BT_VOTER_A_WAIT", "RPM on ECU A", SettleSeconds);
        p.Move("BT_VOTER_AUTO1", "ECU voter: auto", "DA40_ECU_VOTER", 1);
        p.Wait("BT_VOTER_AUTO1_WAIT", "RPM on auto", SettleSeconds);
        p.Move("BT_VOTER_B", "ECU voter: B", "DA40_ECU_VOTER", 0);
        p.Wait("BT_VOTER_B_WAIT", "RPM on ECU B", SettleSeconds);
        p.Move("BT_VOTER_AUTO", "ECU voter: auto", "DA40_ECU_VOTER", 1, "DA40_ECU_VOTER", Is(1), R.BtcVoter);
        p.Run("BT_ECU_TEST", "ECU test: press and hold until complete", X.EcuTestKey, 1, cl: R.BtcEcuTest);
        p.Check("BT_ECU_PASS", "ECU test: no errors", E.EcuTestPassedField, On, 5, R.BtcEcuTest)
         .Requires("BT_ECU_TEST", "Skipping the ECU test result: the test did not complete.");
        p.Set("BT_FUEL_PUMP", "Fuel pump: on", "DA40_FUEL_PUMPS", 1, "DA40_FUEL_PUMPS", On, R.BtcFuelPump);
        p.Captain("BT_CHECKED", "Trim, flaps and flight controls: checked");
        return p;
    }

    private static DA40FoPhase NgClimb()
    {
        var p = new DA40FoPhase("AFTER_TAKEOFF", "Climb", "Flaps up, climb power, fuel pump off.");
        p.Set("CL_FLAPS_UP", "Flaps: up", "DA40_FLAPS_POSITION", 0, "DA40_FLAPS_POSITION", Is(0), R.ClcFlapsUp);
        p.Captain("CL_POWER", "Climb power: 92 percent");
        p.Set("CL_FUEL_PUMP_OFF", "Fuel pump: off", "DA40_FUEL_PUMPS", 0, "DA40_FUEL_PUMPS", Off, R.ClcFuelPump);
        p.Captain("CL_LIGHTS", "Lights: as required");
        p.Captain("CL_ENGINE", "Engine instruments: monitor");
        return p;
    }

    private static DA40FoPhase NgCruise()
    {
        var p = new DA40FoPhase("CRUISE", "Cruise", "Cruise power, fuel balance, alternate air.");
        p.Captain("CR_POWER", "Cruise power: set, 50 to 92 percent");
        p.Captain("CR_FUEL", "Fuel quantities: check, transfer as required. Maximum difference 9 gallons, set a timer");
        p.Captain("CR_ALT_AIR", "Alternate air: as required");
        p.Captain("CR_ENGINE", "Engine instruments: monitor");
        return p;
    }

    private static DA40FoPhase NgDescent()
    {
        var p = new DA40FoPhase("DESCENT", "Descent", "Power, altimeters.");
        p.Captain("DS_POWER", "Power: set");
        Altimeters(p, "DS", R.DscAltimeters);
        return p;
    }

    private static DA40FoPhase NgApproach()
    {
        var p = new DA40FoPhase("APPROACH", "Approach",
            "Landing data, altimeters, radios, fuel transfer off, parking brake off, fuel pump on.");
        p.Captain("AP_DATA", "Landing data: received");
        Altimeters(p, "AP", R.ApcAltimeters);
        p.Captain("AP_RADIOS", "COM, NAV and FMS: set");
        p.Set("AP_XFER_OFF", "Fuel transfer: off", "DA40_FUEL_TRANSFER", 0, "DA40_FUEL_TRANSFER", Off, R.ApcXferOff);
        p.Set("AP_PARKBRK_OFF", "Parking brake: off", "DA40_BRAKE_PARK", 0, "DA40_BRAKE_PARK", Off, R.ApcParkBrakeOff);
        p.Set("AP_FUEL_PUMP", "Fuel pump: on", "DA40_FUEL_PUMPS", 1, "DA40_FUEL_PUMPS", On, R.ApcFuelPump);
        p.Captain("AP_LIGHTS", "Lights: as required");
        return p;
    }

    private static DA40FoPhase NgAfterLanding()
    {
        var p = new DA40FoPhase("AFTER_LANDING", "After Landing",
            "Flaps up, pitot heat off, alternate air closed, transponder standby, fuel pump off.");
        p.Set("AL_FLAPS_UP", "Flaps: up", "DA40_FLAPS_POSITION", 0, "DA40_FLAPS_POSITION", Is(0), R.AlcFlapsUp);
        p.Set("AL_PITOT_OFF", "Pitot heat: off", "DA40_ICE_PITOT_HEAT", 0, "DA40_ICE_PITOT_HEAT", Off, R.AlcPitotOff);
        p.Set("AL_ALT_AIR", "Alternate air: closed", "DA40_ICE_ALTERNATE_AIR", 0, "DA40_ICE_ALTERNATE_AIR", Off, R.AlcAltAir);
        TransponderStandby(p);
        p.Set("AL_FUEL_PUMP_OFF", "Fuel pump: off", "DA40_FUEL_PUMPS", 0, "DA40_FUEL_PUMPS", Off, R.AlcFuelPumpOff);
        p.Captain("AL_LIGHTS", "Lights: as required");
        return p;
    }

    private static DA40FoPhase NgShutdown()
    {
        var p = new DA40FoPhase("SHUTDOWN", "Shutdown",
            "Parking brake, cool-down, avionics off, lights off, engine master off, electric master off.");
        OnGroundGuard(p, "SD_ON_GROUND");
        p.Set("SD_PARKBRK", "Parking brake: set", "DA40_BRAKE_PARK", 1, "DA40_BRAKE_PARK", On, R.SdcParkBrake);
        p.Wait("SD_COOLDOWN", "Engine cool-down at idle for one minute", 60, R.SdcCooldown);
        p.Set("SD_AVIONICS_OFF", "Avionics master: off", "DA40_ELEC_AVIONICS_MASTER", 0, "DA40_ELEC_AVIONICS_MASTER", Off,
            R.SdcAvionicsOff);
        ExteriorLightsOff(p);
        p.Captain("SD_CONSUMERS", "Electrical consumers: off");
        p.Set("SD_ENG_MASTER_OFF", "Engine master: off", "DA40_START_ENGINE_MASTER", 0, "DA40_START_ENGINE_MASTER", Off,
                R.SdcEngineMasterOff)
         .LeaveAlone(NotOnGround, "Engine master stays on: the aircraft is not read on the ground.");
        // "Premature shutdown will corrupt ECU data and cause a latched ECU failure": the electric
        // master waits for the engine to stop.
        p.Check("SD_ENGINE_STOPPED", "Engine: stopped", E.EngineStoppedField, On, 60, R.SdcEngineStopped);
        p.Wait("SD_ECU_SETTLE", "Engine instruments crossing out", 5);
        p.Set("SD_STROBE_OFF", "Strobe: off", "DA40_LIGHT_STROBE", 0, "DA40_LIGHT_STROBE", Off, R.SdcStrobeOff);
        p.Set("SD_ELEC_OFF", "Electric master: off", "DA40_ELEC_MASTER_BATTERY", 0, "DA40_ELEC_MASTER_BATTERY", Off,
                R.SdcElecOff)
         .Requires("SD_ENGINE_STOPPED", "Electric master stays on: the engine was not seen to stop.")
         .LeaveAlone(NotOnGround, "Electric master stays on: the aircraft is not read on the ground.");
        CabinLightsOff(p);
        return p;
    }

    // =======================================================================
    // DA40-XLS
    // =======================================================================

    private static List<DA40FoPhase> Xls() => new()
    {
        XlsBeforeStart(), XlsColdStart(), XlsHotStart(), XlsReprimeStart(), XlsAfterStart(), Taxi(DA40Variant.XLS),
        XlsBeforeTakeoff(), Lineup(DA40Variant.XLS), XlsClimb(), XlsCruise(), XlsDescent(), XlsApproach(),
        Final(DA40Variant.XLS), XlsAfterLanding(), XlsShutdown(),
    };

    private static DA40FoPhase XlsBeforeStart()
    {
        var p = new DA40FoPhase("BEFORE_START", "Before Engine Start",
            "Cold cockpit checks, levers, battery master on, the flap travel test, fuel selector to the lowest tank.");
        EngineStoppedGuard(p, "BS_ENGINE_STOPPED");
        Trim(p);
        p.Set("BS_PROP", "Propeller: high RPM", "DA40_XLS_PROP_SET", 100, "DA40_XLS_PROP_SET", v => v >= E.PropHighRpmPercent,
            R.BscLevers);
        p.Set("BS_MIXTURE", "Mixture: cut-off", "DA40_XLS_MIXTURE_SET", 0, "DA40_XLS_MIXTURE_SET", v => v <= E.LeverStopPercent,
                R.BscLevers)
         .LeaveAlone(NotStopped, "Mixture stays where it is: the engine is not read as stopped.");
        p.Captain("BS_THROTTLE", "Throttle: idle", s => s.IsOn(E.PowerIdleField));
        p.Check("BS_LEVERS", "Throttle idle, propeller high RPM, mixture cut-off", E.XlsLeversSetField, On,
            PilotLeverWaitSeconds, R.BscLevers);
        p.Set("BS_PARKBRK", "Parking brake: set", "DA40_BRAKE_PARK", 1, "DA40_BRAKE_PARK", On, R.BscParkBrake);
        p.Set("BS_IGNITION_OFF", "Ignition: off", "DA40_MAG_KEY", 0, "DA40_MAG_KEY", Is(0), R.BscIgnitionOff)
         .LeaveAlone(NotStopped, "Ignition stays as it is: the engine is not read as stopped.");
        p.Set("BS_ALT_AIR", "Alternate air: closed", "DA40_ICE_ALTERNATE_AIR", 0, "DA40_ICE_ALTERNATE_AIR", Off, R.BscAltAir);
        p.Multi("BS_MASTERS_OFF", "Masters: off",
                new[] { ("DA40_ELEC_AVIONICS_MASTER", 0), ("DA40_ELEC_ESS_BUS", 0), ("DA40_ELEC_ALT_MASTER", 0) },
                E.XlsMastersOffField, On, R.BscMastersOff)
         .LeaveAlone(s => s.IsOn("DA40_ELEC_MASTER_BATTERY"),
                "Masters stay as they are: the battery master is already on.");
        p.Set("BS_ALT_STATIC", "Alternate static: closed", "DA40_ICE_ALTERNATE_STATIC", 0, "DA40_ICE_ALTERNATE_STATIC", Off,
            R.BscAltStatic);
        AllLightsOff(p);
        p.Check("BS_BREAKERS", "Circuit breakers: all in", E.BreakersInField, On, 5, R.BscBreakers);
        p.Set("BS_FLAPS_UP", "Flaps: up", "DA40_FLAPS_POSITION", 0, "DA40_FLAPS_POSITION", Is(0), R.BscFlapsUp);
        p.Set("BS_PITOT_OFF", "Pitot heat: off", "DA40_ICE_PITOT_HEAT", 0, "DA40_ICE_PITOT_HEAT", Off, R.BscPitotPump);
        p.Set("BS_FUEL_PUMP_OFF", "Fuel pump: off", "DA40_XLS_FUEL_PUMP", 0, "DA40_XLS_FUEL_PUMP", Off, R.BscPitotPump);
        p.Set("BS_BATTERY_ON", "Battery master: on", "DA40_ELEC_MASTER_BATTERY", 1, "DA40_ELEC_MASTER_BATTERY", On,
            R.BscElecOn);
        PowerUpTail(p, "Fuel quantity and totaliser: check and set");
        p.Provide("BS_SELECTOR", "Fuel selector: lowest tank", "DA40_XLS_FUEL_SELECTOR", s => s.LowerTank(),
            E.SelectorOnLowerTankField, On, "Fuel selector: select the lowest tank", R.BscSelectorLowest);
        return p;
    }

    // The three XLS starts share the COWS Engine start page, whose read-back they all latch.
    private static DA40FoPhase XlsColdStart()
    {
        var p = new DA40FoPhase("ENGINE_START", "Engine Start",
            "Cold start: doors, strobe, prime, the start, oil and fuel pressure, alternator on.");
        EngineStoppedGuard(p, "ES_ENGINE_STOPPED");
        DoorsAndStrobe(p, R.EscDoors, R.EscStrobe);
        p.Set("ES_THROTTLE_PRIME", "Throttle: priming position", "DA40_XLS_THROTTLE_SET", XlsThrottlePrimingPercent,
            "DA40_XLS_THROTTLE_SET", v => Math.Abs(v - XlsThrottlePrimingPercent) <= 3, R.EscColdThrottlePrime);
        p.Set("ES_PUMP_ON", "Fuel pump: on", "DA40_XLS_FUEL_PUMP", 1, "DA40_XLS_FUEL_PUMP", On, R.EscColdPumpOn);
        p.Run("ES_PRIME", $"Mixture: full rich for {ColdPrimeSeconds} seconds, then cut-off", X.PrimeKey, ColdPrimeSeconds,
            cl: new[] { R.EscColdPrimeRich, R.EscColdPrimeCutOff })
         .LeaveAlone(NotStopped, "Not priming: the engine is not read as stopped.");
        XlsCrank(p, XlsThrottleStartingPercent, "Throttle: starting position",
            R.EscColdThrottleStart, R.EscColdPropClear, R.EscColdIgnition, R.EscColdFires);
        XlsStartTail(p);
        return p;
    }

    private static DA40FoPhase XlsHotStart()
    {
        var p = new DA40FoPhase("ENGINE_START_HOT", "Flooded or Hot Engine Start",
            "Flooded or hot start: fuel pump off, mixture cut-off, throttle half, the start.")
        {
            Related = new[] { R.EngineStartPage },
        };
        EngineStoppedGuard(p, "EH_ENGINE_STOPPED");
        DoorsAndStrobe(p, R.EscDoors, R.EscStrobe);
        p.Set("EH_START_PUMP_OFF", "Fuel pump: off", "DA40_XLS_FUEL_PUMP", 0, "DA40_XLS_FUEL_PUMP", Off, R.EscHotPumpOff);
        p.Set("EH_MIXTURE", "Mixture: cut-off", "DA40_XLS_MIXTURE_SET", 0, "DA40_XLS_MIXTURE_SET",
            v => v <= E.LeverStopPercent, R.EscHotMixture)
         .LeaveAlone(NotStopped, "Mixture stays where it is: the engine is not read as stopped.");
        XlsCrank(p, XlsThrottleHalfPercent, "Throttle: half",
            R.EscHotThrottle, R.EscHotPropClear, R.EscHotIgnition, R.EscHotFires, idPrefix: "EH");
        XlsStartTail(p, idPrefix: "EH");
        return p;
    }

    private static DA40FoPhase XlsReprimeStart()
    {
        var p = new DA40FoPhase("ENGINE_START_REPRIME", "Engine Start, Reprime",
            "The engine did not start: fuel pump on, reprime, the start.")
        {
            Related = new[] { R.EngineStartPage },
        };
        EngineStoppedGuard(p, "ER_ENGINE_STOPPED");
        DoorsAndStrobe(p, R.EscDoors, R.EscStrobe);
        p.Set("ER_PUMP_ON", "Fuel pump: on", "DA40_XLS_FUEL_PUMP", 1, "DA40_XLS_FUEL_PUMP", On, R.EscReprimePumpOn);
        p.Set("ER_THROTTLE", "Throttle: starting position", "DA40_XLS_THROTTLE_SET", XlsThrottleStartingPercent,
            "DA40_XLS_THROTTLE_SET", v => Math.Abs(v - XlsThrottleStartingPercent) <= 3, R.EscReprimeThrottle);
        p.Run("ER_PRIME", $"Mixture: full rich for {ReprimeSeconds} seconds, then cut-off", X.PrimeKey, ReprimeSeconds,
            cl: new[] { R.EscReprimeRich, R.EscReprimeCutOff })
         .LeaveAlone(NotStopped, "Not priming: the engine is not read as stopped.");
        XlsCrank(p, null, null, null, R.EscReprimePropClear, R.EscReprimeIgnition, R.EscReprimeFires, idPrefix: "ER");
        XlsStartTail(p, idPrefix: "ER");
        return p;
    }

    private static DA40FoPhase XlsAfterStart()
    {
        var p = new DA40FoPhase("AFTER_START", "After Engine Start",
            "Ground lean, the standby horizon, flaps T/O, the pitot heat test, avionics, altimeters.");
        p.Captain("AS_GROUND_LEAN",
            "Mixture: ground lean. Set 1200 RPM, pull the mixture back slowly until the RPM drops, set 1000 RPM");
        p.Captain("AS_LIGHTS", "Lights: as required");
        p.Run("AS_CAGE", "Standby horizon: cage", "DA40_STBY_GYRO_CAGE", 1, cl: R.AscCage);
        p.Set("AS_FLAPS_TO", "Flaps: T/O", "DA40_FLAPS_POSITION", 1, "DA40_FLAPS_POSITION", Is(1), R.AscFlapsTo);
        PitotTest(p);
        p.Set("AS_AVIONICS", "Avionics master: on", "DA40_ELEC_AVIONICS_MASTER", 1, "DA40_ELEC_AVIONICS_MASTER", On,
            R.AscAvionics);
        p.Captain("AS_XPDR", "Transponder: mode and code");
        Altimeters(p, "AS", R.AscAltimeters);
        AutopilotTest(p);
        p.Captain("AS_IFR", "IFR departure or heavy traffic: run the before take-off checklist now if required");
        return p;
    }

    private static DA40FoPhase XlsBeforeTakeoff()
    {
        var p = new DA40FoPhase("BEFORE_TAKEOFF", "Before Take-off",
            "Parking brake, temperatures, mixture rich, the propeller cycle and the magneto check at 2000 RPM, fuel pump on.");
        p.Captain("BT_WIND", "Aircraft orientation: into wind if possible");
        p.Set("BT_PARKBRK", "Parking brake: set", "DA40_BRAKE_PARK", 1, "DA40_BRAKE_PARK", On, R.BtcParkBrake);
        p.Check("BT_TEMPS", "Engine temperatures: in the green, oil above 100 F", E.EngineTempsField, On, 10, R.BtcTemps);
        p.Check("BT_BREAKERS", "Circuit breakers: all in", E.BreakersInField, On, 5, R.BtcBreakers);
        p.Check("BT_DOORS", "Doors: closed and latched", E.DoorsClosedField, On, 5, R.BtcDoors);
        p.Set("BT_MIXTURE", "Mixture: full rich", "DA40_XLS_MIXTURE_SET", 100, "DA40_XLS_MIXTURE_SET",
            v => v >= E.PropHighRpmPercent, R.BtcMixtureRich);
        p.Captain("BT_THROTTLE", "Throttle: 2000 RPM",
            s => AtOrAbove(s.GetValue(s.RpmKey), XlsRunUpRpmLow) && s.GetValue(s.RpmKey) <= XlsRunUpRpmHigh);
        p.Check("BT_RPM", "RPM: 2000", "DA40_XLS_RPM", v => v >= XlsRunUpRpmLow && v <= XlsRunUpRpmHigh,
            PilotLeverWaitSeconds, R.BtcThrottle2000);
        for (int n = 1; n <= 3; n++)
        {
            p.Move($"BT_PROP_CYCLE{n}", $"Propeller: cycle, {n} of 3", "DA40_XLS_PROP_CYCLE", 1);
            p.Steps[^1].RequiresStepId = "BT_RPM";
            p.Steps[^1].RequiresStepSkipText = "Skipping the propeller cycle: the RPM was not set to 2000.";
            p.Wait($"BT_PROP_CYCLE{n}_WAIT", "Propeller cycling", PropCycleSeconds, n == 3 ? new[] { R.BtcPropCycle } : Array.Empty<string>());
        }
        // "Magnetos: L, Both, R, Both. Max drop 175RPM. Max difference 50RPM": the definition's
        // magneto check speaks each drop.
        MagMove(p, "BT_MAG_L", "Ignition: left", DA40MagnetoCheck.PositionBoth - 1);
        MagMove(p, "BT_MAG_BOTH1", "Ignition: both", DA40MagnetoCheck.PositionBoth);
        MagMove(p, "BT_MAG_R", "Ignition: right", DA40MagnetoCheck.PositionBoth - 2);
        p.Move("BT_MAG_BOTH", "Ignition: both", "DA40_MAG_KEY", DA40MagnetoCheck.PositionBoth, "DA40_MAG_KEY",
            Is(DA40MagnetoCheck.PositionBoth), R.BtcMagnetos);
        p.Captain("BT_THROTTLE_IDLE", "Throttle: idle, confirm smooth running");
        p.Set("BT_FUEL_PUMP", "Fuel pump: on", "DA40_XLS_FUEL_PUMP", 1, "DA40_XLS_FUEL_PUMP", On, R.BtcFuelPump);
        p.Captain("BT_CHECKED", "Trim, flaps and flight controls: checked");
        return p;
    }

    private static DA40FoPhase XlsClimb()
    {
        var p = new DA40FoPhase("AFTER_TAKEOFF", "Climb", "Flaps up, 2400 RPM, fuel pump off, leaning above 3000 feet.");
        p.Set("CL_FLAPS_UP", "Flaps: up", "DA40_FLAPS_POSITION", 0, "DA40_FLAPS_POSITION", Is(0), R.ClcFlapsUp);
        p.Captain("CL_RPM", "RPM: 2400");
        p.Set("CL_FUEL_PUMP_OFF", "Fuel pump: off", "DA40_XLS_FUEL_PUMP", 0, "DA40_XLS_FUEL_PUMP", Off, R.ClcFuelPump);
        p.Captain("CL_LIGHTS", "Lights: as required");
        p.Captain("CL_MIXTURE", "Above 3000 feet: mixture for an EGT of 1350 F at most");
        p.Captain("CL_ENGINE", "Engine instruments: monitor");
        return p;
    }

    private static DA40FoPhase XlsCruise()
    {
        var p = new DA40FoPhase("CRUISE", "Cruise", "Power by the table, best power mixture, fuel balance.");
        p.Captain("CR_POWER", "Power: set according to the table. Add 5 percent manifold pressure for every 15 degrees above ISA");
        p.Run("CR_BEST_MIXTURE", "Mixture: best power", "DA40_XLS_BEST_MIXTURE", 1, cl: R.CrcBestPower);
        p.Captain("CR_ECONOMY", "Best economy, below 75 percent: peak EGT, if wanted");
        p.Captain("CR_FUEL", "Fuel quantities: check and balance. Maximum difference 8 gallons");
        p.Captain("CR_ALT_AIR", "Alternate air: as required");
        p.Captain("CR_ENGINE", "Engine instruments: monitor");
        return p;
    }

    private static DA40FoPhase XlsDescent()
    {
        var p = new DA40FoPhase("DESCENT", "Descent", "Mixture, engine instruments.");
        p.Captain("DS_MIXTURE", "Mixture: adjust");
        p.Captain("DS_ENGINE", "Engine instruments: monitor");
        return p;
    }

    private static DA40FoPhase XlsApproach()
    {
        var p = new DA40FoPhase("APPROACH", "Approach",
            "Fuel selector to the fullest tank, parking brake off, fuel pump on, altimeters, radios, power.");
        p.Provide("AP_SELECTOR", "Fuel selector: fullest tank", "DA40_XLS_FUEL_SELECTOR", s => s.FullerTank(),
            E.SelectorOnFullerTankField, On, "Fuel selector: select the fullest tank", R.ApcSelectorFullest);
        p.Set("AP_PARKBRK_OFF", "Parking brake: off", "DA40_BRAKE_PARK", 0, "DA40_BRAKE_PARK", Off, R.ApcParkBrakeOff);
        p.Captain("AP_LIGHTS", "Lights: as required");
        p.Set("AP_FUEL_PUMP", "Fuel pump: on", "DA40_XLS_FUEL_PUMP", 1, "DA40_XLS_FUEL_PUMP", On, R.ApcFuelPump);
        p.Captain("AP_DATA", "Landing data: received");
        Altimeters(p, "AP", R.ApcAltimeters);
        p.Captain("AP_RADIOS", "COM, NAV and FMS: set");
        p.Captain("AP_POWER", "Power: set");
        return p;
    }

    private static DA40FoPhase XlsAfterLanding()
    {
        var p = new DA40FoPhase("AFTER_LANDING", "After Landing",
            "Alternate air closed, transponder standby, flaps up, pitot heat and fuel pump off.");
        p.Set("AL_ALT_AIR", "Alternate air: closed", "DA40_ICE_ALTERNATE_AIR", 0, "DA40_ICE_ALTERNATE_AIR", Off, R.AlcAltAir);
        TransponderStandby(p);
        p.Captain("AL_LIGHTS", "Lights: as required");
        p.Set("AL_FLAPS_UP", "Flaps: up", "DA40_FLAPS_POSITION", 0, "DA40_FLAPS_POSITION", Is(0), R.AlcFlapsUp);
        p.Set("AL_PITOT_OFF", "Pitot heat: off", "DA40_ICE_PITOT_HEAT", 0, "DA40_ICE_PITOT_HEAT", Off, R.AlcPitotPump);
        p.Set("AL_FUEL_PUMP_OFF", "Fuel pump: off", "DA40_XLS_FUEL_PUMP", 0, "DA40_XLS_FUEL_PUMP", Off, R.AlcPitotPump);
        return p;
    }

    private static DA40FoPhase XlsShutdown()
    {
        var p = new DA40FoPhase("SHUTDOWN", "Shutdown",
            "Parking brake, avionics off, the grounding check, mixture cut-off, ignition off, lights off, battery master off.");
        OnGroundGuard(p, "SD_ON_GROUND");
        p.Set("SD_PARKBRK", "Parking brake: set", "DA40_BRAKE_PARK", 1, "DA40_BRAKE_PARK", On, R.SdcParkBrake);
        p.Captain("SD_ENGINE", "Engine instruments: check");
        p.Set("SD_AVIONICS_OFF", "Avionics master: off", "DA40_ELEC_AVIONICS_MASTER", 0, "DA40_ELEC_AVIONICS_MASTER", Off,
            R.SdcAvionicsOff);
        p.Captain("SD_THROTTLE", "Throttle: about 1000 RPM");
        // "Ignition: Grounding check. Off then immediately Both again": the two writes 200 ms apart.
        p.Multi("SD_GROUNDING", "Ignition: grounding check, off then both",
            new[] { ("DA40_MAG_KEY", 0), ("DA40_MAG_KEY", DA40MagnetoCheck.PositionBoth) },
            "DA40_MAG_KEY", Is(DA40MagnetoCheck.PositionBoth), R.SdcGrounding);
        p.Steps[^1].SkipCondition = null;   // a test: made every time
        p.LeaveAlone(NotOnGround, "Ignition stays on both: the aircraft is not read on the ground.");
        p.Set("SD_MIXTURE", "Mixture: cut-off", "DA40_XLS_MIXTURE_SET", 0, "DA40_XLS_MIXTURE_SET", v => v <= E.LeverStopPercent,
                R.SdcMixtureCutOff)
         .LeaveAlone(NotOnGround, "Mixture stays where it is: the aircraft is not read on the ground.");
        p.Check("SD_ENGINE_STOPPED", "Engine: stopped", E.EngineStoppedField, On, 60);
        p.Set("SD_IGNITION_OFF", "Ignition: off", "DA40_MAG_KEY", 0, "DA40_MAG_KEY", Is(0), R.SdcIgnitionOff)
         .LeaveAlone(NotOnGround, "Ignition stays on: the aircraft is not read on the ground.");
        ExteriorLightsOff(p);
        p.Captain("SD_CONSUMERS", "Electrical consumers: off");
        p.Set("SD_ELEC_OFF", "Battery master: off", "DA40_ELEC_MASTER_BATTERY", 0, "DA40_ELEC_MASTER_BATTERY", Off,
                R.SdcElecOff)
         .Requires("SD_ENGINE_STOPPED", "Battery master stays on: the engine was not seen to stop.")
         .LeaveAlone(NotOnGround, "Battery master stays on: the aircraft is not read on the ground.");
        p.Set("SD_STROBE_OFF", "Strobe: off", "DA40_LIGHT_STROBE", 0, "DA40_LIGHT_STROBE", Off, R.SdcStrobeOff);
        CabinLightsOff(p);
        return p;
    }

    // =======================================================================
    // Shared by both airframes
    // =======================================================================

    private static DA40FoPhase Taxi(DA40Variant variant)
    {
        var p = new DA40FoPhase("TAXI", "Taxi", variant == DA40Variant.NG
            ? "Taxi light, parking brake, brakes, flight instruments."
            : "Fuel selector to the fullest tank, taxi light, parking brake, brakes, flight instruments.");
        if (variant == DA40Variant.XLS)
            p.Provide("TX_SELECTOR", "Fuel selector: fullest tank", "DA40_XLS_FUEL_SELECTOR", s => s.FullerTank(),
                E.SelectorOnFullerTankField, On, "Fuel selector: select the fullest tank", R.TxcSelectorFullest);
        p.Set("TX_TAXI_LIGHT", "Taxi light: on", "DA40_LIGHT_TAXI", 1, "DA40_LIGHT_TAXI", On, R.TxcTaxiLight);
        p.Captain("TX_PARKBRK", "Parking brake: release", s => s.IsPosition("DA40_BRAKE_PARK", 0));
        p.Captain("TX_BRAKES", "Brakes: test");
        p.Captain("TX_INSTRUMENTS", "Flight instruments: check");
        return p;
    }

    private static DA40FoPhase Lineup(DA40Variant variant)
    {
        var p = new DA40FoPhase("LINEUP", "Line Up", "Lights on, transponder ALT, the runway and approach sector.");
        p.Multi("LU_LIGHTS", "Lights: landing, taxi, position and strobe on",
            new[] { ("DA40_LIGHT_LANDING", 1), ("DA40_LIGHT_TAXI", 1), ("DA40_LIGHT_POSITION", 1), ("DA40_LIGHT_STROBE", 1) },
            E.LineUpLightsField, On, R.LucLights);
        p.Run("LU_XPDR", "Transponder: ALT", X.TransponderKey, 4, "DA40_XPDR_MODE", Is(4), cl: R.LucTransponder);
        p.Captain("LU_PITOT", "Pitot heat: as required");
        p.Captain("LU_PARKBRK", "Parking brake: release", s => s.IsPosition("DA40_BRAKE_PARK", 0));
        p.Captain("LU_APPROACH", "Approach sector: check clear");
        p.Captain("LU_RUNWAY", "Runway: identified");
        p.Captain("LU_POWER", variant == DA40Variant.NG
            ? "Power lever: maximum, confirm a good load and RPM"
            : "Throttle: advance smoothly, confirm a good fuel flow and RPM");
        return p;
    }

    private static DA40FoPhase Final(DA40Variant variant)
    {
        var p = new DA40FoPhase("LANDING", "Final", variant == DA40Variant.NG
            ? "Flaps LDG."
            : "Flaps LDG, propeller high RPM, mixture full rich.");
        p.Set("LD_FLAPS", "Flaps: LDG", "DA40_FLAPS_POSITION", 2, "DA40_FLAPS_POSITION", Is(2), R.LdcFlapsLdg)
         .LeaveAlone(s => s.IsPosition(E.BelowVfeLandingField, 0),
                "Flaps stay where they are: the airspeed is above the flaps LDG limit.");
        if (variant == DA40Variant.XLS)
        {
            p.Set("LD_PROP", "Propeller: high RPM", "DA40_XLS_PROP_SET", 100, "DA40_XLS_PROP_SET",
                v => v >= E.PropHighRpmPercent, R.LdcPropHigh);
            p.Set("LD_MIXTURE", "Mixture: full rich", "DA40_XLS_MIXTURE_SET", 100, "DA40_XLS_MIXTURE_SET",
                v => v >= E.PropHighRpmPercent, R.LdcMixtureRich);
        }
        return p;
    }

    // FO-25: a flow that cuts the engine or its fuel checks first, read-only and Stop, unread failing.
    private static void EngineStoppedGuard(DA40FoPhase p, string id) =>
        p.Check(id, "Engine stopped", E.EngineStoppedField, On, 5).StopOnFailure();

    private static void OnGroundGuard(DA40FoPhase p, string id) =>
        p.Check(id, "On the ground", E.OnGroundField, On, 5).StopOnFailure();

    // The same guards on the ticks that make the cut: unread refuses (FO-25).
    private static bool NotStopped(E s) => !s.IsOn(E.EngineStoppedField);
    private static bool NotOnGround(E s) => !s.IsOn(E.OnGroundField);

    private static void Trim(DA40FoPhase p) =>
        p.Set("BS_TRIM", "Elevator trim: neutral", "DA40_TRIM_CENTRE", 1, "DA40_TRIM_SET", v => Math.Abs(v) < 1, R.BscTrim);

    // A lever the page asks to be checked at idle: the Captain's, so said, then waited for.
    private static void PowerLeverIdle(DA40FoPhase p, string prefix, string lever, string cl)
    {
        p.Captain(prefix + "_POWER", lever + ": idle", s => s.IsOn(E.PowerIdleField));
        p.Check(prefix + "_POWER_IDLE", lever + " at idle", E.PowerIdleField, On, PilotLeverWaitSeconds, cl);
    }

    private static void AllLightsOff(DA40FoPhase p) =>
        p.Multi("BS_LIGHTS_OFF", "All lights: off",
            new[]
            {
                ("DA40_LIGHT_INSTRUMENT_SET", 0), ("DA40_LIGHT_FLOOD_SET", 0), ("DA40_LIGHT_LANDING", 0),
                ("DA40_LIGHT_TAXI", 0), ("DA40_LIGHT_POSITION", 0), ("DA40_LIGHT_STROBE", 0),
                ("DA40_LIGHT_CABIN_RIGHT", 0), ("DA40_LIGHT_CABIN_LEFT", 0), ("DA40_LIGHT_CABIN_BAGGAGE", 0),
            },
            E.AllLightsOffField, On, R.BscLightsOff);

    // The G1000 power-up, the controls and the flap travel test: "Select flaps LDG, check
    // movement. Select flaps UP, check movement."
    private static void PowerUpTail(DA40FoPhase p, string fuelCheck)
    {
        p.Captain("BS_G1000", "G1000: powered, acknowledge the database page");
        p.Captain("BS_CONTROLS", "Flight controls: free and correct");
        p.Move("BS_FLAPTEST_LDG", "Flap travel test: flaps LDG", "DA40_FLAPS_POSITION", 2);
        p.Check("BS_FLAPTEST_LDG_WAIT", "Flaps travelling to LDG", "DA40_FLAPS_TRAVEL_LEFT",
            v => v >= FlapLdgTravelPercent, 30);
        p.Move("BS_FLAPTEST_UP", "Flap travel test: flaps up", "DA40_FLAPS_POSITION", 0);
        p.Check("BS_FLAPTEST_UP_WAIT", "Flaps travelling up", "DA40_FLAPS_TRAVEL_LEFT",
            v => v <= FlapUpTravelPercent, 30, R.BscFlapTest);
        p.Steps[^1].RequiresStepId = "BS_FLAPTEST_LDG_WAIT";
        p.Steps[^1].RequiresStepSkipText = "Skipping: the flaps did not reach LDG.";
        p.Captain("BS_FUEL", fuelCheck);
    }

    private static void DoorsAndStrobe(DA40FoPhase p, string doorsCl, string strobeCl)
    {
        string prefix = p.Id switch { "ENGINE_START_HOT" => "EH", "ENGINE_START_REPRIME" => "ER", _ => "ES" };
        p.Run(prefix + "_DOORS", "Doors: closed and latched", X.CloseDoorsKey, 1, E.DoorsClosedField, On, cl: doorsCl);
        p.Set(prefix + "_STROBE", "Strobe: on", "DA40_LIGHT_STROBE", 1, "DA40_LIGHT_STROBE", On, strobeCl);
    }

    // The XLS crank: the throttle to its starting position (when the block gives one), propeller area,
    // the ignition at BOTH, the start key held until the engine fires (mixture in as it does).
    private static void XlsCrank(DA40FoPhase p, int? throttlePercent, string? throttleLabel, string? throttleCl,
        string propClearCl, string ignitionCl, string firesCl, string idPrefix = "ES")
    {
        if (throttlePercent is int percent && throttleLabel != null && throttleCl != null)
            p.Set(idPrefix + "_THROTTLE_START", throttleLabel, "DA40_XLS_THROTTLE_SET", percent, "DA40_XLS_THROTTLE_SET",
                v => Math.Abs(v - percent) <= 3, throttleCl);
        p.Captain(idPrefix + "_PROP_CLEAR", "Propeller area: clear front and back", cl: propClearCl);
        p.Set(idPrefix + "_MAGS_BOTH", "Ignition: both", "DA40_MAG_KEY", DA40MagnetoCheck.PositionBoth, "DA40_MAG_KEY",
            Is(DA40MagnetoCheck.PositionBoth));
        p.Run(idPrefix + "_CRANK", "Ignition key: start, mixture full rich when the engine fires", X.CrankKey, 1,
            E.EngineRunningField, On, FlowStepFailurePolicy.Stop, ignitionCl, firesCl)
         .LeaveAlone(NotStopped, "Not starting: the engine is not read as stopped.");
    }

    // After the engine fires, the common end of every XLS start.
    private static void XlsStartTail(DA40FoPhase p, string idPrefix = "ES")
    {
        p.Check(idPrefix + "_OIL", "Oil pressure: green", E.OilPressureGreenField, On, 30, R.EscOilGreen);
        p.Captain(idPrefix + "_THROTTLE_1000", "Throttle: about 1000 RPM", cl: R.EscThrottle1000);
        p.Set(idPrefix + "_PUMP_OFF", "Fuel pump: off", "DA40_XLS_FUEL_PUMP", 0, "DA40_XLS_FUEL_PUMP", Off, R.EscPumpOff);
        p.Check(idPrefix + "_FUEL_PRESS", "Fuel pressure: green", E.FuelPressureGreenField, On, 15, R.EscFuelPressure);
        p.Set(idPrefix + "_ALTERNATOR", "Alternator master: on", "DA40_ELEC_ALT_MASTER", 1, "DA40_ELEC_ALT_MASTER", On,
            R.EscAlternator);
        p.Check(idPrefix + "_VOLTS", "Voltage: in the green", "DA40_ELEC_BUS_MAIN_VOLT", v => v >= 25, 15, R.EscElectrical);
        p.Check(idPrefix + "_BREAKERS", "Circuit breakers: all in", E.BreakersInField, On, 5, R.EscElectrical);
        p.Captain(idPrefix + "_LOAD", "Electrical load: check. If a breaker trips: throttle idle, reset it, keep the load under 25 amps");
    }

    private static void MagMove(DA40FoPhase p, string id, string label, int position)
    {
        p.Move(id, label, "DA40_MAG_KEY", position);
        p.Wait(id + "_WAIT", "RPM settling", SettleSeconds);
    }

    // "Pitot heat: On, Annunciation + Amps", then "Pitot heat test: Off".
    private static void PitotTest(DA40FoPhase p)
    {
        p.Move("AS_PITOT_ON", "Pitot heat test: on", "DA40_ICE_PITOT_HEAT", 1, cl: R.AscPitotOn);
        p.Captain("AS_PITOT_CHECK", "Pitot heat: check the annunciation and the amps");
        p.Set("AS_PITOT_OFF", "Pitot heat test: off", "DA40_ICE_PITOT_HEAT", 0, "DA40_ICE_PITOT_HEAT", Off, R.AscPitotOff);
    }

    // "Altimeters(2): Set": the Captain sets the G1000's QNH, the First Officer the standby to match.
    private static void Altimeters(DA40FoPhase p, string prefix, string cl)
    {
        p.Captain(prefix + "_ALTIMETER", "Altimeter: set the QNH on the G1000");
        p.Provide(prefix + "_STBY_BARO", "Standby altimeter: set to the G1000's", X.StandbyBaroKey,
            s => s.G1000BaroHundredths(), E.AltimetersAgreeField, On, "Standby altimeter: set it to the G1000's", cl);
    }

    // "Autopilot test: Perform. AP on, Electric trim->AP off, AP disc->Trim inop, GA button": the pilot's
    // hands-on test, after the GFC 700's own pre-flight test has passed (DA40A-5).
    private static void AutopilotTest(DA40FoPhase p)
    {
        p.Check("AS_AFCS_PFT", "Autopilot pre-flight test: passed", E.AfcsTestPassedField, On, 60);
        p.Captain("AS_AP_TEST",
            "Autopilot test: AP on, electric trim disconnects it, AP disconnect makes the trim inoperative, go-around button");
        p.Captain("AS_FMS", "FMS setup: COM, NAV, flight plan, ADF, DME, CDI, bearings 1 and 2, bugs");
    }

    private static void TransponderStandby(DA40FoPhase p) =>
        p.Run("AL_XPDR", "Transponder: standby", X.TransponderKey, 1, "DA40_XPDR_MODE", Is(1), cl: R.AlcTransponder);

    private static void ExteriorLightsOff(DA40FoPhase p) =>
        p.Multi("SD_LIGHTS_OFF", "Lights except strobe: off",
            new[] { ("DA40_LIGHT_LANDING", 0), ("DA40_LIGHT_TAXI", 0), ("DA40_LIGHT_POSITION", 0) },
            E.ExteriorLightsOffField, On, R.SdcLightsOff);

    private static void CabinLightsOff(DA40FoPhase p) =>
        p.Multi("SD_CABIN_OFF", "Cabin lights: off",
            new[] { ("DA40_LIGHT_CABIN_RIGHT", 0), ("DA40_LIGHT_CABIN_LEFT", 0), ("DA40_LIGHT_CABIN_BAGGAGE", 0) },
            E.CabinLightsOffField, On, R.SdcCabinOff);
}
