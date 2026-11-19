using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Per-variable background-announcement manager for the Fenix A320 (Ctrl+M). Lists every
/// auto-announced variable from the aircraft definition; unticked keys are written to
/// UserSettings.FenixDisabledMonitorVariables, which MainForm.OnSimVarUpdated consults before
/// speaking. All behavior lives in <see cref="MonitorManagerFormBase"/>; the only things this form
/// adds are two sets of rows that no variable owns: one per app-wide cabin announcement
/// (<see cref="FenixCabinAnnouncements.Rows"/>), which FenixCabinReadyMonitor reads the same list for, and one per
/// display channel announcement (<see cref="FenixChannelAnnouncementRows.Rows"/>: the FMA changes, the E/WD warnings,
/// cautions and memos, the DCDU messages and the ND messages), which the channel's announcers read the same list for
/// (<see cref="FenixChannelAnnouncementRows.IsMuted"/>).
/// </summary>
public sealed class FenixMonitorManagerForm : MonitorManagerFormBase
{
    public FenixMonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("Fenix Monitor Manager", BuildRows(variables)) { }

    /// <summary>The rows this dialog shows: the variable rows, then the cabin announcement rows, then the display
    /// channel announcement rows, appended after the sort as the A380's synthetic ECAM row is. Internal so the tests
    /// measure the list the pilot sees.</summary>
    internal static List<MonitorRow> BuildRows(IReadOnlyDictionary<string, SimVarDefinition> variables)
    {
        var rows = MonitorRowBuilder.Build(variables);
        rows.AddRange(FenixCabinAnnouncements.Rows);
        rows.AddRange(FenixChannelAnnouncementRows.Rows);
        return rows;
    }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.FenixDisabledMonitorVariables;
}
