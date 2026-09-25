namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The Sovereign+'s speeds: the vendor's V-speed table (its G3000 panel.xml speed bugs at the
/// reference weight) scaled by the square root of the weight ratio, its published limits, and
/// the flight model's stall speeds. Every scaled figure says it is an estimate.
/// </summary>
public static class C680Speeds
{
    public const double RefTakeoffLb = 30775, RefLandingLb = 27575;
    public const int V1 = 110, Vr = 113, V2 = 121, Vfto = 180, Vapp = 117, Vref = 110;
    /// <summary>The panel's Configuration speed group (below 18000 feet): flaps 1 / gear / flaps 2 / flaps full.</summary>
    public const int VfeFlaps1 = 250, Vlo = 210, VfeFlaps2 = 200, VfeFull = 175;
    /// <summary>The standby instrument's published limits.</summary>
    public const int Vmo = 305;
    public const double Mmo = 0.80;
    /// <summary>flight_model.cfg [REFERENCE SPEEDS], knots true at max gross 30775.</summary>
    public const double StallCleanRef = 89, StallFullFlapRef = 76, StallRefLb = 30775;

    private static int Scale(double refSpeed, double gross, double refWeight) => (int)Math.Round(refSpeed * Math.Sqrt(gross / refWeight));

    public static string ComposeTakeoff(double? grossLb)
    {
        if (grossLb is null or <= 0) return $"V1 {V1}, rotate {Vr}, V2 {V2}, flaps up {Vfto} knots at reference weight";
        double g = grossLb.Value;
        if (Math.Abs(g - RefTakeoffLb) < 1) return $"V1 {V1}, rotate {Vr}, V2 {V2}, flaps up {Vfto} knots at {g:0} pounds";
        return $"V1 {Scale(V1, g, RefTakeoffLb)}, rotate {Scale(Vr, g, RefTakeoffLb)}, V2 {Scale(V2, g, RefTakeoffLb)}, flaps up {Vfto} knots, estimated at {g:0} pounds";
    }

    public static string ComposeLimits() => $"Vmo {Vmo} knots, Mmo {Mmo:0.00}";

    public static string ComposeFlapLimits() => $"Flaps 1: {VfeFlaps1} knots. Flaps 2: {VfeFlaps2}. Flaps full: {VfeFull}. Gear operate and extended: {Vlo}";

    public static string ComposeLanding(double? grossLb)
        => grossLb is null or <= 0
            ? $"Vref {Vref}, Vapp {Vapp} knots at reference weight"
            : $"Vref {Scale(Vref, grossLb.Value, RefLandingLb)}, Vapp {Scale(Vapp, grossLb.Value, RefLandingLb)} knots, estimated at {grossLb:0} pounds";

    public static string ComposeStall(double? grossLb)
        => grossLb is null or <= 0
            ? $"Stall clean {StallCleanRef:0}, full flap {StallFullFlapRef:0} knots at {StallRefLb:0} pounds, from the flight model"
            : $"Stall clean {Scale(StallCleanRef, grossLb.Value, StallRefLb)}, full flap {Scale(StallFullFlapRef, grossLb.Value, StallRefLb)} knots, estimated at {grossLb:0} pounds";

    public static string ComposeVfto() => $"Flaps up {Vfto} knots; gear operate and extended {Vlo}";
}

/// <summary>The Sovereign+'s weight limits (basic empty 17831, max ramp 31025, max takeoff 30775, max landing 27575, max zero fuel 21000 pounds).</summary>
public static class C680Weights
{
    public const double Bew = 17831, MaxRamp = 31025, Mtow = 30775, Mlw = 27575, Mzfw = 21000;

    public static string Describe(double gross)
        => gross > MaxRamp ? $"{gross - MaxRamp:0} pounds over max ramp {MaxRamp:0}"
         : gross > Mtow ? $"{gross - Mtow:0} pounds over max takeoff {Mtow:0}"
         : $"{Mtow - gross:0} pounds under max takeoff {Mtow:0}; max landing {Mlw:0}, max zero fuel {Mzfw:0}";
}
