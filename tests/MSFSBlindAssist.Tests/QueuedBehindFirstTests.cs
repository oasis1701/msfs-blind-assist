// Only the FIRST aircraft on the route ahead is called (GroundTrafficLogic.FirstOnRouteAheadM /
// IsQueuedBehindFirst / WithholdsZoneBehindFirst). PR #247 author's fix 3 (9190e869, his "shadowed"
// traffic): aircraft queued beyond the first on the route cannot be reached without passing it, so they
// get no on-route callout and no Awareness/Caution zone callout — a three-aircraft queue used to be
// announced as three "on your route" calls, then three "Slow down"s, each cutting off the last. "Stop"
// (Warning) is never withheld.
//
// Only traffic that OCCUPIES the route — stopped on it or moving along it — can be the first, and head-on
// traffic is never queued behind it (PR #247 integration review Q3): an aircraft merely crossing the route
// inside the 30 m band silenced the stopped aircraft beyond it, and delayed head-on traffic's "coming
// toward you" by about 15 s.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class QueuedBehindFirstTests
{
    private const RouteRelativeMotion Stopped = RouteRelativeMotion.Stopped;
    private const RouteRelativeMotion Along = RouteRelativeMotion.Along;
    private const RouteRelativeMotion Toward = RouteRelativeMotion.Toward;
    private const RouteRelativeMotion Crossing = RouteRelativeMotion.Crossing;

    [Fact]
    public void The_first_on_the_route_is_the_nearest_along_it()
        => Assert.Equal(150.0, GroundTrafficLogic.FirstOnRouteAheadM(new (bool, double, RouteRelativeMotion?)[]
        {
            (true, 220.0, Stopped), (true, 150.0, Stopped), (true, 290.0, Stopped),
        }));

    [Fact]
    public void Traffic_off_the_route_is_never_the_first()
        => Assert.Equal(220.0, GroundTrafficLogic.FirstOnRouteAheadM(new (bool, double, RouteRelativeMotion?)[]
        {
            (false, 40.0, Stopped), (true, 220.0, Stopped),
        }));

    [Fact]
    public void With_nothing_on_the_route_there_is_no_first()
    {
        Assert.Null(GroundTrafficLogic.FirstOnRouteAheadM(Array.Empty<(bool, double, RouteRelativeMotion?)>()));
        Assert.Null(GroundTrafficLogic.FirstOnRouteAheadM(new (bool, double, RouteRelativeMotion?)[]
        {
            (false, 40.0, Stopped), (false, double.NaN, null),
        }));
    }

    [Fact]
    public void A_crossing_aircraft_is_never_the_first()
        // P3: an aircraft crossing the route 100 m ahead, inside the 30 m band, does not hide the one
        // stopped on it 190 m ahead — it will be gone before the pilot gets there.
        => Assert.Equal(190.0, GroundTrafficLogic.FirstOnRouteAheadM(new (bool, double, RouteRelativeMotion?)[]
        {
            (true, 100.0, Crossing), (true, 190.0, Stopped),
        }));

    [Fact]
    public void Head_on_traffic_is_never_the_first()
        => Assert.Equal(190.0, GroundTrafficLogic.FirstOnRouteAheadM(new (bool, double, RouteRelativeMotion?)[]
        {
            (true, 100.0, Toward), (true, 190.0, Stopped),
        }));

    [Fact]
    public void Traffic_moving_along_the_route_can_be_the_first()
        => Assert.Equal(100.0, GroundTrafficLogic.FirstOnRouteAheadM(new (bool, double, RouteRelativeMotion?)[]
        {
            (true, 100.0, Along), (true, 190.0, Stopped),
        }));

    [Fact]
    public void Nothing_that_only_crosses_or_comes_head_on_makes_a_first()
        => Assert.Null(GroundTrafficLogic.FirstOnRouteAheadM(new (bool, double, RouteRelativeMotion?)[]
        {
            (true, 100.0, Crossing), (true, 300.0, Toward),
        }));

    [Theory]
    [InlineData(150.0, false)]   // the first itself
    [InlineData(160.0, false)]   // side by side with it (within 10 m): both first
    [InlineData(160.1, true)]    // queued behind it
    [InlineData(290.0, true)]
    public void Queued_behind_the_first_means_more_than_ten_metres_beyond_it(double aheadM, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM, firstAheadM: 150.0, Stopped));

    [Fact]
    public void Traffic_off_the_route_is_never_queued_behind_the_first()
        => Assert.False(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: false, aheadM: 400.0, firstAheadM: 150.0, Stopped));

    [Fact]
    public void Without_a_first_nothing_is_queued_behind_it()
        => Assert.False(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 400.0, firstAheadM: null, Stopped));

    [Fact]
    public void A_head_on_aircraft_behind_the_first_is_not_silenced()
        // P4's second half: coming toward the pilot, it keeps its own callouts beyond the first.
        => Assert.False(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 300.0, firstAheadM: 150.0, Toward));

    [Fact]
    public void Traffic_moving_along_or_crossing_beyond_the_first_is_still_queued_behind_it()
    {
        Assert.True(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 300.0, firstAheadM: 150.0, Along));
        Assert.True(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 300.0, firstAheadM: 150.0, Crossing));
    }

    [Fact]
    public void The_authors_queue_is_unchanged()
    {
        // His three-aircraft queue, all stopped on the route: the nearest is first, the other two are queued behind it.
        var queue = new (bool, double, RouteRelativeMotion?)[] { (true, 150.0, Stopped), (true, 220.0, Stopped), (true, 290.0, Stopped) };
        double? first = GroundTrafficLogic.FirstOnRouteAheadM(queue);
        Assert.Equal(150.0, first);
        Assert.False(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 150.0, first, Stopped));
        Assert.True(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 220.0, first, Stopped));
        Assert.True(GroundTrafficLogic.IsQueuedBehindFirst(onRouteAhead: true, aheadM: 290.0, first, Stopped));
    }

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
