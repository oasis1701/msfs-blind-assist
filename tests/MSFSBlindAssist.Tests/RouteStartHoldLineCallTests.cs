// The incursion guard's on-route hold-line call ("Crossing runway X." / "Passing the runway X hold line.")
// about a line the aircraft is taxiing AWAY from.
//
// KIAD, 2026-11-26: after landing on 01C the pilot stopped at 01C's hold line on the Y5 exit (an MSFS 2024
// navdata HSND node, 104 m off the centreline) and planned the route to gate B 71 via Y5, Y, B. The route
// started AT that hold node and ran away from the runway. 20 m down Y5, at 5.7 kt, the pilot heard
// "Crossing runway 01C at Y5." No runway was crossed. Two causes, each pinned here:
//   1. the loaded route's start node counts as an on-route hold (so a stand beside it never draws the
//      off-route warning), ignored only within 20 m of it, and an on-route node was never tested for being
//      ahead: once 20 m past it, it was spoken about;
//   2. RouteReachesRunwayAfter answered "Crossing" for any node that is no segment's END, and the start node
//      never is one.
// The same "left behind" judgement ends a repeat measured with VirtualPilot at LTFM: two hold nodes labelled
// "runway 17R at B9B" on the far side of a crossing, the second shadowed by the first and then spoken 6 m
// behind the aircraft once the 10 s cooldown ran out.
//
// Coordinates: the MSFS 2024 navdata (KIAD 01C/19C thresholds, the Y5 path) and the pilot's logged frames.
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the test to match the output.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Services.RunwayIncursionWatch;

namespace MSFSBlindAssist.Tests;

public class RouteStartHoldLineCallTests
{
    // Runway 01C/19C, 164 ft wide.
    private static readonly TaxiGraph.RunwayCenterline Rwy01C = new()
    {
        Name1 = "01C", Name2 = "19C",
        Lat1 = 38.939056, Lon1 = -77.459778, Lat2 = 38.970619, Lon2 = -77.459335,
        HeadingDeg1 = 0.8, HalfWidthMeters = 164 * 0.3048 / 2.0,
    };

    private static readonly IReadOnlyList<TaxiGraph.RunwayCenterline> Runways = new[] { Rwy01C };

    // Y5's hold line for 01C: node 1466 in the route the app built, the route's start.
    private const int HoldId = 1466;
    private static readonly TaxiNode Hold = Node(HoldId, 38.953819, -77.458366, TaxiNodeType.HoldShort, "runway 01C at Y5");

    private static TaxiNode Node(int id, double lat, double lon, TaxiNodeType type = TaxiNodeType.Normal, string? hold = null)
        => new() { NodeId = id, Latitude = lat, Longitude = lon, Type = type, HoldShortName = hold };

    private static List<TaxiRouteSegment> Route(params TaxiNode[] nodes)
    {
        var segments = new List<TaxiRouteSegment>();
        for (int i = 0; i + 1 < nodes.Length; i++)
            segments.Add(new TaxiRouteSegment
            {
                FromNode = nodes[i], ToNode = nodes[i + 1], TaxiwayName = "Y5",
                DistanceMeters = TaxiGraph.CalculateDistanceMeters(
                    nodes[i].Latitude, nodes[i].Longitude, nodes[i + 1].Latitude, nodes[i + 1].Longitude),
            });
        return segments;
    }

    // The route the pilot was given, as far as Y meets Y5: south-east down Y5, away from 01C.
    private static List<TaxiRouteSegment> AwayFromTheRunway() => Route(
        Hold,
        Node(1465, 38.953304, -77.457970),
        Node(1464, 38.952988, -77.457718),
        Node(1463, 38.952667, -77.457520),
        Node(1593, 38.952412, -77.457397),
        Node(1462, 38.952358, -77.457367),
        Node(1461, 38.951980, -77.457253));

    // The same hold line as the start of a route the other way: north-west up Y5 onto 01C's pavement.
    private static List<TaxiRouteSegment> OntoTheRunway() => Route(
        Hold,
        Node(2001, 38.954441, -77.458855),
        Node(2002, 38.954731, -77.459061),
        Node(2003, 38.954792, -77.459099),
        Node(2004, 38.955200, -77.459320),
        Node(2005, 38.955547, -77.459419));

    // What CheckRunwayIncursion measures for a node in range.
    private static (double DistanceM, double Bearing) From(double lat, double lon, TaxiNode node)
        => (TaxiGraph.FastDistanceMeters(lat, lon, node.Latitude, node.Longitude),
            NavigationCalculator.CalculateBearing(lat, lon, node.Latitude, node.Longitude));

    // --- 1. A route that starts at a runway hold line and taxis away: no "Crossing", no "Passing".

    [Fact]
    public void Where_it_was_spoken_the_start_hold_line_is_behind_the_aircraft()
    {
        // 18:31:11.354, the frame "Crossing runway 01C at Y5." was spoken in: heading 155.4 true, 5.7 kt.
        var (d, bearing) = From(38.9536673, -77.4582393, Hold);
        Assert.InRange(d, 20.0, 21.0);
        Assert.True(IsLeftBehind(d, bearing, headingTrueDeg: 155.4));
    }

    [Fact]
    public void A_planned_hold_line_left_behind_is_not_spoken_about()
    {
        // As CheckRunwayIncursion judges it: behind, called a line passed, and 01C nowhere ahead on the route.
        var (d, bearing) = From(38.9536673, -77.4582393, Hold);
        bool leftBehind = IsPlannedLineLeftBehind(
            IsLeftBehind(d, bearing, 155.4),
            TaxiGuidanceManager.RouteReachesRunwayAfter(AwayFromTheRunway(), 0, HoldId, Runways),
            IsGuardedRunwayStillAhead(38.9536673, -77.4582393, RunwayShape.For(Rwy01C), AwayFromTheRunway(), 0));
        Assert.True(leftBehind);
        var start = new HoldShortCandidate(HoldId, d, OnRoute: true, Approached: false, "runway 01C at Y5", leftBehind);
        Assert.Null(Pick(new[] { start }));
    }

    [Fact]
    public void The_start_hold_of_a_route_away_from_its_runway_is_passed_not_crossed()
        // Had the aircraft been loaded 20 to 40 m short of the line, the line is ahead and is spoken about:
        // as a line passed, for no runway follows it on the route.
        => Assert.False(TaxiGuidanceManager.RouteReachesRunwayAfter(AwayFromTheRunway(), 0, HoldId, Runways));

    [Fact]
    public void At_the_load_the_start_hold_line_was_just_ahead()
    {
        // 18:30:51, the route loaded: heading 166 true, the hold node 5.9 m ahead (the guard ignores the
        // loaded route's start node within ROUTE_START_HOLD_IGNORE_M, 20 m).
        var (d, bearing) = From(38.9538724, -77.4583657, Hold);
        Assert.InRange(d, 5.5, 6.5);
        Assert.False(IsLeftBehind(d, bearing, headingTrueDeg: 166.0));
    }

    // --- 2. A hold line ahead with a runway after it: the crossing is still announced.

    [Fact]
    public void The_start_hold_of_a_route_onto_its_runway_is_crossed()
        => Assert.True(TaxiGuidanceManager.RouteReachesRunwayAfter(OntoTheRunway(), 0, HoldId, Runways));

    [Fact]
    public void A_hold_line_ahead_on_the_route_is_crossed_when_the_runway_follows()
    {
        // Short of the line on the taxiway side, coming up Y5 toward 01C: the line is a segment's end.
        var segments = Route(Node(1465, 38.953304, -77.457970), Hold,
            Node(2001, 38.954441, -77.458855), Node(2002, 38.954731, -77.459061),
            Node(2003, 38.954792, -77.459099), Node(2004, 38.955200, -77.459320));
        Assert.True(TaxiGuidanceManager.RouteReachesRunwayAfter(segments, 0, HoldId, Runways));

        // 30 m short of it, heading for it (330 true): ahead, and picked.
        var (d, bearing) = From(38.953587, -77.458184, Hold);
        Assert.False(IsLeftBehind(d, bearing, headingTrueDeg: 330.0));
        var pick = Pick(new[]
        {
            new HoldShortCandidate(HoldId, d, OnRoute: true, Approached: false, "runway 01C at Y5",
                LeftBehind: IsLeftBehind(d, bearing, 330.0)),
        });
        Assert.Equal(HoldId, pick!.Value.NodeId);
    }

    [Fact]
    public void A_line_behind_with_its_runway_still_ahead_is_still_a_crossing()
    {
        // KFLL 28R at A6 (VirtualPilot, fs2024): the hold capture stopped the aircraft beside the node, just
        // past the line, and "Continue" sent it on toward 28R 40 m ahead. Past the beam is not enough: while
        // the route still runs onto the runway the line guards, the crossing is ahead and is announced.
        // Here: 10 m up the onto-the-runway route from the line, heading 330, the line behind.
        var shape = RunwayShape.For(Rwy01C);
        var (d, bearing) = From(38.953897, -77.458427, Hold);
        Assert.True(IsLeftBehind(d, bearing, headingTrueDeg: 330.0));
        Assert.True(IsGuardedRunwayStillAhead(38.953897, -77.458427, shape, OntoTheRunway(), currentSegmentIndex: 0));
    }

    [Fact]
    public void A_line_behind_with_its_runway_never_ahead_is_not()
    {
        var shape = RunwayShape.For(Rwy01C);
        Assert.False(IsGuardedRunwayStillAhead(38.9536673, -77.4582393, shape, AwayFromTheRunway(), currentSegmentIndex: 0));
    }

    [Fact]
    public void On_the_runway_the_line_guards_its_crossing_is_under_way()
        // On 01C's pavement (on the onto-the-runway route's last node): the crossing is happening now.
        => Assert.True(IsGuardedRunwayStillAhead(38.955547, -77.459419, RunwayShape.For(Rwy01C), AwayFromTheRunway(), currentSegmentIndex: 5));

    [Fact]
    public void A_line_that_guards_no_runway_is_judged_against_every_runway()
    {
        // OEJN H1 (VirtualPilot, fs2024): a set-back line 155 m off 34C's centreline, beyond the 150 m a hold
        // node is tied to its runway within, so it guards none. The route started held at it and crossed 34C:
        // any runway ahead is its crossing, as RouteReachesRunwayAfter already reads it.
        var every = new[] { RunwayShape.For(Rwy01C) };
        Assert.True(IsGuardedRunwayStillAhead(38.953897, -77.458427, guardedRunway: null, OntoTheRunway(), 0, every));
        Assert.False(IsGuardedRunwayStillAhead(38.953897, -77.458427, guardedRunway: null, AwayFromTheRunway(), 0, every));
        Assert.False(IsGuardedRunwayStillAhead(38.953897, -77.458427, guardedRunway: null, OntoTheRunway(), 0));
    }

    // --- Passed by the route itself, whichever way the nose points. KATL 26L, D C B D (VirtualPilot, fs2024):
    // swinging round for the second crossing, the nose came back past a "runway 08R at D" line 40 m off on the
    // loop already flown, and the pilot heard "Passing the runway 08R at D hold line." while crossing 08R.

    [Fact]
    public void A_line_is_passed_once_its_last_place_on_the_route_is_behind_the_search_window()
    {
        var route = AwayFromTheRunway();          // the start node, then 1465 .. 1461 as segments 0 .. 5
        Assert.False(TaxiGuidanceManager.IsPassedOnRoute(route, 0, HoldId));
        Assert.False(TaxiGuidanceManager.IsPassedOnRoute(route, 3, HoldId));
        Assert.True(TaxiGuidanceManager.IsPassedOnRoute(route, 4, HoldId));   // the start node, flying segment 4
        Assert.False(TaxiGuidanceManager.IsPassedOnRoute(route, 4, 1464));    // the end of segment 1: in the window
        Assert.True(TaxiGuidanceManager.IsPassedOnRoute(route, 5, 1464));
    }

    [Fact]
    public void A_line_the_route_comes_back_through_is_not_passed()
    {
        // Out over the line and back through it: its LAST place on the route decides.
        var a = Node(1465, 38.953304, -77.457970);
        var b = Node(1464, 38.952988, -77.457718);
        var route = Route(Hold, a, b, a, Hold, Node(2001, 38.954441, -77.458855));
        Assert.False(TaxiGuidanceManager.IsPassedOnRoute(route, 5, HoldId));
        Assert.False(TaxiGuidanceManager.IsPassedOnRoute(route, 4, 9999));   // not on the route: never "passed"
    }

    // --- What the guard decides from the three: a planned line is done with when it is behind the aircraft and
    // is not a crossing still under way.

    [Theory]
    [InlineData(false, true, true, false)]   // ahead: spoken about as ever
    [InlineData(true, false, false, true)]   // KIAD 01C at Y5: the start hold, the route away from the runway
    [InlineData(true, true, true, false)]    // KFLL 28R at A6: stopped beside the line, 28R just ahead
    [InlineData(true, false, true, true)]    // UUWW 24 at A5: "Passing", the runway 180 m on: never said of a line behind
    [InlineData(true, true, false, true)]    // LTFM 17R at B9B: 17L follows, 17R is behind too
    public void A_planned_line_is_done_with_once_behind_unless_its_crossing_is_under_way(
        bool behind, bool calledACrossing, bool guardedRunwayStillAhead, bool doneWith)
        => Assert.Equal(doneWith, IsPlannedLineLeftBehind(behind, calledACrossing, guardedRunwayStillAhead));

    [Fact]
    public void Another_runway_ahead_is_not_this_lines_crossing()
    {
        // LTFM B9B (VirtualPilot): past the far-side line of 17R the route runs on to 17L. That runway has its
        // own line; the passed 17R line is not spoken about again.
        var other = RunwayShape.For(new TaxiGraph.RunwayCenterline
        {
            Name1 = "01R", Name2 = "19L",
            Lat1 = 38.923771, Lon1 = -77.436424, Lat2 = 38.955288, Lon2 = -77.435997,
            HeadingDeg1 = 0.8, HalfWidthMeters = 25.0,
        });
        Assert.False(IsGuardedRunwayStillAhead(38.953897, -77.458427, other, OntoTheRunway(), currentSegmentIndex: 0));
    }

    [Fact]
    public void A_node_the_route_does_not_pass_keeps_the_old_wording()
        => Assert.True(TaxiGuidanceManager.RouteReachesRunwayAfter(AwayFromTheRunway(), 0, 9999, Runways));

    [Fact]
    public void A_line_on_the_route_behind_the_search_window_is_still_judged_by_the_route()
    {
        // The search starts three segments behind the one being flown (ON_ROUTE_TRACKING_SEGMENTS_BACK) so that
        // the occurrence ahead wins on a route through a node twice; a node found only before that is still a
        // node on the route, never "not found". KMCO 17R exit H7 (VirtualPilot, fs2024): the route ran out
        // through one "runway 35L at H7" node and back through another, the cursor was past both while the
        // pilot turned round, and the first came up ahead as "Crossing runway 35L at H7." with no runway on
        // the route anywhere near.
        Assert.False(TaxiGuidanceManager.RouteReachesRunwayAfter(AwayFromTheRunway(), 5, HoldId, Runways));
        // The line as segment 0's END, flying segment 5: found before the window, nothing after it reaches 01C.
        var lineBeforeTheWindow = Route(Node(1465, 38.953304, -77.457970), Hold,
            Node(1464, 38.952988, -77.457718), Node(1463, 38.952667, -77.457520),
            Node(1593, 38.952412, -77.457397), Node(1462, 38.952358, -77.457367), Node(1461, 38.951980, -77.457253));
        Assert.False(TaxiGuidanceManager.RouteReachesRunwayAfter(lineBeforeTheWindow, 5, HoldId, Runways));
    }

    // --- 3. A hold line passed on the way and then left behind: no repeat.

    [Fact]
    public void A_hold_line_passed_on_the_way_is_not_spoken_about_again()
    {
        // LTFM, VirtualPilot (fs2024): far side of the 17R crossing on B9B, two hold nodes labelled
        // "runway 17R at B9B". The nearer was spoken about and latched; the second, shadowed by it and then
        // by the 10 s cooldown, came up again 6 m behind the aircraft with nothing latched on it.
        var passed = new HoldShortCandidate(5783, 6.0, OnRoute: true, Approached: false, "runway 17R at B9B", LeftBehind: true);
        Assert.Null(Pick(new[] { passed }));
    }

    [Fact]
    public void A_hold_line_left_behind_never_shadows_one_ahead()
    {
        // Nearer, but passed: the next line ahead on the route is the one spoken about.
        var pick = Pick(new[]
        {
            new HoldShortCandidate(1, 21.0, OnRoute: true, Approached: false, LeftBehind: true),
            new HoldShortCandidate(2, 35.0, OnRoute: true, Approached: false),
        });
        Assert.Equal(2, pick!.Value.NodeId);
    }

    [Fact]
    public void A_wrong_turn_toward_a_line_off_the_route_is_still_warned_past_a_line_left_behind()
    {
        var pick = Pick(new[]
        {
            new HoldShortCandidate(1, 15.0, OnRoute: true, Approached: false, LeftBehind: true),
            new HoldShortCandidate(3, 30.0, OnRoute: false, Approached: true),
        });
        Assert.Equal(3, pick!.Value.NodeId);
    }
}
