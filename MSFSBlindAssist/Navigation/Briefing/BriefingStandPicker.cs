using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which stand a briefing leg routes to, decided in this order:
/// <list type="number">
/// <item>Military Combat, Fuel and Vehicles stands and de-ice pads are never briefed.</item>
/// <item>SayIntentions' assigned gate, when its flight is this OFP's: by NAME (every stand whose
/// identity matches the label, and only when none does, every stand whose online alias does). When
/// SayIntentions published a position, a name counts only for a stand whose own reach covers that pin:
/// a namesake far from the pin is a different stand and is never used. When several match, the
/// evidence decides — the one nearest the pin, or with no pin a gate before any other type, then list
/// order. A stand of navdata's None type may be the one (navdata reads UNKN as None, and LEBB's jetway
/// gates are UNKN), but it ranks after every other stand the evidence cannot tell it from: with no
/// position, and within a metre when there is one. Only when the name matched nothing in reach of the
/// pin (or only a stand the briefing never routes to, far from the pin) is the published POSITION
/// used on its own. Either way the stand must connect to the taxiway network; a stand the name found
/// but the briefing cannot use is reported, never swapped for a neighbour.</item>
/// <item>Otherwise a representative stand among those that connect, never one of the None type:
/// category (freighter → cargo stands; code A → ramps, then gates, then cargo; everyone else → gates,
/// then ramps, then cargo), wingspan fit, the SimBrief airline's own stands, then the stand at the
/// centre of what is left so the route represents the terminal area.</item>
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

    /// <summary>How every note that says the stands' types are unknown begins — the picker's own and the
    /// OpenStreetMap graph's (<see cref="OsmPlanningGraph.Note"/>), so a leg that carries both can say it once.</summary>
    public const string StandTypesUnknown = "stand types unknown";

    // Military Combat, Fuel, Vehicles: never briefed, not even as SayIntentions' gate.
    private static readonly HashSet<int> NeverBriefedTypes = new() { 8, 16, 17 };

    /// <summary>
    /// Navdata's None type, which is where navdata puts UNKN as well — and UNKN is not only "no stand": a
    /// 2026-09 fs2024 build has 362 UNKN stands at 79 airports and no NONE, real jetway gates among them
    /// (LEBB 101-106, GABS 152/153) but also KGCN's 13 ft helipad "Spot 101". So a None stand may be
    /// SayIntentions' gate, ranked after every other type, but is no basis for a representative guess.
    /// </summary>
    private const int NoneType = 1;

    /// <summary>How much farther from SayIntentions' published point a stand of another type may be and
    /// still rank ahead of a None stand: the point cannot tell two listings a metre apart.</summary>
    private const double NoneTypeTieMetres = 1.0;

    /// <summary>Whether the briefing may route to the stand at all — the pool the SayIntentions step
    /// matches in.</summary>
    private static bool IsRoutable(ParkingSpot s) => !NeverBriefedTypes.Contains(s.Type) && !s.IsDeiceArea;

    /// <summary>Whether the stand may be the representative one: routable, and not of the None type.</summary>
    private static bool IsRepresentativeCandidate(ParkingSpot s) => IsRoutable(s) && s.Type != NoneType;

    /// <summary>A category the representative choice can prefer: the word a note uses for it and the
    /// navdata types it holds.</summary>
    private sealed record StandKind(string Adjective, string Noun, HashSet<int> Types)
    {
        public bool Has(ParkingSpot s) => Types.Contains(s.Type);
    }

    private static readonly StandKind Cargo = new("cargo", "cargo stand", new() { 6, 7 });
    private static readonly StandKind Gate = new("gate", "gate", new() { 9, 10, 11, 13, 14 });
    private static readonly StandKind Ramp = new("ramp", "ramp", new() { 2, 3, 4, 5, 12, 15 });

    /// <summary>The stand for one leg, or null when neither SayIntentions' gate nor a representative stand
    /// connects to the taxiway network (or there are no stands). A null carries no notes on purpose: no
    /// stand is briefed, so there is nothing for a caveat to qualify, and the caller reports the network
    /// reason instead.</summary>
    public static StandChoice? Pick(IReadOnlyList<ParkingSpot> spots, AircraftProfile aircraft, string? airlineIcao,
                                    SayIntentionsGateHint? siGate, Func<ParkingSpot, bool> hasGraphNode)
    {
        if (spots == null || spots.Count == 0) return null;
        var notes = new List<string>();

        if (siGate != null && MatchSayIntentionsGate(spots, siGate, hasGraphNode, notes) is { } assigned)
            return new StandChoice(assigned, StandChoiceSource.SayIntentions, notes);

        // Every narrowing below judges the stands the route can reach, so a category, fit or airline
        // whose only stands are off the network falls back instead of leaving the leg with no stand.
        var eligible = spots.Where(IsRepresentativeCandidate).ToList();
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
    /// When SayIntentions published a position, a name or alias counts only for a stand whose own reach
    /// covers that pin: the pin says where SayIntentions' gate is, and a namesake far from it is a
    /// different stand, never used. The pool is every stand the briefing may route to, None-type stands
    /// included (see <see cref="NoneType"/>). Once the name has found SayIntentions' stand — even one
    /// that does not connect, or one of a type the briefing never routes to — the step ends there: the
    /// position is not then used to hand the label to a differently named neighbour.
    /// </summary>
    private static ParkingSpot? MatchSayIntentionsGate(IReadOnlyList<ParkingSpot> spots, SayIntentionsGateHint hint,
                                                       Func<ParkingSpot, bool> hasGraphNode, List<string> notes)
    {
        string assigned = LabelNamesItsKind(hint.Label)
            ? $"SayIntentions assigned \"{hint.Label}\""
            : $"SayIntentions assigned gate \"{hint.Label}\"";
        var pool = spots.Where(IsRoutable).ToList();
        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(hint.Label);
        GeoPoint? pin = hint.Position;
        bool AtPin(ParkingSpot s) => pin is not GeoPoint point || Reaches(s, point);

        if (wanted.Length > 0)
        {
            var named = pool.Where(s => SameName(IdentityLabel(s), wanted) && AtPin(s)).ToList();
            if (named.Count == 0)
                named = pool.Where(s => HasAlias(s, wanted) && AtPin(s)).ToList();

            if (named.Count > 0)
            {
                var chosen = FirstConnected(ByEvidence(named, pin), pin, hasGraphNode);
                if (chosen == null)
                {
                    notes.Add($"{assigned} was found but does not connect to the taxiway network; using a representative stand instead");
                    return null;
                }
                return Assigned(chosen, hint.Label, wanted, notes);
            }

            // Only a stand the briefing never routes to (Military Combat, Fuel, Vehicles, a de-ice pad)
            // carries the name, at the pin when there is one. It exists, so "not found" would be untrue.
            if (spots.Any(s => !IsRoutable(s) && (SameName(IdentityLabel(s), wanted) || HasAlias(s, wanted)) && AtPin(s)))
            {
                notes.Add($"{assigned} was found, but only as a stand the briefing does not route to; using a representative stand instead");
                return null;
            }
        }

        if (pin is GeoPoint p)
        {
            var inReach = pool.Where(s => Reaches(s, p)).ToList();
            if (inReach.Count > 0)
            {
                var chosen = FirstConnected(ByEvidence(inReach, p), p, hasGraphNode);
                if (chosen == null)
                {
                    notes.Add($"{assigned} was found by position but does not connect to the taxiway network; using a representative stand instead");
                    return null;
                }
                return Assigned(chosen, hint.Label, wanted, notes);
            }
        }

        // With a published position the only honest claim is about that position: a stand of the same name can be
        // here, outside the pin's reach, and "not found at this airport" would then be untrue.
        notes.Add(pin is GeoPoint
            ? $"{assigned}, but no stand at SayIntentions' position was found in this scenery; using a representative stand instead"
            : $"{assigned} was not found at this airport; using a representative stand instead");
        return null;
    }

    /// <summary>SayIntentions' stand, with a note whenever this scenery lists it under another name (an
    /// alias or a position match), so the pilot hears why the briefed stand is not called what
    /// SayIntentions called it. A label that names no stand at all ("Gate") matches nothing by name, so a stand
    /// found for it was found by position alone and always gets the note — even one whose own label names no
    /// stand either ("Parking"), which would otherwise compare as the same empty name.</summary>
    private static ParkingSpot Assigned(ParkingSpot stand, string label, string wanted, List<string> notes)
    {
        string listedAs = IdentityLabel(stand);
        if (wanted.Length == 0 || !SameName(listedAs, wanted))
            notes.Add($"SayIntentions assigned {label}, which this scenery lists as {listedAs}");
        return stand;
    }

    /// <summary>Whether a stand label already says what kind of stand it is ("Gate 5", "Spot 12", "Parking"), so
    /// "gate" is not put in front of it ("SayIntentions assigned gate Gate 5").</summary>
    internal static bool LabelNamesItsKind(string label) => KindWord.IsMatch(label);

    private static readonly Regex KindWord = new(@"^\s*(?:gate|spot|parking)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool SameName(string label, string wanted) =>
        string.Equals(SayIntentionsClearanceParser.NormalizeParkingName(label), wanted, StringComparison.Ordinal);

    private static bool HasAlias(ParkingSpot s, string wanted) => s.Aliases.Any(a => SameName(a, wanted));

    /// <summary>How the evidence ranks stands SayIntentions' label cannot tell apart when no position was
    /// published: a gate, then any other type, then the None type.</summary>
    private static int RankWithoutPosition(ParkingSpot s) => Gate.Has(s) ? 0 : s.Type == NoneType ? 2 : 1;

    /// <summary>
    /// Several stands answer to the name, or reach the published point: nearest that point first, except
    /// that a None stand gives way to a stand of any other type no more than
    /// <see cref="NoneTypeTieMetres"/> farther. Ordering by distance less that margin for every other type
    /// states exactly this and stays a consistent ordering, which a pairwise "within a metre" test is not.
    /// With no position, <see cref="RankWithoutPosition"/>. OrderBy is stable, so list order breaks every
    /// remaining tie.
    /// </summary>
    private static List<ParkingSpot> ByEvidence(List<ParkingSpot> candidates, GeoPoint? position) =>
        (position is GeoPoint p
            ? candidates.OrderBy(s => MetresTo(s, p) - (s.Type == NoneType ? 0.0 : NoneTypeTieMetres))
                        .ThenBy(s => s.Type == NoneType ? 1 : 0)
            : candidates.OrderBy(RankWithoutPosition)).ToList();

    /// <summary>
    /// The first stand in <paramref name="ordered"/> that connects to the taxiway network. A later stand
    /// is tried only when the evidence that ordered them cannot tell it from the first — with a published
    /// position, its own acceptance also reaches that point (a stand listed twice); with none, it ranks
    /// the same as the first (<see cref="RankWithoutPosition"/>). A stand the evidence ranked below the
    /// first is a DIFFERENT stand, and briefing it as SayIntentions' gate is the failure this match
    /// exists to prevent.
    /// </summary>
    private static ParkingSpot? FirstConnected(List<ParkingSpot> ordered, GeoPoint? position,
                                               Func<ParkingSpot, bool> hasGraphNode)
    {
        var first = ordered[0];
        foreach (var s in ordered)
        {
            bool indistinguishable = ReferenceEquals(s, first) ||
                (position is GeoPoint p
                    ? Reaches(s, p)
                    : RankWithoutPosition(s) == RankWithoutPosition(first));
            if (indistinguishable && hasGraphNode(s)) return s;
        }
        return null;
    }

    private static double MetresTo(ParkingSpot s, GeoPoint p) =>
        TaxiGraph.FastDistanceMeters(p.Latitude, p.Longitude, s.Latitude, s.Longitude);

    /// <summary>Whether the published point is within the stand's own reach
    /// (<see cref="AcceptanceMetres"/>).</summary>
    private static bool Reaches(ParkingSpot s, GeoPoint point) => MetresTo(s, point) <= AcceptanceMetres(s);

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
            notes.Add($"{StandTypesUnknown} at this airport; the stand may not be a {preference[0].Noun}");
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
