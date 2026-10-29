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
        _rolloutTurnPrepAnnounced = false;
        _rolloutApproach500Announced = false;
        _rolloutTurnNowAnnounced = false;
        _rolloutTurnToneTargetDeg = 0.0;
        _rolloutTurnTonePause = false;
        _rolloutStoppedShortAnnounced = false;
        _rolloutTooFastNoExit = false;
        _rolloutCountdownStatusOwed = false;
        // Not Silent: until this rollout's first tone frame the landing assist's centerline cue keeps the tone
        // (RolloutExitGate.ToneModeAtRolloutEntry), so ownership never fails toward both tones silent.
        _rolloutToneMode = Navigation.RolloutExitGate.ToneModeAtRolloutEntry;
        _rolloutToneLogMode = null;
        _rolloutToneLogExit = null;
        _rolloutToneLogUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Whether the post-turn-now steering tone may use ExitBearingTrue: the
    /// bearing's side of the runway must AGREE with the spoken turn direction.
    ///
    /// The two come from different sources — the word from the junction → ApronNodeId
    /// bearing (the side the route actually vacates to), the tone from the exit's
    /// first-edge bearing — and the whole-DB consistency sweep (2026-08-26) found
    /// ~2,000 Normal exits where they disagree in SIGN: crossing exits whose
    /// best-edge tie-break picked the far bank outside the reconcile's gates (CYYZ
    /// D3/H/N/R/S/T), and reverse-angled RETs whose first edge is a near-parallel
    /// stub pointing the other way (KCLT W2/W9/E6-E10). There the pilot heard
    /// "Turn left now" while the tone pulled right, saturating as they obeyed the
    /// verbal — the one contradiction this app must never produce.
    ///
    /// A null word or an uncomputed bearing has nothing to contradict — trusted.
    /// </summary>
    internal static bool ExitToneBearingAgreesWithTurnWord(
        string? spokenDirection, double exitBearingTrue, double runwayHeadingTrue)
    {
        if (spokenDirection == null) return true;
        if (exitBearingTrue <= 0.0) return true;
        double brg = exitBearingTrue == 360.0 ? 0.0 : exitBearingTrue;
        double delta = NormalizeAngle(brg - runwayHeadingTrue);
        if (delta == 0.0) return true;
        string bearingSide = delta < 0 ? "left" : "right";
        return string.Equals(bearingSide, spokenDirection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Picks the steering-tone target for the turn phase of a Normal exit — the
    /// heading the tone steers toward from the "turn now" callout until the 15°
    /// turnBegun handoff brings live route guidance in. The tone is the pilot's
    /// instrument; it must ACTIVELY guide the commanded turn in every case, never
    /// pull against the verbal and never fall silent while a real source exists.
    ///
    /// Sources, best first:
    ///   1. ExitBearingTrue — when its side agrees with the spoken word AND it is
    ///      meaningfully off the runway axis (≥ EXIT_TURN_DIRECTION_MIN_DEG). The
    ///      measured best-edge bearing; today's behaviour for the healthy majority.
    ///   2. The junction → ApronNodeId bearing — the direction of the node the
    ///      handoff route actually terminates at, and the SAME source as the spoken
    ///      word, so tone and verbal structurally agree. Covers the stub-first-edge
    ///      exits (KDTW Y3-class: first edge ~1.4° while the pavement turns 81°,
    ///      where ExitBearingTrue ≈ runway heading pulled BACK against the turn)
    ///      and the wrong-bank residuals no data fix reaches (KCLT reverse RETs).
    ///      Slightly over-commanding on a curved exit is fine — the sign is right
    ///      and turnBegun hands off to the route tone within ~15° of turn.
    ///   3. Word spoken but neither bearing usable → PAUSE. The verbal owns the
    ///      turn for the second or two until turnBegun; a runway-heading hold here
    ///      would pull against the commanded turn, which is worse than silence.
    ///   4. Nothing at all → hold runway heading (exit direction genuinely unknown;
    ///      pre-existing behaviour).
    ///
    /// Returns (targetDeg, pause): targetDeg &gt; 0 = steer to it (360 = due north),
    /// 0 = hold runway heading; pause = true silences the tone until handoff.
    /// </summary>
    internal static (double targetDeg, bool pause) ResolveTurnToneTarget(
        string? spokenDirection, double exitBearingTrue, double? apronBearingTrue,
        double runwayHeadingTrue)
    {
        if (exitBearingTrue > 0.0
            && ExitToneBearingAgreesWithTurnWord(spokenDirection, exitBearingTrue, runwayHeadingTrue))
        {
            double brg = exitBearingTrue == 360.0 ? 0.0 : exitBearingTrue;
            if (Math.Abs(NormalizeAngle(brg - runwayHeadingTrue)) >= EXIT_TURN_DIRECTION_MIN_DEG)
                return (exitBearingTrue, false);
        }

        if (apronBearingTrue.HasValue)
        {
            double apron = apronBearingTrue.Value == 360.0 ? 0.0 : apronBearingTrue.Value;
            if (Math.Abs(NormalizeAngle(apron - runwayHeadingTrue)) >= EXIT_TURN_DIRECTION_MIN_DEG)
                return (apronBearingTrue.Value == 0.0 ? 360.0 : apronBearingTrue.Value, false);
        }

        return spokenDirection != null ? (0.0, true) : (0.0, false);
    }

    /// <summary>
    /// Bearing from the exit junction to its ApronNodeId, or null when the apron
    /// node is missing/degenerate. Shares its geometry with ResolveExitTurnDirection
    /// so the tone target and the spoken word can never disagree by construction.
    /// </summary>
    private double? ApronBearingFromJunction()
    {
        if (_rolloutExit == null) return null;
        if (_rolloutExit.ApronNodeId <= 0 || _rolloutExit.ApronNodeId == _rolloutExit.NodeId)
            return null;
        if (_graph == null || !_graph.Nodes.TryGetValue(_rolloutExit.ApronNodeId, out var apronNode))
            return null;
        double brg = NavigationCalculator.CalculateBearing(
            _rolloutExit.Latitude, _rolloutExit.Longitude,
            apronNode.Latitude, apronNode.Longitude);
        return brg == 0.0 ? 360.0 : brg;
    }

    /// <summary>
    /// The targeted exit's own turn window: its lateral offset in the rollout runway's frame, its angle and
    /// the runway width (RolloutExitGate.TurnWindowFeetFor), or the fixed RolloutExitGate.TurnWindowFeet
    /// with no exit targeted. Computed where it is read, from the exit and runway targeted NOW - a cached
    /// copy had to be recomputed after every assignment of _rolloutExit or _rolloutRunway, and one missed
    /// assignment would leave another exit's window judging this one. Feeds IsExitTurnBegun and
    /// SelectToneMode in place of the fixed 1,000 ft. Each targeted exit's window is logged once.
    /// </summary>
    private double RolloutExitTurnWindowFeet()
    {
        if (_rolloutExit == null || _rolloutRunway == null)
            return Navigation.RolloutExitGate.TurnWindowFeet;
        double lateralM = SignedLateralFromRunwayMeters(
            _rolloutExit.Latitude, _rolloutExit.Longitude,
            _rolloutRunway.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue);
        double window = Navigation.RolloutExitGate.TurnWindowFeetFor(
            _rolloutRunway.Width, lateralM, _rolloutExit.ExitAngleDegrees);
        if (!ReferenceEquals(_rolloutTurnWindowLoggedExit, _rolloutExit))
        {
            _rolloutTurnWindowLoggedExit = _rolloutExit;
            RolloutDiag($"Turn window for '{_rolloutExit.TaxiwayName}': {window:F0} ft " +
                $"(node lateral {lateralM:+0.0;-0.0} m, angle {_rolloutExit.ExitAngleDegrees:F1} deg, " +
                $"runway width {_rolloutRunway.Width:F0} ft)");
        }
        return window;
    }

    /// <summary>
    /// Resets the landing-exit OUTCOME flags that decide HandleArrival's closure. One owner for a
    /// block that was hand-copied into LoadRoute, StopGuidance and the no-route rollout entry and had
    /// already drifted (the rollout copy lacked the last two fields). The values are LoadRoute's:
    /// <c>_landingExitOffPavement</c> resets to TRUE, because a new route re-decides it at its own
    /// handoff.
    /// </summary>
    private void ResetLandingExitOutcomeFlags()
    {
        _landingExitOffPavement = true;
        _landingExitMissed = false;
        _landingExitVacatedEarly = false;
        _landingExitVacatedEarlyPlannedName = null;
        _landingExitRouteUnreachable = false;
        _landingExitMinDistToTargetM = double.MaxValue;
        _missedVacateSince = DateTime.MinValue;
    }

    /// <summary>
    /// Applies the pilot's crossings and steering-tone settings and starts the tone, for the two
    /// landing-rollout entries reached without StartGuidance (which normally does this):
    /// BeginLandingRolloutNoGraph and BeginRunwayEndCountdownRollout.
    /// </summary>
    private void StartRolloutSteeringTone(UserSettings settings)
    {
        _announceCrossings = settings.TaxiGuidanceAnnounceCrossings;
        _steeringTone.InvertPan = settings.TaxiGuidanceInvertSteeringTone;
        _steeringTone.HardPan = settings.TaxiGuidanceHardPanTone;
        _lastToneWaveform = settings.TaxiGuidanceToneWaveform;
        _lastToneVolume = settings.TaxiGuidanceToneVolume;
        _steeringTone.Start(settings.TaxiGuidanceToneWaveform, settings.TaxiGuidanceToneVolume);
    }

    /// <summary>
    /// Speaks a landing-exit rollout's touchdown sentence through AnnounceInstruction, so Ctrl+Y can
    /// replay it. With a runway correction it first retires every approach milestone the aircraft
    /// is already inside, or will reach within ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC, and folds what
    /// they uniquely add into the sentence (Navigation.TouchdownCallout). Without one, nothing is
    /// retired and the sentence is the pre-existing one. Callers set _rolloutExit and
    /// _rolloutRunwayHeadingTrue and reset the approach latches first.
    /// </summary>
    private void AnnounceTouchdownCallout(
        Navigation.LandingExit exit, double touchdownLat, double touchdownLon,
        double groundSpeedKts, Navigation.TouchdownRunwayCorrection? correction, string notes = "")
    {
        // Use the actual aircraft-to-exit distance when the caller provides touchdown coordinates —
        // this accounts for short or long landings. Fall back to the precomputed
        // DistanceFromTouchdownFeet only when coordinates are unavailable.
        int distFt = touchdownLat != 0 && touchdownLon != 0
            ? (int)Math.Round(TaxiGraph.FastDistanceMeters(touchdownLat, touchdownLon, exit.Latitude, exit.Longitude) * METERS_TO_FEET)
            : (int)Math.Round(exit.DistanceFromTouchdownFeet);

        var retired = default(Navigation.ExitCalloutRetirement);
        string? turnPhrase = null;
        if (correction.HasValue)
        {
            var xm = DistanceMilestones.ExitApproach(); // far->near: [0]=1500ft/500m, [1]=900ft/300m, [2]=500ft/150m
            retired = Navigation.TouchdownCallout.RetireExitCallouts(
                distFt, groundSpeedKts, exit.ExitType, ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC,
                xm[0].TriggerMetres / DistanceFormatter.MetresPerFoot,
                xm[1].TriggerMetres / DistanceFormatter.MetresPerFoot,
                xm[2].TriggerMetres / DistanceFormatter.MetresPerFoot,
                ROLLOUT_TURN_NOW_FT, Navigation.RolloutExitGate.SlowDownAboveKts(exit.ExitAngleDegrees, exit.ExitType));

            if (retired.Retire1500) _rolloutApproach1500Announced = true;
            if (retired.Retire900) _rolloutApproach900Announced = true;
            if (retired.Retire500) _rolloutApproach500Announced = true;
            // Never "turn now" folded in at a speed the turn cannot be made at: left unretired then, the turn-now
            // block judges that point with its too-fast rule.
            if (retired.RetireTurnNow
                && !Navigation.RolloutExitGate.IsTooFastToTurn(groundSpeedKts, exit.ExitAngleDegrees))
            {
                _rolloutTurnNowAnnounced = true;
                // The turn-now block's own side effect, reproduced because that block will not run.
                if (exit.ExitType == "Normal"
                    && Navigation.RolloutExitGate.IsPlausibleExitBearing(exit.ExitBearingTrue, _rolloutRunwayHeadingTrue))
                    _headingErrorInitialized = false;
                turnPhrase = ComposeExitTurnPhrase(touchdownLat, touchdownLon, _rolloutRunwayHeadingTrue);
            }

            RolloutDiag($"Touchdown correction {correction.Value.PlannedRunwayId} -> {correction.Value.ActualRunwayId}: " +
                $"distToExit={distFt}ft gs={groundSpeedKts:F1}kt retire1500={retired.Retire1500} " +
                $"retire900={retired.Retire900} retire500={retired.Retire500} " +
                $"retireTurnNow={retired.RetireTurnNow} slowDown={retired.SlowDown}");
        }

        AnnounceInstruction(Navigation.TouchdownCallout.ComposeExit(
            correction, exit.ExitType, exit.TaxiwayName, distFt, retired, turnPhrase) + notes);
    }

    /// <summary>
    /// What the touchdown sentence adds about THIS landing's constraints, each a leading-space
    /// sentence or "": a planned land-and-hold-short (LAHSO, VATSIM gap analysis P5), a turn-pad
    /// exit whose only way off is a guided backtrack (LandingExitBacktrack), or an exit that leaves
    /// by turning sharply back (LandingExitDestination.RequiresTurnBack) — the pilot slows early
    /// for it rather than first hearing "Make a U-turn" at the turn point (VirtualPilot 2026-09-18).
    /// </summary>
    private string TouchdownNotes(Navigation.LandingExit exit, Navigation.LandingExit? backtrackVia)
    {
        string lahsoNote = _lahsoHold == null ? "" : $" LAHSO: hold short of runway {_lahsoHold.CrossingRunwayId}.";
        string backtrackNote = backtrackVia != null
            ? $" Backtrack required, to {BacktrackViaLabel(backtrackVia)}."
            : exit.RequiresTurnBack ? " Sharp turn back, slow down early." : "";
        return lahsoNote + backtrackNote;
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
    /// <param name="groundSpeedKts">Ground speed at touchdown. Used only to decide which milestones a
    /// runway-correction sentence retires.</param>
    /// <param name="correction">Set when the landing-exit plan was made for another runway or the
    /// other end; the touchdown sentence leads with it (Navigation.TouchdownCallout). Null for a
    /// landing on the planned runway, which keeps the pre-existing sentence.</param>
    public void BeginLandingRollout(
        Navigation.LandingExit exit,
        double runwayHeadingTrue,
        Database.Models.Runway runway,
        List<Navigation.LandingExit> allExits,
        double touchdownLat = 0,
        double touchdownLon = 0,
        double groundSpeedKts = 0,
        Navigation.TouchdownRunwayCorrection? correction = null,
        Navigation.LahsoHold? lahso = null)
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
                $"runwayHeadingTrue={runwayHeadingTrue:F2} allExits.Count={allExits.Count} " +
                $"allExits={DescribeExits(allExits)}");
            ResetOffPavementAlert();

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
            // A turn-pad exit whose only way off is back up the runway: plan the guided
            // backtrack now, while the exit list is still here (TaxiGuidanceManager.Backtrack).
            var backtrackVia = PlanBacktrack(exit, runway, allExits);
            ResetRolloutApproachLatches();
            _rolloutEarlyHandoffDone = false;
            _lastUndershootRetargetTime = DateTime.MinValue;
            // LAHSO (VATSIM gap analysis 2026-08-31, P5): land-and-hold-short
            // constraint for this rollout, null when none was planned.
            _lahsoHold = lahso;
            _lahso1500Announced = _lahso500Announced = _lahsoStopAnnounced = _lahsoPassedAnnounced = false;
            _rolloutUnroutableExitNodes.Clear();
            _rolloutCrossingDeclinedUtc = DateTime.MinValue;
            _rolloutCrossingDeclineAnnounced = false;
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
            _rolloutStoppedNoticeGiven = false;
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

            // Touchdown callout, and with a runway correction, the milestones it retires.
            AnnounceTouchdownCallout(exit, touchdownLat, touchdownLon, groundSpeedKts, correction,
                TouchdownNotes(exit, backtrackVia));
        }
    }

    /// <summary>
    /// Enters landing rollout with NO exit — the runway-end countdown — on the runway the aircraft
    /// is actually on, when a landing-exit plan was made for another runway (or the other end) and
    /// no usable exit exists on this one (LandingExitPlanner). No LoadRoute runs first, so this sets
    /// up everything the countdown and a following backtrack read: this airport's taxi graph, data
    /// provider and ICAO (FindBacktrackConnectionNode and Where Am I need them), and the steering
    /// tone started with the pilot's settings (EnterRunwayEndCountdown pauses it and
    /// UpdateBacktracking resumes it — Resume cannot revive a stopped tone). Speaks the correction
    /// sentence after the state change, folding any countdown milestone already due, then asks
    /// MainForm for the position stream.
    /// </summary>
    public void BeginRunwayEndCountdownRollout(
        Database.Models.Runway runway,
        TaxiGraph graph,
        IAirportDataProvider dataProvider,
        string icao,
        UserSettings settings,
        double touchdownLat,
        double touchdownLon,
        double groundSpeedKts,
        Navigation.TouchdownRunwayCorrection correction)
    {
        lock (_stateLock)
        {
            RolloutDiag($"BeginRunwayEndCountdownRollout: runway={runway.RunwayID} " +
                $"hdgTrue={runway.Heading:F2} len={runway.Length:F0} icao={icao} state={_state}");
            ResetOffPavementAlert();

            // Stamp only a NEW graph instance (see _graphGeneration): the same instance handed back
            // keeps the generation it was installed under.
            if (!ReferenceEquals(_graph, graph)) _graphGeneration = DatabaseGeneration;
            _graph = graph;
            _dataProvider = dataProvider;
            _icao = icao ?? "";

            StartRolloutSteeringTone(settings);
            _steeringTone.SetPulse(false);

            ResetLandingExitOutcomeFlags();
            _rolloutRunway = runway;
            _rolloutRunwayHeadingTrue = runway.Heading;
            _rolloutHandoffActive = false;
            _rolloutDiagFirstCallDone = false;
            _rolloutDiagLastPeriodic = DateTime.MinValue;

            // Owns the rest: _route/_rolloutExit clearing, no-exit mode, the countdown and approach
            // latches, the tone pause and the LandingRollout state.
            EnterRunwayEndCountdown();

            // Through RunwayFrame so a runway row with no recorded length still yields a real
            // distance (it falls back to the threshold-to-threshold distance). Reading the raw
            // column here produced a NEGATIVE number on those rows — and they are exactly the rows
            // that reach this method, because the exit finders skip them and leave no usable exit.
            double distToEndFt = Navigation.RunwayFrame.For(runway, touchdownLat)
                .DistanceToEnd(touchdownLat, touchdownLon) * METERS_TO_FEET;

            var rm = DistanceMilestones.RunwayEnd(); // far->near: [0]=1500ft/500m, [1]=500ft/150m, [2]=100ft/30m
            var retired = Navigation.TouchdownCallout.RetireRunwayEndCallouts(
                distToEndFt, groundSpeedKts, ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC,
                rm[0].TriggerMetres / DistanceFormatter.MetresPerFoot,
                rm[1].TriggerMetres / DistanceFormatter.MetresPerFoot,
                rm[2].TriggerMetres / DistanceFormatter.MetresPerFoot,
                ROLLOUT_TAXI_GS_KTS);
            if (retired.Retire1500) _rolloutEnd1500Announced = true;
            if (retired.Retire500) _rolloutEnd500Announced = true;
            if (retired.Retire100) _rolloutEnd100Announced = true;

            AnnounceInstruction(Navigation.TouchdownCallout.ComposeNoUsableExit(
                correction, (int)Math.Round(distToEndFt), retired));

            PositionStreamRequired?.Invoke(this, EventArgs.Empty);
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
    /// </summary>
    /// <param name="groundSpeedKts">See <see cref="BeginLandingRollout"/>.</param>
    /// <param name="correction">See <see cref="BeginLandingRollout"/>.</param>
    public void BeginLandingRolloutNoGraph(
        Navigation.LandingExit exit,
        double runwayHeadingTrue,
        Database.Models.Runway runway,
        List<Navigation.LandingExit> allExits,
        double touchdownLat,
        double touchdownLon,
        UserSettings settings,
        TaxiGraph? graph,
        IAirportDataProvider? dataProvider,
        string icao,
        double groundSpeedKts = 0,
        Navigation.TouchdownRunwayCorrection? correction = null,
        Navigation.LahsoHold? lahso = null)
    {
        lock (_stateLock)
        {
            ResetOffPavementAlert();

            // The caller (LandingExitPlanner.ActivateGuidance) supplies the graph, provider
            // and ICAO it just used for its own (failed) LoadRoute attempt directly, rather
            // than this method relying on a prior LoadRoute call having left them populated
            // as a side effect — a reachability refusal now restores _graph/_dataProvider/
            // _icao to their PREVIOUS values instead of leaving LoadRoute's failed attempt
            // in place, so those fields can be null (a fresh session) or another airport's
            // objects (a rejected recalc) by the time this runs.
            // Stamp only a NEW graph instance (see _graphGeneration): the same instance handed back
            // keeps the generation it was installed under.
            if (!ReferenceEquals(_graph, graph)) _graphGeneration = DatabaseGeneration;
            _graph = graph;
            _dataProvider = dataProvider;
            _icao = icao;

            // Guarantee the handoff-failure fallback's _route == null invariant.
            // LoadRoute's failure paths leave _route untouched, so a stale value
            // from a prior route (e.g., hand-flown departure that didn't call
            // StopGuidance) could survive into this NoGraph entry. Nulling here
            // ensures the !handoffRerouted && _route == null branch in
            // UpdateLandingRollout will fire if the eventual handoff re-route
            // also fails, instead of driving the steering tone against stale
            // departure-airport segments.
            _route = null;

            // Same class of leak, same reason. This entry point is only ever reached AFTER a
            // failed LoadRoute — which is precisely the path that skips LoadRoute's own
            // fresh-route reset block (it sits after every failure return). Without these,
            // landing 1's flags survive into landing 2 when no StopGuidance and no successful
            // LoadRoute intervene: landing 2's handoff captures a stale
            // _landingExitVacatedEarlyPlannedName, the reachability guard takes its != null
            // branch, and the pilot is told "You have left the runway short of Taxiway X"
            // naming a taxiway from a previous flight. ResetLandingExitOutcomeFlags is the
            // same reset LoadRoute performs.
            ResetLandingExitOutcomeFlags();

            // Precondition: the caller supplies graph, dataProvider and icao above; the
            // handoff re-route in UpdateLandingRollout reads the _graph/_dataProvider/_icao
            // fields those arguments were just assigned to directly. A null/empty argument
            // here means the caller had nothing to hand off with — log and proceed
            // (geometry-driven callouts and tone still work, but the handoff re-route will
            // fail until normal taxi guidance kicks in).
            if (_graph == null || _dataProvider == null || string.IsNullOrEmpty(_icao))
            {
                RolloutDiag($"BeginLandingRolloutNoGraph precondition not met: " +
                    $"_graph={(_graph == null ? "null" : "set")} " +
                    $"_dataProvider={(_dataProvider == null ? "null" : "set")} " +
                    $"_icao='{_icao}' — handoff re-route will not succeed.");
            }

            // Start the tone now — StartGuidance (which normally does this) was
            // never called because LoadRoute failed.
            StartRolloutSteeringTone(settings);

            // Populate rollout state (mirrors BeginLandingRollout, without the _route guard).
            _rolloutExit = exit;
            _isLandingExitRoute = true;
            _rolloutRunwayHeadingTrue = runwayHeadingTrue;
            _rolloutRunway = runway;
            _rolloutAllExits = allExits;
            // A turn-pad exit whose only way off is back up the runway: plan the guided
            // backtrack now, while the exit list is still here (TaxiGuidanceManager.Backtrack).
            var backtrackVia = PlanBacktrack(exit, runway, allExits);
            ResetRolloutApproachLatches();
            _rolloutEarlyHandoffDone = false;
            _lastUndershootRetargetTime = DateTime.MinValue;
            // LAHSO (VATSIM gap analysis 2026-08-31, P5): land-and-hold-short
            // constraint for this rollout, null when none was planned.
            _lahsoHold = lahso;
            _lahso1500Announced = _lahso500Announced = _lahsoStopAnnounced = _lahsoPassedAnnounced = false;
            _rolloutUnroutableExitNodes.Clear();
            _rolloutCrossingDeclinedUtc = DateTime.MinValue;
            _rolloutCrossingDeclineAnnounced = false;
            _rolloutNoExitMode = false;
            _rolloutHandoffActive = false;
            _rolloutEnd1500Announced = false;
            _rolloutEnd500Announced = false;
            _rolloutEnd100Announced = false;
            _rolloutStoppedNoticeGiven = false;
            _rolloutDiagFirstCallDone = false;
            _rolloutDiagLastPeriodic = DateTime.MinValue;
            _smoothedHeadingError = 0.0;
            _headingErrorInitialized = false;
            _steeringTone.SetPulse(false);

            SetState(TaxiGuidanceState.LandingRollout);

            RolloutDiag($"BeginLandingRolloutNoGraph: exit='{exit.TaxiwayName}' node={exit.NodeId} " +
                $"runway={runway.RunwayID} hdgTrue={runwayHeadingTrue:F2} allExits={allExits.Count}");

            // Touchdown callout — same composer as BeginLandingRollout.
            AnnounceTouchdownCallout(exit, touchdownLat, touchdownLon, groundSpeedKts, correction,
                TouchdownNotes(exit, backtrackVia));

            // No Taxiing transition ran on this path (LoadRoute failed), so nothing has started the
            // position stream: ask for it, or UpdateLandingRollout would never run.
            PositionStreamRequired?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// "Off pavement." while the aircraft is off every mapped runway and taxiway during the landing roll
    /// or the exit (Navigation.PavementMap / OffPavementAlert), and off the runway being landed on
    /// (<see cref="IsOnRolloutRunwayPavement"/>). Spoken with AnnounceImmediate directly —
    /// NOT AnnounceInstruction — so Ctrl+Y still replays the last guidance instruction. No direction word:
    /// the steering tone is the only direction authority. KMEM 36L 2026-09-26: ~13 s in the grass at
    /// 37-47 kt with nothing said.
    /// </summary>
    private void CheckOffPavement(double lat, double lon, double groundSpeedKts)
    {
        if (_graph == null) return;
        if (!ReferenceEquals(_pavementMapGraph, _graph))
        {
            _pavementMap = Navigation.PavementMap.Build(_graph);
            _pavementMapGraph = _graph;
        }
        bool onGround = OnGroundProvider?.Invoke() ?? true;
        bool off = Navigation.OffPavementAlert.IsOffPavement(
            onGround, IsOnRolloutRunwayPavement(lat, lon), _pavementMap!.IsOnMappedPavement(lat, lon));
        if (off != _offPavementLogged)
        {
            _offPavementLogged = off;
            if (off)
                RolloutDiag($"Off pavement: lat={lat:F6} lon={lon:F6} gs={groundSpeedKts:F1}kt state={_state}");
            else if (!onGround)
                RolloutDiag($"Off-pavement check idle, airborne: lat={lat:F6} lon={lon:F6} gs={groundSpeedKts:F1}kt");
            else
                RolloutDiag($"Back on pavement: lat={lat:F6} lon={lon:F6} gs={groundSpeedKts:F1}kt");
        }
        if (_offPavementAlert.Update(off, groundSpeedKts, MSFSBlindAssist.Utils.SimClock.UtcNow))
        {
            RolloutDiag("Off-pavement alert spoken");
            SpeakNow(Navigation.OffPavementAlert.Phrase);
        }
    }

    private void ResetOffPavementAlert()
    {
        _offPavementAlert.Reset();
        _offPavementLogged = false;
    }

    /// <summary>
    /// The runway being landed on always counts as pavement, from the runway TABLE's own geometry: laterally
    /// within half-width + <see cref="Navigation.PavementMap.RunwayMarginMetres"/> (<see
    /// cref="IsWithinRolloutRunwayLaterally"/> — the rollout's own line, its width fallback included) and
    /// along-track within the runway's length (<see cref="IsWithinRunwayLength"/>). The pavement map knows a
    /// runway only through a centerline paired from its two start rows, which a runway can lack: KDEN 07/25
    /// has no start row for 07, so the map called that whole runway grass, and a simulated centerline landing
    /// roll on 25 drew "Off pavement." 4 s after touchdown, at 122 kt. _rolloutRunway is set at all three
    /// rollout entries, kept through the handoff, retargets and the countdown, and cleared only by
    /// StopGuidance — so it covers the exit too.
    /// </summary>
    private bool IsOnRolloutRunwayPavement(double lat, double lon)
        => _rolloutRunway != null
           && Navigation.RolloutExitGate.IsWithinRunwayPavementLaterally(
                  AbsLateralFromRunwayMeters(lat, lon, _rolloutRunway.StartLat, _rolloutRunway.StartLon,
                      _rolloutRunwayHeadingTrue),
                  _rolloutRunway.Width, _rolloutRunway.Surface)
           && IsWithinRunwayLength(_rolloutRunway, _rolloutRunwayHeadingTrue, lat, lon);

    /// <summary>
    /// The along-track half of <see cref="IsOnRolloutRunwayPavement"/>: from
    /// <see cref="Navigation.PavementMap.RunwayMarginMetres"/> before <paramref name="runway"/>'s start to the
    /// same margin past its length, measured with <see cref="SignedAlongRunwayMeters"/> (the companion of the
    /// lateral projection IsWithinRolloutRunwayLaterally uses). The length is <see cref="Navigation.RunwayFrame"/>'s,
    /// which falls back to the threshold-to-threshold distance on a row whose length is 0 — the rows that reach
    /// the runway-end countdown.
    /// </summary>
    internal static bool IsWithinRunwayLength(
        Database.Models.Runway runway, double runwayHeadingTrue, double lat, double lon)
    {
        // Along-track by SignedAlongRunwayMeters - the projection (111,132 m per degree) the lateral half,
        // IsWithinRolloutRunwayLaterally, uses - never RunwayFrame.Along (111,320), which would move this
        // boundary about 5 m at the far end of a 2.8 km runway; only the LENGTH comes from RunwayFrame.
        double alongM = SignedAlongRunwayMeters(lat, lon, runway.StartLat, runway.StartLon, runwayHeadingTrue);
        double lengthM = Navigation.RunwayFrame.For(runway, lat).LengthM;
        return alongM >= -Navigation.PavementMap.RunwayMarginMetres
            && alongM <= lengthM + Navigation.PavementMap.RunwayMarginMetres;
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
    ///   speed-aware. Since 2026-09 the line is the exit's own
    ///   RolloutExitGate.SlowDownAboveKts: 30 kt for a sharp or end-of-runway
    ///   exit, 60 kt for a rapid one.)
    /// • <b>~150 ft from exit</b> — "Turn {left/right} now, taxiway {name}."
    ///   Direction is computed from aircraft heading vs bearing-to-exit so
    ///   it matches what the pilot needs to do regardless of which side of
    ///   the runway the exit sits on. Since 2026-09 never when too fast for
    ///   the exit (RolloutExitGate.IsTooFastToTurn): the rollout retargets to
    ///   the next exit far enough ahead instead, or with none says
    ///   "Taxiway {name}, too fast to turn. Slow down."
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

        // Before the countdown branch: a LAHSO still stands after the last usable exit is missed,
        // which is exactly when the rollout falls into the runway-end countdown (PR #324 review).
        UpdateLahsoCountdown(lat, lon);

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

        // Heading deviation from the runway centreline, signed, POSITIVE = RIGHT.
        // The SIGN is load-bearing: a deviation away from the exit's own side is drift,
        // not the exit turn (KSEA 34L 2026-08-21). hdgDeltaAbs is still what the lateral
        // and overshoot gates below want.
        double hdgDelta = NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue);
        double hdgDeltaAbs = Math.Abs(hdgDelta);

        // Compute along-runway projection up front — the distance/handoff gates
        // and the overshoot detector below all need it. Positive means the
        // aircraft has moved past the exit in the runway heading direction;
        // negative means still upfield.
        // Measured to the TURN POINT, not the junction node: where a scenery draws the
        // exit taxiway along the centreline first, the junction is short of the real
        // turn (LROP 08R D, 147 ft) and every cue keyed to it fired early.
        double signedAlongPastM = SignedAlongRunwayMeters(
            lat, lon,
            _rolloutExit.Latitude, _rolloutExit.Longitude,
            _rolloutRunwayHeadingTrue);
        double signedAlongPastFt = signedAlongPastM * METERS_TO_FEET - _rolloutExit.TurnPointOffsetFeet;

        // Distance from current position to the chosen exit (feet) — ALONG-TRACK,
        // not straight-line. Exit nodes (especially HS/IHS hold-short markers) sit
        // up to half-width + 15 m (~130-148 ft) off the centerline (the same node
        // offset the lateral measurement below already corrects for — EIDW N4:
        // ~40 m). A straight-line distance to such a node keeps the lateral offset
        // as a floor, so every distance-gated event fired LATE: with a 131 ft
        // offset the 150 ft "turn now" didn't trigger until ~73 ft along-track
        // (the 4.4 s lead at 20 kt collapsed to ~1.7 s), and on a runway wider
        // than ~61 m with a maximally offset node it could never trigger at all —
        // no turn instruction ever spoken. The along-track projection reads 0 at
        // abeam whatever the node's lateral offset, and equals the straight-line
        // value whenever the node IS on the centerline, so well-placed nodes
        // behave identically. |Euclidean| ≥ |along-track| always, so every gate
        // fires at-or-earlier than before, never later.
        double distToExitFeet = Math.Abs(signedAlongPastFt);

        bool atTaxiSpeed = groundSpeedKts < ROLLOUT_TAXI_GS_KTS;
        // Diagnostic only since the handoff gate moved to the turn point — kept in the
        // rollout log lines because "was it near the exit when X happened" is the first
        // question asked of any post-flight rollout trace.
        bool nearExit = distToExitFeet < ROLLOUT_NEAR_EXIT_FT;
        bool pastExit = signedAlongPastFt > 0.0;
        // Relative bearing of the chosen exit from the runway heading, same sign convention.
        // The decoder owns the ExitBearingTrue == 0.0 "unknown" sentinel — see its doc for
        // why the bare subtraction fabricates a side instead of degrading.
        double exitRelBearingDeg = Navigation.RolloutExitGate.ExitRelativeBearingDeg(
            _rolloutExit.ExitBearingTrue, _rolloutRunwayHeadingTrue);

        // Speed-gated: above ROLLOUT_TURN_MAX_GS_KTS a heading deviation is touchdown yaw /
        // crab alignment, not a deliberate runway exit turn. Direction- and proximity-gated
        // since 2026-08: see Navigation/RolloutExitGate.IsExitTurnBegun; the proximity window
        // is the targeted exit's own (TurnWindowFeetFor) since 2026-09.
        double turnWindowFeet = RolloutExitTurnWindowFeet();
        bool turnBegun = Navigation.RolloutExitGate.IsExitTurnBegun(
            hdgDelta, groundSpeedKts, distToExitFeet, pastExit, exitRelBearingDeg,
            turnWindowFeet);
        // Effectively stopped before reaching the exit — e.g. pilot braked
        // hard after an undershoot retarget left the exit 500+ ft away.
        // The atTaxiSpeed&&nearExit gate intentionally doesn't fire this far
        // out (it prevents premature tone on long runways), but a fully stopped
        // aircraft needs to continue as Taxiing so they can taxi to the exit.
        // pastExit guard: let the overshoot detector handle the past-exit case.
        // Distance gate (ROLLOUT_STOPPED_HANDOFF_MAX_DIST_FT): only hand off
        // when the exit is close enough that the graph route to it is a short
        // forward hop. A stop FAR short of the exit must NOT hand off — the
        // graph has no along-runway edges, so the re-route leaves the runway
        // through the nearest connector, which can be a backward-peeling exit
        // (EDDB 24L: stop at 1,108 m with M2 at 3,912 m → 3.2 km route
        // backward via the 130° M7 diagonal). The stopped-short block below
        // owns that case instead.
        bool trulyStopped = groundSpeedKts < ROLLOUT_NO_EXIT_STOPPED_GS_KTS && !pastExit
                            && distToExitFeet <= ROLLOUT_STOPPED_HANDOFF_MAX_DIST_FT;

        // DIAGNOSTIC: periodic snapshot (every ~3s) of rollout state.
        // Captures the moment the per-frame loop is or isn't seeing the
        // distance threshold approach.
        if ((MSFSBlindAssist.Utils.SimClock.UtcNow - _rolloutDiagLastPeriodic).TotalSeconds >= 3.0)
        {
            _rolloutDiagLastPeriodic = MSFSBlindAssist.Utils.SimClock.UtcNow;
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
        // halfRunwayWidthFt is no longer a gate threshold — the lateral trigger below
        // uses the shared IsWithinRolloutRunwayLaterally predicate instead. It is kept
        // solely because the overshoot diagnostic further down reports it.
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
        //
        // (b) is bounded to ROLLOUT_NEAR_EXIT_FT: with no bound, drifting off the
        // runway EDGE anywhere short of the exit read as "took the exit". YPPH 21
        // (live 2026-09-18): 13° off heading and 40 m left of centre on the grass,
        // 2,367 ft short of C11 — the handoff routed from C11's junction 700 m ahead
        // and the tone sent the pilot back across the runway. Off the edge far from
        // the exit the pilot stays in LandingRollout, whose tone steers back to the
        // centreline; turnBegun (15°) still hands off a deliberate turn anywhere. On TAXI
        // pavement (a shallow earlier taxiway taken deliberately) the old handoff still
        // applies — the bound only changes what happens to an aircraft on the grass.
        // The lateral term is the SHARED predicate, not a local threshold. It used to be
        // `lateralFromCenterlineFt >= halfRunwayWidthFt + 30.0` (9.144 m), which sat 0.856 m
        // inside IsWithinRolloutRunwayLaterally's 10 m margin — so the handoff fired on a
        // frame that every downstream guard still read as ON the runway, skipping the
        // early-vacate retarget and passing the reachability guard unconditionally
        // (PR #204 review). The trigger now moves 0.856 m later, the conservative direction.
        bool exitedLaterally = !IsWithinRolloutRunwayLaterally(lat, lon)
                               && (distToExitFeet <= 250.0
                                   || (hdgDeltaAbs >= 8.0
                                       && (distToExitFeet <= ROLLOUT_NEAR_EXIT_FT
                                           || IsOnTaxiPavement(lat, lon)))
                                   || pastExit);

        // Heading-aligned-with-exit handoff for shallow RETs whose angle is
        // below ROLLOUT_TURN_BEGAN_HDG_DEG (15°), so turnBegun never fires.
        // Fires when the aircraft heading is within 5° of ExitBearingTrue AND
        // has deviated at least 70% of how steeply the exit leaves its node
        // (RolloutExitGate.IsAlignedWithExit, LandingExit.DivergenceAngleDegrees).
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
        bool alignedWithExit = Navigation.RolloutExitGate.IsAlignedWithExit(
            headingTrue, _rolloutExit.ExitBearingTrue, _rolloutExit.ExitAngleDegrees,
            _rolloutExit.DivergenceAngleDegrees, hdgDeltaAbs, groundSpeedKts, pastExit);

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
        // Normal/End exits keep the speed-gate but only from the TURN POINT
        // (ROLLOUT_TURN_NOW_FT), not from ROLLOUT_NEAR_EXIT_FT.
        //
        // The claim this gate used to carry — "their hard turn is guided fine by the
        // post-handoff re-route" — is false, and HESH 04L (live 2026-08-26) is the
        // measurement. On a normal decelerating landing the aircraft is already below
        // ROLLOUT_TAXI_GS_KTS a long way out, so at 500 ft this fired while dead-centre
        // on the runway with hdgDelta 0.4°. Everything the KDTW 22L fix built then
        // never ran, because all of it lives in UpdateLandingRollout and the state had
        // left LandingRollout:
        //   - the Normal-exit tone that holds RUNWAY HEADING (silent while tracking
        //     straight) until the turn point — instead UpdatePosition's
        //     _rolloutHandoffActive branch snapped the tone to the exit SEGMENT bearing,
        //     a full 90° hard pan 152 m before the junction;
        //   - the speed-scaled "Left turn ahead" prep call (330 ft at 30 kt);
        //   - the 150 ft "Turn left now, taxiway X" verbal.
        // The pilot got "500 feet. Slow down." and then nothing but a saturated pan,
        // followed it, began the turn 135 m (443 ft) early and cut the corner across
        // the fillet — passing no closer than 59 m to the junction node. Same failure
        // as KDTW 22L Y3, reached through the Taxiing door instead of the rollout one.
        //
        // The gate is _rolloutTurnNowAnnounced, NOT a bare distance test against
        // ROLLOUT_TURN_NOW_FT: this handoff block runs BEFORE the turn-now callout later
        // in the same method and RETURNS, so a distance gate on the same 150 ft would
        // swallow "Turn left now, taxiway X" on the very frame it was due — rebuilding
        // the silence this change exists to remove, one frame later. Waiting for the
        // latch means the pilot has been TOLD to turn before guidance changes hands.
        //
        // Nothing is stranded by holding LandingRollout longer: turnBegun (15°) hands off
        // the instant the pilot commits, trulyStopped (< 3 kt) covers an aircraft that
        // stops short, and the overshoot detector covers a miss.
        // Closed for an exit declared too fast at its turn point (_rolloutTooFastNoExit): slowing down
        // as told must not re-offer it. The handoffs that follow what the pilot does stay open.
        bool speedNearExitHandoff = atTaxiSpeed && !pastExit
                                    && _rolloutTurnNowAnnounced
                                    && _rolloutExit.ExitType != "High-speed"
                                    && !_rolloutTooFastNoExit;

        // Retry floor after a runway-re-crossing decline. Every other exit from this block
        // leaves LandingRollout or stops guidance, so it is one-shot and needs no latch;
        // the crossing decline below is the only path that stays and returns, which makes
        // the block re-entrant on the very next SIM_FRAME. See
        // ROLLOUT_CROSSING_RETRY_FLOOR_SEC for why that has to be bounded — and why this
        // is a floor rather than a latch. DateTime.MinValue (no decline this rollout)
        // makes the term trivially true, so the normal path is unchanged.
        bool crossingRetryFloorElapsed =
            (MSFSBlindAssist.Utils.SimClock.UtcNow - _rolloutCrossingDeclinedUtc).TotalSeconds
                >= ROLLOUT_CROSSING_RETRY_FLOOR_SEC;

        // The chosen exit is a turn pad whose only way off is back up the runway: once
        // the exit to backtrack to is behind us and the pilot turns, stops or reaches the
        // turn point, guide the backtrack instead of routing into the pad.
        // Keyed on the turn-point DISTANCE, not the turn-now latch, so the pad's "Turn left
        // now, taxiway D" is never spoken a moment before "Turn around".
        if (TryStartPlannedBacktrack(lat, lon, groundSpeedKts,
                turnBegun, atTaxiSpeed && distToExitFeet <= ROLLOUT_TURN_NOW_FT, pastExit, atTaxiSpeed))
            return;

        if ((turnBegun || exitedLaterally || alignedWithExit || speedNearExitHandoff || trulyStopped)
            && crossingRetryFloorElapsed)
        {
            RolloutDiag($"UpdateLandingRollout HANDOFF -> Taxiing: " +
                $"turnBegun={turnBegun} exitedLaterally={exitedLaterally} alignedWithExit={alignedWithExit} " +
                $"exitBrgErr={exitBrgErr:F1}deg lateral={lateralFromCenterlineFt:F0}ft " +
                $"distToExit={distToExitFeet:F0}ft hdgDelta={hdgDeltaAbs:F1}deg " +
                $"atTaxiSpeed={atTaxiSpeed} nearExit={nearExit} pastExit={pastExit} trulyStopped={trulyStopped}");

            // Arm post-handoff overshoot monitor so UpdatePosition can detect
            // a missed exit while in Taxiing state.
            _rolloutHandoffActive = true;

            // Whether the early-vacate branch below repointed _rolloutExit at a substitute.
            // If it did, the touchdown route in _route targets the exit the pilot has just
            // left short of, so it can never be resumed as a fallback.
            bool earlyVacateSwapped = false;
            // The substitute-exit callout, held until the re-route AND the reachability guard
            // below have both accepted. AnnounceInstruction is AnnounceImmediate, and BOTH
            // conclude paths further down announce through HandleArrival the same way, so
            // speaking it at the swap meant a failed re-route or a refused route cut it off
            // mid-sentence — the pilot heard a fragment of a claim the same frame withdrew.
            string? pendingVacateAnnouncement = null;

            // Early-vacate retarget. Entered only when the aircraft is BOTH laterally off
            // the runway AND vacated away from the planned exit. Both gates are load-bearing: the
            // lateral gate is what "vacated" physically means, so a trulyStopped handoff on
            // the centreline 2,000 ft short of the exit keeps the planned exit and taxis to
            // it; and the distance gate is now ALONG-TRACK (RolloutExitGate.
            // IsVacateAwayFromPlannedExit), not straight-line: measured along the runway and
            // read under the lateral conjunct above, an aircraft on its OWN exit's pavement
            // can be at most ~313 ft short of that exit's node. No spacing floor between
            // DISTINCT exits is claimed (see VacatedShortAlongTrackFeet). The old straight-line
            // TurnWindowFeet test alone left the band from 350 to 1,000 ft in which a vacate
            // onto a NEIGHBOURING exit re-routed to the planned one.
            // The 500 ft tightening rejected earlier does not apply here — it reasoned about a
            // turn begun ON the runway, which the lateral conjunct already excludes.
            bool offRunwayAtHandoff = !IsWithinRolloutRunwayLaterally(lat, lon);
            bool vacatedAwayFromPlannedExit = Navigation.RolloutExitGate.IsVacateAwayFromPlannedExit(
                pastExit, signedAlongPastFt, distToExitFeet);

            if (offRunwayAtHandoff && vacatedAwayFromPlannedExit && _rolloutExit != null)
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

                    // Capture the PLANNED exit's display name — same "Taxiway X" format
                    // LoadRoute gives _route.DestinationName — BEFORE the swap below. If the
                    // substitute's handoff route later fails the reachability guard,
                    // HandleArrival must still name the exit the pilot was steered toward and
                    // missed, not the substitute they ended up standing on.
                    string plannedName = _rolloutExit.TaxiwayName.Length > 0
                        ? $"Taxiway {_rolloutExit.TaxiwayName}"
                        : "exit taxiway";
                    _landingExitVacatedEarlyPlannedName = plannedName;

                    // Every comparable retarget in this file announces the swap
                    // (RetargetLandingExit, the undershoot retarget below) — silently
                    // repointing the tone at a different exit left the pilot's first notice
                    // of it at the arrival callout, with no warning the tone had moved.
                    // Both clauses use the same lowercase "taxiway X" mid-sentence form
                    // RetargetLandingExit's "Missed taxiway X. Retargeting taxiway Y…" uses —
                    // plannedName stays capitalized ("Taxiway X") because that is the format
                    // _landingExitVacatedEarlyPlannedName must carry to match _route.DestinationName
                    // for the HandleArrival closures above.
                    string plannedNameSpoken = _rolloutExit.TaxiwayName.Length > 0
                        ? $"taxiway {_rolloutExit.TaxiwayName}"
                        : "exit taxiway";
                    string vacatedName = string.IsNullOrEmpty(vacatedAt.TaxiwayName)
                        ? "exit taxiway"
                        : $"taxiway {vacatedAt.TaxiwayName}";
                    // Held, not spoken: see pendingVacateAnnouncement above. It is announced
                    // only on the path that actually ends with guidance following the
                    // substitute — every conclude path below returns without it, and its own
                    // closure is then the single thing the pilot hears.
                    pendingVacateAnnouncement =
                        $"Left the runway short of {plannedNameSpoken}. Now following {vacatedName}.";

                    // Swap the exit so the destination, the post-handoff overshoot monitor
                    // and the arrival callout all name the taxiway the pilot is on.
                    _rolloutExit = vacatedAt;
                    earlyVacateSwapped = true;
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
            // A side-effect of always re-routing: the touchdown route is replaced with a clean
            // 1-2 segment route from the handoff position. (The old per-edge crossing pass used to
            // leave a false "hold short of runway X" tag on the touchdown route, whose destination
            // sits on the runway.)
            bool handoffRerouted = false;
            if (_rolloutExit != null && _dataProvider != null && _graph != null)
            {
                int rerouteDest = ResolveExitHandoffDestination(out string rerouteDestSrc);
                // LoadRoute's fresh-route reset puts this back to true; the verdict
                // that matters was just computed by the resolver above, so carry it
                // across (see the re-set below).
                bool offPavementAtHandoff = _landingExitOffPavement;
                // Same carry for the vacated-early planned-exit name: when the early-vacate
                // branch above matched a substitute exit, it captured the PLANNED exit's
                // name into this field (see the comment there) precisely so a later
                // reachability-guard failure can still name it to the pilot. LoadRoute's
                // fresh-route reset nulls the field before that guard runs, which would
                // silently defeat the capture — carry it across the same way.
                string? vacatedEarlyPlannedNameAtHandoff = _landingExitVacatedEarlyPlannedName;
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
                    announceSummary: false,
                    // Anchor the route START on the chosen exit's own taxiway —
                    // the same anchor TryEarlyExitHandoff passes, for the same
                    // reason. Two of this block's triggers (speedNearExitHandoff,
                    // trulyStopped) fire with the aircraft still ON the runway at
                    // runway heading, where the un-anchored nearest-in-cone snap
                    // is weakest: stopped abeam a NEIGHBOURING exit, the start
                    // snapped to that neighbour and A* routed a hairpin up the
                    // wrong stub to reach the target (the EIDW 28L S6-via-S5
                    // shape, reached through this door instead of the early one).
                    // Unnamed exit → null keeps the legacy snap.
                    // Only NEAR the exit (the stopped-handoff hop distance): a handoff far
                    // short of it (turnBegun anywhere on the runway) anchored the route on
                    // the exit's junction hundreds of metres ahead, so the route began out
                    // of reach and the tone chased it across the pavement (YPPH 21, C11
                    // anchored 700 m ahead, 2026-09-18). Far out, the legacy snap starts
                    // the route where the aircraft actually is.
                    startTaxiwayName: _rolloutExit.TaxiwayName.Length > 0
                                      && distToExitFeet <= ROLLOUT_STOPPED_HANDOFF_MAX_DIST_FT
                        ? _rolloutExit.TaxiwayName : null,
                    exitPathNodeIds: _rolloutExit.TaxiwayName.Length > 0
                                     && distToExitFeet <= ROLLOUT_STOPPED_HANDOFF_MAX_DIST_FT
                        ? GetRolloutExitPathNodeIds() : null,
                    // A log label only (phase=touchdown). Whether the route may start held is the
                    // pass's own call, from the aircraft's position: none while it stands within
                    // the clear margin of any runway, none once it has rolled 10 m along the route.
                    landingRolloutRoute: true);
                handoffRerouted = rerouteErr == null;
                if (handoffRerouted)
                {
                    StripVacateHoldShortsOnLandedRunway(_route);
                    // LoadRoute clears _isLandingExitRoute; re-set so HandleArrival
                    // fires the landing-exit-specific "Hold position. Open the taxi
                    // planner..." message instead of the generic "Destination reached".
                    _isLandingExitRoute = true;
                    // Same for the off-pavement verdict: this route's handoff already
                    // happened, so LoadRoute's "a new route re-decides this" reset would
                    // silence the still-on-runway warning on exactly the airports that
                    // need it (EVRA, EHAM 36C/W8, EFHK 04L/WZ).
                    _landingExitOffPavement = offPavementAtHandoff;
                    // And for the planned-exit name — restored so the reachability guard
                    // below, if it fires, has it available for HandleArrival.
                    _landingExitVacatedEarlyPlannedName = vacatedEarlyPlannedNameAtHandoff;
                }
                if (rerouteErr == null)
                    RolloutDiag($"Handoff re-route OK: lat={lat:F6} lon={lon:F6} → {rerouteDestSrc}={rerouteDest}");
                else
                    RolloutDiag($"Handoff re-route failed ({rerouteErr}), continuing with original route");
            }

            // An early-vacate swap with no route to show for it must CONCLUDE, never fall
            // through to the re-anchor below: _route is still the touchdown route, whose
            // destination is the PLANNED exit the pilot has just been told they left short
            // of. Resuming the tone on it would steer them back toward the exit they
            // skipped, seconds after the substitute announcement said otherwise — and with
            // no runway edges in the graph, that is the 1,678 m KSEA long-way-round.
            if (earlyVacateSwapped && !handoffRerouted)
            {
                RolloutDiag("Early vacate: substitute exit re-route failed — concluding " +
                    "rather than resuming on the route to the planned exit");
                _landingExitVacatedEarly = true;
                _rolloutHandoffActive = false;
                SetState(TaxiGuidanceState.Taxiing);
                HandleArrival();
                return;
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

                // Recompose the route-start turn cue against the cursor just re-anchored.
                // The surviving cue belongs to the TOUCHDOWN route: it was composed rolling
                // straight down the runway, so its heading error was ~0 and it is null — yet
                // the segment now under the cursor can sit behind the aircraft, which is
                // exactly the turnaround the cue exists to speak. Left alone, the pilot gets
                // the hard pan with no words (or, if the touchdown angle happened to clear
                // the threshold, a cue composed thousands of feet ago against a different
                // cursor, which can name the wrong direction). _initialTurnCueAnnounced is
                // still false here — the Taxiing branch never ran during the rollout — but it
                // is cleared explicitly so this does not depend on that remaining true.
                _initialTurnCueAnnounced = false;
                ComposeInitialTurnCue(lat, lon, headingTrue);
                if (LastRouteInitialTurnCue != null)
                    RolloutDiag($"Handoff re-anchor turn cue: {LastRouteInitialTurnCue}");
            }

            // Reachability guard: never hand the steering tone a target the aircraft is not
            // already essentially on. KSEA 34L 2026-08-21: the first segment lay 53.9 m of
            // cross-track away with the aircraft 17.8 m outside the runway edge, and the
            // tone — silent until that instant — panned 79° right.
            //
            // Placed AFTER the re-anchor and read at _currentSegmentIndex so it tests the
            // segment the tone is ACTUALLY about to steer at. It used to sit inside the
            // re-route block gated on handoffRerouted, which left the fallback path — the
            // one that resumes on the touchdown route — with no guard at all, while
            // [ROL-29] requires it to gate EVERY landing-exit handoff re-route. For a
            // successful re-route the cursor is 0, so that case is unchanged.
            if (_route != null && _currentSegmentIndex >= 0
                && _currentSegmentIndex < _route.Segments.Count)
            {
                var firstSeg = _route.Segments[_currentSegmentIndex];
                double crossToFirstM = TaxiGraph.PerpendicularDistanceMetersStatic(
                    lat, lon,
                    firstSeg.FromNode.Latitude, firstSeg.FromNode.Longitude,
                    firstSeg.ToNode.Latitude, firstSeg.ToNode.Longitude);

                if (!Navigation.RolloutExitGate.IsHandoffRouteReachable(
                        offRunwayAtHandoff, crossToFirstM, firstSeg.PathWidth))
                {
                    RolloutDiag($"Handoff route unreachable: {crossToFirstM:F0} m from segment " +
                        $"{_currentSegmentIndex} (width {firstSeg.PathWidth:F0} ft) with the " +
                        $"aircraft off the runway — concluding rather than steering across it");
                    // Off the runway by IsHandoffRouteReachable's own early return, so the
                    // off-runway closure is the right one.
                    ConcludeLandingExitOffRunway();
                    return;
                }

                // Second WHOLE-ROUTE guard, on the same footing as the reachability guard
                // above: after the re-anchor, judged from _currentSegmentIndex — the segment
                // the tone is actually about to steer at.
                //
                // KATL 26R 2026-08-27: the aircraft rolled past B1 without turning, the
                // overshoot monitor retargeted to exit A on the NORTH side, and A* — which
                // has no runway edges to route along — built a 427 m loop: off at B1 to the
                // south, west along B, then back north across the 08L threshold at 22 kt.
                // The reachability guard passed it, and could not have caught it: it
                // measures only the FIRST segment's cross-track, and B1 started right at
                // the aircraft.
                if (HandoffRouteReCrossesLandingRunway(lat, lon))
                {
                    string rwyName = _rolloutRunway?.RunwayID ?? "the runway";

                    // Still ON the runway and SHORT of the exit → the exit is AHEAD and
                    // reachable by simply continuing. Decline the handoff and keep flying the
                    // rollout: state stays LandingRollout, so the tone remains rollout-driven
                    // (RolloutExitGate.SelectToneMode still owns it) and the pilot rolls to the
                    // turnoff. That is the whole distinction — turning off onto a taxiway on the
                    // far side is a vacate, driving across the pavement to reach it is not.
                    //
                    // "Rollout-driven" is NOT "audible". Naming SelectToneMode's above-50-kt
                    // Silent mode here would be naming the one silent state that cannot apply,
                    // since every trigger that reaches this branch is a slow-speed one. The two
                    // that CAN leave a stopped aircraft with no sound are: the turn-window Silent
                    // (from 300 ft out to the targeted exit's own window, RolloutExitTurnWindowFeet(),
                    // at most 1,000 ft; ≥ DriftToneSilentDeg of deviation toward a known exit
                    // side), and DriftCorrection itself, which is a heading cue and therefore
                    // zero volume for an aircraft aligned with the runway. Beyond
                    // ExitToneArmFeet (300 ft) one of those two always owns the tone. That is
                    // why the decline SPEAKS once — see the announcement below.
                    //
                    // !offRunwayAtHandoff is the load-bearing second conjunct, not a belt-and-
                    // braces extra (PR review, 2026-08-27). "Continue and the exit comes to
                    // you" is only true of an aircraft still ON the pavement, and three separate
                    // defects live in the off-runway half of this branch:
                    //   • exitedLaterally is DEFINED as !IsWithinRolloutRunwayLaterally(...)
                    //     plus a proximity/heading term, so an exitedLaterally-triggered decline
                    //     is always off-runway. There the overshoot detector cannot fire
                    //     (stillOnRunway is false) and trulyStopped / speedNearExitHandoff hold
                    //     !pastExit true, so the decline repeats at the retry floor forever with
                    //     no closure and no route.
                    //   • The early-vacate branch above is itself gated on offRunwayAtHandoff.
                    //     Reaching a decline after it swapped _rolloutExit would return without
                    //     speaking pendingVacateAnnouncement and without restoring the planned
                    //     exit — silently repointing the tone at a different exit, the exact
                    //     thing that announcement exists to prevent.
                    //   • The conclude branch below is the only other way out, and its
                    //     _landingExitRouteUnreachable closure says "Stop and hold position".
                    // With this conjunct the off-runway cases all fall through to conclude,
                    // where "stop and hold" is safe and a closure is always spoken.
                    if (signedAlongPastFt < 0.0 && !offRunwayAtHandoff)
                    {
                        RolloutDiag($"Handoff route re-crosses runway {rwyName} and the exit is still " +
                            $"{-signedAlongPastFt:F0} ft ahead — declining the handoff, staying in " +
                            $"LandingRollout and steering to the exit");
                        // Stamped so the block is not re-entered on the very next frame:
                        // see ROLLOUT_CROSSING_RETRY_FLOOR_SEC. This is the only path out
                        // of this block that stays in LandingRollout, so it is the only one
                        // that needs it.
                        _rolloutCrossingDeclinedUtc = MSFSBlindAssist.Utils.SimClock.UtcNow;
                        // Restore the pre-handoff value: the top of this block armed the
                        // post-handoff overshoot monitor on the assumption it ends in Taxiing.
                        _rolloutHandoffActive = false;

                        // Say the decline's own premise, ONCE: the exit is ahead, keep rolling
                        // to it. Without this a pilot who brakes to a stop short of the exit
                        // sits in this loop indefinitely — no tone (trulyStopped carries no
                        // distance gate, and beyond ExitToneArmFeet a stopped aligned aircraft
                        // gets turn-window Silent or a zero-volume DriftCorrection) and no
                        // words, stationary on an active runway with nothing telling them what
                        // to do.
                        //
                        // Fired on the FIRST decline, not on a later "stopped" test. The
                        // triggers that can reach this branch (exitedLaterally and
                        // alignedWithExit are excluded by the two conjuncts above; turnBegun,
                        // speedNearExitHandoff and trulyStopped are what is left) are near the
                        // exit, but NOT all slow: RolloutExitGate.IsExitTurnBegun permits up to
                        // TurnMaxGroundSpeedKts (90 kt), well above the 30 kt this file treats
                        // as taxi speed, so turnBegun can reach this branch at rollout speed —
                        // corrected 2026-08-27, an earlier version of this comment claimed
                        // otherwise. That does not change the outcome: the sentence is still
                        // accurate and useful at 90 kt (the exit genuinely is still ahead), and
                        // it is one-shot, so speaking it early costs nothing even on a fast
                        // frame. Waiting for trulyStopped would instead leave the CREEPING case
                        // silent: a speedNearExitHandoff decline reaches here anywhere inside
                        // ROLLOUT_NEAR_EXIT_FT (500 ft), and the part of that beyond 300 ft overlaps
                        // the turn-window Silent band as far as the exit's own window reaches (up to
                        // all of 300–500 ft) — so an aircraft crawling at 4 kt, above
                        // ROLLOUT_NO_EXIT_STOPPED_GS_KTS and therefore never "stopped", would
                        // hear nothing at all. That is precisely the state this exists to close.
                        // One-shot per rollout, so the cost of speaking early is a single
                        // sentence either way.
                        //
                        // Latched, NOT floored: the decline repeats at ~1 Hz and a sentence a
                        // second would be worse than silence. AnnounceInstruction, the same
                        // call the neighbouring rollout callouts use — no new speech path.
                        //
                        // ONE utterance, composed here, NOT two announcements racing (PR
                        // review, 2026-08-27). AnnounceInstruction is AnnounceImmediate and
                        // this branch RETURNS before the approach/turn-now callout block
                        // below, so that block's latches are never set. On the very next
                        // frame (~16 ms) the crossing retry floor suppresses the handoff
                        // block, execution reaches the callouts, and one of them fires its own
                        // AnnounceImmediate — truncating a sentence that is one-shot and
                        // therefore unrecoverable (Ctrl+Y replays the later callout, not
                        // this). Structural, not coincidental: speedNearExitHandoff requires
                        // distToExitFeet < ROLLOUT_NEAR_EXIT_FT (500 ft) and the 500 ft
                        // milestone triggers on that same boundary, so on any rollout already
                        // at taxi speed at 500 ft — the live 22 kt KATL trace included — both
                        // are true on ONE frame. Sixth instance of this codebase's
                        // two-announcements-stomp-each-other pattern; see
                        // RolloutRunwayReCrossing.ComposeDeclineUtterance for why the house
                        // remedy (one utterance) beats the two alternatives here.
                        // RolloutRunwayReCrossing.PlanDeclineSpeech: silent and unlatched inside the turn point
                        // at a speed the exit cannot be taken at, so the turn-now block's too-fast rule speaks
                        // on a following frame (the retry floor keeps the handoff block out of the way) and this
                        // sentence still speaks once the aircraft is slow enough.
                        var declinePlan = Navigation.RolloutRunwayReCrossing.PlanDeclineSpeech(
                            _rolloutTurnNowAnnounced, distToExitFeet, ROLLOUT_TURN_NOW_FT,
                            Navigation.RolloutExitGate.IsTooFastToTurn(groundSpeedKts, _rolloutExit!.ExitAngleDegrees));
                        if (!_rolloutCrossingDeclineAnnounced && !declinePlan.Speak)
                            RolloutDiag($"Crossing decline held silent: too fast for the turn point " +
                                $"(distToExit={distToExitFeet:F0}ft gs={groundSpeedKts:F1}kt)");
                        if (!_rolloutCrossingDeclineAnnounced && declinePlan.Speak)
                        {
                            _rolloutCrossingDeclineAnnounced = true;

                            // Every approach milestone this sentence supersedes is RETIRED
                            // rather than folded in: each renders as "{exit name}, {round
                            // number}", and the sentence about to be spoken gives the same
                            // name with a LIVE distance. Only what they uniquely add is
                            // folded — the 500 ft cue's "Slow down.", and (inside the turn
                            // window) the turn DIRECTION. Milestones still further ahead than
                            // the lead window are left armed and speak normally later, so the
                            // countdown a rolling pilot depends on survives the decline.
                            //
                            // _rolloutExit captured once: the compiler's null-state for the
                            // field is reset by the intervening calls, and
                            // UpdateLandingRollout's own null-exit guard has already run.
                            var declineExit = _rolloutExit!;
                            var xmDecline = DistanceMilestones.ExitApproach();
                            bool Supersedes(double triggerMetres) =>
                                Navigation.RolloutRunwayReCrossing.DeclineSupersedesCallout(
                                    distToExitFeet,
                                    triggerMetres / DistanceFormatter.MetresPerFoot,
                                    groundSpeedKts,
                                    ROLLOUT_DECLINE_CALLOUT_LEAD_SEC);

                            bool retire1500 = !_rolloutApproach1500Announced && Supersedes(xmDecline[0].TriggerMetres);
                            bool retire900  = !_rolloutApproach900Announced  && Supersedes(xmDecline[1].TriggerMetres);
                            bool retire500  = !_rolloutApproach500Announced  && Supersedes(xmDecline[2].TriggerMetres);

                            // Turn-now uses the STRICT inside test, no lead window: "now" is
                            // time-critical, and a speed-derived lead would speak it up to
                            // ~600 ft out at the 90 kt IsExitTurnBegun permits. The accepted
                            // cost is a moving decline between ROLLOUT_TURN_NOW_FT and roughly
                            // twice it, where turn-now comes due mid-sentence and truncates
                            // it. That is the one truncation worth having: "Turn left now,
                            // taxiway A." carries the exit name AND the more urgent
                            // instruction, so the pilot is never left silent or uninformed —
                            // which is the state this whole announcement exists to prevent.
                            bool retireTurnNow = declinePlan.FoldTurnNow;

                            bool slowDown = retire500
                                && groundSpeedKts > Navigation.RolloutExitGate.SlowDownAboveKts(
                                       declineExit.ExitAngleDegrees, declineExit.ExitType);

                            if (retire1500) _rolloutApproach1500Announced = true;
                            if (retire900) _rolloutApproach900Announced = true;
                            if (retire500) _rolloutApproach500Announced = true;
                            if (retireTurnNow)
                            {
                                _rolloutTurnNowAnnounced = true;
                                // The turn-now block's own side effect, reproduced because
                                // retiring it means that block will never run. Normal exits
                                // (50–110°): reset the heading-error smoother so the
                                // ExitBearingTrue-based tone starts with a sharp hard-pan.
                                if (declineExit.ExitType == "Normal"
                                    && Navigation.RolloutExitGate.IsPlausibleExitBearing(
                                           declineExit.ExitBearingTrue, _rolloutRunwayHeadingTrue))
                                    _headingErrorInitialized = false;
                            }

                            RolloutDiag($"Crossing-decline utterance: distToExit={distToExitFeet:F0}ft " +
                                $"gs={groundSpeedKts:F1}kt retire1500={retire1500} retire900={retire900} " +
                                $"retire500={retire500} retireTurnNow={retireTurnNow} slowDown={slowDown}");

                            string declineSentence = Navigation.RolloutRunwayReCrossing.ComposeDeclineUtterance(
                                declineExit.TaxiwayName,
                                distToExitFeet,
                                slowDown,
                                retireTurnNow ? ComposeExitTurnPhrase(lat, lon, headingTrue) : null);
                            // After "Taxiway X, too fast to turn. Slow down." (4.39 s) the pilot can have slowed
                            // below the line within it: queued, this follows the warning instead of cutting it off.
                            if (_rolloutTooFastNoExit) AnnounceQueuedInstruction(declineSentence);
                            else AnnounceInstruction(declineSentence);
                        }

                        SetState(TaxiGuidanceState.LandingRollout);
                        return;
                    }

                    // Everything else concludes: at or past the exit, or already off the
                    // runway. No route leaves this side without crossing the pavement, and
                    // neither of those states is fixed by continuing.
                    RolloutDiag($"Handoff route re-crosses runway {rwyName} — concluding rather " +
                        $"than driving back across it (signedAlongPast={signedAlongPastFt:F0} ft, " +
                        $"offRunway={offRunwayAtHandoff}); exits considered {DescribeExits(_rolloutAllExits)}");

                    // The closure splits on WHERE THE AIRCRAFT IS, because two of the three
                    // landing-exit closures order ahead of the still-on-runway one and both
                    // say "Stop and hold position". That wording is safe only off the pavement.
                    // The sibling reachability guard can only ever conclude off the runway
                    // (RolloutExitGate.IsHandoffRouteReachable early-returns true while the
                    // aircraft is on it), so its reason split assumed a precondition this guard
                    // does not share: turnBegun and alignedWithExit both reach here ON the
                    // pavement — the first seconds of a turnoff, and a shallow rapid-exit
                    // alignment past the exit node. Telling a blind pilot to stop and hold on
                    // an active runway is the hazard HandleArrival's own final branch exists to
                    // prevent.
                    // Off the pavement reads the planned-exit name restored above after
                    // LoadRoute's fresh-route reset blanked it; on it, the closure must not
                    // say "hold". Both sequences live on the two Conclude helpers.
                    if (offRunwayAtHandoff)
                        ConcludeLandingExitOffRunway();
                    else
                        ConcludeLandingExitOnRunway();
                    return;
                }
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

            // The substitute-exit callout, now that the re-route and the reachability guard
            // have both accepted and the tone really is about to follow the taxiway named.
            if (pendingVacateAnnouncement != null)
                AnnounceInstruction(pendingVacateAnnouncement);

            SetState(TaxiGuidanceState.Taxiing);
            _steeringTone.Resume();
            return;
        }

        // Full stop far short of the chosen exit — the case the trulyStopped
        // distance gate above excludes. Stay in LandingRollout: runway-heading
        // tone guidance and the whole normal flow (undershoot retarget to the
        // next forward exit, prep call, turn-now, vacate) remain armed, so the
        // pilot simply continues ahead when ready. Silence at a stop reads as
        // "system gave up" (user ruling), so say what's happening — once per
        // stop episode, re-armed once genuinely rolling again.
        if (groundSpeedKts < ROLLOUT_NO_EXIT_STOPPED_GS_KTS && !pastExit)
        {
            if (!_rolloutStoppedShortAnnounced)
            {
                _rolloutStoppedShortAnnounced = true;
                string stoppedExitName = _rolloutExit.TaxiwayName.Length > 0
                    ? $"Taxiway {_rolloutExit.TaxiwayName}"
                    : "The chosen exit";
                RolloutDiag($"STOPPED SHORT of exit: distToExit={distToExitFeet:F0}ft " +
                    $"gs={groundSpeedKts:F1}kt — holding LandingRollout");
                AnnounceInstruction(
                    $"Stopped on the runway. {stoppedExitName} is " +
                    $"{DistanceFormatter.FromFeet(distToExitFeet)} ahead. Continue ahead when ready.");
            }
        }
        else if (_rolloutStoppedShortAnnounced
                 && groundSpeedKts >= ROLLOUT_STOPPED_SHORT_REARM_GS_KTS)
        {
            _rolloutStoppedShortAnnounced = false;
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

        // Exit-type-aware margin — the same rule as the post-handoff monitor, read at
        // how steeply the exit leaves its node (RolloutExitGate.OvershootMarginFor).
        double overshootMargin = MissMarginFeet();
        // An exit declined as too fast at its turn point keeps that margin while the aircraft rolls - a
        // pilot who slowed and is turning onto it anyway gets the allowance any exit gets - and is overshot
        // the moment the aircraft STOPS at or past its node (RolloutExitGate.IsPastExitForOvershoot): no
        // speed handoff re-offers it and trulyStopped needs the aircraft short of the node, so with the
        // margin alone a pilot who obeyed and stopped just past it sat silent on the runway. The handoff
        // block above runs first on this frame, so turnBegun (and the other pilot-driven handoffs) still
        // get first refusal.
        bool tooFastDeclined = _rolloutTooFastNoExit;
        // An aircraft TURNING ONTO the exit's own path is not a miss, even below the 15°
        // turnBegun line (YPPH 21 C9: 14° off the runway, 20 ft from the centreline 100 ft
        // past the junction). A straight-rolling aircraft keeps runway heading and is
        // called exactly as before — see LandingExitPathFollow. Evaluated only once every
        // runway-referenced test says "miss", so the path is built at most once per exit
        // and only when it matters.
        // The margin includes the on-centreline stub allowance (MissMarginFeet): an exit drawn along the
        // centreline first is not missed until its stub has been rolled out.
        if (Navigation.RolloutExitGate.IsPastExitForOvershoot(
                signedAlongPastFt, overshootMargin, tooFastDeclined, groundSpeedKts)
            && hdgDeltaAbs < ROLLOUT_TURN_BEGAN_HDG_DEG
            && stillOnRunway
            && !alignedWithExit
            && !Navigation.LandingExitPathFollow.HoldsOffMiss(
                   GetRolloutExitPath(), lat, lon, headingTrue, _rolloutRunwayHeadingTrue,
                   signedAlongPastFt, overshootMargin)
            && !Navigation.LandingExitPathFollow.StillOnExitPath(
                   GetRolloutExitPath(), lat, lon, signedAlongPastFt, overshootMargin))
        {
            RolloutDiag($"OVERSHOOT detected: signedAlongPast={signedAlongPastFt:F0}ft hdgDelta={hdgDeltaAbs:F1}deg " +
                $"lateral={lateralFromCenterlineFt:F0}ft halfWidth={halfRunwayWidthFt:F0}ft exitBrgErr={exitBrgErr:F1}deg " +
                $"margin={overshootMargin:F0}ft tooFastDeclined={tooFastDeclined} gs={groundSpeedKts:F1}kt");

            if (tooFastDeclined)
            {
                // Declined at its turn point, and the pilot was told so ("Taxiway X, too fast to turn. Slow
                // down."): never "Missed", and never an exit the too-fast scan rejected at that speed. The scan
                // is asked again at the speed NOW - slowing down as told may have brought an exit within
                // comfortable reach - and its sentence is QUEUED, so it follows the warning instead of cutting
                // it off. With nothing reachable, the runway-end countdown speaks for itself on its first
                // frame (the stopped notice, a backtrack, a milestone); the owed status covers the one case
                // where it would not: still rolling short of the 1,500 ft milestone, which could be a minute of
                // silence on an active runway.
                var alternative = FindTooFastAlternative(signedAlongPastFt, groundSpeedKts);
                if (alternative != null)
                {
                    RolloutDiag($"OVERSHOOT (declined) retarget to '{alternative.TaxiwayName}' " +
                        $"distFromThr={alternative.DistanceFromThresholdFeet:F0}ft");
                    RetargetLandingExit(alternative, lat, lon, headingTrue, Navigation.RetargetReason.TooFast,
                        queued: true);
                    return;
                }
                RolloutDiag("OVERSHOOT (declined) no reachable exit -> EnterRunwayEndCountdown");
                EnterRunwayEndCountdown();
                _rolloutCountdownStatusOwed = true;
                return;
            }

            // Measured from the aircraft, not the missed exit - see DownfieldCutoffFeet. A
            // high-speed exit is only declared missed up to ROLLOUT_HIGHSPEED_OVERSHOOT_FT
            // (500 ft) past it, so an exit-relative cutoff can call a turnoff several hundred
            // feet BEHIND the wing "downfield" and pan the tone back at it.
            var nextExit = PickOvershootRetarget(signedAlongPastFt, "OVERSHOOT");
            if (nextExit != null)
            {
                RetargetLandingExit(nextExit, lat, lon, headingTrue);
                return;
            }

            AnnounceMissedLastExit();
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
            bool cooldownOk = (MSFSBlindAssist.Utils.SimClock.UtcNow - _lastUndershootRetargetTime).TotalSeconds >= ROLLOUT_UNDERSHOOT_COOLDOWN_SEC;

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

                    // Skip an exit this rollout already failed to route to: offered again it fails again,
                    // every cooldown, while the pilot keeps the planned exit.
                    if (_rolloutUnroutableExitNodes.Contains(e.NodeId))
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
                    _lastUndershootRetargetTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
                    RolloutDiag($"UNDERSHOOT: retargeting to '{earlierExit.TaxiwayName}' at {earlierExitDistFt:F0}ft " +
                        $"(planned was '{_rolloutExit.TaxiwayName}')");
                    RetargetLandingExit(earlierExit, lat, lon, headingTrue, Navigation.RetargetReason.Earlier);
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

            if (!_rolloutApproach500Announced && distToExitFeet <= xm[2].TriggerMetres / DistanceFormatter.MetresPerFoot && distToExitFeet > ROLLOUT_TURN_NOW_FT)
            {
                RolloutDiag($"500-ft approach callout firing: distToExit={distToExitFeet:F0}ft gs={groundSpeedKts:F1}");
                // "Slow down." when faster than this exit can be taken. The line is
                // RolloutExitGate.SlowDownAboveKts: 60 kt for a shallow exit (below 45°), 30 kt for a
                // sharp one (45° or more, or an unmeasured angle, steep by
                // RolloutExitGate.ExitTurnOffSpeedKts) and for any end-of-runway exit. So a high-speed
                // exit below 45° hears it only above 60 kt, where it cannot be taken.
                //
                // History: before 2026-09 a high-speed exit never heard it at any speed (40–80 kt was
                // treated as its correct approach speed, and "slow down" as contradicting the reason
                // it was picked), while every other exit heard it above 30 kt — still their line.
                string slowSuffix = groundSpeedKts > Navigation.RolloutExitGate.SlowDownAboveKts(
                        _rolloutExit.ExitAngleDegrees, _rolloutExit.ExitType)
                    ? " Slow down." : "";
                AnnounceInstruction($"{CapFirst(name2)}, {xm[2].Label}.{slowSuffix}");
                _rolloutApproach500Announced = true;
            }
        }

        string turnExitName = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
            ? "exit"
            : $"taxiway {_rolloutExit.TaxiwayName}";

        // Speed-scaled PREPARATORY turn call — Normal (right-angle) exits only.
        //
        // The "turn now" trigger below is geometric and must stay that way: it also swings
        // the steering tone onto the exit's turn target, so scaling it with speed would
        // hard-pan the tone hundreds of feet early at 40 kt. But the LEAD genuinely is
        // speed-dependent: rapid-exit guidance arms at 300 ft against this 150 ft, and the
        // undershoot scan uses ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT (11 ft/kt, about 6.5 s).
        // Fixed 150 ft is 4.4 s at the 20 kt you should be doing and only 2.2 s at 40 kt.
        //
        // So the two jobs are split: this call warns on the SAME 11 ft/kt law, capped below
        // the 500 ft milestone; the tone still comes alive only at the turn point. Arrive
        // fast and you are warned earlier — never pulled earlier (KDTW 22L).
        //
        // High-speed exits are excluded: their tone is already easing them onto the arc from
        // 300 ft and they have the 900 ft RETIL-analog call, so this would be clutter. End
        // exits are excluded because a backtrack's direction is ambiguous anyway. And never
        // for an exit too fast to turn onto — the turn point says that instead.
        if (!_rolloutTurnPrepAnnounced && !_rolloutTurnNowAnnounced
            && _rolloutExit.ExitType == "Normal"
            && !_rolloutTooFastNoExit
            && !Navigation.RolloutExitGate.IsTooFastToTurn(groundSpeedKts, _rolloutExit.ExitAngleDegrees))
        {
            double prepFt = Math.Min(ROLLOUT_TURN_PREP_MAX_FT,
                                     groundSpeedKts * ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT);
            if (prepFt >= ROLLOUT_TURN_NOW_FT + ROLLOUT_TURN_PREP_MIN_GAP_FT
                && distToExitFeet <= prepFt)
            {
                string? prepDir = ExitTurnDirectionWord();
                RolloutDiag($"Turn-prep callout firing: distToExit={distToExitFeet:F0}ft " +
                    $"prepFt={prepFt:F0} gs={groundSpeedKts:F1} dir={prepDir ?? "(unknown)"}");
                AnnounceInstruction(prepDir != null
                    ? $"{CapFirst(prepDir)} turn ahead, {turnExitName}."
                    : $"Turn ahead, {turnExitName}.");
                _rolloutTurnPrepAnnounced = true;
            }
        }

        if (!_rolloutTurnNowAnnounced && distToExitFeet <= ROLLOUT_TURN_NOW_FT)
        {
            _rolloutTurnNowAnnounced = true;

            // Never "turn now" at a speed the turn cannot be made at (RolloutExitGate.IsTooFastToTurn).
            // KMEM 36L 2026-09-26: "Turn right now, taxiway M6" at 49 kt onto a 52° exit started the turn
            // that ended in the grass.
            if (Navigation.RolloutExitGate.IsTooFastToTurn(groundSpeedKts, _rolloutExit.ExitAngleDegrees))
            {
                var next = FindTooFastAlternative(signedAlongPastFt, groundSpeedKts);
                string tooFastOutcome = next != null
                    ? $"continue to '{next.TaxiwayName}' at {next.DistanceFromThresholdFeet:F0}ft"
                    : "no exit ahead";
                RolloutDiag($"Too fast for '{_rolloutExit.TaxiwayName}': gs={groundSpeedKts:F1}kt " +
                    $"max={Navigation.RolloutExitGate.MaxTurnSpeedKts(_rolloutExit.ExitAngleDegrees):F0}kt " +
                    $"dist={distToExitFeet:F0}ft -> {tooFastOutcome}");
                if (next != null)
                {
                    RetargetLandingExit(next, lat, lon, headingTrue, Navigation.RetargetReason.TooFast);
                    return;
                }
                _rolloutTooFastNoExit = true;
                AnnounceInstruction(Navigation.RetargetCallout.ComposeTooFastNoExit(_rolloutExit.TaxiwayName));
            }
            else
            {
                RolloutDiag($"Turn-now callout firing: distToExit={distToExitFeet:F0}ft dir={ExitTurnDirectionWord() ?? "(unknown)"}");
                AnnounceInstruction($"{ComposeExitTurnPhrase(lat, lon, headingTrue)} now, {turnExitName}.");
                // The tone target for the turn itself — ExitBearingTrue when it is trustworthy, the
                // junction->vacate-node bearing when it is not (stub first edges, wrong-bank residuals),
                // a pause only when nothing usable exists. See ResolveTurnToneTarget.
                (_rolloutTurnToneTargetDeg, _rolloutTurnTonePause) = ResolveTurnToneTarget(
                    ExitTurnDirectionWord(), _rolloutExit.ExitBearingTrue, ApronBearingFromJunction(),
                    _rolloutRunwayHeadingTrue);
                RolloutDiag($"Turn-now tone target: {(_rolloutTurnTonePause ? "PAUSE" : _rolloutTurnToneTargetDeg > 0.0 ? $"{_rolloutTurnToneTargetDeg:F1}°" : "runway heading")} " +
                    $"(brg={_rolloutExit.ExitBearingTrue:F1}°)");
                // Normal exits (50–110°): reset the heading-error smoother immediately
                // so the turn-target tone below starts with a sharp hard-pan rather than
                // ramping up from the runway-heading residual built up during the approach.
                if (_rolloutExit.ExitType == "Normal" && _rolloutTurnToneTargetDeg > 0.0)
                    _headingErrorInitialized = false;
            }
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
            && _rolloutExit.ExitType == "High-speed"
            // a high-speed exit of 45–50° is only flyable below 30 kt; the turn point decides otherwise
            && !Navigation.RolloutExitGate.IsTooFastToTurn(groundSpeedKts, _rolloutExit.ExitAngleDegrees)
            // declared too fast at its turn point: slowing down as told must not re-offer it
            && !_rolloutTooFastNoExit)
        {
            _rolloutEarlyHandoffDone = true;
            if (TryEarlyExitHandoff(lat, lon, headingTrue))
            {
                _steeringTone.Resume();
                return;
            }
            // Fall through — bearing-to-junction tone below acts as the fallback.
        }

        // Rollout steering tone — three modes, selected by RolloutExitGate.SelectToneMode, plus a
        // fourth, Centerline, while the landing assist's centerline cue is engaged (see below):
        //
        // Above ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS (50 kt) the tone is Silent — this heading cue's
        // pan would follow autopilot crab / crosswind alignment at runway speed. The centerline
        // cue, when engaged, covers this band with a cross-track law instead.
        //
        // Below 50 kt and beyond ROLLOUT_EXIT_TONE_ARM_FT (300 ft) of the chosen exit, the
        // tone is DriftCorrection: desired heading is the runway itself, steering the pilot
        // back onto the runway heading through the long deceleration that used to be silent.
        // Heading only — no cross-track term (see the mode's own comment further down).
        // KSEA 34L 2026-08-21: a 15.1° drift built up here with no cue at all, and the
        // steering tone's first utterance was a 79° hard pan once ExitBearing took over.
        //
        // Exception, within the targeted exit's own turn window (RolloutExitTurnWindowFeet() —
        // RolloutExitGate.TurnWindowFeetFor, never more than TurnWindowFeet): a heading deviation
        // that is toward a KNOWN exit side goes Silent instead of DriftCorrection — don't
        // fight a turn IsExitTurnBegun is about to accept just because it hasn't reached the
        // 15° turnBegun threshold yet. See RolloutExitGate.SelectToneMode's doc. Until 2026-09
        // the window was the fixed 1,000 ft: at KMEM 36L a leftover right turn 631 ft before M7,
        // whose own window is 324 ft, silenced the tone; it now gets the drift tone instead.
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
        // Too fast for the targeted exit — by the too-fast rule before its turn point, or declined there
        // ("too fast to turn" with no exit left): the tone holds the runway heading and never leads the
        // pilot toward that exit, nor goes quiet for a turn toward it (RolloutExitGate.SelectToneMode).
        //
        // While the landing assist's rollout centerline cue is engaged (CenterlineCueEngagedProvider), the
        // cue owns the runway and exit guidance the exit: the silent band above 50 kt, the drift band beyond
        // the exit's turn window and every too-fast frame become Centerline, which pauses this tone while the
        // cue plays Takeoff Assist's centerline law (LandingFlareAssistManager reads ExitGuidanceOwnsRolloutTone).
        // KSEA 16R 2026-10-15: an A300 autoland yawed off the runway between 96 and 70 kt, all of it above the
        // 50 kt line, with no cue at all.
        bool tooFastForExit = _rolloutTooFastNoExit
            || (!_rolloutTurnNowAnnounced
                && Navigation.RolloutExitGate.IsTooFastToTurn(groundSpeedKts, _rolloutExit.ExitAngleDegrees));
        var toneMode = Navigation.RolloutExitGate.SelectToneMode(
            groundSpeedKts, distToExitFeet, hdgDelta, exitRelBearingDeg,
            turnWindowFeet, tooFastForExit: tooFastForExit,
            centerlineCueEngaged: CenterlineCueEngagedProvider?.Invoke() ?? false);
        if (toneMode != _rolloutToneMode)
        {
            // Start every mode from a clean filter so the pan is sharp and immediate rather
            // than ramping out of the previous mode's residual.
            _headingErrorInitialized = false;
            _rolloutToneMode = toneMode;
        }

        // What the tone log line below reports for a live tone: kept as numbers and formatted only on a
        // frame that line is written, so a stopped aircraft allocates nothing here per frame.
        bool toneLive = false;
        double toneDesiredHeading = 0.0, toneRawError = 0.0;
        // A Normal exit after its "turn now" with no usable turn target (ResolveTurnToneTarget's
        // pause case): a runway-heading hold would pull AGAINST the turn just commanded, so the verbal
        // owns the turn until the 15° turnBegun handoff brings the route tone in. The one silent
        // window, and it is brief.
        // Centerline: the landing assist's cue plays instead of this tone, so this one is paused exactly as for Silent.
        bool normalExit = _rolloutExit!.ExitType == "Normal";
        bool turnTonePaused = toneMode == Navigation.RolloutToneMode.ExitBearing
                              && _rolloutTurnNowAnnounced && normalExit && _rolloutTurnTonePause;
        if (toneMode is Navigation.RolloutToneMode.Silent or Navigation.RolloutToneMode.Centerline || turnTonePaused)
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
                // Desired heading, by exit type:
                //   High-speed / End — bearing from current position to the exit node.
                //     "Guide me to the junction" rather than "point at the apron"; on a RET the
                //     drift onto the arc is the manoeuvre, so the bearing deviating as the aircraft
                //     nears an off-axis junction is exactly the wanted cue.
                //   Normal — RUNWAY HEADING until the "turn now" callout, then the turn target.
                //
                // Why not bearing-to-junction for a Normal (right-angle) exit before its turn point:
                // a point bearing amplifies ANY lateral offset — the aircraft's or the node's —
                // without bound as the aircraft closes. Measured on KDTW 22L against the 15° full
                // pan: a 10 m offset reads 6° at 300 ft but 47° at 30 ft, a 25 m offset 16° → 70°, so
                // the tone saturated hard-over from parallax in the last seconds before an 81° turn the
                // pilot had not started, while the correct action was to hold the centreline and
                // brake (KDTW 22L Y3, live 2026-08-18). Against runway heading the tone is silent while
                // tracking straight and gives a real, non-amplifying cue if the pilot drifts.
                //
                // After "turn now" a Normal exit steers to the turn target ResolveTurnToneTarget
                // picked (ExitBearingTrue when it agrees with the spoken word, the junction → vacate
                // bearing otherwise), so the tone means "how much turn is LEFT" and decays to silence
                // as the pilot aligns. Only a plausible exit direction
                // (RolloutExitGate.IsPlausibleExitBearing): KMEM M6 carried 127° true on a 359°
                // runway, and after "turn now" the tone demanded that hairpin at 49 kt.
                if (_rolloutTurnNowAnnounced && normalExit)
                {
                    double target = _rolloutTurnToneTargetDeg == 360.0 ? 0.0 : _rolloutTurnToneTargetDeg;
                    desiredHeading = _rolloutTurnToneTargetDeg > 0.0
                                     && Navigation.RolloutExitGate.IsPlausibleExitBearing(
                                            _rolloutTurnToneTargetDeg, _rolloutRunwayHeadingTrue)
                        ? target
                        : _rolloutRunwayHeadingTrue;
                }
                else if (normalExit)
                {
                    desiredHeading = _rolloutRunwayHeadingTrue;
                }
                else
                {
                    const double MPD = 111132.0;
                    double midLatRad = (lat + _rolloutExit.Latitude) * 0.5 * Math.PI / 180.0;
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
                // DriftCorrection — the phase that used to be silent. Desired heading is the
                // runway itself, so the tone reads "steer back onto the runway heading".
                // HEADING ONLY — there is no cross-track term, so an aircraft that has drifted
                // and then re-aligned with the runway gets silence while still displaced,
                // tracking parallel. That is deliberate (a constant offset is not closing on
                // the edge, and a cross-track term would fight the exit turn), but do not
                // describe this tone as steering back to the CENTRELINE: it does not.
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
            toneLive = true;
            toneDesiredHeading = desiredHeading;
            toneRawError = rawError;
        }

        // Rollout tone diagnostics: every frame the tone can be live, so a report of an erratic tone is
        // read from landing_exit.log instead of being reconstructed from the code (KMEM 36L 2026-09-26).
        // While the aircraft is MOVING with the tone live (above RolloutExitGate.NoExitStoppedGroundSpeedKts,
        // at or below the 50 kt tone line), at most one line per ROLLOUT_TONE_LOG_MIN_INTERVAL_MS (100 ms):
        // SIM_FRAME runs at 30-60 Hz, and a line per frame flooded the log. Any frame whose tone mode or
        // targeted exit differs from the last line written is ALWAYS logged, whatever the speed or interval.
        // A pilot held on the runway stays in LandingRollout indefinitely, and a line per frame would cycle
        // landing_exit.log's 5 MB x 3 rotation within the hour, so a stopped aircraft logs nothing until
        // something changes.
        DateTime toneLogNowUtc = MSFSBlindAssist.Utils.SimClock.UtcNow;
        bool toneLogMoving = groundSpeedKts > Navigation.RolloutExitGate.NoExitStoppedGroundSpeedKts
                             && groundSpeedKts <= ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS
                             && (toneLogNowUtc - _rolloutToneLogUtc).TotalMilliseconds >= ROLLOUT_TONE_LOG_MIN_INTERVAL_MS;
        bool toneLogChanged = toneMode != _rolloutToneLogMode
                              || !ReferenceEquals(_rolloutExit, _rolloutToneLogExit);
        if (toneLogMoving || toneLogChanged)
        {
            _rolloutToneLogUtc = toneLogNowUtc;
            string toneDiag = toneLive
                ? $"desired={toneDesiredHeading:F1} raw={toneRawError:+0.0;-0.0} smooth={_smoothedHeadingError:+0.0;-0.0}"
                : "desired=- raw=- smooth=-";
            double lateralSignedM = SignedLateralFromRunwayMeters(
                lat, lon, _rolloutRunway!.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue);
            RolloutDiag($"tone mode={toneMode} exit='{_rolloutExit!.TaxiwayName}' dist={distToExitFeet:F0}ft " +
                $"window={turnWindowFeet:F0}ft hdgDelta={hdgDelta:+0.0;-0.0}deg " +
                $"lateral={lateralSignedM:+0.0;-0.0}m gs={groundSpeedKts:F1}kt turnBegun={turnBegun} {toneDiag}");
            _rolloutToneLogMode = toneMode;
            _rolloutToneLogExit = _rolloutExit;
        }
    }

    /// <summary>
    /// "left" / "right" for the targeted exit, or null when it cannot be said: ResolveExitTurnDirection,
    /// then the side the exit list gives it. Never the aircraft's own bearing to the node — see
    /// ComposeExitTurnPhrase.
    /// </summary>
    private string? ExitTurnDirectionWord()
        => ResolveExitTurnDirection()
           ?? (_rolloutExit == null ? null
               : string.Equals(_rolloutExit.ExitSide, "Left", StringComparison.OrdinalIgnoreCase) ? "left"
               : string.Equals(_rolloutExit.ExitSide, "Right", StringComparison.OrdinalIgnoreCase) ? "right"
               : null);

    /// <summary>
    /// Which way the chosen exit turns, as a spoken word ("left"/"right"), or NULL when no
    /// aircraft-independent source is confident enough to name a side.
    ///
    /// The old rule — bearing from the AIRCRAFT to the junction node, minus aircraft heading
    /// — is a coin flip wherever the scenery models the junction on the runway centreline,
    /// because then the sign is decided by the aircraft's own tracking error rather than by
    /// the exit. Measured at KDTW 22L, where 5 of 13 junctions sit within a metre of the
    /// centreline (Y3 0.4 m, R 0.1 m, V 0.4 m, Z7 0.7 m, Z5 0.9 m): Y3 is a LEFT exit, and
    /// the aircraft happened to be 6-9 m RIGHT of centre on 2026-08-18 so the old rule would
    /// have said "left" correctly — but had it been tracking a metre the other way, which is
    /// well inside normal rollout, it would have announced "Turn RIGHT now" for a left exit.
    /// A confident wrong side is far worse for a blind pilot than no side at all.
    ///
    /// Sources, in order, all independent of where the aircraft is:
    ///   1. Junction -> corridor-exit node (ApronNodeId). This is the exit PATH's own
    ///      direction and the same node the handoff routes to, so the word matches the tone.
    ///   2. ExitBearingTrue vs runway heading.
    /// Either must clear EXIT_TURN_DIRECTION_MIN_DEG to be spoken; otherwise the caller drops
    /// the direction word and says "Turn now, taxiway X" (e.g. KDTW Y4, whose ExitBearingTrue
    /// is a 1.8-degree parallel stub and whose ApronNodeId was never computed).
    /// </summary>
    private string? ResolveExitTurnDirection()
    {
        if (_rolloutExit == null) return null;

        if (_rolloutExit.ApronNodeId > 0 && _rolloutExit.ApronNodeId != _rolloutExit.NodeId
            && _graph != null && _graph.Nodes.TryGetValue(_rolloutExit.ApronNodeId, out var apronNode))
        {
            double apronBrg = NavigationCalculator.CalculateBearing(
                _rolloutExit.Latitude, _rolloutExit.Longitude, apronNode.Latitude, apronNode.Longitude);
            double apronDelta = NormalizeAngle(apronBrg - _rolloutRunwayHeadingTrue);
            if (Math.Abs(apronDelta) >= EXIT_TURN_DIRECTION_MIN_DEG)
                return apronDelta < 0 ? "left" : "right";
        }

        if (_rolloutExit.ExitBearingTrue > 0.0)
        {
            double brg = _rolloutExit.ExitBearingTrue == 360.0 ? 0.0 : _rolloutExit.ExitBearingTrue;
            double delta = NormalizeAngle(brg - _rolloutRunwayHeadingTrue);
            if (Math.Abs(delta) >= EXIT_TURN_DIRECTION_MIN_DEG)
                return delta < 0 ? "left" : "right";
        }

        return null;
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
    ///
    /// ONE failure returns TRUE, deliberately: when the reachability guard refuses the
    /// route, guidance has CONCLUDED (HandleArrival has run and the state is Arrived, not
    /// Taxiing) and the caller must stop processing the frame. Returning false there would
    /// run the rest of UpdateLandingRollout — tone selection, overshoot detection, the
    /// 150 ft verbal — against a concluded session. Do not "tidy" it to false.
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
        //
        // (e) Whichever of the above wins is then pushed past the runway-holding
        //     position by RunwayVacateResolver — see ResolveExitHandoffDestination.
        int destNodeId = ResolveExitHandoffDestination(out string destSrc);
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
        // The verdict is computed before LoadRoute because the reachability guard and the
        // crossing guard's decline/conclude split both need it, from the same lat/lon, under the
        // same name the UpdateLandingRollout site uses. It no longer feeds the start hold: that is
        // the pass's own call from the aircraft's position (see the LoadRoute argument below).
        bool offRunwayAtHandoff = !IsWithinRolloutRunwayLaterally(lat, lon);
        string? err = LoadRoute(
            _dataProvider, _icao,
            lat, lon, headingTrue,
            destNodeId, exitName,
            taxiwaySequence: null,
            prebuiltGraph: _graph,
            announceSummary: false,
            startTaxiwayName: startTwy,
            exitPathNodeIds: startTwy != null ? GetRolloutExitPathNodeIds() : null,
            // A log label only (phase=touchdown), as at UpdateLandingRollout's handoff. The pass
            // itself refuses a start hold while the aircraft stands within the clear margin of any
            // runway, so nothing here has to say whether it is on the pavement.
            landingRolloutRoute: true);

        if (err != null)
        {
            RolloutDiag($"TryEarlyExitHandoff: LoadRoute failed — {err}");
            return false;
        }
        StripVacateHoldShortsOnLandedRunway(_route);

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

        RolloutDiag($"TryEarlyExitHandoff OK: destNodeId={destNodeId} ({destSrc}) " +
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

        // Reachability guard — the LAST ungated landing-exit handoff re-route. This method
        // builds a route, arms the post-handoff monitor and moves to Taxiing exactly like
        // the handoff in UpdateLandingRollout, so [ROL-29]'s "IsHandoffRouteReachable must
        // gate every landing-exit handoff re-route" was simply not true of it. Its only
        // route sanity check is the first segment's BEARING (above); the KSEA 34L failure
        // lived on the DISTANCE axis — a segment 53.9 m of cross-track away, with the
        // aircraft 17.8 m outside the runway edge, that the tone panned 79° at.
        //
        // Placed after _isLandingExitRoute / _landingExitOffPavement are restored so the
        // closure below speaks the landing-exit wording, and read at _currentSegmentIndex
        // (0 here — LoadRoute anchors a fresh route at its start) so it tests the segment
        // the tone is ACTUALLY about to steer at.
        //
        // Concluding is the safe direction: the alternative is pointing the steering tone
        // at a segment the aircraft is not on. Near-no-op for the normal case — the early
        // handoff fires while still on the runway, and an on-runway handoff makes the guard
        // return true unchanged.
        if (_route != null && _currentSegmentIndex >= 0
            && _currentSegmentIndex < _route.Segments.Count)
        {
            var firstSeg = _route.Segments[_currentSegmentIndex];
            double crossToFirstM = TaxiGraph.PerpendicularDistanceMetersStatic(
                lat, lon,
                firstSeg.FromNode.Latitude, firstSeg.FromNode.Longitude,
                firstSeg.ToNode.Latitude, firstSeg.ToNode.Longitude);

            if (!Navigation.RolloutExitGate.IsHandoffRouteReachable(
                    offRunwayAtHandoff, crossToFirstM, firstSeg.PathWidth))
            {
                RolloutDiag($"TryEarlyExitHandoff: route unreachable — {crossToFirstM:F0} m from " +
                    $"segment {_currentSegmentIndex} (width {firstSeg.PathWidth:F0} ft) with the " +
                    $"aircraft off the runway — concluding rather than steering across it");
                // Off the runway by IsHandoffRouteReachable's own early return. From HERE the
                // shared rule always lands on _landingExitRouteUnreachable — the LoadRoute
                // above nulls the planned-exit name and this path never restores it, unlike
                // the UpdateLandingRollout site which deliberately carries it across — and
                // that is the correct closure: the early handoff fires short of the exit,
                // where "short of X" would be unearned.
                ConcludeLandingExitOffRunway();
                // TRUE, not false: false means "handoff declined, keep flying the rollout"
                // and the caller falls through to the bearing-to-junction fallback tone and
                // the rest of UpdateLandingRollout. Guidance has CONCLUDED here, so the
                // caller must return. Its _steeringTone.Resume() on this path is inert —
                // HandleArrival's landing-exit closure already called Stop(), and Resume()
                // only clears the paused flag on a disposed generator.
                return true;
            }

            // Same whole-route crossing guard as the UpdateLandingRollout handoff —
            // [ROL-27] requires EVERY landing-exit handoff re-route to be gated
            // identically, and this method was the last ungated one once before
            // (commit 29b8bcbf). See the other site for the KATL 26R defect.
            if (HandoffRouteReCrossesLandingRunway(lat, lon))
            {
                // Gated identically to the UpdateLandingRollout site: DECLINE only while the
                // aircraft is still ON the runway with the exit ahead; otherwise CONCLUDE.
                // The caller's precondition (!pastExit) already supplies "the exit is ahead"
                // here, so offRunwayAtHandoff is the only term left to test.
                //
                // Off the pavement the decline's premise fails: "keep flying the rollout and
                // the exit comes to you" is a claim about an aircraft on the runway. Off it,
                // the overshoot detector cannot fire and !pastExit holds, so returning false
                // would leave the pilot with no route, no closure and — because
                // _rolloutEarlyHandoffDone latches this method to one shot per exit — no second
                // attempt either. Conclude instead, matching the reachability guard above
                // exactly: same closure helper, same TRUE return meaning "guidance concluded,
                // stop processing the frame". The planned-exit name is always null on this path
                // (LoadRoute nulls it and nothing here restores it), so the shared rule always
                // lands on _landingExitRouteUnreachable, whose "Stop and hold position" wording
                // is safe precisely because this branch is off-runway only.
                if (offRunwayAtHandoff)
                {
                    RolloutDiag($"TryEarlyExitHandoff: route re-crosses runway " +
                        $"{_rolloutRunway?.RunwayID ?? "?"} with the aircraft off the runway — " +
                        $"concluding rather than driving back across it");
                    ConcludeLandingExitOffRunway();
                    return true;
                }

                RolloutDiag($"TryEarlyExitHandoff: route re-crosses runway " +
                    $"{_rolloutRunway?.RunwayID ?? "?"} — declining the early handoff and " +
                    $"staying in LandingRollout");

                // Unwind what this method set, in the shape of the bearing-sanity reject above.
                // NOT a full restore — an earlier version of this comment claimed the machine
                // is "left exactly as if the attempt had never run", which is not true and is
                // corrected here. Four things this method wrote are deliberately left standing:
                // _smoothedHeadingError / _headingErrorInitialized (zeroed just above),
                // _isLandingExitRoute and _landingExitOffPavement. All are behaviourally inert
                // on the path taken. The smoother re-converges within a few frames of rollout
                // tone updates. _landingExitOffPavement is read only by HandleArrival, which
                // this path does not reach — and, because the field PERSISTS past this frame,
                // that on its own is not enough: what makes it inert is that every later route
                // to HandleArrival re-decides it. The UpdateLandingRollout crossing guard's
                // on-runway conclude arm assigns _landingExitOffPavement = false outright, so
                // nothing stale survives it; its off-runway arm sets _landingExitVacatedEarly or
                // _landingExitRouteUnreachable, both of which order AHEAD of the offPavement
                // branch in HandleArrival, so that branch is never reached from there. (The
                // value left standing here is in any case the restored PRE-handoff one — the
                // restore at the top of this method already put it back — not something this
                // attempt invented.) _isLandingExitRoute has two other readers, and both
                // are unreachable from here: one requires _state == Taxiing (this stays in
                // LandingRollout) and the other requires a route on its final segment (_route is
                // nulled below) — and any later LoadRoute resets the flag before either can see
                // it. Left standing rather than cleared because clearing them would be a
                // behaviour change dressed as tidying.
                //
                // Discard the rejected route, mirroring that reject's own rationale — it
                // "would feed the off-route detector once state moves to Taxiing" — which
                // applies with more force here: a route that drives across the landing
                // runway is the last thing that detector should be chasing. Nothing good is
                // lost, since the LoadRoute above already overwrote the touchdown route.
                // This does NOT collide with the "a path landing in Taxiing with a null
                // _route must stop the steering tone first" rule: that rule is about
                // Taxiing, where the tone is driven by _route. We stay in LandingRollout,
                // where UpdateLandingRollout drives the tone from the exit BEARING and never
                // reads _route — so the tone stays rollout-driven rather than being stranded
                // panned at a route that no longer exists. Rollout-driven is not the same as
                // always audible, and the above-50-kt Silent mode is the wrong one to name:
                // this method only runs at low speed. The two of RolloutExitGate.SelectToneMode's
                // states that can genuinely produce no sound here are the turn-window Silent
                // (from 300 ft out to the targeted exit's own window, at most 1,000 ft) and a
                // sub-DriftToneSilentDeg DriftCorrection, which is a
                // heading cue and so goes to zero volume once the aircraft is aligned. This
                // decline fires within ExitToneArmFeet (300 ft), where the tone is ExitBearing
                // and audible, so it does not need its own callout — and if the aircraft then
                // stops short, the UpdateLandingRollout decline speaks the shared one-shot
                // "keep rolling to the exit" instruction.
                _route = null;
                _destinationNodeId = 0;
                _postHighSpeedExitMinBearing = 0.0;
                // Was false before this method ran; the handoff is being declined, so the
                // post-handoff overshoot monitor must not be left armed. Harmless today
                // only because both of its consumers sit behind a _state == Taxiing gate,
                // which is exactly the kind of accidental safety this file has been bitten
                // by before.
                _rolloutHandoffActive = false;

                // No retry floor is needed here (unlike the UpdateLandingRollout site): the
                // caller latches _rolloutEarlyHandoffDone = true before calling, so this
                // method is genuinely one-shot per exit and cannot re-enter per frame.
                //
                // FALSE, not true: false means "handoff declined, keep flying the rollout",
                // which is exactly right here — the early handoff fires SHORT of the exit,
                // so the exit is by construction still ahead and reachable by continuing.
                // LoadRoute left state at RouteLoaded, so restore LandingRollout the way the
                // bearing-sanity reject above does, or the machine is stranded with no tone
                // and "Where am I" reporting no active route.
                SetState(TaxiGuidanceState.LandingRollout);
                return false;
            }
        }

        SetState(TaxiGuidanceState.Taxiing);
        return true;
    }

    /// <summary>
    /// Removes any "hold short of runway X" for the landed runway on the LEADING run of a
    /// landing-exit route — the part still on the runway the aircraft just landed on. Before
    /// 2026-09 the crossing pass detected a crossing by edge-vs-centerline intersection, and an
    /// exit taxiway whose first nodes sit a metre either side of the centerline (EFRO 21 → B,
    /// 2026-08-25: nodes at +1 m then −12 m) read as a crossing of the landed runway, so the pilot
    /// vacating at 24 kt heard "Stop. Hold short of runway 03. Press continue when cleared" ON the
    /// runway they were leaving. The per-runway classifier (RunwayRouteClassifier) no longer reads
    /// a route that starts on a runway and leaves it as meeting it, so this is now a backstop, kept
    /// because the cost of that callout is a stop on an active runway. Leaving the runway you landed
    /// on is never a crossing that needs a hold; only the leading on-pavement run is touched, so a
    /// route that later genuinely re-crosses this strip keeps its hold.
    /// </summary>
    private void StripVacateHoldShortsOnLandedRunway(TaxiRoute? route)
    {
        if (route == null || _rolloutRunway == null) return;
        string landed = _rolloutRunway.RunwayID;
        if (string.IsNullOrEmpty(landed)) return;
        for (int i = 0; i < route.Segments.Count; i++)
        {
            var seg = route.Segments[i];
            if (seg.FromNode == null) break;
            // The leading run ends at the first segment that STARTS off the pavement band.
            if (!IsWithinRolloutRunwayLaterally(seg.FromNode.Latitude, seg.FromNode.Longitude))
                break;
            if (seg.IsHoldShortPoint && !string.IsNullOrEmpty(seg.HoldShortRunway)
                && RunwayDesignatorsMatch(seg.HoldShortRunway, landed))
            {
                RolloutDiag($"Stripped false vacate hold-short '{seg.HoldShortRunway}' at seg {i} " +
                            $"(leading run on landed runway {landed})");
                seg.IsHoldShortPoint = false;
                seg.HoldShortRunway = "";
            }
        }
    }

    /// <summary>
    /// The chosen exit's own path — the graph route from the exit junction to the node the
    /// handoff re-route steers to — for <see cref="Navigation.LandingExitPathFollow"/>.
    /// Built once per exit object (a retarget swaps the object, so it rebuilds); null when
    /// no usable path exists, which leaves both missed-exit detectors exactly as they were.
    /// Uses <see cref="Navigation.LandingExitDestination.Resolve"/> directly rather than
    /// <see cref="ResolveExitHandoffDestination"/>, whose off-pavement verdict and log lines
    /// belong to the handoff itself.
    /// </summary>
    private IReadOnlyList<(double Lat, double Lon)>? GetRolloutExitPath()
    {
        var exit = _rolloutExit;
        if (exit == null || _graph == null || _rolloutRunway == null)
        {
            // Clear the cache on the way out, not just on a successful rebuild. Returning
            // early left _rolloutExitPathFor pointing at the PREVIOUS exit, so
            // OnAxisMissExtensionFeet() kept handing out that exit's on-axis run — a miss
            // held off by a stub belonging to an exit we are no longer flying to.
            _rolloutExitPathFor = null;
            _rolloutExitPath = null;
            _rolloutExitPathNodeIds = null;
            _rolloutExitOnAxisRunFt = 0.0;
            return null;
        }
        if (ReferenceEquals(_rolloutExitPathFor, exit)) return _rolloutExitPath;

        _rolloutExitPathFor = exit;
        _rolloutExitPath = null;
        _rolloutExitPathNodeIds = null;
        _rolloutExitOnAxisRunFt = 0.0;
        try
        {
            int dest = Navigation.LandingExitDestination.Resolve(
                _graph, exit, _rolloutAllExits, _rolloutRunway, _rolloutRunwayHeadingTrue,
                out _, out _, out _);
            if (!LegacyExitAnchorForHarness)
                dest = Navigation.LandingExitDestination.CorrectBackwardsStart(
                    _graph, exit, _rolloutRunway, _rolloutRunwayHeadingTrue, dest);
            if (dest > 0 && dest != exit.NodeId)
            {
                var r = new TaxiRouter(_graph).FindShortestPath(exit.NodeId, dest);
                // A few hundred metres at most for a real exit; anything longer is a
                // detour through the network, not the exit, and must not hold a miss off.
                if (r != null && r.Segments.Count > 0 && r.TotalDistanceMeters <= EXIT_PATH_MAX_M)
                {
                    var pts = new List<(double Lat, double Lon)>(r.Segments.Count + 1)
                    {
                        (r.Segments[0].FromNode.Latitude, r.Segments[0].FromNode.Longitude)
                    };
                    foreach (var s in r.Segments)
                        pts.Add((s.ToNode.Latitude, s.ToNode.Longitude));
                    _rolloutExitPath = pts;
                    var ids = new List<int>(r.Segments.Count + 1) { r.Segments[0].FromNode.NodeId };
                    foreach (var s in r.Segments) ids.Add(s.ToNode.NodeId);
                    _rolloutExitPathNodeIds = ids;
                    _rolloutExitOnAxisRunFt = OnAxisRunFeet(pts, exit);
                }
            }
            RolloutDiag($"Exit path for '{exit.TaxiwayName}': " +
                (_rolloutExitPath == null
                    ? $"none (dest={dest}) — miss detection runway-referenced only"
                    : $"{_rolloutExitPath.Count} points to node {dest}, runs {_rolloutExitOnAxisRunFt:F0} ft along the centreline"));
        }
        catch (Exception ex)
        {
            RolloutDiag($"Exit path for '{exit.TaxiwayName}' failed: {ex.Message}");
            _rolloutExitPath = null;
            _rolloutExitPathNodeIds = null;
        }
        return _rolloutExitPath;
    }

    /// <summary>The exit's own path node ids (junction first), or null — built with GetRolloutExitPath.</summary>
    private IReadOnlyList<int>? GetRolloutExitPathNodeIds()
    {
        GetRolloutExitPath();
        return _rolloutExitPathNodeIds;
    }

    /// <summary>
    /// The landing-exit handoff anchors its route start on the exit's taxiway NAME. Where the
    /// exit's path leaves the runway along a DIFFERENTLY named taxiway, the nearest node carrying
    /// the name can lie on another branch: KCLT 18 "D6" leaves via R, and the nearest D6 node was
    /// on D6's other arm, 31 m beside the runway and ahead — the route started there, its first leg
    /// panned the tone AWAY from the exit, and a tone-following pilot heard "Missed taxiway D6"
    /// (VirtualPilot 2026-09-25). When the name's node is not on the exit's own path, start from
    /// the path node nearest the aircraft instead. A name anchor already on the path — the common
    /// case — is returned unchanged.
    /// </summary>
    /// <summary>
    /// For a landing-rollout route: a filter admitting only nodes on the side of the landed runway the
    /// vacate destination is on, or on the runway's own pavement (Navigation.ExitOwnSide). Null (no
    /// filter) when this is not a landing-rollout route, the landed runway is not in the graph, or the
    /// destination itself is on the pavement and so names no side. See LoadRoute's start-node anchor.
    /// </summary>
    internal Func<TaxiNode, bool>? ExitOwnSideFilter(bool landingRolloutRoute, int destinationNodeId)
    {
        if (!landingRolloutRoute || _graph == null || _rolloutRunway == null) return null;
        if (!_graph.Nodes.TryGetValue(destinationNodeId, out var dest)) return null;
        var cl = Navigation.RolloutRunwayReCrossing.FindLandingRunwayCenterline(
            _graph.RunwayCenterlines, _rolloutRunway.RunwayID);
        if (cl == null) return null;
        return Navigation.ExitOwnSide.Filter(Navigation.RunwayShape.For(cl), dest.Latitude, dest.Longitude);
    }

    private TaxiNode? ExitPathStartAnchor(TaxiNode? named, IReadOnlyList<int>? pathIds,
                                          double lat, double lon, int? componentId)
    {
        if (named == null || pathIds == null || pathIds.Count == 0 || _graph == null) return named;
        if (pathIds.Contains(named.NodeId)) return named;
        // Only where the path does not carry the exit's name at all past the junction (the
        // "exit D6 that leaves via R" shape). Where it does, the name anchor's pick is a
        // deliberate one — on a turn-back exit it is the branch the backwards-start retry
        // relies on (measured: overriding every off-path anchor fixed 59 findings but made 43).
        string? exitName = _rolloutExit?.TaxiwayName;
        if (string.IsNullOrEmpty(exitName)) return named;
        // Nor on a "(sharp turn back)" exit: its path starts backwards by definition, and the
        // name anchor plus LoadRoute's backwards-start retry own that case (KIAH 15R WS, KSLC 35
        // K4, KDTW 09L V2 all got worse when this override reached them).
        if (_rolloutExit!.RequiresTurnBack) return named;
        for (int i = 1; i < pathIds.Count; i++)
            if (_graph.Nodes.TryGetValue(pathIds[i], out var pn) && pn.TaxiwayNames.Contains(exitName))
                return named;
        TaxiNode? best = null;
        double bestD = double.MaxValue;
        foreach (int id in pathIds)
        {
            if (!_graph.Nodes.TryGetValue(id, out var n)) continue;
            if (componentId.HasValue && n.ComponentId != componentId.Value) continue;
            double d = TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude);
            if (d < bestD) { bestD = d; best = n; }
        }
        if (best != null)
            RolloutDiag($"Handoff anchor: n{named.NodeId} ('{named.TaxiwayNames.FirstOrDefault()}') is off the exit's path; starting at path node n{best.NodeId}");
        return best ?? named;
    }

    private const double EXIT_PATH_MAX_M = 800.0;

    /// <summary>
    /// How far (feet) the exit's own path runs ALONG the runway centreline past the junction
    /// before it leaves it. Many sceneries draw the exit taxiway down the centreline first:
    /// KORD 27L M for 80 m, ZSPD 35R B7 for 99 m, EGLL 27L N7 for 40 m after a 3 m jog. The
    /// route tone correctly says "continue" down that stretch, so a miss judged at the
    /// junction + 100 ft told a pilot following the tone that they had missed the exit
    /// (VirtualPilot, 2026-09-18: ~1,000 of 4,070 landings at the 100 busiest airports).
    /// Both missed-exit detectors add the part of this run that TurnPointOffsetFeet does not
    /// already cover. The path is the one GetRolloutExitPath builds; only points that stay
    /// within LandingExitPathFollow.OnAxisLateralMetres of the centreline and move forward count; capped.
    /// </summary>
    private double OnAxisRunFeet(IReadOnlyList<(double Lat, double Lon)> path, Navigation.LandingExit exit)
        => _rolloutRunway == null ? 0.0
            : Navigation.LandingExitPathFollow.OnAxisRunMetres(path, exit.Latitude, exit.Longitude,
                  _rolloutRunway.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue) * METERS_TO_FEET;


    /// <summary>
    /// How far past the exit's junction BOTH missed-exit detectors wait before calling a miss. Main's
    /// margin is read at how steeply the exit leaves its node (RolloutExitGate.OvershootMarginFor on
    /// DivergenceAngleDegrees), which already waits out an exit that hugs the centreline. The other
    /// term is the same rule read at the exit's overall angle plus the on-centreline stub the exit's
    /// own path runs along first (OnAxisMissExtensionFeet, measured with VirtualPilot). The two
    /// describe the SAME stub, so they are combined with Max, never added: added, a straight roll past
    /// LTFM 34L A7A was called 202 m past the junction against 153 m (divergence) and 117 m (stub).
    /// </summary>
    private double MissMarginFeet()
    {
        if (_rolloutExit == null) return Navigation.RolloutExitGate.ExitOvershootFeet;
        double byDivergence = Navigation.RolloutExitGate.OvershootMarginFor(
            _rolloutExit.ExitType, _rolloutExit.DivergenceAngleDegrees);
        double byStub = Navigation.RolloutExitGate.OvershootMarginFor(
            _rolloutExit.ExitType, _rolloutExit.ExitAngleDegrees) + OnAxisMissExtensionFeet();
        return Math.Max(byDivergence, byStub);
    }

    /// <summary>The miss-margin extension for the current exit: the on-centreline run not already in TurnPointOffsetFeet.</summary>
    private double OnAxisMissExtensionFeet()
    {
        if (_rolloutExit == null) return 0.0;
        GetRolloutExitPath();
        return Math.Max(0.0, _rolloutExitOnAxisRunFt - _rolloutExit.TurnPointOffsetFeet);
    }

    /// <summary>
    /// True when the aircraft is on a mapped taxi edge — within that edge's half-width plus
    /// 5 m (15 m when the width is unknown). Only evaluated beyond the runway edge, so a
    /// linear scan of the graph is fine. No graph → true, which keeps the old behaviour.
    /// </summary>
    private bool IsOnTaxiPavement(double lat, double lon)
    {
        if (_graph == null) return true;
        const double MPD = 111132.0;
        double cosLat = Math.Cos(lat * Math.PI / 180.0);
        foreach (var (fromId, edges) in _graph.Adjacency)
        {
            if (!_graph.Nodes.TryGetValue(fromId, out var a)) continue;
            double ax = (a.Longitude - lon) * MPD * cosLat, ay = (a.Latitude - lat) * MPD;
            if (ax * ax + ay * ay > 1000.0 * 1000.0) continue;   // no taxi edge is a km long
            foreach (var e in edges)
            {
                if (!_graph.Nodes.TryGetValue(e.ToNodeId, out var b)) continue;
                double bx = (b.Longitude - lon) * MPD * cosLat, by = (b.Latitude - lat) * MPD;
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-6 ? 0.0 : Math.Clamp(-(ax * dx + ay * dy) / len2, 0.0, 1.0);
                double cx = ax + t * dx, cy = ay + t * dy;
                double allow = e.WidthFeet > 0 ? e.WidthFeet / METERS_TO_FEET * 0.5 + 5.0 : 15.0;
                if (cx * cx + cy * cy <= allow * allow) return true;
            }
        }
        return false;
    }

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
        int corrected = LegacyExitAnchorForHarness ? destNodeId
            : Navigation.LandingExitDestination.CorrectBackwardsStart(
                _graph, _rolloutExit, _rolloutRunway, _rolloutRunwayHeadingTrue, destNodeId);
        if (corrected != destNodeId)
        {
            RolloutDiag($"Vacate destination {destNodeId} is reached by running back down the runway — " +
                $"using {corrected} along '{_rolloutExit.TaxiwayName}' instead");
            destNodeId = corrected;
            source += "+own-taxiway";
            endLateralM = AbsLateralFromRunwayMeters(_graph!.Nodes[corrected].Latitude, _graph.Nodes[corrected].Longitude,
                _rolloutRunway!.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue);
        }

        // Remember whether the aircraft actually ends up off the concrete, so the
        // arrival callout can tell the pilot the truth. At a handful of airports the
        // navdata simply has no taxiway mapped past the junction (EVRA's unnamed
        // exits, EHAM 36C/W8, EFHK 04L/WZ — one graph edge, pointing back at the
        // runway), and no routing can invent one. Saying "stop and hold position"
        // there parks a blind pilot on an active runway.
        // BOTH halves are required: lateral distance from the LANDING runway, AND
        // clear of every OTHER runway — at a runway crossing the stop point can be
        // 95 m from the landing runway's axis while sitting dead-centre on the
        // crossing one (KDTW 04R exits Y/Y4/V resolve onto 09L). Lateral-only read
        // that as vacated and told the pilot to stop and hold there.
        _landingExitOffPavement = RunwayVacateResolver.IsOffPavement(endLateralM, _rolloutRunway)
            && RunwayVacateResolver.IsClearOfOtherRunways(
                   _graph, destNodeId, _rolloutRunway, _rolloutRunwayHeadingTrue);

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
    /// runway, and decides how the countdown ends (see below).
    ///
    /// Distance to end is computed by projecting the aircraft position
    /// onto the runway centerline relative to <c>_rolloutRunway.StartLat/Lon</c>,
    /// then subtracting from the runway length. Sign convention: along-axis
    /// distance from start is positive in the runway heading direction;
    /// distToEnd = length - alongFromStart.
    ///
    /// Ends via Navigation.RunwayEndCountdownGate: "Runway vacated" once laterally
    /// clear of the runway; backtracking when STOPPED within RolloutExitGate.NearRunwayEndFeet
    /// (never on a turn there — a turn-off and a turnaround look the same), or after
    /// turning around anywhere (mid-runway it does not claim the runway ended); one
    /// notice for a mid-runway stop. On exit, _route stays null so the Taxiing branch's
    /// off-route recalc has nothing to chase.
    /// </summary>
    /// <summary>
    /// LAHSO hold-point countdown (P5). Independent of exit guidance — the hold constraint stands
    /// whether or not the exit is made, and in the runway-end countdown too, so it runs before
    /// <c>UpdateLandingRollout</c>'s countdown branch. Distances are ALONG-TRACK to the estimated hold
    /// point (the along-track invariant all rollout distance gates follow). One callout per frame via
    /// else-if.
    /// </summary>
    private void UpdateLahsoCountdown(double lat, double lon)
    {
        if (_lahsoHold == null || _lahsoPassedAnnounced) return;
        double lahsoAheadFt = -SignedAlongRunwayMeters(
            lat, lon, _lahsoHold.Latitude, _lahsoHold.Longitude, _rolloutRunwayHeadingTrue)
            * METERS_TO_FEET;
        string lahsoRwy = _lahsoHold.CrossingRunwayId;
        if (lahsoAheadFt <= 0)
        {
            AnnounceInstruction($"Warning: past the LAHSO hold point for runway {lahsoRwy}.");
            _lahsoPassedAnnounced = true;
        }
        else if (lahsoAheadFt < 500 && !_lahsoStopAnnounced)
        {
            AnnounceInstruction($"LAHSO hold point in {DistanceFormatter.FromFeet(lahsoAheadFt)}. Stop before runway {lahsoRwy}.");
            _lahsoStopAnnounced = _lahso500Announced = _lahso1500Announced = true;
        }
        else if (lahsoAheadFt < 1000 && !_lahso500Announced)
        {
            AnnounceInstruction($"LAHSO hold point in {DistanceFormatter.FromFeet(lahsoAheadFt)}. Slow down.");
            _lahso500Announced = _lahso1500Announced = true;
        }
        else if (lahsoAheadFt < 2000 && !_lahso1500Announced)
        {
            AnnounceInstruction($"LAHSO hold point, runway {lahsoRwy}, in {DistanceFormatter.FromFeet(lahsoAheadFt)}.");
            _lahso1500Announced = true;
        }
    }

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

        // How much pavement is left, through the one RunwayFrame answer so a runway row with no
        // recorded length falls back to the threshold-to-threshold distance instead of counting
        // down from zero. (_rolloutRunwayHeadingTrue is _rolloutRunway.Heading at all three rollout
        // entries, so the frame's own heading is the same one this used to project with.)
        double distToEndFt = Navigation.RunwayFrame.For(_rolloutRunway, lat)
            .DistanceToEnd(lat, lon) * METERS_TO_FEET;

        // Heading deviation from runway centerline.
        double hdgDelta = NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue);
        double hdgDeltaAbs = Math.Abs(hdgDelta);

        // How the countdown ends is decided from WHERE the aircraft is, not merely from a stop or
        // a turn (Navigation.RunwayEndCountdownGate). Any stop or 15-degree turn used to mean
        // "End of runway. Turn around." — false for a pilot turning off at a taxiway or holding for
        // ATC mid-runway (PR #236 review). "At the end" is RolloutExitGate.NearRunwayEndFeet, a
        // guidance constant of its own: it used to be read out of the SPOKEN milestone table, which
        // DistanceMilestones builds from the pilot's distance-unit setting, so the decision moved
        // when they switched between feet and metres.
        var action = Navigation.RunwayEndCountdownGate.Decide(
            distToEndFt, groundSpeedKts, hdgDeltaAbs,
            laterallyClear: !IsWithinRolloutRunwayLaterally(lat, lon),
            stoppedNoticeGiven: _rolloutStoppedNoticeGiven);

        // Every action but Continue speaks its own sentence below, which settles any owed status.
        if (action != Navigation.RunwayEndCountdownAction.Continue)
            _rolloutCountdownStatusOwed = false;

        switch (action)
        {
            case Navigation.RunwayEndCountdownAction.Vacated:
                RolloutDiag($"Runway-end countdown: laterally clear of {_rolloutRunway.RunwayID} " +
                    $"distToEnd={distToEndFt:F0}ft hdgDelta={hdgDeltaAbs:F1}deg gs={groundSpeedKts:F1}kt — vacated");
                // Stop the tone BEFORE the state change: Taxiing with a null route returns from
                // UpdatePosition before anything touches the tone, so a sounding tone would hold its
                // last pan indefinitely.
                _steeringTone.Stop();
                _rolloutNoExitMode = false;
                AnnounceInstruction("Runway vacated. No route set \u2014 use the taxi planner for a route to your stand.");
                SetState(TaxiGuidanceState.Taxiing);
                return;

            case Navigation.RunwayEndCountdownAction.BacktrackAtEnd:
                EnterBacktracking(lat, lon, atRunwayEnd: true);
                return;

            case Navigation.RunwayEndCountdownAction.BacktrackMidRunway:
                EnterBacktracking(lat, lon, atRunwayEnd: false);
                return;

            case Navigation.RunwayEndCountdownAction.StoppedMidRunwayNotice:
                _rolloutStoppedNoticeGiven = true;
                AnnounceInstruction(ComposeRunwayEndStatus(distToEndFt, stopped: true));
                return;
        }

        // Past the runway end already (overrun / off the pavement). The
        // three countdown callouts have either fired or been skipped past;
        // stay quiet here and rely on the gate above to end the countdown
        // when the pilot stops, turns around or clears the runway.
        if (distToEndFt <= 0) return;

        // All three fired — skip the per-frame table build (it allocates). Nothing
        // follows in this method, so the early return is safe.
        if (_rolloutEnd1500Announced && _rolloutEnd500Announced && _rolloutEnd100Announced)
            return;

        var rm = DistanceMilestones.RunwayEnd(); // far->near: [0]=1500ft/500m, [1]=500ft/150m, [2]=100ft/30m
        bool milestoneSpoke = false;
        if (!_rolloutEnd1500Announced && distToEndFt <= rm[0].TriggerMetres / DistanceFormatter.MetresPerFoot && distToEndFt > rm[1].TriggerMetres / DistanceFormatter.MetresPerFoot)
        {
            AnnounceInstruction($"Runway end in {rm[0].Label}.");
            _rolloutEnd1500Announced = true;
            milestoneSpoke = true;
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
            milestoneSpoke = true;
        }

        if (!_rolloutEnd100Announced && distToEndFt <= rm[2].TriggerMetres / DistanceFormatter.MetresPerFoot)
        {
            AnnounceInstruction($"Runway end in {rm[2].Label}. Stop.");
            _rolloutEnd100Announced = true;
            milestoneSpoke = true;
        }

        // The status owed when a too-fast declined exit was overshot with no exit left
        // (_rolloutCountdownStatusOwed): once, on the countdown's first frame, and only when nothing above
        // spoke — rolling short of the first milestone, the countdown would otherwise say nothing at all.
        // QUEUED, never interrupting: the overshoot can fire while "Taxiway X, too fast to turn. Slow down."
        // (4.39 s) is still being spoken - 0.7-3.75 s after it started for a pilot who stopped at the node -
        // and an interrupting status cut that warning off, possibly before "too fast to turn" was heard.
        // Queued, it follows the warning. The interrupting callouts —
        // the distance milestones above, the stopped notice and the end-of-runway / turn-around sentence —
        // outrank it and carry the same information when they come due first. Recorded for Ctrl+Y exactly
        // as AnnounceInstruction records an instruction.
        if (_rolloutCountdownStatusOwed)
        {
            _rolloutCountdownStatusOwed = false;
            if (!milestoneSpoke)
            {
                AnnounceQueuedInstruction(ComposeRunwayEndStatus(distToEndFt, stopped: false));
            }
        }
    }

    /// <summary>
    /// Pilot-commanded exit change DURING an active rollout — the tower's "turn
    /// right at Hotel" on the roll, re-picked through the landing exit planner
    /// (VATSIM gap analysis 2026-08-31, P6: previously a plan set after touchdown
    /// could only arm for the NEXT landing, so a rollout instruction had no input
    /// path). Runs the same retarget machinery the undershoot/overshoot scans
    /// use. Returns false — with nothing spoken — when guidance is not currently
    /// in LandingRollout or the pick is for a different runway; the caller then
    /// falls back to arming a normal next-landing plan.
    ///
    /// <paramref name="lahso"/> is the LAHSO ticked in the form at this pick ("hold short of 27,
    /// exit at H" on the roll). A re-pick only ever SETS one (<see cref="RepickAdoptsLahso"/>), never
    /// clears it: the planner window is rebuilt on every open with LAHSO unticked, so "none" means
    /// "not touched", and clearing on it silently dropped the planned hold (PR #324 re-review). A new
    /// hold re-arms its countdown and is named once, whichever exit the retarget ends on.
    /// </summary>
    public bool TryRetargetActiveRolloutExit(
        Navigation.LandingExit exit, string runwayId, Navigation.LahsoHold? lahso = null)
    {
        lock (_stateLock)
        {
            if (_state != TaxiGuidanceState.LandingRollout || _rolloutExit == null)
                return false;
            if (_rolloutRunway == null ||
                !RunwayDesignatorsMatch(_rolloutRunway.RunwayID, runwayId))
                return false;

            string keptName = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
                ? "the planned exit" : $"taxiway {_rolloutExit.TaxiwayName}";
            string pickedName = string.IsNullOrEmpty(exit.TaxiwayName) ? "That exit" : $"Taxiway {exit.TaxiwayName}";

            // The reciprocal end's list measures every exit from the other threshold (distance, side,
            // type), so its pick cannot steer this rollout; say so and keep the target (PR #324 review).
            if (!IsSameRunwayEnd(_rolloutRunway.RunwayID, runwayId))
            {
                SpeakNow($"That exit list is for runway {runwayId}. Rolling out on runway {_rolloutRunway.RunwayID}. Keeping {keptName}.");
                return true;
            }

            // An exit already behind the aircraft cannot be taken — say so and
            // keep the current target (same signed-along rule as the scans). The
            // pick is rejected whole: its LAHSO was validated against the rejected
            // exit, not the one kept, so it is not adopted either.
            double signedAlongM = SignedAlongRunwayMeters(
                _lastLat, _lastLon, exit.Latitude, exit.Longitude, _rolloutRunwayHeadingTrue);
            if (signedAlongM >= 0.0)
            {
                SpeakNow(
                    $"Exit {(string.IsNullOrEmpty(exit.TaxiwayName) ? "" : exit.TaxiwayName + " ")}is behind you. Keeping {keptName}.");
                return true; // handled: spoken, no next-landing plan wanted
            }

            // The pick must stop short of the LAHSO that will be in force: the one it brings, else
            // the one already active. The planner window opens with LAHSO unticked, so its own check
            // never sees an active hold, and guidance would lead through the hold point while the
            // countdown says "Stop before runway X" (PR #324 review). Same margin as the downfield
            // retarget's ExitsBeforeLahsoHold.
            bool adoptsLahso = RepickAdoptsLahso(_lahsoHold, lahso, _rolloutRunway.RunwayID, runwayId);
            var holdInForce = adoptsLahso ? lahso : _lahsoHold;
            if (holdInForce != null && !ExitStopsShortOfLahso(exit, holdInForce))
            {
                SpeakNow($"{pickedName} is past the LAHSO hold point for runway {holdInForce.CrossingRunwayId}. Keeping {keptName}.");
                return true;
            }

            string lahsoNote = "";
            if (adoptsLahso)
            {
                _lahsoHold = lahso;
                _lahso1500Announced = _lahso500Announced = _lahsoStopAnnounced = _lahsoPassedAnnounced = false;
                lahsoNote = $"Land and hold short of runway {lahso!.CrossingRunwayId}.";
            }

            int distAheadFt = (int)Math.Round(
                TaxiGraph.FastDistanceMeters(_lastLat, _lastLon, exit.Latitude, exit.Longitude)
                * METERS_TO_FEET);
            string newName = string.IsNullOrEmpty(exit.TaxiwayName)
                ? "new exit" : $"taxiway {exit.TaxiwayName}";
            RetargetLandingExit(exit, _lastLat, _lastLon, _lastHeading,
                overrideAnnouncement:
                    $"New exit, {newName}, {DistanceFormatter.FromFeet(distAheadFt)} ahead."
                    + (lahsoNote.Length > 0 ? " " + lahsoNote : ""));
            // A fall-forward to another exit speaks the ordinary retarget sentence, not the one
            // above, so the new constraint follows it queued rather than going unheard.
            if (lahsoNote.Length > 0 && _rolloutExit?.NodeId != exit.NodeId)
                AnnounceQueuedInstruction(lahsoNote);
            return true;
        }
    }

    /// <summary>
    /// Whether a mid-rollout re-pick installs its LAHSO: only a hold the pilot ticked (never "none",
    /// which is the rebuilt window's default), only for the EXACT runway being rolled out on (a hold
    /// computed for the reciprocal end is measured from the other threshold and sits past the
    /// protected runway), and only when it differs from the active one.
    /// </summary>
    internal static bool RepickAdoptsLahso(Navigation.LahsoHold? active, Navigation.LahsoHold? picked,
        string rolloutRunwayId, string pickRunwayId)
        => picked != null
           && IsSameRunwayEnd(rolloutRunwayId, pickRunwayId)
           && !SameLahsoHold(active, picked);

    /// <summary>The same runway END (never its reciprocal): exits and LAHSO holds are measured from one threshold.</summary>
    internal static bool IsSameRunwayEnd(string a, string b)
        => string.Equals(Navigation.RouteRunwayCrossings.NormalizeDesignator(a.Trim()),
                         Navigation.RouteRunwayCrossings.NormalizeDesignator(b.Trim()),
                         StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An exit the aircraft can take while honouring the LAHSO: at least 100 ft short of its hold point,
    /// the margin <see cref="ExitsBeforeLahsoHold"/> filters the downfield retarget with.
    /// </summary>
    internal static bool ExitStopsShortOfLahso(Navigation.LandingExit exit, Navigation.LahsoHold hold)
        => exit.DistanceFromThresholdFeet <= hold.StopFromThresholdFeet - 100.0;

    /// <summary>The same LAHSO constraint: both none, or the same crossing runway and hold point.</summary>
    internal static bool SameLahsoHold(Navigation.LahsoHold? a, Navigation.LahsoHold? b)
        => a == null || b == null
            ? a == null && b == null
            : string.Equals(a.CrossingRunwayId, b.CrossingRunwayId, StringComparison.OrdinalIgnoreCase)
              && Math.Abs(a.StopFromThresholdFeet - b.StopFromThresholdFeet) < 1.0;

    /// <summary>
    /// The runway-end countdown's status sentence, "Runway end in N.", led by "Stopped on runway R." for a
    /// stopped aircraft. ONE composer for the mid-runway stop notice and the status owed after a too-fast
    /// declined exit is overshot with no exit left (_rolloutCountdownStatusOwed).
    /// </summary>
    private string ComposeRunwayEndStatus(double distToEndFt, bool stopped)
        => (stopped ? $"Stopped on runway {_rolloutRunway?.RunwayID ?? "runway"}. " : "")
           + $"Runway end in {DistanceFormatter.FromFeet(Math.Max(0.0, distToEndFt))}.";

    /// <summary>
    /// Re-routes the active landing rollout to a new exit. Called when the
    /// aircraft has overshot the chosen exit and a downfield exit is available
    /// (UpdateLandingRollout's overshoot detector and UpdatePosition's
    /// post-handoff monitor), when it is too fast for the chosen exit at its
    /// turn point, and by the undershoot retarget to an earlier exit.
    ///
    /// Calls LoadRoute (re-entrant on _stateLock, safe from inside
    /// UpdateLandingRollout) to build a new route from the current position
    /// to <paramref name="newExit"/>'s node. LoadRoute transitions the
    /// manager to RouteLoaded; we force it back to LandingRollout afterward
    /// so the per-frame loop keeps invoking UpdateLandingRollout with the
    /// new exit. Approach callouts (1500 / 900 / 500 / turn-now) are re-armed
    /// for the new exit, then <see cref="AnnounceRetarget"/> retires the
    /// milestones its one sentence supersedes and speaks it.
    ///
    /// If the route to <paramref name="newExit"/> cannot be built, falls
    /// forward through every downfield exit in turn; only when all of them
    /// fail does it fall through to EnterRunwayEndCountdown, so the off-route
    /// recalc cannot fire back to the just-passed exit.
    /// </summary>
    /// <param name="reason">Why the rollout moves (Navigation.RetargetReason), which picks the sentence for
    /// every candidate the fall-forward tries: Missed (the default: an overshoot, "Missed taxiway M6.
    /// Retargeting taxiway M8, …"), TooFast (the turn point's too-fast rule, which describes the exit being
    /// left and never says "Missed") or Earlier (the undershoot retarget). An Earlier fall-forward stops
    /// SILENTLY at the exit already targeted (Navigation.RetargetCallout.StaysOnPlannedExit): that exit is
    /// still ahead, so the pilot keeps it and its route and callouts, and the exits that failed are
    /// remembered so the undershoot scan does not offer them again. Every other reason ends, when no
    /// candidate routes, in Navigation.RetargetCallout.ComposeNoReachableExit and the runway-end
    /// countdown.</param>
    /// <param name="queued">Speak the retarget AFTER whatever is being spoken instead of cutting it off: the
    /// overshoot of an exit already declined as too fast, whose warning may still be running.</param>
    /// <param name="overrideAnnouncement">The sentence to speak instead of the retarget callout when the route to
    /// <paramref name="newExit"/> itself is built — a pilot-chosen exit (TryRetargetActiveRolloutExit). A
    /// fall-forward to another exit gets the ordinary callout.</param>
    private void RetargetLandingExit(Navigation.LandingExit newExit, double lat, double lon, double headingTrue,
        Navigation.RetargetReason reason = Navigation.RetargetReason.Missed, bool queued = false,
        string? overrideAnnouncement = null)
    {
        if (_rolloutExit == null || _dataProvider == null || _graph == null)
        {
            EnterRunwayEndCountdown();
            return;
        }

        string prevTaxiwayName = _rolloutExit.TaxiwayName;
        var plannedExit = _rolloutExit;
        // A failed LoadRoute leaves the destination fields on the exit it could not reach (only its
        // reachability refusals roll back); staying on the planned exit puts them back.
        var rollback = CaptureLoadRouteRollback();

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
            if (Navigation.RetargetCallout.StaysOnPlannedExit(
                    reason, candidate.DistanceFromThresholdFeet, plannedExit.DistanceFromThresholdFeet))
                break;

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
                isRunwayDestination: false,
                // Adopted for the landing rollout: labels the crossings log line phase=touchdown.
                landingRolloutRoute: true);

            if (error == null)
            {
                // The crossing-decline ANNOUNCEMENT latch is scoped to whichever exit it last
                // fired for, not to the rollout as a whole (PR review, 2026-08-27). Retargeting
                // here to a DIFFERENT exit (e.g. the undershoot retarget below) must re-arm it:
                // if X's crossing route already declined and announced, and the graph's
                // no-runway-edges defect produces a crossing route for Y too, a stale latch
                // would silently withhold "Continue rolling to Y..." — leaving the pilot
                // stationary and silent again, for a different exit, which is exactly the
                // hazard this announcement exists to close. Deliberately NOT mirrored to
                // _rolloutCrossingDeclinedUtc (the retry floor, a few lines below the latch's
                // field declaration): that field only bounds CPU/log churn, and a stale floor
                // merely delays the new exit's first handoff attempt by under a second — cheap
                // enough that widening this reset to it too would just be an extra line with no
                // benefit. The floor and the latch protect different things; don't fold them
                // back into one reset just because they sit next to each other.
                if (_rolloutExit.NodeId != candidate.NodeId)
                    _rolloutCrossingDeclineAnnounced = false;

                _rolloutExit = candidate;
                StripVacateHoldShortsOnLandedRunway(_route);
                _isLandingExitRoute = true; // LoadRoute above cleared it; still a landing-exit route
                ResetRolloutApproachLatches();
                // Allow TryEarlyExitHandoff to fire for the newly targeted exit.
                _rolloutEarlyHandoffDone = false;

                // LoadRoute set state to RouteLoaded. Re-enter LandingRollout so
                // the next UpdatePosition frame re-runs UpdateLandingRollout.
                SetState(TaxiGuidanceState.LandingRollout);

                // The caller's reason holds for every candidate: an earlier-exit fall-forward is still an
                // earlier exit (it stops at the planned one), a missed exit's fall-forward is still that
                // miss, and a too-fast call never becomes "Missed".
                // A pilot-chosen exit (TryRetargetActiveRolloutExit) brings its own sentence — said only
                // for that exit, never for a fall-forward to another one.
                if (overrideAnnouncement != null && ReferenceEquals(candidate, newExit))
                    AnnounceInstruction(overrideAnnouncement);
                else
                    AnnounceRetarget(reason, prevTaxiwayName, candidate, lat, lon, headingTrue, queued);
                // A turn-pad exit whose only way off is a guided backtrack (LandingExitBacktrack): said
                // after the retarget sentence, queued, so it never cuts that sentence off — and kept with
                // it for Ctrl+Y.
                if (_rolloutRunway != null && PlanBacktrack(candidate, _rolloutRunway, _rolloutAllExits) is { } retargetVia)
                {
                    string backtrackNote = $"Backtrack required, to {BacktrackViaLabel(retargetVia)}.";
                    QueueSpeech(backtrackNote);
                    _lastInstruction = $"{_lastInstruction} {backtrackNote}";
                }
                return;
            }

            RolloutDiag($"RetargetLandingExit: route to '{candidate.TaxiwayName}' failed ({error}) — " +
                $"trying next downfield exit");
            _rolloutUnroutableExitNodes.Add(candidate.NodeId);
            candidate = NextDownfieldExit(candidate);
        }

        if (reason == Navigation.RetargetReason.Earlier)
        {
            RestoreLoadRouteRollback(rollback);
            RolloutDiag($"RetargetLandingExit (Earlier): no earlier exit routes — staying on " +
                $"'{plannedExit.TaxiwayName}', silently");
            return;
        }

        // Every downfield exit failed to route.
        string noExit = Navigation.RetargetCallout.ComposeNoReachableExit(reason, prevTaxiwayName);
        if (queued) AnnounceQueuedInstruction(noExit); else AnnounceInstruction(noExit);
        EnterRunwayEndCountdown();
    }

    /// <summary>
    /// The ONE utterance a retarget speaks (Navigation.RetargetCallout), with every approach milestone it
    /// supersedes retired first so none can cut it off — KMEM 36L 2026-09-26: "Missed taxiway M6.
    /// Retargeting taxiway M7, 650 feet ahead." was cut off 65 ms later by a stale "Taxiway M7, 900 feet."
    /// at 631 ft. Same retirement rules as the touchdown correction (TouchdownCallout.RetireExitCallouts)
    /// with the measured lead of the sentence actually spoken (Navigation.RetargetCallout.Retire). Turn-now is
    /// never retired here: "now" belongs to its own point, where the too-fast rule judges it. "Straighten."
    /// per RolloutExitGate.ShouldStraightenAfterRetarget.
    /// </summary>
    private void AnnounceRetarget(Navigation.RetargetReason reason, string previousTaxiwayName,
        Navigation.LandingExit exit, double lat, double lon, double headingTrue, bool queued = false)
    {
        int distAheadFt = (int)Math.Round(
            TaxiGraph.FastDistanceMeters(lat, lon, exit.Latitude, exit.Longitude) * METERS_TO_FEET);

        double hdgDelta = NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue);
        double exitRelBearing = Navigation.RolloutExitGate.ExitRelativeBearingDeg(
            exit.ExitBearingTrue, _rolloutRunwayHeadingTrue);
        bool pastNewExit = SignedAlongRunwayMeters(
            lat, lon, exit.Latitude, exit.Longitude, _rolloutRunwayHeadingTrue) > 0.0;
        double turnWindowFeet = RolloutExitTurnWindowFeet();
        bool straighten = reason != Navigation.RetargetReason.Earlier
            && Navigation.RolloutExitGate.ShouldStraightenAfterRetarget(
                   hdgDelta, exitRelBearing, distAheadFt, pastNewExit, turnWindowFeet);

        var xm = DistanceMilestones.ExitApproach(); // far->near: [0]=1500ft/500m, [1]=900ft/300m, [2]=500ft/150m
        var retired = Navigation.RetargetCallout.Retire(
            reason, straighten, distAheadFt, _lastGroundSpeedKts, exit.ExitType,
            xm[0].TriggerMetres / DistanceFormatter.MetresPerFoot,
            xm[1].TriggerMetres / DistanceFormatter.MetresPerFoot,
            xm[2].TriggerMetres / DistanceFormatter.MetresPerFoot,
            ROLLOUT_TURN_NOW_FT,
            Navigation.RolloutExitGate.SlowDownAboveKts(exit.ExitAngleDegrees, exit.ExitType));
        if (retired.Retire1500) _rolloutApproach1500Announced = true;
        if (retired.Retire900) _rolloutApproach900Announced = true;
        if (retired.Retire500) _rolloutApproach500Announced = true;

        RolloutDiag($"Retarget ({reason}) '{previousTaxiwayName}' -> '{exit.TaxiwayName}' dist={distAheadFt}ft " +
            $"gs={_lastGroundSpeedKts:F1}kt hdgDelta={hdgDelta:+0.0;-0.0}deg window={turnWindowFeet:F0}ft " +
            $"straighten={straighten} lead={Navigation.RetargetCallout.LeadSecondsFor(reason, straighten, retired.SlowDown):F1}s " +
            $"retire1500={retired.Retire1500} retire900={retired.Retire900} " +
            $"retire500={retired.Retire500} slowDown={retired.SlowDown} queued={queued}");

        string sentence = Navigation.RetargetCallout.Compose(
            reason, previousTaxiwayName, exit.TaxiwayName, distAheadFt, straighten, retired.SlowDown);
        if (queued) AnnounceQueuedInstruction(sentence); else AnnounceInstruction(sentence);
    }

    /// <summary>
    /// The exit to retarget to after an overshoot of <see cref="_rolloutExit"/> - the one rule for BOTH
    /// overshoot sites, the LandingRollout detector and the post-handoff monitor, which had its own copy
    /// that measured "downfield" from the missed exit alone and never asked the graph. Downfield is
    /// measured from the aircraft as well as the missed exit (RolloutExitGate.DownfieldCutoffFeet): a
    /// rapid exit is declared missed up to 500 ft past it, and a cutoff from the exit alone offers a
    /// turnoff that is already behind the wing ("Retargeting taxiway Y, 50 feet ahead" with the tone
    /// panning back at it). When the planned list has nothing left, the graph is asked before any "Missed
    /// last exit": that list comes from GetLandingExits, built for the planner DIALOG, which keeps one
    /// entry per taxiway name and leaves a name's later turnoff out wherever another listed exit stands
    /// within 1,400 ft of it. It once kept no unmarked junction at all once the runway had one hold-short
    /// marker, so a marked crossing near the threshold hid every rapid exit behind it (CYYZ 23, 2026-08-23:
    /// six exits ending at the missed one, with 5,400 ft of runway and three turnoffs ahead). Either
    /// verdict is logged with what was considered. Null when nothing is ahead.
    /// </summary>
    /// <param name="signedAlongPastFt">The aircraft's along-track position relative to the missed exit
    /// (positive = past it).</param>
    /// <param name="site">Log tag of the calling site.</param>
    private Navigation.LandingExit? PickOvershootRetarget(double signedAlongPastFt, string site)
    {
        double downfieldCutoffFt = Navigation.RolloutExitGate.DownfieldCutoffFeet(
            _rolloutExit!.DistanceFromThresholdFeet, signedAlongPastFt, ROLLOUT_OVERSHOOT_FT);

        var nextExit = Navigation.RolloutExitGate.FirstSuitableDownfieldExit(ExitsBeforeLahsoHold(), downfieldCutoffFt);
        if (nextExit == null && _graph != null && _rolloutRunway != null)
        {
            var rescued = _graph.FindDownfieldExits(_rolloutRunway, downfieldCutoffFt);
            if (rescued.Count > 0)
            {
                RolloutDiag($"{site} planned list exhausted \u2014 graph rescan found " +
                    $"{rescued.Count}: {DescribeExits(rescued)}");
                _rolloutAllExits = Navigation.RolloutExitGate.MergeRescueExits(_rolloutAllExits, rescued);
                nextExit = Navigation.RolloutExitGate.FirstSuitableDownfieldExit(ExitsBeforeLahsoHold(), downfieldCutoffFt);
            }
        }

        if (nextExit != null)
            RolloutDiag($"{site} retarget to next exit: name='{nextExit.TaxiwayName}' " +
                $"distFromThr={nextExit.DistanceFromThresholdFeet:F0}ft cutoff={downfieldCutoffFt:F0}ft");
        else
            // Genuinely nothing ahead. Record what was rejected, so a report of this can be answered from
            // the log instead of guessed at - the CYYZ verdict was reached inside a loop that wrote down
            // nothing about what it looked at.
            RolloutDiag($"{site} no downfield exit past {downfieldCutoffFt:F0}ft -> " +
                $"EnterRunwayEndCountdown; considered {DescribeExits(_rolloutAllExits)}");
        return nextExit;
    }

    /// <summary>
    /// The rollout's exits a retarget may choose from: all of them, or with a land-and-hold-short
    /// constraint (LAHSO, VATSIM gap analysis P5) only those short of the hold point — the
    /// constraint outranks exit convenience, so a missed exit never retargets the pilot past it.
    /// </summary>
    private List<Navigation.LandingExit> ExitsBeforeLahsoHold()
        => _lahsoHold == null
            ? _rolloutAllExits
            : _rolloutAllExits.Where(e => ExitStopsShortOfLahso(e, _lahsoHold)).ToList();

    /// <summary>
    /// The overshoot verdict with no way off ahead: "Missed last exit on runway X.", then the runway-end
    /// countdown, which clears the route so the off-route recalc has nothing to chase.
    /// </summary>
    private void AnnounceMissedLastExit()
    {
        string rwyLabel = _rolloutRunway != null && !string.IsNullOrEmpty(_rolloutRunway.RunwayID)
            ? _rolloutRunway.RunwayID
            : "this runway";
        AnnounceInstruction($"Missed last exit on runway {rwyLabel}.");
        EnterRunwayEndCountdown();
    }

    /// <summary>
    /// First exit in <see cref="_rolloutAllExits"/> downfield of
    /// <paramref name="afterExit"/> (beyond it by ROLLOUT_OVERSHOOT_FT) that is
    /// not a greater-than-90-degree turn. Null when none remain. Same
    /// suitability rule as the overshoot next-exit scan.
    /// </summary>
    private Navigation.LandingExit? NextDownfieldExit(Navigation.LandingExit afterExit)
        => Navigation.RolloutExitGate.FirstSuitableDownfieldExit(
            ExitsBeforeLahsoHold(),
            afterExit.DistanceFromThresholdFeet + ROLLOUT_OVERSHOOT_FT);


    /// <summary>
    /// The exit a too-fast pilot is told to continue to: the first suitable exit downfield of the one just
    /// declined that the aircraft can slow down for with COMFORTABLE braking, judged for that exit's own
    /// angle, preferring one mapped clear of the runway (RolloutExitGate.FirstComfortableDownfieldExit — the
    /// touchdown re-plan's comfortable pass). The list is screened for that first
    /// (Navigation.LandingExitVacateScreen): a landing on the planned runway starts from a fresh
    /// GetLandingExits list whose every exit carries VacatesRunway's optimistic default.
    /// Only when none is, today's rule: the first one at least RolloutExitGate.ExitLeadFeet ahead of the
    /// aircraft (the undershoot scan's lead, tuned below 50 kt, which above about 60 kt could pick an exit
    /// itself too fast at its own turn point — a cascade of too-fast retargets). The graph rescue scan is
    /// the fallback exactly as the overshoot path uses it.
    /// </summary>
    /// <param name="signedAlongPastFt">The aircraft's along-track position relative to the declined exit
    /// (positive = past it), as UpdateLandingRollout computed it. The exact projection, never the exit's
    /// distance minus the straight-line range: beside a laterally offset node that puts the aircraft up to
    /// about 100 ft further back than it is, and the lead with it.</param>
    private Navigation.LandingExit? FindTooFastAlternative(double signedAlongPastFt, double groundSpeedKts)
    {
        double aircraftFromThresholdFt = _rolloutExit!.DistanceFromThresholdFeet + signedAlongPastFt;
        double pastDeclinedFt = _rolloutExit.DistanceFromThresholdFeet + ROLLOUT_OVERSHOOT_FT;
        double cutoffFt = Math.Max(
            pastDeclinedFt,
            aircraftFromThresholdFt + Navigation.RolloutExitGate.ExitLeadFeet(groundSpeedKts));
        Navigation.LandingExitVacateScreen.Mark(_graph, _rolloutAllExits, _rolloutRunway);
        var next = PickTooFastAlternative(pastDeclinedFt, aircraftFromThresholdFt, cutoffFt, groundSpeedKts);
        int rescuedCount = 0;
        if (next == null && _graph != null && _rolloutRunway != null)
        {
            var rescued = _graph.FindDownfieldExits(_rolloutRunway, cutoffFt);
            rescuedCount = rescued.Count;
            if (rescued.Count > 0)
            {
                RolloutDiag($"Too fast: planned list exhausted - graph rescan found {rescued.Count}: {DescribeExits(rescued)}");
                Navigation.LandingExitVacateScreen.Mark(_graph, rescued, _rolloutRunway);
                _rolloutAllExits = Navigation.RolloutExitGate.MergeRescueExits(_rolloutAllExits, rescued);
                next = PickTooFastAlternative(pastDeclinedFt, aircraftFromThresholdFt, cutoffFt, groundSpeedKts);
            }
        }
        // What the scan judged, so a "no reachable exit" can be answered from the log, not guessed at.
        string scanOutcome = next != null
            ? $"'{next.TaxiwayName}' at {next.DistanceFromThresholdFeet:F0}ft"
            : $"none; considered {DescribeExits(_rolloutAllExits)}";
        RolloutDiag($"Too fast: alternative scan gs={groundSpeedKts:F1}kt aircraftFromThr={aircraftFromThresholdFt:F0}ft " +
            $"pastDeclined={pastDeclinedFt:F0}ft leadCutoff={cutoffFt:F0}ft rescued={rescuedCount} -> {scanOutcome}");
        return next;
    }

    /// <summary>FindTooFastAlternative's pick over the current exit list: the comfortable-lead exit first,
    /// else the first beyond the ExitLeadFeet cutoff.</summary>
    private Navigation.LandingExit? PickTooFastAlternative(
        double pastDeclinedFt, double aircraftFromThresholdFt, double cutoffFt, double groundSpeedKts)
        => Navigation.RolloutExitGate.FirstComfortableDownfieldExit(
               ExitsBeforeLahsoHold(), pastDeclinedFt, aircraftFromThresholdFt, groundSpeedKts)
           ?? Navigation.RolloutExitGate.FirstSuitableDownfieldExit(ExitsBeforeLahsoHold(), cutoffFt);

    /// <summary>
    /// Ends landing-exit guidance with the aircraft OFF the runway, and picks the closure
    /// reason: only claim "short of X" when an early vacate was actually established, so a
    /// pilot who turned off AT or BEYOND their exit is never told they left the runway short
    /// of it. Both closures say "Stop and hold position", which is safe only off the pavement
    /// — on it, use <see cref="ConcludeLandingExitOnRunway"/>.
    ///
    /// <para>ONE owner for the whole conclude sequence (reason split → clear the handoff flag
    /// → Taxiing → HandleArrival). It was hand-copied at four guard sites, whose identity
    /// [ROL-27] requires and which nothing enforced; this method is that enforcement. Note
    /// the two <c>TryEarlyExitHandoff</c> sites can only ever take the
    /// <c>_landingExitRouteUnreachable</c> arm — that method's <c>LoadRoute</c> nulls the
    /// planned-exit name and never restores it — and calling the shared rule from there
    /// anyway is the point: one rule, not two that happen to agree today.</para>
    /// </summary>
    private void ConcludeLandingExitOffRunway()
    {
        if (_landingExitVacatedEarlyPlannedName != null)
            _landingExitVacatedEarly = true;
        else
            _landingExitRouteUnreachable = true;
        FinishLandingExitConclude();
    }

    /// <summary>
    /// Ends landing-exit guidance with the aircraft still ON the pavement. Steers
    /// <see cref="HandleArrival"/> to its final branch — the only closure that does not say
    /// "hold": "You may still be on the runway — continue ahead until clear". Telling a blind
    /// pilot to stop and hold on an active runway is the hazard that branch exists to prevent.
    ///
    /// <para>The other two flags are CLEARED rather than left alone so the ordering in
    /// <see cref="HandleArrival"/> cannot be defeated by a stale value. Both are provably
    /// false here (every setter of either requires the aircraft to be off the runway, and each
    /// concludes on the frame it sets), so clearing them can only remove a stale claim, never a
    /// real one. <c>_landingExitMissed</c> is deliberately untouched: it is set only from the
    /// Taxiing branch of <c>UpdatePosition</c>, which concludes to Arrived immediately, so it
    /// can never be live here.</para>
    /// </summary>
    private void ConcludeLandingExitOnRunway()
    {
        _landingExitVacatedEarly = false;
        _landingExitRouteUnreachable = false;
        _landingExitOffPavement = false;
        FinishLandingExitConclude();
    }

    /// <summary>
    /// The tail both closures share. <c>_rolloutHandoffActive</c> is cleared FIRST: the top of
    /// the handoff block arms the post-handoff overshoot monitor on the assumption the handoff
    /// ends in Taxiing, and a conclude is not that.
    /// </summary>
    private void FinishLandingExitConclude()
    {
        _rolloutHandoffActive = false;
        SetState(TaxiGuidanceState.Taxiing);
        HandleArrival();
    }

    /// <summary>
    /// Whether the route the handoff is about to steer at re-crosses the runway just landed
    /// on, judged from <c>_currentSegmentIndex</c> onward (a crossing already behind the
    /// aircraft is history, not a route it is about to fly).
    ///
    /// <para>ONE chokepoint for the verdict, so the two handoff sites cannot drift on WHICH
    /// segments, cursor or runway they judge — the responses they take differ legitimately,
    /// the question does not. This method was the last ungated handoff site once before
    /// (commit 29b8bcbf), and a future third site should call this rather than re-derive it.</para>
    /// </summary>
    /// <param name="lat">The aircraft's live latitude — see the aircraft-prepend note above.</param>
    /// <param name="lon">The aircraft's live longitude.</param>
    private bool HandoffRouteReCrossesLandingRunway(double lat, double lon)
        => _route != null
           && Navigation.RolloutRunwayReCrossing.RouteReCrossesRunway(
                  _route.Segments,
                  _currentSegmentIndex,
                  Navigation.RolloutRunwayReCrossing.FindLandingRunwayCenterline(
                      _graph?.RunwayCenterlines, _rolloutRunway?.RunwayID),
                  new Navigation.RouteRunwayCrossings.AircraftPosition(lat, lon));

    /// <summary>
    /// "Turn left" / "Gentle right" for the currently targeted landing exit, from the
    /// aircraft's own position and heading. ONE owner for that wording, so the turn-now
    /// callout and the crossing-decline utterance that can RETIRE it (see the decline branch
    /// in <c>UpdateLandingRollout</c>) can never drift apart — a folded direction that
    /// contradicted the standalone cue would be worse than either alone.
    /// <para>Callers must have passed <c>UpdateLandingRollout</c>'s null-exit guard.</para>
    /// </summary>
    private string ComposeExitTurnPhrase(double lat, double lon, double headingTrue)
    {
        // Direction, from the exit itself and never from where the aircraft happens to be: the
        // junction -> vacate-node chord and then ExitBearingTrue (ResolveExitTurnDirection — the path
        // the handoff and the tone actually take), then the side the exit is listed on. The bearing
        // from the aircraft to the exit's node is NOT a fallback: for a node on the centreline its
        // sign is the aircraft's own tracking error (KDTW 22L: 5 of 13 junctions within a metre of
        // the centreline). With no direction known the word is dropped — "Turn now, taxiway X" —
        // because a confident wrong side is far worse for a blind pilot than none.
        string? dir = ExitTurnDirectionWord();
        if (dir == null) return "Turn";
        // < 20°: chord taxiways and shallow curved-RET entries need a small
        // initial input, not a committed turn — "gentle" prevents over-rotation.
        // ≥ 20°: genuine RETs and normal exits warrant a deliberate turn input.
        string turnWord = _rolloutExit!.ExitAngleDegrees < 20.0 ? "Gentle" : "Turn";
        return $"{turnWord} {dir}";
    }

    /// <summary>
    /// Compact "name@distance" rendering of an exit list for the rollout diagnostic. The
    /// missed-exit verdict is only reviewable after the fact if the log says what was on the
    /// table when it was reached.
    /// </summary>
    internal static string DescribeExits(IReadOnlyList<Navigation.LandingExit>? exits)
    {
        if (exits == null || exits.Count == 0) return "[]";
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < exits.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var e = exits[i];
            sb.Append(string.IsNullOrEmpty(e.TaxiwayName) ? "?" : e.TaxiwayName)
              .Append('@').Append(e.DistanceFromThresholdFeet.ToString("F0"))
              .Append('/').Append(e.ExitAngleDegrees.ToString("F0")).Append("deg");
        }
        return sb.Append(']').ToString();
    }

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
            // The connection node must itself be CLEAR of every runway corridor.
            // Taxi graphs deliberately carry nodes ON runway pavement (exit
            // junction nodes, HS/IHS markers sit within half-width + 15 m of the
            // axis — the landing-exit corridor walk is built on exactly that), and
            // during a backtrack those on-centerline nodes lie straight behind at
            // angleDiff ≈ 0, beating every real apron node on BOTH score terms.
            // The old unfiltered scan then drove UpdateBacktracking to announce
            // "Taxiway ahead. Vacate runway." and — 25 m from that on-runway node
            // — "Runway vacated." with the aircraft dead-centre on the pavement:
            // a false safety claim followed by total silence. A node that clears
            // this filter means "25 m away" is genuinely near pavement's edge, and
            // the handoff's own clearance gate (UpdateBacktracking) covers the
            // rest. If NO node in the cone clears, the honest no-connection
            // message fires instead — better than a confident wrong claim.
            if (!IsClearOfAllRunwayCorridors(node.Latitude, node.Longitude)) continue;
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
    /// Enters <see cref="TaxiGuidanceState.BacktrackingOnRunway"/> from the runway-end countdown:
    /// at the runway end (<paramref name="atRunwayEnd"/>: stopped, or turned around, within
    /// RolloutExitGate.NearRunwayEndFeet — never merely turning there) or after turning around
    /// mid-runway, as decided by
    /// <see cref="Navigation.RunwayEndCountdownGate"/>. Announces the MAGNETIC backtrack heading —
    /// saying "End of runway" only when that is true — and begins steering-tone guidance on the
    /// true reciprocal runway heading.
    /// </summary>
    private void EnterBacktracking(double lat, double lon, bool atRunwayEnd)
    {
        if (_rolloutRunway == null)
        {
            _rolloutNoExitMode = false;
            SetState(TaxiGuidanceState.Taxiing);
            return;
        }

        double reciprocalHdg = (_rolloutRunwayHeadingTrue + 180.0) % 360.0;
        _backtrackHeadingTrue = reciprocalHdg;

        // The spoken heading is MAGNETIC, the instrument the pilot turns to; the tone keeps
        // steering on the true reciprocal above (PR #236 review: KSEA 34L spoke 180 for 165).
        int hdgInt = Navigation.RunwayHeadings.SpokenReciprocalMagnetic(_rolloutRunway.HeadingMag);

        // A planned backtrack to a known exit (the chosen exit was a turn pad) steers
        // to THAT exit's turn-off instead of the nearest taxiway connection.
        string? targetedInstruction = TryBeginTargetedBacktrack(lat, lon, hdgInt);

        TaxiNode? conn = targetedInstruction != null
            ? null
            : FindBacktrackConnectionNode(lat, lon, reciprocalHdg);
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

        if (targetedInstruction != null)
        {
            AnnounceInstruction(targetedInstruction);
            return;
        }
        string rwyId = _rolloutRunway.RunwayID ?? "runway";
        AnnounceInstruction(atRunwayEnd
            ? $"End of runway {rwyId}. Turn around, heading {hdgInt}. Backtracking."
            : $"Backtracking on runway {rwyId}, heading {hdgInt}.");
    }

    /// <summary>
    /// Per-frame logic while in <see cref="TaxiGuidanceState.BacktrackingOnRunway"/>.
    /// Uses precision runway-lineup thresholds (silent ±0.5°, active ±1°). The tone
    /// is NEVER silent during the turnaround: while heading is >90° from the
    /// backtrack target it pans hard to whichever side the pilot commits the 180°,
    /// then settles to fine steering as they straighten onto the reciprocal heading.
    /// The connection-node leg is <see cref="Navigation.BacktrackConnectionHandoff"/>'s: inside its
    /// announce window the taxiway is named with its side and the tone swings onto the node's bearing,
    /// and the backtrack hands off when the aircraft is clear of every runway corridor or within
    /// <see cref="Navigation.BacktrackConnectionHandoff.HandoffMetres"/> of the node, whichever first.
    /// </summary>
    private void UpdateBacktracking(double lat, double lon, double headingTrue, double groundSpeedKts)
    {
        // Steer on the CENTERLINE, not on heading alone. A heading-only law reads 0°
        // for an aircraft taxiing parallel to the runway on the grass beside it —
        // LGZA 16, 2026-08-31: the pilot flew the 180° wide, came out 320 m east of
        // the pavement, and backtracked the whole way on grass with the tone silent,
        // because his heading matched the reciprocal exactly. Same intercept model as
        // UpdateBacktrackDeparture (and runway lineup): cross-track relative to the
        // landed runway's centerline (its threshold end is on the axis) adds up to
        // ±30° of intercept, so off the pavement the tone leans back toward it and
        // silence again means "on the centerline heading the right way". The
        // connection-node handoff below is unchanged.
        double centerlineError = _rolloutRunway != null
            ? BacktrackToneHeadingError(
                lat, lon, headingTrue,
                _rolloutRunway.StartLat, _rolloutRunway.StartLon, _backtrackHeadingTrue)
            : NormalizeAngle(_backtrackHeadingTrue - headingTrue);

        // The taxiway connection node, when this backtrack has one (never a targeted backtrack, which
        // steers at its own exit). distM is -1 without one.
        bool haveNode = !_backtrackTargeted && _backtrackConnectionNodeId > 0;
        double distM = haveNode
            ? TaxiGraph.FastDistanceMeters(lat, lon, _backtrackConnectionLat, _backtrackConnectionLon)
            : -1.0;
        double bearingToNode = haveNode
            ? NavigationCalculator.CalculateBearing(lat, lon, _backtrackConnectionLat, _backtrackConnectionLon)
            : double.NaN;

        // The connection-node leg is Navigation.BacktrackConnectionHandoff's (taxi-landing-port review,
        // finding 1). Once the aircraft has come round onto the backtrack heading and the node is inside
        // the announce window, the taxiway is named WITH ITS SIDE and the tone leaves the centreline law
        // for the bearing to the node, the way the Normal-exit tone does after "turn now". CYYZ 05,
        // 2026-10-03: FindBacktrackConnectionNode's corridor filter put the node on taxiway H, 70-88 m
        // off the 05/23 centreline; the tone kept steering the centreline, "Taxiway ahead. Vacate
        // runway." named no side, and the 25 m hand-off was never reached — the pilot passed the node
        // abeam at 69 m and the distance climbed for good. The filter fixed the false "vacated" claim
        // and removed the only way to reach the node; this is the way back to it.
        if (haveNode
            && !_backtrackApproachAnnounced
            && Math.Abs(centerlineError) <= 90.0
            && Navigation.BacktrackConnectionHandoff.InApproachWindow(distM))
        {
            _backtrackApproachAnnounced = true;
            string? side = Navigation.BacktrackConnectionHandoff.SideWord(bearingToNode, _backtrackHeadingTrue);
            string name = _graph?.PreferredTaxiwayNameAt(_backtrackConnectionNodeId) ?? "";
            AnnounceInstruction(Navigation.BacktrackConnectionHandoff.ComposeApproach(name, side));
            // The tone steps onto the node's bearing WITH the words, not through the low-pass: reset
            // the smoother so the step is heard where it is said.
            _headingErrorInitialized = false;
            RolloutDiag($"Backtrack connection node {_backtrackConnectionNodeId} '{name}' announced at " +
                $"{distM:F0} m, side={side ?? "unspoken"}, brg={bearingToNode:F1}, centerlineErr={centerlineError:F1}");
        }

        double headingError = haveNode
            ? Navigation.BacktrackConnectionHandoff.ToneHeadingError(
                _backtrackApproachAnnounced, bearingToNode, headingTrue, centerlineError)
            : centerlineError;
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
            headingError, _smoothedHeadingError, distM);

        if (_backtrackTargeted)
        {
            UpdateTargetedBacktrack(lat, lon, headingTrue, absError);
            return;
        }

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

        // The hand-off. "Runway vacated." is a safety claim and must be TRUE when spoken: the
        // connection node is off every runway corridor (filtered in FindBacktrackConnectionNode),
        // but the aircraft itself can still be on the pavement edge when it reaches it. Once the
        // approach has been announced, being clear of every corridor hands off as vacated wherever
        // the aircraft is (the node is passed abeam, not driven over, when the taxiway meets the
        // runway at an angle); within HandoffMetres of the node the backtrack hands off regardless,
        // into the ended-on-runway clearing phase when still on pavement — the tone keeps steering
        // ahead toward the taxiway and the honest "Off the runway. Stop and hold position." closure
        // fires only once the aircraft is laterally clear of every corridor
        // (UpdateArrivedRunwayClearing, same machinery as a landing-exit route that ends on pavement).
        switch (Navigation.BacktrackConnectionHandoff.Decide(
                    distM, _backtrackApproachAnnounced, IsClearOfAllRunwayCorridors(lat, lon)))
        {
            case Navigation.BacktrackHandoffAction.Vacated:
                // Stop the tone BEFORE the state change. Taxiing with a null route returns from
                // UpdatePosition before anything touches the tone, so a tone left sounding here
                // never gets another heading-error update: it holds its last pan for as long as
                // the pilot keeps taxiing — reported from CYYZ as "stuck in the right ear", 68
                // seconds of it in the log. The no-connection-node branch above has always
                // stopped the tone for exactly this reason; the corridor filter must not lose it.
                _steeringTone.Stop();
                // Say what state the pilot is now in. _route is null, so a status query answers
                // "No route loaded." — which is true but reads as a fault unless they were told.
                AnnounceInstruction("Runway vacated. No route set — use the taxi planner for a route to your stand.");
                RolloutDiag($"Backtrack hand-off: vacated at {distM:F0} m from the connection node");
                SetState(TaxiGuidanceState.Taxiing);
                break;

            case Navigation.BacktrackHandoffAction.ClearingAhead:
                AnnounceInstruction("Taxiway reached. Continue ahead until clear of the runway.");
                _arrivedRunwayClearing = true;
                _arrivedClearingBearingDeg = bearingToNode;
                _arrivedClearingStartLat = 0.0;   // stamped on the first clearing frame
                _arrivedClearingStartLon = 0.0;
                _headingErrorInitialized = false;
                _smoothedHeadingError = 0.0;
                // The backtrack tone is still live — UpdateArrivedRunwayClearing
                // resumes/drives it per frame; no re-Start needed.
                RolloutDiag($"Backtrack hand-off: at the connection node ({distM:F0} m) still on pavement -> clearing phase");
                SetState(TaxiGuidanceState.Arrived);
                // MainForm stops the position feed on Arrived (its StateChanged handler runs inside
                // SetState), and UpdateArrivedRunwayClearing is driven from that feed. Without this
                // the clearing phase never received a frame and "Continue ahead until clear" was the
                // last thing said (taxi-landing-port review, finding 3). Raised AFTER the state
                // change so the restart is not undone by the handler's stop.
                PositionStreamRequired?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Backtrack steering law shared by the post-landing backtrack
    /// (<see cref="UpdateBacktracking"/>) and the full-length backtrack departure
    /// (<see cref="UpdateBacktrackDeparture"/>): the tone heading error is the
    /// difference between the aircraft heading and the reciprocal runway heading
    /// PLUS a cross-track intercept — up to ±30°, square-root shaped over the
    /// lineup deadband/saturation band — measured against the centerline through
    /// <paramref name="refLat"/>/<paramref name="refLon"/> (a point on the runway
    /// axis) in the backtrack direction. Left of the centerline steers right and
    /// vice versa, exactly as runway lineup does; on the centerline it degrades to
    /// the plain heading error.
    /// </summary>
    private static double BacktrackToneHeadingError(
        double lat, double lon, double headingTrue,
        double refLat, double refLon, double reciprocalHdgTrue)
    {
        var track = RunwayCenterlineTracker.Compute(
            lat, lon, headingTrue, refLat, refLon, reciprocalHdgTrue);

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
        return NormalizeAngle(desiredHeadingTrue - headingTrue);
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
        // Shared with the post-landing backtrack (UpdateBacktracking).
        double toneHeadingError = BacktrackToneHeadingError(
            lat, lon, headingTrue, _lineupTargetLat, _lineupTargetLon, reciprocalHdgTrue);

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

    /// <summary>
    /// Switches the active rollout into runway-end countdown mode: after an
    /// overshoot with no downfield exit available, after a retarget failure
    /// mid-rollout, or at touchdown from BeginRunwayEndCountdownRollout when no
    /// usable exit exists on the runway actually landed on. Clears `_route` and
    /// `_destinationNodeId` so
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
    private void EnterRunwayEndCountdown()
    {
        _route = null;
        _destinationNodeId = 0;
        _currentSegmentIndex = 0;
        _originalTaxiwaySequence = null;
        _userHoldShortIndices = null;
        _userRunwayHoldShorts = null;
        _userTaxiwayHoldShorts = null;
        _rolloutExit = null;
        _isLandingExitRoute = false; // no exit route — runway-end countdown
        _landingExitOffPavement = true;
        // KEEP _rolloutRunway and _rolloutRunwayHeadingTrue — countdown needs them.
        _rolloutAllExits = new List<Navigation.LandingExit>();
        ResetRolloutApproachLatches();
        _rolloutEnd1500Announced = false;
        _rolloutEnd500Announced = false;
        _rolloutEnd100Announced = false;
        // LAHSO milestone latches reset here too (four-reset-site invariant).
        // _lahsoHold itself is kept: the constraint still stands during the
        // countdown; it is cleared only at StopGuidance / the next Begin.
        _lahso1500Announced = _lahso500Announced = _lahsoStopAnnounced = _lahsoPassedAnnounced = false;
        _rolloutStoppedNoticeGiven = false;
        // Defence in depth, matching the _rolloutEnd*Announced resets above: setting
        // _rolloutNoExitMode below makes UpdateLandingRollout divert into
        // UpdateRunwayEndCountdown before the handoff block can be reached at all, so
        // this cannot currently be stale here — but the rollout latches are reset at all
        // four sites by rule, not by whichever of them happens to be reachable today.
        _rolloutCrossingDeclinedUtc = DateTime.MinValue;
        _rolloutCrossingDeclineAnnounced = false;
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
    /// <summary>Arms the entry-stub phase of a runway lineup (see <see cref="_lineupStubActive"/>).</summary>
    private void BeginLineupStub()
    {
        _lineupStubActive = false;
        _lineupStubRunway = null;
        if (!_isRunwayLineup || _route?.RunwayEntryStub is not { Count: >= 2 } || _graph == null) return;
        string dest = _destinationName ?? "";
        if (dest.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)) dest = dest.Substring(7).Trim();
        var cl = RouteRunwayCrossings.FindCenterlineForDesignator(_graph.RunwayCenterlines, dest);
        if (cl == null) return;
        _lineupStubRunway = RunwayShape.For(cl);
        _lineupStubActive = true;
    }

    /// <summary>
    /// The heading to fly along the entry stub, or null once the stub phase is over — the aircraft
    /// is on the runway's pavement, has left the stub (&gt; 25 m), or is at its end. Over is final.
    /// (Handing over before the edge was measured and is worse: starting the turn 12 m early cut
    /// the corner on angled entries — 59 new findings for 24 fixed.)
    /// </summary>
    private double? LineupStubHeading(double lat, double lon)
    {
        if (!_lineupStubActive) return null;
        var stub = _route?.RunwayEntryStub;
        if (stub is not { Count: >= 2 } || _lineupStubRunway == null
            || _lineupStubRunway.Contains(lat, lon, 0.0)
            || LandingExitPathFollow.DistanceToPathMeters(stub, lat, lon) > LINEUP_STUB_MAX_OFF_M
            || TaxiGraph.FastDistanceMeters(lat, lon, stub[^1].Lat, stub[^1].Lon) < LINEUP_STUB_END_M)
        {
            _lineupStubActive = false;
            return null;
        }
        var lats = stub.Select(p => p.Lat).ToArray();
        var lons = stub.Select(p => p.Lon).ToArray();
        int seg = 0; double best = double.MaxValue;
        for (int i = 0; i < stub.Count - 1; i++)
        {
            double d = LandingExitPathFollow.DistanceToPathMeters(new[] { stub[i], stub[i + 1] }, lat, lon);
            if (d < best) { best = d; seg = i; }
        }
        var (tLat, tLon) = GuidanceGeometry.WalkTarget(lats, lons, seg, lat, lon, LINEUP_STUB_LOOKAHEAD_M);
        return NavigationCalculator.CalculateBearing(lat, lon, tLat, tLon);
    }

    private const double LINEUP_STUB_MAX_OFF_M = 25.0;
    private const double LINEUP_STUB_END_M = 8.0;
    private const double LINEUP_STUB_LOOKAHEAD_M = 15.0;

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
            // Still on the entry taxiway short of the runway: follow it onto the pavement first.
            if (LineupStubHeading(lat, lon) is double stubHeading) desiredHeadingTrue = stubHeading;
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
                SpeakNow($"Lined up, {_destinationName}. Hold position.");

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
                    _lineupHugeCrossTrackSince = MSFSBlindAssist.Utils.SimClock.UtcNow;
                else if (!_runwayLineupUnreachableWarned &&
                         (MSFSBlindAssist.Utils.SimClock.UtcNow - _lineupHugeCrossTrackSince).TotalSeconds >= LINEUP_UNREACHABLE_SEC)
                {
                    _runwayLineupUnreachableWarned = true;
                    SpeakNow(
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
                    SpeakNow(_dockingPending
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
    /// An interpolated landing_exit.log line, formatted with the invariant culture
    /// (Utils.Logging.InvariantLogLine): "gs=44.1kt", never the "gs=44,1kt" a German or Turkish Windows
    /// writes, so a log reads and parses the same from every pilot. C# binds every interpolated argument
    /// here and every plain literal to the string overload; a conditional between two interpolated
    /// strings is typed string first and misses it, so format its parts into the one interpolation.
    /// </summary>
    private static void RolloutDiag(ref MSFSBlindAssist.Utils.Logging.InvariantLogLine msg)
        => RolloutDiag(msg.ToStringAndClear());

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
        var now = MSFSBlindAssist.Utils.SimClock.UtcNow;
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
