using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// State machine states for taxiway guidance
/// </summary>
public enum GuidanceState
{
    /// <summary>No guidance active</summary>
    Idle,
    /// <summary>Waiting for user to select initial segment</summary>
    AwaitingSegmentSelection,
    /// <summary>Actively tracking locked segment</summary>
    SegmentLocked,
    /// <summary>Near junction, first warning given</summary>
    ApproachingJunction,
    /// <summary>Junction form shown, waiting for user input</summary>
    AwaitingJunctionSelection,
    /// <summary>Reached dead-end or parking</summary>
    AtDestination,
    /// <summary>Following a pre-built route (route-based guidance)</summary>
    FollowingRoute,
    /// <summary>Approaching a waypoint on the route</summary>
    ApproachingWaypoint,
    /// <summary>Deviated from the planned route</summary>
    Deviated,
    /// <summary>Guiding perpendicular to centerline (aircraft off taxiway)</summary>
    AligningToSegment,
    /// <summary>On centerline but outside segment bounds, moving to entry point</summary>
    AligningAlongSegment
}

/// <summary>
/// Represents a carrot (target) position for pursuit guidance.
/// The carrot is a point on the route ahead of the aircraft that the pilot steers toward.
/// </summary>
public struct CarrotPosition
{
    /// <summary>Latitude of the carrot point</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude of the carrot point</summary>
    public double Longitude { get; set; }

    /// <summary>Heading of the route at the carrot point (for turn anticipation)</summary>
    public double Heading { get; set; }

    /// <summary>Bearing from aircraft to carrot</summary>
    public double BearingFromAircraft { get; set; }

    /// <summary>Actual distance from aircraft to carrot in feet</summary>
    public double DistanceToCarrotFeet { get; set; }

    /// <summary>Which waypoint index the carrot is on (for multi-segment spans)</summary>
    public int WaypointIndex { get; set; }

    /// <summary>Whether the carrot is at the route destination (no more segments ahead)</summary>
    public bool IsAtDestination { get; set; }
}

/// <summary>
/// Represents a circular arc at a turn junction for smooth carrot projection.
/// When taxiway segments meet at an angle, this arc provides a smooth transition path.
/// </summary>
public struct TurnArc
{
    /// <summary>Latitude of the arc circle center</summary>
    public double CenterLat { get; set; }

    /// <summary>Longitude of the arc circle center</summary>
    public double CenterLon { get; set; }

    /// <summary>Arc radius in feet</summary>
    public double RadiusFeet { get; set; }

    /// <summary>Total arc length in feet</summary>
    public double ArcLengthFeet { get; set; }

    /// <summary>Start angle from center (radians, 0=East, CCW positive)</summary>
    public double StartAngleRad { get; set; }

    /// <summary>Turn angle in radians (positive = right/clockwise turn)</summary>
    public double TurnAngleRad { get; set; }

    /// <summary>Distance before turn node where arc begins (feet)</summary>
    public double EntryDistanceFeet { get; set; }

    /// <summary>Whether this arc is valid for projection (turn angle exceeds threshold)</summary>
    public bool IsValid { get; set; }
}

/// <summary>
/// Represents a recovery arc for smoothly returning to the taxiway centerline when off-track.
/// The arc provides a gentle curved path that merges back at a shallow angle.
/// </summary>
public struct RecoveryArc
{
    /// <summary>Latitude of the arc circle center</summary>
    public double CenterLat { get; set; }

    /// <summary>Longitude of the arc circle center</summary>
    public double CenterLon { get; set; }

    /// <summary>Arc radius in feet</summary>
    public double RadiusFeet { get; set; }

    /// <summary>Total arc length in feet</summary>
    public double ArcLengthFeet { get; set; }

    /// <summary>Start angle from center (radians, 0=East, CCW positive) - points to aircraft</summary>
    public double StartAngleRad { get; set; }

    /// <summary>Sweep angle in radians (signed: positive = clockwise arc)</summary>
    public double SweepAngleRad { get; set; }

    /// <summary>Latitude of merge point on centerline</summary>
    public double MergePointLat { get; set; }

    /// <summary>Longitude of merge point on centerline</summary>
    public double MergePointLon { get; set; }

    /// <summary>Whether this recovery arc is valid</summary>
    public bool IsValid { get; set; }
}

/// <summary>
/// Manages taxiway guidance for blind pilots
/// </summary>
public class TaxiwayGuidanceManager : IDisposable
{
    private readonly ScreenReaderAnnouncer _announcer;
    private TaxiwayGraph? _graph;
    private bool _isActive;

    // State machine
    private GuidanceState _state = GuidanceState.Idle;

    // Locked segment tracking (replaces nearest-segment approach)
    private TaxiwaySegment? _lockedSegment;
    private TaxiwayNode? _lockedTargetNode;  // Which end we're heading toward
    private string? _currentTaxiwayName;
    private double _lastAircraftHeading;
    private double _initialLatitude;
    private double _initialLongitude;

    // Audio tone generator for centerline guidance
    private AudioToneGenerator? _centerlineTone;
    private readonly HandFlyWaveType _toneWaveType;
    private readonly double _toneVolume;

    // Tracking state
    private DateTime _lastCenterlineAnnouncement = DateTime.MinValue;
    private DateTime _lastTaxiwayChangeAnnouncement = DateTime.MinValue;
    private TaxiwayNode? _lastJunctionNode;
    private bool _junctionPending;
    private bool _junctionFirstWarningGiven;  // Track if 150ft warning was announced
    private bool _relockSuggestionGiven;  // Track if 200ft relock suggestion was announced

    // Parking spot detection
    private List<ParkingSpotData> _parkingSpots = new();
    private ParkingSpotData? _currentParkingSpot;
    private DateTime _lastParkingAnnouncement = DateTime.MinValue;

    // Route-based guidance
    private TaxiRoute? _activeRoute;
    private bool _isOnRoute = true;
    private bool _waypointApproachAnnounced;
    private DateTime _lastDeviationAnnouncement = DateTime.MinValue;

    // Configuration constants
    private const int ANNOUNCEMENT_INTERVAL_MS = 500;
    private const double CENTERLINE_TOLERANCE_FEET = 15.0; // Within ±15 feet is "on centerline"
    private const double CENTERLINE_CHANGE_THRESHOLD_FEET = 10.0;
    private const double PAN_FULL_RANGE_FEET = 40.0; // ±40 feet for full left/right pan
    private const double SEGMENT_SEARCH_RADIUS_FEET = 200.0; // Max distance to search for segments
    private const double PARKING_ANNOUNCEMENT_INTERVAL_SECONDS = 5.0; // Interval for parking spot announcements

    // Junction detection (two-stage)
    private const double JUNCTION_FIRST_WARNING_FEET = 150.0; // First warning distance
    private const double JUNCTION_SHOW_FORM_FEET = 50.0; // Show junction selection form

    // Hold short detection
    private const double HOLD_SHORT_WARNING_FEET = 200.0; // First hold short warning
    private const double HOLD_SHORT_FINAL_FEET = 50.0; // Final hold short warning

    // Deviation thresholds
    private const double DEVIATION_SUGGEST_RELOCK_FEET = 200.0; // Suggest re-locking at this distance

    // Route-based guidance constants
    private const double WAYPOINT_APPROACH_FEET = 150.0;  // Announce upcoming turn
    private const double WAYPOINT_PASS_FEET = 30.0;       // Advance to next waypoint
    private const double ALONG_TRACK_TOLERANCE_FEET = 10.0; // Tolerance past segment end for advancement
    private const double ROUTE_DEVIATION_FEET = 100.0;    // Consider off-route
    private const double ROUTE_BACK_ON_FEET = 50.0;       // Consider back on route
    private const double DEVIATION_ANNOUNCEMENT_INTERVAL_SECONDS = 10.0;

    // Distance-increasing detection constants
    private const int DISTANCE_INCREASING_THRESHOLD = 10;     // ~1 second at 10Hz updates
    private const double DISTANCE_INCREASING_TOLERANCE = 5.0; // feet tolerance for noise

    // Alignment phase constants
    private const double ALIGNMENT_CENTERLINE_TOLERANCE_FEET = 15.0;  // Consider "on centerline"
    private const double ALIGNMENT_INITIAL_TRIGGER_FEET = 100.0;      // Same as mid-route - recovery arc handles 20-100ft
    private const double ALIGNMENT_MIDROUTE_TRIGGER_FEET = 100.0;     // Only trigger alignment mid-route if severely off

    // PID Controller constants for taxi steering guidance (legacy - kept for reference)
    private const double TAXI_PROPORTIONAL_GAIN = 0.15;     // Degrees per foot of cross-track error
    private const double TAXI_DERIVATIVE_GAIN = 1.0;        // Damping for rate of drift
    private const double TAXI_HEADING_GAIN = 0.5;           // Weight for heading alignment
    private const double TAXI_MAX_CORRECTION_DEGREES = 10.0; // Limit correction to ±10°
    private const double TAXI_ON_PATH_THRESHOLD = 0.5;      // Within 0.5° = "on path"
    private const double TAXI_CENTERLINE_CAPTURE = 5.0;     // Within 5 feet = captured

    // Carrot pursuit guidance constants
    private const double PURSUIT_GAIN = 1.0;                    // Degrees of correction per degree of bearing error
    private const double MIN_LOOK_AHEAD_FEET = 30.0;            // Minimum look-ahead distance
    private const double MAX_LOOK_AHEAD_FEET = 150.0;           // Maximum look-ahead distance
    private const double LOOK_AHEAD_TIME_SECONDS = 2.0;         // Seconds ahead at current speed
    private const double CAPTURE_RADIUS_FEET = 20.0;            // Distance to "capture" route for initial alignment
    private const double ON_TRACK_THRESHOLD_DEGREES = 3.0;      // Within 3° = "on track"
    private const double KNOTS_TO_FEET_PER_SECOND = 1.68781;    // Conversion factor

    // Arc projection constants (for smooth carrot projection through turns)
    private const double ARC_MIN_TURN_ANGLE_DEG = 20.0;         // Minimum turn angle to apply arc projection
    private const double ARC_MIN_RADIUS_FEET = 30.0;            // Minimum arc radius
    private const double ARC_MAX_RADIUS_FEET = 500.0;           // Maximum arc radius
    private const double LAT_TO_FEET = 364000.0;                // Feet per degree latitude (approximate)
    private const double LON_TO_FEET_BASE = 365000.0;           // Feet per degree longitude at equator

    // Recovery arc constants (for smooth return to centerline when off-track)
    private const double RECOVERY_ARC_THRESHOLD_FEET = 20.0;    // Enter recovery mode when > 20ft off centerline
    private const double RECOVERY_ARC_EXIT_FEET = 10.0;         // Exit recovery mode when < 10ft off centerline
    private const double RECOVERY_MERGE_ANGLE_DEGREES = 20.0;   // Gentle 20° merge angle to centerline
    private const double RECOVERY_MIN_RADIUS_FEET = 50.0;       // Minimum recovery arc radius
    private const double RECOVERY_MAX_RADIUS_FEET = 500.0;      // Maximum recovery arc radius

    // Heading-based guidance constants (used in alignment phase)
    private const double HEADING_CORRECTION_GAIN = 1.0;         // Degrees of correction per degree of heading error
    private const double HEADING_BLEND_DISTANCE_FEET = 100.0;   // Start blending toward next waypoint heading

    // Width-relative safety thresholds (for off-taxiway detection only)
    private const double DRIFTING_WIDTH_FACTOR = 0.8;           // 80% to edge = "drifting" warning
    private const double OFF_TAXIWAY_BUFFER_FEET = 15.0;        // Past edge + 15ft = "off taxiway" warning
    private const double SAFETY_WARNING_INTERVAL_SECONDS = 5.0; // Min interval between safety warnings

    // Guidance state tracking
    private DateTime _lastPositionUpdateTime = DateTime.MinValue;
    private int? _lastAnnouncedCorrectionDegrees = null;
    private double _lastGroundSpeedKnots = 10.0; // Default assumption

    // Distance-increasing detection state
    private double _lastDistanceToWaypoint = double.MaxValue;
    private int _distanceIncreasingCount = 0;

    // Turn anticipation state
    private bool _turnAnticipationAnnounced = false;

    // Alignment phase state
    private bool _alignmentAnnounced = false;
    private bool _isInitialAlignment = false;  // True only at route start, before reaching the taxiway

    // Width-relative safety warning state
    private DateTime _lastSafetyWarningTime = DateTime.MinValue;

    // Recovery arc state (for smooth return to centerline when off-track)
    private bool _inRecoveryMode = false;
    private RecoveryArc? _currentRecoveryArc = null;

    public bool IsActive => _isActive;
    public bool HasGraph => _graph != null;
    public string? CurrentAirport => _graph?.AirportIcao;
    public TaxiwaySegment? CurrentSegment => _lockedSegment;
    public TaxiwaySegment? LockedSegment => _lockedSegment;
    public bool IsJunctionPending => _junctionPending;
    public GuidanceState State => _state;
    public TaxiRoute? ActiveRoute => _activeRoute;
    public bool IsFollowingRoute => _activeRoute != null && _state == GuidanceState.FollowingRoute;
    public bool IsOnRoute => _isOnRoute;
    public TaxiwayGraph? Graph => _graph;

    public event EventHandler<bool>? GuidanceActiveChanged;
    public event EventHandler<JunctionEventArgs>? JunctionDetected;
    public event EventHandler<string>? HoldShortDetected;
    public event EventHandler<SegmentSelectionEventArgs>? SegmentSelectionRequired;

    public TaxiwayGuidanceManager(ScreenReaderAnnouncer announcer,
        HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.05)
    {
        _announcer = announcer;
        _toneWaveType = waveType;
        _toneVolume = volume;
        _centerlineTone = new AudioToneGenerator();
    }

    /// <summary>
    /// Loads the taxiway graph for an airport
    /// </summary>
    public bool LoadAirport(string databasePath, string airportIcao)
    {
        // Stop guidance if active
        if (_isActive)
        {
            StopGuidance();
        }

        _graph = TaxiwayGraph.Build(databasePath, airportIcao);

        if (_graph == null)
        {
            _announcer.AnnounceImmediate($"No taxiway data found for {airportIcao}");
            return false;
        }

        // Load parking spots for proximity detection
        LoadParkingSpots(databasePath, airportIcao);

        _announcer.AnnounceImmediate($"{airportIcao} loaded, {_graph.SegmentCount} taxi segments, {_parkingSpots.Count} parking spots");
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Loaded {airportIcao}: {_graph.SegmentCount} segments, {_graph.NodeCount} nodes, {_parkingSpots.Count} parking spots");

        return true;
    }

    /// <summary>
    /// Loads parking spots for the airport
    /// </summary>
    private void LoadParkingSpots(string databasePath, string airportIcao)
    {
        _parkingSpots.Clear();
        _currentParkingSpot = null;

        try
        {
            var provider = new TaxiwayDatabaseProvider(databasePath);
            int? airportId = provider.GetAirportId(airportIcao);

            if (airportId.HasValue)
            {
                _parkingSpots = provider.GetParkingSpotsWithRadius(airportId.Value);
                System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Loaded {_parkingSpots.Count} parking spots for {airportIcao}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Error loading parking spots: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts taxiway guidance by requesting segment selection from user
    /// </summary>
    public void StartGuidance(double currentLat, double currentLon, double currentHeading)
    {
        if (_graph == null)
        {
            _announcer.AnnounceImmediate("No airport loaded. Please select an airport first.");
            return;
        }

        if (_isActive)
        {
            _announcer.AnnounceImmediate("Taxiway guidance already active");
            return;
        }

        // Find accessible segments based on graph connectivity
        var nearbySegments = _graph.FindAccessibleSegments(currentLat, currentLon, currentHeading, SEGMENT_SEARCH_RADIUS_FEET);

        if (nearbySegments.Count == 0)
        {
            _announcer.AnnounceImmediate("No taxiway found nearby. Move closer to a taxiway.");
            return;
        }

        // Store position for when user selects
        _lastAircraftHeading = currentHeading;
        _initialLatitude = currentLat;
        _initialLongitude = currentLon;

        // Transition to awaiting selection state
        _state = GuidanceState.AwaitingSegmentSelection;

        // Raise event for UI to show segment selection form
        SegmentSelectionRequired?.Invoke(this, new SegmentSelectionEventArgs
        {
            NearbySegments = nearbySegments,
            AircraftLatitude = currentLat,
            AircraftLongitude = currentLon,
            AircraftHeading = currentHeading
        });

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Requesting segment selection: {nearbySegments.Count} options");
    }

    /// <summary>
    /// Locks onto the selected segment and begins active guidance
    /// </summary>
    public void LockToSegment(SegmentOption selectedOption)
    {
        if (_graph == null)
            return;

        _lockedSegment = selectedOption.Segment;
        _lockedTargetNode = selectedOption.TargetNode;
        _currentTaxiwayName = selectedOption.Segment.Name;

        // Reset tracking state
        _lastCenterlineAnnouncement = DateTime.MinValue;
        _lastTaxiwayChangeAnnouncement = DateTime.MinValue;
        _lastParkingAnnouncement = DateTime.MinValue;
        _lastJunctionNode = null;
        _junctionPending = false;
        _junctionFirstWarningGiven = false;
        _relockSuggestionGiven = false;

        // Reset guidance state
        _lastPositionUpdateTime = DateTime.MinValue;
        _lastAnnouncedCorrectionDegrees = null;

        // Start audio tone
        _centerlineTone?.Start(_toneWaveType, _toneVolume);

        _isActive = true;
        _state = GuidanceState.SegmentLocked;
        GuidanceActiveChanged?.Invoke(this, true);

        // Check for parking spot at initial position (ensures feedback when starting at a gate)
        CheckParkingSpotProximity(_initialLatitude, _initialLongitude);

        // Announce the lock
        string taxiwayText = selectedOption.TaxiwayName;
        _announcer.AnnounceImmediate($"Locked to {taxiwayText}");

        System.Diagnostics.Debug.WriteLine($"[TaxiTrack] LOCKED: Segment '{_lockedSegment.Name ?? "unnamed"}' TargetNode: ({_lockedTargetNode.Latitude:F6}, {_lockedTargetNode.Longitude:F6}) IsJunction={_lockedTargetNode.IsJunction} Connections={_lockedTargetNode.ConnectedSegments.Count}");
    }

    /// <summary>
    /// Cancels segment selection (user pressed cancel or escape)
    /// </summary>
    public void CancelSegmentSelection()
    {
        if (_state == GuidanceState.AwaitingSegmentSelection)
        {
            _state = GuidanceState.Idle;
            _announcer.AnnounceImmediate("Taxiway selection cancelled");
            System.Diagnostics.Debug.WriteLine("[TaxiwayGuidance] Segment selection cancelled");
        }
    }

    /// <summary>
    /// Requests relock to a different segment (user triggered via menu)
    /// </summary>
    public void RequestRelock(double currentLat, double currentLon, double currentHeading)
    {
        if (_graph == null)
        {
            _announcer.AnnounceImmediate("No airport loaded.");
            return;
        }

        // Find accessible segments based on graph connectivity
        var nearbySegments = _graph.FindAccessibleSegments(currentLat, currentLon, currentHeading, SEGMENT_SEARCH_RADIUS_FEET);

        if (nearbySegments.Count == 0)
        {
            _announcer.AnnounceImmediate("No taxiway found nearby.");
            return;
        }

        _lastAircraftHeading = currentHeading;

        // Raise event for UI to show segment selection form (as relock)
        SegmentSelectionRequired?.Invoke(this, new SegmentSelectionEventArgs
        {
            NearbySegments = nearbySegments,
            AircraftLatitude = currentLat,
            AircraftLongitude = currentLon,
            AircraftHeading = currentHeading
        });

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Requesting relock: {nearbySegments.Count} options");
    }

    /// <summary>
    /// Stops taxiway guidance
    /// </summary>
    public void StopGuidance()
    {
        if (!_isActive && _state == GuidanceState.Idle)
            return;

        _centerlineTone?.Stop();
        _isActive = false;
        _lockedSegment = null;
        _lockedTargetNode = null;
        _junctionPending = false;
        _junctionFirstWarningGiven = false;
        _relockSuggestionGiven = false;
        _activeRoute = null;
        _isOnRoute = true;
        _waypointApproachAnnounced = false;
        _isInitialAlignment = false;
        _state = GuidanceState.Idle;

        GuidanceActiveChanged?.Invoke(this, false);
        _announcer.AnnounceImmediate("Taxiway guidance off");

        System.Diagnostics.Debug.WriteLine("[TaxiwayGuidance] Stopped");
    }

    /// <summary>
    /// Toggles taxiway guidance on/off
    /// </summary>
    public void Toggle(double currentLat, double currentLon, double currentHeading)
    {
        if (_isActive)
        {
            StopGuidance();
        }
        else
        {
            StartGuidance(currentLat, currentLon, currentHeading);
        }
    }

    /// <summary>
    /// Starts guidance for a pre-built route (route-based guidance mode)
    /// </summary>
    /// <param name="route">The pre-built taxi route to follow</param>
    public void StartRouteGuidance(TaxiRoute route)
    {
        if (_graph == null)
        {
            _announcer.AnnounceImmediate("No airport loaded.");
            return;
        }

        if (route == null || route.Waypoints.Count == 0)
        {
            _announcer.AnnounceImmediate("Invalid route.");
            return;
        }

        // Stop any existing guidance
        if (_isActive)
        {
            StopGuidance();
        }

        // Set up the route
        _activeRoute = route;
        _isOnRoute = true;
        _waypointApproachAnnounced = false;
        _lastDeviationAnnouncement = DateTime.MinValue;

        // Lock to the first waypoint's segment
        var firstWaypoint = route.CurrentWaypoint;
        if (firstWaypoint != null)
        {
            _lockedSegment = firstWaypoint.Segment;
            _lockedTargetNode = firstWaypoint.TargetNode;
            _currentTaxiwayName = firstWaypoint.Segment.Name;
        }

        // Reset tracking state
        _lastCenterlineAnnouncement = DateTime.MinValue;
        _lastTaxiwayChangeAnnouncement = DateTime.MinValue;
        _lastParkingAnnouncement = DateTime.MinValue;
        _lastJunctionNode = null;
        _junctionPending = false;
        _junctionFirstWarningGiven = false;
        _relockSuggestionGiven = false;

        // Reset guidance state
        _lastPositionUpdateTime = DateTime.MinValue;
        _lastAnnouncedCorrectionDegrees = null;

        // Start audio tone
        _centerlineTone?.Start(_toneWaveType, _toneVolume);

        _isActive = true;
        _state = GuidanceState.FollowingRoute;
        _isInitialAlignment = true;  // Allow alignment mode at route start
        GuidanceActiveChanged?.Invoke(this, true);

        // Announce the start
        string taxiwayPath = string.Join(", ", route.SelectedTaxiways);
        _announcer.AnnounceImmediate($"Route guidance started to {route.DestinationDescription}. {route.Waypoints.Count} waypoints via {taxiwayPath}.");

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Route guidance started: {route.Waypoints.Count} waypoints to {route.DestinationDescription}");

        // Print all waypoints for debugging
        for (int i = 0; i < route.Waypoints.Count; i++)
        {
            var wp = route.Waypoints[i];
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance]   Waypoint {i + 1}: {wp}");
        }
    }

    /// <summary>
    /// Process position update for guidance (uses LOCKED segment, not nearest)
    /// </summary>
    public void ProcessPositionUpdate(double latitude, double longitude, double heading, double groundSpeedKnots)
    {
        // Check parking spot proximity during active guidance
        CheckParkingSpotProximity(latitude, longitude);

        // Route-based guidance mode (including alignment states)
        if (_activeRoute != null && (_state == GuidanceState.FollowingRoute ||
            _state == GuidanceState.ApproachingWaypoint ||
            _state == GuidanceState.Deviated ||
            _state == GuidanceState.AligningToSegment ||
            _state == GuidanceState.AligningAlongSegment))
        {
            ProcessRouteGuidance(latitude, longitude, heading, groundSpeedKnots);
            return;
        }

        // Only process guidance-specific logic in active states (legacy mode)
        if (_state != GuidanceState.SegmentLocked && _state != GuidanceState.ApproachingJunction)
            return;

        if (_graph == null || _lockedSegment == null || _lockedTargetNode == null)
            return;

        _lastAircraftHeading = heading;
        _lastGroundSpeedKnots = groundSpeedKnots;

        // Debug: Log position and target node details
        double distanceToTarget = _graph.GetDistanceToNode(latitude, longitude, _lockedTargetNode);
        System.Diagnostics.Debug.WriteLine($"[TaxiTrack] Pos: ({latitude:F6}, {longitude:F6}) Hdg: {heading:F1}° Speed: {groundSpeedKnots:F1}kts State: {_state}");
        System.Diagnostics.Debug.WriteLine($"[TaxiTrack] TargetNode: IsJunction={_lockedTargetNode.IsJunction} IsDeadEnd={_lockedTargetNode.IsDeadEnd} Connections={_lockedTargetNode.ConnectedSegments.Count} Type={_lockedTargetNode.Type} Dist={distanceToTarget:F1}ft");

        // Calculate cross-track from LOCKED segment (for safety warnings only)
        double crossTrackFeet = _graph.CalculateCrossTrackFromSegment(
            latitude, longitude, _lockedSegment, heading);

        // Get target heading based on travel direction
        double targetHeading = GetTargetHeading();

        // Calculate speed-dependent look-ahead distance
        double lookAheadFeet = CalculateLookAheadDistance(groundSpeedKnots);

        // Calculate carrot position for single-segment mode
        var carrot = CalculateSingleSegmentCarrot(
            latitude, longitude, heading, _lockedSegment, targetHeading, lookAheadFeet);

        // Calculate CARROT PURSUIT steering correction
        double steeringCorrection = CalculateCarrotPursuitCorrection(heading, carrot);

        // Debug: Log carrot pursuit info
        System.Diagnostics.Debug.WriteLine($"[TaxiTrack] CrossTrack: {crossTrackFeet:F1}ft Carrot: dist={carrot.DistanceToCarrotFeet:F1}ft bearing={carrot.BearingFromAircraft:F1}° Correction: {steeringCorrection:F1}°");

        // Audio panning based on carrot pursuit correction
        float pan = (float)Math.Clamp(steeringCorrection / TAXI_MAX_CORRECTION_DEGREES, -1.0, 1.0);
        _centerlineTone?.SetPan(pan);

        // Announce carrot pursuit steering correction if changed
        AnnouncePursuitCorrectionIfNeeded(steeringCorrection);

        // Safety-only cross-track check (doesn't affect steering, just warns of gross deviations)
        CheckCrossTrackSafetyWarnings(crossTrackFeet, _lockedSegment.Width);

        // Check for junction ahead (two-stage: 150ft warning, 50ft show form)
        CheckForJunction(latitude, longitude);

        // Check for hold short
        CheckForHoldShort(latitude, longitude);

        // Check for dead-end/parking arrival
        CheckForDestinationArrival(latitude, longitude);
    }

    /// <summary>
    /// Checks for excessive deviation and suggests relock if too far
    /// </summary>
    private void CheckForDeviationWarning(double crossTrackFeet)
    {
        if (Math.Abs(crossTrackFeet) >= DEVIATION_SUGGEST_RELOCK_FEET && !_relockSuggestionGiven)
        {
            _announcer.AnnounceImmediate("Far from locked taxiway. Use Taxi menu to relock if needed.");
            _relockSuggestionGiven = true;
        }
        else if (Math.Abs(crossTrackFeet) < DEVIATION_SUGGEST_RELOCK_FEET)
        {
            // Reset flag when back closer
            _relockSuggestionGiven = false;
        }
    }

    /// <summary>
    /// Checks if we've arrived at a dead-end or parking destination
    /// </summary>
    private void CheckForDestinationArrival(double latitude, double longitude)
    {
        if (_lockedTargetNode == null || _graph == null)
            return;

        // Only check if target node is a dead-end
        if (!_lockedTargetNode.IsDeadEnd)
            return;

        double distanceToNode = _graph.GetDistanceToNode(latitude, longitude, _lockedTargetNode);

        if (distanceToNode <= JUNCTION_SHOW_FORM_FEET)  // Within 50ft of dead-end
        {
            _state = GuidanceState.AtDestination;

            string announcement;
            if (_lockedTargetNode.Type == TaxiwayNodeType.Parking)
            {
                string spotName = _lockedTargetNode.ParkingSpotName ?? "parking";
                announcement = _lockedTargetNode.HasJetway
                    ? $"Arrived at {spotName} with jetway. Guidance paused."
                    : $"Arrived at {spotName}. Guidance paused.";
            }
            else if (_lockedTargetNode.Type == TaxiwayNodeType.HoldShort)
            {
                string runway = _lockedTargetNode.HoldShortRunway ?? "runway";
                announcement = $"At hold short for {runway}. Guidance paused.";
            }
            else
            {
                announcement = "End of taxiway. Guidance paused.";
            }

            _announcer.AnnounceImmediate(announcement);
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Arrived at destination: {_lockedTargetNode}");
        }
    }

    #region Route-Based Guidance

    /// <summary>
    /// Processes position update for route-based guidance mode
    /// </summary>
    private void ProcessRouteGuidance(double latitude, double longitude, double heading, double groundSpeedKnots)
    {
        if (_activeRoute == null || _graph == null || _lockedSegment == null || _lockedTargetNode == null)
            return;

        _lastAircraftHeading = heading;
        _lastGroundSpeedKnots = groundSpeedKnots;

        var currentWaypoint = _activeRoute.CurrentWaypoint;
        if (currentWaypoint == null)
        {
            // Route complete
            CompleteRouteGuidance();
            return;
        }

        // Calculate bounded position relative to segment
        var positionResult = _graph.CalculatePositionOnSegment(
            latitude, longitude, _lockedSegment, heading, currentWaypoint.Heading);

        System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Position: type={positionResult.PositionType}, " +
            $"crossTrack={positionResult.CrossTrackFeet:F1}ft, alongTrack={positionResult.AlongTrackFeet:F1}ft, " +
            $"state={_state}");

        // Check if alignment is needed (only when in route-following states)
        if (_state == GuidanceState.FollowingRoute || _state == GuidanceState.ApproachingWaypoint)
        {
            // Use different thresholds for initial alignment vs mid-route drift
            // Initial: 30ft - user may not be on taxiway yet, needs perpendicular intercept guidance
            // Mid-route: 100ft - normal drift uses smooth PID correction, only severe deviation needs alignment
            double alignmentThreshold = _isInitialAlignment ? ALIGNMENT_INITIAL_TRIGGER_FEET : ALIGNMENT_MIDROUTE_TRIGGER_FEET;

            if (Math.Abs(positionResult.CrossTrackFeet) > alignmentThreshold)
            {
                TransitionToAlignmentState(GuidanceState.AligningToSegment, "Aligning to taxiway centerline");
            }
            // On centerline but before segment start (only during initial alignment)
            else if (_isInitialAlignment &&
                     positionResult.IsOnCenterline(ALIGNMENT_CENTERLINE_TOLERANCE_FEET) &&
                     positionResult.AlongTrackFeet < 0)
            {
                TransitionToAlignmentState(GuidanceState.AligningAlongSegment, "Approaching taxiway");
            }
        }

        // Process based on current state
        switch (_state)
        {
            case GuidanceState.AligningToSegment:
                ProcessAlignmentToSegment(latitude, longitude, heading, positionResult);
                return;

            case GuidanceState.AligningAlongSegment:
                ProcessAlignmentAlongSegment(latitude, longitude, heading, positionResult);
                return;

            case GuidanceState.FollowingRoute:
            case GuidanceState.ApproachingWaypoint:
            case GuidanceState.Deviated:
                ProcessNormalRouteGuidance(latitude, longitude, heading, groundSpeedKnots, positionResult);
                return;
        }
    }

    /// <summary>
    /// Processes normal route-following guidance (when on the taxiway)
    /// </summary>
    private void ProcessNormalRouteGuidance(
        double latitude, double longitude, double heading, double groundSpeedKnots,
        SegmentPositionResult positionResult)
    {
        if (_activeRoute == null || _graph == null || _lockedSegment == null || _lockedTargetNode == null)
            return;

        var currentWaypoint = _activeRoute.CurrentWaypoint;
        if (currentWaypoint == null)
            return;

        // Use position result's cross-track for safety warnings
        double crossTrackFeet = positionResult.CrossTrackFeet;

        // Once we're on the segment, disable initial alignment mode
        if (positionResult.PositionType == SegmentPositionType.OnSegment && _isInitialAlignment)
        {
            _isInitialAlignment = false;
            System.Diagnostics.Debug.WriteLine("[RouteGuidance] Initial alignment complete - on taxiway");
        }

        // Calculate speed-dependent look-ahead distance
        double lookAheadFeet = CalculateLookAheadDistance(groundSpeedKnots);

        // Calculate carrot position along the route
        var carrot = CalculateCarrotPosition(latitude, longitude, heading, _activeRoute, lookAheadFeet);

        // Calculate CARROT PURSUIT steering correction
        double steeringCorrection = CalculateCarrotPursuitCorrection(heading, carrot);

        // Audio panning based on carrot pursuit correction
        float pan = (float)Math.Clamp(steeringCorrection / TAXI_MAX_CORRECTION_DEGREES, -1.0, 1.0);
        _centerlineTone?.SetPan(pan);

        // Announce carrot pursuit steering correction if changed
        AnnouncePursuitCorrectionIfNeeded(steeringCorrection);

        // Check distance to current waypoint target node
        double distanceToWaypoint = _graph.GetDistanceToNode(latitude, longitude, currentWaypoint.TargetNode);

        // Safety-only cross-track check (doesn't affect steering, just warns of gross deviations)
        CheckCrossTrackSafetyWarnings(crossTrackFeet, _lockedSegment.Width);

        // Use position result's along-track distance
        double alongTrackFeet = positionResult.AlongTrackFeet;
        double segmentLength = positionResult.SegmentLengthFeet;

        System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Waypoint {_activeRoute.CurrentWaypointIndex + 1}/{_activeRoute.Waypoints.Count}, " +
            $"dist={distanceToWaypoint:F1}ft, crossTrack={crossTrackFeet:F1}ft, alongTrack={alongTrackFeet:F1}ft/{segmentLength:F1}ft, " +
            $"carrot@{carrot.WaypointIndex + 1} dist={carrot.DistanceToCarrotFeet:F1}ft bearing={carrot.BearingFromAircraft:F1}° correction={steeringCorrection:F1}°");

        // Check for route deviation (now includes distance metric)
        CheckRouteDeviation(crossTrackFeet, distanceToWaypoint, segmentLength);

        // Distance-increasing detection (safety net for stuck waypoints)
        CheckDistanceIncreasing(distanceToWaypoint, alongTrackFeet);

        // Waypoint approach announcement (150ft)
        if (distanceToWaypoint <= WAYPOINT_APPROACH_FEET && !_waypointApproachAnnounced)
        {
            if (!string.IsNullOrEmpty(currentWaypoint.ApproachAnnouncement))
            {
                _announcer.AnnounceImmediate(currentWaypoint.ApproachAnnouncement);
            }
            _waypointApproachAnnounced = true;
            _state = GuidanceState.ApproachingWaypoint;
            System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Approaching waypoint: {currentWaypoint.ApproachAnnouncement}");
        }

        // Turn anticipation - announce "Begin turn" for significant turns
        var nextWaypoint = _activeRoute.NextWaypoint;
        if (nextWaypoint != null && Math.Abs(nextWaypoint.TurnAngle) > 30 && !_turnAnticipationAnnounced)
        {
            // Calculate turn anticipation distance based on turn angle
            // Larger turns need more lead time. At 15kt (~25 ft/sec), a 90° turn needs ~2-3 seconds.
            double anticipationDistance = Math.Abs(nextWaypoint.TurnAngle) * 0.6; // ~54ft for 90° turn
            anticipationDistance = Math.Max(anticipationDistance, 40); // Minimum 40ft

            if (distanceToWaypoint <= anticipationDistance)
            {
                string direction = nextWaypoint.TurnAngle > 0 ? "right" : "left";
                _announcer.AnnounceImmediate($"Begin turn {direction}");
                _turnAnticipationAnnounced = true;
                System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Turn anticipation: Begin turn {direction} (turn angle: {nextWaypoint.TurnAngle:F0}°)");
            }
        }

        // Waypoint pass - advance to next waypoint
        // Two conditions: within 30ft of target OR passed the segment end
        bool withinPassDistance = distanceToWaypoint <= WAYPOINT_PASS_FEET;
        bool passedSegmentEnd = alongTrackFeet > segmentLength + ALONG_TRACK_TOLERANCE_FEET;

        if (withinPassDistance || passedSegmentEnd)
        {
            if (passedSegmentEnd && !withinPassDistance)
            {
                System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Advancing via along-track (passed segment end): alongTrack={alongTrackFeet:F1}ft > segLen={segmentLength:F1}ft");
            }
            AdvanceToNextWaypoint();
        }
    }

    /// <summary>
    /// Detects when distance to waypoint is consistently increasing (moving away)
    /// and triggers waypoint advancement if we're past the segment start
    /// </summary>
    private void CheckDistanceIncreasing(double distanceToWaypoint, double alongTrackFeet)
    {
        if (distanceToWaypoint > _lastDistanceToWaypoint + DISTANCE_INCREASING_TOLERANCE)
        {
            _distanceIncreasingCount++;
            if (_distanceIncreasingCount >= DISTANCE_INCREASING_THRESHOLD)
            {
                // Distance consistently increasing - check if we should advance
                if (alongTrackFeet > 0) // Past segment start
                {
                    System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Distance increasing for {_distanceIncreasingCount} cycles, advancing waypoint");
                    AdvanceToNextWaypoint();
                }
                _distanceIncreasingCount = 0;
            }
        }
        else
        {
            _distanceIncreasingCount = 0;
        }
        _lastDistanceToWaypoint = distanceToWaypoint;
    }

    /// <summary>
    /// Processes guidance when aircraft is off centerline (perpendicular alignment)
    /// </summary>
    private void ProcessAlignmentToSegment(
        double latitude, double longitude, double heading,
        SegmentPositionResult positionResult)
    {
        if (_lockedSegment == null || _lockedTargetNode == null)
            return;

        // CRITICAL: Check if past segment end - must advance waypoint even if still off centerline
        // This prevents getting stuck forever when aircraft moves past the segment during alignment
        if (positionResult.AlongTrackFeet > positionResult.SegmentLengthFeet + ALONG_TRACK_TOLERANCE_FEET)
        {
            System.Diagnostics.Debug.WriteLine($"[Alignment] Past segment end during alignment (along={positionResult.AlongTrackFeet:F1}ft > len={positionResult.SegmentLengthFeet:F1}ft), advancing waypoint");
            AdvanceToNextWaypoint();
            return;
        }

        // Calculate steering correction toward intercept point
        double headingError = NormalizeHeading(positionResult.HeadingToIntercept - heading);

        // For alignment, we guide directly to the intercept point on centerline
        double steeringCorrection = Math.Clamp(headingError, -TAXI_MAX_CORRECTION_DEGREES, TAXI_MAX_CORRECTION_DEGREES);

        // Audio panning based on correction direction
        float pan = (float)Math.Clamp(steeringCorrection / TAXI_MAX_CORRECTION_DEGREES, -1.0, 1.0);
        _centerlineTone?.SetPan(pan);

        // Announce heading-based steering correction
        AnnouncePursuitCorrectionIfNeeded(steeringCorrection);

        System.Diagnostics.Debug.WriteLine($"[Alignment] ToSegment: targetHdg={positionResult.HeadingToIntercept:F1}, " +
            $"correction={steeringCorrection:F1}, crossTrack={positionResult.CrossTrackFeet:F1}ft, dist={positionResult.DistanceToInterceptFeet:F1}ft");

        // Check for transition to next state
        if (positionResult.IsOnCenterline(ALIGNMENT_CENTERLINE_TOLERANCE_FEET))
        {
            if (positionResult.IsWithinSegmentBounds)
            {
                TransitionFromAlignment("On taxiway, following route");
            }
            else
            {
                // Centerline captured but outside segment bounds - transition to along-segment alignment
                _state = GuidanceState.AligningAlongSegment;
                _alignmentAnnounced = false; // Reset for new state
                System.Diagnostics.Debug.WriteLine("[RouteGuidance] Centerline captured, moving along segment to entry");
            }
        }
    }

    /// <summary>
    /// Processes guidance when aircraft is on centerline but outside segment bounds
    /// </summary>
    private void ProcessAlignmentAlongSegment(
        double latitude, double longitude, double heading,
        SegmentPositionResult positionResult)
    {
        if (_lockedSegment == null || _lockedTargetNode == null || _activeRoute?.CurrentWaypoint == null)
            return;

        // Get segment heading (direction we should be traveling)
        double segmentHeading = _activeRoute.CurrentWaypoint.Heading;

        if (positionResult.AlongTrackFeet < 0)
        {
            // Before segment start - guide forward along centerline using heading-based guidance
            double steeringCorrection = CalculateHeadingBasedCorrection(
                segmentHeading, heading, -positionResult.AlongTrackFeet, null);

            float pan = (float)Math.Clamp(steeringCorrection / TAXI_MAX_CORRECTION_DEGREES, -1.0, 1.0);
            _centerlineTone?.SetPan(pan);

            AnnouncePursuitCorrectionIfNeeded(steeringCorrection);

            System.Diagnostics.Debug.WriteLine($"[Alignment] AlongSegment: segHdg={segmentHeading:F1}, " +
                $"correction={steeringCorrection:F1}, toEntry={-positionResult.AlongTrackFeet:F1}ft");
        }
        else if (positionResult.AlongTrackFeet > positionResult.SegmentLengthFeet)
        {
            // Past segment end - advance to next waypoint
            System.Diagnostics.Debug.WriteLine("[Alignment] Past segment end, advancing waypoint");
            AdvanceToNextWaypoint();
            return;
        }

        // Check for transition to normal route following
        if (positionResult.IsWithinSegmentBounds)
        {
            TransitionFromAlignment("On taxiway, following route");
        }
    }

    /// <summary>
    /// Transitions to alignment state with announcement
    /// </summary>
    private void TransitionToAlignmentState(GuidanceState newState, string announcement)
    {
        _state = newState;
        if (!_alignmentAnnounced)
        {
            _announcer.AnnounceImmediate(announcement);
            _alignmentAnnounced = true;
        }
        System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Transition to {newState}");
    }

    /// <summary>
    /// Transitions from alignment back to normal route following
    /// </summary>
    private void TransitionFromAlignment(string announcement)
    {
        _state = GuidanceState.FollowingRoute;
        _announcer.AnnounceImmediate(announcement);
        _alignmentAnnounced = false;

        // Reset waypoint approach state in case we were interrupted
        _waypointApproachAnnounced = false;

        System.Diagnostics.Debug.WriteLine("[RouteGuidance] Alignment complete, resuming route");
    }

    /// <summary>
    /// Advances to the next waypoint in the route
    /// </summary>
    private void AdvanceToNextWaypoint()
    {
        if (_activeRoute == null)
            return;

        var currentWaypoint = _activeRoute.CurrentWaypoint;

        // Announce pass if there's a pass announcement
        if (currentWaypoint != null && !string.IsNullOrEmpty(currentWaypoint.PassAnnouncement))
        {
            _announcer.AnnounceImmediate(currentWaypoint.PassAnnouncement);
        }

        // Check if this was the destination
        if (currentWaypoint?.Type == WaypointType.Destination)
        {
            CompleteRouteGuidance();
            return;
        }

        // Advance to next waypoint
        _activeRoute.AdvanceWaypoint();
        _waypointApproachAnnounced = false;

        var nextWaypoint = _activeRoute.CurrentWaypoint;
        if (nextWaypoint == null)
        {
            CompleteRouteGuidance();
            return;
        }

        // Update locked segment to new waypoint
        _lockedSegment = nextWaypoint.Segment;
        _lockedTargetNode = nextWaypoint.TargetNode;
        _currentTaxiwayName = nextWaypoint.Segment.Name;

        // Reset guidance state for new segment
        _lastPositionUpdateTime = DateTime.MinValue;
        _lastAnnouncedCorrectionDegrees = null;

        // Reset distance-increasing detection state
        _lastDistanceToWaypoint = double.MaxValue;
        _distanceIncreasingCount = 0;

        // Reset turn anticipation state
        _turnAnticipationAnnounced = false;

        _state = GuidanceState.FollowingRoute;

        System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Advanced to waypoint {_activeRoute.CurrentWaypointIndex + 1}/{_activeRoute.Waypoints.Count}");
    }

    /// <summary>
    /// Checks if aircraft has deviated from the route using both cross-track and distance metrics
    /// </summary>
    private void CheckRouteDeviation(double crossTrackFeet, double distanceToWaypoint, double segmentLength)
    {
        bool wasOnRoute = _isOnRoute;

        // Cross-track deviation (original check)
        bool crossTrackDeviation = Math.Abs(crossTrackFeet) >= ROUTE_DEVIATION_FEET;

        // Distance-based deviation: if distance to waypoint is way more than segment length,
        // we've likely missed a turn (even if cross-track is small due to infinite line projection)
        // Skip this check for first waypoint - aircraft may legitimately be far from first segment
        // when starting from a gate
        bool isFirstWaypoint = _activeRoute?.CurrentWaypointIndex == 0;
        bool distanceDeviation = !isFirstWaypoint && distanceToWaypoint > Math.Max(segmentLength * 3, 500);

        if (crossTrackDeviation || distanceDeviation)
        {
            _isOnRoute = false;

            if (wasOnRoute || DateTime.Now - _lastDeviationAnnouncement >= TimeSpan.FromSeconds(DEVIATION_ANNOUNCEMENT_INTERVAL_SECONDS))
            {
                if (distanceDeviation && !crossTrackDeviation)
                {
                    // Special message for distance-based deviation (likely missed a turn)
                    _announcer.AnnounceImmediate("You may have missed a turn. Recalculating route.");
                    System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Distance deviation detected: dist={distanceToWaypoint:F1}ft >> segLen={segmentLength:F1}ft");
                }
                else
                {
                    _announcer.AnnounceImmediate("Off route. Continue following guidance to return.");
                }
                _lastDeviationAnnouncement = DateTime.Now;
                _state = GuidanceState.Deviated;
            }
        }
        else if (Math.Abs(crossTrackFeet) <= ROUTE_BACK_ON_FEET)
        {
            if (!wasOnRoute)
            {
                _announcer.AnnounceImmediate("Back on route.");
                _isOnRoute = true;
                _state = GuidanceState.FollowingRoute;
            }
        }
    }

    /// <summary>
    /// Completes route guidance when destination is reached
    /// </summary>
    private void CompleteRouteGuidance()
    {
        if (_activeRoute == null)
            return;

        string destination = _activeRoute.DestinationDescription;
        _state = GuidanceState.AtDestination;

        // Stop audio tone
        _centerlineTone?.Stop();
        _isActive = false;

        _announcer.AnnounceImmediate($"Arrived at {destination}. Guidance complete.");

        // Clear the route
        _activeRoute = null;
        _lockedSegment = null;
        _lockedTargetNode = null;

        GuidanceActiveChanged?.Invoke(this, false);

        System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Route complete: {destination}");
    }

    #endregion

    /// <summary>
    /// Gets the target heading based on locked segment and travel direction
    /// </summary>
    private double GetTargetHeading()
    {
        if (_lockedSegment == null || _lockedTargetNode == null)
            return 0;

        // Get the node we're coming from (opposite of where we're going)
        var sourceNode = _lockedSegment.GetOtherNode(_lockedTargetNode);
        if (sourceNode == null)
            return _lockedSegment.Heading;

        // Get heading from source toward target - this is our desired travel direction
        return _lockedSegment.GetHeadingFrom(sourceNode);
    }

    /// <summary>
    /// Calculates desired steering correction using heading-based guidance.
    /// Primary input is heading error (not cross-track). Blends toward next segment near waypoints.
    /// </summary>
    /// <param name="targetHeading">Current segment's target heading</param>
    /// <param name="aircraftHeading">Current aircraft heading</param>
    /// <param name="distanceToNextWaypointFeet">Distance to the next waypoint/node</param>
    /// <param name="nextSegmentHeading">Heading of the next segment (for blending), or null</param>
    /// <returns>Steering correction in degrees (positive = turn right, negative = turn left)</returns>
    private double CalculateHeadingBasedCorrection(
        double targetHeading,
        double aircraftHeading,
        double distanceToNextWaypointFeet,
        double? nextSegmentHeading)
    {
        // Calculate base heading error
        double headingError = NormalizeHeading(targetHeading - aircraftHeading);

        // Near waypoints, blend toward next segment's heading for smooth turns
        if (nextSegmentHeading.HasValue && distanceToNextWaypointFeet < HEADING_BLEND_DISTANCE_FEET)
        {
            double blendFactor = 1.0 - (distanceToNextWaypointFeet / HEADING_BLEND_DISTANCE_FEET);
            blendFactor = Math.Clamp(blendFactor, 0, 0.5); // Max 50% blend

            double nextHeadingError = NormalizeHeading(nextSegmentHeading.Value - aircraftHeading);
            headingError = headingError * (1 - blendFactor) + nextHeadingError * blendFactor;
        }

        // Apply proportional correction
        double correction = headingError * HEADING_CORRECTION_GAIN;

        // Clamp to max correction
        return Math.Clamp(correction, -TAXI_MAX_CORRECTION_DEGREES, TAXI_MAX_CORRECTION_DEGREES);
    }

    /// <summary>
    /// Normalizes heading difference to -180 to +180 range
    /// </summary>
    private static double NormalizeHeading(double heading)
    {
        while (heading > 180) heading -= 360;
        while (heading < -180) heading += 360;
        return heading;
    }

    /// <summary>
    /// Calculates the carrot (target) position for pursuit guidance.
    /// Projects ahead along the route by the look-ahead distance, spanning multiple segments if needed.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude</param>
    /// <param name="aircraftLon">Aircraft longitude</param>
    /// <param name="aircraftHeading">Aircraft heading</param>
    /// <param name="route">The taxi route being followed</param>
    /// <param name="lookAheadFeet">Distance to project ahead along the route</param>
    /// <returns>CarrotPosition with target point coordinates and bearing</returns>
    private CarrotPosition CalculateCarrotPosition(
        double aircraftLat, double aircraftLon, double aircraftHeading,
        TaxiRoute route, double lookAheadFeet)
    {
        if (_graph == null || route.CurrentWaypoint == null)
        {
            return new CarrotPosition { IsAtDestination = true };
        }

        var currentWaypoint = route.CurrentWaypoint;
        var currentSegment = currentWaypoint.Segment;

        // Calculate aircraft's position along current segment
        var positionResult = _graph.CalculatePositionOnSegment(
            aircraftLat, aircraftLon, currentSegment, aircraftHeading, currentWaypoint.Heading);

        // Get along-track distance on current segment (clamped to >= 0)
        double alongTrackFeet = Math.Max(0, positionResult.AlongTrackFeet);
        double segmentLength = currentSegment.Length;
        double crossTrackFeet = positionResult.CrossTrackFeet;

        // === RECOVERY ARC LOGIC ===
        // When significantly off-centerline, use a recovery arc instead of direct centerline projection.
        // This provides a smooth, gradual return path instead of a harsh 90-degree intercept.
        if (Math.Abs(crossTrackFeet) > RECOVERY_ARC_THRESHOLD_FEET)
        {
            // Calculate or update recovery arc
            if (!_inRecoveryMode || _currentRecoveryArc == null)
            {
                _currentRecoveryArc = CalculateRecoveryArc(
                    aircraftLat, aircraftLon, aircraftHeading,
                    crossTrackFeet, currentWaypoint.Heading);

                if (_currentRecoveryArc.Value.IsValid)
                {
                    _inRecoveryMode = true;
                    System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Entering recovery mode: crossTrack={crossTrackFeet:F1}ft");
                }
            }

            // Project carrot on recovery arc if valid
            if (_inRecoveryMode && _currentRecoveryArc?.IsValid == true)
            {
                return ProjectCarrotOnRecoveryArc(
                    aircraftLat, aircraftLon,
                    _currentRecoveryArc.Value, lookAheadFeet, route.CurrentWaypointIndex);
            }
        }
        else if (Math.Abs(crossTrackFeet) < RECOVERY_ARC_EXIT_FEET && _inRecoveryMode)
        {
            // Exit recovery mode when back near centerline
            _inRecoveryMode = false;
            _currentRecoveryArc = null;
            System.Diagnostics.Debug.WriteLine($"[RouteGuidance] Exiting recovery mode: crossTrack={crossTrackFeet:F1}ft");
        }
        // === END RECOVERY ARC LOGIC ===

        // Check if there's a significant turn coming up
        var nextWaypoint = route.NextWaypoint;
        TurnArc arc = default;
        bool hasArc = false;

        if (nextWaypoint != null)
        {
            arc = CalculateTurnArc(currentWaypoint, nextWaypoint);
            hasArc = arc.IsValid;
        }

        // Start projecting from current position
        double remainingLookAhead = lookAheadFeet;
        int waypointIndex = route.CurrentWaypointIndex;

        // Calculate where arc begins (if applicable)
        double distanceToArcEntry = hasArc
            ? GetDistanceToArcEntry(alongTrackFeet, segmentLength, arc.EntryDistanceFeet)
            : double.MaxValue;

        // Case 1: Carrot is in the pre-arc straight portion of current segment
        if (remainingLookAhead <= distanceToArcEntry && distanceToArcEntry > 0)
        {
            double carrotAlongTrack = alongTrackFeet + remainingLookAhead;
            return ProjectCarrotOnSegment(
                aircraftLat, aircraftLon,
                currentSegment, currentWaypoint.Heading, carrotAlongTrack,
                waypointIndex, false);
        }

        // Case 2: Carrot is in the arc region
        if (hasArc && distanceToArcEntry >= 0)
        {
            // Consume distance to arc entry
            remainingLookAhead -= Math.Max(0, distanceToArcEntry);

            // If carrot is within the arc
            if (remainingLookAhead <= arc.ArcLengthFeet)
            {
                return ProjectCarrotOnArc(
                    aircraftLat, aircraftLon,
                    arc, remainingLookAhead, waypointIndex);
            }

            // Consume the arc and continue to next segment
            remainingLookAhead -= arc.ArcLengthFeet;
            waypointIndex++;

            // Skip to the position on next segment after arc exit
            // (arc exit is at arc.EntryDistanceFeet from the start of next segment)
            if (waypointIndex < route.Waypoints.Count)
            {
                var postArcWaypoint = route.Waypoints[waypointIndex];
                var postArcSegment = postArcWaypoint.Segment;
                double postArcStartPosition = arc.EntryDistanceFeet; // Start after arc exit

                double postArcRemaining = postArcSegment.Length - postArcStartPosition;

                if (remainingLookAhead <= postArcRemaining)
                {
                    double carrotPosition = postArcStartPosition + remainingLookAhead;
                    return ProjectCarrotOnSegment(
                        aircraftLat, aircraftLon,
                        postArcSegment, postArcWaypoint.Heading, carrotPosition,
                        waypointIndex, false);
                }

                // Consume this segment and continue
                remainingLookAhead -= postArcRemaining;
                waypointIndex++;
            }
        }
        else if (!hasArc)
        {
            // No arc - use straight projection for remaining distance on current segment
            double remainingOnCurrentSegment = Math.Max(0, segmentLength - alongTrackFeet);

            if (remainingLookAhead <= remainingOnCurrentSegment)
            {
                double carrotAlongTrack = alongTrackFeet + remainingLookAhead;
                return ProjectCarrotOnSegment(
                    aircraftLat, aircraftLon,
                    currentSegment, currentWaypoint.Heading, carrotAlongTrack,
                    waypointIndex, false);
            }

            // Consume current segment
            remainingLookAhead -= remainingOnCurrentSegment;
            waypointIndex++;
        }
        else
        {
            // Already past arc entry - we're in arc region
            double distanceIntoArc = -distanceToArcEntry;

            if (distanceIntoArc + remainingLookAhead <= arc.ArcLengthFeet)
            {
                return ProjectCarrotOnArc(
                    aircraftLat, aircraftLon,
                    arc, distanceIntoArc + remainingLookAhead, waypointIndex);
            }

            // Consume remaining arc and continue
            remainingLookAhead -= (arc.ArcLengthFeet - distanceIntoArc);
            waypointIndex++;

            // Continue from arc exit on next segment
            if (waypointIndex < route.Waypoints.Count)
            {
                var postArcWaypoint = route.Waypoints[waypointIndex];
                var postArcSegment = postArcWaypoint.Segment;
                double postArcStartPosition = arc.EntryDistanceFeet;

                double postArcRemaining = postArcSegment.Length - postArcStartPosition;

                if (remainingLookAhead <= postArcRemaining)
                {
                    double carrotPosition = postArcStartPosition + remainingLookAhead;
                    return ProjectCarrotOnSegment(
                        aircraftLat, aircraftLon,
                        postArcSegment, postArcWaypoint.Heading, carrotPosition,
                        waypointIndex, false);
                }

                remainingLookAhead -= postArcRemaining;
                waypointIndex++;
            }
        }

        // Project across subsequent segments (simple straight-line for segments beyond the first turn)
        while (remainingLookAhead > 0 && waypointIndex < route.Waypoints.Count)
        {
            var waypoint = route.Waypoints[waypointIndex];
            var segment = waypoint.Segment;
            double waypointSegmentLength = segment.Length;

            if (remainingLookAhead <= waypointSegmentLength)
            {
                return ProjectCarrotOnSegment(
                    aircraftLat, aircraftLon,
                    segment, waypoint.Heading, remainingLookAhead,
                    waypointIndex, false);
            }

            remainingLookAhead -= waypointSegmentLength;
            waypointIndex++;
        }

        // Ran out of segments - carrot is at destination
        var destNode = route.DestinationNode ?? route.Waypoints[^1].TargetNode;
        double bearingToDest = Navigation.NavigationCalculator.CalculateBearing(
            aircraftLat, aircraftLon, destNode.Latitude, destNode.Longitude);
        double distToDest = Navigation.NavigationCalculator.CalculateDistance(
            aircraftLat, aircraftLon, destNode.Latitude, destNode.Longitude) * 6076.12;

        return new CarrotPosition
        {
            Latitude = destNode.Latitude,
            Longitude = destNode.Longitude,
            Heading = route.Waypoints[^1].Heading,
            BearingFromAircraft = bearingToDest,
            DistanceToCarrotFeet = distToDest,
            WaypointIndex = route.Waypoints.Count - 1,
            IsAtDestination = true
        };
    }

    /// <summary>
    /// Projects the carrot point along a segment at a given distance from the start.
    /// </summary>
    private CarrotPosition ProjectCarrotOnSegment(
        double aircraftLat, double aircraftLon,
        TaxiwaySegment segment, double travelHeading, double distanceAlongSegment,
        int waypointIndex, bool isAtDestination)
    {
        // Determine which direction we're traveling along the segment
        double headingDiff = Math.Abs(NormalizeHeading(travelHeading - segment.Heading));
        bool reverseDirection = headingDiff > 90;

        double carrotLat, carrotLon;

        if (reverseDirection)
        {
            // Traveling from end to start
            double distFromEnd = distanceAlongSegment;
            double distFromStart = segment.Length - distFromEnd;

            // Project from start node toward end node, but at (length - distance)
            carrotLat = segment.EndNode.Latitude +
                (segment.StartNode.Latitude - segment.EndNode.Latitude) * (distFromEnd / segment.Length);
            carrotLon = segment.EndNode.Longitude +
                (segment.StartNode.Longitude - segment.EndNode.Longitude) * (distFromEnd / segment.Length);
        }
        else
        {
            // Traveling from start to end (normal direction)
            carrotLat = segment.StartNode.Latitude +
                (segment.EndNode.Latitude - segment.StartNode.Latitude) * (distanceAlongSegment / segment.Length);
            carrotLon = segment.StartNode.Longitude +
                (segment.EndNode.Longitude - segment.StartNode.Longitude) * (distanceAlongSegment / segment.Length);
        }

        // Calculate bearing and distance from aircraft to carrot
        double bearingToCarrot = Navigation.NavigationCalculator.CalculateBearing(
            aircraftLat, aircraftLon, carrotLat, carrotLon);
        double distanceToCarrot = Navigation.NavigationCalculator.CalculateDistance(
            aircraftLat, aircraftLon, carrotLat, carrotLon) * 6076.12; // NM to feet

        return new CarrotPosition
        {
            Latitude = carrotLat,
            Longitude = carrotLon,
            Heading = travelHeading,
            BearingFromAircraft = bearingToCarrot,
            DistanceToCarrotFeet = distanceToCarrot,
            WaypointIndex = waypointIndex,
            IsAtDestination = isAtDestination
        };
    }

    /// <summary>
    /// Calculates carrot position for single-segment guidance mode (no route).
    /// Projects ahead along the segment by look-ahead distance, clamped to segment end.
    /// </summary>
    private CarrotPosition CalculateSingleSegmentCarrot(
        double aircraftLat, double aircraftLon, double aircraftHeading,
        TaxiwaySegment segment, double travelHeading, double lookAheadFeet)
    {
        if (_graph == null)
        {
            return new CarrotPosition { IsAtDestination = true };
        }

        // Calculate aircraft's position along segment
        var positionResult = _graph.CalculatePositionOnSegment(
            aircraftLat, aircraftLon, segment, aircraftHeading, travelHeading);

        // Get along-track distance (clamped to >= 0)
        double alongTrackFeet = Math.Max(0, positionResult.AlongTrackFeet);
        double segmentLength = segment.Length;

        // Calculate carrot position along segment
        double carrotAlongTrack = alongTrackFeet + lookAheadFeet;

        // Clamp to segment end
        bool isAtEnd = carrotAlongTrack >= segmentLength;
        carrotAlongTrack = Math.Min(carrotAlongTrack, segmentLength);

        // Project carrot point
        return ProjectCarrotOnSegment(
            aircraftLat, aircraftLon,
            segment, travelHeading, carrotAlongTrack,
            0, isAtEnd);
    }

    /// <summary>
    /// Calculates steering correction for carrot pursuit guidance.
    /// Simply steers toward the carrot point.
    /// </summary>
    /// <param name="aircraftHeading">Current aircraft heading</param>
    /// <param name="carrot">The carrot position to pursue</param>
    /// <returns>Steering correction in degrees (positive = turn right, negative = turn left)</returns>
    private double CalculateCarrotPursuitCorrection(double aircraftHeading, CarrotPosition carrot)
    {
        // Calculate bearing error: how far off are we from pointing at the carrot?
        double bearingError = NormalizeHeading(carrot.BearingFromAircraft - aircraftHeading);

        // Apply gain and clamp
        double correction = bearingError * PURSUIT_GAIN;
        return Math.Clamp(correction, -TAXI_MAX_CORRECTION_DEGREES, TAXI_MAX_CORRECTION_DEGREES);
    }

    /// <summary>
    /// Calculates the speed-dependent look-ahead distance.
    /// </summary>
    /// <param name="groundSpeedKnots">Aircraft ground speed in knots</param>
    /// <returns>Look-ahead distance in feet</returns>
    private static double CalculateLookAheadDistance(double groundSpeedKnots)
    {
        double groundSpeedFps = groundSpeedKnots * KNOTS_TO_FEET_PER_SECOND;
        double lookAhead = groundSpeedFps * LOOK_AHEAD_TIME_SECONDS;
        return Math.Clamp(lookAhead, MIN_LOOK_AHEAD_FEET, MAX_LOOK_AHEAD_FEET);
    }

    #region Arc Projection Methods

    /// <summary>
    /// Calculates arc parameters for smooth carrot projection through a turn.
    /// Returns an arc that is tangent to both the incoming and outgoing segments at the turn node.
    /// </summary>
    /// <param name="currentWaypoint">Current waypoint (segment leading into the turn)</param>
    /// <param name="nextWaypoint">Next waypoint (contains TurnAngle at the junction)</param>
    /// <returns>TurnArc with geometry, or IsValid=false if turn angle is too small</returns>
    private static TurnArc CalculateTurnArc(TaxiRouteWaypoint currentWaypoint, TaxiRouteWaypoint nextWaypoint)
    {
        // Check if turn angle exceeds threshold
        double turnAngleDeg = nextWaypoint.TurnAngle;
        if (Math.Abs(turnAngleDeg) <= ARC_MIN_TURN_ANGLE_DEG)
        {
            return new TurnArc { IsValid = false };
        }

        // Get turn node coordinates (target of current segment = start of turn)
        var turnNode = currentWaypoint.TargetNode;
        double turnNodeLat = turnNode.Latitude;
        double turnNodeLon = turnNode.Longitude;

        // Use minimum width of both segments to ensure arc fits
        double width = Math.Min(currentWaypoint.Segment.Width, nextWaypoint.Segment.Width);

        // Calculate radius constrained by taxiway width
        // R = width / (2 * tan(|turnAngle|/2)) ensures arc fits within taxiway
        double halfTurnRad = Math.Abs(turnAngleDeg) * Math.PI / 360.0; // |turnAngle|/2 in radians
        double radius = width / (2.0 * Math.Tan(halfTurnRad));
        radius = Math.Clamp(radius, ARC_MIN_RADIUS_FEET, ARC_MAX_RADIUS_FEET);

        // Calculate entry distance (tangent point offset from node)
        double entryDistance = radius * Math.Tan(halfTurnRad);

        // Calculate arc center position
        // Center is perpendicular to incoming heading at distance R from turn node
        double incomingHeadingDeg = currentWaypoint.Heading;
        double turnSign = Math.Sign(turnAngleDeg);
        double perpHeadingDeg = incomingHeadingDeg + 90.0 * turnSign;
        double perpHeadingRad = perpHeadingDeg * Math.PI / 180.0;

        // Longitude scaling factor for latitude
        double cosLat = Math.Cos(turnNodeLat * Math.PI / 180.0);
        double lonToFeet = LON_TO_FEET_BASE * cosLat;

        // The center is offset from the turn node in the perpendicular direction
        // We need to offset in the direction perpendicular to incoming heading
        double centerLat = turnNodeLat + (radius * Math.Cos(perpHeadingRad)) / LAT_TO_FEET;
        double centerLon = turnNodeLon + (radius * Math.Sin(perpHeadingRad)) / lonToFeet;

        // Calculate start angle (from center to arc entry point)
        // Arc entry is on incoming segment, entryDistance before turn node
        // The direction from center to arc start is opposite to the perpendicular
        double startAngleRad = (perpHeadingDeg + 180.0) * Math.PI / 180.0;

        // Calculate arc length
        double turnAngleRad = turnAngleDeg * Math.PI / 180.0;
        double arcLength = radius * Math.Abs(turnAngleRad);

        return new TurnArc
        {
            CenterLat = centerLat,
            CenterLon = centerLon,
            RadiusFeet = radius,
            ArcLengthFeet = arcLength,
            StartAngleRad = startAngleRad,
            TurnAngleRad = turnAngleRad,
            EntryDistanceFeet = entryDistance,
            IsValid = true
        };
    }

    /// <summary>
    /// Projects the carrot point along a circular arc at the given arc-length distance.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude for bearing calculation</param>
    /// <param name="aircraftLon">Aircraft longitude for bearing calculation</param>
    /// <param name="arc">The turn arc parameters</param>
    /// <param name="distanceAlongArc">Distance in feet from arc start point</param>
    /// <param name="waypointIndex">Waypoint index for return value</param>
    /// <returns>CarrotPosition on the arc</returns>
    private static CarrotPosition ProjectCarrotOnArc(
        double aircraftLat, double aircraftLon,
        TurnArc arc, double distanceAlongArc, int waypointIndex)
    {
        // Calculate angular progress along arc (in radians)
        double theta = distanceAlongArc / arc.RadiusFeet;

        // Calculate current angle from center
        // For right turns (positive TurnAngleRad), angle increases (CW when viewed from above, but we use CCW convention)
        // For left turns (negative TurnAngleRad), angle decreases
        double turnSign = Math.Sign(arc.TurnAngleRad);
        double currentAngle = arc.StartAngleRad + theta * turnSign;

        // Longitude scaling factor
        double cosLat = Math.Cos(arc.CenterLat * Math.PI / 180.0);
        double lonToFeet = LON_TO_FEET_BASE * cosLat;

        // Calculate carrot position on arc
        double carrotLat = arc.CenterLat + (arc.RadiusFeet * Math.Cos(currentAngle)) / LAT_TO_FEET;
        double carrotLon = arc.CenterLon + (arc.RadiusFeet * Math.Sin(currentAngle)) / lonToFeet;

        // Calculate tangent heading at arc position (perpendicular to radius, in direction of travel)
        // Tangent is 90° from the radius direction, adjusted for turn direction
        double tangentHeadingDeg = currentAngle * 180.0 / Math.PI + 90.0 * turnSign;
        tangentHeadingDeg = ((tangentHeadingDeg % 360.0) + 360.0) % 360.0; // Normalize to 0-360

        // Calculate bearing and distance from aircraft to carrot
        double bearingToCarrot = Navigation.NavigationCalculator.CalculateBearing(
            aircraftLat, aircraftLon, carrotLat, carrotLon);
        double distanceToCarrot = Navigation.NavigationCalculator.CalculateDistance(
            aircraftLat, aircraftLon, carrotLat, carrotLon) * 6076.12; // NM to feet

        return new CarrotPosition
        {
            Latitude = carrotLat,
            Longitude = carrotLon,
            Heading = tangentHeadingDeg,
            BearingFromAircraft = bearingToCarrot,
            DistanceToCarrotFeet = distanceToCarrot,
            WaypointIndex = waypointIndex,
            IsAtDestination = false
        };
    }

    /// <summary>
    /// Calculates distance from current along-track position to where the arc begins.
    /// </summary>
    /// <param name="alongTrackFeet">Current position along segment from start</param>
    /// <param name="segmentLength">Total segment length</param>
    /// <param name="arcEntryDistance">Distance before segment end where arc starts</param>
    /// <returns>Distance to arc entry. Positive = before arc, negative = already in arc region</returns>
    private static double GetDistanceToArcEntry(double alongTrackFeet, double segmentLength, double arcEntryDistance)
    {
        double arcEntryPoint = segmentLength - arcEntryDistance;
        return arcEntryPoint - alongTrackFeet;
    }

    #endregion

    #region Recovery Arc Methods

    /// <summary>
    /// Calculates a recovery arc that smoothly guides the aircraft back to the taxiway centerline.
    /// The arc starts tangent to the aircraft's current heading and ends tangent to the taxiway
    /// at the merge angle, providing a gradual return path instead of a harsh 90-degree intercept.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude</param>
    /// <param name="aircraftLon">Aircraft longitude</param>
    /// <param name="aircraftHeading">Aircraft heading in degrees</param>
    /// <param name="crossTrackFeet">Cross-track distance (positive = right of centerline)</param>
    /// <param name="segmentHeading">Taxiway segment heading in degrees</param>
    /// <returns>RecoveryArc with geometry, or IsValid=false if not applicable</returns>
    private RecoveryArc CalculateRecoveryArc(
        double aircraftLat, double aircraftLon, double aircraftHeading,
        double crossTrackFeet, double segmentHeading)
    {
        double absCrossTrack = Math.Abs(crossTrackFeet);

        // Don't create recovery arc for small cross-track errors
        if (absCrossTrack < RECOVERY_ARC_THRESHOLD_FEET)
        {
            return new RecoveryArc { IsValid = false };
        }

        // Determine which side we're on and which way to turn
        bool isRightOfCenterline = crossTrackFeet > 0;

        // Calculate the heading we want at the merge point
        // If right of centerline, we merge from the right at a 20° angle (heading = segmentHeading - 20)
        // If left of centerline, we merge from the left at a 20° angle (heading = segmentHeading + 20)
        double mergeHeadingDeg = isRightOfCenterline
            ? segmentHeading - RECOVERY_MERGE_ANGLE_DEGREES
            : segmentHeading + RECOVERY_MERGE_ANGLE_DEGREES;
        mergeHeadingDeg = ((mergeHeadingDeg % 360.0) + 360.0) % 360.0;

        // Calculate the turn we need to execute (from current heading to merge heading)
        double turnAngleDeg = NormalizeHeading(mergeHeadingDeg - aircraftHeading);

        // Validate the turn direction makes sense (we should turn toward centerline)
        // If right of centerline, we should turn left (negative turn angle)
        // If left of centerline, we should turn right (positive turn angle)
        bool turnDirectionCorrect = (isRightOfCenterline && turnAngleDeg < 0) ||
                                     (!isRightOfCenterline && turnAngleDeg > 0);

        if (!turnDirectionCorrect || Math.Abs(turnAngleDeg) < 5.0)
        {
            // Turn direction is wrong (heading away from centerline) or turn too small
            return new RecoveryArc { IsValid = false };
        }

        // Cap the turn angle to prevent extreme maneuvers
        double clampedTurnAngleDeg = Math.Clamp(turnAngleDeg, -60.0, 60.0);
        double turnAngleRad = clampedTurnAngleDeg * Math.PI / 180.0;

        // Calculate arc radius based on cross-track error and turn angle
        // R = crossTrack / (1 - cos(turnAngle))
        // This ensures the arc reaches the centerline at the end
        double denominator = 1.0 - Math.Cos(Math.Abs(turnAngleRad));
        if (denominator < 0.01) // Avoid division by very small numbers
        {
            return new RecoveryArc { IsValid = false };
        }

        double radius = absCrossTrack / denominator;
        radius = Math.Clamp(radius, RECOVERY_MIN_RADIUS_FEET, RECOVERY_MAX_RADIUS_FEET);

        // Calculate arc center position
        // Center is perpendicular to aircraft heading, in the direction of the turn
        double turnSign = Math.Sign(turnAngleDeg);
        double perpHeadingDeg = aircraftHeading + 90.0 * turnSign;
        double perpHeadingRad = perpHeadingDeg * Math.PI / 180.0;

        double cosLat = Math.Cos(aircraftLat * Math.PI / 180.0);
        double lonToFeet = LON_TO_FEET_BASE * cosLat;

        double centerLat = aircraftLat + (radius * Math.Cos(perpHeadingRad)) / LAT_TO_FEET;
        double centerLon = aircraftLon + (radius * Math.Sin(perpHeadingRad)) / lonToFeet;

        // Calculate start angle (from center to aircraft position)
        // This is opposite to the perpendicular direction
        double startAngleRad = (perpHeadingDeg + 180.0) * Math.PI / 180.0;

        // Calculate arc length
        double arcLength = radius * Math.Abs(turnAngleRad);

        // Calculate merge point on centerline
        // End angle on arc
        double endAngleRad = startAngleRad + turnAngleRad;
        double mergePointLat = centerLat + (radius * Math.Cos(endAngleRad)) / LAT_TO_FEET;
        double mergePointLon = centerLon + (radius * Math.Sin(endAngleRad)) / lonToFeet;

        System.Diagnostics.Debug.WriteLine($"[RecoveryArc] Created: crossTrack={crossTrackFeet:F1}ft, " +
            $"turn={clampedTurnAngleDeg:F1}°, radius={radius:F1}ft, arcLen={arcLength:F1}ft");

        return new RecoveryArc
        {
            CenterLat = centerLat,
            CenterLon = centerLon,
            RadiusFeet = radius,
            ArcLengthFeet = arcLength,
            StartAngleRad = startAngleRad,
            SweepAngleRad = turnAngleRad,
            MergePointLat = mergePointLat,
            MergePointLon = mergePointLon,
            IsValid = true
        };
    }

    /// <summary>
    /// Projects the carrot point along the recovery arc at the given distance.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude for bearing calculation</param>
    /// <param name="aircraftLon">Aircraft longitude for bearing calculation</param>
    /// <param name="arc">The recovery arc parameters</param>
    /// <param name="lookAheadFeet">Look-ahead distance in feet</param>
    /// <param name="waypointIndex">Current waypoint index for return value</param>
    /// <returns>CarrotPosition on the recovery arc</returns>
    private CarrotPosition ProjectCarrotOnRecoveryArc(
        double aircraftLat, double aircraftLon,
        RecoveryArc arc, double lookAheadFeet, int waypointIndex)
    {
        // Calculate how far along the arc the look-ahead distance takes us
        double distanceAlongArc = Math.Min(lookAheadFeet, arc.ArcLengthFeet);

        // Calculate angular progress along arc (in radians)
        double theta = distanceAlongArc / arc.RadiusFeet;

        // Apply sweep direction
        double turnSign = Math.Sign(arc.SweepAngleRad);
        double currentAngle = arc.StartAngleRad + theta * turnSign;

        // Longitude scaling factor
        double cosLat = Math.Cos(arc.CenterLat * Math.PI / 180.0);
        double lonToFeet = LON_TO_FEET_BASE * cosLat;

        // Calculate carrot position on arc
        double carrotLat = arc.CenterLat + (arc.RadiusFeet * Math.Cos(currentAngle)) / LAT_TO_FEET;
        double carrotLon = arc.CenterLon + (arc.RadiusFeet * Math.Sin(currentAngle)) / lonToFeet;

        // Calculate tangent heading at arc position (perpendicular to radius, in direction of travel)
        double tangentHeadingDeg = currentAngle * 180.0 / Math.PI + 90.0 * turnSign;
        tangentHeadingDeg = ((tangentHeadingDeg % 360.0) + 360.0) % 360.0;

        // Calculate bearing and distance from aircraft to carrot
        double bearingToCarrot = Navigation.NavigationCalculator.CalculateBearing(
            aircraftLat, aircraftLon, carrotLat, carrotLon);
        double distanceToCarrot = Navigation.NavigationCalculator.CalculateDistance(
            aircraftLat, aircraftLon, carrotLat, carrotLon) * 6076.12; // NM to feet

        return new CarrotPosition
        {
            Latitude = carrotLat,
            Longitude = carrotLon,
            Heading = tangentHeadingDeg,
            BearingFromAircraft = bearingToCarrot,
            DistanceToCarrotFeet = distanceToCarrot,
            WaypointIndex = waypointIndex,
            IsAtDestination = false
        };
    }

    #endregion

    /// <summary>
    /// Checks cross-track distance for safety warnings using width-relative thresholds.
    /// Does NOT affect steering correction - safety warnings only for gross deviations.
    /// </summary>
    /// <param name="crossTrackFeet">Cross-track distance in feet (positive = right, negative = left)</param>
    /// <param name="segmentWidth">Width of the current taxiway segment in feet</param>
    private void CheckCrossTrackSafetyWarnings(double crossTrackFeet, double segmentWidth)
    {
        DateTime now = DateTime.Now;

        // Throttle warnings to prevent spam
        if ((now - _lastSafetyWarningTime).TotalSeconds < SAFETY_WARNING_INTERVAL_SECONDS)
            return;

        // Calculate width-relative thresholds
        double halfWidth = segmentWidth / 2;
        double driftingThreshold = halfWidth * DRIFTING_WIDTH_FACTOR;
        double offTaxiwayThreshold = halfWidth + OFF_TAXIWAY_BUFFER_FEET;

        double absCrossTrack = Math.Abs(crossTrackFeet);
        string direction = crossTrackFeet > 0 ? "right" : "left";

        if (absCrossTrack >= offTaxiwayThreshold)
        {
            // Severe: off the taxiway
            _announcer.AnnounceImmediate($"Off taxiway, {(int)absCrossTrack} feet {direction}");
            _lastSafetyWarningTime = now;
        }
        else if (absCrossTrack >= driftingThreshold)
        {
            // Warning: drifting toward edge
            _announcer.AnnounceImmediate($"Drifting {direction}");
            _lastSafetyWarningTime = now;
        }
    }

    /// <summary>
    /// Announces carrot pursuit steering correction when it changes.
    /// Uses "on track" terminology - pilot is following the carrot well when within threshold.
    /// </summary>
    private void AnnouncePursuitCorrectionIfNeeded(double steeringCorrectionDegrees)
    {
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - _lastCenterlineAnnouncement;

        if (timeSinceLastAnnouncement.TotalMilliseconds < ANNOUNCEMENT_INTERVAL_MS)
            return;

        int roundedCorrection = (int)Math.Round(steeringCorrectionDegrees);

        // Check if on track (small bearing error to carrot)
        bool onTrack = Math.Abs(steeringCorrectionDegrees) < ON_TRACK_THRESHOLD_DEGREES;

        string announcement;
        int trackingValue;

        if (onTrack)
        {
            announcement = "on track";
            trackingValue = 0;
        }
        else if (roundedCorrection < 0)
        {
            announcement = $"{Math.Abs(roundedCorrection)} left";
            trackingValue = roundedCorrection;
        }
        else
        {
            announcement = $"{roundedCorrection} right";
            trackingValue = roundedCorrection;
        }

        // Only announce if correction changed
        if (_lastAnnouncedCorrectionDegrees.HasValue && trackingValue == _lastAnnouncedCorrectionDegrees.Value)
            return;

        _announcer.AnnounceImmediate(announcement);
        _lastAnnouncedCorrectionDegrees = trackingValue;
        _lastCenterlineAnnouncement = DateTime.Now;
    }

    /// <summary>
    /// Checks for upcoming junction (two-stage: 150ft warning, 50ft show form)
    /// </summary>
    private void CheckForJunction(double latitude, double longitude)
    {
        if (_lockedTargetNode == null || _lockedSegment == null || _graph == null)
            return;

        // Skip if not a junction
        if (!_lockedTargetNode.IsJunction)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiTrack] JunctionCheck: Node has {_lockedTargetNode.ConnectedSegments.Count} connections (not a junction, skipping)");
            return;
        }

        double distanceToNode = _graph.GetDistanceToNode(latitude, longitude, _lockedTargetNode);
        System.Diagnostics.Debug.WriteLine($"[TaxiTrack] JunctionCheck: Distance={distanceToNode:F1}ft IsJunction={_lockedTargetNode.IsJunction} FirstWarnGiven={_junctionFirstWarningGiven} FormPending={_junctionPending}");

        // Stage 1: First warning at 150ft (verbal announcement only)
        if (distanceToNode <= JUNCTION_FIRST_WARNING_FEET && !_junctionFirstWarningGiven)
        {
            var options = _graph.GetJunctionOptions(_lockedTargetNode, _lockedSegment, _lastAircraftHeading);

            if (options.Count > 0)
            {
                _junctionFirstWarningGiven = true;
                _state = GuidanceState.ApproachingJunction;

                // Verbal announcement only at 150ft
                _announcer.AnnounceImmediate($"Junction ahead, {(int)distanceToNode} feet. {options.Count} options available.");

                System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Junction warning: {options.Count} options at {distanceToNode:F0}ft");
            }
        }

        // Stage 2: Show selection form at 50ft
        if (distanceToNode <= JUNCTION_SHOW_FORM_FEET && !_junctionPending)
        {
            var options = _graph.GetJunctionOptions(_lockedTargetNode, _lockedSegment, _lastAircraftHeading);

            if (options.Count > 0)
            {
                _lastJunctionNode = _lockedTargetNode;
                _junctionPending = true;
                _state = GuidanceState.AwaitingJunctionSelection;

                // Raise event for UI to show junction selection form
                JunctionDetected?.Invoke(this, new JunctionEventArgs
                {
                    Node = _lockedTargetNode,
                    Options = options,
                    DistanceFeet = distanceToNode
                });

                System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Junction form shown: {options.Count} options at {distanceToNode:F0}ft");
            }
        }
    }

    /// <summary>
    /// Checks for hold short point ahead
    /// </summary>
    private void CheckForHoldShort(double latitude, double longitude)
    {
        if (_lockedTargetNode == null || _graph == null)
            return;

        if (_lockedTargetNode.Type != TaxiwayNodeType.HoldShort)
            return;

        double distanceToNode = _graph.GetDistanceToNode(latitude, longitude, _lockedTargetNode);
        string runwayName = _lockedTargetNode.HoldShortRunway ?? "runway";

        if (distanceToNode <= HOLD_SHORT_FINAL_FEET)
        {
            HoldShortDetected?.Invoke(this, $"Hold short runway {runwayName}");
        }
        else if (distanceToNode <= HOLD_SHORT_WARNING_FEET)
        {
            HoldShortDetected?.Invoke(this, $"Hold short runway {runwayName} ahead, {(int)distanceToNode} feet");
        }
    }

    /// <summary>
    /// Checks if aircraft is within a parking spot and announces every 5 seconds
    /// </summary>
    private void CheckParkingSpotProximity(double latitude, double longitude)
    {
        if (_parkingSpots.Count == 0)
            return;

        ParkingSpotData? spotAtLocation = FindParkingSpotAtLocation(latitude, longitude);

        if (spotAtLocation != null)
        {
            // Inside a parking spot - announce every 5 seconds
            if (DateTime.Now - _lastParkingAnnouncement >= TimeSpan.FromSeconds(PARKING_ANNOUNCEMENT_INTERVAL_SECONDS))
            {
                _announcer.AnnounceImmediate(spotAtLocation.GetAnnouncementText());
                _lastParkingAnnouncement = DateTime.Now;
            }
            _currentParkingSpot = spotAtLocation;
        }
        else
        {
            _currentParkingSpot = null;
        }
    }

    /// <summary>
    /// Finds the parking spot at the given location (within its radius)
    /// </summary>
    private ParkingSpotData? FindParkingSpotAtLocation(double latitude, double longitude)
    {
        foreach (var spot in _parkingSpots)
        {
            double distanceFeet = Navigation.NavigationCalculator.CalculateDistance(
                latitude, longitude,
                spot.Latitude, spot.Longitude) * 6076.12; // NM to feet

            if (distanceFeet <= spot.RadiusFeet)
                return spot;
        }
        return null;
    }

    /// <summary>
    /// Selects a junction option (called from UI after junction form selection)
    /// </summary>
    public void SelectJunctionOption(JunctionOption option)
    {
        if (_graph == null)
            return;

        // Lock to the new segment
        _lockedSegment = option.Segment;
        _currentTaxiwayName = option.Segment.Name;

        // Find the new target node (the other end of the selected segment)
        var newTarget = option.Segment.GetOtherNode(_lockedTargetNode!);
        if (newTarget != null)
        {
            _lockedTargetNode = newTarget;
        }

        // Reset junction tracking for next junction
        _junctionPending = false;
        _junctionFirstWarningGiven = false;
        _lastJunctionNode = null;
        _relockSuggestionGiven = false;

        // Reset guidance state for new segment
        _lastPositionUpdateTime = DateTime.MinValue;
        _lastAnnouncedCorrectionDegrees = null;

        // Return to active guidance state
        _state = GuidanceState.SegmentLocked;

        string announcement = option.GetDisplayText();
        _announcer.AnnounceImmediate(announcement);

        System.Diagnostics.Debug.WriteLine($"[TaxiTrack] JunctionSelected: Segment '{option.Segment.Name ?? "unnamed"}' NewTarget: ({_lockedTargetNode?.Latitude:F6}, {_lockedTargetNode?.Longitude:F6}) IsJunction={_lockedTargetNode?.IsJunction} Connections={_lockedTargetNode?.ConnectedSegments.Count}");
    }

    /// <summary>
    /// Cancels junction selection - auto-selects continue straight (smallest turn angle)
    /// </summary>
    public void CancelJunctionSelection()
    {
        if (_state != GuidanceState.AwaitingJunctionSelection || _graph == null)
            return;

        // Get junction options and select the "continue straight" one (first in list, sorted by turn angle)
        if (_lockedTargetNode != null && _lockedSegment != null)
        {
            var options = _graph.GetJunctionOptions(_lockedTargetNode, _lockedSegment, _lastAircraftHeading);

            if (options.Count > 0)
            {
                // Options are sorted with straight first, so pick the first one
                var continueOption = options[0];
                SelectJunctionOption(continueOption);
                return;
            }
        }

        // Fallback: just return to locked state without changing segment
        _junctionPending = false;
        _junctionFirstWarningGiven = false;
        _state = GuidanceState.SegmentLocked;
        _announcer.AnnounceImmediate("Continuing on current taxiway");
    }

    /// <summary>
    /// Shows junction options again (for Output Mode > Ctrl+P)
    /// </summary>
    public void ShowJunctionOptions()
    {
        if (!_isActive || _graph == null || _lockedSegment == null || _lockedTargetNode == null)
        {
            _announcer.AnnounceImmediate("No junction available");
            return;
        }

        if (!_lockedTargetNode.IsJunction)
        {
            _announcer.AnnounceImmediate("No junction ahead");
            return;
        }

        var options = _graph.GetJunctionOptions(_lockedTargetNode, _lockedSegment, _lastAircraftHeading);

        if (options.Count == 0)
        {
            _announcer.AnnounceImmediate("No options available");
            return;
        }

        _junctionPending = true;
        JunctionDetected?.Invoke(this, new JunctionEventArgs
        {
            Node = _lockedTargetNode,
            Options = options,
            DistanceFeet = 0 // Manual request
        });
    }

    /// <summary>
    /// Resets the guidance manager state
    /// </summary>
    public void Reset()
    {
        if (_isActive)
        {
            _centerlineTone?.Stop();
            _isActive = false;
            GuidanceActiveChanged?.Invoke(this, false);
        }

        _lockedSegment = null;
        _lockedTargetNode = null;
        _graph = null;
        _junctionPending = false;
        _junctionFirstWarningGiven = false;
        _relockSuggestionGiven = false;
        _activeRoute = null;
        _isOnRoute = true;
        _waypointApproachAnnounced = false;
        _isInitialAlignment = false;
        _state = GuidanceState.Idle;

        System.Diagnostics.Debug.WriteLine("[TaxiwayGuidance] Reset");
    }

    public void Dispose()
    {
        _centerlineTone?.Stop();
        _centerlineTone?.Dispose();
        _centerlineTone = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Event arguments for junction detection
/// </summary>
public class JunctionEventArgs : EventArgs
{
    public required TaxiwayNode Node { get; set; }
    public required List<JunctionOption> Options { get; set; }
    public double DistanceFeet { get; set; }
}

/// <summary>
/// Event arguments for segment selection (initial lock or relock)
/// </summary>
public class SegmentSelectionEventArgs : EventArgs
{
    public required List<SegmentOption> NearbySegments { get; set; }
    public double AircraftLatitude { get; set; }
    public double AircraftLongitude { get; set; }
    public double AircraftHeading { get; set; }
}

/// <summary>
/// Represents a segment option for initial selection or relock
/// </summary>
public class SegmentOption
{
    public required TaxiwaySegment Segment { get; set; }
    public required string TaxiwayName { get; set; }  // "Taxiway A" or "Connector to B and C"
    public double Heading { get; set; }  // Direction of travel
    public double DistanceFeet { get; set; }  // Distance from aircraft
    public string RelativeDirection { get; set; } = "";  // "ahead", "behind", "to your left", etc.
    public required TaxiwayNode TargetNode { get; set; }  // The node we'd be heading toward

    /// <summary>
    /// Gets display text for this option
    /// </summary>
    public string GetDisplayText()
    {
        return $"{TaxiwayName} heading {(int)Heading:000} - {RelativeDirection} ({(int)DistanceFeet} ft)";
    }

    public override string ToString() => GetDisplayText();
}
