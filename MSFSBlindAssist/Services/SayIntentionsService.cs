using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public sealed class SayIntentionsService
{
    private const int ApiTimeoutSeconds = 5;
    private static readonly TimeSpan CommsCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ParkingCacheDuration = TimeSpan.FromSeconds(10);

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds)
    };

    private readonly string _flightJsonPath;
    private DateTime _lastCommsFetchUtc = DateTime.MinValue;
    private SayIntentionsTransmission? _cachedLastTransmission;
    private string? _cachedCommsError;
    private DateTime _lastParkingFetchUtc = DateTime.MinValue;
    private SayIntentionsParking? _cachedParking;
    private string? _cachedParkingError;

    public SayIntentionsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SayIntentionsAI",
            "flight.json"))
    {
    }

    internal SayIntentionsService(string flightJsonPath)
    {
        _flightJsonPath = flightJsonPath;
    }

    public async Task<SayIntentionsTransmissionResult> GetLastTransmissionAsync()
    {
        var context = ReadFlightContext();
        var flightJsonTransmission = context.LastFlightJsonTransmission;

        string? apiKey = ResolveApiKey(context);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var apiResult = await GetLastCommsHistoryTransmissionAsync(context, apiKey);
            if (apiResult.Transmission != null)
                return new SayIntentionsTransmissionResult(apiResult.Transmission, null);

            if (flightJsonTransmission != null)
                return new SayIntentionsTransmissionResult(flightJsonTransmission, apiResult.Error);

            return new SayIntentionsTransmissionResult(null, apiResult.Error);
        }

        if (flightJsonTransmission != null)
            return new SayIntentionsTransmissionResult(flightJsonTransmission, null);

        return new SayIntentionsTransmissionResult(
            null,
            context.FlightJsonExists
                ? "No SayIntentions communication found in flight.json. Add a SayIntentions API key in settings for comms history."
                : "SayIntentions flight.json not found. Start an active SayIntentions flight or add an API key in settings.");
    }

    public async Task<SayIntentionsStatusResult> GetAssignedStatusAsync()
    {
        var context = ReadFlightContext();
        string? apiKey = ResolveApiKey(context);
        SayIntentionsParking? parking = null;
        string? parkingError = null;

        if (string.IsNullOrWhiteSpace(context.AssignedGate) && !string.IsNullOrWhiteSpace(apiKey))
        {
            var parkingResult = await GetParkingAsync(context, apiKey);
            parking = parkingResult.Parking;
            parkingError = parkingResult.Error;
        }

        return new SayIntentionsStatusResult(context, parking, parkingError);
    }

    public SayIntentionsFlightContext ReadFlightContext()
    {
        var context = new SayIntentionsFlightContext
        {
            FlightJsonPath = _flightJsonPath,
            FlightJsonExists = File.Exists(_flightJsonPath)
        };

        if (!context.FlightJsonExists)
            return context;

        try
        {
            using var stream = new FileStream(
                _flightJsonPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            JsonElement root = doc.RootElement;
            JsonElement details = GetObject(root, "flight_details") ?? root;
            JsonElement? currentFlight = GetObject(details, "current_flight");

            context.ApiKey = GetString(details, "api_key");
            context.Hostname = GetString(details, "hostname");
            context.Callsign = FirstNonEmpty(GetString(details, "callsign_icao"), GetString(details, "callsign"));
            context.CurrentAirport = CleanIcao(GetString(details, "current_airport"));

            if (currentFlight is JsonElement flight)
            {
                context.Origin = CleanIcao(GetString(flight, "flight_origin"));
                context.Destination = CleanIcao(GetString(flight, "flight_destination"));
                context.AssignedGate = FirstNonEmpty(
                    GetString(flight, "assigned_gate"),
                    GetString(flight, "parking"),
                    GetString(flight, "gate"));
                context.DepartureRunway = CleanRunway(FirstNonEmpty(
                    GetString(flight, "flight_plan_departing_runway"),
                    GetString(flight, "departing_runway"),
                    GetString(flight, "departure_runway")));
                context.ArrivalRunway = CleanRunway(FirstNonEmpty(
                    GetString(flight, "flight_plan_arriving_runway"),
                    GetString(flight, "arriving_runway"),
                    GetString(flight, "arrival_runway")));
                context.FlightPlanRoute = GetString(flight, "flight_plan_route");
                context.TaxiwaySequence = ReadTaxiPath(flight);
            }

            context.ClearedForTakeoff = CleanRunway(GetString(details, "cleared_for_takeoff"));
            context.ClearedForLanding = CleanRunway(GetString(details, "cleared_for_landing"));
            context.Runway = CleanRunway(GetString(details, "runway"));
            context.ClearanceText = FirstNonEmpty(
                GetString(details, "clearance"),
                GetString(details, "last_clearance"),
                GetString(details, "taxi_clearance"),
                FindString(root, "clearance_text"),
                FindString(root, "taxi_clearance"));
            context.LastFlightJsonTransmission = FindLatestTransmission(root);

            if (string.IsNullOrWhiteSpace(context.ClearanceText) && context.LastFlightJsonTransmission != null)
                context.ClearanceText = context.LastFlightJsonTransmission.Message;
        }
        catch (JsonException ex)
        {
            context.Error = $"SayIntentions flight.json is malformed. {ex.Message}";
        }
        catch (IOException ex)
        {
            context.Error = $"Could not read SayIntentions flight.json. {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Error = $"Could not access SayIntentions flight.json. {ex.Message}";
        }

        return context;
    }

    private async Task<SayIntentionsTransmissionResult> GetLastCommsHistoryTransmissionAsync(
        SayIntentionsFlightContext context,
        string apiKey)
    {
        if (DateTime.UtcNow - _lastCommsFetchUtc < CommsCacheDuration)
            return new SayIntentionsTransmissionResult(_cachedLastTransmission, _cachedCommsError);

        _lastCommsFetchUtc = DateTime.UtcNow;
        _cachedLastTransmission = null;
        _cachedCommsError = null;

        try
        {
            string endpoint = BuildSapiUrl(context, "getCommsHistory", apiKey);
            using var response = await HttpClient.GetAsync(endpoint);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _cachedCommsError = $"SayIntentions comms history unavailable. HTTP {(int)response.StatusCode}.";
                return new SayIntentionsTransmissionResult(null, _cachedCommsError);
            }

            using var doc = JsonDocument.Parse(json);
            if (TryGetApiError(doc.RootElement, out string? error))
            {
                _cachedCommsError = $"SayIntentions comms history unavailable. {error}";
                return new SayIntentionsTransmissionResult(null, _cachedCommsError);
            }

            _cachedLastTransmission = FindLatestTransmission(doc.RootElement);
            if (_cachedLastTransmission == null)
                _cachedCommsError = "No SayIntentions communication history found for the active flight.";
        }
        catch (TaskCanceledException)
        {
            _cachedCommsError = "SayIntentions comms history timed out.";
        }
        catch (HttpRequestException ex)
        {
            _cachedCommsError = $"SayIntentions comms history network error. {ex.Message}";
        }
        catch (JsonException ex)
        {
            _cachedCommsError = $"SayIntentions comms history returned malformed JSON. {ex.Message}";
        }

        return new SayIntentionsTransmissionResult(_cachedLastTransmission, _cachedCommsError);
    }

    private async Task<SayIntentionsParkingResult> GetParkingAsync(
        SayIntentionsFlightContext context,
        string apiKey)
    {
        if (DateTime.UtcNow - _lastParkingFetchUtc < ParkingCacheDuration)
            return new SayIntentionsParkingResult(_cachedParking, _cachedParkingError);

        _lastParkingFetchUtc = DateTime.UtcNow;
        _cachedParking = null;
        _cachedParkingError = null;

        try
        {
            string endpoint = BuildSapiUrl(context, "getParking", apiKey);
            using var response = await HttpClient.GetAsync(endpoint);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _cachedParkingError = $"SayIntentions parking unavailable. HTTP {(int)response.StatusCode}.";
                return new SayIntentionsParkingResult(null, _cachedParkingError);
            }

            using var doc = JsonDocument.Parse(json);
            if (TryGetApiError(doc.RootElement, out string? error))
            {
                _cachedParkingError = $"SayIntentions parking unavailable. {error}";
                return new SayIntentionsParkingResult(null, _cachedParkingError);
            }

            JsonElement? parking = GetObject(doc.RootElement, "parking");
            if (parking is JsonElement p)
            {
                _cachedParking = new SayIntentionsParking
                {
                    Name = FirstNonEmpty(GetString(p, "name"), GetString(p, "gate"), GetString(p, "id")),
                    Latitude = GetDouble(p, "lat"),
                    Longitude = GetDouble(p, "lon"),
                    Heading = GetDouble(p, "heading")
                };
            }

            if (_cachedParking == null || string.IsNullOrWhiteSpace(_cachedParking.Name))
                _cachedParkingError = "No SayIntentions parking assignment found for the active flight.";
        }
        catch (TaskCanceledException)
        {
            _cachedParkingError = "SayIntentions parking request timed out.";
        }
        catch (HttpRequestException ex)
        {
            _cachedParkingError = $"SayIntentions parking network error. {ex.Message}";
        }
        catch (JsonException ex)
        {
            _cachedParkingError = $"SayIntentions parking returned malformed JSON. {ex.Message}";
        }

        return new SayIntentionsParkingResult(_cachedParking, _cachedParkingError);
    }

    private static string? ResolveApiKey(SayIntentionsFlightContext context)
    {
        string configured = SettingsManager.Current.SayIntentionsApiKey?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return string.IsNullOrWhiteSpace(context.ApiKey) ? null : context.ApiKey.Trim();
    }

    private static string BuildSapiUrl(SayIntentionsFlightContext context, string endpoint, string apiKey)
    {
        string host = string.IsNullOrWhiteSpace(context.Hostname)
            ? "https://apipri.sayintentions.ai"
            : context.Hostname.Trim().TrimEnd('/');
        if (!host.EndsWith("/sapi", StringComparison.OrdinalIgnoreCase))
            host += "/sapi";

        return $"{host}/{endpoint}?api_key={WebUtility.UrlEncode(apiKey)}";
    }

    private static SayIntentionsTransmission? FindLatestTransmission(JsonElement root)
    {
        var transmissions = new List<SayIntentionsTransmission>();
        CollectTransmissions(root, transmissions);
        return transmissions
            .Where(IsRadioTransmission)
            .OrderBy(t => t.StampZulu ?? DateTime.MinValue)
            .ThenBy(t => t.Id ?? 0)
            .LastOrDefault();
    }

    private static void CollectTransmissions(JsonElement element, List<SayIntentionsTransmission> transmissions)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var incoming = GetString(element, "incoming_message");
                var outgoing = GetString(element, "outgoing_message");
                var message = GetString(element, "message");
                var station = GetString(element, "station_name");
                var channel = GetString(element, "channel");
                var stampText = GetString(element, "stamp_zulu");
                DateTime? stamp = DateTime.TryParse(stampText, out var parsed)
                    ? parsed.ToUniversalTime()
                    : null;
                int? id = GetInt(element, "id");

                if (!string.IsNullOrWhiteSpace(incoming))
                {
                    AddTransmissionIfRadio(transmissions, new SayIntentionsTransmission(
                        "ATC", CleanSpeech(incoming), station, channel, stamp, id));
                }
                if (!string.IsNullOrWhiteSpace(outgoing))
                {
                    AddTransmissionIfRadio(transmissions, new SayIntentionsTransmission(
                        "Pilot", CleanSpeech(outgoing), station, channel, stamp, id));
                }
                if (string.IsNullOrWhiteSpace(incoming) && string.IsNullOrWhiteSpace(outgoing)
                    && !string.IsNullOrWhiteSpace(message)
                    && LooksLikeCommunication(message))
                {
                    AddTransmissionIfRadio(transmissions, new SayIntentionsTransmission(
                        "", CleanSpeech(message), station, channel, stamp, id));
                }

                foreach (var property in element.EnumerateObject())
                    CollectTransmissions(property.Value, transmissions);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectTransmissions(item, transmissions);
                break;
        }
    }

    private static void AddTransmissionIfRadio(
        List<SayIntentionsTransmission> transmissions,
        SayIntentionsTransmission transmission)
    {
        if (IsRadioTransmission(transmission))
            transmissions.Add(transmission);
    }

    private static bool IsRadioTransmission(SayIntentionsTransmission transmission)
    {
        string channel = transmission.Channel?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (channel.Equals("COM1", StringComparison.OrdinalIgnoreCase)
                || channel.Equals("COM2", StringComparison.OrdinalIgnoreCase)
                || channel.Equals("COM1_IN", StringComparison.OrdinalIgnoreCase)
                || channel.Equals("COM2_IN", StringComparison.OrdinalIgnoreCase))
            {
                return !LooksLikeCabinAnnouncement(transmission);
            }

            return false;
        }

        return !LooksLikeCabinAnnouncement(transmission)
            && LooksLikeAtcOrPilotTransmission(transmission);
    }

    private static bool LooksLikeAtcOrPilotTransmission(SayIntentionsTransmission transmission)
    {
        string combined = $"{transmission.Speaker} {transmission.StationName} {transmission.Message}";
        return Regex.IsMatch(
            combined,
            @"\b(ground|tower|delivery|departure|approach|center|radio|atis|clearance|traffic|pilot|runway|taxi|cleared|contact|frequency)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeCabinAnnouncement(SayIntentionsTransmission transmission)
    {
        string combined = $"{transmission.Speaker} {transmission.StationName} {transmission.Channel} {transmission.Message}";
        return Regex.IsMatch(
            combined,
            @"\b(cabin|passenger|passengers|flight attendant|attendant|crew|intercom|boarding|seat belt|seatbelt|beverage|meal|welcome aboard)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeCommunication(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim();
        if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        return trimmed.Length > 3;
    }

    private static List<string> ReadTaxiPath(JsonElement currentFlight)
    {
        var result = new List<string>();
        JsonElement? taxiPath = GetObject(currentFlight, "taxi_path");
        if (taxiPath is not JsonElement path || path.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in path.EnumerateArray())
        {
            string? value = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : FirstNonEmpty(
                    GetString(item, "taxiway"),
                    GetString(item, "name"),
                    GetString(item, "label"),
                    GetString(item, "id"));
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value.Trim());
        }

        return result;
    }

    private static bool TryGetApiError(JsonElement root, out string? error)
    {
        error = FirstNonEmpty(GetString(root, "error"), GetString(root, "message"));
        return !string.IsNullOrWhiteSpace(error)
            && root.TryGetProperty("error", out _);
    }

    private static JsonElement? GetObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }

    private static string? FindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    return ElementToString(property.Value);
                string? found = FindString(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                string? found = FindString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        JsonElement? value = GetObject(element, propertyName);
        return value.HasValue ? ElementToString(value.Value) : null;
    }

    private static string? ElementToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        JsonElement? value = GetObject(element, propertyName);
        if (value == null) return null;
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDouble(out double number))
            return number;
        string? text = ElementToString(value.Value);
        return double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number)
            ? number
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        JsonElement? value = GetObject(element, propertyName);
        if (value == null) return null;
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out int number))
            return number;
        string? text = ElementToString(value.Value);
        return int.TryParse(text, out number) ? number : null;
    }

    private static string? CleanIcao(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string cleaned = Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", "");
        return cleaned.Length is >= 3 and <= 4 ? cleaned : null;
    }

    public static string? CleanRunway(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string cleaned = value.Trim().ToUpperInvariant();
        cleaned = Regex.Replace(cleaned, @"\bRUNWAY\b", "", RegexOptions.IgnoreCase).Trim();
        var match = Regex.Match(cleaned, @"\b([0-9]{1,2}[LCR]?)\b", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        string runway = match.Groups[1].Value.ToUpperInvariant();
        if (runway.Length == 1)
            runway = "0" + runway;
        return runway;
    }

    private static string CleanSpeech(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }
}

public sealed class SayIntentionsFlightContext
{
    public string FlightJsonPath { get; init; } = "";
    public bool FlightJsonExists { get; init; }
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
    public string? ClearanceText { get; set; }
    public List<string> TaxiwaySequence { get; set; } = new();
    public SayIntentionsTransmission? LastFlightJsonTransmission { get; set; }
}

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

        return string.IsNullOrWhiteSpace(prefix)
            ? Message
            : $"{prefix}: {Message}";
    }
}

public sealed record SayIntentionsTransmissionResult(
    SayIntentionsTransmission? Transmission,
    string? Error);

public sealed class SayIntentionsParking
{
    public string? Name { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Heading { get; init; }
}

public sealed record SayIntentionsParkingResult(
    SayIntentionsParking? Parking,
    string? Error);

public sealed record SayIntentionsStatusResult(
    SayIntentionsFlightContext Context,
    SayIntentionsParking? Parking,
    string? ParkingError);
