using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

using Pmdg737Eval = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg737Exec = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor;
using Pmdg737Checklists = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 737 Preflight "Flight and landing altitudes: SET" line (<c>PF_PRESS</c>) with no SimBrief
/// plan, on the real Preflight steps, evaluator, checklists and a real FlowManager (no sim).
///
/// The two SimBrief steps <c>PF_FLT_ALT</c> / <c>PF_LAND_ALT</c> resolve their target from the plan
/// and, with none, skip silently (FO-20) while the Captain fallback speaks. They used to link NO line,
/// so nothing kept <c>PF_PRESS</c> out of the end-of-flow latch: a Preflight run with no plan ticked
/// and froze "Flight and landing altitudes: SET" over windows nobody had set (FO-7). Both steps now
/// name it, so either one skipping (no plan) or failing (a write that did not take) leaves the line
/// open, mirroring the windows, for the pilot to set and tick.
/// </summary>
public class Pmdg737PressurizationNoPlanTests
{
    private const string Line = "PF_PRESS";
    private static readonly string[] PressSteps = { "PF_FLT_ALT", "PF_LAND_ALT", "PF_PRESS" };

    [Fact]
    public void Both_SimBrief_steps_name_the_pressurization_line()
    {
        var steps = Pmdg737Flows.Build().Single(f => f.Id == "PREFLIGHT").Steps;

        foreach (var id in new[] { "PF_FLT_ALT", "PF_LAND_ALT" })
        {
            var step = steps.Single(s => s.Id == id);
            Assert.NotNull(step.TargetValueProvider);
            Assert.Equal(new[] { Line }, step.LinkedChecklistItemIds.ToArray());
        }

        // The Captain fallback stays, and still delivers nothing (a reminder never ticks a state line).
        var captain = steps.Single(s => s.Id == "PF_PRESS");
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Empty(captain.LinkedChecklistItemIds);

        // Exactly the state group's line: the Preflight read-back has no pressurization-altitude line
        // (PFC_PRESS is the mode selector, which no Preflight step writes).
        var line = Pmdg737Checklists.Build().Single(g => g.Id == "PREFLIGHT").Items.Single(i => i.Id == Line);
        Assert.Equal("Flight and landing altitudes: SET", line.Label);
    }

    [Fact]
    public void Without_a_plan_both_targets_are_null()
    {
        var steps = Pmdg737Flows.Build().Single(f => f.Id == "PREFLIGHT").Steps;
        var state = new Pmdg737Eval();

        Assert.Null(steps.Single(s => s.Id == "PF_FLT_ALT").TargetValueProvider!(state));
        Assert.Null(steps.Single(s => s.Id == "PF_LAND_ALT").TargetValueProvider!(state));

        state.SetPlannedPressurizationAltitudes(35000, 450);
        Assert.Equal(35000, steps.Single(s => s.Id == "PF_FLT_ALT").TargetValueProvider!(state));
        Assert.Equal(450, steps.Single(s => s.Id == "PF_LAND_ALT").TargetValueProvider!(state));
    }

    /// <summary>The bug: no plan, the flow finishes, the form latches. The line must stay unticked
    /// and exempt from the latch, and the pilot hears only the Captain reminder.</summary>
    [Fact]
    public async Task No_plan_leaves_the_line_unticked_and_out_of_the_latch()
    {
        var run = await RunAsync(new Pmdg737Eval());

        Assert.Equal(new[]
        {
            "Preflight flow started",
            "Captain action required: Set flight and landing altitudes on the pressurization panel.",
            "Preflight flow complete",
        }, run.Speech);
        Assert.Equal(new[] { "PF_FLT_ALT", "PF_LAND_ALT" }, run.Skipped);
        Assert.Equal(new[] { Line }, run.Unfinished);
        Assert.False(run.Line.IsChecked, "PF_PRESS was ticked with no SimBrief plan loaded");
        Assert.True(run.Line.ExemptFromCompletionLatch, "PF_PRESS is latched: it cannot follow the windows");
        Assert.True(run.PreflightLatched);
    }

    /// <summary>A plan, but the writes do not take (no sim here, so "Sim not connected"): skipped
    /// aloud, and the line stays open too.</summary>
    [Fact]
    public async Task A_failed_write_leaves_the_line_unticked_and_out_of_the_latch()
    {
        var state = new Pmdg737Eval();
        state.SetPlannedPressurizationAltitudes(35000, 450);

        var run = await RunAsync(state);

        Assert.Contains("Skipping: Flight altitude: set", run.Speech);
        Assert.Contains("Skipping: Landing altitude: set", run.Speech);
        Assert.Equal(new[] { Line }, run.Unfinished);
        Assert.False(run.Line.IsChecked);
        Assert.True(run.Line.ExemptFromCompletionLatch);
    }

    private sealed record Run(List<string> Speech, List<string> Skipped, string[] Unfinished,
        ChecklistItem<Pmdg737Exec, Pmdg737Eval> Line, bool PreflightLatched);

    // The real Preflight pressurization steps (in flow order) under the real flow's id, so its
    // CompletionGroupIds are the real PREFLIGHT + PREFLIGHT_CL; latched the way
    // FirstOfficerForm.OnFlowCompleted latches a finished run.
    private static async Task<Run> RunAsync(Pmdg737Eval state)
    {
        var source = Pmdg737Flows.Build().Single(f => f.Id == "PREFLIGHT");
        var flow = new FlowDefinition<Pmdg737Eval>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => PressSteps.Contains(s.Id)).ToList(),
        };
        Assert.Equal(PressSteps, flow.Steps.Select(s => s.Id).ToArray());

        var groups = Pmdg737Checklists.Build();
        var executor = new Pmdg737Exec();
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<Pmdg737Exec, Pmdg737Eval>(state, executor, groups);
        var flows = new FlowManager<Pmdg737Exec, Pmdg737Eval>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flows.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

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

        var preflight = groups.Single(g => g.Id == "PREFLIGHT");
        return new Run(speech.All.ToList(), skipped, unfinished,
            preflight.Items.Single(i => i.Id == Line), preflight.CompletionLatched);
    }
}
