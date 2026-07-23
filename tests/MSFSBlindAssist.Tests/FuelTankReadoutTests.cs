// Characterization tests for the per-tank fuel readout phrasing (Services/FuelTankReadout.cs)
// and the A380 slot table it is driven by. The golden weights are a live capture from a
// FBW A380X cruise flight (2026-07-23): mids/inners already transferred into the feeds.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FuelTankReadoutTests
{
    // FUELSYSTEM TANK WEIGHT:1..16 (pounds), 0-based array. Live A380 capture:
    // 1 LeftOuter, 2 Feed1, 3 LeftMid, 4 LeftInner, 5 Feed2, 6 Feed3,
    // 7 RightInner, 8 RightMid, 9 Feed4, 10 RightOuter, 11 Trim, 12-16 line buffers.
    private static readonly double[] LiveA380 =
    [
        8818, 17739, 0, 0, 18760, 18723, 0, 0, 17520, 8818, 18163, 0, 0, 0, 0, 0
    ];

    [Fact]
    public void Single_tank_slot_reads_label_value_unit()
    {
        var slot = new FuelTankSlot("Feed 1", (null, 2));
        Assert.Equal("Feed 1, 17739 pounds", FuelTankReadout.Format(slot, LiveA380, kilograms: false));
    }

    [Fact]
    public void Single_tank_slot_converts_to_kilograms()
    {
        var slot = new FuelTankSlot("Trim tank", (null, 11));
        // 18163 lbs × 0.453592 = 8238.6 → 8239
        Assert.Equal("Trim tank, 8239 kilograms", FuelTankReadout.Format(slot, LiveA380, kilograms: true));
    }

    [Fact]
    public void Paired_slot_reads_both_sides_with_one_unit()
    {
        var slot = new FuelTankSlot("Outer tanks", ("left", 1), ("right", 10));
        Assert.Equal("Outer tanks, left 8818, right 8818 pounds",
            FuelTankReadout.Format(slot, LiveA380, kilograms: false));
    }

    [Fact]
    public void Empty_paired_slot_reads_zeros_not_silence()
    {
        var slot = new FuelTankSlot("Mid tanks", ("left", 3), ("right", 8));
        Assert.Equal("Mid tanks, left 0, right 0 pounds",
            FuelTankReadout.Format(slot, LiveA380, kilograms: false));
    }

    [Fact]
    public void Out_of_range_tank_index_reads_zero()
    {
        var slot = new FuelTankSlot("Ghost", (null, 42));
        Assert.Equal("Ghost, 0 pounds", FuelTankReadout.Format(slot, LiveA380, kilograms: false));
    }

    [Fact]
    public void A380_slot_table_is_eight_slots_covering_all_eleven_tanks()
    {
        var slots = new FlyByWireA380Definition().GetFuelTankSlots();
        Assert.NotNull(slots);
        Assert.Equal(8, slots!.Count);
        var indices = slots.SelectMany(s => s.Tanks.Select(t => t.TankIndex)).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, indices);
        // Nine digit chords max (Ctrl+0 is Approach Capability) — the table must never grow past 9.
        Assert.True(slots.Count <= 9);
    }
}
