namespace MSFSBlindAssist.Services.SimPerformance;

/// <summary>
/// Process CPU load the way Task Manager states it: the CPU time a process consumed over a wall-clock
/// interval, spread across every logical processor, as a percentage of the whole machine.
/// </summary>
public static class CpuLoad
{
    public static double? Percent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
    {
        if (wallDelta <= TimeSpan.Zero || processorCount <= 0 || cpuDelta < TimeSpan.Zero) return null;
        double pct = cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * processorCount) * 100.0;
        return Math.Clamp(pct, 0.0, 100.0);
    }
}
