namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Represents a node in the taxi graph (intersection, hold-short point, or parking connector).
/// Nodes are created during graph construction by merging nearby endpoints.
/// </summary>
public class TaxiNode
{
    public int NodeId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public TaxiNodeType Type { get; set; } = TaxiNodeType.Normal;

    /// <summary>Names of all taxiways that pass through this node</summary>
    public HashSet<string> TaxiwayNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>If this is a parking connector node, the associated parking spot name</summary>
    public string? ParkingName { get; set; }

    /// <summary>If this is a hold-short node, the name of what to hold short of (runway or taxiway)</summary>
    public string? HoldShortName { get; set; }
}

public enum TaxiNodeType
{
    Normal,
    HoldShort,
    ILSHoldShort,
    Parking
}
