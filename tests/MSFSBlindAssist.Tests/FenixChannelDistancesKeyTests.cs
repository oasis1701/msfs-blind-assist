// Characterization tests for what D and Shift+D say on the Fenix (Aircraft/FenixA320Definition.ChannelDistances.cs): the
// sentences, in the words and the time format the other aircraft's readouts use (BaseAircraftDefinition's
// AnnounceDestFromSDK and AnnounceTODFromSDK), and when the time is added. No sim and no socket: the ground speed
// arrives through a callback the test holds. What a pilot hears from the keys is checked in the sim ([CORE-5]).

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDistancesKeyTests
{
    [Fact]
    public void Sentences_follow_the_base()
    {
        Assert.Equal("412 miles to destination: 01:01:48", FenixA320Definition.DistanceSentence(false, 412, 400));
        Assert.Equal("96 miles to top of descent: 00:12:00", FenixA320Definition.DistanceSentence(true, 96, 480));
        // Too slow for a time: on the ground.
        Assert.Equal("412 miles to destination", FenixA320Definition.DistanceSentence(false, 412, 25));
    }

    [Fact]
    public void The_time_needs_thirty_knots()
    {
        Assert.Equal("412 miles to destination", FenixA320Definition.DistanceSentence(false, 412, 29.9));
        Assert.Equal("412 miles to destination: 13:44:00", FenixA320Definition.DistanceSentence(false, 412, 30));
    }

    [Fact]
    public void Past_and_not_available_are_the_base_s_words()
    {
        Assert.Equal("Past top of descent", FenixA320Definition.PastTopOfDescent);

        var speech = new SpeechCapture();
        var context = new FenixA320Definition.ChannelKeyContext(null!, speech, null!, null!, null);
        var definition = new FenixA320Definition();
        definition.SpeakNotAvailable(context, FenixA320Definition.DestinationName);
        definition.SpeakNotAvailable(context, FenixA320Definition.TopOfDescentName);

        Assert.Equal(new[] { "Distance to destination not available", "Top of descent not available" }, speech.Interrupts);
    }

    [Fact]
    public void A_disconnected_sim_still_speaks_the_distance()
    {
        // With SimConnect disconnected the ground speed request does nothing, so the distance is spoken at once with no
        // time, never silence.
        var spoken = new List<string>();
        bool requested = false;

        FenixA320Definition.SpeakDistance(false, 412, false, _ => requested = true, spoken.Add);

        Assert.Equal(new[] { "412 miles to destination" }, spoken);
        Assert.False(requested);
    }

    [Fact]
    public void A_connected_sim_speaks_when_the_ground_speed_comes()
    {
        var spoken = new List<string>();
        var requests = new List<Action<double>>();

        FenixA320Definition.SpeakDistance(false, 412, true, requests.Add, spoken.Add);

        Assert.Empty(spoken);
        Action<double> groundSpeedArrives = Assert.Single(requests);

        groundSpeedArrives(400);

        Assert.Equal(new[] { "412 miles to destination: 01:01:48" }, spoken);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Sentences_are_the_same_in_every_culture(string culture)
    {
        (string slow, string fast) = TestCulture.Run(culture, () => (
            FenixA320Definition.DistanceSentence(false, 412.4, 25),
            FenixA320Definition.DistanceSentence(false, 1234.6, 400)));

        Assert.Equal("412 miles to destination", slow);
        Assert.Equal("1235 miles to destination: 03:05:11", fast);
    }
}
