using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;          // PMDG777FlowDefinitions
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A380 = MSFSBlindAssist.FirstOfficer.FBWA380;
using HwA330 = MSFSBlindAssist.FirstOfficer.HWA330;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Every First Officer profile's flows: a step's <c>RequiresStepId</c> must name a step that
/// runs EARLIER in the SAME flow, and the step must carry its own skip text. FlowManager only
/// knows the steps it has already passed in this run, so a typo, a later step or a step of
/// another flow never matches — and the dependent step then silently runs every time, which
/// is the failure the dependency exists to prevent (FlowManagerStepDependencyTests pins the
/// engine half).
/// </summary>
public class FlowStepDependencyIdTests
{
    public static IEnumerable<object[]> Profiles() => new[]
    {
        new object[] { "PMDG 737" }, new object[] { "PMDG 777" }, new object[] { "Fenix A320" },
        new object[] { "FBW A32NX" }, new object[] { "FBW A380" }, new object[] { "Headwind A330" },
        new object[] { "iFly 737 MAX8" },
    };

    // (flow id, ordered steps as (id, requiresStepId, requiresStepSkipText)) for one profile.
    private static IEnumerable<(string Flow, (string Id, string? Requires, string? SkipText)[] Steps)> Flows(string profile) =>
        profile switch
        {
            "PMDG 737" => Project(B737.PMDG737FlowDefinitions.Build()),
            "PMDG 777" => Project(PMDG777FlowDefinitions.Build()),
            "Fenix A320" => Project(Fenix.FenixFlowDefinitions.Build()),
            "FBW A32NX" => Project(A320.FbwA320FlowDefinitions.Build()),
            "FBW A380" => Project(A380.FbwA380FlowDefinitions.Build()),
            "Headwind A330" => Project(HwA330.HwA330FlowDefinitions.Build()),
            "iFly 737 MAX8" => Project(IFly737.IFly737FlowDefinitions.Build()),
            _ => throw new System.ArgumentException(profile),
        };

    private static IEnumerable<(string, (string, string?, string?)[])> Project<TState>(
        IEnumerable<FlowDefinition<TState>> flows) where TState : IFoStateEvaluator =>
        flows.Select(f => (f.Id, f.Steps.Select(s => (s.Id, s.RequiresStepId, s.RequiresStepSkipText)).ToArray()))
             .ToList();

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_step_dependency_names_an_earlier_step_of_the_same_flow(string profile)
    {
        var problems = new List<string>();
        foreach (var (flow, steps) in Flows(profile))
        {
            for (int i = 0; i < steps.Length; i++)
            {
                var (id, requires, skipText) = steps[i];
                if (requires == null) continue;
                if (!steps.Take(i).Any(s => s.Id == requires))
                    problems.Add($"{flow}/{id} requires '{requires}', which is not an earlier step of {flow}");
                if (string.IsNullOrWhiteSpace(skipText))
                    problems.Add($"{flow}/{id} requires '{requires}' but has no RequiresStepSkipText");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_audit_sees_the_pmdg_737_generator_dependencies()
    {
        // Guards the audit itself: were the projection to drop RequiresStepId, every profile
        // would pass vacuously.
        var beforeStart = Flows("PMDG 737").Single(f => f.Flow == "BEFORE_START").Steps;
        Assert.Equal(2, beforeStart.Count(s => s.Requires == "BS_APU_GEN_AVAIL"));
    }
}
