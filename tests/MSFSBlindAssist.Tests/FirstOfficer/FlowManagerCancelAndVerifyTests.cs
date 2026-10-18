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
///   about half the 777's verified writes "Skipping";
/// - a Stop during the executor's step (atomic: the PMDG radar test runs about 25 s) is answered at
///   once: the run reports it is stopping inside that step, the executor is handed the run's stop
///   token, and a stopped run lets the executor put back what a flow must never be left between
///   before "flow cancelled" is spoken (PR 160 second review);
/// - a Skip-policy failure is paced like every other spoken step, and a wait's last read is at its
///   timeout, after its own pause check (PR 160 second review).
/// </summary>
public class FlowManagerCancelAndVerifyTests
{
    private sealed class FakeState : IFoStateEvaluator
    {
        // The verify field reads 1 once ReadsOnAfterMs have passed since the write (Written), else 0.
        public readonly Stopwatch Written = new();
        public long ReadsOnAfterMs = long.MaxValue;
        // The wait field reads 1 once SoonAfterMs have passed since the flow started (Started), else 0.
        public readonly Stopwatch Started = new();
        public long SoonAfterMs = long.MaxValue;
        public bool IsAvailable => true;
        public double GetValue(string field) => field switch
        {
            "VERIFY" => Written.IsRunning && Written.ElapsedMilliseconds >= ReadsOnAfterMs ? 1 : 0,
            "SOON" => Started.IsRunning && Started.ElapsedMilliseconds >= SoonAfterMs ? 1 : 0,
            _ => 0,
        };
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
        // When each event was written, on the state's Started clock.
        public ConcurrentDictionary<string, long> SentAtMs { get; } = new();
        // Events whose write fails (the executor returns false).
        public readonly HashSet<string> Fails = new();
        // When set, a write blocks until the test releases it; WriteStarted completes when it begins.
        public TaskCompletionSource<bool>? Hold;
        public readonly TaskCompletionSource WriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // The stop token the last write was handed.
        public CancellationToken StopToken;
        // OnFlowStoppedAsync counts its calls and runs OnStopped.
        public Action? OnStopped;
        public int StopCalls;
        public bool IsAvailable => true;

        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step, CancellationToken stop)
        {
            StopToken = stop;
            return ExecuteStepAsync(step);
        }

        public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            SentAtMs[step.EventName ?? ""] = _state.Started.ElapsedMilliseconds;
            _state.Written.Restart();
            WriteStarted.TrySetResult();
            if (Fails.Contains(step.EventName ?? "")) return false;
            return Hold == null || await Hold.Task;
        }

        public Task OnFlowStoppedAsync()
        {
            Interlocked.Increment(ref StopCalls);
            OnStopped?.Invoke();
            return Task.CompletedTask;
        }

        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; }
        public GatedSpeechCapture Speech { get; } = new();
        public FlowManager<FakeExecutor, FakeState> Flows { get; }
        public int Completed, Cancelled, Failed;
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
            Flows.FlowFailed += (_, _) => Interlocked.Increment(ref Failed);
            Flows.StepCompleted += (_, step, _) => { lock (StepsCompleted) StepsCompleted.Add(step.Id); };
            Flows.StepSkipped += (_, step, _) => { lock (StepsSkipped) StepsSkipped.Add(step.Id); };
        }

        public void Start(FlowDefinition<FakeState> flow)
        {
            State.Started.Restart();
            Flows.StartFlow(flow);
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

        public async Task WaitForSpeechAsync(string text)
        {
            var sw = Stopwatch.StartNew();
            while (!Speech.All.Contains(text))
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"never said \"{text}\"");
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
        TargetValue = 1,        FailurePolicy = FlowStepFailurePolicy.Skip,
        CompletesChecklistItemId = "ITEM_" + id,
        VerifyFieldName = verify ? "VERIFY" : null,
        VerifyCondition = verify ? v => v > 0.5 : null,
    };

    private static FlowStep<FakeState> WaitForever(string id) => WaitFor(id, "NEVER", timeoutSeconds: 30);

    private static FlowStep<FakeState> WaitFor(string id, string field, int timeoutSeconds) => new()
    {
        Id = id,
        Label = id,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = v => v > 0.5,
        TimeoutSeconds = timeoutSeconds,
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

    // A Stop does not interrupt the executor's step, so the window answers it from these: "Stopping
    // after the current step" on the press, and "still stopping" to a Start before the run unwinds.
    [Fact]
    public async Task A_stop_during_an_executor_step_reads_as_stopping_until_the_run_unwinds()
    {
        var h = new Harness();
        h.Executor.Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        h.Start(Flow(Write("A"), Write("B")));
        await h.Executor.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(h.Flows.IsInExecutorStep);
        Assert.False(h.Flows.IsStopping);

        h.Flows.Cancel();
        Assert.True(h.Flows.IsStopping);
        Assert.True(h.Flows.IsInExecutorStep);

        h.Executor.Hold.SetResult(true);
        await h.WaitUntilStoppedAsync();
        Assert.False(h.Flows.IsStopping);
        Assert.False(h.Flows.IsInExecutorStep);
        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.Equal(1, h.Cancelled);
    }

    // The executor is handed the run's stop token, so a step that watches for long (the MD-11's
    // engine light-up) can end on the Stop instead of holding the run for its whole watch.
    [Fact]
    public async Task The_executor_is_handed_the_runs_stop_token()
    {
        var h = new Harness();
        h.Executor.Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        h.Start(Flow(Write("A")));
        await h.Executor.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(h.Executor.StopToken.IsCancellationRequested);
        h.Flows.Cancel();
        Assert.True(h.Executor.StopToken.IsCancellationRequested);

        h.Executor.Hold.SetResult(true);
        await h.WaitUntilStoppedAsync();
    }

    // A stopped run lets the executor put back what a flow must never be left between (the MD-11's
    // START pulled with the fuel still off) before "flow cancelled" is spoken.
    [Fact]
    public async Task A_stop_runs_the_executors_clean_up_before_flow_cancelled()
    {
        var h = new Harness();
        bool cancelledAlreadySpoken = true;
        h.Executor.OnStopped = () => cancelledAlreadySpoken = h.Speech.All.Contains("Test flow cancelled");

        h.Start(Flow(Write("A"), Write("B")));
        await h.Executor.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Flows.Cancel();
        await h.WaitUntilStoppedAsync();

        Assert.Equal(1, h.Executor.StopCalls);
        Assert.False(cancelledAlreadySpoken);
        Assert.Contains("Test flow cancelled", h.Speech.All);
    }

    [Fact]
    public async Task A_completed_run_never_runs_the_stop_clean_up()
    {
        var h = new Harness();

        await RunAsync(h, Flow(Write("A")));

        Assert.Equal(1, h.Completed);
        Assert.Equal(0, h.Executor.StopCalls);
    }

    // A Stop-policy failure ends the run part-way as a Stop does: the run's token is cancelled (how
    // the MD-11 executor tells this run's START pulls from a later run's) and the clean-up runs
    // before "flow stopped". A skipped fuel step and a timed-out wait after it once left the MD-11's
    // START pulled with the fuel off.
    [Fact]
    public async Task A_stop_policy_failure_runs_the_clean_up_with_the_runs_token_cancelled()
    {
        var h = new Harness();
        var failing = Write("A");
        failing.FailurePolicy = FlowStepFailurePolicy.Stop;
        h.Executor.Fails.Add("A");
        bool tokenCancelled = false, stoppedAlreadySpoken = true;
        h.Executor.OnStopped = () =>
        {
            tokenCancelled = h.Executor.StopToken.IsCancellationRequested;
            stoppedAlreadySpoken = h.Speech.All.Any(m => m.StartsWith("Test flow stopped"));
        };

        await RunAsync(h, Flow(failing, Write("B")));

        Assert.Equal(1, h.Executor.StopCalls);
        Assert.True(tokenCancelled);
        Assert.False(stoppedAlreadySpoken);
        Assert.Contains("Test flow stopped. Unable to complete: A: ON", h.Speech.All);
        Assert.Equal(1, h.Failed);
        Assert.Equal(0, h.Cancelled);
        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());   // B never ran
    }

    [Fact]
    public async Task A_clean_up_that_throws_still_ends_the_run_cancelled()
    {
        var h = new Harness();
        h.Executor.OnStopped = () => throw new InvalidOperationException("clean-up failed");

        h.Start(Flow(Write("A"), Write("B")));
        await h.Executor.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Flows.Cancel();
        await h.WaitUntilStoppedAsync();

        Assert.Equal(1, h.Cancelled);
        Assert.Contains("Test flow cancelled", h.Speech.All);
    }

    // "Skipping: …" is spoken, so a Skip-policy failure is paced like every other spoken step (FO-5):
    // the next step's words and write once followed it at once.
    [Fact]
    public async Task A_failed_skip_step_pauses_before_the_next_step()
    {
        var h = new Harness();
        h.Executor.Fails.Add("A");

        await RunAsync(h, Flow(Write("A"), Write("B")));

        Assert.Equal(new[] { "A" }, h.StepsSkipped);
        long gap = h.Executor.SentAtMs["B"] - h.Executor.SentAtMs["A"];
        Assert.True(gap >= 1900, $"B followed the skipped A after {gap} ms");
    }

    // The wait reads at 0, 1, … TimeoutSeconds, so its last read is AT the timeout. Ending on the
    // count after the last delay, a condition that came true in the final second was "timed out".
    [Fact]
    public async Task A_condition_that_comes_true_in_the_last_second_is_done()
    {
        var h = new Harness();
        h.State.SoonAfterMs = 500;

        await RunAsync(h, Flow(WaitFor("W", "SOON", timeoutSeconds: 1)));

        Assert.Equal(new[] { "W" }, h.StepsCompleted);
        Assert.DoesNotContain("Timed out waiting for: W", h.Speech.All);
    }

    // A Pause in the wait's final second holds the wait: the last read comes after its own pause
    // check. It once timed the wait out, and the flow went on, under "Paused".
    [Fact]
    public async Task A_pause_in_the_waits_last_second_holds_the_timeout()
    {
        var h = new Harness();

        h.Start(Flow(WaitFor("W", "NEVER", timeoutSeconds: 1)));
        await h.WaitForSpeechAsync("Waiting for: W");
        await Task.Delay(300);
        h.Flows.Pause();
        await Task.Delay(1500);               // well past the timeout

        Assert.DoesNotContain("Timed out waiting for: W", h.Speech.All);
        Assert.True(h.Flows.IsRunning);

        h.Flows.Resume();
        await h.WaitUntilStoppedAsync();
        Assert.Contains("Timed out waiting for: W", h.Speech.All);
    }

    private static async Task RunAsync(Harness h, FlowDefinition<FakeState> flow)
    {
        h.Start(flow);
        await h.WaitUntilStoppedAsync();
    }
}
