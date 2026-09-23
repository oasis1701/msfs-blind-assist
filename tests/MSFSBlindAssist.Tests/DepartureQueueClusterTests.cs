// Tests for GroundTrafficLogic.QueueAheadOf — "number N in the departure queue".
//
// The scan window (1,500 m of route ahead) says how far to LOOK. It does not say where one
// queue ends and the next begins, and at a busy field that much route in front of a stopped
// aircraft can easily hold two: the line just joined at an intermediate holding point, and a
// second line at the runway hold several hundred metres beyond it. Counting everything in
// the window merges them and tells the pilot they are seventh when they are third.
//
// So the count walks outward from the pilot and stops at the first gap wider than
// QueueLinkMaxGapM. The queue being joined is whichever contiguous line the pilot is in —
// and a queue at a holding point well back from the runway is still theirs, and may be the
// longer of the two.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DepartureQueueClusterTests
{
    [Fact]
    public void AnEmptyRouteAheadLeavesThePilotFirst()
    {
        Assert.Equal(0, GroundTrafficLogic.QueueAheadOf(new double[0]).Count);
    }

    [Fact]
    public void AContiguousLineIsCountedWhole()
    {
        // Five aircraft at ordinary holding spacing.
        Assert.Equal(5, GroundTrafficLogic.QueueAheadOf(
            new double[] { 80, 160, 240, 330, 410 }).Count);
    }

    [Fact]
    public void TwoSeparateQueuesAreNotMerged()
    {
        // Three aircraft in the line the pilot just joined at a holding point, then a
        // 600 m gap, then four more holding at the runway. The pilot is fourth in THEIR
        // queue, not eighth overall.
        var aheadM = new double[] { 70, 150, 230, 830, 900, 975, 1050 };

        Assert.Equal(3, GroundTrafficLogic.QueueAheadOf(aheadM).Count);
    }

    [Fact]
    public void AQueueFurtherBackFromTheRunwayIsStillThePilotsQueue()
    {
        // The intermediate holding point holds the LONGER line. Joining it must report
        // that line, not the shorter one at the runway hold beyond the gap.
        var aheadM = new double[] { 60, 140, 215, 300, 380, 1100, 1180 };

        Assert.Equal(5, GroundTrafficLogic.QueueAheadOf(aheadM).Count);
    }

    [Fact]
    public void AnAircraftBeyondTheJoiningGapDoesNotCount()
    {
        // Nothing within QueueLinkMaxGapM of the pilot: they are not in a queue yet, even
        // though a queue is visible further on.
        var aheadM = new double[] { 400, 470, 545 };

        Assert.Equal(0, GroundTrafficLogic.QueueAheadOf(aheadM).Count);
    }

    [Fact]
    public void TheTailOfAQueueIsJoinedAsSoonAsItIsWithinTheGap()
    {
        var justInside = new double[] { GroundTrafficLogic.QueueLinkMaxGapM - 1, 320, 400 };
        Assert.Equal(3, GroundTrafficLogic.QueueAheadOf(justInside).Count);

        var justOutside = new double[] { GroundTrafficLogic.QueueLinkMaxGapM + 1, 320, 400 };
        Assert.Equal(0, GroundTrafficLogic.QueueAheadOf(justOutside).Count);
    }

    [Fact]
    public void InputOrderDoesNotMatter()
    {
        var shuffled = new double[] { 230, 70, 900, 150, 830 };

        Assert.Equal(3, GroundTrafficLogic.QueueAheadOf(shuffled).Count);
    }

    [Fact]
    public void NegativeDistancesAreIgnored()
    {
        // Defensive: a projection behind the pilot is not part of the queue ahead.
        Assert.Equal(2, GroundTrafficLogic.QueueAheadOf(new double[] { -50, 80, 160 }).Count);
    }

    // ── "More traffic holding further ahead." ────────────────────────────

    [Fact]
    public void ASecondGroupBeyondTheGapIsReported()
    {
        var c = GroundTrafficLogic.QueueAheadOf(new double[] { 70, 150, 230, 830, 900 });

        Assert.Equal(3, c.Count);
        Assert.True(c.MoreBeyond);
    }

    [Fact]
    public void NothingBeyondTheQueueIsNotReported()
    {
        var c = GroundTrafficLogic.QueueAheadOf(new double[] { 70, 150, 230 });

        Assert.Equal(3, c.Count);
        Assert.False(c.MoreBeyond);
    }

    [Fact]
    public void AQueueThePilotHasNotJoinedStillCountsAsTrafficBeyond()
    {
        // Nothing within the joining gap, but a queue is visible further on: the pilot is
        // first in their own (empty) queue and there IS traffic holding ahead of them.
        var c = GroundTrafficLogic.QueueAheadOf(new double[] { 400, 470 });

        Assert.Equal(0, c.Count);
        Assert.True(c.MoreBeyond);
    }

    [Fact]
    public void AnEmptyRouteAheadHasNothingBeyondEither()
    {
        var c = GroundTrafficLogic.QueueAheadOf(new double[0]);

        Assert.Equal(0, c.Count);
        Assert.False(c.MoreBeyond);
    }

    [Fact]
    public void TheClusterReportsItsHead()
    {
        var c = GroundTrafficLogic.QueueAheadOf(new double[] { 70, 150, 230, 830, 900 });
        Assert.Equal(230.0, c.HeadAheadM);
        Assert.Equal(0.0, GroundTrafficLogic.QueueAheadOf(new double[0]).HeadAheadM);
    }
}
