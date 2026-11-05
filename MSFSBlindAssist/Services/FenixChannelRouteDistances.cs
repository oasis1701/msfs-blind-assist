using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>What D or Shift+D can say: a distance, past (the top of descent only), or not available.</summary>
internal enum FenixDistanceKind { Distance, Past, NotAvailable }

/// <summary>An answer of <see cref="FenixChannelRouteDistances"/>: its kind, and the distance in nautical miles (0 unless a distance).</summary>
internal readonly record struct FenixDistanceAnswer(FenixDistanceKind Kind, double Nm);

/// <summary>
/// D and Shift+D on the Fenix: the distance to the destination and to the top of descent, summed along the route the
/// channel sends (<see cref="FenixChannelRoute"/>), as measured in flight (docs/fenix-channel/evidence/10-distances.md,
/// "The rules"). The FMS's own figures are not on the channel, so the sum follows the route the ND draws.
/// <para>
/// The line: the elements of the active route that are vertices of the drawn line (<see cref="FenixChannelRoute.IsPath"/>)
/// and not on the missed approach, in order, up to the destination element or, with none, to the last of them. A turn
/// piece counts as its arc; every other element as the great circle from the point before it. A discontinuity's end
/// first drops the points before it that carry its own ident and lie away from it (a heading leg's drawn far point,
/// MANUAL's 500 NM out), and the next start follows straight on. The aircraft's distance to the line's end runs straight
/// to the TO waypoint, then along the line from the last point that carries the TO's ident: never from the first one,
/// which can lie far behind.
/// </para>
/// <para>
/// A route the sum cannot follow is not available, never a partial sum: no TO, the TO on the missed approach, a TO with
/// no waypoint symbol (unless it is the arrival runway), the TO's ident on no point of the line; for D also fewer than two
/// runways or no destination on the line.
/// </para>
/// </summary>
internal static class FenixChannelRouteDistances
{
    /// <summary>A top of descent marker is on a step of the line when it lies at most this far across it.</summary>
    internal const double OnLineNm = 0.5;

    /// <summary>A top of descent this close ahead, or behind, is past.</summary>
    internal const double PastWithinNm = 0.1;

    /// <summary>Two points this close are one: a discontinuity's end at the point the line already reached adds nothing.</summary>
    internal const double SamePointNm = 0.05;

    // How far before a step's start and past its end a marker still counts as on that step.
    private const double AlongSlackNm = 0.1;

    // A step shorter than this has no direction to measure a marker against (an end and the start after it at one point).
    private const double NoLengthNm = 1e-6;

    private static readonly FenixDistanceAnswer NotAvailableAnswer = new(FenixDistanceKind.NotAvailable, 0);
    private static readonly FenixDistanceAnswer PastAnswer = new(FenixDistanceKind.Past, 0);

    /// <summary>
    /// D's answer from one snapshot of the ND, or Shift+D's with <paramref name="topOfDescent"/>: not available when the
    /// ND is not trusted or the snapshot has no message ([FNX-22]: verdict and data from the same snapshot); not
    /// available, after handing the decoder's problem to <paramref name="reportProblem"/>, when the route cannot be
    /// decoded; otherwise <see cref="ToDestination"/> (null is not available) or <see cref="ToTopOfDescent"/>.
    /// </summary>
    public static FenixDistanceAnswer Answer(FenixChannelSnapshot s, bool topOfDescent, Action<string> reportProblem)
    {
        if (!s.Verdict.Trusted || s.Push is null) return NotAvailableAnswer;

        FenixChannelSection<FenixRoute> route = FenixChannelRoute.Decode(s.Push);
        if (!route.IsOk)
        {
            reportProblem(route.Problem ?? "no data");
            return NotAvailableAnswer;
        }

        if (topOfDescent) return ToTopOfDescent(route.Value!);
        return ToDestination(route.Value!) is { } nm ? new FenixDistanceAnswer(FenixDistanceKind.Distance, nm) : NotAvailableAnswer;
    }

    /// <summary>
    /// The distance from the aircraft to the destination, in nautical miles; null when it is not available. With the
    /// arrival runway as the TO it is the great circle to its threshold (2.1's second entry); otherwise the aircraft's
    /// distance to the end of the line, which must reach a destination element.
    /// </summary>
    public static double? ToDestination(FenixRoute r)
    {
        if (r.Runways.Count < 2) return null;
        if (r.ActiveLeg is { } to && to.Ident.Length > 0 && to.Ident == FenixChannelRoute.ArrivalIdent(r.Runways))
            return FenixChannelRouteGeometry.DistanceNm(r.Position, r.Runways[1].Threshold);

        List<FenixRouteLeg> line = Line(r);
        if (!line.Any(FenixChannelRoute.IsDestination)) return null;

        return AircraftToEnd(r, Walk(line));
    }

    /// <summary>
    /// The distance from the aircraft to the top of descent along the line. Any end of descent marker means the descent
    /// has begun: past, whatever top of descent markers are still drawn (the descent restart points). Otherwise exactly
    /// one top of descent marker that is not the go-around's must lie on a step of the line (within
    /// <see cref="OnLineNm"/> across, the nearest across when it lies on more than one), or the answer is not available;
    /// so must the TO, as for D. The answer is the aircraft's distance to the line's end less the marker's: at most
    /// <see cref="PastWithinNm"/> is past.
    /// </summary>
    public static FenixDistanceAnswer ToTopOfDescent(FenixRoute r)
    {
        if (r.Markers.Any(m => m.Type == MapMarkerKind.Eod)) return PastAnswer;

        List<FenixRouteMarker> tops = r.Markers.Where(m => m.Type == MapMarkerKind.Tod && !m.IsGoAround).ToList();
        if (tops.Count != 1) return NotAvailableAnswer;

        Path path = Walk(Line(r));
        if (AircraftToEnd(r, path) is not { } aircraft) return NotAvailableAnswer;
        if (MarkerToEnd(path, tops[0].Point) is not { } marker) return NotAvailableAnswer;

        double ahead = aircraft - marker;
        return ahead <= PastWithinNm ? PastAnswer : new FenixDistanceAnswer(FenixDistanceKind.Distance, ahead);
    }

    // A point the walk kept: where it is, the ident of its element, and its step's length when the element is a turn
    // piece (its arc); null for a step that is the great circle from the point before.
    private readonly record struct KeptPoint(FenixLatLon Point, string Ident, double? ArcNm);

    // The walked line: its points, each one's step from the point before (0 for the first) and its distance to the end.
    private sealed record Path(IReadOnlyList<KeptPoint> Points, double[] Steps, double[] ToEnd)
    {
        public int LastIndexOf(string ident)
        {
            for (int i = Points.Count - 1; i >= 0; i--)
            {
                if (Points[i].Ident == ident) return i;
            }

            return -1;
        }
    }

    // The elements of the line: path elements off the missed approach, in order, to the first destination element.
    private static List<FenixRouteLeg> Line(FenixRoute r)
    {
        var line = new List<FenixRouteLeg>();
        foreach (FenixRouteLeg leg in r.Legs)
        {
            if (!FenixChannelRoute.IsPath(leg) || leg.MissedApproach) continue;

            line.Add(leg);
            if (FenixChannelRoute.IsDestination(leg)) break;
        }

        return line;
    }

    // Keeps the line's points, then measures the steps between the points kept: a discontinuity can drop a point after
    // the one that follows it was kept, so no step is summed before the walk ends.
    private static Path Walk(List<FenixRouteLeg> line)
    {
        var kept = new List<KeptPoint>();
        foreach (FenixRouteLeg leg in line)
        {
            if (FenixChannelRoute.BreaksPath(leg))
            {
                kept.RemoveAll(k => k.Ident == leg.Ident
                    && FenixChannelRouteGeometry.DistanceNm(k.Point, leg.Point) > SamePointNm);
                if (kept.Count > 0 && FenixChannelRouteGeometry.DistanceNm(kept[^1].Point, leg.Point) <= SamePointNm)
                    continue;

                kept.Add(new KeptPoint(leg.Point, leg.Ident, null));
                continue;
            }

            kept.Add(new KeptPoint(leg.Point, leg.Ident, TurnArc(leg)));
        }

        var steps = new double[kept.Count];
        for (int i = 1; i < kept.Count; i++)
        {
            steps[i] = kept[i].ArcNm ?? FenixChannelRouteGeometry.DistanceNm(kept[i - 1].Point, kept[i].Point);
        }

        var toEnd = new double[kept.Count];
        for (int i = kept.Count - 2; i >= 0; i--)
        {
            toEnd[i] = toEnd[i + 1] + steps[i + 1];
        }

        return new Path(kept, steps, toEnd);
    }

    // A turn piece's arc; null for any other element, and for a turn piece that did not send its radius and courses.
    private static double? TurnArc(FenixRouteLeg leg)
        => leg is { Kind: RouteLegKind.Arc, TurnRadius: { } radius, CourseIn: { } courseIn, CourseOut: { } courseOut }
            ? FenixChannelRouteGeometry.ArcNm(radius, courseIn, courseOut)
            : null;

    // The aircraft's distance to the line's end: straight to the TO waypoint, then along the line from the last point
    // carrying the TO's ident. Null when the TO cannot be followed.
    private static double? AircraftToEnd(FenixRoute r, Path path)
    {
        if (r.ActiveLeg is not { } to || to.Ident.Length == 0 || to.MissedApproach) return null;

        bool hasSymbol = r.Legs.Any(leg => leg.Kind == RouteLegKind.Symbol && leg.Ident == to.Ident);
        if (!hasSymbol && to.Ident != FenixChannelRoute.ArrivalIdent(r.Runways)) return null;

        int last = path.LastIndexOf(to.Ident);
        if (last < 0) return null;

        return FenixChannelRouteGeometry.DistanceNm(r.Position, to.Point) + path.ToEnd[last];
    }

    // The marker's distance to the line's end, from the step it lies on: the rest of that step (its great-circle rest,
    // scaled to the step's own length, for a turn piece) plus the distance from the step's far point. Null on no step.
    private static double? MarkerToEnd(Path path, FenixLatLon marker)
    {
        double? best = null;
        double bestAcross = double.MaxValue;
        for (int i = 1; i < path.Points.Count; i++)
        {
            FenixLatLon from = path.Points[i - 1].Point, to = path.Points[i].Point;
            double chord = FenixChannelRouteGeometry.DistanceNm(from, to);
            if (chord < NoLengthNm) continue;

            (double along, double cross) = FenixChannelRouteGeometry.Project(from, to, marker);
            double across = Math.Abs(cross);
            if (across > OnLineNm || along < -AlongSlackNm || along > chord + AlongSlackNm) continue;
            if (across >= bestAcross) continue;

            bestAcross = across;
            best = Math.Max(0, chord - along) * (path.Steps[i] / chord) + path.ToEnd[i];
        }

        return best;
    }
}
