using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.FirstOfficer.Models;

/// <summary>
/// A named phase/section of the PMDG 777 checklist containing <see cref="ChecklistItem{TExec,TState}"/> entries.
/// </summary>
public class ChecklistGroup<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : IFoStateEvaluator
{
    /// <summary>Unique ID, e.g. "ELEC_POWER_UP".</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name shown as the TreeView parent node, e.g. "Electrical Power Up".</summary>
    public string Name { get; set; } = "";

    /// <summary>Ordered list of items in this group.</summary>
    public List<ChecklistItem<TExec, TState>> Items { get; set; } = new();

    // -----------------------------------------------------------------------
    // Completion latch (see ChecklistManager.EvaluateAutoDetection)
    // -----------------------------------------------------------------------

    /// <summary>True once a user manual tick or a flow MarkComplete touched this group.
    /// Guards the completion latch: coincidental auto-ticks alone can never freeze a
    /// group as complete (that would be the group-shaped version of the removed
    /// item-level StayComplete false-latch bug).</summary>
    public bool HasParticipation { get; set; }

    /// <summary>Set when the group reaches 100% with participation. While latched,
    /// RevertToState un-ticking is suppressed for this group — a completed checklist is
    /// a historical record of its phase, not a live mirror. Cleared by ResetGroup /
    /// ResetAll and by any manual untick in the group.</summary>
    public bool CompletionLatched { get; set; }

    // -----------------------------------------------------------------------
    // Computed progress
    // -----------------------------------------------------------------------

    public int TotalActionable => Items.Count(i => i.Type != ChecklistItemType.Informational);

    public int CompletedCount => Items.Count(i => i.IsChecked);

    public ChecklistGroupStatus Status
    {
        get
        {
            if (TotalActionable == 0) return ChecklistGroupStatus.Complete;
            if (CompletedCount == 0) return ChecklistGroupStatus.NotStarted;
            if (CompletedCount >= TotalActionable) return ChecklistGroupStatus.Complete;
            return ChecklistGroupStatus.InProgress;
        }
    }

    /// <summary>
    /// Label shown on the TreeView parent node.
    /// Example: "Before Start Checklist — 3/5 complete"
    /// </summary>
    public string ProgressLabel
    {
        get
        {
            string suffix = Status switch
            {
                ChecklistGroupStatus.NotStarted => "Not started",
                ChecklistGroupStatus.Complete   => "Complete",
                ChecklistGroupStatus.InProgress => $"{CompletedCount} of {TotalActionable} complete",
                _                              => ""
            };
            return $"{Name} — {suffix}";
        }
    }

    public void ResetAll()
    {
        foreach (var item in Items)
            item.IsChecked = false;
    }
}

public enum ChecklistGroupStatus
{
    NotStarted,
    InProgress,
    Complete,
}
