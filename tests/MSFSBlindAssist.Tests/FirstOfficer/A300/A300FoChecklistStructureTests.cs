using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.A300;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

public class A300FoChecklistStructureTests
{
    private static readonly List<ChecklistGroup<A300FoActionExecutor, A300FoStateEvaluator>> Groups = A300FoChecklistDefinitions.Build();
    private static readonly List<FlowDefinition<A300FoStateEvaluator>> Flows = A300FoFlowDefinitions.Build();

    private static string[] Labels(string id) => Groups.Single(g => g.Id == id).Items.Select(i => i.Label).ToArray();

    [Fact]
    public void Groups_in_flight_order() => Assert.Equal(new[]
    {
        "POWER_UP", "PREFLIGHT", "BEFORE_START", "BEFORE_START_CL", "BEFORE_START_BELOW_CL", "ENGINE_START",
        "AFTER_START", "AFTER_START_CL", "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL", "LINE_UP", "LINE_UP_CL",
        "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL", "DESCENT", "APPROACH", "APPROACH_CL", "LANDING", "LANDING_CL",
        "AFTER_LANDING", "AFTER_LANDING_CL", "PARKING", "PARKING_CL", "SECURING", "SECURING_CL",
    }, Groups.Select(g => g.Id));

    [Fact]
    public void No_readback_item_has_an_action()
    {
        foreach (var g in Groups.Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)))
            foreach (var i in g.Items)
                Assert.True(i.CheckAction == null, $"{g.Id}/{i.Id}");
    }

    [Fact]
    public void Ids_are_unique_and_no_group_lists_a_line_twice()
    {
        var ids = Groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var g in Groups)
            Assert.Equal(g.Items.Count, g.Items.Select(i => i.Label).Distinct().Count());
    }

    [Fact]
    public void Every_action_group_lists_the_same_lines_in_the_same_order_as_its_flow()
    {
        foreach (var f in Flows)
        {
            var group = Groups.Single(g => g.Id == f.Id);
            var ids = group.Items.Select(i => i.Id).ToHashSet();
            var fromFlow = f.Steps.Select(s => s.CompletesChecklistItemId ?? s.Id).Where(ids.Contains).ToList();
            Assert.Equal(group.Items.Select(i => i.Id), fromFlow);
        }
    }

    [Fact]
    public void Every_actionable_item_has_a_delivering_flow_step()
    {
        // Except the two starter lines, which tick themselves on the engine running and latch ([FO-8]).
        var latched = Groups.SelectMany(g => g.Items).Where(i => i.RevertBehavior == RevertBehavior.StayComplete
            && i.Type != ChecklistItemType.CaptainReminder && i.Type != ChecklistItemType.Actionable).Select(i => i.Id).ToList();
        Assert.Equal(new[] { "ES_STARTER_2", "ES_STARTER_1" }, latched);
        foreach (var f in Flows)
        {
            var delivered = f.Steps.Select(s => s.CompletesChecklistItemId).Where(id => id != null).ToHashSet();
            foreach (var i in Groups.Single(g => g.Id == f.Id).Items.Where(i => i.CheckAction != null && !latched.Contains(i.Id)))
                Assert.Contains(i.Id, delivered);
        }
    }

    [Fact]
    public void No_step_ticks_a_starter_line_its_write_is_not_yet_a_running_engine()
    {
        foreach (var s in Flows.SelectMany(f => f.Steps))
            Assert.DoesNotContain(s.LinkedChecklistItemIds, id => id is "ES_STARTER_1" or "ES_STARTER_2");
    }

    [Fact]
    public void Every_flow_link_names_an_item_of_a_group_the_flow_completes()
    {
        var byId = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        var groupIds = Groups.Select(g => g.Id).ToHashSet();
        foreach (var f in Flows)
        {
            var completes = f.CompletionGroupIds(groupIds.Contains).ToHashSet();
            foreach (var s in f.Steps)
                foreach (var id in s.LinkedChecklistItemIds)
                {
                    Assert.True(byId.TryGetValue(id, out var item), $"{f.Id}/{s.Id} links {id}");
                    Assert.Contains(item!.GroupId, completes);
                }
        }
    }

    [Fact]
    public void Every_auto_readback_line_is_delivered_by_its_flow()
    {
        // A finished flow latches its read-back list; an auto line it does not deliver would read done untouched.
        var linked = Flows.SelectMany(f => f.Steps).SelectMany(s => s.LinkedChecklistItemIds).ToHashSet();
        foreach (var g in Groups.Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)))
            foreach (var i in g.Items.Where(i => i.IsAutoDetectable))
                Assert.True(linked.Contains(i.Id), $"{g.Id}/{i.Id}");
    }

    [Fact]
    public void Every_item_field_is_synthetic_or_registered_and_polled_when_on_request()
    {
        var vars = new IniA300Definition().GetVariables();
        var synthetic = A300FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new A300FoStateEvaluator().OnRequestPollFields.ToHashSet();
        foreach (var i in Groups.SelectMany(g => g.Items).Where(i => i.StateFieldName != null))
        {
            string field = i.StateFieldName!;
            if (synthetic.Contains(field) || MSFSBlindAssist.Aircraft.A300.A300LampBoard.ById.ContainsKey(field)) continue;
            Assert.True(vars.TryGetValue(field, out var def), $"{i.Id}: {field}");
            if (def!.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.Contains(field, polled);
        }
    }

    [Fact]
    public void A_hand_tick_refuses_where_its_flow_step_would()
    {
        // Ticking an action line runs the same write as its flow step, so it keeps the step's guards.
        var items = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var s in Flows.SelectMany(f => f.Steps).Where(s => s.LeaveAloneWhen != null || s.RequiresStepId != null))
            Assert.True(items[s.Id].LeaveAloneWhen != null, s.Id);

        var cache = new Dictionary<string, double>();
        var state = new A300FoStateEvaluator { Read = k => cache.TryGetValue(k, out var v) ? v : null };
        cache[A300FoKeys.OnGround] = 0;
        Assert.True(items["AL_FLAPS"].LeaveAloneWhen!(state));            // airborne
        cache[A300FoKeys.SpeedBrake] = 0.5;
        Assert.True(items["AL_SPOILERS"].LeaveAloneWhen!(state));         // speed brake lever out
        cache[A300FoKeys.ApuGen] = 1;
        A300FoTestLights.Set(cache, A300FoKeys.ApuAvailLight, false);
        Assert.True(items["BS_EXT_POWER_OFF"].LeaveAloneWhen!(state));    // the APU is not available
        A300FoTestLights.Set(cache, A300FoKeys.ApuAvailLight, true);
        Assert.False(items["BS_EXT_POWER_OFF"].LeaveAloneWhen!(state));
        state.SetEngineN2(30, 30);
        Assert.True(items["PK_BEACON"].LeaveAloneWhen!(state));           // the engines are still turning
        state.SetEngineN2(5, 5);
        Assert.False(items["PK_BEACON"].LeaveAloneWhen!(state));
    }

    [Fact]
    public void Before_start_follows_the_card() => Assert.Equal(new[]
    {
        "Cockpit prep: COMPLETED", "Fuel quantity: CHECKED", "Takeoff data: SET, both", "Landing elevation: SET",
        "Altimeters: SET, both", "Brakes and anti-skid: NORM and ON", "Windows and doors: CLOSED, both",
        "Start ACARS",   // the fleet's last Before Start line, a Captain reminder (BeforeStartAcarsReadbackTests)
    }, Labels("BEFORE_START_CL"));

    [Fact]
    public void Before_takeoff_below_the_line_follows_the_card() => Assert.Equal(new[]
    {
        "Transponder: SET", "TCAS: TA/RA", "Autobrake: MAX", "Ignition: AS REQUIRED",
    }, Labels("LINE_UP_CL"));

    [Fact]
    public void Approach_follows_the_card_with_continuous_relight() => Assert.Equal(new[]
    {
        "Signs: SET", "Briefing: CONFIRMED", "ECAM status: CHECKED", "Altimeters: SET, both", "Minimums: SET, both",
        "Ignition: CONT RELIGHT", "Landing elevation: SET",
    }, Labels("APPROACH_CL"));

    [Fact]
    public void The_card_blanks_speak_a_live_value()
    {
        var blanks = new[] { "BSC_FUEL", "BSC_LDG_ELEV", "BSC_ALTIMETERS", "ASC_PITCH_TRIM", "ASC_FLAPS", "BTC_SPEEDS",
            "ATC_ALTIMETERS", "APC_ALTIMETERS", "APC_MINIMUMS", "APC_LDG_ELEV", "LDC_FLAPS" };
        var byId = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var id in blanks)
            Assert.NotNull(byId[id].LiveValue);
    }
}
