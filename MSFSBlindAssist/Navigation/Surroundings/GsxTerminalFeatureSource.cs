using MSFSBlindAssist.Database.Models;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// GSX's uiTerminalName per stand, grouped. Read from the SELECTABLE list (GetSelectableGates)
/// — GetNamedSpots deliberately does not carry TerminalName. Outranks navdata's letter
/// inference in the catalog (GSX is right where navdata's letter is wrong, measured KJFK).
/// The header is a free-text section title a profile author wrote, not a promise of what kind
/// of place it names, so the KIND is derived from the header text AND the grouped stands'
/// own parking types (see KindOf) rather than assumed to always be a terminal.
/// Its NAME is the header without the author's notes (see PlaceName).
/// </summary>
public static class GsxTerminalFeatureSource
{
    /// <summary>Category headers with no place behind them — GSX profile authors use these as
    /// plain section dividers, not names. Exact match only: "Terminal 5 - Remote" survives.</summary>
    private static readonly HashSet<string> BareHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "Parking", "Ramp", "Gates", "Gate", "Stands", "Stand", "Apron" };
    private const double StandMajority = 0.60;

    public static List<AirportFeature> Read(IReadOnlyList<ParkingSpot> selectableGates)
    {
        var result = new List<AirportFeature>();
        // Grouped by the header AS WRITTEN: two sections that clean to one name stay two groups, and
        // the catalog's same-name rule (distance + stands) decides whether they are one place.
        var groups = selectableGates
            .Where(s => s.Source == GateSource.Gsx)
            .Select(s => (Spot: s, Header: ParkingSpot.SpeakableTerminalName(s.TerminalName).Trim()))
            .Where(x => x.Header.Length > 0)
            .GroupBy(x => x.Header, x => x.Spot, StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var members = g.ToList();
            if (members.Count < 2) continue;
            // Named as a PLACE, without the author's notes; nothing but notes, or only a category word
            // once they are gone ("Ramp (TD)"), is no place at all.
            string name = PlaceName(g.Key);
            if (name.Length == 0 || BareHeaders.Contains(name)) continue;
            var pts = members.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList();
            var c = SurroundingsGeometry.Centroid(pts);
            // The KIND is read from the header as grouped — notes included, only the size-hint tail and
            // "N/A" gone: a kind word the author put in a note ("Ramp 5 (Cargo)") still says what the
            // section is, even though the NAME drops it.
            result.Add(new AirportFeature { Kind = KindOf(g.Key, members), Name = name, Lat = c.Lat, Lon = c.Lon, Members = pts, Source = FeatureSource.Gsx });
        }
        return result;
    }

    // "(T1-T21)", "(TD)": a profile author's note inside a header, never part of what the place is called.
    private static readonly Regex Parenthesised = new(@"\([^()]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.CultureInvariant);
    private static readonly char[] DanglingSeparators = { ' ', '-', '–', '—', ',', ';', ':', '/', '|' };

    /// <summary>
    /// What a section header is CALLED as a place: every parenthesised group removed (innermost
    /// first, until none is left), then <see cref="ParkingSpot.SpeakableTerminalName"/> (the size-hint
    /// tail and a trailing "N/A"), then any dash or comma left dangling at either end. "K/M-Platform
    /// buffer overflow (TD) N/A" is "K/M-Platform buffer overflow"; KATL's "Concourse T (T1-T21)" is
    /// "Concourse T" — what OSM and the scenery call that pier, which is what lets the catalog merge
    /// the three. The groups go FIRST because a note can hide a tail from SpeakableTerminalName,
    /// which only looks at the END ("A-Platform =&lt; Medium (TD)"). Empty when the header was
    /// nothing but a note. EVERY parenthesised group goes, whatever it says — in a GSX header they
    /// carry the author's notes (a stand range, "TD"), so a qualifier an author put in parentheses
    /// is lost with them; the KIND is not, because Read classifies the header as grouped. The gate
    /// LABEL (ParkingSpot.Describe) keeps them: a label must stay unique, and the terminal name is
    /// there to tell two stands apart.
    /// </summary>
    internal static string PlaceName(string? header)
    {
        string s = header ?? "", before;
        do { before = s; s = Parenthesised.Replace(s, " "); } while (s != before);
        return ParkingSpot.SpeakableTerminalName(Spaces.Replace(s, " ")).Trim(DanglingSeparators);
    }

    /// <summary>
    /// The header's WORDS decide first, through the ONE order every tier reads a name in
    /// (<see cref="FeatureLexicon.NamedKind"/>: Cargo, then Fbo, then Concourse), so a header names
    /// the same kind here as the same words do from OSM or the scenery — the catalog never merges
    /// across kinds. Only a header whose words say none of the three is decided by its STANDS: it is
    /// a scenery author's section title, and a cargo ramp typed Terminal took the one Terminal slot
    /// in the Alt+L sentence. Cargo is CIVIL cargo stands (ParkingTypes.IsCargo); a military ramp is
    /// ramp stands like a GA one, so a military majority is an Apron too rather than falling through
    /// to Terminal.
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
