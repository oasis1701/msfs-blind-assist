namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// CALCULATED PMDG 777 flap maneuvering speeds (the PFD speed-tape flap bugs),
/// the 777 counterpart of <see cref="Pmdg737FlapSpeeds"/>.
///
/// Why calculated: as on the NG3, the 777 SDK carries no flap bug speeds and no
/// L:var holds them, so MSFSBA computes them the way the FMC does:
///
///   maneuver speed = VREF30(gross weight) + FCTM additive
///
/// with the 777 schedule: flaps UP +80, 1 +60, 5 +40, 15 +20, 20 +20.
///
/// VREF30 comes from tables MEASURED on each variant's own FMC: the APPROACH REF
/// page accepts a typed gross weight (1L) and prints VREF20/25/30 for it
/// (2026-10-12, all four variants, FMC set to pounds). Three tables cover them:
/// the 777F and the -200LR read identically at every weight checked, and the
/// -200ER's GE90 and Rolls-Royce versions read identically, so the engine never
/// matters. Linear interpolation between the measured points; outside a table the
/// end value holds. The light end is a real floor on the 777F/-200LR (134) and the
/// -200ER (127), and no 777 is flown below the -300ER's first point or above MTOW.
///
/// Never extrapolate a table by the square-root law: it holds below the maximum
/// landing weight, but the 777F/-200LR curve climbs 4-5 kt above it from 650 klb
/// and the -300ER's from 700 klb, as VREF30 closes on VREF25.
/// </summary>
public static class Pmdg777FlapSpeeds
{
    /// <summary>Sanity range in pounds — outside it the weight source is not
    /// believable (sim not ready, cache cold) and readouts say unavailable.</summary>
    public const double MinWeightLbs = 250000.0;
    public const double MaxWeightLbs = 800000.0;

    /// <summary>(flap label, FCTM additive over VREF30) in retraction order —
    /// index 0..4 maps to the output-mode Shift+1..Shift+5 hotkeys.</summary>
    public static readonly (string Flap, int Additive)[] Schedule =
    {
        ("up", 80), ("1", 60), ("5", 40), ("15", 20), ("20", 20),
    };

    /// <summary>777F and -200LR VREF30 by gross weight in thousands of pounds,
    /// as the APPROACH REF page takes it. Flat at 134 below about 450 klb.</summary>
    private static readonly MeasuredVrefTable TableFreighterAndLr = new(MeasuredVrefTable.Beyond.HoldEndValue,
        (350.0, 134), (400.0, 134), (450.0, 135), (500.0, 139), (575.0, 149),
        (650.0, 162), (710.0, 174), (766.0, 181));

    /// <summary>-300ER. No floor down to 400 klb.</summary>
    private static readonly MeasuredVrefTable Table300Er = new(MeasuredVrefTable.Beyond.HoldEndValue,
        (400.0, 127), (450.0, 135), (500.0, 142), (554.0, 150), (625.0, 159),
        (700.0, 173), (775.0, 183));

    /// <summary>-200ER (GE90; the Rolls-Royce read identically at 470 and 630).
    /// Flat at about 127 at light weights, no heavy-end bend before MTOW.</summary>
    private static readonly MeasuredVrefTable Table200Er = new(MeasuredVrefTable.Beyond.HoldEndValue,
        (325.0, 127), (375.0, 128), (425.0, 132), (470.0, 140), (525.0, 147),
        (600.0, 157), (630.0, 160), (656.0, 163));

    public static bool IsPlausibleWeight(double grossWeightLbs) =>
        grossWeightLbs >= MinWeightLbs && grossWeightLbs <= MaxWeightLbs;

    /// <summary>The measured table for a CDA AircraftModel (1 -200, 2 -200ER,
    /// 3 -300, 4 -200LR, 5 777F, 6 -300ER), or null when the model is unknown
    /// (0 before the first CDA snapshot). PMDG ships no -200 or -300, so those
    /// borrow the -200ER's and -300ER's tables.</summary>
    private static MeasuredVrefTable? TableFor(int aircraftModel) => aircraftModel switch
    {
        1 or 2 => Table200Er,
        3 or 6 => Table300Er,
        4 or 5 => TableFreighterAndLr,
        _ => null,
    };

    public static bool IsKnownModel(int aircraftModel) => TableFor(aircraftModel) != null;

    /// <summary>VREF30 in knots, or null for an unknown model.</summary>
    public static int? Vref30Knots(double grossWeightLbs, int aircraftModel) =>
        TableFor(aircraftModel)?.Knots(grossWeightLbs);

    public static int? ManeuverSpeedKnots(double grossWeightLbs, int aircraftModel, int scheduleIndex) =>
        Vref30Knots(grossWeightLbs, aircraftModel) is { } vref30
            ? vref30 + Schedule[scheduleIndex].Additive
            : null;

    /// <summary>Spoken name for a schedule entry ("Flaps up", "Flaps 1", ...).</summary>
    public static string FlapName(int scheduleIndex) => FlapManeuverSpeeds.FlapName(Schedule[scheduleIndex].Flap);

    /// <summary>The Shift+1..5 readout for a delivered gross weight (null: none delivered),
    /// or <see cref="FlapManeuverSpeeds.Unavailable"/> when the weight is not believable or
    /// the model is unknown.</summary>
    public static string Describe(double? grossWeightLbs, int aircraftModel, int scheduleIndex) =>
        grossWeightLbs is { } lbs && IsPlausibleWeight(lbs)
            && ManeuverSpeedKnots(lbs, aircraftModel, scheduleIndex) is { } knots
            ? FlapManeuverSpeeds.Sentence(Schedule[scheduleIndex].Flap, knots)
            : FlapManeuverSpeeds.Unavailable;
}
