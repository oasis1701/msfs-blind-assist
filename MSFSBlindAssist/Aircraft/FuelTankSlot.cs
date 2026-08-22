namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// One per-tank fuel readout slot (output-mode Ctrl+digit = pounds, Alt+digit = kilograms).
/// A slot is either a single tank ("Feed 1") or a symmetric pair announced together
/// ("Outer tanks, left N, right N") — pairing keeps big airframes like the A380
/// (11 tanks) within the nine digit chords while making imbalance checks one keypress.
/// </summary>
/// <param name="Label">Spoken slot name, e.g. "Feed 1" or "Outer tanks".</param>
/// <param name="Tanks">
/// The sim fuel-system tank(s) this slot reads: (side label or null, 1-based
/// FUELSYSTEM TANK index). Single-tank slots use one entry with a null side.
/// </param>
public sealed record FuelTankSlot(string Label, params (string? Side, int TankIndex)[] Tanks);
