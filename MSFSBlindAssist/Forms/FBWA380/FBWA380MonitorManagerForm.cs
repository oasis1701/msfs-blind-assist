using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Per-variable background-announcement manager for the FlyByWire A380X (Ctrl+M). Unticked
/// keys are written to UserSettings.A380DisabledMonitorVariables. All UI behaviour lives in
/// <see cref="MonitorManagerFormBase"/>; the only thing this form adds is the ECAM fold, and
/// that lives in <see cref="MonitorRowBuilder.BuildWithFold"/> so it can be unit-tested — a
/// private static on a Form cannot be.
/// </summary>
public sealed class FBWA380MonitorManagerForm : MonitorManagerFormBase
{
    /// <summary>Sentinel key that mutes all ECAM E/WD memo/warning call-outs.
    /// MainForm.AircraftSwitch.cs gates the Coherent E/WD scrape AND the FWS failure client on
    /// this key as well as the SimVar memo path, so it must stay public and keep this name.</summary>
    public const string EcamMemosKey = "FBWA380_ECAM_MEMOS";

    private const string EwdLinePrefix = "A32NX_EWD_LOWER_";
    private const string EcamMemosLabel = "ECAM E/WD call-outs";

    public FBWA380MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("A380 Monitor Manager",
               MonitorRowBuilder.BuildWithFold(variables, EwdLinePrefix, EcamMemosKey, EcamMemosLabel)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.A380DisabledMonitorVariables;
}
