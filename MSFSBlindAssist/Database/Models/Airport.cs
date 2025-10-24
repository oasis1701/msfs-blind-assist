
namespace MSFSBlindAssist.Database.Models;

public class Airport
{
    public string ICAO { get; set; }
    public string Name { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double MagVar { get; set; }

    public Airport()
    {
        ICAO = string.Empty;
        Name = string.Empty;
        City = string.Empty;
        Country = string.Empty;
    }

    public Airport(string icao, string name, string city, string country,
                  double latitude, double longitude, double altitude, double magVar)
    {
        ICAO = icao;
        Name = name ?? string.Empty;
        City = city ?? string.Empty;
        Country = country ?? string.Empty;
        Latitude = latitude;
        Longitude = longitude;
        Altitude = altitude;
        MagVar = magVar;
    }

    public override string ToString()
    {
        return $"{ICAO} - {Name}";
    }
}