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
    /// Whether the aircraft has already rolled PAST a candidate hold segment's stop point. Injected
    /// because the real test needs the live aircraft position; keeping it out leaves the passes pure
    /// and testable. Null means "nothing is behind the aircraft", the correct reading for any caller
    /// with no position to offer.
    /// </summary>
    public delegate bool HoldPointPassed(TaxiRouteSegment holdSegment);

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
        return string.Join(", ", parts);
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
    /// A resolved stop point: a node index from segment 0. 0 = the start node (a start hold); -1 = NO
    /// stop — walk 2 met an earlier stretch of THIS SAME runway's own pavement before finding a clear
    /// node, so nothing behind it is a safe place to hold for this passage (see
    /// <see cref="ResolveHoldStop"/>). <see cref="SharesExistingStop"/> is only meaningful for a
    /// NodeIndex &gt;= 0 and is false for the -1 case.
    /// </summary>
    public readonly record struct HoldStop(int NodeIndex, bool SharesExistingStop);

    /// <summary>
    /// Where the hold for one runway entry or crossing goes. The walk starts at the first node on the
    /// runway (for an edge that jumps across it, at the last clear node) and goes back toward the
    /// start of the route:
    ///  1. the first scenery hold node (HS/HSND/IHS/IHSND) within <see cref="CrossingHoldLookbackMetres"/>
    ///     whose name names this runway, its reciprocal or no runway, and which is off the pavement —
    ///     a hold node on the pavement is skipped, one naming a different runway ends this search, and
    ///     (at a node strictly before this walk's own starting node) a node ON this SAME runway's own
    ///     pavement also ends this search — that pavement belongs to an earlier entry or crossing of
    ///     the same runway, and nothing behind it can be reached without a stop this side of it;
    ///  2. otherwise the nearest node at or before the entry that is clear of the runway
    ///     (<see cref="RunwayShape.IsClearOf"/>) — but checked FIRST, before the clear-node and
    ///     existing-stop tests: a node ON this same runway's own pavement ends this walk with NO STOP
    ///     (<see cref="HoldStop"/> with <c>NodeIndex</c> -1). Sharing an earlier crossing's stop across
    ///     that pavement would place THIS crossing's hold on the far side of the runway from where it
    ///     needs to stop, so this passage is reported unheld instead;
    ///  3. otherwise the start node — reached only when walk 2 runs out of nodes without ever meeting
    ///     this runway's own pavement; a walk 2 that ends in a -1 (no stop) never falls back here.
    /// Neither walk passes a segment that is already a hold-short: reaching one, or (in walk 2)
    /// reaching this runway's own pavement, ends the walk before a clear node is found — the existing
    /// hold-short is shared; this runway's own pavement leaves no stop at all.
    ///
    /// <para>Every candidate is at or before the runway and off its pavement, so a stop can only move
    /// EARLIER than the old "segment before the crossing edge", never later — and a runway met twice
    /// close together (KSFO Q) can never have its second crossing borrow a stop from across the first
    /// crossing's own pavement. Cases and history: docs/taxi-guidance.md, "Runway crossings and
    /// entries".</para>
    /// </summary>
    /// <param name="passage">Classified from segment 0 (its indices are node indices of the whole route).</param>
    public static HoldStop ResolveHoldStop(IReadOnlyList<TaxiRouteSegment> segments, RunwayPassage passage)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(passage);

        var shape = passage.Shape;
        int walkStart = passage.FirstOnIndex >= 0 ? passage.FirstOnIndex : passage.EntryIndex;

        double walked = 0.0;
        for (int k = walkStart; k >= 0; k--)
        {
            if (k < walkStart) walked += segments[k].DistanceMeters;
            if (walked > CrossingHoldLookbackMetres) break;

            var node = NodeAt(segments, k);
            if (node != null && k < walkStart)
            {
                var (onAlong, onLateral) = shape.Project(node.Latitude, node.Longitude);
                if (shape.ContainsAlongLateral(onAlong, onLateral, 0.0)) break;
            }
            if (node != null && (node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort))
            {
                var guards = ExtractRunwayDesignators(node.HoldShortName);
                if (guards.Count > 0 && !guards.Any(d => CenterlineHasDesignator(passage.Runway, d)))
                    break;
                if (Math.Abs(shape.Project(node.Latitude, node.Longitude).Lateral) > shape.HalfWidthMeters)
                    return new HoldStop(k, IsExistingStop(segments, k));
            }
            if (IsExistingStop(segments, k)) break;
        }

        for (int k = passage.EntryIndex; k >= 0; k--)
        {
            var node = NodeAt(segments, k);
            bool existing = IsExistingStop(segments, k);
            if (node != null)
            {
                var (along, lateral) = shape.Project(node.Latitude, node.Longitude);
                if (shape.ContainsAlongLateral(along, lateral, 0.0)) return new HoldStop(-1, false);
                if (shape.IsClearOf(lateral)) return new HoldStop(k, existing);
            }
            if (existing) return new HoldStop(k, true);
        }

        return new HoldStop(0, false);
    }

    private static TaxiNode? NodeAt(IReadOnlyList<TaxiRouteSegment> segments, int nodeIndex)
        => nodeIndex == 0 ? segments[0]?.FromNode : segments[nodeIndex - 1]?.ToNode;

    private static bool IsExistingStop(IReadOnlyList<TaxiRouteSegment> segments, int nodeIndex)
        => nodeIndex >= 1 && segments[nodeIndex - 1].IsHoldShortPoint;

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
    /// </summary>
    /// <param name="destinationName">The runway destination as spoken ("Runway 33L"), or "" for other routes.</param>
    /// <param name="allowStartHold">True only when <c>LoadRoute</c> adopts a fresh route (phase "load"); false on a recalculation and on a route adopted for the landing rollout (phase "touchdown").</param>
    /// <param name="holdPointPassed">True when the aircraft has rolled past a candidate hold segment's end. Null = nothing passed.</param>
    /// <param name="startPointPassed">True when the aircraft is already more than 10 m past the start node. Null = not passed.</param>
    public static IReadOnlyList<TaxiRouteRunwayEvent> InsertRunwayHoldShorts(
        TaxiRoute route,
        IReadOnlyList<TaxiGraph.RunwayCenterline> runways,
        string destinationName,
        bool allowStartHold,
        HoldPointPassed? holdPointPassed = null,
        Func<bool>? startPointPassed = null)
    {
        // A null runway list is a wiring error; returning "no runways met" would present it as a
        // safe route, so it fails loudly. A null or empty route legitimately meets nothing.
        ArgumentNullException.ThrowIfNull(runways);
        if (route is null) return Array.Empty<TaxiRouteRunwayEvent>();
        route.RunwayEvents = new List<TaxiRouteRunwayEvent>();
        if (route.Segments.Count == 0 || runways.Count == 0) return route.RunwayEvents;

        string destBare = StripRunwayPrefix(destinationName);
        var nodes = RunwayRouteClassifier.NodesFrom(route.Segments, 0);
        foreach (var passage in RunwayRouteClassifier.ClassifyAll(nodes, runways))
        {
            bool destinationStrip = destBare.Length > 0 && CenterlineHasDesignator(passage.Runway, destBare);
            if (destinationStrip && passage.Kind == RunwayEventKind.Entry && passage.ExitIndex < 0)
                continue;

            string? preferred = destinationStrip ? destBare : null;
            bool held = PlaceHold(route, passage, preferred, userLabel: null,
                allowStartHold, holdPointPassed, startPointPassed, out string announcedDesignator);
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
        return route.RunwayEvents;
    }

    /// <summary>
    /// The pilot's explicit "hold short of runway X" pick for one taxiway row: honoured when the route
    /// enters or crosses X at or after <paramref name="runStartSegmentIndex"/> (the start of that
    /// taxiway's run, which is also the index of its first node), placed by the same resolver as the
    /// automatic pass, labelled "runway X" as the pilot typed it.
    /// </summary>
    public static bool ApplyUserRunwayHold(
        TaxiRoute route,
        TaxiGraph.RunwayCenterline runway,
        string runwayId,
        int runStartSegmentIndex,
        bool allowStartHold,
        HoldPointPassed? holdPointPassed = null,
        Func<bool>? startPointPassed = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(runway);

        var nodes = RunwayRouteClassifier.NodesFrom(route.Segments, 0);
        var passage = RunwayRouteClassifier.Classify(nodes, RunwayShape.For(runway))
            .FirstOrDefault(p => p.ReachIndex >= runStartSegmentIndex);
        if (passage is null) return false;

        string pick = runwayId.Trim();
        PlaceHold(route, passage, preferred: pick, userLabel: $"runway {pick}",
            allowStartHold, holdPointPassed, startPointPassed, out _);
        return true;
    }

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
        bool allowStartHold, HoldPointPassed? holdPointPassed, Func<bool>? startPointPassed,
        out string announcedDesignator)
    {
        string announceAs = preferred ?? passage.Designator;
        announcedDesignator = announceAs;
        var stop = ResolveHoldStop(route.Segments, passage);

        // -1: walk 2 met this runway's own earlier pavement before a clear node — no safe stop for
        // THIS passage (see ResolveHoldStop). Report unheld rather than place nothing at all.
        if (stop.NodeIndex < 0) return false;

        if (stop.NodeIndex == 0)
        {
            if (!allowStartHold || (startPointPassed?.Invoke() ?? false)) return false;
            route.StartHoldRunway = route.StartHoldRunway is null
                ? userLabel ?? $"runway {announceAs}"
                : ComposeSharedLabel(route.StartHoldRunway, announceAs) ?? route.StartHoldRunway;
            announcedDesignator = LabelDesignatorFor(route.StartHoldRunway, passage.Runway) ?? announceAs;
            return true;
        }

        var holdSeg = route.Segments[stop.NodeIndex - 1];
        if (holdPointPassed?.Invoke(holdSeg) ?? false) return false;

        string? label = holdSeg.IsHoldShortPoint
            ? ComposeSharedLabel(holdSeg.HoldShortRunway, announceAs)
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
    }
}
