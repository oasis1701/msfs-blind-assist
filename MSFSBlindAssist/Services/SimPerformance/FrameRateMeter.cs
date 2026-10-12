namespace MSFSBlindAssist.Services.SimPerformance;

/// <summary>
/// One sampling window of frame-rate readings. <see cref="AverageFrameRate"/> is the mean of the
/// simulator's own frame-rate figures delivered in the window, or the last figure seen when none
/// arrived but a frame was delivered within the staleness limit; null when no frame has been
/// delivered recently (sim not connected, paused in a menu, or the subscription not yet answered).
/// </summary>
public readonly record struct FrameRateSample(double? AverageFrameRate, int FramesInWindow, float? SimRate);

/// <summary>
/// Accumulates the SimConnect "Frame" system event between reads. The event arrives once per
/// rendered frame on the UI thread with the sim's own smoothed frame rate; a reader (the Sim
/// Performance window's one-second timer) takes the window's average, which keeps a whole-number
/// display steady instead of flickering with every frame. Pure: callers pass Stopwatch ticks, so
/// the timing is testable.
/// </summary>
public sealed class FrameRateMeter
{
    private readonly object _gate = new();
    private double _sum;
    private int _count;
    private float _lastFrameRate;
    private float _lastSimRate;
    private long _lastFrameTicks;

    /// <summary>Record one frame event.</summary>
    public void Add(float frameRate, float simRate, long nowTicks)
    {
        lock (_gate)
        {
            _sum += frameRate;
            _count++;
            _lastFrameRate = frameRate;
            _lastSimRate = simRate;
            _lastFrameTicks = nowTicks;
        }
    }

    /// <summary>
    /// Take the readings accumulated since the previous call and start a new window. A frame older
    /// than <paramref name="staleAfterTicks"/> no longer counts as a reading.
    /// </summary>
    public FrameRateSample Sample(long nowTicks, long staleAfterTicks)
    {
        lock (_gate)
        {
            bool fresh = _lastFrameTicks != 0 && nowTicks - _lastFrameTicks <= staleAfterTicks;
            double? average = _count > 0 ? _sum / _count : fresh ? _lastFrameRate : null;
            float? simRate = fresh || _count > 0 ? _lastSimRate : null;
            var sample = new FrameRateSample(average, _count, simRate);
            _sum = 0;
            _count = 0;
            return sample;
        }
    }

    /// <summary>Forget everything, including the last frame, so a new subscription starts clean.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _sum = 0;
            _count = 0;
            _lastFrameRate = 0;
            _lastSimRate = 0;
            _lastFrameTicks = 0;
        }
    }
}
