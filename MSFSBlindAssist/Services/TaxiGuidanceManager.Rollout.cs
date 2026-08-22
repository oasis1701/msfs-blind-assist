using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public partial class TaxiGuidanceManager
{
    /// <summary>
    /// Resets the rollout-phase approach-callout latches so the next rollout
    /// entry path can re-fire the 1500 / 900 / 500 ft callouts, the "turn now"
    /// cue, and the exit-tone arming gate. Sites that reset additional rollout
    /// state (NoGraph, EnterRunwayEndCountdown, RetargetLandingExit, etc.) keep
    /// their site-specific resets inline next to the call.
    /// </summary>
    private void ResetRolloutApproachLatches()
    {
        _rolloutApproach1500Announced = false;
        _rolloutApproach900Announced = false;
        _rolloutApproach500Announced = false;
        _rolloutTurnNowAnnounced = false;
        _rolloutToneMode = Navigation.RolloutToneMode.Silent;
    }

    /// <summary>
    /// Switches active guidance into landing-rollout mode. Called by
    /// <see cref="LandingExitPlanner"/> after StartGuidance, before the
    /// aircraft has decelerated to taxi speed.
    ///
    /// Effects:
    ///   • Steering tone is paused — at runway speed the pilot is on
    ///     rudder, not steering toward a taxi-graph waypoint, and a tone
    ///     firing on small heading offsets between the route's first
    ///     graph node and the actual touchdown point would be misleading.
    ///   • State is set to <see cref="TaxiGuidanceState.LandingRollout"/>;
    ///     <c>UpdateLandingRollout</c> takes over the per-frame loop.
    ///   • A touchdown callout is announced immediately:
    ///     "Touchdown. {High-speed / Normal} exit {name} in {N} feet."
    ///   • Distance-based callouts at 1500/500 ft and a "turn now" cue
    ///     follow as the aircraft approaches the exit (see UpdateLandingRollout).
    ///   • State auto-transitions back to Taxiing once GS drops below
    ///     ROLLOUT_TAXI_GS_KTS or aircraft heading deviates more than
    ///     ROLLOUT_TURN_BEGAN_HDG_DEG from <paramref name="runwayHeadingTrue"/>.
    ///
    /// Must be called AFTER <see cref="StartGuidance"/> — relies on the
    /// route already being loaded and the tone generator already alive
    /// (so Pause/Resume do the right thing).
    /// </summary>
    public void BeginLandingRollout(
        Navigation.LandingExit exit,
        double runwayHeadingTrue,
        Database.Models.Runway runway,
        List<Navigation.LandingExit> allExits,
        double touchdownLat = 0,
        double touchdownLon = 0)
    {
        lock (_stateLock)
        {
            // DIAGNOSTIC: capture entry state to landing_exit.log
            RolloutDiag($"BeginLandingRollout entry: state={_state} " +
                $"prior _rolloutNoExitMode={_rolloutNoExitMode} " +
                $"_route={(_route == null ? "null" : $"segs={_route.Segments.Count}")} " +
                $"exit.Lat={exit.Latitude:F6} exit.Lon={exit.Longitude:F6} " +
                $"exit.TaxiwayName='{exit.TaxiwayName}' exit.NodeId={exit.NodeId} " +
                $"runway.RunwayID='{runway.RunwayID}' runway.Length={runway.Length:F0} " +
                $"runwayHeadingTrue={runwayHeadingTrue:F2} allExits.Count={allExits.Count}");

            if (_route == null || _route.Segments.Count == 0)
            {
                RolloutDiag("BeginLandingRollout EARLY-RETURN: _route null or empty");
                return;
            }

            _rolloutExit = exit;
            _isLandingExitRoute = true; // arrival message will direct the pilot to the gate planner
            _rolloutRunwayHeadingTrue = runwayHeadingTrue;
            _rolloutRunway = runway;
            _rolloutAllExits = allExits;
            ResetRolloutApproachLatches();
            _rolloutEarlyHandoffDone = false;
            _lastUndershootRetargetTime = DateTime.MinValue;
            // Defense in depth: clear no-exit/runway-end state from any prior
            // rollout. StopGuidance does this; matching the pattern here
            // ensures we never inherit stale flags when starting a fresh
            // landing-exit flow. Currently no known path leaks these to the
            // next BeginLandingRollout (StopGuidance fires on takeoff via
            // OnTakeoffAssistActiveChanged), but defensive is cheap.
            _rolloutNoExitMode = false;
            _rolloutHandoffActive = false;
            _rolloutEnd1500Announced = false;
            _rolloutEnd500Announced = false;
            _rolloutEnd100Announced = false;
            // DIAGNOSTIC: reset per-rollout instrumentation gates
            _rolloutDiagFirstCallDone = false;
            _rolloutDiagLastPeriodic = DateTime.MinValue;

            // Reset the heading-error smoother so any taxi-phase residual doesn't
            // bleed into the rollout tone and steer the pilot off-axis at the worst
            // possible moment (high speed, on runway). UpdateLandingRollout drives
            // the tone every frame using bearing-to-exit as the desired heading.
            _smoothedHeadingError = 0.0;
            _headingErrorInitialized = false;
            _steeringTone.SetPulse(false);

            SetState(TaxiGuidanceState.LandingRollout);

            RolloutDiag($"BeginLandingRollout DONE: state -> LandingRollout, " +
                $"tone active (bearing-to-exit), touchdown callout queued");

            // Touchdown callout. Use the actual aircraft-to-exit distance when the
            // caller provides touchdown coordinates — this accounts for short or long
            // landings. Fall back to the precomputed DistanceFromTouchdownFeet only
            // when coordinates are unavailable (shouldn't happen in normal flow).
            string exitClass = exit.ExitType switch
            {
                "High-speed" => "high-speed exit",
                "End"        => "runway-end exit",
                _            => "exit"
            };
            string name = string.IsNullOrEmpty(exit.TaxiwayName) ? "exit" : $"taxiway {exit.TaxiwayName}";
            int distFt = touchdownLat != 0 && touchdownLon != 0
                ? (int)Math.Round(TaxiGraph.FastDistanceMeters(touchdownLat, touchdownLon, exit.Latitude, exit.Longitude) * METERS_TO_FEET)
                : (int)Math.Round(exit.DistanceFromTouchdownFeet);
            if (distFt > 0)
                AnnounceInstruction($"Touchdown. {exitClass} {name} in {DistanceFormatter.FromFeet(distFt)}.");
            else
                AnnounceInstruction($"Touchdown. {exitClass} {name}.");
        }
    }

    /// <summary>
    /// Enters landing-rollout mode with full exit guidance even when the initial
    /// A* route from touchdown to the exit failed (e.g. disconnected graph components).
    ///
    /// The rollout distance callouts (1500/500/150 ft), steering tone, overshoot
    /// detection, and handoff to Taxiing all use exit geometry directly — not the
    /// route — so they work without one. At handoff time (turnBegun / exitedLaterally),
    /// <see cref="UpdateLandingRollout"/> calls LoadRoute from the live aircraft position
    /// which by then is within the exit's graph component, so that re-route succeeds.
    ///
    /// Must be called AFTER a failed <see cref="LoadRoute"/> so that _graph,
    /// _dataProvider, and _icao are populated for the subsequent handoff re-route.
    /// </summary>
    public void BeginLandingRolloutNoGraph(
        Navigation.LandingExit exit,
        double runwayHeadingTrue,
        Database.Models.Runway runway,
        List<Navigation.LandingExit> allExits,
        double touchdownLat,
        double touchdownLon,
        UserSettings settings)
    {
        lock (_stateLock)
        {
            // Guarantee the handoff-failure fallback's _route == null invariant.
            // LoadRoute's failure paths leave _route untouched, so a stale value
            // from a prior route (e.g., hand-flown departure that didn't call
            // StopGuidance) could survive into this NoGraph entry. Nulling here
            // ensures the !handoffRerouted && _route == null branch in
            // UpdateLandingRollout will fire if the eventual handoff re-route
            // also fails, instead of driving the steering tone against stale
            // departure-airport segments.
            _route = null;

            // Precondition: a prior LoadRoute (even one that failed at route
            // construction) must have populated _graph, _dataProvider, and _icao.
            // The handoff re-route in UpdateLandingRollout reads these directly.
            // A null here means the caller skipped the LoadRoute attempt — log
            // and proceed (geometry-driven callouts and tone still work, but the
            // handoff re-route will fail until normal taxi guidance kicks in).
            if (_graph == null || _dataProvider == null || string.IsNullOrEmpty(_icao))
            {
                RolloutDiag($"BeginLandingRolloutNoGraph precondition not met: " +
                    $"_graph={(_graph == null ? "null" : "set")} " +
                    $"_dataProvider={(_dataProvider == null ? "null" : "set")} " +
                    $"_icao='{_icao}' — handoff re-route will not succeed.");
            }

            // Start the tone now — StartGuidance (which normally does this) was
            // never called because LoadRoute failed.
            _announceCrossings = settings.TaxiGuidanceAnnounceCrossings;
            _steeringTone.InvertPan = settings.TaxiGuidanceInvertSteeringTone;
            _steeringTone.HardPan = settings.TaxiGuidanceHardPanTone;
            _steeringTone.Start(settings.TaxiGuidanceToneWaveform, settings.TaxiGuidanceToneVolume);

            // Populate rollout state (mirrors BeginLandingRollout, without the _route guard).
            _rolloutExit = exit;
            _isLandingExitRoute = true;
            _rolloutRunwayHeadingTrue = runwayHeadingTrue;
            _rolloutRunway = runway;
            _rolloutAllExits = allExits;
            ResetRolloutApproachLatches();
            _rolloutEarlyHandoffDone = false;
            _lastUndershootRetargetTime = DateTime.MinValue;
            _rolloutNoExitMode = false;
            _rolloutHandoffActive = false;
            _rolloutEnd1500Announced = false;
            _rolloutEnd500Announced = false;
            _rolloutEnd100Announced = false;
            _rolloutDiagFirstCallDone = false;
            _rolloutDiagLastPeriodic = DateTime.MinValue;
            _smoothedHeadingError = 0.0;
            _headingErrorInitialized = false;
            _steeringTone.SetPulse(false);

            SetState(TaxiGuidanceState.LandingRollout);

            RolloutDiag($"BeginLandingRolloutNoGraph: exit='{exit.TaxiwayName}' node={exit.NodeId} " +
                $"runway={runway.RunwayID} hdgTrue={runwayHeadingTrue:F2} allExits={allExits.Count}");

            // Touchdown callout — same logic as BeginLandingRollout.
            string exitClass = exit.ExitType switch
            {
                "High-speed" => "high-speed exit",
                "End"        => "runway-end exit",
                _            => "exit"
            };
            string name = string.IsNullOrEmpty(exit.TaxiwayName) ? "exit" : $"taxiway {exit.TaxiwayName}";
            int distFt = touchdownLat != 0 && touchdownLon != 0
                ? (int)Math.Round(TaxiGraph.FastDistanceMeters(touchdownLat, touchdownLon, exit.Latitude, exit.Longitude) * METERS_TO_FEET)
                : (int)Math.Round(exit.DistanceFromTouchdownFeet);
            if (distFt > 0)
                AnnounceInstruction($"Touchdown. {exitClass} {name} in {DistanceFormatter.FromFeet(distFt)}.");
            else
                AnnounceInstruction($"Touchdown. {exitClass} {name}.");
        }
    }

    /// <summary>
    /// Per-frame logic while in <see cref="TaxiGuidanceState.LandingRollout"/>.
    ///
    /// Tone is kept paused — pilot is decelerating in a straight line on
    /// rudder, not steering toward a graph waypoint. Callouts:
    ///
    /// • <b>1500 ft from exit</b> — "Approaching {high-speed/normal/end} exit
    ///   {name}, 1500 feet."
    /// • <b>500 ft from exit</b> — "{name}, 500 feet, slow down." (the
    ///   "slow down" suffix is dropped if GS is already below
    ///   ROLLOUT_TAXI_GS_KTS, mirroring how the hold-short countdown is
    ///   speed-aware.)
    /// • <b>~150 ft from exit</b> — "Turn {left/right} now, taxiway {name}."
    ///   Direction is computed from aircraft heading vs bearing-to-exit so
    ///   it matches what the pilot needs to do regardless of which side of
    ///   the runway the exit sits on.
    ///
    /// Transition to Taxiing happens when EITHER:
    ///   • Ground speed drops below ROLLOUT_TAXI_GS_KTS, OR
    ///   • Aircraft heading deviates more than ROLLOUT_TURN_BEGAN_HDG_DEG
    ///     from the runway heading (turn onto exit is underway, even at
    ///     high speed on a Code-E rapid exit).
    /// On transition the tone is resumed and the normal taxi-guidance loop
    /// takes over (it'll find the nearest segment and steer the pilot
    /// onto the chosen exit taxiway).
    /// </summary>
    private void UpdateLandingRollout(double lat, double lon, double headingTrue, double groundSpeedKts)
    {
        // DIAGNOSTIC: first-call snapshot per rollout
        if (!_rolloutDiagFirstCallDone)
        {
            _rolloutDiagFirstCallDone = true;
            RolloutDiag($"UpdateLandingRollout FIRST: state={_state} " +
                $"_rolloutNoExitMode={_rolloutNoExitMode} " +
                $"_rolloutExit={(_rolloutExit == null ? "null" : $"lat={_rolloutExit.Latitude:F6} lon={_rolloutExit.Longitude:F6} name='{_rolloutExit.TaxiwayName}'")} " +
                $"_rolloutRunway={(_rolloutRunway == null ? "null" : $"id='{_rolloutRunway.RunwayID}' len={_rolloutRunway.Length:F0}")} " +
                $"_rolloutRunwayHeadingTrue={_rolloutRunwayHeadingTrue:F2} " +
                $"_rolloutAllExits.Count={_rolloutAllExits.Count} " +
                $"acft.lat={lat:F6} acft.lon={lon:F6} hdg={headingTrue:F2} gs={groundSpeedKts:F1}");
        }

        if (_rolloutNoExitMode)
        {
            UpdateRunwayEndCountdown(lat, lon, headingTrue, groundSpeedKts);
            return;
        }

        if (_rolloutExit == null)
        {
            RolloutDiag("UpdateLandingRollout: _rolloutExit NULL, switching to Taxiing");
            // Defensive — should never happen because BeginLandingRollout
            // populates this. If it does, fall back to normal Taxiing.
            SetState(TaxiGuidanceState.Taxiing);
            _steeringTone.Resume();
            return;
        }

        // Distance from current position to the chosen exit (feet).
        double distToExitFeet =
            TaxiGraph.FastDistanceMeters(lat, lon, _rolloutExit.Latitude, _rolloutExit.Longitude)
            * METERS_TO_FEET;

        // Heading deviation from the runway centreline, signed, POSITIVE = RIGHT.
        // The SIGN is load-bearing: a deviation away from the exit's own side is drift,
        // not the exit turn (KSEA 34L 2026-08-21). hdgDeltaAbs is still what the lateral
        // and overshoot gates below want.
        double hdgDelta = NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue);
        double hdgDeltaAbs = Math.Abs(hdgDelta);

        // Compute along-runway projection up front — both the handoff gate
        // and the overshoot detector below need it. Positive means the
        // aircraft has moved past the exit in the runway heading direction;
        // negative means still upfield.
        double signedAlongPastM = SignedAlongRunwayMeters(
            lat, lon,
            _rolloutExit.Latitude, _rolloutExit.Longitude,
            _rolloutRunwayHeadingTrue);
        double signedAlongPastFt = signedAlongPastM * METERS_TO_FEET;

        bool atTaxiSpeed = groundSpeedKts < ROLLOUT_TAXI_GS_KTS;
        bool nearExit = distToExitFeet < ROLLOUT_NEAR_EXIT_FT;
        bool pastExit = signedAlongPastFt > 0.0;
        // Relative bearing of the chosen exit from the runway heading, same sign convention.
        // ExitBearingTrue == 0.0 is the "unknown" sentinel and normalises into the sub-3°
        // band that disables the direction test — the intended degradation.
        double exitRelBearingDeg = _rolloutExit.ExitBearingTrue != 0.0
            ? NormalizeAngle(_rolloutExit.ExitBearingTrue - _rolloutRunwayHeadingTrue)
            : 0.0;

        // Speed-gated: above ROLLOUT_TURN_MAX_GS_KTS a heading deviation is touchdown yaw /
        // crab alignment, not a deliberate runway exit turn. Direction- and proximity-gated
        // since 2026-08: see Navigation/RolloutExitGate.IsExitTurnBegun.
        bool turnBegun = Navigation.RolloutExitGate.IsExitTurnBegun(
            hdgDelta, groundSpeedKts, distToExitFeet, pastExit, exitRelBearingDeg);
        // Effectively stopped before reaching the exit — e.g. pilot braked
        // hard after an undershoot retarget left the exit 500+ ft away.
        // The atTaxiSpeed&&nearExit gate intentionally doesn't fire this far
        // out (it prevents premature tone on long runways), but a fully stopped
        // aircraft needs to continue as Taxiing so they can taxi to the exit.
        // pastExit guard: let the overshoot detector handle the past-exit case.
        bool trulyStopped = groundSpeedKts < ROLLOUT_NO_EXIT_STOPPED_GS_KTS && !pastExit;

        // DIAGNOSTIC: periodic snapshot (every ~3s) of rollout state.
        // Captures the moment the per-frame loop is or isn't seeing the
        // distance threshold approach.
        if ((DateTime.UtcNow - _rolloutDiagLastPeriodic).TotalSeconds >= 3.0)
        {
            _rolloutDiagLastPeriodic = DateTime.UtcNow;
            RolloutDiag($"UpdateLandingRollout periodic: " +
                $"distToExit={distToExitFeet:F0}ft signedAlongPast={signedAlongPastFt:F0}ft " +
                $"hdgDelta={hdgDeltaAbs:F1}deg gs={groundSpeedKts:F1}kt " +
                $"atTaxiSpeed={atTaxiSpeed} nearExit={nearExit} pastExit={pastExit} turnBegun={turnBegun} " +
                $"approach1500Done={_rolloutApproach1500Announced} " +
                $"approach500Done={_rolloutApproach500Announced} " +
                $"turnNowDone={_rolloutTurnNowAnnounced}");
        }

        // Transition to Taxiing once EITHER (a) the pilot has actually begun
        // the turn off the runway, OR (b) the pilot has decelerated to taxi
        // speed AND is within ROLLOUT_NEAR_EXIT_FT of the chosen exit AND
        // has not yet crossed it. The !pastExit guard is critical: without
        // it, a pilot who decelerates to taxi speed and then drifts past
        // the exit on centerline (0-to-100 ft post-exit window) would hit
        // the handoff and enter Taxiing with _destinationNodeId still
        // pointing at the just-missed exit — the off-route recalc would
        // then route back across the runway. The overshoot detector below
        // handles any case where the aircraft IS past the exit.
        // Lateral-exit handoff: aircraft has moved off the runway sideways far
        // enough to be on the exit taxiway, but heading deviation never reached
        // ROLLOUT_TURN_BEGAN_HDG_DEG (true for any shallow RET < 15°). Treat
        // this the same as turnBegun — the exit has been taken.
        // halfRunwayWidthFt + 30 ft is computed below for the overshoot gate
        // but we need the values before that block, so compute them here.
        double halfRunwayWidthFt = (_rolloutRunway?.Width > 0 ? _rolloutRunway.Width : 200.0) * 0.5;
        // Use the runway start as the reference point for lateral measurement, NOT
        // the exit node. Exit nodes (especially HS/IHS hold-short markers) can be
        // positioned up to halfWidth+15 m from the actual centerline — e.g. EIDW N4
        // hold-short nodes sit ~40 m off-center. Using the exit node as the reference
        // means AbsLateralFromRunwayMeters returns ~40 m (131 ft) for an aircraft on
        // the centerline, which immediately exceeds halfRunwayWidthFt+30 (≈128 ft) and
        // fires exitedLaterally at touchdown before the pilot has moved at all. Any
        // point on the actual centerline (the runway start) gives the correct 0 ft
        // reading for a centered aircraft, growing only as the pilot turns off-runway.
        double lateralFromCenterlineFt = AbsLateralFromRunwayMeters(
            lat, lon,
            _rolloutRunway!.StartLat, _rolloutRunway.StartLon,
            _rolloutRunwayHeadingTrue) * METERS_TO_FEET;
        // Combined gate: the bare lateral threshold (halfWidth+30 ft) fires too
        // eagerly when the pilot drifts laterally during the rollout silent-tone
        // phase, BEFORE the 150 ft "turn now" verbal has had a chance to fire.
        // EDDB 24L → M3 reproduction: lateral=129 ft at distToExit=445 ft and
        // hdgDelta=6.6° triggered handoff before the verbal cue.
        //
        // Original intent of exitedLaterally is to recognise the "shallow RET
        // drift-off" case where heading never reaches the 15° turnBegun threshold
        // but the aircraft has clearly moved onto the exit taxiway. That intent
        // is preserved by the three OR'd conditions below — at least one will be
        // true any time the pilot is genuinely committed to the exit.
        //
        // Conditions (any one triggers when lateral threshold is met):
        //   (a) distToExitFeet <= 250 — within turn-cue range. The 150 ft "turn
        //       now" verbal will have fired (or is about to); lateral drift here
        //       is committing, not anticipatory.
        //   (b) hdgDeltaAbs >= 8.0 — heading commitment. Below the 15° turnBegun
        //       threshold but enough to distinguish anticipatory steering from
        //       passive drift. 8° = half of turnBegun.
        //   (c) pastExit — overshoot. Preserves the existing behavior for the
        //       overshoot detector downstream.
        //
        // True shallow RETs (<8° real exit angle): the dist gate fires once the
        // aircraft closes to <=250 ft of the exit — no regression. 90° normal
        // exits: turnBegun fires almost immediately upon turn, before the lateral
        // gate is relevant. No behavioral change for the common case.
        bool exitedLaterally = lateralFromCenterlineFt >= halfRunwayWidthFt + 30.0
                               && (distToExitFeet <= 250.0
                                   || hdgDeltaAbs >= 8.0
                                   || pastExit);

        // Heading-aligned-with-exit handoff for shallow RETs whose angle is
        // below ROLLOUT_TURN_BEGAN_HDG_DEG (15°), so turnBegun never fires.
        // Fires when the aircraft heading is within 5° of ExitBearingTrue AND
        // has deviated at least 70% of the exit angle from runway heading.
        //
        // The 70% floor is the key overshoot guard: a pilot holding a crosswind
        // correction equal to, say, 2° while rolling past a 3° exit would need
        // hdgDelta ≥ 2.1° to satisfy both conditions simultaneously — the exact
        // runway-heading case (0° deviation, genuine missed exit) can never fire.
        // ExitAngleDegrees < 3° exits are excluded: they are geometrically
        // indistinguishable from "rolled straight" and aren't actionable anyway.
        double exitBrgErr = _rolloutExit.ExitBearingTrue != 0.0
            ? Math.Abs(NormalizeAngle(headingTrue - _rolloutExit.ExitBearingTrue))
            : double.MaxValue;
        bool alignedWithExit = _rolloutExit.ExitBearingTrue != 0.0
            && _rolloutExit.ExitAngleDegrees >= 3.0
            && exitBrgErr <= 5.0
            && hdgDeltaAbs >= Math.Max(2.0, _rolloutExit.ExitAngleDegrees * 0.7)
            && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS
            && pastExit;

        // Speed-based "decelerated near the exit" handoff. EXCLUDED for high-speed
        // (rapid-exit) taxiways. On a normal-deceleration landing the aircraft is
        // already below ROLLOUT_TAXI_GS_KTS (30 kt) a few hundred feet short of a
        // mid-field RET, so this gate would fire while still dead-centre on the
        // runway and PREEMPT the exit-bearing tone (arms ≤300 ft), the high-speed
        // TryEarlyExitHandoff (≤300 ft) and the 150 ft "turn now" verbal — all of
        // which live AFTER this handoff's return. That stranded the pilot with no
        // directional turn cue and let them roll past the exit (EDDM 26L → B6:
        // handoff fired at distToExit=311 ft, lateral=3 ft, hdgDelta=0°, then the
        // overshoot monitor retargeted to the next exit). High-speed exits therefore
        // hand off via TryEarlyExitHandoff / turnBegun / exitedLaterally /
        // alignedWithExit / trulyStopped instead, so their guidance gets to run.
        // Normal/End exits keep the speed-gate: their hard turn is guided fine by the
        // post-handoff re-route and turnBegun (15°) fires almost immediately on a 90°
        // exit, so there is no equivalent preemption window to lose.
        bool speedNearExitHandoff = atTaxiSpeed && nearExit && !pastExit
                                    && _rolloutExit.ExitType != "High-speed";

        if (turnBegun || exitedLaterally || alignedWithExit || speedNearExitHandoff || trulyStopped)
        {
            RolloutDiag($"UpdateLandingRollout HANDOFF -> Taxiing: " +
                $"turnBegun={turnBegun} exitedLaterally={exitedLaterally} alignedWithExit={alignedWithExit} " +
                $"exitBrgErr={exitBrgErr:F1}deg lateral={lateralFromCenterlineFt:F0}ft " +
                $"distToExit={distToExitFeet:F0}ft hdgDelta={hdgDeltaAbs:F1}deg " +
                $"atTaxiSpeed={atTaxiSpeed} nearExit={nearExit} pastExit={pastExit} trulyStopped={trulyStopped}");

            // Arm post-handoff overshoot monitor so UpdatePosition can detect
            // a missed exit while in Taxiing state.
            _rolloutHandoffActive = true;

            // Early-vacate retarget. Entered only when the aircraft is BOTH laterally off
            // the runway AND far from the planned exit. Both gates are load-bearing: the
            // lateral gate is what "vacated" physically means, so a trulyStopped handoff on
            // the centreline 2,000 ft short of the exit keeps the planned exit and taxis to
            // it; and the distance gate reuses the same window as IsExitTurnBegun, because
            // ROLLOUT_NEAR_EXIT_FT (500) would classify a legitimate turn begun 800 ft out
            // as an early vacate.
            bool offRunwayAtHandoff = !IsWithinRolloutRunwayLaterally(lat, lon);
            bool farFromPlannedExit = !pastExit
                && distToExitFeet > Navigation.RolloutExitGate.TurnWindowFeet;

            if (offRunwayAtHandoff && farFromPlannedExit && _rolloutExit != null)
            {
                double lateralSignedM = SignedLateralFromRunwayMeters(
                    lat, lon, _rolloutRunway!.StartLat, _rolloutRunway.StartLon,
                    _rolloutRunwayHeadingTrue);

                var vacatedAt = Navigation.RolloutExitGate.MatchEarlyVacateExit(
                    _rolloutAllExits, _rolloutExit,
                    ex => SignedAlongRunwayMeters(
                              lat, lon, ex.Latitude, ex.Longitude,
                              _rolloutRunwayHeadingTrue) * METERS_TO_FEET,
                    lateralSignedM);

                if (vacatedAt != null)
                {
                    RolloutDiag($"Early vacate: left the runway {distToExitFeet:F0} ft short of " +
                        $"'{_rolloutExit.TaxiwayName}' (lateral {lateralSignedM:F0} m) — " +
                        $"retargeting to '{vacatedAt.TaxiwayName}' node={vacatedAt.NodeId}");
                    // Swap the exit so the destination, the post-handoff overshoot monitor
                    // and the arrival callout all name the taxiway the pilot is on.
                    _rolloutExit = vacatedAt;
                }
                else
                {
                    RolloutDiag($"Early vacate: left the runway {distToExitFeet:F0} ft short of " +
                        $"'{_rolloutExit.TaxiwayName}' (lateral {lateralSignedM:F0} m) and no " +
                        $"exit matched — concluding rather than routing back to the planned exit");
                    _landingExitVacatedEarly = true;
                    _rolloutHandoffActive = false;
                    SetState(TaxiGuidanceState.Taxiing);
                    HandleArrival();
                    return;
                }
            }

            // Re-route from the pilot's LIVE position to the best destination node for
            // this exit. Always done (not just for ApronNodeId exits) because the initial
            // touchdown route goes through the taxiway network and is never on the runway
            // pavement the pilot is now crossing. The re-route gives A* a fresh start from
            // wherever the aircraft actually is at handoff time.
            //
            // Destination comes from the shared ResolveExitHandoffDestination —
            // the same priority list TryEarlyExitHandoff uses, followed by the
            // RunwayVacateResolver walk that pushes the stop point past the
            // runway-holding position.
            //
            // A side-effect of always re-routing: the initial route's false "hold short of
            // runway X" tag (inserted by InsertRunwayCrossingHoldShorts because the route's
            // destination sits on the runway) is replaced with a clean 1-2 segment route
            // that has no runway-crossing tags.
            bool handoffRerouted = false;
            if (_rolloutExit != null && _dataProvider != null && _graph != null)
            {
                int rerouteDest = ResolveExitHandoffDestination(out string rerouteDestSrc);
                // LoadRoute's fresh-route reset puts this back to true; the verdict
                // that matters was just computed by the resolver above, so carry it
                // across (see the re-set below).
                bool offPavementAtHandoff = _landingExitOffPavement;
                string exitName = _rolloutExit.TaxiwayName.Length > 0
                    ? $"Taxiway {_rolloutExit.TaxiwayName}"
                    : "exit taxiway";
                string? rerouteErr = LoadRoute(
                    _dataProvider, _icao,
                    lat, lon, headingTrue,
                    rerouteDest,
                    exitName,
                    taxiwaySequence: null,
                    prebuiltGraph: _graph,
                    announceSummary: false);
                handoffRerouted = rerouteErr == null;
                if (handoffRerouted)
                {
                    // LoadRoute clears _isLandingExitRoute; re-set so HandleArrival
                    // fires the landing-exit-specific "Hold position. Open the taxi
                    // planner..." message instead of the generic "Destination reached".
                    _isLandingExitRoute = true;
                    // Same for the off-pavement verdict: this route's handoff already
                    // happened, so LoadRoute's "a new route re-decides this" reset would
                    // silence the still-on-runway warning on exactly the airports that
                    // need it (EVRA, EHAM 36C/W8, EFHK 04L/WZ).
                    _landingExitOffPavement = offPavementAtHandoff;
                }
                RolloutDiag(rerouteErr == null
                    ? $"Handoff re-route OK: lat={lat:F6} lon={lon:F6} → {rerouteDestSrc}={rerouteDest}"
                    : $"Handoff re-route failed ({rerouteErr}), continuing with original route");

                // Reachability guard: never hand the steering tone a target the aircraft is
                // not already essentially on. KSEA 34L 2026-08-21: the first segment lay
                // 53.9 m of cross-track away with the aircraft 17.8 m outside the runway
                // edge, and the tone — silent until that instant — panned 79° right.
                if (handoffRerouted && _route != null && _route.Segments.Count > 0)
                {
                    var firstSeg = _route.Segments[0];
                    double crossToFirstM = TaxiGraph.PerpendicularDistanceMetersStatic(
                        lat, lon,
                        firstSeg.FromNode.Latitude, firstSeg.FromNode.Longitude,
                        firstSeg.ToNode.Latitude, firstSeg.ToNode.Longitude);

                    if (!Navigation.RolloutExitGate.IsHandoffRouteReachable(
                            offRunwayAtHandoff, crossToFirstM, firstSeg.PathWidth))
                    {
                        RolloutDiag($"Handoff route unreachable: {crossToFirstM:F0} m from the " +
                            $"first segment (width {firstSeg.PathWidth:F0} ft) with the aircraft " +
                            $"off the runway — concluding rather than steering across it");
                        _landingExitVacatedEarly = true;
                        _rolloutHandoffActive = false;
                        SetState(TaxiGuidanceState.Taxiing);
                        HandleArrival();
                        return;
                    }
                }
            }

            // If the re-route did NOT succeed (LoadRoute failed, or _rolloutExit
            // / data provider / graph was null), the route is still the one
            // built at touchdown and _currentSegmentIndex is still 0 — the
            // Taxiing branch never ran during the rollout. Segment 0 sits back
            // in the touchdown zone, thousands of feet behind the aircraft;
            // AdvanceToNearestSegment's 6-segment look-ahead cannot recover
            // from index 0, so the tone would target a waypoint behind the
            // aircraft and hard-pan on the ±180° bearing wrap. Re-anchor the
            // segment cursor to the aircraft's true position, once, here.
            if (!handoffRerouted && _route != null)
            {
                _currentSegmentIndex = FindNearestSegmentIndexFullRoute(lat, lon);
                RolloutDiag($"Handoff re-anchored _currentSegmentIndex={_currentSegmentIndex} " +
                    $"of {_route.Segments.Count}");
            }

            // NoGraph path: route was never built (LoadRoute failed at touchdown
            // due to disconnected graph component) and the handoff re-route also
            // failed. Resuming the tone here would leave it frozen at its last
            // heading-error update — no further UpdateHeadingError calls happen
            // in Taxiing state when _route is null. Stop cleanly instead so the
            // pilot isn't misled by a panning tone with no route behind it.
            if (!handoffRerouted && _route == null)
            {
                RolloutDiag("Handoff re-route failed with no fallback route — stopping guidance");
                string exitDesc = _rolloutExit != null && _rolloutExit.TaxiwayName.Length > 0
                    ? $"taxiway {_rolloutExit.TaxiwayName}"
                    : "the exit";
                AnnounceInstruction($"Exit reached. Route unavailable. Taxi to {exitDesc} and use the taxi planner.");
                StopGuidance();
                return;
            }

            SetState(TaxiGuidanceState.Taxiing);
            _steeringTone.Resume();
            return;
        }

        // Overshoot detection. If the aircraft has rolled past the chosen
        // exit along the runway centerline WITHOUT starting the turn, pick
        // the next downfield exit and retarget. If no exits remain on the
        // runway, end the rollout gracefully so the off-route recalc path
        // can't route back across the runway to the now-passed exit (which
        // was the original bug this work fixes).
        //
        // stillOnRunway and !alignedWithExit together protect against misfiring
        // on a completed shallow exit. stillOnRunway handles exits ≥ ~8° (lateral
        // buildup is fast enough). alignedWithExit handles exits 3–8° (lateral
        // buildup is too slow but heading alignment with ExitBearingTrue is clear).
        bool stillOnRunway = !exitedLaterally;

        // Exit-type-aware margin — same angle-proportional formula as the
        // post-handoff monitor. See that block for the rationale.
        double overshootMargin;
        if (_rolloutExit.ExitType == "High-speed" && _rolloutExit.ExitAngleDegrees > 0.0)
        {
            double radOM = _rolloutExit.ExitAngleDegrees * Math.PI / 180.0;
            double angleBasedFtOM = (OVERSHOOT_ON_CENTERLINE_FT + 5.0) / Math.Sin(radOM);
            overshootMargin = Math.Max(ROLLOUT_OVERSHOOT_FT,
                              Math.Min(angleBasedFtOM, ROLLOUT_HIGHSPEED_OVERSHOOT_FT));
        }
        else
        {
            overshootMargin = _rolloutExit.ExitType == "High-speed"
                ? ROLLOUT_HIGHSPEED_OVERSHOOT_FT : ROLLOUT_OVERSHOOT_FT;
        }
        if (signedAlongPastFt >= overshootMargin
            && hdgDeltaAbs < ROLLOUT_TURN_BEGAN_HDG_DEG
            && stillOnRunway
            && !alignedWithExit)
        {
            RolloutDiag($"OVERSHOOT detected: signedAlongPast={signedAlongPastFt:F0}ft hdgDelta={hdgDeltaAbs:F1}deg " +
                $"lateral={lateralFromCenterlineFt:F0}ft halfWidth={halfRunwayWidthFt:F0}ft exitBrgErr={exitBrgErr:F1}deg");

            // Sorted list — first suitable exit further downfield than the current
            // one (with ROLLOUT_OVERSHOOT_FT sentinel to skip the current one and
            // any near-duplicates). Exits requiring a turn > 90° are skipped —
            // same suitability rule as undershoot protection.
            Navigation.LandingExit? nextExit = null;
            foreach (var e in _rolloutAllExits)
            {
                if (e.DistanceFromThresholdFeet <= _rolloutExit.DistanceFromThresholdFeet + ROLLOUT_OVERSHOOT_FT)
                    continue;
                if (e.ExitAngleDegrees > 0.0 && e.ExitAngleDegrees > 90.0)
                    continue;
                nextExit = e;
                break;
            }

            if (nextExit != null)
            {
                RolloutDiag($"OVERSHOOT retarget to next exit: name='{nextExit.TaxiwayName}' distFromThr={nextExit.DistanceFromThresholdFeet:F0}ft");
                RetargetLandingExit(nextExit, lat, lon, headingTrue);
                return;
            }

            RolloutDiag("OVERSHOOT no downfield exit -> EnterRunwayEndCountdown");

            // No downfield exit. Announce, clear the route so the off-route
            // recalc has nothing to chase, fall through to idle Taxiing.
            string rwyLabel = _rolloutRunway != null && !string.IsNullOrEmpty(_rolloutRunway.RunwayID)
                ? _rolloutRunway.RunwayID
                : "this runway";
            AnnounceInstruction($"Missed last exit on runway {rwyLabel}.");
            EnterRunwayEndCountdown();
            return;
        }

        // Undershoot protection. Checked on every frame when below the exit-type
        // speed threshold — not one-shot. Previous one-shot latch fired when GS
        // first crossed 50 kt, at which point the earlier exit was often still
        // outside the 1000 ft window; by the time the aircraft reached it the latch
        // had already fired and the pilot had to roll all the way to the original exit.
        //
        // High-speed exits: threshold is 50 kt — below that speed the exit can no
        // longer be used at proper approach speed, so retarget to an earlier normal exit.
        // Normal/End exits: threshold is 20 kt — the aircraft has decelerated much
        // earlier than planned; take the nearest earlier exit instead.
        //
        // ROLLOUT_UNDERSHOOT_COOLDOWN_SEC between retargets prevents rapid cascade
        // when multiple earlier exits are within ROLLOUT_UNDERSHOOT_RANGE_FT.
        if (!_rolloutNoExitMode && !pastExit)
        {
            bool cooldownOk = (DateTime.UtcNow - _lastUndershootRetargetTime).TotalSeconds >= ROLLOUT_UNDERSHOOT_COOLDOWN_SEC;

            if (groundSpeedKts < ROLLOUT_UNDERSHOOT_ENTRY_GS_KTS && cooldownOk)
            {
                Navigation.LandingExit? earlierExit = null;
                double earlierExitDistFt = double.MaxValue;

                // Minimum lead distance: the earlier exit must be far enough
                // ahead that the aircraft can still react, decelerate and set up
                // the turn at the current speed. Without it the scan grabs
                // whatever exit is physically nearest — observed at YSSY 16R
                // retargeting to taxiway 'L' just 79 ft ahead at 52 kt,
                // impossible to make, which then cascaded to a missed exit and a
                // false "no exit remaining". Scales with speed.
                double undershootMinLeadFt = Math.Max(
                    ROLLOUT_UNDERSHOOT_MIN_LEAD_FT,
                    groundSpeedKts * ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT);

                foreach (var e in _rolloutAllExits)
                {
                    // Only consider exits clearly before the planned exit.
                    if (e.DistanceFromThresholdFeet >= _rolloutExit.DistanceFromThresholdFeet - ROLLOUT_OVERSHOOT_FT)
                        break;

                    // Skip exits requiring a turn greater than 90° — physically unsuitable.
                    if (e.ExitAngleDegrees > 0.0 && e.ExitAngleDegrees > 90.0)
                        continue;

                    // Steep exits need more braking margin — only include them when
                    // the aircraft is slow enough to make the tighter turn safely.
                    if (e.ExitAngleDegrees >= ROLLOUT_UNDERSHOOT_STEEP_ANGLE_DEG
                        && groundSpeedKts >= ROLLOUT_UNDERSHOOT_STEEP_GS_KTS)
                        continue;

                    // Skip exits the aircraft has already rolled past.
                    double signedAlongM = SignedAlongRunwayMeters(
                        lat, lon, e.Latitude, e.Longitude, _rolloutRunwayHeadingTrue);
                    if (signedAlongM >= 0.0)
                        continue;

                    double distFt2 = TaxiGraph.FastDistanceMeters(lat, lon, e.Latitude, e.Longitude) * METERS_TO_FEET;
                    if (distFt2 >= undershootMinLeadFt
                        && distFt2 <= ROLLOUT_UNDERSHOOT_RANGE_FT
                        && distFt2 < earlierExitDistFt)
                    {
                        earlierExit = e;
                        earlierExitDistFt = distFt2;
                    }
                }

                if (earlierExit != null)
                {
                    _lastUndershootRetargetTime = DateTime.UtcNow;
                    string newName = string.IsNullOrEmpty(earlierExit.TaxiwayName)
                        ? "earlier exit"
                        : $"taxiway {earlierExit.TaxiwayName}";
                    string msg = $"Taking earlier exit, {newName}, {DistanceFormatter.FromFeet(earlierExitDistFt)} ahead.";
                    RolloutDiag($"UNDERSHOOT: retargeting to '{earlierExit.TaxiwayName}' at {earlierExitDistFt:F0}ft " +
                        $"(planned was '{_rolloutExit.TaxiwayName}')");
                    RetargetLandingExit(earlierExit, lat, lon, headingTrue, overrideAnnouncement: msg);
                    return;
                }
            }
        }

        // Approach callouts. Each fires once per rollout (flags are reset
        // in BeginLandingRollout). Use ">= threshold" with a generous
        // window so a fast aircraft skipping past 1500 ft between frames
        // doesn't lose the announcement.
        // Skip the per-frame label/table builds once all have fired (they allocate at
        // 30 Hz during the most latency-sensitive audio phase). The 900 ft cue only
        // exists for high-speed exits, so it counts as "done" for the other types.
        // NOT an early return — the turn-now callout and exit handoff below must
        // keep running every frame.
        bool approachCalloutsDone = _rolloutApproach1500Announced && _rolloutApproach500Announced
            && (_rolloutApproach900Announced || _rolloutExit.ExitType != "High-speed");
        if (!approachCalloutsDone)
        {
            string exitClass = _rolloutExit.ExitType switch
            {
                "High-speed" => "high-speed exit",
                "End"        => "runway-end exit",
                _            => "exit"
            };
            string name2 = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
                ? "exit"
                : $"taxiway {_rolloutExit.TaxiwayName}";

            var xm = DistanceMilestones.ExitApproach(); // far->near: [0]=1500ft/500m, [1]=900ft/300m, [2]=500ft/150m
            if (!_rolloutApproach1500Announced && distToExitFeet <= xm[0].TriggerMetres / DistanceFormatter.MetresPerFoot && distToExitFeet > xm[1].TriggerMetres / DistanceFormatter.MetresPerFoot)
            {
                RolloutDiag($"1500-ft approach callout firing: distToExit={distToExitFeet:F0}ft");
                AnnounceInstruction($"Approaching {exitClass} {name2}, {xm[0].Label}.");
                _rolloutApproach1500Announced = true;
            }

            // High-speed exits only: extra callout at 900 ft, analogous to the first RETIL
            // flash (~984 ft). Gives blind pilots a second awareness cue before the 500 ft
            // "prepare to turn" window — sighted pilots would see the first RETIL light here.
            if (!_rolloutApproach900Announced && _rolloutExit.ExitType == "High-speed"
                && distToExitFeet <= xm[1].TriggerMetres / DistanceFormatter.MetresPerFoot && distToExitFeet > xm[2].TriggerMetres / DistanceFormatter.MetresPerFoot)
            {
                RolloutDiag($"900-ft high-speed callout firing: distToExit={distToExitFeet:F0}ft");
                AnnounceInstruction($"{CapFirst(name2)}, {xm[1].Label}.");
                _rolloutApproach900Announced = true;
            }

            if (!_rolloutApproach500Announced && distToExitFeet <= xm[2].TriggerMetres / DistanceFormatter.MetresPerFoot && distToExitFeet > 150.0)   // feet — turn-now handoff boundary (not a milestone)
            {
                RolloutDiag($"500-ft approach callout firing: distToExit={distToExitFeet:F0}ft gs={groundSpeedKts:F1}");
                // Suppress "Slow down" for high-speed exits — 40–80 kt is the correct
                // approach speed for those exits; telling the pilot to slow down contradicts
                // the reason they picked one.
                bool isHighSpeed = _rolloutExit.ExitType == "High-speed";
                string slowSuffix = !isHighSpeed && groundSpeedKts > ROLLOUT_TAXI_GS_KTS ? " Slow down." : "";
                AnnounceInstruction($"{CapFirst(name2)}, {xm[2].Label}.{slowSuffix}");
                _rolloutApproach500Announced = true;
            }
        }

        if (!_rolloutTurnNowAnnounced && distToExitFeet <= 150.0)
        {
            RolloutDiag($"Turn-now callout firing: distToExit={distToExitFeet:F0}ft");
            // Direction = bearing-from-aircraft-to-exit minus aircraft heading.
            // Sign tells us turn left vs right; magnitude is unused here.
            double bearingToExit = NavigationCalculator.CalculateBearing(
                lat, lon, _rolloutExit.Latitude, _rolloutExit.Longitude);
            double turnDelta = NormalizeAngle(bearingToExit - headingTrue);
            string dir = turnDelta < 0 ? "left" : "right";
            // < 20°: chord taxiways and shallow curved-RET entries need a small
            // initial input, not a committed turn — "gentle" prevents over-rotation.
            // ≥ 20°: genuine RETs and normal exits warrant a deliberate turn input.
            string turnWord = _rolloutExit.ExitAngleDegrees < 20.0 ? "Gentle" : "Turn";
            string exitName = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
                ? "exit"
                : $"taxiway {_rolloutExit.TaxiwayName}";
            AnnounceInstruction($"{turnWord} {dir} now, {exitName}.");
            _rolloutTurnNowAnnounced = true;
            // Normal exits (50–110°): reset the heading-error smoother immediately
            // so the ExitBearingTrue-based tone below starts with a sharp hard-pan
            // rather than ramping up from the near-zero "bearing-to-junction ≈ runway
            // heading" residual built up during the approach.
            if (_rolloutExit.ExitType == "Normal" && _rolloutExit.ExitBearingTrue > 0.0)
                _headingErrorInitialized = false;
        }

        // Early handoff to live taxi look-ahead guidance.
        // One-shot: attempted at the first frame where GS ≤ 50 kt AND within
        // ROLLOUT_EXIT_TONE_ARM_FT (300 ft) of the exit. Firing at 300 ft lets the
        // Taxiing state's speed-scaled "turn now" announcement reach its full lead
        // distance (e.g. ~267 ft at 40 kt, scaling up for sharp turns). Firing at
        // 150 ft was too late — the speed-scaled window had already closed by the
        // time live guidance kicked in.
        // If LoadRoute succeeds and the first segment isn't backwards, we move
        // directly to Taxiing. If it fails (bad node snap, A* error, or first
        // segment >120° off runway heading), the bearing-to-junction fallback tone
        // below takes over unchanged, and the rollout's own 150 ft "turn now"
        // callout fires as the verbal backstop.
        //
        // Only applies to HIGH-SPEED exits (angle < HIGH_SPEED_MAX_DEG). For Normal
        // and End exits (≥ 50°) the extension node is too far off the runway heading
        // to give useful tone steering from 300 ft before the junction — e.g., a 90°
        // exit immediately pans the tone to maximum regardless of what the pilot does,
        // and the 150 ft "turn now" callout (from UpdateLandingRollout) is silently
        // skipped because state has already moved to Taxiing. Let those exits fire
        // through the rollout's own callouts and transition via turnBegun instead.
        if (!_rolloutEarlyHandoffDone
            && !pastExit
            && groundSpeedKts <= ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS
            && distToExitFeet <= ROLLOUT_EXIT_TONE_ARM_FT
            && (_rolloutExit == null || _rolloutExit.ExitType == "High-speed"))
        {
            _rolloutEarlyHandoffDone = true;
            if (TryEarlyExitHandoff(lat, lon, headingTrue))
            {
                _steeringTone.Resume();
                return;
            }
            // Fall through — bearing-to-junction tone below acts as the fallback.
        }

        // Rollout steering tone — three modes, selected by RolloutExitGate.SelectToneMode:
        //
        // Above ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS (50 kt) the tone is Silent — at runway
        // speed a pan cue is useless and autopilot crab / crosswind alignment would cause
        // confusing pan.
        //
        // Below 50 kt and beyond ROLLOUT_EXIT_TONE_ARM_FT (300 ft) of the chosen exit, the
        // tone is DriftCorrection: desired heading is the runway itself, steering the pilot
        // back to the centreline through the long deceleration that used to be silent.
        // KSEA 34L 2026-08-21: a 15.1° drift built up here with no cue at all, and the
        // steering tone's first utterance was a 79° hard pan once ExitBearing took over.
        //
        // Within 300 ft (≤50 kt) the tone is ExitBearing: desired heading = bearing to the
        // exit junction node.
        //   When the junction is on the centreline this is ≈ runway heading → tone stays
        //   silent; when the junction is off-axis (RET, angled exit) the bearing deviates
        //   naturally as the aircraft approaches → appropriate directional pan.
        //   The verbal "turn now" at 150 ft and the Taxiing handoff with ApronNodeId
        //   re-route together handle the actual exit turn.
        //   NOTE: ExitBearingTrue is NOT used here. For shallow exits, both the HS-style
        //   override (TaxiGraph.cs:1618, can store an apron-bearing up to NORMAL_MAX_DEG
        //   off the runway) and the implicit-exit override (TaxiGraph.cs:1589, gated to
        //   only-widen via the apronAngle > currentAngleFwd guard) can produce bearings
        //   meaningfully off the approach path. Using ExitBearingTrue here caused
        //   premature hard panning 300 ft before the junction on shallow high-speed
        //   exits like EIDW S5 (apron ~90° off runway). Bearing-to-junction stays silent
        //   while the aircraft is on centreline and only deviates as the aircraft nears
        //   an off-axis junction — appropriate directional pan without false alarms.
        var toneMode = Navigation.RolloutExitGate.SelectToneMode(groundSpeedKts, distToExitFeet);
        if (toneMode != _rolloutToneMode)
        {
            // Start every mode from a clean filter so the pan is sharp and immediate rather
            // than ramping out of the previous mode's residual.
            _headingErrorInitialized = false;
            _rolloutToneMode = toneMode;
        }

        if (toneMode == Navigation.RolloutToneMode.Silent)
        {
            _steeringTone.Pause();
            _headingErrorInitialized = false;
        }
        else
        {
            double desiredHeading;
            double toneSilentDeg;
            double toneActivationDeg;
            double toneMaxPanDeg;

            if (toneMode == Navigation.RolloutToneMode.ExitBearing)
            {
                // Exception: once the "turn now" callout has fired for a Normal exit
                // (50–110°), switch to ExitBearingTrue as the desired heading.
                // Bearing-to-junction fights the turn at this range — the junction is
                // still ahead, so as the pilot turns off the runway the heading error
                // flips toward the wrong side. ExitBearingTrue correctly decreases as
                // the pilot aligns with the exit, telling them how much more to turn.
                if (_rolloutTurnNowAnnounced && _rolloutExit!.ExitType == "Normal"
                    && _rolloutExit.ExitBearingTrue > 0.0)
                {
                    desiredHeading = _rolloutExit.ExitBearingTrue;
                }
                else
                {
                    const double MPD = 111132.0;
                    double midLatRad = (lat + _rolloutExit!.Latitude) * 0.5 * Math.PI / 180.0;
                    double bN = (_rolloutExit.Latitude - lat) * MPD;
                    double bE = (_rolloutExit.Longitude - lon) * MPD * Math.Cos(midLatRad);
                    desiredHeading = (Math.Atan2(bE, bN) * 180.0 / Math.PI + 360.0) % 360.0;
                }

                toneSilentDeg = ROLLOUT_EXIT_TONE_SILENT_DEG;
                toneActivationDeg = ROLLOUT_EXIT_TONE_ACTIVATION_DEG;
                toneMaxPanDeg = ROLLOUT_EXIT_TONE_MAX_PAN_DEG;
            }
            else
            {
                // DriftCorrection — the phase that used to be silent. Desired heading is
                // the runway itself, so the tone reads "steer back to the centreline".
                // KSEA 34L 2026-08-21: the pilot drifted to 15.1° with no cue at all, and
                // the tone's first utterance was a 79° hard pan after the handoff.
                desiredHeading = _rolloutRunwayHeadingTrue;
                toneSilentDeg = ROLLOUT_DRIFT_TONE_SILENT_DEG;
                toneActivationDeg = ROLLOUT_DRIFT_TONE_ACTIVATION_DEG;
                toneMaxPanDeg = ROLLOUT_DRIFT_TONE_MAX_PAN_DEG;
            }

            double rawError = NormalizeAngle(desiredHeading - headingTrue);
            _smoothedHeadingError = _headingErrorInitialized
                ? _smoothedHeadingError * (1 - HEADING_ERROR_FILTER_ALPHA) + rawError * HEADING_ERROR_FILTER_ALPHA
                : rawError;
            _headingErrorInitialized = true;

            if (!_steeringToneSuppressed)
            {
                _steeringTone.Resume();
                _steeringTone.UpdateHeadingErrorWithThresholds(
                    _smoothedHeadingError, toneSilentDeg, toneActivationDeg, toneMaxPanDeg);
            }
        }
    }

    /// <summary>
    /// Attempts to hand off from the static bearing-to-junction rollout tone to
    /// live taxi look-ahead guidance. Called once when GS drops to or below
    /// ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS and the aircraft is within
    /// ROLLOUT_EXIT_TONE_ARM_FT of the chosen exit.
    ///
    /// Uses ApronNodeId (first graph node outside the runway corridor) as the
    /// route destination when available, otherwise falls back to NodeId (the
    /// junction itself). This ensures A* follows the actual exit curve rather
    /// than stopping at the runway edge.
    ///
    /// Returns true and transitions to Taxiing on success; returns false and
    /// leaves state in LandingRollout on any failure so the caller's
    /// bearing-to-junction fallback tone can take over.
    /// </summary>
    private bool TryEarlyExitHandoff(double lat, double lon, double headingTrue)
    {
        if (_rolloutExit == null || _dataProvider == null || _graph == null)
            return false;

        // Pick the best destination node for A* to route toward:
        //
        // (a) ApronNodeId — set for shallow exits (< 5°): first node outside the runway
        //     corridor, computed by ExitPathLeavesCorridor BFS in GetLandingExits.
        //     Gives guidance through the full exit curve for nearly-straight exits.
        //
        // (b) Furthest same-named non-End node in _rolloutAllExits — for multi-segment
        //     angled exits (e.g. LEMD L5 modelled as 3 nodes at 6340/6528/6672 ft).
        //     Routing to the full arc end lets A* traverse every segment of the curve.
        //
        // (c) Extension node adjacent to the junction in the exit direction — for single-
        //     junction exits at any angle (15°, 45°, 90° etc.). Gives the route a second
        //     segment so the look-ahead walk (GuidanceGeometry.WalkTarget) continues past
        //     the junction and pans the tone toward the exit direction before the
        //     aircraft arrives.
        //
        // (d) NodeId — last resort if graph has no adjacent exit node (dead-end junction).
        // (a)-(d) above, plus the RunwayVacateResolver walk that pushes the stop point
        // past the runway-holding position — see ResolveExitHandoffDestination.
        int destNodeId = ResolveExitHandoffDestination(out string destSource);
        // Carried across LoadRoute's fresh-route reset — see the re-set below.
        bool offPavementAtHandoff = _landingExitOffPavement;

        string exitName = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
            ? "exit taxiway"
            : $"Taxiway {_rolloutExit.TaxiwayName}";

        // Anchor the route START on the chosen exit taxiway. Without this the start snaps to
        // the nearest node overall, which — because the early handoff fires while the aircraft
        // is still on the runway short of the exit — can be a NEIGHBOURING exit (EIDW 28L abeam
        // S5 while committed to S6), sending A* up that exit and across the parallel taxiway: a
        // ~600 m hairpin. Empty name (unnamed exit) → null → legacy nearest-node snap.
        string? startTwy = _rolloutExit.TaxiwayName.Length > 0 ? _rolloutExit.TaxiwayName : null;
        string? err = LoadRoute(
            _dataProvider, _icao,
            lat, lon, headingTrue,
            destNodeId, exitName,
            taxiwaySequence: null,
            prebuiltGraph: _graph,
            announceSummary: false,
            startTaxiwayName: startTwy);

        if (err != null)
        {
            RolloutDiag($"TryEarlyExitHandoff: LoadRoute failed — {err}");
            return false;
        }

        if (_route == null || _route.Segments.Count == 0)
        {
            RolloutDiag("TryEarlyExitHandoff: route empty after LoadRoute");
            // LoadRoute reported success but produced no usable route; it may already
            // have set state to RouteLoaded. Restore LandingRollout so the rollout
            // frame loop keeps running (see the sanity-reject path below).
            SetState(TaxiGuidanceState.LandingRollout);
            return false;
        }

        // Sanity check: reject if the first segment is inconsistent with the
        // chosen exit. Two acceptance conditions (either is sufficient):
        //
        // (a) First segment is within ExitAngleDegrees + 10° of runway heading.
        //     This is the normal case: A* started at a runway-centreline node and
        //     the first segment runs along (or just into) the exit curve. The +10°
        //     margin absorbs navdata rounding and slight curve overshoot.
        //     Tighter than the old 120° "not backwards" check — prevents a diagonal
        //     shortcut segment from panning the pilot toward the exit too early
        //     while they are still upfield (the diagonal would be more off-axis than
        //     the exit itself, which is wrong). ExitAngleDegrees is accurate after
        //     the off-axis edge-picker fix; defaults to 90° when no named edge was
        //     found, giving a permissive 100° threshold in the degenerate case.
        //
        // (b) First segment is within 60° of ExitBearingTrue — handles the case
        //     where A* snapped directly to the exit junction or ApronNodeId and the
        //     first segment immediately heads onto the exit taxiway. End exits
        //     (backtrack) land here: the first segment heads ~150° from runway
        //     heading (correct) and aligns with ExitBearingTrue. ExitBearingTrue = 0
        //     means the bearing was not computed; condition (b) is skipped entirely.
        double firstBearing = _route.Segments[0].BearingDegrees;
        double bearingDeltaFromRunway = Math.Abs(NormalizeAngle(firstBearing - _rolloutRunwayHeadingTrue));
        double firstSegThreshold = _rolloutExit.ExitAngleDegrees + 10.0;
        bool alignedWithRunway = bearingDeltaFromRunway <= firstSegThreshold;
        bool alignedWithExit = _rolloutExit.ExitBearingTrue > 0.0
            && Math.Abs(NormalizeAngle(firstBearing - _rolloutExit.ExitBearingTrue)) <= 60.0;
        if (!alignedWithRunway && !alignedWithExit)
        {
            RolloutDiag($"TryEarlyExitHandoff: first segment {firstBearing:F1}° is " +
                $"{bearingDeltaFromRunway:F1}° from runway (threshold {firstSegThreshold:F0}°) " +
                $"and doesn't align with exit bearing {_rolloutExit.ExitBearingTrue:F1}° — rejecting");
            // Intentionally discard the touchdown route too — it routed through the
            // taxiway network and would feed the off-route detector once state moves
            // to Taxiing. Next RetargetLandingExit / handoff will rebuild from
            // current position.
            _route = null;
            _destinationNodeId = 0;
            // LoadRoute above set state to RouteLoaded. Restore LandingRollout so the
            // next UpdatePosition frame re-runs UpdateLandingRollout (bearing-to-junction
            // fallback tone + the normal turnBegun / exitedLaterally / overshoot handoff).
            // Without this the state machine is stranded in RouteLoaded — no tone, and
            // "Where am I" reports no active route — until the pilot manually intervenes.
            // Mirrors RetargetLandingExit's post-LoadRoute SetState(LandingRollout).
            SetState(TaxiGuidanceState.LandingRollout);
            return false;
        }

        // destSource comes from the shared ResolveExitHandoffDestination, which
        // knows about the vacate walk and the same-named-RET branch that main's
        // local apron/junction comparison could not express; startTwy is main's
        // start-anchor field, kept as-is.
        RolloutDiag($"TryEarlyExitHandoff OK: destNodeId={destNodeId} ({destSource}) " +
            $"startTwy={startTwy ?? "(nearest)"} firstSeg={firstBearing:F1}° segs={_route.Segments.Count}");

        // Reset the heading-error smoother so the taxi tone starts clean rather
        // than inheriting the rollout's near-zero centreline residual.
        _smoothedHeadingError = 0.0;
        _headingErrorInitialized = false;

        // Set ExitBearingTrue as a minimum pan floor for the post-exit arc. Prevents
        // the initial flat section of a shallow RET (e.g. EIDW S5: 92 m at ~runway
        // heading) from producing a silent tone when a gentle rightward cue is needed.
        // The floor is a no-op once the A* arc's natural bearing exceeds it.
        _postHighSpeedExitMinBearing = (_rolloutExit!.ExitBearingTrue > 0.0)
            ? _rolloutExit.ExitBearingTrue : 0.0;

        // Arm post-handoff overshoot monitor (same as the turnBegun path above).
        _rolloutHandoffActive = true;

        // LoadRoute above cleared _isLandingExitRoute; re-set so HandleArrival
        // fires the landing-exit-specific "Hold position. Open the taxi planner..."
        // message instead of the generic "Destination reached".
        _isLandingExitRoute = true;
        // Likewise the off-pavement verdict the resolver computed for THIS handoff —
        // LoadRoute's reset would otherwise claim the aircraft ends up clear of the
        // runway at airports where the graph has no taxiway past the junction.
        _landingExitOffPavement = offPavementAtHandoff;

        SetState(TaxiGuidanceState.Taxiing);
        return true;
    }

    /// <summary>
    /// Finds the first graph node adjacent to <paramref name="junctionNodeId"/> in
    /// approximately the exit direction. Used to extend landing-exit routes by one
    /// segment past the junction so the look-ahead walk (GuidanceGeometry.WalkTarget)
    /// can continue around the corner and start panning the tone before the junction.
    /// Returns -1 if no suitable node is found.
    /// </summary>
    /// <summary>
    /// Picks the destination node for a LandingRollout → Taxiing re-route. Shared by
    /// BOTH handoff paths (<see cref="TryEarlyExitHandoff"/> for high-speed exits and
    /// the turnBegun / exitedLaterally / trulyStopped handoff in
    /// <c>UpdateLandingRollout</c>) so the two can never drift apart — they picked the
    /// destination with two hand-copied blocks before, and only one of them carried
    /// the multi-segment-RET branch.
    ///
    /// Priority:
    ///   (a) ApronNodeId — corridor-exit node computed by GetLandingExits' BFS.
    ///   (b) Furthest same-named non-End exit — multi-segment RETs (LEMD L5).
    ///   (c) FindExitExtensionNode — first adjacent node in the exit direction.
    ///   (d) NodeId — the junction itself (dead-end).
    ///
    /// Then — unconditionally, whichever branch won —
    /// <see cref="RunwayVacateResolver.ExtendClearOfRunway"/> walks the destination
    /// on down the exit taxiway until the aircraft would be past the runway-holding
    /// position. None of (a)-(d) guarantees that on its own: (a)'s corridor tolerance
    /// only means "off the pavement" (half-width + 15 m) and it is not computed for
    /// every exit-classification branch, while (c) returns whatever node happens to be
    /// adjacent. At EVRA 18 → B that adjacent node was 33 m from the centreline
    /// (half-width 22.6 m, hold line at 106 m), so guidance said "hold position" with
    /// the aircraft still occupying the runway strip and tower unable to clear a
    /// departure — the defect this stage exists to prevent.
    /// </summary>
    private int ResolveExitHandoffDestination(out string source)
    {
        source = "none";
        if (_rolloutExit == null) return 0;

        int destNodeId = Navigation.LandingExitDestination.Resolve(
            _graph, _rolloutExit, _rolloutAllExits,
            _rolloutRunway, _rolloutRunwayHeadingTrue,
            out double startLateralM, out double endLateralM, out source);

        // Remember whether the aircraft actually ends up off the concrete, so the
        // arrival callout can tell the pilot the truth. At a handful of airports the
        // navdata simply has no taxiway mapped past the junction (EVRA's unnamed
        // exits, EHAM 36C/W8, EFHK 04L/WZ — one graph edge, pointing back at the
        // runway), and no routing can invent one. Saying "stop and hold position"
        // there parks a blind pilot on an active runway.
        _landingExitOffPavement = RunwayVacateResolver.IsOffPavement(endLateralM, _rolloutRunway);

        if (source.EndsWith("+vacate", StringComparison.Ordinal))
        {
            RolloutDiag($"Vacate-extend: {source} ({startLateralM:F0} m from " +
                $"runway axis) → {destNodeId} ({endLateralM:F0} m)");
        }
        else if (endLateralM < RunwayVacateResolver.VacatedClearanceMetres)
        {
            // Walked as far as the graph allows and still short of the holding
            // position — better than before but worth a line when a report says
            // "it stopped me too early".
            RolloutDiag($"Vacate-extend: {source}={destNodeId} stays at " +
                $"{endLateralM:F0} m from the runway axis (target " +
                $"{RunwayVacateResolver.VacatedClearanceMetres:F0} m) — no further node");
        }

        if (!_landingExitOffPavement)
        {
            RolloutDiag($"Vacate-extend: STOP POINT STILL ON THE RUNWAY — {endLateralM:F0} m " +
                $"from the axis at exit '{_rolloutExit.TaxiwayName}'. Arrival callout will " +
                $"tell the pilot to continue ahead rather than hold position.");
        }

        return destNodeId;
    }

    /// <summary>
    /// Per-frame logic while in runway-end countdown mode (set by
    /// <see cref="EnterRunwayEndCountdown"/>). Drives three voice
    /// callouts as the aircraft approaches the physical end of the
    /// runway, then transitions to Taxiing once the pilot has stopped
    /// or begun turning.
    ///
    /// Distance to end is computed by projecting the aircraft position
    /// onto the runway centerline relative to <c>_rolloutRunway.StartLat/Lon</c>,
    /// then subtracting from the runway length. Sign convention: along-axis
    /// distance from start is positive in the runway heading direction;
    /// distToEnd = length - alongFromStart.
    ///
    /// Transitions out to Taxiing when EITHER ground speed drops below
    /// ROLLOUT_NO_EXIT_STOPPED_GS_KTS (3 kt — effectively stopped) OR
    /// heading deviation reaches ROLLOUT_TURN_BEGAN_HDG_DEG (15° — pilot
    /// is maneuvering, typically a backtaxi turn). On exit, _route stays
    /// null so the Taxiing branch's off-route recalc has nothing to chase.
    /// </summary>
    private void UpdateRunwayEndCountdown(double lat, double lon, double headingTrue, double groundSpeedKts)
    {
        if (_rolloutRunway == null)
        {
            // Defensive — without runway data we can't compute the end-distance.
            // Fall through to plain Taxiing; pilot can use Where Am I and other tools.
            _rolloutNoExitMode = false;
            SetState(TaxiGuidanceState.Taxiing);
            return;
        }

        // Distance from runway start to current position, along the runway heading.
        double alongFromStartM = SignedAlongRunwayMeters(
            lat, lon,
            _rolloutRunway.StartLat, _rolloutRunway.StartLon,
            _rolloutRunwayHeadingTrue);
        double lengthM = _rolloutRunway.Length * 0.3048; // Length is feet
        double distToEndM = lengthM - alongFromStartM;
        double distToEndFt = distToEndM * METERS_TO_FEET;

        // Heading deviation from runway centerline.
        double hdgDelta = NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue);
        double hdgDeltaAbs = Math.Abs(hdgDelta);

        // Transition out: effectively stopped, OR pilot is turning (likely
        // beginning a backtaxi). Hand off to BacktrackingOnRunway so the
        // steering tone guides the pilot back along the runway to the apron.
        bool effectivelyStopped = groundSpeedKts < ROLLOUT_NO_EXIT_STOPPED_GS_KTS;
        bool turnBegun = hdgDeltaAbs >= ROLLOUT_TURN_BEGAN_HDG_DEG
                         && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS;
        if (effectivelyStopped || turnBegun)
        {
            EnterBacktracking(lat, lon);
            return;
        }

        // Past the runway end already (overrun / off the pavement). The
        // three countdown callouts have either fired or been skipped past;
        // stay quiet here and rely on the stopped/turn transitions above
        // to retire the countdown when the pilot stops or maneuvers.
        if (distToEndFt <= 0) return;

        // All three fired — skip the per-frame table build (it allocates). Nothing
        // follows in this method, so the early return is safe.
        if (_rolloutEnd1500Announced && _rolloutEnd500Announced && _rolloutEnd100Announced)
            return;

        var rm = DistanceMilestones.RunwayEnd(); // far->near: [0]=1500ft/500m, [1]=500ft/150m, [2]=100ft/30m
        if (!_rolloutEnd1500Announced && distToEndFt <= rm[0].TriggerMetres / DistanceFormatter.MetresPerFoot && distToEndFt > rm[1].TriggerMetres / DistanceFormatter.MetresPerFoot)
        {
            AnnounceInstruction($"Runway end in {rm[0].Label}.");
            _rolloutEnd1500Announced = true;
        }

        if (!_rolloutEnd500Announced && distToEndFt <= rm[1].TriggerMetres / DistanceFormatter.MetresPerFoot && distToEndFt > rm[2].TriggerMetres / DistanceFormatter.MetresPerFoot)
        {
            // "Slow down" is added only when the pilot still has real speed
            // to bleed off. ROLLOUT_TAXI_GS_KTS (30) is the threshold below
            // which the aircraft is at normal taxi speed and the suffix is
            // patronising noise. The near "Stop" callout below is
            // unconditional by contrast — at that distance from the end the pilot
            // needs the directive regardless of current speed.
            string slowSuffix = groundSpeedKts > ROLLOUT_TAXI_GS_KTS ? " Slow down." : "";
            AnnounceInstruction($"Runway end in {rm[1].Label}.{slowSuffix}");
            _rolloutEnd500Announced = true;
        }

        if (!_rolloutEnd100Announced && distToEndFt <= rm[2].TriggerMetres / DistanceFormatter.MetresPerFoot)
        {
            AnnounceInstruction($"Runway end in {rm[2].Label}. Stop.");
            _rolloutEnd100Announced = true;
        }
    }

    /// <summary>
    /// Re-routes the active landing rollout to a new exit. Called by
    /// UpdateLandingRollout when the aircraft has overshot the previously
    /// chosen exit and there is a downfield exit available.
    ///
    /// Calls LoadRoute (re-entrant on _stateLock, safe from inside
    /// UpdateLandingRollout) to build a new route from the current position
    /// to <paramref name="newExit"/>'s node. LoadRoute transitions the
    /// manager to RouteLoaded; we force it back to LandingRollout afterward
    /// so the per-frame loop keeps invoking UpdateLandingRollout with the
    /// new exit. Approach callouts (1500 / 500 / turn-now) are re-armed
    /// for the new exit.
    ///
    /// On LoadRoute failure, falls through to EnterRunwayEndCountdown so
    /// the off-route recalc cannot fire back to the just-passed exit.
    /// </summary>
    private void RetargetLandingExit(Navigation.LandingExit newExit, double lat, double lon, double headingTrue,
        string? overrideAnnouncement = null)
    {
        if (_rolloutExit == null || _dataProvider == null || _graph == null)
        {
            EnterRunwayEndCountdown();
            return;
        }

        string prevName = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
            ? "exit"
            : $"taxiway {_rolloutExit.TaxiwayName}";

        // Try the requested exit; if its route cannot be built, fall forward to
        // the next downfield exit instead of giving up. A single failed
        // LoadRoute used to drop straight into runway-end countdown ("no exit
        // remaining") even with good exits further down the runway — the YSSY
        // 16R failure, where a degenerate retarget target failed to route and
        // condemned the whole rollout. Only declare no-exit once EVERY
        // remaining downfield exit has failed to route.
        Navigation.LandingExit? candidate = newExit;
        while (candidate != null)
        {
            string destNameForRoute = candidate.TaxiwayName.Length > 0
                ? $"Taxiway {candidate.TaxiwayName}"
                : "Exit";

            // LoadRoute acquires _stateLock; same-thread reentrancy is safe.
            // announceSummary:false suppresses the "Route to X via Y" callout.
            string? error = LoadRoute(
                _dataProvider, _icao,
                lat, lon, headingTrue,
                candidate.NodeId,
                destNameForRoute,
                taxiwaySequence: null,
                prebuiltGraph: _graph,
                announceSummary: false,
                isRunwayDestination: false);

            if (error == null)
            {
                _rolloutExit = candidate;
                _isLandingExitRoute = true; // LoadRoute above cleared it; still a landing-exit route
                ResetRolloutApproachLatches();
                // Allow TryEarlyExitHandoff to fire for the newly targeted exit.
                _rolloutEarlyHandoffDone = false;

                // LoadRoute set state to RouteLoaded. Re-enter LandingRollout so
                // the next UpdatePosition frame re-runs UpdateLandingRollout.
                SetState(TaxiGuidanceState.LandingRollout);

                int distAheadFt = (int)Math.Round(
                    TaxiGraph.FastDistanceMeters(lat, lon, candidate.Latitude, candidate.Longitude)
                    * METERS_TO_FEET);
                string newName = string.IsNullOrEmpty(candidate.TaxiwayName)
                    ? "next exit"
                    : $"taxiway {candidate.TaxiwayName}";
                // The caller's override message describes only the originally
                // requested exit; if we fell forward to a later one, drop it.
                string announcement = (candidate == newExit && overrideAnnouncement != null)
                    ? overrideAnnouncement
                    : $"Missed {prevName}. Retargeting {newName}, {DistanceFormatter.FromFeet(distAheadFt)} ahead.";
                AnnounceInstruction(announcement);
                return;
            }

            RolloutDiag($"RetargetLandingExit: route to '{candidate.TaxiwayName}' failed ({error}) — " +
                $"trying next downfield exit");
            candidate = NextDownfieldExit(candidate);
        }

        // Every downfield exit failed to route.
        AnnounceInstruction($"Missed {prevName}. No reachable exit remaining.");
        EnterRunwayEndCountdown();
    }

    /// <summary>
    /// First exit in <see cref="_rolloutAllExits"/> downfield of
    /// <paramref name="afterExit"/> (beyond it by ROLLOUT_OVERSHOOT_FT) that is
    /// not a greater-than-90-degree turn. Null when none remain. Same
    /// suitability rule as the overshoot next-exit scan.
    /// </summary>
    private Navigation.LandingExit? NextDownfieldExit(Navigation.LandingExit afterExit)
    {
        foreach (var e in _rolloutAllExits)
        {
            if (e.DistanceFromThresholdFeet <= afterExit.DistanceFromThresholdFeet + ROLLOUT_OVERSHOOT_FT)
                continue;
            if (e.ExitAngleDegrees > 0.0 && e.ExitAngleDegrees > 90.0)
                continue;
            return e;
        }
        return null;
    }

    /// <summary>
    /// Switches the active rollout into runway-end countdown mode after an
    /// overshoot with no downfield exit available (or after a retarget
    /// failure mid-rollout). Clears `_route` and `_destinationNodeId` so
    /// the off-route detector in the Taxiing branch has nothing to chase
    /// — without that, a route still pointing at the now-passed exit
    /// would trigger TryRecalculateRoute and shortest-path back across
    /// the runway (the original bug).
    ///
    /// Keeps `_rolloutRunway` and `_rolloutRunwayHeadingTrue` because
    /// UpdateRunwayEndCountdown needs them for the distance-to-end
    /// projection. State stays in LandingRollout so UpdatePosition keeps
    /// feeding the per-frame loop (MainForm's position-update gate
    /// includes LandingRollout); UpdateLandingRollout dispatches to
    /// UpdateRunwayEndCountdown when `_rolloutNoExitMode` is set.
    ///
    /// Tone is paused — no steering target. Voice callouts at 1500/500/100
    /// ft to the runway end give the pilot real braking information; full
    /// silence would leave a blind pilot rolling toward the end of an
    /// active runway with no audio cues.
    /// </summary>
    /// <summary>
    /// Scans all taxi-graph nodes for the nearest one in the backtrack heading
    /// direction, within 2000m. Used only by <see cref="EnterBacktracking"/>.
    /// Wider range than <see cref="TaxiGraph.FindNearestNodeInDirection"/> (800m)
    /// to handle airports like LGZA where the apron is ~1006m from the runway end.
    /// No component filter — we just want the nearest reachable apron node.
    /// </summary>
    private TaxiNode? FindBacktrackConnectionNode(double lat, double lon, double backtrackHdg)
    {
        if (_graph == null) return null;
        const double MAX_M = 2000.0;
        TaxiNode? best = null;
        double bestScore = double.MaxValue;
        foreach (var node in _graph.Nodes.Values)
        {
            double dist = TaxiGraph.FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (dist < 5 || dist > MAX_M) continue;
            double bearing = NavigationCalculator.CalculateBearing(lat, lon, node.Latitude, node.Longitude);
            double angleDiff = Math.Abs(NormalizeAngle(bearing - backtrackHdg));
            if (angleDiff > 90) continue;
            double score = dist + (angleDiff * 0.5);
            if (score < bestScore) { bestScore = score; best = node; }
        }
        return best;
    }

    /// <summary>
    /// Enters <see cref="TaxiGuidanceState.BacktrackingOnRunway"/> after the
    /// runway-end countdown completes (pilot stopped or began a 180° turn).
    /// Announces the backtrack heading and begins steering-tone guidance on the
    /// reciprocal runway heading once the pilot's heading comes within 90° of it.
    /// </summary>
    private void EnterBacktracking(double lat, double lon)
    {
        if (_rolloutRunway == null)
        {
            _rolloutNoExitMode = false;
            SetState(TaxiGuidanceState.Taxiing);
            return;
        }

        double reciprocalHdg = (_rolloutRunwayHeadingTrue + 180.0) % 360.0;
        _backtrackHeadingTrue = reciprocalHdg;

        TaxiNode? conn = FindBacktrackConnectionNode(lat, lon, reciprocalHdg);
        if (conn != null)
        {
            _backtrackConnectionLat    = conn.Latitude;
            _backtrackConnectionLon    = conn.Longitude;
            _backtrackConnectionNodeId = conn.NodeId;
        }
        else
        {
            _backtrackConnectionLat    = 0;
            _backtrackConnectionLon    = 0;
            _backtrackConnectionNodeId = 0;
        }

        _backtrackApproachAnnounced = false;
        _rolloutNoExitMode = false;

        // Reset heading-error smoother so no rollout residual leaks into backtrack tone.
        _headingErrorInitialized = false;
        _smoothedHeadingError    = 0;
        _steeringTone.SetPulse(false);
        // Tone resumes on the first UpdateBacktracking frame and pans throughout
        // the 180° turn (no silent phase) — see UpdateBacktracking.

        SetState(TaxiGuidanceState.BacktrackingOnRunway);

        int hdgInt  = (int)Math.Round(reciprocalHdg);
        string rwyId = _rolloutRunway.RunwayID ?? "runway";
        AnnounceInstruction($"End of runway {rwyId}. Turn around, heading {hdgInt}. Backtracking.");
    }

    /// <summary>
    /// Per-frame logic while in <see cref="TaxiGuidanceState.BacktrackingOnRunway"/>.
    /// Uses precision runway-lineup thresholds (silent ±0.5°, active ±1°). The tone
    /// is NEVER silent during the turnaround: while heading is >90° from the
    /// backtrack target it pans hard to whichever side the pilot commits the 180°,
    /// then settles to fine steering as they straighten onto the reciprocal heading.
    /// Transitions to Taxiing when within BACKTRACK_HANDOFF_M of the connection node.
    /// </summary>
    private void UpdateBacktracking(double lat, double lon, double headingTrue, double groundSpeedKts)
    {
        double headingError = NormalizeAngle(_backtrackHeadingTrue - headingTrue);
        double absError = Math.Abs(headingError);

        // Never silent — the tone pans to show which way to turn, follows the
        // pilot around the 180°, and settles to fine centerline-heading steering
        // as they straighten (user preference 2026-07: a backtrack must NOT go
        // quiet during the turnaround; silence reads as "system gave up").
        bool firstFrame = !_headingErrorInitialized;
        if (absError > 90.0)
        {
            // Still swinging through the U-turn. Track the RAW error (no low-pass)
            // so a ±180° sign flip immediately follows whichever way the pilot
            // commits the turn — keeping the pan hard to that side instead of an
            // EMA averaging it toward centre across the wrap.
            _smoothedHeadingError = headingError;
            _headingErrorInitialized = true;
        }
        else
        {
            _smoothedHeadingError = firstFrame
                ? headingError
                : _smoothedHeadingError * (1.0 - HEADING_ERROR_FILTER_ALPHA)
                  + headingError * HEADING_ERROR_FILTER_ALPHA;
            _headingErrorInitialized = true;
        }
        if (!_steeringToneSuppressed)
        {
            if (firstFrame) _steeringTone.Resume();
            _steeringTone.UpdateHeadingErrorWithThresholds(
                _smoothedHeadingError,
                silentThresholdDeg:     0.5,
                activationThresholdDeg: 1.0,
                maxPanThresholdDeg:     15.0);
        }

        LogBacktrackFrame("backtrack", lat, lon, headingTrue,
            headingError, _smoothedHeadingError,
            _backtrackConnectionNodeId > 0
                ? TaxiGraph.FastDistanceMeters(lat, lon, _backtrackConnectionLat, _backtrackConnectionLon)
                : -1);

        if (_backtrackConnectionNodeId <= 0)
        {
            // No taxiway connection node was found in the backtrack direction
            // (FindBacktrackConnectionNode returned null). Do NOT dead-end here
            // — a bare `return` left the pilot stuck in BacktrackingOnRunway
            // forever, on the runway, with no further guidance and no escape.
            // Once they have come round onto the backtrack heading there is
            // nothing more this state can do: say so and hand off to plain
            // Taxiing so Where-Am-I and the taxi planner are available.
            if (absError <= 90.0 && !_backtrackApproachAnnounced)
            {
                _backtrackApproachAnnounced = true;
                _steeringTone.Stop();
                AnnounceInstruction(
                    "Backtracking on the runway. No taxiway connection found — " +
                    "use the taxi planner to set a route.");
                SetState(TaxiGuidanceState.Taxiing);
            }
            return;
        }

        double distM = TaxiGraph.FastDistanceMeters(
            lat, lon, _backtrackConnectionLat, _backtrackConnectionLon);

        if (!_backtrackApproachAnnounced && distM <= BACKTRACK_TAXI_ANNOUNCE_M)
        {
            AnnounceInstruction("Taxiway ahead. Vacate runway.");
            _backtrackApproachAnnounced = true;
        }

        if (distM <= BACKTRACK_HANDOFF_M)
        {
            AnnounceInstruction("Runway vacated.");
            // _route is null; pilot loads the next route via Taxi Assist.
            SetState(TaxiGuidanceState.Taxiing);
        }
    }

    /// <summary>
    /// Per-frame logic while in <see cref="TaxiGuidanceState.BacktrackDeparture"/>
    /// — the FULL-LENGTH backtrack DEPARTURE (opt-in). Steers along the runway
    /// centerline on the RECIPROCAL of the takeoff heading toward the departure
    /// threshold (the full-length lineup point in _lineupTargetLat/Lon), with the
    /// same intercept-angle model as runway lineup so both cross-track and heading
    /// are corrected. NEVER silent: the tone pans to guide the turn onto the runway
    /// and holds the centerline. On reaching the threshold it hands off to
    /// <see cref="UpdateLineup"/>/LiningUp, which pans the 180° turnaround and lines
    /// up + auto-activates Takeoff Assist exactly as a normal full-length departure.
    /// </summary>
    private void UpdateBacktrackDeparture(double lat, double lon, double headingTrue)
    {
        double reciprocalHdgTrue = (_lineupHeadingTrue + 180.0) % 360.0;

        // Cross-track + heading relative to the centerline, in the BACKTRACK
        // direction (reference = reciprocal heading). Same tracker + intercept
        // idiom as the runway-lineup branch of UpdateLineup, just facing the
        // other way, so silence means "on the centerline heading the right way".
        var track = RunwayCenterlineTracker.Compute(
            lat, lon, headingTrue,
            _lineupTargetLat, _lineupTargetLon,
            reciprocalHdgTrue);

        double absCrossFeet   = track.AbsCrossTrackFeet;
        double crossTrackFeet = track.CrossTrackFeet; // signed: + = left of CL, - = right

        const double MAX_INTERCEPT_DEG = 30.0;
        double interceptDeg;
        if (absCrossFeet <= LINEUP_NOISE_DEADBAND_FEET)
            interceptDeg = 0.0;
        else
        {
            double effectiveCross = absCrossFeet - LINEUP_NOISE_DEADBAND_FEET;
            double saturationSpan = LINEUP_INTERCEPT_SAT_FEET - LINEUP_NOISE_DEADBAND_FEET;
            double normalized = Math.Clamp(effectiveCross / saturationSpan, 0.0, 1.0);
            interceptDeg = MAX_INTERCEPT_DEG * Math.Sqrt(normalized) * Math.Sign(crossTrackFeet);
        }
        double desiredHeadingTrue = reciprocalHdgTrue + interceptDeg;
        double toneHeadingError = NormalizeAngle(desiredHeadingTrue - headingTrue);

        bool firstFrame = !_headingErrorInitialized;
        // >90° from the backtrack heading = still turning onto the runway from the
        // entrance. Track the raw error (no low-pass) so the pan follows the turn;
        // low-pass only once roughly aligned, for smooth centerline steering.
        if (Math.Abs(toneHeadingError) > 90.0)
            _smoothedHeadingError = toneHeadingError;
        else
            _smoothedHeadingError = firstFrame
                ? toneHeadingError
                : _smoothedHeadingError * (1 - HEADING_ERROR_FILTER_ALPHA)
                  + toneHeadingError * HEADING_ERROR_FILTER_ALPHA;
        _headingErrorInitialized = true;

        if (!_steeringToneSuppressed)
        {
            if (firstFrame) _steeringTone.Resume();
            _steeringTone.UpdateHeadingErrorWithThresholds(
                _smoothedHeadingError,
                silentThresholdDeg:     0.5,
                activationThresholdDeg: 1.0,
                maxPanThresholdDeg:     15.0);
        }

        // Distance remaining to the departure threshold (full-length lineup point).
        double distToThreshold = TaxiGraph.FastDistanceMeters(
            lat, lon, _lineupTargetLat, _lineupTargetLon);

        LogBacktrackFrame("btDep", lat, lon, headingTrue,
            toneHeadingError, _smoothedHeadingError, distToThreshold);

        if (!_backtrackDepApproachAnnounced && distToThreshold <= BACKTRACK_DEP_APPROACH_M)
        {
            _backtrackDepApproachAnnounced = true;
            int toHdg = (int)Math.Round(_lineupHeadingMag);
            AnnounceInstruction(
                $"Approaching runway end. Slow down, prepare to turn around to heading {toHdg}.");
        }

        if (distToThreshold <= BACKTRACK_DEP_HANDOFF_M)
        {
            // Reached the departure threshold — hand to LiningUp. The lineup target
            // is already the full-length point + takeoff heading (set in LoadRoute),
            // so LiningUp pans the 180° turnaround, converges on the centerline,
            // announces "Lined up", and fires RequestTakeoffAssistAutoActivate. Reset
            // the smoother so the reciprocal-phase error can't leak into it.
            SetState(TaxiGuidanceState.LiningUp);
            _lineupAnnouncedAligned = false;
            _smoothedHeadingError = 0.0;
            _headingErrorInitialized = false;
            if (!_steeringToneSuppressed) _steeringTone.Resume();

            int toHdgMag = (int)Math.Round(_lineupHeadingMag);
            AnnounceInstruction($"Runway end. Turn around to line up, heading {toHdgMag}.");
        }
    }

    private void EnterRunwayEndCountdown()
    {
        _route = null;
        _destinationNodeId = 0;
        _currentSegmentIndex = 0;
        _originalTaxiwaySequence = null;
        _rolloutExit = null;
        _isLandingExitRoute = false; // no exit route — runway-end countdown
        _landingExitOffPavement = true;
        // KEEP _rolloutRunway and _rolloutRunwayHeadingTrue — countdown needs them.
        _rolloutAllExits = new List<Navigation.LandingExit>();
        ResetRolloutApproachLatches();
        _rolloutEnd1500Announced = false;
        _rolloutEnd500Announced = false;
        _rolloutEnd100Announced = false;
        _rolloutNoExitMode = true;
        _offRouteSince = DateTime.MinValue;
        _steeringTone.Pause();
        // Stay in LandingRollout so UpdateLandingRollout (→ UpdateRunwayEndCountdown)
        // runs each frame.
        SetState(TaxiGuidanceState.LandingRollout);
    }

    /// <summary>
    /// Per-frame logic while in <see cref="TaxiGuidanceState.LiningUp"/>.
    /// For runways: guides aircraft to runway centerline using cross-track + heading.
    /// For gates:   guides to correct parking heading (no centerline concept).
    /// Heading is weighted more heavily than centerline (user preference).
    /// Hysteresis: looser ENTER thresholds than EXIT prevent chatter at the boundary.
    /// </summary>
    private void UpdateLineup(double lat, double lon, double headingTrue, double headingMag)
    {
        if (_isRunwayLineup)
        {
            // Shared centerline math — same as takeoff assist uses during roll
            var track = RunwayCenterlineTracker.Compute(
                lat, lon, headingTrue,
                _lineupTargetLat, _lineupTargetLon,
                _lineupHeadingTrue);

            double headingError = track.HeadingErrorDeg;
            double absCrossFeet = track.AbsCrossTrackFeet;
            double crossTrackFeet = track.CrossTrackFeet; // signed: + = left of CL, - = right

            // INTERCEPT-ANGLE guidance (same idea as ILS localizer capture).
            //
            // The desired heading at any moment is:
            //   desiredHeading = runwayHeading + intercept · sign(crossTrack)
            //
            // intercept rises from 0° at the centerline to MAX_INTERCEPT_DEG at
            // LINEUP_INTERCEPT_SAT_FEET on a SQUARE-ROOT curve. The sqrt is the
            // key to making the tone work for a blind pilot: a linear ramp
            // through a 50 ft deadband produced a "silent gap" between ~50–80 ft
            // off centerline, where the heading error stayed below the steering
            // tone's activation threshold and the pilot had no audible cue that
            // they were drifting. With sqrt, even 15–20 ft of cross-track yields
            // ~12° of heading correction → comfortably above the (tightened)
            // hysteresis below, so the tone speaks up early and clearly.
            //
            // Sign convention (RunwayCenterlineTracker): crossTrack > 0 ⇒ aircraft
            // LEFT of CL ⇒ desired heading should be RIGHT of runway heading
            // (i.e., desiredHeading = runwayHeading + intercept). Symmetric on
            // the other side. The small LINEUP_NOISE_DEADBAND_FEET (≤8 ft)
            // exists only to keep GPS-noise sign-flips near the line from
            // chattering the tone — NOT to silence "small" errors. For a blind
            // pilot, the tone is the instrument; small errors must remain
            // audible.
            const double MAX_INTERCEPT_DEG = 30.0;
            double interceptDeg;
            if (absCrossFeet <= LINEUP_NOISE_DEADBAND_FEET)
            {
                interceptDeg = 0.0;
            }
            else
            {
                double effectiveCross = absCrossFeet - LINEUP_NOISE_DEADBAND_FEET;
                double saturationSpan = LINEUP_INTERCEPT_SAT_FEET - LINEUP_NOISE_DEADBAND_FEET;
                double normalized = Math.Clamp(effectiveCross / saturationSpan, 0.0, 1.0);
                interceptDeg = MAX_INTERCEPT_DEG * Math.Sqrt(normalized) * Math.Sign(crossTrackFeet);
            }
            double desiredHeadingTrue = _lineupHeadingTrue + interceptDeg;
            double toneHeadingError = NormalizeAngle(desiredHeadingTrue - headingTrue);

            _smoothedHeadingError = _headingErrorInitialized
                ? _smoothedHeadingError * (1 - HEADING_ERROR_FILTER_ALPHA) + toneHeadingError * HEADING_ERROR_FILTER_ALPHA
                : toneHeadingError;
            _headingErrorInitialized = true;

            // Diagnostic frame trace for runway lineup. Captures the full lineup-phase
            // state so post-flight analysis can pinpoint bugs like "the system is
            // redirecting me away from the runway." Rate-limited inside LogGuidanceFrame.
            // Field overloading vs the taxi-phase format (a separate column-set would
            // mean dual schemas in one log; reusing the columns keeps post-hoc tooling
            // simple):
            //   seg = -1                    → distinguishes lineup-phase rows from taxi
            //   segBrg = desiredHeadingTrue → the heading the tone is steering to
            //   w = _lineupHeadingTrue      → the runway's true heading (reference)
            //   tLat, tLon = threshold      → so a reader can recompute geometry
            //   raw = crossTrackFeet        → signed (+left of CL, -right) — IMPORTANT
            //                                 for diagnosing sign-direction bugs
            //   smooth = smoothed heading error (degrees) — the actual tone driver
            // gs = the real cached ground speed. It was hardcoded to 0.0 here, so every
            // runway-lineup row in taxi_guidance.log read gs=0.0 while the aircraft was
            // rolling at 11 kt (LEPA 2026-08-16) — which silently removes speed from
            // every post-flight lineup diagnosis. The gate-lineup frame below always
            // logged the real value; the two now agree.
            LogGuidanceFrame(
                lat, lon, headingTrue, _lastGroundSpeedKts,
                /* segIdx */ -1, /* segBrg = desiredHeadingTrue */ desiredHeadingTrue,
                /* w = _lineupHeadingTrue (reference) */ _lineupHeadingTrue,
                /* nxtTurn */ true,
                _lineupTargetLat, _lineupTargetLon,
                /* raw = signed crossTrackFeet */ crossTrackFeet,
                /* smooth = smoothed heading error */ _smoothedHeadingError);
            // PRECISION hysteresis for runway LINEUP. We pass explicit thresholds
            // (bypassing the width-scaling MIN_SCALE clamp at 0.65, which still
            // gave silent ≈1.95° / activation ≈3.9° — too loose). With a 3° dead
            // band a pilot approaching alignment from a 10° error released
            // rudder pressure when the tone went silent, drifted back to ~3°,
            // and the tone STAYED silent (3° < 3.9° activation) — leaving the
            // aircraft sitting 3° off heading with no audio cue.
            //
            // New thresholds: silent 0.5° / activation 1° / max-pan 15°. The
            // tone now keeps panning until the heading is genuinely centered
            // within half a degree, and resumes immediately if the pilot drifts
            // past 1°. Max-pan at 15° (vs old 19.5°) gives stronger feedback
            // sooner. This is precision work at low speed — the tone has to be
            // tighter than the GPS noise floor for runway-takeoff alignment.
            if (!_steeringToneSuppressed)
            {
                _steeringTone.UpdateHeadingErrorWithThresholds(
                    _smoothedHeadingError,
                    silentThresholdDeg: 0.5,
                    activationThresholdDeg: 1.0,
                    maxPanThresholdDeg: 15.0);
            }

            // Lineup-aligned hysteresis (gates the "Lined up" announcement and
            // the steering-tone Pause). Enter when BOTH heading < 1° AND
            // cross-track < 10 ft; only re-resume tone if drifted to > 2° OR
            // > 20 ft. Tighter than before so "Lined up" only fires when the
            // pilot really is, and any post-aligned drift past 2° re-enables
            // the steering tone for active correction.
            double enterHdg = 1.0;
            double exitHdg  = 2.0;
            double enterCtr = 10.0;
            double exitCtr  = 20.0;

            bool enterAligned = Math.Abs(headingError) < enterHdg && absCrossFeet < enterCtr;
            bool stillAligned = Math.Abs(headingError) < exitHdg  && absCrossFeet < exitCtr;

            // Stay in LiningUp state even when aligned — just mute the tone.
            // If pilot drifts (happens: nudging brakes, gust), we want the tone to come back.
            // User ends lineup mode by stopping taxi guidance or activating takeoff assist.
            if (!_lineupAnnouncedAligned && enterAligned)
            {
                _lineupAnnouncedAligned = true;
                _steeringTone.Pause();
                // Per FAA AIM 5-2-5 (Line Up and Wait) and ICAO Doc 4444 / EASA
                // SERA: a "line up and wait" clearance authorizes the aircraft
                // to taxi onto the runway, align with the centerline, and
                // REMAIN STATIONARY awaiting further clearance. The aircraft
                // also stops here for "cleared for takeoff" (briefly, before
                // setting thrust). Either way, alignment-achieved = stop point.
                // This matches what runway-teleport puts you at (20 m back
                // from the threshold, aligned), so taxi guidance and teleport
                // converge on the same final state.
                _announcer.AnnounceImmediate($"Lined up, {_destinationName}. Hold position.");

                // Fire auto-activate request — ONE-SHOT per route, gated by
                // _isRunwayLineup (gates lineup-aligned auto-activate to
                // runway destinations, not gates). MainForm's handler decides
                // whether to actually toggle based on the user setting and
                // current takeoff-assist state.
                if (_isRunwayLineup && !_autoActivateFired)
                {
                    _autoActivateFired = true;
                    // Strip "Runway " prefix from _destinationName so the
                    // event payload's RunwayId is just the designator,
                    // matching what TakeoffAssistManager.SetRunwayReference
                    // expects (e.g. "27L", not "Runway 27L").
                    string rwyId = _destinationName ?? "";
                    if (rwyId.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase))
                        rwyId = rwyId.Substring(7).Trim();
                    RequestTakeoffAssistAutoActivate?.Invoke(this,
                        new TakeoffAssistAutoActivateEventArgs
                        {
                            RunwayId = rwyId,
                            AirportIcao = _icao
                        });
                }
            }
            else if (_lineupAnnouncedAligned && !stillAligned)
            {
                _lineupAnnouncedAligned = false;
                if (!_steeringToneSuppressed) _steeringTone.Resume();
            }

            // Stopped + misaligned → PULSE the tone on/off instead of continuous.
            // Same pan direction (so the pilot still knows which way to turn),
            // but the pulse rhythm makes it audibly different from "moving and
            // tracking" — a clear "you've stopped but you're not done" cue
            // without stealing attention with voice (rudder pedals + throttle
            // already occupy both hands and feet during lineup). Pulse off
            // when moving (tone is enough) or aligned (silent anyway).
            // Pulse on stopped + (heading misaligned OR cross-track misaligned).
            // The cross-track branch is the critical one: when intercept-angle
            // saturates at ±30° because cross-track is large, the desired
            // heading is offset from runway heading, and a pilot who matches
            // that desired heading then stops gets a centered tone (heading
            // error ≈ 0) even though cross-track is still huge. Without the
            // cross-track condition here, the pilot has NO audio cue that
            // they're not yet aligned and need to move forward to let the
            // intercept controller close on centerline.
            bool stoppedAndMisaligned =
                !_lineupAnnouncedAligned &&
                _lastGroundSpeedKts <= LINEUP_PULSE_MAX_GS_KTS &&
                (Math.Abs(headingError) >= LINEUP_PULSE_MIN_HDG_ERR_DEG ||
                 absCrossFeet >= LINEUP_PULSE_MIN_CROSS_FEET);
            if (!_steeringToneSuppressed) _steeringTone.SetPulse(stoppedAndMisaligned);

            // Unreachable-runway bailout. If the aircraft sits far off the
            // runway centerline and stays there, the route never reached the
            // runway (the entered clearance ended on a parallel taxiway, with no
            // connector). The intercept controller saturates and the cross-track
            // never closes, so the tone would pan forever (PHNL 04L 2026-06-13,
            // ~4 minutes). Rather than steer toward an unreachable target
            // silently, tell the pilot once — clearly and actionably. One-shot
            // per route (latch reset on LoadRoute / StopGuidance).
            //
            // GATED ON _routeReachesRunway: a lineup does NOT always begin on the
            // centerline extended. A route truncated to a far-back ILS hold (or a
            // hold on an angled connector) legitimately starts the intercept
            // hundreds of feet off the perpendicular and converges over a long
            // creep (LPPT 02 2026-06-16: started at 458 ft and closed to 0 — but
            // sat >400 ft for ~32 s, long enough to false-fire the bare
            // >400 ft / 12 s test). _routeReachesRunway (measured at the
            // destination node, not the hold-short) is true there, so the bailout
            // is correctly disarmed; it still fires for the genuine PHNL case
            // where the destination node itself is far off the runway.
            if (!_routeReachesRunway && absCrossFeet > LINEUP_UNREACHABLE_CROSS_FEET)
            {
                if (_lineupHugeCrossTrackSince == DateTime.MinValue)
                    _lineupHugeCrossTrackSince = DateTime.UtcNow;
                else if (!_runwayLineupUnreachableWarned &&
                         (DateTime.UtcNow - _lineupHugeCrossTrackSince).TotalSeconds >= LINEUP_UNREACHABLE_SEC)
                {
                    _runwayLineupUnreachableWarned = true;
                    _announcer.AnnounceImmediate(
                        $"This route does not reach {_destinationName}. Reprogram the taxi " +
                        $"route, including the taxiway that connects to the runway.");
                }
            }
            else
            {
                _lineupHugeCrossTrackSince = DateTime.MinValue;
            }
        }
        else
        {
            // A gate DOES have a centerline: the lead-in line through the parking
            // position along the gate heading. Steer to it with the SAME intercept-
            // angle model as the runway lineup, so we correct BOTH lateral offset
            // (cross-track) AND heading — converging precisely onto the centerline,
            // aligned with the gate. The old heading-only cue ignored cross-track,
            // so a pilot could stop heading-aligned but laterally offset and a few
            // degrees of yaw off ("parked a bit to the right and askew", per GSX).
            var track = RunwayCenterlineTracker.Compute(
                lat, lon, headingTrue, _lineupTargetLat, _lineupTargetLon, _lineupHeadingTrue);
            double headingError   = track.HeadingErrorDeg;
            double absCrossFeet   = track.AbsCrossTrackFeet;
            double crossTrackFeet = track.CrossTrackFeet; // signed: + = left of CL, - = right

            const double MAX_INTERCEPT_DEG = 30.0;
            double interceptDeg;
            if (absCrossFeet <= LINEUP_NOISE_DEADBAND_FEET)
                interceptDeg = 0.0;
            else
            {
                double effectiveCross = absCrossFeet - LINEUP_NOISE_DEADBAND_FEET;
                double saturationSpan = LINEUP_INTERCEPT_SAT_FEET - LINEUP_NOISE_DEADBAND_FEET;
                double normalized = Math.Clamp(effectiveCross / saturationSpan, 0.0, 1.0);
                interceptDeg = MAX_INTERCEPT_DEG * Math.Sqrt(normalized) * Math.Sign(crossTrackFeet);
            }
            double desiredHeadingTrue = _lineupHeadingTrue + interceptDeg;
            double toneHeadingError = NormalizeAngle(desiredHeadingTrue - headingTrue);

            _smoothedHeadingError = _headingErrorInitialized
                ? _smoothedHeadingError * (1 - HEADING_ERROR_FILTER_ALPHA) + toneHeadingError * HEADING_ERROR_FILTER_ALPHA
                : toneHeadingError;
            _headingErrorInitialized = true;

            // Precision thresholds (same as runway lineup): keep panning until the
            // heading is centred within ½°, full pan by 15° — tighter than the old
            // 5° gate tolerance so the final park is square on the centerline.
            if (!_steeringToneSuppressed)
                _steeringTone.UpdateHeadingErrorWithThresholds(_smoothedHeadingError, 0.5, 1.0, 15.0);

            // Gate-lineup telemetry (rate-limited). seg=-2 marks gate-lineup frames
            // (runway lineup uses -1). Columns: hdg=aircraft true heading, segBrg=the
            // heading the tone is steering to (desired), w=_lineupHeadingTrue (the gate
            // reference heading — should match the gate, var-corrected), nxtTurn=1 when
            // the tone is SUPPRESSED (so I can see if docking muted it), raw=signed
            // cross-track ft, smooth=smoothed tone error (the actual pan driver).
            LogGuidanceFrame(
                lat, lon, headingTrue, _lastGroundSpeedKts,
                /* seg = gate-lineup marker */ -2,
                /* segBrg = desired heading */ desiredHeadingTrue,
                /* w = gate reference heading */ _lineupHeadingTrue,
                /* nxtTurn = tone suppressed? */ _steeringToneSuppressed,
                _lineupTargetLat, _lineupTargetLon,
                /* raw = signed cross-track ft */ crossTrackFeet,
                /* smooth = smoothed tone error */ _smoothedHeadingError);

            // Aligned hysteresis — heading AND cross-track, but the PRECISION depends on
            // who finishes the park. The synthetic centerline runs through the navdata
            // parking point, which is routinely metres off the real stand markings:
            // • Docking ENGAGED (_dockingActive): docking's own tone + 0.3 m stop own the
            //   precision and taxi's verbal is suppressed anyway — keep the tight
            //   runway-grade band so the brief pre-mute window can't flap.
            // • Docking NOT engaged (disabled / never engaged): taxi IS the arrival
            //   guidance. Use the forgiving band — requiring ~12 ft to a possibly-offset
            //   navdata point left correctly-parked pilots permanently "not aligned"
            //   (no "Parking brake." cue) even though they were square on the real stand.
            double enterHdg = _dockingActive ? 1.0 : LINEUP_HEADING_TOLERANCE_DEG - 1.0;        // 1° / 4°
            double exitHdg  = _dockingActive ? 2.0 : LINEUP_HEADING_TOLERANCE_DEG + 2.0;        // 2° / 7°
            double enterCtr = _dockingActive ? LINEUP_CENTERLINE_TOLERANCE_FEET * 0.5
                                             : LINEUP_CENTERLINE_TOLERANCE_FEET;                // 12.5 / 25 ft
            double exitCtr  = _dockingActive ? LINEUP_CENTERLINE_TOLERANCE_FEET
                                             : LINEUP_CENTERLINE_TOLERANCE_FEET * 1.6;          // 25 / 40 ft
            bool enterAligned = Math.Abs(headingError) < enterHdg && absCrossFeet < enterCtr;
            bool stillAligned = Math.Abs(headingError) < exitHdg && absCrossFeet < exitCtr;

            // Same as runway branch — stay in LiningUp; mute/resume via hysteresis.
            if (!_lineupAnnouncedAligned && enterAligned)
            {
                _lineupAnnouncedAligned = true;
                _steeringTone.Pause();
                // When docking owns the stop it announces the stop/brake at the precise
                // position — don't pre-empt with "parking brake" the moment we're merely
                // laterally aligned (we may still be a couple of metres short of the stop).
                // Docking PENDING (armed, GSX stop still ahead, not yet engaged): saying
                // "Parking brake." here parks the pilot tens of metres short of the real
                // stop (KATL F3: 26 s stationary at 33.7 m) — redirect them forward.
                if (!_dockingActive)
                    _announcer.AnnounceImmediate(_dockingPending
                        ? $"Aligned with {_destinationName}. Continue ahead. Docking guidance will take over."
                        : $"Aligned with {_destinationName}. Parking brake.");
            }
            else if (_lineupAnnouncedAligned && !stillAligned)
            {
                _lineupAnnouncedAligned = false;
                if (!_steeringToneSuppressed) _steeringTone.Resume();
            }

            // Gate lineup never pulses. The runway-style stopped-misaligned pulse was
            // briefly enabled here for parking precision, but precision parking is now
            // docking guidance's job (its engaged tone + 0.3 m stop): while docking is
            // engaged taxi's tone is muted entirely, and while it is NOT engaged the
            // reference is a navdata parking point that can sit metres off the real
            // stand — pulsing 3 Hz at a correctly-parked pilot demanding precision to
            // a wrong point is a misfeature. (Runway lineup keeps its pulse — its
            // centerline reference is authoritative.)
            if (!_steeringToneSuppressed) _steeringTone.SetPulse(false);
        }
    }

    private static void RolloutDiag(string msg)
    {
        try { _rolloutDiagLog.Info($"{msg}"); }
        catch { /* never fail on diag */ }
    }

    /// <summary>
    /// Rate-limited per-frame diagnostic for the two backtrack states, mirroring
    /// LogGuidanceFrame's role for Taxiing: proof in taxi_guidance.log that
    /// position frames are actually reaching the state's update method (the EGNM
    /// failure was frame starvation with no log evidence). Shares the Taxiing
    /// logger's throttle — the states are mutually exclusive. distM is the
    /// distance to the state's target (connection node / departure threshold),
    /// -1 when no target exists.
    /// </summary>
    private void LogBacktrackFrame(
        string phase, double lat, double lon, double headingTrue,
        double rawHeadingError, double smoothedHeadingError, double distM)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastGuidanceLogTime).TotalMilliseconds < GUIDANCE_LOG_INTERVAL_MS) return;
        _lastGuidanceLogTime = now;
        try
        {
            _guidanceLog.Info(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}: lat={1:F7},lon={2:F7},hdg={3:F1},raw={4:F2},smooth={5:F2},dist={6:F0}",
                phase, lat, lon, headingTrue,
                rawHeadingError, smoothedHeadingError, distM));
        }
        catch { /* diagnostic only — never crash guidance for a log failure */ }
    }

}
