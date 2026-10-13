using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One value shown in a panel's status display.</summary>
public sealed record A300Readout(string Key, string Name, string Panel, string Var, bool IsStock, string Units,
    Func<double, string> Format);

/// <summary>
/// The status box readouts: beside the knobs that set them, the four FCU windows, the three altimeter
/// settings, the flex temperature, the landing elevation and the course knobs; each panel's gauges and
/// composed lines (oxygen, brakes, trims, radios, clocks, IRS, cargo door); and the PFD, ND and standby
/// instrument boxes' values. Every number is formatted with the invariant culture, so a comma-decimal
/// Windows locale never makes "29.92" read "29,92".
/// </summary>
public static class A300Readouts
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string SpeedKey = "A300_RO_FCU_SPEED";
    public const string HeadingKey = "A300_RO_FCU_HEADING";
    public const string AltitudeKey = "A300_RO_FCU_ALTITUDE";
    public const string VerticalSpeedKey = "A300_RO_FCU_VS";
    public const string BaroCaptainKey = "A300_RO_BARO_CPT";
    public const string BaroFirstOfficerKey = "A300_RO_BARO_FO";
    public const string BaroStandbyKey = "A300_RO_BARO_STBY";
    public const string FuelTotalKey = "A300_RO_FUEL_TOTAL";
    public const string FlexTemperatureKey = "A300_RO_FLEX_TEMP";

    // The PFD status box (A300DisplayPanels).
    public const string PfdHeadingKey = "A300_RO_PFD_HEADING";
    public const string PfdAirspeedKey = "A300_RO_PFD_IAS";
    public const string PfdVerticalSpeedKey = "A300_RO_PFD_VS";
    public const string PfdRadioAltitudeKey = "A300_RO_PFD_RA";
    public const string VlsKey = "A300_RO_VLS";
    public const string VmaxKey = "A300_RO_VMAX";
    public const string GreenDotKey = "A300_RO_GREEN_DOT";
    public const string SSpeedKey = "A300_RO_S_SPEED";
    public const string FSpeedKey = "A300_RO_F_SPEED";
    public const string VsSpeedKey = "A300_RO_VS_SPEED";
    public const string MinimumsKey = "A300_RO_MINIMUMS";

    // The ND status box (A300DisplayPanels). VOR 1 and 2 are NAV 1 and 2, and the ILS is NAV 3
    // (measured 2026-10-03: the radio panel's ILS frequency shows on NAV 3).
    public const string WaypointDistanceKey = "A300_RO_ND_WPT_DIST";
    public const string TrueAirspeedKey = "A300_RO_ND_TAS";
    public const string WindDirectionKey = "A300_RO_ND_WIND_DIR";
    public const string WindSpeedKey = "A300_RO_ND_WIND_SPEED";
    public const string Vor1FrequencyKey = "A300_RO_ND_VOR1_FREQ";
    public const string Dme1Key = "A300_RO_ND_DME1";
    public const string Vor2FrequencyKey = "A300_RO_ND_VOR2_FREQ";
    public const string Dme2Key = "A300_RO_ND_DME2";
    public const string IlsFrequencyKey = "A300_RO_ND_ILS_FREQ";
    public const string IlsCourseKey = "A300_RO_ILS_COURSE";
    public const string LocalizerKey = "A300_RO_ND_LOC";
    public const string GlideslopeKey = "A300_RO_ND_GS";
    public const string Adf1FrequencyKey = "A300_RO_ND_ADF1_FREQ";
    public const string Adf2FrequencyKey = "A300_RO_ND_ADF2_FREQ";

    // The standby instruments box.
    public const string StandbyAltitudeKey = "A300_RO_STBY_ALTITUDE";
    public const string StandbyCompassKey = "A300_RO_STBY_COMPASS";
    public const string SquawkCodeKey = "A300_RO_SQUAWK";
    public const string NavVor1Key = "A300_RO_NAV_VOR1";
    public const string NavVor2Key = "A300_RO_NAV_VOR2";
    public const string NavIlsKey = "A300_RO_NAV_ILS";

    public static readonly IReadOnlyList<A300Readout> All =
    [
        new A300Readout("A300_RO_FCU_SPEED", "Speed window", "FCU", "INI_Airspeed_Dial", false, "number",
            v => $"{Whole(v)} knots"),
        new A300Readout("A300_RO_FCU_HEADING", "Heading window", "FCU", "INI_HEADING_DIAL", false, "number",
            v => $"{Heading(v)} degrees"),
        new A300Readout("A300_RO_FCU_ALTITUDE", "Altitude window", "FCU", "INI_Altitude_Dial", false, "number",
            v => $"{Math.Round(v).ToString("#,0", Inv)} feet"),
        new A300Readout("A300_RO_FCU_VS", "Vertical speed window", "FCU", "INI_vvi_dial", false, "number",
            v => $"{Whole(v)} feet per minute"),
        new A300Readout("A300_RO_BARO_CPT", "Captain altimeter setting", "Captain Side", "KOHLSMAN SETTING MB:1", true, "Millibars",
            Altimeter),
        new A300Readout(BaroFirstOfficerKey, "First officer altimeter setting", "First Officer Side", "KOHLSMAN SETTING MB:2", true, "Millibars",
            Altimeter),
        new A300Readout(BaroStandbyKey, "Standby altimeter setting", "Standby Instruments", "KOHLSMAN SETTING MB:3", true, "Millibars",
            Altimeter),
        new A300Readout("A300_RO_FLEX_TEMP", "Flex temperature", "Thrust Rating Panel", "INI_FLEX_TEMPERATURE", false, "number",
            v => $"{Whole(v)} degrees"),
        new A300Readout("A300_RO_LANDING_ELEV", "Landing elevation", "Landing Elevation", "INI_landing_elevation", false, "number",
            v => $"{Whole(v)} feet"),
        new A300Readout("A300_RO_VOR1_COURSE", "VOR 1 course", "Navigation Radios", "NAV OBS:1", true, "Degrees",
            v => $"{Heading(v)} degrees"),
        new A300Readout("A300_RO_VOR2_COURSE", "VOR 2 course", "Navigation Radios", "NAV OBS:2", true, "Degrees",
            v => $"{Heading(v)} degrees"),
        new A300Readout(IlsCourseKey, "ILS course", "Navigation Radios", "INI_ils_course", false, "number",
            v => $"{Heading(v)} degrees"),
        // The frequency selectors' windows, as the ND box's lines read them.
        new A300Readout(NavVor1Key, "VOR 1", "Navigation Radios", "NAV ACTIVE FREQUENCY:1", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(NavVor2Key, "VOR 2", "Navigation Radios", "NAV ACTIVE FREQUENCY:2", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(NavIlsKey, "ILS", "Navigation Radios", "NAV ACTIVE FREQUENCY:3", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(FuelTotalKey, "Total fuel", "Fuel", "FUEL TOTAL QUANTITY WEIGHT", true, "pounds",
            v => $"{Math.Round(v).ToString("#,0", Inv)} pounds"),

        // The overhead oxygen panel's six needles, from their animation code. The LP faces read psi, the HP
        // faces "PSI x 1000" (0 to 2). iniBuilds' "HIGH_PRESSURE_CURRENT" is the courier low pressure gauge:
        // it rises with the courier low pressure supply.
        new A300Readout("A300_RO_OXY_CREW_LP", "Crew oxygen low pressure", "Oxygen", "INI_OXYGEN_LOW_PRESSURE_CURRENT", false, "number", Psi),
        new A300Readout("A300_RO_OXY_COURIER_LP", "Courier oxygen low pressure", "Oxygen", "INI_OXYGEN_HIGH_PRESSURE_CURRENT", false, "number", Psi),
        new A300Readout("A300_RO_OXY_HP_1", "Crew oxygen high pressure 1", "Oxygen", "INI_OXYGEN_HIGH_PRESSURE_CURRENT1", false, "number", ThousandsPsi),
        new A300Readout("A300_RO_OXY_HP_2", "Crew oxygen high pressure 2", "Oxygen", "INI_OXYGEN_HIGH_PRESSURE_CURRENT2", false, "number", ThousandsPsi),
        new A300Readout("A300_RO_OXY_HP_3", "Crew oxygen high pressure 3", "Oxygen", "INI_OXYGEN_HIGH_PRESSURE_CURRENT3", false, "number", ThousandsPsi),
        new A300Readout("A300_RO_OXY_HP_4", "Crew oxygen high pressure 4", "Oxygen", "INI_OXYGEN_HIGH_PRESSURE_CURRENT4", false, "number", ThousandsPsi),

        // The main panel's triple brake indicator, from its needles' animation code: ACCU PRESS (0 to 4) and the
        // two BRAKES needles (0 to 3), "PSI x 1000" (the gauge face).
        new A300Readout("A300_RO_BRAKE_ACCU", "Brake accumulator pressure", "Autobrake", "INI_BRAKE_PRESSURE_ACCU_PRESS", false, "number", ThousandsPsi),
        new A300Readout("A300_RO_BRAKE_LEFT", "Left brake pressure", "Autobrake", "INI_BRAKE_PRESSURE_LEFT", false, "number", ThousandsPsi),
        new A300Readout("A300_RO_BRAKE_RIGHT", "Right brake pressure", "Autobrake", "INI_BRAKE_PRESSURE_RIGHT", false, "number", ThousandsPsi),

        // The flap and slat indicator: the surfaces' own angles, which it shows (slats 15, flaps 0 at the first
        // detent, 2026-10-10); and the trims (A300Trim): the rudder trim line is composed from its display.
        new A300Readout("A300_RO_SLATS", "Slats", "Flaps and Speed Brake", "LEADING EDGE FLAPS LEFT ANGLE", true, "degrees", v => $"{Whole(v)} degrees"),
        new A300Readout("A300_RO_FLAPS", "Flaps", "Flaps and Speed Brake", "TRAILING EDGE FLAPS LEFT ANGLE", true, "degrees", v => $"{Whole(v)} degrees"),
        new A300Readout("A300_RO_PITCH_TRIM", "Pitch trim", A300Trim.Panel, "ELEVATOR TRIM POSITION", true, "degrees", A300Trim.Pitch),
        new A300Readout(A300Trim.RudderKey, "Rudder trim", A300Trim.Panel, "INI_RUDDER_TRIM_DISPLAY3", false, "number", _ => "unavailable"),

        // The weather radar antenna tilt the tilt knob sets (its slider runs 0 to 100, the tilt 15 down to 15 up).
        new A300Readout("A300_RO_WXR_TILT", "Antenna tilt", "Weather Radar", "INI_WXR_TILT", false, "number", Tilt),

        // The transponder code as its display shows it, read as BCO16 as the FBW A320 and A380 read it.
        new A300Readout(SquawkCodeKey, "Squawk code", "Transponder", "TRANSPONDER CODE:1", true, "BCO16", Squawk),

        // Each IRS's alignment (A300IrsAlignment): its line is composed from its time remaining and flags.
        .. A300IrsAlignment.All.Select(l => new A300Readout(l.LineKey, $"IRS {l.Irs} alignment", A300IrsAlignment.Panel,
            l.TimeVar, false, "number", _ => "unavailable")),

        // The main cargo door (A300CargoDoor): its line is composed from its travel and flags.
        new A300Readout(A300CargoDoor.StatusKey, "Cargo door", A300CargoDoor.Panel, A300CargoDoor.TravelVar, false, "number", _ => "unavailable"),

        // The VHF and ADF panels (A300Radios): each line is composed from both windows and the transfer switch,
        // whose variable carries it; and each ADF's bearing, read only while it has a signal.
        .. RadioLines("VHF 1"), .. RadioLines("VHF 2"),
        .. RadioLines("ADF 1"),
        new A300Readout("A300_RO_ADF1_BEARING", "ADF 1 bearing", "ADF Radios", "ADF RADIAL:1", true, "degrees", _ => "unavailable"),
        .. RadioLines("ADF 2"),
        new A300Readout("A300_RO_ADF2_BEARING", "ADF 2 bearing", "ADF Radios", "ADF RADIAL:2", true, "degrees", _ => "unavailable"),

        // The two clocks (A300Clock): the time line is composed from the clock's GMT digits, so its own value
        // (the seconds) reads only when they are unknown.
        new A300Readout(A300Clock.CaptainTimeKey, "Captain clock", A300Clock.Panel, "INI_CLOCK_GMT_SECONDS", false, "number", _ => "unavailable"),
        new A300Readout("A300_RO_CHRONO_CPT", "Captain chrono", A300Clock.Panel, "INI_CHRONO_TIME", false, "number", A300Clock.Chrono),
        new A300Readout("A300_RO_ET_CPT", "Captain elapsed time", A300Clock.Panel, "INI_ET_TIME", false, "number", A300Clock.Elapsed),
        new A300Readout(A300Clock.FirstOfficerTimeKey, "First officer clock", A300Clock.Panel, "INI_CLOCK_GMT_SECONDS_FO", false, "number", _ => "unavailable"),
        new A300Readout("A300_RO_CHRONO_FO", "First officer chrono", A300Clock.Panel, "INI_CHRONO_TIME_FO", false, "number", A300Clock.Chrono),
        new A300Readout("A300_RO_ET_FO", "First officer elapsed time", A300Clock.Panel, "INI_ET_TIME_FO", false, "number", A300Clock.Elapsed),

        // The PFD: what the speed tape, attitude and altitude show. VMAX is the top of the tape
        // (VMO, or the gear or flap limit: 270 with the gear down), green dot, S and F are read only
        // at the flap settings the tape shows them (the definition's display text), and VS is the
        // aircraft's own name for the speed at the bottom of the tape.
        new A300Readout(PfdHeadingKey, "Heading", "PFD", "PLANE HEADING DEGREES MAGNETIC", true, "degrees", Heading),
        new A300Readout(PfdAirspeedKey, "Indicated airspeed", "PFD", "AIRSPEED INDICATED", true, "knots",
            v => $"{Whole(v)} knots"),
        new A300Readout(PfdVerticalSpeedKey, "Vertical speed", "PFD", "VERTICAL SPEED", true, "feet per minute", A300DisplayText.VerticalSpeed),
        new A300Readout(PfdRadioAltitudeKey, "Radio altitude", "PFD", "RADIO HEIGHT", true, "feet", A300DisplayText.Feet),
        new A300Readout(VlsKey, "VLS", "PFD", "INI_VLS_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(VmaxKey, "VMAX", "PFD", "INI_max_speed", false, "number", A300DisplayText.Speed),
        new A300Readout(GreenDotKey, "Green dot", "PFD", "FMGEC_GD_SPD", false, "number", A300DisplayText.Speed),
        new A300Readout(SSpeedKey, "S speed", "PFD", "FCPC_S_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(FSpeedKey, "F speed", "PFD", "FCPC_F_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(VsSpeedKey, "VS", "PFD", "INI_VS_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(MinimumsKey, "Minimums", "PFD", "INI_MINIMUMS_PILOT", false, "number", A300DisplayText.Minimums),
        // The ILS deviation, in dots, composed with the inputs that decide whether the PFD draws it (A300IlsDeviation).
        new A300Readout(A300IlsDeviation.LocalizerKey, "Localizer deviation", "PFD", A300IlsDeviation.LocalizerVar, false, "number", _ => "unavailable"),
        new A300Readout(A300IlsDeviation.GlideslopeKey, "Glideslope deviation", "PFD", A300IlsDeviation.GlideslopeVar, false, "number", _ => "unavailable"),

        // The ND. The waypoint's name and bearing are not aircraft variables (the ND computes them
        // from its flight plan); the MCDU's F-PLN page reads them.
        new A300Readout(WaypointDistanceKey, "Distance to next waypoint", "ND", "FMGS_DIST", false, "number", A300DisplayText.WaypointDistance),
        new A300Readout(TrueAirspeedKey, "True airspeed", "ND", "AIRSPEED TRUE", true, "knots", A300DisplayText.Knots),
        new A300Readout(WindDirectionKey, "Wind direction", "ND", "AMBIENT WIND DIRECTION", true, "degrees", A300DisplayText.WindDirection),
        new A300Readout(WindSpeedKey, "Wind speed", "ND", "AMBIENT WIND VELOCITY", true, "knots", A300DisplayText.Knots),
        new A300Readout(Vor1FrequencyKey, "VOR 1", "ND", "NAV ACTIVE FREQUENCY:1", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(Dme1Key, "DME 1", "ND", "NAV DME:1", true, "nautical miles", A300DisplayText.Dme),
        new A300Readout(Vor2FrequencyKey, "VOR 2", "ND", "NAV ACTIVE FREQUENCY:2", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(Dme2Key, "DME 2", "ND", "NAV DME:2", true, "nautical miles", A300DisplayText.Dme),
        new A300Readout(IlsFrequencyKey, "ILS", "ND", "NAV ACTIVE FREQUENCY:3", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(LocalizerKey, "Localizer", "ND", "NAV HAS LOCALIZER:3", true, "Bool", A300DisplayText.Received),
        new A300Readout(GlideslopeKey, "Glideslope", "ND", "NAV HAS GLIDE SLOPE:3", true, "Bool", A300DisplayText.Received),
        new A300Readout(Adf1FrequencyKey, "ADF 1", "ND", "ADF ACTIVE FREQUENCY:1", true, "KHz", A300DisplayText.Kilohertz),
        new A300Readout(Adf2FrequencyKey, "ADF 2", "ND", "ADF ACTIVE FREQUENCY:2", true, "KHz", A300DisplayText.Kilohertz),

        // The standby instruments: the standby altimeter has its own baro setting (altimeter 3).
        new A300Readout(StandbyAltitudeKey, "Standby altitude", "Standby Instruments", "INDICATED ALTITUDE:3", true, "feet", A300DisplayText.Feet),
        new A300Readout(StandbyCompassKey, "Standby compass", "Standby Instruments", "WISKEY COMPASS INDICATION DEGREES", true, "degrees", Heading),
    ];

    /// <summary>A knob whose step is read back once it lands, as the FCU's: knob control key → its readout and
    /// the phrase. A numeric confirmation always speaks ([CORE-7]).</summary>
    public static readonly IReadOnlyDictionary<string, (string Readout, Func<double, string> Phrase)> KnobReadBacks =
        new Dictionary<string, (string, Func<double, string>)>(StringComparer.Ordinal)
        {
            ["A300_LANDING_ELEV_SET"] = ("A300_RO_LANDING_ELEV", v => $"Landing elevation {Whole(v)} feet"),
            ["A300_TCAS_BIG"] = (SquawkCodeKey, v => $"Squawk {Squawk(v)}"),
            ["A300_TCAS_SMALL"] = (SquawkCodeKey, v => $"Squawk {Squawk(v)}"),
            ["A300_VOR100_CAPT"] = (NavVor1Key, v => $"VOR 1 {Tuned(v)}"),
            ["A300_VOR10_CAPT"] = (NavVor1Key, v => $"VOR 1 {Tuned(v)}"),
            ["A300_VOR100_FO"] = (NavVor2Key, v => $"VOR 2 {Tuned(v)}"),
            ["A300_VOR10_FO"] = (NavVor2Key, v => $"VOR 2 {Tuned(v)}"),
            ["A300_ILS_100"] = (NavIlsKey, v => $"ILS {Tuned(v)}"),
            ["A300_ILS_10"] = (NavIlsKey, v => $"ILS {Tuned(v)}"),
            ["A300_VOR_CRS_CAPT"] = ("A300_RO_VOR1_COURSE", v => $"VOR 1 course {Heading(Math.Round(v))}"),
            ["A300_VOR_CRS_FO"] = ("A300_RO_VOR2_COURSE", v => $"VOR 2 course {Heading(Math.Round(v))}"),
            ["A300_ILS_CRS"] = (IlsCourseKey, v => $"ILS course {Heading(Math.Round(v))}"),
        };

    private static A300Readout[] RadioLines(string name)
    {
        var radio = A300Radios.All.Single(r => r.Name == name);
        return new[]
        {
            new A300Readout(radio.ActiveKey, $"{name} active", radio.Panel, radio.TransferVar, false, "number", _ => "unavailable"),
            new A300Readout(radio.StandbyKey, $"{name} standby", radio.Panel, radio.TransferVar, false, "number", _ => "unavailable"),
        };
    }

    /// <summary>A VOR or ILS window: "113.15".</summary>
    private static string Tuned(double megahertz) => megahertz.ToString("0.00", Inv);

    /// <summary>"7000": each BCO16 nibble a digit.</summary>
    private static string Squawk(double bcd)
    {
        int word = (int)Math.Round(bcd);
        return $"{(word >> 12) & 0xF}{(word >> 8) & 0xF}{(word >> 4) & 0xF}{word & 0xF}";
    }

    /// <summary>The three speeds the tape shows only at some flap settings.</summary>
    public static readonly IReadOnlyDictionary<string, A300PfdSpeed> FlapSpeeds = new Dictionary<string, A300PfdSpeed>(StringComparer.Ordinal)
    {
        [GreenDotKey] = A300PfdSpeed.GreenDot,
        [SSpeedKey] = A300PfdSpeed.S,
        [FSpeedKey] = A300PfdSpeed.F,
    };

    /// <summary>"1013 hectopascals, 29.92 inches".</summary>
    public static string Altimeter(double millibars) =>
        $"{Hectopascals(millibars)} hectopascals, {Inches(millibars)} inches";

    /// <summary>
    /// The B key, in the fleet's words (the MD-11's, the Fenix's, the FBW Airbuses', the PMDGs' and the iFly's):
    /// "Altimeter standard" while the captain's side is in STD, else "Altimeter: 1018, 30.06", hectopascals then
    /// inches. On the A300 STD is a flag only and the setting keeps its old value ([A300-16]), so a side in STD is
    /// "standard" whatever the number reads.
    /// </summary>
    public static string AltimeterReading(double millibars, bool standard) =>
        standard ? "Altimeter standard" : $"Altimeter: {Hectopascals(millibars)}, {Inches(millibars)}";

    /// <summary>The F and Shift+F keys: the total fuel weight in whole pounds, or whole kilograms, with a thousands
    /// separator ("Fuel 12,345 pounds", "Fuel 5,600 kilograms").</summary>
    public static string Fuel(double pounds, bool kilograms) =>
        kilograms
            ? $"Fuel {Math.Round(pounds * 0.45359237).ToString("#,0", Inv)} kilograms"
            : $"Fuel {Math.Round(pounds).ToString("#,0", Inv)} pounds";

    private static string Hectopascals(double millibars) => Math.Round(millibars).ToString("0", Inv);

    private static string Inches(double millibars) => (millibars * 0.0295299830714).ToString("0.00", Inv);

    /// <summary>Headings are shown as the window shows them: floored, three digits, 360 as 000.</summary>
    public static string Heading(double degrees) =>
        (((int)Math.Floor(degrees) % 360 + 360) % 360).ToString("000", Inv);

    private static string Whole(double value)
    {
        string text = Math.Round(value).ToString("0", Inv);
        return text == "-0" ? "0" : text;
    }

    /// <summary>"3 degrees down", "15 degrees up", "0 degrees".</summary>
    private static string Tilt(double degrees)
    {
        string whole = Whole(Math.Abs(degrees));
        return whole == "0" ? "0 degrees" : $"{whole} degrees {(degrees > 0 ? "up" : "down")}";
    }

    /// <summary>"1,500 psi", as the ECAM pages write pressures.</summary>
    private static string Psi(double value) => $"{Math.Round(value).ToString("#,0", Inv)} psi";

    /// <summary>A "PSI x 1000" face: 1.5 is "1,500 psi".</summary>
    private static string ThousandsPsi(double value) => Psi(value * 1000);
}
