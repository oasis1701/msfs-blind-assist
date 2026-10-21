using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// Every A300 panel row, light and value the First Officer touches, in one place. Rows are the generated map's
/// keys, placed by A300PanelLayout; a position is the row's STATE value, each the target iniBuilds' own checklist
/// tests (Airbus_A300_Checklist.xml, package 1.0.11). Lights are A300LampBoard ids, read as the cockpit shows
/// them, their bus's light power included ([A300-23]).
/// </summary>
public static class A300FoKeys
{
    // ---------------------------------------------------------------- rows
    public const string Batt1 = "A300_BATT_1", Batt2 = "A300_BATT_2", Batt3 = "A300_BATT_3";
    public const string WiperCpt = "A300_CPT_WIPER_KNOB", WiperFo = "A300_FO_WIPER_KNOB";
    public const string GearLever = "A300_GEAR_LEVER";                       // 0 up, 1 down
    public const string FuelLever1 = "A300_ENG1_CUTOFF", FuelLever2 = "A300_ENG2_CUTOFF";
    public const string WxrSystem = "A300_WER_SYS";                          // 0 system 2, 1 off, 2 system 1
    public const string WxrMode = "A300_WER_MODE";                           // 0 test, 1 WX
    public const string ExtPower = "A300_EXT_PWR";
    public const string Irs1 = "A300_NAVATTLEFT", Irs2 = "A300_NAVATTRIGHT", Irs3 = "A300_NAVATTCTR";
    public const string IrsDisplay = "A300_IRS_MODE_SELECT";                 // 4 HDG/STS
    public const string CrewOxygen = "A300_CREW_O2_SUPPLY";
    public const string AnnLight = "A300_ANN_LIGHTSWITCH";                   // 0 test, 1 bright
    public const string NoSmoking = "A300_NOSMOKING";                        // 1 auto
    public const string SeatBelts = "A300_SEATBELT";
    public const string FlightRecorder = "A300_FLTRCDR_GNDCTL";
    public const string Nose = "A300_NOSELIGHTSWITCH";                       // 0 T.O., 1 taxi, 2 off
    public const string LandingL = "A300_LANDINGLEFTSWITCH", LandingR = "A300_LANDINGRIGHTSWITCH";   // 0 on, 2 retracted
    public const string Wing = "A300_WINGLIGHTSWITCH";
    public const string Strobe = "A300_STROBESWITCH";                        // 0 on, 1 auto, 2 off
    public const string Beacon = "A300_BEACONSWITCH";
    public const string TurnoffL = "A300_RWY_TOFF_L", TurnoffR = "A300_RWY_TOFF_R";
    public const string NavLogo = "A300_NAVLIGHT_SWITCH";                    // 1 on (system 1), 2 off
    public const string Ats1 = "A300_ATS_1", Ats2 = "A300_ATS_2";
    public const string PitchTrim1 = "A300_PITCH_TRIM_1", PitchTrim2 = "A300_PITCH_TRIM_2";
    public const string YawDamper1 = "A300_YAW_DAMPER_1", YawDamper2 = "A300_YAW_DAMPER_2";
    public const string AcIndicator = "A300_AC_BUS_SELECT", DcIndicator = "A300_DC_BUS_SELECT";   // 3 EMER, 3 ESS TR
    public const string Ignition = "A300_IGNITION_KNOB";                     // 0 start B, 1 start A, 3 off, 4 cont relight
    public const string WindowHeat1 = "A300_WINDOW_HEAT_1", WindowHeat2 = "A300_WINDOW_HEAT_2";
    public const string ProbeCpt = "A300_CAPT_HEAT", ProbeStby = "A300_STBY_HEAT", ProbeFo = "A300_FO_HEAT";
    public const string OverboardValve = "A300_OVBD_VALVE";
    public const string EmerExit = "A300_EMERGEXIT_SWITCH";                  // 1 arm, 2 disarm
    public const string FdCpt = "A300_CPT_FLT_DIR", FdFo = "A300_FO_FLT_DIR";
    public const string NavSelCpt = "A300_CPT_VOR", NavSelFo = "A300_FO_VOR";
    public const string DhCpt = "A300_DH_CPT", DhFo = "A300_FO_DH";
    public const string ParkingBrake = "A300_PARKINGBRAKE";
    public const string AdfTone1 = "A300_ADF1_TONE", AdfTone2 = "A300_ADF2_TONE";
    public const string RudderTrimReset = "A300_TRIM_KORRY";
    public const string Transponder = "A300_TCAS_MODE";                      // 0 standby, 1 XPDR, 2 TA/RA
    public const string CockpitDoor = "A300_COCKPIT_DOOR";                   // 0 closed
    public const string ApuMaster = "A300_APU_MASTERSWITCH", ApuStart = "A300_APU_START";
    public const string ApuBleed = "A300_APU_BLEEDSWITCH", ApuGen = "A300_APU_GEN";
    public const string EcamApu = "A300_ECAM_APU", EcamEng = "A300_ECAM_ENG";
    public const string AntiSkid = "A300_ANTI_SKID";                         // 0 norm
    public const string Starter1 = "A300_ENG_1_START", Starter2 = "A300_ENG_2_START";
    public const string Prof = "A300_PROFILE_BUTTON", Nav = "A300_NAV_BUTTON";
    public const string AutobrakeMax = "A300_AUTO_BRK_MAX";
    public const string BrakeFan = "A300_BRAKE_FAN";
    public const string Pack1 = "A300_PACK_1_VALVE", Pack2 = "A300_PACK_2_VALVE";
    public const string WingSupply = "A300_WING_SUPPLY", WingMode = "A300_WING_MODE";
    public const string AntiIceEng1 = "A300_ANTI_ICE_E1", AntiIceEng2 = "A300_ANTI_ICE_E2";
    public const string MasterWarningButton = "A300_MASTER_WARNING_CPT";
    public const string Flaps = A300Levers.FlapsKey;                         // 0 up, 1 15/0, 2 15/15, 3 15/20, 4 30/40
    public const string SpoilersArm = A300Levers.SpoilersArmKey;
    public const string SpeedBrake = A300Levers.SpeedBrakeKey;

    // Test rows: PUSH2 is each fire panel's loop test, PUSH1 its squib test (A300WritePlan.TestHoldMs).
    public const string ApuLoopTest = "A300_APU_FIRE_PUSH2", ApuSquibTest = "A300_APU_FIRE_PUSH1";
    public const string Eng1LoopTest = "A300_ENG1_FIRE_PUSH2", Eng1SquibTest = "A300_ENG1_FIRE_PUSH1";
    public const string Eng2LoopTest = "A300_ENG2_FIRE_PUSH2", Eng2SquibTest = "A300_ENG2_FIRE_PUSH1";
    public const string SmokeTest = "A300_SMOKE_TEST", GpwsTest = "A300_GPWS_CPT";
    public const string GearWarningTest = "A300_LDG_TEST", TakeoffConfigTest = "A300_TO_CONFIG_TEST";

    public static readonly string[] FuelPumps =
    {
        "A300_OUT_TK_PMP_L1", "A300_OUT_TK_PMP_L2", "A300_OUT_TK_PMP_R1", "A300_OUT_TK_PMP_R2",
        "A300_INR_TK_PMP_L1", "A300_INR_TK_PMP_L2", "A300_INR_TK_PMP_R1", "A300_INR_TK_PMP_R2",
        "A300_CTR_TK_PMP_L", "A300_CTR_TK_PMP_R", "A300_TRMTK_1_PMP", "A300_TRMTK_2_PMP",
    };

    /// <summary>Rows written alone (not through <see cref="AllAt"/> or a press), for the rows test.</summary>
    public static readonly string[] SingleRows =
    {
        WxrSystem, WxrMode, ExtPower, IrsDisplay, CrewOxygen, AnnLight, NoSmoking, SeatBelts, FlightRecorder, Nose,
        Wing, Strobe, Beacon, Ignition, OverboardValve, EmerExit, ParkingBrake, Transponder, CockpitDoor, ApuMaster,
        ApuBleed, ApuGen, AntiSkid, Starter1, Starter2, BrakeFan, Flaps, SpoilersArm, SpeedBrake, GearLever,
        ApuLoopTest, ApuSquibTest, Eng1LoopTest, Eng1SquibTest, Eng2LoopTest, Eng2SquibTest, SmokeTest, GpwsTest,
        GearWarningTest, TakeoffConfigTest, AntiIceEng1, AntiIceEng2,
    };

    // ---------------------------------------------------------------- lights (A300LampBoard ids)
    public const string ExtPowerAvailLight = "A300_LT_EXT_PWR_SEQ1_LIGHT";
    public const string ApuAvailLight = "A300_LT_APU_AVAIL_SEQ1_LIGHT";
    public const string ApuStartOnLight = "A300_LT_APU_START_SEQ2_LIGHT";
    public const string StartValve1Light = "A300_LT_ENG_1_START_SEQ1_LIGHT", StartValve2Light = "A300_LT_ENG_2_START_SEQ1_LIGHT";
    public const string FireHandleEng1Light = "A300_LT_FIRE_HANDLE_ENG1_LIGHT", FireHandleEng2Light = "A300_LT_FIRE_HANDLE_ENG2_LIGHT";
    public const string FireHandleApuLight = "A300_LT_FIRE_HANDLE_APU_LIGHT";
    public const string Eng1Squib1Light = "A300_LT_FIRE_1_AG1_SEQ1_LIGHT", Eng1Squib2Light = "A300_LT_FIRE_1_AG2_SEQ1_LIGHT";
    public const string Eng2Squib1Light = "A300_LT_FIRE_2_AG1_SEQ1_LIGHT", Eng2Squib2Light = "A300_LT_FIRE_2_AG2_SEQ1_LIGHT";
    public const string ApuSquibLight = "A300_LT_FIRE_APU_AG_SEQ1_LIGHT";
    public static readonly string[] CargoSmokeLights =
        { "A300_LT_DECK_MID1_SMOKE_LIGHT", "A300_LT_DECK_MID2_SMOKE_LIGHT", "A300_LT_DECK_AFT_SMOKE_LIGHT" };
    public const string GpwsLight = "A300_LAMP_GPWS_LIGHT";
    public static readonly string[] GearDownLights =
        { "A300_LT_GEAR_1_UNLK_SEQ2_LIGHT", "A300_LT_GEAR_2_UNLK_SEQ2_LIGHT", "A300_LT_GEAR_3_UNLK_SEQ2_LIGHT" };
    public static readonly string[] GearUnlockedLights =
        { "A300_LT_INDICATOR_LOWER_UNLK1_LIGHT", "A300_LT_INDICATOR_LOWER_UNLK2_LIGHT", "A300_LT_INDICATOR_LOWER_UNLK3_LIGHT" };
    public const string MasterWarning = A300Announcements.MasterWarningKey;   // batch-covered, not a board light

    public static readonly string[] Lights = new[]
    {
        ExtPowerAvailLight, ApuAvailLight, ApuStartOnLight, StartValve1Light, StartValve2Light, FireHandleEng1Light,
        FireHandleEng2Light, FireHandleApuLight, Eng1Squib1Light, Eng1Squib2Light, Eng2Squib1Light, Eng2Squib2Light,
        ApuSquibLight, GpwsLight, MasterWarning,
    }.Concat(CargoSmokeLights).Concat(GearDownLights).Concat(GearUnlockedLights).ToArray();

    // ---------------------------------------------------------------- other values
    public const string LightPowerAc = A300LampBoard.AcPowerKey, LightPowerDc = A300LampBoard.DcPowerKey;
    public const string ProfileLight = "A300_FCU_LT_PROFILE", NavLight = "A300_FCU_LT_NAV";
    public const string AutobrakeLevel = A300Autobrake.LevelKey;             // 3 max
    public static readonly string[] IrsAligned = A300IrsAlignment.All.Select(l => l.AlignedKey).ToArray();
    public const string OnGround = "SIM_ON_GROUND", GroundSpeed = "GROUND_VELOCITY";

    // Readouts read on request, for the live values.
    public const string FuelTotal = A300Readouts.FuelTotalKey;
    public const string BaroCaptain = A300Readouts.BaroCaptainKey, BaroFirstOfficer = A300Readouts.BaroFirstOfficerKey;
    public const string LandingElevation = "A300_RO_LANDING_ELEV", PitchTrim = "A300_RO_PITCH_TRIM";
    public const string FlexTemperature = A300Readouts.FlexTemperatureKey, Squawk = A300Readouts.SquawkCodeKey;
    public const string Minimums = A300Readouts.MinimumsKey, DeltaP = "A300_SD_DELTA_P";
    public const string ApuSpeed = "A300_SD_APU_N";                          // percent, the ECAM APU page's


    // ---------------------------------------------------------------- synthetic fields (A300FoStateEvaluator)
    public const string Eng1N2Field = "FO_ENG1_N2", Eng2N2Field = "FO_ENG2_N2";
    public const string EnginesStopped = "FO_ENGINES_STOPPED", EnginesSpooledDown = "FO_ENGINES_SPOOLED_DOWN";
    public const string GearUp = "FO_GEAR_UP", GearDown = "FO_GEAR_DOWN";
    public const string IrsAllAligned = "FO_IRS_ALIGNED";
    public const string FlapsTakeoff = "FO_FLAPS_TAKEOFF";
    public const string TransponderOn = "FO_TRANSPONDER_ON";
    public const string ApuStartingOrAvailable = "FO_APU_STARTING_OR_AVAILABLE";
    public const string RudderTrimZero = "FO_RUDDER_TRIM_ZERO";
    public const string BrakesBelow100 = "FO_BRAKES_BELOW_100";   // iniBuilds' Parking brake fan test
    public const string ApuOrExtPower = "FO_APU_OR_EXT_POWER";   // something to take the electrics at shutdown

    // ---------------------------------------------------------------- executor sequences (not rows)
    public const string LightUp1 = "FO_LIGHT_UP_1", LightUp2 = "FO_LIGHT_UP_2";
    public const string AnnunciatorTest = "FO_TEST_ANNUNCIATOR", WeatherRadarTest = "FO_TEST_WEATHER_RADAR";

    private static (string Key, int Target)[] Each(int target, params string[] keys) => keys.Select(k => (k, target)).ToArray();

    /// <summary>A line that sets several rows, read back as one field: every row read and at its target.</summary>
    public static readonly IReadOnlyDictionary<string, (string Key, int Target)[]> AllAt =
        new Dictionary<string, (string Key, int Target)[]>(StringComparer.Ordinal)
        {
            ["FO_BATTERIES_ON"] = Each(1, Batt1, Batt2, Batt3),
            ["FO_BATTERIES_OFF"] = Each(0, Batt1, Batt2, Batt3),
            ["FO_WIPERS_OFF"] = Each(0, WiperCpt, WiperFo),
            ["FO_FUEL_LEVERS_OFF"] = Each(0, FuelLever1, FuelLever2),
            ["FO_IRS_NAV"] = Each(1, Irs1, Irs2, Irs3),
            ["FO_IRS_OFF"] = Each(0, Irs1, Irs2, Irs3),
            ["FO_EXT_LIGHTS_PREFLIGHT"] = new[] { (Nose, 2), (LandingL, 2), (LandingR, 2), (Wing, 0), (Strobe, 1), (Beacon, 0), (TurnoffL, 0), (TurnoffR, 0), (NavLogo, 1) },
            ["FO_EXT_LIGHTS_OFF"] = new[] { (Beacon, 0), (Strobe, 2), (NavLogo, 2), (LandingL, 2), (LandingR, 2), (Nose, 2), (TurnoffL, 0), (TurnoffR, 0), (Wing, 0) },
            ["FO_ATS_ON"] = Each(1, Ats1, Ats2),
            ["FO_PITCH_YAW_ON"] = Each(1, PitchTrim1, PitchTrim2, YawDamper1, YawDamper2),
            ["FO_ELEC_INDICATION"] = new[] { (AcIndicator, 3), (DcIndicator, 3) },
            ["FO_FUEL_PUMPS_ON"] = Each(1, FuelPumps),
            ["FO_FUEL_PUMPS_OFF"] = Each(0, FuelPumps),
            ["FO_WINDOW_HEAT_ON"] = Each(1, WindowHeat1, WindowHeat2),
            ["FO_PROBE_HEAT_ON"] = Each(1, ProbeCpt, ProbeStby, ProbeFo),
            ["FO_WINDOW_PROBE_HEAT_OFF"] = Each(0, WindowHeat1, WindowHeat2, ProbeCpt, ProbeStby, ProbeFo),
            ["FO_FLIGHT_DIRECTORS_ON"] = Each(1, FdCpt, FdFo),
            ["FO_NAV_SELECTORS_NAV"] = Each(1, NavSelCpt, NavSelFo),
            ["FO_ADF_TONES_ON"] = Each(1, AdfTone1, AdfTone2),
            ["FO_LANDING_LIGHTS_ON"] = Each(0, LandingL, LandingR),
            ["FO_LANDING_LIGHTS_RETRACTED"] = Each(2, LandingL, LandingR),
            ["FO_TURNOFF_LIGHTS_ON"] = Each(1, TurnoffL, TurnoffR),
            ["FO_TURNOFF_LIGHTS_OFF"] = Each(0, TurnoffL, TurnoffR),
            ["FO_DESCENT_LIGHTS_ON"] = new[] { (LandingL, 0), (LandingR, 0), (TurnoffL, 1), (TurnoffR, 1) },
            ["FO_PACKS_ON"] = Each(1, Pack1, Pack2),
            ["FO_WING_ANTI_ICE_OFF"] = Each(0, WingSupply, WingMode),
            ["FO_SIGNS_SET"] = new[] { (SeatBelts, 1), (NoSmoking, 1) },
            ["FO_APU_AND_BATTERIES_OFF"] = new[] { (ApuMaster, 0), (Batt1, 0), (Batt2, 0), (Batt3, 0) },
        };

    private static bool On(double v) => v >= 0.5;
    private static bool Off(double v) => v < 0.5;
    private static bool Is(double v, double p) => Math.Abs(v - p) < 0.5;

    /// <summary>
    /// Buttons whose state is read on another value: pressed only while that value says not done, and confirmed on
    /// it. A press is never sent to a toggle already where it should be (PROF, NAV and the DH buttons toggle).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Field, Func<double, bool> Done)> PressConfirmations =
        new Dictionary<string, (string Field, Func<double, bool> Done)>(StringComparer.Ordinal)
        {
            [Prof] = (ProfileLight, On),
            [Nav] = (NavLight, On),
            [AutobrakeMax] = (AutobrakeLevel, v => Is(v, 3)),
            [EcamApu] = (A300FoVariables.EcamPageKey, v => Is(v, 9)),
            [EcamEng] = (A300FoVariables.EcamPageKey, v => Is(v, 1)),
            [DhCpt] = (A300FoVariables.DhCaptainKey, On),
            [DhFo] = (A300FoVariables.DhFirstOfficerKey, On),
            [RudderTrimReset] = (RudderTrimZero, On),
            [ApuStart] = (ApuStartingOrAvailable, On),
            [MasterWarningButton] = (MasterWarning, Off),
        };
}
