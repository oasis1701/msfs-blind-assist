namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Represents a taxiway segment (edge) connecting two nodes
/// </summary>
public class TaxiwaySegment
{
    public int Id { get; set; }

    /// <summary>
    /// Start node of this segment
    /// </summary>
    public TaxiwayNode StartNode { get; set; } = null!;

    /// <summary>
    /// End node of this segment
    /// </summary>
    public TaxiwayNode EndNode { get; set; } = null!;

    /// <summary>
    /// Taxiway name (e.g., "A", "B", "C") or null for unnamed connectors
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Width of the taxiway in feet
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Surface type (e.g., "A" for asphalt)
    /// </summary>
    public string? Surface { get; set; }

    /// <summary>
    /// Segment type from database (e.g., "PT" for taxiway path, "P" for parking)
    /// </summary>
    public string? SegmentType { get; set; }

    /// <summary>
    /// Calculated heading from start to end node (degrees true, 0-360)
    /// </summary>
    public double Heading { get; set; }

    /// <summary>
    /// Calculated length of the segment in feet
    /// </summary>
    public double Length { get; set; }

    /// <summary>
    /// Returns true if this segment has a taxiway name
    /// </summary>
    public bool HasName => !string.IsNullOrEmpty(Name);

    /// <summary>
    /// Gets the other node of this segment given one node
    /// </summary>
    public TaxiwayNode? GetOtherNode(TaxiwayNode node)
    {
        if (node == StartNode) return EndNode;
        if (node == EndNode) return StartNode;
        return null;
    }

    /// <summary>
    /// Checks if this segment is connected to the given node
    /// </summary>
    public bool IsConnectedTo(TaxiwayNode node)
    {
        return node == StartNode || node == EndNode;
    }

    /// <summary>
    /// Gets the heading from the specified node toward the other end
    /// </summary>
    public double GetHeadingFrom(TaxiwayNode fromNode)
    {
        if (fromNode == StartNode)
            return Heading;
        if (fromNode == EndNode)
            return (Heading + 180) % 360;
        return Heading; // Default
    }

    /// <summary>
    /// Display name for announcements
    /// </summary>
    public string DisplayName => HasName ? $"taxiway {Name}" : "connector";

    public override string ToString()
    {
        string nameStr = HasName ? $"Taxiway {Name}" : "Connector";
        return $"{nameStr}: ({StartNode.Latitude:F6}, {StartNode.Longitude:F6}) → ({EndNode.Latitude:F6}, {EndNode.Longitude:F6}), {Length:F0}ft";
    }
}
