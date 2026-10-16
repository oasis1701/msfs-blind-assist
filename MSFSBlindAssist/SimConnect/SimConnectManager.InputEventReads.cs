using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// READING a B: input event: the value of a <see cref="SimVarType.InputEvent"/> variable.
///
/// Some MSFS 2024 cockpit switches drive nothing a stock SimVar reports. On the stock Cessna 172
/// G1000 the avionics bus switches and the standby battery switch are input events whose effect
/// shows in no SimVar (measured 2026-10-16: `AVIONICS MASTER SWITCH:n` stays 1 whatever the
/// switches say, `ELECTRICAL MASTER BATTERY:1` stays 1 whatever the standby switch says). A data
/// definition cannot carry a B: name either: "B:ENGINE_MAGNETO_1" registered as an L:var or a
/// SimVar read 0 with the magneto on Left. The only source is the input event itself.
///
/// So a variable of that type is SUBSCRIBED by hash once the catalog is known, and read once
/// (GetInputEvent) for its current value, because a subscription delivers changes only, never the
/// value standing when it starts (measured: nothing arrived on subscribe; each change arrived
/// twice, the repeat dropped by <see cref="IsValueChange"/>). Deliveries go through the same
/// cache and <see cref="SimVarUpdated"/> as any other variable, so panels, the cache and
/// announcements treat the variable like a SimVar. The hashes are per aircraft load, so the
/// subscriptions are rebuilt whenever the catalog is re-enumerated or the variables re-registered.
/// </summary>
public partial class SimConnectManager
{
    /// <summary>First of the request ids a one-off input-event read goes out under (710-799).</summary>
    internal const uint InputEventReadRequestFirst = 710;

    /// <summary>How many ids the input-event reads rotate over.</summary>
    internal const uint InputEventReadRequestCount = 90;

    // hash -> the variable keys reading that input event (two keys may read one event).
    private readonly Dictionary<ulong, List<string>> _inputEventSubscriptions = new();

    // request id -> the variable key a GetInputEvent answers.
    private readonly Dictionary<uint, string> _inputEventReadRequests = new();
    private uint _nextInputEventReadRequest = InputEventReadRequestFirst;

    /// <summary>
    /// True for a variable read from its B: input event: <see cref="SimVarType.InputEvent"/> and not
    /// write-only. Such a variable gets no data definition and never rides the continuous batch.
    /// Pure so the suite can pin it.
    /// </summary>
    internal static bool IsInputEventRead(SimVarDefinition def) =>
        def.Type == SimVarType.InputEvent && def.UpdateFrequency != UpdateFrequency.Never;

    /// <summary>The next read request id in the rotating 710-799 range. Pure so the suite can pin the wrap.</summary>
    internal static uint NextInputEventReadRequest(uint current) =>
        current + 1 >= InputEventReadRequestFirst + InputEventReadRequestCount ? InputEventReadRequestFirst : current + 1;

    /// <summary>The number a delivery carries, or false for a STRING input event or no value.</summary>
    private static bool TryInputEventValue(SIMCONNECT_INPUT_EVENT_TYPE type, object[]? value, out double result)
    {
        result = 0;
        return type == SIMCONNECT_INPUT_EVENT_TYPE.DOUBLE && TryInputEventNumber(value, out result);
    }

    /// <summary>
    /// The first element of a DOUBLE delivery's value array as a number (measured: a boxed double in
    /// <c>Value[0]</c>), or false for an empty array or anything else. Pure so the suite can pin it.
    /// </summary>
    internal static bool TryInputEventNumber(object[]? value, out double result)
    {
        result = 0;
        if (value == null || value.Length == 0) return false;
        switch (value[0])
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Subscribes every input-event variable of the current aircraft and reads each one once.
    /// Called when the catalog enumeration completes and after a re-registration; a variable whose
    /// name is not in the catalog (the catalog not in yet, or another aircraft) is skipped and
    /// picked up by the next call.
    /// </summary>
    private void SubscribeAircraftInputEvents()
    {
        UnsubscribeAircraftInputEvents();
        var sc = simConnect;
        if (!IsConnected || sc == null || CurrentAircraft == null || inputEventHashes.Count == 0) return;

        int subscribed = 0;
        foreach (var (key, def) in CurrentAircraft.GetVariables())
        {
            if (!IsInputEventRead(def)) continue;
            if (!inputEventHashes.TryGetValue(def.Name, out ulong hash))
            {
                Log.Debug("SimConnect", $"Input event {def.Name} ({key}) is not in this aircraft's catalog");
                continue;
            }
            if (!_inputEventSubscriptions.TryGetValue(hash, out var keys))
            {
                keys = new List<string>();
                _inputEventSubscriptions[hash] = keys;
                try { sc.SubscribeInputEvent(hash); subscribed++; }
                catch (Exception ex) { Log.Debug("SimConnect", $"SubscribeInputEvent {def.Name} failed: {ex.Message}"); }
            }
            keys.Add(key);
            ReadInputEvent(key, hash);
        }
        if (subscribed > 0) Log.Debug("SimConnect", $"Subscribed {subscribed} input events for reading");
    }

    private void UnsubscribeAircraftInputEvents()
    {
        var sc = simConnect;
        if (sc != null)
        {
            foreach (ulong hash in _inputEventSubscriptions.Keys)
            {
                try { sc.UnsubscribeInputEvent(hash); } catch { /* the handle may already be gone */ }
            }
        }
        _inputEventSubscriptions.Clear();
        _inputEventReadRequests.Clear();
    }

    /// <summary>One GetInputEvent for <paramref name="varKey"/>, answered in <see cref="SimConnect_OnRecvGetInputEvent"/>.</summary>
    private void ReadInputEvent(string varKey, ulong hash)
    {
        var sc = simConnect;
        if (sc == null) return;
        uint id = _nextInputEventReadRequest;
        _nextInputEventReadRequest = NextInputEventReadRequest(id);
        _inputEventReadRequests[id] = varKey;
        try { sc.GetInputEvent((DATA_REQUESTS)id, hash); }
        catch (Exception ex)
        {
            _inputEventReadRequests.Remove(id);
            Log.Debug("SimConnect", $"GetInputEvent for {varKey} failed: {ex.Message}");
        }
    }

    /// <summary>True when <paramref name="varKey"/> is an input-event variable this aircraft's catalog can answer.</summary>
    private bool IsReadableInputEvent(string varKey)
    {
        var def = DefinitionOf(varKey);
        return def != null && IsInputEventRead(def) && inputEventHashes.ContainsKey(def.Name);
    }

    /// <summary>
    /// <see cref="RequestVariable(string, bool)"/>'s branch for an input-event variable: a one-off
    /// read in place of a data request. False when <paramref name="varKey"/> is not one, so the
    /// caller carries on; true (handled) when it is, subscribed or not yet.
    /// </summary>
    private bool TryRequestInputEventVariable(string varKey, bool forceUpdate)
    {
        var def = DefinitionOf(varKey);
        if (def == null || !IsInputEventRead(def)) return false;
        if (!inputEventHashes.TryGetValue(def.Name, out ulong hash)) return true;
        if (forceUpdate)
        {
            lock (forceUpdateVariables) { forceUpdateVariables.Add(varKey); }
        }
        ReadInputEvent(varKey, hash);
        return true;
    }

    private void SimConnect_OnRecvSubscribeInputEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender,
        SIMCONNECT_RECV_SUBSCRIBE_INPUT_EVENT data)
    {
        try
        {
            if (!_inputEventSubscriptions.TryGetValue(data.Hash, out var keys)) return;
            if (!TryInputEventValue(data.eType, data.Value, out double value)) return;
            foreach (string key in keys.ToArray()) DeliverInputEventValue(key, value, answersRequest: false);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Input event subscription handler error: {ex.Message}");
        }
    }

    private void SimConnect_OnRecvGetInputEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender,
        SIMCONNECT_RECV_GET_INPUT_EVENT data)
    {
        try
        {
            if (!_inputEventReadRequests.Remove(data.dwRequestID, out string? key)) return;
            if (!TryInputEventValue(data.eType, data.Value, out double value)) return;
            DeliverInputEventValue(key, value, answersRequest: true);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Input event read handler error: {ex.Message}");
        }
    }

    /// <summary>
    /// Cache the value and raise <see cref="SimVarUpdated"/> by the individual-response path's
    /// rules: a subscription repeat that did not change is dropped (every change arrives twice);
    /// a read's answer fires unless the variable is announced and continuous and unchanged, so a
    /// panel opening never re-speaks a standing state, and a forced read always fires.
    /// </summary>
    private void DeliverInputEventValue(string varKey, double value, bool answersRequest)
    {
        var def = DefinitionOf(varKey);
        if (def == null) return;

        bool forced;
        lock (forceUpdateVariables) { forced = forceUpdateVariables.Remove(varKey); }

        double? previous = lastVariableValues.TryGetValue(varKey, out double cached) ? cached : null;
        bool changed = IsValueChange(previous, value, def);
        lastVariableValues[varKey] = value;
        _freshReads.Complete(varKey, value);

        if (!changed && !forced &&
            (!answersRequest || (def.IsAnnounced && def.UpdateFrequency == UpdateFrequency.Continuous)))
            return;

        if (changed) Log.Debug("SimConnect", $"Input event {def.Name} ({varKey}) = {value}");
        SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
        {
            VarName = varKey,
            Value = value,
            Description = FormatVariableValue(varKey, def, value)
        });
    }
}
