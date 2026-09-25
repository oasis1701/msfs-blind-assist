// HeldRunwayLabel — the ONE answer to "which runway is guidance holding short of?", shared by the
// status readout and the ground-traffic runway watch. PR #247 review R2: the watch read a mirrored
// field that nothing ever assigned, so no ordinary hold-short was ever watched.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HeldRunwayLabelTests
{
    private static readonly string?[] Segs = { null, "B, Runway 09", "", "runway 27L and runway 04" };

    [Fact]
    public void The_destination_hold_is_the_destination()
        => Assert.Equal("Runway 27L",
            HeldRunwayLabel.Resolve(true, "Runway 27L", 3, "Runway 09", Segs));

    [Fact]
    public void An_empty_destination_name_is_no_label()
        => Assert.Null(HeldRunwayLabel.Resolve(true, "", 0, null, Segs));

    [Fact]
    public void A_start_hold_is_the_start_hold_label()
        => Assert.Equal("Runway 09", HeldRunwayLabel.Resolve(false, "Runway 27L", 0, "Runway 09", Segs));

    [Fact]
    public void Segment_zero_without_a_start_hold_has_no_label()
        => Assert.Null(HeldRunwayLabel.Resolve(false, "Runway 27L", 0, null, Segs));

    [Fact]
    public void A_crossing_hold_is_the_segment_just_completed()
    {
        // AdvanceSegment has already moved the cursor past the hold segment when HandleHoldShort
        // runs, so the hold is Segments[index - 1].
        Assert.Equal("B, Runway 09", HeldRunwayLabel.Resolve(false, "Runway 27L", 2, null, Segs));
        Assert.Equal("runway 27L and runway 04", HeldRunwayLabel.Resolve(false, "Runway 27L", 4, null, Segs));
    }

    [Fact]
    public void An_empty_segment_label_is_no_label()
        => Assert.Null(HeldRunwayLabel.Resolve(false, "Runway 27L", 3, null, Segs));

    [Fact]
    public void An_index_out_of_range_is_no_label()
    {
        Assert.Null(HeldRunwayLabel.Resolve(false, "Runway 27L", 5, null, Segs));
        Assert.Null(HeldRunwayLabel.Resolve(false, "Runway 27L", -1, null, Segs));
    }
}
