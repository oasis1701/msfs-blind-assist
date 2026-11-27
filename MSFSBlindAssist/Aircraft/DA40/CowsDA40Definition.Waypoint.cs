using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.G1000;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// "Report passing WAYPOINT", and the Ctrl+W, D and Shift+D readouts — the one IFR instruction this
/// aeroplane could not answer. The work is <see cref="G1000Waypoints"/>, shared with every aircraft
/// on the stock G1000 since the Cessna 172 took it up; it was born here, and its history is there.
/// What stays here is the DA40's: the keys, the Ctrl+M mute it checks, its PFD socket (the CAS
/// watcher's), and distances spoken in the units the pilot set on the G1000.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>The Ctrl+M row that silences the passing call. The readout key is never muted.</summary>
    private const string WaypointMonitorKey = "DA40_WAYPOINT_PASSING";

    private const string VnavTodDistanceKey = "DA40_VNAV_TOD_DIST";
    private const string VnavTodLegKey = "DA40_VNAV_TOD_LEG";

    private G1000Waypoints? _waypoints;

    private G1000Waypoints Waypoints => _waypoints ??= new G1000Waypoints("DA40", InvokeOnCasClientAsync,
        () => Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(WaypointMonitorKey),
        DistanceText, VnavTodDistanceKey, VnavTodLegKey);

    /// <summary>The passing call's Ctrl+M row (<see cref="G1000Waypoints.AddMonitorRow"/>).</summary>
    private static void AddWaypointMonitorRow(Dictionary<string, SimVarDefinition> v) =>
        G1000Waypoints.AddMonitorRow(v, WaypointMonitorKey);

    /// <summary>The G1000's VNAV output, for Shift+D (<see cref="G1000Waypoints.AddVnavReadouts"/>).</summary>
    private static void AddVnavReadouts(Dictionary<string, SimVarDefinition> v) =>
        G1000Waypoints.AddVnavReadouts(v, VnavTodDistanceKey, VnavTodLegKey);

    public void AttachWaypointSequencer(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) =>
        Waypoints.Attach(simConnect, announcer);

    private void StopWaypointSequencer() => _waypoints?.Stop();

    private string ComposeWaypointReadout() => Waypoints.ComposeWaypointReadout();

    private string ComposeDestinationReadout() => Waypoints.ComposeDestinationReadout();

    private string ComposeTopOfDescentReadout(SimConnectManager simConnect) =>
        Waypoints.ComposeTopOfDescentReadout(simConnect);

    /// <summary>Distance in whatever the pilot set on the G1000, never a fixed unit.</summary>
    private string DistanceText(double nm)
    {
        if (DisplayUnitDistance == "metric")
        {
            double km = nm * 1.852;
            return km < 10 ? FormattableString.Invariant($"{km:0.0} kilometres") : $"{km:0} kilometres";
        }
        return G1000Waypoints.NauticalMilesText(nm);
    }
}
