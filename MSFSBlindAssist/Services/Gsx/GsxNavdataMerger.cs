using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// GSX-authoritative overlay. Each GSX gate is emitted with GSX metadata
/// (size/jetway/VDGS/airline) and a position from GSX (this_parking_pos -> stop
/// position) or, when the profile omits it, from the matching navdata stand.
/// Navdata stands no GSX gate matched are appended (nothing is lost). A gate with
/// no GSX position and no navdata match is dropped (cannot be placed).
/// </summary>
public static class GsxNavdataMerger
{
    public static List<ParkingSpot> Merge(List<ParkingSpot> navdata, List<GsxGate> gsxGates, string icao)
    {
        var byNumber = new Dictionary<int, List<ParkingSpot>>();
        foreach (var s in navdata)
        {
            if (!byNumber.TryGetValue(s.Number, out var bucket)) byNumber[s.Number] = bucket = new();
            bucket.Add(s);
        }

        var usedNav = new HashSet<ParkingSpot>();
        var result = new List<ParkingSpot>();

        foreach (var g in gsxGates)
        {
            ParkingSpot? nav = FindNavMatch(g, byNumber);

            // Position-priority chain (display / teleport / routing position for the spot):
            //   1. GSX this_parking_pos (the actual aircraft-datum parking position),
            //   2. navdata position (also an aircraft-datum location),
            //   3. GSX stop position — LAST resort.
            // The stop position is a VDGS NOSE-STOP reference, not an aircraft datum: using it
            // as the spot's position teleports the aircraft datum AT the nose-stop point —
            // metres deep into the stand, possibly at heading 0 when StopHeading is absent.
            // It must therefore be the final fallback. The true stop is NOT lost: GsxGateMapper
            // copies StopLatitude/StopLongitude/StopHeading onto the spot separately, so docking
            // still drives to the real nose-stop. (Previously this chain put the stop position
            // SECOND, ahead of navdata, which mis-placed stop-position-only gates.)
            double lat, lon, hdg;
            if (g.HasParkingPos)
            { lat = g.Latitude; lon = g.Longitude; hdg = g.Heading; }
            else if (nav != null)
            { lat = nav.Latitude; lon = nav.Longitude; hdg = nav.Heading; }
            else if (g.StopLatitude.HasValue && g.StopLongitude.HasValue)
            { lat = g.StopLatitude.Value; lon = g.StopLongitude.Value; hdg = g.StopHeading ?? nav?.Heading ?? 0; }
            else
            { continue; } // unplaceable -> drop

            var spot = GsxGateMapper.ToParkingSpot(g, icao);
            spot.Latitude = lat; spot.Longitude = lon; spot.Heading = hdg;
            // Borrow navdata's display name when GSX has no concourse letter (e.g. EGLL "P 209").
            if (string.IsNullOrEmpty(spot.Name) && nav != null) spot.Name = nav.Name;

            if (nav != null) usedNav.Add(nav);
            result.Add(spot);
        }

        foreach (var s in navdata)
            if (!usedNav.Contains(s)) result.Add(s); // keep navdata-only stands (Source stays Navdata)

        return result;
    }

    private static ParkingSpot? FindNavMatch(GsxGate g, Dictionary<int, List<ParkingSpot>> byNumber)
    {
        if (!byNumber.TryGetValue(g.Number, out var candidates) || candidates.Count == 0) return null;

        string gsuf = NormSuffix(g.Suffix);
        var bySuffix = candidates.FindAll(c => NormSuffix(c.Suffix) == gsuf);
        // GSX suffix absent / doesn't match any navdata suffix variant — widen to all candidates.
        var pool = bySuffix.Count > 0 ? bySuffix : candidates;

        string gc = NormConcourse(g.Concourse);

        // SAFETY: when the GSX gate HAS a concourse, a navdata candidate may ONLY be borrowed
        // when its normalized concourse matches. Borrowing across concourses ("A12" pulling
        // "B12"'s coords) would route a blind pilot to the WRONG pier with no audible hint —
        // worse than dropping the spot from the list. So the concourse filter is applied to
        // BOTH the pool.Count==1 early return AND the final fallback. When the gate has NO
        // concourse, we keep the previous best-effort behaviour (any same-number candidate).
        if (gc.Length > 0)
        {
            if (pool.Count == 1)
                return NormConcourse(pool[0].Name) == gc ? pool[0] : null;

            var byConc = pool.FindAll(c => NormConcourse(c.Name) == gc);
            return byConc.Count > 0 ? byConc[0] : null; // no concourse match -> drop (don't borrow wrong pier)
        }

        // Only reachable when the GSX gate has NO concourse (gc.Length == 0): every
        // gate WITH a concourse already returned inside the SAFETY block above, so
        // this best-effort first-candidate pick can never borrow across concourses.
        return pool[0];
    }

    private static string NormSuffix(string? s) => (s ?? string.Empty).Trim().ToUpperInvariant();

    // navdata names a gate "GC"/"GG"/"G"/"P"; GSX says "C"/"G"/""/"P". Strip ONE leading
    // 'G' from a multi-letter all-letter navdata name so "GC"->"C", "GG"->"G".
    private static string NormConcourse(string? name)
    {
        string n = (name ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", "");
        if (n.Length > 1 && n[0] == 'G' && IsAllLetters(n)) n = n.Substring(1);
        return n;
    }

    private static bool IsAllLetters(string s)
    {
        foreach (char c in s) if (!char.IsLetter(c)) return false;
        return s.Length > 0;
    }
}
