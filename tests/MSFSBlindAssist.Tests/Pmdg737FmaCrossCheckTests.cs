using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737FmaCrossCheckTests
{
    private static FmaSample S(bool fd1 = true, bool fd2 = false, int alt = 1000, int hdg = 90) =>
        new(default, fd1, fd2, alt, hdg, true);

    private static (long, FmaSample)[] One(FmaSample s) => new[] { (0L, s) };

    [Theory]
    [InlineData(360, 0)]
    [InlineData(0, 360)]
    [InlineData(-10, 350)]
    public void Heading_0_and_360_match(int sdk, int mem)
    {
        var snap = new FmaSdkSnapshot(true, false, 1000, sdk);
        Assert.True(Pmdg737FmaCrossCheck.Passes(snap, One(S(hdg: mem)), 0));
        Assert.True(Pmdg737FmaCrossCheck.HeadingEquals(sdk, mem));
    }

    [Fact]
    public void Lagging_sdk_within_3s_passes()
    {
        var h = new[] { (0L, S(alt: 1000)), (1000L, S(alt: 1100)), (2000L, S(alt: 1200)) };
        var snap = new FmaSdkSnapshot(true, false, 1000, 90);
        Assert.True(Pmdg737FmaCrossCheck.Passes(snap, h, 2500));
        Assert.False(Pmdg737FmaCrossCheck.Passes(snap, h, 3500));
    }

    [Fact]
    public void Each_field_checked()
    {
        var m = One(S());
        Assert.True(Pmdg737FmaCrossCheck.Passes(new FmaSdkSnapshot(true, false, 1000, 90), m, 0));
        Assert.False(Pmdg737FmaCrossCheck.Passes(new FmaSdkSnapshot(false, false, 1000, 90), m, 0));
        Assert.False(Pmdg737FmaCrossCheck.Passes(new FmaSdkSnapshot(true, true, 1000, 90), m, 0));
        Assert.False(Pmdg737FmaCrossCheck.Passes(new FmaSdkSnapshot(true, false, 2000, 90), m, 0));
        Assert.False(Pmdg737FmaCrossCheck.Passes(new FmaSdkSnapshot(true, false, 1000, 91), m, 0));
    }

    [Fact]
    public void Empty_history_fails() =>
        Assert.False(Pmdg737FmaCrossCheck.Passes(new FmaSdkSnapshot(true, false, 1000, 90),
            Array.Empty<(long, FmaSample)>(), 0));
}
