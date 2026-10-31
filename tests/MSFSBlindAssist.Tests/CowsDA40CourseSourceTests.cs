using System.Globalization;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ The CRS knob sets the course of whatever the CDI is on (measured live on the NG,
/// 2026-10-06): NAV 2 moved NAV OBS:2, GPS moved GPS OBS VALUE. The typed Course wrote
/// VOR1_SET whatever the CDI said.
/// </summary>
public class CowsDA40CourseSourceTests
{
    [Theory]
    [InlineData(true, 1, "DA40_AP_CRS_GPS")]
    [InlineData(true, 2, "DA40_AP_CRS_GPS")]
    [InlineData(false, 1, "DA40_AP_CRS_SET")]
    [InlineData(false, 2, "DA40_AP_CRS_NAV2")]
    public void TheCourseIsTheCdis(bool onGps, int navRadio, string key)
        => Assert.Equal(key, CowsDA40Definition.CourseKeyFor(onGps, navRadio));

    [Fact]
    public void TheWriteChoosesTheReceiverInTheSim()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal(
                "(A:GPS DRIVES NAV1, Bool) if{ 95 (>K:GPS_OBS_SET) } els{ (A:AUTOPILOT NAV SELECTED, number) 2 == if{ 95 (>K:VOR2_SET) } els{ 95 (>K:VOR1_SET) } }",
                CowsDA40Definition.CourseWrite(95));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void EveryCourseRidesTheSettleAnnouncerAsCourse()
    {
        var vars = new CowsDA40Definition(DA40Variant.NG).GetVariables();
        foreach (var key in CowsDA40Definition.CourseKeys)
        {
            Assert.Contains(key, CowsDA40Definition.RadioAnnouncedKeys);
            Assert.True(vars[key].IsAnnounced, key);
        }
        Assert.Equal("NAV OBS:2", vars["DA40_AP_CRS_NAV2"].Name);
        Assert.Equal("GPS OBS VALUE", vars["DA40_AP_CRS_GPS"].Name);
        Assert.True(vars["DA40_CDI_GPS"].ExcludeFromMonitorManager);
        Assert.True(vars["DA40_CDI_NAV"].ExcludeFromMonitorManager);
    }

    [Theory]
    [InlineData(null, null, 3)]
    [InlineData(false, null, 3)]     // off GPS, but which NAV is not known yet
    [InlineData(true, null, 1)]
    [InlineData(false, 2, 1)]
    public void AnUnknownSourceLetsEveryCourseSpeak(bool? onGps, int? navRadio, int count)
        // Assuming GPS before the first batch dropped a NAV 2 course turned right after a connect.
        => Assert.Equal(count, CowsDA40Definition.CdiCourseKeysFor(onGps, navRadio).Length);
}
