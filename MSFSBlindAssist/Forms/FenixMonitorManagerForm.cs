using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Per-variable background-announcement manager for the Fenix A320 (Ctrl+M). Lists every
/// auto-announced variable from the aircraft definition; unticked keys are written to
/// UserSettings.FenixDisabledMonitorVariables, which MainForm.OnSimVarUpdated consults before
/// speaking. All behaviour lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class FenixMonitorManagerForm : MonitorManagerFormBase
{
    public FenixMonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("Fenix Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.FenixDisabledMonitorVariables;
}
