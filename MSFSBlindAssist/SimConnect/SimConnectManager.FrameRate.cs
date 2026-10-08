using System.Diagnostics;
using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Services.SimPerformance;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// The SimConnect "Frame" system event, which the sim raises once per rendered frame with its own
/// frame rate and simulation rate. It is subscribed only while a consumer (the Sim Performance
/// window) asks for it: every frame costs a message through ReceiveMessage on the UI thread, so an
/// always-on subscription would tax the app for a window nobody has open. The subscription survives
/// a reconnect while a consumer holds it (SetupEvents re-arms it) and is released on disconnect.
/// </summary>
public partial class SimConnectManager
{
    /// <summary>Frame readings accumulated since the last read; fed only while monitoring is on.</summary>
    public FrameRateMeter FrameRateMeter { get; } = new();

    private int _frameRateConsumers;
    private bool _frameEventSubscribed;

    /// <summary>Ask for frame events. Paired with <see cref="StopFrameRateMonitoring"/>; UI thread only.</summary>
    public void StartFrameRateMonitoring()
    {
        _frameRateConsumers++;
        if (_frameRateConsumers != 1) return;
        FrameRateMeter.Reset();
        SubscribeFrameEvent();
    }

    /// <summary>Release a frame-event request; the subscription ends with the last consumer.</summary>
    public void StopFrameRateMonitoring()
    {
        if (_frameRateConsumers == 0) return;
        _frameRateConsumers--;
        if (_frameRateConsumers == 0) UnsubscribeFrameEvent();
    }

    private void SubscribeFrameEvent()
    {
        if (simConnect == null || _frameEventSubscribed) return;
        try
        {
            simConnect.SubscribeToSystemEvent(SYSTEM_EVENT_ID.Frame, "Frame");
            _frameEventSubscribed = true;
            Log.Debug("SimConnect", "Frame system event subscribed");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Frame system event subscribe failed: {ex.Message}");
        }
    }

    private void UnsubscribeFrameEvent()
    {
        if (!_frameEventSubscribed) return;
        _frameEventSubscribed = false;
        if (simConnect == null) return;
        try
        {
            simConnect.UnsubscribeFromSystemEvent(SYSTEM_EVENT_ID.Frame);
            Log.Debug("SimConnect", "Frame system event released");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Frame system event unsubscribe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called from SetupEvents on every new connection handle: a window still holding a request
    /// after a reconnect gets its frames back without reopening.
    /// </summary>
    private void RearmFrameEventForNewConnection()
    {
        _frameEventSubscribed = false;
        if (_frameRateConsumers > 0) SubscribeFrameEvent();
    }

    private void SimConnect_OnRecvEventFrame(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT_FRAME data)
    {
        if ((SYSTEM_EVENT_ID)data.uEventID != SYSTEM_EVENT_ID.Frame) return;
        FrameRateMeter.Add(data.fFrameRate, data.fSimSpeed, Stopwatch.GetTimestamp());
    }
}
