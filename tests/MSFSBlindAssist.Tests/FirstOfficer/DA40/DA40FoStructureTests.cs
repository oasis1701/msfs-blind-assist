using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.DA40;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.DA40;

using Data = DA40FoTestData;

/// <summary>
/// The shape of the DA40 First Officer (FOD-1 to FOD-6): every write a key the definition writes, every
/// line in a latched group delivered by a step (FO-6, FO-7), read-backs action-free (FO-3), the
/// FO-25 guards first, and the Captain's levers never moved.
/// </summary>
public class DA40FoStructureTests
{
    private static IEnumerable<(string Flow, string Step, string Key)> WriteKeys(DA40Variant v) =>
        Data.Flows(v).SelectMany(f => f.Steps.SelectMany(s => s.ActionType switch
        {
            FlowStepActionType.SetSwitch => new[] { (f.Id, s.Id, s.EventName!) },
            FlowStepActionType.SetSwitchMultiple => s.MultiActions.Select(a => (f.Id, s.Id, a.EventName)).ToArray(),
            _ => Array.Empty<(string, string, string)>(),
        }));

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_write_is_a_pseudo_key_or_a_variable_of_this_airframe(DA40Variant v)
    {
        var vars = new CowsDA40Definition(v).GetVariables();
        var bad = WriteKeys(v).Where(w => !DA40FoActionExecutor.PseudoKeys.Contains(w.Key) && !vars.ContainsKey(w.Key))
            .Select(w => $"{w.Flow}/{w.Step}: {w.Key}").ToList();
        Assert.True(bad.Count == 0, "Not a variable on the " + v + ": " + string.Join(", ", bad));
    }

    // FOD-1: the executor refuses a key the definition does not claim, so every key a flow writes, and
    // every key a pseudo-key writes, must have its own branch in one of the definition's Handle*Set
    // methods. Read from the source: running a handler needs the SimConnect SDK, which CI lacks.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_written_key_is_claimed_by_the_definition(DA40Variant v)
    {
        string src = string.Join("\n", System.IO.Directory.GetFiles(System.IO.Path.Combine(
                ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "DA40"), "CowsDA40Definition*.cs")
            .Select(System.IO.File.ReadAllText));
        var keys = WriteKeys(v).Select(w => w.Key).Where(k => !DA40FoActionExecutor.PseudoKeys.Contains(k))
            .Concat(new[] { "DA40_STBY_ALTIMETER_SET", "DA40_G1000_BARO_SET" })
            .Concat(v == DA40Variant.NG
                ? new[] { "DA40_START_STARTER_ENGAGE", "DA40_START_STARTER_RELEASE", "DA40_ECU_TEST" }
                : new[] { "DA40_MAG_KEY", "DA40_MAG_RELEASE", "DA40_XLS_MIXTURE_SET" })
            .Distinct().ToList();
        var unclaimed = keys.Where(k => !System.Text.RegularExpressions.Regex.IsMatch(src,
            $@"(case\s+""{k}"")|(varKey\s*(==|!=|is)\s*""{k}"")|(or\s+""{k}"")")).ToList();
        Assert.True(unclaimed.Count == 0, "No HandleUIVariableSet branch writes: " + string.Join(", ", unclaimed));
        // The doors are claimed through their exit-index table.
        Assert.All(DA40FoActionExecutor.DoorKeys, k =>
            Assert.Matches($@"\[""{k}""\]\s*=\s*\d", src));
    }

    // FO-7 / FO-6: no auto-detect or action line in a group a flow latches is left without a step
    // naming it, so a finished flow never ticks and latches a line the aircraft does not show.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_line_a_flow_latches_has_a_step_behind_it(DA40Variant v)
    {
        var groups = Data.Groups(v);
        var latched = FlowChecklistLinkAudit.FlowsWithItems(Data.Flows(v), groups,
            f => f.CompletionGroupIds(id => groups.Any(g => g.Id == id)));
        Assert.Empty(FlowChecklistLinkAudit.UndeliveredLines(latched));
    }

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_link_names_a_line_of_a_group_its_flow_latches(DA40Variant v)
    {
        var groups = Data.StandInGroups(v);
        foreach (var flow in Data.Flows(v))
        {
            var ids = flow.CompletionGroupIds(id => groups.Any(g => g.Id == id))
                .SelectMany(id => groups.Single(g => g.Id == id).Items).Select(i => i.Id).ToHashSet();
            foreach (var step in flow.Steps)
                foreach (var id in step.LinkedChecklistItemIds)
                    Assert.True(ids.Contains(id), $"{flow.Id}/{step.Id} links {id}, which {flow.Id} does not latch");
        }
    }

    // FO-3.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Read_backs_and_reference_pages_carry_no_action(DA40Variant v)
    {
        foreach (var g in Data.Groups(v).Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal) || g.Id.StartsWith("DA40_", StringComparison.Ordinal)))
            Assert.All(g.Items, i => Assert.Null(i.CheckAction));
    }

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_action_line_is_a_step_of_its_own_flow(DA40Variant v)
    {
        var flows = Data.Flows(v).ToDictionary(f => f.Id);
        foreach (var g in Data.StandInGroups(v).Where(g => flows.ContainsKey(g.Id)))
        {
            var stepIds = flows[g.Id].Steps.Select(s => s.Id).ToHashSet();
            Assert.All(g.Items, i => Assert.Contains(i.Id, stepIds));
        }
    }

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Ids_are_unique_across_every_flow_and_group(DA40Variant v)
    {
        var steps = Data.Flows(v).SelectMany(f => f.Steps.Select(s => s.Id)).ToList();
        var twiceSteps = steps.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(twiceSteps.Count == 0, "Step used twice: " + string.Join(", ", twiceSteps));
        var items = Data.StandInGroups(v).SelectMany(g => g.Items.Select(i => i.Id)).ToList();
        var twice = items.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(twice.Count == 0, "Used twice: " + string.Join(", ", twice));
    }

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void A_dependency_names_an_earlier_step_of_the_same_flow(DA40Variant v)
    {
        foreach (var flow in Data.Flows(v))
            for (int i = 0; i < flow.Steps.Count; i++)
                if (flow.Steps[i].RequiresStepId is string dep)
                    Assert.True(flow.Steps.Take(i).Any(s => s.Id == dep), $"{flow.Id}/{flow.Steps[i].Id} requires {dep}");
    }

    // FO-25: the flows that cut the engine or its fuel check first, read-only and Stop, unread failing.
    [Theory]
    [InlineData(DA40Variant.NG, "BEFORE_START", "BS_ENGINE_STOPPED", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.NG, "ENGINE_START", "ES_ENGINE_STOPPED", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.NG, "SHUTDOWN", "SD_ON_GROUND", DA40FoStateEvaluator.OnGroundField)]
    [InlineData(DA40Variant.XLS, "BEFORE_START", "BS_ENGINE_STOPPED", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.XLS, "ENGINE_START", "ES_ENGINE_STOPPED", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.XLS, "ENGINE_START_HOT", "EH_ENGINE_STOPPED", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.XLS, "ENGINE_START_REPRIME", "ER_ENGINE_STOPPED", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.XLS, "SHUTDOWN", "SD_ON_GROUND", DA40FoStateEvaluator.OnGroundField)]
    public void The_guard_is_first_read_only_and_stops(DA40Variant v, string flowId, string stepId, string field)
    {
        var g = Data.Flows(v).Single(f => f.Id == flowId).Steps[0];
        Assert.Equal(stepId, g.Id);
        Assert.Equal(FlowStepActionType.WaitForCondition, g.ActionType);
        Assert.Equal(field, g.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, g.FailurePolicy);
        Assert.True(g.Condition!(1));
        Assert.False(g.Condition!(0));
        Assert.False(g.Condition!(double.NaN));
        Assert.Empty(g.LinkedChecklistItemIds);
    }

    // FO-25, as #160's sweep put it for every airliner: Before Engine Start is a cold-and-dark flow
    // that cuts the engine, so it opens with the ground guard too (airborne with the engine out, the
    // engine master, mixture and ignition are what a restart needs) and stops whole.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Before_start_opens_with_both_guards(DA40Variant v)
    {
        var steps = Data.Flows(v).Single(f => f.Id == "BEFORE_START").Steps;
        Assert.Equal(new[] { "BS_ENGINE_STOPPED", "BS_ON_GROUND" }, steps.Take(2).Select(s => s.Id));
        var g = steps[1];
        Assert.Equal(FlowStepActionType.WaitForCondition, g.ActionType);
        Assert.Equal(DA40FoStateEvaluator.OnGroundField, g.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, g.FailurePolicy);
        Assert.False(g.Condition!(double.NaN));
        Assert.Empty(g.LinkedChecklistItemIds);
    }

    // FO-25: the cold cockpit's engine cuts and the master switch off need BOTH: on the ground AND
    // the engine stopped. The master is the one cut the NG's ECU punishes with the engine turning.
    [Theory]
    [InlineData(DA40Variant.NG, "BS_ENG_MASTER_OFF")]
    [InlineData(DA40Variant.NG, "SD_ELEC_OFF")]
    [InlineData(DA40Variant.XLS, "BS_MIXTURE")]
    [InlineData(DA40Variant.XLS, "BS_IGNITION_OFF")]
    [InlineData(DA40Variant.XLS, "SD_ELEC_OFF")]
    public void A_parked_only_tick_needs_the_ground_and_the_engine_stopped(DA40Variant v, string itemId)
    {
        var item = Data.StandInGroups(v).SelectMany(g => g.Items).Single(i => i.Id == itemId);
        var step = Data.Flows(v).SelectMany(f => f.Steps).First(s => s.Id == itemId);
        string rpm = new DA40FoStateEvaluator { Variant = v }.RpmKey;
        DA40FoStateEvaluator State(bool ground, bool running) =>
            Eval(v, (DA40FoStateEvaluator.OnGroundField, ground ? 1 : 0), (rpm, running ? 2000 : 0));
        foreach (var refuse in new[] { item.LeaveAloneWhen!, step.LeaveAloneWhen! })
        {
            Assert.True(refuse(Eval(v)), $"{itemId}: unread must refuse");
            Assert.True(refuse(State(ground: false, running: false)), $"{itemId}: airborne with the engine out must refuse");
            Assert.True(refuse(State(ground: true, running: true)), $"{itemId}: the engine turning must refuse");
            Assert.False(refuse(State(ground: true, running: false)), $"{itemId}: parked, the cut goes ahead");
        }
        Assert.Contains("not read on the ground, or the engine is not read stopped", item.LeaveAloneText);
    }

    // The electric fuel pump is a boost pump the POH switches off in the climb, not a fuel cut: its
    // OFF lines stay outside FO-25, unlike the airliners' fuel pumps.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void The_climb_fuel_pump_off_is_not_refused_airborne(DA40Variant v)
    {
        var item = Data.StandInGroups(v).SelectMany(g => g.Items).Single(i => i.Id == "CL_FUEL_PUMP_OFF");
        Assert.Null(item.LeaveAloneWhen);
    }

    // FO-25: a tick making the same cut refuses on an unread or wrong state.
    [Theory]
    [InlineData(DA40Variant.NG, "SD_ENG_MASTER_OFF", DA40FoStateEvaluator.OnGroundField)]
    [InlineData(DA40Variant.XLS, "EH_MIXTURE", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.XLS, "ES_PRIME", DA40FoStateEvaluator.EngineStoppedField)]
    [InlineData(DA40Variant.XLS, "SD_MIXTURE", DA40FoStateEvaluator.OnGroundField)]
    [InlineData(DA40Variant.XLS, "SD_IGNITION_OFF", DA40FoStateEvaluator.OnGroundField)]
    [InlineData(DA40Variant.XLS, "SD_GROUNDING", DA40FoStateEvaluator.OnGroundField)]
    public void A_cutting_tick_is_left_alone_unless_the_guard_reads_true(DA40Variant v, string itemId, string field)
    {
        var item = Data.StandInGroups(v).SelectMany(g => g.Items).Single(i => i.Id == itemId);
        Assert.NotNull(item.LeaveAloneWhen);
        // The engine-stopped field is read from the RPM: 0 is stopped, 2000 running.
        string rpm = new DA40FoStateEvaluator { Variant = v }.RpmKey;
        (string, double) Holds(bool yes) => field == DA40FoStateEvaluator.EngineStoppedField
            ? (rpm, yes ? 0 : 2000) : (field, yes ? 1 : 0);
        Assert.True(item.LeaveAloneWhen!(Eval(v)), "unread must refuse");
        Assert.True(item.LeaveAloneWhen!(Eval(v, Holds(false))), "a wrong state must refuse");
        Assert.False(item.LeaveAloneWhen!(Eval(v, Holds(true))), "the guard reading true lets it through");
    }

    // FOD-6: the First Officer moves the power lever and throttle only to the XLS's start positions.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void The_power_lever_moves_only_for_the_XLS_start(DA40Variant v)
    {
        var allowed = new HashSet<string> { "ES_THROTTLE_PRIME", "ES_THROTTLE_START", "EH_THROTTLE_START", "ER_THROTTLE" };
        var moves = WriteKeys(v).Where(w => w.Key is "DA40_POWER_LEVER_SET" or "DA40_XLS_THROTTLE_SET")
            .Select(w => w.Step).ToList();
        Assert.All(moves, s => Assert.Contains(s, allowed));
        if (v == DA40Variant.NG) Assert.Empty(moves);
    }

    // FO-2 and the Captain's values: no transponder code, no QNH, no frequency, no flight plan.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void No_flow_sets_a_code_a_pressure_or_a_frequency(DA40Variant v)
    {
        var forbidden = WriteKeys(v).Where(w => w.Key == "DA40_G1000_BARO_SET" || w.Key == "DA40_XPDR_CODE"
            || w.Key.StartsWith("DA40_RADIO_", StringComparison.Ordinal) || w.Key.StartsWith("DA40_AP_", StringComparison.Ordinal));
        Assert.Empty(forbidden);
    }

    [Fact]
    public void Doors_are_closed_only_through_the_guarded_pseudo_key()
    {
        foreach (DA40Variant v in new[] { DA40Variant.NG, DA40Variant.XLS })
            Assert.DoesNotContain(WriteKeys(v), w => DA40FoActionExecutor.DoorKeys.Contains(w.Key));
    }

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_group_is_departure_arrival_or_reference_and_only_one(DA40Variant v)
    {
        foreach (var id in Data.StandInGroups(v).Select(g => g.Id))
        {
            int sets = (FoDepartureGroups.Departure.Contains(id) ? 1 : 0) + (FoDepartureGroups.Arrival.Contains(id) ? 1 : 0)
                       + (FoDepartureGroups.Reference.Contains(id) ? 1 : 0);
            Assert.True(sets == 1, $"{id} is in {sets} sets");
        }
    }

    private static DA40FoStateEvaluator Eval(DA40Variant v, params (string Key, double Value)[] values)
    {
        var e = new DA40FoStateEvaluator { Variant = v };
        e.SetSimConnect(SeededSimConnectCache.With(values));
        return e;
    }
}
