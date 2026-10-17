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
/// The behaviour half of <see cref="A320FamilyReadbackLinkTests"/> (FO-6): the real steps, checklists
/// and a real <see cref="FlowManager{TExec, TState}"/> run in real time, so a run that passes a
/// step's 2 s inter-step pause ([FO-5], deliberate and never shortened) or waits out the 5 s timeout
/// of the radar check or the takeoff-memo check takes seconds. xUnit runs the tests of ONE class one
/// after the other and different classes in parallel, so the three aircraft each have a class of
/// their own below
/// (<see cref="A320FamilyReadbackLinkBehaviourFenixTests"/>, <see cref="A320FamilyReadbackLinkBehaviourA32nxTests"/>,
/// <see cref="A320FamilyReadbackLinkBehaviourA330Tests"/>), and each run drives a SHORT flow built from
/// only the real steps under test (see <see cref="Subset"/>), the way
/// <c>FlowManagerStepDependencyTests</c> and its siblings do. The pure structural link and pairing tests
/// stay in <see cref="A320FamilyReadbackLinkTests"/>. Do not merge the classes back into one: that is the
/// ~30 s the suite paid before.
/// </summary>
public abstract class A320FamilyReadbackLinkBehaviourTests<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : LVarStateEvaluator
{
    // Flow, the write steps of that flow under test, and every line those steps complete.
    private static readonly (string Flow, string[] Steps, string[] Lines)[] Rows =
    {
        ("BEFORE_START", new[] { "BS_BEACON" }, new[] { "BS_BEACON", "BSC_BEACON" }),
        ("AFTER_START", new[] { "AS_RUDDERTRIM" }, new[] { "AS_RUDDERTRIM", "ASC_RUDDER" }),
        ("TAXI", new[] { "TX_AUTOBRAKE", "TX_CABIN", "TX_CONFIG" },
            new[] { "TX_AUTOBRAKE", "TXC_MEMO_AUTOBRK", "TX_CABIN", "TXC_MEMO_CABIN", "TX_CONFIG", "TXC_MEMO_TOCFG" }),
        ("LINEUP", new[] { "LU_TCAS" }, new[] { "LU_TCAS", "LUC_TCAS" }),
        ("APPROACH", new[] { "AP_SEATBELTS" }, new[] { "AP_SEATBELTS", "APC_SEATBELTS" }),
        ("LANDING", new[] { "LD_SPOILERS_ARM" }, new[] { "LD_SPOILERS_ARM", "LDC_MEMO_SPLRS" }),
        ("AFTER_LANDING", new[] { "AL_WXR_OFF", "AL_PWS_OFF" }, new[] { "AL_WXR_OFF", "AL_PWS_OFF" }),
    };

    private const string RadarCheck = A320FamilyReadbackLinkTests.RadarCheck;
    private const string RadarLine = A320FamilyReadbackLinkTests.RadarLine;
    private const string RadarLabel = A320FamilyReadbackLinkTests.RadarLabel;
    private const string WxrOnCheck = A320FamilyReadbackLinkTests.WxrOnCheck;
    private const string WxrOnLine = A320FamilyReadbackLinkTests.WxrOnLine;
    private const string WxrOnLabel = A320FamilyReadbackLinkTests.WxrOnLabel;
    private const string MemoCheck = A320FamilyReadbackLinkTests.MemoCheck;
    private const string MemoLabel = A320FamilyReadbackLinkTests.MemoLabel;
    private static readonly string[] MemoLines = A320FamilyReadbackLinkTests.MemoLines;

    // The aircraft under test: a fresh evaluator, an executor with no sim (every write it is asked for
    // fails: "Sim not connected"), and its real checklists and flows.
    protected abstract TState NewState();
    protected abstract TExec NewExecutor();
    protected abstract List<ChecklistGroup<TExec, TState>> BuildChecklists();
    protected abstract List<FlowDefinition<TState>> BuildFlows();

    // The L-vars the radar read-back reads: the radar switch, and the predictive windshear switch.
    protected abstract string RadarVar { get; }
    protected abstract string WindshearVar { get; }

    // A cache where everything the Taxi flow's two checks read is set for takeoff (radar system 1,
    // predictive windshear AUTO, ground spoilers armed, the memo's signs on) except the flap lever,
    // which is the given position.
    protected abstract (string Key, double Value)[] TaxiReady(double flapLever);

    // The TCAS switch at TA/RA.
    protected abstract (string Key, double Value) TcasTaRa { get; }

    public static IEnumerable<object[]> FlowRuns() =>
        Rows.Select(r => new object[] { r.Flow, r.Steps, r.Lines });

    /// <summary>The bug itself, on the real steps, checklists and a real FlowManager (no sim, so
    /// every write fails: "Sim not connected"). A step that is skipped aloud must leave EVERY line it
    /// names unticked after the end-of-flow latch, applied the way FirstOfficerForm.OnFlowCompleted
    /// applies it, over the flow's whole CompletionGroupIds, and free to follow the aircraft. The radar
    /// read-back line is not a write's: the radar runs below cover it.</summary>
    [Theory]
    [MemberData(nameof(FlowRuns))]
    public async Task A_failed_write_leaves_its_lines_unticked_and_unlatched(
        string flowId, string[] stepIds, string[] lineIds)
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();

        var flow = Subset(BuildFlows(), flowId, stepIds);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_radar_readback_ticks_only_once_radar_and_windshear_both_read_off(bool windshearOff)
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();

        // The same encoding on all three: radar 1 = OFF; predictive windshear 0 = OFF, 1 = AUTO.
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith((RadarVar, 1), (WindshearVar, windshearOff ? 0 : 1)));
        Assert.False(executor.IsAvailable);

        string[] stepIds = { "AL_WXR_OFF", "AL_PWS_OFF", RadarCheck };
        var flow = Subset(BuildFlows(), "AFTER_LANDING", stepIds);
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

    /// <summary>The Taxi flow's two read-only checks on the real steps, evaluator and checklists and a
    /// real FlowManager, with everything set for takeoff except the flaps, still UP (held until the
    /// holding point, the icing case), and the window's one-second auto-detection having run first, as
    /// it does the whole time the First Officer window is open: every line that already reads right is
    /// ticked when the flow starts. The radar check passes; the takeoff-memo check times out and is
    /// skipped aloud. The end-of-flow latch (MarkGroupComplete) then frees only the memo lines that
    /// read wrong, "Flaps: T.O" and "Flaps setting: SET" (a Reminder the aircraft cannot answer): they
    /// stay unticked and out of the latch, while the rest of the Taxi checklist latches, "Spoilers:
    /// ARMED" and the seat-belts (or signs) line included, which read right and were ticked before the
    /// flow ran. The pilot then sets takeoff flaps: "Flaps: T.O" ticks itself from the aircraft, and
    /// "Flaps setting: SET" stays open until they tick it.</summary>
    [Fact]
    public async Task A_taxi_run_with_the_flaps_up_skips_the_memo_check_and_frees_only_the_lines_that_read_wrong()
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(TaxiReady(flapLever: 0)));

        string[] stepIds = { WxrOnCheck, MemoCheck };
        var flow = Subset(BuildFlows(), "TAXI", stepIds);
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());

        // The memo lines that read wrong with the flaps up, and the two that read right.
        string[] open = { "TXC_MEMO_FLAPS", "TXC_FLAPS" };
        string[] right = { "TXC_MEMO_SPLRS", "TXC_MEMO_SIGNS" };
        Assert.Equal(MemoLines.Order().ToArray(), open.Concat(right).Order().ToArray());

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        checklist.EvaluateAutoDetection();   // the window's 1 s timer: lines that already read right are ticked
        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        Assert.All(right, id => Assert.True(items[id].IsChecked, $"{id} reads right and was not ticked by the window"));
        Assert.All(open, id => Assert.False(items[id].IsChecked, $"{id} reads wrong and was ticked"));

        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        // The memo check timed out and is skipped aloud, and all four of its lines are in the run's
        // unfinished set: the latch decides below which of them it frees.
        Assert.Equal(new[] { MemoCheck }, skipped.ToArray());
        Assert.Contains($"Timed out waiting for: {MemoLabel}", speech.All);
        Assert.Contains($"Skipping: {MemoLabel}", speech.All);
        Assert.Equal(MemoLines.Order().ToArray(), flowManager.UnfinishedChecklistItemIds.Order().ToArray());

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        var taxiCl = groups.Single(g => g.Id == "TAXI_CL");
        Assert.True(taxiCl.CompletionLatched);
        foreach (var line in taxiCl.Items)
        {
            if (open.Contains(line.Id))
            {
                // The lines that read wrong stay unticked and out of the latch, following the aircraft.
                Assert.False(line.IsChecked, $"{line.Id} was ticked over a memo check that timed out");
                Assert.True(line.ExemptFromCompletionLatch, $"{line.Id} is latched: it cannot follow the aircraft");
            }
            else
            {
                // Every other line, the memo lines that already read right included, is ticked and latched.
                Assert.True(line.IsChecked, $"{line.Id} was not ticked by the finished Taxi flow");
                Assert.False(line.ExemptFromCompletionLatch, $"{line.Id} was left out of the latch");
            }
        }

        // The pilot sets takeoff flaps: "Flaps: T.O" follows the aircraft by itself (and is free of the
        // exemption once the aircraft agrees), the latched lines stand as they were ...
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(TaxiReady(flapLever: 1)));
        checklist.EvaluateAutoDetection();
        Assert.True(items["TXC_MEMO_FLAPS"].IsChecked);
        Assert.False(items["TXC_MEMO_FLAPS"].ExemptFromCompletionLatch);
        Assert.All(right, id => Assert.True(items[id].IsChecked, id));
        // ... and "Flaps setting: SET", a line the pilot confirms, waits for their tick.
        Assert.False(items["TXC_FLAPS"].IsChecked);
    }

    /// <summary>The Taxi flow's radar block (both radar writes, then the check) with the radar OFF, on
    /// the real steps, evaluator and checklists and a real FlowManager, the way the After Landing run
    /// above covers its check with the windshear on AUTO. The evaluator reads a seeded cache that counts
    /// as connected, with the radar OFF and the predictive windshear on AUTO; the executor has no sim,
    /// so the radar write fails and is skipped aloud, and the windshear step is "Already set". The check
    /// then times out and is skipped aloud too, and says nothing like "Already set": the Taxi Checklist's
    /// "Radar and predictive windshear: ON and AUTO" stays unticked, and the end-of-flow latch leaves it
    /// free to follow the aircraft, so it ticks itself once the pilot turns the radar on.</summary>
    [Fact]
    public async Task A_taxi_run_with_the_radar_off_skips_the_radar_check_and_leaves_its_line_open()
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();

        // The same encoding on all three: radar 0 = system 1, 1 = OFF; predictive windshear 1 = AUTO.
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith((RadarVar, 1), (WindshearVar, 1)));
        Assert.False(executor.IsAvailable);

        string[] stepIds = { "TX_WXR", "TX_PWS", WxrOnCheck };
        var flow = Subset(BuildFlows(), "TAXI", stepIds);
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        checklist.EvaluateAutoDetection();   // the window's 1 s timer: the radar reads OFF, so the line is open
        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        var line = items[WxrOnLine];
        Assert.False(line.IsChecked, "the radar reads OFF and the window ticked the radar line");

        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        // The radar write could not be sent and the check, which reads the radar, timed out: both are
        // skipped aloud, the check never says "Already set", and the run left the line unticked.
        Assert.Equal(new[] { "TX_WXR", WxrOnCheck }, skipped.ToArray());
        Assert.Contains($"Timed out waiting for: {WxrOnLabel}", speech.All);
        Assert.Contains($"Skipping: {WxrOnLabel}", speech.All);
        Assert.DoesNotContain($"Already set: {WxrOnLabel}", speech.All);
        Assert.False(line.IsChecked, "the radar line was ticked with the radar OFF");
        Assert.Equal(new[] { "TX_WXR", WxrOnLine }.Order(StringComparer.Ordinal).ToArray(),
            flowManager.UnfinishedChecklistItemIds.Order(StringComparer.Ordinal).ToArray());

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        // The radar line, and the radar write's own line, stay unticked and out of the latch; the
        // windshear write, "Already set", is done.
        Assert.False(line.IsChecked, "the radar line was ticked with the radar OFF");
        Assert.True(line.ExemptFromCompletionLatch,
            "the radar line is latched: it cannot follow the aircraft once the pilot turns the radar on");
        Assert.False(items["TX_WXR"].IsChecked);
        Assert.True(items["TX_WXR"].ExemptFromCompletionLatch);
        Assert.True(items["TX_PWS"].IsChecked);
        Assert.False(items["TX_PWS"].ExemptFromCompletionLatch);

        // The pilot turns the radar on: the line follows the aircraft and ticks itself.
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith((RadarVar, 0), (WindshearVar, 1)));
        checklist.EvaluateAutoDetection();
        Assert.True(line.IsChecked);
        Assert.False(line.ExemptFromCompletionLatch);
    }

    /// <summary>The same two checks with the flaps set for takeoff: both pass ("Already set"), they
    /// are what tick their five Taxi checklist lines, and the finished flow latches the whole list, the
    /// lines only the pilot can confirm included.</summary>
    [Fact]
    public async Task A_taxi_run_whose_checks_pass_ticks_and_latches_the_whole_taxi_checklist()
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(TaxiReady(flapLever: 2)));

        string[] stepIds = { WxrOnCheck, MemoCheck };
        var flow = Subset(BuildFlows(), "TAXI", stepIds);
        Assert.Equal(new[] { "TAXI", "TAXI_CL" }, flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)).ToArray());

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);
        var skipped = new List<string>();
        flowManager.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        await RunToEnd(flowManager, flow);

        Assert.Empty(skipped);
        Assert.Empty(flowManager.UnfinishedChecklistItemIds);
        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var id in MemoLines.Append(WxrOnLine))
            Assert.True(items[id].IsChecked, $"{id} was not ticked by its check");
        Assert.False(items["TXC_FCTEST"].IsChecked);   // the pilot's line: the finished flow ticks it, below

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        var taxiCl = groups.Single(g => g.Id == "TAXI_CL");
        Assert.True(taxiCl.CompletionLatched);
        Assert.All(taxiCl.Items, i =>
        {
            Assert.True(i.IsChecked, i.Id);
            Assert.False(i.ExemptFromCompletionLatch, i.Id);
        });
    }

    /// <summary>The Line-up flow's TCAS step with TCAS already at TA/RA: it ticks the Line-up
    /// Checklist's "TCAS: TA/RA" itself, and the finished flow ticks and latches the rest of that list
    /// ("Takeoff runway: CONFIRMED", "Packs 1 and 2: SET", lines the pilot confirms).</summary>
    [Fact]
    public async Task A_line_up_run_ticks_TCAS_from_its_step_and_latches_the_line_up_checklist()
    {
        var state = NewState();
        var executor = NewExecutor();
        var groups = BuildChecklists();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(TcasTaRa));

        var flow = Subset(BuildFlows(), "LINEUP", new[] { "LU_TCAS" });
        Assert.Equal(new[] { "LINEUP", "LINEUP_CL" }, flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)).ToArray());

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, TState>(state, executor, groups);
        var flowManager = new FlowManager<TExec, TState>(state, executor, checklist, speech);

        await RunToEnd(flowManager, flow);

        var items = groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        var lineup = groups.Single(g => g.Id == "LINEUP_CL");
        Assert.True(items["LUC_TCAS"].IsChecked);
        Assert.False(items["LUC_RUNWAY"].IsChecked);
        Assert.False(lineup.CompletionLatched);

        foreach (var groupId in flow.CompletionGroupIds(id => groups.Any(g => g.Id == id)))
            checklist.MarkGroupComplete(groupId, flowManager.UnfinishedChecklistItemIds);

        Assert.True(lineup.CompletionLatched);
        Assert.All(lineup.Items, i =>
        {
            Assert.True(i.IsChecked, i.Id);
            Assert.False(i.ExemptFromCompletionLatch, i.Id);
        });
    }

    // -----------------------------------------------------------------------
    // Shared run helpers
    // -----------------------------------------------------------------------

    // The flow's real steps with these ids, in flow order, under the flow's own id (so
    // CompletionGroupIds is the real one). Only these steps run: every step kept is one a test
    // asserts on, so no run pays an inter-step pause or a wait it does not need.
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
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
            await Task.Delay(20);
        }
    }
}

/// <summary>The Fenix A320: its own class so xUnit runs it alongside the other two (see
/// <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyReadbackLinkBehaviourFenixTests
    : A320FamilyReadbackLinkBehaviourTests<FenixActionExecutor, FenixStateEvaluator>
{
    protected override FenixStateEvaluator NewState() => new();
    protected override FenixActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>> BuildChecklists() =>
        FenixChecklistDefinitions.Build();
    protected override List<FlowDefinition<FenixStateEvaluator>> BuildFlows() => FenixFlowDefinitions.Build();
    protected override string RadarVar => "S_WR_SYS";
    protected override string WindshearVar => "S_WR_PRED_WS";
    protected override (string Key, double Value)[] TaxiReady(double flapLever) => new[]
    {
        ("S_WR_SYS", 0.0), ("S_WR_PRED_WS", 1.0),            // radar system 1, predictive windshear AUTO
        ("A_FC_SPEEDBRAKE", 0.0),                             // 0 = ARMED
        ("S_FC_FLAPS", flapLever),
        ("S_OH_SIGNS", 1.0), ("S_OH_SIGNS_SMOKING", 1.0),     // seat belts ON, no smoking AUTO
    };
    protected override (string Key, double Value) TcasTaRa => ("S_XPDR_MODE", 2.0);
}

/// <summary>The FlyByWire A32NX (see <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyReadbackLinkBehaviourA32nxTests
    : A320FamilyReadbackLinkBehaviourTests<FbwA320ActionExecutor, FbwA320StateEvaluator>
{
    protected override FbwA320StateEvaluator NewState() => new();
    protected override FbwA320ActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<FbwA320ActionExecutor, FbwA320StateEvaluator>> BuildChecklists() =>
        FbwA320ChecklistDefinitions.Build();
    protected override List<FlowDefinition<FbwA320StateEvaluator>> BuildFlows() => FbwA320FlowDefinitions.Build();
    protected override string RadarVar => "XMLVAR_A320_WeatherRadar_Sys";
    protected override string WindshearVar => "A32NX_SWITCH_RADAR_PWS_POSITION";
    protected override (string Key, double Value)[] TaxiReady(double flapLever) => new[]
    {
        ("XMLVAR_A320_WeatherRadar_Sys", 0.0), ("A32NX_SWITCH_RADAR_PWS_POSITION", 1.0),   // system 1, AUTO
        ("A32NX_SPOILERS_ARMED", 1.0),
        ("A32NX_FLAPS_HANDLE_INDEX", flapLever),
        ("CABIN SEATBELTS ALERT SWITCH", 1.0), ("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 1.0),
    };
    protected override (string Key, double Value) TcasTaRa => ("A32NX_SWITCH_TCAS_POSITION", 2.0);
}

/// <summary>The Headwind A330 (see <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyReadbackLinkBehaviourA330Tests
    : A320FamilyReadbackLinkBehaviourTests<HwA330ActionExecutor, HwA330StateEvaluator>
{
    protected override HwA330StateEvaluator NewState() => new();
    protected override HwA330ActionExecutor NewExecutor() => new();
    protected override List<ChecklistGroup<HwA330ActionExecutor, HwA330StateEvaluator>> BuildChecklists() =>
        HwA330ChecklistDefinitions.Build();
    protected override List<FlowDefinition<HwA330StateEvaluator>> BuildFlows() => HwA330FlowDefinitions.Build();
    protected override string RadarVar => "XMLVAR_A320_WeatherRadar_Sys";
    protected override string WindshearVar => "A32NX_SWITCH_RADAR_PWS_POSITION";
    protected override (string Key, double Value)[] TaxiReady(double flapLever) => new[]
    {
        ("XMLVAR_A320_WeatherRadar_Sys", 0.0), ("A32NX_SWITCH_RADAR_PWS_POSITION", 1.0),   // system 1, AUTO
        ("A32NX_SPOILERS_ARMED", 1.0),
        ("A32NX_FLAPS_HANDLE_INDEX", flapLever),
        ("CABIN SEATBELTS ALERT SWITCH", 1.0), ("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 1.0),
    };
    protected override (string Key, double Value) TcasTaRa => ("A32NX_SWITCH_TCAS_POSITION", 2.0);
}
