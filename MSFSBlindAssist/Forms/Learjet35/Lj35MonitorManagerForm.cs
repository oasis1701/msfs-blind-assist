using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Learjet35;

/// <summary>
/// Per-variable background-announcement manager for the Flysimware Learjet 35A (Ctrl+M).
///
/// Rows come from the shared builder, so the list is exactly the variables that are
/// Continuous AND IsAnnounced — on this aircraft that is every switch, selector and derived
/// annunciator lamp, and deliberately none of the numeric readouts. N1, ITT, volts and cabin
/// altitude change constantly; speaking them would bury the changes that matter. They are read
/// on demand from the panel status displays (Ctrl+3, F5) and the readout hotkeys.
///
/// Un-ticked keys are written to UserSettings.LJ35DisabledMonitorVariables and honoured by
/// BOTH gates in MainForm.OnSimVarUpdated: the generic gate for switches on the generic path,
/// and the Suppressed-wrap for the lamps the definition announces from INSIDE
/// ProcessSimVarUpdate (the HS787/iFly pattern).
/// </summary>
public sealed class Lj35MonitorManagerForm : MonitorManagerFormBase
{
    public Lj35MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("Learjet 35A Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.LJ35DisabledMonitorVariables;
}
