using System.Net.Http;
using System.Text.Json;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// I/O for the SayIntentions integration: reads the local flight.json snapshot
/// and, when an API key is available, the SAPI comms-history and parking
/// endpoints. All parsing/classification lives in the pure sibling types
/// (SayIntentionsClearanceParser, SayIntentionsTransmissionClassifier,
/// SayIntentionsEndpoint) so it can be unit-tested.
///
/// Every failure path produces a pilot-readable string rather than an exception —
/// a blind pilot pressing the hotkey must always hear something actionable.
/// </summary>
public sealed class SayIntentionsService
{
    private const int ApiTimeoutSeconds = 5;
    private static readonly TimeSpan CommsCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ParkingCacheDuration = TimeSpan.FromSeconds(10);

    private static readonly LogChannel _log = Log.Channel("sayintentions");

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds)
    };

    private readonly string _flightJsonPath;

    private DateTime _lastCommsFetchUtc = DateTime.MinValue;
    private SayIntentionsTransmission? _cachedLastTransmission;
    private string? _cachedCommsError;
    private Task<SayIntentionsTransmissionResult>? _inFlightComms;

    private DateTime _lastParkingFetchUtc = DateTime.MinValue;
    private SayIntentionsParking? _cachedParking;
    private string? _cachedParkingError;
    private Task<SayIntentionsParkingResult>? _inFlightParking;

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
        var context = await ReadFlightContextAsync();
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
        var context = await ReadFlightContextAsync();
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

    /// <summary>Off-thread flight.json read. The file read plus the recursive JSON
    /// walk must never run on the UI thread — every hotkey handler awaits this.</summary>
    public Task<SayIntentionsFlightContext> ReadFlightContextAsync() => Task.Run(ReadFlightContext);

    public SayIntentionsFlightContext ReadFlightContext()
    {
        var context = new SayIntentionsFlightContext
        {
            FlightJsonPath = _flightJsonPath,
            FlightJsonExists = File.Exists(_flightJsonPath)
        };

        if (!context.FlightJsonExists)
        {
            _log.Debug($"flight.json not present at {_flightJsonPath}");
            return context;
        }

        try
        {
            // SayIntentions rewrites this file while we read it.
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
                context.DepartureRunway = SayIntentionsClearanceParser.CleanRunway(FirstNonEmpty(
                    GetString(flight, "flight_plan_departing_runway"),
                    GetString(flight, "departing_runway"),
                    GetString(flight, "departure_runway")));
                context.ArrivalRunway = SayIntentionsClearanceParser.CleanRunway(FirstNonEmpty(
                    GetString(flight, "flight_plan_arriving_runway"),
                    GetString(flight, "arriving_runway"),
                    GetString(flight, "arrival_runway")));
                context.FlightPlanRoute = GetString(flight, "flight_plan_route");
                context.TaxiwaySequence = ReadTaxiPath(flight);
            }

            context.ClearedForTakeoff = SayIntentionsClearanceParser.CleanRunway(GetString(details, "cleared_for_takeoff"));
            context.ClearedForLanding = SayIntentionsClearanceParser.CleanRunway(GetString(details, "cleared_for_landing"));
            context.Runway = SayIntentionsClearanceParser.CleanRunway(GetString(details, "runway"));
            context.ClearanceText = FirstNonEmpty(
                GetString(details, "clearance"),
                GetString(details, "last_clearance"),
                GetString(details, "taxi_clearance"),
                FindString(root, "clearance_text"),
                FindString(root, "taxi_clearance"));
            context.LastFlightJsonTransmission = FindLatestTransmission(root);

            if (string.IsNullOrWhiteSpace(context.ClearanceText) && context.LastFlightJsonTransmission != null)
                context.ClearanceText = context.LastFlightJsonTransmission.Message;

            _log.Debug($"flight.json read: airport={context.CurrentAirport ?? "-"} " +
                       $"gate={context.AssignedGate ?? "-"} taxiPath={context.TaxiwaySequence.Count} " +
                       $"clearance={(string.IsNullOrWhiteSpace(context.ClearanceText) ? "none" : "present")}");
        }
        catch (JsonException ex)
        {
            context.Error = $"SayIntentions flight.json is malformed. {ex.Message}";
            _log.Warn(context.Error);
        }
        catch (IOException ex)
        {
            context.Error = $"Could not read SayIntentions flight.json. {ex.Message}";
            _log.Warn(context.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Error = $"Could not access SayIntentions flight.json. {ex.Message}";
            _log.Warn(context.Error);
        }

        return context;
    }

    /// <summary>
    /// Serves a fresh cached result, otherwise JOINS the in-flight request rather
    /// than starting a second one. PR #86 stamped the cache time before awaiting
    /// and nulled the cache, so a second hotkey press during a slow request hit a
    /// populated-but-empty cache and spoke "no transmission available" — exactly
    /// when the pilot pressed again because they had heard nothing.
    /// </summary>
    private Task<SayIntentionsTransmissionResult> GetLastCommsHistoryTransmissionAsync(
        SayIntentionsFlightContext context, string apiKey)
    {
        if (DateTime.UtcNow - _lastCommsFetchUtc < CommsCacheDuration)
            return Task.FromResult(new SayIntentionsTransmissionResult(_cachedLastTransmission, _cachedCommsError));

        return _inFlightComms ??= FetchCommsAsync(context, apiKey);
    }

    private async Task<SayIntentionsTransmissionResult> FetchCommsAsync(
        SayIntentionsFlightContext context, string apiKey)
    {
        SayIntentionsTransmission? transmission = null;
        string? error = null;

        try
        {
            string endpoint = SayIntentionsEndpoint.Build(context.Hostname, "getCommsHistory", apiKey);
            _log.Debug($"GET {SayIntentionsEndpoint.Redact(endpoint)}");

            using var response = await HttpClient.GetAsync(endpoint);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                error = $"SayIntentions comms history unavailable. HTTP {(int)response.StatusCode}.";
            }
            else
            {
                using var doc = JsonDocument.Parse(json);
                if (TryGetApiError(doc.RootElement, out string? apiError))
                {
                    error = $"SayIntentions comms history unavailable. {apiError}";
                }
                else
                {
                    transmission = FindLatestTransmission(doc.RootElement);
                    if (transmission == null)
                        error = "No SayIntentions communication history found for the active flight.";
                }
            }
        }
        catch (TaskCanceledException)
        {
            error = "SayIntentions comms history timed out.";
        }
        catch (HttpRequestException ex)
        {
            error = $"SayIntentions comms history network error. {ex.Message}";
        }
        catch (JsonException ex)
        {
            error = $"SayIntentions comms history returned malformed JSON. {ex.Message}";
        }
        finally
        {
            _cachedLastTransmission = transmission;
            _cachedCommsError = error;
            _lastCommsFetchUtc = DateTime.UtcNow;
            _inFlightComms = null;
        }

        if (error != null) _log.Warn(error);
        return new SayIntentionsTransmissionResult(transmission, error);
    }

    private Task<SayIntentionsParkingResult> GetParkingAsync(
        SayIntentionsFlightContext context, string apiKey)
    {
        if (DateTime.UtcNow - _lastParkingFetchUtc < ParkingCacheDuration)
            return Task.FromResult(new SayIntentionsParkingResult(_cachedParking, _cachedParkingError));

        return _inFlightParking ??= FetchParkingAsync(context, apiKey);
    }

    private async Task<SayIntentionsParkingResult> FetchParkingAsync(
        SayIntentionsFlightContext context, string apiKey)
    {
        SayIntentionsParking? parking = null;
        string? error = null;

        try
        {
            string endpoint = SayIntentionsEndpoint.Build(context.Hostname, "getParking", apiKey);
            _log.Debug($"GET {SayIntentionsEndpoint.Redact(endpoint)}");

            using var response = await HttpClient.GetAsync(endpoint);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                error = $"SayIntentions parking unavailable. HTTP {(int)response.StatusCode}.";
            }
            else
            {
                using var doc = JsonDocument.Parse(json);
                if (TryGetApiError(doc.RootElement, out string? apiError))
                {
                    error = $"SayIntentions parking unavailable. {apiError}";
                }
                else
                {
                    JsonElement? parkingElement = GetObject(doc.RootElement, "parking");
                    if (parkingElement is JsonElement p)
                    {
                        parking = new SayIntentionsParking
                        {
                            Name = FirstNonEmpty(GetString(p, "name"), GetString(p, "gate"), GetString(p, "id")),
                            Latitude = GetDouble(p, "lat"),
                            Longitude = GetDouble(p, "lon"),
                            Heading = GetDouble(p, "heading")
                        };
                    }

                    if (parking == null || string.IsNullOrWhiteSpace(parking.Name))
                    {
                        parking = null;
                        error = "No SayIntentions parking assignment found for the active flight.";
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            error = "SayIntentions parking request timed out.";
        }
        catch (HttpRequestException ex)
        {
            error = $"SayIntentions parking network error. {ex.Message}";
        }
        catch (JsonException ex)
        {
            error = $"SayIntentions parking returned malformed JSON. {ex.Message}";
        }
        finally
        {
            _cachedParking = parking;
            _cachedParkingError = error;
            _lastParkingFetchUtc = DateTime.UtcNow;
            _inFlightParking = null;
        }

        if (error != null) _log.Warn(error);
        return new SayIntentionsParkingResult(parking, error);
    }

    /// <summary>Settings key wins; otherwise the key flight.json publishes during
    /// an active flight. Never logged.</summary>
    private static string? ResolveApiKey(SayIntentionsFlightContext context)
    {
        string configured = SettingsManager.Current.SayIntentionsApiKey?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return string.IsNullOrWhiteSpace(context.ApiKey) ? null : context.ApiKey.Trim();
    }

    private static SayIntentionsTransmission? FindLatestTransmission(JsonElement root)
    {
        var transmissions = new List<SayIntentionsTransmission>();
        CollectTransmissions(root, transmissions);
        return transmissions
            .OrderBy(t => t.StampZulu ?? DateTime.MinValue)
            .ThenBy(t => t.Id ?? 0)
            .LastOrDefault();
    }

    private static void CollectTransmissions(JsonElement element, List<SayIntentionsTransmission> transmissions)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                string? incoming = GetString(element, "incoming_message");
                string? outgoing = GetString(element, "outgoing_message");
                string? message = GetString(element, "message");
                string? station = GetString(element, "station_name");
                string? channel = GetString(element, "channel");
                string? stampText = GetString(element, "stamp_zulu");
                DateTime? stamp = DateTime.TryParse(stampText, out var parsed) ? parsed.ToUniversalTime() : null;
                int? id = GetInt(element, "id");

                if (!string.IsNullOrWhiteSpace(incoming))
                    AddIfRadio(transmissions, "ATC", incoming, station, channel, stamp, id);

                if (!string.IsNullOrWhiteSpace(outgoing))
                    AddIfRadio(transmissions, "Pilot", outgoing, station, channel, stamp, id);

                if (string.IsNullOrWhiteSpace(incoming) && string.IsNullOrWhiteSpace(outgoing)
                    && LooksLikeCommunication(message))
                {
                    AddIfRadio(transmissions, "", message!, station, channel, stamp, id);
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

    private static void AddIfRadio(
        List<SayIntentionsTransmission> transmissions,
        string speaker, string message, string? station, string? channel, DateTime? stamp, int? id)
    {
        string cleaned = CleanSpeech(message);
        if (SayIntentionsTransmissionClassifier.IsRadioTransmission(speaker, station, channel, cleaned))
            transmissions.Add(new SayIntentionsTransmission(speaker, cleaned, station, channel, stamp, id));
    }

    private static bool LooksLikeCommunication(string? text)
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

    /// <summary>An API error is only an error when the payload actually carries an
    /// "error" member. The presence check is case-insensitive to match the value
    /// lookup — PR #86 mixed a case-insensitive read with a case-sensitive
    /// TryGetProperty, so a payload using "Error" silently parsed as success.</summary>
    private static bool TryGetApiError(JsonElement root, out string? error)
    {
        error = FirstNonEmpty(GetString(root, "error"), GetString(root, "message"));
        return !string.IsNullOrWhiteSpace(error) && GetObject(root, "error") != null;
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

    private static string? ElementToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

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
        string cleaned = System.Text.RegularExpressions.Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", "");
        return cleaned.Length is >= 3 and <= 4 ? cleaned : null;
    }

    private static string CleanSpeech(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

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
