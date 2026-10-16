using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.C172;

/// <summary>
/// Per-variable background-announcement manager for the Cessna 172, Classic and G1000 (Ctrl+M).
///
/// Rows come from <see cref="MonitorRowBuilder"/>: every Continuous + IsAnnounced variable minus
/// those flagged ExcludeFromMonitorManager. On the C172 that is the autopilot modes, the stall
/// warning and the variables behind the low fuel, oil pressure, vacuum and RPM warnings; oil
/// temperature, EGT, fuel flow and bus voltage are read silently for the status displays, so they
/// carry ExcludeFromMonitorManager and have no row (VAR-9).
///
/// Un-ticked keys go to UserSettings.C172DisabledMonitorVariables, shared by both variants, which
/// MainForm.OnSimVarUpdated honours at the generic gate and, because the warnings are spoken from
/// INSIDE ProcessSimVarUpdate, through the Suppressed wrap (Services/DefAnnounceMuteSets, VAR-8).
/// </summary>
public sealed class CessnaC172MonitorManagerForm : MonitorManagerFormBase
{
    public CessnaC172MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("Cessna 172 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.C172DisabledMonitorVariables;
}
