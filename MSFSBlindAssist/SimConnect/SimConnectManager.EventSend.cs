using System.Collections.Concurrent;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

public partial class SimConnectManager
{

    public void SetLVar(string varName, double value)
    {
        if (!IsConnected || simConnect == null) return;

        // GLOBAL WRITE ROUTING: prefer the MobiFlight calculator path for real L:vars.
        // The data-definition write below (AddToDataDefinition + SetDataOnSimObject) is UNRELIABLE for
        // many add-on L:vars (FlyByWire L:vars in particular silently revert a frame later) -- which is
        // why dozens of A380/A32NX controls had to be hand-routed through the calc path one prefix at a
        // time in each aircraft def's HandleUIVariableSet catch-all. Routing every L:var write that
        // reaches this fallback through the calc path fixes them all globally. Guardrails:
        //   * Only when the calc path is PROVEN alive end-to-end (CalcPathVerified — the nonce
        //     round-trip probe). IsMobiFlightConnected is NOT sufficient: it is true even when no
        //     WASM module is installed (purely local setup), and routing on it sent every L:var
        //     write into a dead client-data area for no-module users. Until/unless verification
        //     succeeds, fall through to the data-def write — the exact legacy (main) behavior,
        //     which works for Fenix and degrades to main's known imperfection for FBW for the
        //     few seconds before the probe verifies.
        //   * Only for TRUE L:vars: a name with a space or colon is a stock-SimVar shape
        //     (e.g. "TRANSPONDER STATE:1", "INTERACTIVE POINT OPEN:0") and must NOT be written as (>L:..).
        //     SetLVar always prepends "L:" to varName, so a real caller never passes such a name here.
        if (CalcPathVerified
            && !string.IsNullOrEmpty(varName)
            && varName.IndexOf(' ') < 0
            && varName.IndexOf(':') < 0)
        {
            // Fixed-point format: the default double formatting emits scientific notation
            // for small/large magnitudes ("1E-05"), which the MSFS RPN parser rejects.
            ExecuteCalculatorCode(value.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture)
                + " (>L:" + varName + ")");
            return;
        }

        // For setting LVars, we'll need to use a workaround
        // Create a temporary data definition for this specific LVar
        // Use thread-safe counter to generate unique IDs (fixes crash from ID collision)
        var tempDefId = (DATA_DEFINITIONS)System.Threading.Interlocked.Increment(ref nextTempDefId);

        try
        {
            simConnect.AddToDataDefinition(tempDefId, $"L:{varName}", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

            simConnect.SetDataOnSimObject(tempDefId,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, value);

            SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error setting LVar {varName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes RPN calculator code via the MobiFlight WASM module.
    /// Useful for atomic read-modify-write operations on LVars.
    /// Example: "(L:E_FCU_EFIS1_BARO) 5 + (>L:E_FCU_EFIS1_BARO)"
    /// </summary>
    /// <param name="quiet">
    /// Pass true only from an identified high-rate (per-frame timer) caller -- e.g. the A380
    /// seat-motor/slider-ramp ticks -- to skip the per-command debug log line. Default false
    /// preserves existing logging for every other caller.
    /// </param>
    public void ExecuteCalculatorCode(string rpnCode, bool quiet = false)
    {
        if (!IsConnected || mobiFlightWasm == null) return;

        try
        {
            mobiFlightWasm.SendMFCommand($"MF.SimVars.Set.{rpnCode}", quiet);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error executing calculator code: {ex.Message}");
        }
    }

    public void SetSimVar(string varName, double value, string units = "number")
    {
        if (!IsConnected || simConnect == null) return;

        Log.Debug("SimConnect", $"Setting SimVar: {varName} = {value} ({units})");

        // Create a temporary data definition for this specific SimVar
        // Use thread-safe counter to generate unique IDs (fixes crash from ID collision)
        var tempDefId = (DATA_DEFINITIONS)System.Threading.Interlocked.Increment(ref nextTempDefId);

        try
        {
            simConnect.AddToDataDefinition(tempDefId, varName, units,
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

            simConnect.SetDataOnSimObject(tempDefId,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, value);

            SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);

            Log.Debug("SimConnect", $"Successfully set SimVar {varName} to {value}");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error setting SimVar {varName}: {ex.Message}");
        }
    }   

    public void SendEvent(string eventName, uint data = 0)
    {
        if (!IsConnected || simConnect == null) return;

        Log.Debug("SimConnect", $"Sending event: {eventName} with data: {data}");

        // Two FlyByWire event classes prefer the MobiFlight calculator path:
        //   1. "H:" gauge/HTML events (e.g. H:A380X_EFIS_CP_BARO_PUSH_1) — these have NO
        //      TransmitClientEvent transport AT ALL (main never sent H: via SendEvent; its
        //      SendHVar was MobiFlight-only too), so they always go to the MobiFlight
        //      channel, queued during the brief connect window.
        //   2. Dotted custom input events (e.g. A32NX.FCU_HDG_SET, A32NX.FCU_AP_1_PUSH) —
        //      the calc path is preferred once VERIFIED end-to-end, but these DO have a
        //      legacy transport: MapClientEventToSimEvent + TransmitClientEvent is the
        //      shipping path for the A32NX FCU on main (the FBW WASM registers the custom
        //      client events with the sim). So: verified → calc; probe still running →
        //      queue (flushed on the probe's conclusion); concluded-unverified (module
        //      absent, or a non-FBW aircraft that can't probe) → legacy transmit below.
        if (eventName.StartsWith("H:", StringComparison.Ordinal))
        {
            if (mobiFlightWasm == null) return; // no transport exists for H: without the module object
            if (IsMobiFlightConnected)
                FireCalcEvent(eventName, data);
            else
                lock (pendingCalcEvents)
                {
                    if (pendingCalcEvents.Count < MaxPendingCalcEvents)
                        pendingCalcEvents.Enqueue((eventName, data));
                }
            return;
        }
        if (eventName.Contains('.') && mobiFlightWasm != null)
        {
            if (CalcPathVerified)
            {
                FireCalcEvent(eventName, data);
                return;
            }
            if (!CalcPathProbeConcluded)
            {
                // Probe still in flight (post-aircraft-load window): don't pick a loser yet.
                // Queue; MarkCalcPathVerified flushes via calc, MarkCalcPathProbeConcluded
                // flushes via the legacy transmit fallback in FlushPendingCalcEvents.
                lock (pendingCalcEvents)
                {
                    if (pendingCalcEvents.Count < MaxPendingCalcEvents)
                        pendingCalcEvents.Enqueue((eventName, data));
                }
                return;
            }
            // Probe concluded without verification — fall through to the legacy
            // MapClientEventToSimEvent + TransmitClientEvent path below.
        }

        // Map the event name to an ID if not already mapped
        if (!eventIds.ContainsKey(eventName))
        {
            uint eventId = nextEventId++;
            eventIds[eventName] = eventId;
            simConnect.MapClientEventToSimEvent((EVENTS)eventId, eventName);
            Log.Debug("SimConnect", $"Registered new event: {eventName} with ID: {eventId}");
        }
        
        // Send the event with the data parameter
        simConnect.TransmitClientEvent(SIMCONNECT_OBJECT_ID_USER,
            (EVENTS)eventIds[eventName], data, GROUP_PRIORITY.HIGHEST,
            SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    // Fire a calculator-path event via the MobiFlight bridge. H: events are momentary (no param);
    // dotted custom events take the data param. Callers route here once the verdict/connection
    // gates have been applied (the H: flush may fire during the brief connect window —
    // ExecuteCalculatorCode drops safely if the module object is gone).
    private void FireCalcEvent(string eventName, uint data)
    {
        if (eventName.StartsWith("H:", StringComparison.Ordinal))
            ExecuteCalculatorCode($"(>{eventName})");
        else
            ExecuteCalculatorCode($"{data} (>K:{eventName})");
    }

    // Flush events queued while the calc-path verdict was pending. Called from
    // MarkCalcPathVerified (flush via calc) and MarkCalcPathProbeConcluded (flush
    // dotted events via the legacy TransmitClientEvent transport; H: events go to
    // the MobiFlight channel regardless — they have no other transport).
    private void FlushPendingCalcEvents()
    {
        (string eventName, uint data)[] toFire;
        lock (pendingCalcEvents)
        {
            if (pendingCalcEvents.Count == 0) return;
            toFire = pendingCalcEvents.ToArray();
            pendingCalcEvents.Clear();
        }
        foreach (var e in toFire)
        {
            if (CalcPathVerified || e.eventName.StartsWith("H:", StringComparison.Ordinal))
                FireCalcEvent(e.eventName, e.data);
            else
                SendEvent(e.eventName, e.data); // re-enters; CalcPathProbeConcluded routes it to TransmitClientEvent
        }
    }

    // Release any H: events queued during the MobiFlight connect window without
    // disturbing queued dotted events (which wait for the probe's verdict).
    private void FlushPendingHEvents()
    {
        List<(string eventName, uint data)> hEvents = new();
        lock (pendingCalcEvents)
        {
            if (pendingCalcEvents.Count == 0) return;
            var keep = new Queue<(string eventName, uint data)>();
            while (pendingCalcEvents.Count > 0)
            {
                var e = pendingCalcEvents.Dequeue();
                if (e.eventName.StartsWith("H:", StringComparison.Ordinal)) hEvents.Add(e);
                else keep.Enqueue(e);
            }
            while (keep.Count > 0) pendingCalcEvents.Enqueue(keep.Dequeue());
        }
        foreach (var e in hEvents) FireCalcEvent(e.eventName, e.data);
    }

    public void SendHVar(string hvar)
    {
        Log.Debug("SimConnect", $"Attempting to send H-variable: {hvar}");
        Log.Debug("SimConnect", $"MobiFlight Status: {MobiFlightStatus}");
        Log.Debug("SimConnect", $"Can Send H-Vars: {CanSendHVars}");

        if (mobiFlightWasm?.CanSendHVars == true)
        {
            mobiFlightWasm.SendHVar(hvar);
            Log.Debug("SimConnect", $"Successfully sent H-variable: {hvar}");
        }
        else
        {
            Log.Debug("SimConnect", $"❌ Cannot send H-variable - MobiFlight not ready: {hvar}");
            Log.Debug("SimConnect", $"MobiFlight module null: {mobiFlightWasm == null}");
            if (mobiFlightWasm != null)
            {
                Log.Debug("SimConnect", $"IsConnected: {mobiFlightWasm.IsConnected}");
                Log.Debug("SimConnect", $"IsRegistered: {mobiFlightWasm.IsRegistered}");
                Log.Debug("SimConnect", $"CanSendHVars: {mobiFlightWasm.CanSendHVars}");
            }
        }
    }

    public void SendButtonPressRelease(string pressEvent, string releaseEvent, int delayMs = 200)
    {
        if (string.IsNullOrEmpty(pressEvent) || string.IsNullOrEmpty(releaseEvent))
        {
            Log.Debug("SimConnect", "Invalid press/release events");
            return;
        }

        // Send press event
        SendHVar(pressEvent);

        // Set up timer for release event
        var releaseTimer = new System.Windows.Forms.Timer();
        releaseTimer.Interval = delayMs;
        releaseTimer.Tick += (sender, e) =>
        {
            releaseTimer.Stop();
            releaseTimer.Dispose();
            SendHVar(releaseEvent);
            Log.Debug("SimConnect", $"Button press/release completed: {pressEvent} -> {releaseEvent}");
        };
        releaseTimer.Start();
    }

    public void AddLedVariable(string ledVariable)
    {
        if (mobiFlightWasm != null && !string.IsNullOrEmpty(ledVariable))
        {
            // Use default MobiFlight channel to add L-variable for reading
            mobiFlightWasm.AddDefaultChannelLVar(ledVariable);
            Log.Debug("SimConnect", $"Added LED variable for monitoring via default channel: {ledVariable}");
        }
    }

    public void RequestLedVariableUpdate()
    {
        // This triggers an update of all registered L-variables in the default channel
        // MobiFlight will automatically send updates when any L-variable changes
        if (mobiFlightWasm != null)
        {
            Log.Debug("SimConnect", "LED variable updates will be received automatically from MobiFlight");
        }
    }

    public void ReadLedVariable(string ledVariable)
    {
        if (mobiFlightWasm != null && !string.IsNullOrEmpty(ledVariable))
        {
            mobiFlightWasm.ReadLedVariable(ledVariable);
            Log.Debug("SimConnect", $"Reading LED variable: {ledVariable}");
        }
    }

    public void SendPMDGEvent(string eventName, uint eventId, int? parameter = null)
    {
        pmdgDataManager?.SendEvent(eventName, eventId, parameter);
    }

    public async Task SendPMDGGuardedSet(string guardEventName, uint guardEventId,
                                          string switchEventName, uint switchEventId,
                                          int targetPosition)
    {
        if (pmdgDataManager != null)
            await pmdgDataManager.SendGuardedSet(guardEventName, guardEventId, switchEventName, switchEventId, targetPosition);
    }

    /// <summary>
    /// TransmitClientEvent dispatch for absolute-position selectors (3+ detents) whose
    /// CDA selector handler does not accept the target position directly. PMDG accepts
    /// the absolute target position via the standard SimConnect event path.
    /// </summary>
    public void SendPMDGEventViaTransmitWithTarget(uint eventId, uint targetPosition)
    {
        pmdgDataManager?.SendEventViaTransmitWithTarget(eventId, targetPosition);
    }

    /// <summary>
    /// Walks an NG3 switch to a target position via mouse-click TransmitClientEvents
    /// (TFM convention). PMDG NG3 handles guard physics transparently — no explicit
    /// guard manipulation needed.
    /// </summary>
    public async Task WalkPMDGSelector(uint eventId, int currentPosition, int targetPosition)
    {
        if (pmdgDataManager != null)
            await pmdgDataManager.WalkSelectorViaClicks(eventId, currentPosition, targetPosition);
    }

    /// <summary>
    /// Closed-loop click-walk for NG3 detented rotaries that ignore both the CDA
    /// position write and transmit-with-target (currently the transponder mode
    /// selector, EVT_TCAS_MODE). Awaits a FRESH Data-CDA snapshot before every
    /// re-read (the ambient poll is 1 Hz — too stale to steer clicks) so PMDG's
    /// probabilistically-dropped detent clicks self-correct without overshoot;
    /// click direction is inverted vs <see cref="WalkPMDGSelector"/>'s TFM
    /// convention. Returns the VERIFIED landed position (== target on success,
    /// elsewhere on budget exhaustion), or null when unverified — non-NG3
    /// manager, not ready, or snapshot timeout. See
    /// <see cref="PMDGNG3DataManager.WalkSelectorClosedLoop"/> for the probe history.
    /// </summary>
    public async Task<int?> WalkPMDGSelectorClosedLoop(uint eventId, string fieldName, int targetPosition)
    {
        if (pmdgDataManager is PMDGNG3DataManager ng3)
            return await ng3.WalkSelectorClosedLoop(eventId, fieldName, targetPosition);
        return null;
    }

    /// <summary>
    /// Sends a press-and-release dispatch pair for a momentary spring-loaded
    /// toggle. Used by the PMDG 737 NG3 for GRD POWER, GEN, and APU GEN
    /// switches — bare clicks without RELEASE play the switch sound but the
    /// state springs back. See <see cref="IPMDGDataManager.SendMomentaryToggle"/>.
    /// </summary>
    public async Task SendPMDGMomentaryToggle(uint eventId, int targetPosition)
    {
        if (pmdgDataManager != null)
            await pmdgDataManager.SendMomentaryToggle(eventId, targetPosition);
    }
}
