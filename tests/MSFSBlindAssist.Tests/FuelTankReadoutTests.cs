// Characterization tests for the Fuel Tanks window rows (Services/FuelTankReadout.cs) and
// the A380 slot table they are driven by. The golden weights are a live capture from a
// FBW A380X cruise flight (2026-07-23): mids/inners already transferred into the feeds.

using System.Globalization;
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

    private static FuelTankReading Row(string label, params double[] lbs)
        => new(label, lbs.Select(v => ((string?)null, v)).ToList());

    [Fact]
    public void Single_tank_row_leads_with_label_then_both_units()
    {
        // 17739 lb × 0.453592 = 8046.3 → 8046
        Assert.Equal("Feed 1  17,739 lb (8,046 kg)", FuelTankReadout.FormatRow(Row("Feed 1", 17739)));
    }

    [Fact]
    public void Label_leads_the_line_so_type_ahead_can_reach_every_row()
    {
        // The window replaced nine digit chords with first-letter navigation, which the
        // native ListBox incremental search does on the row's LEADING characters. If a
        // number or unit ever leads, that navigation silently stops working.
        var lines = FuelTankReadout.BuildLines(new[]
        {
            Row("Centre", 1000), Row("Feed 1", 2000)
        });
        Assert.StartsWith("Centre", lines[0]);
        Assert.StartsWith("Feed 1", lines[1]);
        Assert.StartsWith("Total", lines[2]);
    }

    [Fact]
    public void Paired_row_keeps_both_sides_on_one_line_for_imbalance_checks()
    {
        var reading = new FuelTankReading("Outer",
            new List<(string?, double)> { ("left", 8818), ("right", 8000) });
        Assert.Equal("Outer  left 8,818 lb (4,000 kg), right 8,000 lb (3,629 kg)",
            FuelTankReadout.FormatRow(reading));
    }

    [Fact]
    public void Empty_tank_reads_zero_not_silence()
    {
        Assert.Equal("Mid  0 lb (0 kg)", FuelTankReadout.FormatRow(Row("Mid", 0)));
    }

    [Fact]
    public void Total_is_the_sum_of_the_printed_rows()
    {
        // Never a separate simvar read — a total that disagrees with the visible rows
        // reads as a bug even when both numbers are individually right.
        var lines = FuelTankReadout.BuildLines(new[] { Row("A", 1000), Row("B", 2500) });
        Assert.Equal("Total  3,500 lb (1,588 kg)", lines[^1]);
    }

    [Fact]
    public void Total_includes_both_halves_of_a_paired_row()
    {
        var pair = new FuelTankReading("Outer",
            new List<(string?, double)> { ("left", 1000), ("right", 1000) });
        Assert.Equal("Total  2,000 lb (907 kg)", FuelTankReadout.BuildLines(new[] { pair })[^1]);
    }

    [Fact]
    public void Thousands_separators_do_not_follow_the_machine_locale()
    {
        // de-DE would otherwise render 17.739; the readout must not change shape per user.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Feed 1  17,739 lb (8,046 kg)", FuelTankReadout.FormatRow(Row("Feed 1", 17739)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Resolve_maps_the_slot_table_onto_the_weights_array()
    {
        var slots = new[] { new FuelTankSlot("Outer", ("left", 1), ("right", 10)) };
        var rows = FuelTankReadout.Resolve(slots, LiveA380);
        Assert.Equal("Outer  left 8,818 lb (4,000 kg), right 8,818 lb (4,000 kg)",
            FuelTankReadout.FormatRow(rows[0]));
    }

    [Fact]
    public void Resolve_reads_an_out_of_range_tank_index_as_zero()
    {
        var rows = FuelTankReadout.Resolve(new[] { new FuelTankSlot("Ghost", (null, 42)) }, LiveA380);
        Assert.Equal("Ghost  0 lb (0 kg)", FuelTankReadout.FormatRow(rows[0]));
    }

    [Fact]
    public void A380_slot_table_covers_all_eleven_tanks()
    {
        var slots = new FlyByWireA380Definition().GetFuelTankSlots();
        Assert.NotNull(slots);
        Assert.Equal(8, slots!.Count);
        var indices = slots.SelectMany(s => s.Tanks.Select(t => t.TankIndex)).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, indices);
        // The old nine-digit-chord ceiling is GONE with the hotkeys — a window can list any
        // number of tanks, so no upper bound is asserted here on purpose.
    }
}
