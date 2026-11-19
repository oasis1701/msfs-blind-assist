using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using A320State = MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320StateEvaluator;
using A330State = MSFSBlindAssist.FirstOfficer.HWA330.HwA330StateEvaluator;
using A380State = MSFSBlindAssist.FirstOfficer.FBWA380.FbwA380StateEvaluator;
using FenixState = MSFSBlindAssist.FirstOfficer.Fenix.FenixStateEvaluator;
using IFlyState = MSFSBlindAssist.FirstOfficer.IFly737.IFly737StateEvaluator;
using Md11State = MSFSBlindAssist.FirstOfficer.MD11.Md11FoStateEvaluator;
using B757State = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoStateEvaluator;
using Pmdg737State = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg777State = MSFSBlindAssist.FirstOfficer.AircraftStateEvaluator;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-25, swept over every First Officer profile so a new line cannot regress it: every checklist
/// tick that cuts the engines (masters, start levers, fuel controls, fuel switches), the fuel
/// pumps, a hydraulic pump, the IRS / ADIRS / ADIRU or the batteries is left alone (spoken,
/// nothing written) airborne and with the state unread; every one except an engine cut is also
/// left alone with the engines running on the ground (an engine cut runs with them turning by
/// definition), and none is a blanket refusal: parked with the engines stopped, it goes ahead.
/// A packs-OFF tick is left alone airborne and unread. Every flow step that makes one of these
/// cuts sits behind BOTH an on-ground and an engines-stopped guard: an earlier read-only Stop
/// wait (unread failing it), a requires rule on an engines wait, or its own leave-alone rule that
/// refuses the same states as the tick (an engine cut and a packs step need the ground guard, as
/// their ticks do). And every Securing flow, every power-up flow and the 737 Preflights open with
/// both guards, so they stop whole.
///
/// Found by label, so a new line worded the way its neighbours are is covered without being
/// listed. Before 2026-10-20 the fuel-pump-OFF ticks of the A320 family, the A380, both 737s and
/// the 777 had no guard at all: a tick in flight switched the pumps off.
/// </summary>
public class FoCutGuardSweepTests
{
    private enum Cut { Engine, Pumps, Packs }

    private static readonly Regex EngineCut = new(
        @"(\bengine \d master: off|\bengine masters.*: off|start levers?: cutoff|"
        + @"fuel control( switches)?: cutoff|fuel switches: off)", RegexOptions.IgnoreCase);

    private static readonly Regex PumpIrsBattery = new(
        @"(fuel pumps?( switches)?: (all )?off|hydraulic.*pumps?( \w+)?: off|demand pump selectors: off|\b(electric|engine|demand) pumps: off|"
        + @"primary pump switches: off|\birs\b.*: off|\badirs\b.*: off|\badiru\b.*: off|\bbatter(y|ies)\b.*: off)",
        RegexOptions.IgnoreCase);

    private static readonly Regex PacksOff = new(@"\bpacks?( switches)?: off", RegexOptions.IgnoreCase);

    private static Cut? Classify(string label)
    {
        // A line that turns something ON, or names an OFF inside a longer instruction, is not a cut.
        if (EngineCut.IsMatch(label)) return Cut.Engine;
        if (PumpIrsBattery.IsMatch(label)) return Cut.Pumps;
        if (PacksOff.IsMatch(label)) return Cut.Packs;
        return null;
    }

    /// <summary>A profile under test: its checklists, flows, and an evaluator for (on the ground?,
    /// engines running?), and one with nothing read.</summary>
    private sealed record Profile<TE, TS>(
        string Name,
        List<ChecklistGroup<TE, TS>> Groups,
        List<FlowDefinition<TS>> Flows,
        Func<bool, bool, TS> State,
        Func<TS> Unread)
        where TE : IFoActionExecutor where TS : IFoStateEvaluator;

    // Every key any profile reads its air/ground or engine state from, seeded together: a key an
    // evaluator does not read is ignored.
    private static (string Key, double Value)[] Seeds(bool onGround, bool running)
    {
        double state = running ? 1 : 0;
        var seeds = new List<(string, double)> { ("SIM_ON_GROUND", onGround ? 1 : 0) };
        for (int n = 1; n <= 4; n++) seeds.Add(($"A32NX_ENGINE_STATE:{n}", state));
        return seeds.ToArray();
    }

    private static TS LVar<TS>(TS s, bool onGround, bool running) where TS : MSFSBlindAssist.FirstOfficer.Generic.LVarStateEvaluator
    {
        s.SetSimConnect(SeededSimConnectCache.With(Seeds(onGround, running)));
        double n2 = running ? 60 : 0;
        s.SetEngineN2(n2, n2);
        return s;
    }

    private static Func<string, double?> CacheOf(bool onGround, bool running)
    {
        var d = Seeds(onGround, running).ToDictionary(x => x.Key, x => x.Value);
        return k => d.TryGetValue(k, out var v) ? v : null;
    }

    private static void Sweep<TE, TS>(Profile<TE, TS> p)
        where TE : IFoActionExecutor where TS : IFoStateEvaluator
    {
        var airborne = p.State(false, false);   // airborne with every engine stopped: a relight is still owed
        var running = p.State(true, true);      // on the ground, engines turning
        var parked = p.State(true, false);      // on the ground, engines stopped: the cut is allowed
        var unread = p.Unread();
        var failures = new List<string>();
        int found = 0;

        foreach (var item in p.Groups.Where(g => !g.Id.EndsWith("_CL")).SelectMany(g => g.Items)
                     .Where(i => i.CheckAction != null))
        {
            if (Classify(item.Label) is not Cut cut) continue;
            found++;
            string id = $"{p.Name} {item.GroupId}/{item.Id} \"{item.Label}\"";
            if (item.LeaveAloneWhen == null) { failures.Add($"{id}: no LeaveAloneWhen"); continue; }
            if (string.IsNullOrWhiteSpace(item.LeaveAloneText)) failures.Add($"{id}: no LeaveAloneText");
            if (!item.LeaveAloneWhen(airborne)) failures.Add($"{id}: ticks airborne");
            if (!item.LeaveAloneWhen(unread)) failures.Add($"{id}: ticks with the state unread");
            if (cut == Cut.Pumps && !item.LeaveAloneWhen(running)) failures.Add($"{id}: ticks with the engines running");
            // ...and the guard is not a blanket refusal: parked, the cut goes ahead.
            if (cut != Cut.Engine && item.LeaveAloneWhen(parked)) failures.Add($"{id}: refused parked with the engines stopped");
        }

        foreach (var flow in p.Flows)
        {
            // The guards in force at each step: unconditional read-only Stop waits run before it
            // (a wait with a skip rule may never run; a Skip-policy wait lets the flow carry on).
            var groundGuarded = false;
            var enginesGuarded = false;
            var engineWaits = new HashSet<string>();   // any wait on the engines, for RequiresStepId
            foreach (var step in flow.Steps)
            {
                if (step.ActionType == FlowStepActionType.WaitForCondition && step.ConditionFieldName is string field)
                {
                    bool unreadFails = step.Condition != null && !step.Condition(double.NaN);
                    bool hard = step.FailurePolicy == FlowStepFailurePolicy.Stop && step.SkipCondition == null && unreadFails;
                    if (GroundFields.Contains(field) && hard) groundGuarded = true;
                    if (EngineFields.Contains(field))
                    {
                        if (hard) enginesGuarded = true;
                        if (unreadFails) engineWaits.Add(step.Id);
                    }
                    continue;
                }
                if (step.ActionType is not (FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple)) continue;
                if (Classify(step.Label) is not Cut cut) continue;
                found++;

                var la = step.LeaveAloneWhen;
                bool groundOk = groundGuarded || (la != null && la(airborne) && la(unread));
                bool enginesOk = enginesGuarded
                    || (step.RequiresStepId != null && engineWaits.Contains(step.RequiresStepId))
                    || (la != null && la(running) && la(unread));
                bool ok = cut switch
                {
                    // An engine cut runs with the engines turning by nature, so it needs no
                    // engines guard; but never airborne, exactly as its tick (2026-10-20: the
                    // power-up flows' master/fuel-switch checks rode on the engines guard alone
                    // while their ticks already refused airborne).
                    Cut.Engine => groundOk,
                    Cut.Packs => groundOk,
                    _ => groundOk && enginesOk,
                };
                if (!ok)
                    failures.Add($"{p.Name} flow {flow.Id}/{step.Id} \"{step.Label}\": "
                        + (groundOk ? "" : "no on-ground guard ") + (enginesOk ? "" : "no engines-stopped guard"));
            }
        }

        Assert.True(found > 0, $"{p.Name}: the sweep found no engine, pump, IRS, battery or pack line at all");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Profile<MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320ActionExecutor, A320State> A32nx() => new("A32NX",
        MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320ChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320FlowDefinitions.Build(),
        (g, r) => LVar(new A320State(), g, r), () => new A320State());

    private static Profile<MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor, A330State> HwA330() => new("Headwind A330",
        MSFSBlindAssist.FirstOfficer.HWA330.HwA330ChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.HWA330.HwA330FlowDefinitions.Build(),
        (g, r) => LVar(new A330State(), g, r), () => new A330State());

    private static Profile<MSFSBlindAssist.FirstOfficer.Fenix.FenixActionExecutor, FenixState> Fenix() => new("Fenix",
        MSFSBlindAssist.FirstOfficer.Fenix.FenixChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.Fenix.FenixFlowDefinitions.Build(),
        (g, r) => LVar(new FenixState(), g, r), () => new FenixState());

    private static Profile<MSFSBlindAssist.FirstOfficer.FBWA380.FbwA380ActionExecutor, A380State> A380() => new("A380",
        MSFSBlindAssist.FirstOfficer.FBWA380.FbwA380ChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.FBWA380.FbwA380FlowDefinitions.Build(),
        (g, r) => LVar(new A380State(), g, r), () => new A380State());

    private static Profile<MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor, Pmdg737State> Pmdg737() => new("PMDG 737",
        MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions.Build(),
        (g, r) =>
        {
            var s = new Pmdg737State();
            s.SetCachedValueSource(CacheOf(g, r));
            s.SetEngineN2(r ? 60 : 0, r ? 60 : 0);
            return s;
        },
        () => new Pmdg737State());

    private static Profile<MSFSBlindAssist.FirstOfficer.IFly737.IFly737ActionExecutor, IFlyState> IFly737() => new("iFly 737",
        MSFSBlindAssist.FirstOfficer.IFly737.IFly737ChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.IFly737.IFly737FlowDefinitions.Build(),
        (g, r) =>
        {
            var s = IFly737SecondReviewTests.State(null, Seeds(g, r));
            s.SetEngineN2(r ? 60 : 0, r ? 60 : 0);
            return s;
        },
        () => IFly737SecondReviewTests.State(null));

    private static Profile<AircraftActionExecutor, Pmdg777State> Pmdg777() => new("PMDG 777",
        PMDG777ChecklistDefinitions.Build(), PMDG777FlowDefinitions.Build(),
        (g, r) =>
        {
            var s = new Pmdg777State();
            s.SetCachedValueSource(CacheOf(g, r));
            s.SetEngineN2(r ? 60 : 0, r ? 60 : 0);
            return s;
        },
        () => new Pmdg777State());

    private static Profile<MSFSBlindAssist.FirstOfficer.MD11.Md11FoActionExecutor, Md11State> Md11() => new("MD-11",
        MSFSBlindAssist.FirstOfficer.MD11.Md11FoChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.MD11.Md11FoFlowDefinitions.Build(),
        (g, r) =>
        {
            var s = LVar(new Md11State(), g, r);
            s.SetEngine3N2(r ? 60 : 0);
            return s;
        },
        () => new Md11State());

    private static Profile<MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoActionExecutor, B757State> Bbs757() => new("BlueBird 757",
        MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoChecklistDefinitions.Build(),
        MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoFlowDefinitions.Build(),
        (g, r) =>
        {
            // The 757's battery also needs the APU stopped and external power off (E199): its parked state
            // reads both, so "parked" is the whole aircraft shut down, as for every other cut.
            var s = new B757State();
            s.SetSimConnect(SeededSimConnectCache.With(Seeds(g, r)
                .Append((MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys.ApuState, 0.0))
                .Append((MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys.ExtPwrOn, 0.0)).ToArray()));
            double n2 = r ? 60 : 0;
            s.SetEngineN2(n2, n2);
            return s;
        },
        () => new B757State());

    [Fact]
    public void A320_family() // the A32NX, its Headwind A330 duplicate and the Fenix: one rule for all three
    {
        Sweep(A32nx());
        Sweep(HwA330());
        Sweep(Fenix());
    }

    [Fact]
    public void FlyByWire_A380() => Sweep(A380());

    [Fact]
    public void Boeing_737s()
    {
        Sweep(Pmdg737());
        Sweep(IFly737());
    }

    [Fact]
    public void Pmdg_777() => Sweep(Pmdg777());

    [Fact]
    public void Tfdi_MD11() => Sweep(Md11());

    [Fact]
    public void BlueBird_757() => Sweep(Bbs757());

    // ---- The flows that power the aircraft down or set it up cold open with BOTH guards ---------

    // The fields a guard wait reads: air/ground (the stock flag, the 737s' and 777's synthetic copy
    // of it, and the MD-11 Parking flow's "aircraft stopped", a ground speed at a standstill) and
    // the engines stopped (every profile's FO_ENGINES_OFF / _STOPPED).
    private static readonly HashSet<string> GroundFields = new() { "SIM_ON_GROUND", "FO_ON_GROUND", "GROUND_VELOCITY" };
    private static readonly HashSet<string> EngineFields = new() { "FO_ENGINES_OFF", "FO_ENGINES_STOPPED" };

    // The opening run of read-only waits: one must read the aircraft on the ground and one the
    // engines stopped, each Stop with unread failing it and linking no line, so a flow started in
    // flight or with an engine turning stops whole instead of skipping its pump steps one by one.
    private static void AssertOpensWithGroundAndEnginesGuards<T>(IEnumerable<FlowDefinition<T>> flows, string flowId, string who)
        where T : IFoStateEvaluator
    {
        var opening = flows.Single(f => f.Id == flowId).Steps
            .TakeWhile(s => s.ActionType == FlowStepActionType.WaitForCondition).ToList();
        bool Guard(FlowStep<T> s, HashSet<string> fields) =>
            s.ConditionFieldName is string f && fields.Contains(f) && s.FailurePolicy == FlowStepFailurePolicy.Stop
            && s.SkipCondition == null && s.Condition != null && !s.Condition(double.NaN) && !s.LinkedChecklistItemIds.Any();
        Assert.True(opening.Any(s => Guard(s, GroundFields)), $"{who} {flowId}: no opening on-ground Stop guard");
        Assert.True(opening.Any(s => Guard(s, EngineFields)), $"{who} {flowId}: no opening engines-stopped Stop guard");
    }

    [Fact]
    public void Every_securing_flow_opens_with_both_guards()
    {
        AssertOpensWithGroundAndEnginesGuards(A32nx().Flows, "SECURE", "A32NX");
        AssertOpensWithGroundAndEnginesGuards(HwA330().Flows, "SECURE", "Headwind A330");
        AssertOpensWithGroundAndEnginesGuards(Fenix().Flows, "SECURE", "Fenix");
        AssertOpensWithGroundAndEnginesGuards(A380().Flows, "SECURE", "A380");
        AssertOpensWithGroundAndEnginesGuards(Pmdg737().Flows, "SECURE", "PMDG 737");
        AssertOpensWithGroundAndEnginesGuards(IFly737().Flows, "SECURE", "iFly 737");
        AssertOpensWithGroundAndEnginesGuards(Pmdg777().Flows, "SECURE", "PMDG 777");
        AssertOpensWithGroundAndEnginesGuards(Md11().Flows, "SHUTDOWN", "MD-11");
        AssertOpensWithGroundAndEnginesGuards(Bbs757().Flows, "SECURE", "BlueBird 757");
    }

    [Fact]
    public void The_cold_and_dark_flows_that_switch_pumps_off_open_with_both_guards()
    {
        // The power-up flows check the masters / fuel switches OFF, which their ticks do only on
        // the ground: the flow stops airborne too, so flow and tick agree.
        AssertOpensWithGroundAndEnginesGuards(A32nx().Flows, "ELECTRICAL_POWER_UP", "A32NX");
        AssertOpensWithGroundAndEnginesGuards(HwA330().Flows, "ELECTRICAL_POWER_UP", "Headwind A330");
        AssertOpensWithGroundAndEnginesGuards(Fenix().Flows, "ELECTRICAL_POWER_UP", "Fenix");
        AssertOpensWithGroundAndEnginesGuards(A380().Flows, "COCKPIT_PREP", "A380");
        AssertOpensWithGroundAndEnginesGuards(Md11().Flows, "POWER_UP", "MD-11");
        // The 757's Power Up checks the fuel controls CUTOFF and the electric hydraulic pumps OFF, and its
        // Preflight switches the fuel pumps off: cold and dark only.
        AssertOpensWithGroundAndEnginesGuards(Bbs757().Flows, "POWER_UP", "BlueBird 757");
        AssertOpensWithGroundAndEnginesGuards(Bbs757().Flows, "PREFLIGHT", "BlueBird 757");
        // The 737 Preflights switch the fuel pumps off: cold and dark only, like the 777's
        // Cockpit Preparation and Electrical Power Up (hydraulic pumps, fuel controls).
        AssertOpensWithGroundAndEnginesGuards(MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions.Build(), "PREFLIGHT", "PMDG 737");
        AssertOpensWithGroundAndEnginesGuards(MSFSBlindAssist.FirstOfficer.IFly737.IFly737FlowDefinitions.Build(), "PREFLIGHT", "iFly 737");
        AssertOpensWithGroundAndEnginesGuards(PMDG777FlowDefinitions.Build(), "COCKPIT_PREP", "PMDG 777");
        AssertOpensWithGroundAndEnginesGuards(PMDG777FlowDefinitions.Build(), "ELECTRICAL_POWER_UP", "PMDG 777");
    }

    [Fact]
    public void The_737_engines_stopped_field_is_both_N2_below_10_and_NaN_until_read()
    {
        var p = new Pmdg737State();
        Assert.True(double.IsNaN(p.GetValue(Pmdg737State.EnginesStoppedField)));
        p.SetEngineN2(5, double.NaN);
        Assert.True(double.IsNaN(p.GetValue(Pmdg737State.EnginesStoppedField)));
        p.SetEngineN2(5, 9.9);
        Assert.Equal(1, p.GetValue(Pmdg737State.EnginesStoppedField));
        p.SetEngineN2(5, 10);
        Assert.Equal(0, p.GetValue(Pmdg737State.EnginesStoppedField));

        var i = IFly737SecondReviewTests.State(null);
        Assert.True(double.IsNaN(i.GetValue(IFlyState.EnginesStoppedField)));
        i.SetEngineN2(0, 0);
        Assert.Equal(1, i.GetValue(IFlyState.EnginesStoppedField));
        i.SetEngineN2(60, 0);
        Assert.Equal(0, i.GetValue(IFlyState.EnginesStoppedField));
    }

    // xUnit needs a public theory over the private enum, so it compares the enum's name.
    [Theory]
    [InlineData("Fuel pumps: ALL OFF", "Pumps")]
    [InlineData("Engine 1 master: OFF", "Engine")]
    [InlineData("Packs: OFF", "Packs")]
    [InlineData("Pack switches: OFF", "Packs")]
    [InlineData("Engine start switches: OFF", "")]
    [InlineData("Fuel pumps: ALL ON", "")]
    [InlineData("APU master: OFF", "")]
    [InlineData("Pack flow: NORMAL", "")]
    [InlineData("Engine masters 1 to 4: OFF", "Engine")]
    [InlineData("Fuel Control switches: CUTOFF", "Engine")]
    [InlineData("Fuel switches: OFF", "Engine")]
    [InlineData("Engine start levers: CUTOFF", "Engine")]
    [InlineData("Engine anti-ice: OFF", "")]
    [InlineData("IRS 1, 2 and 3: NAV", "")]
    [InlineData("IRS mode selectors: OFF", "Pumps")]
    [InlineData("ADIRU switch: OFF", "Pumps")]
    [InlineData("Engine 1 master: ON", "")]
    [InlineData("Engine hydraulic pumps: ON", "")]
    [InlineData("Electric hydraulic pumps: OFF", "Pumps")]
    [InlineData("Engine primary pump switches: OFF", "Pumps")]
    [InlineData("Electric pumps: OFF", "Pumps")]
    [InlineData("Demand pumps: OFF", "Pumps")]
    [InlineData("Demand pump selectors: OFF", "Pumps")]
    [InlineData("Batteries: OFF", "Pumps")]
    [InlineData("Battery switch: OFF", "Pumps")]
    public void The_sweep_classifies_labels(string label, string expected) =>
        Assert.Equal(expected, Classify(label)?.ToString() ?? "");
}
