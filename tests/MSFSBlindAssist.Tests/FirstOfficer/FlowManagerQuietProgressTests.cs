using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// BEHAVIOUR tests of the "Speak flow progress" setting (UserSettings.FOSpeakFlowProgress,
/// FlowManager.SpeakProgress), on a real FlowManager (harness as FlowManagerLeaveAloneTests;
/// InterStepPauseMs is a real 2 s, so flows stay short). Off, a flow drops only its step-by-step
/// lines (the step itself, "Already set", "Waiting for", "Waiting N seconds"): a pilot reading
/// something else heard one every 2 s and had it broken up. What needs the pilot still speaks:
/// Captain actions, skips, timeouts, failures, "flow started" and "flow complete".
/// </summary>
public class FlowManagerQuietProgressTests
{
    private sealed class FakeState : IFoStateEvaluator
    {
        public bool IsAvailable => true;
        // "ON" reads 1 (a wait on it is met at once); every other field reads 0.
        public double GetValue(string field) => field == "ON" ? 1 : 0;
        public bool IsOn(string field) => false;
        public bool IsPosition(string field, int position) => false;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class FakeExecutor : IFoActionExecutor
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        // Events whose write fails (the executor returns false).
        public readonly HashSet<string> Fails = new();
        // Runs as each write is made, with its event name.
        public Action<string>? OnWrite;
        // The progress gate, as the FlowManager hands it over.
        public Func<bool>? ProgressGate;
        public bool IsAvailable => true;

        public void AttachFlowProgressSpeech(Func<bool> speaksProgress) => ProgressGate = speaksProgress;

        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            string name = step.EventName ?? "";
            Sent.Enqueue(name);
            OnWrite?.Invoke(name);
            return Task.FromResult(!Fails.Contains(name));
        }

        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; } = new();
        public GatedSpeechCapture Speech { get; } = new();
        public FlowManager<FakeExecutor, FakeState> Flows { get; }

        public Harness()
        {
            var checklist = new ChecklistManager<FakeExecutor, FakeState>(
                State, Executor, new List<ChecklistGroup<FakeExecutor, FakeState>>());
            Flows = new FlowManager<FakeExecutor, FakeState>(State, Executor, checklist, Speech);
        }

        public async Task RunAsync(FlowDefinition<FakeState> flow)
        {
            Flows.StartFlow(flow);
            var sw = Stopwatch.StartNew();
            while (Flows.IsRunning)
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
                await Task.Delay(20);
            }
        }
    }

    private static FlowStep<FakeState> Switch(string id, Func<FakeState, bool>? alreadySet = null,
        FlowStepFailurePolicy policy = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id,
        Label = $"{id}: ON",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = id,
        TargetValue = 1,
        FailurePolicy = policy,
        SkipCondition = alreadySet,
    };

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

    private static FlowStep<FakeState> WaitSeconds(string id, int seconds) => new()
    {
        Id = id,
        Label = id,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,
    };

    private static FlowStep<FakeState> Captain(string id) => new()
    {
        Id = id,
        Label = id,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = $"{id} reminder",
    };

    private static FlowDefinition<FakeState> Flow(params FlowStep<FakeState>[] steps) => new()
    {
        Id = "TEST",
        Name = "Test",
        Steps = steps.ToList(),
    };

    [Fact]
    public void Progress_is_spoken_by_default()
    {
        Assert.True(new UserSettings().FOSpeakFlowProgress);
        Assert.True(new Harness().Flows.SpeakProgress);
    }

    [Fact]
    public async Task Progress_off_silences_the_step_by_step_lines()
    {
        var h = new Harness();
        h.Flows.SpeakProgress = false;

        await h.RunAsync(Flow(
            Switch("A"),
            Switch("B", alreadySet: _ => true),
            WaitFor("C", "ON", timeoutSeconds: 5),
            WaitSeconds("D", 1)));

        // The step still runs; only its words are dropped.
        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.Equal(new[] { "Test flow started", "Test flow complete" }, h.Speech.All);
    }

    [Fact]
    public async Task Progress_off_still_speaks_what_needs_the_pilot()
    {
        var h = new Harness();
        h.Flows.SpeakProgress = false;
        h.Executor.Fails.Add("F");

        await h.RunAsync(Flow(
            Captain("X"),
            Switch("F"),
            WaitFor("T", "OFF", timeoutSeconds: 0)));

        Assert.Equal(new[]
        {
            "Test flow started",
            "Captain action required: X reminder",
            "Skipping: F: ON",
            "Timed out waiting for: T",
            "Skipping: T",
            "Test flow complete",
        }, h.Speech.All);
    }

    [Fact]
    public async Task Progress_off_still_speaks_a_failure_that_stops_the_flow()
    {
        var h = new Harness();
        h.Flows.SpeakProgress = false;
        h.Executor.Fails.Add("S");

        await h.RunAsync(Flow(Switch("S", policy: FlowStepFailurePolicy.Stop)));

        Assert.Equal(new[] { "Test flow started", "Test flow stopped. Unable to complete: S: ON" }, h.Speech.All);
    }

    [Fact]
    public async Task Progress_on_speaks_every_step_line()
    {
        var h = new Harness();

        await h.RunAsync(Flow(
            Switch("A"),
            Switch("B", alreadySet: _ => true),
            WaitFor("C", "ON", timeoutSeconds: 5),
            WaitSeconds("D", 1)));

        Assert.Equal(new[]
        {
            "Test flow started",
            "A: ON",
            "Already set: B: ON",
            "Waiting for: C",
            "Waiting 1 seconds: D",
            "Test flow complete",
        }, h.Speech.All);
    }

    [Fact]
    public async Task Unticking_mid_flow_takes_effect_at_the_next_line()
    {
        var h = new Harness();
        h.Executor.OnWrite = name => { if (name == "A") h.Flows.SpeakProgress = false; };

        await h.RunAsync(Flow(Switch("A"), Switch("B")));

        Assert.Equal(new[] { "A", "B" }, h.Executor.Sent.ToArray());
        Assert.Equal(new[] { "Test flow started", "A: ON", "Test flow complete" }, h.Speech.All);
    }

    [Fact]
    public void The_executor_reads_the_setting_through_the_gate_the_flow_hands_over()
    {
        var h = new Harness();
        Assert.NotNull(h.Executor.ProgressGate);
        Assert.True(h.Executor.ProgressGate!());

        h.Flows.SpeakProgress = false;

        Assert.False(h.Executor.ProgressGate!());
    }

    // The iFly MAX's own wait line ("Waiting for engine 1 bowed rotor motoring to finish.") is a
    // step-by-step line too: its executor keeps the gate its FlowManager hands over.
    [Fact]
    public void The_iFly_executor_follows_its_flows_setting()
    {
        var state = new FakeState();
        var exec = new IFly737ActionExecutor();
        var checklist = new ChecklistManager<IFly737ActionExecutor, FakeState>(
            state, exec, new List<ChecklistGroup<IFly737ActionExecutor, FakeState>>());
        var flows = new FlowManager<IFly737ActionExecutor, FakeState>(state, exec, checklist, new GatedSpeechCapture());
        Assert.True(exec.SpeaksFlowProgress);

        flows.SpeakProgress = false;

        Assert.False(exec.SpeaksFlowProgress);
    }
}
