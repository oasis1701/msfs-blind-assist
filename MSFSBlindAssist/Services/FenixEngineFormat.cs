using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>The Fenix's weight unit (its <c>weight_unit</c> setting), used for the fuel flow rows.</summary>
public enum FenixEngineWeightUnit
{
    Kilograms,
    Pounds
}

/// <summary>
/// The Engines window's rows and status line. Rows follow the Airbus screens (E/WD N1, EGT, N2, fuel
/// flow, then the SD ENGINE page's oil, then TAT and OAT), engine 1 then engine 2 on adjacent rows,
/// worded like the PMDG 777 System Display ENG page. Invariant culture, ASCII minus, never "-0"; an
/// unknown value is <see cref="Unknown"/>, never 0. Oil units are the ones the Fenix's own SD ENGINE
/// page shows (QT, PSI, °C; read in the sim 2026-10-22).
/// </summary>
public static class FenixEngineFormat
{
    public const string Unknown = "--";
    public const double PoundsPerKilogram = 2.2046226218487757;
    public const int StaleMs = 10_000;

    private const string RefreshHint = " Values update every second; F5 reads them again.";
    private const string StaleHint = " No new values from the Fenix for more than 10 seconds; F5 reads them again.";

    /// <summary>A number at 0 or 1 decimal places, or <see cref="Unknown"/> for null, NaN or infinity.</summary>
    public static string Number(double? value, int decimals)
    {
        if (decimals is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "Engine values show 0 or 1 decimal places.");
        if (value is not double v || !double.IsFinite(v)) return Unknown;

        double rounded = Math.Round(v, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0.0;   // -0 from a small negative prints "-0"
        return rounded.ToString(decimals == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>The Fenix's <c>weight_unit</c> setting, or null when it is missing or not Kilograms/Pounds.</summary>
    public static FenixEngineWeightUnit? WeightUnitFrom(IReadOnlyList<FenixSetting> settings)
    {
        var setting = settings?.FirstOrDefault(s => s.Key == "weight_unit");
        return setting?.Value switch
        {
            "Kilograms" => FenixEngineWeightUnit.Kilograms,
            "Pounds" => FenixEngineWeightUnit.Pounds,
            _ => null
        };
    }

    /// <summary>
    /// The 18 rows for <paramref name="reading"/>. With a <paramref name="hold"/>, each number passes
    /// through it keyed by its row label (fuel flow after the pounds conversion).
    /// </summary>
    public static IReadOnlyList<string> Rows(FenixEngineReading reading, FenixEngineWeightUnit unit, FenixEngineDisplayHold? hold = null)
    {
        var rows = new List<string>(18);
        var engines = new[] { reading.Engine1, reading.Engine2 };
        bool pounds = unit == FenixEngineWeightUnit.Pounds;
        string flowUnit = pounds ? "pounds per hour" : "kilograms per hour";

        for (int i = 0; i < 2; i++)
        {
            bool? running = engines[i].Running;
            rows.Add($"Engine {i + 1}: {(running is bool r ? (r ? "running" : "not running") : Unknown)}");
        }
        AddPair("N1", e => e.N1, 1, "percent");
        AddPair("EGT", e => e.Egt, 0, "degrees C");
        AddPair("N2", e => e.N2, 1, "percent");
        AddPair("Fuel Flow", e => e.FuelFlowKgPerHour * (pounds ? PoundsPerKilogram : 1), 0, flowUnit);
        AddPair("Oil Quantity", e => e.OilQuantity, 1, "quarts");
        AddPair("Oil Pressure", e => e.OilPressure, 0, "psi");
        AddPair("Oil Temperature", e => e.OilTemperature, 0, "degrees C");
        Add("Total Air Temperature", reading.TatC, 0, "degrees C");
        Add("Outside Air Temperature", reading.OatC, 0, "degrees C");
        return rows;

        void AddPair(string name, Func<FenixEngineValues, double?> value, int decimals, string? suffix)
        {
            for (int i = 0; i < 2; i++)
                Add($"Engine {i + 1} {name}", value(engines[i]), decimals, suffix);
        }

        void Add(string label, double? value, int decimals, string? suffix)
        {
            double? shown = hold != null ? hold.Hold(label, Finite(value), decimals) : value;
            string number = Number(shown, decimals);
            rows.Add(number == Unknown || suffix == null ? $"{label}: {number}" : $"{label}: {number} {suffix}");
        }
    }

    /// <summary>
    /// The status line: the variant (when known), the fuel flow unit and where it came from, and whether
    /// values are flowing; before any value, the waiting text alone.
    /// </summary>
    public static string Status(string? variant, FenixEngineWeightUnit unit, bool unitFromFenix, long? lastUpdateTick, long nowTick)
    {
        if (lastUpdateTick is not long last)
            return "Waiting for engine values from the Fenix. Is the Fenix loaded?";

        string unitName = unit == FenixEngineWeightUnit.Pounds ? "pounds" : "kilograms";
        string unitSentence = unitFromFenix
            ? $"Fuel flow in {unitName} per hour, the Fenix's weight unit."
            : $"Fuel flow in {unitName} per hour; the Fenix's weight unit could not be read.";
        string prefix = variant != null ? variant + ". " : "";
        return prefix + unitSentence + (nowTick - last > StaleMs ? StaleHint : RefreshHint);
    }

    private static double? Finite(double? value) => value is double v && double.IsFinite(v) ? v : null;
}
