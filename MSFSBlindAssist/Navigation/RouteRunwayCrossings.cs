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
        return m.Success ? NormalizeDesignator(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// Canonical designator form for comparisons and speech: trimmed, uppercase,
    /// runway number zero-padded to two digits ("9L" → "09L" — also the correct
    /// ATC phraseology, "runway zero nine left"). Non-runway designators
    /// (compass-point water runways "NE", taxiway-ish strings) pass through
    /// trimmed/uppercased. fs2024 navdata is consistently padded, but the DB
    /// ecosystem documents unpadded spellings (approach tables, third-party
    /// scenery) — every designator compare in this codebase must go through
    /// this so "9" and "09" can never silently fail to match.
    /// </summary>
    public static string NormalizeDesignator(string designator)
    {
        if (string.IsNullOrWhiteSpace(designator)) return designator ?? "";
        string d = designator.Trim().ToUpperInvariant();
        string suffix = "";
        if (d.Length > 1 && (d[^1] is 'L' or 'R' or 'C' or 'W') && char.IsDigit(d[0]))
        {
            suffix = d[^1..];
            d = d[..^1];
        }
        if (int.TryParse(d, out int num) && num >= 1 && num <= 36)
            return $"{num:D2}{suffix}";
        return designator.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Returns the reciprocal runway designator: adds 18 (mod 36, 1-based)
    /// and swaps L↔R suffix (C stays C, W stays W — fs2024 carries 1,166
    /// W-suffixed water-runway ends). "09" → "27", "27L" → "09R", "18W" → "36W".
    /// Input is normalized first, so "9" → "27". Returns
    /// <paramref name="designator"/> unchanged if it is blank or does
    /// not parse as a runway heading number. Shared by the crossing-clause
    /// reciprocal merge below, HoldShortNodeResolver's designated-node runway
    /// gate, and TaxiGuidanceManager.RunwayDesignatorsMatch.
    /// </summary>
    public static string Reciprocal(string designator)
    {
        if (string.IsNullOrWhiteSpace(designator)) return designator;
        string d = NormalizeDesignator(designator);
        string suffix = "";
        if (d.EndsWith("L"))      { suffix = "R"; d = d[..^1]; }
        else if (d.EndsWith("R")) { suffix = "L"; d = d[..^1]; }
        else if (d.EndsWith("C")) { suffix = "C"; d = d[..^1]; }
        else if (d.EndsWith("W")) { suffix = "W"; d = d[..^1]; }  // water runway
        if (!int.TryParse(d, out int num)) return designator;
        int recip = ((num - 1 + 18) % 36) + 1;  // 1-based 1–36; +18 mod 36
        return $"{recip:D2}{suffix}";
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
            // Reciprocal designators name the SAME pavement: the auto-detector
            // labels each crossing by its closer-end designator, so one runway
            // crossed near opposite ends arrives here as e.g. "10L" + "28R".
            // Merge onto the first-encountered designator — "crossing runways
            // 10L and 28R" would misstate one crossing-twice as two runways.
            string key = designator;
            if (!counts.ContainsKey(key))
            {
                string recip = Reciprocal(designator);
                if (counts.ContainsKey(recip)) key = recip;
            }
            if (counts.TryGetValue(key, out int c))
            {
                counts[key] = c + 1;
            }
            else
            {
                counts[key] = 1;
                order.Add(key);
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
