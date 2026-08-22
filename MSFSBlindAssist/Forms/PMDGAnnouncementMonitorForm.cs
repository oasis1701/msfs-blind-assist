using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Per-variable announcement toggle for PMDG aircraft (Ctrl+M). Unticked keys go into
/// UserSettings.PMDGDisabledMonitorVariables; MainForm consults that list in the
/// continuous-monitoring branch and silently skips the announcement — state changes still
/// update internal caches, only the speech is suppressed. Re-ticking resumes the
/// announcement immediately, no restart required.
///
/// This form used to carry its own search box; that (plus the All/Muted/Unmuted filter it
/// never had) now lives in <see cref="MonitorManagerFormBase"/>, shared with the other five
/// aircraft dialogs.
/// </summary>
public sealed class PMDGAnnouncementMonitorForm : MonitorManagerFormBase
{
    public PMDGAnnouncementMonitorForm(Dictionary<string, SimVarDefinition> variables)
        : base("PMDG Announcement Monitor", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.PMDGDisabledMonitorVariables;
}
