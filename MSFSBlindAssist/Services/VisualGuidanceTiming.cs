namespace MSFSBlindAssist.Services;

/// <summary>
/// The timing arithmetic visual guidance's limits and filters run on (VG-20). Guidance updates on
/// every sim frame, so a limit or filter written per update would change with the frame rate; these
/// helpers turn the design's per-second values into per-frame steps from the measured frame time.
/// </summary>
internal static class VisualGuidanceTiming
{
    /// <summary>The longest interval one update counts for: the one-second interval the limits were designed at.</summary>
    public const double MaxFrameSeconds = 1.0;

    /// <summary>
    /// Seconds since the previous update, capped at <see cref="MaxFrameSeconds"/>. Zero when there
    /// is no previous update, or when the clock stood still or stepped back (a time sync), so a
    /// backwards step never moves anything in reverse.
    /// </summary>
    public static double FrameSeconds(DateTime? previousUtc, DateTime nowUtc)
    {
        if (previousUtc is not DateTime previous || nowUtc <= previous) return 0.0;
        return Math.Min((nowUtc - previous).TotalSeconds, MaxFrameSeconds);
    }

    /// <summary>Moves from <paramref name="previous"/> toward <paramref name="target"/> by at most rate × frame time.</summary>
    public static double RateLimit(double previous, double target, double maxRatePerSecond, double frameSeconds)
    {
        double maxStep = maxRatePerSecond * frameSeconds;
        return previous + Math.Clamp(target - previous, -maxStep, maxStep);
    }

    /// <summary>
    /// Exponential smoothing with a per-second factor: α = factor^frameSeconds, so one update a
    /// second applies exactly the old per-update factor and thirty updates of 1/30 s land in the
    /// same place. Starts from the first sample.
    /// </summary>
    public static double Smooth(double? previous, double sample, double factorPerSecond, double frameSeconds)
    {
        if (previous is not double prev) return sample;
        double alpha = Math.Pow(factorPerSecond, frameSeconds);
        return alpha * prev + (1.0 - alpha) * sample;
    }
}

/// <summary>
/// A rate of change over a time window: the change from the oldest to the newest sample inside the
/// window, divided by the seconds between them. Unlike a sample count, the window covers the same
/// time at any frame rate.
/// </summary>
internal sealed class VisualGuidanceWindowedRate
{
    private const double MinSpanSeconds = 0.01;

    private readonly double _windowSeconds;
    private readonly Queue<(DateTime Utc, double Value)> _samples = new();
    private (DateTime Utc, double Value) _newest;

    public VisualGuidanceWindowedRate(double windowSeconds) { _windowSeconds = windowSeconds; }

    /// <summary>
    /// Adds a sample, then drops those older than the window (one exactly at its edge stays). A
    /// sample earlier than the newest (the clock stepped back, a time sync) starts the window
    /// afresh: the samples stamped after it would otherwise stay at its head and give no rate, then
    /// a wrong one.
    /// </summary>
    public void Add(DateTime utc, double value)
    {
        if (_samples.Count > 0 && utc < _newest.Utc) _samples.Clear();
        _samples.Enqueue((utc, value));
        _newest = (utc, value);
        DateTime oldestKept = utc.AddSeconds(-_windowSeconds);
        while (_samples.Count > 1 && _samples.Peek().Utc < oldestKept) _samples.Dequeue();
    }

    /// <summary>Units per second across the window; null with fewer than two samples or a span under 0.01 s.</summary>
    public double? Rate
    {
        get
        {
            if (_samples.Count < 2) return null;
            var oldest = _samples.Peek();
            double span = (_newest.Utc - oldest.Utc).TotalSeconds;
            return span < MinSpanSeconds ? null : (_newest.Value - oldest.Value) / span;
        }
    }

    public void Clear() => _samples.Clear();
}
