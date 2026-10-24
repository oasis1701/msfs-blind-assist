using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.FirstOfficer.A300;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

public class A300FoFlowStructureTests
{
    private static readonly List<FlowDefinition<A300FoStateEvaluator>> Flows = A300FoFlowDefinitions.Build();

    private static FlowDefinition<A300FoStateEvaluator> Flow(string id) => Flows.Single(f => f.Id == id);
    private static string[] Labels(string id) => Flow(id).Steps.Select(s => s.Label).ToArray();

    [Fact]
    public void Fourteen_flows_in_flight_order() => Assert.Equal(new[]
    {
        "POWER_UP", "PREFLIGHT", "BEFORE_START", "ENGINE_START", "AFTER_START", "BEFORE_TAKEOFF", "LINE_UP",
        "AFTER_TAKEOFF", "DESCENT", "APPROACH", "LANDING", "AFTER_LANDING", "PARKING", "SECURING",
    }, Flows.Select(f => f.Id));

    [Fact]
    public void No_flow_names_a_step_or_a_line_twice()
    {
        foreach (var f in Flows)
        {
            Assert.Equal(f.Steps.Count, f.Steps.Select(s => s.Id).Distinct().Count());
            Assert.Equal(f.Steps.Count, f.Steps.Select(s => s.Label).Distinct().Count());
        }
        var all = Flows.SelectMany(f => f.Steps).Select(s => s.Id).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Every_written_key_is_a_row_a_press_or_a_sequence()
    {
        var known = A300FoKeys.SingleRows.Concat(A300FoKeys.PressConfirmations.Keys)
            .Concat(A300FoKeys.AllAt.Values.SelectMany(w => w).Select(w => w.Key))
            .Concat(A300FoSystemTests.ByKey.Keys)
            .Concat(new[] { A300FoKeys.LightUp1, A300FoKeys.LightUp2, A300FoKeys.AnnunciatorTest, A300FoKeys.WeatherRadarTest })
            .ToHashSet();
        foreach (var s in Flows.SelectMany(f => f.Steps))
        {
            if (s.EventName != null) Assert.Contains(s.EventName, known);
            foreach (var (key, _) in s.MultiActions) Assert.Contains(key, known);
        }
    }

    [Theory]
    [InlineData("POWER_UP")] [InlineData("SECURING")]
    public void Cold_and_dark_flows_stop_before_any_switch_unless_on_the_ground_with_the_engines_stopped(string id)
    {
        var steps = Flow(id).Steps;
        int firstWrite = steps.FindIndex(s => s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple);
        foreach (var field in new[] { A300FoKeys.EnginesStopped, A300FoKeys.OnGround })
        {
            int guard = steps.FindIndex(s => s.ConditionFieldName == field);
            Assert.True(guard >= 0 && guard < firstWrite, field);
            Assert.Equal(FlowStepFailurePolicy.Stop, steps[guard].FailurePolicy);
        }
    }

    [Fact]
    public void Engine_start_is_engine_2_then_engine_1_and_a_light_up_failure_stops_before_fuel()
    {
        var steps = Flow("ENGINE_START").Steps;
        int lightUp2 = steps.FindIndex(s => s.EventName == A300FoKeys.LightUp2);
        int fuel2 = steps.FindIndex(s => s.EventName == A300FoKeys.FuelLever2);
        int lightUp1 = steps.FindIndex(s => s.EventName == A300FoKeys.LightUp1);
        Assert.True(lightUp2 < fuel2 && fuel2 < lightUp1);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[lightUp2].FailurePolicy);
    }

    [Fact]
    public void The_takeoff_flaps_are_the_captains_and_never_written_before_flight()
    {
        foreach (var id in new[] { "POWER_UP", "PREFLIGHT", "BEFORE_START", "ENGINE_START", "AFTER_START", "BEFORE_TAKEOFF", "LINE_UP" })
            Assert.DoesNotContain(Flow(id).Steps, s => s.EventName == A300FoKeys.Flaps);
        Assert.Contains(Flow("AFTER_START").Steps, s => s.ActionType == FlowStepActionType.CaptainReminder && s.Label == "Flaps: set for takeoff");
    }

    [Fact]
    public void Flaps_up_is_left_alone_off_the_ground()
    {
        var flaps = Flow("AFTER_LANDING").Steps.Single(s => s.EventName == A300FoKeys.Flaps);
        Assert.Equal(0, flaps.TargetValue);
        var airborne = new A300FoStateEvaluator { Read = k => k == A300FoKeys.OnGround ? 0 : null };
        Assert.True(flaps.LeaveAloneWhen!(airborne));
        var unread = new A300FoStateEvaluator { Read = _ => null };
        Assert.True(flaps.LeaveAloneWhen!(unread));
    }

    [Fact]
    public void An_unread_field_is_never_already_set()
    {
        var unread = new A300FoStateEvaluator { Read = _ => null };
        foreach (var s in Flows.SelectMany(f => f.Steps).Where(s => s.SkipCondition != null))
            Assert.False(s.SkipCondition!(unread), s.Id);
    }

    [Fact]
    public void Each_gear_wait_is_the_last_step_of_its_flow_and_never_writes()
    {
        foreach (var (flow, field) in new[] { ("AFTER_TAKEOFF", A300FoKeys.GearUp), ("LANDING", A300FoKeys.GearDown) })
        {
            var last = Flow(flow).Steps.Last();
            Assert.Equal(FlowStepActionType.WaitForCondition, last.ActionType);
            Assert.Equal(field, last.ConditionFieldName);
        }
    }

    [Fact]
    public void The_landing_autobrake_is_never_written_and_its_reminder_names_the_panel()
    {
        var abrk = new[] { "A300_AUTO_BRK_LO", "A300_AUTO_BRK_MID", A300FoKeys.AutobrakeMax };
        foreach (var id in new[] { "DESCENT", "APPROACH", "LANDING" })
            Assert.DoesNotContain(Flow(id).Steps, s => s.EventName != null && abrk.Contains(s.EventName));
        var reminder = Flow("DESCENT").Steps.Single(s => s.Id == "DS_CPT_AUTOBRAKE");
        Assert.Contains("centre instrument panel", reminder.ReminderText);
        Assert.DoesNotContain("low", reminder.ReminderText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preflight_runs_every_test() => Assert.Subset(Labels("PREFLIGHT").ToHashSet(), new HashSet<string>
    {
        "Engine 1 fire test", "Cargo smoke test", "Engine 2 fire test", "GPWS test", "Landing gear warning test",
        "Takeoff warning test", "Weather radar test, then off",
    });

    [Fact]
    public void Brake_fans_follow_the_inibuilds_checklist()
    {
        // Flight deck preparation and after landing: "As Required", the pilot's call; line-up: off;
        // parking: off only with both brakes below 100 degrees C (iniBuilds' own test).
        Assert.Contains("Set the brake fans as required", Labels("PREFLIGHT"));
        Assert.Contains("Turn on the brake fans if required", Labels("AFTER_LANDING"));
        Assert.Equal(0, Flow("LINE_UP").Steps.Single(s => s.EventName == A300FoKeys.BrakeFan).TargetValue);
        var parking = Flow("PARKING").Steps.Single(s => s.EventName == A300FoKeys.BrakeFan);
        Assert.Equal(0, parking.TargetValue);
        var hot = new A300FoStateEvaluator { Read = k => k == A300FoVariables.BrakeTempLeftKey ? 140 : k == A300FoVariables.BrakeTempRightKey ? 90 : null };
        Assert.True(parking.LeaveAloneWhen!(hot));
        var cool = new A300FoStateEvaluator { Read = k => k == A300FoVariables.BrakeTempLeftKey ? 80 : k == A300FoVariables.BrakeTempRightKey ? 90 : null };
        Assert.False(parking.LeaveAloneWhen!(cool));
        Assert.DoesNotContain(Flow("AFTER_LANDING").Steps, s => s.EventName == A300FoKeys.BrakeFan);
    }

    [Fact]
    public void Securing_waits_for_the_APU_to_wind_down_between_its_master_and_the_batteries()
    {
        var steps = Flow("SECURING").Steps;
        int master = steps.FindIndex(s => s.Id == "SC_APU_MASTER");
        int wait = steps.FindIndex(s => s.Id == "SC_APU_WAIT");
        int batteries = steps.FindIndex(s => s.Id == "SC_BATTERIES");
        Assert.True(master < wait && wait < batteries, $"master {master}, wait {wait}, batteries {batteries}");
        var w = steps[wait];
        Assert.Equal(FlowStepActionType.WaitForCondition, w.ActionType);
        Assert.Equal(A300FoKeys.ApuSpeed, w.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Skip, w.FailurePolicy);   // an unread speed times out and carries on
        Assert.Equal(150, w.TimeoutSeconds);
        Assert.True(w.Condition!(0));                                // an APU that never ran passes at once
        Assert.False(w.Condition!(A300FoStateEvaluator.ApuStoppedPercent));
        Assert.False(w.Condition!(double.NaN));
        Assert.Empty(w.LinkedChecklistItemIds);
    }

    [Fact]
    public void The_APU_speed_the_securing_wait_reads_is_registered_and_polled()
    {
        Assert.Contains(A300FoKeys.ApuSpeed, new A300FoStateEvaluator().OnRequestPollFields);
        Assert.True(new MSFSBlindAssist.Aircraft.IniA300Definition().GetVariables().ContainsKey(A300FoKeys.ApuSpeed));
    }

    [Fact]
    public void Parking_turns_the_fuel_pumps_off_only_after_the_engines_read_stopped()
    {
        var steps = Flow("PARKING").Steps;
        var pumps = steps.Single(s => s.Id == "PK_FUEL_PUMPS");
        Assert.Equal("PK_ENGINES_STOPPED", pumps.RequiresStepId);
        var wait = steps.Single(s => s.Id == "PK_ENGINES_STOPPED");
        Assert.Equal(A300FoKeys.EnginesStopped, wait.ConditionFieldName);
        Assert.True(steps.IndexOf(wait) < steps.IndexOf(pumps));
        Assert.True(steps.FindIndex(s => s.Id == "PK_FUEL_LEVERS") < steps.IndexOf(wait));
    }

    [Fact]
    public void No_flow_programs_the_mcdu()
    {
        foreach (var s in Flows.SelectMany(f => f.Steps).Where(s => s.EventName != null))
            Assert.DoesNotContain("MCDU", s.EventName!);
    }
}
