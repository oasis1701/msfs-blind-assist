namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// One parking position parsed from a GSX profile .ini section
/// (e.g. [gate c 18]). Positions are WGS84 lat/lon; headings are degrees true.
/// </summary>
public sealed class GsxGate
{
    public string Category { get; set; } = string.Empty;    // section category: "gate"/"parking"/"none"/"dock"
    public string Concourse { get; set; } = string.Empty;   // e.g. "C", "P", "" (pure-numeric)
    public int Number { get; set; }                         // e.g. 18 (0 if none)
    public string Suffix { get; set; } = string.Empty;      // e.g. "L"/"R"/"A"
    public bool HasParkingPos { get; set; }                 // true if this_parking_pos was present
    public double Latitude { get; set; }                    // this_parking_pos lat (0 if absent)
    public double Longitude { get; set; }                   // this_parking_pos lon
    public double Heading { get; set; }                     // this_parking_pos heading (deg true)
    public double? StopLatitude { get; set; }               // parkingsystem_stopposition lat
    public double? StopLongitude { get; set; }              // parkingsystem_stopposition lon
    public double? StopHeading { get; set; }                // parkingsystem_stopposition heading
    public bool HasJetway { get; set; }
    public int GsxType { get; set; }                        // raw .ini "type" (fallback only)
    public double? MaxWingspanMeters { get; set; }          // "maxwingspan" — primary size source
    public string VdgsType { get; set; } = string.Empty;    // "parkingsystem" (e.g. SafeDockT42, Marshaller)
    public string AirlineCodes { get; set; } = string.Empty;
    public string RawSectionName { get; set; } = string.Empty; // e.g. "gate c 18"
}
