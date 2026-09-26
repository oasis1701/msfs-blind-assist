using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which stand a briefing leg routes to, decided in this order:
/// <list type="number">
/// <item>Stands that are never briefed are removed first — the None (navdata's UNKN included),
/// Military Combat, Fuel and Vehicles types, and de-ice pads — for the SayIntentions match as well as
/// the representative choice.</item>
/// <item>SayIntentions' assigned gate, when its flight is this OFP's: by NAME (every stand whose
/// identity matches the label, and only when none does, every stand whose online alias does); when
/// several match, the evidence decides — the one nearest the position SayIntentions published, or
/// with no position a gate before any other type, then list order. Only when the name matched nothing
/// is the published POSITION used on its own. Either way the stand must connect to the taxiway
/// network; a stand the name found but the briefing cannot use is reported, never swapped for a
/// neighbour.</item>
/// <item>Otherwise a representative stand among those that connect: category (freighter → cargo
/// stands; code A → ramps, then gates, then cargo; everyone else → gates, then ramps, then cargo),
/// wingspan fit, the SimBrief airline's own stands, then the stand at the centre of what is left so
/// the route represents the terminal area.</item>
/// </list>
/// Every fallback and every substitution leaves a pilot-readable note; nothing is silent. Pure: the
/// caller supplies "has a graph node within 100 m" as a predicate.
/// </summary>
public static class BriefingStandPicker
{
    /// <summary>The position match's distance backstop: the taxi-route import's own
    /// (<see cref="SayIntentionsGatePositionMatcher.MaxMatchMetres"/>), so the briefing and the import
    /// can never disagree about how far a stand may answer for a published gate.</summary>
    public const double SiPositionBackstopMetres = SayIntentionsGatePositionMatcher.MaxMatchMetres;

    /// <summary>The published gate position is the NOSE-STOP, offset from the stand datum by up to about
    /// twice the stand's radius: the import's factor
    /// (<see cref="SayIntentionsGatePositionMatcher.NoseStopRadiusFactor"/>, calibrated on KDTW and EDDB).</summary>
    public const double SiNoseStopRadiusFactor = SayIntentionsGatePositionMatcher.NoseStopRadiusFactor;

    /// <summary>
    /// How far the published point may sit from a stand whose size is unknown (radius 0). This DEPARTS
    /// from <see cref="SayIntentionsGatePositionMatcher"/>, which skips a stand with no radius because
    /// it states no scale: the briefing's OpenStreetMap tier gives EVERY stand radius 0 (OpenStreetMap
    /// carries no stand size), so skipping them would switch the position match off for that whole
    /// tier. 60 m is what a 30 m-radius stand answers to; both measured nose-stop offsets (EDDB 18.9 m,
    /// KDTW 30.1 m) sit inside it, and the nearest stand still wins where several qualify.
    /// </summary>
    public const double SiPositionUnknownRadiusMetres = 60.0;

    private const double FeetToMetres = 0.3048;

    // None (navdata maps UNKN there too), Military Combat, Fuel, Vehicles.
    private static readonly HashSet<int> ExcludedTypes = new() { 1, 8, 16, 17 };

    /// <summary>Whether the briefing may route to the stand at all — the one filter both the SayIntentions
    /// match and the representative choice apply.</summary>
    private static bool IsEligible(ParkingSpot s) => !ExcludedTypes.Contains(s.Type) && !s.IsDeiceArea;

    /// <summary>A category the representative choice can prefer: the word a note uses for it and the
    /// navdata types it holds.</summary>
    private sealed record StandKind(string Adjective, string Noun, HashSet<int> Types)
    {
        public bool Has(ParkingSpot s) => Types.Contains(s.Type);
    }

    private static readonly StandKind Cargo = new("cargo", "cargo stand", new() { 6, 7 });
    private static readonly StandKind Gate = new("gate", "gate", new() { 9, 10, 11, 13, 14 });
    private static readonly StandKind Ramp = new("ramp", "ramp", new() { 2, 3, 4, 5, 12, 15 });

    /// <summary>The stand for one leg, or null when no eligible stand connects to the taxiway network
    /// (or there are none). A null carries no notes on purpose: no stand is briefed, so there is nothing
    /// for a caveat to qualify, and the caller reports the network reason instead.</summary>
    public static StandChoice? Pick(IReadOnlyList<ParkingSpot> spots, AircraftProfile aircraft, string? airlineIcao,
                                    SayIntentionsGateHint? siGate, Func<ParkingSpot, bool> hasGraphNode)
    {
        if (spots == null || spots.Count == 0) return null;
        var notes = new List<string>();
        var eligible = spots.Where(IsEligible).ToList();

        if (siGate != null && MatchSayIntentionsGate(spots, eligible, siGate, hasGraphNode, notes) is { } assigned)
            return new StandChoice(assigned, StandChoiceSource.SayIntentions, notes);

        // Every narrowing below judges the stands the route can reach, so a category, fit or airline
        // whose only stands are off the network falls back instead of leaving the leg with no stand.
        var reachable = eligible.Where(hasGraphNode).ToList();
        if (reachable.Count == 0) return null;

        var pool = ByCategory(reachable, eligible, Preference(aircraft), notes, out var kind);

        if (aircraft.WingspanMetres is double span)
        {
            double spanFeet = span / FeetToMetres;
            bool Fits(ParkingSpot s) => SizeUnknown(s) || s.FitsAircraft(spanFeet);
            var fitting = pool.Where(Fits).ToList();
            if (fitting.Count > 0) pool = fitting;
            else notes.Add(NoneFits(kind, span, fitsOffNetwork: eligible.Any(s => InCategory(s, kind) && Fits(s))));
        }

        var source = kind != null ? StandChoiceSource.Category : StandChoiceSource.Any;
        if (!string.IsNullOrWhiteSpace(airlineIcao))
        {
            var airline = pool.Where(s => ServesAirline(s, airlineIcao)).ToList();
            if (airline.Count > 0) { pool = airline; source = StandChoiceSource.AirlineMatch; }
        }

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

    // ── SayIntentions ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The stand SayIntentions assigned, or null with a note saying why not. NormalizeParkingName strips
    /// GATE/SPOT/PARKING/…, so a letterless "Gate 8", "Spot 8" and "Parking 8" all compare as "8", and
    /// navdata lists its unnamed spots (the GA ramps) first — at CYEG "Spot 8" is a GA ramp 1.2 km from
    /// "Gate 8". So every exact match is collected (and only when there is none, every alias match) and
    /// the evidence chooses among them; list order alone would brief the ramp as the controller's gate.
    /// Once the name has found SayIntentions' stand — even one that does not connect, or one of a type
    /// the briefing never routes to — the step ends there: the position is not then used to hand the
    /// label to a differently named neighbour.
    /// </summary>
    private static ParkingSpot? MatchSayIntentionsGate(IReadOnlyList<ParkingSpot> spots, List<ParkingSpot> eligible,
                                                       SayIntentionsGateHint hint, Func<ParkingSpot, bool> hasGraphNode,
                                                       List<string> notes)
    {
        string assigned = $"SayIntentions assigned gate \"{hint.Label}\"";
        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(hint.Label);
        if (wanted.Length > 0)
        {
            bool viaAlias = false;
            var named = eligible.Where(s => SameName(IdentityLabel(s), wanted)).ToList();
            if (named.Count == 0)
            {
                named = eligible.Where(s => HasAlias(s, wanted)).ToList();
                viaAlias = named.Count > 0;
            }

            if (named.Count > 0)
            {
                var chosen = FirstConnected(ByEvidence(named, hint.Position), hint.Position, hasGraphNode);
                if (chosen == null)
                {
                    notes.Add($"{assigned} was found but does not connect to the taxiway network; using a representative stand instead");
                    return null;
                }
                if (viaAlias) notes.Add($"{assigned} is listed under another name at this airport");
                return chosen;
            }

            // Only a stand the briefing never routes to carries the name. Type 1 is navdata's UNKN as
            // well as NONE — fs2024 has 362 UNKN stands and no NONE, jetway gates LEBB 101-106 among them
            // — so "not found" would be untrue.
            if (spots.Any(s => !IsEligible(s) && (SameName(IdentityLabel(s), wanted) || HasAlias(s, wanted))))
            {
                notes.Add($"{assigned} was found, but only as a stand the briefing does not route to; using a representative stand instead");
                return null;
            }
        }

        if (hint.Position is GeoPoint p)
        {
            var inReach = eligible
                .Select(s => (Spot: s, Metres: MetresTo(s, p)))
                .Where(c => c.Metres <= AcceptanceMetres(c.Spot))
                .OrderBy(c => c.Metres)
                .Select(c => c.Spot)
                .ToList();
            if (inReach.Count > 0)
            {
                var chosen = FirstConnected(inReach, p, hasGraphNode);
                if (chosen == null)
                {
                    notes.Add($"{assigned} was found by position but does not connect to the taxiway network; using a representative stand instead");
                    return null;
                }
                notes.Add($"{assigned} is not listed under that name at this airport; matched by position");
                return chosen;
            }
        }

        notes.Add($"{assigned} was not found at this airport; using a representative stand instead");
        return null;
    }

    private static bool SameName(string label, string wanted) =>
        string.Equals(SayIntentionsClearanceParser.NormalizeParkingName(label), wanted, StringComparison.Ordinal);

    private static bool HasAlias(ParkingSpot s, string wanted) => s.Aliases.Any(a => SameName(a, wanted));

    /// <summary>Several stands answer to the name: nearest the published position first; with no position,
    /// gates before any other type. OrderBy is stable, so list order breaks every tie.</summary>
    private static List<ParkingSpot> ByEvidence(List<ParkingSpot> named, GeoPoint? position) =>
        (position is GeoPoint p
            ? named.OrderBy(s => MetresTo(s, p))
            : named.OrderBy(s => Gate.Has(s) ? 0 : 1)).ToList();

    /// <summary>
    /// The first stand in <paramref name="ordered"/> that connects to the taxiway network. A later stand
    /// is tried only when the evidence that ordered them cannot tell it from the first — with a published
    /// position, its own acceptance also reaches that point (a stand listed twice); with none, it is the
    /// same kind, gate or not, as the first. A stand the evidence ranked below the first is a DIFFERENT
    /// stand, and briefing it as SayIntentions' gate is the failure this match exists to prevent.
    /// </summary>
    private static ParkingSpot? FirstConnected(List<ParkingSpot> ordered, GeoPoint? position,
                                               Func<ParkingSpot, bool> hasGraphNode)
    {
        var first = ordered[0];
        foreach (var s in ordered)
        {
            bool indistinguishable = ReferenceEquals(s, first) ||
                (position is GeoPoint p ? MetresTo(s, p) <= AcceptanceMetres(s) : Gate.Has(s) == Gate.Has(first));
            if (indistinguishable && hasGraphNode(s)) return s;
        }
        return null;
    }

    private static double MetresTo(ParkingSpot s, GeoPoint p) =>
        TaxiGraph.FastDistanceMeters(p.Latitude, p.Longitude, s.Latitude, s.Longitude);

    /// <summary>How far the published point may sit from a stand and still be that stand: twice its
    /// radius, capped by the backstop, or <see cref="SiPositionUnknownRadiusMetres"/> when its size is
    /// unknown.</summary>
    private static double AcceptanceMetres(ParkingSpot s)
    {
        // A navdata radius is FEET and a GSX one METRES — the rule ParkingSpot.FitsAircraft and
        // TaxiAssistForm's own position match apply.
        double radius = s.Source == GateSource.Gsx ? s.Radius : s.Radius * FeetToMetres;
        return radius > 0
            ? Math.Min(SiPositionBackstopMetres, radius * SiNoseStopRadiusFactor)
            : SiPositionUnknownRadiusMetres;
    }

    // ── Representative stand ─────────────────────────────────────────────────────────────────

    /// <summary>The categories an aircraft is briefed to, best first. Code A (under 15 m: a Cessna 172,
    /// a TBM 900) parks on a GA ramp, not at an airline gate. Code B stays with the airliners on purpose:
    /// it holds the CRJ-200 and CRJ-700 as well as the King Air and the Citation, and the wingspan alone
    /// cannot tell a regional jet at its gate from a business jet at the FBO.</summary>
    private static StandKind[] Preference(AircraftProfile aircraft) =>
        aircraft.IsFreighter ? new[] { Cargo }
        : aircraft.CodeLetter == IcaoCodeLetter.A ? new[] { Ramp, Gate, Cargo }
        : new[] { Gate, Ramp, Cargo };

    /// <summary>The reachable stands of the first preferred category that has any, with stands of unknown
    /// type (0) kept beside it; a note whenever the first preference could not be honoured.</summary>
    private static List<ParkingSpot> ByCategory(List<ParkingSpot> reachable, List<ParkingSpot> eligible,
                                                StandKind[] preference, List<string> notes, out StandKind? kind)
    {
        kind = null;
        if (reachable.All(s => s.Type == 0))
        {
            notes.Add($"stand types unknown at this airport; the stand may not be a {preference[0].Noun}");
            return reachable;
        }

        for (int i = 0; i < preference.Length; i++)
        {
            var candidate = preference[i];
            if (!reachable.Any(candidate.Has)) continue;
            kind = candidate;
            if (i > 0) notes.Add($"{NoneOf(preference[..i], eligible)}; using a {candidate.Noun}");
            return reachable.Where(s => InCategory(s, candidate)).ToList();
        }

        notes.Add(NoneOf(preference, eligible));
        return reachable;
    }

    private static bool InCategory(ParkingSpot s, StandKind? kind) => kind == null || kind.Has(s) || s.Type == 0;

    /// <summary>"no gate or ramp stands at this airport" — or, when some exist but none connects to the
    /// taxiway network, says that instead, because the choice is made among the stands that connect.</summary>
    private static string NoneOf(IReadOnlyList<StandKind> kinds, List<ParkingSpot> eligible)
    {
        string names = string.Join(" or ", kinds.Select(k => k.Adjective));
        return eligible.Any(s => kinds.Any(k => k.Has(s)))
            ? $"no {names} stand at this airport connects to the taxiway network"
            : $"no {names} stands at this airport";
    }

    /// <summary>Names the stands the wingspan was judged against: the category's, or every stand when no
    /// category applied.</summary>
    private static string NoneFits(StandKind? kind, double spanMetres, bool fitsOffNetwork)
    {
        string noun = kind?.Noun ?? "stand";
        string span = spanMetres.ToString("0.0", CultureInfo.InvariantCulture);
        return fitsOffNetwork
            ? $"no {noun} that fits a {span} m wingspan connects to the taxiway network"
            : $"no {noun} at this airport is marked as fitting a {span} m wingspan";
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
