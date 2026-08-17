using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Joins GSX's own <c>.ini</c> profile stop positions (<c>parkingsystem_stopposition</c>) onto
/// the API-sourced parking list built by <see cref="GsxRemoteParkingReader"/>. Also recovers a
/// heading <see cref="GsxRemoteParkingReader"/> could not publish (see "Heading recovery"
/// below). This is the safety-critical half of Spec 2's list work: docking guidance parks the
/// aircraft on <see cref="ParkingSpot.StopLatitude"/>/<see cref="ParkingSpot.StopLongitude"/>
/// with a 0.3 m tolerance (<c>DockingGeometry.StopToleranceMetres</c>).
///
/// <para>
/// <b>Why this exists at all.</b> The GSX Remote API publishes <c>stopPosition</c>/
/// <c>objectPosition</c> as null on every single stand (0/238 at a live KJFK capture) —
/// Virtuali's own guide states <c>gate.setStopPositionOffset(...)</c> "is not callable through
/// this verb; it writes data properties only." There is no way to obtain a stop position from
/// the API at all. The <c>.ini</c> profile still carries <c>parkingsystem_stopposition</c> on
/// the large majority of gates (227/231 at KJFK), so it remains the only source.
/// </para>
///
/// <para>
/// <b>The join key is the coordinate, and it is exact.</b> The API's <c>lat</c>/<c>lon</c> are
/// byte-identical to the <c>.ini</c>'s <c>this_parking_pos</c> — verified at KJFK's
/// "[gate a 6]": API <c>40.6421016650217 / -73.7787394243692</c> against <c>.ini</c>
/// <c>this_parking_pos = 40.6421016650217 -73.7787394243692 26.3036148834228</c>. That SAME
/// gate's <c>parkingsystem_stopposition</c> is <c>40.6421951021146 -73.7786780495867
/// 26.3036148834228</c> — 11.62 m away from <c>this_parking_pos</c>, on a bearing of ~26.49°
/// that lies along the stand's own 26.30° heading (i.e. straight down the stand's axis, exactly
/// what a real nose-in stop offset should look like). Those two points are <b>never</b>
/// interchangeable: substituting the parking position for the stop would drive the aircraft
/// datum ~11.6 m into the stand, far outside docking's 0.3 m tolerance. So the match is tried as
/// exact <c>double</c> equality first; only when that fails does a sub-metre tolerance (1e-6°,
/// ~0.11 m, applied independently to each axis) apply — and every tolerance hit is logged,
/// because a SYSTEMATIC tolerance fallback would mean the coordinate-identity assumption this
/// whole join rests on has broken, and that must be visible, not silently absorbed.
/// </para>
///
/// <para>
/// <b>Ambiguity is refused, not guessed (2026-08-14 review fix).</b> If MORE THAN ONE
/// <c>.ini</c> gate has the exact same <c>this_parking_pos</c> as a spot's API coordinate — a
/// copy-pasted <c>.ini</c> section with an unedited <c>this_parking_pos</c> but an edited
/// <c>parkingsystem_stopposition</c> would look exactly like this — this join cannot tell which
/// one is real. It logs every candidate section name (<see cref="Log.Warn"/>) and leaves the
/// stop, and any <see cref="double.NaN"/> heading, untouched — exactly the same degrade as no
/// match at all, never an arbitrary pick. This never falls through to the tolerance path either:
/// every exact match is, trivially, also within tolerance, so searching the same ambiguous set
/// again there would just pick one of them by a distance tie-break instead of an informed
/// choice — no better than guessing.
/// </para>
///
/// <para>
/// <b>Heading recovery.</b> <c>this_parking_pos</c> is <c>lat lon heading</c>. GSX's own
/// <c>handlerData</c> omits <c>heading</c> on at least one real, otherwise-selectable stand
/// (KJFK "Gate 1A", Terminal 8 - Concourse B, Gate Heavy) — <see cref="GsxRemoteParkingReader"/>
/// keeps that stand rather than dropping it, publishing it with
/// <see cref="ParkingSpot.Heading"/> = <see cref="double.NaN"/> (see
/// <see cref="GsxRemoteParkingReader.HasUsableHeading"/>). When the SAME coordinate match used
/// for the stop position finds an unambiguous <c>.ini</c> gate, its <c>this_parking_pos</c>
/// heading fills that gap. This ONLY ever replaces a <see cref="double.NaN"/> — a heading the
/// API DID publish is real data and is never overwritten by the <c>.ini</c>'s.
/// </para>
///
/// <para>
/// <b>Degradation is graceful and matches today.</b> No <c>.ini</c> for the airport, no
/// coordinate match, an ambiguous match, or a matched <c>.ini</c> gate with no
/// <c>parkingsystem_stopposition</c> all leave <see cref="ParkingSpot.StopLatitude"/>/
/// <see cref="ParkingSpot.StopLongitude"/>/<see cref="ParkingSpot.StopHeading"/> null — exactly
/// the state a navdata-only stand is in today. This method never writes
/// <see cref="ParkingSpot.Latitude"/>/<see cref="ParkingSpot.Longitude"/> under any
/// circumstance, and never derives a stop field from them.
/// </para>
///
/// <para>
/// <b>A systematic miss is loud, not just a partial one (2026-08-14 review fix).</b>
/// <see cref="Join"/> logs exactly ONE summary line per call — never per spot, which would be
/// noise. If no <c>.ini</c> candidate gates were available at all, that is expected and normal
/// (a <c>.py</c>-only profile such as EDDF has no <c>.ini</c> to parse) and logs at Debug. If
/// <c>.ini</c> gates WERE available but the coordinate join matched none of them, the exact/
/// tolerance coordinate-identity assumption has broken for this WHOLE airport — every stand
/// loses its stop, and docking guidance silently degrades a null stop to the parking position
/// (many metres from the true stop) with nothing anywhere to explain why — so that case logs at
/// Warn. Otherwise the match count (which may be a partial N of M) is logged at Debug, for
/// visibility into which case applied without being alarming on its own.
/// </para>
///
/// <para>
/// <b>Pure, static, and never throws.</b> No I/O, no GSX calls — the two lists in, the joined
/// list out. A null/empty <c>.ini</c> list, an <c>.ini</c> gate with no real
/// <c>this_parking_pos</c>, or any other malformed input all degrade to an unmatched
/// (stop-still-null) spot rather than throwing. <see cref="Join"/> mutates and returns the SAME
/// <see cref="ParkingSpot"/> instances it was given (no defensive copy — <see cref="ParkingSpot"/>
/// has no clone method, and every field this join can touch is documented above); callers should
/// treat <paramref name="apiSpots"/>'s instances as consumed by the call, matching how
/// <c>GateDataSource.TryBuildGatesFromRemoteApi</c> — the one call site — immediately reassigns
/// its own <c>spots</c> local to this method's result rather than reading the parameter again.
/// </para>
/// </summary>
public static class GsxStopPositionJoiner
{
    /// <summary>
    /// Sub-metre fallback tolerance, in degrees, applied independently to latitude and
    /// longitude ONLY when an exact double-equality match fails. ~0.11 m at these latitudes.
    /// </summary>
    private const double ToleranceDegrees = 1e-6;

    /// <param name="apiSpots">
    /// The current airport's parking list, as built by <see cref="GsxRemoteParkingReader"/>.
    /// Null or containing null entries degrades gracefully (null items are skipped; a null list
    /// returns an empty result) rather than throwing.
    /// </param>
    /// <param name="iniGates">
    /// The same airport's parsed <c>.ini</c> profile (<c>GsxProfileParser.Parse</c>), or null/
    /// empty when no <c>.ini</c> exists for this airport (e.g. a <c>.py</c>-only profile such as
    /// EDDF) — every spot's stop fields are then left exactly as <paramref name="apiSpots"/>
    /// already had them (null, per <see cref="GsxRemoteParkingReader"/>).
    /// </param>
    public static List<ParkingSpot> Join(IReadOnlyList<ParkingSpot>? apiSpots, IReadOnlyList<GsxGate>? iniGates)
    {
        var result = new List<ParkingSpot>();
        if (apiSpots == null) return result;

        List<GsxGate> candidates = BuildCandidates(iniGates);
        int attempted = 0, matched = 0;

        foreach (var spot in apiSpots)
        {
            if (spot == null) continue;
            attempted++;
            try
            {
                if (JoinOne(spot, candidates)) matched++;
            }
            catch (Exception ex)
            {
                // Defensive backstop, same idiom as GsxRemoteParkingReader.Read's per-entry
                // try/catch: nothing below this point should realistically be able to throw
                // (plain double comparisons and property assignments on plain C# objects), but
                // one unexpected failure joining a single spot must never take the rest of the
                // list — or the whole gate dropdown — down with it. The spot is still added to
                // the result below, un-joined (stop stays whatever it already was, i.e. null) —
                // the safe degrade is "this one stand behaves like a navdata-only stand", never
                // "this stand silently vanishes from the list".
                Log.Debug("Gsx", $"stop-position join: skipped one spot ({ex.Message}).");
            }
            result.Add(spot);
        }

        LogSummary(attempted, candidates.Count, matched);
        return result;
    }

    private static List<GsxGate> BuildCandidates(IReadOnlyList<GsxGate>? iniGates)
    {
        var candidates = new List<GsxGate>();
        if (iniGates == null) return candidates;
        foreach (var g in iniGates)
        {
            // A .ini section with no this_parking_pos line leaves GsxGate.Latitude/Longitude at
            // their default 0/0 (HasParkingPos=false) -- that is NOT a real coordinate, and
            // treating it as one risks a spurious match against any API spot that itself sits
            // near (0,0). Only gates with a REAL this_parking_pos are ever join candidates.
            if (g != null && g.HasParkingPos) candidates.Add(g);
        }
        return candidates;
    }

    /// <returns>
    /// true when an UNAMBIGUOUS coordinate match (a single exact hit, or a tolerance hit) was
    /// found for this spot — regardless of whether that matched gate actually had a usable stop
    /// position. Used only to build <see cref="Join"/>'s per-call summary; an ambiguous exact
    /// match counts as NOT matched, since nothing was actually applied for it.
    /// </returns>
    private static bool JoinOne(ParkingSpot spot, List<GsxGate> candidates)
    {
        List<GsxGate> exact = FindAllExact(spot, candidates);
        if (exact.Count > 1)
        {
            LogAmbiguousExactMatch(spot, exact);
            return false; // same degrade as no match: stop and heading both left untouched
        }

        GsxGate? match;
        if (exact.Count == 1)
        {
            match = exact[0];
        }
        else
        {
            match = FindNearestWithinTolerance(spot, candidates);
            if (match != null) LogToleranceHit(spot, match);
        }

        if (match == null) return false; // no .ini, no match -> stop (and any NaN heading) stays exactly as it was

        ApplyMatch(spot, match);
        return true;
    }

    private static void ApplyMatch(ParkingSpot spot, GsxGate match)
    {
        // Stop position: ONLY from parkingsystem_stopposition, ONLY when the matched gate
        // actually has one, and NEVER from spot.Latitude/Longitude or match.Latitude/Longitude.
        // Checked as a pair rather than leaning on GsxProfileParser always setting
        // StopLatitude/StopLongitude/StopHeading together -- that invariant holds for
        // .ini-parsed gates, but this method also has to be correct for any GsxGate a future
        // caller (or a test) builds by hand.
        if (match.StopLatitude.HasValue && match.StopLongitude.HasValue)
        {
            spot.StopLatitude = match.StopLatitude;
            spot.StopLongitude = match.StopLongitude;
            spot.StopHeading = match.StopHeading;
        }

        // Heading recovery: only ever fills a gap GsxRemoteParkingReader left as NaN. A heading
        // the API DID publish is real data and must never be replaced by the .ini's -- this is
        // recovery for the one field the API omitted, not a second opinion on a field it gave.
        if (!GsxRemoteParkingReader.HasUsableHeading(spot))
            spot.Heading = match.Heading;
    }

    /// <summary>Every candidate whose this_parking_pos is IEEE754-equal to the spot's lat/lon —
    /// not just the first, so <see cref="JoinOne"/> can detect and refuse an ambiguous
    /// multi-match rather than silently arbitrating it by list order.</summary>
    private static List<GsxGate> FindAllExact(ParkingSpot spot, List<GsxGate> candidates)
    {
        var matches = new List<GsxGate>();
        foreach (var g in candidates)
            if (g.Latitude == spot.Latitude && g.Longitude == spot.Longitude)
                matches.Add(g);
        return matches;
    }

    /// <summary>Nearest candidate whose this_parking_pos is within <see cref="ToleranceDegrees"/>
    /// on BOTH axes -- only ever consulted after an exact search has already failed (zero hits;
    /// two or more is handled as an ambiguity refusal, not routed here). "Nearest" (by squared
    /// coordinate distance, sufficient for ranking within a ~0.11 m box) rather than "first
    /// within tolerance" so two candidates that both happen to fall inside the tolerance box of
    /// one spot can never pick the farther-but-earlier-iterated one.</summary>
    private static GsxGate? FindNearestWithinTolerance(ParkingSpot spot, List<GsxGate> candidates)
    {
        GsxGate? best = null;
        double bestScore = double.MaxValue;
        foreach (var g in candidates)
        {
            double dLat = Math.Abs(g.Latitude - spot.Latitude);
            double dLon = Math.Abs(g.Longitude - spot.Longitude);
            if (dLat > ToleranceDegrees || dLon > ToleranceDegrees) continue;

            double score = dLat * dLat + dLon * dLon;
            if (score < bestScore) { bestScore = score; best = g; }
        }
        return best;
    }

    private static void LogToleranceHit(ParkingSpot spot, GsxGate match)
    {
        double dLat = match.Latitude - spot.Latitude;
        double dLon = match.Longitude - spot.Longitude;
        Log.Warn("Gsx",
            $"stop-position join: \"{spot.GsxIdentifier ?? spot.Name}\" ({spot.AirportICAO}) " +
            $"matched .ini gate \"{match.RawSectionName}\" only within the {ToleranceDegrees:G3}-degree " +
            $"tolerance (dLat={dLat:G6}, dLon={dLon:G6}) -- exact lat/lon equality failed. A systematic " +
            "tolerance fallback means the API/.ini coordinate-identity assumption this join rests on has " +
            "broken; investigate rather than trust silently.");
    }

    private static void LogAmbiguousExactMatch(ParkingSpot spot, List<GsxGate> matches)
    {
        string names = string.Join(", ", matches.Select(m => $"\"{m.RawSectionName}\""));
        Log.Warn("Gsx",
            $"stop-position join: \"{spot.GsxIdentifier ?? spot.Name}\" ({spot.AirportICAO}) matched " +
            $"{matches.Count} .ini gates at the exact same this_parking_pos coordinate ({names}) -- " +
            "cannot tell which stop position is real, so the stop (and any recoverable heading) is left " +
            "unset for this stand, same as no match at all. Check the .ini profile for a duplicated or " +
            "copy-pasted section.");
    }

    /// <summary>One line per <see cref="Join"/> call (never per spot) distinguishing "no .ini
    /// available" (normal) from "an .ini exists but nothing matched" (the coordinate-identity
    /// assumption has broken for this whole airport, and every stand silently loses its stop
    /// unless this line exists) from an ordinary partial/full match count.</summary>
    private static void LogSummary(int attempted, int candidateCount, int matched)
    {
        if (attempted == 0) return; // nothing to summarize (e.g. an empty apiSpots list)

        if (candidateCount == 0)
        {
            Log.Debug("Gsx",
                $"stop-position join: no .ini candidate gates available -- {attempted} spot(s) left " +
                "without a stop position (same as a navdata-only stand). Normal for an airport with no " +
                ".ini profile (e.g. a .py-only profile such as EDDF).");
            return;
        }

        if (matched == 0)
        {
            Log.Warn("Gsx",
                $"stop-position join: .ini profile has {candidateCount} candidate gate(s) but the " +
                $"coordinate join matched 0 of {attempted} spot(s) -- the API/.ini coordinate-identity " +
                "assumption this join rests on appears to have broken for this ENTIRE airport. Docking " +
                "guidance will fall back to the parking position at every affected stand, many metres " +
                "from the true stop. Investigate.");
            return;
        }

        Log.Debug("Gsx",
            $"stop-position join: matched {matched} of {attempted} spot(s) to a .ini stop position " +
            $"({candidateCount} candidate gate(s)).");
    }
}
