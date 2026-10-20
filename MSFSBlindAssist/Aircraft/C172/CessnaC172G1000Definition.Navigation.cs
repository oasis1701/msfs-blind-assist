using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.G1000;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The G1000's navigation and display readouts, which said nothing on the C172 until 2026-10-18:
/// Ctrl+W (the waypoint flown to), D (the destination), Shift+D (top of descent) and the automatic
/// waypoint-passing call, all <see cref="G1000Waypoints"/>, the code the COWS DA40 (#242) flies;
/// Alt+E, the CAS messages standing now (<see cref="G1000CasWatcher.DescribeCurrentMessages"/>);
/// and Alt+I, the standby instruments.
///
/// MEASURED ON THE C172 (2026-10-18, parked, no flight plan): the GPS waypoint frame registers for
/// every aircraft (SimConnectManager), its SimVars read empty with no plan, and the G1000's VNAV
/// L:vars exist here as on the DA40 (`WTAP_VNav_TOD_Leg_Index` -1, no descent).
/// </summary>
public partial class CessnaC172G1000Definition
{
    /// <summary>The passing call's Ctrl+M row; the readout keys are never muted.</summary>
    internal const string WaypointPassingKey = "C172G_WAYPOINT_PASSING";
    internal const string VnavTodDistanceKey = "C172G_VNAV_TOD_DIST";
    internal const string VnavTodLegKey = "C172G_VNAV_TOD_LEG";

    private G1000Waypoints? _waypoints;

    /// <summary>
    /// The shared waypoint code, on the CAS watcher's PFD socket. Distances in nautical miles: the
    /// C172 reads none of the G1000's unit settings (<see cref="NoteDisplayUnits"/>).
    /// </summary>
    private G1000Waypoints Waypoints => _waypoints ??= new G1000Waypoints("C172", Cas.InvokeAsync,
        () => Settings.SettingsManager.Current.C172DisabledMonitorVariablesSet.Contains(WaypointPassingKey),
        G1000Waypoints.NauticalMilesText, VnavTodDistanceKey, VnavTodLegKey);

    public void AttachWaypointSequencer(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) =>
        Waypoints.Attach(simConnect, announcer);

    /// <summary>
    /// The passing call's Ctrl+M row and the two VNAV readouts (silent, <see cref="IsNavigationReadout"/>),
    /// and the standby instruments, read when Alt+I asks.
    ///
    /// THE STANDBY INSTRUMENTS each read the SimVar of their own gauge: the compass
    /// `WISKEY COMPASS INDICATION DEGREES` (133.6 parked, the heading 133.6), the standby attitude
    /// indicator `ATTITUDE INDICATOR PITCH DEGREES` and `BANK DEGREES`. The airspeed and altitude come
    /// from the same pitot-static system as the G1000's: every index of `AIRSPEED INDICATED` and
    /// `INDICATED ALTITUDE` read the same. OnRequest, under keys of their own, so no name is batched
    /// twice (VAR-7).
    /// </summary>
    private static void AddNavigationVariables(Dictionary<string, SimVarDefinition> v)
    {
        G1000Waypoints.AddMonitorRow(v, WaypointPassingKey);
        G1000Waypoints.AddVnavReadouts(v, VnavTodDistanceKey, VnavTodLegKey);

        v["C172G_STBY_ALTITUDE"] = OnRequestReading("INDICATED ALTITUDE", "Standby Altitude", "feet");
        v["C172G_STBY_COMPASS"] = OnRequestReading("WISKEY COMPASS INDICATION DEGREES", "Compass", "degrees");
        v["C172G_STBY_PITCH"] = OnRequestReading("ATTITUDE INDICATOR PITCH DEGREES", "Standby Attitude Pitch", "degrees");
        v["C172G_STBY_BANK"] = OnRequestReading("ATTITUDE INDICATOR BANK DEGREES", "Standby Attitude Bank", "degrees");
    }

    private static SimVarDefinition OnRequestReading(string simVar, string displayName, string units) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = units,
        UpdateFrequency = UpdateFrequency.OnRequest
    };

    /// <summary>
    /// The three rows the waypoint code reads from the cache and nobody speaks: the passing call is
    /// spoken from the GPS frame, which checks its own Ctrl+M row, and the VNAV readouts answer
    /// Shift+D. Swallowed in ProcessSimVarUpdate, or a flight plan activating would say "Waypoint
    /// Passing Call: 1".
    /// </summary>
    internal static bool IsNavigationReadout(string varName) =>
        varName is WaypointPassingKey or VnavTodDistanceKey or VnavTodLegKey;

    /// <summary>Output Ctrl+W, D, Shift+D, Alt+E and Alt+I; false for any other action.</summary>
    private bool HandleNavigationHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        switch (action)
        {
            case HotkeyAction.ReadNDWaypoint:
                announcer.AnnounceImmediate(Waypoints.ComposeWaypointReadout());
                return true;

            case HotkeyAction.ReadDistanceToDest:
                announcer.AnnounceImmediate(Waypoints.ComposeDestinationReadout());
                return true;

            case HotkeyAction.ReadDistanceToTOD:
                announcer.AnnounceImmediate(Waypoints.ComposeTopOfDescentReadout(simConnect));
                return true;

            // The E/WD key: the G1000's CAS window is this aircraft's.
            case HotkeyAction.ReadDisplayUpperECAM:
                announcer.AnnounceImmediate(Cas.DescribeCurrentMessages());
                return true;

            // The ISIS key: the C172 G1000 carries a standby airspeed indicator, altimeter, attitude
            // indicator and compass, the instruments left when the G1000 is not.
            case HotkeyAction.ReadDisplayISIS:
                _ = SayStandbyInstrumentsAsync(simConnect, announcer);
                return true;
        }
        return false;
    }

    /// <summary>Output Alt+I, each instrument read fresh (C172-19). On the UI thread.</summary>
    private static async Task SayStandbyInstrumentsAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        double? airspeed = await simConnect.ReadFreshAsync("C172_AIRSPEED", 1500);
        double? altitude = await simConnect.ReadFreshAsync("C172G_STBY_ALTITUDE", 1500);
        double? compass = await simConnect.ReadFreshAsync("C172G_STBY_COMPASS", 1500);
        double? pitch = await simConnect.ReadFreshAsync("C172G_STBY_PITCH", 1500);
        double? bank = await simConnect.ReadFreshAsync("C172G_STBY_BANK", 1500);
        double? vacuum = simConnect.GetCachedVariableValue("C172_VACUUM");
        announcer.AnnounceImmediate(DescribeStandbyInstruments(airspeed, altitude, compass, pitch, bank, vacuum));
    }

    /// <summary>
    /// What Alt+I says: "Standby airspeed 65 knots. Altitude 3500 feet. Compass 134 degrees. Attitude
    /// 2 degrees nose up, wings level. Vacuum 5.0 inches, green." Every number with its unit; a reading
    /// that did not come leaves its part out. The vacuum is the engine page's reading, said here
    /// because the real C172's standby attitude indicator runs on it (whether the sim's follows a
    /// failed vacuum is not measured).
    ///
    /// SIGNS: the attitude indicator reads as `PLANE PITCH DEGREES` and `PLANE BANK DEGREES` do (both
    /// equal parked): pitch NEGATIVE NOSE UP, as SimConnectManager's pitch readout negates it, and bank
    /// POSITIVE LEFT, as its bank readout negates it ("so right bank = positive", VG-7). The bank side
    /// is taken from that convention, not yet flown on the C172. Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeStandbyInstruments(double? airspeedKt, double? altitudeFt, double? compassDeg,
        double? pitchRaw, double? bankRaw, double? vacuumInHg)
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        if (airspeedKt is double ias) parts.Add($"Standby airspeed {Math.Round(ias).ToString("0", inv)} knots");
        if (altitudeFt is double alt) parts.Add($"Altitude {Math.Round(alt).ToString("0", inv)} feet");
        if (compassDeg is double c) parts.Add($"Compass {HeadingDigits(c)} degrees");
        if (pitchRaw is double p && bankRaw is double b) parts.Add("Attitude " + DescribeAttitude(p, b));
        if (vacuumInHg is double vac) parts.Add("Vacuum " + CessnaC172Gauges.DescribeVacuum(vac));
        return parts.Count == 0 ? "Standby instruments not available" : string.Join(". ", parts) + ".";
    }

    /// <summary>"2 degrees nose up, wings level", "nose level, 15 degrees bank right", from the raw SimVars.</summary>
    private static string DescribeAttitude(double pitchRaw, double bankRaw)
    {
        var inv = CultureInfo.InvariantCulture;
        double noseUp = -pitchRaw, right = -bankRaw;
        int pitchDeg = (int)Math.Round(Math.Abs(noseUp)), bankDeg = (int)Math.Round(Math.Abs(right));
        string pitch = pitchDeg == 0
            ? "nose level"
            : $"{pitchDeg.ToString(inv)} {(pitchDeg == 1 ? "degree" : "degrees")} nose {(noseUp > 0 ? "up" : "down")}";
        string bank = bankDeg == 0
            ? "wings level"
            : $"{bankDeg.ToString(inv)} {(bankDeg == 1 ? "degree" : "degrees")} bank {(right > 0 ? "right" : "left")}";
        return pitch + ", " + bank;
    }
}
