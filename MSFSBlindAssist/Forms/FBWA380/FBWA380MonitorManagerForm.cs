using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Per-variable background-announcement manager for the FlyByWire A380X (Ctrl+M). Unticked
/// keys are written to UserSettings.A380DisabledMonitorVariables. All UI behaviour lives in
/// <see cref="MonitorManagerFormBase"/>; the only thing this form adds is the ECAM fold.
/// </summary>
public sealed class FBWA380MonitorManagerForm : MonitorManagerFormBase
{
    /// <summary>Sentinel key that mutes all ECAM E/WD memo/warning call-outs.
    /// MainForm.AircraftSwitch.cs gates the Coherent E/WD scrape AND the FWS failure client on
    /// this key as well as the SimVar memo path, so it must stay public and keep this name.</summary>
    public const string EcamMemosKey = "FBWA380_ECAM_MEMOS";

    private const string EwdLinePrefix = "A32NX_EWD_LOWER_";

    public FBWA380MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("A380 Monitor Manager", BuildRows(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.A380DisabledMonitorVariables;

    /// <summary>
    /// The standard row build, minus the 20 E/WD line variables, plus one synthetic row that
    /// stands in for all of them. They are real announcements (so they are NOT excluded via
    /// ExcludeFromMonitorManager) but they are one logical feature, and 20 rows for a single
    /// on/off decision is noise.
    /// </summary>
    private static IReadOnlyList<MonitorRow> BuildRows(Dictionary<string, SimVarDefinition> variables)
    {
        bool anyEwd = variables.Keys.Any(k => k.StartsWith(EwdLinePrefix, StringComparison.Ordinal));

        var withoutEwd = variables
            .Where(kv => !kv.Key.StartsWith(EwdLinePrefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var rows = MonitorRowBuilder.Build(withoutEwd);
        if (anyEwd) rows.Add(new MonitorRow(EcamMemosKey, "ECAM E/WD call-outs"));
        return rows;
    }
}
