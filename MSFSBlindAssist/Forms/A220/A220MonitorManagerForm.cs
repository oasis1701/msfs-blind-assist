using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Per-variable background-announcement manager for the Synaptic A220 (Ctrl+M). All UI lives in
/// <see cref="MonitorManagerFormBase"/> (MON-1: search box, Show filter, hide-on-close); this
/// form supplies the title, the rows — the variable rows plus the synthetic CommBus call-out
/// rows (<see cref="A220MonitorRows"/>) — and UserSettings.A220DisabledMonitorVariables, which
/// MainForm's DefAnnounceMuteSets wrap, its generic gate and the def's own checks all read.
/// </summary>
public sealed class A220MonitorManagerForm : MonitorManagerFormBase
{
    public A220MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("A220 Monitor Manager", A220MonitorRows.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.A220DisabledMonitorVariables;
}
