namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Represents a runway start position from the navdatareader database.
/// Used to find the nearest graph node for runway destinations.
/// </summary>
public class StartPosition
{
    public int StartId { get; set; }
    public int AirportId { get; set; }
    public int? RunwayEndId { get; set; }
    public string RunwayName { get; set; } = "";
    public string Type { get; set; } = "";
    public double Heading { get; set; }
    public double Altitude { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
