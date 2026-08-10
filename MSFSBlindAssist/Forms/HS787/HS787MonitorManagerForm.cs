using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Per-variable background-announcement manager for the HorizonSim 787 (Ctrl+M). Unticked
/// keys are written to UserSettings.HS787DisabledMonitorVariables; MainForm.OnSimVarUpdated
/// skips the announcement for any key in that list. All behaviour lives in
/// <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class HS787MonitorManagerForm : MonitorManagerFormBase
{
    public HS787MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("787 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.HS787DisabledMonitorVariables;
}
