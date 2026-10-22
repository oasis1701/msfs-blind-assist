namespace MSFSBlindAssist.Services;

/// <summary>One engine's Fenix values; null is unknown ([FNX-1]), never 0, false or "not running".</summary>
public sealed record FenixEngineValues(
    bool? Running,
    double? N1,
    double? Egt,
    double? N2,
    double? FuelFlowKgPerHour,
    double? OilQuantity,
    double? OilPressure,
    double? OilTemperature)
{
    public static readonly FenixEngineValues None = new(null, null, null, null, null, null, null, null);
}

/// <summary>
/// Both engines, total and outside air temperature (°C), and when the last known name arrived
/// (<see cref="Environment.TickCount64"/> by default; null before any).
/// </summary>
public sealed record FenixEngineReading(
    FenixEngineValues Engine1,
    FenixEngineValues Engine2,
    double? TatC,
    double? OatC,
    long? LastUpdateTick)
{
    public static readonly FenixEngineReading None = new(FenixEngineValues.None, FenixEngineValues.None, null, null, null);
}

/// <summary>
/// The latest Fenix engine values. The data-service subscription callback writes into it on a pool
/// thread ([FNX-2]) at 30-45 Hz and the Engines window's 1 s timer reads it on the UI thread, so the
/// window never marshals per update. Thread-safe: one lock guards every field.
/// </summary>
public sealed class FenixEngineSnapshot
{
    // Engine 0 is TAT/OAT; their field is unused.
    private static readonly Dictionary<string, (int Engine, FenixEngineField Field)> Map = BuildMap();

    private readonly Func<long> _clock;
    private readonly object _lock = new();
    private readonly bool?[] _running = new bool?[2];
    private readonly double?[,] _numbers = new double?[2, Enum.GetValues<FenixEngineField>().Length];
    private double? _tat;
    private double? _oat;
    private long? _lastUpdateTick;

    public FenixEngineSnapshot(Func<long>? clock = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
    }

    /// <summary>
    /// Stores one value from the data service (any thread). Names outside <see cref="FenixEngineNames.All"/>
    /// are ignored; a value of the wrong kind, NaN or infinity stores unknown.
    /// </summary>
    public void Apply(string name, FenixDataValue value)
    {
        if (name == null || !Map.TryGetValue(name, out var target)) return;

        double? number = value.Kind == FenixValueKind.Number && double.IsFinite(value.Number) ? value.Number : null;
        lock (_lock)
        {
            if (target.Engine == 0)
            {
                if (name == FenixEngineNames.Tat) _tat = number;
                else _oat = number;
            }
            else if (target.Field == FenixEngineField.Running)
            {
                _running[target.Engine - 1] = value.Kind == FenixValueKind.Bool ? value.Bool : null;
            }
            else
            {
                _numbers[target.Engine - 1, (int)target.Field] = number;
            }
            _lastUpdateTick = _clock();
        }
    }

    /// <summary>The current values (any thread).</summary>
    public FenixEngineReading Read()
    {
        lock (_lock)
            return new FenixEngineReading(Engine(0), Engine(1), _tat, _oat, _lastUpdateTick);
    }

    // Caller holds the lock.
    private FenixEngineValues Engine(int i) => new(
        _running[i],
        _numbers[i, (int)FenixEngineField.N1],
        _numbers[i, (int)FenixEngineField.Egt],
        _numbers[i, (int)FenixEngineField.N2],
        _numbers[i, (int)FenixEngineField.FuelFlowKg],
        _numbers[i, (int)FenixEngineField.OilQuantity],
        _numbers[i, (int)FenixEngineField.OilPressure],
        _numbers[i, (int)FenixEngineField.OilTemperature]);

    private static Dictionary<string, (int, FenixEngineField)> BuildMap()
    {
        var map = new Dictionary<string, (int, FenixEngineField)>(StringComparer.Ordinal);
        for (int engine = 1; engine <= 2; engine++)
        {
            foreach (FenixEngineField field in Enum.GetValues<FenixEngineField>())
                map[FenixEngineNames.For(engine, field)] = (engine, field);
        }
        map[FenixEngineNames.Tat] = (0, default);
        map[FenixEngineNames.Oat] = (0, default);
        return map;
    }
}
