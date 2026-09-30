// PMDG 737/777 speed-brake lever detents. Why and how the rest values were measured lives on
// PmdgSpeedBrakeLever — this file only pins the behaviour.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class PmdgSpeedBrakeLeverTests
{
    [Fact]
    public void The_777_rests_at_the_measured_values_not_the_header_25()
    {
        Assert.Equal(new double[] { 0, 50, 75, 100 }, PmdgSpeedBrakeLever.B777.Select(d => d.Value));
    }

    [Fact]
    public void The_737_rests_at_the_measured_values()
    {
        Assert.Equal(new double[] { 0, 100, 250, 337, 400 }, PmdgSpeedBrakeLever.Ng3.Select(d => d.Value));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49, 50)]
    [InlineData(50, 50)]
    [InlineData(62, 50)]    // nearer ARM than half
    [InlineData(63, 75)]
    [InlineData(99, 100)]
    [InlineData(25, 0)]     // a tie goes to the LOWER detent
    public void The_777_combo_seeds_with_the_nearest_detent(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, PmdgSpeedBrakeLever.NearestDetentValue(
            PmdgSpeedBrakeLever.B777, lever, PmdgSpeedBrakeLever.B777SettleTolerance));
    }

    [Theory]
    [InlineData(336.5, 337)]  // a hair off a detent must never open the combo blank
    [InlineData(62, 0)]       // mid-travel short of ARM (seen live): not armed, so Down
    [InlineData(300, 337)]
    [InlineData(420, 400)]
    public void The_737_combo_seeds_with_the_nearest_detent(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, PmdgSpeedBrakeLever.NearestDetentValue(
            PmdgSpeedBrakeLever.Ng3, lever, PmdgSpeedBrakeLever.Ng3SettleTolerance));
    }

    [Fact]
    public void Every_classified_value_is_a_combo_key()
    {
        // The whole point of the classifier: MainForm's lookup is an exact key match.
        foreach (var (table, tolerance) in new[] {
            (PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.Ng3SettleTolerance),
            (PmdgSpeedBrakeLever.B777, PmdgSpeedBrakeLever.B777SettleTolerance) })
        {
            var keys = PmdgSpeedBrakeLever.ComboDescriptions(table);
            for (double v = -20; v <= 450; v += 0.5)
                Assert.True(keys.ContainsKey(PmdgSpeedBrakeLever.NearestDetentValue(table, v, tolerance)), $"{v}");
        }
    }

    [Theory]
    [InlineData(85, -1)]    // between detents: the settle announcer says nothing
    [InlineData(95, 1)]     // within 10 of ARM
    [InlineData(105, 1)]
    [InlineData(328, 3)]
    [InlineData(400, 4)]
    public void The_737_settle_announcer_uses_a_tolerance(double lever, int expectedIndex)
    {
        Assert.Equal(expectedIndex, PmdgSpeedBrakeLever.SettledIndex(
            PmdgSpeedBrakeLever.Ng3, lever, PmdgSpeedBrakeLever.Ng3SettleTolerance));
    }

    // A lever resting short of the ARM detent (a hardware axis, or a lever caught mid-travel) is not
    // armed. The nearest-detent rule called 30 on the 777 "Armed" while the spoilers were not armed
    // and nothing was spoken — the one position a blind pilot most needs to hear the truth about
    // before landing. Within the settle tolerance of ARM it still reads Armed.
    [Theory]
    [InlineData(30, 0)]
    [InlineData(47, 0)]
    [InlineData(48, 50)]     // within B777SettleTolerance of ARM
    public void A_777_lever_short_of_ARM_reads_down(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, PmdgSpeedBrakeLever.NearestDetentValue(
            PmdgSpeedBrakeLever.B777, lever, PmdgSpeedBrakeLever.B777SettleTolerance));
    }

    [Theory]
    [InlineData(30, 0)]
    [InlineData(47, 0)]
    [InlineData(49, 1)]
    [InlineData(60, -1)]     // above ARM, between detents: still "between" (the percentage speaks)
    public void The_777s_resting_position_treats_short_of_ARM_as_down(double lever, int expectedIndex)
    {
        Assert.Equal(expectedIndex, PmdgSpeedBrakeLever.PositionIndex(
            PmdgSpeedBrakeLever.B777, lever, PmdgSpeedBrakeLever.B777SettleTolerance));
    }

    [Fact]
    public void A_777_lever_that_leaves_ARM_and_rests_short_of_it_says_down()
    {
        var callout = New777();
        Assert.Equal("Speed brake armed", callout.Settle(50, 0, muted: false));
        Assert.Equal("Speed brake down", callout.Settle(30, 0, muted: false));
        Assert.Null(callout.Settle(0, 0, muted: false));   // the same position: no repeat
    }

    [Fact]
    public void A_combo_pick_names_its_own_detent_and_its_event()
    {
        foreach (var table in new[] { PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.B777 })
            for (int i = 0; i < table.Count; i++)
            {
                Assert.Equal(i, PmdgSpeedBrakeLever.IndexOfComboValue(table, table[i].Value));
                Assert.StartsWith("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_", table[i].EventName);
            }
        Assert.Equal(-1, PmdgSpeedBrakeLever.IndexOfComboValue(PmdgSpeedBrakeLever.B777, 1));
    }

    [Fact]
    public void Each_event_table_carries_every_detent_event_its_combo_dispatches()
    {
        // A name missing from the table makes the pick silently do nothing (TryGetValue fails).
        foreach (var d in PmdgSpeedBrakeLever.B777)
            Assert.True(PMDG777Definition.EventIds.ContainsKey(d.EventName), d.EventName);
        foreach (var d in PmdgSpeedBrakeLever.Ng3)
            Assert.True(PMDG737Definition.EventIds.ContainsKey(d.EventName), d.EventName);
    }

    [Fact]
    public void Every_detent_on_both_jets_has_its_settle_sentence()
    {
        foreach (var table in new[] { PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.B777 })
            Assert.All(table, d => Assert.False(string.IsNullOrEmpty(d.Spoken)));
    }

    private static PmdgSpeedBrakeCallout New737() => new(
        PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.Ng3SettleTolerance, PmdgSpeedBrakeLever.Ng3SettleMs,
        "PMDG_737", "MON_PMDG737_SpeedBrake", speakFirst: false);

    private static PmdgSpeedBrakeCallout New777() => new(
        PmdgSpeedBrakeLever.B777, PmdgSpeedBrakeLever.B777SettleTolerance, PmdgSpeedBrakeLever.B777SettleMs,
        "PMDG_777", "FCTL_Speedbrake", speakFirst: true, betweenDetents: PmdgSpeedBrakeLever.B777PartialDeployment);

    [Fact]
    public void The_737_records_its_first_settle_silently_then_speaks_changes_once()
    {
        var c = New737();
        Assert.Null(c.Settle(0, 0, muted: false));                            // position at load
        Assert.Equal("Speed brake armed", c.Settle(100, 1000, muted: false));
        Assert.Null(c.Settle(101, 2000, muted: false));                       // same detent
        Assert.Null(c.Settle(170, 3000, muted: false));                       // between detents
    }

    [Fact]
    public void The_777_speaks_its_first_sample_and_reads_the_table()
    {
        var c = New777();
        Assert.Equal("Speed brake armed", c.Settle(50, 0, muted: false));
        Assert.Equal("Speed brake 50 percent", c.Settle(75, 1000, muted: false));
        Assert.Equal("Speed brake 100 percent", c.Settle(100, 2000, muted: false));
        Assert.Equal("Speed brake down", c.Settle(0, 3000, muted: false));
    }

    [Theory]
    [InlineData(60, "Speed brake 20 percent")]
    [InlineData(90, "Speed brake 80 percent")]
    [InlineData(30, "Speed brake down")]   // short of ARMED is not armed: Down (PositionIndex)
    public void The_777_reads_a_lever_resting_between_detents(double lever, string? expected)
    {
        Assert.Equal(expected, New777().Settle(lever, 0, muted: false));
    }

    // The 737's L-var rides a 1 Hz batch and settles after 300 ms, so a travelling lever SETTLES
    // mid-travel. A mid-travel sample short of ARM reads as Down; it must not answer a pick of ARM,
    // or the lever arriving at ARM is announced over the pilot's own pick.
    [Fact]
    public void A_737_pick_survives_a_mid_travel_sample_short_of_ARM()
    {
        var c = New737();
        long t = Environment.TickCount64;
        Assert.Null(c.Settle(0, t, muted: false));           // the load-time baseline

        c.RecordPick(1);                                     // pick ARM
        Assert.Null(c.Settle(62, t, muted: false));          // mid-travel (seen live)
        Assert.Null(c.Settle(100, t, muted: false));         // arrival: the pilot's own pick
    }

    [Fact]
    public void A_pick_silences_its_own_arrival_only()
    {
        var c = New777();
        c.Settle(0, 0, muted: false);
        c.RecordPick(3);
        Assert.Null(c.Settle(100, Environment.TickCount64, muted: false));    // arrived where picked
        Assert.Equal("Speed brake armed", c.Settle(50, Environment.TickCount64, muted: false));
    }

    [Fact]
    public void A_pick_that_lands_elsewhere_is_still_announced()
    {
        var c = New777();
        c.Settle(0, 0, muted: false);
        c.RecordPick(3);
        Assert.Equal("Speed brake armed", c.Settle(50, Environment.TickCount64, muted: false));
        // The pick was answered: a later arrival at UP is someone else's move.
        Assert.Equal("Speed brake 100 percent", c.Settle(100, Environment.TickCount64, muted: false));
    }

    [Fact]
    public void A_pick_the_aircraft_ignored_expires()
    {
        var c = New737();
        c.Settle(0, 0, muted: false);
        c.RecordPick(1);
        long later = Environment.TickCount64 + PmdgSpeedBrakeCallout.PickMemoryMs + 1;
        Assert.Equal("Speed brake armed", c.Settle(100, later, muted: false));
    }

    [Fact]
    public void A_muted_lever_is_recorded_but_not_spoken()
    {
        var c = New777();
        Assert.Null(c.Settle(50, 0, muted: true));
        // Unmuted at the same position: nothing stale is spoken.
        Assert.Null(c.Settle(50, 1000, muted: false));
        Assert.Equal("Speed brake down", c.Settle(0, 2000, muted: false));
    }
}
