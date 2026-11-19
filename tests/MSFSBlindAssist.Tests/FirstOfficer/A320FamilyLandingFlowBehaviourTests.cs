using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The behaviour half of <see cref="A320FamilyLandingFlowTests{TExec, TState}"/> (FOA-11): the real
/// Landing flow steps, checklists and a real <see cref="FlowManager{TExec, TState}"/>, run in real time
/// (its 2 s inter-step pause, [FO-5]). The evaluator reads a seeded cache that counts as connected; the
/// executor has no sim, so a write it is asked for fails. One class per aircraft below, so xUnit runs
/// them in parallel (the <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/> layout).
/// </summary>
public abstract class A320FamilyLandingFlowBehaviourTests<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : LVarStateEvaluator
{
    protected abstract TState NewState();
    protected abstract TExec NewExecutor();
    protected abstract List<ChecklistGroup<TExec, TState>> BuildChecklists();
    protected abstract List<FlowDefinition<TState>> BuildFlows();

    // The aircraft set for landing: seat belts on, flaps FULL (LDG FLAP 3 off), gear down, speed brake
    // retracted, and the ground spoilers armed or not.
    protected abstract (string Key, double Value)[] LandingReady(bool spoilersArmed);

    // The same with the speed brake out at half (so the ground spoilers read disarmed).
    protected abstract (string Key, double Value)[] SpeedBrakeOut();

    /// <summary>The Captain is using the speed brake on final: the arm is left alone and said so, nothing
    /// is sent, and both spoiler lines stay open and out of the latch, so they tick themselves once the
    /// pilot retracts the speed brake and arms the ground spoilers.</summary>
    [Fact]
    public async Task Over_an_extended_speed_brake_the_arm_is_left_alone_and_both_spoiler_lines_stay_open()
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(SpeedBrakeOut()));

        var flow = Subset(BuildFlows(), "LANDING", new[] { "LD_SPOILERS_ARM" });
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        Assert.Contains(AirbusGroundSpoilers.LeaveAloneText, speech.All);
        Assert.DoesNotContain(speech.All, s => s.Contains("Already set", StringComparison.Ordinal));
        Assert.Equal(new[] { "LD_SPOILERS_ARM" }, skipped.ToArray());
        Assert.Equal(new[] { "LDC_MEMO_SPLRS", "LD_SPOILERS_ARM" },
            flowManager.UnfinishedChecklistItemIds.Order(StringComparer.Ordinal).ToArray());

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var id in new[] { "LD_SPOILERS_ARM", "LDC_MEMO_SPLRS" })
        {
            Assert.False(items[id].IsChecked, $"{id} was ticked over a speed brake left alone");
            Assert.True(items[id].ExemptFromCompletionLatch, $"{id} is latched: it cannot follow the spoilers");
        }

        // The pilot retracts the speed brake and arms the ground spoilers: both lines follow by themselves.
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(LandingReady(spoilersArmed: true)));
        checklist.EvaluateAutoDetection();
        Assert.True(items["LD_SPOILERS_ARM"].IsChecked);
        Assert.True(items["LDC_MEMO_SPLRS"].IsChecked);
    }

    /// <summary>Everything already set: each step says "Already set", the gear check is the last step
    /// spoken, and the finished flow latches the Landing group and the whole Landing Checklist ("Cabin:
    /// READY", the pilot's line, included).</summary>
    [Fact]
    public async Task A_landing_run_whose_checks_pass_ticks_and_latches_the_landing_checklist()
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(LandingReady(spoilersArmed: true)));

        var flow = BuildFlows().Single(f => f.Id == "LANDING");
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);

        await RunToEnd(flowManager, flow);

        Assert.Equal(new[]
        {
            "Already set: Ground spoilers: ARMED",
            "Already set: Seat belts and flaps: set for landing",
            "Already set: Landing gear: DOWN",
        }, speech.All.Where(s => s.StartsWith("Already set", StringComparison.Ordinal)).ToArray());
        Assert.Empty(flowManager.UnfinishedChecklistItemIds);

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        foreach (var group in groups.Where(g => g.Id is "LANDING" or "LANDING_CL"))
        {
            Assert.True(group.CompletionLatched, group.Id);
            Assert.All(group.Items, i =>
            {
                Assert.True(i.IsChecked, i.Id);
                Assert.False(i.ExemptFromCompletionLatch, i.Id);
            });
        }
    }

    // The flow's real steps with these ids, in flow order, under the flow's own id (so
    // CompletionGroupIds is the real one).
    private static FlowDefinition<TState> Subset(List<FlowDefinition<TState>> flows, string flowId, string[] stepIds)
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

    private static async Task RunToEnd(FlowManager<TExec, TState> flowManager, FlowDefinition<TState> flow)
    {
        flowManager.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flowManager.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "flow did not finish");
            await Task.Delay(20);
        }
    }
}

/// <summary>The Fenix A320 (see <see cref="A320FamilyLandingFlowBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyLandingFlowBehaviourFenixTests
    : A320FamilyLandingFlowBehaviourTests<FenixActionExecutor, FenixStateEvaluator>
{
    protected override FenixStateEvaluator NewState() => new();
    protected override FenixActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>> BuildChecklists() =>
        FenixChecklistDefinitions.Build();
    protected override List<FlowDefinition<FenixStateEvaluator>> BuildFlows() => FenixFlowDefinitions.Build();

    private static (string Key, double Value)[] Ready(double speedBrake) => new[]
    {
        ("S_OH_SIGNS", 1.0), ("S_FC_FLAPS", 4.0), ("S_OH_GPWS_LDG_FLAP3", 0.0),
        ("A_FC_SPEEDBRAKE", speedBrake),   // 0 ARMED, 1 stowed, 2 half
        // Three green, no red (FenixGearConfirmation).
        (FenixGearConfirmation.LeverField, 1.0),
        ("I_MIP_GEAR_1_L", 1.0), ("I_MIP_GEAR_2_L", 1.0), ("I_MIP_GEAR_3_L", 1.0),
        ("I_MIP_GEAR_1_U", 0.0), ("I_MIP_GEAR_2_U", 0.0), ("I_MIP_GEAR_3_U", 0.0), ("I_MIP_GEAR_RED", 0.0),
    };

    protected override (string Key, double Value)[] LandingReady(bool spoilersArmed) => Ready(spoilersArmed ? 0.0 : 1.0);
    protected override (string Key, double Value)[] SpeedBrakeOut() => Ready(2.0);
}

/// <summary>The FlyByWire A32NX (see <see cref="A320FamilyLandingFlowBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyLandingFlowBehaviourA32nxTests
    : A320FamilyLandingFlowBehaviourTests<FbwA320ActionExecutor, FbwA320StateEvaluator>
{
    protected override FbwA320StateEvaluator NewState() => new();
    protected override FbwA320ActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<FbwA320ActionExecutor, FbwA320StateEvaluator>> BuildChecklists() =>
        FbwA320ChecklistDefinitions.Build();
    protected override List<FlowDefinition<FbwA320StateEvaluator>> BuildFlows() => FbwA320FlowDefinitions.Build();

    protected override (string Key, double Value)[] LandingReady(bool spoilersArmed) =>
        FbwLandingSeed.Ready(spoilersArmed ? 1.0 : 0.0, handle: 0.0);
    protected override (string Key, double Value)[] SpeedBrakeOut() => FbwLandingSeed.Ready(armed: 0.0, handle: 0.5);
}

/// <summary>The Headwind A330 (see <see cref="A320FamilyLandingFlowBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyLandingFlowBehaviourA330Tests
    : A320FamilyLandingFlowBehaviourTests<HwA330ActionExecutor, HwA330StateEvaluator>
{
    protected override HwA330StateEvaluator NewState() => new();
    protected override HwA330ActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<HwA330ActionExecutor, HwA330StateEvaluator>> BuildChecklists() =>
        HwA330ChecklistDefinitions.Build();
    protected override List<FlowDefinition<HwA330StateEvaluator>> BuildFlows() => HwA330FlowDefinitions.Build();

    protected override (string Key, double Value)[] LandingReady(bool spoilersArmed) =>
        FbwLandingSeed.Ready(spoilersArmed ? 1.0 : 0.0, handle: 0.0);
    protected override (string Key, double Value)[] SpeedBrakeOut() => FbwLandingSeed.Ready(armed: 0.0, handle: 0.5);
}

/// <summary>The FlyByWire airframes' landing cache: seat belts on, flaps FULL, gear lever down with every
/// leg out (FbwA320GearConfirmation), and the ground spoilers and speed brake handle as given.</summary>
internal static class FbwLandingSeed
{
    public static (string Key, double Value)[] Ready(double armed, double handle) => new[]
    {
        ("CABIN SEATBELTS ALERT SWITCH", 1.0), ("A32NX_FLAPS_HANDLE_INDEX", 4.0), ("A32NX_GPWS_FLAPS3", 0.0),
        ("A32NX_SPOILERS_ARMED", armed), ("A32NX_SPOILERS_HANDLE_POSITION", handle),
        (FbwA320GearConfirmation.HandleField, 1.0),
        ("A32NX_GEAR_CENTER_POSITION", 100.0), ("A32NX_GEAR_LEFT_POSITION", 100.0), ("A32NX_GEAR_RIGHT_POSITION", 100.0),
    };
}
