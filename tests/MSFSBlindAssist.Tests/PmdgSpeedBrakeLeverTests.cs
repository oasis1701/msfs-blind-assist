// PMDG 737/777 speed-brake lever detents. Why and how the rest values were measured lives on
// PmdgSpeedBrakeLever — this file only pins the behaviour.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class PmdgSpeedBrakeLeverTests
{
    [Fact]
    // Measured 2026-09-30 on L:switch_498_a, the 777 lever's own 0-400 read-back: the combo's
    // Down / Armed / 50 percent / Up picks land exactly on 0 / 200 / 300 / 400.
    public void The_777_rests_at_the_measured_values_not_the_header_25()
    {
        Assert.Equal(new double[] { 0, 200, 300, 400 }, PmdgSpeedBrakeLever.B777.Select(d => d.Value));
    }

    // The 777 reads the lever from its own L:var, never the SDK's FCTL_Speedbrake_Lever byte: that
    // byte truncates, so a lever a fraction past ARM (spoilers already 34 percent up, measured
    // 2026-09-30) still read exactly 50 and was announced "Speed brake armed".
    [Fact]
    public void The_777_lever_is_read_from_its_own_L_var_not_the_truncating_SDK_byte()
    {
        var v = new PMDG777Definition().GetVariables()["FCTL_Speedbrake"];
        Assert.Equal("switch_498_a", v.Name);
        Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.LVar, v.Type);
    }

    // Measured 2026-09-30 with hydraulics pressurised: 200 is armed (spoilers 0 percent), and 201,
    // 202 and 203 all have the spoilers 34 percent up. Nothing past 200 is armed.
    [Theory]
    [InlineData(200, "Speed brake armed")]
    [InlineData(201, "Speed brake 1 percent")]
    [InlineData(203, "Speed brake 2 percent")]
    [InlineData(208, "Speed brake 4 percent")]
    [InlineData(282, "Speed brake 41 percent")]
    [InlineData(22, "Speed brake down")]      // where a hardware axis parks the lever for Down
    public void Nothing_past_the_777_ARM_detent_is_armed(double lever, string expected)
    {
        var c = New777();
        c.Settle(lever == 400 ? 0 : 400, 0, muted: false);                   // position at load
        Assert.Equal(expected, c.Settle(lever, 1000, muted: false));
    }

    [Fact]
    public void A_lever_past_ARM_never_speaks_zero_percent()
    {
        // (201 - 200) / 200 is half a percent, which banker's rounding makes 0: the spoilers are up.
        Assert.Equal("Speed brake 1 percent", PmdgSpeedBrakeLever.PartialDeployment(PmdgSpeedBrakeLever.B777, 201));
    }

    [Fact]
    public void The_737_rests_at_the_measured_values()
    {
        Assert.Equal(new double[] { 0, 100, 250, 337, 400 }, PmdgSpeedBrakeLever.Ng3.Select(d => d.Value));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(22, 0)]     // a hardware axis's Down
    [InlineData(200, 200)]
    [InlineData(248, 300)]  // past ARM: deployed, so the nearest deployed detent, never Armed
    [InlineData(252, 300)]
    [InlineData(396, 400)]
    [InlineData(100, 0)]    // short of ARM: Down (PositionIndex), never the old tie
    [InlineData(350, 300)]  // a tie between two deployed detents goes to the LOWER one
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

    // ARM is exact, so a lever resting past it is DEPLOYED (777: spoilers 34 percent up at 201; 737:
    // 34 percent at 101). The combo is read aloud when focused, so it must never say "Armed" there —
    // the same failure the exact ARM detent removed from the settle announcer — and must never say
    // "Down" either (the 737 at 101 is nearer Down than 50 percent). It names the nearest DEPLOYED
    // detent, which also makes picking "Armed" a real pick that moves the lever back to ARM.
    [Theory]
    [InlineData(201, 300)]
    [InlineData(205, 300)]
    public void A_777_lever_past_ARM_never_seeds_the_combo_Armed(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, PmdgSpeedBrakeLever.NearestDetentValue(
            PmdgSpeedBrakeLever.B777, lever, PmdgSpeedBrakeLever.B777SettleTolerance));
    }

    [Theory]
    [InlineData(101, 250)]
    [InlineData(137, 250)]
    public void A_737_lever_past_ARM_never_seeds_the_combo_Armed_or_Down(double lever, double expectedKey)
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
    [InlineData(85, -1)]    // between detents
    [InlineData(95, -1)]    // ARM is exact: short of it is not armed
    [InlineData(100, 1)]
    [InlineData(101, -1)]   // ARM is exact: at 101 the spoilers are 34 percent up
    [InlineData(105, -1)]
    [InlineData(328, 3)]    // the other detents keep the tolerance
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
    [InlineData(120, 0)]
    [InlineData(199, 0)]     // short of ARM is not armed, however close
    [InlineData(200, 200)]
    public void A_777_lever_short_of_ARM_reads_down(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, PmdgSpeedBrakeLever.NearestDetentValue(
            PmdgSpeedBrakeLever.B777, lever, PmdgSpeedBrakeLever.B777SettleTolerance));
    }

    [Theory]
    [InlineData(120, 0)]
    [InlineData(199, 0)]
    [InlineData(200, 1)]
    [InlineData(201, -1)]    // past ARM: between detents (the percentage speaks)
    [InlineData(240, -1)]
    public void The_777s_resting_position_treats_short_of_ARM_as_down(double lever, int expectedIndex)
    {
        Assert.Equal(expectedIndex, PmdgSpeedBrakeLever.PositionIndex(
            PmdgSpeedBrakeLever.B777, lever, PmdgSpeedBrakeLever.B777SettleTolerance));
    }

    [Fact]
    public void A_777_lever_that_leaves_ARM_and_rests_short_of_it_says_down()
    {
        var callout = New777();
        Assert.Null(callout.Settle(0, 0, muted: false));                   // position at load
        Assert.Equal("Speed brake armed", callout.Settle(200, 0, muted: false));
        Assert.Equal("Speed brake down", callout.Settle(22, 0, muted: false));
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

    private static PmdgSpeedBrakeCallout New737() => new PMDG737Definition().SpeedBrakeCallout;

    // Measured 2026-09-30 (PMDG 737-800, hydraulics pressurised): ARM is 100 with the spoilers down,
    // and at 101, 105 and 137 they are 34, 34 and 37 percent up. Past ARM is a speed brake, never
    // "armed", and a lever resting there says how far it is deployed, as on the 777 and the iFly.
    [Theory]
    [InlineData(100, "Speed brake armed")]
    [InlineData(101, "Speed brake 1 percent")]
    [InlineData(105, "Speed brake 2 percent")]
    [InlineData(137, "Speed brake 12 percent")]
    [InlineData(250, "Speed brake 50 percent")]
    [InlineData(337, "Speed brake flight")]
    public void Nothing_past_the_737_ARM_detent_is_armed(double lever, string expected)
    {
        var c = New737();
        Assert.Null(c.Settle(0, 0, muted: false));                            // position at load
        Assert.Equal(expected, c.Settle(lever, 1000, muted: false));
    }

    // A 737 lever travels in one to two seconds across the 1 Hz L-var batch: with a percentage now
    // spoken between detents, the settle must outlast one batch or a lever still travelling would
    // be read out at the sample it happened to be caught at.
    [Fact]
    public void The_737_settle_outlasts_one_batch()
    {
        Assert.True(PmdgSpeedBrakeLever.Ng3SettleMs > 1000);
    }

    private static PmdgSpeedBrakeCallout New777() => new(
        PmdgSpeedBrakeLever.B777, PmdgSpeedBrakeLever.B777SettleTolerance, PmdgSpeedBrakeLever.B777SettleMs,
        "PMDG_777", "FCTL_Speedbrake", speakFirst: false);

    [Fact]
    public void The_737_records_its_first_settle_silently_then_speaks_changes_once()
    {
        var c = New737();
        Assert.Null(c.Settle(0, 0, muted: false));                            // position at load
        Assert.Equal("Speed brake armed", c.Settle(100, 1000, muted: false));
        Assert.Null(c.Settle(100, 2000, muted: false));                       // same detent
        Assert.Equal("Speed brake 23 percent", c.Settle(170, 3000, muted: false));   // between detents
    }

    [Fact]
    // The lever now rides the 1 Hz L-var batch like the 737's, whose first sample is the lever's
    // position at load, not a change.
    public void The_777_records_its_first_settle_silently_then_reads_the_table()
    {
        var c = New777();
        Assert.Null(c.Settle(0, 0, muted: false));                            // position at load
        Assert.Equal("Speed brake armed", c.Settle(200, 1000, muted: false));
        Assert.Equal("Speed brake 50 percent", c.Settle(300, 2000, muted: false));
        Assert.Equal("Speed brake 100 percent", c.Settle(400, 3000, muted: false));
        Assert.Equal("Speed brake down", c.Settle(0, 4000, muted: false));
    }

    [Theory]
    [InlineData(240, "Speed brake 20 percent")]
    [InlineData(360, "Speed brake 80 percent")]
    [InlineData(120, "Speed brake down")]   // short of ARMED is not armed: Down (PositionIndex)
    public void The_777_reads_a_lever_resting_between_detents(double lever, string? expected)
    {
        var c = New777();
        c.Settle(200, 0, muted: false);                                      // position at load
        Assert.Equal(expected, c.Settle(lever, 1000, muted: false));
    }

    // The 737's L-var rides a 1 Hz batch, so a travelling lever can SETTLE
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

    // The 777 lever is an L-var now, like the 737's: a flight load re-delivers only CHANGED
    // L-vars, so a reset would make the first real move after it a silent baseline. The context
    // reset leaves the callout alone, as the 737's does.
    [Fact]
    public void A_777_context_reset_does_not_swallow_the_next_move()
    {
        var def = new PMDG777Definition();
        Assert.Null(def.SpeedBrakeCallout.Settle(0, 0, muted: false));        // position at load

        def.OnSimContextReset();

        Assert.Equal("Speed brake armed", def.SpeedBrakeCallout.Settle(200, 1000, muted: false));
    }

    [Fact]
    public void A_pick_silences_its_own_arrival_only()
    {
        var c = New777();
        c.Settle(0, 0, muted: false);
        c.RecordPick(3);
        Assert.Null(c.Settle(400, Environment.TickCount64, muted: false));    // arrived where picked
        Assert.Equal("Speed brake armed", c.Settle(200, Environment.TickCount64, muted: false));
    }

    [Fact]
    public void A_pick_that_lands_elsewhere_is_still_announced()
    {
        var c = New777();
        c.Settle(0, 0, muted: false);
        c.RecordPick(3);
        Assert.Equal("Speed brake armed", c.Settle(200, Environment.TickCount64, muted: false));
        // The pick was answered: a later arrival at UP is someone else's move.
        Assert.Equal("Speed brake 100 percent", c.Settle(400, Environment.TickCount64, muted: false));
    }

    // The 737 answers a pick only at a detent: the ARM arrival still clears the ARM pick but keeps the
    // 50 percent pick sent after that sample arrived, so neither arrival is read back.
    [Fact]
    public void A_737_pick_sent_before_the_previous_arrival_settled_is_kept()
    {
        var c = New737();
        long t = Environment.TickCount64;
        Assert.Null(c.Settle(0, t, muted: false, c.NoteSample(0)));          // the load-time baseline

        c.RecordPick(1);                                                     // ARM
        long arm = c.NoteSample(100);
        c.RecordPick(2);                                                     // 50 percent
        Assert.Null(c.Settle(100, t, muted: false, arm));
        Assert.Null(c.Settle(250, t, muted: false, c.NoteSample(250)));
        Assert.Equal("Speed brake down", c.Settle(0, t, muted: false, c.NoteSample(0)));
    }

    // Both picks sent before the 1 Hz batch sample: the batch catches the lever AT the first on its
    // way to the second. That answers the first pick only — clearing every pick sent before the
    // sample dropped the second, and its arrival was read back over the pilot's pick.
    [Fact]
    public void A_737_lever_caught_at_one_pick_on_its_way_to_the_next_keeps_the_next()
    {
        var c = New737();
        long t = Environment.TickCount64;
        Assert.Null(c.Settle(0, t, muted: false, c.NoteSample(0)));          // the load-time baseline

        c.RecordPick(1);                                                     // ARM
        c.RecordPick(2);                                                     // 50 percent
        Assert.Null(c.Settle(100, t, muted: false, c.NoteSample(100)));      // passing ARM
        Assert.Null(c.Settle(250, t, muted: false, c.NoteSample(250)));      // arrival
        Assert.Equal("Speed brake down", c.Settle(0, t, muted: false, c.NoteSample(0)));
    }

    // Overshooting while arrowing — Armed, 50 percent, back to Armed — sends three picks, and the lever
    // rests only at the last. That rest answers every one of them. Answering only the OLDEST Armed pick
    // left the 50 percent and second Armed picks pending, and they swallowed a genuine move to either
    // detent for the rest of their memory.
    [Fact]
    public void A_rest_at_a_detent_picked_twice_answers_every_pick_up_to_the_latest()
    {
        var c = New737();
        Assert.Null(c.Settle(0, 0, muted: false));                            // position at load

        c.RecordPick(1);                                                     // Armed
        c.RecordPick(2);                                                     // 50 percent (overshoot)
        c.RecordPick(1);                                                     // back to Armed
        long t = Environment.TickCount64;
        Assert.Null(c.Settle(100, t, muted: false));                          // the pilot's own pick
        Assert.Equal("Speed brake 50 percent", c.Settle(250, t, muted: false));   // someone else's move
    }

    // The 777 lever takes about ten seconds end to end (measured 2026-09-30), and its arrival settles
    // up to one 1 Hz batch after it lands plus the settle delay. A pick of the far end must still be
    // remembered then, or its own arrival is read back over the screen reader's reading of the pick.
    [Fact]
    public void A_777_pick_across_the_full_travel_is_still_the_pilots_own()
    {
        const int OneBatchMs = 1000;
        var c = New777();
        c.Settle(0, 0, muted: false);                                         // position at load
        c.RecordPick(3);                                                     // Up, from Down
        long arrivalSettles = Environment.TickCount64
            + PmdgSpeedBrakeLever.B777FullTravelMs + OneBatchMs + PmdgSpeedBrakeLever.B777SettleMs;
        Assert.Null(c.Settle(400, arrivalSettles, muted: false));
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

    // The settle timer really speaks through the announcer — the path the switch-away test below
    // proves silent, so that test cannot pass merely because nothing in the harness ever speaks.
    [Fact]
    public void A_lever_left_to_settle_is_spoken()
    {
        UnmuteSpeedBrakes();
        var def = new PMDG737Definition();
        var speech = new SpeechCapture();
        def.SpeedBrakeCallout.Settle(0, 0, muted: false);                    // position at load
        def.SpeedBrakeCallout.OnSample(100, speech);
        Assert.True(SpinWait.SpinUntil(() => speech.All.Count > 0, TimeSpan.FromSeconds(5)),
            "the settle never spoke");
        Assert.Equal("Speed brake armed", speech.All[0]);
    }

    // A lever moved just before the pilot switches aircraft must not be announced over the aircraft
    // that follows: MainForm tells the outgoing definition it was switched away from, and both PMDG
    // jets stop their settle timer there, as the iFly does in its Shutdown.
    [Fact]
    public void A_PMDG_jet_switched_away_never_speaks_a_lever_still_settling()
    {
        UnmuteSpeedBrakes();
        var ng3 = new PMDG737Definition();
        var b777 = new PMDG777Definition();
        var speech = new SpeechCapture();
        ng3.SpeedBrakeCallout.Settle(0, 0, muted: false);                    // positions at load
        b777.SpeedBrakeCallout.Settle(0, 0, muted: false);
        ng3.SpeedBrakeCallout.OnSample(100, speech);                         // moved just before the swap
        b777.SpeedBrakeCallout.OnSample(200, speech);

        ng3.OnSwitchedAway();
        b777.OnSwitchedAway();

        Thread.Sleep(Math.Max(PmdgSpeedBrakeLever.Ng3SettleMs, PmdgSpeedBrakeLever.B777SettleMs) + 1000);
        Assert.Empty(speech.All);
    }

    // The callout's Ctrl+M mute is looked up by its OWN definition's aircraft code
    // (DefAnnounceMuteSets): built with any other code it reads another list, or none, and the
    // Speed Brake row mutes nothing.
    [Fact]
    public void Each_PMDG_callout_is_muted_through_its_own_definitions_Ctrl_M_list()
    {
        var settings = new Settings.UserSettings();
        settings.PMDGDisabledMonitorVariables.AddRange(new[] { "MON_PMDG737_SpeedBrake", "FCTL_Speedbrake" });
        settings.RebuildDisabledMonitorVariableCaches();
        Assert.True(new PMDG737Definition().SpeedBrakeCallout.IsMuted(settings));
        Assert.True(new PMDG777Definition().SpeedBrakeCallout.IsMuted(settings));
    }

    // The timer reads the live Ctrl+M lists: clear the two rows in memory (never saved), so a mute in
    // the settings of whoever runs the suite cannot decide these tests.
    private static void UnmuteSpeedBrakes()
    {
        var settings = Settings.SettingsManager.Current;
        settings.PMDGDisabledMonitorVariables.RemoveAll(k => k is "MON_PMDG737_SpeedBrake" or "FCTL_Speedbrake");
        settings.RebuildDisabledMonitorVariableCaches();
    }

    [Fact]
    public void A_muted_lever_is_recorded_but_not_spoken()
    {
        var c = New777();
        c.Settle(400, 0, muted: false);                                      // position at load
        Assert.Null(c.Settle(200, 500, muted: true));
        // Unmuted at the same position: nothing stale is spoken.
        Assert.Null(c.Settle(200, 1000, muted: false));
        Assert.Equal("Speed brake down", c.Settle(0, 2000, muted: false));
    }
}
