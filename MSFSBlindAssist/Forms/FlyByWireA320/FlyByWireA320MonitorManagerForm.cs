using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FlyByWireA320;

/// <summary>
/// Per-variable background-announcement manager for the FlyByWire A32NX (Ctrl+M). Unticked
/// keys are written to UserSettings.A32NXDisabledMonitorVariables; MainForm.OnSimVarUpdated
/// skips the announcement for any key in that list (when AircraftCode == "A320"). All
/// behaviour lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class FlyByWireA320MonitorManagerForm : MonitorManagerFormBase
{
    public FlyByWireA320MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("A320 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.A32NXDisabledMonitorVariables;
}
