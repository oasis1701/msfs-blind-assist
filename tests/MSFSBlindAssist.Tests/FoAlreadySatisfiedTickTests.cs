using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A hand-tick on an item whose own state condition ALREADY holds must not fire the item's
/// CheckAction. Reported on the Fenix A320 (2026-09-04): a pilot who had started the APU
/// themselves, outside the First Officer window, ticked "APU: ON and available" and the FO
/// re-ran the whole start sequence on a live APU — re-writing MASTER and pulsing the START
/// pushbutton — then sat on the AVAIL poll before announcing
/// "Unable to complete: APU: ON and available".
///
/// The FLOW engine has always had this guard: FlowManager honours a step's SkipCondition and
/// announces "Already set" instead of re-issuing the write (FlowManager.cs:173-185, and the
/// Fenix APU block's own Skip predicates). The hand-tick path had no equivalent, and that
/// asymmetry IS the bug — the same shape docs/first-officer.md already records for the APU
/// wait ("the FLOW never had this bug").
///
/// The guard is the item's OWN condition, so it can never disagree with what the item claims
/// to be about, and an item reading that condition as true is one EvaluateAutoDetection would
/// tick by itself on its next pass anyway — which is what makes running its action pure
/// side-effect on the aircraft. An INDETERMINATE (NaN) read is not evidence of anything and
/// must still run the action, matching the manager's standing "indeterminate is not a
/// failure" contract.
/// </summary>
public class FoAlreadySatisfiedTickTests
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

    // The CheckAction records every time the pilot's tick actually reached the aircraft.
    private static ChecklistItem<FakeExec, FakeState> ApuItem(List<string> ran,
        string[]? additionalFields = null)
        => new()
        {
            Id = "BS_APU", GroupId = "BEFORE_START", Label = "APU: ON and available",
            Type = ChecklistItemType.AutoDetectable,
            AutoCompleteAllowed = true,
            ManualCompletionAllowed = true,
            StateFieldName = "APU_AVAIL",
            StateCondition = v => v > 0.5,
            AdditionalStateFields = additionalFields ?? Array.Empty<string>(),
            RevertBehavior = RevertBehavior.RevertToState,
            CheckAction = (_, _) => { ran.Add("BS_APU"); return Task.CompletedTask; },
        };

    private static (ChecklistManager<FakeExec, FakeState> mgr, FakeState state,
        ChecklistGroup<FakeExec, FakeState> group, List<string> failures)
        Build(params ChecklistItem<FakeExec, FakeState>[] items)
    {
        var state = new FakeState();
        var group = new ChecklistGroup<FakeExec, FakeState>
        {
            Id = items[0].GroupId, Name = items[0].GroupId, Items = items.ToList(),
        };
        var mgr = new ChecklistManager<FakeExec, FakeState>(state, new FakeExec(), new() { group });
        var failures = new List<string>();
        mgr.ItemActionFailed += (_, item) => failures.Add(item.Id);
        return (mgr, state, group, failures);
    }

    /// <summary>The reported bug, minimised: the APU is already available, so ticking the item
    /// must not re-run the start sequence on it.</summary>
    [Fact]
    public void TickingAnItemWhoseStateAlreadyHolds_DoesNotRunItsAction()
    {
        var ran = new List<string>();
        var (mgr, state, _, _) = Build(ApuItem(ran));
        state.Values["APU_AVAIL"] = 1;   // the pilot started the APU themselves

        Assert.True(mgr.ToggleItem("BEFORE_START", "BS_APU"));

        Assert.Empty(ran);
    }

    /// <summary>...and it stays ticked, silently. Nothing is owed to the pilot, so no
    /// "Unable to complete" may follow, even long after the grace window expires.</summary>
    [Fact]
    public void TickingAnItemWhoseStateAlreadyHolds_StaysTickedAndSilent()
    {
        var ran = new List<string>();
        var item = ApuItem(ran);
        var (mgr, state, _, failures) = Build(item);
        state.Values["APU_AVAIL"] = 1;

        mgr.ToggleItem("BEFORE_START", "BS_APU");
        item.LastManualCheckUtc = DateTime.UtcNow - TimeSpan.FromSeconds(11);
        item.ClearActionGrace();
        mgr.EvaluateAutoDetection();

        Assert.True(item.IsChecked);
        Assert.Empty(failures);
        Assert.False(item.AwaitingActionConfirmation);
    }

    /// <summary>State NOT satisfied — the ordinary case — is untouched: the action runs, and
    /// the pilot is still owed the failure announcement if the switch never moves.</summary>
    [Fact]
    public void TickingAnItemWhoseStateDoesNotHold_StillRunsItsActionAndCanStillFail()
    {
        var ran = new List<string>();
        var item = ApuItem(ran);
        var (mgr, state, _, failures) = Build(item);
        state.Values["APU_AVAIL"] = 0;

        mgr.ToggleItem("BEFORE_START", "BS_APU");
        Assert.Single(ran);

        item.LastManualCheckUtc = DateTime.UtcNow - TimeSpan.FromSeconds(11);
        item.ClearActionGrace();
        mgr.EvaluateAutoDetection();

        Assert.False(item.IsChecked);
        Assert.Equal(new[] { "BS_APU" }, failures);
    }

    /// <summary>An unreadable field is not evidence the work is done — indeterminate must still
    /// run the action, the same contract EvaluateAutoDetection applies to NaN.</summary>
    [Fact]
    public void TickingAnItemWhoseStateIsIndeterminate_StillRunsItsAction()
    {
        var ran = new List<string>();
        var (mgr, _, _, _) = Build(ApuItem(ran));
        // "APU_AVAIL" is never set, so FakeState returns NaN for it.

        mgr.ToggleItem("BEFORE_START", "BS_APU");

        Assert.Single(ran);
    }

    /// <summary>The primary field agreeing is not enough when the item carries additional
    /// fields: the whole condition the item is judged by must hold before its action is
    /// skipped, or a partly-set group is ticked with work still outstanding.</summary>
    [Fact]
    public void TickingAnItemWhoseAdditionalFieldDoesNotHold_StillRunsItsAction()
    {
        var ran = new List<string>();
        var (mgr, state, _, _) = Build(ApuItem(ran, new[] { "APU_BLEED" }));
        state.Values["APU_AVAIL"] = 1;
        state.Values["APU_BLEED"] = 0;

        mgr.ToggleItem("BEFORE_START", "BS_APU");

        Assert.Single(ran);
    }

    /// <summary>The boundary of the guard. An ACTIONABLE item — the preflight TCAS / WXR / GPWS
    /// self-tests, "press this and listen" — has no state field to be judged by, so it is not
    /// auto-detectable and its action must fire on every tick. Those tests exist precisely to be
    /// re-run, and a state check can never authorise skipping one.</summary>
    [Fact]
    public void TickingAnActionableItemWithNoStateField_AlwaysRunsItsAction()
    {
        var ran = new List<string>();
        var item = new ChecklistItem<FakeExec, FakeState>
        {
            Id = "PF_TCAS_TEST", GroupId = "BEFORE_START", Label = "TCAS: TEST",
            Type = ChecklistItemType.Actionable,
            AutoCompleteAllowed = false,
            ManualCompletionAllowed = true,
            RevertBehavior = RevertBehavior.StayComplete,
            CheckAction = (_, _) => { ran.Add("PF_TCAS_TEST"); return Task.CompletedTask; },
        };
        var (mgr, _, _, _) = Build(item);

        mgr.ToggleItem("BEFORE_START", "PF_TCAS_TEST");

        Assert.Single(ran);
    }

    /// <summary>A tick still counts as the pilot working this group, whether or not the action
    /// was needed — the completion latch must not lose its participation mark.</summary>
    [Fact]
    public void TickingAnAlreadySatisfiedItem_StillRecordsGroupParticipation()
    {
        var ran = new List<string>();
        var (mgr, state, group, _) = Build(ApuItem(ran));
        state.Values["APU_AVAIL"] = 1;

        mgr.ToggleItem("BEFORE_START", "BS_APU");

        Assert.True(group.HasParticipation);
    }
}
