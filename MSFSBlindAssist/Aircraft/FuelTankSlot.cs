namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// One per-tank fuel row for the Fuel Tanks window (output Alt+U).
/// A slot is either a single tank ("Feed 1") or a symmetric pair kept on ONE row
/// ("Outer tanks, left N, right N") — pairing is what makes a lateral-imbalance check a
/// single read rather than two, so do not split a pair onto separate rows.
/// </summary>
/// <param name="Label">Row name, e.g. "Feed 1" or "Outer tanks". It LEADS the printed
/// line, because the window's first-letter type-ahead matches on the start of the row.</param>
/// <param name="Tanks">
/// The sim fuel-system tank(s) this slot reads: (side label or null, 1-based
/// FUELSYSTEM TANK index). Single-tank slots use one entry with a null side.
/// </param>
public sealed record FuelTankSlot(string Label, params (string? Side, int TankIndex)[] Tanks);
