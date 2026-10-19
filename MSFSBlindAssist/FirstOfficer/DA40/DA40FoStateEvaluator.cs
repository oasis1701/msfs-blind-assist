using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.DA40;

/// <summary>
/// The COWS DA40 First Officer's state evaluator (NG and XLS). Reads the SimConnect cache by the
/// definition's own variable keys (DA40S-1): the switches are Continuous and announced (DA40-9),
/// the readouts the flows wait on that the definition keeps OnRequest are polled here each second
/// by the First Officer window (<see cref="OnRequestPollFields"/>). NaN until read, never 0.
///
/// The synthetic <c>FO_*</c> fields are the checks no single variable answers (every breaker in,
/// both doors closed, the engine running by RPM), each NaN while any of its inputs is unread.
/// Constructed parameterless (the poll-silence sweep builds every evaluator by reflection); the
/// profile then sets the airframe and its breaker list.
/// </summary>
public sealed class DA40FoStateEvaluator : LVarStateEvaluator
{
    private DA40Variant _variant = DA40Variant.NG;
    private IReadOnlyList<string> _breakerKeys = Array.Empty<string>();

    /// <summary>The airframe this evaluator reads. Set once by the profile.</summary>
    public DA40Variant Variant
    {
        get => _variant;
        set => _variant = value;
    }

    public bool IsNg => _variant == DA40Variant.NG;

    /// <summary>The 34 <c>L:CB_*</c> breaker keys of this airframe (DA40F-1), for
    /// <see cref="BreakersInField"/>.</summary>
    public void SetBreakerKeys(IReadOnlyList<string> keys) => _breakerKeys = keys;

    // ---- synthetic fields ----
    /// <summary>Every DA40 breaker in (0); NaN while any is unread or none is known.</summary>
    public const string BreakersInField = "FO_BREAKERS_IN";
    /// <summary>The canopy and the rear door closed.</summary>
    public const string DoorsClosedField = "FO_DOORS_CLOSED";
    /// <summary>All nine lights of the checklist's "All lights(9)" off: instrument and flood
    /// dimmers at zero, landing, taxi, position, strobe and the three cabin lights.</summary>
    public const string AllLightsOffField = "FO_ALL_LIGHTS_OFF";
    /// <summary>Line-up lights: landing, taxi, position and strobe all on.</summary>
    public const string LineUpLightsField = "FO_LINEUP_LIGHTS";
    /// <summary>Landing, taxi and position lights off (the shutdown's "except strobe").</summary>
    public const string ExteriorLightsOffField = "FO_EXT_LIGHTS_OFF";
    /// <summary>The three cabin lights off.</summary>
    public const string CabinLightsOffField = "FO_CABIN_LIGHTS_OFF";
    /// <summary>The engine turning over 400 RPM (the NG's running rule, DA40E-16; the XLS's
    /// magneto check uses the same figure).</summary>
    public const string EngineRunningField = "FO_ENGINE_RUNNING";
    /// <summary>The engine below 200 RPM: stopped (DA40E-16).</summary>
    public const string EngineStoppedField = "FO_ENGINE_STOPPED";
    /// <summary>Oil pressure in its green arc (DA40InstrumentBands).</summary>
    public const string OilPressureGreenField = "FO_OIL_PRESSURE_GREEN";
    /// <summary>NG: oil pressure out of the lower red, the start's three-second check.</summary>
    public const string OilPressureOutOfRedField = "FO_OIL_PRESSURE_OUT_OF_RED";
    /// <summary>NG: idle RPM 710 plus or minus 30 (the COWS After engine start page).</summary>
    public const string IdleRpmField = "FO_IDLE_RPM";
    /// <summary>The engine temperatures for the run-up: NG oil, coolant and gearbox in the green;
    /// XLS oil at or above 100 F (the COWS page's clue) and no cylinder head past its green.</summary>
    public const string EngineTempsField = "FO_ENGINE_TEMPS";
    /// <summary>XLS: fuel pressure in its green arc.</summary>
    public const string FuelPressureGreenField = "FO_FUEL_PRESSURE_GREEN";
    /// <summary>NG: neither ECU failed its test (both result flags Pass).</summary>
    public const string EcuTestPassedField = "FO_ECU_TEST_PASSED";
    /// <summary>The power lever (NG) or throttle (XLS) at idle.</summary>
    public const string PowerIdleField = "FO_POWER_IDLE";
    /// <summary>The GFC 700 pre-flight test passed (AFCS_PFT 2 or 3, DA40A-5).</summary>
    public const string AfcsTestPassedField = "FO_AFCS_PFT_PASSED";
    /// <summary>The standby altimeter within 0.01 inHg of the G1000's setting.</summary>
    public const string AltimetersAgreeField = "FO_ALTIMETERS_AGREE";
    /// <summary>XLS: the fuel selector on the tank holding less fuel (Before engine start).</summary>
    public const string SelectorOnLowerTankField = "FO_SELECTOR_LOWER_TANK";
    /// <summary>XLS: the fuel selector on the tank holding more fuel (Taxi, Approach).</summary>
    public const string SelectorOnFullerTankField = "FO_SELECTOR_FULLER_TANK";
    /// <summary>XLS: throttle idle, propeller high RPM, mixture cut-off (Before engine start).</summary>
    public const string XlsLeversSetField = "FO_XLS_LEVERS_SET";
    /// <summary>The flaps at the setting below their limit speed: 1 when the indicated airspeed is
    /// within the limit for flaps LDG, 0 when it is above it.</summary>
    public const string BelowVfeLandingField = "FO_BELOW_VFE_LDG";

    /// <summary>NG: the start switches safe before power-up: engine master off, fuel pump off, ECU
    /// voter AUTO (the COWS line "Engine master, Fuel pump, Voter: Off/Auto").</summary>
    public const string StartSwitchesSafeField = "FO_START_SWITCHES_SAFE";
    /// <summary>XLS: the avionics master, the ESS bus and the alternator master off (the pre-power
    /// "Masters(4): Off" bar the battery master, which the same page then switches on).</summary>
    public const string XlsMastersOffField = "FO_XLS_MASTERS_OFF";
    /// <summary>The electric fuel pump switch of this airframe (the NG's fuel pumps, the XLS's
    /// electric fuel pump): 1 on, 0 off.</summary>
    public const string FuelPumpOnField = "FO_FUEL_PUMP";
    /// <summary>On the ground (the stock SIM_ON_GROUND): the Shutdown's guard (FO-25).</summary>
    public const string OnGroundField = "SIM_ON_GROUND";

    /// <summary>The idle RPM band, COWS's "Rpm: Check 710 +/- 30".</summary>
    public const double IdleRpmLow = 680, IdleRpmHigh = 740;
    /// <summary>The COWS XLS run-up clue: "Oil above 100F is fine for run-up".</summary>
    public const double XlsRunUpOilTempF = 100;
    /// <summary>A lever at or below this percentage is at its stop (idle, cut-off).</summary>
    public const double LeverStopPercent = 2;
    /// <summary>The XLS propeller at or above this percentage is at high RPM.</summary>
    public const double PropHighRpmPercent = 98;

    private static readonly string[] SharedPollFields =
    {
        // The two dimmers of "All lights(9)": OnRequest on the Lighting panel.
        "DA40_LIGHT_INSTRUMENT_SET", "DA40_LIGHT_FLOOD_SET",
        // The flap travel test.
        "DA40_FLAPS_TRAVEL_LEFT",
        // The pitot heat test's lamp.
        "DA40_ICE_PITOT_STATE",
    };

    private static readonly string[] NgPollFields =
    {
        "DA40_ECU_TEST_ACTIVE",
    };

    private static readonly string[] XlsPollFields =
    {
        "DA40_XLS_FUEL_LEFT", "DA40_XLS_FUEL_RIGHT", "DA40_XLS_FUEL_PRESSURE",
    };

    public override IReadOnlyList<string> OnRequestPollFields =>
        IsNg ? SharedPollFields.Concat(NgPollFields).ToArray() : SharedPollFields.Concat(XlsPollFields).ToArray();

    /// <summary>The engine's RPM key on this airframe: the NG's sensed propeller RPM (DA40E-9),
    /// the XLS's tachometer.</summary>
    public string RpmKey => IsNg ? "DA40_POWER_RPM" : "DA40_XLS_RPM";

    /// <summary>The power lever (NG) or throttle (XLS) key, percent.</summary>
    public string PowerLeverKey => IsNg ? "DA40_POWER_LEVER_SET" : "DA40_XLS_THROTTLE_SET";

    /// <summary>The electric pump key: the NG's fuel pumps, the XLS's electric fuel pump.</summary>
    public string FuelPumpKey => IsNg ? "DA40_FUEL_PUMPS" : "DA40_XLS_FUEL_PUMP";

    /// <summary>XLS: the tank with less fuel, 0 = LEFT, 1 = RIGHT (the selector's own values), or
    /// null while either tank is unread. Equal tanks answer LEFT.</summary>
    public int? LowerTank()
    {
        double left = GetValue("DA40_XLS_FUEL_LEFT"), right = GetValue("DA40_XLS_FUEL_RIGHT");
        if (double.IsNaN(left) || double.IsNaN(right)) return null;
        return right < left ? 1 : 0;
    }

    /// <summary>XLS: the tank with more fuel, or null while either is unread. Equal tanks answer
    /// LEFT.</summary>
    public int? FullerTank()
    {
        double left = GetValue("DA40_XLS_FUEL_LEFT"), right = GetValue("DA40_XLS_FUEL_RIGHT");
        if (double.IsNaN(left) || double.IsNaN(right)) return null;
        return right > left ? 1 : 0;
    }

    /// <summary>The G1000's altimeter setting in hundredths of an inch (2992), or null unread:
    /// the integer target the standby altimeter step writes.</summary>
    public int? G1000BaroHundredths()
    {
        double inHg = GetValue("DA40_G1000_BARO");
        return double.IsNaN(inHg) || inHg < 25 || inHg > 35 ? null : (int)Math.Round(inHg * 100);
    }

    /// <summary>"29.92 inches", the G1000's setting as the read-back's spoken value, or null.</summary>
    public string? BaroText()
    {
        double inHg = GetValue("DA40_G1000_BARO");
        if (double.IsNaN(inHg)) return null;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:0} hectopascals, {1:0.00} inches", inHg * 33.8639, inHg);
    }

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            case StartSwitchesSafeField:
                value = AllOf(new[] { "DA40_START_ENGINE_MASTER", "DA40_FUEL_PUMPS", "DA40_ECU_VOTER" },
                    (key, v) => key == "DA40_ECU_VOTER" ? Math.Abs(v - 1) < 0.5 : v < 0.5);
                return true;
            case XlsMastersOffField:
                value = AllOf(new[] { "DA40_ELEC_AVIONICS_MASTER", "DA40_ELEC_ESS_BUS", "DA40_ELEC_ALT_MASTER" }, v => v < 0.5);
                return true;
            case FuelPumpOnField:
                value = GetValue(FuelPumpKey);
                return true;
            case BreakersInField:
                value = AllOf(_breakerKeys, v => v < 0.5);
                return true;
            case DoorsClosedField:
                value = AllOf(new[] { "DA40_DOOR_CANOPY", "DA40_DOOR_REAR" }, v => v < 1);
                return true;
            case AllLightsOffField:
                value = AllOf(new[]
                {
                    "DA40_LIGHT_INSTRUMENT_SET", "DA40_LIGHT_FLOOD_SET", "DA40_LIGHT_LANDING", "DA40_LIGHT_TAXI",
                    "DA40_LIGHT_POSITION", "DA40_LIGHT_STROBE", "DA40_LIGHT_CABIN_RIGHT", "DA40_LIGHT_CABIN_LEFT",
                    "DA40_LIGHT_CABIN_BAGGAGE",
                }, v => v < 0.5);
                return true;
            case LineUpLightsField:
                value = AllOf(new[] { "DA40_LIGHT_LANDING", "DA40_LIGHT_TAXI", "DA40_LIGHT_POSITION", "DA40_LIGHT_STROBE" },
                    v => v > 0.5);
                return true;
            case ExteriorLightsOffField:
                value = AllOf(new[] { "DA40_LIGHT_LANDING", "DA40_LIGHT_TAXI", "DA40_LIGHT_POSITION" }, v => v < 0.5);
                return true;
            case CabinLightsOffField:
                value = AllOf(new[] { "DA40_LIGHT_CABIN_RIGHT", "DA40_LIGHT_CABIN_LEFT", "DA40_LIGHT_CABIN_BAGGAGE" },
                    v => v < 0.5);
                return true;
            case EngineRunningField:
                value = One(GetValue(RpmKey), rpm => rpm > CowsDA40Definition.EngineRunningRpm);
                return true;
            case EngineStoppedField:
                value = One(GetValue(RpmKey), rpm => rpm < CowsDA40Definition.EngineStoppedRpm);
                return true;
            case OilPressureGreenField:
            {
                string key = IsNg ? "DA40_START_OIL_PRESSURE" : "DA40_XLS_OIL_PRESSURE";
                value = InBand(key, GetValue(key), b => b == GaugeBand.Normal);
                return true;
            }
            case OilPressureOutOfRedField:
            {
                string key = IsNg ? "DA40_START_OIL_PRESSURE" : "DA40_XLS_OIL_PRESSURE";
                value = InBand(key, GetValue(key), b => b != GaugeBand.LowerRed);
                return true;
            }
            case IdleRpmField:
                value = One(GetValue(RpmKey), rpm => rpm >= IdleRpmLow && rpm <= IdleRpmHigh);
                return true;
            case EngineTempsField:
                value = IsNg
                    ? AllOf(new[] { "DA40_START_OIL_TEMP", "DA40_START_COOLANT_TEMP", "DA40_START_GEARBOX_TEMP" },
                        (key, v) => DA40InstrumentBands.For(key)!.Classify(v) == GaugeBand.Normal)
                    : Both(GetValue("DA40_XLS_OIL_TEMP"), GetValue("DA40_XLS_CHT_HOT"),
                        (oil, cht) => oil >= XlsRunUpOilTempF
                                      && DA40InstrumentBands.For("DA40_XLS_CHT_HOT")!.Classify(cht) == GaugeBand.Normal);
                return true;
            case FuelPressureGreenField:
                value = InBand("DA40_XLS_FUEL_PRESSURE", GetValue("DA40_XLS_FUEL_PRESSURE"), b => b == GaugeBand.Normal);
                return true;
            case EcuTestPassedField:
                value = Both(GetValue("DA40_ECU_TEST_FAIL_A"), GetValue("DA40_ECU_TEST_FAIL_B"),
                    (a, b) => a < 0.5 && b < 0.5);
                return true;
            case PowerIdleField:
                value = One(GetValue(PowerLeverKey), v => v <= LeverStopPercent);
                return true;
            case AfcsTestPassedField:
                value = One(GetValue("DA40_AP_PFT"), v => v >= 1.5);
                return true;
            case AltimetersAgreeField:
                value = Both(GetValue("DA40_G1000_BARO"), GetValue("DA40_STBY_ALTIMETER_SET"),
                    (g1000, standby) => Math.Abs(g1000 - standby) < 0.015);
                return true;
            case SelectorOnLowerTankField:
                value = Selector(LowerTank());
                return true;
            case SelectorOnFullerTankField:
                value = Selector(FullerTank());
                return true;
            case XlsLeversSetField:
                value = AllOf(new[] { "DA40_XLS_THROTTLE_SET", "DA40_XLS_PROP_SET", "DA40_XLS_MIXTURE_SET" },
                    (key, v) => key == "DA40_XLS_PROP_SET" ? v >= PropHighRpmPercent : v <= LeverStopPercent);
                return true;
            case BelowVfeLandingField:
                value = One(GetValue("DA40_AIRSPEED"),
                    kias => !DA40Speeds.For(_variant).ExceedsVfe(kias, 2));
                return true;
        }

        value = double.NaN;
        return false;
    }

    // The selector's position against a tank choice: NaN while either is unknown.
    private double Selector(int? tank)
    {
        double selector = GetValue("DA40_XLS_FUEL_SELECTOR");
        if (tank is null || double.IsNaN(selector)) return double.NaN;
        return Math.Abs(selector - tank.Value) < 0.5 ? 1 : 0;
    }

    private static double One(double v, Func<double, bool> test) => double.IsNaN(v) ? double.NaN : test(v) ? 1 : 0;

    private static double InBand(string key, double v, Func<GaugeBand, bool> test)
    {
        var band = DA40InstrumentBands.For(key);
        return band == null || double.IsNaN(v) ? double.NaN : test(band.Classify(v)) ? 1 : 0;
    }

    // Every key read and true: NaN while any is unread (an unread input is not a pass), and while
    // the list is empty (nothing known is not "all in").
    private double AllOf(IReadOnlyList<string> keys, Func<double, bool> test) => AllOf(keys, (_, v) => test(v));

    private double AllOf(IReadOnlyList<string> keys, Func<string, double, bool> test)
    {
        if (keys.Count == 0) return double.NaN;
        bool all = true;
        foreach (string key in keys)
        {
            double v = GetValue(key);
            if (double.IsNaN(v)) return double.NaN;
            all &= test(key, v);
        }
        return all ? 1 : 0;
    }
}
