using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Returns the gate/parking list for an airport, preferring the GSX profile
/// (accurate) when GSX is available AND a profile matches the ICAO; otherwise
/// falls back to the navdata provider unchanged. Parsed profiles are cached per
/// (path, last-write-time).
/// </summary>
public sealed class GateDataSource
{
    private readonly IAirportDataProvider _navdata;
    private readonly Func<bool> _isGsxAvailable;
    private readonly GsxProfileLocator _locator;
    private readonly Dictionary<string, (string path, DateTime stamp, List<ParkingSpot> spots)> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    public GateDataSource(IAirportDataProvider navdata, Func<bool> isGsxAvailable,
                          GsxProfileLocator? locator = null)
    {
        _navdata = navdata;
        _isGsxAvailable = isGsxAvailable;
        _locator = locator ?? new GsxProfileLocator();
    }

    public GateSource GetActiveSource(string icao)
        => (_isGsxAvailable() && _locator.TryFindProfile(NormalizeIcao(icao), out _))
            ? GateSource.Gsx : GateSource.Navdata;

    public List<ParkingSpot> GetGates(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return new List<ParkingSpot>();
        icao = NormalizeIcao(icao);

        if (_isGsxAvailable() && _locator.TryFindProfile(icao, out string path))
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (_cache.TryGetValue(icao, out var c) && c.path == path && c.stamp == stamp)
                    return c.spots;

                var gsxGates = GsxProfileParser.Parse(path);
                if (gsxGates.Count > 0)
                {
                    // GSX-authoritative overlay: GSX metadata wins; navdata supplies the
                    // base skeleton + positions GSX omits. See GsxNavdataMerger.
                    // Deice areas are GSX-only destinations — exclude them from the
                    // normal gate list so they never appear as taxi/teleport destinations.
                    var normalGates = gsxGates.Where(g => !g.IsDeiceArea).ToList();
                    var spots = GsxNavdataMerger.Merge(_navdata.GetParkingSpots(icao), normalGates, icao)
                                                .Where(s => !s.IsDeiceArea).ToList();
                    _cache[icao] = (path, stamp, spots);
                    return spots;
                }
                // Empty/garbage profile → fall through to navdata.
            }
            catch
            {
                // Any IO/parse failure → navdata fallback (never break the dialog).
            }
        }
        return _navdata.GetParkingSpots(icao);
    }

    /// <summary>
    /// Returns the GSX deice-area parking spots for the airport (IsDeiceArea == true).
    /// These are GSX-only: never merged with navdata and never included in the normal
    /// gate list. Returns an empty list when GSX is unavailable or the airport has no
    /// deice areas defined in its profile.
    /// </summary>
    public List<ParkingSpot> GetDeiceAreas(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return new List<ParkingSpot>();
        icao = NormalizeIcao(icao);

        if (!_isGsxAvailable() || !_locator.TryFindProfile(icao, out string path))
            return new List<ParkingSpot>();

        try
        {
            var gsxGates = GsxProfileParser.Parse(path);
            return GsxGateMapper.ToParkingSpots(gsxGates.Where(g => g.IsDeiceArea), icao);
        }
        catch
        {
            return new List<ParkingSpot>();
        }
    }

    private static string NormalizeIcao(string icao) => icao.Trim().ToUpperInvariant();
}
