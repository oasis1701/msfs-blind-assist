using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public partial class TaxiGuidanceManager
{
    /// <summary>
    /// Uncharted-crossing eligibility: the aircraft must genuinely be ON its own
    /// side of the network (nearest node within this) before a cross-component
    /// route is attempted — an aircraft nowhere near any taxi node gives the
    /// crossing builder no honest anchor.
    /// </summary>
    private const double UNCHARTED_AIRCRAFT_MAX_SNAP_M = 200.0;

    /// <summary>
    /// Loads and calculates a route. Returns null on success, error message on failure.
    /// </summary>
    public string? LoadRoute(
        IAirportDataProvider dataProvider,
        string icao,
        double aircraftLat, double aircraftLon, double aircraftHeading,
        int destinationNodeId,
        string destinationName,
        List<string>? taxiwaySequence,
        List<int>? userHoldShortIndices = null,
        double? destinationHeading = null,
        double? destinationThresholdLat = null,
        double? destinationThresholdLon = null,
        double? destinationHeadingTrue = null,
        bool isRunwayDestination = false,
        TaxiGraph? prebuiltGraph = null,
        // When false, the "Taxi to <dest> via <seq>, total distance …" callout
        // is suppressed. The LandingExitPlanner uses this during auto-activation
        // at touchdown, because it emits its own touchdown-specific callout and
        // we don't want three announcements fighting for audio during rollout.
        bool announceSummary = true,
        // User-explicit runway hold-shorts from the form: maps taxiway-sequence
        // index → runway designator. After taxiway[seqIdx], guidance holds before
        // the route enters or crosses that runway (ApplyUserRunwayHoldShorts). The
        // automatic runway hold pass still runs on top, so most users never need
        // this; it's the explicit override for ATC clearances where the pilot
        // wants to confirm the SPECIFIC runway named.
        Dictionary<int, string>? userRunwayHoldShorts = null,
        // Taxiway-flavored twin of userRunwayHoldShorts (per-row "Hold short of"
        // combo picks with the "Taxiway " prefix): sequence index → taxiway name
        // to stop short of after that row's taxiway. "Taxi via A, hold short of
        // taxiway B" — the standard post-landing instruction at KATL/OMDB/VHHH-
        // class fields (VATSIM gap analysis 2026-08-31, P2). Guidance stops at
        // the first junction with the named taxiway after the chosen row.
        Dictionary<int, string>? userTaxiwayHoldShorts = null,
        // Non-null for Progressive Taxi legs. Carries the terminal end-state
        // type/target; drives the progressive-hold end announcement and (for
        // AfterCrossingRunway) suppresses the auto hold-short on the cleared
        // crossing. Task 4 consumes this for the terminal state machine.
        Navigation.ProgressiveTerminator? progressiveTerminator = null,
        // When true (Taxi planner "CAT III / low-visibility hold" checkbox), a
        // runway-destination route holds at the CAT III / ILS hold-short (further
        // back) instead of the default full-length hold. See TruncateToHoldShort.
        bool preferIlsHold = false,
        // Landing-exit EARLY handoff only (TryEarlyExitHandoff). When set — and only
        // when no taxiwaySequence is given — the route START is snapped to the nearest
        // node ON THIS taxiway instead of the nearest node overall. The early handoff
        // fires while the aircraft is still on the runway, possibly abeam a NEIGHBOURING
        // exit; without this anchor the start snaps to that neighbour and A* routes a long
        // hairpin up it and across the parallel taxiway to reach the target (EIDW 28L S6
        // via S5+S, ~600 m — reproduced from the navdata). Null keeps the legacy snap.
        string? startTaxiwayName = null,
        // Landing-exit handoffs only, with startTaxiwayName: the exit's OWN path (junction
        // first). When the node the NAME anchor picks is not on it, the route starts from the
        // nearest node of this path instead — see ExitPathStartAnchor.
        IReadOnlyList<int>? exitPathNodeIds = null,
        // FULL-LENGTH BACKTRACK DEPARTURE (opt-in, Taxi planner checkbox). When true,
        // destinationNodeId is an INTERMEDIATE runway ENTRANCE (found by the form via
        // TaxiGraph.FindBacktrackEntryNode) while destinationThresholdLat/Lon stays the
        // FULL-LENGTH departure threshold. The route holds short of the entrance; after
        // Continue, guidance backtracks to the threshold and lines up full length. See
        // the BacktrackDeparture state.
        bool fullLengthBacktrack = false,
        // NAMED-HOLDING-POINT DEPARTURE (Taxi planner "Depart from named holding point").
        // The graph node the chosen painted hold LINE sits on
        // (TaxiGraph.RunwayIntersection.HoldNodeId), while destinationNodeId stays that
        // point's runway ENTRY node. The route is pinned through it so the pilot taxis up
        // the stub they named — see ApplyHoldingPointPin. Null/0 for every other route.
        int? holdingPointHoldNodeId = null,
        // Magnetic variation (EAST POSITIVE) that converts `aircraftHeading` to TRUE.
        // Pass 0 when `aircraftHeading` is ALREADY true — every in-manager caller
        // (the three Rollout re-routes and LandingExitPlanner) passes a `headingTrue`,
        // so they correctly leave this at the default. The two TaxiAssistForm callers
        // pass SimConnect's HeadingMagnetic and MUST supply the variation.
        //
        // Used ONLY to compose LastRouteInitialTurnCue. It exists because the cue's angle
        // comes from ComputeSteeringHeadingError -- the tone's own definition, which is
        // (geodetic TRUE bearing to the look-ahead walk target) - headingTrue. Subtracting
        // a MAGNETIC heading from a TRUE bearing biases the angle by exactly the variation,
        // and near +/-180 deg -- the near-U-turn case this cue exists for -- that bias
        // FLIPS THE SIGN, so the words say one direction while the tone pans the other.
        // A blind pilot has no third source to break that tie.
        //
        // Not hypothetical: the live KATL 2026-08-27 incident this cue was repaired for
        // had a first-frame error of -175.78 deg (a LEFT turnaround) where the variation
        // is about -5.5 deg. Uncorrected that computes as -181.28 -> +178.72 -> "turn
        // RIGHT to come around", contradicting the tone on the exact flight being fixed.
        // Do not remove this conversion.
        double aircraftHeadingMagVar = 0.0,
        // Whether this route is being adopted FOR THE LANDING ROLLOUT — LandingExitPlanner's
        // touchdown route, RetargetLandingExit's route to another exit, and both landing-handoff
        // re-routes. It LABELS the "Route crossings:" log line (phase=touchdown) and does nothing
        // else: whether the route may start held is now decided by the pass, from the aircraft's own
        // position and AircraftPosition.MayStartHeld (false only from TryRecalculateRoute), never
        // from ground speed, a gate tried and withdrawn; that is the whole of PR #238 deferred
        // finding §2. The old
        // `allowStartHold` bool travelled bool -> "load"/"touchdown" string -> bool, so a fourth
        // phase or a typo silently disabled start holds with no compile error.
        bool landingRolloutRoute = false,
        // The picked point's LABEL ("A1"), spoken when the route's stop node IS the
        // picked node. Null/empty for every other route.
        string? holdingPointName = null,
        // A picked INTERSECTION departure (the form's intersection list). The pilot chose that
        // taxiway, so the reversed-entry re-route (TryForwardFacingRunwayEntry) never swaps it
        // for another: VirtualPilot 2026-09-24 moved 135 intersection departures (LTFM A4 → A3B)
        // when it did. Full-length departures only.
        bool intersectionDeparture = false)
    {
        lock (_stateLock)
        {
        // Snapshot every recalculation/companion field this method mutates before
        // the route is known to build, and restore ALL of them on every failure
        // exit. These fields used to be overwritten up front and left torn when a
        // later step failed: with guidance ACTIVE on an existing route, a failed
        // Calculate (e.g. an ATC amendment that couldn't route) kept the OLD route
        // steering while _destinationNodeId/_originalTaxiwaySequence/_isRunwayLineup
        // and the lineup target all described the FAILED request — the next
        // off-route recalc then silently rerouted to the failed request's
        // destination, and a runway arrival ran the gate-arrival branch (or vice
        // versa). A failed LoadRoute must mean "nothing changed".
        var priorDataProvider = _dataProvider;
        var priorDestinationNodeId = _destinationNodeId;
        var priorDestinationName = _destinationName;
        var priorIcao = _icao;
        var priorSequence = _originalTaxiwaySequence;
        var priorUserHoldShortIndices = _userHoldShortIndices;
        var priorUserRunwayHoldShorts = _userRunwayHoldShorts;
        var priorUserTaxiwayHoldShorts = _userTaxiwayHoldShorts;
        var priorPreferIlsHold = _preferIlsHold;
        var priorBacktrackDeparture = _backtrackDeparture;
        var priorBacktrackDepApproachAnnounced = _backtrackDepApproachAnnounced;
        var priorHoldingPointHoldNodeId = _holdingPointHoldNodeId;
        var priorHoldingPointName = _holdingPointName;
        var priorIsRunwayLineup = _isRunwayLineup;
        var priorProgressiveTerminator = _progressiveTerminator;
        var priorLineupTargetLat = _lineupTargetLat;
        var priorLineupTargetLon = _lineupTargetLon;
        var priorLineupHeadingMag = _lineupHeadingMag;
        var priorLineupHeadingTrue = _lineupHeadingTrue;
        var priorHasLineupTarget = _hasLineupTarget;
        var priorAutoActivateFired = _autoActivateFired;
        var priorPostHighSpeedExitMinBearing = _postHighSpeedExitMinBearing;
        var priorGraph = _graph;
        var priorGraphGeneration = _graphGeneration;
        var priorHoldShortWarning = LastRouteHoldShortWarning;
        var priorStartWarning = LastRouteStartWarning;
        string? Fail(string message)
        {
            _dataProvider = priorDataProvider;
            _destinationNodeId = priorDestinationNodeId;
            _destinationName = priorDestinationName;
            _icao = priorIcao;
            _originalTaxiwaySequence = priorSequence;
            _userHoldShortIndices = priorUserHoldShortIndices;
            _userRunwayHoldShorts = priorUserRunwayHoldShorts;
            _userTaxiwayHoldShorts = priorUserTaxiwayHoldShorts;
            _preferIlsHold = priorPreferIlsHold;
            _backtrackDeparture = priorBacktrackDeparture;
            _backtrackDepApproachAnnounced = priorBacktrackDepApproachAnnounced;
            _holdingPointHoldNodeId = priorHoldingPointHoldNodeId;
            _holdingPointName = priorHoldingPointName;
            _isRunwayLineup = priorIsRunwayLineup;
            _progressiveTerminator = priorProgressiveTerminator;
            _lineupTargetLat = priorLineupTargetLat;
            _lineupTargetLon = priorLineupTargetLon;
            _lineupHeadingMag = priorLineupHeadingMag;
            _lineupHeadingTrue = priorLineupHeadingTrue;
            _hasLineupTarget = priorHasLineupTarget;
            _autoActivateFired = priorAutoActivateFired;
            _postHighSpeedExitMinBearing = priorPostHighSpeedExitMinBearing;
            _graph = priorGraph;
            _graphGeneration = priorGraphGeneration;
            LastRouteHoldShortWarning = priorHoldShortWarning;
            LastRouteStartWarning = priorStartWarning;
            return message;
        }
        try
        {
            // Work on a private copy — alias resolution below rewrites entries in
            // place, and the list object belongs to the caller (the form / the
            // SayIntentions import each pass their own list).
            taxiwaySequence = taxiwaySequence != null ? new List<string>(taxiwaySequence) : null;

            // Snapshot every field this method is about to overwrite, so a reachability
            // refusal below — no start node in range, no buildable route, or a first leg
            // across a runway, each only for a destination off the network the aircraft is on
            // — can put the manager back exactly as it found it: a refused Calculate mid-taxi
            // must leave the route currently being flown untouched — recalculations must
            // keep targeting the OLD destination, not the refused one, and an old runway
            // route must keep its lineup target. The older failure returns further down (no
            // taxi path data, destination node not found, no nearby taxiway node, could not
            // calculate a route) apply to every OTHER reachability class, including
            // Unchanged, and are deliberately left as they are; widening the rollback to
            // cover them is a separate, unasked-for change.
            var rollback = CaptureLoadRouteRollback();

            // Store for recalculation
            _dataProvider = dataProvider;
            _destinationNodeId = destinationNodeId;
            _destinationName = destinationName;
            _icao = icao ?? "";
            _originalTaxiwaySequence = taxiwaySequence;
            // Private copies: TryRecalculateRoute re-applies these to a
            // recalculated route, and the caller may reuse/mutate its own
            // collections after this call returns.
            _userHoldShortIndices = userHoldShortIndices != null
                ? new List<int>(userHoldShortIndices) : null;
            _userRunwayHoldShorts = userRunwayHoldShorts != null
                ? new Dictionary<int, string>(userRunwayHoldShorts) : null;
            _userTaxiwayHoldShorts = userTaxiwayHoldShorts != null
                ? new Dictionary<int, string>(userTaxiwayHoldShorts) : null;
            _preferIlsHold = preferIlsHold;
            _backtrackDeparture = fullLengthBacktrack;
            _backtrackDepApproachAnnounced = false;
            // Assigned unconditionally (0 when absent) so a plain route can never inherit
            // the previous route's holding-point pin through the recalc path.
            _holdingPointHoldNodeId = holdingPointHoldNodeId ?? 0;
            _holdingPointName = _holdingPointHoldNodeId != 0 ? (holdingPointName ?? "") : "";
            // Cleared for every load, so a failed load can never leave the previous route's
            // unmapped-start warning for the form or the one-shot to speak.
            LastRouteUnmappedStartWarning = null;
            // A new route/destination must be able to speak its own first recalculation
            // reachability refusal (ReachabilityRefusalGate), not stay silent because the
            // PREVIOUS route already spoke an identically-keyed one.
            _lastReachabilityRefusalKey = null;

            // Store lineup target data (runway threshold or gate position) for lineup phase
            _isRunwayLineup = isRunwayDestination;
            _progressiveTerminator = progressiveTerminator;
            if (destinationThresholdLat.HasValue && destinationThresholdLon.HasValue && destinationHeading.HasValue)
            {
                _lineupTargetLat = destinationThresholdLat.Value;
                _lineupTargetLon = destinationThresholdLon.Value;
                _lineupHeadingMag = destinationHeading.Value;
                _lineupHeadingTrue = destinationHeadingTrue ?? _lineupHeadingMag;
                _hasLineupTarget = true;
            }
            else
            {
                _lineupTargetLat = 0;
                _lineupTargetLon = 0;
                _lineupHeadingMag = 0;
                _lineupHeadingTrue = 0;
                _hasLineupTarget = false;
            }

            // Reset the one-shot auto-activate latch so this new route can
            // fire RequestTakeoffAssistAutoActivate on its first lineup-aligned
            // transition.
            _autoActivateFired = false;
            _postHighSpeedExitMinBearing = 0.0;

            // Use pre-built graph if provided (avoids a 200-500ms rebuild stall at large
            // airports when the form already ran BuildAsync). Otherwise build from the DB —
            // used by auto-recalculation paths that don't carry a graph.
            if (prebuiltGraph != null)
            {
                _graph = prebuiltGraph;
            }
            else
            {
                var paths = dataProvider.GetTaxiPaths(icao!);
                if (paths.Count == 0)
                    return Fail("No taxi path data available for this airport.");

                var parking = ResolveParkingSpots(dataProvider, icao!);
                var starts = dataProvider.GetRunwayStarts(icao!);

                // Runways let the builder repair laterally-bogus start rows before they
                // reach the centerlines (TaxiGraph.SnapStartToRunwayCenterline).
                _graph = TaxiGraph.Build(paths, parking, starts, dataProvider.GetRunways(icao!));
            }

            // Which database this graph belongs to — the runway probe stops trusting it once a
            // database switch has moved the generation (see _graphGeneration). Stamped only for a
            // NEW instance: the rollout re-routes hand back `prebuiltGraph: _graph`, the SAME graph,
            // and restamping it after a database switch mid-rollout would file the previous
            // database's graph under the new generation. `rollback` (captured at the top of this
            // method, before _graph was touched) holds the instance that was installed until now.
            if (!ReferenceEquals(_graph, rollback.Graph)) _graphGeneration = DatabaseGeneration;

            if (_graph.Nodes.Count == 0)
                return Fail("Could not build taxi graph for this airport.");

            // Find start node. When the pilot has specified a constrained
            // taxiway sequence, prefer a node on the FIRST taxiway near the
            // aircraft — heading is irrelevant for the lookup. Use case:
            // post-pushback the aircraft can be pointing 180° away from
            // where the first taxiway is, but the pilot will rotate / taxi
            // onto it. The heading-aware fallback (FindNearestNodeInDirection)
            // would in that case pick a non-V13 apron node "ahead" of the
            // aircraft, the route's approach segments would go in a direction
            // that doesn't match the aircraft's heading, and the off-route
            // detector would fire as soon as the pilot started moving.
            // Snapping directly to the requested first taxiway lets the
            // route start where it's supposed to, regardless of the aircraft's
            // current orientation.
            if (!_graph.Nodes.ContainsKey(destinationNodeId))
                return Fail("Destination node not found in taxi graph.");

            // Restrict start-node candidates to the destination's connected
            // component. Defends against navdata defects where a closer
            // taxiway is modelled as an isolated island (e.g. GCLP S5 in
            // fs2024) — without this filter, FindNearestNodeInDirection
            // would snap to the island and A* would fail with no path.
            int destComponentId = _graph.Nodes[destinationNodeId].ComponentId;

            // Route reachability (Navigation.RouteReachability). When the aircraft is not on the
            // destination's piece of network the route is still built exactly as below, starting on the
            // destination's piece, and its straight unmapped first leg is checked once the route exists:
            // refused when that leg touches a runway, otherwise announced with a warning. On runway
            // pavement or on no taxi edge the class is Unchanged and everything below runs exactly as
            // before.
            var reachability = RouteReachability.Classify(_graph, aircraftLat, aircraftLon, destinationNodeId);

            // Start-node selection. With a constrained taxiway sequence we prefer
            // a node ON the first cleared taxiway (heading-irrelevant) so the route
            // anchors on the clearance regardless of post-pushback orientation
            // (the LEPA fix). BUT when that taxiway is FAR from the aircraft
            // (a gate across an apron from a main parallel — CYYZ GB/GC -> A was
            // 297 m), pre-snapping onto it makes the guidance beeline across the
            // apron/grass. In that case start from the aircraft's nearest
            // in-component graph node so FindConstrainedPath builds a
            // pavement-following lead-in onto the taxiway (its Step-1 AStarSearch).

            // Resolve any pilot-entered alias names to canonical navdata names BEFORE any
            // routing or node-snapping. This is the single choke point — all callers benefit.
            // Example: pilot enters "K" but navdata calls the taxiway "HAWKER" →
            //          ResolveTaxiwayName maps "K" → "HAWKER" so routing finds the correct nodes.
            if (taxiwaySequence != null)
            {
                for (int i = 0; i < taxiwaySequence.Count; i++)
                    taxiwaySequence[i] = _graph.ResolveTaxiwayName(taxiwaySequence[i]);
            }

            // ── Runway-as-taxiway leg refusal (VATSIM gap analysis 2026-08-31, P3) ──
            // A real clearance can use an inactive runway as a taxi leg ("taxi Delta,
            // enter runway 25, vacate Charlie" — YSSY; EHAM's "via the north" along
            // 09/27), but runway pavement is deliberately NOT a routable corridor in
            // this graph. Such a leg used to die inside the router as a generic
            // "No nodes found on taxiway '25'" → fallback to a shortest path that may
            // bear no resemblance to the clearance, behind a warning that never says
            // WHY. Detect the shape up front: a leg the graph knows no taxiway for,
            // whose name matches one of this airport's runway designators, is removed
            // (the remaining taxiways still constrain the route) and refused BY NAME.
            // A leg that IS a known taxiway name is never touched, even if it looks
            // like a designator — some sceneries genuinely name taxiways "25".
            string? runwayLegNote = null;
            if (taxiwaySequence is { Count: > 0 })
            {
                var knownTaxiways = new HashSet<string>(
                    _graph.GetAllTaxiwayNames(), StringComparer.OrdinalIgnoreCase);
                var runwayIds = dataProvider.GetRunways(icao!)
                    .Select(r => r.RunwayID).ToList();
                List<string>? droppedRunwayLegs = null;
                for (int i = taxiwaySequence.Count - 1; i >= 0; i--)
                {
                    if (knownTaxiways.Contains(taxiwaySequence[i])) continue;
                    string? designator = MatchRunwayDesignator(taxiwaySequence[i], runwayIds);
                    if (designator == null) continue;
                    (droppedRunwayLegs ??= new List<string>()).Insert(0, designator);
                    taxiwaySequence.RemoveAt(i);
                }
                if (droppedRunwayLegs != null)
                {
                    runwayLegNote = droppedRunwayLegs.Count == 1
                        ? $"Runway {droppedRunwayLegs[0]} cannot be used as a taxi route leg; that leg was skipped."
                        : $"Runways {string.Join(", ", droppedRunwayLegs)} cannot be used as taxi route legs; those legs were skipped.";
                    if (taxiwaySequence.Count == 0)
                        taxiwaySequence = null;
                }
            }

            string? firstCleared = (taxiwaySequence is { Count: > 0 }) ? taxiwaySequence[0] : null;

            // ── UNCHARTED APRON CROSSING (cross-component destination) ──────────
            // When the aircraft stands on a DIFFERENT connected component than the
            // destination (default LMML: the P/N/NW aprons are an island 192 m of
            // path-less — but drivable — apron away from the network an arriving
            // aircraft is on), no charted route exists, and forcing the start into
            // the destination's component built a stub route starting hundreds of
            // metres from the aircraft. Instead: build both charted halves and join
            // them with ONE explicit synthetic segment the guidance announces
            // (TaxiRouter.FindCrossComponentPath — gap-capped, runway-corridor-
            // checked). Every failure falls through to the legacy island-side route
            // plus the DisconnectedIsland warning below, so this can only ADD a
            // usable path, never lose one. The component-filter invariant is
            // honoured in spirit: A* itself never runs across components — the two
            // charted legs are routed inside their own components only.
            TaxiRoute? crossingRoute = null;
            double crossingGapM = 0;
            int crossingStartNodeId = 0;
            {
                var aircraftNearestNode = _graph.FindNearestNode(aircraftLat, aircraftLon);
                if (aircraftNearestNode != null
                    && aircraftNearestNode.ComponentId != destComponentId
                    && TaxiGraph.FastDistanceMeters(
                           aircraftLat, aircraftLon,
                           aircraftNearestNode.Latitude, aircraftNearestNode.Longitude)
                       <= UNCHARTED_AIRCRAFT_MAX_SNAP_M)
                {
                    int aircraftComp = aircraftNearestNode.ComponentId;
                    // Start on the aircraft's OWN component: prefer the first cleared
                    // taxiway when it exists on this side, else nearest-in-direction.
                    TaxiNode? crossStart = null;
                    if (firstCleared != null)
                        crossStart = _graph.FindNearestNodeOnTaxiway(
                            aircraftLat, aircraftLon, firstCleared, requiredComponentId: aircraftComp);
                    crossStart ??= _graph.FindNearestNodeInDirection(
                        aircraftLat, aircraftLon, aircraftHeading, requiredComponentId: aircraftComp);
                    crossStart ??= aircraftNearestNode;

                    var crossRouter = new TaxiRouter(_graph);
                    crossingRoute = crossRouter.FindCrossComponentPath(
                        crossStart.NodeId, destinationNodeId, taxiwaySequence,
                        isRunwayDestination, TaxiRouter.UNCHARTED_CROSSING_MAX_M,
                        (la1, lo1, la2, lo2) => _graph.LineIsClearOfRunwayCorridors(la1, lo1, la2, lo2),
                        out crossingGapM);
                    if (crossingRoute != null)
                        crossingStartNodeId = crossStart.NodeId;
                }
            }
            bool isCrossComponentRoute = crossingRoute != null;

            TaxiNode? firstTwNode = null;
            bool attemptLeadIn = false;
            double leadInGap = 0;
            TaxiNode? startNode = null;
            var router = new TaxiRouter(_graph);
            TaxiRoute? route;
            int routeStartNodeId;
            TaxiLeadIn.LeadInInfo leadIn = default;
            bool leadInFallback = false;

            if (isCrossComponentRoute)
            {
                route = crossingRoute;
                routeStartNodeId = crossingStartNodeId;
            }
            else
            {
            firstTwNode = firstCleared != null
                ? SelectFirstTaxiwayEntry(aircraftLat, aircraftLon, firstCleared, destComponentId, destinationNodeId)
                : null;

            if (firstTwNode != null)
            {
                leadInGap = TaxiGraph.FastDistanceMeters(
                    aircraftLat, aircraftLon, firstTwNode.Latitude, firstTwNode.Longitude);
                if (leadInGap > TaxiLeadIn.TriggerMeters)
                {
                    // Task 6 Defect A: this becomes routeStartNodeId below, so a bridge-only stand
                    // stub must never win here even though it now shares destComponentId with
                    // everything else — the component filter alone can't tell it apart post-bridge.
                    var entryNode = _graph.FindNearestNode(
                        aircraftLat, aircraftLon, requiredComponentId: destComponentId,
                        excludeBridgeOnlyStandStubs: true);
                    if (entryNode != null && entryNode.NodeId != firstTwNode.NodeId)
                    {
                        startNode = entryNode;
                        attemptLeadIn = true;
                    }
                    else
                    {
                        startNode = firstTwNode;
                    }
                }
                else
                {
                    startNode = firstTwNode;  // common case: gate on/near its taxiway
                }
            }
            else
            {
                // LANDING-EXIT EARLY HANDOFF: anchor the route START on the exit taxiway itself.
                // The early handoff fires while the aircraft is still ON the runway, possibly abeam a
                // NEIGHBOURING exit; without this anchor the start snaps to that neighbour and A* routes
                // a long hairpin up it and across the parallel taxiway to reach the target (EIDW 28L S6
                // via S5+S, ~600 m). Only when no cleared sequence is given. ExitPathStartAnchor then
                // moves the anchor onto the exit's own PATH where that path carries no node of the exit's
                // name (KCLT 18 D6 → R).
                startNode = null;
                if (!string.IsNullOrEmpty(startTaxiwayName))
                {
                    // Keep to the EXIT'S OWN SIDE of the runway just landed on. A taxiway name can run on
                    // both banks (a crossing taxiway, or a scenery that names a whole side's turnoffs with
                    // one letter), and the nearest node by that name may be across the runway: the route
                    // then drives back over the pavement to reach the exit's vacate point. The re-crossing
                    // guard (HandoffRouteReCrossesLandingRunway) would decline or end that handoff rather
                    // than repair it, and the planned-backtrack handoff is not gated by it at all. Nodes on
                    // the pavement itself always qualify; with no qualifying node the unfiltered pick stands.
                    Func<TaxiNode, bool>? ownSide = ExitOwnSideFilter(landingRolloutRoute, destinationNodeId);
                    startNode = (ownSide != null
                            ? _graph.FindNearestNodeOnTaxiway(
                                aircraftLat, aircraftLon, startTaxiwayName!, requiredComponentId: destComponentId,
                                excludeBridgeOnlyStandStubs: true, accept: ownSide)
                            : null)
                        ?? _graph.FindNearestNodeOnTaxiway(
                            aircraftLat, aircraftLon, startTaxiwayName!, requiredComponentId: destComponentId,
                            excludeBridgeOnlyStandStubs: true);
                    startNode = ExitPathStartAnchor(startNode, exitPathNodeIds, aircraftLat, aircraftLon, destComponentId);
                }
                // Task 6 Defect A: the primary route-start picker. Excludes bridge-only stand
                // stubs — see the entryNode comment above for why the component filter alone
                // cannot.
                startNode ??= _graph.FindNearestNodeInDirection(
                    aircraftLat, aircraftLon, aircraftHeading,
                    requiredComponentId: destComponentId, excludeBridgeOnlyStandStubs: true);
            }
            if (startNode == null)
            {
                // A destination off the network the aircraft is on, with no start node in range: name it,
                // instead of the generic message, and leave the route currently being flown untouched.
                //
                // The rollback below must fire for BOTH non-Unchanged reachability classes, not only
                // DestinationNotConnected -- decided by the one shared LoadRefusalRollback.ShouldRestore
                // predicate all three of this method's refusal sites call (PR #238 review, Important 5),
                // rather than each hand-typing its own copy of the comparison. It protects the SURVIVING
                // route/destination this LoadRoute call was about to overwrite -- state that has nothing
                // to do with which reachability class the NEW (refused) destination fell into. Restricting
                // it to DestinationNotConnected left a LeavingUnconnectedPosition refusal (aircraft on a
                // disconnected position, no main-network node within range) with
                // _destinationNodeId/_isRunwayLineup/_hasLineupTarget still pointing at the just-refused
                // destination while _route stayed the OLD route: the next off-route event then silently
                // re-routed to the refused destination, and a surviving runway route could no longer reach
                // its lineup phase (PR #238 review, Task 7 Defect A). Only DestinationNotConnected gets the
                // NAMED message -- LeavingUnconnectedPosition still falls through to the generic one below,
                // unchanged from before this fix.
                if (LoadRefusalRollback.ShouldRestore(reachability))
                {
                    _guidanceLog.Info($"Reachability: refused dest=\"{destinationName}\" class={reachability} " +
                                      $"no start node ac={aircraftLat:F6},{aircraftLon:F6}");
                    Fail("");   // every field this call overwrote, the graph generation included
                    if (reachability == ReachabilityClass.DestinationNotConnected)
                        return RouteReachabilityMessages.DestinationNotConnected(destinationName);
                }
                return Fail("Could not find a nearby taxiway node.");
            }

            // Calculate route.
            // The node the ROUTE actually starts from. Normally startNode, but the lead-in
            // fallback below rebuilds from firstTwNode — and the holding-point pin re-routes
            // from this node, so it must follow the route, not the original pick.
            routeStartNodeId = startNode.NodeId;

            if (taxiwaySequence != null && taxiwaySequence.Count > 0)
                route = router.FindConstrainedPath(startNode.NodeId, destinationNodeId, taxiwaySequence,
                    destinationIsRunway: isRunwayDestination);
            else
                route = router.FindShortestPath(startNode.NodeId, destinationNodeId);

            // …or one that DETOURS: KSLC 17 K4 — with the online taxiway names loaded, a second node
            // carried "K4", the anchor started there, and the handoff route to K4's own end 135 m
            // off the runway came back as 67 segments / 2,862 m crossing runways 32 and 35, while
            // the tone followed it straight down the runway past the exit (VirtualPilot 2026-09-23).
            // Landing-exit anchor (startTaxiwayName, no clearance): a route whose first leg runs
            // BACK down the runway opens with "Make a U-turn" at rollout speed. KTPA 28 E, drawn
            // down the centreline, snapped to a piece of E 48 m behind the aircraft (VirtualPilot
            // 2026-09-18). Retry from the nearest node of the taxiway AHEAD and keep that route
            // only if it does not start backwards too. The route itself is the test, not the
            // node: an anchor slightly behind whose route peels straight off sideways is fine
            // (KMSP 30R G — overriding those made the route run a centreline stub and cross back).
            // A route that hairpins within its first 150 m is retried the same way: KMSP 30R G n465,
            // anchored on the G drawn across the runway, ran 60 m down the centreline, veered right
            // and turned 162° back across the runway to the exit on the left; the junction itself,
            // 40 m ahead, leaves straight off to the left.
            if (!LegacyExitAnchorForHarness
                && !string.IsNullOrEmpty(startTaxiwayName) && (taxiwaySequence == null || taxiwaySequence.Count == 0)
                && route != null && (RouteStartsBackwards(route, aircraftHeading) || FirstLegPointsBack(route, aircraftHeading)
                                     || RouteHairpinsEarly(route)
                                     || RouteDetoursFromExit(route, aircraftLat, aircraftLon, destinationNodeId)))
            {
                // A detour is judged on length, so every candidate is tried and the SHORTEST
                // acceptable route kept: with the online names loaded KSLC has several "K4" nodes
                // nearer than K4's own junction, and the first-six cut never reached it.
                bool detour = !RouteStartsBackwards(route, aircraftHeading) && !FirstLegPointsBack(route, aircraftHeading)
                              && !RouteHairpinsEarly(route);
                // Candidates, in order: the nodes of the taxiway AHEAD, nearest first (KCVG 18C E:
                // the nearest was the start node itself, drawn on the centreline; the junction
                // 40 m on was never tried), then the route's
                // own nodes over its first 150 m that lie within 60 m AHEAD of the nose — the second
                // covers a start node that IS under the aircraft but whose route runs back down the
                // runway before coming forward (KMSP 04 B: B is drawn twice, on the centreline and
                // 1-4 m left of it; from n354 under the aircraft the route ran 30 m back to reach
                // the left line, then forward along it).
                var candidates = _graph.FindNodesOnTaxiwayAhead(aircraftLat, aircraftLon, startTaxiwayName!,
                    EXIT_ANCHOR_AHEAD_MAX_M, destComponentId, aircraftHeading, EXIT_ANCHOR_MAX_OFF_NOSE_DEG);
                if (!detour && candidates.Count > EXIT_ANCHOR_MAX_CANDIDATES)
                    candidates.RemoveRange(EXIT_ANCHOR_MAX_CANDIDATES, candidates.Count - EXIT_ANCHOR_MAX_CANDIDATES);
                double run = 0;
                foreach (var seg in route.Segments)
                {
                    run += seg.DistanceMeters;
                    if (run > EXIT_ANCHOR_KINK_MAX_RUN_M) break;
                    var n = seg.ToNode;
                    double d = TaxiGraph.FastDistanceMeters(aircraftLat, aircraftLon, n.Latitude, n.Longitude);
                    if (d > EXIT_ANCHOR_KINK_MAX_M) continue;
                    if (d >= 5.0 && Math.Abs(NormalizeAngle(NavigationCalculator.CalculateBearing(
                            aircraftLat, aircraftLon, n.Latitude, n.Longitude) - aircraftHeading)) > 90.0)
                        continue;
                    candidates.Add(n);
                }
                var ownSideRetry = ExitOwnSideFilter(landingRolloutRoute, destinationNodeId);
                foreach (var cand in candidates)
                {
                    if (cand.NodeId == startNode.NodeId || cand.NodeId == destinationNodeId) continue;
                    // Same own-side rule as the anchor above: a retry must not start across the runway either.
                    if (ownSideRetry != null && !ownSideRetry(cand)) continue;
                    var alt = router.FindShortestPath(cand.NodeId, destinationNodeId);
                    // The route must be a real one the aircraft has not already passed the start of:
                    // a one-leg route beside the aircraft reads as ARRIVED on the runway (KABQ 17 F).
                    if (alt != null && alt.Segments.Count > 0 && alt.TotalDistanceMeters >= EXIT_ANCHOR_MIN_ROUTE_M
                        && !AircraftPastFirstLeg(alt, aircraftLat, aircraftLon)
                        && alt.TotalDistanceMeters <= route.TotalDistanceMeters + EXIT_ANCHOR_MAX_EXTRA_M
                        && !RouteHairpinsEarly(alt)
                        && !RouteStartsBackwards(alt, aircraftHeading)
                        && !FirstLegPointsBack(alt, aircraftHeading))
                    {
                        // A detour is only replaced by a route that is not one itself: at KSLC 35 K4 a
                        // marginally shorter 2.5 km detour with a 90° first leg was taken and tipped the
                        // handoff into a U-turn call; with nothing better, the original route stands.
                        if (detour && (alt.TotalDistanceMeters >= route.TotalDistanceMeters
                                       || RouteDetoursFromExit(alt, aircraftLat, aircraftLon, destinationNodeId))) continue;
                        route = alt;
                        startNode = cand;
                        routeStartNodeId = cand.NodeId;
                        if (!detour) break;
                    }
                }
            }

            // Lead-in acceptance. If we started from the aircraft's nearest node to
            // get a pavement lead-in, accept it only when the router honoured the
            // clearance AND the lead-in is not a dead-end detour. Otherwise rebuild
            // from the on-taxiway node (today's behaviour) and note it in the
            // summary so the pilot knows the lead-in wasn't computed.
            if (attemptLeadIn)
            {
                bool accepted = false;
                if (route is { Segments.Count: > 0 })
                {
                    leadIn = TaxiLeadIn.Extract(route, firstCleared!);
                    accepted = TaxiLeadIn.IsAcceptable(
                        leadIn.DistanceMeters, leadInGap, route.ConstrainedFallbackReason);
                }
                if (!accepted)
                {
                    // Couldn't build a sensible lead-in (route empty, router fell back,
                    // or the lead-in was a dead-end detour) — start on the cleared
                    // taxiway like before and note it in the summary.
                    route = router.FindConstrainedPath(
                        firstTwNode!.NodeId, destinationNodeId, taxiwaySequence!,
                        destinationIsRunway: isRunwayDestination);
                    leadIn = default;
                    leadInFallback = true;
                    routeStartNodeId = firstTwNode!.NodeId;
                }
            }
            } // end !isCrossComponentRoute (legacy same-component routing)

            if (route == null || route.Segments.Count == 0)
            {
                // A destination off the network the aircraft is on with no buildable route: the same
                // rollback as the no-start-node case above, decided by the SAME LoadRefusalRollback
                // predicate (PR #238 review, Important 5) for the same reason and for BOTH non-Unchanged
                // reachability classes (PR #238 review, Task 7 Defect A) -- only DestinationNotConnected
                // gets the named refusal; LeavingUnconnectedPosition still falls through to the generic
                // message below, unchanged from before this fix.
                if (LoadRefusalRollback.ShouldRestore(reachability))
                {
                    _guidanceLog.Info($"Reachability: refused dest=\"{destinationName}\" class={reachability} " +
                                      $"no route ac={aircraftLat:F6},{aircraftLon:F6}");
                    Fail("");   // every field this call overwrote, the graph generation included
                    if (reachability == ReachabilityClass.DestinationNotConnected)
                        return RouteReachabilityMessages.DestinationNotConnected(destinationName);
                }
                return Fail("Could not calculate a route to the destination.");
            }

            // The aircraft is not on the destination's piece of network, so the route starts with a
            // straight unmapped leg from the aircraft to its first node. Refuse when that leg touches
            // runway pavement; otherwise compose the warning the pilot hears when guidance starts.
            //
            // Guarded by RouteReachability.IsOffDestinationNetwork, NOT LoadRefusalRollback
            // .ShouldRestore (PR #238 review, Minor D re-fix). This guard answers "is the
            // aircraft off the destination's network at all" -- whether the first leg needs
            // checking -- which is a different question from ShouldRestore's "must a refusal
            // roll back state LoadRoute already overwrote." The two share the exact same
            // formula (`!= Unchanged`) today, which is why reusing ShouldRestore here read as
            // harmless and changed nothing behaviourally -- but it coupled two unrelated
            // decisions to one predicate for no reason beyond removing a duplicate, exactly
            // what Minor D flagged. The RestoreLoadRouteRollback call a few lines below, inside
            // the firstLeg.CrossesRunway branch, is the genuine "must roll back" decision at
            // this site, and it needs no separate ShouldRestore guard of its own: being inside
            // this IsOffDestinationNetwork block already guarantees ShouldRestore would agree
            // (both predicates read the same reachability value, off Unchanged), so calling it
            // there would only re-hand-type a fourth copy of the same comparison.
            string? unmappedStartWarning = null;
            // Not for an uncharted apron crossing: that route ALREADY leaves the aircraft's network on
            // purpose, over a leg checked clear of every runway, and announces itself (routeStartWarning
            // below) — a second "is not connected" warning would contradict it.
            if (!isCrossComponentRoute && RouteReachability.IsOffDestinationNetwork(reachability))
            {
                var firstLeg = RouteReachability.CheckFirstLeg(
                    _graph, aircraftLat, aircraftLon, route.Segments[0].FromNode);
                bool destinationOffNetwork = reachability == ReachabilityClass.DestinationNotConnected;
                if (firstLeg.CrossesRunway)
                {
                    string loadRunwayLog = string.IsNullOrEmpty(firstLeg.RunwayDesignator) ? "(unnamed)" : firstLeg.RunwayDesignator;
                    _guidanceLog.Info($"Reachability: refused dest=\"{destinationName}\" class={reachability} " +
                                      $"first leg crosses runway {loadRunwayLog} gapM={firstLeg.GapMeters:F0} " +
                                      $"ac={aircraftLat:F6},{aircraftLon:F6}");
                    // A refused Calculate mid-taxi must leave the route currently being flown untouched
                    // (see the capture above).
                    Fail("");   // every field this call overwrote, the graph generation included
                    // A touched runway with no designator (both ends unnamed) must never speak a
                    // sentence with a hole where the runway name belongs.
                    return string.IsNullOrEmpty(firstLeg.RunwayDesignator)
                        ? RouteReachabilityMessages.CrossesUnnamedRunway()
                        : destinationOffNetwork
                            ? RouteReachabilityMessages.DestinationLegCrossesRunway(destinationName, firstLeg.RunwayDesignator)
                            : RouteReachabilityMessages.FirstLegCrossesRunway(firstLeg.RunwayDesignator);
                }
                if (destinationOffNetwork)
                {
                    unmappedStartWarning = RouteReachabilityMessages.UnmappedLegToDestination(
                        destinationName, firstLeg.GapMeters, FormatDistance);
                    // A same-numbered stand on the AIRCRAFT's side of the network is almost always the
                    // stand ATC actually meant (LMML: three ramps share plain stand numbers) — name it.
                    if (!isRunwayDestination && icao != null
                        && _graph.FindNearestNode(aircraftLat, aircraftLon) is { } aircraftNode
                        && DescribeConnectedAlternateStand(dataProvider, icao, destinationName,
                               aircraftNode.ComponentId, aircraftLat, aircraftLon) is string alternate)
                        unmappedStartWarning += alternate;
                    _guidanceLog.Info($"Reachability: destination not connected dest=\"{destinationName}\" " +
                                      $"class={reachability} gapM={firstLeg.GapMeters:F0}");
                }
                else
                {
                    string? firstNamedTaxiway = route.Segments
                        .FirstOrDefault(s => !string.IsNullOrEmpty(s.TaxiwayName))?.TaxiwayName;
                    unmappedStartWarning = RouteReachabilityMessages.UnmappedFirstLeg(
                        firstLeg.GapMeters, FormatDistance, firstNamedTaxiway);
                    _guidanceLog.Info($"Reachability: leaving unconnected position dest=\"{destinationName}\" " +
                                      $"class={reachability} gapM={firstLeg.GapMeters:F0} firstTaxiway=\"{firstNamedTaxiway}\"");
                }
            }

            string? constrainedLengthWarning = null;

            route.DestinationName = destinationName;
            if (taxiwaySequence != null)
                route.TaxiwaySequence = taxiwaySequence;

            // The pilot's own hold-short picks (end-of-taxiway, runway and taxiway flavours) are
            // applied BELOW, after the two passes that can REPLACE the route object: the named
            // holding-point pin and the forward-facing runway-entry re-route. Both build a fresh
            // TaxiRoute, so a pick placed before them was silently dropped while the event it
            // recorded still reached the summary — the pilot was told about a hold that was not
            // there (feat/taxi-landing-port review, findings 4 and 5).

            // Pin the route THROUGH the painted hold line's own node, not just its runway ENTRY:
            // the corridor to the entry is a free A* choice, and where two stubs merge short of
            // the runway BOTH painted points resolve to the SAME entry — EGLL 27R, picked A2,
            // taxied and held at A3.
            // (Skipped for a cross-component route: the pin re-routes start→destination
            // through one router call, which cannot span the island gap — and a named
            // holding point on the far side of an uncharted crossing is not a real case.)
            bool holdingPointPinRejected = false;
            if (_holdingPointHoldNodeId != 0 && !isCrossComponentRoute)
            {
                var pinned = ApplyHoldingPointPin(router, route, routeStartNodeId, destinationNodeId, taxiwaySequence,
                    out holdingPointPinRejected);
                if (pinned != null)
                {
                    route = pinned;
                    if (attemptLeadIn && !leadInFallback && firstCleared != null)
                        leadIn = TaxiLeadIn.Extract(route, firstCleared);
                }
            }

            // The picked holding point could not be routed through: say so, or the pilot who asked
            // for P8 is held at another line with nothing to tell them (EDDF 18 via P8: a 5.4 km pin
            // against an 854 m route, rejected in the log only — VirtualPilot 2026-09-18).
            string? runwayHoldShortWarning = holdingPointPinRejected && _holdingPointName.Length > 0
                ? $"Holding point {_holdingPointName} is not on a usable route to {destinationName}. Holding at the runway entry instead."
                : null;

            // A route that arrives on its runway facing the WRONG WAY (EGLL 09R via NB10's 27L curve)
            // is re-routed to where the same clearance meets the runway facing the takeoff direction,
            // and the pilot is told how far down the runway that is. Not for a backtrack (which
            // turns round by design), a named holding point (the pilot picked that stub), or a
            // cross-component route (one router call cannot span the gap).
            string? reversedEntryNote = null;
            if (isRunwayDestination && !fullLengthBacktrack && !intersectionDeparture && _holdingPointHoldNodeId == 0 && !isCrossComponentRoute
                && !LegacyReversedEntryForHarness)
            {
                var fwd = TryForwardFacingRunwayEntry(router, route, routeStartNodeId, destinationNodeId,
                    taxiwaySequence, destinationName);
                if (fwd is { } f)
                {
                    _guidanceLog.Info(
                        $"Reversed runway entry: node {destinationNodeId} arrives facing away from {destinationName}; " +
                        $"re-routed to node {f.NodeId} ({f.AlongM:0} m from the lineup point, route {route.TotalDistanceMeters:0} -> {f.Route.TotalDistanceMeters:0} m)");
                    route = f.Route;
                    route.DestinationName = destinationName;
                    if (taxiwaySequence != null) route.TaxiwaySequence = taxiwaySequence;
                    destinationNodeId = f.NodeId;
                    _destinationNodeId = f.NodeId;
                    if (attemptLeadIn && !leadInFallback && firstCleared != null)
                        leadIn = TaxiLeadIn.Extract(route, firstCleared);
                    string via = route.Segments.LastOrDefault(sg => !string.IsNullOrEmpty(sg.TaxiwayName))?.TaxiwayName ?? "";
                    // Behind the lineup point is a displaced threshold's pavement (KABQ 08, 317 m):
                    // the runway end the pilot lines up from, with MORE runway than the start row.
                    string where = f.AlongM > 20
                        ? $"about {DistanceFormatter.FromMetres(f.AlongM)} down the runway"
                        : f.AlongM < -20
                            ? $"at the runway end, about {DistanceFormatter.FromMetres(-f.AlongM)} before the usual start point"
                            : "at the start of the runway";
                    reversedEntryNote = via.Length > 0
                        ? $"Joining {destinationName} where {via} faces the takeoff direction, {where}."
                        : $"Joining {destinationName} facing the takeoff direction, {where}.";
                }
            }

            // The pilot's own picks, applied to the route object that will actually be ADOPTED.
            // Both passes above (the holding-point pin, the forward-facing re-route) return a fresh
            // TaxiRoute and the picks mutate segments in place, so they must come after. Main ran
            // the pin before the picks for this reason; the branch inverted that, and the
            // forward-facing re-route is new, so both orderings are restored here.

            // Apply user-requested hold-short points at taxiway transitions
            if (userHoldShortIndices != null && userHoldShortIndices.Count > 0 && taxiwaySequence != null)
            {
                ApplyUserHoldShorts(route, taxiwaySequence, userHoldShortIndices);
            }

            // Apply user-requested runway hold-shorts (per-row "Hold short of
            // runway X" pickers in the form). Runs BEFORE the automatic pass: when
            // that pass resolves the same runway to a stop a pick already took, the
            // pilot's label is kept, and another runway held at that stop is added
            // to it (RouteRunwayCrossings.ComposeSharedLabel). If the route neither
            // enters nor crosses the picked runway at or after the chosen taxiway,
            // we collect a warning to announce alongside the route summary so the
            // pilot knows their explicit pick was a clearance/route mismatch. A pick
            // that meets its runway but finds no stop YET is retried after the
            // automatic pass (RetryDeferredRunwayPicks).
            var deferredRunwayPicks = new List<DeferredRunwayPick>();
            // Each honoured pick's own recorded event, merged back in by AdoptRoute after the
            // automatic pass has reset the list (PR #238 deferred finding §7).
            var userPickEvents = new List<TaxiRouteRunwayEvent>();
            if (userRunwayHoldShorts != null && userRunwayHoldShorts.Count > 0 && taxiwaySequence != null)
            {
                string? pickWarning = ApplyUserRunwayHoldShorts(
                    route, taxiwaySequence, userRunwayHoldShorts,
                    aircraftLat, aircraftLon, userPickEvents, deferredRunwayPicks);
                if (pickWarning != null)
                    runwayHoldShortWarning = runwayHoldShortWarning == null
                        ? pickWarning : runwayHoldShortWarning + " " + pickWarning;
            }

            // Taxiway-flavored picks from the same combos: stop at the first
            // junction with the named taxiway after the chosen row. Warnings from
            // both flavors fold into the one note the summary carries.
            if (userTaxiwayHoldShorts != null && userTaxiwayHoldShorts.Count > 0 && taxiwaySequence != null)
            {
                string? taxiwayHoldWarning = ApplyUserTaxiwayHoldShorts(
                    route, taxiwaySequence, userTaxiwayHoldShorts);
                if (taxiwayHoldWarning != null)
                    runwayHoldShortWarning = runwayHoldShortWarning == null
                        ? taxiwayHoldWarning
                        : runwayHoldShortWarning + " " + taxiwayHoldWarning;
            }

            // Capture the FULL constrained-route length BEFORE TruncateToHoldShort
            // trims the tail. The length advisory below must judge the clearance on
            // the full route, not the truncated one: a clearance that doubles back —
            // cleared taxiways that lead AWAY from the destination, forcing the route
            // to loop back (EHAM 18L via A12, B, N2, cross 27, E6, 2026-06-20: N2/E6
            // sit north of runway 27, the 18L lineup is south, so the route went over
            // 27 and reversed) — has that backtrack TRIMMED off by truncation, which
            // previously hid the detour from the advisory and routed the pilot in a
            // silent loop. Truncation only ever SHORTENS, so the full length is the
            // honest "is this clearance sane?" measure.
            double fullRouteMeters = route.TotalDistanceMeters;

            // Did the route actually REACH the runway before TruncateToHoldShort cut it back?
            // TaxiRouter deliberately ends a runway route on the LAST CLEARED TAXIWAY when that
            // taxiway does not connect to the destination node (`lastTaxiwayTerminal`, two sites,
            // runway destinations only) — that is what honours a cleared taxiway instead of
            // bypassing it (EIDW N2, LFPG R1). The route then ends nowhere near
            // `destinationNodeId`, which is never reassigned, so the destination-node reach probe
            // below cannot see it. That is the real PHNL 04L failure: the clearance ended on a
            // taxiway that only parallels 04L, guidance held ~456 m off behind a legitimate-looking
            // "Hold short of Runway 04L", and the lineup tone panned for four minutes.
            // Captured BEFORE truncation, because truncation legitimately moves the end back to a
            // hold line on a route that DID reach the runway — the LPPT 02 case that made the
            // reach probe move to the destination node in the first place, and took this
            // protection with it.
            bool routeReachedDestination = route.Segments.Count > 0 &&
                route.Segments.Any(s => s.ToNode != null && s.ToNode.NodeId == destinationNodeId);
            var preTruncationEndNode = route.Segments.Count > 0 ? route.Segments[^1].ToNode : null;

            // Critical safety fix: when the destination is a runway, the route MUST
            // end at the hold-short line — not at the threshold itself. Otherwise
            // HandleArrival fires only when the aircraft is within the 30 m arrival
            // radius of the threshold, which is often PAST the hold-short markings
            // (real hold-short lines sit ~150-200 ft / 46-61 m back from the threshold).
            if (isRunwayDestination)
                TruncateToHoldShort(route, destinationName, preferIlsHold);

            // The auto hold-shorts for INTERMEDIATE runway crossings (FAA AIM 4-3-18 &
            // ICAO Doc 4444) are applied by AdoptRoute below, at the moment this route
            // becomes the live one — not here. See that method.

            // Runway-reach safety check. Probe the route's DESTINATION node — the
            // node nearest the runway lineup point that the route reaches — NOT
            // the truncated hold-short (route.Segments[^1].ToNode). A hold-short
            // does not necessarily sit on the centerline extended: an ILS hold
            // (IHSND) or a hold on an angled connector legitimately sits far off
            // the perpendicular, so measuring it false-fired "does not reach the
            // runway" on reachable runways (LPPT 02 2026-06-16: ILS hold 151 m
            // off, destination node 6.8 m off, runway reachable). When the
            // destination node itself is well off to the side, the entered
            // clearance ended on a taxiway that only PARALLELS the runway, with
            // no connector — guidance would hold short there and try to line up
            // on a runway it has no path to (PHNL 04L 2026-06-13: lineup tone
            // panned for 4 minutes). Warn the pilot up front so they reprogram.
            // The route still loads — ATC routings and odd navdata exist.
            // Progressive routes pass isRunwayDestination:false + no lineup target,
            // so this check is inert for them (they never line up on a runway).
            // Both halves of the rule live in RunwayReachGate so this and TryRecalculateRoute
            // cannot drift apart again (they had already diverged on the no-route-end case).
            // The outer guard stays here so a gate destination never pays for
            // DestinationCrossTrackMeters.
            string? runwayReachWarning = null;
            _routeReachesRunway = true;
            if (isRunwayDestination && _hasLineupTarget)
            {
                var reach = RunwayReachGate.Evaluate(
                    isRunwayDestination: true,
                    DestinationCrossTrackMeters(destinationNodeId), RUNWAY_REACH_MAX_CROSS_M,
                    routeReachedDestination,
                    preTruncationEndNode != null &&
                        RouteEndIsRunwayHold(preTruncationEndNode, destinationName),
                    preTruncationEndNode != null,
                    () => RouteEndWalkToRunwayMeters(preTruncationEndNode!, destinationName),
                    RUNWAY_REACH_MAX_WALK_M);

                _routeReachesRunway = reach.Verdict == RunwayReachVerdict.Reaches;
                runwayReachWarning = RunwayReachGate.DescribeFailure(
                    reach, destinationName, FormatDistance);
            }

            // Constrained-route sanity advisory: compare against the
            // unconstrained shortest path from the aircraft's natural start
            // node. Fires only for user-sequenced routes that built fully
            // (a fallback route is already announced via its fallback reason).
            // Uses fullRouteMeters (the PRE-truncation length): a doubling-back
            // clearance has its backtrack trimmed by TruncateToHoldShort, so
            // comparing the truncated total let an obvious detour slip under the
            // 2x+500 m trigger (the EHAM 18L loop above — the truncated 852 m sat
            // below the threshold while the full backtrack was ~1.6 km). The
            // advisory quotes the same fullRouteMeters so the warning is internally
            // consistent; for a normal route fullRouteMeters is within ~60 m of the
            // summary total (the hold-short trim), far inside the 500 m pad, so this
            // never adds a false positive — it only catches genuine backtracks.
            // (Not for cross-component routes: there is no in-component "direct route"
            // to compare against — the whole point of the crossing is that none exists.)
            if (!isCrossComponentRoute &&
                taxiwaySequence is { Count: > 0 } &&
                string.IsNullOrEmpty(route.ConstrainedFallbackReason))
            {
                // Task 6 Defect A: this feeds a real router.FindShortestPath call below, so it is
                // a route start like any other, not just a display value.
                var directStart = _graph.FindNearestNodeInDirection(
                    aircraftLat, aircraftLon, aircraftHeading,
                    requiredComponentId: destComponentId, excludeBridgeOnlyStandStubs: true) ?? startNode;
                var direct = router.FindShortestPath(directStart.NodeId, destinationNodeId);
                if (direct != null && direct.Segments.Count > 0 &&
                    fullRouteMeters >
                        direct.TotalDistanceMeters * CONSTRAINED_WARN_RATIO + CONSTRAINED_WARN_PAD_M)
                {
                    // Measure to the first cleared taxiway itself (firstTwNode), not
                    // to startNode — in the lead-in path startNode is the nearby apron
                    // node, which would wrongly read as "0 m" here.
                    double firstTwDist = TaxiGraph.FastDistanceMeters(
                        aircraftLat, aircraftLon,
                        (firstTwNode ?? startNode!).Latitude, (firstTwNode ?? startNode!).Longitude);
                    string firstTwNote = firstTwDist > CONSTRAINED_WARN_FIRST_TW_M
                        ? $" Taxiway {taxiwaySequence[0]} is {FormatDistance(firstTwDist)} from your position."
                        : "";
                    constrainedLengthWarning =
                        $"Warning: route via {string.Join(", ", taxiwaySequence)} is " +
                        $"{FormatDistance(fullRouteMeters)}; direct route is " +
                        $"{FormatDistance(direct.TotalDistanceMeters)}.{firstTwNote} Check taxiway selection.";
                }
            }

            // Route-start sanity for the cases RouteReachability (the unmapped-first-leg refusal and
            // warning above) and RouteStartTurnCue (the turn-around cue) do not cover:
            //   - a route that includes an UNCHARTED APRON CROSSING (TaxiRouter.FindCrossComponentPath):
            //     even though the synthetic crossing makes the destination reachable, the pilot must
            //     hear that it is not on their connected network — it may well be the wrong pick for
            //     the clearance (three ramps share plain numbers at LMML), and the route includes a leg
            //     over pavement the data knows nothing about;
            //   - a same-network route that begins far from the aircraft (RouteStartGate.StartsFarAway).
            // Gated on announceSummary: the landing-exit/rollout LoadRoute sites legitimately build
            // routes ahead of a fast-moving aircraft and suppress the summary.
            string? routeStartWarning = null;
            if (isCrossComponentRoute)
            {
                // Say WHICH side is cut off. Departing from a stand stub that stops short of the taxiway
                // (OMDB B 18, 10 m) this used to tell the pilot the RUNWAY "is on a section of the taxiway
                // network that is not connected to your position" (PR #235 review, 2026-09-15).
                routeStartWarning = AircraftIsOnTheSmallerIsland(aircraftLat, aircraftLon, destComponentId)
                    ? "Note: your position is on a section of the taxiway network that is not " +
                      $"connected to the rest. This route includes a {FormatDistance(crossingGapM)} " +
                      "crossing of uncharted apron with no taxiway data."
                    : $"Note: {destinationName} is on a section of the taxiway network that is " +
                      "not connected to your position. This route includes a " +
                      $"{FormatDistance(crossingGapM)} crossing of uncharted apron with no " +
                      "taxiway data.";
            }
            else if (announceSummary && route.Segments.Count > 0
                     && !RouteReachability.IsOffDestinationNetwork(reachability))
            {
                var firstRouteNode = route.Segments[0].FromNode;
                double startGapM = TaxiGraph.FastDistanceMeters(
                    aircraftLat, aircraftLon, firstRouteNode.Latitude, firstRouteNode.Longitude);
                if (startGapM > RouteStartGate.FAR_START_WARN_M)
                    routeStartWarning =
                        $"Warning: the route begins {FormatDistance(startGapM)} from your position.";
            }

            AdoptRoute(
                route, isRunwayDestination, destinationName,
                aircraftLat, aircraftLon, phase: landingRolloutRoute ? "touchdown" : "load",
                userPickEvents: userPickEvents);
            _loadedRouteStartNodeId = route.Segments.Count > 0 && route.Segments[0].FromNode != null
                ? route.Segments[0].FromNode.NodeId : -1;

            // Picks that met their runway with no stop before the automatic pass (AdoptRoute) may share
            // the stop it has just placed for a runway in front of theirs; only then is a pick that
            // still has no stop warned about.
            string? deferredPickWarning = RetryDeferredRunwayPicks(
                route, deferredRunwayPicks, aircraftLat, aircraftLon);
            if (deferredPickWarning != null)
                runwayHoldShortWarning = runwayHoldShortWarning == null
                    ? deferredPickWarning : runwayHoldShortWarning + " " + deferredPickWarning;

            // Cleared on every LoadRoute, set or not, so it can never describe a previous
            // route (the taxi-route import calculates several times per import).
            LastRouteHoldShortWarning = runwayHoldShortWarning;
            _currentSegmentIndex = 0;
            // Cleared for every fresh route; BeginLandingRollout / RetargetLandingExit
            // re-set it true when this is a Landing Exit Planner route.
            _isLandingExitRoute = false;
            ResetLandingExitOutcomeFlags();   // a new route re-decides these at its own handoff
            _arrivedRunwayClearing = false;   // a fresh route supersedes any clearing phase
            _approachAnnounced = false;
            _curveAnnouncedSign = 0;
            _turnImminentAnnounced = false;
            _crossingAnnounced = false;
            _lastCrossingNodeId = -1;
            _lastAnnouncedTaxiway = "";
            // A deferred taxiway-change name from the PREVIOUS route must never survive
            // into this one -- it names a segment on a route that no longer exists, so
            // FlushPendingTaxiwayAnnouncement's "is it still current" check could
            // otherwise pass by coincidence against the new route's own segment.
            _pendingTaxiwayAnnouncement = null;
            _headingErrorInitialized = false;
            _initialTurnCueAnnounced = false;
            _initialTurnPending = false;
            // Composed here, not on the first taxiing frame, so the form can fold it into
            // its single standstill utterance instead of interrupting it 50 ms later —
            // which is the defect this cue was repaired for (live KATL 2026-08-27: the cue
            // fired as an AnnounceImmediate ~50 ms after the import summary and cut it off
            // mid-word).
            //
            // The angle MUST be the same quantity the steering tone pans on, or the spoken
            // "left"/"right" can contradict the pan and a blind pilot has nothing to break
            // the tie. That is enforced structurally, not by similarity: this calls the very
            // method the per-frame tone site calls (ComputeSteeringHeadingError), against
            // the route and segment cursor just assigned above (AdoptRoute,
            // _currentSegmentIndex = 0) — so it reads the look-ahead walk target the tone
            // will read on its first frame, degenerate-segment guard and all. Do not
            // "simplify" this back to route.Segments[0].BearingDegrees: that is a different
            // number (35° apart on the live KATL route) and the walk exists precisely
            // because raw navdata segment bearings are unrepresentative.
            //
            // BOTH sides are TRUE north. The walk target's bearing is geodetic true, so the
            // aircraft heading is converted with aircraftHeadingMagVar (0 for the callers
            // that already pass a true heading). See that parameter's comment for why a
            // magnetic heading here can invert the spoken direction against the tone.
            //
            // The taxiway comes from the ROUTE (the first named leg). Note this is NOT what
            // made the live cue say a bare "Make a U-turn to the left": the old cue read
            // _lastAnnouncedTaxiway, which LoadRoute blanks but StartGuidance re-sets from
            // an identical first-named-segment walk before the first taxiing frame, so on
            // the form's Calculate path the old cue would have named the taxiway too.
            // Naming from the route is a robustness improvement for the paths that run
            // LoadRoute WITHOUT StartGuidance — the three Rollout re-routes and
            // LandingExitPlanner — where _lastAnnouncedTaxiway really is still empty.
            ComposeInitialTurnCue(aircraftLat, aircraftLon, aircraftHeading + aircraftHeadingMagVar);
            // Reset the tone slew-limiter baseline too. LoadRoute is only ever a
            // FRESH route (the form's Calculate path doesn't call StopGuidance
            // first, and recalcs swap the route in place via TryRecalculateRoute
            // WITHOUT going through LoadRoute) — so a fresh route must snap to its
            // first target on frame one rather than sweep from the prior route's
            // stale value. Recalc-softening is unaffected: recalcs never reach here.
            _toneErrorInitialized = false;
            _smoothedHeadingError = 0;
            ResetIncursionNodeMemory();
            _holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;
            _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
            // Reset lineup/cooldown state so an ATC-amendment reload mid-taxi doesn't
            // inherit stale values from the prior route (e.g., "aligned" carrying over
            // would suppress the first alignment announcement on the new lineup target).
            _lineupAnnouncedAligned = false;
            _lineupStubActive = false;
            _lineupHugeCrossTrackSince = DateTime.MinValue;
            _runwayLineupUnreachableWarned = false;
            _lastRecalculationTime = DateTime.MinValue;
            _lastSpeedWarningTime = DateTime.MinValue;
            _lastIncursionWarningTime = DateTime.MinValue;
            _offRouteSince = DateTime.MinValue;
            _hasJoinedRoute = false;
            _minPerpWhileUnjoinedM = double.MaxValue;
            _lastSegmentAdvanceTime = DateTime.MinValue;
            _holdShortAtDestination = false;
            _unchartedAnnouncedIdx = -1;
            _pairedCrossingSegIdx = -1;
            _pairedCrossingWindowUntil = DateTime.MinValue;

            // Append a session-start header + CSV column row to the diagnostic
            // frame trace. Each "=== Guidance ... ===" line acts as a session
            // separator so a post-flight reader can split on it, and a buggy
            // session's trace survives a subsequent route load. Size-capped
            // rotation (so the file never grows without bound over months of
            // use) is now handled by the shared LogWriter rather than a
            // hand-rolled per-LoadRoute truncate.
            try
            {
                _guidanceLog.Info($"=== Guidance icao={_icao} dest={_destinationName} segments={route.Segments.Count} totalM={route.TotalDistanceMeters:F0} ===");
                _guidanceLog.Info("lat,lon,hdg,gs,seg,segBrg,w,nxtTurn,tLat,tLon,raw,smooth");
            }
            catch { /* diagnostic only */ }
            _lastGuidanceLogTime = DateTime.MinValue;

            SetState(TaxiGuidanceState.RouteLoaded);

            // Always build the summary so the form can show it in its
            // read-only display box, even when announceSummary=false (e.g.
            // landing-exit auto-activation suppresses the spoken callout to
            // avoid stepping on rollout instructions, but the form still
            // wants the text).
            {
                string summary = BuildRouteSummary(route, isRunwayDestination, leadIn, firstCleared);
                if (!string.IsNullOrEmpty(runwayHoldShortWarning))
                    summary = summary + " " + runwayHoldShortWarning;
                if (!string.IsNullOrEmpty(reversedEntryNote))
                    summary = reversedEntryNote + " " + summary;
                // LVP hold feedback: when the CAT III / low-visibility hold was
                // requested, say which hold line the route actually stops at —
                // navdata hold coverage is patchy and the same-approach gate can
                // legitimately reject the ILS hold, and a blind pilot has no
                // other way to know. The honoured confirmation rides at the
                // tail; a fallback is warning-like and goes FIRST (same
                // interrupt reasoning as the length advisory below), and is also
                // one of the warnings that must be HEARD (LastRouteStartWarning).
                string? lvpNote = RunwayHoldShortSelector.DescribeLvpOutcome(
                    isRunwayDestination && preferIlsHold, _lastRunwayHoldChoice);
                bool lvpFellBack = lvpNote != null && _lastRunwayHoldChoice != RunwayHoldChoice.IlsHold;
                if (lvpNote != null)
                    summary = lvpFellBack ? lvpNote + " " + summary : summary + " " + lvpNote;
                // The length advisory goes FIRST, not last. The summary is plain
                // queued speech, and the first AnnounceImmediate tactical callout
                // after the pilot starts rolling INTERRUPTS it — a warning at the
                // tail of a long summary never gets heard. KATL 2026-06-11 "via V":
                // a 7,073 m tour (direct 1.1 km) taxied for 7 minutes with the
                // warning almost certainly cut off before it played.
                if (!string.IsNullOrEmpty(constrainedLengthWarning))
                    summary = constrainedLengthWarning + " " + summary;
                // If a pavement lead-in onto the first cleared taxiway was attempted
                // but couldn't be built (out-of-component / dead-end), the route fell
                // back to starting on that taxiway. Prepend a notice so it is heard
                // before the first tactical callout can interrupt the queued summary.
                if (leadInFallback && firstCleared != null)
                    summary = $"Could not compute a path onto taxiway {firstCleared} " +
                              $"along the apron; route starts on {firstCleared}. " + summary;
                // A refused runway-as-taxiway leg is spoken near the front for the
                // same interruption reason: the pilot must hear WHICH cleared leg
                // guidance cannot honour before tactical callouts start.
                if (!string.IsNullOrEmpty(runwayLegNote))
                    summary = runwayLegNote + " " + summary;
                // Route-start warning goes FIRST of all (prepended last): a route the
                // aircraft cannot join (island) or must turn around for is the single
                // most important thing to hear, and the first tactical callout after
                // the pilot starts rolling interrupts whatever hasn't played yet.
                if (!string.IsNullOrEmpty(routeStartWarning))
                    summary = routeStartWarning + " " + summary;
                // The runway-reach warning ("route does not reach Runway X") is
                // safety-critical and MUST be heard at calculate time. Speaking it
                // here (even via AnnounceImmediate) doesn't work: the caller fires
                // StartGuidance immediately after LoadRoute, whose first-taxiway
                // callout stomps it — confirmed in-sim 2026-06-13 (the pilot saw it
                // in the box and heard "calculating"/the taxiway, but never the
                // warning). So we DON'T speak it here; we expose it via
                // LastRouteReachWarning and let the form announce it AFTER
                // StartGuidance, as the final standstill announcement. It's still
                // prepended to the box text for re-reading.
                string boxText = string.IsNullOrEmpty(runwayReachWarning)
                    ? summary
                    : runwayReachWarning + " " + summary;
                // The unmapped-start warning leads the box so it can be re-read. It is SPOKEN once, by
                // the form's standstill utterance or the first-frame one-shot (ConsumeUnmappedStartWarning).
                if (unmappedStartWarning != null)
                    boxText = unmappedStartWarning + " " + boxText;
                LastRouteSummary = boxText;
                // The warnings that must be heard, for the caller's standstill utterance (see
                // LastRouteStartWarning): the summary they also ride in is cut by StartGuidance.
                var mustHear = new List<string>();
                if (!string.IsNullOrEmpty(routeStartWarning)) mustHear.Add(routeStartWarning);
                string unheldRunways = RouteRunwayCrossings.DescribeUnheldRunways(route.RunwayEvents);
                if (unheldRunways.Length > 0)
                    mustHear.Add("Warning: " + unheldRunways + ".");
                if (!string.IsNullOrEmpty(runwayHoldShortWarning)) mustHear.Add(runwayHoldShortWarning);
                if (lvpFellBack) mustHear.Add(lvpNote!);
                LastRouteStartWarning = mustHear.Count > 0 ? string.Join(" ", mustHear) : null;
                LastRouteUnmappedStartWarning = unmappedStartWarning;
                // SPOKEN warning is a short one-liner (~5 s) so it's heard before
                // the first tactical callout can interrupt it; the full detail
                // (distance off, "missing connector") stays in the box above for
                // re-reading. A 3-sentence spoken warning got cut by "Crossing
                // taxiway G" at guidance start (2026-06-13).
                LastRouteReachWarning = string.IsNullOrEmpty(runwayReachWarning)
                    ? null
                    : $"Warning: this route does not reach {destinationName}. " +
                      "Check your taxiway entry and reprogram.";
                // For a route that doesn't reach its runway, skip the SPOKEN
                // summary (it still shows in the box) — the warning is the
                // message, and the summary would just pile onto the start-of-
                // guidance speech. Normal routes announce the summary as usual.
                if (announceSummary && LastRouteReachWarning == null)
                    QueueSpeech(summary);
            }

            return null;
        }
        catch (Exception ex)
        {
            return Fail($"Error calculating route: {ex.Message}");
        }
        } // end lock(_stateLock)
    }

    /// <summary>
    /// Whether the aircraft stands on a piece of the taxi network with fewer nodes than the destination's —
    /// i.e. it is the aircraft, not the destination, that is cut off (a stand stub short of its taxiway).
    /// </summary>
    private bool AircraftIsOnTheSmallerIsland(double aircraftLat, double aircraftLon, int destComponentId)
    {
        if (_graph == null) return false;
        var near = _graph.FindNearestNode(aircraftLat, aircraftLon);
        if (near == null || near.ComponentId == destComponentId) return false;
        int aircraftNodes = 0, destNodes = 0;
        foreach (var n in _graph.Nodes.Values)
        {
            if (n.ComponentId == near.ComponentId) aircraftNodes++;
            else if (n.ComponentId == destComponentId) destNodes++;
        }
        return aircraftNodes < destNodes;
    }

    /// <summary>
    /// For the DisconnectedIsland route-start warning: finds the nearest stand that
    /// shares the destination's NUMBER (and suffix) but sits on a DIFFERENT ramp whose
    /// taxi nodes are in the AIRCRAFT's connected component — i.e. a stand guidance
    /// could actually take the pilot to. At direction-named airports several ramps
    /// reuse plain numbers (LMML: "N Parking 1" / "Parking 1" / "Southeast 1"), and a
    /// controller's "stand 1" gives a blind pilot no way to tell which combo entry is
    /// meant; when the picked one turns out to be unreachable, the reachable
    /// same-numbered stand is almost always the one ATC intended. Returns a sentence
    /// fragment to splice into the warning (leading space included), or null when
    /// there is no such stand. Best-effort: any failure returns null and the warning
    /// speaks without a suggestion.
    /// </summary>
    private string? DescribeConnectedAlternateStand(
        IAirportDataProvider dataProvider, string icao, string destinationName,
        int aircraftComponentId, double aircraftLat, double aircraftLon)
    {
        try
        {
            if (_graph == null) return null;
            if (!RouteStartGate.TryParseStandNumber(
                    destinationName, out string destPrefix, out int number, out string destSuffix))
                return null;

            ParkingSpot? best = null;
            double bestDistM = double.MaxValue;
            foreach (var spot in ResolveParkingSpots(dataProvider, icao))
            {
                if (spot.Number != number) continue;
                string spotSuffix = (spot.Suffix ?? "").Trim().ToUpperInvariant();
                if (!string.Equals(spotSuffix, destSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                // Skip the destination's own ramp — the point is a DIFFERENT ramp
                // that happens to reuse the number.
                if (string.Equals((spot.Name ?? "").Trim(), destPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // The stand must be servable from the aircraft's side: its nearest
                // in-component node has to be genuinely at the stand, not across the
                // island gap (which FindNearestNode would otherwise happily return).
                var node = _graph.FindNearestNode(
                    spot.Latitude, spot.Longitude, requiredComponentId: aircraftComponentId);
                if (node == null) continue;
                if (TaxiGraph.FastDistanceMeters(
                        spot.Latitude, spot.Longitude, node.Latitude, node.Longitude) > 150)
                    continue;

                double d = TaxiGraph.FastDistanceMeters(
                    aircraftLat, aircraftLon, spot.Latitude, spot.Longitude);
                if (d < bestDistM)
                {
                    bestDistM = d;
                    best = spot;
                }
            }

            return best == null ? null
                : $" The nearest stand with the same number that is connected to your position " +
                  $"is {best}, {FormatDistance(bestDistM)} away.";
        }
        catch
        {
            return null; // suggestion is best-effort; the warning itself still speaks
        }
    }

    /// <summary>
    /// Every field LoadRoute writes before it can reach a reachability refusal — no start
    /// node in range, no buildable route, or a first leg across a runway, each only for a
    /// destination off the network the aircraft is on — captured so a refused Calculate
    /// mid-taxi can be rolled back to leave the route currently being flown untouched.
    /// Deliberately excludes <see cref="LastRouteUnmappedStartWarning"/>: that field must
    /// stay cleared on a refusal, never restored, so a refused load can never leave a warning
    /// behind for the form or the one-shot to speak later. The older failure returns in
    /// LoadRoute (no taxi path data, destination node not found, no nearby taxiway node,
    /// could not calculate a route) apply to every OTHER reachability class, including
    /// Unchanged, and are unaffected by this — they are deliberately left as they were before
    /// this rollback existed.
    /// </summary>
    private readonly record struct LoadRouteRollback(
        IAirportDataProvider? DataProvider,
        int DestinationNodeId,
        string DestinationName,
        string Icao,
        List<string>? OriginalTaxiwaySequence,
        bool PreferIlsHold,
        bool BacktrackDeparture,
        bool BacktrackDepApproachAnnounced,
        int HoldingPointHoldNodeId,
        bool IsRunwayLineup,
        Navigation.ProgressiveTerminator? ProgressiveTerminator,
        double LineupTargetLat,
        double LineupTargetLon,
        double LineupHeadingMag,
        double LineupHeadingTrue,
        bool HasLineupTarget,
        bool AutoActivateFired,
        double PostHighSpeedExitMinBearing,
        TaxiGraph? Graph,
        long GraphGeneration);

    private LoadRouteRollback CaptureLoadRouteRollback() => new(
        _dataProvider, _destinationNodeId, _destinationName, _icao, _originalTaxiwaySequence,
        _preferIlsHold, _backtrackDeparture, _backtrackDepApproachAnnounced, _holdingPointHoldNodeId,
        _isRunwayLineup, _progressiveTerminator, _lineupTargetLat, _lineupTargetLon,
        _lineupHeadingMag, _lineupHeadingTrue, _hasLineupTarget, _autoActivateFired,
        _postHighSpeedExitMinBearing, _graph, _graphGeneration);

    private void RestoreLoadRouteRollback(LoadRouteRollback r)
    {
        _dataProvider = r.DataProvider;
        _destinationNodeId = r.DestinationNodeId;
        _destinationName = r.DestinationName;
        _icao = r.Icao;
        _originalTaxiwaySequence = r.OriginalTaxiwaySequence;
        _preferIlsHold = r.PreferIlsHold;
        _backtrackDeparture = r.BacktrackDeparture;
        _backtrackDepApproachAnnounced = r.BacktrackDepApproachAnnounced;
        _holdingPointHoldNodeId = r.HoldingPointHoldNodeId;
        _isRunwayLineup = r.IsRunwayLineup;
        _progressiveTerminator = r.ProgressiveTerminator;
        _lineupTargetLat = r.LineupTargetLat;
        _lineupTargetLon = r.LineupTargetLon;
        _lineupHeadingMag = r.LineupHeadingMag;
        _lineupHeadingTrue = r.LineupHeadingTrue;
        _hasLineupTarget = r.HasLineupTarget;
        _autoActivateFired = r.AutoActivateFired;
        _postHighSpeedExitMinBearing = r.PostHighSpeedExitMinBearing;
        _graph = r.Graph;
        _graphGeneration = r.GraphGeneration;
    }

    /// <summary>
    /// Full-route nearest-segment search. Unlike <see cref="AdvanceToNearestSegment"/>
    /// (which only scans a 6-segment look-ahead window for the per-frame fast
    /// path), this scans every segment and returns the index whose centerline
    /// endpoints are closest to the given position. Used as a one-shot re-anchor
    /// at the landing-rollout → Taxiing handoff, where _currentSegmentIndex can
    /// be thousands of feet stale because the rollout phase never advanced it.
    /// </summary>
    private int FindNearestSegmentIndexFullRoute(double lat, double lon)
    {
        if (_route == null || _route.Segments.Count == 0) return 0;

        int bestIdx = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _route.Segments.Count; i++)
        {
            var seg = _route.Segments[i];
            double d = Math.Min(
                TaxiGraph.FastDistanceMeters(lat, lon, seg.ToNode.Latitude, seg.ToNode.Longitude),
                TaxiGraph.FastDistanceMeters(lat, lon, seg.FromNode.Latitude, seg.FromNode.Longitude));
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Advances _currentSegmentIndex to the segment closest to the aircraft's current position.
    /// Only moves forward (never backward). Handles segment skipping when updates are sparse.
    /// </summary>
    private void AdvanceToNearestSegment(double lat, double lon, double headingTrue)
    {
        if (_route == null) return;

        int bestIdx = _currentSegmentIndex;
        double bestDist = double.MaxValue;

        // Hairpin guard — see ADVANCE_TRACKING_CROSS_M.
        bool trackingShortOfEnd = false;
        if (_currentSegmentIndex < _route.Segments.Count)
        {
            AlongTrackToSegmentEnd(lat, lon, _route.Segments[_currentSegmentIndex],
                out double alongRemM, out double crossM);
            trackingShortOfEnd = alongRemM > ADVANCE_GUARD_MIN_REMAINING_M
                                 && Math.Abs(crossM) <= ADVANCE_TRACKING_CROSS_M;
        }
        bool sharpJunctionAhead = trackingShortOfEnd
            && _currentSegmentIndex + 1 < _route.Segments.Count
            && Math.Abs(_route.Segments[_currentSegmentIndex + 1].TurnAngleDegrees) >= ADVANCE_MAX_HEADING_OFF_DEG
            && Math.Abs(_route.Segments[_currentSegmentIndex + 1].TurnAngleDegrees) < ADVANCE_GUARD_MAX_TURN_DEG;

        // Look at current segment and up to 5 segments ahead
        int lookAhead = Math.Min(_currentSegmentIndex + 6, _route.Segments.Count);
        for (int i = _currentSegmentIndex; i < lookAhead; i++)
        {
            var seg = _route.Segments[i];

            if (i > _currentSegmentIndex && sharpJunctionAhead
                && Math.Abs(NormalizeAngle(seg.BearingDegrees - headingTrue)) > ADVANCE_MAX_HEADING_OFF_DEG)
                continue;

            double distToEnd = TaxiGraph.FastDistanceMeters(
                lat, lon, seg.ToNode.Latitude, seg.ToNode.Longitude);
            double distToStart = TaxiGraph.FastDistanceMeters(
                lat, lon, seg.FromNode.Latitude, seg.FromNode.Longitude);
            double dist = Math.Min(distToEnd, distToStart);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }

        // Endpoint-tie pin breaker (KLAS 26R, 2026-08-20). The scan above measures
        // ENDPOINT distance, and the current segment shares its end node with the
        // next — so once the aircraft has rolled past that shared node without
        // passing inside the 25 m capture radius (a wide corner), the two tie
        // forever, strict-improvement keeps the stale index, and on a long next
        // segment (B at KLAS is 345 m) the aircraft can be squarely ON the route
        // yet outside every endpoint's reach. The walk target then freezes at
        // (stale segment end + look-ahead) and the tone orbits the pilot around a
        // fixed point. Advance on the evidence the endpoint scan cannot see: the
        // aircraft's projection is past the current segment's end AND interior on
        // the next segment within a taxiway-width cross-track bound.
        //
        // Runs BEFORE the proximity early-out below — a pilot far along the long
        // next segment is >SEGMENT_ADVANCE_MAX_DIST_M from every endpoint, which
        // is exactly the pinned case, not an off-route one. Goes through
        // AdvanceSegment() so the taxiway announcement and latch resets behave
        // exactly like every other advance. NEVER fires while the current segment
        // is a hold-short segment: advancing past an un-announced hold-short is
        // the runway-incursion direction, and that invariant outranks un-pinning
        // (the hold-short flow has its own capture handling).
        //
        // Fires in EITHER situation where the endpoint scan cannot advance: the
        // shared-node tie (bestIdx unmoved — the KLAS shape), OR the scan picking a
        // later segment that is still out of proximity range. The second is the far
        // half of the same long segment: once past its midpoint the far endpoint
        // wins the scan (no tie) but can still sit beyond SEGMENT_ADVANCE_MAX_DIST_M,
        // so gating on the tie alone left a window — ~73 m of the KLAS B segment,
        // more on a longer one — where the index stayed stale with the target
        // frozen behind the aircraft. The projection test is the evidence either
        // way; which endpoint happened to be nearest is not part of it.
        if ((bestIdx == _currentSegmentIndex || bestDist > SEGMENT_ADVANCE_MAX_DIST_M)
            && _currentSegmentIndex + 1 < _route.Segments.Count
            && !_route.Segments[_currentSegmentIndex].IsHoldShortPoint)
        {
            var (pinLats, pinLons) = RoutePoints();
            if (GuidanceGeometry.HasPassedOntoNextSegment(
                    pinLats, pinLons, _currentSegmentIndex, lat, lon,
                    SEGMENT_PASS_ADVANCE_MAX_CROSS_M))
            {
                AdvanceSegment();
                return;
            }
        }

        // Not near any of them — the aircraft is off the route, not progressing along it.
        // Leave the index alone so off-route detection sees an un-refreshed
        // _lastSegmentAdvanceTime and can do its job. Ungated, an aircraft driving AWAY from
        // its route kept advancing along it (measured: 13 of 981 advances with the tone
        // pointing >90 degrees away, one a 0->3 jump at 179 degrees), consuming the route —
        // and because every advance stamps _lastSegmentAdvanceTime, which forces nearTurn
        // true for 4 s, it SUPPRESSED the off-route detector that would have caught it.
        if (bestDist > SEGMENT_ADVANCE_MAX_DIST_M) return;

        if (bestIdx > _currentSegmentIndex)
        {
            // Check for hold-short points we might be skipping. A hold-short is never
            // passed silently — that is the whole point of this block — but it is only
            // ANNOUNCED when the aircraft is genuinely at it. "Stop. Hold short of
            // runway 09L" for a line 90 m away is both a false stop (it pauses the tone
            // and waits for a Continue press) and a lost one, because the index moves
            // past the segment and the real crossing then gets nothing.
            for (int i = _currentSegmentIndex; i < bestIdx; i++)
            {
                if (!_route.Segments[i].IsHoldShortPoint) continue;

                double distToHold = TaxiGraph.FastDistanceMeters(
                    lat, lon,
                    _route.Segments[i].ToNode.Latitude,
                    _route.Segments[i].ToNode.Longitude);

                if (distToHold <= HOLD_SHORT_ANNOUNCE_MAX_DIST_M)
                {
                    _currentSegmentIndex = i + 1;
                    _lastSegmentAdvanceTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
                    HandleHoldShort(_route.Segments[i]);
                }
                else
                {
                    // Too far to call it reached. Hold the index ON the hold-short
                    // segment rather than stepping over it, so the normal
                    // 300/150/50 ft countdown still runs when the aircraft actually
                    // arrives. Never advance past an un-announced hold-short.
                    if (i > _currentSegmentIndex)
                    {
                        _currentSegmentIndex = i;
                        _lastSegmentAdvanceTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
                    }
                }
                return;
            }

            _currentSegmentIndex = bestIdx;
            _lastSegmentAdvanceTime = MSFSBlindAssist.Utils.SimClock.UtcNow;

            var newSeg = _route.Segments[_currentSegmentIndex];
            AnnounceOrDeferTaxiwayChange(newSeg.TaxiwayName);

            _approachAnnounced = false;
            _turnImminentAnnounced = false;
            _crossingAnnounced = false;
        }
    }

    /// <summary>
    /// Picks the node on the FIRST cleared taxiway that the route should be anchored
    /// on (the LEPA pre-snap). Ranks by the total graph cost of the route through the
    /// candidate — (aircraft → entry) + (entry → destination) — via
    /// <see cref="TaxiRouter.FindBestEntryNodeOnTaxiway"/>, so an entry the route
    /// would have to reverse out of loses to the junction it hangs off.
    ///
    /// The Euclidean-nearest node this replaces picked a 15 m dead-end stub at LOWS
    /// (2026-08-16, progressive taxi "L" off the runway 15 vacate point): the route
    /// opened with a 15 m leg the wrong way and a 170° hairpin, and because the
    /// pre-snap becomes the A* start node the router could not recover from it.
    ///
    /// Falls back to the Euclidean-nearest node whenever the cost ranking can't run
    /// (no graph node near the aircraft, taxiway absent from the destination's
    /// component) or picks something beyond the Euclidean search radius the caller
    /// has always been bounded by — so this can only ever change WHICH near node is
    /// chosen, never widen the search.
    ///
    /// Task 6 Defect A (PR #238 review, Important 1): <c>euclideanNearest</c> is returned
    /// UNFILTERED on three of this method's four exits (<c>anchor == null</c>, <c>bestId ==
    /// -1</c>, the gap check below) and becomes the A* start the same way the cost-ranked
    /// <c>best</c> node does, so it must exclude a bridge-only stand stub just as the anchor
    /// call below already does.
    /// </summary>
    private TaxiNode? SelectFirstTaxiwayEntry(
        double aircraftLat, double aircraftLon, string taxiwayName,
        int destComponentId, int destinationNodeId)
    {
        var euclideanNearest = _graph!.FindNearestNodeOnTaxiway(
            aircraftLat, aircraftLon, taxiwayName, requiredComponentId: destComponentId,
            excludeBridgeOnlyStandStubs: true);
        if (euclideanNearest == null) return null;

        // Dijkstra needs a node to start from; the aircraft sits between nodes, so
        // use the nearest one. The hop from the aircraft onto it is the same for
        // every candidate, so it can't affect the ranking. Task 6 Defect A: this is a route-start
        // anchor like any other FindNearestNode call feeding a real path search below.
        var anchor = _graph.FindNearestNode(
            aircraftLat, aircraftLon, requiredComponentId: destComponentId,
            excludeBridgeOnlyStandStubs: true);
        if (anchor == null) return euclideanNearest;

        int bestId = new TaxiRouter(_graph)
            .FindBestEntryNodeOnTaxiway(anchor.NodeId, taxiwayName, destinationNodeId);
        if (bestId == -1 || !_graph.Nodes.TryGetValue(bestId, out var best))
            return euclideanNearest;

        const double MAX_PRESNAP_M = 800.0;   // matches FindNearestNodeOnTaxiway's default
        double gap = TaxiGraph.FastDistanceMeters(
            aircraftLat, aircraftLon, best.Latitude, best.Longitude);
        return gap <= MAX_PRESNAP_M ? best : euclideanNearest;
    }

    /// <summary>
    /// Attempts to recalculate the route from the current position to the destination.
    /// </summary>
    private void TryRecalculateRoute(double lat, double lon, double headingTrue)
    {
        if (_graph == null) return;

        if ((MSFSBlindAssist.Utils.SimClock.UtcNow - _lastRecalculationTime).TotalSeconds < RECALCULATION_COOLDOWN_SEC)
            return;

        // Uncharted-crossing guard (belt and suspenders beside the off-route
        // detector's own suppression): never rebuild a route while a synthetic
        // crossing segment is still ahead — a rebuild routes through the component
        // filter and reproduces the island-stub route the crossing replaced.
        if (_route != null)
        {
            for (int i = _currentSegmentIndex; i < _route.Segments.Count; i++)
            {
                if (_route.Segments[i].IsUncharted)
                {
                    try { _guidanceLog.Info("Recalc suppressed: uncharted crossing segment ahead"); }
                    catch { /* diagnostic only */ }
                    return;
                }
            }
        }

        // Final-segment guard. At the destination hold-short there's nothing
        // left to recalculate — the runway / gate is meters away. The off-route
        // detector can spuriously fire here when the aircraft is stopped at a
        // hold-short with a small lateral drift (synced-cockpit "you're there
        // but your copilot isn't yet" desync). Recalcing from this state can
        // produce wildly different routes.
        if (_route != null && _currentSegmentIndex >= _route.Segments.Count - 1)
            return;

        // Near-destination guard. Some navdata has a gap between the last taxiway
        // node and the runway/gate node (the route bridges this with a virtual
        // straight-line segment). When the aircraft makes a curved entry into the
        // runway or gate from that bridge it can drift off the virtual centerline
        // and trigger a constrained recalc that snaps to the last taxiway dead-end
        // and routes backwards around the taxiway loop (VHHH J1 → K1 → backwards
        // 563 m instead of the remaining 67 m). Within 200 m of the destination
        // the tone already guides the pilot; no recalc is needed.
        if (_destinationNodeId != 0 && _graph != null &&
            _graph.Nodes.TryGetValue(_destinationNodeId, out var destNodeForGuard))
        {
            if (TaxiGraph.FastDistanceMeters(lat, lon,
                    destNodeForGuard.Latitude, destNodeForGuard.Longitude)
                < NEAR_DESTINATION_SUPPRESS_RECALC_M)
                return;
        }

        _lastRecalculationTime = MSFSBlindAssist.Utils.SimClock.UtcNow;

        // Position-aware sequence trim. Walk the original ATC sequence from
        // the LAST taxiway backwards, asking "is there a node on this taxiway
        // within 50 m of the aircraft?" — the first hit is the latest sequence
        // taxiway the aircraft is physically on. Anything before it is behind
        // us and should be dropped from the remaining sequence.
        //
        // Why not BuildRemainingSequence here: that function trims based on
        // route.Segments[currentSegmentIndex].TaxiwayName, i.e. what the
        // ROUTE says we're on. After a recalc resets currentSegmentIndex to
        // 0, the route says we're on LE (first segment) even though the
        // aircraft is actually at H2 hold-short. Trimming from the route
        // state would re-include LE, D, NORTH and produce a constrained
        // path that physically starts way back at LE — the aircraft then
        // has to navigate the whole airport in reverse to follow it. This
        // is the LEPA "big loop" bug.
        //
        // Driving the trim from aircraft position instead means: if you're
        // at H2, the remaining sequence is just ["H2"]; the route is a few
        // meters; no loop.
        // Same component-filter invariant as LoadRoute: the recalculated start
        // node must be co-component with the existing destination, otherwise
        // FindConstrainedPath / FindShortestPath will return null. When the
        // destination isn't in the graph (shouldn't happen during Taxiing, but
        // be defensive), fall back to no filter rather than blocking every
        // candidate — a silent recalc bailout would suppress the legitimate
        // "Off route" announcement.
        int? destComponentId = _graph!.Nodes.ContainsKey(_destinationNodeId)
            ? _graph.Nodes[_destinationNodeId].ComponentId
            : (int?)null;

        // Same reachability decision as LoadRoute: when the aircraft is not on the destination's piece of
        // network, the recalculated route still starts on that piece, and its straight unmapped first leg
        // is checked once the route exists.
        var recalcReachability = destComponentId.HasValue
            ? RouteReachability.Classify(_graph, lat, lon, _destinationNodeId)
            : ReachabilityClass.Unchanged;

        (List<string>? remainingSequence, TaxiNode? nearestNode) =
            FindRemainingSequenceByPosition(lat, lon, destComponentId);

        // If no sequence taxiway is near the aircraft, fall back to the
        // heading-aware picker. The constrained path will likely fail and
        // bail to shortest — that's the right behaviour when the aircraft
        // has drifted off every cleared taxiway.
        if (nearestNode == null)
        {
            // Task 6 Defect A: the recalculated route start. Same exclusion as LoadRoute's
            // primary picker.
            nearestNode = _graph.FindNearestNodeInDirection(
                lat, lon, headingTrue, requiredComponentId: destComponentId,
                excludeBridgeOnlyStandStubs: true);
            if (nearestNode == null)
            {
                if (recalcReachability == ReachabilityClass.DestinationNotConnected)
                {
                    _guidanceLog.Info($"Reachability: recalc refused dest=\"{_destinationName}\" class={recalcReachability} " +
                                      $"no start node ac={lat:F6},{lon:F6}");
                    // ReachabilityRefusalGate: this verdict is a standing property of the
                    // airport data and the current destination, so it refuses IDENTICALLY
                    // every RECALCULATION_COOLDOWN_SEC cycle for as long as the aircraft
                    // stays off this destination's network. Speak it once, not every 15 s —
                    // an unlatched AnnounceImmediate here used to cut off hold-short and
                    // runway-crossing callouts on every retry. The "no-start-node" site tag
                    // (Minor 4) keeps this key from ever colliding with the OTHER refusal
                    // site below, whose CrossesUnnamedRunway branch can also produce an
                    // empty runway designator for the same destination/verdict.
                    string refusalKey = ReachabilityRefusalGate.KeyFor(
                        recalcReachability, _destinationName, "", site: "no-start-node");
                    if (ReachabilityRefusalGate.ShouldAnnounce(_lastReachabilityRefusalKey, refusalKey))
                    {
                        _lastReachabilityRefusalKey = refusalKey;
                        SpeakNow(RouteReachabilityMessages.RecalculationRefusedDestination(_destinationName));
                    }
                }
                return;
            }
        }

        var router = new TaxiRouter(_graph);

        // Prefer getting back onto the ATC-cleared taxiway sequence when possible —
        // only fall back to shortest path if the constrained route fails. This honors
        // the pilot's ATC clearance during brief deviations (e.g., wide turn, wrong
        // turn they corrected). FindConstrainedPath already falls back internally if
        // no valid path exists, setting ConstrainedFallbackReason.
        TaxiRoute? newRoute;
        if (remainingSequence != null && remainingSequence.Count > 0)
        {
            newRoute = router.FindConstrainedPath(nearestNode.NodeId, _destinationNodeId, remainingSequence,
                destinationIsRunway: _isRunwayLineup);
        }
        else
        {
            // The aircraft is near NO taxiway of the cleared sequence — it is
            // either past the whole clearance or far off it. Re-applying the
            // FULL original sequence from here routes the pilot BACKWARDS
            // through the entire clearance (KIAH 2026-06-10 15:24: a via-FE
            // recalc built a 126-node loop back to a taxiway 1.5 km behind;
            // only the post-recalc sanity gate stopped it). Shortest path to
            // the destination is the honest recovery.
            newRoute = router.FindShortestPath(nearestNode.NodeId, _destinationNodeId);
        }

        if (newRoute == null || newRoute.Segments.Count == 0)
        {
            SpeakNow("Off route. Unable to recalculate.");
            return;
        }

        // Re-apply the named-holding-point pin, so a recalc keeps routing the pilot to the
        // painted line they chose. Deliberately BEFORE the sanity gate below: the gate must
        // judge the route we would actually fly, not an intermediate one.
        if (_holdingPointHoldNodeId != 0 &&
            ApplyHoldingPointPin(router, newRoute, nearestNode.NodeId, _destinationNodeId,
                                 remainingSequence, out _) is { } pinnedRecalc)
        {
            newRoute = pinnedRecalc;
        }

        // PR #238 review, Minor D re-fix: gated on RouteReachability.IsOffDestinationNetwork,
        // not a hand-typed `!= Unchanged` comparison -- this was the fourth independent copy
        // of that same expression (see the sibling guard in LoadRoute above for the full
        // reasoning: "is the aircraft off network, so the first leg needs checking" and
        // "must a refusal roll back state" are different questions that happen to share a
        // formula today, and naming this one asks the actual question instead of re-deriving
        // it inline yet again).
        if (RouteReachability.IsOffDestinationNetwork(recalcReachability))
        {
            var firstLeg = RouteReachability.CheckFirstLeg(_graph!, lat, lon, newRoute.Segments[0].FromNode);
            bool destinationOffNetwork = recalcReachability == ReachabilityClass.DestinationNotConnected;
            if (firstLeg.CrossesRunway)
            {
                string recalcRunwayLog = string.IsNullOrEmpty(firstLeg.RunwayDesignator) ? "(unnamed)" : firstLeg.RunwayDesignator;
                _guidanceLog.Info($"Reachability: recalc refused dest=\"{_destinationName}\" class={recalcReachability} " +
                                  $"first leg crosses runway {recalcRunwayLog} gapM={firstLeg.GapMeters:F0} " +
                                  $"ac={lat:F6},{lon:F6}");
                // A touched runway with no designator (both ends unnamed) must never speak a
                // sentence with a hole where the runway name belongs. This path never dropped the
                // route being flown (unlike LoadRoute's rollback above), so it gets its own
                // "Off route. Unable to recalculate." lead rather than CrossesUnnamedRunway's
                // load-time "No taxi route." — see RecalculationRefusedUnnamedRunway's own doc.
                //
                // ReachabilityRefusalGate: the touched runway is a standing fact about this
                // destination from this disconnected position, so it refuses IDENTICALLY every
                // recalculation cycle — speak it once, not every RECALCULATION_COOLDOWN_SEC. The
                // "crosses-runway" site tag (Minor 4) keeps this key from ever colliding with the
                // OTHER refusal site above: both can produce an empty runway designator for the
                // same destination/verdict (this branch's own CrossesUnnamedRunway case), and
                // without the tag the second, materially different refusal would be silently
                // suppressed as a "repeat" of the first.
                string refusalKey = ReachabilityRefusalGate.KeyFor(
                    recalcReachability, _destinationName, firstLeg.RunwayDesignator, site: "crosses-runway");
                if (ReachabilityRefusalGate.ShouldAnnounce(_lastReachabilityRefusalKey, refusalKey))
                {
                    _lastReachabilityRefusalKey = refusalKey;
                    SpeakNow(string.IsNullOrEmpty(firstLeg.RunwayDesignator)
                        ? RouteReachabilityMessages.RecalculationRefusedUnnamedRunway()
                        : destinationOffNetwork
                            ? RouteReachabilityMessages.RecalculationRefusedDestinationRunway(_destinationName, firstLeg.RunwayDesignator)
                            : RouteReachabilityMessages.RecalculationRefusedRunway(firstLeg.RunwayDesignator));
                }
                return;
            }
            // The recalculated route starts with an unmapped leg, the case LoadRoute warns about, but a
            // recalculation never speaks a start warning (it is not the start of guidance). Diagnostic
            // only, so "why did guidance steer me across here" can still be answered from the log.
            _guidanceLog.Info($"Reachability: recalc proceeds across an unmapped first leg dest=\"{_destinationName}\" " +
                              $"class={recalcReachability} gapM={firstLeg.GapMeters:F0} ac={lat:F6},{lon:F6}");
        }

        // This recalculation cycle did not refuse for reachability reasons — either the
        // aircraft was already on the destination's own network (Unchanged), or it was not
        // but the straight unmapped first leg to/from here did not cross a runway. Either way
        // the standing condition ReachabilityRefusalGate's latch exists to silence has, for
        // now, stopped recurring at this position: clear the latch so a LATER, unrelated
        // off-route episode that refuses for the exact same reason is not silently swallowed
        // by a latch raised an arbitrary time earlier in this taxi (PR #238 review, Important
        // 2 — the sibling clear, for the aircraft settling back onto the route without ever
        // reaching a recalculation, lives in the off-route detector in UpdatePosition).
        _lastReachabilityRefusalKey = null;

        // Post-recalc sanity gate. Two failure modes are rejected here:
        //
        //  A. Length blow-up: the new route is dramatically longer than what
        //     the aircraft was about to taxi anyway. This catches the
        //     constrained-router-picks-dead-end-exit bug (KDEN gate A 60 via
        //     M4: recalc produced a 2960 m loop when the remaining route was
        //     ~200 m). Previously gated on ConstrainedFallbackReason because
        //     a successful-but-bad constrained route slipped through; now
        //     gated only on length + a heading sanity check, so the same
        //     class of failure can no longer hide behind the fallback flag.
        //
        //  B. Reverse-from-the-start: the new route's first segment points
        //     opposite the straight-line bearing to the destination. A real
        //     recovery route never starts by moving away from where it's
        //     going (a legitimate detour reaches a junction and turns), so
        //     a first-segment bearing within ±60° of the *opposite* of the
        //     destination bearing is a clear router bug — the dead-end
        //     backtrack signature.
        //
        // Either guard, on its own, would have caught the KDEN bug. Both
        // present together produces a high-precision gate: a long recalc is
        // accepted as long as it at least heads in the right general
        // direction (i.e., the pilot really has wandered far off course),
        // and a backwards recalc is rejected even if its total length is
        // similar to the old route.
        if (_route != null && newRoute.Segments.Count > 0)
        {
            double oldRemaining = 0;
            for (int i = _currentSegmentIndex; i < _route.Segments.Count; i++)
                oldRemaining += _route.Segments[i].DistanceMeters;

            // --- Indicator A: length blow-up ---
            bool lengthBlowUp = oldRemaining > 0
                && newRoute.TotalDistanceMeters > oldRemaining * RECALC_LENGTH_BLOWUP_RATIO + RECALC_LENGTH_BLOWUP_PAD_M;

            // --- Indicator B: first segment heads away from destination ---
            // Bearing of the new route's first segment (true degrees) vs the
            // straight-line bearing from the aircraft's current position to
            // the destination. A difference of ≥ 120° means the route is
            // starting by moving away from the destination.
            bool firstSegHeadsBackwards = false;
            if (_destinationNodeId != 0 && _graph != null &&
                _graph.Nodes.TryGetValue(_destinationNodeId, out var destNodeForBearing))
            {
                double bearingToDest = NavigationCalculator.CalculateBearing(
                    lat, lon, destNodeForBearing.Latitude, destNodeForBearing.Longitude);
                double firstSegBearing = newRoute.Segments[0].BearingDegrees;
                double delta = Math.Abs(NormalizeAngle(firstSegBearing - bearingToDest));
                firstSegHeadsBackwards = delta > RECALC_BACKWARDS_DELTA_DEG;
            }

            // Reject if EITHER indicator fires. Either condition alone is a
            // strong signal of router malfunction; requiring both would let
            // the KDEN case through (length blew up cleanly, first-seg
            // delta was ~144° which is over 120°, both fire here).
            if (lengthBlowUp || firstSegHeadsBackwards)
            {
                string reasonStr = !string.IsNullOrEmpty(newRoute.ConstrainedFallbackReason)
                    ? newRoute.ConstrainedFallbackReason
                    : (lengthBlowUp ? "recalculated route too long" : "recalculated route heads backwards");
                SpeakNow(
                    $"Off route. Could not follow clearance. {reasonStr}. Continuing on original route.");
                return;
            }
        }

        newRoute.DestinationName = _destinationName;

        // Apply the same runway-destination safety truncation as LoadRoute — otherwise
        // after an auto-recalc the pilot would roll straight onto the runway instead
        // of stopping at the hold-short line.
        // Captured BEFORE the truncation below, for the same reason LoadRoute captures it there:
        // truncation legitimately moves a REACHING route's end back to a hold line, and judging
        // the post-truncation end would read that as "ended short".
        bool recalcReachedDestination = newRoute.Segments.Count > 0 &&
            newRoute.Segments.Any(s2 => s2.ToNode != null && s2.ToNode.NodeId == _destinationNodeId);
        var recalcEndNode = newRoute.Segments.Count > 0 ? newRoute.Segments[^1].ToNode : null;

        // Re-apply the user's explicit hold-short picks, remapped onto the
        // REMAINING sequence (a suffix of the original — indices shift by the
        // number of legs already behind the aircraft; picks on those legs are
        // dropped). Runs before the auto-detect pass below so a user label wins,
        // matching LoadRoute's order. A remaining pick the new route cannot
        // honour is reported in the "Route changed" callout rather than silently
        // dropped.
        string? recalcHoldWarning = null;
        var recalcDeferredPicks = new List<DeferredRunwayPick>();
        var recalcPickEvents = new List<TaxiRouteRunwayEvent>();
        if (remainingSequence is { Count: > 0 } && _originalTaxiwaySequence != null)
        {
            int seqOffset = _originalTaxiwaySequence.Count - remainingSequence.Count;
            if (seqOffset >= 0)
            {
                if (_userHoldShortIndices is { Count: > 0 })
                {
                    var remappedIdx = new List<int>();
                    foreach (int idx in _userHoldShortIndices)
                        if (idx >= seqOffset) remappedIdx.Add(idx - seqOffset);
                    if (remappedIdx.Count > 0)
                        ApplyUserHoldShorts(newRoute, remainingSequence, remappedIdx);
                }
                if (_userRunwayHoldShorts is { Count: > 0 })
                {
                    var remappedRwy = new Dictionary<int, string>();
                    foreach (var kvp in _userRunwayHoldShorts)
                        if (kvp.Key >= seqOffset) remappedRwy[kvp.Key - seqOffset] = kvp.Value;
                    if (remappedRwy.Count > 0)
                        recalcHoldWarning = ApplyUserRunwayHoldShorts(
                            newRoute, remainingSequence, remappedRwy,
                            lat, lon, recalcPickEvents, recalcDeferredPicks);
                }
                if (_userTaxiwayHoldShorts is { Count: > 0 })
                {
                    var remappedTwy = new Dictionary<int, string>();
                    foreach (var kvp in _userTaxiwayHoldShorts)
                        if (kvp.Key >= seqOffset) remappedTwy[kvp.Key - seqOffset] = kvp.Value;
                    if (remappedTwy.Count > 0)
                    {
                        string? twyWarning = ApplyUserTaxiwayHoldShorts(
                            newRoute, remainingSequence, remappedTwy);
                        if (twyWarning != null)
                            recalcHoldWarning = recalcHoldWarning == null
                                ? twyWarning : recalcHoldWarning + " " + twyWarning;
                    }
                }
            }
        }

        if (_isRunwayLineup)
            TruncateToHoldShort(newRoute, _destinationName, _preferIlsHold);

        // Distinct consecutive named taxiways of the recalculated route, in order.
        var viaNames = RouteTaxiwaySequence.DistinctConsecutive(newRoute.Segments);

        // No-op recalc guard: if the recalculated route reproduces the SAME remaining
        // taxiway sequence we're already on, leave the current route + guidance untouched.
        // The usual trigger is the off-route detector tripping while the aircraft cuts the
        // corner ONTO a taxiway it is correctly turning onto (the route's next segment is
        // laterally offset mid-turn) — the recalc then re-plans the identical tail. Swapping
        // it in would bark "Route changed", reset the safety-critical countdown latches, and
        // re-slew the steering tone, all for a route the pilot is already correctly on
        // (reported as a spurious "Route changed … super sharp right" while turning onto N).
        // The recalc cooldown was already stamped by the caller, so this won't re-fire each
        // frame. A genuine reroute (different taxiways) has a different sequence and proceeds.
        var oldRemainingVia = RouteTaxiwaySequence.DistinctConsecutive(
            _route?.Segments, _currentSegmentIndex);
        if (oldRemainingVia.Count > 0 &&
            oldRemainingVia.SequenceEqual(viaNames, StringComparer.OrdinalIgnoreCase))
            return;

        // Adopting the route re-runs the auto crossing hold-shorts: truncation only restores
        // the DESTINATION's own hold, so without them the recalculated route reaches the runway
        // with every intermediate crossing untagged (PHNL 2026-09-03 — see AdoptRoute). It sits
        // BELOW the no-op guard so a discarded recalc neither re-tags a route nobody adopts nor
        // writes a crossings line claiming it did.
        AdoptRoute(newRoute, _isRunwayLineup, _destinationName, lat, lon, phase: "recalc",
            userPickEvents: recalcPickEvents, recalculation: true);
        // A remapped pick that met its runway with no stop before that pass may share the stop
        // it has just placed for a runway in front of it (RetryDeferredRunwayPicks).
        string? recalcDeferredWarning = RetryDeferredRunwayPicks(newRoute, recalcDeferredPicks, lat, lon);
        if (recalcDeferredWarning != null)
            recalcHoldWarning = recalcHoldWarning == null
                ? recalcDeferredWarning : recalcHoldWarning + " " + recalcDeferredWarning;

        // Re-probe reachability, using the SAME core LoadRoute uses. This must sit BELOW
        // the no-op guard above: the verdict describes `newRoute`, so computing it earlier
        // let a recalc that was then DISCARDED overwrite the flag for the route still
        // loaded — arming the spoken during-lineup bailout on a good departure, or
        // disarming it on one that genuinely ends short. Pre-PR the expression read only
        // `_destinationNodeId`, so it was route-independent and the early return was
        // harmless; it is not any more. Keeping it here also means the bounded Dijkstra
        // inside the walk probe never runs for a recalc that is thrown away.
        if (_isRunwayLineup)
        {
            var reach = RunwayReachGate.Evaluate(
                isRunwayDestination: true,
                DestinationCrossTrackMeters(_destinationNodeId), RUNWAY_REACH_MAX_CROSS_M,
                recalcReachedDestination,
                recalcEndNode != null && RouteEndIsRunwayHold(recalcEndNode, _destinationName),
                recalcEndNode != null,
                () => RouteEndWalkToRunwayMeters(recalcEndNode!, _destinationName),
                RUNWAY_REACH_MAX_WALK_M);
            _routeReachesRunway = reach.Verdict == RunwayReachVerdict.Reaches;
        }
        else
        {
            // Gate destinations leave the runway-only safety net disarmed.
            _routeReachesRunway = true;
        }

        _currentSegmentIndex = 0;
        _approachAnnounced = false;
        _curveAnnouncedSign = 0;
        _turnImminentAnnounced = false;
        _crossingAnnounced = false;
        _lastCrossingNodeId = -1;
        // Reset countdown latches — otherwise stale flags from the OLD route
        // will suppress the 300/150/50ft hold-short and 50/20/10ft parking
        // callouts on the NEW route. Safety-critical.
        _holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;
        _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
        ResetIncursionNodeMemory();
        // Re-arm the incursion callout for the new route, but START its cooldown: the
        // "Route changed … crossing runways …" sentence below is spoken immediately and would
        // otherwise be cut off by "Crossing runway 28R." on the very next frame. That sentence
        // already names the runways, so nothing is lost by holding the tactical callout for the
        // cooldown. LoadRoute deliberately does the opposite (clears the stamp to MinValue) —
        // it announces its own summary through the queue, not over this callout.
        _lastIncursionWarningTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
        _incursionCooldownFromRecalc = true;
        _headingErrorInitialized = false;

        string firstTaxiway = newRoute.Segments[0].TaxiwayName;
        string distStr = FormatDistance(newRoute.TotalDistanceMeters);

        // Announce the NEW taxiway sequence so the pilot hears that their cleared route
        // changed, AND the runways the new route crosses. Wording and the crossing clause
        // live in RouteChangedCallout (pure, unit-tested) so this path and LoadRoute's
        // summary cannot drift on how a crossing is described — see that class for why the
        // crossings belong here at all. A user hold-short pick the recalculated route could
        // not honour must be heard too, not silently dropped — the pilot programmed that stop.
        string callout = RouteChangedCallout.Compose(
            viaNames, distStr, _destinationName, newRoute.RunwayEvents);
        if (!string.IsNullOrEmpty(recalcHoldWarning))
            callout = callout + " " + recalcHoldWarning;
        AnnounceInstruction(callout);

        _lastAnnouncedTaxiway = firstTaxiway;
    }

    /// <summary>
    /// Walks the original ATC taxiway sequence from latest to earliest, returning
    /// the suffix starting at the first taxiway whose nearest graph node is within
    /// NEAR_TAXIWAY_M of the aircraft. Returns (null, null) if no sequence taxiway
    /// is near the aircraft — caller should fall back to shortest path.
    ///
    /// Task 6 Defect A (PR #238 review, Important 1): the returned node feeds
    /// <see cref="TryRecalculateRoute"/>'s A* start directly, so a bridge-only stand stub must
    /// be excluded here too — the LIVE failure the review measured (EPWR: cleared "via A", the
    /// recalc landed inside the Parking-34 lead-in within 50 m of the stub).
    /// </summary>
    private (List<string>?, TaxiNode?) FindRemainingSequenceByPosition(
        double lat, double lon, int? requiredComponentId)
    {
        const double NEAR_TAXIWAY_M = 50.0;
        if (_graph == null || _originalTaxiwaySequence == null || _originalTaxiwaySequence.Count == 0)
            return (null, null);

        for (int i = _originalTaxiwaySequence.Count - 1; i >= 0; i--)
        {
            var node = _graph.FindNearestNodeOnTaxiway(
                lat, lon, _originalTaxiwaySequence[i], NEAR_TAXIWAY_M,
                requiredComponentId: requiredComponentId, excludeBridgeOnlyStandStubs: true);
            if (node != null)
            {
                var remaining = new List<string>();
                for (int j = i; j < _originalTaxiwaySequence.Count; j++)
                    remaining.Add(_originalTaxiwaySequence[j]);
                return (remaining, node);
            }
        }
        return (null, null);
    }

    private void AdvanceSegment()
    {
        if (_route == null) return;

        var completedSeg = _route.Segments[_currentSegmentIndex];
        if (completedSeg.IsHoldShortPoint)
        {
            _currentSegmentIndex++;
            // Stamp the advance timestamp here too. ContinuePastHoldShort
            // resumes on the next segment; without this, the off-route
            // 3-second persistence timer has no post-advance grace window
            // and a single lateral-deviation sample at the stop line can
            // trigger a spurious recalc the instant the pilot presses
            // Continue.
            _lastSegmentAdvanceTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
            HandleHoldShort(completedSeg);
            return;
        }

        _currentSegmentIndex++;
        _lastSegmentAdvanceTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
        _approachAnnounced = false;
        _turnImminentAnnounced = false;
        _crossingAnnounced = false;

        if (_currentSegmentIndex >= _route.Segments.Count)
        {
            HandleArrival();
            return;
        }

        var newSeg = _route.Segments[_currentSegmentIndex];
        AnnounceOrDeferTaxiwayChange(newSeg.TaxiwayName);
    }

    /// <summary>
    /// The ONE taxiway-change decision point both <see cref="AdvanceToNearestSegment"/> and
    /// <see cref="AdvanceSegment"/> go through, so the two call sites cannot drift (PR #238
    /// review, Task 5 Defect B). Classifies via <see cref="TaxiwayChangeGate.Classify"/>:
    /// skips a repeat, speaks immediately once the start-warning chatter window
    /// (<c>_startChatterSuppressUntil</c>) is closed -- byte-identical to this method's
    /// pre-fix behaviour -- or, while the window is open, DEFERS instead of announcing, so
    /// the callout can no longer cut off the safety-critical start warning the window
    /// exists to protect. <see cref="FlushPendingTaxiwayAnnouncement"/> (called every
    /// Taxiing-state frame from <c>UpdatePosition</c>) delivers a deferred name once the
    /// window closes, or discards it silently if the route has since moved past it. A
    /// deferred name that is still pending when guidance LEAVES the Taxiing state (a
    /// hold-short, lineup, arrival, a rollout) is dropped by <c>SetState</c>'s own hook
    /// instead -- see its remarks and PR #238 review Important 1.
    ///
    /// <para><c>_lastAnnouncedTaxiway</c> is updated only once a name is actually SPOKEN --
    /// immediately here in the <see cref="TaxiwayChangeGate.Decision.SpeakNow"/> case, or
    /// later by <see cref="FlushPendingTaxiwayAnnouncement"/> when a deferred one is
    /// delivered -- never merely at <see cref="TaxiwayChangeGate.Decision.Defer"/> decision
    /// time (PR #238 review, Minor 1: stamping it here too used to permanently mark a name
    /// "announced" even when the deferral was later discarded as stale and the pilot never
    /// actually heard it, silently skipping a later, genuine re-arrival on that same name
    /// forever). <see cref="TaxiwayChangeGate.Classify"/> is handed the CURRENTLY-pending
    /// name separately, which is what still stops a second advance onto the same
    /// still-waiting taxiway from re-deferring a redundant duplicate.</para>
    /// </summary>
    private void AnnounceOrDeferTaxiwayChange(string? newTaxiwayName)
    {
        bool windowOpen = MSFSBlindAssist.Utils.SimClock.UtcNow < _startChatterSuppressUntil;
        switch (TaxiwayChangeGate.Classify(
            newTaxiwayName, _lastAnnouncedTaxiway, windowOpen, _pendingTaxiwayAnnouncement))
        {
            case TaxiwayChangeGate.Decision.Skip:
                return;

            case TaxiwayChangeGate.Decision.SpeakNow:
                AnnounceInstruction($"Taxiway {newTaxiwayName}.");
                _lastAnnouncedTaxiway = newTaxiwayName!;
                // Supersedes anything still waiting from an earlier deferral: the pilot is
                // about to hear the newest name directly, so a stale intermediate one must
                // never surface later out of order.
                _pendingTaxiwayAnnouncement = null;
                return;

            case TaxiwayChangeGate.Decision.Defer:
                // _lastAnnouncedTaxiway is deliberately NOT touched here -- see this
                // method's own doc (Minor 1). Only _pendingTaxiwayAnnouncement records the
                // decision; Classify's pendingTaxiwayName check is what prevents a
                // redundant re-defer of this same name on the next advance.
                _pendingTaxiwayAnnouncement = newTaxiwayName;
                return;
        }
    }

    /// <summary>
    /// Delivers a taxiway-change name <see cref="AnnounceOrDeferTaxiwayChange"/> deferred
    /// while the start-warning chatter window was open, once that window has closed --
    /// called every Taxiing-state frame from <c>UpdatePosition</c> (cheap no-op when nothing
    /// is pending). It can only ever run while guidance is still IN Taxiing: every other
    /// state either returns before reaching this call or has no further position frames at
    /// all (e.g. HoldShort pauses the tone and waits for Continue), so <c>SetState</c> drops
    /// any still-pending name the instant guidance LEAVES Taxiing rather than letting it wait,
    /// unflushed, for a state this method never runs in again (PR #238 review, Important 1 --
    /// see <c>SetState</c>'s own remarks for the hold-short scenario that motivated it). That
    /// guarantee is about STATE EXITS only, though (PR #238 review, Minor E correction) -- a
    /// pending name can still survive a RECALCULATION that stays entirely inside Taxiing
    /// (<c>TryRecalculateRoute</c> never calls <c>SetState</c>, so it never trips that hook),
    /// which is exactly the case <see cref="TaxiwayChangeGate.ShouldSpeakDeferred"/>'s extra
    /// check below exists for.
    ///
    /// <paramref name="currentTaxiwayName"/> is read fresh from the CURRENT segment at flush
    /// time, never assumed equal to the pending value: a recalculation can replace <c>_route</c>
    /// and reset <c>_currentSegmentIndex</c> without going through
    /// <see cref="AnnounceOrDeferTaxiwayChange"/> at all, so the deferred name can go stale
    /// without anything else clearing it. A stale name -- the route has since moved on to a
    /// DIFFERENT current taxiway -- is discarded SILENTLY (per the fix's resolution:
    /// announcing the wrong taxiway is worse than staying quiet about a change the pilot will
    /// hear about anyway the next time the taxiway actually changes, or never needed to hear
    /// about because the aircraft stayed on the one already announced) -- and, because it was
    /// never actually spoken, <c>_lastAnnouncedTaxiway</c> is left untouched on a discard so
    /// that name remains eligible to be announced again later (Minor 1).
    ///
    /// <para>A SECOND, narrower silent case (PR #238 review, Important C): the pending name can
    /// still match the current taxiway and yet already have been SPOKEN by something else --
    /// concretely, a same-frame-or-later recalculation whose new route starts on the very
    /// taxiway that was deferred, and which stamps <c>_lastAnnouncedTaxiway</c> itself as part
    /// of its own "Route changed. Now via ..." sentence. Speaking the deferred name there would
    /// both repeat something the pilot was just told and, because <c>AnnounceInstruction</c> is
    /// an interrupting <c>AnnounceImmediate</c>, risk cutting that sentence off mid-word -- the
    /// one sentence naming which runways the new route crosses. <see
    /// cref="TaxiwayChangeGate.ShouldSpeakDeferred"/> is what tells the two silent cases apart
    /// from the one case that must still speak; see its own remarks.</para>
    /// </summary>
    private void FlushPendingTaxiwayAnnouncement(string? currentTaxiwayName)
    {
        if (_pendingTaxiwayAnnouncement == null) return;
        if (MSFSBlindAssist.Utils.SimClock.UtcNow < _startChatterSuppressUntil) return; // still waiting

        string pending = _pendingTaxiwayAnnouncement;
        _pendingTaxiwayAnnouncement = null; // one-shot delivery either way
        if (TaxiwayChangeGate.ShouldSpeakDeferred(pending, currentTaxiwayName, _lastAnnouncedTaxiway))
        {
            AnnounceInstruction($"Taxiway {pending}.");
            // Only now -- actually spoken -- does it become the dedupe record (Minor 1).
            _lastAnnouncedTaxiway = pending;
        }
    }

    // Bounds on the detour a holding-point pin may add. A pinned route is EXPECTED to be
    // longer than the free-choice one — the pilot asked for a specific stub and that is the
    // feature, so these are deliberately loose (they match the recalc sanity gate). They
    // exist only to reject a pin so large the snapped hold node cannot be the line the
    // pilot meant, in which case the un-pinned route is the safer answer.
    private const double HOLD_PIN_MAX_RATIO = 2.0;
    private const double HOLD_PIN_MAX_PAD_M = 500.0;

    /// <summary>
    /// Reroutes the given route so it passes THROUGH the chosen named holding point's own node
    /// (<see cref="_holdingPointHoldNodeId"/>), returning null to keep the free route when the
    /// pin is unnecessary or unreasonable.
    /// <para>A pinned route is EXPECTED to be longer than the free one — that is the point of
    /// naming a holding point — so <see cref="HOLD_PIN_MAX_RATIO"/>/<see cref="HOLD_PIN_MAX_PAD_M"/>
    /// must never be re-tightened into a "shortest wins" rule; they only reject a pin that has
    /// gone wild.</para>
    /// </summary>
    private TaxiRoute? ApplyHoldingPointPin(
        TaxiRouter router, TaxiRoute route, int startNodeId, int destinationNodeId,
        List<string>? taxiwaySequence, out bool rejected)
    {
        // rejected: the pin was needed and could not be made (no path through the point, or one
        // gone wild) — as opposed to unnecessary (the route already passes it).
        rejected = false;
        int holdNodeId = _holdingPointHoldNodeId;
        if (_graph == null || holdNodeId == 0) return null;
        if (!_graph.Nodes.ContainsKey(holdNodeId)) return null;
        if (holdNodeId == destinationNodeId || holdNodeId == startNodeId) return null;

        // Already on the route — nothing to pin.
        foreach (var seg in route.Segments)
            if (seg.FromNode.NodeId == holdNodeId || seg.ToNode.NodeId == holdNodeId)
                return null;

        var toHold = (taxiwaySequence is { Count: > 0 })
            ? router.FindConstrainedPath(startNodeId, holdNodeId, taxiwaySequence)
            : router.FindShortestPath(startNodeId, holdNodeId);
        var fromHold = router.FindShortestPath(holdNodeId, destinationNodeId);
        if (toHold == null || toHold.Segments.Count == 0 || fromHold == null || fromHold.Segments.Count == 0)
        {
            rejected = true;
            return null;
        }

        var pinned = router.Concatenate(toHold, fromHold);
        if (pinned == null || pinned.Segments.Count == 0) { rejected = true; return null; }

        if (pinned.TotalDistanceMeters > route.TotalDistanceMeters * HOLD_PIN_MAX_RATIO + HOLD_PIN_MAX_PAD_M)
        {
            rejected = true;
            _guidanceLog.Info(
                $"Holding-point pin REJECTED (node {holdNodeId}): pinned {pinned.TotalDistanceMeters:F0} m " +
                $"vs free {route.TotalDistanceMeters:F0} m.");
            return null;
        }

        pinned.ConstrainedFallbackReason = toHold.ConstrainedFallbackReason;
        // Concatenate builds a fresh route: carry the name over, or the summary reads "Route to  via…".
        pinned.DestinationName = route.DestinationName;
        _guidanceLog.Info(
            $"Holding-point pin applied via node {holdNodeId}: {pinned.TotalDistanceMeters:F0} m " +
            $"(free route {route.TotalDistanceMeters:F0} m).");
        return pinned;
    }

    /// <summary>
    /// Marks hold-short points at the end of each user-specified taxiway in the route.
    /// </summary>
    /// <summary>
    /// For a runway-destination route, truncates the route at the last HoldShort /
    /// ILSHoldShort node before the runway and tags the (new) final segment as a
    /// hold-short point. This ensures the pilot stops at the hold-short line, not
    /// on the runway itself, and gets the 300/150/50 ft countdown on approach.
    /// Safe to call multiple times — idempotent when already truncated.
    /// </summary>
    private void TruncateToHoldShort(TaxiRoute route, string destinationName, bool preferIlsHold = false)
    {
        _lastRunwayHoldChoice = RunwayHoldChoice.None;
        if (route.Segments.Count == 0) return;

        // Pass 1: find the latest (closest-to-runway) ILS hold-short and plain
        // hold-short the route passes through.
        //
        // Only a hold that can BE this runway's hold line counts: within DEST_HOLD_NEAR_M of the
        // runway entry (route distance), or within DEST_HOLD_MAX_BACK_M when it names this runway
        // (either end) or no runway at all. The latest hold ANYWHERE on the route used to win, so a
        // route that passed another runway's hold — or this runway's far-end hold — and then taxied on
        // stopped there: WIII 06 held at "runway 07L at N8M" 612-868 m out, RPLL 31 at the 13 end
        // 2.5 km out, OTBH 16L 6 km out (VirtualPilot 2026-09-18: 26 of ~2,000 departures). With no
        // such hold the synthetic back-off below places the stop, as at any hold-less runway.
        double[] toEnd = new double[route.Segments.Count];
        {
            double acc = 0;
            for (int i = route.Segments.Count - 1; i >= 0; i--)
            {
                toEnd[i] = acc;
                acc += route.Segments[i].DistanceMeters;
            }
        }
        string? destDesignator = destinationName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? RouteRunwayCrossings.NormalizeDesignator(destinationName.Substring(7).Trim()) : null;
        bool CanBeDestinationHold(int i)
        {
            if (destDesignator == null || toEnd[i] <= DEST_HOLD_NEAR_M) return true;
            if (toEnd[i] > DEST_HOLD_MAX_BACK_M) return false;
            var named = RouteRunwayCrossings.ExtractRunwayDesignators(route.Segments[i].HoldShortRunway);
            string recip = RouteRunwayCrossings.NormalizeDesignator(RouteRunwayCrossings.Reciprocal(destDesignator));
            return named.Count == 0 || named.Any(d => d == destDesignator || d == recip);
        }

        int truncateAtIHS = -1;
        int truncateAtHS  = -1;
        for (int i = route.Segments.Count - 1; i >= 0; i--)
        {
            var to = route.Segments[i].ToNode;
            if (to == null) continue;
            if ((to.Type == TaxiNodeType.ILSHoldShort || to.Type == TaxiNodeType.HoldShort) && !CanBeDestinationHold(i)) continue;
            if (to.Type == TaxiNodeType.ILSHoldShort && truncateAtIHS < 0) truncateAtIHS = i;
            else if (to.Type == TaxiNodeType.HoldShort && truncateAtHS < 0) truncateAtHS = i;
            if (truncateAtIHS >= 0 && truncateAtHS >= 0) break;
        }

        // Hold-line selection between the full-length hold (HS, closest to the
        // runway) and the CAT III / ILS hold (IHS, further back to protect the
        // ILS critical area) is delegated to RunwayHoldShortSelector — the pure
        // decision core that carries the full rationale (default full-length =
        // user decision 2026-07; the 150 m same-approach gate = the OMDB
        // 30R-via-N12 transit-IHS fix) and is pinned by
        // RunwayHoldShortSelectorTests. The separation between the two holds is
        // only consulted in the LVP branch, when the IHS sits behind the HS.
        double holdSepM = double.MaxValue;
        if (truncateAtIHS >= 0 && truncateAtHS >= 0)
        {
            var ihsNode = route.Segments[truncateAtIHS].ToNode!;
            var hsNode  = route.Segments[truncateAtHS].ToNode!;
            holdSepM = TaxiGraph.FastDistanceMeters(
                ihsNode.Latitude, ihsNode.Longitude, hsNode.Latitude, hsNode.Longitude);
        }
        var (truncateAt, holdChoice) = RunwayHoldShortSelector.Select(
            truncateAtIHS, truncateAtHS, holdSepM, preferIlsHold);
        _lastRunwayHoldChoice = holdChoice;

        // Pass 1.25: a navdata hold node ON runway pavement is not a hold. EFHK 22R's WD hold
        // sits 22 m from the centreline of a 60 m runway, and the route reaches that pavement
        // two legs earlier, so the pilot was told "Hold short of Runway 22R" standing on it
        // (VirtualPilot 2026-09-18; KSLC 14 and ZSSS 18L the same). Move the stop back to the
        // latest route node clear of every runway (half-width + RunwayClearMarginM) — the rule
        // the crossing holds already follow (RouteRunwayCrossings.ResolveHoldStop). Only ever
        // EARLIER, within HOLD_OFF_PAVEMENT_LOOKBACK_M; none found → unchanged.
        // requireClear: the stop must be RunwayClearMarginM beyond every runway edge, not merely off
        // the pavement — for the synthetic back-off, which has no painted line behind it (VIDP 27 via
        // D6, a shallow entry: the back-off node sat on the edge, and the aircraft stopped on it).
        // A navdata hold keeps the on-pavement trigger; its paint is where the scenery put it.
        int MoveOffRunwayPavement(int at, bool requireClear = false)
        {
            if (at < 0 || _graph == null || route.Segments[at].ToNode is not { } holdNode) return at;
            var shapes = _graph.RunwayCenterlines.Where(c => c != null).Select(RunwayShape.For).ToList();
            bool needsMove = requireClear
                ? shapes.Any(sh => sh.Contains(holdNode.Latitude, holdNode.Longitude, RolloutExitGate.RunwayClearMarginM))
                : RouteRunwayCrossings.RunwayUnder(_graph.RunwayCenterlines, holdNode.Latitude, holdNode.Longitude) != null;
            if (!needsMove) return at;
            double back = 0;
            int moved = -1;
            for (int k = at - 1; k >= 0; k--)
            {
                back += route.Segments[k + 1].DistanceMeters;
                if (back > HOLD_OFF_PAVEMENT_LOOKBACK_M) break;
                var n = route.Segments[k].ToNode;
                if (n == null) continue;
                if (shapes.All(sh => !sh.Contains(n.Latitude, n.Longitude, RolloutExitGate.RunwayClearMarginM)))
                {
                    moved = k;
                    break;
                }
            }
            try
            {
                _guidanceLog.Info(moved >= 0
                    ? $"Destination hold node {holdNode.NodeId} is on (or at the edge of) runway pavement; stop moved back {back:F0} m to node {route.Segments[moved].ToNode!.NodeId}"
                    : $"Destination hold node {holdNode.NodeId} is on (or at the edge of) runway pavement; no clear node within {HOLD_OFF_PAVEMENT_LOOKBACK_M:F0} m, left unchanged");
            }
            catch { }
            return moved >= 0 ? moved : at;
        }
        // Pass 1.5 (named-holding-point stop, 2026-08-31 EGKK): the pilot EXPLICITLY
        // picked a named holding point and the scenery carries no HS/IHS node anywhere
        // on the route (EGKK's whole east loop has none — A1/A2 exist only as
        // OSM-resolved points). We know exactly where the picked line is: its node is
        // pinned INTO the route (ApplyHoldingPointPin), so stop THERE, by name, rather
        // than falling through to the synthetic back-off below — which measures 60 m
        // from the runway ENTRY and can land tens of metres past the paint the pilot
        // was told to hold at. Live failure this closes: told "hold at A2", the app
        // had nothing attached to A2's node and the pilot rolled through it with no
        // callout. Navdata HS/IHS placement stays authoritative when it exists
        // (Pass 1 wins, and the resolver snaps to designated navdata nodes first);
        // this consumes the pilot's OWN selection, so it is not the banned blanket
        // OSM hold-short sharpening — an unpicked OSM point still stops nothing.
        bool stopAtPickedPoint = false;
        if (truncateAt < 0 && _holdingPointHoldNodeId != 0)
        {
            var toNodeIds = new List<int>(route.Segments.Count);
            foreach (var s in route.Segments) toNodeIds.Add(s.ToNode?.NodeId ?? -1);
            truncateAt = RunwayHoldShortSelector.SelectNamedHoldingPointStop(
                toNodeIds, _holdingPointHoldNodeId);
            stopAtPickedPoint = truncateAt >= 0;
        }

        // Pass 1.25 runs AFTER Pass 1.5, not before it. Placed first it only ever saw
        // Pass 1's navdata hold, because Pass 1.5 is gated on `truncateAt < 0` — i.e. it
        // runs only where 1.25 had nothing to do. That left the PICKED named holding
        // point as the one destination-hold path never checked for sitting on runway
        // pavement, and that node is not guaranteed clear: InsertHoldingPointNodeOnEdge
        // projects an OSM painted line onto a taxi edge within 5 m, and the documented
        // OSM-vs-navdata offset runs up to ~14 m RUNWAY-WARD. At a field with a close-in
        // hold line — the EFHK 22R / KSLC 14 / ZSSS 18L shape this pass was added for —
        // a pilot who picked "hold at A2" was counted down and told to stop while
        // standing on the pavement. It is a no-op when the node is already clear, so
        // moving it costs the navdata path nothing.
        int beforePavementCheck = truncateAt;
        truncateAt = MoveOffRunwayPavement(truncateAt);
        // If it DID move, the stop is no longer the picked line, so it must not be
        // announced as one: "Hold short of Runway 26L at holding point A2" told to a
        // pilot stopped short of A2 names a line they have not reached.
        if (stopAtPickedPoint && truncateAt != beforePavementCheck) stopAtPickedPoint = false;

        // Pass 2 (universal-DB fallback): if the graph has no HS/IHS nodes on
        // this runway at all (common at small airports, new-numbered runways,
        // and older navdatareader snapshots that lack hold-short data), fall
        // back to a synthetic back-off distance from the runway threshold.
        // ICAO Annex 14 Table 3-2 runway-holding-position distances from
        // threshold range 30 m (Code A) to 90 m (Code E/F) for non-precision;
        // ILS-critical-area holds run 90–107.5 m. 60 m is a conservative
        // middle ground that keeps the aircraft off the runway for any code
        // short of a full CAT II/III ILS hold.
        const double SYNTHETIC_BACKOFF_M = 60.0;
        // Backoff reference: the point where the route MEETS THE RUNWAY — the destination
        // node — NOT the lineup point. For an ordinary full-length departure the two
        // coincide, so this is a no-op there. They diverge by hundreds of metres on three
        // shapes, and on all three the lineup point is the wrong end to measure from:
        //   • FULL-LENGTH BACKTRACK: the lineup point is the FAR threshold, past the
        //     route's actual end (the intermediate entrance).
        //   • NAMED HOLDING POINT: the route is pinned through a stub whose entry can sit
        //     well back from the lineup spot.
        //   • A runway whose lineup point the taxi network does not reach, where
        //     TaxiGraph.FindRunwayLineupEntryNode retargets the destination to a real
        //     entrance: measured over the whole DB, 81 of 81 retargeted runway ends put
        //     that entrance >= SYNTHETIC_BACKOFF_M from the lineup point.
        // Measuring from the lineup point on any of them makes the FIRST candidate (the
        // route's last node, which is ON the pavement) clear the back-off, so nothing is
        // truncated and that on-runway node is tagged "Hold short of Runway X" below — a
        // blind pilot told to stop on the runway. The index choice is delegated to
        // RunwayHoldShortSelector.SelectSyntheticBackoff, whose matrix pins that
        // "nothing far enough back" must stay -1 (tag nothing; HandleArrival's
        // runway-arrival fallback owns the stop) rather than degrading to the last segment.
        Database.Models.TaxiNode? entryNode = null;
        if (_destinationNodeId != 0 && _graph != null)
            _graph.Nodes.TryGetValue(_destinationNodeId, out entryNode);
        bool hasRunwayEntry = entryNode != null;
        if (truncateAt < 0 && _hasLineupTarget)
        {
            var candidates = new List<(int Index, double Lat, double Lon)>(route.Segments.Count);
            for (int i = 0; i < route.Segments.Count; i++)
            {
                var to = route.Segments[i].ToNode;
                if (to == null) continue;
                candidates.Add((i, to.Latitude, to.Longitude));
            }
            truncateAt = RunwayHoldShortSelector.SelectSyntheticBackoff(
                candidates,
                hasRunwayEntry, entryNode?.Latitude ?? 0.0, entryNode?.Longitude ?? 0.0,
                _lineupTargetLat, _lineupTargetLon,
                SYNTHETIC_BACKOFF_M);
            // A back-off measured in a straight line can still sit on runway pavement (an entry
            // taxiway drawn along the runway edge): the same move-back as a navdata hold.
            truncateAt = MoveOffRunwayPavement(truncateAt, requireClear: true);
        }

        route.RunwayEntryStub = null;
        route.RunwayEntryStubAlsoOn = new List<string>();
        if (truncateAt >= 0 && truncateAt < route.Segments.Count - 1)
        {
            // Keep what is cut: the entry stub the lineup tone follows onto the runway.
            var stub = new List<(double Lat, double Lon)>();
            for (int i = truncateAt; i < route.Segments.Count; i++)
                if (route.Segments[i].ToNode is { } sn) stub.Add((sn.Latitude, sn.Longitude));
            // Kept only when it really arrives on the destination runway's pavement: a route that
            // "reaches" the runway at a node far to its side (KVPS 12: 900 m off) has a tail that
            // leads AWAY from the runway, and following it would take the pilot away too.
            var alsoOn = RunwaysOnEntryStub(stub.Count >= 2 ? stub : null, destDesignator, out bool reachesRunway);
            route.RunwayEntryStub = reachesRunway ? stub : null;
            route.RunwayEntryStubAlsoOn = reachesRunway ? alsoOn : new List<string>();
            route.Segments.RemoveRange(truncateAt + 1, route.Segments.Count - truncateAt - 1);
            double total = 0;
            foreach (var s in route.Segments) total += s.DistanceMeters;
            route.TotalDistanceMeters = total;
        }

        // Only tag the last segment if we actually found a truncation point
        // (real HS/IHS node OR synthetic back-off). If neither succeeded — no
        // hold-short node on the runway AND no lineup target to back off from
        // — tagging the untruncated last segment would announce "Hold short"
        // at a node that is physically on the runway. That's worse than
        // letting HandleArrival handle the runway-arrival fallback, which
        // already defaults to HoldShort via _hasLineupTarget && _isRunwayLineup.
        if (truncateAt < 0) return;

        // Tag the (now-last) segment so the 300/150/50 ft hold-short countdown fires.
        // AdvanceSegment explicitly skips the last segment, so this does NOT cause a
        // double HandleHoldShort — HandleArrival owns the runway-destination flow.
        var lastSeg = route.Segments[^1];
        lastSeg.IsHoldShortPoint = true;
        // The scenery names a hold line after the NEARER runway end, so the destination hold can
        // carry the other designator of the pilot's own runway: KORD Y4 counted down "Hold short
        // runway 22L at Y4" and then stopped with "Hold short of Runway 04R" (VirtualPilot
        // 2026-09-18). Swap just that designator; a label naming a DIFFERENT runway is left alone.
        if (!string.IsNullOrEmpty(lastSeg.HoldShortRunway)
            && RouteRunwayCrossings.ExtractRunwayDesignator(lastSeg.HoldShortRunway) is { } labelled
            && destinationName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase))
        {
            string pilotDesignator = destinationName.Substring(7).Trim();
            string recip = RouteRunwayCrossings.Reciprocal(pilotDesignator);
            if (labelled == RouteRunwayCrossings.NormalizeDesignator(recip))
                lastSeg.HoldShortRunway = RouteRunwayCrossings.ComposeCrossingLabel(
                    lastSeg.HoldShortRunway, recip, pilotDesignator) ?? lastSeg.HoldShortRunway;
        }
        foreach (string other in route.RunwayEntryStubAlsoOn)
            lastSeg.HoldShortRunway = RouteRunwayCrossings.ComposeSharedLabel(
                string.IsNullOrEmpty(lastSeg.HoldShortRunway) ? destinationName : lastSeg.HoldShortRunway, other)
                ?? lastSeg.HoldShortRunway;
        if (string.IsNullOrEmpty(lastSeg.HoldShortRunway))
            lastSeg.HoldShortRunway = stopAtPickedPoint && _holdingPointName.Length > 0
                // Spoken by the countdown as "Hold short of Runway 26L at holding
                // point A2 …" — the pilot hears the stop is the line they named.
                ? $"{destinationName} at holding point {_holdingPointName}"
                : destinationName;
    }

    /// <summary>
    /// The runways other than the destination whose pavement a destination entry stub touches, up to
    /// and including the point where it first reaches the destination runway (at an intersection of
    /// two runways the aircraft is on both). Sampled every few metres along the stub.
    /// <paramref name="reachesRunway"/> says whether it reaches the destination runway at all.
    /// </summary>
    private List<string> RunwaysOnEntryStub(List<(double Lat, double Lon)>? stub, string? destDesignator, out bool reachesRunway)
    {
        reachesRunway = false;
        var found = new List<string>();
        if (stub is not { Count: >= 2 } || destDesignator == null || _graph == null) return found;
        var shapes = _graph.RunwayCenterlines.Where(c => c != null).Select(c => (Line: c, Shape: RunwayShape.For(c))).ToList();
        for (int i = 1; i < stub.Count; i++)
        {
            double len = TaxiGraph.FastDistanceMeters(stub[i - 1].Lat, stub[i - 1].Lon, stub[i].Lat, stub[i].Lon);
            int steps = Math.Max(1, (int)Math.Ceiling(len / 5.0));
            for (int k = 1; k <= steps; k++)
            {
                double t = (double)k / steps;
                double lat = stub[i - 1].Lat + (stub[i].Lat - stub[i - 1].Lat) * t;
                double lon = stub[i - 1].Lon + (stub[i].Lon - stub[i - 1].Lon) * t;
                bool onDest = false;
                foreach (var (line, shape) in shapes)
                {
                    if (!shape.Contains(lat, lon, 0.0)) continue;
                    if (RouteRunwayCrossings.CenterlineHasDesignator(line, destDesignator)) { onDest = true; continue; }
                    // The END the stub actually rolls onto, not Name1. A stub reaching the
                    // 35 end of a 17/35 strip announced "and runway 17" — the reciprocal-
                    // naming trap LabelDesignatorFor and ComposeCrossingLabel's `preferred`
                    // both exist to avoid, and the same one the KORD 22L/04R re-label fixes.
                    var (along, _) = shape.Project(lat, lon);
                    string name = shape.NameAt(along);
                    // An unnamed centerline has nothing to announce. It must be skipped, not
                    // appended blank: CenterlineHasDesignator is false for "", so the dedup
                    // below can never match a blank entry and every ~5 m sample adds another
                    // — and the caller folds each through ComposeSharedLabel, which falls
                    // through its guards on an empty name and returns "<X> and runway ",
                    // so the destination hold becomes "Runway 22L and runway and runway …",
                    // spoken verbatim by the 300/150/50 ft countdown. TaxiGraph skips
                    // unnamed centerlines for the same reason.
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!found.Any(f => RouteRunwayCrossings.CenterlineHasDesignator(line, f)))
                        found.Add(name);
                }
                if (onDest) { reachesRunway = true; return found; }
            }
        }

        // A stub that ends ON the destination runway's centerline just beyond the end its data
        // records has reached the runway too. Taxi2Gate LFPG records 26R as starting at its
        // displaced threshold, so T12 — which meets the real pavement 47 m before that — never
        // "entered" it: the stub was dropped and the lineup tone cut across the grass from the
        // hold 98 m to the side. Bounded to the runway's own half-width off the extended
        // centerline and ENTRY_STUB_BEYOND_END_M past the recorded end, so a route that "reaches"
        // a runway at a node well to its side (KVPS 12, 900 m off) still keeps no stub.
        var (endLat, endLon) = stub[^1];
        foreach (var (line, shape) in shapes)
        {
            if (!RouteRunwayCrossings.CenterlineHasDesignator(line, destDesignator)) continue;
            var (along, lateral) = shape.Project(endLat, endLon);
            if (Math.Abs(lateral) <= shape.HalfWidthMeters
                && along >= shape.ExtentMinMeters - ENTRY_STUB_BEYOND_END_M
                && along <= shape.ExtentMaxMeters + ENTRY_STUB_BEYOND_END_M)
            {
                reachesRunway = true;
                break;
            }
        }
        return found;
    }

    private const double ENTRY_STUB_BEYOND_END_M = 100.0;

    /// <summary>
    /// The automatic hold-short passes that must run on EVERY route the manager adopts —
    /// the automatic runway holds (every entry and crossing, and a start hold when the phase
    /// allows one), their log line, and the Progressive Taxi strip of the crossing the pilot
    /// is already cleared for.
    ///
    /// <para>SINGLE OWNER, ON PURPOSE. These previously ran only in <c>LoadRoute</c>, so an
    /// off-route recalculation silently produced a route with no crossing hold-shorts at all.
    /// PHNL 2026-09-03: the route to 04R via D crossed 26R, 04L and 04R and was correctly
    /// tagged at build time — the summary named all three — then a recalc 88 s later, with the
    /// aircraft still ~30 m from the stand, replaced the route and dropped every one of them.
    /// The aircraft crossed all three runways at 13-19 kt with no hold-short, no countdown and
    /// no pause. That is the exact failure FAA AIM 4-3-18 / ICAO Doc 4444 and this codebase's
    /// "never disable the auto-inserted runway-crossing hold-shorts" invariant exist to
    /// prevent. Any future path that adopts a route must call THIS, not the pass directly.</para>
    /// </summary>
    /// <param name="phase">"load", "recalc" or "touchdown" (a route adopted for the landing rollout) — recorded in the log line so they are
    /// separable. The recalc produced no line at all before, which is why it took a segment-
    /// cursor reset to prove it had even happened. It is a LABEL and nothing else: the start hold
    /// (<see cref="TaxiRoute.StartHoldRunway"/>) is decided by the pass itself, from the aircraft's
    /// position and <paramref name="recalculation"/> (PR #238 deferred finding §2). The old
    /// re-derivation (<c>allowStartHold: phase == "load"</c>) made a typo in this string silently
    /// disable start holds with no compile error.</param>
    /// <param name="recalculation">True from <see cref="TryRecalculateRoute"/> only: a recalculated
    /// route never starts held (<see cref="RouteRunwayCrossings.AircraftPosition.MayStartHeld"/>). An
    /// explicit bool, not the phase string, and not a ground-speed gate — that gate read a speed the
    /// manager holds at 0 on every fresh Calculate, and sat 1 kt above the off-route threshold, so a
    /// recalc at 2-3 kt could still start held (PR #243 review).</param>
    private void ApplyAutoHoldShortPasses(
        TaxiRoute route, bool isRunwayDestination, string destinationName,
        double aircraftLat, double aircraftLon, string phase,
        IReadOnlyList<TaxiRouteRunwayEvent>? userPickEvents = null,
        bool recalculation = false)
    {
        // Entries and crossings of every runway, one hold each, all recorded on route.RunwayEvents.
        // The aircraft's position is the route's first point and decides which stops it has already
        // passed; a start hold is refused while it stands within the clear margin of any runway, and
        // on a recalculation outright — that route is built from an aircraft already committed to
        // where it is going, and a stop where it stands would land on top of "Route changed".
        if (_graph != null)
        {
            RouteRunwayCrossings.InsertRunwayHoldShorts(
                route, _graph.RunwayCenterlines,
                isRunwayDestination ? destinationName : "",
                new RouteRunwayCrossings.AircraftPosition(aircraftLat, aircraftLon, MayStartHeld: !recalculation));

            // The pilot's own picks, merged back in: this pass OWNS the event list and resets it, so
            // a pick it skips — the destination-strip arrival — would otherwise be named nowhere and
            // stop the pilot at a hold they were never told about (PR #238 §7). De-duplicated by
            // runway and kind, so a passage the pass did record is not counted twice.
            RouteRunwayCrossings.MergeUserPickEvents(route, userPickEvents);
        }

        // One line per route ADOPTED. Answering "did that route really drive across 08L?" for the
        // 2026-08-27 KATL arrival meant reconstructing segment coordinates against the navdata by
        // hand; the pipeline already knows the answer at build time. `phase` distinguishes the two
        // adopters — the recalc wrote nothing at all before 2026-09, which is why proving a
        // recalculation had even happened at PHNL took a segment-cursor reset rather than a log line.
        //
        // ADOPTED, not built: this runs from AdoptRoute, below the recalculation's no-op guard,
        // so a recalc the guard discards writes nothing. Written before the Progressive strip, so
        // it records the holds the pass placed.
        _guidanceLog.Info(RouteRunwayCrossings.DescribeForLog(phase, destinationName, route));

        // Progressive "after crossing" terminator: the pilot is cleared to cross the terminator
        // runway, so strip the holds for it alone (a stop shared with another runway stays).
        if (_progressiveTerminator?.ClearedCrossingRunway is string clearedRwy)
            RouteRunwayCrossings.StripClearedCrossing(route, clearedRwy);
    }

    /// <summary>
    /// An explicit runway pick that met its runway on the route but found no stop before the
    /// automatic pass ran — retried once that pass has placed its own stops.
    /// </summary>
    private sealed record DeferredRunwayPick(
        TaxiGraph.RunwayCenterline Runway, string RunwayId, int RunStart, string TaxiwayName);

    /// <summary>
    /// THE route-adoption seam: every route the manager takes on becomes the live route HERE,
    /// and the automatic hold-short passes run as part of that, so the two can never be
    /// separated by a future caller.
    ///
    /// <para>The PHNL 2026-09-03 defect was one adopter forgetting the pass — the recalculation
    /// replaced a correctly tagged route with one carrying no crossing hold-shorts at all, and
    /// the aircraft crossed 26R, 04L and 04R at 13-19 kt with no callout. Making the passes a
    /// single method fixed the duplication of their BODY; this fixes the duplication of their
    /// CALL, which is the half that actually goes missing. Assign <c>_route</c> through this
    /// method and nowhere else.</para>
    ///
    /// <para>The aircraft position is required, not optional: the crossing pass treats it as the
    /// route's first point, and must know which candidate stop points the aircraft has already
    /// rolled past. A recalculation or a landing re-route is built from the live position, and a
    /// fresh hold on pavement the aircraft is already standing on ("Stop. Hold short of runway
    /// 26R" while ON 26R) is a stop in the worst possible place. "Passed" is measured along the
    /// route (<see cref="RouteRunwayCrossings.RouteProgressMeters"/>): a caller at a standstill
    /// passes its own position, and only a stop the aircraft is already more than
    /// <see cref="RouteRunwayCrossings.StopPassedToleranceMetres"/> past is dropped.</para>
    /// </summary>
    private void AdoptRoute(
        TaxiRoute route, bool isRunwayDestination, string destinationName,
        double aircraftLat, double aircraftLon, string phase,
        IReadOnlyList<TaxiRouteRunwayEvent>? userPickEvents = null,
        bool recalculation = false)
    {
        ApplyAutoHoldShortPasses(
            route, isRunwayDestination, destinationName, aircraftLat, aircraftLon, phase, userPickEvents,
            recalculation);
        LogStandBridgeSegments(route, phase);
        // A start-hold sentence belongs to the route it was composed for; a new route composes its own.
        LastRouteStartHoldCue = null;
        // So is a paired-crossing double-Continue: its segment index means nothing on a new route.
        _pairedCrossingSegIdx = -1;
        _pairedCrossingWindowUntil = DateTime.MinValue;
        _route = route;
    }

    /// <summary>
    /// One diagnostic line per fabricated stand bridge (TaxiGraph.StandBridgePathType) an adopted route
    /// uses, so "why did guidance steer me across here" can be answered from taxi_guidance.log.
    /// </summary>
    private void LogStandBridgeSegments(TaxiRoute route, string phase)
    {
        if (_graph == null) return;
        foreach (var seg in route.Segments)
        {
            if (seg.FromNode == null || seg.ToNode == null) continue;
            var edge = _graph.GetEdge(seg.FromNode.NodeId, seg.ToNode.NodeId);
            if (edge == null || !TaxiGraph.IsStandBridge(edge)) continue;
            _guidanceLog.Info(
                $"Route uses stand bridge: phase={phase} nodes={seg.FromNode.NodeId}->{seg.ToNode.NodeId} " +
                $"from={seg.FromNode.Latitude:F6},{seg.FromNode.Longitude:F6} " +
                $"to={seg.ToNode.Latitude:F6},{seg.ToNode.Longitude:F6} lenM={seg.DistanceMeters:F1}");
        }
    }

    /// <summary>
    /// Honors the pilot's explicit "Hold short of runway X" pickers from the form. For each
    /// (sequenceIndex → runway) pair the pick binds to the matching run of segments tagged with that
    /// taxiway (first run, counting repeats — KSFO D keeps its name across 10R/28L) and is honoured
    /// when the route enters or crosses X at or after that run's start. The hold is placed by the same
    /// resolver as the automatic pass and labelled as the pilot typed it
    /// (<see cref="RouteRunwayCrossings.ApplyUserRunwayHold"/>).
    ///
    /// Returns a warning string when one or more picks could not be set — the runway is not in the
    /// airport data, or the taxiway or runway is not on the route — or null when every pick was
    /// honoured. A pick that meets its runway but finds no stop is NOT warned about here: it goes to
    /// <paramref name="deferred"/>, because a runway just in front of it (intersecting or close
    /// parallel runways) gets its stop from the automatic pass that runs AFTER this, and the pick
    /// then shares it. Warning at this point said "no safe place to hold short" for a pick that the
    /// finished route holds perfectly well (review finding on PR #238, 2026-09-15).
    /// </summary>
    private string? ApplyUserRunwayHoldShorts(
        TaxiRoute route,
        List<string> taxiwaySequence,
        Dictionary<int, string> userRunwayHoldShorts,
        double aircraftLat,
        double aircraftLon,
        List<TaxiRouteRunwayEvent> placedEvents,
        List<DeferredRunwayPick> deferred)
    {
        if (_graph == null) return null;

        var unmatched = new List<string>();

        foreach (var kvp in userRunwayHoldShorts)
        {
            int seqIdx = kvp.Key;
            string runwayId = kvp.Value;

            if (seqIdx < 0 || seqIdx >= taxiwaySequence.Count) continue;
            string taxiwayName = taxiwaySequence[seqIdx];

            // The user types ONE of two reciprocal designators ("10R" / "28L") but both name the
            // same physical pavement, so the route is classified against that one runway: a pick
            // can never miss because the nearer end carries the OTHER designator. Through the
            // shared matcher, which also folds the leading zero ("9L" vs navdata's "09L").
            TaxiGraph.RunwayCenterline? targetRwy =
                RouteRunwayCrossings.FindCenterlineForDesignator(_graph.RunwayCenterlines, runwayId);
            if (targetRwy == null)
            {
                unmatched.Add($"runway {runwayId} (not in airport runway data)");
                continue;
            }

            // For duplicate-taxiway clearances ("via N, hold short 15R, N, hold short 22R, N" —
            // KBOS style), each sequence entry refers to a distinct run of that taxiway in the
            // route; scanning from the START of the run (not the end) finds crossings INTERNAL to
            // the named taxiway (KSFO D over 10R/28L keeps its name on both sides).
            int runStart = FindTaxiwayRunStart(route, taxiwaySequence, seqIdx, taxiwayName);
            if (runStart < 0)
            {
                unmatched.Add($"runway {runwayId} (taxiway {taxiwayName} not on route)");
                continue;
            }

            switch (RouteRunwayCrossings.ApplyUserRunwayHold(
                        route, targetRwy, _graph.RunwayCenterlines, runwayId, runStart,
                        placed: out var placedEvent,
                        aircraft: new RouteRunwayCrossings.AircraftPosition(aircraftLat, aircraftLon)))
            {
                // The pick's own event, merged back in after the automatic pass RESETS the list —
                // without it a pick on the destination strip, whose arrival that pass skips, was
                // named nowhere at all (PR #238 deferred finding §7).
                case RouteRunwayCrossings.UserRunwayHoldResult.Held when placedEvent != null:
                    placedEvents.Add(placedEvent);
                    break;
                case RouteRunwayCrossings.UserRunwayHoldResult.NotOnRoute:
                    unmatched.Add($"runway {runwayId} (route does not cross it after taxiway {taxiwayName})");
                    break;
                case RouteRunwayCrossings.UserRunwayHoldResult.NotHeld:
                    deferred.Add(new DeferredRunwayPick(targetRwy, runwayId, runStart, taxiwayName));
                    break;
            }
        }

        if (unmatched.Count == 0) return null;
        return $"Note: requested hold-short(s) could not be set — {string.Join("; ", unmatched)}.";
    }

    /// <summary>
    /// Retries the picks <see cref="ApplyUserRunwayHoldShorts"/> deferred, now that the automatic
    /// pass has placed its stops, and warns about any that still have no stop — the stop is already
    /// passed, there is no clear node or existing stop before the runway, or the start hold is
    /// refused. Said, never left to the log alone: the pilot programmed that stop.
    /// </summary>
    private string? RetryDeferredRunwayPicks(
        TaxiRoute route, List<DeferredRunwayPick> deferred,
        double aircraftLat, double aircraftLon)
    {
        if (_graph == null || deferred.Count == 0) return null;
        var unheld = new List<string>();
        foreach (var pick in deferred)
        {
            var result = RouteRunwayCrossings.ApplyUserRunwayHold(
                route, pick.Runway, _graph.RunwayCenterlines, pick.RunwayId, pick.RunStart,
                placed: out var placedEvent,
                aircraft: new RouteRunwayCrossings.AircraftPosition(aircraftLat, aircraftLon));
            if (result == RouteRunwayCrossings.UserRunwayHoldResult.Held && placedEvent != null)
                RouteRunwayCrossings.MergeUserPickEvents(route, new[] { placedEvent });
            else if (result != RouteRunwayCrossings.UserRunwayHoldResult.Held)
                unheld.Add($"runway {pick.RunwayId} (no safe place to hold short after taxiway {pick.TaxiwayName})");
        }
        if (unheld.Count == 0) return null;
        return $"Note: requested hold-short(s) could not be set — {string.Join("; ", unheld)}.";
    }

    private static void ApplyUserHoldShorts(TaxiRoute route, List<string> taxiwaySequence, List<int> userHoldShortIndices)
    {
        foreach (int seqIdx in userHoldShortIndices)
        {
            if (seqIdx < 0 || seqIdx >= taxiwaySequence.Count)
                continue;

            string taxiwayName = taxiwaySequence[seqIdx];

            // Occurrence-aware, mirroring ApplyUserRunwayHoldShorts: a clearance
            // that reuses a taxiway ("via N, …, N" — the KBOS crossing pattern)
            // has one sequence entry per run of that taxiway on the route. The
            // old whole-route "last matching segment" scan silently tagged the
            // SECOND traversal for a hold the user set on the FIRST row (VATSIM
            // gap analysis 2026-08-31, P2 last-occurrence bug). Anchor on the
            // run this row refers to, then take that run's last segment.
            int runStart = FindTaxiwayRunStart(route, taxiwaySequence, seqIdx, taxiwayName);
            if (runStart < 0)
                continue;
            int runEnd = runStart;
            while (runEnd + 1 < route.Segments.Count &&
                   route.Segments[runEnd + 1].TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase))
                runEnd++;

            var seg = route.Segments[runEnd];
            seg.IsHoldShortPoint = true;
            // Force the user-requested "end of taxiway X" label. Previously
            // used `??=`, which preserved any pre-existing runway hold-short
            // label — so if the taxiway happened to terminate at a real HS
            // node, the announcement said "Hold short runway 13L" when the
            // user had asked for an end-of-taxiway stop. The user's intent
            // wins: they typed this hold-short index in the form.
            seg.HoldShortRunway = $"end of taxiway {taxiwayName}";
        }
    }

    /// <summary>
    /// Start index of the route-segment run this sequence row refers to: the
    /// (n+1)-th maximal run of segments tagged <paramref name="taxiwayName"/>,
    /// where n is how many EARLIER sequence entries name the same taxiway.
    /// Returns -1 when the route has no such run (the taxiway never made it onto
    /// the built route). Shared by every per-row hold-short applier so the three
    /// flavors (end-of-taxiway, runway, taxiway) agree about which traversal a
    /// row means.
    /// </summary>
    private static int FindTaxiwayRunStart(
        TaxiRoute route, List<string> taxiwaySequence, int seqIdx, string taxiwayName)
    {
        int priorOccurrences = 0;
        for (int k = 0; k < seqIdx; k++)
        {
            if (taxiwaySequence[k].Equals(taxiwayName, StringComparison.OrdinalIgnoreCase))
                priorOccurrences++;
        }

        int runsSeen = 0;
        bool inRun = false;
        for (int i = 0; i < route.Segments.Count; i++)
        {
            bool matches = route.Segments[i].TaxiwayName.Equals(
                taxiwayName, StringComparison.OrdinalIgnoreCase);
            if (matches && !inRun)
            {
                inRun = true;
                if (runsSeen == priorOccurrences)
                    return i;
            }
            else if (!matches && inRun)
            {
                inRun = false;
                runsSeen++;
            }
        }
        return -1;
    }

    /// <summary>
    /// Applies the per-row "hold short of TAXIWAY X" picks: after the chosen
    /// row's taxiway run, guidance stops at the first junction where the route
    /// meets taxiway X — the segment whose end node carries an edge named X. This
    /// covers both shapes of the instruction: the route TURNING ONTO X (the stop
    /// lands on the segment ending at the A/X junction) and the route CROSSING X
    /// while continuing (same junction node, route carries on past it after the
    /// pilot's Continue). Stops AT the junction node — the same placement the
    /// end-of-taxiway hold uses; navdata has no intermediate hold lines for
    /// taxiway junctions to back off to.
    /// Returns a spoken note naming any pick the route cannot honour, or null.
    /// </summary>
    private string? ApplyUserTaxiwayHoldShorts(
        TaxiRoute route, List<string> taxiwaySequence, Dictionary<int, string> userTaxiwayHoldShorts)
    {
        if (_graph == null) return null;
        var unmatched = new List<string>();

        foreach (var (seqIdx, rawTarget) in userTaxiwayHoldShorts.OrderBy(k => k.Key))
        {
            if (seqIdx < 0 || seqIdx >= taxiwaySequence.Count) continue;
            string rowTaxiway = taxiwaySequence[seqIdx];
            string target = _graph.ResolveTaxiwayName(rawTarget);

            int runStart = FindTaxiwayRunStart(route, taxiwaySequence, seqIdx, rowTaxiway);
            if (runStart < 0)
            {
                unmatched.Add($"taxiway {rawTarget} (taxiway {rowTaxiway} not on route)");
                continue;
            }

            // Forward scan from the run start for the first segment whose END
            // node touches the target taxiway. Segments already ON the target
            // don't count — "hold short of X" from a row on X is a mismatch the
            // pilot should hear about, not a stop at an arbitrary X node.
            int holdSeg = -1;
            for (int i = runStart; i < route.Segments.Count; i++)
            {
                var seg = route.Segments[i];
                if (seg.TaxiwayName.Equals(target, StringComparison.OrdinalIgnoreCase))
                    break; // reached pavement ON the target without a junction hit — stop scanning
                if (seg.ToNode == null) continue;
                if (NodeTouchesTaxiway(seg.ToNode.NodeId, target))
                {
                    holdSeg = i;
                    break;
                }
            }

            if (holdSeg < 0)
            {
                unmatched.Add($"taxiway {rawTarget} (route does not meet it after taxiway {rowTaxiway})");
                continue;
            }

            var hold = route.Segments[holdSeg];
            hold.IsHoldShortPoint = true;
            // User intent wins on the label — spoken as "Hold short of taxiway X".
            hold.HoldShortRunway = $"taxiway {rawTarget}";
        }

        if (unmatched.Count == 0) return null;
        return $"Note: requested hold-short(s) not on route — {string.Join("; ", unmatched)}.";
    }

    /// <summary>True when any edge at this node is named <paramref name="taxiwayName"/>.</summary>
    private bool NodeTouchesTaxiway(int nodeId, string taxiwayName)
    {
        if (_graph == null || !_graph.Adjacency.TryGetValue(nodeId, out var edges))
            return false;
        foreach (var e in edges)
        {
            if (e.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Perpendicular (cross-track) distance in metres from the route's DESTINATION
    /// node to the runway centerline (lineup point + runway heading). This is the
    /// reachability probe for the unreachable-runway safety net: the destination
    /// node is FindNearestNode of the lineup point, so it is near the centerline
    /// when the runway is reachable and far off only when the clearance ended on a
    /// parallel taxiway with no connector node. Unlike route.Segments[^1].ToNode
    /// (the truncated hold-short, which an ILS/angled-connector hold can place far
    /// off the perpendicular), this never false-fires on a reachable runway.
    /// Returns 0 (= on centerline = reachable) when there is no lineup target or
    /// the node is missing, so non-runway destinations never arm the safety net.
    /// </summary>
    private double DestinationCrossTrackMeters(int destinationNodeId)
    {
        if (!_hasLineupTarget || _graph == null ||
            !_graph.Nodes.TryGetValue(destinationNodeId, out var destNode))
            return 0.0;
        return AbsLateralFromRunwayMeters(
            destNode.Latitude, destNode.Longitude,
            _lineupTargetLat, _lineupTargetLon, _lineupHeadingTrue);
    }

    /// <summary>
    /// Does this via-list leg name one of the airport's runways? Returns the
    /// canonical RunwayID when it does, null otherwise. Accepts a bare designator
    /// ("25", "07L") or one prefixed with RW/RWY/RUNWAY, and treats a padded
    /// leading zero as formatting, not identity ("07L" == "7L" — the same rule
    /// the SayIntentions import applies via CleanRunway). Callers must FIRST
    /// check the leg against the graph's known taxiway names: a scenery that
    /// genuinely names a taxiway "25" keeps it as a taxiway.
    /// </summary>
    internal static string? MatchRunwayDesignator(string leg, IReadOnlyList<string> runwayIds)
    {
        if (string.IsNullOrWhiteSpace(leg)) return null;
        string s = leg.Trim().ToUpperInvariant();
        if (s.StartsWith("RUNWAY", StringComparison.Ordinal)) s = s.Substring(6).TrimStart();
        else if (s.StartsWith("RWY", StringComparison.Ordinal)) s = s.Substring(3).TrimStart();
        else if (s.StartsWith("RW", StringComparison.Ordinal)) s = s.Substring(2).TrimStart();
        string norm = NormalizeDesignator(s);
        if (norm.Length == 0) return null;
        foreach (var id in runwayIds)
        {
            if (NormalizeDesignator(id) == norm)
                return id;
        }
        return null;

        static string NormalizeDesignator(string d)
        {
            d = d.Trim().ToUpperInvariant();
            // Shape gate: 1-2 digits then optionally L/C/R/W/T (water/true suffixes
            // exist in some data). Anything else is not a runway designator.
            int i = 0;
            while (i < d.Length && char.IsDigit(d[i])) i++;
            if (i == 0 || i > 2 || d.Length - i > 1) return "";
            if (d.Length > i && d[i] is not ('L' or 'C' or 'R' or 'W' or 'T')) return "";
            string digits = d.Substring(0, i).TrimStart('0');
            if (digits.Length == 0) return "";
            return digits + d.Substring(i);
        }
    }


    private const double EXIT_ANCHOR_AHEAD_MAX_M = 150.0;

    /// <summary>Virtual-pilot harness only: disable the backwards-start retry for A/B measurement. Never set in the app.</summary>
    internal static bool LegacyExitAnchorForHarness;

    /// <summary>How far back along the route a destination hold found on runway pavement may move.</summary>
    private const double HOLD_OFF_PAVEMENT_LOOKBACK_M = 200.0;
    /// <summary>A hold this close (route distance) to the runway entry is its hold line whatever it is labelled.</summary>
    private const double DEST_HOLD_NEAR_M = 200.0;
    /// <summary>No hold further back than this (route distance) from the runway entry is the destination's hold line.</summary>
    private const double DEST_HOLD_MAX_BACK_M = 1000.0;

    /// <summary>How far along a backwards-starting exit route to look for a node ahead to start from.</summary>
    private const double EXIT_ANCHOR_KINK_MAX_RUN_M = 150.0;
    /// <summary>How much longer than the backwards route a retry route may be.</summary>
    private const double EXIT_ANCHOR_MAX_EXTRA_M = 30.0;
    /// <summary>Shortest route the backwards-start retry accepts.</summary>
    private const double EXIT_ANCHOR_MIN_ROUTE_M = 30.0;
    /// <summary>
    /// How far off the nose a retry candidate may lie. At 90° the early handoff (300 ft out)
    /// picked KLAX 07L B5's far end, 106 m to the side across the grass, and the arrival
    /// backstop read the one-look route as already vacated.
    /// </summary>
    private const double EXIT_ANCHOR_MAX_OFF_NOSE_DEG = 45.0;
    /// <summary>How many ahead-of-the-nose taxiway nodes the backwards-start retry tries.</summary>
    private const int EXIT_ANCHOR_MAX_CANDIDATES = 6;
    /// <summary>How far from the aircraft such a node may be.</summary>
    private const double EXIT_ANCHOR_KINK_MAX_M = 60.0;

    /// <summary>
    /// A landing-exit route that DETOURS: more than <see cref="EXIT_ANCHOR_DETOUR_FACTOR"/> times the
    /// straight-line distance from the aircraft to its destination AND more than
    /// <see cref="EXIT_ANCHOR_DETOUR_MIN_EXTRA_M"/> longer than it. A vacate destination is a short
    /// walk off the runway; a route kilometres long to reach it started on the wrong node.
    /// </summary>
    private bool RouteDetoursFromExit(TaxiRoute route, double aircraftLat, double aircraftLon, int destinationNodeId)
    {
        if (_graph == null || !_graph.Nodes.TryGetValue(destinationNodeId, out var dest)) return false;
        double straight = TaxiGraph.FastDistanceMeters(aircraftLat, aircraftLon, dest.Latitude, dest.Longitude);
        return route.TotalDistanceMeters > straight * EXIT_ANCHOR_DETOUR_FACTOR
            && route.TotalDistanceMeters > straight + EXIT_ANCHOR_DETOUR_MIN_EXTRA_M;
    }
    private const double EXIT_ANCHOR_DETOUR_FACTOR = 3.0;
    private const double EXIT_ANCHOR_DETOUR_MIN_EXTRA_M = 500.0;

    /// <summary>
    /// When a runway route ARRIVES on its runway facing the reciprocal of the takeoff heading
    /// (<see cref="RunwayEntryDirection"/>), re-route to the nearest entrance of the same runway whose
    /// arrival faces the takeoff direction, on the same clearance: EGLL 09R via NB10 took NB10's
    /// westbound curve (the 27L entry) and asked for a ~100 degree reversal at the pavement edge. Null
    /// when the arrival is not reversed or no acceptable replacement exists; the route then stands.
    /// A replacement is accepted only when it ends ON the candidate, honours the clearance at least
    /// as well as the original, does not turn round within its last 300 m, adds at most
    /// <see cref="REVERSED_ENTRY_MAX_EXTRA_ROUTE_M"/> of taxiing and starts its takeoff run at most
    /// <see cref="REVERSED_ENTRY_MAX_RUNWAY_LOSS_M"/> further down the runway. The most runway wins.
    /// </summary>
    private (TaxiRoute Route, int NodeId, double AlongM)? TryForwardFacingRunwayEntry(
        TaxiRouter router, TaxiRoute route, int startNodeId, int destNodeId,
        List<string>? taxiwaySequence, string destinationName)
    {
        if (_graph == null || route.Segments.Count == 0 || !_hasLineupTarget) return null;
        string designator = destinationName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? destinationName.Substring(7).Trim() : destinationName.Trim();
        var cl = RouteRunwayCrossings.FindCenterlineForDesignator(_graph.RunwayCenterlines, designator);
        if (cl == null) return null;
        var shape = RunwayShape.For(cl);
        if (shape.IsDegenerate) return null;

        static List<(double Lat, double Lon)> Points(TaxiRoute r)
        {
            var pts = new List<(double, double)>(r.Segments.Count + 1)
                { (r.Segments[0].FromNode.Latitude, r.Segments[0].FromNode.Longitude) };
            foreach (var seg in r.Segments) pts.Add((seg.ToNode.Latitude, seg.ToNode.Longitude));
            return pts;
        }

        var end = route.Segments[^1].ToNode;
        if (end.NodeId != destNodeId || !shape.Contains(end.Latitude, end.Longitude, 0.0)) return null;
        double hdg = _lineupHeadingTrue;
        if (!RunwayEntryDirection.IsReversed(RunwayEntryDirection.ArrivalBearing(Points(route)), hdg)) return null;

        // Along = metres from the lineup point in the takeoff direction.
        double AlongOf(double lat, double lon)
        {
            double d = TaxiGraph.FastDistanceMeters(_lineupTargetLat, _lineupTargetLon, lat, lon);
            if (d < 0.01) return 0;
            double brg = NavigationCalculator.CalculateBearing(_lineupTargetLat, _lineupTargetLon, lat, lon);
            return d * Math.Cos(NormalizeAngle(brg - hdg) * Math.PI / 180.0);
        }
        double origAlong = AlongOf(end.Latitude, end.Longitude);
        double origLateral = Math.Abs(shape.Project(end.Latitude, end.Longitude).Lateral);
        double maxLateral = Math.Max(REVERSED_ENTRY_MAX_LATERAL_M, origLateral + 2.0);
        bool origHonoured = route.ConstrainedFallbackReason == null;

        var candidates = new List<(TaxiNode Node, double Along)>();
        foreach (var n in _graph.Nodes.Values)
        {
            if (n.NodeId == destNodeId || n.ComponentId != end.ComponentId) continue;
            if (_graph.IsHoldingPointProjectionNode(n.NodeId)) continue;
            var (_, lateral) = shape.Project(n.Latitude, n.Longitude);
            if (Math.Abs(lateral) > maxLateral || !shape.Contains(n.Latitude, n.Longitude, 0.0)) continue;
            double along = AlongOf(n.Latitude, n.Longitude);
            if (along < origAlong - REVERSED_ENTRY_MAX_RUNWAY_LOSS_M || along > origAlong + REVERSED_ENTRY_MAX_RUNWAY_LOSS_M) continue;
            candidates.Add((n, along));
        }
        foreach (var (cand, along) in candidates.OrderBy(c => c.Along))
        {
            var alt = taxiwaySequence is { Count: > 0 }
                ? router.FindConstrainedPath(startNodeId, cand.NodeId, taxiwaySequence, destinationIsRunway: true)
                : router.FindShortestPath(startNodeId, cand.NodeId);
            if (alt == null || alt.Segments.Count == 0 || alt.Segments[^1].ToNode.NodeId != cand.NodeId) continue;
            if (origHonoured && alt.ConstrainedFallbackReason != null) continue;
            if (alt.TotalDistanceMeters > route.TotalDistanceMeters + REVERSED_ENTRY_MAX_EXTRA_ROUTE_M) continue;
            var pts = Points(alt);
            if (!RunwayEntryDirection.IsForward(RunwayEntryDirection.ArrivalBearing(pts), hdg)) continue;
            if (RunwayEntryDirection.HairpinsNearEnd(pts, 300.0)) continue;
            return (alt, cand.NodeId, along);
        }
        return null;
    }

    private const double REVERSED_ENTRY_MAX_RUNWAY_LOSS_M = 400.0;
    private const double REVERSED_ENTRY_MAX_EXTRA_ROUTE_M = 400.0;
    private const double REVERSED_ENTRY_MAX_LATERAL_M = 4.0;

    /// <summary>
    /// True when the route turns more than 120° between two consecutive legs (≥ 3 m) within its
    /// first 150 m — a retry route must not trade a U-turn at the start for one a moment later
    /// (KSTL 24 D: out along a branch of D and back across its hold line).
    /// </summary>
    private static bool RouteHairpinsEarly(TaxiRoute route)
    {
        double run = 0;
        double? prev = null;
        foreach (var seg in route.Segments)
        {
            if (run > 150.0) break;
            run += seg.DistanceMeters;
            if (seg.DistanceMeters < 3.0) continue;
            if (prev is { } p && Math.Abs(NormalizeAngle(seg.BearingDegrees - p)) > 120.0) return true;
            prev = seg.BearingDegrees;
        }
        return false;
    }

    /// <summary>
    /// True when the aircraft projects beyond the END of the route's first leg — the route has
    /// started somewhere the aircraft is already past (KABQ 17 F: a one-leg retry route that the
    /// arrival backstop read as ARRIVED on the runway).
    /// </summary>
    private static bool AircraftPastFirstLeg(TaxiRoute route, double lat, double lon)
    {
        var seg = route.Segments[0];
        double cosLat = Math.Cos(lat * Math.PI / 180.0);
        double ax = (seg.FromNode.Longitude - lon) * cosLat, ay = seg.FromNode.Latitude - lat;
        double bx = (seg.ToNode.Longitude - lon) * cosLat, by = seg.ToNode.Latitude - lat;
        double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
        if (len2 < 1e-14) return false;
        return -(ax * dx + ay * dy) / len2 > 1.0;
    }

    /// <summary>
    /// True when the route's very first leg, however short, points more than 120° off the
    /// heading. Post-handoff the tone steers by the CURRENT leg's bearing, so a 2 m stub
    /// pointing back (KSTL 12R L: junction → n1197 at 328° on a 122° runway, then off along M)
    /// is spoken as "Make a U-turn" although the route itself leaves sideways.
    /// </summary>
    private static bool FirstLegPointsBack(TaxiRoute route, double headingTrue)
        => route.Segments.Count > 1
           && route.Segments[0].DistanceMeters >= 0.5
           && Math.Abs(NormalizeAngle(route.Segments[0].BearingDegrees - headingTrue)) > 120.0;

    /// <summary>
    /// True when the route's first real leg (≥ 3 m) points more than 120° off the heading —
    /// following it means turning round.
    /// </summary>
    private static bool RouteStartsBackwards(TaxiRoute route, double headingTrue)
    {
        foreach (var seg in route.Segments)
        {
            if (seg.DistanceMeters < 3.0) continue;
            return Math.Abs(NormalizeAngle(seg.BearingDegrees - headingTrue)) > 120.0;
        }
        return false;
    }

    /// <summary>
    /// True when the route's end is a hold-short node belonging to the DESTINATION runway — i.e.
    /// the route stopped exactly where a departure is supposed to stop, even though it never
    /// contained the destination node. This is the first of the two independent "the route is
    /// fine" signals guarding the ended-short warning, and it is what covers a set-back
    /// CAT II/III hold (EGKK A3 sits 162 m off the centerline) and the sparse GA fields where a
    /// legitimate hold is a long taxi from the pavement — measured over the whole fs2020 DB,
    /// 1.85 % of runway-owned hold nodes are more than <see cref="RUNWAY_REACH_MAX_WALK_M"/> of
    /// taxiing from their runway, and they are almost all small strips with minimal networks.
    /// <para>The name test is reciprocal-tolerant via <see cref="RunwayDesignatorsMatch"/>; an
    /// UNNAMED hold does not qualify (it could belong to any runway), which is deliberate — the
    /// walk test below is what covers airports whose navdata carries no hold names at all.</para>
    /// </summary>
    internal static bool RouteEndIsRunwayHold(Database.Models.TaxiNode endNode, string destinationName)
    {
        if (endNode.Type != Database.Models.TaxiNodeType.HoldShort &&
            endNode.Type != Database.Models.TaxiNodeType.ILSHoldShort)
            return false;
        if (string.IsNullOrWhiteSpace(endNode.HoldShortName)) return false;

        return RunwayDesignatorsMatch(
            endNode.HoldShortName!, RouteRunwayCrossings.StripRunwayPrefix(destinationName));
    }

    /// <summary>
    /// How much further the aircraft would have to TAXI from the route's end to be on the
    /// destination runway's pavement, or 0 when the graph has no centerline for that runway (no
    /// answer → never warn, so a graph that could not pair the runway's ends degrades to today's
    /// behaviour rather than inventing a failure).
    /// <para>Deliberately NOT the perpendicular distance to the centerline, and NOT the distance
    /// to the lineup node — see <see cref="TaxiGraph.GraphWalkToRunwayPavement"/> for why both of
    /// those give the wrong answer here. Bounded by <see cref="RUNWAY_REACH_WALK_SEARCH_M"/> and
    /// run once per route load.</para>
    /// </summary>
    private double RouteEndWalkToRunwayMeters(Database.Models.TaxiNode endNode, string destinationName)
    {
        if (_graph == null) return 0.0;
        var cl = _graph.FindCenterlineByName(destinationName);
        if (cl == null) return 0.0;

        // PositiveInfinity (no path within the search bound) is passed through UNCHANGED.
        // It used to be mapped onto RUNWAY_REACH_WALK_SEARCH_M so it would exceed the
        // threshold, but that number then reached the pilot as "about 1500 metres of
        // taxiing away" — a specific, confident, fabricated distance for a route with no
        // path at all, which a blind pilot has no way to check. RunwayReachGate compares
        // it (infinity exceeds any threshold) and gives it its own wording.
        // Where the runway is comes from the one runway shape: the runway-table pavement when it is a
        // sound line for this centerline, otherwise the start rows (docs/taxi-guidance.md). At a
        // displaced threshold the start rows sit hundreds of metres inside the pavement, and a
        // start-row window reports an aircraft standing on the runway as having no path to it.
        var shape = RunwayShape.For(cl);
        return _graph.GraphWalkToRunwayPavement(
            endNode.NodeId, shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2,
            shape.HalfWidthMeters, RUNWAY_REACH_WALK_SEARCH_M);
    }

}
