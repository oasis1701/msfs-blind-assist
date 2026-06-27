
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
    /// <summary>
    /// GSX "gatedistancethreshold" (metres) — the distance at which GSX activates the VDGS
    /// for this stand. Present only for .ini-sourced gates; null for navdata-only stands.
    /// Docking guidance uses this as the engage range (clamped to [20, 70] m) instead of
    /// the fixed 50 m default when non-null.
    /// </summary>
    public double? GateDistanceThreshold { get; set; }

    /// <summary>
    /// Alternative names for this parking spot discovered from online sources (OSM / X-Plane
    /// apt.dat) when those sources use a different label than the navdata <see cref="Name"/>.
    /// For example, navdata might use "GN 3" while ATC/OSM uses "47" — both refer to the
    /// same physical stand.
    /// <para>
    /// In-memory only — never persisted to the database. Empty list when no alias is known.
    /// Navdata <see cref="Name"/> is always authoritative; aliases only ADD extra selectable
    /// entries to the UI.
    /// </para>
    /// </summary>
    public List<string> Aliases { get; set; } = new();

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

    /// <summary>True for gate-type stands (Gate Small/Medium/Large/Heavy/Extra) — used to render
    /// an empty-name gate as "Gate {n}" rather than the generic "Spot {n}".</summary>
    private bool IsGateType() => Type is 9 or 10 or 11 or 13 or 14;

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

    /// <summary>
    /// Base human description WITHOUT online aliases. Dropdowns that list aliases as their OWN
    /// separate entries (e.g. TaxiAssistForm) use this as the clean base label, then add a
    /// "{alias} ({Describe()})" entry per alias — so the base never carries a redundant or nested
    /// "(also …)" suffix.
    /// </summary>
    public string Describe()
    {
        string baseDescription;
        string numberPart = Number > 0
            ? $"{Number}{Suffix}"
            : (!string.IsNullOrEmpty(Suffix) ? $"0{Suffix}" : "");

        if (!string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(numberPart))
            baseDescription = $"{Name} {numberPart} - {GetParkingType()}";
        else if (!string.IsNullOrEmpty(Name))
            baseDescription = $"{Name} - {GetParkingType()}";
        else if (!string.IsNullOrEmpty(numberPart))
            baseDescription = IsGateType()
                ? $"Gate {numberPart} - {GetParkingType()}"
                : $"Spot {numberPart} - {GetParkingType()}";
        else
            baseDescription = $"Parking - {GetParkingType()}";

        if (HasJetway)
            baseDescription += " (Jetway)";

        string vdgs = FriendlyVdgs(VdgsType);
        if (!string.IsNullOrEmpty(vdgs))
            baseDescription += $" [{vdgs}]";

        return baseDescription;
    }

    public override string ToString()
    {
        string d = Describe();
        // Full description APPENDS online aliases — used by listboxes that show ONE entry per
        // spot (e.g. the gate-teleport listbox, whose SelectedParkingSpot resolves by object
        // identity, unaffected by the display string). Dropdowns that list aliases as their own
        // entries call Describe() instead to avoid a redundant/nested suffix.
        if (Aliases.Count > 0)
            d += ", also " + string.Join(", ", Aliases) + " (online)";
        return d;
    }
}