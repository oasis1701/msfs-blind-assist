using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.Aircraft.G1000;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The G1000's own warnings, read off the PFD: the CAS window and the FMA, spoken by the watcher
/// the COWS DA40 uses (<see cref="G1000CasWatcher"/>, C172-17).
///
/// THE C172 HAS NO ANNUNCIATOR LAMPS. Its warnings and cautions are the seven CAS messages the
/// aircraft's own panel.xml defines (read live from the PFD's <c>xmlConfig</c>, 2026-10-18): OIL
/// PRESSURE, LOW VOLTS and HIGH VOLTS (warnings), STBY BATT, LOW VACUUM, LOW FUEL L and LOW FUEL R
/// (cautions). The port computed four of them from SimVars with its own thresholds; reading the
/// screen speaks exactly what the G1000 shows, worded as it words it, and follows any change the
/// sim makes to them. Measured with the agent: the battery on with the engine stopped raised LOW
/// VOLTS, OIL PRESSURE and LOW VACUUM, the standby battery alone added STBY BATT.
///
/// THE FMA SPEAKS THE AUTOPILOT, as on the DA40 (DA40A-22): the mode rows are quiet, and the
/// master and the flight director keep only their OFF (<see cref="IsAutopilotModeSpokenByFma"/>).
/// The FMA also says what is ARMED (ALTS, GS, a NAV source), which no SimVar carries.
/// </summary>
public partial class CessnaC172G1000Definition : IG1000DisplayOwner
{
    /// <summary>The quiet period after a flight load or a reconnect, the DA40's (DA40-14).</summary>
    private readonly DA40LoadSettle _loadSettle = new();

    private G1000CasWatcher? _casWatcher;

    private G1000CasWatcher Cas => _casWatcher ??= new G1000CasWatcher("C172", () => _loadSettle.Settling);

    public void StartCasMonitor(ScreenReaderAnnouncer announcer) => Cas.Start(announcer);

    public void StopCasMonitor() => _casWatcher?.Stop();

    public void ProcessCasRows(List<string> rows) => Cas.ProcessRows(rows);

    public void SuspendCasMonitor(bool suspended, Func<string, Task<string>>? invokeWhileSuspended = null) =>
        Cas.Suspend(suspended, invokeWhileSuspended);

    /// <summary>Nothing here follows the G1000's unit settings: the C172's readouts are in the units they name.</summary>
    public void NoteDisplayUnits(IEnumerable<string> rows) { }

    /// <summary>The C172 has no radio settle announcer for the window to step around.</summary>
    public void MarkRadioTunedByWindow(string varKey) { }

    /// <summary>The autopilot mode rows the FMA speaks, with the master and the flight director.</summary>
    internal static readonly IReadOnlyList<string> FmaModeKeys = new[]
    {
        "C172_AP_MASTER_STATE", "C172G_AP_FD_STATE",
        "C172_AP_HDG_STATE", "C172_AP_NAV_STATE", "C172_AP_APR_STATE", "C172_AP_BC_STATE",
        "C172_AP_ALT_STATE", "C172_AP_VS_STATE", "C172G_AP_FLC_STATE",
    };

    /// <summary>
    /// Whether the FMA already says this change. Every mode is the FMA's; the master and the flight
    /// director keep their OFF, which the FMA deliberately does not say (a disconnect to "off" is
    /// its resting state). Pure so the suite can pin it.
    /// </summary>
    internal static bool IsAutopilotModeSpokenByFma(string varName, double value) => varName switch
    {
        "C172_AP_MASTER_STATE" or "C172G_AP_FD_STATE" => value >= 0.5,
        _ => FmaModeKeys.Contains(varName)
    };

    protected override bool IsAutopilotModeSpokenElsewhere(string varName, double value) =>
        IsAutopilotModeSpokenByFma(varName, value);

    /// <summary>
    /// The mode rows stay buttons that follow the aircraft in an open panel although their
    /// updates are handled here (<see cref="SimVarDefinition.RefreshControlWhenDefHandled"/>); a mode
    /// the FMA alone speaks has no Ctrl+M row, which would mute nothing (VAR-9). The master and the
    /// flight director keep theirs, for their OFF. Each stays IsAnnounced: that is what keeps it in
    /// the cache the nose wheel's read-back reads (DA40S-1).
    /// </summary>
    private static void QuietModesTheFmaSpeaks(Dictionary<string, SimVarDefinition> v)
    {
        foreach (string key in FmaModeKeys)
        {
            v[key].RefreshControlWhenDefHandled = _ => true;
            if (key is not ("C172_AP_MASTER_STATE" or "C172G_AP_FD_STATE"))
                v[key].ExcludeFromMonitorManager = true;
        }
    }

    /// <inheritdoc />
    public override void OnVariableCacheCleared()
    {
        base.OnVariableCacheCleared();
        _loadSettle.Begin();
    }

    /// <inheritdoc />
    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        _loadSettle.OnBatchDelivered(batchNum);
    }
}
