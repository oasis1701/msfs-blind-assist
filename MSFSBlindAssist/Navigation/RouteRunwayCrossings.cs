using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Builds the "crossing runway 10L twice" clause for the taxi route summary from
/// the route's hold-short-tagged segments.
///
/// Motivating incident (KSFO 2026-07-01): cleared "Q, hold short 10R" from a stop
/// on D between 28R/28L. The navdata (and the real airport) has no D→Q link between
/// the runways, so the only route onto Q re-crossed 28R twice. The route was correct
/// and both crossings were correctly hold-short-tagged — but the spoken summary said
/// only "2 hold short points", so the pilot had no idea the route would take them
/// back across the runway they had just vacated, perceived a "giant loop", and
/// doubted the "hold short of runway 10L" callouts. Naming the crossed runways in
/// the summary makes the route's shape audible up front.
///
/// Pure static (no graph, no manager state) so tools/ProgressiveTaxiProbe can
/// assert the composition rules.
/// </summary>
public static class RouteRunwayCrossings
{
    // Matches the runway designator inside every hold-short label shape the route
    // pipeline produces: "runway 10L", "runway 15R at N" (centerline naming),
    // "D5, Runway 22R" (threshold-fallback naming), "Runway 33L" (destination
    // truncation tag). "end of taxiway B" and bare holding-point names ("A5")
    // deliberately do not match — those are counted as plain hold-short points.
    private static readonly Regex RunwayToken = new(
        @"\brunway\s+([0-9]{1,2}[LRCW]?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extracts the bare runway designator ("10L") from a hold-short label, or
    /// null when the label doesn't name a runway.
    /// </summary>
    public static string? ExtractRunwayDesignator(string? holdShortLabel)
    {
        if (string.IsNullOrEmpty(holdShortLabel)) return null;
        var m = RunwayToken.Match(holdShortLabel);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Scans the hold-short-tagged segments and splits them into runway crossings
    /// (composed into a spoken clause) and plain hold-short points (returned as a
    /// count for the existing "N hold short points" wording).
    /// </summary>
    /// <param name="segments">The route's segments.</param>
    /// <param name="excludeLastSegment">
    /// True for runway destinations, where TruncateToHoldShort tags the final
    /// segment purely as an internal countdown rail — it is NOT an ATC crossing
    /// and must not be described as one (same exclusion the old count applied).
    /// </param>
    /// <returns>
    /// clause: "" when the route crosses no runway, else e.g.
    ///   "crossing runway 10L twice" / "crossing runways 04L, 04R and 27".
    /// nonRunwayHoldShorts: count of hold-short points whose label names no runway
    ///   (user checkbox holds, "end of taxiway X", bare holding-point names).
    /// </returns>
    public static (string clause, int nonRunwayHoldShorts) Describe(
        IReadOnlyList<TaxiRouteSegment> segments, bool excludeLastSegment)
    {
        // Designator → count, preserving first-encounter order so the clause
        // reads in taxi order ("04L, 04R and 27" at KBOS, not alphabetical).
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int nonRunway = 0;

        int end = segments.Count - (excludeLastSegment ? 1 : 0);
        for (int i = 0; i < end; i++)
        {
            var seg = segments[i];
            if (!seg.IsHoldShortPoint) continue;

            string? designator = ExtractRunwayDesignator(seg.HoldShortRunway);
            if (designator == null)
            {
                nonRunway++;
                continue;
            }
            if (counts.TryGetValue(designator, out int c))
            {
                counts[designator] = c + 1;
            }
            else
            {
                counts[designator] = 1;
                order.Add(designator);
            }
        }

        if (order.Count == 0) return ("", nonRunway);

        var parts = new List<string>();
        foreach (var d in order)
        {
            int c = counts[d];
            parts.Add(c switch
            {
                1 => d,
                2 => $"{d} twice",
                _ => $"{d} {c} times",
            });
        }

        string joined = parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        string noun = order.Count == 1 ? "runway" : "runways";
        return ($"crossing {noun} {joined}", nonRunway);
    }
}
