using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// GSX's uiTerminalName per stand, grouped. Read from the SELECTABLE list (GetSelectableGates)
/// — GetNamedSpots deliberately does not carry TerminalName. Outranks navdata's letter
/// inference in the catalog (GSX is right where navdata's letter is wrong, measured KJFK).
/// The header is a free-text section title a profile author wrote, not a promise of what kind
/// of place it names, so the KIND is derived from the header text AND the grouped stands'
/// own parking types (see KindOf) rather than assumed to always be a terminal.
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
        var groups = selectableGates
            .Where(s => s.Source == GateSource.Gsx)
            .Select(s => (Spot: s, Header: ParkingSpot.SpeakableTerminalName(s.TerminalName).Trim()))
            .Where(x => x.Header.Length > 0 && !BareHeaders.Contains(x.Header))
            .GroupBy(x => x.Header, x => x.Spot, StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var members = g.ToList();
            if (members.Count < 2) continue;
            var pts = members.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList();
            var c = SurroundingsGeometry.Centroid(pts);
            result.Add(new AirportFeature { Kind = KindOf(g.Key, members), Name = g.Key, Lat = c.Lat, Lon = c.Lon, Members = pts, Source = FeatureSource.Gsx });
        }
        return result;
    }

    /// <summary>The header is a scenery author's SECTION title, so the stands decide as much as the
    /// words do: a cargo ramp typed Terminal took the one Terminal slot in the Alt+L sentence. Cargo
    /// is CIVIL cargo stands (ParkingTypes.IsCargo); a military ramp is ramp stands like a GA one, so
    /// a military majority is an Apron too rather than falling through to Terminal.</summary>
    private static FeatureKind KindOf(string header, List<ParkingSpot> members)
    {
        double Share(Func<int, bool> of) => members.Count(m => of(m.Type)) / (double)members.Count;
        if (FeatureLexicon.Cargo.IsMatch(header) || Share(ParkingTypes.IsCargo) >= StandMajority) return FeatureKind.Cargo;
        if (FeatureLexicon.Fbo.IsMatch(header)) return FeatureKind.Fbo;
        if (FeatureLexicon.Concourse.IsMatch(header)) return FeatureKind.Concourse;
        if (Share(t => ParkingTypes.IsGaRamp(t) || ParkingTypes.IsMilitary(t)) >= StandMajority) return FeatureKind.Apron;
        return FeatureKind.Terminal;
    }
}
