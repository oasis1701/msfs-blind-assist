using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>maps GsxGate -> ParkingSpot; size from maxwingspan + section category</summary>
public static class GsxGateMapper
{
    // GSX .ini "type" uses the MSFS SDK parking-type enum; ParkingSpot.Type uses
    // LittleNavMap's enum (gates 9..14). Map between them. VERIFY against maxwingspan
    // on real stands (Task 7) before trusting; if GSX "type" is unreliable, derive
    // size from MaxWingspanMeters instead.
    public static int MapGsxTypeToNavdataType(int gsxType) => gsxType switch
    {
        1 => 2,   // Ramp GA
        2 => 3,   // Ramp GA Small
        3 => 4,   // Ramp GA Medium
        4 => 5,   // Ramp GA Large
        5 => 6,   // Ramp Cargo
        6 => 7,   // Ramp Mil Cargo
        7 => 8,   // Ramp Mil Combat
        8 => 9,   // Gate Small
        9 => 10,  // Gate Medium
        10 => 13, // Gate Heavy
        11 => 12, // Dock GA
        _ => 0
    };

    public static ParkingSpot ToParkingSpot(GsxGate g, string icao) => new ParkingSpot
    {
        AirportICAO = icao,
        // For deice areas the full display name (uiname) lives in Concourse; Number is always 0.
        // For normal gates, Concourse is the letter prefix and Number is the stand number.
        Name = g.Concourse,
        Number = g.IsDeiceArea ? 0 : g.Number,
        Suffix = g.IsDeiceArea ? string.Empty : g.Suffix,
        Type = DeriveType(g.Category, g.MaxWingspanMeters),
        Latitude = g.Latitude,
        Longitude = g.Longitude,
        Heading = g.Heading,
        // Radius is centre-to-edge; the "fitting stands" filter tests Radius >= wingspan/2.
        // Use GSX's accurate maxwingspan; if absent, a permissive value so the gate isn't
        // spuriously filtered out.
        Radius = g.MaxWingspanMeters.HasValue ? g.MaxWingspanMeters.Value / 2.0 : 100.0,
        HasJetway = g.HasJetway,
        AirlineCodes = g.AirlineCodes,
        Source = GateSource.Gsx,
        VdgsType = string.IsNullOrWhiteSpace(g.VdgsType) ? null : g.VdgsType,
        MaxWingspanMeters = g.MaxWingspanMeters,
        StopLatitude = g.StopLatitude,
        StopLongitude = g.StopLongitude,
        StopHeading = g.StopHeading,
        IsDeiceArea = g.IsDeiceArea
    };

    // Size/heavy from maxwingspan (ICAO wingspan code) + section category. This is the
    // accurate, universal source (the .ini "type" enum is ambiguous across profiles).
    // Maps to the LittleNavMap ParkingSpot.Type enum so ToString()/GetFilterCategory() work.
    public static int DeriveType(string category, double? wingspan)
    {
        char code = WingspanCode(wingspan); // A B C D E F, or '?' when unknown
        switch ((category ?? string.Empty).ToLowerInvariant())
        {
            case "dock": return 12;
            case "cargo": return 6; // Ramp Cargo
            case "gate":
                return code switch { 'E' or 'F' => 13, 'D' => 11, 'C' => 10, 'A' or 'B' => 9, _ => 10 };
            default: // parking / none / ramp / etc. -> Ramp GA sizes (all read "Ramp GA")
                return code switch { 'E' or 'F' => 15, 'D' => 5, 'C' => 4, 'B' => 3, _ => 2 };
        }
    }

    // ICAO wingspan code from metres: A<15, B<24, C<36, D<52, E<65, F>=65. '?' if unknown.
    private static char WingspanCode(double? w)
    {
        if (!w.HasValue) return '?';
        double m = w.Value;
        if (m >= 65) return 'F';
        if (m >= 52) return 'E';
        if (m >= 36) return 'D';
        if (m >= 24) return 'C';
        if (m >= 15) return 'B';
        return 'A';
    }

    // Maps metadata only (no placement). Position resolution + dropping unplaceable gates
    // is GsxNavdataMerger's job. Kept for the probe / standalone inspection.
    public static List<ParkingSpot> ToParkingSpots(IEnumerable<GsxGate> gates, string icao)
    {
        var list = new List<ParkingSpot>();
        foreach (var g in gates) list.Add(ToParkingSpot(g, icao));
        return list;
    }
}
