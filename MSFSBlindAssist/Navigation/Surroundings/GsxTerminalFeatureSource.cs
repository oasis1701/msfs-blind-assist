using MSFSBlindAssist.Database.Models;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// GSX's uiTerminalName per stand, grouped into features, from the selectable list (GetNamedSpots
/// carries no TerminalName). The header is a profile author's free-text section title, so its kind
/// comes from its words and its stands (<see cref="KindOf"/>) and its name drops the author's notes
/// (<see cref="PlaceName"/>).
/// </summary>
public static class GsxTerminalFeatureSource
{
    /// <summary>Plain section dividers, not names. Exact match only: "Terminal 5 - Remote" survives.</summary>
    private static readonly HashSet<string> BareHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "Parking", "Ramp", "Gates", "Gate", "Stands", "Stand", "Apron" };
    private const double StandMajority = 0.60;

    public static List<AirportFeature> Read(IReadOnlyList<ParkingSpot> selectableGates)
    {
        var result = new List<AirportFeature>();
        // Grouped by the header as written; the catalog's same-name rule decides whether two
        // sections that clean to one name are one place.
        var groups = selectableGates
            .Where(s => s.Source == GateSource.Gsx)
            .Select(s => (Spot: s, Header: ParkingSpot.SpeakableTerminalName(s.TerminalName).Trim()))
            .Where(x => x.Header.Length > 0)
            .GroupBy(x => x.Header, x => x.Spot, StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var members = g.ToList();
            if (members.Count < 2) continue;
            // Nothing but notes, or only a category word once they are gone ("Ramp (TD)"), is no place.
            string name = PlaceName(g.Key);
            if (name.Length == 0 || BareHeaders.Contains(name)) continue;
            var pts = members.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList();
            var c = SurroundingsGeometry.Centroid(pts);
            // The kind reads the header with its notes: "Ramp 5 (Cargo)" is still cargo.
            result.Add(new AirportFeature { Kind = KindOf(g.Key, members), Name = name, Lat = c.Lat, Lon = c.Lon, Members = pts, Source = FeatureSource.Gsx });
        }
        return result;
    }

    // "(T1-T21)", "(TD)": a profile author's note inside a header, never part of what the place is called.
    private static readonly Regex Parenthesised = new(@"\([^()]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.CultureInvariant);
    private static readonly char[] DanglingSeparators = { ' ', '-', '–', '—', ',', ';', ':', '/', '|' };

    /// <summary>
    /// A section header as a place name: every parenthesised note removed (they can hide a size-hint
    /// tail from <see cref="ParkingSpot.SpeakableTerminalName"/>, which only looks at the end), then
    /// that tail, then dangling separators. KATL's "Concourse T (T1-T21)" is "Concourse T", which is
    /// what lets it merge with OSM's and the scenery's pier. Empty when the header was only a note.
    /// The gate LABEL keeps the notes, because a label must stay unique.
    /// </summary>
    internal static string PlaceName(string? header)
    {
        string s = header ?? "", before;
        do { before = s; s = Parenthesised.Replace(s, " "); } while (s != before);
        return ParkingSpot.SpeakableTerminalName(Spaces.Replace(s, " ")).Trim(DanglingSeparators);
    }

    /// <summary>
    /// The header's words decide first, through <see cref="FeatureLexicon.NamedKind"/>, so a header
    /// names the same kind as the same words do in the other tiers. Otherwise a 60 % majority of
    /// civil cargo stands makes it Cargo, of GA-ramp or military stands an Apron, else Terminal.
    /// </summary>
    private static FeatureKind KindOf(string header, List<ParkingSpot> members)
    {
        if (FeatureLexicon.NamedKind(header) is FeatureKind named) return named;
        double Share(Func<int, bool> of) => members.Count(m => of(m.Type)) / (double)members.Count;
        if (Share(ParkingTypes.IsCargo) >= StandMajority) return FeatureKind.Cargo;
        if (Share(t => ParkingTypes.IsGaRamp(t) || ParkingTypes.IsMilitary(t)) >= StandMajority) return FeatureKind.Apron;
        return FeatureKind.Terminal;
    }
}
