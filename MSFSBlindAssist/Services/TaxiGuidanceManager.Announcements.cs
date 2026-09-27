using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public partial class TaxiGuidanceManager
{
    private void CheckUpcomingAnnouncements(double distToTargetM, TaxiRouteSegment currentSeg, double arrivalRadius)
    {
        if (_route == null) return;

        int nextIdx = _currentSegmentIndex + 1;
        if (nextIdx >= _route.Segments.Count)
        {
            // Hold during the start-warning grace window (_startChatterSuppressUntil,
            // armed by StartGuidance whenever a route-reach or unmapped-start warning
            // exists): this callout would otherwise cut off a warning spoken by the
            // form's standstill utterance or the first-frame one-shot at a standstill.
            // But holding is only safe when the callout can still be delivered once the
            // window closes -- StartWarningChatterGate.ShouldHold projects the aircraft's
            // ground speed forward to that moment and returns false (speak now) when it
            // would not.
            //
            // The raw distToTargetM is NOT the point this callout is actually lost at
            // (PR #238 review finding). On the final segment, UpdatePosition's own
            // "arrived" check -- just above the onFinalSegment block, using the SAME
            // arrivalRadius passed in below -- fires (and transitions state via
            // HandleArrival) once distToTargetM drops under that radius, not under zero.
            // Passing the raw distance here only narrowed the loss band the
            // WAYPOINT_CAPTURE_RADIUS_M fix below closes for the advance notice -- a route
            // shorter than APPROACH_ANNOUNCE_DISTANCE_M (100m), exactly the short
            // bridged-stand routes PR #235 creates, could still reach that radius inside
            // the window and lose "X ahead." outright.
            //
            // arrivalRadius is a PARAMETER, not recomputed here (PR #238 second-round
            // review) -- it is the exact same local UpdatePosition already computed a few
            // lines above its call into this method (TaxiGuidanceManager.cs), covering
            // every destination kind including a landing-exit route's own
            // LANDING_EXIT_ARRIVAL_RADIUS_M. A local copy of that ternary here could drift
            // from the original the moment either constant is re-tuned or a new
            // destination-kind branch is added there — the gate would keep measuring to
            // the OLD radius while UpdatePosition's own "arrived" check already moved to
            // the new one, silently reopening the loss band this method exists to close,
            // and nothing would fail: the StartWarningChatterGateTests hardcode the
            // radius as a literal, so they cannot see this class recompute the wrong one.
            //
            // Don't set _approachAnnounced when holding, so it still fires normally on
            // the first frame after the window closes if it still applies.
            // TryAnnounceCurve and the advance-notice block below hold the same way;
            // "turn now" already waits on _approachAnnounced so it needs no extra gate,
            // but AdvanceSegment clears that latch (and _turnImminentAnnounced) as soon as
            // the aircraft comes within WAYPOINT_CAPTURE_RADIUS_M of the junction -- 25m
            // BEFORE it is reached, not at it -- which is how a lost advance notice used
            // to cost "turn now" too, permanently rather than just late.
            if (distToTargetM < APPROACH_ANNOUNCE_DISTANCE_M && !_approachAnnounced &&
                !StartWarningChatterGate.ShouldHold(
                    DateTime.UtcNow, _startChatterSuppressUntil, distToTargetM, arrivalRadius, _lastGroundSpeedKts))
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
        TryAnnounceCurve(distToTargetM);

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
            _lastGroundSpeedKts * StartWarningChatterGate.MetersPerSecondPerKnot * APPROACH_ANNOUNCE_SEC_LEAD,
            APPROACH_ANNOUNCE_MIN_M, APPROACH_ANNOUNCE_MAX_M);
        if (sharpTurn) approachDist *= 1.5;

        // Advance notice (speed-scaled). Terse: direction + taxiway + angle for sharp.
        // Speed-slow warning is owned by CheckSpeedWarnings — don't duplicate it here.
        // Held during the start-warning grace window (see the comment above in this
        // method) via the same StartWarningChatterGate, so it can't cut off a start
        // warning at a standstill AND can't be lost outright when the junction it
        // describes is close enough to be reached before the window closes.
        //
        // The junction itself is NOT where this callout is actually lost (PR #238
        // review finding, CRITICAL): AdvanceSegment (TaxiGuidanceManager.Routing.cs)
        // clears _approachAnnounced/_turnImminentAnnounced as soon as distToTargetM
        // drops under WAYPOINT_CAPTURE_RADIUS_M (25m) -- 25m BEFORE the junction, not at
        // it. Passing the raw distToTargetM here left a 25m-wide loss band open: a
        // junction reached inside the grace window could be farther than the true
        // (junction - 25m) loss point yet still closer than the junction itself, so
        // ShouldHold kept saying "hold" right up until the latch was already cleared and
        // the callout was gone for good. Hand the gate WAYPOINT_CAPTURE_RADIUS_M as its
        // clear radius so it measures to the point where AdvanceSegment actually fires,
        // not to the junction.
        if (distToTargetM < approachDist && !_approachAnnounced &&
            !StartWarningChatterGate.ShouldHold(
                DateTime.UtcNow, _startChatterSuppressUntil,
                distToTargetM, WAYPOINT_CAPTURE_RADIUS_M, _lastGroundSpeedKts))
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
        // 1 knot = 0.5144 m/s (StartWarningChatterGate.MetersPerSecondPerKnot); 4 sec * m/s = meters of lead.
        double turnImminentDistance = Math.Clamp(
            _lastGroundSpeedKts * StartWarningChatterGate.MetersPerSecondPerKnot * TURN_IMMINENT_SEC_LEAD,
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

    private void TryAnnounceCurve(double distToTargetM)
    {
        if (_route == null || _route.Segments.Count == 0 ||
            _currentSegmentIndex >= _route.Segments.Count) return;

        // Hold during the start-warning grace window (see the comment in
        // CheckUpcomingAnnouncements) via the same StartWarningChatterGate — return
        // before touching _curveAnnouncedSign so the cue still fires normally once the
        // window closes.
        //
        // Measure against distToTargetM -- the distance to the CURRENT segment's end
        // node -- not the fixed CURVE_SCAN_WINDOW_M lookahead (PR #238 review finding).
        // CumulativeTurnDeg's scan below starts at that same end node, so distToTargetM
        // is the genuine "when does this geometry go behind me" distance: the first bend
        // the scan would accumulate. CURVE_SCAN_WINDOW_M is a fixed constant with no
        // relation to where the aircraft actually is, and holding against it degenerated
        // ShouldHold to a bare speed threshold -- under ~15.55kt (100m / (0.5144 * this
        // class's 12.5s window)) it held for the WHOLE window regardless of real
        // geometry, so the fix did nothing at this site; above that speed it was the
        // only non-monotonic call site (ShouldHold could say "speak now" at the top of
        // the window and flip back to "hold" moments later as the window's remaining
        // time shrank). distToTargetM needs no clear-radius ADJUSTMENT here (passed as
        // clearRadiusMeters: 0 below) -- but that is not because nothing can lose this
        // callout, just that it isn't lost via a latch. (PR #238 second-round review,
        // MINOR finding: an earlier version of this comment justified the 0 with
        // "AdvanceSegment does not reset _curveAnnouncedSign", which is true but not the
        // actual loss mode.) The real mechanism is geometric: when AdvanceSegment fires
        // at WAYPOINT_CAPTURE_RADIUS_M (25m), _currentSegmentIndex increments and
        // CumulativeTurnDeg's scan above starts fresh from the NEW current segment's end
        // node, so the first bend -- the one distToTargetM measures to -- drops out of
        // the cumulative sum 25m early, same loss as the junction callouts above suffer,
        // just with no flag to point at. distToTargetM is still the right value to pass;
        // there is simply no second, closer point (short of the bend itself) where that
        // dropping-out happens, so there is nothing to subtract.
        if (StartWarningChatterGate.ShouldHold(
                DateTime.UtcNow, _startChatterSuppressUntil,
                distToTargetM, clearRadiusMeters: 0, _lastGroundSpeedKts))
            return;

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
    /// An instruction that must FOLLOW whatever is being spoken rather than cut it off - recorded for Ctrl+Y
    /// exactly as <see cref="AnnounceInstruction"/> records one, but queued.
    /// </summary>
    private void AnnounceQueuedInstruction(string text)
    {
        _lastInstruction = text;
        _announcer.Announce(text);
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

        // Full-length backtrack DEPARTURE. This branch must stay ABOVE the route
        // checks below: the route ENDED at the intermediate entrance, so
        // _currentSegmentIndex is already past the last segment and the query would
        // otherwise fall through to "Arrived at {destination}." — telling a pilot
        // rolling the wrong way down an active runway that the maneuver is over.
        if (_state == TaxiGuidanceState.BacktrackDeparture)
        {
            string gsStr = _positionInitialized
                ? $" Ground speed {(int)Math.Round(_lastGroundSpeedKts)} knots."
                : "";
            int turnHdg = (int)Math.Round(_lineupHeadingMag);
            if (_hasLineupTarget && _positionInitialized)
            {
                double distM = TaxiGraph.FastDistanceMeters(
                    _lastLat, _lastLon, _lineupTargetLat, _lineupTargetLon);
                return $"Backtracking {_destinationName} to full length. " +
                       $"{FormatDistance(distM)} to go, then turn around to heading {turnHdg}.{gsStr}";
            }
            return $"Backtracking {_destinationName} to full length. " +
                   $"Turn around to heading {turnHdg} at the runway end.{gsStr}";
        }

        if (_state == TaxiGuidanceState.ProgressiveHold)
            return _progressiveTerminator?.EndAnnouncement()
                   ?? "Holding. Set a new route when cleared.";

        if (_route == null || _route.Segments.Count == 0)
            return "No route loaded.";

        if (_state == TaxiGuidanceState.HoldShort)
        {
            // Part-way through a STAGED hold (a stop guarding more than one runway), the status must
            // say which runway the NEXT press authorises — found in flight at KJFK 31L+04L, where it
            // answered "Press continue when cleared." and named both runways as though nothing had
            // been cleared. The destination hold is never staged, so its branch is untouched.
            string? stagedNext = !_holdShortAtDestination && _holdStageIndex < _holdStages.Count
                ? _holdStages[_holdStageIndex]
                : null;

            if (_holdShortAtDestination)
                return $"Holding short of {_destinationName}. Press continue when cleared.";

            // The same derivation the ground-traffic runway watch uses (Navigation.HeldRunwayLabel).
            string? held = Navigation.HeldRunwayLabel.Resolve(
                false, _destinationName, _currentSegmentIndex, _route.StartHoldRunway,
                _route.Segments.Select(s => s.HoldShortRunway).ToList());
            if (held != null)
                return stagedNext != null
                    ? Navigation.RunwayHoldStages.ComposeStatus(held, stagedNext)
                    : $"Holding short of {held}. Press continue when cleared.";
            return stagedNext != null
                ? Navigation.RunwayHoldStages.ComposeStatus(null, stagedNext)
                : "Holding short. Press continue when cleared.";
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
            // Unconstrained or fallback: list all taxiway names from segments.
            // Shared with the recalculation's via-list so the two spoken descriptions of a
            // route cannot disagree about which taxiways it uses.
            var taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments);
            taxiwayStr = taxiways.Count > 0
                ? $" via {string.Join(", ", taxiways)}"
                : "";
        }

        // Every runway the route crosses or enters is NAMED, held or not — "2 hold short points"
        // told the pilot nothing about the route's shape, and at KSFO (2026-07-01) two unexplained
        // "hold short of runway 10L" callouts read as a giant loop. The clause comes from the route's
        // recorded events, never from hold labels, so a crossing that could not be held is still
        // said. Other hold-short points (end of taxiway, named holding points) are counted; a
        // runway destination's own countdown rail is excluded (ShouldExcludeFinalHold).
        string crossingClause = RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents);
        int otherHolds = RouteRunwayCrossings.CountNonRunwayHoldShorts(
            route.Segments,
            RouteRunwayCrossings.ShouldExcludeFinalHold(route.Segments, isRunwayDestination));
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
