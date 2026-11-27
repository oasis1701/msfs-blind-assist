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
    [InlineData("EVT_OH_ELEC_BATTERY_SWITCH", CockpitCameraTarget.Overhead)]
    [InlineData("Battery 1: ON", CockpitCameraTarget.Overhead)]
    [InlineData("Landing lights: ON", CockpitCameraTarget.Overhead)]
    [InlineData("Fuel pumps: ON", CockpitCameraTarget.Overhead)]
    [InlineData("EVT_MCP_HDG_SEL_SWITCH", CockpitCameraTarget.Glareshield)]
    [InlineData("Flight Director: ON", CockpitCameraTarget.Glareshield)]
    [InlineData("EVT_PED_SPEEDBRAKE_LEVER", CockpitCameraTarget.Pedestal)]
    [InlineData("Flaps: 1", CockpitCameraTarget.Pedestal)]
    [InlineData("Autobrake: RTO", CockpitCameraTarget.MainPanel)]
    [InlineData("Landing Gear: DOWN", CockpitCameraTarget.MainPanel)]
    [InlineData("Cockpit Door", CockpitCameraTarget.CockpitDoor)]
    [InlineData("S_COCKPIT_DOOR", CockpitCameraTarget.CockpitDoor)]
    [InlineData("Cabin Door", CockpitCameraTarget.CockpitDoor)]
    public void ResolveSection_ResolvesKnownKeywords(string sectionName, CockpitCameraTarget expected)
    {
        var resolved = CockpitCameraTargetExtensions.ResolveSection(sectionName);
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveSection_MultipleCandidates_ResolvesFirstMatch()
    {
        var resolved = CockpitCameraTargetExtensions.ResolveSection(null, "Unknown", "Battery: ON");
        Assert.Equal(CockpitCameraTarget.Overhead, resolved);
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
        Assert.Null(def.GetCameraViewForTarget(CockpitCameraTarget.MainPanel));
        Assert.Equal(12, def.GetCameraViewForTarget(CockpitCameraTarget.CockpitDoor));
    }

    [Fact]
    public void PMDG737_CameraIndices_MatchExpectedViews()
    {
        var def = new PMDG737Definition();
        Assert.Equal(0, def.GetCameraViewForTarget(CockpitCameraTarget.Glareshield));
        Assert.Null(def.GetCameraViewForTarget(CockpitCameraTarget.MainPanel));
        Assert.Equal(2, def.GetCameraViewForTarget(CockpitCameraTarget.Overhead));
        Assert.Equal(4, def.GetCameraViewForTarget(CockpitCameraTarget.Pedestal));
        Assert.Equal(5, def.GetCameraViewForTarget(CockpitCameraTarget.MCDU));
        Assert.Equal(8, def.GetCameraViewForTarget(CockpitCameraTarget.CockpitDoor));
    }
}

