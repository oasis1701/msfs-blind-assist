using System.Text;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.FirstOfficer.DA40;

using Item = ChecklistItem<DA40FoActionExecutor, DA40FoStateEvaluator>;
using Group = ChecklistGroup<DA40FoActionExecutor, DA40FoStateEvaluator>;
using E = DA40FoStateEvaluator;

/// <summary>
/// The DA40's read-back checklists: the aircraft's OWN checklist pages, read out of the installed
/// package at run time (<see cref="NativeChecklistReader"/>, DA40S-9), in COWS's words and order,
/// clues and block headings included. Nothing is transcribed, so a COWS update reaches the window
/// with the aircraft.
///
/// Every procedure page becomes a <c>*_CL</c> group beside the phase's action group (FO-3: no line
/// carries an action). A line the First Officer delivers is matched by its block, subject and
/// expectation (<see cref="Links"/>) to a fixed id that its flow step links, and auto-detects where
/// the aircraft can say it is done; every other line is a Captain line, which the pilot ticks. The
/// reference pages (weights and speeds, the tips) are Informational: read, never ticked.
///
/// A COWS edit that renames a line only loses that line's link: it reads as a Captain line, never as
/// a wrong tick. A page with a title not listed here keeps its text as an Informational group.
/// </summary>
internal static class DA40FoReadback
{
    // ---- page → group ----
    public const string BeforeStartPage = "BEFORE_START_CL";
    public const string EngineStartPage = "ENGINE_START_CL";
    public const string AfterStartPage = "AFTER_START_CL";
    public const string TaxiPage = "TAXI_CL";
    public const string BeforeTakeoffPage = "BEFORE_TAKEOFF_CL";
    public const string LineupPage = "LINEUP_CL";
    public const string ClimbPage = "AFTER_TAKEOFF_CL";
    public const string CruisePage = "CRUISE_CL";
    public const string DescentPage = "DESCENT_CL";
    public const string ApproachPage = "APPROACH_CL";
    public const string FinalPage = "LANDING_CL";
    public const string AfterLandingPage = "AFTER_LANDING_CL";
    public const string ShutdownPage = "SHUTDOWN_CL";
    public const string WeightsSpeedsPage = "DA40_WEIGHTS_SPEEDS";
    public const string TipsPage = "DA40_TIPS";

    /// <summary>The COWS page titles (both airframes' spellings), normalized, to their group.</summary>
    private static readonly Dictionary<string, string> PageGroups = new(StringComparer.Ordinal)
    {
        ["before engine start"] = BeforeStartPage,
        ["engine start"] = EngineStartPage,
        ["after engine start"] = AfterStartPage,
        ["taxi"] = TaxiPage,
        ["before take off"] = BeforeTakeoffPage,
        ["line up"] = LineupPage,
        ["climb"] = ClimbPage,
        ["cruise"] = CruisePage,
        ["descent"] = DescentPage,
        ["approach"] = ApproachPage,
        ["final"] = FinalPage,
        ["after landing"] = AfterLandingPage,
        ["shutdown"] = ShutdownPage,
        ["weights speeds"] = WeightsSpeedsPage,
        // The XLS's reference page carries its cruise power table too.
        ["weights speeds power table"] = WeightsSpeedsPage,
        ["tips and help"] = TipsPage,
    };

    /// <summary>The reference pages: read, never ticked.</summary>
    public static readonly IReadOnlySet<string> ReferencePages =
        new HashSet<string>(StringComparer.Ordinal) { WeightsSpeedsPage, TipsPage };

    // ---- the lines a step delivers ----
    // Before engine start
    public const string BscTrim = "BSC_TRIM", BscPowerIdle = "BSC_POWER_IDLE", BscLevers = "BSC_LEVERS",
        BscParkBrake = "BSC_PARKBRK", BscIgnitionOff = "BSC_IGNITION_OFF", BscAltAir = "BSC_ALT_AIR",
        BscAvionicsEss = "BSC_AVIONICS_ESS", BscMastersOff = "BSC_MASTERS_OFF", BscAltStatic = "BSC_ALT_STATIC",
        BscStartSwitches = "BSC_START_SWITCHES", BscLightsOff = "BSC_LIGHTS_OFF", BscBreakers = "BSC_BREAKERS",
        BscFlapsUp = "BSC_FLAPS_UP", BscPitotXfer = "BSC_PITOT_XFER", BscPitotPump = "BSC_PITOT_PUMP",
        BscElecOn = "BSC_ELEC_ON", BscFlapTest = "BSC_FLAP_TEST", BscSelectorLowest = "BSC_SELECTOR";
    // Engine start (the XLS's three blocks keep their own lines)
    public const string EscDoors = "ESC_DOORS", EscStrobe = "ESC_STROBE", EscPowerIdle = "ESC_POWER_IDLE",
        EscEngineMaster = "ESC_ENG_MASTER", EscGlow = "ESC_GLOW", EscStartKey = "ESC_START_KEY",
        EscOilPressure = "ESC_OIL", EscElectrical = "ESC_ELECTRICAL",
        EscColdThrottlePrime = "ESC_COLD_THROTTLE_PRIME", EscColdPumpOn = "ESC_COLD_PUMP",
        EscColdPrimeRich = "ESC_COLD_PRIME", EscColdPrimeCutOff = "ESC_COLD_CUTOFF",
        EscColdThrottleStart = "ESC_COLD_THROTTLE", EscColdPropClear = "ESC_COLD_PROP_CLEAR",
        EscColdIgnition = "ESC_COLD_IGNITION", EscColdFires = "ESC_COLD_FIRES",
        EscHotPumpOff = "ESC_HOT_PUMP", EscHotMixture = "ESC_HOT_MIXTURE", EscHotThrottle = "ESC_HOT_THROTTLE",
        EscHotPropClear = "ESC_HOT_PROP_CLEAR", EscHotIgnition = "ESC_HOT_IGNITION", EscHotFires = "ESC_HOT_FIRES",
        EscReprimePumpOn = "ESC_REPRIME_PUMP", EscReprimeThrottle = "ESC_REPRIME_THROTTLE",
        EscReprimeRich = "ESC_REPRIME_PRIME", EscReprimeCutOff = "ESC_REPRIME_CUTOFF",
        EscReprimePropClear = "ESC_REPRIME_PROP_CLEAR", EscReprimeIgnition = "ESC_REPRIME_IGNITION",
        EscReprimeFires = "ESC_REPRIME_FIRES",
        EscOilGreen = "ESC_OIL_GREEN", EscThrottle1000 = "ESC_THROTTLE_1000", EscPumpOff = "ESC_PUMP_OFF",
        EscFuelPressure = "ESC_FUEL_PRESSURE", EscAlternator = "ESC_ALTERNATOR";
    // After engine start
    public const string AscOilPressure = "ASC_OIL", AscIdleRpm = "ASC_RPM", AscPitotOn = "ASC_PITOT_ON",
        AscPitotOff = "ASC_PITOT_OFF", AscAvionics = "ASC_AVIONICS", AscAltimeters = "ASC_ALTIMETERS",
        AscCage = "ASC_CAGE", AscFlapsTo = "ASC_FLAPS_TO";
    // Taxi
    public const string TxcTaxiLight = "TXC_TAXI_LIGHT", TxcSelectorFullest = "TXC_SELECTOR";
    // Before take-off
    public const string BtcParkBrake = "BTC_PARKBRK", BtcDoors = "BTC_DOORS", BtcBreakers = "BTC_BREAKERS",
        BtcPowerIdle = "BTC_POWER_IDLE", BtcTemps = "BTC_TEMPS", BtcVoter = "BTC_VOTER", BtcEcuTest = "BTC_ECU_TEST",
        BtcFuelPump = "BTC_FUEL_PUMP", BtcMixtureRich = "BTC_MIXTURE", BtcThrottle2000 = "BTC_RPM_2000",
        BtcPropCycle = "BTC_PROP_CYCLE", BtcMagnetos = "BTC_MAGNETOS";
    // Line up, climb, cruise
    public const string LucLights = "LUC_LIGHTS", LucTransponder = "LUC_XPDR",
        ClcFlapsUp = "CLC_FLAPS_UP", ClcFuelPump = "CLC_FUEL_PUMP", CrcBestPower = "CRC_BEST_POWER";
    // Descent, approach, final
    public const string DscAltimeters = "DSC_ALTIMETERS", ApcAltimeters = "APC_ALTIMETERS", ApcXferOff = "APC_XFER_OFF",
        ApcParkBrakeOff = "APC_PARKBRK_OFF", ApcFuelPump = "APC_FUEL_PUMP", ApcSelectorFullest = "APC_SELECTOR",
        LdcFlapsLdg = "LDC_FLAPS_LDG", LdcPropHigh = "LDC_PROP_HIGH", LdcMixtureRich = "LDC_MIXTURE_RICH";
    // After landing
    public const string AlcFlapsUp = "ALC_FLAPS_UP", AlcPitotOff = "ALC_PITOT_OFF", AlcAltAir = "ALC_ALT_AIR",
        AlcTransponder = "ALC_XPDR", AlcFuelPumpOff = "ALC_FUEL_PUMP_OFF", AlcPitotPump = "ALC_PITOT_PUMP";
    // Shutdown
    public const string SdcParkBrake = "SDC_PARKBRK", SdcCooldown = "SDC_COOLDOWN", SdcAvionicsOff = "SDC_AVIONICS_OFF",
        SdcLightsOff = "SDC_LIGHTS_OFF", SdcEngineMasterOff = "SDC_ENG_MASTER_OFF", SdcEngineStopped = "SDC_ENGINE_STOPPED",
        SdcStrobeOff = "SDC_STROBE_OFF", SdcElecOff = "SDC_ELEC_OFF", SdcCabinOff = "SDC_CABIN_OFF",
        SdcGrounding = "SDC_GROUNDING", SdcMixtureCutOff = "SDC_MIXTURE", SdcIgnitionOff = "SDC_IGNITION_OFF";

    private static bool On(double v) => v > 0.5;
    private static bool Off(double v) => v < 0.5;
    private static Func<double, bool> Is(int n) => v => Math.Abs(v - n) < 0.5;
    private static bool Stop(double v) => v <= E.LeverStopPercent;
    private static bool Full(double v) => v >= E.PropHighRpmPercent;

    /// <summary>
    /// One page line the First Officer delivers: matched on the page, the block it sits in (null
    /// outside a block) and its subject and expectation, normalized. <see cref="Field"/> set makes it
    /// auto-detect; null leaves it a Captain line the flow ticks when its step runs.
    /// </summary>
    internal sealed record Link(string Page, string? Block, string Subject, string Expectation, string Id,
        string? Field = null, Func<double, bool>? Holds = null, string[]? AlsoFields = null);

    /// <summary>Every line of both airframes' pages a step delivers. A line one airframe does not
    /// have never matches there.</summary>
    internal static readonly IReadOnlyList<Link> Links = new Link[]
    {
        // ---- Before engine start ----
        new(BeforeStartPage, null, "Elevator trim", "Neutral", BscTrim, "DA40_TRIM_SET", v => Math.Abs(v) < 1),
        new(BeforeStartPage, null, "Power lever", "Idle", BscPowerIdle, E.PowerIdleField, On),
        new(BeforeStartPage, null, "Throttle, Propeller, Mixture", "Set", BscLevers, E.XlsLeversSetField, On),
        new(BeforeStartPage, null, "Parking Brake", "Set", BscParkBrake, "DA40_BRAKE_PARK", On),
        new(BeforeStartPage, null, "Ignition", "Off", BscIgnitionOff, "DA40_MAG_KEY", Is(0)),
        new(BeforeStartPage, null, "Alternate air", "Closed", BscAltAir, "DA40_ICE_ALTERNATE_AIR", Off),
        new(BeforeStartPage, null, "Avionics, ESS bus", "Off", BscAvionicsEss, "DA40_ELEC_AVIONICS_MASTER", Off,
            new[] { "DA40_ELEC_ESS_BUS" }),
        // Reversed on the same page (the battery master comes on): a Captain line.
        new(BeforeStartPage, null, "Masters(4)", "Off", BscMastersOff),
        new(BeforeStartPage, null, "Alternate Static", "Closed", BscAltStatic, "DA40_ICE_ALTERNATE_STATIC", Off),
        new(BeforeStartPage, null, "Engine master, Fuel pump, Voter", "Off/Auto", BscStartSwitches, E.StartSwitchesSafeField, On),
        new(BeforeStartPage, null, "All lights(9)", "Off", BscLightsOff, E.AllLightsOffField, On),
        new(BeforeStartPage, null, "Circuit Breakers", "Check all in", BscBreakers, E.BreakersInField, On),
        new(BeforeStartPage, null, "Flaps selector", "Check up", BscFlapsUp, "DA40_FLAPS_POSITION", Is(0)),
        new(BeforeStartPage, null, "Pitot heat, Fuel Transfer", "Off", BscPitotXfer, "DA40_ICE_PITOT_HEAT", Off,
            new[] { "DA40_FUEL_TRANSFER" }),
        new(BeforeStartPage, null, "Pitot heat, Fuel pump", "Off", BscPitotPump, "DA40_ICE_PITOT_HEAT", Off,
            new[] { "DA40_XLS_FUEL_PUMP" }),
        new(BeforeStartPage, null, "Electric master", "On", BscElecOn, "DA40_ELEC_MASTER_BATTERY", On),
        new(BeforeStartPage, null, "Battery master", "On", BscElecOn, "DA40_ELEC_MASTER_BATTERY", On),
        new(BeforeStartPage, null, "Flaps full travel", "Test", BscFlapTest),
        new(BeforeStartPage, null, "Fuel selector", "Lowest tank", BscSelectorLowest, E.SelectorOnLowerTankField, On),

        // ---- Engine start ----
        new(EngineStartPage, null, "Doors", "Closed and latched", EscDoors, E.DoorsClosedField, On),
        new(EngineStartPage, null, "Strobe", "On", EscStrobe, "DA40_LIGHT_STROBE", On),
        new(EngineStartPage, null, "Power lever", "Check idle", EscPowerIdle, E.PowerIdleField, On),
        new(EngineStartPage, null, "Engine Master", "On", EscEngineMaster, "DA40_START_ENGINE_MASTER", On),
        new(EngineStartPage, null, "Glow indication", "Wait till off", EscGlow),
        new(EngineStartPage, null, "Start key", "Start", EscStartKey, E.EngineRunningField, On),
        new(EngineStartPage, null, "Oil pressure", "Outside red within 3 sec", EscOilPressure, E.OilPressureOutOfRedField, On),
        new(EngineStartPage, null, "Voltage, Electrical load, Circuit breakers", "Check", EscElectrical),
        new(EngineStartPage, "Cold start", "Throttle", "Priming position", EscColdThrottlePrime),
        new(EngineStartPage, "Cold start", "Fuel pump", "On", EscColdPumpOn),
        new(EngineStartPage, "Cold start", "Mixture", "Full rich for 3-5 seconds", EscColdPrimeRich),
        new(EngineStartPage, "Cold start", "Mixture", "Cut-off", EscColdPrimeCutOff),
        new(EngineStartPage, "Cold start", "Throttle", "Starting position", EscColdThrottleStart),
        new(EngineStartPage, "Cold start", "Propeller area", "Clear front and back", EscColdPropClear),
        new(EngineStartPage, "Cold start", "Ignition key", "Start", EscColdIgnition),
        new(EngineStartPage, "Cold start", "When engine fires", "Mixture:Full rich", EscColdFires),
        new(EngineStartPage, "Flooded/Hot start", "Fuel pump", "Off", EscHotPumpOff),
        new(EngineStartPage, "Flooded/Hot start", "Mixture", "Cut-off", EscHotMixture),
        new(EngineStartPage, "Flooded/Hot start", "Throttle", "Half", EscHotThrottle),
        new(EngineStartPage, "Flooded/Hot start", "Propeller area", "Clear front and back", EscHotPropClear),
        new(EngineStartPage, "Flooded/Hot start", "Ignition key", "Start", EscHotIgnition),
        new(EngineStartPage, "Flooded/Hot start", "When engine fires", "Mixture:Full rich", EscHotFires),
        new(EngineStartPage, "Engine does not start/Reprime procedure", "Fuel pump", "On", EscReprimePumpOn),
        new(EngineStartPage, "Engine does not start/Reprime procedure", "Throttle", "Starting position", EscReprimeThrottle),
        new(EngineStartPage, "Engine does not start/Reprime procedure", "Mixture", "Full rich for 1-2 seconds", EscReprimeRich),
        new(EngineStartPage, "Engine does not start/Reprime procedure", "Mixture", "Cut-off", EscReprimeCutOff),
        new(EngineStartPage, "Engine does not start/Reprime procedure", "Propeller area", "Clear front and back", EscReprimePropClear),
        new(EngineStartPage, "Engine does not start/Reprime procedure", "Ignition key", "Start", EscReprimeIgnition),
        new(EngineStartPage, "Engine does not start/Reprime procedure", "When engine fires", "Mixture:Full rich", EscReprimeFires),
        new(EngineStartPage, null, "Oil pressure", "In green", EscOilGreen, E.OilPressureGreenField, On),
        new(EngineStartPage, null, "Throttle", "~1000RPM", EscThrottle1000),
        new(EngineStartPage, null, "Fuel pump", "Off", EscPumpOff, "DA40_XLS_FUEL_PUMP", Off),
        new(EngineStartPage, null, "Fuel pressure", "In green", EscFuelPressure, E.FuelPressureGreenField, On),
        new(EngineStartPage, null, "Alternator master", "On", EscAlternator, "DA40_ELEC_ALT_MASTER", On),

        // ---- After engine start ----
        new(AfterStartPage, null, "Oil pressure", "Check", AscOilPressure, E.OilPressureGreenField, On),
        new(AfterStartPage, null, "Rpm", "Check 710 +/- 30", AscIdleRpm, E.IdleRpmField, On),
        // Reversed by the next line (the test switches it off again): a Captain line.
        new(AfterStartPage, null, "Pitot heat", "On, Annunciation + Amps", AscPitotOn),
        new(AfterStartPage, null, "Pitot heat test", "Off", AscPitotOff, "DA40_ICE_PITOT_HEAT", Off),
        new(AfterStartPage, null, "Avionics Master", "On", AscAvionics, "DA40_ELEC_AVIONICS_MASTER", On),
        new(AfterStartPage, null, "Altimeters(2)", "Set", AscAltimeters, E.AltimetersAgreeField, On),
        new(AfterStartPage, null, "Standby horizon", "Cage", AscCage),
        new(AfterStartPage, null, "Flaps", "T/O", AscFlapsTo, "DA40_FLAPS_POSITION", Is(1)),

        // ---- Taxi ----
        new(TaxiPage, null, "Taxi light", "On when moving", TxcTaxiLight, "DA40_LIGHT_TAXI", On),
        new(TaxiPage, null, "Fuel selector", "Fullest tank", TxcSelectorFullest, E.SelectorOnFullerTankField, On),

        // ---- Before take-off ----
        new(BeforeTakeoffPage, null, "Parking Brake", "Set", BtcParkBrake, "DA40_BRAKE_PARK", On),
        new(BeforeTakeoffPage, null, "Parking brake", "Set", BtcParkBrake, "DA40_BRAKE_PARK", On),
        new(BeforeTakeoffPage, null, "Doors", "Check, no warning", BtcDoors, E.DoorsClosedField, On),
        new(BeforeTakeoffPage, null, "Doors", "Closed and latched", BtcDoors, E.DoorsClosedField, On),
        new(BeforeTakeoffPage, null, "Circuit Breakers", "Check all in", BtcBreakers, E.BreakersInField, On),
        new(BeforeTakeoffPage, null, "Power lever", "Idle", BtcPowerIdle, E.PowerIdleField, On),
        new(BeforeTakeoffPage, null, "Engine temperatures", "Check green", BtcTemps, E.EngineTempsField, On),
        new(BeforeTakeoffPage, null, "Engine temperatures", "In green", BtcTemps, E.EngineTempsField, On),
        new(BeforeTakeoffPage, null, "Voter switch", "A, Auto, B, Auto", BtcVoter, "DA40_ECU_VOTER", Is(1)),
        new(BeforeTakeoffPage, null, "ECU test", "Test", BtcEcuTest),
        new(BeforeTakeoffPage, null, "Fuel pump", "On", BtcFuelPump, E.FuelPumpOnField, On),
        new(BeforeTakeoffPage, null, "Mixture", "Full rich", BtcMixtureRich, "DA40_XLS_MIXTURE_SET", Full),
        new(BeforeTakeoffPage, null, "Throttle", "2000RPM", BtcThrottle2000),
        new(BeforeTakeoffPage, null, "Propeller", "Cycle 3 times", BtcPropCycle),
        new(BeforeTakeoffPage, null, "Magnetos", "L, Both, R, Both", BtcMagnetos, "DA40_MAG_KEY", Is(3)),

        // ---- Line up, climb, cruise ----
        new(LineupPage, null, "Lights", "On", LucLights, E.LineUpLightsField, On),
        new(LineupPage, null, "Transponder", "ALT", LucTransponder, "DA40_XPDR_MODE", Is(4)),
        new(ClimbPage, null, "Flaps", "UP", ClcFlapsUp, "DA40_FLAPS_POSITION", Is(0)),
        new(ClimbPage, null, "Fuel pump", "Off", ClcFuelPump, E.FuelPumpOnField, Off),
        new(CruisePage, "Leaning", "Best Power", "100F Rich of peak", CrcBestPower),

        // ---- Descent, approach, final ----
        new(DescentPage, null, "Altimeters(2)", "Set", DscAltimeters, E.AltimetersAgreeField, On),
        new(ApproachPage, null, "Altimeters(2)", "Set", ApcAltimeters, E.AltimetersAgreeField, On),
        new(ApproachPage, null, "Fuel Transfer", "Off", ApcXferOff, "DA40_FUEL_TRANSFER", Off),
        new(ApproachPage, null, "Parking Brake", "Check off", ApcParkBrakeOff, "DA40_BRAKE_PARK", Off),
        new(ApproachPage, null, "Parking brake", "Release", ApcParkBrakeOff, "DA40_BRAKE_PARK", Off),
        new(ApproachPage, null, "Fuel pump", "On", ApcFuelPump, E.FuelPumpOnField, On),
        new(ApproachPage, null, "Fuel selector", "Fullest tank", ApcSelectorFullest, E.SelectorOnFullerTankField, On),
        new(FinalPage, null, "Flaps", "LDG", LdcFlapsLdg, "DA40_FLAPS_POSITION", Is(2)),
        new(FinalPage, null, "Rpm", "High rpm", LdcPropHigh, "DA40_XLS_PROP_SET", Full),
        new(FinalPage, null, "Mixture", "Full rich", LdcMixtureRich, "DA40_XLS_MIXTURE_SET", Full),

        // ---- After landing ----
        new(AfterLandingPage, null, "Flaps", "Up", AlcFlapsUp, "DA40_FLAPS_POSITION", Is(0)),
        new(AfterLandingPage, null, "Pitot heat", "Off", AlcPitotOff, "DA40_ICE_PITOT_HEAT", Off),
        new(AfterLandingPage, null, "Alternate air", "Closed", AlcAltAir, "DA40_ICE_ALTERNATE_AIR", Off),
        new(AfterLandingPage, null, "Transponder", "Standby", AlcTransponder, "DA40_XPDR_MODE", Is(1)),
        new(AfterLandingPage, null, "Fuel pump", "Off", AlcFuelPumpOff, E.FuelPumpOnField, Off),
        new(AfterLandingPage, null, "Pitot heat, Fuel pump", "Off", AlcPitotPump, "DA40_ICE_PITOT_HEAT", Off,
            new[] { "DA40_XLS_FUEL_PUMP" }),

        // ---- Shutdown ----
        new(ShutdownPage, null, "Parking brake", "Set", SdcParkBrake, "DA40_BRAKE_PARK", On),
        new(ShutdownPage, null, "Engine cooldown", "Idle for 1 min", SdcCooldown),
        new(ShutdownPage, null, "Avionics Master", "Off", SdcAvionicsOff, "DA40_ELEC_AVIONICS_MASTER", Off),
        new(ShutdownPage, null, "Lights (except strobe)", "Off", SdcLightsOff, E.ExteriorLightsOffField, On),
        new(ShutdownPage, null, "Engine master", "Off", SdcEngineMasterOff, "DA40_START_ENGINE_MASTER", Off),
        new(ShutdownPage, null, "Engine Instruments", "Check crossed out", SdcEngineStopped, E.EngineStoppedField, On),
        new(ShutdownPage, null, "Strobe", "Off", SdcStrobeOff, "DA40_LIGHT_STROBE", Off),
        new(ShutdownPage, null, "Electric master", "Off", SdcElecOff, "DA40_ELEC_MASTER_BATTERY", Off),
        new(ShutdownPage, null, "Battery master", "Off", SdcElecOff, "DA40_ELEC_MASTER_BATTERY", Off),
        new(ShutdownPage, null, "Cabin light", "Off", SdcCabinOff, E.CabinLightsOffField, On),
        new(ShutdownPage, null, "Ignition", "Grounding check", SdcGrounding),
        new(ShutdownPage, null, "Mixture", "Cut-off", SdcMixtureCutOff, "DA40_XLS_MIXTURE_SET", Stop),
        new(ShutdownPage, null, "Ignition", "Off", SdcIgnitionOff, "DA40_MAG_KEY", Is(0)),
    };

    /// <summary>The group for a COWS page title, or null for a page this list does not know.</summary>
    public static string? GroupFor(string pageTitle) => PageGroups.GetValueOrDefault(Normalize(pageTitle));

    /// <summary>
    /// The read-back and reference groups built from the aircraft's pages. A page this list does not
    /// know keeps its text as an Informational group, so nothing COWS writes is lost.
    /// </summary>
    public static List<Group> Build(IReadOnlyList<NativeChecklistPage> pages)
    {
        var groups = new List<Group>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            string? known = GroupFor(page.Title);
            string id = known ?? "DA40_PAGE_" + Slug(page.Title);
            // Two pages with one title (none today): the second keeps its own group.
            for (int n = 2; !usedIds.Add(id); n++) id = (known ?? "DA40_PAGE_" + Slug(page.Title)) + "_" + n;

            bool reference = known == null || ReferencePages.Contains(known);
            var group = new Group
            {
                Id = id,
                Name = reference ? page.Title : page.Title + " checklist",
            };
            var usedItems = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (var line in page.Lines)
                group.Items.Add(LineItem(id, line, reference, ++index, usedItems));
            groups.Add(group);
        }
        return groups;
    }

    private static Item LineItem(string groupId, NativeChecklistLine line, bool reference, int index,
        HashSet<string> usedItems)
    {
        string label = Label(line);
        string positional = $"{groupId}_{index:00}";

        if (reference || line.IsHeading || line.IsNote)
            return Info(positional, groupId, label);

        var link = Links.FirstOrDefault(l => l.Page == groupId
            && Normalize(l.Block ?? "") == Normalize(line.Block ?? "")
            && Normalize(l.Subject) == Normalize(line.Subject)
            && Normalize(l.Expectation) == Normalize(line.Expectation));
        // A link id already used on this page (COWS repeating a line) leaves the repeat a Captain line.
        string id = link != null && usedItems.Add(link.Id) ? link.Id : positional;
        usedItems.Add(id);

        if (link?.Field != null && link.Holds != null && id == link.Id)
            return new Item
            {
                Id = id, GroupId = groupId, Label = label,
                Type = ChecklistItemType.AutoDetectable,
                AutoCompleteAllowed = true,
                ManualCompletionAllowed = true,
                StateFieldName = link.Field,
                StateCondition = link.Holds,
                AdditionalStateFields = link.AlsoFields ?? Array.Empty<string>(),
                RevertBehavior = RevertBehavior.RevertToState,
            };

        return new Item
        {
            Id = id, GroupId = groupId, Label = label,
            Type = ChecklistItemType.CaptainReminder,
            ManualCompletionAllowed = true,
            ReminderText = label,
        };
    }

    private static Item Info(string id, string groupId, string label) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Informational,
        ManualCompletionAllowed = false,
    };

    /// <summary>"Subject: Expectation. Clue. Clue": the page's words, a clue read as part of its line
    /// because it is often the only place an operating detail is written down.</summary>
    internal static string Label(NativeChecklistLine line)
    {
        var sb = new StringBuilder(line.Subject);
        if (line.Expectation.Length > 0) sb.Append(sb.Length > 0 ? ": " : "").Append(line.Expectation);
        foreach (string clue in line.Clues)
        {
            if (sb.Length > 0) sb.Append(sb[^1] is '.' or '!' or '?' ? " " : ". ");
            sb.Append(clue);
        }
        return sb.ToString();
    }

    internal static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { if (space && sb.Length > 0) sb.Append(' '); sb.Append(c); space = false; }
            else space = true;
        }
        return sb.ToString();
    }

    private static string Slug(string s) => Normalize(s).Replace(' ', '_').ToUpperInvariant();
}
