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
    /// <summary>
    /// Whether a centerline is the pavement named by <paramref name="designator"/> — matched
    /// on EITHER of its reciprocal designators through <see cref="NormalizeDesignator"/>, so
    /// 26R and 08L are one runway and "8L" and "08L" are one spelling.
    ///
    /// <para>THE by-designator comparison for centerlines. It exists because three sites grew
    /// their own — a raw <c>Equals</c> with no trim, a <c>Trim</c> with no zero-folding, and
    /// this normalized form — so a designator-format drift one site tolerated another silently
    /// rejected, invisibly until a particular airport's naming triggered it. Add a caller
    /// here rather than a fourth spelling elsewhere.</para>
    /// </summary>
    public static bool CenterlineHasDesignator(TaxiGraph.RunwayCenterline centerline, string? designator)
    {
        if (string.IsNullOrWhiteSpace(designator)) return false;
        string want = NormalizeDesignator(designator);
        return string.Equals(NormalizeDesignator(centerline.Name1 ?? ""), want, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeDesignator(centerline.Name2 ?? ""), want, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The centerline for <paramref name="designator"/>, or null when the graph does not
    /// carry it. Matched by <see cref="CenterlineHasDesignator"/>.
    /// </summary>
    public static TaxiGraph.RunwayCenterline? FindCenterlineForDesignator(
        IReadOnlyList<TaxiGraph.RunwayCenterline>? centerlines, string? designator)
    {
        if (centerlines is null || string.IsNullOrWhiteSpace(designator)) return null;
        foreach (var c in centerlines)
            if (CenterlineHasDesignator(c, designator))
                return c;
        return null;
    }

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
    /// Label policy for an auto-detected runway crossing's hold segment
    /// (<c>TaxiGuidanceManager.InsertRunwayCrossingHoldShorts</c>). Returns the
    /// label to write, or null to KEEP the existing label. Rules:
    ///  - empty → "runway {crossedRwy}";
    ///  - user "end of taxiway …" terminator label → keep (user intent wins);
    ///  - names no runway (bare DB holding-point name, e.g. "A5") → upgrade to
    ///    "runway {crossedRwy} at {name}" so callout + summary name the runway;
    ///  - names THIS pavement (same designator or reciprocal — user picks and
    ///    correct DB names) → keep;
    ///  - names a DIFFERENT pavement → the DB node was named for the wrong
    ///    runway (TaxiGraph's 150 m nearest-centerline naming can mis-bind
    ///    between closely spaced parallels); the geometric detection is the
    ///    truth here, so rewrite to "runway {crossedRwy}" — otherwise the
    ///    summary announces crossings of a runway the route never crosses and
    ///    the tactical callout names the wrong pavement.
    /// </summary>
    /// <param name="preferredDesignator">The designator this crossing should be ANNOUNCED
    /// under, when that is not the one geometry reported — i.e. the destination runway, on a
    /// route that crosses its own strip. TaxiGraph.Build names a hold node after whichever
    /// runway END is nearer it, so on the destination strip the DB label routinely carries
    /// the reciprocal; without this the "already names this pavement" rule below kept it and
    /// the pilot heard "hold short of runway 22R" while taxiing to 04L. The rewrite swaps
    /// only the designator TOKEN, so the hold point and the label's shape survive (both
    /// "runway 22R at D5" and "D5, Runway 22R" are Build outputs). Null/empty = no
    /// preference, and then every rule below behaves exactly as it always has.</param>
    public static string? ComposeCrossingLabel(
        string? existingLabel, string crossedRwy, string? preferredDesignator = null)
    {
        string announceAs = string.IsNullOrWhiteSpace(preferredDesignator)
            ? crossedRwy : preferredDesignator.Trim();

        if (string.IsNullOrEmpty(existingLabel)) return $"runway {announceAs}";
        // User intent always wins, on the destination's own strip as everywhere else.
        if (existingLabel.StartsWith("end of taxiway", StringComparison.OrdinalIgnoreCase))
            return null;
        string? named = ExtractRunwayDesignator(existingLabel);
        if (named == null) return $"runway {announceAs} at {existingLabel}";

        string want = NormalizeDesignator(announceAs);
        // Already announced under the designator we want (padding aside) — nothing to do.
        if (named.Equals(want, StringComparison.OrdinalIgnoreCase)) return null;

        string cross = NormalizeDesignator(crossedRwy);
        if (named.Equals(cross, StringComparison.OrdinalIgnoreCase) ||
            named.Equals(Reciprocal(cross), StringComparison.OrdinalIgnoreCase))
        {
            // Names THIS pavement, but not under the designator the pilot chose. With no
            // preference that is the scenery's own correct name and it is kept; with one,
            // swap just the designator token so the hold point rides along.
            if (string.IsNullOrWhiteSpace(preferredDesignator)) return null;
            return RunwayToken.Replace(
                existingLabel,
                m => m.Value.Replace(m.Groups[1].Value, announceAs), 1);
        }
        return $"runway {announceAs}";
    }

    /// <summary>
    /// How far back along the route the hold-short walk may look for the
    /// scenery's own hold line. Real hold lines sit 40-150 m from the
    /// centerline (FAA minimum 125 ft ≈ 38 m; CAT II/III lines further out),
    /// and the walk starts from a node already close to the pavement, so this
    /// budget reaches every realistic hold line while staying short enough
    /// that it cannot step back through a junction onto another taxiway's.
    /// </summary>
    public const double CrossingHoldLookbackMetres = 150.0;

    /// <summary>
    /// Picks the route segment to tag as the hold-short for a runway crossing:
    /// the last segment ending at the scenery's own hold-short node (navdata
    /// HS/HSND/IHS/IHSND → <see cref="TaxiNodeType.HoldShort"/> /
    /// <see cref="TaxiNodeType.ILSHoldShort"/>) before the crossing, falling
    /// back to the segment immediately before the crossing edge when no such
    /// node is within <see cref="CrossingHoldLookbackMetres"/>.
    ///
    /// Why: the crossing is detected as the edge that straddles the runway
    /// CENTERLINE, and a taxi network commonly carries nodes ON the pavement
    /// (the crossing taxiway meets the runway's own path at a centerline node),
    /// so "the node before the crossing edge" can sit INSIDE the runway. LEBL
    /// D5 over 24R (2026-08): nodes run 250 m → 105 m (HSND) → 51 m → 21 m →
    /// centerline, and the fallback rule held the aircraft 21 m from the
    /// centerline of a 60 m-wide runway — ~9 m inside the pavement edge, with
    /// the painted line 105 m back. KBOS taxiway C over 04R lands at 25 m with
    /// a 25 m half-width; the shape is not airport-specific.
    ///
    /// Safety property: the walk only ever moves the hold EARLIER along the
    /// route, so it can never place a hold closer to the runway than today's
    /// rule — a miss degrades to exactly the previous behaviour.
    ///
    /// Guards, in walk order:
    ///  - a hold node whose own <see cref="TaxiNode.HoldShortName"/> names a
    ///    DIFFERENT runway (reciprocal-aware) means the walk has stepped past a
    ///    junction onto another runway's hold line → stop, keep the fallback;
    ///  - an already-tagged hold-short segment means another hold line owns
    ///    that stretch → stop rather than merge two runways onto one stop point.
    ///
    /// The FIRST qualifying node wins, i.e. the one nearest the runway — the
    /// full-length hold, matching the default ATC clearance. The CAT III / ILS
    /// hold preference is a destination-runway opt-in (TruncateToHoldShort) and
    /// deliberately does not apply to crossings.
    ///
    /// Pure (segments in, index out) so the placement rule is unit-testable.
    /// </summary>
    /// <param name="segments">The route's segments.</param>
    /// <param name="crossingSegIndex">Index of the segment whose edge straddles the runway centerline.</param>
    /// <param name="crossedRunway">Designator of the runway being crossed (either end).</param>
    public static int ResolveCrossingHoldSegment(
        IReadOnlyList<TaxiRouteSegment> segments, int crossingSegIndex, string? crossedRunway)
    {
        if (segments == null || segments.Count == 0) return 0;

        // Always a VALID index — callers index straight into the route with it.
        int fallback = Math.Clamp(crossingSegIndex - 1, 0, segments.Count - 1);
        if (crossingSegIndex <= 0 || crossingSegIndex >= segments.Count)
            return fallback;

        string crossed = NormalizeDesignator(crossedRunway ?? "");
        string recip = Reciprocal(crossed);

        double walked = 0.0;
        for (int i = crossingSegIndex - 1; i >= 0; i--)
        {
            var seg = segments[i];

            // Never walk back THROUGH an existing hold-short (the fallback
            // segment itself may already carry this crossing's own tag).
            if (i < crossingSegIndex - 1 && seg.IsHoldShortPoint) break;

            var node = seg.ToNode;
            if (node != null &&
                (node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort))
            {
                string? guards = ExtractRunwayDesignator(node.HoldShortName);
                if (guards == null ||
                    guards.Equals(crossed, StringComparison.OrdinalIgnoreCase) ||
                    guards.Equals(recip, StringComparison.OrdinalIgnoreCase))
                    return i;
                break;
            }

            walked += seg.DistanceMeters;
            if (walked > CrossingHoldLookbackMetres) break;
        }

        return fallback;
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
        // All distinct SIGNED designators seen per merged pavement, encounter
        // order. The tactical callouts speak each crossing's own closer-end
        // label, so when one pavement is crossed near opposite ends the summary
        // must pre-announce BOTH names ("10L/28R") — "crossing runway 10L
        // twice" followed by a live "hold short of runway 28R" callout would
        // recreate the exact trust failure this clause exists to fix.
        var namesByKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
                if (!namesByKey[key].Contains(designator, StringComparer.OrdinalIgnoreCase))
                    namesByKey[key].Add(designator);
            }
            else
            {
                counts[key] = 1;
                namesByKey[key] = new List<string> { designator };
                order.Add(key);
            }
        }

        if (order.Count == 0) return ("", nonRunway);

        var parts = new List<string>();
        foreach (var d in order)
        {
            int c = counts[d];
            string name = string.Join("/", namesByKey[d]);   // "10L" or "10L/28R"
            parts.Add(c switch
            {
                1 => name,
                2 => $"{name} twice",
                _ => $"{name} {c} times",
            });
        }

        string joined = parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        string noun = order.Count == 1 ? "runway" : "runways";
        return ($"crossing {noun} {joined}", nonRunway);
    }

    /// <summary>
    /// Names the runway a taxi edge (a→b) crosses, or "" when it crosses none. The real
    /// implementation needs the graph's runway centrelines, so it is injected — that is what
    /// keeps <see cref="InsertCrossingHoldShorts"/> pure and testable.
    /// </summary>
    public delegate string EdgeRunwayProbe(double aLat, double aLon, double bLat, double bLon);

    /// <summary>
    /// Scans the route for segments whose edge crosses a runway centreline and tags the hold
    /// segment before each crossing, returning the runways crossed in route order.
    ///
    /// <para>FAA AIM 4-3-18 and ICAO Doc 4444: an aircraft must hold short of every runway it
    /// crosses, with explicit ATC clearance for each — controllers issue crossings one at a
    /// time. Without this pass a route taxis straight across an active runway with no pause.</para>
    ///
    /// <para>The destination runway is handled in two ways, and both halves are load-bearing:
    /// only the route's own ARRIVAL at it is skipped (the final segment, which
    /// <c>TruncateToHoldShort</c> already truncated to and tagged), because the crossing is
    /// named after whichever runway END is nearer the crossing point and a blanket
    /// name-equality skip dropped genuine mid-route crossings of the active runway; and a
    /// crossing of that strip is ANNOUNCED under the designator the pilot selected, not the
    /// reciprocal the geometry reported.</para>
    ///
    /// <para>Pure (segments + two delegates in, names out) so the composition is unit-testable;
    /// it was previously private on the manager and reachable only with a live graph.</para>
    /// </summary>
    public static IReadOnlyList<string> InsertCrossingHoldShorts(
        IReadOnlyList<TaxiRouteSegment> segments,
        string destinationName,
        EdgeRunwayProbe probe,
        Func<string, string, bool> designatorsMatch)
    {
        // A null delegate FAILS LOUDLY; a null route does not. The asymmetry is deliberate.
        // This pass is the FAA AIM 4-3-18 / ICAO Doc 4444 hold-short before every crossed
        // runway, and its empty result is indistinguishable from a route that genuinely
        // crosses nothing — so degrading a wiring error into "no crossings found" would
        // present it as a safe route. `designatorsMatch` in particular is only dereferenced
        // when the destination is non-empty, so without this guard a null could sit unnoticed
        // through every gate-destination route and surface only on a runway one.
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(designatorsMatch);

        var crossed = new List<string>();
        if (segments == null || segments.Count < 2) return crossed;

        // Tracks the runway whose hold-short was most recently inserted, so we don't tag every
        // consecutive segment that is on the same runway pavement.
        string lastTaggedRunway = "";

        // The destination arrives prefixed ("Runway 33L"); the crossed runway is a bare
        // designator ("33L"). Normalise so the exclusion below actually matches.
        string destBare = destinationName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? destinationName.Substring("Runway ".Length).Trim()
            : destinationName.Trim();

        for (int i = 1; i < segments.Count; i++)
        {
            var crossingSeg = segments[i];
            if (crossingSeg.FromNode == null || crossingSeg.ToNode == null) continue;

            string crossedRwy = probe(
                crossingSeg.FromNode.Latitude, crossingSeg.FromNode.Longitude,
                crossingSeg.ToNode.Latitude, crossingSeg.ToNode.Longitude);
            if (string.IsNullOrEmpty(crossedRwy)) continue;

            bool sameStripAsDestination = !string.IsNullOrEmpty(destBare) &&
                designatorsMatch(crossedRwy, destBare);
            if (sameStripAsDestination && i >= segments.Count - 1)
                continue;
            string? preferredRwy = sameStripAsDestination ? destBare : null;

            if (crossedRwy.Equals(lastTaggedRunway, StringComparison.OrdinalIgnoreCase))
                continue;

            var holdSeg = segments[ResolveCrossingHoldSegment(segments, i, crossedRwy)];
            holdSeg.IsHoldShortPoint = true;
            string? newLabel = ComposeCrossingLabel(holdSeg.HoldShortRunway, crossedRwy, preferredRwy);
            if (newLabel != null)
                holdSeg.HoldShortRunway = newLabel;
            lastTaggedRunway = crossedRwy;
            crossed.Add(crossedRwy);
        }

        return crossed;
    }
}
