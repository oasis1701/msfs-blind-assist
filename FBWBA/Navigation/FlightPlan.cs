using FBWBA.Database.Models;

namespace FBWBA.Navigation;
/// <summary>
/// Represents a complete flight plan with all waypoints organized by section
/// </summary>
public class FlightPlan
{
    // Flight plan metadata
    public string DepartureICAO { get; set; } = "";
    public string DepartureRunway { get; set; } = "";
    public string ArrivalICAO { get; set; } = "";
    public string ArrivalRunway { get; set; } = "";
    public string SIDName { get; set; } = "";
    public string STARName { get; set; } = "";
    public string ApproachName { get; set; } = "";

    // Waypoint lists by section
    public List<WaypointFix> DepartureAirportWaypoints { get; set; } = new List<WaypointFix>(); // Section A
    public List<WaypointFix> SIDWaypoints { get; set; } = new List<WaypointFix>(); // Section B
    public List<WaypointFix> EnrouteWaypoints { get; set; } = new List<WaypointFix>(); // Section C
    public List<WaypointFix> STARWaypoints { get; set; } = new List<WaypointFix>(); // Section D
    public List<WaypointFix> ApproachWaypoints { get; set; } = new List<WaypointFix>(); // Section E
    public List<WaypointFix> ArrivalAirportWaypoints { get; set; } = new List<WaypointFix>(); // Section F

    // SimBrief metadata
    public string SimBriefUsername { get; set; } = "";
    public DateTime? LoadedTime { get; set; }

    /// <summary>
    /// Gets all waypoints in flight plan order (A through F)
    /// </summary>
    public List<WaypointFix> GetAllWaypoints()
    {
        var allWaypoints = new List<WaypointFix>();

        allWaypoints.AddRange(DepartureAirportWaypoints);
        allWaypoints.AddRange(SIDWaypoints);
        allWaypoints.AddRange(EnrouteWaypoints);
        allWaypoints.AddRange(STARWaypoints);
        allWaypoints.AddRange(ApproachWaypoints);
        allWaypoints.AddRange(ArrivalAirportWaypoints);

        return allWaypoints;
    }

    /// <summary>
    /// Gets waypoints for a specific section
    /// </summary>
    public List<WaypointFix> GetSectionWaypoints(FlightPlanSection section)
    {
        switch (section)
        {
            case FlightPlanSection.DepartureAirport:
                return DepartureAirportWaypoints;
            case FlightPlanSection.SID:
                return SIDWaypoints;
            case FlightPlanSection.Enroute:
                return EnrouteWaypoints;
            case FlightPlanSection.STAR:
                return STARWaypoints;
            case FlightPlanSection.Approach:
                return ApproachWaypoints;
            case FlightPlanSection.ArrivalAirport:
                return ArrivalAirportWaypoints;
            default:
                return new List<WaypointFix>();
        }
    }

    /// <summary>
    /// Updates a specific section with new waypoints
    /// </summary>
    public void UpdateSection(FlightPlanSection section, List<WaypointFix> waypoints)
    {
        // Set the section for all waypoints
        foreach (var waypoint in waypoints)
        {
            waypoint.Section = section;
        }

        switch (section)
        {
            case FlightPlanSection.DepartureAirport:
                DepartureAirportWaypoints = waypoints;
                break;
            case FlightPlanSection.SID:
                SIDWaypoints = waypoints;
                break;
            case FlightPlanSection.Enroute:
                EnrouteWaypoints = waypoints;
                break;
            case FlightPlanSection.STAR:
                STARWaypoints = waypoints;
                break;
            case FlightPlanSection.Approach:
                ApproachWaypoints = waypoints;
                break;
            case FlightPlanSection.ArrivalAirport:
                ArrivalAirportWaypoints = waypoints;
                break;
        }

        // Recalculate leg distances for entire flight plan after section update
        CalculateLegDistances();
    }

    /// <summary>
    /// Clears a specific section
    /// </summary>
    public void ClearSection(FlightPlanSection section)
    {
        UpdateSection(section, new List<WaypointFix>());
    }

    /// <summary>
    /// Returns total waypoint count
    /// </summary>
    public int GetTotalWaypointCount()
    {
        return DepartureAirportWaypoints.Count +
               SIDWaypoints.Count +
               EnrouteWaypoints.Count +
               STARWaypoints.Count +
               ApproachWaypoints.Count +
               ArrivalAirportWaypoints.Count;
    }

    /// <summary>
    /// Checks if flight plan has any waypoints
    /// </summary>
    public bool IsEmpty()
    {
        return GetTotalWaypointCount() == 0;
    }

    /// <summary>
    /// Gets a summary string of the flight plan
    /// </summary>
    public string GetSummary()
    {
        if (IsEmpty())
            return "No flight plan loaded";

        string summary = $"{DepartureICAO ?? "???"} to {ArrivalICAO ?? "???"}";

        if (!string.IsNullOrEmpty(DepartureRunway))
            summary += $" (Runway {DepartureRunway})";

        if (!string.IsNullOrEmpty(SIDName))
            summary += $" via {SIDName}";

        if (!string.IsNullOrEmpty(STARName))
            summary += $", {STARName}";

        if (!string.IsNullOrEmpty(ApproachName))
            summary += $", {ApproachName}";

        if (!string.IsNullOrEmpty(ArrivalRunway))
            summary += $" to Runway {ArrivalRunway}";

        summary += $" ({GetTotalWaypointCount()} waypoints)";

        return summary;
    }

    /// <summary>
    /// Calculates leg distances for all waypoints in the flight plan.
    /// Leg distance = distance from each waypoint to its previous waypoint in the flight plan sequence.
    /// This is called automatically when sections are updated, so leg distances stay current.
    /// </summary>
    public void CalculateLegDistances()
    {
        var allWaypoints = GetAllWaypoints();

        // First waypoint has no previous waypoint, so leg distance is null
        if (allWaypoints.Count > 0)
        {
            allWaypoints[0].Distance = null;
        }

        // Calculate leg distance for each subsequent waypoint
        for (int i = 1; i < allWaypoints.Count; i++)
        {
            var previousWaypoint = allWaypoints[i - 1];
            var currentWaypoint = allWaypoints[i];

            // Calculate distance from previous waypoint to current waypoint
            double legDistance = NavigationCalculator.CalculateDistance(
                previousWaypoint.Latitude,
                previousWaypoint.Longitude,
                currentWaypoint.Latitude,
                currentWaypoint.Longitude);

            currentWaypoint.Distance = legDistance;
        }
    }
}
