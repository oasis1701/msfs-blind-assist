using System.Globalization;

namespace MSFSBlindAssist.Services.SimPerformance;

/// <summary>
/// Renders a <see cref="SimPerformanceSnapshot"/> as the rows of the Sim Performance window, one
/// value per row so a screen-reader user arrows to the figure they want and hears only that row
/// change. Whole numbers throughout: a row re-reads whenever its text changes, so a decimal of
/// noise would make the focused row chatter every second. Invariant culture, so a comma-decimal
/// Windows locale still produces "11,205 MB" and not "11.205 MB".
/// </summary>
public static class SimPerformanceFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const long OneMegabyte = 1024L * 1024L;

    public static IReadOnlyList<string> Lines(SimPerformanceSnapshot s)
    {
        var lines = new List<string>(10);

        if (s.SimulatorVersion == null)
            lines.Add("Simulator: not running");
        else
            lines.Add($"Simulator: {SimulatorName(s.SimulatorVersion)}{(s.ProcessName == null ? "" : $", {s.ProcessName}.exe")}");

        if (!s.SimConnected)
            lines.Add("Frame rate: waiting for simulator connection");
        else if (s.FrameRate is { } fps && fps > 0)
        {
            lines.Add($"Frame rate: {Math.Round(fps).ToString("N0", Inv)} FPS");
            lines.Add($"Frame time: {Math.Round(1000.0 / fps).ToString("N0", Inv)} ms");
        }
        else
            lines.Add("Frame rate: no frames received, sim paused or still loading");

        if (s.SimConnected && s.SimRate is { } rate)
            lines.Add($"Simulation rate: {SimRateText(rate)}");

        if (s.SimulatorVersion != null)
        {
            lines.Add(s.SimCpuPercent is { } cpu
                ? $"Simulator CPU: {Math.Round(cpu).ToString("N0", Inv)}% of all cores"
                : "Simulator CPU: measuring");
            lines.Add(s.SimWorkingSetBytes is { } ws
                ? $"Simulator memory: {Mb(ws)} MB"
                : "Simulator memory: unavailable");
        }

        if (s.SystemMemoryTotalBytes is { } total && total > 0 && s.SystemMemoryAvailableBytes is { } avail)
        {
            long used = Math.Max(0, total - avail);
            lines.Add($"System memory: {Mb(used)} MB used of {Mb(total)} MB, {Math.Round(used * 100.0 / total).ToString("N0", Inv)}%");
        }

        if (s.SimulatorVersion != null)
        {
            if (s.GpuUnavailableReason != null)
                lines.Add($"GPU: unavailable, {s.GpuUnavailableReason}");
            else
            {
                lines.Add(s.GpuPercent is { } gpu
                    ? $"GPU load: {Math.Round(gpu).ToString("N0", Inv)}%{(s.GpuBusiestEngine == null ? "" : $", {EngineName(s.GpuBusiestEngine)} engine")}"
                    : "GPU load: measuring");
                if (s.SimVideoMemoryBytes is { } vram)
                    lines.Add($"Simulator video memory: {Mb(vram)} MB");
                if (s.AdapterVideoMemoryUsedBytes is { } adapterUsed)
                    lines.Add(s.AdapterVideoMemoryTotalBytes is { } adapterTotal && adapterTotal > 0
                        ? $"GPU memory in use: {Mb(adapterUsed)} MB of {Mb(adapterTotal)} MB"
                        : $"GPU memory in use: {Mb(adapterUsed)} MB");
            }
        }

        return lines;
    }

    public static string SimulatorName(string version) => version switch
    {
        "FS2024" => "Microsoft Flight Simulator 2024",
        "FS2020" => "Microsoft Flight Simulator 2020",
        _ => version,
    };

    /// <summary>"1x" at real time, "0.5x"/"4x" at other rates, "paused" at 0.</summary>
    public static string SimRateText(float rate)
    {
        if (rate <= 0) return "paused";
        double r = Math.Round(rate, 2);
        return $"{r.ToString("0.##", Inv)}x";
    }

    /// <summary>The counter's engine type as spoken text: "3d" → "3D", "videodecode" → "video decode".</summary>
    public static string EngineName(string engineType) => engineType.ToLowerInvariant() switch
    {
        "3d" => "3D",
        "copy" => "copy",
        "compute" => "compute",
        "videodecode" => "video decode",
        "videoencode" => "video encode",
        "videoprocessing" => "video processing",
        "vr" => "VR",
        _ => engineType,
    };

    private static string Mb(long bytes) => (bytes / OneMegabyte).ToString("N0", Inv);
}
