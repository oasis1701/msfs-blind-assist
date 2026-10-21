using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A380 = MSFSBlindAssist.FirstOfficer.FBWA380;
using HwA330 = MSFSBlindAssist.FirstOfficer.HWA330;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Airbus Securing (owner, 2026-10-14): APU master OFF, then wait for the APU flap to close,
/// THEN external power OFF, then the batteries — FlyByWire's own order, because the flap needs
/// APU or external power to close. The FlyByWire-based jets wait on the published flap
/// position; the Fenix publishes none and waits a fixed 2 minutes after an APU this run
/// switched off. The Fenix, A32NX and A330 Securing flows also latch the Securing Checklist.
/// </summary>
public class AirbusSecuringPowerOffTests
{
    private static List<string> Ids<T>(FlowDefinition<T> f) where T : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator
        => f.Steps.Select(s => s.Id).ToList();

    private static FlowDefinition<Fenix.FenixStateEvaluator> FenixSecure() => Fenix.FenixFlowDefinitions.Build().Single(f => f.Id == "SECURE");
    private static FlowDefinition<A320.FbwA320StateEvaluator> A32nxSecure() => A320.FbwA320FlowDefinitions.Build().Single(f => f.Id == "SECURE");
    private static FlowDefinition<HwA330.HwA330StateEvaluator> A330Secure() => HwA330.HwA330FlowDefinitions.Build().Single(f => f.Id == "SECURE");
    private static FlowDefinition<A380.FbwA380StateEvaluator> A380Secure() => A380.FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE");

    private static void AssertInOrder(List<string> ids, params string[] expected)
    {
        var positions = expected.Select(e => ids.IndexOf(e)).ToArray();
        Assert.DoesNotContain(-1, positions);
        Assert.True(positions.SequenceEqual(positions.OrderBy(p => p)), string.Join(", ", ids));
    }

    [Fact]
    public void A320_family_order_is_apu_master_wait_external_power_batteries()
    {
        foreach (var ids in new[] { Ids(FenixSecure()), Ids(A32nxSecure()), Ids(A330Secure()) })
            AssertInOrder(ids, "SC_APUBLEED", "SC_APUMASTER", "SC_APU_WAIT", "SC_EXTPWR_OFF", "SC_BAT1", "SC_BAT2");
    }

    [Fact]
    public void A380_order_is_apu_master_wait_external_power_batteries()
        => AssertInOrder(Ids(A380Secure()), "SC_APUBLEED_OFF", "SC_APU_OFF", "SC_APU_WAIT", "SC_EXTPWR_OFF", "SC_BAT_OFF");

    [Fact]
    public void FlyByWire_jets_wait_on_the_published_flap_position()
    {
        AssertFlapWait(A32nxSecure().Steps.Single(s => s.Id == "SC_APU_WAIT"));
        AssertFlapWait(A330Secure().Steps.Single(s => s.Id == "SC_APU_WAIT"));
        AssertFlapWait(A380Secure().Steps.Single(s => s.Id == "SC_APU_WAIT"));
    }

    private static void AssertFlapWait<T>(FlowStep<T> w) where T : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator
    {
        Assert.Equal(FlowStepActionType.WaitForCondition, w.ActionType);
        Assert.Equal("A32NX_APU_FLAP_OPEN_PERCENTAGE", w.ConditionFieldName);
        Assert.True(w.Condition!(0) && !w.Condition(1) && !w.Condition(100) && !w.Condition(double.NaN));
        Assert.Equal(150, w.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, w.FailurePolicy);
        Assert.Null(w.RunOnlyIfStepSent);   // a closed flap passes at once
        Assert.Equal("APU flap to close", w.Label);
    }

    /// <summary>The flap wait reads the flap through the SimConnect cache, so every FlyByWire jet's evaluator
    /// must poll it and its definition must register it OnRequest and silent (never Continuous without
    /// IsAnnounced, FOA-6): the A32NX and A330 get it from the SD APU page rows, the A380 declares it.</summary>
    [Fact]
    public void The_apu_flap_is_polled_and_registered_silent_on_every_FlyByWire_jet()
    {
        const string Flap = "A32NX_APU_FLAP_OPEN_PERCENTAGE";
        foreach (var (airframe, registered, polled) in new (string, IReadOnlyDictionary<string, SimVarDefinition>, IReadOnlyList<string>)[]
        {
            ("A32NX", new FlyByWireA320Definition().GetVariables(), new A320.FbwA320StateEvaluator().OnRequestPollFields),
            ("A339X", new HeadwindA330Definition().GetVariables(), new HwA330.HwA330StateEvaluator().OnRequestPollFields),
            ("A380", new FlyByWireA380Definition().GetVariables(), new A380.FbwA380StateEvaluator().OnRequestPollFields),
        })
        {
            Assert.True(polled.Contains(Flap), airframe + ": the First Officer does not poll the APU flap");
            Assert.True(registered.TryGetValue(Flap, out var def), airframe + ": the APU flap is not registered");
            Assert.Equal(UpdateFrequency.OnRequest, def!.UpdateFrequency);
            Assert.False(def.IsAnnounced, airframe + ": the APU flap must not be announced");
        }
    }

    [Fact]
    public void Fenix_waits_two_minutes_only_after_this_runs_apu_master_off()
    {
        var w = FenixSecure().Steps.Single(s => s.Id == "SC_APU_WAIT");
        Assert.Equal(FlowStepActionType.WaitSeconds, w.ActionType);
        Assert.Equal(120, w.WaitSeconds);
        Assert.Equal("SC_APUMASTER", w.RunOnlyIfStepSent);
        Assert.Equal("APU flap to close", w.Label);
    }

    [Fact]
    public void A32nx_and_a330_external_power_skip_only_on_a_definite_off()
    {
        // A NaN (unread) switch must NOT skip the absolute OFF write.
        Assert.False(A32nxSecure().Steps.Single(s => s.Id == "SC_EXTPWR_OFF").SkipCondition!(new A320.FbwA320StateEvaluator()));
        Assert.False(A330Secure().Steps.Single(s => s.Id == "SC_EXTPWR_OFF").SkipCondition!(new HwA330.HwA330StateEvaluator()));
    }

    [Fact]
    public void A320_family_securing_latches_the_securing_checklist_through_its_steps()
    {
        foreach (var (related, links) in new[]
        {
            (FenixSecure().RelatedChecklistGroupIds, Links(FenixSecure())),
            (A32nxSecure().RelatedChecklistGroupIds, Links(A32nxSecure())),
            (A330Secure().RelatedChecklistGroupIds, Links(A330Secure())),
        })
        {
            Assert.Contains("SECURE", related);
            Assert.Contains("SECURING_CL", related);
            Assert.Contains("SCC_OXY", links["SC_OXY"]);
            Assert.Contains("SCC_EMEREXIT", links["SC_EMEREXIT"]);
            Assert.Contains("SCC_BAT", links["SC_BAT1"]);
            Assert.Contains("SCC_BAT", links["SC_BAT2"]);
        }
    }

    private static Dictionary<string, string[]> Links<T>(FlowDefinition<T> f) where T : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator
        => f.Steps.ToDictionary(s => s.Id, s => s.LinkedChecklistItemIds.ToArray());
}
