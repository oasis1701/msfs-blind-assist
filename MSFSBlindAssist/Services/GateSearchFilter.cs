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

    // Stand-TYPE qualifier words that appear in online stand names ("Ramp H2", "Gate 5",
    // "Stand 12 Apron") but are NOT part of the gate IDENTITY. Stripped before comparing an
    // online name to a navdata stand so e.g. "Ramp H2" is recognized as the SAME stand as
    // navdata "H 2" (identity "H2") and not recorded as a redundant alias. Deliberately EXCLUDES
    // "GA" (a real GA-apron concourse designator at some airports) to avoid over-merging.
    // internal so StandId.Parse shares the SAME list — the identity parser and the search/normalizer
    // must agree on what counts as a type word, or e.g. "Ramp 51" parses to a bogus letter "RAMP".
    internal static readonly HashSet<string> StandTypeWords = new(StringComparer.Ordinal)
    {
        "GATE", "RAMP", "STAND", "PARKING", "PARK", "APRON", "DOCK", "HARDSTAND",
        "HANGAR", "TIEDOWN", "TIE", "DOWN", "REMOTE", "CARGO", "MILITARY", "BAY",
        "SPOT", "POSITION", "POS", "DEICE", "DE-ICE",
    };

    /// <summary>
    /// Normalizes a raw online stand NAME (e.g. "Ramp H2", "Gate 5") to its bare gate identity
    /// ("H2", "5") by dropping whitespace-delimited stand-type qualifier words and all spaces, then
    /// uppercasing — mirroring <see cref="NormalizeIdentity"/> for a string with no Number/Suffix
    /// split. Returns "" when nothing but type words remain (e.g. a bare "Ramp"), so such a name is
    /// never recorded as an alias.
    /// </summary>
    public static string NormalizeGateName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var tok in raw.Trim().ToUpperInvariant()
                     .Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (StandTypeWords.Contains(tok)) continue;   // drop a pure type-qualifier word
            sb.Append(tok);
        }
        return sb.ToString();
    }

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
