using System;
using System.Collections.Generic;
using System.Linq;
using FBWBA.Database.Models;

namespace FBWBA.Navigation
{
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
        /// <param name="flightPlan">Current flight plan</param>
        /// <param name="aircraftLatitude">Aircraft latitude</param>
        /// <param name="aircraftLongitude">Aircraft longitude</param>
        /// <param name="magneticVariation">Magnetic variation in degrees</param>
        /// <returns>Formatted announcement string or null if slot is empty</returns>
        public string GetTrackedWaypointInfo(int slotNumber, FlightPlan flightPlan, double aircraftLatitude, double aircraftLongitude, double magneticVariation)
        {
            if (slotNumber < 1 || slotNumber > MAX_SLOTS)
                throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

            int index = slotNumber - 1;
            var tracked = _slots[index];

            if (tracked == null)
                return null;

            // Find the waypoint in the current flight plan
            var waypoint = FindWaypointInFlightPlan(tracked, flightPlan);

            if (waypoint == null)
            {
                // Waypoint not found in current flight plan (may have been cleared/replaced)
                return $"Track slot {slotNumber}, {tracked.Ident}, waypoint not in current flight plan";
            }

            // Calculate current distance and bearing
            double distance = NavigationCalculator.CalculateDistance(
                aircraftLatitude, aircraftLongitude,
                waypoint.Latitude, waypoint.Longitude);

            double bearing = NavigationCalculator.CalculateMagneticBearing(
                aircraftLatitude, aircraftLongitude,
                waypoint.Latitude, waypoint.Longitude,
                magneticVariation);

            // Format: "Waypoint name, distance nm, bearing degrees"
            return $"{waypoint.Ident}, {distance:F0} nautical miles, {bearing:F0} degrees";
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
            _slots[index] = null;
        }

        /// <summary>
        /// Clears all tracking slots
        /// </summary>
        public void ClearAllSlots()
        {
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                _slots[i] = null;
            }
        }

        /// <summary>
        /// Gets the waypoint identifier for a slot (for display purposes)
        /// </summary>
        public string GetSlotIdent(int slotNumber)
        {
            if (slotNumber < 1 || slotNumber > MAX_SLOTS)
                throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 5");

            int index = slotNumber - 1;
            return _slots[index]?.Ident;
        }

        /// <summary>
        /// Finds a waypoint in the flight plan by ident and section
        /// </summary>
        private WaypointFix FindWaypointInFlightPlan(TrackedWaypoint tracked, FlightPlan flightPlan)
        {
            if (flightPlan == null || flightPlan.IsEmpty())
                return null;

            var allWaypoints = flightPlan.GetAllWaypoints();

            // First try to find by ident, section, and approximate coordinates
            var waypoint = allWaypoints.FirstOrDefault(w =>
                w.Ident == tracked.Ident &&
                w.Section == tracked.Section &&
                Math.Abs(w.Latitude - tracked.Latitude) < 0.01 &&
                Math.Abs(w.Longitude - tracked.Longitude) < 0.01);

            if (waypoint != null)
                return waypoint;

            // Fallback: find by ident and section only (in case coordinates changed slightly)
            waypoint = allWaypoints.FirstOrDefault(w =>
                w.Ident == tracked.Ident &&
                w.Section == tracked.Section);

            return waypoint;
        }

        /// <summary>
        /// Internal class to store tracked waypoint information
        /// </summary>
        private class TrackedWaypoint
        {
            public string Ident { get; set; }
            public FlightPlanSection Section { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }
    }
}
