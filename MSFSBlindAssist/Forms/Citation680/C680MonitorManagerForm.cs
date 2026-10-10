using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Citation680;

/// <summary>
/// Ctrl+M for the Sovereign+: every Continuous + IsAnnounced switch, selector and CAS class row.
/// Numeric readouts are deliberately absent (they change constantly); they are read from the
/// panel scans and the readout hotkeys. Un-ticked keys go to UserSettings.C680DisabledMonitorVariables
/// and are honoured at BOTH MainForm gates.
/// </summary>
public sealed class C680MonitorManagerForm : MonitorManagerFormBase
{
    public C680MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("Citation Sovereign+ Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables => SettingsManager.Current.C680DisabledMonitorVariables;
}
