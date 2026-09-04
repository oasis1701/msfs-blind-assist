// The FCU hardware-dial ANNOUNCE path reads the same vars as the Shift+H/S/A/V (A380) and
// Ctrl+H/S/A/V (A32NX) readouts, so the two must never disagree about units or wording.
//
// FBW #10855 turned the A380's FCU value L:vars into display-unit shims: heading arrives in
// DEGREES, V/S in FEET PER MINUTE and FPA in DEGREES, straight off the FCU. The readout was
// corrected for that in cece8e09, whose comment is explicit: "Do NOT re-add a 'looks like radians'
// guess — it would mangle any selected heading of 006° or less." Re-introducing the old
// conversions would announce a 500 fpm selection as "98400" and a heading of 005 as "286", so
// these pins exist to make that fail loudly rather than ship silently wrong.
//
// The values below are the ones cece8e09 recorded as verified live against the A380X build in
// docs/a380x.md. Every A380 announce call site routes its value through FcuAnnounceDisplayValue,
// so these assertions cover the production transform rather than a parallel copy — the speed arm
// was once bypassed at the call site, which left its pin decorative.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FlyByWireA380FcuAnnounceUnitsTests
{
    [Theory]
    // Heading: already degrees. 345 is cece8e09's live-verified reading.
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", 345.0, 345.0)]
    // The trap the readout comment names by hand: a low heading sits inside the radian range,
    // so a "looks like radians" guess turns 005° into 286°.
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", 5.0, 5.0)]
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", 0.0, 0.0)]
    // Wrap stays, because the var is still a heading.
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", 360.0, 0.0)]
    // Rounding lives INSIDE the helper, not at the call site, so it is covered here too.
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", 249.6, 250.0)]
    // The managed sentinel must survive the 0-360 wrap as a NEGATIVE number: wrapped, -1 becomes
    // 359, which the announce phrase could no longer tell apart from a real selection (it would
    // say "Heading 359 degrees" for dashes) and which a genuine 359 would later match in the
    // baseline and be swallowed by.
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED", -1.0, -1.0)]
    // V/S: already fpm. The old x196.85 m/s conversion spoke "98400" for this.
    [InlineData("A32NX_AUTOPILOT_VS_SELECTED", 500.0, 500.0)]
    [InlineData("A32NX_AUTOPILOT_VS_SELECTED", -1500.0, -1500.0)]
    // The 100-fpm rounding is a detent snap and must survive.
    [InlineData("A32NX_AUTOPILOT_VS_SELECTED", 1449.0, 1400.0)]
    // FPA: already degrees. A shallow FPA is inside the old radian guard, so it was scaled 57x.
    [InlineData("A32NX_AUTOPILOT_FPA_SELECTED", 0.1, 0.1)]
    [InlineData("A32NX_AUTOPILOT_FPA_SELECTED", 3.0, 3.0)]
    [InlineData("A32NX_AUTOPILOT_FPA_SELECTED", -2.5, -2.5)]
    // Speed carries no scaling on either side of the migration.
    [InlineData("A32NX_AUTOPILOT_SPEED_SELECTED", 250.0, 250.0)]
    [InlineData("A32NX_AUTOPILOT_SPEED_SELECTED", 0.78, 0.78)]
    public void Fcu_announce_uses_display_units_not_the_pre_10855_si_conversions(
        string varName, double raw, double expected)
    {
        double? actual = FlyByWireA380Definition.FcuAnnounceDisplayValue(varName, raw);
        Assert.NotNull(actual);
        Assert.Equal(expected, actual!.Value, 3);
    }

    [Fact]
    public void Fcu_announce_declines_a_var_that_is_not_an_fcu_selected_value()
    {
        Assert.Null(FlyByWireA380Definition.FcuAnnounceDisplayValue("A32NX_FMA_EXPEDITE_MODE", 1.0));
    }

    // ---- Echo window: a knob push/pull mutes only the value vars IT moves ----
    // A single shared deadline meant setting one value in a dialog swallowed a hardware turn of a
    // DIFFERENT knob made in the same 2.5 s — and because the baseline is committed before the echo
    // test, that swallowed change could never be re-announced.

    [Theory]
    [InlineData("A32NX.FCU_HDG_PUSH", "A32NX_AUTOPILOT_HEADING_SELECTED")]
    [InlineData("A32NX.FCU_HDG_PULL", "A32NX_AUTOPILOT_HEADING_SELECTED")]
    [InlineData("A32NX.FCU_SPD_PULL", "A32NX_AUTOPILOT_SPEED_SELECTED")]
    [InlineData("A32NX.FCU_ALT_PUSH", "FCU_ALT_VALUE")]
    public void A380_fcu_button_mutes_only_its_own_value_var(string evt, string expectedKey)
    {
        Assert.Equal(new[] { expectedKey }, FlyByWireA380Definition.FcuEchoKeysForEvent(evt));
    }

    [Fact]
    public void A380_vs_button_mutes_both_halves_of_the_shared_vertical_channel()
    {
        // VS_SELECTED and FPA_SELECTED are both shims off the same FCU vs_fpa_value, so a V/S
        // push/pull re-syncs both and both must be muted.
        Assert.Equal(
            new[] { "A32NX_AUTOPILOT_VS_SELECTED", "A32NX_AUTOPILOT_FPA_SELECTED" },
            FlyByWireA380Definition.FcuEchoKeysForEvent("A32NX.FCU_VS_PULL"));
    }

    [Theory]
    // A button that moves no value var must mute nothing at all.
    [InlineData("A32NX.FCU_AP_1_PUSH")]
    [InlineData("A32NX.FCU_ATHR_PUSH")]
    [InlineData("A32NX.FCU_EFIS_L_ARPT_PUSH")]
    // SPD/MACH toggle genuinely re-expresses the target in the other unit and nothing else speaks
    // it on the silent path, so it is deliberately not muted.
    [InlineData("A32NX.FCU_SPD_MACH_TOGGLE_PUSH")]
    public void A380_non_value_fcu_buttons_mute_nothing(string evt)
    {
        Assert.Empty(FlyByWireA380Definition.FcuEchoKeysForEvent(evt));
    }

    [Theory]
    [InlineData("A32NX.FCU_HDG_PUSH", "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE")]
    [InlineData("A32NX.FCU_SPD_PULL", "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE")]
    [InlineData("A32NX.FCU_ALT_PUSH", "A32NX_FCU_AFS_DISPLAY_ALT_VALUE")]
    [InlineData("A32NX.FCU_VS_PULL", "A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE")]
    public void A32nx_fcu_button_mutes_only_its_own_value_var(string evt, string expectedKey)
    {
        Assert.Equal(new[] { expectedKey }, FlyByWireA320Definition.FcuEchoKeysForEvent(evt));
    }

    [Theory]
    [InlineData("A32NX.FCU_AP_1_PUSH")]
    [InlineData("A32NX.FCU_SPD_MACH_TOGGLE_PUSH")]
    public void A32nx_non_value_fcu_buttons_mute_nothing(string evt)
    {
        Assert.Empty(FlyByWireA320Definition.FcuEchoKeysForEvent(evt));
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

    // ---- A32NX speed readout: a Mach target is not "001 knots" ----
    // A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE holds the target DIRECTLY — a Mach number below 10,
    // otherwise knots — so the readout cannot render it as "{value:000} knots" unconditionally.
    // It did, which meant a selected Mach 0.78 announced as "FCU speed 001 knots" one keypress
    // after the change announcer had correctly said "Mach 0.78".

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
