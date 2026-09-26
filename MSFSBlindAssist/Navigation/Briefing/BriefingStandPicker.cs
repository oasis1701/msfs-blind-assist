using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Which stand a briefing leg routes to, decided in this order:
/// <list type="number">
/// <item>Military Combat, Fuel and Vehicles stands and de-ice pads — the EXCLUDED kinds — are never a
/// representative stand; as SayIntentions' gate one is briefed only when no stand of another kind answers,
/// as Taxi Assist's import would seat it where its list carries one (see ExcludedTypes).</item>
/// <item>SayIntentions' assigned gate, when its flight is this OFP's, looked for in the order Taxi
/// Assist's own SayIntentions import looks for it (TaxiAssistForm.TryResolveExternalDestination): by
/// NAME (every stand whose identity matches the label), only when none does by the scenery's online
/// ALIASES, and only when neither matches by the POSITION SayIntentions published. A name or alias counts
/// wherever the stand is, and whatever its kind, as it does in the import, but a stand of an ordinary kind
/// always beats one of an excluded kind, which is briefed with a note naming its kind. When several match,
/// the evidence decides — the one nearest the published position, or with none a gate before any other
/// type, then list order — and when the published position is outside the chosen stand's own reach, a
/// note says how far away it is. A stand of navdata's None type may be the one (navdata reads UNKN as
/// None, and LEBB's jetway gates are UNKN), but it ranks after every other stand the evidence cannot tell
/// it from: with no position, and within a metre when there is one. The position is used only when no
/// name or alias matched at all: the nearest stand whose reach covers it, of an ordinary kind when any is
/// in reach, else of an excluded kind. Either way the stand must connect to the taxiway network: a
/// namesake that does not gives way only to another at least as plausible, else it is reported and a
/// representative stand used; at the position the nearest stand in reach that connects is used, with a
/// note naming the nearer one that does not.</item>
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

    /// <summary>
    /// The EXCLUDED navdata types — Military Combat, Fuel, Vehicles — in the words a note uses for them (de-ice
    /// pads are excluded too, by their own flag). An excluded stand is never the representative choice. As
    /// SayIntentions' gate it is taken only when no stand of another kind answers — to the name, to an alias,
    /// or, with nothing answering to either, within reach of the published position — as Taxi Assist's import
    /// would seat it where its list carries one (owner decision, 2026-09-26; applied to the position the same
    /// day), and then with <see cref="KindNotePrefix"/> naming its kind. The import's gate list carries fuel and
    /// vehicle stands only when it comes from navdata: GSX's gate list never carries them (it can carry a
    /// military combat ramp), and de-ice pads are a destination type of their own there.
    /// </summary>
    private static readonly Dictionary<int, string> ExcludedTypes = new()
    {
        [8] = "military combat ramp",   // Ramp Military Combat
        [16] = "fuel stand",            // Fuel
        [17] = "vehicle stand",         // Vehicles
    };

    private const string DeicePadWords = "de-icing pad";

    /// <summary>How the note on a briefed stand of an excluded kind begins: "this scenery marks that stand as a
    /// fuel stand".</summary>
    private const string KindNotePrefix = "this scenery marks that stand as a ";

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

    /// <summary>Whether the stand is of an excluded kind (<see cref="ExcludedTypes"/>, or a de-ice pad).</summary>
    private static bool IsExcludedKind(ParkingSpot s) => s.IsDeiceArea || ExcludedTypes.ContainsKey(s.Type);

    /// <summary>"fuel stand", "de-icing pad": an excluded stand's kind in a note's words.</summary>
    private static string ExcludedKindWords(ParkingSpot s) => s.IsDeiceArea ? DeicePadWords : ExcludedTypes[s.Type];

    /// <summary>" as a fuel stand" for a stand of an excluded kind, nothing for any other: what a "was found …
    /// but does not connect" note says of the stand it found.</summary>
    private static string AsKind(ParkingSpot s) => IsExcludedKind(s) ? $" as a {ExcludedKindWords(s)}" : "";

    /// <summary>Whether the stand may be the representative one: not of an excluded kind, and not of the None
    /// type.</summary>
    private static bool IsRepresentativeCandidate(ParkingSpot s) => !IsExcludedKind(s) && s.Type != NoneType;

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
                                    SayIntentionsGateHint? siGate, Func<ParkingSpot, bool> hasGraphNode,
                                    DistanceUnit unit = DistanceUnit.Metres)
    {
        if (spots == null || spots.Count == 0) return null;
        var notes = new List<string>();
        IReadOnlyList<ParkingSpot> refused = Array.Empty<ParkingSpot>();

        if (siGate != null && MatchSayIntentionsGate(spots, siGate, hasGraphNode, notes, unit, out refused) is { } assigned)
            return new StandChoice(assigned, StandChoiceSource.SayIntentions, notes);

        // Every narrowing below judges the stands the route can reach, so a category, fit or airline
        // whose only stands are off the network falls back instead of leaving the leg with no stand. A stand
        // the SayIntentions step has just refused is never the representative one either, or "Gate 5 was
        // found but does not connect" could be followed by "representative stand Gate 5".
        var eligible = spots.Where(IsRepresentativeCandidate).ToList();
        var reachable = eligible.Where(s => hasGraphNode(s) && !refused.Any(r => ReferenceEquals(r, s))).ToList();
        if (reachable.Count == 0) return null;

        var pool = ByCategory(reachable, eligible, Preference(aircraft), notes, out var kind);

        if (aircraft.WingspanMetres is double span)
        {
            double spanFeet = span / FeetToMetres;
            bool Fits(ParkingSpot s) => SizeUnknown(s) || s.FitsAircraft(spanFeet);
            var fitting = pool.Where(Fits).ToList();
            if (fitting.Count > 0) pool = fitting;
            else notes.Add(NoneFits(kind, span, fitsOffNetwork: eligible.Any(s => InCategory(s, kind) && Fits(s)), unit));
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
    /// The stand SayIntentions assigned, or null with a note saying why not. The evidence is tried in the
    /// order Taxi Assist's own SayIntentions import tries it (TaxiAssistForm.TryResolveExternalDestination:
    /// the name, then the scenery's online aliases, then the published position), so the briefing looks for
    /// SayIntentions' gate the way the import does (owner decision, 2026-09-26, reversing an earlier rule
    /// under which the published position overruled the name). NormalizeParkingName strips
    /// GATE/SPOT/PARKING/…, so a letterless "Gate 8", "Spot 8" and "Parking 8" all compare as "8", and
    /// navdata lists its unnamed spots (the GA ramps) first — at CYEG "Spot 8" is a GA ramp 1.2 km from
    /// "Gate 8". So every match is collected (<see cref="NameMatches"/>), wherever it is, and the evidence
    /// chooses among them; list order alone would brief the ramp as the controller's gate. Stands of every
    /// kind are matched, None-type stands included (see <see cref="NoneType"/>), but one of an excluded kind
    /// only when no stand of another kind answers. Once the name has found a stand — even one that does not
    /// connect — the step ends there: the position is used only when no name or alias matched at all, and
    /// then lands on an excluded kind only when no stand of an ordinary kind is in reach. Whatever it
    /// refuses as not connecting comes back in <paramref name="refused"/>, so that the representative pick
    /// never briefs a stand the notes have just said is not used.
    /// </summary>
    private static ParkingSpot? MatchSayIntentionsGate(IReadOnlyList<ParkingSpot> spots, SayIntentionsGateHint hint,
                                                       Func<ParkingSpot, bool> hasGraphNode, List<string> notes,
                                                       DistanceUnit unit, out IReadOnlyList<ParkingSpot> refused)
    {
        refused = Array.Empty<ParkingSpot>();
        string assigned = LabelNamesItsKind(hint.Label)
            ? $"SayIntentions assigned \"{hint.Label}\""
            : $"SayIntentions assigned gate \"{hint.Label}\"";
        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(hint.Label);
        GeoPoint? pin = hint.Position;

        var named = wanted.Length > 0 ? NameMatches(spots, wanted) : new List<ParkingSpot>();
        if (named.Count > 0)
        {
            var ordered = ByEvidence(named, pin);
            var chosen = FirstConnected(ordered, pin, hasGraphNode);
            if (chosen == null)
            {
                notes.Add($"{assigned} was found{AsKind(ordered[0])} but does not connect to the taxiway network; using a representative stand instead");
                refused = named;
                return null;
            }
            return Assigned(chosen, ListedAs(chosen, hint.Label, wanted), pin, notes, unit);
        }

        if (pin is GeoPoint p)
        {
            // A stand of an ordinary kind in reach first; one of an excluded kind only when none is, as the
            // import would find it there when its list carries one (a navdata-sourced list; see ExcludedTypes).
            var inReach = spots.Where(s => !IsExcludedKind(s) && Reaches(s, p)).ToList();
            if (inReach.Count == 0) inReach = spots.Where(s => IsExcludedKind(s) && Reaches(s, p)).ToList();
            if (inReach.Count > 0)
            {
                var ordered = ByEvidence(inReach, p);
                var chosen = FirstConnected(ordered, p, hasGraphNode);
                if (chosen == null)
                {
                    notes.Add($"{assigned} was found by position{AsKind(ordered[0])} but does not connect to the taxiway network; using a representative stand instead");
                    refused = inReach;
                    return null;
                }
                // When the stand at the position does not connect, ONE sentence says what is there and what is
                // used instead — never a second note calling the stand used SayIntentions' gate — with the
                // distances that tell the two apart where this scenery calls them the same ("Gate 5", "Parking").
                string? identity = ReferenceEquals(chosen, ordered[0])
                    ? ListedAs(chosen, hint.Label, wanted)
                    : $"SayIntentions assigned {hint.Label}; the stand at its position, {IdentityLabel(ordered[0])} " +
                      $"({Away(ordered[0], p, unit)}), does not connect to the taxiway network, so {IdentityLabel(chosen)} " +
                      $"({Away(chosen, p, unit)}) is used";
                return Assigned(chosen, identity, pin, notes, unit);
            }
        }

        // With a published position the note speaks of that position, the last evidence tried.
        notes.Add(pin is GeoPoint
            ? $"{assigned}, but no stand at SayIntentions' position was found in this scenery; using a representative stand instead"
            : $"{assigned} was not found at this airport; using a representative stand instead");
        return null;
    }

    /// <summary>
    /// The stands SayIntentions' name finds, as the import's name and alias steps would find them: every stand
    /// of an ordinary kind whose identity matches, else every one whose online alias does — and only when
    /// neither finds one, the same two steps over the excluded kinds. An ordinary stand always beats an
    /// excluded one, even one the published position is nearer.
    /// </summary>
    private static List<ParkingSpot> NameMatches(IReadOnlyList<ParkingSpot> spots, string wanted)
    {
        var ordinary = Matching(spots.Where(s => !IsExcludedKind(s)), wanted);
        return ordinary.Count > 0 ? ordinary : Matching(spots.Where(IsExcludedKind), wanted);
    }

    /// <summary>Every stand whose identity matches, or when none does, every stand whose online alias does.</summary>
    private static List<ParkingSpot> Matching(IEnumerable<ParkingSpot> stands, string wanted)
    {
        var pool = stands.ToList();
        var named = pool.Where(s => SameName(IdentityLabel(s), wanted)).ToList();
        return named.Count > 0 ? named : pool.Where(s => HasAlias(s, wanted)).ToList();
    }

    /// <summary>
    /// SayIntentions' stand, with the notes the pilot needs to hear about it, in this order: how this scenery's
    /// stand relates to SayIntentions' label (<paramref name="identityNote"/>, when there is anything to say:
    /// <see cref="ListedAs"/>, or the position step's own sentence when the stand at the position did not
    /// connect); that it is of an excluded kind ("this scenery marks that stand as a fuel stand"); and how far
    /// SayIntentions' published position is from it, when that is outside the stand's own reach
    /// (<see cref="Reaches"/>), so the pilot hears that SayIntentions put its gate somewhere other than the
    /// stand the name found. A stand found by position is in reach by construction, so it never carries the
    /// last.
    /// </summary>
    private static ParkingSpot Assigned(ParkingSpot stand, string? identityNote, GeoPoint? pin, List<string> notes, DistanceUnit unit)
    {
        if (identityNote != null) notes.Add(identityNote);
        if (IsExcludedKind(stand))
            notes.Add(KindNotePrefix + ExcludedKindWords(stand));
        if (pin is GeoPoint published && !Reaches(stand, published))
            notes.Add($"SayIntentions' position is {TaxiBriefingRenderer.FormatDistance(MetresTo(stand, published), unit)} from this stand");
        return stand;
    }

    /// <summary>
    /// "SayIntentions assigned Gate 99, which this scenery lists as B 6" — whenever this scenery lists the stand
    /// under another name (an alias or a position match), so the pilot hears why the briefed stand is not called
    /// what SayIntentions called it; null when the names agree. A label that names no stand at all ("Gate")
    /// matches nothing by name, so a stand found for it was found by position alone and always gets the note —
    /// even one whose own label names no stand either ("Parking"), which would otherwise compare as the same
    /// empty name.
    /// </summary>
    private static string? ListedAs(ParkingSpot stand, string label, string wanted)
    {
        string listedAs = IdentityLabel(stand);
        return wanted.Length == 0 || !SameName(listedAs, wanted)
            ? $"SayIntentions assigned {label}, which this scenery lists as {listedAs}"
            : null;
    }

    /// <summary>"20 m away": how far the published position is from a stand, in the block's own distance words.</summary>
    private static string Away(ParkingSpot s, GeoPoint p, DistanceUnit unit) => $"{TaxiBriefingRenderer.FormatDistance(MetresTo(s, p), unit)} away";

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
    /// The first stand in <paramref name="ordered"/> that connects to the taxiway network. When the first
    /// does not, a later stand is tried only when it is at least as plausible as SayIntentions' stand
    /// (<see cref="AtLeastAsPlausible"/>). A less plausible one is a DIFFERENT stand — a same-numbered GA ramp
    /// for an unconnected gate — and briefing it as SayIntentions' gate is the failure this match exists to
    /// prevent.
    /// </summary>
    private static ParkingSpot? FirstConnected(List<ParkingSpot> ordered, GeoPoint? position,
                                               Func<ParkingSpot, bool> hasGraphNode)
    {
        var first = ordered[0];
        foreach (var s in ordered)
        {
            if (!ReferenceEquals(s, first) && !AtLeastAsPlausible(s, first, position)) continue;
            if (hasGraphNode(s)) return s;
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="later"/>, ranked below <paramref name="first"/>, is at least as plausible as
    /// SayIntentions' stand. With no published position: when it ranks the same
    /// (<see cref="RankWithoutPosition"/>; ranked by that rule, it can rank no better). Where the point is
    /// within the reach of either, the point decides: the later stand is when the point is within its own
    /// reach — two different stands can both answer to one point, and then the point cannot tell them apart,
    /// while a stand the point is outside of is no answer to it. Where the point is within the reach of
    /// neither, it cannot say which it means, so the rule for no position decides: the later stand ranks the
    /// same or better (a gate after a ramp the point was merely nearer) — with a bound: it must be no farther
    /// from the point than the first plus the first's own reach, so a same-kind namesake kilometres farther
    /// from the point than the first never stands in for it. Name matches can lie anywhere, so the first may
    /// be out of reach; "the first was out of reach too" is never enough alone, or a pin far from a gate and a
    /// same-numbered GA ramp would let the ramp stand in for the unconnected gate, which the rule for no
    /// position forbids.
    /// </summary>
    private static bool AtLeastAsPlausible(ParkingSpot later, ParkingSpot first, GeoPoint? position)
    {
        if (position is not GeoPoint p) return RankWithoutPosition(later) == RankWithoutPosition(first);
        if (Reaches(later, p) || Reaches(first, p)) return Reaches(later, p);
        return RankWithoutPosition(later) <= RankWithoutPosition(first)
            && MetresTo(later, p) <= MetresTo(first, p) + AcceptanceMetres(first);
    }

    private static double MetresTo(ParkingSpot s, GeoPoint p) =>
        TaxiGraph.FastDistanceMeters(p.Latitude, p.Longitude, s.Latitude, s.Longitude);

    /// <summary>Whether the published point is within the stand's own reach
    /// (<see cref="AcceptanceMetres"/>): what the position match accepts, and how near a stand found by name
    /// or alias must be for its distance from the point to go unsaid.</summary>
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
    private static string NoneFits(StandKind? kind, double spanMetres, bool fitsOffNetwork, DistanceUnit unit)
    {
        string noun = kind?.Noun ?? "stand";
        string span = TaxiBriefingRenderer.FormatSize(spanMetres, unit);
        return fitsOffNetwork
            ? $"no {noun} that fits a {span} wingspan connects to the taxiway network"
            : $"no {noun} at this airport is marked as fitting a {span} wingspan";
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
/// Turns SayIntentions' state into the briefing's arrival-gate hint, or null. SayIntentions assigns an ARRIVAL gate
/// only, so the hint is offered only when SayIntentions' flight is THIS OFP's flight (<see cref="IsThisFlight"/>) and a
/// gate is set. The gate is the flight file's <c>assigned_gate</c>, with the file's position; when the file has none,
/// the parking service's answer with ITS position — the order MSFS Blind Assist's SayIntentions window uses
/// (<c>SayIntentionsService.GetAssignedStatusAsync</c>), so the two never disagree. A name is never paired with the
/// other source's position. No timestamp is consulted (a stale file for the same city pair is accepted and labelled
/// as SayIntentions' assignment).
/// </summary>
public static class SayIntentionsArrivalGate
{
    /// <summary>SayIntentions is flying this flight: its file exists and its origin and destination are the flight
    /// plan's. The briefing's one test before it takes anything from SayIntentions — the gate and the runways alike.</summary>
    public static bool IsThisFlight([NotNullWhen(true)] SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao) =>
        ctx != null && ctx.FlightJsonExists &&
        IcaoEquals(ctx.Origin, departureIcao) && IcaoEquals(ctx.Destination, arrivalIcao);

    /// <summary>The flight file's gate only.</summary>
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao) =>
        From(ctx, null, departureIcao, arrivalIcao);

    /// <summary>The flight file's gate, else the parking service's.</summary>
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, SayIntentionsParking? parking,
                                              string departureIcao, string arrivalIcao)
    {
        if (!IsThisFlight(ctx, departureIcao, arrivalIcao)) return null;
        if (!string.IsNullOrWhiteSpace(ctx.AssignedGate))
            return new SayIntentionsGateHint(ctx.AssignedGate.Trim(), ctx.AssignedGatePosition);
        if (parking == null || string.IsNullOrWhiteSpace(parking.Name)) return null;
        // (0, 0) is what two absent numbers look like once read as zero — the flight-file reader refuses it too.
        GeoPoint? position = parking.Latitude is double lat && parking.Longitude is double lon && (lat != 0 || lon != 0)
            ? new GeoPoint(lat, lon)
            : null;
        return new SayIntentionsGateHint(parking.Name.Trim(), position, SayIntentionsGateSource.ParkingService);
    }

    /// <summary>What <c>SayIntentionsService.GetAssignedStatusAsync</c> returned: the flight file and, when the file had
    /// no gate, the parking service.</summary>
    public static SayIntentionsGateHint? FromStatus(SayIntentionsStatusResult? status, string departureIcao, string arrivalIcao) =>
        status == null ? null : From(status.Context, status.Parking, departureIcao, arrivalIcao);

    private static bool IcaoEquals(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
