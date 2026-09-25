using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which stand the briefing routes to when SayIntentions has not assigned one (or its gate is not
/// at this airport): category (freighter → cargo ramps, airliner → gates, ramps as fallback),
/// wingspan fit, the SimBrief airline's own stands, then the stand at the centre of what is left so
/// the route represents the terminal area. Every fallback leaves a pilot-readable note; nothing is
/// silent. Pure: the caller supplies "has a graph node within 100 m" as a predicate.
/// </summary>
public static class BriefingStandPicker
{
    /// <summary>Position backstop for the SayIntentions gate (same role as GateAliasResolver's 150 m).</summary>
    public const double SiPositionBackstopMetres = 150.0;
    /// <summary>The published gate position is the NOSE-STOP, offset from the stand datum by up to
    /// about twice the stand's radius (measured KDTW/EDDB) — TaxiAssistForm's SI step uses the same factor.</summary>
    public const double SiNoseStopRadiusFactor = 2.0;
    /// <summary>Acceptance for a stand whose size is unknown (radius 0: OpenStreetMap, sparse navdata).</summary>
    public const double SiPositionUnknownRadiusMetres = 60.0;

    private static readonly HashSet<int> ExcludedTypes = new() { 1, 8, 16, 17 };   // None, Military Combat, Fuel, Vehicles
    private static readonly HashSet<int> CargoTypes = new() { 6, 7 };
    private static readonly HashSet<int> GateTypes = new() { 9, 10, 11, 13, 14 };
    private static readonly HashSet<int> RampTypes = new() { 2, 3, 4, 5, 12, 15 };

    public static StandChoice? Pick(IReadOnlyList<ParkingSpot> spots, AircraftProfile aircraft, string? airlineIcao,
                                    SayIntentionsGateHint? siGate, Func<ParkingSpot, bool> hasGraphNode)
    {
        var notes = new List<string>();
        if (spots == null || spots.Count == 0) return null;

        if (siGate != null)
        {
            var si = MatchSayIntentionsGate(spots, siGate, hasGraphNode, notes);
            if (si != null) return si;
            notes.Add($"SayIntentions assigned gate \"{siGate.Label}\" was not found at this airport; using a representative stand instead");
        }

        var pool = spots.Where(s => !ExcludedTypes.Contains(s.Type) && !s.IsDeiceArea).ToList();
        if (pool.Count == 0) return null;

        bool categoryApplied = false;
        if (aircraft.IsFreighter)
        {
            var cargo = pool.Where(s => CargoTypes.Contains(s.Type) || s.Type == 0).ToList();
            if (cargo.Any(s => CargoTypes.Contains(s.Type))) { pool = cargo; categoryApplied = true; }
            else notes.Add("no cargo stands at this airport");
        }
        else
        {
            var gates = pool.Where(s => GateTypes.Contains(s.Type) || s.Type == 0).ToList();
            if (gates.Any(s => GateTypes.Contains(s.Type))) { pool = gates; categoryApplied = true; }
            else
            {
                var ramps = pool.Where(s => RampTypes.Contains(s.Type) || s.Type == 0).ToList();
                if (ramps.Any(s => RampTypes.Contains(s.Type)))
                {
                    pool = ramps; categoryApplied = true;
                    notes.Add("no gate stands at this airport; using a ramp");
                }
            }
        }

        if (aircraft.WingspanMetres is double span)
        {
            double spanFeet = span / 0.3048;
            var fitting = pool.Where(s => SizeUnknown(s) || s.FitsAircraft(spanFeet)).ToList();
            if (fitting.Count > 0) pool = fitting;
            else notes.Add($"no stand at this airport is marked as fitting a {span.ToString("0.0", CultureInfo.InvariantCulture)} m wingspan");
        }

        var source = categoryApplied ? StandChoiceSource.Category : StandChoiceSource.Any;
        if (!string.IsNullOrWhiteSpace(airlineIcao))
        {
            var airline = pool.Where(s => ServesAirline(s, airlineIcao)).ToList();
            if (airline.Count > 0) { pool = airline; source = StandChoiceSource.AirlineMatch; }
        }

        pool = pool.Where(hasGraphNode).ToList();
        if (pool.Count == 0) return null;
        return new StandChoice(Central(pool), source, notes);
    }

    /// <summary>"A 24A", "Gate 5" — the part of <see cref="ParkingSpot.Describe"/> before its first spaced dash,
    /// which is the boundary NormalizeParkingName cuts at, so a SayIntentions label compares against it.</summary>
    public static string IdentityLabel(ParkingSpot spot)
    {
        string d = spot.Describe();
        int cut = d.IndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? d[..cut] : d;
    }

    private static StandChoice? MatchSayIntentionsGate(IReadOnlyList<ParkingSpot> spots, SayIntentionsGateHint hint,
                                                       Func<ParkingSpot, bool> hasGraphNode, List<string> notes)
    {
        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(hint.Label);
        if (wanted.Length > 0)
        {
            var byName = spots.FirstOrDefault(s => hasGraphNode(s) &&
                (SayIntentionsClearanceParser.NormalizeParkingName(IdentityLabel(s)) == wanted ||
                 s.Aliases.Any(a => SayIntentionsClearanceParser.NormalizeParkingName(a) == wanted)));
            if (byName != null) return new StandChoice(byName, StandChoiceSource.SayIntentions, notes.ToList());
        }

        if (hint.Position is GeoPoint p)
        {
            ParkingSpot? best = null;
            double bestD = double.MaxValue;
            foreach (var s in spots)
            {
                if (!hasGraphNode(s)) continue;
                double d = TaxiGraph.FastDistanceMeters(p.Latitude, p.Longitude, s.Latitude, s.Longitude);
                double radiusM = s.Source == GateSource.Navdata ? s.Radius * 0.3048 : s.Radius;
                double limit = radiusM > 0
                    ? Math.Min(SiPositionBackstopMetres, radiusM * SiNoseStopRadiusFactor)
                    : SiPositionUnknownRadiusMetres;
                if (d <= limit && d < bestD) { best = s; bestD = d; }
            }
            if (best != null)
            {
                notes.Add("assigned gate matched by position");
                return new StandChoice(best, StandChoiceSource.SayIntentions, notes.ToList());
            }
        }
        return null;
    }

    private static bool SizeUnknown(ParkingSpot s) => s.Radius <= 0 && !s.MaxWingspanMeters.HasValue;

    private static bool ServesAirline(ParkingSpot s, string airlineIcao)
    {
        if (string.IsNullOrWhiteSpace(s.AirlineCodes)) return false;
        return s.AirlineCodes.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(code => string.Equals(code, airlineIcao.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static ParkingSpot Central(List<ParkingSpot> pool)
    {
        double cLat = pool.Average(s => s.Latitude);
        double cLon = pool.Average(s => s.Longitude);
        return pool
            .OrderBy(s => TaxiGraph.FastDistanceMeters(cLat, cLon, s.Latitude, s.Longitude))
            .ThenBy(s => s.Describe(), StringComparer.Ordinal)
            .First();
    }
}

/// <summary>
/// Turns a flight.json snapshot into the briefing's arrival-gate hint, or null. SayIntentions assigns
/// an ARRIVAL gate only, so the hint is offered only when SayIntentions' flight is THIS OFP's flight:
/// the file exists, its origin AND destination match the plan, and a gate is set. SI not running → no
/// file → null; SI on another flight, or a stale file from another city pair → null. No timestamp is
/// consulted (a stale file for the same city pair is accepted and labelled as SayIntentions' assignment).
/// </summary>
public static class SayIntentionsArrivalGate
{
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao)
    {
        if (ctx == null || !ctx.FlightJsonExists) return null;
        if (string.IsNullOrWhiteSpace(ctx.AssignedGate)) return null;
        if (!IcaoEquals(ctx.Origin, departureIcao) || !IcaoEquals(ctx.Destination, arrivalIcao)) return null;
        return new SayIntentionsGateHint(ctx.AssignedGate.Trim(), ctx.AssignedGatePosition);
    }

    private static bool IcaoEquals(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
