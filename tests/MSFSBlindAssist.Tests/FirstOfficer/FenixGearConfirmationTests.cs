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
/// The Fenix A320 First Officer's landing-gear lines, confirmed the way a crew confirms
/// them — "gear up, lights out" and "three green" — never from the lever alone (owner
/// decisions 2026-09-22). Pure logic plus the app's own Build() definitions.
///
/// Each wheel's LDG GEAR indication is two legend L:vars (`_U`/`_L`) plus the lever's red
/// arrow. Which legend is the green DOWN-AND-LOCKED one was MEASURED live on 2026-09-25
/// (Fenix A320 CFM, parked at KDFW, powered, lever DOWN, gear down and locked): all three
/// `_L` lit, all three `_U` and `I_MIP_GEAR_RED` dark; with the annunciator light TEST
/// selected all three `_U` and `I_MIP_GEAR_RED` lit. So `_L` is the green, and `_U` plus the
/// arrow are the reds — a Fenix legend is measured, never inferred (the APU START `_U`/`_L`
/// reversal).
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

    // The Fenix profile has no Landing flow, so "Landing gear: DOWN" (LDC_GEAR) is only
    // ever ticked by its own state condition — FO_GEAR_DOWN, three green and no red. That
    // premise is pinned here because FirstOfficerForm's RelatedGroupIdsFor latches
    // `flow.Id + "_CL"` complete automatically whenever a checklist group of that id exists:
    // a future Fenix "LANDING" flow would latch LDC_GEAR the moment it completes, over gear
    // that may still be up. If one is ever added, it needs a read-only gear-down check that
    // completes LDC_GEAR FIRST (the same shape as AT_GEAR_UP_CHECK above).
    [Fact]
    public void No_Fenix_flow_latches_LANDING_CL()
    {
        var flows = FenixFlowDefinitions.Build();
        Assert.DoesNotContain(flows, f => f.Id == "LANDING");
        Assert.DoesNotContain(flows, f => f.RelatedChecklistGroupIds.Contains("LANDING_CL"));
    }

    // ---- DOWN: "three green" — every _L lit, every _U and the red arrow out ----

    private static Dictionary<string, double> ThreeGreen(double lever = 1.0)
    {
        var values = new Dictionary<string, double> { [FenixGearConfirmation.LeverField] = lever };
        foreach (var green in FenixGearConfirmation.GreenFields) values[green] = 1.0;
        foreach (var red in FenixGearConfirmation.RedFields) values[red] = 0.0;
        return values;
    }

    [Fact]
    public void The_greens_are_the_lower_legends_and_the_reds_the_upper_legends_plus_the_arrow()
    {
        // Pinned to the 2026-09-25 live measurement — never to the suffix or the real jet.
        Assert.Equal(new[] { "I_MIP_GEAR_1_L", "I_MIP_GEAR_2_L", "I_MIP_GEAR_3_L" },
            FenixGearConfirmation.GreenFields);
        Assert.Equal(new[] { "I_MIP_GEAR_1_U", "I_MIP_GEAR_2_U", "I_MIP_GEAR_3_U", "I_MIP_GEAR_RED" },
            FenixGearConfirmation.RedFields);
        // Together they are exactly the seven lights the UP rule reads.
        Assert.Equal(FenixGearConfirmation.LightFields.OrderBy(f => f),
            FenixGearConfirmation.GreenFields.Concat(FenixGearConfirmation.RedFields).OrderBy(f => f));
        Assert.Equal("FO_GEAR_DOWN", FenixGearConfirmation.DownField);
    }

    [Fact]
    public void Lever_down_with_three_green_and_no_red_reads_confirmed_down()
    {
        Assert.Equal(1.0, FenixGearConfirmation.DownValue(Reader(ThreeGreen())));
    }

    [Fact]
    public void The_measured_down_and_locked_state_reads_confirmed_down()
    {
        // The literal 2026-09-25 KDFW readings.
        var values = new Dictionary<string, double>
        {
            ["S_MIP_GEAR"] = 1,
            ["I_MIP_GEAR_1_L"] = 1, ["I_MIP_GEAR_2_L"] = 1, ["I_MIP_GEAR_3_L"] = 1,
            ["I_MIP_GEAR_1_U"] = 0, ["I_MIP_GEAR_2_U"] = 0, ["I_MIP_GEAR_3_U"] = 0,
            ["I_MIP_GEAR_RED"] = 0,
        };
        Assert.Equal(1.0, FenixGearConfirmation.DownValue(Reader(values)));
    }

    [Fact]
    public void Any_single_green_missing_means_not_down()
    {
        foreach (var green in FenixGearConfirmation.GreenFields)
        {
            var values = ThreeGreen();
            values[green] = 0.0;
            Assert.Equal(0.0, FenixGearConfirmation.DownValue(Reader(values)));
        }
    }

    [Fact]
    public void Any_upper_legend_lit_means_not_down()
    {
        foreach (var upper in new[] { "I_MIP_GEAR_1_U", "I_MIP_GEAR_2_U", "I_MIP_GEAR_3_U" })
        {
            var values = ThreeGreen();
            values[upper] = 1.0;
            Assert.Equal(0.0, FenixGearConfirmation.DownValue(Reader(values)));
        }
    }

    [Fact]
    public void The_red_arrow_lit_means_not_down()
    {
        var values = ThreeGreen();
        values["I_MIP_GEAR_RED"] = 1.0;
        Assert.Equal(0.0, FenixGearConfirmation.DownValue(Reader(values)));
    }

    [Fact]
    public void An_annunciator_light_test_never_reads_down()
    {
        // The light test lights every legend and the arrow (measured 2026-09-25).
        var values = ThreeGreen();
        foreach (var red in FenixGearConfirmation.RedFields) values[red] = 1.0;
        Assert.Equal(0.0, FenixGearConfirmation.DownValue(Reader(values)));
    }

    [Fact]
    public void Lever_up_reads_not_down_even_with_three_green()
    {
        Assert.Equal(0.0, FenixGearConfirmation.DownValue(Reader(ThreeGreen(lever: 0.0))));
    }

    [Fact]
    public void Any_unknown_reading_makes_down_NaN()
    {
        foreach (var field in FenixGearConfirmation.GreenFields
                     .Concat(FenixGearConfirmation.RedFields)
                     .Append(FenixGearConfirmation.LeverField))
        {
            var values = ThreeGreen();
            values.Remove(field);
            Assert.True(double.IsNaN(FenixGearConfirmation.DownValue(Reader(values))),
                $"{field} unknown must read NaN");
        }
    }

    [Fact]
    public void Evaluator_reports_gear_down_unknown_with_no_data()
    {
        Assert.True(double.IsNaN(new FenixStateEvaluator().GetValue(FenixGearConfirmation.DownField)));
    }

    [Fact]
    public void LandingChecklist_gear_line_reads_three_green_and_keeps_its_label()
    {
        var item = FenixChecklistDefinitions.Build()
            .Single(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_GEAR");

        Assert.Equal("Landing gear: DOWN", item.Label);
        Assert.Equal(FenixGearConfirmation.DownField, item.StateFieldName);
        Assert.NotNull(item.StateCondition);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.False(item.StateCondition!(double.NaN));
        // Read-only: ticking it never writes the gear.
        Assert.Null(item.CheckAction);
    }
}
