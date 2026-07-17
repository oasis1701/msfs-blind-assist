using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

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
    /// A Progressive Taxi leg has reached its terminator (hold short of a
    /// runway/taxiway, just past a cleared crossing, or end of taxiway). The
    /// tone is off and the aircraft holds; the pilot sets the next leg. No
    /// lineup, no Takeoff-Assist, no docking.
    /// </summary>
    ProgressiveHold,
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
    LandingRollout,
    /// <summary>
    /// The pilot reached the runway end without exiting and is backtaxiing
    /// toward the apron. Steering tone guides on the reciprocal runway heading
    /// (silent while heading is &gt;90° from backtrack target — turn direction is
    /// ambiguous for a 180° rotation). Transitions to Taxiing once the aircraft
    /// is within BACKTRACK_HANDOFF_M of the first taxi-graph connection node,
    /// or if no connection node was found nearby.
    /// </summary>
    BacktrackingOnRunway
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
public partial class TaxiGuidanceManager : IDisposable
{
    private readonly ScreenReaderAnnouncer _announcer;
    private TaxiSteeringTone _steeringTone;
    private TaxiGraph? _graph;
    private TaxiRoute? _route;
    private TaxiGuidanceState _state = TaxiGuidanceState.Inactive;

    private bool _steeringToneSuppressed;
    /// <summary>When true, the taxi steering tone is silenced (e.g. while docking guidance owns the centerline cue near the gate).</summary>
    public void SetSteeringToneSuppressed(bool suppressed)
    {
        if (_steeringToneSuppressed == suppressed) return;
        // Latch only AFTER Pause/Resume succeeds. If the call threw with the latch
        // already flipped, the equality guard above would block every retry with the
        // same value and the tone would stay stuck in the wrong state. The caller
        // feeds this per-frame, so an unchanged latch retries on the next frame.
        try { if (suppressed) _steeringTone.Pause(); else _steeringTone.Resume(); }
        catch { return; }
        _steeringToneSuppressed = suppressed;
    }

    private bool _dockingActive;
    /// <summary>
    /// True while docking guidance is ENGAGED on the destination gate (its state machine is in
    /// Docking or Stopped) — fed per-frame from <c>DockingGuidanceManager.IsActive</c>. While
    /// true, taxi suppresses its OWN terminal arrival callouts (the parking countdown, "Stop.
    /// Hold position.", the "Align with…"/"Destination reached" verbal, and the gate-lineup
    /// "Parking brake.") so it never contradicts docking's countdown to the precise GSX stop —
    /// which sits a few metres beyond taxi's route-end node. ENGAGE-LATCHED by design: before
    /// docking engages, taxi speaks its normal arrival sequence — so a gate where docking never
    /// engages (approach outside the cone, stop beyond engage range, navdata heading off) still
    /// gets full verbal arrival guidance instead of total silence. Do NOT widen this back to
    /// "a gate is set" semantics — that silenced every navdata user's gate arrival.
    /// Written and read on the same marshalled UI-thread frame (write follows taxi's own
    /// UpdatePosition in the MainForm handler), so a plain bool is safe; worst case one frame stale.
    /// </summary>
    public void SetDockingActive(bool active) { _dockingActive = active; }

    /// <summary>
    /// True while docking guidance is ARMED for this gate with the GSX stop position
    /// still AHEAD of the aircraft, but not yet engaged (see
    /// DockingGuidanceManager.IsArmedAwaitingEngage). In that window taxi's arrival
    /// wording must redirect the pilot FORWARD — "continue ahead for docking
    /// guidance" — instead of "Parking brake." / "hold position": the GSX stop is
    /// routinely tens of metres past the navdata parking point where taxi's route
    /// ends, and the gate's GSX gatedistancethreshold can shrink docking's engage
    /// range below that gap (KATL F3 2026-06-11: pilot parked-on-instruction at
    /// 33.7 m and sat 26 s with docking Armed). Same write/read pattern as
    /// SetDockingActive (one frame stale at worst).
    /// </summary>
    public void SetDockingPending(bool pending) { _dockingPending = pending; }
    private bool _dockingPending;

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
    // Taxi planner "CAT III / low-visibility hold (LVP)" preference for the CURRENT
    // route. Persisted across a mid-taxi recalc so the recalculated route keeps the
    // same hold choice. Default false = hold at the full-length line (user decision
    // 2026-07); see TruncateToHoldShort.
    private bool _preferIlsHold = false;
    // Which hold line TruncateToHoldShort actually picked for the CURRENT route
    // (via RunwayHoldShortSelector). LoadRoute turns this into the LVP
    // route-summary feedback sentence — with LVP requested, the same-approach
    // gate can legitimately reject the ILS hold, and a blind pilot has no other
    // way to know which line the route stops at.
    private RunwayHoldChoice _lastRunwayHoldChoice = RunwayHoldChoice.None;

    // Route polyline cache for GuidanceGeometry (node k = segment k's FromNode,
    // last entry = final ToNode). Rebuilt lazily whenever _route changes —
    // keyed by reference so every assignment site (LoadRoute, recalc,
    // retarget) is covered without per-site invalidation duties.
    private TaxiRoute? _routePointsSource;
    private double[] _routeLats = Array.Empty<double>();
    private double[] _routeLons = Array.Empty<double>();

    private (double[] lats, double[] lons) RoutePoints()
    {
        var route = _route!;
        if (!ReferenceEquals(_routePointsSource, route))
        {
            int n = route.Segments.Count;
            var lats = new double[n + 1];
            var lons = new double[n + 1];
            for (int i = 0; i < n; i++)
            {
                lats[i] = route.Segments[i].FromNode.Latitude;
                lons[i] = route.Segments[i].FromNode.Longitude;
            }
            lats[n] = route.Segments[n - 1].ToNode.Latitude;
            lons[n] = route.Segments[n - 1].ToNode.Longitude;
            _routeLats = lats; _routeLons = lons;
            _routePointsSource = route;
        }
        return (_routeLats, _routeLons);
    }

    // Current tracking state
    private int _currentSegmentIndex = 0;
    // True when the active route came from the Landing Exit Planner (it
    // terminates at a runway exit, not a gate) — drives the post-exit arrival
    // announcement in HandleArrival.
    private bool _isLandingExitRoute = false;
    private DateTime _lastRecalculationTime = DateTime.MinValue;
    private string _lastAnnouncedTaxiway = "";
    private bool _approachAnnounced = false;      // "In X, turn..." advance notice (~300 ft lead, spoken in the active unit)
    private int _curveAnnouncedSign = 0;   // -1 announced left, +1 right, 0 armed
    private bool _turnImminentAnnounced = false;   // "Turn now" at ~100ft
    // Yaw-episode tracker driving the "Straighten." rollout cue. The original
    // design armed off the route's per-junction TurnAngleDegrees — DEAD ON REAL
    // NAVDATA: a measured KSFO route had 107 junctions and only ONE ≥ 60° (real
    // 90° turns are chains of 5–15° micro-bends), so the cue never fired
    // (pilot report 2026-06-11). Episodes are route-classification-independent:
    // an episode opens when sustained |yaw| ≥ STRAIGHTEN_EPISODE_MIN_RATE,
    // accumulates signed heading change, and closes when yaw settles. The cue
    // fires once per episode when (a) the episode has turned ≥
    // STRAIGHTEN_MIN_TURN_DEG, (b) the route ahead is straight-ish (so a
    // mid-curve steady arc — where the projected error legitimately hovers
    // near zero — can't false-fire), and (c) the projected error crosses
    // through centre against the yaw direction.
    private int _yawEpisodeSign;            // 0 = no episode in progress
    private double _yawEpisodeTurnDeg;      // signed accumulated heading change
    private bool _straightenFiredThisEpisode;
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
    private const double TURN_IMMINENT_MIN_M = 20.0;             // floor: ~65ft (slow taxi, stopped)
    private const double TURN_IMMINENT_MAX_M = 75.0;             // ceiling: ~245ft (fast jet taxi)
    private const double TURN_IMMINENT_SEC_LEAD = 4.0;           // desired lead time at current ground speed
    private const double CROSSING_ANNOUNCE_DISTANCE_M = 50.0;    // ~150ft "crossing taxiway X"
    // Cumulative-curve verbal cue. Navdata models gentle curves as chains of
    // 5–15 m segments bending 5–15° each — every step is below the 20°
    // discrete-turn threshold, so no turn callout ever fired for a 30–90°
    // cumulative curve (KATL 2026-06-10: a 48° curve produced a silent −72°
    // tone error at 20 kt). Announce once per curve direction when the next
    // CURVE_SCAN_WINDOW_M of route bends ≥ CURVE_ANNOUNCE_MIN_DEG without any
    // single discrete step (discrete turns keep their own callouts).
    private const double CURVE_SCAN_WINDOW_M = 100.0;
    private const double CURVE_ANNOUNCE_MIN_DEG = 30.0;
    private const double CURVE_RESET_DEG = 15.0;   // hysteresis: re-arm when the window straightens
    private const double CURVE_ANNOUNCE_MAX_OPPOSITE_YAW_DEG_SEC = 3.0;  // defer cue while yawing against it
    // Runway-destination arrival radius (12 m ≈ 40 ft). Tight enough that the
    // 300/150/50 ft countdown fires in full BEFORE HandleArrival takes over
    // (previously 30 m preempted the 50 ft "Stop." callout). The pilot therefore
    // hears: "…150 ft slow down" → "…50 ft stop" → "Stop. Hold short of runway X.
    // Press continue when cleared." in that order, ending at ~40 ft from the
    // hold-short node — enough braking room even at brisk taxi speed.
    private const double ARRIVAL_RADIUS_M = 12.0;
    // Landing-exit ("vacate onto the taxiway") arrival capture. A landing-exit route
    // ends at the exit taxiway's extension node — the hold-clear point past the
    // runway. Unlike a gate, the pilot rolls THROUGH this node at taxi speed
    // (15-25 kt on a high-speed exit) and typically passes 15-25 m to the SIDE of the
    // exact graph node, so the ~6 m gate radius never fires: the "Off the runway.
    // Hold position." closure is silently lost, the tone goes quiet (the pilot is
    // aligned), and they sail on up the taxiway (LPPT 02 → U5, 2026-06-12, closest
    // approach to the node was 15.4 m). These looser captures fire arrival for EVERY
    // exit type — a 25 m radius for the normal rolling pass, plus an along-track
    // backstop for a wide pass (drew abeam/past the node but >25 m to the side).
    private const double LANDING_EXIT_ARRIVAL_RADIUS_M = 25.0;
    private const double LANDING_EXIT_ARRIVAL_CROSS_M = 60.0;

    // Plain taxiway-endpoint arrival backstop (progressive-taxi "end of taxiway X").
    // These destinations carry the tight ~6 m gate radius but have NO lineup target,
    // parking countdown, or docking handoff, so a pilot rolling THROUGH the endpoint
    // at taxi speed can miss the 6 m circle entirely — the look-ahead tone then keeps
    // pointing at the now-behind node and the pilot circles it (OMDB end-of-K10
    // 2026-06-14: passed within ~8 m at 17 kt, the 6 m arrival never fired, tone wound
    // hard right onto the passed node). Mirror the landing-exit along-track backstop:
    // once the aircraft is past the final node (alongRemaining <= 0) and within this
    // cross tolerance, declare arrival. Scoped (in the arrival check) to non-gate /
    // non-runway endpoints so gate parking countdown and runway lineup are untouched.
    private const double ENDPOINT_ARRIVAL_BACKSTOP_CROSS_M = 25.0;

    // How far past the runway half-width the aircraft datum must be before a
    // landing-exit arrival ("Off the runway. Stop and hold position.") is allowed
    // to fire. Without this, an early/aggressive exit turn + re-route can curve the
    // route back toward the runway and the 25 m arrival fires while the aircraft is
    // still WITHIN the runway half-width — telling a blind pilot to STOP on an active
    // runway (OMDB 30L → K10 2026-06-14: "arrived" at ~20 m off a 30 m-half-width
    // runway; Where-Am-I correctly reported the aircraft was still on 12R/30L). While
    // still on the runway we suppress the arrival and keep guiding toward the
    // off-runway extension node, so the "stop" is spoken only once genuinely clear.
    private const double RUNWAY_CLEAR_MARGIN_M = 10.0;
    private const double RECALCULATION_COOLDOWN_SEC = 15.0;
    // Steering-tone look-ahead: target = the point this many metres ahead
    // along the route polyline (continuous walk — see GuidanceGeometry).
    // Speed-scaled so the tone leads turns earlier at speed: 6 s of travel,
    // clamped. At 10 kt → 50 m (floor); 20 kt → 62 m; 30 kt+ → 93–120 m.
    // The 50 m floor matches the old fixed GUIDANCE_LOOK_AHEAD_M so slow-taxi
    // cross-track sensitivity is unchanged.
    private const double GUIDANCE_LOOK_AHEAD_SEC = 6.0;
    private const double GUIDANCE_LOOK_AHEAD_MIN_M = 50.0;
    private const double GUIDANCE_LOOK_AHEAD_MAX_M = 120.0;
    // Rollout anticipation: project the tone's heading error forward by the
    // yaw rate over TurnLeadSeconds so the tone centres BEFORE the nose
    // arrives — the pilot starts unwinding on time instead of overshooting
    // the turn (reaction time + airframe yaw inertia). Lead is PER-AIRCRAFT
    // (IAircraftDefinition.TaxiTurnLeadSeconds, wired by MainForm on aircraft
    // load/switch): FBW Airbuses need ~1.3–1.6 s (1.3 measured from 13 turn
    // rollouts in the 2026-06-10 A20N telemetry), PMDG Boeings ~0.8–1.0 s.
    // 0 disables the projection entirely. Contribution clamped; rate
    // low-passed; only applied while actually yawing.
    // Cross-thread note: written by the UI thread on aircraft load/switch, read per-frame on the SimConnect thread WITHOUT _stateLock — benign (single aligned double, no coupled invariant). Do not couple a second field to it without taking the lock.
    public double TurnLeadSeconds { get; set; } = 1.2;
    private const double TURN_LEAD_MAX_DEG = 30.0;
    private const double TURN_LEAD_MIN_RATE_DEG_SEC = 1.0;
    private const double YAW_RATE_FILTER_ALPHA = 0.3;
    // Taxiing-tone slew-rate limit (degrees of heading-error change per second).
    // The taxiing pan is driven by the heading error to a look-ahead walk target.
    // Two events can move that target DISCONTINUOUSLY — a one-frame jump the
    // low-pass filter barely dents:
    //   1. Segment-skip: AdvanceToNearestSegment picks the nearest *endpoint*
    //      over a 6-segment window, so a large aircraft (A380: ~50 m+ turn
    //      radius) that swings wide through a sharp corner can leap the segment
    //      index several steps in one frame, jumping the target tens of metres
    //      (PHNL 04L 2026-06-13: seg 8→11, target jumped ~65 m, pan slammed
    //      +44°→−80°, the aircraft over-rotated, off-route recalc followed).
    //   2. Recalc: TryRecalculateRoute swaps the route in place and resets the
    //      heading-error smoother, so the pan snaps to the new target instantly.
    // A real turn changes the error gradually at roughly the yaw rate (the A380
    // taxis at ~8°/s, well under 20°/s even with route curvature), so a 60°/s
    // cap passes genuine turns untouched while stretching a 60–110° discontinuity
    // into a smooth ~1–1.5 s sweep — a clear "ease across" cue instead of a slam.
    // Applied AFTER the rate-lead projection so it also bounds lead-driven swings.
    // Taxiing phase ONLY (lineup/docking keep their own precision profiles).
    private const double TAXI_TONE_MAX_SLEW_DEG_PER_SEC = 60.0;
    private double _lastToneError;
    private DateTime _lastToneErrorTime = DateTime.MinValue;
    private bool _toneErrorInitialized = false;
    // Initial big-turn cue. When guidance starts with the aircraft pointing well
    // away from the route's first segment — the normal post-pushback case, where
    // the route leaves the gate one way but the tug left the aircraft facing
    // another — a tone alone starts at full pan and can't convey "turn around
    // which way", which is disorienting (PHNL 2026-06-13: pilot faced 269° while
    // the route headed 91°, a ~180° turnaround, and felt the initial cue was
    // "really off"). On the FIRST taxiing frame, if the heading error exceeds
    // this threshold, speak a one-shot turn-direction cue (sign matches the tone)
    // so the pilot knows which way to come around. One-shot, reset on LoadRoute /
    // StopGuidance (NOT on recalc — mid-taxi recalcs use the normal turn cues).
    private const double INITIAL_TURN_CUE_DEG = 100.0;
    // Above this heading error the initial cue is phrased as a U-turn / "behind
    // you" rather than a "sharp turn" — the boundary between "come around" and
    // "turn hard onto the first taxiway".
    private const double INITIAL_TURN_UTURN_DEG = 135.0;
    private bool _initialTurnCueAnnounced = false;
    // After a route-reach warning, briefly hold the INFORMATIONAL taxiway-crossing
    // and taxiway-change callouts so they don't stomp that (longer, safety-
    // critical) warning at guidance start — 2026-06-13: "Crossing taxiway G" cut
    // the warning off mid-sentence. Hold-shorts, runway-crossing callouts, and the
    // lineup bailout are NOT gated by this.
    private const double REACH_WARNING_CHATTER_GRACE_SEC = 8.0;
    private DateTime _startChatterSuppressUntil = DateTime.MinValue;
    // "Straighten." yaw-episode thresholds (see the _yawEpisodeSign field comment).
    private const double STRAIGHTEN_EPISODE_MIN_RATE_DEG_SEC = 4.0;  // open episode / cue may fire
    private const double STRAIGHTEN_EPISODE_END_RATE_DEG_SEC = 1.5;  // close episode (hysteresis)
    private const double STRAIGHTEN_MIN_TURN_DEG = 35.0;             // episode must have turned this much
    private const double STRAIGHTEN_AHEAD_WINDOW_M = 60.0;           // route-ahead straightness scan
    private const double STRAIGHTEN_AHEAD_STRAIGHT_DEG = 15.0;       // ...must bend less than this
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
    // Minimum negative along-track (behind the segment's start node) before the
    // "going backwards" off-route condition fires. GPS at taxi scale has <1 m
    // noise; 10 m gives a comfortable margin while catching deliberate rearward
    // movement. Requires the same GS + persistence gates as the lateral check.
    // Blind spot this closes: heading almost opposite to segment bearing keeps
    // cross-track near zero (PerpendicularDistance returns ~0), so the lateral
    // check never fires even as the pilot drives steadily away from the route.
    private const double OFF_ROUTE_BEHIND_START_M = 10.0;
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

    // Initial-load constrained-route advisory. The recalc path has a sanity
    // gate (RECALC_LENGTH_BLOWUP_*) but the INITIAL LoadRoute had none — a
    // mis-picked taxiway ("via FE" at KIAH when the gate was 600 m away)
    // produced a 6,094 m tour with out-and-back loops and zero warning.
    // Same thresholds as the recalc gate; advisory only (the clearance might
    // be genuine — ATC can route around closures), so the route still loads
    // and the pilot decides.
    private const double CONSTRAINED_WARN_RATIO = 2.0;
    private const double CONSTRAINED_WARN_PAD_M = 500.0;
    private const double CONSTRAINED_WARN_FIRST_TW_M = 300.0;

    // Position tracking
    private double _lastLat, _lastLon, _lastHeading;
    private double _yawRateDegSec;            // low-passed, right-positive
    private DateTime _lastYawSampleTime = DateTime.MinValue;
    private double _lastYawHeading;
    private double _lastGroundSpeedKts;
    private bool _positionInitialized = false;

    // Off-route persistence tracker — off-route must be sustained for
    // OFF_ROUTE_PERSISTENCE_SEC before a recalc fires. MinValue = not off-route.
    private DateTime _offRouteSince = DateTime.MinValue;
    // Route-joined latch. Off-route detection (and thus auto-recalc) is suppressed
    // until the aircraft has actually reached the route line at least once. The
    // post-pushback taxi from the gate ONTO the first taxiway is legitimately off
    // the route's first segment (the route starts on the taxiway, often 100 m+
    // from the gate), and recalcing during that join was silently trimming the
    // entered clearance before the pilot started following it (PHNL 2026-06-13:
    // 4–5 recalcs at 3–6 kt while still on segment 0, cutting Z A L N Z D → Z D).
    // Set true the first frame the aircraft is within perp tolerance of the route;
    // reset on LoadRoute / StopGuidance.
    private bool _hasJoinedRoute = false;
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
    // analysed post-hoc. Re-headered on every LoadRoute. Rate-limited to ~10 Hz to keep
    // the file under ~100 KB for a typical 5-10 min taxi.
    // Always on while Taxiing — cheap (one enqueue per ~100 ms; the shared LogWriter
    // background thread does the actual disk I/O and handles size-capped rotation).
    private static readonly LogChannel _guidanceLog = Log.Channel("taxi_guidance");
    private DateTime _lastGuidanceLogTime = DateTime.MinValue;
    private const double GUIDANCE_LOG_INTERVAL_MS = 100.0;

    // Last actionable instruction announced (for the Ctrl+Y "Repeat" hotkey).
    // Only TACTICAL announcements update this — turn callouts, hold-shorts,
    // taxiway changes, lineup, arrival, distance countdowns. Two peripheral
    // sites still call _announcer.Announce directly (without recording to
    // _lastInstruction) so the Repeat-Last buffer keeps the actionable callout:
    // the LoadRoute route summary at start of guidance. Cleared on StopGuidance.
    // (The periodic ground-speed announcer that also used to feed this buffer
    // moved to the global Services/GroundSpeedAnnouncer.)
    private string _lastInstruction = "";

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
    private bool _holdShortOuterAnnounced = false;
    private bool _holdShortSlowDownAnnounced = false;
    private bool _holdShortStopAnnounced = false;

    // Parking countdown announcements
    private bool _parkingAnnounce50 = false;
    private bool _parkingAnnounce20 = false;
    private bool _parkingAnnounce10 = false;

    // Runway incursion detection (off-route hold-short proximity)
    private int _lastIncursionWarnedNodeId = -1;
    private DateTime _lastIncursionWarningTime = DateTime.MinValue;
    private const double INCURSION_WARN_DISTANCE_M = 40.0;
    private const double INCURSION_WARNING_COOLDOWN_SEC = 10.0;

    // CheckRunwayIncursion runs at ~30 Hz (Taxiing AND Arrived states) inside
    // _stateLock, so both of its per-frame allocations are cached here rather
    // than rebuilt every frame:
    //
    // (a) Per-graph HS/ILS-HS node list. TaxiGraph is immutable once Build()
    // returns — ResolveNode/UpgradeNodeType (the only writers of Nodes/Node.Type)
    // are private and called only from within the static Build() method; no
    // public TaxiGraph method mutates Nodes afterward (verified by inspection,
    // 2026-07). So it's safe to rebuild only when the _graph REFERENCE changes
    // (every _graph assignment site — LoadRoute's two branches, StopGuidance —
    // installs a brand-new TaxiGraph or null, never mutates the existing one).
    private TaxiGraph? _holdShortNodesGraph;
    private List<TaxiNode>? _cachedHoldShortNodes;

    // (b) Reference-keyed on-route HS node-ID set, mirroring the RoutePoints()
    // idiom above: keyed on (_route reference, _currentSegmentIndex) since the
    // set depends on the remaining segments from _currentSegmentIndex onward.
    // _route.Segments is never mutated in place after being assigned to _route
    // (every route mutation — TruncateToHoldShort, ApplyUserRunwayHoldShorts —
    // runs on the local route/newRoute BEFORE it's assigned to the field), so a
    // reference-equality check on _route is sufficient, same as RoutePoints().
    private TaxiRoute? _onRouteHsSourceRoute;
    private int _onRouteHsSourceSegmentIndex = -1;
    private HashSet<int> _cachedOnRouteHsNodes = new();

    // Lineup state (active during LiningUp phase — for runways AND gates)
    private double _lineupTargetLat, _lineupTargetLon;
    private double _lineupHeadingTrue, _lineupHeadingMag;
    private bool _lineupAnnouncedAligned = false;
    /// <summary>
    /// One-shot latch for auto-activate. Set to true when
    /// RequestTakeoffAssistAutoActivate is raised; reset to false on every
    /// LoadRoute and StopGuidance. Prevents re-fire on drift-and-re-align,
    /// which is intentional — if the pilot manually deactivates Takeoff
    /// Assist after auto-activation, they should not be surprised by it
    /// coming back on after a small drift.
    /// </summary>
    private bool _autoActivateFired = false;
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
    // Non-null while a Progressive Taxi leg is active. Drives the terminal
    // "progressive hold" end-state + announcement and suppresses the auto
    // hold-short on a cleared crossing. Cleared on every LoadRoute (set fresh
    // below) and StopGuidance.
    private Navigation.ProgressiveTerminator? _progressiveTerminator;

    // Unreachable-runway safety net (PHNL 04L 2026-06-13). When the entered
    // taxiway clearance ends on a taxiway that merely PARALLELS the runway —
    // with no connector taxiway to the runway itself — the route's DESTINATION
    // node (the node nearest the lineup point that the route reaches) sits tens
    // to hundreds of metres off to the side, then guidance silently holds short
    // there and tries to "line up" on a runway it has no path to. The intercept
    // controller saturates and the cross-track never closes, so the lineup tone
    // pans forever and the pilot has no idea why. These two thresholds drive
    // (a) an up-front route-load warning and (b) a during-lineup spoken bailout
    // so the failure becomes an actionable message instead of an endless tone.
    //
    // REACHABILITY IS MEASURED AT THE DESTINATION NODE, NOT THE HOLD-SHORT.
    // The route is truncated to a hold-short (TruncateToHoldShort) BEFORE these
    // checks, and a hold-short does NOT necessarily sit on the centerline
    // extended: an ILS hold-short (IHSND) or any hold on a connector that meets
    // the runway at an angle legitimately sits far off the perpendicular
    // (LPPT 02 2026-06-16: the rwy-02 ILS hold is 151 m / ~500 ft off the
    // centerline, yet the runway IS reachable — the route's destination node is
    // 6.8 m off and the lineup intercept bridges the gap). Measuring the
    // truncated hold-short therefore false-fired "does not reach the runway" on
    // a perfectly reachable runway. The destination node (_destinationNodeId =
    // FindNearestNode of the lineup point) is the correct reachability probe:
    // near the runway when reachable, far off only when the clearance ended on a
    // parallel taxiway with no node near the lineup point (the real PHNL case).
    // RUNWAY_REACH_MAX_CROSS_M is the perpendicular distance from that
    // destination node to the runway centerline beyond which the route clearly
    // does not reach the runway. 120 m sits safely above any LEGITIMATE runway-
    // entrance node offset, yet well below the ~456 m the PHNL 04L failure
    // produced.
    private const double RUNWAY_REACH_MAX_CROSS_M = 120.0;
    // During LiningUp, cross-track this far off the centerline (≈122 m, again
    // above the ~90 m legitimate-hold-short ceiling) sustained for
    // LINEUP_UNREACHABLE_SEC without converging means the route never reached
    // the runway — fire the one-shot spoken bailout.
    private const double LINEUP_UNREACHABLE_CROSS_FEET = 400.0;
    private const double LINEUP_UNREACHABLE_SEC = 12.0;
    private DateTime _lineupHugeCrossTrackSince = DateTime.MinValue;
    private bool _runwayLineupUnreachableWarned = false;
    // True when the current route's destination node is close enough to the
    // runway centerline that the lineup intercept can bridge the remaining gap.
    // Set by LoadRoute / TryRecalculateRoute from the DESTINATION node (not the
    // truncated hold-short — see RUNWAY_REACH_MAX_CROSS_M comment). Gates BOTH
    // the up-front reach warning and the during-lineup unreachable bailout, so a
    // legitimate far-back ILS hold-short never triggers either. Defaults true so
    // non-runway destinations (and any unset state) never arm the safety net.
    private bool _routeReachesRunway = true;

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
    private bool _rolloutApproach900Announced = false;
    private bool _rolloutApproach500Announced = false;
    private bool _rolloutTurnNowAnnounced = false;
    // Latches true when distToExitFeet first drops below ROLLOUT_EXIT_TONE_ARM_FT.
    // Used to reset the heading-error smoother exactly once at that point so the
    // exit-bearing tone fires immediately rather than lagging the transition.
    private bool _rolloutExitToneArmed = false;
    // Latches true after the one-shot TryEarlyExitHandoff attempt so we don't
    // retry on every subsequent frame. The attempt happens once: at the first
    // frame where GS ≤ ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS and dist ≤ ROLLOUT_EXIT_TONE_ARM_FT.
    // If it succeeds, state moves to Taxiing and we never return here. If it fails,
    // the bearing-to-junction fallback tone below takes over for the remainder.
    private bool _rolloutEarlyHandoffDone = false;
    // Set true when LandingRollout hands off early to Taxiing (TryEarlyExitHandoff
    // or turnBegun). While set, UpdatePosition monitors for post-handoff overshoot:
    // if the pilot rolls past the exit without turning, retarget or countdown.
    // Cleared when the exit is confirmed taken, on StopGuidance, or on next
    // BeginLandingRollout.
    private bool _rolloutHandoffActive = false;
    // After TryEarlyExitHandoff fires for a high-speed exit, stores ExitBearingTrue
    // as a minimum desired heading so the post-exit arc tone stays active during the
    // initial flat section where A* segment bearing is nearly parallel to the runway
    // (e.g. EIDW S5: 92 m at ~runway heading before the RET curve tightens).
    // Acts as a pan floor — whichever of the look-ahead bearing or this minimum
    // gives the stronger pan toward the exit side wins. Becomes a no-op once the
    // arc's natural segment bearings exceed it, and is RELEASED (reset to 0) the
    // frame the aircraft heading reaches/passes the exit bearing — the turn onto the
    // exit is complete, so the floor must stop clamping or it would mute a legitimate
    // opposite-side route cue. Also cleared on LoadRoute and StopGuidance.
    // 0 = inactive.
    private double _postHighSpeedExitMinBearing = 0.0;
    // Timestamp of the last undershoot retarget. DateTime.MinValue = no retarget
    // yet this rollout. Guards against rapid cascade retargeting when multiple
    // earlier exits are within ROLLOUT_UNDERSHOOT_RANGE_FT.
    private DateTime _lastUndershootRetargetTime = DateTime.MinValue;
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

    // Backtrack state. Entered from runway-end countdown once the pilot has stopped
    // or begun a 180° turn. Guides on the reciprocal runway heading until the
    // aircraft reaches the first taxi-graph connection node.
    private double _backtrackHeadingTrue;
    private double _backtrackConnectionLat;
    private double _backtrackConnectionLon;
    private int    _backtrackConnectionNodeId;  // 0 = no graph node found within range
    private bool   _backtrackApproachAnnounced; // "taxiway ahead" callout fired

    // --- DIAGNOSTIC LOGGING (permanent rollout diagnostics) ---
    // Captures rollout-phase state to landing_exit.log for troubleshooting
    // UpdateLandingRollout and approach-callout firing. Rate-limited
    // (one snapshot per few seconds); kept as permanent diagnostic tooling
    // (owner decision 2026-07).
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
    // Above this GS a heading deviation is NOT treated as a deliberate exit
    // turn. Category E rapid-exit taxiways top out at ~90 kt; at higher speeds
    // the deviation is touchdown yaw, crosswind crab alignment, or sim physics
    // at wheel contact — not a real runway exit maneuver.
    private const double ROLLOUT_TURN_MAX_GS_KTS = 90.0;

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

    // Overshoot margin for a HIGH-SPEED (rapid-exit) taxiway. A RET curves away
    // from the runway so gently (ICAO design radius >= 550 m) that at 100 ft
    // past the exit a CORRECT turn is still only ~3 deg of heading and ~3 ft
    // off the centerline — indistinguishable from rolling straight past. The
    // flat 100 ft margin therefore mistakes a correct RET turn for a miss and
    // retargets, cascading exit-to-exit down the runway. ~500 ft gives the turn
    // room to register before an overshoot can be declared.
    private const double ROLLOUT_HIGHSPEED_OVERSHOOT_FT = 500.0;

    // Cross-track-from-centerline gate for overshoot detection: a genuine
    // overshoot is past the exit AND still tracking the runway. Once the
    // aircraft has moved this far off the centerline it is curving onto the
    // exit, not missing it — not an overshoot regardless of distance past.
    private const double OVERSHOOT_ON_CENTERLINE_FT = 30.0;

    // If the aircraft is within this distance of an earlier exit while below
    // the undershoot speed threshold, retarget to minimise runway occupancy.
    // 1000 ft gives ~15 s at 40 kt to hear the callout and react.
    private const double ROLLOUT_UNDERSHOOT_RANGE_FT = 1000.0;
    // Outer speed threshold for the undershoot scan. Below this speed any
    // earlier shallow or high-speed exit is viable.
    private const double ROLLOUT_UNDERSHOOT_ENTRY_GS_KTS = 50.0;
    // Steep exits need the aircraft to be slower — the tighter turn demands
    // more braking margin. Only included in the undershoot scan below this speed.
    private const double ROLLOUT_UNDERSHOOT_STEEP_ANGLE_DEG = 45.0;
    private const double ROLLOUT_UNDERSHOOT_STEEP_GS_KTS = 20.0;
    // Minimum gap between consecutive undershoot retargets. Prevents rapid
    // cascade when multiple earlier exits fall within the range window.
    private const double ROLLOUT_UNDERSHOOT_COOLDOWN_SEC = 8.0;
    // Minimum lead distance for an undershoot retarget: the earlier exit must be
    // far enough ahead for the aircraft to react, decelerate and set up the turn
    // at the current speed. A floor plus a speed-proportional term.
    //
    // IMPLICIT COUPLING with ROLLOUT_UNDERSHOOT_RANGE_FT (1000): at gs ≥ 91 kt
    // the min-lead floor (91 · 11 = 1001 ft) exceeds the scan range, so the
    // undershoot retarget effectively no-ops. This is intentional — 90 kt is
    // the high-speed-exit ceiling (Category E RETs top out there), beyond which
    // any retarget is unsafe at any range. If you change either constant, check
    // the cutoff GS still lines up.
    private const double ROLLOUT_UNDERSHOOT_MIN_LEAD_FT = 200.0;
    private const double ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT = 11.0;

    // Distance from the chosen exit at which the rollout tone snaps from
    // runway-heading guidance (centreline tracking) to exit-bearing guidance.
    // Chosen to give the pilot ~2 s of directional preview before the
    // "turn now" speech fires at 150 ft — unambiguous "this direction" cue
    // without the premature-action risk of a long gradual build-up.
    private const double ROLLOUT_EXIT_TONE_ARM_FT = 300.0;

    // Explicit thresholds used in the exit-guidance zone (dist ≤ ARM_FT).
    // Tighter than the width-scaled runway values (4.2° activation on a wide
    // runway) so the tone gives fine steering on shallow exits (3–5°) where
    // the heading correction is tiny. Silent at 1.5° covers GPS jitter;
    // activation at 2.5° catches any exit ≥ 3° while keeping truly
    // straight-ahead exits (< 2.5°) silent. Max pan matches runway lineup.
    private const double ROLLOUT_EXIT_TONE_SILENT_DEG = 1.5;
    private const double ROLLOUT_EXIT_TONE_ACTIVATION_DEG = 2.5;
    private const double ROLLOUT_EXIT_TONE_MAX_PAN_DEG = 15.0;

    // Ground speed below which the steering tone activates during rollout.
    // Above this speed the tone is silent — at 80+ kt a pan cue is useless
    // because the pilot can't react and a high-speed exit is still many
    // seconds away. Below this (roughly approach-speed walk-down final
    // phase), the tone behaves like taxi-assist: centred = silent, deviated
    // = pans toward the exit so the pilot steers to keep it centred and
    // naturally turns onto the exit. 50 kt gives ~10 s more lead time than
    // 40 kt at typical deceleration — meaningful for high-speed RETs (40–60 kt).
    private const double ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS = 50.0;

    // Below this ground speed, runway-end countdown mode considers the
    // aircraft effectively stopped and hands off to plain Taxiing. Lower
    // than ROLLOUT_TAXI_GS_KTS (30) because the pilot in this mode is
    // braking hard with no further callouts coming; once they're at a
    // crawl the countdown has nothing more useful to say.
    private const double ROLLOUT_NO_EXIT_STOPPED_GS_KTS = 3.0;

    // Backtrack guidance thresholds.
    // Within ANNOUNCE distance: fires the "taxiway ahead" callout.
    // Within HANDOFF distance: transitions to Taxiing so the pilot can pick up
    // normal routing via Taxi Assist. 25m = WAYPOINT_CAPTURE_RADIUS_M equivalent.
    private const double BACKTRACK_TAXI_ANNOUNCE_M = 200.0;
    private const double BACKTRACK_HANDOFF_M        = 25.0;

    // Lineup thresholds — runway needs degree-level precision because takeoff roll
    // amplifies any heading error; gate is more forgiving since there's no roll.
    private const double LINEUP_HEADING_TOLERANCE_DEG = 5.0;             // gate default
    private const double LINEUP_CENTERLINE_TOLERANCE_FEET = 25.0;        // gate default cross-track band
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

    // Minimum along-runway distance (m) the lineup point must still be AHEAD of
    // the aircraft for the LiningUp status readout to speak "X to go". At or
    // below this (abeam/behind — the normal case once the pilot has turned onto
    // the runway at an entry downfield of the start-table point), the distance
    // is omitted: a lineup point behind the aircraft has no actionable meaning,
    // and a straight-line readout there GROWS with correct forward progress
    // (the "increasing distance while centered" complaint, KBWI 28 2026-06-11).
    private const double LINEUP_TOGO_MIN_AHEAD_M = 5.0;

    // Conversion constants
    private const double METERS_TO_FEET = 3.28084;
    private const double METERS_TO_NM = 0.000539957;

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
    /// <summary>
    /// The unreachable-runway warning for the most recent LoadRoute, or null if
    /// the route reaches its runway. The caller (TaxiAssistForm) speaks this
    /// AFTER StartGuidance so it isn't stomped by the first-taxiway callout.
    /// </summary>
    public string? LastRouteReachWarning { get; private set; }
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

        TaxiGraph? graph;

        // _stateLock serializes the cache read + build + write against the background-thread
        // OnAirportDataUpdated (which nulls the cache) and the locked GetStatusAnnouncement
        // overload — without it, OnAirportDataUpdated could null _whereAmICachedGraph between
        // the read here and a later use, or tear the (graph, icao) pair. DescribeLocation runs
        // OUTSIDE the lock on the local graph reference (a pure query, no shared state).
        lock (_stateLock)
        {
            // Prefer the active guidance graph if it's for this airport
            if (_graph != null && string.Equals(_icao, icao, StringComparison.OrdinalIgnoreCase))
                graph = _graph;
            else if (_whereAmICachedGraph != null &&
                     string.Equals(_whereAmICachedIcao, icao, StringComparison.OrdinalIgnoreCase))
                graph = _whereAmICachedGraph;
            else
                graph = null;

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
        }

        string description = graph.DescribeLocation(lat, lon);
        if (string.IsNullOrEmpty(description))
            return $"Not on a known taxiway or ramp at {icao}.";

        return $"{description} at {icao}.";
    }

    /// <summary>
    /// Invalidates the "Where Am I" graph cache. Call on aircraft change or when the
    /// user explicitly wants a fresh graph (rare). Takes _stateLock to serialize against
    /// its twin OnAirportDataUpdated and the locked DescribeCurrentLocation/
    /// GetStatusAnnouncement readers — both touch the same _whereAmICachedGraph/
    /// _whereAmICachedIcao pair, so an unlocked write here could race a locked read/build
    /// elsewhere and leave the pair inconsistent (graph set but ICAO stale, or vice versa).
    /// </summary>
    public void ClearWhereAmICache()
    {
        lock (_stateLock)
        {
            _whereAmICachedGraph = null;
            _whereAmICachedIcao = "";
        }
    }

    /// <summary>
    /// Online taxiway-name augmentation for <paramref name="icao"/> was just (re)fetched. Drop any
    /// cached Where-Am-I graph built from the OLDER (pre-augmentation) data so the next query rebuilds
    /// with the fresh names — keeps Where-Am-I real-time without a manual refresh. Invoked from the
    /// background fetch thread, so it MUST take _stateLock to serialize against the UI-thread
    /// Where-Am-I reader/writer (DescribeCurrentLocation) and the locked GetStatusAnnouncement
    /// overload — both touch the same _whereAmICachedGraph/_whereAmICachedIcao pair.
    /// </summary>
    public void OnAirportDataUpdated(string icao)
    {
        if (string.IsNullOrEmpty(icao)) return;
        lock (_stateLock)
        {
            if (string.Equals(_whereAmICachedIcao, icao, StringComparison.OrdinalIgnoreCase))
            {
                _whereAmICachedGraph = null;
                _whereAmICachedIcao = "";
            }
        }
    }

    /// <summary>
    /// Attempts to get a runway lineup reference from the current taxi guidance state.
    /// Returns true if the taxi is actively lining up to a runway (LiningUp state, runway target
    /// available), AND the aircraft has arrived at the runway (_hasLineupTarget set with valid
    /// coordinates). Used by takeoff assist to seed its reference without re-teleporting.
    /// Takes _stateLock like every other accessor of _state/_hasLineupTarget/the lineup fields —
    /// this is a public cross-thread read and must not observe a torn snapshot mid-update.
    /// </summary>
    public bool TryGetRunwayLineupReference(
        out double thresholdLat, out double thresholdLon,
        out double headingTrue, out double headingMag,
        out string runwayId, out string airportIcao)
    {
        thresholdLat = 0; thresholdLon = 0;
        headingTrue = 0; headingMag = 0;
        runwayId = ""; airportIcao = "";

        lock (_stateLock)
        {
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
    }

    /// <summary>
    /// Detects the runway under the aircraft using the airport's taxi graph and
    /// fills in everything needed to seed TakeoffAssistManager's runway reference
    /// (threshold lat/lon, true and magnetic runway heading, designator, ICAO).
    /// Mirrors the graph-caching policy of DescribeCurrentLocation: reuses the
    /// active guidance graph if its ICAO matches, otherwise the Where-Am-I cached
    /// graph, otherwise builds and caches a fresh graph for this ICAO.
    ///
    /// Does NOT mutate any guidance state. Pure query. Caller is responsible for
    /// gating on `_lastOnGround` (no callsite should ever ask this airborne).
    /// </summary>
    /// <param name="dataProvider">Airport data provider for graph rebuilds.</param>
    /// <param name="icao">Airport ICAO (4-char canonical).</param>
    /// <param name="lat">Aircraft latitude (degrees).</param>
    /// <param name="lon">Aircraft longitude (degrees).</param>
    /// <param name="aircraftHeadingMag">Aircraft magnetic heading (degrees).</param>
    /// <param name="magVar">Magnetic variation (degrees, east positive).</param>
    /// <param name="thresholdLat">Out: upwind threshold latitude.</param>
    /// <param name="thresholdLon">Out: upwind threshold longitude.</param>
    /// <param name="headingTrue">Out: runway true heading (degrees).</param>
    /// <param name="headingMag">Out: runway magnetic heading (degrees).</param>
    /// <param name="runwayId">Out: runway designator (e.g. "27L").</param>
    /// <param name="airportIcao">Out: airport ICAO (echoes the input).</param>
    /// <returns>True if a runway was detected; false otherwise.</returns>
    public bool TryDetectRunwayUnderAircraft(
        IAirportDataProvider dataProvider,
        string icao,
        double lat, double lon, double aircraftHeadingMag, double magVar,
        out double thresholdLat, out double thresholdLon,
        out double headingTrue, out double headingMag,
        out string runwayId, out string airportIcao)
    {
        thresholdLat = 0; thresholdLon = 0;
        headingTrue = 0; headingMag = 0;
        runwayId = ""; airportIcao = icao ?? "";

        if (string.IsNullOrWhiteSpace(icao)) return false;

        lock (_stateLock)
        {
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
                    var paths = dataProvider.GetTaxiPaths(icao) ?? new List<TaxiPath>();
                    if (paths.Count == 0) return false;

                    var parking = dataProvider.GetParkingSpots(icao) ?? new List<ParkingSpot>();
                    var runwayStarts = dataProvider.GetRunwayStarts(icao) ?? new List<StartPosition>();

                    graph = TaxiGraph.Build(paths, parking, runwayStarts);
                    _whereAmICachedGraph = graph;
                    _whereAmICachedIcao = icao;
                }
                catch (Exception ex)
                {
                    Log.Debug("Taxi", 
                        $"TryDetectRunwayUnderAircraft graph build failed for {icao}: {ex.Message}");
                    return false;
                }
            }

            // Convert aircraft magnetic heading to true: True = Magnetic + MagVar
            // (east positive, matching the convention used elsewhere in the codebase
            // — see TakeoffAssistManager.Toggle()).
            double aircraftHeadingTrue = aircraftHeadingMag + magVar;

            if (!graph.TryGetRunwayAtPosition(
                    lat, lon, aircraftHeadingTrue,
                    out runwayId, out thresholdLat, out thresholdLon, out headingTrue))
            {
                return false;
            }

            // Return the runway's magnetic heading too — TakeoffAssistManager
            // uses it for the centerline pan comparison.
            headingMag = headingTrue - magVar;
            airportIcao = icao;

            return true;
        }
    }

    public event EventHandler<TaxiGuidanceState>? StateChanged;

    /// <summary>
    /// Fires once per route when the aircraft becomes lined up on the
    /// destination runway (lineup hysteresis met). Subscribed by MainForm
    /// to auto-activate Takeoff Assist when the user's setting permits.
    /// One-shot — gated by _autoActivateFired, which is reset on LoadRoute
    /// and StopGuidance.
    /// </summary>
    public event EventHandler<TakeoffAssistAutoActivateEventArgs>? RequestTakeoffAssistAutoActivate;

    public TaxiGuidanceManager(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;
        _steeringTone = new TaxiSteeringTone();
    }

    /// <summary>
    /// Starts active guidance (begins steering tone and position tracking).
    /// </summary>
    public void StartGuidance(UserSettings settings)
    {
        lock (_stateLock)
        {
        if (_route == null || _route.Segments.Count == 0) return;

        // If this route can't reach its runway, the form speaks the reach warning
        // right after this call. Open a short grace window so the informational
        // taxiway-crossing / taxiway-change callouts don't stomp it at start.
        _startChatterSuppressUntil = LastRouteReachWarning != null
            ? DateTime.UtcNow.AddSeconds(REACH_WARNING_CHATTER_GRACE_SEC)
            : DateTime.MinValue;

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

        // NOTE: the periodic ground-speed announcer used to live here. It has been moved to
        // the global Services/GroundSpeedAnnouncer (fed by the always-on GROUND_VELOCITY
        // continuous variable) so callouts work in every phase — takeoff roll, landing
        // rollout, taxi — not just while taxi guidance is active. Do not re-add it here.

        // Snapshot settings ONCE per frame (SV-5): SettingsManager.Current takes a
        // static lock on every access, and this frame runs entirely inside
        // _stateLock — two separate Current reads meant two lock acquisitions per
        // 30 Hz sample, so an options-dialog Save (which briefly holds that lock to
        // serialize/publish) could stall a position frame behind it. UserSettings is
        // a mutable reference the settings dialog edits IN PLACE (SettingsForm.OnOk
        // mutates the same `Current` object then calls Save(), which just republishes
        // the identical reference) — so this per-call snapshot observes the same
        // live-applied values a repeated Current read would; live-apply keeps working
        // frame-to-frame. Only the separate SettingsManager.Reset() path swaps in a
        // brand-new instance, and even then only the one frame whose snapshot was
        // taken just before the reset would use the pre-reset object — equal or
        // better than the old behavior, which could tear mid-frame between the two
        // reads if a save landed between them.
        var settings = SettingsManager.Current;

        // Refresh tone-direction settings once per frame, before any branch
        // (taxiing, lining-up, or runway-aligned hold) that can drive the
        // tone. This makes runtime toggles in Taxi Guidance Options take
        // effect on the very next sample without restarting taxi guidance.
        // Cheap — two property assignments per frame.
        _steeringTone.InvertPan = settings.TaxiGuidanceInvertSteeringTone;
        _steeringTone.HardPan   = settings.TaxiGuidanceHardPanTone;

        // Convert magnetic heading to true heading for bearing comparison
        // True heading = magnetic heading + magnetic variation (east positive)
        double headingTrue = headingMag + magVariation;
        if (headingTrue < 0) headingTrue += 360;
        if (headingTrue >= 360) headingTrue -= 360;

        _lastLat = lat;
        _lastLon = lon;
        _lastHeading = headingTrue;
        _positionInitialized = true;

        // Yaw-rate tracking for rollout anticipation (wrap-safe, low-passed).
        var nowYaw = DateTime.UtcNow;
        if (_lastYawSampleTime != DateTime.MinValue)
        {
            double dt = (nowYaw - _lastYawSampleTime).TotalSeconds;
            if (dt > 0.01 && dt < 1.0)
            {
                double dHdg = NormalizeAngle(headingTrue - _lastYawHeading);
                double rate = dHdg / dt;
                _yawRateDegSec = _yawRateDegSec * (1 - YAW_RATE_FILTER_ALPHA)
                               + rate * YAW_RATE_FILTER_ALPHA;

                // Yaw-episode tracking for the "Straighten." cue. An episode is a
                // sustained turn in one direction; a direction flip restarts it.
                if (Math.Abs(_yawRateDegSec) >= STRAIGHTEN_EPISODE_MIN_RATE_DEG_SEC)
                {
                    int s = Math.Sign(_yawRateDegSec);
                    if (_yawEpisodeSign != s)
                    {
                        _yawEpisodeSign = s;
                        _yawEpisodeTurnDeg = 0.0;
                        _straightenFiredThisEpisode = false;
                    }
                    _yawEpisodeTurnDeg += dHdg;
                }
                else if (Math.Abs(_yawRateDegSec) < STRAIGHTEN_EPISODE_END_RATE_DEG_SEC)
                {
                    _yawEpisodeSign = 0;
                    _yawEpisodeTurnDeg = 0.0;
                }
                else if (_yawEpisodeSign != 0)
                {
                    // between the two thresholds: episode continues accumulating
                    _yawEpisodeTurnDeg += dHdg;
                }
            }
            else if (dt >= 1.0)
            {
                _yawRateDegSec = 0;   // stale gap (paused sim / reconnect) — reset
                _yawEpisodeSign = 0;
                _yawEpisodeTurnDeg = 0.0;
            }
        }
        _lastYawSampleTime = nowYaw;
        _lastYawHeading = headingTrue;

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

        // Backtrack after runway-end: tone guides on reciprocal runway heading.
        if (_state == TaxiGuidanceState.BacktrackingOnRunway)
        {
            UpdateBacktracking(lat, lon, headingTrue, groundSpeedKts);
            return;
        }

        if (_state != TaxiGuidanceState.Taxiing || _route == null || _graph == null)
        {
            // After a landing-exit arrival the pilot is in Arrived state with no
            // route — they still need runway incursion warnings while taxiing to
            // their gate before opening the taxi planner (e.g. runway 16/34 at EIDW
            // lies east of the S6 exit on the way to the terminal).
            if (_state == TaxiGuidanceState.Arrived && _graph != null)
                CheckRunwayIncursion(lat, lon);
            // ProgressiveHold is a terminal no-op: tone is off, the aircraft holds,
            // the pilot sets the next leg. No tone, no recalc, no movement logic.
            // (The unreachable-runway safety net and lineup path are gated on
            // _isRunwayLineup / _hasLineupTarget, which progressive never sets.)
            return;
        }

        // Post-handoff overshoot monitor. After TryEarlyExitHandoff or the
        // turnBegun/exitedLaterally handoff from UpdateLandingRollout transitions
        // to Taxiing, the LandingRollout overshoot detector no longer runs. If
        // the pilot fails to turn, the off-route recalc would try to route BACK
        // to the missed exit — dangerous on an active runway. This block detects
        // that case and retargets to the next downfield exit or triggers the
        // runway-end countdown, exactly like the LandingRollout overshoot path.
        if (_rolloutHandoffActive && _rolloutExit != null && _rolloutRunway != null)
        {
            double hdgDeltaAbsPH = Math.Abs(NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue));
            bool turnBegunPH = hdgDeltaAbsPH >= ROLLOUT_TURN_BEGAN_HDG_DEG
                               && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS;

            double halfWidthFtPH = (_rolloutRunway.Width > 0 ? _rolloutRunway.Width : 200.0) * 0.5;
            double lateralFtPH = AbsLateralFromRunwayMeters(
                lat, lon,
                _rolloutRunway.StartLat, _rolloutRunway.StartLon,
                _rolloutRunwayHeadingTrue) * METERS_TO_FEET;
            // Bare lateral check (no dist/heading gate) is intentional in this
            // post-handoff context. The primary handoff in UpdateLandingRollout
            // uses a combined gate to avoid false-firing during anticipatory
            // drift (EDDB 24L → M3 case), but this block runs AFTER handoff in
            // Taxiing state and asks "has the pilot committed?" — gating it
            // would delay clearing _rolloutHandoffActive and could cause
            // spurious overshoot-retarget cascades. Commit b69a03d adds the
            // pastExit guard to the sibling alignedWithExitPH check below for
            // the analogous A/P-jitter concern; this lateral check doesn't
            // suffer the same false-positive mode.
            bool exitedLaterallyPH = lateralFtPH >= halfWidthFtPH + 30.0;

            double exitBrgErrPH = _rolloutExit.ExitBearingTrue != 0.0
                ? Math.Abs(NormalizeAngle(headingTrue - _rolloutExit.ExitBearingTrue))
                : double.MaxValue;

            double signedAlongPastFtPH = SignedAlongRunwayMeters(
                lat, lon,
                _rolloutExit.Latitude, _rolloutExit.Longitude,
                _rolloutRunwayHeadingTrue) * METERS_TO_FEET;

            // Must be past the exit junction AND heading toward the exit bearing to
            // count as committed. Without the pastExit guard, A/P jitter on shallow
            // exits (e.g. 7.6° at LGAV 03R D8/D9) falsely satisfies this check while
            // the aircraft is still hundreds of feet short, killing the overshoot monitor
            // prematurely. Thresholds match alignedWithExit in UpdateLandingRollout.
            bool alignedWithExitPH = _rolloutExit.ExitBearingTrue != 0.0
                && _rolloutExit.ExitAngleDegrees >= 3.0
                && exitBrgErrPH <= 5.0
                && hdgDeltaAbsPH >= Math.Max(2.0, _rolloutExit.ExitAngleDegrees * 0.7)
                && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS
                && signedAlongPastFtPH >= 0.0;

            if (turnBegunPH || exitedLaterallyPH || alignedWithExitPH)
            {
                // Pilot has taken the exit — stop monitoring.
                _rolloutHandoffActive = false;
            }
            else
            {
                // A high-speed (rapid-exit) taxiway needs a larger margin before
                // a miss can be declared — a correctly-turning aircraft carries
                // downfield while still only a few degrees into the turn. But
                // the old fixed 500 ft margin was too generous for shallow
                // exits (e.g. 17°): the guidance pointed at ApronNodeId ~435 ft
                // past the junction, giving counterproductive left pan for
                // hundreds of feet. Use an angle-proportional formula:
                // fire as soon as the lateral displacement of a committed
                // aircraft would exceed OVERSHOOT_ON_CENTERLINE_FT + 5 ft.
                double overshootMarginPH;
                if (_rolloutExit.ExitType == "High-speed" && _rolloutExit.ExitAngleDegrees > 0.0)
                {
                    double radPH = _rolloutExit.ExitAngleDegrees * Math.PI / 180.0;
                    double angleBasedFtPH = (OVERSHOOT_ON_CENTERLINE_FT + 5.0) / Math.Sin(radPH);
                    overshootMarginPH = Math.Max(ROLLOUT_OVERSHOOT_FT,
                                        Math.Min(angleBasedFtPH, ROLLOUT_HIGHSPEED_OVERSHOOT_FT));
                }
                else
                {
                    overshootMarginPH = _rolloutExit.ExitType == "High-speed"
                        ? ROLLOUT_HIGHSPEED_OVERSHOOT_FT : ROLLOUT_OVERSHOOT_FT;
                }
                if (signedAlongPastFtPH >= overshootMarginPH
                    && lateralFtPH < OVERSHOOT_ON_CENTERLINE_FT)
                {
                    _rolloutHandoffActive = false;

                    Navigation.LandingExit? nextExitPH = null;
                    foreach (var e in _rolloutAllExits)
                    {
                        if (e.DistanceFromThresholdFeet <= _rolloutExit.DistanceFromThresholdFeet + ROLLOUT_OVERSHOOT_FT)
                            continue;
                        if (e.ExitAngleDegrees > 0.0 && e.ExitAngleDegrees > 90.0)
                            continue;
                        nextExitPH = e;
                        break;
                    }

                    if (nextExitPH != null)
                    {
                        RetargetLandingExit(nextExitPH, lat, lon, headingTrue);
                        return;
                    }

                    string rwyLabelPH = !string.IsNullOrEmpty(_rolloutRunway.RunwayID)
                        ? _rolloutRunway.RunwayID : "this runway";
                    AnnounceInstruction($"Missed last exit on runway {rwyLabelPH}.");
                    EnterRunwayEndCountdown();
                    return;
                }
            }
        }

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

        bool onFinalSegment = _currentSegmentIndex == _route.Segments.Count - 1;

        // Landing-exit ("vacate onto the taxiway") arrival — fire the "Off the runway.
        // Stop and hold position." closure for EVERY exit type. The final node is the
        // hold-clear point past the runway; the pilot rolls through it at taxi speed
        // and won't pass within the tight gate radius, so use looser captures: a 25 m
        // radius for a normal rolling pass, plus an along-track backstop for a wide
        // pass (drew abeam/past the node but never entered the radius). Without this
        // the pilot gets silence and sails up the taxiway. Gated on !_hasLineupTarget
        // so only HandleArrival's "just stop" / landing-exit branch is affected
        // (landing-exit routes never carry lineup data — they don't go to a gate).
        if (_isLandingExitRoute && onFinalSegment && !_hasLineupTarget)
        {
            AlongTrackToSegmentEnd(lat, lon, currentSeg,
                out double alongRemainingM, out double crossM);
            bool pastNode = alongRemainingM <= 0.0
                            && Math.Abs(crossM) <= LANDING_EXIT_ARRIVAL_CROSS_M;

            // Don't declare "Off the runway. Stop and hold." while the aircraft is
            // still within the runway half-width. An early/aggressive exit turn can
            // re-route the final approach back toward the runway and trip the 25 m
            // arrival on the pavement (OMDB 30L → K10). Suppress arrival while still
            // on the runway PROVIDED the extension node is still ahead (alongRemaining
            // > 0) so the look-ahead tone keeps guiding the pilot off — once clear,
            // arrival fires normally. If the node is already behind while still on the
            // runway (degenerate on-runway extension node), fall through and arrive
            // rather than risk the tone chasing a passed node back across the runway.
            bool stillOnRunway = false;
            if (_rolloutRunway != null && _rolloutRunwayHeadingTrue != 0.0 && alongRemainingM > 0.0)
            {
                // Runway.Width is in FEET (matches the halfWidthFt usages elsewhere in
                // this file, e.g. the overshoot-gate lateral check); convert to a metre
                // half-width before comparing against the metre lateral offset. A bare
                // Width * 0.5 treated as metres made the band ~3x too wide and suppressed
                // the "off the runway" arrival long after the aircraft had cleared.
                // Fallback 200 ft ≈ 60 m, same as the sibling code.
                double halfWidthM = (_rolloutRunway.Width > 0 ? _rolloutRunway.Width : 200.0)
                                    * 0.5 / METERS_TO_FEET;
                double lateralM = AbsLateralFromRunwayMeters(
                    lat, lon, _rolloutRunway.StartLat, _rolloutRunway.StartLon,
                    _rolloutRunwayHeadingTrue);
                stillOnRunway = lateralM <= halfWidthM + RUNWAY_CLEAR_MARGIN_M;
            }

            if (!stillOnRunway && (distToTarget < LANDING_EXIT_ARRIVAL_RADIUS_M || pastNode))
            {
                RolloutDiag($"Landing-exit arrival: distToTarget={distToTarget:F0}m " +
                    $"alongRemaining={alongRemainingM:F0}m cross={crossM:F0}m pastNode={pastNode}");
                HandleArrival();
                return;
            }
        }

        if (onFinalSegment)
        {
            bool arrived = distToTarget < arrivalRadius;

            // Past-node backstop for plain taxiway-endpoint destinations
            // (progressive-taxi "end of taxiway X"): no lineup target, no parking
            // countdown, no docking — so a roll-through at taxi speed can miss the
            // tight 6 m radius and the tone then chases the now-behind node. Scoped
            // to !_hasLineupTarget && !_isRunwayLineup so gate parking countdown and
            // runway lineup are untouched; landing-exit routes are already handled
            // (and returned) by the branch above. Mirrors that along-track backstop.
            if (!arrived && !_hasLineupTarget && !_isRunwayLineup)
            {
                AlongTrackToSegmentEnd(lat, lon, currentSeg,
                    out double alongRemEndM, out double crossEndM);
                if (alongRemEndM <= 0.0
                    && Math.Abs(crossEndM) <= ENDPOINT_ARRIVAL_BACKSTOP_CROSS_M)
                {
                    arrived = true;
                }
            }

            if (arrived)
            {
                // Announce the 50/20/10ft countdown one last time before arriving,
                // so it fires even if updates are sparse near the target.
                CheckParkingCountdown(distToTarget);
                HandleArrival();
                return;
            }
        }

        // Calculate heading error for steering tone using LOOK-AHEAD target
        // This prevents tone jitter from very short segments (5-15m in navdata)
        var (targetLat, targetLon) = GetGuidanceTarget(lat, lon);
        double bearingToTarget = NavigationCalculator.CalculateBearing(lat, lon, targetLat, targetLon);
        double headingError;
        if (_rolloutHandoffActive)
        {
            // During the post-handoff exit phase, use segment bearing instead of
            // bearing-to-waypoint. The exit node sits north of the runway; while the
            // aircraft is still on the pavement, bearing-to-node is nearly due north
            // (~350° for a westward runway), giving ~80° of right pan regardless of
            // actual heading. That drove the pilot far past the exit arc and into a loop.
            // Segment bearing tracks the arc itself (288.6° → 289.7° → 296.2° for a
            // shallow RET) and decays correctly to zero as the aircraft aligns.
            // _rolloutHandoffActive clears at turnBegunPH (15° from runway heading),
            // by which point the aircraft is physically on the exit and look-ahead works.
            headingError = NormalizeAngle(currentSeg.BearingDegrees - headingTrue);
        }
        else
        {
            headingError = NormalizeAngle(bearingToTarget - headingTrue);
        }

        // Initial big-turn cue (one-shot, first taxiing frame). When guidance
        // starts with the aircraft pointing well away from the route's first
        // segment — the normal post-pushback case — a tone alone can't convey
        // "turn around, which way." Speak a one-shot direction cue (sign matches
        // the tone). Skipped when the route doesn't reach its runway
        // (LastRouteReachWarning set): that warning is the priority — the form
        // speaks it after StartGuidance, and a turn cue here would be moot (the
        // pilot will reprogram) AND would stomp the warning.
        if (!_initialTurnCueAnnounced)
        {
            _initialTurnCueAnnounced = true;
            double absInitErr = Math.Abs(headingError);
            if (absInitErr >= INITIAL_TURN_CUE_DEG && LastRouteReachWarning == null)
            {
                string dir = headingError < 0 ? "left" : "right";
                bool hasTw = !string.IsNullOrEmpty(_lastAnnouncedTaxiway);
                string cue = absInitErr >= INITIAL_TURN_UTURN_DEG
                    ? (hasTw ? $"Taxiway {_lastAnnouncedTaxiway} is behind you. Turn {dir} to come around."
                             : $"Make a U-turn to the {dir}.")
                    : (hasTw ? $"Sharp turn {dir} onto taxiway {_lastAnnouncedTaxiway}."
                             : $"Sharp turn {dir}.");
                AnnounceInstruction(cue);
            }
        }

        // Post-high-speed-exit: ExitBearingTrue acts as a minimum pan floor so the
        // tone stays active during the initial flat section of a shallow RET where
        // the A* segment bearing is nearly parallel to the runway. Takes whichever of
        // the look-ahead error or the minimum-bearing error gives the stronger pan
        // toward the exit side. Becomes a no-op once the arc steers harder than the
        // minimum. Cleared by LoadRoute and StopGuidance.
        if (_postHighSpeedExitMinBearing > 0.0)
        {
            double minError = NormalizeAngle(_postHighSpeedExitMinBearing - headingTrue);

            // The floor only bridges the turn ONTO the exit. exitSide (exit bearing
            // relative to the runway heading) is the direction the floor pushes. Once
            // the aircraft heading has swung all the way to — or past — the exit
            // bearing, minError crosses zero against exitSide: the turn is complete and
            // the floor has done its job. RELEASE it here. Without this the floor would
            // persist until the next LoadRoute/StopGuidance, and a legitimate opposite-
            // side cue from the live route (e.g. the exit taxiway curving back the other
            // way, or the look-ahead wrapping onto the next segment) would be clamped to
            // silence — the worst failure mode for a blind pilot relying on the tone.
            double exitSide = NormalizeAngle(_postHighSpeedExitMinBearing - _rolloutRunwayHeadingTrue);
            bool turnComplete = (exitSide > 0.0 && minError <= 0.0)
                             || (exitSide < 0.0 && minError >= 0.0);

            // Release the floor when the LIVE route is steering clearly the OPPOSITE
            // way from ExitBearingTrue. ExitBearingTrue is the exit's first runway-edge
            // bearing; at some airports it points to the opposite side from where the
            // taxiway actually routes to the apron (CYVR M1 off 26R: first edge heads
            // NW ~305°, but the M1 taxiway curves SOUTH). Holding the floor there steered
            // the aircraft the WRONG way (right toward 305°), then snapped ~115° left the
            // instant the floor released at turnComplete — a violent L/R reversal on
            // rollout. The constructed route is authoritative; once it commits the other
            // way by more than a noise margin, drop the floor for good. The opposite-SIGN
            // test (not magnitude vs. the floor) is what distinguishes this from the
            // shallow-RET case the floor exists for, where the live route runs ~parallel
            // to the runway ON the exit side (same sign as the floor, so never released).
            const double FLOOR_OPPOSITE_RELEASE_DEG = 10.0;
            bool routeOpposesFloor = Math.Sign(headingError) == -Math.Sign(minError)
                                     && Math.Abs(headingError) >= FLOOR_OPPOSITE_RELEASE_DEG;
            if (turnComplete || routeOpposesFloor)
            {
                _postHighSpeedExitMinBearing = 0.0;
            }
            else if (_rolloutHandoffActive)
            {
                // _rolloutHandoffActive: using segment bearing, which can be shallower
                // than ExitBearingTrue on a stub RET (e.g. EIDW S5 at ~runway heading).
                // Floor ensures the tone stays at least at ExitBearingTrue in that case.
                headingError = minError >= 0.0 ? Math.Max(headingError, minError)
                                               : Math.Min(headingError, minError);
            }
            else
            {
                // _rolloutHandoffActive already cleared (turnBegunPH at 15°) but
                // turnComplete not yet fired. bearing-to-target is still distorted
                // (exit node sits north of the runway). Use ExitBearingTrue directly
                // so the tone decays smoothly to zero rather than jumping to a large
                // bearing-to-node error for the brief gap between the two releases.
                headingError = minError;
            }
        }

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
        if (!_steeringToneSuppressed)
        {
            // Rollout anticipation: only while genuinely yawing, so straight-line
            // sensor noise can't perturb the tone. Runway/gate lineup and docking
            // keep their own precision profiles — this applies to Taxiing only.
            // TurnLeadSeconds is per-aircraft (0 = disabled).
            double toneError = TurnLeadSeconds > 0.0
                               && Math.Abs(_yawRateDegSec) >= TURN_LEAD_MIN_RATE_DEG_SEC
                ? GuidanceGeometry.ProjectHeadingError(
                      _smoothedHeadingError, _yawRateDegSec, TurnLeadSeconds, TURN_LEAD_MAX_DEG)
                : _smoothedHeadingError;
            // Soften discontinuous target jumps (segment-skip on a sharp corner,
            // or a route recalc) into a smooth sweep — see SlewLimitToneError.
            toneError = SlewLimitToneError(toneError);
            _steeringTone.Resume();
            _steeringTone.UpdateHeadingError(toneError, currentSeg.PathWidth);
        }

        // Turn rollout cue ("Straighten.") — yaw-episode based, independent of
        // route junction classification (see the _yawEpisodeSign field comment:
        // real navdata splits 90° turns into micro-bends, so per-junction angle
        // gates never fire). Fires once per sustained-yaw episode when the
        // aircraft has genuinely turned (≥ STRAIGHTEN_MIN_TURN_DEG), the route
        // ahead is straight (a steady mid-curve arc holds projected error near
        // zero by DESIGN — without the straightness gate this would false-fire
        // inside every long curve), and the projected error crosses through
        // centre against the yaw direction: unwind NOW or overshoot.
        if (TurnLeadSeconds > 0.0 && _yawEpisodeSign != 0 && !_straightenFiredThisEpisode
            && Math.Abs(_yawRateDegSec) >= STRAIGHTEN_EPISODE_MIN_RATE_DEG_SEC
            && Math.Abs(_yawEpisodeTurnDeg) >= STRAIGHTEN_MIN_TURN_DEG)
        {
            var (slats, slons) = RoutePoints();
            double cumAhead = GuidanceGeometry.CumulativeTurnDeg(
                slats, slons, _currentSegmentIndex, _lastLat, _lastLon,
                STRAIGHTEN_AHEAD_WINDOW_M, out _);
            if (Math.Abs(cumAhead) < STRAIGHTEN_AHEAD_STRAIGHT_DEG)
            {
                double projected = GuidanceGeometry.ProjectHeadingError(
                    _smoothedHeadingError, _yawRateDegSec, TurnLeadSeconds, TURN_LEAD_MAX_DEG);
                // Crossed centre: projected error now on the OPPOSITE side of the
                // yaw direction (turning right but projected error says left).
                if (projected * _yawEpisodeSign <= 0)
                {
                    AnnounceInstruction("Straighten.");
                    _straightenFiredThisEpisode = true;
                }
            }
        }

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

        // Detect backwards travel along the segment axis. When the aircraft moves
        // in nearly the opposite direction to the segment bearing, cross-track stays
        // near zero — the perpendicular check above is blind to it. Compute the
        // unclamped signed along-track from the segment's start node: negative means
        // the aircraft is behind where the segment begins.
        //
        // TWO conditions must both hold to avoid the false-positive where the aircraft
        // is approaching the segment from behind in the correct direction (e.g. started
        // 20 m south of a NNE segment start and is heading NNE to reach it):
        //   (a) along-track is negative enough to be genuinely behind the start, AND
        //   (b) aircraft heading is MORE than 90° off the segment bearing — meaning
        //       the aircraft is moving away from the segment, not toward it.
        bool goingBackward;
        {
            const double MPD = 111132.0;
            double latMid = (currentSeg.FromNode.Latitude + currentSeg.ToNode.Latitude) * 0.5;
            double mpl = MPD * Math.Cos(latMid * Math.PI / 180.0);
            double dx = (currentSeg.ToNode.Longitude - currentSeg.FromNode.Longitude) * mpl;
            double dy = (currentSeg.ToNode.Latitude  - currentSeg.FromNode.Latitude)  * MPD;
            double px2 = (lon - currentSeg.FromNode.Longitude) * mpl;
            double py2 = (lat - currentSeg.FromNode.Latitude)  * MPD;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double signedAlongTrack = len < 1.0 ? 0.0 : (px2 * dx + py2 * dy) / len;
            double segBrg = len < 1.0 ? 0.0 : Math.Atan2(dx, dy) * 180.0 / Math.PI;
            if (segBrg < 0) segBrg += 360.0;
            double hdgVsSeg = Math.Abs(NormalizeAngle(headingTrue - segBrg));
            goingBackward = signedAlongTrack < -OFF_ROUTE_BEHIND_START_M && hdgVsSeg > 90.0;
        }

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
        if ((DateTime.UtcNow - _lastSegmentAdvanceTime).TotalSeconds < POST_TURN_OFFROUTE_GRACE_SEC)
            nearTurn = true;

        // Route-joined latch (see field). Until the aircraft has reached the route
        // line once, the taxi from the gate onto the first taxiway reads as
        // "off-route" by definition — suppress recalc so the entered clearance
        // isn't trimmed before the pilot joins it.
        if (perp <= perpTolerance) _hasJoinedRoute = true;

        bool offRouteNow = _hasJoinedRoute && !nearTurn && (perp > perpTolerance || farBehindStart || goingBackward);

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
                _offRouteSince = DateTime.UtcNow;

            if ((DateTime.UtcNow - _offRouteSince).TotalSeconds >= OFF_ROUTE_PERSISTENCE_SEC)
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
    /// Gets the steering-tone target: the point a speed-scaled look-ahead
    /// distance ahead along the route polyline, via the continuous arc-length
    /// walk in <see cref="GuidanceGeometry.WalkTarget"/>. Continuous in both
    /// aircraft position and segment advancement — replaces the old binary
    /// turn/no-turn target picker whose one-frame 100+ m target jumps caused
    /// violent steering-tone pan flips at junctions.
    /// </summary>
    private (double lat, double lon) GetGuidanceTarget(double aircraftLat, double aircraftLon)
    {
        if (_route == null || _route.Segments.Count == 0 ||
            _currentSegmentIndex >= _route.Segments.Count)
            return (aircraftLat, aircraftLon);

        var (lats, lons) = RoutePoints();
        double lookAhead = Math.Clamp(
            _lastGroundSpeedKts * 0.5144 * GUIDANCE_LOOK_AHEAD_SEC,
            GUIDANCE_LOOK_AHEAD_MIN_M, GUIDANCE_LOOK_AHEAD_MAX_M);
        return GuidanceGeometry.WalkTarget(
            lats, lons, _currentSegmentIndex, aircraftLat, aircraftLon, lookAhead);
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
            _holdShortOuterAnnounced = false;
            _holdShortSlowDownAnnounced = false;
            _holdShortStopAnnounced = false;
            return;
        }

        // All three callouts already fired — nothing left to compute. (Without this
        // early-out the live-distance label below was re-formatted ~30×/s for the
        // entire stationary wait at the hold line: pure garbage on the position thread.)
        if (_holdShortOuterAnnounced && _holdShortSlowDownAnnounced && _holdShortStopAnnounced)
            return;

        double distFeet = distToTargetM * METERS_TO_FEET;
        string what = !string.IsNullOrEmpty(currentSeg.HoldShortRunway)
            ? currentSeg.HoldShortRunway
            : "hold short";

        // Speed-based thresholds with fixed minimums and caps. Each tier gives the
        // pilot a fixed lead time (in seconds at current speed), floored so slow/
        // stopped aircraft always get full coverage. Caps prevent excessively-early
        // callouts on long taxiways.
        //   Outer      (300 ft min, 600 ft cap) — heads-up:   15 s lead
        //   Slow-down  (150 ft min, 400 ft cap) — brake cue:   8 s lead
        //   Stop        (50 ft min, 200 ft cap) — stop cue:    4 s lead
        // "Slow down." and "Stop." are unconditional — fire regardless of current
        // speed so the pilot always gets the directive, even at very low taxi speeds.
        const double ktsToFps = 1852.0 * 3.28084 / 3600.0;
        double outerDistFt    = Math.Clamp(_lastGroundSpeedKts * ktsToFps * 15.0, 300.0, 600.0);
        double slowDownDistFt = Math.Clamp(_lastGroundSpeedKts * ktsToFps *  8.0, 150.0, 400.0);
        double stopDistFt     = Math.Clamp(_lastGroundSpeedKts * ktsToFps *  4.0,  50.0, 200.0);

        // Hold-short speaks the LIVE distance: its triggers are speed-proportional
        // and cannot be pre-tabulated like the fixed parking/exit/runway-end milestones.
        // Formatted only inside the firing branch — not per frame.

        // Fire in natural countdown order. Each block returns after announcing so
        // only one callout fires per frame — no stacking.
        if (distFeet < outerDistFt && !_holdShortOuterAnnounced)
        {
            AnnounceInstruction($"Hold short {what} in {DistanceFormatter.FromFeet(distFeet)}.");
            _holdShortOuterAnnounced = true;
            return;
        }
        if (distFeet < slowDownDistFt && !_holdShortSlowDownAnnounced)
        {
            AnnounceInstruction($"Hold short {what} in {DistanceFormatter.FromFeet(distFeet)}. Slow down.");
            _holdShortSlowDownAnnounced = true;
            return;
        }
        if (distFeet < stopDistFt && !_holdShortStopAnnounced)
        {
            AnnounceInstruction($"Hold short {what} in {DistanceFormatter.FromFeet(distFeet)}. Stop.");
            _holdShortStopAnnounced = true;
            return;
        }
        // Stopped-in-zone fallback: pilot slowed to a stop between the slow-down
        // and stop thresholds — "slow down" fired but stop never triggered because
        // the aircraft parked above the stop-distance floor. Fire stop so the pilot
        // knows to hold position and await clearance.
        if (_holdShortSlowDownAnnounced && !_holdShortStopAnnounced && _lastGroundSpeedKts < 1.0)
        {
            AnnounceInstruction($"Hold short {what}. Stop.");
            _holdShortStopAnnounced = true;
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

        if ((DateTime.UtcNow - _lastSpeedWarningTime).TotalSeconds < SPEED_WARNING_COOLDOWN_SEC)
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
            _lastSpeedWarningTime = DateTime.UtcNow;
        }
        else if (normalTurnComing && _lastGroundSpeedKts > MAX_TAXI_SPEED_TURN_KTS)
        {
            _announcer.AnnounceImmediate("Slow for turn.");
            _lastSpeedWarningTime = DateTime.UtcNow;
        }
        else if (!normalTurnComing && !sharpTurnComing && _lastGroundSpeedKts > MAX_TAXI_SPEED_STRAIGHT_KTS)
        {
            _announcer.AnnounceImmediate($"Taxi speed, {(int)_lastGroundSpeedKts} knots.");
            _lastSpeedWarningTime = DateTime.UtcNow;
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
        if (_graph == null) return;

        // Currently-scheduled hold-short node (owned by CheckHoldShortCountdown).
        // Only meaningful when a route is active.
        int scheduledHsNodeId = -1;
        if (_route != null && _currentSegmentIndex < _route.Segments.Count &&
            _route.Segments[_currentSegmentIndex].IsHoldShortPoint)
        {
            scheduledHsNodeId = _route.Segments[_currentSegmentIndex].ToNode.NodeId;
        }

        if (_lastIncursionWarnedNodeId != -1 &&
            (DateTime.UtcNow - _lastIncursionWarningTime).TotalSeconds < INCURSION_WARNING_COOLDOWN_SEC)
            return;

        // Build the set of HS node-IDs that lie on the remaining planned route
        // (so "crossing" announcements fire for them; "off route" does not).
        // When _route is null (e.g. after landing-exit arrival, before the pilot
        // opens the taxi planner) the set stays empty and every approaching
        // hold-short node triggers the "off route" warning — exactly what we want.
        // Cached: rebuilt only when _route's reference or _currentSegmentIndex
        // changes since the last frame (see field comments above).
        if (!ReferenceEquals(_onRouteHsSourceRoute, _route) ||
            _onRouteHsSourceSegmentIndex != _currentSegmentIndex)
        {
            var set = new HashSet<int>();
            if (_route != null)
            {
                for (int i = _currentSegmentIndex; i < _route.Segments.Count; i++)
                {
                    var seg = _route.Segments[i];
                    if (seg.ToNode != null &&
                        (seg.ToNode.Type == TaxiNodeType.HoldShort || seg.ToNode.Type == TaxiNodeType.ILSHoldShort))
                    {
                        set.Add(seg.ToNode.NodeId);
                    }
                }
            }
            _cachedOnRouteHsNodes = set;
            _onRouteHsSourceRoute = _route;
            _onRouteHsSourceSegmentIndex = _currentSegmentIndex;
        }
        var onRouteHsNodes = _cachedOnRouteHsNodes;

        // Per-graph HS/ILS-HS candidate list, cached (see field comments above).
        if (!ReferenceEquals(_holdShortNodesGraph, _graph))
        {
            var list = new List<TaxiNode>();
            foreach (var node in _graph.Nodes.Values)
            {
                if (node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort)
                    list.Add(node);
            }
            _cachedHoldShortNodes = list;
            _holdShortNodesGraph = _graph;
        }

        // Scan only the cached HS/ILS-HS candidates (not every graph node)
        TaxiNode? nearestHs = null;
        double bestDist = INCURSION_WARN_DISTANCE_M;
        foreach (var node in _cachedHoldShortNodes!)
        {
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
        _lastIncursionWarningTime = DateTime.UtcNow;
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
        // Docking guidance owns the final approach to the precise GSX stop (a few metres
        // beyond this route's end node), so let ITS countdown be the only one — otherwise
        // taxi says "5 m. Stop." at the node while docking is still 20 m from the stop.
        if (_dockingActive) return;
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

        // All milestones fired — skip the per-frame table build (it allocates).
        if (_parkingAnnounce50 && _parkingAnnounce20 && _parkingAnnounce10)
            return;

        var pm = DistanceMilestones.ParkingArrival(); // far->near: [0]=50ft/15m, [1]=20ft/10m, [2]=10ft/5m

        // Fire thresholds in natural order (far → mid → near). Independent `if` blocks so
        // a fast arrival that first samples inside the mid or near trigger still fires the
        // earlier callouts. One announce per frame to avoid stacking.
        if (distToTargetM < pm[0].TriggerMetres && !_parkingAnnounce50)
        {
            AnnounceInstruction($"{_destinationName} in {pm[0].Label}.");
            _parkingAnnounce50 = true;
            return;
        }
        if (distToTargetM < pm[1].TriggerMetres && !_parkingAnnounce20)
        {
            AnnounceInstruction($"{pm[1].Label}.");
            _parkingAnnounce20 = true;
            return;
        }
        if (distToTargetM < pm[2].TriggerMetres && !_parkingAnnounce10)
        {
            // Docking pending: the GSX stop is still ahead of the navdata point this
            // countdown measures to — a "Stop." directive here parks the pilot short.
            AnnounceInstruction(_dockingPending
                ? $"{pm[2].Label}. Continue ahead for docking."
                : $"{pm[2].Label}. Stop.");
            _parkingAnnounce10 = true;
            return;
        }
        // Stopped-in-zone: pilot slowed to a stop between 10 ft and the gate.
        // Fire a generic "hold position" cue rather than naming the gate — the
        // aircraft hasn't actually arrived, so a `"{gate}. Stop."` callout reads
        // misleadingly like an arrival announcement. The dedicated arrival
        // announcement fires separately when the aircraft is within the
        // arrival radius. With docking PENDING the correct instruction is the
        // opposite — keep rolling until docking engages (KATL F3 2026-06-11).
        if (_parkingAnnounce50 && !_parkingAnnounce10 && _lastGroundSpeedKts < 1.0)
        {
            AnnounceInstruction(_dockingPending
                ? "Docking guidance ahead. Continue forward."
                : "Stop. Hold position.");
            _parkingAnnounce10 = true;
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
        _holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;

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
        // Progressive Taxi leg: reached its terminator (hold short / after crossing /
        // end of taxiway). Tone off, announce the end-of-leg callout, hold position.
        // This intercept must run before the runway/gate/lineup branches so a
        // progressive route never enters LiningUp, fires RequestTakeoffAssistAutoActivate,
        // or triggers docking arrival.
        if (_progressiveTerminator is { } term)
        {
            _steeringTone.Stop();
            SetState(TaxiGuidanceState.ProgressiveHold);
            AnnounceInstruction(term.EndAnnouncement());
            return;
        }

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
            // NOTE: taxi keeps steering here even when docking is active. The final
            // turn INTO the gate box happens in the last ~20 m past the route node —
            // taxi has the gate-lineup heading and can steer that turn; docking's
            // straight-to-stop tone cannot. Docking owns distance + the precise stop
            // (taxi's parking distance countdown is suppressed in CheckParkingCountdown
            // while _dockingActive), so the two never give conflicting countdowns, but
            // taxi remains the steering authority all the way in.
            SetState(TaxiGuidanceState.LiningUp);
            _lineupAnnouncedAligned = false;

            // Same reset as the runway-lineup path — start the lineup tone from
            // a clean smoother instead of carrying taxi-phase residual.
            _smoothedHeadingError = 0.0;
            _headingErrorInitialized = false;

            // _dockingActive here means docking has ENGAGED (engage-latched semantics): it
            // already announced "Docking guidance… X to stop" and owns the countdown to the
            // precise stop, so taxi stays silent to avoid a contradictory "Align with X" over
            // it. When docking has NOT engaged (the common navdata case), taxi announces the
            // alignment normally — this is the pilot's only arrival guidance then. Taxi holds
            // LiningUp state either way; its tone is muted separately while docking is active.
            if (!_dockingActive)
            {
                int hdgAnnounce = (int)Math.Round(_lineupHeadingMag);
                double headingError = NormalizeAngle(_lineupHeadingTrue - _lastHeading);
                string turnDir = Math.Abs(headingError) < 10 ? "" :
                    (headingError > 0 ? " Turn right." : " Turn left.");
                // Docking pending: the GSX stop is still ahead — tell the pilot now
                // so the arrival doesn't read as "park here" (see SetDockingPending).
                string dockNote = _dockingPending ? " Then continue ahead for docking guidance." : "";
                AnnounceInstruction($"Align with {_destinationName}, heading {hdgAnnounce}.{turnDir}{dockNote}");
            }

            // Keep tone going for lineup guidance
            _steeringTone.Resume();
            return;
        }

        // No lineup data — just stop.
        _steeringTone.Stop();
        SetState(TaxiGuidanceState.Arrived);

        if (_isLandingExitRoute)
        {
            // A Landing Exit Planner route terminates at the runway exit, not
            // at the gate. Tell the pilot explicitly what to do next — newer
            // pilots won't know the planner stops here by design, and the tone
            // simply going quiet reads as "broken" rather than "done, your
            // move". The taxi planner (Input mode + Shift+Y) builds the gate
            // route.
            string exitName = _route?.DestinationName ?? "the exit";
            AnnounceInstruction(
                $"Off the runway at {exitName}. Stop and hold position. " +
                $"Open the taxi planner to set a route to your gate.");
        }
        else if (!_dockingActive)
        {
            // Docking, when it owns this arrival, announces the stop itself — suppress the
            // generic "Destination reached" so the two don't double up.
            AnnounceInstruction($"{_route?.DestinationName ?? "Destination"} reached.");
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
        _initialTurnCueAnnounced = false;
        _startChatterSuppressUntil = DateTime.MinValue;
        // Reset the tone slew-limiter baseline so a fresh guidance session snaps
        // to its first target instead of sweeping from a stale value. (LoadRoute
        // resets it too, for the same reason. Recalcs do NOT — they swap the
        // route in place via TryRecalculateRoute without StopGuidance/LoadRoute —
        // so a recalc pan-snap is intentionally softened by the limiter.)
        _toneErrorInitialized = false;
        _lastGroundSpeedKts = 0;
        _hasLineupTarget = false;
        _progressiveTerminator = null;
        _lineupAnnouncedAligned = false;
        _lineupHugeCrossTrackSince = DateTime.MinValue;
        _runwayLineupUnreachableWarned = false;
        _routeReachesRunway = true;
        _autoActivateFired = false;
        // Reset all announcement latches — defense in depth; LoadRoute resets them too
        // but StopGuidance can be called independently (hotkey, takeoff-assist takeover).
        _approachAnnounced = false;
        _curveAnnouncedSign = 0;
        _turnImminentAnnounced = false;
        _crossingAnnounced = false;
        _lastCrossingNodeId = -1;
        _lastAnnouncedTaxiway = "";
        _holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;
        _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
        _lastIncursionWarnedNodeId = -1;
        // Reset cooldowns so a freshly-loaded route after Stop gets prompt warnings
        // instead of inheriting a stale cooldown from the prior session.
        _lastRecalculationTime = DateTime.MinValue;
        _lastSpeedWarningTime = DateTime.MinValue;
        _lastIncursionWarningTime = DateTime.MinValue;
        _offRouteSince = DateTime.MinValue;
        _hasJoinedRoute = false;
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
        _isLandingExitRoute = false;
        _rolloutRunway = null;
        _rolloutAllExits = new List<Navigation.LandingExit>();
        ResetRolloutApproachLatches();
        _rolloutNoExitMode = false;
        _rolloutHandoffActive = false;
        _rolloutEnd1500Announced = false;
        _rolloutEnd500Announced = false;
        _rolloutEnd100Announced = false;
        _backtrackConnectionNodeId = 0;
        _backtrackApproachAnnounced = false;
        _postHighSpeedExitMinBearing = 0.0;
        SetState(TaxiGuidanceState.Inactive);
        } // end lock(_stateLock)
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

    // --- DIAGNOSTIC LOGGING HELPER (permanent rollout diagnostics) ---
    // Writes to landing_exit.log alongside LandingExitPlanner.DiagLog so the
    // rollout-phase per-frame loop is interleaved with the planner's touchdown
    // events. Rate-limited (one enqueue per few seconds during rollout),
    // serialized through the shared LogWriter thread. Kept as permanent
    // diagnostic tooling (owner decision 2026-07).
    // NOTE: diag lines log raw FEET on purpose (unlike user-facing callouts, which
    // go through DistanceFormatter). The internal rollout math and its constants
    // are feet-native, and logs must be comparable across users regardless of the
    // GroundDistanceUnit setting — do not "fix" these to be unit-aware.
    private static readonly LogChannel _rolloutDiagLog = Log.Channel("landing_exit");

    /// <summary>
    /// Slew-rate limits the taxiing steering-tone heading error so a
    /// DISCONTINUOUS target jump (a segment-index skip when a large aircraft
    /// cuts a sharp corner, or a route recalc that resets the smoother) becomes
    /// a smooth fast sweep instead of an instant left↔right pan slam. A genuine
    /// turn changes the error gradually (≈ the yaw rate, well under the cap), so
    /// it passes through untouched; only one-frame artifacts get stretched.
    ///
    /// Baseline is established on the first call and persists across in-place
    /// route recalcs (so a recalc snap is softened too); it is reset by
    /// StopGuidance and LoadRoute, so a genuinely fresh guidance session still
    /// snaps to its first target on frame one rather than sweeping from a stale
    /// value (recalcs bypass both, so their softening is preserved).
    /// Wrap-safe: the step is taken along the shortest angular path and the
    /// result is re-normalized to [-180, 180]. A stale/huge dt (paused sim,
    /// reconnect) re-baselines rather than allowing an unbounded jump.
    /// </summary>
    private double SlewLimitToneError(double target)
    {
        var now = DateTime.UtcNow;
        if (!_toneErrorInitialized)
        {
            _toneErrorInitialized = true;
            _lastToneError = target;
            _lastToneErrorTime = now;
            return target;
        }

        double dt = (now - _lastToneErrorTime).TotalSeconds;
        _lastToneErrorTime = now;
        if (dt <= 0.0 || dt > 1.0)
        {
            // Non-positive (clock skew) or a long gap — don't let it license an
            // unbounded step; treat as a fresh baseline.
            _lastToneError = target;
            return target;
        }

        double maxStep = TAXI_TONE_MAX_SLEW_DEG_PER_SEC * dt;
        double delta = NormalizeAngle(target - _lastToneError);
        if (delta > maxStep) delta = maxStep;
        else if (delta < -maxStep) delta = -maxStep;
        _lastToneError = NormalizeAngle(_lastToneError + delta);
        return _lastToneError;
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
        // discontinuities). The per-line timestamp is now supplied by the shared
        // LogChannel formatter (local time, millisecond precision) instead of a
        // hand-rolled leading field.
        var now = DateTime.UtcNow;
        if ((now - _lastGuidanceLogTime).TotalMilliseconds < GUIDANCE_LOG_INTERVAL_MS) return;
        _lastGuidanceLogTime = now;
        try
        {
            string line = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "lat={0:F7},lon={1:F7},hdg={2:F1},gs={3:F1},seg={4},segBrg={5:F1},w={6:F0},nxtTurn={7},tLat={8:F7},tLon={9:F7},raw={10:F2},smooth={11:F2}",
                lat, lon, headingTrue, groundSpeedKts,
                segIdx, segBearing, pathWidthFeet,
                nextIsTurn ? 1 : 0, targetLat, targetLon,
                rawHeadingError, smoothedHeadingError);
            _guidanceLog.Info(line);
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

/// <summary>
/// Payload for TaxiGuidanceManager.RequestTakeoffAssistAutoActivate.
/// Identifies the runway the aircraft has just lined up on so the handler
/// can announce / log; the actual reference seeding is done by the
/// MainForm POSITION_FOR_TAKEOFF_ASSIST handler reading TryGetRunwayLineupReference.
/// </summary>
public class TakeoffAssistAutoActivateEventArgs : EventArgs
{
    public required string RunwayId { get; init; }
    public required string AirportIcao { get; init; }
}

