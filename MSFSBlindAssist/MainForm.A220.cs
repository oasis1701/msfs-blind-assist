using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>
/// The Synaptic A220-300's hooks into the shell: the Aircraft-menu entry and the Ctrl+M
/// Announcement Monitor window. Everything else the A220 needs lives in the definition
/// itself (<see cref="SynapticA220Definition"/> and its partials) or in
/// <c>Forms/A220</c> — its hotkeys are routed by the def's own
/// <c>HandleHotkeyAction</c>, so there is nothing to add to the shared hotkey table.
///
/// Kept as its own partial so the aircraft is one self-contained set of files: adding it
/// touches the shared MainForm partials only where the shell genuinely has to know it
/// exists (the menu check-state map, the outgoing-def teardown, the monitor-mute gate).
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Ctrl+M for the A220. The def honours the muted set itself for the variables it
    /// announces from inside <c>ProcessSimVarUpdate</c> (FG modes, APU, reversers);
    /// the lamps and plain combos are gated on the generic announce path in
    /// <c>MainForm.Announcers.cs</c>. Both read the same
    /// <c>UserSettings.A220DisabledMonitorVariablesSet</c>.
    /// </summary>
    public void ShowA220MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (a220MonitorManagerForm == null || a220MonitorManagerForm.IsDisposed)
        {
            a220MonitorManagerForm = new Forms.A220.A220MonitorManagerForm(currentAircraft.GetVariables());
        }
        a220MonitorManagerForm.ShowForm();
    }

    private void SynapticA220MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new SynapticA220Definition());
    }
}
