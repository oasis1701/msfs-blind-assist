using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A hand-tick whose action the First Officer cannot perform (the executor unavailable: the sim not
/// connected, the iFly plugin not ready, the MD-11's calc path unverified) is refused aloud, like the
/// flow's own "Sim not connected — cannot perform: …". It once ticked the line, ran nothing and said
/// nothing; over a state not read yet the group then latched "Complete" over switches that never moved
/// (PR 160 second review).
/// </summary>
public class FoUnreachableTickTests
{
    private sealed class FakeExec : IFoActionExecutor
    {
        public bool Available { get; set; }
        public bool IsAvailable => Available;
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

    private static ChecklistItem<FakeExec, FakeState> BeaconItem(List<string> ran) => new()
    {
        Id = "BS_BEACON", GroupId = "BEFORE_START", Label = "Beacon: ON",
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = "BEACON",
        StateCondition = v => v > 0.5,
        RevertBehavior = RevertBehavior.RevertToState,
        CheckAction = (_, _) => { ran.Add("BS_BEACON"); return Task.CompletedTask; },
    };

    private static (ChecklistManager<FakeExec, FakeState> mgr, FakeExec exec, FakeState state,
        ChecklistGroup<FakeExec, FakeState> group) Build(ChecklistItem<FakeExec, FakeState> item)
    {
        var exec = new FakeExec();
        var state = new FakeState();
        var group = new ChecklistGroup<FakeExec, FakeState>
        {
            Id = item.GroupId, Name = item.GroupId, Items = new() { item },
        };
        return (new ChecklistManager<FakeExec, FakeState>(state, exec, new() { group }), exec, state, group);
    }

    [Fact]
    public void ATickWhoseActionCannotReachTheAircraft_IsRefusedAloud_AndRunsNothing()
    {
        var ran = new List<string>();
        var item = BeaconItem(ran);
        var (mgr, _, _, group) = Build(item);   // executor unavailable, state unread

        bool? result = mgr.ToggleItem("BEFORE_START", "BS_BEACON", out string? said);

        Assert.False(result);
        Assert.False(item.IsChecked);
        Assert.Equal("Sim not connected — cannot perform: Beacon: ON", said);
        Assert.Empty(ran);
        Assert.False(group.HasParticipation);   // nothing for the group latch to build on
    }

    // The aircraft already shows the line done: nothing is owed, so the tick stands (FO-12).
    [Fact]
    public void ATickTheAircraftAlreadyShowsDone_StillTicks_WhenTheExecutorIsUnavailable()
    {
        var ran = new List<string>();
        var item = BeaconItem(ran);
        var (mgr, _, state, _) = Build(item);
        state.Values["BEACON"] = 1;

        Assert.True(mgr.ToggleItem("BEFORE_START", "BS_BEACON", out string? said));
        Assert.Null(said);
        Assert.Empty(ran);
    }

    // A line with no action (a Captain reminder, a read-back) is the pilot's own mark: never refused.
    [Fact]
    public void ALineWithNoAction_Ticks_WhenTheExecutorIsUnavailable()
    {
        var item = new ChecklistItem<FakeExec, FakeState>
        {
            Id = "BS_CLEARANCE", GroupId = "BEFORE_START", Label = "Start clearance: obtained",
            Type = ChecklistItemType.Actionable,
            ManualCompletionAllowed = true,
            RevertBehavior = RevertBehavior.StayComplete,
        };
        var (mgr, _, _, _) = Build(item);

        Assert.True(mgr.ToggleItem("BEFORE_START", "BS_CLEARANCE", out string? said));
        Assert.Null(said);
    }

    // Available again, the same tick runs its action as before.
    [Fact]
    public void ATickWithTheExecutorAvailable_RunsItsAction()
    {
        var ran = new List<string>();
        var (mgr, exec, _, _) = Build(BeaconItem(ran));
        exec.Available = true;

        Assert.True(mgr.ToggleItem("BEFORE_START", "BS_BEACON", out _));
        Assert.Equal(new[] { "BS_BEACON" }, ran);
    }
}
