namespace MSFSBlindAssist.FirstOfficer;

public interface IFoPhaseMonitor
{
    /// <summary>Gates ONLY the 10,000 ft landing-light switching (UserSettings
    /// .FOAutoLights10kEnabled). Transition-altitude baro pushes and the
    /// no-transition reminder are never gated by this. The crossing latch keeps
    /// tracking while disabled so re-enabling mid-flight can't fire a stale crossing.</summary>
    bool AutoLights10kEnabled { get; set; }

    /// <summary>Gates every altimeter write at the transition altitude and level (UserSettings
    /// .FOAutoAltimetersEnabled, FO-23). Off, the crossing is spoken as a reminder
    /// (<see cref="TransitionAltimeters"/>) and nothing else at the crossing is gated (the
    /// MD-11 still sets Dial-A-Flap). The crossing detector keeps tracking while disabled.</summary>
    bool AutoAltimetersEnabled { get; set; }

    /// <summary>Auto seat-belt-sign mode (Disabled / 10k / TOC-TOD). Actuated through the
    /// aircraft's own SetSeatbeltSign; the trigger logic lives in SeatbeltAutomation.</summary>
    FoSeatbeltMode AutoSeatbeltMode { get; set; }

    void SetThresholds(int transAltFt, int transLevelFt);
    void Reset();
    /// <summary>The SimBrief cruise altitude (feet), or null with no plan. The TOC/TOD
    /// seat-belt automation only calls a level-off "cruise" near it (SeatbeltAutomation).
    /// Survives <see cref="Reset"/>.</summary>
    void SetPlannedCruiseAltitude(int? cruiseFt);

    /// <summary>One position sample. <paramref name="onGround"/> re-arms the seat-belt
    /// automation for the next leg — the ONLY thing that does.</summary>
    void Update(double altitudeFt, double verticalSpeedFpm, bool onGround);
}
