using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300FmaTrackerTests
{
    private readonly A300FmaTracker _tracker = new();

    private static A300FmaReading Shown(string? thrust = "Speed", string? pitch = "Vertical speed", string? roll = "Heading hold",
        string? common = null, string[]? armed = null) =>
        new(true, thrust, pitch, roll, common, armed ?? Array.Empty<string>(), "CMD 1");

    private static readonly A300FmaReading Blank = new(false, null, null, null, null, Array.Empty<string>(), null);

    private string[] Said(A300FmaReading reading) => _tracker.Observe(reading).Select(c => c.Phrase).ToArray();

    [Fact]
    public void The_first_reading_is_a_silent_baseline()
    {
        Assert.Empty(Said(Shown()));
    }

    [Fact]
    public void An_unchanged_reading_says_nothing()
    {
        Said(Shown());
        Assert.Empty(Said(Shown()));
    }

    [Fact]
    public void Each_changed_column_speaks_in_order()
    {
        Said(Shown());
        Assert.Equal(new[] { "Thrust mode: Thrust", "Pitch mode: Speed", "Roll mode: NAV" },
            Said(Shown(thrust: "Thrust", pitch: "Speed", roll: "NAV")));
    }

    [Fact]
    public void A_column_going_blank_says_nothing()
    {
        Said(Shown());
        Assert.Empty(Said(Shown(thrust: null)));
        Assert.Equal(new[] { "Thrust mode: Speed" }, Said(Shown()));
    }

    [Fact]
    public void A_newly_armed_mode_speaks_and_a_disarmed_one_does_not()
    {
        Said(Shown(armed: new[] { "Altitude" }));
        Assert.Equal(new[] { "Localizer armed", "Glide slope armed" },
            Said(Shown(armed: new[] { "Altitude", "Localizer", "Glide slope" })));
        Assert.Empty(Said(Shown(armed: new[] { "Glide slope" })));
    }

    [Fact]
    public void A_combined_mode_speaks_its_own_word()
    {
        Said(Shown(pitch: "Glide slope", roll: "Localizer"));
        Assert.Equal(new[] { "Land" }, Said(Shown(pitch: null, roll: null, common: "Land")));
        Assert.Equal(new[] { "Flare" }, Said(Shown(pitch: null, roll: null, common: "Flare")));
    }

    [Fact]
    public void Leaving_a_combined_mode_speaks_the_pitch_and_roll_that_return()
    {
        Said(Shown(pitch: null, roll: null, common: "Go around"));
        Assert.Equal(new[] { "Pitch mode: SRS", "Roll mode: NAV" }, Said(Shown(pitch: "SRS", roll: "NAV")));
    }

    [Fact]
    public void The_guidance_columns_appearing_is_a_silent_baseline()
    {
        Said(Blank);
        Assert.Empty(Said(Shown()));
        Assert.Equal(new[] { "Pitch mode: Altitude" }, Said(Shown(pitch: "Altitude")));
    }

    [Fact]
    public void The_guidance_columns_going_blank_say_nothing()
    {
        Said(Shown());
        Assert.Empty(Said(Blank));
    }

    [Fact]
    public void A_reset_makes_the_next_reading_a_baseline()
    {
        Said(Shown());
        _tracker.Reset();
        Assert.Empty(Said(Shown(pitch: "Altitude")));
    }

    [Fact]
    public void Each_callout_carries_its_column()
    {
        Said(Shown());
        var callouts = _tracker.Observe(Shown(thrust: "Thrust", pitch: "Speed", roll: "NAV", armed: new[] { "Altitude" }));
        Assert.Equal(new[] { A300FmaColumn.Thrust, A300FmaColumn.Pitch, A300FmaColumn.Roll, A300FmaColumn.Armed },
            callouts.Select(c => c.Column));
        Said(Shown(pitch: null, roll: null, common: "Land"));
        Assert.Empty(Said(Shown(pitch: null, roll: null, common: "Land")));
        Assert.Equal(A300FmaColumn.Combined, _tracker.Observe(Shown(pitch: null, roll: null, common: "Flare")).Single().Column);
    }

    [Fact]
    public void A_muted_change_is_still_remembered()
    {
        // The caller drops a muted column's phrase; the tracker has moved on either way, so the
        // change is never spoken late.
        Said(Shown());
        _tracker.Observe(Shown(pitch: "Altitude"));
        Assert.Empty(Said(Shown(pitch: "Altitude")));
    }
}
