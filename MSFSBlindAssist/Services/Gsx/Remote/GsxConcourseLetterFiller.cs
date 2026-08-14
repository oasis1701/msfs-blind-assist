using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Fills in the CONCOURSE LETTER (<see cref="ParkingSpot.Name"/>) for API-sourced stands whose
/// GSX <c>uiGateName</c> does not carry one — the Remote API path's equivalent of the name borrow
/// <c>GsxNavdataMerger.Merge</c> has always performed on the <c>.ini</c> path
/// (<c>if (string.IsNullOrEmpty(spot.Name) &amp;&amp; nav != null) spot.Name = nav.Name;</c>).
///
/// <para>
/// <b>Why the app cannot do without it.</b> <see cref="GsxRemoteParkingReader"/> derives the
/// letter from <c>uiGateName</c> alone, and at a real airport that name usually has none: of
/// KJFK's 231 selectable stands, 9 carry the letter in <c>uiGateName</c> ("Stand H6"), 91 carry
/// it only in <c>uiTerminalName</c> ("Gate 25" @ "Terminal 4 - Concourse B"), and 131 genuinely
/// have no letter at all. For that middle 91, <c>Describe()</c> renders "Gate 25 - Gate Medium,
/// Terminal 4 - Concourse B" and <c>SayIntentionsClearanceParser.NormalizeParkingName</c> reduces
/// it to "25" — while every real captured SayIntentions <c>assigned_gate</c> carries the letter
/// ("Terminal 3 Gate J1", "Terminal 2 Gate C6", "South Terminal Gate A24"), i.e. SI asks for
/// "B25". "25" != "B25", <c>MatchDestinationLabel</c> fails, and destination resolution runs its
/// whole chain to the ARRIVAL RUNWAY: a just-landed aircraft routed at the runway it landed on,
/// with the taxiway half of the import perfect so nothing else sounds wrong.
/// </para>
///
/// <para>
/// <b>Two sources, in this order, and the order is the point.</b>
/// <list type="number">
/// <item><b>Navdata, matched by POSITION.</b> The authority for stand naming everywhere else in
/// this app (the taxi graph, the SI matcher and the router all run on navdata), so it wins. The
/// match is positional because the name is exactly what is missing, and because the API's
/// <c>lat</c>/<c>lon</c> are the one field both sides state independently.</item>
/// <item><b><c>uiTerminalName</c>'s own "Concourse X" wording</b>, when navdata offered no
/// letter — no match, no navdata at all, or a matched stand that is itself letterless. It
/// recovers all 91 KJFK stands with no navdata dependency whatsoever, but it only works at
/// airports GSX happens to word that way (KJFK's other 140 stands sit under "Terminal 5",
/// "North Cargo Ramp", …), which is why it is the fallback and not the primary.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>A stand that is still letterless afterwards is a CORRECT answer, not a failure.</b> 131 of
/// KJFK's 231 stands and the whole of a live ENGM read genuinely have no concourse letter.
/// <c>Name = ""</c> stays a fully supported shape — <see cref="ParkingSpot.Describe"/> renders it
/// as "Gate 25 - …", and that is what the airport calls the stand.
/// </para>
///
/// <para>
/// <b>NAME-ONLY. Nothing else is ever imported from the matched navdata spot</b> — not the
/// coordinates, not the heading, not the radius, not the stop position. This is deliberately NOT
/// a merge: the Remote API's own values are complete and authoritative for all of those, which is
/// exactly why <c>GateDataSource.TryBuildGatesFromRemoteApi</c> does not call
/// <c>GsxNavdataMerger</c> wholesale. It also never OVERWRITES a letter GSX did supply — same
/// "only fill what is EMPTY" rule the <c>.ini</c> path applies, and the same rule the taxi-data
/// augmentation follows (navdata is authoritative; other sources only fill gaps).
/// </para>
///
/// <para>
/// <b>Pure, static, and never throws.</b> A null spot list, a null or throwing navdata provider,
/// or any malformed spot all degrade to "no letter borrowed" — i.e. to exactly the behaviour
/// before this class existed. Like <see cref="GsxStopPositionJoiner"/>, it mutates and returns
/// the SAME <see cref="ParkingSpot"/> instances it was given.
/// </para>
/// </summary>
public static class GsxConcourseLetterFiller
{
    /// <summary>
    /// How near a navdata stand must be to an API stand before its letter may be borrowed.
    ///
    /// <para>
    /// <b>Chosen from measured stand geometry, not from feel.</b> Across the 231 selectable
    /// stands of the committed KJFK capture, the two CLOSEST stands of any kind are 21.2 m apart
    /// (median nearest-neighbour separation 53.4 m; nothing at all under 15 m). 10 m is therefore
    /// under half the tightest real stand spacing measured at a dense major airport: the
    /// acceptance ball around one stand cannot reach the centre of another.
    /// </para>
    /// <para>
    /// The margin against the failure that actually matters — borrowing a letter from a stand on
    /// a DIFFERENT concourse — is far larger still, because <see cref="Fill"/> also requires the
    /// stand NUMBER to agree. In the same capture the closest pair sharing a number while
    /// differing in concourse letter is <b>227.4 m</b> apart ("Stand H12" @ Terminal 5 - Remote
    /// vs "Gate 12" @ Terminal 4 - Concourse A), so the guard has a ~22x margin on the case it
    /// exists for.
    /// </para>
    /// <para>
    /// Erring tight is deliberate and the asymmetry is not close. A radius too SMALL costs
    /// nothing worse than a stand keeping <c>Name = ""</c> — today's behaviour, and a supported
    /// shape. A radius too LARGE hands a stand its neighbour's letter, which is a WRONG STAND
    /// IDENTITY: it corrupts SayIntentions' assigned-gate match, mints a junk alias in
    /// <c>GateAliasResolver</c>, and can taxi a blind pilot to the wrong pier with every other
    /// part of the readout sounding correct.
    /// </para>
    /// </summary>
    internal const double MatchRadiusMetres = 10.0;

    /// <summary>
    /// The one wording accepted from <c>uiTerminalName</c>: the literal word "Concourse" followed
    /// by a SINGLE letter standing on its own ("Terminal 4 - Concourse B" -> "B").
    ///
    /// <para>
    /// Deliberately narrow. This fallback can only ever INVENT a letter where the primary source
    /// had none, so every widening ("Pier X", "Satellite X", a two-letter concourse) would be a
    /// guess whose failure mode is a wrong stand identity. "Concourse X" is the one form there is
    /// real captured evidence for (91 KJFK stands across four terminals). "Terminal 4" cannot
    /// match — the capture group is a letter, never a digit — and "Concourse BC" cannot either,
    /// because the trailing <c>\b</c> requires the letter to stand alone.
    /// </para>
    /// <para>
    /// <c>CultureInvariant</c> is mandatory alongside <c>IgnoreCase</c>: in tr-TR the pattern's
    /// own "I" folds to dotless "ı" and the match silently stops working, the same defect
    /// <c>SayIntentionsCultureTests</c> pins for the SayIntentions regexes.
    /// </para>
    /// </summary>
    private static readonly Regex ConcourseWording = new(
        @"\bconcourse\s+([A-Za-z])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <param name="apiSpots">
    /// The current airport's parking list as built by <see cref="GsxRemoteParkingReader"/>. Null,
    /// or containing null entries, degrades gracefully rather than throwing.
    /// </param>
    /// <param name="navdata">
    /// Supplies the SAME airport's navdata parking spots — invoked AT MOST ONCE per call, and not
    /// at all when no stand needs a letter. It is a delegate rather than a materialized list
    /// precisely so the database read can be skipped: this runs on the UI thread while a gate
    /// dropdown is being built, and an airport whose stands all carry their own letter must not
    /// pay for a query it cannot use. Null (or a delegate returning null/empty/throwing) simply
    /// means the navdata source contributes nothing and the terminal-wording fallback decides.
    /// </param>
    public static List<ParkingSpot> Fill(IReadOnlyList<ParkingSpot>? apiSpots,
                                         Func<IReadOnlyList<ParkingSpot>?>? navdata)
    {
        var result = new List<ParkingSpot>();
        if (apiSpots == null) return result;

        var needy = new List<ParkingSpot>();
        foreach (var spot in apiSpots)
        {
            if (spot == null) continue;
            result.Add(spot);
            if (NeedsLetter(spot)) needy.Add(spot);
        }

        if (needy.Count == 0) return result;   // navdata is never even asked for

        int fromNavdata = 0, ambiguous = 0;
        var donors = LoadDonors(navdata);

        if (donors.Count > 0)
        {
            foreach (var spot in needy)
            {
                string letter = BorrowFromNavdata(spot, donors, out bool wasAmbiguous);
                if (wasAmbiguous) ambiguous++;
                if (letter.Length > 0) { spot.Name = letter; fromNavdata++; }
            }
        }

        int fromTerminal = 0;
        foreach (var spot in needy)
        {
            if (!NeedsLetter(spot)) continue;  // navdata already answered for this one
            string letter = ConcourseLetterFromTerminal(spot.TerminalName);
            if (letter.Length > 0) { spot.Name = letter; fromTerminal++; }
        }

        LogSummary(needy.Count, donors.Count, fromNavdata, fromTerminal, ambiguous);
        return result;
    }

    /// <summary>
    /// A stand wants a letter only when it has none AND has a real stand NUMBER to attach one to.
    /// The number requirement is the same safety rule <c>GateAliasResolver</c> opens with
    /// (<c>if (gate.Number &lt;= 0) return result;</c>): a letter with no number is not a stand
    /// identity, nothing downstream can match on it, and — since the navdata match below
    /// corroborates on the number — a numberless stand would "agree" with every other numberless
    /// navdata spot in range.
    /// </summary>
    private static bool NeedsLetter(ParkingSpot spot)
        => string.IsNullOrWhiteSpace(spot.Name) && spot.Number > 0;

    /// <summary>
    /// The navdata spots that are eligible to DONATE a letter, read once. Everything that cannot
    /// possibly donate is filtered out here rather than re-tested per stand.
    /// <para>
    /// A donor must carry a real stand number, a real coordinate, and a <see cref="ParkingSpot.Name"/>
    /// that is a SINGLE A-Z letter. That last filter is what structurally prevents this borrow
    /// from reintroducing the very defect it exists to fix (terminal prose landing in
    /// <c>Name</c>), and it composes exactly with what navdata actually holds:
    /// <c>LittleNavMapProvider.MapParkingName</c> has already turned the MSFS <c>GATE_A</c>…
    /// <c>GATE_Z</c> enum into a bare letter ("GA" -> "A"), while every NON-concourse parking
    /// name it can produce is a WORD ("Parking", "North", "Southwest", "Dock") and is rejected
    /// here — a stand category is not a concourse and must never enter the identity slot.
    /// </para>
    /// <para>
    /// (0,0) is rejected outright: null island is a real coordinate to a distance test, and a
    /// navdata row with no position would otherwise sit 10 m from any API stand that also lacked
    /// one. NaN coordinates are rejected for the same reason (every comparison against NaN is
    /// false, so they can never match, but excluding them keeps the donor count honest).
    /// </para>
    /// </summary>
    private static List<ParkingSpot> LoadDonors(Func<IReadOnlyList<ParkingSpot>?>? navdata)
    {
        var donors = new List<ParkingSpot>();
        if (navdata == null) return donors;

        IReadOnlyList<ParkingSpot>? spots;
        try
        {
            spots = navdata();
        }
        catch (Exception ex)
        {
            // A navdata read that fails must never cost the pilot the whole API-sourced gate
            // list — the same rule the .ini stop join follows in GateDataSource. Every stand
            // simply keeps whatever letter it already had.
            Log.Debug("Gsx", $"concourse letter: navdata lookup failed, no letters borrowed: {ex.Message}");
            return donors;
        }

        if (spots == null) return donors;

        foreach (var s in spots)
        {
            if (s == null || s.Number <= 0) continue;
            if (!IsSingleLetter(s.Name)) continue;
            if (double.IsNaN(s.Latitude) || double.IsNaN(s.Longitude)) continue;
            if (s.Latitude == 0.0 && s.Longitude == 0.0) continue;
            donors.Add(s);
        }
        return donors;
    }

    /// <summary>
    /// The letter every in-range, same-numbered navdata stand agrees on — or "" when none is in
    /// range, or when two of them DISAGREE.
    /// <para>
    /// The disagreement refusal is the same guard <c>GateAliasResolver</c> applies for the same
    /// reason ("if two surviving candidates carry DIFFERENT non-empty concourse letters … the
    /// gate's real concourse is unknown, so adopting either would let the pilot 'find' gate 51 by
    /// the wrong concourse"), and it is why this returns an agreed letter rather than the nearest
    /// stand's: two navdata rows describing one physical stand (a duplicated row, a MARS pair
    /// "232N"/"232S") both name the same concourse, so agreement is the property that matters,
    /// not proximity ranking. Refusing rather than picking mirrors
    /// <see cref="GsxStopPositionJoiner"/>'s ambiguity rule.
    /// </para>
    /// </summary>
    private static string BorrowFromNavdata(ParkingSpot spot, List<ParkingSpot> donors, out bool wasAmbiguous)
    {
        wasAmbiguous = false;
        string agreed = string.Empty;

        foreach (var donor in donors)
        {
            // Number agreement is a SECOND, independent axis of evidence beside position, and it
            // is what lets the radius stay tight without losing real matches: two datasets
            // agreeing both on where a stand is and on what it is numbered is what makes it the
            // same stand. It is also how GsxNavdataMerger's own borrow has always been
            // constrained — its FindNavMatch buckets navdata by number before anything else.
            if (donor.Number != spot.Number) continue;
            if (TaxiGeo.HaversineMeters(spot.Latitude, spot.Longitude,
                                        donor.Latitude, donor.Longitude) > MatchRadiusMetres) continue;

            string letter = donor.Name.Trim().ToUpperInvariant();
            if (agreed.Length == 0) { agreed = letter; continue; }
            if (!string.Equals(agreed, letter, StringComparison.Ordinal))
            {
                wasAmbiguous = true;
                return string.Empty;   // two concourses in range: refuse, never arbitrate
            }
        }

        return agreed;
    }

    /// <summary>The single letter GSX's own <c>uiTerminalName</c> names as the concourse, or ""
    /// when it names none. See <see cref="ConcourseWording"/> for why the pattern is this narrow.</summary>
    internal static string ConcourseLetterFromTerminal(string? terminalName)
    {
        if (string.IsNullOrWhiteSpace(terminalName)) return string.Empty;
        var m = ConcourseWording.Match(terminalName);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : string.Empty;
    }

    /// <summary>ASCII A-Z, exactly one character — never <c>char.IsLetter</c>, which would admit
    /// a non-ASCII letter that no stand-id consumer in this app can compare.</summary>
    private static bool IsSingleLetter(string? value)
    {
        if (value == null) return false;
        string v = value.Trim();
        if (v.Length != 1) return false;
        char c = char.ToUpperInvariant(v[0]);
        return c >= 'A' && c <= 'Z';
    }

    /// <summary>One line per <see cref="Fill"/> call, never per stand. A stand left letterless is
    /// NOT an error (131 of KJFK's 231 genuinely have no letter), so the ordinary line is Debug;
    /// only an ambiguous navdata match — two concourses within
    /// <see cref="MatchRadiusMetres"/> of one stand, which real data should never produce — is
    /// worth a Warn, and it is folded into this same single line so it can never spam.</summary>
    private static void LogSummary(int needed, int donorCount, int fromNavdata, int fromTerminal, int ambiguous)
    {
        string summary =
            $"concourse letter: {needed} stand(s) had none; filled {fromNavdata} from navdata " +
            $"({donorCount} candidate stand(s)) and {fromTerminal} from the GSX terminal name; " +
            $"{needed - fromNavdata - fromTerminal} left without one (normal - many stands have no letter).";

        if (ambiguous > 0)
            Log.Warn("Gsx", summary + $" {ambiguous} stand(s) had TWO different navdata concourse letters " +
                            $"within {MatchRadiusMetres:0.#} m and were left alone rather than guessed at - " +
                            "check the navigation database for duplicated parking rows.");
        else
            Log.Debug("Gsx", summary);
    }
}
