using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>One engine value the Fenix data service publishes, in the Engines window's row order.</summary>
public enum FenixEngineField
{
    Running,
    N1,
    Egt,
    N2,
    FuelFlowKg,
    OilQuantity,
    OilPressure,
    OilTemperature
}

/// <summary>
/// The 18 Fenix data-service names behind the Engines window: eight per engine plus total and outside
/// air temperature. All read non-null live on Fenix 3.0.0.7426; the vibration values read "" (not
/// published) and are left out. Names from open sources only ([FNX-3]).
/// </summary>
public static class FenixEngineNames
{
    public const string Tat = "aircraft.temperature.tat";
    public const string Oat = "aircraft.temperature.oat";

    /// <summary>Engine 1's eight in <see cref="FenixEngineField"/> order, engine 2's eight, then TAT and OAT.</summary>
    public static IReadOnlyList<string> All { get; } = BuildAll();

    /// <summary>The data-service name of one engine's value; <paramref name="engine"/> is 1 or 2.</summary>
    public static string For(int engine, FenixEngineField field)
    {
        if (engine is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(engine), engine, "The A320 family has engines 1 and 2.");

        string suffix = field switch
        {
            FenixEngineField.Running => "running",
            FenixEngineField.N1 => "n1",
            FenixEngineField.Egt => "egt",
            FenixEngineField.N2 => "n2",
            FenixEngineField.FuelFlowKg => "ff.kg",
            FenixEngineField.OilQuantity => "oil.quantity",
            FenixEngineField.OilPressure => "oil.pressure",
            FenixEngineField.OilTemperature => "oil.temperature",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };
        return string.Create(CultureInfo.InvariantCulture, $"aircraft.engines.{engine}.{suffix}");
    }

    private static IReadOnlyList<string> BuildAll()
    {
        var names = new List<string>();
        for (int engine = 1; engine <= 2; engine++)
        {
            foreach (FenixEngineField field in Enum.GetValues<FenixEngineField>())
                names.Add(For(engine, field));
        }
        names.Add(Tat);
        names.Add(Oat);
        return names.AsReadOnly();
    }
}
