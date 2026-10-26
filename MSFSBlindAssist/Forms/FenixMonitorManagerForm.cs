using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Per-variable background-announcement manager for the Fenix A320 (Ctrl+M). Lists every
/// auto-announced variable from the aircraft definition; unticked keys are written to
/// UserSettings.FenixDisabledMonitorVariables, which MainForm.OnSimVarUpdated consults before
/// speaking. All behaviour lives in <see cref="MonitorManagerFormBase"/>; the only thing this form
/// adds is one row per app-wide cabin announcement (<see cref="FenixCabinAnnouncements.Rows"/>), which
/// FenixCabinReadyMonitor reads the same list for.
/// </summary>
public sealed class FenixMonitorManagerForm : MonitorManagerFormBase
{
    public FenixMonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("Fenix Monitor Manager", BuildRows(variables)) { }

    /// <summary>The rows this dialog shows: the variable rows, then the cabin announcement rows, appended
    /// after the sort as the A380's synthetic ECAM row is. Internal so the tests measure the list the pilot sees.</summary>
    internal static List<MonitorRow> BuildRows(IReadOnlyDictionary<string, SimVarDefinition> variables)
    {
        var rows = MonitorRowBuilder.Build(variables);
        rows.AddRange(FenixCabinAnnouncements.Rows);
        return rows;
    }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.FenixDisabledMonitorVariables;
}
