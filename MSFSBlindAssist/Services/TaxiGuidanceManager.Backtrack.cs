using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

// Guided backtrack to a planned exit — for a landing exit whose only way off the runway
// is to turn round and backtrack (LandingExitBacktrack: a turn pad / runway-end loop).
//
// Motivating case — LROP 08R (2026-09-13): the last "D" is a loop at the runway end that
// never leaves the pavement; the way off is D at 7,793 ft. Picking it used to end with
// "continue ahead until clear of the runway", which a loop cannot satisfy.
//
// Flow: the rollout runs to the chosen exit as usual. Once the via exit is BEHIND the
// aircraft and the pilot turns, stops, or reaches the turn point at taxi speed, guidance
// enters BacktrackingOnRunway (the proven centreline-steering backtrack, never silent)
// with a TARGET: the point on the runway where the route to the via exit's vacate
// destination leaves the centreline. 150 ft before it, the route is built from the live
// position and guidance hands to Taxiing with "Turn right now, taxiway D", finishing with
// the normal landing-exit arrival wording.
//
// Never engages on a rapid exit: LandingExitBacktrack only ever returns a via for an END
// exit from which no way off the runway pavement exists without backtracking.
public partial class TaxiGuidanceManager
{
    // Planned at rollout start (and on retarget): the exit to backtrack to, its vacate
    // destination, and whether that destination is off the pavement.
    private LandingExit? _plannedBacktrackVia;
    private int _plannedBacktrackDestNodeId;
    private bool _plannedBacktrackOffPavement = true;

    // Live targeted-backtrack state (BacktrackingOnRunway with a target).
    private bool _backtrackTargeted;
    private double _backtrackTurnoffLat, _backtrackTurnoffLon;
    private string? _backtrackTurnWord;

    /// <summary>Latest a targeted backtrack hands to the route — the rollout's own turn-now distance.</summary>
    private const double BACKTRACK_TURNOFF_LEAD_M = ROLLOUT_TURN_NOW_FT * 0.3048;

    /// <summary>A route node within this of the centreline still counts as "on the runway axis".</summary>
    private const double BACKTRACK_ON_AXIS_M = 3.0;

    private void ResetPlannedBacktrack()
    {
        _plannedBacktrackVia = null;
        _plannedBacktrackDestNodeId = 0;
        _plannedBacktrackOffPavement = true;
        _backtrackTargeted = false;
        _backtrackTurnWord = null;
    }

    /// <summary>
    /// Works out, for the exit the rollout is heading for, whether it needs a backtrack and
    /// where to — and caches the via exit's vacate destination, because the rollout's exit
    /// list is gone by the time the backtrack runs. Returns the via exit, or null.
    /// </summary>
    private LandingExit? PlanBacktrack(LandingExit exit, Runway runway, IReadOnlyList<LandingExit> allExits)
    {
        ResetPlannedBacktrack();
        if (_graph == null || exit.ExitType != "End") return null;

        var verdicts = new Dictionary<LandingExit, bool>();
        bool Vacates(LandingExit e)
        {
            if (!verdicts.TryGetValue(e, out bool v))
                verdicts[e] = v = LandingExitBacktrack.Vacates(_graph, e, allExits, runway);
            return v;
        }

        var via = LandingExitBacktrack.FindVia(_graph, runway, exit, allExits, Vacates);
        if (via == null) return null;

        int dest = LandingExitDestination.Resolve(_graph, via, allExits, runway, runway.Heading,
            out _, out double endLateralM, out string src);
        _plannedBacktrackVia = via;
        _plannedBacktrackDestNodeId = dest;
        _plannedBacktrackOffPavement = RunwayVacateResolver.IsOffPavement(endLateralM, runway)
            && RunwayVacateResolver.IsClearOfOtherRunways(_graph, dest, runway, runway.Heading);
        RolloutDiag($"PlanBacktrack: exit '{exit.TaxiwayName}' at {exit.DistanceFromThresholdFeet:F0}ft needs a backtrack " +
            $"→ via '{via.TaxiwayName}' at {via.DistanceFromThresholdFeet:F0}ft dest={src}:{dest} offPavement={_plannedBacktrackOffPavement}");
        return via;
    }

    private static string BacktrackViaLabel(LandingExit via)
        => string.IsNullOrEmpty(via.TaxiwayName) ? "the previous exit" : $"taxiway {via.TaxiwayName}";

    /// <summary>
    /// Called from UpdateLandingRollout before its normal handoff: when a backtrack is
    /// planned, the via exit is behind, and the pilot has turned, stopped, or reached the
    /// turn point at taxi speed, start the backtrack instead of routing into the turn pad.
    /// </summary>
    private bool TryStartPlannedBacktrack(double lat, double lon, double groundSpeedKts,
                                          bool turnBegun, bool speedNearExitHandoff, bool pastExit, bool atTaxiSpeed)
    {
        if (_plannedBacktrackVia == null || _rolloutRunway == null) return false;
        bool viaBehind = SignedAlongRunwayMeters(lat, lon,
            _plannedBacktrackVia.Latitude, _plannedBacktrackVia.Longitude, _rolloutRunwayHeadingTrue) > 0.0;
        if (!viaBehind) return false;

        bool stopped = groundSpeedKts < ROLLOUT_NO_EXIT_STOPPED_GS_KTS;
        if (!(turnBegun || stopped || speedNearExitHandoff || (pastExit && atTaxiSpeed))) return false;

        RolloutDiag($"Planned backtrack starting: turnBegun={turnBegun} stopped={stopped} " +
            $"turnPoint={speedNearExitHandoff} pastExit={pastExit} gs={groundSpeedKts:F1}");
        // Same clean-down as a no-exit rollout (route, exit and list go; the runway stays),
        // then straight into the backtrack — EnterBacktracking picks the plan up.
        EnterRunwayEndCountdown();
        EnterBacktracking(lat, lon, atRunwayEnd: false);
        return true;
    }

    /// <summary>
    /// Sets up a TARGETED backtrack when one is planned and its via exit is behind the
    /// aircraft. Returns the spoken instruction, or null to fall back to the generic
    /// backtrack (nearest taxiway connection).
    /// </summary>
    private string? TryBeginTargetedBacktrack(double lat, double lon, int reciprocalHdgMag)
    {
        var via = _plannedBacktrackVia;
        if (via == null || _graph == null || _rolloutRunway == null || _plannedBacktrackDestNodeId <= 0)
            return null;
        if (SignedAlongRunwayMeters(lat, lon, via.Latitude, via.Longitude, _rolloutRunwayHeadingTrue) <= 0.0)
            return null;   // not behind us — nothing to backtrack to

        double reciprocal = _backtrackHeadingTrue;
        _backtrackTurnWord = null;
        bool haveTurnoff = false;

        // Where does the route to the via exit's destination leave the runway? The last
        // on-axis node before its first off-pavement node. From the far end that is often
        // NOT the via exit's own junction (LROP: the route to D leaves by the leg meeting
        // the runway at ~8,360 ft, not the 08R junction at 7,793 ft).
        var start = _graph.FindNearestNode(lat, lon);
        var path = start == null ? null : new TaxiRouter(_graph).FindShortestPath(start.NodeId, _plannedBacktrackDestNodeId);
        if (path != null && path.Segments.Count > 0)
        {
            var nodes = new List<TaxiNode> { path.Segments[0].FromNode };
            foreach (var s in path.Segments) nodes.Add(s.ToNode);

            int firstOff = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                double l = AbsLateralFromRunwayMeters(nodes[i].Latitude, nodes[i].Longitude,
                    _rolloutRunway.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue);
                if (RunwayVacateResolver.IsOffPavement(l, _rolloutRunway)) { firstOff = i; break; }
            }
            if (firstOff > 0)
            {
                int turnoff = firstOff - 1;
                for (int i = firstOff - 1; i >= 0; i--)
                {
                    double l = AbsLateralFromRunwayMeters(nodes[i].Latitude, nodes[i].Longitude,
                        _rolloutRunway.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue);
                    if (l <= BACKTRACK_ON_AXIS_M) { turnoff = i; break; }
                }
                var t = nodes[turnoff];
                // Must lie ahead in the backtrack direction, or the route is not a backtrack.
                if (SignedAlongRunwayMeters(lat, lon, t.Latitude, t.Longitude, reciprocal) < 0.0)
                {
                    _backtrackTurnoffLat = t.Latitude;
                    _backtrackTurnoffLon = t.Longitude;
                    haveTurnoff = true;
                    var next = nodes[turnoff + 1];
                    double brg = NavigationCalculator.CalculateBearing(t.Latitude, t.Longitude, next.Latitude, next.Longitude);
                    double delta = NormalizeAngle(brg - reciprocal);
                    if (Math.Abs(delta) >= EXIT_TURN_DIRECTION_MIN_DEG)
                        _backtrackTurnWord = delta < 0 ? "left" : "right";
                }
            }
        }
        if (!haveTurnoff)
        {
            // No usable graph route along the runway (many sceneries leave the runway out
            // of the taxi network): aim at the via exit's own junction.
            _backtrackTurnoffLat = via.Latitude;
            _backtrackTurnoffLon = via.Longitude;
        }

        _backtrackTargeted = true;
        RolloutDiag($"Targeted backtrack to '{via.TaxiwayName}': turnoff=({_backtrackTurnoffLat:F6},{_backtrackTurnoffLon:F6}) " +
            $"fromRoute={haveTurnoff} turn={_backtrackTurnWord ?? "unspoken"} dest={_plannedBacktrackDestNodeId}");
        return $"Turn around, heading {reciprocalHdgMag}. Backtrack to {BacktrackViaLabel(via)}.";
    }

    /// <summary>Per-frame targeted-backtrack handoff; the centreline tone is driven by UpdateBacktracking.</summary>
    private void UpdateTargetedBacktrack(double lat, double lon, double headingTrue, double absHeadingError)
    {
        var via = _plannedBacktrackVia;
        if (via == null) { _backtrackTargeted = false; return; }

        // Still swinging round: nothing to announce or hand off yet.
        if (absHeadingError > 90.0) return;

        double aheadM = -SignedAlongRunwayMeters(lat, lon, _backtrackTurnoffLat, _backtrackTurnoffLon, _backtrackHeadingTrue);

        if (!_backtrackApproachAnnounced && aheadM <= BACKTRACK_TAXI_ANNOUNCE_M)
        {
            _backtrackApproachAnnounced = true;
            string side = _backtrackTurnWord != null ? $", on the {_backtrackTurnWord}" : "";
            string name = BacktrackViaLabel(via);
            AnnounceInstruction($"{char.ToUpperInvariant(name[0])}{name[1..]} ahead{side}. Slow down.");
        }

        if (aheadM > BACKTRACK_TURNOFF_LEAD_M) return;

        string destName = via.TaxiwayName.Length > 0 ? $"Taxiway {via.TaxiwayName}" : "exit taxiway";
        string? err = _dataProvider == null || _graph == null
            ? "no graph"
            : LoadRoute(
                _dataProvider, _icao,
                lat, lon, headingTrue,
                _plannedBacktrackDestNodeId,
                destName,
                taxiwaySequence: null,
                prebuiltGraph: _graph,
                announceSummary: false,
                startTaxiwayName: via.TaxiwayName.Length > 0 ? via.TaxiwayName : null,
                // Handed off while still rolling on the landed runway: a landing-rollout route
                // (the pass refuses a start hold within the clear margin of any runway).
                landingRolloutRoute: true);

        bool offPavement = _plannedBacktrackOffPavement;
        string turnWord = _backtrackTurnWord != null ? $"Turn {_backtrackTurnWord} now" : "Turn now";
        _backtrackTargeted = false;
        _plannedBacktrackVia = null;

        if (err == null)
        {
            StripVacateHoldShortsOnLandedRunway(_route);
            _isLandingExitRoute = true;             // LoadRoute cleared it; landing-exit arrival wording
            _landingExitOffPavement = offPavement;  // LoadRoute reset it; keep the real verdict
            _headingErrorInitialized = false;
            _smoothedHeadingError = 0.0;
            RolloutDiag($"Targeted backtrack handoff OK at ahead={aheadM:F0}m → dest={_route?.Segments.Count} segs");
            AnnounceInstruction($"{turnWord}, {BacktrackViaLabel(via)}.");
            SetState(TaxiGuidanceState.Taxiing);
            _steeringTone.Resume();
        }
        else
        {
            RolloutDiag($"Targeted backtrack handoff re-route FAILED ({err})");
            _steeringTone.Stop();
            AnnounceInstruction($"{turnWord}, {BacktrackViaLabel(via)}. Route unavailable — use the taxi planner.");
            SetState(TaxiGuidanceState.Taxiing);
        }
    }
}
