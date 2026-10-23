using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The iFly 737 MAX8 Preflight "Flight and landing altitudes: SET" line (<c>PF_PRESS</c>) with no
/// SimBrief plan, on the real Preflight steps, evaluator, checklists and a real FlowManager (no sim).
///
/// The iFly sets both windows in ONE step, <c>PF_PRESS_ALTS</c>, through the executor's
/// <c>PRESS_ALTS</c> pseudo-key (FOB-17's sanctioned bypass). With no plan that pseudo-key is a quiet
/// no-op that still reports success, so the step used to read "Flight and landing altitudes: set"
/// over windows it never touched, right before the Captain fallback asked the pilot to set them,
/// and it linked no line, so the end-of-flow latch ticked and froze <c>PF_PRESS</c> anyway (FO-7).
/// The step now resolves a plan-gated target (FO-20: no plan is a SILENT skip, the Captain fallback
/// is the only speech) and names <c>PF_PRESS</c>, so a no-plan run, or a write that did not take,
/// leaves the line open, mirroring the windows, for the pilot to set and tick.
/// </summary>
public class IFly737PressurizationNoPlanTests
{
    private const string Line = "PF_PRESS";
    private static readonly string[] PressSteps = { "PF_PRESS_ALTS", "PF_PRESS" };

    [Fact]
    public void The_pressurization_step_is_plan_gated_and_names_the_line()
    {
        var steps = IFly737FlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT").Steps;
        var step = steps.Single(s => s.Id == "PF_PRESS_ALTS");

        // Still the one sanctioned pseudo-key step (FOB-17), never the NumSet keys.
        Assert.Equal(IFly737ActionExecutor.KeyPressAlts, step.EventName);
        Assert.Equal(new[] { Line }, step.LinkedChecklistItemIds.ToArray());

        // No plan: no target, so FlowManager skips it silently (FO-20). Any plan value: a target.
        var state = new IFly737StateEvaluator();
        Assert.NotNull(step.TargetValueProvider);
        Assert.Null(step.TargetValueProvider!(state));
        state.SetPlannedPressurizationAltitudes(35000, null);
        Assert.NotNull(step.TargetValueProvider!(state));
        state.SetPlannedPressurizationAltitudes(null, 450);
        Assert.NotNull(step.TargetValueProvider!(state));

        // The Captain fallback stays, and still delivers nothing.
        var captain = steps.Single(s => s.Id == "PF_PRESS");
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Empty(captain.LinkedChecklistItemIds);

        var line = IFly737ChecklistDefinitions.Build().Single(g => g.Id == "PREFLIGHT").Items.Single(i => i.Id == Line);
        Assert.Equal("Flight and landing altitudes: SET", line.Label);
    }

    /// <summary>The bug: no plan, the flow finishes, the form latches. The line must stay unticked
    /// and exempt from the latch, and the pilot hears only the Captain reminder.</summary>
    [Fact]
    public async Task No_plan_leaves_the_line_unticked_and_out_of_the_latch()
    {
        var run = await RunAsync(new IFly737StateEvaluator());

        Assert.Equal(new[]
        {
            "Preflight flow started",
            "Captain action required: Set flight and landing altitudes on the pressurization panel.",
            "Preflight flow complete",
        }, run.Speech);
        Assert.Equal(new[] { "PF_PRESS_ALTS" }, run.Skipped);
        Assert.Equal(new[] { Line }, run.Unfinished);
        Assert.False(run.Line.IsChecked, "PF_PRESS was ticked with no SimBrief plan loaded");
        Assert.True(run.Line.ExemptFromCompletionLatch, "PF_PRESS is latched: it cannot follow the windows");
        Assert.True(run.PreflightLatched);
    }

    /// <summary>A plan, but the write does not take (no sim here, so "Sim not connected"): skipped
    /// aloud, and the line stays open too.</summary>
    [Fact]
    public async Task A_failed_write_leaves_the_line_unticked_and_out_of_the_latch()
    {
        var state = new IFly737StateEvaluator();
        state.SetPlannedPressurizationAltitudes(35000, 450);

        var run = await RunAsync(state);

        Assert.Contains("Skipping: Flight and landing altitudes: set", run.Speech);
        Assert.Equal(new[] { Line }, run.Unfinished);
        Assert.False(run.Line.IsChecked);
        Assert.True(run.Line.ExemptFromCompletionLatch);
    }

    private sealed record Run(List<string> Speech, List<string> Skipped, string[] Unfinished,
        ChecklistItem<IFly737ActionExecutor, IFly737StateEvaluator> Line, bool PreflightLatched);

    // The real Preflight pressurization steps (in flow order) under the real flow's id, so its
    // CompletionGroupIds are the real PREFLIGHT + PREFLIGHT_CL; latched the way
    // FirstOfficerForm.OnFlowCompleted latches a finished run.
    private static async Task<Run> RunAsync(IFly737StateEvaluator state)
    {
        var source = IFly737FlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT");
        var flow = new FlowDefinition<IFly737StateEvaluator>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => PressSteps.Contains(s.Id)).ToList(),
        };
        Assert.Equal(PressSteps, flow.Steps.Select(s => s.Id).ToArray());

        var groups = IFly737ChecklistDefinitions.Build();
        var executor = new IFly737ActionExecutor();
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<IFly737ActionExecutor, IFly737StateEvaluator>(state, executor, groups);
        var flows = new FlowManager<IFly737ActionExecutor, IFly737StateEvaluator>(state, executor, checklist, speech);
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
