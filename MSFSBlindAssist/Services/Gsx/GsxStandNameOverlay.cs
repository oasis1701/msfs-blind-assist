using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Corrects the CONCOURSE LETTER (<see cref="ParkingSpot.Name"/>) of a NAVDATA parking list from
/// the authoritative gate list, IN PLACE. Same spots, same count, same coordinates, same order —
/// only the name improves, and only where a GSX stand demonstrably describes the same stand.
///
/// <para>
/// <b>Why in-place, and not "just use the GSX list".</b> Swapping the list would fix the name and
/// break two other things, both of which come from the same root — the parking list is not only a
/// name source, it is what decides which graph nodes get marked parking:
/// <list type="number">
/// <item><b>Node typing.</b> <c>TaxiGraph.Build</c>'s parking pass writes
/// <c>node.Type = TaxiNodeType.Parking</c> as well as <c>node.ParkingName</c>, and unlike
/// <c>ParkingName</c> — which is read only by <c>DescribeLocation</c> — <c>Type</c> is read by
/// <c>NamedHoldingPointResolver</c> (which SKIPS parking nodes when snapping a named holding point,
/// a Progressive-Taxi terminator target), by <c>HoldShortNodeResolver</c>, and by the route
/// truncation in <c>TaxiGuidanceManager.Routing</c>. A different SET of spots marks a different set
/// of nodes, so a hold-short could move — and CLAUDE.md is emphatic that
/// <c>NamedHoldingPointResolver</c>'s snap behaviour was probed against real navdata and live OSM
/// at six airports and must not be re-tuned. A hold-short that moves is a runway-incursion surface,
/// not a readout.</item>
/// <item><b>Where-Am-I coverage.</b> The GSX list excludes Vehicle/Fuel stands and drops any stand
/// with no usable heading, so a pilot parked at one would have gone from hearing "Parking 21" to
/// hearing "Near taxiway X" — a regression in the very readout this exists to improve.</item>
/// </list>
/// Correcting in place removes both by construction rather than by argument: naming a stand and
/// deciding which nodes are parking are different jobs, and only the first may change.
/// </para>
///
/// <para>
/// <b>What it does and does not touch.</b> <see cref="ParkingSpot.Name"/> only. Not the
/// coordinates, not the heading, not the radius (FEET on a navdata spot and METRES on a GSX one —
/// copying one across would make every tolerance 3.28x wrong), not the type, not the jetway/VDGS
/// metadata, not <c>GsxIdentifier</c>. Nothing is ADDED to the list and nothing is REMOVED from it:
/// a navdata stand GSX does not have keeps its navdata name, and a GSX stand navdata does not have
/// is simply not added.
/// </para>
/// <para>
/// <b>Number and Suffix are deliberately NOT copied</b>, and the reason is not caution for its own
/// sake. <see cref="GsxStandLetterMatch.AgreedLetter"/> already REQUIRES the numbers to be equal
/// before it will call two rows the same stand, so copying the number could only ever be a no-op.
/// Copying the suffix would be worse than a no-op: navdata "A 24A" matched against GSX "A 24" would
/// lose its suffix and become a claim about a DIFFERENT stand — the exact wrong-stand-identity
/// failure this whole change exists to remove — and no measurement says navdata's suffix is
/// unreliable. The KJFK measurement is specifically about the LETTER, which rides in the BGL
/// parking NAME ENUM; the number and suffix come from other columns of the same row and are not
/// implicated.
/// </para>
///
/// <para>
/// <b>Overwriting a non-empty navdata name is the point, and it is a MEASURED ruling</b>, not a new
/// one — the same one <see cref="Gsx.Remote.GsxConcourseLetterFiller"/> already applies in the
/// opposite direction. Resolved over all 222 letterless KJFK stands against the real fs2024
/// navdata: 32 agree, <b>46 DISAGREE</b>, and GSX is right in every sampled case. Navdata calls
/// "Gate 25" at "Terminal 4 - Concourse B" concourse <b>A</b>, while the real KJFK Terminal 4 is
/// Concourse A (A2-A7) and Concourse B (B20-B41) — so it is <b>B25</b>, which is what a controller
/// and SayIntentions say. The cause is specific: navdata's letter comes from the BGL parking NAME
/// ENUM (<c>GATE_A</c>…<c>GATE_Z</c>) and that field is whatever the scenery author set — at KJFK
/// uniformly <c>GATE_A</c> across a whole concourse. So navdata stays authoritative for stand
/// GEOMETRY (nothing here touches any of it) and is demonstrably NOT authoritative for the
/// concourse LETTER. Do not flip this back on general "navdata is authoritative" grounds.
/// </para>
///
/// <para>
/// <b>Degrades to nothing whenever there is nothing better.</b> When GSX is not running,
/// <c>GateDataSource.GetGates</c> falls through to its <c>.ini</c>/navdata path whose last fallback
/// is literally <c>GetParkingSpots</c> — so the gate list IS the navdata list, every match is a
/// stand against itself, and every letter it agrees on is the one already there. A null or empty
/// gate list, a null spot list, or any malformed row all leave the list exactly as it arrived.
/// </para>
/// </summary>
public static class GsxStandNameOverlay
{
    /// <summary>
    /// Corrects <paramref name="navdataSpots"/> in place and returns the SAME list instance.
    ///
    /// <para>
    /// Safe to mutate: every <c>IAirportDataProvider.GetParkingSpots</c> implementation builds
    /// fresh <see cref="ParkingSpot"/> objects per call from the database, so this can never write
    /// through into a cached list somebody else is holding. <paramref name="authoritativeGates"/>
    /// is only ever READ — it may well be a list <c>GateDataSource</c> has cached.
    /// </para>
    /// </summary>
    public static List<ParkingSpot> Apply(List<ParkingSpot>? navdataSpots,
                                          IReadOnlyList<ParkingSpot>? authoritativeGates)
    {
        var spots = navdataSpots ?? new List<ParkingSpot>();
        if (spots.Count == 0 || authoritativeGates == null || authoritativeGates.Count == 0)
            return spots;

        var donors = GsxStandLetterMatch.EligibleDonors(authoritativeGates);
        if (donors.Count == 0) return spots;

        int corrected = 0, confirmed = 0, ambiguous = 0;
        List<string>? changes = null;

        foreach (var spot in spots)
        {
            if (spot == null || spot.Number <= 0) continue;
            if (double.IsNaN(spot.Latitude) || double.IsNaN(spot.Longitude)) continue;
            if (spot.Latitude == 0.0 && spot.Longitude == 0.0) continue;

            string letter = GsxStandLetterMatch.AgreedLetter(spot, donors, out bool wasAmbiguous);
            if (wasAmbiguous) ambiguous++;
            if (letter.Length == 0) continue;

            string before = (spot.Name ?? string.Empty).Trim();
            if (string.Equals(before, letter, StringComparison.OrdinalIgnoreCase)) { confirmed++; continue; }

            // A navdata name that is NOT a bare concourse letter is a stand CATEGORY ("Parking",
            // "North", "Dock" — LittleNavMapProvider.MapParkingName's other outputs) rather than a
            // competing identity claim. Filling one in is the same "only fill what is EMPTY" borrow
            // the .ini path has always done; replacing one would be asserting that a GA parking
            // spot is really gate B, which no measurement supports.
            if (before.Length > 0 && !GsxStandLetterMatch.IsSingleLetter(before)) continue;

            (changes ??= new List<string>()).Add(
                before.Length == 0
                    ? $"{spot.Number}{spot.Suffix}: (none)->{letter}"
                    : $"{before} {spot.Number}{spot.Suffix}->{letter} {spot.Number}{spot.Suffix}");
            spot.Name = letter;
            corrected++;
        }

        LogSummary(spots.Count, donors.Count, corrected, confirmed, ambiguous, changes);
        return spots;
    }

    /// <summary>
    /// ONE line per call, plus a detail line only when something actually changed. A stand left
    /// alone is the overwhelmingly normal case (most stands have no letter anywhere, and most that
    /// do already agree), so it is never worth a line of its own. The detail line carries the WHOLE
    /// change list rather than a truncated sample, because its entire job is to answer "why is this
    /// stand called B?" for a stand somebody asks about later — the same reasoning, and the same
    /// shape, as <c>GsxConcourseLetterFiller.LogSummary</c>.
    /// </summary>
    private static void LogSummary(int spotCount, int donorCount, int corrected, int confirmed,
                                   int ambiguous, List<string>? changes)
    {
        string summary =
            $"stand names: {spotCount} navdata stand(s) checked against {donorCount} authoritative " +
            $"stand(s); {corrected} corrected, {confirmed} already agreed, " +
            $"{spotCount - corrected - confirmed} left alone (normal - no matching GSX stand, or no letter anywhere).";

        if (ambiguous > 0)
            Log.Warn("Gsx", summary + $" {ambiguous} stand(s) had TWO different concourse letters within " +
                            $"{GsxStandLetterMatch.MatchRadiusMetres:0.#} m and were left alone rather than " +
                            "guessed at - check the gate list for duplicated stands.");
        else
            Log.Debug("Gsx", summary);

        if (changes is { Count: > 0 })
            Log.Debug("Gsx", $"stand names: {changes.Count} corrected from the authoritative gate list: " +
                             string.Join("; ", changes));
    }
}
