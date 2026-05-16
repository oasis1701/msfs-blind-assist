using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// State machine for taxi guidance.
/// </summary>
public enum TaxiGuidanceState
{
    Inactive,
    RouteLoaded,
    Taxiing,
    HoldShort,
    LiningUp,
    Arrived,
    /// <summary>
    /// Set immediately after a landing-exit auto-activation. The aircraft
    /// is still on the runway at high speed, decelerating; we don't want
    /// the steering tone firing on slight heading offsets that just mean
    /// the route's first node isn't dead-centre on the runway. The
    /// rollout phase suppresses the tone and issues distance-based
    /// callouts to the chosen exit. Transitions to Taxiing once ground
    /// speed drops below ROLLOUT_TAXI_GS_KTS or the aircraft has rotated
    /// significantly off runway heading (the turn onto the exit is
    /// underway).
    /// </summary>
    LandingRollout
}

/// <summary>
/// Real-time taxi guidance manager. Tracks aircraft position against a calculated route,
/// provides steering tone feedback and spoken announcements for turns, taxiway changes,
/// hold-short points, and arrival.
///
/// Design principles:
/// - Steering tone uses HEADING ERROR only (pan left = turn left, pan right = turn right)
/// - Tone is SILENT when aircraft heading matches bearing to guidance target
/// - Guidance target is a point ~50m+ ahead on the route (smooths short segments)
/// - Uses TRUE heading for bearing comparison (converts magnetic heading from SimConnect)
/// - Speech for upcoming turns and taxiway changes (like car GPS)
/// - Works on any airport, any taxiway width, any aircraft size
/// - Auto-recalculates route when user goes significantly off-route
/// </summary>
public class TaxiGuidanceManager : IDisposable
{
    private readonly ScreenReaderAnnouncer _announcer;
    private TaxiSteeringTone _steeringTone;
    private TaxiGraph? _graph;
    private TaxiRoute? _route;
    private TaxiGuidanceState _state = TaxiGuidanceState.Inactive;

    // Single lock serializing all access to _route / _graph / _state and related
    // tracking fields between the SimConnect position thread (UpdatePosition)
    // and UI-thread callers (LoadRoute, StopGuidance, ContinuePastHoldShort,
    // TryRecalculateRoute). Critical sections are short (no I/O, no awaits) so
    // a plain lock is fine and keeps the position loop responsive.
    private readonly object _stateLock = new object();

    // Retained for recalculation
    private IAirportDataProvider? _dataProvider;
    private int _destinationNodeId;
    private string _destinationName = "";
    private string _icao = "";
    private List<string>? _originalTaxiwaySequence;

    // Current tracking state
    private int _currentSegmentIndex = 0;
    private DateTime _lastRecalculationTime = DateTime.MinValue;
    private string _lastAnnouncedTaxiway = "";
    private bool _approachAnnounced = false;      // "In X feet, turn..." at ~300ft
    private bool _turnImminentAnnounced = false;   // "Turn now" at ~100ft
    private bool _crossingAnnounced = false;       // "Crossing taxiway X" at ~150ft
    private int _lastCrossingNodeId = -1;          // suppress re-announce for same node
    // Time-windowed dedup per taxiway NAME — many airports split what looks like a single
    // intersection into two closely-spaced graph nodes; without this the same name was
    // announced back-to-back ("Crossing taxiway Link 53" ... "Crossing taxiway Link 53").
    private readonly Dictionary<string, DateTime> _recentCrossingAnnouncements = new(StringComparer.OrdinalIgnoreCase);
    private const double CROSSING_DEDUP_WINDOW_SEC = 45.0;

    // User-controlled toggle for crossing announcements (wired from settings)
    private bool _announceCrossings = true;

    // Thresholds
    private const double WAYPOINT_CAPTURE_RADIUS_M = 25.0;       // Advance to next segment
    private const double APPROACH_ANNOUNCE_DISTANCE_M = 100.0;   // ~300ft advance warning (base value; scaled by speed)
    private const double APPROACH_ANNOUNCE_MIN_M = 80.0;         // floor: ~260ft at very slow taxi
    private const double APPROACH_ANNOUNCE_MAX_M = 200.0;        // ceiling: ~650ft at 30 kt+
    private const double APPROACH_ANNOUNCE_SEC_LEAD = 10.0;      // desired lead time at current ground speed
    private const double TURN_IMMINENT_DISTANCE_M = 30.0;        // ~100ft "turn now" (base value, scaled by speed)
    private const double TURN_IMMINENT_MIN_M = 20.0;             // floor: ~65ft (slow taxi, stopped)
    private const double TURN_IMMINENT_MAX_M = 75.0;             // ceiling: ~245ft (fast jet taxi)
    private const double TURN_IMMINENT_SEC_LEAD = 4.0;           // desired lead time at current ground speed
    private const double CROSSING_ANNOUNCE_DISTANCE_M = 50.0;    // ~150ft "crossing taxiway X"
    // Runway-destination arrival radius (12 m ≈ 40 ft). Tight enough that the
    // 300/150/50 ft countdown fires in full BEFORE HandleArrival takes over
    // (previously 30 m preempted the 50 ft "Stop." callout). The pilot therefore
    // hears: "…150 ft slow down" → "…50 ft stop" → "Stop. Hold short of runway X.
    // Press continue when cleared." in that order, ending at ~40 ft from the
    // hold-short node — enough braking room even at brisk taxi speed.
    private const double ARRIVAL_RADIUS_M = 12.0;
    private const double RECALCULATION_COOLDOWN_SEC = 15.0;
    private const double GUIDANCE_LOOK_AHEAD_M = 50.0;           // Min distance for heading target
    // If the aircraft is within this distance of the destination, suppress off-route
    // recalculation entirely. Navdata gaps between the last taxiway and the runway/gate
    // node (e.g., J1 terminus → K1 junction at VHHH is 67 m from 07R), combined with
    // a wide-arc runway entry, can push the aircraft off the virtual bridge segment and
    // trigger a constrained recalc that routes BACKWARDS around the taxiway loop. When
    // this close to the destination the steering tone handles the remaining metres; no
    // recalc is needed or useful.
    private const double NEAR_DESTINATION_SUPPRESS_RECALC_M = 200.0;

    // Sharp-turn threshold (ICAO Annex 14: >90° requires significantly wider radius).
    // Beyond this we add an explicit angle callout and push "slow for turn" earlier
    // so the blind pilot has time to slow and doesn't end up mowing grass on the
    // outside of the turn.
    private const double SHARP_TURN_ANGLE_DEG = 60.0;

    // Off-route (perpendicular cross-track) thresholds.
    // Tolerance = max(halfWidth + margin, floor). The margin absorbs navdata
    // centerline sampling error and pilot-discretion margin on wide aprons;
    // the floor protects against tiny/zero width values on unnamed connectors.
    // 75 ft default taxiway width covers FAA AC 150/5300 Code B/C taxiways
    // (50–82 ft) — good enough as a fallback when the DB row has no width.
    private const double OFF_ROUTE_PERP_FLOOR_M = 25.0;
    private const double OFF_ROUTE_PERP_MARGIN_M = 15.0;
    private const double DEFAULT_TAXIWAY_WIDTH_FT = 75.0;
    // Some navdata rows report absurd widths (up to thousands of feet on aprons
    // / combined surfaces). Cap so a malformed row can't blow the perpendicular
    // off-route tolerance out to hundreds of meters — that would mean the
    // aircraft is effectively never "off route" on those segments.
    private const double OFF_ROUTE_PERP_WIDTH_CAP_FT = 300.0;
    // Grace window after a segment advance during which off-route is suppressed.
    // First-turn false-trigger: in the middle of the turn arc the aircraft's
    // perpendicular distance to the just-completed *or* just-entered segment can
    // briefly exceed tolerance before the bearing settles. Without this grace
    // window the 3 s persistence timer accumulated across a slow turn and fired
    // a recalc mid-turn, which re-routed the pilot onto a random shortest path.
    private const double POST_TURN_OFFROUTE_GRACE_SEC = 4.0;
    // Minimum ground speed before off-route detection can fire. Prevents the
    // spurious "Recalculating…" chain when the aircraft is sitting still at the
    // gate before pushback: the initial position is never exactly on a graph
    // centerline, so a stationary aircraft can be "off-route" by definition.
    // 2 kt is low enough to catch any real pushback/taxi start.
    private const double OFF_ROUTE_MIN_GS_KTS = 2.0;
    // Off-route must persist this many seconds before we trigger a reroute.
    // Absorbs single-sample GPS jitter and brief lateral deviations (wide turn,
    // hopping curb on an intersection) without false triggers.
    private const double OFF_ROUTE_PERSISTENCE_SEC = 3.0;

    // Post-recalc sanity gate thresholds. Two indicators reject a recalc:
    //   A. Length blow-up: new route > ratio·oldRemaining + pad meters.
    //   B. First-segment-heads-backwards: bearing delta from destination > deg.
    // Tuned together in TryRecalculateRoute. See the comment block there for
    // motivation. Promoted to named constants from inline values so they're
    // discoverable from outside TryRecalculateRoute (and tunable without
    // hunting for them) — matches the file-style convention of every other
    // gate threshold above.
    private const double RECALC_LENGTH_BLOWUP_RATIO = 2.0;
    private const double RECALC_LENGTH_BLOWUP_PAD_M = 500.0;
    private const double RECALC_BACKWARDS_DELTA_DEG = 120.0;

    // Position tracking
    private double _lastLat, _lastLon, _lastHeading;
    private double _lastGroundSpeedKts;
    private bool _positionInitialized = false;

    // Off-route persistence tracker — off-route must be sustained for
    // OFF_ROUTE_PERSISTENCE_SEC before a recalc fires. MinValue = not off-route.
    private DateTime _offRouteSince = DateTime.MinValue;
    // Timestamp of the last segment advance (AdvanceSegment or
    // AdvanceToNearestSegment). Used with POST_TURN_OFFROUTE_GRACE_SEC to
    // suppress off-route detection briefly after we cross a turn node.
    private DateTime _lastSegmentAdvanceTime = DateTime.MinValue;

    // Smoothed heading error for tone (low-pass filter kills jitter from wheel vibration)
    private double _smoothedHeadingError = 0.0;
    private bool _headingErrorInitialized = false;
    private const double HEADING_ERROR_FILTER_ALPHA = 0.25;  // 0 = no smoothing, 1 = max smoothing

    // Diagnostic per-frame trace for steering-tone troubleshooting. Captures the inputs
    // and outputs of the heading-error pipeline so erratic L/R tone flipping can be
    // analysed post-hoc. Truncated and re-headered on every LoadRoute. Rate-limited
    // to ~10 Hz to keep the file under ~100 KB for a typical 5-10 min taxi.
    // Always on while Taxiing — cheap (one File.AppendAllText per ~100 ms) and the
    // log is overwritten each new route, so no growth across sessions.
    private static readonly string GuidanceLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MSFSBlindAssist", "taxi_guidance.log");
    private DateTime _lastGuidanceLogTime = DateTime.MinValue;
    private const double GUIDANCE_LOG_INTERVAL_MS = 100.0;

    // Last actionable instruction announced (for the Ctrl+Y "Repeat" hotkey).
    // Only TACTICAL announcements update this — turn callouts, hold-shorts,
    // taxiway changes, lineup, arrival, distance countdowns. Two peripheral
    // sites still call _announcer.Announce directly (without recording to
    // _lastInstruction) so the Repeat-Last buffer keeps the actionable callout:
    // (a) the LoadRoute route summary at start of guidance, (b) the periodic
    // ground-speed bucket announcer (per CLAUDE.md, must not displace the
    // Repeat-Last buffer). Cleared on StopGuidance.
    private string _lastInstruction = "";

    // Periodic ground-speed announcer state. The "last announced bucket"
    // approach (gs / interval, integer) fires exactly once when the speed
    // crosses each multiple in either direction, with no risk of stuttering
    // on jitter near the boundary because we track the integer bucket, not
    // an absolute threshold. Initialised to -1 so the FIRST sample after
    // guidance starts establishes a baseline without firing — we don't want
    // a "10 knots" callout immediately when the aircraft is sitting at
    // 10 kt steady from a previous taxi.
    private int _lastAnnouncedGsBucket = -1;

    // Speed-aware warnings
    private DateTime _lastSpeedWarningTime = DateTime.MinValue;
    private const double SPEED_WARNING_COOLDOWN_SEC = 8.0;
    // FAA / airline standard taxi speeds:
    //   - Straight, unrestricted: 30 kt max (our cap)
    //   - Normal turn: 10 kt max
    //   - Sharp turn (≥60°, per ICAO Annex 14 considerations): 10 kt with extra caution
    //   - Ramp area: 10 kt (handled by the straight cap effectively)
    private const double MAX_TAXI_SPEED_STRAIGHT_KTS = 30.0;
    private const double MAX_TAXI_SPEED_TURN_KTS = 10.0;
    private const double MAX_TAXI_SPEED_SHARP_TURN_KTS = 8.0;  // conservative for 60°+ turns
    // Lookahead distance for the "slow for turn" warning. Scales with speed so
    // the pilot has time to bleed off knots before the turn: 6 s of travel time
    // at current ground speed, floored at 40 m, ceilinged at 150 m.
    private const double TURN_SPEED_WARN_SEC_LEAD = 6.0;
    private const double TURN_SPEED_WARN_MIN_M = 40.0;
    private const double TURN_SPEED_WARN_MAX_M = 150.0;

    // Hold-short countdown announcements (track which threshold has fired)
    private bool _holdShortAnnounce300 = false;
    private bool _holdShortAnnounce150 = false;
    private bool _holdShortAnnounce50 = false;

    // Parking countdown announcements
    private bool _parkingAnnounce50 = false;
    private bool _parkingAnnounce20 = false;
    private bool _parkingAnnounce10 = false;

    // Runway incursion detection (off-route hold-short proximity)
    private int _lastIncursionWarnedNodeId = -1;
    private DateTime _lastIncursionWarningTime = DateTime.MinValue;
    private const double INCURSION_WARN_DISTANCE_M = 40.0;
    private const double INCURSION_WARNING_COOLDOWN_SEC = 10.0;

    // Lineup state (active during LiningUp phase — for runways AND gates)
    private double _lineupTargetLat, _lineupTargetLon;
    private double _lineupHeadingTrue, _lineupHeadingMag;
    private bool _lineupAnnouncedAligned = false;
    // Thresholds for the lineup-pulse cue: when the pilot is essentially
    // stopped (≤ MAX_GS_KTS) AND still meaningfully misaligned (heading or
    // cross-track), the steering tone PULSES on/off instead of playing
    // continuously. Pulse is purely audio (no voice — verbal cues during
    // lineup steal attention from rudder control), and a stopped pilot needs
    // a distinct cue from "moving and slightly off". Pan direction still
    // says WHICH way to turn; the pulse rhythm says "you've stopped but
    // you're not aligned yet, keep working." Aligned or moving → continuous.
    //
    // Why the cross-track condition exists: the intercept-angle controller
    // computes a desired heading that's offset from runway heading when
    // cross-track is large (saturated at ±30°). A pilot who matches that
    // desired heading and stops gets ZERO audio cue from the heading-only
    // pulse, even though cross-track is still huge — the controller is
    // "satisfied" because the aircraft IS pointing the right way to close
    // on centerline once it moves. But the pilot doesn't know they need
    // to move. The cross-track threshold here ensures the pulse fires in
    // that case too, telling the pilot "you're stopped + not done."
    private const double LINEUP_PULSE_MAX_GS_KTS = 3.0;
    private const double LINEUP_PULSE_MIN_HDG_ERR_DEG = 5.0;
    private const double LINEUP_PULSE_MIN_CROSS_FEET = 10.0;
    private bool _isRunwayLineup = false;  // true = runway (use centerline), false = gate (heading only)
    private bool _hasLineupTarget = false; // explicit flag — safer than (_lineupTargetLat != 0)

    // When true, we are currently holding short AT the destination runway
    // (FAA/ICAO: ATC taxi-to clearance never authorizes entering the assigned
    // takeoff runway — pilot must wait for "line up and wait" or "cleared for
    // takeoff"). On ContinuePastHoldShort we transition to LiningUp instead of
    // the normal "continue taxiing" path.
    private bool _holdShortAtDestination = false;

    // Landing-rollout state. _rolloutExit is the user's chosen exit; the rest
    // are progress flags so each callout fires once. The steering tone is
    // muted while in this state — the pilot is decelerating in a straight
    // line on rudder, not steering toward a taxi-graph waypoint, and false
    // tone activations off small heading offsets from the route's first
    // node would be confusing on rollout. Tone resumes when state
    // transitions to Taxiing (GS dropped below ROLLOUT_TAXI_GS_KTS, or the
    // pilot has begun the actual turn onto the exit).
    private Navigation.LandingExit? _rolloutExit;
    private double _rolloutRunwayHeadingTrue;
    // Runway and full exit list captured at BeginLandingRollout time, so the
    // overshoot-retarget path can pick the next downfield exit without rebuilding
    // the graph or querying the DB on the per-frame loop. _rolloutAllExits is
    // sorted by DistanceFromThresholdFeet ascending (the order returned by
    // TaxiGraph.GetLandingExits), so finding the next downfield exit is a linear
    // scan terminating at the first match.
    private Database.Models.Runway? _rolloutRunway;
    private List<Navigation.LandingExit> _rolloutAllExits = new();
    // Touchdown is announced unconditionally inside BeginLandingRollout (a one-shot
    // entry event), so no per-rollout flag is needed for it. The three flags below
    // gate per-rollout-once distance callouts in UpdateLandingRollout.
    private bool _rolloutApproach1500Announced = false;
    private bool _rolloutApproach500Announced = false;
    private bool _rolloutTurnNowAnnounced = false;
    // Runway-end countdown mode. Entered when the overshoot detector finds
    // no downfield exit remaining (or RetargetLandingExit's LoadRoute call
    // fails). Drives a distance-to-runway-end countdown (1500 / 500 / 100 ft)
    // so the blind pilot knows how much pavement is left for braking instead
    // of falling silent. State stays in LandingRollout so UpdatePosition
    // continues to feed the per-frame loop; transitions to Taxiing only
    // when the pilot is effectively stopped or has begun a backtaxi turn.
    private bool _rolloutNoExitMode;
    private bool _rolloutEnd1500Announced;
    private bool _rolloutEnd500Announced;
    private bool _rolloutEnd100Announced;

    // --- DIAGNOSTIC LOGGING (debug/landing-rollout-instrumentation branch) ---
    // Captures rollout-phase state to landing_exit.log so we can see why
    // UpdateLandingRollout silently fails to fire approach callouts at some
    // airports. Removed after the root cause is identified.
    private bool _rolloutDiagFirstCallDone;
    private DateTime _rolloutDiagLastPeriodic = DateTime.MinValue;
    // Below this GS the aircraft is at taxi speed — graduate to normal taxi
    // guidance even if we haven't started the turn yet. 30 kt is a typical
    // taxi-fast cap (real-world SOPs cap straight-taxi at 30 kt; turns
    // get ~10-15 kt limits).
    private const double ROLLOUT_TAXI_GS_KTS = 30.0;
    // Heading deviation from the runway centerline that signals "the
    // pilot has started the turn onto the exit". Once we see this, hand
    // over to normal taxi guidance even if speed is still high (a
    // high-speed exit at 60 kt is plausible on Code-E rapid exits).
    private const double ROLLOUT_TURN_BEGAN_HDG_DEG = 15.0;

    // Speed-based handoff is now gated on proximity to the chosen exit. On long
    // runways the aircraft routinely decelerates below ROLLOUT_TAXI_GS_KTS
    // (30 kt) thousands of feet upfield of the planned exit; without this gate
    // the steering tone resumed at 30 kt and started panning toward the next
    // route waypoint while the pilot was still on the runway centerline,
    // implying "turn now" far too early. 500 ft matches the existing
    // "500 ft slow down" approach callout — by the time the pilot hears that,
    // they're committed to the turn.
    private const double ROLLOUT_NEAR_EXIT_FT = 500.0;

    // Along-runway distance past the planned exit at which we declare an
    // overshoot. Must be tight enough to react quickly while loose enough to
    // ride out GPS jitter and the moment between "passing the exit marker"
    // and "starting the turn" on a normal exit. 100 ft @ 30 kt = ~2 s — the
    // tone resume / turn-began handoff has already fired by then in the
    // normal case, so an actual overshoot is unambiguous when this fires.
    private const double ROLLOUT_OVERSHOOT_FT = 100.0;

    // Below this ground speed, runway-end countdown mode considers the
    // aircraft effectively stopped and hands off to plain Taxiing. Lower
    // than ROLLOUT_TAXI_GS_KTS (30) because the pilot in this mode is
    // braking hard with no further callouts coming; once they're at a
    // crawl the countdown has nothing more useful to say.
    private const double ROLLOUT_NO_EXIT_STOPPED_GS_KTS = 3.0;

    // Lineup thresholds — runway needs degree-level precision because takeoff roll
    // amplifies any heading error; gate is more forgiving since there's no roll.
    private const double LINEUP_HEADING_TOLERANCE_DEG = 5.0;             // gate default
    private const double LINEUP_RUNWAY_HEADING_TOLERANCE_DEG = 3.0;      // runway — tighter
    private const double LINEUP_CENTERLINE_TOLERANCE_FEET = 25.0;
    private const double GATE_ARRIVAL_RADIUS_FEET = 20.0;

    // Runway-lineup blend band: when far from centerline, steer toward the threshold
    // Lineup intercept-angle ramp (see UpdateLineup). The intercept rises from
    // 0° at zero cross-track to MAX at LINEUP_INTERCEPT_SAT_FEET. A small noise
    // deadband (LINEUP_NOISE_DEADBAND_FEET) keeps GPS jitter near the line from
    // toggling the sign of the correction every frame, but it's much tighter
    // than the previous 50 ft band — for a blind pilot the tone IS the
    // instrument, so even small drifts must be audible. The sqrt curve gives a
    // steep response near zero so a 15 ft offset already produces an audible
    // pan, while still saturating gracefully at large offsets.
    private const double LINEUP_NOISE_DEADBAND_FEET = 8.0;
    private const double LINEUP_INTERCEPT_SAT_FEET = 100.0;

    // Conversion constants
    private const double METERS_TO_FEET = 3.28084;
    private const double METERS_TO_NM = 0.000539957;
    private const double NM_TO_FEET = 6076.12;

    public TaxiGuidanceState State => _state;

    /// <summary>
    /// The full spoken route-summary text from the most recent successful
    /// LoadRoute, or empty if no route has been loaded. Read by
    /// TaxiAssistForm to populate its read-only summary box. The string is
    /// the same one that gets passed to <c>_announcer.Announce</c> when
    /// <c>announceSummary</c> is true — useful for verifying what the
    /// shortest-path calculation actually produced (the spoken version is
    /// often interrupted by the screen reader).
    /// </summary>
    public string LastRouteSummary { get; private set; } = "";
    public TaxiRoute? CurrentRoute => _route;
    public TaxiGraph? CurrentGraph => _graph;
    public int CurrentSegmentIndex => _currentSegmentIndex;

    // "Where Am I" graph cache — used when guidance is inactive so we don't rebuild
    // the graph on every hotkey press. Keyed by ICAO; invalidated on aircraft/airport change.
    private TaxiGraph? _whereAmICachedGraph;
    private string _whereAmICachedIcao = "";

    /// <summary>
    /// Describes the aircraft's current location for the "Where Am I" hotkey.
    /// Returns a spoken string like "Taxiway Bravo at KJFK", "Gate A25 at KJFK", or
    /// "Runway 22L at KJFK". If the aircraft isn't near any known airport or the
    /// location can't be resolved, returns a user-facing diagnostic.
    ///
    /// Reuses the active guidance graph if it matches the airport; otherwise builds and
    /// caches a graph for ad-hoc queries. Does NOT mutate any guidance state.
    /// </summary>
    public string DescribeCurrentLocation(
        IAirportDataProvider dataProvider,
        string icao,
        double lat,
        double lon)
    {
        if (string.IsNullOrWhiteSpace(icao))
            return "No airport nearby.";

        TaxiGraph? graph = null;

        // Prefer the active guidance graph if it's for this airport
        if (_graph != null && string.Equals(_icao, icao, StringComparison.OrdinalIgnoreCase))
            graph = _graph;
        else if (_whereAmICachedGraph != null &&
                 string.Equals(_whereAmICachedIcao, icao, StringComparison.OrdinalIgnoreCase))
            graph = _whereAmICachedGraph;

        if (graph == null)
        {
            try
            {
                var paths = dataProvider.GetTaxiPaths(icao);
                if (paths == null || paths.Count == 0)
                    return $"No taxi data available for {icao}.";

                var parking = dataProvider.GetParkingSpots(icao) ?? new List<ParkingSpot>();
                var runwayStarts = dataProvider.GetRunwayStarts(icao) ?? new List<StartPosition>();

                graph = TaxiGraph.Build(paths, parking, runwayStarts);
                _whereAmICachedGraph = graph;
                _whereAmICachedIcao = icao;
            }
            catch (Exception ex)
            {
                return $"Could not load airport data for {icao}. {ex.Message}";
            }
        }

        string description = graph.DescribeLocation(lat, lon);
        if (string.IsNullOrEmpty(description))
            return $"Not on a known taxiway or ramp at {icao}.";

        return $"{description} at {icao}.";
    }

    /// <summary>
    /// Invalidates the "Where Am I" graph cache. Call on aircraft change or when the
    /// user explicitly wants a fresh graph (rare).
    /// </summary>
    public void ClearWhereAmICache()
    {
        _whereAmICachedGraph = null;
        _whereAmICachedIcao = "";
    }

    /// <summary>
    /// Attempts to get a runway lineup reference from the current taxi guidance state.
    /// Returns true if the taxi is actively lining up to a runway (LiningUp state, runway target
    /// available), AND the aircraft has arrived at the runway (_hasLineupTarget set with valid
    /// coordinates). Used by takeoff assist to seed its reference without re-teleporting.
    /// </summary>
    public bool TryGetRunwayLineupReference(
        out double thresholdLat, out double thresholdLon,
        out double headingTrue, out double headingMag,
        out string runwayId, out string airportIcao)
    {
        thresholdLat = 0; thresholdLon = 0;
        headingTrue = 0; headingMag = 0;
        runwayId = ""; airportIcao = "";

        // Accept both LiningUp (actively aligning) and Arrived (lined up, brake set)
        if (!_hasLineupTarget) return false;
        if (!_isRunwayLineup) return false;
        if (_state != TaxiGuidanceState.LiningUp && _state != TaxiGuidanceState.Arrived) return false;

        thresholdLat = _lineupTargetLat;
        thresholdLon = _lineupTargetLon;
        headingTrue = _lineupHeadingTrue;
        headingMag = _lineupHeadingMag;
        airportIcao = _icao;

        // Destination name is typically "Runway 27L" — strip the "Runway " prefix if present.
        string dn = _destinationName ?? "";
        if (dn.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase))
            runwayId = dn.Substring(7).Trim();
        else
            runwayId = dn.Trim();

        return true;
    }

    public event EventHandler<TaxiGuidanceState>? StateChanged;

    public TaxiGuidanceManager(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;
        _steeringTone = new TaxiSteeringTone();
    }

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
        Dictionary<int, string>? userRunwayHoldShorts = null)
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

            // Store lineup target data (runway threshold or gate position) for lineup phase
            _isRunwayLineup = isRunwayDestination;
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

            // Use pre-built graph if provided (avoids a 200-500ms rebuild stall at large
            // airports when the form already ran BuildAsync). Otherwise build from the DB —
            // used by auto-recalculation paths that don't carry a graph.
            if (prebuiltGraph != null)
            {
                _graph = prebuiltGraph;
            }
            else
            {
                var paths = dataProvider.GetTaxiPaths(icao);
                if (paths.Count == 0)
                    return "No taxi path data available for this airport.";

                var parking = dataProvider.GetParkingSpots(icao);
                var starts = dataProvider.GetRunwayStarts(icao);

                _graph = TaxiGraph.Build(paths, parking, starts);
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

            TaxiNode? startNode = null;
            if (taxiwaySequence != null && taxiwaySequence.Count > 0)
            {
                startNode = _graph.FindNearestNodeOnTaxiway(
                    aircraftLat, aircraftLon, taxiwaySequence[0],
                    requiredComponentId: destComponentId);
            }
            if (startNode == null)
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

            if (taxiwaySequence != null && taxiwaySequence.Count > 0)
                route = router.FindConstrainedPath(startNode.NodeId, destinationNodeId, taxiwaySequence);
            else
                route = router.FindShortestPath(startNode.NodeId, destinationNodeId);

            if (route == null || route.Segments.Count == 0)
                return "Could not calculate a route to the destination.";

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

            // Critical safety fix: when the destination is a runway, the route MUST
            // end at the hold-short line — not at the threshold itself. Otherwise
            // HandleArrival fires only when the aircraft is within the 30 m arrival
            // radius of the threshold, which is often PAST the hold-short markings
            // (real hold-short lines sit ~150-200 ft / 46-61 m back from the threshold).
            if (isRunwayDestination)
                TruncateToHoldShort(route, destinationName);

            // Auto-insert hold-shorts for INTERMEDIATE runway crossings.
            // FAA AIM 4-3-18 & ICAO Doc 4444: an aircraft must hold short of every
            // runway it crosses on the way to its destination, with explicit ATC
            // clearance for each one. Controllers issue runway crossings one at a
            // time. Without this pass, a route from gate to runway 22L through
            // an intersection with runway 13L would taxi straight across 13L
            // with no pause — illegal in real life and a runway-incursion risk on
            // VATSIM. This pause-and-resume flow uses the same Continue hotkey
            // pattern as ATC-instructed hold-shorts.
            InsertRunwayCrossingHoldShorts(route, isRunwayDestination ? destinationName : "");

            _route = route;
            _currentSegmentIndex = 0;
            _approachAnnounced = false;
            _turnImminentAnnounced = false;
            _crossingAnnounced = false;
            _lastCrossingNodeId = -1;
            _lastAnnouncedTaxiway = "";
            _headingErrorInitialized = false;
            _smoothedHeadingError = 0;
            _lastIncursionWarnedNodeId = -1;
            _holdShortAnnounce300 = _holdShortAnnounce150 = _holdShortAnnounce50 = false;
            _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
            // Reset GS announcer bucket so the first sample after a fresh
            // route load establishes baseline silently (no spurious "10 knots"
            // if the aircraft is already moving when guidance starts).
            _lastAnnouncedGsBucket = -1;
            // Reset lineup/cooldown state so an ATC-amendment reload mid-taxi doesn't
            // inherit stale values from the prior route (e.g., "aligned" carrying over
            // would suppress the first alignment announcement on the new lineup target).
            _lineupAnnouncedAligned = false;
            _lastRecalculationTime = DateTime.MinValue;
            _lastSpeedWarningTime = DateTime.MinValue;
            _lastIncursionWarningTime = DateTime.MinValue;
            _offRouteSince = DateTime.MinValue;
            _lastSegmentAdvanceTime = DateTime.MinValue;
            _holdShortAtDestination = false;

            // Reset diagnostic frame trace. Each LoadRoute starts a fresh file
            // headed with route metadata + CSV column names so the file is
            // self-describing for post-flight analysis.
            try
            {
                File.WriteAllText(GuidanceLogPath,
                    $"=== Guidance {DateTime.Now:yyyy-MM-dd HH:mm:ss} icao={_icao} dest={_destinationName} segments={_route.Segments.Count} totalM={_route.TotalDistanceMeters:F0} ===" + Environment.NewLine
                    + "time,lat,lon,hdg,gs,seg,segBrg,w,nxtTurn,tLat,tLon,raw,smooth" + Environment.NewLine);
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
                string summary = BuildRouteSummary(route, isRunwayDestination);
                if (!string.IsNullOrEmpty(runwayHoldShortWarning))
                    summary = summary + " " + runwayHoldShortWarning;
                LastRouteSummary = summary;
                if (announceSummary)
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
    /// Starts active guidance (begins steering tone and position tracking).
    /// </summary>
    public void StartGuidance(UserSettings settings)
    {
        lock (_stateLock)
        {
        if (_route == null || _route.Segments.Count == 0) return;

        _announceCrossings = settings.TaxiGuidanceAnnounceCrossings;

        // Apply invert-pan / hard-pan settings before Start so the tone is
        // configured correctly the moment it begins sounding. Both are also
        // re-read on every tone update (cheap), so a runtime toggle while
        // taxi guidance is already active takes effect on the next position
        // frame.
        _steeringTone.InvertPan = settings.TaxiGuidanceInvertSteeringTone;
        _steeringTone.HardPan = settings.TaxiGuidanceHardPanTone;

        _steeringTone.Start(
            settings.TaxiGuidanceToneWaveform,
            settings.TaxiGuidanceToneVolume);

        SetState(TaxiGuidanceState.Taxiing);

        // Announce the first *named* taxiway, not the literal first segment. When
        // the aircraft is pushing off a stand, the first route segment is
        // typically a parking connector (PathType "P") with no taxiway name —
        // reading it out as "" would be useless to the pilot. Walk forward until
        // we find a segment with a real name and announce that, along with an
        // estimated distance to join it. If the whole route is unnamed (tiny
        // airport), fall back to the "steering guidance active" message.
        double distanceToFirstNamed = 0;
        string firstNamedTaxiway = "";
        foreach (var seg in _route.Segments)
        {
            if (!string.IsNullOrEmpty(seg.TaxiwayName))
            {
                firstNamedTaxiway = seg.TaxiwayName;
                break;
            }
            distanceToFirstNamed += seg.DistanceMeters;
        }

        if (!string.IsNullOrEmpty(firstNamedTaxiway))
        {
            if (distanceToFirstNamed > 10.0)
            {
                // Still on the apron / parking connector — tell the pilot how
                // far to the first named taxiway so they aren't surprised by
                // the "Taxiway X" callout mid-roll.
                string distStr = FormatDistance(distanceToFirstNamed);
                AnnounceInstruction($"Steering guidance active. Join taxiway {firstNamedTaxiway} in {distStr}.");
            }
            else
            {
                AnnounceInstruction($"Taxiway {firstNamedTaxiway}. Steering guidance active.");
            }
            _lastAnnouncedTaxiway = firstNamedTaxiway;
        }
        else
        {
            AnnounceInstruction("Steering guidance active.");
        }
        } // end lock(_stateLock)
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
        List<Navigation.LandingExit> allExits)
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
            _rolloutRunwayHeadingTrue = runwayHeadingTrue;
            _rolloutRunway = runway;
            _rolloutAllExits = allExits;
            _rolloutApproach1500Announced = false;
            _rolloutApproach500Announced = false;
            _rolloutTurnNowAnnounced = false;
            // Defense in depth: clear no-exit/runway-end state from any prior
            // rollout. StopGuidance does this; matching the pattern here
            // ensures we never inherit stale flags when starting a fresh
            // landing-exit flow. Currently no known path leaks these to the
            // next BeginLandingRollout (StopGuidance fires on takeoff via
            // OnTakeoffAssistActiveChanged), but defensive is cheap.
            _rolloutNoExitMode = false;
            _rolloutEnd1500Announced = false;
            _rolloutEnd500Announced = false;
            _rolloutEnd100Announced = false;
            // DIAGNOSTIC: reset per-rollout instrumentation gates
            _rolloutDiagFirstCallDone = false;
            _rolloutDiagLastPeriodic = DateTime.MinValue;

            // Pause the tone — UpdateLandingRollout's per-frame logic
            // explicitly leaves it paused. When we transition to Taxiing it
            // calls Resume so the existing taxi-tone behaviour kicks in
            // without restart.
            _steeringTone.Pause();

            SetState(TaxiGuidanceState.LandingRollout);

            RolloutDiag($"BeginLandingRollout DONE: state -> LandingRollout, " +
                $"tone paused, touchdown callout queued");

            // Touchdown callout. Distance-from-touchdown is what the user
            // cares about (not from-threshold) — the pilot needs to know
            // how much rollout they have to decelerate.
            string exitClass = exit.ExitType switch
            {
                "High-speed" => "high-speed exit",
                "End"        => "runway-end exit",
                _            => "exit"
            };
            string name = string.IsNullOrEmpty(exit.TaxiwayName) ? "exit" : $"taxiway {exit.TaxiwayName}";
            int distFt = (int)Math.Round(exit.DistanceFromTouchdownFeet);
            if (distFt > 0)
                AnnounceInstruction($"Touchdown. {exitClass} {name} in {distFt} feet.");
            else
                AnnounceInstruction($"Touchdown. {exitClass} {name}.");
        }
    }

    /// <summary>
    /// Enters runway-end countdown mode directly, without a taxi route. Used by
    /// LandingExitPlanner as a safety net when LoadRoute fails at touchdown —
    /// e.g., at an airport with navdata defects that leave no valid path from
    /// touchdown to the chosen exit. Keeps the per-frame distance callouts
    /// ("Runway end in 1500 feet" → "...500 feet, slow down" → "...100 feet,
    /// stop") active so a blind pilot has audio cues during rollout even when
    /// turn-by-turn taxi guidance is unavailable.
    /// </summary>
    public void BeginLandingRolloutNoRoute(Database.Models.Runway runway)
    {
        lock (_stateLock)
        {
            _rolloutExit = null;
            _rolloutRunway = runway;
            _rolloutRunwayHeadingTrue = runway.Heading;

            // EnterRunwayEndCountdown handles everything else: clears _route /
            // _destinationNodeId, sets _rolloutNoExitMode, resets the approach
            // and end-countdown announce flags, pauses the tone, and enters
            // LandingRollout state.
            EnterRunwayEndCountdown();
        }
    }

    /// <summary>
    /// Called every position update (~30Hz from SimConnect SIM_FRAME).
    /// headingMag is MAGNETIC heading in degrees; magVariation converts to true heading.
    /// </summary>
    public void UpdatePosition(double lat, double lon, double headingMag, double magVariation, double groundSpeedKts = 0)
    {
        // Hold _stateLock for the entire frame. UI-thread callers (LoadRoute,
        // StopGuidance, ContinuePastHoldShort) take the same lock, so _route /
        // _graph / _currentSegmentIndex cannot change underneath us mid-frame.
        // Critical section contains only in-memory work and fire-and-forget
        // announce/tone calls — no I/O, no awaits, no re-entry into this
        // manager, so the lock stays short.
        lock (_stateLock)
        {
        _lastGroundSpeedKts = groundSpeedKts;

        // Periodic ground-speed announcement. When the user has configured an
        // interval (5 or 10 kt), announce the current speed rounded to the
        // NEAREST multiple of the interval — so 4 / 5 / 6 kt all read as
        // "5 knots", 9 / 10 / 11 kt all read as "10 knots". The previous
        // floor-bucket logic ((int)(gs / interval)) flipped between "0" and
        // "5" every time the raw value crossed 5.000, producing announcements
        // that bore no resemblance to the actual speed at any given moment.
        //
        // Hysteresis: once a bucket has been announced, the new bucket must
        // be reached with a small margin (HYSTERESIS_KTS) past its rounding
        // boundary before we re-announce. This kills jitter at the new
        // boundary (the midpoint between buckets, e.g., 7.5 kt with
        // interval=5) — without it, a steady 7.5 kt with throttle noise would
        // alternate "5 knots" / "10 knots" / "5 knots". 0.5 kt is enough to
        // ride out typical SimConnect GS jitter at taxi speeds.
        //
        // First sample after guidance start establishes baseline silently
        // (initial _lastAnnouncedGsBucket = -1 → updated, no announcement).
        // Goes through plain _announcer (NOT AnnounceInstruction) so a fading
        // ground-speed callout doesn't displace the most recent actionable
        // instruction in the Repeat-Last buffer.
        int gsInterval = SettingsManager.Current.TaxiGuidanceGroundSpeedAnnounceInterval;
        if (gsInterval > 0 && groundSpeedKts >= 0)
        {
            const double HYSTERESIS_KTS = 0.5;
            int newBucket = (int)Math.Round(groundSpeedKts / gsInterval, MidpointRounding.AwayFromZero);

            if (_lastAnnouncedGsBucket < 0)
            {
                // Establish baseline silently on first sample.
                _lastAnnouncedGsBucket = newBucket;
            }
            else if (newBucket != _lastAnnouncedGsBucket)
            {
                // Hysteresis: only commit the change if gs is at least
                // HYSTERESIS_KTS past the rounding boundary inside the new
                // bucket. The midpoint between bucketA and bucketB is
                // (bucketA + bucketB) / 2 * interval. We require the speed
                // to be at least HYSTERESIS_KTS into the new bucket from
                // that midpoint.
                double newBucketCenter = newBucket * gsInterval;
                double distanceIntoNewBucket = Math.Abs(groundSpeedKts - newBucketCenter);
                double bucketHalfWidth = gsInterval / 2.0;
                if (distanceIntoNewBucket <= bucketHalfWidth - HYSTERESIS_KTS)
                {
                    int announceKts = newBucket * gsInterval;
                    _announcer.Announce($"{announceKts} knots.");
                    _lastAnnouncedGsBucket = newBucket;
                }
            }
        }

        // Refresh tone-direction settings once per frame, before any branch
        // (taxiing, lining-up, or runway-aligned hold) that can drive the
        // tone. This makes runtime toggles in Taxi Guidance Options take
        // effect on the very next sample without restarting taxi guidance.
        // Cheap — two property assignments per frame.
        _steeringTone.InvertPan = SettingsManager.Current.TaxiGuidanceInvertSteeringTone;
        _steeringTone.HardPan   = SettingsManager.Current.TaxiGuidanceHardPanTone;

        // Convert magnetic heading to true heading for bearing comparison
        // True heading = magnetic heading + magnetic variation (east positive)
        double headingTrue = headingMag + magVariation;
        if (headingTrue < 0) headingTrue += 360;
        if (headingTrue >= 360) headingTrue -= 360;

        _lastLat = lat;
        _lastLon = lon;
        _lastHeading = headingTrue;
        _positionInitialized = true;

        // Handle lineup phase separately
        if (_state == TaxiGuidanceState.LiningUp)
        {
            UpdateLineup(lat, lon, headingTrue, headingMag);
            return;
        }

        // Landing rollout — mute tone, do distance-based callouts, transition
        // to Taxiing when the aircraft has decelerated or begun the turn.
        if (_state == TaxiGuidanceState.LandingRollout)
        {
            UpdateLandingRollout(lat, lon, headingTrue, groundSpeedKts);
            return;
        }

        if (_state != TaxiGuidanceState.Taxiing || _route == null || _graph == null)
            return;

        if (_currentSegmentIndex >= _route.Segments.Count)
        {
            HandleArrival();
            return;
        }

        // Find the best matching segment (current or ahead)
        AdvanceToNearestSegment(lat, lon);

        if (_currentSegmentIndex >= _route.Segments.Count)
        {
            HandleArrival();
            return;
        }

        var currentSeg = _route.Segments[_currentSegmentIndex];

        // Distance to current segment's target waypoint
        double distToTarget = TaxiGraph.FastDistanceMeters(
            lat, lon, currentSeg.ToNode.Latitude, currentSeg.ToNode.Longitude);

        // Check if we've arrived at the final destination.
        // Use a SMALLER radius for gates so the 50/20/10ft parking countdown has time
        // to fire before HandleArrival transitions us out of Taxiing state. For runways,
        // the larger radius lets the LiningUp centerline tracker pick up earlier.
        double arrivalRadius = _isRunwayLineup
            ? ARRIVAL_RADIUS_M
            : GATE_ARRIVAL_RADIUS_FEET / METERS_TO_FEET; // 20 ft → ~6 m

        if (_currentSegmentIndex == _route.Segments.Count - 1 && distToTarget < arrivalRadius)
        {
            // Announce the 50/20/10ft countdown one last time before arriving,
            // so it fires even if updates are sparse near the target.
            CheckParkingCountdown(distToTarget);
            HandleArrival();
            return;
        }

        // Calculate heading error for steering tone using LOOK-AHEAD target
        // This prevents tone jitter from very short segments (5-15m in navdata)
        var (targetLat, targetLon) = GetGuidanceTarget(lat, lon);
        double bearingToTarget = NavigationCalculator.CalculateBearing(lat, lon, targetLat, targetLon);
        double headingError = NormalizeAngle(bearingToTarget - headingTrue);

        // Apply 1-pole low-pass filter to heading error (kills jitter from wheel vibration)
        if (!_headingErrorInitialized)
        {
            _smoothedHeadingError = headingError;
            _headingErrorInitialized = true;
        }
        else
        {
            _smoothedHeadingError = _smoothedHeadingError * (1 - HEADING_ERROR_FILTER_ALPHA) +
                                     headingError * HEADING_ERROR_FILTER_ALPHA;
        }

        // Update steering tone with smoothed heading error.
        // Pass current segment width so the tone's hysteresis adapts to pavement:
        // narrow taxiways get tighter tolerances, wide runways looser.
        // Pan-direction settings (InvertPan / HardPan) are refreshed at the
        // top of UpdatePosition so they apply uniformly to taxiing, lining-up,
        // and runway-aligned phases.
        _steeringTone.Resume();
        _steeringTone.UpdateHeadingError(_smoothedHeadingError, currentSeg.PathWidth);

        // Diagnostic trace — captures raw and smoothed inputs to the steering
        // tone so post-hoc analysis can pinpoint where erratic L/R flipping
        // originates (target jumps vs heading transients vs smoothing residue).
        // Rate-limited inside LogGuidanceFrame; no behavioural effect.
        {
            bool nextIsTurnDiag = false;
            if (_currentSegmentIndex + 1 < _route.Segments.Count)
            {
                var nsDiag = _route.Segments[_currentSegmentIndex + 1];
                nextIsTurnDiag = nsDiag.TurnDirection != "straight" ||
                    Math.Abs(NormalizeAngle(nsDiag.BearingDegrees - currentSeg.BearingDegrees)) > 25.0;
            }
            LogGuidanceFrame(
                lat, lon, headingTrue, groundSpeedKts,
                _currentSegmentIndex, currentSeg.BearingDegrees, currentSeg.PathWidth,
                nextIsTurnDiag, targetLat, targetLon,
                headingError, _smoothedHeadingError);
        }

        // Check for upcoming announcements
        CheckUpcomingAnnouncements(distToTarget, currentSeg);

        // Check hold-short countdown (300/150/50ft cadence) — critical runway incursion prevention
        CheckHoldShortCountdown(distToTarget, currentSeg);

        // Check ground speed warnings (too fast for turn, too fast straight)
        CheckSpeedWarnings(distToTarget, currentSeg);

        // Runway incursion: warn if approaching any off-route hold-short node
        CheckRunwayIncursion(lat, lon);

        // Parking countdown on the final segment (50/20/10 ft)
        CheckParkingCountdown(distToTarget);

        // Check if we're close enough to advance to next segment.
        // IMPORTANT: skip this on the last segment so the parking/arrival countdown
        // (50/20/10 ft) has a chance to fire below 15 m. Arrival for the last segment
        // is driven exclusively by the arrivalRadius check above — WAYPOINT_CAPTURE
        // would otherwise preempt it at 25 m (gates use a 6 m arrivalRadius).
        if (_currentSegmentIndex < _route.Segments.Count - 1 &&
            distToTarget < WAYPOINT_CAPTURE_RADIUS_M)
        {
            AdvanceSegment();
            return;
        }

        // Off-route deviation — perpendicular (cross-track) distance from the segment
        // centerline. Robust version uses **MIN(currentSeg, nextSeg)** so the normal
        // turn arc (aircraft swings outside the current segment's centerline while
        // rolling through the curve) doesn't false-trigger: during the turn the
        // aircraft is legitimately closer to the NEXT segment than the current one,
        // and that still counts as "on route". This was the root cause of the
        // first-turn-causes-reroute bug: wide turns burned the full 3-second
        // persistence window and the recalc then fell back to shortest path,
        // bypassing the ATC-cleared sequence.
        //
        // Tolerance = max(edge half-width + 15 m, OFF_ROUTE_PERP_FLOOR_M), capped
        // at an upper bound so bogus DB widths (we see values up to 4000+ ft in
        // some rows — aprons mis-tagged as taxi paths) don't produce a 600 m
        // tolerance that would mask real deviations.
        // PathWidth is in feet and may be 0 for unnamed connectors; 75 ft is a
        // reasonable default taxiway width — FAA AC 150/5300 Code B/C = 50-82 ft.
        double widthFt = currentSeg.PathWidth > 0 ? currentSeg.PathWidth : DEFAULT_TAXIWAY_WIDTH_FT;
        if (widthFt > OFF_ROUTE_PERP_WIDTH_CAP_FT) widthFt = OFF_ROUTE_PERP_WIDTH_CAP_FT;
        double widthM = widthFt * 0.3048;
        double halfWidth = widthM * 0.5;
        double perpTolerance = Math.Max(halfWidth + OFF_ROUTE_PERP_MARGIN_M, OFF_ROUTE_PERP_FLOOR_M);

        double perpCurrent = PerpendicularDistanceToSegmentMeters(
            lat, lon,
            currentSeg.FromNode.Latitude, currentSeg.FromNode.Longitude,
            currentSeg.ToNode.Latitude, currentSeg.ToNode.Longitude);

        // Perp distance to the NEXT segment (if any) — during a turn the aircraft
        // transitions from "close to current" → "close to next" and the MIN of the
        // two stays small throughout. Only meaningful when there IS a next segment.
        double perp = perpCurrent;
        if (_currentSegmentIndex + 1 < _route.Segments.Count)
        {
            var nextSeg = _route.Segments[_currentSegmentIndex + 1];
            double perpNext = PerpendicularDistanceToSegmentMeters(
                lat, lon,
                nextSeg.FromNode.Latitude, nextSeg.FromNode.Longitude,
                nextSeg.ToNode.Latitude, nextSeg.ToNode.Longitude);
            if (perpNext < perp) perp = perpNext;
        }

        // Also require that we're not simply past the segment's end (the endpoint-advance
        // check already handled forward progress; here we only flag lateral drift or
        // backtracking significantly past the start).
        double distFromSegStart = TaxiGraph.FastDistanceMeters(
            lat, lon, currentSeg.FromNode.Latitude, currentSeg.FromNode.Longitude);
        bool farBehindStart = distFromSegStart > currentSeg.DistanceMeters + 50.0
                              && distToTarget > distFromSegStart;

        // Suspend off-route detection when we're close to a turn. Even with the
        // min(current, next) trick, very tight 90°+ turns can briefly exceed both
        // centerlines. The planned turn geometry is known — just trust it.
        bool nearTurn = false;
        if (_currentSegmentIndex + 1 < _route.Segments.Count)
        {
            var ns = _route.Segments[_currentSegmentIndex + 1];
            if (ns.TurnDirection != "straight")
            {
                // Approaching turn: use the same speed-scaled window as the imminent-turn
                // announcement, with a fixed floor so even at 0 kt we still suspend
                // within ~40 m of the turn node.
                double turnWin = Math.Max(
                    _lastGroundSpeedKts * 0.5144 * TURN_IMMINENT_SEC_LEAD,
                    40.0);
                if (distToTarget < turnWin) nearTurn = true;
            }
        }
        // Also suspend for a short grace window AFTER completing a turn — the
        // aircraft is still settling onto the new centerline while we've already
        // advanced _currentSegmentIndex. Uses segment-advance timestamp below.
        if ((DateTime.Now - _lastSegmentAdvanceTime).TotalSeconds < POST_TURN_OFFROUTE_GRACE_SEC)
            nearTurn = true;

        bool offRouteNow = !nearTurn && (perp > perpTolerance || farBehindStart);

        // Persistence: off-route must be sustained for N seconds AND the aircraft
        // must actually be moving. This kills two bugs:
        //  1. Stationary-at-gate recalc spam — initial position is never exactly on
        //     a graph centerline, so a stationary aircraft can be "off-route" by
        //     definition. Requiring GS ≥ 2 kt means a parked plane never triggers.
        //  2. Single-sample GPS jitter — a transient lateral blip should not kick
        //     off a reroute. Must hold > OFF_ROUTE_PERSISTENCE_SEC consecutively.
        if (offRouteNow && _lastGroundSpeedKts >= OFF_ROUTE_MIN_GS_KTS)
        {
            if (_offRouteSince == DateTime.MinValue)
                _offRouteSince = DateTime.Now;

            if ((DateTime.Now - _offRouteSince).TotalSeconds >= OFF_ROUTE_PERSISTENCE_SEC)
            {
                _offRouteSince = DateTime.MinValue;  // reset so next off-route starts fresh
                TryRecalculateRoute(lat, lon, headingTrue);
            }
        }
        else
        {
            // Reset the persistence timer whenever we're back inside tolerance
            // (or not moving — a stationary off-route sample shouldn't count toward
            // the persistence window either).
            _offRouteSince = DateTime.MinValue;
        }
        } // end lock(_stateLock)
    }

    /// <summary>
    /// Gets a guidance target point at least GUIDANCE_LOOK_AHEAD_M meters ahead on the route.
    /// This smooths steering through short navdata segments that would otherwise cause jitter.
    ///
    /// CRITICAL: Never look past a turn. If the next segment is a turn (>25°), the look-ahead
    /// is clamped to the current segment's ToNode — otherwise the tone would push the blind
    /// pilot to cut the corner diagonally through grass.
    /// </summary>
    private (double lat, double lon) GetGuidanceTarget(double aircraftLat, double aircraftLon)
    {
        if (_route == null || _currentSegmentIndex >= _route.Segments.Count)
            return (aircraftLat, aircraftLon);

        var currentSeg = _route.Segments[_currentSegmentIndex];

        // If the NEXT segment is a turn, never look past this segment's end (no corner cutting)
        bool nextIsTurn = false;
        if (_currentSegmentIndex + 1 < _route.Segments.Count)
        {
            var nextSeg = _route.Segments[_currentSegmentIndex + 1];
            nextIsTurn = nextSeg.TurnDirection != "straight" ||
                         Math.Abs(NormalizeAngle(nextSeg.BearingDegrees - currentSeg.BearingDegrees)) > 25.0;
        }

        // Start from current segment's ToNode
        double accumulatedDist = TaxiGraph.FastDistanceMeters(
            aircraftLat, aircraftLon,
            currentSeg.ToNode.Latitude,
            currentSeg.ToNode.Longitude);

        // Before a turn: always aim at this segment's endpoint
        if (nextIsTurn)
        {
            return (currentSeg.ToNode.Latitude, currentSeg.ToNode.Longitude);
        }

        // If the immediate ToNode is already far enough, use it
        if (accumulatedDist >= GUIDANCE_LOOK_AHEAD_M)
        {
            return (currentSeg.ToNode.Latitude, currentSeg.ToNode.Longitude);
        }

        // Otherwise, keep looking further along the route (stop at first turn)
        for (int i = _currentSegmentIndex + 1; i < _route.Segments.Count; i++)
        {
            var seg = _route.Segments[i];

            // Stop if this segment is a turn — use previous segment's ToNode as target
            bool thisIsTurn = seg.TurnDirection != "straight" ||
                              (i > 0 && Math.Abs(NormalizeAngle(
                                  seg.BearingDegrees - _route.Segments[i - 1].BearingDegrees)) > 25.0);
            if (thisIsTurn)
            {
                return (_route.Segments[i - 1].ToNode.Latitude, _route.Segments[i - 1].ToNode.Longitude);
            }

            accumulatedDist += seg.DistanceMeters;
            if (accumulatedDist >= GUIDANCE_LOOK_AHEAD_M)
            {
                return (seg.ToNode.Latitude, seg.ToNode.Longitude);
            }
        }

        // If the entire remaining route is <50m, aim for the final destination
        var lastSeg = _route.Segments[^1];
        return (lastSeg.ToNode.Latitude, lastSeg.ToNode.Longitude);
    }

    /// <summary>
    /// Hold-short countdown cadence — critical for runway incursion prevention.
    /// Announces at 300ft, 150ft, 50ft from hold-short node. Only fires on segments
    /// ending at a hold-short.
    /// </summary>
    private void CheckHoldShortCountdown(double distToTargetM, TaxiRouteSegment currentSeg)
    {
        if (!currentSeg.IsHoldShortPoint)
        {
            // Reset flags when not on a hold-short-ending segment
            _holdShortAnnounce300 = false;
            _holdShortAnnounce150 = false;
            _holdShortAnnounce50 = false;
            return;
        }

        double distFeet = distToTargetM * METERS_TO_FEET;
        string what = !string.IsNullOrEmpty(currentSeg.HoldShortRunway)
            ? currentSeg.HoldShortRunway
            : "hold short";

        // Fire thresholds in natural countdown order (300 → 150 → 50). Independent
        // `if` blocks so a fast approach that first samples inside 50 ft still fires
        // the earlier callouts the pilot "missed" — safety-critical for runway
        // incursion prevention. One announce per frame keeps cadence tight without
        // stacking three simultaneous utterances on one speak call.
        // Speed-aware callout suffixes. The user's principle: every directive
        // ("slow down", "stop") should fire only when actually needed. Telling
        // a pilot already at 5 kt to "slow down" is noise; telling a stopped
        // pilot to "stop" is patronising. Thresholds picked from typical
        // braking distances:
        //   At 150 ft, 10 kt → ~9 s to hold line: comfortable, no slow-down
        //   needed. At >10 kt, slow-down is genuine.
        //   At 50 ft, ≤1 kt → already at a crawl, "Stop" is redundant; > 1 kt
        //   → real stopping action required.
        const double SLOW_DOWN_GS_THRESHOLD_KTS = 10.0;
        const double STOP_GS_THRESHOLD_KTS = 1.0;

        if (distFeet < 300 && !_holdShortAnnounce300)
        {
            AnnounceInstruction($"Hold short {what} in 300 feet.");
            _holdShortAnnounce300 = true;
            return;
        }
        if (distFeet < 150 && !_holdShortAnnounce150)
        {
            string msg = $"Hold short {what} in 150 feet.";
            if (_lastGroundSpeedKts > SLOW_DOWN_GS_THRESHOLD_KTS) msg += " Slow down.";
            AnnounceInstruction(msg);
            _holdShortAnnounce150 = true;
            return;
        }
        if (distFeet < 50 && !_holdShortAnnounce50)
        {
            string msg = $"Hold short {what} in 50 feet.";
            if (_lastGroundSpeedKts > STOP_GS_THRESHOLD_KTS) msg += " Stop.";
            AnnounceInstruction(msg);
            _holdShortAnnounce50 = true;
        }
    }

    /// <summary>
    /// Warns if ground speed is too high for the current situation.
    /// Per FAA / major-carrier SOPs:
    ///   - >30 kt straight: "Taxi speed" (exceeding max straight-line taxi speed)
    ///   - >10 kt with a normal turn ahead: "Slow for turn"
    ///   - >8 kt with a SHARP (≥60°) turn ahead: "Slow for sharp turn"
    /// Lookahead scales with speed (6 s at current GS, clamped 40–150 m) so the
    /// warning fires early enough to actually bleed off energy before the turn.
    /// Cooldown prevents spam.
    /// </summary>
    private void CheckSpeedWarnings(double distToTargetM, TaxiRouteSegment currentSeg)
    {
        if (_route == null) return;
        if (_lastGroundSpeedKts < 5) return;  // not taxiing

        if ((DateTime.Now - _lastSpeedWarningTime).TotalSeconds < SPEED_WARNING_COOLDOWN_SEC)
            return;

        // Speed-scaled lookahead — at 20 kt (≈10 m/s), 6 s = 60 m of lead.
        double turnWarnDist = Math.Clamp(
            _lastGroundSpeedKts * 0.5144 * TURN_SPEED_WARN_SEC_LEAD,
            TURN_SPEED_WARN_MIN_M, TURN_SPEED_WARN_MAX_M);

        bool normalTurnComing = false;
        bool sharpTurnComing = false;
        int nextIdx = _currentSegmentIndex + 1;
        if (nextIdx < _route.Segments.Count)
        {
            var next = _route.Segments[nextIdx];
            if (next.TurnDirection != "straight" && distToTargetM < turnWarnDist)
            {
                double ang = Math.Abs(next.TurnAngleDegrees);
                if (ang >= SHARP_TURN_ANGLE_DEG) sharpTurnComing = true;
                else normalTurnComing = true;
            }
        }

        if (sharpTurnComing && _lastGroundSpeedKts > MAX_TAXI_SPEED_SHARP_TURN_KTS)
        {
            _announcer.AnnounceImmediate("Slow for sharp turn.");
            _lastSpeedWarningTime = DateTime.Now;
        }
        else if (normalTurnComing && _lastGroundSpeedKts > MAX_TAXI_SPEED_TURN_KTS)
        {
            _announcer.AnnounceImmediate("Slow for turn.");
            _lastSpeedWarningTime = DateTime.Now;
        }
        else if (!normalTurnComing && !sharpTurnComing && _lastGroundSpeedKts > MAX_TAXI_SPEED_STRAIGHT_KTS)
        {
            _announcer.AnnounceImmediate($"Taxi speed, {(int)_lastGroundSpeedKts} knots.");
            _lastSpeedWarningTime = DateTime.Now;
        }
    }

    /// <summary>
    /// Runway incursion guard. Two cases, different wording:
    ///   1. Aircraft is approaching a hold-short node that lies ON the planned route
    ///      → "Crossing runway X" (informational — cleared to cross per ATC)
    ///   2. Aircraft is approaching a hold-short node OFF the planned route
    ///      → "Warning: approaching runway X, off route" (red flag — wrong turn)
    /// The scheduled-hold-short (user-checked) case is silent here because
    /// CheckHoldShortCountdown owns those callouts.
    /// </summary>
    private void CheckRunwayIncursion(double lat, double lon)
    {
        if (_graph == null || _route == null) return;

        // Currently-scheduled hold-short node (owned by CheckHoldShortCountdown).
        int scheduledHsNodeId = -1;
        if (_currentSegmentIndex < _route.Segments.Count &&
            _route.Segments[_currentSegmentIndex].IsHoldShortPoint)
        {
            scheduledHsNodeId = _route.Segments[_currentSegmentIndex].ToNode.NodeId;
        }

        if (_lastIncursionWarnedNodeId != -1 &&
            (DateTime.Now - _lastIncursionWarningTime).TotalSeconds < INCURSION_WARNING_COOLDOWN_SEC)
            return;

        // Build the set of HS node-IDs that lie on the remaining planned route
        // (so "crossing" announcements fire for them; "off route" does not).
        var onRouteHsNodes = new HashSet<int>();
        for (int i = _currentSegmentIndex; i < _route.Segments.Count; i++)
        {
            var seg = _route.Segments[i];
            if (seg.ToNode != null &&
                (seg.ToNode.Type == TaxiNodeType.HoldShort || seg.ToNode.Type == TaxiNodeType.ILSHoldShort))
            {
                onRouteHsNodes.Add(seg.ToNode.NodeId);
            }
        }

        // Scan for nearby HS nodes
        TaxiNode? nearestHs = null;
        double bestDist = INCURSION_WARN_DISTANCE_M;
        foreach (var node in _graph.Nodes.Values)
        {
            if (node.Type != TaxiNodeType.HoldShort && node.Type != TaxiNodeType.ILSHoldShort)
                continue;
            if (node.NodeId == scheduledHsNodeId)
                continue; // countdown owns this one

            double d = TaxiGraph.FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (d < bestDist)
            {
                bestDist = d;
                nearestHs = node;
            }
        }

        if (nearestHs == null || nearestHs.NodeId == _lastIncursionWarnedNodeId)
            return;

        string rwy = !string.IsNullOrEmpty(nearestHs.HoldShortName) ? nearestHs.HoldShortName : "runway";

        if (onRouteHsNodes.Contains(nearestHs.NodeId))
        {
            // Planned crossing — informational, not a warning
            _announcer.AnnounceImmediate($"Crossing {rwy}.");
        }
        else
        {
            _announcer.AnnounceImmediate($"Warning: approaching {rwy}, off route.");
        }

        _lastIncursionWarnedNodeId = nearestHs.NodeId;
        _lastIncursionWarningTime = DateTime.Now;
    }

    /// <summary>
    /// Final-approach distance countdown when taxiing toward destination (gate or runway).
    /// Fires only on the last route segment and only below each threshold.
    ///
    /// For runway destinations the last segment is tagged as a hold-short (via
    /// TruncateToHoldShort) — in that case CheckHoldShortCountdown owns the 300/150/50 ft
    /// cadence and this method bails out, otherwise the pilot would hear overlapping
    /// "Hold short Runway 27L in 50 feet. Stop." + "Runway 27L in 50 feet." callouts.
    /// </summary>
    private void CheckParkingCountdown(double distToTargetM)
    {
        if (_route == null) return;
        if (_currentSegmentIndex != _route.Segments.Count - 1)
        {
            _parkingAnnounce50 = false;
            _parkingAnnounce20 = false;
            _parkingAnnounce10 = false;
            return;
        }

        // Runway destination: hold-short countdown owns the callouts — skip parking.
        if (_route.Segments[_currentSegmentIndex].IsHoldShortPoint)
            return;

        double feet = distToTargetM * METERS_TO_FEET;

        // Fire thresholds in natural order (50 → 20 → 10). Independent `if` blocks so
        // a fast arrival that first samples inside 20 ft or 10 ft still fires the
        // earlier callouts. One announce per frame to avoid stacking.
        if (feet < 50 && !_parkingAnnounce50)
        {
            AnnounceInstruction($"{_destinationName} in 50 feet.");
            _parkingAnnounce50 = true;
            return;
        }
        if (feet < 20 && !_parkingAnnounce20)
        {
            AnnounceInstruction("20 feet.");
            _parkingAnnounce20 = true;
            return;
        }
        if (feet < 10 && !_parkingAnnounce10)
        {
            // Speed-aware: skip "Stop" if already essentially stopped (≤1 kt).
            string msg = _lastGroundSpeedKts > 1.0 ? "10 feet. Stop." : "10 feet.";
            AnnounceInstruction(msg);
            _parkingAnnounce10 = true;
        }
    }

    /// <summary>
    /// Advances _currentSegmentIndex to the segment closest to the aircraft's current position.
    /// Only moves forward (never backward). Handles segment skipping when updates are sparse.
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

        if (bestIdx > _currentSegmentIndex)
        {
            // Check for hold-short points we might be skipping
            for (int i = _currentSegmentIndex; i < bestIdx; i++)
            {
                if (_route.Segments[i].IsHoldShortPoint)
                {
                    _currentSegmentIndex = i + 1;
                    _lastSegmentAdvanceTime = DateTime.Now;
                    HandleHoldShort(_route.Segments[i]);
                    return;
                }
            }

            _currentSegmentIndex = bestIdx;
            _lastSegmentAdvanceTime = DateTime.Now;

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
    /// Attempts to recalculate the route from the current position to the destination.
    /// </summary>
    private void TryRecalculateRoute(double lat, double lon, double headingTrue)
    {
        if (_graph == null) return;

        if ((DateTime.Now - _lastRecalculationTime).TotalSeconds < RECALCULATION_COOLDOWN_SEC)
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

        _lastRecalculationTime = DateTime.Now;

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
        int? destComponentId = _graph.Nodes.ContainsKey(_destinationNodeId)
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
            newRoute = router.FindConstrainedPath(nearestNode.NodeId, _destinationNodeId, remainingSequence);
        else if (_originalTaxiwaySequence != null && _originalTaxiwaySequence.Count > 0)
            newRoute = router.FindConstrainedPath(nearestNode.NodeId, _destinationNodeId, _originalTaxiwaySequence);
        else
            newRoute = router.FindShortestPath(nearestNode.NodeId, _destinationNodeId);

        if (newRoute == null || newRoute.Segments.Count == 0)
        {
            _announcer.AnnounceImmediate("Off route. Unable to recalculate.");
            return;
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
            TruncateToHoldShort(newRoute, _destinationName);

        _route = newRoute;
        _currentSegmentIndex = 0;
        _approachAnnounced = false;
        _turnImminentAnnounced = false;
        _crossingAnnounced = false;
        _lastCrossingNodeId = -1;
        // Reset countdown latches — otherwise stale flags from the OLD route
        // will suppress the 300/150/50ft hold-short and 50/20/10ft parking
        // callouts on the NEW route. Safety-critical.
        _holdShortAnnounce300 = _holdShortAnnounce150 = _holdShortAnnounce50 = false;
        _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
        _lastIncursionWarnedNodeId = -1;
        _headingErrorInitialized = false;

        string firstTaxiway = newRoute.Segments[0].TaxiwayName;
        string taxiStr = !string.IsNullOrEmpty(firstTaxiway) ? $" Taxiway {firstTaxiway}." : "";
        string distStr = FormatDistance(newRoute.TotalDistanceMeters);
        AnnounceInstruction($"Recalculating. {distStr} to {_destinationName}.{taxiStr}");

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
            _lastSegmentAdvanceTime = DateTime.Now;
            HandleHoldShort(completedSeg);
            return;
        }

        _currentSegmentIndex++;
        _lastSegmentAdvanceTime = DateTime.Now;
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

    private void HandleHoldShort(TaxiRouteSegment holdShortSeg)
    {
        _steeringTone.Pause();
        SetState(TaxiGuidanceState.HoldShort);

        string holdOf = !string.IsNullOrEmpty(holdShortSeg.HoldShortRunway)
            ? $" of {holdShortSeg.HoldShortRunway}"
            : "";
        AnnounceInstruction($"Stop. Hold short{holdOf}. Press continue when cleared.");
    }

    public void ContinuePastHoldShort()
    {
        lock (_stateLock)
        {
        if (_state != TaxiGuidanceState.HoldShort || _route == null) return;

        // Holding short AT the destination runway: Continue means pilot has been
        // cleared (line up and wait, or cleared for takeoff). Transition into the
        // lineup phase rather than back into regular Taxiing. The existing lineup
        // state machine handles centerline + heading guidance from here.
        //
        // LUAW reminder: the pilot may be entering under "line up and wait" only
        // (NOT a takeoff clearance, per AIM 5-2-5). If so they must stop once
        // aligned on the centerline and wait for takeoff clearance. The takeoff
        // assist module handles the actual takeoff roll — we just get them aligned.
        if (_holdShortAtDestination)
        {
            _holdShortAtDestination = false;
            SetState(TaxiGuidanceState.LiningUp);
            _lineupAnnouncedAligned = false;
            _steeringTone.Resume();

            // CRITICAL: reset the heading-error smoother so the lineup tone starts
            // from a clean slate. Without this, the smoothed error carries the
            // last value from the taxi-phase low-pass filter — which on a
            // connector taxiway is a large turn-into-the-runway value (e.g.,
            // 50–80°). The 25 %/frame filter then takes ~300 ms to decay, and
            // for that first ~10 frames the tone is still chasing the taxi-phase
            // error, not the lineup error. At 5 kt that's enough to drift well
            // off centerline before the tone catches up.
            _smoothedHeadingError = 0.0;
            _headingErrorInitialized = false;

            int hdgAnnounce = (int)Math.Round(_lineupHeadingMag);
            double headingError = NormalizeAngle(_lineupHeadingTrue - _lastHeading);
            string turnDir = Math.Abs(headingError) < 10 ? "" :
                (headingError > 0 ? " Turn right." : " Turn left.");

            // Terse — pilot already knows what they were cleared for when they pressed Continue.
            // The TONE does the actual lineup work; verbal info is just an event marker. We
            // intentionally do NOT speak feet-from-centerline ("42 ft left of CL" means nothing
            // to a blind pilot — there's no spatial reference). Heading is fine because every
            // pilot has heading from their instruments; cross-track guidance must come through
            // the tone, which is now tuned for the precision this phase needs.
            AnnounceInstruction($"Entering {_destinationName}, heading {hdgAnnounce}.{turnDir}");
            return;
        }

        SetState(TaxiGuidanceState.Taxiing);
        _steeringTone.Resume();

        // Reset approach/crossing flags so the first turn or crossing on the new leg
        // isn't suppressed by flags left set during the pre-hold-short approach.
        // Also reset the hold-short countdown latches — the next leg may have its
        // own hold-short and needs fresh 300/150/50ft callouts.
        _approachAnnounced = false;
        _turnImminentAnnounced = false;
        _crossingAnnounced = false;
        _lastCrossingNodeId = -1;
        _holdShortAnnounce300 = _holdShortAnnounce150 = _holdShortAnnounce50 = false;

        if (_currentSegmentIndex < _route.Segments.Count)
        {
            var seg = _route.Segments[_currentSegmentIndex];
            string taxiway = !string.IsNullOrEmpty(seg.TaxiwayName) ? $"Taxiway {seg.TaxiwayName}. " : "";
            // Update the announced-taxiway tracker so AdvanceToNearestSegment doesn't
            // redundantly re-announce the same taxiway a few frames later.
            _lastAnnouncedTaxiway = seg.TaxiwayName ?? "";
            AnnounceInstruction($"Continuing. {taxiway}");
        }
        else
        {
            AnnounceInstruction("Continuing.");
        }
        } // end lock(_stateLock)
    }

    private void HandleArrival()
    {
        // FAA AIM 4-3-18, ATC 7110.65 §3-7-2, and §3-7-2-h: an ATC taxi clearance
        // NEVER authorizes entering or crossing any runway — including the assigned
        // takeoff runway. Pilot must hold short until given "cleared to cross",
        // "line up and wait" (LUAW, the ICAO-aligned phrase that replaced
        // "position and hold" in Sep 2010), or "cleared for takeoff".
        //
        // Additionally, FAA rule change: explicit crossing clearance is required for
        // EACH runway — controllers issue them one at a time, an aircraft must have
        // crossed the previous runway before the next crossing clearance is issued.
        //
        // For safety we default to HOLD SHORT at any runway destination and require
        // an explicit Continue (pilot's confirmation they have the clearance) before
        // we transition into the lineup phase.
        if (_hasLineupTarget && _isRunwayLineup)
        {
            _holdShortAtDestination = true;
            _steeringTone.Pause();
            SetState(TaxiGuidanceState.HoldShort);

            string rwy = _destinationName;
            AnnounceInstruction($"Stop. Hold short of {rwy}. Press continue when cleared.");
            return;
        }

        // Gate / parking destination with lineup heading data — guide heading.
        if (_hasLineupTarget)
        {
            SetState(TaxiGuidanceState.LiningUp);
            _lineupAnnouncedAligned = false;

            // Same reset as the runway-lineup path — start the lineup tone from
            // a clean smoother instead of carrying taxi-phase residual.
            _smoothedHeadingError = 0.0;
            _headingErrorInitialized = false;

            int hdgAnnounce = (int)Math.Round(_lineupHeadingMag);
            double headingError = NormalizeAngle(_lineupHeadingTrue - _lastHeading);
            string turnDir = Math.Abs(headingError) < 10 ? "" :
                (headingError > 0 ? " Turn right." : " Turn left.");

            AnnounceInstruction($"Align with {_destinationName}, heading {hdgAnnounce}.{turnDir}");

            // Keep tone going for lineup guidance
            _steeringTone.Resume();
            return;
        }

        // No lineup data — just stop
        _steeringTone.Stop();
        SetState(TaxiGuidanceState.Arrived);
        AnnounceInstruction($"{_route?.DestinationName ?? "Destination"} reached.");
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

        // Heading deviation from runway centerline. Positive sign matters
        // less than magnitude here — the question is whether the pilot has
        // started turning yet.
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
        bool turnBegun = hdgDeltaAbs >= ROLLOUT_TURN_BEGAN_HDG_DEG;

        // DIAGNOSTIC: periodic snapshot (every ~3s) of rollout state.
        // Captures the moment the per-frame loop is or isn't seeing the
        // distance threshold approach.
        if ((DateTime.Now - _rolloutDiagLastPeriodic).TotalSeconds >= 3.0)
        {
            _rolloutDiagLastPeriodic = DateTime.Now;
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
        if (turnBegun || (atTaxiSpeed && nearExit && !pastExit))
        {
            RolloutDiag($"UpdateLandingRollout HANDOFF -> Taxiing: " +
                $"turnBegun={turnBegun} atTaxiSpeed={atTaxiSpeed} nearExit={nearExit} pastExit={pastExit}");
            SetState(TaxiGuidanceState.Taxiing);
            _steeringTone.Resume();
            // The next UpdatePosition tick will re-enter via the Taxiing
            // branch, AdvanceToNearestSegment will pick the route segment
            // closest to current position, and the tone will guide the
            // pilot down the exit taxiway as normal.
            return;
        }

        // Overshoot detection. If the aircraft has rolled past the chosen
        // exit along the runway centerline WITHOUT starting the turn, pick
        // the next downfield exit and retarget. If no exits remain on the
        // runway, end the rollout gracefully so the off-route recalc path
        // can't route back across the runway to the now-passed exit (which
        // was the original bug this work fixes).
        if (signedAlongPastFt >= ROLLOUT_OVERSHOOT_FT && hdgDeltaAbs < ROLLOUT_TURN_BEGAN_HDG_DEG)
        {
            RolloutDiag($"OVERSHOOT detected: signedAlongPast={signedAlongPastFt:F0}ft hdgDelta={hdgDeltaAbs:F1}deg");

            // Sorted list — first exit further downfield than the current one
            // (with ROLLOUT_OVERSHOOT_FT sentinel to skip the current one and
            // any near-duplicates) is the next exit.
            Navigation.LandingExit? nextExit = null;
            foreach (var e in _rolloutAllExits)
            {
                if (e.DistanceFromThresholdFeet > _rolloutExit.DistanceFromThresholdFeet + ROLLOUT_OVERSHOOT_FT)
                {
                    nextExit = e;
                    break;
                }
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

        // Approach callouts. Each fires once per rollout (flags are reset
        // in BeginLandingRollout). Use ">= threshold" with a generous
        // window so a fast aircraft skipping past 1500 ft between frames
        // doesn't lose the announcement.
        string exitClass = _rolloutExit.ExitType switch
        {
            "High-speed" => "high-speed exit",
            "End"        => "runway-end exit",
            _            => "exit"
        };
        string name = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
            ? "exit"
            : $"taxiway {_rolloutExit.TaxiwayName}";

        if (!_rolloutApproach1500Announced && distToExitFeet <= 1500.0 && distToExitFeet > 500.0)
        {
            RolloutDiag($"1500-ft approach callout firing: distToExit={distToExitFeet:F0}ft");
            AnnounceInstruction($"Approaching {exitClass} {name}, 1500 feet.");
            _rolloutApproach1500Announced = true;
        }

        if (!_rolloutApproach500Announced && distToExitFeet <= 500.0 && distToExitFeet > 150.0)
        {
            RolloutDiag($"500-ft approach callout firing: distToExit={distToExitFeet:F0}ft gs={groundSpeedKts:F1}");
            string slowSuffix = groundSpeedKts > ROLLOUT_TAXI_GS_KTS ? " Slow down." : "";
            AnnounceInstruction($"{CapFirst(name)}, 500 feet.{slowSuffix}");
            _rolloutApproach500Announced = true;
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
            AnnounceInstruction($"Turn {dir} now, {name}.");
            _rolloutTurnNowAnnounced = true;
        }
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
        // beginning a backtaxi). After transitioning, _route is still null
        // so Taxiing's off-route recalc cannot fire.
        bool effectivelyStopped = groundSpeedKts < ROLLOUT_NO_EXIT_STOPPED_GS_KTS;
        bool turnBegun = hdgDeltaAbs >= ROLLOUT_TURN_BEGAN_HDG_DEG;
        if (effectivelyStopped || turnBegun)
        {
            _rolloutNoExitMode = false;
            SetState(TaxiGuidanceState.Taxiing);
            // Tone stays paused — no route to steer toward.
            return;
        }

        // Past the runway end already (overrun / off the pavement). The
        // three countdown callouts have either fired or been skipped past;
        // stay quiet here and rely on the stopped/turn transitions above
        // to retire the countdown when the pilot stops or maneuvers.
        if (distToEndFt <= 0) return;

        if (!_rolloutEnd1500Announced && distToEndFt <= 1500.0 && distToEndFt > 500.0)
        {
            AnnounceInstruction("Runway end in 1500 feet.");
            _rolloutEnd1500Announced = true;
        }

        if (!_rolloutEnd500Announced && distToEndFt <= 500.0 && distToEndFt > 100.0)
        {
            // "Slow down" is added only when the pilot still has real speed
            // to bleed off — mirrors the hold-short countdown's speed-aware
            // suffix rule. ROLLOUT_TAXI_GS_KTS (30) is the threshold below
            // which the aircraft is at normal taxi speed and the suffix is
            // patronising noise.
            string slowSuffix = groundSpeedKts > ROLLOUT_TAXI_GS_KTS ? " Slow down." : "";
            AnnounceInstruction($"Runway end in 500 feet.{slowSuffix}");
            _rolloutEnd500Announced = true;
        }

        if (!_rolloutEnd100Announced && distToEndFt <= 100.0)
        {
            // "Stop" is added only when the pilot still has speed to lose.
            // ≤ 1 kt is the threshold the hold-short countdown uses for the
            // same wording rule — at that point the aircraft is crawling
            // and the directive is redundant.
            string stopSuffix = groundSpeedKts > 1.0 ? " Stop." : "";
            AnnounceInstruction($"Runway end in 100 feet.{stopSuffix}");
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
    private void RetargetLandingExit(Navigation.LandingExit newExit, double lat, double lon, double headingTrue)
    {
        if (_rolloutExit == null || _dataProvider == null || _graph == null)
        {
            EnterRunwayEndCountdown();
            return;
        }

        string prevName = string.IsNullOrEmpty(_rolloutExit.TaxiwayName)
            ? "exit"
            : $"taxiway {_rolloutExit.TaxiwayName}";
        string newName = string.IsNullOrEmpty(newExit.TaxiwayName)
            ? "next exit"
            : $"taxiway {newExit.TaxiwayName}";
        int distAheadFt = (int)Math.Round(
            TaxiGraph.FastDistanceMeters(lat, lon, newExit.Latitude, newExit.Longitude)
            * METERS_TO_FEET);

        string destNameForRoute = newExit.TaxiwayName.Length > 0
            ? $"Taxiway {newExit.TaxiwayName}"
            : "Exit";

        // LoadRoute acquires _stateLock; same-thread reentrancy is safe.
        // announceSummary:false suppresses the "Route to X via Y" callout so
        // the only spoken line from this transition is our retarget message
        // below.
        string? error = LoadRoute(
            _dataProvider, _icao,
            lat, lon, headingTrue,
            newExit.NodeId,
            destNameForRoute,
            taxiwaySequence: null,
            prebuiltGraph: _graph,
            announceSummary: false,
            isRunwayDestination: false);

        if (error != null)
        {
            AnnounceInstruction($"Missed {prevName}. Could not retarget: {error}.");
            EnterRunwayEndCountdown();
            return;
        }

        _rolloutExit = newExit;
        _rolloutApproach1500Announced = false;
        _rolloutApproach500Announced = false;
        _rolloutTurnNowAnnounced = false;

        // LoadRoute set state to RouteLoaded. Re-enter LandingRollout so the
        // next UpdatePosition frame re-runs UpdateLandingRollout (tone stays
        // paused, approach callouts re-armed, overshoot detector active for
        // the new exit).
        SetState(TaxiGuidanceState.LandingRollout);

        AnnounceInstruction($"Missed {prevName}. Retargeting {newName}, {distAheadFt} feet ahead.");
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
    private void EnterRunwayEndCountdown()
    {
        _route = null;
        _destinationNodeId = 0;
        _currentSegmentIndex = 0;
        _originalTaxiwaySequence = null;
        _rolloutExit = null;
        // KEEP _rolloutRunway and _rolloutRunwayHeadingTrue — countdown needs them.
        _rolloutAllExits = new List<Navigation.LandingExit>();
        _rolloutApproach1500Announced = false;
        _rolloutApproach500Announced = false;
        _rolloutTurnNowAnnounced = false;
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
            _steeringTone.UpdateHeadingErrorWithThresholds(
                _smoothedHeadingError,
                silentThresholdDeg: 0.5,
                activationThresholdDeg: 1.0,
                maxPanThresholdDeg: 15.0);

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
            }
            else if (_lineupAnnouncedAligned && !stillAligned)
            {
                _lineupAnnouncedAligned = false;
                _steeringTone.Resume();
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
            _steeringTone.SetPulse(stoppedAndMisaligned);
        }
        else
        {
            // Gate lineup never pulses — pulse cue is for runway lineup only,
            // where the pilot may sit on the runway misaligned for a long time
            // ("line up and wait"). At a gate, you're either taxiing in or
            // parked; pulse would be noise.
            _steeringTone.SetPulse(false);

            double headingError = NormalizeAngle(_lineupHeadingTrue - headingTrue);
            // Gate lineup: heading only (gates aren't lines)
            _smoothedHeadingError = _headingErrorInitialized
                ? _smoothedHeadingError * (1 - HEADING_ERROR_FILTER_ALPHA) + headingError * HEADING_ERROR_FILTER_ALPHA
                : headingError;
            _headingErrorInitialized = true;
            // Gate lineup: use baseline (60 ft) — no strong reason to widen or
            // tighten, and gates don't have a well-defined "width" concept.
            _steeringTone.UpdateHeadingError(_smoothedHeadingError);

            // Hysteresis (gate): enter ±4°, exit ±7°
            double enterHdg = LINEUP_HEADING_TOLERANCE_DEG - 1.0;
            double exitHdg  = LINEUP_HEADING_TOLERANCE_DEG + 2.0;
            bool enterAligned = Math.Abs(headingError) < enterHdg;
            bool stillAligned = Math.Abs(headingError) < exitHdg;

            // Same as runway branch — stay in LiningUp; mute/resume via hysteresis.
            if (!_lineupAnnouncedAligned && enterAligned)
            {
                _lineupAnnouncedAligned = true;
                _steeringTone.Pause();
                _announcer.AnnounceImmediate($"Aligned with {_destinationName}. Parking brake.");
            }
            else if (_lineupAnnouncedAligned && !stillAligned)
            {
                _lineupAnnouncedAligned = false;
                _steeringTone.Resume();
            }
        }
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
    private void TruncateToHoldShort(TaxiRoute route, string destinationName)
    {
        if (route.Segments.Count == 0) return;

        // Pass 1: prefer an IHS (ILSHoldShort) node over plain HS when both
        // exist. IHS sits further back from the threshold to clear the ILS
        // critical area — the safer stop for an ILS-equipped runway. For
        // non-ILS runways, the two types collapse to "whichever is latest."
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

        int truncateAt = truncateAtIHS >= 0 ? truncateAtIHS : truncateAtHS;

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
        if (truncateAt < 0 && _hasLineupTarget)
        {
            for (int i = route.Segments.Count - 1; i >= 0; i--)
            {
                var to = route.Segments[i].ToNode;
                if (to == null) continue;
                double d = TaxiGraph.FastDistanceMeters(
                    to.Latitude, to.Longitude, _lineupTargetLat, _lineupTargetLon);
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
    private void InsertRunwayCrossingHoldShorts(TaxiRoute route, string destinationName)
    {
        if (route == null || route.Segments.Count < 2) return;
        if (_graph == null || _graph.RunwayCenterlines.Count == 0) return;

        // Tracks the runway whose hold-short was most recently inserted, so we
        // don't tag every consecutive segment that's on the same runway pavement.
        string lastTaggedRunway = "";

        for (int i = 0; i < route.Segments.Count - 1; i++)
        {
            var seg = route.Segments[i];
            var nextSeg = route.Segments[i + 1];
            if (nextSeg.ToNode == null || seg.ToNode == null) continue;

            // Check if the NEXT segment ends ON a runway (i.e., this segment is
            // the last one BEFORE the runway crossing).
            string crossedRwy = WhichRunwayContains(nextSeg.ToNode.Latitude, nextSeg.ToNode.Longitude);
            if (string.IsNullOrEmpty(crossedRwy)) continue;

            // Skip the destination runway (TruncateToHoldShort owns it).
            if (!string.IsNullOrEmpty(destinationName) &&
                crossedRwy.Equals(destinationName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip if we've just tagged this runway already (consecutive segments).
            if (crossedRwy.Equals(lastTaggedRunway, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip if the CURRENT segment's end is ALREADY on the same runway —
            // means we're already on it; we should have tagged the segment before.
            string currentEndOnRwy = WhichRunwayContains(seg.ToNode.Latitude, seg.ToNode.Longitude);
            if (currentEndOnRwy.Equals(crossedRwy, StringComparison.OrdinalIgnoreCase))
                continue;

            // Tag this segment as the hold-short for the upcoming runway.
            seg.IsHoldShortPoint = true;
            // Don't overwrite an existing HoldShortRunway label (a user-configured
            // taxiway hold-short or DB-derived hold-short name).
            if (string.IsNullOrEmpty(seg.HoldShortRunway))
                seg.HoldShortRunway = $"runway {crossedRwy}";
            lastTaggedRunway = crossedRwy;
        }
    }

    /// <summary>
    /// Returns the name of the runway whose pavement contains the given lat/lon
    /// (within half-width + 5 m tolerance), or "" if not on any runway. Picks
    /// the closer threshold's designator (the takeoff direction from where the
    /// aircraft is) — same convention as `TaxiGraph.DescribeLocation`.
    /// </summary>
    private string WhichRunwayContains(double lat, double lon)
    {
        if (_graph == null) return "";

        foreach (var rwy in _graph.RunwayCenterlines)
        {
            // Perpendicular distance to centerline segment.
            double perp = TaxiGraph.PerpendicularDistanceMetersStatic(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            if (perp > rwy.HalfWidthMeters + 5.0) continue;

            // Pick the closer threshold's name.
            double d1 = TaxiGraph.FastDistanceMeters(lat, lon, rwy.Lat1, rwy.Lon1);
            double d2 = TaxiGraph.FastDistanceMeters(lat, lon, rwy.Lat2, rwy.Lon2);
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

            // Find the last segment of taxiway[seqIdx]; the runway hold-short
            // we tag must come AFTER this point in the route. Search forward
            // from there for the first segment whose endpoint sits on the
            // requested runway's centerline.
            int lastSegOnTaxiway = -1;
            for (int i = 0; i < route.Segments.Count; i++)
            {
                if (route.Segments[i].TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase))
                    lastSegOnTaxiway = i;
            }
            if (lastSegOnTaxiway < 0)
            {
                unmatched.Add($"runway {runwayId} (taxiway {taxiwayName} not on route)");
                continue;
            }

            // Look for the first segment after lastSegOnTaxiway whose ToNode
            // sits on runway runwayId. Use the same WhichRunwayContains helper
            // the auto-detect path uses so the geometric tolerance matches.
            int matchSeg = -1;
            for (int i = lastSegOnTaxiway; i < route.Segments.Count; i++)
            {
                var seg = route.Segments[i];
                if (seg.ToNode == null) continue;
                string rwy = WhichRunwayContains(seg.ToNode.Latitude, seg.ToNode.Longitude);
                if (!string.IsNullOrEmpty(rwy) &&
                    rwy.Equals(runwayId, StringComparison.OrdinalIgnoreCase))
                {
                    matchSeg = i;
                    break;
                }
            }

            if (matchSeg < 0)
            {
                unmatched.Add($"runway {runwayId} (route does not cross it after taxiway {taxiwayName})");
                continue;
            }

            // Tag the segment immediately BEFORE the runway pavement (so the
            // aircraft stops at the hold-short line, not on the runway).
            int holdSegIdx = Math.Max(matchSeg - 1, lastSegOnTaxiway);
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

    public void StopGuidance()
    {
        lock (_stateLock)
        {
        _steeringTone.Stop();
        _route = null;
        _graph = null;
        _dataProvider = null;
        _currentSegmentIndex = 0;
        _positionInitialized = false;
        _headingErrorInitialized = false;
        _lastGroundSpeedKts = 0;
        _lastAnnouncedGsBucket = -1;
        _hasLineupTarget = false;
        _lineupAnnouncedAligned = false;
        // Reset all announcement latches — defense in depth; LoadRoute resets them too
        // but StopGuidance can be called independently (hotkey, takeoff-assist takeover).
        _approachAnnounced = false;
        _turnImminentAnnounced = false;
        _crossingAnnounced = false;
        _lastCrossingNodeId = -1;
        _lastAnnouncedTaxiway = "";
        _holdShortAnnounce300 = _holdShortAnnounce150 = _holdShortAnnounce50 = false;
        _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
        _lastIncursionWarnedNodeId = -1;
        // Reset cooldowns so a freshly-loaded route after Stop gets prompt warnings
        // instead of inheriting a stale cooldown from the prior session.
        _lastRecalculationTime = DateTime.MinValue;
        _lastSpeedWarningTime = DateTime.MinValue;
        _lastIncursionWarningTime = DateTime.MinValue;
        _offRouteSince = DateTime.MinValue;
        _lastSegmentAdvanceTime = DateTime.MinValue;
        _holdShortAtDestination = false;
        _recentCrossingAnnouncements.Clear();
        // Drop the cached last-instruction so a stale callout from a prior
        // route can't be replayed via Ctrl+Y after StopGuidance.
        _lastInstruction = "";
        // Rollout caches — matches the spec's edge-case row "StopGuidance
        // clears all state including the new rollout caches." Practically
        // harmless because UpdateLandingRollout cannot fire in Inactive
        // state, but keeps state-reset semantics symmetric and lets a
        // subsequent BeginLandingRollout caller assume a clean baseline
        // without depending on its own field assignments to overwrite.
        _rolloutExit = null;
        _rolloutRunway = null;
        _rolloutAllExits = new List<Navigation.LandingExit>();
        _rolloutApproach1500Announced = false;
        _rolloutApproach500Announced = false;
        _rolloutTurnNowAnnounced = false;
        _rolloutNoExitMode = false;
        _rolloutEnd1500Announced = false;
        _rolloutEnd500Announced = false;
        _rolloutEnd100Announced = false;
        SetState(TaxiGuidanceState.Inactive);
        } // end lock(_stateLock)
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
            // "done, hold position" rather than "lost / broken".
            if (_lineupAnnouncedAligned)
                return $"Lined up {_destinationName}, heading {hdg}. Aligned — hold position.";

            // Not yet aligned: surface distance-to-go so a silent tone
            // (between hysteresis pulses, or while the system is busy) still
            // has a progress readout the pilot can query on demand. No
            // cross-track feet — the tone is the cross-track instrument
            // (see the blind-pilot-cue rule); distance-remaining + heading
            // are the numbers a pilot can actually use.
            string lineupDistStr = "";
            if (_hasLineupTarget && _positionInitialized)
            {
                double dM = TaxiGraph.FastDistanceMeters(
                    _lastLat, _lastLon, _lineupTargetLat, _lineupTargetLon);
                lineupDistStr = $" {FormatDistance(dM)} to go.";
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

    /// <summary>
    /// Formats a distance in meters to feet or nautical miles.
    /// </summary>
    private static string FormatDistance(double meters)
    {
        double feet = meters * METERS_TO_FEET;
        if (feet > 6000)
        {
            double nm = meters * METERS_TO_NM;
            return $"{nm:F1} nautical miles";
        }
        return $"{(int)feet} feet";
    }

    private string BuildRouteSummary(TaxiRoute route, bool isRunwayDestination)
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

        // Count explicit hold-shorts the pilot asked for (or real intermediate
        // runway crossings). For runway destinations, TruncateToHoldShort tags
        // the last segment as a hold-short purely as an internal safety rail so
        // the 300/150/50 ft countdown fires — it is NOT an ATC-assigned hold
        // point and must not be counted. Including it produced confusing
        // summaries like "…, 1 hold short point" when ATC never issued one.
        int holdShorts = route.Segments.Count(s => s.IsHoldShortPoint);
        if (isRunwayDestination && route.Segments.Count > 0 &&
            route.Segments[^1].IsHoldShortPoint)
        {
            holdShorts--;
        }
        string holdStr = holdShorts > 0 ? $", {holdShorts} hold short point{(holdShorts > 1 ? "s" : "")}" : "";

        string fallbackStr = "";
        if (!string.IsNullOrEmpty(route.ConstrainedFallbackReason))
        {
            fallbackStr = $" Warning: could not follow specified taxiways. Using shortest path. Reason: {route.ConstrainedFallbackReason}";
        }

        return $"Route to {route.DestinationName}{taxiwayStr}. {distStr}{holdStr}.{fallbackStr}";
    }

    private void SetState(TaxiGuidanceState newState)
    {
        if (_state == newState) return;
        var prev = _state;
        _state = newState;
        // DIAGNOSTIC: state transitions during landing-exit / rollout flow.
        RolloutDiag($"SetState: {prev} -> {newState}");

        // Lineup must never end in silence. The lineup tone is the pilot's only
        // alignment instrument; if a route reload / recalc (LiningUp ->
        // RouteLoaded) or any non-aligned exit tears it down, the tone just
        // stops with no explanation — the pilot can't tell "aligned, hold" from
        // "system gave up", and then hears a "crossing <other runway>" callout
        // from the freshly-loaded route that makes no sense ("I was lining up
        // 31L, why 22R?"). Announce the interruption explicitly. The aligned
        // case does NOT come through here (it keeps state in LiningUp and
        // pauses the tone), so this only fires on a genuine interruption.
        // The guard catches ANY non-aligned exit from LiningUp (route reload is
        // the only one today, but keep the message generic so a future
        // transition can't make it lie).
        if (prev == TaxiGuidanceState.LiningUp &&
            newState != TaxiGuidanceState.Arrived &&
            newState != TaxiGuidanceState.Inactive)
        {
            _announcer.AnnounceImmediate(
                "Lineup interrupted. No longer aligning with the runway.");
        }

        StateChanged?.Invoke(this, newState);
    }

    // --- DIAGNOSTIC LOGGING HELPER (debug/landing-rollout-instrumentation branch) ---
    // Writes to landing_exit.log alongside LandingExitPlanner.DiagLog so the
    // rollout-phase per-frame loop is interleaved with the planner's touchdown
    // events. Cheap (one File.AppendAllText per ~few seconds during rollout);
    // entirely removed once we've identified the root cause of the RJAA bug.
    private static readonly string _rolloutDiagPath = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "MSFSBlindAssist", "landing_exit.log");

    private static void RolloutDiag(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(_rolloutDiagPath,
                $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TGM] {msg}{System.Environment.NewLine}");
        }
        catch { /* never fail on diag */ }
    }

    private static string CapFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s[1..];
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    /// <summary>
    /// Computes the spoken turn direction ("left" / "slight right" / "continue")
    /// FROM THE AIRCRAFT'S CURRENT HEADING toward a target bearing. Used for
    /// turn callouts so the verbal direction always agrees with the steering
    /// tone (which is also driven by the aircraft's instantaneous heading).
    ///
    /// Why not just use the route's pre-computed `TaxiRouteSegment.TurnDirection`?
    /// That value is `nextSeg.bearing - currentSeg.bearing` — it assumes the
    /// aircraft is exactly on-axis with the current segment. When the aircraft
    /// is off-axis (e.g., still at the gate before pushback rotation, after a
    /// wide turn, after a brief deviation), the actual turn the aircraft must
    /// make to align with the next segment differs from the route's intent —
    /// sometimes in the OPPOSITE direction. The steering tone (real-time pan
    /// from aircraft-heading vs target-bearing) is always right; the static
    /// pre-computed verbal was wrong in those cases. Following the tone is
    /// the correct behavior; this helper makes the verbal match.
    ///
    /// Thresholds match `TaxiRouter.GetTurnDirection`: <20° → continue,
    /// 20–60° → slight, ≥60° → full. The "sharp" prefix is owned by callers
    /// that read `TurnAngleDegrees` separately.
    /// </summary>
    private static string ComputeTurnVerbalFromHeading(double targetBearing, double aircraftHeadingTrue)
    {
        double turn = NormalizeAngle(targetBearing - aircraftHeadingTrue);
        double absTurn = Math.Abs(turn);
        if (absTurn < 20) return "continue";
        if (absTurn < 60) return turn < 0 ? "slight left" : "slight right";
        return turn < 0 ? "left" : "right";
    }

    /// <summary>
    /// Returns the signed along-runway distance in meters from (refLat, refLon)
    /// to (pointLat, pointLon), measured along the runway heading. Positive
    /// values mean `point` lies past `ref` in the direction of flight; negative
    /// values mean `point` is still upfield of `ref`. Equirectangular projection
    /// — sub-cm accuracy at runway scale.
    ///
    /// Runway heading is measured clockwise from true north, so the unit vector
    /// along the runway in (east, north) coordinates is (sin H, cos H). The
    /// signed projection is the dot product of (dE, dN) with that unit vector.
    /// </summary>
    private static double SignedAlongRunwayMeters(
        double pointLat, double pointLon,
        double refLat, double refLon,
        double runwayHeadingTrueDeg)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (pointLat + refLat) * 0.5 * Math.PI / 180.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);
        double dN = (pointLat - refLat) * METERS_PER_DEG_LAT;
        double dE = (pointLon - refLon) * metersPerDegLon;
        double hdgRad = runwayHeadingTrueDeg * Math.PI / 180.0;
        return dE * Math.Sin(hdgRad) + dN * Math.Cos(hdgRad);
    }

    /// <summary>
    /// Perpendicular (cross-track) distance in meters from (plat, plon) to the segment
    /// (a→b), clamped to endpoints so points beyond either end use the nearest endpoint.
    /// Uses equirectangular projection — sub-cm accuracy at taxi scale.
    /// </summary>
    private static double PerpendicularDistanceToSegmentMeters(
        double plat, double plon,
        double alat, double alon,
        double blat, double blon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (alat + blat) * 0.5 * (Math.PI / 180.0);
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);

        double bx = (blon - alon) * metersPerDegLon;
        double by = (blat - alat) * METERS_PER_DEG_LAT;
        double px = (plon - alon) * metersPerDegLon;
        double py = (plat - alat) * METERS_PER_DEG_LAT;

        double lenSq = bx * bx + by * by;
        if (lenSq < 1e-9)
            return Math.Sqrt(px * px + py * py);

        double t = (px * bx + py * by) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        double fx = t * bx;
        double fy = t * by;
        double ex = px - fx;
        double ey = py - fy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>
    /// Diagnostic frame logger for steering-tone troubleshooting. Captures the
    /// raw and smoothed heading-error inputs plus the geometric inputs that
    /// produce them, rate-limited to roughly 10 Hz. Silent on I/O errors —
    /// must never affect guidance behaviour. The companion `taxi_router.log`
    /// captures route construction; this file captures the per-frame tone-driving
    /// data needed to diagnose erratic L/R flipping.
    /// </summary>
    private void LogGuidanceFrame(
        double lat, double lon, double headingTrue, double groundSpeedKts,
        int segIdx, double segBearing, double pathWidthFeet,
        bool nextIsTurn, double targetLat, double targetLon,
        double rawHeadingError, double smoothedHeadingError)
    {
        // UTC for rate-limit math (monotonic, immune to DST / clock-change
        // discontinuities); DateTime.Now is used below for the human-readable
        // printed timestamp because users reading the file expect local time.
        var now = DateTime.UtcNow;
        if ((now - _lastGuidanceLogTime).TotalMilliseconds < GUIDANCE_LOG_INTERVAL_MS) return;
        _lastGuidanceLogTime = now;
        try
        {
            string line = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff},lat={1:F7},lon={2:F7},hdg={3:F1},gs={4:F1},seg={5},segBrg={6:F1},w={7:F0},nxtTurn={8},tLat={9:F7},tLon={10:F7},raw={11:F2},smooth={12:F2}",
                DateTime.Now, lat, lon, headingTrue, groundSpeedKts,
                segIdx, segBearing, pathWidthFeet,
                nextIsTurn ? 1 : 0, targetLat, targetLon,
                rawHeadingError, smoothedHeadingError);
            File.AppendAllText(GuidanceLogPath, line + Environment.NewLine);
        }
        catch { /* diagnostic only — never crash guidance for a log failure */ }
    }

    public void Dispose()
    {
        StopGuidance();
        _steeringTone.Dispose();
        GC.SuppressFinalize(this);
    }
}

