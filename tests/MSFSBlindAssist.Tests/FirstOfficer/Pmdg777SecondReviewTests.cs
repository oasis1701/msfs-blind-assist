using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 777 First Officer's fixes from the second PR #160 review (2026-10-18): the transition
/// altimeters push STD only from a reading that is not 29.92 and take back only their own push;
/// flaps 1 is no takeoff setting; LNAV/VNAV and the two-switch writes are read back; a running
/// engine is not started again; gear up only airborne; the recirculation fans and turnoff lights
/// through the variant's and the panel's own switches; Secure keeps the battery on until the APU has
/// stopped; a queued press never reaches the next aircraft; one weather radar test at a time.
/// Structure through the real Build() definitions, behaviour on a real FlowManager and executor
/// where no CDA snapshot is needed (a test cannot supply one), pure logic through the helpers.
/// </summary>
public class Pmdg777SecondReviewTests
{
    private static FlowStep<AircraftStateEvaluator> Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private static List<string> StepIds(string flowId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Select(s => s.Id).ToList();

    private static ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Line(string groupId, string itemId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    /// <summary>An evaluator that reads as available with no CDA snapshot (every CDA field NaN), whose
    /// SimConnect cache holds <paramref name="cache"/>.</summary>
    private static AircraftStateEvaluator Available(params (string Key, double Value)[] cache)
    {
        var eval = new AircraftStateEvaluator();
        eval.SetDataManager(new PMDG777DataManager());
        var values = cache.ToDictionary(c => c.Key, c => c.Value);
        eval.SetCachedValueSource(key => values.TryGetValue(key, out double v) ? v : null);
        return eval;
    }

    // ==================================================================================
    // 1. Transition altimeters: 29.92 is no proof of STD
    // ==================================================================================

    [Fact]
    public void Climbing_from_a_QNH_pushes_both_STD_buttons_and_remembers_the_push()
    {
        var std = new Pmdg777TransitionStd();
        var d = std.OnClimb(captainReads2992: false);
        Assert.True(d.PushBoth);
        Assert.Equal("Transition altitude. Altimeters set to standard.", d.Speech);
        Assert.True(std.PushedStandard);
    }

    // The reported case: a QNH of 29.92 (US standard day, MSFS preset weather) read as STD, nothing
    // pushed, and "Altimeters set to standard" spoken.
    [Fact]
    public void Climbing_at_29_92_pushes_nothing_and_never_claims_standard_was_set()
    {
        var std = new Pmdg777TransitionStd();
        var d = std.OnClimb(captainReads2992: true);
        Assert.False(d.PushBoth);
        Assert.Equal("Transition altitude. The captain's altimeter already reads standard, nothing pushed.", d.Speech);
        Assert.DoesNotContain("set to standard", d.Speech);
        Assert.False(std.PushedStandard);
    }

    [Fact]
    public void Climbing_over_an_unread_altimeter_only_reminds()
    {
        var std = new Pmdg777TransitionStd();
        var d = std.OnClimb(captainReads2992: null);
        Assert.False(d.PushBoth);
        Assert.Equal(TransitionAltimeters.AltitudeReminder, d.Speech);
        Assert.False(std.PushedStandard);
    }

    [Fact]
    public void The_descent_takes_back_its_own_push()
    {
        var std = new Pmdg777TransitionStd();
        std.OnClimb(false);
        var d = std.OnDescent(captainReads2992: true);
        Assert.True(d.PushBoth);
        Assert.Equal("Transition level. Altimeters set to local QNH. Set local pressure now.", d.Speech);
        Assert.False(std.PushedStandard);
    }

    // The reported case: descending with 29.92 that the FO never pushed (a QNH of 29.92, or STD set
    // by hand) pushed both sides INTO standard while saying "set to local QNH". Now the 737/iFly way.
    [Fact]
    public void A_descent_with_no_push_of_its_own_only_reminds()
    {
        var std = new Pmdg777TransitionStd();
        std.OnClimb(true);                      // 29.92 on the climb: nothing pushed
        var d = std.OnDescent(captainReads2992: true);
        Assert.False(d.PushBoth);
        Assert.Equal("Transition level. Set local altimeter pressure now.", d.Speech);
        Assert.Equal(TransitionAltimeters.LevelReminder, d.Speech);
    }

    [Fact]
    public void A_descent_after_the_pilot_left_standard_pushes_nothing()
    {
        var std = new Pmdg777TransitionStd();
        std.OnClimb(false);
        var d = std.OnDescent(captainReads2992: false);   // a QNH set by hand: a push would re-engage STD
        Assert.False(d.PushBoth);
        Assert.Equal(TransitionAltimeters.LevelReminder, d.Speech);
        Assert.False(std.PushedStandard);
    }

    [Fact]
    public void A_descent_over_an_unread_altimeter_pushes_nothing_and_forgets_the_push()
    {
        var std = new Pmdg777TransitionStd();
        std.OnClimb(false);
        var d = std.OnDescent(captainReads2992: null);
        Assert.False(d.PushBoth);
        Assert.Equal(TransitionAltimeters.LevelReminder, d.Speech);
        Assert.False(std.PushedStandard);
        Assert.False(std.OnDescent(true).PushBoth);
    }

    [Fact]
    public void A_push_is_taken_back_once()
    {
        var std = new Pmdg777TransitionStd();
        std.OnClimb(false);
        Assert.True(std.OnDescent(true).PushBoth);
        Assert.False(std.OnDescent(true).PushBoth);
    }

    [Fact]
    public void Reset_forgets_the_push()
    {
        var std = new Pmdg777TransitionStd();
        std.OnClimb(false);
        std.Reset();
        Assert.False(std.PushedStandard);
        Assert.False(std.OnDescent(true).PushBoth);
    }

    // A climb back above the transition altitude with no descent crossing between (an origin
    // altitude above the destination level): its own push still stands at 29.92 and is kept.
    [Fact]
    public void A_second_climb_at_29_92_keeps_the_push_it_made()
    {
        var std = new Pmdg777TransitionStd();
        std.OnClimb(false);
        Assert.False(std.OnClimb(true).PushBoth);
        Assert.True(std.PushedStandard);
        Assert.True(std.OnDescent(true).PushBoth);
    }

    // Everything spoken here is fixed text: a comma-decimal culture must not change a word of it.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_altimeter_calls_are_the_same_under_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var std = new Pmdg777TransitionStd();
            Assert.Equal(Pmdg777TransitionStd.AlreadyStandardText, std.OnClimb(true).Speech);
            Assert.Equal(Pmdg777TransitionStd.StandardSetText, std.OnClimb(false).Speech);
            Assert.Equal(Pmdg777TransitionStd.BackToQnhText, std.OnDescent(true).Speech);
            Assert.Equal(TransitionAltimeters.LevelReminder, std.OnDescent(true).Speech);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    // The 777 monitor needs a connected executor and a ScreenReaderAnnouncer to run, so its wiring is
    // pinned by its source: the STD buttons are pushed only on the helper's decision, and the push is
    // forgotten on Reset and at a transition level crossed with the altimeter setting off.
    [Fact]
    public void The_777_monitor_pushes_STD_only_on_the_helpers_decision()
    {
        string src = StripComments(File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
            "MSFSBlindAssist", "FirstOfficer", "FlightPhaseMonitor.cs")));
        Assert.Contains("_std.OnClimb(_state.IsBaroStdCapt())", src);
        Assert.Contains("_std.OnDescent(_state.IsBaroStdCapt())", src);
        Assert.Equal(2, Regex.Matches(src, @"_std\.Reset\(\)").Count);
        Assert.Single(Regex.Matches(src, @"_executor\.PushBaroSTDCapt\(\)"));
        Assert.Single(Regex.Matches(src, @"_executor\.PushBaroSTDFO\(\)"));
        Assert.Contains("if (d.PushBoth)", src);
    }

    private static string StripComments(string src)
        => Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", "");

    // ==================================================================================
    // 2. Flaps 1 is not a 777 takeoff setting
    // ==================================================================================

    [Fact]
    public void A_lever_at_flaps_1_is_not_set_for_takeoff_anywhere()
    {
        foreach (var (flow, step) in new[] { ("BEFORE_TAXI", "BT_FLAPS_CHECK"), ("BEFORE_TAKEOFF", "BTKOF_FLAPS_CHECK") })
        {
            var check = Step(flow, step);
            Assert.False(check.Condition!(1), $"{step} confirmed flaps 1");
            Assert.True(check.Condition!(2) && check.Condition!(3) && check.Condition!(4), $"{step} at flaps 5, 15 or 20");
        }
        Assert.False(Line("BEFORE_TKOF_CL", "BTKOF_FLAPS").EvaluateState(1));
        Assert.False(AircraftStateEvaluator.IsTakeoffFlapLever(1));
    }

    // ==================================================================================
    // 3. LNAV and VNAV are read back
    // ==================================================================================

    [Theory]
    [InlineData("BTKOF_LNAV", "MCP_annunLNAV", "BTKO_LNAV")]
    [InlineData("BTKOF_VNAV", "MCP_annunVNAV", "BTKO_VNAV")]
    public void Verify_armed_reads_the_annunciator_back_after_the_press(string stepId, string annunciator, string lineId)
    {
        var step = Step("BEFORE_TAKEOFF", stepId);
        Assert.Equal(annunciator, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(1));
        Assert.False(step.VerifyCondition!(0), "a press the FMC rejected is not armed");
        Assert.False(step.VerifyCondition!(double.NaN));
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);   // heard skipped, kept out of the latch
        Assert.Equal(new[] { lineId }, step.LinkedChecklistItemIds.ToArray());
        // Still a toggle pressed only over a read annunciator.
        Assert.True(step.LeaveAloneWhen!(new AircraftStateEvaluator()));
    }

    // ==================================================================================
    // 4. A running engine is not started again
    // ==================================================================================

    [Theory]
    [InlineData(80, 1, true)]
    [InlineData(50, 1, true)]          // EngineRunningN2 itself
    [InlineData(49.9, 1, false)]
    [InlineData(80, 0, false)]         // coasting after a cutoff: N2 but no RUN
    [InlineData(double.NaN, 1, false)]
    [InlineData(80, double.NaN, false)]
    public void Running_is_idle_N2_with_the_fuel_control_at_RUN(double n2, double fuelControlRun, bool running)
        => Assert.Equal(running, AircraftStateEvaluator.EngineRunning(n2, fuelControlRun));

    [Fact]
    public void An_engine_whose_fuel_control_is_unread_is_not_running()
    {
        var eval = Available();
        eval.SetEngineN2(80, 80);
        Assert.False(eval.IsEngineRunning(1));
        Assert.False(eval.IsEngineRunning(2));
        // So both START steps are sent, never "Already set", over data that has not arrived.
        Assert.False(Step("ENGINE_START", "ES_ENG1_START").SkipCondition!(eval));
        Assert.False(Step("ENGINE_START", "ES_ENG2_START").SkipCondition!(eval));
    }

    [Theory]
    [InlineData(2, "ES_ENG2_START", "ES_E2_WAIT1", "ES_FC2_RUN", "ES_E2_VALVE_WAIT", "ES_ENG2_RUNNING", "ES_ENG2_STABLE", "ES_ENG2_SETTLE")]
    [InlineData(1, "ES_ENG1_START", "ES_E1_WAIT1", "ES_FC1_RUN", "ES_E1_VALVE_WAIT", "ES_ENG1_RUNNING", "ES_ENG1_STABLE", "ES_ENG1_SETTLE")]
    public void A_running_engines_start_block_passes_without_a_write(int engine, string start, string crank,
        string fuel, string lightOff, string running, string valve, string settle)
    {
        // START: "Already set" over a running engine, and still a Stop gate for a start that fails.
        var startStep = Step("ENGINE_START", start);
        Assert.NotNull(startStep.SkipCondition);
        Assert.Equal(FlowStepFailurePolicy.Stop, startStep.FailurePolicy);

        // The fixed waits follow only a START this run sent: silent for a running engine.
        foreach (var wait in new[] { crank, lightOff, settle })
        {
            var w = Step("ENGINE_START", wait);
            Assert.Equal(FlowStepActionType.WaitSeconds, w.ActionType);
            Assert.Equal(start, w.RunOnlyIfStepSent);
        }

        // Fuel control: "Already set" at RUN (never over an unread switch), still a Stop gate.
        var fuelStep = Step("ENGINE_START", fuel);
        Assert.False(fuelStep.SkipCondition!(new AircraftStateEvaluator()));
        Assert.Equal(FlowStepFailurePolicy.Stop, fuelStep.FailurePolicy);

        // The running and start-valve waits read the engine: they pass at once over a running one,
        // and stay unconditional Stop gates (a starter still engaged is never started over).
        foreach (var read in new[] { running, valve })
        {
            var r = Step("ENGINE_START", read);
            Assert.Equal(FlowStepActionType.WaitForCondition, r.ActionType);
            Assert.Null(r.RunOnlyIfStepSent);
            Assert.Equal(FlowStepFailurePolicy.Stop, r.FailurePolicy);
        }
        Assert.True(Step("ENGINE_START", running).Condition!(AircraftStateEvaluator.EngineRunningN2));
        Assert.Equal($"FO_ENG{engine}_N2", Step("ENGINE_START", running).ConditionFieldName);
    }

    // ==================================================================================
    // 5. Gear UP only when airborne
    // ==================================================================================

    [Theory]
    [InlineData(1.0, true, false)]
    [InlineData(0.0, false, true)]
    [InlineData(double.NaN, null, false)]
    public void Sim_on_ground_is_read_from_the_cache(double simOnGround, bool? onGround, bool airborne)
    {
        var eval = Available((AircraftStateEvaluator.OnGroundKey, simOnGround));
        Assert.Equal(onGround, eval.IsOnGround());
        Assert.Equal(airborne, eval.IsDefinitelyAirborne());
    }

    [Fact]
    public void An_unread_air_ground_state_is_not_airborne()
    {
        var eval = Available();
        Assert.Null(eval.IsOnGround());
        Assert.False(eval.IsDefinitelyAirborne());
    }

    [Fact]
    public void The_gear_up_step_and_line_leave_the_lever_alone_unless_airborne()
    {
        var step = Step("AFTER_TAKEOFF", "ATKOF_GEAR_UP");
        var line = Line("AFTER_TAKEOFF", "ATKO_GEAR_UP");
        Assert.Equal(AircraftStateEvaluator.GearUpLeaveAloneText, step.LeaveAloneText);
        Assert.Equal(AircraftStateEvaluator.GearUpLeaveAloneText, line.LeaveAloneText);
        foreach (var (eval, leftAlone) in new[]
        {
            (Available((AircraftStateEvaluator.OnGroundKey, 1)), true),
            (Available(), true),
            (Available((AircraftStateEvaluator.OnGroundKey, 0)), false),
        })
        {
            Assert.Equal(leftAlone, step.LeaveAloneWhen!(eval));
            Assert.Equal(leftAlone, line.LeaveAloneWhen!(eval));
        }
    }

    [Fact]
    public async Task On_the_ground_the_after_takeoff_flow_says_why_and_leaves_the_gear_down()
    {
        var run = await Pmdg777FlowRunTests.RunAsync(Available((AircraftStateEvaluator.OnGroundKey, 1)),
            "AFTER_TAKEOFF", "ATKOF_GEAR_UP");

        Assert.Equal(new[]
        {
            "After Takeoff flow started",
            "Not airborne, so the gear stays down.",
            "After Takeoff flow complete",
        }, run.Speech);
        Assert.Equal(new[] { "ATKO_GEAR_UP" }, run.Unfinished);
        Assert.False(run.Line("AFTER_TAKEOFF", "ATKO_GEAR_UP").IsChecked);
    }

    [Fact]
    public void On_the_ground_a_tick_of_gear_up_is_refused_with_its_reason()
    {
        var state = Available((AircraftStateEvaluator.OnGroundKey, 1));
        var groups = PMDG777ChecklistDefinitions.Build();
        var checklist = new ChecklistManager<AircraftActionExecutor, AircraftStateEvaluator>(
            state, new AircraftActionExecutor(), groups);

        Assert.False(checklist.ToggleItem("AFTER_TAKEOFF", "ATKO_GEAR_UP", out string? why));
        Assert.Equal(AircraftStateEvaluator.GearUpLeaveAloneText, why);
        Assert.False(groups.Single(g => g.Id == "AFTER_TAKEOFF").Items.Single(i => i.Id == "ATKO_GEAR_UP").IsChecked);
    }

    // Every caller, not only the flow step and the line: the executor refuses gear UP unless SIM ON
    // GROUND reads airborne. The manager here is never connected for real, so a SENT write goes
    // nowhere; the result says whether it was sent.
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(1.0, false)]
    [InlineData(double.NaN, false)]
    public async Task The_executor_sends_gear_up_only_when_airborne(double simOnGround, bool sent)
    {
        var exec = new AircraftActionExecutor();
        exec.SetSimConnect(double.IsNaN(simOnGround)
            ? SeededSimConnectCache.ConnectedWith()
            : SeededSimConnectCache.ConnectedWith((AircraftStateEvaluator.OnGroundKey, simOnGround)));
        Assert.Equal(sent, await exec.ExecuteStepAsync(Step("AFTER_TAKEOFF", "ATKOF_GEAR_UP")));
    }

    [Fact]
    public async Task Gear_DOWN_is_never_refused_on_the_ground()
    {
        var exec = new AircraftActionExecutor();
        exec.SetSimConnect(SeededSimConnectCache.ConnectedWith((AircraftStateEvaluator.OnGroundKey, 1)));
        Assert.True(await exec.ExecuteStepAsync(Step("ELECTRICAL_POWER_UP", "EPU_GEAR_DOWN")));
    }

    // ==================================================================================
    // 6. The fuel control CUTOFF reads both switches back
    // ==================================================================================

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(1.0, 0.0, 1.0)]
    [InlineData(0.0, 1.0, 1.0)]
    [InlineData(1.0, 1.0, 2.0)]
    [InlineData(double.NaN, 0.0, double.NaN)]
    [InlineData(0.0, double.NaN, double.NaN)]
    public void Count_on_counts_both_and_is_unknown_while_either_is(double a, double b, double expected)
    {
        var values = new Dictionary<string, double> { ["A"] = a, ["B"] = b };
        AssertSame(expected, AircraftStateEvaluator.CountOn(f => values[f], "A", "B"));
    }

    // NaN (unknown) must stay NaN; anything else the exact value.
    private static void AssertSame(double expected, double actual)
    {
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual), $"expected unknown (NaN), got {actual}");
        else Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("SHUTDOWN", "SD_FC_CUTOFF")]
    [InlineData("COCKPIT_PREP", "CP_FUEL_CTRL")]
    public void Fuel_control_cutoff_is_verified_on_both_switches(string flowId, string stepId)
    {
        var step = Step(flowId, stepId);
        Assert.Equal(new (string, int?)[]
        {
            ("EVT_CONTROL_STAND_ENG1_START_LEVER", 1), ("EVT_CONTROL_STAND_ENG2_START_LEVER", 1),
        }, step.MultiActions.ToArray());
        Assert.Equal(AircraftStateEvaluator.FuelControlsAtRunField, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(1), "engine 2 (or 1) still at RUN");
        Assert.False(step.VerifyCondition!(2));
        Assert.False(step.VerifyCondition!(double.NaN));
    }

    [Fact]
    public void The_two_switch_synthetics_are_unknown_before_the_first_snapshot()
    {
        var eval = Available();
        Assert.True(double.IsNaN(eval.GetValue(AircraftStateEvaluator.FuelControlsAtRunField)));
        Assert.True(double.IsNaN(eval.GetValue(AircraftStateEvaluator.RunwayTurnoffLightsOnField)));
        Assert.True(double.IsNaN(eval.GetValue(Pmdg777RecircFans.OnField)));
    }

    // ==================================================================================
    // 7. Recirculation fans through the variant's own switches
    // ==================================================================================

    [Theory]
    [InlineData(2)]   // -200ER
    [InlineData(4)]   // -200LR
    [InlineData(6)]   // -300ER
    [InlineData(1)]
    [InlineData(3)]
    public void A_passenger_777_writes_upper_and_lower(int model)
        => Assert.Equal(new (string, int?)[]
            {
                ("EVT_OH_AIRCOND_RECIRC_FAN_UPP_SWITCH", 1), ("EVT_OH_AIRCOND_RECIRC_FAN_LWR_SWITCH", 1),
            }, Pmdg777RecircFans.Writes(model, 1).ToArray());

    [Fact]
    public void The_777F_writes_its_single_switch()
        => Assert.Equal(new (string, int?)[] { ("EVT_OH_AIRCOND_RECIRC_FANS_SWITCH", 0) },
            Pmdg777RecircFans.Writes(Pmdg777RecircFans.FreighterModel, 0).ToArray());

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(7)]
    public void An_unknown_variant_writes_nothing(double model)
        => Assert.Empty(Pmdg777RecircFans.Writes(model, 1));

    [Fact]
    public void Every_recirculation_event_is_a_777_event()
    {
        foreach (var ev in new[] { Pmdg777RecircFans.UpperEvent, Pmdg777RecircFans.LowerEvent, Pmdg777RecircFans.FreighterEvent })
            Assert.True(PMDG777Definition.EventIds.ContainsKey(ev), ev);
        Assert.False(PMDG777Definition.EventIds.ContainsKey(Pmdg777RecircFans.PseudoKey));
    }

    [Theory]
    [InlineData(6, 1, 1, 1.0)]
    [InlineData(6, 1, 0, 0.0)]          // passenger: LOWER still off
    [InlineData(6, 0, 1, 0.0)]
    [InlineData(5, 1, 0, 1.0)]          // 777F: its one switch moves at least one reading
    [InlineData(5, 0, 1, 1.0)]
    [InlineData(5, 0, 0, 0.0)]
    [InlineData(0, 1, 1, double.NaN)]   // unknown variant
    [InlineData(double.NaN, 1, 1, double.NaN)]
    [InlineData(6, double.NaN, 1, double.NaN)]
    public void The_fans_are_on_as_the_variant_has_them(double model, double upper, double lower, double expected)
    {
        var values = new Dictionary<string, double>
        {
            [Pmdg777RecircFans.ModelField] = model,
            [Pmdg777RecircFans.UpperField] = upper,
            [Pmdg777RecircFans.LowerField] = lower,
        };
        AssertSame(expected, Pmdg777RecircFans.OnValue(f => values[f]));
    }

    [Fact]
    public void Cockpit_prep_sets_and_reads_back_the_variants_fans_and_the_line_reads_the_same()
    {
        var step = Step("COCKPIT_PREP", "CP_RECIRC_FANS");
        Assert.Equal(Pmdg777RecircFans.PseudoKey, step.EventName);
        Assert.Equal(1, step.TargetValue);
        Assert.Equal(Pmdg777RecircFans.OnField, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(1));
        Assert.False(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(double.NaN));
        Assert.Equal(new[] { "PF_RECIRC_FANS" }, step.LinkedChecklistItemIds.ToArray());

        var line = Line("PREFLIGHT", "PF_RECIRC_FANS");
        Assert.Equal(Pmdg777RecircFans.OnField, line.StateFieldName);
        Assert.Empty(line.AdditionalStateFields);
        Assert.True(line.EvaluateState(1));
        Assert.False(line.EvaluateState(0));
    }

    [Fact]
    public async Task Before_the_CDA_names_the_variant_no_recirculation_switch_is_written()
    {
        var exec = new AircraftActionExecutor();
        exec.SetSimConnect(SeededSimConnectCache.ConnectedWith());   // no PMDG data manager: no model
        Assert.False(exec.SetRecircFans(1));
        Assert.False(await exec.SetRecircFansAsync(1));
        Assert.False(await exec.ExecuteStepAsync(Step("COCKPIT_PREP", "CP_RECIRC_FANS")));
    }

    // ==================================================================================
    // 8. Runway turnoff lights through the per-side events, read back on both
    // ==================================================================================

    [Theory]
    [InlineData("BEFORE_TAKEOFF", "BTKOF_TURNOFF", 1, "BTKO_TURNOFF")]
    [InlineData("AFTER_TAKEOFF", "ATKOF_TURNOFF_OFF", 0, "ATKO_TURNOFF_OFF")]
    [InlineData("AFTER_LANDING", "AL_TURNOFF_OFF", 0, "AL_EXT_LIGHTS")]
    public void The_turnoff_steps_write_each_side_and_read_both_back(string flowId, string stepId, int target, string lineId)
    {
        var step = Step(flowId, stepId);
        Assert.Equal(FlowStepActionType.SetSwitchMultiple, step.ActionType);
        Assert.Equal(new (string, int?)[] { ("EVT_OH_LIGHTS_L_TURNOFF", target), ("EVT_OH_LIGHTS_R_TURNOFF", target) },
            step.MultiActions.ToArray());
        Assert.Equal(AircraftStateEvaluator.RunwayTurnoffLightsOnField, step.VerifyFieldName);
        double both = target == 1 ? 2 : 0;
        Assert.True(step.VerifyCondition!(both));
        Assert.False(step.VerifyCondition!(1), "one light on, one off");
        Assert.False(step.VerifyCondition!(double.NaN));
        Assert.Equal(new[] { lineId }, step.LinkedChecklistItemIds.ToArray());
        Assert.NotNull(step.SkipCondition);
    }

    [Fact]
    public void No_flow_writes_the_ganged_turnoff_event()
    {
        var written = PMDG777FlowDefinitions.Build().SelectMany(f => f.Steps)
            .SelectMany(s => s.EventName is null ? s.MultiActions.Select(a => a.EventName) : new[] { s.EventName });
        Assert.DoesNotContain("EVT_OH_LIGHTS_LR_TURNOFF", written);
        Assert.True(PMDG777Definition.EventIds.ContainsKey("EVT_OH_LIGHTS_L_TURNOFF"));
        Assert.True(PMDG777Definition.EventIds.ContainsKey("EVT_OH_LIGHTS_R_TURNOFF"));
    }

    // ==================================================================================
    // 9. Secure: the cool-down only after a switch-off it sent, the battery only once the APU stopped
    // ==================================================================================

    [Fact]
    public void Secure_waits_for_the_APU_to_stop_before_the_battery_in_PMDGs_order()
    {
        var ids = StepIds("SECURE");
        int apuOff = ids.IndexOf("SEC_APU_OFF"), cool = ids.IndexOf("SEC_APU_WAIT"),
            gpu1 = ids.IndexOf("SEC_GND_PWR_PRIM"), gpu2 = ids.IndexOf("SEC_GND_PWR_SEC"),
            stopped = ids.IndexOf("SEC_APU_STOPPED"), battery = ids.IndexOf("SEC_BATTERY_OFF");
        Assert.True(apuOff >= 0 && apuOff < cool && cool < gpu1 && gpu1 < gpu2 && gpu2 < stopped && stopped < battery);
        Assert.Equal(ids.Count - 1, battery);   // PMDG's Electrical Power Down ends with the battery

        Assert.False(Step("SECURE", "SEC_APU_OFF").SkipCondition!(new AircraftStateEvaluator()),
            "an unread selector is not OFF: the switch-off is sent");
        Assert.Equal("SEC_APU_OFF", Step("SECURE", "SEC_APU_WAIT").RunOnlyIfStepSent);

        var wait = Step("SECURE", "SEC_APU_STOPPED");
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal("APURunning", wait.ConditionFieldName);
        Assert.True(wait.Condition!(0));
        Assert.False(wait.Condition!(1));
        Assert.False(wait.Condition!(double.NaN));
        Assert.Equal(FlowStepFailurePolicy.Skip, wait.FailurePolicy);
        Assert.Null(wait.RunOnlyIfStepSent);   // it reads the APU itself
        Assert.Empty(wait.LinkedChecklistItemIds);

        var bat = Step("SECURE", "SEC_BATTERY_OFF");
        Assert.Equal("SEC_APU_STOPPED", bat.RequiresStepId);
        Assert.Equal("APU still running, so the battery stays on.", bat.RequiresStepSkipText);
    }

    // No sim: the APU switch-off cannot be sent, so its cool-down passes silently (no 30 s wait: the
    // harness fails a run over 20 s), and the APU never reads stopped, so the battery stays on.
    [Fact]
    public async Task Secure_runs_no_cool_down_it_did_not_cause_and_keeps_the_battery_on_over_a_running_APU()
    {
        var run = await Pmdg777FlowRunTests.RunAsync(Available(), "SECURE",
            "SEC_APU_OFF", "SEC_APU_WAIT", "SEC_APU_STOPPED", "SEC_BATTERY_OFF");

        Assert.DoesNotContain(run.Speech, s => s.Contains("APU cooling down", StringComparison.Ordinal));
        Assert.Contains("Timed out waiting for: APU to stop", run.Speech);
        Assert.Contains("APU still running, so the battery stays on.", run.Speech);
        Assert.Contains("EPD_BATTERY_OFF", run.Unfinished);
        Assert.False(run.Line("ELEC_POWER_DOWN", "EPD_BATTERY_OFF").IsChecked);
    }

    // ==================================================================================
    // 10. A queued press never reaches the next aircraft; the emergency lights share the panel's gate
    // ==================================================================================

    [Fact]
    public async Task A_press_queued_behind_the_gate_is_refused_once_the_executor_is_detached()
    {
        var exec = new AircraftActionExecutor();
        exec.SetSimConnect(SeededSimConnectCache.ConnectedWith());

        // The first press goes out at once and holds the dispatch gate for its hold.
        var first = exec.HeldTransmitTestAsync("EVT_TCAS_TEST", 500);
        var queued = exec.HeldTransmitTestAsync("EVT_TCAS_TEST", AircraftActionExecutor.QuickTestHoldMs);
        Assert.False(queued.IsCompleted);

        exec.SetSimConnect(null);   // the window closed: an aircraft switch

        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(10)), "a press already sent is released and completes");
        Assert.False(await queued.WaitAsync(TimeSpan.FromSeconds(10)), "the queued press went out after the detach");
    }

    [Fact]
    public async Task The_emergency_light_sequence_waits_for_the_panels_own_sequence()
    {
        var exec = new AircraftActionExecutor();
        exec.SetSimConnect(SeededSimConnectCache.ConnectedWith());
        var panelGate = PMDG777Definition.EmerLightsSequenceGate;

        Task<bool>? fo = null;
        await panelGate.WaitAsync();   // a panel sequence mid-gap
        try
        {
            fo = exec.SetEmerExitLightsAsync(EmerExitLightSequence.Off);
            await Task.Delay(400);
            Assert.False(fo.IsCompleted, "the First Officer's sequence ran inside the panel's");
        }
        finally { panelGate.Release(); }

        // No PMDG data manager here: once inside, there is no position to read and nothing is sent.
        Assert.False(await fo!.WaitAsync(TimeSpan.FromSeconds(10)));
        await exec.WaitForDispatchDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ==================================================================================
    // 11. The panel's weather radar test: one at a time, on the caller's context
    // ==================================================================================

    [Fact]
    public void A_second_weather_radar_test_press_during_the_test_is_refused_aloud()
    {
        var def = new PMDG777Definition();
        var varDef = def.GetVariables()["WXR_Test"];
        var sc = SeededSimConnectCache.With();       // never connected: the presses go nowhere
        var speech = new GatedSpeechCapture();

        Assert.True(def.HandleUIVariableSet("WXR_Test", 1, varDef, sc, speech));
        Assert.Empty(speech.All);                    // the test announces itself in the sim
        Assert.True(def.HandleUIVariableSet("WXR_Test", 1, varDef, sc, speech));
        Assert.Equal(new[] { "Weather radar test still running, please wait." }, speech.All);
    }

    // SIM-20 / P777-13: a sequence started on the UI thread must resume there, never on a pool thread.
    [Fact]
    public void The_panel_test_sequences_never_leave_the_callers_context()
    {
        string src = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
            "MSFSBlindAssist", "Aircraft", "PMDG777Definition.cs"));
        int start = src.IndexOf("private static async Task HeldTransmitAsync(", StringComparison.Ordinal);
        int end = src.IndexOf("private Task? _wxrTestOp;", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the held-transmit helpers moved: re-point this scan");
        Assert.DoesNotContain("ConfigureAwait(false)", StripComments(src[start..end]));
    }
}
