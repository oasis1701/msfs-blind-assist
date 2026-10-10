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
    // The First Officer reads the lever from L:switch_498_a (main's FCTL_Speedbrake key), never
    // the SDK's FCTL_Speedbrake_Lever byte. Measured 2026-09-30 (PR #261, hydraulics
    // pressurised): the lever rests at DOWN 0 / ARM 200 / 50 percent 300 / UP 400; the byte is
    // that value / 4, TRUNCATED, so a lever at 201-203 (spoilers already 34 percent up) read 50,
    // "armed", and a hardware axis's DOWN at 22 read 5, "not down". The SDK header's
    // "25: ARMED" is wrong on both scales. The values come from main's PmdgSpeedBrakeLever.B777.
    // =====================================================================

    [Theory]
    [InlineData(0, true)]
    [InlineData(22, true)]     // a hardware axis parks DOWN here
    [InlineData(199, true)]    // short of ARM is not armed
    [InlineData(200, false)]
    [InlineData(300, false)]
    [InlineData(400, false)]
    public void SpeedbrakeDown_is_anything_short_of_ARM(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsDown(lever));

    [Theory]
    [InlineData(200, true)]
    [InlineData(199, false)]
    [InlineData(201, false)]   // spoilers already 34 percent up
    [InlineData(50, false)]    // the old SDK-byte value
    [InlineData(0, false)]
    [InlineData(400, false)]
    public void SpeedbrakeArmed_is_exactly_the_ARM_detent(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsArmed(lever));

    [Theory]
    [InlineData(201, true)]
    [InlineData(300, true)]
    [InlineData(400, true)]
    [InlineData(200, false)]
    [InlineData(0, false)]
    public void SpeedbrakeDeployed_is_anything_past_ARM(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsDeployed(lever));

    [Fact]
    public void An_unread_lever_is_none_of_the_three()
    {
        Assert.False(Pmdg777SpeedbrakeLever.IsDown(double.NaN));
        Assert.False(Pmdg777SpeedbrakeLever.IsArmed(double.NaN));
        Assert.False(Pmdg777SpeedbrakeLever.IsDeployed(double.NaN));
    }

    [Fact]
    public void The_measured_detents_come_from_mains_table()
    {
        Assert.Equal(0.0, Pmdg777SpeedbrakeLever.DownValue);
        Assert.Equal(200.0, Pmdg777SpeedbrakeLever.ArmedValue);
        Assert.Equal(300.0, Pmdg777SpeedbrakeLever.HalfDeployedValue);
        Assert.Equal(400.0, Pmdg777SpeedbrakeLever.UpValue);
    }

    // The Landing section's arming line and the Landing Checklist's read-only line both read the
    // lever exactly at ARM.
    [Theory]
    [InlineData("LANDING", "LDA_SPEEDBRAKE")]
    [InlineData("LANDING_CL", "LDG_SPEEDBRAKE")]
    public void Landing_speedbrake_lines_accept_the_armed_lever(string groupId, string itemId)
    {
        var item = Item(groupId, itemId);
        Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, item.StateFieldName);
        Assert.True(item.EvaluateState(Pmdg777SpeedbrakeLever.ArmedValue),
            "a ticked speedbrake line must not revert once the lever reaches ARM");
        Assert.False(item.EvaluateState(Pmdg777SpeedbrakeLever.DownValue));
        Assert.False(item.EvaluateState(201));
    }

    // A 777 evaluator whose speed-brake lever reads `lever` (null = never delivered), the same
    // shape as Pmdg777SpeedbrakeLeverReadTests.With. The leave-alone and already-set predicates
    // are pinned through it, so swapping IsSpeedbrakeArmed and IsSpeedbrakeDeployed fails.
    private static AircraftStateEvaluator LeverReading(double? lever)
    {
        var eval = new AircraftStateEvaluator();
        eval.SetCachedValueSource(key => key == SpeedbrakeLeverState.Pmdg777.LeverKey ? lever : null);
        return eval;
    }

    [Fact]
    public void LandingSection_speedbrake_leaves_a_deployed_lever_alone()
    {
        var item = Item("LANDING", "LDA_SPEEDBRAKE");
        Assert.NotNull(item.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, item.LeaveAloneText);

        Assert.True(item.LeaveAloneWhen!(LeverReading(201)), "spoilers 34 percent up: leave it");
        Assert.False(item.LeaveAloneWhen!(LeverReading(200)), "ARMED is not deployed");
        Assert.False(item.LeaveAloneWhen!(LeverReading(0)), "DOWN is not deployed");
        Assert.False(item.LeaveAloneWhen!(LeverReading(null)), "an unread lever is not known deployed");
    }

    [Fact]
    public void LandingFlow_speedbrake_goes_through_the_verified_arm()
    {
        var arm = Step("LANDING", "LD_SPEEDBRAKE_ARM");
        Assert.Equal(SpeedbrakeLeverState.ArmPseudoKey, arm.EventName);
        Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, arm.VerifyFieldName);
        Assert.NotNull(arm.VerifyCondition);
        Assert.True(arm.VerifyCondition!(Pmdg777SpeedbrakeLever.ArmedValue),
            "the Landing flow must not announce 'Skipping' on a lever it just armed");
        Assert.False(arm.VerifyCondition!(Pmdg777SpeedbrakeLever.DownValue));
    }

    [Fact]
    public void LandingFlow_never_clicks_ARM_over_a_deployed_lever()
    {
        // Clicking the ARM detent retracts a lever the pilot has raised. Armed is "Already
        // set"; deployed is left alone with its reason — never "Already set".
        var arm = Step("LANDING", "LD_SPEEDBRAKE_ARM");
        Assert.NotNull(arm.SkipCondition);
        Assert.NotNull(arm.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, arm.LeaveAloneText);

        // Already set: exactly the ARM detent.
        Assert.True(arm.SkipCondition!(LeverReading(200)));
        Assert.False(arm.SkipCondition!(LeverReading(201)), "deployed is not 'Already set'");
        Assert.False(arm.SkipCondition!(LeverReading(0)));
        Assert.False(arm.SkipCondition!(LeverReading(null)));

        // Left alone: anything past ARM.
        Assert.True(arm.LeaveAloneWhen!(LeverReading(201)));
        Assert.False(arm.LeaveAloneWhen!(LeverReading(200)));
        Assert.False(arm.LeaveAloneWhen!(LeverReading(0)));
        Assert.False(arm.LeaveAloneWhen!(LeverReading(null)));
    }

    [Fact]
    public void AfterLandingChecklist_speedbrake_down_uses_the_same_scale()
    {
        var item = Item("AFTER_LANDING", "AL_SPEEDBRAKE");
        Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, item.StateFieldName);
        Assert.True(item.EvaluateState(Pmdg777SpeedbrakeLever.DownValue));
        Assert.True(item.EvaluateState(22), "a hardware axis parks DOWN at 22");
        Assert.False(item.EvaluateState(Pmdg777SpeedbrakeLever.ArmedValue));
    }

    // =====================================================================
    // 2. Seat belt signs: OFF in Cockpit Prep, ON (not AUTO) in Before Start
    //
    // PMDG_777X_SDK.h:154  SIGNS_SeatBeltsSelector  // 0: OFF  1: AUTO   2: ON
    //
    // PMDG's Preflight Procedure reads "SEAT BELTS selector .. OFF", its Before Start
    // Procedure "AUTO". Before Start selects ON, not AUTO (owner ruling 2026-08-29): both 737
    // profiles already select ON (2) and detect on "v > 1.5" (PMDG737ChecklistDefinitions
    // PF_BELTS, IFly737ChecklistDefinitions PF_BELTS), and the 777's own seat-belt AUTOMATION
    // writes ON/OFF only — AircraftActionExecutor.SetSeatbeltSign(bool) => SetSeatBelts(on ? 2 : 0)
    // — so AUTO contradicted the automation that follows it. Cockpit Prep applied the same ON
    // until 2026-10-09; it now follows PMDG's Preflight, OFF (owner, 2026-10-09).
    // =====================================================================

    private const int SeatBeltsOff = 0, SeatBeltsAuto = 1, SeatBeltsOn = 2;

    [Fact]
    public void CockpitPrepFlow_selects_seat_belts_OFF_as_PMDGs_preflight()
    {
        var step = Step("COCKPIT_PREP", "CP_SEAT_BELTS");
        Assert.Equal(SeatBeltsOff, step.TargetValue);
        Assert.Contains("OFF", step.Label);
        // "Already set" only over a selector that READS OFF: an unread one (NaN) is not OFF.
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    [Fact]
    public void PreflightSeatBeltItem_requires_OFF()
    {
        var item = Item("PREFLIGHT", "PF_SEAT_BELTS");
        Assert.True(item.EvaluateState(SeatBeltsOff));
        Assert.False(item.EvaluateState(SeatBeltsAuto));
        Assert.False(item.EvaluateState(SeatBeltsOn));
        Assert.False(item.EvaluateState(double.NaN));
    }

    [Theory]
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
    // Owner-ruled OMISSIONS (2026-09-22, "not needed there"): MSFSBA deliberately carries
    // neither "LNAV Arm as needed" nor "VNAV Arm" nor "clearance to pressurize the
    // hydraulic systems" — Before Takeoff's "Verify armed" pair is where the First
    // Officer handles LNAV/VNAV.
    //
    // MSFSBA had trim NINE items early (#7-9 of 23) and, in the flow, as step 3 of 17 —
    // ahead of the entire APU start and of every hydraulic pump. It also disconnected
    // ground power at a different point in the flow than in the checklist.
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
            "BS_HYD_PUMPS_ON", "BS_HYD_DEMAND",
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

    // Preflight and Before Start — flow descriptions and steps, checklist items — alike.
    // Single() so a renamed id fails loudly instead of silently scanning nothing.
    private static readonly string[] PreStartFlowIds = { "COCKPIT_PREP", "BEFORE_START" };
    private static readonly string[] PreStartGroupIds =
        { "PREFLIGHT", "PREFLIGHT_CL", "BEFORE_START", "BEFORE_START_CL" };

    private static System.Collections.Generic.IEnumerable<(string Where, string Text)> PreStartLines()
    {
        var flows = PMDG777FlowDefinitions.Build();
        foreach (var id in PreStartFlowIds)
        {
            var flow = flows.Single(f => f.Id == id);
            if (!string.IsNullOrEmpty(flow.Description)) yield return ($"flow {id} description", flow.Description);
            foreach (var s in flow.Steps)
                foreach (var t in new[] { s.Label, s.SpokenLabel, s.ReminderText })
                    if (!string.IsNullOrEmpty(t)) yield return ($"flow {id} step {s.Id}", t);
        }
        var groups = PMDG777ChecklistDefinitions.Build();
        foreach (var id in PreStartGroupIds)
            foreach (var i in groups.Single(g => g.Id == id).Items)
                foreach (var t in new[] { i.Label, i.ReminderText })
                    if (!string.IsNullOrEmpty(t)) yield return ($"group {id} item {i.Id}", t);
    }

    [Fact]
    public void Preflight_and_BeforeStart_no_longer_ask_for_LNAV_or_VNAV()
    {
        // Owner ruling 2026-09-22: arming LNAV/VNAV is not needed before engine start,
        // even though PMDG's printed Before Start Procedure lists it. Before Takeoff's
        // "Verify armed" pair (below) is where the First Officer handles them.
        foreach (var (where, text) in PreStartLines())
            Assert.False(
                text.Contains("LNAV", System.StringComparison.OrdinalIgnoreCase)
                || text.Contains("VNAV", System.StringComparison.OrdinalIgnoreCase),
                $"{where}: \"{text}\"");
    }

    [Fact]
    public void Preflight_and_BeforeStart_no_longer_ask_for_clearance_to_pressurize_the_hydraulics()
    {
        // Owner ruling 2026-09-22, the same as LNAV/VNAV: the vendor's Before Start page
        // lists "clearance to pressurize the hydraulic systems"; MSFSBA deliberately does
        // not. The hydraulic pumps themselves, and trim after them, are unchanged.
        foreach (var (where, text) in PreStartLines())
            Assert.False(
                text.Contains("pressuri", System.StringComparison.OrdinalIgnoreCase)
                && text.Contains("clearance", System.StringComparison.OrdinalIgnoreCase),
                $"{where}: \"{text}\"");
    }

    [Fact]
    public void BeforeTakeoff_is_where_LNAV_and_VNAV_get_armed()
    {
        // With Preflight and Before Start no longer asking for them (owner ruling
        // 2026-09-22), these are the First Officer's ONLY LNAV/VNAV lines — and the only
        // net that catches an unarmed VNAV on the runway, since the 777 profile has no
        // other LNAV/VNAV automation. They read as a check ("Verify armed"), and a tick
        // presses the button when the annunciator shows the mode unarmed.
        foreach (var id in new[] { "BTKO_LNAV", "BTKO_VNAV" })
        {
            var item = Item("BEFORE_TAKEOFF", id);
            Assert.Contains("Verify", item.Label);
            Assert.NotNull(item.CheckAction);
        }
        foreach (var id in new[] { "BTKOF_LNAV", "BTKOF_VNAV" })
            Assert.Contains(id, FlowStepIds("BEFORE_TAKEOFF"));
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
            //
            // TWO items are left out, deliberately, for one reason: a flow completes its
            // read-back's gear line from its LAST step, a read-only up-to-20 s check that the gear
            // is physically there (lever plus the stock gear-leg positions — the 777 SDK has no
            // gear lights), placed last so a slow gear confirmation never holds up the flaps
            // (FO-13). The After Takeoff flow completes AFTER_TKOF_CL's ATKOF_GEAR ("Landing
            // Gear: UP", listed first) after ATKOF_FLAPS; since 2026-10-09 the Landing flow
            // completes LANDING_CL's LDG_GEAR ("Landing Gear: DOWN", listed second) after its
            // flap-lever check completes LDG_FLAPS. Only those two are excused; every other item
            // in every group, including any later added to either read-back, is still checked.
            var linked = flow.Steps
                .Select(s => s.CompletesChecklistItemId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Where(id => !(flow.Id == "AFTER_TAKEOFF" && id == "ATKOF_GEAR"))
                .Where(id => !(flow.Id == "LANDING" && id == "LDG_GEAR"))
                // A line several steps deliver (both wiper sides → one "Wiper selectors: OFF";
                // left FD, both A/T ARMs, right FD in physical MCP order → "Flight Director
                // switches: ON" and "Autothrottle arm switches: ARM") is placed by the FIRST
                // step that names it.
                .Distinct()
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

    // =====================================================================
    // 8. Flows tick their checklist, made honest (2026-10-09)
    //
    // A finished flow ticks and latches every line of its groups, so every line the First
    // Officer can read or act on has a step behind it: a write that links it, or a read-only
    // check. Each new step sits where its line sits in the procedure.
    // =====================================================================

    [Fact]
    public void Flap_lever_checks_sit_where_their_lines_are_flown()
    {
        // Before Taxi: right after the plan-gated flap steps, before the packs.
        var bt = FlowStepIds("BEFORE_TAXI");
        Assert.Equal(bt.IndexOf("BT_FLAPS_25") + 1, bt.IndexOf("BT_FLAPS_CHECK"));
        AssertOrder(bt, "BT_FLAPS_CHECK", "BT_PACK_L_AUTO");

        // Before Takeoff: after the Captain's flap confirmation, and last.
        var btkof = FlowStepIds("BEFORE_TAKEOFF");
        Assert.Equal(btkof.IndexOf("BTKOF_FLAPS_CONFIRM") + 1, btkof.IndexOf("BTKOF_FLAPS_CHECK"));
        Assert.Equal("BTKOF_FLAPS_CHECK", btkof[^1]);

        // Landing: right before the gear check, which stays LAST (FO-13).
        var ld = FlowStepIds("LANDING");
        Assert.Equal(new[] { "LD_FLAPS_CHECK", "LD_GEAR_DOWN_CHECK" }, ld.Skip(ld.Count - 2).ToArray());

        // Shutdown: right after the fuel pumps, as the Shutdown Checklist reads.
        var sd = FlowStepIds("SHUTDOWN");
        Assert.Equal(sd.IndexOf("SD_FUEL_PUMPS") + 1, sd.IndexOf("SD_FLAPS_UP_CHECK"));
    }

    [Fact]
    public void Reused_writes_sit_where_their_lines_are_flown()
    {
        // Before Start: PMDG's SEAT BELTS line follows the MCP setup; the flow sets it before the
        // APU start, where its checklist group lists it.
        AssertOrder(FlowStepIds("BEFORE_START"), "BS_MCP_SPEEDS", "BS_SEAT_BELTS", "BS_APU_ON");
        AssertOrder(ItemIds("BEFORE_START"), "BS_SEAT_BELTS", "BS_APU_START");

        // Before Taxi: autobrake RTO after the packs; PMDG's flight-control check and then RECALL
        // close the procedure.
        var bt = FlowStepIds("BEFORE_TAXI");
        AssertOrder(bt, "BT_PACK_R_AUTO", "BT_AUTOBRAKE_RTO", "BT_FCTL_CHECK", "BT_RECALL");
        Assert.Equal("BT_RECALL", bt[^1]);

        // Descent Setup: the RECALL push keeps the reminder's place.
        AssertOrder(FlowStepIds("DESCENT_SETUP"), "DSC_EFIS_FO_RANGE", "DSC_RECALL", "DSC_APPROACH_BRIEF");
    }

    [Fact]
    public void Cockpit_prep_checks_the_power_up_first_and_turns_the_pumps_off_beside_crossfeed()
    {
        var cp = FlowStepIds("COCKPIT_PREP");
        // The power-up lines head the Preflight group (PF_BATTERY, PF_ADIRU) and PMDG's preflight
        // opens with the battery, so the check opens the flow. The order guard
        // (Every_flow_ticks_its_checklist_items_in_the_order_they_are_listed) reads only
        // CompletesChecklistItemId (PF_BATTERY), so it never sees the check's four Also lines,
        // three of which (PF_DEMAND_PUMPS_OFF, PF_NAV_LIGHTS, PF_GEAR_DOWN) the group lists far
        // below: by design, so do not move the check to "fix" that order.
        Assert.Equal("CP_POWER_UP_CHECK", cp[0]);
        // PMDG's order (FOB-4): CROSSFEED switches OFF, then FUEL PUMP switches OFF; the
        // Preflight group lists the two lines the same way.
        Assert.Equal(cp.IndexOf("CP_XFEED_OFF") + 1, cp.IndexOf("CP_FUEL_PUMPS_OFF"));
        var items = ItemIds("PREFLIGHT");
        Assert.Equal(items.IndexOf("PF_CROSSFEED_OFF") + 1, items.IndexOf("PF_FUEL_PUMPS_OFF"));
    }

    // =====================================================================
    // 9. Non-PMDG steps (FOB-4, owner 2026-10-09)
    //
    // PMDG's procedure pages have neither of these two steps, and the owner removed both. The
    // other non-PMDG steps stay as the owner's choices, listed in FOB-4's full text.
    // =====================================================================

    private static System.Collections.Generic.List<string> WrittenEvents(string flowId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps
            .SelectMany(s => s.EventName is null ? s.MultiActions.Select(a => a.EventName) : new[] { s.EventName })
            .ToList();

    [Fact]
    public void After_takeoff_leaves_the_landing_lights_to_the_10000_ft_automation()
    {
        Assert.DoesNotContain("ATKOF_LANDING_OFF", FlowStepIds("AFTER_TAKEOFF"));
        Assert.DoesNotContain("ATKO_LANDING_OFF", ItemIds("AFTER_TAKEOFF"));
        Assert.DoesNotContain(WrittenEvents("AFTER_TAKEOFF"),
            e => e.StartsWith("EVT_OH_LIGHTS_LANDING_", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Shutdown_no_longer_turns_the_storm_lights_on()
    {
        Assert.DoesNotContain("SD_STORM", FlowStepIds("SHUTDOWN"));
        Assert.DoesNotContain("SD_STORM_ON", ItemIds("SHUTDOWN"));
        Assert.DoesNotContain("EVT_OH_LIGHTS_STORM", WrittenEvents("SHUTDOWN"));
    }

    // The steps and Captain reminders with no line on PMDG's pages that the flows KEEP as the
    // owner's choices (checked against every procedure page of B777_Checklist.xml, 2026-10-09).
    // FOB-4's full text lists each, and each must still be a 777 flow step, so the list cannot
    // outlive a rename or a removal unnoticed.
    private static readonly string[] OwnersChoices =
    {
        "EPU_STORM_OFF", "EPU_CVR", "CP_TCAS_TEST", "CP_WXR_TEST", "CP_NO_SMOKING", "CP_GASPER",
        "BT_TAXI_LIGHTS", "BT_STORM_OFF", "BTKOF_LANDING_L", "BTKOF_TURNOFF", "BTKOF_STROBE", "BTKOF_XPNDR",
        "ATKOF_TURNOFF_OFF", "DSC_EFIS_FO_MODE", "DSC_EFIS_FO_RANGE", "SD_ENG_PUMPS",
        "CP_ATIS", "BS_START_ACARS", "BS_TAXI_CLR", "LD_MISSED", "CP_RESET_CL",
    };

    [Fact]
    public void Fob4_lists_every_non_PMDG_step_the_flows_keep_and_the_two_removed()
    {
        string text = System.IO.File.ReadAllText(System.IO.Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "docs", "invariants", "first-officer-boeing.md"));
        int start = text.IndexOf("## FOB-4", System.StringComparison.Ordinal);
        string fob4 = text[start..text.IndexOf("## FOB-5", start, System.StringComparison.Ordinal)];
        var steps = PMDG777FlowDefinitions.Build().SelectMany(f => f.Steps).Select(s => s.Id).ToList();

        foreach (string id in OwnersChoices)
        {
            Assert.True(steps.Contains(id), $"{id} is not a 777 flow step any more: update FOB-4's full text");
            Assert.True(fob4.Contains($"`{id}`", System.StringComparison.Ordinal), $"FOB-4's full text does not list {id}");
        }
        foreach (string removed in new[] { "ATKOF_LANDING_OFF", "SD_STORM" })
        {
            Assert.DoesNotContain(removed, steps);
            Assert.True(fob4.Contains($"`{removed}`", System.StringComparison.Ordinal), $"FOB-4's full text does not record {removed}");
        }
    }
}
