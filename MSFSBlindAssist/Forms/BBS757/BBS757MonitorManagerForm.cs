using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// Per-variable background-announcement manager for the BlueBird 757-200 (Ctrl+M).
///
/// Rows come from <see cref="Aircraft.BBS757Definition.MonitorRows"/>: <see cref="MonitorRowBuilder"/>'s,
/// like every other aircraft's (every Continuous + IsAnnounced variable minus those flagged
/// ExcludeFromMonitorManager, so the announced status rows (APU, master warning and caution), the
/// flight-control call-outs (flaps, gear, speedbrake, parking brake), the fourteen MCP lights and the
/// shared ones (ground state, trim; no glideslope: the 757 does not use the sim's NAV radios)), then
/// the rows of speech that is no variable's.
/// Unticked keys go to UserSettings.BBS757DisabledMonitorVariables, which MainForm honors at
/// the generic gate and through the Suppressed wrap (Services/DefAnnounceMuteSets). All behavior
/// lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class BBS757MonitorManagerForm : MonitorManagerFormBase
{
    public BBS757MonitorManagerForm(IReadOnlyList<MonitorRow> rows)
        : base("757 Monitor Manager", rows) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.BBS757DisabledMonitorVariables;
}
