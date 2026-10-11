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
/// interpolation between the measured points matches the FMC to the knot, where
/// the iFly's single-anchor square-root law was 1 kt off at both ends of the -700's
/// range. Outside a table the square-root law extends it from the end point. The
/// -600 borrows the -700's table and the -900 the -800's (nearest airframe, same
/// wing) until someone measures them.
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
    private static readonly (double Klb, int Vref40)[] Table700 =
    {
        (100.0, 114), (115.0, 123), (127.0, 130), (130.0, 132), (145.0, 140),
    };

    /// <summary>The -800's, all typed in at the gate 2026-10-11 (737-800). It runs
    /// 1 kt above the -700 when light and level with it from 130 klb up.</summary>
    private static readonly (double Klb, int Vref40)[] Table800 =
    {
        (100.0, 115), (115.0, 124), (130.0, 132), (145.0, 140), (160.0, 146),
    };

    private static readonly Regex VariantPattern = new(@"737-([6789])00", RegexOptions.CultureInvariant);

    public static bool IsPlausibleWeight(double grossWeightLbs) =>
        grossWeightLbs >= MinWeightLbs && grossWeightLbs <= MaxWeightLbs;

    /// <summary>The variant digit from the aircraft TITLE ("737-700 PAX BW SC" -> 7,
    /// "737-800BCF SSW" -> 8), or null when the title names none. Never the ICAO
    /// type instead: PMDG's -700 fuselage sets atc_model to B738, so
    /// CurrentAircraftIcaoType reads B738 on the -700 (debug log, 2026-10-11).</summary>
    public static int? VariantFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var m = VariantPattern.Match(title);
        return m.Success ? m.Groups[1].Value[0] - '0' : null;
    }

    /// <summary>The measured table a variant uses: the -600 and -700 the -700's,
    /// the -800, the -900 and an unrecognised title the -800's (the one most flown).</summary>
    private static (double Klb, int Vref40)[] TableFor(int? variant) =>
        variant is 6 or 7 ? Table700 : Table800;

    public static int Vref40Knots(double grossWeightLbs, string? aircraftTitle) =>
        (int)Math.Round(Interpolate(TableFor(VariantFromTitle(aircraftTitle)), grossWeightLbs / 1000.0),
            MidpointRounding.AwayFromZero);

    public static int ManeuverSpeedKnots(double grossWeightLbs, string? aircraftTitle, int scheduleIndex) =>
        Vref40Knots(grossWeightLbs, aircraftTitle) + Schedule[scheduleIndex].Additive;

    /// <summary>Spoken name for a schedule entry ("Flaps up", "Flaps 1", ...).</summary>
    public static string FlapName(int scheduleIndex) =>
        Schedule[scheduleIndex].Flap == "up" ? "Flaps up" : $"Flaps {Schedule[scheduleIndex].Flap}";

    /// <summary>Linear between measured points; beyond either end, the square-root
    /// law from that end's point.</summary>
    private static double Interpolate((double Klb, int Vref40)[] table, double klb)
    {
        var first = table[0];
        if (klb <= first.Klb) return first.Vref40 * Math.Sqrt(klb / first.Klb);
        for (int i = 1; i < table.Length; i++)
        {
            var (aKlb, aVref) = table[i - 1];
            var (bKlb, bVref) = table[i];
            if (klb <= bKlb) return aVref + (bVref - aVref) * (klb - aKlb) / (bKlb - aKlb);
        }
        var last = table[^1];
        return last.Vref40 * Math.Sqrt(klb / last.Klb);
    }
}
