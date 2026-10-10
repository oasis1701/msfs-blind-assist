using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist;

/// <summary>
/// The wiring of the universal auto-gear/auto-AP service (<c>Automation/UniversalAutomationService.cs</c>):
/// its construction, its 1 Hz feed timer, the aircraft-position handler and the push of the current
/// aircraft's own limits. Kept in its own partial so <c>.claude/rules/first-officer.md</c> loads for it
/// (FO-16, FO-17, FO-18, FO-21) instead of being mirrored into every MainForm partial.
/// </summary>
public partial class MainForm
{
    private MSFSBlindAssist.Automation.UniversalAutomationService universalAutomation = null!;
    private System.Windows.Forms.Timer _universalAutomationTimer = null!;
    private double _universalLatestAgl;

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
            () => currentAircraft?.IsAutopilotEngaged(simConnectManager));

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
            if (!universalAutomation.AnyEnabled) return;   // zero SimConnect load when disabled
            simConnectManager.RequestAircraftPosition();
            simConnectManager.RequestFOAltitudeAGL();
        };
        _universalAutomationTimer.Start();
    }

    /// <summary>
    /// Stops and disposes the 1 Hz feed timer. Called from MainForm's shutdown, beside the other timers.
    /// </summary>
    private void DisposeUniversalAutomation()
    {
        _universalAutomationTimer?.Stop();
        _universalAutomationTimer?.Dispose();
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
        universalAutomation.Update(pos.Altitude, pos.VerticalSpeedFPM, _universalLatestAgl);
    }
}
