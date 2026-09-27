using MSFSBlindAssist.Database;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services;

public static class CurrentAirport
{
    /// <summary>
    /// Which airport the aircraft is at (<see cref="CurrentAirportResolver.Pick"/>). The legacy
    /// nearest-4-character-ident rule runs only when the provider supplied no candidates at all,
    /// never as a second opinion on candidates Pick declined: it has no true-distance filter and
    /// would hand back the corner-of-the-box airport Pick had just refused.
    /// </summary>
    public static string? Resolve(IAirportDataProvider provider, double lat, double lon)
    {
        if (provider is IAirportFacilitiesProvider facilities)
        {
            var candidates = facilities.GetNearbyAirportCandidates(lat, lon, CurrentAirportResolver.AnyKindNm);
            if (candidates.Count > 0) return CurrentAirportResolver.Pick(candidates, lat, lon);
        }
        return provider.GetNearbyAirportICAOs(lat, lon, 5.0).FirstOrDefault(c => c != null && c.Length == 4);
    }
}
