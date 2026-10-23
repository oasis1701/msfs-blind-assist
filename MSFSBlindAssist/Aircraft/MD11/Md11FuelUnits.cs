namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The unit the five overhead tank read-outs (<c>MD11_OVHD_TANK_*_VAL</c>) are in, and the
/// conversion the fuel hotkeys need.
///
/// The tank vars follow the EFB's weight-unit option: kilograms with Metric selected, pounds with
/// Imperial. TFDi document them only as "the value of the overhead tank readout", so the app read
/// every one as pounds and a metric pilot heard their kilograms called pounds (issue #327).
///
/// The option lives in <c>L:MD11_EFB_OPTIONS_GENERAL</c>, UNDOCUMENTED: a bitmask the EFB writes
/// when its General options are saved and reads back on load (efb.js, <c>saveGeneralOptions</c> /
/// <c>getPlaneConfig</c>). Bit 0 is the weight unit, set for pounds; the other bits are
/// Fahrenheit, severe failures, random failures and pause at top of descent. MEASURED LIVE
/// (2026-10-23, GE N821TD): Metric read 4 with the tanks at 11,900 each against the stock
/// <c>FUEL TOTAL QUANTITY WEIGHT</c> of 78,729 lb (35,711 kg); Imperial read 5 with the tanks at
/// 26,200 each against 78,639 lb.
///
/// An unreadable option (never delivered, or not a small whole bitmask) is NOT taken for either
/// unit: the read-outs say the unit is unknown rather than guess.
/// </summary>
public static class Md11FuelUnits
{
    /// <summary>The EFB's general-options bitmask: cache key and L:var name.</summary>
    public const string OptionsKey = "MD11_EFB_OPTIONS_GENERAL";

    /// <summary>Bit 0 of <see cref="OptionsKey"/>: set when the EFB's weight unit is Imperial.</summary>
    private const int PoundsBit = 1 << 0;

    /// <summary>
    /// Exclusive bound of a usable bitmask: the float-exact integers. The EFB defines five bits
    /// today; a bit TFDi add later must not cost the weight unit, which is bit 0 whatever else is set.
    /// </summary>
    private const int OptionsLimit = 1 << 24;

    public const double KilogramsPerPound = 0.45359237;

    /// <summary>
    /// True when the tanks read pounds, false when they read kilograms, null when the option has
    /// not been delivered or is not a value the EFB writes.
    /// </summary>
    public static bool? TanksInPounds(double? options)
    {
        if (options is not { } o || double.IsNaN(o) || double.IsInfinity(o)) return null;
        if (o < 0 || o >= OptionsLimit || Math.Abs(o - Math.Round(o)) > 1e-6) return null;
        return ((int)Math.Round(o) & PoundsBit) != 0;
    }

    /// <summary>The five tanks the fuel keys read, in the order they are spoken.</summary>
    public static readonly (string Key, string Name)[] Tanks =
    {
        ("MD11_OVHD_TANK_1_VAL", "Tank 1"),
        ("MD11_OVHD_TANK_2_VAL", "Tank 2"),
        ("MD11_OVHD_TANK_3_VAL", "Tank 3"),
        ("MD11_OVHD_TANK_AUX_VAL", "Auxiliary"),
        ("MD11_OVHD_TANK_TAIL_VAL", "Tail"),
    };

    public const string Unavailable = "Fuel quantity unavailable";
    public const string UnitUnknown = "Fuel quantity unavailable, the aircraft has not reported its weight unit";

    /// <summary>
    /// The fuel keys' sentence: each tank read, then the total with its unit, all converted from
    /// the unit the tanks read in to the one asked for. A tank never delivered is left out; with
    /// none delivered, or the unit unknown, the sentence says so and reads no number.
    /// </summary>
    public static string Describe(IReadOnlyList<(string Name, double? Value)> tanks, double? options, bool kilograms)
    {
        if (!tanks.Any(t => t.Value != null)) return Unavailable;
        if (TanksInPounds(options) is not { } tanksInPounds) return UnitUnknown;

        var parts = new List<string>(tanks.Count + 1);
        double total = 0;
        foreach (var (name, value) in tanks)
        {
            if (value is not { } v) continue;
            total += v;
            // An empty aux/tail tank is normal on this aircraft, not a fault — say zero rather than
            // omitting it, so the pilot can tell "empty" from "not reported".
            parts.Add($"{name} {Whole(Convert(v, tanksInPounds, kilograms))}");
        }
        parts.Add($"Total {Whole(Convert(total, tanksInPounds, kilograms))} {UnitWord(kilograms)}");
        return string.Join(", ", parts);
    }

    /// <summary>A tank reading in the unit it was read in, converted to pounds or kilograms.</summary>
    private static double Convert(double value, bool tanksInPounds, bool wantKilograms)
    {
        if (tanksInPounds == !wantKilograms) return value;
        return wantKilograms ? value * KilogramsPerPound : value / KilogramsPerPound;
    }

    private static string UnitWord(bool kilograms) => kilograms ? "kilograms" : "pounds";

    private static string Whole(double v) => Md11StatusRow.Whole(v);

    /// <summary>
    /// A Status Display tank row: the number the overhead shows, with the unit it shows it in.
    /// With the unit unknown, just the number (the minimums rows' shape before their mode is read).
    /// </summary>
    public static string DescribeTank(double value, bool? tanksInPounds)
    {
        string text = Whole(value);
        return tanksInPounds is { } pounds ? $"{text} {UnitWord(!pounds)}" : text;
    }
}
