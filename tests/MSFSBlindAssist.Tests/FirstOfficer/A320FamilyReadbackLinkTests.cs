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
/// FO-6 on the read-back lines of the three A320-family First Officers (Fenix, FBW A32NX, Headwind
/// A330). A finished flow latches its action group AND its own <c>*_CL</c> read-back
/// (<see cref="FlowDefinition{TState}.CompletionGroupIds"/>), so a step that delivers a read-back line
/// has to name it, or a step that failed ("Skipping: Beacon: ON") still latches the line ticked.
/// Three read-back lines have a step behind them:
///   Before Start   BS_BEACON        -> BSC_BEACON  "Beacon: ON" (the write sets it)
///   After Start    AS_RUDDERTRIM    -> ASC_RUDDER  "Rudder trim: NEUTRAL" (the write sets it)
///   After Landing  AL_WXR_PWS_CHECK -> ALC_WXR     "Radar and predictive windshear: OFF"
/// The radar line is true only when BOTH the radar (<c>AL_WXR_OFF</c>) and the predictive windshear
/// (<c>AL_PWS_OFF</c>) are off, so neither write completes it. Linked to both writes, the radar
/// write's success ticked the line, and as the only line of <c>AFTER_LANDING_CL</c> that latched the
/// group at once, with the windshear still on: a windshear write that then failed, or a flow cancelled
/// during the APU wait, left the line latched complete. A line that only a later condition makes true
/// is completed by a read-only wait (FO-6): <c>AL_WXR_PWS_CHECK</c>, right after both writes, reads
/// <c>FO_WXR_PWS_OFF</c> for up to 5 s, is skipped on timeout and never writes (the FO-13 shape).
/// The Fenix's ASC_RUDDER is a Reminder (no measured trim var); it is linked all the same, so a reset
/// that could not be sent leaves it unticked for the pilot.
///
/// Considered and NOT linked: AS_ECAMSTS (the ECAM page it shows is not the pilot's "ECAM status:
/// CHECKED"), and the Approach flow, which has no step that sets a line of APPROACH_CL (the seat belts
/// the Descent flow sets are not in a group the Approach flow latches).
///
/// This class holds the structural link and pairing tests, which are instant. The behaviour tests, which
/// run real <see cref="FlowManager{TExec, TState}"/> flows in real time (its 2 s inter-step pause and
/// the radar check's 5 s timeout), live one class per aircraft in
/// <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/> so xUnit runs them in parallel.
/// </summary>
public class A320FamilyReadbackLinkTests
{
    private static readonly string[] Aircraft = { "Fenix", "A32NX", "A330" };

    internal const string RadarCheck = "AL_WXR_PWS_CHECK";
    internal const string RadarLine = "ALC_WXR";
    internal const string RadarLabel = "Radar and predictive windshear: OFF";

    /// <summary>Aircraft, flow, step, the action-group line it completes (null: none) and the
    /// read-back line it completes (null: none).</summary>
    public static IEnumerable<object?[]> StepLinks()
    {
        foreach (var aircraft in Aircraft)
        {
            yield return new object?[] { aircraft, "BEFORE_START", "BS_BEACON", "BS_BEACON", "BSC_BEACON" };
            yield return new object?[] { aircraft, "AFTER_START", "AS_RUDDERTRIM", "AS_RUDDERTRIM", "ASC_RUDDER" };
            // One radar switch off does not make "Radar and predictive windshear: OFF" true: each
            // radar write names its own action line only ...
            yield return new object?[] { aircraft, "AFTER_LANDING", "AL_WXR_OFF", "AL_WXR_OFF", null };
            yield return new object?[] { aircraft, "AFTER_LANDING", "AL_PWS_OFF", "AL_PWS_OFF", null };
            // ... and the read-only check after both names the read-back line only.
            yield return new object?[] { aircraft, "AFTER_LANDING", RadarCheck, null, RadarLine };
        }
    }

    public static IEnumerable<object[]> AircraftOnly() => Aircraft.Select(a => new object[] { a });

    /// <summary>Every step names exactly its lines, and each exists: the action line in the group
    /// named after the flow, the read-back line in that group's <c>_CL</c> read-back.</summary>
    [Theory]
    [MemberData(nameof(StepLinks))]
    public void A_step_completes_exactly_its_action_line_and_its_readback_line(
        string aircraft, string flowId, string stepId, string? actionLine, string? readbackLine)
    {
        var (links, groups) = Profile(aircraft);

        Assert.Equal(new[] { actionLine, readbackLine }.OfType<string>(), links[(flowId, stepId)]);
        if (actionLine != null) Assert.Contains(actionLine, groups[flowId]);
        if (readbackLine != null) Assert.Contains(readbackLine, groups[flowId + "_CL"]);
    }

    /// <summary>The radar read-back line has ONE step behind it in all the aircraft's flows: a
    /// read-only check placed right after the two radar writes, reading the line's own field, short,
    /// skipped on timeout, and sending nothing.</summary>
    [Theory]
    [MemberData(nameof(AircraftOnly))]
    public void The_radar_readback_is_completed_by_one_read_only_check_after_both_radar_writes(string aircraft)
    {
        switch (aircraft)
        {
            case "Fenix": AssertRadarCheck(FenixFlowDefinitions.Build(), FenixChecklistDefinitions.Build()); break;
            case "A32NX": AssertRadarCheck(FbwA320FlowDefinitions.Build(), FbwA320ChecklistDefinitions.Build()); break;
            case "A330": AssertRadarCheck(HwA330FlowDefinitions.Build(), HwA330ChecklistDefinitions.Build()); break;
            default: throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null);
        }
    }

    private static void AssertRadarCheck<TExec, TState>(
        List<FlowDefinition<TState>> flows, List<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var steps = flows.Single(f => f.Id == "AFTER_LANDING").Steps;
        var ids = steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf("AL_WXR_OFF") + 1, ids.IndexOf("AL_PWS_OFF"));
        Assert.Equal(ids.IndexOf("AL_PWS_OFF") + 1, ids.IndexOf(RadarCheck));

        var check = steps[ids.IndexOf(RadarCheck)];
        var line = groups.Single(g => g.Id == "AFTER_LANDING_CL").Items.Single(i => i.Id == RadarLine);
        Assert.Equal(RadarLabel, check.Label);
        Assert.Equal(line.Label, check.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, check.ActionType);
        Assert.Equal("FO_WXR_PWS_OFF", check.ConditionFieldName);
        Assert.Equal(line.StateFieldName, check.ConditionFieldName);
        Assert.Equal(5, check.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, check.FailurePolicy);
        Assert.True(check.Condition!(1.0));
        Assert.False(check.Condition!(0.0));
        Assert.False(check.Condition!(double.NaN));   // unknown is not off
        Assert.NotNull(check.SkipCondition);
        // Read-only: nothing to send.
        Assert.Null(check.EventName);
        Assert.Null(check.TargetValue);
        Assert.Null(check.TargetValueProvider);
        Assert.Empty(check.MultiActions);
        Assert.Equal(new[] { RadarLine }, check.LinkedChecklistItemIds);

        // No other step of any flow completes the line.
        Assert.Equal(new[] { RadarCheck },
            flows.SelectMany(f => f.Steps).Where(s => s.LinkedChecklistItemIds.Contains(RadarLine)).Select(s => s.Id));
    }

    // -----------------------------------------------------------------------
    // The aircraft's real flows and checklists, as comparable data
    // -----------------------------------------------------------------------

    private static (Dictionary<(string Flow, string Step), List<string>> Links, Dictionary<string, List<string>> Groups)
        Profile(string aircraft) => aircraft switch
    {
        "Fenix" => (LinksOf(FenixFlowDefinitions.Build()), GroupsOf(FenixChecklistDefinitions.Build())),
        "A32NX" => (LinksOf(FbwA320FlowDefinitions.Build()), GroupsOf(FbwA320ChecklistDefinitions.Build())),
        "A330" => (LinksOf(HwA330FlowDefinitions.Build()), GroupsOf(HwA330ChecklistDefinitions.Build())),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static Dictionary<(string Flow, string Step), List<string>> LinksOf<TState>(
        IEnumerable<FlowDefinition<TState>> flows) where TState : IFoStateEvaluator =>
        flows.SelectMany(f => f.Steps.Select(s => (Key: (Flow: f.Id, Step: s.Id), Links: s.LinkedChecklistItemIds.ToList())))
            .ToDictionary(x => x.Key, x => x.Links);

    private static Dictionary<string, List<string>> GroupsOf<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.ToDictionary(g => g.Id, g => g.Items.Select(i => i.Id).ToList());
}
