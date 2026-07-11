using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the ChecklistManager group-completion latch
/// (2026-07-11): once a group reaches 100% complete AND a user manual tick or a
/// flow MarkComplete touched it, RevertToState un-ticking is suppressed for that
/// group until it is reset or an item is manually unticked. Coincidental
/// auto-ticks alone must never latch (the group-shaped StayComplete failure).
/// </summary>
public class FoChecklistLatchTests
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

    private static ChecklistItem<FakeExec, FakeState> ReminderItem(string id, string groupId)
        => new()
        {
            Id = id, GroupId = groupId, Label = id,
            Type = ChecklistItemType.CaptainReminder,
            ManualCompletionAllowed = true,
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

    [Fact]
    public void CoincidentalAutoCompletion_WithoutParticipation_StillReverts()
    {
        var (mgr, state, group) = Build(
            AutoItem("A1", "G", "F1"), AutoItem("A2", "G", "F2"));

        state.Values["F1"] = 1; state.Values["F2"] = 1;
        mgr.EvaluateAutoDetection();
        Assert.Equal(ChecklistGroupStatus.Complete, group.Status);
        Assert.False(group.CompletionLatched); // no manual tick, no MarkComplete

        state.Values["F1"] = 0;
        mgr.EvaluateAutoDetection();
        Assert.False(group.Items[0].IsChecked); // live mirror still reverts
        Assert.Equal(ChecklistGroupStatus.InProgress, group.Status);
    }

    [Fact]
    public void ManualTickParticipation_LatchesAtComplete_SurvivesLaterStateFlip()
    {
        var (mgr, state, group) = Build(
            AutoItem("A1", "G", "F1"), ReminderItem("R1", "G"));

        state.Values["F1"] = 1;
        mgr.EvaluateAutoDetection();          // auto item ticks
        mgr.ToggleItem("G", "R1");            // manual tick → participation
        Assert.True(group.CompletionLatched);

        state.Values["F1"] = 0;               // a later phase moves the switch
        mgr.EvaluateAutoDetection();
        Assert.True(group.Items[0].IsChecked); // latched — no un-tick
        Assert.Equal(ChecklistGroupStatus.Complete, group.Status);
    }

    [Fact]
    public void MarkComplete_CountsAsParticipation()
    {
        var (mgr, state, group) = Build(
            AutoItem("A1", "G", "F1"), AutoItem("A2", "G", "F2"));

        state.Values["F1"] = 1; state.Values["F2"] = 1;
        mgr.MarkComplete("A1");               // flow step ticked it
        mgr.EvaluateAutoDetection();          // second item auto-ticks → complete
        Assert.True(group.CompletionLatched);

        state.Values["F2"] = 0;
        mgr.EvaluateAutoDetection();
        Assert.True(group.Items[1].IsChecked); // latched
    }

    [Fact]
    public void ManualUntick_ClearsLatch_RevertsResume()
    {
        var (mgr, state, group) = Build(
            AutoItem("A1", "G", "F1"), ReminderItem("R1", "G"));

        state.Values["F1"] = 1;
        mgr.EvaluateAutoDetection();
        mgr.ToggleItem("G", "R1");
        Assert.True(group.CompletionLatched);

        mgr.ToggleItem("G", "R1");            // manual UNTICK → unlatch
        Assert.False(group.CompletionLatched);

        state.Values["F1"] = 0;
        mgr.EvaluateAutoDetection();
        Assert.False(group.Items[0].IsChecked); // live mirror resumed
    }

    [Fact]
    public void ResetGroup_ClearsLatchAndParticipation()
    {
        var (mgr, state, group) = Build(
            AutoItem("A1", "G", "F1"), ReminderItem("R1", "G"));

        state.Values["F1"] = 1;
        mgr.EvaluateAutoDetection();
        mgr.ToggleItem("G", "R1");
        Assert.True(group.CompletionLatched);

        mgr.ResetGroup("G");
        Assert.False(group.CompletionLatched);
        Assert.False(group.HasParticipation);

        // Coincidental re-completion after reset must NOT latch.
        mgr.EvaluateAutoDetection();          // F1 still 1 → auto-ticks
        Assert.False(group.CompletionLatched);
    }

    [Fact]
    public void NaNField_RemainsInert_EvenWhenLatchCandidatesExist()
    {
        var (mgr, state, group) = Build(
            AutoItem("A1", "G", "F1"), ReminderItem("R1", "G"));

        // F1 never set → NaN → no tick, no revert, no latch arming off it.
        mgr.EvaluateAutoDetection();
        Assert.False(group.Items[0].IsChecked);
        mgr.ToggleItem("G", "R1");            // participation, but group not complete
        Assert.False(group.CompletionLatched);
    }
}
