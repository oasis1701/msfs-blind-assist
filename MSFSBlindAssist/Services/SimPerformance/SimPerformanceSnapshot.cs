namespace MSFSBlindAssist.Services.SimPerformance;

/// <summary>
/// What the Sim Performance window shows at one instant. The resource fields come from the
/// background <see cref="SimResourceSampler"/>; the frame fields from the SimConnect "Frame" event
/// via <see cref="FrameRateMeter"/>. Every field is nullable: null means "not measurable right now",
/// and the formatter says so in words instead of showing a zero that reads as a measurement.
/// </summary>
public sealed record SimPerformanceSnapshot
{
    /// <summary>"FS2024", "FS2020", or null when no simulator process is running.</summary>
    public string? SimulatorVersion { get; init; }
    public string? ProcessName { get; init; }

    /// <summary>True while SimConnect is connected, i.e. frame events can arrive at all.</summary>
    public bool SimConnected { get; init; }
    /// <summary>The simulator's own frame rate, averaged over the last window; null when no frames came.</summary>
    public double? FrameRate { get; init; }
    /// <summary>The simulation rate the last frame reported (1 = real time, 0 = paused).</summary>
    public float? SimRate { get; init; }

    public double? SimCpuPercent { get; init; }
    public long? SimWorkingSetBytes { get; init; }

    public long? SystemMemoryTotalBytes { get; init; }
    public long? SystemMemoryAvailableBytes { get; init; }

    /// <summary>The busiest GPU engine of the simulator process, Task Manager style (max, not sum).</summary>
    public double? GpuPercent { get; init; }
    public string? GpuBusiestEngine { get; init; }
    public long? SimVideoMemoryBytes { get; init; }
    public long? AdapterVideoMemoryUsedBytes { get; init; }
    public long? AdapterVideoMemoryTotalBytes { get; init; }
    /// <summary>Why the GPU rows are unavailable, when they are.</summary>
    public string? GpuUnavailableReason { get; init; }

    public static SimPerformanceSnapshot Empty { get; } = new();
}
