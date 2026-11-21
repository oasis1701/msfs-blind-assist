using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class CockpitCameraTargetTests
{
    [Theory]
    [InlineData("Overhead", CockpitCameraTarget.Overhead)]
    [InlineData("Overhead Forward", CockpitCameraTarget.Overhead)]
    [InlineData("Overhead Maintenance", CockpitCameraTarget.Overhead)]
    [InlineData("FCU", CockpitCameraTarget.Glareshield)]
    [InlineData("MCP", CockpitCameraTarget.Glareshield)]
    [InlineData("Glareshield", CockpitCameraTarget.Glareshield)]
    [InlineData("Pedestal", CockpitCameraTarget.Pedestal)]
    [InlineData("Center Pedestal", CockpitCameraTarget.Pedestal)]
    [InlineData("Main Panel", CockpitCameraTarget.MainPanel)]
    [InlineData("Forward Panel", CockpitCameraTarget.MainPanel)]
    [InlineData("Main Instrument Panel", CockpitCameraTarget.MainPanel)]
    [InlineData("Displays", CockpitCameraTarget.MainPanel)]
    [InlineData("MCDU", CockpitCameraTarget.MCDU)]
    [InlineData("CDU", CockpitCameraTarget.MCDU)]
    [InlineData("EFB", CockpitCameraTarget.EFB)]
    [InlineData("flyPad", CockpitCameraTarget.EFB)]
    public void ResolveSection_ResolvesKnownKeywords(string sectionName, CockpitCameraTarget expected)
    {
        var resolved = CockpitCameraTargetExtensions.ResolveSection(sectionName);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unrelated Section")]
    public void ResolveSection_ReturnsNullForUnknownOrEmpty(string? sectionName)
    {
        var resolved = CockpitCameraTargetExtensions.ResolveSection(sectionName);
        Assert.Null(resolved);
    }

    [Fact]
    public void FenixA320_CameraIndices_MatchExpectedViews()
    {
        var def = new FenixA320Definition();
        Assert.Equal(0, def.GetCameraViewForTarget(CockpitCameraTarget.MCDU));
        Assert.Equal(2, def.GetCameraViewForTarget(CockpitCameraTarget.EFB));
        Assert.Equal(4, def.GetCameraViewForTarget(CockpitCameraTarget.Overhead));
        Assert.Equal(5, def.GetCameraViewForTarget(CockpitCameraTarget.Pedestal));
        Assert.Equal(6, def.GetCameraViewForTarget(CockpitCameraTarget.Glareshield));
        Assert.Equal(7, def.GetCameraViewForTarget(CockpitCameraTarget.MainPanel));
    }

    [Fact]
    public void PMDG737_CameraIndices_MatchExpectedViews()
    {
        var def = new PMDG737Definition();
        Assert.Equal(0, def.GetCameraViewForTarget(CockpitCameraTarget.Glareshield));
        Assert.Equal(1, def.GetCameraViewForTarget(CockpitCameraTarget.MainPanel));
        Assert.Equal(2, def.GetCameraViewForTarget(CockpitCameraTarget.Overhead));
        Assert.Equal(4, def.GetCameraViewForTarget(CockpitCameraTarget.Pedestal));
        Assert.Equal(5, def.GetCameraViewForTarget(CockpitCameraTarget.MCDU));
    }
}

