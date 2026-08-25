using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Ticking a checklist item with a linked action fires that action. When the switch does
/// not move, EvaluateAutoDetection correctly un-ticks the item — but that correction was
/// silent and visual-only, in a tree a blind pilot has probably navigated away from. These
/// pin the ItemActionFailed event that gives the correction a voice, and pin that an
/// ORDINARY revert (the pilot moved the switch back) stays silent.
/// </summary>
public class FoFailedTickAnnouncementTests
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

    private static ChecklistItem<FakeExec, FakeState> ActionItem(string id, string groupId, string field)
        => new()
        {
            Id = id, GroupId = groupId, Label = id,
            Type = ChecklistItemType.AutoDetectable,
            AutoCompleteAllowed = true,
            ManualCompletionAllowed = true,
            StateFieldName = field,
            StateCondition = v => v > 0.5,
            RevertBehavior = RevertBehavior.RevertToState,
            CheckAction = (_, _) => Task.CompletedTask,
        };

    private static ChecklistItem<FakeExec, FakeState> PlainAutoItem(string id, string groupId, string field)
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
        ChecklistGroup<FakeExec, FakeState> group, List<string> failures)
        Build(params ChecklistItem<FakeExec, FakeState>[] items)
    {
        var state = new FakeState();
        var group = new ChecklistGroup<FakeExec, FakeState>
        {
            Id = items[0].GroupId, Name = items[0].GroupId,
            Items = items.ToList(),
        };
        var mgr = new ChecklistManager<FakeExec, FakeState>(state, new FakeExec(), new() { group });
        var failures = new List<string>();
        mgr.ItemActionFailed += (_, item) => failures.Add(item.Id);
        return (mgr, state, group, failures);
    }

    // ChecklistManager.WithinManualTickGrace also honors a second clock — ActionGraceUtc,
    // stamped by RunCheckActionWithGraceAsync after the action's dispatch queue drains.
    // FakeExec's tasks are already-completed, so that whole async method runs to
    // completion synchronously inside ToggleItem, stamping ActionGraceUtc to the real
    // current instant. Aging only the public LastManualCheckUtc therefore isn't enough to
    // step past the grace window for an item with a CheckAction — clear ActionGraceUtc too,
    // via the item's own ClearActionGrace() (the symmetric partner of StampActionGraceUtc).
    private static void AgeTheTick(ChecklistItem<FakeExec, FakeState> item)
    {
        item.LastManualCheckUtc = DateTime.UtcNow - TimeSpan.FromSeconds(11);
        item.ClearActionGrace();
    }

    [Fact]
    public void TickWhoseActionNeverTakes_RaisesItemActionFailed()
    {
        var (mgr, state, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));
        state.Values["F1"] = 0;               // the switch never moves

        mgr.ToggleItem("G", "SPDBRK");
        AgeTheTick(group.Items[0]);
        mgr.EvaluateAutoDetection();          // state never agreed

        Assert.False(group.Items[0].IsChecked);
        Assert.Equal(new[] { "SPDBRK" }, failures);
    }

    [Fact]
    public void TickWhoseActionTakes_IsSilent()
    {
        var (mgr, state, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));

        mgr.ToggleItem("G", "SPDBRK");
        state.Values["F1"] = 1;               // the switch moved
        mgr.EvaluateAutoDetection();

        Assert.True(group.Items[0].IsChecked);
        Assert.Empty(failures);
    }

    // The switch moved, then the pilot moved it back later. That is an ordinary revert,
    // not a failed action, and speaking it would make the feature chatter.
    [Fact]
    public void RevertAfterTheStateOnceAgreed_IsSilent()
    {
        var (mgr, state, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));

        mgr.ToggleItem("G", "SPDBRK");
        state.Values["F1"] = 1;
        mgr.EvaluateAutoDetection();          // confirmed — clears the mark
        state.Values["F1"] = 0;               // pilot moves it back
        AgeTheTick(group.Items[0]);
        mgr.EvaluateAutoDetection();

        Assert.False(group.Items[0].IsChecked);
        Assert.Empty(failures);
    }

    // An item with no linked action was never a promise the app made, so its revert is
    // ordinary too.
    [Fact]
    public void ItemWithNoCheckAction_NeverRaisesTheEvent()
    {
        var (mgr, state, group, failures) = Build(PlainAutoItem("PLAIN", "G", "F1"));
        state.Values["F1"] = 0;               // the switch never moves

        mgr.ToggleItem("G", "PLAIN");
        AgeTheTick(group.Items[0]);
        mgr.EvaluateAutoDetection();

        Assert.False(group.Items[0].IsChecked);
        Assert.Empty(failures);
    }

    // Fires once per failed tick, not once per polling pass.
    [Fact]
    public void TheEventFiresOnce_NotOnEveryPoll()
    {
        var (mgr, state, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));
        state.Values["F1"] = 0;               // the switch never moves

        mgr.ToggleItem("G", "SPDBRK");
        AgeTheTick(group.Items[0]);
        mgr.EvaluateAutoDetection();
        mgr.EvaluateAutoDetection();
        mgr.EvaluateAutoDetection();

        Assert.Equal(new[] { "SPDBRK" }, failures);
    }

    // Unticking is the pilot withdrawing the request — nothing is owed to them afterwards.
    [Fact]
    public void ManualUntick_ClearsTheMark()
    {
        var (mgr, state, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));
        state.Values["F1"] = 0;               // the switch never moves

        mgr.ToggleItem("G", "SPDBRK");
        mgr.ToggleItem("G", "SPDBRK");        // untick
        Assert.False(group.Items[0].AwaitingActionConfirmation);

        mgr.ToggleItem("G", "SPDBRK");        // tick again, then let it fail
        AgeTheTick(group.Items[0]);
        mgr.EvaluateAutoDetection();
        Assert.Equal(new[] { "SPDBRK" }, failures);
    }

    [Fact]
    public void ResetGroup_ClearsTheMark()
    {
        var (mgr, _, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));

        mgr.ToggleItem("G", "SPDBRK");
        mgr.ResetGroup("G");
        Assert.False(group.Items[0].AwaitingActionConfirmation);

        AgeTheTick(group.Items[0]);
        mgr.EvaluateAutoDetection();
        Assert.Empty(failures);
    }
}
