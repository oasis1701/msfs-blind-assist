namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Everything read from one snapshot of %LOCALAPPDATA%\SayIntentionsAI\flight.json.
/// Every field is optional — SayIntentions writes a different subset depending on
/// flight phase, and a missing field must degrade to "not available", never throw.
/// </summary>
public sealed class SayIntentionsFlightContext
{
    public string FlightJsonPath { get; init; } = "";
    public bool FlightJsonExists { get; init; }

    /// <summary>Set when the file existed but could not be read or parsed. When
    /// non-null this string is spoken verbatim, so it must stay pilot-readable.</summary>
    public string? Error { get; set; }

    public string? ApiKey { get; set; }
    public string? Hostname { get; set; }
    public string? Callsign { get; set; }
    public string? CurrentAirport { get; set; }
    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public string? AssignedGate { get; set; }
    public string? DepartureRunway { get; set; }
    public string? ArrivalRunway { get; set; }
    public string? Runway { get; set; }
    public string? ClearedForTakeoff { get; set; }
    public string? ClearedForLanding { get; set; }
    public string? FlightPlanRoute { get; set; }
    /// <summary>The taxi clearance to parse. flight.json carries no clearance text of
    /// its own in the observed wire format, so in practice this is filled from the
    /// last radio transmission — which means the taxi import needs the SAPI comms
    /// endpoint to be reachable. See docs/sayintentions.md.</summary>
    public string? ClearanceText { get; set; }

    public SayIntentionsTransmission? LastFlightJsonTransmission { get; set; }

    /// <summary>ICAO type code, e.g. "B738". From <c>aircraft_icao</c>.</summary>
    public string? AircraftIcao { get; set; }

    /// <summary>SayIntentions' own air/ground flag (<c>on_ground</c>), or null when
    /// absent. Read for the report only — guidance keeps using the SimConnect
    /// air/ground state, which is a frame old rather than a file-write old.</summary>
    public bool? OnGround { get; set; }

    /// <summary>Weather and ATIS for the departure airport (<c>departure_wx</c>).</summary>
    public SayIntentionsAirportWeather? DepartureWeather { get; set; }

    /// <summary>Weather and ATIS for the arrival airport (<c>arrival_wx</c>). Not seen
    /// in any capture yet — read defensively so it appears if SI starts publishing it
    /// once a flight plan is filed.</summary>
    public SayIntentionsAirportWeather? ArrivalWeather { get; set; }
}

/// <summary>
/// One airport's weather/ATIS block as SayIntentions publishes it in flight.json.
///
/// This is the richest thing in the file and nothing read it before: the ATIS letter
/// and the ACTIVE RUNWAY CONFIGURATION are not available anywhere else in this app —
/// not from VATSIM, not from ActiveSky, not from navdata — and they are exactly what
/// a blind pilot otherwise has to sit through an ATIS loop to learn. Every field is
/// optional; SI leaves plenty of them empty.
/// </summary>
public sealed class SayIntentionsAirportWeather
{
    public string? Airport { get; init; }

    /// <summary>The ATIS letter, e.g. "U" for information Uniform.</summary>
    public string? InformationLetter { get; init; }

    /// <summary>Decoded ATIS as prose. SI also publishes a phonetic variant, which we
    /// do NOT use: the screen reader is the one deciding how to pronounce things, and
    /// feeding it "two-two-left" produces a worse reading than "22L".</summary>
    public string? Atis { get; init; }

    public string? ActiveRunwaysArriving { get; init; }
    public string? ActiveRunwaysDeparting { get; init; }
    public string? PreferredRunway { get; init; }
    public string? CurrentlyOperating { get; init; }

    public double? WindDirection { get; init; }
    public double? WindSpeed { get; init; }
    public double? WindGusting { get; init; }
    public double? Visibility { get; init; }
    public double? Altimeter { get; init; }
    public double? DensityAltitude { get; init; }

    public string? Metar { get; init; }
    public string? Taf { get; init; }
}

/// <summary>One radio transmission. <paramref name="Speaker"/> is "ATC", "Pilot",
/// or empty when the source message carried no direction.</summary>
public sealed record SayIntentionsTransmission(
    string Speaker,
    string Message,
    string? StationName,
    string? Channel,
    DateTime? StampZulu,
    int? Id)
{
    public string ToAnnouncement()
    {
        string prefix = "";
        if (!string.IsNullOrWhiteSpace(Speaker))
            prefix = Speaker;
        if (!string.IsNullOrWhiteSpace(StationName))
            prefix = string.IsNullOrWhiteSpace(prefix) ? StationName! : $"{prefix}, {StationName}";

        return string.IsNullOrWhiteSpace(prefix) ? Message : $"{prefix}: {Message}";
    }
}

/// <summary>A parking assignment from the SAPI getParking endpoint.</summary>
public sealed class SayIntentionsParking
{
    public string? Name { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Heading { get; init; }
}

public sealed record SayIntentionsTransmissionResult(
    SayIntentionsTransmission? Transmission,
    string? Error);

public sealed record SayIntentionsParkingResult(
    SayIntentionsParking? Parking,
    string? Error);

public sealed record SayIntentionsStatusResult(
    SayIntentionsFlightContext Context,
    SayIntentionsParking? Parking,
    string? ParkingError);
