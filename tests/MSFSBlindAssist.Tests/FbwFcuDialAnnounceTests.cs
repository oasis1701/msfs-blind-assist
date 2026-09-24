// The FBW FCU hardware-dial announcer (PR #140): WHICH variables it listens to on each airframe,
// what it says for them, how they are registered, and which of them a MSFSBA-originated write mutes.
//
// The source of each value is the whole fix. While an FCU window shows dashes, FBW's A320 FCU keeps
// copying the aircraft's LIVE heading, airspeed and vertical speed into the plain display values
// (A32NX_FCU_AFS_DISPLAY_*_VALUE) — the first version announced those, so the heading was read out
// as the aircraft turned on the ground, the airspeed through the take-off roll and the vertical
// speed through every managed climb. Every source below says on its own whether the window shows
// a selection: shims written as -1 while dashed (heading, speed) and ARINC429 words that are
// Normal Operation only for a displayed selection in their own mode (V/S, FPA). The phrase rules
// themselves are pinned in FcuValuePhrasesTests; the speak/stay-silent rules in
// FcuValueAnnouncerTests.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FbwFcuDialAnnounceTests
{
    private const uint NoComputedData = 1, NormalOperation = 3;
    private static double Word(uint ssm, float value) => FcuValuePhrasesTests.Word(ssm, value);

    public static IEnumerable<object[]> A32nxFamily() => new[]
    {
        new object[] { new FlyByWireA320Definition() },
        new object[] { new HeadwindA330Definition() },   // inherits the A32NX path unchanged
    };

    // ---- A32NX / Headwind A330 ----

    public static IEnumerable<object[]> A32nxLiveTrackingDisplayValues()
    {
        foreach (var def in A32nxFamily())
            foreach (string key in new[]
            {
                "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE",
                "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE",
                "A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE",
            })
                yield return new[] { def[0], key };
    }

    [Theory]
    [MemberData(nameof(A32nxLiveTrackingDisplayValues))]
    public void A32nx_display_values_that_follow_live_data_are_never_announced(FlyByWireA320Definition def, string key)
    {
        // The reported bug: turning on the ground moved the dashed heading window's display value.
        Assert.False(def.TryComposeFcuValuePhrase(key, 123.0, out _));
    }

    [Theory]
    [MemberData(nameof(A32nxLiveTrackingDisplayValues))]
    public void A32nx_display_values_that_follow_live_data_are_not_streamed(FlyByWireA320Definition def, string key)
    {
        // Read on demand by the output-mode Shift+H/S/V readouts only, exactly as before PR #140.
        Assert.Equal(UpdateFrequency.OnRequest, def.GetVariables()[key].UpdateFrequency);
    }

    [Theory]
    [MemberData(nameof(A32nxFamily))]
    public void A32nx_heading_is_silent_while_dashed_and_spoken_when_selected(FlyByWireA320Definition def)
    {
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_AUTOPILOT_HEADING_SELECTED", -1.0, out string? dashed));
        Assert.Null(dashed);
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_AUTOPILOT_HEADING_SELECTED", 123.0, out string? selected));
        Assert.Equal("Heading 123 degrees", selected);
    }

    [Theory]
    [MemberData(nameof(A32nxFamily))]
    public void A32nx_speed_is_silent_while_dashed_and_spoken_when_selected(FlyByWireA320Definition def)
    {
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_AUTOPILOT_SPEED_SELECTED", -1.0, out string? dashed));
        Assert.Null(dashed);
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_AUTOPILOT_SPEED_SELECTED", 250.0, out string? knots));
        Assert.Equal("Speed 250 knots", knots);
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_AUTOPILOT_SPEED_SELECTED", 0.78, out string? mach));
        Assert.Equal("Mach 0.78", mach);
    }

    [Theory]
    [MemberData(nameof(A32nxFamily))]
    public void A32nx_altitude_is_spoken_in_feet(FlyByWireA320Definition def)
    {
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_FCU_AFS_DISPLAY_ALT_VALUE", 10000.0, out string? phrase));
        Assert.Equal("Altitude 10000 feet", phrase);
    }

    [Theory]
    [MemberData(nameof(A32nxFamily))]
    public void A32nx_vertical_speed_is_silent_while_dashed_even_though_the_word_carries_the_live_value(FlyByWireA320Definition def)
    {
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_FCU_SELECTED_VERTICAL_SPEED",
            Word(NoComputedData, -1300f), out string? dashed));
        Assert.Null(dashed);
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_FCU_SELECTED_VERTICAL_SPEED",
            Word(NormalOperation, -1500f), out string? selected));
        Assert.Equal("Vertical speed -1500 feet per minute", selected);
    }

    [Theory]
    [MemberData(nameof(A32nxFamily))]
    public void A32nx_flight_path_angle_is_spoken_from_its_own_word(FlyByWireA320Definition def)
    {
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_FCU_SELECTED_FPA", Word(NormalOperation, -3.0f), out string? phrase));
        Assert.Equal("FPA -3.0 degrees", phrase);
    }

    public static IEnumerable<object[]> A32nxAnnounceKeys()
    {
        foreach (var def in A32nxFamily())
            foreach (string key in new[]
            {
                "A32NX_AUTOPILOT_HEADING_SELECTED",
                "A32NX_AUTOPILOT_SPEED_SELECTED",
                "A32NX_FCU_AFS_DISPLAY_ALT_VALUE",
                "A32NX_FCU_SELECTED_VERTICAL_SPEED",
                "A32NX_FCU_SELECTED_FPA",
            })
                yield return new[] { def[0], key };
    }

    [Theory]
    [MemberData(nameof(A32nxAnnounceKeys))]
    public void A32nx_announce_sources_stream_and_can_be_muted(FlyByWireA320Definition def, string key)
    {
        var v = def.GetVariables()[key];
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        Assert.False(v.ExcludeFromMonitorManager);          // its Ctrl+M row is the mute
        Assert.False(string.IsNullOrWhiteSpace(v.DisplayName));
        // A batched L:var is read in its registered unit, and SimConnect would "convert" a packed
        // ARINC429 word (or a display-unit value) as if it were SI.
        if (!v.ExcludeFromBatch) Assert.True(v.Units is null or "number", $"{key} Units = {v.Units}");
    }

    [Theory]
    [MemberData(nameof(A32nxFamily))]
    public void A32nx_trk_fpa_mode_is_not_announced_on_its_own(FlyByWireA320Definition def)
    {
        // The FCU panel's TRK/FPA button already confirms the mode through its press feedback;
        // streaming this var as announced spoke the same flip twice. The V/S and FPA words carry
        // the mode themselves, so nothing needs it streamed.
        Assert.Equal(UpdateFrequency.OnRequest, def.GetVariables()["A32NX_TRK_FPA_MODE_ACTIVE"].UpdateFrequency);
    }

    // ---- A380X ----

    [Theory]
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", 345.0, "Heading 345 degrees")]
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", 5.0, "Heading 005 degrees")]
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", -1.0, null)]
    [InlineData("A32NX_AUTOPILOT_SPEED_SELECTED", 250.0, "Speed 250 knots")]
    [InlineData("A32NX_AUTOPILOT_SPEED_SELECTED", 0.82, "Mach 0.82")]
    [InlineData("A32NX_AUTOPILOT_SPEED_SELECTED", -1.0, null)]
    [InlineData("FCU_ALT_VALUE", 10000.0, "Altitude 10000 feet")]
    public void A380_values_speak_in_display_units(string key, double value, string? expected)
    {
        var def = new FlyByWireA380Definition();
        Assert.True(def.TryComposeFcuValuePhrase(key, value, out string? phrase));
        Assert.Equal(expected, phrase);
    }

    [Fact]
    public void A380_vertical_speed_and_fpa_come_from_prim_1s_words()
    {
        var def = new FlyByWireA380Definition();
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_PRIM_1_SELECTED_VERTICAL_SPEED",
            Word(NormalOperation, 500f), out string? vs));
        Assert.Equal("Vertical speed 500 feet per minute", vs);
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_PRIM_1_SELECTED_VERTICAL_SPEED",
            Word(NoComputedData, 0f), out string? dashed));
        Assert.Null(dashed);
        Assert.True(def.TryComposeFcuValuePhrase("A32NX_PRIM_1_SELECTED_FPA",
            Word(NormalOperation, -2.5f), out string? fpa));
        Assert.Equal("FPA -2.5 degrees", fpa);
    }

    [Theory]
    [InlineData("A32NX_AUTOPILOT_VS_SELECTED")]
    [InlineData("A32NX_AUTOPILOT_FPA_SELECTED")]
    public void A380_vs_and_fpa_shims_are_not_announce_sources(string key)
    {
        // Both read 0 while the window shows dashes AND in the other mode, so a level-off spoke
        // "Vertical speed 0 feet per minute" — and 0 is also a real V/S selection.
        var def = new FlyByWireA380Definition();
        Assert.False(def.TryComposeFcuValuePhrase(key, 0.0, out _));
        Assert.Equal(UpdateFrequency.OnRequest, def.GetVariables()[key].UpdateFrequency);
    }

    [Theory]
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED")]
    [InlineData("A32NX_AUTOPILOT_SPEED_SELECTED")]
    [InlineData("FCU_ALT_VALUE")]
    [InlineData("A32NX_PRIM_1_SELECTED_VERTICAL_SPEED")]
    [InlineData("A32NX_PRIM_1_SELECTED_FPA")]
    public void A380_announce_sources_stream_and_can_be_muted(string key)
    {
        var v = new FlyByWireA380Definition().GetVariables()[key];
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        Assert.False(v.ExcludeFromMonitorManager);
        Assert.False(string.IsNullOrWhiteSpace(v.DisplayName));
        if (v.Type == SimVarType.LVar && !v.ExcludeFromBatch)
            Assert.True(v.Units is null or "number", $"{key} Units = {v.Units}");
    }

    [Fact]
    public void A380_registers_the_selected_altitude_simvar_under_exactly_one_key()
    {
        // FCU_ALT_VALUE and FCU_SEL_ALT were two keys on AUTOPILOT ALTITUDE LOCK VAR:3 — one number
        // with two 1 Hz pollers and, worse, two spoken names ("Selected Altitude" in Ctrl+M vs "FCU
        // selected altitude" in the PFD panel). VarNameCollisionTests cannot catch this class: it
        // filters to batched vars (!ExcludeFromBatch) because the hazard it guards is batch struct
        // drift, and both of these carry their own data def.
        var keys = new FlyByWireA380Definition().GetVariables()
            .Where(kv => kv.Value.Name == "AUTOPILOT ALTITUDE LOCK VAR:3")
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "FCU_ALT_VALUE" }, keys);
    }

    // ---- Echo window: a MSFSBlindAssist write mutes only the value vars IT moves ----
    // A single shared deadline meant setting one value in a dialog swallowed a hardware turn of a
    // DIFFERENT knob made in the same 2.5 s.

    [Theory]
    [InlineData("A32NX.FCU_HDG_PUSH", new[] { "A32NX_AUTOPILOT_HEADING_SELECTED" })]
    [InlineData("A32NX.FCU_HDG_PULL", new[] { "A32NX_AUTOPILOT_HEADING_SELECTED" })]
    [InlineData("A32NX.FCU_SPD_PULL", new[] { "A32NX_AUTOPILOT_SPEED_SELECTED" })]
    [InlineData("A32NX.FCU_ALT_PUSH", new[] { "FCU_ALT_VALUE" })]
    [InlineData("A32NX.FCU_VS_PULL", new[] { "A32NX_PRIM_1_SELECTED_VERTICAL_SPEED", "A32NX_PRIM_1_SELECTED_FPA" })]
    // TRK/FPA re-syncs the heading window onto the track as well as the vertical channel —
    // the same three keys SetTrkFpaMode mutes.
    [InlineData("A32NX.FCU_TRK_FPA_TOGGLE_PUSH", new[]
        { "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_PRIM_1_SELECTED_VERTICAL_SPEED", "A32NX_PRIM_1_SELECTED_FPA" })]
    public void A380_fcu_button_mutes_only_the_values_it_moves(string evt, string[] expected)
    {
        Assert.Equal(expected, FlyByWireA380Definition.FcuEchoKeysForEvent(evt));
    }

    [Theory]
    [InlineData("A32NX.FCU_HDG_PUSH", new[] { "A32NX_AUTOPILOT_HEADING_SELECTED" })]
    [InlineData("A32NX.FCU_SPD_PULL", new[] { "A32NX_AUTOPILOT_SPEED_SELECTED" })]
    [InlineData("A32NX.FCU_ALT_PUSH", new[] { "A32NX_FCU_AFS_DISPLAY_ALT_VALUE" })]
    [InlineData("A32NX.FCU_VS_PULL", new[] { "A32NX_FCU_SELECTED_VERTICAL_SPEED", "A32NX_FCU_SELECTED_FPA" })]
    [InlineData("A32NX.FCU_TRK_FPA_TOGGLE_PUSH", new[]
        { "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_FCU_SELECTED_VERTICAL_SPEED", "A32NX_FCU_SELECTED_FPA" })]
    public void A32nx_fcu_button_mutes_only_the_values_it_moves(string evt, string[] expected)
    {
        Assert.Equal(expected, FlyByWireA320Definition.FcuEchoKeysForEvent(evt));
    }

    [Theory]
    // A button that moves no value var must mute nothing at all.
    [InlineData("A32NX.FCU_AP_1_PUSH")]
    [InlineData("A32NX.FCU_ATHR_PUSH")]
    [InlineData("A32NX.FCU_EFIS_L_ARPT_PUSH")]
    // SPD/MACH toggle genuinely re-expresses the target in the other unit and nothing else speaks
    // it on the silent path, so it is deliberately not muted.
    [InlineData("A32NX.FCU_SPD_MACH_TOGGLE_PUSH")]
    public void Non_value_fcu_buttons_mute_nothing(string evt)
    {
        Assert.Empty(FlyByWireA380Definition.FcuEchoKeysForEvent(evt));
        Assert.Empty(FlyByWireA320Definition.FcuEchoKeysForEvent(evt));
    }

    public static IEnumerable<object[]> ValueButtons() => new[]
    {
        "A32NX.FCU_HDG_PUSH", "A32NX.FCU_HDG_PULL", "A32NX.FCU_SPD_PUSH", "A32NX.FCU_SPD_PULL",
        "A32NX.FCU_ALT_PUSH", "A32NX.FCU_ALT_PULL", "A32NX.FCU_VS_PUSH", "A32NX.FCU_VS_PULL",
        "A32NX.FCU_TRK_FPA_TOGGLE_PUSH",
    }.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(ValueButtons))]
    public void Every_echo_key_is_a_var_the_announcer_actually_listens_to(string evt)
    {
        // An echo key that names a var no longer announced mutes nothing, and the button's own
        // echo is then spoken on top of its confirmation. Moving a source must move its echo key.
        var a380 = new FlyByWireA380Definition();
        foreach (string key in FlyByWireA380Definition.FcuEchoKeysForEvent(evt))
            Assert.True(a380.TryComposeFcuValuePhrase(key, 0.0, out _), $"A380 {evt} -> {key}");

        var a32nx = new FlyByWireA320Definition();
        foreach (string key in FlyByWireA320Definition.FcuEchoKeysForEvent(evt))
            Assert.True(a32nx.TryComposeFcuValuePhrase(key, 0.0, out _), $"A32NX {evt} -> {key}");
    }

    // ---- A380 heading readout: dashes are not "359 degrees" ----
    // A32NX_AUTOPILOT_HEADING_SELECTED is -1 while the heading window shows dashes. The Shift+H
    // readout wrapped it into 0-360 first, so a managed heading read "FCU heading 359 degrees,
    // managed" — a plausible number that is on no display. The dial announcer and the readout must
    // agree, and the announcer says nothing for dashes; the readout says so in words, the way the
    // speed readout already does ("FCU speed managed").

    [Theory]
    [InlineData(-1.0, true, "FCU heading managed")]
    [InlineData(-1.0, false, "FCU heading managed")]
    [InlineData(345.0, false, "FCU heading 345 degrees, selected")]
    [InlineData(5.0, false, "FCU heading 005 degrees, selected")]
    [InlineData(360.0, true, "FCU heading 000 degrees, managed")]
    public void A380_heading_readout_speaks_dashes_as_managed_not_as_a_number(double raw, bool managed, string expected)
    {
        Assert.Equal(expected, FlyByWireA380Definition.FormatFcuHeadingReadout(raw, managed));
    }

    // ---- A32NX speed readout: a Mach target is not "001 knots" ----
    // A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE holds the target DIRECTLY — a Mach number below 10,
    // otherwise knots — so the readout cannot render it as "{value:000} knots" unconditionally.

    [Theory]
    [InlineData(0.78, "selected", "FCU speed mach 0.78, selected")]
    [InlineData(0.82, "managed", "FCU speed mach 0.82, managed")]
    [InlineData(250.0, "selected", "FCU speed 250 knots, selected")]
    [InlineData(80.0, "selected", "FCU speed 080 knots, selected")]
    public void A32nx_speed_readout_splits_mach_from_knots(double value, string status, string expected)
    {
        Assert.Equal(expected, FlyByWireA320Definition.FormatFcuSpeedReadout(value, status));
    }
}
