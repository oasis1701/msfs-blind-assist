using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Taxi and Line-up flows of the three A320-family First Officers (Fenix, FBW A32NX, Headwind
/// A330), which replaced the one Before Takeoff flow on 2026-10-09
/// (docs/invariants/first-officer-airbus.md#foa-8). Its action group sat between the Taxi and
/// Line-up checklists, so the Taxi checklist asked for the radar, the autobrake, the config test
/// and the cabin before the flow that sets them, and running that flow first lit the strobes and
/// the landing lights while taxiing. Each flow now sits just above its own checklist, and a
/// finished run latches that checklist (FlowDefinition.CompletionGroupIds). The links are pinned by
/// A320FamilyReadbackLinkTests, the runs by A320FamilyReadbackLinkBehaviourTests. The file name says
/// "RunwayEntry" for the Line-up flow on purpose: the taxi-steering rules load for the glob
/// <c>tests/**/*Lineup*.cs</c>, so a name with "Lineup" in it (in any letter case) makes reading this
/// file load their rules for nothing.
/// </summary>
public class A320FamilyTaxiAndRunwayEntryFlowTests
{
    public static TheoryData<string> Aircraft => new() { "Fenix", "A32NX", "A330" };

    private static readonly string[] TaxiSteps =
    {
        "TX_AUTOBRAKE", "TX_WXR", "TX_PWS", "TX_WXR_PWS_CHECK", "TX_XPDRAUTO", "TX_CABIN", "TX_MEMO_CHECK", "TX_CONFIG",
    };

    private static readonly string[] TaxiLabels =
    {
        "Autobrake: MAX", "Weather radar: SYSTEM 1", "Predictive windshear: AUTO",
        "Radar and predictive windshear: ON and AUTO", "Transponder: AUTO",
        "Advise the cabin crew for takeoff (call all)", "Spoilers, flaps and seat belts: set for takeoff",
        "Takeoff config test",
    };

    private static readonly string[] LineupSteps =
        { "LU_TCAS", "LU_STROBE", "LU_TURNOFF", "LU_NOSE_TO", "LU_LANDING_LT", "LU_CLEARANCE" };

    private static readonly string[] LineupLabels =
    {
        "TCAS: TA/RA", "Strobes: ON", "Runway turn-off lights: ON", "Nose light: TAKEOFF",
        "Landing lights: ON", "Obtain takeoff clearance",
    };

    private sealed record FlowView(string Id, string Name, string[] Related, string[] Latches,
        string[] Steps, string[] Labels, FlowStepActionType[] Kinds);

    private sealed record GroupView(string Id, string Name, string[] Items);

    private static (List<FlowView> Flows, List<GroupView> Groups) Profile(string aircraft) => aircraft switch
    {
        "Fenix" => View(FenixFlowDefinitions.Build(), FenixChecklistDefinitions.Build()),
        "A32NX" => View(FbwA320FlowDefinitions.Build(), FbwA320ChecklistDefinitions.Build()),
        "A330" => View(HwA330FlowDefinitions.Build(), HwA330ChecklistDefinitions.Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static (List<FlowView> Flows, List<GroupView> Groups) View<TExec, TState>(
        List<FlowDefinition<TState>> flows, List<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var flowViews = flows.Select(f => new FlowView(f.Id, f.Name, f.RelatedChecklistGroupIds.ToArray(),
            f.CompletionGroupIds(ids.Contains).ToArray(), f.Steps.Select(s => s.Id).ToArray(),
            f.Steps.Select(s => s.Label).ToArray(), f.Steps.Select(s => s.ActionType).ToArray())).ToList();
        var groupViews = groups.Select(g => new GroupView(g.Id, g.Name, g.Items.Select(i => i.Id).ToArray())).ToList();
        return (flowViews, groupViews);
    }

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void Taxi_and_line_up_replace_before_takeoff_in_flying_order(string aircraft)
    {
        var (flows, groups) = Profile(aircraft);
        var flowIds = flows.Select(f => f.Id).ToList();
        Assert.Equal(flowIds.IndexOf("AFTER_START") + 1, flowIds.IndexOf("TAXI"));
        Assert.Equal(flowIds.IndexOf("TAXI") + 1, flowIds.IndexOf("LINEUP"));
        Assert.Equal(flowIds.IndexOf("LINEUP") + 1, flowIds.IndexOf("AFTER_TAKEOFF"));
        Assert.DoesNotContain("BEFORE_TAKEOFF", flowIds);
        Assert.DoesNotContain(groups, g => g.Id is "BEFORE_TAKEOFF" or "BEFORE_TAKEOFF_CL");
        Assert.Equal("Taxi", flows.Single(f => f.Id == "TAXI").Name);
        Assert.Equal("Line-up", flows.Single(f => f.Id == "LINEUP").Name);
    }

    // In the tree each flow's action group sits just above its own checklist.
    [Theory]
    [MemberData(nameof(Aircraft))]
    public void Each_action_group_sits_just_above_its_own_checklist(string aircraft)
    {
        var groups = Profile(aircraft).Groups;
        var groupIds = groups.Select(g => g.Id).ToList();
        int i = groupIds.IndexOf("AFTER_START_CL");
        Assert.Equal(new[] { "AFTER_START_CL", "TAXI", "TAXI_CL", "LINEUP", "LINEUP_CL", "AFTER_TAKEOFF" },
            groupIds.Skip(i).Take(6).ToArray());
        Assert.Equal("Taxi", groups.Single(g => g.Id == "TAXI").Name);
        Assert.Equal("Line-up", groups.Single(g => g.Id == "LINEUP").Name);
    }

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void The_taxi_flow_runs_the_taxi_items_and_ends_with_the_config_test(string aircraft)
    {
        var taxi = Profile(aircraft).Flows.Single(f => f.Id == "TAXI");
        Assert.Equal(TaxiSteps, taxi.Steps);
        Assert.Equal(TaxiLabels, taxi.Labels);
        Assert.Equal(new[] { "TAXI" }, taxi.Related);
        Assert.Equal(new[] { "TAXI", "TAXI_CL" }, taxi.Latches);
    }

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void The_line_up_flow_runs_the_line_up_items_and_ends_with_the_clearance_reminder(string aircraft)
    {
        var lineup = Profile(aircraft).Flows.Single(f => f.Id == "LINEUP");
        Assert.Equal(LineupSteps, lineup.Steps);
        Assert.Equal(LineupLabels, lineup.Labels);
        Assert.Equal(FlowStepActionType.CaptainReminder, lineup.Kinds[^1]);
        Assert.Equal(new[] { "LINEUP" }, lineup.Related);
        Assert.Equal(new[] { "LINEUP", "LINEUP_CL" }, lineup.Latches);
    }

    // One cabin call, in Taxi (owner, 2026-10-09: on FlyByWire it makes the memo show CABIN READY),
    // and no flight-controls prompt in either flow: the Taxi checklist's line is enough.
    [Theory]
    [MemberData(nameof(Aircraft))]
    public void One_cabin_call_and_no_flight_controls_prompt(string aircraft)
    {
        var labels = Profile(aircraft).Flows.Where(f => f.Id is "TAXI" or "LINEUP")
            .SelectMany(f => f.Labels).ToList();
        Assert.Single(labels, l => l.Contains("cabin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(labels, l => l.Contains("flight control", StringComparison.OrdinalIgnoreCase));
    }

    // The action groups hold one line per step that acts, completed by that step (its own id).
    [Theory]
    [MemberData(nameof(Aircraft))]
    public void The_action_groups_hold_a_line_for_each_step_that_acts(string aircraft)
    {
        var groups = Profile(aircraft).Groups.ToDictionary(g => g.Id, g => g.Items);
        Assert.Equal(new[] { "TX_AUTOBRAKE", "TX_WXR", "TX_PWS", "TX_XPDRAUTO", "TX_CABIN", "TX_CONFIG" }, groups["TAXI"]);
        Assert.Equal(LineupSteps, groups["LINEUP"]);
    }
}
