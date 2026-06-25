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

        // Collect accepted candidates with the concourse letter they carry, so a letterless gate
        // can reject an AMBIGUOUS concourse (two different letters competing) below.
        var accepted = new List<(string Canonical, string Letter)>();

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

            accepted.Add((canonical, oid.Letter));
        }

        // Ambiguous-concourse guard (letterless gate only): if two surviving candidates carry
        // DIFFERENT non-empty concourse letters (e.g. "A51" AND "B51" on bare gate 51 — facing piers
        // within the distance backstop), the gate's real concourse is unknown, so adopting either
        // would let the pilot "find" gate 51 by the wrong concourse. Drop the lettered candidates;
        // keep any letterless ones (a MARS suffix like "53A" carries no concourse and is unaffected).
        if (gateLetter.Length == 0)
        {
            int distinctLetters = accepted
                .Where(a => a.Letter.Length > 0)
                .Select(a => a.Letter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (distinctLetters > 1)
                accepted.RemoveAll(a => a.Letter.Length > 0);
        }

        foreach (var a in accepted)
            result.Add(a.Canonical);
        return result;
    }
}
