using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>What became of one Turn pick. Public so a public test theory can take it.</summary>
public enum TurnPickOutcome { AlreadyMet, Charted, OffChart, NoTurnFound, DoesNotLead, LongDetour, MissesHoldingPoint }

/// <summary>A pick the route could not honor; the charted turn is kept.</summary>
internal sealed record TurnPickRefusal(int RowIndex, TurnSide Side, string TaxiwayName, TurnPickOutcome Reason);

/// <summary>The route after the picks, its (possibly re-resolved) destination, and what each pick did.</summary>
internal sealed record TurnPickResult(TaxiRoute Route, int DestinationNodeId,
    IReadOnlyList<AppliedTurnPick> Applied, IReadOnlyList<TurnPickRefusal> Refusals);

/// <summary>Everything a pick needs besides the route. Read only; nothing here is changed.</summary>
internal sealed record TurnPickContext(
    TaxiGraph Graph, PavementMap Pavement,
    IReadOnlyList<string> Sequence,
    int DestinationNodeId, bool DestinationIsRunway,
    double AircraftLat, double AircraftLon, double AircraftHeadingTrue,
    ProgressiveTerminator? Terminator,
    int PinnedNodeId,
    double MaxLengthRatio, double MaxLengthPadMeters,
    // True when the aircraft is off the destination's network: the route's start node is the one
    // LoadRoute's first-leg check (RouteReachability.CheckFirstLeg) passed, so a first-row pick
    // works from it and never re-starts the route at another node.
    bool KeepRouteStart = false,
    Action<string>? Log = null);

/// <summary>
/// Applies the planner's Turn picks to a route already built the ordinary way: a pick the route
/// already makes changes nothing; otherwise a charted turn that way, else a short off-chart leg,
/// with the rest re-routed with the junction blocked so it cannot come back through it. A pick
/// that cannot be honored leaves the route as it was and is returned as a refusal. Pure: the graph
/// is never changed.
/// </summary>
internal static class TurnPickApplier
{
    public const double ChartedSearchMeters = 150.0, ConnectorWalkBackMeters = 200.0;

    /// One way to make the turn: A to u (Connection, A first and u last; two nodes for an off-chart
    /// leg, which has no graph edge), then the edge u to v.
    private sealed record Option(List<int> Connection, int NextNodeId, bool OffChart, double OffChartLength)
    {
        public int DepartureNodeId => Connection[0];
        public int JoinNodeId => Connection[^1];
    }

    private enum Failure { None, Remainder, Pinned, Length, Other }

    /// The charted route's longest run along a runway, by end node (TURN-7): the same for every option
    /// of one pick bar a Progressive end re-resolved per option, so it is measured once per end.
    private sealed class ChartedRunwayRun(TaxiGraph graph, TaxiRoute input)
    {
        private readonly Dictionary<int, double> _byEnd = new();

        public double For(int end)
        {
            if (!_byEnd.TryGetValue(end, out double m))
                _byEnd[end] = m = LongestRunAlongARunwayMeters(graph, input, end);
            return m;
        }
    }

    /// OtherArm: the nodes of the row's taxiway that lie on from the join the way the pick did NOT turn
    /// (see <see cref="OtherArm"/>); closed for every later pick too.
    private sealed record Completed(TaxiRoute Route, int DestinationNodeId, AppliedTurnPick Pick, double ConnectionMeters,
        HashSet<int> OtherArm);

    /// Applies picks in ascending row order. The returned Route is the input route object when no pick
    /// changed it. Route.TurnPicks is set to Applied.
    public static TurnPickResult Apply(TaxiRoute route, IReadOnlyDictionary<int, TurnSide> picks, TurnPickContext ctx)
    {
        var applied = new List<AppliedTurnPick>();
        var refusals = new List<TurnPickRefusal>();
        var blocked = new HashSet<int>();
        bool inputHasPin = ctx.PinnedNodeId != 0 && NodeIds(route).Contains(ctx.PinnedNodeId);
        var current = route;
        int destination = ctx.DestinationNodeId;

        foreach (var (row, side) in picks.OrderBy(p => p.Key))
        {
            bool inSequence = row >= 0 && row < ctx.Sequence.Count;
            string name = inSequence ? ctx.Sequence[row] : "";
            (TurnPickOutcome outcome, Completed? done) = (TurnPickOutcome.NoTurnFound, null);
            // One pick never takes the whole route down: ApplyOne only reads current and blocked, so a
            // pick that throws leaves both as they were and the later picks still run.
            try
            {
                if (inSequence) (outcome, done) = ApplyOne(current, route, inputHasPin, row, side, ctx, blocked);
            }
            catch (Exception ex)
            {
                (outcome, done) = (TurnPickOutcome.NoTurnFound, null);
                Log(ctx, row, side, name, outcome, $"failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (done == null)
            {
                refusals.Add(new TurnPickRefusal(row, side, name, outcome));
                continue;
            }
            applied.Add(done.Pick);
            blocked.UnionWith(done.Pick.BlockedNodeIds);
            // A later pick does not come back onto the arm this one turned away from either. Closed
            // here, for this load, and not in the pick's BlockedNodeIds: a recalculation keeps only the
            // junction closed (TURN-3), because a pick counts as made a little before its junction, and
            // a pilot who then took the other arm after all must still be routed along it.
            foreach (int id in done.OtherArm)
                if (id != ctx.DestinationNodeId) blocked.Add(id);
            if (done.Pick.ChangedRoute)
            {
                current = done.Route;
                destination = done.DestinationNodeId;
            }
        }

        current.TurnPicks = applied;
        return new TurnPickResult(current, destination, applied, refusals);
    }

    /// The join for one row in a built route: A, the heading arriving at A (null when no segment arrives),
    /// the index of the first segment after A, and the index of the first segment named for the row.
    /// Null when the route has no segment named for the row at or after the previous row's run.
    internal static (int DepartureNodeId, double? IncomingBearing, int FirstSegmentAfterDeparture,
        int FirstTaxiwaySegment)? LocateJoin(TaxiRoute route, IReadOnlyList<string> sequence, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= sequence.Count) return null;
        var segs = route.Segments;
        int cursor = 0, first = -1;
        for (int r = 0; r <= rowIndex; r++)
        {
            int at = -1;
            for (int i = cursor; i < segs.Count; i++)
                if (Named(segs[i].TaxiwayName, sequence[r])) { at = i; break; }
            if (at < 0)
            {
                if (r == rowIndex) return null;
                continue;   // an earlier row the route never names: search on from where we are
            }
            if (r == rowIndex) { first = at; break; }
            cursor = at;
            while (cursor < segs.Count && Named(segs[cursor].TaxiwayName, sequence[r])) cursor++;
        }

        // Back over unnamed connector segments (they belong to neither taxiway), at most 200 m; never
        // over an apron crossing or an earlier pick's off-chart leg.
        int k = first;
        double walked = 0;
        while (k > 0)
        {
            var prev = segs[k - 1];
            if (prev.TaxiwayName != "" || prev.IsUncharted || prev.IsOffChartTurn) break;
            if (walked + prev.DistanceMeters > ConnectorWalkBackMeters) break;
            walked += prev.DistanceMeters;
            k--;
        }
        double? incoming = k > 0 ? segs[k - 1].BearingDegrees : null;
        return (segs[k].FromNode.NodeId, incoming, k, first);
    }

    private static (TurnPickOutcome, Completed?) ApplyOne(TaxiRoute current, TaxiRoute input, bool inputHasPin,
        int row, TurnSide side, TurnPickContext ctx, HashSet<int> blocked)
    {
        string name = ctx.Sequence[row];
        var join = LocateJoin(current, ctx.Sequence, row);
        if (join == null)
        {
            Log(ctx, row, side, name, TurnPickOutcome.NoTurnFound, "the route never joins the taxiway");
            return (TurnPickOutcome.NoTurnFound, null);
        }
        var (departure, incomingOrNull, afterDeparture, firstTaxiway) = join.Value;
        double incoming = incomingOrNull ?? ctx.AircraftHeadingTrue;

        // Step 1: is it already so.
        double joinAt = 0;
        for (int i = afterDeparture; i < firstTaxiway; i++) joinAt += current.Segments[i].DistanceMeters;
        if (TurnShape.IsCleanTurn(incoming, LegsThroughRow(current, afterDeparture, firstTaxiway, name), joinAt, side))
        {
            int u = current.Segments[firstTaxiway].FromNode.NodeId;
            Log(ctx, row, side, name, TurnPickOutcome.AlreadyMet, $"departure {departure}, join {u}");
            return (TurnPickOutcome.AlreadyMet, new Completed(current, ctx.DestinationNodeId, new AppliedTurnPick
            {
                RowIndex = row, Side = side, TaxiwayName = name,
                DepartureNodeId = departure, JoinNodeId = u,
                FirstSegmentAfterDeparture = afterDeparture, FirstTaxiwaySegment = firstTaxiway,
                ChangedRoute = false, IsOffChart = false,
                BlockedNodeIds = new[] { departure, u }.Distinct().ToArray(),
            }, joinAt, OtherArm(ctx.Graph, u, current.Segments[firstTaxiway].ToNode.NodeId, name)));
        }

        // Steps 2 and 3 work from A and the route before it; a first row with nothing arriving at A
        // starts from the node nearest the aircraft, with an empty prefix. Off the destination's
        // network it starts from the route's own start instead: that is the node the unmapped
        // first leg was checked from (RTE-26), and another one would skip that check.
        int a;
        var prefix = new List<int>();
        if (incomingOrNull == null && ctx.KeepRouteStart)
        {
            a = departure;
        }
        else if (incomingOrNull == null)
        {
            int? component = ctx.Graph.Nodes.TryGetValue(ctx.DestinationNodeId, out var dest) ? dest.ComponentId : null;
            var near = ctx.Graph.FindNearestNode(ctx.AircraftLat, ctx.AircraftLon,
                requiredComponentId: component, excludeBridgeOnlyStandStubs: true);
            if (near == null)
            {
                Log(ctx, row, side, name, TurnPickOutcome.NoTurnFound, "no node near the aircraft");
                return (TurnPickOutcome.NoTurnFound, null);
            }
            a = near.NodeId;
        }
        else
        {
            a = departure;
            for (int i = 0; i < afterDeparture; i++) prefix.Add(current.Segments[i].FromNode.NodeId);
        }
        // An earlier pick's off-chart leg in the prefix has no graph edge: it is carried as an off-chart
        // pair, read from the route's own segments.
        var prefixOffChart = new List<(int From, int To)>();
        for (int i = 0; i < prefix.Count; i++)
            if (current.Segments[i].IsOffChartTurn)
                prefixOffChart.Add((current.Segments[i].FromNode.NodeId, current.Segments[i].ToNode.NodeId));

        var failures = new List<Failure>();
        var chartedRun = new ChartedRunwayRun(ctx.Graph, input);

        // The charted way this pick declines: the route's own nodes up to and including where it joins
        // the row's taxiway. An option may start along it (A and its connection are its own), but what
        // follows the turn never goes back over it.
        var chartedBeforeJoin = new List<int>();
        for (int i = 0; i <= firstTaxiway; i++) chartedBeforeJoin.Add(current.Segments[i].FromNode.NodeId);

        // Step 2: a charted turn; the completed option with the shortest whole route wins.
        Completed? best = null;
        foreach (var option in ChartedOptions(ctx, a, incoming, name, side, blocked))
        {
            var (done, failure) = Complete(option, prefix, prefixOffChart, row, side, ctx, input, inputHasPin, blocked, incoming, chartedBeforeJoin, chartedRun);
            if (done == null) { failures.Add(failure); continue; }
            if (best == null || done.Route.TotalDistanceMeters < best.Route.TotalDistanceMeters - 0.01 ||
                (Math.Abs(done.Route.TotalDistanceMeters - best.Route.TotalDistanceMeters) <= 0.01 &&
                 done.ConnectionMeters < best.ConnectionMeters))
                best = done;
        }
        // Step 2 can arrive at the very route step 1 looked at (another join node of the same turn):
        // that route already makes the turn.
        if (best != null && NodeIds(best.Route).SequenceEqual(NodeIds(current)))
        {
            Log(ctx, row, side, name, TurnPickOutcome.AlreadyMet,
                $"departure {best.Pick.DepartureNodeId}, join {best.Pick.JoinNodeId}, the charted option is the route");
            return (TurnPickOutcome.AlreadyMet, new Completed(current, ctx.DestinationNodeId, new AppliedTurnPick
            {
                RowIndex = row, Side = side, TaxiwayName = name,
                DepartureNodeId = best.Pick.DepartureNodeId, JoinNodeId = best.Pick.JoinNodeId,
                FirstSegmentAfterDeparture = best.Pick.FirstSegmentAfterDeparture,
                FirstTaxiwaySegment = best.Pick.FirstTaxiwaySegment,
                ChangedRoute = false, IsOffChart = false,
                BlockedNodeIds = best.Pick.BlockedNodeIds,
            }, best.ConnectionMeters, best.OtherArm));
        }
        if (best != null)
        {
            Log(ctx, row, side, name, TurnPickOutcome.Charted,
                $"departure {a}, join {best.Pick.JoinNodeId}, {best.Route.TotalDistanceMeters:F0} m");
            return (TurnPickOutcome.Charted, best);
        }

        // Step 3: an off-chart leg, candidates best first; the first that completes wins. Find's own
        // refusals feed the log line below, so the legs are judged once.
        var legRefusals = ctx.Log != null ? new List<OffChartLeg.Refusal>() : null;
        foreach (var c in OffChartLeg.Find(ctx.Graph, ctx.Pavement, a, incoming, name, side, blocked, legRefusals))
        {
            if (blocked.Contains(c.NextNodeId)) continue;   // as the charted path skips a blocked v
            var option = new Option(new List<int> { a, c.JoinNodeId }, c.NextNodeId, OffChart: true, c.LengthMeters);
            var (done, failure) = Complete(option, prefix, prefixOffChart, row, side, ctx, input, inputHasPin, blocked, incoming, chartedBeforeJoin, chartedRun);
            if (done == null) { failures.Add(failure); continue; }
            Log(ctx, row, side, name, TurnPickOutcome.OffChart,
                $"departure {a}, join {c.JoinNodeId}, leg {c.LengthMeters:F1} m, {done.Route.TotalDistanceMeters:F0} m");
            return (TurnPickOutcome.OffChart, done);
        }

        // A turn that exists but would miss the picked holding point is said as that, never as no turn.
        var reason = failures.Contains(Failure.Length) ? TurnPickOutcome.LongDetour
            : failures.Contains(Failure.Remainder) ? TurnPickOutcome.DoesNotLead
            : failures.Contains(Failure.Pinned) ? TurnPickOutcome.MissesHoldingPoint
            : TurnPickOutcome.NoTurnFound;
        if (ctx.Log != null)
            Log(ctx, row, side, name, reason,
                $"departure {a}, options failed: {Counts(failures.Select(f => f.ToString()))}; " +
                $"off-chart legs: {Counts(legRefusals!.Select(r => r.ToString()))}");
        return (reason, null);
    }

    /// (bearing, length) of the route's segments from fromSegment on, through the join and along the
    /// row's own taxiway: the list ends where the route leaves that taxiway. A turn the route makes onto
    /// the NEXT taxiway is that row's turn, never this one's, however soon it comes.
    private static List<(double Bearing, double LengthMeters)> LegsThroughRow(TaxiRoute route, int fromSegment,
        int firstTaxiwaySegment, string name)
    {
        var legs = new List<(double, double)>();
        for (int i = fromSegment; i < route.Segments.Count; i++)
        {
            if (i > firstTaxiwaySegment && !Named(route.Segments[i].TaxiwayName, name)) break;
            legs.Add((route.Segments[i].BearingDegrees, route.Segments[i].DistanceMeters));
        }
        return legs;
    }

    /// Does the route AS BUILT make the picked turn? Step 2 judges the taxiway's straightest run past
    /// the join and an off-chart leg its first edge, but the remainder is routed freely and at a fork
    /// can turn the picked way and then double back (LTFM "right onto N1": 65 degrees right, then 153
    /// degrees the other way 55 m on). A charted turn is judged from the heading arriving at A; an
    /// off-chart leg has passed its own gates, so only its turn onto the taxiway and what follows is.
    /// Nor may it double back within 80 m of the join on whatever comes next: a reversal onto ANOTHER
    /// taxiway there (more than 120 degrees in one change) undoes the turn just as surely.
    private static bool BuiltRouteMakesTheTurn(TaxiRoute stitched, int afterDeparture, int firstTaxiway,
        string name, TurnSide side, double incoming, bool offChart)
    {
        bool clean;
        if (offChart)
            clean = TurnShape.IsCleanTurn(stitched.Segments[firstTaxiway - 1].BearingDegrees,
                LegsThroughRow(stitched, firstTaxiway, firstTaxiway, name), 0, side);
        else
        {
            double joinAt = 0;
            for (int i = afterDeparture; i < firstTaxiway; i++) joinAt += stitched.Segments[i].DistanceMeters;
            clean = TurnShape.IsCleanTurn(incoming, LegsThroughRow(stitched, afterDeparture, firstTaxiway, name), joinAt, side);
        }
        if (!clean) return false;

        double walked = stitched.Segments[firstTaxiway].DistanceMeters;
        for (int i = firstTaxiway + 1; i < stitched.Segments.Count && walked < TurnShape.WalkPastJoinMeters; i++)
        {
            double change = Normalize(stitched.Segments[i].BearingDegrees - stitched.Segments[i - 1].BearingDegrees);
            if (Math.Abs(change) > TurnShape.MaxSingleChangeDeg) return false;
            walked += stitched.Segments[i].DistanceMeters;
        }
        return true;
    }

    /// Step 2: every directed edge (u, v) of the taxiway with u within 150 m of A along the network
    /// whose connection (the shortest path A to u not passing v), u to v and the straightest onward
    /// run to 80 m past u make a clean turn to the picked side.
    private static List<Option> ChartedOptions(TurnPickContext ctx, int a, double incoming, string name,
        TurnSide side, HashSet<int> blocked)
    {
        var options = new List<Option>();
        var graph = ctx.Graph;
        var reach = new TaxiRouter(graph);
        reach.BlockedNodes.UnionWith(blocked);
        var dist = reach.ComputeGraphDistancesFrom(a);

        foreach (var (u, d) in dist.OrderBy(p => p.Value).ThenBy(p => p.Key))
        {
            if (d > ChartedSearchMeters) break;
            if (!graph.Adjacency.TryGetValue(u, out var edges)) continue;
            foreach (var edge in edges)
            {
                int v = edge.ToNodeId;
                if (!Named(edge.TaxiwayName, name) || v == a || blocked.Contains(v)) continue;

                List<int> connection;
                if (u == a) connection = new List<int> { a };
                else
                {
                    var avoid = new TaxiRouter(graph);
                    avoid.BlockedNodes.UnionWith(blocked);
                    avoid.BlockedNodes.Add(v);
                    var path = avoid.FindShortestPath(a, u);
                    if (path == null || path.Segments.Count == 0) continue;
                    connection = NodeIds(path);
                    if (connection[0] != a || connection[^1] != u) continue;
                }

                var legs = new List<(double Bearing, double LengthMeters)>();
                double joinAt = 0;
                for (int i = 0; i + 1 < connection.Count; i++)
                {
                    var e = graph.GetEdge(connection[i], connection[i + 1])!;
                    legs.Add((e.BearingDegrees, e.DistanceMeters));
                    joinAt += e.DistanceMeters;
                }
                legs.Add((edge.BearingDegrees, edge.DistanceMeters));
                AddStraightestContinuation(graph, legs, connection, edge, name);

                if (TurnShape.IsCleanTurn(incoming, legs, joinAt, side))
                    options.Add(new Option(connection, v, OffChart: false, 0));
            }
        }
        return options;
    }

    /// Onward from v along the same taxiway, each time the edge that bends least, until 80 m past u.
    private static void AddStraightestContinuation(TaxiGraph graph, List<(double Bearing, double LengthMeters)> legs,
        List<int> connection, TaxiEdge first, string name)
    {
        var visited = new HashSet<int>(connection) { first.ToNodeId };
        double past = first.DistanceMeters;
        var last = first;
        while (past < TurnShape.WalkPastJoinMeters && graph.Adjacency.TryGetValue(last.ToNodeId, out var next))
        {
            var pick = next.Where(e => Named(e.TaxiwayName, name) && !visited.Contains(e.ToNodeId))
                .OrderBy(e => Math.Abs(Normalize(e.BearingDegrees - last.BearingDegrees)))
                .FirstOrDefault();
            if (pick == null) break;
            legs.Add((pick.BearingDegrees, pick.DistanceMeters));
            visited.Add(pick.ToNodeId);
            past += pick.DistanceMeters;
            last = pick;
        }
    }

    /// Step 4: the rest of the route from v with A, u and the connection's interior blocked, then the
    /// honoring conditions (no fallback, the pinned node kept, within the length bound).
    private static (Completed?, Failure) Complete(Option option, List<int> prefix,
        List<(int From, int To)> prefixOffChart, int row, TurnSide side,
        TurnPickContext ctx, TaxiRoute input, bool inputHasPin, HashSet<int> blocked, double incoming,
        List<int> chartedBeforeJoin, ChartedRunwayRun chartedRun)
    {
        int a = option.DepartureNodeId, u = option.JoinNodeId, v = option.NextNodeId;
        var own = option.Connection.Distinct().ToList();   // A, the interior, u
        var stepBlocked = new HashSet<int>(blocked);
        stepBlocked.UnionWith(own);
        // The rest never comes back to where the route has already been: with only the junction closed,
        // a turn the destination does not lie on was "honored" by a loop back through an earlier node
        // and down the charted side after all (WMKK "left onto G", 265 m of loop). Closed for this
        // search only; the pick's own BlockedNodeIds stay A, the interior and u.
        stepBlocked.UnionWith(prefix);
        // Nor does it go back over the charted way it declined, up to where that way joins the taxiway
        // (ZJHK "right onto C": an off-chart leg beside the charted lead-in, 37 m of C, then back up the
        // lead-in itself to the taxiway before, and onto C the charted way after all).
        stepBlocked.UnionWith(chartedBeforeJoin);
        // Nor does it come back onto the arm of the taxiway the pick turned AWAY from: turning the picked
        // way, leaving the taxiway and returning to its other arm is going the charted way after all
        // (the owner's ruling, 2026-11-15: KCLE "left onto G7" came back onto G7's other arm across the
        // runway just vacated, and a recalculation on that lap began it again). The destination itself
        // stays open, so one that lies on that arm is still reached from elsewhere when there is a way.
        var otherArm = OtherArm(ctx.Graph, u, v, ctx.Sequence[row]);
        foreach (int id in otherArm)
            if (id != ctx.DestinationNodeId) stepBlocked.Add(id);
        // An option whose very next node is one of those would start the rest on closed ground.
        if (stepBlocked.Contains(v)) return (null, Failure.Remainder);

        int end = ctx.DestinationNodeId;
        bool lastRow = row == ctx.Sequence.Count - 1;
        if (lastRow && ctx.Terminator is { Type: ProgressiveTerminatorType.EndOfTaxiway or ProgressiveTerminatorType.HoldShortTaxiway })
        {
            end = ResolveProgressiveEnd(ctx.Graph, v, ctx.Sequence[row], stepBlocked, ctx.Terminator);
            if (end == 0) return (null, Failure.Remainder);
        }

        List<int> remainder;
        if (end == v) remainder = new List<int> { v };
        else
        {
            var router = new TaxiRouter(ctx.Graph);
            router.BlockedNodes.UnionWith(stepBlocked);
            var rest = router.FindConstrainedPath(v, end, ctx.Sequence.Skip(row).ToList(), ctx.DestinationIsRunway);
            if (rest == null || rest.ConstrainedFallbackReason != null || rest.Segments.Count == 0)
                return (null, Failure.Remainder);
            remainder = NodeIds(rest);
            if (remainder[0] != v) return (null, Failure.Remainder);
        }

        var all = new List<int>(prefix);
        all.AddRange(option.Connection);
        all.AddRange(remainder);
        if (VisitsANodeTwice(all)) return (null, Failure.Remainder);
        var offChartPairs = new List<(int From, int To)>(prefixOffChart);
        if (option.OffChart) offChartPairs.Add((a, u));
        var stitched = new TaxiRouter(ctx.Graph).BuildRouteThrough(all, offChartPairs);
        if (stitched == null || stitched.Segments.Count == 0) return (null, Failure.Other);
        // A route that turns the picked way and then doubles back does not lead there that way.
        if (!BuiltRouteMakesTheTurn(stitched, prefix.Count, prefix.Count + option.Connection.Count - 1,
                ctx.Sequence[row], side, incoming, option.OffChart))
            return (null, Failure.Remainder);
        if (ComesBackPastTheJunction(stitched, prefix.Count + option.Connection.Count - 1, ctx.Graph.Nodes[a]))
            return (null, Failure.Remainder);
        // A pick never ADDS a stretch along a runway (crossing one is another matter, and has its hold).
        if (LongestRunAlongARunwayMeters(ctx.Graph, stitched, end) >
            Math.Max(AlongRunwayLimitMeters, chartedRun.For(end) + AlongRunwayMoreThanChartedMeters))
            return (null, Failure.Remainder);
        // The pin is tested before the length: an option that drops the pin is Pinned (MissesHoldingPoint
        // when no option fails another way) even when it is also too long.
        if (inputHasPin && !all.Contains(ctx.PinnedNodeId)) return (null, Failure.Pinned);
        if (stitched.TotalDistanceMeters > input.TotalDistanceMeters * ctx.MaxLengthRatio + ctx.MaxLengthPadMeters)
            return (null, Failure.Length);

        stitched.DestinationName = input.DestinationName;
        stitched.TaxiwaySequence = input.TaxiwaySequence;
        // A route that fell back to shortest path is still one: the summary's fallback warning stays.
        stitched.ConstrainedFallbackReason = input.ConstrainedFallbackReason;
        double connectionMeters = 0;
        for (int i = prefix.Count; i < prefix.Count + option.Connection.Count - 1; i++)
            connectionMeters += stitched.Segments[i].DistanceMeters;

        return (new Completed(stitched, end, new AppliedTurnPick
        {
            RowIndex = row, Side = side, TaxiwayName = ctx.Sequence[row],
            DepartureNodeId = a, JoinNodeId = u,
            FirstSegmentAfterDeparture = prefix.Count,
            FirstTaxiwaySegment = prefix.Count + option.Connection.Count - 1,
            ChangedRoute = true, IsOffChart = option.OffChart,
            OffChartLengthMeters = option.OffChart ? option.OffChartLength : 0,
            BlockedNodeIds = own,
        }, connectionMeters, otherArm), Failure.None);
    }

    /// The arm of the row's taxiway the pick turns away from: every node reached along the taxiway's
    /// own edges from the join u's neighbors other than v, never through u, and not also reached that
    /// way from v. On a taxiway that is a ring both ways round are the same nodes, and there is none.
    internal static HashSet<int> OtherArm(TaxiGraph graph, int u, int v, string name)
    {
        HashSet<int> Reach(IEnumerable<int> from)
        {
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            foreach (int id in from)
                if (id != u && seen.Add(id)) queue.Enqueue(id);
            while (queue.Count > 0)
            {
                if (!graph.Adjacency.TryGetValue(queue.Dequeue(), out var edges)) continue;
                foreach (var e in edges)
                    if (Named(e.TaxiwayName, name) && e.ToNodeId != u && seen.Add(e.ToNodeId))
                        queue.Enqueue(e.ToNodeId);
            }
            return seen;
        }

        var arm = Reach(graph.Adjacency.TryGetValue(u, out var fromU)
            ? fromU.Where(e => Named(e.TaxiwayName, name) && e.ToNodeId != v).Select(e => e.ToNodeId)
            : Enumerable.Empty<int>());
        arm.ExceptWith(Reach(new[] { v }));
        return arm;
    }

    /// The same node twice is a loop. Each leg the router builds is a simple path, but joined they can
    /// run out along a taxiway and straight back over it: ZBAD "right onto L2" went 116 m west along
    /// M to the node the router had picked to leave it by, and back east over the same five nodes.
    /// VirtualPilot: 487 of the 1,606 picks still honored without this test did that somewhere.
    internal static bool VisitsANodeTwice(IReadOnlyList<int> nodeIds)
    {
        var seen = new HashSet<int>();
        foreach (int id in nodeIds)
            if (!seen.Add(id)) return true;
        return false;
    }

    /// Once it is 60 m beyond the join, does the route pass within 30 m of the junction it turned at
    /// (the departure node A)? Then it has turned the picked way and come straight back beside its own
    /// turn, on other nodes: a loop no closed node catches, and one that puts the aircraft next to the
    /// start of the leg it has just taxied, where the segment tracker can take it round again.
    private const double BackPastJunctionAfterMeters = 60.0, BackPastJunctionWithinMeters = 30.0;

    private static bool ComesBackPastTheJunction(TaxiRoute stitched, int firstTaxiway, TaxiNode junction)
    {
        double walked = 0;
        for (int i = firstTaxiway; i < stitched.Segments.Count; i++)
        {
            var s = stitched.Segments[i];
            if (walked >= BackPastJunctionAfterMeters &&
                GuidanceGeometry.PerpendicularDistanceToSegmentMeters(junction.Latitude, junction.Longitude,
                    s.FromNode.Latitude, s.FromNode.Longitude, s.ToNode.Latitude, s.ToNode.Longitude)
                    < BackPastJunctionWithinMeters)
                return true;
            walked += s.DistanceMeters;
        }
        return false;
    }

    /// A runway crossing puts 45 to 60 m of route on its pavement; more than 100 m on one runway is
    /// driving ALONG it. Where the scenery draws taxi lines along a runway the router will use them, and
    /// the hold passes have no good stop for such a route (OMDB "left onto N": onto 12L/30R at N8 with
    /// "Passing the hold line" and 370 m along it, unheld and unwarned). The charted route may already
    /// do it; the pick may not make it more than 50 m worse.
    private const double AlongRunwayLimitMeters = 100.0, AlongRunwayMoreThanChartedMeters = 50.0;

    /// The longest stretch of the route whose segments lie on one runway's pavement at both ends. A
    /// runway the destination node stands on is not counted: the route ends there by design.
    private static double LongestRunAlongARunwayMeters(TaxiGraph graph, TaxiRoute route, int destinationNodeId)
    {
        double longest = 0;
        graph.Nodes.TryGetValue(destinationNodeId, out var destination);
        foreach (var centerline in graph.RunwayCenterlines)
        {
            var shape = RunwayShape.For(centerline);
            if (destination != null && shape.Contains(destination.Latitude, destination.Longitude, 0.0)) continue;
            double run = 0;
            bool fromOn = route.Segments.Count > 0 &&
                shape.Contains(route.Segments[0].FromNode.Latitude, route.Segments[0].FromNode.Longitude, 0.0);
            foreach (var s in route.Segments)
            {
                bool toOn = shape.Contains(s.ToNode.Latitude, s.ToNode.Longitude, 0.0);
                run = fromOn && toOn ? run + s.DistanceMeters : 0;
                if (run > longest) longest = run;
                fromOn = toOn;
            }
        }
        return longest;
    }

    /// Section 5: walking only the taxiway's own edges from v (v itself counts), never into a blocked
    /// node: the farthest node by path distance (EndOfTaxiway) or the nearest that also carries the
    /// target taxiway (HoldShortTaxiway). 0 when there is none.
    private static int ResolveProgressiveEnd(TaxiGraph graph, int v, string name, HashSet<int> blocked,
        ProgressiveTerminator terminator)
    {
        var dist = new Dictionary<int, double> { [v] = 0 };
        var done = new HashSet<int>();
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(v, 0);
        var order = new List<int>();
        while (queue.Count > 0)
        {
            int n = queue.Dequeue();
            if (!done.Add(n)) continue;
            order.Add(n);
            if (!graph.Adjacency.TryGetValue(n, out var edges)) continue;
            foreach (var e in edges)
            {
                if (!Named(e.TaxiwayName, name) || blocked.Contains(e.ToNodeId) || done.Contains(e.ToNodeId)) continue;
                double d = dist[n] + e.DistanceMeters;
                if (d < dist.GetValueOrDefault(e.ToNodeId, double.MaxValue))
                {
                    dist[e.ToNodeId] = d;
                    queue.Enqueue(e.ToNodeId, d);
                }
            }
        }

        if (terminator.Type == ProgressiveTerminatorType.EndOfTaxiway)
            return order.OrderByDescending(n => dist[n]).ThenBy(n => n).First();

        foreach (int n in order)   // settled nearest first
            if (graph.Adjacency.TryGetValue(n, out var edges) && edges.Any(e => Named(e.TaxiwayName, terminator.Target)))
                return n;
        return 0;
    }

    private static string Counts(IEnumerable<string> items)
    {
        var groups = items.GroupBy(s => s).Select(g => $"{g.Key} {g.Count()}").ToList();
        return groups.Count == 0 ? "none" : string.Join(", ", groups);
    }

    private static void Log(TurnPickContext ctx, int row, TurnSide side, string name, TurnPickOutcome outcome, string detail)
    {
        if (ctx.Log == null) return;
        try { ctx.Log($"Turn pick row {row} {side} onto {name}: {outcome} ({detail})"); }
        catch { /* logging never breaks routing */ }
    }

    private static List<int> NodeIds(TaxiRoute route)
    {
        var ids = new List<int>();
        if (route.Segments.Count == 0) return ids;
        ids.Add(route.Segments[0].FromNode.NodeId);
        foreach (var s in route.Segments) ids.Add(s.ToNode.NodeId);
        return ids;
    }

    private static bool Named(string? edgeName, string name)
        => !string.IsNullOrEmpty(name) && string.Equals(edgeName, name, StringComparison.OrdinalIgnoreCase);

    private static double Normalize(double deg) => TaxiGraph.NormalizeAngle(deg);
}
