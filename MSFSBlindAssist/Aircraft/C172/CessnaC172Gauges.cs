using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The C172 G1000's engine, fuel and electrical readings as its own engine page draws them, and
/// the words each is spoken in (C172-18). Every colour zone is the aircraft's panel.xml, read live
/// from the PFD's <c>xmlConfig</c> on 2026-10-18 (docs/c172.md), never the DA40's table: these gauges
/// have stretches with no colour at all between their arcs (oil pressure is red to 20 psi, nothing
/// to 50, green to 90), which a table of contiguous bands would call yellow.
///
/// THE BAND IS PART OF THE READING. A sighted pilot sees the needle sitting in the green, so a
/// reading carries its zone: "66 psi, green". The words are the DA40's ("green", "red, below
/// minimum", "yellow, high"), so both G1000 aircraft sound alike. A reading in an uncoloured stretch
/// says "below the green arc" or "above the green arc" only when a red or yellow zone lies further
/// out on that side, and nothing otherwise: idle RPM, cold oil, a full tank or a high fuel flow are
/// numbers, not cautions.
///
/// Pure and culture-proof (every number written invariant), so the suite can pin it.
/// </summary>
internal static class CessnaC172Gauges
{
    internal enum Colour { Green, Yellow, Red }

    /// <summary>One coloured arc, inclusive at both ends, as panel.xml's Begin and End.</summary>
    internal readonly record struct Zone(double From, double To, Colour Colour);

    // The engine page's gauges (panel.xml EngineDisplay > EnginePage). EGT and CHT have no zones.
    internal static readonly Zone[] OilPressure =
        { new(0, 20, Colour.Red), new(50, 90, Colour.Green), new(115, 120, Colour.Red) };
    internal static readonly Zone[] OilTemperature =
        { new(100, 245, Colour.Green), new(245, 250, Colour.Red) };
    internal static readonly Zone[] FuelFlow = { new(0, 12, Colour.Green) };
    internal static readonly Zone[] Vacuum = { new(4.5, 5.5, Colour.Green) };
    internal static readonly Zone[] FuelQuantity =
        { new(0, 1.5, Colour.Red), new(1.5, 5, Colour.Yellow), new(5, 24, Colour.Green) };

    /// <summary>
    /// The tachometer's green arc tops out by altitude, through a state machine with 600 ft of
    /// hysteresis: 2500 rpm low, 2600 above 5300 ft (back below 4700), 2700 above 10300 ft (back
    /// below 9700). The red arc starts at 2700 whatever the altitude.
    /// </summary>
    internal enum RpmArc { LowAlt, MedAlt, HighAlt }

    internal static double RpmGreenTop(RpmArc arc) => arc switch
    {
        RpmArc.LowAlt => 2500,
        RpmArc.MedAlt => 2600,
        _ => 2700
    };

    internal static Zone[] Rpm(RpmArc arc) =>
        new[] { new Zone(2100, RpmGreenTop(arc), Colour.Green), new Zone(2700, 3000, Colour.Red) };

    /// <summary>
    /// The arc state after an indicated altitude, its transitions applied until none fires (the
    /// gauge starts LowAlt and steps once per evaluation, so a load at altitude settles the same way).
    /// </summary>
    internal static RpmArc NextRpmArc(RpmArc arc, double altitudeFeet)
    {
        while (true)
        {
            RpmArc next = arc switch
            {
                RpmArc.LowAlt when altitudeFeet > 5300 => RpmArc.MedAlt,
                RpmArc.MedAlt when altitudeFeet < 4700 => RpmArc.LowAlt,
                RpmArc.MedAlt when altitudeFeet > 10300 => RpmArc.HighAlt,
                RpmArc.HighAlt when altitudeFeet < 9700 => RpmArc.MedAlt,
                _ => arc
            };
            if (next == arc) return arc;
            arc = next;
        }
    }

    /// <summary>
    /// The words for where a value sits on a gauge, or "" for an uncoloured stretch with no arc
    /// beyond it. Green is checked first and holds both its ends (oil temperature 245 is green,
    /// as the red blink starts above it); then the red or yellow zone holding the value, the last
    /// listed winning on a shared end (fuel 1.5 gallons is yellow, the zone the gauge draws from
    /// there up). Oil pressure 20 is red, as the CAS's OIL PRESSURE is at or below 20.
    /// </summary>
    internal static string Band(double value, IReadOnlyList<Zone> zones)
    {
        Zone? green = null;
        foreach (var z in zones)
            if (z.Colour == Colour.Green) green = z;

        if (green is { } g && value >= g.From && value <= g.To) return "green";

        Zone? holding = null;
        foreach (var z in zones)
            if (z.Colour != Colour.Green && value >= z.From && value <= z.To) holding = z;

        bool below = green is { } gb && value < gb.From;
        if (holding is { } h)
        {
            string side = green is null ? "" : below ? (h.Colour == Colour.Red ? ", below minimum" : ", low")
                                                     : (h.Colour == Colour.Red ? ", above maximum" : ", high");
            return (h.Colour == Colour.Red ? "red" : "yellow") + side;
        }

        if (green is not { } gr) return "";
        bool arcBeyond = false;
        foreach (var z in zones)
            if (z.Colour != Colour.Green && (below ? z.To <= gr.From : z.From >= gr.To)) arcBeyond = true;
        if (!arcBeyond) return "";
        return below ? "below the green arc" : "above the green arc";
    }

    private static string Reading(string number, string unit, string band) =>
        band.Length == 0 ? $"{number} {unit}" : $"{number} {unit}, {band}";

    private static string N(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    internal static string DescribeRpm(double rpm, RpmArc arc) => Reading(N(rpm, "0"), "rpm", Band(rpm, Rpm(arc)));

    internal static string DescribeOilPressure(double psi) => Reading(N(psi, "0"), "psi", Band(psi, OilPressure));

    internal static string DescribeOilTemperature(double fahrenheit) =>
        Reading(N(fahrenheit, "0"), "degrees fahrenheit", Band(fahrenheit, OilTemperature));

    /// <summary>EGT and CHT: the engine page draws no zones for either.</summary>
    internal static string DescribeTemperature(double fahrenheit) => Reading(N(fahrenheit, "0"), "degrees fahrenheit", "");

    internal static string DescribeFuelFlow(double gph) => Reading(N(gph, "0.0"), "gallons per hour", Band(gph, FuelFlow));

    internal static string DescribeVacuum(double inHg) => Reading(N(inHg, "0.0"), "inches", Band(inHg, Vacuum));

    internal static string DescribeFuelQuantity(double gallons) =>
        Reading(N(gallons, "0.0"), "gallons", Band(gallons, FuelQuantity));

    internal static string DescribeVolts(double volts) => Reading(N(volts, "0.0"), "volts", "");

    internal static string DescribeHours(double hours) => Reading(N(hours, "0.0"), "hours", "");

    /// <summary>
    /// Output+L: the flap handle's notch, `FLAPS HANDLE INDEX:1`, measured against the flap angle
    /// (2026-10-18): 0 Up, 1 10 degrees, 2 20 degrees, 3 Full (30 degrees). Worded as the Flaps combo is.
    /// </summary>
    internal static string DescribeFlaps(double index) => (int)Math.Round(index) switch
    {
        0 => "Flaps Up",
        1 => "Flaps 10 degrees",
        2 => "Flaps 20 degrees",
        3 => "Flaps Full",
        int other => $"Flaps {other.ToString(CultureInfo.InvariantCulture)}"
    };

    /// <summary>
    /// Output+B, as the DA40 says it: inches first, the unit the C172's G1000 is set to ("Display
    /// units: ... pressure inches", read off the PFD), then hectopascals. Never "standard": 29.92
    /// typed as a local setting and the G1000's STD are the same number to the sim.
    /// </summary>
    internal static string DescribeAltimeter(double millibars) =>
        $"Altimeter {N(millibars / 33.8639, "0.00")} inches, {N(millibars, "0")} hectopascals";

    /// <summary>
    /// Output+Shift+W, as the DA40 says it, in kilograms and the pounds this aircraft's G1000 and
    /// POH work in, with the sim's maximum (`MAX GROSS WEIGHT`, 2558 pounds measured) when it is read.
    /// </summary>
    internal static string DescribeGrossWeight(double pounds, double? maxPounds)
    {
        string text = $"Gross weight {N(pounds * 0.45359237, "0")} kilograms, {N(pounds, "0")} pounds";
        if (maxPounds is double max)
            text += $". Maximum {N(max * 0.45359237, "0")} kilograms, {N(max, "0")} pounds";
        return text;
    }

    /// <summary>Output+F: each tank and the total.</summary>
    internal static string DescribeFuelQuantities(double left, double right) =>
        $"Left {N(left, "0.0")}, Right {N(right, "0.0")}, Total {N(left + right, "0.0")} gallons";

    /// <summary>
    /// Output+Shift+F: the tanks, then the flow and the endurance it gives when the engine burns
    /// anything (above 0.1 gallons per hour).
    /// </summary>
    internal static string DescribeFuelInfo(double left, double right, double? flow)
    {
        string text = DescribeFuelQuantities(left, right);
        if (flow is not double f) return text;
        text += $", Flow {N(f, "0.0")} gallons per hour";
        if (f > 0.1) text += $", Endurance {N((left + right) / f, "0.0")} hours";
        return text;
    }
}
