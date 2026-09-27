using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.MD11;

/// <summary>
/// Per-variable background-announcement manager for the TFDi MD-11 (Ctrl+M).
///
/// This matters more here than on any other aircraft in the app. The MD-11's six display units are
/// WASM-rendered and unreadable, so its 488 announcing annunciator lamps ARE the instrument panel —
/// a blind pilot has no other way to know a light came on. That is exactly why they all announce,
/// and equally why they need to be individually mutable: 488 lamps is a lot of voice in a busy
/// phase, and one chatty lamp can bury the one that matters. With ~530 rows the search box and the
/// Show filter the base class provides are not conveniences, they are how a row gets found at all.
///
/// Rows come from <see cref="MonitorRowBuilder"/> like every other aircraft's: every
/// UpdateFrequency.Continuous + IsAnnounced variable minus those flagged ExcludeFromMonitorManager
/// (the DC-bus voltage gate, the wordless lamps, the silent numeric read-outs — rows that would
/// mute nothing), sorted by spoken name. Unticked keys go to
/// UserSettings.Md11DisabledMonitorVariables, which MainForm.OnSimVarUpdated honours TWICE: via the
/// Suppressed wrap (the MD-11 speaks its lamps, flaps, COM frequencies and speedbrake from INSIDE
/// ProcessSimVarUpdate, where the generic gate never runs — the HS787 pattern) and via the generic
/// gate for everything on the normal path. All behaviour lives in <see cref="MonitorManagerFormBase"/>;
/// this used to be a hand-rolled form with no search field, which is why it was migrated.
/// </summary>
public sealed class Md11MonitorManagerForm : MonitorManagerFormBase
{
    public Md11MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("MD-11 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.Md11DisabledMonitorVariables;
}
