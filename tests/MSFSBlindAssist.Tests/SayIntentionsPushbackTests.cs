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

    // Tail south-west means the NOSE finishes north-east. Getting this backwards would
    // send a blind pilot's mental picture 180 degrees out.
    [Fact]
    public void TheNoseFinishesOppositeTheTail()
    {
        string? advisory = SayIntentionsPushback.DescribeApproval(LiveApproval, LiveHeading, 0);

        Assert.NotNull(advisory);
        Assert.Contains("Tail to the south-west", advisory);
        Assert.Contains("finish facing north-east", advisory);
        Assert.Contains("heading 045", advisory);
    }

    // From 303 to 045 is 102 degrees the short way round — to the RIGHT. Computed
    // naively the wrap makes it a 258-degree left turn.
    [Fact]
    public void TheTurnTakesTheShortWayRound()
    {
        string? advisory = SayIntentionsPushback.DescribeApproval(LiveApproval, LiveHeading, 0);

        Assert.Contains("about 100 degrees right", advisory);
        Assert.DoesNotContain("left", advisory);
    }

    [Fact]
    public void TheTurnCanGoLeft()
    {
        // Facing 090, tail south-west, so the nose finishes 045 — 45 degrees left.
        string? advisory = SayIntentionsPushback.DescribeApproval(LiveApproval, 90, 0);

        Assert.Contains("about 45 degrees left", advisory);
    }

    // Magnetic variation is applied so the heading spoken is the one on the pilot's
    // instrument. KBOS is about 14 degrees west, i.e. magneticVariation = -14.
    [Fact]
    public void TheFinalHeadingIsMagnetic()
    {
        string? advisory = SayIntentionsPushback.DescribeApproval(LiveApproval, LiveHeading, -14);

        Assert.Contains("heading 059", advisory);
    }

    // "about 0 degrees right" is worse than saying nothing.
    [Fact]
    public void ANegligibleTurnIsNotDescribed()
    {
        string? advisory = SayIntentionsPushback.DescribeApproval(LiveApproval, 45, 0);

        Assert.Contains("finish facing north-east", advisory);
        Assert.DoesNotContain("degrees", advisory);
    }

    // The last-transmission hotkey works with SimConnect disconnected, so the advisory
    // has to survive having no aircraft state — minus the parts that need one.
    [Fact]
    public void WithoutAHeadingTheAdvisoryStillNamesTheOutcome()
    {
        string? advisory = SayIntentionsPushback.DescribeApproval(LiveApproval, null, 0);

        Assert.Equal("Tail to the south-west. You will finish facing north-east.", advisory);
    }

    // --- what must NOT be read as a pushback approval ---------------------------------

    [Fact]
    public void ATransmissionThatIsNotAnApprovalIsIgnored()
    {
        Assert.Null(SayIntentionsPushback.DescribeApproval(
            "Delta 123, taxi to runway 22R via Alpha, Tango.", 303, 0));
        Assert.Null(SayIntentionsPushback.DescribeApproval("Request pushback.", 303, 0));
        Assert.Null(SayIntentionsPushback.DescribeApproval(null, 303, 0));
    }

    // An approval with no direction in it still must not invent one.
    [Fact]
    public void AnApprovalWithoutADirectionProducesNothing()
    {
        Assert.Null(SayIntentionsPushback.DescribeApproval("Push and start approved.", 303, 0));
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
