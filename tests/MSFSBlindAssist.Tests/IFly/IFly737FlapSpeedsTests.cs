// Characterization tests for IFly737FlapSpeeds (Aircraft/IFly737FlapSpeeds.cs):
// the calculated 737 MAX 8 flap maneuvering speeds behind the FMS Data panel row
// and the output-mode Shift+1..6 hotkeys. Pins the FCTM additive schedule
// (UP +70 / 1 +50 / 5 +30 / 10 +30 / 15 +20 / 25 +10 over VREF40), the
// sqrt-law VREF40 anchor (138 kt @ 60 t), the screen-reader-safe panel wording
// (the "at" separator that keeps "flaps 1" and the speed from running together
// into one number), and the unavailable guard rails.

namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.Aircraft;

public class IFly737FlapSpeedsTests
{
    [Fact]
    public void Vref40_AtAnchorWeight_IsAnchorSpeed()
    {
        Assert.Equal(138, IFly737FlapSpeeds.Vref40Knots(60000));
    }

    [Theory]
    [InlineData(45000, 120)]  // 138 * sqrt(45/60) = 119.5 -> 120
    [InlineData(80000, 159)]  // 138 * sqrt(80/60) = 159.35 -> 159
    public void Vref40_ScalesBySquareRootOfWeight(double kg, int expected)
    {
        Assert.Equal(expected, IFly737FlapSpeeds.Vref40Knots(kg));
    }

    [Fact]
    public void ManeuverSpeeds_FollowTheFctmAdditiveSchedule()
    {
        // At the 60 t anchor: VREF40 = 138, so UP 208 / 1 188 / 5 168 /
        // 10 168 / 15 158 / 25 148.
        int[] expected = { 208, 188, 168, 168, 158, 148 };
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], IFly737FlapSpeeds.ManeuverSpeedKnots(60000, i));
    }

    [Fact]
    public void FlapNames_AreSpokenForms()
    {
        Assert.Equal("Flaps up", IFly737FlapSpeeds.FlapName(0));
        Assert.Equal("Flaps 1", IFly737FlapSpeeds.FlapName(1));
        Assert.Equal("Flaps 25", IFly737FlapSpeeds.FlapName(5));
    }

    [Fact]
    public void PanelText_UsesAtSeparators_SoNumbersNeverRunTogether()
    {
        Assert.Equal(
            "Up 208, flaps 1 at 188, flaps 5 at 168, flaps 10 at 168, flaps 15 at 158, flaps 25 at 148",
            IFly737FlapSpeeds.ComposePanelText(60000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20000)]   // below MinWeightKg — sim not ready / cold cache
    [InlineData(120000)]  // above MaxWeightKg — garbage read
    public void PanelText_OutOfRangeWeight_SaysUnavailable(double kg)
    {
        Assert.False(IFly737FlapSpeeds.IsPlausibleWeight(kg));
        Assert.Equal("Unavailable", IFly737FlapSpeeds.ComposePanelText(kg));
    }
}
