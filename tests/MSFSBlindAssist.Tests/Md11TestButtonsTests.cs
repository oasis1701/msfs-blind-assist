using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's test buttons are hold-to-test: their lights stay on while the button is held and
/// go out on release (measured live on the fire test, 2026-09-06). A 60 ms tap can end before the
/// 1 Hz lamp batch ever sees a light, so the pilot hears nothing. These are held instead.
/// </summary>
public class Md11TestButtonsTests
{
    [Theory]
    [InlineData("MD11_AOVHD_FIRETEST_BT")]
    [InlineData("MD11_AOVHD_CRGSMK_TEST_BT")]
    [InlineData("MD11_OVHD_HYD_HYD_TEST_BT")]
    [InlineData("MD11_OVHD_FUEL_QTY_TEST_BT")]
    [InlineData("MD11_OVHD_LTS_EMER_TEST_BT")]
    [InlineData("MD11_OVHD_CRG_DOOR_TEST_BT")]
    [InlineData("MD11_OVHD_CVR_TEST_BT")]
    [InlineData("MD11_LSIDE_OXY_TEST_BT")]
    [InlineData("MD11_RSIDE_OXY_TEST_BT")]
    [InlineData("MD11_MIP_ISFD_TEST_BT")]
    [InlineData("MD11_PED_XPNDR_TEST_BT")]
    public void EveryTestButton_IsHeld(string nodeId) => Assert.True(Md11TestButtons.IsHoldToTest(nodeId));

    [Fact]
    public void TheWeatherRadarTestMode_IsASelection_NotAHeldTest()
        => Assert.False(Md11TestButtons.IsHoldToTest("MD11_PED_WXR_TEST_BT"));

    [Fact]
    public void OrdinaryButtons_AreNotHeld()
    {
        Assert.False(Md11TestButtons.IsHoldToTest("MD11_OVHD_ELEC_BATT_BT"));
        Assert.False(Md11TestButtons.IsHoldToTest("MD11_CGS_NAV_BT"));
        // The annunciator light test stays a tap: held, it lights all ~488 lamps and the lamp
        // announcer would speak a sentence for each, lit and again dark.
        Assert.False(Md11TestButtons.IsHoldToTest("MD11_OVHD_ANNUNLT_TEST_BT"));
    }

    [Fact]
    public void EveryHeldButton_ExistsInTheMap_AsAButton()
    {
        var map = Md11ControlMap.Load();
        foreach (var id in Md11TestButtons.HoldToTest)
        {
            var c = map.Controls.SingleOrDefault(x => string.Equals(x.NodeId, id, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(c);
            Assert.Equal(Md11Kinds.Button, c!.Kind);
            Assert.True(c.Events.ContainsKey("LEFT_BUTTON_DOWN") && c.Events.ContainsKey("LEFT_BUTTON_UP"), $"{id} needs a down/up pair to be held");
        }
    }

    [Fact]
    public void TheHold_IsLongEnoughForTheLampBatch_AndShortEnoughToFeelLikeAPress()
    {
        Assert.InRange(Md11TestButtons.HoldMs, 2000, 5000);
    }
}
