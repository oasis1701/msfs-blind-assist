using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The ONE place the app decides what a stand is CALLED. Every readout a pilot can hear a stand
/// named in — the taxi dialog's destination combo, the gate-teleport list, <c>gate.select</c>,
/// Where-Am-I, and SayIntentions' "are you at your assigned gate" check — gets its stand names
/// from here, so a stand cannot be called two different things in one session.
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
/// <b>TWO shapes, because callers ask two different questions — and both get the same NAME.</b>
/// <list type="bullet">
/// <item><see cref="GetSelectableGates"/> answers <i>"which stands can I be sent to?"</i>. It is
/// GSX's own list, because a caller acting on a stand needs GSX's identity and metadata:
/// <c>GsxIdentifier</c> for <c>gate.select</c>, the stop position for docking, the max wingspan for
/// the fit filter, <c>TerminalName</c> to tell two identically-named stands apart. The two
/// selection dialogs use this, and it is exactly what they already did.</item>
/// <item><see cref="GetNamedSpots"/> answers <i>"what is this stand called?"</i>. It is the NAVDATA
/// list with its names corrected in place from the gate list — same spots, same count, same
/// coordinates, same order. Every <c>TaxiGraph.Build</c> call and the SayIntentions check use
/// this.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why the naming shape is NOT simply the gate list.</b> The parking list handed to
/// <c>TaxiGraph.Build</c> is not only a name source — its parking pass also writes
/// <c>node.Type = TaxiNodeType.Parking</c>, which <c>NamedHoldingPointResolver</c>,
/// <c>HoldShortNodeResolver</c> and the route truncation in <c>TaxiGuidanceManager.Routing</c> all
/// read. A different SET of spots marks a different set of nodes, so a hold-short could move, and
/// a stand GSX omits (Vehicle/Fuel, or one dropped for having no usable heading) would lose its
/// Where-Am-I label entirely. Naming a stand and deciding which nodes are parking are different
/// jobs, and only the first may change. See <see cref="GsxStandNameOverlay"/>.
/// </para>
///
/// <para>
/// <b>ALIASES ARE PART OF THE NAME</b>, which is why both shapes attach them here rather than
/// leaving it to the caller. <see cref="AugmentingAirportDataProvider.GetParkingSpots"/> aliases the
/// navdata list on its way out, so a caller taking the GSX list INSTEAD would silently get a list
/// with no aliases at all — and the alias is a real matching leg, not decoration: live KDTW names
/// the stand <c>A 24A</c> where SayIntentions, OSM and the controller all say <c>A24</c>.
/// <see cref="AugmentingAirportDataProvider.AugmentParking"/> is public for exactly this reason (see
/// its own doc comment) and is alias-ONLY: it never overwrites a Name or a position and never adds a
/// selectable gate. <see cref="GetNamedSpots"/> re-runs it AFTER the correction on purpose —
/// <c>GateAliasResolver</c> matches on the concourse letter, so aliases resolved against the old
/// letter would be resolved against the wrong identity.
/// </para>
///
/// <para>
/// <b>When GSX is not running, nothing here changes anything.</b>
/// <see cref="GateDataSource.GetGates"/> falls through to its <c>.ini</c>/navdata path — and its
/// last fallback is literally <c>GetParkingSpots</c> — so the correction compares the navdata list
/// against itself and confirms every name it already had. A null <paramref name="gateSource"/> (no
/// database, no GSX service wired) degrades to exactly the pre-seam call.
/// </para>
///
/// <para>
/// <b>Call it where a graph is BUILT or a dialog is opened, never on a position update.</b>
/// <see cref="GateDataSource"/> caches per ICAO, but a caller that constructs a fresh one per call
/// (the safe choice when the supplier can run off the UI thread — see
/// <c>TaxiGuidanceManager.ParkingSpotSupplier</c>) pays a directory listing and, on the GSX Remote
/// API path, a JSON read plus one navdata query. That is nothing beside a <c>TaxiGraph.Build</c>,
/// and unacceptable per frame.
/// </para>
/// </summary>
public static class ParkingSpotSource
{
    /// <summary>
    /// <b>"Which stands can I be sent to?"</b> — GSX's own gate list (or navdata when there is no
    /// better source), with this scenery's online aliases attached. Use this for anything that must
    /// ACT on a stand: the taxi planner's destination combo, the gate-teleport list,
    /// <c>gate.select</c>. It carries GSX's identity and metadata, which
    /// <see cref="GetNamedSpots"/> deliberately does not.
    ///
    /// <para>
    /// Never throws: a <see cref="GateDataSource.GetGates"/> failure is already swallowed inside
    /// that method (it returns navdata instead), and an augmentation failure would only cost the
    /// aliases. The returned list may be the SAME instance <see cref="GateDataSource"/> holds when
    /// it served a cached answer, and
    /// <see cref="AugmentingAirportDataProvider.AugmentParking"/> is documented idempotent, so
    /// calling this repeatedly for one ICAO is safe.
    /// </para>
    /// </summary>
    public static List<ParkingSpot> GetSelectableGates(
        IAirportDataProvider dataProvider, GateDataSource? gateSource, string icao)
    {
        var spots = gateSource?.GetGates(icao) ?? dataProvider.GetParkingSpots(icao) ?? new List<ParkingSpot>();

        // No-op for a navdata list (GetParkingSpots already aliased it on the way out) and when
        // augmentation is off/uncached; the load-bearing case is a GSX-sourced list, which
        // bypasses GetParkingSpots entirely and would otherwise arrive with no aliases.
        (dataProvider as AugmentingAirportDataProvider)?.AugmentParking(icao, spots);

        return spots;
    }

    /// <summary>
    /// <b>"What is this stand called?"</b> — the airport's own navdata parking list, with the
    /// concourse letter corrected in place from the authoritative gate list and this scenery's
    /// online aliases re-resolved against the corrected identity.
    ///
    /// <para>
    /// <b>Same spots, same count, same coordinates, same order</b> as
    /// <c>dataProvider.GetParkingSpots(icao)</c> — nothing is added and nothing is removed. That is
    /// what makes this safe to feed to <c>TaxiGraph.Build</c>: the same nodes are marked
    /// <c>Parking</c> as before, so <c>NamedHoldingPointResolver</c>,
    /// <c>HoldShortNodeResolver</c> and the route truncation see a graph identical to today's, and
    /// no stand loses its Where-Am-I label. See <see cref="GsxStandNameOverlay"/> for what the
    /// correction does and does not touch.
    /// </para>
    /// </summary>
    public static List<ParkingSpot> GetNamedSpots(
        IAirportDataProvider dataProvider, GateDataSource? gateSource, string icao)
    {
        var spots = dataProvider.GetParkingSpots(icao) ?? new List<ParkingSpot>();
        if (spots.Count == 0) return spots;

        GsxStandNameOverlay.Apply(spots, gateSource?.GetGates(icao));

        // AFTER the correction, not before: GateAliasResolver matches an online stand to a gate on
        // their concourse letters agreeing, so aliases resolved against the pre-correction letter
        // were resolved against the wrong identity. Recomputed from scratch each call, so this
        // simply replaces them.
        (dataProvider as AugmentingAirportDataProvider)?.AugmentParking(icao, spots);

        return spots;
    }
}
