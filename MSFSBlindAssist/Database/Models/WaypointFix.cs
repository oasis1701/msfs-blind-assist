
namespace MSFSBlindAssist.Database.Models;
/// <summary>
/// Represents a navigation waypoint fix with all relevant flight planning data
/// </summary>
public class WaypointFix
{
    // Identification
    public string Ident { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Type { get; set; } = ""; // RNAV, VOR, NDB, Airport, Runway, etc.
    public string ArincType { get; set; } = "";

    // Position
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Altitude { get; set; } // Waypoint altitude in feet

    // Flight plan data
    public string InboundAirway { get; set; } = ""; // Airway name or "DCT" for direct
    public string AltitudeRestriction { get; set; } = ""; // e.g., "AT 10000", "AT OR ABOVE 5000", "AT OR BELOW 15000"
    public int? MinAltitude { get; set; }
    public int? MaxAltitude { get; set; }
    public int? SpeedLimit { get; set; } // Speed restriction in knots
    public string SpeedLimitType { get; set; } = ""; // Speed limit type descriptor
    public double? Course { get; set; } // Inbound course in degrees
    public double? Distance { get; set; } // Distance along route in NM

    // Dynamic calculations (updated from aircraft position)
    public double? DistanceFromAircraft { get; set; } // Distance in NM
    public double? BearingFromAircraft { get; set; } // Magnetic bearing in degrees

    // Additional metadata
    public string Notes { get; set; } = "";
    public bool IsFlyover { get; set; }
    public string TurnDirection { get; set; } = ""; // L or R
    public double? RNP { get; set; } // Required Navigation Performance
    public double? VerticalAngle { get; set; } // Vertical angle / Decision height in feet
    public double? Time { get; set; } // Time constraint in minutes
    public double? Theta { get; set; } // DME arc angle in degrees
    public double? Rho { get; set; } // DME arc distance in NM
    public bool IsTrueCourse { get; set; } // Whether course is true or magnetic
    public string ArincDescCode { get; set; } = ""; // ARINC leg type descriptor code
    public string ApproachFixType { get; set; } = ""; // Approach fix type
    public bool IsMissedApproach { get; set; } // Whether this leg is part of the missed approach
    public string FixType { get; set; } = ""; // Fix type (W=Waypoint, V=VOR, N=NDB, R=Runway, A=Airport)
    public string FixAirportIdent { get; set; } = ""; // Associated airport identifier for the fix
    public string RecommendedFixIdent { get; set; } = ""; // Alternate/recommended fix identifier for conditional legs

    // Section identifier for flight plan organization
    public FlightPlanSection Section { get; set; }

    public override string ToString()
    {
        if (DistanceFromAircraft.HasValue && BearingFromAircraft.HasValue)
        {
            return $"{Ident} - {DistanceFromAircraft.Value:F1} NM, {BearingFromAircraft.Value:F0}°";
        }
        return Ident ?? "Unknown";
    }

    /// <summary>
    /// Returns a formatted string with all waypoint details for screen reader announcement
    /// </summary>
    public string GetDetailedDescription()
    {
        var details = $"{Ident ?? "Unknown"}";

        if (DistanceFromAircraft.HasValue)
            details += $", Distance {DistanceFromAircraft.Value:F1} nautical miles";

        if (BearingFromAircraft.HasValue)
            details += $", Bearing {BearingFromAircraft.Value:F0} degrees";

        if (!string.IsNullOrEmpty(InboundAirway))
            details += $", via {InboundAirway}";

        if (!string.IsNullOrEmpty(Type))
            details += $", Type {Type}";

        if (!string.IsNullOrEmpty(AltitudeRestriction))
            details += $", {AltitudeRestriction}";
        else if (MinAltitude.HasValue || MaxAltitude.HasValue)
        {
            if (MinAltitude.HasValue && MaxAltitude.HasValue)
                details += $", Altitude between {MinAltitude.Value} and {MaxAltitude.Value} feet";
            else if (MinAltitude.HasValue)
                details += $", Minimum altitude {MinAltitude.Value} feet";
            else if (MaxAltitude.HasValue)
                details += $", Maximum altitude {MaxAltitude.Value} feet";
        }

        if (SpeedLimit.HasValue)
            details += $", Speed limit {SpeedLimit.Value} knots";

        if (!string.IsNullOrEmpty(Notes))
            details += $", Notes: {Notes}";

        return details;
    }
}

/// <summary>
/// Defines the sections of a flight plan
/// </summary>
public enum FlightPlanSection
{
    DepartureAirport = 0,  // Section A
    SID = 1,               // Section B
    Enroute = 2,           // Section C
    STAR = 3,              // Section D
    Approach = 4,          // Section E
    ArrivalAirport = 5     // Section F
}
