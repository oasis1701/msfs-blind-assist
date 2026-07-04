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
    /// Tracks a waypoint in the specified slot (1-5).
    /// </summary>
    /// <param name="crossingAltitude">Optional crossing-altitude target (feet MSL) for the Waypoint
    ///   Flight Director's vertical guidance. Null = lateral-only (no vertical command at this fix).</param>
    /// <param name="crossingAltitudeUpper">Optional upper bound (feet MSL) for a BETWEEN constraint.</param>
    /// <param name="constraint">How <paramref name="crossingAltitude"/> is interpreted by the FD.</param>
    /// <param name="course">Optional magnetic course (degrees) for the Waypoint Flight Director to
    ///   capture and hold THROUGH this fix (airway leg / radial / approach course), instead of a
    ///   direct-to. Null = direct-to.</param>
    /// <param name="referenceMagVar">Magnetic variation (EAST-positive) the <paramref name="course"/> is
    ///   referenced to (navaid station declination / fix local variation). Null → the FD converts the
    ///   course using the aircraft's live magvar instead. Ignored when <paramref name="course"/> is null.</param>
    public void TrackWaypoint(int slotNumber, WaypointFix waypoint,
        double? crossingAltitude = null, double? crossingAltitudeUpper = null,
        AltitudeConstraintType constraint = AltitudeConstraintType.None,
        double? course = null, double? referenceMagVar = null)
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
            Longitude = waypoint.Longitude,
            CrossingAltitude = crossingAltitude,
            CrossingAltitudeUpper = crossingAltitudeUpper,
            Constraint = constraint,
            Course = course,
            ReferenceMagVar = course.HasValue ? referenceMagVar : null
        };
    }

    /// <summary>
    /// Returns a snapshot of the slot's data for the Waypoint Flight Director, or null if empty.
    /// </summary>
    public WaypointSlotData? GetSlot(int slotNumber)
    {
        if (slotNumber < 1 || slotNumber > MAX_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

        var t = _slots[slotNumber - 1];
        if (t == null) return null;
        return new WaypointSlotData(t.Ident, t.Latitude, t.Longitude,
            t.CrossingAltitude, t.CrossingAltitudeUpper, t.Constraint, t.Course, t.ReferenceMagVar);
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

    /// <summary>True if at least one slot (1-5) holds a tracked waypoint.</summary>
    public bool HasAnyWaypoint()
    {
        for (int i = 0; i < MAX_SLOTS; i++)
            if (_slots[i] != null) return true;
        return false;
    }

    /// <summary>
    /// The first FILLED slot at or after <paramref name="startInclusive"/>, skipping empty (gap) slots,
    /// or 0 if none remain through slot 5. Single source of truth for the Waypoint Flight Director's
    /// "start on / advance to the next tracked slot" sequencing (used by both Initialize and AdvanceLeg).
    /// </summary>
    public int NextFilledSlot(int startInclusive)
    {
        for (int s = System.Math.Max(1, startInclusive); s <= MAX_SLOTS; s++)
            if (!IsSlotEmpty(s)) return s;
        return 0;
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
        // Optional vertical guidance for the Waypoint Flight Director.
        public double? CrossingAltitude { get; set; }
        public double? CrossingAltitudeUpper { get; set; }
        public AltitudeConstraintType Constraint { get; set; } = AltitudeConstraintType.None;
        public double? Course { get; set; }
        public double? ReferenceMagVar { get; set; }
    }
}

/// <summary>
/// Public read-only snapshot of a tracked slot, consumed by the Waypoint Flight Director.
/// </summary>
public readonly record struct WaypointSlotData(
    string Ident,
    double Latitude,
    double Longitude,
    double? CrossingAltitude,
    double? CrossingAltitudeUpper,
    AltitudeConstraintType Constraint,
    double? Course,
    double? ReferenceMagVar);
