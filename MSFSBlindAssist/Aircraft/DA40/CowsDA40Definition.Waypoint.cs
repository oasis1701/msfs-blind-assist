using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// "Report passing WAYPOINT" — the one IFR instruction this aeroplane could not answer.
///
/// ⚠️ CORRECTION TO SOMETHING THIS PROJECT WROTE DOWN AS TRUE: the docs said Ctrl+W answered
/// the waypoint on demand and that only the automatic call was missing. It did not. Ctrl+W maps
/// to <c>HotkeyAction.ReadNDWaypoint</c>, which the DA40 never handled, and the shared service
/// behind it reads <c>A32NX_EFIS_L_TO_WPT_*</c> — FlyByWire variables that do not exist on a
/// Diamond. The key did nothing at all, and the audit reported it as working. Both halves were
/// missing, not one.
///
/// WHAT FILLS THEM is the aeroplane's own navigator. The Working Title G1000 carries a
/// <c>GpsSynchronizer</c> that writes the stock GPS SimVars from its active flight plan — read
/// live out of the running instrument rather than taken from documentation — so the TO-waypoint,
/// the waypoint just passed, the distance, the bearing and the time are all published already.
/// Nothing needed to be inferred and nothing needed a Coherent socket.
///
/// ⚠️ MSFSBA ANNOUNCES THE PASSING; IT DOES NOT MAKE THE CALL. Which report to make, and when,
/// is the pilot's job — this only removes the reason they could not know. See
/// <see cref="GpsWaypointSequencer"/> for why "the ident changed" is not the test.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>
    /// The Ctrl+M row, and nothing else. A monitor row needs a real variable behind it, so this
    /// one rides GPS IS ACTIVE FLIGHT PLAN — a genuine reading (is there a plan at all), cheap,
    /// and ⚠️ NOT a SimVar name any other DA40 key already carries: the continuous batch sorts
    /// by SimVar NAME, so a second key on a name already in the batch shifts every later
    /// variable's slot and corrupts unrelated readouts. It is silenced in ProcessSimVarUpdate
    /// via SilentCachedReadouts; the passing call is spoken from the SimConnect callback below,
    /// which checks this key's mute itself.
    /// </summary>
    private static void AddWaypointMonitorRow(Dictionary<string, SimVarDefinition> v)
    {
        v[WaypointMonitorKey] = new SimVarDefinition
        {
            Name = "GPS IS ACTIVE FLIGHT PLAN",
            DisplayName = "Waypoint Passing Call",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true
        };
    }

    /// <summary>
    /// The G1000's own VNAV output, for the top-of-descent readout.
    ///
    /// ⚠️ BOTH NAMES WERE READ OUT OF THE RUNNING INSTRUMENT'S PUBLISHER TABLE, never guessed —
    /// `vNavPublisher` maps `vnav_tod_distance` to `L:WTAP_VNav_Distance_To_TOD` and
    /// `vnav_path_available` to `L:WTAP_VNav_Path_Available`. The flag is not optional: without
    /// a computed path there IS no top of descent, and the distance reads 0 in that case exactly
    /// as it does when the aeroplane is sitting on top of one.
    ///
    /// ⚠️ THE DISTANCE UNIT IS METRES BY THE WORKING TITLE CONVENTION AND IS NOT YET VERIFIED
    /// IN FLIGHT. If it turns out to be nautical miles the readout will be wrong by a factor of
    /// 1852, which is loud rather than subtle — but do not treat it as confirmed until a real
    /// descent has been flown against it.
    ///
    /// Registered `Units = "number"` because they are L:VARS: a converting unit makes SimConnect
    /// convert from a base unit the variable does not have. Announced only to reach the batch
    /// cache, and silenced in ProcessSimVarUpdate.
    /// </summary>
    private static void AddVnavReadouts(Dictionary<string, SimVarDefinition> v)
    {
        Add("DA40_VNAV_TOD_DIST", "WTAP_VNav_Distance_To_TOD", "Distance to Top of Descent");
        // ⚠️ WTAP_VNav_Path_Available DOES NOT EXIST, AND Shift+D COULD NEVER SAY ANYTHING
        // ELSE. The Working Title autopilot publishes exactly TWO VNAV variables - grepped
        // over the whole installed package, binary files included:
        //
        //     WTAP_VNav_Distance_To_TOD
        //     WTAP_VNav_TOD_Leg_Index
        //
        // and no availability flag of any kind. A missing L:var READS AS ZERO rather than
        // failing, so the readout answered "No vertical path computed." every single time
        // and looked like an aeroplane that never computes one.
        //
        // The leg index IS the flag: it is -1 when there is no top of descent and the index
        // of the leg carrying it otherwise (measured live: -1 with no descent planned).
        Add("DA40_VNAV_TOD_LEG", "WTAP_VNav_TOD_Leg_Index", "Top of Descent Leg");

        void Add(string key, string lvar, string label)
        {
            v[key] = new SimVarDefinition
            {
                Name = lvar,
                DisplayName = label,
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true,
                RenderAsReadOnlyStatus = true
            };
        }
    }

    /// <summary>
    /// The active leg's name and the one before it, read from the FLIGHT PLAN rather than from
    /// the stock GPS SimVars.
    ///
    /// ⚠️ THE SIMVAR IDENTS ARE EMPTY ON EVERY PROCEDURE, WHICH IS MOST OF IFR FLYING.
    /// `GPS WP NEXT ID` and `GPS WP PREV ID` are written by the G1000's GpsSynchronizer from
    /// `plan.getLeg(plan.activeLateralLeg).name`, off a plan-change event that does NOT fire as
    /// a procedure sequences. Measured live on a hand-built ANUT1D departure: both SimVars were
    /// empty strings while the flight plan's own getLeg(5).name returned "BI583", and the
    /// aeroplane passed BI551 and BI582 in total silence - which is exactly the call this
    /// feature exists to make.
    ///
    /// Distance, bearing and time were correct throughout, because those ride continuous LNAV
    /// events. Only the IDENT is missing, so only the ident is fetched here; everything else
    /// still comes from the standing SimConnect frame and needs no socket at all.
    ///
    /// ⚠️ Best-effort by design. The socket belongs to the CAS monitor and the PFD window can
    /// take it, so a failed or absent read leaves the names as they were rather than clearing
    /// them - a momentarily stale ident is worth far more than a passing call that vanishes
    /// whenever the pilot opens a display.
    /// </summary>
    private string _wptLegNext = "";
    private string _wptLegPrev = "";
    private DateTime _wptLegAskedAt = DateTime.MinValue;
    private DateTime _wptLegReadAt = DateTime.MinValue;
    private int _wptLegBusy;

    /// <summary>How often to ask the display for the leg names. They change on sequencing only.</summary>
    private const int LegPollMs = 1500;

    /// <summary>
    /// How long a plan-sourced name may outrank the live SimVar.
    ///
    /// ⚠️ WITHOUT THIS BOUND Ctrl+W ANSWERED THE OLD WAYPOINT FOR THE REST OF THE SESSION.
    /// Reported from the cockpit on the XLS: assign a Direct-To and the readout keeps naming
    /// the previous waypoint "unless you restarted the MSFSBA app" — and a restart is exactly
    /// what clears these two fields, which is what pinned the cause here.
    ///
    /// Three things had to line up and all three were ours. The plan name beat the SimVar
    /// UNCONDITIONALLY; it was written only on a SUCCESSFUL read, deliberately, so that a
    /// momentarily unavailable socket would not blank the passing call; and nothing ever
    /// expired it. So any condition that stops the read succeeding freezes the answer for
    /// good — and the obvious one is routine: the leg names are fetched on the CAS monitor's
    /// PFD socket, Coherent GT allows exactly ONE inspector per view, and opening the PFD
    /// display window takes it. The pilot then has a frozen ident and no indication of it.
    ///
    /// A Direct-To is the case where the fallback is known-good: it DOES populate the ident
    /// SimVars (unlike a SID or STAR, which is why the plan path exists at all), so letting
    /// a stale plan name lapse gives the correct new waypoint rather than silence.
    ///
    /// Sized at three poll intervals, so an answer survives a couple of missed reads — the
    /// best-effort behaviour the passing call needs — without ever outliving the flight.
    /// </summary>
    private const int LegFreshMs = LegPollMs * 3;

    /// <summary>
    /// The plan-sourced names, or null once they are too old to outrank the live SimVar.
    /// Null is the whole point: <see cref="GpsWaypointSequencer.Read"/> treats an absent plan
    /// name as "fall back", which is the behaviour a fresh install has and the one a restart
    /// used to be needed to get back.
    /// </summary>
    private bool LegNamesAreFresh =>
        _wptLegReadAt != DateTime.MinValue &&
        (DateTime.UtcNow - _wptLegReadAt).TotalMilliseconds <= LegFreshMs;

    private void RequestActiveLegNames(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _wptLegAskedAt).TotalMilliseconds < LegPollMs) return;
        if (System.Threading.Interlocked.Exchange(ref _wptLegBusy, 1) == 1) return;
        _wptLegAskedAt = DateTime.UtcNow;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                string r = await InvokeOnCasClientAsync(
                    // ⚠️ THE FULL GLOBAL, NEVER A BARE "A". The agent keeps its namespace in a
                    // closure and publishes only window.__MSFSBA_DA40G1000 - so "A.M.x()" is a
                    // ReferenceError that a catch swallows, leaving the feature silently dead.
                    // That is exactly how this shipped: the leg names never arrived and every
                    // readout said "Unnamed leg" through a whole flight.
                    "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.M.activeLeg()");
                if (!string.IsNullOrEmpty(r) && r.IndexOf('|') >= 0)
                {
                    var parts = r.Split('|');
                    _wptLegPrev = parts[0].Trim();
                    _wptLegNext = parts.Length > 1 ? parts[1].Trim() : "";
                    // ⚠️ STAMPED ONLY HERE, ON A READ THAT ACTUALLY ANSWERED. A stamp moved
                    // when the request was ISSUED would call a permanently failing socket
                    // fresh and restore the very bug this closes.
                    _wptLegReadAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex) { Utils.Logging.Log.Debug("DA40", $"Active leg read: {ex.Message}"); }
            finally { System.Threading.Volatile.Write(ref _wptLegBusy, 0); }
        });
    }

    private ScreenReaderAnnouncer? _wptAnnouncer;
    private SimConnectManager? _wptSimConnect;
    private string? _wptLastNextId;

    /// <summary>The Ctrl+M row that silences the passing call. The readout key is never muted.</summary>
    private const string WaypointMonitorKey = "DA40_WAYPOINT_PASSING";

    public void AttachWaypointSequencer(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        StopWaypointSequencer();
        _wptSimConnect = simConnect;
        _wptAnnouncer = announcer;
        simConnect.GpsWaypointReceived += OnGpsWaypoint;
    }

    private void StopWaypointSequencer()
    {
        if (_wptSimConnect != null) _wptSimConnect.GpsWaypointReceived -= OnGpsWaypoint;
        _wptSimConnect = null;
        _wptAnnouncer = null;
        _wptLastNextId = null;
        // Restarting the app was the pilot's only workaround for a frozen ident; an aircraft
        // switch or a reconnect must clear it too, and leave nothing to outrank the SimVar.
        _wptLegNext = "";
        _wptLegPrev = "";
        _wptLegReadAt = DateTime.MinValue;
    }

    private void OnGpsWaypoint(object? sender, SimConnectManager.GpsWaypointData data)
    {
        try
        {
            RequestActiveLegNames();

            var reading = GpsWaypointSequencer.Read(
                data, _wptLastNextId, FreshLegNext, FreshLegPrev);
            _wptLastNextId = reading.NextId;

            if (reading.PassedId.Length == 0) return;

            // ⚠️ This speaks from a SimConnect callback, OUTSIDE the wrap MainForm puts around
            // ProcessSimVarUpdate, so it must check the mute itself — the same rule the baro
            // settle timer and the A32NX armed-altitude flush follow.
            if (Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(WaypointMonitorKey)) return;

            // Immediate, not queued: a controller expects the report within seconds, and a
            // passing that arrives behind a queue of scan chatter is a passing the pilot
            // reports late. Same reasoning as the settled altimeter value.
            _wptAnnouncer?.AnnounceImmediate(GpsWaypointSequencer.ComposePassing(reading, DistanceText));
        }
        catch (Exception ex)
        {
            Utils.Logging.Log.Debug("DA40", $"Waypoint sequencer: {ex.Message}");
        }
    }

    /// <summary>
    /// Answers Ctrl+W from the LAST DELIVERED frame, never a fresh request — the definition
    /// carries a standing once-a-second subscription and re-issuing its id as a one-shot would
    /// cancel it outright, which is the trap that froze the A380's managed-altitude derivation.
    /// </summary>
    private string FreshLegNext => LegNamesAreFresh ? _wptLegNext : null!;
    private string FreshLegPrev => LegNamesAreFresh ? _wptLegPrev : null!;

    private string ComposeWaypointReadout()
    {
        // ⚠️ ASK BEFORE ANSWERING. The leg names used to be refreshed from exactly ONE place,
        // the GPS frame handler, so Ctrl+W read whatever happened to be cached and never
        // caused a read of its own. Forced past the poll interval because the pilot pressing
        // the key IS the reason to go and look; the answer lands for the next press, and this
        // press falls back to the live SimVar if the cache has gone stale.
        RequestActiveLegNames(force: true);

        var last = _wptSimConnect?.LastGpsWaypoint;
        if (last == null) return "Waypoint information not available yet.";
        return GpsWaypointSequencer.ComposeReadout(
            GpsWaypointSequencer.Read(last.Value, _wptLastNextId, FreshLegNext, FreshLegPrev),
            DistanceText);
    }

    /// <summary>Answers D — distance and time to the destination, from the standing GPS frame.</summary>
    private string ComposeDestinationReadout()
    {
        var last = _wptSimConnect?.LastGpsWaypoint;
        return last == null
            ? "Destination information not available yet."
            : GpsWaypointSequencer.ComposeDestination(last.Value, DistanceText);
    }

    /// <summary>Answers Shift+D — top of descent, from the G1000's own VNAV.</summary>
    private string ComposeTopOfDescentReadout(SimConnectManager simConnect)
    {
        // A leg index of -1 means the navigator has no top of descent; anything from 0 up is
        // the leg that carries it. See the definition above for why this is not a flag.
        double leg = simConnect.GetCachedVariableValue("DA40_VNAV_TOD_LEG") ?? -1;
        double avail = leg >= 0 ? 1 : 0;
        double tod = simConnect.GetCachedVariableValue("DA40_VNAV_TOD_DIST") ?? 0;
        return GpsWaypointSequencer.ComposeTopOfDescent(avail > 0.5, tod, DistanceText);
    }

    /// <summary>Distance in whatever the pilot set on the G1000, never a fixed unit.</summary>
    private string DistanceText(double nm)
    {
        if (DisplayUnitDistance == "metric")
        {
            double km = nm * 1.852;
            return km < 10 ? $"{km:0.0} kilometres" : $"{km:0} kilometres";
        }
        return nm < 10 ? $"{nm:0.0} miles" : $"{nm:0} miles";
    }
}
