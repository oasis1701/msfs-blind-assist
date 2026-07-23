using System;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// CALCULATED 737 MAX 8 flap maneuvering speeds (the PFD speed-tape flap bugs).
///
/// Why calculated: the 2026-07-24 investigation confirmed the iFly exposes no
/// flap maneuver speeds anywhere — not in the SDK shared-memory struct, not in
/// the WASM's exported L:vars (complete list extracted from the binary), and
/// not as text on any CDU page; the bugs exist only as WASM-drawn pixels. So
/// MSFSBA computes them the way the FMC does:
///
///   maneuver speed = VREF40(current gross weight) + FCTM additive
///
/// The additive schedule (multiple concordant public sources for the 737
/// NG/MAX, e.g. the FCTM as quoted across PPRuNe/AVSIM/b737.org.uk): flaps UP
/// +70, flaps 1 +50, flaps 5 +30, flaps 10 +30, flaps 15 +20, flaps 25 +10.
///
/// VREF40(GW) uses the square-root stall-speed law anchored at
/// <see cref="AnchorVref40Kts"/> @ <see cref="AnchorWeightKg"/> — the sqrt law
/// reproduces published 737-800 QRH-style VREF40 tables within about a knot
/// across the whole weight range, and the anchor carries the MAX wing's small
/// credit vs the -800. This is an approximation of the iFly FMC's own table
/// (typically within a few knots of the drawn bugs), which is why every
/// readout says "calculated". If live comparison against the speed tape shows
/// a systematic offset, correct the single anchor constant below.
/// </summary>
public static class IFly737FlapSpeeds
{
    /// <summary>MAX 8 VREF40 anchor: derived from published 737-800 QRH-style
    /// tables (VREF40 ≈ 140 kt at 60 t) minus ~2 kt for the MAX wing.</summary>
    private const double AnchorVref40Kts = 138.0;
    private const double AnchorWeightKg = 60000.0;

    /// <summary>Sanity range — outside this the weight source is not believable
    /// (sim not ready, cache cold) and readouts must say unavailable rather
    /// than announce a garbage speed.</summary>
    public const double MinWeightKg = 35000.0;
    public const double MaxWeightKg = 90000.0;

    /// <summary>(flap label, FCTM additive over VREF40) in retraction order —
    /// index 0..5 maps to the output-mode Shift+1..Shift+6 hotkeys.</summary>
    public static readonly (string Flap, int Additive)[] Schedule =
    {
        ("up", 70), ("1", 50), ("5", 30), ("10", 30), ("15", 20), ("25", 10),
    };

    public static bool IsPlausibleWeight(double grossWeightKg) =>
        grossWeightKg >= MinWeightKg && grossWeightKg <= MaxWeightKg;

    public static int Vref40Knots(double grossWeightKg) =>
        (int)Math.Round(AnchorVref40Kts * Math.Sqrt(grossWeightKg / AnchorWeightKg));

    public static int ManeuverSpeedKnots(double grossWeightKg, int scheduleIndex) =>
        Vref40Knots(grossWeightKg) + Schedule[scheduleIndex].Additive;

    /// <summary>Spoken name for a schedule entry ("Flaps up", "Flaps 1", ...).</summary>
    public static string FlapName(int scheduleIndex) =>
        Schedule[scheduleIndex].Flap == "up" ? "Flaps up" : $"Flaps {Schedule[scheduleIndex].Flap}";

    /// <summary>One panel line, e.g. "Up 214, flaps 1 at 194, flaps 5 at 174,
    /// flaps 10 at 174, flaps 15 at 164, flaps 25 at 154". The "at" separates
    /// the flap number from the speed so a screen reader can never run
    /// "flaps 1 194" together into a single number.</summary>
    public static string ComposePanelText(double grossWeightKg)
    {
        if (!IsPlausibleWeight(grossWeightKg)) return "Unavailable";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Schedule.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(i == 0 ? "Up " : $"flaps {Schedule[i].Flap} at ");
            sb.Append(ManeuverSpeedKnots(grossWeightKg, i));
        }
        return sb.ToString();
    }
}
