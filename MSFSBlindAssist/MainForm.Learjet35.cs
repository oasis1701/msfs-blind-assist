using MSFSBlindAssist.Aircraft.Learjet35;

namespace MSFSBlindAssist;

// Flysimware Learjet 35A support: the monitor-manager dialog, the display windows' lifetime
// and the menu entry. Kept in its own partial so the upstream MainForm partials stay
// untouched; the form field declarations live in MainForm.cs.
public partial class MainForm
{
    public void ShowLj35MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (lj35MonitorManagerForm == null || lj35MonitorManagerForm.IsDisposed)
        {
            lj35MonitorManagerForm = new Forms.Learjet35.Lj35MonitorManagerForm(currentAircraft.GetVariables());
        }
        lj35MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// Disposes every Learjet-owned form on an aircraft switch. The monitor manager snapshots the
    /// variable dictionary at construction, so a stale instance would list the previous
    /// aircraft's rows; the display windows each hold a Coherent socket that must be released.
    /// </summary>
    private void DisposeLj35Forms(Aircraft.IAircraftDefinition? oldAircraft)
    {
        if (lj35MonitorManagerForm != null && !lj35MonitorManagerForm.IsDisposed) lj35MonitorManagerForm.Dispose();
        lj35MonitorManagerForm = null;
        (oldAircraft as FlysimwareLearjet35ADefinition)?.DisposeWindows();
    }

    private void Lj35MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FlysimwareLearjet35ADefinition());
    }
}
