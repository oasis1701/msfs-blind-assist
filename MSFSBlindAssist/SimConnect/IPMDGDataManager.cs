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
        MobiFlightWasmModule? mobiFlightWasm);

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
    /// Used by the 777; NG3 manager prefers <see cref="WalkSelectorViaClicks"/> per TFM convention.
    /// </summary>
    Task SendGuardedSet(
        string guardEventName, uint guardEventId,
        string switchEventName, uint switchEventId,
        int targetPosition);

    /// <summary>
    /// Walks a switch from currentPosition to targetPosition by sending abs(delta)
    /// mouse-click TransmitClientEvent dispatches on the switch event:
    ///   ClkR (RIGHTSINGLE = 0x80000000) when currentPosition &gt; targetPosition
    ///   ClkL (LEFTSINGLE  = 0x20000000) when currentPosition &lt; targetPosition
    /// PMDG NG3 handles guarded switches transparently via clicks — no explicit
    /// guard open/close is needed. This matches the working TFM convention
    /// (PMDG737Aircraft.CalculateSwitchPosition with useClicks=true) and replaces
    /// the broken CDA-with-direct-position dispatch for the NG3, where the CDA
    /// handler ignored absolute parameter values for guarded switches and the
    /// extra guard-close write caused the switch to spring back to its prior detent.
    /// </summary>
    Task WalkSelectorViaClicks(uint eventId, int currentPosition, int targetPosition);

    /// <summary>
    /// Sends a press-and-release dispatch pair for a momentary spring-loaded
    /// toggle switch. PMDG NG3's GRD POWER and (APU) generator switches are
    /// modeled as momentary press-to-toggle controls: a bare LEFTSINGLE /
    /// RIGHTSINGLE click without its matching RELEASE flag plays the switch
    /// sound but never commits the state — the switch springs back to its
    /// prior value. Pattern source: PMDG_NG3_ConnectionTest.cpp
    /// `toggleFlightDirector` (LEFTSINGLE + LEFTRELEASE). target=1 routes
    /// through LEFT (up/ON); target=0 routes through RIGHT (down/OFF).
    /// </summary>
    Task SendMomentaryToggle(uint eventId, int targetPosition);

    void RequestCDUScreen(int cdu);

    string[]? GetCDURows(int cdu);

    (string[] rows, byte[,] colors, byte[,] flags)? GetCDURowsWithColors(int cdu);
}
