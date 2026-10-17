namespace MSFSBlindAssist.Services.Volanta;

/// <summary>Where a flight on Volanta's map comes from. <see cref="Volanta"/> is a Volanta user (Volanta's own
/// flight file); the rest come from Volanta's merged network file. A Volanta user who also flies on a network is
/// still <see cref="Volanta"/>, with the network in <see cref="VolantaFlight.AlsoOnNetwork"/>: Volanta drops that
/// pilot from the network file so they are drawn once.</summary>
public enum VolantaSource { Volanta, Vatsim, Ivao, PilotEdge, Apoc, Other }

/// <summary>One aircraft on Volanta's live map, as Volanta's own page downloaded it. Units are Volanta's: feet,
/// knots, feet per minute, degrees true.</summary>
public sealed record VolantaFlight
{
    public required string Id { get; init; }
    public VolantaSource Source { get; init; }
    /// <summary>The network a Volanta user is also connected to ("Vatsim", "Ivao"), else null.</summary>
    public VolantaSource? AlsoOnNetwork { get; init; }
    public string UserName { get; init; } = "";
    /// <summary>The pilot's id on Volanta or the network; for a Volanta user, the id Volanta logs them in with.</summary>
    public string UserId { get; init; } = "";
    public string Callsign { get; init; } = "";
    public string FlightNumber { get; init; } = "";
    public string AircraftIcao { get; init; } = "";
    public string Registration { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Destination { get; init; } = "";
    public string Route { get; init; } = "";
    /// <summary>Volanta's simulator abbreviation ("msfs24", "xp12"); empty for network pilots.</summary>
    public string Simulator { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double AltitudeFt { get; init; }
    /// <summary>Height above ground, or null when the feed has none (network pilots always report 0).</summary>
    public double? AltitudeAglFt { get; init; }
    public double HeadingTrue { get; init; }
    public double GroundSpeedKt { get; init; }
    /// <summary>Vertical speed, or null when the feed has none (network pilots always report 0).</summary>
    public double? VerticalSpeedFpm { get; init; }
    /// <summary>On the ground: Volanta's own flag for a Volanta user, inferred from ground speed for a network
    /// pilot, whose flag is always false (<see cref="VolantaFeedParser.NetworkGroundSpeedKt"/>).</summary>
    public bool OnGround { get; init; }
    public string Transponder { get; init; } = "";
    /// <summary>When this position was reported; Volanta users report every 10 seconds to 2 minutes.</summary>
    public DateTimeOffset? PositionTime { get; init; }
}

/// <summary>One online ATC position from Volanta's controller file.</summary>
public sealed record VolantaController
{
    public required string Id { get; init; }
    public string Callsign { get; init; } = "";
    public VolantaSource Network { get; init; }
    /// <summary>Volanta's facility name: "Ground", "Tower", "Approach", "Center", "Atis" and the like.</summary>
    public string Facility { get; init; } = "";
    public string Frequency { get; init; } = "";
    public string Atis { get; init; } = "";
    public string UserName { get; init; } = "";
    public string AirportIcao { get; init; } = "";
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public DateTimeOffset? OnlineSince { get; init; }
}

/// <summary>A parsed Volanta file: the time Volanta stamped on it and its entries.</summary>
public sealed record VolantaFeed<T>(DateTimeOffset Time, IReadOnlyList<T> Items);

/// <summary>Where the pilot is, from the sim or from Volanta's own position report.</summary>
public readonly record struct VolantaOwnPosition(
    double Latitude, double Longitude, double AltitudeFt, double HeadingTrue, double GroundSpeedKt,
    bool OnGround, DateTimeOffset Time);

/// <summary>The pilot's own Volanta flight, from Volanta's live status message: its id is the id of the pilot's
/// own entry in Volanta's flight file.</summary>
public sealed record VolantaOwnFlight(string FlightId, string Callsign, string AircraftIcao, string Origin, string Destination);

/// <summary>A friend's live status as Volanta keeps it ("rich status"). <see cref="State"/> is Volanta's word:
/// Offline, Online, AtAirport, TaxiingToRunway, ClimbingToAltitude, CruisingOverCountry, Cruising, Descending,
/// TaxiingToGate (measured 2026-10-15); <see cref="Value"/> is the place or level that goes with it.</summary>
public sealed record VolantaFriendStatus
{
    public string State { get; init; } = "";
    public string Value { get; init; } = "";
    public string CountryName { get; init; } = "";
    public string OceanName { get; init; } = "";
    public string NearestAirport { get; init; } = "";
    public string DepartureAirport { get; init; } = "";
    public string ArrivalAirport { get; init; } = "";
    public string GateName { get; init; } = "";
    public double? Altitude { get; init; }
    public bool IsOffline => State.Length == 0 || State.Equals("Offline", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One of the pilot's Volanta friends. <see cref="FriendId"/> is the friend's Volanta user id, the user
/// id on their entry in Volanta's flight file when they fly. <see cref="Status"/> is null when unknown: a friend
/// Volanta listed with no status, and has not reported one for since.</summary>
public sealed record VolantaFriend(string FriendId, string UserName, VolantaFriendStatus? Status);

/// <summary>What one hub frame told: Volanta's own position report and live status, and friend changes.</summary>
public sealed record VolantaHubUpdate
{
    public VolantaOwnPosition? Position { get; init; }
    public VolantaOwnFlight? Flight { get; init; }
    public IReadOnlyList<VolantaFriend> FriendsAdded { get; init; } = Array.Empty<VolantaFriend>();
    public IReadOnlyList<string> FriendsRemoved { get; init; } = Array.Empty<string>();
    /// <summary>Status changes by user id; the pilot's own id can be among them and is ignored by the list.</summary>
    public IReadOnlyList<(string FriendId, VolantaFriendStatus Status)> FriendStatuses { get; init; }
        = Array.Empty<(string, VolantaFriendStatus)>();
    public bool Any => Position != null || Flight != null || FriendsAdded.Count > 0 || FriendsRemoved.Count > 0
        || FriendStatuses.Count > 0;
}
