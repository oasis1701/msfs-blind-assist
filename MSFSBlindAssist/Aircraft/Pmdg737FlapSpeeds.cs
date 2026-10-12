using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// CALCULATED PMDG 737 NG3 flap maneuvering speeds (the PFD speed-tape flap bugs).
///
/// Why calculated: the NG3 SDK carries only FMC_LandingVREF (the selected landing
/// flap's VREF, 0 until set), no L:var holds the bugs, and the VREF tables are
/// compiled into 737NG3.wasm (CAeroLib::getVREF). So MSFSBA computes them the way
/// the FMC does:
///
///   maneuver speed = VREF40(gross weight) + FCTM additive
///
/// with the same additive schedule as the 737 MAX (<see cref="IFly737FlapSpeeds"/>):
/// flaps UP +70, 1 +50, 5 +30, 10 +30, 15 +20, 25 +10.
///
/// VREF40 comes from tables MEASURED on each variant's own FMC: the APPROACH REF
/// page accepts a typed gross weight (1L) and prints VREF15/30/40 for it. Linear
/// interpolation between the measured points matches the FMC to the knot at every
/// measured point, where the iFly's single-anchor square-root law was 1 kt off at
/// both ends of the -700's range; between points it is an estimate. Outside a table
/// the square-root law extends it from the end point. The -600 borrows the -700's
/// table and the -900 the -800's (nearest airframe, same wing) until someone
/// measures them.
///
/// In-sim check, -700, KMCO-KTPA 2026-10-11: VREF40 + 70 / + 30 matched the PFD's
/// UP and flaps 5 bugs within the AI display read's own scatter.
/// </summary>
public static class Pmdg737FlapSpeeds
{
    /// <summary>Sanity range in pounds — outside it the weight source is not
    /// believable (sim not ready, cache cold) and readouts say unavailable.</summary>
    public const double MinWeightLbs = 70000.0;
    public const double MaxWeightLbs = 200000.0;

    /// <summary>(flap label, FCTM additive over VREF40) in retraction order —
    /// index 0..5 maps to the output-mode Shift+1..Shift+6 hotkeys.</summary>
    public static readonly (string Flap, int Additive)[] Schedule =
    {
        ("up", 70), ("1", 50), ("5", 30), ("10", 30), ("15", 20), ("25", 10),
    };

    /// <summary>VREF40 by gross weight, in thousands of pounds as the APPROACH
    /// REF page takes it. Measured 2026-10-11 (737-700 PAX BW SC); 127.0 is the
    /// page's own in-flight gross weight, the rest typed in at the gate.</summary>
    private static readonly MeasuredVrefTable Table700 = new(MeasuredVrefTable.Beyond.SquareRootLaw,
        (100.0, 114), (115.0, 123), (127.0, 130), (130.0, 132), (145.0, 140));

    /// <summary>The -800's, all typed in at the gate 2026-10-11 (737-800). It runs
    /// 1 kt above the -700 when light and level with it from 130 klb up.</summary>
    private static readonly MeasuredVrefTable Table800 = new(MeasuredVrefTable.Beyond.SquareRootLaw,
        (100.0, 115), (115.0, 124), (130.0, 132), (145.0, 140), (160.0, 146));

    private static readonly Regex VariantPattern = new(@"737-([6789])00", RegexOptions.CultureInvariant);

    public static bool IsPlausibleWeight(double grossWeightLbs) =>
        grossWeightLbs >= MinWeightLbs && grossWeightLbs <= MaxWeightLbs;

    /// <summary>The variant digit (6..9) for the NG3 CDA's AircraftModel field, per the SDK
    /// header (PMDG_NG3_SDK.h): 1 -600; 2-4 -700, 13-14 -700 BDSF, 19-20 -700 BBJ; 5-7 -800,
    /// 15-16 -800 BDSF, 17-18 -800 BCF, 21-22 -800 BBJ (the header numbers the last one 16, a
    /// typo); 8-12 -900 and -900ER. Null for 0 (no CDA snapshot yet) or a value it lists none for.</summary>
    public static int? VariantFromModel(int aircraftModel) => aircraftModel switch
    {
        1 => 6,
        2 or 3 or 4 or 13 or 14 or 19 or 20 => 7,
        5 or 6 or 7 or 15 or 16 or 17 or 18 or 21 or 22 => 8,
        >= 8 and <= 12 => 9,
        _ => null,
    };

    /// <summary>The variant digit from the aircraft TITLE ("737-700 PAX BW SC" -> 7,
    /// "737-800BCF SSW" -> 8), or null when the title names none. Only a fallback
    /// for <see cref="VariantFromModel"/>: livery titles are freeform. Never the ICAO
    /// type instead: PMDG's -700 fuselage sets atc_model to B738, so
    /// CurrentAircraftIcaoType reads B738 on the -700 (debug log, 2026-10-11).</summary>
    public static int? VariantFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var m = VariantPattern.Match(title);
        return m.Success ? m.Groups[1].Value[0] - '0' : null;
    }

    /// <summary>The variant from the CDA AircraftModel, else from the TITLE, else null.</summary>
    public static int? Variant(int aircraftModel, string? title) =>
        VariantFromModel(aircraftModel) ?? VariantFromTitle(title);

    /// <summary>The measured table a variant uses: the -600 and -700 the -700's,
    /// the -800, the -900 and an unknown variant the -800's (the one most flown).</summary>
    private static MeasuredVrefTable TableFor(int? variant) =>
        variant is 6 or 7 ? Table700 : Table800;

    public static int Vref40Knots(double grossWeightLbs, int? variant) =>
        TableFor(variant).Knots(grossWeightLbs);

    public static int ManeuverSpeedKnots(double grossWeightLbs, int? variant, int scheduleIndex) =>
        Vref40Knots(grossWeightLbs, variant) + Schedule[scheduleIndex].Additive;

    /// <summary>Spoken name for a schedule entry ("Flaps up", "Flaps 1", ...).</summary>
    public static string FlapName(int scheduleIndex) => FlapManeuverSpeeds.FlapName(Schedule[scheduleIndex].Flap);

    /// <summary>The Shift+1..6 readout for a delivered gross weight (null: none delivered),
    /// or <see cref="FlapManeuverSpeeds.Unavailable"/> when it is not believable.</summary>
    public static string Describe(double? grossWeightLbs, int? variant, int scheduleIndex) =>
        grossWeightLbs is { } lbs && IsPlausibleWeight(lbs)
            ? FlapManeuverSpeeds.Sentence(Schedule[scheduleIndex].Flap, ManeuverSpeedKnots(lbs, variant, scheduleIndex))
            : FlapManeuverSpeeds.Unavailable;
}
