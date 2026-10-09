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
/// checklist group starts, resolved by <see cref="RelatedFlow.For{TState}"/> in three steps, tried in
/// this order. Step 1: a flow whose related groups include the group itself. Step 2: the flow named
/// after the group's phase (a "_CL" read-back shares its action group's phase). Step 3: a flow whose
/// related groups include a group of that phase (the A380's flows name only their read-backs).
/// <para>
/// Step 3 also matches a flow that step 1 or 2 finds whenever that flow lists a group of the same
/// phase, so a row tells the steps apart only when a stand-in is set up to separate them: a flow that
/// names no group (step 3 cannot match it), an earlier flow that step 3 would return instead, or two
/// different flows that steps 1 and 2 find. Each row's comment says which step it pins, or which step
/// finds it when another would too. The rows that pin a step are DESCENT_CL (step 1 before step 2),
/// ENGINE_START and ENGINE_START_CL (step 2), DESCENT (step 2 before step 3) and SECURING (step 3).
/// Deleting a step, or swapping neighbouring steps, fails at least one of them.
/// </para>
/// </summary>
public class RelatedFlowTests
{
    private static FlowDefinition<FbwA320StateEvaluator> Flow(string id, params string[] related) =>
        new() { Id = id, Name = id, RelatedChecklistGroupIds = related };

    // The last three flows separate the steps, and their order is part of the setup. ENGINE_START names
    // no group, so only step 2 reaches it. APPROACH_BRIEF names DESCENT's read-back and is listed BEFORE
    // DESCENT: for the group DESCENT, step 3 would return APPROACH_BRIEF and only step 2 returns DESCENT;
    // for the group DESCENT_CL, step 1 returns APPROACH_BRIEF and step 2 would return DESCENT. There is
    // no PARKING flow on purpose: A_group_no_flow_covers_runs_nothing relies on PARKING_CL finding
    // nothing here.
    private static readonly List<FlowDefinition<FbwA320StateEvaluator>> StandIns = new()
    {
        Flow("ELECTRICAL_POWER_UP", "ELEC_POWER_UP"),   // names an action group of another name
        Flow("BEFORE_START", "BEFORE_START"),           // names the group of its own name
        Flow("SECURE", "SECURING_CL"),                  // names only a read-back (the A380 shape)
        Flow("ENGINE_START"),                           // names no group (the A380's ENGINE_START and AFTER_TAKEOFF)
        Flow("APPROACH_BRIEF", "DESCENT_CL"),           // names DESCENT's read-back, listed before DESCENT
        Flow("DESCENT"),                                // named after its phase, names no group
    };

    [Theory]
    [InlineData("ELEC_POWER_UP", "ELECTRICAL_POWER_UP")]   // step 1: a flow names the group (step 3 finds it too)
    [InlineData("SECURING_CL", "SECURE")]                   // step 1: ... a read-back too (step 3 too)
    [InlineData("BEFORE_START", "BEFORE_START")]            // step 1: ... its own group (steps 2 and 3 too)
    [InlineData("DESCENT_CL", "APPROACH_BRIEF")]            // pins step 1 before 2: step 2 would return DESCENT
    [InlineData("BEFORE_START_CL", "BEFORE_START")]         // step 2: the read-back of a flow's phase (step 3 finds it too)
    [InlineData("ENGINE_START", "ENGINE_START")]            // pins step 2: the flow names no group, so steps 1 and 3 miss it
    [InlineData("ENGINE_START_CL", "ENGINE_START")]         // pins step 2: ... from its read-back too
    [InlineData("DESCENT", "DESCENT")]                      // pins step 2 before 3: step 3 would return APPROACH_BRIEF
    [InlineData("SECURING", "SECURE")]                      // pins step 3: a group of the phase a flow's read-back names
    public void A_group_runs_the_flow_that_names_it_then_its_phase_flow(string groupId, string flowId)
        => Assert.Equal(flowId, RelatedFlow.For(StandIns, groupId)?.Id);

    [Fact]
    public void A_group_no_flow_covers_runs_nothing()
        => Assert.Null(RelatedFlow.For(StandIns, "PARKING_CL"));

    [Theory]
    [InlineData("BEFORE_START_CL", "BEFORE_START")]
    [InlineData("TAXI_CL", "TAXI")]
    [InlineData("SECURING", "SECURING")]
    [InlineData("PRE_CL_CHECK", "PRE_CL_CHECK")]   // a _CL that does not end the id stays
    [InlineData("taxi_cl", "taxi_cl")]             // the strip is ordinal and case-sensitive: a lower-case _cl stays
    public void The_base_phase_strips_only_a_trailing_CL(string groupId, string expected)
        => Assert.Equal(expected, RelatedFlow.BasePhaseId(groupId));

    /// <summary>
    /// A320-family checklist groups and the flow "Run Related Flow" must start from each. In every row
    /// step 3 would find the same flow, so these check the answer on the real flows, not one step.
    /// </summary>
    public static IEnumerable<object[]> A320FamilyGroups()
    {
        foreach (var aircraft in new[] { "Fenix", "A32NX", "A330" })
        {
            yield return new object[] { aircraft, "ELEC_POWER_UP", "ELECTRICAL_POWER_UP" };   // step 1: the flow lists the action group
            yield return new object[] { aircraft, "BEFORE_START_CL", "BEFORE_START" };        // step 2: the flow is named after the phase
            yield return new object[] { aircraft, "AFTER_LANDING_CL", "AFTER_LANDING" };      // step 2, likewise
            // Each new flow starts from its action group and from its own checklist (2026-10-09).
            yield return new object[] { aircraft, "TAXI", "TAXI" };                           // step 1: the flow lists the action group
            yield return new object[] { aircraft, "TAXI_CL", "TAXI" };                        // step 2: the flow is named after the phase
            yield return new object[] { aircraft, "LINEUP", "LINEUP" };                       // step 1: the flow lists the action group
            yield return new object[] { aircraft, "LINEUP_CL", "LINEUP" };                    // step 2: the flow is named after the phase
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
