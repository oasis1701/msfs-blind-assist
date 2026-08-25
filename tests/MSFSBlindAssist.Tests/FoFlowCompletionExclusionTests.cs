using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A finished flow marks its related checklist groups complete — which ticks every item
/// and latches the group against RevertToState. That is right for the steps the flow
/// actually performed, and wrong for a step it announced as skipped: the pilot heard
/// "Skipping: Speedbrake: ARMED" and then, two seconds later, saw the item ticked and
/// frozen for the session. These pin the exclusion that keeps a failed step un-ticked.
/// </summary>
public class FoFlowCompletionExclusionTests
{
    private sealed class FakeExec : IFoActionExecutor
    {
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step) => Task.FromResult(true);
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class FakeState : IFoStateEvaluator
    {
        public Dictionary<string, double> Values { get; } = new();
        public bool IsAvailable => true;
        public double GetValue(string field)
            => Values.TryGetValue(field, out double v) ? v : double.NaN;
        public bool IsOn(string field) => GetValue(field) > 0.5;
        public bool IsPosition(string field, int position)
            => Math.Abs(GetValue(field) - position) < 0.5;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private static ChecklistItem<FakeExec, FakeState> AutoItem(string id, string groupId, string field)
        => new()
        {
            Id = id, GroupId = groupId, Label = id,
            Type = ChecklistItemType.AutoDetectable,
            AutoCompleteAllowed = true,
            ManualCompletionAllowed = true,
            StateFieldName = field,
            StateCondition = v => v > 0.5,
            RevertBehavior = RevertBehavior.RevertToState,
        };

    private static (ChecklistManager<FakeExec, FakeState> mgr, FakeState state,
        ChecklistGroup<FakeExec, FakeState> group) Build(params ChecklistItem<FakeExec, FakeState>[] items)
    {
        var state = new FakeState();
        var group = new ChecklistGroup<FakeExec, FakeState>
        {
            Id = items[0].GroupId, Name = items[0].GroupId,
            Items = items.ToList(),
        };
        var mgr = new ChecklistManager<FakeExec, FakeState>(state, new FakeExec(), new() { group });
        return (mgr, state, group);
    }

    // The whole point: the failed item stays un-ticked while its siblings complete.
    [Fact]
    public void ExcludedItem_IsNotTicked_ButItsSiblingsAre()
    {
        var (mgr, _, group) = Build(
            AutoItem("GOOD", "G", "F1"), AutoItem("FAILED", "G", "F2"));

        mgr.MarkGroupComplete("G", new[] { "FAILED" });

        Assert.True(group.Items.Single(i => i.Id == "GOOD").IsChecked);
        Assert.False(group.Items.Single(i => i.Id == "FAILED").IsChecked);
    }

    // Latching an un-ticked failed item would freeze the group and permanently disable
    // the live-state mirror that is the pilot's only way to learn the truth later.
    [Fact]
    public void ExcludedItemLeftUnticked_DoesNotLatchTheGroup()
    {
        var (mgr, _, group) = Build(
            AutoItem("GOOD", "G", "F1"), AutoItem("FAILED", "G", "F2"));

        mgr.MarkGroupComplete("G", new[] { "FAILED" });

        Assert.False(group.CompletionLatched);
    }

    // A group left unlatched must still be able to correct itself upward when the pilot
    // sets the switch by hand.
    [Fact]
    public void AfterExclusion_TheFailedItemStillAutoTicksWhenTheSwitchIsSet()
    {
        var (mgr, state, group) = Build(
            AutoItem("GOOD", "G", "F1"), AutoItem("FAILED", "G", "F2"));

        mgr.MarkGroupComplete("G", new[] { "FAILED" });
        state.Values["F2"] = 1;
        mgr.EvaluateAutoDetection();

        Assert.True(group.Items.Single(i => i.Id == "FAILED").IsChecked);
    }

    // An excluded item that is ALREADY true is not a failure — the state agrees, so the
    // phase really is complete and the historical-record latch is still correct.
    [Fact]
    public void ExcludedItemAlreadyTrue_StillLatches()
    {
        var (mgr, state, group) = Build(
            AutoItem("GOOD", "G", "F1"), AutoItem("FAILED", "G", "F2"));

        state.Values["F2"] = 1;
        mgr.EvaluateAutoDetection();          // FAILED auto-ticks from real state
        mgr.MarkGroupComplete("G", new[] { "FAILED" });

        Assert.True(group.CompletionLatched);
    }

    // The overwhelmingly common case — every step succeeded — must be byte-identical to
    // the old behaviour, on both the no-argument and empty-exclusion call shapes.
    [Fact]
    public void NoExclusion_TicksEverythingAndLatches_AsBefore()
    {
        var (mgr, _, group) = Build(
            AutoItem("A", "G", "F1"), AutoItem("B", "G", "F2"));

        mgr.MarkGroupComplete("G");

        Assert.All(group.Items, i => Assert.True(i.IsChecked));
        Assert.True(group.CompletionLatched);
    }

    [Fact]
    public void EmptyExclusion_BehavesLikeNoExclusion()
    {
        var (mgr, _, group) = Build(
            AutoItem("A", "G", "F1"), AutoItem("B", "G", "F2"));

        mgr.MarkGroupComplete("G", Array.Empty<string>());

        Assert.All(group.Items, i => Assert.True(i.IsChecked));
        Assert.True(group.CompletionLatched);
    }

    // An id that belongs to another group (or to nothing) must not disturb this one.
    [Fact]
    public void ExclusionNamingAnUnknownItem_IsIgnored()
    {
        var (mgr, _, group) = Build(
            AutoItem("A", "G", "F1"), AutoItem("B", "G", "F2"));

        mgr.MarkGroupComplete("G", new[] { "NOT_IN_THIS_GROUP" });

        Assert.All(group.Items, i => Assert.True(i.IsChecked));
        Assert.True(group.CompletionLatched);
    }
}
