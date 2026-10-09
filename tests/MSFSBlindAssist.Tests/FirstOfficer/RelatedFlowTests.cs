using System;
using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// "Run Related Flow" on the Checklists tab (FirstOfficerForm.RunRelatedFlow): the flow a selected
/// checklist group starts, resolved by <see cref="RelatedFlow.For{TState}"/> in three steps — a flow
/// that names the group, then the flow named after the group's phase (a "_CL" read-back shares its
/// action group's phase), then a flow that names a group of that phase (the A380's flows name only
/// their read-backs). Pinned on stand-in flows for the three steps, and on the A320-family
/// profiles' real flows.
/// </summary>
public class RelatedFlowTests
{
    private static FlowDefinition<FbwA320StateEvaluator> Flow(string id, params string[] related) =>
        new() { Id = id, Name = id, RelatedChecklistGroupIds = related };

    private static readonly List<FlowDefinition<FbwA320StateEvaluator>> StandIns = new()
    {
        Flow("ELECTRICAL_POWER_UP", "ELEC_POWER_UP"),   // names an action group of another name
        Flow("BEFORE_START", "BEFORE_START"),           // names the group of its own name
        Flow("SECURE", "SECURING_CL"),                  // names only a read-back (the A380 shape)
    };

    [Theory]
    [InlineData("ELEC_POWER_UP", "ELECTRICAL_POWER_UP")]   // 1. a flow names the group
    [InlineData("SECURING_CL", "SECURE")]                   // 1. ... a read-back too
    [InlineData("BEFORE_START", "BEFORE_START")]            // 1. ... its own group
    [InlineData("BEFORE_START_CL", "BEFORE_START")]         // 2. the read-back of a flow's phase
    [InlineData("SECURING", "SECURE")]                      // 3. a group of the phase a flow's read-back names
    public void A_group_runs_the_flow_that_names_it_then_its_phase_flow(string groupId, string flowId)
        => Assert.Equal(flowId, RelatedFlow.For(StandIns, groupId)?.Id);

    [Fact]
    public void A_group_no_flow_covers_runs_nothing()
        => Assert.Null(RelatedFlow.For(StandIns, "PARKING_CL"));

    [Theory]
    [InlineData("BEFORE_START_CL", "BEFORE_START")]
    [InlineData("TAXI_CL", "TAXI")]
    [InlineData("SECURING", "SECURING")]
    public void The_base_phase_strips_only_a_trailing_CL(string groupId, string expected)
        => Assert.Equal(expected, RelatedFlow.BasePhaseId(groupId));

    /// <summary>A320-family checklist groups and the flow "Run Related Flow" must start from each.</summary>
    public static IEnumerable<object[]> A320FamilyGroups()
    {
        foreach (var aircraft in new[] { "Fenix", "A32NX", "A330" })
        {
            yield return new object[] { aircraft, "ELEC_POWER_UP", "ELECTRICAL_POWER_UP" };
            yield return new object[] { aircraft, "BEFORE_START_CL", "BEFORE_START" };
            yield return new object[] { aircraft, "AFTER_LANDING_CL", "AFTER_LANDING" };
        }
    }

    [Theory]
    [MemberData(nameof(A320FamilyGroups))]
    public void The_A320_family_groups_run_their_flows(string aircraft, string groupId, string flowId)
        => Assert.Equal(flowId, FlowIdFor(aircraft, groupId));

    private static string? FlowIdFor(string aircraft, string groupId) => aircraft switch
    {
        "Fenix" => RelatedFlow.For(FenixFlowDefinitions.Build(), groupId)?.Id,
        "A32NX" => RelatedFlow.For(FbwA320FlowDefinitions.Build(), groupId)?.Id,
        "A330" => RelatedFlow.For(HwA330FlowDefinitions.Build(), groupId)?.Id,
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, null),
    };
}
