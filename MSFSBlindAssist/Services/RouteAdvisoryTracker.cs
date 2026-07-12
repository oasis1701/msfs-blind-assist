namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure announce-decision logic for route advisories (spec 2026-07-12 §4).
/// Baseline-first: the first successful Observe seeds silently (preflight discovery
/// belongs to the Weather Radar box, not a startup announcement burst). Afterwards,
/// Observe returns only keys never seen before. ClearAnnouncedKeys() drops the seen
/// set but KEEPS the baseline latch — the 15-minute reminder semantics shared with
/// MainForm's _announcedSigmetKeys, so a still-active advisory re-announces after
/// the periodic clear. Reset() (connect / aircraft switch) re-baselines fully.
/// </summary>
internal sealed class RouteAdvisoryTracker
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private bool _baselineDone;

    public IReadOnlyList<string> Observe(IReadOnlyList<string> keys)
    {
        var fresh = new List<string>();
        if (!_baselineDone)
        {
            foreach (var k in keys) _seen.Add(k);
            _baselineDone = true;
            return fresh;
        }
        foreach (var k in keys)
            if (_seen.Add(k)) fresh.Add(k);
        return fresh;
    }

    public void ClearAnnouncedKeys() => _seen.Clear();

    public void Reset()
    {
        _seen.Clear();
        _baselineDone = false;
    }
}
