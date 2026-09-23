// The departure-queue reading, the traffic intake and the sweep plan. PR #247 review R10 (the
// "departure queue" wording mixed a distance measured from the start of the current route segment
// with distances measured from the aircraft, and took the FARTHEST aircraft on the route as the
// head), R11/R12 (blind past ~610 m while queued; pushbacks, crossers, head-on and parked aircraft
// counted), R13 (progressive legs called "departure queue"), L7 (sweep radius / fast polling).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class QueueReadingTests
{
    // Route heading north (bearing 0); queued traffic points north too.
    private static GroundTrafficLogic.QueueCandidate Q(double aheadM, double lateralM = 5, double gs = 0,
        double direction = 0, double routeBearing = 0)
        => new(aheadM, lateralM, gs, direction, routeBearing);

    private static GroundTrafficLogic.QueueCandidate[] Line(params double[] aheadM)
        => aheadM.Select(a => Q(a)).ToArray();

    // ── the reading ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_intermediate_queue_is_just_the_queue_and_mentions_the_one_beyond()
    {
        // Pilot's line at 70/150/230 m, a gap, a second line at the runway hold (route end 905 m).
        var r = GroundTrafficLogic.ReadQueue(Line(70, 150, 230, 830, 900), 905, routeEndsAtTakeoffRunway: true);
        Assert.Equal(4, r.Position);
        Assert.False(r.AtRunwayHold);
        Assert.True(r.MoreBeyond);
        Assert.Equal("queue", r.Wording);
    }

    [Fact]
    public void Third_at_the_runway_hold_is_the_departure_queue()
    {
        var r = GroundTrafficLogic.ReadQueue(Line(80, 160), 170, true);
        Assert.Equal(3, r.Position);
        Assert.True(r.AtRunwayHold);
        Assert.False(r.MoreBeyond);
        Assert.Equal("departure queue", r.Wording);
    }

    [Fact]
    public void First_at_the_runway_hold_counts_from_the_aircraft_not_the_segment_start()
    {
        // The pilot stands 20 m short of the route end at the end of a 400 m final segment; the
        // caller passes the route end measured FROM THE AIRCRAFT.
        var r = GroundTrafficLogic.ReadQueue(Array.Empty<GroundTrafficLogic.QueueCandidate>(), 20, true);
        Assert.Equal(1, r.Position);
        Assert.True(r.AtRunwayHold);
        Assert.Equal("departure queue", r.Wording);
    }

    [Fact]
    public void A_progressive_leg_is_never_the_departure_queue()
        => Assert.Equal("queue", GroundTrafficLogic.ReadQueue(Line(80, 160), 170, false).Wording);

    [Fact]
    public void An_unknown_route_end_is_not_the_runway_hold()
        => Assert.False(GroundTrafficLogic.ReadQueue(Line(80), null, true).AtRunwayHold);

    [Fact]
    public void Nothing_beyond_is_mentioned_at_the_runway_hold()
    {
        var r = GroundTrafficLogic.ReadQueue(Line(80, 400), 90, true);
        Assert.True(r.AtRunwayHold);
        Assert.False(r.MoreBeyond);
    }

    // ── who counts ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(31, 100, 0, 0)]    // beside the route
    [InlineData(5, 9, 0, 0)]       // not really ahead
    [InlineData(5, 1501, 0, 0)]    // beyond the scan
    [InlineData(5, 100, 6.1, 0)]   // taxiing, not queued
    [InlineData(5, 100, 0, 90)]    // crossing the route / parked nose-in
    [InlineData(5, 100, 3, 180)]   // head-on, or a pushback moving tail-first
    [InlineData(5, 100, 0, 36)]    // just outside the alignment band
    public void Aircraft_that_are_not_queued_ahead_do_not_count(double lateral, double ahead, double gs, double direction)
        => Assert.False(GroundTrafficLogic.QualifiesForQueue(Q(ahead, lateral, gs, direction)));

    [Theory]
    [InlineData(0)]
    [InlineData(34)]
    [InlineData(-34)]
    public void Aircraft_pointing_along_the_route_count(double direction)
        => Assert.True(GroundTrafficLogic.QualifiesForQueue(Q(100, 5, 0, direction)));

    [Fact]
    public void An_unknown_route_distance_never_counts()
        => Assert.False(GroundTrafficLogic.QualifiesForQueue(Q(double.NaN)));

    [Fact]
    public void Alignment_wraps_through_north()
        => Assert.True(GroundTrafficLogic.QualifiesForQueue(Q(100, 5, 0, direction: 355, routeBearing: 10)));

    // ── intake ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, 500, 20, false, false, true)]     // proximity range
    [InlineData(true, 700, 20, false, false, false)]    // beyond 2,000 ft, nothing else asks
    [InlineData(true, 500, 80, false, false, false)]    // taking off / landing traffic
    [InlineData(true, 700, 20, false, true, true)]      // queue scan
    [InlineData(true, 1600, 20, false, true, false)]    // beyond the queue scan
    [InlineData(true, 700, 80, false, true, false)]     // queue scan still wants taxi speeds
    [InlineData(true, 4000, 140, true, false, true)]    // runway watch: anything on the ground to 5 km
    [InlineData(true, 6000, 0, true, false, false)]
    [InlineData(false, 10000, 140, true, false, true)]  // airborne, watching finals
    [InlineData(false, 20000, 140, true, false, false)]
    [InlineData(false, 1000, 140, false, true, false)]  // airborne never without the watch
    public void KeepInIntake(bool onGround, double distM, double gs, bool watch, bool queue, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.KeepInIntake(onGround, distM, gs, watch, queue));

    [Fact]
    public void The_sweep_radius_covers_what_the_intake_keeps_and_no_more()
    {
        Assert.Equal(16612u, GroundTrafficLogic.SweepRadiusMeters(true, false));
        Assert.Equal(2000u, GroundTrafficLogic.SweepRadiusMeters(false, true));
        Assert.Equal(1000u, GroundTrafficLogic.SweepRadiusMeters(false, false));
        Assert.True(GroundTrafficLogic.SweepRadiusMeters(true, true) >= GroundTrafficLogic.RunwayWatchAirRangeM);
        Assert.True(GroundTrafficLogic.SweepRadiusMeters(false, true) >= GroundTrafficLogic.QueueScanM);
        Assert.True(GroundTrafficLogic.SweepRadiusMeters(false, false)
                    >= GroundTrafficLogic.TrackRangeFt / GroundTrafficLogic.FeetPerMetre);
    }

    // ── fast polling ────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_runway_watch_always_polls_fast()
        => Assert.True(GroundTrafficLogic.NeedsFastPoll(true, false,
            Array.Empty<(double, double, double)>()));

    [Fact]
    public void Parked_aircraft_alone_never_force_fast_polling()
        => Assert.False(GroundTrafficLogic.NeedsFastPoll(false, false,
            new[] { (300.0, 0.0, 90.0), (800.0, 0.0, 10.0) }));

    [Fact]
    public void Moving_traffic_nearby_polls_fast()
    {
        Assert.True(GroundTrafficLogic.NeedsFastPoll(false, false, new[] { (1000.0, 5.0, 200.0) }));
        Assert.False(GroundTrafficLogic.NeedsFastPoll(false, false, new[] { (2000.0, 5.0, 200.0) }));
    }

    [Fact]
    public void A_queue_ahead_of_a_stopped_pilot_polls_fast()
    {
        Assert.True(GroundTrafficLogic.NeedsFastPoll(false, true, new[] { (400.0, 0.0, 10.0) }));
        Assert.False(GroundTrafficLogic.NeedsFastPoll(false, true, new[] { (700.0, 0.0, 10.0) }));
        Assert.False(GroundTrafficLogic.NeedsFastPoll(false, true, new[] { (400.0, 0.0, 60.0) }));
        Assert.False(GroundTrafficLogic.NeedsFastPoll(false, false, new[] { (400.0, 0.0, 10.0) }));
    }

    [Theory]
    [InlineData(400, 10, true)]
    [InlineData(400, 350, true)]
    [InlineData(400, 40, false)]
    [InlineData(700, 0, false)]
    public void IsInQueueCone(double distFt, double rel, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsInQueueCone(distFt, rel));
}
