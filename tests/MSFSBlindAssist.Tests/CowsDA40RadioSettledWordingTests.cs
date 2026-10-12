using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What the settle announcer says when a value moves outside MSFSBA: an autopilot selected
/// value turned on external hardware is just the value (the pilot's request, 2026-10-12), a
/// radio always says which radio.
/// </summary>
public class CowsDA40RadioSettledWordingTests
{
    [Theory]
    [InlineData("DA40_AP_HDG_SET", 250, "250")]
    [InlineData("DA40_AP_HDG_SET", 5, "005")]
    [InlineData("DA40_AP_HDG_SET", 360, "360")]
    [InlineData("DA40_AP_HDG_SET", 0, "360")]
    [InlineData("DA40_AP_ALT_SET", 10000, "10000")]
    [InlineData("DA40_AP_VS_SET", -500, "-500")]
    [InlineData("DA40_AP_IAS_SET", 90, "90")]
    [InlineData("DA40_AP_CRS_SET", 284, "284")]
    public void OneSelectedValueIsJustTheValue(string key, double value, string said)
        => Assert.Equal(said, CowsDA40Definition.ComposeSettled(new Dictionary<string, double> { [key] = value }));

    [Fact]
    public void TwoSelectedValuesTogetherKeepTheirNames()
        => Assert.Equal("Selected altitude 10000. Selected vertical speed 500",
            CowsDA40Definition.ComposeSettled(new Dictionary<string, double>
            {
                ["DA40_AP_VS_SET"] = 500,
                ["DA40_AP_ALT_SET"] = 10000
            }));

    [Fact]
    public void ARadioAlwaysSaysWhichRadio()
        => Assert.Equal("COM 1 active 118.205. COM 1 standby 123.325",
            CowsDA40Definition.ComposeSettled(new Dictionary<string, double>
            {
                ["DA40_RADIO_COM1_SET"] = 123.325,
                ["DA40_RADIO_COM1_ACTIVE"] = 118.205
            }));

    [Fact]
    public void ACommaDecimalCultureStillSaysAPoint()
    {
        var was = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("NAV 1 standby 112.70",
                CowsDA40Definition.ComposeSettled(new Dictionary<string, double> { ["DA40_RADIO_NAV1_SET"] = 112.7 }));
        }
        finally { Thread.CurrentThread.CurrentCulture = was; }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheSelectedValuesRideTheFastFeed(DA40Variant variant)
    {
        // The settle is 300 ms because the feed is per frame; on the 1 Hz batch it would
        // announce every value a knob swept through (DA40S-2).
        var vars = new CowsDA40Definition(variant).GetVariables();
        foreach (var key in new[] { "DA40_AP_ALT_SET", "DA40_AP_VS_SET", "DA40_AP_IAS_SET", "DA40_AP_HDG_SET", "DA40_AP_CRS_SET", "DA40_AP_CRS_NAV2", "DA40_AP_CRS_GPS" })
        {
            Assert.True(vars[key].HighFrequency && vars[key].ExcludeFromBatch, key + " is not on a per-frame feed");
            Assert.Equal(UpdateFrequency.Continuous, vars[key].UpdateFrequency);
        }
    }
}
