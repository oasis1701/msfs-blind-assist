using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A380 = MSFSBlindAssist.FirstOfficer.FBWA380;
using HwA330 = MSFSBlindAssist.FirstOfficer.HWA330;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;
using Md11 = MSFSBlindAssist.FirstOfficer.MD11;
using B757 = MSFSBlindAssist.FirstOfficer.BBS757;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Every profile's flows: a step's <c>RunOnlyIfStepSent</c> must name an EARLIER switch step
/// (SetSwitch / SetSwitchMultiple) of the SAME flow. FlowManager only records steps it has
/// already sent in this run, so a typo or a later step never matches and the wait would then
/// pass silently on every run (FlowManagerRunOnlyIfSentTests pins the engine half).
/// </summary>
public class FlowStepRunOnlyIfSentIdTests
{
    public static IEnumerable<object[]> Profiles() => new[]
    {
        new object[] { "PMDG 737" }, new object[] { "PMDG 777" }, new object[] { "Fenix A320" },
        new object[] { "FBW A32NX" }, new object[] { "FBW A380" }, new object[] { "Headwind A330" },
        new object[] { "iFly 737 MAX8" }, new object[] { "TFDi MD-11" }, new object[] { "BlueBird 757" },
    };

    internal static IEnumerable<(string Flow, (string Id, FlowStepActionType Type, string? After)[] Steps)> Flows(string profile) =>
        profile switch
        {
            "PMDG 737" => Project(B737.PMDG737FlowDefinitions.Build()),
            "PMDG 777" => Project(PMDG777FlowDefinitions.Build()),
            "Fenix A320" => Project(Fenix.FenixFlowDefinitions.Build()),
            "FBW A32NX" => Project(A320.FbwA320FlowDefinitions.Build()),
            "FBW A380" => Project(A380.FbwA380FlowDefinitions.Build()),
            "Headwind A330" => Project(HwA330.HwA330FlowDefinitions.Build()),
            "iFly 737 MAX8" => Project(IFly737.IFly737FlowDefinitions.Build()),
            "TFDi MD-11" => Project(Md11.Md11FoFlowDefinitions.Build()),
            "BlueBird 757" => Project(B757.Bbs757FoFlowDefinitions.Build()),
            _ => throw new System.ArgumentException(profile),
        };

    private static IEnumerable<(string, (string, FlowStepActionType, string?)[])> Project<TState>(
        IEnumerable<FlowDefinition<TState>> flows) where TState : IFoStateEvaluator =>
        flows.Select(f => (f.Id, f.Steps.Select(s => (s.Id, s.ActionType, s.RunOnlyIfStepSent)).ToArray()))
             .ToList();

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_run_only_if_sent_names_an_earlier_switch_step_of_the_same_flow(string profile)
    {
        var problems = new List<string>();
        foreach (var (flow, steps) in Flows(profile))
            for (int i = 0; i < steps.Length; i++)
            {
                var (id, _, after) = steps[i];
                if (after == null) continue;
                bool ok = steps.Take(i).Any(s => s.Id == after
                    && s.Type is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple);
                if (!ok) problems.Add($"{flow}/{id} runs only if '{after}' was sent, which is not an earlier switch step of {flow}");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_audit_sees_the_737_cooldown_waits()
    {
        Assert.Equal("SE_APU_OFF", Flows("PMDG 737").Single(f => f.Flow == "SECURE").Steps.Single(s => s.Id == "SE_APU_COOLDOWN").After);
        Assert.Equal("SE_APU_OFF", Flows("PMDG 737").Single(f => f.Flow == "SECURE").Steps.Single(s => s.Id == "SE_APU_WAIT").After);
        Assert.Equal("SE_APU_OFF", Flows("iFly 737 MAX8").Single(f => f.Flow == "SECURE").Steps.Single(s => s.Id == "SE_APU_WAIT").After);
    }

    [Fact]
    public void The_audit_sees_the_fenix_cooldown_wait()
        => Assert.Equal("SC_APUMASTER", Flows("Fenix A320").Single(f => f.Flow == "SECURE").Steps.Single(s => s.Id == "SC_APU_WAIT").After);
}
