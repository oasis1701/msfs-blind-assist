using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-6 on the read-back lines of the three A320-family First Officers (Fenix, FBW A32NX, Headwind
/// A330). A finished flow latches its action group AND its own <c>*_CL</c> read-back
/// (<see cref="FlowDefinition{TState}.CompletionGroupIds"/>), so a step that delivers a read-back line
/// has to name it, or a step that failed ("Skipping: Beacon: ON") still latches the line ticked.
/// Three read-back lines have a step behind them:
///   Before Start   BS_BEACON        -> BSC_BEACON  "Beacon: ON" (the write sets it)
///   After Start    AS_RUDDERTRIM    -> ASC_RUDDER  "Rudder trim: NEUTRAL" (the write sets it)
///   After Landing  AL_WXR_PWS_CHECK -> ALC_WXR     "Radar and predictive windshear: OFF"
/// The radar line is true only when BOTH the radar (<c>AL_WXR_OFF</c>) and the predictive windshear
/// (<c>AL_PWS_OFF</c>) are off, so neither write completes it. Linked to both writes, the radar
/// write's success ticked the line, and as the only line of <c>AFTER_LANDING_CL</c> that latched the
/// group at once, with the windshear still on: a windshear write that then failed, or a flow cancelled
/// during the APU wait, left the line latched complete. A line that only a later condition makes true
/// is completed by a read-only wait (FO-6): <c>AL_WXR_PWS_CHECK</c>, right after both writes, reads
/// <c>FO_WXR_PWS_OFF</c> for up to 5 s, is skipped on timeout and never writes (the FO-13 shape).
/// The Fenix's ASC_RUDDER is a Reminder (no measured trim var); it is linked all the same, so a reset
/// that could not be sent leaves it unticked for the pilot.
///
/// Considered and NOT linked: AS_ECAMSTS (the ECAM page it shows is not the pilot's "ECAM status:
/// CHECKED"), and the Approach flow, which has no step that sets a line of APPROACH_CL (the seat belts
/// the Descent flow sets are not in a group the Approach flow latches).
/// </summary>
public class A320FamilyReadbackLinkTests
{
    // Flow, the write steps of that flow under test, and every line those steps complete.
    private static readonly (string Flow, string[] Steps, string[] Lines)[] Rows =
    {
        ("BEFORE_START", new[] { "BS_BEACON" }, new[] { "BS_BEACON", "BSC_BEACON" }),
        ("AFTER_START", new[] { "AS_RUDDERTRIM" }, new[] { "AS_RUDDERTRIM", "ASC_RUDDER" }),
        ("AFTER_LANDING", new[] { "AL_WXR_OFF", "AL_PWS_OFF" }, new[] { "AL_WXR_OFF", "AL_PWS_OFF" }),
    };

    private static readonly string[] Aircraft = { "Fenix", "A32NX", "A330" };

    private const string RadarCheck = "AL_WXR_PWS_CHECK";
    private const string RadarLine = "ALC_WXR";
    private const string RadarLabel = "Radar and predictive windshear: OFF";

    /// <summary>Aircraft, flow, step, the action-group line it completes (null: none) and the
    /// read-back line it completes (null: none).</summary>
    public static IEnumerable<object?[]> StepLinks()
    {
        foreach (var aircraft in Aircraft)
        {
            yield return new object?[] { aircraft, "BEFORE_START", "BS_BEACON", "BS_BEACON", "BSC_BEACON" };
            yield return new object?[] { aircraft, "AFTER_START", "AS_RUDDERTRIM", "AS_RUDDERTRIM", "ASC_RUDDER" };
            // One radar switch off does not make "Radar and predictive windshear: OFF" true: each
            // radar write names its own action line only ...
            yield return new object?[] { aircraft, "AFTER_LANDING", "AL_WXR_OFF", "AL_WXR_OFF", null };
            yield return new object?[] { aircraft, "AFTER_LANDING", "AL_PWS_OFF", "AL_PWS_OFF", null };
            // ... and the read-only check after both names the read-back line only.
            yield return new object?[] { aircraft, "AFTER_LANDING", RadarCheck, null, RadarLine };
        }
    }

    public static IEnumerable<object[]> FlowRuns()
    {
        foreach (var aircraft in Aircraft)
            foreach (var (flow, steps, lines) in Rows)
                yield return new object[] { aircraft, flow, steps, lines };
    }

    public static IEnumerable<object[]> RadarRuns()
    {
        foreach (var aircraft in Aircraft)
            foreach (var windshearOff in new[] { false, true })
                yield return new object[] { aircraft, windshearOff };
    }

    public static IEnumerable<object[]> AircraftOnly() => Aircraft.Select(a => new object[] { a });

    /// <summary>Every step names exactly its lines, and each exists: the action line in the group
    /// named after the flow, the read-back line in that group's <c>_CL</c> read-back.</summary>
    [Theory]
    [MemberData(nameof(StepLinks))]
    public void A_step_completes_exactly_its_action_line_and_its_readback_line(
        string aircraft, string flowId, string stepId, string? actionLine, string? readbackLine)
    {
        var (links, groups) = Profile(aircraft);

        Assert.Equal(new[] { actionLine, readbackLine }.OfType<string>(), links[(flowId, stepId)]);
        if (actionLine != null) Assert.Contains(actionLine, groups[flowId]);
        if (readbackLine != null) Assert.Contains(readbackLine, groups[flowId + "_CL"]);
    }

    /// <summary>The radar read-back line has ONE step behind it in all the aircraft's flows: a
    /// read-only check placed right after the two radar writes, reading the line's own field, short,
    /// skipped on timeout, and sending nothing.</summary>
    [Theory]
    [MemberData(nameof(AircraftOnly))]
    public void The_radar_readback_is_completed_by_one_read_only_check_after_both_radar_writes(string aircraft)
    {
        switch (aircraft)
        {
            case "Fenix": AssertRadarCheck(FenixFlowDefinitions.Build(), FenixChecklistDefinitions.Build()); break;
            case "A32NX": AssertRadarCheck(FbwA320FlowDefinitions.Build(), FbwA320ChecklistDefinitions.Build()); break;
            case "A330": AssertRadarCheck(HwA330FlowDefinitions.Build(), HwA330ChecklistDefinitions.Build()); break;
            default: throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null);
        }
    }

    private static void AssertRadarCheck<TExec, TState>(
        List<FlowDefinition<TState>> flows, List<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var steps = flows.Single(f => f.Id == "AFTER_LANDING").Steps;
        var ids = steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf("AL_WXR_OFF") + 1, ids.IndexOf("AL_PWS_OFF"));
        Assert.Equal(ids.IndexOf("AL_PWS_OFF") + 1, ids.IndexOf(RadarCheck));

        var check = steps[ids.IndexOf(RadarCheck)];
        var line = groups.Single(g => g.Id == "AFTER_LANDING_CL").Items.Single(i => i.Id == RadarLine);
        Assert.Equal(RadarLabel, check.Label);
        Assert.Equal(line.Label, check.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, check.ActionType);
        Assert.Equal("FO_WXR_PWS_OFF", check.ConditionFieldName);
        Assert.Equal(line.StateFieldName, check.ConditionFieldName);
        Assert.Equal(5, check.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, check.FailurePolicy);
        Assert.True(check.Condition!(1.0));
        Assert.False(check.Condition!(0.0));
        Assert.False(check.Condition!(double.NaN));   // unknown is not off
        Assert.NotNull(check.SkipCondition);
        // Read-only: nothing to send.
        Assert.Null(check.EventName);
        Assert.Null(check.TargetValue);
        Assert.Null(check.TargetValueProvider);
        Assert.Empty(check.MultiActions);
        Assert.Equal(new[] { RadarLine }, check.LinkedChecklistItemIds);

        // No other step of any flow completes the line.
        Assert.Equal(new[] { RadarCheck },
            flows.SelectMany(f => f.Steps).Where(s => s.LinkedChecklistItemIds.Contains(RadarLine)).Select(s => s.Id));
    }

    /// <summary>The bug itself, on the real steps, checklists and a real FlowManager (no sim, so
    /// every write fails: "Sim not connected"). A step that is skipped aloud must leave EVERY line it
    /// names unticked after the end-of-flow latch, applied the way FirstOfficerForm.OnFlowCompleted
    /// applies it, over the flow's whole CompletionGroupIds, and free to follow the aircraft. The radar
    /// read-back line is not a write's: the radar runs below cover it.</summary>
    [Theory]
    [MemberData(nameof(FlowRuns))]
    public Task A_failed_write_leaves_its_lines_unticked_and_unlatched(
        string aircraft, string flowId, string[] stepIds, string[] lineIds) => aircraft switch
    {
        "Fenix" => RunFailedWrites(new FenixStateEvaluator(), new FenixActionExecutor(),
            FenixChecklistDefinitions.Build(), FenixFlowDefinitions.Build(), flowId, stepIds, lineIds),
        "A32NX" => RunFailedWrites(new FbwA320StateEvaluator(), new FbwA320ActionExecutor(),
            FbwA320ChecklistDefinitions.Build(), FbwA320FlowDefinitions.Build(), flowId, stepIds, lineIds),
        "A330" => RunFailedWrites(new HwA330StateEvaluator(), new HwA330ActionExecutor(),
            HwA330ChecklistDefinitions.Build(), HwA330FlowDefinitions.Build(), flowId, stepIds, lineIds),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static async Task RunFailedWrites<TExec, TState>(TState state, TExec executor,
        List<ChecklistGroup<TExec, TState>> groups, List<FlowDefinition<TState>> flows,
        string flowId, string[] stepIds, string[] lineIds)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var flow = Subset(flows, flowId, stepIds);
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        Assert.All(flow.Steps, s => Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType));

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        // Every write was skipped aloud, and its lines are all in the run's unfinished set.
        Assert.Equal(stepIds, skipped.ToArray());
        Assert.Equal(lineIds.Order().ToArray(), flowManager.UnfinishedChecklistItemIds.Order().ToArray());

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var lineId in lineIds)
        {
            Assert.False(items[lineId].IsChecked, $"{lineId} was ticked over a write that failed");
            Assert.True(items[lineId].ExemptFromCompletionLatch,
                $"{lineId} is latched: it cannot follow the aircraft once the pilot does it");
        }
    }

    /// <summary>The radar block of After Landing (both radar writes, then the check) on the real
    /// steps, evaluator and checklists and a real FlowManager. The evaluator reads a seeded cache
    /// that counts as connected, with the radar OFF and the predictive windshear OFF or on AUTO; the
    /// executor has no sim, so every write it is asked for fails. The radar step therefore succeeds
    /// the way it does in the sim when the radar already reads off, "Already set": FlowManager's
    /// success path, which marks every line the step names exactly as a sent write does.
    /// (a) With the radar step done and the check not yet run, the read-back line is neither ticked
    /// nor latched, so a cancelled flow (the APU wait that follows can run 180 s) leaves it open.
    /// (b) Windshear on AUTO, its write failing: the check times out, is skipped aloud, and the line
    /// stays unticked and free to follow the aircraft after the end-of-flow latch.
    /// (c) Both off: the check passes, and it is what ticks and latches the line.</summary>
    [Theory]
    [MemberData(nameof(RadarRuns))]
    public Task The_radar_readback_ticks_only_once_radar_and_windshear_both_read_off(
        string aircraft, bool windshearOff) => aircraft switch
    {
        "Fenix" => RunRadarBlock(new FenixStateEvaluator(), new FenixActionExecutor(),
            FenixChecklistDefinitions.Build(), FenixFlowDefinitions.Build(),
            "S_WR_SYS", "S_WR_PRED_WS", windshearOff),
        "A32NX" => RunRadarBlock(new FbwA320StateEvaluator(), new FbwA320ActionExecutor(),
            FbwA320ChecklistDefinitions.Build(), FbwA320FlowDefinitions.Build(),
            "XMLVAR_A320_WeatherRadar_Sys", "A32NX_SWITCH_RADAR_PWS_POSITION", windshearOff),
        "A330" => RunRadarBlock(new HwA330StateEvaluator(), new HwA330ActionExecutor(),
            HwA330ChecklistDefinitions.Build(), HwA330FlowDefinitions.Build(),
            "XMLVAR_A320_WeatherRadar_Sys", "A32NX_SWITCH_RADAR_PWS_POSITION", windshearOff),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static async Task RunRadarBlock<TExec, TState>(TState state, TExec executor,
        List<ChecklistGroup<TExec, TState>> groups, List<FlowDefinition<TState>> flows,
        string radarVar, string windshearVar, bool windshearOff)
        where TExec : IFoActionExecutor
        where TState : LVarStateEvaluator
    {
        // The same encoding on all three: radar 1 = OFF; predictive windshear 0 = OFF, 1 = AUTO.
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith((radarVar, 1), (windshearVar, windshearOff ? 0 : 1)));
        Assert.False(executor.IsAvailable);

        string[] stepIds = { "AL_WXR_OFF", "AL_PWS_OFF", RadarCheck };
        var flow = Subset(flows, "AFTER_LANDING", stepIds);
        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        var readback = groups.Single(g => g.Id == "AFTER_LANDING_CL");
        var line = items[RadarLine];

        // What the checklist held as each step began, before that step marked anything: the first of
        // StepStarted / StepCompleted / StepSkipped each step raises comes before its own marks.
        var before = new Dictionary<string, (bool RadarStepDone, bool LineTicked, bool Latched)>();
        void Snap(FlowStep<TState> step)
        {
            lock (before)
                before.TryAdd(step.Id, (items["AL_WXR_OFF"].IsChecked, line.IsChecked, readback.CompletionLatched));
        }

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepStarted += (_, step, _) => Snap(step);
        flowManager.StepCompleted += (_, step, _) => Snap(step);
        flowManager.StepSkipped += (_, step, _) => { Snap(step); lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        // (a) The radar step is done, and the read-back line is still open.
        var afterRadar = before["AL_PWS_OFF"];
        Assert.True(afterRadar.RadarStepDone, "the radar step did not succeed");
        Assert.False(afterRadar.LineTicked, "the radar write alone ticked the radar read-back line");
        Assert.False(afterRadar.Latched, "the radar write alone latched the After Landing Checklist");

        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());
        var beforeCheck = before[RadarCheck];
        Assert.False(beforeCheck.LineTicked, "the radar read-back line was ticked before its check ran");
        Assert.False(beforeCheck.Latched, "the After Landing Checklist was latched before the radar check ran");

        if (windshearOff)
        {
            // (c) Both off: nothing skipped; the check ticked the line, and the group latched on it.
            Assert.Empty(skipped);
            Assert.True(line.IsChecked);
            Assert.True(readback.CompletionLatched);
        }
        else
        {
            // (b) The windshear write failed and the check timed out: both skipped aloud, and the
            // line was never ticked, nor the group latched, by the run itself (a cancel stops here).
            Assert.Equal(new[] { "AL_PWS_OFF", RadarCheck }, skipped.ToArray());
            Assert.Contains($"Timed out waiting for: {RadarLabel}", speech.All);
            Assert.Contains($"Skipping: {RadarLabel}", speech.All);
            Assert.False(line.IsChecked);
            Assert.False(readback.CompletionLatched);
            Assert.Equal(new[] { "AL_PWS_OFF", RadarLine }, flowManager.UnfinishedChecklistItemIds.Order().ToArray());
        }

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        // The radar write's own line is done either way.
        Assert.True(items["AL_WXR_OFF"].IsChecked);
        Assert.False(items["AL_WXR_OFF"].ExemptFromCompletionLatch);
        if (windshearOff)
        {
            Assert.True(line.IsChecked);
            Assert.False(line.ExemptFromCompletionLatch);
        }
        else
        {
            Assert.False(line.IsChecked, "the radar read-back line was ticked with the windshear on AUTO");
            Assert.True(line.ExemptFromCompletionLatch,
                "the radar read-back line is latched: it cannot follow the aircraft once the pilot does it");
        }
    }

    // -----------------------------------------------------------------------
    // Shared run helpers
    // -----------------------------------------------------------------------

    // The flow's real steps with these ids, in flow order, under the flow's own id (so
    // CompletionGroupIds is the real one).
    private static FlowDefinition<TState> Subset<TState>(List<FlowDefinition<TState>> flows, string flowId, string[] stepIds)
        where TState : IFoStateEvaluator
    {
        var source = flows.Single(f => f.Id == flowId);
        return new FlowDefinition<TState>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
    }

    private static async Task RunToEnd<TExec, TState>(FlowManager<TExec, TState> flowManager, FlowDefinition<TState> flow)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        flowManager.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flowManager.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
            await Task.Delay(20);
        }
    }

    // -----------------------------------------------------------------------
    // The aircraft's real flows and checklists, as comparable data
    // -----------------------------------------------------------------------

    private static (Dictionary<(string Flow, string Step), List<string>> Links, Dictionary<string, List<string>> Groups)
        Profile(string aircraft) => aircraft switch
    {
        "Fenix" => (LinksOf(FenixFlowDefinitions.Build()), GroupsOf(FenixChecklistDefinitions.Build())),
        "A32NX" => (LinksOf(FbwA320FlowDefinitions.Build()), GroupsOf(FbwA320ChecklistDefinitions.Build())),
        "A330" => (LinksOf(HwA330FlowDefinitions.Build()), GroupsOf(HwA330ChecklistDefinitions.Build())),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static Dictionary<(string Flow, string Step), List<string>> LinksOf<TState>(
        IEnumerable<FlowDefinition<TState>> flows) where TState : IFoStateEvaluator =>
        flows.SelectMany(f => f.Steps.Select(s => (Key: (Flow: f.Id, Step: s.Id), Links: s.LinkedChecklistItemIds.ToList())))
            .ToDictionary(x => x.Key, x => x.Links);

    private static Dictionary<string, List<string>> GroupsOf<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.ToDictionary(g => g.Id, g => g.Items.Select(i => i.Id).ToList());
}
