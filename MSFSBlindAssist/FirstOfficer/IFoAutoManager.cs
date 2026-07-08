namespace MSFSBlindAssist.FirstOfficer;

public interface IFoAutoManager
{
    bool AutoGearUpEnabled { get; set; }
    bool AutoGearDownEnabled { get; set; }
    bool AutoFlapsEnabled { get; set; }
    bool AutoApEnabled { get; set; }

    /// <summary>Height (ft AGL) at which AutoAp engages the autopilot on climbout (user setting, default 350).</summary>
    int AutoApEngageAltitudeAgl { get; set; }

    void Reset();
    void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts);
}
