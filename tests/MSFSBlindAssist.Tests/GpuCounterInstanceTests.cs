using MSFSBlindAssist.Services.SimPerformance;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Instance-name shapes of the Windows GPU performance counters, as sampled on a Windows 11 machine
/// with an RTX 5080 (Get-Counter '\GPU Engine(*)\Utilization Percentage'): the sampler picks the
/// simulator's rows by pid and ties its memory to the adapter by the luid_…_phys_N key.
/// </summary>
public class GpuCounterInstanceTests
{
    [Fact]
    public void EngineInstance_YieldsPidAdapterAndEngineType()
    {
        Assert.True(GpuCounterInstance.TryParse("pid_30804_luid_0x00000000_0x0000ee5d_phys_0_eng_0_engtype_3d",
            out int pid, out string adapter, out string? engine));
        Assert.Equal(30804, pid);
        Assert.Equal("luid_0x00000000_0x0000ee5d_phys_0", adapter);
        Assert.Equal("3d", engine);
    }

    [Fact]
    public void MultiWordEngineType_IsKeptWhole()
    {
        Assert.True(GpuCounterInstance.TryParse("pid_33700_luid_0x00000000_0x0000ee5d_phys_0_eng_6_engtype_videoencode",
            out _, out _, out string? engine));
        Assert.Equal("videoencode", engine);
    }

    [Fact]
    public void ProcessMemoryInstance_HasNoEngine_AdapterKeyMatchesAdapterMemoryInstance()
    {
        Assert.True(GpuCounterInstance.TryParse("pid_30804_luid_0x00000000_0x0000ee5d_phys_0",
            out int pid, out string adapter, out string? engine));
        Assert.Equal(30804, pid);
        Assert.Equal("luid_0x00000000_0x0000ee5d_phys_0", adapter);
        Assert.Null(engine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("luid_0x00000000_0x0000ee5d_phys_0")]
    [InlineData("pid_abc_luid_0x0_0x1_phys_0")]
    [InlineData("pid_luid_0x0_0x1_phys_0")]
    [InlineData("pid__luid_0x0_0x1_phys_0")]
    [InlineData("pid_4_phys_0")]
    public void OtherShapes_AreRejected(string? instance)
    {
        Assert.False(GpuCounterInstance.TryParse(instance, out _, out _, out _));
    }
}
