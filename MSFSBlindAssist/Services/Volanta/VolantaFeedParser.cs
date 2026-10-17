using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services.Volanta;

/// <summary>
/// Parses the files Volanta's live map downloads, and the hub messages it receives, into
/// <see cref="VolantaFlight"/>, <see cref="VolantaController"/> and the pilot's own position and flight. Pure:
/// <see cref="VolantaLink"/> hands it the text Volanta's page already holds. Every parse is tolerant: a missing
/// or mistyped field becomes its default, and an entry with no position is dropped, as Volanta's map drops it.
/// Field shapes measured from Volanta 1.19.2 on 2026-10-15 (docs/volanta.md).
/// </summary>
public static class VolantaFeedParser
{
    /// <summary>Below this ground speed a network pilot counts as on the ground: the network file reports
    /// <c>onGround: false</c> and a height above ground of 0 for every pilot, parked or not.</summary>
    public const double NetworkGroundSpeedKt = 40;

    /// <summary>The SignalR record separator: one hub frame can carry several messages.</summary>
    private const char RecordSeparator = '\u001e';

    /// <summary>Volanta's own flight file (every Volanta user on the map).</summary>
    public static VolantaFeed<VolantaFlight>? ParseVolantaFlights(string json) => ParseFlights(json, networkFile: false);

    /// <summary>Volanta's merged network file (VATSIM, IVAO, PilotEdge and APOC pilots).</summary>
    public static VolantaFeed<VolantaFlight>? ParseNetworkFlights(string json) => ParseFlights(json, networkFile: true);

    private static VolantaFeed<VolantaFlight>? ParseFlights(string json, bool networkFile)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetFeedArray(doc.RootElement, out var time, out var data)) return null;
            var flights = new List<VolantaFlight>(data.GetArrayLength());
            foreach (var f in data.EnumerateArray())
            {
                var flight = ParseFlight(f, networkFile);
                if (flight != null) flights.Add(flight);
            }
            return new VolantaFeed<VolantaFlight>(time, flights);
        }
        catch (JsonException) { return null; }
    }

    private static VolantaFlight? ParseFlight(JsonElement f, bool networkFile)
    {
        if (f.ValueKind != JsonValueKind.Object) return null;
        string id = Str(f, "id");
        if (id.Length == 0 || !f.TryGetProperty("position", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        double? lat = Num(p, "latitude"), lon = Num(p, "longitude");
        if (lat is null || lon is null) return null;

        double groundSpeed = Num(p, "groundSpeed") ?? 0;
        var source = networkFile ? ParseSource(Str(f, "network")) : VolantaSource.Volanta;
        VolantaSource? alsoOn = null;
        if (!networkFile)
        {
            string mp = Str(f, "multiplayerNetwork");
            if (mp.Length > 0) alsoOn = ParseSource(mp);
        }

        // The network file carries no real on-ground flag, height above ground or vertical speed: it always
        // says false and 0. Infer ground from speed there; trust Volanta's own values for Volanta users.
        bool onGround = networkFile ? groundSpeed < NetworkGroundSpeedKt : Bool(p, "onGround");
        double? agl = networkFile ? null : Num(p, "altitudeAgl");
        double? vs = networkFile ? null : Num(p, "verticalSpeed");

        return new VolantaFlight
        {
            Id = id,
            Source = source,
            AlsoOnNetwork = alsoOn,
            UserName = Str(f, "networkUserName"),
            UserId = Str(f, "networkUserId"),
            Callsign = Str(f, "callsign"),
            FlightNumber = Str(f, "flightNumber"),
            AircraftIcao = Str(f, "aircraftIcao"),
            Registration = Str(f, "aircraftRegistration"),
            Origin = Str(f, "originIcao"),
            Destination = Str(f, "destinationIcao"),
            Route = Str(f, "atcRoute"),
            Simulator = Str(f, "simulatorAbbreviation"),
            Latitude = lat.Value,
            Longitude = lon.Value,
            AltitudeFt = Num(p, "altitude") ?? 0,
            AltitudeAglFt = agl,
            HeadingTrue = Num(p, "headingTrue") ?? 0,
            GroundSpeedKt = groundSpeed,
            VerticalSpeedFpm = vs,
            OnGround = onGround,
            Transponder = Str(p, "transponder"),
            PositionTime = Time(p, "time"),
        };
    }

    /// <summary>Volanta's controller file (online VATSIM and IVAO ATC).</summary>
    public static VolantaFeed<VolantaController>? ParseControllers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetFeedArray(doc.RootElement, out var time, out var data)) return null;
            var list = new List<VolantaController>(data.GetArrayLength());
            foreach (var c in data.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object
                    || !c.TryGetProperty("session", out var s) || s.ValueKind != JsonValueKind.Object) continue;
                string id = Str(s, "id");
                string callsign = Str(s, "callsign");
                if (id.Length == 0) id = callsign;
                if (id.Length == 0) continue;
                double? lat = null, lon = null;
                if (c.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object)
                {
                    lat = Num(loc, "latitude");
                    lon = Num(loc, "longitude");
                }
                string airport = Str(s, "airportIcao");
                if (airport.Length == 0 && loc.ValueKind == JsonValueKind.Object) airport = Str(loc, "airportIcaoCode");
                list.Add(new VolantaController
                {
                    Id = id,
                    Callsign = callsign,
                    Network = ParseSource(Str(s, "network")),
                    Facility = Str(s, "facilityType"),
                    Frequency = Str(s, "radioFrequency"),
                    Atis = Str(s, "atisMessage").Replace("\r\n", "\n").Trim(),
                    UserName = Str(s, "networkUserName"),
                    AirportIcao = airport,
                    Latitude = lat,
                    Longitude = lon,
                    OnlineSince = Time(s, "firstSeen"),
                });
            }
            return new VolantaFeed<VolantaController>(time, list);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Reads one SignalR frame from Volanta's hub: Volanta's own position report ("position-update"), its live
    /// status for the pilot's flight ("flight-live-status-update"), and friend changes ("friend-added",
    /// "friend-removed", "rich-status-update"). A frame may hold several messages, or none of interest
    /// (keep-alives); the latest position and flight win, friend changes are kept in order.
    /// </summary>
    public static VolantaHubUpdate ParseHubFrame(string payload)
    {
        VolantaOwnPosition? position = null;
        VolantaOwnFlight? flight = null;
        var added = new List<VolantaFriend>();
        var removed = new List<string>();
        var statuses = new List<(string, VolantaFriendStatus)>();
        if (string.IsNullOrEmpty(payload)) return new VolantaHubUpdate();
        foreach (string message in payload.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("arguments", out var args) || args.ValueKind != JsonValueKind.Array
                    || args.GetArrayLength() == 0) continue;
                var arg = args[0];
                string target = Str(root, "target");
                if (target == "friend-removed")
                {
                    string gone = arg.ValueKind == JsonValueKind.String ? (arg.GetString() ?? "").Trim()
                        : arg.ValueKind == JsonValueKind.Object ? Str(arg, "friendId") : "";
                    if (gone.Length > 0) removed.Add(gone);
                    continue;
                }
                if (arg.ValueKind != JsonValueKind.Object) continue;
                switch (target)
                {
                    case "position-update":
                        double? lat = Num(arg, "latitude"), lon = Num(arg, "longitude");
                        if (lat is null || lon is null) break;
                        position = new VolantaOwnPosition(lat.Value, lon.Value, Num(arg, "altitude") ?? 0,
                            Num(arg, "headingTrue") ?? 0, Num(arg, "groundSpeed") ?? 0, Bool(arg, "onGround"),
                            Time(arg, "time") ?? DateTimeOffset.UtcNow);
                        break;
                    case "flight-live-status-update":
                        string id = Str(arg, "flightId");
                        if (id.Length == 0) break;
                        flight = new VolantaOwnFlight(id, Str(arg, "callsign"), Str(arg, "aircraftIcao"),
                            Str(arg, "originIcao"), Str(arg, "destinationIcao"));
                        break;
                    case "friend-added":
                        // Volanta's own handler stores the added friend as a full friend.
                        if (ParseFriend(arg, requireFriendState: false) is { } friend) added.Add(friend);
                        break;
                    case "rich-status-update":
                        string who = Str(arg, "friendId");
                        if (who.Length > 0 && arg.TryGetProperty("richStatus", out var rs) && ParseFriendStatus(rs) is { } status)
                            statuses.Add((who, status));
                        break;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                /* a keep-alive or a frame cut short: nothing to take from it */
            }
        }
        return new VolantaHubUpdate
        {
            Position = position, Flight = flight, FriendsAdded = added, FriendsRemoved = removed, FriendStatuses = statuses,
        };
    }

    /// <summary>
    /// Volanta's friends list, the answer to the request Volanta itself makes once when the pilot logs in.
    /// Only accepted friendships are kept (Volanta's state "Friend"; a pending request or a block is not a
    /// friend). Null when the text is not that list.
    /// </summary>
    public static IReadOnlyList<VolantaFriend>? ParseFriends(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<VolantaFriend>();
            foreach (var f in doc.RootElement.EnumerateArray())
                if (ParseFriend(f, requireFriendState: true) is { } friend) list.Add(friend);
            return list;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The logged-in pilot's active flight, the answer to the request Volanta's page makes as soon as it is logged in
    /// (<c>/api/v1/Flights/Active</c>): the flight object itself, whose <c>id</c> Volanta logs as "Found active
    /// flight {id}" and is the id of the pilot's entry in the flight file. Only the id is needed (VOL-4); the
    /// callsign, aircraft and airports are taken when named as in Volanta's other files. Null when the text is not
    /// a flight (no active flight).
    /// </summary>
    public static VolantaOwnFlight? ParseActiveFlight(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string id = Str(root, "id");
            if (id.Length == 0) id = Str(root, "flightId");
            if (id.Length == 0) return null;
            return new VolantaOwnFlight(id, Str(root, "callsign"), Str(root, "aircraftIcao"),
                Str(root, "originIcao"), Str(root, "destinationIcao"));
        }
        catch (JsonException) { return null; }
    }

    private static VolantaFriend? ParseFriend(JsonElement f, bool requireFriendState)
    {
        if (f.ValueKind != JsonValueKind.Object) return null;
        string id = Str(f, "friendId");
        if (id.Length == 0) return null;
        string state = Str(f, "state");
        if (requireFriendState && !state.Equals("Friend", StringComparison.OrdinalIgnoreCase)) return null;
        var status = f.TryGetProperty("richStatus", out var rs) ? ParseFriendStatus(rs) : null;
        return new VolantaFriend(id, Str(f, "userName"), status);
    }

    private static VolantaFriendStatus? ParseFriendStatus(JsonElement rs)
    {
        if (rs.ValueKind != JsonValueKind.Object) return null;
        return new VolantaFriendStatus
        {
            State = Str(rs, "state"),
            Value = Str(rs, "value"),
            CountryName = Str(rs, "countryName"),
            OceanName = Str(rs, "oceanName"),
            NearestAirport = Str(rs, "nearestAirport"),
            DepartureAirport = Str(rs, "departureAirport"),
            ArrivalAirport = Str(rs, "arrivalAirport"),
            // Volanta sends false, not a name, when there is no gate.
            GateName = Str(rs, "gateName"),
            Altitude = Num(rs, "altitude") is double a && a > 0 ? a : null,
        };
    }

    public static VolantaSource ParseSource(string network) => network.Trim().ToLowerInvariant() switch
    {
        "volanta" => VolantaSource.Volanta,
        "vatsim" => VolantaSource.Vatsim,
        "ivao" => VolantaSource.Ivao,
        "pilotedge" => VolantaSource.PilotEdge,
        "apoc" => VolantaSource.Apoc,
        _ => VolantaSource.Other,
    };

    /// <summary>The range <see cref="DateTimeOffset.FromUnixTimeSeconds"/> accepts: a stamp outside it (one in
    /// milliseconds, say) would throw past the parser's JSON catch and drop the link's page session on every
    /// file, so it is left as "now" instead.</summary>
    private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    private static bool TryGetFeedArray(JsonElement root, out DateTimeOffset time, out JsonElement data)
    {
        time = DateTimeOffset.UtcNow;
        data = default;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Array) return false;
        if (root.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out long unix)
            && unix >= MinUnixSeconds && unix <= MaxUnixSeconds)
            time = DateTimeOffset.FromUnixTimeSeconds(unix);
        return true;
    }

    private static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => (v.GetString() ?? "").Trim(),
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }

    private static double? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d)) return d;
        // A number Volanta sent as text: another program's output (CORE-17), never the PC's culture.
        if (v.ValueKind == JsonValueKind.String && NumberParsing.TryParseExternal(v.GetString(), out d)) return d;
        return null;
    }

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Time(JsonElement e, string name)
    {
        string s = Str(e, name);
        if (s.Length == 0) return null;
        // Volanta writes UTC, sometimes without the Z (the controller file's firstSeen).
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }
}
