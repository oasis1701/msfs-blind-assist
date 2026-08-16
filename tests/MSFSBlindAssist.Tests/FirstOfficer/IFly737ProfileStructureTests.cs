using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural invariants for the iFly 737 MAX8 First Officer checklist AND flow data
/// (<see cref="IFly737ChecklistDefinitions"/>, <see cref="IFly737FlowDefinitions"/>) —
/// the checklist half is Task 5's structure test suite, the flow half (below) is
/// Task 6's. Pure-logic over the data-driven Build() lists; no live SDK needed,
/// mirroring the fleet's other *ProfileStructureTests/ChecklistRefinementTests suites.
/// </summary>
public class IFly737ProfileStructureTests
{
    // Same 24 group ids as the PMDG 737 template, in flight order.
    private static readonly string[] ExpectedGroupIds =
    {
        "ELEC_POWER_UP",
        "PREFLIGHT", "PREFLIGHT_CL",
        "BEFORE_START", "BEFORE_START_CL",
        "ENGINE_START",
        "BEFORE_TAXI", "BEFORE_TAXI_CL",
        "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL",
        "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL",
        "DESCENT", "DESCENT_CL",
        "APPROACH", "APPROACH_CL",
        "LANDING", "LANDING_CL",
        "AFTER_LANDING",
        "SHUTDOWN", "SHUTDOWN_CL",
        "SECURE", "SECURE_CL",
        "ELEC_POWER_DOWN",
    };

    private static System.Collections.Generic.List<ChecklistGroup<IFly737ActionExecutor, IFly737StateEvaluator>> Groups()
        => IFly737ChecklistDefinitions.Build();

    [Fact]
    public void AllPmdg737PhaseGroupsExist()
    {
        var groups = Groups();
        var ids = groups.Select(g => g.Id).ToArray();

        // Every expected id is present, no duplicates, and no stray extras — a totality
        // check, not just a subset check, so a typo'd or dropped group id fails loudly.
        Assert.Equal(ExpectedGroupIds.Length, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (string id in ExpectedGroupIds)
            Assert.Contains(id, ids);

        // Flight order must match exactly — a reordered group would silently change
        // where a pilot lands when opening the checklist tree at the current phase.
        Assert.Equal(ExpectedGroupIds, ids);

        // No group is empty — an empty group is either a typo'd Id (items landed under
        // the wrong group id) or a forgotten Build*() call.
        Assert.All(groups, g => Assert.NotEmpty(g.Items));
    }

    [Fact]
    public void ReadbackGroups_AreActionFree()
    {
        var groups = Groups();
        Assert.Contains(groups, g => g.Id.EndsWith("_CL"));
        foreach (var g in groups.Where(g => g.Id.EndsWith("_CL")))
            Assert.All(g.Items, i => Assert.True(i.CheckAction == null,
                $"{g.Id}.{i.Id} is a readback item but has a non-null CheckAction"));
    }

    [Fact]
    public void AutoItems_AreRevertToState()
    {
        var groups = Groups();
        var autoItems = groups.SelectMany(g => g.Items)
            .Where(i => i.Type == ChecklistItemType.AutoDetectable)
            .ToArray();
        Assert.NotEmpty(autoItems);
        Assert.All(autoItems, i => Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior));
    }

    [Fact]
    public void TakeoffFlaps_LandingAutobrake_Speedbrake_AreReminders()
    {
        var groups = Groups();

        // "Set the takeoff flaps" (Before Taxi) — Captain reminder, never automated.
        var takeoffFlaps = groups.First(g => g.Id == "BEFORE_TAXI").Items.Single(i => i.Id == "BT_FLAPS");
        Assert.Equal(ChecklistItemType.CaptainReminder, takeoffFlaps.Type);
        Assert.Null(takeoffFlaps.CheckAction);

        // "Set the landing autobrake" (Descent) — Captain reminder.
        var landingAutobrake = groups.First(g => g.Id == "DESCENT").Items.Single(i => i.Id == "DSA_AB");
        Assert.Equal(ChecklistItemType.CaptainReminder, landingAutobrake.Type);
        Assert.Null(landingAutobrake.CheckAction);

        // Speedbrake ARM (Landing) — Captain reminder on this aircraft (unverified lever
        // write scale, deliberately read-only).
        var speedbrakeArm = groups.First(g => g.Id == "LANDING").Items.Single(i => i.Id == "LDA_SPDBRK");
        Assert.Equal(ChecklistItemType.CaptainReminder, speedbrakeArm.Type);
        Assert.Null(speedbrakeArm.CheckAction);

        // Its Landing Checklist twin auto-detects (the iFly DOES expose a speedbrake-armed
        // readback the PMDG NG3 struct lacks) but must still be action-free per the _CL
        // invariant checked above.
        var speedbrakeReadback = groups.First(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_SPDBRK");
        Assert.Equal(ChecklistItemType.AutoDetectable, speedbrakeReadback.Type);
        Assert.Null(speedbrakeReadback.CheckAction);
        Assert.Equal("SPEED_BRAKE_ARMED_Light_Status", speedbrakeReadback.StateFieldName);
    }

    [Fact]
    public void MergedFuelPumpItems()
    {
        var groups = Groups();

        // Preflight: single "Fuel pumps: OFF" item, action-based (not a synthetic — a plain
        // all-fields-off condition), covering all six wing+center switches.
        var preflight = groups.First(g => g.Id == "PREFLIGHT").Items.Single(i => i.Id == "PF_FUEL_OFF");
        Assert.Equal("Fuel pumps: OFF", preflight.Label);
        Assert.Equal(ChecklistItemType.AutoDetectable, preflight.Type);
        Assert.Equal(5, preflight.AdditionalStateFields.Count);
        Assert.NotNull(preflight.CheckAction);

        // Before Start: single "Fuel pumps: ON" item keyed on the merged synthetic
        // FO_FUEL_PUMPS_BS_OK (wing ON AND center matches the fuel state) — never a
        // wing+center split, and never a standalone center-pump item.
        var beforeStart = groups.First(g => g.Id == "BEFORE_START").Items.Single(i => i.Id == "BS_FUEL");
        Assert.Equal("Fuel pumps: ON", beforeStart.Label);
        Assert.Equal("FO_FUEL_PUMPS_BS_OK", beforeStart.StateFieldName);
        Assert.Empty(beforeStart.AdditionalStateFields);
        var beforeStartIds = groups.First(g => g.Id == "BEFORE_START").Items.Select(i => i.Id);
        Assert.DoesNotContain("FO_CTR_PUMPS_ON", beforeStartIds);
        Assert.DoesNotContain("BS_CTR_PUMPS_ON", beforeStartIds);

        // Shutdown: single "Fuel pumps: OFF" item, wing-then-center ordering enforced by
        // the underlying executor's serialized dispatch (DispatchCoreAsync gate), not by
        // this data — pin only that both SetWingFuelPumps/SetCenterFuelPumps calls exist
        // by asserting a single merged item with the full six-field detection set.
        var shutdown = groups.First(g => g.Id == "SHUTDOWN").Items.Single(i => i.Id == "SD_FUEL");
        Assert.Equal("Fuel pumps: OFF", shutdown.Label);
        Assert.Equal(5, shutdown.AdditionalStateFields.Count);
        Assert.NotNull(shutdown.CheckAction);

        // No standalone "center pumps" items anywhere in the checklist set — the merged
        // item is the ONLY fuel-pump entry per phase.
        var allIds = groups.SelectMany(g => g.Items).Select(i => i.Id).ToArray();
        Assert.DoesNotContain(allIds, id => id.Contains("CTR_PUMP"));
    }

    /// <summary>
    /// Fix pass 1 (2026-08), Fix 3: every StateFieldName/AdditionalStateFields entry across all
    /// 24 groups must be EITHER a synthetic FO_-prefixed key (computed by
    /// <see cref="IFly737StateEvaluator.GetValue"/>, never a raw SDK struct field) OR a real
    /// flattened SDK field name. A misspelled raw field name reads NaN forever from
    /// <see cref="IFly737StateEvaluator.GetValue"/> — ChecklistManager treats NaN as
    /// INDETERMINATE (skips both auto-tick and revert) — which presents to a blind pilot as
    /// "the checklist item never ticks", with no error, no crash, and nothing in a build log to
    /// catch it. Membership is checked against <see cref="IFly737MAXDefinition.FieldOffsetsByKey"/>
    /// rather than re-implementing the Count&gt;1 -> "Name_i" / else-bare-Name flattening rule —
    /// that dictionary IS the app's own flattening (built once from IFlySdkFields.All), so this
    /// test can never drift from the real membership test the evaluator itself relies on.
    /// </summary>
    [Fact]
    public void EveryStateFieldName_IsSyntheticOrARealSdkField()
    {
        var groups = Groups();
        var fieldNames = groups.SelectMany(g => g.Items)
            .Where(i => i.StateFieldName != null)
            .SelectMany(i => new[] { i.StateFieldName! }.Concat(i.AdditionalStateFields))
            .Distinct()
            .ToArray();

        Assert.NotEmpty(fieldNames);

        foreach (var field in fieldNames)
        {
            bool isSynthetic = field.StartsWith("FO_", System.StringComparison.Ordinal);
            bool isRealSdkField = IFly737MAXDefinition.FieldOffsetsByKey.ContainsKey(field);
            Assert.True(isSynthetic || isRealSdkField,
                $"'{field}' is neither a synthetic FO_ key nor a field in " +
                "IFly737MAXDefinition.FieldOffsetsByKey — this reads NaN forever and the " +
                "checklist item that names it will never tick.");
        }
    }

    // =======================================================================
    // Flow structure tests (Task 6) — IFly737FlowDefinitions.Build().
    // =======================================================================

    // Same 13 flow ids + RelatedChecklistGroupIds as PMDG737FlowDefinitions (the template
    // this file ports), in flight order.
    private static readonly (string Id, string[] RelatedChecklistGroupIds)[] ExpectedFlows =
    {
        ("ELECTRICAL_POWER_UP", new[] { "ELEC_POWER_UP" }),
        ("PREFLIGHT", new[] { "PREFLIGHT" }),
        ("BEFORE_START", new[] { "BEFORE_START" }),
        ("ENGINE_START", new[] { "ENGINE_START" }),
        ("BEFORE_TAXI", new[] { "BEFORE_TAXI" }),
        ("BEFORE_TAKEOFF", new[] { "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL" }),
        ("AFTER_TAKEOFF", new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL" }),
        ("DESCENT", new[] { "DESCENT", "DESCENT_CL" }),
        ("APPROACH", new[] { "APPROACH", "APPROACH_CL" }),
        ("LANDING", new[] { "LANDING", "LANDING_CL" }),
        ("AFTER_LANDING", new[] { "AFTER_LANDING" }),
        ("SHUTDOWN", new[] { "SHUTDOWN" }),
        ("SECURE", new[] { "SECURE" }),
    };

    [Fact]
    public void AllThirteenFlowsExist()
    {
        var flows = IFly737FlowDefinitions.Build();
        var ids = flows.Select(f => f.Id).ToArray();

        // Totality + exact flight order, same shape as AllPmdg737PhaseGroupsExist above.
        Assert.Equal(ExpectedFlows.Length, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(ExpectedFlows.Select(f => f.Id).ToArray(), ids);

        foreach (var (id, related) in ExpectedFlows)
        {
            var flow = flows.Single(f => f.Id == id);
            Assert.Equal(related, flow.RelatedChecklistGroupIds);
            Assert.NotEmpty(flow.Steps);
        }

        // Every RelatedChecklistGroupIds entry must name a checklist group that actually
        // exists — a typo'd id would silently break "Run Related Flow" from the Checklist tab.
        var realGroupIds = IFly737ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet();
        foreach (var flow in flows)
            foreach (string gid in flow.RelatedChecklistGroupIds)
                Assert.Contains(gid, realGroupIds);
    }

    [Fact]
    public void EngineStartFlow_GatesFuelOnN2WithStopPolicy()
    {
        var steps = IFly737FlowDefinitions.Build().Single(f => f.Id == "ENGINE_START").Steps;

        // Engine 2: the N2 wait (FO_ENG2_N2, Stop policy) must sit BEFORE the start-lever
        // step that introduces fuel (Engine_Start_Lever_Status_1) — a starter that never
        // spins up must abort the flow rather than move the lever.
        int e2WaitIdx = steps.FindIndex(s => s.ActionType == FlowStepActionType.WaitForCondition
            && s.ConditionFieldName == "FO_ENG2_N2");
        int e2RunIdx = steps.FindIndex(s => s.ActionType == FlowStepActionType.SetSwitch
            && s.EventName == "Engine_Start_Lever_Status_1");
        Assert.True(e2WaitIdx >= 0, "no FO_ENG2_N2 wait step found");
        Assert.True(e2RunIdx >= 0, "no Engine_Start_Lever_Status_1 step found");
        Assert.True(e2WaitIdx < e2RunIdx, "the engine 2 N2 gate must precede the start-lever step");
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[e2WaitIdx].FailurePolicy);
        Assert.NotNull(steps[e2WaitIdx].Condition);
        Assert.True(steps[e2WaitIdx].Condition!(IFly737StateEvaluator.EngStartFuelN2));
        Assert.False(steps[e2WaitIdx].Condition!(IFly737StateEvaluator.EngStartFuelN2 - 1));

        // Engine 1: same shape, mirrored fields.
        int e1WaitIdx = steps.FindIndex(s => s.ActionType == FlowStepActionType.WaitForCondition
            && s.ConditionFieldName == "FO_ENG1_N2");
        int e1RunIdx = steps.FindIndex(s => s.ActionType == FlowStepActionType.SetSwitch
            && s.EventName == "Engine_Start_Lever_Status_0");
        Assert.True(e1WaitIdx >= 0, "no FO_ENG1_N2 wait step found");
        Assert.True(e1RunIdx >= 0, "no Engine_Start_Lever_Status_0 step found");
        Assert.True(e1WaitIdx < e1RunIdx, "the engine 1 N2 gate must precede the start-lever step");
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[e1WaitIdx].FailurePolicy);

        // Engine 2 is started before engine 1 (PMDG 737 convention).
        Assert.True(e2WaitIdx < e1WaitIdx);
    }

    [Fact]
    public void BeforeStartFlow_ApuWait_StopsOnTimeout()
    {
        var steps = IFly737FlowDefinitions.Build().Single(f => f.Id == "BEFORE_START").Steps;

        int waitIdx = steps.FindIndex(s => s.ActionType == FlowStepActionType.WaitForCondition
            && s.ConditionFieldName == "APU_GEN_OFF_BUS_Light_Status");
        Assert.True(waitIdx >= 0, "no APU_GEN_OFF_BUS_Light_Status wait step found");
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[waitIdx].FailurePolicy);
        Assert.NotNull(steps[waitIdx].Condition);
        Assert.True(steps[waitIdx].Condition!(1));
        Assert.False(steps[waitIdx].Condition!(0));

        // No ground-power-OFF press may precede the APU availability gate.
        int gpuOffIdx = steps.FindIndex(s => s.ActionType == FlowStepActionType.SetSwitch
            && s.EventName == "BTN_GRD_PWR_OFF");
        Assert.True(gpuOffIdx >= 0, "no ground-power-off step found");
        Assert.True(waitIdx < gpuOffIdx, "the APU wait must precede the ground-power-off press");
    }

    /// <summary>
    /// Totality safety net: every SetSwitch/SetSwitchMultiple EventName across all 13 flows
    /// must be either a declared executor pseudo-key, or a variable registered on the
    /// definition that is actually WRITABLE. Membership alone is not enough — Task 4's review
    /// found that <see cref="IFly737MAXDefinition.ApplyUIVariable"/> returns true for a
    /// registered but READ-ONLY key (it speaks "X is a read-only indicator" and re-fires
    /// state), so a flow step targeting e.g. Spoiler_Lever_Status would resolve as "valid"
    /// and then silently do nothing in the sim. Writability is read off
    /// <see cref="SimConnect.SimVarDefinition.RenderAsReadOnlyStatus"/> — the flag SwD sets
    /// to true exactly when a field was registered with a null write command.
    /// </summary>
    [Fact]
    public void EverySetSwitchStep_Resolves()
    {
        var vars = new IFly737MAXDefinition().GetVariables();
        var flows = IFly737FlowDefinitions.Build();
        Assert.NotEmpty(flows);

        int checkedKeys = 0;
        foreach (var flow in flows)
        {
            foreach (var step in flow.Steps)
            {
                var keys = new List<string>();
                if (step.ActionType == FlowStepActionType.SetSwitch && step.EventName != null)
                    keys.Add(step.EventName);
                else if (step.ActionType == FlowStepActionType.SetSwitchMultiple)
                    keys.AddRange(step.MultiActions.Select(a => a.EventName));

                foreach (string key in keys)
                {
                    checkedKeys++;
                    bool isPseudoKey = IFly737ActionExecutor.IsPseudoKey(key);
                    bool isWritableVar = vars.TryGetValue(key, out var def) && !def.RenderAsReadOnlyStatus;
                    Assert.True(isPseudoKey || isWritableVar,
                        $"{flow.Id}.{step.Id}: '{key}' is neither a declared pseudo-key nor a " +
                        "writable registered variable (either unregistered, or registered " +
                        "read-only) — this step would silently do nothing in the sim.");
                }
            }
        }
        Assert.True(checkedKeys > 0, "no SetSwitch/SetSwitchMultiple steps were found to check");
    }

    [Fact]
    public void PressurizationSteps_UseTargetValueProvider()
    {
        var steps = IFly737FlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT").Steps;
        var fltStep = steps.Single(s => s.Id == "PF_FLT_ALT");
        var landStep = steps.Single(s => s.Id == "PF_LAND_ALT");
        var captainStep = steps.Single(s => s.Id == "PF_PRESS");

        Assert.NotNull(fltStep.TargetValueProvider);
        Assert.NotNull(landStep.TargetValueProvider);
        Assert.Equal(FlowStepActionType.CaptainReminder, captainStep.ActionType);
        Assert.NotNull(captainStep.SkipCondition);

        // No SimBrief plan loaded: both providers quietly resolve to null (the flow step
        // skips with no announcement), and the Captain fallback is NOT skipped — the pilot
        // still hears something.
        var state = new IFly737StateEvaluator();
        Assert.Null(fltStep.TargetValueProvider!(state));
        Assert.Null(landStep.TargetValueProvider!(state));
        Assert.False(captainStep.SkipCondition!(state));

        // Plan loaded: providers resolve to the rounded/clamped planned values, and the
        // Captain fallback IS skipped away (HasPressurizationPlan).
        state.SetPlannedPressurizationAltitudes(35000, 500);
        Assert.Equal(35000, fltStep.TargetValueProvider!(state));
        Assert.Equal(500, landStep.TargetValueProvider!(state));
        Assert.True(captainStep.SkipCondition!(state));
    }
}
