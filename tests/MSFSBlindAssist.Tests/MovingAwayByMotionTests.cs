// "Moving away" judged from MOTION, beside the distance-rate test (GroundTrafficLogic.OpeningSpeedMps /
// IsLeadGrowing / IsOpeningByMotion). PR #247 author's fix 4 (9190e869): traffic pulling away ahead
// earned "Stop, … very close" because the distance-growth test needs two evaluations; opening is also
// judged from the relative velocity (about 2 kt) and, for traffic on the route ahead, from its growing
// lead ALONG the route — through a bend the straight-line gap is the wrong measure.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class MovingAwayByMotionTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private const double KtsToMps = 0.514444;

    // ── the relative velocity along the line of sight ──────────────────────────────────────

    [Fact]
    public void The_authors_scenario_opens_at_about_seven_metres_a_second()
    {
        // 95 m ahead (east), 20 kt against the pilot's 6 kt, both heading east.
        double opening = GroundTrafficLogic.OpeningSpeedMps(95, 0, (20 - 6) * KtsToMps, 0);
        Assert.Equal(7.2, opening, 1);
        Assert.True(GroundTrafficLogic.IsOpeningByMotion(trafficGsKts: 20, opening, leadGrowing: false));
    }

    [Fact]
    public void Closing_is_negative()
        => Assert.Equal(-5.0, GroundTrafficLogic.OpeningSpeedMps(0, 200, 0, -5), 6);

    [Fact]
    public void Crossing_the_line_of_sight_neither_opens_nor_closes()
        => Assert.Equal(0.0, GroundTrafficLogic.OpeningSpeedMps(100, 0, 0, 8), 6);

    [Fact]
    public void Only_the_component_along_the_line_of_sight_counts()
        // 3-4-5 geometry: offset (30, 40), relative velocity (3, 0) → 3 × 30 / 50.
        => Assert.Equal(1.8, GroundTrafficLogic.OpeningSpeedMps(30, 40, 3, 0), 6);

    [Fact]
    public void Within_a_metre_there_is_no_line_of_sight()
        => Assert.Equal(0.0, GroundTrafficLogic.OpeningSpeedMps(0.5, 0.5, 10, 10));

    // ── the lead along the route ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100.0, 101.0, true)]    // 1 m/s: the threshold
    [InlineData(100.0, 100.9, false)]
    [InlineData(100.0, 95.0, false)]    // closing along the route
    public void The_lead_grows_at_a_metre_a_second_or_more(double before, double after, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsLeadGrowing(before, T0, after, T0.AddSeconds(1)));

    [Fact]
    public void Without_a_previous_lead_it_is_not_growing()
    {
        Assert.False(GroundTrafficLogic.IsLeadGrowing(double.NaN, T0, 150, T0.AddSeconds(1)));
        Assert.False(GroundTrafficLogic.IsLeadGrowing(100, T0, double.NaN, T0.AddSeconds(1)));
    }

    [Theory]
    [InlineData(0.2)]    // too close together to be a rate
    [InlineData(10.0)]   // too far apart to still describe it
    public void A_gap_outside_a_fifth_of_a_second_to_ten_seconds_is_no_basis(double seconds)
        => Assert.False(GroundTrafficLogic.IsLeadGrowing(100, T0, 200, T0.AddSeconds(seconds)));

    // ── moving traffic only ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2.9, 5.0, true, false)]    // parked for this rule, whatever the numbers say
    [InlineData(3.0, 1.0, false, true)]    // opening at the threshold
    [InlineData(3.0, 0.9, false, false)]
    [InlineData(10.0, -2.0, true, true)]   // closing in a straight line, pulling away along the route
    [InlineData(10.0, -2.0, false, false)]
    public void Opening_by_motion_needs_moving_traffic(double trafficGs, double openingMps, bool leadGrowing, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsOpeningByMotion(trafficGs, openingMps, leadGrowing));
}
