using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The C172's speeds: the take-off roll's single "Rotate" call (<c>TakeoffRollCallouts</c>) and
/// output Shift+1 to 6, whose Airbus speeds (green dot, S, F, VLS) mean nothing on a Cessna.
///
/// THE AIRCRAFT'S OWN NUMBERS FIRST. Every speed the flight model defines was read live from the
/// G1000 PFD's view on 2026-10-18 (<c>Simplane.getDesignSpeeds()</c>, the flight model's reference
/// speeds): VS0 40, VS1 48, VFE 85, VNO 129, VNE 163, Vr 55, Vx 62, Vy 74, best glide 68 knots.
/// Each matches the Cessna 172S Pilot's Operating Handbook. The flight model sets no approach
/// speed (Vapp 0), manoeuvring speed or 10-degree flap limit, so those few are the POH's, quoted
/// as the POH quotes them: approach ranges stay ranges.
///
/// Pure and culture-proof (every number written invariant), so the suite can pin it.
/// </summary>
internal static class CessnaC172Speeds
{
    // The flight model's reference speeds, knots indicated.
    internal const double Vs0 = 40, Vs1 = 48, Vr = 55, Vx = 62, Vy = 74, BestGlide = 68;
    internal const double VfeFull = 85, Vno = 129, Vne = 163;

    // The POH's, for what the flight model leaves out: 10-degree flap limit, approach speeds
    // (normal landing, full flap and flaps up; short-field landing, full flap).
    internal const double VfeTen = 110;
    internal const double ApproachFullFlapLow = 60, ApproachFullFlapHigh = 70;
    internal const double ApproachFlapsUpLow = 65, ApproachFlapsUpHigh = 75;
    internal const double ApproachShortField = 61;

    /// <summary>
    /// The POH's manoeuvring speed by weight (pounds, knots): it falls as the aircraft gets lighter,
    /// so the figure for maximum weight would overstate it on most flights.
    /// </summary>
    private static readonly (double Pounds, double Knots)[] ManoeuvringByWeight =
        { (2550, 105), (2200, 98), (1900, 90) };

    /// <summary>
    /// The manoeuvring speed at a weight: the POH's figure for the heaviest listed weight at or below
    /// it (never interpolated upward, so it never overstates), the lightest figure below 1900 lb.
    /// </summary>
    internal static double ManoeuvringSpeed(double pounds)
    {
        foreach (var (listed, knots) in ManoeuvringByWeight)
            if (pounds >= listed) return knots;
        return ManoeuvringByWeight[^1].Knots;
    }

    /// <summary>Output Shift+1: best glide.</summary>
    internal static string DescribeBestGlide() => $"Best glide {K(BestGlide)} knots.";

    // THE WORDING (the owner's review, 2026-10-18): every number carries its unit, every flap setting
    // is said in words ("with full flaps", "without flaps"), never "clean" or a bare list of numbers.
    // "10 degrees 110, 20 degrees and full 85 knots" was heard as a muddle of degrees and knots.

    /// <summary>
    /// Output Shift+2: the speeds that bound the airspeed tape, slowest first, as the tape reads them.
    /// The manoeuvring speed is for the weight the sim reports, or for maximum weight when it has none.
    /// </summary>
    internal static string DescribeLimits(double? pounds) =>
        $"Stall {K(Vs1)} knots with flaps up, {K(Vs0)} knots with full flaps. " +
        (pounds is double lb
            ? $"Manoeuvring speed {K(ManoeuvringSpeed(lb))} knots at this weight. "
            : $"Manoeuvring speed {K(ManoeuvringByWeight[0].Knots)} knots at maximum weight. ") +
        $"Maximum structural cruise {K(Vno)} knots. Never exceed {K(Vne)} knots.";

    /// <summary>Output Shift+3: both flap limits, by the settings the flap combo names.</summary>
    internal static string DescribeFlapLimits() =>
        $"Flap limits: {K(VfeTen)} knots with 10 degrees, {K(VfeFull)} knots with 20 degrees or full.";

    /// <summary>
    /// Output Shift+4: the landing set, the normal landing first. The POH's flaps-up approach is for a
    /// landing made without flaps, so it is named that way, after the two landings flown with them.
    /// </summary>
    internal static string DescribeLandingSpeeds() =>
        $"Approach {K(ApproachFullFlapLow)} to {K(ApproachFullFlapHigh)} knots with full flaps. " +
        $"Short field approach {K(ApproachShortField)} knots with full flaps. " +
        $"Landing without flaps, {K(ApproachFlapsUpLow)} to {K(ApproachFlapsUpHigh)} knots. " +
        $"Best glide {K(BestGlide)} knots.";

    /// <summary>Output Shift+5: the take-off set.</summary>
    internal static string DescribeTakeoffSpeeds() =>
        $"Rotate {K(Vr)} knots. Best angle of climb {K(Vx)} knots. Best rate of climb {K(Vy)} knots.";

    /// <summary>
    /// Output Shift+6: where the flaps are set (as output L says it) and the limit for that setting
    /// (FLAPS HANDLE INDEX: 0 up, 1 ten degrees, 2 twenty, 3 full); with the flaps up, the limit for
    /// the first setting.
    /// </summary>
    internal static string DescribeFlapLimitNow(double handleIndex) => (int)Math.Round(handleIndex) switch
    {
        <= 0 => $"Flaps up. Limit with 10 degrees of flaps, {K(VfeTen)} knots.",
        1 => $"Flaps 10 degrees. Limit {K(VfeTen)} knots.",
        2 => $"Flaps 20 degrees. Limit {K(VfeFull)} knots.",
        _ => $"Flaps full. Limit {K(VfeFull)} knots."
    };

    private static string K(double knots) => knots.ToString("0", CultureInfo.InvariantCulture);
}
