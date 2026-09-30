// iFly 737 MAX speed-brake lever. Where the two scales come from lives on IFly737SpeedBrakeLever —
// this file only pins the behaviour.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.IFly;

public class IFly737SpeedBrakeLeverTests
{
    [Fact]
    public void The_detents_are_the_measured_values_not_either_headers_other_guesses()
    {
        // All four measured (180 in flight). Never 35 or 149 (SDK_Defines.h) or 254
        // (key_command.h — ignored by the aircraft).
        Assert.Equal(new double[] { 0, 34, 180, 224 }, IFly737SpeedBrakeLever.Detents.Select(d => d.Value));
    }

    [Theory]
    [InlineData(33.5, 34)]   // a hair off a detent must never open the combo blank
    [InlineData(29, 34)]     // within the settle tolerance of ARMED
    [InlineData(22, 0)]      // short of ARMED is not armed: Down, never "Armed"
    [InlineData(28, 0)]
    [InlineData(100, 34)]    // a hardware axis between detents, nearer ARMED
    [InlineData(149, 180)]
    [InlineData(210, 224)]
    [InlineData(225, 224)]
    public void The_combo_seeds_with_the_nearest_detent(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, IFly737SpeedBrakeLever.NearestDetentValue(lever));
    }

    [Fact]
    public void Every_classified_value_is_a_combo_key()
    {
        // MainForm's combo lookup is an exact key match.
        var keys = IFly737SpeedBrakeLever.ComboDescriptions();
        for (double v = -10; v <= 260; v += 0.5)
            Assert.True(keys.ContainsKey(IFly737SpeedBrakeLever.NearestDetentValue(v)), $"{v}");
    }

    [Fact]
    public void The_spoken_sentences_match_the_PMDG_737s()
    {
        foreach (var d in IFly737SpeedBrakeLever.Detents)
            Assert.Contains(PmdgSpeedBrakeLever.Ng3, n => n.Spoken == d.Spoken && n.Label == d.Label);
    }

    [Fact]
    public void The_lever_is_a_writable_Control_Stand_combo_not_a_display_row()
    {
        var def = new IFly737MAXDefinition();
        var v = def.GetVariables()[IFly737SpeedBrakeLever.FieldName];

        Assert.False(v.RenderAsReadOnlyStatus);
        Assert.NotNull(v.ValueToDescriptionKey);
        Assert.True(v.IsAnnounced);
        Assert.True(v.RefreshesControlWhenDefHandled(224));   // an open combo follows the auto speed brake
        Assert.Contains(IFly737SpeedBrakeLever.FieldName, def.GetPanelControls()["Control Stand"]);
        Assert.DoesNotContain(IFly737SpeedBrakeLever.FieldName,
            def.GetPanelDisplayVariables().GetValueOrDefault("Control Stand") ?? new List<string>());
    }

    // The open combo follows the lever only where it RESTS. Refreshed on every 250 ms sample, a
    // focused combo was narrated at each detent a travelling lever passed ("Flight detent", "Fully
    // deployed") before the settle announcer spoke the resting one again, and a sample between
    // detents re-selected the nearest detent, which the lever was not at.
    [Theory]
    [InlineData(0, true)]
    [InlineData(22, true)]      // short of ARMED: Down, the position it is at
    [InlineData(34, true)]
    [InlineData(180, true)]
    [InlineData(224, true)]
    [InlineData(120, false)]    // between detents above ARMED: travelling, or a partial deployment
    [InlineData(60, false)]
    public void The_open_combo_follows_the_lever_only_at_a_position(double lever, bool refreshes)
    {
        var v = new IFly737MAXDefinition().GetVariables()[IFly737SpeedBrakeLever.FieldName];
        Assert.Equal(refreshes, v.RefreshesControlWhenDefHandled(lever));
    }

    [Fact]
    public void The_settle_announcer_speaks_only_a_resting_detent_and_not_the_pilots_own_pick()
    {
        var callout = new IFly737MAXDefinition().SpeedBrakeCallout;

        Assert.Equal("Speed brake armed", callout.Settle(34, 0, muted: false));

        callout.RecordPick(IFly737SpeedBrakeLever.IndexOfComboValue(224));
        Assert.Null(callout.Settle(224, Environment.TickCount64, muted: false));

        Assert.Equal("Speed brake down", callout.Settle(0, Environment.TickCount64, muted: false));
        Assert.Null(callout.Settle(180, Environment.TickCount64, muted: true));
    }

    [Theory]
    [InlineData(100, "Speed brake 35 percent")]   // (100 - 34) / 190 of the ARMED→UP travel
    [InlineData(120, "Speed brake 45 percent")]
    [InlineData(20, "Speed brake down")]            // short of ARMED is not armed: Down
    public void A_lever_resting_between_detents_speaks_its_travel_or_down(double lever, string? expected)
    {
        var callout = new IFly737MAXDefinition().SpeedBrakeCallout;
        Assert.Equal(expected, callout.Settle(lever, 0, muted: false));
    }

    [Fact]
    public void A_lever_that_leaves_ARMED_and_rests_short_of_it_says_down()
    {
        // A hardware axis drifting from ARMED to 22: the pilot last heard "armed" and must hear
        // that it no longer is.
        var callout = new IFly737MAXDefinition().SpeedBrakeCallout;
        Assert.Equal("Speed brake armed", callout.Settle(34, 0, muted: false));
        Assert.Equal("Speed brake down", callout.Settle(22, 0, muted: false));
    }

    // A pick always lands at once on the iFly (the write reads back exactly, with no travel), so a
    // pick of the detent the lever already rests at produces NO sample and nothing answers it at
    // once. Any later settle answers it: a lever resting between detents has gone somewhere else.
    // Without that the armed pick swallowed the lever's return to that detent — which is why the
    // pick used to be skipped when a (possibly stale) snapshot said the lever was already there.
    [Fact]
    public void A_settle_between_detents_answers_a_pending_pick()
    {
        var callout = new IFly737MAXDefinition().SpeedBrakeCallout;
        long t = Environment.TickCount64;
        Assert.Equal("Speed brake down", callout.Settle(0, t, muted: false));

        callout.RecordPick(0);                               // picked Down while already down
        Assert.Equal("Speed brake 35 percent", callout.Settle(100, t, muted: false));
        Assert.Equal("Speed brake down", callout.Settle(0, t, muted: false));   // a real move back
    }

    // Arrowing through the combo: the Flight pick is sent after Armed read back but before that
    // reading settled. With only the newest pick remembered, the Armed settle cleared the Flight pick
    // and both arrivals were read back over the screen reader's own reading of the picks.
    [Fact]
    public void Arrowing_through_the_combo_speaks_neither_arrival()
    {
        var callout = new IFly737MAXDefinition().SpeedBrakeCallout;
        long t = Environment.TickCount64;
        Assert.Equal("Speed brake down", callout.Settle(0, t, muted: false, callout.NoteSample(0)));

        callout.RecordPick(1);                               // Armed
        long armed = callout.NoteSample(34);
        callout.RecordPick(2);                               // Flight detent, before Armed settled
        Assert.Null(callout.Settle(34, t, muted: false, armed));
        long flight = callout.NoteSample(180);
        Assert.Null(callout.Settle(180, t, muted: false, flight));

        Assert.Equal("Speed brake down", callout.Settle(0, t, muted: false, callout.NoteSample(0)));
    }

    [Fact]
    public void The_mute_is_read_from_the_iFly_Ctrl_M_list_not_the_PMDG_one()
    {
        var callout = new IFly737MAXDefinition().SpeedBrakeCallout;

        var ifly = new Settings.UserSettings();
        ifly.IFlyDisabledMonitorVariables.Add(IFly737SpeedBrakeLever.FieldName);
        ifly.RebuildDisabledMonitorVariableCaches();
        Assert.True(callout.IsMuted(ifly));

        var pmdg = new Settings.UserSettings();
        pmdg.PMDGDisabledMonitorVariables.Add(IFly737SpeedBrakeLever.FieldName);
        pmdg.RebuildDisabledMonitorVariableCaches();
        Assert.False(callout.IsMuted(pmdg));
    }

    [Fact]
    public void A_context_reset_forgets_the_last_sentence_so_the_same_detent_speaks_again()
    {
        // A flight load or SimConnect drop: carried over, the last sentence would swallow the
        // first genuine settle at that detent as a repeat.
        var def = new IFly737MAXDefinition();
        Assert.Equal("Speed brake armed", def.SpeedBrakeCallout.Settle(34, 0, muted: false));
        Assert.Null(def.SpeedBrakeCallout.Settle(34, 0, muted: false));

        def.OnSimContextReset();

        Assert.Equal("Speed brake armed", def.SpeedBrakeCallout.Settle(34, 0, muted: false));
    }
}
