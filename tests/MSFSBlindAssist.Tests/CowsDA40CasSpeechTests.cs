using System;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ Every new CAS message used to be its own interrupting announcement, so two cautions
/// raised together were heard as the last one alone, and an autopilot mode change in the same
/// poll cut both off.
/// </summary>
public class CowsDA40CasSpeechTests
{
    [Fact]
    public void TwoNewMessagesAreOneUtteranceAndBothAreHeard()
    {
        var (now, _) = CowsDA40Definition.ComposeCasSpeech(
            new[] { "WARNING: ECU A FAIL", "Caution: PITOT HT OFF" }, Array.Empty<string>(), null);

        Assert.Equal("WARNING: ECU A FAIL. Caution: PITOT HT OFF.", now);
    }

    [Fact]
    public void AnAutopilotModeChangeJoinsTheSentenceInsteadOfCuttingItOff()
    {
        var (now, _) = CowsDA40Definition.ComposeCasSpeech(new[] { "Caution: LOW FUEL" }, Array.Empty<string>(),
            CowsDA40Definition.DescribeFmaChange("AP, GPS, ALT", "AP, HDG, ALT"));

        Assert.Equal("Caution: LOW FUEL. Autopilot HDG, ALT.", now);
    }

    [Fact]
    public void AutopilotOffIsNotNews()
    {
        Assert.Equal("", CowsDA40Definition.DescribeFmaChange("AP, GPS, ALT", "off"));
        var (now, _) = CowsDA40Definition.ComposeCasSpeech(Array.Empty<string>(), Array.Empty<string>(), "");
        Assert.Equal("", now);
    }

    [Theory]
    // The AFCS breaker pulled in flight, 2026-10-12: the director stayed up in PIT.
    [InlineData("AP, GPS, ALT", "PIT, ALTS armed", "Autopilot off, flight director PIT, ALTS armed")]
    [InlineData("PIT, ALTS armed", "ALT", "Flight director ALT")]
    [InlineData("ALT", "AP, YD, GPS, ALT", "Autopilot YD, GPS, ALT")]
    [InlineData("off", "AP", "Autopilot on")]
    public void ModesWithoutApAreTheFlightDirectors(string previous, string current, string said)
        => Assert.Equal(said, CowsDA40Definition.DescribeFmaChange(previous, current));

    [Fact]
    public void AClearedMessageIsQueuedBehindNotSpokenOverANewOne()
    {
        var (now, queued) = CowsDA40Definition.ComposeCasSpeech(
            new[] { "Caution: LOW VOLTS" }, new[] { "Caution: PITOT HT OFF" }, null);

        Assert.Equal("Caution: LOW VOLTS.", now);
        Assert.Equal(new[] { "PITOT HT OFF cleared" }, queued);
    }
    [Theory]
    [InlineData("AP, YD, GPS, FLC, ALTS armed, target 88 KT", "AP, YD, GPS, FLC, ALTS armed")]
    [InlineData("AP, GPS, VS, ALTS armed, target -500 FPM", "AP, GPS, VS, ALTS armed")]
    [InlineData("AP, HDG, ALT", "AP, HDG, ALT")]
    public void TheFmaIsComparedOnItsModesNotItsTarget(string row, string modes)
        => Assert.Equal(modes, CowsDA40Definition.FmaModes(row));

    [Fact]
    public void AnAlertThatIsAlreadyACasLineIsNotSaidTwice()
    {
        // Captured in flight, LOWG climb-out 2026-10-12.
        var rows = new[]
        {
            "CAS messages: 1", "  Caution: PITOT HT OFF",
            "Alert messages: 2", "  PITOT HT OFF - Pitot heat is off.", "  SCHEDULER 1 - FUEL XFER",
            "Autopilot: AP, GPS, FLC, ALTS armed, target 88 KT"
        };
        var cas = CowsDA40Definition.ExtractCasMessages(rows);
        Assert.Equal(new[] { "Alert: SCHEDULER 1 - FUEL XFER" }, CowsDA40Definition.AlertsNotOnCas(rows, cas));
    }

    [Fact]
    public void AClearedAlertIsSaidWithoutItsPrefix()
    {
        var (_, queued) = CowsDA40Definition.ComposeCasSpeech(
            Array.Empty<string>(), new[] { "Alert: SCHEDULER 1 - FUEL XFER" }, null);
        Assert.Equal(new[] { "SCHEDULER 1 - FUEL XFER cleared" }, queued);
    }
}
