using MSFSBlindAssist.Database;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services;

public static class CurrentAirport
{
    /// <summary>
    /// Which airport the aircraft is at. The legacy first-4-character rule runs ONLY when the
    /// provider supplied no candidates at all — never as a second opinion on candidates the
    /// resolver considered and declined. Both queries scan the same ±5 NM bounding BOX, but
    /// <see cref="CurrentAirportResolver.Pick"/>'s last pass demands ≤ 5 NM TRUE distance while
    /// the legacy query has no distance filter and orders by summed raw degrees — so falling
    /// through on a null pick handed back exactly the corner-of-the-box airport (5–7.07 NM out)
    /// the resolver had just refused, chosen by the metric this all exists to retire.
    ///
    /// An empty candidate list is therefore the ONLY fallback trigger, and it has two causes that
    /// are both handled correctly: a provider that cannot supply candidates (the interface's
    /// default implementation, a test double, a future provider), where the legacy rule is the
    /// only answer available; and a real provider with genuinely nothing in the box (open ocean),
    /// where the legacy query runs once and also finds nothing. The second costs one wasted query
    /// on a call that was going to answer "no airport" either way.
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
