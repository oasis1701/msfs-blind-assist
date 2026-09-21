using MSFSBlindAssist.Database;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services;

public static class CurrentAirport
{
    /// <summary>Resolver first; the legacy first-4-character rule only for a provider that cannot
    /// supply candidates (a test double, a future provider).</summary>
    public static string? Resolve(IAirportDataProvider provider, double lat, double lon)
    {
        if (provider is IAirportFacilitiesProvider facilities)
        {
            string? pick = CurrentAirportResolver.Pick(facilities.GetNearbyAirportCandidates(lat, lon, CurrentAirportResolver.AnyKindNm), lat, lon);
            if (pick != null) return pick;
        }
        return provider.GetNearbyAirportICAOs(lat, lon, 5.0).FirstOrDefault(c => c != null && c.Length == 4);
    }
}
