using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Manages the runtime state of all PMDG 777 checklist groups.
/// Handles auto-completion, revert logic, and manual toggle.
/// </summary>
public class ChecklistManager<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : IFoStateEvaluator
{
    private readonly TState _state;
    private readonly TExec _executor;
    private readonly List<ChecklistGroup<TExec, TState>> _groups;

    // Raised when any item's IsChecked state changes.
    public event Action<ChecklistGroup<TExec, TState>, ChecklistItem<TExec, TState>>? ItemStateChanged;

    // Raised when a group's overall progress changes.
    public event Action<ChecklistGroup<TExec, TState>>? GroupProgressChanged;

    /// <summary>
    /// Raised when an item the pilot ticked BY HAND is un-ticked again because its linked
    /// action never took effect. The form speaks this — it is the only channel that reaches
    /// a blind pilot who has navigated away from the checklist tree, where the correction
    /// is otherwise silent and visual-only. Never raised for an ordinary revert.
    /// </summary>
    public event Action<ChecklistGroup<TExec, TState>, ChecklistItem<TExec, TState>>? ItemActionFailed;

    public IReadOnlyList<ChecklistGroup<TExec, TState>> Groups => _groups;

    public ChecklistManager(TState state, TExec executor,
        List<ChecklistGroup<TExec, TState>> groups)
    {
        _state    = state;
        _executor = executor;
        _groups   = groups;
    }

    // -----------------------------------------------------------------------
    // Manual toggle
    // -----------------------------------------------------------------------

    /// <summary>
    /// Toggle the IsChecked state of an item. Only works if ManualCompletionAllowed.
    /// Returns the new checked state, or null if toggling was not permitted.
    /// </summary>
    public bool? ToggleItem(string groupId, string itemId)
    {
        var group = FindGroup(groupId);
        var item  = group?.Items.FirstOrDefault(i => i.Id == itemId);
        if (group == null || item == null || !item.ManualCompletionAllowed) return null;

        item.IsChecked = !item.IsChecked;

        // If the item is now checked AND has a linked action, execute it.
        if (item.IsChecked)
        {
            // Stamp the manual tick so auto-detection grants the fired action a grace
            // window before RevertToState can un-tick it (frame-spaced writes + the CDA
            // snapshot cadence mean the state can lag the tick by several seconds).
            item.LastManualCheckUtc = DateTime.UtcNow;
            group.HasParticipation = true;
            if (item.CheckAction != null && _executor.IsAvailable)
            {
                item.AwaitingActionConfirmation = true;
                _ = RunCheckActionWithGraceAsync(item);
            }
            // No TryLatch here: the fresh grace stamp always defers arming (see
            // TryLatch) — the next EvaluateAutoDetection pass arms it once the tick's
            // readback has had its chance to surface a failed action.
        }
        else
        {
            // A manual untick re-opens the group: the live mirror (and reverts) resume.
            group.CompletionLatched = false;
            item.ExemptFromCompletionLatch = false;
            // The pilot is withdrawing the request themselves — nothing is owed to them
            // if the (now-moot) action never lands.
            item.AwaitingActionConfirmation = false;
        }

        RaiseChanged(group, item);
        return item.IsChecked;
    }

    /// <summary>Mark an item complete — called when a flow step succeeds.</summary>
    public void MarkComplete(string itemId)
    {
        foreach (var group in _groups)
        {
            var item = group.Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null) continue;
            group.HasParticipation = true; // a flow worked this group
            // A real delivery: this item is no longer the one the flow could not
            // perform, even if an earlier run left it exempted. Cleared
            // unconditionally — whether or not THIS call is the one that ticks the
            // item — because the item may already be checked (e.g. a prior manual
            // tick) when the flow's own delivery lands. Without this, a re-run
            // whose step now succeeds can leave the item exempt from the group
            // latch forever, so it alone keeps un-ticking itself whenever the
            // switch later moves — the exact false-completion the exemption exists
            // to prevent, narrowed to one item.
            item.ExemptFromCompletionLatch = false;
            if (!item.IsChecked)
            {
                item.IsChecked = true;
                // A flow working this group supersedes the pilot's pending request.
                item.AwaitingActionConfirmation = false;
                RaiseChanged(group, item);
            }
            TryLatch(group);
            return;
        }
    }

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
    /// steps it announced as skipped. Those are neither ticked nor force-latched over.
    /// Without it, a step that failed and said so out loud ("Skipping: Speedbrake: ARMED")
    /// was force-ticked two seconds later at flow completion AND frozen by the latch, so the
    /// live-state mirror could never correct it: a blind pilot reading the Landing
    /// checklist on final was told the speedbrake was armed when it was not.
    ///
    /// An item is ALSO treated as excluded, regardless of <paramref name="excludeItemIds"/>,
    /// when <see cref="ChecklistItem{TExec,TState}.NeverForceComplete"/> is set — a step
    /// that isn't a failure (nothing was skipped) but whose item the app can never actually
    /// deliver, because the underlying switch cannot be written (e.g. the 737 gear lever).
    /// Same treatment, same reasoning: force-ticking it would assert a lie a live mirror
    /// could never correct once the group latches.
    ///
    /// The group latch ALWAYS arms here, unconditionally — the flow's completed steps are
    /// still a flight-long historical record and must survive switches moving later for the
    /// rest of the group, exactly as before an excluded item ever existed. What changes is
    /// per-item: an excluded item is marked <see cref="ChecklistItem{TExec,TState}.ExemptFromCompletionLatch"/>
    /// so IT ALONE keeps mirroring live state inside the otherwise-latched group — a single
    /// failed step no longer strips the historical record from every sibling item that
    /// really did complete.
    /// </summary>
    public void MarkGroupComplete(string groupId, IReadOnlyCollection<string>? excludeItemIds = null)
    {
        var group = FindGroup(groupId);
        if (group == null) return;

        group.HasParticipation = true;
        foreach (var item in group.Items)
        {
            if (item.Type == ChecklistItemType.Informational) continue;   // separators aren't tickable
            bool excluded = item.NeverForceComplete
                || (excludeItemIds != null && excludeItemIds.Contains(item.Id));
            if (excluded)
            {
                // Exempt whenever the flow did not itself deliver this item: it is
                // un-checked, OR it is checked only because the PILOT hand-ticked it
                // (AwaitingActionConfirmation) while the flow was skipping it. Without
                // the second half, a pilot who reacts to "Skipping: ..." by ticking the
                // box before the flow finishes hands the item a checked-with-no-
                // exemption state at MarkGroupComplete — frozen by the latch below with
                // no way back to a live mirror, silently defeating both the exemption
                // and the failed-tick announcement.
                if (!item.IsChecked || item.AwaitingActionConfirmation)
                    item.ExemptFromCompletionLatch = true;
                continue;
            }
            // A real delivery clears any exemption left over from a prior run of this
            // same flow (see the identical clear in MarkComplete above) — this item is
            // no longer the one that could not be performed. Cleared unconditionally,
            // not only when this call is the one that ticks the item (see MarkComplete
            // for why that matters).
            item.ExemptFromCompletionLatch = false;
            if (!item.IsChecked)
            {
                item.IsChecked = true;
                // A flow working this group supersedes the pilot's pending request.
                item.AwaitingActionConfirmation = false;
                ItemStateChanged?.Invoke(group, item);
            }
        }
        // The flow already ran its actions; freeze the group as a historical record so a
        // later flow moving the same switches can't un-tick it. The one item the flow could
        // not deliver is individually exempted above, so it keeps telling the truth.
        group.CompletionLatched = true;
        GroupProgressChanged?.Invoke(group);
    }

    // -----------------------------------------------------------------------
    // Reset
    // -----------------------------------------------------------------------

    public void ResetGroup(string groupId)
    {
        var group = FindGroup(groupId);
        if (group == null) return;
        group.CompletionLatched = false;
        group.HasParticipation  = false;
        foreach (var item in group.Items)
        {
            item.ExemptFromCompletionLatch = false;
            item.AwaitingActionConfirmation = false;
            item.LastManualCheckUtc = null;
            item.ClearActionGrace();
            if (item.IsChecked)
            {
                item.IsChecked = false;
                RaiseChanged(group, item);
            }
        }
    }

    public void ResetAll()
    {
        foreach (var group in _groups)
            ResetGroup(group.Id);
    }

    // -----------------------------------------------------------------------
    // Auto-detection — called periodically when sim data arrives
    // -----------------------------------------------------------------------

    /// <summary>
    /// Evaluate all auto-detectable items against current sim state.
    /// Call this at a reasonable polling frequency (e.g. once per second from a timer).
    /// </summary>
    public void EvaluateAutoDetection()
    {
        if (!_state.IsAvailable) return;

        foreach (var group in _groups)
        {
            bool groupChanged = false;

            foreach (var item in group.Items)
            {
                if (!item.IsAutoDetectable) continue;

                bool? stateMatches = EvaluateItemState(item);

                // null = indeterminate (e.g. no SimBrief plan loaded, CDA not yet ready).
                // Skip BOTH auto-tick AND auto-revert so a manual tick is never disturbed
                // by a state that cannot currently be evaluated.
                if (stateMatches is null) continue;

                if (stateMatches.Value)
                {
                    // The state agrees — whatever the pilot asked for happened. Clear
                    // both marks whether or not this pass is the one that ticks the
                    // item: a hand-tick that later comes to agree with live state is
                    // just as much "the switch moved" as a flow's own delivery, and
                    // must stop being treated as unresolved/exempt either way.
                    item.AwaitingActionConfirmation = false;
                    item.ExemptFromCompletionLatch = false;
                    if (!item.IsChecked)
                    {
                        item.IsChecked = true;
                        ItemStateChanged?.Invoke(group, item);
                        groupChanged = true;
                    }
                }
                else if (item.IsChecked
                    && item.RevertBehavior == RevertBehavior.RevertToState
                    && (!group.CompletionLatched || item.ExemptFromCompletionLatch)
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
            }

            TryLatch(group);

            if (groupChanged)
                GroupProgressChanged?.Invoke(group);
        }
    }

    // Arm the completion latch: only a group the user or a flow actually worked
    // (HasParticipation) freezes at 100%. Coincidentally-true auto conditions alone
    // never latch — those groups stay live mirrors and keep reverting. Arming is
    // DEFERRED while any item is still inside its action-settling / manual-tick grace
    // window: a genuinely failed group-final tick (switch never moves) must first get
    // its chance to revert — surface — before the group freezes as a historical
    // record, keeping the RunCheckActionWithGraceAsync failure guarantee true.
    private void TryLatch(ChecklistGroup<TExec, TState> group)
    {
        if (group.CompletionLatched || !group.HasParticipation
            || group.Status != ChecklistGroupStatus.Complete)
            return;

        foreach (var item in group.Items)
            if (item.ActionSettling || WithinManualTickGrace(item))
                return;

        group.CompletionLatched = true;
    }

    // Grace window during which RevertToState does not un-tick the item, measured from
    // BOTH the manual tick AND the tick's action-drained stamp (see
    // RunCheckActionWithGraceAsync) — the CDA snapshot cadence means the state can lag
    // the last write by a second or two. Auto-TICKING is never delayed (an early truth
    // is fine); only the revert is. 10 s covers the readback lag with margin; SLOW
    // actions are covered by ActionSettling, not by inflating this constant.
    private static readonly TimeSpan ManualTickGrace = TimeSpan.FromSeconds(10);

    // Cap on waiting for the executor's dispatch queue to drain after a tick's action —
    // generous headroom over the worst closed-loop selector walk (~23 s) plus writes
    // queued ahead of it, while guaranteeing a wedged gate can't suppress revert forever.
    private static readonly TimeSpan ActionDrainCap = TimeSpan.FromSeconds(45);

    private static bool WithinManualTickGrace(ChecklistItem<TExec, TState> item)
    {
        var now = DateTime.UtcNow;
        if (item.LastManualCheckUtc is DateTime t && now - t < ManualTickGrace) return true;
        return item.ActionGraceUtc is DateTime g && now - g < ManualTickGrace;
    }

    /// <summary>
    /// Runs a manual tick's CheckAction and keeps the RevertToState grace honest for
    /// SLOW actions. A fixed grace measured from tick time loses to (a) the closed-loop
    /// selector walks (transponder / position lights — 4–20+ s, unbounded by dropped
    /// clicks and per-detent fresh-snapshot awaits) and (b) ANY write queued behind such
    /// a walk on the executor's serialized dispatch gate — both reverted fresh ticks
    /// mid-action (the 2026-07-06 "transponder / strobe won't stay ticked" bug).
    /// ActionSettling suppresses revert from tick until the action completes AND the
    /// dispatch queue drains past its writes (fire-and-forget actions return before
    /// their writes clear the gate, so the drain wait is what actually covers them);
    /// the post-drain grace stamp then gives the ~1 Hz readback a full window to show
    /// the landed switch. A genuinely failed action (switch never moves) still
    /// surfaces: settling clears, the grace expires, and the item reverts.
    /// </summary>
    private async Task RunCheckActionWithGraceAsync(ChecklistItem<TExec, TState> item)
    {
        item.BeginActionSettling();
        try
        {
            try { await item.CheckAction!(_executor, _state); }
            catch { /* an action failure must never wedge the settling count */ }
            await Task.WhenAny(_executor.WaitForDispatchDrainAsync(), Task.Delay(ActionDrainCap));
            item.StampActionGraceUtc();
        }
        finally
        {
            item.EndActionSettling();
        }
    }

    // -----------------------------------------------------------------------
    // Lookup helpers
    // -----------------------------------------------------------------------

    public ChecklistGroup<TExec, TState>? FindGroup(string groupId)
        => _groups.FirstOrDefault(g => g.Id == groupId);

    public ChecklistItem<TExec, TState>? FindItem(string groupId, string itemId)
        => FindGroup(groupId)?.Items.FirstOrDefault(i => i.Id == itemId);

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private bool? EvaluateItemState(ChecklistItem<TExec, TState> item)
    {
        double primary = _state.GetValue(item.StateFieldName!);
        if (double.IsNaN(primary)) return null;
        if (!item.EvaluateState(primary)) return false;

        foreach (var field in item.AdditionalStateFields)
        {
            double v = _state.GetValue(field);
            if (double.IsNaN(v)) return null;
            if (!item.EvaluateAdditionalState(v)) return false;
        }

        return true;
    }

    private void RaiseChanged(ChecklistGroup<TExec, TState> group, ChecklistItem<TExec, TState> item)
    {
        ItemStateChanged?.Invoke(group, item);
        GroupProgressChanged?.Invoke(group);
    }
}
