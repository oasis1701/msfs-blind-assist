using System;
using System.Linq;
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
    private static string[] Events(string key, double desired, double? current) =>
        A380EfisCpControls.Commands(key, desired, current)!.Select(c => c.EventName).ToArray();

    // ==================================================================
    // Navaid selector — one push cycles Off -> VOR -> ADF
    // ==================================================================

    /// <summary>Live-measured on the F/O side: three presses walked 0 -> 2 -> 1 -> 0. The
    /// press COUNT is what this class exists to get right — a control reached by cycling
    /// cannot be set by writing the value.</summary>
    [Theory]
    [InlineData(0, 0, 0)]   // already Off
    [InlineData(0, 2, 1)]   // Off -> VOR
    [InlineData(0, 1, 2)]   // Off -> ADF (the two-press case)
    [InlineData(2, 1, 1)]   // VOR -> ADF
    [InlineData(2, 0, 2)]   // VOR -> Off
    [InlineData(1, 0, 1)]   // ADF -> Off
    [InlineData(1, 2, 2)]   // ADF -> VOR
    public void Navaid_walks_the_measured_cycle(int current, int desired, int presses)
    {
        Assert.Equal(Enumerable.Repeat("A32NX.FCU_EFIS_L_NAVAID_1_PUSH", presses).ToArray(),
            Events("A32NX_EFIS_L_NAVAID_1_MODE", desired, current));
        Assert.Equal(Enumerable.Repeat("A32NX.FCU_EFIS_R_NAVAID_2_PUSH", presses).ToArray(),
            Events("A32NX_EFIS_R_NAVAID_2_MODE", desired, current));
    }

    /// <summary>A position outside the cycle sends nothing rather than spraying presses at a
    /// selector whose state cannot be placed.</summary>
    [Fact]
    public void Navaid_with_an_unplaceable_state_sends_nothing()
    {
        Assert.Empty(Events("A32NX_EFIS_L_NAVAID_1_MODE", 1, 7));
        Assert.Empty(Events("A32NX_EFIS_L_NAVAID_1_MODE", 9, 0));
    }

    // ==================================================================
    // OANS zoom — absolute, on the ND RANGE knob's own enum
    // ==================================================================

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
        var cmd = A380EfisCpControls.Commands("A32NX_EFIS_L_OANS_RANGE", zoom, 5)!.Single();
        Assert.Equal("A32NX.FCU_EFIS_L_RANGE_SET", cmd.EventName);
        Assert.Equal((uint)zoom, cmd.Parameter);
    }

    /// <summary>5 is "not zoomed" — a readback state, not a selection. Leaving the zoom means
    /// picking a range in NM on the ND RANGE knob, so this sends nothing rather than
    /// inventing a parameter.</summary>
    [Fact]
    public void Oans_not_zoomed_is_not_settable()
    {
        Assert.Empty(A380EfisCpControls.Commands("A32NX_EFIS_L_OANS_RANGE", 5, 2)!);
    }

    // ==================================================================
    // Plain toggles
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
        Assert.Equal(new[] { evt }, Events(key, 1, 0));
        Assert.Equal(new[] { evt }, Events(key, 0, 1));
        Assert.Empty(Events(key, 1, 1));
        Assert.Empty(Events(key, 0, 0));
    }

    // ==================================================================
    // ND overlay — two buttons over one three-state selection
    // ==================================================================

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
        Assert.Equal(new[] { evt }, Events("A380X_EFIS_L_ACTIVE_OVERLAY", desired, current));
    }

    [Fact]
    public void Overlay_already_there_sends_nothing()
    {
        Assert.Empty(Events("A380X_EFIS_L_ACTIVE_OVERLAY", 0, 0));
        Assert.Empty(Events("A380X_EFIS_R_ACTIVE_OVERLAY", 2, 2));
    }

    // ==================================================================
    // Scoping
    // ==================================================================

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
        Assert.Null(A380EfisCpControls.Commands(key, 1, 0));
    }

    /// <summary>A cold cache reads as 0 rather than throwing or sending nothing — the
    /// _fcuToggleEvents precedent. Worst case is a press too few on a control the pilot can
    /// immediately re-pick.</summary>
    [Fact]
    public void A_cold_cache_is_treated_as_zero()
    {
        Assert.Equal(new[] { "A32NX.FCU_EFIS_L_LS_PUSH" },
            Events("A380X_EFIS_L_LS_BUTTON_IS_ON", 1, null));
        Assert.Empty(Events("A380X_EFIS_L_LS_BUTTON_IS_ON", 0, null));
    }

    /// <summary>Every event name this class can emit was read out of the shipped fbw.wasm. A
    /// typo is a silent no-op in the sim, which is the exact failure being fixed.</summary>
    [Fact]
    public void Emitted_event_names_are_well_formed()
    {
        string[] keys =
        {
            "A32NX_EFIS_L_NAVAID_1_MODE", "A32NX_EFIS_R_NAVAID_2_MODE", "A32NX_EFIS_L_OANS_RANGE",
            "A380X_EFIS_L_LS_BUTTON_IS_ON", "A380X_EFIS_R_TRAF_BUTTON_IS_ON",
            "A380X_EFIS_L_ACTIVE_OVERLAY", "A32NX_PUSH_TRUE_REF"
        };
        foreach (var key in keys)
            foreach (var cmd in A380EfisCpControls.Commands(key, 2, 0)!)
            {
                Assert.StartsWith("A32NX.FCU_", cmd.EventName);
                Assert.DoesNotContain(" ", cmd.EventName);
            }
    }
}
