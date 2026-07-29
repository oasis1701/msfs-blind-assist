using System.Net.Http;
using System.Text.Json;
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

    /// <summary>Spoken when transmissions were found but every one of them was the
    /// pilot's own. It is a different answer from "nothing found": the pilot DID hear
    /// something on the frequency, it just was not the controller, and telling them so
    /// stops them pressing the key again waiting for a call that has not come.</summary>
    private const string NoAtcTransmissionMessage =
        "No ATC transmission yet. Only your own calls so far.";

    private static readonly TimeSpan CommsCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ParkingCacheDuration = TimeSpan.FromSeconds(10);

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The largest a raw Unix-epoch-seconds value can be while <see cref="UnixEpoch"/>
    /// plus that many seconds still lands inside DateTime's representable range (year
    /// 1-9999). Computed from UnixEpoch/DateTime.MaxValue rather than hardcoded so it
    /// can never drift out of sync with the type it guards. See ReadTaxiPathStampUtc —
    /// this is the range check that keeps a bogus/rescaled timestamp away from
    /// DateTime.AddSeconds, which throws ArgumentOutOfRangeException outside it.
    /// </summary>
    private static readonly double MaxPlausibleUnixSeconds = (DateTime.MaxValue - UnixEpoch).TotalSeconds;

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

        // No key to reach comms history with, and nothing in the file. The honest
        // reason is that SayIntentions is not running, or is running without
        // publishing a key — there is no setting left for the pilot to go and fill in.
        return new SayIntentionsTransmissionResult(
            null,
            context.OnlyPilotTransmissions
                ? NoAtcTransmissionMessage
                : context.FlightJsonExists
                    ? "No SayIntentions transmission found. Check SayIntentions is connected to this flight."
                    : "SayIntentions flight.json not found. Start an active SayIntentions flight.");
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

            // current_flight.taxi_path is SayIntentions' own taxi-route geometry, a
            // live EDDF capture gave ~200 entries shaped
            // {"heading": 93.92, "point": {"lon": …, "lat": …}} with no name anywhere.
            // ReadTaxiPathPoints below reads ONLY point.lat/point.lon — see its doc
            // comment for why nothing else in an entry is ever touched. See the
            // rewritten CLAUDE.md invariant ("SayIntentions integration") and
            // docs/sayintentions.md for the hazard this boundary exists to prevent.
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
                context.TaxiPathPoints = ReadTaxiPathPoints(flight);
            }

            context.ClearedForTakeoff = SayIntentionsClearanceParser.CleanRunway(GetString(details, "cleared_for_takeoff"));
            context.ClearedForLanding = SayIntentionsClearanceParser.CleanRunway(GetString(details, "cleared_for_landing"));
            context.Runway = SayIntentionsClearanceParser.CleanRunway(GetString(details, "runway"));
            context.AircraftIcao = GetString(details, "aircraft_icao");
            context.OnGround = GetBool(details, "on_ground");
            context.DepartureWeather = ReadAirportWeather(GetObject(details, "departure_wx"));
            context.ArrivalWeather = ReadAirportWeather(GetObject(details, "arrival_wx"));
            context.TaxiPathStampUtc = ReadTaxiPathStampUtc(details, _flightJsonPath);

            context.ClearanceText = FirstNonEmpty(
                GetString(details, "clearance"),
                GetString(details, "last_clearance"),
                GetString(details, "taxi_clearance"),
                FindString(root, "clearance_text"),
                FindString(root, "taxi_clearance"));
            context.LastFlightJsonTransmission = FindLatestTransmission(root, out bool pilotOnly);
            context.OnlyPilotTransmissions = pilotOnly;

            // The pilot's own transmissions are already filtered out above, so a
            // clearance can only ever be taken from the controller — never from the
            // pilot's readback of one, which is the newest thing on the frequency at
            // exactly the moment the import key gets pressed.
            if (string.IsNullOrWhiteSpace(context.ClearanceText) && context.LastFlightJsonTransmission != null)
                context.ClearanceText = context.LastFlightJsonTransmission.Message;

            _log.Debug($"flight.json read: airport={context.CurrentAirport ?? "-"} " +
                       $"gate={context.AssignedGate ?? "-"} " +
                       $"clearance={(string.IsNullOrWhiteSpace(context.ClearanceText) ? "none" : "present")} " +
                       $"taxiPathPoints={context.TaxiPathPoints.Count}");
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
    /// Reads <c>current_flight.taxi_path</c> — SayIntentions' own taxi-route
    /// geometry, published per entry as <c>{"heading":…, "point":{"lon":…,"lat":…}}</c>.
    ///
    /// COORDINATES ONLY. This deliberately reads <c>point.lat</c> / <c>point.lon</c>
    /// and NOTHING else. Do not add a read of <c>id</c>, <c>label</c>, or <c>name</c>
    /// here, even if a future capture appears to carry one: the reader deleted in
    /// 2026-07 accepted exactly those members, which is exactly what a geometry
    /// array would plausibly gain on a schema change, and ~200 point ids would have
    /// become "taxiway names" that silently replaced the clearance-derived route.
    /// Names come from the airport's own TaxiGraph, never from SI — see the
    /// rewritten CLAUDE.md invariant under "SayIntentions integration".
    ///
    /// An entry missing either coordinate is skipped, not defaulted to (0,0) — a
    /// zeroed point would snap to nothing useful at best and to some other
    /// airport's pavement at worst.
    /// </summary>
    private static IReadOnlyList<GeoPoint> ReadTaxiPathPoints(JsonElement flight)
    {
        JsonElement? taxiPath = GetObject(flight, "taxi_path");
        if (taxiPath is not JsonElement path || path.ValueKind != JsonValueKind.Array)
            return Array.Empty<GeoPoint>();

        var points = new List<GeoPoint>();
        foreach (JsonElement entry in path.EnumerateArray())
        {
            if (GetObject(entry, "point") is not JsonElement point)
                continue;

            double? latitude = GetDouble(point, "lat");
            double? longitude = GetDouble(point, "lon");
            if (latitude is null || longitude is null)
                continue;

            points.Add(new GeoPoint(latitude.Value, longitude.Value));
        }

        return points;
    }

    /// <summary>
    /// When this flight.json snapshot (and therefore its taxi_path, when present)
    /// was generated, in UTC.
    ///
    /// <c>flight_details.timestamp</c> is a raw Unix epoch in SECONDS, fractional —
    /// e.g. <c>1785357161.40969</c> — NOT an ISO/"Zulu" string like <c>stamp_zulu</c>
    /// elsewhere in this file (see ParseZuluStamp). Confirmed against ten real wire
    /// captures (LSZH and EGLL, 2026-07-29/30,
    /// docs/superpowers/plans/2026-07-29-geometry-captures/): every one carried this
    /// shape, each within a few seconds of the file's own last-write time. Feeding
    /// that raw numeric string through a date-string parser (the shape used for
    /// stamp_zulu) never matches any recognized format and always fails, which would
    /// make this fall back to the file time on every real flight — silently
    /// defeating the point of preferring SI's own stamp. Kept as a plain double
    /// (GetDouble already handles both a JSON number and a numeric JSON string) and
    /// converted via epoch arithmetic instead.
    ///
    /// Falls back to the file's own last-write time when the field is absent, not a
    /// plausible epoch-seconds instant (see below), or the file can no longer be
    /// reached for its timestamp — a later answer from this app's read, rather than
    /// SI's generation time, but still an honest one instead of leaving a genuinely
    /// present path with no stamp at all.
    ///
    /// The value is range-checked BEFORE conversion: it must be positive and land
    /// within DateTime's representable range once added to UnixEpoch
    /// (<see cref="MaxPlausibleUnixSeconds"/>), or it falls straight through to the
    /// file-time fallback instead of reaching <see cref="DateTime.AddSeconds"/>, which
    /// throws <see cref="ArgumentOutOfRangeException"/> for an out-of-range value —
    /// and that exception sits outside ReadFlightContext's catch list
    /// (JsonException/IOException/UnauthorizedAccessException), so unguarded it took
    /// down Ctrl+S, Ctrl+Shift+S and Alt+Shift+S all at once. The commonest way this
    /// fires for real is SayIntentions migrating the field to milliseconds — a live
    /// value like 1785357161409 overflows DateTime's year-9999 ceiling by tens of
    /// thousands of years when misread as seconds, which is exactly what makes the
    /// range check catch it (also catches 1e30, an "Infinity" string, and a
    /// microsecond-scale epoch). The same check rejects 0 and negative values too —
    /// both "successfully" convert to a real DateTime (1970 / a 1913-ish date) without
    /// throwing, so without folding them into this same range they would silently
    /// stop the mtime fallback from ever running for an explicit "unset" sentinel.
    /// </summary>
    private static DateTime? ReadTaxiPathStampUtc(JsonElement details, string flightJsonPath)
    {
        double? unixSeconds = GetDouble(details, "timestamp");
        if (unixSeconds is double seconds)
        {
            if (seconds > 0 && seconds <= MaxPlausibleUnixSeconds)
                return UnixEpoch.AddSeconds(seconds);

            _log.Debug("flight_details.timestamp " +
                       seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                       " is not a plausible Unix-epoch-seconds instant; using flight.json's file time instead.");
        }

        try
        {
            return File.GetLastWriteTimeUtc(flightJsonPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
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
                    transmission = FindLatestTransmission(doc.RootElement, out bool pilotOnly);
                    if (transmission == null)
                    {
                        error = pilotOnly
                            ? NoAtcTransmissionMessage
                            : "No SayIntentions communication history found for the active flight.";
                    }
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

    /// <summary>The key SayIntentions publishes in flight.json during an active
    /// flight (<c>flight_details.api_key</c>), which a live capture confirms is
    /// always there. There is deliberately no setting to override it — a field the
    /// user must fill in by hand to duplicate something the file already carries is
    /// one more thing to get wrong. Never logged.</summary>
    private static string? ResolveApiKey(SayIntentionsFlightContext context) =>
        string.IsNullOrWhiteSpace(context.ApiKey) ? null : context.ApiKey.Trim();

    private static SayIntentionsTransmission? FindLatestTransmission(JsonElement root) =>
        FindLatestTransmission(root, out _);

    /// <summary>
    /// The newest transmission the pilot did not make themselves.
    ///
    /// A Pilot-speaker transmission is DROPPED, never merely outranked. Ordering by
    /// stamp and preferring the ATC call only WITHIN one record still announced a
    /// pilot transmission that arrived in a later record — and a readback is normally
    /// the newest thing on the frequency at exactly the moment someone presses the
    /// hotkey, so "read the last transmission" spoke the pilot their own words back,
    /// prefixed "Pilot:". The controller is the only thing this readout exists to give.
    ///
    /// An EMPTY speaker stays eligible. It comes from the bare-"message" fallback,
    /// which carries no direction at all, so it is not identified as the pilot;
    /// dropping it would leave a payload shape we cannot classify silent, and for a
    /// readout whose whole job is to say what was heard, silence is the worse failure.
    /// It also cannot be mistaken for the pilot when spoken — with no speaker,
    /// ToAnnouncement prefixes nothing.
    /// </summary>
    /// <param name="pilotOnly">True when transmissions were found but every one was the
    /// pilot's, so the caller can say why instead of the generic "nothing found".</param>
    private static SayIntentionsTransmission? FindLatestTransmission(JsonElement root, out bool pilotOnly)
    {
        var transmissions = new List<SayIntentionsTransmission>();
        CollectTransmissions(root, transmissions);

        var fromOthers = transmissions.Where(t => t.Speaker != PilotSpeaker).ToList();
        pilotOnly = fromOthers.Count == 0 && transmissions.Count > 0;

        return fromOthers
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

    /// <summary>
    /// One <c>*_wx</c> block. Returns null when the member is absent or carries nothing
    /// worth showing — SI writes the block with most fields empty at times, and an
    /// all-blank weather section in the report is worse than no section.
    ///
    /// <c>phonetic</c> is deliberately not read. It spells values out ("two-two-left",
    /// "one-six-zero at eight") for SI's own text-to-speech; the screen reader does its
    /// own pronunciation, and pre-spelt text reads worse, not better.
    /// </summary>
    private static SayIntentionsAirportWeather? ReadAirportWeather(JsonElement? element)
    {
        if (element is not JsonElement wx || wx.ValueKind != JsonValueKind.Object) return null;

        var weather = new SayIntentionsAirportWeather
        {
            Airport = GetString(wx, "airport"),
            InformationLetter = GetString(wx, "current"),
            Atis = GetString(wx, "atis"),
            ActiveRunwaysArriving = GetString(wx, "active_runways_arriving"),
            ActiveRunwaysDeparting = GetString(wx, "active_runways_departing"),
            PreferredRunway = GetString(wx, "preferred_runway"),
            CurrentlyOperating = GetString(wx, "currently_operating"),
            WindDirection = GetDouble(wx, "wind_direction"),
            WindSpeed = GetDouble(wx, "wind_speed"),
            WindGusting = GetDouble(wx, "wind_gusting"),
            Visibility = GetDouble(wx, "visibility"),
            Altimeter = GetDouble(wx, "altimeter"),
            DensityAltitude = GetDouble(wx, "density_altitude"),
            Metar = GetString(wx, "metar"),
            Taf = GetString(wx, "taf")
        };

        bool hasAnything =
            !string.IsNullOrWhiteSpace(weather.Atis)
            || !string.IsNullOrWhiteSpace(weather.Metar)
            || !string.IsNullOrWhiteSpace(weather.Taf)
            || !string.IsNullOrWhiteSpace(weather.ActiveRunwaysArriving)
            || !string.IsNullOrWhiteSpace(weather.ActiveRunwaysDeparting)
            || weather.WindSpeed.HasValue;

        return hasAnything ? weather : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        JsonElement? value = GetObject(element, propertyName);
        if (value == null) return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // SI writes on_ground as the number 1/0, not a JSON boolean.
            JsonValueKind.Number => value.Value.TryGetDouble(out double n) ? n != 0 : null,
            JsonValueKind.String => bool.TryParse(value.Value.GetString(), out bool parsed)
                ? parsed
                : int.TryParse(value.Value.GetString(), out int number) ? number != 0 : null,
            _ => null
        };
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
