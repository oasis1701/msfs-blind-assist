using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>What became of one Turn pick. Public so a public test theory can take it.</summary>
public enum TurnPickOutcome { AlreadyMet, Charted, OffChart, NoTurnFound, DoesNotLead, LongDetour }

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
    IReadOnlySet<int>? AlreadyBlocked = null,
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

    private sealed record Completed(TaxiRoute Route, int DestinationNodeId, AppliedTurnPick Pick, double ConnectionMeters);

    /// Applies picks in ascending row order. The returned Route is the input route object when no pick
    /// changed it. Route.TurnPicks is set to Applied.
    public static TurnPickResult Apply(TaxiRoute route, IReadOnlyDictionary<int, TurnSide> picks, TurnPickContext ctx)
    {
        var applied = new List<AppliedTurnPick>();
        var refusals = new List<TurnPickRefusal>();
        var blocked = new HashSet<int>(ctx.AlreadyBlocked ?? (IReadOnlySet<int>)new HashSet<int>());
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
        if (TurnShape.IsCleanTurn(incoming, LegsFrom(current, afterDeparture), joinAt, side))
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
            }, joinAt));
        }

        // Steps 2 and 3 work from A and the route before it; a first row with nothing arriving at A
        // starts from the node nearest the aircraft, with an empty prefix.
        int a;
        var prefix = new List<int>();
        if (incomingOrNull == null)
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

        // Step 2: a charted turn; the completed option with the shortest whole route wins.
        Completed? best = null;
        foreach (var option in ChartedOptions(ctx, a, incoming, name, side, blocked))
        {
            var (done, failure) = Complete(option, prefix, prefixOffChart, row, side, ctx, input, inputHasPin, blocked);
            if (done == null) { failures.Add(failure); continue; }
            if (best == null || done.Route.TotalDistanceMeters < best.Route.TotalDistanceMeters - 0.01 ||
                (Math.Abs(done.Route.TotalDistanceMeters - best.Route.TotalDistanceMeters) <= 0.01 &&
                 done.ConnectionMeters < best.ConnectionMeters))
                best = done;
        }
        if (best != null)
        {
            Log(ctx, row, side, name, TurnPickOutcome.Charted,
                $"departure {a}, join {best.Pick.JoinNodeId}, {best.Route.TotalDistanceMeters:F0} m");
            return (TurnPickOutcome.Charted, best);
        }

        // Step 3: an off-chart leg, candidates best first; the first that completes wins.
        foreach (var c in OffChartLeg.Find(ctx.Graph, ctx.Pavement, a, incoming, name, side, blocked))
        {
            if (blocked.Contains(c.NextNodeId)) continue;   // as the charted path skips a blocked v
            var option = new Option(new List<int> { a, c.JoinNodeId }, c.NextNodeId, OffChart: true, c.LengthMeters);
            var (done, failure) = Complete(option, prefix, prefixOffChart, row, side, ctx, input, inputHasPin, blocked);
            if (done == null) { failures.Add(failure); continue; }
            Log(ctx, row, side, name, TurnPickOutcome.OffChart,
                $"departure {a}, join {c.JoinNodeId}, leg {c.LengthMeters:F1} m, {done.Route.TotalDistanceMeters:F0} m");
            return (TurnPickOutcome.OffChart, done);
        }

        var reason = failures.Contains(Failure.Length) ? TurnPickOutcome.LongDetour
            : failures.Contains(Failure.Remainder) ? TurnPickOutcome.DoesNotLead
            : TurnPickOutcome.NoTurnFound;
        if (ctx.Log != null)
            Log(ctx, row, side, name, reason,
                $"departure {a}, options failed: {Counts(failures.Select(f => f.ToString()))}; " +
                $"off-chart legs: {Counts(OffChartRefusals(ctx, a, incoming, name, side, blocked))}");
        return (reason, null);
    }

    /// (bearing, length) of each route segment from segmentIndex on.
    private static List<(double Bearing, double LengthMeters)> LegsFrom(TaxiRoute route, int segmentIndex)
    {
        var legs = new List<(double, double)>();
        for (int i = segmentIndex; i < route.Segments.Count; i++)
            legs.Add((route.Segments[i].BearingDegrees, route.Segments[i].DistanceMeters));
        return legs;
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
        TurnPickContext ctx, TaxiRoute input, bool inputHasPin, HashSet<int> blocked)
    {
        int a = option.DepartureNodeId, u = option.JoinNodeId, v = option.NextNodeId;
        var own = option.Connection.Distinct().ToList();   // A, the interior, u
        var stepBlocked = new HashSet<int>(blocked);
        stepBlocked.UnionWith(own);

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
        var offChartPairs = new List<(int From, int To)>(prefixOffChart);
        if (option.OffChart) offChartPairs.Add((a, u));
        var stitched = new TaxiRouter(ctx.Graph).BuildRouteThrough(all, offChartPairs);
        if (stitched == null || stitched.Segments.Count == 0) return (null, Failure.Other);
        // The pin is tested before the length: an option that drops the pin is NoTurnFound even when it is also too long.
        if (inputHasPin && !all.Contains(ctx.PinnedNodeId)) return (null, Failure.Pinned);
        if (stitched.TotalDistanceMeters > input.TotalDistanceMeters * ctx.MaxLengthRatio + ctx.MaxLengthPadMeters)
            return (null, Failure.Length);

        stitched.DestinationName = input.DestinationName;
        stitched.TaxiwaySequence = input.TaxiwaySequence;
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
        }, connectionMeters), Failure.None);
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

    /// For the refusal log: why each off-chart leg from A onto the taxiway was refused.
    private static IEnumerable<string> OffChartRefusals(TurnPickContext ctx, int a, double incoming, string name,
        TurnSide side, HashSet<int> blocked)
    {
        foreach (int join in ctx.Graph.GetNodesOnTaxiway(name))
        {
            if (join == a || blocked.Contains(join) || !ctx.Graph.Adjacency.TryGetValue(join, out var edges)) continue;
            foreach (int next in edges.Where(e => Named(e.TaxiwayName, name)).Select(e => e.ToNodeId).Distinct())
                yield return OffChartLeg.Judge(ctx.Graph, ctx.Pavement, a, incoming, join, next, side).ToString();
        }
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

    private static double Normalize(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        else if (deg <= -180.0) deg += 360.0;
        return deg;
    }
}
