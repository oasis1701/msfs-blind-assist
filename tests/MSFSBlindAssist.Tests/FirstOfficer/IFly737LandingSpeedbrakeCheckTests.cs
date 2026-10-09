using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// iFly 737 MAX8: the First Officer arms the speed brake itself, as the PMDG 737's does (same
/// aircraft type), through the verified SPEEDBRAKE_ARM action — main's PR #261 gave the lever a
/// measured write (FLTCTRL_SPOILER, the same 0-224 scale as the read, ARM exactly 34). "Armed"
/// is the lever exactly at 34 AND the SPEED BRAKE ARMED light: the light alone is lit from 34
/// all the way to 224. A deployed speed brake is left alone with its reason.
/// </summary>
public class IFly737LandingSpeedbrakeCheckTests
{
    private static FlowDefinition<IFly737StateEvaluator> LandingFlow()
        => IFly737FlowDefinitions.Build().Single(f => f.Id == "LANDING");

    private static FlowStep<IFly737StateEvaluator> Arm()
        => LandingFlow().Steps.Single(s => s.Id == "LD_SPDBRK");

    private static IFly737StateEvaluator Ready(int lever, byte armedLight)
    {
        var data = new byte[IFlySdkOffsets.StructSize];
        BitConverter.GetBytes(lever).CopyTo(data, IFlySdkOffsets.Spoiler_Lever_Status);
        data[IFlySdkOffsets.SPEED_BRAKE_ARMED_Light_Status] = armedLight;
        var snap = new IFlySdkSnapshot(data);
        var eval = new IFly737StateEvaluator();
        eval.SnapshotSource = () => snap;
        eval.ReadySource = () => true;
        return eval;
    }

    [Fact]
    public void Landing_flow_matches_the_PMDG_737s_order()
    {
        Assert.Equal(new[] { "LD_START_CONT", "LD_SPDBRK", "LD_MISSED", "LD_GEAR_DOWN_CHECK" },
            LandingFlow().Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void The_first_officer_arms_through_the_verified_pseudo_key()
    {
        var step = Arm();
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal(IFly737ActionExecutor.KeySpeedbrakeArm, step.EventName);
        Assert.Equal(SpeedbrakeLeverState.ArmPseudoKey, step.EventName);
        Assert.Equal(new[] { "LDA_SPDBRK", "LDC_SPDBRK" }, step.LinkedChecklistItemIds.ToArray());
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
    }

    [Fact]
    public void An_armed_speed_brake_is_already_set_and_a_deployed_one_is_left_alone()
    {
        var step = Arm();
        Assert.True(step.SkipCondition!(Ready(34, 1)));
        Assert.False(step.SkipCondition!(Ready(0, 0)));
        Assert.False(step.SkipCondition!(Ready(100, 1)));   // lit, but deployed
        Assert.True(step.LeaveAloneWhen!(Ready(100, 1)));
        Assert.False(step.LeaveAloneWhen!(Ready(0, 0)));
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, step.LeaveAloneText);
    }

    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(33, 0, 0.0)]
    [InlineData(34, 1, 1.0)]     // DIM counts
    [InlineData(34, 2, 1.0)]     // BRIGHT
    [InlineData(34, 0, 0.0)]
    [InlineData(100, 1, 0.0)]    // the light is lit all the way up
    [InlineData(224, 2, 0.0)]
    public void Armed_is_the_lever_at_34_and_the_light(int lever, byte light, double expected) =>
        Assert.Equal(expected, Ready(lever, light).GetValue(SpeedbrakeLeverState.ArmedField));

    [Fact]
    public void Armed_is_unknown_until_the_SDK_is_ready()
    {
        var eval = Ready(34, 1);
        eval.ReadySource = () => false;
        Assert.True(double.IsNaN(eval.GetValue(SpeedbrakeLeverState.ArmedField)));
    }

    [Fact]
    public void Both_lines_read_the_armed_field_and_only_the_landing_group_line_arms()
    {
        var groups = IFly737ChecklistDefinitions.Build();
        var group = groups.Single(g => g.Id == "LANDING").Items.Single(i => i.Id == "LDA_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, group.StateFieldName);
        Assert.NotNull(group.CheckAction);
        Assert.NotNull(group.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, group.LeaveAloneText);

        var readback = groups.Single(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, readback.StateFieldName);
        Assert.Null(readback.CheckAction);
    }

    // ---- end to end through the real ChecklistManager ---------------------------------------

    private static (ChecklistManager<IFly737ActionExecutor, IFly737StateEvaluator> mgr,
        List<ChecklistGroup<IFly737ActionExecutor, IFly737StateEvaluator>> groups) Manager()
    {
        var groups = IFly737ChecklistDefinitions.Build();
        var mgr = new ChecklistManager<IFly737ActionExecutor, IFly737StateEvaluator>(
            new IFly737StateEvaluator(), new IFly737ActionExecutor(), groups);
        return (mgr, groups);
    }

    private static ChecklistItem<IFly737ActionExecutor, IFly737StateEvaluator> Item(
        List<ChecklistGroup<IFly737ActionExecutor, IFly737StateEvaluator>> groups,
        string groupId, string itemId)
        => groups.Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    // A failed or left-alone arm puts its linked ids in FlowManager's unfinished set (the Skip
    // branch — pinned by the source guard below); finishing the flow must then leave BOTH
    // speedbrake lines un-ticked and exempt from the latch, while every other line of the phase
    // completes.
    [Fact]
    public void A_failed_arm_leaves_both_speedbrake_lines_unticked_and_live_after_the_flow_finishes()
    {
        var (mgr, groups) = Manager();
        var unfinished = Arm().LinkedChecklistItemIds.ToArray();

        foreach (var groupId in LandingFlow().RelatedChecklistGroupIds)
            mgr.MarkGroupComplete(groupId, unfinished);

        foreach (var (g, id) in new[] { ("LANDING_CL", "LDC_SPDBRK"), ("LANDING", "LDA_SPDBRK") })
        {
            var item = Item(groups, g, id);
            Assert.False(item.IsChecked);
            Assert.True(item.ExemptFromCompletionLatch);
        }
        Assert.True(Item(groups, "LANDING_CL", "LDC_START").IsChecked);
        Assert.True(Item(groups, "LANDING", "LDA_MISSED").IsChecked);
    }

    // A verified (or already-set) arm marks every linked line complete — FlowManager's success
    // and already-set branches — so the latch then stands over an armed speed brake.
    [Fact]
    public void A_verified_arm_ticks_both_speedbrake_lines()
    {
        var (mgr, groups) = Manager();
        foreach (var id in Arm().LinkedChecklistItemIds)
            mgr.MarkComplete(id);

        Assert.True(Item(groups, "LANDING_CL", "LDC_SPDBRK").IsChecked);
        Assert.True(Item(groups, "LANDING", "LDA_SPDBRK").IsChecked);
    }

    // ---- the generic step plumbing ---------------------------------------------------------

    [Fact]
    public void LinkedChecklistItemIds_is_the_primary_id_then_the_extras_without_blanks_or_repeats()
    {
        var none = new FlowStep<IFly737StateEvaluator>();
        Assert.Empty(none.LinkedChecklistItemIds);

        var single = new FlowStep<IFly737StateEvaluator> { CompletesChecklistItemId = "A" };
        Assert.Equal(new[] { "A" }, single.LinkedChecklistItemIds.ToArray());

        var multi = new FlowStep<IFly737StateEvaluator>
        {
            CompletesChecklistItemId = "A",
            AlsoCompletesChecklistItemIds = new[] { "B", "", "A", "C" },
        };
        Assert.Equal(new[] { "A", "B", "C" }, multi.LinkedChecklistItemIds.ToArray());
    }

    // FlowManager itself is not unit-testable (its ScreenReaderAnnouncer drives a real screen
    // reader — see its own remarks), so its three checklist hand-offs are pinned on the source:
    // the already-set and success branches mark, and the Skip branch excludes, EVERY linked id —
    // not just CompletesChecklistItemId, or LDA_SPDBRK would be latched over an unarmed lever.
    [Fact]
    public void FlowManager_marks_and_excludes_every_linked_checklist_item()
    {
        string source = File.ReadAllText(FirstOfficerSourcePath("FlowManager.cs")).Replace("\r\n", "\n");
        Assert.Equal(2, CountOf(source,
            "foreach (var itemId in step.LinkedChecklistItemIds)\n                    _checklist.MarkComplete(itemId);"));
        Assert.Contains(
            "foreach (var itemId in step.LinkedChecklistItemIds)\n                            _unfinishedChecklistItemIds.Add(itemId);",
            source);
        Assert.DoesNotContain("_checklist.MarkComplete(step.CompletesChecklistItemId)", source);
        Assert.DoesNotContain("_unfinishedChecklistItemIds.Add(step.CompletesChecklistItemId)", source);
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    private static string FirstOfficerSourcePath(string fileName,
        [CallerFilePath] string thisTestFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "..", "MSFSBlindAssist", "FirstOfficer", fileName));
}
