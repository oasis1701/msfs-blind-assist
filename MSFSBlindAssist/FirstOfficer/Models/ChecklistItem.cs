using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.FirstOfficer.Models;

/// <summary>
/// Whether the checklist item represents an automatable action,
/// a state that can be detected from sim data, a captain reminder,
/// or purely informational text.
/// </summary>
public enum ChecklistItemType
{
    /// <summary>
    /// The user can action this in the panels or flow. Auto-detection may be possible.
    /// </summary>
    Actionable,

    /// <summary>
    /// State is read from sim variables. No switch to action; item completes automatically.
    /// </summary>
    AutoDetectable,

    /// <summary>
    /// A reminder for the captain — cannot be automated. User must manually tick.
    /// </summary>
    CaptainReminder,

    /// <summary>
    /// Informational only — not actionable, not verifiable. Shown for context.
    /// </summary>
    Informational,
}

/// <summary>
/// How the item reacts if the aircraft state changes AFTER the item was marked complete.
/// </summary>
public enum RevertBehavior
{
    /// <summary>
    /// Historical action: once completed (by user or flow), stays complete regardless of state.
    /// Use for switch actions that shouldn't un-tick just because a related state changed.
    /// Example: "Window Heat ON" — stays ticked even if one window heat later shows INOP.
    /// </summary>
    StayComplete,

    /// <summary>
    /// Live state item: reverts to incomplete if the sim state no longer matches.
    /// Use for configuration items that must be correct at the moment of use.
    /// Example: "Landing Gear DOWN", "Speedbrake ARMED".
    /// </summary>
    RevertToState,
}

/// <summary>
/// A single checklist item within a <see cref="ChecklistGroup{TExec,TState}"/>.
/// </summary>
public class ChecklistItem<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : IFoStateEvaluator
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    /// <summary>Unique ID within the full checklist set, e.g. "BEFORESTART_BEACON".</summary>
    public string Id { get; set; } = "";

    /// <summary>ID of the parent <see cref="ChecklistGroup"/>.</summary>
    public string GroupId { get; set; } = "";

    /// <summary>Human-readable label shown in the TreeView, e.g. "Beacon: ON".</summary>
    public string Label { get; set; } = "";

    // -----------------------------------------------------------------------
    // Type and state
    // -----------------------------------------------------------------------

    public ChecklistItemType Type { get; set; } = ChecklistItemType.Actionable;

    /// <summary>Whether the user has manually ticked this item or a flow has completed it.</summary>
    public bool IsChecked { get; set; }

    /// <summary>
    /// UTC time of the last manual tick TO checked (set by ChecklistManager.ToggleItem).
    /// EvaluateAutoDetection suppresses RevertToState un-ticking within a short grace
    /// window of this, so a tick whose CheckAction is still writing frame-spaced switch
    /// events isn't reverted before the aircraft state has had a chance to catch up.
    /// </summary>
    public DateTime? LastManualCheckUtc { get; set; }

    /// <summary>Whether the user can tick/untick this item manually via the TreeView.</summary>
    public bool ManualCompletionAllowed { get; set; } = true;

    // -----------------------------------------------------------------------
    // Manual-tick action settling (ChecklistManager.RunCheckActionWithGraceAsync)
    // -----------------------------------------------------------------------

    private int _actionSettlingCount;
    private long _actionGraceUtcTicks;

    /// <summary>
    /// True from a manual tick until the tick's CheckAction has completed AND the
    /// executor's dispatch queue has drained past its writes. RevertToState never
    /// un-ticks while set — a fixed grace measured from tick time loses to the
    /// closed-loop selector walks (transponder / position lights, 4–20+ s with
    /// dropped clicks) and to writes queued behind one on the serialized dispatch
    /// gate. A COUNT, not a bool: an untick + quick re-tick overlaps two settling
    /// tasks, and the first one's completion must not strip the second's protection.
    /// Written from background continuations, read by the UI evaluation timer.
    /// </summary>
    public bool ActionSettling => Volatile.Read(ref _actionSettlingCount) > 0;

    public void BeginActionSettling() => Interlocked.Increment(ref _actionSettlingCount);
    public void EndActionSettling() => Interlocked.Decrement(ref _actionSettlingCount);

    /// <summary>Stamps the post-action grace clock. Ticks-based + Volatile because the
    /// writer is a background continuation while the reader is the UI evaluation timer
    /// (a DateTime? property write is not atomic).</summary>
    public void StampActionGraceUtc() => Volatile.Write(ref _actionGraceUtcTicks, DateTime.UtcNow.Ticks);

    /// <summary>UTC time the manual tick's action finished draining, or null if never.</summary>
    public DateTime? ActionGraceUtc
    {
        get
        {
            long t = Volatile.Read(ref _actionGraceUtcTicks);
            return t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
        }
    }

    // -----------------------------------------------------------------------
    // Auto-detection (state evaluation from PMDG data)
    // -----------------------------------------------------------------------

    /// <summary>Whether to attempt auto-completion by reading sim state.</summary>
    public bool AutoCompleteAllowed { get; set; }

    /// <summary>
    /// Name of the PMDG data field to read for auto-detection.
    /// If multiple fields are required, use <see cref="AdditionalStateFields"/>.
    /// </summary>
    public string? StateFieldName { get; set; }

    /// <summary>Additional PMDG fields that all must satisfy the condition.</summary>
    public IReadOnlyList<string> AdditionalStateFields { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Condition applied to the primary field value. Returns true if the item should be marked complete.
    /// If null and <see cref="ExpectedValue"/> is set, equality is used.
    /// </summary>
    public Func<double, bool>? StateCondition { get; set; }

    /// <summary>Simple equality check — used when <see cref="StateCondition"/> is null.</summary>
    public double? ExpectedValue { get; set; }

    /// <summary>
    /// If set, additional state fields are checked against this condition.
    /// If null, the primary <see cref="StateCondition"/> is reused.
    /// </summary>
    public Func<double, bool>? AdditionalStateCondition { get; set; }

    // -----------------------------------------------------------------------
    // Revert behavior
    // -----------------------------------------------------------------------

    public RevertBehavior RevertBehavior { get; set; } = RevertBehavior.StayComplete;

    // -----------------------------------------------------------------------
    // Linked sim action
    // -----------------------------------------------------------------------

    /// <summary>
    /// If set, executed by <see cref="ChecklistManager{TExec,TState}"/> when the user checks this item ON.
    /// Does nothing on uncheck — just resets the checkbox state.
    /// The state evaluator is provided so toggle-type actions (FD, AT Arm) can read
    /// current state before deciding to press. Leave null for verify/briefing items.
    /// </summary>
    public Func<TExec, TState, Task>? CheckAction { get; set; }

    /// <summary>
    /// Momentary "button" action (a self-completing TEST or one-shot press with no
    /// persistent sim state — fire/overheat test, warning tests). When true, a MANUAL
    /// tick's <see cref="CheckAction"/> runs and the item then AUTO-CLEARS back to
    /// unchecked once the action completes, so a single check re-triggers it every time
    /// (no uncheck-then-recheck cycle). Flow-driven completion (MarkComplete) is
    /// unaffected — it doesn't run the CheckAction, so the flow's tick still persists.
    /// </summary>
    public bool MomentaryAction { get; set; }

    // -----------------------------------------------------------------------
    // Linking to flows
    // -----------------------------------------------------------------------

    /// <summary>
    /// If set, completing this item's flow step auto-checks this item.
    /// Also used by "Run Related Flow" button.
    /// </summary>
    public string? RelatedFlowId { get; set; }

    // -----------------------------------------------------------------------
    // Captain reminder
    // -----------------------------------------------------------------------

    /// <summary>Text spoken to the captain for CaptainReminder type items.</summary>
    public string? ReminderText { get; set; }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Returns true if this item can be auto-completed at all.</summary>
    public bool IsAutoDetectable => AutoCompleteAllowed && StateFieldName != null;

    /// <summary>
    /// Evaluate whether the given field value satisfies the completion condition.
    /// </summary>
    public bool EvaluateState(double value)
    {
        if (StateCondition != null) return StateCondition(value);
        if (ExpectedValue.HasValue) return Math.Abs(value - ExpectedValue.Value) < 0.01;
        return false;
    }

    /// <summary>
    /// Evaluate an additional field value (uses AdditionalStateCondition if set, else primary).
    /// </summary>
    public bool EvaluateAdditionalState(double value)
    {
        var cond = AdditionalStateCondition ?? StateCondition;
        if (cond != null) return cond(value);
        if (ExpectedValue.HasValue) return Math.Abs(value - ExpectedValue.Value) < 0.01;
        return false;
    }
}
