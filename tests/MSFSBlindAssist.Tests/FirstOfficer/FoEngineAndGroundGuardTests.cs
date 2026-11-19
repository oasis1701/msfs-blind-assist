using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklists = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;
using Pmdg737State = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg777State = MSFSBlindAssist.FirstOfficer.AircraftStateEvaluator;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The engine, ground and power guards of the 2026-10-19 review fixes. A cold-and-dark flow run with
/// the engines turning cut them (the MD-11 saw it live and grew PU_ENGINES_STOPPED; the 777, the
/// Fenix, the A32NX, the A330 and the A380 had no guard); a Shutdown or Securing flow started in
/// flight cut the engines or the IRS; and checklist ticks bypassed the guards their flows carry.
/// </summary>
public class FoEngineAndGroundGuardTests
{
    // ---- Flow guards: first step, read-only, Stop, unread fails --------------------------------

    private static void AssertGuardFirst<T>(IEnumerable<FlowDefinition<T>> flows, string flowId, string stepId, string field)
        where T : IFoStateEvaluator
    {
        var steps = flows.Single(f => f.Id == flowId).Steps;
        var g = steps[0];
        Assert.Equal(stepId, g.Id);
        Assert.Equal(FlowStepActionType.WaitForCondition, g.ActionType);
        Assert.Equal(field, g.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, g.FailurePolicy);
        Assert.True(g.Condition!(1));
        Assert.False(g.Condition!(0));
        Assert.False(g.Condition!(double.NaN));
        Assert.Empty(g.LinkedChecklistItemIds);
    }

    [Fact]
    public void Every_cold_and_dark_flow_stops_unless_the_engines_read_stopped()
    {
        AssertGuardFirst(FenixFlowDefinitions.Build(), "ELECTRICAL_POWER_UP", "EPU_ENGINES_STOPPED", "FO_ENGINES_OFF");
        AssertGuardFirst(FbwA320FlowDefinitions.Build(), "ELECTRICAL_POWER_UP", "EPU_ENGINES_STOPPED", "FO_ENGINES_OFF");
        AssertGuardFirst(HwA330FlowDefinitions.Build(), "ELECTRICAL_POWER_UP", "EPU_ENGINES_STOPPED", "FO_ENGINES_OFF");
        AssertGuardFirst(FbwA380FlowDefinitions.Build(), "COCKPIT_PREP", "CP_ENGINES_STOPPED", FbwA380StateEvaluator.EnginesStoppedField);
        AssertGuardFirst(PMDG777FlowDefinitions.Build(), "ELECTRICAL_POWER_UP", "EPU_ENGINES_STOPPED", Pmdg777State.EnginesStoppedField);
        AssertGuardFirst(PMDG777FlowDefinitions.Build(), "COCKPIT_PREP", "CP_ENGINES_STOPPED", Pmdg777State.EnginesStoppedField);
        AssertGuardFirst(Md11FoFlowDefinitions.Build(), "POWER_UP", "PU_ENGINES_STOPPED", "FO_ENGINES_STOPPED");
        AssertGuardFirst(MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoFlowDefinitions.Build(), "POWER_UP", "PU_ENGINES_STOPPED", "FO_ENGINES_STOPPED");
    }

    [Fact]
    public void Every_shutdown_and_securing_flow_stops_unless_on_the_ground()
    {
        AssertGuardFirst(FenixFlowDefinitions.Build(), "SHUTDOWN", "SD_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(FenixFlowDefinitions.Build(), "SECURE", "SC_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(FbwA320FlowDefinitions.Build(), "SHUTDOWN", "SD_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(FbwA320FlowDefinitions.Build(), "SECURE", "SC_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(HwA330FlowDefinitions.Build(), "SHUTDOWN", "SD_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(HwA330FlowDefinitions.Build(), "SECURE", "SC_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(FbwA380FlowDefinitions.Build(), "PARKING", "PK_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(FbwA380FlowDefinitions.Build(), "SECURE", "SC_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(PMDG777FlowDefinitions.Build(), "SHUTDOWN", "SD_ON_GROUND", Pmdg777State.OnGroundField);
        AssertGuardFirst(PMDG777FlowDefinitions.Build(), "SECURE", "SEC_ON_GROUND", Pmdg777State.OnGroundField);
        AssertGuardFirst(Pmdg737Flows.Build(), "SHUTDOWN", "SD_ON_GROUND", Pmdg737State.OnGroundField);
        AssertGuardFirst(Pmdg737Flows.Build(), "SECURE", "SE_ON_GROUND", Pmdg737State.OnGroundField);
        AssertGuardFirst(IFly737FlowDefinitions.Build(), "SHUTDOWN", "SD_ON_GROUND", IFly737StateEvaluator.OnGroundField);
        AssertGuardFirst(IFly737FlowDefinitions.Build(), "SECURE", "SE_ON_GROUND", IFly737StateEvaluator.OnGroundField);
        AssertGuardFirst(MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoFlowDefinitions.Build(), "SHUTDOWN", "SD_ON_GROUND", "SIM_ON_GROUND");
        AssertGuardFirst(MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoFlowDefinitions.Build(), "SECURE", "SC_ON_GROUND", "SIM_ON_GROUND");
    }

    private static void AssertPowerWaitBefore<T>(IEnumerable<FlowDefinition<T>> flows, string flowId, string waitId,
        string field, string firstCutId)
        where T : IFoStateEvaluator
    {
        var steps = flows.Single(f => f.Id == flowId).Steps;
        int wait = steps.FindIndex(s => s.Id == waitId), cut = steps.FindIndex(s => s.Id == firstCutId);
        Assert.True(wait >= 0 && cut == wait + 1, $"{flowId}: {waitId} at {wait}, {firstCutId} at {cut}");
        Assert.Equal(field, steps[wait].ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[wait].FailurePolicy);
        Assert.False(steps[wait].Condition!(double.NaN));
        Assert.NotNull(steps[wait].SkipCondition);   // passes at once with the masters already off
    }

    [Fact]
    public void Airbus_engine_masters_go_off_only_with_the_APU_or_external_power_able_to_take_the_buses()
    {
        AssertPowerWaitBefore(FenixFlowDefinitions.Build(), "SHUTDOWN", "SD_POWER_SOURCE", FenixStateEvaluator.ApuOrExtPowerField, "SD_ENG1_OFF");
        AssertPowerWaitBefore(FbwA320FlowDefinitions.Build(), "SHUTDOWN", "SD_POWER_SOURCE", FbwA320StateEvaluator.ApuOrExtPowerField, "SD_ENG1_OFF");
        AssertPowerWaitBefore(HwA330FlowDefinitions.Build(), "SHUTDOWN", "SD_POWER_SOURCE", HwA330StateEvaluator.ApuOrExtPowerField, "SD_ENG1_OFF");
        AssertPowerWaitBefore(FbwA380FlowDefinitions.Build(), "PARKING", "PK_POWER_SOURCE", FbwA380StateEvaluator.ApuOrExtPowerField, "PK_MASTERS_OFF");
    }

    // ---- The readings the guards rest on: unread is never stopped, never powered ----------------

    [Fact]
    public void Engine_N2_reads_unknown_until_the_first_answer()
    {
        Assert.True(double.IsNaN(new FenixStateEvaluator().GetValue("FO_ENG1_N2")));
        Assert.True(double.IsNaN(new FenixStateEvaluator().GetValue("FO_ENGINES_OFF")));
        Assert.True(double.IsNaN(new Pmdg737State().GetValue("FO_ENG2_N2")));
        Assert.False(Pmdg737Flows.EnginesStopped(new Pmdg737State()));
        Assert.True(double.IsNaN(new Pmdg777State().GetValue(Pmdg777State.EnginesStoppedField)));

        var fenix = new FenixStateEvaluator();
        fenix.SetEngineN2(0, double.NaN);
        Assert.True(double.IsNaN(fenix.GetValue("FO_ENGINES_OFF")));
        fenix.SetEngineN2(0, 58);
        Assert.Equal(0, fenix.GetValue("FO_ENGINES_OFF"));
        fenix.SetEngineN2(3, 0);
        Assert.Equal(1, fenix.GetValue("FO_ENGINES_OFF"));
    }

    [Theory]
    [InlineData(1.0, double.NaN, 1.0)]       // one source lit is enough with the other unread
    [InlineData(double.NaN, 1.0, 1.0)]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.0, double.NaN, double.NaN)]
    public void The_A320_family_power_source_is_either_one_lit(double apuAvail, double extPwr, double expected)
    {
        var fenix = new FenixStateEvaluator();
        fenix.SetSimConnect(Seed((FenixActionExecutor.ApuAvailField, apuAvail), ("I_OH_ELEC_EXT_PWR_L", extPwr)));
        Assert.Equal(expected, fenix.GetValue(FenixStateEvaluator.ApuOrExtPowerField));

        var a32nx = new FbwA320StateEvaluator();
        a32nx.SetSimConnect(Seed(("A32NX_OVHD_APU_START_PB_IS_AVAILABLE", apuAvail), ("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", extPwr)));
        Assert.Equal(expected, a32nx.GetValue(FbwA320StateEvaluator.ApuOrExtPowerField));
    }

    [Fact]
    public void The_A380_engines_read_stopped_only_with_all_four_off()
    {
        var a380 = new FbwA380StateEvaluator();
        a380.SetSimConnect(Seed(("A32NX_ENGINE_STATE:1", 0), ("A32NX_ENGINE_STATE:2", 0), ("A32NX_ENGINE_STATE:3", 0)));
        Assert.True(double.IsNaN(a380.GetValue(FbwA380StateEvaluator.EnginesStoppedField)));
        a380.SetSimConnect(Seed(("A32NX_ENGINE_STATE:1", 0), ("A32NX_ENGINE_STATE:2", 0),
            ("A32NX_ENGINE_STATE:3", 0), ("A32NX_ENGINE_STATE:4", 1)));
        Assert.Equal(0, a380.GetValue(FbwA380StateEvaluator.EnginesStoppedField));
        a380.SetSimConnect(Seed(("A32NX_ENGINE_STATE:1", 0), ("A32NX_ENGINE_STATE:2", 0),
            ("A32NX_ENGINE_STATE:3", 0), ("A32NX_ENGINE_STATE:4", 0)));
        Assert.Equal(1, a380.GetValue(FbwA380StateEvaluator.EnginesStoppedField));
    }

    private static SimConnectManager Seed(params (string Key, double Value)[] values) =>
        SeededSimConnectCache.With(values.Where(v => !double.IsNaN(v.Value)).ToArray());

    // ---- Checklist ticks carry the flows' guards -------------------------------------------------

    private static Func<TState, bool> GuardOf<TExec, TState>(IEnumerable<ChecklistGroup<TExec, TState>> groups, string itemId)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator
    {
        var item = groups.SelectMany(g => g.Items).Single(i => i.Id == itemId);
        Assert.NotNull(item.LeaveAloneWhen);
        Assert.False(string.IsNullOrWhiteSpace(item.LeaveAloneText));
        return item.LeaveAloneWhen!;
    }

    [Fact]
    public void Battery_and_lever_ticks_are_refused_with_the_engines_unread()
    {
        // Unread N2 is not stopped: the tick is refused (the Secure flows' FOB-22 rule).
        Assert.True(GuardOf(Pmdg737Checklists.Build(), "EPD_BAT")(new Pmdg737State()));
        Assert.True(GuardOf(PMDG777ChecklistDefinitions.Build(), "EPD_BATTERY_OFF")(new Pmdg777State()));
        Assert.True(GuardOf(PMDG777ChecklistDefinitions.Build(), "PF_FUEL_CONTROL")(new Pmdg777State()));
        var ifly = IFly737SecondReviewTests.State(null);
        Assert.True(GuardOf(IFly737ChecklistDefinitions.Build(), "EPD_BAT")(ifly));
        Assert.True(GuardOf(IFly737ChecklistDefinitions.Build(), "SD_LEVERS")(ifly));
        Assert.True(GuardOf(Pmdg737Checklists.Build(), "SD_LEVERS")(new Pmdg737State()));
        Assert.True(GuardOf(PMDG777ChecklistDefinitions.Build(), "BS_EXT_PWR_OFF")(new Pmdg777State()));

        var md11 = new Md11FoStateEvaluator();
        foreach (var id in new[] { "PU_FUEL_OFF", "PK_FUEL", "SD_IRS", "SD_BATTERY" })
            Assert.True(GuardOf(Md11FoChecklistDefinitions.Build(), id)(md11), id);

        var b757 = new MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoStateEvaluator();
        foreach (var id in new[] { "PU_FUEL_CUTOFF", "SD_FUEL_CUTOFF", "SC_IRS", "SC_BATTERY" })
            Assert.True(GuardOf(MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoChecklistDefinitions.Build(), id)(b757), id);
    }

    [Fact]
    public void Airbus_engine_and_IRS_ticks_are_refused_off_the_ground()
    {
        var fenix = new FenixStateEvaluator();   // nothing read: not on the ground, no power source
        foreach (var id in new[] { "SD_ENG1_OFF", "SD_ENG2_OFF", "SC_ADIRS" })
            Assert.True(GuardOf(FenixChecklistDefinitions.Build(), id)(fenix), id);
        var a32nx = new FbwA320StateEvaluator();
        foreach (var id in new[] { "SD_ENG1_OFF", "SD_ENG2_OFF", "SC_ADIRS" })
            Assert.True(GuardOf(FbwA320ChecklistDefinitions.Build(), id)(a32nx), id);
        var a330 = new HwA330StateEvaluator();
        foreach (var id in new[] { "SD_ENG1_OFF", "SD_ENG2_OFF", "SC_ADIRS" })
            Assert.True(GuardOf(HwA330ChecklistDefinitions.Build(), id)(a330), id);
        var a380 = new FbwA380StateEvaluator();
        foreach (var id in new[] { "CP_MASTERS_OFF", "PK_MASTERS_OFF", "SC_ADIRS" })
            Assert.True(GuardOf(FbwA380ChecklistDefinitions.Build(), id)(a380), id);

        // On the ground with the APU available, a master goes off on a tick.
        fenix.SetSimConnect(Seed(("SIM_ON_GROUND", 1), (FenixActionExecutor.ApuAvailField, 1)));
        Assert.False(GuardOf(FenixChecklistDefinitions.Build(), "SD_ENG1_OFF")(fenix));
    }

    // ---- The Fenix pulse toggles never press on an unread light ---------------------------------

    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_EXTPWR")]
    [InlineData("PREFLIGHT", "PF_GNDCTL")]
    [InlineData("BEFORE_START", "BS_EXTPWR_OFF")]
    [InlineData("TAXI", "TX_AUTOBRAKE")]
    [InlineData("APPROACH", "AP_LS1")]
    [InlineData("APPROACH", "AP_LS2")]
    [InlineData("SHUTDOWN", "SD_LS1")]
    [InlineData("SECURE", "SC_EXTPWR_OFF")]
    public void A_Fenix_toggle_with_its_light_unread_is_left_alone_never_pressed_or_already_set(string flowId, string stepId)
    {
        var step = FenixFlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);
        var unread = new FenixStateEvaluator();
        Assert.False(step.SkipCondition!(unread));    // never "Already set" over an unread light
        Assert.True(step.LeaveAloneWhen!(unread));    // and never pressed
    }

    // ---- Smaller fixes -------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, false)]   // OFF
    [InlineData(1.0, true)]    // ARMED
    [InlineData(2.0, false)]   // ON: the lights burning on their batteries is not ARMED
    public void The_737_emergency_exit_ARMED_line_accepts_armed_alone(double selector, bool expected)
    {
        var item = Pmdg737Checklists.Build().SelectMany(g => g.Items).Single(i => i.Id == "PF_EMER");
        Assert.Equal(expected, item.StateCondition!(selector));
    }

    [Theory]
    [InlineData(25.0, true, false)]    // N2 at the motoring point mid-BRM: motoring is NOT done
    [InlineData(25.0, false, true)]    // motoring done: the start lever may go to IDLE
    [InlineData(10.0, false, false)]   // the starter has not brought N2 up yet
    public void The_iFly_fuel_point_is_motoring_done(double n2, bool brmActive, bool done)
    {
        Assert.Equal(done, IFly737ActionExecutor.StartWaitDone(IFly737ActionExecutor.KeyEng2FuelN2, n2, brmActive));
        // The starter-cutout wait is not a fuel point: BRM never holds its condition.
        Assert.True(IFly737ActionExecutor.StartWaitDone(IFly737ActionExecutor.KeyEng2StarterCutout,
            IFly737ActionExecutor.EngStartOff, brmActive: true));
    }

    [Fact]
    public void The_weather_radar_test_gate_is_one_per_connection()
    {
        var a = SeededSimConnectCache.With();
        var b = SeededSimConnectCache.With();
        var gate = WxrTestGate.Pmdg737(a);
        Assert.Same(gate, WxrTestGate.Pmdg737(a));
        Assert.True(gate.TryBegin());
        Assert.False(WxrTestGate.Pmdg737(a).TryBegin());   // the panel and the FO share it
        Assert.True(WxrTestGate.Pmdg737(b).TryBegin());    // another connection does not
        Assert.True(WxrTestGate.Pmdg777(a).TryBegin());    // nor the other jet
        gate.End();
        Assert.True(WxrTestGate.Pmdg737(a).TryBegin());
    }

    [Theory]
    [InlineData(0, 899, true)]
    [InlineData(0, 900, false)]
    [InlineData(0, 1000, false)]
    public void A_sample_request_repeated_within_900_ms_is_coalesced(long last, long now, bool coalesced)
    {
        Assert.Equal(coalesced, SimConnectManager.IsCoalesced(last, now));
    }

    // ---- FlowManager: a throw outside a step's own catch ends the run aloud ---------------------

    [Fact]
    public async Task A_throwing_step_handler_stops_the_flow_and_says_so()
    {
        var state = IFly737SecondReviewTests.State(IFly737SecondReviewTests.Sdk());
        var executor = new IFly737RecordingExecutor();
        var checklist = new ChecklistManager<IFly737RecordingExecutor, IFly737StateEvaluator>(
            state, executor, new List<ChecklistGroup<IFly737RecordingExecutor, IFly737StateEvaluator>>());
        var speech = new GatedSpeechCapture();
        var flows = new FlowManager<IFly737RecordingExecutor, IFly737StateEvaluator>(state, executor, checklist, speech);
        string? failed = null;
        flows.FlowFailed += (_, reason) => failed = reason;
        flows.StepStarted += (_, _, _) => throw new InvalidOperationException("handler threw");

        var flow = new FlowDefinition<IFly737StateEvaluator>
        {
            Id = "TEST", Name = "Test",
            Steps = new()
            {
                new FlowStep<IFly737StateEvaluator>
                {
                    Id = "T1", Label = "Seatbelt signs: ON",
                    ActionType = FlowStepActionType.SetSwitch, EventName = "Fasten_Belts_Switch_Status", TargetValue = 2,
                },
            },
        };
        flows.StartFlow(flow);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (flows.IsRunning && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.False(flows.IsRunning);
        Assert.NotNull(failed);
        Assert.Contains("Test flow stopped. Unable to complete: Seatbelt signs: ON", speech.All);
        Assert.Empty(executor.Sent);
    }
}
