using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Runway hold-shorts along a taxi route: the automatic pass that holds before every runway entry
/// and crossing (<see cref="InsertRunwayHoldShorts"/>), the pilot's explicit per-row pick
/// (<see cref="ApplyUserRunwayHold"/>), where each hold goes (<see cref="ResolveHoldStop"/>), the
/// labels those stops carry, and the clause naming every runway a route crosses or enters
/// (<see cref="DescribeRunwayEvents"/>) — plus the designator helpers every runway comparison uses.
///
/// <para>Why the clause exists (KSFO 2026-07-01): a route whose only way onto Q re-crossed 28R was
/// summarised as "2 hold short points"; the pilot heard two unexplained hold-short callouts and read
/// a correct route as a giant loop. Rules and cases: docs/taxi-guidance.md, "Runway crossings and
/// entries".</para>
///
/// <para>Pure static (no graph, no manager state) so the rules are unit-testable and
/// tools/ProgressiveTaxiProbe can assert them.</para>
/// </summary>
public static class RouteRunwayCrossings
{
    // Matches the runway designator inside every hold-short label shape the route
    // pipeline produces: "runway 10L", "runway 15R at N" (centerline naming),
    // "D5, Runway 22R" (threshold-fallback naming), "Runway 33L" (destination
    // truncation tag). "end of taxiway B" and bare holding-point names ("A5")
    // deliberately do not match — those are counted as plain hold-short points.
    //
    // The COMPASS-POINT branch is not decoration: fs2024 carries 204 runway ends named
    // N/S/E/W/NE/NW/SE/SW, at 21 airports that also have taxi paths, so they reach the taxi
    // graph. Digits-only, this pattern read none of them — ExtractRunwayDesignator returned null,
    // ComposeCrossingLabel took its "names no runway" branch and prefixed an already-prefixed
    // label a second time, and live 3KS4 and RJSSE spoke "Stop. Hold short of runway N at
    // runway N."; LabelNamesOnlyRunway was false for every such label, so StripClearedCrossing
    // could never clear a Progressive Taxi crossing of one. The numeric branch is FIRST so a
    // designator that could match both is read as the number, and each compass alternative ends
    // at a word boundary so ordinary prose ("runway North side") is not a designator.
    // CultureInvariant beside IgnoreCase per CLAUDE.md: tr-TR folds the pattern letter I.
    private static readonly Regex RunwayToken = new(
        @"\brunway\s+([0-9]{1,2}[LRCW]?|N[EW]?|S[EW]?|[EW])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The opposite end of a compass-point runway. Keyed and returned uppercase; the numeric
    /// reciprocal in <see cref="Reciprocal"/> cannot serve these because they do not parse.
    /// </summary>
    private static readonly Dictionary<string, string> CompassReciprocals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["N"] = "S", ["S"] = "N", ["E"] = "W", ["W"] = "E",
        ["NE"] = "SW", ["SW"] = "NE", ["NW"] = "SE", ["SE"] = "NW",
    };

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
    /// Every runway designator a hold label names, normalized, in order, without repeats — a stop
    /// shared by two runways reads "runway 06 and runway 29".
    /// </summary>
    public static IReadOnlyList<string> ExtractRunwayDesignators(string? holdShortLabel)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(holdShortLabel)) return found;
        foreach (Match m in RunwayToken.Matches(holdShortLabel))
        {
            string d = NormalizeDesignator(m.Groups[1].Value);
            if (!found.Contains(d, StringComparer.OrdinalIgnoreCase)) found.Add(d);
        }
        return found;
    }

    /// <summary>
    /// True when the label names at least one runway and every runway it names is
    /// <paramref name="designator"/>'s pavement (either end). A stop shared with another runway is
    /// not "only" this runway's, so clearing this runway must not remove it.
    /// </summary>
    public static bool LabelNamesOnlyRunway(string? holdShortLabel, string designator)
    {
        var named = ExtractRunwayDesignators(holdShortLabel);
        if (named.Count == 0 || string.IsNullOrWhiteSpace(designator)) return false;
        string want = NormalizeDesignator(designator);
        string recip = Reciprocal(want);
        return named.All(d => d.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                              d.Equals(recip, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Drops the spoken "Runway " prefix a destination label carries ("Runway 33L" → "33L"),
    /// leaving a bare designator that <see cref="NormalizeDesignator"/> and the crossing
    /// comparisons can use. A label with no prefix is returned trimmed.
    ///
    /// <para>ONE owner: the same three-line idiom is spelled out at nine other sites
    /// (TaxiGraph.FindCenterlineByName, TaxiGuidanceManager.RouteEndIsRunwayHold, and the
    /// Substring(7) copies in TaxiGuidanceManager / Rollout / TaxiAssistForm). This is the
    /// seam they should adopt — a change to the destination-label format then lands once.</para>
    /// </summary>
    public static string StripRunwayPrefix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        // Trim BEFORE testing the prefix, not only after. Every copy of this idiom tested the
        // untrimmed string, so a label carrying a leading space came back with "Runway " still
        // attached — which then fails every designator comparison downstream. No current caller
        // passes one, so this changes nothing today and closes the trap for the sites that
        // adopt this helper later.
        string trimmed = name.Trim();
        return trimmed.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring("Runway ".Length).Trim()
            : trimmed;
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
        // Compass-point runways first: they never parse as a heading number, so without this the
        // numeric path below returns them unchanged and a stop labelled "runway N" could never be
        // recognised as the same pavement the pilot was cleared across as "S".
        if (CompassReciprocals.TryGetValue(d, out string? opposite)) return opposite;
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
    /// Label policy for the stop an automatic runway hold places
    /// (<see cref="InsertRunwayHoldShorts"/>). Returns the
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
    /// How far back from the runway the hold resolver may look for the scenery's own hold line. Real
    /// hold lines sit 40-150 m from the centerline (FAA minimum 125 ft ≈ 38 m; CAT II/III lines
    /// further out), so this reaches every realistic line while staying short enough that it cannot
    /// step back through a junction onto another taxiway's.
    /// </summary>
    public const double CrossingHoldLookbackMetres = 150.0;

    /// <summary>
    /// Whether a route's final hold-short is a runway destination's own countdown rail
    /// (<c>TruncateToHoldShort</c>) rather than a hold point the pilot should hear counted, for
    /// <see cref="CountNonRunwayHoldShorts"/>. A gate route never runs that pass, so a hold-short on
    /// its final segment is real.
    /// </summary>
    public static bool ShouldExcludeFinalHold(
        IReadOnlyList<TaxiRouteSegment> segments, bool isRunwayDestination)
        => isRunwayDestination && segments is { Count: > 0 } && segments[^1].IsHoldShortPoint;

    /// <summary>
    /// Where the aircraft stands, for the passes that judge a route from the aircraft's own position:
    /// the route's first point for classification, whether a stop is already passed
    /// (<see cref="RouteProgressMeters"/>), and whether it is clear of every runway. Callers with no
    /// position to offer (tests, the probe) pass null: classification then starts at node 0 and
    /// nothing is passed.
    /// </summary>
    /// <param name="MayStartHeld">
    /// False for a RECALCULATION, the one route built while the aircraft is already committed to
    /// where it is going (off-route detection fires only above <c>OFF_ROUTE_MIN_GS_KTS</c>): a start
    /// hold stops the aircraft where it stands, and on a recalc that is a stop the pilot never asked
    /// for, spoken on top of "Route changed". Every other adopter leaves it true — a landing-rollout
    /// route is refused a start hold by <see cref="RunwayWithinClearMargin"/> while the aircraft is
    /// still on or beside the pavement, and by <see cref="RouteProgressMeters"/> once it has rolled
    /// on. A ground-speed gate was tried here first (PR #243) and withdrawn: the manager's speed is 0
    /// on every fresh Calculate and live only mid-guidance, and 3 kt is the codebase's "stopped"
    /// line, not a "committed" one — a pilot re-importing a clearance at 8 kt thirty metres short of
    /// a runway can still stop, and lost the hold that told them to.
    /// </param>
    public readonly record struct AircraftPosition(double Lat, double Lon, bool MayStartHeld = true);

    /// <summary>
    /// How far past a stop point, measured along the route, the aircraft must be before no hold is
    /// placed there. Generous on purpose: a stop a few metres back is still effectively at the
    /// aircraft, and dropping it there would cost a legitimate safety stop.
    /// </summary>
    public const double StopPassedToleranceMetres = 10.0;

    /// <summary>
    /// How near a route segment the aircraft must be for its position to count as progress along it: one
    /// taxiway width, the same "travelling along this segment" bound as <c>TaxiGuidanceManager</c>'s
    /// SEGMENT_PASS_ADVANCE_MAX_CROSS_M. An aircraft further from every candidate has not joined the route,
    /// so nothing on it is passed.
    /// </summary>
    public const double RouteJoinMaxCrossTrackMetres = 30.0;

    // RouteProgressMeters' candidates: segments starting within this route distance of node 0. A route
    // is adopted from the aircraft's own position, so its far legs are never where the aircraft is.
    private const double ProgressSearchMetres = 200.0;

    /// <summary>
    /// How far along the route the aircraft is: its projection onto the nearest segment (clamped
    /// perpendicular distance, ties to the earlier segment) among those starting within 200 m of the
    /// route start and within <see cref="RouteJoinMaxCrossTrackMetres"/> of the aircraft, as that segment's
    /// route distance plus the clamped distance along it; 0 when no segment qualifies. Projection as
    /// <see cref="RunwayShape"/>. A stop counts as passed only when its own route distance is more than
    /// <see cref="StopPassedToleranceMetres"/> behind this. Never judge "passed" on a hold segment's own
    /// axis, which read holds more than a kilometre ahead as behind an aircraft still at its stand, nor
    /// without the cross-track bound, which read an aircraft waiting at a hold line beside a route along the
    /// runway as already on it (docs/taxi-guidance.md, "Runway crossings and entries").
    /// </summary>
    public static double RouteProgressMeters(IReadOnlyList<TaxiRouteSegment> segments, double lat, double lon)
    {
        if (segments is null) return 0.0;
        double progress = 0.0, nearest = double.MaxValue, start = 0.0;
        for (int i = 0; i < segments.Count && (i == 0 || start <= ProgressSearchMetres); i++)
        {
            var seg = segments[i];
            if (seg?.FromNode != null && seg.ToNode != null)
            {
                var (along, perpendicular) = ProjectOntoSegment(seg.FromNode, seg.ToNode, lat, lon);
                if (perpendicular <= RouteJoinMaxCrossTrackMetres && perpendicular < nearest)
                {
                    nearest = perpendicular;
                    progress = start + along;
                }
            }
            start += SegmentLengthMeters(seg);
        }
        return progress;
    }

    /// <summary>Route distance of node <paramref name="nodeIndex"/>: the lengths of segments 0..nodeIndex-1.</summary>
    private static double RouteDistanceToNode(IReadOnlyList<TaxiRouteSegment> segments, int nodeIndex)
    {
        double distance = 0.0;
        for (int i = 0; i < nodeIndex && i < segments.Count; i++) distance += SegmentLengthMeters(segments[i]);
        return distance;
    }

    private static double SegmentLengthMeters(TaxiRouteSegment? segment)
        => segment?.FromNode == null || segment.ToNode == null
            ? 0.0
            : TaxiGraph.FastDistanceMeters(segment.FromNode.Latitude, segment.FromNode.Longitude,
                segment.ToNode.Latitude, segment.ToNode.Longitude);

    // Equirectangular from the segment's start: 111,132 m per degree of latitude, cos(mid-latitude) for longitude.
    private static (double Along, double Perpendicular) ProjectOntoSegment(TaxiNode from, TaxiNode to, double lat, double lon)
    {
        const double MetersPerDegLat = 111132.0;
        double metersPerDegLon = MetersPerDegLat * Math.Cos((from.Latitude + to.Latitude) * 0.5 * (Math.PI / 180.0));
        double bx = (to.Longitude - from.Longitude) * metersPerDegLon;
        double by = (to.Latitude - from.Latitude) * MetersPerDegLat;
        double px = (lon - from.Longitude) * metersPerDegLon;
        double py = (lat - from.Latitude) * MetersPerDegLat;
        double length = Math.Sqrt(bx * bx + by * by);
        if (length <= 0.0) return (0.0, Math.Sqrt(px * px + py * py));
        double along = Math.Clamp((px * bx + py * by) / length, 0.0, length);
        double dx = px - bx * along / length, dy = py - by * along / length;
        return (along, Math.Sqrt(dx * dx + dy * dy));
    }

    /// <summary>
    /// Label for a stop point that is ALREADY a hold-short, when another runway's hold resolves to
    /// it: the label names both runways, in route order. Returns null to keep the existing label
    /// (it already names this pavement, or it is the pilot's own "end of taxiway" stop).
    /// </summary>
    public static string? ComposeSharedLabel(string? existingLabel, string designator)
    {
        if (string.IsNullOrEmpty(existingLabel)) return $"runway {designator}";
        if (existingLabel.StartsWith("end of taxiway", StringComparison.OrdinalIgnoreCase)) return null;
        var named = ExtractRunwayDesignators(existingLabel);
        if (named.Count == 0) return ComposeCrossingLabel(existingLabel, designator);
        string want = NormalizeDesignator(designator);
        string recip = Reciprocal(want);
        if (named.Any(d => d.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                           d.Equals(recip, StringComparison.OrdinalIgnoreCase)))
            return null;
        return $"{existingLabel} and runway {designator}";
    }

    /// <summary>The one hold-short sentence, for a hold reached en route and for a start hold.</summary>
    public static string ComposeHoldShortInstruction(string? holdShortLabel)
        => string.IsNullOrEmpty(holdShortLabel)
            ? "Stop. Hold short. Press continue when cleared."
            : $"Stop. Hold short of {holdShortLabel}. Press continue when cleared.";

    /// <summary>
    /// The route summary / "Route changed" clause: every crossing, then every entry, each group in
    /// taxi order, reciprocal designators merged as one pavement speaking both names, repeats
    /// counted ("crossing runway 10L/28R twice, entering runway 04L"). Built from the recorded
    /// events, not from hold labels, so a crossing that could not be held is still named.
    ///
    /// <para>A runway the pass could NOT hold for is then named again in a trailing warning
    /// ("…, with no hold short point for runway 26R"). Without it an unheld crossing was worded
    /// EXACTLY like a held one: the pilot was told the route crosses 26R, waited for the
    /// "Stop. Hold short of runway 26R" that the tactical callouts would never speak, and rolled
    /// across. <see cref="TaxiRouteRunwayEvent.Held"/> reached only the diagnostic log line.</para>
    /// </summary>
    public static string DescribeRunwayEvents(IReadOnlyList<TaxiRouteRunwayEvent>? events)
    {
        if (events is null || events.Count == 0) return "";
        var parts = new List<string>();
        string crossing = ComposeRunwayGroup("crossing",
            events.Where(e => e.Kind == RunwayEventKind.Crossing).Select(e => e.Designator));
        string entering = ComposeRunwayGroup("entering",
            events.Where(e => e.Kind == RunwayEventKind.Entry).Select(e => e.Designator));
        if (crossing.Length > 0) parts.Add(crossing);
        if (entering.Length > 0) parts.Add(entering);
        if (parts.Count == 0) return "";
        string unheld = ComposeUnheldWarning(events);
        if (unheld.Length > 0) parts.Add(unheld);
        return string.Join(", ", parts);
    }

    /// <summary>
    /// "with no hold short point for runway 26R" / "… for runways 09L and 04L" — each pavement
    /// named ONCE however many of its passages went unheld, reciprocals merged onto the first name
    /// seen (the same pavement identity <see cref="ComposeRunwayGroup"/> uses). Empty when every
    /// passage was held, which is the overwhelmingly common route and must gain no extra words.
    /// </summary>
    private static string ComposeUnheldWarning(IReadOnlyList<TaxiRouteRunwayEvent> events)
    {
        var names = new List<string>();
        foreach (var e in events)
        {
            if (e.Held || string.IsNullOrWhiteSpace(e.Designator)) continue;
            string designator = NormalizeDesignator(e.Designator);
            string reciprocal = Reciprocal(designator);
            if (names.Any(n => n.Equals(designator, StringComparison.OrdinalIgnoreCase)
                            || n.Equals(reciprocal, StringComparison.OrdinalIgnoreCase)))
                continue;
            names.Add(designator);
        }
        if (names.Count == 0) return "";
        string joined = names.Count == 1
            ? names[0]
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        return $"with no hold short point for {(names.Count == 1 ? "runway" : "runways")} {joined}";
    }

    private static string ComposeRunwayGroup(string verb, IEnumerable<string> designators)
    {
        // Designator key → count, first-encounter order; reciprocals merge onto the first-seen key
        // and keep every signed name, because the tactical callouts speak each stop's own label.
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var namesByKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in designators)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string designator = NormalizeDesignator(raw);
            string key = designator;
            if (!counts.ContainsKey(key) && counts.ContainsKey(Reciprocal(designator)))
                key = Reciprocal(designator);
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
        if (order.Count == 0) return "";

        var parts = order.Select(key =>
        {
            string name = string.Join("/", namesByKey[key]);
            return counts[key] switch { 1 => name, 2 => $"{name} twice", var n => $"{name} {n} times" };
        }).ToList();
        string joined = parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        return $"{verb} {(order.Count == 1 ? "runway" : "runways")} {joined}";
    }

    /// <summary>
    /// Hold-short points whose label names no runway (end of taxiway, bare holding-point names) —
    /// the "N hold short points" count. <paramref name="excludeLastSegment"/> drops a runway
    /// destination's own countdown rail (see <see cref="ShouldExcludeFinalHold"/>).
    /// </summary>
    public static int CountNonRunwayHoldShorts(IReadOnlyList<TaxiRouteSegment> segments, bool excludeLastSegment)
    {
        if (segments is null) return 0;
        int end = segments.Count - (excludeLastSegment ? 1 : 0);
        int count = 0;
        for (int i = 0; i < end; i++)
            if (segments[i].IsHoldShortPoint && ExtractRunwayDesignator(segments[i].HoldShortRunway) == null)
                count++;
        return count;
    }

    /// <summary>The "Route crossings:" line written once per route adopted.</summary>
    public static string DescribeForLog(string phase, string destinationName, TaxiRoute route)
    {
        static string ListOrNone(IEnumerable<string> items)
        {
            string joined = string.Join(",", items);
            return joined.Length > 0 ? joined : "(none)";
        }
        var events = route.RunwayEvents ?? new List<TaxiRouteRunwayEvent>();
        string startHold = route.StartHoldRunway is null ? "(none)" : $"\"{route.StartHoldRunway}\"";
        return $"Route crossings: phase={phase} dest=\"{destinationName}\" segments={route.Segments.Count} " +
               $"crosses={ListOrNone(events.Where(e => e.Kind == RunwayEventKind.Crossing).Select(e => e.Designator))} " +
               $"enters={ListOrNone(events.Where(e => e.Kind == RunwayEventKind.Entry).Select(e => e.Designator))} " +
               $"unheld={ListOrNone(events.Where(e => !e.Held).Select(e => e.Designator))} " +
               $"startHold={startHold}";
    }

    /// <summary>
    /// A resolved stop point: a node index from segment 0. 0 = the start node (a start hold, which for a
    /// passage entered from the aircraft is where the aircraft stands); -1 = NO stop — walk 2 met an
    /// earlier stretch of THIS SAME runway's own pavement before finding a clear node, or crossed
    /// another runway's pavement without reaching an existing stop, so nothing is a safe place to hold
    /// for this passage (see <see cref="ResolveHoldStop"/>). <see cref="SharesExistingStop"/> is only
    /// meaningful for a NodeIndex &gt;= 0 and is false for the -1 case.
    /// </summary>
    public readonly record struct HoldStop(int NodeIndex, bool SharesExistingStop);

    /// <summary>
    /// Where the hold for one runway entry or crossing goes. A passage whose entry is the aircraft itself
    /// (<c>EntryIndex</c> -1: classified with the aircraft as the route's first point, see
    /// <see cref="InsertRunwayHoldShorts"/>) stops where the aircraft stands, the start node. Otherwise
    /// the walk starts at the first node on the runway (for an edge that jumps across it, at the last
    /// clear node) and goes back toward the start of the route:
    ///  1. the first scenery hold node (HS/HSND/IHS/IHSND) within <see cref="CrossingHoldLookbackMetres"/>
    ///     whose name names this runway, its reciprocal or no runway, and which is off the pavement of this
    ///     runway and of every other — a hold node on either is skipped (at the walk's own starting node
    ///     too), one naming a different runway ends this search, and
    ///     (at a node strictly before this walk's own starting node) a node ON this SAME runway's own
    ///     pavement or on another runway's also ends this search — this runway's pavement belongs to an
    ///     earlier entry or crossing of it, and nothing behind it can be reached without a stop this
    ///     side of it;
    ///  2. otherwise the nearest node at or before the entry that is clear of the runway
    ///     (<see cref="RunwayShape.IsClearOf"/>) — but checked FIRST, before the clear-node and
    ///     existing-stop tests: a node ON this same runway's own pavement ends this walk with NO STOP
    ///     (<see cref="HoldStop"/> with <c>NodeIndex</c> -1). Sharing an earlier crossing's stop across
    ///     that pavement would place THIS crossing's hold on the far side of the runway from where it
    ///     needs to stop, so this passage is reported unheld instead. A node on ANOTHER runway's
    ///     pavement is never a stop: it is skipped, and behind it only an existing stop may be used
    ///     (shared, its label naming both runways), never a clear node;
    ///  3. otherwise the start node — reached only when walk 2 runs out of nodes without ever meeting
    ///     this runway's own pavement or another runway's; a walk 2 that met another runway's pavement
    ///     and found no existing stop ends in a -1 instead.
    /// Neither walk passes a segment that is already a hold-short: reaching one, or (in walk 2)
    /// reaching this runway's own pavement, ends the walk before a clear node is found — the existing
    /// hold-short is shared; this runway's own pavement leaves no stop at all. The one exception is an
    /// existing stop on ANOTHER runway's pavement (in practice a pilot's "end of taxiway" stop): like
    /// every node there it is never a stop for this runway, so walk 2 passes it and may still share an
    /// existing stop behind it.
    ///
    /// <para>Every candidate is at or before the runway and off the pavement of every runway on the
    /// pass's list, so a stop can only move EARLIER than the old "segment before the crossing edge",
    /// never later — and a runway met twice close together (KSFO Q) can never have its second crossing
    /// borrow a stop from across the first crossing's own pavement. Cases and history:
    /// docs/taxi-guidance.md, "Runway crossings and entries".</para>
    /// </summary>
    /// <param name="passage">Classified from segment 0 (its indices are node indices of the whole route).</param>
    /// <param name="otherRunways">The shapes of every other runway on the pass's list (null: none).</param>
    /// <param name="startHoldPlaced">
    /// True when an earlier passage already resolved to a START HOLD (<see cref="TaxiRoute.StartHoldRunway"/>).
    /// A start hold tags NO segment, so <see cref="IsExistingStop"/> — which reads
    /// <c>segments[nodeIndex - 1]</c> and is therefore false at node 0 by construction — cannot see it.
    /// Without this, a second runway whose walk back meets the first runway's pavement latched
    /// <c>crossedOther</c>, found no existing stop to share, and returned -1: the pilot heard only
    /// "hold short of runway A", pressed Continue, and crossed BOTH runways with no callout for B.
    /// The start hold sits before every node on the route, so it is always a safe stop to share.
    /// </param>
    public static HoldStop ResolveHoldStop(
        IReadOnlyList<TaxiRouteSegment> segments, RunwayPassage passage,
        IReadOnlyList<RunwayShape>? otherRunways = null, bool startHoldPlaced = false)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(passage);

        // The aircraft itself is the last point clear of the runway: the stop is where it stands.
        if (passage.EntryIndex < 0) return new HoldStop(0, false);

        var shape = passage.Shape;
        var others = otherRunways ?? Array.Empty<RunwayShape>();
        int walkStart = passage.FirstOnIndex >= 0 ? passage.FirstOnIndex : passage.EntryIndex;

        double walked = 0.0;
        for (int k = walkStart; k >= 0; k--)
        {
            if (k < walkStart) walked += segments[k].DistanceMeters;
            if (walked > CrossingHoldLookbackMetres) break;

            var node = NodeAt(segments, k);
            // Projected ONCE per node: walk 1 asks two different questions of the same point (is it
            // on the pavement, and is a hold line here usable) and used to re-project for the second.
            (double Along, double Lateral) at = node == null
                ? (0.0, 0.0)
                : shape.Project(node.Latitude, node.Longitude);
            if (node != null && k < walkStart)
            {
                if (shape.ContainsAlongLateral(at.Along, at.Lateral, 0.0)
                    || IsOnAnyRunway(others, node)) break;
            }
            if (node != null && (node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort))
            {
                var guards = ExtractRunwayDesignators(node.HoldShortName);
                if (guards.Count > 0 && !guards.Any(d => CenterlineHasDesignator(passage.Runway, d)))
                    break;
                // Never a hold line on another runway's pavement — also at the walk's own starting node, which
                // the pavement break above does not test. The BARE half-width (margin 0) is deliberate and
                // is an owner ruling: a painted line hugging the pavement edge must stay usable (SC99's is
                // 7.2 m out on a 4.0 m half-width), where walk 2 below, which invents a stop of its own,
                // demands the full clear margin. Extent-aware since PR #238 §3 — an on-axis scenery hold
                // line BEYOND the runway end used to read as a node on the pavement and be rejected.
                if (shape.IsClearOfAt(at.Along, at.Lateral, 0.0)
                    && !IsOnAnyRunway(others, node))
                    return new HoldStop(k, IsExistingStop(segments, k));
            }
            if (IsExistingStop(segments, k)) break;
        }

        bool crossedOther = false;
        for (int k = passage.EntryIndex; k >= 0; k--)
        {
            var node = NodeAt(segments, k);
            // Node 0 carries a stop when an earlier passage started the route held — see startHoldPlaced.
            bool existing = IsExistingStop(segments, k) || (k == 0 && startHoldPlaced);
            if (node != null)
            {
                var (along, lateral) = shape.Project(node.Latitude, node.Longitude);
                if (shape.ContainsAlongLateral(along, lateral, 0.0)) return new HoldStop(-1, false);
                if (IsOnAnyRunway(others, node))
                {
                    crossedOther = true;   // never a stop; behind it only an existing stop may be shared
                    continue;
                }
                // Extent-aware (PR #238 §3): a node beyond the runway's along-track extent but near
                // its axis used to be neither "on the runway" (Contains bounds the extent) nor
                // "clear of" it (IsClearOf tested |lateral| only), so this walk stepped over it and
                // every node behind it and fell through to a START hold — "Stop. Hold short of
                // runway 09" before moving, hundreds of metres from the real hold line, with no hold
                // where the route actually meets the pavement.
                if (!crossedOther
                    && shape.IsClearOfAt(along, lateral, RolloutExitGate.RunwayClearMarginM))
                    return new HoldStop(k, existing);
            }
            if (existing) return new HoldStop(k, true);
        }

        return crossedOther ? new HoldStop(-1, false) : new HoldStop(0, false);
    }

    private static TaxiNode? NodeAt(IReadOnlyList<TaxiRouteSegment> segments, int nodeIndex)
        => nodeIndex == 0 ? segments[0]?.FromNode : segments[nodeIndex - 1]?.ToNode;

    private static bool IsExistingStop(IReadOnlyList<TaxiRouteSegment> segments, int nodeIndex)
        => nodeIndex >= 1 && segments[nodeIndex - 1].IsHoldShortPoint;

    private static bool IsOnAnyRunway(IReadOnlyList<RunwayShape> shapes, TaxiNode node)
    {
        foreach (var shape in shapes)
            if (shape.Contains(node.Latitude, node.Longitude, 0.0)) return true;
        return false;
    }

    /// <summary>
    /// The runway the aircraft is on or within <see cref="RolloutExitGate.RunwayClearMarginM"/> of —
    /// by <see cref="RunwayShape.IsClearOfAt"/>, so along the axis past an end as well as laterally —
    /// else null. The start hold's "not on a runway" test, asked by the pass and by
    /// <c>TaxiGuidanceManager</c>'s per-frame start-hold entry: a stop where the aircraft stands is
    /// only safe where the pass would itself place a stop (PR #243 review).
    /// </summary>
    public static TaxiGraph.RunwayCenterline? RunwayWithinClearMargin(
        IReadOnlyList<TaxiGraph.RunwayCenterline>? runways, double lat, double lon)
    {
        if (runways is null) return null;
        foreach (var rwy in runways)
        {
            var shape = RunwayShape.For(rwy);
            if (shape.IsDegenerate) continue;
            var (along, lateral) = shape.Project(lat, lon);
            if (!shape.IsClearOfAt(along, lateral, RolloutExitGate.RunwayClearMarginM)) return rwy;
        }
        return null;
    }

    /// <summary>
    /// The first runway whose pavement (no margin) holds the point, or null.
    /// </summary>
    internal static TaxiGraph.RunwayCenterline? RunwayUnder(
        IEnumerable<TaxiGraph.RunwayCenterline>? runways, double lat, double lon)
    {
        if (runways is null) return null;
        foreach (var runway in runways)
            if (runway != null && RunwayShape.For(runway).Contains(lat, lon, 0.0))
                return runway;
        return null;
    }

    /// <summary>
    /// The automatic runway hold-short pass (FAA AIM 4-3-18 / ICAO Doc 4444): classifies the route
    /// against every runway, places one hold per entry or crossing and records every one on
    /// <see cref="TaxiRoute.RunwayEvents"/>, held or not.
    ///
    /// <para>On the destination strip only the route's own ARRIVAL (an entry that ends on it) is
    /// skipped; every other entry or crossing of that strip is held and announced under the
    /// designator the pilot selected. A stop the aircraft has already passed, or a start hold on a
    /// recalculated route, is recorded as not held and not placed — both would command a stop on the
    /// pavement.</para>
    ///
    /// <para>With <paramref name="aircraft"/> the aircraft is the route's first point while it has not rolled
    /// along the route: the route is classified with it prepended, so a route whose first node is already on
    /// the runway still meets it from where the aircraft stands, and a start hold is a stop at the aircraft —
    /// never set while it stands on any runway's pavement. A stop it has rolled more than
    /// <see cref="StopPassedToleranceMetres"/> past, along the route (<see cref="RouteProgressMeters"/>),
    /// is not placed.</para>
    /// </summary>
    /// <param name="destinationName">The runway destination as spoken ("Runway 33L"), or "" for other routes.</param>
    /// <param name="aircraft">
    /// Where the aircraft stands, and whether this route may start held at all. Null (tests, the
    /// probe): classification starts at node 0, nothing is passed and a start hold is allowed. There
    /// is no <c>allowStartHold</c> flag any more — see <see cref="AircraftPosition.MayStartHeld"/>
    /// and PR #238 deferred finding §2.
    /// </param>
    public static IReadOnlyList<TaxiRouteRunwayEvent> InsertRunwayHoldShorts(
        TaxiRoute route,
        IReadOnlyList<TaxiGraph.RunwayCenterline> runways,
        string destinationName,
        AircraftPosition? aircraft = null)
    {
        // A null runway list is a wiring error; returning "no runways met" would present it as a
        // safe route, so it fails loudly. A null or empty route legitimately meets nothing.
        ArgumentNullException.ThrowIfNull(runways);
        if (route is null) return Array.Empty<TaxiRouteRunwayEvent>();
        route.RunwayEvents = new List<TaxiRouteRunwayEvent>();
        if (route.Segments.Count == 0 || runways.Count == 0) return route.RunwayEvents;

        string destBare = StripRunwayPrefix(destinationName);
        var nodes = ClassificationNodes(route, aircraft, out bool aircraftPrepended);
        var passages = RunwayRouteClassifier.ClassifyAll(nodes, runways)
            .Select(p => ToRouteIndices(p, aircraftPrepended))
            .ToList();
        foreach (var passage in passages)
        {
            bool destinationStrip = destBare.Length > 0 && CenterlineHasDesignator(passage.Runway, destBare);
            if (destinationStrip && passage.Kind == RunwayEventKind.Entry && passage.ExitIndex < 0)
                continue;

            string? preferred = destinationStrip ? destBare : null;
            bool held = PlaceHold(route, passage, preferred, userLabel: null,
                runways, aircraft, out string announcedDesignator);
            route.RunwayEvents.Add(new TaxiRouteRunwayEvent
            {
                Kind = passage.Kind,
                // The designator the STOP actually announces, not necessarily preferred/passage's own —
                // a shared stop can already name a different runway first, and a kept scenery label can
                // carry the reciprocal end from the one the crossing geometry reported. See PlaceHold.
                Designator = announcedDesignator,
                Held = held,
            });
        }
        ReorderSharedLabels(route, passages);
        return route.RunwayEvents;
    }

    /// <summary>
    /// A shared stop's label ("runway 09 and runway 01") in ROUTE order — the runway the aircraft
    /// meets first named first — whichever pass placed which half. <see cref="ComposeSharedLabel"/>
    /// appends in PLACEMENT order, and the pilot's explicit picks are placed BEFORE this pass runs, so
    /// a picked runway always came first even when the route crossed the other one first; the staged
    /// hold (<see cref="RunwayHoldStages"/>) reads the label in order and would then ask for the far
    /// runway's clearance while the aircraft sat at the near one (PR #243 review). Only a label that
    /// is nothing but a runway list is touched; a kept scenery label is left as the scenery wrote it.
    /// </summary>
    private static void ReorderSharedLabels(TaxiRoute route, IReadOnlyList<RunwayPassage> passages)
    {
        if (passages.Count < 2) return;
        // Route order of a passage: the node it reaches, then how far along the edge before it the
        // route meets the centerline — two runways met on ONE edge share a reach index and only the
        // meet point tells them apart. Same pavement both ends, first passage wins.
        var order = new Dictionary<string, (int Reach, double Meet)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in passages)
        {
            double meet = 0.0;
            var entry = NodeAt(route.Segments, Math.Max(0, p.EntryIndex));
            var next = NodeAt(route.Segments, Math.Max(0, p.EntryIndex) + 1);
            if (entry != null && next != null) meet = ProjectOntoSegment(entry, next, p.MeetLat, p.MeetLon).Along;
            string d = NormalizeDesignator(p.Designator);
            if (!order.ContainsKey(d)) order[d] = (p.ReachIndex, meet);
            string r = Reciprocal(d);
            if (!order.ContainsKey(r)) order[r] = (p.ReachIndex, meet);
        }
        (int, double) OrderOf(string raw) =>
            order.TryGetValue(NormalizeDesignator(raw), out var k) ? k : (int.MaxValue, 0.0);

        route.StartHoldRunway = ReorderSharedLabel(route.StartHoldRunway, OrderOf);
        foreach (var seg in route.Segments)
            if (seg.IsHoldShortPoint) seg.HoldShortRunway = ReorderSharedLabel(seg.HoldShortRunway, OrderOf);
    }

    // Exactly the shape ComposeSharedLabel builds: "runway X and runway Y[ and runway Z...]".
    private static readonly Regex SharedRunwayList = new(
        @"^runway\s+\S+(?:\s+and\s+runway\s+\S+)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SharedRunwayToken = new(
        @"runway\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string? ReorderSharedLabel(string? label, Func<string, (int Reach, double Meet)> orderOf)
    {
        if (string.IsNullOrEmpty(label) || !SharedRunwayList.IsMatch(label)) return label;
        var tokens = SharedRunwayToken.Matches(label).Select(m => m.Groups[1].Value).ToList();
        // Stable: two runways met at the same reach keep the order they were written in.
        var sorted = tokens.Select((t, i) => (t, i)).OrderBy(x => orderOf(x.t)).ThenBy(x => x.i).Select(x => x.t);
        return string.Join(" and ", sorted.Select(t => $"runway {t}"));
    }

    /// <summary>What became of an explicit per-row runway pick (<see cref="ApplyUserRunwayHold"/>).</summary>
    public enum UserRunwayHoldResult
    {
        /// <summary>The route neither enters nor crosses the runway at or after the taxiway's run.</summary>
        NotOnRoute,
        /// <summary>It does, but no stop could be placed (already passed, no safe stop, start hold refused).</summary>
        NotHeld,
        Held,
    }

    /// <summary>
    /// The pilot's explicit "hold short of runway X" pick for one taxiway row: honoured when the route
    /// enters or crosses X at or after <paramref name="runStartSegmentIndex"/> (the start of that
    /// taxiway's run, which is also the index of its first node), placed by the same resolver as the
    /// automatic pass, labelled "runway X" as the pilot typed it — also at a stop already carrying the
    /// pilot's own "end of taxiway" label, since the pick names the runway.
    /// </summary>
    /// <param name="runways">Every runway of the airport: none may carry the stop, and no start hold is set while the aircraft stands on one.</param>
    /// <param name="aircraft">As for <see cref="InsertRunwayHoldShorts"/>.</param>
    public static UserRunwayHoldResult ApplyUserRunwayHold(
        TaxiRoute route,
        TaxiGraph.RunwayCenterline runway,
        IReadOnlyList<TaxiGraph.RunwayCenterline> runways,
        string runwayId,
        int runStartSegmentIndex,
        AircraftPosition? aircraft = null)
        => ApplyUserRunwayHold(route, runway, runways, runwayId, runStartSegmentIndex, out _, aircraft);

    /// <summary>
    /// As above, reporting the passage the pick bound to so the caller can MERGE it into the route's
    /// recorded events (<see cref="MergeUserPickEvents"/>).
    /// </summary>
    /// <param name="placed">
    /// The event for an honoured pick, else null. PR #238 deferred finding §7: this pass recorded no
    /// event at all and <see cref="InsertRunwayHoldShorts"/> then RESETS
    /// <see cref="TaxiRoute.RunwayEvents"/>, so when the automatic pass skips the same passage — the
    /// destination-strip arrival skip — the pick was named NOWHERE. <see cref="DescribeRunwayEvents"/>
    /// said nothing and <see cref="CountNonRunwayHoldShorts"/> skipped it too, because its label DOES
    /// name a runway: the pilot picked "hold short of runway 04R" on a route to 04R, heard no mention
    /// of it in the summary, and was then stopped by a hold they were never told about.
    /// </param>
    public static UserRunwayHoldResult ApplyUserRunwayHold(
        TaxiRoute route,
        TaxiGraph.RunwayCenterline runway,
        IReadOnlyList<TaxiGraph.RunwayCenterline> runways,
        string runwayId,
        int runStartSegmentIndex,
        out TaxiRouteRunwayEvent? placed,
        AircraftPosition? aircraft = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(runway);
        ArgumentNullException.ThrowIfNull(runways);

        placed = null;
        var nodes = ClassificationNodes(route, aircraft, out bool aircraftPrepended);
        var passage = RunwayRouteClassifier.Classify(nodes, RunwayShape.For(runway))
            .Select(p => ToRouteIndices(p, aircraftPrepended))
            .FirstOrDefault(p => p.ReachIndex >= runStartSegmentIndex);
        if (passage is null) return UserRunwayHoldResult.NotOnRoute;

        string pick = runwayId.Trim();
        if (!PlaceHold(route, passage, preferred: pick, userLabel: $"runway {pick}",
                runways, aircraft, out string announcedDesignator))
            return UserRunwayHoldResult.NotHeld;

        placed = new TaxiRouteRunwayEvent
        {
            Kind = passage.Kind,
            Designator = announcedDesignator,
            Held = true,
        };
        return UserRunwayHoldResult.Held;
    }

    /// <summary>
    /// Adds each honoured explicit pick's event to <paramref name="route"/>'s recorded events unless
    /// the automatic pass already recorded that passage — the same runway (either end) met the same
    /// way.
    ///
    /// <para>PR #238 deferred finding §7. The automatic pass owns the events and RESETS them, so a
    /// pick it skips has to be merged back in afterwards. The destination-strip arrival skip itself
    /// is deliberately untouched: it has its own incident history (a blanket same-runway skip once
    /// dropped genuine mid-route crossings of the active runway, 2026-08-24) and must keep skipping
    /// ONLY the route's own final arrival.</para>
    ///
    /// <para>The de-duplication is by runway AND kind, not by runway alone: the same pavement met
    /// twice, once crossed and once entered, is two passages and the pilot needs to hear both.</para>
    /// </summary>
    public static void MergeUserPickEvents(TaxiRoute? route, IReadOnlyList<TaxiRouteRunwayEvent>? userEvents)
    {
        if (route?.RunwayEvents is null || userEvents is null) return;
        foreach (var ev in userEvents)
        {
            if (ev is null) continue;
            string want = NormalizeDesignator(ev.Designator);
            string recip = Reciprocal(want);
            bool already = route.RunwayEvents.Any(e =>
                e.Kind == ev.Kind &&
                (NormalizeDesignator(e.Designator).Equals(want, StringComparison.OrdinalIgnoreCase) ||
                 NormalizeDesignator(e.Designator).Equals(recip, StringComparison.OrdinalIgnoreCase)));
            if (!already) route.RunwayEvents.Add(ev);
        }
    }

    /// <summary>
    /// The nodes to classify: the route's own, with the aircraft PREPENDED while it has not rolled along the
    /// route (progress within <see cref="StopPassedToleranceMetres"/>). A route whose first node is already on
    /// a runway then still meets that runway from where the aircraft stands; an aircraft standing on a runway
    /// starts on it, so a route leaving it meets nothing. An aircraft already on the route is not prepended:
    /// one past a runway would invent a crossing from where it stands back to node 0.
    ///
    /// <para>The rule itself lives on <see cref="RunwayRouteClassifier.NodesFrom"/> (PR #238 deferred
    /// finding §1) because the landing re-crossing guard needs the SAME question asked the same way —
    /// it had no prepend at all, and the classifier's "started on the runway and vacated" branch made
    /// that silently accept a route back across the landing runway.</para>
    /// </summary>
    private static IReadOnlyList<TaxiNode?> ClassificationNodes(
        TaxiRoute route, AircraftPosition? aircraft, out bool aircraftPrepended)
        => RunwayRouteClassifier.NodesFrom(route.Segments, 0, aircraft, out aircraftPrepended);

    /// <summary>
    /// A passage classified with the aircraft prepended, back in the route's own node indices. An emitted
    /// passage always had an entry node, so only <c>EntryIndex</c> can become -1: the aircraft itself was
    /// the last point clear of the runway.
    /// </summary>
    private static RunwayPassage ToRouteIndices(RunwayPassage passage, bool aircraftPrepended)
        => !aircraftPrepended ? passage : passage with
        {
            EntryIndex = passage.EntryIndex - 1,
            FirstOnIndex = passage.FirstOnIndex >= 0 ? passage.FirstOnIndex - 1 : -1,
            ExitIndex = passage.ExitIndex >= 0 ? passage.ExitIndex - 1 : -1,
        };

    /// <summary>
    /// Whether the aircraft has rolled past the stop at node <paramref name="nodeIndex"/>: its route
    /// distance is more than <see cref="StopPassedToleranceMetres"/> behind the aircraft's progress along
    /// the route. Without a position nothing is passed.
    /// </summary>
    private static bool IsPassed(IReadOnlyList<TaxiRouteSegment> segments, int nodeIndex, AircraftPosition? aircraft)
        => aircraft is { } position
           && RouteDistanceToNode(segments, nodeIndex)
              < RouteProgressMeters(segments, position.Lat, position.Lon) - StopPassedToleranceMetres;

    /// <summary>
    /// Resolves and places the stop for <paramref name="passage"/>: a start hold, a tagged segment
    /// (shared with whatever hold already occupies it), or nothing at all — see
    /// <see cref="ResolveHoldStop"/> for when each applies.
    /// </summary>
    /// <param name="announcedDesignator">
    /// The designator the placed stop's label actually announces for <paramref name="passage"/>'s
    /// runway — the first designator <see cref="LabelDesignatorFor"/> finds in the label that was
    /// composed or kept, so the recorded event can never name the opposite end from the one the pilot
    /// will hear at the stop. When nothing is held (including a -1 "no stop" from
    /// <see cref="ResolveHoldStop"/>), or the label ends up naming no designator of this runway at
    /// all, this is <c>preferred ?? passage.Designator</c> — unchanged from before this parameter
    /// existed.
    /// </param>
    private static bool PlaceHold(
        TaxiRoute route, RunwayPassage passage, string? preferred, string? userLabel,
        IReadOnlyList<TaxiGraph.RunwayCenterline> runways, AircraftPosition? aircraft,
        out string announcedDesignator)
    {
        string announceAs = preferred ?? passage.Designator;
        announcedDesignator = announceAs;
        var otherRunways = runways
            .Where(r => r != null && !ReferenceEquals(r, passage.Runway))
            .Select(RunwayShape.For)
            .ToList();
        var stop = ResolveHoldStop(route.Segments, passage, otherRunways,
                                   startHoldPlaced: route.StartHoldRunway != null);

        // -1: walk 2 met this runway's own earlier pavement before a clear node, or another runway's
        // with no existing stop behind it — no safe stop for THIS passage (see ResolveHoldStop).
        // Report unheld rather than place nothing at all.
        if (stop.NodeIndex < 0) return false;

        if (stop.NodeIndex == 0)
        {
            // A start hold stops the aircraft where it stands: never on a runway's pavement, not once
            // the aircraft has rolled past the start node, and only while it is actually standing.
            //
            // Those three are ONE question each, asked of the aircraft's own state, and they replace
            // the deleted allowStartHold bool (PR #238 deferred finding §2). That bool was computed at
            // the landing-handoff sites as !IsWithinRolloutRunwayLaterally — lateral-only,
            // single-runway, along-track unbounded, +10 m margin, 200 ft default width — and then
            // RunwayUnder asked the same question again, extent-bounded, zero-margin, 75 ft default.
            // Every disagreement cost a legitimate start hold: an aircraft off the far END of the
            // runway is still inside the infinite strip, one 5 m outside the pavement edge is inside
            // the margin, and on a width-less centreline the two disagree over a 7.6 m band by
            // construction. Since PR #238 a refused start hold is also SPOKEN ("with no hold short
            // point for runway X"), so each of those is audible.
            if (IsPassed(route.Segments, 0, aircraft)) return false;
            // Clear of every runway by the SAME margin walk 2 demands of a stop it invents (half-width
            // + RunwayClearMarginM, along the axis as well): the deleted handoff gate was
            // !IsWithinRolloutRunwayLaterally, half-width + 10 m, and a zero-margin RunwayUnder in its
            // place let an aircraft stopped 4 m outside the pavement edge — tail still over the
            // runway it just landed on — be told to hold short right there (PR #243 review).
            if (aircraft is { } position
                && (!position.MayStartHeld
                    || RunwayWithinClearMargin(runways, position.Lat, position.Lon) != null))
                return false;
            route.StartHoldRunway = route.StartHoldRunway is null
                ? userLabel ?? $"runway {announceAs}"
                : ComposeSharedLabel(route.StartHoldRunway, announceAs) ?? route.StartHoldRunway;
            announcedDesignator = LabelDesignatorFor(route.StartHoldRunway, passage.Runway) ?? announceAs;
            return true;
        }

        if (IsPassed(route.Segments, stop.NodeIndex, aircraft)) return false;
        var holdSeg = route.Segments[stop.NodeIndex - 1];

        // At a stop that is already a hold-short, an explicit pick replaces the pilot's own "end of
        // taxiway" label with the runway it names; the automatic pass never touches that label.
        bool pickOverEndOfTaxiway = userLabel != null
            && (holdSeg.HoldShortRunway?.StartsWith("end of taxiway", StringComparison.OrdinalIgnoreCase) ?? false);
        string? label = holdSeg.IsHoldShortPoint
            ? (pickOverEndOfTaxiway ? userLabel : ComposeSharedLabel(holdSeg.HoldShortRunway, announceAs))
            : userLabel ?? ComposeCrossingLabel(holdSeg.HoldShortRunway, passage.Designator, preferred);
        holdSeg.IsHoldShortPoint = true;
        if (label != null) holdSeg.HoldShortRunway = label;
        announcedDesignator = LabelDesignatorFor(holdSeg.HoldShortRunway, passage.Runway) ?? announceAs;
        return true;
    }

    /// <summary>
    /// The first designator <see cref="ExtractRunwayDesignators"/> finds in <paramref name="label"/>
    /// (in label order) that belongs to <paramref name="runway"/> — a shared stop's label can name
    /// another runway's designator first, and a kept scenery label can carry the reciprocal end from
    /// the one a crossing's geometry reported under <see cref="RunwayPassage.Designator"/>. Null when
    /// the label names no designator of this runway at all.
    /// </summary>
    private static string? LabelDesignatorFor(string? label, TaxiGraph.RunwayCenterline runway)
        => ExtractRunwayDesignators(label).FirstOrDefault(d => CenterlineHasDesignator(runway, d));

    /// <summary>
    /// Progressive Taxi "after crossing runway X": the pilot is cleared across X, so remove every stop
    /// — including a start hold — that holds short of X ALONE. A stop shared with another runway
    /// stays, because the other runway is not cleared.
    ///
    /// <para>X's recorded EVENTS go with them. The summary is built from events, not from labels, so
    /// leaving them behind announced "crossing runway X" for a runway the pilot is cleared across —
    /// and, once an unheld event is flagged, would have read "with no hold short point for runway X"
    /// over a deliberate clearance. The Progressive terminator already names X. A shared stop's other
    /// runway keeps both its stop and its event.</para>
    /// </summary>
    public static void StripClearedCrossing(TaxiRoute route, string clearedRunway)
    {
        if (route is null || string.IsNullOrWhiteSpace(clearedRunway)) return;
        foreach (var seg in route.Segments)
        {
            if (seg.IsHoldShortPoint && LabelNamesOnlyRunway(seg.HoldShortRunway, clearedRunway))
            {
                seg.IsHoldShortPoint = false;
                seg.HoldShortRunway = "";
            }
        }
        if (LabelNamesOnlyRunway(route.StartHoldRunway, clearedRunway))
            route.StartHoldRunway = null;

        string cleared = NormalizeDesignator(clearedRunway);
        string reciprocal = Reciprocal(cleared);
        route.RunwayEvents?.RemoveAll(e =>
        {
            string d = NormalizeDesignator(e.Designator);
            return d.Equals(cleared, StringComparison.OrdinalIgnoreCase)
                || d.Equals(reciprocal, StringComparison.OrdinalIgnoreCase);
        });
    }
}
