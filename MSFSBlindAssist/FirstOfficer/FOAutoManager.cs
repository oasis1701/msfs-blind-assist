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

    public void Reset() { _centerPumps.Reset(); }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts)
    {
        if (!_executor.IsAvailable) return;

        var action = _centerPumps.Update(
            enabled:           SettingsManager.Current.FOAutoCenterPumpsEnabled,
            onGround:          altitudeAgl < 20,
            centerQtyLbs:      _state.FuelCenterLbs(),
            centerPumpsOn:     _state.IsEitherCenterPumpOn(),
            centerLowPressRaw: _state.IsAnyCenterLowPress(),
            wingPumpsOn:       _state.AreWingFuelPumpsOn());

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
