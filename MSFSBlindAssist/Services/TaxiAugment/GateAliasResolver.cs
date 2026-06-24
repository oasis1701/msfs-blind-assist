using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Pure, idempotent resolver: given an authoritative gate (GSX/sqlite) and the online stands,
/// returns the searchable aliases the online layer contributes — number-matched, letter-agreeing,
/// extra-info-only. NEVER returns a position and NEVER affects which gates are selectable.
/// </summary>
public static class GateAliasResolver
{
    /// <param name="maxMeters">Distance sanity backstop; a same-number stand farther than this
    /// is treated as a data error and skipped. Pass 0 to disable (identity-only).</param>
    public static List<string> ResolveAliases(
        ParkingSpot gate,
        IReadOnlyList<(string Name, double Lat, double Lon)> onlineStands,
        double maxMeters = 150.0)
    {
        var result = new List<string>();
        if (gate == null || onlineStands == null || onlineStands.Count == 0) return result;
        if (gate.Number <= 0) return result; // no numeric identity → never match (safety)

        string gateLetter = MSFSBlindAssist.Services.StandId.Parse(gate.Name).Letter; // navdata letter ("N"/…) or ""
        string gateSuffix = gate.Suffix ?? "";
        string gateCanonical = (gateLetter + gate.Number + gateSuffix).ToUpperInvariant();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gateCanonical };

        foreach (var (name, lat, lon) in onlineStands)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var oid = MSFSBlindAssist.Services.StandId.Parse(name);
            if (!oid.HasNumber || oid.Number != gate.Number) continue; // number must match

            // Letter agreement: a gate WITH a letter requires the same letter ("N3" never matches "S3"
            // or bare "3"); a gate WITHOUT a letter may take an online concourse prefix ("A51" on 51).
            if (gateLetter.Length > 0 && oid.Letter != gateLetter) continue;

            if (maxMeters > 0 &&
                TaxiGeo.HaversineMeters(gate.Latitude, gate.Longitude, lat, lon) > maxMeters) continue;

            string canonical = oid.Canonical; // "A51", "55A", …
            if (string.Equals(canonical, gateCanonical, StringComparison.OrdinalIgnoreCase)) continue; // restatement
            if (!seen.Add(canonical)) continue; // dedup

            result.Add(canonical);
        }
        return result;
    }
}
