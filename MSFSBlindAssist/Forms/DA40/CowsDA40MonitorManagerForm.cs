using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.DA40;

/// <summary>
/// Per-variable background-announcement manager for the COWS DA40 (Ctrl+M).
///
/// Rows come from the shared builder, so the list is exactly the variables that are
/// Continuous AND IsAnnounced — on this aircraft that is every switch and selector, and
/// deliberately none of the numeric readouts. Volts, temperatures, RPM and brightness
/// change constantly; speaking them would bury the changes that matter. They are read
/// on demand from the panel status displays instead (Ctrl+3, F5).
///
/// Un-ticked keys are written to UserSettings.DA40DisabledMonitorVariables and honoured
/// by the generic gate in MainForm.OnSimVarUpdated. Unlike the HS787, iFly and PMDG
/// definitions, the DA40 announces nothing from inside ProcessSimVarUpdate, so it needs
/// no Step-2.5 Suppressed-wrap — the one generic gate covers everything.
/// </summary>
public sealed class CowsDA40MonitorManagerForm : MonitorManagerFormBase
{
    public CowsDA40MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("COWS DA40 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.DA40DisabledMonitorVariables;
}
