using System.Globalization;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The 757's flap maneuver speeds and VREF (plan 06; Shift+1 to Shift+6). VREF 30 and 25 come from twelve
/// points measured on BlueBird's own APPROACH REF page (140,000 to 250,000 lb), linear between them; a
/// weight outside them is unavailable, never extrapolated. The maneuver speeds are the owner's schedule
/// over VREF 30 (Q5): flaps up + 80, 1 + 60, 5 + 40, 15 + 20, 20 + 20. Pure.
/// <para>Since plan 27 the keys speak the FMC's own VREF 30 and VREF 25 instead while the ADI reader has them
/// (<see cref="DescribeFromFmc"/>, the owner's word, 2026-11-16): the measured table is what is left when it has not
/// (the reader not running, its part missing, PERF INIT not done, a weight outside the FMC's table).</para>
/// </summary>
public static class Bbs757FlapSpeeds
{
    private const double MinLbs = 140_000, MaxLbs = 250_000;

    private static readonly MeasuredVrefTable Vref30Table = new(MeasuredVrefTable.Beyond.HoldEndValue,
        (140, 109), (150, 113), (160, 117), (170, 121), (180, 125), (190, 129),
        (200, 133), (210, 137), (220, 140), (230, 144), (240, 148), (250, 152));

    private static readonly MeasuredVrefTable Vref25Table = new(MeasuredVrefTable.Beyond.HoldEndValue,
        (140, 111), (150, 115), (160, 119), (170, 123), (180, 127), (190, 131),
        (200, 135), (210, 139), (220, 142), (230, 146), (240, 150), (250, 154));

    /// <summary>The schedule, Shift+1 to Shift+5: (flap label, knots over VREF 30).</summary>
    private static readonly (string Flap, int Add)[] Schedule = { ("up", 80), ("1", 60), ("5", 40), ("15", 20), ("20", 20) };

    /// <summary>VREF 30 in knots, or null for a weight outside the measured points (or not read).</summary>
    public static int? Vref30(double? grossWeightLbs) => InTable(grossWeightLbs) is double lbs ? Vref30Table.Knots(lbs) : null;

    /// <summary>VREF 25 in knots, or null for a weight outside the measured points (or not read).</summary>
    public static int? Vref25(double? grossWeightLbs) => InTable(grossWeightLbs) is double lbs ? Vref25Table.Knots(lbs) : null;

    /// <summary>
    /// The readout for <paramref name="scheduleIndex"/>: 0 to 4 the maneuver speeds ("Flaps 5 maneuver speed
    /// 164 knots, calculated"), 5 "VREF 30 124 knots, VREF 25 126 knots, calculated";
    /// <see cref="FlapManeuverSpeeds.Unavailable"/> outside the table.
    /// </summary>
    public static string Describe(double? grossWeightLbs, int scheduleIndex)
    {
        if (Vref30(grossWeightLbs) is not int vref30 || Vref25(grossWeightLbs) is not int vref25)
            return FlapManeuverSpeeds.Unavailable;
        if (scheduleIndex == 5)
            return string.Create(CultureInfo.InvariantCulture, $"VREF 30 {vref30} knots, VREF 25 {vref25} knots, calculated");
        var (flap, add) = Schedule[scheduleIndex];
        return FlapManeuverSpeeds.Sentence(flap, vref30 + add);
    }

    /// <summary>
    /// The readout for <paramref name="scheduleIndex"/> from the FMC's own VREF 30 and VREF 25 (plan 27: the numbers
    /// APPROACH REF prints, read from the aircraft's memory by the ADI reader), in <see cref="Describe"/>'s sentences
    /// without ", calculated": 0 to 4 the maneuver speeds by the same schedule, which is BlueBird's own ("Flaps 5 maneuver
    /// speed 161 knots"), 5 "VREF 30 121 knots, VREF 25 123 knots". Null when either is not read or not above 0 (the FMC
    /// holds 0 until PERF INIT has a weight, and for a weight outside its table): the caller then keeps the calculated
    /// readout. Each speed is spoken as the nearest knot.
    /// </summary>
    public static string? DescribeFromFmc(double? vref30, double? vref25, int scheduleIndex)
    {
        if (FmcKnots(vref30) is not int v30 || FmcKnots(vref25) is not int v25) return null;
        if (scheduleIndex == 5)
            return string.Create(CultureInfo.InvariantCulture, $"VREF 30 {v30} knots, VREF 25 {v25} knots");
        var (flap, add) = Schedule[scheduleIndex];
        return string.Create(CultureInfo.InvariantCulture, $"{FlapManeuverSpeeds.FlapName(flap)} maneuver speed {v30 + add} knots");
    }

    /// <summary>An FMC speed as whole knots, or null when it is not read, not a number or not above 0.</summary>
    private static int? FmcKnots(double? knots)
    {
        if (knots is not double v || !double.IsFinite(v) || v <= 0 || v > int.MaxValue / 2) return null;
        int whole = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return whole > 0 ? whole : null;
    }

    private static double? InTable(double? lbs)
        => lbs is double v && double.IsFinite(v) && v >= MinLbs && v <= MaxLbs ? v : null;
}
