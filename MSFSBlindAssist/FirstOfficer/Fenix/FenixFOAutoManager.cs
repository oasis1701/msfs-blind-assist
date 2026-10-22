using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// Fenix A320 flap-management placeholder. Gear and AP-engage moved to the universal
/// UniversalAutomationService (2026-07). The Fenix does publish its takeoff V-speeds now
/// (N_MISC_PERF_TO_*), but it has no flap schedule: FO-15 limits auto-flaps to the FBW jets
/// and the A330, so AutoFlapsEnabled is stored but never acted on.
/// </summary>
public sealed class FenixFOAutoManager : IFoAutoManager
{
    private readonly FenixActionExecutor _executor;
    private readonly FenixStateEvaluator _state;
    private readonly ScreenReaderAnnouncer _announcer;

    public bool AutoFlapsEnabled { get; set; }

    public FenixFOAutoManager(
        FenixActionExecutor executor,
        FenixStateEvaluator state,
        ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _state = state;
        _announcer = announcer;
    }

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround)
    {
        // No Fenix flap schedule; gear/AP now handled by UniversalAutomationService.
    }
}
