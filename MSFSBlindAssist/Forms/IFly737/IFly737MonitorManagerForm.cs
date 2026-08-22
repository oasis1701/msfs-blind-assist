using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.IFly737;

/// <summary>
/// Per-variable background-announcement manager for the iFly 737 MAX8 (Ctrl+M). Unticked keys
/// are written to UserSettings.IFlyDisabledMonitorVariables, which MainForm.OnSimVarUpdated
/// honours TWICE: the Suppressed-wrap covers the vars the iFly def announces from INSIDE
/// ProcessSimVarUpdate (annunciators, MCP mode lights, warning push lights, synthetic display
/// windows), and the generic gate covers the plain switch combos that announce on the generic
/// path. All behaviour lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class IFly737MonitorManagerForm : MonitorManagerFormBase
{
    public IFly737MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("iFly 737 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.IFlyDisabledMonitorVariables;
}
