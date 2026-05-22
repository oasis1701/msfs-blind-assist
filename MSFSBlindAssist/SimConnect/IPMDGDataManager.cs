using Microsoft.FlightSimulator.SimConnect;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Abstraction over PMDG aircraft data managers (777X, NG3, …).
/// Implemented by PMDG777DataManager; will also be implemented by PMDGNG3DataManager.
/// </summary>
public interface IPMDGDataManager : IDisposable
{
    /// <summary>Aircraft code, e.g. "PMDG_777" or "PMDG_737".</summary>
    string AircraftCode { get; }

    /// <summary>Number of CDU sides: 3 for 777X (L/C/R), 2 for NG3 (L/R).</summary>
    int CDUSideCount { get; }

    /// <summary>
    /// True once the data manager has received at least one CDA snapshot. Until this
    /// is true, GetFieldValue returns 0.0 for any field — callers must not interpret
    /// that as a real position-0 reading.
    /// </summary>
    bool IsReady { get; }

    event EventHandler<PMDGVarUpdateEventArgs>? VariableChanged;

    void Initialize(
        Microsoft.FlightSimulator.SimConnect.SimConnect simConnect,
        MobiFlightWasmModule mobiFlightWasm);

    void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data);

    double GetFieldValue(string fieldName);

    void SendEvent(string eventName, uint eventId, int? parameter);

    /// <summary>
    /// Sends a PMDG event through the standard SimConnect TransmitClientEvent path
    /// with the event registered under the alias "#&lt;eventId&gt;". Used for absolute-position
    /// selectors (3+ detents) where PMDG's CDA selector handler does not accept the
    /// direct target position. The 777 emergency-exit lights selector uses this path —
    /// see PMDG777Definition LTS_EmerLights case and the e1682ae commit history.
    /// </summary>
    void SendEventViaTransmitWithTarget(uint eventId, uint targetPosition);

    /// <summary>
    /// Guarded set: opens the guard (param=1), writes the switch position (param=targetPosition),
    /// closes the guard (param=0). Used for two-position guarded toggles AND for multi-position
    /// guarded selectors where the underlying switch event accepts a direct target position via CDA.
    /// Delays between sub-steps so PMDG's frame loop sees each transition.
    /// </summary>
    Task SendGuardedSet(
        string guardEventName, uint guardEventId,
        string switchEventName, uint switchEventId,
        int targetPosition);

    void RequestCDUScreen(int cdu);

    string[]? GetCDURows(int cdu);

    (string[] rows, byte[,] colors, byte[,] flags)? GetCDURowsWithColors(int cdu);
}
