using MSFSBlindAssist.Services.SimPerformance;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The accumulator behind the Sim Performance window's frame-rate row. It averages the sim's own
/// per-frame figures over the window between two reads, falls back to the last frame while frames
/// are recent, and reports nothing once frames stop (menu, pause, disconnect).
/// </summary>
public class FrameRateMeterTests
{
    private const long Second = 1000;      // ticks are opaque; tests use milliseconds
    private const long Stale = 2 * Second;

    [Fact]
    public void AveragesTheFramesDeliveredSinceTheLastSample()
    {
        var m = new FrameRateMeter();
        m.Add(40f, 1f, 100);
        m.Add(44f, 1f, 120);
        m.Add(48f, 1f, 140);
        var s = m.Sample(nowTicks: 1000, staleAfterTicks: Stale);
        Assert.Equal(44.0, s.AverageFrameRate);
        Assert.Equal(3, s.FramesInWindow);
        Assert.Equal(1f, s.SimRate);
    }

    [Fact]
    public void SampleStartsANewWindow_ButKeepsTheLastFrameWhileFresh()
    {
        var m = new FrameRateMeter();
        m.Add(60f, 2f, 500);
        m.Sample(1000, Stale);
        var again = m.Sample(1500, Stale);     // no new frames, last one 1 s ago
        Assert.Equal(60.0, again.AverageFrameRate);
        Assert.Equal(0, again.FramesInWindow);
        Assert.Equal(2f, again.SimRate);
    }

    [Fact]
    public void NoFrameWithinTheStaleLimit_ReportsNothing()
    {
        var m = new FrameRateMeter();
        m.Add(60f, 1f, 500);
        m.Sample(1000, Stale);
        var later = m.Sample(500 + Stale + 1, Stale);
        Assert.Null(later.AverageFrameRate);
        Assert.Null(later.SimRate);
    }

    [Fact]
    public void NeverFed_ReportsNothing()
    {
        var s = new FrameRateMeter().Sample(5000, Stale);
        Assert.Null(s.AverageFrameRate);
        Assert.Equal(0, s.FramesInWindow);
        Assert.Null(s.SimRate);
    }

    [Fact]
    public void Reset_ForgetsTheLastFrame()
    {
        var m = new FrameRateMeter();
        m.Add(60f, 1f, 900);
        m.Reset();
        Assert.Null(m.Sample(1000, Stale).AverageFrameRate);
    }
}
