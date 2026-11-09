using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A300;

/// <summary>
/// Per-variable background-announcement manager for the iniBuilds A300 (Ctrl+M).
///
/// Rows come from <see cref="MonitorRowBuilder"/> like every other aircraft's: every Continuous +
/// IsAnnounced variable minus those flagged ExcludeFromMonitorManager, so the A300 lists its master
/// warning and caution, its announced levers (flaps, ground spoilers, gear lever, parking brake),
/// every spoken light, the FMA columns, the take-off speeds, the altitude window, the autopilot and
/// autothrottle call-outs and the shared base variables; the silently consumed switch positions are
/// excluded because a checkbox there would mute nothing. Unticked keys go to UserSettings.A300DisabledMonitorVariables,
/// honored by MainForm (generic gate and the ProcessSimVarUpdate wrap). All behavior lives in
/// <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class A300MonitorManagerForm : MonitorManagerFormBase
{
    public A300MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("A300 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.A300DisabledMonitorVariables;
}
