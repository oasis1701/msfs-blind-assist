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

    event EventHandler<PMDGVarUpdateEventArgs>? VariableChanged;

    void Initialize(
        Microsoft.FlightSimulator.SimConnect.SimConnect simConnect,
        MobiFlightWasmModule mobiFlightWasm);

    void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data);

    double GetFieldValue(string fieldName);

    void SendEvent(string eventName, uint eventId, int? parameter);

    Task SendGuardedToggle(
        string guardEventName, uint guardEventId,
        string switchEventName, uint switchEventId);

    /// <summary>
    /// Walks a multi-position selector from currentPosition to targetPosition by sending
    /// click-flag events on the switch event. ClkL (LEFTSINGLE = increment) or ClkR
    /// (RIGHTSINGLE = decrement) is sent abs(delta) times with a short gap between clicks.
    /// Reads current position from outside (caller responsibility) to avoid races.
    /// </summary>
    Task SendSelectorStepwise(string eventName, uint eventId, int currentPosition, int targetPosition);

    /// <summary>
    /// Guarded variant of SendSelectorStepwise: opens the guard, walks the selector via clicks,
    /// then closes the guard. Use for guarded multi-position selectors like ELEC_StandbyPowerSelector.
    /// </summary>
    Task SendGuardedSelectorStepwise(
        string guardEventName, uint guardEventId,
        string switchEventName, uint switchEventId,
        int currentPosition, int targetPosition);

    void RequestCDUScreen(int cdu);

    string[]? GetCDURows(int cdu);

    (string[] rows, byte[,] colors, byte[,] flags)? GetCDURowsWithColors(int cdu);
}
