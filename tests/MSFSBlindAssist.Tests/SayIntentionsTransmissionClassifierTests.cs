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

    // Still rejected after the override was added: the speaker field itself ("Flight
    // Attendant") is a cabin marker, which blocks IsCabinVetoOverridden on the fields leg
    // regardless of message shape — and this message carries no instruction shape either,
    // so both override legs independently fail. This pins the surviving filter.
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

    [Theory]
    // The veto's dangerous direction is a silenced ATC instruction. One cabin word in
    // a genuine ground instruction must not kill it — it also vanishes from the
    // clearance selector's history, which never sees a filtered record.
    [InlineData("ATC", "Metro Ground", "COM1", "Taxi via Alpha, Bravo to the passenger terminal")]
    // SI's standard clearance shape: destination BEFORE the via-list. The override must
    // rescue this shape, not just a verb-adjacent "taxi via" — see AtcInstructionVocabulary.
    [InlineData("ATC", "Metro Ground", "COM1", "Taxi to the passenger terminal via Alpha, Bravo")]
    [InlineData("ATC", "Ground", null, "Hold position, passenger aircraft crossing left to right")]
    [InlineData("ATC", "Tower", "118.700", "Line up and wait runway 27, passenger jet departing ahead")]
    public void AnAtcInstructionCarryingACabinWordIsStillRadio(
        string speaker, string station, string? channel, string message)
        => Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            speaker, station, channel, message));

    [Theory]
    // The override needs an imperative instruction SHAPE, not ATC-adjacent nouns:
    // purser speech routinely carries "taxi", "runway" and "cleared to land" as prose.
    [InlineData("", null, null, "Please keep your seat belts fastened while we taxi to the runway")]
    [InlineData("", null, null, "Ladies and gentlemen we have been cleared to land, cabin crew please be seated")]
    // Plural "attendants" — CabinVocabulary must catch it the same as the singular form,
    // mirroring the existing PASSENGERS? tolerance in the same regex.
    [InlineData("", null, null, "Ladies and gentlemen we have been cleared to land, flight attendants please be seated")]
    // A cabin marker in the CHANNEL stays authoritative whatever the message says.
    [InlineData("", "Purser", "PA", "Cabin crew be seated, we are holding short of runway 27")]
    public void CabinSpeechStaysFilteredEvenWhenItSoundsOperational(
        string speaker, string? station, string? channel, string message)
        => Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            speaker, station, channel, message));
}
