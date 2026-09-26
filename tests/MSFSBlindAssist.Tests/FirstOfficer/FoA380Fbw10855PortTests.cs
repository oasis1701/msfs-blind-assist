using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380 cockpit-preparation items FBW #10855 rewired, ported when #251 reached this branch.
///
/// BARO: the hPa/inHg selector is the EFIS-CP's own <c>A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG</c>, whose
/// polarity is the OPPOSITE of the dead <c>XMLVAR_Baro_Selector_HPA_{1,2}</c> it replaces — 1 is inHg,
/// so hectopascals is 0. A port that kept the old 1 would set every pilot to inches.
///
/// FLIGHT DIRECTORS: ONE FCU pushbutton, read from its light and pressed only when the pick differs
/// (<see cref="A380FlightDirector"/>). The per-side FD_1_CTL/FD_2_CTL keys are gone from the
/// definition; writing them would have created bogus L:vars and reported success with the flight
/// directors still off (<see cref="FbwA380FoKeyRegistrationTests"/> guards the whole class).
/// </summary>
public class FoA380Fbw10855PortTests
{
    private static FlowStep<FbwA380StateEvaluator> Step(string id) =>
        FbwA380FlowDefinitions.Build().SelectMany(f => f.Steps).Single(s => s.Id == id);

    private static ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator> Item(string id) =>
        FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void The_baro_step_selects_hectopascals_as_zero_on_both_sides()
    {
        Assert.Equal(
            new (string, int?)[] { ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0), ("A32NX_FCU_EFIS_R_BARO_IS_INHG", 0) },
            Step("CP_BARO").MultiActions.ToArray());
    }

    [Fact]
    public void The_baro_item_reads_hectopascals_as_zero_on_both_sides()
    {
        var item = Item("CP_BARO");

        Assert.Equal("A32NX_FCU_EFIS_L_BARO_IS_INHG", item.StateFieldName);
        Assert.Equal(new[] { "A32NX_FCU_EFIS_R_BARO_IS_INHG" }, item.AdditionalStateFields.ToArray());
        Assert.True(item.StateCondition!(0));        // hectopascals
        Assert.False(item.StateCondition!(1));       // inches
        Assert.True(item.AdditionalStateCondition!(0));
        Assert.False(item.AdditionalStateCondition!(1));
        Assert.False(item.StateCondition!(double.NaN));
    }

    [Fact]
    public void The_fd_step_sets_the_one_fcu_pushbutton_and_skips_when_already_lit()
    {
        var step = Step("CP_FD");

        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal(A380FlightDirector.StateKey, step.EventName);
        Assert.Equal(1, step.TargetValue);
        Assert.Empty(step.MultiActions);
        // A toggle: the skip keeps "Already set" honest, and the definition presses only on a difference.
        Assert.NotNull(step.SkipCondition);
    }

    [Fact]
    public void The_fd_item_reads_the_fd_light()
    {
        var item = Item("CP_FD");

        Assert.Equal(A380FlightDirector.StateKey, item.StateFieldName);
        Assert.Empty(item.AdditionalStateFields);
        Assert.True(item.StateCondition!(1));
        Assert.False(item.StateCondition!(0));
        Assert.NotNull(item.CheckAction);
    }
}
