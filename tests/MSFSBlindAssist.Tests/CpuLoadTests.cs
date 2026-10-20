using MSFSBlindAssist.Services.SimPerformance;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Process CPU percent the way Task Manager states it: CPU time over wall time across all cores.</summary>
public class CpuLoadTests
{
    [Fact]
    public void HalfASecondOfCpuInOneSecondOnTwoCores_IsTwentyFivePercent()
    {
        Assert.Equal(25.0, CpuLoad.Percent(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), 2)!.Value, 6);
    }

    [Fact]
    public void AllCoresBusy_IsOneHundred()
    {
        Assert.Equal(100.0, CpuLoad.Percent(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1), 8)!.Value, 6);
    }

    [Fact]
    public void TimerJitterOverOneHundred_IsClamped()
    {
        Assert.Equal(100.0, CpuLoad.Percent(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1), 8)!.Value, 6);
    }

    [Fact]
    public void NoWallTimeOrNoCores_IsUnknown()
    {
        Assert.Null(CpuLoad.Percent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 8));
        Assert.Null(CpuLoad.Percent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 0));
    }

    [Fact]
    public void NegativeCpuDelta_FromAProcessRestart_IsUnknown()
    {
        Assert.Null(CpuLoad.Percent(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1), 8));
    }
}
