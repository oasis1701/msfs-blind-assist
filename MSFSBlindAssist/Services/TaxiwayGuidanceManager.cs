using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Manages taxiway guidance for blind pilots
/// </summary>
public class TaxiwayGuidanceManager : IDisposable
{
    private readonly ScreenReaderAnnouncer _announcer;
    private TaxiwayGraph? _graph;
    private bool _isActive;

    // Current guidance state
    private TaxiwaySegment? _currentSegment;
    private TaxiwayNode? _upcomingNode;
    private string? _currentTaxiwayName;
    private double _lastAircraftHeading;

    // Audio tone generator for centerline guidance
    private AudioToneGenerator? _centerlineTone;
    private readonly HandFlyWaveType _toneWaveType;
    private readonly double _toneVolume;

    // Tracking state
    private double _lastAnnouncedCrossTrackFeet = 0;
    private DateTime _lastCenterlineAnnouncement = DateTime.MinValue;
    private DateTime _lastTaxiwayChangeAnnouncement = DateTime.MinValue;
    private TaxiwayNode? _lastJunctionNode;
    private bool _junctionPending;
    private DateTime _lastPerpendicularAnnouncement = DateTime.MinValue;

    // Parking spot detection
    private List<ParkingSpotData> _parkingSpots = new();
    private ParkingSpotData? _currentParkingSpot;
    private DateTime _lastParkingAnnouncement = DateTime.MinValue;

    // Configuration constants
    private const int ANNOUNCEMENT_INTERVAL_MS = 500;
    private const double CENTERLINE_TOLERANCE_FEET = 15.0; // Within ±15 feet is "on centerline"
    private const double CENTERLINE_CHANGE_THRESHOLD_FEET = 10.0;
    private const double PAN_FULL_RANGE_FEET = 40.0; // ±40 feet for full left/right pan
    private const double JUNCTION_DETECTION_FEET = 100.0; // Distance to trigger junction detection
    private const double HOLD_SHORT_WARNING_FEET = 200.0; // First hold short warning
    private const double HOLD_SHORT_FINAL_FEET = 50.0; // Final hold short warning
    private const double SEGMENT_SEARCH_RADIUS_FEET = 150.0; // Max distance to search for segments
    private const double PERPENDICULAR_THRESHOLD_DEGREES = 45.0; // Misalignment threshold
    private const double PERPENDICULAR_REMINDER_SECONDS = 5.0; // Reminder interval when perpendicular
    private const double PARKING_ANNOUNCEMENT_INTERVAL_SECONDS = 5.0; // Interval for parking spot announcements

    public bool IsActive => _isActive;
    public bool HasGraph => _graph != null;
    public string? CurrentAirport => _graph?.AirportIcao;
    public TaxiwaySegment? CurrentSegment => _currentSegment;
    public bool IsJunctionPending => _junctionPending;

    public event EventHandler<bool>? GuidanceActiveChanged;
    public event EventHandler<JunctionEventArgs>? JunctionDetected;
    public event EventHandler<string>? HoldShortDetected;

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
    /// Starts taxiway guidance
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

        // Find nearest segment
        var result = _graph.FindNearestSegment(currentLat, currentLon, currentHeading, SEGMENT_SEARCH_RADIUS_FEET);

        if (result == null)
        {
            _announcer.AnnounceImmediate("No taxiway found nearby. Move closer to a taxiway.");
            return;
        }

        _currentSegment = result.Value.Segment;
        _currentTaxiwayName = _currentSegment.Name;
        _lastAircraftHeading = currentHeading;
        _upcomingNode = _graph.GetAheadNode(_currentSegment, currentHeading);

        // Reset tracking state
        _lastAnnouncedCrossTrackFeet = 0;
        _lastCenterlineAnnouncement = DateTime.MinValue;
        _lastTaxiwayChangeAnnouncement = DateTime.MinValue;
        _lastJunctionNode = null;
        _junctionPending = false;
        _lastPerpendicularAnnouncement = DateTime.MinValue;

        // Start audio tone
        _centerlineTone?.Start(_toneWaveType, _toneVolume);

        _isActive = true;
        GuidanceActiveChanged?.Invoke(this, true);

        // Announce activation - check if perpendicular to taxiway
        if (IsPerpendicular(currentHeading, _currentSegment.Heading))
        {
            // Perpendicular: announce that taxiway extends left and right
            AnnouncePerpendicularIfNeeded(isStartup: true);
        }
        else
        {
            // Aligned: normal announcement
            string taxiwayText = _currentSegment.HasName ? $"On taxiway {_currentSegment.Name}" : "On connector";
            _announcer.AnnounceImmediate($"Taxiway guidance active. {taxiwayText}");
        }

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Started on {_currentSegment}");
    }

    /// <summary>
    /// Stops taxiway guidance
    /// </summary>
    public void StopGuidance()
    {
        if (!_isActive)
            return;

        _centerlineTone?.Stop();
        _isActive = false;
        _currentSegment = null;
        _upcomingNode = null;
        _junctionPending = false;

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
    /// Process position update for guidance
    /// </summary>
    public void ProcessPositionUpdate(double latitude, double longitude, double heading)
    {
        if (!_isActive || _graph == null || _currentSegment == null)
            return;

        _lastAircraftHeading = heading;

        // Find nearest segment (may have changed)
        var result = _graph.FindNearestSegment(latitude, longitude, heading, SEGMENT_SEARCH_RADIUS_FEET);

        if (result == null)
        {
            // Off all taxiways - announce distance to nearest
            var anyResult = _graph.FindNearestSegment(latitude, longitude, null, 500.0);
            if (anyResult != null && DateTime.Now - _lastCenterlineAnnouncement > TimeSpan.FromSeconds(2))
            {
                _announcer.AnnounceImmediate($"Off taxiway, {(int)anyResult.Value.DistanceFeet} feet from nearest");
                _lastCenterlineAnnouncement = DateTime.Now;
            }
            return;
        }

        var (segment, distanceFeet, crossTrackFeet) = result.Value;

        // Check if we've changed segments
        if (segment != _currentSegment)
        {
            HandleSegmentChange(segment);
        }

        // Update upcoming node based on heading
        _upcomingNode = _graph.GetAheadNode(_currentSegment, heading);

        // Calculate and apply audio panning
        float pan = (float)Math.Clamp(-crossTrackFeet / PAN_FULL_RANGE_FEET, -1.0, 1.0);
        _centerlineTone?.SetPan(pan);

        // Announce deviation if changed significantly
        AnnounceCrossTrackIfNeeded(crossTrackFeet);

        // Check for junction ahead
        CheckForJunction(latitude, longitude);

        // Check for hold short
        CheckForHoldShort(latitude, longitude);

        // Check for parking spot proximity
        CheckParkingSpotProximity(latitude, longitude);

        // Periodic reminder if still perpendicular to taxiway
        AnnouncePerpendicularIfNeeded(isStartup: false);
    }

    /// <summary>
    /// Handles segment change (taxiway change announcement)
    /// </summary>
    private void HandleSegmentChange(TaxiwaySegment newSegment)
    {
        string? oldName = _currentTaxiwayName;
        string? newName = newSegment.Name;

        _currentSegment = newSegment;
        _currentTaxiwayName = newName;

        // Clear junction state if we've moved to a new segment
        if (_junctionPending)
        {
            _junctionPending = false;
        }

        // Announce taxiway change if name changed
        if (newName != oldName && DateTime.Now - _lastTaxiwayChangeAnnouncement > TimeSpan.FromSeconds(1))
        {
            if (newSegment.HasName)
            {
                _announcer.AnnounceImmediate($"Now on taxiway {newName}");
            }
            _lastTaxiwayChangeAnnouncement = DateTime.Now;
        }

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Segment changed to {newSegment}");
    }

    /// <summary>
    /// Announces cross-track deviation if needed
    /// </summary>
    private void AnnounceCrossTrackIfNeeded(double crossTrackFeet)
    {
        double deviationChange = Math.Abs(crossTrackFeet - _lastAnnouncedCrossTrackFeet);
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - _lastCenterlineAnnouncement;

        if (deviationChange >= CENTERLINE_CHANGE_THRESHOLD_FEET &&
            timeSinceLastAnnouncement.TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
        {
            string announcement = FormatCrossTrackAnnouncement(crossTrackFeet);
            _announcer.AnnounceImmediate(announcement);

            _lastAnnouncedCrossTrackFeet = crossTrackFeet;
            _lastCenterlineAnnouncement = DateTime.Now;
        }
    }

    /// <summary>
    /// Checks if aircraft is perpendicular to the taxiway (misaligned by more than threshold)
    /// </summary>
    private bool IsPerpendicular(double aircraftHeading, double segmentHeading)
    {
        // Calculate alignment with segment (either direction)
        double headingDiff = Math.Abs(aircraftHeading - segmentHeading);
        if (headingDiff > 180) headingDiff = 360 - headingDiff;

        double reverseHeadingDiff = Math.Abs(aircraftHeading - ((segmentHeading + 180) % 360));
        if (reverseHeadingDiff > 180) reverseHeadingDiff = 360 - reverseHeadingDiff;

        double alignmentDiff = Math.Min(headingDiff, reverseHeadingDiff);

        return alignmentDiff > PERPENDICULAR_THRESHOLD_DEGREES;
    }

    /// <summary>
    /// Announces perpendicular taxiway direction if applicable
    /// </summary>
    private void AnnouncePerpendicularIfNeeded(bool isStartup = false)
    {
        if (_currentSegment == null)
            return;

        if (!IsPerpendicular(_lastAircraftHeading, _currentSegment.Heading))
        {
            // Not perpendicular, reset timer
            _lastPerpendicularAnnouncement = DateTime.MinValue;
            return;
        }

        // Check if enough time has passed for a reminder
        if (!isStartup && DateTime.Now - _lastPerpendicularAnnouncement < TimeSpan.FromSeconds(PERPENDICULAR_REMINDER_SECONDS))
            return;

        string taxiwayName = _currentSegment.HasName ? $"Taxiway {_currentSegment.Name}" : "Taxiway";

        if (isStartup)
        {
            _announcer.AnnounceImmediate($"Taxiway guidance active. {taxiwayName} extends left and right");
        }
        else
        {
            _announcer.AnnounceImmediate($"{taxiwayName} extends left and right");
        }

        _lastPerpendicularAnnouncement = DateTime.Now;
    }

    /// <summary>
    /// Formats cross-track deviation announcement
    /// </summary>
    private static string FormatCrossTrackAnnouncement(double crossTrackFeet)
    {
        if (Math.Abs(crossTrackFeet) <= CENTERLINE_TOLERANCE_FEET)
        {
            return "on centerline";
        }

        int deviationFeet = (int)Math.Round(Math.Abs(crossTrackFeet));
        string direction = crossTrackFeet > 0 ? "right" : "left";

        return $"{deviationFeet} feet {direction}";
    }

    /// <summary>
    /// Checks for upcoming junction
    /// </summary>
    private void CheckForJunction(double latitude, double longitude)
    {
        if (_upcomingNode == null || _currentSegment == null || _graph == null)
            return;

        // Don't re-announce the same junction
        if (_upcomingNode == _lastJunctionNode && _junctionPending)
            return;

        double distanceToNode = _graph.GetDistanceToNode(latitude, longitude, _upcomingNode);

        // Check if approaching a junction
        if (distanceToNode <= JUNCTION_DETECTION_FEET && _upcomingNode.IsJunction)
        {
            // Get junction options
            var options = _graph.GetJunctionOptions(_upcomingNode, _currentSegment, _lastAircraftHeading);

            if (options.Count > 0)
            {
                _lastJunctionNode = _upcomingNode;
                _junctionPending = true;

                // Raise event for UI to show junction selection
                JunctionDetected?.Invoke(this, new JunctionEventArgs
                {
                    Node = _upcomingNode,
                    Options = options,
                    DistanceFeet = distanceToNode
                });

                System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Junction detected: {options.Count} options at {distanceToNode:F0}ft");
            }
        }
    }

    /// <summary>
    /// Checks for hold short point ahead
    /// </summary>
    private void CheckForHoldShort(double latitude, double longitude)
    {
        if (_upcomingNode == null || _graph == null)
            return;

        if (_upcomingNode.Type != TaxiwayNodeType.HoldShort)
            return;

        double distanceToNode = _graph.GetDistanceToNode(latitude, longitude, _upcomingNode);
        string runwayName = _upcomingNode.HoldShortRunway ?? "runway";

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
    /// Selects a junction option (called from UI)
    /// </summary>
    public void SelectJunctionOption(JunctionOption option)
    {
        if (!_isActive || _graph == null)
            return;

        _currentSegment = option.Segment;
        _currentTaxiwayName = option.Segment.Name;
        _junctionPending = false;

        // Find the new upcoming node
        var newUpcoming = option.Segment.GetOtherNode(_upcomingNode!);
        if (newUpcoming != null)
        {
            _upcomingNode = newUpcoming;
        }

        string announcement = option.GetDisplayText();
        _announcer.AnnounceImmediate(announcement);

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGuidance] Junction option selected: {option}");
    }

    /// <summary>
    /// Shows junction options again (for Output Mode > Ctrl+P)
    /// </summary>
    public void ShowJunctionOptions()
    {
        if (!_isActive || _graph == null || _currentSegment == null || _upcomingNode == null)
        {
            _announcer.AnnounceImmediate("No junction available");
            return;
        }

        if (!_upcomingNode.IsJunction)
        {
            _announcer.AnnounceImmediate("No junction ahead");
            return;
        }

        var options = _graph.GetJunctionOptions(_upcomingNode, _currentSegment, _lastAircraftHeading);

        if (options.Count == 0)
        {
            _announcer.AnnounceImmediate("No options available");
            return;
        }

        _junctionPending = true;
        JunctionDetected?.Invoke(this, new JunctionEventArgs
        {
            Node = _upcomingNode,
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

        _currentSegment = null;
        _upcomingNode = null;
        _graph = null;
        _junctionPending = false;

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
