using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Manages the runtime state of all PMDG 777 checklist groups.
/// Handles auto-completion, revert logic, and manual toggle.
/// </summary>
public class ChecklistManager
{
    private readonly AircraftStateEvaluator _state;
    private readonly AircraftActionExecutor _executor;
    private readonly List<ChecklistGroup> _groups;

    // Raised when any item's IsChecked state changes.
    public event Action<ChecklistGroup, ChecklistItem>? ItemStateChanged;

    // Raised when a group's overall progress changes.
    public event Action<ChecklistGroup>? GroupProgressChanged;

    public IReadOnlyList<ChecklistGroup> Groups => _groups;

    public ChecklistManager(AircraftStateEvaluator state, AircraftActionExecutor executor,
        List<ChecklistGroup> groups)
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
        var item = FindItem(groupId, itemId);
        if (item == null || !item.ManualCompletionAllowed) return null;

        item.IsChecked = !item.IsChecked;

        // If the item is now checked AND has a linked action, execute it.
        if (item.IsChecked && item.CheckAction != null && _executor.IsAvailable)
            item.CheckAction(_executor, _state);

        RaiseChanged(FindGroup(groupId)!, item);
        return item.IsChecked;
    }

    /// <summary>Mark an item complete — called when a flow step succeeds.</summary>
    public void MarkComplete(string itemId)
    {
        foreach (var group in _groups)
        {
            var item = group.Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null) continue;
            if (item.IsChecked) return;
            item.IsChecked = true;
            RaiseChanged(group, item);
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

                bool stateMatches = EvaluateItemState(item);

                if (stateMatches && !item.IsChecked)
                {
                    item.IsChecked = true;
                    ItemStateChanged?.Invoke(group, item);
                    groupChanged = true;
                }
                else if (!stateMatches && item.IsChecked
                    && item.RevertBehavior == RevertBehavior.RevertToState)
                {
                    item.IsChecked = false;
                    ItemStateChanged?.Invoke(group, item);
                    groupChanged = true;
                }
            }

            if (groupChanged)
                GroupProgressChanged?.Invoke(group);
        }
    }

    // -----------------------------------------------------------------------
    // Lookup helpers
    // -----------------------------------------------------------------------

    public ChecklistGroup? FindGroup(string groupId)
        => _groups.FirstOrDefault(g => g.Id == groupId);

    public ChecklistItem? FindItem(string groupId, string itemId)
        => FindGroup(groupId)?.Items.FirstOrDefault(i => i.Id == itemId);

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private bool EvaluateItemState(ChecklistItem item)
    {
        double primary = _state.GetValue(item.StateFieldName!);
        if (!item.EvaluateState(primary)) return false;

        foreach (var field in item.AdditionalStateFields)
        {
            double v = _state.GetValue(field);
            if (!item.EvaluateAdditionalState(v)) return false;
        }

        return true;
    }

    private void RaiseChanged(ChecklistGroup group, ChecklistItem item)
    {
        ItemStateChanged?.Invoke(group, item);
        GroupProgressChanged?.Invoke(group);
    }
}
