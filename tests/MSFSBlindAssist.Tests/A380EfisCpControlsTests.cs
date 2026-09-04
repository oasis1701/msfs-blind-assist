using System;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A380 EFIS-CP / FCU controls whose backing L:var is an FCU-shim OUTPUT, so the
/// definition's direct-L:var catch-alls were dead writes for them: the combo snapped back
/// and the pilot got a silent no-op. Live-measured 2026-09-03 on a380x — writing
/// A32NX_EFIS_R_NAVAID_1_MODE = 2 read back 0, and A32NX_PUSH_TRUE_REF = 1 read back 0.
///
/// None of these keys is touched by the First Officer; this is panel-only.
/// </summary>
public class A380EfisCpControlsTests
{
    private static string? Evt(string key, double desired, double? current) =>
        A380EfisCpControls.Command(key, desired, current)?.EventName;

    // ==================================================================
    // Absolute setters — no current state needed, one event, no ordering
    // ==================================================================

    /// <summary>FBW #10914 (2026-09-01) added the absolute NAVAID SET, which replaced a
    /// 0-2 press walk of NAVAID_n_PUSH. The published getNavaidMode values and the
    /// a380_efis_navaid_selection enum the event takes are the SAME (NONE 0, ADF 1, VOR 2),
    /// so the value goes on the wire unchanged.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Navaid_is_an_absolute_set_on_the_published_enum(int mode)
    {
        var l = A380EfisCpControls.Command("A32NX_EFIS_L_NAVAID_1_MODE", mode, null)!.Value;
        Assert.Equal("A32NX.FCU_EFIS_L_NAVAID_1_SET", l.EventName);
        Assert.Equal((uint)mode, l.Parameter);

        var r = A380EfisCpControls.Command("A32NX_EFIS_R_NAVAID_2_MODE", mode, null)!.Value;
        Assert.Equal("A32NX.FCU_EFIS_R_NAVAID_2_SET", r.EventName);
        Assert.Equal((uint)mode, r.Parameter);
    }

    /// <summary>The absolute setters ignore the live value entirely, so they work from a COLD
    /// CACHE — which the cycling navaid path could not (it had to place the current position
    /// to count presses, and sent nothing when it could not).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(2.0)]
    [InlineData(99.0)]
    public void Navaid_set_does_not_depend_on_the_current_value(double? current)
    {
        Assert.Equal("A32NX.FCU_EFIS_L_NAVAID_1_SET",
            Evt("A32NX_EFIS_L_NAVAID_1_MODE", 1, current));
    }

    /// <summary>getOansRange publishes 0..4 for the five zoom levels, which ARE
    /// a380_efis_range_selection's RANGE_ZOOM_POINT_2..RANGE_ZOOM_5, so the parameter is the
    /// value itself. Live-verified: RANGE_SET 2 drove A32NX_EFIS_R_OANS_RANGE to 2.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Oans_zoom_is_an_absolute_range_set(int zoom)
    {
        var cmd = A380EfisCpControls.Command("A32NX_EFIS_L_OANS_RANGE", zoom, 5)!.Value;
        Assert.Equal("A32NX.FCU_EFIS_L_RANGE_SET", cmd.EventName);
        Assert.Equal((uint)zoom, cmd.Parameter);
    }

    /// <summary>5 is "not zoomed" — a readback state, not a selection. Leaving the zoom means
    /// picking a range in NM on the ND RANGE knob, so this sends nothing rather than
    /// inventing a parameter. Same for a navaid position off the enum.</summary>
    [Theory]
    [InlineData("A32NX_EFIS_L_OANS_RANGE", 5)]
    [InlineData("A32NX_EFIS_L_OANS_RANGE", -1)]
    [InlineData("A32NX_EFIS_L_NAVAID_1_MODE", 3)]
    [InlineData("A32NX_EFIS_L_NAVAID_1_MODE", -1)]
    public void Out_of_range_selections_send_nothing(string key, int value)
    {
        Assert.Null(A380EfisCpControls.Command(key, value, 0));
    }

    // ==================================================================
    // Relative controls — plain toggles and the overlay
    // ==================================================================

    /// <summary>LS and TRAF are single toggle buttons; press only when the pick differs.
    /// LS live-verified 0 -> 1 -> 0 on the F/O side.</summary>
    [Theory]
    [InlineData("A380X_EFIS_L_LS_BUTTON_IS_ON", "A32NX.FCU_EFIS_L_LS_PUSH")]
    [InlineData("A380X_EFIS_R_LS_BUTTON_IS_ON", "A32NX.FCU_EFIS_R_LS_PUSH")]
    [InlineData("A380X_EFIS_L_TRAF_BUTTON_IS_ON", "A32NX.FCU_EFIS_L_TRAF_PUSH")]
    [InlineData("A380X_EFIS_R_TRAF_BUTTON_IS_ON", "A32NX.FCU_EFIS_R_TRAF_PUSH")]
    [InlineData("A32NX_PUSH_TRUE_REF", "A32NX.FCU_TRUE_TOGGLE_PUSH")]
    public void Toggles_press_only_on_a_real_change(string key, string evt)
    {
        Assert.Equal(evt, Evt(key, 1, 0));
        Assert.Equal(evt, Evt(key, 0, 1));
        Assert.Null(Evt(key, 1, 1));
        Assert.Null(Evt(key, 0, 0));
    }

    /// <summary>Press the button you WANT; to clear, press whichever is shown. Live-verified
    /// on the F/O side across all four legs (Off-&gt;TERR-&gt;Off and Off-&gt;WX-&gt;TERR-&gt;Off), so —
    /// unlike the ND filter — this one really does clear and needs no spoken refusal.</summary>
    [Theory]
    [InlineData(0, 1, "A32NX.FCU_EFIS_L_WX_PUSH")]     // Off -> Weather
    [InlineData(0, 2, "A32NX.FCU_EFIS_L_TERR_PUSH")]   // Off -> Terrain
    [InlineData(1, 2, "A32NX.FCU_EFIS_L_TERR_PUSH")]   // Weather -> Terrain (live: replaced)
    [InlineData(2, 1, "A32NX.FCU_EFIS_L_WX_PUSH")]     // Terrain -> Weather
    [InlineData(1, 0, "A32NX.FCU_EFIS_L_WX_PUSH")]     // clear: press the one that is shown
    [InlineData(2, 0, "A32NX.FCU_EFIS_L_TERR_PUSH")]   // clear: press the one that is shown
    public void Overlay_presses_one_button(int current, int desired, string evt)
    {
        Assert.Equal(evt, Evt("A380X_EFIS_L_ACTIVE_OVERLAY", desired, current));
    }

    [Fact]
    public void Overlay_already_there_sends_nothing()
    {
        Assert.Null(Evt("A380X_EFIS_L_ACTIVE_OVERLAY", 0, 0));
        Assert.Null(Evt("A380X_EFIS_R_ACTIVE_OVERLAY", 2, 2));
    }

    // ==================================================================
    // Ownership — the caller gates on Handles, never on Command != null
    // ==================================================================

    /// <summary>Command answers null for TWO different questions — "not my key" and "already
    /// there". Handles separates them, and the caller must use it: gating on Command alone
    /// would drop a no-op set (a toggle picked at its current position) through to the
    /// direct-L:var catch-all, which is the dead write this class exists to bypass.</summary>
    [Theory]
    [InlineData("A380X_EFIS_L_LS_BUTTON_IS_ON")]
    [InlineData("A380X_EFIS_R_ACTIVE_OVERLAY")]
    [InlineData("A32NX_PUSH_TRUE_REF")]
    [InlineData("A32NX_EFIS_L_OANS_RANGE")]
    [InlineData("A32NX_EFIS_R_NAVAID_2_MODE")]
    public void A_no_op_set_is_still_owned(string key)
    {
        Assert.True(A380EfisCpControls.Handles(key));
        // Same value in and out, or an unsettable selection: nothing to send, still ours.
        Assert.Null(A380EfisCpControls.Command(key, 5, 5));
    }

    /// <summary>Only these shim-output keys are claimed. Anything else must keep its existing
    /// routing — in particular the ND MODE/RANGE knobs, whose own fix lives elsewhere, and
    /// the overhead/EFIS keys the direct calculator write genuinely does reach.</summary>
    [Theory]
    [InlineData("A32NX_EFIS_L_ND_MODE")]
    [InlineData("A32NX_EFIS_L_ND_RANGE")]
    [InlineData("A32NX_OVHD_COND_PACK_1_PB_IS_ON")]
    [InlineData("XMLVAR_Baro_Selector_HPA_1")]
    [InlineData("ND_FILTER_L")]
    [InlineData("A32NX_EFIS_X_NAVAID_1_MODE")]
    [InlineData("A32NX_EFIS_LR_OANS_RANGE")]
    public void Other_keys_are_not_claimed(string key)
    {
        Assert.False(A380EfisCpControls.Handles(key));
        Assert.Null(A380EfisCpControls.Command(key, 1, 0));
    }

    /// <summary>Every event name this class can emit was read out of the shipped fbw.wasm. A
    /// typo is a silent no-op in the sim, which is the exact failure being fixed.</summary>
    [Theory]
    [InlineData("A32NX_EFIS_L_NAVAID_1_MODE")]
    [InlineData("A32NX_EFIS_R_NAVAID_2_MODE")]
    [InlineData("A32NX_EFIS_L_OANS_RANGE")]
    [InlineData("A380X_EFIS_L_LS_BUTTON_IS_ON")]
    [InlineData("A380X_EFIS_R_TRAF_BUTTON_IS_ON")]
    [InlineData("A380X_EFIS_L_ACTIVE_OVERLAY")]
    [InlineData("A32NX_PUSH_TRUE_REF")]
    public void Emitted_event_names_are_well_formed(string key)
    {
        var cmd = A380EfisCpControls.Command(key, 2, 0);
        if (cmd is null) return;
        Assert.StartsWith("A32NX.FCU_", cmd.Value.EventName);
        Assert.DoesNotContain(" ", cmd.Value.EventName);
    }
}
