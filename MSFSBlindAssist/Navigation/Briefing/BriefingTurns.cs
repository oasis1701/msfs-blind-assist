// MSFSBlindAssist/Navigation/Briefing/BriefingTurns.cs
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which way a briefed route turns where it changes taxiway, and where it turns into the stand, in a controller's
/// plain words ("left", "slight right", "sharp left", "straight ahead").
///
/// <para>A turn is measured over a STRETCH of route around the change, never at one junction: navdata splits a real 90°
/// turn into several small bends (the reason live guidance's "Straighten." cue cannot trust one junction either), so the
/// sum of the bearing changes over the stretch is the turn. The stretch reaches <see cref="StretchMetres"/> into each
/// taxiway but never past its middle — the other half belongs to the neighbouring change — and takes in any unnamed
/// connector between the two.</para>
///
/// <para>Runs are exactly <see cref="RouteTaxiwaySequence.DistinctConsecutive"/>'s groups, so the turns line up with the
/// taxiway names. The 20° and 60° lines are <see cref="TaxiRouter.GetTurnDirection"/>'s, so the briefing uses the same
/// 20° and 60° lines as live taxi guidance (which judges a single junction from the aircraft's heading, so the two can
/// still differ where a junction's bend and the stretch disagree); the 120° line is TaxiRouter's documented split
/// between a normal and a sharp turn. Live guidance adds "sharp" and the angle from 60° up; a briefing keeps plain
/// words.</para>
///
/// <para>No direction is given for joining the first taxiway: after pushback the aircraft's heading is not known, and on
/// the taxi-in the landing exit's side is already briefed.</para>
/// </summary>
public static class BriefingTurns
{
    /// <summary>How far into each taxiway a change's stretch reaches, at most.</summary>
    public const double StretchMetres = 60.0;

    /// <summary>A segment shorter than this is a point with no direction (GuidanceGeometry's rule).</summary>
    private const double DegenerateMetres = 1.0;

    private const double StraightBelowDeg = 20.0;
    private const double SlightBelowDeg = 60.0;
    private const double SharpFromDeg = 120.0;

    /// <summary>The words for a signed turn, right positive.</summary>
    public static string Words(double signedDegrees)
    {
        double magnitude = Math.Abs(signedDegrees);
        if (magnitude < StraightBelowDeg) return "straight ahead";
        string side = signedDegrees < 0 ? "left" : "right";
        if (magnitude < SlightBelowDeg) return $"slight {side}";
        return magnitude < SharpFromDeg ? side : $"sharp {side}";
    }

    /// <summary>One entry per name of <see cref="RouteTaxiwaySequence.DistinctConsecutive"/>, in order: null for the first
    /// taxiway, then the turn onto each later one — null where it cannot be measured.</summary>
    public static IReadOnlyList<string?> TaxiwayTurns(IReadOnlyList<TaxiRouteSegment>? segments)
    {
        var turns = new List<string?>();
        if (segments == null) return turns;
        var runs = Runs(segments);
        for (int i = 0; i < runs.Count; i++)
        {
            if (i == 0)
            {
                turns.Add(null);
                continue;
            }
            double back = Math.Min(StretchMetres, runs[i - 1].Length / 2.0);
            double ahead = Math.Min(StretchMetres, runs[i].Length / 2.0);
            turns.Add(TurnOver(segments, runs[i - 1].Last, back, runs[i].First, ahead) is double d ? Words(d) : null);
        }
        return turns;
    }

    /// <summary>The turn from the last named taxiway into the unnamed segments that end the route (the stand lead-in);
    /// null when the route ends on a named segment, when there is nothing to measure, when it runs straight in, or
    /// when the unnamed tail is longer than a stand lead-in (<see cref="TaxiGraph.STAND_LEAD_IN_CHAIN_MAX_M"/>,
    /// 100 m) — beyond that the tail is apron taxilane and its first bend is not the turn into the stand (live
    /// KATL C 22: 541 m of unnamed ramp beyond taxiway F).</summary>
    public static string? StandTurn(IReadOnlyList<TaxiRouteSegment>? segments)
    {
        if (segments == null) return null;
        var runs = Runs(segments);
        if (runs.Count == 0) return null;
        var last = runs[^1];
        int tailFirst = last.Last + 1;
        if (tailFirst >= segments.Count) return null;
        double tail = Length(segments, tailFirst, segments.Count - 1);
        if (tail > TaxiGraph.STAND_LEAD_IN_CHAIN_MAX_M) return null;
        double back = Math.Min(StretchMetres, last.Length / 2.0);
        double ahead = Math.Min(StretchMetres, tail);
        if (TurnOver(segments, last.Last, back, tailFirst, ahead) is not double d) return null;
        return Math.Abs(d) < StraightBelowDeg ? null : Words(d);
    }

    /// <summary>One taxiway run: its first and last NAMED segment (unnamed segments between two named segments of the
    /// same name belong to it) and its length from the first to the last.</summary>
    private readonly record struct Run(int First, int Last, double Length);

    private static List<Run> Runs(IReadOnlyList<TaxiRouteSegment> segments)
    {
        var runs = new List<Run>();
        string? name = null;
        int first = -1, last = -1;
        for (int i = 0; i < segments.Count; i++)
        {
            string current = segments[i].TaxiwayName;
            if (string.IsNullOrEmpty(current)) continue;
            if (name != null && current.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                last = i;
                continue;
            }
            if (name != null) runs.Add(new Run(first, last, Length(segments, first, last)));
            name = current;
            first = last = i;
        }
        if (name != null) runs.Add(new Run(first, last, Length(segments, first, last)));
        return runs;
    }

    /// <summary>The signed sum of the bearing changes over the stretch from <paramref name="back"/> metres before the
    /// end of segment <paramref name="incomingLast"/> to <paramref name="ahead"/> metres after the start of segment
    /// <paramref name="outgoingFirst"/>; null when fewer than two segments with a direction lie in it.</summary>
    private static double? TurnOver(IReadOnlyList<TaxiRouteSegment> s, int incomingLast, double back, int outgoingFirst, double ahead)
    {
        int start = incomingLast;
        double walked = s[start].DistanceMeters;
        while (walked < back && start > 0) walked += s[--start].DistanceMeters;
        int end = outgoingFirst;
        walked = s[end].DistanceMeters;
        while (walked < ahead && end < s.Count - 1) walked += s[++end].DistanceMeters;

        double? previous = null;
        double sum = 0;
        bool compared = false;
        for (int i = start; i <= end; i++)
        {
            if (s[i].DistanceMeters < DegenerateMetres) continue;
            if (previous is double p)
            {
                sum += Normalize(s[i].BearingDegrees - p);
                compared = true;
            }
            previous = s[i].BearingDegrees;
        }
        return compared ? sum : null;
    }

    private static double Length(IReadOnlyList<TaxiRouteSegment> s, int first, int last)
    {
        double metres = 0;
        for (int i = first; i <= last; i++) metres += s[i].DistanceMeters;
        return metres;
    }

    /// <summary>A bearing change folded into (-180, 180], right positive.</summary>
    private static double Normalize(double degrees)
    {
        degrees %= 360.0;
        if (degrees > 180.0) degrees -= 360.0;
        else if (degrees <= -180.0) degrees += 360.0;
        return degrees;
    }
}
