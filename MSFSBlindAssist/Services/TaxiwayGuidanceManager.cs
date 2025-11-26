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
    AtDestination
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

    // PID Controller constants for taxi steering guidance
    private const double TAXI_PROPORTIONAL_GAIN = 0.15;     // Degrees per foot of cross-track error
    private const double TAXI_DERIVATIVE_GAIN = 1.0;        // Damping for rate of drift
    private const double TAXI_HEADING_GAIN = 0.5;           // Weight for heading alignment
    private const double TAXI_MAX_CORRECTION_DEGREES = 10.0; // Limit correction to ±10°
    private const double TAXI_ON_PATH_THRESHOLD = 0.5;      // Within 0.5° = "on path"
    private const double TAXI_CENTERLINE_CAPTURE = 5.0;     // Within 5 feet = captured

    // PID state tracking
    private double _lastCrossTrackFeet = 0;
    private DateTime _lastPositionUpdateTime = DateTime.MinValue;
    private int? _lastAnnouncedCorrectionDegrees = null;
    private double _lastGroundSpeedKnots = 10.0; // Default assumption

    public bool IsActive => _isActive;
    public bool HasGraph => _graph != null;
    public string? CurrentAirport => _graph?.AirportIcao;
    public TaxiwaySegment? CurrentSegment => _lockedSegment;
    public TaxiwaySegment? LockedSegment => _lockedSegment;
    public bool IsJunctionPending => _junctionPending;
    public GuidanceState State => _state;

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

        // Find nearby segments for selection
        var nearbySegments = _graph.FindNearbySegments(currentLat, currentLon, currentHeading, SEGMENT_SEARCH_RADIUS_FEET);

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

        // Reset PID state
        _lastCrossTrackFeet = 0;
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

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Locked to {selectedOption}");
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

        // Find nearby segments for selection
        var nearbySegments = _graph.FindNearbySegments(currentLat, currentLon, currentHeading, SEGMENT_SEARCH_RADIUS_FEET);

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
    /// Process position update for guidance (uses LOCKED segment, not nearest)
    /// </summary>
    public void ProcessPositionUpdate(double latitude, double longitude, double heading, double groundSpeedKnots)
    {
        // Check parking spot proximity during active guidance
        CheckParkingSpotProximity(latitude, longitude);

        // Only process guidance-specific logic in active states
        if (_state != GuidanceState.SegmentLocked && _state != GuidanceState.ApproachingJunction)
            return;

        if (_graph == null || _lockedSegment == null || _lockedTargetNode == null)
            return;

        _lastAircraftHeading = heading;
        _lastGroundSpeedKnots = groundSpeedKnots;

        // Calculate cross-track from LOCKED segment
        double crossTrackFeet = _graph.CalculateCrossTrackFromSegment(
            latitude, longitude, _lockedSegment, heading);

        // Get target heading based on travel direction
        double targetHeading = GetTargetHeading();

        // Calculate PID steering correction
        double steeringCorrection = CalculateSteeringCorrection(
            crossTrackFeet, targetHeading, heading);

        // Audio panning: Pan in direction of CORRECTION (where to go), not deviation
        // Positive correction (turn right) → pan right (positive)
        // Negative correction (turn left) → pan left (negative)
        float pan = (float)Math.Clamp(steeringCorrection / TAXI_MAX_CORRECTION_DEGREES, -1.0, 1.0);
        _centerlineTone?.SetPan(pan);

        // Announce steering correction if changed
        AnnounceSteeringCorrectionIfNeeded(steeringCorrection, crossTrackFeet);

        // Check for excessive deviation - suggest relock at 200ft
        CheckForDeviationWarning(crossTrackFeet);

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
    /// Calculates desired steering correction using PID controller
    /// </summary>
    private double CalculateSteeringCorrection(double crossTrackFeet, double targetHeading, double aircraftHeading)
    {
        // Calculate time delta for derivative term
        DateTime now = DateTime.Now;
        double deltaSeconds = _lastPositionUpdateTime == DateTime.MinValue
            ? 0.1
            : (now - _lastPositionUpdateTime).TotalSeconds;
        deltaSeconds = Math.Max(0.01, Math.Min(deltaSeconds, 1.0)); // Clamp to reasonable range

        // Calculate cross-track rate (feet per second)
        double crossTrackRate = (crossTrackFeet - _lastCrossTrackFeet) / deltaSeconds;

        // Store for next iteration
        _lastCrossTrackFeet = crossTrackFeet;
        _lastPositionUpdateTime = now;

        // Heading error (how well aligned with taxiway)
        double headingError = NormalizeHeading(targetHeading - aircraftHeading);

        // PID calculation
        // P: Cross-track error drives turn toward centerline
        //    Positive cross-track (right of center) → negative correction (turn left)
        double proportionalTerm = -TAXI_PROPORTIONAL_GAIN * crossTrackFeet;

        // D: Rate damping prevents overshoot
        //    Positive rate (moving right) → negative correction (turn left to stop drift)
        double derivativeTerm = -TAXI_DERIVATIVE_GAIN * crossTrackRate;

        // H: Heading alignment ensures nose points along taxiway once on centerline
        double headingTerm = TAXI_HEADING_GAIN * headingError;

        // Combine terms
        double correction = proportionalTerm + derivativeTerm + headingTerm;

        // Clamp to max correction
        correction = Math.Clamp(correction, -TAXI_MAX_CORRECTION_DEGREES, TAXI_MAX_CORRECTION_DEGREES);

        return correction;
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
    /// Announces steering correction (degrees left/right) when it changes
    /// </summary>
    private void AnnounceSteeringCorrectionIfNeeded(double steeringCorrectionDegrees, double crossTrackFeet)
    {
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - _lastCenterlineAnnouncement;

        if (timeSinceLastAnnouncement.TotalMilliseconds < ANNOUNCEMENT_INTERVAL_MS)
            return;

        int roundedCorrection = (int)Math.Round(steeringCorrectionDegrees);

        // Check if on path (small correction AND near centerline)
        bool onPath = Math.Abs(steeringCorrectionDegrees) < TAXI_ON_PATH_THRESHOLD
                      && Math.Abs(crossTrackFeet) < TAXI_CENTERLINE_CAPTURE;

        string announcement;
        int trackingValue;

        if (onPath)
        {
            announcement = "on path";
            trackingValue = 0;
        }
        else if (roundedCorrection < 0)
        {
            announcement = $"{Math.Abs(roundedCorrection)} left";
            trackingValue = roundedCorrection;
        }
        else if (roundedCorrection > 0)
        {
            announcement = $"{roundedCorrection} right";
            trackingValue = roundedCorrection;
        }
        else
        {
            // Rounded to 0 but not quite on path
            announcement = "on path";
            trackingValue = 0;
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
            return;

        double distanceToNode = _graph.GetDistanceToNode(latitude, longitude, _lockedTargetNode);

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

        // Reset PID state for new segment
        _lastCrossTrackFeet = 0;
        _lastPositionUpdateTime = DateTime.MinValue;
        _lastAnnouncedCorrectionDegrees = null;

        // Return to active guidance state
        _state = GuidanceState.SegmentLocked;

        string announcement = option.GetDisplayText();
        _announcer.AnnounceImmediate(announcement);

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Junction option selected: {option}");
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
