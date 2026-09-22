using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// "Which airport is this aircraft at?" Four passes over the candidates, each taking the nearest by
/// TRUE distance to the reference point: (1) an airport WITH TAXI PATHS whose navdata box, grown
/// <see cref="BoxMarginMetres"/>, contains the aircraft; (2) an airport of ANY kind whose grown box
/// contains it — a strip with runways but no taxi paths; (3) the nearest airport with taxi paths
/// within <see cref="NearWithTaxiPathsNm"/>; (4) the nearest of any kind within
/// <see cref="AnyKindNm"/>. Idents of any length. Replaces `GetNearbyAirportICAOs(…)[0]` filtered to
/// 4-character idents, which is ordered by unscaled |Δlat|+|Δlon| to the reference point: measured
/// on fs2024.sqlite it sent 2,371 stands at 212 airports to a neighbour (KSNA 111/201 → heliport
/// 10CL) and could never resolve 2,454 fields with a 3-character ident. Pure.
///
/// <para>Pass 2's place is load-bearing both ways. It comes AFTER pass 1 so an airport's own box
/// still beats a heliport or strip box overlapping it: 41 of KSNA's stands lie inside heliport
/// 10CL's grown box, nearer its reference point than KSNA's. It comes BEFORE pass 3 because without
/// it a strip with no taxi paths went to a taxi-path neighbour: 1,552 strips the old rule named at
/// their own reference point — 1,354 of them by the 3 NM pass — and 3,306 runway ends
/// (`runway_end` rows) at 1,790 strips were sent elsewhere; 8TX2 Freeman Ranch went to KECU 4.4 km
/// away, so Where Am I on 8TX2's runway said "Not on a known taxiway or ramp at KECU." Replayed on
/// fs2024, pass 2 names 1,353 of those reference points and 2,917 of those runway ends after their
/// own strip, and changes the answer at none of the 302,142 stands or the 56,396 runway ends
/// (`runway_end` rows) of airports with taxi paths.</para>
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

        foreach (var x in ranked)   // 1: an airport with taxi paths whose box contains the aircraft
            if (x.c.NumTaxiPaths > 0 && Contains(x.c, lat, lon)) return x.c.Ident;
        foreach (var x in ranked)   // 2: ANY airport whose box contains it — a strip with no taxi paths
            if (Contains(x.c, lat, lon)) return x.c.Ident;
        foreach (var x in ranked)   // 3: the nearest with taxi paths within 3 NM
            if (x.c.NumTaxiPaths > 0 && x.Metres <= NearWithTaxiPathsNm * MetresPerNm) return x.c.Ident;
        foreach (var x in ranked)   // 4: the nearest of any kind within 5 NM
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
