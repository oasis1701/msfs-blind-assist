using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The ONE place the app decides what a stand is CALLED. Every readout a pilot can hear a stand
/// named in — the taxi dialog's destination combo, the gate-teleport list, <c>gate.select</c>,
/// Where-Am-I, and SayIntentions' "are you at your assigned gate" check — resolves its parking
/// list through here, so a stand cannot be called two different things in one session.
///
/// <para>
/// <b>The defect this exists to prevent.</b> Before this seam, the dialogs went through
/// <see cref="GateDataSource.GetGates"/> (GSX's own gate list, plus
/// <c>GsxConcourseLetterFiller</c>) while everything else called
/// <see cref="IAirportDataProvider.GetParkingSpots"/> directly. At KJFK Terminal 4 those two
/// disagree: GSX says <b>B 25</b> — what a controller and SayIntentions say — and navdata says
/// <b>A 25</b>, because navdata's letter rides in the BGL parking NAME ENUM that KJFK's scenery
/// fills uniformly <c>GATE_A</c> across a whole concourse (measured: navdata and GSX disagree on
/// 46 of 222 letterless stands, GSX right in every sampled case; see
/// <c>GsxConcourseLetterFiller</c>'s own doc comment for the numbers). One of those consumers is
/// not cosmetic: <c>MainForm.NearestSpotMatchesAssignedGate</c> compares the nearest stand's name
/// against SayIntentions' assigned gate, so an aircraft parked EXACTLY at B25 was told
/// <i>"Aircraft appears near A 25, not assigned gate Terminal 4 Gate B25."</i> — a confident,
/// wrong claim that a blind pilot is at the wrong stand.
/// </para>
///
/// <para>
/// <b>ALIASES ARE PART OF THE NAME, which is why they are attached here and not by the caller.</b>
/// <see cref="AugmentingAirportDataProvider.GetParkingSpots"/> already aliases the navdata list on
/// its way out, so a caller that took the GSX list INSTEAD would silently get a list with no
/// aliases at all — and the alias is a real matching leg, not decoration: live KDTW names the
/// stand <c>A 24A</c> where SayIntentions, OSM and the controller all say <c>A24</c>, and it is
/// the alias that makes those the same stand. <see cref="AugmentingAirportDataProvider.AugmentParking"/>
/// is public for exactly this reason (see its own doc comment) and is alias-ONLY: it never
/// overwrites a Name or a position and never adds a selectable gate.
/// </para>
///
/// <para>
/// <b>When GSX is not running, nothing here changes anything.</b>
/// <see cref="GateDataSource.GetGates"/> falls through to its <c>.ini</c>/navdata path — and its
/// last fallback is literally <c>GetParkingSpots</c> — so this seam only ever engages where a
/// better name actually exists. A null <paramref name="gateSource"/> (no database, no GSX service
/// wired) degrades to exactly the pre-seam call.
/// </para>
///
/// <para>
/// <b>Call it where a graph is BUILT or a dialog is opened, never on a position update.</b>
/// <see cref="GateDataSource"/> caches per ICAO, but a caller that constructs a fresh one per
/// call (the safe choice when the supplier can run off the UI thread — see
/// <c>TaxiGuidanceManager.ParkingSpotSupplier</c>) pays a directory listing and, on the GSX
/// Remote API path, a JSON read plus one navdata query. That is nothing beside a
/// <c>TaxiGraph.Build</c>, and unacceptable per frame.
/// </para>
/// </summary>
public static class ParkingSpotSource
{
    /// <summary>
    /// The airport's parking list under its authoritative names, with this scenery's online
    /// aliases attached.
    ///
    /// <para>
    /// Never throws: a <see cref="GateDataSource.GetGates"/> failure is already swallowed inside
    /// that method (it returns navdata instead), and an augmentation failure would only cost the
    /// aliases. The returned list is the SAME instance <see cref="GateDataSource"/> holds when it
    /// served a cached answer, and <see cref="AugmentingAirportDataProvider.AugmentParking"/> is
    /// documented idempotent, so calling this repeatedly for one ICAO is safe.
    /// </para>
    /// </summary>
    /// <param name="dataProvider">
    /// The navdata provider — used both as the fallback source and, when it is an
    /// <see cref="AugmentingAirportDataProvider"/>, as the alias source. Never null.
    /// </param>
    /// <param name="gateSource">
    /// The authoritative gate list, or null for "no GSX wiring available" (a test, a form
    /// constructed without one, no database yet), which degrades to plain navdata.
    /// </param>
    public static List<ParkingSpot> GetSpots(
        IAirportDataProvider dataProvider, GateDataSource? gateSource, string icao)
    {
        var spots = gateSource?.GetGates(icao) ?? dataProvider.GetParkingSpots(icao) ?? new List<ParkingSpot>();

        // No-op for a navdata list (GetParkingSpots already aliased it on the way out) and when
        // augmentation is off/uncached; the load-bearing case is a GSX-sourced list, which
        // bypasses GetParkingSpots entirely and would otherwise arrive with no aliases.
        (dataProvider as AugmentingAirportDataProvider)?.AugmentParking(icao, spots);

        return spots;
    }
}
