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
/// Since 2026-10-09 every line the app can read or act on in a list a flow latches has a step behind
/// it (FOA-8, "made honest"; the audit below holds the undelivered set empty). Read-back lines with a
/// step behind them:
///   Before Start   BS_BEACON         -> BSC_BEACON        "Beacon: ON" (the write sets it)
///   After Start    AS_RUDDERTRIM     -> ASC_RUDDER        "Rudder trim: NEUTRAL" (the write sets it)
///   Taxi           TX_AUTOBRAKE      -> TXC_MEMO_AUTOBRK  "Autobrake: MAX"
///                  TX_WXR_PWS_CHECK  -> TXC_WXR           "Radar and predictive windshear: ON and AUTO"
///                  TX_MEMO_CHECK     -> TXC_MEMO_SPLRS, TXC_MEMO_FLAPS, TXC_FLAPS, TXC_MEMO_SIGNS
///                  TX_CONFIG         -> TXC_MEMO_TOCFG    "T.O config: NORMAL"
///   Line-up        LU_CABIN          -> LUC_CABIN         "Cabin: READY" (a line the pilot confirms;
///                                                         in Taxi until 2026-10-19)
///                  LU_TCAS           -> LUC_TCAS          "TCAS: TA/RA"
///   Approach       AP_SEATBELTS      -> APC_SEATBELTS     "Seat belts: ON"
///   Landing        LD_SPOILERS_ARM   -> LDC_MEMO_SPLRS    "Spoilers: ARMED" (the arm sets it)
///                  LD_MEMO_CHECK     -> LDC_MEMO_SIGNS, LDC_MEMO_FLAPS (A320FamilyLandingFlowTests)
///                  LD_GEAR_DOWN_CHECK -> LDC_MEMO_GEAR    "Landing gear: DOWN" (FO-13, last)
///   After Landing  AL_WXR_PWS_CHECK  -> ALC_WXR           "Radar and predictive windshear: OFF"
/// A line true only when two switches are both set (the radar lines) is completed by a read-only
/// wait right after the two writes; a line that reads what an earlier flow set (the takeoff memo
/// items: the ground spoilers and the takeoff flaps from After Start, the signs from Preflight's no
/// smoking and Before Start's seat belts) is completed by a read-only check in the flow that latches
/// it. Either reads the line's own field (or the evaluator's FO_TAXI_MEMO_OK) for up to 5 s, is
/// skipped on timeout and never writes (the FO-13 shape). Linked to a write instead, the first
/// write's success ticked the radar line, and a flow cancelled before the second left it latched
/// complete. The Fenix's ASC_RUDDER is a Reminder (no measured trim var); it is linked all the same,
/// so a reset that could not be sent leaves it unticked for the pilot.
///
/// Considered and NOT linked: AS_ECAMSTS (the ECAM page it shows is not the pilot's "ECAM status:
/// CHECKED").
///
/// This class holds the structural link, pairing and audit tests, which are instant. The behaviour
/// tests, which run real <see cref="FlowManager{TExec, TState}"/> flows in real time (its 2 s
/// inter-step pause and the checks' 5 s timeout), live one class per aircraft in
/// <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/> so xUnit runs them in parallel.
/// </summary>
public class A320FamilyReadbackLinkTests
{
    private static readonly string[] Aircraft = { "Fenix", "A32NX", "A330" };

    internal const string RadarCheck = "AL_WXR_PWS_CHECK";
    internal const string RadarLine = "ALC_WXR";
    internal const string RadarLabel = "Radar and predictive windshear: OFF";

    // The Taxi flow's two read-only checks.
    internal const string WxrOnCheck = "TX_WXR_PWS_CHECK";
    internal const string WxrOnLine = "TXC_WXR";
    internal const string WxrOnLabel = "Radar and predictive windshear: ON and AUTO";
    internal const string MemoCheck = "TX_MEMO_CHECK";
    internal const string MemoField = "FO_TAXI_MEMO_OK";
    internal const string MemoLabel = "Spoilers, flaps and seat belts: set for takeoff";
    internal static readonly string[] MemoLines = { "TXC_MEMO_SPLRS", "TXC_MEMO_FLAPS", "TXC_FLAPS", "TXC_MEMO_SIGNS" };

    /// <summary>Aircraft, flow, step, the action-group line it completes (null: none) and the
    /// read-back line it completes (null: none).</summary>
    public static IEnumerable<object?[]> StepLinks()
    {
        foreach (var aircraft in Aircraft)
        {
            yield return new object?[] { aircraft, "BEFORE_START", "BS_BEACON", "BS_BEACON", "BSC_BEACON" };
            yield return new object?[] { aircraft, "AFTER_START", "AS_RUDDERTRIM", "AS_RUDDERTRIM", "ASC_RUDDER" };
            // Each engine-start wait completes its own "Engine n: running" line (the line reads the
            // state the wait waits for; a timeout stops the flow, so nothing is latched).
            yield return new object?[] { aircraft, "ENGINE_START", "ES_ENG1_N2", "ES_ENG1_RUN", null };
            yield return new object?[] { aircraft, "ENGINE_START", "ES_ENG2_N2", "ES_ENG2_RUN", null };
            // The Approach flow's seat-belt write completes its own line and the Approach Checklist's
            // "Seat belts: ON" (usually "Already set" after Descent).
            yield return new object?[] { aircraft, "APPROACH", "AP_SEATBELTS", "AP_SEATBELTS", "APC_SEATBELTS" };
            // Landing (FOA-11): the arm completes its own line and the Landing Checklist's "Spoilers:
            // ARMED"; the gear check, last, completes "Landing gear: DOWN" only.
            yield return new object?[] { aircraft, "LANDING", "LD_SPOILERS_ARM", "LD_SPOILERS_ARM", "LDC_MEMO_SPLRS" };
            yield return new object?[] { aircraft, "LANDING", "LD_GEAR_DOWN_CHECK", null, "LDC_MEMO_GEAR" };
            // Taxi: every write names its own action line, and the two whose item the takeoff memo
            // reads name that read-back line too. Neither radar write names "Radar and predictive
            // windshear: ON and AUTO" (true only with both set): the read-only check after them does.
            yield return new object?[] { aircraft, "TAXI", "TX_AUTOBRAKE", "TX_AUTOBRAKE", "TXC_MEMO_AUTOBRK" };
            yield return new object?[] { aircraft, "TAXI", "TX_WXR", "TX_WXR", null };
            yield return new object?[] { aircraft, "TAXI", "TX_PWS", "TX_PWS", null };
            yield return new object?[] { aircraft, "TAXI", WxrOnCheck, null, WxrOnLine };
            yield return new object?[] { aircraft, "TAXI", "TX_XPDRAUTO", "TX_XPDRAUTO", null };
            yield return new object?[] { aircraft, "TAXI", "TX_CONFIG", "TX_CONFIG", "TXC_MEMO_TOCFG" };
            // Line-up: the cabin call (in Taxi until 2026-10-19) also names the read-back's "Cabin:
            // READY" and TCAS its "TCAS: TA/RA"; the lights name their own lines.
            yield return new object?[] { aircraft, "LINEUP", "LU_CABIN", "LU_CABIN", "LUC_CABIN" };
            yield return new object?[] { aircraft, "LINEUP", "LU_TCAS", "LU_TCAS", "LUC_TCAS" };
            yield return new object?[] { aircraft, "LINEUP", "LU_STROBE", "LU_STROBE", null };
            yield return new object?[] { aircraft, "LINEUP", "LU_TURNOFF", "LU_TURNOFF", null };
            yield return new object?[] { aircraft, "LINEUP", "LU_NOSE_TO", "LU_NOSE_TO", null };
            yield return new object?[] { aircraft, "LINEUP", "LU_LANDING_LT", "LU_LANDING_LT", null };
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

    /// <summary>"Radar and predictive windshear: ON and AUTO" has ONE step behind it in all the
    /// aircraft's flows: a read-only check right after the Taxi flow's two radar writes, in the After
    /// Landing check's shape.</summary>
    [Theory]
    [MemberData(nameof(AircraftOnly))]
    public void The_taxi_radar_readback_is_completed_by_one_read_only_check_after_both_radar_writes(string aircraft)
    {
        switch (aircraft)
        {
            case "Fenix": AssertTaxiRadarCheck(FenixFlowDefinitions.Build(), FenixChecklistDefinitions.Build()); break;
            case "A32NX": AssertTaxiRadarCheck(FbwA320FlowDefinitions.Build(), FbwA320ChecklistDefinitions.Build()); break;
            case "A330": AssertTaxiRadarCheck(HwA330FlowDefinitions.Build(), HwA330ChecklistDefinitions.Build()); break;
            default: throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null);
        }
    }

    private static void AssertTaxiRadarCheck<TExec, TState>(
        List<FlowDefinition<TState>> flows, List<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var steps = flows.Single(f => f.Id == "TAXI").Steps;
        var ids = steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf("TX_WXR") + 1, ids.IndexOf("TX_PWS"));
        Assert.Equal(ids.IndexOf("TX_PWS") + 1, ids.IndexOf(WxrOnCheck));

        var check = steps[ids.IndexOf(WxrOnCheck)];
        var line = groups.Single(g => g.Id == "TAXI_CL").Items.Single(i => i.Id == WxrOnLine);
        AssertReadOnlyCheck(check, WxrOnLabel, "FO_WXR_ON_AUTO");
        Assert.Equal(line.Label, check.Label);
        Assert.Equal(line.StateFieldName, check.ConditionFieldName);
        Assert.Equal(new[] { WxrOnLine }, check.LinkedChecklistItemIds);
        Assert.Equal(new[] { WxrOnCheck },
            flows.SelectMany(f => f.Steps).Where(s => s.LinkedChecklistItemIds.Contains(WxrOnLine)).Select(s => s.Id));
    }

    /// <summary>The takeoff-memo items set earlier (the ground spoilers and the takeoff flaps in After
    /// Start, the signs in Preflight and Before Start) are confirmed by ONE read-only check of the
    /// Taxi flow, right before the takeoff config test (the last step), reading the evaluator's
    /// FO_TAXI_MEMO_OK: it completes the four Taxi checklist lines those items answer, and no other
    /// step completes any of them.</summary>
    [Theory]
    [MemberData(nameof(AircraftOnly))]
    public void The_takeoff_memo_items_are_completed_by_one_read_only_check_before_the_config_test(string aircraft)
    {
        switch (aircraft)
        {
            case "Fenix": AssertMemoCheck(FenixFlowDefinitions.Build(), FenixChecklistDefinitions.Build()); break;
            case "A32NX": AssertMemoCheck(FbwA320FlowDefinitions.Build(), FbwA320ChecklistDefinitions.Build()); break;
            case "A330": AssertMemoCheck(HwA330FlowDefinitions.Build(), HwA330ChecklistDefinitions.Build()); break;
            default: throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null);
        }
    }

    private static void AssertMemoCheck<TExec, TState>(
        List<FlowDefinition<TState>> flows, List<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var steps = flows.Single(f => f.Id == "TAXI").Steps;
        var ids = steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf(MemoCheck) + 1, ids.IndexOf("TX_CONFIG"));
        Assert.Equal("TX_CONFIG", ids[^1]);   // the config test is last (FOA-8, owner choice 12)

        var check = steps[ids.IndexOf(MemoCheck)];
        AssertReadOnlyCheck(check, MemoLabel, MemoField);
        Assert.Equal(MemoLines, check.LinkedChecklistItemIds);
        var taxiCl = groups.Single(g => g.Id == "TAXI_CL").Items.Select(i => i.Id).ToList();
        Assert.All(MemoLines, id => Assert.Contains(id, taxiCl));
        Assert.Empty(flows.SelectMany(f => f.Steps)
            .Where(s => s.Id != MemoCheck && s.LinkedChecklistItemIds.Any(id => MemoLines.Contains(id)))
            .Select(s => s.Id));
    }

    // A read-only check (the FO-13 shape): waits on one field for 5 s, is skipped aloud on timeout,
    // says "Already set" when the aircraft already agrees, and sends nothing.
    private static void AssertReadOnlyCheck<TState>(FlowStep<TState> check, string label, string field)
        where TState : IFoStateEvaluator
    {
        Assert.Equal(label, check.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, check.ActionType);
        Assert.Equal(field, check.ConditionFieldName);
        Assert.Equal(5, check.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, check.FailurePolicy);
        Assert.True(check.Condition!(1.0));
        Assert.False(check.Condition!(0.0));
        Assert.False(check.Condition!(double.NaN));   // unknown is not set
        Assert.NotNull(check.SkipCondition);
        Assert.Null(check.EventName);
        Assert.Null(check.TargetValue);
        Assert.Null(check.TargetValueProvider);
        Assert.Empty(check.MultiActions);
    }

    // -----------------------------------------------------------------------
    // The audit: lines a finished flow latches with no step behind them
    // -----------------------------------------------------------------------

    /// <summary>Per aircraft, the lines a finished flow latches (its CompletionGroupIds) that the app
    /// can read or act on (an Auto line or an action line; a line the pilot confirms is a Reminder)
    /// and that no step of that flow completes, as "FLOW/LINE" (FlowChecklistLinkAudit.UndeliveredLines,
    /// the PMDG 777's audit). Such a line is latched whatever the aircraft reads (FO-7). EMPTY on all
    /// three since 2026-10-09 (FOA-8, "made honest"): an entry here is a deliberate, reviewed gap.</summary>
    public static IEnumerable<object[]> KnownUndeliveredLines()
    {
        foreach (var aircraft in Aircraft)
            yield return new object[] { aircraft, Array.Empty<string>() };
    }

    [Theory]
    [MemberData(nameof(KnownUndeliveredLines))]
    public void Lines_latched_with_no_delivering_step_are_the_known_set(string aircraft, string[] known)
        => Assert.Equal(known, UndeliveredLines(aircraft));

    private static List<string> UndeliveredLines(string aircraft) => aircraft switch
    {
        "Fenix" => Undelivered(FenixFlowDefinitions.Build(), FenixChecklistDefinitions.Build()),
        "A32NX" => Undelivered(FbwA320FlowDefinitions.Build(), FbwA320ChecklistDefinitions.Build()),
        "A330" => Undelivered(HwA330FlowDefinitions.Build(), HwA330ChecklistDefinitions.Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };

    private static List<string> Undelivered<TExec, TState>(
        List<FlowDefinition<TState>> flows, List<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return FlowChecklistLinkAudit.UndeliveredLines(
            FlowChecklistLinkAudit.FlowsWithItems(flows, groups, f => f.CompletionGroupIds(ids.Contains)));
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
