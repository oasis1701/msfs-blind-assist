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
                _ = RunCheckActionWithGraceAsync(item);
            // No TryLatch here: the fresh grace stamp always defers arming (see
            // TryLatch) — the next EvaluateAutoDetection pass arms it once the tick's
            // readback has had its chance to surface a failed action.
        }
        else
        {
            // A manual untick re-opens the group: the live mirror (and reverts) resume.
            group.CompletionLatched = false;
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
            if (!item.IsChecked)
            {
                item.IsChecked = true;
                RaiseChanged(group, item);
            }
            TryLatch(group);
            return;
        }
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

                if (stateMatches.Value && !item.IsChecked)
                {
                    item.IsChecked = true;
                    ItemStateChanged?.Invoke(group, item);
                    groupChanged = true;
                }
                else if (!stateMatches.Value && item.IsChecked
                    && item.RevertBehavior == RevertBehavior.RevertToState
                    && !group.CompletionLatched
                    && !item.ActionSettling
                    && !WithinManualTickGrace(item))
                {
                    item.IsChecked = false;
                    ItemStateChanged?.Invoke(group, item);
                    groupChanged = true;
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

            // Momentary "button" test (fire/overheat, warning tests): once the action's
            // sound/hold has completed, AUTO-CLEAR the manual tick so a single check
            // re-triggers it every time — no silent uncheck-then-recheck cycle. Only the
            // MANUAL tick path runs this method; the flow's MarkComplete does NOT, so a
            // flow that works the same item still marks it complete and it stays checked.
            if (item.MomentaryAction && item.IsChecked)
            {
                var group = FindGroup(item.GroupId);
                item.IsChecked = false;
                if (group != null) RaiseChanged(group, item);
            }
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
