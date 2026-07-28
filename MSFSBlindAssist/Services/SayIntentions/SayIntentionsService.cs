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
    private const string AtcSpeaker = "ATC";
    private const string PilotSpeaker = "Pilot";

    private static readonly TimeSpan CommsCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ParkingCacheDuration = TimeSpan.FromSeconds(10);

    private static readonly LogChannel _log = Log.Channel("sayintentions");

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds)
    };

    /// <summary>Wording that marks a bare "message" payload (one with no "error"
    /// member) as a rejection rather than a status line. Kept tight on purpose —
    /// see TryGetApiError.</summary>
    private static readonly System.Text.RegularExpressions.Regex ErrorMessageVocabulary = new(
        @"\b(?:ERROR|INVALID|UNAUTHORI[SZ]ED|FORBIDDEN|DENIED|EXPIRED|REQUIRED|MISSING|" +
        @"FAILED|FAILURE|API\s+KEY|NOT\s+FOUND|BAD\s+REQUEST|RATE\s+LIMIT)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.Compiled);

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

            // current_flight.taxi_path is deliberately NOT read. It is GEOMETRY, not
            // taxiway names — a live EDDF capture gave ~200 entries shaped
            // {"heading": 93.92, "point": {"lon": …, "lat": …}} with no name anywhere.
            // A reader that hunts for name-ish members in it can only mis-fire: the
            // members a geometry array would plausibly gain (id, label) are exactly
            // the ones such a reader trusts, and the route it fed REPLACED the
            // sequence parsed from the clearance. See docs/sayintentions.md.
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
                       $"gate={context.AssignedGate ?? "-"} " +
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
    ///
    /// The latch must only ever hold a request that is genuinely still running.
    /// A plain "??= Fetch(...)" could not guarantee that: an async method may run to
    /// completion synchronously (an exception thrown before its first await is
    /// captured into the returned Task, so the body — including the finally that
    /// clears the latch — has already run by the time ??= stores it), which latched
    /// a finished, possibly faulted task forever and replayed it on every later
    /// press. Hence: ignore a completed latch, and drop one that finished before we
    /// could store it.
    /// </summary>
    private Task<SayIntentionsTransmissionResult> GetLastCommsHistoryTransmissionAsync(
        SayIntentionsFlightContext context, string apiKey)
    {
        if (DateTime.UtcNow - _lastCommsFetchUtc < CommsCacheDuration)
            return Task.FromResult(new SayIntentionsTransmissionResult(_cachedLastTransmission, _cachedCommsError));

        if (_inFlightComms is { IsCompleted: false } joinable)
            return joinable;

        Task<SayIntentionsTransmissionResult> fetch = FetchCommsAsync(context, apiKey);
        _inFlightComms = fetch;
        if (fetch.IsCompleted)
            Interlocked.CompareExchange(ref _inFlightComms, null, fetch);
        return fetch;
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
                    // The API can flag a failure without naming one; don't speak a
                    // dangling "unavailable. ." at the pilot.
                    error = string.IsNullOrWhiteSpace(apiError)
                        ? "SayIntentions comms history unavailable."
                        : $"SayIntentions comms history unavailable. {apiError}";
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

    /// <summary>Same cache-then-join contract, and the same latch-ordering rule, as
    /// GetLastCommsHistoryTransmissionAsync — see the note there.</summary>
    private Task<SayIntentionsParkingResult> GetParkingAsync(
        SayIntentionsFlightContext context, string apiKey)
    {
        if (DateTime.UtcNow - _lastParkingFetchUtc < ParkingCacheDuration)
            return Task.FromResult(new SayIntentionsParkingResult(_cachedParking, _cachedParkingError));

        if (_inFlightParking is { IsCompleted: false } joinable)
            return joinable;

        Task<SayIntentionsParkingResult> fetch = FetchParkingAsync(context, apiKey);
        _inFlightParking = fetch;
        if (fetch.IsCompleted)
            Interlocked.CompareExchange(ref _inFlightParking, null, fetch);
        return fetch;
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
                    error = string.IsNullOrWhiteSpace(apiError)
                        ? "SayIntentions parking unavailable."
                        : $"SayIntentions parking unavailable. {apiError}";
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

    /// <summary>
    /// Newest transmission wins. Within ONE record the controller's call and the
    /// pilot's readback share a stamp and an id, so the final key ranks the pilot's
    /// own words first and the ATC call last: "read the last transmission" must give
    /// the pilot the controller, not a repeat of what they just said themselves. It
    /// is the last key, so ordering BETWEEN records is untouched — a genuinely later
    /// pilot transmission still wins.
    /// </summary>
    private static SayIntentionsTransmission? FindLatestTransmission(JsonElement root)
    {
        var transmissions = new List<SayIntentionsTransmission>();
        CollectTransmissions(root, transmissions);
        return transmissions
            .OrderBy(t => t.StampZulu ?? DateTime.MinValue)
            .ThenBy(t => t.Id ?? 0)
            .ThenBy(t => t.Speaker == PilotSpeaker ? 0 : 1)
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
                DateTime? stamp = ParseZuluStamp(stampText);
                int? id = GetInt(element, "id");

                // Direction is from SayIntentions' point of view, NOT the pilot's:
                // incoming_message is what SI received (the PILOT speaking) and
                // outgoing_message is what SI sent back (ATC). Verified against a live
                // EDDF session: every turn pair reads "Request taxi" / "Taxi to
                // Terminal 3 Gate J1 via …", and across 89 records outgoing_message
                // carried 20 ATC-phrase hits and zero pilot-phrase hits. Labelling
                // these the intuitive way round makes Ctrl+S announce the pilot's own
                // readback as ATC.
                if (!string.IsNullOrWhiteSpace(incoming))
                    AddIfRadio(transmissions, PilotSpeaker, incoming, station, channel, stamp, id);

                if (!string.IsNullOrWhiteSpace(outgoing))
                    AddIfRadio(transmissions, AtcSpeaker, outgoing, station, channel, stamp, id);

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

    /// <summary>stamp_zulu is a UTC wire timestamp, not a locale-formatted date. Parsing
    /// it with the ambient culture reorders the history on a d/M/y machine, so the hotkey
    /// speaks the wrong "last transmission" — pin it to InvariantCulture, and treat a
    /// stamp with no offset as the UTC it claims to be rather than local time.</summary>
    private static DateTime? ParseZuluStamp(string? stampText) =>
        DateTime.TryParse(
            stampText,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out DateTime parsed)
            ? parsed
            : null;

    private static bool LooksLikeCommunication(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim();
        if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        return trimmed.Length > 3;
    }

    /// <summary>
    /// An API error is decided by the VALUE of the "error" member, never by its mere
    /// presence: SayIntentions answers a good request with {"error": false, "message":
    /// "OK"}, and a presence check spoke "SayIntentions comms history unavailable.
    /// false." over that perfectly valid response. Only a non-empty string, a true
    /// boolean, or a non-zero number is an error — null, false, 0 and "" are not.
    /// A truthy flag carries no reason a pilot can act on, so the sibling "message"
    /// supplies the text; when there is none the caller speaks the bare "unavailable".
    ///
    /// With NO "error" member at all we fall back to "message", but only when it reads
    /// like a rejection (ErrorMessageVocabulary): a bare {"message": "Invalid API key"}
    /// must reach the pilot instead of being swallowed into the generic "no history
    /// found", while an informational {"message": "3 records"} must not be mistaken for
    /// a failure. Losing a real transmission is worse than losing a reason, so that
    /// fallback stays deliberately narrow.
    ///
    /// Lookups are case-insensitive (GetObject) — PR #86 mixed a case-insensitive read
    /// with a case-sensitive TryGetProperty, so a payload using "Error" parsed as success.
    /// </summary>
    internal static bool TryGetApiError(JsonElement root, out string? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object) return false;

        string? messageText = GetString(root, "message")?.Trim();

        if (GetObject(root, "error") is JsonElement errorValue)
        {
            if (!IsErrorValue(errorValue)) return false;

            error = errorValue.ValueKind == JsonValueKind.String
                ? FirstNonEmpty(errorValue.GetString())
                : FirstNonEmpty(messageText);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(messageText)
            && !HasDataPayload(root)
            && ErrorMessageVocabulary.IsMatch(messageText))
        {
            error = messageText;
            return true;
        }

        return false;
    }

    /// <summary>Truthiness of an "error" member. Anything not listed — null, false, 0,
    /// an empty/whitespace string, an object or an array — is NOT an error, so a success
    /// payload can never be reported as a failure.</summary>
    private static bool IsErrorValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.True => true,
        JsonValueKind.Number => value.TryGetDouble(out double number) && number != 0,
        _ => false
    };

    /// <summary>True when the response carries a nested object or array — an actual
    /// payload. Alongside one, a "message" is a status line, not a rejection, so the
    /// error-wording fallback stands down and the payload is read as normal.</summary>
    private static bool HasDataPayload(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return true;
        }
        return false;
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
