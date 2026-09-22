using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The iFly 737 MAX8 First Officer's gear lines, confirmed the way a crew confirms them —
/// never from the lever alone (owner decision 2026-09-22): "Landing gear: UP" is gear up,
/// lights out after takeoff; "Landing gear: DOWN" is three green on landing. Finishing a flow
/// can no longer latch either line complete over gear in the wrong position. Pure logic
/// (<see cref="IFly737GearConfirmation"/>, built on the shared <c>GearLightRules</c>) plus the
/// app's own Build() definitions — the PMDG 737 sibling is
/// <c>MSFSBlindAssist.Tests.Pmdg737GearConfirmationTests</c>.
/// </summary>
public class IFly737GearConfirmationTests
{
    // ---- dictionary readers ---------------------------------------------------------------
    // "Known" means every field the verdict reads has an entry; a field missing from the
    // dictionary reads as NaN (see Reader) — that's how the NaN tests below simulate a
    // reading that hasn't arrived yet.

    private static Dictionary<string, double> GearUpKnown()
    {
        var d = new Dictionary<string, double> { [IFly737GearConfirmation.LeverField] = 0.0 };
        foreach (var f in IFly737GearConfirmation.AllLightFields) d[f] = 0.0; // every light out
        return d;
    }

    private static Dictionary<string, double> GearDownKnown()
    {
        var d = new Dictionary<string, double> { [IFly737GearConfirmation.LeverField] = 1.0 };
        foreach (var f in IFly737GearConfirmation.GreenFields) d[f] = 2.0;          // three green, BRT
        foreach (var f in IFly737GearConfirmation.RedFields) d[f] = 0.0;           // no red
        foreach (var f in IFly737GearConfirmation.OverheadGreenFields) d[f] = 0.0; // known, not required
        return d;
    }

    private static Func<string, double> Reader(Dictionary<string, double> values)
        => f => values.TryGetValue(f, out var v) ? v : double.NaN;

    // ---- UP truth table — "gear up, lights out" --------------------------------------------

    [Fact]
    public void Up_lever_0_every_light_0_gives_1()
    {
        Assert.Equal(1.0, IFly737GearConfirmation.UpValue(Reader(GearUpKnown())));
    }

    [Fact]
    public void Up_lever_1_gives_0()
    {
        var d = GearUpKnown();
        d[IFly737GearConfirmation.LeverField] = 1.0;
        Assert.Equal(0.0, IFly737GearConfirmation.UpValue(Reader(d)));
    }

    [Fact]
    public void Up_a_single_light_dim_or_bright_gives_0_for_every_light()
    {
        foreach (var field in IFly737GearConfirmation.AllLightFields)
        {
            foreach (var lit in new[] { 1.0, 2.0 }) // 1 = DIM, 2 = BRT
            {
                var d = GearUpKnown();
                d[field] = lit;
                Assert.False(IFly737GearConfirmation.UpValue(Reader(d)) > 0.5,
                    $"{field}={lit} lit must read as not up");
                Assert.Equal(0.0, IFly737GearConfirmation.UpValue(Reader(d)));
            }
        }
    }

    // ---- DOWN truth table — "three green" ---------------------------------------------------

    [Fact]
    public void Down_lever_1_three_greens_2_no_red_gives_1()
    {
        Assert.Equal(1.0, IFly737GearConfirmation.DownValue(Reader(GearDownKnown())));
    }

    [Fact]
    public void Down_one_green_0_gives_0_for_every_green()
    {
        foreach (var field in IFly737GearConfirmation.GreenFields)
        {
            var d = GearDownKnown();
            d[field] = 0.0;
            Assert.Equal(0.0, IFly737GearConfirmation.DownValue(Reader(d)));
        }
    }

    [Fact]
    public void Down_a_red_at_1_gives_0_for_every_red()
    {
        foreach (var field in IFly737GearConfirmation.RedFields)
        {
            var d = GearDownKnown();
            d[field] = 1.0; // DIM still counts as "on" — a red must never read as down
            Assert.Equal(0.0, IFly737GearConfirmation.DownValue(Reader(d)));
        }
    }

    [Fact]
    public void Down_lever_0_gives_0()
    {
        var d = GearDownKnown();
        d[IFly737GearConfirmation.LeverField] = 0.0;
        Assert.Equal(0.0, IFly737GearConfirmation.DownValue(Reader(d)));
    }

    [Fact]
    public void Down_a_green_at_1_dim_still_counts_as_on_gives_1()
    {
        var d = GearDownKnown();
        foreach (var field in IFly737GearConfirmation.GreenFields) d[field] = 1.0; // DIM
        Assert.Equal(1.0, IFly737GearConfirmation.DownValue(Reader(d)));
    }

    // ---- NaN — an unknown reading is never coerced into a verdict --------------------------

    [Fact]
    public void Up_unknown_lever_gives_NaN()
    {
        var d = GearUpKnown();
        d.Remove(IFly737GearConfirmation.LeverField);
        Assert.True(double.IsNaN(IFly737GearConfirmation.UpValue(Reader(d))));
    }

    [Fact]
    public void Up_any_unknown_light_gives_NaN()
    {
        foreach (var field in IFly737GearConfirmation.AllLightFields)
        {
            var d = GearUpKnown();
            d.Remove(field);
            Assert.True(double.IsNaN(IFly737GearConfirmation.UpValue(Reader(d))), field);
        }
    }

    [Fact]
    public void Down_unknown_lever_gives_NaN()
    {
        var d = GearDownKnown();
        d.Remove(IFly737GearConfirmation.LeverField);
        Assert.True(double.IsNaN(IFly737GearConfirmation.DownValue(Reader(d))));
    }

    [Fact]
    public void Down_any_unknown_green_or_red_gives_NaN()
    {
        foreach (var field in IFly737GearConfirmation.GreenFields.Concat(IFly737GearConfirmation.RedFields))
        {
            var d = GearDownKnown();
            d.Remove(field);
            Assert.True(double.IsNaN(IFly737GearConfirmation.DownValue(Reader(d))), field);
        }
    }

    // ---- field names + light-group sizes match the SDK -------------------------------------

    [Fact]
    public void Field_names_and_light_groups_match_the_SDK()
    {
        Assert.Equal("FO_GEAR_UP", IFly737GearConfirmation.UpField);
        Assert.Equal("FO_GEAR_DOWN", IFly737GearConfirmation.DownField);
        Assert.Equal("Gear_Lever_Status", IFly737GearConfirmation.LeverField);
        Assert.Equal(
            new[] { "LEFT_GEAR_GreenLight_Status", "NOSE_GEAR_GreenLight_Status", "RIGHT_GEAR_GreenLight_Status" },
            IFly737GearConfirmation.GreenFields.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "LEFT_GEAR_RedLight_Status", "NOSE_GEAR_RedLight_Status", "RIGHT_GEAR_RedLight_Status" },
            IFly737GearConfirmation.RedFields.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                "SYS2_LEFT_GEAR_GreenLight_Status", "SYS2_NOSE_GEAR_GreenLight_Status",
                "SYS2_RIGHT_GEAR_GreenLight_Status"
            },
            IFly737GearConfirmation.OverheadGreenFields.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(9, IFly737GearConfirmation.AllLightFields.Count);
        Assert.Equal(9, IFly737GearConfirmation.AllLightFields.Distinct().Count());
        Assert.Equal(
            IFly737GearConfirmation.GreenFields.Concat(IFly737GearConfirmation.RedFields)
                .Concat(IFly737GearConfirmation.OverheadGreenFields),
            IFly737GearConfirmation.AllLightFields);
    }

    // ---- every field name resolves through the same table ReadRawField uses ---------------

    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];
    private static IFlySdkSnapshot Snap(byte[] b) => new(b);

    [Fact]
    public void Every_field_resolves_through_ReadRawField()
    {
        var snap = Snap(Buf());
        foreach (var field in IFly737GearConfirmation.AllLightFields.Append(IFly737GearConfirmation.LeverField))
            Assert.NotNull(IFly737MAXDefinition.ReadRawField(snap, field));
    }

    // ---- After Takeoff flow: the read-only gear-up check is the LAST step ------------------

    [Fact]
    public void AfterTakeoff_flow_ends_with_a_read_only_gear_up_check_that_completes_ATC_GEAR()
    {
        var step = IFly737FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF").Steps.Last();

        Assert.Equal("AT_GEAR_UP_CHECK", step.Id);
        Assert.Equal("Landing gear: UP", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(IFly737GearConfirmation.UpField, step.ConditionFieldName);
        Assert.Equal("ATC_GEAR", step.CompletesChecklistItemId);
        // A timeout must SKIP (so FlowManager keeps ATC_GEAR out of the completion latch),
        // never Stop the flow.
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new IFly737StateEvaluator())); // no data never reads "Already set"
        // Read-only: it never writes the gear lever.
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);
        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));
    }

    // ---- Landing flow: the read-only gear-down check is the LAST step ----------------------

    [Fact]
    public void Landing_flow_ends_with_a_read_only_gear_down_check_that_completes_LDC_GEAR()
    {
        var step = IFly737FlowDefinitions.Build().Single(f => f.Id == "LANDING").Steps.Last();

        Assert.Equal("LD_GEAR_DOWN_CHECK", step.Id);
        Assert.Equal("Landing gear: DOWN", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(IFly737GearConfirmation.DownField, step.ConditionFieldName);
        Assert.Equal("LDC_GEAR", step.CompletesChecklistItemId);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.InRange(step.TimeoutSeconds, 1, 30);
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new IFly737StateEvaluator()));
        Assert.Null(step.EventName);
        Assert.Empty(step.MultiActions);
        Assert.NotNull(step.Condition);
        Assert.True(step.Condition!(1));
        Assert.False(step.Condition!(0));
        Assert.False(step.Condition!(double.NaN));
    }

    // ---- checklist: the gear lines read the synthetic fields, not the lever ----------------

    [Fact]
    public void AfterTakeoffChecklist_gear_line_reads_the_UpField()
    {
        var item = IFly737ChecklistDefinitions.Build()
            .Single(g => g.Id == "AFTER_TAKEOFF_CL").Items.Single(i => i.Id == "ATC_GEAR");
        Assert.Equal("Landing gear: UP", item.Label);
        Assert.Equal(IFly737GearConfirmation.UpField, item.StateFieldName);
        Assert.NotNull(item.StateCondition);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.False(item.StateCondition!(double.NaN));
    }

    [Fact]
    public void LandingChecklist_gear_line_reads_the_DownField()
    {
        var item = IFly737ChecklistDefinitions.Build()
            .Single(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_GEAR");
        Assert.Equal("Landing gear: DOWN", item.Label);
        Assert.Equal(IFly737GearConfirmation.DownField, item.StateFieldName);
        Assert.NotNull(item.StateCondition);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.False(item.StateCondition!(double.NaN));
    }

    // ---- the lever-writing step is untouched ------------------------------------------------

    [Fact]
    public void AT_GEAR_OFF_still_exists_and_still_writes_the_lever()
    {
        var step = IFly737FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF")
            .Steps.Single(s => s.Id == "AT_GEAR_OFF");
        Assert.Equal("Gear lever: UP", step.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal("Gear_Lever_Status", step.EventName);
        Assert.Equal(IFly737ActionExecutor.GearUp, step.TargetValue);
    }
}
