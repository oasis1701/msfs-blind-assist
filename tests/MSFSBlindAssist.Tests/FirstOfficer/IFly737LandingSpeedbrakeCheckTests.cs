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
/// iFly 737 MAX8: finishing the Landing flow must not latch "Speedbrake: ARMED" complete
/// over a speedbrake that is NOT armed. The lever has no First Officer write (a Captain item,
/// deliberately read-only), so the flow can only REMIND the Captain — and the flow's
/// completion used to tick and latch both speedbrake lines (the Landing action group's
/// LDA_SPDBRK and the Landing Checklist's LDC_SPDBRK) whatever the lever was doing.
///
/// The fix follows the gear-check pattern: a READ-ONLY wait on the SPEED BRAKE ARMED light,
/// Skip on timeout, which completes both lines when the light comes on and, when it does not,
/// is announced as skipped so FlowManager keeps both lines out of MarkGroupComplete's latch and
/// they keep mirroring the light. It sits BEFORE the gear-down check, which stays LAST.
/// Pure logic over the app's own Build() data plus the real ChecklistManager; no SDK.
/// </summary>
public class IFly737LandingSpeedbrakeCheckTests
{
    private const string ArmedLight = "SPEED_BRAKE_ARMED_Light_Status";

    private static FlowDefinition<IFly737StateEvaluator> LandingFlow()
        => IFly737FlowDefinitions.Build().Single(f => f.Id == "LANDING");

    private static FlowStep<IFly737StateEvaluator> Check()
        => LandingFlow().Steps.Single(s => s.Id == "LD_SPDBRK_CHECK");

    private static IFly737StateEvaluator ReadyWithArmedLight(byte lightValue)
    {
        var data = new byte[IFlySdkOffsets.StructSize];
        data[IFlySdkOffsets.SPEED_BRAKE_ARMED_Light_Status] = lightValue;
        var snap = new IFlySdkSnapshot(data);
        var eval = new IFly737StateEvaluator();
        eval.SnapshotSource = () => snap;
        eval.ReadySource = () => true;
        return eval;
    }

    // ---- the flow's shape ------------------------------------------------------------------

    [Fact]
    public void Landing_flow_checks_the_speedbrake_before_the_gear_and_the_gear_check_stays_last()
    {
        Assert.Equal(
            new[] { "LD_START_CONT", "LD_SPDBRK", "LD_MISSED", "LD_SPDBRK_CHECK", "LD_GEAR_DOWN_CHECK" },
            LandingFlow().Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void The_Captain_is_still_asked_to_arm_the_speedbrake_before_the_check_waits_for_it()
    {
        var steps = LandingFlow().Steps;
        var reminder = steps.Single(s => s.Id == "LD_SPDBRK");
        Assert.Equal(FlowStepActionType.CaptainReminder, reminder.ActionType);
        int check = steps.FindIndex(s => s.Id == "LD_SPDBRK_CHECK");
        Assert.InRange(steps.IndexOf(reminder), 0, check - 1);
    }

    // ---- the check is read-only and linked to both speedbrake lines ------------------------

    [Fact]
    public void Speedbrake_check_is_a_read_only_wait_on_the_armed_light()
    {
        var step = Check();
        Assert.Equal("Speedbrake: ARMED", step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(ArmedLight, step.ConditionFieldName);
        // Never a write — the lever is a Captain item on this aircraft.
        Assert.Null(step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Empty(step.MultiActions);
        Assert.Null(step.VerifyFieldName);
    }

    [Fact]
    public void Speedbrake_check_reads_DIM_or_BRIGHT_as_armed_and_off_or_unknown_as_not()
    {
        var c = Check().Condition!;
        Assert.True(c(1));   // light ON (DIM)
        Assert.True(c(2));   // light ON (BRIGHT)
        Assert.False(c(0));  // light OFF
        Assert.False(c(double.NaN));
    }

    [Fact]
    public void Speedbrake_check_is_Skip_with_a_short_bounded_timeout()
    {
        var step = Check();
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        // Short — the Captain was just asked, and the gear check is waiting behind it.
        Assert.InRange(step.TimeoutSeconds, 5, 20);
    }

    [Fact]
    public void Speedbrake_check_completes_the_Landing_Checklist_line_and_the_action_group_line()
    {
        var step = Check();
        Assert.Equal("LDC_SPDBRK", step.CompletesChecklistItemId);
        Assert.Equal(new[] { "LDC_SPDBRK", "LDA_SPDBRK" }, step.LinkedChecklistItemIds.ToArray());

        var itemIds = IFly737ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Id).ToHashSet();
        Assert.All(step.LinkedChecklistItemIds, id => Assert.Contains(id, itemIds));
    }

    [Fact]
    public void Speedbrake_check_skips_as_already_set_only_on_a_lit_armed_light()
    {
        var skip = Check().SkipCondition!;
        Assert.True(skip(ReadyWithArmedLight(1)));
        Assert.True(skip(ReadyWithArmedLight(2)));
        Assert.False(skip(ReadyWithArmedLight(0)));
        Assert.False(skip(new IFly737StateEvaluator())); // no data never reads "Already set"
    }

    // ---- both speedbrake lines mirror the light, and neither can write ---------------------

    [Fact]
    public void Landing_action_group_speedbrake_line_mirrors_the_armed_light_with_no_write()
    {
        var item = IFly737ChecklistDefinitions.Build()
            .Single(g => g.Id == "LANDING").Items.Single(i => i.Id == "LDA_SPDBRK");
        Assert.Equal("Speedbrake: ARMED", item.Label);
        Assert.Equal(ChecklistItemType.AutoDetectable, item.Type);
        Assert.Equal(ArmedLight, item.StateFieldName);
        Assert.Equal(RevertBehavior.RevertToState, item.RevertBehavior);
        Assert.Null(item.CheckAction);
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

    // A timed-out check puts its linked ids in FlowManager's unfinished set (the Skip branch —
    // pinned by the source guard below); finishing the flow must then leave BOTH speedbrake
    // lines un-ticked and exempt from the latch, while every other line of the phase completes.
    [Fact]
    public void Timed_out_check_leaves_both_speedbrake_lines_unticked_and_live_after_the_flow_finishes()
    {
        var (mgr, groups) = Manager();
        var unfinished = Check().LinkedChecklistItemIds.ToArray();

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

    // A successful (or already-set) check marks every linked line complete — FlowManager's
    // success and already-set branches — so the latch then stands over an armed speedbrake.
    [Fact]
    public void Confirmed_check_ticks_both_speedbrake_lines()
    {
        var (mgr, groups) = Manager();
        foreach (var id in Check().LinkedChecklistItemIds)
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
