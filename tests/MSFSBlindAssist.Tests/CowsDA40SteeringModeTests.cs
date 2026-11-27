using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The gear lever cycles the steering assist through THREE modes on both airframes, named by
/// the PFD advisory each press flashes (panel.xml: STEER REAL, STEER AUTOBRAKE, STEER NWS).
/// As an Off/On switch, mode 2 was spoken as a raw number (2026-10-26).
/// </summary>
public class CowsDA40SteeringModeTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryModeTheGearLeverReachesHasItsAdvisoryName(DA40Variant variant)
    {
        var v = new CowsDA40Definition(variant).GetVariables()["DA40_OPT_WHEEL_ASSIST"];

        Assert.Equal(new[] { "Real", "Autobrake", "NWS" },
            v.ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, v.ValueDescriptions.Keys.OrderBy(k => k));
    }
}
