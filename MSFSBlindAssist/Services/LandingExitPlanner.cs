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
/// runway id via SimConnect on the ground. It does check the touchdown position and
/// heading against the airport's runway geometry (<see cref="Navigation.LandingRunwayMatch"/>)
/// before the planned runway becomes the rollout's measurement frame (issue #234):
/// on another runway, or the other end, it re-plans an exit on the runway actually
/// landed on (<see cref="Navigation.LandingExitReplan"/>) and says so in the touchdown
/// sentence; with no usable exit there it runs the runway-end countdown; with no
/// aligned runway under the aircraft it gives no guidance for THAT landing and keeps
/// the plan set for the next one. The pilot's stored plan is never modified.
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
    // Every runway end at the planned airport, closed ones included, captured at planning time
    // (the form already loaded it to build the graph), so touchdown needs no database query.
    private IReadOnlyList<Runway> _airportRunways = Array.Empty<Runway>();

    // Touchdown detection state
    private bool _wasAirborne;
    private bool _activatedThisLanding;
    // "Runway not identified" has been said for THIS plan. The plan itself survives that verdict
    // (LandingExitActivationPolicy), so without this latch a bounce or a touch-and-go would repeat
    // the message on every touchdown the app could not place.
    private bool _unidentifiedRunwayAnnounced;

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
    /// Keeps a reference to the pre-built graph so activation doesn't have to rebuild it,
    /// and the airport's runway list so activation can tell which runway it landed on.
    /// </summary>
    public void SetExit(
        IAirportDataProvider dataProvider,
        string icao,
        Runway runway,
        LandingExit exit,
        TaxiGraph graph,
        IReadOnlyList<Runway> airportRunways,
        bool currentlyAirborne = true)
    {
        _dataProvider = dataProvider;
        _icao = icao;
        _runway = runway;
        _exit = exit;
        _graph = graph;
        _airportRunways = airportRunways ?? Array.Empty<Runway>();
        _activatedThisLanding = false;
        _unidentifiedRunwayAnnounced = false;

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
                $"node={exit.NodeId} runways={_airportRunways.Count} currentlyAirborne={currentlyAirborne} " +
                $"_wasAirborne={_wasAirborne} HasPendingExit={HasPendingExit}");

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
        _airportRunways = Array.Empty<Runway>();
        _activatedThisLanding = false;
        _unidentifiedRunwayAnnounced = false;
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
            bool activated = ActivateGuidance(lat, lon, headingTrue, groundSpeedKnots);
            if (activated)
                _wasAirborne = false;
            return activated;
        }

        return false;
    }

    private bool ActivateGuidance(double lat, double lon, double headingTrue, double groundSpeedKnots)
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
                $"from lat={lat:F6} lon={lon:F6} hdgTrue={headingTrue:F1} gs={groundSpeedKnots:F1}");

        // Is the plan's runway the runway this aircraft is on? Asked BEFORE any routing, because the
        // answer decides which runway the rollout is measured in. Issue #234: a 12L plan and a 30L
        // landing at OMDB handed the rollout a frame reversed 179 degrees, which handed off to
        // Taxiing on its first frame, twice.
        var match = LandingRunwayMatch.Evaluate(lat, lon, headingTrue, _runway, _airportRunways);
        DiagLog($"Runway check: planned={_runway.RunwayID} hdgRwy={_runway.Heading:F1} " +
                $"acftHdg={headingTrue:F1} runways={_airportRunways.Count} verdict={match.Verdict} " +
                $"actual={match.Actual?.RunwayID ?? "-"}");

        switch (match.Verdict)
        {
            case LandingRunwayVerdict.Matches:
                // The planned runway: exactly the original flow — no correction, no re-plan.
                return StartExitGuidance(_runway, _exit, _graph.GetLandingExits(_runway),
                    lat, lon, headingTrue, groundSpeedKnots, correction: null);

            case LandingRunwayVerdict.ReciprocalEnd:
            case LandingRunwayVerdict.DifferentRunway:
                return ReplanOnActualRunway(match, lat, lon, headingTrue, groundSpeedKnots);

            default:
                // No aligned runway contains the aircraft (a stale plan at another airport, or a
                // runway the navdata lacks). The planned frame provably does not describe this
                // landing, and a rollout measured in it says things like "left the runway short of
                // the exit, stop and hold position" at landing speed — so no guidance this time.
                //
                // The plan SURVIVES (LandingExitActivationPolicy): nothing was started, so nothing
                // was used up, and this verdict rests on one position sample taken at the instant
                // the wheels touched. A bounce, a touch-and-go or a go-around used to fly the next
                // approach with no exit guidance at all and no second word about why. Returning
                // true still clears the airborne edge, so the message cannot repeat as the aircraft
                // rolls; the latch keeps it to once per plan across later touchdowns.
                if (LandingExitActivationPolicy.AnnouncesUnidentifiedRunway(
                        match.Verdict, _unidentifiedRunwayAnnounced))
                {
                    _announcer.AnnounceImmediate(LandingExitActivationPolicy.UnidentifiedRunwayMessage);
                    _unidentifiedRunwayAnnounced = true;
                }
                DiagLog("ActivateGuidance: no aligned runway contains the aircraft — no guidance, plan kept");
                _activatedThisLanding = LandingExitActivationPolicy.ConsumesPlan(match.Verdict);
                return true;
        }
    }

    /// <summary>
    /// The aircraft is on another runway, or the other end of the planned one: choose an exit on the
    /// runway actually landed on and start guidance to it, or run the runway-end countdown when none
    /// is usable. The runway and exit used here exist only for this landing.
    /// </summary>
    private bool ReplanOnActualRunway(LandingRunwayResult match, double lat, double lon,
                                      double headingTrue, double groundSpeedKnots)
    {
        Runway actual = match.Actual!;
        Runway planned = _runway!;
        LandingExit plannedExit = _exit!;
        TaxiGraph graph = _graph!;
        var correction = new TouchdownRunwayCorrection(actual.RunwayID, planned.RunwayID);

        // Measured from the landing runway's own landing threshold — the same reference
        // GetLandingExits uses for DistanceFromThresholdFeet (runway start + ThresholdOffset).
        double aircraftFromThresholdFt =
            RunwayFrame.For(actual, lat).Along(lat, lon) / 0.3048 - actual.ThresholdOffset;
        // Only the other end of the SAME runway can still offer the pilot's own taxiway.
        LandingExit? reciprocalPlanned = match.Verdict == LandingRunwayVerdict.ReciprocalEnd ? plannedExit : null;

        // An exit the aircraft can slow down for comfortably first; the reachability floor only when
        // there is none, even after the rescue scan (LandingExitReplan).
        List<LandingExit> exits = graph.GetLandingExits(actual);
        // Ask, for each one, whether the taxiways past it actually lead clear of THIS runway — the
        // same screen the planner dialog shows the pilot. These exits were built for a runway the
        // pilot never selected, so nothing had asked, and every one arrived flagged as fine.
        LandingExitVacateScreen.Mark(graph, exits, actual);
        var choice = LandingExitReplan.ChooseExit(exits, reciprocalPlanned,
            plannedExit.DistanceFromThresholdFeet, aircraftFromThresholdFt, groundSpeedKnots,
            LandingExitLeadTier.Comfortable);

        bool rescued = false;
        List<LandingExit>? rescue = null;
        if (choice.Exit == null)
        {
            // GetLandingExits is lossy by design; ask the graph directly before giving up, as the
            // rollout's missed-exit handler does.
            rescue = graph.FindDownfieldExits(actual,
                aircraftFromThresholdFt + RolloutExitGate.ExitLeadFeet(groundSpeedKnots));
            if (rescue.Count > 0)
            {
                rescued = true;
                exits = RolloutExitGate.MergeRescueExits(exits, rescue);
                LandingExitVacateScreen.Mark(graph, exits, actual);
                choice = LandingExitReplan.ChooseExit(exits, reciprocalPlanned,
                    plannedExit.DistanceFromThresholdFeet, aircraftFromThresholdFt, groundSpeedKnots,
                    LandingExitLeadTier.Comfortable);
            }
        }

        if (choice.Exit == null)
            choice = LandingExitReplan.ChooseExit(exits, reciprocalPlanned,
                plannedExit.DistanceFromThresholdFeet, aircraftFromThresholdFt, groundSpeedKnots,
                LandingExitLeadTier.Floor);

        DiagLog($"Re-plan on {actual.RunwayID} ({match.Verdict}): aircraftFromThr={aircraftFromThresholdFt:F0}ft " +
                $"floorLead={RolloutExitGate.ExitLeadFeet(groundSpeedKnots):F0}ft " +
                $"plannedDist={plannedExit.DistanceFromThresholdFeet:F0}ft exits={exits.Count} " +
                $"rescued={rescued} rule={choice.Rule} tier={choice.Tier} " +
                (choice.Exit == null
                    ? "exit=none"
                    : $"exit='{choice.Exit.TaxiwayName}' node={choice.Exit.NodeId} " +
                      $"dist={choice.Exit.DistanceFromThresholdFeet:F0}ft angle={choice.Exit.ExitAngleDegrees:F0} " +
                      $"needLead={LandingExitReplan.LeadFeet(choice.Exit, groundSpeedKnots, choice.Tier):F0}ft" +
                      (choice.Rule == LandingExitReplanRule.PilotsOwnTaxiway
                          ? $" own={(choice.Exit.NodeId == plannedExit.NodeId ? "node" : "name")} " +
                            $"sep={LandingExitReplan.SeparationMetres(plannedExit, choice.Exit):F0}m"
                          : "")) +
                $" allExits={TaxiGuidanceManager.DescribeExits(exits)}" +
                (rescued ? $" rescue={TaxiGuidanceManager.DescribeExits(rescue)}" : ""));

        if (choice.Exit == null)
        {
            // Both the planner's exit list and the rescue scan need a taxiway NAME on the edge, so
            // a database that names nothing at this airport produces the same empty answer as a
            // runway that genuinely has no turnoffs. Record which it was, or a report of "it said
            // no usable exit and there obviously is one" cannot be answered from the log.
            int namedEdges = graph.GetNamedEdges().Count();
            DiagLog($"ActivateGuidance: no usable exit on {actual.RunwayID} — runway-end countdown " +
                    $"(airport named taxiway edges={namedEdges}" +
                    (namedEdges == 0
                        ? "; this database names no taxiways here, so no exit could be found on ANY runway)"
                        : ")"));
            _guidanceManager.BeginRunwayEndCountdownRollout(
                actual, graph, _dataProvider!, _icao!, SettingsManager.Current,
                lat, lon, groundSpeedKnots, correction);
            _activatedThisLanding = true;
            return true;
        }

        return StartExitGuidance(actual, choice.Exit, exits, lat, lon, headingTrue, groundSpeedKnots, correction);
    }

    /// <summary>
    /// Routes from the touchdown position to <paramref name="exit"/> and enters the landing rollout:
    /// the original activation flow, for the runway and exit actually used, with an optional runway
    /// correction for the touchdown sentence.
    /// </summary>
    private bool StartExitGuidance(Runway runway, LandingExit exit, List<LandingExit> allExits,
        double lat, double lon, double headingTrue, double groundSpeedKnots,
        TouchdownRunwayCorrection? correction)
    {
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
            _dataProvider!, _icao!,
            lat, lon, headingTrue,
            exit.NodeId,
            $"Taxiway {(exit.TaxiwayName.Length > 0 ? exit.TaxiwayName : "exit")}",
            taxiwaySequence: null,
            userHoldShortIndices: null,
            destinationHeading: null,
            destinationThresholdLat: exit.Latitude,
            destinationThresholdLon: exit.Longitude,
            destinationHeadingTrue: null,
            isRunwayDestination: false,
            prebuiltGraph: _graph,
            announceSummary: false,
            // The aircraft is still rolling at landing speed, so the pass sets no start hold; this
            // labels the crossings log line phase=touchdown.
            landingRolloutRoute: true);

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
                exit, runway.Heading, runway, allExits, lat, lon,
                SettingsManager.Current, _graph, _dataProvider, _icao ?? "",
                groundSpeedKnots, correction);

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
            exit, runway.Heading, runway, allExits, lat, lon, groundSpeedKnots, correction);
        return true;
    }
}
