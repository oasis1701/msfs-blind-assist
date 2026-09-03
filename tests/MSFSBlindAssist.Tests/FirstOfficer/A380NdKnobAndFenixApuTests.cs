using System;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using Xunit;

using A380Checklist = MSFSBlindAssist.FirstOfficer.FBWA380.FbwA380ChecklistDefinitions;
using FenixChecklist = MSFSBlindAssist.FirstOfficer.Fenix.FenixChecklistDefinitions;
using FenixFlows = MSFSBlindAssist.FirstOfficer.Fenix.FenixFlowDefinitions;
using FenixExec = MSFSBlindAssist.FirstOfficer.Fenix.FenixActionExecutor;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Two live-reported First Officer failures, both surfacing as "Unable to complete".
///
/// 1. FBW A380 — "Unable to complete: EFIS mode: ARC" / "EFIS range: 40".
///    A32NX_EFIS_{L,R}_ND_{MODE,RANGE} are FCU-shim OUTPUTS rewritten every frame by
///    fbw.wasm, so the definition's A32NX_EFIS_ prefix catch-all (a direct L:var write)
///    was overwritten within one frame. Live-measured on a380x 2026-09-03: writing 3 to
///    A32NX_EFIS_L_ND_MODE read back 2 immediately, while
///    A32NX.FCU_EFIS_L_RANGE_SET param 6 moved the published range 1 to 2.
///
/// 2. Fenix A320 — "Unable to complete: APU: ON and available" on a healthy APU start.
///    StartApuAsync returned at the START pulse while BS_APU detects on the AVAIL lamp,
///    which lights ~45 s later; the checklist's revert grace is ~10 s past the action.
/// </summary>
public class A380NdKnobAndFenixApuTests
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
    public void Other_efis_keys_are_left_to_the_direct_write(string varKey)
    {
        Assert.Null(A380NdKnobSelection.SetEvent(varKey, 1));
        Assert.False(A380NdKnobSelection.IsZoomAttempt(varKey, 0));
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

    /// <summary>The A380 First Officer keeps asking in the PUBLISHED enum — 3 = ARC for mode
    /// and 3 = 40 NM for range — because the definition owns the remap. If a future edit moves
    /// the remap into the profile, these targets change meaning silently.</summary>
    [Fact]
    public void A380_first_officer_targets_stay_on_the_published_enum()
    {
        var cockpitPrep = A380Checklist.Build()
            .Single(g => g.Id == "COCKPIT_PREP");

        var mode = cockpitPrep.Items.Single(i => i.Id == "CP_EFISMODE");
        Assert.Equal("A32NX_EFIS_L_ND_MODE", mode.StateFieldName);
        Assert.True(mode.EvaluateState(3));       // ARC
        Assert.False(mode.EvaluateState(2));

        var range = cockpitPrep.Items.Single(i => i.Id == "CP_EFISRANGE");
        Assert.Equal("A32NX_EFIS_L_ND_RANGE", range.StateFieldName);
        Assert.True(range.EvaluateState(3));      // 40 NM on the published enum
        Assert.False(range.EvaluateState(7));     // 7 is the FCU-enum value, not a readback
    }

    // ==================================================================
    // Fenix — the APU wait
    // ==================================================================

    /// <summary>The executor waits on the same lamp BS_APU/AL_APU detect on. If these ever
    /// diverge the wait would end on one condition while the checklist judged another — the
    /// exact shape of the bug being fixed.</summary>
    [Theory]
    [InlineData("BEFORE_START", "BS_APU")]
    [InlineData("AFTER_LANDING", "AL_APU")]
    public void Fenix_apu_items_detect_on_the_lamp_the_executor_waits_for(string groupId, string itemId)
    {
        var item = FenixChecklist.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

        Assert.Equal(FenixExec.ApuAvailField, item.StateFieldName);
        Assert.True(item.EvaluateState(1));
        Assert.False(item.EvaluateState(0));
    }

    /// <summary>The lamp test the executor polls agrees with the checklist condition, and an
    /// unread (null) cache keeps waiting rather than reading as available.</summary>
    [Fact]
    public void Apu_available_test_matches_the_checklist_condition()
    {
        Assert.True(FenixExec.IsApuAvailable(1));
        Assert.False(FenixExec.IsApuAvailable(0));
        Assert.False(FenixExec.IsApuAvailable(null));
        Assert.False(FenixExec.IsApuAvailable(double.NaN));
    }

    /// <summary>The wait budget has to outlast a real A320 APU start (~45 s) by a wide margin,
    /// and matches the Before Start flow's own 180 s WaitForField so both paths give up
    /// together. Anything near the ChecklistManager's 10 s ManualTickGrace reproduces the bug.</summary>
    [Fact]
    public void Apu_wait_budget_matches_the_flow_and_dwarfs_the_revert_grace()
    {
        Assert.Equal(180_000, FenixExec.ApuAvailTimeoutMs);

        var wait = FenixFlows.Build()
            .Single(f => f.Id == "BEFORE_START").Steps
            .Single(s => s.Id == "BS_APU_AVAIL");
        Assert.Equal(FenixExec.ApuAvailField, wait.ConditionFieldName);
        Assert.Equal(FenixExec.ApuAvailTimeoutMs / 1000, wait.TimeoutSeconds);
    }
}
