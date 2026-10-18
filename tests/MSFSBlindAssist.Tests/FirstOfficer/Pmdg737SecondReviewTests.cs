using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

using Pmdg737Eval = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg737Exec = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor;
using Pmdg737Checklists = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using GeneratorBusState = MSFSBlindAssist.FirstOfficer.PMDG737.GeneratorBusState;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 737 fixes of PR #160's second review, on the real definition, flows, evaluator and
/// executor (no sim). The tests of the definition's process-wide state are in
/// <see cref="Pmdg737SecondReviewStaticStateTests"/>.
/// <list type="bullet">
/// <item>The First Officer's transition-altitude rotation is the Ctrl+B dialog's own (one click
/// count, one guard) and records that the knob reads 29.92 with STD off, so the pilot's QNH entry
/// at the transition level rotates instead of pressing STD, which SELECTED it.</item>
/// <item>Shutdown cuts the start levers only once the APU generator (or ground power) carries both
/// transfer buses, and stops the flow otherwise.</item>
/// <item>Preflight's pressurization reminder is skipped only when the windows READ the plan.</item>
/// <item>Ground power OFF is "Already set" only when read as off, never before the first snapshot.</item>
/// <item>The panel's long test sequences resume on the UI thread.</item>
/// </list>
/// </summary>
public class Pmdg737SecondReviewTests
{
    // =============================================================================================
    // 1. The transition altimeters: one click count, and a 29.92 the app rotated to is no STD
    // =============================================================================================

    [Theory]
    [InlineData(30.12, 29.92, false, -20)]   // the First Officer's standard, knob in inHg
    [InlineData(29.80, 29.92, false, 12)]
    [InlineData(30.12, 29.92, true, -7)]     // knob in hPa: 1020 to 1013
    [InlineData(29.92, 30.05, false, 13)]    // the pilot's QNH at the transition level, typed in inHg
    [InlineData(29.92, 1018, true, 5)]       // typed in hPa, knob in hPa: 1013 to 1018
    [InlineData(29.92, 1018, false, 14)]     // typed in hPa, knob in inHg: 29.92 to 30.06
    public void Ctrl_B_and_the_First_Officer_count_the_same_clicks(double currentInHg, double typed, bool hpaMode, int clicks)
        => Assert.Equal(clicks, PMDG737Definition.BaroKnobClicks(currentInHg, typed, hpaMode));

    [Theory]
    [InlineData(29.92, false, true)]     // a 29.92 no rotation of the app's put there: STD selected, as before
    [InlineData(29.924, false, true)]
    [InlineData(29.92, true, false)]     // the app rotated the knob there: a QNH of 29.92 with STD off
    [InlineData(29.914, false, false)]   // 1013 hPa, where a knob in hPa lands: never taken for STD
    [InlineData(30.05, false, false)]
    public void The_dialog_reads_29_92_as_STD_unless_the_app_rotated_the_knob_there(double currentInHg, bool rotated, bool readsAsStd)
        => Assert.Equal(readsAsStd, PMDG737Definition.BaroReadsAsStd(currentInHg, rotated));

    /// <summary>The finding: after the First Officer's rotation to 29.92, the pilot's Ctrl+B QNH
    /// pressed STD "to deselect it", which selected it, and the QNH went in behind the STD mask.</summary>
    [Fact]
    public void After_the_rotation_a_QNH_entry_rotates_and_never_presses_STD()
    {
        Assert.False(PMDG737Definition.BaroSetPressesStdFirst(29.92, 30.05, rotatedToStdByApp: true));
        Assert.False(PMDG737Definition.BaroSetPressesStdFirst(29.92, 1018, rotatedToStdByApp: true));
        // A 29.92 the app did not rotate to keeps the old STD lockout fix.
        Assert.True(PMDG737Definition.BaroSetPressesStdFirst(29.92, 30.05, rotatedToStdByApp: false));
        // Standard typed over standard: nothing to deselect.
        Assert.False(PMDG737Definition.BaroSetPressesStdFirst(29.92, 29.92, rotatedToStdByApp: false));
    }

    // =============================================================================================
    // 2. Shutdown: power for the levers
    // =============================================================================================

    [Theory]
    // GEN 1 / GEN 2 OFF BUS lights, transfer buses 1 / 2, APU GEN OFF BUS light → load off the engines, ready
    [InlineData(0, 0, 1, 1, 0, false, false)]   // the engines carry both buses, the APU generator is not up
    [InlineData(0, 0, 1, 1, 1, false, true)]    // ... and the APU generator is up
    [InlineData(1, 1, 1, 1, 0, true, true)]     // the APU generator or ground power carries both: a completed transfer
    [InlineData(1, 0, 1, 1, 0, false, true)]    // half a transfer: bus 1 carried without generator 1
    [InlineData(1, 1, 0, 0, 0, false, false)]   // the engines stopped, on battery: nothing carries the buses
    [InlineData(0, 0, 0, 0, 0, false, false)]   // unpowered: every light dark
    public void Shutdown_power_reads_the_generator_lights_and_the_transfer_buses(
        double gen1Light, double gen2Light, double bus1, double bus2, double apuLight, bool loadOff, bool ready)
    {
        var fields = new Dictionary<string, double>
        {
            ["ELEC_annunGEN_BUS_OFF_0"] = gen1Light, ["ELEC_annunGEN_BUS_OFF_1"] = gen2Light,
            ["ELEC_BusPowered_7"] = bus1, ["ELEC_BusPowered_8"] = bus2,
            ["ELEC_annunAPU_GEN_OFF_BUS"] = apuLight,
        };
        Func<string, double> read = f => fields.TryGetValue(f, out var v) ? v : double.NaN;
        Assert.Equal(loadOff, GeneratorBusState.IsLoadOffEngines(read));
        Assert.Equal(ready, GeneratorBusState.IsShutdownPowerReady(read));
    }

    [Fact]
    public void Shutdown_power_is_unknown_until_the_first_snapshot()
    {
        Func<string, double> unread = _ => double.NaN;
        Assert.False(GeneratorBusState.IsLoadOffEngines(unread));
        Assert.False(GeneratorBusState.IsShutdownPowerReady(unread));

        var state = Pmdg737Sim.State(snapshot: null);
        Assert.True(double.IsNaN(state.GetValue(GeneratorBusState.LoadOffEnginesField)));
        Assert.True(double.IsNaN(state.GetValue(GeneratorBusState.ShutdownPowerReadyField)));
        Assert.Equal(1, Pmdg737Sim.State(Pmdg737Sim.LoadOffEngines()).GetValue(GeneratorBusState.LoadOffEnginesField));
        Assert.Equal(1, Pmdg737Sim.State(Pmdg737Sim.EnginesOnBuses(apuGenUp: true)).GetValue(GeneratorBusState.ShutdownPowerReadyField));
    }

    [Fact]
    public void Shutdown_waits_for_power_before_the_transfer_and_verifies_it_before_the_levers()
    {
        var steps = Pmdg737Flows.Build().Single(f => f.Id == "SHUTDOWN").Steps;
        int wait = steps.FindIndex(s => s.Id == "SD_POWER_WAIT");
        int transfer = steps.FindIndex(s => s.Id == "SD_APUGEN");
        int levers = steps.FindIndex(s => s.Id == "SD_LEVERS");
        Assert.True(wait == 0 && transfer == 1 && levers == 2, $"order {wait}, {transfer}, {levers}");

        var w = steps[wait];
        Assert.Equal(FlowStepActionType.WaitForCondition, w.ActionType);
        Assert.Equal(GeneratorBusState.ShutdownPowerReadyField, w.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, w.FailurePolicy);
        // Before Start's measured budget for the APU generator (Pmdg737ApuGeneratorGateTests).
        Assert.True(w.TimeoutSeconds >= 120, $"power wait is {w.TimeoutSeconds} s");
        Assert.Empty(w.LinkedChecklistItemIds);

        var t = steps[transfer];
        Assert.Equal(GeneratorBusState.LoadOffEnginesField, t.VerifyFieldName);
        Assert.True(t.VerifyCondition!(1));
        Assert.False(t.VerifyCondition!(0));
        Assert.False(t.VerifyCondition!(double.NaN));
        Assert.Equal(FlowStepFailurePolicy.Stop, t.FailurePolicy);
        Assert.Equal(new[] { "SD_APUGEN" }, t.LinkedChecklistItemIds.ToArray());
    }

    private sealed class RecordingExecutor : IFoActionExecutor
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public Action<IFlowStepDispatch>? OnStep { get; init; }
        public bool IsAvailable => true;

        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            var events = step.ActionType == FlowStepActionType.SetSwitchMultiple
                ? step.MultiActions.Select(a => a.EventName)
                : new[] { step.EventName ?? "" };
            foreach (var ev in events) Sent.Enqueue(ev);
            OnStep?.Invoke(step);
            return Task.FromResult(true);
        }

        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed record ShutdownRun(List<string> Speech, string[] Sent, bool Completed, bool Failed);

    private static readonly string[] PowerSteps = { "SD_POWER_WAIT", "SD_APUGEN", "SD_LEVERS" };

    // The real Shutdown steps from the power wait to the levers, under the real flow's id, on a real
    // FlowManager and the real evaluator. powerWaitSeconds shortens the 120 s wait for the run where
    // no power ever comes (its real budget is pinned above).
    private static async Task<ShutdownRun> RunShutdownAsync(Pmdg737Eval state, RecordingExecutor executor,
        int? powerWaitSeconds = null)
    {
        var source = Pmdg737Flows.Build().Single(f => f.Id == "SHUTDOWN");
        var flow = new FlowDefinition<Pmdg737Eval>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => PowerSteps.Contains(s.Id)).ToList(),
        };
        Assert.Equal(PowerSteps, flow.Steps.Select(s => s.Id).ToArray());
        if (powerWaitSeconds is int seconds) flow.Steps[0].TimeoutSeconds = seconds;

        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<RecordingExecutor, Pmdg737Eval>(state, executor,
            new List<ChecklistGroup<RecordingExecutor, Pmdg737Eval>>());
        var flows = new FlowManager<RecordingExecutor, Pmdg737Eval>(state, executor, checklist, speech);
        bool completed = false, failed = false;
        flows.FlowCompleted += _ => completed = true;
        flows.FlowFailed += (_, _) => failed = true;

        flows.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flows.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "flow did not finish");
            await Task.Delay(20);
        }
        return new ShutdownRun(speech.All.ToList(), executor.Sent.ToArray(), completed, failed);
    }

    private static readonly string[] ApuGenEvents = { "EVT_OH_ELEC_APU_GEN1_SWITCH", "EVT_OH_ELEC_APU_GEN2_SWITCH" };
    private static readonly string[] LeverEvents = { "EVT_CONTROL_STAND_ENG1_START_LEVER", "EVT_CONTROL_STAND_ENG2_START_LEVER" };

    /// <summary>The finding: After Landing, then Shutdown before the APU generator is up. The flow
    /// pressed the APU generators to no effect and cut the levers onto battery.</summary>
    [Fact]
    public async Task No_power_source_stops_the_flow_before_the_levers()
    {
        var run = await RunShutdownAsync(Pmdg737Sim.State(Pmdg737Sim.EnginesOnBuses(apuGenUp: false)),
            new RecordingExecutor(), powerWaitSeconds: 2);

        Assert.Empty(run.Sent);
        Assert.True(run.Failed);
        Assert.False(run.Completed);
        Assert.Contains("Shutdown flow stopped. Unable to complete: APU generator or ground power", run.Speech);
        Assert.DoesNotContain("Shutdown flow complete", run.Speech);
    }

    [Fact]
    public async Task A_transfer_that_does_not_take_stops_the_flow_before_the_levers()
    {
        // The APU generator is up, but the buses never move off the engines.
        var run = await RunShutdownAsync(Pmdg737Sim.State(Pmdg737Sim.EnginesOnBuses(apuGenUp: true)),
            new RecordingExecutor());

        Assert.Equal(ApuGenEvents, run.Sent);
        Assert.True(run.Failed);
        Assert.Contains("Shutdown flow stopped. Unable to complete: APU generators: ON", run.Speech);
    }

    [Fact]
    public async Task A_transfer_that_takes_cuts_the_levers()
    {
        var state = Pmdg737Sim.State(Pmdg737Sim.EnginesOnBuses(apuGenUp: true));
        // The APU generators take both buses when their buttons are pressed.
        var executor = new RecordingExecutor
        {
            OnStep = step =>
            {
                if (step.MultiActions.Any(a => a.EventName == "EVT_OH_ELEC_APU_GEN2_SWITCH"))
                    state.SetDataManager(Pmdg737Sim.Ng3(Pmdg737Sim.LoadOffEngines()));
            },
        };
        var run = await RunShutdownAsync(state, executor);

        Assert.Equal(ApuGenEvents.Concat(LeverEvents), run.Sent);
        Assert.True(run.Completed);
        Assert.Contains("APU generators: ON", run.Speech);
        Assert.Contains("Engine start levers: CUTOFF", run.Speech);
    }

    [Fact]
    public async Task Ground_power_on_the_buses_is_the_source_and_nothing_is_pressed_over_it()
    {
        var run = await RunShutdownAsync(Pmdg737Sim.State(Pmdg737Sim.LoadOffEngines(gpuAvailable: true)),
            new RecordingExecutor());

        Assert.Equal(LeverEvents, run.Sent);
        Assert.True(run.Completed);
        Assert.Contains("Already set: APU generators: ON", run.Speech);
    }

    // =============================================================================================
    // 3. Preflight: the pressurization reminder is keyed on the windows, never on the plan
    // =============================================================================================

    private static readonly string[] PressSteps = { "PF_FLT_ALT", "PF_LAND_ALT", "PF_PRESS" };
    private const string PressReminder = "Captain action required: Set flight and landing altitudes on the pressurization panel.";

    // The real Preflight pressurization steps on a real FlowManager, a LIVE evaluator (a data manager
    // with a snapshot, so FlowManager consults each SkipCondition) and the real executor with no sim,
    // so every write that is attempted fails (Pmdg737PressurizationNoPlanTests runs these unwired).
    private static async Task<List<string>> RunPressurizationAsync(Pmdg737Eval state)
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

        var executor = new Pmdg737Exec();
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<Pmdg737Exec, Pmdg737Eval>(state, executor, Pmdg737Checklists.Build());
        var flows = new FlowManager<Pmdg737Exec, Pmdg737Eval>(state, executor, checklist, speech);
        flows.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flows.IsRunning)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
            await Task.Delay(20);
        }
        return speech.All.ToList();
    }

    private static Pmdg737Eval Windows(uint fltAlt, uint landAlt, int? plannedFlt, int? plannedLand)
    {
        var state = Pmdg737Sim.State(new PMDGNG3DataStruct { AIR_FltAltWindow = fltAlt, AIR_LandAltWindow = landAlt });
        state.SetPlannedPressurizationAltitudes(plannedFlt, plannedLand);
        return state;
    }

    /// <summary>The finding: a plan, writes that did not take, and the reminder skipped as
    /// "Already set" because a plan existed.</summary>
    [Fact]
    public async Task Writes_that_did_not_take_leave_the_reminder_spoken()
    {
        var speech = await RunPressurizationAsync(Windows(0, 0, 35000, 450));

        Assert.Contains("Skipping: Flight altitude: set", speech);
        Assert.Contains("Skipping: Landing altitude: set", speech);
        Assert.Contains(PressReminder, speech);
        Assert.DoesNotContain("Already set: Flight and landing altitudes", speech);
    }

    [Fact]
    public async Task Windows_reading_the_plan_skip_the_reminder()
    {
        var speech = await RunPressurizationAsync(Windows(35000, 450, 35000, 450));

        Assert.Contains("Already set: Flight altitude: set", speech);
        Assert.Contains("Already set: Landing altitude: set", speech);
        Assert.Contains("Already set: Flight and landing altitudes", speech);
        Assert.DoesNotContain(PressReminder, speech);
    }

    [Theory]
    [InlineData(null, null)]     // no plan
    [InlineData(35000, null)]    // a plan without the landing altitude: that window is nobody's
    public async Task Without_both_planned_values_the_reminder_speaks(int? plannedFlt, int? plannedLand)
    {
        var speech = await RunPressurizationAsync(Windows(35000, 0, plannedFlt, plannedLand));

        Assert.Contains(PressReminder, speech);
        Assert.DoesNotContain("Already set: Flight and landing altitudes", speech);
    }

    // =============================================================================================
    // 6. Ground power OFF is "Already set" only when READ as off
    // =============================================================================================

    [Fact]
    public void Ground_power_reads_off_only_once_read()
    {
        var unread = Pmdg737Sim.State(snapshot: null);
        Assert.False(unread.IsGpuOn());
        Assert.False(unread.IsGpuOff());

        var none = Pmdg737Sim.State(Pmdg737Sim.LoadOffEngines(gpuAvailable: false));
        Assert.True(none.IsGpuOff());
        Assert.False(none.IsGpuOn());

        var connected = Pmdg737Sim.State(Pmdg737Sim.LoadOffEngines(gpuAvailable: true));
        Assert.True(connected.IsGpuOn());
        Assert.False(connected.IsGpuOff());
    }

    [Theory]
    [InlineData("BEFORE_START", "BS_GPU_OFF")]
    [InlineData("SECURE", "SE_GND_PWR_OFF")]
    public void Ground_power_off_is_never_already_set_before_the_first_snapshot(string flowId, string stepId)
    {
        var step = Pmdg737Flows.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

        Assert.False(step.SkipCondition!(Pmdg737Sim.State(snapshot: null)));                                 // unread: sent
        Assert.True(step.SkipCondition!(Pmdg737Sim.State(Pmdg737Sim.LoadOffEngines(gpuAvailable: false))));  // read off: Already set
        Assert.False(step.SkipCondition!(Pmdg737Sim.State(Pmdg737Sim.LoadOffEngines(gpuAvailable: true))));  // on the buses: sent
    }

    // =============================================================================================
    // 7. The panel's long test sequences resume on the UI thread
    // =============================================================================================

    [Fact]
    public void The_panel_test_sequences_resume_on_the_UI_thread()
    {
        // A continuation after ConfigureAwait(false) sent the presses and releases from a pool
        // thread while the UI thread pumps SimConnect's ReceiveMessage.
        string source = File.ReadAllText(DefinitionSourcePath());
        foreach (var signature in new[]
                 {
                     "private static async Task HeldTransmitAsync(",
                     "private static async Task WxrTestSequenceAsync(",
                 })
            Assert.DoesNotContain("ConfigureAwait(false)", MethodBody(source, signature));
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidOperationException($"{signature} has no closing brace");
    }

    private static string DefinitionSourcePath([CallerFilePath] string thisTestFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "..", "MSFSBlindAssist", "Aircraft", "PMDG737Definition.cs"));
}

/// <summary>
/// Run apart from every other test: these tests drive PMDG737Definition's process-wide state, the
/// baro knob rotation (one op at a time, and its "rotated to standard" record) and the register of
/// held test presses, which other tests' definitions reach too (OnSwitchedAway, OnSimContextReset).
/// </summary>
[CollectionDefinition("Pmdg737DefinitionStaticState", DisableParallelization = true)]
public class Pmdg737DefinitionStaticStateCollection { }

/// <summary>
/// The second-review fixes that live in PMDG737Definition's process-wide state: the baro knob's one
/// guard and its "rotated to standard" record (finding 1), the register of held test presses the
/// app-exit and aircraft-switch release empties (finding 4), the executor refusing a press once
/// detached (finding 5), and the panel's long tests one run at a time (finding 7).
/// </summary>
[Collection("Pmdg737DefinitionStaticState")]
public class Pmdg737SecondReviewStaticStateTests
{
    // No production seam is added for tests (see SeededSimConnectCache): the definition's private
    // statics are reached through UnsafeAccessor.

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "s_baroRotatedToStd")]
    private static extern ref bool BaroRotatedToStd(PMDG737Definition? owner);

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "s_baroOpEndedUtc")]
    private static extern ref DateTime BaroOpEndedUtc(PMDG737Definition? owner);

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "s_heldPresses")]
    private static extern ref HashSet<PMDG737Definition.HeldPress> HeldPresses(PMDG737Definition? owner);

    private static int HeldCount() => HeldPresses(null).Count;

    // MainForm's app-exit and aircraft-switch release, which also empties the register of held
    // presses, so each test starts with none.
    private static void ReleaseHeld(SimConnectManager? mgr = null) =>
        new PMDG737Definition().ReleaseEngagedWarningTests(mgr ?? Pmdg737Sim.NeverConnected());

    private static Task BaroIdle() => Pmdg737Sim.WaitUntil(() => !PMDG737Definition.BaroKnobBusy);

    // =============================================================================================
    // 1. The baro knob: one guard, and the "rotated to standard" record
    // =============================================================================================

    /// <summary>
    /// The First Officer's rotation runs as the dialog's one guarded knob op, so a Ctrl+B entry or a
    /// Standard press meanwhile is refused (the double-step half of the finding). The knob clicks
    /// themselves need the sim: a test cannot send through a PMDG data manager (it loads the
    /// SimConnect SDK), so with none attached nothing is clicked and nothing is recorded. That the
    /// rotation then marks the knob rotated to standard is the in-sim test plan's (AA.2 item 1).
    /// </summary>
    [Fact]
    public async Task The_First_Officers_rotation_holds_the_dialogs_guard()
    {
        await BaroIdle();
        new PMDG737Definition().OnSimContextReset();

        var exec = new Pmdg737Exec();
        exec.SetSimConnect(SeededSimConnectCache.With(("ALTIMETER_SETTING", 30.12)));
        var rotation = exec.SetAltimetersStandardAsync();
        Assert.True(PMDG737Definition.BaroKnobBusy);
        await rotation;

        Assert.False(PMDG737Definition.BaroKnobBusy);
        Assert.False(BaroRotatedToStd(null));   // no 737 loaded: no knob was clicked, so none is on standard
    }

    [Fact]
    public async Task The_record_holds_until_the_knob_is_known_to_have_left_standard()
    {
        await BaroIdle();
        var def = new PMDG737Definition();
        var speech = new SpeechCapture();

        // A reading away from standard just after the rotation ended is the rotation's own.
        BaroRotatedToStd(null) = true;
        BaroOpEndedUtc(null) = DateTime.UtcNow;
        def.ProcessSimVarUpdate("ALTIMETER_SETTING", 30.00, speech);
        Assert.True(BaroRotatedToStd(null));

        // A standard reading keeps it.
        BaroOpEndedUtc(null) = DateTime.UtcNow.AddSeconds(-10);
        def.ProcessSimVarUpdate("ALTIMETER_SETTING", 29.92, speech);
        Assert.True(BaroRotatedToStd(null));

        // Once the rotation has settled, a reading away from standard is the knob moved in the cockpit.
        def.ProcessSimVarUpdate("ALTIMETER_SETTING", 30.00, speech);
        Assert.False(BaroRotatedToStd(null));

        // A new flight or a lost connection, and another aircraft, forget it too.
        BaroRotatedToStd(null) = true;
        def.OnSimContextReset();
        Assert.False(BaroRotatedToStd(null));
        BaroRotatedToStd(null) = true;
        new PMDG737Definition().OnSwitchedAway();
        Assert.False(BaroRotatedToStd(null));
    }

    // =============================================================================================
    // 4 and 5. Held tests: released on exit and switch; nothing pressed by a detached executor
    // =============================================================================================

    [Fact]
    public void A_hold_is_released_once_by_whoever_ends_it_first()
    {
        ReleaseHeld();
        var mgr = Pmdg737Sim.NeverConnected();
        int released = 0;

        // The exit or switch release first: the hold's own timer then owes nothing.
        var hold = PMDG737Definition.BeginHeldPress(sc => { Assert.Same(mgr, sc); released++; });
        ReleaseHeld(mgr);
        Assert.Equal(1, released);
        Assert.False(PMDG737Definition.EndHeldPress(hold));

        // Its own timer first: the exit or switch release then sends nothing for it.
        var own = PMDG737Definition.BeginHeldPress(_ => released++);
        Assert.True(PMDG737Definition.EndHeldPress(own));
        ReleaseHeld(mgr);
        Assert.Equal(1, released);
    }

    // Short holds for the warning tests: what matters is that each kind of hold is registered.
    private static Task<bool> StartHeldTest(Pmdg737Exec exec, string test) => test switch
    {
        "fire" => exec.FireDetectionTestAsync(),
        "stall" => exec.WarningTestAsync("EVT_OH_WARNING_TEST_STALL_1_PUSH", 600),
        _ => exec.WarningTestAsync("EVT_OH_WARNING_TEST_MACH_IAS_1_PUSH", 600),
    };

    /// <summary>The finding: quitting MSFSBA or switching aircraft inside a First Officer hold left
    /// the fire bell, stick shaker or clacker sounding, because the exit and switch release only knew
    /// the panel's toggles.</summary>
    [Theory]
    [InlineData("fire")]
    [InlineData("stall")]
    [InlineData("overspeed")]
    public async Task The_exit_and_switch_release_covers_a_test_the_First_Officer_is_holding(string test)
    {
        ReleaseHeld();
        var mgr = Pmdg737Sim.NeverConnected();
        var exec = new Pmdg737Exec();
        exec.SetSimConnect(mgr);

        var run = StartHeldTest(exec, test);
        await Pmdg737Sim.WaitUntil(() => HeldCount() == 1);
        ReleaseHeld(mgr);                 // MainForm's release, on app exit or an aircraft switch
        Assert.Equal(0, HeldCount());
        Assert.True(await run);
        Assert.Equal(0, HeldCount());
    }

    [Fact]
    public async Task A_press_already_sent_is_released_after_the_window_closes()
    {
        ReleaseHeld();
        var exec = new Pmdg737Exec();
        exec.SetSimConnect(Pmdg737Sim.NeverConnected());

        var run = exec.WarningTestAsync("EVT_OH_WARNING_TEST_STALL_1_PUSH", 600);
        await Pmdg737Sim.WaitUntil(() => HeldCount() == 1);
        exec.SetSimConnect(null);         // the First Officer window closed mid-hold
        Assert.True(await run);
        Assert.Equal(0, HeldCount());     // ended by its own release, on the SimConnect it pressed through
    }

    /// <summary>The finding: each held test read the executor's SimConnect BEFORE waiting for the
    /// dispatch gate, so a test queued behind another was pressed after the window closed (an
    /// aircraft switch), into the next aircraft's data manager.</summary>
    [Fact]
    public async Task Tests_queued_behind_the_gate_are_refused_once_the_executor_is_detached()
    {
        ReleaseHeld();
        var exec = new Pmdg737Exec();
        exec.SetSimConnect(Pmdg737Sim.NeverConnected());

        var first = exec.WarningTestAsync("EVT_OH_WARNING_TEST_STALL_1_PUSH", 600);   // holds the gate
        await Pmdg737Sim.WaitUntil(() => HeldCount() == 1);
        var queuedFire = exec.FireDetectionTestAsync();
        var queuedWarning = exec.WarningTestAsync("EVT_OH_WARNING_TEST_MACH_IAS_1_PUSH", 600);
        exec.SetSimConnect(null);

        Assert.True(await first);
        Assert.False(await queuedFire);
        Assert.False(await queuedWarning);
        Assert.Equal(0, HeldCount());
    }

    // =============================================================================================
    // 7. The panel's long system tests: one run at a time
    // =============================================================================================

    [Theory]
    [InlineData("WXR_Test", "Still running the weather radar test, please wait.")]
    [InlineData("GPWS_SysTestLong", "Still running the GPWS test, please wait.")]
    public void A_second_press_while_the_panel_test_runs_is_refused_aloud(string key, string refusal)
    {
        var def = new PMDG737Definition();
        var mgr = Pmdg737Sim.NeverConnected();
        var speech = new SpeechCapture();
        var varDef = def.GetVariables()[key];

        Assert.True(def.HandleUIVariableSet(key, 1, varDef, mgr, speech));
        Assert.Empty(speech.All);           // the test's own audio in the aircraft is the confirmation
        Assert.True(def.HandleUIVariableSet(key, 1, varDef, mgr, speech));
        Assert.Equal(new[] { refusal }, speech.All);

        ReleaseHeld(mgr);                   // no hold of this test outlives it in the shared register
    }
}

/// <summary>A 737 with no sim: NG3 snapshots, evaluators over them, and a SimConnectManager that
/// was never connected (anything sent through it goes nowhere).</summary>
internal static class Pmdg737Sim
{
    public static PMDGNG3DataManager Ng3(PMDGNG3DataStruct snapshot)
    {
        // The NG3 manager takes a snapshot only from a SimConnect client-data callback, which a test
        // cannot supply (Boeing737SecureApuAlreadyOffTests seeds it the same way).
        var dm = new PMDGNG3DataManager();
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(PMDGNG3DataManager).GetField("_lastDataSnapshot", Private)!.SetValue(dm, snapshot);
        typeof(PMDGNG3DataManager).GetField("_hasSnapshot", Private)!.SetValue(dm, true);
        return dm;
    }

    /// <summary>A 737 evaluator over a data manager holding <paramref name="snapshot"/>; null = a
    /// data manager whose first snapshot has not arrived (every CDA field unread).</summary>
    public static Pmdg737Eval State(PMDGNG3DataStruct? snapshot)
    {
        var state = new Pmdg737Eval();
        state.SetDataManager(snapshot is { } s ? Ng3(s) : new PMDGNG3DataManager());
        return state;
    }

    /// <summary>The electrical lights and buses the Shutdown power check and the ground-power
    /// steps read. The ground service buses follow the transfer buses, as on the NG3.</summary>
    public static PMDGNG3DataStruct Elec(bool gen1OffBusLit, bool gen2OffBusLit, bool xfr1, bool xfr2,
        bool apuGenOffBusLit, bool gpuAvailable = false)
    {
        var buses = new bool[16];
        buses[7] = xfr1;
        buses[8] = xfr2;
        buses[9] = buses[10] = xfr1 || xfr2;
        return new PMDGNG3DataStruct
        {
            ELEC_annunGEN_BUS_OFF = new[] { gen1OffBusLit, gen2OffBusLit },
            ELEC_BusPowered = buses,
            ELEC_annunAPU_GEN_OFF_BUS = apuGenOffBusLit,
            ELEC_annunGRD_POWER_AVAILABLE = gpuAvailable,
        };
    }

    /// <summary>The engines carry both transfer buses; the APU generator is up (its OFF BUS light lit) or not.</summary>
    public static PMDGNG3DataStruct EnginesOnBuses(bool apuGenUp) => Elec(false, false, true, true, apuGenUp);

    /// <summary>Neither engine generator on a bus, both buses powered: the APU generator or ground power carries them.</summary>
    public static PMDGNG3DataStruct LoadOffEngines(bool gpuAvailable = false) => Elec(true, true, true, true, false, gpuAvailable);

    public static SimConnectManager NeverConnected() => new(IntPtr.Zero);

    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < timeoutMs, "condition not reached in time");
            await Task.Delay(10);
        }
    }
}
