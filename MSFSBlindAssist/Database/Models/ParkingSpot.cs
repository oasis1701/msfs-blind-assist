
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

    // GSX-source enrichment (null/default for navdata-sourced spots).
    public GateSource Source { get; set; } = GateSource.Navdata;
    public string? VdgsType { get; set; }              // e.g. "SafeDockT42", "Marshaller"
    public double? MaxWingspanMeters { get; set; }     // GSX "maxwingspan"
    public double? StopLatitude { get; set; }          // GSX parkingsystem_stopposition lat
    public double? StopLongitude { get; set; }         // GSX parkingsystem_stopposition lon
    public double? StopHeading { get; set; }           // GSX stop-position nose heading (deg true); null for navdata-only
    public bool IsDeiceArea { get; set; }              // true when parsed from a GSX is_deicearea = 1 section

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

    /// <summary>
    /// Returns whether this spot fits an aircraft with the given wing span
    /// (in FEET — matches <c>SimConnectManager.AircraftWingSpan</c>).
    /// <para>
    /// UNIT-AWARE by SOURCE:
    ///   • GSX spots carry the authoritative max allowed wing span in METERS
    ///     (<see cref="MaxWingspanMeters"/>) — compare directly (aircraft → metres).
    ///     The GSX-sourced <see cref="Radius"/> is metres (maxwingspan/2), so the old
    ///     "Radius >= wingspanFeet/2" test mixed metres with a feet threshold and
    ///     filtered almost everything out. A GSX spot whose profile omits maxwingspan
    ///     has no reliable size → treat it as fitting (don't hide it).
    ///   • Navdata spots have a physical parking <see cref="Radius"/> in FEET — keep the
    ///     original "radius holds the half-span" test (both feet).
    /// </para>
    /// An unknown wing span (&lt;= 0) fits everything (filter is a no-op).
    /// </summary>
    public bool FitsAircraft(double aircraftWingspanFeet)
    {
        if (aircraftWingspanFeet <= 0) return true;

        if (Source == GateSource.Gsx)
        {
            // No GSX size info → don't filter it out (placeholder Radius is not real).
            if (!MaxWingspanMeters.HasValue) return true;

            const double feetToMeters = 0.3048;
            double aircraftWingspanMeters = aircraftWingspanFeet * feetToMeters;
            return MaxWingspanMeters.Value >= aircraftWingspanMeters;
        }

        // Navdata: physical parking radius (feet) must hold the half-span (feet).
        return Radius >= aircraftWingspanFeet / 2.0;
    }

    private static string FriendlyVdgs(string? vdgs)
    {
        if (string.IsNullOrWhiteSpace(vdgs)) return string.Empty;
        if (vdgs.StartsWith("Safedock", StringComparison.OrdinalIgnoreCase))  return "SafeDock";   // incl. SafeDock*
        if (vdgs.StartsWith("Marshaller", StringComparison.OrdinalIgnoreCase)) return "Marshaller";
        if (vdgs.StartsWith("Apis", StringComparison.OrdinalIgnoreCase))      return "APIS";
        if (vdgs.StartsWith("Agnis", StringComparison.OrdinalIgnoreCase))     return "AGNIS";
        if (vdgs.StartsWith("Honeywell", StringComparison.OrdinalIgnoreCase)) return "Honeywell";
        if (vdgs.StartsWith("Rlg", StringComparison.OrdinalIgnoreCase))       return "RLG";
        if (vdgs.StartsWith("Vgds", StringComparison.OrdinalIgnoreCase))      return "VDGS";
        return string.Empty; // "Dummy", "1", or anything not a recognized VDGS -> no suffix
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

        string vdgs = FriendlyVdgs(VdgsType);
        if (!string.IsNullOrEmpty(vdgs))
            baseDescription += $" [{vdgs}]";

        return baseDescription;
    }
}