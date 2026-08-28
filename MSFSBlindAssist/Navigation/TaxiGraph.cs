using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Represents an edge in the taxi graph connecting two nodes.
/// </summary>
public class TaxiEdge
{
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public double DistanceMeters { get; set; }
    public string TaxiwayName { get; set; } = "";
    public double BearingDegrees { get; set; }
    public double WidthFeet { get; set; }
    public string PathType { get; set; } = "";
}

/// <summary>
/// Builds and represents a taxi graph from navdatareader taxi_path data.
/// Each taxi_path row defines a centerline segment; endpoints within ~1m are merged into shared nodes.
/// </summary>
public class TaxiGraph
{
    // Merge threshold in METERS. Using a distance-based check keeps merging consistent
    // across latitudes — a degree-based check was asymmetric (NS vs EW) at high latitudes
    // like ENGM (60°N) / ENSB (78°N) and fragmented the graph at arctic airports.
    // 1.5 m is wide enough to absorb navdatareader coordinate rounding yet tight enough
    // to keep genuinely-separate endpoints apart (real taxi_path segments are >>5 m long).
    private const double MERGE_THRESHOLD_METERS = 1.5;
    private const int SPATIAL_HASH_PRECISION = 5; // decimal places for hash key

    public Dictionary<int, TaxiNode> Nodes { get; } = new();
    public Dictionary<int, List<TaxiEdge>> Adjacency { get; } = new();

    /// <summary>
    /// One physical runway as a centerline segment between its two thresholds,
    /// with a name for each direction. Built from the navdatareader `start`
    /// table at construction time. Used by DescribeLocation to detect
    /// "on runway X" mid-runway — the older edge-scan path required
    /// taxi_path.type starting with 'R', but no row in the navdatareader DB
    /// actually has that type, so without these pairs we could only report a
    /// runway when the aircraft was sitting within 50 m of a threshold node.
    /// </summary>
    public class RunwayCenterline
    {
        public double Lat1, Lon1;          // primary-end threshold
        public double Lat2, Lon2;          // opposite-end threshold
        public string Name1 = "";          // designator at primary end (e.g. "27L")
        public string Name2 = "";          // designator at opposite end (e.g. "09R")
        public double HeadingDeg1;         // heading from end 1 (0..360, true)
        public double HalfWidthMeters;     // centerline → edge tolerance
    }
    public List<RunwayCenterline> RunwayCenterlines { get; } = new();

    /// <summary>
    /// A taxiway that meets a runway partway down its length — an intersection
    /// (a.k.a. intersection-departure) point. Enumerated by
    /// <see cref="GetRunwayIntersections"/> so the Taxi form can offer "depart
    /// from taxiway W" instead of taxiing to the full-length threshold.
    /// </summary>
    public class RunwayIntersection
    {
        public string TaxiwayName = "";
        public int NodeId;                      // graph node where the taxiway meets the runway
        public double Latitude, Longitude;      // that node projected ONTO the centerline
        public double AlongMetersFromThreshold; // from the named runway's takeoff-end threshold
        public double RemainingMeters;          // runway ahead in the takeoff direction

        /// <summary>
        /// For a NAMED holding point (<see cref="ResolveHoldingPointEntries"/>): the graph
        /// node the PAINTED HOLD LINE itself snapped to, as opposed to <see cref="NodeId"/>
        /// (where its stub meets the runway). 0 for runway intersections, and for a painted
        /// point whose line didn't snap to a distinct reachable node.
        /// <para>Routing pins the route THROUGH this node so the pilot taxis up the stub they
        /// named. Without it only the runway ENTRY is fixed and the approach corridor is a
        /// free A* choice, which at EGLL 27R took a pilot who picked A2 up the neighbouring A3
        /// stub — A2 and A3 merge just short of the runway, so the route rejoined A2 for its
        /// last 60 m and the hold-short (navdata-authoritative, the LAST hold node on the
        /// route) landed on and announced A3.</para>
        /// </summary>
        public int HoldNodeId;
    }

    // Max perpendicular distance (metres) from a hold-short node to a runway
    // centerline for the node to be named after that runway. Above a CAT III /
    // code-F holding-position setback (~107 m) so real hold lines match, below
    // major-airport parallel-runway spacing so a node binds to its OWN runway.
    // internal (not private): HoldShortNodeResolver.HS_NODE_MATCH_M references
    // this directly so its designated-node trust window can never drift from
    // the naming tolerance.
    internal const double HOLDSHORT_RUNWAY_MATCH_M = 150.0;

    // Start at 1 so node ID 0 is a permanent "not set" sentinel. TaxiGuidanceManager
    // uses _destinationNodeId = 0 to mark a cleared route (e.g. after
    // EnterRunwayEndCountdown). If _nextNodeId were 0, the first real node would
    // collide with that sentinel — ContainsKey(0) would return true and the
    // recalc path could try to route to it.
    private int _nextNodeId = 1;
    private readonly Dictionary<string, List<int>> _spatialHash = new();
    private readonly Dictionary<string, List<int>> _taxiwayNodeIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Build-time-only dedup sidecar for <see cref="RegisterTaxiwayNode"/>: mirrors the node-id
    /// SET for each taxiway in <see cref="_taxiwayNodeIndex"/> so the per-registration "already
    /// added?" check is O(1) instead of an O(n) <see cref="List{T}.Contains"/> scan (large airports
    /// register thousands of (taxiway, node) pairs during Build). The LIST in
    /// <see cref="_taxiwayNodeIndex"/> remains the single source of truth for iteration order —
    /// <see cref="GetNodesOnTaxiway"/> reads only the list, never this set.
    /// </summary>
    private readonly Dictionary<string, HashSet<int>> _taxiwayNodeIdSet = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a normalized alias (see TaxiDataMerger.NormalizeTaxiwayName) to the canonical
    /// taxiway name stored in the graph. Populated during Build from TaxiPath.Aliases.
    /// Used by ResolveTaxiwayName so pilots can enter alternative names (e.g. "K" for
    /// a navdata taxiway named "HAWKER") and still find the correct route.
    /// </summary>
    public Dictionary<string, string> TaxiwayAliasToCanonical { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Self-describing dropdown labels for online-source aliases — maps the display string
    /// (e.g. "B (HAWKER)") to the canonical navdata name ("HAWKER"). Surfaced in the taxiway
    /// dropdowns so a screen-reader user hears "B, HAWKER" and knows it's the SAME pavement;
    /// selecting one resolves to the canonical via ResolveTaxiwayName (exact match on the label).
    /// </summary>
    public Dictionary<string, string> AliasDisplayToCanonical { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Display label (e.g. "B (HAWKER)") → the normalized alias form ("B") captured at Build time.
    /// GetAllTaxiwayNames' collision skip reads THIS instead of re-parsing the label with
    /// LastIndexOf(" (") — a canonical name that itself contains " (" (e.g. "RAMP (NORTH)") would
    /// otherwise split at the wrong paren and mis-classify the alias.
    /// </summary>
    private readonly Dictionary<string, string> _aliasDisplayToNormalized = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalized forms of every REAL navdata taxiway name. An alias is never allowed to remap a
    /// name that is itself a real taxiway (that would misroute a legitimate clearance), so
    /// ResolveTaxiwayName / GetAllTaxiwayNames consult this set as a guard.
    /// </summary>
    private readonly HashSet<string> _normalizedRealNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalized bare aliases that map to MORE THAN ONE distinct canonical name (two different
    /// navdata taxiways online-named the same thing). A bare such alias can't safely pick one
    /// pavement, so ResolveTaxiwayName refuses to resolve it (returns the entered text unchanged) —
    /// a miss is safer than guessing. The disambiguated labels ("B (HAWKER)" / "B (FOXTROT)") still
    /// resolve via AliasDisplayToCanonical.
    /// </summary>
    private readonly HashSet<string> _ambiguousNormalizedAliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the taxi graph asynchronously — useful at large airports where Build can take
    /// 200-500ms and would otherwise stall the UI thread. Runs on the thread pool.
    /// </summary>
    public static System.Threading.Tasks.Task<TaxiGraph> BuildAsync(
        List<TaxiPath> paths, List<ParkingSpot> parkingSpots, List<StartPosition> runwayStarts,
        IReadOnlyList<Runway>? runways = null)
    {
        return System.Threading.Tasks.Task.Run(() => Build(paths, parkingSpots, runwayStarts, runways));
    }

    /// <summary>
    /// One spelling per taxiway, resolvable from any spelling that appears for it.
    ///
    /// Scenery data really does carry the same taxiway under two casings — CYVR navdata
    /// holds both "D" and "d" — and two accessors on this class then disagreed about the
    /// airport. <see cref="GetAllTaxiwayNames"/> dedupes into a HashSet with
    /// StringComparer.OrdinalIgnoreCase, so it reported ONE of them; <see cref="GetNamedEdges"/>
    /// returned each edge's raw name, so it reported BOTH. The SayIntentions import consumes
    /// the two TOGETHER — the first to resolve the spoken clearance, the second to snap the
    /// published ground track — so a live 2026-08-19 CYVR import produced "d" and "D" as
    /// separate legs and the form could seat only one of them.
    ///
    /// The canonical spelling is THE ONE THE DATA MOSTLY USES, with the ordinally smallest
    /// breaking an even vote. Ordinal-smallest ALONE was tried first and is wrong for
    /// word-shaped names: uppercase sorts before lowercase at the first differing letter
    /// ('I' 0x49 &lt; 'i' 0x69), so a SINGLE row spelled "LINK 5" renamed every "Link 5"
    /// segment at the airport — and TaxiwayName is SPOKEN VERBATIM, so a screen reader then
    /// reads it letter by letter. docs/taxi-guidance.md states the rule that breaks: "The
    /// stored name is always the original human-readable form from the authoritative
    /// source." Counting rows honours it wherever the data has a predominant spelling, and
    /// the ordinal tie-break keeps the whole rule deterministic and independent of
    /// enumeration order — the same property GetNamedEdges' own sort key exists to
    /// guarantee — while still preferring the conventional "D" over "d" for a
    /// single-letter designator, where the two spellings are genuinely one row each.
    ///
    /// A row the graph itself DISCARDS gets no vote: <see cref="Build"/> skips a path whose
    /// endpoints resolve to one node, but only AFTER this fold has run, so a zero-length row
    /// could otherwise decide the spelling of every real segment while contributing no node,
    /// no edge and no RegisterTaxiwayNode call. Such a row still MAPS (it is a key like any
    /// other) so that whatever Build does with it lands on the same spelling; it just does
    /// not choose. A group with nothing but discarded rows falls back to the ordinal
    /// tie-break naturally, because every vote in it is zero.
    ///
    /// This does NOT only arise where an airport's OWN data is inconsistently cased — an
    /// earlier version of this comment claimed it did, and that is wrong. The list reaching
    /// <see cref="Build"/> is the AUGMENTED one: AugmentingAirportDataProvider writes OSM /
    /// apt.dat names into TaxiPath.Name for segments navdata left unnamed, un-normalised, so
    /// an online spelling competes here with a navdata one.
    ///
    /// So PROVENANCE OUTRANKS THE VOTE: a spelling with a navdata row behind it beats one
    /// with none, however many online rows carry the latter. "navdata is AUTHORITATIVE — an
    /// existing navdata taxiway/gate name is never overwritten" (CLAUDE.md) is enforced by
    /// TaxiDataMerger when it merges, which refuses to overwrite a named segment; without
    /// this the fold could undo it one layer up, because a taxiway navdata names on two
    /// segments and an online source fills on three would lose 3-2 on count alone. The vote
    /// then decides inside the winning provenance, and ordinal-smallest inside that. A group
    /// with no navdata row at all is decided by the vote exactly as before.
    ///
    /// A discarded row confers no authority either — the navdata tally counts only rows Build
    /// will keep, for the same reason the vote does.
    ///
    /// The returned dictionary holds ONE entry per case-insensitive group, keyed by the
    /// first spelling seen for it — not one entry per spelling. Lookups are
    /// OrdinalIgnoreCase, so every spelling still resolves; do not read <c>Keys</c> expecting
    /// the canonical form.
    ///
    /// Safe to apply at the point names enter the graph because every consumer of
    /// TaxiEdge.TaxiwayName that COMPARES names already does so OrdinalIgnoreCase (TaxiRouter,
    /// TaxiGuidanceManager, TaxiLeadIn, ResolveTaxiwayName). That is not a class invariant —
    /// this file's own edge dedups and SayIntentionsTaxiPathSnapper compare ordinally — it is
    /// what makes folding safe for them, and this fold is what makes their ordinal compares
    /// safe in turn. The only behavioural change is that one taxiway now has one spelling
    /// wherever it is displayed, emitted or seated.
    /// </summary>
    /// <summary>How many rows carry one exact spelling, and how many of those are
    /// navdata's rather than an online source's. Discarded rows count in neither.</summary>
    private readonly record struct Tally(int Navdata, int Total);

    internal static Dictionary<string, string> BuildCanonicalTaxiwayNames(List<TaxiPath> paths)
    {
        // Case-insensitive group -> per exact spelling, how many SUBSTANTIAL rows carry it
        // and how many of those came from navdata rather than an online source.
        var votes = new Dictionary<string, Dictionary<string, Tally>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            string name = path.Name?.Trim() ?? "";
            if (name.Length == 0) continue;

            if (!votes.TryGetValue(name, out var perSpelling))
            {
                perSpelling = new Dictionary<string, Tally>(StringComparer.Ordinal);
                votes[name] = perSpelling;
            }

            // Zero weight for a row Build will discard: it still gets an entry, so the
            // spelling maps, but it cannot outvote - or lend authority to - pavement that
            // is not there.
            bool discardedByBuild = FastDistanceMeters(
                path.StartLat, path.StartLon, path.EndLat, path.EndLon) < MERGE_THRESHOLD_METERS;
            int weight = discardedByBuild ? 0 : 1;
            int navdataWeight = path.NameFromOnlineSource ? 0 : weight;

            var seen = perSpelling.TryGetValue(name, out var t) ? t : default;
            perSpelling[name] = new Tally(seen.Navdata + navdataWeight, seen.Total + weight);
        }

        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in votes)
        {
            string? best = null;
            var bestTally = new Tally(-1, -1);
            foreach (var (spelling, tally) in group.Value)
            {
                if (Beats(spelling, tally, best, bestTally))
                {
                    best = spelling;
                    bestTally = tally;
                }
            }

            canonical[group.Key] = best!;
        }

        return canonical;

        // Total order: navdata authority first, then most rows, then ordinally smallest.
        // Every step is a property of the data rather than of enumeration order, so the same
        // airport always folds the same way however the paths list was ordered.
        static bool Beats(string spelling, Tally tally, string? best, Tally bestTally)
        {
            if (tally.Navdata != bestTally.Navdata) return tally.Navdata > bestTally.Navdata;
            if (tally.Total != bestTally.Total) return tally.Total > bestTally.Total;
            return string.CompareOrdinal(spelling, best) < 0;
        }
    }

    /// <summary>
    /// Builds the taxi graph from raw taxi path data and parking spots.
    /// </summary>
    public static TaxiGraph Build(List<TaxiPath> paths, List<ParkingSpot> parkingSpots, List<StartPosition> runwayStarts,
        IReadOnlyList<Runway>? runways = null)
    {
        var graph = new TaxiGraph();

        // Repair laterally-bogus start rows against the runway table when the caller has
        // one (see SnapStartToRunwayCenterline for the EGKK evidence). Everything below —
        // runway-start node marking, the centerlines, the hold-short naming fallback —
        // reads runwayStarts, so correcting it once here covers all three. Callers with no
        // runway table (tests, probes) keep today's behavior exactly.
        if (runways != null && runways.Count > 0 && runwayStarts.Count > 0)
        {
            var byName = new Dictionary<string, Runway>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in runways)
                if (!string.IsNullOrEmpty(r.RunwayID)) byName.TryAdd(r.RunwayID.Trim(), r);

            var snapped = new List<StartPosition>(runwayStarts.Count);
            foreach (var s in runwayStarts)
            {
                if (!byName.TryGetValue(s.RunwayName?.Trim() ?? "", out var rwy))
                {
                    snapped.Add(s);
                    continue;
                }
                var (lat, lon) = SnapStartToRunwayCenterline(
                    s.Latitude, s.Longitude, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon);
                if (Math.Abs(lat - s.Latitude) < 1e-9 && Math.Abs(lon - s.Longitude) < 1e-9)
                {
                    snapped.Add(s);
                    continue;
                }
                // Copy rather than mutate: the caller's list is often a cached provider
                // result that other features read.
                snapped.Add(new StartPosition
                {
                    RunwayName = s.RunwayName,
                    Type = s.Type,
                    Heading = s.Heading,
                    Altitude = s.Altitude,
                    Latitude = lat,
                    Longitude = lon,
                });
            }
            runwayStarts = snapped;
        }

        // One spelling per taxiway, decided across ALL paths before any is processed —
        // see BuildCanonicalTaxiwayNames for the CYVR "D"/"d" case this removes.
        var canonicalTaxiwayNames = BuildCanonicalTaxiwayNames(paths);

        foreach (var path in paths)
        {
            // Defense-in-depth: trim here in case the path was constructed directly
            // (e.g. tests) bypassing the DB provider normalization. Then fold the
            // trimmed name onto the airport's canonical spelling of it, so nodes, edges,
            // RegisterTaxiwayNode, _normalizedRealNames and the alias labels below all
            // agree with GetAllTaxiwayNames().
            string name = path.Name?.Trim() ?? "";
            if (name.Length > 0 && canonicalTaxiwayNames.TryGetValue(name, out string? canonicalName))
            {
                name = canonicalName;
            }

            // Resolve start and end nodes (create if new, merge if close) — pass the
            // trimmed name so node.TaxiwayNames HashSet entries are canonical.
            int startNodeId = graph.ResolveNode(path.StartLat, path.StartLon, path.StartType, name);
            int endNodeId = graph.ResolveNode(path.EndLat, path.EndLon, path.EndType, name);

            if (startNodeId == endNodeId)
                continue; // degenerate segment

            // Calculate edge properties
            double distMeters = CalculateDistanceMeters(
                path.StartLat, path.StartLon, path.EndLat, path.EndLon);
            double bearing = NavigationCalculator.CalculateBearing(
                path.StartLat, path.StartLon, path.EndLat, path.EndLon);

            // Add bidirectional edges
            var fwdEdge = new TaxiEdge
            {
                FromNodeId = startNodeId,
                ToNodeId = endNodeId,
                DistanceMeters = distMeters,
                TaxiwayName = name,
                BearingDegrees = bearing,
                WidthFeet = path.Width,
                PathType = path.Type
            };
            var revEdge = new TaxiEdge
            {
                FromNodeId = endNodeId,
                ToNodeId = startNodeId,
                DistanceMeters = distMeters,
                TaxiwayName = name,
                BearingDegrees = (bearing + 180.0) % 360.0,
                WidthFeet = path.Width,
                PathType = path.Type
            };

            graph.AddEdge(fwdEdge);
            graph.AddEdge(revEdge);

            // Register taxiway name to nodes
            if (!string.IsNullOrEmpty(name))
            {
                graph.RegisterTaxiwayNode(name, startNodeId);
                graph.RegisterTaxiwayNode(name, endNodeId);
                // Track the normalized real name so an alias can never remap a genuine taxiway.
                graph._normalizedRealNames.Add(
                    MSFSBlindAssist.Services.TaxiAugment.TaxiDataMerger.NormalizeTaxiwayName(name));
            }

            // Register any online-source aliases discovered by TaxiDataMerger.
            // Only affects COMPARISON / routing — never changes what is stored in the graph.
            if (path.Aliases != null && path.Aliases.Count > 0 && !string.IsNullOrEmpty(name))
            {
                foreach (var alias in path.Aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    string normalizedAlias = MSFSBlindAssist.Services.TaxiAugment.TaxiDataMerger
                        .NormalizeTaxiwayName(alias);
                    if (string.IsNullOrEmpty(normalizedAlias)) continue;

                    // Self-describing label "ALIAS (CANONICAL)" for the dropdown so the pilot can
                    // find + select the ATC/real name and hear which pavement it is. ALWAYS register
                    // the label (one per distinct canonical), so when two taxiways share an online
                    // name BOTH "B (HAWKER)" and "B (FOXTROT)" are selectable; resolution is exact on
                    // the label string. Also store the normalized alias for GetAllTaxiwayNames' skip.
                    string label = $"{alias.Trim()} ({name})";
                    graph.AliasDisplayToCanonical[label] = name;
                    graph._aliasDisplayToNormalized[label] = normalizedAlias;

                    // Bare-alias → canonical map, with ambiguity detection. A second DIFFERENT
                    // canonical for the same normalized alias makes the bare form ambiguous: remove
                    // it and never re-add (ResolveTaxiwayName then leaves a bare "B" unresolved).
                    if (graph._ambiguousNormalizedAliases.Contains(normalizedAlias))
                    {
                        // already ambiguous — labels stay, bare form stays unresolved
                    }
                    else if (graph.TaxiwayAliasToCanonical.TryGetValue(normalizedAlias, out var existing))
                    {
                        if (!string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                        {
                            graph._ambiguousNormalizedAliases.Add(normalizedAlias);
                            graph.TaxiwayAliasToCanonical.Remove(normalizedAlias);
                        }
                        // same canonical (another segment of the same taxiway) — keep as-is
                    }
                    else
                    {
                        graph.TaxiwayAliasToCanonical[normalizedAlias] = name;
                    }
                }
            }
        }

        // Mark parking nodes by matching to parking spot coordinates (spatial-hash FindNearestNode)
        foreach (var spot in parkingSpots)
        {
            var nearNode = graph.FindNearestNode(spot.Latitude, spot.Longitude);
            if (nearNode != null)
            {
                double dist = FastDistanceMeters(nearNode.Latitude, nearNode.Longitude, spot.Latitude, spot.Longitude);
                if (dist < 100) // within 100m
                {
                    nearNode.Type = TaxiNodeType.Parking;
                    string displayName = FormatParkingName(spot);
                    nearNode.ParkingName = displayName;
                }
            }
        }

        // Mark runway start nodes
        foreach (var start in runwayStarts)
        {
            var nearNode = graph.FindNearestNode(start.Latitude, start.Longitude);
            if (nearNode != null)
            {
                double dist = FastDistanceMeters(nearNode.Latitude, nearNode.Longitude, start.Latitude, start.Longitude);
                if (dist < 150)
                {
                    nearNode.ParkingName ??= $"Runway {start.RunwayName}";
                }
            }
        }

        // Build runway centerlines by pairing opposing runway-start positions, so
        // DescribeLocation can detect "on runway X" anywhere along the runway, not just
        // within 50 m of a threshold node — the previous edge-scan path required
        // taxi_path.type='R', which doesn't exist in the navdatareader DB. Half-width
        // defaults to 75 ft (≈23 m) when we can't infer it from a nearby taxi_path edge,
        // which covers most Code C/D/E runways.
        //
        // TWO PASSES, DESIGNATOR FIRST. Designators are reciprocal BY DEFINITION (number
        // differs by 18, L↔R swapped, C/none unchanged) and the side letter keeps
        // parallels apart — 08L can only ever pair with 26R — whereas `start.heading` is
        // wrong often enough to mis-pair whole airports. LEMD stores 0° on runways that
        // point 322°, which made the heading test read 32R as the reciprocal of 18L and
        // 32L as the reciprocal of 18R: two lines drawn DIAGONALLY ACROSS THE AIRFIELD,
        // and no correct line at all. EGKK stores 08L and 26R both at 257.6° and 08R and
        // 26L both at ~168°, so nothing paired and it built ZERO centerlines.
        //
        // Measured over the whole fs2020 navdata (41.8 k airports, 47.9 k centerlines),
        // running the designator pass first changes 10 airports and improves ALL TEN —
        // mis-paired lines fall from 662 to 642 and LEMD goes from 2 wrong lines to 4
        // right ones. Nothing regresses, which is why the order is safe to fix.
        const double DEFAULT_HALF_WIDTH_FT = 75.0;
        var paired = new HashSet<int>();

        for (int i = 0; i < runwayStarts.Count; i++)
        {
            if (paired.Contains(i)) continue;
            var a = runwayStarts[i];
            string? reciprocal = ReciprocalRunwayName(a.RunwayName);
            if (reciprocal == null) continue;

            for (int j = i + 1; j < runwayStarts.Count; j++)
            {
                if (paired.Contains(j)) continue;
                var b = runwayStarts[j];
                if (!string.Equals(b.RunwayName?.Trim(), reciprocal, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Sanity: opposing thresholds should be 200 m – 6000 m apart
                // (smaller = same end, larger = different runway pair).
                double sep = FastDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (sep < 200.0 || sep > 6000.0) continue;

                // Does the a→b direction actually match what "a" is called? This replaces
                // the stored heading as the geometry check. The tolerance is wide (45°)
                // because the designator is magnetic while the computed bearing is true,
                // and magnetic variation reaches ~20° at high latitudes; the exact-
                // reciprocal-name and separation tests do the real work.
                double designatorHdg = RunwayDesignatorHeading(a.RunwayName);
                double actualHdg = NavigationCalculator.CalculateBearing(
                    a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (Math.Abs(NormalizeAngle(designatorHdg - actualHdg)) > 45.0) continue;

                graph.RunwayCenterlines.Add(new RunwayCenterline
                {
                    Lat1 = a.Latitude, Lon1 = a.Longitude,
                    Lat2 = b.Latitude, Lon2 = b.Longitude,
                    Name1 = a.RunwayName,
                    Name2 = b.RunwayName,
                    // The stored heading is what we just declined to trust — use the
                    // measured one, which is also what every consumer of this line means.
                    HeadingDeg1 = actualHdg,
                    HalfWidthMeters = (DEFAULT_HALF_WIDTH_FT * 0.3048),
                });
                paired.Add(i);
                paired.Add(j);
                break;
            }
        }

        // SECOND PASS — the leftovers, by reciprocal HEADING (±15° to absorb mag/true
        // conventions). Still needed, and not merely as a safety net: it is what rescues
        // the NAME-SWAPPED airports, where the row labelled for one end physically sits at
        // the other. AYCH's "03" row sits at the 21 threshold carrying 21's heading, so the
        // designator pass correctly refuses it (the a→b bearing is 185° from what "03"
        // claims) while the heading pass still finds the true pair.
        for (int i = 0; i < runwayStarts.Count; i++)
        {
            if (paired.Contains(i)) continue;
            var a = runwayStarts[i];
            for (int j = i + 1; j < runwayStarts.Count; j++)
            {
                if (paired.Contains(j)) continue;
                var b = runwayStarts[j];
                double hdgDelta = Math.Abs(NormalizeAngle(a.Heading - b.Heading + 180.0));
                if (hdgDelta > 15.0) continue;
                double sep = FastDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (sep < 200.0 || sep > 6000.0) continue;

                graph.RunwayCenterlines.Add(new RunwayCenterline
                {
                    Lat1 = a.Latitude, Lon1 = a.Longitude,
                    Lat2 = b.Latitude, Lon2 = b.Longitude,
                    Name1 = a.RunwayName,
                    Name2 = b.RunwayName,
                    HeadingDeg1 = a.Heading,
                    HalfWidthMeters = (DEFAULT_HALF_WIDTH_FT * 0.3048),
                });
                paired.Add(i);
                paired.Add(j);
                break;
            }
        }

        // Assign hold-short names using the node's taxiway designator and nearest runway.
        // The taxiway name on the hold-short node IS the holding point designator (e.g. NB1, A5).
        // The nearest runway tells us what we're holding short OF.
        foreach (var node in graph.Nodes.Values)
        {
            if (node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort)
            {
                // Find nearest runway for "holding short of" context
                string? nearestRunway = null;
                double nearestDist = double.MaxValue;

                foreach (var start in runwayStarts)
                {
                    double dist = FastDistanceMeters(node.Latitude, node.Longitude, start.Latitude, start.Longitude);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestRunway = start.RunwayName;
                    }
                }

                // The hold point's own name comes from its taxiway edges.
                // Prefer connector-style designators (A5, NB1, K12) over plain
                // main taxiway names (A, B, K) because the connector name is
                // the actual *holding point* designator pilots use with ATC.
                //
                // Ranking (best first):
                //   1. Name contains BOTH a letter and a digit (e.g., "A5", "NB1", "K12")
                //   2. Any non-empty name (longest-first — gives "MAIN" over "A")
                //   3. Fallback: empty
                string holdPointName = "";
                if (graph.Adjacency.TryGetValue(node.NodeId, out var edges))
                {
                    var distinctNames = new HashSet<string>();
                    foreach (var edge in edges)
                    {
                        if (!string.IsNullOrEmpty(edge.TaxiwayName))
                            distinctNames.Add(edge.TaxiwayName);
                    }

                    // Tier 1: names that look like connector/holding-point designators
                    string? connectorName = null;
                    foreach (var n in distinctNames)
                    {
                        bool hasLetter = false, hasDigit = false;
                        foreach (char c in n)
                        {
                            if (char.IsLetter(c)) hasLetter = true;
                            else if (char.IsDigit(c)) hasDigit = true;
                        }
                        if (hasLetter && hasDigit)
                        {
                            // Among connectors, prefer the shortest (A5 > KILO5A);
                            // ties broken by alpha order for determinism.
                            if (connectorName == null ||
                                n.Length < connectorName.Length ||
                                (n.Length == connectorName.Length &&
                                 string.Compare(n, connectorName, StringComparison.Ordinal) < 0))
                            {
                                connectorName = n;
                            }
                        }
                    }

                    if (connectorName != null)
                    {
                        holdPointName = connectorName;
                    }
                    else if (distinctNames.Count > 0)
                    {
                        // No connector pattern — fall back to any name.
                        // Prefer longer over single-letter (more specific).
                        string? best = null;
                        foreach (var n in distinctNames)
                        {
                            if (best == null ||
                                n.Length > best.Length ||
                                (n.Length == best.Length &&
                                 string.Compare(n, best, StringComparison.Ordinal) < 0))
                            {
                                best = n;
                            }
                        }
                        holdPointName = best ?? "";
                    }
                }

                // Primary: associate by nearest runway CENTERLINE (length-invariant),
                // so a mid-runway crossing of a long runway is named after the runway,
                // not the taxiway. Format leads with the runway (the safety cue),
                // appending the holding-point/taxiway designator when present.
                string? centerlineRwy = MatchHoldShortRunwayName(
                    node.Latitude, node.Longitude, graph.RunwayCenterlines, HOLDSHORT_RUNWAY_MATCH_M);
                if (centerlineRwy != null)
                {
                    node.HoldShortName = !string.IsNullOrEmpty(holdPointName)
                        ? $"runway {centerlineRwy} at {holdPointName}"
                        : $"runway {centerlineRwy}";
                }
                // Fallback (no centerlines built, or none within tolerance): the
                // existing threshold-distance heuristic, unchanged — preserves
                // today's output for sparse navdata without reciprocal pairs.
                else if (nearestRunway != null && nearestDist < 500)
                {
                    if (!string.IsNullOrEmpty(holdPointName))
                        node.HoldShortName = $"{holdPointName}, Runway {nearestRunway}";
                    else
                        node.HoldShortName = $"Runway {nearestRunway}";
                }
                else if (!string.IsNullOrEmpty(holdPointName))
                {
                    node.HoldShortName = holdPointName;
                }
            }
        }

        // Compute connected components so start-node selectors can filter by
        // reachability to a known destination. Runs after all edges and node
        // upgrades are in place.
        graph.AssignConnectedComponents();

        return graph;
    }

    /// <summary>
    /// Assigns each node a ComponentId so callers can filter start-node candidates
    /// by reachability. Runs once at Build time after all edges are added. BFS over
    /// Adjacency; nodes in the same connected component share an integer ID
    /// starting at 0.
    ///
    /// Motivating defect: fs2024 navdata at GCLP models taxiway S5 as a 13-node
    /// island with no connection to any other taxiway at either terminus. A pilot
    /// touching down on 03L near S5 would have the start-node picker snap to an
    /// S5 node, and A* could never reach the chosen exit (in the main 1075-node
    /// component). With component IDs, the caller filters the start-node search
    /// to nodes co-component with the destination — the picker skips S5 and finds
    /// a reachable node on R3 instead.
    /// </summary>
    private void AssignConnectedComponents()
    {
        int nextComponentId = 0;
        var queue = new Queue<int>();

        foreach (var startNode in Nodes.Values)
        {
            if (startNode.ComponentId != -1) continue;
            int componentId = nextComponentId++;
            startNode.ComponentId = componentId;
            queue.Enqueue(startNode.NodeId);

            while (queue.Count > 0)
            {
                int currentId = queue.Dequeue();
                if (!Adjacency.TryGetValue(currentId, out var edges)) continue;
                foreach (var edge in edges)
                {
                    var neighbor = Nodes[edge.ToNodeId];
                    if (neighbor.ComponentId == -1)
                    {
                        neighbor.ComponentId = componentId;
                        queue.Enqueue(neighbor.NodeId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Subdivides the taxi edge nearest <paramref name="lat"/>/<paramref name="lon"/> at the
    /// perpendicular projection of that point and returns the inserted node — the fallback that
    /// lets a PAINTED holding point which sits on the pavement but between two navdata vertices
    /// be used as a destination. Returns null when no edge lies within
    /// <paramref name="maxPerpMeters"/>, so the caller still DROPS the point rather than
    /// misplacing it.
    ///
    /// <para>Motivating measurement (EGLL, 2026-08): 11 of the airport's 14 unresolved painted
    /// points sit ≤2 m from a taxiway centreline yet 31-63 m from the nearest vertex, because MSFS
    /// only puts vertices at junctions and bends. LOMAN is the clean case — 0.0 m from the taxiway
    /// A edge, 34.7 m from a vertex, and equidistant between the two vertices either side, so it is
    /// unreachable by ANY node-snap radius: widening the radius would place the hold ~35 m up or
    /// down the taxiway from the paint. Projection is the only placement that lands on the line.</para>
    ///
    /// <para>Topologically this is a pure subdivision: the new node has degree 2, the two halves
    /// sum to the original length, and no connectivity is created or removed — so A* costs and
    /// every route are unchanged, and the node is <see cref="TaxiNodeType.Normal"/> so it can never
    /// be mistaken for a scenery hold-short node by the (navdata-authoritative, safety-critical)
    /// hold-short placement walks. Parking connectors are skipped for the same reason
    /// <see cref="NamedHoldingPointResolver.SnapToNode"/> skips them: a stand connector is not a
    /// holding point. A projection landing on (or within the merge threshold of) an endpoint is
    /// refused — that case is already the node snap's, and splitting there would create a
    /// zero-length edge.</para>
    /// </summary>
    /// <summary>
    /// Node ids created by <see cref="InsertHoldingPointNodeOnEdge"/>. These are placements for
    /// PAINTED HOLD LINES, never junctions, so any search that is looking for real navdata
    /// topology must skip them — see the skip in <see cref="ResolveHoldingPointEntries"/>.
    /// </summary>
    private readonly HashSet<int> _holdingPointProjectionNodes = new();

    /// <summary>True when the node was inserted by the holding-point edge projection.</summary>
    public bool IsHoldingPointProjectionNode(int nodeId) =>
        _holdingPointProjectionNodes.Contains(nodeId);

    public TaxiNode? InsertHoldingPointNodeOnEdge(double lat, double lon, double maxPerpMeters)
    {
        TaxiEdge? bestEdge = null;
        double bestPerp = maxPerpMeters;
        double bestLat = 0, bestLon = 0;

        // Build stores every segment as a forward + reverse pair, so visit each undirected
        // pair once. Keyed on (min,max,name) rather than a From<To filter so a one-directional
        // edge (if one ever exists) is still considered.
        var visited = new HashSet<(int, int, string)>();

        foreach (var edges in Adjacency.Values)
        {
            foreach (var e in edges)
            {
                var key = e.FromNodeId < e.ToNodeId
                    ? (e.FromNodeId, e.ToNodeId, e.TaxiwayName)
                    : (e.ToNodeId, e.FromNodeId, e.TaxiwayName);
                if (!visited.Add(key)) continue;

                if (!Nodes.TryGetValue(e.FromNodeId, out var a) ||
                    !Nodes.TryGetValue(e.ToNodeId, out var b)) continue;
                if (a.Type == TaxiNodeType.Parking || b.Type == TaxiNodeType.Parking) continue;

                var (perp, t, projLat, projLon) = ProjectOntoSegmentClamped(
                    lat, lon, a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (perp >= bestPerp) continue;
                if (t <= 0.0 || t >= 1.0) continue;
                if (FastDistanceMeters(projLat, projLon, a.Latitude, a.Longitude) < MERGE_THRESHOLD_METERS) continue;
                if (FastDistanceMeters(projLat, projLon, b.Latitude, b.Longitude) < MERGE_THRESHOLD_METERS) continue;

                bestPerp = perp;
                bestEdge = e;
                bestLat = projLat;
                bestLon = projLon;
            }
        }

        return bestEdge == null ? null : SplitEdgeAt(bestEdge, bestLat, bestLon);
    }

    /// <summary>
    /// Replaces the undirected edge <paramref name="fwd"/> (and its reverse twin, when present)
    /// with two halves meeting at a new node placed at <paramref name="lat"/>/<paramref name="lon"/>.
    /// The new node inherits the edge's taxiway name (so taxiway-keyed lookups see it as part of
    /// that taxiway, like every other vertex on it) and its endpoint's ComponentId — correct by
    /// construction, since a subdivision cannot change reachability, and needed because
    /// AssignConnectedComponents has already run by the time holding points resolve.
    /// </summary>
    private TaxiNode SplitEdgeAt(TaxiEdge fwd, double lat, double lon)
    {
        int aId = fwd.FromNodeId, bId = fwd.ToNodeId;
        var a = Nodes[aId];
        var b = Nodes[bId];

        int newId = _nextNodeId++;
        var node = new TaxiNode
        {
            NodeId = newId,
            Latitude = lat,
            Longitude = lon,
            Type = TaxiNodeType.Normal,
            ComponentId = a.ComponentId,
        };
        if (!string.IsNullOrEmpty(fwd.TaxiwayName))
            node.TaxiwayNames.Add(fwd.TaxiwayName);

        Nodes[newId] = node;
        Adjacency[newId] = new List<TaxiEdge>();
        _holdingPointProjectionNodes.Add(newId);

        string hashKey = GetSpatialHashKey(lat, lon);
        if (!_spatialHash.ContainsKey(hashKey))
            _spatialHash[hashKey] = new List<int>();
        _spatialHash[hashKey].Add(newId);

        if (!string.IsNullOrEmpty(fwd.TaxiwayName))
            RegisterTaxiwayNode(fwd.TaxiwayName, newId);

        double distA = FastDistanceMeters(a.Latitude, a.Longitude, lat, lon);
        double distB = FastDistanceMeters(lat, lon, b.Latitude, b.Longitude);
        double bearAn = NavigationCalculator.CalculateBearing(a.Latitude, a.Longitude, lat, lon);
        double bearNb = NavigationCalculator.CalculateBearing(lat, lon, b.Latitude, b.Longitude);

        Adjacency[aId].Remove(fwd);
        TaxiEdge? rev = null;
        if (Adjacency.TryGetValue(bId, out var bEdges))
        {
            foreach (var e in bEdges)
            {
                if (e.ToNodeId == aId && e.TaxiwayName == fwd.TaxiwayName && e.PathType == fwd.PathType)
                {
                    rev = e;
                    break;
                }
            }
            if (rev != null) bEdges.Remove(rev);
        }

        AddEdge(Half(fwd, aId, newId, distA, bearAn));
        AddEdge(Half(fwd, newId, bId, distB, bearNb));
        if (rev != null)
        {
            AddEdge(Half(rev, bId, newId, distB, (bearNb + 180.0) % 360.0));
            AddEdge(Half(rev, newId, aId, distA, (bearAn + 180.0) % 360.0));
        }

        return node;

        static TaxiEdge Half(TaxiEdge src, int from, int to, double dist, double bearing) => new()
        {
            FromNodeId = from,
            ToNodeId = to,
            DistanceMeters = dist,
            TaxiwayName = src.TaxiwayName,
            BearingDegrees = bearing,
            WidthFeet = src.WidthFeet,
            PathType = src.PathType,
        };
    }

    /// <summary>
    /// Perpendicular distance and projection of a point onto the SEGMENT a→b, with the
    /// parameter clamped to [0,1]. Distinct from <see cref="ProjectOntoCenterline"/>, which
    /// deliberately leaves the parameter unclamped so callers can reason about along-track
    /// positions beyond a runway threshold.
    /// </summary>
    private static (double perp, double t, double projLat, double projLon) ProjectOntoSegmentClamped(
        double plat, double plon, double alat, double alon, double blat, double blon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos((alat + blat) * 0.5 * (Math.PI / 180.0));

        double bx = (blon - alon) * metersPerDegLon, by = (blat - alat) * METERS_PER_DEG_LAT;
        double px = (plon - alon) * metersPerDegLon, py = (plat - alat) * METERS_PER_DEG_LAT;

        double lenSq = bx * bx + by * by;
        if (lenSq < 1e-9)
            return (Math.Sqrt(px * px + py * py), 0.0, alat, alon);

        double t = (px * bx + py * by) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        double ex = px - t * bx, ey = py - t * by;
        return (Math.Sqrt(ex * ex + ey * ey), t,
                alat + t * (blat - alat), alon + t * (blon - alon));
    }

    /// <summary>
    /// Resolves a coordinate to an existing node (if within merge threshold) or creates a new one.
    /// </summary>
    private int ResolveNode(double lat, double lon, string nodeType, string? taxiwayName)
    {
        string hashKey = GetSpatialHashKey(lat, lon);

        // Check nearby cells for existing nodes within merge threshold
        foreach (var key in GetNearbyCellKeys(lat, lon))
        {
            if (_spatialHash.TryGetValue(key, out var nodeIds))
            {
                foreach (int nodeId in nodeIds)
                {
                    var existing = Nodes[nodeId];
                    // Distance-based merge check — consistent NS and EW at any latitude.
                    if (FastDistanceMeters(existing.Latitude, existing.Longitude, lat, lon) < MERGE_THRESHOLD_METERS)
                    {
                        // Upgrade node type if this endpoint is hold-short
                        UpgradeNodeType(existing, nodeType);
                        if (!string.IsNullOrEmpty(taxiwayName))
                            existing.TaxiwayNames.Add(taxiwayName);
                        return nodeId;
                    }
                }
            }
        }

        // Create new node
        int newId = _nextNodeId++;
        var newNode = new TaxiNode
        {
            NodeId = newId,
            Latitude = lat,
            Longitude = lon,
            Type = MapNodeType(nodeType)
        };
        if (!string.IsNullOrEmpty(taxiwayName))
            newNode.TaxiwayNames.Add(taxiwayName);

        Nodes[newId] = newNode;
        Adjacency[newId] = new List<TaxiEdge>();

        if (!_spatialHash.ContainsKey(hashKey))
            _spatialHash[hashKey] = new List<int>();
        _spatialHash[hashKey].Add(newId);

        return newId;
    }

    private void AddEdge(TaxiEdge edge)
    {
        if (!Adjacency.ContainsKey(edge.FromNodeId))
            Adjacency[edge.FromNodeId] = new List<TaxiEdge>();

        // Avoid duplicate edges
        bool exists = false;
        foreach (var e in Adjacency[edge.FromNodeId])
        {
            if (e.ToNodeId == edge.ToNodeId && e.TaxiwayName == edge.TaxiwayName)
            {
                exists = true;
                break;
            }
        }
        if (!exists)
            Adjacency[edge.FromNodeId].Add(edge);
    }

    private void RegisterTaxiwayNode(string taxiwayName, int nodeId)
    {
        if (!_taxiwayNodeIndex.ContainsKey(taxiwayName))
            _taxiwayNodeIndex[taxiwayName] = new List<int>();
        if (!_taxiwayNodeIdSet.TryGetValue(taxiwayName, out var seen))
        {
            seen = new HashSet<int>();
            _taxiwayNodeIdSet[taxiwayName] = seen;
        }
        // HashSet.Add returns false (and is a no-op) when nodeId is already present, so this
        // preserves the exact same dedup semantics — and the exact same list order — as the
        // original List.Contains check, just in O(1) instead of O(n).
        if (seen.Add(nodeId))
            _taxiwayNodeIndex[taxiwayName].Add(nodeId);
    }

    /// <summary>
    /// Returns every node id that has at least one edge whose <see cref="TaxiEdge.TaxiwayName"/>
    /// matches <paramref name="name"/> (case-insensitive, same comparer as the index and every
    /// TaxiRouter scan site). Backed by the private <c>_taxiwayNodeIndex</c> built during
    /// <see cref="Build"/> — O(1) lookup instead of a full <see cref="Adjacency"/> scan. Returns an
    /// empty list (never null) for an unknown/unregistered taxiway name.
    /// </summary>
    public IReadOnlyList<int> GetNodesOnTaxiway(string name)
    {
        if (string.IsNullOrEmpty(name))
            return Array.Empty<int>();
        return _taxiwayNodeIndex.TryGetValue(name, out var nodes) ? nodes : Array.Empty<int>();
    }

    /// <summary>
    /// Every named taxiway edge in the graph as flat (name, endpoint-coordinate) tuples —
    /// airport pavement geometry for <c>SayIntentionsTaxiPathSnapper.Snap</c> to measure a
    /// SayIntentions taxi-path point against, instead of parsing an ATC clearance's
    /// phrasing. Returns a tuple rather than <see cref="TaxiEdge"/> (or the snapper's own
    /// <c>NamedEdge</c>) so Navigation carries no dependency on Services.SayIntentions.
    ///
    /// <see cref="Adjacency"/> stores BOTH directions of every physical segment — the
    /// forward copy in the start node's list, the reverse copy in the end node's — so a
    /// bare walk would double the snapper's per-point work for no benefit. Two node ids on
    /// one edge are never equal (<see cref="Build"/> skips degenerate same-node segments),
    /// so of a segment's two directional copies exactly one always has
    /// <c>FromNodeId &lt; ToNodeId</c>; keeping only that one selects exactly one
    /// representative per physical segment without a separate seen-set.
    ///
    /// A blank <see cref="TaxiEdge.TaxiwayName"/> is skipped — nothing downstream filters
    /// an unnamed segment out, so a blank would flow straight through into a snapped route
    /// as an empty leg name. An edge whose endpoint node id is missing from
    /// <see cref="Nodes"/> is also skipped rather than resolved to (0,0): Build() never
    /// leaves a dangling reference itself, but a (0,0) edge would sit ~5000 km from any
    /// real airport and could still win a nearest-edge distance comparison for an outlier
    /// point, so this stays defensive rather than assuming the invariant always holds.
    ///
    /// Output order is explicit and deterministic — sorted by ascending TaxiwayName, then by
    /// the FROM endpoint's latitude/longitude, then the TO endpoint's — and must never be
    /// replaced by a bare Adjacency/Dictionary walk: Dictionary&lt;TKey,TValue&gt; enumeration
    /// order is an implementation detail, not a contract. The snapper resolves nearest-edge
    /// ties with a strict "&lt;" (first candidate in the sequence wins on an exact tie), so a
    /// reordering here can silently change which taxiway a pilot is told — on a live LSZH
    /// capture one point was decided by a 3.24 cm margin, and two taxiways meeting at a
    /// junction can legitimately sit at an exactly equal distance from a point.
    ///
    /// The key is intrinsic to each edge's own data, deliberately NOT the node-id pair: node
    /// ids are stable only WITHIN one <see cref="Build"/> call — they are assigned by a
    /// monotonic counter in the order the caller's <c>paths</c> list was processed, not by
    /// anything about the edge's geography or name. So the very same physical edges fed to
    /// <see cref="Build"/> in a different order (a navdata re-import that changes
    /// <c>taxi_path_id</c> assignment, or any future caller that doesn't share today's
    /// <c>ORDER BY taxi_path_id</c> query) would silently reorder a node-id-keyed result even
    /// though the airport itself never changed. Sorting on name plus coordinates instead makes
    /// the order hold across rebuilds, independent of processing order — it depends only on
    /// the edges themselves. This introduces no new ties: <see cref="ResolveNode"/>'s merge
    /// check (endpoints within <see cref="MERGE_THRESHOLD_METERS"/> of each other are merged
    /// into one node) guarantees two DISTINCT nodes can never share bit-identical coordinates
    /// — they would have been merged into one — so a coordinate+name key is exactly as
    /// tie-free as the node-id key it replaces.
    /// </summary>
    public IEnumerable<(string Name, double FromLat, double FromLon, double ToLat, double ToLon)> GetNamedEdges()
    {
        var candidates = new List<TaxiEdge>();

        foreach (var nodeEdges in Adjacency.Values)
        {
            foreach (var edge in nodeEdges)
            {
                if (string.IsNullOrEmpty(edge.TaxiwayName)) continue;
                // Reciprocal copy of this physical segment — the other direction
                // represents it (see method doc comment).
                if (edge.FromNodeId >= edge.ToNodeId) continue;
                if (!Nodes.ContainsKey(edge.FromNodeId) || !Nodes.ContainsKey(edge.ToNodeId)) continue;
                candidates.Add(edge);
            }
        }

        // Explicit, total-order sort — never rely on Dictionary/Adjacency enumeration
        // order, and never on FromNodeId/ToNodeId. See method doc comment for why: node ids
        // are stable only within one Build() call (assigned by processing order), not across
        // rebuilds, so a node-id key can silently reorder the same physical airport. Keyed on
        // TaxiwayName, then the FROM endpoint's coordinates, then the TO endpoint's —
        // intrinsic to the edge's own data, so the order holds regardless of the order paths
        // were supplied to Build(). All candidates already passed the Nodes-containment check
        // above, so these lookups cannot throw.
        candidates.Sort((a, b) =>
        {
            int cmp = string.CompareOrdinal(a.TaxiwayName, b.TaxiwayName);
            if (cmp != 0) return cmp;
            var fromA = Nodes[a.FromNodeId];
            var fromB = Nodes[b.FromNodeId];
            cmp = fromA.Latitude.CompareTo(fromB.Latitude);
            if (cmp != 0) return cmp;
            cmp = fromA.Longitude.CompareTo(fromB.Longitude);
            if (cmp != 0) return cmp;
            var toA = Nodes[a.ToNodeId];
            var toB = Nodes[b.ToNodeId];
            cmp = toA.Latitude.CompareTo(toB.Latitude);
            if (cmp != 0) return cmp;
            return toA.Longitude.CompareTo(toB.Longitude);
        });

        var result = new List<(string Name, double FromLat, double FromLon, double ToLat, double ToLon)>(candidates.Count);
        foreach (var edge in candidates)
        {
            var from = Nodes[edge.FromNodeId];
            var to = Nodes[edge.ToNodeId];
            result.Add((edge.TaxiwayName, from.Latitude, from.Longitude, to.Latitude, to.Longitude));
        }
        return result;
    }

    /// <summary>
    /// Finds the nearest graph node to a given position. When
    /// <paramref name="requiredComponentId"/> is set, only nodes in that
    /// connected component are considered (the spatial-hash ring and the
    /// full-scan fallback both honour it) — used to keep an aircraft's start
    /// node in the destination's component.
    /// </summary>
    public TaxiNode? FindNearestNode(double lat, double lon, int? requiredComponentId = null)
    {
        // Fast path: search the spatial hash with an expanding ring of cells.
        // Precision 5 = ~1.1m cells at equator. Rings 1, 3, 10, 30 cover up to ~330m cheaply.
        foreach (int ringRadius in new[] { 1, 3, 10, 30 })
        {
            TaxiNode? best = null;
            double bestDist = double.MaxValue;

            double step = Math.Pow(10, -SPATIAL_HASH_PRECISION);
            for (int dlat = -ringRadius; dlat <= ringRadius; dlat++)
            {
                for (int dlon = -ringRadius; dlon <= ringRadius; dlon++)
                {
                    string key = GetSpatialHashKey(lat + dlat * step, lon + dlon * step);
                    if (_spatialHash.TryGetValue(key, out var nodeIds))
                    {
                        foreach (int nodeId in nodeIds)
                        {
                            var node = Nodes[nodeId];
                            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                                continue;
                            double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                best = node;
                            }
                        }
                    }
                }
            }
            if (best != null) return best;
        }

        // Fallback: full scan (rare — only hit for coordinates outside all airport nodes)
        TaxiNode? fallback = null;
        double fallbackDist = double.MaxValue;
        foreach (var node in Nodes.Values)
        {
            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                continue;
            double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (dist < fallbackDist)
            {
                fallbackDist = dist;
                fallback = node;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Finds the nearest graph node lying on a named taxiway. Heading-independent.
    /// Use case: snapping a route start onto the user's first ATC-cleared taxiway
    /// regardless of aircraft orientation (e.g. immediately after pushback). Caller
    /// can pass <paramref name="requiredComponentId"/> to restrict candidates to a
    /// connected component (typically the destination's) so isolated-island
    /// taxiways are skipped — see <see cref="FindNearestNodeInDirection"/>. Returns
    /// null if no node on <paramref name="taxiwayName"/> lies within
    /// <paramref name="maxDistanceM"/> of the position (and within the requested
    /// component if set).
    /// </summary>
    public TaxiNode? FindNearestNodeOnTaxiway(
        double lat, double lon, string taxiwayName,
        double maxDistanceM = 800.0,
        int? requiredComponentId = null)
    {
        if (string.IsNullOrEmpty(taxiwayName)) return null;

        TaxiNode? best = null;
        double bestDist = maxDistanceM;

        foreach (var node in Nodes.Values)
        {
            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                continue;
            if (!node.TaxiwayNames.Contains(taxiwayName)) continue;
            double d = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (d < bestDist)
            {
                bestDist = d;
                best = node;
            }
        }

        return best;
    }

    /// <summary>
    /// Finds the nearest graph node in the direction the aircraft is facing.
    /// Returns the closest node that is roughly ahead (within ±90° of heading),
    /// bounded by MAX_START_NODE_DISTANCE_M — if nothing ahead is within range,
    /// falls back to the overall nearest node (also distance-bounded), otherwise
    /// returns null. A null return means "no taxiway node near this position" —
    /// caller should report "no nearby taxiway" rather than silently snap to
    /// something far away. Caller can pass <paramref name="requiredComponentId"/>
    /// to restrict candidates (including the fallback) to a connected component
    /// (typically the destination's) so isolated-island taxiways are skipped —
    /// see <see cref="FindNearestNodeOnTaxiway"/>.
    /// </summary>
    public TaxiNode? FindNearestNodeInDirection(
        double lat, double lon, double headingDeg,
        int? requiredComponentId = null)
    {
        // Tiered caps: prefer ahead-of-aircraft nodes within 300m (the common case at
        // a gate pushback), widen to 800m before giving up. Small airports with a large
        // apron between parking and the taxi network routinely produce >300m gaps; 800m
        // still rejects "you're flying over the field" cases while keeping small strips usable.
        const double PREFERRED_DISTANCE_M = 300.0;
        const double MAX_START_NODE_DISTANCE_M = 800.0;

        TaxiNode? preferred = null;    double preferredScore = double.MaxValue;
        TaxiNode? extended = null;     double extendedScore = double.MaxValue;

        foreach (var node in Nodes.Values)
        {
            // Component filter: when a destination is known, candidate start nodes
            // must be in the same connected component or A* will fail. Defends
            // against navdata defects where a nearby taxiway is an isolated island
            // (e.g. GCLP S5 in fs2024 — 13 nodes, 0 external connections).
            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                continue;

            double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (dist < 5) continue;                         // skip nodes right under us
            if (dist > MAX_START_NODE_DISTANCE_M) continue; // too far to be "at" the airport

            double bearing = NavigationCalculator.CalculateBearing(lat, lon, node.Latitude, node.Longitude);
            double angleDiff = Math.Abs(NormalizeAngle(bearing - headingDeg));

            if (angleDiff > 90) continue; // behind us

            // Score: prioritize closeness, with slight penalty for off-heading
            double score = dist + (angleDiff * 0.5);

            if (dist <= PREFERRED_DISTANCE_M)
            {
                if (score < preferredScore)
                {
                    preferredScore = score;
                    preferred = node;
                }
            }
            else if (score < extendedScore)
            {
                extendedScore = score;
                extended = node;
            }
        }

        if (preferred != null) return preferred;
        if (extended != null) return extended;

        // Nothing ahead — try the overall nearest, but only if within the extended range
        // AND (when filtering) in the requested component.
        var fallback = FindNearestNode(lat, lon);
        if (fallback == null) return null;
        if (requiredComponentId.HasValue && fallback.ComponentId != requiredComponentId.Value)
            return null;
        double fallbackDist = FastDistanceMeters(lat, lon, fallback.Latitude, fallback.Longitude);
        return fallbackDist <= MAX_START_NODE_DISTANCE_M ? fallback : null;
    }

    /// <summary>
    /// Describes what the aircraft is currently on: "Gate A25", "Runway 22L",
    /// "Taxiway Bravo", or "" if nothing plausible is nearby.
    ///
    /// Priority order (more specific wins):
    ///   1. Parking node within 40 m (gate).
    ///   2. Runway edge within half-width+5 m perpendicular distance (on the runway surface).
    ///      Runway edges are those with PathType indicating a runway (first char 'R').
    ///   3. Runway threshold node within 50 m (near a runway start).
    ///   4. Taxiway edge within half-width+3 m perpendicular distance (on a named taxiway).
    ///   5. Nearest node's first taxiway name as a fallback (within 60 m).
    ///
    /// This does NOT depend on guidance being active — it's a pure query against the graph.
    /// Caller is responsible for prepending " at ICAO" if desired.
    /// </summary>
    public string DescribeLocation(double lat, double lon)
    {
        const double PARKING_RADIUS_M = 40.0;
        const double RUNWAY_THRESHOLD_RADIUS_M = 50.0;
        const double NODE_FALLBACK_RADIUS_M = 60.0;
        const double EDGE_SCAN_RADIUS_M = 120.0; // generous — runway surfaces are wide

        // --- Pass 1: scan nearby nodes via spatial hash ---
        TaxiNode? nearestParking = null;      double nearestParkingDist = double.MaxValue;
        TaxiNode? nearestRunwayThreshold = null; double nearestRunwayDist = double.MaxValue;
        TaxiNode? nearestAnyNode = null;      double nearestAnyDist = double.MaxValue;

        double step = Math.Pow(10, -SPATIAL_HASH_PRECISION);
        // ring radius 30 cells ~= 330 m at equator — covers EDGE_SCAN_RADIUS_M comfortably
        for (int dlat = -30; dlat <= 30; dlat++)
        {
            for (int dlon = -30; dlon <= 30; dlon++)
            {
                string key = GetSpatialHashKey(lat + dlat * step, lon + dlon * step);
                if (!_spatialHash.TryGetValue(key, out var nodeIds)) continue;

                foreach (int nodeId in nodeIds)
                {
                    var node = Nodes[nodeId];
                    double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);

                    if (dist < nearestAnyDist)
                    {
                        nearestAnyDist = dist;
                        nearestAnyNode = node;
                    }

                    if (node.Type == TaxiNodeType.Parking &&
                        !string.IsNullOrEmpty(node.ParkingName) &&
                        !node.ParkingName.StartsWith("Runway", StringComparison.OrdinalIgnoreCase) &&
                        dist < nearestParkingDist)
                    {
                        nearestParkingDist = dist;
                        nearestParking = node;
                    }

                    if (!string.IsNullOrEmpty(node.ParkingName) &&
                        node.ParkingName.StartsWith("Runway", StringComparison.OrdinalIgnoreCase) &&
                        dist < nearestRunwayDist)
                    {
                        nearestRunwayDist = dist;
                        nearestRunwayThreshold = node;
                    }
                }
            }
        }

        // Gate wins if close enough
        if (nearestParking != null && nearestParkingDist <= PARKING_RADIUS_M)
        {
            string raw = nearestParking.ParkingName!.Trim();
            // Parking name may already be "Gate A25" or just "A25" / "G 10" — prefix "Gate" once
            if (raw.StartsWith("Gate", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Parking", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Ramp", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Tie", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Hangar", StringComparison.OrdinalIgnoreCase))
                return raw;
            return $"Gate {raw}";
        }

        // --- Pass 2: edge scan for on-runway / on-taxiway ---
        TaxiEdge? bestRunwayEdge = null;  double bestRunwayEdgePerp = double.MaxValue;
        TaxiEdge? bestTaxiwayEdge = null; double bestTaxiwayEdgePerp = double.MaxValue;

        // Collect candidate edges from adjacency of nodes within EDGE_SCAN_RADIUS_M, using the
        // same spatial-hash ring mechanism as Pass 1 (instead of scanning every node in the
        // graph). The ring must be sized independently in the lat and lon directions to cover
        // EDGE_SCAN_RADIUS_M in real METERS: a hash cell is a fixed DEGREE step (`step`, same in
        // both dimensions), but a degree of longitude is only cos(latitude) as many meters as a
        // degree of latitude — so a latitude-blind cell count under-covers longitude at higher
        // latitudes (e.g. ENSB at 78°N, cos(78°) ≈ 0.21). Pass 1's fixed "ring 30" is calibrated
        // for its own ≤60 m radii at the equator (and its own comment overstates that coverage —
        // 30 cells is ≈33 m, not "330 m" — a pre-existing, unrelated Pass-1 limitation left
        // untouched here); reusing "ring 30" as-is for this 120 m radius would silently drop real
        // candidates, so Pass 2 computes its own ring from EDGE_SCAN_RADIUS_M. Candidates are a
        // strict superset of what a latitude-correct 120 m circle would need (rectangular ring,
        // +1 cell margin for rounding) — never a subset — so this is equivalence-preserving with
        // the original full Adjacency scan, which itself is unconditionally correct because it
        // filters by true FastDistanceMeters, not by ring geometry.
        double metersPerDegLat = 111132.0;
        double metersPerDegLon = metersPerDegLat * Math.Cos(lat * (Math.PI / 180.0));
        if (metersPerDegLon < 1.0) metersPerDegLon = 1.0; // guard near-pole degeneracy (no real airport is this far north/south)
        int edgeScanRingLat = (int)Math.Ceiling(EDGE_SCAN_RADIUS_M / (step * metersPerDegLat)) + 1;
        int edgeScanRingLon = (int)Math.Ceiling(EDGE_SCAN_RADIUS_M / (step * metersPerDegLon)) + 1;

        var edgeScanCandidates = new HashSet<int>();
        for (int dlat = -edgeScanRingLat; dlat <= edgeScanRingLat; dlat++)
        {
            for (int dlon = -edgeScanRingLon; dlon <= edgeScanRingLon; dlon++)
            {
                string key = GetSpatialHashKey(lat + dlat * step, lon + dlon * step);
                if (_spatialHash.TryGetValue(key, out var cellNodeIds))
                {
                    foreach (int nodeId in cellNodeIds)
                        edgeScanCandidates.Add(nodeId);
                }
            }
        }

        // We dedupe by (from,to) pair since adjacency holds both directions.
        var visitedEdges = new HashSet<long>();

        foreach (int fromId in edgeScanCandidates)
        {
            if (!Adjacency.TryGetValue(fromId, out var edgesFromNode)) continue;
            var fromNode = Nodes[fromId];
            double fromDist = FastDistanceMeters(lat, lon, fromNode.Latitude, fromNode.Longitude);
            if (fromDist > EDGE_SCAN_RADIUS_M) continue;

            foreach (var edge in edgesFromNode)
            {
                // Dedupe
                long edgeKey = Math.Min(edge.FromNodeId, edge.ToNodeId) * 1_000_000L + Math.Max(edge.FromNodeId, edge.ToNodeId);
                if (!visitedEdges.Add(edgeKey)) continue;

                var toNode = Nodes[edge.ToNodeId];
                double perp = PerpendicularDistanceMeters(
                    lat, lon,
                    fromNode.Latitude, fromNode.Longitude,
                    toNode.Latitude, toNode.Longitude);

                // Half-width in meters (width stored as feet)
                double halfWidthM = (edge.WidthFeet * 0.3048) * 0.5;

                bool isRunway = !string.IsNullOrEmpty(edge.PathType) &&
                                edge.PathType.StartsWith("R", StringComparison.OrdinalIgnoreCase);

                double tolerance = isRunway ? halfWidthM + 5.0 : halfWidthM + 3.0;
                if (tolerance < 5.0) tolerance = 5.0; // minimum tolerance if width is missing/zero

                if (perp <= tolerance)
                {
                    if (isRunway)
                    {
                        if (perp < bestRunwayEdgePerp)
                        {
                            bestRunwayEdgePerp = perp;
                            bestRunwayEdge = edge;
                        }
                    }
                    else if (!string.IsNullOrEmpty(edge.TaxiwayName))
                    {
                        if (perp < bestTaxiwayEdgePerp)
                        {
                            bestTaxiwayEdgePerp = perp;
                            bestTaxiwayEdge = edge;
                        }
                    }
                }
            }
        }

        // On runway surface (graph-edge path) wins over near-threshold-node.
        // NOTE: this branch only fires for DBs that store runway centerlines as
        // taxi_path.type='R' rows. The current navdatareader schema does NOT —
        // every taxi_path row is type T / PT / P. The runway-centerline scan
        // below covers the common case using start-table threshold pairs.
        if (bestRunwayEdge != null && !string.IsNullOrEmpty(bestRunwayEdge.TaxiwayName))
            return $"Runway {bestRunwayEdge.TaxiwayName}";

        // Runway centerline scan (works for the whole length, not just the
        // thresholds). Each RunwayCenterline is the segment between the two
        // opposing-end thresholds; if the aircraft is within half-width-plus-
        // a-bit of that segment, it's on the runway. Pick the runway end whose
        // heading is closer to the aircraft's bearing along the centerline so
        // we report the correct designator (27L vs 09R).
        foreach (var rwy in RunwayCenterlines)
        {
            double perp = PerpendicularDistanceMeters(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            double tolerance = rwy.HalfWidthMeters + 5.0;
            if (perp > tolerance) continue;

            // Pick the directional name. For a stationary aircraft we can't use
            // its heading, so default to the end the aircraft is closer to:
            // it'll be lined up to take off in that direction. (For a rolling
            // aircraft this same convention happens to match the takeoff end.)
            double d1 = FastDistanceMeters(lat, lon, rwy.Lat1, rwy.Lon1);
            double d2 = FastDistanceMeters(lat, lon, rwy.Lat2, rwy.Lon2);
            string name = d1 <= d2 ? rwy.Name1 : rwy.Name2;
            if (string.IsNullOrEmpty(name)) name = rwy.Name1;
            return $"Runway {name}";
        }

        // Otherwise if we're near a runway threshold node
        if (nearestRunwayThreshold != null && nearestRunwayDist <= RUNWAY_THRESHOLD_RADIUS_M &&
            !string.IsNullOrEmpty(nearestRunwayThreshold.ParkingName))
            return nearestRunwayThreshold.ParkingName; // already "Runway 22L"

        // On taxiway
        if (bestTaxiwayEdge != null && !string.IsNullOrEmpty(bestTaxiwayEdge.TaxiwayName))
            return $"Taxiway {bestTaxiwayEdge.TaxiwayName}";

        // Fallback: nearest node's first taxiway name
        if (nearestAnyNode != null && nearestAnyDist <= NODE_FALLBACK_RADIUS_M &&
            nearestAnyNode.TaxiwayNames.Count > 0)
        {
            string name = nearestAnyNode.TaxiwayNames.First();
            return $"Near taxiway {name}";
        }

        return "";
    }

    /// <summary>
    /// Detects which runway the aircraft is sitting on, using the same half-width
    /// tolerance as DescribeLocation but exposing structured data for callers that
    /// need geometry (threshold lat/lon, true heading, designator) rather than a
    /// spoken string. Uses the aircraft's true heading to pick the correct
    /// reciprocal designator (e.g. 27L vs 09R) — the "threshold" is the upwind
    /// end of the runway, i.e. the end the aircraft is taking off FROM.
    /// </summary>
    /// <param name="lat">Aircraft latitude (degrees).</param>
    /// <param name="lon">Aircraft longitude (degrees).</param>
    /// <param name="aircraftHeadingTrue">
    /// Aircraft true heading in degrees. Used to pick the reciprocal designator.
    /// </param>
    /// <param name="runwayId">
    /// Out: runway designator (e.g. "27L"), no "Runway " prefix.
    /// </param>
    /// <param name="thresholdLat">Out: latitude of the upwind threshold.</param>
    /// <param name="thresholdLon">Out: longitude of the upwind threshold.</param>
    /// <param name="runwayHeadingTrue">
    /// Out: true heading of the runway in the takeoff direction (degrees, 0..360).
    /// </param>
    /// <returns>
    /// True if the aircraft is within half-width of a runway centerline. False
    /// if the aircraft is not on any runway in this graph's RunwayCenterlines list.
    /// </returns>
    public bool TryGetRunwayAtPosition(
        double lat, double lon, double aircraftHeadingTrue,
        out string runwayId,
        out double thresholdLat, out double thresholdLon,
        out double runwayHeadingTrue)
    {
        runwayId = "";
        thresholdLat = 0; thresholdLon = 0;
        runwayHeadingTrue = 0;

        foreach (var rwy in RunwayCenterlines)
        {
            double perp = PerpendicularDistanceMeters(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            // Strict half-width (no +5 m tolerance). Stricter than DescribeLocation
            // because takeoff-assist centerline math depends on the chosen runway
            // actually being the one under the aircraft — a 5 m fudge could
            // mis-attribute when the aircraft is sitting on a high-speed exit
            // immediately adjacent to a runway.
            if (perp > rwy.HalfWidthMeters) continue;

            // Pick the end whose takeoff heading is closer to the aircraft's
            // heading. End 1's takeoff heading is HeadingDeg1; end 2's is
            // HeadingDeg1 + 180 (mod 360).
            double hdg1 = NormalizeHeading(rwy.HeadingDeg1);
            double hdg2 = NormalizeHeading(rwy.HeadingDeg1 + 180.0);
            double diff1 = Math.Abs(NormalizeAngle(aircraftHeadingTrue - hdg1));
            double diff2 = Math.Abs(NormalizeAngle(aircraftHeadingTrue - hdg2));

            if (diff1 <= diff2)
            {
                runwayId = rwy.Name1;
                thresholdLat = rwy.Lat1;
                thresholdLon = rwy.Lon1;
                runwayHeadingTrue = hdg1;
            }
            else
            {
                runwayId = rwy.Name2;
                thresholdLat = rwy.Lat2;
                thresholdLon = rwy.Lon2;
                runwayHeadingTrue = hdg2;
            }

            // Fallback if the chosen end has an empty Name (shouldn't happen
            // in well-formed navdata, but defensive — an empty designator
            // would propagate into the spoken callout). When we fall over to
            // the other end's name, also re-point the threshold + heading to
            // that other end so the geometry stays consistent with the name.
            // If both names are empty, leave the geometry on the originally
            // chosen end — the empty runwayId will be the caller's signal
            // that data is malformed, but threshold + heading remain valid
            // approximations.
            if (string.IsNullOrEmpty(runwayId))
            {
                if (rwy.Name1.Length > 0)
                {
                    runwayId = rwy.Name1;
                    thresholdLat = rwy.Lat1;
                    thresholdLon = rwy.Lon1;
                    runwayHeadingTrue = hdg1;
                }
                else if (rwy.Name2.Length > 0)
                {
                    runwayId = rwy.Name2;
                    thresholdLat = rwy.Lat2;
                    thresholdLon = rwy.Lon2;
                    runwayHeadingTrue = hdg2;
                }
            }

            return true;
        }

        return false;
    }

    private static double NormalizeHeading(double deg)
    {
        deg = deg % 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }

    /// <summary>
    /// Enumerates the taxiways that meet a runway partway along its length — the
    /// valid intersection-departure points, one per MEETING POINT.
    /// Qualifying nodes on the same taxiway are clustered by along-track
    /// distance (a gap > 100 m starts a new cluster — paired high-speed-exit
    /// branches sharing one name sit 105-555 m apart in fs2024 navdata, while
    /// polyline nodes along a single entrance are far denser). Each cluster is
    /// ONE meeting point and contributes its node closest to the centerline,
    /// with distance measured from the DEPARTURE threshold (so "remaining" is
    /// the runway ahead in the takeoff direction).
    /// Sorted threshold-first. The threshold connector itself and the far-end
    /// nubs are filtered out.
    ///
    /// Parallel taxiways that run alongside the runway (e.g. a full-length
    /// parallel) never have a node within half-width, so they're correctly
    /// excluded — only taxiways that genuinely enter the runway are offered.
    ///
    /// The centerline is passed as explicit PHYSICAL geometry
    /// (<paramref name="thrLat"/>..<paramref name="farLon"/>, from the runway_end
    /// thresholds via the <c>Runway</c> model) — NOT the <see cref="RunwayCenterline"/>
    /// list, which is built from the <c>start</c> table and can sit hundreds of
    /// metres inside the pavement at displaced-threshold runways (that would make
    /// "remaining" badly understate the real runway length).
    /// </summary>
    /// <param name="thrLat">Departure-end threshold latitude (takeoff direction origin).</param>
    /// <param name="thrLon">Departure-end threshold longitude.</param>
    /// <param name="farLat">Opposite-end (rollout-end) latitude.</param>
    /// <param name="farLon">Opposite-end longitude.</param>
    /// <param name="halfWidthMeters">Runway half-width; a node must be within this of the centerline to count.</param>
    /// <param name="lineupLat">Optional: latitude of the full-length lineup point
    /// (start-table row). When given, meeting points at or before it (+50 m) are
    /// dropped — they are the NORMAL departure entrance, not an intersection
    /// shortcut. Matters at displaced-threshold runways (KJFK 22R: ~1 km displaced),
    /// where that entrance otherwise lists as a bogus "intersection".</param>
    /// <param name="lineupLon">Optional: longitude of the full-length lineup point.</param>
    public List<RunwayIntersection> GetRunwayIntersections(
        double thrLat, double thrLon, double farLat, double farLon, double halfWidthMeters,
        double? lineupLat = null, double? lineupLon = null)
    {
        var result = new List<RunwayIntersection>();

        double totalLen = FastDistanceMeters(thrLat, thrLon, farLat, farLon);
        if (totalLen < 1.0) return result;

        // Small tolerance above the stored half-width to absorb navdata rounding
        // at the runway edge (the node is often exactly on the centerline, but a
        // few metres of slop keeps a wide runway's entrance node in).
        double maxPerp = halfWidthMeters + 5.0;
        const double MIN_ALONG_M = 15.0;      // exclude the threshold connector itself
        const double MIN_REMAINING_M = 45.0;  // exclude far-end nubs (~150 ft left)

        // Meeting points at or before the full-length lineup point are the normal
        // departure entrance, not a shortcut; 50 m of margin absorbs connector
        // geometry around the lineup spot.
        const double FULL_LENGTH_MARGIN_M = 50.0;

        double minAlong = MIN_ALONG_M;
        if (lineupLat.HasValue && lineupLon.HasValue)
        {
            var (_, lineupAlong, _, _) = ProjectOntoCenterline(
                lineupLat.Value, lineupLon.Value, thrLat, thrLon, farLat, farLon);
            // Sanity clamp: a lineup point past mid-runway is a corrupt start
            // row — ignore the filter rather than emptying the list.
            if (lineupAlong > 0 && lineupAlong <= totalLen / 2.0)
                minAlong = Math.Max(minAlong, lineupAlong + FULL_LENGTH_MARGIN_M);
        }

        // Two qualifying nodes on the SAME taxiway further apart than this along
        // the runway are distinct meeting points (the paired branches of a
        // high-speed exit sharing one name). Over-splitting is benign — both
        // points are genuinely on the runway and the labels carry distances;
        // under-splitting hides a real branch, which is the failure that matters.
        const double CLUSTER_GAP_M = 100.0;

        foreach (var kv in _taxiwayNodeIndex)
        {
            string twName = kv.Key;
            if (string.IsNullOrEmpty(twName)) continue;

            // All qualifying nodes on this taxiway. Gating HERE — not after
            // picking a best node — means a taxiway's near-threshold connector
            // node (along < MIN_ALONG_M) or a far-end nub can't shadow a genuine
            // mid-field entrance further down the same taxiway.
            var candidates = new List<(double along, double perp, int nodeId, double lat, double lon)>();
            foreach (int nid in kv.Value)
            {
                if (!Nodes.TryGetValue(nid, out var n)) continue;
                var (perp, along, projLat, projLon) =
                    ProjectOntoCenterline(n.Latitude, n.Longitude, thrLat, thrLon, farLat, farLon);
                if (along < minAlong || along > totalLen) continue;
                if (totalLen - along < MIN_REMAINING_M) continue;
                if (perp > maxPerp) continue;
                candidates.Add((along, perp, nid, projLat, projLon));
            }
            if (candidates.Count == 0) continue;
            candidates.Sort((a, b) => a.along.CompareTo(b.along));

            // Walk the sorted candidates; a >CLUSTER_GAP_M along-track gap closes
            // the current cluster. Emit each cluster's min-perpendicular node.
            int clusterStart = 0;
            for (int i = 1; i <= candidates.Count; i++)
            {
                if (i < candidates.Count &&
                    candidates[i].along - candidates[i - 1].along <= CLUSTER_GAP_M)
                    continue;

                var best = candidates[clusterStart];
                for (int j = clusterStart + 1; j < i; j++)
                    if (candidates[j].perp < best.perp) best = candidates[j];

                result.Add(new RunwayIntersection
                {
                    TaxiwayName = twName,
                    NodeId = best.nodeId,
                    Latitude = best.lat,
                    Longitude = best.lon,
                    AlongMetersFromThreshold = best.along,
                    RemainingMeters = totalLen - best.along,
                });
                clusterStart = i;
            }
        }

        result.Sort((a, b) => a.AlongMetersFromThreshold.CompareTo(b.AlongMetersFromThreshold));
        return result;
    }

    /// <summary>
    /// Finds the taxiway→runway entrance node to use for a FULL-LENGTH BACKTRACK
    /// departure: the pilot enters the runway partway down, backtracks toward the
    /// departure threshold, turns around, and lines up full length.
    ///
    /// Purely GEOMETRIC and NAME-INDEPENDENT (unlike <see cref="GetRunwayIntersections"/>,
    /// which keys on taxiway names) — many third-party sceneries model backtrack
    /// airports with UNNAMED taxi_path segments (iniBuilds EGNM: every taxiway name
    /// is empty), so a name-based scan finds nothing there. This walks the graph
    /// nodes instead.
    ///
    /// <paramref name="thrLat"/>/<paramref name="thrLon"/> is the DEPARTURE-END
    /// threshold (the takeoff end of the named runway); <paramref name="farLat"/>/
    /// <paramref name="farLon"/> is the opposite end. An entrance qualifies when it:
    ///   • lies on the runway (perpendicular ≤ half-width + slop),
    ///   • is genuinely down-field of the departure threshold (so a backtrack is
    ///     actually required) yet not a far-end nub,
    ///   • has at least one graph neighbour OFF the runway (a real taxiway junction,
    ///     not a mid-runway centerline node), and
    ///   • is reachable from the aircraft (same connected component).
    /// Among those, the one CLOSEST to the departure threshold is returned — that
    /// minimises the backtrack distance and matches real procedure (e.g. EGNM 32
    /// enters at D1, ~575 m from the 32 threshold, not at the far-end A1). Returns
    /// null when the airport has no such entrance (caller falls back to a normal
    /// full-length departure).
    /// </summary>
    public TaxiNode? FindBacktrackEntryNode(
        double thrLat, double thrLon, double farLat, double farLon,
        double halfWidthMeters, double aircraftLat, double aircraftLon)
    {
        double totalLen = FastDistanceMeters(thrLat, thrLon, farLat, farLon);
        if (totalLen < 1.0) return null;

        double maxPerp = halfWidthMeters + 5.0;
        const double MIN_ALONG_M     = 40.0;  // past the threshold connector — a backtrack is genuinely needed
        const double MIN_REMAINING_M = 45.0;  // and on the runway proper, not a far-end nub

        // Reachability: restrict to the aircraft's connected component. A shared
        // ComponentId guarantees A* can path to the node (undirected graph), so
        // this filters out isolated pad/runway islands the apron can't reach.
        var acNode = FindNearestNode(aircraftLat, aircraftLon);
        if (acNode == null) return null;
        int comp = acNode.ComponentId;

        TaxiNode? best = null;
        double bestAlong = double.MaxValue;
        foreach (var node in Nodes.Values)
        {
            if (node.ComponentId != comp) continue;
            var (perp, along, _, _) = ProjectOntoCenterline(
                node.Latitude, node.Longitude, thrLat, thrLon, farLat, farLon);
            if (perp > maxPerp) continue;
            if (along < MIN_ALONG_M || along > totalLen - MIN_REMAINING_M) continue;
            if (!HasOffRunwayNeighbour(node, thrLat, thrLon, farLat, farLon, maxPerp)) continue;

            // Closest to the departure threshold = least backtrack. This is what
            // makes the choice DIRECTION-AWARE: measured from the takeoff-end
            // threshold, the nearest entrance is the correct one for THIS runway
            // direction (the reciprocal picks the entrance near the other end).
            if (along < bestAlong) { bestAlong = along; best = node; }
        }
        return best;
    }

    /// <summary>
    /// Resolves named PAINTED HOLDING POINTS (OSM aeroway=holding_position refs, e.g.
    /// LSZH "A2") to runway entry nodes, so the Taxi planner can offer "depart from
    /// holding point A2" even where the entry stub taxiway is UNNAMED in navdata
    /// (MK Studios LSZH: every taxi_path is nameless, so the name-keyed
    /// <see cref="GetRunwayIntersections"/> can't list those entries).
    ///
    /// GEOMETRIC and name-independent, mirroring <see cref="FindBacktrackEntryNode"/>:
    /// a holding point belongs to this runway when it sits within
    /// <paramref name="maxPointPerpMeters"/> of the centerline, and its entry node is the nearest
    /// same-component on-runway node with an off-runway neighbour (a real
    /// taxiway↔runway junction) within <paramref name="maxNodeDistMeters"/> of the
    /// point. The painted point only ever SELECTS the entry — hold-short placement on
    /// the resulting route stays authoritative from navdata (TruncateToHoldShort),
    /// per the augmentation anti-geometry rule.
    ///
    /// Returns one <see cref="RunwayIntersection"/> per resolved point (TaxiwayName
    /// carries the HOLDING-POINT name; Latitude/Longitude is the entry node projected
    /// onto the centerline — the same lineup-target convention as
    /// <see cref="GetRunwayIntersections"/>), sorted by distance from the departure
    /// threshold. Full-length entries are deliberately INCLUDED (unlike the
    /// intersection list): picking the full-length holding point by name (A2 vs A1)
    /// is exactly the use case. Same-name points resolving to the same entry node are
    /// deduplicated; distinct entries sharing a name are all returned (the caller's
    /// labels carry distances).
    ///
    /// <paramref name="thrLat"/>/<paramref name="thrLon"/> must be the DEPARTURE
    /// lineup point (the `start` table row), never the runway_end pavement edge —
    /// see the caller's note in TaxiAssistForm.PopulateHoldingPoints. Both along-track
    /// gates below are written against that anchor.
    ///
    /// BEHIND-THRESHOLD holds (EGCC 23L): some airports paint their full-length
    /// departure holds well BEHIND the threshold, on the lead-in taxiways of a
    /// holding/queue area — EGCC's Runway 2 has VB1 73 m and T1 430 m behind the 23L
    /// pavement edge, and the whole area was silently dropped by the old
    /// "ptAlong ≥ −30" gate (unlike EGKK 26L, whose set-back holding area is rescued
    /// by the start-row envelope, EGCC's start row sits INTO the runway so the
    /// envelope never grows backwards). Such a point is admitted only under THREE
    /// gates, all required, so other airports never gain false entries:
    /// (1) <paramref name="behindThresholdEligible"/> says its OSM
    /// holding_position:type marks a line that GUARDS a runway ("runway"/"ils" —
    /// never "intermediate" queue-ladder holds, never untagged points; callers
    /// without kind data pass null and keep the old behavior exactly);
    /// (2) no OTHER runway's centerline is closer to the point than this one's
    /// (see <see cref="AnotherRunwayClaimsPoint"/> — EGCC's V1–V6 sit almost midway
    /// between the parallels and must not migrate between lists), which needs
    /// <paramref name="runwayName"/> to tell self/reciprocal apart;
    /// (3) its entry binds no farther away than the threshold anchor itself is
    /// (+60 m slack), so a behind-threshold point can only ever select the
    /// full-length entry, never a junction downfield.
    /// </summary>
    public List<RunwayIntersection> ResolveHoldingPointEntries(
        IReadOnlyList<(string Name, double Lat, double Lon)> holdingPoints,
        double thrLat, double thrLon, double farLat, double farLon,
        double halfWidthMeters, double aircraftLat, double aircraftLon,
        double maxNodeDistMeters = 200.0,
        // Own tolerance rather than HOLDSHORT_RUNWAY_MATCH_M (150 m). That constant sizes
        // a DIFFERENT judgement — "which runway is this hold-short node protecting" — where
        // being tight matters, because EGKK's two centerlines run only ~200 m apart and a
        // loose gate would name a hold after the wrong runway. Here the runway is already
        // decided by the caller and the entry NODE still has to sit on this centerline, so
        // the point gate only has to be wide enough to admit a legitimately set-back
        // CAT II/III hold: EGKK's A3 is 162 m out and was silently missing from the picker.
        double maxPointPerpMeters = 200.0,
        // Opt-in for the behind-threshold admission above: given a point's NAME, is its
        // OSM kind one that guards a runway ("runway"/"ils")? Null = feature off.
        Func<string, bool>? behindThresholdEligible = null,
        // This runway's designator ("23L"), used only to exclude self + reciprocal from
        // the other-runway ownership guard. Null = guard unusable, behind-threshold
        // points stay excluded (safe default).
        string? runwayName = null)
    {
        var result = new List<RunwayIntersection>();
        if (holdingPoints == null || holdingPoints.Count == 0) return result;

        double totalLen = FastDistanceMeters(thrLat, thrLon, farLat, farLon);
        if (totalLen < 1.0) return result;

        double maxPerp = halfWidthMeters + 5.0;
        // The anchor is the departure lineup point, and a FULL-LENGTH entry stub meets
        // the runway right at it — often a few metres behind, since the lineup point is
        // where the nose sits and the stub joins the pavement abeam or short of that.
        // A 5 m floor (correct when this was anchored on the pavement edge) filtered the
        // full-length junction out entirely, and the full-length holding points then
        // snapped to the NEXT entry down the runway: at EGKK 26L, M1/M3 resolved to the
        // A entrance 112 m in, silently mislabelling which stub you were selecting.
        const double MIN_ALONG_M = -40.0;     // allow the full-length junction itself; still exclude off-end nubs
        const double MIN_REMAINING_M = 45.0;  // far-end nubs are not a usable departure entry

        // Reachability: same connected component as the aircraft (matches
        // FindBacktrackEntryNode — guarantees A* can path to the entry).
        var acNode = FindNearestNode(aircraftLat, aircraftLon);
        if (acNode == null) return result;
        int comp = acNode.ComponentId;

        // Behind-threshold admission window (gate 1 of the three in the doc comment).
        // 1000 m spans the largest holding area measured (EGCC V1 is 901 m back); the
        // eligibility/ownership/binding gates are what carry the precision.
        const double MAX_BEHIND_ALONG_M = 1000.0;

        var seen = new HashSet<(string, int)>();
        var scored = new List<(RunwayIntersection Entry, double PointToEntryMeters)>();
        foreach (var (name, hLat, hLon) in holdingPoints)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            // OSM names a runway-crossing hold line after the RUNWAY it protects
            // ("08L/26R" at EGKK), not after a taxiway. That is never something ATC
            // clears you to depart from, and in a picker it reads as a second runway
            // having appeared in the list — drop it.
            if (IsRunwayDesignatorLabel(name)) continue;

            // Does this painted point belong to THIS runway?
            var (ptPerp, ptAlong, _, _) = ProjectOntoCenterline(hLat, hLon, thrLat, thrLon, farLat, farLon);
            if (ptPerp > maxPointPerpMeters) continue;
            // Beyond the far end is the RECIPROCAL's holding area — always its list, never ours.
            if (ptAlong > totalLen) continue;

            double bindCap = maxNodeDistMeters;
            if (ptAlong < -30.0)
            {
                // Behind the threshold: admit only under all three gates (doc comment).
                if (ptAlong < -MAX_BEHIND_ALONG_M) continue;
                if (behindThresholdEligible == null || !behindThresholdEligible(name)) continue;
                if (runwayName == null ||
                    AnotherRunwayClaimsPoint(hLat, hLon, ptPerp, runwayName, MAX_BEHIND_ALONG_M)) continue;
                // Gate 3: the entry may sit no farther from the point than the threshold
                // anchor itself (+60 m connector slack) — structurally, only the
                // full-length entry (or something even nearer) can bind.
                bindCap = Math.Max(maxNodeDistMeters,
                    FastDistanceMeters(hLat, hLon, thrLat, thrLon) + 60.0);
            }

            // Nearest qualifying entry node to the painted point.
            TaxiNode? best = null;
            double bestDist = bindCap;
            double bestAlong = 0, bestProjLat = 0, bestProjLon = 0;
            foreach (var node in Nodes.Values)
            {
                if (node.ComponentId != comp) continue;
                // A hold-line projection node is a painted LINE's position, not a runway
                // junction. Skipping it keeps this entry list byte-identical to what it was
                // before the projection fallback existed: an earlier point in this same loop
                // can insert one, and a mid-stub node close enough to the centreline would
                // otherwise outrank the real junction and move the entry short of the runway.
                if (IsHoldingPointProjectionNode(node.NodeId)) continue;
                var (perp, along, projLat, projLon) = ProjectOntoCenterline(
                    node.Latitude, node.Longitude, thrLat, thrLon, farLat, farLon);
                if (perp > maxPerp) continue;
                if (along < MIN_ALONG_M || along > totalLen - MIN_REMAINING_M) continue;
                if (!HasOffRunwayNeighbour(node, thrLat, thrLon, farLat, farLon, maxPerp)) continue;

                double dist = FastDistanceMeters(node.Latitude, node.Longitude, hLat, hLon);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = node;
                    bestAlong = along;
                    bestProjLat = projLat;
                    bestProjLon = projLon;
                }
            }
            if (best == null) continue;
            if (!seen.Add((name.Trim().ToUpperInvariant(), best.NodeId))) continue;

            // The painted line's OWN node, snapped per point (not per name — a name can be
            // painted twice). Routing pins the route through it so the pilot taxis up THIS
            // stub; see RunwayIntersection.HoldNodeId for what goes wrong without it.
            // Same-component only (the pin must be routable) and never the entry node itself
            // (pinning to the destination is a no-op).
            int holdNodeId = 0;
            if (NamedHoldingPointResolver.SnapOrInsert(this, hLat, hLon) is { } holdSnap
                && holdSnap.Node.ComponentId == comp
                && holdSnap.Node.NodeId != best.NodeId)
            {
                holdNodeId = holdSnap.Node.NodeId;
            }

            scored.Add((new RunwayIntersection
            {
                TaxiwayName = name.Trim(),
                NodeId = best.NodeId,
                Latitude = bestProjLat,
                Longitude = bestProjLon,
                AlongMetersFromThreshold = bestAlong,
                RemainingMeters = totalLen - bestAlong,
                HoldNodeId = holdNodeId,
            }, bestDist));
        }

        // Along-track first (the picker's reading order). Several behind-threshold
        // holds share the full-length entry and so tie exactly on along-track; break
        // the tie by the painted line NEAREST its entry — the line closest to the
        // runway is "the normal clearance" AnnounceDefaultHoldingPoint promises to
        // name (EGCC 23L: VB1 at ~90 m beats T1 at ~430 m) — then by name so the
        // order is deterministic across sessions.
        scored.Sort((a, b) =>
        {
            int byAlong = a.Entry.AlongMetersFromThreshold.CompareTo(b.Entry.AlongMetersFromThreshold);
            if (byAlong != 0) return byAlong;
            int byDist = a.PointToEntryMeters.CompareTo(b.PointToEntryMeters);
            if (byDist != 0) return byDist;
            return string.Compare(a.Entry.TaxiwayName, b.Entry.TaxiwayName, StringComparison.OrdinalIgnoreCase);
        });
        result.AddRange(scored.Select(s => s.Entry));
        return result;
    }

    /// <summary>
    /// Ownership guard for a BEHIND-THRESHOLD holding point candidate (see
    /// <see cref="ResolveHoldingPointEntries"/>): true when some OTHER runway's
    /// centerline is strictly closer to the point than the runway being resolved is
    /// — the point is that runway's hold, not ours. "Other" excludes the resolved
    /// runway's own centerline by designator (either end — one centerline carries
    /// both names, so this also excludes the reciprocal). The along window is that
    /// runway's own span EXTENDED by the same behind-threshold band the admission
    /// uses (<paramref name="behindBandMeters"/>, both ends — the centerline serves
    /// both directions): the claim window must equal the admission window, or a
    /// point behind BOTH runways' thresholds is in neither narrow window and the
    /// guard never fires — at LEMD, Y-1 (36R's hold, 82 m from its line) was also
    /// admitted to 14L's list at 169 m perp through exactly that gap.
    /// </summary>
    private bool AnotherRunwayClaimsPoint(
        double lat, double lon, double perpToThisRunwayMeters, string runwayName, double behindBandMeters)
    {
        foreach (var cl in RunwayCenterlines)
        {
            // Shared matcher: this compare trimmed but did not fold the leading zero, so a
            // "9L"/"09L" spelling difference let a runway fail to recognise ITSELF here and
            // claim its own point.
            if (RouteRunwayCrossings.CenterlineHasDesignator(cl, runwayName)) continue;
            double len = FastDistanceMeters(cl.Lat1, cl.Lon1, cl.Lat2, cl.Lon2);
            if (len < 1.0) continue;
            var (perp, along, _, _) = ProjectOntoCenterline(lat, lon, cl.Lat1, cl.Lon1, cl.Lat2, cl.Lon2);
            if (along < -behindBandMeters || along > len + behindBandMeters) continue;
            // 5 m margin: a real claim is closer by tens of metres; the margin keeps a
            // same-runway centerline that slipped past the name check (designator
            // format drift) from "claiming" its own point on float noise, since that
            // line is collinear with the envelope line the caller measured against.
            if (perp < perpToThisRunwayMeters - 5.0) return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="node"/> has at least one adjacent node that is
    /// OFF the given runway centerline (perpendicular &gt; <paramref name="maxPerp"/>)
    /// — i.e. the node is a real taxiway↔runway junction, not a node buried in the
    /// runway pavement. Used by <see cref="FindBacktrackEntryNode"/> to reject
    /// mid-runway nodes that no taxiway actually connects to.
    /// </summary>
    private bool HasOffRunwayNeighbour(
        TaxiNode node, double thrLat, double thrLon, double farLat, double farLon, double maxPerp)
    {
        if (!Adjacency.TryGetValue(node.NodeId, out var edges)) return false;
        foreach (var e in edges)
        {
            int otherId = e.FromNodeId == node.NodeId ? e.ToNodeId : e.FromNodeId;
            if (!Nodes.TryGetValue(otherId, out var other)) continue;
            var (perp, _, _, _) = ProjectOntoCenterline(
                other.Latitude, other.Longitude, thrLat, thrLon, farLat, farLon);
            if (perp > maxPerp) return true;
        }
        return false;
    }

    /// <summary>
    /// Projects a point onto the runway centerline (a→b). Returns the
    /// perpendicular distance, the signed along-track distance from a (metres),
    /// and the foot-of-perpendicular point in lat/lon. Equirectangular frame —
    /// accurate at runway scale. Unlike <see cref="PerpendicularDistanceMeters"/>
    /// the along-track value is NOT clamped, so callers can reject points beyond
    /// the thresholds.
    /// </summary>
    private static (double perp, double along, double projLat, double projLon) ProjectOntoCenterline(
        double plat, double plon, double alat, double alon, double blat, double blon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos((alat + blat) * 0.5 * (Math.PI / 180.0));

        double bx = (blon - alon) * metersPerDegLon, by = (blat - alat) * METERS_PER_DEG_LAT;
        double px = (plon - alon) * metersPerDegLon, py = (plat - alat) * METERS_PER_DEG_LAT;

        double lenSq = bx * bx + by * by;
        if (lenSq < 1e-9)
            return (Math.Sqrt(px * px + py * py), 0.0, alat, alon);

        double t = (px * bx + py * by) / lenSq;
        double fx = t * bx, fy = t * by;
        double ex = px - fx, ey = py - fy;
        double perp = Math.Sqrt(ex * ex + ey * ey);
        double along = t * Math.Sqrt(lenSq);
        double projLat = alat + t * (blat - alat);
        double projLon = alon + t * (blon - alon);
        return (perp, along, projLat, projLon);
    }

    /// <summary>
    /// Runway designator a hold-short node holds short of, found by the NEAREST
    /// runway centerline (full length, perpendicular distance clamped to the
    /// threshold endpoints). Length-invariant: a hold-short where a taxiway
    /// crosses a long runway far from either threshold is still matched — unlike a
    /// distance-to-threshold test. Returns the closer-end designator (same
    /// convention as DescribeLocation / WhichRunwayContains), or null when no
    /// centerline is within <paramref name="maxMatchMeters"/> (the caller then
    /// falls back to the threshold heuristic). Public static for probe coverage.
    /// </summary>
    public static string? MatchHoldShortRunwayName(
        double lat, double lon,
        IReadOnlyList<RunwayCenterline> centerlines,
        double maxMatchMeters)
    {
        string? best = null;
        double bestPerp = double.MaxValue;
        foreach (var rwy in centerlines)
        {
            double perp = PerpendicularDistanceMeters(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            if (perp > maxMatchMeters || perp >= bestPerp) continue;

            // Closer-end designator (same convention as DescribeLocation): the end
            // the aircraft is nearer is the one it would line up to depart from.
            double d1 = FastDistanceMeters(lat, lon, rwy.Lat1, rwy.Lon1);
            double d2 = FastDistanceMeters(lat, lon, rwy.Lat2, rwy.Lon2);
            string name = d1 <= d2 ? rwy.Name1 : rwy.Name2;
            if (string.IsNullOrEmpty(name)) name = rwy.Name1;
            if (string.IsNullOrEmpty(name)) continue; // unnamed centerline — skip

            best = name;
            bestPerp = perp;
        }
        return best;
    }

    /// <summary>
    /// Public wrapper for the internal perpendicular-distance calculation, so
    /// other components (e.g. TaxiGuidanceManager.WhichRunwayContains) can do
    /// runway-pavement membership tests without duplicating the projection math.
    /// </summary>
    public static double PerpendicularDistanceMetersStatic(
        double plat, double plon,
        double alat, double alon,
        double blat, double blon)
        => PerpendicularDistanceMeters(plat, plon, alat, alon, blat, blon);

    /// <summary>
    /// Perpendicular distance (meters) from point (plat, plon) to segment (a→b).
    /// Uses equirectangular projection — accurate for taxiway-scale distances.
    /// Returns the distance to the nearest point on the segment (not the infinite line),
    /// so endpoints count when the foot of the perpendicular falls outside.
    /// </summary>
    private static double PerpendicularDistanceMeters(
        double plat, double plon,
        double alat, double alon,
        double blat, double blon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (alat + blat) * 0.5 * (Math.PI / 180.0);
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);

        double ax = 0.0, ay = 0.0;
        double bx = (blon - alon) * metersPerDegLon;
        double by = (blat - alat) * METERS_PER_DEG_LAT;
        double px = (plon - alon) * metersPerDegLon;
        double py = (plat - alat) * METERS_PER_DEG_LAT;

        double dx = bx - ax;
        double dy = by - ay;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9)
            return Math.Sqrt(px * px + py * py);

        double t = (px * dx + py * dy) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        double fx = t * dx;
        double fy = t * dy;
        double ex = px - fx;
        double ey = py - fy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>
    /// True when the taxi edge (a→b) crosses the runway centerline (t1→t2)
    /// between the thresholds — a proper segment-segment intersection.
    ///
    /// This is the CORRECT "does the route cross this runway" test. A taxiway
    /// crosses a runway via an EDGE that spans the pavement, with its endpoint
    /// NODES sitting OFF the runway on either side — so a "is a node ON the
    /// pavement?" test (perpendicular distance ≤ half-width) silently misses the
    /// crossing whenever the flanking nodes are more than ~half-width+5 m from
    /// the centerline. KBOS taxiway C over runway 04L is the motivating case:
    /// C plainly crosses 04L, but C's nearest node is 35 m from the 04L
    /// centerline (half-width is 25 m), so the node test found nothing and no
    /// hold-short was inserted — even though the route clearly traverses the
    /// runway. The edge-intersection test catches it regardless of node spacing.
    ///
    /// "Proper" (strict opposite-sides) intersection by design: a taxiway that
    /// merely touches a threshold endpoint or runs parallel alongside the runway
    /// is NOT flagged, avoiding false hold-shorts.
    /// </summary>
    public static bool EdgeCrossesRunwayStatic(
        double aLat, double aLon, double bLat, double bLon,
        double t1Lat, double t1Lon, double t2Lat, double t2Lon)
    {
        // Project the four points to a local planar frame (origin = a, x=east,
        // y=north, metres) — equirectangular, accurate at taxiway/runway scale.
        const double METERS_PER_DEG_LAT = 111132.0;
        double metersPerDegLon =
            METERS_PER_DEG_LAT * Math.Cos((aLat + bLat) * 0.5 * (Math.PI / 180.0));

        double p1x = 0.0, p1y = 0.0;
        double p2x = (bLon - aLon) * metersPerDegLon, p2y = (bLat - aLat) * METERS_PER_DEG_LAT;
        double p3x = (t1Lon - aLon) * metersPerDegLon, p3y = (t1Lat - aLat) * METERS_PER_DEG_LAT;
        double p4x = (t2Lon - aLon) * metersPerDegLon, p4y = (t2Lat - aLat) * METERS_PER_DEG_LAT;

        // Orientation of (b)/(t1)/(t2) relative to the two directed lines.
        double d1 = Orient(p3x, p3y, p4x, p4y, p1x, p1y); // p1 vs runway line
        double d2 = Orient(p3x, p3y, p4x, p4y, p2x, p2y); // p2 vs runway line
        double d3 = Orient(p1x, p1y, p2x, p2y, p3x, p3y); // t1 vs edge line
        double d4 = Orient(p1x, p1y, p2x, p2y, p4x, p4y); // t2 vs edge line

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));

        // Signed area (z of cross product) of (o→a)×(o→b): >0 left, <0 right.
        static double Orient(double ox, double oy, double ax, double ay, double bx, double by)
            => (ax - ox) * (by - oy) - (ay - oy) * (bx - ox);
    }

    /// <summary>
    /// Gets all unique taxiway names in the graph, sorted, including alias names so
    /// pilots can type an alternative name and still see it in dropdowns.
    /// Alias names are the human-readable forms collected from online sources.
    /// </summary>
    public List<string> GetAllTaxiwayNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edges in Adjacency.Values)
        {
            foreach (var edge in edges)
            {
                if (!string.IsNullOrEmpty(edge.TaxiwayName))
                    names.Add(edge.TaxiwayName);
            }
        }

        // Include alias display names (the human-readable online-source forms, e.g. "B" for a
        // navdata "HAWKER") so a pilot can SELECT the ATC/real name from the dropdown — the combo
        // is DropDownList (no free text), so an alias the pilot can't select would be useless.
        // Selecting an alias resolves to the canonical name at route time (ResolveTaxiwayName).
        // Skip any alias whose normalized form collides with a real taxiway name: that real name
        // is ALREADY in the list and ResolveTaxiwayName routes the bare name to the REAL taxiway
        // (collision guard), so surfacing e.g. "Z (K)" would be a mislabeled duplicate of the real
        // taxiway Z that, if selected, mis-routes to K. These collisions are common at rich,
        // junction-dense airports (OMDB: ~130 such labels) where a navdata segment's midpoint
        // matches a DIFFERENT-named crossing online segment. The normalized alias is captured at
        // Build time in _aliasDisplayToNormalized — do NOT re-derive it by parsing the label, since
        // a canonical name containing " (" (e.g. "RAMP (NORTH)") splits at the wrong paren.
        foreach (var display in AliasDisplayToCanonical.Keys)
        {
            string normAlias = _aliasDisplayToNormalized.TryGetValue(display, out var na) ? na : "";
            if (!string.IsNullOrEmpty(normAlias) && _normalizedRealNames.Contains(normAlias))
                continue;
            names.Add(display);
        }

        var sorted = names.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    /// <summary>
    /// Resolves an entered taxiway name to the canonical navdata name via the alias map.
    /// If <paramref name="entered"/> (normalized) is a known alias, returns the canonical
    /// navdata name stored in the graph. Otherwise returns <paramref name="entered"/> unchanged.
    /// Comparison is normalized (case-insensitive, spaces stripped, TWY/TAXIWAY prefix stripped).
    /// </summary>
    public string ResolveTaxiwayName(string entered)
    {
        if (string.IsNullOrWhiteSpace(entered))
            return entered;

        // Exact match on a labeled dropdown alias ("B (HAWKER)") → canonical navdata name.
        if (AliasDisplayToCanonical.TryGetValue(entered.Trim(), out string? byLabel))
            return byLabel;

        string normalized = MSFSBlindAssist.Services.TaxiAugment.TaxiDataMerger
            .NormalizeTaxiwayName(entered);

        // Collision guard: if the entered name IS a real taxiway, never remap it to an alias
        // canonical — a legitimate "B" clearance must route to the real "B", not to whatever
        // online source happened to also call "B" by another name.
        if (!string.IsNullOrEmpty(normalized) && !_normalizedRealNames.Contains(normalized) &&
            TaxiwayAliasToCanonical.TryGetValue(normalized, out string? canonical))
        {
            return canonical;
        }

        return entered;
    }

    /// <summary>
    /// Gets taxiway names sorted by distance from a given position, closest first.
    /// Only returns taxiways that have at least one node within the direction the aircraft is facing.
    /// </summary>
    public List<string> GetTaxiwayNamesSortedByDistance(double lat, double lon, double headingDeg)
    {
        var taxiwayDistances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _taxiwayNodeIndex)
        {
            string name = kvp.Key;
            double bestDist = double.MaxValue;

            foreach (int nodeId in kvp.Value)
            {
                var node = Nodes[nodeId];
                double dist = CalculateDistanceMeters(lat, lon, node.Latitude, node.Longitude);
                if (dist < bestDist)
                    bestDist = dist;
            }

            taxiwayDistances[name] = bestDist;
        }

        // Sort by distance
        return taxiwayDistances
            .OrderBy(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Gets the closest taxiway name in the direction the aircraft is facing.
    /// Considers both distance and heading alignment.
    /// </summary>
    public string? GetClosestTaxiwayInDirection(double lat, double lon, double headingDeg)
    {
        string? bestName = null;
        double bestScore = double.MaxValue;

        foreach (var kvp in _taxiwayNodeIndex)
        {
            string name = kvp.Key;

            foreach (int nodeId in kvp.Value)
            {
                var node = Nodes[nodeId];
                double dist = CalculateDistanceMeters(lat, lon, node.Latitude, node.Longitude);
                if (dist < 10) continue; // too close, probably under us

                double bearing = NavigationCalculator.CalculateBearing(lat, lon, node.Latitude, node.Longitude);
                double angleDiff = Math.Abs(NormalizeAngle(bearing - headingDeg));

                if (angleDiff > 90) continue; // behind us

                // Weighted score: distance + angle penalty
                double score = dist + (angleDiff * 2.0);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestName = name;
                }
            }
        }

        return bestName;
    }

    /// <summary>
    /// Gets taxiway names that connect to a given taxiway (within a small number
    /// of named-taxiway crossings, freely traversing along the seed and unnamed
    /// connectors).
    /// </summary>
    public List<string> GetConnectedTaxiwayNames(string taxiwayName)
    {
        // 2 named-taxiway crossings is the right granularity for surfacing
        // adjacent taxiways: it catches direct neighbors (1 crossing) and
        // one step further (e.g., a parallel taxiway reached via an
        // intermediate connector taxiway). The router does the heavy lifting
        // for actual path-finding; this is just to prioritize relevant items
        // in the dropdown UI.
        return GetReachableTaxiwayNames(taxiwayName, maxCrossings: 2);
    }

    /// <summary>
    /// Returns named taxiways reachable from any node on <paramref name="taxiwayName"/>
    /// within at most <paramref name="maxCrossings"/> NAMED-TAXIWAY transitions.
    /// Walking along the seed taxiway and through unnamed connector edges is
    /// FREE (does not consume the crossing budget). Only crossing into a
    /// different named taxiway counts as a crossing.
    ///
    /// This matches how ATC clearances read: a clearance like "M5 M1 A L" is
    /// 3 crossings end-to-end. Each step of the clearance corresponds to one
    /// crossing in this metric, regardless of how many unnamed connector
    /// segments physically lie between the two named taxiways. Counting raw
    /// graph hops (the previous behavior) silently hid taxiways like M1 from
    /// the M5 dropdown at KSFO, where M5 and M1 are connected via 4-6
    /// unnamed connector segments rather than sharing a node.
    /// </summary>
    public List<string> GetReachableTaxiwayNames(string taxiwayName, int maxCrossings = 2)
    {
        var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!_taxiwayNodeIndex.TryGetValue(taxiwayName, out var seedNodes))
            return new List<string>();

        // BFS state: (nodeId, currentNamedTaxiway, crossingsUsed). The
        // currentNamedTaxiway carries the context of which named entity we're
        // logically on — staying on it (or walking unnamed connectors that
        // don't "name" anything) is free; only crossing INTO a different
        // named taxiway counts. visited is keyed on (nodeId, currentTaxiway)
        // so an intersection node can be revisited from different taxiway
        // contexts (each context may yield different reachability), bounded
        // by the small number of named taxiways crossing any one node.
        var visited = new HashSet<(int, string)>();
        var queue = new Queue<(int nodeId, string currentTaxiway, int crossings)>();
        foreach (int n in seedNodes)
        {
            if (visited.Add((n, taxiwayName)))
                queue.Enqueue((n, taxiwayName, 0));
        }

        while (queue.Count > 0)
        {
            var (current, currentTw, crossings) = queue.Dequeue();
            if (!Adjacency.TryGetValue(current, out var edges)) continue;

            foreach (var edge in edges)
            {
                string edgeName = edge.TaxiwayName ?? string.Empty;
                int nextCrossings;
                string nextTw;

                if (string.IsNullOrEmpty(edgeName))
                {
                    // Unnamed connector — stay logically on the current
                    // named taxiway and don't consume a crossing.
                    nextTw = currentTw;
                    nextCrossings = crossings;
                }
                else if (edgeName.Equals(currentTw, StringComparison.OrdinalIgnoreCase))
                {
                    // Walking along the same named taxiway — free.
                    nextTw = currentTw;
                    nextCrossings = crossings;
                }
                else
                {
                    // Crossing into a different named taxiway.
                    nextTw = edgeName;
                    nextCrossings = crossings + 1;
                }

                // Bound check applies to BOTH the enqueue and the collection
                // step: a name reached at nextCrossings > maxCrossings is
                // beyond the budget and must not appear in the result. (The
                // earlier version added the name BEFORE the bound check,
                // which silently widened the dropdown by one extra crossing
                // — a name at distance 3 would surface for maxCrossings=2.)
                if (nextCrossings > maxCrossings) continue;

                // Only collect distinct named taxiways (skip unnamed connectors
                // and skip self-references back to the seed).
                if (!string.IsNullOrEmpty(edgeName) &&
                    !edgeName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase) &&
                    !edgeName.Equals(currentTw, StringComparison.OrdinalIgnoreCase))
                {
                    collected.Add(edgeName);
                }

                if (!visited.Add((edge.ToNodeId, nextTw))) continue;
                queue.Enqueue((edge.ToNodeId, nextTw, nextCrossings));
            }
        }

        return collected.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Returns the node on <paramref name="fromTaxiway"/> that also has an outgoing edge on
    /// <paramref name="toTaxiway"/> within the given connected component — i.e. the intersection
    /// node where the two taxiways meet.  Used by the progressive-taxi terminator to locate
    /// "hold short of taxiway Y while on taxiway X".
    /// Returns -1 when no such node exists in the component.
    /// </summary>
    public int FindTaxiwayIntersectionNode(string fromTaxiway, string toTaxiway, int requiredComponentId)
    {
        foreach (var kvp in Adjacency)
        {
            int nodeId = kvp.Key;
            if (!Nodes.TryGetValue(nodeId, out var node) || node.ComponentId != requiredComponentId)
                continue;
            bool onFrom = false, onTo = false;
            foreach (var e in kvp.Value)
            {
                if (e.TaxiwayName.Equals(fromTaxiway, StringComparison.OrdinalIgnoreCase)) onFrom = true;
                else if (e.TaxiwayName.Equals(toTaxiway, StringComparison.OrdinalIgnoreCase)) onTo = true;
            }
            if (onFrom && onTo) return nodeId;
        }
        return -1;
    }

    /// <summary>
    /// Returns the node on <paramref name="taxiway"/> (in the same connected component as
    /// <paramref name="fromNodeId"/>) that is farthest — by straight-line distance — from
    /// <paramref name="fromNodeId"/>.  Used by the progressive-taxi terminator to locate
    /// the "end of taxiway" destination.
    /// Returns -1 when <paramref name="fromNodeId"/> is not found or no node on
    /// <paramref name="taxiway"/> exists in the component.
    /// </summary>
    public int FindTaxiwayEndNode(int fromNodeId, string taxiway)
    {
        if (!Nodes.TryGetValue(fromNodeId, out var from)) return -1;
        int comp = from.ComponentId;
        int best = -1;
        double bestDist = -1;
        foreach (var kvp in Adjacency)
        {
            int nodeId = kvp.Key;
            if (!Nodes.TryGetValue(nodeId, out var node) || node.ComponentId != comp) continue;
            if (!kvp.Value.Any(e => e.TaxiwayName.Equals(taxiway, StringComparison.OrdinalIgnoreCase))) continue;
            double d = FastDistanceMeters(from.Latitude, from.Longitude, node.Latitude, node.Longitude);
            if (d > bestDist) { bestDist = d; best = nodeId; }
        }
        return best;
    }

    /// <summary>
    /// Gets the edge between two adjacent nodes on a specific taxiway, or any edge if taxiway is empty.
    /// </summary>
    public TaxiEdge? GetEdge(int fromNodeId, int toNodeId, string? preferredTaxiway = null)
    {
        if (!Adjacency.TryGetValue(fromNodeId, out var edges))
            return null;

        TaxiEdge? fallback = null;
        foreach (var edge in edges)
        {
            if (edge.ToNodeId != toNodeId) continue;
            if (!string.IsNullOrEmpty(preferredTaxiway) &&
                edge.TaxiwayName.Equals(preferredTaxiway, StringComparison.OrdinalIgnoreCase))
                return edge;
            fallback ??= edge;
        }
        return fallback;
    }

    #region Helpers

    private static string GetSpatialHashKey(double lat, double lon)
    {
        return $"{Math.Round(lat, SPATIAL_HASH_PRECISION)},{Math.Round(lon, SPATIAL_HASH_PRECISION)}";
    }

    private static IEnumerable<string> GetNearbyCellKeys(double lat, double lon)
    {
        double step = Math.Pow(10, -SPATIAL_HASH_PRECISION);
        for (int dlat = -1; dlat <= 1; dlat++)
        {
            for (int dlon = -1; dlon <= 1; dlon++)
            {
                yield return GetSpatialHashKey(lat + dlat * step, lon + dlon * step);
            }
        }
    }

    private static void UpgradeNodeType(TaxiNode node, string typeCode)
    {
        var newType = MapNodeType(typeCode);
        // Only upgrade: Normal → HoldShort → ILSHoldShort
        if (newType > node.Type)
            node.Type = newType;
    }

    private static TaxiNodeType MapNodeType(string typeCode)
    {
        return typeCode?.ToUpperInvariant() switch
        {
            "HS" or "HSND" => TaxiNodeType.HoldShort,
            "IHS" or "IHSND" => TaxiNodeType.ILSHoldShort,
            "P" => TaxiNodeType.Parking,
            _ => TaxiNodeType.Normal
        };
    }

    public static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        return NavigationCalculator.CalculateDistance(lat1, lon1, lat2, lon2) * 1852.0; // NM to meters
    }

    /// <summary>
    /// Fast equirectangular distance approximation in meters — accurate to &lt;1 cm at taxiway
    /// scale (&lt; a few km), ~2 orders of magnitude cheaper than Haversine. Use in per-frame hot
    /// paths (e.g. segment advance, look-ahead, incursion scan). Do NOT use for long distances.
    /// </summary>
    public static double FastDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latRad = (lat1 + lat2) * 0.5 * (Math.PI / 180.0);
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latRad);
        double dLat = (lat2 - lat1) * METERS_PER_DEG_LAT;
        double dLon = (lon2 - lon1) * metersPerDegLon;
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    /// <summary>
    /// Of a runway end's `start`-table rows, the FULL-LENGTH departure point — the one
    /// furthest back along the takeoff direction.
    ///
    /// A runway end can carry several rows, and taking whichever the DB returned first picks
    /// an arbitrary one: EGLL 09R's first row is 342 m down the runway while a 67 m row sits
    /// right there, and VRMM 18's is 826 m in versus 28 m. 14 airports in the fs2020 navdata
    /// carry duplicates, and 13 runway ends were choosing a worse row. The full-length point
    /// is by definition the furthest back, so there is nothing to weigh up.
    ///
    /// Rows are compared by along-track only — lateral error is <see cref="SnapStartToRunwayCenterline"/>'s
    /// business. Returns null for an empty sequence.
    ///
    /// Returns null too when even the best row lies past <paramref name="maxAlongFraction"/> of
    /// the runway, because such a row is not describing a departure point at all and the caller
    /// is better off with the threshold. LatinVFR's LEMD is the case: its 32R, 32L, 18L and 18R
    /// rows all sit at 47-48 % — the runway MIDPOINT, a placeholder never dragged to the
    /// threshold — with no taxiway within 220-660 m, and two of them carry a heading of 0° on a
    /// runway that points 322°. Selecting LEMD 32R as a taxi destination therefore routed to a
    /// taxiway a third of the way down the runway and aimed the lineup mid-field.
    ///
    /// The 40 % bar comes from the shape of the data, not taste: across all 97,488 rows in the
    /// live fs2020 navdata, 93.5 % sit within the first 10 % of their runway and over half
    /// between 2 % and 5 %. Only 115 rows (0.12 %) lie past 40 %, and the ones inspected —
    /// LEMD's four, URWW 05/23 at 99-101 %, KMWH 18 at 103 %, N15 16/34 at 97-98 % — are all
    /// plainly broken. Runways under 100 m are exempt: the fraction is meaningless on a strip.
    /// </summary>
    public static StartPosition? PickFullLengthStart(
        IEnumerable<StartPosition> rows,
        double thrLat, double thrLon, double farLat, double farLon,
        double maxAlongFraction = 0.40)
    {
        StartPosition? best = null;
        double bestAlong = double.MaxValue;
        foreach (var row in rows)
        {
            var (_, along, _, _) = ProjectOntoCenterline(
                row.Latitude, row.Longitude, thrLat, thrLon, farLat, farLon);
            if (best == null || along < bestAlong)
            {
                best = row;
                bestAlong = along;
            }
        }
        if (best == null) return null;

        double totalLen = FastDistanceMeters(thrLat, thrLon, farLat, farLon);
        if (totalLen >= 100.0 && bestAlong > totalLen * maxAlongFraction) return null;
        return best;
    }

    /// <summary>
    /// Picks the runway extent the named-holding-point picker measures against: the OUTER
    /// ENVELOPE of the pavement edges (runway_end) and the departure lineup points.
    ///
    /// Neither source alone works, because navdata disagrees with itself in both directions.
    /// EGKK 26L departs 406 m BEYOND its `runway_end` on a starter extension, so anchoring on
    /// the edge hides the entire A/M holding area behind the anchor. But EGLL 09R and EHAM 36C
    /// put the start row 45 m and 480 m INTO the runway, so anchoring on the lineup point
    /// instead drops the full-length holds that sit behind it — measured, that cost EGLL 09R
    /// its N1/NB1 and 27L its N8/NB8, and EHAM 36C its CAT III hold.
    ///
    /// Taking the furthest-back near end and the furthest-forward far end is inclusive by
    /// construction: the window can only ever grow, so no entry either source would have
    /// offered can be lost. Callers with no reciprocal lineup point pass the far pavement edge
    /// for both far arguments, which degrades to the edge.
    ///
    /// LineupAlong (never negative) is where the departure point sits inside that window, so
    /// the caller can still tell a full-length entry from a partway one.
    /// </summary>
    public static (double ThrLat, double ThrLon, double FarLat, double FarLon, double LineupAlong)
        ChooseHoldingPointExtent(
            double edgeThrLat, double edgeThrLon, double edgeFarLat, double edgeFarLon,
            double lineupLat, double lineupLon,
            double farLineupLat, double farLineupLon)
    {
        double thrLat = edgeThrLat, thrLon = edgeThrLon;
        var (_, lineupOnEdge, _, _) = ProjectOntoCenterline(
            lineupLat, lineupLon, edgeThrLat, edgeThrLon, edgeFarLat, edgeFarLon);
        if (lineupOnEdge < 0)
        {
            thrLat = lineupLat;
            thrLon = lineupLon;
        }

        double farLat = edgeFarLat, farLon = edgeFarLon;
        var (_, edgeFarAlong, _, _) = ProjectOntoCenterline(
            edgeFarLat, edgeFarLon, thrLat, thrLon, edgeFarLat, edgeFarLon);
        var (_, farLineupAlong, _, _) = ProjectOntoCenterline(
            farLineupLat, farLineupLon, thrLat, thrLon, edgeFarLat, edgeFarLon);
        if (farLineupAlong > edgeFarAlong)
        {
            farLat = farLineupLat;
            farLon = farLineupLon;
        }

        var (_, lineupAlong, _, _) = ProjectOntoCenterline(
            lineupLat, lineupLon, thrLat, thrLon, farLat, farLon);
        return (thrLat, thrLon, farLat, farLon, Math.Max(0.0, lineupAlong));
    }

    /// <summary>
    /// Pulls a `start`-table lineup point back ONTO the runway_end centerline, keeping
    /// its along-track position. A no-op (to the metre) wherever the start row is sound.
    ///
    /// The start table is the right source for WHERE ALONG the runway a departure begins —
    /// it accounts for displaced thresholds and starter extensions, which runway_end does
    /// not — but it is not always laterally trustworthy. EGKK (fs2020) is the case that
    /// forced this: three of its four start rows sit 99-122 m to the SIDE of their own
    /// runway (and carry headings 90-180° out), while every other airport probed —
    /// EGLL, EGCC, EGSS, EGGW, EHAM, LFPG, KJFK, KBOS, LEBL — lands within 5 m. Left
    /// alone, that lateral error propagates into the route destination, the LINEUP TARGET
    /// a blind pilot steers by, and the runway centerlines; at EGKK 26L it aimed the
    /// lineup ~109 m north of the pavement.
    ///
    /// Projecting rather than rejecting keeps the along-track value that made the start
    /// table worth using: EGKK 26L's row projects 406 m BEHIND the landing threshold,
    /// which is exactly the full-length departure point on the starter extension.
    /// Negative along-track is therefore expected and preserved.
    ///
    /// This ONLY ever repairs the lateral error, and only inside a plausible along-track
    /// window; anything else is returned UNCHANGED so today's behaviour is preserved
    /// exactly. Both refusals are load-bearing:
    ///
    /// - Further than <paramref name="maxOffsetMeters"/> off the line, the row is not
    ///   "offset", it is describing something else, and we have no basis to relocate it.
    /// - Past midfield (or as far behind), the row is not this runway's departure point at
    ///   all. Some airports carry NAME-SWAPPED start rows — AYCH's "03" row sits at the 21
    ///   threshold with 21's heading, and URWW's "05" sits 2854 m away at the 23 end.
    ///   Projecting those onto their named runway put both of an airport's rows at
    ///   midfield, on top of each other, which then failed the 200 m separation test and
    ///   COST AYCH the centerline it used to have. Refusing leaves that pre-existing data
    ///   problem exactly as it was rather than converting it into a new one.
    /// </summary>
    public static (double Lat, double Lon) SnapStartToRunwayCenterline(
        double startLat, double startLon,
        double thrLat, double thrLon, double farLat, double farLon,
        double maxOffsetMeters = 250.0)
    {
        double totalLen = FastDistanceMeters(thrLat, thrLon, farLat, farLon);
        if (totalLen < 1.0) return (startLat, startLon);

        var (perp, along, projLat, projLon) =
            ProjectOntoCenterline(startLat, startLon, thrLat, thrLon, farLat, farLon);

        if (perp <= 1.0) return (startLat, startLon);          // already on the line
        if (perp > maxOffsetMeters) return (startLat, startLon); // not this runway's line at all

        // The along-track window: anywhere from a starter extension half a runway behind
        // the landing threshold, up to midfield. Outside it, this row is not describing
        // this runway's departure point — leave it alone.
        double half = totalLen / 2.0;
        if (along < -half || along > half) return (startLat, startLon);

        return (projLat, projLon);
    }

    /// <summary>
    /// Splits a runway designator into its number and side letter — "26L" → (26, "L"),
    /// "08" → (8, ""), "9C" → (9, "C"). Returns null for anything that isn't a
    /// 1-2 digit number optionally followed by a single L/R/C (helipads "H1", water
    /// starts, blank names). Case-insensitive; surrounding whitespace is ignored.
    /// </summary>
    internal static (int Number, string Side)? ParseRunwayDesignator(string? name)
    {
        string s = name?.Trim() ?? "";
        if (s.Length == 0) return null;

        int digits = 0;
        while (digits < s.Length && char.IsAsciiDigit(s[digits])) digits++;
        if (digits == 0 || digits > 2) return null;
        if (!int.TryParse(s.Substring(0, digits), out int number)) return null;
        if (number < 1 || number > 36) return null;

        string side = s.Substring(digits).ToUpperInvariant();
        if (side.Length > 1) return null;
        if (side.Length == 1 && side != "L" && side != "R" && side != "C") return null;
        return (number, side);
    }

    /// <summary>
    /// The designator of the opposite end — "26L" → "08R", "08" → "26", "18C" → "36C".
    /// Null when the name doesn't parse as a runway designator. The number wraps in the
    /// 1-36 space (26 + 18 = 44 → 8) and L/R swap because the two ends see the parallel
    /// pair from opposite directions; C and side-less designators are unchanged.
    /// Zero-padded to two digits, matching how navdata writes them.
    /// </summary>
    internal static string? ReciprocalRunwayName(string? name)
    {
        var parsed = ParseRunwayDesignator(name);
        if (parsed == null) return null;
        var (number, side) = parsed.Value;

        int opposite = number + 18;
        if (opposite > 36) opposite -= 36;

        string oppositeSide = side switch { "L" => "R", "R" => "L", _ => side };
        return $"{opposite:00}{oppositeSide}";
    }

    /// <summary>
    /// True when a label is a runway designator or a pair of them — "26L", "08L/26R",
    /// "36C-18C" (EHAM maps its runway hold lines dash-separated). OSM tags a
    /// runway-crossing holding position with the crossed runway's name, so this
    /// separates those from genuine taxiway holding-point designators (A2, N4, VIKAS).
    /// </summary>
    internal static bool IsRunwayDesignatorLabel(string? label)
    {
        string s = label?.Trim() ?? "";
        if (s.Length == 0) return false;

        foreach (var part in s.Split(SeparatorChars, StringSplitOptions.RemoveEmptyEntries))
            if (ParseRunwayDesignator(part) == null) return false;
        return true;
    }

    private static readonly char[] SeparatorChars = { '/', '-' };

    /// <summary>
    /// The nominal heading a designator implies, in degrees — "26L" → 260. Zero when the
    /// name doesn't parse, which callers treat as "no opinion" rather than "due north"
    /// (they only ever use this against a tolerance alongside a name match).
    /// </summary>
    internal static double RunwayDesignatorHeading(string? name)
    {
        var parsed = ParseRunwayDesignator(name);
        return parsed == null ? 0.0 : parsed.Value.Number * 10.0;
    }

    /// <summary>
    /// Formats a parking spot display name from its name, number, and suffix.
    /// </summary>
    public static string FormatParkingDisplayName(ParkingSpot spot)
    {
        string name = spot.Name;
        if (spot.Number > 0)
            name += $" {spot.Number}";
        if (!string.IsNullOrEmpty(spot.Suffix))
            name += spot.Suffix;
        return name.Trim();
    }

    private static string FormatParkingName(ParkingSpot spot) => FormatParkingDisplayName(spot);

    #endregion

    #region Landing Exit Planning

    /// <summary>
    /// Last-resort scan for a way off <paramref name="rwy"/> ahead of the aircraft, used by
    /// the landing rollout when the planned exit has been missed and
    /// <see cref="GetLandingExits"/>' list holds nothing further down the runway.
    ///
    /// <para><b>Why this is not GetLandingExits with a filter.</b> That list is built for the
    /// planner DIALOG and is deliberately lossy in two ways. It keeps one entry per taxiway
    /// name, and - decisively - the moment ONE node in the runway corridor carries a
    /// hold-short marker with a forward exit it stops considering unmarked junctions for the
    /// WHOLE runway (`hasHoldShortOnRunway`). Rapid-exit taxiways are routinely modelled
    /// without a hold-short bar, because they are one-way turnoffs, so a single marked
    /// crossing taxiway near the threshold can hide every RET behind it. Both choices are
    /// right for a menu the pilot reads before the flight. Neither is an acceptable answer to
    /// "is there any way off this runway ahead of me?", which is asked at 90 kt and whose
    /// fallback is a 180-degree backtrack on an active runway (CYYZ 23, 2026-08-23: the
    /// rollout declared the missed exit the last one with 5,400 ft of pavement and three
    /// further turnoffs ahead).</para>
    ///
    /// <para>So this asks the graph directly: every corridor node beyond
    /// <paramref name="afterDistanceFromThresholdFeet"/> whose named edges demonstrably leave
    /// the runway strip, forward-peeling only (a turn past 90 degrees is the backtrack this
    /// exists to avoid), stopping short of the pavement end. Hold-short markers are ignored
    /// in BOTH directions - a marked node is as eligible as an unmarked one.</para>
    ///
    /// <para>Nodes of one curved RET arc collapse onto the arc entry point: a candidate is
    /// dropped when a nearer kept candidate shares its taxiway name within
    /// EXIT_COVERAGE_GAP_FT. Genuinely separate turnoffs sharing a name - the KBNA shape -
    /// survive, exactly as the coverage fill in <see cref="GetLandingExits"/> intends.</para>
    ///
    /// <para>Returned nearest-first. Runs once, at the overshoot decision - never per frame.</para>
    /// </summary>
    public List<LandingExit> FindDownfieldExits(Runway rwy, double afterDistanceFromThresholdFeet)
    {
        var found = new List<LandingExit>();
        if (rwy == null || rwy.Length <= 0) return found;

        // Same frame, tolerances and classification thresholds as GetLandingExits - a rescue
        // candidate must describe the same geometry a planned one would.
        const double MIN_FALLBACK_EXIT_ANGLE_DEG = 20.0;
        const double HIGH_SPEED_MAX_DEG = 50.0;
        const double NORMAL_MAX_DEG     = 110.0;
        const double END_RATIO          = 0.85;
        const double END_BUFFER_FT      = 50.0;
        const double METERS_PER_DEG_LAT = 111132.0;
        const double COVERAGE_GAP_FT    = RolloutExitGate.EarlyVacateMaxPassedFeet;

        double rwyHeadingTrue = rwy.Heading;
        double cosH = Math.Cos(rwyHeadingTrue * Math.PI / 180.0);
        double sinH = Math.Sin(rwyHeadingTrue * Math.PI / 180.0);
        double halfWidthFt = rwy.Width > 0 ? rwy.Width * 0.5 : 75.0;
        double lateralToleranceM = (halfWidthFt * 0.3048) + 15.0;
        double lengthM = rwy.Length * 0.3048;
        double maxDistFt = rwy.Length - END_BUFFER_FT;
        double landingThresholdOffsetFt = rwy.ThresholdOffset;

        foreach (var node in Nodes.Values)
        {
            if (node.Type == TaxiNodeType.Parking) continue;
            if (!Adjacency.TryGetValue(node.NodeId, out var edges)) continue;

            double latRad = (rwy.StartLat + node.Latitude) * 0.5 * Math.PI / 180.0;
            double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latRad);
            double dN = (node.Latitude - rwy.StartLat) * METERS_PER_DEG_LAT;
            double dE = (node.Longitude - rwy.StartLon) * metersPerDegLon;
            double alongM   = dE * sinH + dN * cosH;
            double lateralM = dE * cosH - dN * sinH;

            if (Math.Abs(lateralM) > lateralToleranceM) continue;
            if (alongM < 0 || alongM > lengthM + 50.0) continue;

            double alongFt = alongM / 0.3048;
            if (alongFt > maxDistFt) continue;
            double distFromThresholdFt = alongFt - landingThresholdOffsetFt;
            if (distFromThresholdFt <= afterDistanceFromThresholdFeet) continue;

            // Does anything named actually leave the runway strip from here? An edge turning
            // meaningfully off the axis answers yes at once; otherwise follow the named path
            // (a smooth RET whose every segment reads near-parallel) and see whether it
            // clears the corridor. A parallel holding taxiway never does.
            bool hasOffAxisNamedEdge = false;
            foreach (var ed in edges)
            {
                if (string.IsNullOrEmpty(ed.TaxiwayName)) continue;
                double rel = Math.Abs(NormalizeAngle(ed.BearingDegrees - rwyHeadingTrue));
                double off = rel > 90.0 ? 180.0 - rel : rel;
                if (off >= MIN_FALLBACK_EXIT_ANGLE_DEG) { hasOffAxisNamedEdge = true; break; }
            }
            int apronNodeId = ExitPathLeavesCorridor(
                node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);
            if (!hasOffAxisNamedEdge && apronNodeId < 0) continue;

            // Best exit edge: connector-style names first, then the widest turn off the axis
            // - the same ranking GetLandingExits uses, so a rescue candidate is announced to
            // the pilot the way a planned one would be.
            TaxiEdge? best = null;
            foreach (var e in edges)
            {
                if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(e.TaxiwayName)) continue;
                if (best == null) { best = e; continue; }
                bool bestHasDigit = HasLetterAndDigit(best.TaxiwayName);
                bool curHasDigit  = HasLetterAndDigit(e.TaxiwayName);
                if (curHasDigit && !bestHasDigit) { best = e; continue; }
                if (curHasDigit != bestHasDigit) continue;
                double bestRel = Math.Abs(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue));
                double bestOff = bestRel > 90.0 ? 180.0 - bestRel : bestRel;
                double curRel  = Math.Abs(NormalizeAngle(e.BearingDegrees - rwyHeadingTrue));
                double curOff  = curRel > 90.0 ? 180.0 - curRel : curRel;
                if (curOff > bestOff + 0.01) { best = e; continue; }
                if (Math.Abs(curOff - bestOff) > 0.01) continue;

                // Equal off-axis angle: the adjacency list holds BOTH the forward exit edge
                // and the reverse edge of the same taxiway segment, and the two fold to the
                // SAME `off`, so without a tie-break first-encountered wins on navdata row
                // order alone. That is not cosmetic here - the relBest > 90 guard below then
                // discards the junction outright, so a real turnoff (a 60-degree crossing
                // taxiway, say) is invisible to the rescue scan on roughly half of orderings
                // and the pilot is told the runway has run out of exits. Same tie-break
                // GetLandingExits carries: the correct edge moves the aircraft further
                // off-runway on the SAME side as the junction (lateralM: + right, - left).
                if (Math.Abs(lateralM) > 1.0)
                {
                    double bestLatComp = Math.Sin(
                        NormalizeAngle(best.BearingDegrees - rwyHeadingTrue) * Math.PI / 180.0);
                    double curLatComp = Math.Sin(
                        NormalizeAngle(e.BearingDegrees - rwyHeadingTrue) * Math.PI / 180.0);
                    if (Math.Sign(curLatComp) == Math.Sign(lateralM)
                        && Math.Sign(bestLatComp) != Math.Sign(lateralM))
                        best = e;
                }
                else if (curRel <= 90.0 && bestRel > 90.0)
                {
                    // Junction sits on the centreline - lateral direction cannot
                    // discriminate. Fall back to hemisphere: forward beats backward.
                    best = e;
                }
            }
            if (best == null) continue;

            double relBest = Math.Abs(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue));
            // A stub peeling back toward the approach end is a turnaround, not an exit - the
            // very thing this scan exists to keep the pilot out of.
            if (relBest > 90.0) continue;
            double exitAngle = relBest;

            double endRatio = alongFt / rwy.Length;
            string exitType = endRatio > END_RATIO ? "End"
                : exitAngle <= HIGH_SPEED_MAX_DEG ? "High-speed"
                : exitAngle <= NORMAL_MAX_DEG ? "Normal" : "End";

            double exitBearingTrue = best.BearingDegrees == 0.0 ? 360.0 : best.BearingDegrees;

            // Shallow first-edge stub: this scan deliberately admits smooth-curve RETs whose
            // every segment reads near-parallel (the ExitPathLeavesCorridor branch above), and
            // for those the first edge's bearing is barely off the runway axis - EDDB 24L M3
            // starts at 6.9 degrees, LGAV 03R D8/D9 at 7.6. The rollout's exit-bearing tone
            // mode and the post-exit pan floor both steer on ExitBearingTrue, so publishing the
            // stub bearing pans the pilot straight down the runway at the moment they must turn
            // and makes ExitSide a coin flip. Use the node->apron bearing instead when it is
            // wider - the same override, guards and threshold GetLandingExits applies.
            if (exitAngle < MIN_FALLBACK_EXIT_ANGLE_DEG
                && apronNodeId > 0
                && Nodes.TryGetValue(apronNodeId, out var apronTaxiNode))
            {
                double latRb = (node.Latitude + apronTaxiNode.Latitude) * 0.5 * Math.PI / 180.0;
                double mPLb = METERS_PER_DEG_LAT * Math.Cos(latRb);
                double dNb = (apronTaxiNode.Latitude - node.Latitude) * METERS_PER_DEG_LAT;
                double dEb = (apronTaxiNode.Longitude - node.Longitude) * mPLb;
                double apronBrg = Math.Atan2(dEb, dNb) * 180.0 / Math.PI;
                if (apronBrg < 0) apronBrg += 360.0;
                double apronAngle = Math.Abs(NormalizeAngle(apronBrg - rwyHeadingTrue));
                double currentAngleFwd = Math.Abs(NormalizeAngle(
                    (exitBearingTrue == 360.0 ? 0.0 : exitBearingTrue) - rwyHeadingTrue));
                if (apronAngle <= NORMAL_MAX_DEG && apronAngle > currentAngleFwd)
                    exitBearingTrue = apronBrg == 0.0 ? 360.0 : apronBrg;
            }

            found.Add(new LandingExit
            {
                NodeId = node.NodeId,
                ApronNodeId = apronNodeId > 0 ? apronNodeId : node.NodeId,
                Latitude = node.Latitude,
                Longitude = node.Longitude,
                DistanceFromThresholdFeet = distFromThresholdFt,
                DistanceFromTouchdownFeet = distFromThresholdFt - 1000.0,
                TaxiwayName = best.TaxiwayName,
                ExitAngleDegrees = exitAngle,
                ExitBearingTrue = exitBearingTrue,
                ExitType = exitType,
                ExitSide = NormalizeAngle(
                    (exitBearingTrue == 360.0 ? 0.0 : exitBearingTrue) - rwyHeadingTrue) >= 0
                    ? "Right" : "Left"
            });
        }

        found.Sort((a, b) => a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet));

        // Collapse the interior nodes of one RET arc onto its entry point, without merging two
        // turnoffs that merely share a name (KBNA). Same coverage rule, same constant.
        var kept = new List<LandingExit>(found.Count);
        foreach (var e in found)
        {
            bool duplicate = false;
            for (int i = kept.Count - 1; i >= 0; i--)
            {
                if (e.DistanceFromThresholdFeet - kept[i].DistanceFromThresholdFeet > COVERAGE_GAP_FT)
                    break; // sorted - nothing earlier can be inside the window either
                if (string.Equals(kept[i].TaxiwayName, e.TaxiwayName, StringComparison.OrdinalIgnoreCase))
                { duplicate = true; break; }
            }
            if (!duplicate) kept.Add(e);
        }
        return kept;
    }

    /// <summary>
    /// Finds usable runway exit taxiways for the given landing runway. Projects every
    /// hold-short and ILS hold-short node onto the runway centerline; any node that
    /// lies within the runway footprint (half-width + 15 m lateral buffer) and between
    /// a minimum along-runway distance and the runway end is considered an exit.
    ///
    /// Returned list is sorted by along-runway distance from the landing threshold (nearest first).
    /// Useful exits typically start 1500 ft past the threshold (jet touchdown zone) and
    /// end ~500 ft before the runway end.
    ///
    /// "Threshold" = rwy.StartLat/Lon (primary end of the Runway record, which is the
    /// landing threshold for this runway direction — the DB returns one Runway per
    /// direction, each with its own StartLat representing its own threshold).
    /// </summary>
    public List<LandingExit> GetLandingExits(Runway rwy)
    {
        var exits = new List<LandingExit>();
        if (rwy == null || rwy.Length <= 0) return exits;

        // Runway axis — use true heading (rwy.Heading is true in the DB model).
        double rwyHeadingTrue = rwy.Heading;
        double cosH = Math.Cos(rwyHeadingTrue * Math.PI / 180.0);
        double sinH = Math.Sin(rwyHeadingTrue * Math.PI / 180.0);

        // Lateral tolerance: runway half-width + a buffer, because hold-short nodes
        // are usually painted a few meters inside the runway edge stripe. If the DB
        // lacks width (Width==0), fall back to 75 ft (23 m half-width → 60 ft total
        // which covers most Code C/D taxiway-runway intersections).
        double halfWidthFt = rwy.Width > 0 ? rwy.Width * 0.5 : 75.0;
        double lateralToleranceM = (halfWidthFt * 0.3048) + 15.0;

        double lengthM = rwy.Length * 0.3048;

        // Displaced threshold handling. rwy.ThresholdOffset is the distance (feet)
        // from the physical runway end (rwy.StartLat/Lon) to the painted landing
        // threshold. Pilots land past the displaced threshold — the aim point, the
        // touchdown zone markings, and therefore the "usable exit" math must all
        // be measured from the LANDING threshold, not the physical pavement end.
        // KJFK 13R has a ~2055 ft displaced threshold; KJFK 22R has ~3438 ft; EGLL
        // 27R has ~1004 ft. Without this, a "2000 ft from touchdown" exit at 13R
        // would actually be ~3000 ft before the touchdown aim point (i.e. behind
        // the aircraft) — unsafe and wrong. ~5,500 runway ends in a typical
        // navdatareader DB have non-zero offset; this matters at every major hub.
        double landingThresholdOffsetFt = rwy.ThresholdOffset;

        // Cutoffs: usable exits lie past the jet touchdown zone and before the runway end.
        // MIN_DIST_FT is a conservative floor (still captures very-early RETs at some
        // airports and "reject take-off" spots; also avoids false positives from
        // threshold hold-short lines). END_BUFFER_FT is a small margin against nodes
        // literally on the runway-end markings; the geometric corridor + named-edge
        // filters are the real protection, so 50 ft is enough (200 ft was excluding
        // legitimate end-of-runway vacate exits like S7 at EIDW 28L).
        const double MIN_DIST_FT = 500.0;
        const double END_BUFFER_FT = 50.0;
        const double TOUCHDOWN_AIM_FT = 1000.0;  // typical jet aim point past landing threshold

        // Classification thresholds (angle between exit edge and runway axis).
        const double HIGH_SPEED_MAX_DEG = 50.0;   // RET geometry (≤50° off runway axis)
        const double NORMAL_MAX_DEG     = 110.0;  // beyond this → End
        const double END_RATIO          = 0.85;   // last 15% of runway → always End

        // Dedup window: exits within this along-runway distance that share a
        // taxiway name are collapsed to a single entry.
        const double DEDUP_WINDOW_FT = 50.0;

        // Coverage window for the gap fill (the final block below). An exit dropped by one of
        // the name dedups is re-admitted only when no surviving exit lies within this
        // distance of it — i.e. only where it is the sole option for that stretch of runway.
        //
        // Measured, not guessed. Sweeping 266 runway directions across 39 airports, every
        // row a gap fill would add outside KBNA sits within 1309 ft of an exit already in
        // the list (median 672, p95 968) — those are the far ends of RET arcs, the same
        // physical turnoff the pilot already has. KBNA's genuinely missing turnoffs start at
        // 1481 ft. 1400 ft sits in that gap: it adds nothing at any of the other 38 airports
        // and recovers KBNA's lost exits.
        // Shared with the early-vacate matcher, which answers the same question from the
        // other direction ("is this exit close enough behind me to be the one I turned at?").
        // A local const initialised from the gate's, so the two cannot drift.
        const double EXIT_COVERAGE_GAP_FT = RolloutExitGate.EarlyVacateMaxPassedFeet;

        double maxDistFt = rwy.Length - END_BUFFER_FT;
        if (maxDistFt < MIN_DIST_FT) return exits;

        // Universal fallback: many runways in real-world navdatareader DBs have
        // no HoldShort / ILSHoldShort nodes recorded at all — small airports,
        // renumbered runways, certain third-party scenery, and (notably) every
        // runway whose taxi_path rows just don't carry the HS/IHS markers.
        // Without a fallback, GetLandingExits returns an empty list for those
        // runways and a blind pilot has no way to plan an exit.
        //
        // Earlier versions of this fallback gated on "the node has a runway-type
        // edge (PathType starts with R)" — but in the schema this app actually
        // ships against, NO taxi_path row has PathType == "R" (runway centerlines
        // live in the separate runway/runway_end tables, not in taxi_path). So
        // that gate was dead code. The geometric fallback below is independent of
        // PathType labels: it finds Normal nodes that lie on the runway axis AND
        // have at least one named-taxiway edge that turns meaningfully off the
        // runway (≥ MIN_FALLBACK_EXIT_ANGLE_DEG). That excludes parallel taxiway
        // nodes (which lie close to the axis but only have edges parallel to it)
        // while still picking up real intersections.
        const double MIN_FALLBACK_EXIT_ANGLE_DEG = 20.0;

        bool hasHoldShortOnRunway = false;
        foreach (var n in Nodes.Values)
        {
            if (n.Type != TaxiNodeType.HoldShort && n.Type != TaxiNodeType.ILSHoldShort)
                continue;
            const double M_PER_DEG_LAT = 111132.0;
            double latR = (rwy.StartLat + n.Latitude) * 0.5 * Math.PI / 180.0;
            double mPerLon = M_PER_DEG_LAT * Math.Cos(latR);
            double dNn = (n.Latitude - rwy.StartLat) * M_PER_DEG_LAT;
            double dEn = (n.Longitude - rwy.StartLon) * mPerLon;
            double latM = dEn * cosH - dNn * sinH;
            double aM = dEn * sinH + dNn * cosH;
            if (Math.Abs(latM) > lateralToleranceM || aM < 0 || aM > lengthM + 50.0)
                continue;
            // Node is geometrically within this runway's corridor. Also require that it
            // has at least one named edge whose exit angle is meaningful for this landing
            // direction (≤ NORMAL_MAX_DEG after applying the same backward-RET override
            // used in the main exit-angle computation). Without this check, a RET that is
            // designed for the OPPOSITE runway direction (e.g. N4 at EIDW — a 28R RET
            // whose node physically lies inside the 10L corridor) would set
            // hasHoldShortOnRunway=true, blocking the Normal-node fallback from finding
            // the real 10L exits (N1/N2/N3), leaving only that backward RET.
            if (!Adjacency.TryGetValue(n.NodeId, out var hsEdges)) continue;
            bool hasForwardExit = false;
            foreach (var he in hsEdges)
            {
                if (string.IsNullOrEmpty(he.TaxiwayName)) continue;
                double relAngle = Math.Abs(NormalizeAngle(he.BearingDegrees - rwyHeadingTrue));
                bool peelsBack = relAngle > 90.0;
                double ea = peelsBack ? 180.0 - relAngle : relAngle;
                if (peelsBack && ea < 50.0) ea = NORMAL_MAX_DEG + 20.0;
                if (ea <= NORMAL_MAX_DEG) { hasForwardExit = true; break; }
            }
            if (hasForwardExit) { hasHoldShortOnRunway = true; break; }
        }

        foreach (var node in Nodes.Values)
        {
            bool isHoldShortNode = node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort;
            bool isImplicitExitNode = false;
            // For fallback implicit exits: node ID of the first point outside the corridor.
            // Set by ExitPathLeavesCorridor; used as ApronNodeId for tone re-routing.
            int implicitApronNodeId = -1;
            if (!isHoldShortNode)
            {
                // Fallback gate: only consider Normal nodes when THIS runway has
                // no explicit hold-short nodes at all. Otherwise the HS/IHS data
                // is authoritative and we shouldn't muddy the list with implicit
                // junctions (which tend to dedupe awkwardly).
                if (hasHoldShortOnRunway) continue;
                if (node.Type != TaxiNodeType.Normal) continue;
                if (!Adjacency.TryGetValue(node.NodeId, out var ee)) continue;

                // Need at least one named-taxiway edge that turns off the runway
                // axis at a meaningful angle. Parallel taxiways (whose edges run
                // along the runway heading) are filtered out here.
                bool hasOffAxisNamedEdge = false;
                foreach (var ed in ee)
                {
                    if (string.IsNullOrEmpty(ed.TaxiwayName)) continue;
                    double rel = Math.Abs(NormalizeAngle(ed.BearingDegrees - rwyHeadingTrue));
                    // Fold to 0..90 so reverse direction (180°) counts as parallel.
                    double off = rel > 90.0 ? 180.0 - rel : rel;
                    if (off >= MIN_FALLBACK_EXIT_ANGLE_DEG)
                    { hasOffAxisNamedEdge = true; break; }
                }
                // Secondary gate: some scenery packages (e.g. LVFR LEMD) model RETs as
                // smooth curves whose individual PT-type path segments all run nearly
                // parallel to the runway (< 20° off-axis), so the angle test above
                // misses them. A truly parallel holding taxiway stays within the lateral
                // corridor for its entire length; a real RET must eventually leave the
                // corridor to reach the apron. Follow named edges up to 600 m and accept
                // the node if the path demonstrably exits the runway strip.
                // Also captures the corridor-exit node ID for ApronNodeId (re-routing).
                if (!hasOffAxisNamedEdge)
                {
                    implicitApronNodeId = ExitPathLeavesCorridor(node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);
                    if (implicitApronNodeId < 0) continue;
                    hasOffAxisNamedEdge = true;
                }
                if (!hasOffAxisNamedEdge) continue;
                isImplicitExitNode = true;
            }
            if (!isHoldShortNode && !isImplicitExitNode) continue;

            // Convert node offset from threshold into local meters (equirectangular).
            const double METERS_PER_DEG_LAT = 111132.0;
            double latRad = (rwy.StartLat + node.Latitude) * 0.5 * Math.PI / 180.0;
            double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latRad);
            double dN = (node.Latitude - rwy.StartLat) * METERS_PER_DEG_LAT;         // north
            double dE = (node.Longitude - rwy.StartLon) * metersPerDegLon;           // east

            // Project onto runway axis.
            // Runway bearing is measured from north going clockwise; unit vector = (sin H, cos H) in (E, N).
            // along = dE*sinH + dN*cosH  ← distance along runway in its flight direction
            // lateral = dE*cosH - dN*sinH ← perpendicular distance (sign indicates left/right)
            double alongM = dE * sinH + dN * cosH;
            double lateralM = dE * cosH - dN * sinH;

            if (Math.Abs(lateralM) > lateralToleranceM) continue;
            if (alongM < 0) continue; // behind the threshold
            if (alongM > lengthM + 50.0) continue; // past the far end

            double alongFt = alongM / 0.3048;
            // Distance measured from the LANDING (displaced) threshold, which is
            // where the aircraft actually touches down. MIN_DIST_FT (500 ft) is
            // the earliest usable exit after that point; END_BUFFER_FT still
            // caps against the physical end of pavement.
            double distFromLandingThresholdFt = alongFt - landingThresholdOffsetFt;
            if (distFromLandingThresholdFt < MIN_DIST_FT || alongFt > maxDistFt) continue;

            // Find the best taxiway name + exit angle. Walk the node's edges and pick
            // the edge that is NOT on the runway (taxiway continuation), preferring
            // a named path. Compute the angle between that edge and the runway.
            string taxiwayName = "";
            double exitAngle = 90.0; // default to perpendicular if nothing better found
            double exitBearingTrue = 0.0; // true bearing of best exit edge; 0 = not found
            if (Adjacency.TryGetValue(node.NodeId, out var edges))
            {
                TaxiEdge? best = null;
                foreach (var e in edges)
                {
                    // Skip runway-type edges (the edge that lies on the runway centerline).
                    bool onRunway = string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase);
                    if (onRunway) continue;
                    if (string.IsNullOrEmpty(e.TaxiwayName)) continue;

                    if (best == null)
                    {
                        best = e;
                        continue;
                    }

                    // Prefer connector-style names (letter+digit) over bare main names.
                    // Same ranking spirit as hold-short naming (Feature #7).
                    bool bestHasDigit = HasLetterAndDigit(best.TaxiwayName);
                    bool curHasDigit  = HasLetterAndDigit(e.TaxiwayName);
                    if (curHasDigit && !bestHasDigit)
                    {
                        best = e;
                    }
                    else if (curHasDigit == bestHasDigit)
                    {
                        // Same name priority — prefer the edge that turns most off-axis
                        // from the runway. Adjacency-list ordering is not guaranteed, so
                        // without this tie-break a parallel-running named edge can be
                        // chosen over the actual perpendicular exit edge, producing an
                        // exit angle of ~0° for exits like EGCC AF/AG on 23R.
                        double bestRel = Math.Abs(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue));
                        double bestOff = bestRel > 90.0 ? 180.0 - bestRel : bestRel;
                        double curRel  = Math.Abs(NormalizeAngle(e.BearingDegrees - rwyHeadingTrue));
                        double curOff  = curRel > 90.0 ? 180.0 - curRel : curRel;
                        if (curOff > bestOff + 0.01)
                        {
                            best = e;
                        }
                        else if (Math.Abs(curOff - bestOff) <= 0.01)
                        {
                            // Equal off-axis angle (within float tolerance): the adjacency
                            // list contains both the forward exit edge and the reverse edge
                            // of the same taxiway segment. Both fold to the same `off`, so
                            // first-encountered was winning non-deterministically.
                            // Wrong edge → wrong ExitBearingTrue → FindExitExtensionNode
                            // routes backward → permanent max-pan tone for any exit angle.
                            //
                            // Primary tiebreak — lateral direction.
                            //   Use the junction node's signed lateral offset from the
                            //   runway centreline (lateralM: + = right, - = left, already
                            //   computed above). The correct exit edge moves the aircraft
                            //   further off-runway on the SAME side as the junction; the
                            //   reverse edge heads toward the opposite apron or back across
                            //   the runway. lateralComponent = sin(NormalizeAngle(bearing −
                            //   rwyHeading)) gives the signed lateral movement of an edge.
                            //   This criterion is geometrically correct for ALL exit angles:
                            //     7°  exit: correct edge lat≈+0.12, reverse lat≈-0.12
                            //     90° exit: correct edge lat=±1.00, reverse lat=∓1.00
                            //     100° exit: correct edge lat≈±0.98, reverse lat≈∓0.98
                            //   (hemisphere alone would mis-pick the reverse edge for
                            //   obtuse exits 90°–180° where the correct edge is in the
                            //   "backward" hemisphere by the rel≤90 criterion.)
                            //
                            // Fallback tiebreak — hemisphere.
                            //   Applied only when the junction is within 1 m of the
                            //   centreline and lateral direction can't discriminate.
                            //   Forward-hemisphere edges (rel ≤ 90°) beat backward edges;
                            //   correct for acute exits, ambiguous for obtuse exits on the
                            //   centreline (an inherently rare degenerate case).
                            if (Math.Abs(lateralM) > 1.0)
                            {
                                double bestLatComp = Math.Sin(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue) * Math.PI / 180.0);
                                double curLatComp  = Math.Sin(NormalizeAngle(e.BearingDegrees   - rwyHeadingTrue) * Math.PI / 180.0);
                                bool curMatchesSide  = Math.Sign(curLatComp)  == Math.Sign(lateralM);
                                bool bestMatchesSide = Math.Sign(bestLatComp) == Math.Sign(lateralM);
                                if (curMatchesSide && !bestMatchesSide)
                                    best = e;
                            }
                            else
                            {
                                // Junction near centreline: fall back to hemisphere.
                                bool bestForward = bestRel <= 90.0;
                                bool curForward  = curRel  <= 90.0;
                                if (curForward && !bestForward)
                                    best = e;
                            }
                        }
                    }
                }

                if (best != null)
                {
                    taxiwayName = best.TaxiwayName;
                    // Store 360.0 for due-north edges so 0.0 stays unambiguous as "not found".
                    exitBearingTrue = best.BearingDegrees == 0.0 ? 360.0 : best.BearingDegrees;
                    // Raw relative angle in 0..180 (absolute value of normalized delta).
                    double rel = Math.Abs(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue));

                    // Is the edge pointing "backward" relative to the landing direction?
                    // rel > 90 means more than a right angle off the landing heading —
                    // i.e., the taxiway peels off toward the approach end rather than
                    // toward the departure end.
                    bool peelsBackward = rel > 90.0;

                    // Fold to 0..90 so "exitAngle" is magnitude of turn from runway axis.
                    exitAngle = peelsBackward ? 180.0 - rel : rel;

                    // Backward-peel nodes are almost always end-of-runway turnoffs
                    // (handled by the endRatio>0.85 check below). But if a backward
                    // peel appears mid-runway, we should NOT classify it as
                    // "High-speed" — exiting through it requires turning around,
                    // which is not a high-speed RET. Forcing exitAngle to an
                    // obtuse-looking value (>=100) pushes classification to "End"
                    // below regardless of the along-runway position.
                    if (peelsBackward && exitAngle < 50.0)
                    {
                        // Geometry says RET-angle but direction is backward —
                        // treat as end-style exit, not a high-speed. Forcing the
                        // angle above NORMAL_MAX_DEG pushes classification to
                        // "End" below regardless of along-runway position.
                        exitAngle = NORMAL_MAX_DEG + 20.0;
                    }
                }
            }

            // For implicit (non-HS) exits whose first named edge is nearly parallel to
            // the runway (< 20°), the edge bearing gives an inadequate pan cue. The BFS
            // apron node — first node found outside the corridor — captures the actual
            // exit direction after the arc, so use node→apron bearing instead when it
            // gives a wider (more useful) angle.
            //
            // Guards (both required):
            //   (a) apronAngle <= NORMAL_MAX_DEG (110°) — forward direction only. A
            //       backward apron-bearing (BFS exited toward the approach end) would
            //       pan the pilot the wrong way.
            //   (b) apronAngle > currentAngleFwd — only override when apron is MORE
            //       off-axis than the existing first-edge bearing. Mirrors the HS-style
            //       override guard at the next block. Without this, an exit whose stub
            //       points further off-runway than its eventual apron node (a curved-
            //       back-toward-centreline shape) would have its bearing narrowed by
            //       the override — a regression vs. the first-edge value.
            //
            // Threshold widened from 5° → 20° so EDDB-style implicit
            // exits with shallow first-edge stubs (e.g. EDDB 24L M3 at 6.9°, LGAV 03R
            // D8/D9 at 7.6°) are covered. Symmetric with the HS-style override gate at
            // the next block (also < 20°). EIDW S5 and other hold-short shallow exits
            // are unaffected — they use the parallel HS-style branch (`isHoldShortNode`
            // gate). EGNX 27/M (90° normal) is above 20°, also unaffected.
            if (!isHoldShortNode && exitAngle < 20.0
                && implicitApronNodeId > 0
                && Nodes.TryGetValue(implicitApronNodeId, out var apronTaxiNode))
            {
                const double MPD_BRG = 111132.0;
                double latRb = (node.Latitude + apronTaxiNode.Latitude) * 0.5 * Math.PI / 180.0;
                double mPLb = MPD_BRG * Math.Cos(latRb);
                double dNb = (apronTaxiNode.Latitude - node.Latitude) * MPD_BRG;
                double dEb = (apronTaxiNode.Longitude - node.Longitude) * mPLb;
                double apronBrg = Math.Atan2(dEb, dNb) * 180.0 / Math.PI;
                if (apronBrg < 0) apronBrg += 360.0;
                double apronAngle = Math.Abs(NormalizeAngle(apronBrg - rwyHeadingTrue));
                // Compare against current exitBearingTrue. 0.0 means no bearing found
                // (sentinel) → treat as -1 so any forward-direction apron wins.
                double currentAngleFwd = exitBearingTrue != 0.0
                    ? Math.Abs(NormalizeAngle(exitBearingTrue - rwyHeadingTrue))
                    : -1.0;
                if (apronAngle <= NORMAL_MAX_DEG && apronAngle > currentAngleFwd)
                    exitBearingTrue = apronBrg == 0.0 ? 360.0 : apronBrg;
            }

            // For HS/IHS exits with shallow angle (<20°), the hold-short marker may sit at
            // the start of a curved RET whose individual segments each run nearly parallel
            // to the runway. The first-edge ExitBearingTrue in those cases is close to
            // runway heading → near-zero tone blend during rollout. Run the same corridor-
            // BFS used for Normal-node implicit exits to find the first node outside the
            // runway strip. Two benefits:
            //   (a) ExitBearingTrue is overridden with the node→apron bearing when it gives
            //       a wider (more useful) angle — clearer pan cue at the RET turn point.
            //   (b) ApronNodeId is set to that node → the Taxiing-handoff re-route fires,
            //       giving A* guidance through the actual curve rather than the apron-network
            //       route computed at touchdown.
            // Threshold 20°: captures all real ICAO Cat E RETs; avoids BFS overhead on
            // standard 60-90° exits where the first-edge bearing is already adequate.
            int hsApronNodeId = -1;
            if (isHoldShortNode && exitAngle < 20.0)
            {
                int bfsResult = ExitPathLeavesCorridor(node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);
                if (bfsResult > 0 && Nodes.TryGetValue(bfsResult, out var hsApronNode))
                {
                    hsApronNodeId = bfsResult;
                    const double MPD_BRG_HS = 111132.0;
                    double latRh = (node.Latitude + hsApronNode.Latitude) * 0.5 * Math.PI / 180.0;
                    double mPLh = MPD_BRG_HS * Math.Cos(latRh);
                    double dNh = (hsApronNode.Latitude - node.Latitude) * MPD_BRG_HS;
                    double dEh = (hsApronNode.Longitude - node.Longitude) * mPLh;
                    double hsApronBrg = Math.Atan2(dEh, dNh) * 180.0 / Math.PI;
                    if (hsApronBrg < 0) hsApronBrg += 360.0;
                    double apronAngle = Math.Abs(NormalizeAngle(hsApronBrg - rwyHeadingTrue));
                    // Compare against current ExitBearingTrue. 0.0 means no bearing found
                    // (sentinel) → treat as -1 so any forward-direction apron wins.
                    double currentAngleFwd = exitBearingTrue != 0.0
                        ? Math.Abs(NormalizeAngle(exitBearingTrue - rwyHeadingTrue))
                        : -1.0;
                    if (apronAngle <= NORMAL_MAX_DEG && apronAngle > currentAngleFwd)
                        exitBearingTrue = hsApronBrg == 0.0 ? 360.0 : hsApronBrg;
                }
            }

            // Handoff destination must be a node genuinely CLEAR of the runway.
            // implicitApronNodeId is only computed in the shallow-angle branch above
            // (the one that runs ExitPathLeavesCorridor when the first named edge is
            // < MIN_FALLBACK_EXIT_ANGLE_DEG off the runway axis). For an implicit exit
            // whose first stub already turns off at ≥ 20° — e.g. LPFR taxiway F, whose
            // stub leaves the centreline at ~23° — that branch is skipped, so
            // implicitApronNodeId stays -1. With no ApronNodeId the LandingRollout →
            // Taxiing handoff falls back to FindExitExtensionNode, which returns the
            // FIRST adjacent node; on a taxiway modelled from the runway centreline
            // outward that node can still sit inside the runway half-width (LPFR F's
            // node ~12 m off a 22.6 m half-width). The route then "arrives" (Stop) with
            // the aircraft still on the runway — the pilot never gets guided fully off
            // (LPFR 28→F: Alt+Y reported "runway 10", ATC "not vacated"). Compute the
            // corridor-exit node here for every implicit exit so ApronNodeId always
            // points off the pavement. Kept SEPARATE from implicitApronNodeId so the
            // ExitBearingTrue overrides above see the exact value they always have.
            int implicitApronForHandoff = implicitApronNodeId;
            if (!isHoldShortNode && implicitApronForHandoff <= 0)
                implicitApronForHandoff = ExitPathLeavesCorridor(
                    node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);

            // End-of-runway classification: if the exit is within the last 15% of the
            // runway, label it "End" regardless of angle — exiting there means rolling
            // out the full length.
            double endRatio = alongFt / rwy.Length;
            string exitType;
            if (endRatio > END_RATIO)
                exitType = "End";
            else if (exitAngle <= HIGH_SPEED_MAX_DEG)
                exitType = "High-speed";
            else if (exitAngle <= NORMAL_MAX_DEG)
                exitType = "Normal";
            else
                exitType = "End";

            exits.Add(new LandingExit
            {
                NodeId = node.NodeId,
                // HS/IHS exits: normally the hold-short bar is at the junction (apron side).
                // Exception: shallow HS exits on curved RETs — BFS found a corridor-exit node
                // further along the curve (hsApronNodeId > 0). That node is used instead so
                // the Taxiing-handoff re-route drives A* through the actual curve geometry.
                // Fallback Normal exits: corridor-exit node (implicitApronForHandoff),
                // computed for EVERY implicit exit — not just the shallow-angle ones —
                // so the handoff destination is always clear of the runway pavement.
                ApronNodeId = isHoldShortNode
                    ? (hsApronNodeId > 0 ? hsApronNodeId : node.NodeId)
                    : implicitApronForHandoff,
                Latitude = node.Latitude,
                Longitude = node.Longitude,
                DistanceFromThresholdFeet = distFromLandingThresholdFt,
                DistanceFromTouchdownFeet = distFromLandingThresholdFt - TOUCHDOWN_AIM_FT,
                TaxiwayName = taxiwayName,
                ExitAngleDegrees = exitAngle,
                ExitBearingTrue = exitBearingTrue,
                ExitType = exitType,
                ExitSide = exitBearingTrue != 0.0
                    ? (NormalizeAngle((exitBearingTrue == 360.0 ? 0.0 : exitBearingTrue) - rwyHeadingTrue) >= 0 ? "Right" : "Left")
                    : ""
            });
        }

        // Deduplicate exits that share the same taxiway name and are within 50 ft of
        // each other along the runway (happens when both sides of a taxiway intersection
        // produce a hold-short node). Keep the one with the smaller angle (better RET
        // candidate) or, if equal, the one closer to the threshold.
        exits.Sort((a, b) =>
        {
            int c = a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet);
            return c;
        });

        var deduped = new List<LandingExit>(exits.Count);
        foreach (var e in exits)
        {
            bool merged = false;
            for (int i = deduped.Count - 1; i >= 0; i--)
            {
                var d = deduped[i];
                if (Math.Abs(d.DistanceFromThresholdFeet - e.DistanceFromThresholdFeet) > DEDUP_WINDOW_FT)
                    break; // list is sorted; no further candidates within DEDUP_WINDOW_FT
                if (string.Equals(d.TaxiwayName, e.TaxiwayName, StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the one with smaller exit angle.
                    if (e.ExitAngleDegrees < d.ExitAngleDegrees)
                        deduped[i] = e;
                    merged = true;
                    break;
                }
            }
            if (!merged) deduped.Add(e);
        }

        // Candidate pool for the coverage gap fill at the very end. Snapshotted HERE, before
        // the two name dedups below, because those are where a genuinely separate turnoff
        // sharing a name gets thrown away — at KBNA 20L the High-speed dedup immediately
        // below is what removes the forward RETs at 4497 and 6155 ft, leaving the gap fill
        // nothing but backward-peeling End nodes to work with. The 50 ft window dedup above
        // has already run, so co-located duplicates never enter the pool.
        var gapFillPool = new List<LandingExit>(deduped);

        // High-speed RET dedup: a curved RET whose navdata has multiple HS/IHS nodes
        // along its arc (common in third-party scenery) generates one High-speed entry
        // per node, all more than 50 ft apart — the window above doesn't catch them.
        // The threshold-nearest node is the RET entry point; interior curve nodes are not
        // meaningful separate choices. Normal and End exits keep the 50 ft window only —
        // a Normal taxiway crossing the runway at 90° twice is a legitimate pair.
        {
            var hsSeenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hsDedupedList = new List<LandingExit>(deduped.Count);
            foreach (var e in deduped)
            {
                if (e.ExitType != "High-speed" || string.IsNullOrEmpty(e.TaxiwayName))
                {
                    hsDedupedList.Add(e);
                    continue;
                }
                if (hsSeenNames.Add(e.TaxiwayName)) hsDedupedList.Add(e);
            }
            deduped = hsDedupedList;
        }

        // Fallback-mode extra dedup: curved RETs (e.g. LVFR LEMD) generate many Normal
        // nodes along the same exit curve — all pass ExitPathLeavesCorridor but span
        // hundreds of feet of runway, far beyond the 50 ft window above. When no
        // HS/IHS nodes exist for this runway, keep only the first (threshold-nearest)
        // occurrence per named taxiway. That entry-point node is what matters; interior
        // curve nodes are not meaningful exit choices.
        //
        // Also handles the "HS-only-ends" case: when HS mode yielded only End-type exits,
        // the hold-short data isn't providing useful pre-end exits for this landing direction.
        // Typical cause: a curved RET HSND node designed for the opposite runway direction
        // (e.g. N4 at EIDW — a 28R rapid exit whose node lies inside the 10L corridor) is
        // the only HS node in range, but its exit angle is backward/End for 10L landings.
        // In that case we run a second pass collecting Normal-node fallback exits, merge them
        // with the HS End exits, and return the combined deduplicated list.
        bool hsOnlyEnds = hasHoldShortOnRunway && deduped.Count > 0
            && deduped.TrueForAll(e => e.ExitType == "End");

        // HS nodes exist in corridor but every one failed the distance filter
        // (too close to threshold or beyond END_BUFFER). Treat the same as
        // hsOnlyEnds — run the Normal-node fallback to find usable exits.
        bool hsYieldedNothing = hasHoldShortOnRunway && deduped.Count == 0;

        // Whether this runway is taking the Normal-node fallback at all. Also decides
        // whether the strict per-name dedup at the very end applies — see there.
        bool onFallbackPath = !hasHoldShortOnRunway || hsOnlyEnds || hsYieldedNothing;

        if (onFallbackPath)
        {
            if (hsOnlyEnds || hsYieldedNothing)
            {
                var fallbackExits = new List<LandingExit>();
                foreach (var node in Nodes.Values)
                {
                    if (node.Type != TaxiNodeType.Normal) continue;
                    if (!Adjacency.TryGetValue(node.NodeId, out var ee)) continue;

                    bool hasOffAxis = false;
                    int apronNode = -1;
                    foreach (var ed in ee)
                    {
                        if (string.IsNullOrEmpty(ed.TaxiwayName)) continue;
                        double rel = Math.Abs(NormalizeAngle(ed.BearingDegrees - rwyHeadingTrue));
                        double off = rel > 90.0 ? 180.0 - rel : rel;
                        if (off >= MIN_FALLBACK_EXIT_ANGLE_DEG) { hasOffAxis = true; break; }
                    }
                    if (!hasOffAxis)
                    {
                        apronNode = ExitPathLeavesCorridor(node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);
                        if (apronNode < 0) continue;
                        hasOffAxis = true;
                    }
                    if (!hasOffAxis) continue;

                    const double MPD2 = 111132.0;
                    double latR2 = (rwy.StartLat + node.Latitude) * 0.5 * Math.PI / 180.0;
                    double mPL2 = MPD2 * Math.Cos(latR2);
                    double dN2 = (node.Latitude - rwy.StartLat) * MPD2;
                    double dE2 = (node.Longitude - rwy.StartLon) * mPL2;
                    double aM2 = dE2 * sinH + dN2 * cosH;
                    double lM2 = dE2 * cosH - dN2 * sinH;
                    if (Math.Abs(lM2) > lateralToleranceM || aM2 < 0 || aM2 > lengthM + 50.0) continue;
                    double aFt2 = aM2 / 0.3048;
                    double dft2 = aFt2 - landingThresholdOffsetFt;
                    if (dft2 < MIN_DIST_FT || aFt2 > maxDistFt) continue;

                    string txName2 = "";
                    double angle2 = 90.0;
                    TaxiEdge? best2 = null;
                    double best2Brg = 0.0; // 0 = not found; due-north stored as 360
                    foreach (var e in ee)
                    {
                        if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)
                            || string.IsNullOrEmpty(e.TaxiwayName)) continue;
                        if (best2 == null) { best2 = e; continue; }
                        bool b2hd = HasLetterAndDigit(best2.TaxiwayName);
                        bool ehd  = HasLetterAndDigit(e.TaxiwayName);
                        if (ehd && !b2hd)
                        {
                            best2 = e;
                        }
                        else if (ehd == b2hd)
                        {
                            double b2r = Math.Abs(NormalizeAngle(best2.BearingDegrees - rwyHeadingTrue));
                            double b2o = b2r > 90.0 ? 180.0 - b2r : b2r;
                            double er  = Math.Abs(NormalizeAngle(e.BearingDegrees - rwyHeadingTrue));
                            double eo  = er > 90.0 ? 180.0 - er : er;
                            if (eo > b2o) best2 = e;
                        }
                    }
                    if (best2 != null)
                    {
                        txName2 = best2.TaxiwayName;
                        best2Brg = best2.BearingDegrees == 0.0 ? 360.0 : best2.BearingDegrees;
                        double rel2 = Math.Abs(NormalizeAngle(best2.BearingDegrees - rwyHeadingTrue));
                        bool pb2 = rel2 > 90.0;
                        angle2 = pb2 ? 180.0 - rel2 : rel2;
                        if (pb2 && angle2 < 50.0) angle2 = NORMAL_MAX_DEG + 20.0;
                    }
                    // Same targeted apron-bearing override: only for near-parallel first
                    // edges (< 5°) and only when the apron is in the forward direction.
                    if (angle2 < 5.0 && apronNode > 0
                        && Nodes.TryGetValue(apronNode, out var apronTaxiNode2))
                    {
                        const double MPD_BRG2 = 111132.0;
                        double latRc = (node.Latitude + apronTaxiNode2.Latitude) * 0.5 * Math.PI / 180.0;
                        double mPLc = MPD_BRG2 * Math.Cos(latRc);
                        double dNc = (apronTaxiNode2.Latitude - node.Latitude) * MPD_BRG2;
                        double dEc = (apronTaxiNode2.Longitude - node.Longitude) * mPLc;
                        double apronBrg2 = Math.Atan2(dEc, dNc) * 180.0 / Math.PI;
                        if (apronBrg2 < 0) apronBrg2 += 360.0;
                        if (Math.Abs(NormalizeAngle(apronBrg2 - rwyHeadingTrue)) <= NORMAL_MAX_DEG)
                            best2Brg = apronBrg2 == 0.0 ? 360.0 : apronBrg2;
                    }

                    double er2 = aFt2 / rwy.Length;
                    string et2 = er2 > END_RATIO ? "End"
                        : angle2 <= HIGH_SPEED_MAX_DEG ? "High-speed"
                        : angle2 <= NORMAL_MAX_DEG ? "Normal"
                        : "End";

                    fallbackExits.Add(new LandingExit
                    {
                        NodeId = node.NodeId,
                        ApronNodeId = apronNode,
                        Latitude = node.Latitude,
                        Longitude = node.Longitude,
                        DistanceFromThresholdFeet = dft2,
                        DistanceFromTouchdownFeet = dft2 - TOUCHDOWN_AIM_FT,
                        TaxiwayName = txName2,
                        ExitAngleDegrees = angle2,
                        ExitBearingTrue = best2Brg,
                        ExitType = et2,
                        ExitSide = best2Brg != 0.0
                            ? (NormalizeAngle((best2Brg == 360.0 ? 0.0 : best2Brg) - rwyHeadingTrue) >= 0 ? "Right" : "Left")
                            : ""
                    });
                }

                if (fallbackExits.Count > 0)
                {
                    var merged = new List<LandingExit>(deduped.Count + fallbackExits.Count);
                    merged.AddRange(deduped);
                    merged.AddRange(fallbackExits);
                    merged.Sort((a, b) => a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet));
                    deduped = new List<LandingExit>(merged.Count);
                    foreach (var e in merged)
                    {
                        bool wasMerged = false;
                        for (int i = deduped.Count - 1; i >= 0; i--)
                        {
                            var d = deduped[i];
                            if (Math.Abs(d.DistanceFromThresholdFeet - e.DistanceFromThresholdFeet) > DEDUP_WINDOW_FT) break;
                            if (string.Equals(d.TaxiwayName, e.TaxiwayName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (e.ExitAngleDegrees < d.ExitAngleDegrees) deduped[i] = e;
                                wasMerged = true; break;
                            }
                        }
                        if (!wasMerged) deduped.Add(e);
                    }

                    // These arrived after the pool snapshot above, so add them or a runway
                    // WITH hold-short markers could never gap-fill from its Normal-junction
                    // exits. KBNA 13 is that case: 11031 ft of runway whose last listed exit
                    // was at 4988 ft. Duplicates against already-kept entries are harmless —
                    // the coverage test measures zero distance to itself.
                    gapFillPool.AddRange(fallbackExits);
                    gapFillPool.Sort((a, b) => a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet));
                }
            }
        }

        // Name dedup on the FALLBACK PATH ONLY, then a COVERAGE GAP FILL in every path.
        // `deduped` is sorted by distance from the threshold at this point (every branch
        // that builds it sorts).
        //
        // The strict pass is scoped to `onFallbackPath` because that is the scope it has
        // always had. A runway with usable hold-short markers keeps both junctions of a
        // same-named taxiway that meets it twice — the case the High-speed dedup comment
        // above calls "a legitimate pair" (EGLL 09R: S5W at 5659 and 6605 ft, N5E at 5860
        // and 6771). Running the pass there instead drops the FARTHER junction, and the
        // coverage fill cannot restore it: it re-admits only exits with no survivor within
        // EXIT_COVERAGE_GAP_FT, and those pairs sit 900-950 ft apart. RetargetLandingExit's
        // downfield rescan would then skip past a real turnoff to a farther one.
        //
        // This used to keep only the FIRST occurrence of each taxiway name, unconditionally.
        // That is right when a scenery names its connectors individually (one turnoff = one
        // name, and the repeats are extra nodes along one RET arc), and it is what stops a
        // curved RET modelled as eight nodes becoming eight list entries. But it silently
        // assumes "one name = one turnoff", and some sceneries give an entire side of a
        // runway a single name.
        //
        // Motivating defect (reported 2026-08-08): KBNA 20L offered ONE exit. That scenery
        // has just eight taxiway names at the whole airport (A-H, 1486 of 2437 segments
        // unnamed, zero numbered connectors), and every segment touching 02R/20L is named
        // "G". The runway's five real turnoffs — measured at ~1813, 3719, 4497, 6155 and
        // 7812 ft — therefore collapsed to one, and the survivor was the threshold-nearest
        // node, which is a backward-peeling arc (angle forced to NORMAL_MAX_DEG + 20) at
        // 1467 ft: a 130-degree turn 1500 ft down a 7991 ft runway. 02R is the mirror image.
        // Airports whose connectors carry distinct names (KBOS, EGLL, KJFK, EIDW ...) never
        // showed the bug because their name dedup only ever collapsed arcs.
        //
        // The strict pass below is therefore KEPT EXACTLY AS IT WAS, and a second pass only
        // fills GAPS: an exit either name dedup dropped is re-admitted when — and only when —
        // no surviving exit lies within EXIT_COVERAGE_GAP_FT of it, so it is the sole option
        // for that stretch of runway. A dropped exit that merely duplicates coverage the
        // pilot already has stays dropped, which is what keeps well-named airports untouched.
        //
        // Why COVERAGE and not "split same-name runs by distance": splitting runs was tried
        // first and is far too broad — measured across 266 runway directions in 39 airports
        // it changed 145 of them and added 212 rows, because a link taxiway commonly meets a
        // runway at two junctions a few hundred feet apart (EGLL 09R: S5W at 5659 and 6605
        // ft, N5E at 5860 and 6771, S4E at 7707 and 8140). Those are real junctions but not
        // turnoffs the pilot was missing, and a list with three duplicated names is worse
        // than the list they have today. Coverage is the property that separates the two
        // populations cleanly — see EXIT_COVERAGE_GAP_FT for the measurement.
        //
        // Deliberately NOT done here: swapping a run's representative for a later
        // forward-peeling one. It reads like an improvement (it would replace KBNA's
        // backward 130-degree entries with the forward High-speed nodes behind them) but it
        // re-typed 118 exits from End to High-speed across the same sweep, and ExitType
        // drives TryEarlyExitHandoff, which fires for High-speed exits ONLY and has its own
        // hard-won invariant (the EGNX miss). Adding rows is safe; re-typing existing ones
        // in bulk is not.
        var dedupedFinal = new List<LandingExit>(deduped.Count);
        if (onFallbackPath)
        {
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in deduped)
            {
                if (string.IsNullOrEmpty(e.TaxiwayName)) { dedupedFinal.Add(e); continue; }
                if (seenNames.Add(e.TaxiwayName)) dedupedFinal.Add(e);
            }
        }
        else
        {
            dedupedFinal.AddRange(deduped);
        }

        // Coverage gap fill. `gapFillPool` is distance-sorted (it was snapshotted from a
        // sorted list), so candidates are offered threshold-first and one that is admitted
        // becomes coverage for the next — an arc of many nodes still contributes at most one
        // entry per EXIT_COVERAGE_GAP_FT. Reference identity is the right "already kept"
        // test: every LandingExit instance flows through the pipeline by reference, never
        // copied.
        bool addedAny = false;
        foreach (var e in gapFillPool)
        {
            bool covered = false;
            foreach (var kept in dedupedFinal)
            {
                if (ReferenceEquals(kept, e)
                    || Math.Abs(kept.DistanceFromThresholdFeet - e.DistanceFromThresholdFeet) <= EXIT_COVERAGE_GAP_FT)
                { covered = true; break; }
            }
            if (covered) continue;
            dedupedFinal.Add(e);
            addedAny = true;
        }

        // Callers (LandingExitPlanner's downfield scan, the planner form's default
        // selection, RetargetLandingExit) all rely on nearest-first ordering.
        if (addedAny)
            dedupedFinal.Sort((a, b) => a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet));

        return dedupedFinal;
    }

    private static bool HasLetterAndDigit(string s)
    {
        bool hasL = false, hasD = false;
        foreach (char c in s)
        {
            if (char.IsLetter(c)) hasL = true;
            else if (char.IsDigit(c)) hasD = true;
            if (hasL && hasD) return true;
        }
        return false;
    }

    // BFS from startNodeId. Returns the node ID of the first reachable node that lies
    // outside the runway lateral corridor (|lateral| > lateralToleranceM) within
    // MAX_RET_SEARCH_M metres, or -1 if none found.
    // Used to detect smooth-curve RETs whose individual segments each fall below the
    // MIN_FALLBACK_EXIT_ANGLE_DEG threshold yet still exit the runway.
    //
    // The returned node ID is used as ApronNodeId on the LandingExit so the
    // LandingRollout → Taxiing handoff can re-route from the pilot's live position
    // to that corridor-exit point, giving correct tone guidance through the curve.
    //
    // Seeding: only named-taxiway edges from the start node (the node must be a real
    // taxiway junction, not just an unnamed runway surface waypoint).
    // Traversal: all edges — named and unnamed — so the BFS can cross unnamed connector
    // segments that some scenery packages insert between the named RET portions.
    //
    // A truly parallel taxiway that never leaves the runway strip returns -1. A real
    // RET — however shallow the departure angle — returns its first corridor-exit node.
    private int ExitPathLeavesCorridor(
        int startNodeId,
        double rwyStartLat, double rwyStartLon,
        double cosH, double sinH,
        double lateralToleranceM)
    {
        const double MAX_RET_SEARCH_M = 600.0;
        const double METERS_PER_DEG_LAT = 111132.0;

        if (!Adjacency.TryGetValue(startNodeId, out var initEdges)) return -1;

        // Require at least one named adjacent edge — node must be a taxiway junction.
        bool hasNamedStart = false;
        foreach (var e in initEdges)
            if (!string.IsNullOrEmpty(e.TaxiwayName)) { hasNamedStart = true; break; }
        if (!hasNamedStart) return -1;

        var visited = new HashSet<int> { startNodeId };
        var queue = new Queue<(int nodeId, double dist)>();

        // Seed from named edges only.
        foreach (var e in initEdges)
        {
            if (!string.IsNullOrEmpty(e.TaxiwayName))
                queue.Enqueue((e.ToNodeId, e.DistanceMeters));
        }

        while (queue.Count > 0)
        {
            var (nodeId, dist) = queue.Dequeue();
            if (visited.Contains(nodeId)) continue;
            visited.Add(nodeId);

            if (!Nodes.TryGetValue(nodeId, out var node)) continue;

            double latR = (rwyStartLat + node.Latitude) * 0.5 * Math.PI / 180.0;
            double mPerLon = METERS_PER_DEG_LAT * Math.Cos(latR);
            double dN = (node.Latitude - rwyStartLat) * METERS_PER_DEG_LAT;
            double dE = (node.Longitude - rwyStartLon) * mPerLon;
            double lateralM = Math.Abs(dE * cosH - dN * sinH);

            if (lateralM > lateralToleranceM) return nodeId;

            if (dist >= MAX_RET_SEARCH_M) continue;

            if (!Adjacency.TryGetValue(nodeId, out var edges)) continue;
            foreach (var e in edges)
            {
                // Follow all edges (named and unnamed) so unnamed connector segments
                // between the named portions of a RET don't break the chain.
                if (visited.Contains(e.ToNodeId)) continue;
                double newDist = dist + e.DistanceMeters;
                if (newDist <= MAX_RET_SEARCH_M)
                    queue.Enqueue((e.ToNodeId, newDist));
            }
        }
        return -1;
    }

    #endregion
}
