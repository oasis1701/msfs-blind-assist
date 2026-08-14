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
/// <b>Two sources, in this order, and the order is MEASURED — do not flip it back.</b>
/// <list type="number">
/// <item><b><c>uiTerminalName</c>'s own "Concourse X" wording.</b> GSX authors that string to
/// describe the terminal layout, and at KJFK it is right.</item>
/// <item><b>Navdata, matched by POSITION</b>, when the terminal names no concourse. The match is
/// positional because the name is exactly what is missing, and because the API's <c>lat</c>/
/// <c>lon</c> are the one field both sides state independently.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why the terminal leads, against the usual "navdata is authoritative" instinct.</b> The two
/// sources were resolved for all 222 letterless KJFK stands against the real fs2024 navdata:
/// 32 agree, <b>46 DISAGREE</b>, 52 are navdata-only, 13 terminal-only, 79 stay letterless. Every
/// sampled disagreement has GSX right and navdata wrong — navdata calls "Gate 25", "Gate 27",
/// "Gate 29B" and "Gate 31" at "Terminal 4 - Concourse B" concourse <b>A</b>, while the real
/// KJFK Terminal 4 is Concourse A (A2-A7) and Concourse B (B20-B41), so gate 25 is <b>B25</b> —
/// which is what a controller, and SayIntentions, say.
/// </para>
/// <para>
/// The reason is specific, not a general indictment: navdata's letter comes from the BGL parking
/// NAME ENUM (<c>GATE_A</c>…<c>GATE_Z</c>, which <c>LittleNavMapProvider.MapParkingName</c>
/// reduces to a bare letter), and that field is whatever the scenery author set — at KJFK
/// uniformly <c>GATE_A</c> across a whole concourse. So navdata stays authoritative for stand
/// GEOMETRY (position, heading, radius — nothing here touches any of it) and is demonstrably NOT
/// authoritative for the concourse LETTER. That is a measured exception to a real principle, not
/// a contradiction of it. Navdata-FIRST produced the wrong letter for 46 of 222 stands, i.e. it
/// reproduced a navdata-quality problem instead of fixing it, and the whole purpose of
/// <see cref="ParkingSpot.Name"/> is to equal what ATC calls the stand.
/// </para>
/// <para>
/// Neither source is dropped: the terminal wording only works at airports GSX words that way
/// (KJFK's other 140 stands sit under "Terminal 5", "North Cargo Ramp", …), and navdata is the
/// only source for the 52 stands the terminal says nothing about. A disagreement is LOGGED with
/// both letters (see <see cref="LogSummary"/>) so "why is this stand called B?" is answerable
/// from the log without re-deriving any of the above.
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
    /// How near a navdata stand must be to an API stand before its letter may be borrowed — and
    /// the SAME radius <see cref="GsxStandNameOverlay"/> uses in the opposite direction. Both the
    /// value and the measurement behind it live on <see cref="GsxStandLetterMatch"/>; read that
    /// before touching it. Kept here as a forwarding alias because this class's own doc comments
    /// and log lines reference it.
    /// </summary>
    internal const double MatchRadiusMetres = GsxStandLetterMatch.MatchRadiusMetres;

    /// <summary>
    /// The one wording accepted from <c>uiTerminalName</c>: the literal word "Concourse" followed
    /// by a SINGLE letter standing on its own ("Terminal 4 - Concourse B" -> "B").
    ///
    /// <para>
    /// Deliberately narrow, and the narrowness is what makes it safe to put FIRST. This source
    /// can INVENT a letter — it asserts one from prose rather than reading it off a stand — so
    /// every widening ("Pier X", "Satellite X", a two-letter concourse) would be a guess whose
    /// failure mode is a wrong stand identity. "Concourse X" is the one form there is real
    /// captured evidence for (91 KJFK stands across four terminals, and correct on every sampled
    /// one). "Terminal 4" cannot match — the capture group is a letter, never a digit — and
    /// "Concourse BC" cannot either, because the trailing <c>\b</c> requires the letter to stand
    /// alone.
    /// </para>
    /// <para>
    /// The residual risk this leaves, stated rather than hidden: at an airport that letters its
    /// CONCOURSES but not its GATES, this would render "B 25" while the controller says plain
    /// "Gate 25" — the same class of failure being fixed here, pointed the other way, and it got
    /// slightly larger when this source was promoted to first. No such airport appears in any
    /// capture we hold, GSX wording a terminal "Concourse X" is itself a statement about how its
    /// stands are named, and every real captured SayIntentions <c>assigned_gate</c> carries the
    /// letter — but the mitigation is this pattern's strictness, so do not relax it.
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

        var donors = LoadDonors(navdata);
        int fromTerminal = 0, fromNavdata = 0, agreed = 0, ambiguous = 0;
        List<string>? conflicts = null;

        foreach (var spot in needy)
        {
            string terminalLetter = ConcourseLetterFromTerminal(spot.TerminalName);

            // Navdata is resolved even when the terminal has already answered — not to override
            // it, but so a DISAGREEMENT can be logged. That is the whole diagnostic trail behind
            // "why is this stand called B", and it costs nothing extra: the same scan already
            // ran for every letterless stand when navdata led.
            bool wasAmbiguous = false;
            string navdataLetter = donors.Count > 0
                ? BorrowFromNavdata(spot, donors, out wasAmbiguous)
                : string.Empty;
            if (wasAmbiguous) ambiguous++;

            if (terminalLetter.Length > 0 && navdataLetter.Length > 0)
            {
                if (string.Equals(terminalLetter, navdataLetter, StringComparison.Ordinal)) agreed++;
                else (conflicts ??= new List<string>()).Add(
                    $"\"{spot.GsxIdentifier ?? spot.Name}\" @ \"{spot.TerminalName}\" GSX={terminalLetter} navdata={navdataLetter}");
            }

            // The terminal WINS outright when it names a concourse — see the type's own doc
            // comment for the KJFK measurement behind that order (46 of 222 stands disagree, GSX
            // correct in every sampled case). Navdata still answers for every stand the terminal
            // says nothing about (52 of 222 at KJFK).
            if (terminalLetter.Length > 0) { spot.Name = terminalLetter; fromTerminal++; }
            else if (navdataLetter.Length > 0) { spot.Name = navdataLetter; fromNavdata++; }
        }

        LogSummary(needy.Count, donors.Count, fromTerminal, fromNavdata, agreed, conflicts, ambiguous);
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
    /// The navdata spots that are eligible to DONATE a letter, read once. The eligibility rule
    /// itself lives on <see cref="GsxStandLetterMatch.EligibleDonors"/> (shared with the reverse
    /// direction); all this adds is reading the delegate safely.
    /// </summary>
    private static List<ParkingSpot> LoadDonors(Func<IReadOnlyList<ParkingSpot>?>? navdata)
    {
        if (navdata == null) return new List<ParkingSpot>();

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
            return new List<ParkingSpot>();
        }

        return GsxStandLetterMatch.EligibleDonors(spots);
    }

    /// <summary>
    /// The letter every in-range, same-numbered navdata stand agrees on — or "" when none is in
    /// range, or when two of them DISAGREE (<see cref="GsxStandLetterMatch.AgreedLetter"/>, the
    /// rule shared with the reverse direction; its doc carries the reasoning). Resolved for EVERY
    /// letterless stand, including ones the terminal wording has already answered, because
    /// <see cref="Fill"/> logs the disagreement between the two; it is only USED when the terminal
    /// named no concourse (52 of KJFK's 222 letterless stands).
    /// </summary>
    private static string BorrowFromNavdata(ParkingSpot spot, List<ParkingSpot> donors, out bool wasAmbiguous)
        => GsxStandLetterMatch.AgreedLetter(spot, donors, out wasAmbiguous);

    /// <summary>The single letter GSX's own <c>uiTerminalName</c> names as the concourse, or ""
    /// when it names none. See <see cref="ConcourseWording"/> for why the pattern is this narrow.</summary>
    internal static string ConcourseLetterFromTerminal(string? terminalName)
    {
        if (string.IsNullOrWhiteSpace(terminalName)) return string.Empty;
        var m = ConcourseWording.Match(terminalName);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : string.Empty;
    }

    /// <summary>
    /// At most TWO lines per <see cref="Fill"/> call, never one per stand: a summary, plus the
    /// disagreement detail when there is any.
    /// <para>
    /// A stand left letterless is NOT an error (131 of KJFK's 231 genuinely have no letter), and
    /// neither is a disagreement — 46 of 222 at KJFK, which is why the detail line is Debug and
    /// carries the whole list on ONE line rather than 46 lines or a truncated sample: its entire
    /// job is to answer "why is this stand called B?" for a stand somebody asks about later, and
    /// a capped list cannot do that. Only an ambiguous NAVDATA match — two concourses within
    /// <see cref="MatchRadiusMetres"/> of one stand, which real data should not produce — is
    /// worth a Warn, and it is folded into the summary line so it can never spam.
    /// </para>
    /// </summary>
    private static void LogSummary(int needed, int donorCount, int fromTerminal, int fromNavdata,
                                   int agreed, List<string>? conflicts, int ambiguous)
    {
        int conflicted = conflicts?.Count ?? 0;
        string summary =
            $"concourse letter: {needed} stand(s) had none; filled {fromTerminal} from the GSX terminal name " +
            $"and {fromNavdata} from navdata ({donorCount} candidate stand(s)); " +
            $"{needed - fromTerminal - fromNavdata} left without one (normal - many stands have no letter). " +
            $"Both sources answered for {agreed + conflicted}: {agreed} agreed, {conflicted} disagreed " +
            "(the GSX terminal name wins - see GsxConcourseLetterFiller for the measurement behind that).";

        if (ambiguous > 0)
            Log.Warn("Gsx", summary + $" {ambiguous} stand(s) had TWO different navdata concourse letters " +
                            $"within {MatchRadiusMetres:0.#} m and were left alone rather than guessed at - " +
                            "check the navigation database for duplicated parking rows.");
        else
            Log.Debug("Gsx", summary);

        if (conflicted > 0)
            Log.Debug("Gsx", $"concourse letter: {conflicted} stand(s) where GSX and navdata name different " +
                             $"concourses; GSX won each: {string.Join("; ", conflicts!)}");
    }
}
