using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;
/// <summary>
/// Manages waypoint tracking slots for quick access via hotkeys
/// </summary>
public class WaypointTracker
{
    private readonly TrackedWaypoint[] _slots;
    private const int MAX_SLOTS = 5;

    public WaypointTracker()
    {
        _slots = new TrackedWaypoint[MAX_SLOTS];
    }

    /// <summary>
    /// Tracks a waypoint in the specified slot (1-5)
    /// </summary>
    public void TrackWaypoint(int slotNumber, WaypointFix waypoint)
    {
        if (slotNumber < 1 || slotNumber > MAX_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

        if (waypoint == null)
            throw new ArgumentNullException(nameof(waypoint));

        int index = slotNumber - 1;
        _slots[index] = new TrackedWaypoint
        {
            Ident = waypoint.Ident,
            Section = waypoint.Section,
            Latitude = waypoint.Latitude,
            Longitude = waypoint.Longitude
        };
    }

    /// <summary>
    /// Gets tracked waypoint info with current distance and bearing
    /// </summary>
    /// <param name="slotNumber">Slot number (1-5)</param>
    /// <param name="aircraftLatitude">Aircraft latitude</param>
    /// <param name="aircraftLongitude">Aircraft longitude</param>
    /// <param name="magneticVariation">Magnetic variation in degrees</param>
    /// <returns>Formatted announcement string or null if slot is empty</returns>
    public string? GetTrackedWaypointInfo(int slotNumber, double aircraftLatitude, double aircraftLongitude, double magneticVariation)
    {
        if (slotNumber < 1 || slotNumber > MAX_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

        int index = slotNumber - 1;
        var tracked = _slots[index];

        if (tracked == null)
            return null;

        // Calculate current distance and bearing from stored coordinates
        double distance = NavigationCalculator.CalculateDistance(
            aircraftLatitude, aircraftLongitude,
            tracked.Latitude, tracked.Longitude);

        double bearing = NavigationCalculator.CalculateMagneticBearing(
            aircraftLatitude, aircraftLongitude,
            tracked.Latitude, tracked.Longitude,
            magneticVariation);

        // Format: "Waypoint name, distance nm, bearing degrees"
        return $"{tracked.Ident}, {distance:F0} nautical miles, {bearing:F0} degrees";
    }

    /// <summary>
    /// Checks if a slot is empty
    /// </summary>
    public bool IsSlotEmpty(int slotNumber)
    {
        if (slotNumber < 1 || slotNumber > MAX_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

        int index = slotNumber - 1;
        return _slots[index] == null;
    }

    /// <summary>
    /// Clears a tracking slot
    /// </summary>
    public void ClearSlot(int slotNumber)
    {
        if (slotNumber < 1 || slotNumber > MAX_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

        int index = slotNumber - 1;
        _slots[index] = null!;
    }

    /// <summary>
    /// Clears all tracking slots
    /// </summary>
    public void ClearAllSlots()
    {
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _slots[i] = null!;
        }
    }

    /// <summary>
    /// Gets the waypoint identifier for a slot (for display purposes)
    /// </summary>
    public string? GetSlotIdent(int slotNumber)
    {
        if (slotNumber < 1 || slotNumber > MAX_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

        int index = slotNumber - 1;
        return _slots[index]?.Ident;
    }

    /// <summary>
    /// Internal class to store tracked waypoint information
    /// </summary>
    private class TrackedWaypoint
    {
        public required string Ident { get; set; }
        public FlightPlanSection Section { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
