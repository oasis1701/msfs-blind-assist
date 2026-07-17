using System.Diagnostics;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// PMDG 777 per-aircraft FO automation. Gear and AP-engage moved to the universal
/// UniversalAutomationService (2026-07); the 777 has no auto-flap schedule, so
/// AutoFlapsEnabled is stored but never acted on. <see cref="Update"/> drives center-tank
/// fuel pump automation via the shared <see cref="CenterFuelPumpAutomation"/> policy,
/// gated on <c>SettingsManager.Current.FOAutoCenterPumpsEnabled</c>.
/// </summary>
public class FOAutoManager : IFoAutoManager
{
    private readonly AircraftActionExecutor _executor;
    private readonly AircraftStateEvaluator _state;
    private readonly ScreenReaderAnnouncer  _announcer;
    private readonly CenterFuelPumpAutomation _centerPumps = new();

    // Wall-clock elapsed-time measurement for the center-pump policy's wall-clock windows —
    // Update() is driven by AircraftPositionReceived, a variable-rate feed (~1-2.7 Hz), not
    // a fixed per-frame tick.
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastCenterPumpsMs;
    private bool   _centerPumpsClockPrimed;

    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on (PMDG auto-flaps removed 2026-07-08)

    public FOAutoManager(
        AircraftActionExecutor executor,
        AircraftStateEvaluator state,
        ScreenReaderAnnouncer  announcer)
    {
        _executor  = executor;
        _state     = state;
        _announcer = announcer;
    }

    public void Reset()
    {
        _centerPumps.Reset();
        _centerPumpsClockPrimed = false;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts)
    {
        if (!_executor.IsAvailable) return;

        double now = _clock.Elapsed.TotalMilliseconds;
        double elapsedMs = _centerPumpsClockPrimed ? now - _lastCenterPumpsMs : 0;
        _lastCenterPumpsMs = now;
        _centerPumpsClockPrimed = true;

        var action = _centerPumps.Update(
            enabled:       SettingsManager.Current.FOAutoCenterPumpsEnabled,
            dataReady:     _state.IsDataReady,
            onGround:      altitudeAgl < 20,
            centerQtyLbs:  _state.FuelCenterLbs(),
            centerPumpsOn: _state.IsEitherCenterPumpOn(),
            centerTankDry: _state.IsCenterTankDry(),
            systemCredible:_state.IsFuelSystemCredible(),
            wingPumpsOn:   _state.AreWingFuelPumpsOn(),
            rawElapsedMs:  elapsedMs);

        switch (action)
        {
            case CenterFuelPumpAutomation.Action.TurnOn:
                _executor.SetCenterFuelPumps(1);
                _announcer.AnnounceImmediate("Center fuel pumps on.");
                break;
            case CenterFuelPumpAutomation.Action.TurnOff:
                _executor.SetCenterFuelPumps(0);
                _announcer.AnnounceImmediate("Center tank low. Center fuel pumps off.");
                break;
        }
    }
}
