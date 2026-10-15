using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>A typed-value row: a text box and a Set button on a panel (the key contains "_SET").</summary>
public sealed record A300TypedValue(string Key, string Name, string Panel);

/// <summary>What a typed value does: one calculator string and its spoken confirmation, or an error.
/// <paramref name="Before"/> is sent first, <see cref="A300TypedValues.ModeSwitchSettleMs"/> ahead of
/// <paramref name="Rpn"/> (the SPD/MACH switch, which the aircraft applies a moment later).
/// <paramref name="SpeedMode"/> is the SPD/MACH mode a typed speed is in, 1 for Mach and 0 for knots (null for
/// any other value): the mode the FCU is in once <paramref name="Before"/> has gone.</summary>
public sealed record A300TypedResult(string? Rpn, string? Confirmation, string? Error, string? Before = null,
    double? SpeedMode = null)
{
    public static A300TypedResult Fail(string error) => new(null, null, error);
}

/// <summary>
/// The A300's typed values. Every write path below was measured live at the gate (2026-10-03):
/// <list type="bullet">
/// <item>FCU windows: writing <c>L:INI_Airspeed_Dial</c>, <c>L:INI_HEADING_DIAL</c>,
/// <c>L:INI_Altitude_Dial</c> and <c>L:INI_vvi_dial</c> moved the autopilot's own targets
/// (<c>A:AUTOPILOT HEADING LOCK DIR</c>, <c>AIRSPEED HOLD VAR</c>, <c>ALTITUDE LOCK VAR</c>,
/// <c>VERTICAL HOLD VAR</c>). The knobs stop at speed 100-399 knots, Mach 0.10-0.99, altitude
/// up to 49,000 feet and vertical speed -6,000 to +6,000; heading wraps 359 to 0. In Mach mode
/// (<c>L:INI_Airspeed_is_mach</c> 1) the SAME speed variable holds the Mach number, so a typed value
/// in the other unit presses SPD/MACH first — and waits <see cref="ModeSwitchSettleMs"/>: the
/// aircraft applies the switch a moment later and converts whatever the variable holds then, so a
/// value written in the same string was converted (0.78 became Mach 0.15; measured). A 50 ms pause
/// was enough.</item>
/// <item>VOR 1, VOR 2 and ILS frequencies: the aircraft rebuilds each frequency from its megahertz and
/// kilohertz parts (<c>INI_VOR1_FREQUENCY_MHZ</c> / <c>_KHZ</c>) and drives the stock NAV 1, 2 and 3
/// from them; writing the combined value or the stock NAV radio is undone.</item>
/// <item>VOR courses: the stock <c>K:VOR1_SET</c> / <c>K:VOR2_SET</c> (the A300 mirrors NAV OBS);
/// ILS course: <c>L:INI_ils_course</c>, which the aircraft keeps.</item>
/// <item>VHF standby: the stock standby set events (the A300 mirrors them). Squawk: <c>K:XPNDR_SET</c>.
/// Altimeters: <c>index value (&gt;K:2:KOHLSMAN_SET)</c>, index FIRST (value first does nothing).</item>
/// <item>Flex temperature and landing elevation: never written. The typed value turns the knob click by click
/// (<see cref="KnobTarget"/>, <see cref="A300SteppedKnobs"/>, [A300-30]; measured in cruise, 2026-10-14): the FMS
/// copies its flex temperature into <c>INI_FLEX_TEMPERATURE</c> on every update, so a write is undone.</item>
/// </list>
/// Pure: the definition sends the strings and speaks the confirmations. Every number is formatted
/// with the invariant culture.
/// </summary>
public static class A300TypedValues
{
    public const string SpeedKey = "A300_FCU_SPEED_SET";
    public const string HeadingKey = "A300_FCU_HEADING_SET";
    public const string AltitudeKey = "A300_FCU_ALTITUDE_SET";
    public const string VerticalSpeedKey = "A300_FCU_VS_SET";
    public const string Vor1FrequencyKey = "A300_VOR1_FREQ_SET";
    public const string Vor1CourseKey = "A300_VOR1_COURSE_SET";
    public const string Vor2FrequencyKey = "A300_VOR2_FREQ_SET";
    public const string Vor2CourseKey = "A300_VOR2_COURSE_SET";
    public const string IlsFrequencyKey = "A300_ILS_FREQ_SET";
    public const string IlsCourseKey = "A300_ILS_COURSE_SET";
    public const string Com1StandbyKey = "A300_COM1_STBY_SET";
    public const string Com2StandbyKey = "A300_COM2_STBY_SET";
    public const string SquawkKey = "A300_SQUAWK_SET";
    public const string BaroCaptainKey = "A300_BARO_CPT_SET";
    public const string BaroFirstOfficerKey = "A300_BARO_FO_SET";
    public const string BaroStandbyKey = "A300_BARO_STBY_SET";
    public const string MinimumsKey = "A300_MINIMUMS_SET";
    public const string FlexTemperatureKey = "A300_FLEX_TEMPERATURE_SET";
    public const string LandingElevationKey = "A300_LANDING_ELEVATION_SET";

    public static readonly IReadOnlyList<A300TypedValue> All = new[]
    {
        new A300TypedValue(SpeedKey, "Speed or Mach", "FCU"),
        new A300TypedValue(HeadingKey, "Heading", "FCU"),
        new A300TypedValue(AltitudeKey, "Altitude", "FCU"),
        new A300TypedValue(VerticalSpeedKey, "Vertical speed", "FCU"),
        new A300TypedValue(Vor1FrequencyKey, "VOR 1 frequency", "Navigation Radios"),
        new A300TypedValue(Vor1CourseKey, "VOR 1 course setting", "Navigation Radios"),
        new A300TypedValue(Vor2FrequencyKey, "VOR 2 frequency", "Navigation Radios"),
        new A300TypedValue(Vor2CourseKey, "VOR 2 course setting", "Navigation Radios"),
        new A300TypedValue(IlsFrequencyKey, "ILS frequency", "Navigation Radios"),
        new A300TypedValue(IlsCourseKey, "ILS course setting", "Navigation Radios"),
        new A300TypedValue(Com1StandbyKey, "VHF 1 standby frequency", "VHF Radios"),
        new A300TypedValue(Com2StandbyKey, "VHF 2 standby frequency", "VHF Radios"),
        new A300TypedValue(SquawkKey, "Squawk", "Transponder"),
        new A300TypedValue(BaroCaptainKey, "Altimeter setting", "Captain Side"),
        new A300TypedValue(BaroFirstOfficerKey, "Altimeter setting", "First Officer Side"),
        new A300TypedValue(BaroStandbyKey, "Standby altimeter setting", "Standby Instruments"),
        new A300TypedValue(MinimumsKey, "Decision height", "EFIS Captain"),
        // Named "... setting": the "Flex temperature" and "Landing elevation" status lines sit on the same panels ([VAR-1]).
        new A300TypedValue(FlexTemperatureKey, "Flex temperature setting", "Thrust Rating Panel"),
        new A300TypedValue(LandingElevationKey, "Landing elevation setting", "Landing Elevation"),
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The pause between a SPD/MACH switch and the value written after it (50 ms measured enough).</summary>
    public const int ModeSwitchSettleMs = 300;

    private const string SpeedMachToggle = "1 (>B:AIRLINER_SPDMACH_Set)";

    // What a refusal and a box's hint say is what the pilot types back, so the altitude, vertical speed and decision
    // height ranges are written without a thousands separator: "-6,000" invites "-1,500" (see IsWhole).
    public const string WholeNumberError = "type a whole number, without a thousands separator";
    public const string SpeedError = "100 to 399 knots, or Mach 0.10 to 0.99";
    public const string HeadingError = "0 to 360 degrees";
    public const string AltitudeError = "100 to 49000 feet";
    public const string VerticalSpeedError = "0, or 100 to 6000 feet per minute either way";
    public const string VorError = "108.00 to 117.95 megahertz, in steps of 0.05";
    public const string IlsError = "108.10 to 111.95 megahertz, in steps of 0.05";
    public const string CourseError = "0 to 360 degrees";
    public const string ComError = "118.000 to 136.990 megahertz";
    public const string SquawkError = "four digits, each 0 to 7";
    public const string AltimeterError = "28.20 to 31.30 inches, or 955 to 1060 hectopascals";

    /// <summary>The decision height's range. 1 to 9 feet is refused (<see cref="Minimums"/>), so it is not "0 to 2500
    /// feet", which held a refused 5.</summary>
    public const string MinimumsError = "0, or 10 to 2500 feet";

    /// <summary>The flex temperature knob's own travel, 0 to 72 (<c>INI_flex_temp_up/dn_handler</c>, [A300-21]).</summary>
    public const string FlexTemperatureError = "0 to 72 degrees";

    /// <summary>The landing elevation's range: a judgment (the knob's own limits were not measured); the box says where
    /// the knob stopped if it stops short (<see cref="A300SteppedKnobs"/>).</summary>
    public const string LandingElevationError = "-2000 to 14000 feet";

    /// <summary>
    /// Whether a typed altitude, vertical speed, decision height or landing elevation is a whole number. Both ways of
    /// typing a value (a panel's box and the value boxes) turn a comma into a point, so "-1,500", the thousands separator
    /// the box's own hint used to show, arrives as -1.5 and "1,200" as 1.2. Rounding them wrote a vertical speed of 0 and a
    /// decision height of 1 foot, confirmed aloud; a fraction can only be that typo, so it is refused instead. A round
    /// thousand ("12,000") arrives whole, as 12: see <see cref="TextError"/> and <see cref="SeparatorThen"/>.
    /// </summary>
    private static bool IsWhole(double value) => value == Math.Round(value);

    /// <summary>The values that must be typed as whole numbers (<see cref="IsWhole"/>): the altitude, the vertical speed,
    /// the decision height and the landing elevation, the values in feet.</summary>
    private static bool WantsWholeNumber(string key) =>
        key is AltitudeKey or VerticalSpeedKey or MinimumsKey or LandingElevationKey;

    /// <summary>
    /// What a value box says about the TEXT typed for an altitude, a vertical speed, a decision height or a landing
    /// elevation, before it is read as a number: an integer grouped by a comma ("12,000", "-1,000") or by a space, a
    /// no-break space or a narrow no-break space ("12 000", the grouping comma-decimal cultures write) is refused with
    /// <see cref="WholeNumberError"/>. Read as a number the comma grouping is whole (12, -1), so the fraction rule let it
    /// through to a refusal by a range that holds what was typed ("100 to 49000 feet" for "12,000"), and a decision height
    /// of "1,000" was written as 1 foot; the space grouping is no number, and was answered with that range too. Null for
    /// any other text, and for every other key, where a comma is a decimal point ("121,500" is 121.5).
    /// </summary>
    public static string? TextError(string key, string text) =>
        WantsWholeNumber(key)
        && Regex.IsMatch(text.Trim(), @"^\s*-?\d{1,3}([, \u00A0\u202F]\d{3})+\s*$", RegexOptions.CultureInvariant)
            ? WholeNumberError
            : null;

    /// <summary>
    /// The refusal of an altitude, vertical speed, decision height or landing elevation a thousands separator could have
    /// made, for a path that has only the number: the separator first, then the range, which on its own holds what was
    /// typed. A panel's box hands "12,000" over as 12, so callers use it for the values such an entry gives inside the
    /// range (an altitude of 1 to 49, a vertical speed of 1 to 6 either way, a decision height of 1 to 9), and "12 000" as
    /// NaN, so <see cref="NotANumberError"/> uses it for a value that is no number, the landing elevation's included (its
    /// "5,450" arrives as the fraction 5.45, which <see cref="WholeNumberError"/> refuses alone).
    /// </summary>
    private static string SeparatorThen(string range) => $"{WholeNumberError}; {range}";

    /// <summary>
    /// What a typed value that is no number is answered with: for an altitude, a vertical speed, a decision height or a
    /// landing elevation the separator, then the range (<see cref="SeparatorThen"/>), because "12 000" typed in a panel's
    /// box arrives as NaN and its range alone holds the 12,000 typed; "abc" is answered the same way, the words fitting it
    /// as well. Every other value is answered with its range (<see cref="ErrorFor"/>).
    /// </summary>
    public static string NotANumberError(string key) => WantsWholeNumber(key) ? SeparatorThen(ErrorFor(key)) : ErrorFor(key);

    /// <summary>The plan for a typed value. <paramref name="isMach"/> is the FCU's current speed mode.</summary>
    public static A300TypedResult Plan(string key, double value, bool isMach)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return A300TypedResult.Fail(NotANumberError(key));
        return key switch
        {
            SpeedKey => Speed(value, isMach),
            HeadingKey => Heading(value),
            AltitudeKey => Altitude(value),
            VerticalSpeedKey => VerticalSpeed(value),
            Vor1FrequencyKey => NavFrequency(value, "VOR1", "VOR 1", 108.0, VorError),
            Vor2FrequencyKey => NavFrequency(value, "VOR2", "VOR 2", 108.0, VorError),
            IlsFrequencyKey => value > 111.95 + 1e-6
                ? A300TypedResult.Fail(IlsError)
                : NavFrequency(value, "ILS", "ILS", 108.1, IlsError),
            Vor1CourseKey => Course(value, c => $"{c} (>K:VOR1_SET)", "VOR 1 course"),
            Vor2CourseKey => Course(value, c => $"{c} (>K:VOR2_SET)", "VOR 2 course"),
            IlsCourseKey => Course(value, c => $"{c} (>L:INI_ils_course)", "ILS course"),
            Com1StandbyKey => ComStandby(value, "COM_STBY_RADIO_SET_HZ", "VHF 1 standby"),
            Com2StandbyKey => ComStandby(value, "COM2_STBY_RADIO_SET_HZ", "VHF 2 standby"),
            SquawkKey => Squawk(value),
            BaroCaptainKey => Altimeter(value, new[] { 1 }, "Captain altimeter"),
            BaroFirstOfficerKey => Altimeter(value, new[] { 2 }, "First officer altimeter"),
            BaroStandbyKey => Altimeter(value, new[] { 3 }, "Standby altimeter"),
            MinimumsKey => Minimums(value),
            _ => A300TypedResult.Fail("cannot be set from this panel"),
        };
    }

    public static string ErrorFor(string key) => key switch
    {
        SpeedKey => SpeedError,
        HeadingKey => HeadingError,
        AltitudeKey => AltitudeError,
        VerticalSpeedKey => VerticalSpeedError,
        Vor1FrequencyKey or Vor2FrequencyKey => VorError,
        IlsFrequencyKey => IlsError,
        Vor1CourseKey or Vor2CourseKey or IlsCourseKey => CourseError,
        Com1StandbyKey or Com2StandbyKey => ComError,
        SquawkKey => SquawkError,
        MinimumsKey => MinimumsError,
        FlexTemperatureKey => FlexTemperatureError,
        LandingElevationKey => LandingElevationError,
        _ => AltimeterError,
    };

    /// <summary>Knots (100-399) or Mach (0.10-0.99), whichever the number is; a value in the other
    /// unit from the FCU's current mode presses SPD/MACH first (<see cref="A300TypedResult.Before"/>, which
    /// the caller sends and waits out; never in the same string as the write).</summary>
    public static A300TypedResult Speed(double value, bool isMach)
    {
        bool mach = value < 1.0;
        if (mach)
        {
            double m = Math.Round(value, 2);
            if (m < 0.10 - 1e-9 || m > 0.99 + 1e-9)
                return A300TypedResult.Fail(SpeedError);
            return new($"{m.ToString("0.00", Inv)} (>L:INI_Airspeed_Dial)", $"Mach {m.ToString("0.00", Inv)}", null,
                isMach ? null : SpeedMachToggle, SpeedMode: 1);
        }
        double kt = Math.Round(value);
        if (kt < 100 || kt > 399)
            return A300TypedResult.Fail(SpeedError);
        return new($"{kt.ToString("0", Inv)} (>L:INI_Airspeed_Dial)", $"Speed {kt.ToString("0", Inv)} knots", null,
            isMach ? SpeedMachToggle : null, SpeedMode: 0);
    }

    public static A300TypedResult Heading(double value)
    {
        double h = Math.Round(value);
        if (h < 0 || h > 360)
            return A300TypedResult.Fail(HeadingError);
        if (h == 360)
            h = 0;
        return new($"{h.ToString("0", Inv)} (>L:INI_HEADING_DIAL)", $"Heading {h.ToString("000", Inv)}", null);
    }

    /// <summary>Whole feet, 100 to 49000, to the nearest 100 (the knob's step); a fraction is refused (<see cref="IsWhole"/>).
    /// A refused 1 to 49, what "1,000" to "49,000" typed in a panel's box arrive as, names the separator
    /// (<see cref="SeparatorThen"/>).</summary>
    public static A300TypedResult Altitude(double value)
    {
        if (!IsWhole(value))
            return A300TypedResult.Fail(WholeNumberError);
        double a = Math.Round(value / 100) * 100;
        if (a < 100 || a > 49000)
            return A300TypedResult.Fail(value is >= 1 and <= 49 ? SeparatorThen(AltitudeError) : AltitudeError);
        return new($"{a.ToString("0", Inv)} (>L:INI_Altitude_Dial)", $"Altitude {a.ToString("#,0", Inv)} feet", null);
    }

    /// <summary>
    /// Whole feet per minute, 0 or 100 to 6000 either way, to the nearest 100 (the knob's step). A fraction is
    /// refused (<see cref="IsWhole"/>), and so is a non-zero value under 100 in magnitude, as the MD-11 and the FBW
    /// A320 refuse it: it rounded to 0, and was written and confirmed as a level-off nobody asked for. 0 itself is
    /// the one value under 100 that stays valid. A refused 1 to 6 either way, what "1,000" to "6,000" typed in a
    /// panel's box arrive as, names the separator (<see cref="SeparatorThen"/>).
    /// </summary>
    public static A300TypedResult VerticalSpeed(double value)
    {
        if (!IsWhole(value))
            return A300TypedResult.Fail(WholeNumberError);
        if (value != 0 && Math.Abs(value) < 100)
            return A300TypedResult.Fail(Math.Abs(value) <= 6 ? SeparatorThen(VerticalSpeedError) : VerticalSpeedError);
        double v = Math.Round(value / 100) * 100;
        if (v < -6000 || v > 6000)
            return A300TypedResult.Fail(VerticalSpeedError);
        if (v == 0)
            v = 0;   // never "-0"
        return new($"{v.ToString("0", Inv)} (>L:INI_vvi_dial)", $"Vertical speed {v.ToString("#,0", Inv)} feet per minute", null);
    }

    /// <summary>A 50 kHz-spaced NAV frequency, written as its megahertz and kilohertz parts.</summary>
    public static A300TypedResult NavFrequency(double value, string radio, string spoken, double min, string error)
    {
        int hundredths = (int)Math.Round(value * 100);
        if (hundredths < (int)Math.Round(min * 100) || hundredths > 11795 || hundredths % 5 != 0)
            return A300TypedResult.Fail(error);
        int mhz = hundredths / 100;
        int khz = hundredths % 100;
        string text = $"{mhz.ToString(Inv)}.{khz.ToString("00", Inv)}";
        return new($"{mhz.ToString(Inv)} (>L:INI_{radio}_FREQUENCY_MHZ) {khz.ToString(Inv)} (>L:INI_{radio}_FREQUENCY_KHZ)",
            $"{spoken} {text}", null);
    }

    public static A300TypedResult Course(double value, Func<string, string> rpn, string spoken)
    {
        double c = Math.Round(value);
        if (c < 0 || c > 360)
            return A300TypedResult.Fail(CourseError);
        if (c == 360)
            c = 0;
        return new(rpn(c.ToString("0", Inv)), $"{spoken} {c.ToString("000", Inv)}", null);
    }

    public static A300TypedResult ComStandby(double value, string evt, string spoken)
    {
        long khz = (long)Math.Round(value * 1000);
        if (khz < 118000 || khz > 136990)
            return A300TypedResult.Fail(ComError);
        long hz = khz * 1000;
        return new($"{hz.ToString(Inv)} (>K:{evt})", $"{spoken} {(khz / 1000.0).ToString("0.000", Inv)}", null);
    }

    /// <summary>Four octal digits, as the panel's typed box hands them over (4521 for "4521", 22 for "0022").</summary>
    public static A300TypedResult Squawk(double value)
    {
        if (value < 0 || value > 7777 || value != Math.Floor(value))
            return A300TypedResult.Fail(SquawkError);
        string digits = ((int)value).ToString("0000", Inv);
        if (digits.Any(d => d > '7'))
            return A300TypedResult.Fail(SquawkError);
        int bcd = Convert.ToInt32(digits, 16);
        return new($"{bcd.ToString(Inv)} (>K:XPNDR_SET)", $"Squawk {digits}", null);
    }

    /// <summary>Inches (28.20-31.30) or hectopascals (955-1060), whichever the number is.</summary>
    public static double? Millibars(double value)
    {
        if (value >= 28.2 - 1e-9 && value <= 31.3 + 1e-9)
            return value / 0.0295299830714;
        if (value >= 955 && value <= 1060)
            return value;
        return null;
    }

    /// <summary>
    /// A typed altimeter setting. The aircraft takes it in sixteenths of a hectopascal, so the confirmation is of the
    /// setting that is written, which is what every later read-back says: 29.90 inches is 1012.53 hectopascals, but
    /// 16200 sixteenths is 1012.5, read back as 1012, and the confirmation, worked out from the unrounded entry,
    /// said 1013 (about 3% of the inch entries were a hectopascal out).
    /// </summary>
    public static A300TypedResult Altimeter(double value, IReadOnlyList<int> indexes, string spoken)
    {
        if (Millibars(value) is not double mb)
            return A300TypedResult.Fail(AltimeterError);
        double sixteenths = Math.Round(mb * 16);
        string units = sixteenths.ToString("0", Inv);
        string rpn = string.Join(" ", indexes.Select(i => $"{i.ToString(Inv)} {units} (>K:2:KOHLSMAN_SET)"));
        return new(rpn, $"{spoken} {A300Readouts.Altimeter(sixteenths / 16)}", null);
    }

    /// <summary>
    /// The decision height, written to BOTH pilots' minimums (<c>INI_MINIMUMS_PILOT</c>,
    /// <c>INI_MINIMUMS_FO</c>): they are the stored values the PFD and the aircraft's "minimums"
    /// call-out read, and the DH knob moves them only while DH is selected on its EFIS panel (it steps
    /// the flight path angle otherwise). A direct write held, and nothing rewrote it (measured
    /// 2026-10-04). Whole feet; 0 clears it; a fraction is refused (<see cref="IsWhole"/>). So is 1 to 9 feet,
    /// naming the separator (<see cref="SeparatorThen"/>): no real decision height is that low, and "1,000" and
    /// "2,000" typed in a panel's box arrive as 1 and 2, which were written and confirmed ("Decision height 1
    /// feet"), and with 1 foot set the minimums call-out effectively never came. Every refusal says the range as
    /// "0, or 10 to 2500 feet" (<see cref="MinimumsError"/>), so a refused 5 never hears a range that holds it.
    /// </summary>
    public static A300TypedResult Minimums(double value)
    {
        if (!IsWhole(value))
            return A300TypedResult.Fail(WholeNumberError);
        double ft = value;
        if (ft is >= 1 and <= 9)
            return A300TypedResult.Fail(SeparatorThen(MinimumsError));
        if (ft < 0 || ft > 2500)
            return A300TypedResult.Fail(MinimumsError);
        string text = ft.ToString("0", Inv);
        return new($"{text} (>L:INI_MINIMUMS_PILOT) {text} (>L:INI_MINIMUMS_FO)",
            ft == 0 ? "Decision height not set" : $"Decision height {ft.ToString("#,0", Inv)} feet", null);
    }

    /// <summary>Whether a typed value turns its knob instead of being written: the flex temperature and the landing
    /// elevation (<see cref="A300SteppedKnobs"/>, [A300-30]). <see cref="Plan"/> has no write for them; what they turn
    /// the knob to is <see cref="KnobTarget"/>.</summary>
    public static bool IsKnobTyped(string key) => key is FlexTemperatureKey or LandingElevationKey;

    /// <summary>
    /// What a typed flex temperature or landing elevation turns its knob to, or why it is refused. The range holds what
    /// was typed: a value outside it is refused (73 or 72.4 degrees, 14001 feet), and one inside goes to the knob's
    /// nearest click, a half click away from zero: the flex temperature to the whole degree (0 to 72, the knob's own
    /// travel), the landing elevation to 50 feet (-2000 to 14000, a judgment). A landing elevation is whole feet, as an
    /// altitude is: a fraction is refused (<see cref="WholeNumberError"/>), since "5,450" typed in a panel's box arrives
    /// as 5.45, and no number ("5 450") names the separator, then the range (<see cref="NotANumberError"/>). A round
    /// thousand typed with a comma ("5,000") arrives as 5, which nothing can tell from a field 5 feet up: it is taken,
    /// and the read-back says the 0 feet the knob turned to.
    /// </summary>
    public static (double? Target, string? Error) KnobTarget(string key, double value)
    {
        if (!IsKnobTyped(key))
            return (null, "cannot be set from this panel");
        if (double.IsNaN(value) || double.IsInfinity(value))
            return (null, NotANumberError(key));
        if (key == FlexTemperatureKey)
        {
            if (value is < 0 or > 72)
                return (null, FlexTemperatureError);
            return (Math.Round(value, MidpointRounding.AwayFromZero), null);
        }
        if (!IsWhole(value))
            return (null, WholeNumberError);
        if (value is < -2000 or > 14000)
            return (null, LandingElevationError);
        return (Math.Round(value / 50, MidpointRounding.AwayFromZero) * 50, null);
    }

    /// <summary>Ctrl+B: one entry sets all three altimeters.</summary>
    public static A300TypedResult AllAltimeters(double value) => Altimeter(value, new[] { 1, 2, 3 }, "Altimeters");
}
