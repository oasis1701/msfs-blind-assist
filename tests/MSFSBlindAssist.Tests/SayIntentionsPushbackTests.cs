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

    // Tail south-west puts the nose north-east, i.e. 045. From 303 that is 102 degrees
    // the short way round — to the RIGHT. Computed naively the wrap makes it a
    // 258-degree LEFT turn, which is the one error that would actively mislead.
    [Fact]
    public void TheTurnTakesTheShortWayRound()
    {
        Assert.Equal("right", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, LiveHeading, 0));
    }

    [Fact]
    public void TheTurnCanGoLeft()
    {
        // Facing 090, tail south-west, so the nose finishes 045 — 45 degrees left.
        Assert.Equal("left", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 90, 0));
    }

    // The whole reason the advisory is one word: a magnetic variation's worth of error
    // cannot change it. KBOS is about 14 degrees west, and the live turn is 102 —
    // nowhere near either guard band.
    [Fact]
    public void VariationCannotFlipTheDirection()
    {
        Assert.Equal("right", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, LiveHeading, 0));
        Assert.Equal("right", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, LiveHeading, -14));
        Assert.Equal("right", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, LiveHeading, 14));
    }

    // Already pointing where it will end up: "right" would be describing nothing.
    [Fact]
    public void ANegligibleTurnIsNotClaimed()
    {
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 45, 0));
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 50, 0));
    }

    // Near a half-turn the two directions are equally valid, and which one comes out is
    // decided by heading noise and by the true-versus-magnetic assumption. Claiming one
    // would be inventing precision that isn't there.
    [Fact]
    public void ANearHalfTurnIsNotClaimedAsADirection()
    {
        // Facing 225 with the nose finishing 045 is exactly 180.
        Assert.Equal("about turn", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 225, 0));
        Assert.Equal("about turn", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 215, 0));
        Assert.Equal("about turn", SayIntentionsPushback.DescribeTurnDirection(LiveApproval, 235, 0));
    }

    // The last-transmission hotkey works with SimConnect disconnected. Without a
    // heading there is no direction to give, and the transmission is still read.
    [Fact]
    public void WithoutAHeadingNoDirectionIsGiven()
    {
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(LiveApproval, null, 0));
    }

    // --- what must NOT be read as a pushback approval ---------------------------------

    [Fact]
    public void ATransmissionThatIsNotAnApprovalIsIgnored()
    {
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(
            "Delta 123, taxi to runway 22R via Alpha, Tango.", 303, 0));
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection("Request pushback.", 303, 0));
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection(null, 303, 0));
    }

    // An approval with no direction in it still must not invent one.
    [Fact]
    public void AnApprovalWithoutADirectionProducesNothing()
    {
        Assert.Null(SayIntentionsPushback.DescribeTurnDirection("Push and start approved.", 303, 0));
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
