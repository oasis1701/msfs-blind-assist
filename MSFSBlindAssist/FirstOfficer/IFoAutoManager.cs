namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Per-aircraft automatic FLAP management (gear and AP-engage moved to the universal
/// MSFSBlindAssist.Automation.UniversalAutomationService, 2026-07). Live only on the
/// A380/A32NX (their CheckFlaps schedule); a no-op elsewhere. Kept as a layer so the
/// A380/A32NX flap automation keeps its home.
/// </summary>
public interface IFoAutoManager
{
    bool AutoFlapsEnabled { get; set; }

    void Reset();
    void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround);
}
