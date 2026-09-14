using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Pre-selects a runway exit taxiway during cruise/descent and auto-activates taxi
/// guidance from the touchdown point to the chosen exit as soon as the aircraft
/// lands.
///
/// The pilot selects an exit before touchdown via the LandingExitForm. The planner
/// holds that selection and watches ground state + ground speed; the first airborne→
/// on-ground transition above LANDING_MIN_GS_KNOTS is treated as touchdown, and the
/// planner immediately routes from the current aircraft position to the chosen exit
/// node through TaxiGuidanceManager.
///
/// Does NOT depend on which runway the SIM reports — many airports don't expose a
/// runway id via SimConnect on the ground. It does, however, check the touchdown
/// position and heading against the planned runway's own geometry before handing that
/// runway to the rollout as a measurement frame: the exit's lat/lon and node id are
/// enough to ROUTE to it from any touchdown point, but the ROLLOUT measures distance
/// along, and lateral offset from, the planned runway, and a frame that does not
/// describe the aircraft produces nonsense rather than a degraded answer. See
/// <see cref="Navigation.LandingRunwayMatch"/>.
/// </summary>
public class LandingExitPlanner
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TaxiGuidanceManager _guidanceManager;

    // Pending selection (captured at planning time, lives until activation)
    private string? _icao;
    private Runway? _runway;
    private LandingExit? _exit;
    private TaxiGraph? _graph;
    private IAirportDataProvider? _dataProvider;

    // Touchdown detection state
    private bool _wasAirborne;
    private bool _activatedThisLanding;

    // Minimum ground speed at on-ground transition for it to count as a real landing
    // rather than a teleport or taxi-onto-ground. Light aircraft touch down around
    // 50 kt; below 40 kt is almost always a taxi start or teleport reload.
    private const double LANDING_MIN_GS_KNOTS = 40.0;

    // Diagnostic log so we can see why activation didn't fire when only the
    // "On ground" callout was heard at touchdown. Lives with every other MSFSBA
    // log in the canonical AppLogs folder (%APPDATA%\MSFSBlindAssist\logs).
    // Shared with TaxiGuidanceManager's rollout diag + MainForm.Announcers' — all
    // now serialize through the same LogChannel/LogWriter instead of racing on
    // File.AppendAllText.
    private static readonly LogChannel _diagLog = Log.Channel("landing_exit");

    private static void DiagLog(string msg)
    {
        try { _diagLog.Info(msg); }
        catch { }
    }

    public LandingExitPlanner(ScreenReaderAnnouncer announcer, TaxiGuidanceManager guidanceManager)
    {
        _announcer = announcer;
        _guidanceManager = guidanceManager;
    }

    public bool HasPendingExit => _exit != null && _runway != null && _icao != null && _graph != null;

    public string? PendingIcao => _icao;
    public Runway? PendingRunway => _runway;
    public LandingExit? PendingExit => _exit;

    /// <summary>
    /// Captures a landing-exit selection. Call when the pilot picks an exit in the form.
    /// Keeps a reference to the pre-built graph so activation doesn't have to rebuild it.
    /// </summary>
    public void SetExit(
        IAirportDataProvider dataProvider,
        string icao,
        Runway runway,
        LandingExit exit,
        TaxiGraph graph,
        bool currentlyAirborne = true)
    {
        _dataProvider = dataProvider;
        _icao = icao;
        _runway = runway;
        _exit = exit;
        _graph = graph;
        _activatedThisLanding = false;

        // Arm the touchdown edge detector based on the aircraft's CURRENT
        // air/ground state. Setting it to true unconditionally is wrong:
        // a pilot who plans an exit while ON THE GROUND (planning the next
        // landing while still taxiing in from the previous, or planning
        // during an unusual ground-pause) would have _wasAirborne=true while
        // actually grounded. Then the next ground-state event (still on
        // ground) would meet (_wasAirborne && !_activatedThisLanding &&
        // HasPendingExit && GS≥40), which during a high-speed taxi or
        // rejected-takeoff could false-trigger activation. Honoring the
        // actual state:
        //   - Currently airborne (typical: planning during descent):
        //     _wasAirborne=true → activates on touchdown as expected.
        //   - Currently on ground: _wasAirborne=false → activation requires
        //     a future airborne→ground edge (i.e., a real landing). The
        //     pilot can still plan; activation just waits for next takeoff
        //     and land cycle.
        _wasAirborne = currentlyAirborne;

        DiagLog($"SetExit icao={icao} runway={runway.RunwayID} exit='{exit.TaxiwayName}' " +
                $"node={exit.NodeId} currentlyAirborne={currentlyAirborne} _wasAirborne={_wasAirborne} " +
                $"HasPendingExit={HasPendingExit}");

        string dist = DistanceFormatter.FromFeet(exit.DistanceFromThresholdFeet, round: false);
        string name = string.IsNullOrEmpty(exit.TaxiwayName) ? "unnamed taxiway" : $"taxiway {exit.TaxiwayName}";
        _announcer.Announce(
            $"Landing exit planned: {name} at {icao} runway {runway.RunwayID}, " +
            $"{dist} from threshold. Guidance will auto-start on touchdown.");
    }

    /// <summary>Clears any pending selection without activating.</summary>
    public void Clear()
    {
        bool had = HasPendingExit;
        DiagLog($"Clear called (had pending exit: {had})");
        _icao = null;
        _runway = null;
        _exit = null;
        _graph = null;
        _dataProvider = null;
        _activatedThisLanding = false;
        // Also reset the airborne-edge tracker so any latent "true" from before
        // the clear can't trick the next plan into firing on a stale ground bit.
        _wasAirborne = false;
        if (had) _announcer.Announce("Landing exit plan cleared.");
    }

    /// <summary>
    /// Feeds the airborne/on-ground state. Call on every SIM_ON_GROUND update (or,
    /// equivalently, whenever a position update arrives and the ground bit is known).
    /// Returns true if this call triggered guidance activation.
    /// </summary>
    public bool ProcessGroundState(bool onGround, double groundSpeedKnots,
        double lat, double lon, double headingTrue)
    {
        DiagLog($"ProcessGroundState onGround={onGround} gs={groundSpeedKnots:F1} " +
                $"lat={lat:F6} lon={lon:F6} hdgTrue={headingTrue:F1} " +
                $"_wasAirborne={_wasAirborne} _activatedThisLanding={_activatedThisLanding} " +
                $"HasPendingExit={HasPendingExit}");

        // Any airborne sample arms the touchdown edge detector. Ground samples
        // (even with the bit momentarily flickering) never arm it, so a teleport
        // or reload with onGround=true doesn't falsely set _wasAirborne.
        if (!onGround)
        {
            _wasAirborne = true;
            return false;
        }

        // On ground now. Was airborne before? Require GS ≥ LANDING_MIN_GS_KNOTS
        // so taxi-onto-ground transitions and low-speed teleports are rejected
        // as landings.
        if (_wasAirborne && !_activatedThisLanding && HasPendingExit &&
            groundSpeedKnots >= LANDING_MIN_GS_KNOTS)
        {
            // Try to activate. _wasAirborne is cleared ONLY on success. On
            // failure we leave it set so a brief ground-bit flicker (oleo
            // bounce on the ground sensor at touchdown, common on hard
            // landings) or a true airborne→ground→airborne→ground bounce
            // re-enters this branch and retries. _activatedThisLanding
            // inside ActivateGuidance guards against successful double-fire.
            bool activated = ActivateGuidance(lat, lon, headingTrue);
            if (activated)
                _wasAirborne = false;
            return activated;
        }

        return false;
    }

    private bool ActivateGuidance(double lat, double lon, double headingTrue)
    {
        if (_exit == null || _runway == null || _icao == null ||
            _graph == null || _dataProvider == null)
        {
            DiagLog($"ActivateGuidance EARLY-RETURN: " +
                    $"_exit={(_exit == null ? "null" : "set")} " +
                    $"_runway={(_runway == null ? "null" : "set")} " +
                    $"_icao={(_icao == null ? "null" : _icao)} " +
                    $"_graph={(_graph == null ? "null" : "set")} " +
                    $"_dataProvider={(_dataProvider == null ? "null" : "set")}");
            return false;
        }
        DiagLog($"ActivateGuidance starting: icao={_icao} runway={_runway.RunwayID} " +
                $"exit='{_exit.TaxiwayName}' node={_exit.NodeId} " +
                $"from lat={lat:F6} lon={lon:F6} hdgTrue={headingTrue:F1}");

        // Is the plan's runway the runway this aircraft is actually on? Asked BEFORE any
        // routing, because the answer can change which runway the rollout is measured in.
        //
        // The planner's runway combo defaults to the first runway at the airport, so a pilot
        // who plans an exit without changing it plans against a runway they may not land on.
        // At OMDB that default is 12L, and two landings on 30L (2026-09-06 and 2026-09-12,
        // issue #234) handed the 12L frame to the rollout: hdgDelta came out at 179 degrees,
        // signedAlongPast at +4,570 ft and lateral at 1,263 ft, so the first update concluded
        // pastExit AND exitedLaterally and handed straight off to Taxiing — the pilot heard
        // taxiway names instead of the touchdown and exit callouts they had planned for.
        string? touchdownPrefix = null;
        var runways = _dataProvider.GetRunways(_icao);
        var match = LandingRunwayMatch.Evaluate(lat, lon, headingTrue, _runway, runways);
        DiagLog($"Runway check: planned={_runway.RunwayID} hdgRwy={_runway.Heading:F1} " +
                $"acftHdg={headingTrue:F1} verdict={match.Verdict} " +
                $"actual={match.Actual?.RunwayID ?? "-"}");

        if (match.Verdict == LandingRunwayVerdict.DifferentRunway && match.Actual != null)
        {
            // The chosen exit belongs to pavement the aircraft is not on, so there is no exit
            // to steer to and nothing to re-measure. Say so and give the pilot the one thing
            // that is still true and still needed at 160 kt — how much runway is left.
            // AnnounceImmediate, not Announce: this line REPLACES the touchdown callout on this
            // path (nothing else speaks here), and it is the only thing telling the pilot why
            // the plan they made is not running. Queued, a ground-speed callout could bury it.
            _announcer.AnnounceImmediate(
                $"Touchdown. Landing exit plan was for runway {_runway.RunwayID}, but you are " +
                $"on runway {match.Actual.RunwayID}. Exit plan cancelled. Runway end countdown only.");
            DiagLog($"ActivateGuidance: plan runway {_runway.RunwayID} does not match " +
                    $"{match.Actual.RunwayID} — runway-end countdown on the actual runway");
            _guidanceManager.BeginRunwayEndCountdownRollout(match.Actual, match.Actual.Heading);
            _activatedThisLanding = true;
            return true;
        }

        if (match.Verdict == LandingRunwayVerdict.ReciprocalEnd && match.Actual != null)
        {
            // Same pavement, other direction. The pilot's exit taxiway is still the right
            // taxiway — only its distance from the threshold changes — so swap the frame to
            // the end actually being rolled down and carry on with full exit guidance.
            DiagLog($"ActivateGuidance: plan runway {_runway.RunwayID} is the reciprocal of " +
                    $"{match.Actual.RunwayID} — re-framing the rollout on the landed end");
            // NOT spoken here. The touchdown callout goes out immediately after this through
            // AnnounceImmediate, which discards the queue, so a line spoken now would be
            // thrown away before the pilot heard it. It rides WITH the touchdown callout
            // instead — one utterance, the house remedy for this repeat failure.
            touchdownPrefix =
                $"Landing exit plan was for runway {_runway.RunwayID}; you are on runway " +
                $"{match.Actual.RunwayID}.";
            _runway = match.Actual;

            // Re-measure the pilot's exit from the end actually being rolled down. A
            // LandingExit's DistanceFromThresholdFeet and ExitAngleDegrees are both
            // DIRECTION-dependent, and the angle is not cosmetic: ExitType is derived from
            // it, the exit-turn gate opens at ExitAngleDegrees * 0.7, and
            // TryEarlyExitHandoff fires only below 50 degrees — so a 30-degree rapid-exit
            // turnoff read from the far end is really 150, and keeping the stale figure
            // would arm the early handoff on a 90-degree turn. Same pavement, same node id,
            // new numbers.
            var reFramed = _graph.GetLandingExits(_runway)
                                 .FirstOrDefault(e => e.NodeId == _exit.NodeId);
            if (reFramed != null)
            {
                DiagLog($"Re-framed exit '{_exit.TaxiwayName}' node={_exit.NodeId}: " +
                        $"distFromThr {_exit.DistanceFromThresholdFeet:F0} -> " +
                        $"{reFramed.DistanceFromThresholdFeet:F0} ft, angle " +
                        $"{_exit.ExitAngleDegrees:F0} -> {reFramed.ExitAngleDegrees:F0} deg, " +
                        $"type '{_exit.ExitType}' -> '{reFramed.ExitType}'");
                _exit = reFramed;
            }
            else
            {
                // GetLandingExits is lossy BY DESIGN (one entry per taxiway name, and the
                // geometric fallback switches off for a whole runway once any corridor node
                // carries a hold-short marker), so an absent entry does not mean the taxiway
                // is unusable from this end. Keep the pilot's exit: the rollout's callouts,
                // tone and overshoot logic all read its geometry directly.
                DiagLog($"Re-frame: '{_exit.TaxiwayName}' node={_exit.NodeId} is not listed " +
                        $"for {_runway.RunwayID} — keeping the planned exit as-is");
            }
        }

        // Route from current position to the exit node, unconstrained (no pilot-entered
        // taxiway sequence — just shortest path). The route will follow the runway centerline
        // through the exit node's graph path naturally.
        //
        // announceSummary:false suppresses the normal "Taxi to ... via ..." callout
        // so the pilot only hears a single touchdown-specific line during the high-
        // workload rollout moment. StartGuidance also announces "Taxiway X. Steering
        // guidance active" once — that one's useful because it confirms which
        // taxiway the tone is currently aligning to.
        string? error = _guidanceManager.LoadRoute(
            _dataProvider, _icao,
            lat, lon, headingTrue,
            _exit.NodeId,
            $"Taxiway {(_exit.TaxiwayName.Length > 0 ? _exit.TaxiwayName : "exit")}",
            taxiwaySequence: null,
            userHoldShortIndices: null,
            destinationHeading: null,
            destinationThresholdLat: _exit.Latitude,
            destinationThresholdLon: _exit.Longitude,
            destinationHeadingTrue: null,
            isRunwayDestination: false,
            prebuiltGraph: _graph,
            announceSummary: false);

        // Compute the full exit list once — used by both the success and no-route
        // fallback paths below. GetLandingExits returns exits sorted by
        // DistanceFromThresholdFeet ascending, which is what the overshoot scan needs.
        var allExits = _graph.GetLandingExits(_runway);

        if (error != null)
        {
            DiagLog($"ActivateGuidance LoadRoute failed: {error} — entering rollout with exit geometry (no taxi route)");

            // The taxi graph is disconnected at this airport: the exit's component
            // has no nodes near the touchdown zone, so A* can't build a route from
            // here. BUT the rollout distance callouts (1500/500/150 ft), steering
            // tone, and overshoot logic all use exit geometry directly — not the
            // route. Enter rollout mode with the exit set so the pilot still gets
            // full guidance. At handoff (turnBegun / exitedLaterally), LoadRoute is
            // retried from the live near-exit position, which IS in the exit's
            // component, so that re-route succeeds and normal taxi guidance follows.
            _guidanceManager.BeginLandingRolloutNoGraph(
                _exit, _runway.Heading, _runway, allExits, lat, lon,
                SettingsManager.Current, touchdownPrefix);

            _activatedThisLanding = true;
            return true;
        }

        DiagLog($"ActivateGuidance LoadRoute OK, calling StartGuidance");
        _activatedThisLanding = true;
        _guidanceManager.StartGuidance(SettingsManager.Current);

        // Switch into landing-rollout mode: tone is paused, distance-based
        // callouts ("approaching high-speed exit Sierra-5, 1500 feet" /
        // "...500 feet, slow down" / "turn left now, taxiway Sierra-5")
        // fire on the rollout. State transitions to normal Taxiing once
        // the aircraft decelerates to taxi speed AND is within
        // ROLLOUT_NEAR_EXIT_FT of the chosen exit, or once the pilot
        // begins the actual turn off the runway. On overshoot the
        // manager retargets to the next downfield exit (or falls through
        // to idle Taxiing if none remain) using the allExits list passed
        // through here.
        // Runway.Heading is true heading per the DB schema; pass it
        // through so the rollout can detect when the pilot starts the
        // turn off centerline.
        _guidanceManager.BeginLandingRollout(
            _exit, _runway.Heading, _runway, allExits, lat, lon, touchdownPrefix);
        return true;
    }
}
