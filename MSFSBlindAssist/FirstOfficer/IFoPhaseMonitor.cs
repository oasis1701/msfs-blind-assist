namespace MSFSBlindAssist.FirstOfficer;

public interface IFoPhaseMonitor
{
    /// <summary>Gates ONLY the 10,000 ft landing-light switching (UserSettings
    /// .FOAutoLights10kEnabled). Transition-altitude baro pushes and the
    /// no-transition reminder are never gated by this. The crossing latch keeps
    /// tracking while disabled so re-enabling mid-flight can't fire a stale crossing.</summary>
    bool AutoLights10kEnabled { get; set; }

    void SetThresholds(int transAltFt, int transLevelFt);
    void Reset();
    void Update(double altitudeFt, double verticalSpeedFpm);
}
