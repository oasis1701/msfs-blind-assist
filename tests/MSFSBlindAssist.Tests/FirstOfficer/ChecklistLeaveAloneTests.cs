using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The hand-tick half of the leave-alone rule (ChecklistItem.LeaveAloneWhen): ticking a line
/// whose aircraft state says the First Officer must not act runs nothing, leaves the line
/// unticked and hands back the reason at once — instead of running the action and saying
/// "Unable to complete" ten seconds later with no reason.
/// </summary>
public class ChecklistLeaveAloneTests
{
    private const string Reason = "Speedbrake extended, not armed. Left as it is.";

    private sealed class FakeExec : IFoActionExecutor
    {
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step) => Task.FromResult(true);
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class FakeState : IFoStateEvaluator
    {
        public bool Deployed { get; set; }
        public Dictionary<string, double> Values { get; } = new();
        public bool IsAvailable => true;
        public double GetValue(string field) => Values.TryGetValue(field, out double v) ? v : double.NaN;
        public bool IsOn(string field) => GetValue(field) > 0.5;
        public bool IsPosition(string field, int position) => Math.Abs(GetValue(field) - position) < 0.5;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class Rig
    {
        public int ActionsRun;
        public List<string> Failures { get; } = new();
        public FakeState State { get; } = new();
        public ChecklistGroup<FakeExec, FakeState> Group { get; }
        public ChecklistManager<FakeExec, FakeState> Mgr { get; }
        public ChecklistItem<FakeExec, FakeState> Item => Group.Items[0];

        public Rig(string? text = Reason)
        {
            var item = new ChecklistItem<FakeExec, FakeState>
            {
                Id = "SPDBRK", GroupId = "G", Label = "Speedbrake: ARMED",
                Type = ChecklistItemType.AutoDetectable,
                AutoCompleteAllowed = true,
                ManualCompletionAllowed = true,
                StateFieldName = "F1",
                StateCondition = v => v > 0.5,
                RevertBehavior = RevertBehavior.RevertToState,
                CheckAction = (_, _) => { ActionsRun++; return Task.CompletedTask; },
                LeaveAloneWhen = s => s.Deployed,
                LeaveAloneText = text,
            };
            Group = new ChecklistGroup<FakeExec, FakeState> { Id = "G", Name = "G", Items = new() { item } };
            Mgr = new ChecklistManager<FakeExec, FakeState>(State, new FakeExec(), new() { Group });
            Mgr.ItemActionFailed += (_, i) => Failures.Add(i.Id);
            State.Values["F1"] = 0;
        }
    }

    [Fact]
    public void A_tick_on_a_line_whose_state_says_leave_it_alone_runs_nothing_and_says_why()
    {
        var r = new Rig();
        r.State.Deployed = true;

        bool? result = r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.False(result);
        Assert.False(r.Item.IsChecked);
        Assert.Equal(0, r.ActionsRun);
        Assert.Equal(Reason, why);
        Assert.False(r.Item.AwaitingActionConfirmation);

        // Nothing is owed, so no "Unable to complete" follows later either.
        r.Item.LastManualCheckUtc = DateTime.UtcNow - TimeSpan.FromSeconds(11);
        r.Mgr.EvaluateAutoDetection();
        Assert.Empty(r.Failures);
    }

    [Fact]
    public void Otherwise_the_tick_runs_its_action_as_before()
    {
        var r = new Rig();
        r.State.Deployed = false;

        bool? result = r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.True(result);
        Assert.True(r.Item.IsChecked);
        Assert.Equal(1, r.ActionsRun);
        Assert.Null(why);
    }

    [Fact]
    public void Unticking_is_never_refused()
    {
        var r = new Rig();
        r.Item.IsChecked = true;
        r.State.Deployed = true;

        bool? result = r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.False(result);
        Assert.False(r.Item.IsChecked);
        Assert.Null(why);
    }

    [Fact]
    public void The_two_argument_overload_refuses_the_same_way()
    {
        var r = new Rig();
        r.State.Deployed = true;

        Assert.False(r.Mgr.ToggleItem("G", "SPDBRK"));
        Assert.False(r.Item.IsChecked);
        Assert.Equal(0, r.ActionsRun);
    }

    [Fact]
    public void Without_its_own_text_it_says_skipping_the_label()
    {
        var r = new Rig(text: null);
        r.State.Deployed = true;

        r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.Equal("Skipping: Speedbrake: ARMED", why);
    }
}
