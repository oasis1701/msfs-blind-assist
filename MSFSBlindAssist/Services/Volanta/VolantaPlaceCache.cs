using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services.Volanta;

/// <summary>
/// Each aircraft's place for the traffic list, by flight id: the nearest town's words ("Pike Lake, Wisconsin,
/// United States"), or the nearest major city, whose distance and direction the window works out again at every
/// update. A place changes slowly, so an aircraft is looked up again only once it has moved
/// <see cref="LookUpAgainAfterNm"/> from where it was looked up: at 450 knots that is every 16 seconds, and a
/// parked aircraft is looked up once. Thread-safe: the window reads it on the UI thread while one burst of
/// lookups at a time (<see cref="TryBeginLookup"/>) fills it in the background. A lookup that found nothing is
/// kept too, so it is not retried until the aircraft moves.
/// </summary>
public sealed class VolantaPlaceCache<TPlace> where TPlace : class
{
    public const double LookUpAgainAfterNm = 2.0;

    private readonly object _gate = new();
    private readonly Dictionary<string, (double Lat, double Lon, TPlace? Place)> _byId = new(StringComparer.Ordinal);
    private int _lookupRunning;

    public TPlace? PlaceOf(string flightId)
    {
        lock (_gate) return _byId.TryGetValue(flightId, out var hit) ? hit.Place : null;
    }

    /// <summary>The entries due a lookup, in the order given (the window gives nearest first), at most
    /// <paramref name="max"/>: never looked up, or moved more than <see cref="LookUpAgainAfterNm"/> since.</summary>
    public IReadOnlyList<VolantaTrafficEntry> Due(IEnumerable<VolantaTrafficEntry> entries, int max)
    {
        var due = new List<VolantaTrafficEntry>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var e in entries)
            {
                if (due.Count >= max) break;
                if (!taken.Add(e.Flight.Id)) continue;
                if (_byId.TryGetValue(e.Flight.Id, out var hit)
                    && NavigationCalculator.CalculateDistance(hit.Lat, hit.Lon, e.Latitude, e.Longitude) <= LookUpAgainAfterNm)
                    continue;
                due.Add(e);
            }
        }
        return due;
    }

    public void Store(string flightId, double lat, double lon, TPlace? place)
    {
        lock (_gate) _byId[flightId] = (lat, lon, place);
    }

    /// <summary>Forgets every flight not among <paramref name="flightIds"/>: those that left Volanta's map.</summary>
    public void KeepOnly(IEnumerable<string> flightIds)
    {
        var keep = new HashSet<string>(flightIds, StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (string id in _byId.Keys.Where(id => !keep.Contains(id)).ToList()) _byId.Remove(id);
        }
    }

    public int Count { get { lock (_gate) return _byId.Count; } }

    /// <summary>Claims this cache for one burst of lookups; false while another burst fills it.</summary>
    public bool TryBeginLookup() => Interlocked.CompareExchange(ref _lookupRunning, 1, 0) == 0;

    public void EndLookup() => Interlocked.Exchange(ref _lookupRunning, 0);
}
