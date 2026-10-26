using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// The six values the Fenix First Officer reads from the Fenix's own data service rather than from
/// L:vars: both engines' N2 and running flag, the cabin-ready signal and the total fuel in kilograms.
/// The service's subscription pushes four of them (<see cref="SubscribedNames"/>) and the evaluator
/// polls the other two, the N2s (<see cref="PolledNames"/>), once a second: measured live
/// (2026-10-22), a subscription sends an engine's N2 ONCE, when it opens, and never again
/// (one value through a whole shutdown, 62.3 % to 5 %), while a query answers a fresh N2 every time.
/// Both arrive from a thread-pool thread (FNX-2), so they are stored here under one lock and read by
/// the evaluator's synthetic fields; nothing in this class speaks.
///
/// A value the service has not sent, sent as unknown, or sent as the wrong kind reads NaN, never 0 or
/// false (FNX-1). A polled N2 the Fenix answers null (measured: an engine that is off) replaces the
/// number before it at once, and reads NaN. A read with no answer (the gateway is down, the Fenix is
/// not loaded: every name comes back "") is not handed to this class by the evaluator's poll callback,
/// so it leaves the last number in place. That number goes stale: N2 reads NaN once it is older than
/// <see cref="N2StaleAfterMs"/>, because a poll that stopped answering must not leave the last number
/// saying "running"; past 5 s it is not trusted and the stock N2 serves instead. The poll refreshes a
/// steady N2 every second, so only a failing poll lets it go stale. The flags and the fuel are not aged:
/// they keep their last value, on the assumption (not measured) that they change rarely.
/// </summary>
public sealed class FenixFoLiveReadings
{
    public const string Engine1N2 = "aircraft.engines.1.n2";
    public const string Engine2N2 = "aircraft.engines.2.n2";
    public const string Engine1Running = "aircraft.engines.1.running";
    public const string Engine2Running = "aircraft.engines.2.running";
    public const string CabinReadyName = "aircraft.communication.cids.cabinReady";
    public const string FuelTotalKgName = "aircraft.fuel.total.amount.kg";

    /// <summary>How long an N2 reading counts as current.</summary>
    public const long N2StaleAfterMs = 5_000;

    /// <summary>Every name this class reads: <see cref="SubscribedNames"/> and <see cref="PolledNames"/>
    /// together. Neither the subscription nor the poll takes all six.</summary>
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
    {
        Engine1N2, Engine2N2, Engine1Running, Engine2Running, CabinReadyName, FuelTotalKgName,
    });

    /// <summary>The names the data service's subscription streams: both running flags (sent on change)
    /// and the fuel total (sent every 2 s), measured live 2026-10-22, and the cabin-ready signal (not part
    /// of that measurement; it is a flag, so assumed to be sent on change like the running flags).</summary>
    public static IReadOnlyList<string> SubscribedNames { get; } = Array.AsReadOnly(new[]
    {
        Engine1Running, Engine2Running, CabinReadyName, FuelTotalKgName,
    });

    /// <summary>The names read by query once a second, because the subscription sends them once and never
    /// again (measured live 2026-10-22): both engines' N2.</summary>
    public static IReadOnlyList<string> PolledNames { get; } = Array.AsReadOnly(new[]
    {
        Engine1N2, Engine2N2,
    });

    private readonly Func<long> _clockMs;
    private readonly object _lock = new();
    private readonly Dictionary<string, (FenixDataValue Value, long ReceivedAtMs)> _values = new(StringComparer.Ordinal);

    /// <param name="clockMs">Milliseconds on a monotonic clock; the default is <see cref="Environment.TickCount64"/>.</param>
    public FenixFoLiveReadings(Func<long>? clockMs = null) => _clockMs = clockMs ?? (() => Environment.TickCount64);

    public void Apply(string name, FenixDataValue value)
    {
        lock (_lock)
            _values[name] = (value, _clockMs());
    }

    public void Clear()
    {
        lock (_lock)
            _values.Clear();
    }

    /// <summary>N2 in percent of the given engine (1 or 2), or NaN when unknown or older than <see cref="N2StaleAfterMs"/>.</summary>
    public double N2(int engine)
    {
        string? name = engine switch { 1 => Engine1N2, 2 => Engine2N2, _ => null };
        if (name == null) return double.NaN;

        lock (_lock)
        {
            if (!_values.TryGetValue(name, out var entry) || entry.Value.Kind != FenixValueKind.Number)
                return double.NaN;
            return _clockMs() - entry.ReceivedAtMs > N2StaleAfterMs ? double.NaN : entry.Value.Number;
        }
    }

    /// <summary>1 when the given engine (1 or 2) reports running, 0 when it reports not running, else NaN.</summary>
    public double Running(int engine) => engine switch
    {
        1 => Flag(Engine1Running),
        2 => Flag(Engine2Running),
        _ => double.NaN,
    };

    /// <summary>1 when the cabin crew report the cabin ready, 0 when not, NaN when unknown.</summary>
    public double CabinReady => Flag(CabinReadyName);

    /// <summary>Total fuel on board in kilograms, NaN when unknown.</summary>
    public double FuelTotalKg
    {
        get
        {
            lock (_lock)
                return _values.TryGetValue(FuelTotalKgName, out var entry) && entry.Value.Kind == FenixValueKind.Number
                    ? entry.Value.Number
                    : double.NaN;
        }
    }

    private double Flag(string name)
    {
        lock (_lock)
            return _values.TryGetValue(name, out var entry) && entry.Value.Kind == FenixValueKind.Bool
                ? (entry.Value.Bool ? 1.0 : 0.0)
                : double.NaN;
    }
}
