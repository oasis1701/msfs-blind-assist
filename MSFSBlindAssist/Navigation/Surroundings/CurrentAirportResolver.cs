using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// "Which airport is this aircraft at?" — the airport WITH TAXI PATHS whose navdata box (grown a
/// little) contains it, nearest by TRUE distance. Replaces `GetNearbyAirportICAOs(…)[0]` filtered to
/// 4-character idents, which is ordered by unscaled |Δlat|+|Δlon| to the reference point: measured on
/// fs2024.sqlite it sent 2,371 stands at 212 airports to a neighbour (KSNA 111/201 → heliport 10CL)
/// and could never resolve 2,454 fields with a 3-character ident. Pure.
/// </summary>
public static class CurrentAirportResolver
{
    public const double BoxMarginMetres = 300.0, NearWithTaxiPathsNm = 3.0, AnyKindNm = 5.0;
    private const double MetresPerNm = 1852.0;

    public static string? Pick(IReadOnlyList<AirportCandidate> candidates, double lat, double lon)
    {
        if (candidates == null || candidates.Count == 0) return null;
        var ranked = candidates.Where(c => !string.IsNullOrWhiteSpace(c.Ident))
            .Select(c => (c, Metres: TaxiGeo.HaversineMeters(lat, lon, c.Lat, c.Lon)))
            .OrderBy(x => x.Metres).ToList();

        foreach (var x in ranked)
            if (x.c.NumTaxiPaths > 0 && Contains(x.c, lat, lon)) return x.c.Ident;
        foreach (var x in ranked)
            if (x.c.NumTaxiPaths > 0 && x.Metres <= NearWithTaxiPathsNm * MetresPerNm) return x.c.Ident;
        foreach (var x in ranked)
            if (x.Metres <= AnyKindNm * MetresPerNm) return x.c.Ident;
        return null;
    }

    /// <summary>Assumes LeftLon &lt;= RightLon, as the box SQL feeding it always has: a box
    /// spanning ±180° fails this test and the airport degrades to the distance passes.</summary>
    private static bool Contains(AirportCandidate c, double lat, double lon)
    {
        double dLat = BoxMarginMetres / 111_320.0;
        double dLon = BoxMarginMetres / (111_320.0 * Math.Max(0.05, Math.Cos(lat * Math.PI / 180.0)));
        return lat <= c.TopLat + dLat && lat >= c.BottomLat - dLat && lon >= c.LeftLon - dLon && lon <= c.RightLon + dLon;
    }
}
