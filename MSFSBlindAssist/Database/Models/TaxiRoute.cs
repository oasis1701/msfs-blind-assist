namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Represents a calculated taxi route with segments and hold-short points.
/// </summary>
public class TaxiRoute
{
    public List<TaxiRouteSegment> Segments { get; set; } = new();
    public double TotalDistanceMeters { get; set; }
    public List<string> TaxiwaySequence { get; set; } = new();
    public List<TaxiHoldShort> HoldShortPoints { get; set; } = new();
    public string DestinationName { get; set; } = "";
    /// <summary>If the constrained route fell back to shortest path, explains why.</summary>
    public string? ConstrainedFallbackReason { get; set; }
}

/// <summary>
/// A single segment in the taxi route (edge between two nodes).
/// </summary>
public class TaxiRouteSegment
{
    public TaxiNode FromNode { get; set; } = null!;
    public TaxiNode ToNode { get; set; } = null!;
    public double DistanceMeters { get; set; }
    public double CumulativeDistanceMeters { get; set; }
    public double RemainingDistanceMeters { get; set; }
    public string TaxiwayName { get; set; } = "";
    public double BearingDegrees { get; set; }
    public double TurnAngleDegrees { get; set; }
    public string TurnDirection { get; set; } = "straight";
    public double PathWidth { get; set; }
    public bool IsHoldShortPoint { get; set; }
    public string? HoldShortRunway { get; set; }
}

/// <summary>
/// Represents a hold-short instruction at a specific segment.
/// </summary>
public class TaxiHoldShort
{
    public int SegmentIndex { get; set; }
    public string RunwayOrTaxiway { get; set; } = "";
    public bool IsRunway { get; set; }
}
