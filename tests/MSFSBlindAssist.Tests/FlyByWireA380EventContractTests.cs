// The A380 def's INPUT-EVENT contract with the FlyByWire A380X build.
//
// VarNameCollisionTests pins the VARIABLE side of an FBW subsystem move. This file pins the
// EVENT side, which is the half the #10855 audit missed: FBW deletes a custom input event by
// removing its addInputDataDefinition() call, and a K-event nobody registered is silently
// swallowed by the sim. There is no error, no log line and no wrong value to notice — the
// control simply stops doing anything, which for a blind pilot is indistinguishable from
// "I must have pressed the wrong thing".
//
// Verified against the FlyByWire tree at a380x commit 1bbd304 ("feat(a380x): add FG part to
// PRIM", #10855) — the build docs/a380x.md requires.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FlyByWireA380EventContractTests
{
    /// <summary>
    /// Custom input events #10855 DELETED from SimConnectInterface.cpp. Firing one is a no-op:
    /// `git grep -F "A32NX.FCU_TO_AP_HDG_PUSH" 1bbd304` returns nothing, against 2 hits at
    /// 1bbd304^. Each maps to a same-shaped replacement below.
    /// </summary>
    public static readonly string[] RetiredByFbw10855 =
    {
        "A32NX.FCU_TO_AP_HDG_PUSH",
        "A32NX.FCU_TO_AP_HDG_PULL",
        "A32NX.FCU_TO_AP_VS_PULL",
    };

    /// <summary>
    /// The replacements, each confirmed present at 1bbd304 as an addInputDataDefinition() plus a
    /// handler that drives the FCU AFS panel knob (SimConnectInterface.cpp:2349 hdg pushed,
    /// :2356 hdg pulled, :2460 vs pulled).
    /// </summary>
    public static readonly string[] ReplacedByFbw10855 =
    {
        "A32NX.FCU_HDG_PUSH",
        "A32NX.FCU_HDG_PULL",
        "A32NX.FCU_VS_PULL",
    };

    private static IEnumerable<string> A380EventNames() =>
        new FlyByWireA380Definition().GetVariables()
            .Where(kv => kv.Value.Type == SimVarType.Event)
            .Select(kv => kv.Value.Name);

    [Fact]
    public void No_input_event_retired_by_the_fg_into_prim_move_is_still_registered()
    {
        var stale = A380EventNames().Where(RetiredByFbw10855.Contains).Distinct().Order().ToList();

        Assert.True(stale.Count == 0,
            "A380 def fires input events FBW #10855 deleted (silent no-ops on the required build): "
            + string.Join(", ", stale));
    }

    [Fact]
    public void The_replacement_input_events_are_registered()
    {
        var names = A380EventNames().ToHashSet(StringComparer.Ordinal);
        var missing = ReplacedByFbw10855.Where(e => !names.Contains(e)).ToList();

        Assert.True(missing.Count == 0,
            "A380 def is missing the #10855 replacement input events: " + string.Join(", ", missing));
    }

    // ---- EFIS baro STD/QNH ----------------------------------------------------------------
    //
    // #10855 deleted MsfsBaroManager.ts and the A32NX_Interior_FCU.xml behaviour that between
    // them were the ONLY consumers of H:A380X_EFIS_CP_BARO_{PUSH,PULL}_{1,2}. The knob is now
    // driven by the FCU computer through the K-events below.
    //
    // POLARITY IS THE OPPOSITE OF THE A32NX AND MUST STAY THAT WAY — it is not a copy/paste
    // slip. A380FcuComputer.cpp:2142-2150 reads, with rtb_Equal7 = pushed and rtb_Compare_j =
    // pulled:  if (pulled && std_active) std_active = false;  ... else std_active = ((pushed &&
    // !std_active) || std_active).  So PUSH selects STD and PULL selects QNH, where the A32NX
    // knob is PULL=STD. The `pulled && !std_active` arm toggles QNH<->QFE, but the very next
    // block forces qnh_active=true whenever !pin_prog_qfe_avail, and that input is hardcoded
    // false (FlyByWireInterface.cpp:2351) — which is what keeps PULL idempotent and lets the
    // caller fire it unconditionally.
    [Theory]
    [InlineData("A32NX_FCU_LEFT_EIS_BARO_IS_STD", true, "A32NX.FCU_EFIS_L_BARO_PUSH")]
    [InlineData("A32NX_FCU_LEFT_EIS_BARO_IS_STD", false, "A32NX.FCU_EFIS_L_BARO_PULL")]
    [InlineData("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", true, "A32NX.FCU_EFIS_R_BARO_PUSH")]
    [InlineData("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", false, "A32NX.FCU_EFIS_R_BARO_PULL")]
    public void Baro_mode_event_maps_side_and_std_to_the_fcu_k_event(
        string varKey, bool standard, string expected)
    {
        Assert.Equal(expected, FlyByWireA380Definition.BaroModeEvent(varKey, standard));
    }

    [Fact]
    public void Baro_mode_event_declines_a_key_that_is_not_a_baro_std_selector()
    {
        Assert.Null(FlyByWireA380Definition.BaroModeEvent("A32NX_FCU_LEFT_EIS_BARO_HPA", true));
    }

    // ---- EFIS-CP shim-output actuators ----------------------------------------------------
    //
    // A380EventNames() can only see events registered as SimVarDefinitions via Evt(...), which
    // is how the sibling EFIS-CP push events are registered (FlyByWireA380Definition.cs, the
    // ARPT/CSTR/VV/WPT/VORD/NDB loop). The actuators A380EfisCpControls and A380NdKnobSelection
    // emit are bare string literals inside those helpers, so the FBW-drift sweep this file IS
    // would walk straight past them — and a deleted custom input event has no failure signal
    // whatsoever: the sim swallows an unregistered K-event and the control silently reverts to
    // the dead write these classes were written to bypass.
    //
    // So inventory them here, the same way the baro pair is pinned above (through the helper,
    // not by re-typing the literal). A32NX.FCU_EFIS_{L,R}_NAVAID_{1,2}_SET only landed in FBW
    // #10914 on 2026-09-01 and A32NX.FCU_TRUE_TOGGLE_PUSH is source-verified only, so these are
    // the newest and least-proven names in the def.
    public static readonly string[] EfisCpActuators =
    {
        "A32NX.FCU_EFIS_L_MODE_SET", "A32NX.FCU_EFIS_R_MODE_SET",
        "A32NX.FCU_EFIS_L_RANGE_SET", "A32NX.FCU_EFIS_R_RANGE_SET",
        "A32NX.FCU_EFIS_L_NAVAID_1_SET", "A32NX.FCU_EFIS_R_NAVAID_1_SET",
        "A32NX.FCU_EFIS_L_NAVAID_2_SET", "A32NX.FCU_EFIS_R_NAVAID_2_SET",
        "A32NX.FCU_EFIS_L_LS_PUSH", "A32NX.FCU_EFIS_R_LS_PUSH",
        "A32NX.FCU_EFIS_L_TRAF_PUSH", "A32NX.FCU_EFIS_R_TRAF_PUSH",
        "A32NX.FCU_EFIS_L_WX_PUSH", "A32NX.FCU_EFIS_R_WX_PUSH",
        "A32NX.FCU_EFIS_L_TERR_PUSH", "A32NX.FCU_EFIS_R_TERR_PUSH",
        "A32NX.FCU_TRUE_TOGGLE_PUSH",
    };

    [Fact]
    public void Every_efis_cp_actuator_the_helpers_emit_is_inventoried()
    {
        var inventory = EfisCpActuators.ToHashSet(StringComparer.Ordinal);
        var emitted = new List<string>();

        foreach (var side in new[] { "L", "R" })
        {
            emitted.Add(A380NdKnobSelection.SetEvent($"A32NX_EFIS_{side}_ND_MODE", 3)!.Value.EventName);
            emitted.Add(A380NdKnobSelection.SetEvent($"A32NX_EFIS_{side}_ND_RANGE", 3)!.Value.EventName);
            for (int nav = 1; nav <= 2; nav++)
                emitted.Add(A380EfisCpControls.Command($"A32NX_EFIS_{side}_NAVAID_{nav}_MODE", 1, 0)!.Value.EventName);
            emitted.Add(A380EfisCpControls.Command($"A32NX_EFIS_{side}_OANS_RANGE", 2, 5)!.Value.EventName);
            emitted.Add(A380EfisCpControls.Command($"A380X_EFIS_{side}_LS_BUTTON_IS_ON", 1, 0)!.Value.EventName);
            emitted.Add(A380EfisCpControls.Command($"A380X_EFIS_{side}_TRAF_BUTTON_IS_ON", 1, 0)!.Value.EventName);
            emitted.Add(A380EfisCpControls.Command($"A380X_EFIS_{side}_ACTIVE_OVERLAY", 1, 0)!.Value.EventName);
            emitted.Add(A380EfisCpControls.Command($"A380X_EFIS_{side}_ACTIVE_OVERLAY", 2, 0)!.Value.EventName);
        }
        emitted.Add(A380EfisCpControls.Command("A32NX_PUSH_TRUE_REF", 1, 0)!.Value.EventName);

        var unlisted = emitted.Where(e => !inventory.Contains(e)).Distinct().Order().ToList();
        Assert.True(unlisted.Count == 0,
            "EFIS-CP actuators emitted but not inventoried in EfisCpActuators (so the FBW-drift "
            + "sweep cannot see them): " + string.Join(", ", unlisted));
    }

    [Fact]
    public void The_inventoried_efis_cp_actuators_are_all_fcu_events()
    {
        // Every one must clear SimConnectManager.IsFbwFcuEvent's "A32NX.FCU_" test, which is
        // what lets them bypass the calc-path probe on the A380 (CLAUDE.md: a probe false
        // negative there does not degrade an FCU event, it kills it).
        Assert.All(EfisCpActuators, e => Assert.StartsWith("A32NX.FCU_", e, StringComparison.Ordinal));
    }
}
