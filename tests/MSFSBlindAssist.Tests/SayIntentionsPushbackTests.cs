// Pushback approvals. The wording is a live capture, KBOS 2026-07-29:
//
//     IN : Request pushback.
//     OUT: Push and start approved. Tail South-West.
//
// SayIntentions names the TAIL, as a compass point. A sighted pilot looks outside and
// knows what that means; a blind pilot has a heading indicator and nothing else, so a
// compass point alone says nothing about which way the aircraft will rotate or where it
// ends up pointing. These pin the conversion that closes that gap.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsPushbackTests
{
    private const string LiveApproval = "Push and start approved. Tail South-West.";

    // Heading 303 was the live aircraft state at the time of this transmission.
    private const double LiveHeading = 303;

    [Fact]
    public void TheLiveApprovalIsRecognizedAndTheTailDirectionRead()
    {
        var tail = SayIntentionsPushback.ParseTailDirection(LiveApproval);

        Assert.NotNull(tail);
        Assert.Equal("south-west", tail!.Value.Spoken);
        Assert.Equal(225, tail.Value.Bearing);
    }

    // Live case. Heading 303, tail south-west: the push finishes you on 045, so it is
    // a RIGHT turn. Measured the naive way the wrap makes it 258 the other way, which
    // is the one error that would actively mislead.
    [Fact]
    public void TheLiveApprovalIsARightTurn()
    {
        Assert.Equal("right", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, LiveHeading));
    }

    [Fact]
    public void TheTurnCanGoLeft()
    {
        // Facing 090, tail south-west: the push finishes you on 045.
        Assert.Equal("left", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 90));
    }

    // "Straight" is an ANSWER, not the absence of one — a pilot who hears nothing
    // cannot tell it from a readout that failed.
    [Fact]
    public void ADeadAsternTailIsAStraightPush()
    {
        Assert.Equal("straight", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 45));
    }

    // SayIntentions was seen choosing from an EIGHT-point compass, so its answer can sit
    // up to 22.5 degrees from the truth. A genuinely straight push has to survive that
    // much slop without being called a turn; nothing finer is recoverable at this
    // resolution.
    [Theory]
    [InlineData(25)]   // 20 degrees off dead astern
    [InlineData(65)]   // 20 the other way
    public void SayIntentionsCompassRoundingStillReadsAsStraight(double heading)
    {
        Assert.Equal("straight", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, heading));
    }

    // Three answers, and only three.
    [Theory]
    [InlineData(0)] [InlineData(45)] [InlineData(90)] [InlineData(135)]
    [InlineData(180)] [InlineData(225)] [InlineData(270)] [InlineData(315)]
    public void EveryHeadingGivesOneOfThreeAnswers(double heading)
    {
        Assert.Contains(
            SayIntentionsPushback.DescribeTurnDirection(LiveApproval, heading),
            new[] { "straight", "left", "right" });
    }

    // The last-transmission hotkey works with SimConnect disconnected and before SI has
    // written a heading. Without one there is nothing to compare against, and the
    // transmission is still read out.
    [Fact]
    public void WithoutAHeadingNoDirectionIsGiven()
    {
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(LiveApproval, null));
    }

    // --- what must NOT be read as a pushback approval ---------------------------------

    [Fact]
    public void ATransmissionThatIsNotAnApprovalIsIgnored()
    {
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(
            "Delta 123, taxi to runway 22R via Alpha, Tango.", 303));
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection("Request pushback.", 303));
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(null, 303));
    }

    // An approval with no direction in it still must not invent one.
    [Fact]
    public void AnApprovalWithoutADirectionProducesNothing()
    {
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection("Push and start approved.", 303));
    }

    // --- direction parsing ------------------------------------------------------------

    // Longest name first. Matched shortest-first, "Tail South-South-West" would read as
    // plain "south" and put the pilot 22 degrees out with nothing to reveal it.
    [Theory]
    [InlineData("Push and start approved. Tail South.", "south", 180)]
    [InlineData("Push and start approved. Tail South-West.", "south-west", 225)]
    [InlineData("Push and start approved. Tail South-South-West.", "south-south-west", 202.5)]
    [InlineData("Push and start approved. Tail North.", "north", 0)]
    [InlineData("Push and start approved. Tail North-East.", "north-east", 45)]
    public void CompoundDirectionsWinOverTheirPrefixes(string message, string spoken, double bearing)
    {
        var tail = SayIntentionsPushback.ParseTailDirection(message);

        Assert.NotNull(tail);
        Assert.Equal(spoken, tail!.Value.Spoken);
        Assert.Equal(bearing, tail.Value.Bearing);
    }

    // SI wrote "South-West"; a space or nothing at all is the same direction.
    [Theory]
    [InlineData("Push and start approved. Tail South West.")]
    [InlineData("Push and start approved. Tail Southwest.")]
    [InlineData("Push and start approved. tail south-west.")]
    public void SeparatorsAndCaseDoNotMatter(string message)
    {
        Assert.Equal("south-west", SayIntentionsPushback.ParseTailDirection(message)!.Value.Spoken);
    }
}
