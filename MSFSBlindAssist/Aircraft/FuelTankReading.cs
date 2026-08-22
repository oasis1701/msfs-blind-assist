using System.Collections.Generic;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// One RESOLVED per-tank fuel row — a <see cref="FuelTankSlot"/> with live numbers in it.
///
/// The slot table is static (label + which sim tank indices); this is what the aircraft
/// hands back once the quantities are known. The split exists because the two families
/// read fuel from completely different places and one of them is asynchronous:
/// stock-fuel aircraft await a SimConnect <c>FUELSYSTEM TANK WEIGHT</c> request, while
/// the PMDG jets read their CDA snapshot synchronously (the stock vars read 0 there —
/// legacy fuel model). Resolving to a common row type is what lets ONE window serve both.
/// </summary>
/// <param name="Label">Row name, e.g. "Centre" or "Outer". Leads the line so the list's
/// type-ahead lands on it — see <c>FuelTankReadout.FormatRow</c>.</param>
/// <param name="Values">
/// One entry per tank in the row: (side label or null, pounds). A single-tank row has one
/// entry with a null side; a symmetric pair has "left"/"right", kept on ONE row so an
/// imbalance check is a single utterance rather than two.
/// </param>
public sealed record FuelTankReading(string Label, IReadOnlyList<(string? Side, double Lbs)> Values);
