using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A320-family Landing flow (Fenix A320, FBW A32NX, Headwind A330; FOA-11). After Takeoff disarms
/// the ground spoilers, and nothing armed them again for landing: a pilot landed with them disarmed
/// while After Start's "Ground spoilers: ARMED" stayed ticked as the record of the departure (report,
/// 2026-10-16). The flow, run on final once the gear is down, arms them, then checks the landing memo
/// and the gear, read only (the FBW A380's Landing flow shape, FlyByWire's order: gear down, ground
/// spoilers ARM, Landing Checklist). The arm leaves an extended speed brake alone: on the FlyByWire jets
/// SPOILERS_ARM_ON snaps the speed brake handle back to retracted (measured on the A32NX 2026-10-16: the
/// handle went from 0.5 to 0 and the spoilers armed), and the Fenix's arm write moves its lever to ARM.
/// These are the structural and pure tests; the runs are in
/// <see cref="A320FamilyLandingFlowBehaviourTests{TExec, TState}"/>. Each aircraft has a class of its
/// own below, supplying its raw values (the A330 duplicates the A32NX profile, [FOA-3]).
/// </summary>
public abstract class A320FamilyLandingFlowTests<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : LVarStateEvaluator
{
    internal static readonly string[] StepIds = { "LD_SPOILERS_ARM", "LD_MEMO_CHECK", "LD_GEAR_DOWN_CHECK" };
    internal const string MemoField = "FO_LDG_MEMO_OK";
    internal const string MemoLabel = "Seat belts and flaps: set for landing";
    internal static readonly string[] MemoLines = { "LDC_MEMO_SIGNS", "LDC_MEMO_FLAPS" };

    protected abstract TState NewState();
    protected abstract List<ChecklistGroup<TExec, TState>> BuildChecklists();
    protected abstract List<FlowDefinition<TState>> BuildFlows();

    // The ground-spoiler arm state and its raw values: armed, disarmed.
    protected abstract string SpoilersVar { get; }
    protected abstract double[] SpoilersRaw { get; }

    // The speed brake, and raw readings the arm must act over (retracted, or the Fenix lever at ARM
    // or stowed) and must leave alone (extended).
    protected abstract string SpeedBrakeVar { get; }
    protected abstract double[] SpeedBrakeRetracted { get; }
    protected abstract double[] SpeedBrakeExtended { get; }

    // What the landing memo reads: the seat belts (on, off), the flap lever (0-4) and the GPWS
    // LDG FLAP 3 switch (0 off, 1 on).
    protected abstract string SeatBeltsVar { get; }
    protected abstract double[] SeatBeltsRaw { get; }
    protected abstract string FlapLeverVar { get; }
    protected abstract string Flap3Var { get; }

    private FlowDefinition<TState> Landing() => BuildFlows().Single(f => f.Id == "LANDING");
    private FlowStep<TState> Step(string id) => Landing().Steps.Single(s => s.Id == id);
    private Dictionary<string, ChecklistItem<TExec, TState>> Items() =>
        BuildChecklists().SelectMany(g => g.Items).ToDictionary(i => i.Id);

    private TState StateWith(params (string Key, double Value)[] seed)
    {
        var state = NewState();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(seed));
        return state;
    }

    [Fact]
    public void The_landing_flow_runs_between_approach_and_after_landing()
    {
        var flows = BuildFlows();
        var ids = flows.Select(f => f.Id).ToList();
        Assert.Equal(ids.IndexOf("APPROACH") + 1, ids.IndexOf("LANDING"));
        Assert.Equal(ids.IndexOf("LANDING") + 1, ids.IndexOf("AFTER_LANDING"));
        Assert.Equal("Landing", Landing().Name);

        // Its own action group sits before the Landing Checklist, between Approach and After Landing.
        var groups = BuildChecklists().Select(g => g.Id).ToList();
        Assert.Equal(groups.IndexOf("APPROACH_CL") + 1, groups.IndexOf("LANDING"));
        Assert.Equal(groups.IndexOf("LANDING") + 1, groups.IndexOf("LANDING_CL"));
        Assert.Equal(groups.IndexOf("LANDING_CL") + 1, groups.IndexOf("AFTER_LANDING"));

        // A finished run latches the Landing group and the Landing Checklist.
        var groupIds = groups.ToHashSet();
        Assert.Equal(new[] { "LANDING", "LANDING_CL" }, Landing().CompletionGroupIds(groupIds.Contains).ToArray());
    }

    [Fact]
    public void The_landing_flow_arms_the_ground_spoilers_then_checks_the_memo_and_ends_with_the_gear()
        => Assert.Equal(StepIds, Landing().Steps.Select(s => s.Id).ToArray());

    /// <summary>The arm is After Start's own verified write, completing its action line and the Landing
    /// Checklist's "Spoilers: ARMED", which reads the same state.</summary>
    [Fact]
    public void The_arm_step_sends_after_starts_write_and_completes_both_spoiler_lines()
    {
        var arm = Step("LD_SPOILERS_ARM");
        var afterStart = BuildFlows().Single(f => f.Id == "AFTER_START").Steps.Single(s => s.Id == "AS_SPOILERS_ARM");
        Assert.Equal(FlowStepActionType.SetSwitch, arm.ActionType);
        Assert.Equal("Ground spoilers: ARMED", arm.Label);
        Assert.Equal(afterStart.EventName, arm.EventName);
        Assert.Equal(afterStart.TargetValue, arm.TargetValue);
        Assert.Equal(new[] { "LD_SPOILERS_ARM", "LDC_MEMO_SPLRS" }, arm.LinkedChecklistItemIds);

        var items = Items();
        var line = items["LD_SPOILERS_ARM"];
        Assert.Equal("LANDING", line.GroupId);
        Assert.Equal("Ground spoilers: ARMED", line.Label);
        Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
        Assert.NotNull(line.CheckAction);
        Assert.Equal(SpoilersVar, line.StateFieldName);
        Assert.Equal(SpoilersVar, items["LDC_MEMO_SPLRS"].StateFieldName);
        Assert.True(line.EvaluateState(SpoilersRaw[0]));
        Assert.False(line.EvaluateState(SpoilersRaw[1]));

        // No other step of any flow completes the Landing Checklist's spoilers line.
        Assert.Equal(new[] { "LD_SPOILERS_ARM" }, BuildFlows().SelectMany(f => f.Steps)
            .Where(s => s.LinkedChecklistItemIds.Contains("LDC_MEMO_SPLRS")).Select(s => s.Id));
    }

    [Fact]
    public void The_arm_is_already_set_only_when_the_spoilers_read_armed()
    {
        var skip = Step("LD_SPOILERS_ARM").SkipCondition!;
        Assert.True(skip(StateWith((SpoilersVar, SpoilersRaw[0]))));
        Assert.False(skip(StateWith((SpoilersVar, SpoilersRaw[1]))));
        Assert.False(skip(StateWith()));   // unread: not armed
    }

    /// <summary>The flow step and the checklist line alike leave an extended speed brake alone, and say so;
    /// a retracted (or unread) one is armed over.</summary>
    [Fact]
    public void The_arm_leaves_an_extended_speed_brake_alone_in_the_flow_and_the_checklist()
    {
        var step = Step("LD_SPOILERS_ARM");
        var line = Items()["LD_SPOILERS_ARM"];
        Assert.Equal(AirbusGroundSpoilers.LeaveAloneText, step.LeaveAloneText);
        Assert.Equal(AirbusGroundSpoilers.LeaveAloneText, line.LeaveAloneText);

        // The ground spoilers read disarmed alongside, except on the Fenix, where one lever is both.
        (string Key, double Value)[] With(double speedBrake) => SpeedBrakeVar == SpoilersVar
            ? new[] { (SpeedBrakeVar, speedBrake) }
            : new[] { (SpeedBrakeVar, speedBrake), (SpoilersVar, SpoilersRaw[1]) };

        foreach (double extended in SpeedBrakeExtended)
        {
            var s = StateWith(With(extended));
            Assert.True(step.LeaveAloneWhen!(s), $"step armed over a speed brake at {extended}");
            Assert.True(line.LeaveAloneWhen!(s), $"line armed over a speed brake at {extended}");
        }
        foreach (double retracted in SpeedBrakeRetracted)
        {
            var s = StateWith(With(retracted));
            Assert.False(step.LeaveAloneWhen!(s), $"step left a speed brake at {retracted} alone");
            Assert.False(line.LeaveAloneWhen!(s), $"line left a speed brake at {retracted} alone");
        }
        Assert.False(step.LeaveAloneWhen!(StateWith()));
        Assert.False(line.LeaveAloneWhen!(StateWith()));
    }

    /// <summary>The memo check (the Taxi memo check's shape): one read-only field for the Landing
    /// Checklist's seat-belts and flaps lines, 5 s, skipped aloud on timeout, never writing.</summary>
    [Fact]
    public void The_memo_check_is_read_only_and_completes_the_seat_belts_and_flaps_lines()
    {
        var check = Step("LD_MEMO_CHECK");
        Assert.Equal(MemoLabel, check.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, check.ActionType);
        Assert.Equal(MemoField, check.ConditionFieldName);
        Assert.Equal(5, check.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, check.FailurePolicy);
        Assert.True(check.Condition!(1.0));
        Assert.False(check.Condition!(0.0));
        Assert.False(check.Condition!(double.NaN));
        Assert.NotNull(check.SkipCondition);
        Assert.Null(check.EventName);
        Assert.Null(check.TargetValue);
        Assert.Null(check.TargetValueProvider);
        Assert.Empty(check.MultiActions);
        Assert.Equal(MemoLines, check.LinkedChecklistItemIds);

        Assert.Empty(BuildFlows().SelectMany(f => f.Steps)
            .Where(s => s.Id != "LD_MEMO_CHECK" && s.LinkedChecklistItemIds.Any(id => MemoLines.Contains(id)))
            .Select(s => s.Id));
    }

    /// <summary>LAST (FO-13): the gear confirmed down, read only, on the Landing Checklist line's own
    /// field; 20 s, skipped aloud on timeout, never writing.</summary>
    [Fact]
    public void The_gear_check_is_last_read_only_and_completes_the_gear_line()
    {
        var gear = Landing().Steps.Last();
        var line = Items()["LDC_MEMO_GEAR"];
        Assert.Equal("LD_GEAR_DOWN_CHECK", gear.Id);
        Assert.Equal("Landing gear: DOWN", gear.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, gear.ActionType);
        Assert.Equal(line.StateFieldName, gear.ConditionFieldName);
        Assert.Equal(20, gear.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, gear.FailurePolicy);
        Assert.False(gear.Condition!(double.NaN));
        Assert.NotNull(gear.SkipCondition);
        Assert.Null(gear.EventName);
        Assert.Empty(gear.MultiActions);
        Assert.Equal(new[] { "LDC_MEMO_GEAR" }, gear.LinkedChecklistItemIds);
        Assert.Equal(new[] { "LD_GEAR_DOWN_CHECK" }, BuildFlows().SelectMany(f => f.Steps)
            .Where(s => s.LinkedChecklistItemIds.Contains("LDC_MEMO_GEAR")).Select(s => s.Id));
    }

    // What one Auto line says about the aircraft, from its own definition: null while any variable it
    // reads is unknown, else whether its own condition holds.
    private static bool? LineReads(ChecklistItem<TExec, TState> line, TState state)
    {
        double primary = state.GetValue(line.StateFieldName!);
        var extra = line.AdditionalStateFields.Select(state.GetValue).ToArray();
        if (double.IsNaN(primary) || extra.Any(double.IsNaN)) return null;
        return line.EvaluateState(primary) && extra.All(line.EvaluateAdditionalState);
    }

    /// <summary>The memo field reads exactly the AND of its two lines' own conditions, from the
    /// aircraft's own checklist definitions: 1 when both hold, 0 when either does not, NaN while any
    /// variable they read is unknown (unknown is not false).</summary>
    [Fact]
    public void The_memo_check_equals_the_AND_of_its_two_lines_over_a_grid()
    {
        var items = Items();
        var lines = MemoLines.Select(id => items[id]).ToList();
        Assert.All(lines, l => Assert.True(l.IsAutoDetectable, l.Id));

        var seeds = new List<(string Key, double Value)[]>();
        int?[] belts = { 0, 1, null };
        double?[] levers = { 0, 1, 2, 3, 4, null };
        double?[] flap3 = { 0, 1, null };
        foreach (var b in belts)
            foreach (var lever in levers)
                foreach (var f3 in flap3)
                {
                    var seed = new List<(string Key, double Value)>();
                    if (b is { } bi) seed.Add((SeatBeltsVar, SeatBeltsRaw[bi]));
                    if (lever is { } l) seed.Add((FlapLeverVar, l));
                    if (f3 is { } f) seed.Add((Flap3Var, f));
                    seeds.Add(seed.ToArray());
                }

        int ones = 0, zeros = 0, unknowns = 0;
        foreach (var seed in seeds)
        {
            var state = StateWith(seed);
            var reads = lines.Select(line => LineReads(line, state)).ToList();
            double expected = reads.Contains(null) ? double.NaN : reads.All(r => r == true) ? 1.0 : 0.0;
            double actual = state.GetValue(MemoField);
            Assert.True(expected.Equals(actual),
                $"{string.Join(", ", seed.Select(x => $"{x.Key}={x.Value}"))}: memo {actual}, lines {expected}");
            if (double.IsNaN(expected)) unknowns++;
            else if (expected > 0.5) ones++;
            else zeros++;
        }
        Assert.True(ones > 0 && zeros > 0 && unknowns > 0, $"1: {ones}, 0: {zeros}, unknown: {unknowns}");
    }
}

/// <summary>The Fenix A320 (see <see cref="A320FamilyLandingFlowTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyLandingFlowFenixTests
    : A320FamilyLandingFlowTests<FenixActionExecutor, FenixStateEvaluator>
{
    protected override FenixStateEvaluator NewState() => new();
    protected override List<ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>> BuildChecklists() =>
        FenixChecklistDefinitions.Build();
    protected override List<FlowDefinition<FenixStateEvaluator>> BuildFlows() => FenixFlowDefinitions.Build();
    // A_FC_SPEEDBRAKE: 0 ARMED, 1 stowed (disarmed), 2 half, 3 full.
    protected override string SpoilersVar => "A_FC_SPEEDBRAKE";
    protected override double[] SpoilersRaw => new[] { 0.0, 1.0 };
    protected override string SpeedBrakeVar => "A_FC_SPEEDBRAKE";
    protected override double[] SpeedBrakeRetracted => new[] { 0.0, 1.0 };
    protected override double[] SpeedBrakeExtended => new[] { 1.5, 2.0, 3.0 };
    protected override string SeatBeltsVar => "S_OH_SIGNS";
    protected override double[] SeatBeltsRaw => new[] { 1.0, 0.0 };
    protected override string FlapLeverVar => "S_FC_FLAPS";
    protected override string Flap3Var => "S_OH_GPWS_LDG_FLAP3";
}

/// <summary>The FlyByWire A32NX (see <see cref="A320FamilyLandingFlowTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyLandingFlowA32nxTests
    : A320FamilyLandingFlowTests<FbwA320ActionExecutor, FbwA320StateEvaluator>
{
    protected override FbwA320StateEvaluator NewState() => new();
    protected override List<ChecklistGroup<FbwA320ActionExecutor, FbwA320StateEvaluator>> BuildChecklists() =>
        FbwA320ChecklistDefinitions.Build();
    protected override List<FlowDefinition<FbwA320StateEvaluator>> BuildFlows() => FbwA320FlowDefinitions.Build();
    protected override string SpoilersVar => "A32NX_SPOILERS_ARMED";
    protected override double[] SpoilersRaw => new[] { 1.0, 0.0 };
    // A32NX_SPOILERS_HANDLE_POSITION: 0 retracted (also while armed), 1 full.
    protected override string SpeedBrakeVar => "A32NX_SPOILERS_HANDLE_POSITION";
    protected override double[] SpeedBrakeRetracted => new[] { 0.0, 0.04 };
    protected override double[] SpeedBrakeExtended => new[] { 0.06, 0.5, 1.0 };
    protected override string SeatBeltsVar => "CABIN SEATBELTS ALERT SWITCH";
    protected override double[] SeatBeltsRaw => new[] { 1.0, 0.0 };
    protected override string FlapLeverVar => "A32NX_FLAPS_HANDLE_INDEX";
    protected override string Flap3Var => "A32NX_GPWS_FLAPS3";
}

/// <summary>The Headwind A330 (see <see cref="A320FamilyLandingFlowTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyLandingFlowA330Tests
    : A320FamilyLandingFlowTests<HwA330ActionExecutor, HwA330StateEvaluator>
{
    protected override HwA330StateEvaluator NewState() => new();
    protected override List<ChecklistGroup<HwA330ActionExecutor, HwA330StateEvaluator>> BuildChecklists() =>
        HwA330ChecklistDefinitions.Build();
    protected override List<FlowDefinition<HwA330StateEvaluator>> BuildFlows() => HwA330FlowDefinitions.Build();
    protected override string SpoilersVar => "A32NX_SPOILERS_ARMED";
    protected override double[] SpoilersRaw => new[] { 1.0, 0.0 };
    protected override string SpeedBrakeVar => "A32NX_SPOILERS_HANDLE_POSITION";
    protected override double[] SpeedBrakeRetracted => new[] { 0.0, 0.04 };
    protected override double[] SpeedBrakeExtended => new[] { 0.06, 0.5, 1.0 };
    protected override string SeatBeltsVar => "CABIN SEATBELTS ALERT SWITCH";
    protected override double[] SeatBeltsRaw => new[] { 1.0, 0.0 };
    protected override string FlapLeverVar => "A32NX_FLAPS_HANDLE_INDEX";
    protected override string Flap3Var => "A32NX_GPWS_FLAPS3";
}
