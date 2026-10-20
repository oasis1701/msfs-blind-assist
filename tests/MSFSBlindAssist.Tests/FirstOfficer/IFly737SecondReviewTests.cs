// The second review of PR #160 (2026-10-18) on the iFly 737 MAX8 First Officer. Pure logic and
// structure here; the behaviour on a real FlowManager (no sim) in IFly737SecondReviewFlowTests and
// IFly737ShutdownPowerFlowTests below, split so the runs, each a few seconds of FlowManager's 2 s
// step pacing, go in parallel.
//
//   1. An autopilot is engaged when CMD A, CMD B, CWS A or CWS B is lit (was CMD only: the universal
//      auto-AP pressed CMD A over a CWS autopilot the pilot engaged).
//   2. The gear lever goes UP only when the aircraft is definitely airborne (SIM_ON_GROUND 0).
//   3. Shutdown waits for the APU generator or ground power before the transfer and the cut, and
//      never cuts the levers while the engine generators still carry the buses.
//   4. The Preflight pressurization reminder is skipped only when the windows read the plan.
//   5. The fire test's release survives the window's detach (and the definition's Shutdown).
//   6. The Engine Start waits are aware of the LEAP-1B's bowed rotor motoring.
//   7. Ctrl+B and the First Officer's altimeters to standard share one Kohlsman computation.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect.IFly;
using MSFSBlindAssist.Utils;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class IFly737SecondReviewTests
{
    internal static byte[] Sdk() => new byte[IFlySdkOffsets.StructSize];

    /// <summary>An evaluator as in the sim: a definition attached (FlowManager and ChecklistManager
    /// consult SkipCondition and LeaveAloneWhen only then), an SDK snapshot (null = not ready) and
    /// the SimConnect cache holding <paramref name="simConnect"/> (a key not given reads unread).</summary>
    internal static IFly737StateEvaluator State(byte[]? sdk, params (string Key, double Value)[] simConnect)
    {
        var snapshot = sdk == null ? null : new IFlySdkSnapshot(sdk);
        var state = new IFly737StateEvaluator
        {
            SnapshotSource = () => snapshot,
            ReadySource = () => snapshot != null,
        };
        state.SetDefinition(new IFly737MAXDefinition());
        state.SetSimConnect(SeededSimConnectCache.With(simConnect));
        return state;
    }

    private static FlowStep<IFly737StateEvaluator> FlowStep(string flowId, string stepId) =>
        IFly737FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    // ---- 1. CWS counts as an engaged autopilot ------------------------------------------------

    [Theory]
    [InlineData(IFlySdkOffsets.CWS_A_Switch_Status, 1)]   // released, light DIM
    [InlineData(IFlySdkOffsets.CWS_A_Switch_Status, 2)]   // released, light BRT
    [InlineData(IFlySdkOffsets.CWS_B_Switch_Status, 4)]   // pressed, light DIM
    [InlineData(IFlySdkOffsets.CWS_B_Switch_Status, 5)]   // pressed, light BRT
    [InlineData(IFlySdkOffsets.CMD_A_Switch_Status, 2)]
    [InlineData(IFlySdkOffsets.CMD_B_Switch_Status, 1)]
    public void Any_lit_autopilot_engage_switch_reads_engaged(int offset, int value)
    {
        var b = Sdk();
        b[offset] = (byte)value;
        Assert.True(IFly737MAXDefinition.AutopilotEngagedIn(new IFlySdkSnapshot(b)));
    }

    [Fact]
    public void No_lit_engage_switch_reads_not_engaged_and_no_snapshot_reads_unread()
    {
        var b = Sdk();
        // Switches pressed with their lights out (3) are not an engaged autopilot.
        b[IFlySdkOffsets.CMD_A_Switch_Status] = 3;
        b[IFlySdkOffsets.CWS_A_Switch_Status] = 3;
        Assert.False(IFly737MAXDefinition.AutopilotEngagedIn(new IFlySdkSnapshot(b)));
        // Unread is null, never false (FO-18): the universal service waits for a reading.
        Assert.Null(IFly737MAXDefinition.AutopilotEngagedIn(null));
        Assert.Equal(4, IFly737MAXDefinition.AutopilotEngageOffsets.Distinct().Count());
    }

    // ---- 2. Gear UP only when definitely airborne ---------------------------------------------

    [Fact]
    public void Air_ground_reads_the_SimConnect_cache_and_unread_is_not_airborne()
    {
        Assert.True(State(Sdk(), ("SIM_ON_GROUND", 1)).OnGround);
        Assert.False(State(Sdk(), ("SIM_ON_GROUND", 1)).IsDefinitelyAirborne);
        Assert.False(State(Sdk(), ("SIM_ON_GROUND", 0)).OnGround);
        Assert.True(State(Sdk(), ("SIM_ON_GROUND", 0)).IsDefinitelyAirborne);

        var unread = State(Sdk());
        Assert.Null(unread.OnGround);
        Assert.False(unread.IsDefinitelyAirborne);

        // Disconnected (no SimConnect): unread.
        var disconnected = State(Sdk(), ("SIM_ON_GROUND", 0));
        disconnected.SetSimConnect(null);
        Assert.Null(disconnected.OnGround);
    }

    [Fact]
    public void The_gear_step_and_the_gear_line_leave_the_lever_down_unless_airborne()
    {
        var step = FlowStep("AFTER_TAKEOFF", "AT_GEAR_OFF");
        var line = IFly737ChecklistDefinitions.Build().Single(g => g.Id == "AFTER_TAKEOFF")
            .Items.Single(i => i.Id == "ATKO_GEAR_OFF");

        Assert.Equal(IFly737ActionExecutor.GearUpLeaveAloneText, step.LeaveAloneText);
        Assert.Equal(IFly737ActionExecutor.GearUpLeaveAloneText, line.LeaveAloneText);
        foreach (var (state, leftAlone) in new[]
        {
            (State(Sdk(), ("SIM_ON_GROUND", 1)), true),
            (State(Sdk()), true),                           // unread
            (State(Sdk(), ("SIM_ON_GROUND", 0)), false),
        })
        {
            Assert.Equal(leftAlone, step.LeaveAloneWhen!(state));
            Assert.Equal(leftAlone, line.LeaveAloneWhen!(state));
        }
    }

    [Theory]
    [InlineData(IFly737ActionExecutor.GearUp, true, true)]      // UP on the ground
    [InlineData(IFly737ActionExecutor.GearUp, null, true)]      // UP, air/ground unread
    [InlineData(IFly737ActionExecutor.GearUp, false, false)]    // UP in the air
    [InlineData(IFly737ActionExecutor.GearDown, true, false)]   // DOWN is never refused
    public void The_executor_refuses_gear_up_unless_airborne(int target, bool? onGround, bool refused)
    {
        Assert.Equal(refused, IFly737ActionExecutor.RefusesGearUp("Gear_Lever_Status", target, onGround));
        // A null target writes DOWN (1), and no other key is touched.
        Assert.False(IFly737ActionExecutor.RefusesGearUp("Gear_Lever_Status", null, onGround));
        Assert.False(IFly737ActionExecutor.RefusesGearUp("Autobrake_Selector_Status", target, onGround));
    }

    [Fact]
    public void A_hand_tick_of_gear_up_on_the_ground_is_refused_with_its_reason()
    {
        var groups = IFly737ChecklistDefinitions.Build();
        var checklist = new ChecklistManager<IFly737ActionExecutor, IFly737StateEvaluator>(
            State(Sdk(), ("SIM_ON_GROUND", 1)), new IFly737ActionExecutor(), groups);

        Assert.False(checklist.ToggleItem("AFTER_TAKEOFF", "ATKO_GEAR_OFF", out string? text));
        Assert.Equal(IFly737ActionExecutor.GearUpLeaveAloneText, text);
        Assert.False(groups.Single(g => g.Id == "AFTER_TAKEOFF").Items.Single(i => i.Id == "ATKO_GEAR_OFF").IsChecked);
    }

    // ---- 3. Shutdown: something must carry the buses before the cut --------------------------

    // The overhead as the SDK reads it: the APU GEN OFF BUS light, both GEN OFF BUS lights, both
    // TRANSFER BUS OFF lights and both start levers (0-5 composite: 0-2 CUTOFF, 3-5 IDLE).
    internal static byte[] Overhead(int apuGenOffBus, int genOffBus, int transferBusOff, int levers)
    {
        var b = Sdk();
        void Set(string field, int value) => b[IFly737MAXDefinition.FieldOffsetsByKey[field].Offset] = (byte)value;
        Set("APU_GEN_OFF_BUS_Light_Status", apuGenOffBus);
        Set("ENG_GEN_OFF_BUS_Light_Status_0", genOffBus);
        Set("ENG_GEN_OFF_BUS_Light_Status_1", genOffBus);
        Set("ENG_TRANSFER_BUS_OFF_Light_Status_0", transferBusOff);
        Set("ENG_TRANSFER_BUS_OFF_Light_Status_1", transferBusOff);
        Set("Engine_Start_Lever_Status_0", levers);
        Set("Engine_Start_Lever_Status_1", levers);
        return b;
    }

    [Theory]
    [InlineData(2, 0, 0, 3, 1.0)]   // the APU generator waits off the buses
    [InlineData(1, 0, 0, 3, 1.0)]   // the same, the light DIM
    [InlineData(0, 2, 0, 3, 1.0)]   // the buses already off the engine generators (APU or ground power)
    [InlineData(0, 2, 2, 0, 1.0)]   // levers already at CUTOFF, on the battery: nothing left to protect
    [InlineData(0, 0, 0, 3, 0.0)]   // the engine generators carry the buses and no APU is up
    [InlineData(0, 2, 2, 3, 0.0)]   // generators off the buses but the buses unpowered
    public void The_shutdown_power_gate(int apuGenOffBus, int genOffBus, int transferBusOff, int levers, double expected)
    {
        var state = State(Overhead(apuGenOffBus, genOffBus, transferBusOff, levers));
        Assert.Equal(expected, state.GetValue(IFly737StateEvaluator.ShutdownPowerField));
    }

    [Fact]
    public void The_shutdown_power_gate_is_unread_while_the_sdk_is_not_ready()
    {
        Assert.True(double.IsNaN(State(null).GetValue(IFly737StateEvaluator.ShutdownPowerField)));
        Assert.False(State(null).AreBusesOffEngineGenerators());
        Assert.False(State(null).AreStartLeversAtCutoff());
    }

    [Fact]
    public void Shutdown_waits_for_power_before_the_transfer_and_stops_when_none_comes()
    {
        var steps = IFly737FlowDefinitions.Build().Single(f => f.Id == "SHUTDOWN").Steps;
        int onGround = steps.FindIndex(s => s.Id == "SD_ON_GROUND");
        int wait = steps.FindIndex(s => s.Id == "SD_POWER_WAIT");
        int apuGen = steps.FindIndex(s => s.Id == "SD_APUGEN");
        int loadOff = steps.FindIndex(s => s.Id == "SD_LOAD_OFF_ENGINES");
        int levers = steps.FindIndex(s => s.Id == "SD_LEVERS");

        // Never airborne first (2026-10-19); the transfer is verified before the cut, and a
        // transfer that did not take stops the flow there.
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, new[] { onGround, wait, apuGen, loadOff, levers });
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[onGround].FailurePolicy);
        Assert.Equal(IFly737StateEvaluator.OnGroundField, steps[onGround].ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[loadOff].FailurePolicy);
        Assert.Equal(IFly737StateEvaluator.LoadOffEnginesField, steps[loadOff].ConditionFieldName);
        Assert.Empty(steps[loadOff].LinkedChecklistItemIds);
        var w = steps[wait];
        Assert.Equal(FlowStepActionType.WaitForCondition, w.ActionType);
        Assert.Equal(IFly737StateEvaluator.ShutdownPowerField, w.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, w.FailurePolicy);
        Assert.Equal(120, w.TimeoutSeconds);
        Assert.True(w.Condition!(1));
        Assert.False(w.Condition!(0));
        Assert.False(w.Condition!(double.NaN));
        Assert.Empty(w.LinkedChecklistItemIds);

        // The press only with the APU generator waiting; the cut only off the engine generators.
        Assert.NotNull(steps[apuGen].LeaveAloneWhen);
        Assert.NotNull(steps[levers].LeaveAloneWhen);
        Assert.NotNull(steps[levers].SkipCondition);
        Assert.Equal(new[] { "SD_LEVERS", "SDC_LEVERS" }, steps[levers].LinkedChecklistItemIds.ToArray());
    }

    // ---- 4. The pressurization reminder follows the windows -----------------------------------
    // (The truth table is IFly737ProfileStructureTests.PressurizationStep_RoutesThroughSanctionedBypass;
    // the run that lost the reminder is in IFly737SecondReviewFlowTests.)

    // ---- 5. The held fire test is released once ------------------------------------------------

    [Fact]
    public void A_held_fire_test_is_released_once_by_whichever_comes_first()
    {
        var def = new IFly737MAXDefinition();
        Assert.False(def.TakeHeldFireTest());   // nothing held
        def.NoteFireTestHeld();
        Assert.True(def.TakeHeldFireTest());    // the first taker (the executor or Shutdown) releases it
        Assert.False(def.TakeHeldFireTest());   // the other finds it already released
    }

    // ---- 6. Engine Start waits and bowed rotor motoring ---------------------------------------

    [Fact]
    public void Bowed_rotor_motoring_reads_the_SimConnect_cache_and_unread_is_not_active()
    {
        var state = State(Sdk(), (IFly737MAXDefinition.Eng1BrmKey, 0), (IFly737MAXDefinition.Eng2BrmKey, 1));
        Assert.False(state.IsBowedRotorMotoring(1));
        Assert.True(state.IsBowedRotorMotoring(2));
        Assert.False(State(Sdk()).IsBowedRotorMotoring(2));
        // The definition registers both keys (the continuous batch caches them).
        var vars = new IFly737MAXDefinition().GetVariables();
        Assert.True(vars.ContainsKey(IFly737MAXDefinition.Eng1BrmKey));
        Assert.True(vars.ContainsKey(IFly737MAXDefinition.Eng2BrmKey));
    }

    [Theory]
    // done always wins
    [InlineData(true, 5000, 5000, 60, "Done")]
    // no BRM: the 60 s hung-start limit, exactly as before
    [InlineData(false, 59, 59, 60, "Waiting")]
    [InlineData(false, 60, 60, 60, "TimedOut")]
    // BRM read active a moment ago, 4 minutes in: still waiting
    [InlineData(false, 240, 1, 60, "Waiting")]
    // BRM ended 59 s ago: still waiting; 60 s after it ended: timed out
    [InlineData(false, 300, 59, 60, "Waiting")]
    [InlineData(false, 300, 60, 60, "TimedOut")]
    // a BRM flag stuck active: the allowance ends it (60 + 300 s)
    [InlineData(false, 359, 0, 60, "Waiting")]
    [InlineData(false, 360, 0, 60, "TimedOut")]
    // the starter cutout wait's own 120 s limit
    [InlineData(false, 119, 119, 120, "Waiting")]
    [InlineData(false, 120, 120, 120, "TimedOut")]
    [InlineData(false, 419, 0, 120, "Waiting")]
    [InlineData(false, 420, 0, 120, "TimedOut")]
    public void A_start_wait_runs_on_through_bowed_rotor_motoring(bool done, double sinceStart,
        double sinceStartOrBrm, int baseSeconds, string expected)
    {
        Assert.Equal(Enum.Parse<IFly737ActionExecutor.StartWaitVerdict>(expected),
            IFly737ActionExecutor.JudgeStartWait(done, sinceStart, sinceStartOrBrm, baseSeconds));
    }

    // An N2 wait that ends without the fuel point (timed out, or the flow stopped) puts the start switch
    // back to OFF, as the MD-11's light-up watch pushes START in: left at GRD, the starter went on
    // motoring a dry engine. Only over a lever read at CUTOFF and a switch read at GRD.
    [Theory]
    [InlineData(0.0, 0.0, true)]               // CUTOFF, GRD: abort
    [InlineData(2.0, 0.0, true)]               // CUTOFF with its fire light (the 0-5 composite's 0-2)
    [InlineData(3.0, 0.0, false)]              // IDLE: the fuel is on, the start runs on
    [InlineData(5.0, 0.0, false)]
    [InlineData(0.0, 1.0, false)]              // the switch already OFF
    [InlineData(double.NaN, 0.0, false)]       // lever unread
    [InlineData(0.0, double.NaN, false)]       // switch unread
    public void A_start_that_never_reached_the_fuel_point_is_aborted_only_without_fuel(
        double lever, double startSwitch, bool abort)
        => Assert.Equal(abort, IFly737ActionExecutor.ShouldAbortDryStart(lever, startSwitch));

    // A flow's start switch to GRD records the start with the run, so a Stop after the N2 wait passed
    // (the 2 s before the start lever step) or the window's close can abort it; the executor
    // can't be made available here, so the record's two rules are pinned as the pure functions it uses.
    [Theory]
    [InlineData("Engine_Start_Switch_Status_1", 0.0, 2)]   // engine 2 to GRD: recorded
    [InlineData("Engine_Start_Switch_Status_0", 0.0, 1)]
    [InlineData("Engine_Start_Switch_Status_1", 2.0, 0)]   // CONT: no start
    [InlineData("Engine_Start_Lever_Status_1", 0.0, 0)]    // not a start switch
    [InlineData("Engine_Start_Switch_Status_2", 0.0, 0)]   // no third engine
    public void A_flows_start_switch_to_GRD_records_its_engine(string key, double target, int engine)
        => Assert.Equal(engine, IFly737ActionExecutor.FlowStartedEngine(key, target));

    [Theory]
    [InlineData("Engine_Start_Lever_Status_1", 3.0, 2)]    // fuel on: the start runs on
    [InlineData("Engine_Start_Lever_Status_0", 3.0, 1)]
    [InlineData("Engine_Start_Lever_Status_1", 0.0, 0)]    // CUTOFF: still a dry start
    [InlineData("Engine_Start_Switch_Status_1", 1.0, 2)]   // switch OFF (an abort, or the pilot's): nothing to abort
    [InlineData("Engine_Start_Switch_Status_0", 2.0, 1)]   // CONT
    [InlineData("Engine_Start_Switch_Status_1", 0.0, 0)]   // GRD again: still a start
    [InlineData("Pack_Switch_Status_0", 1.0, 0)]
    public void The_fuel_on_or_the_switch_off_GRD_forgets_a_flows_start(string key, double target, int engine)
        => Assert.Equal(engine, IFly737ActionExecutor.StartSettledEngine(key, target));

    [Fact]
    public void The_engine_start_waits_are_the_executors_and_stop_the_flow()
    {
        var steps = IFly737FlowDefinitions.Build().Single(f => f.Id == "ENGINE_START").Steps;
        var expected = new[]
        {
            ("ES_E2_GRD", "Engine_Start_Switch_Status_1"),
            ("ES_E2_N2", IFly737ActionExecutor.KeyEng2FuelN2),
            ("ES_E2_RUN", "Engine_Start_Lever_Status_1"),
            ("ES_E2_CUTOUT", IFly737ActionExecutor.KeyEng2StarterCutout),
            ("ES_E1_GRD", "Engine_Start_Switch_Status_0"),
            ("ES_E1_N2", IFly737ActionExecutor.KeyEng1FuelN2),
            ("ES_E1_RUN", "Engine_Start_Lever_Status_0"),
            ("ES_E1_CUTOUT", IFly737ActionExecutor.KeyEng1StarterCutout),
        };
        Assert.Equal(expected, steps.Skip(2).Select(s => (s.Id, s.EventName!)).ToArray());

        foreach (var key in new[] { IFly737ActionExecutor.KeyEng1FuelN2, IFly737ActionExecutor.KeyEng2FuelN2,
                     IFly737ActionExecutor.KeyEng1StarterCutout, IFly737ActionExecutor.KeyEng2StarterCutout })
        {
            var step = steps.Single(s => s.EventName == key);
            Assert.Equal(FlowStepFailurePolicy.Stop, step.FailurePolicy);
            Assert.Empty(step.LinkedChecklistItemIds);   // a wait writes nothing and claims no line
            Assert.True(IFly737ActionExecutor.IsPseudoKey(key));
            Assert.True(IFly737ActionExecutor.HasPseudoKeyHandler(key));
        }
        Assert.Equal(60, IFly737ActionExecutor.FuelN2WaitSeconds);
        Assert.Equal(120, IFly737ActionExecutor.StarterCutoutWaitSeconds);
    }

    [Fact]
    public void Each_start_wait_reads_its_own_engine_and_ends_at_its_boundary()
    {
        double fuel = IFly737StateEvaluator.EngStartFuelN2;
        foreach (var (key, field) in new[]
        {
            (IFly737ActionExecutor.KeyEng1FuelN2, "FO_ENG1_N2"),
            (IFly737ActionExecutor.KeyEng2FuelN2, "FO_ENG2_N2"),
        })
        {
            Assert.Equal((field, true), IFly737ActionExecutor.StartWaitReads(key, fuel));
            Assert.False(IFly737ActionExecutor.StartWaitReads(key, fuel - 1).Done);
            Assert.False(IFly737ActionExecutor.StartWaitReads(key, double.NaN).Done);
        }
        foreach (var (key, field) in new[]
        {
            (IFly737ActionExecutor.KeyEng1StarterCutout, "Engine_Start_Switch_Status_0"),
            (IFly737ActionExecutor.KeyEng2StarterCutout, "Engine_Start_Switch_Status_1"),
        })
        {
            Assert.Equal((field, true), IFly737ActionExecutor.StartWaitReads(key, IFly737ActionExecutor.EngStartOff));
            Assert.False(IFly737ActionExecutor.StartWaitReads(key, IFly737ActionExecutor.EngStartGround).Done);
            Assert.False(IFly737ActionExecutor.StartWaitReads(key, IFly737ActionExecutor.EngStartContinuous).Done);
            Assert.False(IFly737ActionExecutor.StartWaitReads(key, double.NaN).Done);
        }
    }

    [Fact]
    public void The_bowed_rotor_motoring_note_reads_the_same_under_a_comma_decimal_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Waiting for engine 2 bowed rotor motoring to finish.", IFly737ActionExecutor.BrmWaitText(2));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---- 7. One Kohlsman computation ----------------------------------------------------------

    [Fact]
    public void Ctrl_B_and_the_First_Officer_share_the_Kohlsman_parameter()
    {
        Assert.Equal(16211u, IFly737MAXDefinition.KohlsmanSetParameter(29.92));
        Assert.Equal(IFly737MAXDefinition.KohlsmanSetParameter(29.92), IFly737ActionExecutor.StandardKohlsmanParameter);
        Assert.Equal(16208u, IFly737MAXDefinition.KohlsmanSetParameter(1013));      // hectopascals as typed
        Assert.Equal(16212u, IFly737MAXDefinition.KohlsmanSetParameter(1013.25));
    }

    [Fact]
    public void A_pressure_typed_with_a_comma_sets_the_same_parameter()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(NumberParsing.TryParseTyped("29,92", out double inches));
            Assert.Equal(IFly737ActionExecutor.StandardKohlsmanParameter, IFly737MAXDefinition.KohlsmanSetParameter(inches));
            Assert.True(NumberParsing.TryParseTyped("1013,25", out double hpa));
            Assert.Equal(16212u, IFly737MAXDefinition.KohlsmanSetParameter(hpa));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}

/// <summary>Accepts every write (a real executor needs the sim) and records what it was sent.</summary>
internal sealed class IFly737RecordingExecutor : IFoActionExecutor
{
    public ConcurrentQueue<(string Event, int? Target)> Sent { get; } = new();
    public bool IsAvailable => true;

    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (step.ActionType == FlowStepActionType.SetSwitchMultiple)
            foreach (var (ev, target) in step.MultiActions) Sent.Enqueue((ev, target));
        else
            Sent.Enqueue((step.EventName ?? "", step.TargetValue));
        return Task.FromResult(true);
    }

    public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
}

/// <summary>Runs the named steps of a real flow on a real FlowManager and checklist.</summary>
internal static class IFly737FlowRun
{
    internal sealed record Result(List<string> Speech, List<string> Skipped, string[] Unfinished,
        (string Event, int? Target)[] Sent, bool Finished);

    /// <summary>When <paramref name="cancelWhenStarted"/> starts, the run is cancelled 300 ms later
    /// (a wait that really waits would take minutes); otherwise it must finish within 20 s.</summary>
    public static async Task<Result> RunAsync<TExec>(IFly737StateEvaluator state, TExec executor,
        string flowId, string[] stepIds, string? cancelWhenStarted = null)
        where TExec : IFoActionExecutor
    {
        var source = IFly737FlowDefinitions.Build().Single(f => f.Id == flowId);
        var flow = new FlowDefinition<IFly737StateEvaluator>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());

        var groups = new List<ChecklistGroup<TExec, IFly737StateEvaluator>>();
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<TExec, IFly737StateEvaluator>(state, executor, groups);
        var flows = new FlowManager<TExec, IFly737StateEvaluator>(state, executor, checklist, speech);
        var skipped = new List<string>();
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
        if (!finished) flows.Cancel();

        var sent = executor is IFly737RecordingExecutor recorder ? recorder.Sent.ToArray() : Array.Empty<(string, int?)>();
        return new Result(speech.All.ToList(), skipped,
            flows.UnfinishedChecklistItemIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(), sent, finished);
    }
}

/// <summary>The gear and pressurization fixes on a real FlowManager (no sim).</summary>
public class IFly737SecondReviewFlowTests
{
    private static IFly737StateEvaluator State(byte[]? sdk, params (string Key, double Value)[] simConnect)
        => IFly737SecondReviewTests.State(sdk, simConnect);

    // ---- 2. Gear UP only when definitely airborne ---------------------------------------------

    [Theory]
    [InlineData(1.0)]           // on the ground
    [InlineData(double.NaN)]    // unread
    public async Task After_Takeoff_on_the_ground_leaves_the_gear_lever_down(double onGround)
    {
        var state = double.IsNaN(onGround)
            ? State(IFly737SecondReviewTests.Sdk())
            : State(IFly737SecondReviewTests.Sdk(), ("SIM_ON_GROUND", onGround));
        var run = await IFly737FlowRun.RunAsync(state, new IFly737RecordingExecutor(), "AFTER_TAKEOFF", new[] { "AT_GEAR_OFF" });

        Assert.True(run.Finished);
        Assert.Contains(IFly737ActionExecutor.GearUpLeaveAloneText, run.Speech);
        Assert.Empty(run.Sent);
        Assert.Equal(new[] { "AT_GEAR_OFF" }, run.Skipped);
        Assert.Equal(new[] { "ATKO_GEAR_OFF" }, run.Unfinished);
    }

    [Fact]
    public async Task After_Takeoff_in_the_air_raises_the_gear_lever()
    {
        var state = State(IFly737SecondReviewTests.Sdk(), ("SIM_ON_GROUND", 0));
        var run = await IFly737FlowRun.RunAsync(state, new IFly737RecordingExecutor(), "AFTER_TAKEOFF", new[] { "AT_GEAR_OFF" });

        Assert.Contains(("Gear_Lever_Status", (int?)IFly737ActionExecutor.GearUp), run.Sent);
        Assert.Contains("Gear lever: UP", run.Speech);
        Assert.Empty(run.Skipped);
    }

    // ---- 4. The pressurization reminder follows the windows -----------------------------------

    private static readonly string[] PressSteps = { "PF_PRESS_ALTS", "PF_PRESS" };

    /// <summary>The bug: a plan, a write that did not take (no sim: "Sim not connected"), and the
    /// windows not reading the plan. The reminder used to be skipped as "Already set" because a
    /// plan existed, and was lost.</summary>
    [Fact]
    public async Task A_write_that_did_not_take_still_reminds_the_Captain()
    {
        var b = IFly737SecondReviewTests.Sdk();   // windows all zero: not the plan
        var state = State(b);
        state.SetPlannedPressurizationAltitudes(35000, 450);

        var run = await IFly737FlowRun.RunAsync(state, new IFly737ActionExecutor(), "PREFLIGHT", PressSteps);

        Assert.Contains("Skipping: Flight and landing altitudes: set", run.Speech);
        Assert.Contains("Captain action required: Set flight and landing altitudes on the pressurization panel.", run.Speech);
        Assert.DoesNotContain("Already set: Flight and landing altitudes", run.Speech);
    }

    [Fact]
    public async Task Windows_that_read_the_plan_skip_the_reminder()
    {
        var b = IFly737SecondReviewTests.Sdk();
        b[IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status] = 3;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status] = 5;
        b[IFlySdkOffsets.Landing_Altitude_Indicator_100_Status] = 4;
        b[IFlySdkOffsets.Landing_Altitude_Indicator_10_Status] = 5;   // 35000 / 00450
        var state = State(b);
        state.SetPlannedPressurizationAltitudes(35000, 450);

        var run = await IFly737FlowRun.RunAsync(state, new IFly737RecordingExecutor(), "PREFLIGHT", PressSteps);

        Assert.Contains("Already set: Flight and landing altitudes", run.Speech);
        Assert.DoesNotContain(run.Speech, s => s.StartsWith("Captain action required", StringComparison.Ordinal));
    }
}

/// <summary>The Shutdown power guard on a real FlowManager (no sim): a recording executor stands in
/// for the aircraft, so a press changes no light and the guard reads the lights as they were.</summary>
public class IFly737ShutdownPowerFlowTests
{
    private static readonly string[] Head = { "SD_POWER_WAIT", "SD_APUGEN", "SD_LOAD_OFF_ENGINES", "SD_LEVERS" };

    private static bool SentLevers(IFly737FlowRun.Result run) =>
        run.Sent.Any(s => s.Event.StartsWith("Engine_Start_Lever_Status", StringComparison.Ordinal));

    private static bool SentApuGen(IFly737FlowRun.Result run) =>
        run.Sent.Any(s => s.Event.StartsWith("BTN_APU_GEN", StringComparison.Ordinal));

    [Fact]
    public async Task An_apu_generator_press_that_did_not_take_stops_the_flow_with_the_engines_running()
    {
        // The APU generator waits off the buses, the engine generators carry them, levers at IDLE.
        // The press changes no light here, so the transfer never takes: the flow STOPS at the
        // transfer check (10 s) and never reaches the levers or the pumps after them (2026-10-19:
        // a left-alone lever step let the rest of Shutdown switch the fuel and hydraulic pumps off).
        var state = IFly737SecondReviewTests.State(IFly737SecondReviewTests.Overhead(2, 0, 0, 3));
        var run = await IFly737FlowRun.RunAsync(state, new IFly737RecordingExecutor(), "SHUTDOWN", Head);

        Assert.True(run.Finished);
        Assert.Contains("Waiting for: APU generator or ground power", run.Speech);
        Assert.True(SentApuGen(run));
        Assert.False(SentLevers(run));
        Assert.Contains(run.Speech, s => s.Contains("APU generator or ground power on the buses", StringComparison.Ordinal));
        Assert.DoesNotContain("SD_LEVERS", run.Skipped);
    }

    [Fact]
    public async Task Buses_already_off_the_engine_generators_are_cut_without_a_press()
    {
        // The APU or ground power carries the buses: both GEN OFF BUS lit, both TRANSFER BUS OFF out.
        var state = IFly737SecondReviewTests.State(IFly737SecondReviewTests.Overhead(0, 2, 0, 3));
        var run = await IFly737FlowRun.RunAsync(state, new IFly737RecordingExecutor(), "SHUTDOWN", Head);

        Assert.True(run.Finished);
        Assert.False(SentApuGen(run));
        Assert.Contains("APU generators left as they are: no APU generator is waiting to take the buses.", run.Speech);
        Assert.Contains(("Engine_Start_Lever_Status_0", (int?)0), run.Sent);
        Assert.Contains(("Engine_Start_Lever_Status_1", (int?)0), run.Sent);
        Assert.Equal(new[] { "SD_APUGEN" }, run.Skipped);
    }

    [Fact]
    public async Task Levers_already_at_cutoff_on_the_battery_are_already_set()
    {
        // A re-run after the engines were cut by hand with nothing on the buses.
        var state = IFly737SecondReviewTests.State(IFly737SecondReviewTests.Overhead(0, 2, 2, 0));
        var run = await IFly737FlowRun.RunAsync(state, new IFly737RecordingExecutor(), "SHUTDOWN", Head);

        Assert.True(run.Finished);
        Assert.Empty(run.Sent);
        Assert.Contains("Already set: Engine start levers: CUTOFF", run.Speech);
    }

    [Fact]
    public async Task With_no_apu_and_no_ground_power_nothing_is_pressed_or_cut()
    {
        // The engine generators carry the buses and the APU is not up: the flow waits.
        var state = IFly737SecondReviewTests.State(IFly737SecondReviewTests.Overhead(0, 0, 0, 3));
        var run = await IFly737FlowRun.RunAsync(state, new IFly737RecordingExecutor(), "SHUTDOWN", Head,
            cancelWhenStarted: "SD_POWER_WAIT");

        Assert.Contains("Waiting for: APU generator or ground power", run.Speech);
        Assert.Empty(run.Sent);
        Assert.Contains("Shutdown flow cancelled", run.Speech);
    }
}
