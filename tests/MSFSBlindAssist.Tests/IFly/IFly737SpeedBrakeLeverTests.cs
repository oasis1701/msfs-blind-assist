// iFly 737 MAX speed-brake lever. Where the two scales come from lives on IFly737SpeedBrakeLever —
// this file only pins the behaviour.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.IFly;

public class IFly737SpeedBrakeLeverTests
{
    [Fact]
    public void The_lever_rests_at_the_read_scale_values_and_is_moved_with_the_write_scale_values()
    {
        Assert.Equal(new double[] { 0, 35, 149, 224 }, IFly737SpeedBrakeLever.Detents.Select(d => d.RestValue));
        Assert.Equal(new double[] { 0, 34, 180, 254 }, IFly737SpeedBrakeLever.Detents.Select(d => d.WriteValue));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(35, 34)]
    [InlineData(149, 180)]
    [InlineData(224, 254)]
    public void A_pick_sends_its_detents_write_value_never_the_read_value(double pick, double expectedValue2)
    {
        Assert.Equal(expectedValue2, IFly737SpeedBrakeLever.WriteValueFor(pick));
    }

    [Theory]
    [InlineData(33.5, 35)]   // a hair off a detent must never open the combo blank
    [InlineData(90, 35)]     // mid-travel, nearer ARMED
    [InlineData(100, 149)]
    [InlineData(200, 224)]
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
        Assert.Contains(IFly737SpeedBrakeLever.FieldName, def.GetPanelControls()["Control Stand"]);
        Assert.DoesNotContain(IFly737SpeedBrakeLever.FieldName,
            def.GetPanelDisplayVariables().GetValueOrDefault("Control Stand") ?? new List<string>());
    }

    [Fact]
    public void The_settle_announcer_speaks_only_a_resting_detent_and_not_the_pilots_own_pick()
    {
        var callout = new PmdgSpeedBrakeCallout(IFly737SpeedBrakeLever.CalloutDetents,
            IFly737SpeedBrakeLever.SettleTolerance, IFly737SpeedBrakeLever.SettleMs,
            muteKey: IFly737SpeedBrakeLever.FieldName, speakFirst: true,
            muteSet: s => s.IFlyDisabledMonitorVariablesSet);

        Assert.Equal("Speed brake armed", callout.Settle(36, 0, muted: false));
        Assert.Null(callout.Settle(90, 100, muted: false));             // between detents: nothing

        callout.RecordPick(IFly737SpeedBrakeLever.IndexOfComboValue(224));
        Assert.Null(callout.Settle(224, Environment.TickCount64, muted: false));

        Assert.Equal("Speed brake down", callout.Settle(0, Environment.TickCount64, muted: false));
        Assert.Null(callout.Settle(149, Environment.TickCount64, muted: true));
    }
}
