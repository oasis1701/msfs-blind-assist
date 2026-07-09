using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public partial class TaxiGuidanceManager
{
    private void CheckUpcomingAnnouncements(double distToTargetM, TaxiRouteSegment currentSeg)
    {
        if (_route == null) return;

        int nextIdx = _currentSegmentIndex + 1;
        if (nextIdx >= _route.Segments.Count)
        {
            if (distToTargetM < APPROACH_ANNOUNCE_DISTANCE_M && !_approachAnnounced)
            {
                AnnounceInstruction($"{_route.DestinationName} ahead.");
                _approachAnnounced = true;
            }
            return;
        }

        var nextSeg = _route.Segments[nextIdx];

        // Hold-short approach callouts are owned exclusively by
        // CheckHoldShortCountdown (300/150/50 ft cadence). Previously this
        // method had its own "<150" branch here — but distToTargetM is in
        // METERS not feet, so "<150" fired at ~492 ft and duplicated the 300
        // ft cadence. Removed.
        if (currentSeg.IsHoldShortPoint)
            return;

        // Cumulative-curve cue — fires for gentle multi-segment curves that the
        // discrete-turn logic below cannot see (every individual junction < 20°).
        TryAnnounceCurve();

        string nextTaxiway = nextSeg.TaxiwayName;
        bool taxiwayChanging = !string.IsNullOrEmpty(nextTaxiway) &&
            !nextTaxiway.Equals(currentSeg.TaxiwayName, StringComparison.OrdinalIgnoreCase);
        bool hasTurn = nextSeg.TurnDirection != "straight";

        // "Crossing taxiway X" — junction with named taxiways where route stays on current taxiway.
        // Announce ~150ft out so the pilot has awareness of intersection layout without clutter.
        // Suppressed when a turn/taxiway change is happening (that announcement carries the info).
        TryAnnounceCrossing(distToTargetM, currentSeg, nextSeg, taxiwayChanging, hasTurn);

        // Final segment onto a runway: the lineup logic (intercept-to-centerline
        // tone + "Lined up" callout + the LiningUp status readout) owns guidance
        // here. The route's STATIC turn onto the runway is computed from the
        // segment bearing, while the lineup tone pans toward the intercept-
        // corrected centerline — these can point OPPOSITE ways at the taxi->
        // lineup boundary ("turn right" spoken while the tone pans left, which
        // is exactly the reported confusion). Don't emit the segment-bearing
        // turn/approach callout for that last hop; let the tone steer it.
        if (_isRunwayLineup && nextIdx == _route.Segments.Count - 1)
            return;

        if (!hasTurn && !taxiwayChanging)
            return;

        // Is this a sharp turn? (>60° — ICAO Annex 14 regards >90° as requiring
        // significantly wider radius; we flag a bit earlier so slow-down margin
        // is built in.)
        double turnAngle = Math.Abs(nextSeg.TurnAngleDegrees);
        bool sharpTurn = hasTurn && turnAngle >= SHARP_TURN_ANGLE_DEG;

        // Advance-notice distance scales with ground speed — at 20 kt, 300 ft is
        // only 9 seconds of lead, not enough to slow down for a sharp turn.
        // Target ≈10 s lead; floor 80 m (≈260 ft) for slow taxi, ceiling 200 m
        // (≈650 ft) for fast taxi. Sharp turns get an extra 50% to give time to slow.
        double approachDist = Math.Clamp(
            _lastGroundSpeedKts * 0.5144 * APPROACH_ANNOUNCE_SEC_LEAD,
            APPROACH_ANNOUNCE_MIN_M, APPROACH_ANNOUNCE_MAX_M);
        if (sharpTurn) approachDist *= 1.5;

        // Advance notice (speed-scaled). Terse: direction + taxiway + angle for sharp.
        // Speed-slow warning is owned by CheckSpeedWarnings — don't duplicate it here.
        if (distToTargetM < approachDist && !_approachAnnounced)
        {
            string distStr = distToTargetM > 15 ? $"In {FormatDistance(distToTargetM)}, " : "";
            // Direction is computed from the aircraft's CURRENT heading toward the
            // next segment's bearing — see ComputeTurnVerbalFromHeading. This makes
            // the verbal cue match the steering tone (which is also heading-based)
            // even when the aircraft is off-axis from the current route segment.
            string turnStr = hasTurn
                ? ComputeTurnVerbalFromHeading(nextSeg.BearingDegrees, _lastHeading)
                : "continue";
            string sharpStr = sharpTurn ? "sharp " : "";
            string taxiStr = taxiwayChanging ? $" onto taxiway {nextTaxiway}" : "";
            // For sharp turns, speak the rounded angle so the blind pilot knows
            // 70° vs. 150° hairpin. No extra "slow to X" advice here — the speed
            // warning module handles that with its own cadence.
            string angleStr = sharpTurn
                ? $", {(int)Math.Round(turnAngle / 10.0) * 10} degrees"
                : "";

            AnnounceInstruction($"{distStr}{sharpStr}{turnStr}{taxiStr}{angleStr}.");
            _approachAnnounced = true;
        }

        // Imminent turn at speed-scaled distance (~4 seconds lead at current ground speed).
        // At 10 kts → 21m; at 20 kts → 41m; at 30 kts → 62m. Clamped to [20, 75]m.
        // 1 knot = 0.5144 m/s; 4 sec * m/s = meters of lead.
        double turnImminentDistance = Math.Clamp(
            _lastGroundSpeedKts * 0.5144 * TURN_IMMINENT_SEC_LEAD,
            TURN_IMMINENT_MIN_M, TURN_IMMINENT_MAX_M);
        // Sharp turns: push "turn now" further out so the pilot begins the turn
        // slightly early, giving a wider radius on the outside of the corner.
        // Keeps the wheels on pavement — no lawn mowing.
        if (sharpTurn) turnImminentDistance *= 1.3;

        if (hasTurn && distToTargetM < turnImminentDistance && !_turnImminentAnnounced && _approachAnnounced)
        {
            string sharpStr = sharpTurn ? "sharp " : "";
            string taxiStr = taxiwayChanging ? $", taxiway {nextTaxiway}" : "";
            // Same as the advance-notice cue: actual heading, not route's static turn.
            string turnStr = ComputeTurnVerbalFromHeading(nextSeg.BearingDegrees, _lastHeading);
            AnnounceInstruction($"{sharpStr}{turnStr} now{taxiStr}.");
            _turnImminentAnnounced = true;
        }
    }

    private void TryAnnounceCurve()
    {
        if (_route == null || _route.Segments.Count == 0 ||
            _currentSegmentIndex >= _route.Segments.Count) return;

        var (lats, lons) = RoutePoints();
        double cum = GuidanceGeometry.CumulativeTurnDeg(
            lats, lons, _currentSegmentIndex, _lastLat, _lastLon,
            CURVE_SCAN_WINDOW_M, out bool hasDiscreteStep);

        // A discrete junction inside the window is owned by the approach/now
        // callouts — saying "curving" as well would double-speak the same bend.
        if (hasDiscreteStep) return;

        int sign = cum <= -CURVE_ANNOUNCE_MIN_DEG ? -1
                 : cum >= CURVE_ANNOUNCE_MIN_DEG ? 1 : 0;

        if (sign == 0)
        {
            if (Math.Abs(cum) < CURVE_RESET_DEG) _curveAnnouncedSign = 0;  // re-arm
            return;
        }
        if (sign == _curveAnnouncedSign) return;   // this curve already announced

        // Don't announce a NEW direction while the aircraft is still yawing the
        // OTHER way: near a curve's exit the scan window already reaches into the
        // next, opposite curve, and announcing it mid-turn reads as a contradiction
        // ("Curving right." while the pilot is still steering left — pilot report
        // 2026-06-11). Defer; the cue fires the moment the current turn settles.
        if (_yawRateDegSec * sign < 0 &&
            Math.Abs(_yawRateDegSec) >= CURVE_ANNOUNCE_MAX_OPPOSITE_YAW_DEG_SEC)
            return;

        _curveAnnouncedSign = sign;
        AnnounceInstruction(sign < 0 ? "Curving left." : "Curving right.");
    }

    /// <summary>
    /// Announces named taxiways being crossed at intersections where the route itself
    /// doesn't change taxiways. The ToNode of the current segment is the intersection.
    /// Uses node.TaxiwayNames minus the current/next route taxiway to derive "crossing"
    /// names. Only fires once per node (guarded by _lastCrossingNodeId).
    /// </summary>
    private void TryAnnounceCrossing(
        double distToTargetM,
        TaxiRouteSegment currentSeg,
        TaxiRouteSegment nextSeg,
        bool taxiwayChanging,
        bool hasTurn)
    {
        if (!_announceCrossings) return;       // user disabled via settings
        if (_crossingAnnounced) return;
        if (distToTargetM >= CROSSING_ANNOUNCE_DISTANCE_M) return;

        // Skip if the upcoming turn/taxiway-change announcement will carry the info.
        if (hasTurn || taxiwayChanging) return;

        var junctionNode = currentSeg.ToNode;
        if (junctionNode == null) return;
        if (junctionNode.NodeId == _lastCrossingNodeId) return;

        // Only announce at "real" intersections: a node that has taxiway names
        // beyond the one the route is following through it.
        if (junctionNode.TaxiwayNames.Count <= 1) return;

        var otherTaxiways = new List<string>();
        foreach (var name in junctionNode.TaxiwayNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Equals(currentSeg.TaxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals(nextSeg.TaxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
            otherTaxiways.Add(name);
        }

        if (otherTaxiways.Count == 0) return;

        // De-duplicate & sort
        otherTaxiways = otherTaxiways
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Time-windowed per-name dedup. Many airports (KJFK, EGLL) represent a single
        // intersection as two graph nodes 5–15m apart, which would otherwise fire
        // "Crossing taxiway Link 53" twice in a row. Suppress repeats of the same
        // NAME within the dedup window even across different junction nodes.
        DateTime now = DateTime.UtcNow;
        var freshNames = new List<string>();
        foreach (var name in otherTaxiways)
        {
            if (_recentCrossingAnnouncements.TryGetValue(name, out var last) &&
                (now - last).TotalSeconds < CROSSING_DEDUP_WINDOW_SEC)
            {
                continue; // recently announced — skip
            }
            freshNames.Add(name);
        }

        if (freshNames.Count == 0)
        {
            // All names are duplicates of recent announcements — still mark this node
            // as "handled" so we don't retry on every frame while within range.
            _crossingAnnounced = true;
            _lastCrossingNodeId = junctionNode.NodeId;
            return;
        }

        // Hold this informational callout during the post-reach-warning grace
        // window so it doesn't stomp the warning at guidance start. Mark the node
        // handled so we don't re-test every frame while inside the window.
        if (DateTime.UtcNow < _startChatterSuppressUntil)
        {
            _crossingAnnounced = true;
            _lastCrossingNodeId = junctionNode.NodeId;
            return;
        }

        string label = freshNames.Count == 1
            ? $"Crossing taxiway {freshNames[0]}."
            : $"Crossing taxiways {string.Join(", ", freshNames)}.";

        _announcer.AnnounceImmediate(label);
        _crossingAnnounced = true;
        _lastCrossingNodeId = junctionNode.NodeId;

        // Record announcement times for dedup
        foreach (var name in freshNames)
            _recentCrossingAnnouncements[name] = now;

        // Occasional prune to keep the dict bounded on very long taxis.
        if (_recentCrossingAnnouncements.Count > 64)
        {
            var stale = _recentCrossingAnnouncements
                .Where(kv => (now - kv.Value).TotalSeconds > CROSSING_DEDUP_WINDOW_SEC * 2)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in stale) _recentCrossingAnnouncements.Remove(k);
        }
    }

    /// <summary>
    /// Speaks a tactical taxi instruction (turns, hold-shorts, taxiway
    /// changes, lineup, arrival, distance countdowns) and stores it in
    /// _lastInstruction for the Repeat-Last hotkey. Uses AnnounceImmediate
    /// (interrupts queued speech) because tactical callouts are time-critical:
    /// the pilot needs to act on "in 300 feet, turn left onto B" right now,
    /// not after a fading "10 knots" GS callout finishes speaking. The
    /// Repeat-Last buffer is updated even though the speech interrupts —
    /// the buffer is the user's "what did you just say?" recall.
    ///
    /// Use plain _announcer.Announce for peripheral, non-time-critical alerts
    /// (route summary at LoadRoute, ground-speed callouts) so they don't
    /// step on each other when stacked.
    /// </summary>
    private void AnnounceInstruction(string text)
    {
        _lastInstruction = text;
        _announcer.AnnounceImmediate(text);
    }

    /// <summary>
    /// Replays the most recent tactical instruction. Bound to Ctrl+Y in output
    /// mode. Returns a fallback string when no instruction has been recorded
    /// yet (e.g., guidance just started, or guidance is inactive).
    /// </summary>
    public string RepeatLastInstruction()
    {
        lock (_stateLock)
        {
            if (_state == TaxiGuidanceState.Inactive)
                return "No taxi guidance active.";
            if (string.IsNullOrEmpty(_lastInstruction))
                return "No taxi instruction yet.";
            return _lastInstruction;
        }
    }

    /// <summary>
    /// Gets a status string for on-demand announcement.
    /// Uses live aircraft position for accurate real-time distances.
    /// </summary>
    public string GetStatusAnnouncement()
    {
        lock (_stateLock)
        {
        if (_state == TaxiGuidanceState.Inactive)
            return "No taxi guidance active.";

        if (_state == TaxiGuidanceState.LandingRollout)
        {
            string gsStr = _positionInitialized
                ? $" Ground speed {(int)Math.Round(_lastGroundSpeedKts)} knots."
                : "";

            if (_rolloutNoExitMode)
            {
                if (_rolloutRunway != null && _positionInitialized)
                {
                    double alongFromStartM = SignedAlongRunwayMeters(
                        _lastLat, _lastLon,
                        _rolloutRunway.StartLat, _rolloutRunway.StartLon,
                        _rolloutRunwayHeadingTrue);
                    double distToEndFt = (_rolloutRunway.Length * 0.3048 - alongFromStartM) * METERS_TO_FEET;
                    double distFt = Math.Max(0, distToEndFt);
                    return $"Rolling out. No exit. Runway end in {DistanceFormatter.FromFeet(distFt)}.{gsStr}";
                }
                return $"Rolling out. No exit planned.{gsStr}";
            }

            if (_rolloutExit != null && _positionInitialized)
            {
                double distToExitM = TaxiGraph.FastDistanceMeters(
                    _lastLat, _lastLon, _rolloutExit.Latitude, _rolloutExit.Longitude);
                double distFt = Math.Max(0, distToExitM * METERS_TO_FEET);
                string exitName = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
                    ? "unnamed exit"
                    : $"taxiway {_rolloutExit.TaxiwayName}";
                return $"Rolling out. {_rolloutExit.ExitType} exit {exitName} in {DistanceFormatter.FromFeet(distFt)}.{gsStr}";
            }

            return $"Rolling out.{gsStr}";
        }

        if (_state == TaxiGuidanceState.BacktrackingOnRunway)
        {
            string gsStr = _positionInitialized
                ? $" Ground speed {(int)Math.Round(_lastGroundSpeedKts)} knots."
                : "";
            if (_backtrackConnectionNodeId > 0 && _positionInitialized)
            {
                double distM = TaxiGraph.FastDistanceMeters(
                    _lastLat, _lastLon, _backtrackConnectionLat, _backtrackConnectionLon);
                return $"Backtracking. {DistanceFormatter.FromMetres(distM)} to taxiway connection.{gsStr}";
            }
            return $"Backtracking.{gsStr}";
        }

        if (_state == TaxiGuidanceState.ProgressiveHold)
            return _progressiveTerminator?.EndAnnouncement()
                   ?? "Holding. Set a new route when cleared.";

        if (_route == null || _route.Segments.Count == 0)
            return "No route loaded.";

        if (_state == TaxiGuidanceState.HoldShort)
        {
            if (_holdShortAtDestination)
                return $"Holding short of {_destinationName}. Press continue when cleared.";
            if (_currentSegmentIndex > 0 && _currentSegmentIndex <= _route.Segments.Count)
            {
                var holdSeg = _route.Segments[_currentSegmentIndex - 1];
                if (!string.IsNullOrEmpty(holdSeg.HoldShortRunway))
                    return $"Holding short of {holdSeg.HoldShortRunway}. Press continue when cleared.";
            }
            return "Holding short. Press continue when cleared.";
        }

        if (_state == TaxiGuidanceState.LiningUp)
        {
            string what = _isRunwayLineup ? "runway" : "gate";
            int hdg = (int)Math.Round(_lineupHeadingMag);

            // Aligned: the tone is intentionally muted (we paused it on
            // alignment). Say so explicitly so a silent tone reads as
            // "done, hold position" rather than "lost / broken". With docking
            // PENDING the pilot must keep rolling to the GSX stop — a status
            // query must never tell them to hold short of it (KATL F3).
            if (_lineupAnnouncedAligned)
                return _dockingPending
                    ? $"Lined up {_destinationName}, heading {hdg}. Continue ahead for docking guidance."
                    : $"Lined up {_destinationName}, heading {hdg}. Aligned — hold position.";

            // Not yet aligned: surface distance-to-go so a silent tone
            // (between hysteresis pulses, or while the system is busy) still
            // has a progress readout the pilot can query on demand. No
            // cross-track feet — the tone is the cross-track instrument
            // (see the blind-pilot-cue rule); distance-remaining + heading
            // are the numbers a pilot can actually use.
            //
            // RUNWAY lineups measure SIGNED ALONG-RUNWAY distance to the lineup
            // point, NOT straight-line distance. Pilots normally reach the runway
            // at or downfield of the navdata start-table point, so the point
            // passes abeam and falls BEHIND during the turn onto the centerline —
            // straight-line distance then GROWS with every metre of correct
            // forward progress (KBWI 28, 2026-06-11: 55 m abeam climbing to
            // 165 m while the pilot sat 9 ft off centerline, 2° off heading).
            // The along-track projection decreases monotonically with forward
            // progress and is immune to lateral convergence; once the point is
            // abeam/behind (≤ LINEUP_TOGO_MIN_AHEAD_M) the distance is omitted
            // entirely — a lineup point behind the aircraft carries no
            // actionable information, and heading + tone own the alignment.
            // GATE lineups keep straight-line: the aircraft converges ONTO the
            // gate point, so the distance is meaningful all the way to zero.
            string lineupDistStr = "";
            if (_hasLineupTarget && _positionInitialized)
            {
                if (_isRunwayLineup)
                {
                    double aheadM = SignedAlongRunwayMeters(
                        _lineupTargetLat, _lineupTargetLon,
                        _lastLat, _lastLon, _lineupHeadingTrue);
                    if (aheadM > LINEUP_TOGO_MIN_AHEAD_M)
                        lineupDistStr = $" {FormatDistance(aheadM)} to go.";
                }
                else
                {
                    double dM = TaxiGraph.FastDistanceMeters(
                        _lastLat, _lastLon, _lineupTargetLat, _lineupTargetLon);
                    lineupDistStr = $" {FormatDistance(dM)} to go.";
                }
            }
            return $"Lining up {_destinationName}, {what} heading {hdg}. Follow the tone.{lineupDistStr}";
        }

        if (_state == TaxiGuidanceState.Arrived)
            return $"Arrived at {_route.DestinationName}.";

        if (_currentSegmentIndex >= _route.Segments.Count)
            return $"Arrived at {_route.DestinationName}.";

        var seg = _route.Segments[_currentSegmentIndex];
        string taxiway = !string.IsNullOrEmpty(seg.TaxiwayName) ? $"Taxiway {seg.TaxiwayName}" : "Connector";

        // Calculate remaining distance using live aircraft position (if we have one).
        // Use the explicit _positionInitialized flag instead of (lat!=0 || lon!=0) — the
        // latter would falsely trigger at the equator/prime meridian.
        double remaining = 0;
        if (_positionInitialized)
        {
            // Distance from aircraft to current segment's end
            remaining = TaxiGraph.FastDistanceMeters(
                _lastLat, _lastLon, seg.ToNode.Latitude, seg.ToNode.Longitude);
            // Plus all remaining segments after current
            for (int i = _currentSegmentIndex + 1; i < _route.Segments.Count; i++)
                remaining += _route.Segments[i].DistanceMeters;
        }
        else
        {
            for (int i = _currentSegmentIndex; i < _route.Segments.Count; i++)
                remaining += _route.Segments[i].DistanceMeters;
        }

        string distStr = FormatDistance(Math.Max(remaining, 0));

        // Next turn or taxiway change info
        string nextTurn = "";
        for (int i = _currentSegmentIndex + 1; i < _route.Segments.Count; i++)
        {
            var nextSeg = _route.Segments[i];
            bool isTurn = nextSeg.TurnDirection != "straight";
            bool isTaxiwayChange = !string.IsNullOrEmpty(nextSeg.TaxiwayName) &&
                !nextSeg.TaxiwayName.Equals(seg.TaxiwayName, StringComparison.OrdinalIgnoreCase);

            if (isTurn || isTaxiwayChange)
            {
                // Distance from aircraft to this event
                double turnDist = 0;
                if (_positionInitialized)
                {
                    turnDist = TaxiGraph.FastDistanceMeters(
                        _lastLat, _lastLon, seg.ToNode.Latitude, seg.ToNode.Longitude);
                    for (int j = _currentSegmentIndex + 1; j < i; j++)
                        turnDist += _route.Segments[j].DistanceMeters;
                }
                else
                {
                    for (int j = _currentSegmentIndex; j < i; j++)
                        turnDist += _route.Segments[j].DistanceMeters;
                }

                // Use the aircraft's current heading vs the next segment's bearing
                // so the spoken direction agrees with the steering tone — see
                // ComputeTurnVerbalFromHeading. _positionInitialized check above
                // already guarantees _lastHeading is fresh; if not, fall back to
                // the route's static intent (best-effort when no position has been
                // received yet).
                string turnAction;
                if (isTurn)
                {
                    turnAction = _positionInitialized
                        ? ComputeTurnVerbalFromHeading(nextSeg.BearingDegrees, _lastHeading)
                        : nextSeg.TurnDirection;
                }
                else
                {
                    turnAction = "continue";
                }
                string ontoStr = isTaxiwayChange ? $" onto taxiway {nextSeg.TaxiwayName}" : "";
                nextTurn = $", {turnAction}{ontoStr} in {FormatDistance(Math.Max(turnDist, 0))}";
                break;
            }
        }

        return $"{taxiway}{nextTurn}, {distStr} to {_destinationName}.";
        } // end lock(_stateLock)
    }

    private string BuildRouteSummary(
        TaxiRoute route, bool isRunwayDestination,
        TaxiLeadIn.LeadInInfo leadIn, string? firstClearedTaxiway)
    {
        string distStr = FormatDistance(route.TotalDistanceMeters);

        // If the user specified a taxiway sequence, use that for the announcement
        // (the actual route segments include approach/exit connectors the user doesn't care about)
        string taxiwayStr;
        if (route.TaxiwaySequence != null && route.TaxiwaySequence.Count > 0 &&
            string.IsNullOrEmpty(route.ConstrainedFallbackReason))
        {
            taxiwayStr = $" via {string.Join(", ", route.TaxiwaySequence)}";
        }
        else
        {
            // Unconstrained or fallback: list all taxiway names from segments
            var taxiways = new List<string>();
            foreach (var seg in route.Segments)
            {
                if (!string.IsNullOrEmpty(seg.TaxiwayName) &&
                    (taxiways.Count == 0 || !taxiways[^1].Equals(seg.TaxiwayName, StringComparison.OrdinalIgnoreCase)))
                {
                    taxiways.Add(seg.TaxiwayName);
                }
            }
            taxiwayStr = taxiways.Count > 0
                ? $" via {string.Join(", ", taxiways)}"
                : "";
        }

        // Describe the hold-short points. Runway crossings are NAMED, not just
        // counted — "2 hold short points" told the pilot nothing about the
        // route's shape, and at KSFO (2026-07-01, "Q hold short 10R" from D
        // between the 28s) the only route onto Q re-crossed 28R twice; the
        // pilot heard two unexplained "hold short of runway 10L" callouts and
        // perceived a giant loop. "crossing runway 10L twice" up front makes
        // the route's runway crossings audible before the pilot starts rolling.
        // For runway destinations, TruncateToHoldShort tags the last segment
        // purely as an internal countdown rail — it is NOT an ATC-assigned hold
        // point and is excluded (same exclusion the old bare count applied).
        bool excludeLastHold = isRunwayDestination && route.Segments.Count > 0 &&
            route.Segments[^1].IsHoldShortPoint;
        var (crossingClause, otherHolds) =
            RouteRunwayCrossings.Describe(route.Segments, excludeLastHold);
        string holdStr = "";
        if (crossingClause.Length > 0)
            holdStr += $", {crossingClause}";
        if (otherHolds > 0)
            holdStr += $", {otherHolds} hold short point{(otherHolds > 1 ? "s" : "")}";

        string fallbackStr = "";
        if (!string.IsNullOrEmpty(route.ConstrainedFallbackReason))
        {
            fallbackStr = $" Warning: could not follow specified taxiways. Using shortest path. Reason: {route.ConstrainedFallbackReason}";
        }

        string leadInStr = (firstClearedTaxiway != null)
            ? TaxiLeadIn.Clause(leadIn, firstClearedTaxiway)
            : "";

        return $"Route to {route.DestinationName}{taxiwayStr}.{leadInStr} {distStr}{holdStr}.{fallbackStr}";
    }

}
