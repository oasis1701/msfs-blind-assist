namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Raw database record from taxi_path table
/// Used during graph construction before converting to TaxiwaySegment
/// </summary>
public class TaxiPathRecord
{
    public int Id { get; set; }
    public int AirportId { get; set; }

    /// <summary>
    /// Path type (e.g., "PT" for taxiway path, "P" for parking)
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Surface type (e.g., "A" for asphalt)
    /// </summary>
    public string? Surface { get; set; }

    /// <summary>
    /// Width in feet
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Taxiway name (e.g., "A", "B") or null for unnamed
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Start node type: "N" (normal), "P" (parking), "HSND" (hold short)
    /// </summary>
    public string? StartType { get; set; }

    /// <summary>
    /// Start node direction indicator
    /// </summary>
    public string? StartDir { get; set; }

    public double StartLonx { get; set; }
    public double StartLaty { get; set; }

    /// <summary>
    /// End node type: "N" (normal), "P" (parking), "HSND" (hold short)
    /// </summary>
    public string? EndType { get; set; }

    /// <summary>
    /// End node direction indicator
    /// </summary>
    public string? EndDir { get; set; }

    public double EndLonx { get; set; }
    public double EndLaty { get; set; }

    public override string ToString()
    {
        string nameStr = !string.IsNullOrEmpty(Name) ? $"[{Name}]" : "[unnamed]";
        return $"{nameStr} ({StartLaty:F6}, {StartLonx:F6}) → ({EndLaty:F6}, {EndLonx:F6})";
    }
}
