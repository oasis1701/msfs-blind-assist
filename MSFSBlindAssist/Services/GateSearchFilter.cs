using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Type-to-filter matching for gate/parking lists. Matches on stand identity:
/// name + number + suffix (e.g. "C18", "C", "18", "1L"), AND on any online alias
/// (e.g. the real ATC gate "B04" aliased onto navdata "B 6") — so the pilot can type
/// EITHER name and find the gate. Concourse filtering falls out of typing the concourse
/// letter. Used identically by both gate UIs (teleport + Taxi Assist).
/// </summary>
public static class GateSearchFilter
{
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    public static string NormalizeIdentity(ParkingSpot s)
        => Normalize((s.Name ?? "") + (s.Number > 0 ? s.Number.ToString() : "") + (s.Suffix ?? ""));

    public static bool Matches(ParkingSpot s, string? query)
    {
        string q = Normalize(query);
        if (q.Length == 0) return true;
        if (NormalizeIdentity(s).Contains(q, StringComparison.Ordinal)) return true;
        // Also match online aliases so typing the real ATC gate number finds the navdata spot.
        if (s.Aliases != null)
            foreach (var a in s.Aliases)
                if (Normalize(a).Contains(q, StringComparison.Ordinal)) return true;
        return false;
    }

    public static List<ParkingSpot> Filter(IEnumerable<ParkingSpot> spots, string? query)
    {
        string q = Normalize(query);
        if (q.Length == 0) return spots.ToList();
        return spots.Where(s => Matches(s, query)).ToList();
    }
}
