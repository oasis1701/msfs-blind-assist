// Characterization tests for the SayIntentions radio-vs-cabin classifier.
//
// SayIntentions synthesizes cabin PA and crew intercom lines into the same
// message stream as real ATC traffic. The Ctrl+S readout must speak the last
// RADIO transmission — a blind pilot pressing it during taxi wants the ground
// controller, not the purser's welcome-aboard announcement.
//
// Channel is authoritative when present (COM1/COM2 and their _IN variants);
// otherwise the ATC-vocabulary heuristic decides. The cabin filter wins ties,
// so cabin content carried on a radio channel is still rejected.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsTransmissionClassifierTests
{
    [Theory]
    [InlineData("COM1")]
    [InlineData("com2")]
    [InlineData("COM1_IN")]
    [InlineData("COM2_IN")]
    public void RadioChannelsAreRadio(string channel)
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "ATC", "Toronto Ground", channel, "Taxi to runway 15L via Alpha"));
    }

    [Theory]
    [InlineData("PA")]
    [InlineData("INTERCOM")]
    [InlineData("CABIN")]
    public void NonRadioChannelsAreRejected(string channel)
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Crew", null, channel, "Taxi to runway 15L via Alpha"));
    }

    [Fact]
    public void CabinContentOnARadioChannelIsStillRejected()
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Flight Attendant", null, "COM1", "Cabin crew, please prepare for boarding"));
    }

    [Fact]
    public void AtcContentWithNoChannelIsAccepted()
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "ATC", "Toronto Ground", null, "Cleared to taxi, hold short of runway 23"));
    }

    [Fact]
    public void ChatterWithNoChannelAndNoAtcVocabularyIsRejected()
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, null, "Welcome aboard, our flight time today is two hours"));
    }

    [Fact]
    public void EmptyMessageIsNeverATransmission()
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission("ATC", "Ground", "COM1", "   "));
    }
}
