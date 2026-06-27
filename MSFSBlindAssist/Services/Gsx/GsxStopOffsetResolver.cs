namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Resolves the GSX per-aircraft stop offset for a gate by locating the airport's <c>.py</c>
/// profile, parsing it (cached by path + last-write-time), and evaluating the gate's offset
/// function for the resolved aircraft id. Returns <see cref="GsxOffset.Zero"/> (the safe base
/// position) on ANY miss — no profile, no function, unknown aircraft, parse error — so docking
/// is never worse than today. Never throws.
///
/// This is the WRITE-side counterpart to GateDataSource (which deliberately does NOT parse
/// <c>.py</c> for the gate LIST). Only the docking stop position consumes <c>.py</c> offsets.
/// </summary>
public sealed class GsxStopOffsetResolver
{
    private readonly GsxProfileLocator _locator;
    private readonly object _lock = new();
    // icao -> (profile path, last-write-time, parsed reader). One entry per airport is plenty.
    private readonly Dictionary<string, (string path, DateTime stamp, GsxPyProfileReader reader)> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    public GsxStopOffsetResolver(GsxProfileLocator? locator = null)
        => _locator = locator ?? new GsxProfileLocator();

    /// <summary>
    /// Computes the stop offset for <paramref name="icao"/> gate <paramref name="number"/>
    /// (+ optional <paramref name="suffix"/>) and aircraft <paramref name="ac"/>. Returns
    /// <see cref="GsxOffset.Zero"/> if no <c>.py</c> profile exists for the airport, the gate
    /// has no offset function, or anything else fails.
    /// </summary>
    public GsxOffset Resolve(string? icao, int number, string? suffix, GsxAircraftId? ac)
    {
        if (string.IsNullOrWhiteSpace(icao) || ac == null) return GsxOffset.Zero;
        try
        {
            var reader = GetReader(icao!);
            if (reader == null) return GsxOffset.Zero;
            return GsxPyOffsetEvaluator.Evaluate(reader, number, suffix, ac);
        }
        catch
        {
            return GsxOffset.Zero; // any locator/parse/eval failure -> base position
        }
    }

    private GsxPyProfileReader? GetReader(string icao)
    {
        if (!_locator.TryFindPyProfile(icao, out string path)) return null;

        DateTime stamp = File.GetLastWriteTimeUtc(path);
        lock (_lock)
        {
            if (_cache.TryGetValue(icao, out var c) && c.path == path && c.stamp == stamp)
                return c.reader;

            var reader = GsxPyProfileReader.Load(path);
            _cache[icao] = (path, stamp, reader);
            return reader;
        }
    }
}
