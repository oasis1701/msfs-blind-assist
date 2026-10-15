using System.Globalization;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the A220 panel helpers (Plan B): the speedbrake lever's cockpit
/// write and detent read-back ([A220-40]) and the spoken press confirmations (CORE-7). CI runs
/// en-US, so every number that reaches the sim or the screen reader is also checked under a
/// comma-decimal culture.
/// </summary>
public class A220PanelsTests
{
    [Theory]
    [InlineData(0, "0 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(1, "0.2 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(2, "0.4 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(3, "0.6 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(4, "0.8 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(5, "1 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(-3, "0 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(9, "1 0.5 - 32768 * (>K:SPOILERS_SET)")]
    public void SpeedbrakeCalc_IsTheCockpitLeversOwnWrite(int detent, string expected)
        => Assert.Equal(expected, A220Panels.SpeedbrakeCalc(detent));

    [Fact]
    public void SpeedbrakeCalc_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("0.6 0.5 - 32768 * (>K:SPOILERS_SET)", A220Panels.SpeedbrakeCalc(3));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.09, 0)]
    [InlineData(0.13, 1)]
    [InlineData(0.31, 2)]
    [InlineData(0.6, 3)]
    [InlineData(0.79, 4)]
    [InlineData(1.0, 5)]
    [InlineData(-0.02, 0)]   // read-back a hair below RET
    [InlineData(1.03, 5)]    // a hair above MAX
    public void SpeedbrakeDetentForTravel_PicksTheNearestDetent(double travel, double detent)
        => Assert.Equal(detent, A220Panels.SpeedbrakeDetentForTravel(travel));

    [Fact]
    public void SpeedbrakeDetentForTravel_TreatsNaNAsRetracted()
        => Assert.Equal(0, A220Panels.SpeedbrakeDetentForTravel(double.NaN));

    [Fact]
    public void SpeedbrakeDetents_HaveOneLabelEach()
    {
        Assert.Equal(6, A220Panels.SpeedbrakeDetentTravel.Count);
        Assert.Equal(A220Panels.SpeedbrakeDetentTravel.Count, A220Panels.SpeedbrakeDetentLabels.Count);
        Assert.Equal("Retracted", A220Panels.SpeedbrakeDetentLabels[0]);
        Assert.Equal("Max (emergency)", A220Panels.SpeedbrakeDetentLabels[5]);
    }

    [Theory]
    [InlineData("Left Engine Fire (guarded)", "Left Engine Fire pressed")]
    [InlineData("CVR Test", "CVR Test pressed")]
    [InlineData("Mechanic Call", "Mechanic Call pressed")]
    public void PressedText_DropsTheGuardNote(string displayName, string expected)
        => Assert.Equal(expected, A220Panels.PressedText(displayName));

    [Theory]
    [InlineData(1, "Transmit VHF 1")]
    [InlineData(2, "Transmit VHF 2")]
    [InlineData(3, "Transmit VHF 3")]
    [InlineData(0, "No transmit radio selected")]
    [InlineData(5, "Transmit channel 5")]
    [InlineData(-1, "Transmit radio not available")]
    public void TxChannelText_NamesTheSelectedTransmitter(double value, string expected)
        => Assert.Equal(expected, A220Panels.TxChannelText(value));

    [Fact]
    public void PressConfirmKeys_AreTheStatelessPresses()
        => Assert.Equal(
            new[] { "A22X_CVR_ERASE", "A22X_CVR_TEST", "A22X_L_ENG_FIRE", "A22X_MECH_CALL", "A22X_R_ENG_FIRE" },
            A220Panels.PressConfirmKeys.OrderBy(k => k, StringComparer.Ordinal));
}
