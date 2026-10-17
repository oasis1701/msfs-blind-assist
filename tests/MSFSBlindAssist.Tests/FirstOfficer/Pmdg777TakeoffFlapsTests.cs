using System.Linq;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// PMDG 777 takeoff flaps. The 777's per-detent flap-lever events are named by flap DEGREES
/// (<c>EVT_CONTROL_STAND_FLAPS_LEVER_0/_1/_5/_15/_20/_25/_30</c>), not by detent index as on
/// the 737. The Before Taxi flow's plan-gated flap steps used the 737-style indices, so flaps
/// 5 / 15 / 20 — the 777's ordinary takeoff settings — named events the 777 does not have and
/// were announced as skipped with the lever untouched, and "Flaps: 25" fired
/// <c>_5</c>, i.e. set flaps 5. The executor's own <c>SetFlapsPosition</c> already used the
/// degree names; the flow now matches it.
/// </summary>
public class Pmdg777TakeoffFlapsTests
{
    [Theory]
    [InlineData("BT_FLAPS_1", 1)]
    [InlineData("BT_FLAPS_5", 5)]
    [InlineData("BT_FLAPS_15", 15)]
    [InlineData("BT_FLAPS_20", 20)]
    [InlineData("BT_FLAPS_25", 25)]
    public void Before_Taxi_flap_step_fires_the_event_named_for_its_own_degrees(string stepId, int degrees)
    {
        var step = PMDG777FlowDefinitions.Build()
            .Single(f => f.Id == "BEFORE_TAXI").Steps.Single(s => s.Id == stepId);

        string expected = "EVT_CONTROL_STAND_FLAPS_LEVER_" + degrees;
        Assert.Equal(expected, step.EventName);
        Assert.True(PMDG777Definition.EventIds.ContainsKey(expected), expected + " is not a 777 event");
    }

    // FCTL_Flaps_Lever detents: 0 UP, 1 = 1, 2 = 5, 3 = 15, 4 = 20, 5 = 25, 6 = 30. The 777's
    // takeoff settings are 5, 15 and 20, so "Flaps: Set for takeoff" must accept lever 4 —
    // it stopped at 3, so a flaps 20 takeoff never ticked the Before Takeoff line.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void Before_Takeoff_flaps_line_accepts_every_takeoff_detent(double lever, bool expected)
    {
        var item = PMDG777ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).Single(i => i.Id == "BTKOF_FLAPS");

        Assert.Equal("FCTL_Flaps_Lever", item.StateFieldName);
        Assert.Equal(expected, item.StateCondition!(lever));
    }
}
