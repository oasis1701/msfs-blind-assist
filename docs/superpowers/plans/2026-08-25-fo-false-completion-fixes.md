# First Officer false-completion fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the First Officer checklist claiming an item is done when the switch never moved — on the flow path (where completion force-ticks and latches everything) and on the manual-tick path (where a failed action reverts silently).

**Architecture:** Both faults live in shared machinery used by all six aircraft profiles: `FlowManager`, `ChecklistManager` and `FirstOfficerForm`. `FlowManager` learns which checklist items its run could not deliver; `MarkGroupComplete` is told to leave those alone and not to latch over them. Separately, an item manually ticked with a linked action is marked as awaiting confirmation, and a revert while that mark stands raises a new event the form speaks.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit. No new dependencies.

## Background — why this matters

`FirstOfficerForm.OnFlowCompleted` calls `ChecklistManager.MarkGroupComplete` for every group related to the finished flow. That method ticks **every** non-`Informational` item unconditionally and sets `group.CompletionLatched = true`. `CompletionLatched` is the exact flag `EvaluateAutoDetection`'s revert branch tests, so from that moment the group can never correct itself.

So a flow step that failed — announced honestly as *"Skipping: Speedbrake: ARMED"* — is force-ticked about two seconds later and frozen that way for the session. A blind pilot who checks the Landing checklist on final is told the speedbrake is armed when it is not.

This is already documented as a known hazard elsewhere in the codebase: `IFly737ActionExecutor.IsAvailable`'s XML doc describes exactly this chain ("…FlowCompleted fires, and MarkGroupComplete ticks every item and sets the completion latch — which permanently disables RevertToState for the group. A blind pilot is then shown 'Complete' for switches that never moved") and works around it by gating `IsAvailable`. This plan fixes it at the source; that workaround stays (it solves a different problem — a dead plugin reporting success).

The manual-tick path fails the opposite way. `ToggleItem` fires the action, `RunCheckActionWithGraceAsync` holds off revert until it drains, and `EvaluateAutoDetection` then un-ticks the item when the state does not agree. That is correct — but `OnChecklistItemChanged` only calls `RefreshTreeNodeForItem`, so the correction is silent and visual-only, in a tree the pilot has probably navigated away from.

## Global Constraints

- Build the SOLUTION, never the bare csproj: `dotnet build MSFSBlindAssist.sln -c Debug`. A bare `dotnet build` on `MSFSBlindAssist\MSFSBlindAssist.csproj` silently defaults to `Platform=AnyCPU` and writes to a different folder than the x64 run path.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
- Suite baseline at the start of this plan: **3840 passed, 0 failed.**
- Known pre-existing warnings: TaxiGraph.cs CS8601 ×2, GsxServiceAnnouncerDiagnosticsTests xUnit2029 ×3, GsxServiceStateTests xUnit2029 ×1. Anything beyond those six is a finding.
- The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building.
- **Screen-reader rule:** never announce a direct UI interaction. Only numeric confirmations, **error conditions**, and background state changes may be announced. The new announcement in Task 2 is an error condition and is therefore permitted — but it must be **queued** (`Announce`), never `AnnounceImmediate`, so it cannot interrupt a landing callout.
- **This is shared machinery.** Six aircraft profiles (PMDG 777, PMDG 737, Fenix A320, FBW A380, FBW A32NX, iFly 737 MAX8) run through these classes. No change may alter behaviour for a flow whose every step succeeded — that is the overwhelmingly common case and it must stay byte-identical.
- Branch `feature/first-officer`, PR #160. Never commit to `main`. Do NOT push.
- Every commit message ends with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- Changelog fragments are `changelog.d/160-<slug>.<category>.md`, pilot-facing prose, no heading.

---

## File Structure

| File | Responsibility | Task |
|------|----------------|------|
| `MSFSBlindAssist/FirstOfficer/FlowManager.cs` | records which checklist items the run could not deliver | 1 |
| `MSFSBlindAssist/FirstOfficer/ChecklistManager.cs` | `MarkGroupComplete` honours the exclusion; awaiting-confirmation tracking + new event | 1, 2 |
| `MSFSBlindAssist/FirstOfficer/Models/ChecklistItem.cs` | `AwaitingActionConfirmation` flag | 2 |
| `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs` | passes the exclusion; speaks the failed-action event | 1, 2 |
| `tests/MSFSBlindAssist.Tests/FoFlowCompletionExclusionTests.cs` | **new** — Task 1 coverage | 1 |
| `tests/MSFSBlindAssist.Tests/FoFailedTickAnnouncementTests.cs` | **new** — Task 2 coverage | 2 |
| `changelog.d/160-*.md`, `docs/superpowers/specs/2026-08-25-pr160-fo-procedure-fixes-design.md` | release notes + retire the known-limitation note | 3 |

Both test files use the `FakeExec` / `FakeState` harness already established in `tests/MSFSBlindAssist.Tests/FoChecklistLatchTests.cs` — copy that pattern (the two interfaces are small; `FakeState` exposes a `Values` dictionary). Note `ChecklistItem.LastManualCheckUtc` is a public settable property, which is how those tests step past the 10-second `ManualTickGrace` without sleeping.

---

## Task 1: A flow must not tick what it could not do

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/FoFlowCompletionExclusionTests.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/ChecklistManager.cs` (`MarkGroupComplete`, ~line 92–118)
- Modify: `MSFSBlindAssist/FirstOfficer/FlowManager.cs` (run loop ~line 130–215)
- Modify: `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs` (`OnFlowCompleted`, ~line 1000–1013)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ChecklistManager<TExec,TState>.MarkGroupComplete(string groupId, IReadOnlyCollection<string>? excludeItemIds = null)` — the optional parameter is additive, so the existing single-argument call compiles unchanged.
  - `FlowManager<TState>.UnfinishedChecklistItemIds` → `IReadOnlyCollection<string>`.

**Which steps count as "could not deliver".** Only the `FlowStepFailurePolicy.Skip` branch matters. `Stop` and a fully-failed `RetryThenStop` both raise `FlowFailed` and `return`, so `FlowCompleted` never fires and `MarkGroupComplete` is never called on those runs. The *"Already set"* early-continue is **not** a failure — it raises `StepCompleted` and calls `MarkComplete`, and must keep doing so.

---

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/FoFlowCompletionExclusionTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoFlowCompletionExclusionTests"`

Expected: a build error — `MarkGroupComplete` takes one argument, not two. The three no-exclusion facts would pass once it compiles; the exclusion facts are the red evidence.

- [ ] **Step 3: Give `MarkGroupComplete` its exclusion**

In `MSFSBlindAssist/FirstOfficer/ChecklistManager.cs`, replace the whole `MarkGroupComplete` method (signature, XML doc and body) with:

```csharp
    /// <summary>
    /// Mark an ENTIRE group complete — called when a related flow finishes. Ticks every
    /// tickable (non-Informational) item, records participation, and latches the group so
    /// RevertToState won't un-tick it. Rationale: running a flow IS the First Officer
    /// working that phase, so its checklist should stand complete as the phase's historical
    /// record (same philosophy as the group-completion latch). Without this, a flow that
    /// set all the switches left the checklist header stuck at a partial "N of M" because
    /// the phase's Captain-reminder / reminder items never auto-tick from state.
    ///
    /// <paramref name="excludeItemIds"/> names the items the flow could NOT deliver — the
    /// steps it announced as skipped. Those are neither ticked nor latched over. Without
    /// it, a step that failed and said so out loud ("Skipping: Speedbrake: ARMED") was
    /// force-ticked two seconds later at flow completion AND frozen by the latch, so the
    /// live-state mirror could never correct it: a blind pilot reading the Landing
    /// checklist on final was told the speedbrake was armed when it was not.
    ///
    /// The latch is withheld only while an excluded item is ACTUALLY un-ticked. An excluded
    /// item the state already agrees with is not a failure — the phase genuinely is
    /// complete, and freezing it as a historical record is still right.
    /// </summary>
    public void MarkGroupComplete(string groupId, IReadOnlyCollection<string>? excludeItemIds = null)
    {
        var group = FindGroup(groupId);
        if (group == null) return;

        bool anyExcludedStillUnset = false;
        group.HasParticipation = true;
        foreach (var item in group.Items)
        {
            if (item.Type == ChecklistItemType.Informational) continue;   // separators aren't tickable
            if (excludeItemIds != null && excludeItemIds.Contains(item.Id))
            {
                if (!item.IsChecked) anyExcludedStillUnset = true;
                continue;
            }
            if (!item.IsChecked)
            {
                item.IsChecked = true;
                ItemStateChanged?.Invoke(group, item);
            }
        }
        // The flow already ran its actions; freeze the group as a historical record so a
        // later flow moving the same switches can't un-tick it — but never freeze over a
        // step the flow could not deliver, or the group can never tell the truth again.
        if (!anyExcludedStillUnset)
            group.CompletionLatched = true;
        GroupProgressChanged?.Invoke(group);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoFlowCompletionExclusionTests"`

Expected: 7 passed, 0 failed.

- [ ] **Step 5: Have `FlowManager` record what it could not deliver**

In `MSFSBlindAssist/FirstOfficer/FlowManager.cs`, add the field and the property near the other private state (put the property beside `CurrentStepIndex` if that is public; otherwise directly after the field):

```csharp
    // Checklist items belonging to steps this run announced as SKIPPED — i.e. the step
    // failed and its FailurePolicy let the flow continue. FirstOfficerForm passes these to
    // MarkGroupComplete so flow completion cannot tick and latch an item the flow never
    // delivered. Only the Skip branch contributes: Stop and an exhausted RetryThenStop both
    // raise FlowFailed and return, so FlowCompleted never fires on those runs, and the
    // "Already set" early-continue is a SUCCESS (it raises StepCompleted and marks the item).
    private readonly HashSet<string> _unfinishedChecklistItemIds = new(StringComparer.Ordinal);

    /// <summary>Checklist item ids the most recent run could not deliver. Valid to read
    /// from the FlowCompleted handler; cleared when the next run starts.</summary>
    public IReadOnlyCollection<string> UnfinishedChecklistItemIds => _unfinishedChecklistItemIds;
```

Clear it where the run begins — in the same method that raises `FlowStarted`, immediately before that event is raised:

```csharp
        _unfinishedChecklistItemIds.Clear();
```

And record in the `Skip` branch:

```csharp
                    case FlowStepFailurePolicy.Skip:
                        if (!string.IsNullOrEmpty(step.CompletesChecklistItemId))
                            _unfinishedChecklistItemIds.Add(step.CompletesChecklistItemId);
                        StepSkipped?.Invoke(flow, step, i);
                        _announcer.Announce($"Skipping: {step.AnnounceText}");
                        break;
```

Do not touch the `Stop` or `RetryThenStop` branches, the `StepCompleted` branch, or the "Already set" early-continue.

- [ ] **Step 6: Pass the exclusion from the form**

In `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs`, `OnFlowCompleted`, replace:

```csharp
        foreach (var groupId in RelatedGroupIdsFor(flow))
            _checklistMgr.MarkGroupComplete(groupId);
```

with:

```csharp
        // Never tick — or latch over — an item whose step the flow announced as skipped.
        // The pilot heard "Skipping: X"; force-ticking X two seconds later contradicted
        // that out loud, and the latch made the contradiction permanent for the session.
        var unfinished = _flowMgr.UnfinishedChecklistItemIds;
        foreach (var groupId in RelatedGroupIdsFor(flow))
            _checklistMgr.MarkGroupComplete(groupId, unfinished);
```

- [ ] **Step 7: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors, exactly the six known warnings.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: 3847 passed, 0 failed (3840 + 7).

- [ ] **Step 8: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/ChecklistManager.cs MSFSBlindAssist/FirstOfficer/FlowManager.cs MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs tests/MSFSBlindAssist.Tests/FoFlowCompletionExclusionTests.cs
git commit -m "fix(fo): a finished flow no longer ticks the steps it could not do

MarkGroupComplete ticked every item in the related groups and set the
completion latch, so a step that failed and announced 'Skipping: X' was
force-ticked two seconds later and frozen that way for the session - the
live-state mirror could never correct it. FlowManager now records the
checklist items its skipped steps own, and those are neither ticked nor
latched over. A flow whose every step succeeded is unchanged.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: A failed manual tick must say so

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/FoFailedTickAnnouncementTests.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/Models/ChecklistItem.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/ChecklistManager.cs` (`ToggleItem`, `MarkComplete`, `MarkGroupComplete`, `ResetGroup`, `EvaluateAutoDetection`, plus a new event)
- Modify: `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs` (subscribe + announce)

**Interfaces:**
- Consumes: Task 1's `MarkGroupComplete(groupId, excludeItemIds)` signature.
- Produces:
  - `ChecklistItem<TExec,TState>.AwaitingActionConfirmation` (`bool`, get/set)
  - `ChecklistManager<TExec,TState>.ItemActionFailed` — `event Action<ChecklistGroup<TExec,TState>, ChecklistItem<TExec,TState>>?`

**The signal.** A revert is ordinary and must stay silent when the pilot simply moved the switch back themselves. The one case worth speaking is: the pilot ticked the item, that tick fired a linked action, and the state never came to agree. `AwaitingActionConfirmation` marks exactly that window — set when a manual tick fires an action, cleared the moment the state agrees, and read at the revert.

---

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/FoFailedTickAnnouncementTests.cs`:

```csharp
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

    // Step past the 10-second ManualTickGrace without sleeping.
    private static void AgeTheTick(ChecklistItem<FakeExec, FakeState> item)
        => item.LastManualCheckUtc = DateTime.UtcNow - TimeSpan.FromSeconds(11);

    [Fact]
    public void TickWhoseActionNeverTakes_RaisesItemActionFailed()
    {
        var (mgr, _, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));

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
        var (mgr, _, group, failures) = Build(PlainAutoItem("PLAIN", "G", "F1"));

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
        var (mgr, _, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));

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
        var (mgr, _, group, failures) = Build(ActionItem("SPDBRK", "G", "F1"));

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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoFailedTickAnnouncementTests"`

Expected: build errors — neither `ItemActionFailed` nor `AwaitingActionConfirmation` exists.

- [ ] **Step 3: Add the flag to `ChecklistItem`**

In `MSFSBlindAssist/FirstOfficer/Models/ChecklistItem.cs`, add beside the other manual-tick state (near `LastManualCheckUtc`):

```csharp
    /// <summary>
    /// Set when a MANUAL tick fired this item's <see cref="CheckAction"/>, and cleared the
    /// moment the sim state agrees (or on untick / reset / a flow marking it complete).
    /// While it stands, a RevertToState un-tick means the action the pilot asked for did
    /// not take — an error condition worth speaking. An ordinary revert, where the pilot
    /// simply moved the switch back themselves, must stay silent, and this flag is what
    /// tells the two apart.
    /// </summary>
    public bool AwaitingActionConfirmation { get; set; }
```

- [ ] **Step 4: Add the event and the tracking to `ChecklistManager`**

Add the event beside the existing two:

```csharp
    /// <summary>
    /// Raised when an item the pilot ticked BY HAND is un-ticked again because its linked
    /// action never took effect. The form speaks this — it is the only channel that reaches
    /// a blind pilot who has navigated away from the checklist tree, where the correction
    /// is otherwise silent and visual-only. Never raised for an ordinary revert.
    /// </summary>
    public event Action<ChecklistGroup<TExec, TState>, ChecklistItem<TExec, TState>>? ItemActionFailed;
```

In `ToggleItem`, inside the `if (item.IsChecked)` branch, where the action is fired:

```csharp
            if (item.CheckAction != null && _executor.IsAvailable)
            {
                item.AwaitingActionConfirmation = true;
                _ = RunCheckActionWithGraceAsync(item);
            }
```

and in the `else` branch (the manual untick), beside the latch reset:

```csharp
            item.AwaitingActionConfirmation = false;
```

In `EvaluateAutoDetection`, inside the per-item loop, replace the two state branches with:

```csharp
                if (stateMatches.Value)
                {
                    // The state agrees — whatever the pilot asked for happened. Clear the
                    // mark whether or not this pass is the one that ticks the item.
                    item.AwaitingActionConfirmation = false;
                    if (!item.IsChecked)
                    {
                        item.IsChecked = true;
                        ItemStateChanged?.Invoke(group, item);
                        groupChanged = true;
                    }
                }
                else if (item.IsChecked
                    && item.RevertBehavior == RevertBehavior.RevertToState
                    && !group.CompletionLatched
                    && !item.ActionSettling
                    && !WithinManualTickGrace(item))
                {
                    item.IsChecked = false;
                    ItemStateChanged?.Invoke(group, item);
                    groupChanged = true;
                    if (item.AwaitingActionConfirmation)
                    {
                        // Clear BEFORE raising so a handler that re-enters can't loop, and
                        // so the next polling pass cannot report the same failure twice.
                        item.AwaitingActionConfirmation = false;
                        ItemActionFailed?.Invoke(group, item);
                    }
                }
```

In `MarkComplete`, after `item.IsChecked = true;`, and in `MarkGroupComplete` where a non-excluded item is ticked, clear the mark — a flow working the group supersedes the pilot's pending request:

```csharp
                item.AwaitingActionConfirmation = false;
```

In `ResetGroup`, inside its per-item loop:

```csharp
            item.AwaitingActionConfirmation = false;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoFailedTickAnnouncementTests"`

Expected: 7 passed, 0 failed.

- [ ] **Step 6: Speak it from the form**

In `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs`, beside the existing `_checklistMgr.ItemStateChanged` subscription:

```csharp
        _checklistMgr.ItemActionFailed  += OnChecklistItemActionFailed;
```

and add the handler next to `OnChecklistItemChanged`:

```csharp
    // The pilot ticked this by hand, the linked action ran, and the state never agreed —
    // so the item just un-ticked itself. That correction is otherwise silent and
    // visual-only, which is no use to a blind pilot who has left the tree. Speaking a
    // failure is permitted under the screen-reader rule (it is an error condition, not a
    // UI interaction), and it is QUEUED so it can never cut across a landing callout.
    private void OnChecklistItemActionFailed(ChecklistGroup<TExec, TState> group,
                                             ChecklistItem<TExec, TState> item)
    {
        if (InvokeRequired) { Invoke(() => OnChecklistItemActionFailed(group, item)); return; }
        _announcer.Announce($"Unable to complete: {item.Label}");
    }
```

- [ ] **Step 7: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors, exactly the six known warnings.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: 3854 passed, 0 failed (3847 + 7).

- [ ] **Step 8: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/Models/ChecklistItem.cs MSFSBlindAssist/FirstOfficer/ChecklistManager.cs MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs tests/MSFSBlindAssist.Tests/FoFailedTickAnnouncementTests.cs
git commit -m "fix(fo): say so when a checklist tick's action does not take

Ticking an item with a linked action fires it; when the switch never moved
the item correctly un-ticked itself, but silently and visually only - no
use to a blind pilot who has left the tree. A manual tick that fired an
action is now marked as awaiting confirmation, and a revert while that mark
stands raises ItemActionFailed, which the form speaks as an error. An
ordinary revert - the pilot moving the switch back - stays silent.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Retire the known limitation and add the release note

**Files:**
- Create: `changelog.d/160-flow-false-completion.fix.md`
- Modify: `changelog.d/160-737-speedbrake-arm-verified.fix.md`
- Modify: `docs/superpowers/specs/2026-08-25-pr160-fo-procedure-fixes-design.md`
- Modify: `docs/superpowers/plans/2026-08-25-pr160-fo-procedure-fixes.md`

**Interfaces:** Consumes nothing; produces nothing consumed later.

---

- [ ] **Step 1: Confirm the PR number**

Run: `gh pr view --json number,url`

Expected: `160`. Do not guess — this repo draws issue and PR numbers from one shared sequence, so an inferred number silently attributes a change to a PR that never made it. If it prints something else, use what it prints and say so.

- [ ] **Step 2: Add the new fragment**

Create `changelog.d/160-flow-false-completion.fix.md` (markdown prose, no heading, one paragraph, pilot-facing):

```markdown
A First Officer flow no longer checks off a step it could not do. When a step fails the flow says so — "Skipping: Speedbrake: ARMED" — but a moment later, when the flow finished, it used to tick that item anyway along with the rest of the section and freeze it there for the rest of the session, so the checklist told you the switch was set when it never moved and could never correct itself. And if you ticked an item by hand and its action did not take, the tick quietly undid itself with nothing said; you now hear "Unable to complete" and the item's name. Affects every aircraft with First Officer support.
```

- [ ] **Step 3: Drop the caveat from the 737 fragment**

`changelog.d/160-737-speedbrake-arm-verified.fix.md` currently describes what happens when the arm fails. Task 1 and Task 2 changed that behaviour, so re-read the fragment and correct any clause that is now wrong — in particular anything describing the flow path leaving the item ticked, or the manual-tick path being silent. Keep it one paragraph, keep the DO NOT ARM clause. Do not add a second fragment for the 737; edit this one in place (it has not been released — it was added on this branch and no tag has been cut).

- [ ] **Step 4: Retire the known-limitation note in the design doc**

In `docs/superpowers/specs/2026-08-25-pr160-fo-procedure-fixes-design.md`, §3 carries a **Known limitation** note about `MarkGroupComplete` force-ticking and latching, saying the owner deliberately scoped it out. That is no longer true. Replace it with a short note recording that it WAS fixed, on which date, by which mechanism (`FlowManager.UnfinishedChecklistItemIds` → `MarkGroupComplete`'s `excludeItemIds`), and that the manual-tick path now raises `ItemActionFailed`. Keep the description of the original defect — it is why the fix exists.

Then correct the in-sim test plan's item 3 (737 landing) so it describes the behaviour that now ships: a failed arm leaves the item un-ticked on both paths, is announced as a skip by the flow, and is announced as "Unable to complete" on a manual tick.

- [ ] **Step 5: Correct the same test-plan text in the plan doc**

`docs/superpowers/plans/2026-08-25-pr160-fo-procedure-fixes.md`, Task 5 Step 5, item 3, carries the same in-sim wording. Make it match Step 4.

- [ ] **Step 6: Commit**

Both `docs/superpowers/` files are gitignored in this repo but tracked anyway — you must `git add -f` them or they will silently not be committed.

```bash
git add changelog.d/160-flow-false-completion.fix.md changelog.d/160-737-speedbrake-arm-verified.fix.md
git add -f docs/superpowers/specs/2026-08-25-pr160-fo-procedure-fixes-design.md docs/superpowers/plans/2026-08-25-pr160-fo-procedure-fixes.md
git commit -m "docs: record the flow false-completion fix and retire its known limitation

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Report the in-sim check**

Do not push. Hand this back for the PR body:

- Run a flow with a step you know will fail (e.g. the 737 Landing flow with the sim paused so the speedbrake cannot move). You should hear "Skipping: Speedbrake: ARMED", and when the flow completes that item must remain **un-ticked** while the rest of the section ticks. The section header must not read complete.
- Then set the switch by hand — the item must tick itself, proving the group was not latched.
- Tick an action item by hand whose switch cannot move. Within ~10 s you should hear "Unable to complete:" and the item's name, and see it un-tick.
- Run any flow where every step succeeds, on any aircraft. It must behave exactly as before: everything ticks, section reads complete, nothing speaks.
