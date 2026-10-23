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
    /// toward the apron. Steering tone guides on the reciprocal runway heading until the
    /// taxiway connection node is announced, then on the bearing to that node. Hands off
    /// once the aircraft is clear of every runway corridor or within
    /// <see cref="Navigation.BacktrackConnectionHandoff.HandoffMetres"/> of the node
    /// (<see cref="Navigation.BacktrackConnectionHandoff"/>), or if no connection node
    /// was found nearby.
    /// </summary>
    BacktrackingOnRunway,
    /// <summary>
    /// FULL-LENGTH BACKTRACK DEPARTURE (opt-in via the Taxi planner checkbox).
    /// The pilot has been cleared onto the runway at an intermediate entrance and
    /// is backtracking on the reciprocal takeoff heading toward the departure
    /// threshold. The steering tone keeps them on the centerline (never silent —
    /// the tone pans to guide the entry turn and the centerline hold). On reaching
    /// the departure threshold it hands off to <see cref="LiningUp"/> at the
    /// full-length lineup point, which pans the 180° turnaround and lines up +
    /// auto-activates Takeoff Assist exactly as a normal full-length departure.
    /// Entirely separate from <see cref="BacktrackingOnRunway"/> (the landing-side
    /// backtaxi-to-apron maneuver).
    /// </summary>
    BacktrackDeparture
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
    /// <summary>Virtual-pilot harness only (tools/VirtualPilot): the tone the pilot is listening to.</summary>
    internal TaxiSteeringTone SteeringToneForHarness => _steeringTone;
    /// <summary>Virtual-pilot harness only: the route being flown, and the segment the manager is on.</summary>
    internal TaxiRoute? RouteForHarness => _route;
    internal int SegmentIndexForHarness => _currentSegmentIndex;
    private TaxiGraph? _graph;
    // DatabaseGeneration when _graph's instance was installed (never restamped when a rollout
    // re-route hands the same instance back). A database switch leaves a route's graph in place,
    // so this is how the runway probe knows that graph belongs to the previous database.
    private long _graphGeneration;
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

    /// <summary>
    /// Supplies the parking list every <see cref="TaxiGraph.Build"/> call in this class is fed —
    /// the ONE place a stand's name is decided app-wide (see <see cref="ParkingSpotSource"/>).
    /// Wired by <c>MainForm</c> to <see cref="ParkingSpotSource.GetNamedSpots"/>, so Where-Am-I
    /// calls a stand what the taxi dialog, the gate-teleport list and <c>gate.select</c> call it,
    /// instead of navdata's BGL parking-name enum (KJFK Terminal 4: "Gate B 25" vs navdata's
    /// "Gate A 25").
    ///
    /// <para>
    /// <b>It must return navdata's own spot SET, with names corrected in place</b> — same spots,
    /// same count, same coordinates, same order. Never a different list. <c>TaxiGraph.Build</c>'s
    /// parking pass writes <c>node.Type = TaxiNodeType.Parking</c> as well as the name, and
    /// <c>Type</c> is read by <c>NamedHoldingPointResolver</c> (which SKIPS parking nodes when
    /// snapping a named holding point), <c>HoldShortNodeResolver</c>, and the route truncation in
    /// <c>TaxiGuidanceManager.Routing</c>. A supplier that returned a different SET would move a
    /// hold-short and would strip the Where-Am-I label off any stand it omitted. Naming a stand and
    /// deciding which nodes are parking are different jobs, and only the first may change.
    /// </para>
    /// <para>
    /// <b>Null is the supported default</b>, and it keeps every caller that does not wire it —
    /// the xUnit suite, anything constructed outside MainForm — byte-identical to the
    /// pre-seam behaviour: <see cref="ResolveParkingSpots"/> falls straight back to
    /// <c>dataProvider.GetParkingSpots</c>. Same backward-compatibility shape as
    /// <see cref="GateDataSource"/>'s own optional constructor parameters.
    /// </para>
    /// <para>
    /// <b>It runs off the UI thread.</b> Where-Am-I and the takeoff-assist runway probe both
    /// reach their graph builds from inside a <c>RequestAircraftPositionAsync</c> callback, so
    /// MainForm's wiring must not hand out a <see cref="GateDataSource"/> instance the UI thread
    /// is also using — its per-ICAO caches are plain Dictionaries. The wiring builds a fresh one
    /// per call for exactly that reason; that is affordable ONLY because every call site here is
    /// a graph build (once per airport, then cached), never a position update.
    /// </para>
    /// </summary>
    public Func<string, List<ParkingSpot>>? ParkingSpotSupplier { get; set; }

    /// <summary>
    /// The last SIM_ON_GROUND sample (null while none has arrived), read on the position thread by the
    /// off-pavement alert, which speaks only on the ground (Navigation.OffPavementAlert.IsOffPavement).
    /// MainForm wires it to SimConnectManager.LastKnownOnGround. Unwired or unknown counts as ON the ground:
    /// the alert exists to catch a grass excursion, and missing air/ground data must not silence it.
    /// </summary>
    public Func<bool?>? OnGroundProvider { get; set; }

    /// <summary>
    /// True while the landing assist's rollout centerline cue is engaged (LandingFlareAssistManager.IsRolloutEngaged),
    /// read every rollout frame by the tone selection (RolloutExitGate.SelectToneMode's centerlineCueEngaged): exit
    /// guidance then pauses its own tone on the runway and takes the tone only for the exit. MainForm wires it.
    /// Unwired counts as not engaged, which is exactly the behaviour before the cue existed.
    /// </summary>
    public Func<bool>? CenterlineCueEngagedProvider { get; set; }

    // The rollout is held for an airborne sample (LandingExitGoAround.HoldsRollout); logged on each change only.
    private bool _rolloutAirborneHold;

    /// <summary>
    /// The parking list for <paramref name="icao"/> from <see cref="ParkingSpotSupplier"/>, or
    /// <paramref name="dataProvider"/>'s own when none is wired.
    ///
    /// <para>
    /// The supplier reaches into GSX (a live socket's published JSON, a profile directory, a
    /// navdata query), so it gets a try/catch the raw provider call never needed: a graph built
    /// with navdata's names is a slightly worse readout, while no graph at all is a blind pilot
    /// with no answer to "where am I". An empty list is a legitimate answer (an airport with no
    /// modelled stands) and is NOT treated as failure — falling back on empty would just re-ask
    /// the source <see cref="GateDataSource"/> itself already falls through to.
    /// </para>
    /// </summary>
    private List<ParkingSpot> ResolveParkingSpots(IAirportDataProvider dataProvider, string icao)
    {
        var supplier = ParkingSpotSupplier;
        if (supplier != null)
        {
            try
            {
                var spots = supplier(icao);
                if (spots != null) return spots;
            }
            catch (Exception ex)
            {
                Log.Debug("Taxi",
                    $"parking list: supplier failed for {icao}, falling back to navdata names: {ex.Message}");
            }
        }

        return dataProvider.GetParkingSpots(icao) ?? new List<ParkingSpot>();
    }

    // Retained for recalculation
    private IAirportDataProvider? _dataProvider;
    private int _destinationNodeId;
    private string _destinationName = "";
    private string _icao = "";
    private List<string>? _originalTaxiwaySequence;
    // The user's explicit hold-short picks from the form, stored (as private
    // copies) alongside the sequence they index into, so TryRecalculateRoute can
    // re-apply them to a recalculated route. Before these were kept, a mid-taxi
    // recalc silently dropped every user-picked hold-short (they were LoadRoute
    // parameters only).
    private List<int>? _userHoldShortIndices;
    private Dictionary<int, string>? _userRunwayHoldShorts;
    private Dictionary<int, string>? _userTaxiwayHoldShorts;

    // Paired-runway crossing double-Continue (VATSIM gap analysis 2026-08-31, P7):
    // set when a Continue past a crossing found ANOTHER runway-crossing hold-short
    // within PAIRED_CROSSING_MAX_M ahead; a second Continue press inside the window
    // clears that stop ("cross runways 28L and 28R at F" — one clearance, no stop
    // between the parallels). 400 m ≈ the FAA's 1,300 ft paired-runway threshold.
    // The window (Navigation.PairedCrossingHoldWindow) opens a delay after the first press, never at
    // once, so a key bounce or a quick double press cannot clear the second runway's stop.
    private int _pairedCrossingSegIdx = -1;
    private DateTime _pairedCrossingWindowFrom = DateTime.MinValue;
    private DateTime _pairedCrossingWindowUntil = DateTime.MinValue;
    private const double PAIRED_CROSSING_MAX_M = 400.0;

    // LAHSO — land and hold short (VATSIM gap analysis 2026-08-31, P5). Set by
    // BeginLandingRollout(NoGraph) from the landing exit planner, cleared at
    // StopGuidance. Milestone latches follow the four-reset-site invariant.
    private Navigation.LahsoHold? _lahsoHold;
    private bool _lahso1500Announced, _lahso500Announced, _lahsoStopAnnounced, _lahsoPassedAnnounced;
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
    // Taxi planner "Depart from named holding point" pick for the CURRENT route: the graph
    // node of the chosen PAINTED hold line (0 = no holding point chosen). Persisted across a
    // mid-taxi recalc for the same reason as _preferIlsHold — a recalc that dropped the pin
    // would quietly hand the pilot back the corridor they didn't choose. See
    // ApplyHoldingPointPin.
    private int _holdingPointHoldNodeId;
    // The picked point's LABEL ("A1"), spoken in the hold-short stop when the route's
    // stop node IS the picked node — "Stop. Hold short of Runway 26L at holding point
    // A1." — so the pilot hears the stop is the line they named. Empty otherwise.
    private string _holdingPointName = "";

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
    // Whether the landing-exit handoff actually ended up off the runway pavement.
    // Set by ResolveExitHandoffDestination from the vacate walk's final lateral
    // offset; read by the arrival callout, which must not tell a pilot to hold
    // position while they are still on the concrete. Defaults true so any path that
    // never reaches the handoff keeps the original wording.
    private bool _landingExitOffPavement = true;
    // Missed-vacate backstop state (landing-exit routes only). The closest the
    // aircraft has come to the exit's hold-clear node on the final segment, the
    // moment the "it's behind us" test started holding, and the latch that makes
    // HandleArrival say so instead of claiming a normal vacate.
    private double _landingExitMinDistToTargetM = double.MaxValue;
    private DateTime _missedVacateSince = DateTime.MinValue;
    private bool _landingExitMissed = false;
    // Set when the handoff concludes because the aircraft left the runway somewhere other
    // than the planned exit and no exit could be matched to where it actually went — or
    // the route that was built is not one the aircraft is on. HandleArrival renders a
    // distinct closure: this is the OPPOSITE failure to _landingExitMissed, which means
    // "you rolled past the vacate point", so the two must never share wording.
    private bool _landingExitVacatedEarly = false;
    // Captured immediately before the early-vacate swap in UpdateLandingRollout, on the
    // branch where a substitute exit was matched: the PLANNED exit's display name (the
    // same "Taxiway X" / "exit taxiway" format LoadRoute uses for _route.DestinationName).
    // _rolloutExit gets swapped to the substitute right after, so if that substitute's
    // handoff route is later refused as unreachable, HandleArrival's "_route.DestinationName"
    // would otherwise name the substitute the pilot is standing on, not the exit they were
    // steered toward and missed. Null on the no-match branch, where _rolloutExit is never
    // swapped and _route already names the planned exit correctly.
    private string? _landingExitVacatedEarlyPlannedName = null;
    // Set when the handoff concludes because the route that was built is not one the
    // aircraft is on, WITHOUT an early vacate having been established first. Distinct from
    // _landingExitVacatedEarly because that closure claims a POSITION — "short of X" — and
    // this path cannot support that claim: the guard also fires when the aircraft turned off
    // at or beyond the planned exit, where "short of" is simply false. Distinct from
    // _landingExitOffPavement too, whose "Off the runway at X" wording reads as a successful
    // vacate.
    private bool _landingExitRouteUnreachable = false;
    private DateTime _lastRecalculationTime = DateTime.MinValue;
    // The ReachabilityRefusalGate key for the most recently SPOKEN recalculation
    // reachability refusal (Navigation.ReachabilityRefusalGate), or null before any refusal
    // has been spoken for the current route. A reachability verdict is a standing property
    // of the airport data and the current destination, so without this latch the same
    // ~20-word AnnounceImmediate refusal repeated every RECALCULATION_COOLDOWN_SEC (15 s)
    // for as long as the aircraft stayed off-route, cutting off hold-short and
    // runway-crossing callouts each time. Reset on LoadRoute and StopGuidance so a new
    // route or a new destination can speak its own first refusal.
    private string? _lastReachabilityRefusalKey = null;
    private string _lastAnnouncedTaxiway = "";
    // A taxiway-change name deferred by TaxiwayChangeGate while the start-warning chatter
    // window (_startChatterSuppressUntil) was open, waiting to be spoken -- or discarded
    // silently -- once it closes. Null whenever nothing is waiting. Set only by
    // AnnounceOrDeferTaxiwayChange (the one shared helper both AdvanceToNearestSegment and
    // AdvanceSegment go through) and consumed only by FlushPendingTaxiwayAnnouncement, which
    // UpdatePosition calls on every Taxiing-state frame -- never while guidance is holding,
    // lining up, arrived, or otherwise off that state, because none of those states ever
    // reach that call again. SetState clears this the instant guidance LEAVES Taxiing (PR
    // #238 review, Important 1), so a name deferred just before a hold-short can no longer
    // sit waiting through the whole hold and then be flushed against a changed world once
    // Continue is pressed -- see SetState's own remarks. Also reset on LoadRoute and
    // StopGuidance so a stale deferred name from one route can never leak into (and be
    // spoken over) the next.
    private string? _pendingTaxiwayAnnouncement = null;
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
    // Defer the curve cue while the tone is asking for a turn the OTHER way at least this big
    // (VirtualPilot 2026-09-25: at a Normal exit's handoff "Turn left to come around." and
    // "Curving right." were spoken in the same frame — the curve is on the route AFTER the
    // turn the pilot has not made yet).
    private const double CURVE_ANNOUNCE_MAX_OPPOSING_ERROR_DEG = 25.0;
    // CheckRunwayIncursion ignores the route's start node while the aircraft is this close to it.
    private const double ROUTE_START_HOLD_IGNORE_M = 20.0;
    // The off-route runway warning needs the aircraft closing on the line (it says "approaching").
    private const double INCURSION_WARN_MIN_CLOSING_KTS = 2.0;
    private double _lastRouteHeadingErrorDeg;  // defer cue while yawing against it
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

    // Missed-vacate backstop — the case where NEITHER capture above can fire because
    // the aircraft never came near the hold-clear node (took the wrong branch at the
    // exit junction). Deliberately conservative: the aircraft must have got within
    // MISSED_VACATE_APPROACH_M of the node at some point (so this can only fire on a
    // vacate that was genuinely under way), must now be further off the segment than
    // the arrival cross gate allows, and must be either astern of the node or opening
    // the range on it by a clear margin, held for MISSED_VACATE_PERSISTENCE_SEC while
    // moving. A normal vacate passes within the 25 m radius and arrives long before
    // any of this is true.
    private const double MISSED_VACATE_ASTERN_M = 25.0;
    private const double MISSED_VACATE_OPENING_M = 50.0;
    private const double MISSED_VACATE_APPROACH_M = 150.0;
    private const double MISSED_VACATE_PERSISTENCE_SEC = 2.0;

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

    /// <summary>Standstill capture at a runway hold-short node: an aircraft stopped
    /// (≤ this ground speed) within <see cref="RUNWAY_HOLD_STANDSTILL_CAPTURE_M"/> of
    /// the route's final hold node counts as holding short even outside the 12 m
    /// arrival radius (corner-cut stop, LEBL 24L 2026-08-28).</summary>
    private const double RUNWAY_HOLD_STANDSTILL_MAX_KTS = 2.0;
    private const double RUNWAY_HOLD_STANDSTILL_CAPTURE_M = 30.0;

    // How far past the runway half-width the aircraft datum must be before a
    // landing-exit arrival ("Off the runway. Stop and hold position.") is allowed
    // to fire. Without this, an early/aggressive exit turn + re-route can curve the
    // route back toward the runway and the 25 m arrival fires while the aircraft is
    // still WITHIN the runway half-width — telling a blind pilot to STOP on an active
    // runway (OMDB 30L → K10 2026-06-14: "arrived" at ~20 m off a 30 m-half-width
    // runway; Where-Am-I correctly reported the aircraft was still on 12R/30L). While
    // still on the runway we suppress the arrival and keep guiding toward the
    // off-runway extension node, so the "stop" is spoken only once genuinely clear.
    // One source of truth — see Navigation/RolloutExitGate.RunwayClearMarginM, which the
    // lateral handoff trigger and the early-vacate guards also read through
    // IsLaterallyClearOfRunway.
    private const double RUNWAY_CLEAR_MARGIN_M = Navigation.RolloutExitGate.RunwayClearMarginM;

    private const double RECALCULATION_COOLDOWN_SEC = 15.0;
    // Steering-tone look-ahead: target = the point this many metres ahead
    // along the route polyline (continuous walk — see GuidanceGeometry).
    // Speed-scaled so the tone leads turns earlier at speed: 6 s of travel,
    // clamped. At 10 kt → 50 m (floor); 20 kt → 62 m; 30 kt+ → 93–120 m.
    // The 50 m floor matches the old fixed GUIDANCE_LOOK_AHEAD_M so slow-taxi
    // cross-track sensitivity is unchanged.
    private const double GUIDANCE_LOOK_AHEAD_SEC = 6.0;
    // Post-handoff cross-track pull on shallow exit segments (see the _rolloutHandoffActive
    // heading-error branch). crossM > 0 = aircraft RIGHT of the segment → steer left.
    private const double HANDOFF_XTRACK_DEG_PER_M = 1.0;
    private const double HANDOFF_XTRACK_MAX_DEG = 20.0;
    private const double HANDOFF_XTRACK_MAX_SEG_DEG = 30.0;
    // Post-handoff: this far to the side of the current leg, its bearing no longer says where the exit is.
    private const double HANDOFF_OFF_SEGMENT_M = 10.0;
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
    // The lead once a turn has swept past GuidanceGeometry.SHARP_TURN_RAMP_END_DEG
    // (IAircraftDefinition.TaxiSharpTurnLeadSeconds; equal to TurnLeadSeconds on most aircraft),
    // and the ceiling on the lead's contribution, degrees (TaxiTurnLeadMaxDegrees), both wired
    // beside TurnLeadSeconds. Same cross-thread note; the three have no coupled invariant: a frame
    // that reads one new and one old value only shapes the same bounded term differently once.
    public double SharpTurnLeadSeconds { get; set; } = 1.2;
    public double TurnLeadMaxDegrees { get; set; } = 30.0;
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
    // The thresholds and the wording now live in Navigation/RouteStartTurnCue
    // (SharpTurnDeg 100°, TurnaroundDeg 135° — the boundary between "come around"
    // and "turn hard onto the first taxiway"). They moved there so the cue has ONE
    // owner: it is composed at LoadRoute and delivered either inside the form's
    // single standstill utterance or by the one-shot below, and two composers would
    // be two wordings and two chances to disagree on left versus right. The local
    // INITIAL_TURN_CUE_DEG / INITIAL_TURN_UTURN_DEG consts were deleted rather than
    // left in place, so nobody tunes a number here and wonders why nothing changes.
    private bool _initialTurnCueAnnounced = false;
    // Set when the initial big-turn cue SPOKE; cleared once the aircraft points within
    // INITIAL_TURN_DONE_DEG of the route. While set, the junction/curve callouts wait: they
    // describe the route beyond a turn the pilot has not made, and each one is an
    // AnnounceImmediate that cut the turn-around cue off ("Taxiway A1 is behind you. Turn
    // left to come around." → "Left." → "Left now." in one frame; VirtualPilot 2026-09-25).
    private bool _initialTurnPending = false;
    // The side (+1 right, -1 left) the opening turn cue named; 0 when there is no cue.
    private int _initialTurnSign;
    private const double INITIAL_TURN_DONE_DEG = 45.0;
    // After a route-reach warning OR an unmapped-start warning (either "start warning"),
    // briefly hold the INFORMATIONAL taxiway-crossing, taxiway-change, curve and
    // final-destination-ahead callouts so they don't stomp that (longer, safety-critical)
    // warning at guidance start — 2026-06-13: "Crossing taxiway G" cut the reach warning
    // off mid-sentence, and the same collision reproduces for the unmapped-start warning
    // on the very first frame (Progressive Taxi cuts the warning itself; a normal
    // Calculate cuts the whole standstill utterance ~33 ms later). Hold-shorts,
    // runway-crossing callouts, lineup, speed warnings, and "Straighten." are NOT gated
    // by this.
    //
    // Sized from the measured System.Speech Rate-0 length (trailing silence trimmed) of the
    // LONGEST start utterance this window protects: a destination-not-connected warning
    // followed by the route-start turn cue it can be folded ahead of ("B 18R isn't connected
    // to the taxiway network you're on. The first 35 metres of the route aren't mapped. Make
    // a U-turn to the left onto taxiway B." — 10.41 s), plus about a fifth for margin ≈ 12.5 s.
    // Was 8.0 s, sized before the destination-off-network warning existed and too short for
    // this one — a route-reach or unmapped-start warning is safety-critical and must never be
    // the thing that gets cut. Does NOT cover a longer SayIntentions import summary spoken
    // ahead of the warning in the same utterance; that combination is not measured here.
    private const double START_WARNING_CHATTER_GRACE_SEC = 12.5;
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

    // Off-route (perpendicular cross-track) tolerance lives in Navigation.PavementTolerance.
    // Minimum negative along-track (behind the segment's start node) before the
    // "going backwards" off-route condition fires. GPS at taxi scale has <1 m
    // noise; 10 m gives a comfortable margin while catching deliberate rearward
    // movement. Requires the same GS + persistence gates as the lateral check.
    // Blind spot this closes: heading almost opposite to segment bearing keeps
    // cross-track near zero (PerpendicularDistance returns ~0), so the lateral
    // check never fires even as the pilot drives steadily away from the route.
    private const double OFF_ROUTE_BEHIND_START_M = 10.0;
    // Grace window after a segment advance during which off-route is suppressed.
    // First-turn false-trigger: in the middle of the turn arc the aircraft's
    // perpendicular distance to the just-completed *or* just-entered segment can
    // briefly exceed tolerance before the bearing settles. Without this grace
    // window the 3 s persistence timer accumulated across a slow turn and fired
    // a recalc mid-turn, which re-routed the pilot onto a random shortest path.
    private const double POST_TURN_OFFROUTE_GRACE_SEC = 4.0;

    // Proximity required before AdvanceToNearestSegment may treat "nearest of the next
    // six segments" as progress. Generous enough to cover a real skip (a 30 kt taxi
    // covers 15 m per 1 Hz sample, and navdata segments run 5-15 m, so a sparse update
    // can legitimately jump several), tight enough that an aircraft leaving the route
    // stops "advancing" along it. See AdvanceToNearestSegment for what happened without it.
    private const double SEGMENT_ADVANCE_MAX_DIST_M = 100.0;

    // Cross-track bound for the endpoint-tie pin breaker in AdvanceToNearestSegment
    // (GuidanceGeometry.HasPassedOntoNextSegment): the aircraft must be within this
    // of the NEXT segment's centerline to count as travelling along it. One full
    // wide-taxiway width (KLAS B is 98 ft ≈ 30 m) — tight enough that a parallel
    // taxiway (typically 60 m+ apart) can never satisfy it, loose enough to cover
    // the whole orbit the KLAS pilot flew (max ~20 m off centerline).
    private const double SEGMENT_PASS_ADVANCE_MAX_CROSS_M = 30.0;

    // How near the hold-short node the aircraft must be before a skipped hold-short is
    // ANNOUNCED as reached ("Stop. Hold short of X"). Beyond this the index is clamped to
    // the hold-short segment instead — never advanced past it, never announced early.
    private const double HOLD_SHORT_ANNOUNCE_MAX_DIST_M = 40.0;
    // Hairpin guard for AdvanceToNearestSegment (2026-09-24). Its "nearest" is measured to segment
    // ENDPOINTS, so on a long straight leg into a hairpin the far end of the leg AFTER the turn —
    // which runs back diagonally beside the current one — can read nearer than anything on the
    // current leg. Taxi2Gate LFPG, K → Z1 (a 129° right turn onto the 09L entry): the index jumped
    // onto Z1 with the aircraft still 124 m short of the junction, so the "sharp right onto Z1"
    // advance notice never fired (its junction had been skipped) and the tone swung hard right
    // across the grass, 22 m off the pavement. A segment beyond the current one's END junction is
    // therefore NOT progress while ALL of these hold: the aircraft is still TRACKING the current
    // segment (within ADVANCE_TRACKING_CROSS_M of its line) with more than
    // ADVANCE_GUARD_MIN_REMAINING_M still to go to its end; the turn at that junction is between
    // ADVANCE_MAX_HEADING_OFF_DEG and ADVANCE_GUARD_MAX_TURN_DEG; and the candidate points more than
    // ADVANCE_MAX_HEADING_OFF_DEG away from the aircraft's heading. Every gate was MEASURED
    // (VirtualPilot, 100 airports, 15,368 scenarios, occurrence-keyed — 2026-09-24):
    //  - remaining > 25 m (the capture radius) and > 60 m both broke hairpins the early advance was
    //    HELPING: KJFK 31L MB → M (161°) switched ~54 m out, which is what starts a tight turn in
    //    time; held to 25 m it went 38 m wide (62 taxi / 11 landing off-pavement findings appeared).
    //  - guarding only the NEXT segment let LFPG skip two segments instead of one — so every
    //    segment beyond the junction is covered.
    //  - no upper turn cap exposed LEBL K7 → K6 → K7: a 180° dead-end out-and-back across runway 02
    //    that the early advance short-cuts; flown as drawn, its return crossing has no hold of its
    //    own. Dead-end U-turns (≥ 150°) keep the old shortcut.
    // Result with all gates: 2 findings fixed (incl. LFPG), 0 new. Re-run VirtualPilot A/B before
    // touching any of them.
    private const double ADVANCE_TRACKING_CROSS_M = 30.0;
    private const double ADVANCE_GUARD_MIN_REMAINING_M = 90.0;
    private const double ADVANCE_MAX_HEADING_OFF_DEG = 90.0;
    private const double ADVANCE_GUARD_MAX_TURN_DEG = 150.0;

    // Never-joined escape for the off-route detector. _hasJoinedRoute exists so the taxi
    // from a gate onto the first cleared taxiway doesn't read as off-route before the
    // pilot has joined it — but it had no way out: a route the aircraft NEVER joins left
    // off-route detection disabled for that route's whole life. KBNA 2026-08-08: a gate
    // route was loaded while the aircraft was still rolling out on 02R having missed its
    // exit; it then drove 690 m the other way over 50 s at up to 31 kt with the tone
    // pinned ~170° behind, and neither a recalc nor a single word ever came.
    // A genuine join moves TOWARD the route, so the escape keys on the range OPENING:
    // never joined, currently further from the route than the closest approach by this
    // margin, and beyond the floor.
    //
    // Both numbers are deliberately well clear of normal ground manoeuvring. A pushback
    // runs 60-100 m and an apron repositioning can add more, all of it legitimately
    // AWAY from a route that starts at the stand — so an 80 m floor would have put a
    // recalc in the middle of a long pushback. The failures this exists for are not
    // marginal: KBNA was 690 m. 150 m still fires there within ~15 s at taxi speed.
    private const double NEVER_JOINED_OPENING_M = 100.0;
    private const double NEVER_JOINED_FLOOR_M = 150.0;
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
    // Segment index whose IsUncharted "entering uncharted apron" announcement has
    // fired (-1 = none). Also drives the one-shot "back on charted taxiways" when
    // the index advances past it. Reset on LoadRoute.
    private int _unchartedAnnouncedIdx = -1;
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
    // Closest the aircraft has come to the route line while it has yet to join it.
    // Feeds the never-joined escape in the off-route detector; MaxValue = no sample yet.
    private double _minPerpWhileUnjoinedM = double.MaxValue;
    // One-shot latch for the speed-scaled PREPARATORY exit-turn call (see
    // ROLLOUT_TURN_PREP_MAX_FT). Reset with the other rollout approach latches.
    private bool _rolloutTurnPrepAnnounced = false;
    // Hold-short nodes the incursion guard has already logged as "in range but not ahead"
    // (RunwayIncursionWatch.IsApproaching false) — one log line per node per route, never one
    // per frame: two hold lines at near-equal range can swap places as the nearest on every
    // position sample. Cleared with the warned-node latch, only by ResetIncursionNodeMemory.
    private readonly HashSet<int> _incursionWithheldLoggedNodes = new();
    // Per-frame scratch for the incursion guard, reused so the ~30 Hz guard allocates nothing.
    private readonly List<RunwayIncursionWatch.HoldShortCandidate> _incursionCandidates = new();
    // What the guard knows about each hold-short node that depends only on the map, built with
    // _cachedHoldShortNodes (same lifetime): the path segments leading into the node on its
    // TAXIWAY side (a chain that runs onto pavement is the runway side of the hold line), and
    // the runway the node guards — the one its spoken name comes from. A node the form inserts
    // after the cache was built (a projected holding point, SplitEdgeAt) is missing here exactly
    // as it is missing from _cachedHoldShortNodes; a split of an existing edge leaves every
    // cached segment's line where it was.
    private readonly Dictionary<int, IncursionNodeGeometry> _incursionNodeGeometry = new();
    private sealed record IncursionNodeGeometry(
        RunwayIncursionWatch.PathSegment[] PathsIntoNode, RunwayShape? GuardedRunway);
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
    // True while the incursion cooldown was started by an accepted recalculation ("Route changed …")
    // rather than by a warning of its own: only then is an off-route warning inside the window queued
    // instead of dropped (CheckRunwayIncursion).
    private bool _incursionCooldownFromRecalc;
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
    // (every _graph assignment site — LoadRoute's two branches,
    // BeginRunwayEndCountdownRollout, StopGuidance — installs a TaxiGraph reference or
    // null and never mutates the graph it replaces).
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
    // Runway lineup from a hold line set back from the runway: after Continue the tone first
    // follows the route's cut-off entry stub (TaxiRoute.RunwayEntryStub) onto the pavement, and only
    // then flies the centreline intercept. Straight from the hold, the intercept turned the pilot
    // onto the grass: KORD Y4 -> 04R, hold 95 m out, "Turn left" at the line, 137 degrees, and
    // "Lined up" 500 m down the runway from the intersection picked (VirtualPilot 2026-09-18).
    private bool _lineupStubActive;
    private RunwayShape? _lineupStubRunway;
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

    // FULL-LENGTH BACKTRACK DEPARTURE (opt-in, Taxi planner checkbox). When true,
    // the route ends at an intermediate runway entrance (_destinationNodeId) and
    // the lineup target (_lineupTargetLat/Lon/_lineupHeadingTrue) is the FULL-LENGTH
    // departure threshold. After the hold-short Continue, guidance enters
    // BacktrackDeparture and steers the reciprocal-heading backtrack to the
    // threshold, then hands to LiningUp for the turnaround. Set in LoadRoute,
    // reset in StopGuidance; persists across auto-recalcs (like _preferIlsHold).
    private bool _backtrackDeparture = false;
    private bool _backtrackDepApproachAnnounced = false; // "approaching runway end" one-shot
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
    // FindNearestNode of the lineup point) is the correct reachability probe for the
    // DESTINATION NODE: near the runway when reachable, far off when the lineup point
    // itself is stranded with no entrance near it (LPPT 20, now handled up front by
    // TaxiGraph.FindRunwayLineupEntryNode).
    //
    // NOT, however, for "the clearance ended on a parallel taxiway" — that comment used
    // to claim this probe covered the real PHNL 04L case and it does NOT. Measured
    // 2026-08-24, PHNL 04L's lineup point has a taxiway-F node 3.7 m away, 3.2 m off the
    // centerline, so its destination node is fine and this check cannot fire there. That
    // failure is a route that ENDED SHORT of the runway, which is what
    // RUNWAY_REACH_MAX_WALK_M below exists to catch.
    //
    // RUNWAY_REACH_MAX_CROSS_M is the perpendicular distance from the destination node to
    // the runway centerline beyond which the route clearly does not reach the runway.
    // 120 m sits safely above any LEGITIMATE runway-ENTRANCE node offset (an entrance sits
    // on the pavement).
    //
    // DO NOT justify this number by hold-short offsets, and do NOT move this probe back
    // onto a HOLD. The old rationale ("above the ~90 m ICAO Annex 14 max hold-short
    // offset") is stale: measured in this DB, LPPT 02's own full-length ILS hold is 151 m
    // off the centerline, its other IHSND holds reach 183 m, and EGKK A3 is 162 m — all
    // above 120 m. A hold-based measure at this threshold false-fires immediately, which
    // is exactly why the probe was moved here in 2026-06-16 — and moving it here is what
    // silently disarmed the PHNL 04L protection until the ended-short test was added.
    // internal (not private): TaxiAssistForm passes this same number to
    // TaxiGraph.FindRunwayLineupEntryNode, whose whole job is to find a real runway
    // entrance BEFORE this warning would fire. The two must be the same number or the
    // search and the check disagree about what "reaches the runway" means.
    internal const double RUNWAY_REACH_MAX_CROSS_M = 120.0;

    // SECOND half of the reach test — "the route ENDED SHORT of the runway".
    //
    // The cross-track probe above only sees the DESTINATION NODE, and TaxiRouter deliberately
    // ends a runway route on the LAST CLEARED TAXIWAY when that taxiway does not connect to the
    // destination (`lastTaxiwayTerminal`), which is what honours a cleared taxiway rather than
    // bypassing it (EIDW N2, LFPG R1). `_destinationNodeId` is never reassigned, so such a route
    // leaves the destination node sitting happily on the runway while the route itself stops
    // hundreds of metres away — the real PHNL 04L failure, where the clearance ended on a taxiway
    // paralleling 04L, guidance held ~456 m off behind a legitimate-looking "Hold short of Runway
    // 04L", and the lineup tone panned for four minutes. Moving the probe to the destination node
    // in 2026-06-16 (to stop the LPPT 02 false positive) took this protection with it.
    //
    // A route that ended short is judged by how far the aircraft would still have to TAXI to be
    // on the runway (TaxiGraph.GraphWalkToRunwayPavement) — NOT by perpendicular distance, which
    // cannot tell a set-back CAT II/III hold (EGKK A3, 162 m off, one short taxi away) from a
    // parallel taxiway with no connector at all. Measured against this DB: PHNL 04L's real holds
    // are 34-60 m of taxiing from the pavement while taxiway H is 655 m and P is 804 m; over the
    // whole fs2020 DB, 99.16 % of the 172,802 runway-owned hold nodes are within 400 m (p50 54 m,
    // p90 130 m). The remaining 1.85 % are almost all tiny GA strips with sparse networks, and
    // they are caught by the SEPARATE hold-name signal (RouteEndIsRunwayHold) rather than this
    // number — the two guards are independent, and the warning needs BOTH to fail.
    internal const double RUNWAY_REACH_MAX_WALK_M = 400.0;

    // Search bound for that walk. Comfortably past the threshold so the answer is never a
    // truncation artefact, and small enough that an end disconnected from the runway cannot walk
    // the whole airport. Route load and (rarely) an accepted off-route recalc — never per frame.
    //
    // Exceeding it yields PositiveInfinity, and that value is passed through to
    // RunwayReachGate UNCHANGED. It must never be mapped onto this constant to make it
    // "exceed the threshold": the number then reaches the pilot as a spoken distance, and
    // "about 1500 metres of taxiing away" is a confident, fabricated figure for a route that
    // has NO path at all. RunwayReachGate.DescribeFailure gives infinity its own wording.
    private const double RUNWAY_REACH_WALK_SEARCH_M = 1500.0;
    // During LiningUp, cross-track this far off the centerline (≈122 m) sustained for
    // LINEUP_UNREACHABLE_SEC without converging means the route never reached the runway —
    // fire the one-shot spoken bailout.
    //
    // Do NOT re-justify this by a "~90 m legitimate hold-short ceiling" (the rationale this
    // comment used to carry, and which RUNWAY_REACH_MAX_CROSS_M above retracts with
    // measurements): real hold lines reach 151-183 m in this DB. What makes 400 ft safe here
    // is the 12-second CONVERGENCE requirement, not the distance — a lineup that legitimately
    // starts far off the perpendicular is closing, and the timer resets as soon as it does.
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
    // The chosen exit's own path (junction → handoff destination), built lazily by
    // GetRolloutExitPath and keyed by the exit OBJECT, so a retarget rebuilds it. Both
    // missed-exit detectors consult it (LandingExitPathFollow) so an aircraft ON a
    // shallow exit is never called missed (YPPH 21 C9, 2026-09-18).
    private IReadOnlyList<(double Lat, double Lon)>? _rolloutExitPath;
    // Node ids of _rolloutExitPath (junction first), for the handoff start anchor.
    private IReadOnlyList<int>? _rolloutExitPathNodeIds;
    private Navigation.LandingExit? _rolloutExitPathFor;
    private double _rolloutExitOnAxisRunFt;
    // Touchdown is announced unconditionally inside BeginLandingRollout (a one-shot
    // entry event), so no per-rollout flag is needed for it. The three flags below
    // gate per-rollout-once distance callouts in UpdateLandingRollout.
    private bool _rolloutApproach1500Announced = false;
    private bool _rolloutApproach900Announced = false;
    private bool _rolloutApproach500Announced = false;
    private bool _rolloutTurnNowAnnounced = false;
    // One-shot-per-stop-episode latch for the "Stopped on the runway. Taxiway X
    // is N ahead." orientation call, spoken when a full stop happens too far
    // short of the chosen exit for the trulyStopped handoff (see
    // ROLLOUT_STOPPED_HANDOFF_MAX_DIST_FT). Re-armed once genuinely rolling
    // again so a second full stop gets its own call.
    private bool _rolloutStoppedShortAnnounced = false;
    // Set at the turn-now callout by ResolveTurnToneTarget (TaxiGuidanceManager.
    // Rollout.cs): the heading the Normal-exit tone steers toward through the turn
    // (0 = hold runway heading, 360 = due north), and whether the tone should
    // instead pause because no trustworthy target exists. The tone must actively
    // guide the commanded turn and must never pull against the verbal it just gave.
    private double _rolloutTurnToneTargetDeg = 0.0;
    private bool _rolloutTurnTonePause = false;
    // Ended-on-runway clearing phase (Arrived state): the route's data ran out with
    // the aircraft still on runway pavement, so the tone stays alive as a
    // heading-hold on the exit direction until laterally clear of every runway
    // corridor. See HandleArrival's landing-exit branch + UpdateArrivedRunwayClearing.
    private bool _arrivedRunwayClearing = false;
    private double _arrivedClearingBearingDeg = 0.0;
    private double _arrivedClearingStartLat = 0.0;
    private double _arrivedClearingStartLon = 0.0;
    // Last tone waveform/volume handed to _steeringTone.Start — HandleArrival's
    // clearing branch must re-Start the tone after the generic arrival Stop().
    private MSFSBlindAssist.Settings.HandFlyWaveType _lastToneWaveform = MSFSBlindAssist.Settings.HandFlyWaveType.Sine;
    private double _lastToneVolume = 0.05;
    // Set when "too fast to turn" was spoken at the turn point with no exit left ahead. That exit is then
    // not offered again: the tone holds the runway heading (SelectToneMode's tooFastForExit —
    // DriftCorrection, never ExitBearing or the turn-window Silent) and never steers toward its junction
    // or bearing, and the two SPEED-driven handoffs (speedNearExitHandoff and the High-speed early
    // handoff) stay closed for it, so slowing down as told never brings back "turn … taxiway X". The
    // handoffs that follow what the pilot DOES stay open (turnBegun, exitedLaterally, alignedWithExit,
    // trulyStopped): a pilot who turns onto it anyway, or stops short of it, is still guided. It keeps the
    // usual overshoot margin while the aircraft rolls, and a pilot who STOPS at or past it is moved on at
    // once (RolloutExitGate.IsPastExitForOvershoot) - to an exit the too-fast scan now finds reachable,
    // announced queued after the warning, or to the runway-end countdown - instead of being left silent.
    // Reset with the approach latches, so a later retarget restores normal behaviour.
    private bool _rolloutTooFastNoExit = false;
    // Set when such a declined exit is overshot with no exit left and the runway-end countdown begins
    // without "Missed last exit" (the pilot already heard "too fast to turn"). The countdown's first frame
    // then speaks its own status once ("Runway end in …"), QUEUED behind that warning rather than over it,
    // unless the countdown itself spoke on that frame, so a pilot still rolling mid-runway is not left in
    // silence until the 1,500 ft milestone.
    private bool _rolloutCountdownStatusOwed = false;
    // Which steering-tone behaviour the last rollout frame used. A change resets the
    // heading-error smoother so a DriftCorrection residual never leaks into the sharp
    // exit-bearing pan, and vice versa. Replaces the old _rolloutExitToneArmed latch,
    // which reset the smoother on exit-tone entry only — the drift tone needs the same
    // treatment in both directions.
    private Navigation.RolloutToneMode _rolloutToneMode = Navigation.RolloutExitGate.ToneModeAtRolloutEntry;
    // The exit whose turn window RolloutExitTurnWindowFeet last logged, so landing_exit.log records each
    // targeted exit's window once. Logging only: the window itself is computed where it is read.
    private Navigation.LandingExit? _rolloutTurnWindowLoggedExit;
    // Tone mode and targeted exit of the last per-frame "tone mode=" line in landing_exit.log (null = none
    // yet). A stopped aircraft gets a line only when one of them changes, so a pilot held on the runway
    // cannot flood the log. Reset in ResetRolloutApproachLatches.
    private Navigation.RolloutToneMode? _rolloutToneLogMode;
    private Navigation.LandingExit? _rolloutToneLogExit;
    // When the last "tone mode=" line was written (MinValue = none yet): a MOVING frame logs at most one
    // line per ROLLOUT_TONE_LOG_MIN_INTERVAL_MS, a mode or exit change always logs. Reset with the latches.
    private DateTime _rolloutToneLogUtc = DateTime.MinValue;
    private const double ROLLOUT_TONE_LOG_MIN_INTERVAL_MS = 100.0;
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
    // Exits RetargetLandingExit could not route to this rollout. The undershoot scan skips them, or it would
    // offer the same earlier exit again after every cooldown. Cleared at both rollout entries.
    private readonly HashSet<int> _rolloutUnroutableExitNodes = new();
    // Timestamp of the last handoff declined because the re-routed path re-crossed
    // the landing runway (RolloutRunwayReCrossing). DateTime.MinValue = no decline
    // yet this rollout. See ROLLOUT_CROSSING_RETRY_FLOOR_SEC for why this exists.
    private DateTime _rolloutCrossingDeclinedUtc = DateTime.MinValue;
    // One-shot latch for the spoken "keep rolling to the exit" instruction that a
    // runway-re-crossing decline speaks. The decline itself repeats at the retry floor
    // (~1 Hz); the SENTENCE must not. Reset alongside _rolloutCrossingDeclinedUtc at all
    // four rollout reset sites, PLUS a fifth: RetargetLandingExit re-arms it when the
    // targeted exit actually changes, so a decline latched for the abandoned exit can't
    // silently swallow the announcement for the new one (PR review, 2026-08-27). The
    // retry floor _rolloutCrossingDeclinedUtc is deliberately NOT given that fifth reset
    // — it only bounds CPU/log churn, so a stale floor costs under a second, while a
    // stale latch withholds information from the pilot. See
    // RolloutRunwayReCrossing.ComposeContinueToExit.
    private bool _rolloutCrossingDeclineAnnounced = false;
    // Runway-end countdown mode. Entered when the overshoot detector finds
    // no downfield exit remaining (or RetargetLandingExit's LoadRoute call
    // fails), or at touchdown from BeginRunwayEndCountdownRollout when a
    // landing-exit plan made for another runway finds no usable exit on the
    // runway actually landed on. Drives a distance-to-runway-end countdown
    // (1500 / 500 / 100 ft) so the blind pilot knows how much pavement is
    // left for braking instead of falling silent. State stays in
    // LandingRollout so UpdatePosition continues to feed the per-frame
    // loop. Ends by POSITION, never on any stop or turn alone
    // (Navigation.RunwayEndCountdownGate): "Runway vacated" once laterally
    // clear; backtracking when STOPPED within RolloutExitGate.NearRunwayEndFeet
    // (500 ft — never on a turn there, where a turn-off and a turnaround look
    // the same), or after turning around anywhere; one stopped notice for a
    // stop mid-runway.
    private bool _rolloutNoExitMode;
    private bool _rolloutEnd1500Announced;
    private bool _rolloutEnd500Announced;
    private bool _rolloutEnd100Announced;
    // One-shot "Stopped on runway X. Runway end in N." for a mid-runway stop during the countdown
    // (Navigation.RunwayEndCountdownGate). Reset at all four rollout latch sites, matching
    // _rolloutEnd*Announced: BeginLandingRollout, BeginLandingRolloutNoGraph,
    // EnterRunwayEndCountdown and StopGuidance.
    private bool _rolloutStoppedNoticeGiven;
    // Off-pavement alert for the landing roll and the exit (Navigation.OffPavementAlert). The map is
    // built lazily once per graph and dropped with it.
    private readonly Navigation.OffPavementAlert _offPavementAlert = new();
    private Navigation.PavementMap? _pavementMap;
    private TaxiGraph? _pavementMapGraph;
    private bool _offPavementLogged;
    // Backtrack state. Entered from runway-end countdown when the pilot has STOPPED within
    // RolloutExitGate.NearRunwayEndFeet of the end, or has turned around (150°+)
    // anywhere on the runway (Navigation.RunwayEndCountdownGate). Guides on the
    // reciprocal runway heading until the aircraft reaches the first taxi-graph
    // connection node.
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
    // One 30 kt for the rollout: aliases RolloutExitGate.TaxiGroundSpeedKts (value unchanged).
    private const double ROLLOUT_TAXI_GS_KTS = Navigation.RolloutExitGate.TaxiGroundSpeedKts;

    // Heading deviation from the runway centerline that signals "the
    // pilot has started the turn onto the exit". Once we see this, hand
    // over to normal taxi guidance even if speed is still high (a
    // high-speed exit at 60 kt is plausible on Code-E rapid exits).
    private const double ROLLOUT_TURN_BEGAN_HDG_DEG = Navigation.RolloutExitGate.TurnBegunHeadingDeg;
    // Above this GS a heading deviation is NOT treated as a deliberate exit
    // turn. Category E rapid-exit taxiways top out at ~90 kt; at higher speeds
    // the deviation is touchdown yaw, crosswind crab alignment, or sim physics
    // at wheel contact — not a real runway exit maneuver.
    private const double ROLLOUT_TURN_MAX_GS_KTS = Navigation.RolloutExitGate.TurnMaxGroundSpeedKts;

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
    private const double ROLLOUT_OVERSHOOT_FT = Navigation.RolloutExitGate.ExitOvershootFeet;

    // Overshoot margin for a HIGH-SPEED (rapid-exit) taxiway. A RET curves away
    // from the runway so gently (ICAO design radius >= 550 m) that at 100 ft
    // past the exit a CORRECT turn is still only ~3 deg of heading and ~3 ft
    // off the centerline — indistinguishable from rolling straight past. The
    // flat 100 ft margin therefore mistakes a correct RET turn for a miss and
    // retargets, cascading exit-to-exit down the runway. ~500 ft gives the turn
    // room to register before an overshoot can be declared.
    private const double ROLLOUT_HIGHSPEED_OVERSHOOT_FT = Navigation.RolloutExitGate.HighSpeedExitOvershootMaxFeet;

    // Cross-track-from-centerline gate for overshoot detection: a genuine
    // overshoot is past the exit AND still tracking the runway. Once the
    // aircraft has moved this far off the centerline it is curving onto the
    // exit, not missing it — not an overshoot regardless of distance past.
    private const double OVERSHOOT_ON_CENTERLINE_FT = Navigation.RolloutExitGate.OnCentrelineOvershootFeet;

    // If the aircraft is within this distance of an earlier exit while below
    // the undershoot speed threshold, retarget to minimise runway occupancy.
    // 1000 ft gives ~15 s at 40 kt to hear the callout and react.
    private const double ROLLOUT_UNDERSHOOT_RANGE_FT = 1000.0;
    // Outer speed threshold for the undershoot scan. Below this speed any
    // earlier shallow or high-speed exit is viable. These three turn-off values
    // are RolloutExitGate's, shared with the touchdown re-plan's comfortable lead.
    private const double ROLLOUT_UNDERSHOOT_ENTRY_GS_KTS = Navigation.RolloutExitGate.ShallowExitTurnOffSpeedKts;
    // Steep exits need the aircraft to be slower — the tighter turn demands
    // more braking margin. Only included in the undershoot scan below this speed.
    private const double ROLLOUT_UNDERSHOOT_STEEP_ANGLE_DEG = Navigation.RolloutExitGate.SteepExitAngleDeg;
    private const double ROLLOUT_UNDERSHOOT_STEEP_GS_KTS = Navigation.RolloutExitGate.SteepExitTurnOffSpeedKts;
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
    private const double ROLLOUT_UNDERSHOOT_MIN_LEAD_FT = Navigation.RolloutExitGate.ExitLeadMinFeet;
    private const double ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT = Navigation.RolloutExitGate.ExitLeadFeetPerKnot;

    // Maximum distance from the chosen exit at which a full stop (< 3 kt) hands
    // the rollout off to a taxi-network route (the trulyStopped handoff). The
    // handoff exists for "braked hard a few hundred feet short of the exit" —
    // there a graph route to the exit is a short forward hop. Stopped FAR short,
    // the graph route is actively wrong: the runway pavement has no along-runway
    // edges, so A* must leave the runway through whatever connector is nearest.
    // EDDB 24L live 2026-08-30: a full stop 1,108 m down the runway with M2
    // chosen 3,912 m down produced a 3.2 km re-route BACKWARD off the 130° M7
    // diagonal onto the parallel — steered with full confidence. Beyond this
    // distance the manager now HOLDS LandingRollout (see the stopped-short
    // block in UpdateLandingRollout): the pilot continues ahead to the chosen
    // exit with the whole normal flow still armed, and the undershoot scan can
    // still offer the next forward exit as it comes into range. 1000 ft matches
    // ROLLOUT_UNDERSHOOT_RANGE_FT — inside it a forward graph route is short
    // and unambiguous; beyond it the undershoot machinery is the right tool.
    private const double ROLLOUT_STOPPED_HANDOFF_MAX_DIST_FT = 1000.0;
    // Speed above which a stop episode is considered over — re-arms the
    // stopped-short orientation call for a subsequent full stop.
    private const double ROLLOUT_STOPPED_SHORT_REARM_GS_KTS = 10.0;

    // Minimum gap before the handoff block is re-entered after a handoff was DECLINED
    // for re-crossing the landing runway (see the RolloutRunwayReCrossing guard in
    // UpdateLandingRollout). Every other exit from that block either moves the state
    // out of LandingRollout or stops guidance, so the block is one-shot today and needs
    // no latch; the crossing decline is the first path that stays in LandingRollout and
    // returns, which makes it re-entrant on the very next position frame.
    //
    // That matters because the block runs at SIM_FRAME rate (StartTaxiGuidanceMonitoring
    // requests SIMCONNECT_PERIOD.SIM_FRAME) on the UI thread, and it performs an
    // unconditional LoadRoute — a full-graph A* — before the guard is reached. Without a
    // floor an aircraft stopped short of the exit would run that A* 30-60 times a second
    // forever, against the hot-path perf invariants, and the decline would churn
    // landing_exit.log through its 5 MB x 3 rotation — evicting the evidence for exactly
    // the incident class this guard exists to catch. The write volume per decline is
    // higher than the two unthrottled RolloutDiag lines it looks like: the decline's
    // SetState(LandingRollout) completes a RouteLoaded -> LandingRollout round trip, and
    // each SetState both writes its own diag line and raises StateChanged, which MainForm.
    // OnTaxiGuidanceStateChanged also logs. That is roughly 8 lines per decline, not 1-2 —
    // another reason the floor must not shrink. The two triggers that persist
    // frame-to-frame (speedNearExitHandoff, trulyStopped) both require !pastExit, which IS
    // the decline condition, so the loop is guaranteed in precisely the case the decline
    // branch is for.
    //
    // A FLOOR, deliberately, not a per-exit latch: a latch would also block a legitimate
    // turnBegun handoff if the pilot does turn onto the exit, and the route recomputed
    // from that new position would very likely no longer cross. One second bounds the cost
    // to ~1 A* per second while still re-evaluating often enough that a real turn is picked
    // up promptly and the conclude branch fires as soon as the aircraft reaches the exit.
    // The delay costs the pilot nothing audible: the rollout stays in LandingRollout, where
    // the tone remains rollout-driven, and the ~59 frames a second that the floor suppresses
    // are exactly the frames on which that tone is updated.
    private const double ROLLOUT_CROSSING_RETRY_FLOOR_SEC = 1.0;

    // How far ahead the crossing-decline utterance looks when deciding which rollout callouts
    // it supersedes. A TIME, converted to distance at the aircraft's own ground speed by
    // Navigation.RolloutRunwayReCrossing.DeclineSupersedesCallout: it must cover the utterance
    // itself, so that a callout the aircraft is about to reach is folded in rather than left
    // to fire an AnnounceImmediate over a one-shot sentence that can never be re-spoken.
    // Four seconds comfortably spans the longest form ("Continue rolling to taxiway Alpha,
    // 1500 feet ahead. Slow down."). Sizing it is low-stakes in both directions — too short
    // returns a narrow band to the pre-fix behaviour, too long retires a coarse distance
    // restatement slightly early — and it is NOT the kind of speech-duration estimate
    // the liftoff-handoff rule [VG-1] forbids: nothing here mutes speech.
    private const double ROLLOUT_DECLINE_CALLOUT_LEAD_SEC = 4.0;

    // Lead window for the touchdown sentence that carries a landing-exit runway correction
    // ("Touchdown on runway 30L, not 12L. High-speed exit taxiway M12A in 4,000 feet."): a rollout
    // milestone the aircraft reaches inside it is retired and folded into that sentence
    // (Navigation.TouchdownCallout), so its AnnounceImmediate cannot cut the correction off.
    // MEASURED, not estimated: that sentence renders in 7.51 s through System.Speech at Rate 0 (the
    // rate ScreenReaderAnnouncer's SAPI fallback uses; voice Microsoft David Desktop, trailing
    // silence trimmed), plus about a fifth. Not a speech mute.
    private const double ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC = 9.0;

    // Distance from the chosen exit at which the rollout speaks "turn now". Not a
    // DistanceMilestones entry — it is the turn-now handoff boundary, and the 500 ft approach
    // callout's lower bound is deliberately the same number so the two never overlap. It is also
    // the floor on every exit's turn window (RolloutExitGate.TurnNowFeet, which this aliases).
    private const double ROLLOUT_TURN_NOW_FT = Navigation.RolloutExitGate.TurnNowFeet;
    // The LEAD, by contrast, genuinely is speed-dependent, so a SEPARATE preparatory call fires
    // first on the 11 ft/kt law. Capped so it can never land on the 500 ft milestone, and
    // skipped entirely when it would fall within MIN_GAP of the turn point itself. Arrive fast
    // and you are warned earlier, never pulled earlier.
    private const double ROLLOUT_TURN_PREP_MAX_FT = 400.0;
    private const double ROLLOUT_TURN_PREP_MIN_GAP_FT = 50.0;
    // Below this the spoken exit turn DIRECTION is dropped entirely ("Turn now, taxiway X")
    // rather than guessed. Where a scenery models the junction ON the centreline, the old
    // aircraft-to-junction bearing had its sign decided by the aircraft's own tracking error —
    // 5 of KDTW 22L's 13 junctions sit within a metre of the centreline, so a LEFT exit
    // announced "Turn right now" for anyone tracking a metre left of centre. A confident wrong
    // side is far worse for a blind pilot than no side.
    private const double EXIT_TURN_DIRECTION_MIN_DEG = 10.0;

    // Distance from the chosen exit at which the rollout tone snaps from
    // runway-heading guidance (centreline tracking) to exit-bearing guidance.
    // Chosen to give the pilot ~2 s of directional preview before the
    // "turn now" speech fires at 150 ft — unambiguous "this direction" cue
    // without the premature-action risk of a long gradual build-up.
    private const double ROLLOUT_EXIT_TONE_ARM_FT = Navigation.RolloutExitGate.ExitToneArmFeet;

    // Explicit thresholds used in the exit-guidance zone (dist ≤ ARM_FT).
    // Tighter than the width-scaled runway values (4.2° activation on a wide
    // runway) so the tone gives fine steering on shallow exits (3–5°) where
    // the heading correction is tiny. Silent at 1.5° covers GPS jitter;
    // activation at 2.5° catches any exit ≥ 3° while keeping truly
    // straight-ahead exits (< 2.5°) silent. Max pan matches runway lineup.
    private const double ROLLOUT_EXIT_TONE_SILENT_DEG = 1.5;
    private const double ROLLOUT_EXIT_TONE_ACTIVATION_DEG = 2.5;
    private const double ROLLOUT_EXIT_TONE_MAX_PAN_DEG = 15.0;

    // Drift-correction tone thresholds — the rollout phase that used to be silent.
    // See Navigation/RolloutExitGate for where 2.0 comes from (it is the codebase's
    // existing floor for a meaningful heading deviation) and why the max pan matches
    // every other steering tone rather than inventing a wider one.
    private const double ROLLOUT_DRIFT_TONE_SILENT_DEG = Navigation.RolloutExitGate.DriftToneSilentDeg;
    private const double ROLLOUT_DRIFT_TONE_ACTIVATION_DEG = Navigation.RolloutExitGate.DriftToneActivationDeg;
    private const double ROLLOUT_DRIFT_TONE_MAX_PAN_DEG = Navigation.RolloutExitGate.DriftToneMaxPanDeg;

    // Ground speed below which the steering tone activates during rollout.
    // Above this speed the tone is silent — at 80+ kt a pan cue is useless
    // because the pilot can't react and a high-speed exit is still many
    // seconds away. Below this (roughly approach-speed walk-down final
    // phase), the tone behaves like taxi-assist: centred = silent, deviated
    // = pans toward the exit so the pilot steers to keep it centred and
    // naturally turns onto the exit. 50 kt gives ~10 s more lead time than
    // 40 kt at typical deceleration — meaningful for high-speed RETs (40–60 kt).
    private const double ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS = Navigation.RolloutExitGate.ToneActiveBelowGroundSpeedKts;

    // Below this ground speed, runway-end countdown mode considers the
    // aircraft effectively stopped and hands off to plain Taxiing. Lower
    // than ROLLOUT_TAXI_GS_KTS (30) because the pilot in this mode is
    // braking hard with no further callouts coming; once they're at a
    // crawl the countdown has nothing more useful to say.
    private const double ROLLOUT_NO_EXIT_STOPPED_GS_KTS = Navigation.RolloutExitGate.NoExitStoppedGroundSpeedKts;

    // Backtrack guidance thresholds — owned by Navigation.BacktrackConnectionHandoff, which also
    // owns the approach sentence, the node-bearing tone and the hand-off decision.
    // Within ANNOUNCE distance: the "Taxiway H ahead on the left. Vacate runway." callout and
    // the tone's swing onto the node. Within HANDOFF distance: hands off regardless of pavement.
    private const double BACKTRACK_TAXI_ANNOUNCE_M = Navigation.BacktrackConnectionHandoff.AnnounceMetres;
    private const double BACKTRACK_HANDOFF_M        = Navigation.BacktrackConnectionHandoff.HandoffMetres;

    // Full-length backtrack DEPARTURE thresholds (distance to the departure
    // threshold / full-length lineup point).
    // APPROACH: one-shot "approaching runway end, prepare to turn around" callout.
    // HANDOFF:  switch from the reciprocal-heading backtrack steer to LiningUp,
    //           which then pans the 180° turnaround and lines up full length.
    private const double BACKTRACK_DEP_APPROACH_M = 150.0;
    private const double BACKTRACK_DEP_HANDOFF_M  = 40.0;

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
    /// True once the landing-exit handoff has completed and the exit route's own
    /// steering tone is panning the pilot onto the taxiway. Read by the manual-landing
    /// assist as a backstop: it normally hands over on the state change itself
    /// (LandingFlareAssistManager.StepTaxiHandover, forwarded from StateChanged), and this
    /// catches an assist that re-enters rollout while taxi guidance already steers — the
    /// handoff can fire as high as 90 kt (turnBegun) or at any speed at all
    /// (exitedLaterally), so a speed threshold alone cannot keep the two apart.
    /// </summary>
    public bool IsLandingExitTaxiSteering
    {
        get
        {
            lock (_stateLock)
            {
                return _state == TaxiGuidanceState.Taxiing && _isLandingExitRoute;
            }
        }
    }

    /// <summary>
    /// True while the landing roll's off-pavement alert has told the pilot about the current excursion
    /// (<see cref="Navigation.OffPavementAlert.HasSpokenThisExcursion"/>); false once it re-arms and after
    /// guidance ends. The surface callout reads it so one excursion never gets two phrasings.
    /// </summary>
    public bool OffPavementAnnounced
    {
        get
        {
            lock (_stateLock)
            {
                return _offPavementAlert.HasSpokenThisExcursion;
            }
        }
    }

    /// <summary>
    /// True while landing-exit guidance runs: the rollout, runway-end countdown included, or taxi steering on
    /// the landing-exit route (<see cref="LandingExitGoAround.Arms"/>). MainForm arms the go-around check on a
    /// liftoff in this state.
    /// </summary>
    public bool IsLandingExitGuidance
    {
        get
        {
            lock (_stateLock)
            {
                return LandingExitGoAround.Arms(_state, _isLandingExitRoute);
            }
        }
    }

    /// <summary>
    /// Ends landing-exit guidance after a go-around or touch-and-go: when the fresh sample says the aircraft is
    /// still airborne and landing-exit guidance still runs (<see cref="LandingExitGoAround.Ends"/>), stops it
    /// as <see cref="StopGuidance"/> does - silently, the tone off, and the position stream with it through the
    /// Inactive state change. True when it ended something.
    /// </summary>
    public bool EndLandingExitGuidanceIfGoAround(bool freshSampleOnGround)
    {
        lock (_stateLock)
        {
            if (!LandingExitGoAround.Ends(freshSampleOnGround, _state, _isLandingExitRoute)) return false;
            RolloutDiag($"GO-AROUND: still airborne {LandingExitGoAround.ConfirmMs} ms after liftoff in {_state}; landing-exit guidance ended");
            StopGuidance();   // re-enters _stateLock on this thread
            return true;
        }
    }

    /// <summary>
    /// Everything the landing assist's rollout cue reads from taxi guidance on a frame, taken under ONE lock:
    /// LandingFlareAssistManager reads it once per rollout frame, at 30-60 Hz, and every acquisition can wait
    /// behind a retarget's LoadRoute on the position thread.
    /// </summary>
    public RolloutCueState RolloutCueSnapshot
    {
        get
        {
            lock (_stateLock)
            {
                return new RolloutCueState(_state == TaxiGuidanceState.LandingRollout, _rolloutNoExitMode,
                    _rolloutToneMode, _state == TaxiGuidanceState.Taxiing && _isLandingExitRoute);
            }
        }
    }

    /// <summary>
    /// What the landing assist's rollout cue reads from taxi guidance on one frame (<see cref="RolloutCueSnapshot"/>):
    /// whether a landing rollout runs (steering at an exit or the runway-end countdown: the cue then has no speed
    /// end, LandingFlareAssistManager.DecideRolloutEnd), whether it is the countdown, exit guidance's last tone
    /// mode, and whether the landing-exit route already steers (<see cref="IsLandingExitTaxiSteering"/>).
    /// </summary>
    public readonly record struct RolloutCueState(bool InLandingRollout, bool NoExitMode,
        Navigation.RolloutToneMode ToneMode, bool LandingExitTaxiSteering)
    {
        /// <summary>A landing rollout steering at an exit, not the runway-end countdown.</summary>
        public bool InExitRollout => InLandingRollout && !NoExitMode;

        /// <summary>
        /// True while exit guidance owns the rollout tone (Navigation.RolloutExitGate.ExitGuidanceOwnsTone): a landing
        /// rollout with an exit whose last tone mode is not Centerline. The cue stays engaged but silent while this
        /// is true, so the two tones never sound together.
        /// </summary>
        public bool ExitGuidanceOwnsRolloutTone
            => Navigation.RolloutExitGate.ExitGuidanceOwnsTone(InLandingRollout, NoExitMode, ToneMode);
    }

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

    /// <summary>
    /// The route-start turn cue for the route just built, or null when the first leg needs
    /// no words. Composed ONCE by LoadRoute — the same pattern LastRouteReachWarning uses,
    /// and for the same reason: an AnnounceImmediate from inside LoadRoute is stomped by
    /// StartGuidance's own first-taxiway callout milliseconds later.
    /// <para>
    /// The form folds it into its single standstill utterance and calls
    /// <see cref="ConsumeInitialTurnCue"/>. Whatever is left unconsumed is spoken by the
    /// per-frame one-shot instead, so routes the form did not start (landing-exit handoffs,
    /// announceSummary:false) keep the cue. One text, two delivery paths, never both.
    /// </para>
    /// </summary>
    public string? LastRouteInitialTurnCue { get; private set; }

    /// <summary>
    /// Takes the cue and clears it, so the per-frame one-shot will not repeat it.
    /// </summary>
    public string? ConsumeInitialTurnCue()
    {
        string? cue = LastRouteInitialTurnCue;
        LastRouteInitialTurnCue = null;
        return cue;
    }

    /// <summary>
    /// The unmapped-first-leg warning for the route just built, or null. Composed ONCE by LoadRoute
    /// when RouteReachability finds the route starting with a straight unmapped leg: either the
    /// aircraft is leaving a disconnected piece of network ("Your position isn't connected to the
    /// taxiway network…") or the destination is on a piece of network the aircraft is not on ("{name}
    /// isn't connected to the taxiway network you're on…"). Delivered exactly like
    /// <see cref="LastRouteInitialTurnCue"/>: folded into the form's standstill utterance, or spoken by
    /// the first-taxiing-frame one-shot together with the turn cue. Never both.
    /// </summary>
    public string? LastRouteUnmappedStartWarning { get; private set; }

    /// <summary>Takes the warning and clears it, so neither delivery path can repeat it.</summary>
    public string? ConsumeUnmappedStartWarning()
    {
        string? warning = LastRouteUnmappedStartWarning;
        LastRouteUnmappedStartWarning = null;
        return warning;
    }

    /// <summary>
    /// The start-hold sentence ("Stop. Hold short of runway 12R. Press continue when cleared.") for a
    /// route that begins at a runway hold line (<see cref="TaxiRoute.StartHoldRunway"/>), set when
    /// guidance enters that hold, and spoken exactly once. Every caller of <see cref="StartGuidance"/>
    /// that leaves guidance in Taxiing or HoldShort must consume it (<see cref="ConsumeStartHoldCue"/>)
    /// and speak it: MainForm feeds no position frames while guidance holds, so no later frame will.
    /// <c>TaxiAssistForm</c> folds it LAST into its standstill utterance on Calculate and speaks it as
    /// a Progressive leg's opening instruction. <c>LandingExitPlanner</c> starts guidance and enters
    /// the landing rollout at once, where the clear margin refuses a start hold. A landing-exit
    /// re-route, which no caller starts, enters the hold in <see cref="UpdatePosition"/> and is spoken
    /// in that same frame.
    /// </summary>
    public string? LastRouteStartHoldCue { get; private set; }

    /// <summary>Takes the start-hold sentence and clears it, so it is spoken only once.</summary>
    public string? ConsumeStartHoldCue()
    {
        lock (_stateLock)
        {
            string? cue = LastRouteStartHoldCue;
            LastRouteStartHoldCue = null;
            return cue;
        }
    }

    /// <summary>
    /// Warning text when one or more of the form's explicit "hold short of runway X" picks could
    /// not be matched to the built route. Cleared on every LoadRoute, set or not, so it can never
    /// describe a previous route.
    /// </summary>
    public string? LastRouteHoldShortWarning { get; private set; }

    /// <summary>
    /// The spoken warnings a route carries that must be HEARD at Calculate — the route cannot be joined from
    /// where the aircraft is, a runway on it has no hold short point, an explicit hold-short pick could not be
    /// set — or null. They are also in <see cref="LastRouteSummary"/>, but the spoken summary is interrupted
    /// by StartGuidance's first callout (the reach warning's 2026-06-13 lesson), so the caller speaks this in
    /// its one standstill utterance after StartGuidance, exactly like <see cref="LastRouteReachWarning"/>.
    /// </summary>
    public string? LastRouteStartWarning { get; private set; }

    public TaxiRoute? CurrentRoute => _route;
    public TaxiGraph? CurrentGraph => _graph;
    public int CurrentSegmentIndex => _currentSegmentIndex;

    /// <summary>
    /// True while a route is actually being flown (see <see cref="LiveRouteStates"/>) --
    /// the question <c>TaxiAssistForm.ShowRouteFailure</c>'s <c>keepSummary</c> decision
    /// needs, and which <see cref="CurrentRoute"/> cannot answer on its own: <c>HandleArrival</c>
    /// sets <see cref="State"/> to <see cref="TaxiGuidanceState.Arrived"/> without ever
    /// nulling <c>_route</c>, so <c>CurrentRoute != null</c> stayed true for the rest of the
    /// session after a completed, docking-off arrival with no Stop pressed -- wrongly
    /// protecting the NEXT leg's failed-Calculate summary with a route that was no longer
    /// live (PR #238 review, Important 3).
    /// </summary>
    public bool HasLiveRoute => LiveRouteStates.IsRouteLive(_state);

    // "Where Am I" graph cache — used when guidance is inactive so we don't rebuild
    // the graph on every hotkey press. Keyed by ICAO AND by the gate-list source token, and
    // invalidated on aircraft/airport change.
    //
    // The token half exists because stand names are frozen into the graph's nodes at build
    // time. Keyed on ICAO alone, a Where-Am-I pressed before GSX published handlerData cached
    // navdata's BGL parking-enum letters for good, so once GSX published, Where-Am-I said
    // "Gate A 25" about the stand the taxi dialog and the TCAS label called "Gate B 25" — the
    // one-stand-two-names defect Services/ParkingSpotSource exists to remove.
    private TaxiGraph? _whereAmICachedGraph;
    private string _whereAmICachedIcao = "";
    private string _whereAmICachedToken = "";

    // Moved (under _stateLock) by every invalidation of that cache — OnAirportDataUpdated and
    // ClearWhereAmICache. DescribeCurrentLocation builds OUTSIDE the lock and publishes only if
    // this has not moved meanwhile, so a taxiway-name fetch landing mid-build is never undone by
    // a graph carrying the older names.
    private long _whereAmICacheEpoch;

    /// <summary>
    /// Which database the runway geometry came from, as a number: moved by every database switch
    /// (<see cref="ClearWhereAmICache"/>). Anything read through a provider is stored only while
    /// the generation read WITH that provider is still current (<see cref="StoreWhereAmIGraph"/>,
    /// <see cref="RunwayShapeSource.Publish"/>), so a lookup in flight across a switch never
    /// caches the old database's geometry.
    /// </summary>
    public long DatabaseGeneration => Interlocked.Read(ref _databaseGeneration);
    private long _databaseGeneration;

    /// <summary>
    /// The gate-list source token for an ICAO — <see cref="GateDataSource.GetGateListVersion"/>
    /// at the production call site, wired by <c>MainForm</c> beside
    /// <see cref="ParkingSpotSupplier"/>. Must be O(1) and must never throw; a failure degrades
    /// to "none".
    ///
    /// <para>
    /// Null is the supported default and keeps every caller that does not wire it — the xUnit
    /// suite, anything built outside MainForm — byte-identical to the pre-token behaviour: the
    /// token is then the constant "none", can never differ, and the cache falls back to its
    /// original ICAO-only invalidation. Same backward-compatibility shape as
    /// <see cref="ParkingSpotSupplier"/> itself.
    /// </para>
    /// </summary>
    public Func<string, string>? ParkingSpotVersionSupplier { get; set; }

    /// <summary>
    /// The gate-list source token for <paramref name="icao"/>, or "none" when no supplier is
    /// wired. Never throws — a supplier failure degrades to "none", which simply means the
    /// cache keeps its pre-token ICAO-only behaviour rather than losing the graph.
    /// </summary>
    private string ResolveParkingSpotVersion(string icao)
    {
        var supplier = ParkingSpotVersionSupplier;
        if (supplier == null) return "none";
        try { return supplier(icao) ?? "none"; }
        catch (Exception ex)
        {
            Log.Debug("Taxi", $"parking list: version read failed for {icao}: {ex.Message}");
            return "none";
        }
    }

    /// <summary>
    /// Describes the aircraft's current location for the "Where Am I" hotkey.
    /// Returns a spoken string like "Taxiway Bravo at KJFK", "Gate A25 at KJFK", or
    /// "Runway 22L at KJFK". If the aircraft isn't near any known airport or the
    /// location can't be resolved, returns a user-facing diagnostic.
    ///
    /// Reuses the active guidance graph if it matches the airport; otherwise builds and
    /// caches a graph for ad-hoc queries. Does NOT mutate any guidance state.
    /// </summary>
    /// <param name="databaseGeneration"><see cref="DatabaseGeneration"/> read with
    /// <paramref name="dataProvider"/>, in the same turn. Required: Alt+L captures its provider at
    /// the press and calls this from a pool thread.</param>
    public string DescribeCurrentLocation(
        IAirportDataProvider dataProvider,
        string icao,
        double lat,
        double lon,
        long databaseGeneration)
    {
        if (string.IsNullOrWhiteSpace(icao))
            return "No airport nearby.";

        TaxiGraph? graph;
        string token;
        long cacheEpoch;

        // _stateLock serializes the cache read and the cache write against the background-thread
        // OnAirportDataUpdated (which nulls the cache) and the locked GetStatusAnnouncement
        // overload, so the (graph, icao, token) triple is never torn. The BUILD runs between the
        // two, outside the lock: Alt+L calls this from a pool thread, and a cold build (database
        // reads plus TaxiGraph.Build, up to seconds at a hub) held under the lock froze every
        // UI-thread caller of it — the passing-callout monitor's runway probe, taxi position
        // updates. DescribeLocation runs OUTSIDE the lock on the local graph reference. That graph
        // can be the active one, which the taxi form subdivides on the UI thread; TaxiGraph
        // serialises that itself (_structureLock), taken only after _stateLock is released, so
        // the two never nest.
        lock (_stateLock)
        {
            // ShouldRebuildGateList, not a plain compare: it rebuilds on an upgrade or a
            // refresh and never on the DOWNGRADE a transient GSX drop causes, so a good graph
            // survives a reconnect flap. Same treatment the two sibling caches give this token.
            token = ResolveParkingSpotVersion(icao);
            cacheEpoch = _whereAmICacheEpoch;

            // Prefer the active guidance graph if it's for this airport
            if (_graph != null && string.Equals(_icao, icao, StringComparison.OrdinalIgnoreCase))
                graph = _graph;
            else if (_whereAmICachedGraph != null &&
                     string.Equals(_whereAmICachedIcao, icao, StringComparison.OrdinalIgnoreCase) &&
                     !GateDataSource.ShouldRebuildGateList(_whereAmICachedToken, token))
                graph = _whereAmICachedGraph;
            else
                graph = null;
        }

        if (graph == null)
        {
            try
            {
                var paths = dataProvider.GetTaxiPaths(icao);
                if (paths == null || paths.Count == 0)
                    return $"No taxi data available for {icao}.";

                var parking = ResolveParkingSpots(dataProvider, icao);
                var runwayStarts = dataProvider.GetRunwayStarts(icao) ?? new List<StartPosition>();

                // Runways let the builder repair laterally-bogus start rows before
                // they reach the centerlines this very call is about to query
                // (TaxiGraph.SnapStartToRunwayCenterline).
                graph = TaxiGraph.Build(paths, parking, runwayStarts,
                                        dataProvider.GetRunways(icao));
            }
            catch (Exception ex)
            {
                return $"Could not load airport data for {icao}. {ex.Message}";
            }

            lock (_stateLock)
            {
                // The token stored is the one read BEFORE the build, so a GSX publish landing
                // mid-build still reads as a token move on the next press and rebuilds.
                if (cacheEpoch == _whereAmICacheEpoch)
                    StoreWhereAmIGraph(graph, icao, token, databaseGeneration);
                else
                    Log.Debug("Taxi", $"Where-Am-I graph for {icao} answered, not cached: the cache was invalidated while it was built");
            }
        }

        string description = graph.DescribeLocation(lat, lon);
        if (string.IsNullOrEmpty(description))
            return $"Not on a known taxiway or ramp at {icao}.";

        return $"{description} at {icao}.";
    }

    /// <summary>
    /// Caches a Where-Am-I graph built under generation <paramref name="readUnder"/> — unless a
    /// database switch has happened since, in which case the caller still answers from it but it
    /// is not kept. Call under _stateLock.
    /// </summary>
    private void StoreWhereAmIGraph(TaxiGraph graph, string icao, string token, long readUnder)
    {
        if (!RunwayShapeSource.MayStore(readUnder, DatabaseGeneration))
        {
            Log.Debug("Taxi", $"Where-Am-I graph for {icao} answered, not cached: the database changed while it was built");
            return;
        }
        _whereAmICachedGraph = graph;
        _whereAmICachedIcao = icao;
        _whereAmICachedToken = token;
    }

    // The runway shapes last memoised for one airport (RunwayShapeMemo). Keyed by airport so it
    // OUTLIVES the Where-Am-I graph, which the taxiway-name fetch nulls seconds after it is built;
    // runway pavement does not depend on taxiway names. Built once per graph instance, never per poll.
    private RunwayShapeMemo? _runwayShapeMemo;
    // The airport the probe was last asked about; a warm-up publishes only for it, so a late one
    // for the previous airport cannot evict this one's memo. Under _stateLock.
    private string _runwayProbeIcao = "";

    /// <summary>
    /// Is this point on any runway's pavement at <paramref name="icao"/>? Answers only from geometry
    /// already in hand — the active graph (if installed under the current generation), else the
    /// Where-Am-I graph, else the memo — and NEVER builds one: the caller is the passing-callout
    /// monitor on the UI thread. Null means "nothing to ask". The whole step is
    /// <see cref="RunwayShapeSource.Resolve"/>. Runway geometry does not depend on the gate-list
    /// token, so that is not consulted.
    /// </summary>
    public bool? IsOnRunwayPavement(string icao, double lat, double lon)
    {
        if (string.IsNullOrWhiteSpace(icao)) return null;

        IReadOnlyList<RunwayShape>? shapes;
        lock (_stateLock)
        {
            _runwayProbeIcao = icao;
            (shapes, _runwayShapeMemo) = RunwayShapeSource.Resolve(icao, DatabaseGeneration,
                _graph, _icao, _graphGeneration,
                _whereAmICachedGraph, _whereAmICachedIcao,
                _runwayShapeMemo);
        }
        // Outside the lock on a local reference — the shape list is immutable once built.
        return shapes == null ? null : RunwayPavement.IsOnPavement(lat, lon, shapes);
    }

    /// <summary>
    /// Captures, on the calling (UI) thread, the generation that goes with
    /// <paramref name="dataProvider"/> and returns the warm-up to run on any thread. The warm-up
    /// reads the runway rows alone — never a taxi graph — so it also answers at airports with no
    /// taxi paths and never starts the taxiway-name fetch; an airport with no runways publishes an
    /// empty list ("not on a runway"). It is stored only if the generation is unchanged and the
    /// probe is still asked about this airport (<see cref="RunwayShapeSource.Publish"/>).
    /// </summary>
    public Action PrepareRunwayShapeWarmUp(IAirportDataProvider dataProvider, string icao)
    {
        long readUnder = DatabaseGeneration;
        return () => WarmRunwayShapes(dataProvider, icao, readUnder);
    }

    private void WarmRunwayShapes(IAirportDataProvider dataProvider, string icao, long readUnder)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        var starts = dataProvider.GetRunwayStarts(icao) ?? new List<StartPosition>();
        var shapes = RunwayPavement.BuildShapesFromRunwayRows(starts, dataProvider.GetRunways(icao) ?? new List<Runway>());
        lock (_stateLock)
        {
            var held = _runwayShapeMemo;
            _runwayShapeMemo = RunwayShapeSource.Publish(
                held: held,
                icao: icao,
                readUnder: readUnder,
                currentGeneration: DatabaseGeneration,
                shapes: shapes,
                trackedIcao: _runwayProbeIcao);
            if (ReferenceEquals(_runwayShapeMemo, held))
            {
                string why = !RunwayShapeSource.MayStore(readUnder, DatabaseGeneration)
                    ? "the database changed while they were read"
                    : $"the probe is now asked about {(string.IsNullOrEmpty(_runwayProbeIcao) ? "no airport" : _runwayProbeIcao)}";
                Log.Debug("Surroundings", $"runway probe: {icao} runway rows read but not stored — {why}");
                return;
            }
        }
        Log.Debug("Surroundings", $"runway probe: {icao} warmed from its runway rows — {shapes.Count} runway(s) from {starts.Count} start row(s)");
    }

    /// <summary>
    /// Invalidates the "Where Am I" graph cache and the runway-shape memo, and moves
    /// <see cref="DatabaseGeneration"/>. Called by a database switch. Takes _stateLock like its
    /// twin <see cref="OnAirportDataUpdated"/>. Active guidance keeps its own graph, but the
    /// runway probe stops trusting it (its generation is now stale).
    /// </summary>
    public void ClearWhereAmICache()
    {
        lock (_stateLock)
        {
            _whereAmICachedGraph = null;
            _whereAmICachedIcao = "";
            _whereAmICachedToken = "";
            _whereAmICacheEpoch++;
            _runwayShapeMemo = null;
            Interlocked.Increment(ref _databaseGeneration);
        }
    }

    /// <summary>
    /// Online taxiway-name augmentation for <paramref name="icao"/> was just (re)fetched: drop the
    /// cached Where-Am-I graph so the next query picks up the new names. Called from the fetch
    /// thread, so it takes _stateLock like every other user of the cached graph pair.
    /// </summary>
    public void OnAirportDataUpdated(string icao)
    {
        if (string.IsNullOrEmpty(icao)) return;
        lock (_stateLock)
        {
            // Moved whatever the cache holds: a build of THIS airport in flight (which the cache
            // does not show yet) must not publish the names this fetch just replaced.
            _whereAmICacheEpoch++;
            if (string.Equals(_whereAmICachedIcao, icao, StringComparison.OrdinalIgnoreCase))
            {
                _whereAmICachedGraph = null;
                _whereAmICachedIcao = "";
                _whereAmICachedToken = "";
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
    /// <param name="icao">The airport <see cref="CurrentAirport.Resolve"/> named — an ident of any length; every provider lookup matches icao OR ident.</param>
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

        // The generation that goes with dataProvider: this method's one caller read it this turn.
        long databaseGeneration = DatabaseGeneration;

        lock (_stateLock)
        {
            TaxiGraph? graph = null;

            // Same cache, same staleness key — see the _whereAmICachedToken field comment.
            string token = ResolveParkingSpotVersion(icao);

            // Prefer the active guidance graph if it's for this airport
            if (_graph != null && string.Equals(_icao, icao, StringComparison.OrdinalIgnoreCase))
                graph = _graph;
            else if (_whereAmICachedGraph != null &&
                     string.Equals(_whereAmICachedIcao, icao, StringComparison.OrdinalIgnoreCase) &&
                     !GateDataSource.ShouldRebuildGateList(_whereAmICachedToken, token))
                graph = _whereAmICachedGraph;

            if (graph == null)
            {
                try
                {
                    var paths = dataProvider.GetTaxiPaths(icao) ?? new List<TaxiPath>();
                    if (paths.Count == 0) return false;

                    var parking = ResolveParkingSpots(dataProvider, icao);
                    var runwayStarts = dataProvider.GetRunwayStarts(icao) ?? new List<StartPosition>();

                    // Runways let the builder repair laterally-bogus start rows before
                    // they reach the centerlines this very call is about to query
                    // (TaxiGraph.SnapStartToRunwayCenterline).
                    graph = TaxiGraph.Build(paths, parking, runwayStarts,
                                            dataProvider.GetRunways(icao));
                    StoreWhereAmIGraph(graph, icao, token, databaseGeneration);
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
    /// Raised by the two landing-rollout entries that can be reached with NO position stream
    /// running: <see cref="BeginLandingRolloutNoGraph"/> (after a failed LoadRoute, so no Taxiing
    /// transition ever started one) and <see cref="BeginRunwayEndCountdownRollout"/> (no route at
    /// all) — and by the two entries into the ended-on-runway clearing phase
    /// (<c>_arrivedRunwayClearing</c>: the backtrack hand-off at the connection node and
    /// <see cref="HandleArrival"/>'s "taxiway data ends" arm), whose state is <c>Arrived</c>, on
    /// which MainForm STOPS the stream that <c>UpdateArrivedRunwayClearing</c> is driven from.
    /// MainForm answers by starting the taxi-guidance position stream; without it those rollouts
    /// would speak their touchdown sentence and never receive another frame, and the clearing phase
    /// would say "continue ahead until clear" and then nothing. Raised under the state lock after
    /// the state change, like <see cref="StateChanged"/> — after, because that handler's stop runs
    /// inside <c>SetState</c> and would otherwise undo the restart.
    /// </summary>
    public event EventHandler? PositionStreamRequired;

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
    /// <param name="announceStart">False for the landing-exit touchdown route: its first taxiway
    /// is wherever the placeholder route happens to begin (a parallel taxiway — "Taxiway A.
    /// Steering guidance active." ahead of "Touchdown… exit taxiway C9" on every landing,
    /// VirtualPilot 2026-09-18), and the touchdown callout is the instruction that matters.</param>
    public void StartGuidance(UserSettings settings, bool announceStart = true)
    {
        lock (_stateLock)
        {
        if (_route == null || _route.Segments.Count == 0) return;

        // If this route can't reach its runway, starts with an unmapped first leg, or carries a
        // route-start warning (LastRouteStartWarning: a start that is far away, behind or turned
        // back, a runway with no hold short point, a hold-short pick that could not be set), the
        // form's standstill utterance (or the first-taxiing-frame one-shot, for paths the form
        // doesn't run) speaks that warning right after this call. Open a short grace window so the
        // informational taxiway-crossing / taxiway-change / curve / destination-ahead callouts
        // don't stomp it at start.
        _startChatterSuppressUntil = (LastRouteReachWarning != null || LastRouteUnmappedStartWarning != null
                                      || LastRouteStartWarning != null)
            ? MSFSBlindAssist.Utils.SimClock.UtcNow.AddSeconds(START_WARNING_CHATTER_GRACE_SEC)
            : DateTime.MinValue;

        _announceCrossings = settings.TaxiGuidanceAnnounceCrossings;

        // Apply invert-pan / hard-pan settings before Start so the tone is
        // configured correctly the moment it begins sounding. Both are also
        // re-read on every tone update (cheap), so a runtime toggle while
        // taxi guidance is already active takes effect on the next position
        // frame.
        _steeringTone.InvertPan = settings.TaxiGuidanceInvertSteeringTone;
        _steeringTone.HardPan = settings.TaxiGuidanceHardPanTone;

        _lastToneWaveform = settings.TaxiGuidanceToneWaveform;
        _lastToneVolume = settings.TaxiGuidanceToneVolume;
        _steeringTone.Start(
            settings.TaxiGuidanceToneWaveform,
            settings.TaxiGuidanceToneVolume);

        SetState(TaxiGuidanceState.Taxiing);

        // A route that begins at a runway hold line starts HELD: no "steering guidance active"
        // callout — the hold sentence is the instruction, published as LastRouteStartHoldCue for
        // the caller to speak (see its doc). Continue resumes taxiing on segment 0. Entered FROM
        // Taxiing so every state listener sees guidance start: MainForm starts the position feed on
        // that transition (OnTaxiGuidanceStateChanged) — the previous Progressive leg's
        // ProgressiveHold stopped it — though it hands guidance no frames until Continue leaves
        // HoldShort. No taxiing frame can run in between: this method holds _stateLock until it returns.
        if (_route.StartHoldRunway != null)
        {
            EnterStartHold();
            return;
        }

        if (!announceStart) return;

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
        lock (_stateLock)
        {
            // Everything this frame says is spoken as ONE utterance (SpeakNow).
            var speech = _frameSpeech = new List<string>();
            var queued = _frameQueuedSpeech = new List<string>();
            try { UpdatePositionFrame(lat, lon, headingMag, magVariation, groundSpeedKts); }
            finally
            {
                _frameSpeech = null;
                _frameQueuedSpeech = null;
                FlushFrameSpeech(speech, queued);
            }
        }
    }


    private void UpdatePositionFrame(double lat, double lon, double headingMag, double magVariation, double groundSpeedKts)
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
        var nowYaw = MSFSBlindAssist.Utils.SimClock.UtcNow;
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
            // Airborne - a bounce, or the first seconds of a touch-and-go or go-around - holds the rollout
            // (LandingExitGoAround.HoldsRollout); MainForm ends it if the aircraft stays up.
            bool held = LandingExitGoAround.HoldsRollout(OnGroundProvider?.Invoke());
            if (held != _rolloutAirborneHold)
            {
                _rolloutAirborneHold = held;
                // Two calls, never one conditional: a conditional between interpolated strings is typed string
                // and misses RolloutDiag's invariant-culture handler (InvariantLogLine).
                if (held) RolloutDiag($"Rollout held, airborne: lat={lat:F6} lon={lon:F6} gs={groundSpeedKts:F1}kt");
                else RolloutDiag($"Rollout resumed, on the ground: lat={lat:F6} lon={lon:F6} gs={groundSpeedKts:F1}kt");
            }
            if (held) return;
            CheckOffPavement(lat, lon, groundSpeedKts);
            UpdateLandingRollout(lat, lon, headingTrue, groundSpeedKts);
            return;
        }

        // Backtrack after runway-end: tone guides on reciprocal runway heading.
        if (_state == TaxiGuidanceState.BacktrackingOnRunway)
        {
            UpdateBacktracking(lat, lon, headingTrue, groundSpeedKts);
            return;
        }

        // FULL-LENGTH BACKTRACK DEPARTURE: entered from ContinuePastHoldShort at the
        // intermediate entrance, driven here until the full-length threshold is reached,
        // then handed to LiningUp for the 180 degree turnaround.
        if (_state == TaxiGuidanceState.BacktrackDeparture)
        {
            UpdateBacktrackDeparture(lat, lon, headingTrue);
            return;
        }

        // Start hold (TaxiRoute.StartHoldRunway) for a route adopted while guidance was already
        // running — a landing-exit re-route, which goes straight to Taxiing without StartGuidance.
        // It enters the hold on its first taxiing frame and speaks the sentence in that same frame:
        // MainForm feeds no frames while guidance holds. An aircraft already more than 10 m along the
        // route, or within the clear margin of any runway (the pass's own tests — the SAME margin the
        // pass demands of a stop it invents, never bare containment), gets no hold, and the skip is
        // logged with its reason beside the adoption's "Route crossings:" line.
        if (_state == TaxiGuidanceState.Taxiing && _currentSegmentIndex == 0
            && _route?.StartHoldRunway != null)
        {
            string? skipReason = null;
            if (Navigation.RouteRunwayCrossings.RouteProgressMeters(_route.Segments, lat, lon)
                > Navigation.RouteRunwayCrossings.StopPassedToleranceMetres)
                skipReason = "aircraft already past the start node";
            else if (Navigation.RouteRunwayCrossings.RunwayWithinClearMargin(_graph?.RunwayCenterlines, lat, lon) is { } runwayUnder)
                skipReason = $"aircraft on or within the clear margin of runway {runwayUnder.Name1}/{runwayUnder.Name2}";

            if (skipReason != null)
            {
                _guidanceLog.Info(
                    $"Start hold skipped: startHold=\"{_route.StartHoldRunway}\" — {skipReason}.");
                _route.StartHoldRunway = null;
            }
            else
            {
                EnterStartHold();
                string? cue = ConsumeStartHoldCue();
                if (cue != null) AnnounceInstruction(cue);
                return;
            }
        }

        if (_state != TaxiGuidanceState.Taxiing || _route == null || _graph == null)
        {
            // Ended-on-runway clearing tone: a landing-exit arrival whose route data ran out with
            // the aircraft still on runway pavement (landing runway or a crossing one), so
            // HandleArrival kept the tone ALIVE as a heading-hold on the exit direction — the tone
            // is the instrument, and "continue ahead until clear" must be flyable by tone, not just
            // spoken. Concludes the moment the aircraft is laterally clear of every runway
            // corridor, or after CLEARING_MAX_TRAVEL_M of travel (never hold a tone forever).
            if (_state == TaxiGuidanceState.Arrived && _graph != null && _arrivedRunwayClearing)
            {
                UpdateArrivedRunwayClearing(lat, lon, headingTrue);
                return;
            }
            // With no route the pilot still needs runway incursion warnings while they taxi to
            // their gate before opening the taxi planner (e.g. runway 16/34 at EIDW lies east of
            // the S6 exit on the way to the terminal). That covers a landing-exit arrival AND the
            // three route-less ways guidance can now finish on the airfield still taxiing: the
            // countdown's "Runway vacated" close-out and both backtrack endings, all of which land
            // in Taxiing. See Services/RunwayIncursionWatch for why the map, not the state, decides.
            if (RunwayIncursionWatch.RunsWithoutARoute(_state, _graph != null))
                CheckRunwayIncursion(lat, lon, headingTrue);
            // ProgressiveHold is a terminal no-op: tone is off, the aircraft holds,
            // the pilot sets the next leg. No tone, no recalc, no movement logic.
            // (The unreachable-runway safety net and lineup path are gated on
            // _isRunwayLineup / _hasLineupTarget, which progressive never sets.)
            return;
        }

        // The exit: from the handoff until exit guidance ends (Arrived) — not after, where unmapped
        // aprons would make it cry wolf.
        if (_isLandingExitRoute)
            CheckOffPavement(lat, lon, groundSpeedKts);

        // Post-handoff overshoot monitor. After TryEarlyExitHandoff or the
        // turnBegun/exitedLaterally handoff from UpdateLandingRollout transitions
        // to Taxiing, the LandingRollout overshoot detector no longer runs. If
        // the pilot fails to turn, the off-route recalc would try to route BACK
        // to the missed exit — dangerous on an active runway. This block detects
        // that case and retargets to the next downfield exit or triggers the
        // runway-end countdown, exactly like the LandingRollout overshoot path.
        if (_rolloutHandoffActive && _rolloutExit != null && _rolloutRunway != null)
        {
            double hdgDeltaSignedPH = NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue);
            double hdgDeltaAbsPH = Math.Abs(hdgDeltaSignedPH);
            double exitRelBearingPH = Navigation.RolloutExitGate.ExitRelativeBearingDeg(
                _rolloutExit.ExitBearingTrue, _rolloutRunwayHeadingTrue);
            // Direction test only, NOT the distance window IsExitTurnBegun applies. This
            // block runs AFTER handoff, when the aircraft is already near or past the exit,
            // so a proximity gate would be wrong here. But a wrong-way turn clearing the
            // overshoot monitor is the same hole one level down: turnBegunPH sets
            // _rolloutHandoffActive = false under the comment "Pilot has taken the exit".
            bool turnBegunPH = hdgDeltaAbsPH >= ROLLOUT_TURN_BEGAN_HDG_DEG
                               && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS
                               && Navigation.RolloutExitGate.IsTurnTowardExit(
                                      hdgDeltaSignedPH, exitRelBearingPH);

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

            double signedAlongPastFtPH = SignedAlongRunwayMeters(
                lat, lon,
                _rolloutExit.Latitude, _rolloutExit.Longitude,
                _rolloutRunwayHeadingTrue) * METERS_TO_FEET - _rolloutExit.TurnPointOffsetFeet;

            // Must be past the exit junction AND heading toward the exit bearing to
            // count as committed. Without the pastExit guard, A/P jitter on shallow
            // exits (e.g. 7.6° at LGAV 03R D8/D9) falsely satisfies this check while
            // the aircraft is still hundreds of feet short, killing the overshoot monitor
            // prematurely. The same rule as alignedWithExit in UpdateLandingRollout.
            bool alignedWithExitPH = Navigation.RolloutExitGate.IsAlignedWithExit(
                headingTrue, _rolloutExit.ExitBearingTrue, _rolloutExit.ExitAngleDegrees,
                _rolloutExit.DivergenceAngleDegrees, hdgDeltaAbsPH, groundSpeedKts, signedAlongPastFtPH >= 0.0);

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
                // aircraft would exceed OVERSHOOT_ON_CENTERLINE_FT + 5 ft, read at how
                // steeply the exit leaves its node (RolloutExitGate.OvershootMarginFor).
                double overshootMarginPH = MissMarginFeet();
                if (signedAlongPastFtPH >= overshootMarginPH
                    && lateralFtPH < OVERSHOOT_ON_CENTERLINE_FT
                    && !Navigation.LandingExitPathFollow.HoldsOffMiss(
                           GetRolloutExitPath(), lat, lon, headingTrue, _rolloutRunwayHeadingTrue,
                           signedAlongPastFtPH, overshootMarginPH)
                    && !Navigation.LandingExitPathFollow.StillOnExitPath(
                           GetRolloutExitPath(), lat, lon, signedAlongPastFtPH, overshootMarginPH))
                {
                    _rolloutHandoffActive = false;

                    // The rollout overshoot's own rule: downfield of the aircraft as well as the missed
                    // exit, and the graph asked before any "Missed last exit".
                    var nextExitPH = PickOvershootRetarget(signedAlongPastFtPH, "OVERSHOOT (post-handoff)");
                    if (nextExitPH != null)
                    {
                        RetargetLandingExit(nextExitPH, lat, lon, headingTrue);
                        return;
                    }

                    AnnounceMissedLastExit();
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
        AdvanceToNearestSegment(lat, lon, headingTrue);

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

        // WHERE A CALLOUT IS LOST is a different question from WHEN WE HAVE ARRIVED, and
        // the two must not share one local. CheckUpcomingAnnouncements needs the radius at
        // which the destination-ahead callout stops being deliverable, which for a
        // landing-exit route is its own LANDING_EXIT_ARRIVAL_RADIUS_M (the dedicated
        // `_isLandingExitRoute && onFinalSegment` block below measures arrival against
        // that, not against arrivalRadius). Folding that branch into arrivalRadius itself
        // was tried and reverted: the fallback arrival check further down has no
        // stillOnRunway guard of its own, so widening its radius from ~6 m to 25 m let a
        // landing-exit route DECLARE ARRIVAL while still laterally on the runway -- which
        // the dedicated block deliberately withholds. Arrival behaviour is unchanged by
        // this local; only the announcement gate reads it.
        double announcementClearRadius = _isLandingExitRoute
            ? LANDING_EXIT_ARRIVAL_RADIUS_M
            : arrivalRadius;

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
            bool stillOnRunway = alongRemainingM > 0.0 && IsWithinRolloutRunwayLaterally(lat, lon);

            if (!stillOnRunway && (distToTarget < LANDING_EXIT_ARRIVAL_RADIUS_M || pastNode))
            {
                RolloutDiag($"Landing-exit arrival: distToTarget={distToTarget:F0}m " +
                    $"alongRemaining={alongRemainingM:F0}m cross={crossM:F0}m pastNode={pastNode}");
                HandleArrival();
                return;
            }

            // MISSED-VACATE BACKSTOP. Both captures above require ending up NEAR the extension
            // node (the 25 m radius, or past it inside the 60 m cross gate), so taking the WRONG
            // branch at the exit junction satisfies neither. The off-route recalc cannot rescue
            // it either: TryRecalculateRoute's final-segment guard covers nearly the whole of a
            // 5-segment exit route. The result was total silence with the tone panning at a node
            // astern. Conclude guidance instead, and mark it so HandleArrival SAYS the exit was
            // missed rather than falling through to the normal "Off the runway at E" phrasing,
            // which reads as a successful vacate.
            if (!stillOnRunway)
            {
                if (distToTarget < _landingExitMinDistToTargetM)
                    _landingExitMinDistToTargetM = distToTarget;

                bool targetAstern = alongRemainingM <= -MISSED_VACATE_ASTERN_M;
                bool rangeOpening = distToTarget >= _landingExitMinDistToTargetM + MISSED_VACATE_OPENING_M;

                // Only arm once the aircraft actually got close (so a route abandoned early never
                // trips it), and only while genuinely off to the side and still rolling.
                bool missing = _landingExitMinDistToTargetM <= MISSED_VACATE_APPROACH_M
                               && Math.Abs(crossM) > LANDING_EXIT_ARRIVAL_CROSS_M
                               && (targetAstern || rangeOpening)
                               && groundSpeedKts >= OFF_ROUTE_MIN_GS_KTS;

                if (!missing)
                {
                    _missedVacateSince = DateTime.MinValue;
                }
                else
                {
                    if (_missedVacateSince == DateTime.MinValue)
                        _missedVacateSince = MSFSBlindAssist.Utils.SimClock.UtcNow;

                    if ((MSFSBlindAssist.Utils.SimClock.UtcNow - _missedVacateSince).TotalSeconds >= MISSED_VACATE_PERSISTENCE_SEC)
                    {
                        RolloutDiag($"Landing-exit MISSED: distToTarget={distToTarget:F0}m " +
                            $"min={_landingExitMinDistToTargetM:F0}m alongRemaining={alongRemainingM:F0}m " +
                            $"cross={crossM:F0}m astern={targetAstern} opening={rangeOpening}");
                        _missedVacateSince = DateTime.MinValue;
                        _landingExitMissed = true;
                        HandleArrival();
                        return;
                    }
                }
            }
        }

        if (onFinalSegment)
        {
            bool arrived = distToTarget < arrivalRadius;

            // Runway hold-short STANDSTILL capture. The runway route ends at the hold
            // node and its only capture is the 12 m radius (the past-node backstop below
            // is deliberately not applied to runway destinations). A pilot who cuts the
            // last corner and stops abeam the hold line sits just outside that radius
            // with the tone pointing at a node beside them and no "Stop. Hold short"
            // ever spoken — LEBL 24L via G3, 2026-08-28: stopped 15 m from the hold node
            // for 15 s, tone -107°, then drove off and repositioned to the runway. An
            // aircraft that has STOPPED this close to the hold line is holding short,
            // whichever side of the node it stopped on, so treat it as arrived: the
            // HoldShort state then names the runway and waits for Continue exactly as
            // it does for a centred stop. Bounded by speed AND distance so a taxiing
            // pass-by is untouched and a stop further back (queueing) is not claimed.
            if (!arrived && _isRunwayLineup
                && groundSpeedKts <= RUNWAY_HOLD_STANDSTILL_MAX_KTS
                && distToTarget <= RUNWAY_HOLD_STANDSTILL_CAPTURE_M)
            {
                arrived = true;
            }

            // Past-node backstop for a destination the aircraft can roll THROUGH: a
            // plain taxiway endpoint (progressive-taxi "end of taxiway X") or a gate.
            // Both carry a tight ~6 m arrival radius, so a roll-through at taxi speed
            // can miss the circle entirely and the tone then chases the now-behind node
            // — the LPPT 02 → U5 failure that the landing-exit captures were widened
            // for, and which a gate could still hit: its ONLY capture is that 6 m
            // radius (a LOWS park came within 0.3 m, but nothing guarantees it), and
            // missing it costs the "Align with gate" instruction as well as the tone.
            //
            // The countdown is not lost by including gates: CheckParkingCountdown gets
            // its final call in the arrived branch below, and by the time the node is
            // ASTERN the 50/20/10 ft cadence has nothing left to say anyway.
            //
            // Runway lineup stays excluded — LiningUp owns that approach and has its own
            // unreachable-route warning. Landing-exit routes are normally handled (and
            // returned) by the branch above, but NOT while stillOnRunway is true there —
            // that guard deliberately withholds arrival while the aircraft is still
            // laterally within the runway, so a landing-exit route on its final segment
            // can still fall through to here on every frame until it clears the pavement.
            // This block therefore keeps the ~6 m gate radius: `arrivalRadius` is
            // deliberately NOT widened to LANDING_EXIT_ARRIVAL_RADIUS_M (25 m), because
            // this fallback has no stillOnRunway guard of its own and a 25 m radius here
            // would let a landing-exit route declare arrival while still laterally on the
            // runway — exactly what the block above withholds. See the
            // `announcementClearRadius` comment at the top of this method.
            if (!arrived && !_isRunwayLineup)
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
        double headingError = ComputeSteeringHeadingError(
            lat, lon, headingTrue, out double targetLat, out double targetLon);
        double bearingToTarget = NavigationCalculator.CalculateBearing(lat, lon, targetLat, targetLon);
        // The bounded pull back onto the exit segment (see ExitCrossTrackPull) — also applied to
        // the post-high-speed-exit bearing floor below.
        double exitCrossTrackPull = ExitCrossTrackPull(lat, lon);

        // Initial big-turn cue (one-shot, first taxiing frame). When guidance
        // starts with the aircraft pointing well away from the route's first
        // segment — the normal post-pushback case — a tone alone can't convey
        // "turn around, which way." Speak a one-shot direction cue (sign matches
        // the tone). The cue (not the unmapped-start warning below) is dropped when
        // the route doesn't reach its runway (LastRouteReachWarning set): that
        // warning is the priority — the form speaks it after StartGuidance, and a
        // turn cue here would be moot (the pilot will reprogram) AND would stomp it.
        if (!_initialTurnCueAnnounced)
        {
            _initialTurnCueAnnounced = true;
            // The cue is composed once by LoadRoute and owned by RouteStartTurnCue. Speak
            // it here only if the form did not fold it into its standstill utterance --
            // routes the form did not start (landing-exit handoffs, announceSummary:false)
            // have nobody else to say it. Never recomposed here: two composers would be two
            // wordings and, worse, two chances to disagree on left versus right.
            //
            // Consumed UNCONDITIONALLY (so the per-frame one-shot can never repeat it
            // behind the form's back) regardless of whether a reach warning drops it from
            // what actually gets spoken.
            string? cue = ConsumeInitialTurnCue();
            // The unmapped-start warning is a DIFFERENT kind of fact from the cue -- a
            // safety-relevant statement about ground the aircraft is about to taxi across
            // unmapped -- and must survive a reach warning rather than being dropped with
            // the cue (ComposeStartSpeech's job). It is read here WITHOUT being consumed
            // yet: only once we know it is actually about to be spoken is it taken
            // (cleared), so a route with nothing to say at all can never silently discard a
            // warning nobody else will ever say.
            //
            // PR #238 review, Task 5 Defect A: this used to consume (clear) BOTH the cue and
            // the warning unconditionally via JoinStartSpeech, then drop the WHOLE result --
            // warning included -- behind a `LastRouteReachWarning == null` guard that was
            // written only to suppress the moot cue. On every path that doesn't run
            // TaxiAssistForm's standstill block (Progressive Taxi, landing-exit handoffs,
            // announceSummary:false) nothing else speaks either message, so the warning was
            // silently lost instead of merely reordered.
            bool reachWarningPresent = LastRouteReachWarning != null;
            string? startSpeech = RouteReachabilityMessages.ComposeStartSpeech(
                LastRouteUnmappedStartWarning, cue, reachWarningPresent);
            if (startSpeech != null)
            {
                ConsumeUnmappedStartWarning();
                AnnounceInstruction(startSpeech);
            }
            // A route that opens with a big turn (the RouteStartTurnCue threshold — whether the cue
            // was spoken just above or folded into the form's standstill utterance): the junction
            // and curve callouts describe junctions beyond the turn, so they wait until the pilot
            // has come round (INITIAL_TURN_DONE_DEG) — said on top of "Turn right to come around"
            // they pointed the wrong way (VirtualPilot, 2026-09-25).
            if (Navigation.RouteStartTurnCue.Compose(headingError, null) != null)
                _initialTurnPending = true;
        }
        if (_initialTurnPending && Math.Abs(headingError) < INITIAL_TURN_DONE_DEG)
            _initialTurnPending = false;
        // The tone keeps to the side the opening cue SAID while the pilot is still coming round. Near a
        // reversal the shortest way round is a coin toss between frames, so the tone could pan hard the
        // other way from "Turn right to come around" and the pilot, following the tone, ran off the stand
        // apron (VirtualPilot, LOWW Parking 61B: 14 m off the pavement). Past 90° the same turn is simply
        // expressed the other way round; once within 90° the route's own error rules again.
        if (_initialTurnPending && _initialTurnSign != 0 && Math.Abs(headingError) >= 90.0
            && Math.Sign(headingError) != _initialTurnSign)
            // Capped below 180: the tone folds any error past ±180 back to the short way round.
            headingError = _initialTurnSign * Math.Min(360.0 - Math.Abs(headingError), 179.0);

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
            else if (IsWithinRolloutRunwayLaterally(lat, lon))
            {
                // _rolloutHandoffActive already cleared (turnBegunPH at 15°) but
                // turnComplete not yet fired. bearing-to-target is still distorted
                // (exit node sits north of the runway). Use ExitBearingTrue directly
                // so the tone decays smoothly to zero rather than jumping to a large
                // bearing-to-node error for the brief gap between the two releases.
                //
                // GATED on still being laterally over the runway pavement — the one shared
                // answer to "is bearing-to-a-node-beside-the-runway still distorted?".
                // Ungated this branch REPLACED the route error outright, and neither release
                // test can fire when the route wants a larger pan the SAME way (turnComplete
                // needs the heading to cross the exit bearing; routeOpposesFloor needs
                // opposite signs): LOWS 15 → E, taxiway E curves to 246° but ExitBearingTrue
                // is 193.5°, the aircraft settled on 192.8°, and the tone reported 0.7° —
                // "on profile" — while the route asked for +123°, running the pilot 380 m
                // down the wrong taxiway at 33 kt in silence. Once clear of the pavement the
                // floor is released unconditionally and the live route owns the tone.
                headingError = NormalizeAngle(minError + exitCrossTrackPull);
            }
            else
            {
                // Clear of the pavement — the distortion this override exists for is over,
                // bearing-to-target is trustworthy again, and the live route is authoritative.
                // RELEASE, unconditionally (the LOWS 15 → E failure above).
                _postHighSpeedExitMinBearing = 0.0;
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
        // The lead grows with the heading this turn has already swept (gentle lead, then the
        // sharp-turn lead past GuidanceGeometry.SHARP_TURN_RAMP_END_DEG); constant on an aircraft
        // whose two leads are equal. Read by the tone and the "Straighten." cue alike.
        double turnLead = GuidanceGeometry.TurnLeadSeconds(
            _yawEpisodeTurnDeg, TurnLeadSeconds, SharpTurnLeadSeconds);
        if (!_steeringToneSuppressed)
        {
            // Rollout anticipation: only while genuinely yawing, so straight-line
            // sensor noise can't perturb the tone. Runway/gate lineup and docking
            // keep their own precision profiles — this applies to Taxiing only.
            // The leads are per-aircraft (0 = disabled).
            double toneError = turnLead > 0.0
                               && Math.Abs(_yawRateDegSec) >= TURN_LEAD_MIN_RATE_DEG_SEC
                ? GuidanceGeometry.ProjectHeadingError(
                      _smoothedHeadingError, _yawRateDegSec, turnLead, TurnLeadMaxDegrees)
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
        if (turnLead > 0.0 && _yawEpisodeSign != 0 && !_straightenFiredThisEpisode
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
                    _smoothedHeadingError, _yawRateDegSec, turnLead, TurnLeadMaxDegrees);
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

        // The turn the tone is asking for RIGHT NOW (raw, this frame) — TryAnnounceCurve
        // must not name a curve further along the route that runs the other way.
        _lastRouteHeadingErrorDeg = headingError;

        // Deliver a taxiway-change name AnnounceOrDeferTaxiwayChange deferred while the
        // start-warning chatter window was open, now that it may have closed. Cheap no-op
        // when nothing is pending; must run every Taxiing frame this method reaches, not
        // only on a segment advance, or a deferred name could sit unspoken until the window
        // closes several frames later. This point in UpdatePosition is only ever reached
        // while _state == Taxiing (every earlier branch of this method returns first for
        // every other state), so nothing pending can survive past a transition out of
        // Taxiing without help -- SetState provides that help; see its own remarks.
        FlushPendingTaxiwayAnnouncement(currentSeg.TaxiwayName);

        // Check for upcoming announcements
        CheckUpcomingAnnouncements(distToTarget, currentSeg, announcementClearRadius);

        // Check hold-short countdown (300/150/50ft cadence) — critical runway incursion prevention
        CheckHoldShortCountdown(distToTarget, currentSeg);

        // Check ground speed warnings (too fast for turn, too fast straight)
        CheckSpeedWarnings(distToTarget, currentSeg);

        // Runway incursion: warn if approaching any off-route hold-short node
        CheckRunwayIncursion(lat, lon, headingTrue);

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
        // Tolerance = max(edge half-width + 15 m, 25 m), width defaulted (75 ft) and capped
        // (300 ft) against bogus DB rows. One definition, shared with RouteReachability:
        // see Navigation.PavementTolerance.
        double perpTolerance = PavementTolerance.ForWidthFeet(currentSeg.PathWidth);

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
        if ((MSFSBlindAssist.Utils.SimClock.UtcNow - _lastSegmentAdvanceTime).TotalSeconds < POST_TURN_OFFROUTE_GRACE_SEC)
            nearTurn = true;

        // Route-joined latch (see field). Until the aircraft has reached the route
        // line once, the taxi from the gate onto the first taxiway reads as
        // "off-route" by definition — suppress recalc so the entered clearance
        // isn't trimmed before the pilot joins it.
        if (perp <= perpTolerance)
        {
            _hasJoinedRoute = true;

            // PR #238 review, Critical A re-fix. The reachability-refusal latch
            // (ReachabilityRefusalGate's _lastReachabilityRefusalKey) must ALSO clear here,
            // on this SPECIFIC test — being back within the route's own pavement tolerance is
            // the one signal that genuinely means an off-route episode has ended. This used to
            // be decided by the `else` of the offRouteNow persistence guard further down (the
            // ORIGINAL Important 2 fix) — but that guard's `else` runs whenever offRouteNow
            // reads false, or ground speed drops below OFF_ROUTE_MIN_GS_KTS, and offRouteNow
            // has THREE independent ways to read false, only one of which is "the episode is
            // over":
            //   - perp <= perpTolerance (this test) — genuinely back on the route line.
            //   - nearTurn — true for POST_TURN_OFFROUTE_GRACE_SEC after EVERY ordinary
            //     segment advance, and within the speed-scaled window before a turning next
            //     segment. On navdata's short 5-15 m segments this is ROUTINE while an
            //     excursion is still ongoing, not the end of one.
            //   - _lastGroundSpeedKts < OFF_ROUTE_MIN_GS_KTS — a pilot who just heard a
            //     refusal and stops to think crosses this for a single frame while STILL
            //     laterally off-route. The old code read that stop as "episode over," cleared
            //     the latch, and let the identical ~20-word interrupting refusal fire again the
            //     moment RECALCULATION_COOLDOWN_SEC expired and the aircraft rolled on — the
            //     exact 15-second nag Important 2 was written to close, reopened by clearing on
            //     the wrong signal.
            // Clearing on perp <= perpTolerance instead cannot fire on either in-episode case:
            // both presuppose the aircraft is still laterally off the route, and offRouteNow's
            // other two disjuncts (farBehindStart, goingBackward) are irrelevant to nearTurn or
            // ground speed — they don't make perp small. Harmless to re-clear every frame the
            // aircraft is on the route line, including frames where nothing was latched (this
            // runs far more often than a genuine refusal latch exists to be cleared). The
            // successful-recalculation clear in TryRecalculateRoute is untouched — that is the
            // OTHER legitimate way an off-route episode ends, and is unaffected by this fix.
            _lastReachabilityRefusalKey = null;
        }

        // NEVER-JOINED ESCAPE. The latch above correctly stops a stand-to-first-taxiway taxi
        // reading as off-route, but with no way out a route the aircraft NEVER joins leaves
        // off-route detection dead for that route's whole life — KBNA 2026-08-08, 690 m the
        // wrong way over 50 s at 31 kt with the tone pinned ~170 degrees behind, no recalc and
        // no word. Key it on the range OPENING against the closest approach so far, never on
        // absolute distance: a genuine join is CLOSING, which is what keeps the protected case
        // protected.
        if (!_hasJoinedRoute && perp < _minPerpWhileUnjoinedM)
            _minPerpWhileUnjoinedM = perp;

        bool neverJoinedEscape = !_hasJoinedRoute
                                 && _minPerpWhileUnjoinedM < double.MaxValue
                                 && perp > _minPerpWhileUnjoinedM + NEVER_JOINED_OPENING_M
                                 && perp > NEVER_JOINED_FLOOR_M;

        // Uncharted-crossing routes: suppress off-route/auto-recalc while a synthetic
        // crossing segment is still ahead (or current). Mid-gap the aircraft is off
        // charted pavement BY DESIGN, and a recalc from there would rebuild through
        // the component filter — reproducing the exact island-stub route the crossing
        // exists to replace. Once past the crossing (on the destination's component),
        // this re-enables and recalcs behave normally on that side.
        bool unchartedAhead = false;
        for (int ui = _currentSegmentIndex; ui < _route.Segments.Count; ui++)
        {
            if (_route.Segments[ui].IsUncharted) { unchartedAhead = true; break; }
        }

        bool offRouteNow = (_hasJoinedRoute || neverJoinedEscape) && !nearTurn && !unchartedAhead
                           && (perp > perpTolerance || farBehindStart || goingBackward);

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
                _offRouteSince = MSFSBlindAssist.Utils.SimClock.UtcNow;

            if ((MSFSBlindAssist.Utils.SimClock.UtcNow - _offRouteSince).TotalSeconds >= OFF_ROUTE_PERSISTENCE_SEC)
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
            //
            // Deliberately does NOT ALSO clear _lastReachabilityRefusalKey here (PR #238
            // review, Critical A re-fix — this branch used to, and that was the regression).
            // This `else` is not specific to "the episode ended, the aircraft is back on
            // route": it is reached whenever offRouteNow reads false for ANY of its three
            // reasons (back on route, nearTurn, or a low-speed frame while still off-route —
            // see the perp <= perpTolerance block above for the one test that actually means
            // "on route," and for why the other two fire routinely INSIDE a sustained
            // off-route episode). Clearing the latch on nearTurn or a momentary stop reopened
            // the exact 15-second-nag defect Important 2 was written to close: a pilot who
            // stopped below OFF_ROUTE_MIN_GS_KTS to think about a just-spoken refusal cleared
            // the latch here, then rolled on and heard the identical refusal again the moment
            // the recalculation cooldown expired.
            _offRouteSince = DateTime.MinValue;
        }
        } // end lock(_stateLock)
    }

    private List<string>? _frameSpeech;
    private List<string>? _frameQueuedSpeech;
    // First node of the route LoadRoute adopted (not of a recalculation) — see CheckRunwayIncursion.
    private int _loadedRouteStartNodeId = -1;

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
    /// THE definition of the steering error: the signed angle (negative = left) between
    /// where the aircraft points and where the route wants it to go. Read by the steering
    /// tone every frame, and — through <c>LoadRoute</c> — by the route-start turn cue.
    ///
    /// <para><b>Both callers must read THIS, not an approximation of it.</b> The tone pans
    /// on the sign; the cue speaks the sign as the words "left" or "right". A blind pilot
    /// hears both and has no third source to break a tie, so the two must be the same
    /// quantity by construction rather than by two sites happening to compute something
    /// similar. The cue previously took <c>Segments[0].BearingDegrees - headingTrue</c>
    /// instead, which is a genuinely different number — measured 35° apart on the live
    /// KATL 2026-08-27 route (−143.6° against the tone's −175.8°). Sign disagreement needs
    /// only the two to straddle ±180°, which is precisely the near-U-turn band this cue
    /// exists for, and 35° is a far wider flip window than the ~5.5° magnetic bias the
    /// preceding fix removed.</para>
    ///
    /// <para>Two properties come free from reading the look-ahead walk rather than a raw
    /// segment bearing, and are the reason the walk exists: navdata's 5–15 m segments make
    /// raw bearings unrepresentative of the turn actually being asked for, and
    /// <see cref="GuidanceGeometry"/> skips sub-<c>DEGENERATE_SEG_M</c> segments before
    /// projecting — which matters most at <c>Segments[0]</c>, where
    /// <c>TaxiGraph.SplitEdgeAtPoint</c> can leave an arbitrarily short snap stub whose
    /// bearing is a phantom axis.</para>
    /// </summary>
    private double ComputeSteeringHeadingError(double lat, double lon, double headingTrue)
        => ComputeSteeringHeadingError(lat, lon, headingTrue, out _, out _);

    /// <summary>
    /// Composes (or clears) <see cref="LastRouteInitialTurnCue"/> for the route and segment
    /// cursor CURRENTLY set, from the aircraft's live position and TRUE heading. The one
    /// composer, so the cue is always the tone's own quantity and one wording.
    ///
    /// <para>Called by <c>LoadRoute</c> with the cursor at 0, and again by the landing-exit
    /// handoff's re-anchor. That second call is not tidiness: when the handoff re-route
    /// FAILS, guidance continues on the TOUCHDOWN route, whose cue was composed rolling
    /// straight down the runway — heading error ≈ 0, so <c>Compose</c> returned null — and
    /// the re-anchor then moves the cursor to a segment that can sit behind the aircraft.
    /// The pilot would get the hard-panned tone of a turnaround with no words, on the
    /// degraded path where they are least able to infer it, because the pre-composition
    /// design computed this cue fresh on the first taxiing frame and this one does not.
    /// Recomposing restores that, without giving the cue a second wording.</para>
    ///
    /// <para>The taxiway is the first NAMED leg at or after the cursor — the first leg
    /// actually ahead, which for a re-anchored cursor is not <c>Segments[0]</c>.</para>
    /// </summary>
    private void ComposeInitialTurnCue(double lat, double lon, double headingTrue)
    {
        LastRouteInitialTurnCue = null;
        if (_route == null || _route.Segments.Count == 0) return;

        double initialErr = ComputeSteeringHeadingError(lat, lon, headingTrue);
        string? firstNamed = null;
        for (int i = Math.Max(0, _currentSegmentIndex); i < _route.Segments.Count; i++)
        {
            if (string.IsNullOrEmpty(_route.Segments[i].TaxiwayName)) continue;
            firstNamed = _route.Segments[i].TaxiwayName;
            break;
        }
        LastRouteInitialTurnCue = Navigation.RouteStartTurnCue.Compose(initialErr, firstNamed);
        _initialTurnSign = LastRouteInitialTurnCue != null ? Math.Sign(initialErr) : 0;
    }

    /// <inheritdoc cref="ComputeSteeringHeadingError(double, double, double)"/>
    /// <remarks>
    /// The out-parameter overload also hands back the look-ahead walk target, so the
    /// per-frame diagnostic trace can log it without walking the polyline a second time on
    /// a ~30 Hz path. The target is computed unconditionally (even on the rollout-handoff
    /// branch that does not steer by it) so the trace keeps the exact shape it had before
    /// this computation was extracted.
    /// </remarks>
    private double ComputeSteeringHeadingError(
        double lat, double lon, double headingTrue,
        out double targetLat, out double targetLon)
    {
        (targetLat, targetLon) = GetGuidanceTarget(lat, lon);

        if (_rolloutHandoffActive && _route != null
            && _currentSegmentIndex < _route.Segments.Count)
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
            var currentSeg = _route.Segments[_currentSegmentIndex];
            double headingError = NormalizeAngle(
                NormalizeAngle(currentSeg.BearingDegrees - headingTrue) + ExitCrossTrackPull(lat, lon));

            // The segment's bearing only means "follow the exit" when the aircraft is ON that
            // segment. A handoff route can open with a leg that runs ALONGSIDE the runway,
            // beside the pavement (KTPA 28 N: 44 m parallel to the centreline, 31 m right of
            // it, then 91° right up N) — its bearing is runway heading, the cross-track pull
            // stands aside for the sharp turn after it, and the tone went SILENT under "Sharp
            // right now, taxiway N." until the pilot heard "Missed taxiway N" (VirtualPilot
            // 2026-09-25). Well off to the side of such a leg, and with the route's next turn
            // toward that same side, steer at the leg's end: the direction is the one the
            // verbal just gave, so this never pulls the other way.
            if (_currentSegmentIndex + 1 < _route.Segments.Count)
            {
                double nextTurn = NormalizeAngle(_route.Segments[_currentSegmentIndex + 1].BearingDegrees
                                                 - currentSeg.BearingDegrees);
                AlongTrackToSegmentEnd(lat, lon, currentSeg, out double alongRemM, out double crossM);
                if (Math.Abs(nextTurn) > HANDOFF_XTRACK_MAX_SEG_DEG && alongRemM > 0.0
                    && Math.Abs(crossM) >= HANDOFF_OFF_SEGMENT_M)
                {
                    double toEnd = NormalizeAngle(NavigationCalculator.CalculateBearing(
                        lat, lon, currentSeg.ToNode.Latitude, currentSeg.ToNode.Longitude) - headingTrue);
                    if (Math.Sign(toEnd) == Math.Sign(nextTurn) && Math.Abs(toEnd) > Math.Abs(headingError))
                        headingError = toEnd;
                }
            }
            return headingError;
        }

        double bearingToTarget = NavigationCalculator.CalculateBearing(lat, lon, targetLat, targetLon);
        return NormalizeAngle(bearingToTarget - headingTrue);
    }

    /// <summary>
    /// A bounded pull back onto the current exit segment, for the two post-handoff tone sources
    /// that have no cross-track term of their own (segment bearing while _rolloutHandoffActive,
    /// and the ExitBearingTrue floor while still over the runway). Segment bearing alone reads an
    /// aircraft parallel to the exit but 16 m wide of it as zero error; on a shallow RET the
    /// aircraft never turns the 15° that ends that phase, so the whole exit was steered that way —
    /// KPDX 28R A6 (16°): tone silent while the pilot drifted 16 m wide, then "Missed A6"; KATL
    /// 08L B11 ran onto the grass under the floor (VirtualPilot 2026-09-23). Only where cross-track
    /// means something: alongside the segment (not short of it — before a right-angle exit's first
    /// segment the "cross-track" is the along-runway distance to the junction), on a segment within
    /// HANDOFF_XTRACK_MAX_SEG_DEG of the runway, and not into a sharp turn (pulling back onto a
    /// short straight before a hook makes the turn late and wide: KDTW 27R V2). Bounded far below
    /// the bearing-to-node pan those sources exist to avoid; a few metres of wobble stays inside
    /// the tone's silent band. 0 when none of that applies.
    /// </summary>
    private double ExitCrossTrackPull(double lat, double lon)
    {
        if (!(_rolloutHandoffActive || _postHighSpeedExitMinBearing > 0.0) || _rolloutRunway == null
            || _route == null || _currentSegmentIndex >= _route.Segments.Count)
            return 0.0;
        var currentSeg = _route.Segments[_currentSegmentIndex];
        bool sharpTurnAhead = _rolloutExit?.RequiresTurnBack == true
            || (_currentSegmentIndex + 1 < _route.Segments.Count
                && Math.Abs(NormalizeAngle(_route.Segments[_currentSegmentIndex + 1].BearingDegrees
                                           - currentSeg.BearingDegrees)) > HANDOFF_XTRACK_MAX_SEG_DEG);
        if (sharpTurnAhead
            || Math.Abs(NormalizeAngle(currentSeg.BearingDegrees - _rolloutRunwayHeadingTrue)) > HANDOFF_XTRACK_MAX_SEG_DEG)
            return 0.0;
        AlongTrackToSegmentEnd(lat, lon, currentSeg, out double alongRemM, out double crossM);
        if (alongRemM < 0.0 || alongRemM > currentSeg.DistanceMeters) return 0.0;
        return -Math.Clamp(crossM * HANDOFF_XTRACK_DEG_PER_M, -HANDOFF_XTRACK_MAX_DEG, HANDOFF_XTRACK_MAX_DEG);
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
        // only one callout fires per frame — no stacking. A segment first reached already
        // inside a DEEPER tier (a short hold segment, or the 15 s lead shrinking as the
        // aircraft slows) speaks only that tier: firing the outer one and then the next a
        // frame later cut the first off and repeated the same distance ("in 55 metres." →
        // "in 55 metres. Slow down." 0.1 s apart; VirtualPilot 2026-09-25, LTFM B9B).
        if (distFeet < stopDistFt) { _holdShortOuterAnnounced = true; _holdShortSlowDownAnnounced = true; }
        else if (distFeet < slowDownDistFt) _holdShortOuterAnnounced = true;
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

        if ((MSFSBlindAssist.Utils.SimClock.UtcNow - _lastSpeedWarningTime).TotalSeconds < SPEED_WARNING_COOLDOWN_SEC)
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
            SpeakNow("Slow for sharp turn.");
            _lastSpeedWarningTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
        }
        else if (normalTurnComing && _lastGroundSpeedKts > MAX_TAXI_SPEED_TURN_KTS)
        {
            SpeakNow("Slow for turn.");
            _lastSpeedWarningTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
        }
        else if (!normalTurnComing && !sharpTurnComing && _lastGroundSpeedKts > MAX_TAXI_SPEED_STRAIGHT_KTS)
        {
            SpeakNow($"Taxi speed, {(int)_lastGroundSpeedKts} knots.");
            _lastSpeedWarningTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
        }
    }

    /// <summary>
    /// Runway incursion guard. Two cases, different wording:
    ///   1. Aircraft is approaching a hold-short node that lies ON the planned route
    ///      → "Crossing runway X" (informational — cleared to cross per ATC), or "Passing the
    ///      runway X hold line" when the route does not then go onto runway pavement (an
    ///      approach or ILS-area line: KDTW F), and nothing at all for the landed runway's own
    ///      hold line while vacating it (IsVacatingLandedRunwayHold: LROP 08R D).
    ///   2. Aircraft is approaching a hold-short node OFF the planned route
    ///      → "Warning: approaching runway X, off route" (red flag — wrong turn)
    /// The scheduled-hold-short (user-checked) case is silent here because
    /// CheckHoldShortCountdown owns those callouts.
    /// "Approaching", for case 2, is the node being AHEAD on the aircraft's own heading line
    /// (RunwayIncursionWatch.IsApproaching), not merely within the 40 m radius: at KMEM every
    /// hold line along taxiway M sits 34-38 m from M's centreline, and by proximity alone a
    /// pilot correctly taxiing M heard the warning at every connector. On a route it is also
    /// withheld while the aircraft is TRACKING the route (IsTrackingRoute) and while it is not
    /// closing on the line at INCURSION_WARN_MIN_CLOSING_KTS — both measured with
    /// tools/VirtualPilot (docs/virtual-pilot.md): a pilot rounding a junction wide of its node
    /// (KORD G/G2) or standing short of a hold line after "Continue" (KMSP C6) is on the route.
    /// </summary>
    private void CheckRunwayIncursion(double lat, double lon, double headingTrue)
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
        // The cooldown is a pure TIME check. It used to also require _lastIncursionWarnedNodeId
        // != -1, which meant any path that re-armed the node id disarmed the cooldown with it —
        // and this callout interrupts, so it could fire on the very next position frame and cut
        // off whatever had just been spoken. That is exactly what an accepted recalculation does:
        // it re-arms the id and then speaks "Route changed. Now via Q, A, crossing runways 28R
        // and 10L", which the bare "Crossing runway 28R." would truncate one frame later. Every
        // other reset site (LoadRoute, StopGuidance) clears the timestamp to MinValue as well.
        //
        // The cooldown no longer DROPS a real off-route warning, it QUEUES it (below): after an accepted
        // recalculation the window ran out only after the aircraft had reached the line, so a wrong turn
        // the recalculation itself did not fix rolled up to a runway in silence (VirtualPilot wrong turns,
        // LEMD Gate 384 → 18R toward 36L at Z-4: re-routed at 51 m, the line reached unwarned). Queued, the
        // warning follows "Route changed …" instead of cutting it off. Planned-crossing lines stay silent
        // inside the window, as before.
        bool inIncursionCooldown =
            (MSFSBlindAssist.Utils.SimClock.UtcNow - _lastIncursionWarningTime).TotalSeconds < INCURSION_WARNING_COOLDOWN_SEC;
        // The HS node-IDs on the planned route ("crossing" announcements fire for them; "off
        // route" does not). The WHOLE route, not just what is left of it: a line the aircraft has
        // just crossed drops out of a "remaining" set the moment it is passed, and the aircraft is
        // still beside it — it read as an off-route line (VirtualPilot, 2026-09-18). The route's
        // start node counts too when it is the LOADED route's start (a hold node the pilot is
        // standing at), never a recalculated route's — that one is wherever the aircraft happened
        // to be, and counting it lost real wrong-turn warnings. When _route is null (after a
        // landing-exit arrival, before the pilot opens the taxi planner) the set stays empty and
        // every approaching hold-short node triggers the "off route" warning — exactly what we
        // want. Cached: rebuilt only when _route's reference or _currentSegmentIndex changes.
        if (!ReferenceEquals(_onRouteHsSourceRoute, _route) ||
            _onRouteHsSourceSegmentIndex != _currentSegmentIndex)
        {
            var set = new HashSet<int>();
            if (_route != null)
            {
                if (_route.Segments.Count > 0 && _route.Segments[0].FromNode is { } startNode
                    && startNode.NodeId == _loadedRouteStartNodeId
                    && (startNode.Type == TaxiNodeType.HoldShort || startNode.Type == TaxiNodeType.ILSHoldShort))
                    set.Add(startNode.NodeId);
                for (int i = 0; i < _route.Segments.Count; i++)
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
        // Per-graph HS/ILS-HS candidate list, cached. A NEW graph is a new airport with its own
        // node numbering (ids restart at 1 per graph), so the per-node memory — the warned-node
        // latch and the withheld-log set — goes with it.
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
            // Everything the guard knows about a node that depends only on the map — the paths
            // leading into it and the runway it guards — is built once here, not per frame.
            var shapes = RunwayPavement.BuildShapes(_graph.RunwayCenterlines);
            _incursionNodeGeometry.Clear();
            foreach (var node in list)
            {
                _incursionNodeGeometry[node.NodeId] = new IncursionNodeGeometry(
                    CollectPathsIntoNode(node, shapes),
                    RunwayIncursionWatch.GuardedRunway(node.Latitude, node.Longitude, shapes));
            }
            ResetIncursionNodeMemory();
        }
        // The loaded route's start node, while the aircraft is still standing at it: a hold node
        // the pilot is waiting at is not one they are approaching.
        int routeStartNodeId = _route != null && _route.Segments.Count > 0
            && _route.Segments[0].FromNode != null && _route.Segments[0].FromNode.NodeId == _loadedRouteStartNodeId
            ? _route.Segments[0].FromNode.NodeId : -1;
        // Judge every cached HS/ILS-HS node in range (not every graph node) and speak about the
        // nearest that is on the route or genuinely ahead — never simply the nearest: an abeam
        // node a few metres closer than the one dead ahead would otherwise shadow it
        // (RunwayIncursionWatch.Pick). A node that is not ahead is not latched, so it is judged
        // again next frame and the moment the aircraft turns toward it the warning fires.
        // Leaving a runway is not approaching one (RunwayIncursionWatch.IsLeavingThroughHoldLine).
        // The candidate list is a field, cleared per use, and only touched once a node is in
        // range: on the >99 % of frames with nothing in range this allocates nothing.
        _incursionCandidates.Clear();
        foreach (var node in _cachedHoldShortNodes!)
        {
            if (node.NodeId == scheduledHsNodeId)
                continue; // countdown owns this one
            double d = TaxiGraph.FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (d >= INCURSION_WARN_DISTANCE_M) continue;
            if (node.NodeId == routeStartNodeId && d < ROUTE_START_HOLD_IGNORE_M)
                continue;
            string name = !string.IsNullOrEmpty(node.HoldShortName) ? node.HoldShortName : "runway";
            bool onRoute = onRouteHsNodes.Contains(node.NodeId);
            bool approached = false;
            _incursionNodeGeometry.TryGetValue(node.NodeId, out var geometry);
            if (!onRoute && !RunwayIncursionWatch.IsLeavingThroughHoldLine(
                    lat, lon, node.Latitude, node.Longitude, geometry?.GuardedRunway))
            {
                double bearingToNode = NavigationCalculator.CalculateBearing(
                    lat, lon, node.Latitude, node.Longitude);
                approached = RunwayIncursionWatch.IsApproaching(d, bearingToNode, headingTrue);
                var pathsIntoNode = geometry?.PathsIntoNode ?? Array.Empty<RunwayIncursionWatch.PathSegment>();
                if (!approached)
                {
                    approached = RunwayIncursionWatch.IsApproachingAlongAPath(
                        lat, lon, headingTrue, node.Latitude, node.Longitude, pathsIntoNode);
                }
                // A turn onto a connector whose hold line is close to its junction is seen by
                // neither test until the nose is nearly round; the yaw rate UpdatePosition already
                // keeps (low-passed, right-positive) says where the turn is taking the aircraft.
                if (!approached)
                {
                    approached = RunwayIncursionWatch.IsApproachingWhileTurning(
                        lat, lon, headingTrue, _lastGroundSpeedKts * StartWarningChatterGate.MetersPerSecondPerKnot, _yawRateDegSec,
                        node.Latitude, node.Longitude, pathsIntoNode);
                }
                if (!approached && _incursionWithheldLoggedNodes.Add(node.NodeId))
                    _guidanceLog.Info(
                        $"Incursion warning withheld: {name} {d:F0} m away, bearing {bearingToNode:F0} vs heading {headingTrue:F0} — not ahead.");
            }
            _incursionCandidates.Add(new RunwayIncursionWatch.HoldShortCandidate(node.NodeId, d, onRoute, approached, name));
        }
        var pick = RunwayIncursionWatch.Pick(_incursionCandidates);
        if (pick == null || pick.Value.NodeId == _lastIncursionWarnedNodeId)
            return;
        string rwy = pick.Value.SpokenName;
        var pickNode = _graph.Nodes[pick.Value.NodeId];
        if (pick.Value.OnRoute && inIncursionCooldown)
            return;
        if (pick.Value.OnRoute)
        {
            // Planned crossing — informational, not a warning. Silent for the landed runway's own
            // hold line while vacating it; "Passing … hold line" for a line the route passes
            // without then going onto runway pavement.
            bool vacatingOwnRunway = _rolloutRunway != null && IsVacatingLandedRunwayHold(
                _isLandingExitRoute, _rolloutRunway.RunwayID, rwy,
                AbsLateralFromRunwayMeters(lat, lon, _rolloutRunway.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue),
                AbsLateralFromRunwayMeters(pickNode.Latitude, pickNode.Longitude, _rolloutRunway.StartLat, _rolloutRunway.StartLon, _rolloutRunwayHeadingTrue));
            if (!vacatingOwnRunway)
                SpeakNow(RouteReachesRunwayAfter(pick.Value.NodeId)
                    ? $"Crossing {rwy}."
                    : $"Passing the {rwy} hold line.");
        }
        else if (_route != null && IsTrackingRoute(lat, lon))
        {
            // Off the route's hold list but ON the route: a line beside the taxiway being followed,
            // or one behind a "Continue" the pilot stopped short of. Nothing latched — the moment
            // the aircraft leaves the route the warning can fire.
            return;
        }
        else if (_lastGroundSpeedKts * Math.Cos(
                     NormalizeAngle(NavigationCalculator.CalculateBearing(lat, lon, pickNode.Latitude, pickNode.Longitude)
                                    - headingTrue) * Math.PI / 180.0) < INCURSION_WARN_MIN_CLOSING_KTS)
        {
            // Not closing on the line (stopped, or moving across it): nothing to warn about yet.
            return;
        }
        else if (inIncursionCooldown && !_incursionCooldownFromRecalc)
        {
            // Inside the window a previous warning opened: the earlier rule, nothing said.
            return;
        }
        else if (inIncursionCooldown)
        {
            QueueSpeech($"Warning: approaching {rwy}, off route.");
        }
        else
        {
            SpeakNow($"Warning: approaching {rwy}, off route.");
        }
        _lastIncursionWarnedNodeId = pick.Value.NodeId;
        _lastIncursionWarningTime = MSFSBlindAssist.Utils.SimClock.UtcNow;
        _incursionCooldownFromRecalc = false;
        // Re-arm the once-per-node withheld log for this node: a later pass that withholds it
        // again is a new judgement and deserves its own line.
        _incursionWithheldLoggedNodes.Remove(pick.Value.NodeId);
    }

    /// <summary>
    /// True while the aircraft is ON its route and heading along it: within
    /// ON_ROUTE_TRACKING_M of the current or an adjacent segment, with the heading within
    /// ON_ROUTE_TRACKING_HDG_DEG of that segment's bearing. Used only to silence the
    /// off-route runway warning for hold lines beside the route (CheckRunwayIncursion).
    /// </summary>
    private bool IsTrackingRoute(double lat, double lon)
    {
        if (_route == null || _route.Segments.Count == 0) return false;
        // Three segments back, not one: a pilot stops SHORT of a hold line (KMSP C6: 18 m), and
        // "Continue" moves the index past the hold segment while the aircraft is still on it,
        // so the lines around the hold read as off-route (VirtualPilot 2026-09-18).
        int from = Math.Max(0, _currentSegmentIndex - ON_ROUTE_TRACKING_SEGMENTS_BACK);
        int to = Math.Min(_route.Segments.Count - 1, _currentSegmentIndex + 1);
        for (int i = from; i <= to; i++)
        {
            var seg = _route.Segments[i];
            if (seg.DistanceMeters < 1.0) continue;
            // TaxiRouteSegment declares both nodes `null!`; every neighbouring scan in
            // this file guards them, and this one runs inside CheckRunwayIncursion on the
            // per-frame position handler, where a NullReferenceException would take the
            // whole frame with it.
            if (seg.FromNode == null || seg.ToNode == null) continue;
            double d = Navigation.LandingExitPathFollow.DistanceToPathMeters(
                new[] { (seg.FromNode.Latitude, seg.FromNode.Longitude), (seg.ToNode.Latitude, seg.ToNode.Longitude) },
                lat, lon);
            if (d <= ON_ROUTE_TRACKING_M
                && Math.Abs(NormalizeAngle(seg.BearingDegrees - _lastHeading)) <= ON_ROUTE_TRACKING_HDG_DEG)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does the route go onto any runway's pavement within <see cref="HOLD_LINE_RUNWAY_AHEAD_M"/>
    /// after passing this hold node? True when the node is not found on the route (keeps the old
    /// wording rather than guess).
    /// </summary>
    private bool RouteReachesRunwayAfter(int holdNodeId)
    {
        if (_route == null || _graph == null) return true;
        int k = -1;
        for (int i = Math.Max(0, _currentSegmentIndex - ON_ROUTE_TRACKING_SEGMENTS_BACK); i < _route.Segments.Count; i++)
            if (_route.Segments[i].ToNode?.NodeId == holdNodeId) { k = i; break; }
        if (k < 0) return true;
        double run = 0;
        for (int j = k + 1; j < _route.Segments.Count && run <= HOLD_LINE_RUNWAY_AHEAD_M; j++)
        {
            var n = _route.Segments[j].ToNode;
            if (n != null && RouteRunwayCrossings.RunwayUnder(_graph.RunwayCenterlines, n.Latitude, n.Longitude) != null)
                return true;
            run += _route.Segments[j].DistanceMeters;
        }
        return false;
    }

    private const double HOLD_LINE_RUNWAY_AHEAD_M = 150.0;

    /// <summary>Virtual-pilot harness only: skip the reversed-runway-entry re-route (TryForwardFacingRunwayEntry) for A/B measurement. Never set in the app.</summary>
    internal static bool LegacyReversedEntryForHarness;

    private const double ON_ROUTE_TRACKING_M = 12.0;
    private const double ON_ROUTE_TRACKING_HDG_DEG = 30.0;
    private const int ON_ROUTE_TRACKING_SEGMENTS_BACK = 3;

    /// <summary>
    /// Forgets which hold-short nodes the incursion guard has spoken or logged about: the
    /// warned-node latch AND the once-per-node "Incursion warning withheld" log set, always
    /// together (a new route, a recalculation, a stop, or a new graph whose node ids restart).
    /// The only place the latch is re-armed — <c>IncursionGuardResetTests</c>: the accepted
    /// recalculation once re-armed the latch alone and left the log set holding the old route's
    /// withheld nodes.
    /// </summary>
    private void ResetIncursionNodeMemory()
    {
        _lastIncursionWarnedNodeId = -1;
        _incursionWithheldLoggedNodes.Clear();
    }

    /// <summary>
    /// Builds every segment of every path that leads into a
    /// hold-short node, for <see cref="RunwayIncursionWatch.IsApproachingAlongAPath"/>: from each
    /// incident edge, back through degree-two nodes for up to
    /// <see cref="RunwayIncursionWatch.PathLookBackMetres"/>, so a fillet drawn as micro-bend
    /// segments is seen whole — on the TAXIWAY side only: a chain whose first node beyond the
    /// hold line is on runway pavement is the runway side, and an aircraft on it is leaving the
    /// runway, not approaching it. Called once per node when the hold-short cache is built for a
    /// graph, never per frame.
    /// </summary>
    private RunwayIncursionWatch.PathSegment[] CollectPathsIntoNode(
        TaxiNode node, IReadOnlyList<RunwayShape> runwayShapes)
    {
        var into = new List<RunwayIncursionWatch.PathSegment>();
        if (_graph == null || !_graph.Adjacency.TryGetValue(node.NodeId, out var incident)) return into.ToArray();
        const int MaxStepsPerChain = 8;
        foreach (var first in incident)
        {
            var near = node;
            int farId = first.FromNodeId == node.NodeId ? first.ToNodeId : first.FromNodeId;
            double widthFeet = first.WidthFeet;
            double walked = 0;
            if (_graph.Nodes.TryGetValue(farId, out var beyond)
                && RunwayPavement.IsOnPavement(beyond.Latitude, beyond.Longitude, runwayShapes))
                continue; // the runway side of the hold line
            for (int step = 0; step < MaxStepsPerChain; step++)
            {
                if (!_graph.Nodes.TryGetValue(farId, out var far) || far.NodeId == node.NodeId) break;
                into.Add(new RunwayIncursionWatch.PathSegment(
                    far.Latitude, far.Longitude, near.Latitude, near.Longitude, widthFeet));
                walked += TaxiGraph.FastDistanceMeters(far.Latitude, far.Longitude, near.Latitude, near.Longitude);
                if (walked >= RunwayIncursionWatch.PathLookBackMetres) break;
                // Continue only through a plain degree-two node (a bend, not a junction).
                if (!_graph.Adjacency.TryGetValue(far.NodeId, out var farEdges) || farEdges.Count != 2) break;
                var onward = farEdges[0].FromNodeId == near.NodeId || farEdges[0].ToNodeId == near.NodeId
                    ? farEdges[1] : farEdges[0];
                int nextId = onward.FromNodeId == far.NodeId ? onward.ToNodeId : onward.FromNodeId;
                if (nextId == near.NodeId) break;
                widthFeet = onward.WidthFeet;
                near = far;
                farId = nextId;
            }
        }
        return into.ToArray();
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

    /// <summary>
    /// Holds at the route's start node (<see cref="TaxiRoute.StartHoldRunway"/>): tone paused,
    /// <c>HoldShort</c> on segment 0, and the hold sentence published for exactly one delivery. It is
    /// also the Repeat-last instruction, so Ctrl+Y replays the hold rather than an older callout.
    /// </summary>
    // The runways THIS hold still guards, and how far through them Continue has got
    // (PR #238 deferred finding §6, implemented on the owner's ruling). Empty for the ordinary
    // single-runway hold, which is what keeps that path byte-identical. Derived from the stop's own
    // label by Navigation.RunwayHoldStages, so nothing new has to survive a route mutation; the only
    // new state is this list and its index, and both are re-established on every hold entry.
    private IReadOnlyList<string> _holdStages = Array.Empty<string>();
    private int _holdStageIndex;

    /// <summary>
    /// The hold sentence for a stop, staged when it guards more than one runway. Records the stages
    /// on the way past, so <see cref="ContinuePastHoldShort"/> knows how many clearances are still
    /// outstanding.
    /// </summary>
    private string ComposeHoldEntry(string? holdShortLabel)
    {
        _holdStages = Navigation.RunwayHoldStages.From(holdShortLabel);
        _holdStageIndex = 0;
        return _holdStages.Count > 0
            ? Navigation.RunwayHoldStages.ComposeHold(holdShortLabel, _holdStages[0])
            : Navigation.RouteRunwayCrossings.ComposeHoldShortInstruction(holdShortLabel);
    }

    private void EnterStartHold()
    {
        _steeringTone.Pause();
        SetState(TaxiGuidanceState.HoldShort);
        string cue = ComposeHoldEntry(_route?.StartHoldRunway);
        LastRouteStartHoldCue = cue;
        _lastInstruction = cue;
    }

    private void HandleHoldShort(TaxiRouteSegment holdShortSeg)
    {
        _steeringTone.Pause();
        SetState(TaxiGuidanceState.HoldShort);
        AnnounceInstruction(ComposeHoldEntry(holdShortSeg.HoldShortRunway));
    }

    public void ContinuePastHoldShort()
    {
        lock (_stateLock)
        {
        // Paired-runway crossing (VATSIM gap analysis 2026-08-31, P7): under the
        // FAA's ≤1,300 ft exception one clearance crosses BOTH parallels ("cross
        // runways 28 Left and 28 Right at Foxtrot" — KSFO/KSEA/KLAX), while this
        // route carries an auto hold-short at EACH, commanding a stop in the gap
        // between two active runways. The first Continue offers the pairing out
        // loud; a second Continue press inside the window is the pilot's explicit
        // "cleared across both" and clears the next crossing's stop. Never
        // automatic — an un-pressed second crossing keeps its full countdown.
        // A press before the window opens is ignored and keeps the offer standing (it falls through to
        // the HoldShort guard below, which returns: the aircraft is already taxiing).
        if (_state == TaxiGuidanceState.Taxiing && _route != null
            && _pairedCrossingSegIdx >= 0
            && Navigation.PairedCrossingHoldWindow.Accepts(
                MSFSBlindAssist.Utils.SimClock.UtcNow, _pairedCrossingWindowFrom, _pairedCrossingWindowUntil))
        {
            int segIdx = _pairedCrossingSegIdx;
            _pairedCrossingSegIdx = -1;
            if (segIdx < _route.Segments.Count && _route.Segments[segIdx].IsHoldShortPoint)
            {
                var pairedSeg = _route.Segments[segIdx];
                string label = string.IsNullOrEmpty(pairedSeg.HoldShortRunway)
                    ? "the next runway" : pairedSeg.HoldShortRunway;
                pairedSeg.IsHoldShortPoint = false;
                pairedSeg.HoldShortRunway = "";
                AnnounceInstruction($"Also cleared across {label}. No stop between the runways.");
            }
            return;
        }

        if (_state != TaxiGuidanceState.HoldShort || _route == null) return;

        // ONE CONTINUE PER RUNWAY at a stop that guards more than one (PR #238 deferred finding §6).
        // The label merges when two runways resolve to the same stop ("runway 09 and runway 01") and
        // one segment is tagged, so a single Continue used to authorise crossing BOTH — against this
        // manager's own stated rule that explicit crossing clearance is required for EACH runway,
        // because controllers issue them one at a time and an aircraft must have crossed the
        // previous runway before the next clearance is issued.
        //
        // The aircraft does NOT move on this press: the state stays HoldShort and the tone stays
        // paused, which is why the sentence says "Still holding" rather than "Continuing".
        //
        // NOT applied at the DESTINATION hold (_holdShortAtDestination): a Continue there is a
        // lineup or takeoff clearance and hands over to the lineup state machine, a different
        // meaning from a crossing clearance. A destination stop that also guards another runway
        // keeps today's single-press behaviour — a deliberate limit, recorded rather than guessed
        // at.
        if (!_holdShortAtDestination && _holdStageIndex + 1 < _holdStages.Count)
        {
            string cleared = _holdStages[_holdStageIndex];
            _holdStageIndex++;
            string next = _holdStages[_holdStageIndex];
            string staged = Navigation.RunwayHoldStages.ComposeAdvance(cleared, next);
            LastRouteStartHoldCue = _route.StartHoldRunway != null && _currentSegmentIndex == 0
                ? staged
                : LastRouteStartHoldCue;
            AnnounceInstruction(staged);
            return;
        }
        _holdStages = Array.Empty<string>();
        _holdStageIndex = 0;

        // Belt-and-braces (PR #238 review, Important 1): SetState's own leaving-Taxiing hook
        // already cleared _pendingTaxiwayAnnouncement the moment guidance entered this hold
        // -- reaching HoldShort at all requires having left Taxiing first -- so this is
        // always a no-op today. Kept explicit anyway, right where _lastAnnouncedTaxiway is
        // about to be re-set below for the ordinary resume: this method already owns
        // speaking the taxiway name for the leg guidance is about to resume on, so a
        // deferred copy of an EARLIER name would be redundant by construction even if some
        // future change ever let one survive this far.
        _pendingTaxiwayAnnouncement = null;

        // Leaving a start hold: it is spent. _holdShortAtDestination is false for it, so execution
        // falls through to the ordinary resume on segment 0 below. The route-start turn cue was
        // never spoken while held (its per-frame one-shot runs only on a taxiing frame), so unless
        // the form already folded it into its standstill utterance it rides in the resume sentence
        // here — left for the next frame it would interrupt "Continuing." (a Progressive leg that
        // begins at a hold line, a landing-exit re-route).
        string? startTurnCue = null;
        if (_route.StartHoldRunway != null && _currentSegmentIndex == 0)
        {
            _route.StartHoldRunway = null;
            LastRouteStartHoldCue = null;
            startTurnCue = LastRouteReachWarning == null ? ConsumeInitialTurnCue() : null;
        }

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

            // FULL-LENGTH BACKTRACK DEPARTURE: the route held short of an INTERMEDIATE
            // entrance, so Continue enters the runway and backtracks toward the full-length
            // threshold rather than lining up here. UpdateBacktrackDeparture hands to
            // LiningUp for the 180 degree turnaround once the threshold is reached.
            if (_backtrackDeparture)
            {
                SetState(TaxiGuidanceState.BacktrackDeparture);
                _backtrackDepApproachAnnounced = false;
                _steeringTone.Resume();
                _smoothedHeadingError = 0;
                _headingErrorInitialized = false;
                int backHdg = (int)Math.Round((_lineupHeadingMag + 180.0) % 360.0);
                int depHdg = (int)Math.Round(_lineupHeadingMag);
                AnnounceInstruction(
                    $"Entering {_destinationName}. Backtrack to full length, heading {backHdg}, " +
                    $"then turn around to heading {depHdg}.");
                return;
            }

            SetState(TaxiGuidanceState.LiningUp);
            _lineupAnnouncedAligned = false;
            _steeringTone.Resume();
            BeginLineupStub();

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
            // Still short of the runway on the entry taxiway, heading along it: the turn comes at
            // the runway, not here — "Turn left" at a set-back hold line reads as "turn now".
            if (turnDir.Length > 0 && _lineupStubActive && _route?.RunwayEntryStub is { Count: >= 2 } st
                && Math.Abs(NormalizeAngle(NavigationCalculator.CalculateBearing(
                       st[0].Lat, st[0].Lon, st[^1].Lat, st[^1].Lon) - _lastHeading)) < 45)
                turnDir = headingError > 0 ? " Continue onto the runway, then turn right." : " Continue onto the runway, then turn left.";

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

        // Paired-crossing offer: is the NEXT hold-short a runway crossing within
        // PAIRED_CROSSING_MAX_M (≈ the FAA's 1,300 ft paired-runway rule) of here,
        // and not the destination hold (never the final segment)? If so, name it
        // and open the double-press window.
        string pairedOffer = "";
        _pairedCrossingSegIdx = -1;
        {
            double aheadM = 0;
            for (int i = _currentSegmentIndex; i < _route.Segments.Count - 1; i++)
            {
                aheadM += _route.Segments[i].DistanceMeters;
                if (aheadM > PAIRED_CROSSING_MAX_M) break;
                var s = _route.Segments[i];
                if (s.IsHoldShortPoint && s.HoldShortRunway != null &&
                    s.HoldShortRunway.StartsWith("runway ", StringComparison.OrdinalIgnoreCase))
                {
                    // "Cleared across both" can only mean this runway and ONE more: a stop shared by two
                    // runways ("runway 28R and runway 01L") is never offered, or a double press would clear a
                    // third runway the pilot never heard named as part of the pair.
                    if (Navigation.RouteRunwayCrossings.ExtractRunwayDesignators(s.HoldShortRunway).Count != 1)
                        break;
                    _pairedCrossingSegIdx = i;
                    (_pairedCrossingWindowFrom, _pairedCrossingWindowUntil) =
                        Navigation.PairedCrossingHoldWindow.Open(MSFSBlindAssist.Utils.SimClock.UtcNow);
                    pairedOffer = $"Next hold short of {s.HoldShortRunway} in {FormatDistance(aheadM)}. " +
                                  "Press continue again if cleared across both.";
                    break;
                }
            }
        }

        if (_currentSegmentIndex < _route.Segments.Count)
        {
            var seg = _route.Segments[_currentSegmentIndex];
            string taxiway = !string.IsNullOrEmpty(seg.TaxiwayName) ? $"Taxiway {seg.TaxiwayName}. " : "";
            // Update the announced-taxiway tracker so AdvanceToNearestSegment doesn't
            // redundantly re-announce the same taxiway a few frames later.
            _lastAnnouncedTaxiway = seg.TaxiwayName ?? "";
            AnnounceInstruction(startTurnCue != null
                ? $"Continuing. {startTurnCue} {pairedOffer}".TrimEnd()
                : $"Continuing. {taxiway}{pairedOffer}");
        }
        else
        {
            AnnounceInstruction($"Continuing. {pairedOffer}");
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
            // The destination hold is never staged (a Continue here is a lineup/takeoff clearance,
            // not a crossing clearance) — drop any stages a previous hold left, so nothing can leak
            // into this one. ContinuePastHoldShort's own !_holdShortAtDestination guard is the rule;
            // this is the state hygiene beside it.
            _holdStages = Array.Empty<string>();
            _holdStageIndex = 0;

            string rwy = _destinationName;
            // When the stop node IS the pilot's picked named holding point, say so —
            // "…at holding point A1" — whether it got there via a navdata hold node
            // or the Pass-1.5 picked-point truncation (TruncateToHoldShort).
            string atPoint = "";
            if (_holdingPointName.Length > 0 && _holdingPointHoldNodeId != 0
                && _route != null && _route.Segments.Count > 0
                && _route.Segments[^1].ToNode?.NodeId == _holdingPointHoldNodeId)
                atPoint = $" at holding point {_holdingPointName}";
            // The lineup rolls onto another runway first (intersecting runways at the threshold):
            // name it in the one hold sentence, since nothing after Continue would.
            if (_route != null)
                foreach (string other in _route.RunwayEntryStubAlsoOn)
                    rwy = RouteRunwayCrossings.ComposeSharedLabel(rwy, other) ?? rwy;
            AnnounceInstruction(_backtrackDeparture
                ? $"Stop. Hold short of {rwy}{atPoint}. Press continue when cleared to enter and backtrack."
                : $"Stop. Hold short of {rwy}{atPoint}. Press continue when cleared.");
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
            if (_landingExitVacatedEarly)
            {
                // _landingExitVacatedEarlyPlannedName, when set, is the exit the pilot was
                // actually steered toward and missed — captured before the early-vacate swap
                // repointed _rolloutExit (and therefore _route.DestinationName) at a substitute
                // exit. Without it, a substitute whose own handoff route then failed the
                // reachability guard would have this closure name the substitute the pilot is
                // standing on/near, not the exit they left the runway short of. Scoped to this
                // branch only — the other three closures below describe a SUCCESSFUL arrival at
                // whatever _rolloutExit/_route now name, and applying the planned-exit override
                // there would misname the substitute exit the pilot actually reached.
                string vacatedEarlyExitName = _landingExitVacatedEarlyPlannedName ?? exitName;
                // The aircraft is off the runway but not at the planned exit, and nothing
                // could be matched to where it actually went. Never route back to the
                // planned exit from here: at KSEA 34L that produced a 1,678 m loop up the
                // parallel taxiway and back down toward the runway, because the taxi graph
                // carries no runway edges and that is the only path between two exits.
                AnnounceInstruction(
                    $"You have left the runway short of {vacatedEarlyExitName}. Exit guidance ended. " +
                    $"Stop and hold position, then open the taxi planner to set a route " +
                    $"to your gate.");
            }
            else if (_landingExitMissed)
            {
                // The missed-vacate backstop concluded guidance because the aircraft took the
                // wrong branch at the exit junction. Falling through to the normal "Off the
                // runway at E" wording would read as a SUCCESSFUL vacate — the opposite of
                // what happened — so this case must say so in its own words.
                AnnounceInstruction(
                    $"You have passed the {exitName} vacate point. Exit guidance ended. " +
                    $"Stop and hold position, then open the taxi planner to set a route to your gate.");
            }
            else if (_landingExitRouteUnreachable)
            {
                // Off the runway, but nothing established WHERE along it — the guard fires
                // for an at-the-exit and a past-the-exit vacate as well as an early one. Any
                // positional claim here would be a guess, and a blind pilot told they are
                // somewhere they are not may manoeuvre to "correct" a position that was fine.
                // State only what is certain: guidance has ended and they should hold.
                AnnounceInstruction(
                    "Exit guidance ended: no usable route from here. Stop and hold position, " +
                    "then open the taxi planner to set a route to your gate.");
            }
            else if (_landingExitOffPavement)
            {
                AnnounceInstruction(
                    $"Off the runway at {exitName}. Stop and hold position. " +
                    $"Open the taxi planner to set a route to your gate.");
            }
            else
            {
                // The handoff destination could not be walked clear of the runway
                // (no mapped taxiway past it, or it sits on a crossing runway's
                // pavement). "Continue ahead until clear" must be flyable by TONE,
                // not just spoken — keep the steering tone alive as a heading-hold
                // on the exit direction and conclude only once the aircraft is
                // laterally clear of every runway corridor. The tone Stop() above
                // is undone by re-resuming here; UpdateArrivedRunwayClearing owns
                // the per-frame drive from now on.
                double clearingBrg = _route != null && _route.Segments.Count > 0
                    ? _route.Segments[^1].BearingDegrees
                    : (_rolloutExit != null && _rolloutExit.ExitBearingTrue > 0.0
                        ? (_rolloutExit.ExitBearingTrue == 360.0 ? 0.0 : _rolloutExit.ExitBearingTrue)
                        : double.NaN);
                if (!double.IsNaN(clearingBrg))
                {
                    _arrivedRunwayClearing = true;
                    _arrivedClearingBearingDeg = clearingBrg;
                    _arrivedClearingStartLat = 0.0;   // stamped on the first clearing frame
                    _arrivedClearingStartLon = 0.0;
                    _headingErrorInitialized = false;
                    _smoothedHeadingError = 0.0;
                    // The generic arrival Stop() above disposed the generator —
                    // Resume() is not enough, re-Start with the last-used settings.
                    if (!_steeringToneSuppressed)
                        _steeringTone.Start(_lastToneWaveform, _lastToneVolume);
                    AnnounceInstruction(
                        $"Taxiway data ends at {exitName}. You may still be on the runway — " +
                        $"follow the tone ahead until clear.");
                    // The state is already Arrived (set above), so MainForm has stopped the
                    // position feed that UpdateArrivedRunwayClearing is driven from. Without this
                    // the clearing phase never received a frame: the tone was re-started and held
                    // its first pan, and nothing more was said (taxi-landing-port review, finding 3).
                    PositionStreamRequired?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    AnnounceInstruction(
                        $"Taxiway data ends at {exitName}. You may still be on the runway — " +
                        $"continue ahead until clear, then open the taxi planner to set a route to your gate.");
                }
            }
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
        // Clear the docking-driven suppression latch. It is normally refreshed
        // every frame from docking's IsActive, but StopGuidance also stops the
        // position feed — so a latch left true here had no frames left to heal it,
        // and the NEXT route could start its tone silently (nothing else resets
        // it before the first frame's SetSteeringToneSuppressed call).
        _steeringToneSuppressed = false;
        _route = null;
        _graph = null;
        _dataProvider = null;
        _currentSegmentIndex = 0;
        _positionInitialized = false;
        _headingErrorInitialized = false;
        _initialTurnCueAnnounced = false;
        _initialTurnPending = false;
        LastRouteStartHoldCue = null;
        _holdStages = Array.Empty<string>();
        _holdStageIndex = 0;
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
        _lineupStubActive = false;
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
        // A deferred taxiway-change name from this session must never survive into the
        // next one and be spoken (or wrongly discarded as stale) against a route it was
        // never about.
        _pendingTaxiwayAnnouncement = null;
        _holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;
        _parkingAnnounce50 = _parkingAnnounce20 = _parkingAnnounce10 = false;
        ResetIncursionNodeMemory();
        // Reset cooldowns so a freshly-loaded route after Stop gets prompt warnings
        // instead of inheriting a stale cooldown from the prior session.
        _lastRecalculationTime = DateTime.MinValue;
        // A new guidance session must be able to speak its own first reachability
        // refusal, not stay silent because a prior route already spoke an
        // identically-keyed one (ReachabilityRefusalGate).
        _lastReachabilityRefusalKey = null;
        _lastSpeedWarningTime = DateTime.MinValue;
        _lastIncursionWarningTime = DateTime.MinValue;
        _offRouteSince = DateTime.MinValue;
        _hasJoinedRoute = false;
        _minPerpWhileUnjoinedM = double.MaxValue;
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
        _rolloutAirborneHold = false;
        ResetLandingExitOutcomeFlags();   // fresh session: no failed handoff to remember
        _rolloutRunway = null;
        _rolloutAllExits = new List<Navigation.LandingExit>();
        ResetRolloutApproachLatches();
        _rolloutNoExitMode = false;
        _rolloutHandoffActive = false;
        _rolloutEnd1500Announced = false;
        _rolloutEnd500Announced = false;
        _rolloutEnd100Announced = false;
        _lahsoHold = null;
        _lahso1500Announced = _lahso500Announced = _lahsoStopAnnounced = _lahsoPassedAnnounced = false;
        _rolloutStoppedNoticeGiven = false;
        _rolloutCrossingDeclinedUtc = DateTime.MinValue;
        _rolloutCrossingDeclineAnnounced = false;
        ResetOffPavementAlert();
        _pavementMap = null;
        _pavementMapGraph = null;
        _backtrackConnectionNodeId = 0;
        _backtrackApproachAnnounced = false;
        ResetPlannedBacktrack();
        _backtrackDeparture = false;
        _backtrackDepApproachAnnounced = false;
        _postHighSpeedExitMinBearing = 0.0;
        _arrivedRunwayClearing = false;
        _minPerpWhileUnjoinedM = double.MaxValue;
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

        // A taxiway-change name deferred by AnnounceOrDeferTaxiwayChange while the
        // start-warning chatter window was open must not outlive the Taxiing frame it was
        // deferred in. FlushPendingTaxiwayAnnouncement only ever runs from the Taxiing
        // branch of UpdatePosition, so once guidance leaves Taxiing that method never runs
        // again until (if ever) guidance returns to Taxiing -- a pending name held onto
        // across the gap would then be flushed against a world that has moved on (PR #238
        // review, Important 1). Concretely: a name deferred just before a mid-route
        // hold-short used to survive the WHOLE hold, sometimes minutes, and
        // ContinuePastHoldShort's own "Continuing. Taxiway X." resume sentence was then
        // stomped by that stale deferred name on the very next Taxiing frame -- a segment
        // just past a crossing hold-short routinely carries the SAME taxiway name (the
        // documented KBOS "N, hold short 15R, N" shape), so IsStillCurrent read the stale
        // name as still current and re-announced it, cutting the Continue confirmation off
        // mid-word at the moment the pilot is entering an active runway. Dropping it here is
        // safe and deliberate in every direction guidance can leave Taxiing: HoldShort and
        // LiningUp both speak their own opening sentence moments later, Arrived needs no
        // taxiway name at all, and ProgressiveHold speaks its own terminator sentence --
        // none of them owe the pilot the name that was waiting (this is also why a name
        // still pending at HandleArrival is correctly, not accidentally, lost outright:
        // see FlushPendingTaxiwayAnnouncement's own remarks and Minor 2 of the same review).
        if (prev == TaxiGuidanceState.Taxiing && newState != TaxiGuidanceState.Taxiing)
            _pendingTaxiwayAnnouncement = null;

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
            SpeakNow(
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
        var now = MSFSBlindAssist.Utils.SimClock.UtcNow;
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
        var now = MSFSBlindAssist.Utils.SimClock.UtcNow;
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

