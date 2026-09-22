using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.C172;

/// <summary>
/// Per-variable background-announcement manager for the stock Cessna 172 (Ctrl+M). Rows are every
/// Continuous + IsAnnounced variable minus those flagged ExcludeFromMonitorManager (the cache-only
/// feeds), so the list is the switches, the eight warnings, the magneto position, the COM actives
/// and the squawk. Unticked keys go to UserSettings.C172DisabledMonitorVariables, which
/// MainForm.OnSimVarUpdated honours at the generic gate and via the Suppressed wrap, and which the
/// definition's batch hook checks itself. All behaviour lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class Cessna172MonitorManagerForm : MonitorManagerFormBase
{
    public Cessna172MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("Cessna 172 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.C172DisabledMonitorVariables;
}
