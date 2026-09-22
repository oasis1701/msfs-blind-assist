using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Fenix A320 First Officer's "Landing gear: UP" line, confirmed the way a crew
/// confirms it — gear up, lights out — never from the lever alone (owner decision
/// 2026-09-22). Finishing the After Takeoff flow can no longer latch the line complete
/// over gear that is still down. Pure logic plus the app's own Build() definitions.
///
/// UP only: each wheel's LDG GEAR indication is two legend L:vars (`_U`/`_L`, "Upper"/"Lower")
/// plus the lever's red arrow, and WHICH of each wheel's two legends is the green DOWN-AND-
/// LOCKED one has not been measured — a Fenix legend must be measured, never inferred (the
/// APU START `_U`/`_L` reversal) — so "three green, no red" is not composed from them yet.
/// "Lights out" needs no colour: every legend and the arrow go dark when the gear is up and
/// locked. And the Fenix profile has no Landing flow, so its checklist's "Landing gear: DOWN"
/// line is never latched by a flow completing.
/// </summary>
public class FenixGearConfirmationTests
{
    // ---- a dictionary reader: unknown names read NaN, exactly like the live cache ----
    private static Func<string, double> Reader(Dictionary<string, double> values)
        => f => values.TryGetValue(f, out double v) ? v : double.NaN;

    private static Dictionary<string, double> AllUp(double lever = 0.0)
    {
        var values = new Dictionary<string, double> { [FenixGearConfirmation.LeverField] = lever };
        foreach (var light in FenixGearConfirmation.LightFields)
            values[light] = 0.0;
        return values;
    }

    [Fact]
    public void Lever_up_with_every_light_out_reads_confirmed_up()
    {
        Assert.Equal(1.0, FenixGearConfirmation.UpValue(Reader(AllUp(lever: 0.0))));
    }

    [Fact]
    public void Lever_down_reads_not_up()
    {
        Assert.Equal(0.0, FenixGearConfirmation.UpValue(Reader(AllUp(lever: 1.0))));
    }

    [Fact]
    public void Any_single_gear_light_on_means_not_up()
    {
        foreach (var light in FenixGearConfirmation.LightFields)
        {
            var values = AllUp();
            values[light] = 1.0;
            Assert.False(FenixGearConfirmation.UpValue(Reader(values)) > 0.5, $"{light} lit must read as not up");
        }
    }

    [Fact]
    public void An_unknown_lever_reads_NaN()
    {
        var values = AllUp();
        values.Remove(FenixGearConfirmation.LeverField);
        Assert.True(double.IsNaN(FenixGearConfirmation.UpValue(Reader(values))));
    }

    [Fact]
    public void An_unknown_light_reads_NaN()
    {
        foreach (var light in FenixGearConfirmation.LightFields)
        {
            var values = AllUp();
            values.Remove(light);
            Assert.True(double.IsNaN(FenixGearConfirmation.UpValue(Reader(values))),
                $"{light} unknown must read NaN");
        }
    }

    [Fact]
    public void Reads_all_seven_gear_lights_and_each_one_is_registered_continuous()
    {
        Assert.Equal(7, FenixGearConfirmation.LightFields.Distinct().Count());

        var vars = new FenixA320Definition().GetVariables();
        foreach (var field in FenixGearConfirmation.LightFields)
        {
            Assert.True(vars.TryGetValue(field, out var def), $"{field} must be registered");
            Assert.Equal(UpdateFrequency.Continuous, def!.UpdateFrequency);
        }
    }

    [Fact]
    public void The_lever_field_is_registered_and_polled_by_the_evaluator()
    {
        var vars = new FenixA320Definition().GetVariables();
        Assert.True(vars.ContainsKey(FenixGearConfirmation.LeverField));
        Assert.Contains(FenixGearConfirmation.LeverField, new FenixStateEvaluator().OnRequestPollFields);
    }

    [Fact]
    public void AfterTakeoff_flow_ends_with_a_read_only_gear_up_check_that_completes_ATC_GEAR()
    {
        var step = FenixFlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF").Steps.Last();

        Assert.Equal("AT_GEAR_UP_CHECK", step.Id);
        Assert.Equal("Landing gear: UP", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(FenixGearConfirmation.UpField, step.ConditionFieldName);
        Assert.Equal("ATC_GEAR", step.CompletesChecklistItemId);
        // A timeout must SKIP (so FlowManager keeps ATC_GEAR out of the completion latch),
        // never Stop the flow.
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new FenixStateEvaluator())); // no data never reads "Already set"
        // Read-only: it never writes the gear lever.
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);
        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));
    }

    [Fact]
    public void AfterTakeoffChecklist_gear_line_reads_lights_out_and_keeps_its_label()
    {
        var item = FenixChecklistDefinitions.Build()
            .Single(g => g.Id == "AFTER_TAKEOFF_CL").Items.Single(i => i.Id == "ATC_GEAR");

        Assert.Equal("Landing gear: UP", item.Label);
        Assert.Equal(FenixGearConfirmation.UpField, item.StateFieldName);
        Assert.NotNull(item.StateCondition);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.False(item.StateCondition!(double.NaN));
    }

    // The Fenix profile has no Landing flow at all — that is WHY "Landing gear: DOWN"
    // (LDC_GEAR) stays a plain lever mirror forever (see the class doc above). That safety
    // property rests on a premise nothing else pins: FirstOfficerForm's RelatedGroupIdsFor
    // latches `flow.Id + "_CL"` complete automatically whenever a checklist group of that id
    // exists, even when the flow never lists it in RelatedChecklistGroupIds. A future Fenix
    // "LANDING" flow would silently latch "LANDING_CL" — including LDC_GEAR — the moment it
    // completes, reproducing for DOWN exactly the bug this whole class exists to prevent for
    // UP. If a Fenix Landing flow is ever added: LDC_GEAR needs its own read-only gear-down
    // check FIRST (the same shape as AT_GEAR_UP_CHECK above), and which of each wheel's
    // _U/_L legend is the green one must be MEASURED before a DOWN rule can be composed —
    // never inferred (see the class doc's APU START warning).
    [Fact]
    public void No_Fenix_flow_latches_LANDING_CL()
    {
        var flows = FenixFlowDefinitions.Build();
        Assert.DoesNotContain(flows, f => f.Id == "LANDING");
        Assert.DoesNotContain(flows, f => f.RelatedChecklistGroupIds.Contains("LANDING_CL"));
    }
}
