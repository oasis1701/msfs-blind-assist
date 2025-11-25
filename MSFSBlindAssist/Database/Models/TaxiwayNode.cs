namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Type of taxiway node
/// </summary>
public enum TaxiwayNodeType
{
    Normal,      // Standard intersection/connection point
    Parking,     // Parking area connection
    HoldShort    // Runway hold short point (HSND)
}

/// <summary>
/// Represents a node (junction point) in the taxiway graph
/// </summary>
public class TaxiwayNode
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public TaxiwayNodeType Type { get; set; }

    /// <summary>
    /// If this is a HoldShort node, the runway identifier it holds short of
    /// </summary>
    public string? HoldShortRunway { get; set; }

    /// <summary>
    /// If this is a Parking node, the name of the parking spot (e.g., "Gate A5", "Ramp 3")
    /// </summary>
    public string? ParkingSpotName { get; set; }

    /// <summary>
    /// If this is a Parking node with a gate, whether it has a jetway
    /// </summary>
    public bool HasJetway { get; set; }

    /// <summary>
    /// Segments connected to this node
    /// </summary>
    public List<TaxiwaySegment> ConnectedSegments { get; set; }

    /// <summary>
    /// Unique key for node identification (based on coordinates)
    /// </summary>
    public string NodeKey => $"{Latitude:F6},{Longitude:F6}";

    public TaxiwayNode()
    {
        ConnectedSegments = new List<TaxiwaySegment>();
    }

    public TaxiwayNode(double latitude, double longitude, TaxiwayNodeType type = TaxiwayNodeType.Normal)
    {
        Latitude = latitude;
        Longitude = longitude;
        Type = type;
        ConnectedSegments = new List<TaxiwaySegment>();
    }

    /// <summary>
    /// Returns true if this node is a junction (connected to more than 2 segments)
    /// </summary>
    public bool IsJunction => ConnectedSegments.Count > 2;

    /// <summary>
    /// Returns true if this node is a dead end (connected to only 1 segment)
    /// </summary>
    public bool IsDeadEnd => ConnectedSegments.Count == 1;

    public override string ToString()
    {
        string typeStr = Type switch
        {
            TaxiwayNodeType.HoldShort => $"Hold Short {HoldShortRunway}",
            TaxiwayNodeType.Parking => "Parking",
            _ => "Node"
        };
        return $"{typeStr} at ({Latitude:F6}, {Longitude:F6}) - {ConnectedSegments.Count} connections";
    }
}
