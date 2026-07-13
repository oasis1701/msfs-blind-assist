using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// PMDG 737 automatic flap management placeholder. Gear and AP-engage (including the
/// LNAV/VNAV push) moved to the universal UniversalAutomationService (2026-07); the 737
/// has no auto-flap schedule, so AutoFlapsEnabled is stored but never acted on. Retained
/// for IFoAutoManager symmetry.
/// </summary>
public class FOAutoManager : IFoAutoManager
{
    private readonly AircraftActionExecutor _executor;
    private readonly AircraftStateEvaluator _state;
    private readonly ScreenReaderAnnouncer  _announcer;

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

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts)
    {
        // No 737 flap schedule; gear/AP now handled by UniversalAutomationService.
    }
}
