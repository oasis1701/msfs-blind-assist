using System.Globalization;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// The Learjet 35A's characteristic speeds, on the output-mode keys the Airbus green dot /
/// S / F / VLS / VS / VFE set was built for (Shift+1 to Shift+6), because none of those
/// speeds exists on a Learjet and the keys are what the pilot already knows.
///
/// TWO KINDS OF NUMBER, and the readouts say which they are:
///
/// COMPUTED AT WEIGHT. Flysimware's flight model carries the AFM stall speeds at maximum
/// gross weight — clean 129 kt, full flap 105 kt at 18,300 lb (`[REFERENCE SPEEDS]`, marked
/// "AFM DATA" by the vendor). Stall speed scales with the square root of weight, so the
/// clean and full-flap stall, Vref (1.3 × Vs0) and the takeoff speeds are computed from the
/// live gross weight. Checked against the real 35A Vref chart: this gives 124 kt at
/// 15,000 lb and 111 kt at 12,000 lb, within a knot of where the chart puts them. The intermediate
/// flap settings have no vendor figure, so flaps 8 and flaps 20 stall speeds are ESTIMATED
/// (0.94 × clean and 1.06 × full flap), the takeoff speeds as 1.10 × Vs(flaps 8) for rotate
/// and 1.20 × for V2 — the FAR 25 minima. Those readouts say "estimated".
///
/// PUBLISHED LIMITATIONS. Vmo 300 kt, Mmo 0.81, flap limits 200 kt (8° and 20°) and 150 kt
/// (40°), gear operating 200 kt and extended 260 kt are the Learjet 35A limitations as
/// published; only Mmo (0.81) is also in the flight model. They are not derived from
/// anything on this machine and the readouts say "as published".
/// </summary>
public static class Lj35Speeds
{
    public const double MaxGrossLb = 18300;
    public const double VsCleanAtMaxKt = 129;
    public const double VsFullFlapAtMaxKt = 105;

    public const double VmoKt = 300;
    public const double Mmo = 0.81;
    public const double VfeFlaps8And20Kt = 200;
    public const double VfeFlaps40Kt = 150;
    public const double VloKt = 200;
    public const double VleKt = 260;

    private const double Flaps8StallFactor = 0.94;
    private const double Flaps20StallFactor = 1.06;
    private const double VrFactor = 1.10;
    private const double V2Factor = 1.20;
    private const double VrefFactor = 1.30;

    /// <summary>Square-root-of-weight scaling of a max-gross stall speed; a weight that has not been read uses max gross.</summary>
    public static double AtWeight(double speedAtMaxKt, double? grossLb)
    {
        double w = grossLb is > 1000 ? grossLb.Value : MaxGrossLb;
        return speedAtMaxKt * Math.Sqrt(w / MaxGrossLb);
    }

    public static double StallClean(double? grossLb) => AtWeight(VsCleanAtMaxKt, grossLb);
    public static double StallFullFlap(double? grossLb) => AtWeight(VsFullFlapAtMaxKt, grossLb);
    public static double StallFlaps8(double? grossLb) => StallClean(grossLb) * Flaps8StallFactor;
    public static double StallFlaps20(double? grossLb) => StallFullFlap(grossLb) * Flaps20StallFactor;
    public static double Vref40(double? grossLb) => StallFullFlap(grossLb) * VrefFactor;
    public static double Vref20(double? grossLb) => StallFlaps20(grossLb) * VrefFactor;
    public static double Vr(double? grossLb) => StallFlaps8(grossLb) * VrFactor;
    public static double V2(double? grossLb) => StallFlaps8(grossLb) * V2Factor;

    // ---- the six readouts ----

    /// <summary>Shift+1.</summary>
    public static string ComposeTakeoff(double? grossLb) =>
        $"Takeoff, flaps 8, at {Weight(grossLb)}: rotate {K(Vr(grossLb))}, V2 {K(V2(grossLb))} knots. Estimated from the stall speeds.";

    /// <summary>Shift+2.</summary>
    public static string ComposeBarberPole() =>
        $"Vmo {VmoKt:0} knots, Mmo point {(int)Math.Round(Mmo * 100)}, as published for the 35A.";

    /// <summary>Shift+3.</summary>
    public static string ComposeFlapLimits() =>
        $"Flap limits: 8 and 20 degrees {VfeFlaps8And20Kt:0} knots, 40 degrees {VfeFlaps40Kt:0} knots, as published.";

    /// <summary>Shift+4.</summary>
    public static string ComposeVref(double? grossLb) =>
        $"Vref flaps 40 {K(Vref40(grossLb))} knots at {Weight(grossLb)}; flaps 20 about {K(Vref20(grossLb))}. Computed from the stall speed at weight.";

    /// <summary>Shift+5.</summary>
    public static string ComposeStall(double? grossLb) =>
        $"Stall at {Weight(grossLb)}: clean {K(StallClean(grossLb))}, flaps 40 {K(StallFullFlap(grossLb))} knots. " +
        $"AFM at {MaxGrossLb:0} pounds: {VsCleanAtMaxKt:0} and {VsFullFlapAtMaxKt:0}.";

    /// <summary>Shift+6.</summary>
    public static string ComposeGearLimits() =>
        $"Gear: operate below {VloKt:0} knots, extended limit {VleKt:0} knots, as published.";

    private static string K(double kt) => Math.Round(kt, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static string Weight(double? grossLb) => grossLb is > 1000
        ? (Math.Round(grossLb.Value / 100) * 100).ToString("0", CultureInfo.InvariantCulture) + " pounds"
        : "maximum weight, gross weight not read yet";
}
