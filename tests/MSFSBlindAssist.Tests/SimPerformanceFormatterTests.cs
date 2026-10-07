using System.Globalization;
using MSFSBlindAssist.Services.SimPerformance;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Sim Performance window's rows. One value per row, whole numbers, invariant culture: a
/// screen-reader user arrows to a row and hears it re-read only when its value changes, so the
/// text must be stable under noise and identical under a comma-decimal Windows locale.
/// </summary>
public class SimPerformanceFormatterTests
{
    private static readonly long MB = 1024L * 1024L;

    private static SimPerformanceSnapshot Full() => new()
    {
        SimulatorVersion = "FS2024",
        ProcessName = "FlightSimulator2024",
        SimConnected = true,
        FrameRate = 42.4,
        SimRate = 1f,
        SimCpuPercent = 23.4,
        SimWorkingSetBytes = 11205 * MB,
        SystemMemoryTotalBytes = 32768 * MB,
        SystemMemoryAvailableBytes = 8456 * MB,
        GpuPercent = 63.5,
        GpuBusiestEngine = "3d",
        SimVideoMemoryBytes = 11205 * MB,
        AdapterVideoMemoryUsedBytes = 13190 * MB,
        AdapterVideoMemoryTotalBytes = 16303 * MB,
    };

    [Fact]
    public void FullSnapshot_OneValuePerRow_WholeNumbers()
    {
        var lines = SimPerformanceFormatter.Lines(Full());
        Assert.Equal(new[]
        {
            "Simulator: Microsoft Flight Simulator 2024, FlightSimulator2024.exe",
            "Frame rate: 42 FPS",
            "Frame time: 24 ms",
            "Simulation rate: 1x",
            "Simulator CPU: 23% of all cores",
            "Simulator memory: 11,205 MB",
            "System memory: 24,312 MB used of 32,768 MB, 74%",
            "GPU load: 64%, 3D engine",
            "Simulator video memory: 11,205 MB",
            "GPU memory in use: 13,190 MB of 16,303 MB",
        }, lines);
    }

    [Fact]
    public void CommaDecimalCulture_DoesNotChangeTheRows()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var lines = SimPerformanceFormatter.Lines(Full() with { SimRate = 0.5f });
            Assert.Contains("Simulator memory: 11,205 MB", lines);   // not "11.205"
            Assert.Contains("Simulation rate: 0.5x", lines);         // not "0,5x"
            Assert.Contains("Frame rate: 42 FPS", lines);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void SimNotRunning_SaysSoAndShowsOnlySystemMemory()
    {
        var lines = SimPerformanceFormatter.Lines(new SimPerformanceSnapshot
        {
            SystemMemoryTotalBytes = 32768 * MB,
            SystemMemoryAvailableBytes = 16384 * MB,
        });
        Assert.Equal(new[]
        {
            "Simulator: not running",
            "Frame rate: waiting for simulator connection",
            "System memory: 16,384 MB used of 32,768 MB, 50%",
        }, lines);
    }

    [Fact]
    public void RunningButNotConnected_FrameRowWaits_ResourceRowsShow()
    {
        var lines = SimPerformanceFormatter.Lines(Full() with { SimConnected = false, FrameRate = null, SimRate = null });
        Assert.Equal("Frame rate: waiting for simulator connection", lines[1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("Simulation rate", StringComparison.Ordinal));
        Assert.Contains("Simulator CPU: 23% of all cores", lines);
    }

    [Fact]
    public void ConnectedButNoFrames_SaysPausedOrLoading()
    {
        var lines = SimPerformanceFormatter.Lines(Full() with { FrameRate = null, SimRate = null });
        Assert.Equal("Frame rate: no frames received, sim paused or still loading", lines[1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("Frame time", StringComparison.Ordinal));
    }

    [Fact]
    public void BeforeFirstMeasurements_RowsSayMeasuring()
    {
        var lines = SimPerformanceFormatter.Lines(Full() with { SimCpuPercent = null, GpuPercent = null, GpuBusiestEngine = null });
        Assert.Contains("Simulator CPU: measuring", lines);
        Assert.Contains("GPU load: measuring", lines);
    }

    [Fact]
    public void GpuUnavailable_OneRowWithTheReason_NoVideoMemoryRows()
    {
        var lines = SimPerformanceFormatter.Lines(Full() with { GpuUnavailableReason = "GPU performance counters could not be read" });
        Assert.Contains("GPU: unavailable, GPU performance counters could not be read", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("GPU load", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("video memory", StringComparison.Ordinal));
    }

    [Fact]
    public void AdapterTotalUnknown_DropsTheOfPart()
    {
        var lines = SimPerformanceFormatter.Lines(Full() with { AdapterVideoMemoryTotalBytes = null });
        Assert.Contains("GPU memory in use: 13,190 MB", lines);
    }

    [Theory]
    [InlineData(1f, "1x")]
    [InlineData(0.5f, "0.5x")]
    [InlineData(0.25f, "0.25x")]
    [InlineData(4f, "4x")]
    [InlineData(0f, "paused")]
    public void SimRateText(float rate, string expected)
    {
        Assert.Equal(expected, SimPerformanceFormatter.SimRateText(rate));
    }

    [Theory]
    [InlineData("3d", "3D")]
    [InlineData("videodecode", "video decode")]
    [InlineData("VR", "VR")]
    [InlineData("something_new", "something_new")]
    public void EngineName(string engine, string expected)
    {
        Assert.Equal(expected, SimPerformanceFormatter.EngineName(engine));
    }
}
