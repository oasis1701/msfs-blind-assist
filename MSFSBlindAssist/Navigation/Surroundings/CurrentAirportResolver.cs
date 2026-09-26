using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// "Which airport is this aircraft at?" Four passes, each taking the nearest by true distance:
/// (1) an airport with taxi paths whose box, grown <see cref="BoxMarginMetres"/>, contains the
/// aircraft; (2) any airport whose grown box contains it; (3) the nearest with taxi paths within
/// <see cref="NearWithTaxiPathsNm"/>; (4) the nearest of any kind within <see cref="AnyKindNm"/>.
/// Idents of any length. Replaces the old |Δlat|+|Δlon| nearest-4-character rule, which sent 2,371
/// fs2024 stands to a neighbour (KSNA → heliport 10CL) and never resolved 3-character idents.
/// <para>Pass 2 sits after pass 1 so an airport's own box beats an overlapping heliport's (41 KSNA
/// stands lie in 10CL's box), and before pass 3 so a strip with no taxi paths is not handed to a
/// taxi-path neighbour (8TX2 went to KECU 4.4 km away). Replayed on fs2024 it changes no stand's
/// answer.</para>
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

    /// <summary>Assumes LeftLon &lt;= RightLon, as the box SQL always has; a box spanning ±180°
    /// degrades to the distance passes.</summary>
    private static bool Contains(AirportCandidate c, double lat, double lon)
        => GrownBox.Of(c.TopLat, c.BottomLat, c.LeftLon, c.RightLon, BoxMarginMetres).Contains(lat, lon);
}
