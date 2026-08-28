using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public partial class TaxiGuidanceManager
{
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
        // index → runway designator. After taxiway[seqIdx], guidance stops at
        // the route segment whose endpoint sits on that runway. Auto-detection
        // (`InsertRunwayCrossingHoldShorts`) still runs on top, so most users
        // never need this; it's the explicit override for ATC clearances where
        // the pilot wants to confirm the SPECIFIC runway named, or where the
        // auto-detect didn't fire for some reason.
        Dictionary<int, string>? userRunwayHoldShorts = null,
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
        double aircraftHeadingMagVar = 0.0)
    {
        lock (_stateLock)
        {
        try
        {
            // Store for recalculation
            _dataProvider = dataProvider;
            _destinationNodeId = destinationNodeId;
            _destinationName = destinationName;
            _icao = icao ?? "";
            _originalTaxiwaySequence = taxiwaySequence;
            _preferIlsHold = preferIlsHold;
            _backtrackDeparture = fullLengthBacktrack;
            _backtrackDepApproachAnnounced = false;
            // Assigned unconditionally (0 when absent) so a plain route can never inherit
            // the previous route's holding-point pin through the recalc path.
            _holdingPointHoldNodeId = holdingPointHoldNodeId ?? 0;

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
                    return "No taxi path data available for this airport.";

                var parking = ResolveParkingSpots(dataProvider, icao!);
                var starts = dataProvider.GetRunwayStarts(icao!);

                // Runways let the builder repair laterally-bogus start rows before they
                // reach the centerlines (TaxiGraph.SnapStartToRunwayCenterline).
                _graph = TaxiGraph.Build(paths, parking, starts, dataProvider.GetRunways(icao!));
            }

            if (_graph.Nodes.Count == 0)
                return "Could not build taxi graph for this airport.";

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
                return "Destination node not found in taxi graph.";

            // Restrict start-node candidates to the destination's connected
            // component. Defends against navdata defects where a closer
            // taxiway is modelled as an isolated island (e.g. GCLP S5 in
            // fs2024) — without this filter, FindNearestNodeInDirection
            // would snap to the island and A* would fail with no path.
            int destComponentId = _graph.Nodes[destinationNodeId].ComponentId;

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

            string? firstCleared = (taxiwaySequence is { Count: > 0 }) ? taxiwaySequence[0] : null;
            TaxiNode? firstTwNode = firstCleared != null
                ? SelectFirstTaxiwayEntry(
                    aircraftLat, aircraftLon, firstCleared, destComponentId, destinationNodeId)
                : null;

            bool attemptLeadIn = false;
            double leadInGap = 0;
            TaxiNode? startNode;
            if (firstTwNode != null)
            {
                leadInGap = TaxiGraph.FastDistanceMeters(
                    aircraftLat, aircraftLon, firstTwNode.Latitude, firstTwNode.Longitude);
                if (leadInGap > TaxiLeadIn.TriggerMeters)
                {
                    var entryNode = _graph.FindNearestNode(
                        aircraftLat, aircraftLon, requiredComponentId: destComponentId);
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
            else if (!string.IsNullOrEmpty(startTaxiwayName)
                     && _graph.FindNearestNodeOnTaxiway(
                            aircraftLat, aircraftLon, startTaxiwayName!,
                            requiredComponentId: destComponentId) is { } exitStartNode)
            {
                // Landing-exit early handoff: anchor the start on the CHOSEN exit taxiway
                // rather than the nearest node overall. When the early handoff fires the
                // aircraft is still on the runway, short of the exit, and the nearest graph
                // node can belong to a NEIGHBOURING exit — EIDW 28L abeam S5 while committed
                // to S6. Snapping there sends A* up the wrong exit and across the parallel
                // taxiway to reach the target: a 600 m+ hairpin (verified against the DB).
                // Snapping to the nearest node ON the chosen exit gives the direct
                // up-the-exit route. Still anchored to the live position — it is the nearest
                // node on the exit — so the look-ahead tone is measured from where the
                // aircraft actually is. Only reached when taxiwaySequence is null (this
                // branch is the else of the sequence path), so it never fights a clearance.
                startNode = exitStartNode;
            }
            else
            {
                startNode = _graph.FindNearestNodeInDirection(
                    aircraftLat, aircraftLon, aircraftHeading,
                    requiredComponentId: destComponentId);
            }
            if (startNode == null)
                return "Could not find a nearby taxiway node.";

            // Calculate route
            var router = new TaxiRouter(_graph);
            TaxiRoute? route;
            // The node the surviving route was actually built from. Diverges from
            // startNode.NodeId only in the lead-in fallback below; the holding-point pin
            // must rebuild its first leg from the SAME start or it would silently undo
            // that fallback.
            int routeStartNodeId = startNode.NodeId;

            if (taxiwaySequence != null && taxiwaySequence.Count > 0)
                route = router.FindConstrainedPath(startNode.NodeId, destinationNodeId, taxiwaySequence,
                    destinationIsRunway: isRunwayDestination);
            else
                route = router.FindShortestPath(startNode.NodeId, destinationNodeId);

            // Lead-in acceptance. If we started from the aircraft's nearest node to
            // get a pavement lead-in, accept it only when the router honoured the
            // clearance AND the lead-in is not a dead-end detour. Otherwise rebuild
            // from the on-taxiway node (today's behaviour) and note it in the
            // summary so the pilot knows the lead-in wasn't computed.
            TaxiLeadIn.LeadInInfo leadIn = default;
            bool leadInFallback = false;
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
                    routeStartNodeId = firstTwNode!.NodeId;
                    leadIn = default;
                    leadInFallback = true;
                }
            }

            if (route == null || route.Segments.Count == 0)
                return "Could not calculate a route to the destination.";

            // Named-holding-point departure: make the pilot's chosen stub the one they
            // actually taxi. Runs BEFORE TruncateToHoldShort, which is the whole point —
            // truncation takes the LAST hold node ON THE ROUTE, so the corridor decides
            // which painted line the pilot stops at and which name is announced.
            if (_holdingPointHoldNodeId != 0 &&
                ApplyHoldingPointPin(router, route, routeStartNodeId, destinationNodeId,
                                     taxiwaySequence) is { } pinnedRoute)
            {
                route = pinnedRoute;
                // The lead-in note in the summary describes the surviving route, so
                // re-measure it against the pinned one (same first cleared taxiway).
                if (attemptLeadIn && !leadInFallback && firstCleared != null)
                    leadIn = TaxiLeadIn.Extract(route, firstCleared);
            }

            string? constrainedLengthWarning = null;

            route.DestinationName = destinationName;
            if (taxiwaySequence != null)
                route.TaxiwaySequence = taxiwaySequence;

            // Apply user-requested hold-short points at taxiway transitions
            if (userHoldShortIndices != null && userHoldShortIndices.Count > 0 && taxiwaySequence != null)
            {
                ApplyUserHoldShorts(route, taxiwaySequence, userHoldShortIndices);
            }

            // Apply user-requested runway hold-shorts (per-row "Hold short of
            // runway X" pickers in the form). Runs BEFORE auto-detection so
            // when the user picked a runway that the route geometrically
            // crosses, the user's label wins (auto-detect respects existing
            // HoldShortRunway). If the user picked a runway the route does
            // NOT cross between the chosen taxiway and the next, we collect
            // a warning to announce alongside the route summary so the pilot
            // knows their explicit pick was a clearance/route mismatch.
            string? runwayHoldShortWarning = null;
            if (userRunwayHoldShorts != null && userRunwayHoldShorts.Count > 0 && taxiwaySequence != null)
            {
                runwayHoldShortWarning = ApplyUserRunwayHoldShorts(
                    route, taxiwaySequence, userRunwayHoldShorts);
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

            // Critical safety fix: when the destination is a runway, the route MUST
            // end at the hold-short line — not at the threshold itself. Otherwise
            // HandleArrival fires only when the aircraft is within the 30 m arrival
            // radius of the threshold, which is often PAST the hold-short markings
            // (real hold-short lines sit ~150-200 ft / 46-61 m back from the threshold).
            if (isRunwayDestination)
                TruncateToHoldShort(route, destinationName, preferIlsHold);

            // Auto-insert hold-shorts for INTERMEDIATE runway crossings.
            // FAA AIM 4-3-18 & ICAO Doc 4444: an aircraft must hold short of every
            // runway it crosses on the way to its destination, with explicit ATC
            // clearance for each one. Controllers issue runway crossings one at a
            // time. Without this pass, a route from gate to runway 22L through
            // an intersection with runway 13L would taxi straight across 13L
            // with no pause — illegal in real life and a runway-incursion risk on
            // VATSIM. This pause-and-resume flow uses the same Continue hotkey
            // pattern as ATC-instructed hold-shorts.
            var crossedRunways = InsertRunwayCrossingHoldShorts(route, isRunwayDestination ? destinationName : "");
            // One line per built route naming every runway it crosses. Answering "did that
            // route really drive across 08L?" for the 2026-08-27 KATL arrival meant
            // reconstructing segment coordinates against the navdata by hand; the route
            // pipeline already knows the answer at build time.
            _guidanceLog.Info(crossedRunways.Count > 0
                ? $"Route crossings: dest=\"{destinationName}\" segments={route.Segments.Count} " +
                  $"crosses={string.Join(",", crossedRunways)}"
                : $"Route crossings: dest=\"{destinationName}\" segments={route.Segments.Count} crosses=(none)");

            // Progressive "after crossing" terminator: the pilot is cleared to
            // cross the terminator runway, so strip the auto hold-short for it
            // (other crossings keep their safety hold — spec Decision 2).
            if (_progressiveTerminator?.ClearedCrossingRunway is string clearedRwy)
            {
                foreach (var seg in route.Segments)
                {
                    if (seg.IsHoldShortPoint && !string.IsNullOrEmpty(seg.HoldShortRunway) &&
                        RunwayDesignatorsMatch(seg.HoldShortRunway, clearedRwy))
                    {
                        seg.IsHoldShortPoint = false;
                        seg.HoldShortRunway = "";
                    }
                }
            }

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
            string? runwayReachWarning = null;
            _routeReachesRunway = true;
            if (isRunwayDestination && _hasLineupTarget)
            {
                double endCrossM = DestinationCrossTrackMeters(destinationNodeId);
                if (endCrossM > RUNWAY_REACH_MAX_CROSS_M)
                {
                    _routeReachesRunway = false;
                    runwayReachWarning =
                        $"Warning: this route ends about {FormatDistance(endCrossM)} to the side of " +
                        $"{destinationName} and does not reach the runway. You may be missing the " +
                        $"taxiway that connects to the runway. Check your taxiway entry and reprogram.";
                }
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
            if (taxiwaySequence is { Count: > 0 } &&
                string.IsNullOrEmpty(route.ConstrainedFallbackReason))
            {
                var directStart = _graph.FindNearestNodeInDirection(
                    aircraftLat, aircraftLon, aircraftHeading,
                    requiredComponentId: destComponentId) ?? startNode;
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
                        (firstTwNode ?? startNode).Latitude, (firstTwNode ?? startNode).Longitude);
                    string firstTwNote = firstTwDist > CONSTRAINED_WARN_FIRST_TW_M
                        ? $" Taxiway {taxiwaySequence[0]} is {FormatDistance(firstTwDist)} from your position."
                        : "";
                    constrainedLengthWarning =
                        $"Warning: route via {string.Join(", ", taxiwaySequence)} is " +
                        $"{FormatDistance(fullRouteMeters)}; direct route is " +
                        $"{FormatDistance(direct.TotalDistanceMeters)}.{firstTwNote} Check taxiway selection.";
                }
            }

            _route = route;
            _currentSegmentIndex = 0;
            // Cleared for every fresh route; BeginLandingRollout / RetargetLandingExit
            // re-set it true when this is a Landing Exit Planner route.
            _isLandingExitRoute = false;
            _landingExitOffPavement = true;   // a new route re-decides this at its own handoff
            _landingExitMissed = false;
            _landingExitVacatedEarly = false;
            _landingExitVacatedEarlyPlannedName = null;
            _landingExitRouteUnreachable = false;
            _landingExitMinDistToTargetM = double.MaxValue;
            _missedVacateSince = DateTime.MinValue;
            _approachAnnounced = false;
            _curveAnnouncedSign = 0;
            _turnImminentAnnounced = false;
            _crossingAnnounced = false;
            _lastCrossingNodeId = -1;
            _lastAnnouncedTaxiway = "";
            _headingErrorInitialized = false;
            _initialTurnCueAnnounced = false;
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
            // the route and segment cursor just assigned above (_route = route,
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
            LastRouteInitialTurnCue = null;
            if (route.Segments.Count > 0)
            {
                double aircraftHeadingTrue = aircraftHeading + aircraftHeadingMagVar;
                double initialErr = ComputeSteeringHeadingError(
                    aircraftLat, aircraftLon, aircraftHeadingTrue);
                string? firstNamed = null;
                foreach (var seg in route.Segments)
                {
                    if (string.IsNullOrEmpty(seg.TaxiwayName)) continue;
                    firstNamed = seg.TaxiwayName;
                    break;
                }
                LastRouteInitialTurnCue = Navigation.RouteStartTurnCue.Compose(initialErr, firstNamed);
            }
            // Reset the tone slew-limiter baseline too. LoadRoute is only ever a
            // FRESH route (the form's Calculate path doesn't call StopGuidance
            // first, and recalcs swap the route in place via TryRecalculateRoute
            // WITHOUT going through LoadRoute) — so a fresh route must snap to its
            // first target on frame one rather than sweep from the prior route's
            // stale value. Recalc-softening is unaffected: recalcs never reach here.
            _toneErrorInitialized = false;
            _smoothedHeadingError = 0;
            _lastIncursionWarnedNodeId = -1;
            _holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;
            _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
            // Reset lineup/cooldown state so an ATC-amendment reload mid-taxi doesn't
            // inherit stale values from the prior route (e.g., "aligned" carrying over
            // would suppress the first alignment announcement on the new lineup target).
            _lineupAnnouncedAligned = false;
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

            // Append a session-start header + CSV column row to the diagnostic
            // frame trace. Each "=== Guidance ... ===" line acts as a session
            // separator so a post-flight reader can split on it, and a buggy
            // session's trace survives a subsequent route load. Size-capped
            // rotation (so the file never grows without bound over months of
            // use) is now handled by the shared LogWriter rather than a
            // hand-rolled per-LoadRoute truncate.
            try
            {
                _guidanceLog.Info($"=== Guidance icao={_icao} dest={_destinationName} segments={_route.Segments.Count} totalM={_route.TotalDistanceMeters:F0} ===");
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
                // LVP hold feedback: when the CAT III / low-visibility hold was
                // requested, say which hold line the route actually stops at —
                // navdata hold coverage is patchy and the same-approach gate can
                // legitimately reject the ILS hold, and a blind pilot has no
                // other way to know. The honoured confirmation rides at the
                // tail; a fallback is warning-like and goes FIRST (same
                // interrupt reasoning as the length advisory below).
                string? lvpNote = RunwayHoldShortSelector.DescribeLvpOutcome(
                    isRunwayDestination && preferIlsHold, _lastRunwayHoldChoice);
                if (lvpNote != null)
                    summary = _lastRunwayHoldChoice == RunwayHoldChoice.IlsHold
                        ? summary + " " + lvpNote
                        : lvpNote + " " + summary;
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
                LastRouteSummary = boxText;
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
                    _announcer.Announce(summary);
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Error calculating route: {ex.Message}";
        }
        } // end lock(_stateLock)
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
    ///
    /// PROXIMITY IS REQUIRED (<see cref="SEGMENT_ADVANCE_MAX_DIST_M"/>). "Nearest of the next
    /// six" is only evidence of progress if the aircraft is actually NEAR the winner —
    /// without that gate an aircraft driving AWAY from the route still advanced along it,
    /// because as it moves some other segment endpoint keeps becoming marginally nearest.
    /// Measured across three guidance logs: 13 of 981 advances happened while the steering
    /// tone was pointing more than 90° away from the aircraft's heading, including a
    /// 0 → 3 jump at 179° (three segments consumed in one frame, exactly reversed) and
    /// five consecutive advances over 9 s at EIDW while the error grew 103° → 139°.
    ///
    /// Three things went wrong, all of them silent:
    ///   1. the route consumed itself, so turning back resumed from the wrong place;
    ///   2. every advance stamps <c>_lastSegmentAdvanceTime</c>, which forces
    ///      <c>nearTurn</c> true for POST_TURN_OFFROUTE_GRACE_SEC and SUPPRESSES off-route
    ///      detection — being off-route caused advances, and the advances switched off the
    ///      thing that would have noticed (KBNA 2026-08-08: 690 m away over 50 s at up to
    ///      31 kt, tone pinned ~170° behind, not one recalc and not one word);
    ///   3. the hold-short skip loop below fired <see cref="HandleHoldShort"/> at ANY
    ///      distance — which pauses the tone and says "Stop. Hold short of runway X" for a
    ///      runway that may be hundreds of metres away, AND moves the index past it so the
    ///      real crossing never announces. That is the runway-incursion direction.
    /// </summary>
    private void AdvanceToNearestSegment(double lat, double lon)
    {
        if (_route == null) return;

        int bestIdx = _currentSegmentIndex;
        double bestDist = double.MaxValue;

        // Look at current segment and up to 5 segments ahead
        int lookAhead = Math.Min(_currentSegmentIndex + 6, _route.Segments.Count);
        for (int i = _currentSegmentIndex; i < lookAhead; i++)
        {
            var seg = _route.Segments[i];

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
        // _lastSegmentAdvanceTime and can do its job.
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
                    _lastSegmentAdvanceTime = DateTime.UtcNow;
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
                        _lastSegmentAdvanceTime = DateTime.UtcNow;
                    }
                }
                return;
            }

            _currentSegmentIndex = bestIdx;
            _lastSegmentAdvanceTime = DateTime.UtcNow;

            var newSeg = _route.Segments[_currentSegmentIndex];
            if (!string.IsNullOrEmpty(newSeg.TaxiwayName) &&
                !newSeg.TaxiwayName.Equals(_lastAnnouncedTaxiway, StringComparison.OrdinalIgnoreCase))
            {
                AnnounceInstruction($"Taxiway {newSeg.TaxiwayName}.");
                _lastAnnouncedTaxiway = newSeg.TaxiwayName;
            }

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
    /// </summary>
    private TaxiNode? SelectFirstTaxiwayEntry(
        double aircraftLat, double aircraftLon, string taxiwayName,
        int destComponentId, int destinationNodeId)
    {
        var euclideanNearest = _graph!.FindNearestNodeOnTaxiway(
            aircraftLat, aircraftLon, taxiwayName, requiredComponentId: destComponentId);
        if (euclideanNearest == null) return null;

        // Dijkstra needs a node to start from; the aircraft sits between nodes, so
        // use the nearest one. The hop from the aircraft onto it is the same for
        // every candidate, so it can't affect the ranking.
        var anchor = _graph.FindNearestNode(
            aircraftLat, aircraftLon, requiredComponentId: destComponentId);
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

        if ((DateTime.UtcNow - _lastRecalculationTime).TotalSeconds < RECALCULATION_COOLDOWN_SEC)
            return;

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

        _lastRecalculationTime = DateTime.UtcNow;

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

        (List<string>? remainingSequence, TaxiNode? nearestNode) =
            FindRemainingSequenceByPosition(lat, lon, destComponentId);

        // If no sequence taxiway is near the aircraft, fall back to the
        // heading-aware picker. The constrained path will likely fail and
        // bail to shortest — that's the right behaviour when the aircraft
        // has drifted off every cleared taxiway.
        if (nearestNode == null)
        {
            nearestNode = _graph.FindNearestNodeInDirection(
                lat, lon, headingTrue, requiredComponentId: destComponentId);
            if (nearestNode == null) return;
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
            _announcer.AnnounceImmediate("Off route. Unable to recalculate.");
            return;
        }

        // Re-apply the named-holding-point pin, so a recalc keeps routing the pilot to the
        // painted line they chose. Deliberately BEFORE the sanity gate below: the gate must
        // judge the route we would actually fly, not an intermediate one.
        if (_holdingPointHoldNodeId != 0 &&
            ApplyHoldingPointPin(router, newRoute, nearestNode.NodeId, _destinationNodeId,
                                 remainingSequence) is { } pinnedRecalc)
        {
            newRoute = pinnedRecalc;
        }

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
                _announcer.AnnounceImmediate(
                    $"Off route. Could not follow clearance. {reasonStr}. Continuing on original route.");
                return;
            }
        }

        newRoute.DestinationName = _destinationName;

        // Apply the same runway-destination safety truncation as LoadRoute — otherwise
        // after an auto-recalc the pilot would roll straight onto the runway instead
        // of stopping at the hold-short line.
        if (_isRunwayLineup)
            TruncateToHoldShort(newRoute, _destinationName, _preferIlsHold);

        // Re-probe reachability for the recalculated route (the recalc routes to
        // the same _destinationNodeId, so this measures the destination node, not
        // the new hold-short). Keeps the during-lineup bailout correct after an
        // auto-recalc, which was the bailout's original reason for existing.
        // Only meaningful for a runway lineup (matching the TruncateToHoldShort
        // guard above); gate destinations leave _routeReachesRunway true so the
        // runway-only safety net stays disarmed.
        _routeReachesRunway = !_isRunwayLineup ||
            DestinationCrossTrackMeters(_destinationNodeId) <= RUNWAY_REACH_MAX_CROSS_M;

        // Distinct consecutive named taxiways of the recalculated route, in order.
        var viaNames = new List<string>();
        foreach (var s in newRoute.Segments)
        {
            if (!string.IsNullOrEmpty(s.TaxiwayName) &&
                (viaNames.Count == 0 || !viaNames[^1].Equals(s.TaxiwayName, StringComparison.OrdinalIgnoreCase)))
                viaNames.Add(s.TaxiwayName);
        }

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
        var oldRemainingVia = new List<string>();
        if (_route != null)
        {
            for (int i = Math.Max(0, _currentSegmentIndex); i < _route.Segments.Count; i++)
            {
                var nm = _route.Segments[i].TaxiwayName;
                if (!string.IsNullOrEmpty(nm) &&
                    (oldRemainingVia.Count == 0 || !oldRemainingVia[^1].Equals(nm, StringComparison.OrdinalIgnoreCase)))
                    oldRemainingVia.Add(nm);
            }
        }
        if (oldRemainingVia.Count > 0 &&
            oldRemainingVia.SequenceEqual(viaNames, StringComparer.OrdinalIgnoreCase))
            return;

        _route = newRoute;
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
        _lastIncursionWarnedNodeId = -1;
        _headingErrorInitialized = false;

        string firstTaxiway = newRoute.Segments[0].TaxiwayName;
        string distStr = FormatDistance(newRoute.TotalDistanceMeters);

        // Announce the NEW taxiway sequence so the pilot hears that their cleared
        // route changed — a recalc can trim/replace the entered clearance (PHNL
        // 2026-06-13: "Z A L N Z D" silently became "Z D", and the old generic
        // "Recalculating. … Taxiway Z." never said the sequence had changed).
        string callout = viaNames.Count > 0
            ? $"Route changed. Now via {string.Join(", ", viaNames)}. {distStr} to {_destinationName}."
            : $"Route changed. {distStr} to {_destinationName}.";
        AnnounceInstruction(callout);

        _lastAnnouncedTaxiway = firstTaxiway;
    }

    /// <summary>
    /// Walks the original ATC taxiway sequence from latest to earliest, returning
    /// the suffix starting at the first taxiway whose nearest graph node is within
    /// NEAR_TAXIWAY_M of the aircraft. Returns (null, null) if no sequence taxiway
    /// is near the aircraft — caller should fall back to shortest path.
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
                requiredComponentId: requiredComponentId);
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
            _lastSegmentAdvanceTime = DateTime.UtcNow;
            HandleHoldShort(completedSeg);
            return;
        }

        _currentSegmentIndex++;
        _lastSegmentAdvanceTime = DateTime.UtcNow;
        _approachAnnounced = false;
        _turnImminentAnnounced = false;
        _crossingAnnounced = false;

        if (_currentSegmentIndex >= _route.Segments.Count)
        {
            HandleArrival();
            return;
        }

        var newSeg = _route.Segments[_currentSegmentIndex];
        string newTaxiway = newSeg.TaxiwayName;
        if (!string.IsNullOrEmpty(newTaxiway) &&
            !newTaxiway.Equals(_lastAnnouncedTaxiway, StringComparison.OrdinalIgnoreCase))
        {
            AnnounceInstruction($"Taxiway {newTaxiway}.");
            _lastAnnouncedTaxiway = newTaxiway;
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
    /// Re-routes a named-holding-point departure THROUGH the painted hold line the pilot
    /// chose, so the stub they named is the stub they taxi. Returns the pinned route, or
    /// null to keep the caller's original.
    /// <para>Selecting a holding point fixes only the runway ENTRY node; the corridor to it
    /// is a free A* choice. At EGLL 27R (2026-08-08) a pilot who picked A2 was routed up the
    /// neighbouring A3 stub — the two merge just short of the runway, so the route rejoined
    /// A2 for its final 60 m and reached the entry as asked, but the aircraft crossed A3's
    /// painted line and never A2's. TruncateToHoldShort takes the LAST hold node on the
    /// route, so guidance correctly named the line it stopped at: "A3", contradicting the
    /// pilot's choice. Pinning the corridor is what makes the two agree.</para>
    /// <para>The pin degrades to a no-op on every doubt — node missing, unreachable, either
    /// leg unbuildable, or a detour beyond <see cref="HOLD_PIN_MAX_RATIO"/>/
    /// <see cref="HOLD_PIN_MAX_PAD_M"/>. It can only ever make the route match the request;
    /// it must never be able to make a working route worse.</para>
    /// </summary>
    private TaxiRoute? ApplyHoldingPointPin(
        TaxiRouter router, TaxiRoute route, int startNodeId, int destinationNodeId,
        List<string>? taxiwaySequence)
    {
        int holdNodeId = _holdingPointHoldNodeId;
        if (_graph == null || holdNodeId == 0) return null;
        if (!_graph.Nodes.ContainsKey(holdNodeId)) return null;
        // Pinning to the destination itself is meaningless (and would make leg B empty).
        if (holdNodeId == destinationNodeId || holdNodeId == startNodeId) return null;

        // Already on the chosen corridor — the free route happens to pass the painted line,
        // which is the common case at an airport whose stubs don't merge. Nothing to do.
        foreach (var seg in route.Segments)
            if (seg.FromNode.NodeId == holdNodeId || seg.ToNode.NodeId == holdNodeId)
                return null;

        // Leg A keeps the clearance (the pilot's taxiways still constrain the way there);
        // destinationIsRunway is false because the hold node is NOT the runway — the
        // last-cleared-taxiway-as-terminus rule belongs to the runway leg, which is leg B.
        var legA = taxiwaySequence is { Count: > 0 }
            ? router.FindConstrainedPath(startNodeId, holdNodeId, taxiwaySequence,
                                         destinationIsRunway: false)
            : router.FindShortestPath(startNodeId, holdNodeId);
        // Leg B is the stub itself: hold line → runway entry, a few nodes, no constraint to
        // apply (the clearance's taxiways are all behind us by here).
        var legB = router.FindShortestPath(holdNodeId, destinationNodeId);
        if (legA is not { Segments.Count: > 0 } || legB is not { Segments.Count: > 0 })
            return null;

        var pinned = router.Concatenate(legA, legB);
        if (pinned == null || pinned.Segments.Count == 0) return null;

        if (pinned.TotalDistanceMeters >
            route.TotalDistanceMeters * HOLD_PIN_MAX_RATIO + HOLD_PIN_MAX_PAD_M)
        {
            _guidanceLog.Info(
                $"Holding-point pin REJECTED (node {holdNodeId}): pinned " +
                $"{pinned.TotalDistanceMeters:F0} m vs free {route.TotalDistanceMeters:F0} m.");
            return null;
        }

        // Surface leg A's clearance verdict — the pinned route IS leg A up to the hold line,
        // so a fallback there is the fallback the pilot needs told about.
        pinned.ConstrainedFallbackReason = legA.ConstrainedFallbackReason;
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
        int truncateAtIHS = -1;
        int truncateAtHS  = -1;
        for (int i = route.Segments.Count - 1; i >= 0; i--)
        {
            var to = route.Segments[i].ToNode;
            if (to == null) continue;
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
        // Backoff reference: normally the runway lineup point. For a FULL-LENGTH
        // BACKTRACK departure the lineup point is the FAR (full-length) threshold
        // — hundreds of metres past the route's actual end (the intermediate
        // entrance) — so backing off from it would never truncate and would tag a
        // segment on the runway. Back off from the ENTRANCE (the destination node)
        // instead, so the hold-short sits just before the pilot enters to backtrack.
        double backoffRefLat = _lineupTargetLat, backoffRefLon = _lineupTargetLon;
        if (_backtrackDeparture && _destinationNodeId != 0 && _graph != null &&
            _graph.Nodes.TryGetValue(_destinationNodeId, out var entryNode))
        {
            backoffRefLat = entryNode.Latitude;
            backoffRefLon = entryNode.Longitude;
        }
        if (truncateAt < 0 && _hasLineupTarget)
        {
            for (int i = route.Segments.Count - 1; i >= 0; i--)
            {
                var to = route.Segments[i].ToNode;
                if (to == null) continue;
                double d = TaxiGraph.FastDistanceMeters(
                    to.Latitude, to.Longitude, backoffRefLat, backoffRefLon);
                if (d >= SYNTHETIC_BACKOFF_M)
                {
                    truncateAt = i;
                    break;
                }
            }
        }

        if (truncateAt >= 0 && truncateAt < route.Segments.Count - 1)
        {
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
        if (string.IsNullOrEmpty(lastSeg.HoldShortRunway))
            lastSeg.HoldShortRunway = destinationName;
    }

    /// <summary>
    /// Scans the route for segments that cross a runway centerline, and tags the
    /// LAST segment before each crossing as a hold-short with the runway name.
    /// The destination runway (if `destinationName` is non-empty) is excluded —
    /// `TruncateToHoldShort` already handles the destination's hold-short
    /// separately, and tagging it twice would produce duplicate announcements.
    ///
    /// "Crossing" detection: project the segment endpoint onto each runway
    /// centerline; if the perpendicular distance is within the runway's
    /// half-width tolerance AND the projection point lies between the two
    /// thresholds (along-track within [0, length]), the segment ends ON the
    /// runway → the previous segment was the approach, tag THAT one as the
    /// hold-short. Skip duplicate consecutive hold-shorts of the same runway
    /// (one approach, multiple internal segments on the runway pavement).
    /// </summary>
    private List<string> InsertRunwayCrossingHoldShorts(TaxiRoute route, string destinationName)
    {
        if (route == null || route.Segments.Count < 2) return new List<string>();
        if (_graph == null || _graph.RunwayCenterlines.Count == 0) return new List<string>();

        // Tracks the runway whose hold-short was most recently inserted, so we
        // don't tag every consecutive segment that's on the same runway pavement.
        string lastTaggedRunway = "";
        var crossed = new List<string>();

        // The destination name arrives prefixed ("Runway 33L"), but the crossed
        // runway is a bare designator ("33L"). Normalise so the destination
        // exclusion below actually matches — TruncateToHoldShort owns the
        // destination's hold-short, and tagging it here too would double-announce.
        string destBare = destinationName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? destinationName.Substring("Runway ".Length).Trim()
            : destinationName.Trim();

        // Detect a crossing by EDGE intersection, not point-on-pavement. A
        // taxiway crosses a runway via an edge that SPANS the pavement, with its
        // endpoint nodes sitting off the runway on either side — so the old
        // "is the next node ON the runway?" test silently missed every crossing
        // where the flanking nodes are more than half-width+5 m from the
        // centerline (KBOS taxiway C over 04L / 27: nearest C node is 35 m / 86 m
        // from the centerline, so no node landed on pavement and no hold-short
        // was inserted, even though C plainly crosses the runways).
        for (int i = 1; i < route.Segments.Count; i++)
        {
            var crossingSeg = route.Segments[i];
            if (crossingSeg.FromNode == null || crossingSeg.ToNode == null) continue;

            string crossedRwy = WhichRunwayCrossedByEdge(
                crossingSeg.FromNode.Latitude, crossingSeg.FromNode.Longitude,
                crossingSeg.ToNode.Latitude, crossingSeg.ToNode.Longitude);
            if (string.IsNullOrEmpty(crossedRwy)) continue;

            // Skip the destination runway (TruncateToHoldShort owns it).
            if (!string.IsNullOrEmpty(destBare) &&
                crossedRwy.Equals(destBare, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip if we've just tagged this runway already (a wide runway whose
            // entry and exit edges both cross the centerline, or consecutive
            // pavement segments).
            if (crossedRwy.Equals(lastTaggedRunway, StringComparison.OrdinalIgnoreCase))
                continue;

            // The crossing edge is segment i; hold short at the scenery's own
            // hold line before it (falling back to the end of segment i-1 when
            // the navdata carries no hold node within reach — see
            // RouteRunwayCrossings.ResolveCrossingHoldSegment; the node before
            // the crossing edge is routinely ON the runway pavement, because
            // the crossing is detected against the CENTERLINE).
            var holdSeg = route.Segments[
                RouteRunwayCrossings.ResolveCrossingHoldSegment(route.Segments, i, crossedRwy)];
            holdSeg.IsHoldShortPoint = true;
            // Label policy lives in RouteRunwayCrossings.ComposeCrossingLabel
            // (pure, probe-tested): empty → tagged; bare DB names upgraded to
            // "runway X at <holdPoint>"; user labels + correct names kept;
            // a DB name for a DIFFERENT pavement corrected to geometric truth.
            string? newLabel = RouteRunwayCrossings.ComposeCrossingLabel(
                holdSeg.HoldShortRunway, crossedRwy);
            if (newLabel != null)
                holdSeg.HoldShortRunway = newLabel;
            lastTaggedRunway = crossedRwy;
            crossed.Add(crossedRwy);
        }

        return crossed;
    }

    /// <summary>
    /// Returns the name of the runway crossed by the taxi edge (a→b), or "" if
    /// the edge crosses no runway. Uses edge-vs-centerline intersection
    /// (<see cref="TaxiGraph.EdgeCrossesRunwayStatic"/>) so a crossing is found
    /// regardless of how far the edge's endpoint nodes sit from the centerline.
    /// Names the runway after the threshold closer to the edge midpoint — same
    /// convention as TaxiGraph.DescribeLocation.
    /// </summary>
    private string WhichRunwayCrossedByEdge(double aLat, double aLon, double bLat, double bLon)
    {
        if (_graph == null) return "";

        foreach (var rwy in _graph.RunwayCenterlines)
        {
            if (!TaxiGraph.EdgeCrossesRunwayStatic(
                    aLat, aLon, bLat, bLon,
                    rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2))
                continue;

            double mLat = (aLat + bLat) * 0.5;
            double mLon = (aLon + bLon) * 0.5;
            double d1 = TaxiGraph.FastDistanceMeters(mLat, mLon, rwy.Lat1, rwy.Lon1);
            double d2 = TaxiGraph.FastDistanceMeters(mLat, mLon, rwy.Lat2, rwy.Lon2);
            string name = d1 <= d2 ? rwy.Name1 : rwy.Name2;
            if (string.IsNullOrEmpty(name)) name = rwy.Name1;
            return name;
        }
        return "";
    }

    /// <summary>
    /// Honors the user's explicit "Hold short of runway X" pickers from the
    /// form. For each (sequenceIndex → runway) pair, finds the route segment
    /// whose endpoint sits on that runway's centerline AND comes after the
    /// last segment of taxiway[sequenceIndex], and tags it as a hold-short.
    ///
    /// Returns a non-null warning string when one or more user picks could
    /// not be matched to the route (clearance/route mismatch — caller may
    /// announce alongside the route summary so the pilot sees the issue).
    /// Returns null when every pick was honored cleanly.
    ///
    /// Auto-detection (`InsertRunwayCrossingHoldShorts`) runs after this and
    /// will pick up any crossings the user didn't explicitly mark. When both
    /// fire on the same segment, this method's label wins because auto-detect
    /// respects an existing non-empty `HoldShortRunway`.
    /// </summary>
    private string? ApplyUserRunwayHoldShorts(
        TaxiRoute route,
        List<string> taxiwaySequence,
        Dictionary<int, string> userRunwayHoldShorts)
    {
        if (_graph == null) return null;

        var unmatched = new List<string>();

        foreach (var kvp in userRunwayHoldShorts)
        {
            int seqIdx = kvp.Key;
            string runwayId = kvp.Value;

            if (seqIdx < 0 || seqIdx >= taxiwaySequence.Count) continue;
            string taxiwayName = taxiwaySequence[seqIdx];

            // Pre-resolve the RunwayCenterline whose designators include the
            // user's runwayId. The user types ONE of two reciprocal designators
            // (e.g. "10R" / "28L") but both name the same physical pavement.
            // Testing the edge-crossing against this centerline's geometry
            // directly avoids the closer-threshold pitfall where naming a
            // crossing by the nearer reciprocal designator would return the
            // OTHER end's name and silently miss the user's pick.
            TaxiGraph.RunwayCenterline? targetRwy = null;
            foreach (var rwy in _graph.RunwayCenterlines)
            {
                if (rwy.Name1.Equals(runwayId, StringComparison.OrdinalIgnoreCase) ||
                    rwy.Name2.Equals(runwayId, StringComparison.OrdinalIgnoreCase))
                {
                    targetRwy = rwy;
                    break;
                }
            }
            if (targetRwy == null)
            {
                unmatched.Add($"runway {runwayId} (not in airport runway data)");
                continue;
            }

            // For duplicate-taxiway clearances ("via N, hold short 15R, N,
            // hold short 22R, N" — KBOS style), each sequence entry refers to a
            // distinct run of that taxiway in the route. Count prior
            // occurrences in the sequence so we anchor the scan at the right
            // run instead of always starting at the first one.
            int priorOccurrences = 0;
            for (int k = 0; k < seqIdx; k++)
            {
                if (taxiwaySequence[k].Equals(taxiwayName, StringComparison.OrdinalIgnoreCase))
                    priorOccurrences++;
            }

            // Locate the start of the (priorOccurrences+1)-th maximal run of
            // route segments tagged with this taxiway. Scanning from the START
            // of the run — not the end — finds runway crossings INTERNAL to the
            // named taxiway. Example: KSFO taxiway D crosses 10R/28L mid-way
            // while keeping the same name on both sides; with the previous
            // last-segment anchor the scan started past the runway and the
            // user's correct "hold short 10R" pick was silently rejected as
            // "route does not cross 10R".
            int runStart = -1;
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
                    {
                        runStart = i;
                        break;
                    }
                }
                else if (!matches && inRun)
                {
                    inRun = false;
                    runsSeen++;
                }
            }
            if (runStart < 0)
            {
                unmatched.Add($"runway {runwayId} (taxiway {taxiwayName} not on route)");
                continue;
            }

            // Forward scan from the run start for the first segment whose EDGE
            // crosses the target runway (edge-vs-centerline intersection, not
            // node-on-pavement). A taxiway crosses a runway via an edge that
            // spans the pavement with both endpoint nodes OFF the runway, so the
            // old "is a node within half-width of the centerline?" test silently
            // rejected the user's correct pick whenever the flanking nodes were
            // more than half-width+5 m out (KBOS "hold short 04L" on taxiway C:
            // nearest C node is 35 m from the 04L centerline → falsely reported
            // "route does not cross 04L after taxiway C").
            int crossingSeg = -1;
            for (int i = runStart; i < route.Segments.Count; i++)
            {
                var seg = route.Segments[i];
                if (seg.FromNode == null || seg.ToNode == null) continue;
                if (TaxiGraph.EdgeCrossesRunwayStatic(
                        seg.FromNode.Latitude, seg.FromNode.Longitude,
                        seg.ToNode.Latitude, seg.ToNode.Longitude,
                        targetRwy.Lat1, targetRwy.Lon1, targetRwy.Lat2, targetRwy.Lon2))
                {
                    crossingSeg = i;
                    break;
                }
            }

            if (crossingSeg < 0)
            {
                unmatched.Add($"runway {runwayId} (route does not cross it after taxiway {taxiwayName})");
                continue;
            }

            // Tag the segment ending at the scenery's own hold line before the
            // crossing (the segment immediately before the crossing edge when
            // there is none) — the crossing edge straddles the CENTERLINE, so
            // its start node is routinely on the pavement itself. See
            // RouteRunwayCrossings.ResolveCrossingHoldSegment.
            int holdSegIdx = RouteRunwayCrossings.ResolveCrossingHoldSegment(
                route.Segments, crossingSeg, runwayId);
            var holdSeg = route.Segments[holdSegIdx];
            holdSeg.IsHoldShortPoint = true;
            // User intent wins on the label.
            holdSeg.HoldShortRunway = $"runway {runwayId}";
        }

        if (unmatched.Count == 0) return null;
        return $"Note: requested hold-short(s) not on route — {string.Join("; ", unmatched)}.";
    }

    private static void ApplyUserHoldShorts(TaxiRoute route, List<string> taxiwaySequence, List<int> userHoldShortIndices)
    {
        foreach (int seqIdx in userHoldShortIndices)
        {
            if (seqIdx < 0 || seqIdx >= taxiwaySequence.Count)
                continue;

            string taxiwayName = taxiwaySequence[seqIdx];

            int lastSegOnTaxiway = -1;
            for (int i = 0; i < route.Segments.Count; i++)
            {
                if (route.Segments[i].TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase))
                    lastSegOnTaxiway = i;
            }

            if (lastSegOnTaxiway >= 0)
            {
                var seg = route.Segments[lastSegOnTaxiway];
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

}
