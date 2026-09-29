namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Decides whether an entry from a traffic sweep (<c>RequestDataOnSimObjectType</c>) is the
/// pilot's OWN aircraft, which the AIRCRAFT object type always includes.
///
/// <para><c>SIMCONNECT_OBJECT_ID_USER</c> (0) is only an alias for a REQUEST — a sweep reports the
/// user aircraft under its real object id (measured live: 524288), so the old <c>dwObjectID == 0</c> test never
/// matched and the callsign comparison was the only guard. That guard fails whenever the aircraft's
/// ATC ID is empty or differs from the one read at load: a FlyByWire A320 was announced to its own
/// pilot as "Stop, Fly By Wire A320 very close, ahead, 0 feet." (live 2026-09-29).</para>
///
/// <para>The real id is learned from the answers to our own requests on the user aircraft
/// (<see cref="Observe"/>), every one of which carries it; the callsign comparison stays as a
/// second guard for the moments before an id has been learned.</para>
/// </summary>
public sealed class OwnAircraftFilter
{
    /// <summary>The user aircraft's real object id, or 0 while it is not yet known.</summary>
    public uint UserObjectId { get; private set; }

    /// <summary>
    /// Records the object id carried by an answer to a request made on the user aircraft.
    /// Returns true when the known id changed (first learned, or a reload handed out a new one).
    /// </summary>
    public bool Observe(uint objectIdFromUserRequest)
    {
        if (objectIdFromUserRequest == 0 || objectIdFromUserRequest == UserObjectId) return false;
        UserObjectId = objectIdFromUserRequest;
        return true;
    }

    /// <summary>Forgets the id — the next connection may hand out a different one.</summary>
    public void Reset() => UserObjectId = 0;

    /// <summary>True when a sweep entry is the pilot's own aircraft and must not be treated as traffic.</summary>
    public bool IsOwnAircraft(uint entryObjectId, string? entryAtcId, string? ownAtcId) =>
        IsOwnAircraft(entryObjectId, UserObjectId, entryAtcId, ownAtcId);

    /// <summary>
    /// Pure form of <see cref="IsOwnAircraft(uint, string?, string?)"/>. An empty callsign on either
    /// side never matches: two aircraft that both lack one are not therefore the same aircraft.
    /// </summary>
    public static bool IsOwnAircraft(uint entryObjectId, uint userObjectId, string? entryAtcId, string? ownAtcId)
    {
        if (entryObjectId == 0) return true;
        if (userObjectId != 0 && entryObjectId == userObjectId) return true;

        string entry = entryAtcId?.Trim() ?? "";
        string own = ownAtcId?.Trim() ?? "";
        return entry.Length > 0 && own.Length > 0 &&
               string.Equals(entry, own, StringComparison.OrdinalIgnoreCase);
    }
}
