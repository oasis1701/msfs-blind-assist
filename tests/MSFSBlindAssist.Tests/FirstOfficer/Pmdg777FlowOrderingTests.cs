// Guardrails for the six owner-reported PMDG 777 First Officer defects (in-flight report,
// 2026-08-29). Every fact here walks the public Build() accessors the app enumerates, plus
// the pure Pmdg777SpeedbrakeLever policy — no SimConnect, no executor invocation.
//
// The authority for every numeric claim is the vendor header
// PMDG_777X_SDK.h, quoted inline where it settles a value.

using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

public class Pmdg777FlowOrderingTests
{
    // -- helpers ----------------------------------------------------------

    private static System.Collections.Generic.List<string> FlowStepIds(string flowId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId)
            .Steps.Select(s => s.Id).ToList();

    private static System.Collections.Generic.List<string> ItemIds(string groupId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId)
            .Items.Select(i => i.Id).ToList();

    private static MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<
        AircraftActionExecutor, AircraftStateEvaluator> Item(string groupId, string itemId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId)
            .Items.Single(i => i.Id == itemId);

    private static MSFSBlindAssist.FirstOfficer.Models.FlowStep<AircraftStateEvaluator>
        Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId)
            .Steps.Single(s => s.Id == stepId);

    // =====================================================================
    // 1. Speedbrake lever scale
    //
    // ⚠️ THE SDK HEADER IS WRONG HERE. PMDG_777X_SDK.h:454 (identical in the 77W, 77ER and
    // 77F packages) says:
    //     // Position 0...100  0: DOWN, 25: ARMED, 26...100: DEPLOYED
    // The real detents were MEASURED on a live 777 at the gate (2026-08-29) by clicking
    // each one through the stock K:ROTOR_BRAKE transport and reading the field back, and
    // cross-checked against the owner moving the lever by hand:
    //     DOWN 0   |   ARM 50   |   "50 percent" detent 75   |   UP 100
    // The 75 reading is what proves the mapping rather than merely asserting it: PMDG's
    // own event for that detent is named _50, and (75-50)/50 = 50 %. So deployment is
    // measured from the ARM detent at 50, and ARMED is 50 — NOT 25.
    //
    // What was actually broken is unchanged by that correction: every 777 FO condition
    // tested "v > 0.5 && v < 1.5", a detent INDEX this analog lever never produces. The
    // Landing flow armed the lever and then announced "Skipping: Speedbrake: ARM"; a
    // hand-tick reverted with "Unable to complete". The wrong scale came from a comment on
    // AircraftStateEvaluator.SpeeedbrakeLeverPos ("0=Down, 1=Armed, 2-7 = deployed").
    // =====================================================================

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(50, false)]
    [InlineData(100, false)]
    public void SpeedbrakeDown_is_only_the_zero_detent(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsDown(lever));

    [Theory]
    [InlineData(50, true)]    // MEASURED arm detent — the whole point
    [InlineData(0, false)]
    [InlineData(1, false)]    // what the old condition accepted, and the lever never rests at
    [InlineData(25, false)]   // what the SDK header claims; the lever never rests here either
    [InlineData(75, false)]   // the half-deployed detent
    [InlineData(100, false)]
    public void SpeedbrakeArmed_is_the_measured_detent_50(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsArmed(lever));

    [Theory]
    [InlineData(75, true)]     // measured half-deployed detent
    [InlineData(100, true)]    // measured full-up detent
    [InlineData(50, false)]    // armed is not deployed
    [InlineData(0, false)]
    public void SpeedbrakeDeployed_starts_above_the_arm_detent(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsDeployed(lever));

    [Fact]
    public void Deployed_percent_is_measured_from_the_arm_detent()
    {
        // PMDG names the 75 detent "_50", so a correct mapping must call it 50 percent.
        // This is what the ORIGINAL panel decoder already did — (v-50)/50 — and it was
        // right; the 25-based reading briefly introduced here was the regression.
        Assert.Equal(0, Pmdg777SpeedbrakeLever.DeployedPercent(50));
        Assert.Equal(50, Pmdg777SpeedbrakeLever.DeployedPercent(75));
        Assert.Equal(100, Pmdg777SpeedbrakeLever.DeployedPercent(100));
    }

    [Fact]
    public void The_measured_detents_are_pinned_so_the_SDK_comment_cannot_creep_back()
    {
        Assert.Equal(0, Pmdg777SpeedbrakeLever.DownValue);
        Assert.Equal(50, Pmdg777SpeedbrakeLever.ArmedValue);
        Assert.Equal(100, Pmdg777SpeedbrakeLever.UpValue);
        Assert.Equal(75, Pmdg777SpeedbrakeLever.HalfDeployedValue);
    }

    [Fact]
    public void LandingChecklist_speedbrake_accepts_the_armed_lever()
    {
        var item = Item("LANDING_CL", "LDG_SPEEDBRAKE");
        Assert.True(item.EvaluateState(Pmdg777SpeedbrakeLever.ArmedValue),
            "ticking 'Speedbrake: ARMED' must not revert once the lever reaches ARM");
        Assert.False(item.EvaluateState(Pmdg777SpeedbrakeLever.DownValue));
    }

    [Fact]
    public void LandingFlow_speedbrake_verification_accepts_the_armed_lever()
    {
        var arm = Step("LANDING", "LD_SPEEDBRAKE_ARM");
        Assert.Equal("FCTL_Speedbrake_Lever", arm.VerifyFieldName);
        Assert.NotNull(arm.VerifyCondition);
        Assert.True(arm.VerifyCondition!(Pmdg777SpeedbrakeLever.ArmedValue),
            "the Landing flow must not announce 'Skipping' on a lever it just armed");
        Assert.False(arm.VerifyCondition!(Pmdg777SpeedbrakeLever.DownValue));
    }

    [Fact]
    public void LandingFlow_never_clicks_ARM_over_a_deployed_lever()
    {
        // Clicking the ARM detent retracts a lever the pilot has raised for descent —
        // the same hazard SpeedbrakeArmLadder.ExtendedField guards on the 737. The step
        // needs a skip predicate; without one it is unconditional.
        var arm = Step("LANDING", "LD_SPEEDBRAKE_ARM");
        Assert.NotNull(arm.SkipCondition);
    }

    [Fact]
    public void AfterLandingChecklist_speedbrake_down_uses_the_same_scale()
    {
        var item = Item("AFTER_LANDING", "AL_SPEEDBRAKE");
        Assert.True(item.EvaluateState(Pmdg777SpeedbrakeLever.DownValue));
        Assert.False(item.EvaluateState(Pmdg777SpeedbrakeLever.ArmedValue));
    }

    // =====================================================================
    // 2. Seat belt signs: ON, not AUTO
    //
    // PMDG_777X_SDK.h:154  SIGNS_SeatBeltsSelector  // 0: OFF  1: AUTO   2: ON
    //
    // Both 737 profiles already select ON (2) and detect on "v > 1.5"
    // (PMDG737ChecklistDefinitions PF_BELTS, IFly737ChecklistDefinitions PF_BELTS); the
    // 777 was the only Boeing selecting AUTO. Its own seat-belt AUTOMATION already writes
    // ON/OFF only — AircraftActionExecutor.SetSeatbeltSign(bool) => SetSeatBelts(on ? 2 : 0)
    // — so preflight AUTO contradicted the automation that follows it.
    // =====================================================================

    private const int SeatBeltsOff = 0, SeatBeltsAuto = 1, SeatBeltsOn = 2;

    [Fact]
    public void CockpitPrepFlow_selects_seat_belts_ON()
    {
        var step = Step("COCKPIT_PREP", "CP_SEAT_BELTS");
        Assert.Equal(SeatBeltsOn, step.TargetValue);
        Assert.Contains("ON", step.Label);
    }

    [Theory]
    [InlineData("PREFLIGHT", "PF_SEAT_BELTS")]
    [InlineData("BEFORE_START", "BS_SEAT_BELTS")]
    [InlineData("BEFORE_START_CL", "BSCL_SIGNS")]
    public void SeatBeltItems_require_ON_and_reject_AUTO(string groupId, string itemId)
    {
        var item = Item(groupId, itemId);
        Assert.True(item.EvaluateState(SeatBeltsOn));
        Assert.False(item.EvaluateState(SeatBeltsAuto));
        Assert.False(item.EvaluateState(SeatBeltsOff));
    }

    // =====================================================================
    // 3. Oxygen tests sit with the other system tests, not at the bottom
    //
    // The Preflight CHECKLIST kept the position of the old single PF_OXYGEN item when it
    // was split per side (9f21d3f2), so the two oxygen items sat at #44/#45 of 49 - after
    // the gear lever, the CDU preflight and the FMC perf entry - while the COCKPIT_PREP
    // FLOW runs them 7th, just before the fire test. The 737 has them at #1/#2.
    // FoSystemTestsStructureTests already pins the FLOW order; this pins the CHECKLIST.
    // =====================================================================

    [Fact]
    public void PreflightChecklist_runs_the_oxygen_tests_before_the_fire_test()
    {
        var ids = ItemIds("PREFLIGHT");
        int capt = ids.IndexOf("PF_OXY_TEST_CAPT");
        int fo = ids.IndexOf("PF_OXY_TEST_FO");
        int fire = ids.IndexOf("PF_FIRE_TEST");
        Assert.True(capt >= 0 && fo >= 0 && fire >= 0);
        Assert.Equal(capt + 1, fo);
        Assert.True(fo < fire,
            $"oxygen tests must precede the fire test (capt={capt}, fo={fo}, fire={fire})");
    }

    // =====================================================================
    // 4. Before Start follows PMDG's own printed Before Start Procedure
    //
    // B777_Checklist.xml, "Before Start Procedure", in order:
    //   ... IAS/MACH Set V2 / LNAV Arm as needed / VNAV Arm / initial heading /
    //   initial altitude / doors / windows / SEAT BELTS / clearance to pressurize the
    //   hydraulic systems / hydraulic pumps / fuel pumps / BEACON ON /
    //   CANCEL RECALL x2 / Transponder XPNDR / Stabilizer trim Set for TakeOff /
    //   Aileron trim Verify 0 / Rudder trim Verify 0 / BEFORE START CHECKLIST.
    //
    // MSFSBA had trim NINE items early (#7-9 of 23) and, in the flow, as step 3 of 17 —
    // ahead of the entire APU start and of every hydraulic pump. It also had VNAV before
    // LNAV, and disconnected ground power at a different point in the flow than in the
    // checklist.
    // =====================================================================

    private static void AssertOrder(System.Collections.Generic.List<string> ids,
        params string[] expectedRelativeOrder)
    {
        int prev = -1;
        foreach (var id in expectedRelativeOrder)
        {
            int at = ids.IndexOf(id);
            Assert.True(at >= 0, $"{id} missing");
            Assert.True(at > prev, $"{id} (index {at}) must follow the item before it");
            prev = at;
        }
    }

    [Fact]
    public void BeforeStartChecklist_sets_trim_after_the_hydraulics_are_pressurised()
    {
        // The reported defect: "Trim is before hydraulic pumps even come on, so can't
        // even be set."
        AssertOrder(ItemIds("BEFORE_START"),
            "BS_HYD_PRESSURIZE", "BS_HYD_PUMPS_ON", "BS_HYD_DEMAND",
            "BS_STAB_TRIM", "BS_AIL_TRIM", "BS_RUD_TRIM");
    }

    [Fact]
    public void BeforeStartChecklist_matches_the_vendor_tail_order()
    {
        AssertOrder(ItemIds("BEFORE_START"),
            "BS_BEACON_ON", "BS_CANCEL_RECALL", "BS_TRANSPONDER", "BS_STAB_TRIM");
    }

    [Fact]
    public void BeforeStartFlow_briefs_trim_after_the_hydraulic_pumps_run()
    {
        AssertOrder(FlowStepIds("BEFORE_START"),
            "BS_HYD_ELEC", "BS_HYD_ENG", "BS_DEMAND_AUTO", "BS_TRIM_SET");
    }

    [Fact]
    public void BeforeTaxi_no_longer_repeats_the_trim_instruction()
    {
        // PMDG's Before Taxi Procedure, Before Taxi Checklist and Before Takeoff
        // Checklist carry no trim checkpoint at all; the read-back already lives on
        // BSCL_TRIM in BEFORE_START_CL.
        Assert.DoesNotContain("BT_SET_TRIM", FlowStepIds("BEFORE_TAXI"));
        Assert.DoesNotContain("BT_SET_TRIM", ItemIds("BEFORE_TAXI"));
    }

    [Fact]
    public void BeforeStartChecklist_arms_LNAV_before_VNAV()
    {
        // Vendor order is LNAV ("Arm as needed") then VNAV ("Arm"); MSFSBA had them
        // inverted, which is half of why the pair reads oddly against Before Takeoff.
        AssertOrder(ItemIds("BEFORE_START"), "BS_V2_SET", "BS_LNAV_SET", "BS_VNAV_ARM");
    }

    [Fact]
    public void BeforeTakeoff_presents_LNAV_and_VNAV_as_a_verification_not_a_second_arming()
    {
        // The Before Takeoff pushes stay — they are the only net that catches an unarmed
        // VNAV on the runway, and the 777 has no other LNAV/VNAV automation. What changes
        // is that they no longer read as a second, competing "arm" instruction.
        foreach (var id in new[] { "BTKO_LNAV", "BTKO_VNAV" })
            Assert.Contains("Verify", Item("BEFORE_TAKEOFF", id).Label);
    }

    // =====================================================================
    // 5. Before Start performs everything its checklist group latches complete
    //
    // A finished flow calls ChecklistManager.MarkGroupComplete, which ticks and latches
    // every item the flow did not explicitly skip. BS_CANCEL_RECALL and BS_TRANSPONDER
    // had no step in the BEFORE_START flow at all, so "Transponder: XPNDR" read complete
    // with the selector untouched — a false completion a blind pilot cannot see.
    // =====================================================================

    [Theory]
    [InlineData("BS_CANCEL_RECALL")]
    [InlineData("BS_TRANSPONDER")]
    public void BeforeStartFlow_performs_every_actionable_item_its_group_latches(string itemId)
    {
        var delivered = PMDG777FlowDefinitions.Build()
            .Single(f => f.Id == "BEFORE_START").Steps
            .Select(s => s.CompletesChecklistItemId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();
        Assert.Contains(itemId, delivered);
    }

    [Fact]
    public void BeforeStartFlow_ticks_its_own_groups_beacon_item_not_the_readback_line()
    {
        // BS_BEACON was the only step in all thirteen 777 flows whose
        // CompletesChecklistItemId named a read-back (*_CL) item.
        var step = Step("BEFORE_START", "BS_BEACON");
        Assert.Equal("BS_BEACON_ON", step.CompletesChecklistItemId);
    }

    // =====================================================================
    // 6. Ground power: one mapping, shared with the panel
    //
    // PMDG's ext-power event NAMES are reversed against the ELEC_annunExtPowr_ON[2]
    // array — the +7 event (named SEC) drives array index 0. The panel has applied that
    // swap since e051748d ("Verified via live sim testing"); the First Officer profile
    // never did, so every FO ground-power press targeted the OTHER receptacle.
    // =====================================================================

    [Fact]
    public void GroundPowerGate_maps_each_annunciator_index_to_the_event_that_drives_it()
    {
        Assert.Equal("EVT_OH_ELEC_GRD_PWR_SEC_SWITCH", GroundPowerGate.EventForAnnunciatorIndex(0));
        Assert.Equal("EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH", GroundPowerGate.EventForAnnunciatorIndex(1));
    }

    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_GND_PWR_PRIM", 0)]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_GND_PWR_SEC", 1)]
    [InlineData("BEFORE_START", "BS_GND_PWR_1", 0)]
    [InlineData("BEFORE_START", "BS_GND_PWR_2", 1)]
    [InlineData("SECURE", "SEC_GND_PWR_PRIM", 0)]
    [InlineData("SECURE", "SEC_GND_PWR_SEC", 1)]
    public void GroundPowerSteps_fire_the_event_driving_the_annunciator_they_gate_on(
        string flowId, string stepId, int annunciatorIndex) =>
        Assert.Equal(GroundPowerGate.EventForAnnunciatorIndex(annunciatorIndex),
                     Step(flowId, stepId).EventName);

    [Fact]
    public void BothGroundPowerSides_keep_distinct_events()
    {
        // The one thing the swap must not break: a two-GPU stand must still end with both
        // receptacles dropped. Guards against a copy-paste mapping both indices to one event.
        Assert.NotEqual(Step("ELECTRICAL_POWER_UP", "EPU_GND_PWR_PRIM").EventName,
                        Step("ELECTRICAL_POWER_UP", "EPU_GND_PWR_SEC").EventName);
    }

    [Fact]
    public void BeforeStart_disconnects_ground_power_at_the_same_point_in_flow_and_checklist()
    {
        // Flow and checklist disagreed by four mirrored pairs: the flow dropped ground
        // power after the beacon, the checklist listed it before the hydraulics.
        AssertOrder(ItemIds("BEFORE_START"), "BS_BEACON_ON", "BS_EXT_PWR_OFF");
        AssertOrder(FlowStepIds("BEFORE_START"), "BS_BEACON", "BS_GND_PWR_1");
    }

    // =====================================================================
    // 7. Flow and checklist never disagree about ORDER
    //
    // A flow step that names a CompletesChecklistItemId is the one machine-checkable
    // link between the two lists. Ticks must walk each group top-to-bottom in the order
    // the flow performs them — otherwise a pilot running the flow hears boxes tick out of
    // sequence, which is exactly what the oxygen pair did (flow step 7, checklist item 44).
    // =====================================================================

    [Fact]
    public void Every_flow_ticks_its_checklist_items_in_the_order_they_are_listed()
    {
        var groups = PMDG777ChecklistDefinitions.Build();

        foreach (var flow in PMDG777FlowDefinitions.Build())
        {
            // Group the flow's linked ticks by the group each item belongs to, keeping
            // flow order, then assert each group's indices only ever increase.
            var linked = flow.Steps
                .Select(s => s.CompletesChecklistItemId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

            foreach (var group in groups)
            {
                var ids = group.Items.Select(i => i.Id).ToList();
                var walked = linked.Where(id => ids.Contains(id!)).ToList();
                int prev = -1;
                foreach (var id in walked)
                {
                    int at = ids.IndexOf(id!);
                    Assert.True(at > prev,
                        $"flow '{flow.Id}' ticks '{id}' (index {at} of group '{group.Id}') " +
                        $"after an item at index {prev} — the two lists disagree about order");
                    prev = at;
                }
            }
        }
    }

    [Fact]
    public void Every_flow_tick_names_an_item_that_exists_in_one_of_the_flows_own_groups()
    {
        // BS_BEACON used to tick BSCL_BEACON — an item of the read-back group, not of its
        // own. A step must deliver an item of a group the flow declares.
        var groups = PMDG777ChecklistDefinitions.Build();

        foreach (var flow in PMDG777FlowDefinitions.Build())
        {
            var related = flow.RelatedChecklistGroupIds.ToHashSet();
            foreach (var step in flow.Steps)
            {
                if (string.IsNullOrEmpty(step.CompletesChecklistItemId)) continue;
                bool found = groups.Any(g => related.Contains(g.Id)
                                          && g.Items.Any(i => i.Id == step.CompletesChecklistItemId));
                Assert.True(found,
                    $"flow '{flow.Id}' step '{step.Id}' ticks '{step.CompletesChecklistItemId}', " +
                    "which is not an item of any group the flow declares as related");
            }
        }
    }

    [Fact]
    public void PreflightChecklist_and_CockpitPrepFlow_agree_on_where_the_tests_run()
    {
        // The flow and the checklist describe the same phase; a pilot working either
        // top-to-bottom must meet the system tests in the same place.
        var flow = FlowStepIds("COCKPIT_PREP");
        var items = ItemIds("PREFLIGHT");

        static double Fraction(System.Collections.Generic.List<string> l, string id) =>
            (double)l.IndexOf(id) / l.Count;

        Assert.True(System.Math.Abs(Fraction(flow, "CP_OXY_TEST_CAPT")
                                  - Fraction(items, "PF_OXY_TEST_CAPT")) < 0.25,
            "the oxygen test must not sit near the top of the flow and the bottom of the checklist");
    }
}
