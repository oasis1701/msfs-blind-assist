using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Type of waypoint along a taxi route
/// </summary>
public enum WaypointType
{
    /// <summary>Normal junction/turn point</summary>
    Normal,
    /// <summary>Transition to a different named taxiway</summary>
    TaxiwayChange,
    /// <summary>Runway hold short point</summary>
    HoldShort,
    /// <summary>Final destination (gate or hold short)</summary>
    Destination
}

/// <summary>
/// Represents a waypoint along a taxi route
/// </summary>
public class TaxiRouteWaypoint
{
    /// <summary>
    /// The segment to follow to reach this waypoint's target node
    /// </summary>
    public required TaxiwaySegment Segment { get; set; }

    /// <summary>
    /// The node we're heading toward (the end of this segment in our direction)
    /// </summary>
    public required TaxiwayNode TargetNode { get; set; }

    /// <summary>
    /// Type of waypoint (affects announcements)
    /// </summary>
    public WaypointType Type { get; set; } = WaypointType.Normal;

    /// <summary>
    /// Turn direction to enter this segment (relative to previous segment)
    /// </summary>
    public TurnDirection TurnDirection { get; set; } = TurnDirection.Straight;

    /// <summary>
    /// Turn angle in degrees (positive = right, negative = left)
    /// </summary>
    public double TurnAngle { get; set; }

    /// <summary>
    /// Announcement when approaching this waypoint (150ft out)
    /// </summary>
    public string ApproachAnnouncement { get; set; } = "";

    /// <summary>
    /// Announcement when passing this waypoint
    /// </summary>
    public string PassAnnouncement { get; set; } = "";

    /// <summary>
    /// Distance from the previous waypoint in feet
    /// </summary>
    public double DistanceFromPreviousFeet { get; set; }

    /// <summary>
    /// Name of the taxiway this segment belongs to (or "connector")
    /// </summary>
    public string TaxiwayName => Segment.HasName ? Segment.Name! : "connector";

    /// <summary>
    /// Heading to follow on this segment (toward target node)
    /// </summary>
    public double Heading { get; set; }

    public override string ToString()
    {
        string typeStr = Type switch
        {
            WaypointType.TaxiwayChange => $"→ Taxiway {TaxiwayName}",
            WaypointType.HoldShort => $"Hold Short {TargetNode.HoldShortRunway}",
            WaypointType.Destination => TargetNode.Type switch
            {
                TaxiwayNodeType.HoldShort => $"Hold Short {TargetNode.HoldShortRunway}",
                TaxiwayNodeType.Parking => TargetNode.ParkingSpotName ?? "Destination",
                _ => "Destination"
            },
            _ => $"via {(Segment.HasName ? $"Taxiway {TaxiwayName}" : "connector")}"
        };
        return $"{typeStr}, heading {Heading:000}, {DistanceFromPreviousFeet:F0}ft";
    }
}

/// <summary>
/// Represents a planned taxi route from current position to destination
/// </summary>
public class TaxiRoute
{
    /// <summary>
    /// Ordered list of waypoints along the route
    /// </summary>
    public List<TaxiRouteWaypoint> Waypoints { get; } = new();

    /// <summary>
    /// Current waypoint index being navigated toward
    /// </summary>
    public int CurrentWaypointIndex { get; set; } = 0;

    /// <summary>
    /// The final destination node (HoldShort or Parking)
    /// </summary>
    public TaxiwayNode? DestinationNode { get; set; }

    /// <summary>
    /// Human-readable destination description (e.g., "Runway 27L" or "Gate A5")
    /// </summary>
    public string DestinationDescription { get; set; } = "";

    /// <summary>
    /// Total route distance in feet
    /// </summary>
    public double TotalDistanceFeet { get; set; }

    /// <summary>
    /// Ordered list of taxiway names the user selected
    /// </summary>
    public List<string> SelectedTaxiways { get; set; } = new();

    /// <summary>
    /// Gets the current waypoint we're navigating toward
    /// </summary>
    public TaxiRouteWaypoint? CurrentWaypoint =>
        CurrentWaypointIndex < Waypoints.Count ? Waypoints[CurrentWaypointIndex] : null;

    /// <summary>
    /// Gets the next waypoint (for lookahead announcements)
    /// </summary>
    public TaxiRouteWaypoint? NextWaypoint =>
        CurrentWaypointIndex + 1 < Waypoints.Count ? Waypoints[CurrentWaypointIndex + 1] : null;

    /// <summary>
    /// Gets the previous waypoint (for tracking where we came from)
    /// </summary>
    public TaxiRouteWaypoint? PreviousWaypoint =>
        CurrentWaypointIndex > 0 ? Waypoints[CurrentWaypointIndex - 1] : null;

    /// <summary>
    /// Whether we've reached the final destination
    /// </summary>
    public bool IsComplete => CurrentWaypointIndex >= Waypoints.Count;

    /// <summary>
    /// Number of waypoints remaining (including current)
    /// </summary>
    public int RemainingWaypoints => Math.Max(0, Waypoints.Count - CurrentWaypointIndex);

    /// <summary>
    /// Advances to the next waypoint
    /// </summary>
    /// <returns>True if advanced, false if already at end</returns>
    public bool AdvanceWaypoint()
    {
        if (CurrentWaypointIndex < Waypoints.Count)
        {
            CurrentWaypointIndex++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets remaining distance from current waypoint to destination
    /// </summary>
    public double GetRemainingDistanceFeet()
    {
        double remaining = 0;
        for (int i = CurrentWaypointIndex; i < Waypoints.Count; i++)
        {
            remaining += Waypoints[i].DistanceFromPreviousFeet;
        }
        return remaining;
    }

    public override string ToString()
    {
        return $"Route to {DestinationDescription}: {Waypoints.Count} waypoints, {TotalDistanceFeet:F0}ft total, currently at waypoint {CurrentWaypointIndex + 1}";
    }
}

/// <summary>
/// Result of route building attempt
/// </summary>
public class TaxiRouteResult
{
    /// <summary>
    /// Whether the route was successfully built
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The built route (null if unsuccessful)
    /// </summary>
    public TaxiRoute? Route { get; set; }

    /// <summary>
    /// Error message if unsuccessful
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static TaxiRouteResult Succeeded(TaxiRoute route) => new()
    {
        Success = true,
        Route = route
    };

    /// <summary>
    /// Creates a failed result with error message
    /// </summary>
    public static TaxiRouteResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
