using System.Globalization;

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
/// unit: the read-outs say the unit is unknown rather than guess. Nor is a readable one taken on
/// trust: 0 is also what the option reads before the EFB writes it (an unwritten L:var reads 0),
/// so the sim's own fuel weight confirms the bit wherever it can (<see cref="TankUnit"/>).
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
    /// The option's bit 0: true for pounds, false for kilograms, null when the option has not been
    /// delivered or is not a value the EFB writes. Read through <see cref="TankUnit"/>, which
    /// confirms it: a flat 0 is also the option before the EFB writes it.
    /// </summary>
    public static bool? TanksInPounds(double? options)
    {
        if (options is not { } o || double.IsNaN(o) || double.IsInfinity(o)) return null;
        if (o < 0 || o >= OptionsLimit || Math.Abs(o - Math.Round(o)) > 1e-6) return null;
        return ((int)Math.Round(o) & PoundsBit) != 0;
    }

    /// <summary>
    /// The five tanks, in the order the fuel keys speak them: the key, the name the keys speak and
    /// the Status Display row's name. The one list of tank keys: the exports and the Fuel Quantity
    /// rows are built from it too. Read-only, so no caller can change what the others read.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Name, string RowName)> Tanks = Array.AsReadOnly(new[]
    {
        ("MD11_OVHD_TANK_1_VAL", "Tank 1", "Tank 1"),
        ("MD11_OVHD_TANK_2_VAL", "Tank 2", "Tank 2"),
        ("MD11_OVHD_TANK_3_VAL", "Tank 3", "Tank 3"),
        ("MD11_OVHD_TANK_AUX_VAL", "Auxiliary", "Auxiliary tank"),
        ("MD11_OVHD_TANK_TAIL_VAL", "Tail", "Tail tank"),
    });

    public const string Unavailable = "Fuel quantity unavailable";
    public const string UnitUnknown = "Fuel quantity unavailable, the aircraft has not reported its weight unit";

    /// <summary>
    /// The sim's own fuel weight, in pounds: cache key. Read only to tell tanks the aircraft has not
    /// published yet from empty ones (<see cref="TanksUnpublished"/>) and to confirm the unit they
    /// read in (<see cref="TankUnit"/>), never spoken.
    /// </summary>
    public const string SimFuelKey = "MD11_SIM_FUEL_TOTAL_POUNDS";
    public const string SimFuelVar = "FUEL TOTAL QUANTITY WEIGHT";

    /// <summary>
    /// The sim fuel weight above which every tank reading 0 cannot be an empty aircraft. Well above
    /// the readouts' 100-step resolution, so a nearly empty aircraft is never called unavailable.
    /// </summary>
    public const double UnpublishedMinSimPounds = 1000;

    /// <summary>
    /// True when the tank read-outs are the aircraft's not-yet-published zeros, not fuel: every tank
    /// delivered reads 0 while the sim already carries fuel. MEASURED LIVE (2026-10-23, PW N831TD,
    /// cold load): all tanks read 0 for about 20 s while <c>FUEL TOTAL QUANTITY WEIGHT</c> read
    /// 142,579 lb, then 26,500 each against 79,554 lb. A 0 cannot be told from "not written" by the
    /// var itself (an unwritten L:var reads 0), so the sim's weight is the only witness. Unknown sim
    /// weight is no evidence: the zeros are then taken as read.
    /// </summary>
    public static bool TanksUnpublished(IEnumerable<double?> tanks, double? simPounds)
    {
        if (simPounds is not { } sim || double.IsNaN(sim) || sim < UnpublishedMinSimPounds) return false;
        var delivered = tanks.Where(t => t != null).ToList();
        return delivered.Count > 0 && delivered.All(t => t == 0);
    }

    /// <summary>
    /// The tank total over the sim's fuel weight in pounds: above <see cref="PoundsRatioMin"/> the
    /// tanks read pounds, below <see cref="KilogramsRatioMax"/> kilograms. MEASURED LIVE
    /// (2026-10-23, GE N821TD): 78,600 over 78,639 lb on Imperial (1.00), 35,700 over 78,729 lb on
    /// Metric (0.45). The units are a factor of 2.2 apart, so the band between never names either.
    /// </summary>
    private const double PoundsRatioMin = 0.8;
    private const double KilogramsRatioMax = 0.6;

    /// <summary>
    /// The unit the tanks read in: true for pounds, false for kilograms, null for no unit. The
    /// option's bit (<see cref="TanksInPounds"/>) decides only where the sim's own fuel weight agrees:
    /// a flat 0 is a real setting (Metric, every other option off) but also what the option reads
    /// before the EFB writes it, or for good should TFDi rename it, and the bit alone would then call
    /// an Imperial pilot's pounds kilograms. A total between the units, or one naming the other unit
    /// than the bit, is no unit. With the sim weight unknown or under
    /// <see cref="UnpublishedMinSimPounds"/>, or every tank at 0, there is no evidence and the bit
    /// stands alone.
    /// </summary>
    public static bool? TankUnit(IEnumerable<double?> tanks, double? options, double? simPounds)
    {
        if (TanksInPounds(options) is not { } pounds) return null;
        if (simPounds is not { } sim || !double.IsFinite(sim) || sim < UnpublishedMinSimPounds) return pounds;
        double total = tanks.Sum(t => t ?? 0);
        if (total <= 0) return pounds;
        double ratio = total / sim;
        bool agrees = pounds ? ratio > PoundsRatioMin : ratio < KilogramsRatioMax;
        return agrees ? pounds : null;
    }

    /// <summary>
    /// The fuel keys' sentence: each tank read, then the total with its unit, all converted from
    /// the unit the tanks read in to the one asked for. A tank never delivered is left out; with
    /// none delivered, the tanks not yet published, or the unit unknown, the sentence says so and
    /// reads no number. The total is the sum of the tanks as spoken, so it always adds up by ear.
    /// </summary>
    public static string Describe(IReadOnlyList<(string Name, double? Value)> tanks, double? options, double? simPounds, bool kilograms)
    {
        if (!tanks.Any(t => t.Value != null)) return Unavailable;
        var values = tanks.Select(t => t.Value).ToList();
        if (TanksUnpublished(values, simPounds)) return Unavailable;
        if (TankUnit(values, options, simPounds) is not { } tanksInPounds) return UnitUnknown;

        var parts = new List<string>(tanks.Count + 1);
        double total = 0;
        foreach (var (name, value) in tanks)
        {
            if (value is not { } v) continue;
            double spoken = Math.Round(Convert(v, tanksInPounds, kilograms), MidpointRounding.AwayFromZero);
            total += spoken;
            // An empty aux/tail tank is normal on this aircraft, not a fault — say zero rather than
            // omitting it, so the pilot can tell "empty" from "not reported".
            parts.Add($"{name} {Whole(spoken)}");
        }
        parts.Add($"Total {Whole(total)} {UnitWord(kilograms)}");
        return string.Join(", ", parts);
    }

    /// <summary>A tank reading in the unit it was read in, converted to pounds or kilograms.</summary>
    private static double Convert(double value, bool tanksInPounds, bool wantKilograms)
    {
        if (tanksInPounds == !wantKilograms) return value;
        return wantKilograms ? value * KilogramsPerPound : value / KilogramsPerPound;
    }

    private static string UnitWord(bool kilograms) => kilograms ? "kilograms" : "pounds";

    private static string Whole(double v) => v.ToString("0", CultureInfo.InvariantCulture);

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
