
namespace MSFSBlindAssist.Database.Models;
public class ParkingSpot
{
    public int Id { get; set; }
    public string AirportICAO { get; set; }
    public string Name { get; set; }
    public int Number { get; set; }
    public int Type { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Heading { get; set; }
    public double Radius { get; set; }

    // Additional properties from Little Navmap database (optional, defaults for legacy databases)
    public string Suffix { get; set; }
    public bool HasJetway { get; set; }
    public string AirlineCodes { get; set; }

    public ParkingSpot()
    {
        AirportICAO = string.Empty;
        Name = string.Empty;
        Suffix = string.Empty;
        AirlineCodes = string.Empty;
        HasJetway = false;
    }

    public string GetParkingType()
    {
        switch (Type)
        {
            case 1: return "None";
            case 2: return "Ramp GA";
            case 3: return "Ramp GA Small";
            case 4: return "Ramp GA Medium";
            case 5: return "Ramp GA Large";
            case 6: return "Ramp Cargo";
            case 7: return "Ramp Military Cargo";
            case 8: return "Ramp Military Combat";
            case 9: return "Gate Small";
            case 10: return "Gate Medium";
            case 11: return "Gate Large";
            case 12: return "Dock GA";
            case 13: return "Gate Heavy";
            case 14: return "Gate Extra";
            case 15: return "Ramp GA Extra";
            case 16: return "Fuel";
            case 17: return "Vehicles";
            default: return "Unknown";
        }
    }

    public string GetFilterCategory()
    {
        return Type switch
        {
            9 => "Gate Small",
            10 => "Gate Medium",
            11 => "Gate Large",
            13 => "Gate Heavy",
            14 => "Gate Extra",
            2 or 3 or 4 or 5 or 15 => "Ramp GA",
            6 => "Ramp Cargo",
            7 or 8 => "Ramp Military",
            12 => "Dock",
            _ => "Other"
        };
    }

    public override string ToString()
    {
        string baseDescription;
        string numberPart = Number > 0 ? $"{Number}{Suffix}" : "";

        if (!string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(numberPart))
            baseDescription = $"{Name} {numberPart} - {GetParkingType()}";
        else if (!string.IsNullOrEmpty(Name))
            baseDescription = $"{Name} - {GetParkingType()}";
        else if (!string.IsNullOrEmpty(numberPart))
            baseDescription = $"Spot {numberPart} - {GetParkingType()}";
        else
            baseDescription = $"Parking - {GetParkingType()}";

        if (HasJetway)
            baseDescription += " (Jetway)";

        return baseDescription;
    }
}