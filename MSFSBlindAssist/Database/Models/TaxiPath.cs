namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Represents a taxi path segment from the navdatareader database.
/// Each row defines a segment with start and end coordinates plus metadata.
/// </summary>
public class TaxiPath
{
    public int TaxiPathId { get; set; }
    public int AirportId { get; set; }

    /// <summary>Type: "P" (parking to taxiway), "PT" (parking/taxiway connector), "T" (taxiway)</summary>
    public string Type { get; set; } = "";

    /// <summary>Surface type string from database (e.g., "CONCRETE", "ASPHALT")</summary>
    public string Surface { get; set; } = "";

    /// <summary>Width of the taxi path in feet</summary>
    public double Width { get; set; }

    /// <summary>Taxiway name/designator (e.g., "A", "B", "K2"). Empty for unnamed paths.</summary>
    public string Name { get; set; } = "";

    // Start point
    public string StartType { get; set; } = "";
    public string StartDir { get; set; } = "";
    public double StartLat { get; set; }
    public double StartLon { get; set; }

    // End point
    public string EndType { get; set; } = "";
    public string EndDir { get; set; } = "";
    public double EndLat { get; set; }
    public double EndLon { get; set; }
}
