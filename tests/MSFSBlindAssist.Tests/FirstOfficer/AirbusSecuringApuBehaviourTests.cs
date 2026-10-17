using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// BEHAVIOUR of the Airbus Securing APU wait (owner, 2026-10-14) on a real FlowManager over the REAL
/// Securing steps and the REAL evaluators, the way <see cref="Boeing737SecureApuAlreadyOffTests"/> does
/// for the 737s. The Fenix's fixed 2-minute wait is <c>RunOnlyIfStepSent "SC_APUMASTER"</c>, which is
/// only conditional because SC_APUMASTER skips ("Already set") when the master already reads OFF: with no
/// skip the step would count as "sent" on every Securing and every pilot would wait 2 minutes with the
/// APU never started (Task 2's review finding on the 737s). The FlyByWire jets' wait reads the flap
/// position, so it passes at once on a closed flap and holds external power while the flap is open.
///
/// The flow is cut to the steps under test, under the real flow's id. The executor is a fake that
/// accepts every write (a real one needs the sim); the evaluators are the real ones, reading a seeded
/// cache that counts as connected (FlowManager consults a SkipCondition only on an available evaluator).
/// </summary>
internal static class AirbusSecuringRun
{
    private sealed class FakeExecutor : IFoActionExecutor
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            return Task.FromResult(true);
        }
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    internal sealed record Run(List<string> Speech, List<string> Completed, List<string> Skipped, string[] Sent, bool Finished);

    /// <summary>Runs <paramref name="stepIds"/> of the real Securing flow. When <paramref name="cancelWhenStarted"/>
    /// starts, the run is cancelled (a wait that really waits would otherwise take minutes); otherwise it
    /// must finish by itself within 20 s (a passing run takes about 6 s).</summary>
    internal static async Task<Run> RunAsync<TState>(TState state, FlowDefinition<TState> source, string[] stepIds,
        string? cancelWhenStarted = null) where TState : IFoStateEvaluator
    {
        var flow = new FlowDefinition<TState>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());

        var executor = new FakeExecutor();
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<FakeExecutor, TState>(state, executor, new List<ChecklistGroup<FakeExecutor, TState>>());
        var flows = new FlowManager<FakeExecutor, TState>(state, executor, checklist, speech);
        var completed = new List<string>();
        var skipped = new List<string>();
        flows.StepCompleted += (_, step, _) => { lock (completed) completed.Add(step.Id); };
        flows.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };
        flows.StepStarted += (_, step, _) =>
        {
            if (step.Id == cancelWhenStarted) _ = Task.Delay(300).ContinueWith(_ => flows.Cancel());
        };

        flows.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(20))
            await Task.Delay(20);
        bool finished = !flows.IsRunning;
        flows.Cancel();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(25))
            await Task.Delay(20);

        return new Run(speech.All.ToList(), completed, skipped, executor.Sent.ToArray(), finished);
    }

    internal static bool SpokeAFixedWait(Run run) =>
        run.Speech.Any(s => s.StartsWith("Waiting ", StringComparison.Ordinal) && s.Contains(" seconds: ", StringComparison.Ordinal));
}

/// <summary>The Fenix Securing tail, SC_APUBLEED through SC_BAT2: the 2-minute APU wait follows only an
/// APU master this run switched off.</summary>
public class FenixSecuringApuAlreadyOffTests
{
    private static readonly string[] Tail =
        { "SC_APUBLEED", "SC_APUMASTER", "SC_APU_WAIT", "SC_EXTPWR_OFF", "SC_BAT1", "SC_BAT2" };

    private static FlowDefinition<Fenix.FenixStateEvaluator> Secure()
        => Fenix.FenixFlowDefinitions.Build().Single(f => f.Id == "SECURE");

    /// <summary>A Fenix evaluator reading a seeded cache: APU bleed OFF, the external-power ON light
    /// lit and both batteries ON, with the APU master at <paramref name="apuMaster"/> (0 OFF, 1 ON);
    /// null = the master is unread.</summary>
    private static Fenix.FenixStateEvaluator State(double? apuMaster)
    {
        var values = new List<(string, double)>
        {
            ("S_OH_PNEUMATIC_APU_BLEED", 0), ("I_OH_ELEC_EXT_PWR_L", 1),
            ("S_OH_ELEC_BAT1", 1), ("S_OH_ELEC_BAT2", 1),
        };
        if (apuMaster != null) values.Add(("S_OH_ELEC_APU_MASTER", apuMaster.Value));
        var state = new Fenix.FenixStateEvaluator();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(values.ToArray()));
        return state;
    }

    [Fact]
    public async Task No_wait_when_the_apu_master_already_reads_off()
    {
        var run = await AirbusSecuringRun.RunAsync(State(apuMaster: 0), Secure(), Tail);

        Assert.True(run.Finished, "the run did not finish by itself: the 2-minute wait is running");
        Assert.Contains("Already set: APU master: OFF", run.Speech);
        Assert.False(AirbusSecuringRun.SpokeAFixedWait(run), string.Join(" | ", run.Speech));
        Assert.DoesNotContain("S_OH_ELEC_APU_MASTER", run.Sent);
        // The wait passes as done, not as a skip, and the flow still reaches external power and the batteries, in order.
        Assert.Contains("SC_APU_WAIT", run.Completed);
        Assert.DoesNotContain("SC_APU_WAIT", run.Skipped);
        Assert.Equal(new[] { "S_OH_ELEC_EXT_PWR", "S_OH_ELEC_BAT1", "S_OH_ELEC_BAT2" }, run.Sent);
    }

    [Fact]
    public async Task Wait_starts_after_a_master_off_this_run_sent_and_holds_external_power()
    {
        var run = await AirbusSecuringRun.RunAsync(State(apuMaster: 1), Secure(), Tail, cancelWhenStarted: "SC_APU_WAIT");

        Assert.Contains("S_OH_ELEC_APU_MASTER", run.Sent);
        Assert.DoesNotContain("Already set: APU master: OFF", run.Speech);
        Assert.Contains("Waiting 120 seconds: APU flap to close", run.Speech);
        // External power and the batteries come only after the wait.
        Assert.DoesNotContain("S_OH_ELEC_EXT_PWR", run.Sent);
        Assert.DoesNotContain("S_OH_ELEC_BAT1", run.Sent);
    }

    [Fact]
    public async Task An_unread_apu_master_is_not_already_off()
    {
        var run = await AirbusSecuringRun.RunAsync(State(apuMaster: null), Secure(), Tail, cancelWhenStarted: "SC_APU_WAIT");

        Assert.Contains("S_OH_ELEC_APU_MASTER", run.Sent);
        Assert.Contains("Waiting 120 seconds: APU flap to close", run.Speech);
    }
}

/// <summary>The A32NX Securing tail from the APU master: external power waits for the flap to read
/// closed. The flap wait is unconditional (a field wait, not <c>RunOnlyIfStepSent</c>), so it also
/// passes at once on an APU that never ran.</summary>
public class A32nxSecuringApuFlapTests
{
    private static readonly string[] Tail = { "SC_APUMASTER", "SC_APU_WAIT", "SC_EXTPWR_OFF" };

    private static FlowDefinition<A320.FbwA320StateEvaluator> Secure()
        => A320.FbwA320FlowDefinitions.Build().Single(f => f.Id == "SECURE");

    /// <summary>An A32NX evaluator reading a seeded cache: external power ON, the APU master ON, and the
    /// APU flap at <paramref name="flapPercent"/>; null = the flap is unread.</summary>
    private static A320.FbwA320StateEvaluator State(double? flapPercent)
    {
        var values = new List<(string, double)>
        {
            ("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1), ("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 1),
        };
        if (flapPercent != null) values.Add(("A32NX_APU_FLAP_OPEN_PERCENTAGE", flapPercent.Value));
        var state = new A320.FbwA320StateEvaluator();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(values.ToArray()));
        return state;
    }

    [Fact]
    public async Task A_closed_flap_passes_at_once_and_external_power_follows()
    {
        var run = await AirbusSecuringRun.RunAsync(State(flapPercent: 0), Secure(), Tail);

        Assert.True(run.Finished, "the run did not finish by itself: the flap wait did not pass");
        Assert.Contains("Waiting for: APU flap to close", run.Speech);
        Assert.DoesNotContain("Timed out waiting for: APU flap to close", run.Speech);
        Assert.Contains("SC_APU_WAIT", run.Completed);
        Assert.DoesNotContain("SC_APU_WAIT", run.Skipped);
        Assert.Equal(new[] { "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON" }, run.Sent);
    }

    [Fact]
    public async Task An_open_flap_holds_external_power()
    {
        var run = await AirbusSecuringRun.RunAsync(State(flapPercent: 60), Secure(), Tail, cancelWhenStarted: "SC_APU_WAIT");

        Assert.Contains("Waiting for: APU flap to close", run.Speech);
        Assert.DoesNotContain("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", run.Sent);
    }
}
