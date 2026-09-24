// Only the FIRST aircraft on the route ahead is called (GroundTrafficLogic.FirstOnRouteAheadM /
// IsQueuedBehindFirst / WithholdsZoneBehindFirst). PR #247 author's fix 3 (9190e869, his "shadowed"
// traffic): aircraft queued beyond the first on the route cannot be reached without passing it, so they
// get no on-route callout and no Awareness/Caution zone callout — a three-aircraft queue used to be
// announced as three "on your route" calls, then three "Slow down"s, each cutting off the last. "Stop"
// (Warning) is never withheld.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class QueuedBehindFirstTests
{
    [Fact]
    public void The_first_on_the_route_is_the_nearest_along_it()
        => Assert.Equal(150.0, GroundTrafficLogic.FirstOnRouteAheadM(new[]
        {
            (true, 220.0), (true, 150.0), (true, 290.0),
        }));

    [Fact]
    public void Traffic_off_the_route_is_never_the_first()
        => Assert.Equal(220.0, GroundTrafficLogic.FirstOnRouteAheadM(new[]
        {
            (false, 40.0), (true, 220.0),
        }));

    [Fact]
    public void With_nothing_on_the_route_there_is_no_first()
    {
        Assert.Null(GroundTrafficLogic.FirstOnRouteAheadM(Array.Empty<(bool, double)>()));
        Assert.Null(GroundTrafficLogic.FirstOnRouteAheadM(new[] { (false, 40.0), (false, double.NaN) }));
    }

    [Theory]
    [InlineData(150.0, false)]   // the first itself
    [InlineData(160.0, false)]   // side by side with it (within 10 m): both first
    [InlineData(160.1, true)]    // queued behind it
    [InlineData(290.0, true)]
    public void Queued_behind_the_first_means_more_than_ten_metres_beyond_it(double aheadM, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM, firstAheadM: 150.0));

    [Fact]
    public void Traffic_off_the_route_is_never_queued_behind_the_first()
        => Assert.False(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: false, aheadM: 400.0, firstAheadM: 150.0));

    [Fact]
    public void Without_a_first_nothing_is_queued_behind_it()
        => Assert.False(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 400.0, firstAheadM: null));

    [Fact]
    public void Behind_the_first_only_Stop_speaks()
    {
        Assert.True(GroundTrafficLogic.WithholdsZoneBehindFirst(queuedBehindFirst: true, GroundZone.Awareness));
        Assert.True(GroundTrafficLogic.WithholdsZoneBehindFirst(queuedBehindFirst: true, GroundZone.Caution));
        Assert.False(GroundTrafficLogic.WithholdsZoneBehindFirst(queuedBehindFirst: true, GroundZone.Warning));
    }

    [Fact]
    public void The_first_aircraft_is_never_withheld()
    {
        Assert.False(GroundTrafficLogic.WithholdsZoneBehindFirst(queuedBehindFirst: false, GroundZone.Awareness));
        Assert.False(GroundTrafficLogic.WithholdsZoneBehindFirst(queuedBehindFirst: false, GroundZone.Caution));
        Assert.False(GroundTrafficLogic.WithholdsZoneBehindFirst(queuedBehindFirst: false, GroundZone.Warning));
    }
}
