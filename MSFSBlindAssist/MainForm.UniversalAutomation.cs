using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist;

/// <summary>
/// The wiring of the universal auto-gear/auto-AP service (<c>Automation/UniversalAutomationService.cs</c>):
/// its construction, its 1 Hz feed timer, the aircraft-position handler and the push of the current
/// aircraft's own limits. Kept in its own partial so <c>.claude/rules/first-officer-automation.md</c>
/// loads for it (FO-16, FO-17, FO-18, FO-21) instead of being mirrored into every MainForm partial.
/// </summary>
public partial class MainForm
{
    private MSFSBlindAssist.Automation.UniversalAutomationService universalAutomation = null!;
    private System.Windows.Forms.Timer _universalAutomationTimer = null!;
    // The latest FO_ALTITUDE_AGL and FO_GEAR_HANDLE answers (MainForm.Announcers.cs), NaN while not
    // read. They are read only while an auto feature is on, so they are dropped (NaN) whenever none
    // is: a value left from earlier in the flight once lowered the gear above its limit speed at
    // FL150 the moment auto gear was switched back on.
    private double _universalLatestAgl = double.NaN;
    private double _universalLatestGearHandle = double.NaN;

    /// <summary>
    /// Builds the universal automation service and starts its 1 Hz feed. <c>simConnectManager</c> must
    /// already exist: the service's delegates and the position subscription use it.
    /// <c>InitializeManagers</c> calls this after the <c>ContinuousBatchDelivered</c> subscription and
    /// before the <c>QueuedEventDispatched</c> one; <c>DisposeUniversalAutomation</c> stops the timer.
    /// </summary>
    private void InitializeUniversalAutomation()
    {
        // Universal (all-aircraft) auto-gear/AP service. Construct BEFORE the
        // AircraftPositionReceived subscription + feed timer below reference it.
        universalAutomation = new MSFSBlindAssist.Automation.UniversalAutomationService(
            eventName => simConnectManager.SendEvent(eventName),
            msg => announcer.AnnounceImmediate(msg),
            // AP engage routes through the current aircraft def: PMDG jets press their own
            // MCP switch (CMD A / A/P L), the FlyByWire family AP 1 (FO-21), everything else
            // falls back to stock AUTOPILOT_ON.
            () => currentAircraft?.EngageAutopilot(simConnectManager),
            // ...and is verified against the def's engaged-readback where it has one, so a
            // press the aircraft rejected is retried instead of being announced as success.
            () => currentAircraft?.IsAutopilotEngaged(simConnectManager),
            // The success confirmation ("350 feet. Autopilot engaged.") does NOT interrupt (FO-21): the
            // aircraft says its own "Autopilot 1 on" with the 1 Hz batch, and the confirmation follows 0-1 s
            // later, so an interrupting one cut it off. The gear calls and the two Captain calls above keep
            // AnnounceImmediate. Announce, unlike it, honours announcer.Suppressed (the first-detect grace).
            announceConfirmation: msg => announcer.Announce(msg),
            // The stock gear handle, so a gear the pilot already moved is left alone and unannounced.
            isGearHandleDown: () => double.IsNaN(_universalLatestGearHandle) ? null : _universalLatestGearHandle > 0.5);

        simConnectManager.AircraftPositionReceived += OnUniversalAircraftPosition;

        _universalAutomationTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _universalAutomationTimer.Tick += (_, _) =>
        {
            if (!simConnectManager.IsConnected) return;
            var s = SettingsManager.Current;
            universalAutomation.AutoGearUpEnabled       = s.FOAutoGearUpEnabled;
            universalAutomation.AutoGearDownEnabled     = s.FOAutoGearDownEnabled;
            universalAutomation.AutoApEnabled           = s.FOAutoApEnabled;
            universalAutomation.AutoApEngageAltitudeAgl = s.FOAutoApEngageAltitudeAgl;
            // The current aircraft's own engage floor, press spacing and wait flag (also pushed on a switch).
            PushUniversalAircraftLimits();
            if (!universalAutomation.AnyEnabled)
            {
                // Zero SimConnect load when disabled, and nothing kept that would be stale on re-enable.
                _universalLatestAgl = double.NaN;
                _universalLatestGearHandle = double.NaN;
                universalAutomation.MarkGroundStateUnknown();
                return;
            }
            simConnectManager.RequestAircraftPosition();
            simConnectManager.RequestFOAltitudeAGL();
            simConnectManager.RequestFOGearHandle();
        };
        _universalAutomationTimer.Start();
    }

    /// <summary>
    /// Stops and disposes the 1 Hz feed timer and drops the position subscription. Called from MainForm's
    /// shutdown, beside the other timers: the subscription once outlived the form, so a position answer
    /// during the exit's SimConnect drain still ran the service.
    /// </summary>
    private void DisposeUniversalAutomation()
    {
        _universalAutomationTimer?.Stop();
        _universalAutomationTimer?.Dispose();
        if (universalAutomation != null)
            simConnectManager.AircraftPositionReceived -= OnUniversalAircraftPosition;
    }

    /// <summary>
    /// Forgets the universal service's legs and its latest readings: an aircraft switch, or a flight loaded
    /// in the simulator. The takeoff gates then wait for a ground sample, and nothing acts on a reading from
    /// before (the AGL and gear handle are read again within a second).
    /// </summary>
    private void ResetUniversalAutomation()
    {
        universalAutomation?.Reset();
        _universalLatestAgl = double.NaN;
        _universalLatestGearHandle = double.NaN;
    }

    /// <summary>
    /// Pushes the CURRENT aircraft's own auto-engage limits to the universal automation: the engage floor
    /// (the 737 refuses CMD below 400 ft RA after takeoff, FO-17), the spacing between presses (the FlyByWire
    /// AP 1 push is a toggle) and whether a null engaged-readback waits for a definite reading instead of
    /// pressing blind (FO-21). Called by the 1 Hz universal timer and by <c>SwitchAircraft</c> right after it
    /// resets the service: an <c>Update</c> that ran between a switch and the next tick would otherwise act on
    /// the previous aircraft's values. The user-setting pushes (gear, AP enable, engage height) stay in the timer.
    /// </summary>
    private void PushUniversalAircraftLimits()
    {
        if (universalAutomation == null) return;   // InitializeUniversalAutomation has not run yet
        universalAutomation.MinimumApEngageAltitudeAgl = currentAircraft?.MinimumAutopilotEngageAltitudeAgl ?? 0;
        universalAutomation.ApRetryIntervalMs = currentAircraft?.AutopilotEngageRetryIntervalMs ?? 0;
        universalAutomation.WaitForApState = currentAircraft?.AutopilotEngageWaitsForState ?? false;
    }

    /// <summary>Universal auto-gear/AP feed — runs for every aircraft, independent of the
    /// First Officer window. Gated inside the service by the per-feature settings.</summary>
    private void OnUniversalAircraftPosition(object? sender, SimConnect.SimConnectManager.AircraftPosition pos)
    {
        universalAutomation.Update(pos.Altitude, pos.VerticalSpeedFPM, _universalLatestAgl, pos.SimOnGround >= 0.5);
    }
}
