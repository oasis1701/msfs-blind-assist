// Characterization tests for the SayIntentions radio-vs-cabin classifier.
//
// SayIntentions synthesizes cabin PA and crew intercom lines into the same
// message stream as real ATC traffic. The Ctrl+S readout must speak the last
// RADIO transmission — a blind pilot pressing it during taxi wants the ground
// controller, not the purser's welcome-aboard announcement.
//
// A RECOGNIZED channel is authoritative: a known radio channel (COM/VHF/HF, with
// or without a direction suffix, or a bare frequency) accepts, a known cabin
// channel (PA/INTERCOM/CABIN and friends) rejects. An UNRECOGNIZED channel must
// fall through to the ATC-vocabulary heuristic, never veto: the SayIntentions
// comms schema is third-party and undocumented, and the old allowlist-or-reject
// rule meant one unseen token ("com1_out", "ATC", a frequency string) silenced
// Ctrl+S for the whole flight. The cabin filter still wins ties, so cabin content
// carried on a radio channel is rejected.

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
    [InlineData("COM1_OUT")]
    [InlineData("com2_out")]
    [InlineData("COM 1")]
    [InlineData("COM3")]
    [InlineData("VHF1")]
    [InlineData("121.900")]
    public void OtherRadioChannelShapesAreAlsoRadio(string channel)
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Pilot", "Toronto Ground", channel, "Wilco"));
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

    [Theory]
    [InlineData("cabin_pa")]
    [InlineData("CREW")]
    [InlineData("PA_OUT")]
    public void OtherCabinChannelShapesAreAlsoRejected(string channel)
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Crew", null, channel, "Taxi to runway 15L via Alpha"));
    }

    // An unknown channel token must not veto the message — it just leaves the
    // decision to the ATC-vocabulary heuristic, exactly as an absent channel does.
    [Theory]
    [InlineData("ATC")]
    [InlineData("AIRBAND")]
    [InlineData("7")]
    public void UnrecognizedChannelDefersToTheAtcHeuristic(string channel)
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "ATC", "Toronto Ground", channel, "Taxi to runway 15L via Alpha"));

        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, channel, "See you at the hotel later"));
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
