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
