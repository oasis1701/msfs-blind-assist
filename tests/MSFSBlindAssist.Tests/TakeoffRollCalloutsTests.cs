using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class TakeoffRollCalloutsTests
{
    [Theory]
    [InlineData(DA40Variant.NG, 67)]
    [InlineData(DA40Variant.XLS, 59)]
    public void TheDA40GetsOneCallAtItsOwnRotateSpeed(DA40Variant variant, double vr)
    {
        var p = TakeoffRollCallouts.For(new CowsDA40Definition(variant));

        Assert.Equal(vr, p.LowKts);
        Assert.Equal("Rotate", p.LowPhrase);
        Assert.Null(p.HighKts);
    }

    /// <summary>
    /// ⚠️ The DA40's profile was applied to the app-wide manager and never taken back, so
    /// every aircraft flown after it lost its 80 and 100 knot calls. Any other aircraft — and
    /// no aircraft at all — must get the airliner pair back.
    /// </summary>
    [Fact]
    public void EveryOtherAircraftGetsTheAirlinerPairBack()
    {
        Assert.Equal(TakeoffRollCallouts.Default, TakeoffRollCallouts.For(new FlyByWireA320Definition()));
        Assert.Equal(TakeoffRollCallouts.Default, TakeoffRollCallouts.For(null));
        Assert.Equal(80.0, TakeoffRollCallouts.Default.LowKts);
        Assert.Equal(100.0, TakeoffRollCallouts.Default.HighKts);
    }

    /// <summary>The DA40-only configure calls are gone; every path goes through Apply.</summary>
    [Fact]
    public void NoCallSiteConfiguresTheCalloutsByHand()
    {
        string root = System.AppContext.BaseDirectory;
        var dir = new System.IO.DirectoryInfo(root);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);

        foreach (string file in System.IO.Directory.GetFiles(System.IO.Path.Combine(dir!.FullName, "MSFSBlindAssist"), "MainForm*.cs"))
            Assert.DoesNotContain(".ConfigureSpeedCallouts(", System.IO.File.ReadAllText(file));
    }
}
