using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Pause, active pause and slew: the "Pause_EX1" and "Sim" system events and the IS SLEW ACTIVE
/// simvar, read into <see cref="SimPauseSlewTracker"/> (which holds what was measured about them).
/// All three deliver only on a change, so they stay subscribed for the whole connection.
/// </summary>
public partial class SimConnectManager
{
    private readonly SimPauseSlewTracker _simState = new();

    /// <summary>A pause or slew change worth speaking, raised on the UI thread. Never raised for the
    /// state found at connect.</summary>
    public event EventHandler<SimStateChange>? SimStateChanged;

    /// <summary>
    /// Called from SetupEvents on every new connection, after OnRecvSimobjectData is attached: the
    /// slew request's first delivery is its current value, and one drained before the handler
    /// existed would leave the state unseeded, so the first real toggle would only seed it.
    /// </summary>
    private void SetupSimStateMonitoring()
    {
        _simState.Reset();
        var sc = simConnect!;
        try
        {
            sc.OnRecvEvent += SimConnect_OnRecvEvent;
            // "Sim" first, so the flight-running gate is known before the first pause flags arrive.
            sc.SubscribeToSystemEvent(SYSTEM_EVENT_ID.Sim, "Sim");
            sc.SubscribeToSystemEvent(SYSTEM_EVENT_ID.PauseEx1, "Pause_EX1");

            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_SLEW_STATE, "IS SLEW ACTIVE", "bool",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
            sc.RegisterDataDefineStruct<SingleValue>(DATA_DEFINITIONS.DEF_SLEW_STATE);
            sc.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_SLEW_STATE, DATA_DEFINITIONS.DEF_SLEW_STATE,
                SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Pause/slew monitoring setup failed (no pause or slew announcements this session): {ex.Message}");
        }
    }

    private void SimConnect_OnRecvEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT data)
    {
        switch ((SYSTEM_EVENT_ID)data.uEventID)
        {
            case SYSTEM_EVENT_ID.Sim:
                Log.Debug("SimConnect", $"Sim system event: {(data.dwData != 0 ? "running" : "stopped")}");
                _simState.OnSimRunning(data.dwData != 0);
                break;
            case SYSTEM_EVENT_ID.PauseEx1:
                // Raw flags logged: the FS2020 values are unmeasured (SimPauseSlewTracker).
                Log.Debug("SimConnect", $"Pause_EX1 flags: {data.dwData}");
                RaiseSimStateChanged(_simState.OnPauseFlags(data.dwData));
                break;
        }
    }

    private void ProcessSlewState(double value) => RaiseSimStateChanged(_simState.OnSlew(value != 0));

    private void RaiseSimStateChanged(SimStateChange change)
    {
        if (change != SimStateChange.None)
            SimStateChanged?.Invoke(this, change);
    }
}
