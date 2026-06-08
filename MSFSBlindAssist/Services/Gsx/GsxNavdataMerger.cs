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

            double lat, lon, hdg;
            if (g.HasParkingPos)
            { lat = g.Latitude; lon = g.Longitude; hdg = g.Heading; }
            else if (g.StopLatitude.HasValue && g.StopLongitude.HasValue)
            { lat = g.StopLatitude.Value; lon = g.StopLongitude.Value; hdg = g.StopHeading ?? nav?.Heading ?? 0; }
            else if (nav != null)
            { lat = nav.Latitude; lon = nav.Longitude; hdg = nav.Heading; }
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
        if (pool.Count == 1) return pool[0];

        string gc = NormConcourse(g.Concourse);
        if (gc.Length > 0)
        {
            var byConc = pool.FindAll(c => NormConcourse(c.Name) == gc);
            if (byConc.Count > 0) return byConc[0];
        }
        return pool[0]; // best-effort
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
