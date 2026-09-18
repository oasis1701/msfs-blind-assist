using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// One runway the route meets. Indices are NODE indices into the node list the classifier was
/// given (<see cref="RunwayRouteClassifier.NodesFrom"/>).
/// </summary>
/// <param name="EntryIndex">The last node clear of the runway before it.</param>
/// <param name="FirstOnIndex">The first node on the runway, or -1 when one edge jumped across it.</param>
/// <param name="ExitIndex">The first clear node after the runway, or -1 when the route ends on it.</param>
/// <param name="MeetLat">The first node on the runway, or where a jumping edge meets the centerline.</param>
/// <param name="Designator">The designator of the runway end nearer the meet point.</param>
public sealed record RunwayPassage(
    TaxiGraph.RunwayCenterline Runway,
    RunwayShape Shape,
    RunwayEventKind Kind,
    int EntryIndex,
    int FirstOnIndex,
    int ExitIndex,
    double MeetLat,
    double MeetLon,
    string Designator)
{
    /// <summary>The first node at or past the runway; orders passages along the route.</summary>
    public int ReachIndex => FirstOnIndex >= 0 ? FirstOnIndex : EntryIndex + 1;
}

/// <summary>
/// Decides, per runway, whether a route ENTERS it (onto the pavement and back off the same side,
/// or ends on it) or CROSSES it (off the other side), from which side of the runway the route is
/// on before and after. A route that starts on a runway and leaves it meets nothing.
///
/// <para>Replaces the strict per-edge intersection, whose verdict at a node on or centimetres from
/// the centerline was decided by float placement: it lost real crossings (a node exactly on a
/// north-south line), invented them along the runway (ESMX) and at end exits (KORD W5).
/// Detection uses the half-width alone; hold placement adds the 10 m clear margin. Rules and
/// cases: docs/taxi-guidance.md, "Runway crossings and entries".</para>
/// </summary>
public static class RunwayRouteClassifier
{
    public static IReadOnlyList<TaxiNode?> NodesFrom(IReadOnlyList<TaxiRouteSegment>? segments, int fromSegmentIndex)
    {
        var nodes = new List<TaxiNode?>();
        if (segments is null || fromSegmentIndex < 0 || fromSegmentIndex >= segments.Count) return nodes;
        nodes.Add(segments[fromSegmentIndex]?.FromNode);
        for (int i = fromSegmentIndex; i < segments.Count; i++)
            nodes.Add(segments[i]?.ToNode);
        return nodes;
    }

    public static List<RunwayPassage> ClassifyAll(
        IReadOnlyList<TaxiNode?> nodes, IEnumerable<TaxiGraph.RunwayCenterline> runways)
    {
        var all = new List<RunwayPassage>();
        if (nodes is null || runways is null) return all;
        foreach (var runway in runways)
        {
            if (runway is null) continue;
            all.AddRange(Classify(nodes, RunwayShape.For(runway)));
        }
        return all.OrderBy(p => p.ReachIndex).ThenBy(p => p.EntryIndex).ToList();
    }

    public static List<RunwayPassage> Classify(IReadOnlyList<TaxiNode?> nodes, RunwayShape shape)
    {
        var passages = new List<RunwayPassage>();
        if (nodes is null || shape is null || shape.IsDegenerate) return passages;

        int lastClear = -1;
        int lastClearSide = 0;
        bool runOpen = false;
        int runEntry = -1;
        int runEntrySide = 0;
        int runFirstOn = -1;
        double runMeetLat = 0.0, runMeetLon = 0.0, runMeetAlong = 0.0;

        for (int k = 0; k < nodes.Count; k++)
        {
            var node = nodes[k];
            if (node is null) continue;
            var (along, lateral) = shape.Project(node.Latitude, node.Longitude);

            if (shape.ContainsAlongLateral(along, lateral, 0.0))
            {
                if (!runOpen)
                {
                    runOpen = true;
                    runEntry = lastClear;
                    runEntrySide = lastClearSide;
                    runFirstOn = k;
                    runMeetLat = node.Latitude;
                    runMeetLon = node.Longitude;
                    runMeetAlong = along;
                }
                continue;
            }

            int side = Math.Sign(lateral);
            if (side == 0) continue;   // on the extended axis beyond the extent: no side

            if (runOpen)
            {
                if (runEntry >= 0)
                {
                    var kind = side == runEntrySide ? RunwayEventKind.Entry : RunwayEventKind.Crossing;
                    passages.Add(new RunwayPassage(shape.Centerline, shape, kind, runEntry, runFirstOn, k,
                        runMeetLat, runMeetLon, shape.NameAt(runMeetAlong)));
                }
                runOpen = false;   // no entry: the route started on the runway and vacated
            }
            else if (lastClear >= 0 && side != lastClearSide &&
                     TryFindCrossingPoint(nodes, lastClear, k, shape, out double meetLat, out double meetLon, out double meetAlong))
            {
                passages.Add(new RunwayPassage(shape.Centerline, shape, RunwayEventKind.Crossing, lastClear, -1, k,
                    meetLat, meetLon, shape.NameAt(meetAlong)));
            }

            lastClear = k;
            lastClearSide = side;
        }

        if (runOpen && runEntry >= 0)
            passages.Add(new RunwayPassage(shape.Centerline, shape, RunwayEventKind.Entry, runEntry, runFirstOn, -1,
                runMeetLat, runMeetLon, shape.NameAt(runMeetAlong)));

        return passages;
    }

    /// <summary>
    /// Where the stretch between two clear nodes on opposite sides meets the centerline inside the
    /// runway's extent (the first such point), if anywhere. A stretch that changes side only beyond
    /// a runway end is a taxiway looping round it, not a crossing.
    /// </summary>
    private static bool TryFindCrossingPoint(
        IReadOnlyList<TaxiNode?> nodes, int from, int to, RunwayShape shape,
        out double meetLat, out double meetLon, out double meetAlong)
    {
        meetLat = meetLon = meetAlong = 0.0;
        TaxiNode? previous = null;
        double prevAlong = 0.0, prevLateral = 0.0;
        for (int k = from; k <= to; k++)
        {
            var node = nodes[k];
            if (node is null) continue;
            var (along, lateral) = shape.Project(node.Latitude, node.Longitude);
            if (previous is not null && prevLateral * lateral < 0.0)
            {
                double t = prevLateral / (prevLateral - lateral);
                double a = prevAlong + t * (along - prevAlong);
                if (a >= shape.ExtentMinMeters && a <= shape.ExtentMaxMeters)
                {
                    meetLat = previous.Latitude + t * (node.Latitude - previous.Latitude);
                    meetLon = previous.Longitude + t * (node.Longitude - previous.Longitude);
                    meetAlong = a;
                    return true;
                }
            }
            previous = node;
            prevAlong = along;
            prevLateral = lateral;
        }
        return false;
    }
}
