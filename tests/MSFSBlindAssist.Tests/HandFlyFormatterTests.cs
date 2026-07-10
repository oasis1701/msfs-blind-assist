using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for Hand Fly's spoken pitch/bank formatters. These pin
/// the whole-degree, no-decimals output: the callout stream fires as often as
/// every 500 ms, so decimals made it unbearably wordy, and the pitch style must
/// match TakeoffAssistManager's so the liftoff handoff sounds seamless.
/// </summary>
public class HandFlyFormatterTests
{
    [Theory]
    [InlineData(12.4, "+12")]
    [InlineData(12.6, "+13")]
    [InlineData(1.0, "+1")]
    [InlineData(-2.4, "-2")]
    [InlineData(-15.7, "-16")]
    [InlineData(0.0, "level")]
    [InlineData(0.4, "level")]
    [InlineData(-0.4, "level")]
    // Midpoints: (int)Math.Round defaults to banker's rounding (to even) — the
    // pinned platform default, same precedent as FuelReadoutFormatTests. The .5
    // literals are exactly representable doubles, so these rows are stable.
    [InlineData(12.5, "+12")]
    [InlineData(13.5, "+14")]
    [InlineData(-12.5, "-12")]
    [InlineData(0.5, "level")]
    public void FormatPitchAnnouncement_SpeaksWholeDegrees(double pitch, string expected)
    {
        Assert.Equal(expected, HandFlyManager.FormatPitchAnnouncement(pitch));
    }

    // SimConnect bank convention: positive = left, negative = right.
    [Theory]
    [InlineData(3.4, "left 3")]
    [InlineData(29.6, "left 30")]
    [InlineData(-2.6, "right 3")]
    [InlineData(-45.2, "right 45")]
    [InlineData(0.0, "wings level")]
    [InlineData(0.3, "wings level")]
    [InlineData(-0.4, "wings level")]
    // Midpoints: banker's rounding (to even), applied after Math.Abs.
    [InlineData(2.5, "left 2")]
    [InlineData(-3.5, "right 4")]
    [InlineData(0.5, "wings level")]
    public void FormatBankAnnouncement_SpeaksWholeDegreesWithDirection(double bank, string expected)
    {
        Assert.Equal(expected, HandFlyManager.FormatBankAnnouncement(bank));
    }
}
