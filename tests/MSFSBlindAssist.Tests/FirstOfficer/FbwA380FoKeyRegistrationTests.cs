// Every variable a FlyByWire-family First Officer READS must be one its aircraft definition
// REGISTERS — and on the A380, every key it WRITES too.
//
// The FO reads through the SimConnect value cache (LVarStateEvaluator.GetValue ->
// GetCachedVariableValue), and only a registered key is ever put there — its own continuous
// stream, or the FO window's once-a-second RequestVariable poll, which does nothing for a key
// with no definition. An unregistered field reads NaN forever: its checklist item never
// confirms, and a hand-tick reverts with "Unable to complete".
//
// The A380 FO writes through FlyByWireA380Definition.ApplyUIVariable, and a key the definition
// declines falls back to SimConnectManager.SetLVar, which merely prepends "L:" and REPORTS
// SUCCESS. For a key the definition has stopped registering, that is a bogus L:var named after
// the old key: the flow step announces done and the cockpit control never moves.
//
// That is exactly how #251 reached this branch: FBW #10855 gave the A380 ONE flight-director
// pushbutton, main retired FD_1_CTL/FD_2_CTL for it, and "Flight directors: ON" would have
// written L:FD_1_CTL and L:FD_2_CTL, announced done, and left the flight directors off.
// A380Fbw10855DeadNameTests caught the baro-unit step beside it, because it knows the FBW
// names #10855 killed; a key MSFSBA itself retires is invisible to it. This file is what sees
// one. (The A32NX and A330 WRITE contract is FoFbwUnclaimedEventKeyTests' — a different
// question, because plain L:var writes the definition never lists are legitimate there.)

using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FbwA380FoKeyRegistrationTests
{
    /// <summary>
    /// Keys the A380 FO writes that the definition deliberately does NOT register, each with the
    /// reason no fallback write ever lands on it. Every entry must still be written and still be
    /// unregistered (<see cref="Every_known_unregistered_a380_write_is_still_one"/>), so a stale
    /// line cannot quietly excuse a future casualty.
    /// </summary>
    private static readonly Dictionary<string, string> KnownUnregisteredA380Writes = new()
    {
        ["BARO_STD"] = "the executor's own name: DispatchCoreAsync fires both baro PUSHes itself",
        ["BARO_QNH"] = "the executor's own name: DispatchCoreAsync fires both baro PULLs itself",
        ["FCU_PUSH_SPEED"] = "the executor's own name for A32NX.FCU_SPD_PUSH (FireFcu)",
        ["FCU_PUSH_HEADING"] = "the executor's own name for A32NX.FCU_HDG_PUSH (FireFcu)",
        ["FCU_PUSH_ALT"] = "the executor's own name for A32NX.FCU_ALT_PUSH (FireFcu)",
        ["AP1_ENGAGE"] = "the executor's own name for A32NX.FCU_AP_1_PUSH (FireFcu)",
        ["FIRE_TEST"] = "a flow step ExecuteStepAsync runs itself (the held fire test)",
        ["TO_CONFIG_TEST"] = "a flow step ExecuteStepAsync runs itself (the held T.O CONFIG press)",
        ["A32NX_RCDR_GROUND_CONTROL_ON"] = "a plain FBW L:var the definition never lists; the "
            + "L:var write IS the control (FBW's own aircraft_preset_procedures.xml writes it the same way)",
    };

    /// <summary>A field the evaluator composes itself (<c>TryGetSyntheticValue</c>) instead of
    /// reading it from the cache — never registered, and not meant to be.</summary>
    private static bool IsSynthetic(string field) => field.StartsWith("FO_", System.StringComparison.Ordinal);

    private static void AssertAllRegistered(
        IReadOnlyDictionary<string, SimVarDefinition> registered, IEnumerable<string> keys,
        string aircraft, string what, IReadOnlyDictionary<string, string>? known = null)
    {
        var missing = keys
            .Where(k => !string.IsNullOrEmpty(k) && !IsSynthetic(k) && !registered.ContainsKey(k)
                        && (known == null || !known.ContainsKey(k)))
            .Distinct()
            .OrderBy(k => k, System.StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"The {aircraft} First Officer {what} key(s) its definition does not register: " +
            string.Join(", ", missing) + ". Re-point each at the key the definition registers " +
            "today — a renamed control keeps working only under its new key.");
    }

    /// <summary>Every key an FO profile READS: the evaluator's poll list, each checklist item's
    /// detection fields, and each flow step's wait and verify fields.</summary>
    private static IEnumerable<string> ReadKeys<TExec, TState>(
        LVarStateEvaluator evaluator,
        IEnumerable<ChecklistGroup<TExec, TState>> groups,
        IEnumerable<FlowDefinition<TState>> flows)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        foreach (var field in evaluator.OnRequestPollFields) yield return field;
        foreach (var item in groups.SelectMany(g => g.Items))
        {
            if (item.StateFieldName != null) yield return item.StateFieldName;
            foreach (var field in item.AdditionalStateFields) yield return field;
        }
        foreach (var step in flows.SelectMany(f => f.Steps))
        {
            if (step.ConditionFieldName != null) yield return step.ConditionFieldName;
            if (step.VerifyFieldName != null) yield return step.VerifyFieldName;
        }
    }

    [Fact]
    public void Every_field_the_a380_fo_reads_is_registered() =>
        AssertAllRegistered(new FlyByWireA380Definition().GetVariables(),
            ReadKeys(new FbwA380StateEvaluator(), FbwA380ChecklistDefinitions.Build(), FbwA380FlowDefinitions.Build()),
            "A380", "reads");

    [Fact]
    public void Every_field_the_a32nx_fo_reads_is_registered() =>
        AssertAllRegistered(new FlyByWireA320Definition().GetVariables(),
            ReadKeys(new FbwA320StateEvaluator(), FbwA320ChecklistDefinitions.Build(), FbwA320FlowDefinitions.Build()),
            "A32NX", "reads");

    [Fact]
    public void Every_field_the_a330_fo_reads_is_registered() =>
        AssertAllRegistered(new HeadwindA330Definition().GetVariables(),
            ReadKeys(new HwA330StateEvaluator(), HwA330ChecklistDefinitions.Build(), HwA330FlowDefinitions.Build()),
            "A339X", "reads");

    private static IEnumerable<string> A380WrittenKeys() =>
        FoFbwUnclaimedEventKeyTests.FlowWrittenKeys(FbwA380FlowDefinitions.Build())
            .Concat(FoFbwUnclaimedEventKeyTests.KeyLiteralsPassedToWrites(
                FoFbwUnclaimedEventKeyTests.ChecklistSourcePath("FBWA380", "FbwA380ChecklistDefinitions.cs")))
            .Concat(FoFbwUnclaimedEventKeyTests.KeyLiteralsPassedToWrites(
                FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("FBWA380", "FbwA380ActionExecutor.cs")));

    [Fact]
    public void Every_key_the_a380_fo_writes_is_registered() =>
        AssertAllRegistered(new FlyByWireA380Definition().GetVariables(), A380WrittenKeys(),
            "A380", "writes", KnownUnregisteredA380Writes);

    [Fact]
    public void Every_known_unregistered_a380_write_is_still_one()
    {
        var registered = new FlyByWireA380Definition().GetVariables();
        var written = A380WrittenKeys().ToHashSet();
        foreach (var key in KnownUnregisteredA380Writes.Keys)
        {
            Assert.True(written.Contains(key), $"{key} is no longer written by the A380 FO; drop its entry.");
            Assert.False(registered.ContainsKey(key),
                $"{key} is registered now, so it needs no excuse here; drop its entry.");
        }
    }
}
