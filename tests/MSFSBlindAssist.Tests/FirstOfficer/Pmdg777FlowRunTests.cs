using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// BEHAVIOUR tests on the PMDG 777's real flow steps, checklists, evaluator and executor, on a
/// real FlowManager and ChecklistManager, with no sim — the harness of
/// Pmdg737PressurizationNoPlanTests. A flow is cut down to the steps under test, under the real
/// flow's id, so its completion groups are the real ones, and a finished run is latched the way
/// FirstOfficerForm.OnFlowCompleted latches it. With no sim every write fails ("Sim not
/// connected") and every CDA field reads unknown (NaN): the 777 evaluator reads them only from a
/// CDA snapshot, which a test cannot supply. The speed-brake lever comes from SimConnect's
/// variable cache instead (main's L:switch_498_a), so a test sets it.
/// </summary>
public class Pmdg777FlowRunTests
{
    internal sealed record Run(List<string> Speech, string[] Unfinished,
        List<ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator>> Groups,
        ChecklistManager<AircraftActionExecutor, AircraftStateEvaluator> Checklist)
    {
        public ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Line(string groupId, string itemId) =>
            Groups.Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

        public bool Latched(string groupId) => Groups.Single(g => g.Id == groupId).CompletionLatched;
    }

    /// <summary>An evaluator that reads as available, as in the sim before the first CDA snapshot:
    /// a data manager that never received one (every CDA field NaN), so FlowManager consults a
    /// step's SkipCondition and ChecklistManager auto-detects. The speed-brake lever reads
    /// <paramref name="lever"/> (null = never delivered).</summary>
    internal static AircraftStateEvaluator Available(Func<double?> lever)
    {
        var eval = new AircraftStateEvaluator();
        eval.SetDataManager(new PMDG777DataManager());
        eval.SetCachedValueSource(key => key == SpeedbrakeLeverState.Pmdg777.LeverKey ? lever() : null);
        return eval;
    }

    /// <summary>Runs <paramref name="stepIds"/> (in flow order) of the real flow
    /// <paramref name="flowId"/>, then latches its completion groups as the form does. A read-only
    /// check waits 1 s instead of its real timeout: the same timeout path, in a test that cannot
    /// move the lever. What is pinned on the real steps, and where: exactly 20 s on the four
    /// flap-lever checks (Pmdg777FlapLeverCheckTests); only the 1-30 s window on the After Takeoff
    /// flap check and the two gear checks (Pmdg777AfterTakeoffFlapsTests, Pmdg777LandingGearCheckTests).</summary>
    internal static async Task<Run> RunAsync(AircraftStateEvaluator state, string flowId, params string[] stepIds)
    {
        var source = PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId);
        var flow = new FlowDefinition<AircraftStateEvaluator>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        foreach (var step in flow.Steps.Where(s => s.ActionType == FlowStepActionType.WaitForCondition))
            step.TimeoutSeconds = 1;

        var groups = PMDG777ChecklistDefinitions.Build();
        var executor = new AircraftActionExecutor();
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<AircraftActionExecutor, AircraftStateEvaluator>(state, executor, groups);
        var flows = new FlowManager<AircraftActionExecutor, AircraftStateEvaluator>(state, executor, checklist, speech);

        flows.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flows.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
            await Task.Delay(20);
        }

        var unfinished = flows.UnfinishedChecklistItemIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flows.UnfinishedChecklistItemIds);
        return new Run(speech.All.ToList(), unfinished, groups, checklist);
    }

    // ---- Landing: the arming is the Landing section's, the read-back only reads the lever ------

    [Fact]
    public async Task An_armed_lever_is_already_set_and_the_arm_ticks_both_speedbrake_lines()
    {
        var run = await RunAsync(Available(() => Pmdg777SpeedbrakeLever.ArmedValue), "LANDING", "LD_SPEEDBRAKE_ARM");

        Assert.Contains("Already set: Speedbrake: ARM", run.Speech);
        Assert.Empty(run.Unfinished);
        foreach (var (group, id) in new[] { ("LANDING", "LDA_SPEEDBRAKE"), ("LANDING_CL", "LDG_SPEEDBRAKE") })
        {
            Assert.True(run.Line(group, id).IsChecked, id);
            Assert.False(run.Line(group, id).ExemptFromCompletionLatch, id);
            Assert.True(run.Latched(group), group);
        }
    }

    [Fact]
    public async Task A_failed_arm_leaves_both_lines_open_and_the_readback_ticks_from_the_lever()
    {
        double lever = Pmdg777SpeedbrakeLever.DownValue;
        var run = await RunAsync(Available(() => lever), "LANDING", "LD_SPEEDBRAKE_ARM");

        // No sim: the arm is not sent, so it is heard skipped and neither line is latched.
        Assert.Contains("Skipping: Speedbrake: ARM", run.Speech);
        Assert.Equal(new[] { "LDA_SPEEDBRAKE", "LDG_SPEEDBRAKE" }, run.Unfinished);
        Assert.False(run.Line("LANDING_CL", "LDG_SPEEDBRAKE").IsChecked);
        Assert.True(run.Line("LANDING_CL", "LDG_SPEEDBRAKE").ExemptFromCompletionLatch);
        Assert.False(run.Line("LANDING", "LDA_SPEEDBRAKE").IsChecked);

        // The pilot arms it by hand: the read-only line ticks from the lever alone.
        lever = Pmdg777SpeedbrakeLever.ArmedValue;
        run.Checklist.EvaluateAutoDetection();

        Assert.True(run.Line("LANDING_CL", "LDG_SPEEDBRAKE").IsChecked);
        Assert.Null(run.Line("LANDING_CL", "LDG_SPEEDBRAKE").CheckAction);
        Assert.True(run.Line("LANDING", "LDA_SPEEDBRAKE").IsChecked);
    }

    // ---- Before Takeoff: "Flaps: Set for takeoff" ticks only off the lever ---------------------

    // The lever is unread here (no CDA snapshot), so the check's condition is false, exactly as for
    // a lever at UP (that the condition rejects UP is pinned in Pmdg777FlapLeverCheckTests).
    [Fact]
    public async Task A_flap_check_that_cannot_confirm_the_lever_is_heard_skipped_and_keeps_the_line_open()
    {
        var run = await RunAsync(Available(() => null), "BEFORE_TAKEOFF", "BTKOF_FLAPS_CHECK");

        Assert.Equal(new[]
        {
            "Before Takeoff flow started",
            "Waiting for: Flap lever: takeoff",
            "Timed out waiting for: Flap lever: takeoff",
            "Skipping: Flap lever: takeoff",
            "Before Takeoff flow complete",
        }, run.Speech);
        Assert.Equal(new[] { "BTKOF_FLAPS" }, run.Unfinished);
        Assert.False(run.Line("BEFORE_TKOF_CL", "BTKOF_FLAPS").IsChecked);
        Assert.True(run.Line("BEFORE_TKOF_CL", "BTKOF_FLAPS").ExemptFromCompletionLatch);
        Assert.True(run.Latched("BEFORE_TKOF_CL"));
    }
}
