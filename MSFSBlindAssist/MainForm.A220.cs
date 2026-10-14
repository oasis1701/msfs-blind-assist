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

    private SynapticA220Definition? _a220BridgeDef;

    /// <summary>
    /// The A220's CommBus bridge ([A220-39]): bus-only control states (transponder mode, TCAS
    /// altitude limit, baro STD, minimums mode, the entry fields' current values) arrive as
    /// SILENT initial-snapshot updates, so the panel's combos and entry fields show the aircraft's
    /// state without any announcement — the iFly SDK bridge's pattern. Re-pointed on every
    /// aircraft switch; a no-op unless the A220 is loaded.
    /// </summary>
    private void StartA220CommBusBridge()
    {
        if (_a220BridgeDef != null)
        {
            _a220BridgeDef.CommBusValueChanged -= OnA220CommBusValue;
            _a220BridgeDef = null;
        }
        if (currentAircraft is not SynapticA220Definition a220) return;
        a220.CommBusValueChanged += OnA220CommBusValue;
        _a220BridgeDef = a220;
    }

    private void OnA220CommBusValue(string varKey, double value)
    {
        if (currentAircraft is not SynapticA220Definition) return;
        OnSimVarUpdated(this, new SimConnect.SimVarUpdateEventArgs
        {
            VarName = varKey,
            Value = value,
            Description = string.Empty,
            IsInitialSnapshot = true,
        });
    }

    private void SynapticA220MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new SynapticA220Definition());
    }
}
