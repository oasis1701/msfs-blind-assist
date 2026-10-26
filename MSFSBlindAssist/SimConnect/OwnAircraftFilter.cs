using MSFSBlindAssist.Navigation;

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
/// (<see cref="Observe"/>), every one of which carries it. The callsign comparison applies ONLY
/// until an id has been learned: once the id is known it can only drop real traffic, because AI
/// and multiplayer aircraft share placeholder callsigns ("ASXGSA" on a 737, a 717 and an MD80 in
/// one live session), and a dropped aircraft never earns its "Stop".</para>
///
/// <para>The id is forgotten on every disconnect AND on every aircraft or flight load
/// (<see cref="Reset"/>): a reload can hand the user aircraft a new id, and a stale one both lets the
/// own aircraft through (the callsign fallback is off while any id is held) and hides whichever AI
/// aircraft is given the old id. Until the next answer re-learns it, the callsign decides again.</para>
///
/// <para>Backstop, independent of the id: a sweep entry on the ground within
/// <see cref="SamePositionMetres"/> of the own aircraft's last known position, itself on the ground,
/// is the own aircraft (<see cref="SharesOwnPosition"/>). Two real aircraft's reference points
/// cannot be that close, and every gap the id leaves open (before it is learned with no callsign, a
/// sweep landing between a load and the re-learn) reports the own aircraft at exactly that spot —
/// the live symptom was "0 feet".</para>
///
/// <para>Known regression, unmeasured: if MSFS 2024's walkaround makes the user object the pilot's
/// avatar, the learned id follows it, and the parked aircraft may be reported as traffic until the
/// pilot is back aboard, when the next answer re-learns the aircraft's id. The old code filtered
/// that aircraft whenever its ATC ID was set; this one does not, because the callsign is ignored
/// while an id is held, and the position backstop does not reach it once the avatar walks away.
/// Restoring the callsign match would bring back the dropped-traffic failure above, so it stays
/// off until a walkaround is measured.</para>
///
/// <para>Also dropped, by type: the invisible helper objects an add-on spawns in the "airplane"
/// category, which every AIRCRAFT sweep returns as if they were aircraft
/// (<see cref="IsAddOnHelperObject"/>).</para>
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

    /// <summary>
    /// Within this distance of the own aircraft's last known position, a sweep entry on the ground is
    /// the own aircraft. Far below any two real aircraft's datum separation, and small enough that a
    /// position a second or two stale during a taxi simply misses (the id is the primary guard).
    /// </summary>
    public const double SamePositionMetres = 5.0;

    /// <summary>Forgets the id — the next connection, or the next aircraft or flight load, may hand out a different one.</summary>
    public void Reset() => UserObjectId = 0;

    /// <summary>True when a sweep entry is the pilot's own aircraft and must not be treated as traffic.</summary>
    public bool IsOwnAircraft(uint entryObjectId, string? entryAtcId, string? ownAtcId) =>
        IsOwnAircraft(entryObjectId, UserObjectId, entryAtcId, ownAtcId);

    /// <summary>
    /// Pure form of <see cref="IsOwnAircraft(uint, string?, string?)"/>. Once the user object id is
    /// known it alone decides; before that, a matching callsign does. An empty callsign on either
    /// side never matches: two aircraft that both lack one are not therefore the same aircraft.
    /// </summary>
    public static bool IsOwnAircraft(uint entryObjectId, uint userObjectId, string? entryAtcId, string? ownAtcId)
    {
        if (entryObjectId == 0) return true;
        if (userObjectId != 0) return entryObjectId == userObjectId;

        string entry = entryAtcId?.Trim() ?? "";
        string own = ownAtcId?.Trim() ?? "";
        return entry.Length > 0 && own.Length > 0 &&
               string.Equals(entry, own, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when a sweep entry on the ground sits within <see cref="SamePositionMetres"/> of the own
    /// aircraft's last known position, itself on the ground — the own aircraft, whatever the id says.
    /// No own position yet, or either side airborne, never matches.
    /// </summary>
    public static bool SharesOwnPosition(double entryLat, double entryLon, bool entryOnGround,
                                         double? ownLat, double? ownLon, bool ownOnGround)
    {
        if (!entryOnGround || !ownOnGround || ownLat is not double lat || ownLon is not double lon) return false;
        if (!double.IsFinite(entryLat) || !double.IsFinite(entryLon) || !double.IsFinite(lat) || !double.IsFinite(lon)) return false;
        if (lat == 0 && lon == 0) return false;   // an unset position, never a real stand
        return TaxiGraph.FastDistanceMeters(entryLat, entryLon, lat, lon) <= SamePositionMetres;
    }

    /// <summary>
    /// ATC TYPE / ATC MODEL values of add-on helper objects. The Fenix update of 2026-10-22 ships
    /// <c>SimObjects/Misc/FNX_DUMMY</c> (category "airplane", atc_type and atc_model "Fenix Dummy Object",
    /// icao_type_designator "FNXDUMMY") and spawns it at its own gear as nose-FNX, left-FNX and
    /// right-FNX, each a separate object whose id changes within a second, so neither the id nor
    /// the position filter reaches it: "Stop, nose-FNX, Fenix very close, ahead, 25 feet." all
    /// through the taxi (live 2026-10-22/23).
    /// </summary>
    private static readonly HashSet<string> HelperObjectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fenix Dummy Object", "FNXDUMMY",
    };

    /// <summary>
    /// True when a sweep entry is an add-on's invisible helper object rather than an aircraft, by its
    /// ATC TYPE or ATC MODEL. Whoever spawned it, it is never traffic.
    /// </summary>
    public static bool IsAddOnHelperObject(string? atcType, string? atcModel) =>
        HelperObjectTypes.Contains(atcType?.Trim() ?? "") || HelperObjectTypes.Contains(atcModel?.Trim() ?? "");
}
