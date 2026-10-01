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
        var (now, _) = CowsDA40Definition.ComposeCasSpeech(new[] { "Caution: LOW FUEL" }, Array.Empty<string>(), "HDG ALT");

        Assert.Equal("Caution: LOW FUEL. Autopilot HDG ALT.", now);
    }

    [Fact]
    public void AutopilotOffIsNotNews()
    {
        var (now, _) = CowsDA40Definition.ComposeCasSpeech(Array.Empty<string>(), Array.Empty<string>(), "off");
        Assert.Equal("", now);
    }

    [Fact]
    public void AClearedMessageIsQueuedBehindNotSpokenOverANewOne()
    {
        var (now, queued) = CowsDA40Definition.ComposeCasSpeech(
            new[] { "Caution: LOW VOLTS" }, new[] { "Caution: PITOT HT OFF" }, null);

        Assert.Equal("Caution: LOW VOLTS.", now);
        Assert.Equal(new[] { "PITOT HT OFF cleared" }, queued);
    }
}
