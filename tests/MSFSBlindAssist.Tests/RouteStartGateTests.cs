// Tests for MSFSBlindAssist.Navigation.RouteStartGate's stand-label parser, which the
// "did you mean" suggestion uses when a stand sits on a piece of taxi network the
// aircraft is not on (LMML 2026-08-31: "Parking 1" on the disconnected P-apron island).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteStartGateTests
{
    [Theory]
    [InlineData("Parking 1 - Ramp GA Large", "Parking", 1, "")]
    [InlineData("Southeast 22 - Ramp GA Medium", "Southeast", 22, "")]
    [InlineData("North 1 - Ramp GA Medium", "North", 1, "")]
    [InlineData("A 24A - Gate Medium, also A24 (online)", "A", 24, "A")]
    [InlineData("Gate B12 - Gate Medium", "Gate B", 12, "")]
    public void TryParseStandNumber_ParsesLabels(
        string label, string expectedPrefix, int expectedNumber, string expectedSuffix)
    {
        Assert.True(RouteStartGate.TryParseStandNumber(
            label, out string prefix, out int number, out string suffix));
        Assert.Equal(expectedPrefix, prefix);
        Assert.Equal(expectedNumber, number);
        Assert.Equal(expectedSuffix, suffix);
    }

    [Theory]
    [InlineData("Runway 31")]           // runways never take the stand-suggestion path...
    [InlineData("Taxiway E")]
    [InlineData("")]
    public void TryParseStandNumber_NumberlessOrNonGate(string label)
    {
        // ...though a numbered runway label WOULD parse ("Runway 31" → 31); the caller
        // gates on isRunwayDestination, so only genuinely numberless labels must be false.
        if (label == "Runway 31")
            Assert.True(RouteStartGate.TryParseStandNumber(label, out _, out _, out _));
        else
            Assert.False(RouteStartGate.TryParseStandNumber(label, out _, out _, out _));
    }
}
