using System;
using MSFSBlindAssist.Aircraft.G1000;
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
        var (now, _) = G1000CasWatcher.ComposeCasSpeech(
            new[] { "WARNING: ECU A FAIL", "Caution: PITOT HT OFF" }, Array.Empty<string>(), null);

        Assert.Equal("WARNING: ECU A FAIL. Caution: PITOT HT OFF.", now);
    }

    [Fact]
    public void AnAutopilotModeChangeJoinsTheSentenceInsteadOfCuttingItOff()
    {
        var (now, _) = G1000CasWatcher.ComposeCasSpeech(new[] { "Caution: LOW FUEL" }, Array.Empty<string>(),
            G1000CasWatcher.DescribeFmaChange("AP, GPS, ALT", "AP, HDG, ALT"));

        Assert.Equal("Caution: LOW FUEL. Autopilot HDG.", now);
    }

    // ⚠️ Arming the approach, a CDI change and the press read-back came seconds apart, and each
    // one cut off the one before (2026-10-27): a mode change alone is queued (DA40G-35).
    [Fact]
    public void AModeChangeAloneIsQueuedNotInterrupting()
    {
        var (now, queued) = G1000CasWatcher.ComposeCasSpeech(Array.Empty<string>(), Array.Empty<string>(),
            "Autopilot GS", "Navigation source LOC1");

        Assert.Equal("", now);
        Assert.Equal(new[] { "Navigation source LOC1. Autopilot GS." }, queued);
    }

    [Theory]
    // Arming the approach read the whole strip, then read the rest of it again (2026-10-27).
    [InlineData("ROL, PIT", "ROL, LOC armed, PIT, ALTS armed, GS armed", "Flight director LOC armed, ALTS armed, GS armed")]
    [InlineData("AP, ROL, LOC armed, PIT, GS armed", "AP, LOC, PIT, GS armed", "Autopilot LOC")]
    [InlineData("AP, LOC, PIT, GS armed", "AP, LOC, GS", "Autopilot GS")]
    [InlineData("AP, ROL, LOC armed, PIT, GS armed", "AP, ROL, PIT", "Autopilot LOC disarmed, GS disarmed")]
    // LOC captured and GS dropped must not sound like both dropped.
    [InlineData("AP, ROL, LOC armed, PIT, GS armed", "AP, LOC, PIT", "Autopilot LOC, GS disarmed")]
    [InlineData("AP, GPS armed, ROL, PIT", "AP, LOC armed, ROL, PIT", "Autopilot LOC armed, GPS disarmed")]
    // ALTS captures as ALTS and then ALT: neither is "ALTS disarmed".
    [InlineData("AP, GPS, FLC, ALTS armed", "AP, GPS, ALTS", "Autopilot ALTS")]
    [InlineData("AP, GPS, ALTS", "AP, GPS, ALT", "Autopilot ALT")]
    [InlineData("AP, GPS, FLC, ALTS armed", "AP, GPS, ALT", "Autopilot ALT")]
    [InlineData("AP, GPS, VPTH, ALTV armed", "AP, GPS, ALT", "Autopilot ALT")]
    [InlineData("AP, YD, GPS, ALT", "AP, GPS, ALT", "Autopilot YD off")]
    [InlineData("AP, HDG, ALT", "AP, HDG, ALT", "")]
    public void TheSameOwnerSaysOnlyWhatChanged(string previous, string current, string said)
        => Assert.Equal(said, G1000CasWatcher.DescribeFmaChange(previous, current));

    [Fact]
    public void AutopilotOffIsNotNews()
    {
        Assert.Equal("", G1000CasWatcher.DescribeFmaChange("AP, GPS, ALT", "off"));
        var (now, _) = G1000CasWatcher.ComposeCasSpeech(Array.Empty<string>(), Array.Empty<string>(), "");
        Assert.Equal("", now);
    }

    [Theory]
    // The AFCS breaker pulled in flight, 2026-10-12: the director stayed up in PIT.
    [InlineData("AP, GPS, ALT", "PIT, ALTS armed", "Flight director PIT, ALTS armed")]
    [InlineData("PIT, ALTS armed", "ALT", "Flight director ALT")]
    [InlineData("ALT", "AP, YD, GPS, ALT", "Autopilot YD, GPS, ALT")]
    [InlineData("off", "AP", "Autopilot on")]
    public void ModesWithoutApAreTheFlightDirectors(string previous, string current, string said)
        => Assert.Equal(said, G1000CasWatcher.DescribeFmaChange(previous, current));

    [Fact]
    public void AClearedMessageIsQueuedBehindNotSpokenOverANewOne()
    {
        var (now, queued) = G1000CasWatcher.ComposeCasSpeech(
            new[] { "Caution: LOW VOLTS" }, new[] { "Caution: PITOT HT OFF" }, null);

        Assert.Equal("Caution: LOW VOLTS.", now);
        Assert.Equal(new[] { "PITOT HT OFF cleared" }, queued);
    }
    [Theory]
    [InlineData("AP, YD, GPS, FLC, ALTS armed, target 88 KT", "AP, YD, GPS, FLC, ALTS armed")]
    [InlineData("AP, GPS, VS, ALTS armed, target -500 FPM", "AP, GPS, VS, ALTS armed")]
    [InlineData("AP, HDG, ALT", "AP, HDG, ALT")]
    public void TheFmaIsComparedOnItsModesNotItsTarget(string row, string modes)
        => Assert.Equal(modes, G1000CasWatcher.FmaModes(row));

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
        var cas = G1000CasWatcher.ExtractCasMessages(rows);
        Assert.Equal(new[] { "Alert: SCHEDULER 1 - FUEL XFER" }, G1000CasWatcher.AlertsNotOnCas(rows, cas));
    }

    [Fact]
    public void AClearedAlertIsSaidWithoutItsPrefix()
    {
        var (_, queued) = G1000CasWatcher.ComposeCasSpeech(
            Array.Empty<string>(), new[] { "Alert: SCHEDULER 1 - FUEL XFER" }, null);
        Assert.Equal(new[] { "SCHEDULER 1 - FUEL XFER cleared" }, queued);
    }
}
