using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380 cockpit-preparation items FBW #10855 rewired. The owner flies BOTH simulators with a
/// DIFFERENT FlyByWire build in each (FS2020 fs2020-master, without #10855; FS2024, with it), so the
/// flow and the checklist name build-NEUTRAL keys and the per-build names live in
/// <see cref="A380BuildNames"/> (pinned by <see cref="FbwA380BuildResolutionTests"/>).
/// </summary>
public class FoA380Fbw10855PortTests
{
    private static FlowStep<FbwA380StateEvaluator> Step(string id) =>
        FbwA380FlowDefinitions.Build().SelectMany(f => f.Steps).Single(s => s.Id == id);

    private static ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator> Item(string id) =>
        FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void The_baro_step_writes_the_per_build_hectopascals_key_and_skips_when_already_set()
    {
        var step = Step("CP_BARO");
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal(FbwA380ActionExecutor.BaroUnitHectopascalsKey, step.EventName);
        Assert.Equal(1, step.TargetValue);
        Assert.NotNull(step.SkipCondition);
    }

    [Fact]
    public void The_baro_item_reads_the_per_build_hectopascals_field()
    {
        var item = Item("CP_BARO");
        Assert.Equal(FbwA380StateEvaluator.BaroUnitHectopascalsField, item.StateFieldName);
        Assert.Empty(item.AdditionalStateFields);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.False(item.StateCondition!(double.NaN));
        Assert.NotNull(item.CheckAction);
    }

    [Fact]
    public void The_fd_step_writes_the_per_build_key_and_skips_when_already_on()
    {
        var step = Step("CP_FD");
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal(FbwA380ActionExecutor.FlightDirectorsKey, step.EventName);
        Assert.Equal(1, step.TargetValue);
        Assert.Empty(step.MultiActions);
        Assert.NotNull(step.SkipCondition);
    }

    [Fact]
    public void The_fd_item_reads_the_per_build_fd_field()
    {
        var item = Item("CP_FD");
        Assert.Equal(FbwA380StateEvaluator.FlightDirectorsOnField, item.StateFieldName);
        Assert.Empty(item.AdditionalStateFields);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.NotNull(item.CheckAction);
    }
}
