using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// BEHAVIOUR tests of FlowManager's stop and verify paths, on a real FlowManager (harness as
/// FlowManagerLeaveAloneTests):
/// - a Stop always ends at the one cancellation exit: "{flow} flow cancelled" is spoken and
///   FlowCancelled raised, wherever the run was. A Stop during the LAST step's write once went on
///   to "flow complete" and latched the flow's checklists, and a Stop inside a wait or a delay was
///   silent;
/// - a step's verify gives the write a window (a first read at 600 ms, then every 200 ms, up to
///   2.6 s), so a write the PMDG 1 Hz snapshot shows late is still done. One read at 600 ms called
///   about half the 777's verified writes "Skipping".
/// </summary>
public class FlowManagerCancelAndVerifyTests
{
    private sealed class FakeState : IFoStateEvaluator
    {
        // The verify field reads 1 once ReadsOnAfterMs have passed since the write (Written), else 0.
        public readonly Stopwatch Written = new();
        public long ReadsOnAfterMs = long.MaxValue;
        public bool IsAvailable => true;
        public double GetValue(string field)
            => field == "VERIFY" && Written.IsRunning && Written.ElapsedMilliseconds >= ReadsOnAfterMs ? 1 : 0;
        public bool IsOn(string field) => false;
        public bool IsPosition(string field, int position) => false;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class FakeExecutor : IFoActionExecutor
    {
        private readonly FakeState _state;
        public FakeExecutor(FakeState state) => _state = state;

        public ConcurrentQueue<string> Sent { get; } = new();
        // When set, a write blocks until the test releases it; WriteStarted completes when it begins.
        public TaskCompletionSource<bool>? Hold;
        public readonly TaskCompletionSource WriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;

        public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            _state.Written.Restart();
            WriteStarted.TrySetResult();
            return Hold == null || await Hold.Task;
        }

        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; }
        public GatedSpeechCapture Speech { get; } = new();
        public FlowManager<FakeExecutor, FakeState> Flows { get; }
        public int Completed, Cancelled;
        public List<string> StepsCompleted { get; } = new();
        public List<string> StepsSkipped { get; } = new();

        public Harness()
        {
            Executor = new FakeExecutor(State);
            var checklist = new ChecklistManager<FakeExecutor, FakeState>(
                State, Executor, new List<ChecklistGroup<FakeExecutor, FakeState>>());
            Flows = new FlowManager<FakeExecutor, FakeState>(State, Executor, checklist, Speech);
            Flows.FlowCompleted += _ => Interlocked.Increment(ref Completed);
            Flows.FlowCancelled += _ => Interlocked.Increment(ref Cancelled);
            Flows.StepCompleted += (_, step, _) => { lock (StepsCompleted) StepsCompleted.Add(step.Id); };
            Flows.StepSkipped += (_, step, _) => { lock (StepsSkipped) StepsSkipped.Add(step.Id); };
        }

        public async Task WaitUntilStoppedAsync()
        {
            var sw = Stopwatch.StartNew();
            while (Flows.IsRunning)
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
                await Task.Delay(20);
            }
        }
    }

    private static FlowStep<FakeState> Write(string id, bool verify = false) => new()
    {
        Id = id,
        Label = $"{id}: ON",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = id,
        TargetValue = 1,
        PostActionDelayMs = 0,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        CompletesChecklistItemId = "ITEM_" + id,
        VerifyFieldName = verify ? "VERIFY" : null,
        VerifyCondition = verify ? v => v > 0.5 : null,
    };

    private static FlowStep<FakeState> WaitForever(string id) => new()
    {
        Id = id,
        Label = id,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = "NEVER",
        Condition = v => v > 0.5,
        TimeoutSeconds = 30,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static FlowDefinition<FakeState> Flow(params FlowStep<FakeState>[] steps) => new()
    {
        Id = "TEST",
        Name = "Test",
        Steps = steps.ToList(),
    };

    [Fact]
    public async Task A_stop_during_the_last_steps_write_never_completes_the_flow()
    {
        var h = new Harness();
        h.Executor.Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        h.Flows.StartFlow(Flow(Write("A")));
        await h.Executor.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Flows.Cancel();                     // Stop while the executor is still writing
        h.Executor.Hold.SetResult(true);      // the write itself lands
        await h.WaitUntilStoppedAsync();

        Assert.Equal(0, h.Completed);         // never "complete", never latched
        Assert.Equal(1, h.Cancelled);
        Assert.Empty(h.StepsCompleted);       // its line is not marked either
        Assert.Contains("Test flow cancelled", h.Speech.All);
        Assert.DoesNotContain("Test flow complete", h.Speech.All);
    }

    [Fact]
    public async Task A_stop_inside_a_wait_is_spoken_once()
    {
        var h = new Harness();

        h.Flows.StartFlow(Flow(WaitForever("W"), Write("A")));
        var sw = Stopwatch.StartNew();
        while (!h.Speech.All.Contains("Waiting for: W"))
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), "the wait never started");
            await Task.Delay(20);
        }
        h.Flows.Cancel();
        await h.WaitUntilStoppedAsync();

        Assert.Equal(1, h.Cancelled);
        Assert.Equal(0, h.Completed);
        Assert.Single(h.Speech.All, m => m == "Test flow cancelled");
        Assert.Empty(h.Executor.Sent);        // the step after the wait never ran
    }

    [Fact]
    public async Task A_stop_in_the_pause_between_steps_is_spoken_once()
    {
        var h = new Harness();

        h.Flows.StartFlow(Flow(Write("A"), Write("B")));
        await h.Executor.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(300);                // inside the 2 s pause after A
        h.Flows.Cancel();
        await h.WaitUntilStoppedAsync();

        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.Equal(1, h.Cancelled);
        Assert.Single(h.Speech.All, m => m == "Test flow cancelled");
    }

    [Fact]
    public async Task A_write_the_aircraft_shows_late_inside_the_window_is_done()
    {
        var h = new Harness();
        h.State.ReadsOnAfterMs = 1500;        // the next 1 Hz snapshot after a write landing just after one

        await RunAsync(h, Flow(Write("A", verify: true)));

        Assert.Equal(new[] { "A" }, h.StepsCompleted);
        Assert.Empty(h.StepsSkipped);
        Assert.DoesNotContain("Skipping: A: ON", h.Speech.All);
        Assert.Equal(1, h.Completed);
    }

    [Fact]
    public async Task A_write_the_aircraft_never_shows_is_skipped_once_the_window_has_passed()
    {
        var h = new Harness();                // the verify field never reads on
        var sw = Stopwatch.StartNew();

        await RunAsync(h, Flow(Write("A", verify: true)));

        Assert.True(sw.ElapsedMilliseconds >= 2600, $"judged after {sw.ElapsedMilliseconds} ms");
        Assert.Equal(new[] { "A" }, h.StepsSkipped);
        Assert.Contains("Skipping: A: ON", h.Speech.All);
        Assert.Equal(new[] { "ITEM_A" }, h.Flows.UnfinishedChecklistItemIds.ToArray());
    }

    [Fact]
    public async Task A_write_the_aircraft_shows_at_once_is_judged_at_the_first_read()
    {
        var h = new Harness();
        h.State.ReadsOnAfterMs = 0;
        var sw = Stopwatch.StartNew();

        await RunAsync(h, Flow(Write("A", verify: true)));

        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(new[] { "A" }, h.StepsCompleted);
    }

    private static async Task RunAsync(Harness h, FlowDefinition<FakeState> flow)
    {
        h.Flows.StartFlow(flow);
        await h.WaitUntilStoppedAsync();
    }
}
