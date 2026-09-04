using System;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using Xunit;


namespace MSFSBlindAssist.Tests;

/// <summary>
/// A380 ND MODE / RANGE knobs. Both L:vars are FCU-shim OUTPUTS rewritten every frame by
/// fbw.wasm, so the definition's A32NX_EFIS_ prefix catch-all (a direct L:var write) was
/// overwritten within one frame. Live-measured on a380x 2026-09-03: writing 3 to
/// A32NX_EFIS_L_ND_MODE read back 2 immediately, while A32NX.FCU_EFIS_L_RANGE_SET param 6
/// moved the published range 1 to 2.
/// </summary>
public class A380NdKnobSelectionTests
{
    // ==================================================================
    // A380 — ND MODE: the SET enum equals the published enum
    // ==================================================================

    /// <summary>a380_efis_mode_selection (A380FcuComputer_types.h) is ROSE_ILS 0, ROSE_VOR 1,
    /// ROSE_NAV 2, ARC 3, PLAN 4 — identical to what the shim's getNdMode publishes, so the
    /// mode value passes through untouched. FBW's own in-flight init sends MODE_SET 3 for ARC.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Nd_mode_is_sent_verbatim_on_the_published_enum(int mode)
    {
        Assert.Equal(("A32NX.FCU_EFIS_L_MODE_SET", (uint)mode),
            A380NdKnobSelection.SetEvent("A32NX_EFIS_L_ND_MODE", mode));
        Assert.Equal(("A32NX.FCU_EFIS_R_MODE_SET", (uint)mode),
            A380NdKnobSelection.SetEvent("A32NX_EFIS_R_ND_MODE", mode));
    }

    // ==================================================================
    // A380 — ND RANGE: the SET enum is the published enum shifted by the
    // five OANS zoom levels. This is the half that silently selects the
    // wrong range if the read value is forwarded verbatim.
    // ==================================================================

    /// <summary>Published getNdRange 1..7 = 10..640 NM; a380_efis_range_selection puts
    /// RANGE_10..RANGE_640 at 5..11 behind five zoom levels. Pinned end to end, including the
    /// 40 NM the First Officer asks for (reads 3, must be SET as 7) and the 10/20 pair the
    /// live probe measured (SET 5 and 6 published 1 and 2).</summary>
    [Theory]
    [InlineData(1, 5)]   // 10 NM  — live: RANGE_SET 5 published 1
    [InlineData(2, 6)]   // 20 NM  — live: RANGE_SET 6 published 2
    [InlineData(3, 7)]   // 40 NM  — the FO's "EFIS range: 40"
    [InlineData(4, 8)]   // 80 NM
    [InlineData(5, 9)]   // 160 NM
    [InlineData(6, 10)]  // 320 NM
    [InlineData(7, 11)]  // 640 NM
    public void Nd_range_is_remapped_from_the_published_enum_to_the_fcu_enum(int published, int set)
    {
        Assert.Equal(("A32NX.FCU_EFIS_L_RANGE_SET", (uint)set),
            A380NdKnobSelection.SetEvent("A32NX_EFIS_L_ND_RANGE", published));
        Assert.Equal(("A32NX.FCU_EFIS_R_RANGE_SET", (uint)set),
            A380NdKnobSelection.SetEvent("A32NX_EFIS_R_ND_RANGE", published));
    }

    /// <summary>The published 0 means "an OANS zoom level is active". Five distinct zoom
    /// values sit behind that single readback, so no one SET value reproduces it — it must not
    /// be guessed into RANGE_10 or into a zoom level.</summary>
    [Fact]
    public void Nd_range_zoom_is_not_settable_and_says_so()
    {
        Assert.Null(A380NdKnobSelection.SetEvent("A32NX_EFIS_L_ND_RANGE", 0));
        Assert.True(A380NdKnobSelection.IsZoomAttempt("A32NX_EFIS_L_ND_RANGE", 0));
        Assert.False(A380NdKnobSelection.IsZoomAttempt("A32NX_EFIS_L_ND_RANGE", 3));
        Assert.False(A380NdKnobSelection.IsZoomAttempt("A32NX_EFIS_L_ND_MODE", 0));
        Assert.NotEmpty(A380NdKnobSelection.ZoomUnsupportedMessage);
    }

    /// <summary>Only the four knob keys are claimed. Every other A32NX_EFIS_/A380X_EFIS_ key
    /// must still fall through to the direct-L:var catch-all, which is correct for them —
    /// widening this would kill the navaid, OANS-range, LS and TRAF controls.</summary>
    [Theory]
    [InlineData("A32NX_EFIS_L_NAVAID_1_MODE")]
    [InlineData("A32NX_EFIS_L_OANS_RANGE")]
    [InlineData("A380X_EFIS_L_LS_BUTTON_IS_ON")]
    [InlineData("A380X_EFIS_R_ACTIVE_OVERLAY")]
    [InlineData("XMLVAR_Baro_Selector_HPA_1")]
    // A key that ENDS in _ND_RANGE but is not one of the four. IsZoomAttempt used to match on
    // that suffix alone, so it swallowed any such key and answered it with a sentence about the
    // OANS — and not one of the rows above ends that way, which made the Assert.False below
    // pass on string shape and never test the predicate at all.
    [InlineData("A380X_EFIS_L_ND_RANGE")]
    public void Other_efis_keys_are_left_to_the_direct_write(string varKey)
    {
        Assert.False(A380NdKnobSelection.Handles(varKey));
        Assert.Null(A380NdKnobSelection.SetEvent(varKey, 1));
        Assert.False(A380NdKnobSelection.IsZoomAttempt(varKey, 0));
    }

    /// <summary>The caller gates on Handles, never on SetEvent being null — SetEvent answers
    /// null for "not my key" AND for "value I refuse", and only the zoom refusal has a rescue.
    /// An out-of-range value must stay OWNED so it cannot reach the direct-L:var catch-all.</summary>
    [Theory]
    [InlineData("A32NX_EFIS_L_ND_MODE", 5)]
    [InlineData("A32NX_EFIS_R_ND_MODE", -1)]
    [InlineData("A32NX_EFIS_L_ND_RANGE", 8)]
    [InlineData("A32NX_EFIS_R_ND_RANGE", 0)]
    public void A_refused_value_is_still_owned(string varKey, int value)
    {
        Assert.True(A380NdKnobSelection.Handles(varKey));
        Assert.Null(A380NdKnobSelection.SetEvent(varKey, value));
    }

    /// <summary>An out-of-range value produces no event rather than an event the FCU would
    /// clamp or misread.</summary>
    [Theory]
    [InlineData("A32NX_EFIS_L_ND_MODE", 5)]
    [InlineData("A32NX_EFIS_L_ND_MODE", -1)]
    [InlineData("A32NX_EFIS_L_ND_RANGE", 8)]
    [InlineData("A32NX_EFIS_L_ND_RANGE", -1)]
    public void Out_of_range_values_send_nothing(string varKey, int value)
    {
        Assert.Null(A380NdKnobSelection.SetEvent(varKey, value));
    }

    /// <summary>Every event this class can emit is one fbw.wasm actually registers. The names
    /// were read out of the shipped module; a typo here is a silent no-op in the sim.</summary>
    [Fact]
    public void Emitted_event_names_match_the_fcu_registrations()
    {
        foreach (var side in new[] { "L", "R" })
        {
            Assert.Equal($"A32NX.FCU_EFIS_{side}_MODE_SET",
                A380NdKnobSelection.SetEvent($"A32NX_EFIS_{side}_ND_MODE", 3)!.Value.EventName);
            Assert.Equal($"A32NX.FCU_EFIS_{side}_RANGE_SET",
                A380NdKnobSelection.SetEvent($"A32NX_EFIS_{side}_ND_RANGE", 3)!.Value.EventName);
        }
    }
}
