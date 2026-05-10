using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

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
/// Intentionally does NOT depend on which runway the sim reports — we trust the
/// exit selection the pilot made, because many airports don't expose runway id via
/// SimConnect on the ground. The exit has lat/lon and a taxi graph node id; those
/// are enough to route to it from any touchdown point.
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
    private DateTime _lastGroundStateUpdate = DateTime.MinValue;
    private double _lastGroundSpeedKnots;

    // Minimum ground speed at on-ground transition for it to count as a real landing
    // rather than a teleport or taxi-onto-ground. Light aircraft touch down around
    // 50 kt; below 40 kt is almost always a taxi start or teleport reload.
    private const double LANDING_MIN_GS_KNOTS = 40.0;

    // Diagnostic log so we can see why activation didn't fire when only the
    // "On ground" callout was heard at touchdown. Same pattern as taxi router:
    // ApplicationData\MSFSBlindAssist\landing_exit.log.
    private static readonly string DiagLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MSFSBlindAssist", "landing_exit.log");

    private static void DiagLog(string msg)
    {
        try
        {
            File.AppendAllText(DiagLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
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

        int distFt = (int)Math.Round(exit.DistanceFromThresholdFeet);
        string name = string.IsNullOrEmpty(exit.TaxiwayName) ? "unnamed taxiway" : $"taxiway {exit.TaxiwayName}";
        _announcer.Announce(
            $"Landing exit planned: {name} at {icao} runway {runway.RunwayID}, " +
            $"{distFt} feet from threshold. Guidance will auto-start on touchdown.");
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
        _lastGroundStateUpdate = DateTime.UtcNow;
        _lastGroundSpeedKnots = groundSpeedKnots;

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

        if (error != null)
        {
            DiagLog($"ActivateGuidance LoadRoute failed: {error}");
            _announcer.Announce($"Landing exit guidance failed: {error}");
            return false;  // _activatedThisLanding stays false → bounce/retry allowed
        }

        DiagLog($"ActivateGuidance LoadRoute OK, calling StartGuidance");
        _activatedThisLanding = true;
        _guidanceManager.StartGuidance(SettingsManager.Current);

        // Switch into landing-rollout mode: tone is paused, distance-based
        // callouts ("approaching high-speed exit Sierra-5, 1500 feet" /
        // "...500 feet, slow down" / "turn left now, taxiway Sierra-5")
        // fire on the rollout. State transitions to normal Taxiing once
        // the aircraft decelerates to taxi speed or begins the actual
        // turn off the runway. The previous "Touchdown. Guiding to
        // taxiway X, N feet remaining." line was a single bare
        // announcement that didn't communicate the turn direction or the
        // exit class; BeginLandingRollout speaks a richer touchdown
        // callout itself.
        // Runway.Heading is true heading per the DB schema; pass it
        // through so the rollout can detect when the pilot starts the
        // turn off centerline.
        _guidanceManager.BeginLandingRollout(_exit, _runway.Heading);
        return true;
    }
}
