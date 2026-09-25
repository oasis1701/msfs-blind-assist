using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// "Report passing WAYPOINT" — the cue a blind pilot cannot get from a jet whose flight plan
/// sequences on a map. The GNS 530 writes the stock GPS SimVars through the Working Title
/// Garmin SDK's GpsSynchronizer (the same code the DA40's G1000 runs), so
/// <see cref="GpsWaypointSequencer"/> recognises a real sequence — the fix we were flying TO
/// has become the fix we are flying FROM — and nothing else: a Direct-To, a plan edit or a
/// procedure load all change the TO-waypoint and none of them is a passing.
///
/// MSFSBA announces the passing; it does not make the radio call. One Ctrl+M row silences it.
/// ⚠️ On a SID or STAR the unit leaves the idents blank (measured on the G1000; the GNS runs
/// the same synchronizer), so the call can only name enroute fixes today.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    /// <summary>
    /// The Ctrl+M row, and nothing else. A monitor row needs a real variable behind it, so it
    /// rides GPS IS ACTIVE FLIGHT PLAN — cheap, and ⚠️ a SimVar NAME no other Learjet key
    /// carries: the continuous batch sorts by name, so a duplicate name shifts every later
    /// variable's slot. It is silenced in ProcessSimVarUpdate (SilentCachedReadouts); the
    /// call itself speaks from the SimConnect frame below, which checks the mute itself.
    /// </summary>
    private const string WaypointPassingKey = "LJ35_WAYPOINT_PASSING";

    private string? _wptLastNextId;

    private static Dictionary<string, SimVarDefinition> BuildWaypointVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>
        {
            [WaypointPassingKey] = new SimVarDefinition
            {
                Name = "GPS IS ACTIVE FLIGHT PLAN",
                DisplayName = "Waypoint Passing Call",
                Type = SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsReadOnlyStatus = true,
                HelpText = "Untick to stop the call announcing when the GNS sequences a waypoint."
            }
        };
        SilentCachedReadouts.Add(WaypointPassingKey);
        return v;
    }

    /// <summary>
    /// One frame of the standing GPS waypoint definition, forwarded by MainForm for every
    /// aircraft. ⚠️ This speaks OUTSIDE the wrap MainForm puts around ProcessSimVarUpdate, so
    /// it checks the Ctrl+M mute itself — the same rule the lamps follow.
    /// </summary>
    public override void OnGpsWaypointReceived(SimConnectManager.GpsWaypointData data, ScreenReaderAnnouncer announcer)
    {
        var reading = GpsWaypointSequencer.Read(data, _wptLastNextId);
        _wptLastNextId = reading.NextId;
        if (reading.PassedId.Length == 0) return;
        if (Settings.SettingsManager.Current.LJ35DisabledMonitorVariablesSet.Contains(WaypointPassingKey)) return;
        // Immediate, not queued: a controller expects the report within seconds.
        announcer.AnnounceImmediate(GpsWaypointSequencer.ComposePassing(reading));
    }

    /// <summary>Reconnect or aircraft switch: the next frame is a baseline again, never a passing.</summary>
    private void ResetWaypointBaseline() => _wptLastNextId = null;
}
